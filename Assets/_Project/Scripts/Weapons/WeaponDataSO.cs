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

        /// <summary>Effective hip-to-ADS time (s) after ergonomics.</summary>
        public float AdsTime => aimDownSightsTime * Mathf.Lerp(1.3f, 0.75f, ergonomics / 100f);
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

        public FireMode GetFireMode(int index)
        {
            if (fireModes.Length == 0) throw new InvalidOperationException($"{name} has no fire modes.");
            return fireModes[Mathf.Clamp(index, 0, fireModes.Length - 1)];
        }
    }
}
