using UnityEngine;

namespace FPS.Ballistics
{
    public enum DragModel
    {
        /// <summary>Flat-base reference projectile; pistol and older bullets.</summary>
        G1,
        /// <summary>Boat-tail reference projectile; modern long-range rifle bullets.</summary>
        G7,
    }

    /// <summary>
    /// Ammunition ballistics, separate from the firearm: projectile, muzzle velocity versus barrel length, aerodynamic
    /// and terminal properties. Weapons reference one; the ballistics simulation reads it per shot.
    /// </summary>
    [CreateAssetMenu(fileName = "Caliber", menuName = "FPS/Ballistics/Caliber Data")]
    public sealed class CaliberDataSO : ScriptableObject
    {
        [SerializeField] private string displayName = "5.56x45mm NATO";

        [Header("Projectile")]
        [SerializeField, Min(0.1f)] private float bulletMassGrams = 4f;
        [SerializeField, Min(1f)] private float bulletDiameterMm = 5.7f;

        [Header("Muzzle Velocity")]
        [Tooltip("Muzzle velocity from the reference barrel (m/s).")]
        [SerializeField, Min(1f)] private float referenceVelocity = 940f;
        [SerializeField, Min(1f)] private float referenceBarrelInches = 20f;
        [Tooltip("Velocity gained per inch of extra barrel, lost per inch less (m/s).")]
        [SerializeField, Min(0f)] private float velocityPerInch = 7.5f;

        [Header("Aerodynamics")]
        [SerializeField] private DragModel dragModel = DragModel.G7;
        [Tooltip("Ballistic coefficient against the selected drag model (lb/in²).")]
        [SerializeField, Min(0.01f)] private float ballisticCoefficient = 0.151f;

        [Header("Terminal")]
        [Tooltip("Armor penetration rating, 0-100, compared against armor and material hardness.")]
        [SerializeField, Range(0f, 100f)] private float penetrationPower = 40f;
        [Tooltip("Flesh damage at muzzle velocity before armor and range falloff.")]
        [SerializeField, Min(0f)] private float damage = 35f;

        public string DisplayName => displayName;
        public float BulletMassKg => bulletMassGrams * 0.001f;
        public float BulletDiameterMeters => bulletDiameterMm * 0.001f;
        public float ReferenceVelocity => referenceVelocity;
        public DragModel DragModel => dragModel;
        public float BallisticCoefficient => ballisticCoefficient;
        public float PenetrationPower => penetrationPower;
        public float Damage => damage;

        /// <summary>Muzzle velocity (m/s) from a barrel of the given length; never below half the reference.</summary>
        public float MuzzleVelocity(float barrelInches) =>
            Mathf.Max(referenceVelocity * 0.5f, referenceVelocity + (barrelInches - referenceBarrelInches) * velocityPerInch);

        /// <summary>Kinetic energy (J) at <paramref name="velocity"/> m/s.</summary>
        public float KineticEnergy(float velocity) => 0.5f * BulletMassKg * velocity * velocity;
    }
}
