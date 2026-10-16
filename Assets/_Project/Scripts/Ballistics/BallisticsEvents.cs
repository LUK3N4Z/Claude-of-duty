using System.Collections.Generic;
using UnityEngine;

namespace FPS.Ballistics
{
    public enum ImpactOutcome
    {
        Stopped,
        Penetrated,
        Ricocheted,
    }

    /// <summary>Raised on <c>EventBus</c> for every bullet-surface interaction (decals, impact audio, dust).</summary>
    public struct ProjectileImpactEvent
    {
        public Vector3 Point;
        public Vector3 Normal;
        /// <summary>Velocity at impact, before the outcome changed it (m/s).</summary>
        public Vector3 Velocity;
        public ImpactOutcome Outcome;
        public SurfaceMaterialSO Surface;
        public Collider Collider;
        public CaliberDataSO Caliber;
    }

    /// <summary>Raised on <c>EventBus</c> whenever a hit reaches a body zone.</summary>
    public struct DamageEvent
    {
        public TacticalHealthAndArmorSystem Target;
        public HitZone Zone;
        public float Damage;
        public Vector3 Point;
        public Vector3 Direction;
        /// <summary>Armor that took the hit, or null.</summary>
        public ArmorPieceSO Armor;
        /// <summary>The bullet defeated <see cref="Armor"/> (false when it was stopped or no armor covered the zone).</summary>
        public bool ArmorPenetrated;
        public bool Fatal;
        /// <summary>Who dealt it (shooter or thrower root), or null when unknown.</summary>
        public Transform Attacker;
        /// <summary>Where it came from: the muzzle the round left (a fragment's blast centre) or the blast centre.</summary>
        public Vector3 Source;
    }

    /// <summary>
    /// Heads that can hear supersonic rounds crack past. The simulation checks every projectile segment against them and
    /// raises a <c>SuppressionEvent</c> per listener per round.
    /// </summary>
    public static class NearMissListeners
    {
        private static readonly List<Transform> listeners = new List<Transform>(8);
        private static readonly List<Transform> owners = new List<Transform>(8);

        public static int Count => listeners.Count;

        /// <summary>Registers <paramref name="listener"/> (a head or camera); rounds fired by <paramref name="owner"/> are ignored.</summary>
        public static void Register(Transform listener, Transform owner)
        {
            if (listeners.Contains(listener)) return;
            listeners.Add(listener);
            owners.Add(owner);
        }

        public static void Unregister(Transform listener)
        {
            int index = listeners.IndexOf(listener);
            if (index < 0) return;
            listeners.RemoveAt(index);
            owners.RemoveAt(index);
        }

        public static Transform GetListener(int index) => listeners[index];
        public static Transform GetOwner(int index) => owners[index];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Clear()
        {
            listeners.Clear();
            owners.Clear();
        }
    }
}
