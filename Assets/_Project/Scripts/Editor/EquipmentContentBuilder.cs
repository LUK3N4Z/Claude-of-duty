using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using UnityEditor;
using UnityEngine;
using static FPS.EditorTools.TestWeaponBuilder;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the hand grenades: an M67-style fragmentation grenade (64 steel fragments run through the ballistics
    /// simulation, blast overpressure inside 4 m) and an M84-style stun grenade, with pooled bouncing bodies, a fragment
    /// caliber and synthesized detonation reports.
    /// </summary>
    public static class EquipmentContentBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Equipment";
        private const string DataFolder = "Assets/_Project/Data/Equipment";
        private const string CaliberFolder = "Assets/_Project/Data/Ballistics";
        internal const string FragName = "Grenade_M67";
        internal const string FlashName = "Grenade_M84";

        internal static GrenadeDataSO Grenade(string grenadeName) => AssetDatabase.LoadAssetAtPath<GrenadeDataSO>($"{DataFolder}/{grenadeName}.asset");

        public static void CreateContent()
        {
            ProjectSetupWizard.EnsureFolder(PrefabFolder);
            ProjectSetupWizard.EnsureFolder(DataFolder);

            CaliberDataSO fragment = LoadOrCreate<CaliberDataSO>($"{CaliberFolder}/Caliber_Fragment.asset");
            Configure(fragment, so =>
            {
                Set(so, "displayName", "Steel grenade fragment (~0.5 g)");
                Set(so, "bulletMassGrams", 0.5f);
                Set(so, "bulletDiameterMm", 4f);
                Set(so, "referenceVelocity", 1300f);
                Set(so, "referenceBarrelInches", 0f);
                Set(so, "velocityPerInch", 0f);
                SetEnum(so, "dragModel", (int)DragModel.G1);
                Set(so, "ballisticCoefficient", 0.012f); // irregular, light: sheds speed within metres
                Set(so, "penetrationPower", 12f);
                Set(so, "damage", 22f);
            });

            PhysicsMaterial bounce = LoadOrCreatePhysicsMaterial();
            Material olive = LitMaterial("GrenadeOlive", new Color(0.2f, 0.24f, 0.15f), 0.2f, 0.35f);
            Material black = LitMaterial("GrenadeBlack", new Color(0.04f, 0.04f, 0.04f), 0.3f, 0.4f);
            Material steel = LitMaterial("GrenadeSpoon", new Color(0.5f, 0.5f, 0.48f), 0.9f, 0.5f);

            ThrownGrenade fragBody = Body("Grenade_M67", PrimitiveType.Sphere, new Vector3(0.064f, 0.064f, 0.064f), olive, steel, bounce);
            ThrownGrenade flashBody = Body("Grenade_M84", PrimitiveType.Cylinder, new Vector3(0.045f, 0.065f, 0.045f), black, steel, bounce);

            Configure(LoadOrCreate<GrenadeDataSO>($"{DataFolder}/{FragName}.asset"), so =>
            {
                Set(so, "displayName", "M67 Fragmentation Grenade");
                SetEnum(so, "type", (int)GrenadeType.Fragmentation);
                Set(so, "prefab", fragBody);
                Set(so, "massKg", 0.4f);
                Set(so, "fuseTime", 4.5f);
                Set(so, "throwSpeed", 16f);
                Set(so, "fragmentCaliber", fragment);
                Set(so, "fragmentCount", 64);
                Set(so, "fragmentVelocity", 1300f);
                Set(so, "blastRadius", 4f);
                Set(so, "blastDamage", 140f);
                Set(so, "detonationAudio", EffectsContentBuilder.ExplosionAudio("Grenade_Frag", 1f, 0.6f));
                Set(so, "flashLumen", 2000000f);
                Set(so, "suppressionRadius", 18f);
                Set(so, "smokeMaterial", EffectsContentBuilder.SmokeMaterial());
                Set(so, "smokeColor", new Color(0.42f, 0.4f, 0.36f, 0.8f));
            });
            Configure(LoadOrCreate<GrenadeDataSO>($"{DataFolder}/{FlashName}.asset"), so =>
            {
                Set(so, "displayName", "M84 Stun Grenade");
                SetEnum(so, "type", (int)GrenadeType.Flashbang);
                Set(so, "prefab", flashBody);
                Set(so, "massKg", 0.24f);
                Set(so, "fuseTime", 1.5f);
                Set(so, "throwSpeed", 14f);
                Set(so, "fragmentCount", 0);
                Set(so, "blastRadius", 0f);
                Set(so, "blastDamage", 0f);
                Set(so, "flashIntensity", 1f);
                Set(so, "detonationAudio", EffectsContentBuilder.ExplosionAudio("Grenade_Stun", 0.7f, 1f));
                Set(so, "flashLumen", 6000000f);
                Set(so, "suppressionRadius", 8f);
                Set(so, "smokeMaterial", EffectsContentBuilder.SmokeMaterial());
                Set(so, "smokeColor", new Color(0.75f, 0.75f, 0.75f, 0.6f));
            });

            AssetDatabase.SaveAssets();
        }

        private static ThrownGrenade Body(string prefabName, PrimitiveType shape, Vector3 size, Material bodyMaterial, Material spoonMaterial,
            PhysicsMaterial bounce)
        {
            var root = new GameObject(prefabName) { layer = GameLayers.Debris };
            try
            {
                Transform model = Part(root.transform, "Body", shape, Vector3.zero, size, Quaternion.identity, bodyMaterial);
                Part(root.transform, "Spoon", PrimitiveType.Cube, new Vector3(size.x * 0.55f, size.y * 0.25f, 0f), new Vector3(0.006f, size.y * 1.1f, 0.012f),
                    Quaternion.identity, spoonMaterial);
                Part(root.transform, "Fuse", PrimitiveType.Cylinder, new Vector3(0f, size.y * (shape == PrimitiveType.Sphere ? 0.5f : 1f), 0f),
                    new Vector3(0.018f, 0.012f, 0.018f), Quaternion.identity, spoonMaterial);
                SetLayerRecursively(root.transform, GameLayers.Debris);
                model.gameObject.layer = GameLayers.Debris;

                if (shape == PrimitiveType.Sphere)
                {
                    var sphere = root.AddComponent<SphereCollider>();
                    sphere.radius = size.x * 0.5f;
                    sphere.sharedMaterial = bounce;
                }
                else
                {
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.radius = size.x * 0.5f;
                    capsule.height = size.y * 2f;
                    capsule.sharedMaterial = bounce;
                }

                var body = root.AddComponent<Rigidbody>();
                body.mass = 0.4f;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.6f;
                body.isKinematic = true; // woken by Throw
                root.AddComponent<ThrownGrenade>();
                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{prefabName}.prefab").GetComponent<ThrownGrenade>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static PhysicsMaterial LoadOrCreatePhysicsMaterial()
        {
            string path = $"{PrefabFolder}/GrenadeBounce.asset";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material != null) return material;

            material = new PhysicsMaterial("GrenadeBounce")
            {
                bounciness = 0.3f,
                dynamicFriction = 0.6f,
                staticFriction = 0.7f,
                bounceCombine = PhysicsMaterialCombine.Average,
                frictionCombine = PhysicsMaterialCombine.Average,
            };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
