using System.Collections.Generic;
using FPS.Core;
using FPS.Gameplay;
using FPS.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using static FPS.EditorTools.TestWeaponBuilder;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// The armory hub: the catalog every saved blueprint and loadout names, dynamic resolution enabled in the HDRP asset
    /// (so the settings menu's TAAU / FSR2 / DLSS hooks have something to drive), and the Armory scene — a lit workshop
    /// with the gunsmith workbench (turntable, bench camera), ammunition and equipment, opening straight onto a 30 m
    /// indoor range with target boards and armored dummies.
    /// </summary>
    public static class ArmoryContentBuilder
    {
        private const string CatalogPath = "Assets/_Project/Data/Armory/ArmoryCatalog.asset";
        private const string HDRPAssetPath = "Assets/_Project/Settings/Rendering/HDRenderPipelineAsset.asset";
        internal const string ArmoryScene = "Assets/_Project/Scenes/Armory.unity";
        private const string ScenesFolder = "Assets/_Project/Scenes";

        /// <summary>Creates or refreshes the catalog: both test weapons, every attachment, the hand grenades.</summary>
        internal static ArmoryCatalogSO Catalog()
        {
            ProjectSetupWizard.EnsureFolder("Assets/_Project/Data/Armory");
            var attachments = new List<AttachmentDataSO>();
            foreach (string guid in AssetDatabase.FindAssets("t:AttachmentDataSO", new[] { "Assets/_Project/Data/Weapons/Attachments" }))
                attachments.Add(AssetDatabase.LoadAssetAtPath<AttachmentDataSO>(AssetDatabase.GUIDToAssetPath(guid)));

            var weapons = new List<ArmoryCatalogSO.WeaponEntry>();
            foreach (string weaponName in new[] { CarbineName, MarksmanName, BattleRifleName, SidearmName })
            {
                var model = AssetDatabase.LoadAssetAtPath<WeaponModelRig>(PrefabPath(weaponName));
                var data = AssetDatabase.LoadAssetAtPath<WeaponDataSO>(DataPath(weaponName));
                if (model != null && data != null) weapons.Add(new ArmoryCatalogSO.WeaponEntry { model = model, data = data });
            }

            var grenades = new List<GrenadeDataSO>();
            foreach (string grenadeName in new[] { EquipmentContentBuilder.FragName, EquipmentContentBuilder.FlashName, EquipmentContentBuilder.SmokeName })
            {
                GrenadeDataSO grenade = EquipmentContentBuilder.Grenade(grenadeName);
                if (grenade != null) grenades.Add(grenade);
            }

            ArmoryCatalogSO catalog = LoadOrCreate<ArmoryCatalogSO>(CatalogPath);
            catalog.Configure(weapons.ToArray(), attachments.ToArray(), grenades.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>
        /// Turns HDRP dynamic resolution on (hardware scaling, 50-100 %, TAA upscale, DLSS then FSR2 when present).
        /// Cameras keep rendering natively until the settings menu allows scaling on them.
        /// </summary>
        internal static void EnableUpscalers()
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(HDRPAssetPath);
            if (asset == null) return;
            Configure(asset, so =>
            {
                const string root = "m_RenderPipelineSettings.dynamicResolutionSettings.";
                Set(so, root + "enabled", true);
                Set(so, root + "minPercentage", 50f);
                Set(so, root + "maxPercentage", 100f);
                Set(so, root + "upsampleFilter", (int)DynamicResUpscaleFilter.TAAU);
                Set(so, root + "enableDLSS", true);
                SerializedProperty upscalers = Property(so, root + "advancedUpscalersByPriority");
                upscalers.arraySize = 2;
                upscalers.GetArrayElementAtIndex(0).intValue = (int)AdvancedUpscalers.DLSS;
                upscalers.GetArrayElementAtIndex(1).intValue = (int)AdvancedUpscalers.FSR2;
                SerializedProperty names = Property(so, root + "advancedUpscalerNames");
                names.arraySize = 2;
                names.GetArrayElementAtIndex(0).stringValue = AdvancedUpscalers.DLSS.ToString();
                names.GetArrayElementAtIndex(1).stringValue = AdvancedUpscalers.FSR2.ToString();
            });
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/FPS Builder/8. Build Armory", priority = 8)]
        public static void BuildArmory()
        {
            GameObject playerPrefab = PlayerBuilder.CreateOrUpdatePlayerPrefab();
            if (playerPrefab == null) return;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ArmoryCatalogSO catalog = Catalog();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out Camera _)) Object.DestroyImmediate(root);
            }

            Transform level = new GameObject("Armory").transform;
            PlayerBuilder.AtmosphereVolume(level);
            Workshop(level);
            Workbench(level, catalog);
            DeploymentTable(level);
            IndoorRange(level);
            WorldLook.Interior(level, "Workshop", new Vector3(0f, 1.5f, 0f), new Vector3(12f, 3f, 10f));
            WorldLook.Interior(level, "Indoor Range", new Vector3(21.5f, 1.5f, 0f), new Vector3(31f, 3f, 6f));
            EnvironmentMaterials.Dress(level, EnvironmentSurface.Concrete);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.SetPositionAndRotation(new Vector3(-3f, 0f, -2.5f), Quaternion.Euler(0f, 20f, 0f));

            EditorSceneManager.SaveScene(scene, ArmoryScene);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(entry => entry.path == ArmoryScene);
            scenes.Insert(0, new EditorBuildSettingsScene(ArmoryScene, true)); // the hub opens the game
            foreach (DeploymentStation.Operation operation in Operations())
            {
                string path = $"{ScenesFolder}/{operation.scene}.unity";
                if (!scenes.Exists(entry => entry.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true)); // every deployable scene ships
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[FPS Builder] {ArmoryScene} built.");
        }

        private static void Workshop(Transform level)
        {
            GameModeSceneBuilder.Block(level, "Floor", new Vector3(14f, -0.5f, 0f), new Vector3(46f, 1f, 14f));
            GameModeSceneBuilder.Block(level, "Workshop Roof", new Vector3(0f, 3.15f, 0f), new Vector3(12.3f, 0.3f, 10.3f));
            GameModeSceneBuilder.Block(level, "Workshop Wall N", new Vector3(0f, 1.5f, 5f), new Vector3(12.3f, 3f, 0.3f), "Brick");
            GameModeSceneBuilder.Block(level, "Workshop Wall S", new Vector3(0f, 1.5f, -5f), new Vector3(12.3f, 3f, 0.3f), "Brick");
            GameModeSceneBuilder.Block(level, "Workshop Wall W", new Vector3(-6f, 1.5f, 0f), new Vector3(0.3f, 3f, 10.3f), "Brick");
            // East wall with the doorway into the range.
            GameModeSceneBuilder.Block(level, "Workshop Wall E North", new Vector3(6f, 1.5f, 3.05f), new Vector3(0.3f, 3f, 4.2f), "Brick");
            GameModeSceneBuilder.Block(level, "Workshop Wall E South", new Vector3(6f, 1.5f, -3.05f), new Vector3(0.3f, 3f, 4.2f), "Brick");
            // Weapon racks on the west wall: the primary rack swaps carbine / battle rifle, the secondary marksman / sidearm.
            ProceduralProps.PlaceWeaponRack(level, "Primary Rack", new Vector3(-5.6f, 0.8f, -0.8f), Quaternion.Euler(0f, 90f, 0f), 0, CarbineName, BattleRifleName);
            ProceduralProps.PlaceWeaponRack(level, "Secondary Rack", new Vector3(-5.6f, 0.8f, 0.8f), Quaternion.Euler(0f, 90f, 0f), 1, MarksmanName, SidearmName);

            foreach (Vector3 lamp in new[] { new Vector3(-3f, 2.85f, 0f), new Vector3(2.5f, 2.85f, 0f), new Vector3(0f, 2.85f, 3f) })
                Lamp(level, "Workshop Lamp", lamp, 1800f);

            BoxCollider crate = Crate(level, "Ammo Crate", new Vector3(4.5f, 0.3f, -3.8f));
            ProceduralProps.Dress(crate, "Prop_AmmoCrate");
            Interactable(crate.transform, "Resupply (interact)", new Vector3(1.1f, 1.2f, 1.2f)).AddComponent<AmmoCrate>();
            BoxCollider locker = Crate(level, "Equipment Locker", new Vector3(4.5f, 0.9f, 3.9f), new Vector3(0.9f, 1.8f, 0.5f));
            ProceduralProps.Dress(locker, "Prop_Locker");
            ProceduralProps.Equipment("Clacker_M57", Socket(level, "Clacker (display)", new Vector3(4.2f, 1.82f, 3.85f), Quaternion.Euler(0f, 20f, 0f)), 0);
            Interactable(locker.transform, "Swap tactical (interact)", new Vector3(1.4f, 1.1f, 1.6f)).AddComponent<EquipmentLocker>().Configure(
                EquipmentContentBuilder.Grenade(EquipmentContentBuilder.FlashName), EquipmentContentBuilder.Grenade(EquipmentContentBuilder.SmokeName), 2);
        }

        private static void Workbench(Transform level, ArmoryCatalogSO catalog)
        {
            GameModeSceneBuilder.Block(level, "Workbench Table", new Vector3(0f, 0.45f, 3.6f), new Vector3(2.6f, 0.9f, 1.1f), "Wood");
            ProceduralProps.Dress(level.Find("Workbench Table"), "Prop_Workbench");
            Transform turntable = new GameObject("Turntable").transform;
            turntable.SetParent(level, false);
            turntable.position = new Vector3(0f, 1.2f, 3.6f);

            var cameraObject = new GameObject("Bench Camera");
            cameraObject.transform.SetParent(level, false);
            cameraObject.transform.position = new Vector3(0f, 1.55f, 2.35f);
            cameraObject.transform.rotation = Quaternion.LookRotation(turntable.position - cameraObject.transform.position);
            var benchCamera = cameraObject.AddComponent<Camera>();
            benchCamera.fieldOfView = 42f;
            benchCamera.nearClipPlane = 0.05f;
            benchCamera.depth = 5f;
            benchCamera.enabled = false;
            cameraObject.AddComponent<HDAdditionalCameraData>();
            Lamp(level, "Bench Lamp", new Vector3(0f, 2.4f, 3.2f), 1200f);

            var station = new GameObject("Gunsmith Workbench (interact)") { layer = GameLayers.Interactable };
            station.transform.SetParent(level, false);
            // Covers the bench top and the space in front of it, so looking at the bench from any side opens it.
            station.transform.position = new Vector3(0f, 1f, 3.45f);
            var trigger = station.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.8f, 0.9f, 1.6f);
            station.AddComponent<GunsmithWorkbenchController>().Configure(catalog, benchCamera, turntable,
                UnlitMaterial("Workbench Node", new Color(0.2f, 0.85f, 1f), null, false), UnlitMaterial("Workbench Node Selected", new Color(1f, 0.6f, 0.1f), null, false));
        }

        /// <summary>The operations on the deployment table, with their map schematics (rendered by the scene builders).</summary>
        private static DeploymentStation.Operation[] Operations() => new[]
        {
            Operation("Tactical Raid", "Mode_Raid",
                "One life. Download the intel at both terminals in the warehouse and the compound; every terminal you finish alerts the garrison. Then hold the extraction zone until the helicopter arrives."),
            Operation("Search & Destroy", "Mode_SearchAndDestroy",
                "Rounds, no respawns. Attackers plant the charge at a site; defenders stop them or defuse it. Sides swap at half time."),
            Operation("Hardpoint", "Mode_Hardpoint",
                "Respawns. Hold the active zone with nobody else in it to score; the zone moves every minute."),
            Operation("Locomotion Test Range", "LocomotionTestRange",
                "No objectives. Ramps, ice, ledges and tunnels, the traversal course, targets out to 200 m, the shoot house and Squad Alpha's firefight yard."),
        };

        private static DeploymentStation.Operation Operation(string title, string scene, string briefing) => new DeploymentStation.Operation
        {
            title = title,
            scene = scene,
            briefing = briefing,
            preview = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPreviewBuilder.PathFor(scene)),
        };

        /// <summary>The deployment table by the south wall: a hologram of the selected map, the station camera over it.</summary>
        private static void DeploymentTable(Transform level)
        {
            var center = new Vector3(-0.5f, 0f, -3.6f);
            GameModeSceneBuilder.Block(level, "Deployment Table", center + new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.9f, 1.2f), "Metal");
            ProceduralProps.Dress(level.Find("Deployment Table"), "Prop_Workbench");

            GameObject hologram = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hologram.name = "Deployment Hologram";
            Object.DestroyImmediate(hologram.GetComponent<Collider>());
            hologram.transform.SetParent(level, false);
            hologram.transform.SetPositionAndRotation(center + new Vector3(0f, 0.97f, 0f), Quaternion.Euler(90f, 0f, 0f));
            hologram.transform.localScale = new Vector3(1.05f, 1.05f, 1f);
            hologram.GetComponent<MeshRenderer>().sharedMaterial = HologramMaterial();
            Lamp(level, "Deployment Lamp", center + new Vector3(0f, 2.4f, 0.5f), 600f);

            var cameraObject = new GameObject("Deployment Camera");
            cameraObject.transform.SetParent(level, false);
            cameraObject.transform.position = center + new Vector3(0f, 2.1f, 1.3f);
            cameraObject.transform.rotation = Quaternion.LookRotation(center + new Vector3(0f, 0.95f, 0f) - cameraObject.transform.position);
            var view = cameraObject.AddComponent<Camera>();
            view.fieldOfView = 50f;
            view.nearClipPlane = 0.05f;
            view.depth = 5f;
            view.enabled = false;
            cameraObject.AddComponent<HDAdditionalCameraData>();

            var station = new GameObject("Deployment Table (interact)") { layer = GameLayers.Interactable };
            station.transform.SetParent(level, false);
            // Covers the table top and the space in front of it, so looking at the hologram from any side opens it.
            station.transform.position = center + new Vector3(0f, 1f, 0.15f);
            var trigger = station.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.4f, 0.9f, 1.6f);
            station.AddComponent<DeploymentStation>().Configure(Operations(), view, hologram.GetComponent<MeshRenderer>());
        }

        /// <summary>Additive cyan HDRP/Unlit for the map hologram; the station feeds the map texture per operation.</summary>
        private static Material HologramMaterial()
        {
            const string path = "Assets/_Project/Art/UI/DeploymentHologram.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            ProjectSetupWizard.EnsureFolder("Assets/_Project/Art/UI");
            material = new Material(Shader.Find("HDRP/Unlit")) { name = "DeploymentHologram" };
            material.SetColor("_UnlitColor", new Color(0.55f, 1.4f, 1.7f, 1f));
            material.SetFloat("_SurfaceType", 1f); // transparent
            material.SetFloat("_BlendMode", 1f); // additive
            material.SetFloat("_DoubleSidedEnable", 1f);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void IndoorRange(Transform level)
        {
            GameModeSceneBuilder.Block(level, "Range Roof", new Vector3(21.5f, 3.15f, 0f), new Vector3(31.3f, 0.3f, 6.3f));
            GameModeSceneBuilder.Block(level, "Range Wall N", new Vector3(21.5f, 1.5f, 3f), new Vector3(31.3f, 3f, 0.3f));
            GameModeSceneBuilder.Block(level, "Range Wall S", new Vector3(21.5f, 1.5f, -3f), new Vector3(31.3f, 3f, 0.3f));
            GameModeSceneBuilder.Block(level, "Backstop", new Vector3(37f, 1.5f, 0f), new Vector3(0.6f, 3f, 6.3f), "Dirt");
            GameModeSceneBuilder.Block(level, "Firing Line Bench", new Vector3(8f, 0.5f, 0f), new Vector3(0.6f, 1f, 5f), "Wood");
            for (float x = 10f; x <= 34f; x += 6f) Lamp(level, "Range Lamp", new Vector3(x, 2.85f, 0f), 2200f);

            foreach (float distance in new[] { 10f, 20f, 28f })
                GameModeSceneBuilder.Block(level, $"Target {distance:0}m", new Vector3(8f + distance, 1f, -1.2f), new Vector3(0.1f, 1.6f, 1f));
            BallisticsContentBuilder.Dummy(level, "Dummy 15m (unarmored)", new Vector3(23f, 0f, 1.2f), false, -90f);
            BallisticsContentBuilder.Dummy(level, "Dummy 25m (plates + helmet)", new Vector3(33f, 0f, 1.2f), true, -90f);
        }

        private static void Lamp(Transform level, string lampName, Vector3 position, float lumen)
        {
            var lampObject = new GameObject(lampName);
            lampObject.transform.SetParent(level, false);
            lampObject.transform.position = position;
            var lamp = lampObject.AddComponent<Light>();
            lampObject.AddComponent<HDAdditionalLightData>();
            lamp.type = LightType.Point;
            lamp.lightUnit = LightUnit.Lumen;
            lamp.intensity = lumen;
            lamp.range = 12f;
            lamp.shadows = LightShadows.Soft;
            WorldLook.EnableContactShadows(lamp);
        }

        private static BoxCollider Crate(Transform level, string crateName, Vector3 position, Vector3? size = null)
        {
            GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = crateName;
            crate.layer = GameLayers.WorldGeometry;
            crate.transform.SetParent(level, false);
            crate.transform.position = position;
            crate.transform.localScale = size ?? new Vector3(0.8f, 0.6f, 0.5f);
            crate.GetComponent<MeshRenderer>().sharedMaterial = LitMaterial("CoverConcrete", new Color(0.46f, 0.45f, 0.43f), 0f, 0.2f);
            return crate.GetComponent<BoxCollider>();
        }

        private static GameObject Interactable(Transform parent, string interactName, Vector3 size)
        {
            var interact = new GameObject(interactName) { layer = GameLayers.Interactable };
            interact.transform.SetParent(parent, false);
            var box = interact.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x / parent.localScale.x, size.y / parent.localScale.y, size.z / parent.localScale.z);
            return interact;
        }
    }
}
