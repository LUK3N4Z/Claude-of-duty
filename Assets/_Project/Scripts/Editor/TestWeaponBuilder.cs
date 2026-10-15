using FPS.Core;
using FPS.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates a primitive test rifle with every socket, a 4x Picture-in-Picture scope and 45° canted iron sights,
    /// plus its weapon data and recoil profile, so the viewmodel systems can be played before real models arrive.
    /// </summary>
    public static class TestWeaponBuilder
    {
        internal const string WeaponPrefabPath = "Assets/_Project/Prefabs/Weapons/TestRifle.prefab";
        internal const string WeaponDataPath = DataFolder + "/TestRifle_Data.asset";

        private const string ArtFolder = "Assets/_Project/Art/TestRifle";
        private const string DataFolder = "Assets/_Project/Data/Weapons";
        private const string RecoilPath = DataFolder + "/TestRifle_Recoil.asset";
        private const float LensRadius = 0.0165f;

        [MenuItem("Tools/FPS Builder/3. Create Test Weapon", priority = 3)]
        public static void CreateTestWeaponMenu()
        {
            CreateTestWeapon();
            PlayerBuilder.CreateOrUpdatePlayerPrefab();
        }

        public static void CreateTestWeapon()
        {
            ProjectSetupWizard.EnsureFolder(ArtFolder);
            ProjectSetupWizard.EnsureFolder(DataFolder);
            ProjectSetupWizard.EnsureFolder(WeaponPrefabPath.Substring(0, WeaponPrefabPath.LastIndexOf('/')));

            Material metal = LitMaterial("Gunmetal", new Color(0.16f, 0.16f, 0.17f), 0.85f, 0.45f);
            Material polymer = LitMaterial("Polymer", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f);
            Material lens = UnlitMaterial("Scope Lens", Color.white, null, false);
            Material reticle = UnlitMaterial("Scope Reticle", Color.black, ReticleTexture(), true);
            Mesh disc = DiscMesh();

            var root = new GameObject("TestRifle");
            try
            {
                Transform t = root.transform;
                Part(t, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.08f, 0.3f), Quaternion.identity, metal);
                Part(t, "Handguard", PrimitiveType.Cube, new Vector3(0f, 0.005f, 0.29f), new Vector3(0.045f, 0.055f, 0.28f), Quaternion.identity, polymer);
                Part(t, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.52f), new Vector3(0.02f, 0.09f, 0.02f), Quaternion.Euler(90f, 0f, 0f), metal);
                Part(t, "Stock", PrimitiveType.Cube, new Vector3(0f, -0.015f, -0.27f), new Vector3(0.04f, 0.09f, 0.24f), Quaternion.identity, polymer);
                Part(t, "Grip", PrimitiveType.Cube, new Vector3(0f, -0.075f, -0.07f), new Vector3(0.03f, 0.09f, 0.035f), Quaternion.Euler(15f, 0f, 0f), polymer);
                Part(t, "Magazine", PrimitiveType.Cube, new Vector3(0f, -0.1f, 0.06f), new Vector3(0.025f, 0.12f, 0.06f), Quaternion.Euler(8f, 0f, 0f), polymer);

                // 4x scope: eyepiece at z = -0.06, 7.5 cm above the bore.
                Part(t, "Scope Mount", PrimitiveType.Cube, new Vector3(0f, 0.05f, 0.02f), new Vector3(0.02f, 0.03f, 0.06f), Quaternion.identity, metal);
                Part(t, "Scope Body", PrimitiveType.Cylinder, new Vector3(0f, 0.075f, 0.02f), new Vector3(0.04f, 0.08f, 0.04f), Quaternion.Euler(90f, 0f, 0f), metal);
                Transform primary = Socket(t, WeaponModelRig.PrimarySightSocketName, new Vector3(0f, 0.075f, -0.06f), Quaternion.identity);
                MeshRenderer lensRenderer = Disc(primary, "Ocular Lens", disc, -0.001f, lens);
                Disc(primary, "Reticle", disc, -0.0015f, reticle);
                primary.gameObject.AddComponent<WeaponSight>().Configure(4f, 0.07f, 1.25f, lensRenderer);

                // 45° canted irons on the right: aperture ring at the aligner, post 48 cm ahead, both on the sight line.
                Quaternion cant = Quaternion.Euler(0f, 0f, -45f);
                Transform canted = Socket(t, WeaponModelRig.CantedSightSocketName, new Vector3(0.042f, 0.042f, -0.09f), cant);
                Part(canted, "Aperture Top", PrimitiveType.Cube, new Vector3(0f, 0.006f, 0.005f), new Vector3(0.016f, 0.003f, 0.004f), Quaternion.identity, metal);
                Part(canted, "Aperture Bottom", PrimitiveType.Cube, new Vector3(0f, -0.006f, 0.005f), new Vector3(0.016f, 0.003f, 0.004f), Quaternion.identity, metal);
                Part(canted, "Aperture Left", PrimitiveType.Cube, new Vector3(-0.006f, 0f, 0.005f), new Vector3(0.003f, 0.016f, 0.004f), Quaternion.identity, metal);
                Part(canted, "Aperture Right", PrimitiveType.Cube, new Vector3(0.006f, 0f, 0.005f), new Vector3(0.003f, 0.016f, 0.004f), Quaternion.identity, metal);
                Part(canted, "Rear Base", PrimitiveType.Cube, new Vector3(0f, -0.012f, 0.005f), new Vector3(0.016f, 0.006f, 0.01f), Quaternion.identity, metal);
                Part(canted, "Front Post", PrimitiveType.Cube, new Vector3(0f, -0.004f, 0.48f), new Vector3(0.0025f, 0.008f, 0.003f), Quaternion.identity, metal);
                Part(canted, "Front Base", PrimitiveType.Cube, new Vector3(0f, -0.012f, 0.48f), new Vector3(0.012f, 0.01f, 0.006f), Quaternion.identity, metal);
                canted.gameObject.AddComponent<WeaponSight>().Configure(1f, 0.09f, 1.1f, null);

                Socket(t, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.61f), Quaternion.identity);
                Socket(t, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.02f, -0.39f), Quaternion.identity);
                Socket(t, WeaponModelRig.ChamberEjectionSocketName, new Vector3(0.026f, 0.01f, 0f), Quaternion.Euler(0f, 90f, 0f));
                Socket(t, WeaponModelRig.MagazineSocketName, new Vector3(0f, -0.1f, 0.06f), Quaternion.Euler(8f, 0f, 0f));
                Socket(t, WeaponModelRig.ChargingHandleSocketName, new Vector3(0f, 0.03f, -0.15f), Quaternion.identity);
                Socket(t, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, -0.035f, 0.3f), Quaternion.identity);
                Socket(t, WeaponModelRig.RightHandIKGripName, new Vector3(0f, -0.075f, -0.07f), Quaternion.Euler(15f, 0f, 0f));
                root.AddComponent<WeaponModelRig>().EnsureSockets();

                SetLayerRecursively(t, GameLayers.FirstPersonViewmodel);
                PrefabUtility.SaveAsPrefabAsset(root, WeaponPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            RecoilProfileSO recoil = LoadOrCreate<RecoilProfileSO>(RecoilPath);
            WeaponDataSO data = LoadOrCreate<WeaponDataSO>(WeaponDataPath);
            var serialized = new SerializedObject(data);
            serialized.FindProperty(WeaponDataSO.RecoilFieldName).objectReferenceValue = recoil;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPS Builder] {WeaponPrefabPath} and {WeaponDataPath} ready.");
        }

        private static void Part(Transform parent, string partName, PrimitiveType shape, Vector3 position, Vector3 size,
            Quaternion rotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = partName;
            Object.DestroyImmediate(part.GetComponent<Collider>()); // the viewmodel never collides; barrel tucking casts instead
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Transform Socket(Transform parent, string socketName, Vector3 position, Quaternion rotation)
        {
            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.SetLocalPositionAndRotation(position, rotation);
            return socket;
        }

        private static MeshRenderer Disc(Transform parent, string discName, Mesh mesh, float depth, Material material)
        {
            var disc = new GameObject(discName);
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, 0f, depth);
            disc.transform.localScale = new Vector3(LensRadius * 2f, LensRadius * 2f, 1f);
            disc.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = disc.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return meshRenderer;
        }

        private static Material LitMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            string path = $"{ArtFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("HDRP/Lit")) { name = materialName };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material UnlitMaterial(string materialName, Color color, Texture texture, bool alphaClip)
        {
            string path = $"{ArtFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("HDRP/Unlit")) { name = materialName };
            material.SetColor("_UnlitColor", color);
            if (texture != null) material.SetTexture("_UnlitColorMap", texture);
            if (alphaClip)
            {
                HDMaterial.SetAlphaClipping(material, true);
                material.SetFloat("_AlphaCutoff", 0.5f);
            }

            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Unit-diameter disc in the XY plane facing -Z (toward the eye); UVs map the full 0-1 square.</summary>
        private static Mesh DiscMesh()
        {
            string path = $"{ArtFolder}/LensDisc.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            const int segments = 48;
            var vertices = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var normals = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            uvs[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.back;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f;
                vertices[i + 1] = point;
                uvs[i + 1] = point + new Vector2(0.5f, 0.5f);
                normals[i + 1] = Vector3.back;
                // Clockwise as seen from -Z, so the face points at the eye.
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            mesh = new Mesh { name = "LensDisc", vertices = vertices, uv = uvs, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Duplex crosshair: thick outer posts, fine centre lines, centre dot; transparent elsewhere.</summary>
        private static Texture2D ReticleTexture()
        {
            string path = $"{ArtFolder}/ReticleDuplex.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null) return texture;

            const int size = 512;
            const float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half);
                    float dy = Mathf.Abs(y + 0.5f - half);
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float along = Mathf.Max(dx, dy);
                    float across = Mathf.Min(dx, dy);
                    bool post = along > half * 0.45f && across < 6f;
                    bool fine = along <= half * 0.45f && across < 1.2f;
                    bool dot = radius < 3f;
                    bool ink = radius < half && (post || fine || dot);
                    pixels[y * size + x] = ink ? new Color32(0, 0, 0, 255) : new Color32(0, 0, 0, 0);
                }
            }

            texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "ReticleDuplex", wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(true);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
