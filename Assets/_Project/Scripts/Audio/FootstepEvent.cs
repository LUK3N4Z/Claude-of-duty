using UnityEngine;

namespace FPS.Audio
{
    /// <summary>Raised on <c>EventBus</c> for every foot strike, crawl movement or landing of a body.</summary>
    public struct FootstepEvent
    {
        /// <summary>Root of whoever stepped.</summary>
        public Transform Source;
        /// <summary>Contact point on the ground.</summary>
        public Vector3 Position;
        /// <summary>The surface's step, crawl or landing sound; null when the surface has none.</summary>
        public AudioClip Clip;
        /// <summary>Source loudness 0-1 (pace, stance, fall speed).</summary>
        public float Loudness;
        /// <summary>Distance the step carries (m): the footstep radius AI hearing uses.</summary>
        public float Radius;
    }
}
