using System;
using FPS.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS.EditorTools
{
    /// <summary>One-click project configuration: layers, physics collision matrix and core data assets. Idempotent.</summary>
    public static class ProjectSetupWizard
    {
        private const string MenuRoot = "Tools/FPS Builder/";
        private const string InputActionsPath = "Assets/_Project/Settings/Input/InputSystem_Actions.inputactions";
        private const string CoreDataFolder = "Assets/_Project/Data/Core";
        internal const string InputReaderPath = CoreDataFolder + "/InputReader.asset";
        private const int FirstUserLayer = 8;
        private const int LayerCount = 32;

        [MenuItem(MenuRoot + "1. Initialize Project Layers & Tags", priority = 1)]
        public static void InitializeLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            int player = EnsureLayer(layers, GameLayers.PlayerName, 8);
            int viewmodel = EnsureLayer(layers, GameLayers.FirstPersonViewmodelName, 9);
            int world = EnsureLayer(layers, GameLayers.WorldGeometryName, 10);
            int hitbox = EnsureLayer(layers, GameLayers.HitboxName, 11);
            int interactable = EnsureLayer(layers, GameLayers.InteractableName, 12);
            tagManager.ApplyModifiedPropertiesWithoutUndo();

            // Viewmodel is purely visual: barrel tucking and leaning use casts against the world, never contacts.
            for (int layer = 0; layer < LayerCount; layer++) Physics.IgnoreLayerCollision(viewmodel, layer, true);
            Physics.IgnoreLayerCollision(player, hitbox, true);       // own hitboxes must never push the capsule
            Physics.IgnoreLayerCollision(hitbox, hitbox, true);       // kinematic query targets only
            Physics.IgnoreLayerCollision(interactable, player, true); // interaction is ray-based; props never block
            Physics.IgnoreLayerCollision(interactable, hitbox, true);

            AssetDatabase.SaveAssets();
            Debug.Log($"[FPS Builder] Layers ready: Player={player}, FirstPersonViewmodel={viewmodel}, " +
                      $"WorldGeometry={world}, Hitbox={hitbox}, Interactable={interactable}. Collision matrix configured.");
        }

        [MenuItem(MenuRoot + "2. Create Core Assets", priority = 2)]
        public static void CreateCoreAssets()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
            {
                Debug.LogError($"[FPS Builder] Input actions not found at {InputActionsPath}.");
                return;
            }

            var reader = AssetDatabase.LoadAssetAtPath<InputReader>(InputReaderPath);
            if (reader == null)
            {
                EnsureFolder(CoreDataFolder);
                reader = ScriptableObject.CreateInstance<InputReader>();
                AssetDatabase.CreateAsset(reader, InputReaderPath);
            }

            var serialized = new SerializedObject(reader);
            serialized.FindProperty(InputReader.ActionAssetFieldName).objectReferenceValue = actions;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Selection.activeObject = reader;
            Debug.Log($"[FPS Builder] {InputReaderPath} ready, bound to {InputActionsPath}.", reader);
        }

        private static int EnsureLayer(SerializedProperty layers, string layerName, int preferredIndex)
        {
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;
            }

            int slot = string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferredIndex).stringValue) ? preferredIndex : -1;
            for (int i = FirstUserLayer; slot < 0 && i < layers.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) slot = i;
            }

            if (slot < 0) throw new InvalidOperationException($"No free user layer for '{layerName}'.");

            layers.GetArrayElementAtIndex(slot).stringValue = layerName;
            if (slot != preferredIndex)
                Debug.LogWarning($"[FPS Builder] Layer {preferredIndex} is taken; '{layerName}' placed at {slot}.");
            return slot;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
