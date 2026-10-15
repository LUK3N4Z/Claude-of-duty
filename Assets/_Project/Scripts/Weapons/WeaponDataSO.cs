using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// Firearm handling data: physical properties, aim handling and recoil. Firing mechanics (rate, fire modes,
    /// bolt type, caliber) join in Milestone 4. Never references meshes; the model binds via <see cref="WeaponModelRig"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponData", menuName = "FPS/Weapons/Weapon Data")]
    public sealed class WeaponDataSO : ScriptableObject
    {
        public const string RecoilFieldName = nameof(recoil);

        [SerializeField] private string displayName = "Rifle";

        [Header("Physical")]
        [SerializeField, Min(0.1f)] private float massKg = 3.4f;
        [Tooltip("Handling quality 0-100: ADS speed, sway recovery and movement acceleration.")]
        [SerializeField, Range(0f, 100f)] private float ergonomics = 55f;
        [SerializeField, Min(1f)] private float barrelLengthInches = 14.5f;

        [Header("Handling")]
        [Tooltip("Hip-to-sight time at 50 ergonomics (s); better ergonomics shortens it.")]
        [SerializeField, Min(0.05f)] private float aimDownSightsTime = 0.3f;
        [Tooltip("Stock contact point relative to the eye when held at the hip (m).")]
        [SerializeField] private Vector3 hipStockOffset = new Vector3(0.13f, -0.2f, -0.02f);
        [Tooltip("Weapon orientation at the hip (deg).")]
        [SerializeField] private Vector3 hipEuler = new Vector3(1.5f, -2f, -4f);

        [Header("Recoil")]
        [SerializeField] private RecoilProfileSO recoil;

        public string DisplayName => displayName;
        public float MassKg => massKg;
        public float Ergonomics => ergonomics;
        public float BarrelLengthInches => barrelLengthInches;
        public float BarrelLengthMeters => barrelLengthInches * 0.0254f;
        /// <summary>Effective hip-to-ADS time (s) after ergonomics.</summary>
        public float AdsTime => aimDownSightsTime * Mathf.Lerp(1.3f, 0.75f, ergonomics / 100f);
        public Vector3 HipStockOffset => hipStockOffset;
        public Vector3 HipEuler => hipEuler;
        public RecoilProfileSO Recoil => recoil;
    }
}
