using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.AI
{
    /// <summary>
    /// Footfalls of a <see cref="PerceivableTarget"/>: a <see cref="FootstepEvent"/> per foot strike with the sound of the
    /// surface underfoot, as loud and as far-carrying as the footstep noise AI hearing uses (speed, stance, sprint). The
    /// player's steps follow the head-bob gait (<see cref="RealisticLocomotionController.StepPhase"/>, a strike at the
    /// bottom of each bob); soldiers keep their own gait from the agent's speed. Prone, each movement is a crawl rustle;
    /// the player's landings thud with fall speed. <see cref="AcousticPropagationSystem"/> plays them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PerceivableTarget))]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        private const float TwoPi = Mathf.PI * 2f;

        [Tooltip("Ground without a SurfaceIdentifier, or one without footstep audio, sounds like this.")]
        [SerializeField] private SurfaceMaterialSO fallbackSurface;
        [Tooltip("Footstep noise radius (m) that is full loudness: about a sprint.")]
        [SerializeField, Min(1f)] private float fullLoudnessRadius = 40f;
        [Tooltip("Impact speed (m/s) of a landing at full loudness: about a 1.8 m drop.")]
        [SerializeField, Min(0.1f)] private float hardLandingSpeed = 6f;

        [Header("Soldier Gait")]
        [Tooltip("Stride (two steps, m) extrapolated to a standstill; it lengthens with speed.")]
        [SerializeField, Min(0.1f)] private float baseStride = 1.2f;
        [Tooltip("Extra stride length per m/s of speed (s).")]
        [SerializeField, Min(0f)] private float stridePerSpeed = 0.2f;
        [SerializeField, Range(0.3f, 1f)] private float crouchStrideMultiplier = 0.75f;

        private PerceivableTarget target;
        private RealisticLocomotionController locomotion;
        private StanceAndLeanController stance;
        private AIBody body;
        private float phase;
        private int foot;

        /// <summary>The foot (0 or 1) carrying the body at gait phase <paramref name="stepPhase"/> (rad); strikes fall at π/2 and 3π/2.</summary>
        public static int Foot(float stepPhase) => Mathf.Repeat(stepPhase - Mathf.PI * 0.5f, TwoPi) < Mathf.PI ? 0 : 1;

        /// <summary>
        /// Source loudness 0-1 of a step heard out to <paramref name="noiseRadius"/> m. Pressure falls as 1/distance, so
        /// the amplitude that fades out at a radius is proportional to it.
        /// </summary>
        public static float Loudness(float noiseRadius, float fullRadius) => Mathf.Clamp01(noiseRadius / fullRadius);

        public SurfaceMaterialSO FallbackSurface
        {
            get => fallbackSurface;
            set => fallbackSurface = value;
        }

        private float Phase => locomotion != null ? locomotion.StepPhase : phase;

        private void Awake()
        {
            target = GetComponent<PerceivableTarget>();
            TryGetComponent(out locomotion);
            TryGetComponent(out stance);
            TryGetComponent(out body);
        }

        private void OnEnable()
        {
            foot = Foot(Phase);
            if (locomotion != null) locomotion.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (locomotion != null) locomotion.Landed -= OnLanded;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !target.IsAlive) return;

            if (locomotion == null)
            {
                Vector3 velocity = target.Velocity;
                float speed = new Vector2(velocity.x, velocity.z).magnitude;
                float stride = (baseStride + stridePerSpeed * speed) * (body != null && body.IsCrouched ? crouchStrideMultiplier : 1f);
                phase = Mathf.Repeat(phase + speed / stride * TwoPi * dt, TwoPi);
            }

            int down = Foot(Phase);
            if (down == foot) return;
            foot = down;

            float radius = target.NoiseRadius;
            if (radius <= 0f) return;
            bool crawl = stance != null && stance.Current == Stance.Prone;
            Raise(crawl ? Sound.Crawl : Sound.Step, radius, Loudness(radius, fullLoudnessRadius));
        }

        private void OnLanded(float impactSpeed)
        {
            float loudness = Mathf.Clamp01(impactSpeed / hardLandingSpeed);
            Raise(Sound.Landing, loudness * fullLoudnessRadius, loudness);
        }

        private void Raise(Sound sound, float radius, float loudness)
        {
            Vector3 position = transform.position;
            SurfaceMaterialSO surface = fallbackSurface;
            if (Physics.Raycast(position + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 0.6f, GameLayers.EnvironmentMask,
                    QueryTriggerInteraction.Ignore))
            {
                position = ground.point;
                surface = SurfaceIdentifier.Resolve(ground.collider, fallbackSurface);
            }

            AudioClip clip = Clip(surface, sound);
            if (clip == null) clip = Clip(fallbackSurface, sound); // a surface without footstep audio (flesh)
            EventBus<FootstepEvent>.Raise(new FootstepEvent
            {
                Source = transform, Position = position, Clip = clip, Loudness = loudness, Radius = radius,
            });
        }

        private static AudioClip Clip(SurfaceMaterialSO surface, Sound sound)
        {
            if (surface == null) return null;
            switch (sound)
            {
                case Sound.Crawl: return surface.RandomCrawlClip();
                case Sound.Landing: return surface.RandomLandingClip();
                default: return surface.RandomFootstepClip();
            }
        }

        private enum Sound
        {
            Step,
            Crawl,
            Landing,
        }
    }
}
