using FPS.Ballistics;
using FPS.Core;
using UnityEngine;

namespace FPS.Audio
{
    /// <summary>
    /// The listener's acoustic world. Own shots play as layered first-person audio (muzzle blast, sub-bass thump,
    /// mechanical action, body resonance; the magazine pings on the last rounds while the action rises in pitch);
    /// others arrive after distance / 343 m/s as close or distant reports. Reverb tails blend between small room,
    /// warehouse, urban canyon and open field by ray-probing the surroundings (ceiling, walls and how far they are).
    /// Footsteps (<see cref="FootstepEvent"/>): own steps play in the head, others' from where they fall, delayed, and only
    /// within the footstep radius AI hearing uses.
    /// Suppression or heavy damage muffles the mix through a low-pass filter on the listener (near-miss cracks belong to
    /// <see cref="SupersonicCrackEmitter"/>). Lives on the player root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AcousticPropagationSystem : MonoBehaviour
    {
        private const int SmallRoom = 0, Warehouse = 1, Urban = 2, Open = 3;
        /// <summary>
        /// Voices per space. Each shot starts its tail on the oldest voice, so in sustained fire every tail rings for a few
        /// shots (its early reflections and the build-up) instead of being restarted by the next round before it is heard.
        /// </summary>
        public const int TailVoices = 4;

        [Header("Handling Foley")]
        [SerializeField] private AudioClip dryFire;
        [SerializeField] private AudioClip magazineOut;
        [SerializeField] private AudioClip magazineIn;
        [SerializeField] private AudioClip boltCycle;
        [SerializeField] private AudioClip fireModeSwitch;
        [SerializeField] private AudioClip magazineCheck;

        [Header("Mix")]
        [Tooltip("How much own-weapon audio is placed in the world rather than in the head.")]
        [SerializeField, Range(0f, 1f)] private float firstPersonSpatialBlend = 0.3f;
        [Tooltip("Own-shot layers are each normalized near full scale; this leaves the summed shot headroom under the master limiter.")]
        [SerializeField, Range(0f, 1f)] private float firstPersonGain = 0.6f;
        [SerializeField, Range(0f, 1f)] private float tailVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float subBassVolume = 0.85f;
        [SerializeField] private float remoteMinDistance = 6f;
        [Tooltip("Handling noise of other people carries this far (m).")]
        [SerializeField] private float remoteFoleyMaxDistance = 25f;

        [Header("Footsteps")]
        [Tooltip("Own steps at full loudness (a sprint). Heard from 1.6 m above the feet, so a square-root curve keeps a crouched step audible.")]
        [SerializeField, Range(0f, 1f)] private float ownFootstepVolume = 0.35f;
        [Tooltip("Distance (m) inside which someone else's steps get no louder.")]
        [SerializeField, Min(0.1f)] private float footstepMinDistance = 1f;

        [Header("Last Rounds")]
        [Tooltip("Ping pitch rise on the very last round (fraction); the follower spring is nearly unloaded.")]
        [SerializeField] private float lowAmmoPitchRise = 0.12f;
        [Tooltip("Action pitch rise on the very last round (fraction): a lighter magazine stack under the bolt.")]
        [SerializeField] private float lowAmmoActionRise = 0.04f;
        [SerializeField, Range(0f, 1f)] private float lowAmmoVolume = 0.55f;

        [Header("Environment Probe")]
        [SerializeField] private float probeInterval = 0.25f;
        [SerializeField] private float ceilingProbe = 20f;
        [SerializeField] private float wallProbe = 60f;
        [SerializeField, Range(4, 16)] private int horizontalRays = 8;
        [SerializeField] private float environmentBlendSpeed = 3f;
        [Tooltip("Room size (m, larger of 1.6 x ceiling height and mean wall distance) where a room starts sounding like a hall.")]
        [SerializeField] private float hallStart = 5f;
        [Tooltip("Room size (m) at which it is fully a warehouse.")]
        [SerializeField] private float hallFull = 12f;

        [Header("Muffling")]
        [SerializeField] private float openCutoff = 22000f;
        [SerializeField] private float muffledCutoff = 700f;
        [Tooltip("Time for full suppression to stop muffling (s).")]
        [SerializeField] private float suppressionDecay = 1.5f;
        [SerializeField, Range(0f, 1f)] private float suppressionMuffle = 0.7f;
        [Tooltip("Health fraction below which damage starts muffling the world.")]
        [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.35f;

        [Header("Stun Grenades")]
        [Tooltip("Ringing played at full deafness, fading as hearing returns.")]
        [SerializeField] private AudioClip tinnitus;
        [Tooltip("Time for full deafness to wear off (s).")]
        [SerializeField, Min(0.1f)] private float deafnessRecovery = 6f;

        private readonly AudioSource[] tails = new AudioSource[4 * TailVoices];
        private readonly int[] nextTailVoice = new int[4];
        private Transform listener;
        private AudioSource firstPerson;
        private AudioSource mechanism;
        private AudioSource ping;
        private AudioSource footsteps;
        private AudioSource ringing;
        private AudioLowPassFilter lowPass;
        private TacticalHealthAndArmorSystem health;
        private AudioEmitter emitterTemplate;
        private Vector3 environment = new Vector3(0f, 0f, 1f);
        private Vector3 targetEnvironment = new Vector3(0f, 0f, 1f);
        private float nextProbe, suppression, targetWarehouse;

        /// <summary>Smoothed (indoor, urban, open) tail weights; they sum to 1.</summary>
        public Vector3 EnvironmentWeights => environment;
        /// <summary>Smoothed share of the indoor weight that is a warehouse or hall rather than a small room (0-1).</summary>
        public float WarehouseShare { get; private set; }
        public float MuffleAmount { get; private set; }
        /// <summary>Stun-grenade deafness, 0-1.</summary>
        public float Deafness { get; private set; }
        public float CutoffFrequency => lowPass != null ? lowPass.cutoffFrequency : openCutoff;

        /// <summary>
        /// Share of a remote shot's loudness in its reverb tail: reverberant energy falls about 3 dB per doubling of
        /// distance beyond <paramref name="nearDistance"/> (the direct report falls 6 dB), fading out at the audible limit.
        /// </summary>
        public static float RemoteTailGain(float distance, float nearDistance, float maxAudible) =>
            0.7f * Mathf.Sqrt(Mathf.Min(1f, nearDistance / Mathf.Max(distance, 0.01f))) * Mathf.Clamp01(1f - distance / maxAudible);

        /// <summary>Time for sound to cover <paramref name="distance"/> metres.</summary>
        public static float PropagationDelay(float distance) => distance / BallisticsMath.SpeedOfSound;

        /// <summary>
        /// Tail weights (indoor, urban, open) from a ray probe: a ceiling means a room (denser the more walls answer);
        /// open sky with walls around is an urban canyon (stronger when they are close); otherwise open ground.
        /// </summary>
        public static Vector3 ClassifyEnvironment(bool ceiling, float wallHitFraction, float meanWallDistance)
        {
            float indoor = ceiling ? 0.4f + 0.6f * wallHitFraction : 0f;
            float urban = ceiling ? 0f : wallHitFraction * Mathf.Clamp01(1.2f - meanWallDistance / 60f);
            float open = Mathf.Max(0f, 1f - indoor - urban);
            return new Vector3(indoor, urban, open) / (indoor + urban + open);
        }

        /// <summary>
        /// How much of a room is a hall: 0 below <paramref name="start"/> metres of size, 1 above <paramref name="full"/>,
        /// where size is the larger of 1.6 x the ceiling height and the mean distance to the walls.
        /// </summary>
        public static float HallShare(float ceilingHeight, float meanWallDistance, float start, float full) =>
            Mathf.Clamp01((Mathf.Max(ceilingHeight * 1.6f, meanWallDistance) - start) / Mathf.Max(0.01f, full - start));

        /// <summary>
        /// Last-rounds warning, 0 while plenty is left, then rising to 1 on the very last round: with
        /// <paramref name="warnRounds"/> = 3 the shots leaving 2, 1 and 0 rounds give 1/3, 2/3 and 1. Negative counts
        /// (no magazine) never warn.
        /// </summary>
        public static float LowAmmoShift(int roundsLeft, int warnRounds) =>
            roundsLeft < 0 || roundsLeft >= warnRounds || warnRounds <= 0 ? 0f : (float)(warnRounds - roundsLeft) / warnRounds;

        private void Awake()
        {
            var audioListener = GetComponentInChildren<AudioListener>(true);
            if (audioListener == null)
            {
                Debug.LogError($"{nameof(AcousticPropagationSystem)} on '{name}' needs an AudioListener in its children.", this);
                enabled = false;
                return;
            }

            listener = audioListener.transform;
            TryGetComponent(out health);
            if (!listener.TryGetComponent(out lowPass)) lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
            if (!listener.TryGetComponent(out DynamicRangeProcessor _)) listener.gameObject.AddComponent<DynamicRangeProcessor>(); // master-bus dynamics
            lowPass.cutoffFrequency = openCutoff;

            var mix = new GameObject("Acoustics").transform;
            mix.SetParent(listener, false);
            firstPerson = CreateSource(mix, firstPersonSpatialBlend);
            mechanism = CreateSource(mix, firstPersonSpatialBlend);
            ping = CreateSource(mix, firstPersonSpatialBlend);
            footsteps = CreateSource(mix, 0f);
            for (int i = 0; i < tails.Length; i++) tails[i] = CreateSource(mix, 0f);
            ringing = CreateSource(mix, 0f);
            ringing.loop = true;

            var template = new GameObject("Audio Emitter Template");
            template.SetActive(false); // pooled clones wake when spawned
            template.transform.SetParent(transform, false);
            template.AddComponent<AudioSource>();
            emitterTemplate = template.AddComponent<AudioEmitter>();
        }

        private void OnEnable()
        {
            EventBus<GunshotEvent>.Subscribe(OnGunshot);
            EventBus<WeaponActionEvent>.Subscribe(OnWeaponAction);
            EventBus<SuppressionEvent>.Subscribe(OnSuppressed);
            EventBus<FlashbangEvent>.Subscribe(OnFlashbang);
            EventBus<FootstepEvent>.Subscribe(OnFootstep);
        }

        private void OnDisable()
        {
            EventBus<GunshotEvent>.Unsubscribe(OnGunshot);
            EventBus<WeaponActionEvent>.Unsubscribe(OnWeaponAction);
            EventBus<SuppressionEvent>.Unsubscribe(OnSuppressed);
            EventBus<FlashbangEvent>.Unsubscribe(OnFlashbang);
            EventBus<FootstepEvent>.Unsubscribe(OnFootstep);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (Time.time >= nextProbe)
            {
                nextProbe = Time.time + probeInterval;
                targetEnvironment = Probe(listener.position, out targetWarehouse);
            }

            environment = Vector3.MoveTowards(environment, targetEnvironment, environmentBlendSpeed * dt);
            WarehouseShare = Mathf.MoveTowards(WarehouseShare, targetWarehouse, environmentBlendSpeed * dt);
            suppression = Mathf.Max(0f, suppression - dt / suppressionDecay);

            float lowHealth = health != null && lowHealthThreshold > 0f
                ? Mathf.Clamp01((lowHealthThreshold - health.Health / health.MaxHealth) / lowHealthThreshold)
                : 0f;
            if (Deafness > 0f)
            {
                Deafness = Mathf.Max(0f, Deafness - dt / deafnessRecovery);
                ringing.volume = Deafness;
                if (Deafness <= 0f) ringing.Stop();
            }

            MuffleAmount = Mathf.Max(Mathf.Max(suppression * suppressionMuffle, lowHealth), Deafness);
            lowPass.cutoffFrequency = openCutoff * Mathf.Pow(muffledCutoff / openCutoff, MuffleAmount); // perceptually even sweep
        }

        private void OnGunshot(in GunshotEvent shot)
        {
            WeaponAudioProfileSO profile = shot.Profile;
            if (profile == null) return;

            if (shot.Shooter == transform)
            {
                float loudness = profile.Loudness * firstPersonGain;
                firstPerson.pitch = Random.Range(0.97f, 1.03f);
                PlayFirstPerson(profile.PressureTransient, loudness);
                PlayFirstPerson(profile.SubBass, loudness * subBassVolume);
                PlayFirstPerson(profile.BodyResonance, loudness * 0.9f);

                float lowAmmo = LowAmmoShift(shot.RoundsLeft, profile.LowAmmoRounds);
                mechanism.pitch = Random.Range(0.98f, 1.02f) * (1f + lowAmmoActionRise * lowAmmo);
                AudioClip action = profile.MechanicalCycle;
                if (action != null) mechanism.PlayOneShot(action, loudness * 0.8f);
                if (lowAmmo > 0f && profile.LowAmmoPing != null)
                {
                    ping.clip = profile.LowAmmoPing;
                    ping.pitch = 1f + lowAmmoPitchRise * lowAmmo;
                    ping.volume = lowAmmoVolume * (0.6f + 0.4f * lowAmmo);
                    ping.Play();
                }

                PlayTails(profile, 0f, loudness);
                return;
            }

            float distance = Vector3.Distance(shot.Position, listener.position);
            if (distance > profile.MaxAudibleDistance) return;

            float delay = PropagationDelay(distance);
            float pitch = Random.Range(0.97f, 1.03f);
            bool distant = distance > profile.DistantThreshold;
            AudioClip report = distant ? profile.DistantReport : profile.PressureTransient;
            Emit(report, shot.Position, profile.Loudness, pitch, delay, remoteMinDistance, profile.MaxAudibleDistance);
            if (!distant) Emit(profile.SubBass, shot.Position, profile.Loudness * subBassVolume, pitch, delay, remoteMinDistance, profile.DistantThreshold);
            // Up close the action (or an explosion's falling debris) is heard too.
            if (distance <= remoteFoleyMaxDistance)
                Emit(profile.MechanicalCycle, shot.Position, profile.Loudness * 0.8f, pitch, delay, 1f, remoteFoleyMaxDistance);
            PlayTails(profile, delay, profile.Loudness * RemoteTailGain(distance, remoteMinDistance, profile.MaxAudibleDistance));
        }

        private void OnWeaponAction(in WeaponActionEvent action)
        {
            AudioClip clip = FoleyFor(action.Type);
            if (clip == null) return;

            if (action.Owner == transform)
            {
                firstPerson.PlayOneShot(clip, 0.8f);
                return;
            }

            float distance = Vector3.Distance(action.Position, listener.position);
            if (distance <= remoteFoleyMaxDistance)
                Emit(clip, action.Position, 0.8f, 1f, PropagationDelay(distance), 1f, remoteFoleyMaxDistance);
        }

        private void OnFootstep(in FootstepEvent step)
        {
            if (step.Clip == null) return;
            float pitch = Random.Range(0.94f, 1.06f);
            if (step.Source == transform)
            {
                footsteps.pitch = pitch;
                footsteps.PlayOneShot(step.Clip, ownFootstepVolume * Mathf.Sqrt(step.Loudness));
                return;
            }

            float distance = Vector3.Distance(step.Position, listener.position);
            if (distance <= step.Radius)
                Emit(step.Clip, step.Position, step.Loudness, pitch, PropagationDelay(distance), footstepMinDistance, step.Radius);
        }

        private void OnSuppressed(in SuppressionEvent evt)
        {
            if (evt.Listener != listener) return;

            suppression = Mathf.Clamp01(suppression + evt.Intensity * 0.7f);
        }

        private void OnFlashbang(in FlashbangEvent bang)
        {
            FlashbangEvent.Exposure(bang.Position, bang.Intensity, listener.position, listener.forward, out float deafness);
            if (deafness <= Deafness) return;

            Deafness = deafness;
            if (tinnitus == null) return;
            ringing.clip = tinnitus;
            ringing.volume = Deafness;
            if (!ringing.isPlaying) ringing.Play();
        }

        private void PlayFirstPerson(AudioClip clip, float volume)
        {
            if (clip != null) firstPerson.PlayOneShot(clip, volume);
        }

        private void PlayTails(WeaponAudioProfileSO profile, float delay, float volume)
        {
            PlayTail(SmallRoom, profile.IndoorTail, environment.x * (1f - WarehouseShare) * volume, delay);
            PlayTail(Warehouse, profile.WarehouseTail, environment.x * WarehouseShare * volume, delay);
            PlayTail(Urban, profile.UrbanTail, environment.y * volume, delay);
            PlayTail(Open, profile.OpenTail, environment.z * volume, delay);
        }

        private void PlayTail(int space, AudioClip clip, float volume, float delay)
        {
            if (clip == null || volume < 0.01f) return;
            int voice = nextTailVoice[space];
            nextTailVoice[space] = (voice + 1) % TailVoices;
            AudioSource source = tails[space * TailVoices + voice];
            source.clip = clip;
            source.volume = volume * tailVolume;
            if (delay > 0f) source.PlayDelayed(delay);
            else source.Play();
        }

        private void Emit(AudioClip clip, Vector3 position, float volume, float pitch, float delay, float minDistance, float maxDistance)
        {
            if (clip == null) return;
            AudioEmitter emitter = ObjectPoolManager.Instance.Spawn(emitterTemplate, position, Quaternion.identity, null,
                AudioEmitter.LifetimeFor(clip, delay, pitch));
            emitter.Play(clip, volume, pitch, delay, minDistance, maxDistance);
        }

        private Vector3 Probe(Vector3 origin, out float hall)
        {
            int mask = GameLayers.EnvironmentMask;
            bool ceiling = Physics.Raycast(origin, Vector3.up, out RaycastHit roof, ceilingProbe, mask, QueryTriggerInteraction.Ignore);
            int hits = 0;
            float distanceSum = 0f;
            for (int i = 0; i < horizontalRays; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, i * 360f / horizontalRays, 0f) * Vector3.forward;
                if (!Physics.Raycast(origin, direction, out RaycastHit hit, wallProbe, mask, QueryTriggerInteraction.Ignore)) continue;
                hits++;
                distanceSum += hit.distance;
            }

            float meanWall = hits > 0 ? distanceSum / hits : wallProbe;
            hall = ceiling ? HallShare(roof.distance, meanWall, hallStart, hallFull) : 0f;
            return ClassifyEnvironment(ceiling, (float)hits / horizontalRays, meanWall);
        }

        private AudioClip FoleyFor(WeaponActionType type)
        {
            switch (type)
            {
                case WeaponActionType.DryFire: return dryFire;
                case WeaponActionType.MagazineRemoved:
                case WeaponActionType.MagazineDropped: return magazineOut;
                case WeaponActionType.MagazineInserted: return magazineIn;
                case WeaponActionType.BoltCycled:
                case WeaponActionType.BoltLockedBack: return boltCycle;
                case WeaponActionType.FireModeChanged: return fireModeSwitch;
                case WeaponActionType.MagazineChecked: return magazineCheck;
                default: return null;
            }
        }

        private static AudioSource CreateSource(Transform parent, float spatialBlend)
        {
            var source = parent.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
