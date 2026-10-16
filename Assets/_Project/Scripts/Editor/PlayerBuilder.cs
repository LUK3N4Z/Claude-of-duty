using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools
{
    /// <summary>Generates the first-person player prefab (with viewmodel and test rifle) and the test range scene.</summary>
    public static class PlayerBuilder
    {
        private const string MenuRoot = "Tools/FPS Builder/";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string TestScenePath = "Assets/_Project/Scenes/LocomotionTestRange.unity";
        private const string IceMaterialPath = "Assets/_Project/Settings/Physics/Ice.asset";
        private const StaticEditorFlags LevelStaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                                                           StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                                           StaticEditorFlags.ReflectionProbeStatic;

        [MenuItem(MenuRoot + "4. Create Player Prefab", priority = 4)]
        public static void CreatePlayerPrefabMenu() => CreateOrUpdatePlayerPrefab();

        /// <summary>
        /// Creates the player prefab, or completes an existing one in place (keeps its GUID and edits): rig, viewmodel,
        /// IK arms, and the test carbine / marksman loadout when they exist (Tools/FPS Builder/3).
        /// </summary>
        public static GameObject CreateOrUpdatePlayerPrefab()
        {
            var input = AssetDatabase.LoadAssetAtPath<InputReader>(ProjectSetupWizard.InputReaderPath);
            if (input == null)
            {
                Debug.LogError("[FPS Builder] InputReader missing. Run Tools/FPS Builder/2. Create Core Assets first.");
                return null;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
                try
                {
                    BuildPlayer(contents, input);
                    PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            else
            {
                ProjectSetupWizard.EnsureFolder(FolderOf(PlayerPrefabPath));
                var root = new GameObject("Player");
                try
                {
                    BuildPlayer(root, input);
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            Debug.Log($"[FPS Builder] {PlayerPrefabPath} ready.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        }

        [MenuItem(MenuRoot + "5. Build Test Range", priority = 5)]
        public static void BuildLocomotionTestRange()
        {
            GameObject playerPrefab = CreateOrUpdatePlayerPrefab();
            if (playerPrefab == null) return;
            AIContentBuilder.CreateContent();
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out Camera _)) Object.DestroyImmediate(root); // the player brings its own camera
            }

            Transform level = new GameObject("Level").transform;
            AtmosphereVolume(level);
            Block(level, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f), Quaternion.identity);

            // Slide physics: friction wins on 15°, gravity wins on 35°. Falling off the top end tests landing.
            Ramp(level, "Ramp 15deg", new Vector3(-8f, 0f, 8f), 15f, 10f);
            Ramp(level, "Ramp 35deg", new Vector3(-14f, 0f, 8f), 35f, 8f);

            Block(level, "Ice Sheet (low traction)", new Vector3(8f, 0.02f, 14f), new Vector3(6f, 0.04f, 20f), Quaternion.identity)
                .sharedMaterial = LoadOrCreateIce();

            // Mounting: aim + mount key in front of each. The corner wall's right edge also tests lean collision.
            Block(level, "Ledge 1.4m (standing mount)", new Vector3(0f, 0.7f, 4f), new Vector3(3f, 1.4f, 0.3f), Quaternion.identity);
            Block(level, "Ledge 0.75m (crouched mount)", new Vector3(4f, 0.375f, 4f), new Vector3(3f, 0.75f, 0.3f), Quaternion.identity);
            Block(level, "Corner Wall (corner mount)", new Vector3(-4.5f, 1.5f, 4f), new Vector3(3f, 3f, 0.3f), Quaternion.identity);

            // Stance clearance: cannot stand up inside, cannot crouch into the crawl tunnel.
            Tunnel(level, "Crouch Tunnel 1.3m", new Vector3(10f, 0f, -8f), 1.3f);
            Tunnel(level, "Crawl Tunnel 0.6m", new Vector3(15f, 0f, -8f), 0.6f);

            // Optics: boards down range for the PiP scope; the narrow corridor exercises barrel tucking.
            Block(level, "Range Floor", new Vector3(20f, -0.5f, 140f), new Vector3(12f, 1f, 200f), Quaternion.identity)
                .gameObject.AddComponent<FPS.Ballistics.SurfaceIdentifier>().Surface = BallisticsContentBuilder.Surface("Dirt");
            foreach (float distance in new[] { 25f, 50f, 100f, 200f })
                Block(level, $"Target {distance:0}m", new Vector3(20f, 1f, distance), new Vector3(1f, 2f, 0.1f), Quaternion.identity);
            Block(level, "Corridor Left", new Vector3(-21f, 1.5f, -8f), new Vector3(0.3f, 3f, 8f), Quaternion.identity);
            Block(level, "Corridor Right", new Vector3(-19.8f, 1.5f, -8f), new Vector3(0.3f, 3f, 8f), Quaternion.identity);

            BallisticsContentBuilder.BuildRange(level);

            // Acoustics: an enclosed room (indoor tail) and a roofless street canyon (urban slapback).
            Transform room = new GameObject("Shoot House (indoor acoustics)").transform;
            room.SetParent(level, false);
            room.localPosition = new Vector3(-26f, 0f, 6f);
            Block(room, "Wall N", new Vector3(0f, 1.5f, 3f), new Vector3(6.3f, 3f, 0.3f), Quaternion.identity);
            Block(room, "Wall S Left", new Vector3(-2f, 1.5f, -3f), new Vector3(2.3f, 3f, 0.3f), Quaternion.identity);
            Block(room, "Wall S Right", new Vector3(2f, 1.5f, -3f), new Vector3(2.3f, 3f, 0.3f), Quaternion.identity);
            Block(room, "Wall E", new Vector3(3f, 1.5f, 0f), new Vector3(0.3f, 3f, 6f), Quaternion.identity);
            Block(room, "Wall W", new Vector3(-3f, 1.5f, 0f), new Vector3(0.3f, 3f, 6f), Quaternion.identity);
            Block(room, "Roof", new Vector3(0f, 3.15f, 0f), new Vector3(6.3f, 0.3f, 6.3f), Quaternion.identity);
            var dustObject = new GameObject("Dust (local volumetric fog)");
            dustObject.transform.SetParent(room, false);
            dustObject.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var dust = dustObject.AddComponent<UnityEngine.Rendering.HighDefinition.LocalVolumetricFog>();
            UnityEngine.Rendering.HighDefinition.LocalVolumetricFogArtistParameters dustParameters = dust.parameters;
            dustParameters.size = new Vector3(5.7f, 3f, 5.7f);
            dustParameters.meanFreePath = 12f; // airborne dust: light shafts and torch beams show
            dustParameters.albedo = new Color(0.85f, 0.8f, 0.72f);
            dust.parameters = dustParameters;
            Block(level, "Canyon Wall West (urban acoustics)", new Vector3(-34f, 6f, 26f), new Vector3(0.5f, 12f, 30f), Quaternion.identity);
            Block(level, "Canyon Wall East (urban acoustics)", new Vector3(-26f, 6f, 26f), new Vector3(0.5f, 12f, 30f), Quaternion.identity);

            BoxCollider crate = Block(level, "Ammo Crate", new Vector3(2f, 0.3f, -3f), new Vector3(0.8f, 0.6f, 0.5f), Quaternion.identity);
            var resupply = new GameObject("Resupply (interact)") { layer = GameLayers.Interactable };
            resupply.transform.SetParent(crate.transform, false);
            resupply.AddComponent<BoxCollider>().size = new Vector3(1.1f, 1.2f, 1.2f);
            resupply.AddComponent<AmmoCrate>();

            // Swaps the player's tactical grenade between stun and smoke.
            BoxCollider locker = Block(level, "Equipment Locker", new Vector3(3.2f, 0.9f, -3f), new Vector3(0.6f, 1.8f, 0.5f), Quaternion.identity);
            var swap = new GameObject("Swap tactical (interact)") { layer = GameLayers.Interactable };
            swap.transform.SetParent(locker.transform, false);
            swap.AddComponent<BoxCollider>().size = new Vector3(1.3f, 1.1f, 1.4f);
            swap.AddComponent<EquipmentLocker>().Configure(EquipmentContentBuilder.Grenade(EquipmentContentBuilder.FlashName),
                EquipmentContentBuilder.Grenade(EquipmentContentBuilder.SmokeName), 2);

            AIContentBuilder.BuildEncounters(level); // last: bakes the NavMesh over all of the level

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -4f), Quaternion.identity);

            ProjectSetupWizard.EnsureFolder(FolderOf(TestScenePath));
            EditorSceneManager.SaveScene(scene, TestScenePath);
            Debug.Log($"[FPS Builder] {TestScenePath} built.");
        }

        /// <summary>Light volumetric fog so flashlight and muzzle-flash cones read in the air.</summary>
        internal static void AtmosphereVolume(Transform level)
        {
            const string profilePath = "Assets/_Project/Settings/Rendering/TestRangeAtmosphere.asset";
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(profilePath);
            if (profile == null)
            {
                ProjectSetupWizard.EnsureFolder(FolderOf(profilePath));
                profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            if (!profile.TryGet(out UnityEngine.Rendering.HighDefinition.Fog fog))
            {
                fog = profile.Add<UnityEngine.Rendering.HighDefinition.Fog>();
                AssetDatabase.AddObjectToAsset(fog, profile);
            }

            // Thin outdoors so the 300 m targets stay readable; the shoot house adds its own dust.
            fog.enabled.Override(true);
            fog.enableVolumetricFog.Override(true);
            fog.meanFreePath.Override(250f);
            fog.albedo.Override(new Color(0.8f, 0.82f, 0.85f));
            EditorUtility.SetDirty(fog);
            AssetDatabase.SaveAssets();

            var volumeObject = new GameObject("Atmosphere (volumetric fog)");
            volumeObject.transform.SetParent(level, false);
            var volume = volumeObject.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static void BuildPlayer(GameObject root, InputReader input)
        {
            PlayerRig rig = PlayerRig.Build(root, input);
            ViewmodelRig viewmodel = ViewmodelRig.Build(rig);
            BuildArms(viewmodel);
            if (!root.TryGetComponent(out FPS.UI.DiegeticTacticalHUD _)) root.AddComponent<FPS.UI.DiegeticTacticalHUD>();
            if (!root.TryGetComponent(out FPS.AI.PerceivableTarget _)) root.AddComponent<FPS.AI.PerceivableTarget>(); // team 0: AI sees and hears the player
            EquipmentContentBuilder.CreateContent();
            if (!root.TryGetComponent(out TacticalEquipmentSystem equipment)) equipment = root.AddComponent<TacticalEquipmentSystem>();
            equipment.SetLoadout(EquipmentSlot.Lethal, EquipmentContentBuilder.Grenade(EquipmentContentBuilder.FragName), 2);
            equipment.SetLoadout(EquipmentSlot.Tactical, EquipmentContentBuilder.Grenade(EquipmentContentBuilder.FlashName), 2);
            EditorUtility.SetDirty(equipment);
            EffectsContentBuilder.ConfigurePlayer(root);

            var controller = root.GetComponent<RealisticWeaponController>();
            var serialized = new SerializedObject(controller);
            SerializedProperty slots = serialized.FindProperty(RealisticWeaponController.LoadoutFieldName);
            slots.arraySize = 2;
            AssignSlot(slots.GetArrayElementAtIndex(0), TestWeaponBuilder.CarbineName);
            AssignSlot(slots.GetArrayElementAtIndex(1), TestWeaponBuilder.MarksmanName);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignSlot(SerializedProperty slot, string weaponName)
        {
            slot.FindPropertyRelative("model").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<WeaponModelRig>(TestWeaponBuilder.PrefabPath(weaponName));
            slot.FindPropertyRelative("data").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<WeaponDataSO>(TestWeaponBuilder.DataPath(weaponName));
        }

        /// <summary>Primitive first-person arms (sleeve capsules, glove boxes) driven by two-bone IK.</summary>
        private static void BuildArms(ViewmodelRig viewmodel)
        {
            Material sleeve = TestWeaponBuilder.LitMaterial("Sleeve", new Color(0.24f, 0.26f, 0.17f), 0f, 0.25f);
            Material glove = TestWeaponBuilder.LitMaterial("Glove", new Color(0.07f, 0.07f, 0.07f), 0f, 0.35f);
            Transform arms = PlayerRig.EnsureChild(viewmodel.ViewmodelRoot, "Arms");

            GameObject root = viewmodel.gameObject;
            if (!root.TryGetComponent(out FirstPersonArmsIK ik)) ik = root.AddComponent<FirstPersonArmsIK>();
            var serialized = new SerializedObject(ik);
            Arm(serialized.FindProperty(FirstPersonArmsIK.LeftArmFieldName), arms, "Left", new Vector3(-0.1f, -0.22f, -0.05f), sleeve, glove);
            Arm(serialized.FindProperty(FirstPersonArmsIK.RightArmFieldName), arms, "Right", new Vector3(0.16f, -0.2f, -0.2f), sleeve, glove);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetLayerRecursively(arms, GameLayers.FirstPersonViewmodel);
        }

        private static void Arm(SerializedProperty chain, Transform arms, string side, Vector3 shoulder, Material sleeve, Material glove)
        {
            const float upperLength = 0.33f, lowerLength = 0.33f;
            Transform upper = PlayerRig.EnsureChild(arms, side + "UpperArm");
            upper.SetLocalPositionAndRotation(shoulder, Quaternion.Euler(30f, 0f, 0f));
            Transform lower = PlayerRig.EnsureChild(upper, side + "Forearm");
            lower.SetLocalPositionAndRotation(new Vector3(0f, 0f, upperLength), Quaternion.identity);
            Transform hand = PlayerRig.EnsureChild(lower, side + "Hand");
            hand.SetLocalPositionAndRotation(new Vector3(0f, 0f, lowerLength), Quaternion.identity);

            Visual(upper, PrimitiveType.Capsule, new Vector3(0f, 0f, upperLength * 0.5f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.09f, upperLength * 0.5f, 0.09f), sleeve);
            Visual(lower, PrimitiveType.Capsule, new Vector3(0f, 0f, lowerLength * 0.5f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.075f, lowerLength * 0.5f, 0.075f), sleeve);
            Visual(hand, PrimitiveType.Cube, new Vector3(0f, 0f, 0.03f), Quaternion.identity, new Vector3(0.08f, 0.035f, 0.1f), glove);

            chain.FindPropertyRelative("upper").objectReferenceValue = upper;
            chain.FindPropertyRelative("lower").objectReferenceValue = lower;
            chain.FindPropertyRelative("hand").objectReferenceValue = hand;
        }

        private static void Visual(Transform bone, PrimitiveType shape, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            if (bone.Find("Visual") != null) return;

            GameObject visual = GameObject.CreatePrimitive(shape);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(bone, false);
            visual.transform.SetLocalPositionAndRotation(position, rotation);
            visual.transform.localScale = scale;
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }

        private static BoxCollider Block(Transform parent, string blockName, Vector3 position, Vector3 size, Quaternion rotation)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = blockName;
            block.layer = GameLayers.WorldGeometry;
            block.transform.SetParent(parent, false);
            block.transform.SetLocalPositionAndRotation(position, rotation);
            block.transform.localScale = size;
            GameObjectUtility.SetStaticEditorFlags(block, LevelStaticFlags);
            return block.GetComponent<BoxCollider>();
        }

        private static void Ramp(Transform parent, string rampName, Vector3 foot, float angle, float length)
        {
            const float width = 4f, thickness = 0.3f;
            float radians = angle * Mathf.Deg2Rad;
            var along = new Vector3(0f, Mathf.Sin(radians), Mathf.Cos(radians));
            var normal = new Vector3(0f, Mathf.Cos(radians), -Mathf.Sin(radians));
            Vector3 center = foot + along * (length * 0.5f) - normal * (thickness * 0.5f); // top face starts at the foot
            Block(parent, rampName, center, new Vector3(width, thickness, length), Quaternion.Euler(-angle, 0f, 0f));
        }

        private static void Tunnel(Transform parent, string tunnelName, Vector3 entrance, float clearance)
        {
            const float length = 6f, width = 2.2f, wall = 0.3f, roof = 0.3f;
            Transform root = new GameObject(tunnelName).transform;
            root.SetParent(parent, false);
            root.localPosition = entrance;

            float wallX = (width + wall) * 0.5f;
            Block(root, "Left Wall", new Vector3(-wallX, clearance * 0.5f, length * 0.5f), new Vector3(wall, clearance, length), Quaternion.identity);
            Block(root, "Right Wall", new Vector3(wallX, clearance * 0.5f, length * 0.5f), new Vector3(wall, clearance, length), Quaternion.identity);
            Block(root, "Roof", new Vector3(0f, clearance + roof * 0.5f, length * 0.5f), new Vector3(width + wall * 2f, roof, length), Quaternion.identity);
        }

        private static PhysicsMaterial LoadOrCreateIce()
        {
            var ice = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(IceMaterialPath);
            if (ice != null) return ice;

            ProjectSetupWizard.EnsureFolder(FolderOf(IceMaterialPath));
            ice = new PhysicsMaterial("Ice")
            {
                dynamicFriction = 0.05f,
                staticFriction = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
            AssetDatabase.CreateAsset(ice, IceMaterialPath);
            return ice;
        }

        private static string FolderOf(string assetPath) => assetPath.Substring(0, assetPath.LastIndexOf('/'));
    }
}
