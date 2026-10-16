using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.EditorTools
{
    internal enum SurfaceFinish
    {
        AnodizedBlack,
        PolymerBlack,
        PolymerFde,
        CerakoteFde,
        ParkerizedSteel,
        BluedSteel,
        Rubber,
        Brass,
        GrenadeOliveDrab,
        StunBlack,
        SmokeCanister,
        C4Putty,
        CrateWood,
        LockerPaint,
        BenchSteel,
        BenchWood,
        ClothTan,
        ClothOlive,
        WebbingCoyote,
        WebbingRanger,
        HelmetTan,
        HelmetOlive,
        Skin,
        GloveLeather,
    }

    internal enum FinishPattern
    {
        Plain,
        Stipple,
        Brushed,
        WoodGrain,
        Weave,
        Camouflage,
        Pores,
    }

    /// <summary>
    /// Procedural PBR material library for the fallback models: each finish gets tileable 256² base colour, tangent
    /// normal and HDRP mask maps (R metallic, G occlusion, B detail mask, A smoothness) generated from periodic value
    /// noise, a surface pattern (stipple, brushing, wood grain, weave, camouflage, pores) and wear scratches that cut
    /// through coatings to bare metal. Generated once; delete Art/Models/Materials to regenerate.
    /// </summary>
    internal static class SurfaceFinishes
    {
        internal const string Folder = "Assets/_Project/Art/Models/Materials";
        private const int Size = 256;

        private struct Spec
        {
            public Color Color;
            public float ColorVariation;
            public float Metallic;
            public float Smoothness;
            public float SmoothnessVariation;
            public FinishPattern Pattern;
            public float PatternScale;
            public float NormalStrength;
            public int Scratches;
            /// <summary>Colour, metallic and smoothness exposed where scratches cut the coating.</summary>
            public Color ScratchColor;
            public float ScratchMetallic;
            public Color SecondColor;
            public Color ThirdColor;
        }

        private static readonly Dictionary<SurfaceFinish, Material> Cache = new Dictionary<SurfaceFinish, Material>();

        public static Material Get(SurfaceFinish finish)
        {
            if (Cache.TryGetValue(finish, out Material cached) && cached != null) return cached;

            string materialPath = $"{Folder}/{finish}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                ProjectSetupWizard.EnsureFolder(Folder);
                Generate(finish, SpecOf(finish), out Color32[] baseColor, out Color32[] normal, out Color32[] mask);
                Texture2D baseMap = WriteTexture($"{Folder}/{finish}_BaseColor.png", baseColor, true, false);
                Texture2D normalMap = WriteTexture($"{Folder}/{finish}_Normal.png", normal, false, true);
                Texture2D maskMap = WriteTexture($"{Folder}/{finish}_Mask.png", mask, false, false);

                material = new Material(Shader.Find("HDRP/Lit")) { name = finish.ToString() };
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseColorMap", baseMap);
                material.SetTexture("_NormalMap", normalMap);
                material.SetFloat("_NormalScale", 1f);
                material.SetTexture("_MaskMap", maskMap);
                material.SetFloat("_MetallicRemapMin", 0f);
                material.SetFloat("_MetallicRemapMax", 1f);
                material.SetFloat("_SmoothnessRemapMin", 0f);
                material.SetFloat("_SmoothnessRemapMax", 1f);
                material.SetFloat("_AORemapMin", 0f);
                material.SetFloat("_AORemapMax", 1f);
                HDMaterial.ValidateMaterial(material);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            Cache[finish] = material;
            return material;
        }

        /// <summary>Texture repeats per metre that suit a finish (cloth and wood read at a coarser scale than gun metal).</summary>
        public static float UvScaleFor(SurfaceFinish finish)
        {
            switch (finish)
            {
                case SurfaceFinish.CrateWood:
                case SurfaceFinish.BenchWood:
                case SurfaceFinish.LockerPaint:
                case SurfaceFinish.BenchSteel:
                    return 1.5f;
                case SurfaceFinish.ClothTan:
                case SurfaceFinish.ClothOlive:
                case SurfaceFinish.WebbingCoyote:
                case SurfaceFinish.WebbingRanger:
                case SurfaceFinish.HelmetTan:
                case SurfaceFinish.HelmetOlive:
                case SurfaceFinish.Skin:
                case SurfaceFinish.GloveLeather:
                    return 3f;
                default:
                    return 8f;
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void ClearCache() => Cache.Clear();

        private static Spec SpecOf(SurfaceFinish finish)
        {
            Color steel = new Color(0.56f, 0.56f, 0.55f);
            switch (finish)
            {
                case SurfaceFinish.AnodizedBlack:
                    return new Spec { Color = new Color(0.035f, 0.035f, 0.038f), ColorVariation = 0.15f, Metallic = 0.65f, Smoothness = 0.48f, SmoothnessVariation = 0.12f,
                        Pattern = FinishPattern.Brushed, PatternScale = 1f, NormalStrength = 0.8f, Scratches = 26, ScratchColor = new Color(0.62f, 0.62f, 0.64f), ScratchMetallic = 1f };
                case SurfaceFinish.PolymerBlack:
                    return new Spec { Color = new Color(0.03f, 0.03f, 0.03f), ColorVariation = 0.12f, Metallic = 0f, Smoothness = 0.32f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.Stipple, PatternScale = 1f, NormalStrength = 1.4f, Scratches = 8, ScratchColor = new Color(0.08f, 0.08f, 0.08f) };
                case SurfaceFinish.PolymerFde:
                    return new Spec { Color = new Color(0.47f, 0.39f, 0.28f), ColorVariation = 0.1f, Metallic = 0f, Smoothness = 0.3f, SmoothnessVariation = 0.08f,
                        Pattern = FinishPattern.Stipple, PatternScale = 1f, NormalStrength = 1.2f, Scratches = 8, ScratchColor = new Color(0.36f, 0.3f, 0.22f) };
                case SurfaceFinish.CerakoteFde:
                    return new Spec { Color = new Color(0.5f, 0.42f, 0.3f), ColorVariation = 0.08f, Metallic = 0.05f, Smoothness = 0.38f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.Plain, PatternScale = 1f, NormalStrength = 0.5f, Scratches = 30, ScratchColor = new Color(0.55f, 0.55f, 0.57f), ScratchMetallic = 1f };
                case SurfaceFinish.ParkerizedSteel:
                    return new Spec { Color = new Color(0.12f, 0.12f, 0.12f), ColorVariation = 0.2f, Metallic = 0.8f, Smoothness = 0.36f, SmoothnessVariation = 0.15f,
                        Pattern = FinishPattern.Pores, PatternScale = 1f, NormalStrength = 0.7f, Scratches = 18, ScratchColor = steel, ScratchMetallic = 1f };
                case SurfaceFinish.BluedSteel:
                    return new Spec { Color = new Color(0.06f, 0.065f, 0.08f), ColorVariation = 0.1f, Metallic = 0.95f, Smoothness = 0.62f, SmoothnessVariation = 0.12f,
                        Pattern = FinishPattern.Brushed, PatternScale = 1.4f, NormalStrength = 0.4f, Scratches = 14, ScratchColor = steel, ScratchMetallic = 1f };
                case SurfaceFinish.Rubber:
                    return new Spec { Color = new Color(0.025f, 0.025f, 0.025f), ColorVariation = 0.08f, Metallic = 0f, Smoothness = 0.18f, SmoothnessVariation = 0.06f,
                        Pattern = FinishPattern.Stipple, PatternScale = 0.6f, NormalStrength = 1f };
                case SurfaceFinish.Brass:
                    return new Spec { Color = new Color(0.78f, 0.6f, 0.3f), ColorVariation = 0.1f, Metallic = 1f, Smoothness = 0.7f, SmoothnessVariation = 0.15f,
                        Pattern = FinishPattern.Brushed, PatternScale = 1f, NormalStrength = 0.3f, Scratches = 6, ScratchColor = new Color(0.85f, 0.7f, 0.42f), ScratchMetallic = 1f };
                case SurfaceFinish.GrenadeOliveDrab:
                    return new Spec { Color = new Color(0.2f, 0.23f, 0.14f), ColorVariation = 0.12f, Metallic = 0.1f, Smoothness = 0.36f, SmoothnessVariation = 0.12f,
                        Pattern = FinishPattern.Plain, PatternScale = 1f, NormalStrength = 0.5f, Scratches = 22, ScratchColor = steel, ScratchMetallic = 1f };
                case SurfaceFinish.StunBlack:
                    return new Spec { Color = new Color(0.04f, 0.04f, 0.04f), ColorVariation = 0.1f, Metallic = 0.4f, Smoothness = 0.4f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.Plain, PatternScale = 1f, NormalStrength = 0.4f, Scratches = 20, ScratchColor = steel, ScratchMetallic = 1f };
                case SurfaceFinish.SmokeCanister:
                    return new Spec { Color = new Color(0.27f, 0.3f, 0.2f), ColorVariation = 0.1f, Metallic = 0.2f, Smoothness = 0.34f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.Plain, PatternScale = 1f, NormalStrength = 0.4f, Scratches = 16, ScratchColor = steel, ScratchMetallic = 1f };
                case SurfaceFinish.C4Putty:
                    return new Spec { Color = new Color(0.66f, 0.63f, 0.53f), ColorVariation = 0.08f, Metallic = 0f, Smoothness = 0.45f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.Pores, PatternScale = 0.7f, NormalStrength = 0.9f };
                case SurfaceFinish.CrateWood:
                    return new Spec { Color = new Color(0.42f, 0.34f, 0.2f), ColorVariation = 0.2f, Metallic = 0f, Smoothness = 0.22f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.WoodGrain, PatternScale = 1f, NormalStrength = 1.2f, Scratches = 10, ScratchColor = new Color(0.55f, 0.46f, 0.3f) };
                case SurfaceFinish.LockerPaint:
                    return new Spec { Color = new Color(0.28f, 0.31f, 0.27f), ColorVariation = 0.08f, Metallic = 0.3f, Smoothness = 0.42f, SmoothnessVariation = 0.12f,
                        Pattern = FinishPattern.Plain, PatternScale = 1f, NormalStrength = 0.4f, Scratches = 34, ScratchColor = steel, ScratchMetallic = 1f };
                case SurfaceFinish.BenchSteel:
                    return new Spec { Color = new Color(0.22f, 0.23f, 0.24f), ColorVariation = 0.1f, Metallic = 0.85f, Smoothness = 0.45f, SmoothnessVariation = 0.15f,
                        Pattern = FinishPattern.Brushed, PatternScale = 1f, NormalStrength = 0.5f, Scratches = 30, ScratchColor = steel, ScratchMetallic = 1f };
                case SurfaceFinish.BenchWood:
                    return new Spec { Color = new Color(0.36f, 0.25f, 0.15f), ColorVariation = 0.18f, Metallic = 0f, Smoothness = 0.4f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.WoodGrain, PatternScale = 1.6f, NormalStrength = 0.8f, Scratches = 24, ScratchColor = new Color(0.5f, 0.38f, 0.24f) };
                case SurfaceFinish.ClothTan:
                    return new Spec { Color = new Color(0.55f, 0.47f, 0.34f), ColorVariation = 0.08f, Metallic = 0f, Smoothness = 0.12f, SmoothnessVariation = 0.05f,
                        Pattern = FinishPattern.Camouflage, PatternScale = 1f, NormalStrength = 1f,
                        SecondColor = new Color(0.43f, 0.38f, 0.26f), ThirdColor = new Color(0.33f, 0.27f, 0.19f) };
                case SurfaceFinish.ClothOlive:
                    return new Spec { Color = new Color(0.27f, 0.29f, 0.2f), ColorVariation = 0.08f, Metallic = 0f, Smoothness = 0.12f, SmoothnessVariation = 0.05f,
                        Pattern = FinishPattern.Camouflage, PatternScale = 1f, NormalStrength = 1f,
                        SecondColor = new Color(0.21f, 0.24f, 0.15f), ThirdColor = new Color(0.15f, 0.15f, 0.11f) };
                case SurfaceFinish.WebbingCoyote:
                    return new Spec { Color = new Color(0.45f, 0.36f, 0.24f), ColorVariation = 0.07f, Metallic = 0f, Smoothness = 0.16f, SmoothnessVariation = 0.05f,
                        Pattern = FinishPattern.Weave, PatternScale = 1f, NormalStrength = 1.4f, Scratches = 4, ScratchColor = new Color(0.5f, 0.42f, 0.3f) };
                case SurfaceFinish.WebbingRanger:
                    return new Spec { Color = new Color(0.18f, 0.2f, 0.15f), ColorVariation = 0.07f, Metallic = 0f, Smoothness = 0.16f, SmoothnessVariation = 0.05f,
                        Pattern = FinishPattern.Weave, PatternScale = 1f, NormalStrength = 1.4f, Scratches = 4, ScratchColor = new Color(0.24f, 0.26f, 0.2f) };
                case SurfaceFinish.HelmetTan:
                    return new Spec { Color = new Color(0.5f, 0.43f, 0.31f), ColorVariation = 0.08f, Metallic = 0f, Smoothness = 0.26f, SmoothnessVariation = 0.08f,
                        Pattern = FinishPattern.Stipple, PatternScale = 0.8f, NormalStrength = 0.9f, Scratches = 16, ScratchColor = new Color(0.32f, 0.3f, 0.26f) };
                case SurfaceFinish.HelmetOlive:
                    return new Spec { Color = new Color(0.23f, 0.25f, 0.18f), ColorVariation = 0.08f, Metallic = 0f, Smoothness = 0.26f, SmoothnessVariation = 0.08f,
                        Pattern = FinishPattern.Stipple, PatternScale = 0.8f, NormalStrength = 0.9f, Scratches = 16, ScratchColor = new Color(0.3f, 0.3f, 0.26f) };
                case SurfaceFinish.Skin:
                    return new Spec { Color = new Color(0.6f, 0.45f, 0.36f), ColorVariation = 0.06f, Metallic = 0f, Smoothness = 0.38f, SmoothnessVariation = 0.08f,
                        Pattern = FinishPattern.Pores, PatternScale = 0.5f, NormalStrength = 0.5f };
                default:
                    return new Spec { Color = new Color(0.06f, 0.055f, 0.05f), ColorVariation = 0.1f, Metallic = 0f, Smoothness = 0.3f, SmoothnessVariation = 0.1f,
                        Pattern = FinishPattern.Pores, PatternScale = 1f, NormalStrength = 1f, Scratches = 10, ScratchColor = new Color(0.14f, 0.12f, 0.1f) };
            }
        }

        // ---- Generation ----

        private static void Generate(SurfaceFinish finish, Spec spec, out Color32[] baseColor, out Color32[] normal, out Color32[] mask)
        {
            int seed = (int)finish * 7919 + 17;
            var height = new float[Size * Size];
            var tone = new float[Size * Size];
            var camo = new int[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float broad = Fbm(u, v, 4, 4, seed);
                    float fine = Fbm(u, v, 64, 2, seed + 101);
                    float pattern = Pattern(spec.Pattern, u, v, spec.PatternScale, seed, out int camoBand);
                    height[y * Size + x] = broad * 0.35f + fine * 0.25f + pattern;
                    tone[y * Size + x] = broad;
                    camo[y * Size + x] = camoBand;
                }
            }

            var scratch = new float[Size * Size];
            var random = new System.Random(seed + 7);
            for (int i = 0; i < spec.Scratches; i++) DrawScratch(scratch, random);

            baseColor = new Color32[Size * Size];
            normal = new Color32[Size * Size];
            mask = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float s = scratch[i];
                    Color color = spec.Pattern == FinishPattern.Camouflage
                        ? camo[i] == 0 ? spec.Color : camo[i] == 1 ? spec.SecondColor : spec.ThirdColor
                        : spec.Color;
                    color *= 1f + (tone[i] - 0.5f) * 2f * spec.ColorVariation;
                    color = Color.Lerp(color, spec.ScratchColor, s);
                    color.a = 1f;
                    baseColor[i] = color;

                    float h = height[i] - s * 0.35f;
                    float dx = Sample(height, scratch, x + 1, y) - Sample(height, scratch, x - 1, y);
                    float dy = Sample(height, scratch, x, y + 1) - Sample(height, scratch, x, y - 1);
                    var n = new Vector3(-dx * spec.NormalStrength * 6f, -dy * spec.NormalStrength * 6f, 1f).normalized;
                    normal[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);

                    float metallic = Mathf.Lerp(spec.Metallic, spec.ScratchMetallic > 0f ? spec.ScratchMetallic : spec.Metallic, s);
                    float occlusion = Mathf.Clamp01(0.75f + h * 0.4f);
                    float smoothness = Mathf.Clamp01(spec.Smoothness + (tone[i] - 0.5f) * 2f * spec.SmoothnessVariation + s * 0.15f);
                    mask[i] = MaskMapPacker.Pack(metallic, occlusion, 1f, 1f - smoothness);
                }
            }
        }

        private static float Sample(float[] height, float[] scratch, int x, int y)
        {
            int i = ((y + Size) % Size) * Size + (x + Size) % Size;
            return height[i] - scratch[i] * 0.35f;
        }

        private static float Pattern(FinishPattern pattern, float u, float v, float scale, int seed, out int camoBand)
        {
            camoBand = 0;
            switch (pattern)
            {
                case FinishPattern.Stipple:
                {
                    float n = ValueNoise(u * 96f * scale, v * 96f * scale, Mathf.RoundToInt(96f * scale), seed + 3);
                    return Mathf.SmoothStep(0.45f, 0.75f, n) * 0.5f;
                }
                case FinishPattern.Brushed:
                    return Fbm(u, v * 0.05f + u * 0.002f, Mathf.RoundToInt(128f * scale), 2, seed + 5) * 0.15f +
                           ValueNoise(u * 4f, v * 256f, 4, seed + 9) * 0.12f;
                case FinishPattern.WoodGrain:
                {
                    float warp = Fbm(u, v, 3, 3, seed + 11) * 2.5f;
                    float rings = Mathf.Sin((v * 18f * scale + warp) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    return rings * 0.35f + ValueNoise(u * 2f, v * 128f, 2, seed + 13) * 0.2f;
                }
                case FinishPattern.Weave:
                {
                    float a = Mathf.Sin(u * Mathf.PI * 2f * 64f * scale), b = Mathf.Sin(v * Mathf.PI * 2f * 64f * scale);
                    return (Mathf.Abs(a) * 0.5f + Mathf.Abs(b) * 0.5f) * 0.3f;
                }
                case FinishPattern.Camouflage:
                {
                    float blob = Fbm(u, v, 3, 3, seed + 17);
                    float blob2 = Fbm(u, v, 5, 2, seed + 23);
                    camoBand = blob > 0.56f ? 1 : blob2 > 0.62f ? 2 : 0;
                    float a = Mathf.Sin(u * Mathf.PI * 2f * 80f), b = Mathf.Sin(v * Mathf.PI * 2f * 80f);
                    return (Mathf.Abs(a) + Mathf.Abs(b)) * 0.1f;
                }
                case FinishPattern.Pores:
                    return Mathf.SmoothStep(0.62f, 0.8f, ValueNoise(u * 128f * scale, v * 128f * scale, Mathf.RoundToInt(128f * scale), seed + 29)) * -0.25f;
                default:
                    return 0f;
            }
        }

        private static void DrawScratch(float[] scratch, System.Random random)
        {
            float x = (float)random.NextDouble() * Size, y = (float)random.NextDouble() * Size;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float length = 8f + (float)random.NextDouble() * 50f;
            float strength = 0.35f + (float)random.NextDouble() * 0.65f;
            for (float t = 0f; t < length; t += 0.5f)
            {
                angle += ((float)random.NextDouble() - 0.5f) * 0.08f;
                int px = ((int)(x + Mathf.Cos(angle) * t) % Size + Size) % Size;
                int py = ((int)(y + Mathf.Sin(angle) * t) % Size + Size) % Size;
                float taper = Mathf.Sin(t / length * Mathf.PI);
                int i = py * Size + px;
                scratch[i] = Mathf.Max(scratch[i], strength * taper);
            }
        }

        /// <summary>Fractal sum of periodic value noise; the base lattice repeats every <paramref name="period"/> cells.</summary>
        internal static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int p = period << o;
                sum += ValueNoise(u * p, v * p, p, seed + o * 31) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
            }

            return sum / total;
        }

        /// <summary>Smooth value noise on an integer lattice that wraps every <paramref name="period"/> cells (tileable).</summary>
        internal static float ValueNoise(float x, float y, int period, int seed)
        {
            period = Mathf.Max(1, period);
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y, int period, int seed)
        {
            x = (x % period + period) % period;
            y = (y % period + period) % period;
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // ---- Assets ----

        private static Texture2D WriteTexture(string assetPath, Color32[] pixels, bool srgb, bool normalMap)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, !srgb);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath);
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.filterMode = FilterMode.Trilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
