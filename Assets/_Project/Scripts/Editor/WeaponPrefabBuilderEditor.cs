using System;
using System.Collections.Generic;
using FPS.Core;
using FPS.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Inspector wizard for <see cref="WeaponModelRig"/>: validation report, socket generation, HDRP material conversion,
    /// collider stripping, magazine renderer discovery and model orientation fixes for imported FBX/GLTF weapons.
    /// </summary>
    [CustomEditor(typeof(WeaponModelRig))]
    public sealed class WeaponPrefabBuilderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var rig = (WeaponModelRig)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Weapon Pipeline", EditorStyles.boldLabel);
            List<string> issues = WeaponModelSetup.Validate(rig);
            if (issues.Count == 0) EditorGUILayout.HelpBox("Ready: sockets bound, model oriented +Z, no colliders.", MessageType.Info);
            foreach (string issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);

            if (GUILayout.Button("Generate Missing Sockets")) Apply(rig, "Generate Sockets", () => WeaponModelSetup.GenerateSockets(rig));
            if (GUILayout.Button("Find Magazine Renderers")) Apply(rig, "Find Magazine Renderers", () => WeaponModelSetup.AssignMagazineRenderers(rig));
            if (GUILayout.Button("Convert Materials To HDRP Lit")) Apply(rig, "Convert Materials", () => WeaponModelSetup.ConvertMaterials(rig.gameObject));
            if (GUILayout.Button("Strip Colliders + Viewmodel Layer")) Apply(rig, "Strip Colliders", () => WeaponModelSetup.StripCollidersAndSetLayer(rig.gameObject));

            Transform model = rig.transform.Find(WeaponModelSetup.ModelChildName);
            using (new EditorGUI.DisabledScope(model == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Model +90° Y")) Apply(rig, "Rotate Model", () => model.Rotate(0f, 90f, 0f, Space.Self));
                if (GUILayout.Button("Model 180° Y")) Apply(rig, "Flip Model", () => model.Rotate(0f, 180f, 0f, Space.Self));
                if (GUILayout.Button("Model ×0.01")) Apply(rig, "Scale Model", () => model.localScale *= 0.01f);
                EditorGUILayout.EndHorizontal();
            }
        }

        [MenuItem("Tools/FPS Builder/6. Build Weapon From Selected Model", priority = 6)]
        [MenuItem("Assets/FPS/Build Weapon Prefab From Model")]
        public static void BuildFromSelection()
        {
            if (!(Selection.activeObject is GameObject model) || !PrefabUtility.IsPartOfPrefabAsset(model))
            {
                Debug.LogError("[FPS Builder] Select an imported model (FBX/GLTF) in the Project window.");
                return;
            }

            Selection.activeObject = WeaponModelSetup.BuildWeaponPrefab(model);
        }

        [MenuItem("Assets/FPS/Build Weapon Prefab From Model", true)]
        private static bool CanBuildFromSelection() => Selection.activeObject is GameObject go && PrefabUtility.IsPartOfModelPrefab(go);

        private static void Apply(WeaponModelRig rig, string undoName, Action action)
        {
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, undoName);
            action();
            EditorUtility.SetDirty(rig);
        }
    }

    /// <summary>Editor-side setup steps for turning an imported model into a weapon prefab.</summary>
    public static class WeaponModelSetup
    {
        public const string ModelChildName = "Model";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Weapons";
        private const string DataFolder = "Assets/_Project/Data/Weapons";
        private const string ArtRoot = "Assets/_Project/Art";

        private static readonly HashSet<string> SocketNames = new HashSet<string>
        {
            WeaponModelRig.MuzzleSocketName, WeaponModelRig.ChamberEjectionSocketName, WeaponModelRig.MagazineSocketName,
            WeaponModelRig.ChargingHandleSocketName, WeaponModelRig.StockPivotSocketName, WeaponModelRig.PrimarySightSocketName,
            WeaponModelRig.CantedSightSocketName, WeaponModelRig.LeftHandIKGripName, WeaponModelRig.RightHandIKGripName,
        };

        /// <summary>
        /// Wraps <paramref name="model"/> in a weapon root (rig on the root, model as a child so orientation and scale
        /// can be fixed without moving sockets), converts materials, strips colliders, generates sockets, saves the
        /// prefab and a default <see cref="WeaponDataSO"/>. Returns the prefab.
        /// </summary>
        public static GameObject BuildWeaponPrefab(GameObject model)
        {
            string weaponName = model.name;
            var root = new GameObject(weaponName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                instance.name = ModelChildName;
                instance.transform.SetParent(root.transform, false);

                Bounds bounds = LocalRendererBounds(root.transform);
                if (bounds.size.x > bounds.size.z * 1.5f) instance.transform.Rotate(0f, 90f, 0f, Space.Self); // barrel along X

                ConvertMaterials(root);
                StripCollidersAndSetLayer(root);
                var rig = root.AddComponent<WeaponModelRig>();
                GenerateSockets(rig);
                AssignMagazineRenderers(rig);

                ProjectSetupWizard.EnsureFolder(PrefabFolder);
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{weaponName}.prefab");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                ProjectSetupWizard.EnsureFolder(DataFolder);
                string dataPath = $"{DataFolder}/{weaponName}_Data.asset";
                if (AssetDatabase.LoadAssetAtPath<WeaponDataSO>(dataPath) == null)
                    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<WeaponDataSO>(), dataPath);
                AssetDatabase.SaveAssets();

                foreach (string issue in Validate(prefab.GetComponent<WeaponModelRig>()))
                    Debug.LogWarning($"[FPS Builder] {weaponName}: {issue}", prefab);
                Debug.Log($"[FPS Builder] {prefabPath} built; place sockets in prefab mode, then fill {dataPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public static List<string> Validate(WeaponModelRig rig)
        {
            var issues = new List<string>();
            if (rig.MuzzleSocket == null) issues.Add("MuzzleSocket missing: bullets cannot spawn.");
            if (rig.StockPivotSocket == null) issues.Add("StockPivotSocket missing: recoil and inertia pivot at the model origin.");
            if (rig.PrimarySightSocket == null) issues.Add("PrimarySightSocket missing: cannot aim down sights.");
            if (rig.MagazineSocket == null) issues.Add("MagazineSocket missing.");
            if (rig.ChamberEjectionSocket == null) issues.Add("ChamberEjectionSocket missing.");
            if (rig.ChargingHandleSocket == null) issues.Add("ChargingHandleSocket missing.");
            if (rig.LeftHandIKGrip == null || rig.RightHandIKGrip == null) issues.Add("Hand IK grips missing: arms cannot hold the weapon.");

            if (rig.GetComponentsInChildren<Renderer>(true).Length == 0) issues.Add("No renderers under the weapon.");
            else
            {
                Bounds bounds = LocalRendererBounds(rig.transform);
                float length = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (length > 2.5f || length < 0.15f) issues.Add($"Model is {length:0.###} m long: check the import scale (cm models need ×0.01).");
                if (bounds.size.z < Mathf.Max(bounds.size.x, bounds.size.y)) issues.Add("Longest axis is not Z: rotate the Model child so the barrel points +Z.");
            }

            if (rig.MuzzleSocket != null && rig.StockPivotSocket != null &&
                rig.transform.InverseTransformPoint(rig.MuzzleSocket.position).z <= rig.transform.InverseTransformPoint(rig.StockPivotSocket.position).z)
                issues.Add("Muzzle is behind the stock: flip the Model child 180° or move the sockets.");

            if (rig.GetComponentsInChildren<Collider>(true).Length > 0) issues.Add("Colliders found: the viewmodel must not collide (barrel tucking casts instead).");
            if (rig.PrimarySightSocket != null && rig.PrimarySightSocket.GetComponentInChildren<WeaponSight>(true) == null)
                issues.Add("Primary sight has no WeaponSight: iron-sight defaults (no magnification) are used.");
            return issues;
        }

        /// <summary>Creates every missing socket: at a name-matched model node when one exists, else from the bounds.</summary>
        public static void GenerateSockets(WeaponModelRig rig)
        {
            Transform root = rig.transform;
            Bounds b = LocalRendererBounds(root);
            Vector3 c = b.center, min = b.min, max = b.max;
            float length = b.size.z, height = b.size.y;

            Place(root, WeaponModelRig.MuzzleSocketName, new Vector3(c.x, c.y + height * 0.15f, max.z), Quaternion.identity, "muzzle", "flash", "barrel_end");
            Place(root, WeaponModelRig.StockPivotSocketName, new Vector3(c.x, c.y, min.z), Quaternion.identity, "buttpad", "butt", "stock_end");
            Place(root, WeaponModelRig.PrimarySightSocketName, new Vector3(c.x, max.y + 0.005f, min.z + length * 0.4f), Quaternion.identity, "rear_sight", "optic", "scope", "sight");
            Place(root, WeaponModelRig.MagazineSocketName, new Vector3(c.x, min.y + height * 0.25f, c.z + length * 0.05f), Quaternion.identity, "magazine", "mag");
            Place(root, WeaponModelRig.ChamberEjectionSocketName, new Vector3(max.x, c.y + height * 0.1f, c.z), Quaternion.Euler(0f, 90f, 0f), "ejection", "port");
            Place(root, WeaponModelRig.ChargingHandleSocketName, new Vector3(c.x, max.y - height * 0.15f, min.z + length * 0.45f), Quaternion.identity, "charging", "bolt");
            Place(root, WeaponModelRig.LeftHandIKGripName, new Vector3(c.x, min.y + height * 0.35f, c.z + length * 0.25f), Quaternion.identity, "handguard", "foregrip");
            Place(root, WeaponModelRig.RightHandIKGripName, new Vector3(c.x, min.y + height * 0.2f, c.z - length * 0.1f), Quaternion.Euler(15f, 0f, 0f), "pistol_grip", "grip");
            rig.EnsureSockets();
        }

        public static void AssignMagazineRenderers(WeaponModelRig rig)
        {
            var found = new List<Renderer>();
            foreach (Renderer renderer in rig.GetComponentsInChildren<Renderer>(true))
            {
                string node = renderer.name.ToLowerInvariant();
                if ((node.Contains("magazine") || node.Contains("mag")) && !node.Contains("magnif")) found.Add(renderer);
            }

            var serialized = new SerializedObject(rig);
            SerializedProperty list = serialized.FindProperty(WeaponModelRig.MagazineRenderersFieldName);
            list.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Replaces non-HDRP materials with HDRP/Lit copies (base colour, base map, normal, metallic, smoothness).</summary>
        public static void ConvertMaterials(GameObject root)
        {
            string folder = $"{ArtRoot}/{root.name}/Materials";
            var converted = new Dictionary<Material, Material>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || source.shader.name.StartsWith("HDRP/", StringComparison.Ordinal)) continue;
                    if (!converted.TryGetValue(source, out Material lit))
                    {
                        lit = ToHdrpLit(source);
                        ProjectSetupWizard.EnsureFolder(folder);
                        AssetDatabase.CreateAsset(lit, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}.mat"));
                        converted.Add(source, lit);
                    }

                    materials[i] = lit;
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        public static void StripCollidersAndSetLayer(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = GameLayers.FirstPersonViewmodel;
        }

        /// <summary>Combined renderer bounds expressed in <paramref name="root"/>'s local space.</summary>
        public static Bounds LocalRendererBounds(Transform root)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = root.InverseTransformPoint(renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, offset)));
                    if (any) bounds.Encapsulate(point);
                    else bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }

            return bounds;
        }

        private static void Place(Transform root, string socketName, Vector3 fallback, Quaternion rotation, params string[] keywords)
        {
            if (FindDeep(root, socketName) != null) return;

            var socket = new GameObject(socketName).transform;
            socket.SetParent(root, false);
            Transform match = FindByKeyword(root, keywords);
            socket.localPosition = match != null ? root.InverseTransformPoint(match.position) : fallback;
            socket.localRotation = rotation;
        }

        private static Transform FindByKeyword(Transform root, string[] keywords)
        {
            foreach (string keyword in keywords)
            {
                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                {
                    if (node != root && !SocketNames.Contains(node.name) && node.name.ToLowerInvariant().Contains(keyword)) return node;
                }
            }

            return null;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == childName) return node;
            }

            return null;
        }

        private static Material ToHdrpLit(Material source)
        {
            var lit = new Material(Shader.Find("HDRP/Lit")) { name = source.name };
            Texture baseMap = FindTexture(source, "basecolor", "base", "main", "albedo", "diffuse");
            Texture normalMap = FindTexture(source, "normal", "bump");
            lit.SetColor("_BaseColor", FindColor(source, "_BaseColor", "_Color", "baseColorFactor"));
            if (baseMap != null) lit.SetTexture("_BaseColorMap", baseMap);
            if (normalMap != null) lit.SetTexture("_NormalMap", normalMap);
            if (source.HasFloat("_Metallic")) lit.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            else if (source.HasFloat("metallicFactor")) lit.SetFloat("_Metallic", source.GetFloat("metallicFactor"));
            if (source.HasFloat("_Smoothness")) lit.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
            else if (source.HasFloat("_Glossiness")) lit.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            else if (source.HasFloat("roughnessFactor")) lit.SetFloat("_Smoothness", 1f - source.GetFloat("roughnessFactor"));
            HDMaterial.ValidateMaterial(lit);
            return lit;
        }

        private static Texture FindTexture(Material source, params string[] keywords)
        {
            string[] names = source.GetTexturePropertyNames();
            foreach (string keyword in keywords)
            {
                foreach (string property in names)
                {
                    if (!property.ToLowerInvariant().Contains(keyword)) continue;
                    Texture texture = source.GetTexture(property);
                    if (texture != null) return texture;
                }
            }

            return null;
        }

        private static Color FindColor(Material source, params string[] properties)
        {
            foreach (string property in properties)
            {
                if (source.HasColor(property)) return source.GetColor(property);
            }

            return Color.white;
        }
    }
}
