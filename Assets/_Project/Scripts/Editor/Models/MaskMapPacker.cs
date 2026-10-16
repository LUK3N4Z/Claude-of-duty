using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Packs separate PBR maps into HDRP's Mask Map: R metallic, G ambient occlusion, B detail mask, A smoothness
    /// (inverted roughness). Sources are read straight from their image files, so packing works in -nographics batch
    /// mode and needs no readable import settings. Maps of different sizes are resampled to the largest.
    /// </summary>
    internal static class MaskMapPacker
    {
        public static Color32 Pack(float metallic, float occlusion, float detailMask, float roughness) =>
            new Color32(ToByte(metallic), ToByte(occlusion), ToByte(detailMask), ToByte(1f - roughness));

        /// <summary>
        /// Packs greyscale maps (red channel read). Missing occlusion is white (unoccluded); missing metallic is black;
        /// missing roughness is mid (0.5). The detail mask is white so later detail maps apply everywhere.
        /// </summary>
        public static Color32[] Pack(Color32[] metallic, Color32[] roughness, Color32[] occlusion, int count)
        {
            var packed = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                float m = metallic != null ? metallic[i].r / 255f : 0f;
                float r = roughness != null ? roughness[i].r / 255f : 0.5f;
                float ao = occlusion != null ? occlusion[i].r / 255f : 1f;
                packed[i] = Pack(m, ao, 1f, r);
            }

            return packed;
        }

        /// <summary>
        /// Unpacks a glTF-style ORM map (R occlusion, G roughness, B metallic) into HDRP mask pixels.
        /// </summary>
        public static Color32[] FromOrm(Color32[] orm)
        {
            var packed = new Color32[orm.Length];
            for (int i = 0; i < orm.Length; i++) packed[i] = Pack(orm[i].b / 255f, orm[i].r / 255f, 1f, orm[i].g / 255f);
            return packed;
        }

        /// <summary>
        /// Builds the mask map PNG at <paramref name="outputPath"/> (asset path) from image files (null entries are
        /// treated as missing) and imports it as linear. Returns the imported texture, or null when nothing to pack.
        /// </summary>
        public static Texture2D BuildFromFiles(string metallicPath, string roughnessPath, string occlusionPath, string ormPath, string outputPath)
        {
            Texture2D metallic = Load(metallicPath), roughness = Load(roughnessPath), occlusion = Load(occlusionPath), orm = Load(ormPath);
            try
            {
                int size = 0;
                foreach (Texture2D source in new[] { metallic, roughness, occlusion, orm })
                {
                    if (source != null) size = Mathf.Max(size, Mathf.Max(source.width, source.height));
                }

                if (size == 0) return null;
                Color32[] packed = orm != null && metallic == null && roughness == null
                    ? FromOrm(Resample(orm, size))
                    : Pack(Resample(metallic, size), Resample(roughness, size), Resample(occlusion, size), size * size);

                var output = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                output.SetPixels32(packed);
                output.Apply(false);
                File.WriteAllBytes(FullPath(outputPath), output.EncodeToPNG());
                Object.DestroyImmediate(output);
            }
            finally
            {
                foreach (Texture2D source in new[] { metallic, roughness, occlusion, orm })
                {
                    if (source != null) Object.DestroyImmediate(source);
                }
            }

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(outputPath);
            importer.sRGBTexture = false;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }

        private static Texture2D Load(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string full = FullPath(assetPath);
            if (!File.Exists(full)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            return texture.LoadImage(File.ReadAllBytes(full)) ? texture : null;
        }

        private static Color32[] Resample(Texture2D source, int size)
        {
            if (source == null) return null;
            if (source.width == size && source.height == size) return source.GetPixels32();
            var result = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    result[y * size + x] = source.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size);
            }

            return result;
        }

        private static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        internal static string FullPath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath);
    }
}
