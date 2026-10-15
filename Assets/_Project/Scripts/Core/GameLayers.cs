using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Project layer names (single source of truth for runtime code and the setup wizard) and their indices.
    /// Indices are resolved by name because the wizard may place a layer in any free slot. First access must
    /// happen at runtime (Awake or later), never from a field initializer or constructor.
    /// </summary>
    public static class GameLayers
    {
        public const string PlayerName = "Player";
        public const string FirstPersonViewmodelName = "FirstPersonViewmodel";
        public const string WorldGeometryName = "WorldGeometry";
        public const string HitboxName = "Hitbox";
        public const string InteractableName = "Interactable";

        /// <summary>Unity built-in "Ignore Raycast" layer.</summary>
        public const int IgnoreRaycast = 2;

        public static readonly int Player = Require(PlayerName);
        public static readonly int FirstPersonViewmodel = Require(FirstPersonViewmodelName);
        public static readonly int WorldGeometry = Require(WorldGeometryName);
        public static readonly int Hitbox = Require(HitboxName);
        public static readonly int Interactable = Require(InteractableName);

        /// <summary>Solid level geometry (Default + WorldGeometry): ground probes, stance clearance, lean and mount casts.</summary>
        public static readonly int EnvironmentMask = (1 << 0) | (1 << WorldGeometry);

        private static int Require(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
                Debug.LogError($"Layer '{layerName}' is missing. Run Tools/FPS Builder/1. Initialize Project Layers & Tags.");
            return layer;
        }
    }
}
