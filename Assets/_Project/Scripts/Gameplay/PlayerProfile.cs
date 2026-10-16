using System;
using System.Collections.Generic;
using FPS.AI;
using FPS.Audio;
using FPS.Locomotion;

namespace FPS.Gameplay
{
    /// <summary>A saved weapon build: the base weapon and its attachments, by catalog id (asset name).</summary>
    [Serializable]
    public sealed class WeaponBlueprint
    {
        public string name = string.Empty;
        public string weapon = string.Empty;
        public List<string> attachments = new List<string>();
    }

    /// <summary>What the player spawns with: blueprint names per weapon slot and grenade ids.</summary>
    [Serializable]
    public sealed class LoadoutProfile
    {
        public string primary = string.Empty;
        public string secondary = string.Empty;
        public string lethal = string.Empty;
        public string tactical = string.Empty;
    }

    public enum MotionBlurLevel
    {
        Off,
        Low,
        High,
    }

    public enum UpscalerMode
    {
        /// <summary>Full resolution, no upscaling.</summary>
        Native,
        /// <summary>HDRP temporal anti-aliasing upscale (every platform).</summary>
        TAAU,
        /// <summary>AMD FidelityFX Super Resolution 2 (where supported).</summary>
        FSR2,
        /// <summary>NVIDIA Deep Learning Super Sampling (where supported).</summary>
        DLSS,
    }

    [Serializable]
    public sealed class GameSettings
    {
        /// <summary>Vertical field of view at the hip (deg).</summary>
        public float fieldOfView = 75f;
        /// <summary>Square PiP scope render target size (px).</summary>
        public int scopeResolution = 1024;
        public MotionBlurLevel motionBlur = MotionBlurLevel.Low;
        public UpscalerMode upscaler = UpscalerMode.Native;
        /// <summary>Internal render resolution while upscaling (% of the output).</summary>
        public int renderScale = 77;
        public AudioDynamicRange audioRange = AudioDynamicRange.StudioReference;
        /// <summary>Look sensitivity (1 = default).</summary>
        public float lookSensitivity = 1f;
        /// <summary>Extra look sensitivity factor while aiming down sights.</summary>
        public float adsSensitivity = 1f;
        public float masterVolume = 1f;
        public float effectsVolume = 1f;
        public float voiceVolume = 1f;
        public PhosphorColor nightVisionPhosphor = PhosphorColor.White;
        public AIDifficulty aiDifficulty = AIDifficulty.Regular;
        /// <summary>Key binding changes (Input System overrides JSON); empty keeps the defaults.</summary>
        public string bindingOverrides = string.Empty;
    }

    /// <summary>Everything persisted for the player, written as JSON by <see cref="SaveProfileManager"/>.</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public List<WeaponBlueprint> blueprints = new List<WeaponBlueprint>();
        public LoadoutProfile loadout = new LoadoutProfile();
        public GameSettings settings = new GameSettings();

        public WeaponBlueprint Blueprint(string blueprintName)
        {
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i].name == blueprintName) return blueprints[i];
            }

            return null;
        }
    }
}
