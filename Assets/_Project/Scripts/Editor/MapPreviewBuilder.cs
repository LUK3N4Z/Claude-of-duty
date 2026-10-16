using System.Collections.Generic;
using System.IO;
using FPS.Core;
using FPS.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools
{
    /// <summary>
    /// Draws a top-down tactical schematic of a built scene for the deployment table and the loading screen: every solid
    /// collider of the level as a block shaded by its height over a 10 m grid, objectives (terminals, plant points, zones)
    /// in amber, spawns by team (friendly green, hostile red). CPU only, so batch builds without graphics can make it.
    /// </summary>
    internal static class MapPreviewBuilder
    {
        internal const string Folder = "Assets/_Project/Art/UI";
        private const int Size = 512;

        internal static string PathFor(string sceneName) => $"{Folder}/MapPreview_{sceneName}.png";

        /// <summary>
        /// Renders <paramref name="scene"/> to <see cref="PathFor"/>, framed on its colliders, or on a square of
        /// <paramref name="halfExtent"/> metres around <paramref name="center"/> when given.
        /// </summary>
        public static Texture2D Render(Scene scene, string sceneName, Vector2? center = null, float halfExtent = 0f)
        {
            var blocks = new List<Bounds>(256);
            var objectives = new List<Bounds>(16);
            var spawns = new List<KeyValuePair<Vector3, int>>(32);
            Bounds world = default;
            bool any = false;
            int mask = GameLayers.EnvironmentMask;
            Physics.SyncTransforms(); // colliders built this session report stale bounds until synced
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger || ((1 << collider.gameObject.layer) & mask) == 0) continue;
                    Bounds bounds = collider.bounds;
                    if (bounds.size.sqrMagnitude < 1e-6f && collider.TryGetComponent(out Renderer shape)) bounds = shape.bounds;
                    blocks.Add(bounds);
                    if (any) world.Encapsulate(bounds);
                    else world = bounds;
                    any = true;
                }

                foreach (HoldInteraction terminal in root.GetComponentsInChildren<HoldInteraction>(true)) objectives.Add(new Bounds(terminal.transform.position, Vector3.one));
                foreach (ObjectiveZone zone in root.GetComponentsInChildren<ObjectiveZone>(true))
                {
                    Collider area = zone.GetComponent<Collider>();
                    objectives.Add(area != null ? area.bounds : new Bounds(zone.transform.position, Vector3.one * 4f));
                }

                foreach (SpawnPoint spawn in root.GetComponentsInChildren<SpawnPoint>(true)) spawns.Add(new KeyValuePair<Vector3, int>(spawn.transform.position, spawn.Team));
            }

            Vector2 middle = center ?? (any ? new Vector2(world.center.x, world.center.z) : Vector2.zero);
            float half = halfExtent > 0f ? halfExtent : any ? Mathf.Max(world.extents.x, world.extents.z) * 1.04f : 10f;
            var pixels = new Color32[Size * Size];
            var background = new Color32(6, 14, 18, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;

            // 10 m grid.
            for (float line = Mathf.Ceil((middle.x - half) / 10f) * 10f; line <= middle.x + half; line += 10f) Column(pixels, ToPixel(line, middle.x, half));
            for (float line = Mathf.Ceil((middle.y - half) / 10f) * 10f; line <= middle.y + half; line += 10f) Row(pixels, ToPixel(line, middle.y, half));

            blocks.Sort((a, b) => a.max.y.CompareTo(b.max.y)); // low first, so walls draw over floors
            foreach (Bounds block in blocks)
            {
                bool slab = block.size.y < 0.25f || block.size.x * block.size.z > half * half; // floors and ground plates
                float shade = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(block.max.y / 4f));
                Color32 fill = slab ? new Color32(12, 28, 34, 255) : new Color32((byte)(40 * shade), (byte)(140 * shade), (byte)(160 * shade), 255);
                Color32 edge = slab ? new Color32(18, 40, 48, 255) : new Color32(120, 230, 245, 255);
                Box(pixels, block, middle, half, fill, edge);
            }

            var amber = new Color32(255, 176, 48, 255);
            foreach (Bounds objective in objectives) Box(pixels, objective, middle, half, new Color32(110, 70, 16, 255), amber);
            foreach (KeyValuePair<Vector3, int> spawn in spawns)
                Dot(pixels, ToPixel(spawn.Key.x, middle.x, half), ToPixel(spawn.Key.z, middle.y, half), 3,
                    spawn.Value == 0 ? new Color32(90, 230, 120, 255) : new Color32(240, 80, 70, 255));

            ProjectSetupWizard.EnsureFolder(Folder);
            string path = PathFor(sceneName);
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(MaskMapPacker.FullPath(path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static int ToPixel(float metres, float middle, float half) => Mathf.RoundToInt((metres - (middle - half)) / (2f * half) * (Size - 1));

        private static void Box(Color32[] pixels, Bounds bounds, Vector2 middle, float half, Color32 fill, Color32 edge)
        {
            int x0 = Mathf.Clamp(ToPixel(bounds.min.x, middle.x, half), 0, Size - 1), x1 = Mathf.Clamp(ToPixel(bounds.max.x, middle.x, half), 0, Size - 1);
            int y0 = Mathf.Clamp(ToPixel(bounds.min.z, middle.y, half), 0, Size - 1), y1 = Mathf.Clamp(ToPixel(bounds.max.z, middle.y, half), 0, Size - 1);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++) pixels[y * Size + x] = x == x0 || x == x1 || y == y0 || y == y1 ? edge : fill;
            }
        }

        private static void Dot(Color32[] pixels, int cx, int cy, int radius, Color32 color)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= Size || y >= Size || (x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
                    pixels[y * Size + x] = color;
                }
            }
        }

        private static void Column(Color32[] pixels, int x)
        {
            if (x < 0 || x >= Size) return;
            for (int y = 0; y < Size; y++) pixels[y * Size + x] = new Color32(12, 30, 36, 255);
        }

        private static void Row(Color32[] pixels, int y)
        {
            if (y < 0 || y >= Size) return;
            for (int x = 0; x < Size; x++) pixels[y * Size + x] = new Color32(12, 30, 36, 255);
        }
    }
}
