#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Gameplay
{
    /// <summary>
    /// Editor workaround for HDRP 17.6: entering Play mode resets the local volumetric fog manager at
    /// SubsystemRegistration, discarding the buffers the already-running pipeline allocated in its constructor, so every
    /// frame with volumetric fog throws in "Fog Volume And VFX Voxelization". Re-allocates them before the first scene
    /// loads. Players are unaffected (the reset is editor-only).
    /// </summary>
    internal static class VolumetricFogBufferRepair
    {
        private const string ManagerType = "UnityEngine.Rendering.HighDefinition.LocalVolumetricFogManager";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Repair()
        {
            // A pipeline created after the reset allocates its own buffers.
            if (!(RenderPipelineManager.currentPipeline is HDRenderPipeline)) return;
            if (!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset asset)) return;

            Type type = typeof(HDRenderPipeline).Assembly.GetType(ManagerType);
            object manager = type?.GetProperty("manager", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo isInitialized = type?.GetMethod("IsInitialized", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo initialize = type?.GetMethod("InitializeGraphicsBuffers", BindingFlags.NonPublic | BindingFlags.Instance);
            if (manager == null || isInitialized == null || initialize == null)
            {
                Debug.LogWarning("[VolumetricFogBufferRepair] HDRP fog manager API changed; repair skipped.");
                return;
            }

            if ((bool)isInitialized.Invoke(manager, null)) return;
            initialize.Invoke(manager, new object[] { asset.currentPlatformRenderPipelineSettings.lightLoopSettings.maxLocalVolumetricFogOnScreen });
        }
    }
}
#endif
