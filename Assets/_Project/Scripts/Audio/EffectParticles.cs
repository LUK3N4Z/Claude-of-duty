using UnityEngine;

namespace FPS.Audio
{
    /// <summary>
    /// Builds particle systems in code for HDRP/Unlit materials. HDRP/Unlit ignores vertex colour, so particles fade
    /// through a 4×4 alpha flipbook in the material's texture rather than colour-over-lifetime.
    /// </summary>
    public static class EffectParticles
    {
        public const int FlipbookTiles = 4;

        public struct Settings
        {
            public Vector2 Lifetime;
            public Vector2 Speed;
            public Vector2 Size;
            /// <summary>Size multiplier reached at the end of life.</summary>
            public float SizeGrowth;
            public float ConeAngle;
            public float ConeRadius;
            public float GravityModifier;
            public float NoiseStrength;
            public int MaxParticles;
        }

        /// <summary>A stopped, non-looping, world-space system emitting along its +Z through a cone; drive it with Emit or rate.</summary>
        public static ParticleSystem Create(string systemName, Transform parent, Material material, in Settings settings)
        {
            var systemObject = new GameObject(systemName);
            systemObject.transform.SetParent(parent, false);
            var system = systemObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true; // keeps rate-driven emitters alive; bursts use Emit
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(settings.Lifetime.x, settings.Lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(settings.Speed.x, settings.Speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(settings.Size.x, settings.Size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = settings.GravityModifier;
            main.maxParticles = settings.MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = settings.ConeAngle;
            shape.radius = Mathf.Max(0.0001f, settings.ConeRadius);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, settings.SizeGrowth));

            if (settings.NoiseStrength > 0f)
            {
                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = settings.NoiseStrength;
                noise.frequency = 0.8f;
            }

            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = FlipbookTiles;
            sheet.numTilesY = FlipbookTiles;
            sheet.cycleCount = 1;

            var renderer = systemObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (systemObject.activeInHierarchy) system.Play(); // armed; nothing appears until Emit or a rate is set
            return system;
        }
    }
}
