using UnityEngine;

namespace FPS.Ballistics
{
    /// <summary>
    /// Physical surface for terminal ballistics and impact audio. Penetration resistance comes from density × hardness;
    /// shallow impacts below the ricochet angle may glance off.
    /// </summary>
    [CreateAssetMenu(fileName = "Surface", menuName = "FPS/Ballistics/Surface Material")]
    public sealed class SurfaceMaterialSO : ScriptableObject
    {
        /// <summary>Converts density (kg/m³) × hardness (0-100) into J per metre per mm² of bullet cross-section.</summary>
        public const float ResistanceScale = 0.007f;

        [SerializeField] private string displayName = "Concrete";

        [Header("Penetration")]
        [SerializeField, Min(1f)] private float densityKgM3 = 2400f;
        [Tooltip("Penetration hardness 0-100: how much the material resists being pushed through.")]
        [SerializeField, Range(0f, 100f)] private float hardness = 60f;
        [Tooltip("Largest random deflection of a bullet exiting this material (deg).")]
        [SerializeField, Min(0f)] private float exitDeflection = 4f;

        [Header("Ricochet")]
        [Tooltip("Impacts shallower than this (deg between path and surface) may ricochet; 0 disables.")]
        [SerializeField, Range(0f, 60f)] private float ricochetAngle = 20f;
        [SerializeField, Range(0f, 1f)] private float ricochetSpeedRetention = 0.55f;
        [Tooltip("Random spread around the mirror direction (deg).")]
        [SerializeField, Min(0f)] private float ricochetSpread = 6f;

        [Header("Audio")]
        [Tooltip("Spent cases hitting this surface; one is picked at random.")]
        [SerializeField] private AudioClip[] casingImpactClips = new AudioClip[0];

        public string DisplayName => displayName;
        public float DensityKgM3 => densityKgM3;
        public float Hardness => hardness;
        /// <summary>Energy per metre per mm² of cross-section (J/(m·mm²)).</summary>
        public float Resistance => ResistanceScale * densityKgM3 * hardness;
        public float ExitDeflection => exitDeflection;
        public float RicochetAngle => ricochetAngle;
        public float RicochetSpeedRetention => ricochetSpeedRetention;
        public float RicochetSpread => ricochetSpread;

        public AudioClip RandomCasingClip() =>
            casingImpactClips.Length == 0 ? null : casingImpactClips[Random.Range(0, casingImpactClips.Length)];
    }
}
