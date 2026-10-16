using FPS.Ballistics;
using FPS.Core;
using UnityEngine;

namespace FPS.Audio
{
    /// <summary>
    /// Supersonic near misses as they sound. The bullet's Mach cone sweeps over the listener a few milliseconds after it
    /// passes, so the crack arrives from a point upstream of the closest approach (the classic misdirection of incoming
    /// fire), as an N-wave whose duration grows with miss distance (Whitham: T ∝ M b^¼ / (M² − 1)^⅜, played as a lower
    /// pitch) and whose overpressure falls as (M² − 1)^⅛ / b^¾. The round's flight noise follows as a whip, Doppler-shifted
    /// down while it recedes. Blasts (speed 0) and other listeners' events are ignored. Lives on the player root, the
    /// AudioListener below it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupersonicCrackEmitter : MonoBehaviour
    {
        /// <summary>The geometry and acoustics of one near miss.</summary>
        public struct Solution
        {
            public bool Supersonic;
            public float Mach;
            public float MissDistance;
            /// <summary>Where the shock that reaches the listener left the bullet's path (upstream of the closest point).</summary>
            public Vector3 EmissionPoint;
            /// <summary>Time from the bullet passing the closest point to its shock reaching the listener (s).</summary>
            public float ArrivalDelay;
            /// <summary>N-wave duration relative to the reference miss (1 at the reference distance and Mach number).</summary>
            public float DurationRatio;
            /// <summary>Peak overpressure relative to the reference miss.</summary>
            public float OverpressureRatio;
            /// <summary>Where the receding flight noise is heard from: one miss distance downstream.</summary>
            public Vector3 WhipPoint;
            /// <summary>Doppler factor of the receding flight noise, f'/f = 1 / (1 + M cos 45°).</summary>
            public float DopplerPitch;
            /// <summary>Time from the bullet passing the closest point to its flight noise from <see cref="WhipPoint"/> arriving (s).</summary>
            public float WhipDelay;
        }

        [Tooltip("N-wave cracks recorded or synthesized for the reference miss; one is picked per near miss.")]
        [SerializeField] private AudioClip[] crackClips = new AudioClip[0];
        [Tooltip("Receding flight noise (whip/zip), played Doppler-shifted after the crack.")]
        [SerializeField] private AudioClip[] whipClips = new AudioClip[0];
        [Tooltip("Miss distance the crack clips represent (m).")]
        [SerializeField, Min(0.1f)] private float referenceMiss = 1f;
        [Tooltip("Mach number the crack clips represent.")]
        [SerializeField, Min(1.05f)] private float referenceMach = 2.5f;
        [SerializeField, Range(0f, 1f)] private float crackVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float whipVolume = 0.45f;
        [Tooltip("Cracks and whips fade out by this distance from where they are heard from (m).")]
        [SerializeField, Min(1f)] private float maxDistance = 40f;

        private Transform listener;
        private AudioEmitter emitterTemplate;

        /// <summary>The last near miss solved for this listener.</summary>
        public Solution Last { get; private set; }

        /// <summary>
        /// Solves a near miss: the listener at <paramref name="listenerPosition"/>, the round's closest approach
        /// <paramref name="closest"/>, its direction of travel and speed (m/s); ratios are relative to a round at
        /// <paramref name="referenceMach"/> passing <paramref name="referenceMiss"/> metres away.
        /// </summary>
        public static Solution Solve(Vector3 listenerPosition, Vector3 closest, Vector3 direction, float speed, float referenceMiss, float referenceMach)
        {
            Vector3 travel = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
            float c = BallisticsMath.SpeedOfSound;
            float miss = Mathf.Max(0.05f, Vector3.Distance(listenerPosition, closest));
            float mach = Mathf.Max(0f, speed) / c;
            var solution = new Solution
            {
                Mach = mach,
                MissDistance = miss,
                EmissionPoint = closest,
                WhipPoint = closest + travel * miss,
                DopplerPitch = 1f / (1f + mach * 0.70710678f),
                WhipDelay = (speed > 1f ? miss / speed : 0f) + miss * 1.41421356f / c,
                DurationRatio = 1f,
                OverpressureRatio = 0f,
            };
            if (mach <= 1.0001f) return solution; // subsonic: no shock wave, only the flight noise

            float root = Mathf.Sqrt(mach * mach - 1f);
            float referenceRoot = Mathf.Max(1.0001f, referenceMach);
            solution.Supersonic = true;
            solution.EmissionPoint = closest - travel * Mathf.Min(miss / root, 200f); // Mach angle: sin μ = 1/M
            solution.ArrivalDelay = miss * root / (mach * c);
            solution.DurationRatio = WhithamDuration(mach, miss) / WhithamDuration(referenceRoot, referenceMiss);
            solution.OverpressureRatio = WhithamOverpressure(mach, miss) / WhithamOverpressure(referenceRoot, referenceMiss);
            return solution;
        }

        private static float WhithamDuration(float mach, float miss) => mach * Mathf.Pow(miss, 0.25f) / Mathf.Pow(mach * mach - 1f, 0.375f);

        private static float WhithamOverpressure(float mach, float miss) => Mathf.Pow(mach * mach - 1f, 0.125f) / Mathf.Pow(miss, 0.75f);

        private void Awake()
        {
            var audioListener = GetComponentInChildren<AudioListener>(true);
            listener = audioListener != null ? audioListener.transform : transform;

            var template = new GameObject("Crack Emitter Template");
            template.SetActive(false); // pooled clones wake when spawned
            template.transform.SetParent(transform, false);
            template.AddComponent<AudioSource>();
            emitterTemplate = template.AddComponent<AudioEmitter>();
        }

        private void OnEnable() => EventBus<SuppressionEvent>.Subscribe(OnSuppressed);

        private void OnDisable() => EventBus<SuppressionEvent>.Unsubscribe(OnSuppressed);

        private void OnSuppressed(in SuppressionEvent evt)
        {
            if (evt.Listener != listener || evt.Speed <= 0f) return;

            Solution solution = Solve(listener.position, evt.Point, evt.Direction, evt.Speed, referenceMiss, referenceMach);
            Last = solution;
            if (solution.Supersonic && crackClips.Length > 0)
            {
                // A longer N-wave is a lower, duller crack.
                float pitch = Mathf.Clamp(1f / solution.DurationRatio, 0.6f, 1.6f);
                Emit(crackClips[Random.Range(0, crackClips.Length)], solution.EmissionPoint, crackVolume * Mathf.Clamp01(solution.OverpressureRatio),
                    pitch, solution.ArrivalDelay);
            }

            if (whipClips.Length > 0)
            {
                float volume = whipVolume * Mathf.Clamp01(referenceMiss / solution.MissDistance);
                Emit(whipClips[Random.Range(0, whipClips.Length)], solution.WhipPoint, volume, Mathf.Clamp(solution.DopplerPitch, 0.25f, 1f),
                    solution.WhipDelay);
            }
        }

        private void Emit(AudioClip clip, Vector3 position, float volume, float pitch, float delay)
        {
            if (clip == null || volume < 0.01f) return;
            AudioEmitter emitter = ObjectPoolManager.Instance.Spawn(emitterTemplate, position, Quaternion.identity, null,
                AudioEmitter.LifetimeFor(clip, delay, pitch));
            emitter.Play(clip, volume, pitch, delay, 1f, maxDistance);
        }
    }
}
