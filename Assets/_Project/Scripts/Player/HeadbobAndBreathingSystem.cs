using System;
using FPS.Core;
using UnityEngine;

namespace FPS.Locomotion
{
    /// <summary>
    /// Physiology and head motion: heart rate driven by exertion and suppression, a respiratory cycle producing
    /// figure-8 optic sway, breath holding that steadies the sight at the cost of stamina, and gait-synchronised
    /// headbob with landing, slide and dive response. Owns HeadMotion; exposes <see cref="WeaponSwayEuler"/> for
    /// the weapon/optic systems.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RealisticLocomotionController))]
    public sealed class HeadbobAndBreathingSystem : MonoBehaviour
    {
        [Serializable]
        public struct BobShape
        {
            [Tooltip("Vertical head travel (m).")] public float vertical;
            [Tooltip("Side-to-side head travel (m).")] public float lateral;
            [Tooltip("Head roll (deg).")] public float roll;

            public BobShape(float vertical, float lateral, float roll)
            {
                this.vertical = vertical;
                this.lateral = lateral;
                this.roll = roll;
            }

            public static BobShape Lerp(BobShape a, BobShape b, float t) => new BobShape(
                Mathf.Lerp(a.vertical, b.vertical, t), Mathf.Lerp(a.lateral, b.lateral, t), Mathf.Lerp(a.roll, b.roll, t));
        }

        private const float TwoPi = Mathf.PI * 2f;
        private const float SpringStep = 0.01f;

        [Header("Heart Rate (BPM)")]
        [SerializeField] private float restingHeartRate = 65f;
        [SerializeField] private float maxHeartRate = 185f;
        [SerializeField] private float walkHeartRate = 82f;
        [SerializeField] private float combatPaceHeartRate = 98f;
        [SerializeField] private float sprintHeartRate = 142f;
        [SerializeField] private float tacticalSprintHeartRate = 172f;
        [SerializeField] private float slideHeartRate = 125f;
        [Tooltip("Time constant while the heart rate climbs (s).")]
        [SerializeField] private float heartRateRiseTime = 3.5f;
        [Tooltip("Time constant while it recovers (s); respiratory recovery is slower than exertion.")]
        [SerializeField] private float heartRateRecoveryTime = 14f;
        [Tooltip("Extra BPM per unit of suppression intensity.")]
        [SerializeField] private float suppressionHeartRate = 30f;
        [SerializeField] private float maxStressHeartRate = 45f;
        [SerializeField] private float stressDecayTime = 6f;

        [Header("Respiration (breaths/min)")]
        [SerializeField] private float restingBreathRate = 12f;
        [SerializeField] private float maxBreathRate = 42f;

        [Header("Optic Sway (deg)")]
        [SerializeField] private float restingSway = 0.18f;
        [SerializeField] private float maxSway = 1.4f;
        [Tooltip("Slow muscular drift layered on the breathing figure-8.")]
        [SerializeField] private float wanderSway = 0.12f;
        [Tooltip("Pitch kick of each heartbeat, visible mostly while holding breath.")]
        [SerializeField] private float heartbeatSway = 0.025f;
        [SerializeField, Range(0f, 1f)] private float crouchSwayMultiplier = 0.75f;
        [SerializeField, Range(0f, 1f)] private float proneSwayMultiplier = 0.45f;
        [Tooltip("Share of the weapon sway also applied to the camera.")]
        [SerializeField, Range(0f, 1f)] private float cameraSwayFraction = 0.15f;

        [Header("Hold Breath")]
        [SerializeField] private float maxHoldSeconds = 7f;
        [Tooltip("Hold time at maximum heart rate (s).")]
        [SerializeField] private float minHoldSeconds = 2.5f;
        [SerializeField, Range(0f, 1f)] private float holdSwayMultiplier = 0.12f;
        [SerializeField] private float holdBlendTime = 0.3f;
        [Tooltip("Time to refill an empty breath reserve (s).")]
        [SerializeField] private float breathRecoveryTime = 4f;
        [SerializeField] private float gaspDuration = 1.6f;
        [SerializeField] private float gaspSwayMultiplier = 1.8f;
        [SerializeField] private float gaspHeartRateKick = 10f;
        [Tooltip("Stamina fraction spent per second of held breath.")]
        [SerializeField] private float holdStaminaDrain = 0.05f;

        [Header("Headbob")]
        [SerializeField] private BobShape slowWalkBob = new BobShape(0.010f, 0.006f, 0.35f);
        [SerializeField] private BobShape combatPaceBob = new BobShape(0.020f, 0.012f, 0.6f);
        [SerializeField] private BobShape sprintBob = new BobShape(0.032f, 0.020f, 1.0f);
        [SerializeField] private BobShape tacticalSprintBob = new BobShape(0.042f, 0.026f, 1.4f);
        [SerializeField, Range(0f, 1f)] private float crouchBobMultiplier = 0.7f;
        [SerializeField, Range(0f, 1f)] private float proneBobMultiplier = 0.5f;
        [SerializeField, Range(0f, 1f)] private float aimBobMultiplier = 0.35f;
        [Tooltip("Head nod per metre of vertical bob (deg/m).")]
        [SerializeField] private float bobPitchPerMeter = 25f;
        [SerializeField] private float bobBlendRate = 6f;

