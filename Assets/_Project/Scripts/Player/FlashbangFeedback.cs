using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Locomotion
{
    /// <summary>
    /// Stun-grenade whiteout: the view burns out to white (exposure boost, colour drained) for a hold time that scales
    /// with exposure, then fades back. Facing the bang and standing close make it worse; a wall in between leaves only
    /// bounce light. Deafness and ringing are the acoustic system's job.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class FlashbangFeedback : MonoBehaviour
    {
        [Tooltip("Exposure boost at full blindness (EV): the view saturates to white.")]
        [SerializeField] private float maxExposureBoost = 9f;
        [Tooltip("Seconds of whiteout per unit of exposure before it starts to fade.")]
        [SerializeField, Min(0f)] private float holdTime = 2.5f;
        [Tooltip("Time for full blindness to fade once the hold ends (s).")]
        [SerializeField, Min(0.1f)] private float fadeTime = 3f;

        private Transform eye;
        private Volume volume;
        private VolumeProfile profile;
        private ColorAdjustments colorAdjustments;
        private float hold;

        /// <summary>Current blindness, 0-1.</summary>
        public float Blindness { get; private set; }

        private void Awake()
        {
            eye = GetComponent<PlayerRig>().PlayerCamera.transform;

            // Player layer: only the main camera's volume mask sees it (the PiP scope camera excludes it).
            var volumeObject = new GameObject("Flashbang Whiteout") { layer = GameLayers.Player };
            volumeObject.transform.SetParent(transform, false);
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 95f;
            volume.weight = 0f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Flashbang Whiteout";
            volume.sharedProfile = profile;
            colorAdjustments = profile.Add<ColorAdjustments>();
            colorAdjustments.postExposure.Override(0f);
            colorAdjustments.saturation.Override(0f);
        }

        private void OnEnable() => EventBus<FlashbangEvent>.Subscribe(OnFlashbang);

        private void OnDisable()
        {
            EventBus<FlashbangEvent>.Unsubscribe(OnFlashbang);
            Blindness = 0f;
            hold = 0f;
            Apply();
        }

        private void OnDestroy() => Destroy(profile);

        private void Update()
        {
            if (Blindness <= 0f) return;

            if (hold > 0f) hold -= Time.deltaTime;
            else Blindness = Mathf.Max(0f, Blindness - Time.deltaTime / fadeTime);
            Apply();
        }

        private void OnFlashbang(in FlashbangEvent bang)
        {
            float exposure = FlashbangEvent.Exposure(bang.Position, bang.Intensity, eye.position, eye.forward, out _);
            if (exposure <= Blindness) return;

            Blindness = exposure;
            hold = exposure * holdTime;
            Apply();
        }

        private void Apply()
        {
            volume.weight = Blindness > 0f ? 1f : 0f;
            colorAdjustments.postExposure.value = maxExposureBoost * Blindness;
            colorAdjustments.saturation.value = -100f * Blindness;
        }
    }
}
