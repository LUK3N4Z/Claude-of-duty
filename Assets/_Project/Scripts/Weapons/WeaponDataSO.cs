using System;
using FPS.Audio;
using FPS.Ballistics;
using UnityEngine;

namespace FPS.Weapons
{
    public enum FireMode
    {
        Semi,
        Burst,
        Auto,
    }

    public enum BoltType
    {
        /// <summary>Fires from a chambered round: magazine + 1 capacity, bolt may lock back on empty.</summary>
        ClosedBolt,
        /// <summary>Fires from a cocked open bolt stripping rounds straight from the magazine: no +1.</summary>
        OpenBolt,
        /// <summary>Every shot needs a manual bolt cycle to chamber the next round.</summary>
        ManualBoltAction,
    }

    /// <summary>
    /// Firearm mechanics and handling, separate from the ammunition (<see cref="CaliberDataSO"/>): physical
    /// properties, action, fire modes, accuracy, reload stage timings and recoil. Never references meshes; the model
    /// binds through <see cref="WeaponModelRig"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponData", menuName = "FPS/Weapons/Weapon Data")]
    public sealed class WeaponDataSO : ScriptableObject
    {
        public const string RecoilFieldName = nameof(recoil);

        [SerializeField] private string displayName = "Rifle";

        [Header("Physical")]
        [SerializeField, Min(0.1f)] private float massKg = 3.4f;
        [Tooltip("Handling quality 0-100: ADS speed, sway recovery, movement acceleration and manipulation speed.")]
        [SerializeField, Range(0f, 100f)] private float ergonomics = 55f;
        [SerializeField, Min(1f)] private float barrelLengthInches = 14.5f;

        [Header("Mechanics")]
        [SerializeField] private CaliberDataSO caliber;
        [SerializeField] private BoltType boltType = BoltType.ClosedBolt;
        [SerializeField] private FireMode[] fireModes = { FireMode.Semi, FireMode.Auto };
        [Tooltip("Cyclic rate (rounds/min).")]
        [SerializeField, Min(1f)] private float roundsPerMinute = 800f;
        [SerializeField, Min(2)] private int burstCount = 3;
        [Tooltip("Mechanical accuracy as group size (minutes of angle).")]
        [SerializeField, Min(0f)] private float moaAccuracy = 2.5f;
        [SerializeField, Min(1)] private int magazineCapacity = 30;
        [Tooltip("The bolt catch locks the bolt back when the last round leaves a closed-bolt weapon.")]
        [SerializeField] private bool boltHoldOpen = true;

        [Header("Ammunition")]
        [SerializeField, Min(0)] private int spareMagazines = 4;
        [Tooltip("Physical magazine spawned (pooled) when a speed reload drops the old one.")]
        [SerializeField] private Rigidbody droppedMagazinePrefab;

        [Header("Handling (s)")]
        [Tooltip("Hip-to-sight time at 50 ergonomics; better ergonomics shortens it.")]
        [SerializeField, Min(0.05f)] private float aimDownSightsTime = 0.3f;
        [SerializeField, Min(0f)] private float sprintToFireTime = 0.18f;
        [SerializeField, Min(0.05f)] private float holsterTime = 0.35f;
        [SerializeField, Min(0.05f)] private float drawTime = 0.45f;
        [SerializeField, Min(0.05f)] private float fireModeSwitchTime = 0.25f;
        [SerializeField, Min(0.05f)] private float magazineCheckTime = 0.8f;
        [Tooltip("Manual bolt cycle between shots (bolt-action only).")]
        [SerializeField, Min(0.05f)] private float manualCycleTime = 0.9f;

        [Header("Reload Stages (s)")]
        [SerializeField, Min(0.05f)] private float magazineRemoveTime = 0.7f;
        [Tooltip("Magazine removal when it is simply dropped (speed reload).")]
        [SerializeField, Min(0.05f)] private float speedMagazineRemoveTime = 0.25f;
        [SerializeField, Min(0.05f)] private float magazineInsertTime = 0.9f;
        [Tooltip("Charging handle or bolt release to chamber a round after an empty reload.")]
        [SerializeField, Min(0.05f)] private float boltCycleTime = 0.6f;

        [Header("Hip Pose")]
        [Tooltip("Stock contact point relative to the eye when held at the hip (m).")]
        [SerializeField] private Vector3 hipStockOffset = new Vector3(0.14f, -0.2f, -0.16f);
        [Tooltip("Weapon orientation at the hip (deg).")]
        [SerializeField] private Vector3 hipEuler = new Vector3(1.5f, -2f, -4f);

        [Header("Recoil")]
        [SerializeField] private RecoilProfileSO recoil;

        [Header("Audio")]
        [SerializeField] private WeaponAudioProfileSO audioProfile;

        [Header("Mass Distribution")]
        [Tooltip("Centre of mass of the bare weapon in weapon space (m).")]
        [SerializeField] private Vector3 centerOfMass = new Vector3(0f, -0.02f, 0.05f);

        [Header("Attachments")]
        [Tooltip("The build fitted when a loadout names no attachments.")]
        [SerializeField] private AttachmentDataSO[] defaultAttachments = new AttachmentDataSO[0];

