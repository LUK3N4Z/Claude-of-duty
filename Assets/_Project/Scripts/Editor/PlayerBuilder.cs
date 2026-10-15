using FPS.Core;
using FPS.Locomotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools
{
    /// <summary>Generates the first-person player prefab and a locomotion test range covering every M2 mechanic.</summary>
    public static class PlayerBuilder
    {
        private const string MenuRoot = "Tools/FPS Builder/";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string TestScenePath = "Assets/_Project/Scenes/LocomotionTestRange.unity";
        private const string IceMaterialPath = "Assets/_Project/Settings/Physics/Ice.asset";
        private const StaticEditorFlags LevelStaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                                                           StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                                           StaticEditorFlags.ReflectionProbeStatic;

        [MenuItem(MenuRoot + "3. Create Player Prefab", priority = 3)]
        public static void CreatePlayerPrefabMenu() => CreateOrUpdatePlayerPrefab();

        /// <summary>Creates the player prefab, or completes an existing one in place (keeps its GUID and edits).</summary>
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
                    PlayerRig.Build(contents, input);
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
                    PlayerRig.Build(root, input);
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

        [MenuItem(MenuRoot + "4. Build Locomotion Test Range", priority = 4)]
        public static void BuildLocomotionTestRange()
        {
            GameObject playerPrefab = CreateOrUpdatePlayerPrefab();
            if (playerPrefab == null) return;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out Camera _)) Object.DestroyImmediate(root); // the player brings its own camera
            }

            Transform level = new GameObject("Level").transform;
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

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -4f), Quaternion.identity);

            ProjectSetupWizard.EnsureFolder(FolderOf(TestScenePath));
            EditorSceneManager.SaveScene(scene, TestScenePath);
            Debug.Log($"[FPS Builder] {TestScenePath} built.");
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
