using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Locomotion
{
    public enum PhosphorColor
    {
        White,
        Green,
    }

    /// <summary>
    /// Night vision goggles (N / hold View). Flipping them down blends in a main-camera-only volume (Player layer) that
    /// renders an image-intensified view: monochrome white or green phosphor, automatic brightness control (the
    /// exposure may drop to EV -4, so dark rooms are lifted to a readable image <see cref="Gain"/> EV above normal,
    /// while daylight, muzzle flashes and lights gate the tube down instead of whiting it out), heavy bloom halos,
    /// intensifier scintillation (grain) and the round tube mask. While they are down <see cref="NightVision.Active"/>
    /// is set, so infrared lasers become visible and weapons switch to canted active aiming.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class NightVisionGoggles : MonoBehaviour
    {
        [SerializeField] private PhosphorColor phosphor = PhosphorColor.White;
        [Tooltip("Brightness of the intensified image over the normal exposure (EV); the auto-brightness control lifts dark scenes down to EV -4.")]
        [SerializeField] private float gain = 1f;
        [Tooltip("Time to flip the goggles down or up (s).")]
        [SerializeField, Min(0.01f)] private float flipTime = 0.35f;
        [Tooltip("Auto-gating speed: how fast the gain drops on a bright source and recovers (EV/s).")]
        [SerializeField] private float gatingSpeed = 8f;

        private InputReader input;
        private Volume volume;
        private VolumeProfile profile;
        private ColorAdjustments color;

        public bool IsOn { get; private set; }
        /// <summary>0 goggles up to 1 fully down (the volume weight).</summary>
        public float Blend => volume != null ? volume.weight : 0f;
        public float Gain => gain;
        public Volume Volume => volume;

        public PhosphorColor Phosphor
        {
            get => phosphor;
            set
            {
                phosphor = value;
                if (color != null) color.colorFilter.Override(PhosphorTint(value));
            }
        }

        public static Color PhosphorTint(PhosphorColor value) =>
            value == PhosphorColor.Green ? new Color(0.42f, 1f, 0.46f) : new Color(0.82f, 0.94f, 1f);

        public void Toggle() => SetOn(!IsOn);

        public void SetOn(bool on) => IsOn = on;

        private void Awake()
        {
            input = GetComponent<PlayerRig>().Input;

            var volumeObject = new GameObject("Night Vision") { layer = GameLayers.Player }; // main camera only, never the scope
            volumeObject.transform.SetParent(transform, false);
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 90f;
            volume.weight = 0f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Night Vision";
            volume.sharedProfile = profile;

            Exposure exposure = profile.Add<Exposure>();
            exposure.compensation.Override(gain);
            exposure.limitMin.Override(-4f); // dark rooms are lifted, not left black
            exposure.adaptationMode.Override(AdaptationMode.Progressive);
            exposure.adaptationSpeedDarkToLight.Override(gatingSpeed);
            exposure.adaptationSpeedLightToDark.Override(gatingSpeed * 0.5f);

            color = profile.Add<ColorAdjustments>();
            color.saturation.Override(-100f);
            color.contrast.Override(22f);
            color.colorFilter.Override(PhosphorTint(phosphor));

            Bloom bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.65f);
            bloom.threshold.Override(0.6f);
            bloom.scatter.Override(0.8f);

            FilmGrain grain = profile.Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Large02);
            grain.intensity.Override(0.7f);
            grain.response.Override(0.35f);

            Vignette tube = profile.Add<Vignette>();
            tube.intensity.Override(0.62f);
            tube.smoothness.Override(0.22f);
            tube.rounded.Override(true);
            tube.roundness.Override(1f);
            tube.color.Override(Color.black);

            ChromaticAberration fringe = profile.Add<ChromaticAberration>();
            fringe.intensity.Override(0.15f);
        }

        private void OnEnable()
        {
            if (input != null) input.ToggleNightVisionPressed += Toggle;
        }

        private void OnDisable()
        {
            if (input != null) input.ToggleNightVisionPressed -= Toggle;
            IsOn = false;
            if (volume != null) volume.weight = 0f;
            NightVision.Active = false;
        }

        private void OnDestroy() => Destroy(profile);

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            volume.weight = Mathf.MoveTowards(volume.weight, IsOn ? 1f : 0f, dt / flipTime);
            NightVision.Active = volume.weight > 0.5f; // the tube is in front of the eye
        }
    }
}
