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
    /// Generates the audio-visual content: weapon reports (<see cref="GunshotAudioBuilder"/>), handling foley, bullet
    /// cracks and whips, gear rustle and per-surface impacts; flipbook, bullet-hole and distortion textures; dust, muzzle
    /// gas, smoke, heat-haze and per-surface decal materials. Wires them into surfaces and the player.
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
            Texture2D crater = Texture("BulletCraterNormal", true, CraterNormalPixel, 128);
            Texture2D dot = Texture("SoftDot", false, SoftDotPixel, 64);
            Unlit("Ember", Color.white, dot, true, false);
            Unlit("Spark", Color.white, dot, true, false);
            Material shock = Unlit("MuzzleShock", Color.white, Texture("ShockGradient", false, ShockPixel, 64), true, false);
            shock.SetFloat("_DoubleSidedEnable", 1f); // the cone reads from inside and out
            HDMaterial.ValidateMaterial(shock);
            EditorUtility.SetDirty(shock);
            ThermalDecal("ImpactThermal", Texture("ThermalRing", false, ThermalRingPixel, 128));
            VolumeNoise();
            ShockMesh();
            Texture2D haze = Texture("HeatDistortion", true, DistortionPixel, 128);
            Unlit("ImpactDust", Color.white, puff, false, false);
            Unlit("MuzzleGas", new Color(1f, 0.75f, 0.45f, 0.9f), puff, true, false);
            Unlit("BreechSmoke", new Color(0.55f, 0.55f, 0.55f, 0.45f), puff, false, false);
            Unlit("HeatShimmer", Color.white, haze, false, true);

            Surface("Concrete", new Color(0.17f, 0.16f, 0.15f), new Color(0.6f, 0.58f, 0.55f, 0.8f), hole, crater, false, true,
                s => s.Noise(1f, 0f, 0.0005f, 0.02f).HighPass(1200f).Noise(0.3f, 0.01f, 0.01f, 0.12f).LowPass(5000f));
            Surface("Brick", new Color(0.2f, 0.12f, 0.1f), new Color(0.55f, 0.35f, 0.28f, 0.8f), hole, crater, false, true,
                s => s.Noise(1f, 0f, 0.0005f, 0.025f).HighPass(800f).Noise(0.35f, 0.01f, 0.01f, 0.12f).LowPass(3500f));
            Surface("Wood", new Color(0.12f, 0.08f, 0.05f), new Color(0.45f, 0.35f, 0.22f, 0.8f), hole, crater, false, false,
                s => s.Tone(320f, 280f, 0.8f, 0f, 0.001f, 0.04f).Noise(0.5f, 0f, 0.0005f, 0.02f).LowPass(4000f));
            Surface("Drywall", new Color(0.26f, 0.25f, 0.24f), new Color(0.85f, 0.84f, 0.82f, 0.85f), hole, crater, false, true,
                s => s.Noise(0.8f, 0f, 0.001f, 0.03f).LowPass(1800f).Tone(180f, 160f, 0.4f, 0f, 0.001f, 0.03f));
            Surface("Metal", new Color(0.08f, 0.08f, 0.09f), new Color(1f, 0.78f, 0.45f, 0.9f), hole, crater, true, false,
                s => s.Partials(new[] { 1800f, 3300f, 5100f, 7400f }, 0.9f, 0f, 0.25f).Noise(0.4f, 0f, 0.0003f, 0.005f));
            Surface("Glass", new Color(0.55f, 0.58f, 0.62f), new Color(0.85f, 0.9f, 0.95f, 0.6f), hole, crater, false, false,
                s => s.Noise(0.6f, 0f, 0.0005f, 0.06f).HighPass(3000f).Partials(new[] { 3800f, 6100f, 8900f }, 0.6f, 0f, 0.08f));
            Surface("Dirt", new Color(0.1f, 0.08f, 0.06f), new Color(0.42f, 0.34f, 0.25f, 0.85f), hole, crater, false, true,
                s => s.Noise(0.8f, 0f, 0.002f, 0.03f).LowPass(600f).Tone(90f, 60f, 0.5f, 0f, 0.002f, 0.05f));
            Surface("Flesh", Color.clear, new Color(0.35f, 0.04f, 0.03f, 0.7f), null, null, false, false,
                s => s.Noise(0.4f, 0f, 0.002f, 0.03f).LowPass(900f).Tone(140f, 90f, 0.8f, 0f, 0.002f, 0.05f));

            GunshotAudioBuilder.Weapon(GunshotAudioBuilder.Carbine);
            GunshotAudioBuilder.Weapon(GunshotAudioBuilder.Marksman);
            GunshotAudioBuilder.Weapon(GunshotAudioBuilder.BattleRifle);
            GunshotAudioBuilder.Weapon(GunshotAudioBuilder.Sidearm);

            AudioSynth.LoadOrCreate(Clip("Foley", "DryFire"), () => new AudioSynth(0.08f, 11).Partials(new[] { 3200f, 5600f }, 0.7f, 0f, 0.01f).Noise(0.2f, 0f, 0.0003f, 0.004f));
            AudioSynth.LoadOrCreate(Clip("Foley", "MagazineOut"), () => new AudioSynth(0.25f, 12).Noise(0.25f, 0f, 0.02f, 0.08f).HighPass(1500f).Partials(new[] { 1800f, 3400f }, 0.5f, 0.09f, 0.02f));
            AudioSynth.LoadOrCreate(Clip("Foley", "MagazineIn"), () => new AudioSynth(0.2f, 13).Tone(220f, 200f, 0.4f, 0f, 0.001f, 0.04f).Partials(new[] { 1500f, 2900f, 4700f }, 0.7f, 0f, 0.02f).Partials(new[] { 2100f, 3900f }, 0.5f, 0.06f, 0.015f));
            AudioSynth.LoadOrCreate(Clip("Foley", "BoltCycle"), () => new AudioSynth(0.3f, 14).Partials(new[] { 2600f, 4400f }, 0.6f, 0f, 0.015f).Noise(0.2f, 0.02f, 0.01f, 0.05f).Partials(new[] { 2000f, 3800f }, 0.7f, 0.16f, 0.02f));
            AudioSynth.LoadOrCreate(Clip("Foley", "FireModeSwitch"), () => new AudioSynth(0.06f, 15).Partials(new[] { 4200f, 6900f }, 0.5f, 0f, 0.006f));
            AudioSynth.LoadOrCreate(Clip("Foley", "MagazineCheck"), () => new AudioSynth(0.25f, 16).Partials(new[] { 1700f, 3300f }, 0.3f, 0f, 0.02f).Partials(new[] { 1600f, 3100f }, 0.3f, 0.12f, 0.02f));
            GunshotAudioBuilder.Cracks();
            GunshotAudioBuilder.Whips();
            GunshotAudioBuilder.GearRustle();
            GunshotAudioBuilder.SlingClinks();

            AssetDatabase.SaveAssets();
        }

        /// <summary>Assigns foley, cracks, gear sounds and VFX materials to a built player.</summary>
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
                TestWeaponBuilder.Set(so, "tinnitus", Tinnitus());
            });
            TestWeaponBuilder.Configure(player.GetComponent<SupersonicCrackEmitter>(), so =>
            {
                SetClips(TestWeaponBuilder.Property(so, "crackClips"), GunshotAudioBuilder.Cracks());
                SetClips(TestWeaponBuilder.Property(so, "whipClips"), GunshotAudioBuilder.Whips());
            });
            TestWeaponBuilder.Configure(player.GetComponent<GearFoley>(), so =>
            {
                TestWeaponBuilder.Set(so, "rustleLoop", GunshotAudioBuilder.GearRustle());
                SetClips(TestWeaponBuilder.Property(so, "swivelClinks"), GunshotAudioBuilder.SlingClinks());
            });
            TestWeaponBuilder.Configure(player.GetComponent<ImpactEffectsController>(), so =>
            {
                TestWeaponBuilder.Set(so, "dustMaterial", Vfx("ImpactDust"));
                TestWeaponBuilder.Set(so, "sparkMaterial", Vfx("Spark"));
                TestWeaponBuilder.Set(so, "thermalDecalMaterial", Vfx("ImpactThermal"));
                TestWeaponBuilder.Set(so, "dustVolumeMask", AssetDatabase.LoadAssetAtPath<Texture3D>(VolumeNoisePath));
            });
            TestWeaponBuilder.Configure(player.GetComponent<MuzzleAndSmokeVFXController>(), so =>
            {
                TestWeaponBuilder.Set(so, "gasMaterial", Vfx("MuzzleGas"));
                TestWeaponBuilder.Set(so, "smokeMaterial", Vfx("BreechSmoke"));
                TestWeaponBuilder.Set(so, "shimmerMaterial", Vfx("HeatShimmer"));
                TestWeaponBuilder.Set(so, "shockMesh", AssetDatabase.LoadAssetAtPath<Mesh>(ShockMeshPath));
                TestWeaponBuilder.Set(so, "shockMaterial", Vfx("MuzzleShock"));
                TestWeaponBuilder.Set(so, "emberMaterial", Vfx("Ember"));
                TestWeaponBuilder.Set(so, "smokeVolumeMask", AssetDatabase.LoadAssetAtPath<Texture3D>(VolumeNoisePath));
            });
        }

        /// <summary>Stun-grenade ringing: two high partials beating slowly, looped while hearing returns.</summary>
        internal static AudioClip Tinnitus()
        {
            ProjectSetupWizard.EnsureFolder($"{AudioFolder}/Foley");
            return AudioSynth.LoadOrCreate(Clip("Foley", "Tinnitus"), () => new AudioSynth(3f, 41)
                .Tone(3900f, 3900f, 0.35f, 0f, 0.05f, 30f).Tone(3913f, 3913f, 0.3f, 0f, 0.05f, 30f).Tone(6100f, 6100f, 0.08f, 0f, 0.05f, 30f));
        }

        internal static Material SmokeMaterial() => Vfx("BreechSmoke");

        /// <summary>The armed charge's short electronic beep.</summary>
        internal static AudioClip BombBeep()
        {
            ProjectSetupWizard.EnsureFolder($"{AudioFolder}/Explosives");
            return AudioSynth.LoadOrCreate(Clip("Explosives", "Charge_Beep"), () => new AudioSynth(0.09f, 51).Tone(2900f, 2900f, 0.6f, 0f, 0.002f, 0.05f));
        }

        private static void Surface(string surfaceName, Color decalColor, Color dustColor, Texture2D hole, Texture2D crater, bool sparks, bool lingeringDust,
            Func<AudioSynth, AudioSynth> impact)
        {
            var clips = new AudioClip[2];
            for (int i = 0; i < clips.Length; i++)
            {
                int seed = surfaceName.Length * 31 + i;
                clips[i] = AudioSynth.LoadOrCreate(Clip("Impacts", $"Impact_{surfaceName}_{i}"), () => impact(new AudioSynth(0.4f, seed)));
            }

            Material decal = hole != null ? Decal($"Decal_{surfaceName}", decalColor, hole, crater) : null;
            SurfaceMaterialSO surface = BallisticsContentBuilder.Surface(surfaceName);
            TestWeaponBuilder.Configure(surface, so =>
            {
                SetClips(TestWeaponBuilder.Property(so, "impactClips"), clips);
                TestWeaponBuilder.Set(so, "impactDecal", decal);
                TestWeaponBuilder.Property(so, "impactDustColor").colorValue = dustColor;
                TestWeaponBuilder.Set(so, "sparks", sparks);
                TestWeaponBuilder.Set(so, "lingeringDust", lingeringDust);
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

        /// <summary>Bullet-hole decal: dark hole and spall in the base map, a crater normal (depth and raised lip) blended by its alpha.</summary>
        private static Material Decal(string materialName, Color color, Texture texture, Texture normal)
        {
            string path = $"{VfxFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Decal")) { name = materialName };
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_DecalBlend", 1f);
            material.SetTexture("_NormalMap", normal);
            material.SetFloat("_AffectNormal", normal != null ? 1f : 0f);
            material.SetFloat("_NormalBlendSrc", 1f); // base colour alpha
            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Emissive-only decal: the hot ring a round leaves in steel (faded by FadingDecal).</summary>
        private static Material ThermalDecal(string materialName, Texture ring)
        {
            string path = $"{VfxFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Decal")) { name = materialName };
            material.SetFloat("_AffectAlbedo", 0f);
            material.SetFloat("_AffectNormal", 0f);
            material.SetFloat("_AffectEmission", 1f);
            material.SetTexture("_EmissiveColorMap", ring);
            Color ldr = new Color(1f, 0.55f, 0.2f);
            const float nits = 2500f;
            material.SetColor("_EmissiveColorLDR", ldr);
            material.SetFloat("_UseEmissiveIntensity", 1f);
            material.SetFloat("_EmissiveIntensity", nits);
            material.SetColor("_EmissiveColor", ldr.linear * nits);
            material.SetFloat("_EmissiveExposureWeight", 0.6f); // glows in daylight, not only in the dark
            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        internal const string VolumeNoisePath = VfxFolder + "/VolumeNoise.asset";
        internal const string ShockMeshPath = MeshStore.Folder + "/VFX/VFX_MuzzleShock.asset";

        /// <summary>Tileable 32³ noise for volumetric smoke and dust (RGB white, A density pockets).</summary>
        private static Texture3D VolumeNoise()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(VolumeNoisePath);
            if (existing != null) return existing;

            const int size = 32;
            var texture = new Texture3D(size, size, size, TextureFormat.RGBA32, false) { name = "VolumeNoise", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[size * size * size];
            for (int z = 0; z < size; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float n = 0f, amplitude = 0.5f, total = 0f;
                        for (int octave = 0; octave < 3; octave++)
                        {
                            int period = 4 << octave;
                            n += Noise3(x * period / (float)size, y * period / (float)size, z * period / (float)size, period, 97 + octave) * amplitude;
                            total += amplitude;
                            amplitude *= 0.5f;
                        }

                        byte density = (byte)Mathf.RoundToInt(SurfaceFinishes.Smoothstep(0.3f, 0.75f, n / total) * 255f);
                        pixels[(z * size + y) * size + x] = new Color32(255, 255, 255, density);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            AssetDatabase.CreateAsset(texture, VolumeNoisePath);
            return texture;
        }

        private static float Noise3(float x, float y, float z, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
            float fx = Smooth(x - x0), fy = Smooth(y - y0), fz = Smooth(z - z0);
            float Lattice(int i, int j, int k)
            {
                unchecked
                {
                    uint h = (uint)(((i % period + period) % period) * 73856093 ^ ((j % period + period) % period) * 19349663 ^
                                    ((k % period + period) % period) * 83492791 ^ seed * 2654435761);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
                }
            }

            float a = Mathf.Lerp(Lattice(x0, y0, z0), Lattice(x0 + 1, y0, z0), fx);
            float b = Mathf.Lerp(Lattice(x0, y0 + 1, z0), Lattice(x0 + 1, y0 + 1, z0), fx);
            float c = Mathf.Lerp(Lattice(x0, y0, z0 + 1), Lattice(x0 + 1, y0, z0 + 1), fx);
            float d = Mathf.Lerp(Lattice(x0, y0 + 1, z0 + 1), Lattice(x0 + 1, y0 + 1, z0 + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>The muzzle shock: a 1 m bottle-shaped blast envelope along +Z (UV v runs base to tip).</summary>
        private static Mesh ShockMesh()
        {
            var kit = new MeshKit { UvScale = 1f };
            kit.Lathe(new[]
            {
                new Vector2(0.06f, 0f), new Vector2(0.3f, 0.12f), new Vector2(0.48f, 0.35f), new Vector2(0.5f, 0.55f),
                new Vector2(0.36f, 0.8f), new Vector2(0.12f, 0.96f), new Vector2(0f, 1f),
            }, 20, true);
            return MeshStore.Save("VFX", kit.ToMesh("VFX_MuzzleShock"));
        }

        private static Color CraterNormalPixel(float u, float v)
        {
            // Height: a deep hole, a raised spall lip, then the undisturbed surface.
            float Height(float x, float y)
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return r < 0.07f ? -1f : -0.6f * Mathf.Exp(-(r - 0.07f) * 14f) + 0.35f * Mathf.Exp(-Mathf.Pow((r - 0.13f) * 22f, 2f));
            }

            float px = u - 0.5f, py = v - 0.5f, e = 0.004f;
            float dx = (Height(px + e, py) - Height(px - e, py)) / (2f * e);
            float dy = (Height(px, py + e) - Height(px, py - e)) / (2f * e);
            Vector3 n = new Vector3(-dx * 0.04f, -dy * 0.04f, 1f).normalized;
            return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f); // alpha 1: HDRP unpacks x by alpha
        }

        private static Color SoftDotPixel(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float a = Mathf.Exp(-(x * x + y * y) * 40f);
            return new Color(1f, 1f, 1f, a);
        }

        private static Color ShockPixel(float u, float v)
        {
            float streaks = Mathf.PerlinNoise(u * 24f, v * 3f);
            float a = Mathf.Pow(1f - v, 1.5f) * (0.55f + 0.45f * streaks);
            return new Color(a, a * 0.85f, a * 0.65f, a);
        }

        private static Color ThermalRingPixel(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float r = Mathf.Sqrt(x * x + y * y);
            float heat = r < 0.06f ? 0f : Mathf.Exp(-(r - 0.06f) * 16f) * Mathf.Clamp01((r - 0.06f) * 60f);
            Color hot = Color.Lerp(new Color(1f, 0.35f, 0.08f), new Color(1f, 0.92f, 0.7f), heat);
            return new Color(hot.r * heat, hot.g * heat, hot.b * heat, heat);
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

        internal static void SetClips(SerializedProperty list, params AudioClip[] clips)
        {
            list.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        private static AudioClip Load(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
