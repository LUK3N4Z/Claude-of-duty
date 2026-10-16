using UnityEngine;

namespace FPS.Audio
{
    /// <summary>Raised on <c>EventBus</c> for every discharge, for the acoustic and muzzle-effect systems.</summary>
    public struct GunshotEvent
    {
        public Vector3 Position;
        public Vector3 Direction;
        /// <summary>Root of whoever fired.</summary>
        public Transform Shooter;
        public WeaponAudioProfileSO Profile;
        /// <summary>Visible muzzle flash: 0 suppressed (or none), 1 bare muzzle. AI sees flashes inside its view.</summary>
        public float MuzzleFlash;
    }
}
