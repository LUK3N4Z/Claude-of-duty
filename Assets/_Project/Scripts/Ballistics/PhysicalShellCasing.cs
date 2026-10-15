using FPS.Core;
using UnityEngine;

namespace FPS.Ballistics
{
    /// <summary>
    /// Pooled spent case: ejected with velocity and spin, tumbles on the Debris layer and plays the hit surface's
    /// clink on its first few impacts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
    public sealed class PhysicalShellCasing : MonoBehaviour, IPoolable
    {
        [Tooltip("Surface used when the hit collider has no SurfaceIdentifier.")]
        [SerializeField] private SurfaceMaterialSO fallbackSurface;
        [Tooltip("Slower impacts are silent (m/s).")]
        [SerializeField] private float minImpactSpeed = 0.6f;
        [SerializeField] private float volumePerMetrePerSecond = 0.3f;
        [SerializeField, Min(1)] private int maxClinks = 4;

        private Rigidbody body;
        private AudioSource source;
        private int clinks;

        public SurfaceMaterialSO FallbackSurface
        {
            get => fallbackSurface;
            set => fallbackSurface = value;
        }

        /// <summary>Throws the case out of the port.</summary>
        public void Eject(Vector3 velocity, Vector3 angularVelocity)
        {
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
        }

        public void OnSpawned()
        {
            clinks = 0;
            body.WakeUp();
        }

        public void OnDespawned()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            source = GetComponent<AudioSource>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (clinks >= maxClinks) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;

            SurfaceMaterialSO surface = SurfaceIdentifier.Resolve(collision.collider, fallbackSurface);
            AudioClip clip = surface != null ? surface.RandomCasingClip() : null;
            if (clip == null) return;

            clinks++;
            source.pitch = Random.Range(0.92f, 1.08f);
            source.PlayOneShot(clip, Mathf.Clamp01(speed * volumePerMetrePerSecond));
        }
    }
}
