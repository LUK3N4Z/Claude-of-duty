using System.Collections.Generic;
using System.IO;
using FPS.Ballistics;
using FPS.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.EditorTools
{
    internal enum EnvironmentSurface
    {
        Concrete,
        /// <summary>Concrete ground: the concrete layers plus wet patches.</summary>
        ConcreteFloor,
        Asphalt,
        Brick,
        Plaster,
        WoodPlanks,
        PaintedSteel,
        Glass,
        Dirt,
    }

    /// <summary>
    /// Weathered world materials for the level builders: HDRP/LayeredLit with every layer triplanar-mapped in world
    /// space (blockout cubes of any scale keep a constant texel density and no stretching). Layer 0 is the surface
    /// (generated base colour, normal, mask and height), layer 1 grime that settles into low spots by height-based
    /// blending, and on ground (concrete floors, asphalt, soil) a layer 2 of wet patches (darkened, glossy, puddling in
    /// cavities); walls and ceilings stay dry. A world-space noise mask places the weathering; a shared micro-normal detail map adds fine grit.
    /// Generated once into Art/Environment; delete the folder to regenerate.
    /// </summary>
    internal static class EnvironmentMaterials
    {
        internal const string Folder = "Assets/_Project/Art/Environment";
        private const int Size = 256;
        private const int Triplanar = 5;

        private struct Layer
        {
            public Texture2D BaseColor, Normal, Mask, Height;
        }

        private static readonly Dictionary<EnvironmentSurface, Material> Cache = new Dictionary<EnvironmentSurface, Material>();

        public static Material Get(EnvironmentSurface surface)
        {
            if (Cache.TryGetValue(surface, out Material cached) && cached != null) return cached;
            string path = $"{Folder}/{surface}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                ProjectSetupWizard.EnsureFolder(Folder);
                material = surface == EnvironmentSurface.Glass ? GlassMaterial() : LayeredMaterial(surface);
                AssetDatabase.CreateAsset(material, path);
            }

            Cache[surface] = material;
            return material;
        }

        /// <summary>The weathered material for a ballistic surface name (Concrete, Brick, Wood, Drywall, Metal, Glass, Dirt).</summary>
        public static EnvironmentSurface ForBallisticSurface(string displayName)
        {
            switch (displayName)
            {
                case "Brick": return EnvironmentSurface.Brick;
                case "Wood": return EnvironmentSurface.WoodPlanks;
                case "Drywall": return EnvironmentSurface.Plaster;
                case "Metal": return EnvironmentSurface.PaintedSteel;
                case "Glass": return EnvironmentSurface.Glass;
                case "Dirt": return EnvironmentSurface.Dirt;
                default: return EnvironmentSurface.Concrete;
            }
        }

        /// <summary>
        /// Gives every blockout primitive under <paramref name="level"/> (solid world geometry, not props, targets of
        /// hitboxes or markers) the weathered material of its ballistic surface; unmarked ground uses
        /// <paramref name="ground"/>, everything else concrete. Returns the number of renderers dressed.
        /// </summary>
        public static int Dress(Transform level, EnvironmentSurface ground)
        {
            int dressed = 0;
            foreach (MeshRenderer renderer in level.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObject target = renderer.gameObject;
                if (target.layer != GameLayers.WorldGeometry) continue;
                if (!target.TryGetComponent(out Collider collider) || collider.isTrigger) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(target) || target.GetComponentInParent<TacticalHealthAndArmorSystem>() != null) continue;
                if (!target.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;

                EnvironmentSurface surface;
                if (target.TryGetComponent(out SurfaceIdentifier identifier) && identifier.Surface != null)
                    surface = ForBallisticSurface(identifier.Surface.DisplayName);
                else
                    surface = IsGround(target) ? ground : EnvironmentSurface.Concrete;
                if (surface == EnvironmentSurface.Concrete && IsGround(target))
                    surface = ground == EnvironmentSurface.Concrete ? EnvironmentSurface.ConcreteFloor : ground;

                renderer.sharedMaterial = Get(surface);
                dressed++;
            }

            return dressed;
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in text) hash = hash * 31 + c;
                return hash & 0xFFFF;
            }
        }

        private static bool IsGround(GameObject target)
        {
            string name = target.name.ToLowerInvariant();
            return name.Contains("floor") || name.Contains("ground");
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void ClearCache() => Cache.Clear();

        // ---- Materials ----

        private static Material LayeredMaterial(EnvironmentSurface surface)
        {
            bool wet = surface == EnvironmentSurface.ConcreteFloor || surface == EnvironmentSurface.Asphalt || surface == EnvironmentSurface.Dirt;
            EnvironmentSurface look = surface == EnvironmentSurface.ConcreteFloor ? EnvironmentSurface.Concrete : surface;
            float tiling = TilesPerMetre(surface);
            var material = new Material(Shader.Find("HDRP/LayeredLit")) { name = surface.ToString() };
            material.SetFloat("_LayerCount", wet ? 3f : 2f);
            material.SetTexture("_LayerMaskMap", BlendMask());
            material.SetFloat("_UVBlendMask", Triplanar);
            material.SetFloat("_TexWorldScaleBlendMask", 0.06f); // weathering patches every ~16 m
            material.SetFloat("_UseHeightBasedBlend", 1f);
            material.SetFloat("_HeightTransition", 0.12f);
            material.SetFloat("_UseMainLayerInfluence", 1f); // upper layers inherit the surface's colour and relief

            SetLayer(material, 0, Generate(look.ToString(), look), tiling, Color.white);
            SetLayer(material, 1, GrimeLayer(), tiling * 0.5f, Color.white);
            material.SetFloat("_InheritBaseNormal1", 0.6f); // grime sits in the surface's relief
            if (wet)
            {
                SetLayer(material, 2, WetLayer(), tiling * 0.25f, new Color(0.2f, 0.2f, 0.2f));
                material.SetFloat("_InheritBaseColor2", 0.55f); // wet surface: about half the base colour's albedo
                material.SetFloat("_InheritBaseNormal2", 0.25f); // water fills the relief
            }

            HDMaterial.ValidateMaterial(material);
            return material;
        }

        private static void SetLayer(Material material, int index, Layer layer, float tiling, Color tint)
        {
            material.SetTexture($"_BaseColorMap{index}", layer.BaseColor);
            material.SetTexture($"_NormalMap{index}", layer.Normal);
            material.SetTexture($"_MaskMap{index}", layer.Mask);
            material.SetTexture($"_HeightMap{index}", layer.Height);
            material.SetColor($"_BaseColor{index}", tint);
            material.SetFloat($"_NormalScale{index}", 1f);
            material.SetFloat($"_UVBase{index}", Triplanar);
            material.SetFloat($"_TexWorldScale{index}", tiling);
            material.SetFloat($"_InvTilingScale{index}", 1f / tiling);
            material.SetFloat($"_HeightCenter{index}", 0.5f);
            material.SetFloat($"_HeightAmplitude{index}", 1f);
            material.SetFloat($"_SmoothnessRemapMin{index}", 0f);
            material.SetFloat($"_SmoothnessRemapMax{index}", 1f);
            material.SetFloat($"_MetallicRemapMin{index}", 0f);
            material.SetFloat($"_MetallicRemapMax{index}", 1f);
            material.SetFloat($"_AORemapMin{index}", 0f);
            material.SetFloat($"_AORemapMax{index}", 1f);
            material.SetTexture($"_DetailMap{index}", SurfaceFinishes.MicroDetail());
            material.SetFloat($"_DetailNormalScale{index}", 0.6f);
            material.SetFloat($"_DetailAlbedoScale{index}", 0f);
            material.SetFloat($"_DetailSmoothnessScale{index}", 0.3f);
        }

        private static Material GlassMaterial()
        {
            var material = new Material(Shader.Find("HDRP/Lit")) { name = "Glass" };
            material.SetColor("_BaseColor", new Color(0.32f, 0.36f, 0.38f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.95f);
            material.SetTexture("_DetailMap", SurfaceFinishes.MicroDetail()); // dust and smears on the pane
            material.SetFloat("_DetailNormalScale", 0.15f);
            material.SetFloat("_DetailSmoothnessScale", 0.5f);
            HDMaterial.ValidateMaterial(material);
            return material;
        }

        private static float TilesPerMetre(EnvironmentSurface surface)
        {
            switch (surface)
            {
                case EnvironmentSurface.Brick: return 1f; // the brick pattern texture spans 1 m
                case EnvironmentSurface.WoodPlanks: return 1f;
                case EnvironmentSurface.Asphalt: return 1.5f;
                default: return 0.75f;
            }
        }

        // ---- Texture generation ----

        private static Layer GrimeLayer() => Generate("Grime", null);

        private static Layer WetLayer() => Generate("Wet", null);

        /// <summary>Base colour, normal, mask (R metal, G AO, B detail, A smoothness) and height for a layer.</summary>
        private static Layer Generate(string name, EnvironmentSurface? surface)
        {
            string prefix = $"{Folder}/{name}";
            var existing = new Layer
            {
                BaseColor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_BaseColor.png"),
                Normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_Normal.png"),
                Mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_Mask.png"),
                Height = AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_Height.png"),
            };
            if (existing.BaseColor != null && existing.Normal != null && existing.Mask != null && existing.Height != null) return existing;

            int seed = StableHash(name);
            var height = new float[Size * Size];
            var color = new Color[Size * Size];
            var metal = new float[Size * Size];
            var smooth = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    Sample(name, surface, u, v, seed, out color[i], out height[i], out metal[i], out smooth[i]);
                }
            }

            var basePixels = new Color32[Size * Size];
            var normalPixels = new Color32[Size * Size];
            var maskPixels = new Color32[Size * Size];
            var heightPixels = new Color32[Size * Size];
            float strength = name == "Wet" ? 0.5f : 5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float dx = height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size];
                    float dy = height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x];
                    Vector3 n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    Color c = color[i];
                    c.a = 1f;
                    basePixels[i] = c;
                    normalPixels[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    float occlusion = Mathf.Clamp01(0.55f + height[i] * 0.6f);
                    maskPixels[i] = MaskMapPacker.Pack(metal[i], occlusion, 1f, 1f - smooth[i]);
                    byte h = (byte)Mathf.RoundToInt(Mathf.Clamp01(height[i]) * 255f);
                    heightPixels[i] = new Color32(h, h, h, 255);
                }
            }

            return new Layer
            {
                BaseColor = Write($"{prefix}_BaseColor.png", basePixels, true, false),
                Normal = Write($"{prefix}_Normal.png", normalPixels, false, true),
                Mask = Write($"{prefix}_Mask.png", maskPixels, false, false),
                Height = Write($"{prefix}_Height.png", heightPixels, false, false),
            };
        }

        private static void Sample(string name, EnvironmentSurface? surface, float u, float v, int seed, out Color color, out float height, out float metal, out float smooth)
        {
            float broad = SurfaceFinishes.Fbm(u, v, 4, 4, seed);
            float fine = SurfaceFinishes.Fbm(u, v, 64, 2, seed + 7);
            metal = 0f;
            if (surface == null)
            {
                if (name == "Wet")
                {
                    color = Color.white; // tinted and inherited from the base layer
                    height = 0.35f + broad * 0.3f; // pools where the base is low
                    smooth = 0.84f;
                }
                else
                {
                    color = new Color(0.27f, 0.23f, 0.18f) * (0.8f + broad * 0.4f); // soot, mud and oil
                    height = 0.5f + (broad - 0.5f) * 0.6f + (fine - 0.5f) * 0.2f;
                    smooth = 0.12f + fine * 0.1f;
                }

                return;
            }

            switch (surface.Value)
            {
                case EnvironmentSurface.Asphalt:
                {
                    float aggregate = SurfaceFinishes.ValueNoise(u * 96f, v * 96f, 96, seed + 11);
                    bool stone = aggregate > 0.72f;
                    color = stone ? new Color(0.33f, 0.32f, 0.3f) * (0.8f + fine * 0.4f) : new Color(0.085f, 0.085f, 0.09f) * (0.85f + broad * 0.3f);
                    height = 0.45f + (stone ? 0.25f : 0f) + (fine - 0.5f) * 0.2f;
                    smooth = stone ? 0.3f : 0.18f;
                    break;
                }
                case EnvironmentSurface.Brick:
                {
                    // Running bond: 4 bricks across, 8 courses up in a 1 m tile; mortar recessed.
                    float course = v * 8f;
                    int row = Mathf.FloorToInt(course);
                    float along = u * 4f + (row % 2 == 0 ? 0f : 0.5f);
                    int column = Mathf.FloorToInt(along);
                    float fu = along - column, fv = course - row;
                    bool mortar = fu < 0.04f || fv < 0.12f;
                    float shade = SurfaceFinishes.ValueNoise(column * 7.3f + row * 3.1f, row * 1.7f, 64, seed + 3);
                    color = mortar ? new Color(0.52f, 0.5f, 0.46f) * (0.85f + fine * 0.3f)
                        : Color.Lerp(new Color(0.42f, 0.17f, 0.12f), new Color(0.56f, 0.3f, 0.21f), shade) * (0.85f + fine * 0.3f);
                    height = mortar ? 0.2f + fine * 0.1f : 0.6f + (fine - 0.5f) * 0.25f + (broad - 0.5f) * 0.1f;
                    smooth = mortar ? 0.12f : 0.2f;
                    break;
                }
                case EnvironmentSurface.Plaster:
                    color = new Color(0.74f, 0.73f, 0.7f) * (0.92f + broad * 0.12f);
                    height = 0.5f + (SurfaceFinishes.ValueNoise(u * 48f, v * 48f, 48, seed + 5) - 0.5f) * 0.25f;
                    smooth = 0.32f;
                    break;
                case EnvironmentSurface.WoodPlanks:
                {
                    float plank = u * 6f;
                    float fu = plank - Mathf.Floor(plank);
                    bool gap = fu < 0.03f;
                    float grain = Mathf.Sin((v * 30f + SurfaceFinishes.Fbm(u, v, 3, 3, seed + 9) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float tone = SurfaceFinishes.ValueNoise(Mathf.Floor(plank) * 5.1f, 0.5f, 32, seed + 13);
                    color = gap ? new Color(0.06f, 0.05f, 0.04f) : Color.Lerp(new Color(0.32f, 0.22f, 0.13f), new Color(0.48f, 0.35f, 0.21f), tone) * (0.85f + grain * 0.25f);
                    height = gap ? 0.1f : 0.55f + grain * 0.1f;
                    smooth = gap ? 0.05f : 0.28f;
                    break;
                }
                case EnvironmentSurface.PaintedSteel:
                {
                    float chips = SurfaceFinishes.Fbm(u, v, 16, 3, seed + 17);
                    bool bare = chips > 0.66f;
                    float rust = SurfaceFinishes.Smoothstep(0.6f, 0.8f, SurfaceFinishes.Fbm(u * 0.5f, v * 2f, 4, 3, seed + 19));
                    color = bare ? Color.Lerp(new Color(0.5f, 0.5f, 0.49f), new Color(0.38f, 0.2f, 0.1f), rust)
                        : Color.Lerp(new Color(0.3f, 0.33f, 0.29f), new Color(0.35f, 0.22f, 0.14f), rust * 0.6f) * (0.9f + fine * 0.2f);
                    metal = bare ? 1f - rust : 0.1f;
                    height = bare ? 0.38f : 0.55f;
                    smooth = bare ? 0.45f - rust * 0.3f : 0.38f;
                    break;
                }
                case EnvironmentSurface.Dirt:
                {
                    float pebble = SurfaceFinishes.ValueNoise(u * 64f, v * 64f, 64, seed + 23);
                    bool stone = pebble > 0.74f;
                    color = stone ? new Color(0.45f, 0.42f, 0.38f) : new Color(0.33f, 0.27f, 0.19f) * (0.8f + broad * 0.4f);
                    height = 0.4f + (stone ? 0.3f : 0f) + (broad - 0.5f) * 0.3f;
                    smooth = 0.08f;
                    break;
                }
                default: // concrete: pores, formwork seams, staining
                {
                    float pores = SurfaceFinishes.ValueNoise(u * 128f, v * 128f, 128, seed + 29);
                    bool pore = pores > 0.8f;
                    float seam = Mathf.Abs(v - 0.5f) < 0.006f ? 1f : 0f;
                    color = new Color(0.5f, 0.49f, 0.46f) * (0.82f + broad * 0.3f) * (pore ? 0.7f : 1f) * (1f - seam * 0.25f);
                    height = 0.5f + (fine - 0.5f) * 0.25f - (pore ? 0.25f : 0f) - seam * 0.2f;
                    smooth = 0.22f + (broad - 0.5f) * 0.1f;
                    break;
                }
            }
        }

        /// <summary>R = grime coverage, G = wet coverage, A = 1 (main layer): low-frequency world noise shared by every layered material.</summary>
        private static Texture2D BlendMask()
        {
            string path = $"{Folder}/WeatherMask.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float grime = SurfaceFinishes.Smoothstep(0.45f, 0.8f, SurfaceFinishes.Fbm(u, v, 3, 4, 401)) * 0.8f;
                    float wet = SurfaceFinishes.Smoothstep(0.62f, 0.85f, SurfaceFinishes.Fbm(u, v, 4, 4, 733));
                    pixels[y * Size + x] = new Color(grime, wet, 0f, 1f); // A: the main layer's own weight (always present)
                }
            }

            return Write(path, pixels, false, false);
        }

        private static Texture2D Write(string assetPath, Color32[] pixels, bool srgb, bool normalMap)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, !srgb);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(MaskMapPacker.FullPath(assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8;
            importer.filterMode = FilterMode.Trilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
