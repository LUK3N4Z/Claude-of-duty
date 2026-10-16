using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Runs one asset through Meshy's API (preview mesh, then PBR refine), downloads the FBX and its base colour,
    /// metallic, roughness and normal maps into the asset's folder, and lets the import pipeline build the material.
    /// Also writes the prompt sheet used to generate the same assets by hand on meshy.ai.
    /// </summary>
    internal static class MeshyAssetGenerator
    {
        private const int PollMilliseconds = 5000;
        private const string PromptSheetPath = "Assets/_Project/Art/Models/MeshyPrompts.md";

        public static async Task GenerateAsync(MeshyAssetSpec spec, string apiKey, IProgress<string> progress)
        {
            var client = new MeshyApiClient(apiKey);
            progress?.Report($"{spec.Id}: preview requested");
            string previewId = await client.CreateAsync(MeshyApiClient.PreviewJson(spec));
            MeshyTask preview = await WaitAsync(client, previewId, spec.Id, "preview", progress);

            progress?.Report($"{spec.Id}: refine (PBR textures) requested");
            string refineId = await client.CreateAsync(MeshyApiClient.RefineJson(preview.id, spec));
            MeshyTask refined = await WaitAsync(client, refineId, spec.Id, "refine", progress);

            ProjectSetupWizard.EnsureFolder(spec.Folder);
            string folder = MaskMapPacker.FullPath(spec.Folder);
            string modelUrl = refined.model_urls?.fbx ?? throw new InvalidOperationException($"{spec.Id}: Meshy returned no FBX.");
            await MeshyApiClient.DownloadAsync(modelUrl, Path.Combine(folder, $"{spec.Id}.fbx"));
            if (refined.texture_urls != null && refined.texture_urls.Length > 0)
            {
                MeshyTextureUrls textures = refined.texture_urls[0];
                await Fetch(textures.base_color, folder, $"{spec.Id}_BaseColor.png");
                await Fetch(textures.metallic, folder, $"{spec.Id}_Metallic.png");
                await Fetch(textures.roughness, folder, $"{spec.Id}_Roughness.png");
                await Fetch(textures.normal, folder, $"{spec.Id}_Normal.png");
                await Fetch(textures.emission, folder, $"{spec.Id}_Emission.png");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportedModels.EnsureMaterial(spec);
            AssetDatabase.SaveAssets();
            progress?.Report($"{spec.Id}: imported ({refined.consumed_credits + preview.consumed_credits} credits)");
        }

        /// <summary>Writes every prompt as a Markdown checklist for generating the assets on meshy.ai.</summary>
        public static string WritePromptSheet()
        {
            var sheet = new StringBuilder();
            sheet.AppendLine("# Meshy asset prompts");
            sheet.AppendLine();
            sheet.AppendLine("Generate each asset on meshy.ai (Text to 3D, Realistic, PBR on), then download **FBX with textures** and drop the");
            sheet.AppendLine("files into the folder shown. The importer packs metallic/roughness/AO into an HDRP mask map, builds the material, poses");
            sheet.AppendLine("the model in its part and binds sockets. Rebuild with `Tools/FPS Builder/3`, `4`, `5`, `7`, `8` afterwards.");
            sheet.AppendLine("If a model comes in rotated wrong, add `fit.json` next to it: `{\"euler\": {\"x\": 0, \"y\": 90, \"z\": 0}}`.");
            string category = null;
            foreach (MeshyAssetSpec spec in MeshyAssetManifest.Specs)
            {
                if (spec.Category != category)
                {
                    category = spec.Category;
                    sheet.AppendLine();
                    sheet.AppendLine($"## {category}");
                }

                sheet.AppendLine();
                sheet.AppendLine($"- [{(ImportedModels.Has(spec.Id) ? "x" : " ")}] **{spec.Id}** → `{spec.Folder}/`");
                sheet.AppendLine($"  - Prompt: {spec.Prompt}");
                sheet.AppendLine($"  - Texture prompt: {spec.TexturePrompt}");
                sheet.AppendLine($"  - Target polycount: {spec.Polycount}");
            }

            ProjectSetupWizard.EnsureFolder(ImportedModels.Root);
            File.WriteAllText(MaskMapPacker.FullPath(PromptSheetPath), sheet.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(PromptSheetPath);
            return PromptSheetPath;
        }

        [MenuItem("Tools/FPS Builder/Meshy/Write Prompt Sheet", priority = 101)]
        public static void WritePromptSheetMenu() => Debug.Log($"[FPS Builder] Meshy prompt sheet written to {WritePromptSheet()}.");

        private static async Task<MeshyTask> WaitAsync(MeshyApiClient client, string taskId, string assetId, string stage, IProgress<string> progress)
        {
            while (true)
            {
                MeshyTask task = await client.GetAsync(taskId);
                if (task.Finished)
                {
                    if (!task.Succeeded) throw new InvalidOperationException($"{assetId}: Meshy {stage} {task.status}: {task.task_error?.message}");
                    return task;
                }

                progress?.Report($"{assetId}: {stage} {task.progress}%");
                await Task.Delay(PollMilliseconds);
            }
        }

        private static Task Fetch(string url, string folder, string fileName) =>
            string.IsNullOrEmpty(url) ? Task.CompletedTask : MeshyApiClient.DownloadAsync(url, Path.Combine(folder, fileName));
    }
}
