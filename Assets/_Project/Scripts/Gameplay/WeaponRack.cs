using System;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Armory weapon rack: Interact swaps the weapon in one loadout slot for the next weapon on the rack (fresh,
    /// loaded, default build) and saves the loadout when the player has a profile. The prompt names the weapon the
    /// next use will hand over. Each option with a peg is shown on it (a world-layer copy of its default build, butt
    /// on the peg, muzzle along the peg's +Y, right side toward its +Z); the one the player carries is hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponRack : MonoBehaviour, IInteractable
    {
        [Serializable]
        public struct Option
        {
            public WeaponModelRig model;
            public WeaponDataSO data;
            [Tooltip("Where the weapon is shown: its butt rests here, muzzle along +Y, right side facing +Z. None = not shown.")]
            public Transform peg;
        }

        /// <summary>Seconds between searches for the player while none is found.</summary>
        private const float PlayerSearchInterval = 1f;

        [Tooltip("Loadout slot this rack fills: 0 primary, 1 secondary.")]
        [SerializeField, Range(0, 1)] private int slot;
        [SerializeField] private Option[] options = Array.Empty<Option>();

        private string prompt = "Take weapon";
        private int promptIndex = -1;
        private GameObject[] displays = Array.Empty<GameObject>();
        private RealisticWeaponController player;
        private WeaponDataSO shownCarried;
        private bool started, displaysValid;
        private float nextPlayerSearch;

        public string Prompt => prompt;
        public int Slot => slot;
        /// <summary>The weapon shown for option <paramref name="index"/> (null without a peg or model).</summary>
        public GameObject Display(int index) => index >= 0 && index < displays.Length ? displays[index] : null;

        public void Configure(int loadoutSlot, Option[] weapons)
        {
            slot = loadoutSlot;
            options = weapons ?? Array.Empty<Option>();
            promptIndex = -1;
            if (started) BuildDisplays(); // reconfigured after Start
        }

        private void Start()
        {
            started = true;
            BuildDisplays();
        }

        private void OnDestroy() => ClearDisplays();

        private void LateUpdate()
        {
            if (player == null)
            {
                if (Time.unscaledTime < nextPlayerSearch) return;
                nextPlayerSearch = Time.unscaledTime + PlayerSearchInterval;
                player = FindAnyObjectByType<RealisticWeaponController>();
                if (player == null) return;
            }

            player.GetLoadoutWeapon(slot, out _, out WeaponDataSO carried);
            if (displaysValid && carried == shownCarried) return;
            shownCarried = carried;
            displaysValid = true;
            for (int i = 0; i < displays.Length; i++)
            {
                if (displays[i] != null) displays[i].SetActive(options[i].data != carried);
            }
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

        private void ClearDisplays()
        {
            foreach (GameObject display in displays)
            {
                if (display != null) Destroy(display);
            }
        }

        private void BuildDisplays()
        {
            ClearDisplays();
            displays = new GameObject[options.Length];
            displaysValid = false;
            for (int i = 0; i < options.Length; i++)
            {
                Option option = options[i];
                if (option.peg == null || option.model == null || option.data == null) continue;

                WeaponModelRig weapon = Instantiate(option.model);
                weapon.name = option.model.name;
                weapon.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                WeaponDataSO build = GunsmithAssembler.Assemble(option.data, option.data.DefaultAttachments);
                GunsmithAssembler.AttachParts(weapon, build, option.data.DefaultAttachments);
                Destroy(build);
                SetLayer(weapon.transform, 0); // a prop: seen by every camera, no colliders

                // The rearmost point of the weapon rests on the peg, with the bore line over it.
                float rear = float.PositiveInfinity;
                foreach (MeshRenderer meshRenderer in weapon.GetComponentsInChildren<MeshRenderer>())
                {
                    if (meshRenderer.enabled) rear = Mathf.Min(rear, meshRenderer.bounds.min.z);
                }

                Vector3 bore = weapon.MuzzleSocket != null ? weapon.MuzzleSocket.position : Vector3.zero;
                Vector3 anchor = new Vector3(bore.x, bore.y, float.IsInfinity(rear) ? 0f : rear);
                Quaternion upright = Quaternion.LookRotation(Vector3.up, Vector3.right);
                weapon.transform.SetParent(option.peg, false);
                weapon.transform.SetLocalPositionAndRotation(-(upright * anchor), upright);
                displays[i] = weapon.gameObject;
            }
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
        }

        private void RefreshPrompt(int index)
        {
            if (index == promptIndex) return; // the prompt string is rebuilt only when it changes
            promptIndex = index;
            prompt = $"Take {options[index].data.DisplayName}";
        }
    }
}
