using System;
using FPS.Ballistics;
using FPS.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the ballistics content: surface materials with procedurally synthesized case-impact and footstep audio
    /// (steps, crawl movements, landings), pooled shell-casing prefabs per cartridge, armor pieces, and the range additions
    /// (simulation settings, penetration walls, armored and unarmored target dummies).
    /// </summary>
    public static class BallisticsContentBuilder
    {
        private const string SurfaceFolder = "Assets/_Project/Data/Ballistics/Surfaces";
        private const string ArmorFolder = "Assets/_Project/Data/Ballistics/Armor";
        private const string AudioFolder = "Assets/_Project/Art/Audio/Casings";
        private const string FootstepAudioFolder = "Assets/_Project/Art/Audio/Footsteps";
        private const string CasingFolder = "Assets/_Project/Prefabs/Ballistics";

        private struct SurfaceSpec
        {
            public string Name;
            public float Density, Hardness, ExitDeflection, RicochetAngle, RicochetRetention, RicochetSpread;
            public float[] Partials;
            public float Decay, Noise;
            public StepSpec Step;
        }

        /// <summary>How feet sound on a surface; a zero <c>ThudHz</c> makes no footstep audio.</summary>
        private struct StepSpec
        {
            /// <summary>Body thump of the sole landing (Hz) and the low-pass of its noise (Hz).</summary>
            public float ThudHz, ThudCutoff;
            /// <summary>Surface scuff: level against the thud, band (Hz) and decay (s).</summary>
            public float Grit, GritLow, GritHigh, GritDecay;
            /// <summary>Crunch grains (gravel, shards) per full-weight contact.</summary>
            public int Grains;
            /// <summary>Modes of a hollow or metal floor (Hz) and their decay (s).</summary>
            public float[] Ring;
            public float RingDecay;
        }

        private static readonly SurfaceSpec[] Surfaces =
        {
            new SurfaceSpec { Name = "Concrete", Density = 2400f, Hardness = 60f, ExitDeflection = 4f, RicochetAngle = 20f, RicochetRetention = 0.5f, RicochetSpread = 6f, Partials = new[] { 2900f, 5300f, 7900f }, Decay = 0.09f, Noise = 0.35f,
                Step = new StepSpec { ThudHz = 85f, ThudCutoff = 900f, Grit = 0.5f, GritLow = 1500f, GritHigh = 7000f, GritDecay = 0.02f, Grains = 5 } },
            new SurfaceSpec { Name = "Brick", Density = 1900f, Hardness = 45f, ExitDeflection = 4f, RicochetAngle = 15f, RicochetRetention = 0.45f, RicochetSpread = 8f, Partials = new[] { 2600f, 4800f, 7200f }, Decay = 0.08f, Noise = 0.4f,
                Step = new StepSpec { ThudHz = 80f, ThudCutoff = 800f, Grit = 0.55f, GritLow = 1200f, GritHigh = 6000f, GritDecay = 0.025f, Grains = 8 } },
            new SurfaceSpec { Name = "Wood", Density = 600f, Hardness = 35f, ExitDeflection = 2f, RicochetAngle = 8f, RicochetRetention = 0.4f, RicochetSpread = 10f, Partials = new[] { 900f, 1750f, 3100f }, Decay = 0.05f, Noise = 0.5f,
                Step = new StepSpec { ThudHz = 110f, ThudCutoff = 600f, Grit = 0.25f, GritLow = 700f, GritHigh = 4000f, GritDecay = 0.015f, Grains = 1, Ring = new[] { 170f, 390f, 720f }, RingDecay = 0.05f } },
            new SurfaceSpec { Name = "Drywall", Density = 700f, Hardness = 3f, ExitDeflection = 1f, RicochetAngle = 3f, RicochetRetention = 0.3f, RicochetSpread = 10f, Partials = new[] { 600f, 1300f }, Decay = 0.04f, Noise = 0.6f,
                Step = new StepSpec { ThudHz = 95f, ThudCutoff = 500f, Grit = 0.3f, GritLow = 600f, GritHigh = 3000f, GritDecay = 0.02f, Grains = 2, Ring = new[] { 140f, 330f }, RingDecay = 0.035f } },
            new SurfaceSpec { Name = "Metal", Density = 7850f, Hardness = 100f, ExitDeflection = 3f, RicochetAngle = 30f, RicochetRetention = 0.7f, RicochetSpread = 5f, Partials = new[] { 3400f, 6100f, 8800f, 11200f }, Decay = 0.3f, Noise = 0.2f,
                Step = new StepSpec { ThudHz = 75f, ThudCutoff = 700f, Grit = 0.25f, GritLow = 2000f, GritHigh = 8000f, GritDecay = 0.012f, Grains = 1, Ring = new[] { 310f, 760f, 1450f, 2380f }, RingDecay = 0.2f } },
            new SurfaceSpec { Name = "Glass", Density = 2500f, Hardness = 12f, ExitDeflection = 6f, RicochetAngle = 5f, RicochetRetention = 0.3f, RicochetSpread = 12f, Partials = new[] { 4200f, 7600f, 10100f }, Decay = 0.12f, Noise = 0.25f,
                Step = new StepSpec { ThudHz = 90f, ThudCutoff = 900f, Grit = 0.4f, GritLow = 3000f, GritHigh = 11000f, GritDecay = 0.02f, Grains = 14, Ring = new[] { 2900f, 5200f }, RingDecay = 0.04f } },
            new SurfaceSpec { Name = "Dirt", Density = 1600f, Hardness = 35f, ExitDeflection = 3f, RicochetAngle = 6f, RicochetRetention = 0.3f, RicochetSpread = 12f, Partials = new[] { 220f, 410f }, Decay = 0.03f, Noise = 0.8f,
                Step = new StepSpec { ThudHz = 65f, ThudCutoff = 400f, Grit = 0.8f, GritLow = 500f, GritHigh = 3500f, GritDecay = 0.05f, Grains = 22 } },
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
            ProjectSetupWizard.EnsureFolder(FootstepAudioFolder);
            ProjectSetupWizard.EnsureFolder(CasingFolder);

            foreach (SurfaceSpec spec in Surfaces)
            {
                AudioClip[] clips = spec.Partials.Length == 0
                    ? new AudioClip[0]
                    : new[] { Clink(spec, 0, 1f), Clink(spec, 1, 1.04f), Clink(spec, 2, 0.96f) };
                bool walkable = spec.Step.ThudHz > 0f;
                AudioClip[] steps = walkable ? new[] { Footstep(spec, 0), Footstep(spec, 1), Footstep(spec, 2), Footstep(spec, 3) } : new AudioClip[0];
                AudioClip[] crawls = walkable ? new[] { Crawl(spec, 0), Crawl(spec, 1) } : new AudioClip[0];
                AudioClip[] landings = walkable ? new[] { Landing(spec, 0), Landing(spec, 1) } : new AudioClip[0];
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
                    SetClips(so, "casingImpactClips", clips);
                    SetClips(so, "footstepClips", steps);
                    SetClips(so, "crawlClips", crawls);
                    SetClips(so, "landingClips", landings);
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

        internal static void Dummy(Transform parent, string dummyName, Vector3 position, bool armored, float yaw = 180f)
        {
            Material skin = TestWeaponBuilder.LitMaterial("DummySkin", new Color(0.55f, 0.45f, 0.36f), 0f, 0.35f);
            Material gear = TestWeaponBuilder.LitMaterial("DummyGear", new Color(0.18f, 0.2f, 0.14f), 0f, 0.3f);
            SurfaceMaterialSO flesh = Surface("Flesh");

            var root = new GameObject(dummyName);
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f)); // facing the shooter
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

        private static void SetClips(SerializedObject so, string field, AudioClip[] clips)
        {
            SerializedProperty list = TestWeaponBuilder.Property(so, field);
            list.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        /// <summary>A foot strike: the heel, then the toe after a roll that varies per variant.</summary>
        private static AudioClip Footstep(SurfaceSpec spec, int variant)
        {
            StepSpec step = spec.Step;
            float toe = 0.055f + 0.012f * variant;
            return AudioSynth.LoadOrCreate($"{FootstepAudioFolder}/Step_{spec.Name}_{variant}.wav", () =>
                Contacts(step, Mathf.Max(0.28f, toe + 4f * step.RingDecay), spec.Name.Length * 6151 + variant, 0.97f + 0.02f * variant,
                    new[] { 0.004f, toe }, new[] { 1f, 0.55f }, 0.0015f, 1f, 0.6f));
        }

        /// <summary>Both feet coming down hard a moment apart, the body weight in a deeper thump.</summary>
        private static AudioClip Landing(SurfaceSpec spec, int variant)
        {
            StepSpec step = spec.Step;
            return AudioSynth.LoadOrCreate($"{FootstepAudioFolder}/Land_{spec.Name}_{variant}.wav", () =>
                Contacts(step, Mathf.Max(0.45f, 0.1f + 4f * step.RingDecay), spec.Name.Length * 7307 + variant, 0.85f + 0.05f * variant,
                    new[] { 0.003f, 0.02f + 0.008f * variant }, new[] { 1.4f, 1.2f }, 0.001f, 1.6f, 0.9f));
        }

        /// <summary>A prone crawl movement: an elbow then a knee dragging on the surface, with the uniform rustling.</summary>
        private static AudioClip Crawl(SurfaceSpec spec, int variant)
        {
            StepSpec step = spec.Step;
            int seed = spec.Name.Length * 8209 + variant;
            float seconds = Mathf.Max(0.9f, 0.3f + 4f * step.RingDecay); // a metal deck rings on after the knee
            return AudioSynth.LoadOrCreate($"{FootstepAudioFolder}/Crawl_{spec.Name}_{variant}.wav", () =>
            {
                var cloth = new AudioSynth(seconds, seed + 2).Noise(0.5f, 0.01f, 0.06f, 0.15f).Noise(0.4f, 0.22f, 0.05f, 0.15f).BandPass(1800f, 7000f);
                return Contacts(step, seconds, seed, 1f, new[] { 0.02f, 0.2f + 0.03f * variant }, new[] { 0.5f, 0.35f }, 0.04f, 3.5f, 0f)
                    .Mix(cloth, 0.6f);
            });
        }

        /// <summary>
        /// Contacts with a surface at <paramref name="times"/> (s), scaled by <paramref name="weights"/>: low-passed thud
        /// noise and a sine thump (<paramref name="thump"/>), the band-passed scuff with its crunch grains (decays and
        /// grain spread stretched by <paramref name="stretch"/>), and a hollow or metal floor's ringing.
        /// </summary>
        private static AudioSynth Contacts(StepSpec s, float seconds, int seed, float pitch, float[] times, float[] weights,
            float attack, float stretch, float thump)
        {
            var random = new System.Random(seed);
            var thud = new AudioSynth(seconds, seed);
            var scuff = new AudioSynth(seconds, seed + 1);
            for (int i = 0; i < times.Length; i++)
            {
                float t = times[i], w = weights[i];
                thud.Noise(w, t, attack, 0.02f * stretch);
                scuff.Noise(s.Grit * w, t, attack, s.GritDecay * stretch);
                int grains = Mathf.RoundToInt(s.Grains * w);
                for (int g = 0; g < grains; g++)
                {
                    float level = 0.3f + 0.7f * (float)random.NextDouble();
                    float start = t + (float)random.NextDouble() * 0.09f * stretch;
                    float brightness = Mathf.Lerp(s.GritLow, s.GritHigh / 2.41f, (float)random.NextDouble()); // top partial stays in the band
                    scuff.Click(s.Grit * w * level, start, brightness);
                }
            }

            thud.LowPass(s.ThudCutoff).Mix(scuff.BandPass(s.GritLow, s.GritHigh), 1f);
            for (int i = 0; i < times.Length; i++)
            {
                if (thump > 0f) thud.Tone(s.ThudHz * 1.4f * pitch, s.ThudHz * pitch, thump * weights[i], times[i], 0.002f, 0.035f, 0.025f);
                if (s.Ring != null) thud.Partials(s.Ring, 0.35f * weights[i], times[i], s.RingDecay, pitch);
            }

            return thud;
        }

        /// <summary>A case-on-surface clink: decaying inharmonic partials plus a short noise click.</summary>
        private static AudioClip Clink(SurfaceSpec spec, int variant, float pitch)
        {
            int seed = spec.Name.Length * 7919 + variant;
            return AudioSynth.LoadOrCreate($"{AudioFolder}/Casing_{spec.Name}_{variant}.wav", () =>
                new AudioSynth(Mathf.Max(0.12f, spec.Decay * 5f), seed)
                    .Partials(spec.Partials, 1f, 0f, spec.Decay, pitch)
                    .Noise(spec.Noise, 0f, 0.0005f, 0.004f));
        }
    }
}
