using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Raised on <see cref="EventBus{T}"/> when a stun grenade detonates. Every eye and ear (player camera, AI heads)
    /// computes its own exposure with <see cref="Exposure"/>.
    /// </summary>
    public struct FlashbangEvent
    {
        /// <summary>Distance inside which a stun grenade blinds and deafens fully (m).</summary>
        public const float FullEffectRadius = 4f;
        /// <summary>Distance beyond which the flash no longer blinds (m).</summary>
        public const float BlindRadius = 18f;
        /// <summary>Distance beyond which the bang no longer deafens (m).</summary>
        public const float DeafRadius = 25f;

        public Vector3 Position;
        /// <summary>1 = a standard stun grenade (~170 dB, millions of candela).</summary>
        public float Intensity;

        /// <summary>
        /// Blindness 0-1 for an eye at <paramref name="eye"/> looking along <paramref name="forward"/>: falls off with
        /// distance, is strongest when facing the bang and drops to bounce light when a wall is in between. Deafness
        /// falls off more slowly and passes through walls at reduced strength.
        /// </summary>
        public static float Exposure(Vector3 bang, float intensity, Vector3 eye, Vector3 forward, out float deafness)
        {
            Vector3 toBang = bang - eye;
            float distance = toBang.magnitude;
            bool occluded = Physics.Linecast(bang + Vector3.up * 0.1f, eye, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);

            float hearing = Mathf.Clamp01(1f - (distance - FullEffectRadius) / (DeafRadius - FullEffectRadius));
            deafness = Mathf.Clamp01(intensity * hearing * (occluded ? 0.6f : 1f));

            float sight = Mathf.Clamp01(1f - (distance - FullEffectRadius) / (BlindRadius - FullEffectRadius));
            float facing = distance > 1e-3f ? Vector3.Dot(forward, toBang / distance) : 1f;
            float facingFactor = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((facing + 0.2f) / 1.2f)); // light off the walls still dazzles
            return Mathf.Clamp01(intensity * sight * facingFactor * (occluded ? 0.15f : 1f));
        }
    }
}
