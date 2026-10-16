using System;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Armory weapon rack: Interact swaps the weapon in one loadout slot for the next weapon on the rack (fresh,
    /// loaded, default build) and saves the loadout when the player has a profile. The prompt names the weapon the
    /// next use will hand over.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponRack : MonoBehaviour, IInteractable
    {
        [Serializable]
        public struct Option
        {
            public WeaponModelRig model;
            public WeaponDataSO data;
        }

        [Tooltip("Loadout slot this rack fills: 0 primary, 1 secondary.")]
        [SerializeField, Range(0, 1)] private int slot;
        [SerializeField] private Option[] options = Array.Empty<Option>();

        private string prompt = "Take weapon";
        private int promptIndex = -1;

        public string Prompt => prompt;
        public int Slot => slot;

        public void Configure(int loadoutSlot, Option[] weapons)
        {
            slot = loadoutSlot;
            options = weapons ?? Array.Empty<Option>();
            promptIndex = -1;
        }

        public bool CanInteract(GameObject user)
        {
            if (options.Length == 0 || !user.TryGetComponent(out RealisticWeaponController weapons) || !weapons.enabled) return false;
            if (weapons.CurrentAction == WeaponAction.Holster || weapons.CurrentAction == WeaponAction.Equipment) return false;
            RefreshPrompt(NextIndex(weapons));
            return true;
        }

        public void Interact(GameObject user)
        {
            if (!CanInteract(user)) return;
            var weapons = user.GetComponent<RealisticWeaponController>();
            int index = NextIndex(weapons);
            weapons.SetLoadout(slot, options[index].model, options[index].data);
            if (user.TryGetComponent(out SaveProfileManager profiles)) profiles.StoreLoadout(weapons, user.GetComponent<TacticalEquipmentSystem>());
            RefreshPrompt(NextIndex(weapons));
        }

        /// <summary>The rack option after the one the slot carries now (the first when it carries none of them).</summary>
        public int NextIndex(RealisticWeaponController weapons)
        {
            int current = -1;
            if (weapons.GetLoadoutWeapon(slot, out _, out WeaponDataSO carried))
            {
                for (int i = 0; i < options.Length; i++)
                {
                    if (options[i].data == carried) current = i;
                }
            }

            return (current + 1) % options.Length;
        }

        private void RefreshPrompt(int index)
        {
            if (index == promptIndex) return; // the prompt string is rebuilt only when it changes
            promptIndex = index;
            prompt = $"Take {options[index].data.DisplayName}";
        }
    }
}
