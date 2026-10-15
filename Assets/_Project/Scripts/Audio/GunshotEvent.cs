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
    }
}
