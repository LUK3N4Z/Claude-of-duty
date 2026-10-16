using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPS.Weapons
{
    /// <summary>
    /// Everything a saved profile can name: weapons (model + data), attachments and grenades, looked up by asset name
    /// (the id written into blueprints and loadouts). The workbench lists compatible parts from here.
    /// </summary>
    [CreateAssetMenu(fileName = "ArmoryCatalog", menuName = "FPS/Weapons/Armory Catalog")]
    public sealed class ArmoryCatalogSO : ScriptableObject
    {
        [Serializable]
        public struct WeaponEntry
        {
            public WeaponModelRig model;
            public WeaponDataSO data;
        }

        [SerializeField] private WeaponEntry[] weapons = new WeaponEntry[0];
        [SerializeField] private AttachmentDataSO[] attachments = new AttachmentDataSO[0];
        [SerializeField] private GrenadeDataSO[] grenades = new GrenadeDataSO[0];

        public IReadOnlyList<WeaponEntry> Weapons => weapons;
        public IReadOnlyList<AttachmentDataSO> Attachments => attachments;
        public IReadOnlyList<GrenadeDataSO> Grenades => grenades;

        /// <summary>The id a profile stores for <paramref name="asset"/>: its asset name.</summary>
        public static string Id(Object asset) => asset != null ? asset.name : string.Empty;

        public void Configure(WeaponEntry[] weaponEntries, AttachmentDataSO[] parts, GrenadeDataSO[] throwables)
        {
            weapons = weaponEntries;
            attachments = parts;
            grenades = throwables;
        }

        public bool TryGetWeapon(string id, out WeaponEntry entry)
        {
            for (int i = 0; i < weapons.Length; i++)
            {
                if (Id(weapons[i].data) != id) continue;
                entry = weapons[i];
                return true;
            }

            entry = default;
            return false;
        }

        public AttachmentDataSO Attachment(string id) => Find(attachments, id);
        public GrenadeDataSO Grenade(string id) => Find(grenades, id);

        /// <summary>
        /// Parts that fit <paramref name="slot"/> on <paramref name="model"/>: its receiver has the slot's mount, or (for
        /// muzzle devices) a barrel mount whose barrels carry the muzzle mount. Returns the number written.
        /// </summary>
        public int CompatibleParts(AttachmentSlot slot, WeaponModelRig model, List<AttachmentDataSO> results)
        {
            results.Clear();
            if (!HasMount(model, slot)) return 0;
            for (int i = 0; i < attachments.Length; i++)
            {
                if (attachments[i] != null && attachments[i].Slot == slot) results.Add(attachments[i]);
            }

            return results.Count;
        }

        public static bool HasMount(WeaponModelRig model, AttachmentSlot slot) =>
            model.FindNode(AttachmentDataSO.MountName(slot)) != null ||
            (slot == AttachmentSlot.Muzzle && model.FindNode(AttachmentDataSO.MountName(AttachmentSlot.Barrel)) != null);

        private static T Find<T>(T[] assets, string id) where T : Object
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && assets[i].name == id) return assets[i];
            }

            return null;
        }
    }
}
