using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Player volume settings, 0-1. Effects play through the listener at <see cref="ListenerVolume"/> (master × effects);
    /// voices ignore the listener volume and play at <see cref="VoiceVolume"/> (master × voice), so each can be set alone.
    /// </summary>
    public static class AudioLevels
    {
        public static float Master { get; set; } = 1f;
        public static float Effects { get; set; } = 1f;
        public static float Voice { get; set; } = 1f;

        public static float ListenerVolume => Mathf.Clamp01(Master) * Mathf.Clamp01(Effects);
        public static float VoiceVolume => Mathf.Clamp01(Master) * Mathf.Clamp01(Voice);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Master = Effects = Voice = 1f;
    }
}
