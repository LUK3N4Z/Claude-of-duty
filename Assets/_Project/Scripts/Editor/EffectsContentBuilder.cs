using System;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the audio-visual content: synthesized weapon reports (transient, mechanism, body, distant, three reverb
    /// tails), handling foley, supersonic cracks and per-surface impacts; flipbook, bullet-hole and distortion textures;
    /// dust, muzzle gas, smoke, heat-haze and per-surface decal materials. Wires them into surfaces and the player.
    /// </summary>
    public static class EffectsContentBuilder
    {
        private const string AudioFolder = "Assets/_Project/Art/Audio";
        private const string VfxFolder = "Assets/_Project/Art/VFX";
        private const string AudioDataFolder = "Assets/_Project/Data/Audio";

        private static string Clip(string folder, string clipName) => $"{AudioFolder}/{folder}/{clipName}.wav";
        internal static WeaponAudioProfileSO WeaponAudio(string weapon) =>
            AssetDatabase.LoadAssetAtPath<WeaponAudioProfileSO>($"{AudioDataFolder}/{weapon}_Audio.asset");
        private static Material Vfx(string materialName) => AssetDatabase.LoadAssetAtPath<Material>($"{VfxFolder}/{materialName}.mat");

        public static void CreateContent()
        {
            foreach (string folder in new[] { "Weapons", "Foley", "Impacts" }) ProjectSetupWizard.EnsureFolder($"{AudioFolder}/{folder}");
            ProjectSetupWizard.EnsureFolder(VfxFolder);
            ProjectSetupWizard.EnsureFolder(AudioDataFolder);

            Texture2D puff = Texture("PuffFlipbook", false, FlipbookPixel, 256);
            Texture2D hole = Texture("BulletHole", false, BulletHolePixel, 128);
            Texture2D haze = Texture("HeatDistortion", true, DistortionPixel, 128);
            Unlit("ImpactDust", Color.white, puff, false, false);
            Unlit("MuzzleGas", new Color(1f, 0.75f, 0.45f, 0.9f), puff, true, false);
            Unlit("BreechSmoke", new Color(0.55f, 0.55f, 0.55f, 0.45f), puff, false, false);
            Unlit("HeatShimmer", Color.white, haze, false, true);

            Surface("Concrete", new Color(0.17f, 0.16f, 0.15f), new Color(0.6f, 0.58f, 0.55f, 0.8f), hole,
                s => s.Noise(1f, 0f, 0.0005f, 0.02f).HighPass(1200f).Noise(0.3f, 0.01f, 0.01f, 0.12f).LowPass(5000f));
            Surface("Brick", new Color(0.2f, 0.12f, 0.1f), new Color(0.55f, 0.35f, 0.28f, 0.8f), hole,
                s => s.Noise(1f, 0f, 0.0005f, 0.025f).HighPass(800f).Noise(0.35f, 0.01f, 0.01f, 0.12f).LowPass(3500f));
            Surface("Wood", new Color(0.12f, 0.08f, 0.05f), new Color(0.45f, 0.35f, 0.22f, 0.8f), hole,
                s => s.Tone(320f, 280f, 0.8f, 0f, 0.001f, 0.04f).Noise(0.5f, 0f, 0.0005f, 0.02f).LowPass(4000f));
            Surface("Drywall", new Color(0.26f, 0.25f, 0.24f), new Color(0.85f, 0.84f, 0.82f, 0.85f), hole,
                s => s.Noise(0.8f, 0f, 0.001f, 0.03f).LowPass(1800f).Tone(180f, 160f, 0.4f, 0f, 0.001f, 0.03f));
            Surface("Metal", new Color(0.08f, 0.08f, 0.09f), new Color(1f, 0.78f, 0.45f, 0.9f), hole,
                s => s.Partials(new[] { 1800f, 3300f, 5100f, 7400f }, 0.9f, 0f, 0.25f).Noise(0.4f, 0f, 0.0003f, 0.005f));
            Surface("Glass", new Color(0.55f, 0.58f, 0.62f), new Color(0.85f, 0.9f, 0.95f, 0.6f), hole,
                s => s.Noise(0.6f, 0f, 0.0005f, 0.06f).HighPass(3000f).Partials(new[] { 3800f, 6100f, 8900f }, 0.6f, 0f, 0.08f));
            Surface("Dirt", new Color(0.1f, 0.08f, 0.06f), new Color(0.42f, 0.34f, 0.25f, 0.85f), hole,
                s => s.Noise(0.8f, 0f, 0.002f, 0.03f).LowPass(600f).Tone(90f, 60f, 0.5f, 0f, 0.002f, 0.05f));
            Surface("Flesh", Color.clear, new Color(0.35f, 0.04f, 0.03f, 0.7f), null,
                s => s.Noise(0.4f, 0f, 0.002f, 0.03f).LowPass(900f).Tone(140f, 90f, 0.8f, 0f, 0.002f, 0.05f));

            WeaponReport(TestWeaponBuilder.CarbineName, 1f, 1f, false);
            WeaponReport(TestWeaponBuilder.MarksmanName, 1.35f, 0.8f, true);
            WeaponReport(TestWeaponBuilder.BattleRifleName, 1.2f, 0.88f, false);
            WeaponReport(TestWeaponBuilder.SidearmName, 0.62f, 1.35f, false);

            AudioSynth.LoadOrCreate(Clip("Foley", "DryFire"), () => new AudioSynth(0.08f, 11).Partials(new[] { 3200f, 5600f }, 0.7f, 0f, 0.01f).Noise(0.2f, 0f, 0.0003f, 0.004f));
            AudioSynth.LoadOrCreate(Clip("Foley", "MagazineOut"), () => new AudioSynth(0.25f, 12).Noise(0.25f, 0f, 0.02f, 0.08f).HighPass(1500f).Partials(new[] { 1800f, 3400f }, 0.5f, 0.09f, 0.02f));
            AudioSynth.LoadOrCreate(Clip("Foley", "MagazineIn"), () => new AudioSynth(0.2f, 13).Tone(220f, 200f, 0.4f, 0f, 0.001f, 0.04f).Partials(new[] { 1500f, 2900f, 4700f }, 0.7f, 0f, 0.02f).Partials(new[] { 2100f, 3900f }, 0.5f, 0.06f, 0.015f));
            AudioSynth.LoadOrCreate(Clip("Foley", "BoltCycle"), () => new AudioSynth(0.3f, 14).Partials(new[] { 2600f, 4400f }, 0.6f, 0f, 0.015f).Noise(0.2f, 0.02f, 0.01f, 0.05f).Partials(new[] { 2000f, 3800f }, 0.7f, 0.16f, 0.02f));
            AudioSynth.LoadOrCreate(Clip("Foley", "FireModeSwitch"), () => new AudioSynth(0.06f, 15).Partials(new[] { 4200f, 6900f }, 0.5f, 0f, 0.006f));
            AudioSynth.LoadOrCreate(Clip("Foley", "MagazineCheck"), () => new AudioSynth(0.25f, 16).Partials(new[] { 1700f, 3300f }, 0.3f, 0f, 0.02f).Partials(new[] { 1600f, 3100f }, 0.3f, 0.12f, 0.02f));
            for (int i = 0; i < 2; i++)
            {
                int seed = 20 + i;
                AudioSynth.LoadOrCreate(Clip("Foley", $"SupersonicCrack_{i}"), () => new AudioSynth(0.12f, seed)
                    .NWave(1f, 0f, 0.0012f).Noise(0.5f, 0.0012f, 0.0005f, 0.012f).HighPass(2500f).Echo(0.021f, 0.2f));
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Assigns foley, cracks and VFX materials to a built player.</summary>
        internal static void ConfigurePlayer(GameObject player)
        {
            TestWeaponBuilder.Configure(player.GetComponent<AcousticPropagationSystem>(), so =>
            {
                TestWeaponBuilder.Set(so, "dryFire", Load(Clip("Foley", "DryFire")));
                TestWeaponBuilder.Set(so, "magazineOut", Load(Clip("Foley", "MagazineOut")));
                TestWeaponBuilder.Set(so, "magazineIn", Load(Clip("Foley", "MagazineIn")));
                TestWeaponBuilder.Set(so, "boltCycle", Load(Clip("Foley", "BoltCycle")));
                TestWeaponBuilder.Set(so, "fireModeSwitch", Load(Clip("Foley", "FireModeSwitch")));
                TestWeaponBuilder.Set(so, "magazineCheck", Load(Clip("Foley", "MagazineCheck")));
                SetClips(TestWeaponBuilder.Property(so, "supersonicCracks"), Load(Clip("Foley", "SupersonicCrack_0")), Load(Clip("Foley", "SupersonicCrack_1")));
                TestWeaponBuilder.Set(so, "tinnitus", Tinnitus());
            });
            TestWeaponBuilder.Configure(player.GetComponent<ImpactEffectsController>(), so => TestWeaponBuilder.Set(so, "dustMaterial", Vfx("ImpactDust")));
            TestWeaponBuilder.Configure(player.GetComponent<MuzzleAndSmokeVFXController>(), so =>
            {
                TestWeaponBuilder.Set(so, "gasMaterial", Vfx("MuzzleGas"));
                TestWeaponBuilder.Set(so, "smokeMaterial", Vfx("BreechSmoke"));
                TestWeaponBuilder.Set(so, "shimmerMaterial", Vfx("HeatShimmer"));
            });
        }

        private static void WeaponReport(string weapon, float power, float pitch, bool boltAction)
        {
            string Path(string layer, int variant) => Clip("Weapons", $"{weapon}_{layer}_{variant}");
            int seed = weapon.Length * 101;
            var transients = new AudioClip[3];
            for (int i = 0; i < transients.Length; i++)
            {
                int s = seed + i;
                transients[i] = AudioSynth.LoadOrCreate(Path("Transient", i), () => new AudioSynth(0.35f * power, s)
                    .Noise(1f, 0f, 0.0003f, 0.018f * power).Tone(160f * pitch, 55f * pitch, 0.9f * power, 0f, 0.001f, 0.06f * power, 0.04f)
                    .Noise(0.25f, 0f, 0.002f, 0.08f * power).LowPass(9000f));
            }

            AudioClip mechanism = AudioSynth.LoadOrCreate(Path("Mechanism", 0), () => boltAction
                ? new AudioSynth(0.08f, seed + 10).Partials(new[] { 3000f, 5200f }, 0.4f, 0f, 0.008f) // firing pin only
                : new AudioSynth(0.15f, seed + 10).Partials(new[] { 2300f, 4100f, 6200f }, 0.6f, 0f, 0.012f)
                    .Partials(new[] { 1900f, 3600f }, 0.45f, 0.045f, 0.015f).Noise(0.15f, 0f, 0.0005f, 0.01f).HighPass(800f));
            AudioClip body = AudioSynth.LoadOrCreate(Path("Body", 0), () => new AudioSynth(0.4f * power, seed + 20)
                .Tone(190f * pitch, 190f * pitch, 0.6f, 0f, 0.002f, 0.12f * power).Tone(410f * pitch, 400f * pitch, 0.35f, 0f, 0.002f, 0.08f)
                .Tone(620f * pitch, 610f * pitch, 0.2f, 0f, 0.002f, 0.05f).Noise(0.1f, 0f, 0.002f, 0.05f).LowPass(3000f));
            AudioClip distant = AudioSynth.LoadOrCreate(Path("Distant", 0), () => new AudioSynth(1.6f * power, seed + 30)
                .Noise(1f, 0f, 0.012f, 0.35f * power).LowPass(700f).LowPass(700f).Tone(70f * pitch, 45f * pitch, 0.5f, 0f, 0.02f, 0.4f * power, 0.3f)
                .Echo(0.4f, 0.25f));
            AudioClip indoor = AudioSynth.LoadOrCreate(Path("TailIndoor", 0), () => new AudioSynth(0.8f * power, seed + 40)
                .Noise(0.7f, 0f, 0.005f, 0.18f * power).LowPass(5000f).Echo(0.013f, 0.6f).Echo(0.029f, 0.45f).Echo(0.047f, 0.35f));
            AudioClip urban = AudioSynth.LoadOrCreate(Path("TailUrban", 0), () => new AudioSynth(2f * power, seed + 50)
                .Noise(0.6f, 0f, 0.01f, 0.25f).LowPass(2500f).Echo(0.18f, 0.55f).Echo(0.37f, 0.4f).Echo(0.62f, 0.3f)
                .Noise(0.15f, 0.05f, 0.1f, 0.8f * power).LowPass(4000f));
            AudioClip open = AudioSynth.LoadOrCreate(Path("TailOpen", 0), () => new AudioSynth(2.6f * power, seed + 60)
                .Noise(0.5f, 0f, 0.03f, 0.9f * power).LowPass(450f).LowPass(450f).Tone(55f * pitch, 40f * pitch, 0.25f, 0f, 0.05f, 1f * power, 0.8f));

            WeaponAudioProfileSO profile = TestWeaponBuilder.LoadOrCreate<WeaponAudioProfileSO>($"{AudioDataFolder}/{weapon}_Audio.asset");
            TestWeaponBuilder.Configure(profile, so =>
            {
                SetClips(TestWeaponBuilder.Property(so, "pressureTransients"), transients);
                SetClips(TestWeaponBuilder.Property(so, "mechanicalCycles"), mechanism);
                SetClips(TestWeaponBuilder.Property(so, "bodyResonances"), body);
                SetClips(TestWeaponBuilder.Property(so, "distantReports"), distant);
                TestWeaponBuilder.Set(so, "indoorTail", indoor);
                TestWeaponBuilder.Set(so, "urbanTail", urban);
                TestWeaponBuilder.Set(so, "openTail", open);
                TestWeaponBuilder.Set(so, "loudness", Mathf.Min(1f, 0.8f * power));
            });
        }

        /// <summary>Stun-grenade ringing: two high partials beating slowly, looped while hearing returns.</summary>
        internal static AudioClip Tinnitus()
        {
            ProjectSetupWizard.EnsureFolder($"{AudioFolder}/Foley");
            return AudioSynth.LoadOrCreate(Clip("Foley", "Tinnitus"), () => new AudioSynth(3f, 41)
                .Tone(3900f, 3900f, 0.35f, 0f, 0.05f, 30f).Tone(3913f, 3913f, 0.3f, 0f, 0.05f, 30f).Tone(6100f, 6100f, 0.08f, 0f, 0.05f, 30f));
        }

        /// <summary>
        /// A grenade report: <paramref name="power"/> scales the blast, <paramref name="crack"/> the sharp high-frequency
        /// snap (a stun grenade is mostly crack, a frag mostly thump). Close report, distant boom and three tails.
        /// </summary>
        internal static WeaponAudioProfileSO ExplosionAudio(string name, float power, float crack)
        {
            ProjectSetupWizard.EnsureFolder($"{AudioFolder}/Explosives");
            ProjectSetupWizard.EnsureFolder(AudioDataFolder);
            string Path(string layer, int variant) => Clip("Explosives", $"{name}_{layer}_{variant}");
            int seed = name.Length * 307;
            var blasts = new AudioClip[2];
            for (int i = 0; i < blasts.Length; i++)
            {
                int s = seed + i;
                blasts[i] = AudioSynth.LoadOrCreate(Path("Blast", i), () => new AudioSynth(1.2f * power, s)
                    .NWave(crack, 0f, 0.002f).Noise(1f, 0f, 0.001f, 0.12f * power).LowPass(6000f)
                    .Tone(70f, 32f, 1f, 0f, 0.004f, 0.5f * power, 0.3f).Noise(0.35f, 0.02f, 0.02f, 0.5f * power).LowPass(2500f));
            }

            AudioClip debris = AudioSynth.LoadOrCreate(Path("Debris", 0), () => new AudioSynth(1f, seed + 10)
                .Noise(0.25f, 0.15f, 0.05f, 0.4f).HighPass(1500f).Partials(new[] { 2400f, 4100f }, 0.15f, 0.3f, 0.05f));
            AudioClip distant = AudioSynth.LoadOrCreate(Path("Distant", 0), () => new AudioSynth(2.5f * power, seed + 30)
                .Noise(1f, 0f, 0.03f, 0.7f * power).LowPass(300f).LowPass(300f).Tone(45f, 28f, 0.8f, 0f, 0.05f, 1f * power, 0.6f).Echo(0.5f, 0.3f));
            AudioClip indoor = AudioSynth.LoadOrCreate(Path("TailIndoor", 0), () => new AudioSynth(1.4f * power, seed + 40)
                .Noise(0.8f, 0f, 0.005f, 0.4f * power).LowPass(3000f).Echo(0.017f, 0.6f).Echo(0.037f, 0.5f).Echo(0.061f, 0.4f));
            AudioClip urban = AudioSynth.LoadOrCreate(Path("TailUrban", 0), () => new AudioSynth(3f * power, seed + 50)
                .Noise(0.7f, 0f, 0.02f, 0.6f).LowPass(1500f).Echo(0.21f, 0.55f).Echo(0.43f, 0.4f).Echo(0.71f, 0.3f));
            AudioClip open = AudioSynth.LoadOrCreate(Path("TailOpen", 0), () => new AudioSynth(4f * power, seed + 60)
                .Noise(0.6f, 0f, 0.06f, 1.6f * power).LowPass(300f).LowPass(300f).Tone(38f, 30f, 0.4f, 0f, 0.1f, 2f * power, 1.5f));

            WeaponAudioProfileSO profile = TestWeaponBuilder.LoadOrCreate<WeaponAudioProfileSO>($"{AudioDataFolder}/{name}_Audio.asset");
            TestWeaponBuilder.Configure(profile, so =>
            {
                SetClips(TestWeaponBuilder.Property(so, "pressureTransients"), blasts);
                SetClips(TestWeaponBuilder.Property(so, "mechanicalCycles"), debris);
                SetClips(TestWeaponBuilder.Property(so, "bodyResonances"));
                SetClips(TestWeaponBuilder.Property(so, "distantReports"), distant);
                TestWeaponBuilder.Set(so, "distantThreshold", 150f);
                TestWeaponBuilder.Set(so, "maxAudibleDistance", 2500f);
                TestWeaponBuilder.Set(so, "indoorTail", indoor);
                TestWeaponBuilder.Set(so, "urbanTail", urban);
                TestWeaponBuilder.Set(so, "openTail", open);
                TestWeaponBuilder.Set(so, "loudness", 1f);
            });
            return profile;
        }

        internal static Material SmokeMaterial() => Vfx("BreechSmoke");

        /// <summary>The armed charge's short electronic beep.</summary>
        internal static AudioClip BombBeep()
        {
            ProjectSetupWizard.EnsureFolder($"{AudioFolder}/Explosives");
            return AudioSynth.LoadOrCreate(Clip("Explosives", "Charge_Beep"), () => new AudioSynth(0.09f, 51).Tone(2900f, 2900f, 0.6f, 0f, 0.002f, 0.05f));
        }

        /// <summary>
        /// The weapon behind a suppressor: a short, dull "thup" with the bolt clearly audible, quieter tails and a
        /// report that fades out far sooner. Shares the unsuppressed mechanism and body clips.
        /// </summary>
        internal static WeaponAudioProfileSO SuppressedAudio(string weapon)
        {
            WeaponAudioProfileSO open = WeaponAudio(weapon);
            string Path(string layer, int variant) => Clip("Weapons", $"{weapon}_Suppressed{layer}_{variant}");
            int seed = weapon.Length * 211;
            var transients = new AudioClip[3];
            for (int i = 0; i < transients.Length; i++)
            {
                int s = seed + i;
                transients[i] = AudioSynth.LoadOrCreate(Path("Transient", i), () => new AudioSynth(0.25f, s)
                    .Noise(0.6f, 0f, 0.001f, 0.025f).LowPass(1800f).Tone(120f, 70f, 0.7f, 0f, 0.002f, 0.05f, 0.03f)
                    .Noise(0.12f, 0.002f, 0.004f, 0.04f).HighPass(60f));
            }

            AudioClip distant = AudioSynth.LoadOrCreate(Path("Distant", 0), () => new AudioSynth(0.6f, seed + 30)
                .Noise(0.6f, 0f, 0.008f, 0.12f).LowPass(500f).LowPass(500f));
            AudioClip indoor = AudioSynth.LoadOrCreate(Path("TailIndoor", 0), () => new AudioSynth(0.4f, seed + 40)
                .Noise(0.35f, 0f, 0.005f, 0.08f).LowPass(2500f).Echo(0.013f, 0.5f).Echo(0.029f, 0.35f));
            AudioClip outdoor = AudioSynth.LoadOrCreate(Path("TailOutdoor", 0), () => new AudioSynth(0.8f, seed + 50)
                .Noise(0.25f, 0f, 0.01f, 0.2f).LowPass(900f).Echo(0.18f, 0.35f));

            WeaponAudioProfileSO profile = TestWeaponBuilder.LoadOrCreate<WeaponAudioProfileSO>($"{AudioDataFolder}/{weapon}_Suppressed_Audio.asset");
            TestWeaponBuilder.Configure(profile, so =>
            {
                SetClips(TestWeaponBuilder.Property(so, "pressureTransients"), transients);
                SetClips(TestWeaponBuilder.Property(so, "mechanicalCycles"), open.MechanicalCycle); // one clip each on the base profile
                SetClips(TestWeaponBuilder.Property(so, "bodyResonances"), open.BodyResonance);
                SetClips(TestWeaponBuilder.Property(so, "distantReports"), distant);
                TestWeaponBuilder.Set(so, "distantThreshold", 40f);
                TestWeaponBuilder.Set(so, "maxAudibleDistance", 300f);
                TestWeaponBuilder.Set(so, "indoorTail", indoor);
                TestWeaponBuilder.Set(so, "urbanTail", outdoor);
                TestWeaponBuilder.Set(so, "openTail", outdoor);
                TestWeaponBuilder.Set(so, "loudness", 0.35f);
            });
            return profile;
        }

        private static void Surface(string surfaceName, Color decalColor, Color dustColor, Texture2D hole, Func<AudioSynth, AudioSynth> impact)
        {
            var clips = new AudioClip[2];
            for (int i = 0; i < clips.Length; i++)
            {
                int seed = surfaceName.Length * 31 + i;
                clips[i] = AudioSynth.LoadOrCreate(Clip("Impacts", $"Impact_{surfaceName}_{i}"), () => impact(new AudioSynth(0.4f, seed)));
            }

            Material decal = hole != null ? Decal($"Decal_{surfaceName}", decalColor, hole) : null;
            SurfaceMaterialSO surface = BallisticsContentBuilder.Surface(surfaceName);
            TestWeaponBuilder.Configure(surface, so =>
            {
                SetClips(TestWeaponBuilder.Property(so, "impactClips"), clips);
                TestWeaponBuilder.Set(so, "impactDecal", decal);
                TestWeaponBuilder.Property(so, "impactDustColor").colorValue = dustColor;
            });
        }

        private static Material Unlit(string materialName, Color color, Texture texture, bool additive, bool distortion)
        {
            string path = $"{VfxFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("HDRP/Unlit")) { name = materialName };
            HDMaterial.SetSurfaceType(material, true);
            material.SetFloat("_BlendMode", additive ? 1f : 0f);
            material.SetColor("_UnlitColor", color);
            material.SetTexture("_UnlitColorMap", texture);
            if (distortion)
            {
                material.SetFloat("_DistortionEnable", 1f);
                material.SetFloat("_DistortionOnly", 1f);
                material.SetTexture("_DistortionVectorMap", texture);
                material.SetFloat("_DistortionScale", 0.35f);
                material.SetFloat("_DistortionVectorScale", 2f);
                material.SetFloat("_DistortionVectorBias", -1f);
                material.SetFloat("_DistortionBlurScale", 0.5f);
            }

            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material Decal(string materialName, Color color, Texture texture)
        {
            string path = $"{VfxFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("HDRP/Decal")) { name = materialName };
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_DecalBlend", 1f);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Texture2D Texture(string textureName, bool linear, Func<float, float, Color> pixel, int size)
        {
            // Regenerated every run so the asset always matches the generator.
            string path = $"{VfxFolder}/{textureName}.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool exists = texture != null;
            if (!exists) texture = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = textureName, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) pixels[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            if (exists) EditorUtility.SetDirty(texture);
            else AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        /// <summary>4×4 flipbook of a soft puff that swells, frays and fades (frames left-to-right, top-to-bottom).</summary>
        private static Color FlipbookPixel(float u, float v)
        {
            const int tiles = EffectParticles.FlipbookTiles;
            int column = Mathf.Min(tiles - 1, (int)(u * tiles));
            int row = Mathf.Min(tiles - 1, (int)((1f - v) * tiles));
            float progress = (row * tiles + column) / (float)(tiles * tiles - 1);
            float x = u * tiles - column - 0.5f, y = v * tiles - (tiles - 1 - row) - 0.5f;
            float distance = Mathf.Sqrt(x * x + y * y) / (0.3f + 0.15f * progress);
            float noise = 0.6f + 0.4f * Mathf.PerlinNoise(u * 18f + progress * 3f, v * 18f);
            float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.5f) * noise * Mathf.Pow(1f - progress, 1.3f);
            return new Color(1f, 1f, 1f, alpha);
        }

        /// <summary>Dark hole, cracked rim fading out; tinted per surface by the decal material colour.</summary>
        private static Color BulletHolePixel(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float distance = Mathf.Sqrt(x * x + y * y);
            if (distance < 0.07f) return new Color(0.02f, 0.02f, 0.02f, 1f);
            float rim = Mathf.Clamp01(1f - (distance - 0.07f) / 0.33f);
            float cracks = Mathf.PerlinNoise(Mathf.Atan2(y, x) * 4f + 10f, distance * 12f);
            return new Color(0.3f, 0.3f, 0.3f, rim * rim * (0.35f + 0.65f * cracks));
        }

        /// <summary>Smooth noise distortion vectors around zero (0.5 encoded) for heat haze.</summary>
        /// <summary>Noise offsets (RG, 0.5 = none) and blur (B) that fade out toward the rim, so no quad edge ever shows.</summary>
        private static Color DistortionPixel(float u, float v)
        {
            float rim = Mathf.Clamp01(1f - Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f);
            float fade = rim * rim * (3f - 2f * rim);
            return new Color(0.5f + (Mathf.PerlinNoise(u * 6f, v * 6f) - 0.5f) * fade,
                0.5f + (Mathf.PerlinNoise(u * 6f + 31.7f, v * 6f + 17.3f) - 0.5f) * fade, fade, 1f);
        }

        private static void SetClips(SerializedProperty list, params AudioClip[] clips)
        {
            list.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        private static AudioClip Load(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
