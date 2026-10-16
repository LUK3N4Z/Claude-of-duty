using System;
using System.Collections.Generic;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FPS.AI
{
    public enum AwarenessState
    {
        Unaware,
        /// <summary>Something caught the eye or ear: turn and look.</summary>
        Suspicious,
        /// <summary>A threat is known to be out there (heard, told by the squad, under fire) but not identified.</summary>
        Alerted,
        /// <summary>A target has been positively identified and is still remembered.</summary>
        Engaged,
    }

    /// <summary>
    /// Sight and hearing for one soldier. Sight accumulates awareness per target from how much of the body is in line
    /// of sight, how lit it is (sun, open sky, flashlights — dark corners hide), stance and motion, distance and whether
    /// it is in focus or peripheral vision. Hearing picks up footsteps (scaled by the target's speed and stance) and
    /// gunshots (suppressed weapons are heard closer and located worse); unsuppressed muzzle flashes give the shooter
    /// away. Smoke (<see cref="VisionObstruction"/>) thins or cuts sight lines. Near misses, hits and stun grenades feed
    /// suppression, blindness and deafness.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AIPerceptionSensor : MonoBehaviour
    {
        private const int MaxTracked = 4;

        public struct TargetKnowledge
        {
            public PerceivableTarget Target;
            /// <summary>0-1; 1 means positively identified.</summary>
            public float Awareness;
            public bool Identified;
            /// <summary>Identified and in line of sight right now.</summary>
            public bool Visible;
            public float VisibleFraction;
            public Vector3 LastKnownPosition;
            public Vector3 LastKnownVelocity;
            public float LastSeenTime;
            /// <summary>Last time any sense (or the squad) updated the position.</summary>
            public float LastInfoTime;
        }

        [SerializeField] private Transform eye;
        [Tooltip("Own team; targets of other teams are threats.")]
        [SerializeField] private int team = 1;

        [Header("Sight")]
        [SerializeField, Min(1f)] private float viewDistance = 90f;
        [Tooltip("Half-angle of peripheral vision (deg).")]
        [SerializeField, Range(10f, 110f)] private float peripheralHalfAngle = 80f;
        [Tooltip("Half-angle of focused vision (deg).")]
        [SerializeField, Range(5f, 60f)] private float focusHalfAngle = 30f;
        [SerializeField, Range(0f, 1f)] private float peripheralRate = 0.35f;
        [Tooltip("Awareness per second for a fully visible, lit, standing target at point-blank range.")]
        [SerializeField, Min(0.01f)] private float detectionRate = 2.5f;
        [Tooltip("Share of the view distance left in total darkness.")]
        [SerializeField, Range(0.01f, 1f)] private float darkRange = 0.12f;
        [Tooltip("Light level of open sky without direct sun.")]
        [SerializeField, Range(0f, 1f)] private float skyAmbient = 0.35f;
        [Tooltip("Within this distance anything in view is spotted almost at once (m).")]
        [SerializeField, Min(0f)] private float pointBlank = 4f;

        [Header("Hearing")]
        [SerializeField, Min(0f)] private float hearingScale = 1f;
        [Tooltip("Gunshot localisation error as a fraction of distance; suppressed shots use the larger value.")]
        [SerializeField, Range(0f, 1f)] private float gunshotLocateError = 0.08f;
        [SerializeField, Range(0f, 1f)] private float suppressedLocateError = 0.3f;
        [Tooltip("Awareness hearing alone can reach: sound never identifies a target.")]
        [SerializeField, Range(0f, 1f)] private float hearingCap = 0.85f;

        [Header("Memory")]
        [SerializeField, Min(0f)] private float awarenessDecay = 0.12f;
        [Tooltip("How long an identified target stays engaged after the last information (s).")]
        [SerializeField, Min(1f)] private float memoryTime = 25f;
        [SerializeField, Range(0.02f, 0.5f)] private float tickInterval = 0.1f;

        [Header("Stress")]
        [SerializeField, Range(0f, 1f)] private float suppressionGain = 0.45f;
        [Tooltip("Time for full suppression to wear off (s).")]
        [SerializeField, Min(0.1f)] private float suppressionRecovery = 4f;
        [Tooltip("Seconds of blindness per unit of flash exposure before it fades.")]
        [SerializeField, Min(0f)] private float blindHold = 3f;
        [SerializeField, Min(0.1f)] private float blindFade = 2.5f;
        [SerializeField, Min(0.1f)] private float deafRecovery = 6f;

        private readonly TargetKnowledge[] tracked = new TargetKnowledge[MaxTracked];
        private int trackedCount;
        private int primary = -1;
        private float lastTick, blindHoldLeft;
        private TacticalHealthAndArmorSystem health;

        /// <summary>Raised when a target is positively identified (not again until it has been forgotten).</summary>
        public event Action<PerceivableTarget> TargetIdentified;

        public Transform Eye => eye;
        public int Team => team;
        public float Blindness { get; private set; }
        public float Deafness { get; private set; }
        /// <summary>Incoming fire stress, 0-1.</summary>
        public float Suppression { get; private set; }
        public bool IsStunned => Blindness > 0.5f;

        public PerceivableTarget Target => primary >= 0 ? tracked[primary].Target : null;
        public float Awareness => primary >= 0 ? tracked[primary].Awareness : 0f;
        public bool TargetIdentifiedNow => primary >= 0 && tracked[primary].Identified;
        public bool TargetVisible => primary >= 0 && tracked[primary].Visible;
        public Vector3 LastKnownPosition => primary >= 0 ? tracked[primary].LastKnownPosition : transform.position;
        public Vector3 LastKnownVelocity => primary >= 0 ? tracked[primary].LastKnownVelocity : Vector3.zero;
        public float TimeSinceSeen => primary >= 0 ? Time.time - tracked[primary].LastSeenTime : float.PositiveInfinity;
        public float TimeSinceInfo => primary >= 0 ? Time.time - tracked[primary].LastInfoTime : float.PositiveInfinity;

        public AwarenessState State
        {
            get
            {
                if (primary < 0) return AwarenessState.Unaware;
                TargetKnowledge k = tracked[primary];
                if (k.Identified) return AwarenessState.Engaged;
                if (k.Awareness >= 0.6f) return AwarenessState.Alerted;
                return k.Awareness >= 0.2f ? AwarenessState.Suspicious : AwarenessState.Unaware;
            }
        }

        public void Configure(Transform eyeTransform, int teamIndex)
        {
            eye = eyeTransform;
            team = teamIndex;
        }

        /// <summary>Squad information: a teammate's sighting or estimate. Alerts, and identifies when they saw it.</summary>
        public void ShareKnowledge(PerceivableTarget target, Vector3 position, float infoTime, bool identified)
        {
            int slot = Track(target);
            if (slot < 0) return;

            ref TargetKnowledge k = ref tracked[slot];
            if (infoTime > k.LastInfoTime)
            {
                k.LastKnownPosition = position;
                k.LastInfoTime = infoTime;
                if (identified) k.LastSeenTime = Mathf.Max(k.LastSeenTime, infoTime); // a teammate's eyes
            }

            k.Awareness = Mathf.Max(k.Awareness, identified ? 1f : 0.7f);
            if (identified) Identify(ref k);
            SelectPrimary();
        }

        /// <summary>Knowledge about <paramref name="target"/>, if tracked.</summary>
        public bool TryGetKnowledge(PerceivableTarget target, out TargetKnowledge knowledge)
        {
            for (int i = 0; i < trackedCount; i++)
            {
                if (tracked[i].Target != target) continue;
                knowledge = tracked[i];
                return true;
            }

            knowledge = default;
            return false;
        }

        private void Awake()
        {
            if (eye == null) eye = transform;
            TryGetComponent(out health);
        }

        private void OnEnable()
        {
            lastTick = Time.time + Random.Range(0f, tickInterval); // stagger squads across frames
            NearMissListeners.Register(eye, transform);
            EventBus<GunshotEvent>.Subscribe(OnGunshot);
            EventBus<SuppressionEvent>.Subscribe(OnSuppressed);
            EventBus<DamageEvent>.Subscribe(OnDamaged);
            EventBus<FlashbangEvent>.Subscribe(OnFlashbang);
        }

        private void OnDisable()
        {
            NearMissListeners.Unregister(eye);
            EventBus<GunshotEvent>.Unsubscribe(OnGunshot);
            EventBus<SuppressionEvent>.Unsubscribe(OnSuppressed);
            EventBus<DamageEvent>.Unsubscribe(OnDamaged);
            EventBus<FlashbangEvent>.Unsubscribe(OnFlashbang);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Suppression = Mathf.Max(0f, Suppression - dt / suppressionRecovery);
            Deafness = Mathf.Max(0f, Deafness - dt / deafRecovery);
            if (blindHoldLeft > 0f) blindHoldLeft -= dt;
            else Blindness = Mathf.Max(0f, Blindness - dt / blindFade);

            if (Time.time < lastTick + tickInterval) return;
            float tickDt = Time.time - lastTick;
            lastTick = Time.time;
            Tick(Mathf.Min(tickDt, 0.5f));
        }

        private void Tick(float dt)
        {
            IReadOnlyList<PerceivableTarget> targets = PerceivableTarget.All;
            for (int i = 0; i < targets.Count; i++)
            {
                PerceivableTarget target = targets[i];
                if (target.Team == team || !target.IsAlive || target.transform == transform) continue;
                Sense(target, dt);
            }

            for (int i = trackedCount - 1; i >= 0; i--)
            {
                if (tracked[i].Target == null || !tracked[i].Target.IsAlive) Forget(i);
            }

            SelectPrimary();
        }

        private void Sense(PerceivableTarget target, float dt)
        {
            Vector3 eyePosition = eye.position;
            Vector3 toTarget = target.AimPoint - eyePosition;
            float distance = toTarget.magnitude;
            int slot = IndexOf(target);

            float fraction = 0f;
            float sightGain = Blindness < 0.5f ? SightGain(target, toTarget, distance, slot, out fraction) : 0f;
            float hearingGain = HearingGain(target, distance);
            if (sightGain <= 0f && hearingGain <= 0f)
            {
                if (slot >= 0) Fade(slot, dt);
                return;
            }

            if (slot < 0) slot = Track(target);
            if (slot < 0) return;

            ref TargetKnowledge k = ref tracked[slot];
            float now = Time.time;
            if (sightGain > 0f)
            {
                k.Awareness = Mathf.Min(1f, k.Awareness + sightGain * dt);
                if (k.Awareness >= 1f)
                {
                    Identify(ref k);
                    k.Visible = true;
                    k.VisibleFraction = fraction;
                    k.LastKnownPosition = target.AimPoint;
                    k.LastKnownVelocity = target.Velocity;
                    k.LastSeenTime = now;
                    k.LastInfoTime = now;
                    return;
                }
            }

            k.Visible = false;
            if (hearingGain > 0f)
            {
                if (!k.Identified) k.Awareness = Mathf.Max(k.Awareness, Mathf.Min(hearingCap, k.Awareness + hearingGain * dt));
                k.LastKnownPosition = Estimate(target.AimPoint, distance * 0.1f);
                k.LastInfoTime = now;
            }
            else if (sightGain <= 0f)
            {
                Fade(slot, dt);
            }
        }

        /// <summary>Awareness per second from sight, 0 when the target cannot be seen.</summary>
        private float SightGain(PerceivableTarget target, Vector3 toTarget, float distance, int slot, out float fraction)
        {
            fraction = 0f;
            if (distance > viewDistance * 1.5f || distance < 1e-3f) return 0f;

            bool engaged = slot >= 0 && tracked[slot].Identified;
            float angle = Vector3.Angle(eye.forward, toTarget);
            if (angle > (engaged ? peripheralHalfAngle + 20f : peripheralHalfAngle)) return 0f;

            TacticalDevice targetLight = target.LitLight;
            float light = AIIllumination.Sample(target.AimPoint, skyAmbient);
            float rangeScale = 1f;
            if (targetLight != null)
            {
                // A lit light gives its carrier away; one shining straight at the eye is visible from far off.
                Transform beam = targetLight.Flashlight.transform;
                bool facingUs = Vector3.Angle(beam.forward, eye.position - beam.position) < targetLight.Flashlight.spotAngle * 0.5f;
                light = Mathf.Max(light, facingUs ? 1f : 0.6f);
                if (facingUs) rangeScale = 1.5f;
            }

            float effectiveRange = viewDistance * Mathf.Lerp(darkRange, 1f, light) * rangeScale;
            if (distance > effectiveRange) return 0f;

            fraction = VisibleFraction(target);
            if (fraction <= 0f) return 0f;

            float proximity = 1f - distance / effectiveRange;
            float distanceFactor = 0.15f + 0.85f * proximity * proximity;
            if (distance < pointBlank) distanceFactor *= 3f;
            float vision = angle <= focusHalfAngle ? 1f : peripheralRate;
            return detectionRate * fraction * target.Conspicuity * vision * distanceFactor * Mathf.Lerp(0.3f, 1f, light);
        }

        private float VisibleFraction(PerceivableTarget target)
        {
            Vector3 from = eye.position;
            return (Sight(from, target.Head.position) + Sight(from, target.Chest.position) + Sight(from, target.Pelvis.position)) / 3f;
        }

        /// <summary>How much of a point shows: 0 behind cover or deep in smoke, the smoke's transmittance otherwise.</summary>
        private static float Sight(Vector3 from, Vector3 to)
        {
            if (Physics.Linecast(from, to, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return 0f;
            float transmittance = VisionObstruction.Transmittance(from, to);
            return transmittance > 0.15f ? transmittance : 0f;
        }

        /// <summary>Awareness per second from footsteps.</summary>
        private float HearingGain(PerceivableTarget target, float distance)
        {
            float radius = target.NoiseRadius * hearingScale * (1f - Deafness);
            if (radius <= 0f || distance > radius) return 0f;
            if (Physics.Linecast(eye.position, target.Pelvis.position, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                radius *= 0.6f; // walls muffle footsteps
                if (distance > radius) return 0f;
            }

            return 1.5f * (1f - distance / radius);
        }

        private void OnGunshot(in GunshotEvent shot)
        {
            if (Deafness > 0.9f) return;
            PerceivableTarget target = PerceivableTarget.Find(shot.Shooter);
            if (target == null || target.Team == team || target.transform == transform) return;

            Vector3 eyePosition = eye.position;
            float distance = Vector3.Distance(eyePosition, shot.Position);
            WeaponAudioProfileSO profile = shot.Profile;
            bool occluded = Physics.Linecast(eyePosition, shot.Position, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            float audible = (profile != null ? profile.MaxAudibleDistance : 500f) * hearingScale * (1f - Deafness) * (occluded ? 0.5f : 1f);
            bool flashSeen = !occluded && shot.MuzzleFlash > 0.05f && Blindness < 0.5f && distance <= viewDistance * 1.5f &&
                             Vector3.Angle(eye.forward, shot.Position - eyePosition) <= peripheralHalfAngle &&
                             VisionObstruction.Transmittance(eyePosition, shot.Position) > 0.3f; // a flash glows through thin smoke
            if (distance > audible && !flashSeen) return;

            int slot = Track(target);
            if (slot < 0) return;

            ref TargetKnowledge k = ref tracked[slot];
            float now = Time.time;
            if (flashSeen)
            {
                // The flash pins the shooter even when nothing else of them shows.
                k.Awareness = 1f;
                Identify(ref k);
                k.LastKnownPosition = target.AimPoint;
                k.LastSeenTime = now;
            }
            else
            {
                bool suppressed = profile != null && profile.Loudness < 0.5f;
                if (!k.Identified) k.Awareness = Mathf.Max(k.Awareness, 0.75f);
                k.LastKnownPosition = Estimate(target.AimPoint, distance * (suppressed ? suppressedLocateError : gunshotLocateError));
            }

            k.LastInfoTime = now;
            SelectPrimary();
        }

        private void OnSuppressed(in SuppressionEvent evt)
        {
            if (evt.Listener != eye) return;
            Suppression = Mathf.Clamp01(Suppression + evt.Intensity * suppressionGain);
            InferShooter(evt.Point, evt.Direction, 0.8f);
        }

        private void OnDamaged(in DamageEvent evt)
        {
            if (health == null || evt.Target != health) return;
            Suppression = Mathf.Clamp01(Suppression + 0.4f);
            InferShooter(evt.Point, evt.Direction, 0.9f);
        }

        private void OnFlashbang(in FlashbangEvent bang)
        {
            float blind = FlashbangEvent.Exposure(bang.Position, bang.Intensity, eye.position, eye.forward, out float deaf);
            Deafness = Mathf.Max(Deafness, deaf);
            if (blind <= Blindness) return;
            Blindness = blind;
            blindHoldLeft = blind * blindHold;
        }

        /// <summary>
        /// Knocked senseless for about <paramref name="seconds"/> (a bashed door slammed into him): fully blinded and stunned,
        /// then recovering like after a flashbang.
        /// </summary>
        public void Stun(float seconds)
        {
            Blindness = 1f;
            blindHoldLeft = Mathf.Max(blindHoldLeft, seconds);
        }

        /// <summary>Something came in along <paramref name="direction"/>: alert toward the enemy behind that line.</summary>
        private void InferShooter(Vector3 point, Vector3 direction, float awareness)
        {
            PerceivableTarget best = null;
            float bestAlignment = 0.9f;
            IReadOnlyList<PerceivableTarget> targets = PerceivableTarget.All;
            for (int i = 0; i < targets.Count; i++)
            {
                PerceivableTarget candidate = targets[i];
                if (candidate.Team == team || !candidate.IsAlive) continue;
                Vector3 toCandidate = candidate.AimPoint - point;
                float alignment = Vector3.Dot(toCandidate.normalized, -direction);
                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    best = candidate;
                }
            }

            if (best == null) return;
            int slot = Track(best);
            if (slot < 0) return;

            ref TargetKnowledge k = ref tracked[slot];
            if (!k.Identified)
            {
                k.Awareness = Mathf.Max(k.Awareness, awareness);
                float distance = Vector3.Distance(eye.position, best.AimPoint);
                k.LastKnownPosition = Estimate(best.AimPoint, distance * 0.2f);
                k.LastInfoTime = Time.time;
            }

            SelectPrimary();
        }

        private void Identify(ref TargetKnowledge k)
        {
            if (k.Identified) return;
            k.Identified = true;
            TargetIdentified?.Invoke(k.Target);
        }

        private void Fade(int slot, float dt)
        {
            ref TargetKnowledge k = ref tracked[slot];
            k.Visible = false;
            if (k.Identified)
            {
                if (Time.time - k.LastInfoTime < memoryTime) return;
                k.Identified = false;
                k.Awareness = 0.6f; // lost, still searching
                return;
            }

            k.Awareness = Mathf.Max(0f, k.Awareness - awarenessDecay * dt);
            if (k.Awareness <= 0f) Forget(slot);
        }

        private static Vector3 Estimate(Vector3 position, float error)
        {
            Vector2 offset = Random.insideUnitCircle * error;
            return position + new Vector3(offset.x, 0f, offset.y);
        }

        private int IndexOf(PerceivableTarget target)
        {
            for (int i = 0; i < trackedCount; i++)
            {
                if (tracked[i].Target == target) return i;
            }

            return -1;
        }

        private int Track(PerceivableTarget target)
        {
            int slot = IndexOf(target);
            if (slot >= 0) return slot;
            if (trackedCount >= MaxTracked) return -1;

            tracked[trackedCount] = new TargetKnowledge
            {
                Target = target,
                LastKnownPosition = target.AimPoint,
                LastSeenTime = float.NegativeInfinity,
                LastInfoTime = float.NegativeInfinity,
            };
            return trackedCount++;
        }

        private void Forget(int slot)
        {
            trackedCount--;
            tracked[slot] = tracked[trackedCount];
            tracked[trackedCount] = default;
            SelectPrimary();
        }

        private void SelectPrimary()
        {
            primary = -1;
            float best = float.NegativeInfinity;
            for (int i = 0; i < trackedCount; i++)
            {
                TargetKnowledge k = tracked[i];
                float score = (k.Identified ? 2f : 0f) + (k.Visible ? 1f : 0f) + k.Awareness;
                if (score <= best) continue;
                best = score;
                primary = i;
            }
        }
    }
}
