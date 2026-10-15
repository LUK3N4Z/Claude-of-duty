using FPS.Ballistics;
using FPS.Core;
using UnityEngine;

namespace FPS.Audio
{
    /// <summary>
    /// The listener's acoustic world. Own shots play as layered first-person audio (pressure transient, mechanism, body
    /// resonance); others arrive after distance / 343 m/s as close or distant reports. Reverb tails blend between indoor,
    /// urban and open by ray-probing the surroundings. Supersonic rounds crack past the head, and suppression or heavy
    /// damage muffles the mix through a low-pass filter on the listener. Lives on the player root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AcousticPropagationSystem : MonoBehaviour
    {
        private const int Indoor = 0, Urban = 1, Open = 2;

        [Header("Handling Foley")]
        [SerializeField] private AudioClip dryFire;
        [SerializeField] private AudioClip magazineOut;
        [SerializeField] private AudioClip magazineIn;
        [SerializeField] private AudioClip boltCycle;
        [SerializeField] private AudioClip fireModeSwitch;
        [SerializeField] private AudioClip magazineCheck;
        [Tooltip("Supersonic N-wave cracks; one is picked per near miss.")]
        [SerializeField] private AudioClip[] supersonicCracks = new AudioClip[0];

        [Header("Mix")]
        [Tooltip("How much own-weapon audio is placed in the world rather than in the head.")]
        [SerializeField, Range(0f, 1f)] private float firstPersonSpatialBlend = 0.3f;
        [SerializeField, Range(0f, 1f)] private float tailVolume = 0.8f;
        [SerializeField] private float remoteMinDistance = 6f;
        [Tooltip("Handling noise of other people carries this far (m).")]
        [SerializeField] private float remoteFoleyMaxDistance = 25f;

        [Header("Environment Probe")]
        [SerializeField] private float probeInterval = 0.25f;
        [SerializeField] private float ceilingProbe = 20f;
        [SerializeField] private float wallProbe = 60f;
        [SerializeField, Range(4, 16)] private int horizontalRays = 8;
        [SerializeField] private float environmentBlendSpeed = 3f;

        [Header("Muffling")]
        [SerializeField] private float openCutoff = 22000f;
        [SerializeField] private float muffledCutoff = 700f;
        [Tooltip("Time for full suppression to stop muffling (s).")]
        [SerializeField] private float suppressionDecay = 1.5f;
        [SerializeField, Range(0f, 1f)] private float suppressionMuffle = 0.7f;
        [Tooltip("Health fraction below which damage starts muffling the world.")]
        [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.35f;

        private readonly AudioSource[] tails = new AudioSource[3];
        private Transform listener;
        private AudioSource firstPerson;
        private AudioLowPassFilter lowPass;
        private TacticalHealthAndArmorSystem health;
        private AudioEmitter emitterTemplate;
        private Vector3 environment = new Vector3(0f, 0f, 1f);
        private Vector3 targetEnvironment = new Vector3(0f, 0f, 1f);
        private float nextProbe, suppression;

        /// <summary>Smoothed (indoor, urban, open) tail weights; they sum to 1.</summary>
        public Vector3 EnvironmentWeights => environment;
        public float MuffleAmount { get; private set; }
        public float CutoffFrequency => lowPass != null ? lowPass.cutoffFrequency : openCutoff;

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
            lowPass.cutoffFrequency = openCutoff;

            var mix = new GameObject("Acoustics").transform;
            mix.SetParent(listener, false);
            firstPerson = CreateSource(mix, firstPersonSpatialBlend);
            for (int i = 0; i < tails.Length; i++) tails[i] = CreateSource(mix, 0f);

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
        }

        private void OnDisable()
        {
            EventBus<GunshotEvent>.Unsubscribe(OnGunshot);
            EventBus<WeaponActionEvent>.Unsubscribe(OnWeaponAction);
            EventBus<SuppressionEvent>.Unsubscribe(OnSuppressed);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (Time.time >= nextProbe)
            {
                nextProbe = Time.time + probeInterval;
                targetEnvironment = Probe(listener.position);
            }

            environment = Vector3.MoveTowards(environment, targetEnvironment, environmentBlendSpeed * dt);
            suppression = Mathf.Max(0f, suppression - dt / suppressionDecay);

            float lowHealth = health != null && lowHealthThreshold > 0f
                ? Mathf.Clamp01((lowHealthThreshold - health.Health / health.MaxHealth) / lowHealthThreshold)
                : 0f;
            MuffleAmount = Mathf.Max(suppression * suppressionMuffle, lowHealth);
            lowPass.cutoffFrequency = openCutoff * Mathf.Pow(muffledCutoff / openCutoff, MuffleAmount); // perceptually even sweep
        }

        private void OnGunshot(in GunshotEvent shot)
        {
            WeaponAudioProfileSO profile = shot.Profile;
            if (profile == null) return;

            if (shot.Shooter == transform)
            {
                firstPerson.pitch = Random.Range(0.97f, 1.03f);
                PlayFirstPerson(profile.PressureTransient, profile.Loudness);
                PlayFirstPerson(profile.MechanicalCycle, profile.Loudness * 0.8f);
                PlayFirstPerson(profile.BodyResonance, profile.Loudness * 0.9f);
                PlayTails(profile, 0f, profile.Loudness);
                return;
            }

            float distance = Vector3.Distance(shot.Position, listener.position);
            if (distance > profile.MaxAudibleDistance) return;

            float delay = PropagationDelay(distance);
            AudioClip report = distance > profile.DistantThreshold ? profile.DistantReport : profile.PressureTransient;
            Emit(report, shot.Position, profile.Loudness, Random.Range(0.97f, 1.03f), delay, remoteMinDistance, profile.MaxAudibleDistance);
            PlayTails(profile, delay, profile.Loudness * 0.7f * (1f - distance / profile.MaxAudibleDistance));
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

        private void OnSuppressed(in SuppressionEvent evt)
        {
            if (evt.Listener != listener) return;

            suppression = Mathf.Clamp01(suppression + evt.Intensity * 0.7f);
            if (supersonicCracks.Length > 0)
                Emit(supersonicCracks[Random.Range(0, supersonicCracks.Length)], evt.Point, Mathf.Lerp(0.5f, 1f, evt.Intensity),
                    Random.Range(0.95f, 1.05f), 0f, 1f, 30f);
        }

        private void PlayFirstPerson(AudioClip clip, float volume)
        {
            if (clip != null) firstPerson.PlayOneShot(clip, volume);
        }

        private void PlayTails(WeaponAudioProfileSO profile, float delay, float volume)
        {
            PlayTail(tails[Indoor], profile.IndoorTail, environment.x * volume, delay);
            PlayTail(tails[Urban], profile.UrbanTail, environment.y * volume, delay);
            PlayTail(tails[Open], profile.OpenTail, environment.z * volume, delay);
        }

        private void PlayTail(AudioSource source, AudioClip clip, float volume, float delay)
        {
            if (clip == null || volume < 0.01f) return;
            // Restarting one voice per environment keeps sustained fire as a single continuous tail.
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

        private Vector3 Probe(Vector3 origin)
        {
            int mask = GameLayers.EnvironmentMask;
            bool ceiling = Physics.Raycast(origin, Vector3.up, ceilingProbe, mask, QueryTriggerInteraction.Ignore);
            int hits = 0;
            float distanceSum = 0f;
            for (int i = 0; i < horizontalRays; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, i * 360f / horizontalRays, 0f) * Vector3.forward;
                if (!Physics.Raycast(origin, direction, out RaycastHit hit, wallProbe, mask, QueryTriggerInteraction.Ignore)) continue;
                hits++;
                distanceSum += hit.distance;
            }

            return ClassifyEnvironment(ceiling, (float)hits / horizontalRays, hits > 0 ? distanceSum / hits : wallProbe);
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
