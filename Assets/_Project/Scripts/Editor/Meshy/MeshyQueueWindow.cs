using System;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Meshy asset queue: every model the game wants, whether it is imported or still procedural, its prompt (copy it
    /// into meshy.ai), its drop folder, and (with an API key) one-click generation through the Meshy API.
    /// </summary>
    internal sealed class MeshyQueueWindow : EditorWindow
    {
        private Vector2 scroll;
        private string status = string.Empty;
        private bool busy;

        [MenuItem("Tools/FPS Builder/Meshy/Asset Queue", priority = 100)]
        private static void Open() => GetWindow<MeshyQueueWindow>("Meshy Assets");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Generate on meshy.ai (Text to 3D, Realistic, PBR), download FBX + textures into the asset's folder, then rerun " +
                                    "the builders. Assets without a model use the procedural fallback.", MessageType.Info);

            string key = EditorPrefs.GetString(MeshyApiClient.ApiKeyPreference, string.Empty);
            string edited = EditorGUILayout.PasswordField("API key (optional)", key);
            if (edited != key) EditorPrefs.SetString(MeshyApiClient.ApiKeyPreference, edited);
            bool hasKey = !string.IsNullOrEmpty(MeshyApiClient.ResolveApiKey());
            if (!hasKey) EditorGUILayout.LabelField("No key: API generation is off (the API bills separate Meshy API credits).", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Write prompt sheet")) EditorUtility.RevealInFinder(MaskMapPacker.FullPath(MeshyAssetGenerator.WritePromptSheet()));
                if (GUILayout.Button("Refresh materials"))
                {
                    foreach (MeshyAssetSpec spec in MeshyAssetManifest.Specs) ImportedModels.EnsureMaterial(spec);
                    AssetDatabase.SaveAssets();
                }
            }

            if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            string category = null;
            foreach (MeshyAssetSpec spec in MeshyAssetManifest.Specs)
            {
                if (spec.Category != category)
                {
                    category = spec.Category;
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(category, EditorStyles.boldLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool imported = ImportedModels.Has(spec.Id);
                    EditorGUILayout.LabelField(imported ? "● Imported" : "○ Procedural", GUILayout.Width(90));
                    EditorGUILayout.LabelField(spec.Id, GUILayout.Width(200));
                    if (GUILayout.Button("Copy prompt", GUILayout.Width(95))) EditorGUIUtility.systemCopyBuffer = spec.Prompt;
                    if (GUILayout.Button("Copy texture", GUILayout.Width(95))) EditorGUIUtility.systemCopyBuffer = spec.TexturePrompt;
                    if (GUILayout.Button("Folder", GUILayout.Width(60)))
                    {
                        ProjectSetupWizard.EnsureFolder(spec.Folder);
                        EditorUtility.RevealInFinder(MaskMapPacker.FullPath(spec.Folder));
                    }

                    using (new EditorGUI.DisabledScope(!hasKey || busy))
                    {
                        if (GUILayout.Button("Generate", GUILayout.Width(75))) Generate(spec);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private async void Generate(MeshyAssetSpec spec)
        {
            busy = true;
            try
            {
                await MeshyAssetGenerator.GenerateAsync(spec, MeshyApiClient.ResolveApiKey(), new Progress<string>(message =>
                {
                    status = message;
                    Repaint();
                }));
            }
            catch (Exception exception)
            {
                status = exception.Message;
                Debug.LogError($"[FPS Builder] {exception.Message}");
            }
            finally
            {
                busy = false;
                Repaint();
            }
        }
    }
}
