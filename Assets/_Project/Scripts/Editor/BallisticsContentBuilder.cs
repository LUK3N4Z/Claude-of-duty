using System;
using System.IO;
using FPS.Ballistics;
using FPS.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the ballistics content: surface materials with procedurally synthesized case-impact audio, pooled
    /// shell-casing prefabs per cartridge, armor pieces, and the range additions (simulation settings, penetration walls,
    /// armored and unarmored target dummies).
    /// </summary>
    public static class BallisticsContentBuilder
    {
        private const string SurfaceFolder = "Assets/_Project/Data/Ballistics/Surfaces";
        private const string ArmorFolder = "Assets/_Project/Data/Ballistics/Armor";
        private const string AudioFolder = "Assets/_Project/Art/Audio/Casings";
        private const string CasingFolder = "Assets/_Project/Prefabs/Ballistics";
        private const int SampleRate = 44100;

        private struct SurfaceSpec
        {
            public string Name;
            public float Density, Hardness, ExitDeflection, RicochetAngle, RicochetRetention, RicochetSpread;
            public float[] Partials;
            public float Decay, Noise;
        }

        private static readonly SurfaceSpec[] Surfaces =
        {
            new SurfaceSpec { Name = "Concrete", Density = 2400f, Hardness = 60f, ExitDeflection = 4f, RicochetAngle = 20f, RicochetRetention = 0.5f, RicochetSpread = 6f, Partials = new[] { 2900f, 5300f, 7900f }, Decay = 0.09f, Noise = 0.35f },
            new SurfaceSpec { Name = "Brick", Density = 1900f, Hardness = 45f, ExitDeflection = 4f, RicochetAngle = 15f, RicochetRetention = 0.45f, RicochetSpread = 8f, Partials = new[] { 2600f, 4800f, 7200f }, Decay = 0.08f, Noise = 0.4f },
            new SurfaceSpec { Name = "Wood", Density = 600f, Hardness = 35f, ExitDeflection = 2f, RicochetAngle = 8f, RicochetRetention = 0.4f, RicochetSpread = 10f, Partials = new[] { 900f, 1750f, 3100f }, Decay = 0.05f, Noise = 0.5f },
            new SurfaceSpec { Name = "Drywall", Density = 700f, Hardness = 3f, ExitDeflection = 1f, RicochetAngle = 3f, RicochetRetention = 0.3f, RicochetSpread = 10f, Partials = new[] { 600f, 1300f }, Decay = 0.04f, Noise = 0.6f },
            new SurfaceSpec { Name = "Metal", Density = 7850f, Hardness = 100f, ExitDeflection = 3f, RicochetAngle = 30f, RicochetRetention = 0.7f, RicochetSpread = 5f, Partials = new[] { 3400f, 6100f, 8800f, 11200f }, Decay = 0.3f, Noise = 0.2f },
            new SurfaceSpec { Name = "Glass", Density = 2500f, Hardness = 12f, ExitDeflection = 6f, RicochetAngle = 5f, RicochetRetention = 0.3f, RicochetSpread = 12f, Partials = new[] { 4200f, 7600f, 10100f }, Decay = 0.12f, Noise = 0.25f },
            new SurfaceSpec { Name = "Dirt", Density = 1600f, Hardness = 35f, ExitDeflection = 3f, RicochetAngle = 6f, RicochetRetention = 0.3f, RicochetSpread = 12f, Partials = new[] { 220f, 410f }, Decay = 0.03f, Noise = 0.8f },
            new SurfaceSpec { Name = "Flesh", Density = 1050f, Hardness = 15f, ExitDeflection = 3f, RicochetAngle = 0f, RicochetRetention = 0f, RicochetSpread = 0f, Partials = new float[0], Decay = 0f, Noise = 0f },
        };

        internal static SurfaceMaterialSO Surface(string name) => AssetDatabase.LoadAssetAtPath<SurfaceMaterialSO>($"{SurfaceFolder}/Surface_{name}.asset");
        internal static ArmorPieceSO Armor(string name) => AssetDatabase.LoadAssetAtPath<ArmorPieceSO>($"{ArmorFolder}/{name}.asset");
        internal static PhysicalShellCasing Casing(string name) =>
            AssetDatabase.LoadAssetAtPath<PhysicalShellCasing>($"{CasingFolder}/{name}.prefab");

        public static void CreateContent()
        {
            ProjectSetupWizard.EnsureFolder(SurfaceFolder);
            ProjectSetupWizard.EnsureFolder(ArmorFolder);
            ProjectSetupWizard.EnsureFolder(AudioFolder);
            ProjectSetupWizard.EnsureFolder(CasingFolder);

            foreach (SurfaceSpec spec in Surfaces)
            {
                AudioClip[] clips = spec.Partials.Length == 0
                    ? new AudioClip[0]
                    : new[] { Clink(spec, 0, 1f), Clink(spec, 1, 1.04f), Clink(spec, 2, 0.96f) };
                SurfaceMaterialSO surface = TestWeaponBuilder.LoadOrCreate<SurfaceMaterialSO>($"{SurfaceFolder}/Surface_{spec.Name}.asset");
                TestWeaponBuilder.Configure(surface, so =>
                {
                    TestWeaponBuilder.Set(so, "displayName", spec.Name);
                    TestWeaponBuilder.Set(so, "densityKgM3", spec.Density);
                    TestWeaponBuilder.Set(so, "hardness", spec.Hardness);
                    TestWeaponBuilder.Set(so, "exitDeflection", spec.ExitDeflection);
                    TestWeaponBuilder.Set(so, "ricochetAngle", spec.RicochetAngle);
                    TestWeaponBuilder.Set(so, "ricochetSpeedRetention", spec.RicochetRetention);
                    TestWeaponBuilder.Set(so, "ricochetSpread", spec.RicochetSpread);
                    SerializedProperty list = TestWeaponBuilder.Property(so, "casingImpactClips");
                    list.arraySize = clips.Length;
                    for (int i = 0; i < clips.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
                });
            }

            ArmorPiece("Plate_Front_Level4", "Level IV Ceramic Plate (Front)", ArmorCoverage.FrontPlate, 60f, 80f, 0.12f, 0.25f, 3.2f);
            ArmorPiece("Plate_Back_Level4", "Level IV Ceramic Plate (Back)", ArmorCoverage.BackPlate, 60f, 80f, 0.12f, 0.25f, 3.2f);
            ArmorPiece("Helmet_IIIA", "Level IIIA Helmet", ArmorCoverage.Helmet, 25f, 40f, 0.2f, 0.15f, 1.4f);

            Material brass = TestWeaponBuilder.LitMaterial("Brass", new Color(0.78f, 0.57f, 0.24f), 1f, 0.75f);
            SurfaceMaterialSO concrete = Surface("Concrete");
            CasingPrefab("Casing_556x45", 0.0096f, 0.045f, 0.012f, brass, concrete);
            CasingPrefab("Casing_762x51", 0.0119f, 0.051f, 0.012f, brass, concrete);
            CasingPrefab("Casing_9x19", 0.0099f, 0.019f, 0.004f, brass, concrete);
            CasingPrefab("Casing_300WM", 0.0135f, 0.066f, 0.017f, brass, concrete);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Adds the simulation settings, penetration walls and target dummies to the test range.</summary>
        internal static void BuildRange(Transform level)
        {
            var simulation = new GameObject("Ballistics Simulation").AddComponent<BallisticsSimulationManager>();
            simulation.transform.SetParent(level, false);
            simulation.DefaultSurface = Surface("Concrete");
            simulation.FleshSurface = Surface("Flesh");

            Transform walls = new GameObject("Penetration Walls").transform;
            walls.SetParent(level, false);
            Wall(walls, "Drywall 3cm", "Drywall", -14f, 0.03f);
            Wall(walls, "Wood Door 5cm", "Wood", -12f, 0.05f);
            Wall(walls, "Glass 1cm", "Glass", -10f, 0.01f);
            Wall(walls, "Steel 1cm", "Metal", -8f, 0.01f);
            Wall(walls, "Steel 3cm", "Metal", -6f, 0.03f);
            Wall(walls, "Brick 20cm", "Brick", -4f, 0.2f);
            Wall(walls, "Concrete 30cm", "Concrete", -2f, 0.3f);

            Dummy(level, "Dummy (unarmored)", new Vector3(4f, 0f, 15f), false);
            Dummy(level, "Dummy (plates + helmet)", new Vector3(7f, 0f, 15f), true);
        }

        private static void Wall(Transform parent, string wallName, string surface, float x, float thickness)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = wallName;
            wall.layer = GameLayers.WorldGeometry;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = new Vector3(x, 1f, 12f);
            wall.transform.localScale = new Vector3(1.6f, 2f, thickness);
            wall.AddComponent<SurfaceIdentifier>().Surface = Surface(surface);
            GameObjectUtility.SetStaticEditorFlags(wall, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        private static void Dummy(Transform parent, string dummyName, Vector3 position, bool armored)
        {
            Material skin = TestWeaponBuilder.LitMaterial("DummySkin", new Color(0.55f, 0.45f, 0.36f), 0f, 0.35f);
            Material gear = TestWeaponBuilder.LitMaterial("DummyGear", new Color(0.18f, 0.2f, 0.14f), 0f, 0.3f);
            SurfaceMaterialSO flesh = Surface("Flesh");

            var root = new GameObject(dummyName);
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f)); // facing the shooter
            TacticalHealthAndArmorSystem health = root.AddComponent<TacticalHealthAndArmorSystem>();

            Zone(root.transform, HitZone.Head, PrimitiveType.Sphere, new Vector3(0f, 1.62f, 0f), new Vector3(0.22f, 0.22f, 0.22f), skin, flesh);
            Zone(root.transform, HitZone.Thorax, PrimitiveType.Cube, new Vector3(0f, 1.33f, 0f), new Vector3(0.38f, 0.32f, 0.22f), skin, flesh);
            Zone(root.transform, HitZone.Stomach, PrimitiveType.Cube, new Vector3(0f, 1.04f, 0f), new Vector3(0.34f, 0.26f, 0.2f), skin, flesh);
            Zone(root.transform, HitZone.LeftArm, PrimitiveType.Capsule, new Vector3(-0.27f, 1.25f, 0f), new Vector3(0.09f, 0.3f, 0.09f), skin, flesh);
            Zone(root.transform, HitZone.RightArm, PrimitiveType.Capsule, new Vector3(0.27f, 1.25f, 0f), new Vector3(0.09f, 0.3f, 0.09f), skin, flesh);
            Zone(root.transform, HitZone.LeftLeg, PrimitiveType.Capsule, new Vector3(-0.1f, 0.45f, 0f), new Vector3(0.13f, 0.45f, 0.13f), skin, flesh);
            Zone(root.transform, HitZone.RightLeg, PrimitiveType.Capsule, new Vector3(0.1f, 0.45f, 0f), new Vector3(0.13f, 0.45f, 0.13f), skin, flesh);

            if (!armored) return;
            Visual(root.transform, "Front Plate", PrimitiveType.Cube, new Vector3(0f, 1.25f, 0.125f), new Vector3(0.3f, 0.38f, 0.025f), gear);
            Visual(root.transform, "Back Plate", PrimitiveType.Cube, new Vector3(0f, 1.25f, -0.125f), new Vector3(0.3f, 0.38f, 0.025f), gear);
            Visual(root.transform, "Helmet", PrimitiveType.Sphere, new Vector3(0f, 1.68f, -0.01f), new Vector3(0.25f, 0.17f, 0.25f), gear);
            TestWeaponBuilder.Configure(health, so =>
            {
                SerializedProperty slots = TestWeaponBuilder.Property(so, TacticalHealthAndArmorSystem.ArmorFieldName);
                string[] pieces = { "Plate_Front_Level4", "Plate_Back_Level4", "Helmet_IIIA" };
                slots.arraySize = pieces.Length;
                for (int i = 0; i < pieces.Length; i++)
                    slots.GetArrayElementAtIndex(i).FindPropertyRelative("piece").objectReferenceValue = Armor(pieces[i]);
            });
        }

        private static void Zone(Transform root, HitZone zone, PrimitiveType shape, Vector3 position, Vector3 size, Material material, SurfaceMaterialSO flesh)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = zone.ToString();
            part.layer = GameLayers.Hitbox;
            part.transform.SetParent(root, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            part.AddComponent<Hitbox>().Configure(zone, flesh);
        }

        private static void Visual(Transform root, string visualName, PrimitiveType shape, Vector3 position, Vector3 size, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = visualName;
            Object.DestroyImmediate(part.GetComponent<Collider>()); // armor is resolved by coverage, not geometry
            part.transform.SetParent(root, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void ArmorPiece(string assetName, string displayName, ArmorCoverage coverage, float rating, float durability,
            float blunt, float residual, float mass)
        {
            ArmorPieceSO piece = TestWeaponBuilder.LoadOrCreate<ArmorPieceSO>($"{ArmorFolder}/{assetName}.asset");
            TestWeaponBuilder.Configure(piece, so =>
            {
                TestWeaponBuilder.Set(so, "displayName", displayName);
                TestWeaponBuilder.SetEnum(so, "coverage", (int)coverage);
                TestWeaponBuilder.Set(so, "protectionRating", rating);
                TestWeaponBuilder.Set(so, "maxDurability", durability);
                TestWeaponBuilder.Set(so, "bluntTransfer", blunt);
                TestWeaponBuilder.Set(so, "residualAbsorption", residual);
                TestWeaponBuilder.Set(so, "massKg", mass);
            });
        }

        private static void CasingPrefab(string prefabName, float diameter, float length, float massKg, Material material, SurfaceMaterialSO fallback)
        {
            GameObject casing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            try
            {
                casing.name = prefabName;
                casing.layer = GameLayers.Debris;
                casing.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
                casing.GetComponent<MeshRenderer>().sharedMaterial = material;
                var body = casing.AddComponent<Rigidbody>();
                body.mass = massKg;
                body.linearDamping = 0.1f;
                body.angularDamping = 0.3f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var source = casing.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.minDistance = 0.4f;
                source.maxDistance = 25f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                casing.AddComponent<PhysicalShellCasing>().FallbackSurface = fallback;
                PrefabUtility.SaveAsPrefabAsset(casing, $"{CasingFolder}/{prefabName}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(casing);
            }
        }

        /// <summary>
        /// Synthesizes a case-on-surface impact: a short noise click plus decaying inharmonic partials, written as 16-bit
        /// mono WAV and imported as an AudioClip.
        /// </summary>
        private static AudioClip Clink(SurfaceSpec spec, int variant, float pitch)
        {
            string path = $"{AudioFolder}/Casing_{spec.Name}_{variant}.wav";
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;

            var random = new Random(spec.Name.GetHashCode() ^ (variant * 7919));
            float duration = Mathf.Max(0.12f, spec.Decay * 5f);
            int length = Mathf.CeilToInt(duration * SampleRate);
            var samples = new float[length];
            var phases = new float[spec.Partials.Length];
            for (int k = 0; k < phases.Length; k++) phases[k] = (float)random.NextDouble() * Mathf.PI * 2f;

            float peak = 1e-6f;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float sample = 0f;
                for (int k = 0; k < spec.Partials.Length; k++)
                {
                    float decay = spec.Decay * (1f - 0.18f * k); // higher partials die first
                    sample += Mathf.Sin(2f * Mathf.PI * spec.Partials[k] * pitch * t + phases[k]) * Mathf.Exp(-t / decay) / (1f + k * 0.6f);
                }

                sample += ((float)random.NextDouble() * 2f - 1f) * spec.Noise * Mathf.Exp(-t / 0.004f);
                samples[i] = sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);
            WriteWav(fullPath, samples, 0.9f / peak);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static void WriteWav(string path, float[] samples, float gain)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);
                foreach (float sample in samples) writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * gain * short.MaxValue), short.MinValue, short.MaxValue));
            }
        }
    }
}
