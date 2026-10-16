using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Audio
{
    /// <summary>
    /// A short-lived, pooled local volumetric fog cloud: muzzle smoke wisps and lingering impact dust. It grows, drifts
    /// and thins out (mean free path rising) over its lifetime; being real participating media it is lit by the sun,
    /// lamps and weapon lights, and casts and receives volumetric shadows. A 3D noise mask scrolls through it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalVolumetricFog))]
    public sealed class VolumetricPuff : MonoBehaviour, IPoolable
    {
        [Tooltip("Mean free path when the puff fades out (m): larger is thinner.")]
        [SerializeField, Min(0.1f)] private float fadedFreePath = 40f;

        private LocalVolumetricFog fog;
        private Vector3 startSize, endSize, drift;
        private float startFreePath, lifetime, age;

        public float Age => age;
        public float Lifetime => lifetime;
        public float MeanFreePath => fog != null ? fog.parameters.meanFreePath : 0f;
        public Vector3 Size => fog != null ? fog.parameters.size : Vector3.zero;

        /// <summary>
        /// Starts the puff: <paramref name="color"/> albedo, <paramref name="size"/> growing to <paramref name="growth"/>
        /// times over <paramref name="life"/> seconds, density as mean free path (m, smaller is thicker), drifting.
        /// </summary>
        public void Play(Color color, float size, float growth, float freePath, float life, Vector3 driftVelocity)
        {
            if (fog == null) fog = GetComponent<LocalVolumetricFog>();
            startSize = Vector3.one * size;
            endSize = startSize * growth;
            startFreePath = freePath;
            lifetime = Mathf.Max(0.05f, life);
            drift = driftVelocity;
            age = 0f;

            LocalVolumetricFogArtistParameters parameters = fog.parameters;
            parameters.albedo = color;
            parameters.size = startSize;
            parameters.meanFreePath = startFreePath;
            fog.parameters = parameters;
        }

        public void OnSpawned() => age = 0f;

        public void OnDespawned() => age = lifetime;

        private void Awake() => fog = GetComponent<LocalVolumetricFog>();

        private void Update()
        {
            float dt = Time.deltaTime;
            if (age >= lifetime || dt <= 0f) return;
            age += dt;
            float t = Mathf.Clamp01(age / lifetime);
            float grow = 1f - (1f - t) * (1f - t); // fast early expansion, then it hangs

            transform.position += drift * dt;
            drift *= Mathf.Exp(-1.5f * dt); // air drag
            LocalVolumetricFogArtistParameters parameters = fog.parameters;
            parameters.size = Vector3.Lerp(startSize, endSize, grow);
            parameters.meanFreePath = Mathf.Lerp(startFreePath, fadedFreePath, t * t);
            fog.parameters = parameters;
        }
    }
}
