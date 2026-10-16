using System.Collections.Generic;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Hands the player's body and weapons over to a menu or station and back: pausing disables movement, weapons,
    /// throwables and interaction (which also frees the cursor); resuming re-enables only what pausing switched off.
    /// </summary>
    internal static class PlayerControl
    {
        public static void Pause(GameObject player, List<Behaviour> paused)
        {
            paused.Clear();
            Disable(player.GetComponent<RealisticWeaponController>(), paused);
            Disable(player.GetComponent<TacticalEquipmentSystem>(), paused);
            Disable(player.GetComponent<Interactor>(), paused);
            Disable(player.GetComponent<RealisticLocomotionController>(), paused);
        }

        public static void Resume(List<Behaviour> paused)
        {
            for (int i = 0; i < paused.Count; i++)
            {
                if (paused[i] != null) paused[i].enabled = true;
            }

            paused.Clear();
        }

        private static void Disable(Behaviour behaviour, List<Behaviour> paused)
        {
            if (behaviour == null || !behaviour.enabled) return;
            if (behaviour is RealisticWeaponController weapons) weapons.ReleaseTrigger();
            behaviour.enabled = false;
            paused.Add(behaviour);
        }
    }
}
