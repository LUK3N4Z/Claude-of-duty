using UnityEngine;

namespace FPS.Ballistics
{
    /// <summary>
    /// Raised on <c>EventBus</c> for every round leaving a barrel. Origin and direction are the true bore axis at the
    /// muzzle (with mechanical dispersion), never the screen centre. Ballistics simulates it; audio, VFX and AI react.
    /// </summary>
    public struct ShotFiredEvent
    {
        public Vector3 Origin;
        /// <summary>Unit direction of travel.</summary>
        public Vector3 Direction;
        /// <summary>Muzzle velocity for this barrel and caliber (m/s).</summary>
        public float MuzzleVelocity;
        public CaliberDataSO Caliber;
        /// <summary>Root of whoever fired, so the shooter's own hitboxes can be ignored.</summary>
        public Transform Shooter;
        /// <summary>Who is credited with the hits (hit markers); defaults to <see cref="Shooter"/> (grenade fragments credit the thrower).</summary>
        public Transform Attacker;
    }
}
