using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Raised on <see cref="EventBus{T}"/> when a listener (a player camera or AI head) is suppressed: a supersonic near
    /// miss, close impact or blast. Drives heart rate, flinch, focal blur and AI morale.
    /// </summary>
    public struct SuppressionEvent
    {
        /// <summary>The head/camera transform that was suppressed; consumers ignore events for other listeners.</summary>
        public Transform Listener;
        /// <summary>0 = barely noticeable, 1 = a round cracking past the head.</summary>
        public float Intensity;
        /// <summary>Closest point of the round's path to the listener.</summary>
        public Vector3 Point;
        /// <summary>Unit direction of travel of the round.</summary>
        public Vector3 Direction;
    }
}
