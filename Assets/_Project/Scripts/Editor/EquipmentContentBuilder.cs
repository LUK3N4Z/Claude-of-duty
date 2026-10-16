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
        internal const string SmokeName = "Grenade_M18";
        internal const string ChargeName = "Charge_C4";
        internal const string PlantedChargePath = PrefabFolder + "/PlantedCharge.prefab";

        internal static FPS.Gameplay.PlantedBomb PlantedCharge() => AssetDatabase.LoadAssetAtPath<FPS.Gameplay.PlantedBomb>(PlantedChargePath);

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
            ThrownGrenade fragBody = Body("Grenade_M67", PrimitiveType.Sphere, new Vector3(0.064f, 0.064f, 0.064f), bounce);
            ThrownGrenade flashBody = Body("Grenade_M84", PrimitiveType.Cylinder, new Vector3(0.045f, 0.065f, 0.045f), bounce);
            ThrownGrenade smokeBody = Body("Grenade_M18", PrimitiveType.Cylinder, new Vector3(0.062f, 0.075f, 0.062f), bounce);
            ThrownGrenade chargeBody = Body("Charge_C4", PrimitiveType.Cube, new Vector3(0.28f, 0.08f, 0.18f), bounce);

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

            Configure(LoadOrCreate<GrenadeDataSO>($"{DataFolder}/{SmokeName}.asset"), so =>
            {
                Set(so, "displayName", "M18 Smoke Grenade");
                SetEnum(so, "type", (int)GrenadeType.Smoke);
                Set(so, "prefab", smokeBody);
                Set(so, "massKg", 0.54f);
                Set(so, "fuseTime", 1.5f);
                Set(so, "throwSpeed", 14f);
                Set(so, "fragmentCount", 0);
                Set(so, "blastRadius", 0f);
                Set(so, "blastDamage", 0f);
                Set(so, "smokeRadius", 4.5f);
                Set(so, "smokeBuildUp", 3f);
                Set(so, "smokeDuration", 35f);
                Set(so, "smokeFade", 6f);
                Set(so, "smokeExtinction", 1.5f);
                Set(so, "flashLumen", 0f);
                Set(so, "suppressionRadius", 0f);
                Set(so, "smokeMaterial", EffectsContentBuilder.SmokeMaterial());
                Set(so, "smokeColor", new Color(0.72f, 0.72f, 0.7f, 0.85f));
            });
            GrenadeDataSO c4 = LoadOrCreate<GrenadeDataSO>($"{DataFolder}/{ChargeName}.asset");
            Configure(c4, so =>
            {
                Set(so, "displayName", "C4 Demolition Charge");
                SetEnum(so, "type", (int)GrenadeType.Fragmentation);
                Set(so, "prefab", chargeBody);
                Set(so, "massKg", 1.25f);
                Set(so, "fuseTime", 0.1f);
                Set(so, "fragmentCaliber", fragment);
                Set(so, "fragmentCount", 48);
                Set(so, "fragmentVelocity", 1500f);
                Set(so, "blastRadius", 10f);
                Set(so, "blastDamage", 450f);
                Set(so, "detonationAudio", EffectsContentBuilder.ExplosionAudio("Charge_C4", 1.6f, 0.5f));
                Set(so, "flashLumen", 8000000f);
                Set(so, "suppressionRadius", 30f);
                Set(so, "smokeMaterial", EffectsContentBuilder.SmokeMaterial());
                Set(so, "smokeColor", new Color(0.35f, 0.33f, 0.3f, 0.85f));
            });
            PlantedChargePrefab(c4);

            AssetDatabase.SaveAssets();
        }

        /// <summary>The armed charge left at a site: charge and clacker models, Interactable defuse collider, beep and countdown.</summary>
        private static void PlantedChargePrefab(GrenadeDataSO c4)
        {
            var root = new GameObject("PlantedCharge") { layer = GameLayers.Interactable };
            try
            {
                Transform charge = Socket(root.transform, "Charge", new Vector3(0f, 0.04f, 0f), Quaternion.identity);
                ProceduralProps.Equipment("Charge_C4", charge, 0);
                Transform clacker = Socket(root.transform, "Clacker", new Vector3(0.24f, 0.0225f, 0.06f), Quaternion.Euler(0f, -30f, 0f));
                ProceduralProps.Equipment("Clacker_M57", clacker, 0);
                SetLayerRecursively(root.transform, 0);
                root.layer = GameLayers.Interactable;
                var trigger = root.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 0.25f, 0f);
                trigger.size = new Vector3(1f, 0.8f, 1f);
                root.AddComponent<FPS.Gameplay.HoldInteraction>();
                var speaker = root.AddComponent<AudioSource>();
                speaker.playOnAwake = false;
                root.AddComponent<FPS.Gameplay.PlantedBomb>().Configure(c4, EffectsContentBuilder.BombBeep());
                PrefabUtility.SaveAsPrefabAsset(root, PlantedChargePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// A throwable: the model (<paramref name="prefabName"/> doubles as its Meshy asset id), a collider of the given
        /// primitive <paramref name="shape"/> and <paramref name="size"/> (unit-primitive scale), a kinematic body until thrown.
        /// </summary>
        private static ThrownGrenade Body(string prefabName, PrimitiveType shape, Vector3 size, PhysicsMaterial bounce)
        {
            var root = new GameObject(prefabName) { layer = GameLayers.Debris };
            try
            {
                ProceduralProps.Equipment(prefabName, root.transform, GameLayers.Debris);
                SetLayerRecursively(root.transform, GameLayers.Debris);

                if (shape == PrimitiveType.Sphere)
                {
                    var sphere = root.AddComponent<SphereCollider>();
                    sphere.radius = size.x * 0.5f;
                    sphere.sharedMaterial = bounce;
                }
                else if (shape == PrimitiveType.Cube)
                {
                    var box = root.AddComponent<BoxCollider>();
                    box.size = size;
                    box.sharedMaterial = bounce;
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
