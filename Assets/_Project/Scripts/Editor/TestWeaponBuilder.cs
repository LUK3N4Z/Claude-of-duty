using System;
using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the reference calibers and two primitive test weapons with every socket so the weapon systems can be
    /// played before real models arrive: a 5.56 closed-bolt carbine (4x PiP scope, 45° canted irons) and a .300 Win Mag
    /// bolt-action marksman rifle (8x PiP scope), each with weapon data, recoil profile and a droppable magazine.
    /// </summary>
    public static class TestWeaponBuilder
    {
        internal const string CarbineName = "TestRifle";
        internal const string MarksmanName = "TestMarksman";

        private const string ArtFolder = "Assets/_Project/Art/TestRifle";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Weapons";
        private const string DataFolder = "Assets/_Project/Data/Weapons";
        private const string CaliberFolder = "Assets/_Project/Data/Ballistics";

        private struct RifleSpec
        {
            public string Name;
            public float BarrelExposed;
            public float StockLength;
            public float ScopeLength;
            public float ScopeDiameter;
            public float Magnification;
            public float EyeRelief;
            public float AdsZoom;
            public bool CantedIrons;
            public Vector3 MagazineSize;
            public float MagazineAngle;
        }

        internal static string PrefabPath(string weaponName) => $"{PrefabFolder}/{weaponName}.prefab";
        internal static string DataPath(string weaponName) => $"{DataFolder}/{weaponName}_Data.asset";

        [MenuItem("Tools/FPS Builder/3. Create Test Weapons", priority = 3)]
        public static void CreateTestWeaponsMenu()
        {
            CreateTestWeapons();
            PlayerBuilder.CreateOrUpdatePlayerPrefab();
        }

        public static void CreateTestWeapons()
        {
            BallisticsContentBuilder.CreateContent();
            EffectsContentBuilder.CreateContent();
            ProjectSetupWizard.EnsureFolder(ArtFolder);
            ProjectSetupWizard.EnsureFolder(PrefabFolder);
            ProjectSetupWizard.EnsureFolder(DataFolder);
            ProjectSetupWizard.EnsureFolder(CaliberFolder);

            CaliberDataSO nato556 = Caliber("Caliber_556x45", "5.56x45mm NATO (M855)", 4.0f, 5.7f, 940f, 20f, 7.5f, DragModel.G7, 0.151f, 40f, 35f, "Casing_556x45");
            Caliber("Caliber_762x51", "7.62x51mm NATO (M80)", 9.5f, 7.82f, 840f, 22f, 5.5f, DragModel.G7, 0.2f, 55f, 55f, "Casing_762x51");
            Caliber("Caliber_9x19", "9x19mm Parabellum (124gr FMJ)", 8.0f, 9.01f, 360f, 4f, 4.5f, DragModel.G1, 0.15f, 15f, 30f, "Casing_9x19");
            CaliberDataSO winMag = Caliber("Caliber_300WM", ".300 Winchester Magnum (190gr)", 12.3f, 7.82f, 880f, 24f, 7.6f, DragModel.G7, 0.29f, 70f, 90f, "Casing_300WM");

            Material metal = LitMaterial("Gunmetal", new Color(0.16f, 0.16f, 0.17f), 0.85f, 0.45f);
            Material polymer = LitMaterial("Polymer", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f);

            var carbine = new RifleSpec
            {
                Name = CarbineName, BarrelExposed = 0.18f, StockLength = 0.24f, ScopeLength = 0.16f, ScopeDiameter = 0.04f,
                Magnification = 4f, EyeRelief = 0.07f, AdsZoom = 1.25f, CantedIrons = true,
                MagazineSize = new Vector3(0.025f, 0.12f, 0.06f), MagazineAngle = 8f,
            };
            WeaponModelRig carbineModel = BuildRifle(carbine, metal, polymer);
            Rigidbody carbineMagazine = DroppedMagazine($"{CarbineName}_DroppedMagazine", carbine.MagazineSize, 0.45f, polymer);
            RecoilProfileSO carbineRecoil = LoadOrCreate<RecoilProfileSO>($"{DataFolder}/{CarbineName}_Recoil.asset");
            Configure(LoadOrCreate<WeaponDataSO>(DataPath(CarbineName)), so =>
            {
                Set(so, "displayName", "Test Carbine 5.56");
                Set(so, "massKg", 3.4f);
                Set(so, "ergonomics", 55f);
                Set(so, "barrelLengthInches", 14.5f);
                Set(so, "caliber", nato556);
                SetEnum(so, "boltType", (int)BoltType.ClosedBolt);
                SetFireModes(so, FireMode.Semi, FireMode.Auto);
                Set(so, "roundsPerMinute", 800f);
                Set(so, "moaAccuracy", 2.5f);
                Set(so, "magazineCapacity", 30);
                Set(so, "boltHoldOpen", true);
                Set(so, "spareMagazines", 4);
                Set(so, "droppedMagazinePrefab", carbineMagazine);
                Set(so, "hipStockOffset", new Vector3(0.14f, -0.2f, -0.16f));
                Set(so, WeaponDataSO.RecoilFieldName, carbineRecoil);
                Set(so, "audioProfile", EffectsContentBuilder.WeaponAudio(CarbineName));
            });

            var marksman = new RifleSpec
            {
                Name = MarksmanName, BarrelExposed = 0.46f, StockLength = 0.28f, ScopeLength = 0.3f, ScopeDiameter = 0.05f,
                Magnification = 8f, EyeRelief = 0.09f, AdsZoom = 1.3f, CantedIrons = false,
                MagazineSize = new Vector3(0.03f, 0.07f, 0.09f), MagazineAngle = 0f,
            };
            BuildRifle(marksman, metal, polymer);
            Rigidbody marksmanMagazine = DroppedMagazine($"{MarksmanName}_DroppedMagazine", marksman.MagazineSize, 0.3f, polymer);
            RecoilProfileSO marksmanRecoil = LoadOrCreate<RecoilProfileSO>($"{DataFolder}/{MarksmanName}_Recoil.asset");
            Configure(marksmanRecoil, so =>
            {
                Set(so, "referenceMassKg", 6.2f);
                Set(so, "verticalKick", 330f);
                Set(so, "verticalSpread", 0.1f);
                Set(so, "horizontalBias", 5f);
                Set(so, "horizontalSpread", 30f);
                Set(so, "rollKick", 15f);
                Set(so, "backKick", 1.9f);
                Set(so, "upKick", 0.2f);
                Set(so, "boltCarrierImpulse", 0f); // no carrier on a bolt action
                Set(so, "boltCarrierPitch", 0f);
                Set(so, "viewClimb", 1.1f);
                Set(so, "cameraKick", 55f);
                Set(so, "cameraPush", 0.09f);
                Set(so, "kickRampPerShot", 0f);
                Set(so, "maxKickRamp", 1f);
            });
            Configure(LoadOrCreate<WeaponDataSO>(DataPath(MarksmanName)), so =>
            {
                Set(so, "displayName", "Test Marksman .300 WM");
                Set(so, "massKg", 6.2f);
                Set(so, "ergonomics", 32f);
                Set(so, "barrelLengthInches", 24f);
                Set(so, "caliber", winMag);
                SetEnum(so, "boltType", (int)BoltType.ManualBoltAction);
                SetFireModes(so, FireMode.Semi);
                Set(so, "roundsPerMinute", 60f);
                Set(so, "moaAccuracy", 0.6f);
                Set(so, "magazineCapacity", 5);
                Set(so, "boltHoldOpen", false);
                Set(so, "spareMagazines", 3);
                Set(so, "aimDownSightsTime", 0.42f);
                Set(so, "manualCycleTime", 1f);
                Set(so, "droppedMagazinePrefab", marksmanMagazine);
                Set(so, "hipStockOffset", new Vector3(0.14f, -0.2f, -0.18f));
                Set(so, WeaponDataSO.RecoilFieldName, marksmanRecoil);
                Set(so, "audioProfile", EffectsContentBuilder.WeaponAudio(MarksmanName));
            });

            AssetDatabase.SaveAssets();
            Debug.Log($"[FPS Builder] Test weapons {CarbineName}, {MarksmanName} and 4 calibers ready.");
        }

        internal static Material LitMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            string path = $"{ArtFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            ProjectSetupWizard.EnsureFolder(ArtFolder);
            material = new Material(Shader.Find("HDRP/Lit")) { name = materialName };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static WeaponModelRig BuildRifle(RifleSpec spec, Material metal, Material polymer)
        {
            Material lens = UnlitMaterial("Scope Lens", Color.white, null, false);
            Material reticle = UnlitMaterial("Scope Reticle", Color.black, ReticleTexture(), true);
            Mesh disc = DiscMesh();

            var root = new GameObject(spec.Name);
            try
            {
                Transform t = root.transform;
                const float handguardEnd = 0.43f;
                float stockEnd = -0.15f - spec.StockLength;
                float muzzle = handguardEnd + spec.BarrelExposed;

                Part(t, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.08f, 0.3f), Quaternion.identity, metal);
                Part(t, "Handguard", PrimitiveType.Cube, new Vector3(0f, 0.005f, 0.29f), new Vector3(0.045f, 0.055f, 0.28f), Quaternion.identity, polymer);
                Part(t, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0f, handguardEnd + spec.BarrelExposed * 0.5f),
                    new Vector3(0.02f, spec.BarrelExposed * 0.5f, 0.02f), Quaternion.Euler(90f, 0f, 0f), metal);
                Part(t, "Stock", PrimitiveType.Cube, new Vector3(0f, -0.015f, -0.15f - spec.StockLength * 0.5f),
                    new Vector3(0.04f, 0.09f, spec.StockLength), Quaternion.identity, polymer);
                Part(t, "Grip", PrimitiveType.Cube, new Vector3(0f, -0.075f, -0.07f), new Vector3(0.03f, 0.09f, 0.035f), Quaternion.Euler(15f, 0f, 0f), polymer);
                Transform magazine = Part(t, "Magazine", PrimitiveType.Cube, new Vector3(0f, -0.04f - spec.MagazineSize.y * 0.5f, 0.06f),
                    spec.MagazineSize, Quaternion.Euler(spec.MagazineAngle, 0f, 0f), polymer);

                // Scope: eyepiece fixed at z = -0.06, 7.5 cm above the bore; longer scopes extend forward.
                Part(t, "Scope Mount", PrimitiveType.Cube, new Vector3(0f, 0.05f, 0.02f), new Vector3(0.02f, 0.03f, 0.06f), Quaternion.identity, metal);
                Part(t, "Scope Body", PrimitiveType.Cylinder, new Vector3(0f, 0.075f, -0.06f + spec.ScopeLength * 0.5f),
                    new Vector3(spec.ScopeDiameter, spec.ScopeLength * 0.5f, spec.ScopeDiameter), Quaternion.Euler(90f, 0f, 0f), metal);
                Transform primary = Socket(t, WeaponModelRig.PrimarySightSocketName, new Vector3(0f, 0.075f, -0.06f), Quaternion.identity);
                float lensRadius = spec.ScopeDiameter * 0.4125f;
                MeshRenderer lensRenderer = Disc(primary, "Ocular Lens", disc, -0.001f, lensRadius, lens);
                Disc(primary, "Reticle", disc, -0.0015f, lensRadius, reticle);
                primary.gameObject.AddComponent<WeaponSight>().Configure(spec.Magnification, spec.EyeRelief, spec.AdsZoom, lensRenderer);

                if (spec.CantedIrons)
                {
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
                }

                Socket(t, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, muzzle), Quaternion.identity);
                Socket(t, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.02f, stockEnd), Quaternion.identity);
                Socket(t, WeaponModelRig.ChamberEjectionSocketName, new Vector3(0.026f, 0.01f, 0f), Quaternion.Euler(0f, 90f, 0f));
                Socket(t, WeaponModelRig.MagazineSocketName, magazine.localPosition, magazine.localRotation);
                Socket(t, WeaponModelRig.ChargingHandleSocketName, new Vector3(0f, 0.03f, -0.15f), Quaternion.identity);
                Socket(t, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, -0.035f, 0.3f), Quaternion.identity);
                Socket(t, WeaponModelRig.RightHandIKGripName, new Vector3(0f, -0.075f, -0.07f), Quaternion.Euler(15f, 0f, 0f));
                var rig = root.AddComponent<WeaponModelRig>();
                rig.EnsureSockets();
                Configure(rig, so =>
                {
                    SerializedProperty list = Property(so, WeaponModelRig.MagazineRenderersFieldName);
                    list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = magazine.GetComponent<Renderer>();
                });

                SetLayerRecursively(t, GameLayers.FirstPersonViewmodel);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(spec.Name)).GetComponent<WeaponModelRig>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Rigidbody DroppedMagazine(string prefabName, Vector3 size, float massKg, Material material)
        {
            GameObject magazine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                magazine.name = prefabName;
                magazine.transform.localScale = size;
                magazine.GetComponent<MeshRenderer>().sharedMaterial = material;
                magazine.layer = GameLayers.Debris;
                var body = magazine.AddComponent<Rigidbody>();
                body.mass = massKg;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                return PrefabUtility.SaveAsPrefabAsset(magazine, $"{PrefabFolder}/{prefabName}.prefab").GetComponent<Rigidbody>();
            }
            finally
            {
                Object.DestroyImmediate(magazine);
            }
        }

        private static CaliberDataSO Caliber(string assetName, string displayName, float grams, float diameterMm, float velocity,
            float referenceBarrel, float velocityPerInch, DragModel drag, float ballisticCoefficient, float penetration, float damage,
            string casingName)
        {
            CaliberDataSO caliber = LoadOrCreate<CaliberDataSO>($"{CaliberFolder}/{assetName}.asset");
            Configure(caliber, so =>
            {
                Set(so, "displayName", displayName);
                Set(so, "bulletMassGrams", grams);
                Set(so, "bulletDiameterMm", diameterMm);
                Set(so, "referenceVelocity", velocity);
                Set(so, "referenceBarrelInches", referenceBarrel);
                Set(so, "velocityPerInch", velocityPerInch);
                SetEnum(so, "dragModel", (int)drag);
                Set(so, "ballisticCoefficient", ballisticCoefficient);
                Set(so, "penetrationPower", penetration);
                Set(so, "damage", damage);
                Set(so, "casingPrefab", BallisticsContentBuilder.Casing(casingName));
            });
            return caliber;
        }

        private static Transform Part(Transform parent, string partName, PrimitiveType shape, Vector3 position, Vector3 size,
            Quaternion rotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = partName;
            Object.DestroyImmediate(part.GetComponent<Collider>()); // the viewmodel never collides; barrel tucking casts instead
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }

        private static Transform Socket(Transform parent, string socketName, Vector3 position, Quaternion rotation)
        {
            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.SetLocalPositionAndRotation(position, rotation);
            return socket;
        }

        private static MeshRenderer Disc(Transform parent, string discName, Mesh mesh, float depth, float radius, Material material)
        {
            var disc = new GameObject(discName);
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, 0f, depth);
            disc.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            disc.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = disc.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return meshRenderer;
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

        internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        internal static void Configure(Object asset, Action<SerializedObject> apply)
        {
            var serialized = new SerializedObject(asset);
            apply(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        internal static SerializedProperty Property(SerializedObject so, string field) =>
            so.FindProperty(field) ?? throw new InvalidOperationException($"{so.targetObject.GetType().Name} has no serialized field '{field}'.");

        internal static void Set(SerializedObject so, string field, float value) => Property(so, field).floatValue = value;
        internal static void Set(SerializedObject so, string field, int value) => Property(so, field).intValue = value;
        internal static void Set(SerializedObject so, string field, bool value) => Property(so, field).boolValue = value;
        internal static void Set(SerializedObject so, string field, string value) => Property(so, field).stringValue = value;
        internal static void Set(SerializedObject so, string field, Object value) => Property(so, field).objectReferenceValue = value;
        internal static void Set(SerializedObject so, string field, Vector3 value) => Property(so, field).vector3Value = value;
        internal static void SetEnum(SerializedObject so, string field, int index) => Property(so, field).enumValueIndex = index;

        private static void SetFireModes(SerializedObject so, params FireMode[] modes)
        {
            SerializedProperty list = Property(so, "fireModes");
            list.arraySize = modes.Length;
            for (int i = 0; i < modes.Length; i++) list.GetArrayElementAtIndex(i).enumValueIndex = (int)modes[i];
        }

        internal static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