        // Build results: written only on runtime clones by GunsmithAssembler; assets keep the neutral values.
        [NonSerialized] private float recoilVerticalMultiplier = 1f;
        [NonSerialized] private float recoilHorizontalMultiplier = 1f;
        [NonSerialized] private float recoilBackMultiplier = 1f;
        [NonSerialized] private float muzzleVelocityMultiplier = 1f;
        [NonSerialized] private float muzzleFlashMultiplier = 1f;
        [NonSerialized] private float adsTimeMultiplier = 1f;
        [NonSerialized] private float momentOfInertia = -1f;
        [NonSerialized] private Vector3 buildCenterOfMass;

        public string DisplayName => displayName;
        public float MassKg => massKg;
        public float Ergonomics => ergonomics;
        public float BarrelLengthInches => barrelLengthInches;
        public float BarrelLengthMeters => barrelLengthInches * 0.0254f;

        public CaliberDataSO Caliber => caliber;
        public BoltType BoltType => boltType;
        public int FireModeCount => fireModes.Length;
        public float CycleTime => 60f / roundsPerMinute;
        public int BurstCount => burstCount;
        public float MoaAccuracy => moaAccuracy;
        public int MagazineCapacity => magazineCapacity;
        public bool BoltHoldOpen => boltHoldOpen;
        public int SpareMagazines => spareMagazines;
        public Rigidbody DroppedMagazinePrefab => droppedMagazinePrefab;

        /// <summary>Effective hip-to-ADS time (s) after ergonomics and attachments.</summary>
        public float AdsTime => aimDownSightsTime * Mathf.Lerp(1.3f, 0.75f, ergonomics / 100f) * adsTimeMultiplier;
        /// <summary>Multiplier on every manipulation time: good ergonomics are faster.</summary>
        public float ManipulationScale => Mathf.Lerp(1.15f, 0.85f, ergonomics / 100f);
        public float SprintToFireTime => sprintToFireTime;
        public float HolsterTime => holsterTime * ManipulationScale;
        public float DrawTime => drawTime * ManipulationScale;
        public float FireModeSwitchTime => fireModeSwitchTime * ManipulationScale;
        public float MagazineCheckTime => magazineCheckTime * ManipulationScale;
        public float ManualCycleTime => manualCycleTime * ManipulationScale;
        public float MagazineRemoveTime => magazineRemoveTime * ManipulationScale;
        public float SpeedMagazineRemoveTime => speedMagazineRemoveTime * ManipulationScale;
        public float MagazineInsertTime => magazineInsertTime * ManipulationScale;
        public float BoltCycleTime => boltCycleTime * ManipulationScale;

        public Vector3 HipStockOffset => hipStockOffset;
        public Vector3 HipEuler => hipEuler;
        public RecoilProfileSO Recoil => recoil;
        public WeaponAudioProfileSO AudioProfile => audioProfile;
        /// <summary>Centre of mass of the bare weapon (receiver without attachments).</summary>
        public Vector3 BaseCenterOfMass => centerOfMass;
        /// <summary>Centre of mass of the build once its parts are fitted; the bare weapon's before that.</summary>
        public Vector3 CenterOfMass => momentOfInertia > 0f ? buildCenterOfMass : centerOfMass;
        public AttachmentDataSO[] DefaultAttachments => defaultAttachments;
        public float RecoilVerticalMultiplier => recoilVerticalMultiplier;
        public float RecoilHorizontalMultiplier => recoilHorizontalMultiplier;
        public float RecoilBackMultiplier => recoilBackMultiplier;
        public float MuzzleVelocityMultiplier => muzzleVelocityMultiplier;
        /// <summary>Visible muzzle flash scale: 0 suppressed, 1 bare muzzle.</summary>
        public float MuzzleFlashMultiplier => muzzleFlashMultiplier;
        /// <summary>Radius of gyration about the shoulder of a typical rifle (m): the bare-weapon inertia estimate.</summary>
        public const float TypicalGyrationRadius = 0.45f;

        /// <summary>
        /// Rotational inertia about the stock contact point (kg·m²): measured from the fitted parts, or estimated with
        /// <see cref="TypicalGyrationRadius"/> for a bare weapon. Long barrels and suppressors raise it and slow handling.
        /// </summary>
        public float MomentOfInertia => momentOfInertia > 0f ? momentOfInertia : massKg * TypicalGyrationRadius * TypicalGyrationRadius;

        internal void ApplyBuild(float mass, float ergonomicsValue, float barrelInches, float vertical, float horizontal, float back,
            float velocity, float flash, float adsTime, WeaponAudioProfileSO audio)
        {
            massKg = mass;
            ergonomics = Mathf.Clamp(ergonomicsValue, 0f, 100f);
            barrelLengthInches = barrelInches;
            recoilVerticalMultiplier = vertical;
            recoilHorizontalMultiplier = horizontal;
            recoilBackMultiplier = back;
            muzzleVelocityMultiplier = velocity;
            muzzleFlashMultiplier = flash;
            adsTimeMultiplier = adsTime;
            audioProfile = audio;
        }

        internal void ApplyMassDistribution(Vector3 center, float inertia)
        {
            buildCenterOfMass = center;
            momentOfInertia = inertia;
        }

        public FireMode GetFireMode(int index)
        {
            if (fireModes.Length == 0) throw new InvalidOperationException($"{name} has no fire modes.");
            return fireModes[Mathf.Clamp(index, 0, fireModes.Length - 1)];
        }
    }
}
