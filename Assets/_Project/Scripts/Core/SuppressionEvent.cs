namespace FPS.Core
{
    /// <summary>
    /// Raised on <see cref="EventBus{T}"/> when the local player is suppressed (supersonic near miss, close impact,
    /// blast). Drives heart rate, optic sway and later the flinch/blur feedback.
    /// </summary>
    public struct SuppressionEvent
    {
        /// <summary>0 = barely noticeable, 1 = a round cracking past the head.</summary>
        public float Intensity;
    }
}
