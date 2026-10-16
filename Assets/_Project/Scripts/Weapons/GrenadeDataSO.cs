using FPS.Audio;
using FPS.Ballistics;
using UnityEngine;

namespace FPS.Weapons
{
    public enum GrenadeType
    {
        /// <summary>Steel fragments simulated as real projectiles, plus close-range blast overpressure.</summary>
        Fragmentation,
        /// <summary>Stun grenade: blinding flash and deafening bang, no fragments.</summary>
        Flashbang,
        /// <summary>Burning canister: a dense cloud that blocks sight (volumetric for players, obstruction for AI).</summary>
        Smoke,
    }

    /// <summary>A hand grenade: fuse, throw, and what the detonation does.</summary>
    [CreateAssetMenu(fileName = "Grenade", menuName = "FPS/Weapons/Grenade")]
    public sealed class GrenadeDataSO : ScriptableObject
    {
        [SerializeField] private string displayName = "Grenade";
        [SerializeField] private GrenadeType type = GrenadeType.Fragmentation;
        [Tooltip("Pooled body with a Rigidbody; +Z forward.")]
        [SerializeField] private ThrownGrenade prefab;
        [SerializeField, Min(0.05f)] private float massKg = 0.4f;
        [Tooltip("Spoon release to detonation (s). M67 frag: 4-5.5 s; M84 stun: 1-2.3 s.")]
        [SerializeField, Min(0.1f)] private float fuseTime = 4.5f;
        [Tooltip("Release speed of a full overhand throw (m/s).")]
        [SerializeField, Min(1f)] private float throwSpeed = 16f;

        [Header("Fragmentation")]
        [SerializeField] private CaliberDataSO fragmentCaliber;
        [SerializeField, Range(0, 256)] private int fragmentCount = 64;
        [SerializeField, Min(0f)] private float fragmentVelocity = 1300f;
        [Tooltip("Radius of lethal blast overpressure (m); fragments reach much further.")]
        [SerializeField, Min(0f)] private float blastRadius = 4f;
        [Tooltip("Overpressure damage at the centre, falling off with the square of distance.")]
        [SerializeField, Min(0f)] private float blastDamage = 140f;

        [Header("Stun")]
        [Tooltip("1 = standard stun grenade.")]
        [SerializeField, Min(0f)] private float flashIntensity = 1f;

        [Header("Smoke")]
        [Tooltip("Radius of the fully built cloud (m).")]
        [SerializeField, Min(0.5f)] private float smokeRadius = 4.5f;
        [Tooltip("Time from ignition to the full cloud (s).")]
        [SerializeField, Min(0.1f)] private float smokeBuildUp = 3f;
        [Tooltip("How long the cloud holds before it thins out (s).")]
        [SerializeField, Min(1f)] private float smokeDuration = 35f;
        [SerializeField, Min(0.1f)] private float smokeFade = 6f;
        [Tooltip("Optical depth per metre at full density: 1.5 hides anything a metre or two inside.")]
        [SerializeField, Min(0.01f)] private float smokeExtinction = 1.5f;

        [Header("Presentation")]
        [Tooltip("Detonation report: close crack, distant thump and environment tails.")]
        [SerializeField] private WeaponAudioProfileSO detonationAudio;
        [Tooltip("Peak luminous flux of the detonation flash (lm).")]
        [SerializeField, Min(0f)] private float flashLumen = 2000000f;
        [Tooltip("Radius within which listeners are suppressed by the blast (m).")]
        [SerializeField, Min(0f)] private float suppressionRadius = 15f;
        [SerializeField] private Material smokeMaterial;
        [SerializeField] private Color smokeColor = new Color(0.45f, 0.43f, 0.4f, 0.7f);

        public string DisplayName => displayName;
        public GrenadeType Type => type;
        public ThrownGrenade Prefab => prefab;
        public float MassKg => massKg;
        public float FuseTime => fuseTime;
        public float ThrowSpeed => throwSpeed;
        public CaliberDataSO FragmentCaliber => fragmentCaliber;
        public int FragmentCount => fragmentCount;
        public float FragmentVelocity => fragmentVelocity;
        public float BlastRadius => blastRadius;
        public float BlastDamage => blastDamage;
        public float FlashIntensity => flashIntensity;
        public float SmokeRadius => smokeRadius;
        public float SmokeBuildUp => smokeBuildUp;
        public float SmokeDuration => smokeDuration;
        public float SmokeFade => smokeFade;
        public float SmokeExtinction => smokeExtinction;
        public WeaponAudioProfileSO DetonationAudio => detonationAudio;
        public float FlashLumen => flashLumen;
        public float SuppressionRadius => suppressionRadius;
        public Material SmokeMaterial => smokeMaterial;
        public Color SmokeColor => smokeColor;

        /// <summary>Blast damage at <paramref name="distance"/> metres.</summary>
        public float BlastDamageAt(float distance)
        {
            if (blastRadius <= 0f || distance >= blastRadius) return 0f;
            float k = 1f - distance / blastRadius;
            return blastDamage * k * k;
        }
    }
}
