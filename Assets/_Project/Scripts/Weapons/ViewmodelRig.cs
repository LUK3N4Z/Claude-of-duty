using System;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// First-person weapon presentation chain and the weapon currently held. Hangs off <see cref="PlayerRig.HeadMotion"/>
    /// beside the camera, one writer per transform: ViewmodelRoot → InertiaPivot (<see cref="RealisticWeaponInertia"/>)
    /// → RecoilPivot (<see cref="PhysicalRecoilEngine"/>) → ActionPivot (<see cref="RealisticWeaponController"/>
    /// manipulation poses) → AdsPivot (<see cref="ProceduralADSAndOpticsController"/>) → weapon model. The weapon renders
    /// in the world (no overlay camera); barrel tucking keeps it out of walls.
    /// </summary>
    [DefaultExecutionOrder(-25)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig), typeof(RealisticLocomotionController))]
    public sealed class ViewmodelRig : MonoBehaviour
    {
        [SerializeField] private Transform viewmodelRoot;
        [SerializeField] private Transform inertiaPivot;
        [SerializeField] private Transform recoilPivot;
        [SerializeField] private Transform actionPivot;
        [SerializeField] private Transform adsPivot;

        private PlayerRig playerRig;
        private RealisticLocomotionController locomotion;

        /// <summary>Raised after every equip and unequip; <see cref="Weapon"/> and <see cref="Data"/> hold the new state.</summary>
        public event Action WeaponChanged;

        public WeaponModelRig Weapon { get; private set; }
        public WeaponDataSO Data { get; private set; }
        public PlayerRig PlayerRig => playerRig;
        public Transform ViewmodelRoot => viewmodelRoot;
        public Transform InertiaPivot => inertiaPivot;
        public Transform RecoilPivot => recoilPivot;
        public Transform ActionPivot => actionPivot;
        public Transform AdsPivot => adsPivot;

        /// <summary>Spawns <paramref name="prefab"/> as the held weapon, replacing the current one.</summary>
        public WeaponModelRig Equip(WeaponModelRig prefab, WeaponDataSO data)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (data == null) throw new ArgumentNullException(nameof(data));

            RemoveCurrent();
            Weapon = Instantiate(prefab, adsPivot, false);
            Weapon.name = prefab.name;
            Weapon.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            SetLayerRecursively(Weapon.transform, GameLayers.FirstPersonViewmodel);
            Data = data;
            locomotion.SetWeaponHandling(data.MassKg, data.Ergonomics);
            WeaponChanged?.Invoke();
            return Weapon;
        }

        public void Unequip()
        {
            if (Weapon == null) return;

            RemoveCurrent();
            locomotion.SetWeaponHandling(0f, 100f);
            WeaponChanged?.Invoke();
        }

        /// <summary>Adds the viewmodel chain and weapon systems to a built player. Idempotent; existing parts are kept.</summary>
        public static ViewmodelRig Build(PlayerRig player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            GameObject root = player.gameObject;
            bool wasActive = root.activeSelf;
            root.SetActive(false); // systems wake only once everything is wired

            if (!root.TryGetComponent(out ViewmodelRig rig)) rig = root.AddComponent<ViewmodelRig>();
            rig.viewmodelRoot = PlayerRig.EnsureChild(player.HeadMotion, "ViewmodelRoot");
            rig.inertiaPivot = PlayerRig.EnsureChild(rig.viewmodelRoot, "InertiaPivot");
            rig.recoilPivot = PlayerRig.EnsureChild(rig.inertiaPivot, "RecoilPivot");
            rig.actionPivot = PlayerRig.EnsureChild(rig.recoilPivot, "ActionPivot");
            Transform legacyAds = rig.recoilPivot.Find("AdsPivot"); // rigs built before ActionPivot existed
            if (legacyAds != null) legacyAds.SetParent(rig.actionPivot, false);
            rig.adsPivot = PlayerRig.EnsureChild(rig.actionPivot, "AdsPivot");
            SetLayerRecursively(rig.viewmodelRoot, GameLayers.FirstPersonViewmodel);

            if (!root.TryGetComponent(out RealisticWeaponInertia _)) root.AddComponent<RealisticWeaponInertia>();
            if (!root.TryGetComponent(out ProceduralADSAndOpticsController _)) root.AddComponent<ProceduralADSAndOpticsController>();
            if (!root.TryGetComponent(out PhysicalRecoilEngine _)) root.AddComponent<PhysicalRecoilEngine>();
            if (!root.TryGetComponent(out RealisticWeaponController _)) root.AddComponent<RealisticWeaponController>();
            if (!root.TryGetComponent(out MuzzleAndSmokeVFXController _)) root.AddComponent<MuzzleAndSmokeVFXController>();

            root.SetActive(wasActive);
            return rig;
        }

        private void Awake()
        {
            playerRig = GetComponent<PlayerRig>();
            locomotion = GetComponent<RealisticLocomotionController>();
        }

        private void RemoveCurrent()
        {
            if (Weapon != null)
            {
                Weapon.gameObject.SetActive(false);
                Destroy(Weapon.gameObject);
            }

            Weapon = null;
            Data = null;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
