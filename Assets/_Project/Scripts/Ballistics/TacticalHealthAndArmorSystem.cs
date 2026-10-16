using System;
using FPS.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FPS.Ballistics
{
    /// <summary>
    /// Health with multi-zone damage and plate-carrier armor. Plates and helmets test the bullet's energy-scaled
    /// penetration power against their protection (degraded by wear); a stopped round transfers blunt trauma, a defeated
    /// plate still absorbs part of the hit. Limb damage accumulates as injuries other systems read (legs slow sprinting,
    /// arms add sway).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TacticalHealthAndArmorSystem : MonoBehaviour
    {
        [Serializable]
        public struct ArmorSlot
        {
            public ArmorPieceSO piece;
            [Tooltip("Current durability; 0 on start means a new piece.")]
            public float durability;
        }

        public const string ArmorFieldName = nameof(armor);
        private const int ZoneCount = 7;

        [SerializeField, Min(1f)] private float maxHealth = 100f;

        [Header("Zone Damage Multipliers")]
        [SerializeField] private float headMultiplier = 3.5f;
        [SerializeField] private float thoraxMultiplier = 1.4f;
        [SerializeField] private float stomachMultiplier = 1.1f;
        [SerializeField] private float armMultiplier = 0.55f;
        [SerializeField] private float legMultiplier = 0.7f;

        [Header("Injury")]
        [Tooltip("Zone damage that fully disables a limb.")]
        [SerializeField, Min(1f)] private float limbInjuryCapacity = 40f;

        [Header("Armor")]
        [SerializeField] private ArmorSlot[] armor = new ArmorSlot[0];
        [Tooltip("Width of the penetrate-or-not band around the protection rating (logistic slope).")]
        [SerializeField, Min(0.1f)] private float penetrationSpread = 6f;

        private readonly float[] zoneDamage = new float[ZoneCount];

        /// <summary>Raised with the zone and final damage after every hit.</summary>
        public event Action<HitZone, float> Damaged;
        public event Action Died;

        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead => Health <= 0f;
        /// <summary>Worst leg injury, 0-1.</summary>
        public float LegInjury01 => Mathf.Clamp01(Mathf.Max(zoneDamage[(int)HitZone.LeftLeg], zoneDamage[(int)HitZone.RightLeg]) / limbInjuryCapacity);
        /// <summary>Worst arm injury, 0-1.</summary>
        public float ArmInjury01 => Mathf.Clamp01(Mathf.Max(zoneDamage[(int)HitZone.LeftArm], zoneDamage[(int)HitZone.RightArm]) / limbInjuryCapacity);
        public int ArmorCount => armor.Length;

        public float ArmorDurability(int index) => armor[index].durability;

        /// <summary>
        /// Resolves a bullet striking <paramref name="zone"/> while travelling along <paramref name="direction"/>, fired by
        /// <paramref name="attacker"/>. Returns true when armor stopped the round; false when it reached the body (it may still exit).
        /// </summary>
        public bool ApplyBulletHit(HitZone zone, Vector3 point, Vector3 direction, CaliberDataSO caliber, float speed, Transform attacker = null)
        {
            if (IsDead) return false;

            float damage = caliber.Damage * Mathf.Clamp(speed / caliber.ReferenceVelocity, 0.25f, 1.2f);
            int slot = CoveringArmor(zone, direction);
            bool penetrated = true;
            ArmorPieceSO piece = null;

            if (slot >= 0)
            {
                piece = armor[slot].piece;
                float wear = armor[slot].durability / piece.MaxDurability;
                float protection = piece.ProtectionRating * Mathf.Lerp(0.5f, 1f, wear);
                float energyRatio = caliber.KineticEnergy(speed) / caliber.KineticEnergy(caliber.ReferenceVelocity);
                float penetration = caliber.PenetrationPower * Mathf.Sqrt(energyRatio);
                penetrated = Random.value < PenetrationChance(penetration, protection, penetrationSpread);

                armor[slot].durability = Mathf.Max(0f, armor[slot].durability - damage * (penetrated ? 0.5f : 0.9f));
                damage *= penetrated ? 1f - piece.ResidualAbsorption : piece.BluntTransfer;
            }

            damage *= Multiplier(zone);
            zoneDamage[(int)zone] += damage;
            Health = Mathf.Max(0f, Health - damage);

            EventBus<DamageEvent>.Raise(new DamageEvent
            {
                Target = this,
                Zone = zone,
                Damage = damage,
                Point = point,
                Direction = direction,
                Armor = piece,
                ArmorPenetrated = piece != null && penetrated,
                Fatal = IsDead,
                Attacker = attacker,
            });
            Damaged?.Invoke(zone, damage);
            if (IsDead) Died?.Invoke();
            return !penetrated;
        }

        /// <summary>
        /// Damage armor cannot stop (blast overpressure, falls): applied to <paramref name="zone"/> with its multiplier.
        /// </summary>
        public void ApplyDamage(HitZone zone, float damage, Vector3 point, Vector3 direction, Transform attacker = null)
        {
            if (IsDead || damage <= 0f) return;

            damage *= Multiplier(zone);
            zoneDamage[(int)zone] += damage;
            Health = Mathf.Max(0f, Health - damage);
            EventBus<DamageEvent>.Raise(new DamageEvent
            {
                Target = this,
                Zone = zone,
                Damage = damage,
                Point = point,
                Direction = direction,
                Fatal = IsDead,
                Attacker = attacker,
            });
            Damaged?.Invoke(zone, damage);
            if (IsDead) Died?.Invoke();
        }

        /// <summary>Probability that a round with <paramref name="penetration"/> defeats <paramref name="protection"/>.</summary>
        public static float PenetrationChance(float penetration, float protection, float spread) =>
            1f / (1f + Mathf.Exp(-(penetration - protection) / spread));

        /// <summary>Restores full health, clears injuries and replaces worn armor.</summary>
        public void ResetState()
        {
            Health = maxHealth;
            Array.Clear(zoneDamage, 0, ZoneCount);
            for (int i = 0; i < armor.Length; i++)
            {
                if (armor[i].piece != null) armor[i].durability = armor[i].piece.MaxDurability;
            }
        }

        private void Awake()
        {
            Health = maxHealth;
            for (int i = 0; i < armor.Length; i++)
            {
                if (armor[i].piece != null && armor[i].durability <= 0f) armor[i].durability = armor[i].piece.MaxDurability;
            }
        }

        private int CoveringArmor(HitZone zone, Vector3 direction)
        {
            // How square the round arrives on the body's front: +1 dead ahead, -1 from behind.
            float frontal = -Vector3.Dot(direction, transform.forward);
            for (int i = 0; i < armor.Length; i++)
            {
                ArmorPieceSO piece = armor[i].piece;
                if (piece == null || armor[i].durability <= 0f) continue;

                switch (piece.Coverage)
                {
                    case ArmorCoverage.FrontPlate when (zone == HitZone.Thorax || zone == HitZone.Stomach) && frontal > 0f:
                    case ArmorCoverage.BackPlate when (zone == HitZone.Thorax || zone == HitZone.Stomach) && frontal <= 0f:
                    case ArmorCoverage.Helmet when zone == HitZone.Head && frontal < 0.6f: // straight-on shots hit the face
                        return i;
                }
            }

            return -1;
        }

        private float Multiplier(HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return headMultiplier;
                case HitZone.Thorax: return thoraxMultiplier;
                case HitZone.Stomach: return stomachMultiplier;
                case HitZone.LeftArm:
                case HitZone.RightArm: return armMultiplier;
                default: return legMultiplier;
            }
        }
    }
}
