using System;
using System.Collections.Generic;
using System.IO;
using FPS.Weapons;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// The player's persistent profile as JSON in the persistent data folder: weapon blueprints, the loadout and the
    /// settings. Saving writes a temporary file and swaps it in, keeping the previous file as <c>.bak</c>, so a crash
    /// mid-write never loses the profile; an unreadable file is set aside as <c>.corrupt</c> and the backup (or the
    /// defaults) used instead. On the player it re-applies the saved loadout at start.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaveProfileManager : MonoBehaviour
    {
        public const string PrimaryBlueprint = "Primary";
        public const string SecondaryBlueprint = "Secondary";

        [SerializeField] private ArmoryCatalogSO catalog;
        [SerializeField] private string fileName = "profile.json";
        [Tooltip("Fit the saved loadout to this object's weapons at start.")]
        [SerializeField] private bool applyOnStart = true;

        private readonly List<AttachmentDataSO> parts = new List<AttachmentDataSO>(8);

        /// <summary>Folder used instead of <c>Application.persistentDataPath</c> (tests, tools); null for the default.</summary>
        public static string DirectoryOverride { get; set; }

        public ArmoryCatalogSO Catalog => catalog;
        public PlayerProfile Profile { get; private set; } = new PlayerProfile();
        public bool LoadedFromDisk { get; private set; }
        public string FilePath => Path.Combine(string.IsNullOrEmpty(DirectoryOverride) ? Application.persistentDataPath : DirectoryOverride, fileName);

        public void Configure(ArmoryCatalogSO armory, bool apply)
        {
            catalog = armory;
            applyOnStart = apply;
        }

        public static string ToJson(PlayerProfile profile) => JsonUtility.ToJson(profile, true);

        /// <summary>Parses a profile; throws <see cref="FormatException"/> on anything that is not one.</summary>
        public static PlayerProfile FromJson(string json)
        {
            PlayerProfile profile = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<PlayerProfile>(json);
            if (profile == null || profile.version <= 0) throw new FormatException("Not a player profile.");
            if (profile.blueprints == null) profile.blueprints = new List<WeaponBlueprint>();
            if (profile.loadout == null) profile.loadout = new LoadoutProfile();
            if (profile.settings == null) profile.settings = new GameSettings();
            for (int i = 0; i < profile.blueprints.Count; i++)
            {
                if (profile.blueprints[i].attachments == null) profile.blueprints[i].attachments = new List<string>();
            }

            return profile;
        }

        /// <summary>Reads the profile: the file, else its backup, else defaults.</summary>
        public void Load()
        {
            string path = FilePath;
            PlayerProfile loaded = TryRead(path) ?? TryRead(path + ".bak");
            LoadedFromDisk = loaded != null;
            Profile = loaded ?? new PlayerProfile();
        }

        /// <summary>Writes the profile atomically. False (and logged) when the disk refuses.</summary>
        public bool Save()
        {
            string path = FilePath;
            string temporary = path + ".tmp";
            string backup = path + ".bak";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, ToJson(Profile));
                if (!File.Exists(path))
                {
                    File.Move(temporary, path);
                    return true;
                }

                try
                {
                    File.Replace(temporary, path, backup);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(path, backup, true);
                    File.Delete(path);
                    File.Move(temporary, path);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogError($"Could not save the profile to '{path}': {exception.Message}", this);
                return false;
            }
        }

        /// <summary>Stores (or replaces) a blueprint called <paramref name="blueprintName"/>.</summary>
        public WeaponBlueprint Capture(string blueprintName, WeaponDataSO weapon, IReadOnlyList<AttachmentDataSO> attachments)
        {
            WeaponBlueprint blueprint = Profile.Blueprint(blueprintName);
            if (blueprint == null)
            {
                blueprint = new WeaponBlueprint { name = blueprintName };
                Profile.blueprints.Add(blueprint);
            }

            blueprint.weapon = ArmoryCatalogSO.Id(weapon);
            blueprint.attachments.Clear();
            for (int i = 0; i < attachments.Count; i++)
            {
                if (attachments[i] != null) blueprint.attachments.Add(ArmoryCatalogSO.Id(attachments[i]));
            }

            return blueprint;
        }

        /// <summary>
        /// Looks a blueprint up in the catalog. Unknown attachments (renamed or removed assets) are skipped with a
        /// warning; an unknown weapon fails.
        /// </summary>
        public bool Resolve(WeaponBlueprint blueprint, out ArmoryCatalogSO.WeaponEntry weapon, List<AttachmentDataSO> resolved)
        {
            resolved.Clear();
            weapon = default;
            if (blueprint == null || catalog == null || !catalog.TryGetWeapon(blueprint.weapon, out weapon)) return false;

            for (int i = 0; i < blueprint.attachments.Count; i++)
            {
                AttachmentDataSO part = catalog.Attachment(blueprint.attachments[i]);
                if (part != null) resolved.Add(part);
                else Debug.LogWarning($"Blueprint '{blueprint.name}': unknown attachment '{blueprint.attachments[i]}' skipped.", this);
            }

            return true;
        }

        /// <summary>Saves what the player carries now as the loadout (weapon slots as blueprints, grenades by id).</summary>
        public bool StoreLoadout(RealisticWeaponController weapons, TacticalEquipmentSystem equipment)
        {
            Profile.loadout.primary = StoreSlot(weapons, 0, PrimaryBlueprint);
            Profile.loadout.secondary = StoreSlot(weapons, 1, SecondaryBlueprint);
            if (equipment != null)
            {
                Profile.loadout.lethal = ArmoryCatalogSO.Id(equipment.Grenade(EquipmentSlot.Lethal));
                Profile.loadout.tactical = ArmoryCatalogSO.Id(equipment.Grenade(EquipmentSlot.Tactical));
            }

            return Save();
        }

        /// <summary>Fits the saved loadout. Returns the number of weapon slots applied.</summary>
        public int ApplyLoadout(RealisticWeaponController weapons, TacticalEquipmentSystem equipment)
        {
            int applied = 0;
            applied += ApplySlot(weapons, 0, Profile.loadout.primary);
            applied += ApplySlot(weapons, 1, Profile.loadout.secondary);
            if (equipment != null && catalog != null)
            {
                GrenadeDataSO lethal = catalog.Grenade(Profile.loadout.lethal);
                GrenadeDataSO tactical = catalog.Grenade(Profile.loadout.tactical);
                if (lethal != null) equipment.SetLoadout(EquipmentSlot.Lethal, lethal, equipment.Capacity(EquipmentSlot.Lethal));
                if (tactical != null) equipment.SetLoadout(EquipmentSlot.Tactical, tactical, equipment.Capacity(EquipmentSlot.Tactical));
            }

            return applied;
        }

        private void Awake() => Load();

        private void Start()
        {
            if (applyOnStart && LoadedFromDisk && TryGetComponent(out RealisticWeaponController weapons))
                ApplyLoadout(weapons, GetComponent<TacticalEquipmentSystem>());
        }

        private string StoreSlot(RealisticWeaponController weapons, int slot, string blueprintName)
        {
            if (!weapons.GetLoadoutWeapon(slot, out _, out WeaponDataSO data)) return string.Empty;
            Capture(blueprintName, data, weapons.GetAttachments(slot));
            return blueprintName;
        }

        private int ApplySlot(RealisticWeaponController weapons, int slot, string blueprintName)
        {
            if (string.IsNullOrEmpty(blueprintName) || !Resolve(Profile.Blueprint(blueprintName), out ArmoryCatalogSO.WeaponEntry weapon, parts)) return 0;
            weapons.SetLoadout(slot, weapon.model, weapon.data, parts);
            return 1;
        }

        private PlayerProfile TryRead(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return FromJson(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is IOException)
            {
                Debug.LogWarning($"Profile '{path}' is unreadable ({exception.Message}); kept as .corrupt.", this);
                try
                {
                    File.Copy(path, path + ".corrupt", true);
                }
                catch (IOException copyFailure)
                {
                    Debug.LogWarning($"Could not keep a copy of the unreadable profile: {copyFailure.Message}", this);
                }

                return null;
            }
        }
    }
}
