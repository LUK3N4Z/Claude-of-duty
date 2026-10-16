using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Whether the local player is looking through night vision. Set by the goggles (Player assembly); read by
    /// systems below it that change with NVG on: infrared lasers become visible, weapons switch to canted active
    /// aiming instead of sighting through the optic.
    /// </summary>
    public static class NightVision
    {
        public static bool Active { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Active = false;
    }
}