        [Header("Landing")]
        [SerializeField] private float landingDipPerMps = 0.012f;
        [SerializeField] private float maxLandingDip = 0.12f;
        [Tooltip("Natural frequency of the critically damped landing spring (rad/s).")]
        [SerializeField] private float landingSpringFrequency = 12f;

        [Header("Slide & Dive")]
        [SerializeField] private float slideLowering = 0.12f;
        [SerializeField] private float slideRoll = 5f;
        [SerializeField] private float divePitch = 6f;
        [SerializeField] private float postureBlendTime = 0.15f;

        private RealisticLocomotionController locomotion;
        private StanceAndLeanController stance;
        private PlayerRig rig;
        private InputReader input;

        private float heartRate, stress;
        private float breathPhase, heartbeatPhase;
        private float breathReserve = 1f, holdBlend, gaspTimer;
        private bool holding, holdLatched;
        private BobShape bob;
        private float bobWeight;
        private float landingOffset, landingVelocity;
        private float slideBlend, diveBlend;

        public float HeartRateBpm => heartRate;
        /// <summary>Heart rate normalised between resting (0) and maximum (1).</summary>
        public float HeartRate01 { get; private set; }
        public float BreathReserve01 => breathReserve;
        public bool IsHoldingBreath => holding;
        /// <summary>Forced recovery after running out of breath: no holding, amplified sway.</summary>
        public bool IsGasping => gaspTimer > 0f;
        /// <summary>
        /// Optic sway for the weapon/sight in degrees (x = pitch, y = yaw), with stance, mount and breath-hold
        /// already applied. The camera receives only <c>cameraSwayFraction</c> of it.
        /// </summary>
        public Vector2 WeaponSwayEuler { get; private set; }

        private void Awake()
        {
            locomotion = GetComponent<RealisticLocomotionController>();
            stance = GetComponent<StanceAndLeanController>();
            rig = GetComponent<PlayerRig>();
            input = rig.Input;
            heartRate = restingHeartRate;
        }

        private void OnEnable()
        {
            if (input == null)
            {
                enabled = false; // locomotion already reports the missing InputReader
                return;
            }

            EventBus<SuppressionEvent>.Subscribe(OnSuppressed);
            locomotion.Landed += OnLanded;
        }

        private void OnDisable()
        {
            EventBus<SuppressionEvent>.Unsubscribe(OnSuppressed);
            locomotion.Landed -= OnLanded;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateHeartRate(dt);
            UpdateBreathHold(dt);
            UpdateSway();
            ApplyHeadMotion(dt);
        }

        private void UpdateHeartRate(float dt)
        {
            stress *= Mathf.Exp(-dt / stressDecayTime);
            float target = Mathf.Min(maxHeartRate, ExertionHeartRate() + stress);
            float timeConstant = target > heartRate ? heartRateRiseTime : heartRateRecoveryTime;
            heartRate += (target - heartRate) * (1f - Mathf.Exp(-dt / timeConstant));
            HeartRate01 = Mathf.InverseLerp(restingHeartRate, maxHeartRate, heartRate);

            // Holding breath freezes the respiratory cycle; the heart keeps beating.
            if (!holding)
            {
                float breathsPerMinute = Mathf.Lerp(restingBreathRate, maxBreathRate, HeartRate01);
                breathPhase = Mathf.Repeat(breathPhase + breathsPerMinute / 60f * TwoPi * dt, TwoPi);
            }

            heartbeatPhase = Mathf.Repeat(heartbeatPhase + heartRate / 60f * dt, 1f);
        }

        private void UpdateBreathHold(float dt)
        {
            if (!input.HoldBreathHeld) holdLatched = false; // after a gasp the key must be released first
            if (gaspTimer > 0f) gaspTimer -= dt;

            holding = input.HoldBreathHeld && locomotion.IsAiming && !holdLatched && gaspTimer <= 0f && breathReserve > 0f;
            if (holding)
            {
                breathReserve -= dt / Mathf.Lerp(maxHoldSeconds, minHoldSeconds, HeartRate01);
                locomotion.DrainStamina(holdStaminaDrain * dt);
                if (breathReserve <= 0f)
                {
                    breathReserve = 0f;
                    holding = false;
                    holdLatched = true;
                    gaspTimer = gaspDuration;
                    stress = Mathf.Min(maxStressHeartRate, stress + gaspHeartRateKick);
                }
            }
            else if (gaspTimer <= 0f)
            {
                breathReserve = Mathf.Min(1f, breathReserve + dt / breathRecoveryTime);
            }

            holdBlend = Mathf.MoveTowards(holdBlend, holding ? 1f : 0f, dt / holdBlendTime);
        }

