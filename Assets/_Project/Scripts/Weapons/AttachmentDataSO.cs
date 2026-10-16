using FPS.Audio;
using UnityEngine;

namespace FPS.Weapons
{
    public enum AttachmentSlot
    {
        Barrel,
        Muzzle,
        Optic,
        Underbarrel,
        Stock,
        Laser,
        Light,
    }

    /// <summary>
    /// A swappable part: its model (attached at the weapon's <c>{Slot}Mount</c>, which may itself be provided by another
    /// part, e.g. the barrel's MuzzleMount) and how it changes the weapon. Parts can carry their own sockets
    /// (MuzzleSocket, LeftHandIKGrip, StockPivotSocket) and a WeaponSight, which override the receiver's.
    /// </summary>
    [CreateAssetMenu(fileName = "Attachment", menuName = "FPS/Weapons/Attachment")]
    public sealed class AttachmentDataSO : ScriptableObject
    {
        [SerializeField] private string displayName = "Attachment";
        [SerializeField] private AttachmentSlot slot = AttachmentSlot.Muzzle;
        [Tooltip("Model placed at the mount; +Z forward along the barrel.")]
        [SerializeField] private GameObject prefab;

        [Header("Physical")]
        [SerializeField, Min(0f)] private float massKg = 0.1f;
        [Tooltip("Part centre of mass in its own space (m).")]
        [SerializeField] private Vector3 centerOfMass;
        [SerializeField] private float ergonomicsDelta;
        [Tooltip("Barrels only: length in inches (sets muzzle velocity).")]
        [SerializeField, Min(0f)] private float barrelLengthInches;

        [Header("Recoil Multipliers")]
        [SerializeField, Min(0f)] private float recoilVertical = 1f;
        [SerializeField, Min(0f)] private float recoilHorizontal = 1f;
        [SerializeField, Min(0f)] private float recoilBack = 1f;

        [Header("Muzzle")]
        [Tooltip("0 removes the visible flash (suppressor), < 1 reduces it (flash hider), > 1 a compensator's blast.")]
        [SerializeField, Min(0f)] private float flashMultiplier = 1f;
        [SerializeField, Min(0.5f)] private float velocityMultiplier = 1f;
        [Tooltip("Replaces the weapon's audio profile (suppressors).")]
        [SerializeField] private WeaponAudioProfileSO audioOverride;

        [Header("Handling")]
        [SerializeField, Min(0.1f)] private float adsTimeMultiplier = 1f;

        public string DisplayName => displayName;
        public AttachmentSlot Slot => slot;
        public GameObject Prefab => prefab;
        public float MassKg => massKg;
        public Vector3 CenterOfMass => centerOfMass;
        public float ErgonomicsDelta => ergonomicsDelta;
        public float BarrelLengthInches => barrelLengthInches;
        public float RecoilVertical => recoilVertical;
        public float RecoilHorizontal => recoilHorizontal;
        public float RecoilBack => recoilBack;
        public float FlashMultiplier => flashMultiplier;
        public float VelocityMultiplier => velocityMultiplier;
        public WeaponAudioProfileSO AudioOverride => audioOverride;
        public float AdsTimeMultiplier => adsTimeMultiplier;

        /// <summary>Name of the transform a part of <paramref name="slot"/> attaches to, e.g. "MuzzleMount".</summary>
        public static string MountName(AttachmentSlot slot) => SlotMountNames[(int)slot];

        private static readonly string[] SlotMountNames =
            { "BarrelMount", "MuzzleMount", "OpticMount", "UnderbarrelMount", "StockMount", "LaserMount", "LightMount" };
    }
}
