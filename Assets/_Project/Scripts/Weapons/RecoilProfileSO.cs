using FPS.Core;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// Per-shot recoil physics for <see cref="PhysicalRecoilEngine"/>. Impulses are authored for
    /// <see cref="ReferenceMassKg"/>; a heavier build kicks proportionally less (momentum conservation).
    /// </summary>
    [CreateAssetMenu(fileName = "RecoilProfile", menuName = "FPS/Weapons/Recoil Profile")]
    public sealed class RecoilProfileSO : ScriptableObject
    {
        [Tooltip("Weapon mass these impulses were tuned for (kg).")]
        [SerializeField, Min(0.1f)] private float referenceMassKg = 3.4f;

        [Header("Weapon Impulse")]
        [Tooltip("Muzzle-rise angular impulse (deg/s).")]
        [SerializeField] private float verticalKick = 140f;
        [SerializeField, Range(0f, 1f)] private float verticalSpread = 0.15f;
        [Tooltip("Mean sideways torque (deg/s); the sign sets the drift direction.")]
        [SerializeField] private float horizontalBias = 12f;
        [Tooltip("Random sideways torque ± (deg/s).")]
        [SerializeField] private float horizontalSpread = 45f;
        [Tooltip("Random roll torque ± (deg/s).")]
        [SerializeField] private float rollKick = 25f;
        [Tooltip("Rearward shoulder impulse at the stock (m/s).")]
        [SerializeField] private float backKick = 0.9f;
        [Tooltip("Upward impulse at the stock (m/s).")]
        [SerializeField] private float upKick = 0.12f;

        [Header("Bolt Carrier")]
        [Tooltip("Time from shot to the carrier slamming home (s).")]
        [SerializeField, Min(0f)] private float boltCarrierDelay = 0.035f;
        [Tooltip("Forward jolt when the carrier returns to battery (m/s).")]
        [SerializeField] private float boltCarrierImpulse = 0.25f;
        [Tooltip("Muzzle-dip torque from the carrier returning (deg/s).")]
        [SerializeField] private float boltCarrierPitch = 18f;

        [Header("View")]
        [Tooltip("Permanent view climb per shot that the shooter has to pull back down (deg).")]
        [SerializeField] private float viewClimb = 0.35f;
        [Tooltip("Share of the sideways torque that also drifts the view.")]
        [SerializeField, Range(0f, 1f)] private float viewHorizontalFactor = 0.4f;
        [Tooltip("Camera concussion pitch impulse (deg/s). Kept small and damped to avoid nausea.")]
        [SerializeField] private float cameraKick = 30f;
        [Tooltip("Camera concussion rearward impulse (m/s).")]
        [SerializeField] private float cameraPush = 0.05f;

        [Header("Sustained Fire")]
        [SerializeField] private float kickRampPerShot = 0.05f;
        [SerializeField, Min(1f)] private float maxKickRamp = 1.4f;
        [Tooltip("Pause after which the ramp resets (s).")]
        [SerializeField] private float rampResetTime = 0.3f;

        [Header("Posture")]
        [SerializeField, Range(0f, 2f)] private float hipMultiplier = 1f;
        [SerializeField, Range(0f, 2f)] private float adsMultiplier = 0.75f;

        [Header("Springs")]
        [SerializeField] private ProceduralSpringSolver.Tuning weaponPositionSpring = new ProceduralSpringSolver.Tuning(10f, 0.55f);
        [SerializeField] private ProceduralSpringSolver.Tuning weaponRotationSpring = new ProceduralSpringSolver.Tuning(7f, 0.45f);
        [SerializeField] private ProceduralSpringSolver.Tuning cameraPositionSpring = new ProceduralSpringSolver.Tuning(12f, 1f);
        [SerializeField] private ProceduralSpringSolver.Tuning cameraRotationSpring = new ProceduralSpringSolver.Tuning(9f, 0.95f);

        public float ReferenceMassKg => referenceMassKg;
        public float VerticalKick => verticalKick;
        public float VerticalSpread => verticalSpread;
        public float HorizontalBias => horizontalBias;
        public float HorizontalSpread => horizontalSpread;
        public float RollKick => rollKick;
        public float BackKick => backKick;
        public float UpKick => upKick;
        public float BoltCarrierDelay => boltCarrierDelay;
        public float BoltCarrierImpulse => boltCarrierImpulse;
        public float BoltCarrierPitch => boltCarrierPitch;
        public float ViewClimb => viewClimb;
        public float ViewHorizontalFactor => viewHorizontalFactor;
        public float CameraKick => cameraKick;
        public float CameraPush => cameraPush;
        public float KickRampPerShot => kickRampPerShot;
        public float MaxKickRamp => maxKickRamp;
        public float RampResetTime => rampResetTime;
        public float HipMultiplier => hipMultiplier;
        public float AdsMultiplier => adsMultiplier;
        public ProceduralSpringSolver.Tuning WeaponPositionSpring => weaponPositionSpring;
        public ProceduralSpringSolver.Tuning WeaponRotationSpring => weaponRotationSpring;
        public ProceduralSpringSolver.Tuning CameraPositionSpring => cameraPositionSpring;
        public ProceduralSpringSolver.Tuning CameraRotationSpring => cameraRotationSpring;
    }
}