        private void UpdateSway()
        {
            float steadiness = StanceSwayMultiplier() * stance.SwayMultiplier * Mathf.Lerp(1f, holdSwayMultiplier, holdBlend);
            float amplitude = Mathf.Lerp(restingSway, maxSway, HeartRate01) * steadiness;
            if (IsGasping) amplitude *= gaspSwayMultiplier;

            // 1:2 Lissajous = the classic figure-8 of a breathing shooter.
            float yawSway = amplitude * Mathf.Sin(breathPhase);
            float pitchSway = amplitude * 0.5f * Mathf.Sin(2f * breathPhase);

            float wander = wanderSway * (0.5f + HeartRate01) * steadiness;
            float t = Time.time * 0.35f;
            yawSway += wander * (Mathf.PerlinNoise(t, 0.17f) * 2f - 1f);
            pitchSway += wander * (Mathf.PerlinNoise(0.53f, t) * 2f - 1f);

            pitchSway -= heartbeatSway * (0.5f + HeartRate01) * stance.SwayMultiplier * Mathf.Exp(-heartbeatPhase * 12f);
            WeaponSwayEuler = new Vector2(pitchSway, yawSway);
        }

        private void ApplyHeadMotion(float dt)
        {
            bob = BobShape.Lerp(bob, PaceBob(), 1f - Mathf.Exp(-bobBlendRate * dt));
            bool striding = locomotion.IsGrounded && !locomotion.IsSliding && !locomotion.IsDiving && locomotion.PlanarSpeed > 0.1f;
            bobWeight = Mathf.MoveTowards(bobWeight, striding ? 1f : 0f, dt * 4f);

            float multiplier = bobWeight * StanceBobMultiplier() * (locomotion.IsAiming ? aimBobMultiplier : 1f) *
                               (stance.IsMounted ? 0f : 1f);
            float phase = locomotion.StepPhase;
            float vertical = bob.vertical * multiplier * (Mathf.Cos(2f * phase) - 1f) * 0.5f; // two dips per stride
            float lateral = bob.lateral * multiplier * Mathf.Sin(phase);
            float roll = bob.roll * multiplier * Mathf.Sin(phase);
            float nod = -vertical * bobPitchPerMeter;

            StepLandingSpring(dt);
            slideBlend = Mathf.MoveTowards(slideBlend, locomotion.IsSliding ? 1f : 0f, dt / postureBlendTime);
            diveBlend = Mathf.MoveTowards(diveBlend, locomotion.IsDiving ? 1f : 0f, dt / postureBlendTime);

            Vector2 sway = WeaponSwayEuler * cameraSwayFraction;
            rig.HeadMotion.localPosition = new Vector3(lateral, vertical + landingOffset - slideLowering * slideBlend, 0f);
            rig.HeadMotion.localRotation = Quaternion.Euler(
                nod + sway.x + divePitch * diveBlend,
                sway.y,
                roll + slideRoll * slideBlend);
        }

        private void StepLandingSpring(float dt)
        {
            float w = landingSpringFrequency;
            int steps = Mathf.CeilToInt(dt / SpringStep);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                landingVelocity += (-w * w * landingOffset - 2f * w * landingVelocity) * h;
                landingOffset += landingVelocity * h;
            }
        }

        private void OnLanded(float impactSpeed)
        {
            // A critically damped spring kicked from rest with v0 peaks at v0 / (ω·e).
            float dip = Mathf.Min(maxLandingDip, impactSpeed * landingDipPerMps);
            landingVelocity -= dip * landingSpringFrequency * (float)Math.E;
        }

        private void OnSuppressed(in SuppressionEvent evt)
        {
            stress = Mathf.Min(maxStressHeartRate, stress + Mathf.Clamp01(evt.Intensity) * suppressionHeartRate);
        }

        private float ExertionHeartRate()
        {
            if (locomotion.IsSliding || locomotion.IsDiving) return slideHeartRate;
            switch (locomotion.Pace)
            {
                case LocomotionPace.SlowTacticalWalk: return walkHeartRate;
                case LocomotionPace.CombatPace: return combatPaceHeartRate;
                case LocomotionPace.Sprint: return sprintHeartRate;
                case LocomotionPace.TacticalSprint: return tacticalSprintHeartRate;
                default: return restingHeartRate;
            }
        }

        private BobShape PaceBob()
        {
            switch (locomotion.Pace)
            {
                case LocomotionPace.SlowTacticalWalk: return slowWalkBob;
                case LocomotionPace.CombatPace: return combatPaceBob;
                case LocomotionPace.Sprint: return sprintBob;
                case LocomotionPace.TacticalSprint: return tacticalSprintBob;
                default: return bob; // idle: keep the shape, bobWeight fades it out
            }
        }

        private float StanceSwayMultiplier() =>
            stance.Current == Stance.Prone ? proneSwayMultiplier : stance.Current == Stance.Crouch ? crouchSwayMultiplier : 1f;

        private float StanceBobMultiplier() =>
            stance.Current == Stance.Prone ? proneBobMultiplier : stance.Current == Stance.Crouch ? crouchBobMultiplier : 1f;
    }
}
