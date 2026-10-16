using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Builds the Windows 64-bit player from the build settings scenes (the Armory hub first) into a folder next to the
    /// repository, so build output never lands in version control. Exits with an error code in batch mode on failure.
    /// </summary>
    internal static class PlayerBuildTool
    {
        private const string OutputFolder = "../Claude of duty Build";
        private const string ExecutableName = "ClaudeOfDuty.exe";

        [MenuItem("Tools/FPS Builder/9. Build Windows Player", priority = 9)]
        public static void BuildWindowsPlayer()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled) scenes.Add(scene.path);
            }

            string project = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.GetFullPath(Path.Combine(project, OutputFolder, ExecutableName));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            BuildSummary summary = report.summary;
            Debug.Log($"[FPS Builder] Windows player {summary.result}: {output} ({summary.totalSize / 1048576} MB, {summary.totalTime.TotalMinutes:0.0} min, {scenes.Count} scenes)");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
