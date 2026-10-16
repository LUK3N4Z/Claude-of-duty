using System.Collections.Generic;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;

namespace FPS.AI
{
    /// <summary>
    /// How lit a point is, 0 (pitch dark) to 1 (daylight), from the sun (shadow-tested), sky light reaching it (open
    /// sky above lights it, a roof and walls leave dark corners) and lit flashlights (cone, inverse-square, occlusion).
    /// Sight range and detection speed scale with it.
    /// </summary>
    public static class AIIllumination
    {
        /// <summary>Illuminance at which a flashlight alone makes a target read as fully lit (lux).</summary>
        public const float ReferenceLux = 60f;
        private const float SkyProbeDistance = 30f;
        private const float SunProbeDistance = 500f;

        private static readonly Vector3[] SkyDirections =
        {
            Vector3.up,
            new Vector3(0.64f, 0.77f, 0f),
            new Vector3(-0.64f, 0.77f, 0f),
            new Vector3(0f, 0.77f, 0.64f),
            new Vector3(0f, 0.77f, -0.64f),
        };

        private static Light sun;
        private static float nextSunSearch;

        /// <summary>The directional light treated as the sun; found automatically when not assigned.</summary>
        public static Light Sun
        {
            get
            {
                if (sun == null && Time.time >= nextSunSearch)
                {
                    nextSunSearch = Time.time + 5f;
                    foreach (Light light in Object.FindObjectsByType<Light>())
                    {
                        if (light.type == LightType.Directional && light.isActiveAndEnabled)
                        {
                            sun = light;
                            break;
                        }
                    }
                }

                return sun;
            }
            set => sun = value;
        }

        /// <summary>Light level at <paramref name="point"/>; <paramref name="skyAmbient"/> is full open-sky light.</summary>
        public static float Sample(Vector3 point, float skyAmbient)
        {
            int mask = GameLayers.EnvironmentMask;
            float level = 0f;

            Light currentSun = Sun;
            if (currentSun != null && currentSun.isActiveAndEnabled &&
                !Physics.Raycast(point, -currentSun.transform.forward, SunProbeDistance, mask, QueryTriggerInteraction.Ignore))
                level += 1f;

            int open = 0;
            for (int i = 0; i < SkyDirections.Length; i++)
            {
                if (!Physics.Raycast(point, SkyDirections[i], SkyProbeDistance, mask, QueryTriggerInteraction.Ignore)) open++;
            }

            level += skyAmbient * open / SkyDirections.Length;
            level += FlashlightLevel(point, null);
            return Mathf.Clamp01(level);
        }

        /// <summary>
        /// Light reaching <paramref name="point"/> from lit flashlights, in units of <see cref="ReferenceLux"/>;
        /// <paramref name="ignore"/> skips one device (a target's own light does not light its own body much).
        /// </summary>
        public static float FlashlightLevel(Vector3 point, TacticalDevice ignore)
        {
            float level = 0f;
            IReadOnlyList<TacticalDevice> lights = TacticalDevice.LitFlashlights;
            for (int i = 0; i < lights.Count; i++)
            {
                TacticalDevice device = lights[i];
                Light light = device.Flashlight;
                if (device == ignore || light == null || !light.enabled) continue;

                Transform source = light.transform;
                Vector3 toPoint = point - source.position;
                float distance = toPoint.magnitude;
                if (distance < 1e-3f || distance > light.range) continue;

                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(source.forward, toPoint / distance), -1f, 1f)) * Mathf.Rad2Deg;
                float half = light.spotAngle * 0.5f;
                if (angle > half) continue;
                float inner = light.innerSpotAngle * 0.5f;
                float cone = angle <= inner ? 1f : 1f - (angle - inner) / Mathf.Max(0.01f, half - inner);

                if (Physics.Linecast(source.position, point, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                level += device.BeamCandela * cone / (distance * distance) / ReferenceLux;
            }

            return level;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            sun = null;
            nextSunSearch = 0f;
        }
    }
}
