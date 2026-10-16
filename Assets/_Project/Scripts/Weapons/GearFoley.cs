using FPS.Locomotion;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// First-person cloth and gear: the plate carrier's nylon rustles with how hard the body accelerates, how fast the
    /// view turns and how much the weapon swings on its inertia spring, and the sling swivels clink when the weapon is
    /// jerked (a snap turn or sudden stop) or on a hard landing. Reads the rig, writes no transform. Lives on the player
    /// root next to the viewmodel.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ViewmodelRig))]
    public sealed class GearFoley : MonoBehaviour
    {
        [Tooltip("Seamless nylon friction loop; its level follows the motion.")]
        [SerializeField] private AudioClip rustleLoop;
        [Tooltip("Sling swivel and buckle clinks; one is picked per jolt.")]
        [SerializeField] private AudioClip[] swivelClinks = new AudioClip[0];
        [SerializeField, Range(0f, 1f)] private float rustleVolume = 0.35f;
        [SerializeField, Range(0f, 1f)] private float clinkVolume = 0.5f;

        [Header("Motion")]
        [Tooltip("Body acceleration that rustles the carrier fully (m/s²).")]
        [SerializeField, Min(0.1f)] private float fullAcceleration = 8f;
        [Tooltip("View or weapon turn rate that rustles fully (deg/s).")]
        [SerializeField, Min(1f)] private float fullTurnRate = 220f;
        [Tooltip("Ground speed at which steady movement rustles at half level (m/s).")]
        [SerializeField, Min(0.1f)] private float halfRustleSpeed = 5f;
        [Tooltip("Rustle rise and fall rates (1/s).")]
        [SerializeField] private float attack = 14f, release = 3f;

        [Header("Clinks")]
        [Tooltip("Weapon swing angular acceleration on its inertia spring that knocks the swivels (deg/s²).")]
        [SerializeField, Min(1f)] private float clinkSwing = 6000f;
        [Tooltip("Landing speed that rattles the gear at full level (m/s).")]
        [SerializeField, Min(0.1f)] private float clinkLanding = 3f;
        [SerializeField, Min(0f)] private float clinkCooldown = 0.2f;

        private ViewmodelRig viewmodel;
        private PlayerRig rig;
        private RealisticLocomotionController locomotion;
        private AudioSource rustle, clink;
        private Vector3 lastVelocity;
        private Quaternion lastView, lastSwing;
        private float swingRate, smoothedAcceleration, nextClink;

        /// <summary>Rustle level 0-1.</summary>
        public float Intensity { get; private set; }
        /// <summary>Clinks played so far.</summary>
        public int Clinks { get; private set; }

        /// <summary>
        /// Rustle target from body acceleration (m/s²), turn rate (deg/s) and ground speed (m/s): the strongest motion
        /// wins; steady walking alone reaches half level at <paramref name="halfSpeed"/>.
        /// </summary>
        public static float RustleTarget(float acceleration, float turnRate, float speed, float fullAcceleration, float fullTurnRate, float halfSpeed) =>
            Mathf.Clamp01(Mathf.Max(Mathf.Max(acceleration / fullAcceleration, turnRate / fullTurnRate), 0.5f * speed / halfSpeed));

        /// <summary>Knocks the sling swivels with a jolt of <paramref name="strength"/> (0-1), respecting the cooldown.</summary>
        public void Clink(float strength)
        {
            if (Time.time < nextClink || swivelClinks.Length == 0 || clink == null) return;
            AudioClip clip = swivelClinks[Random.Range(0, swivelClinks.Length)];
            if (clip == null) return;

            nextClink = Time.time + clinkCooldown;
            clink.pitch = Random.Range(0.92f, 1.08f);
            clink.PlayOneShot(clip, clinkVolume * Mathf.Clamp01(strength));
            Clinks++;
        }

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            rig = GetComponent<PlayerRig>();
            locomotion = GetComponent<RealisticLocomotionController>();

            var mix = new GameObject("Gear Foley").transform;
            Transform ear = rig != null && rig.PlayerCamera != null ? rig.PlayerCamera.transform : transform;
            mix.SetParent(ear, false);
            mix.localPosition = new Vector3(0f, -0.35f, 0.05f); // chest
            rustle = Source(mix);
            rustle.loop = true;
            clink = Source(mix);
        }

        private void OnEnable()
        {
            if (locomotion != null) locomotion.Landed += OnLanded;
            lastVelocity = locomotion != null ? locomotion.Velocity : Vector3.zero;
            lastView = View();
            lastSwing = viewmodel.InertiaPivot != null ? viewmodel.InertiaPivot.localRotation : Quaternion.identity;
            if (rustleLoop != null)
            {
                rustle.clip = rustleLoop;
                rustle.volume = 0f;
                rustle.Play();
            }
        }

        private void OnDisable()
        {
            if (locomotion != null) locomotion.Landed -= OnLanded;
            rustle.Stop();
            Intensity = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 velocity = locomotion != null ? locomotion.Velocity : Vector3.zero;
            float acceleration = (velocity - lastVelocity).magnitude / dt;
            lastVelocity = velocity;
            smoothedAcceleration = Mathf.Lerp(smoothedAcceleration, acceleration, 1f - Mathf.Exp(-20f * dt)); // controller steps are noisy

            Quaternion view = View();
            float turnRate = Quaternion.Angle(lastView, view) / dt;
            lastView = view;

            float swing = 0f;
            if (viewmodel.InertiaPivot != null)
            {
                Quaternion pose = viewmodel.InertiaPivot.localRotation;
                float rate = Quaternion.Angle(lastSwing, pose) / dt;
                swing = Mathf.Abs(rate - swingRate) / dt; // angular acceleration of the weapon on its spring
                swingRate = rate;
                lastSwing = pose;
                turnRate = Mathf.Max(turnRate, rate);
            }

            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            float target = RustleTarget(smoothedAcceleration, turnRate, speed, fullAcceleration, fullTurnRate, halfRustleSpeed);
            float rate01 = target > Intensity ? attack : release;
            Intensity = Mathf.MoveTowards(Intensity, target, rate01 * dt);
            rustle.volume = Intensity * rustleVolume;
            rustle.pitch = 0.92f + 0.16f * Intensity;

            if (swing > clinkSwing) Clink(Mathf.Clamp01(swing / (clinkSwing * 2f)));
        }

        private void OnLanded(float impactSpeed) => Clink(Mathf.Clamp01(impactSpeed / clinkLanding));

        private Quaternion View() => rig != null && rig.PitchPivot != null ? rig.PitchPivot.rotation : transform.rotation;

        private static AudioSource Source(Transform parent)
        {
            var source = parent.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.2f;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
