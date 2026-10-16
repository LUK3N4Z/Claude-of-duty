using System.Collections.Generic;
using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Weapons
{
    public enum TacticalDeviceType
    {
        /// <summary>HDRP spot light that lights volumetric fog as a visible cone.</summary>
        Flashlight,
        /// <summary>Visible beam with a glowing dot where it lands.</summary>
        Laser,
    }

    /// <summary>A rail-mounted flashlight or laser on an attachment; toggled with the ToggleDevice input.</summary>
    [DisallowMultipleComponent]
    public sealed class TacticalDevice : MonoBehaviour
    {
        [SerializeField] private TacticalDeviceType type = TacticalDeviceType.Flashlight;
        [Tooltip("Beam origin; +Z is the beam direction.")]
        [SerializeField] private Transform emitter;

        [Header("Flashlight")]
        [Tooltip("Peak beam intensity (cd). Duty weapon lights throw 10,000-50,000 cd from ~1,000 lm.")]
        [SerializeField] private float candela = 15000f;
        [Tooltip("Full spill angle; the hot spot is the inner 30%.")]
        [SerializeField, Range(5f, 90f)] private float spotAngle = 40f;
        [SerializeField] private float lightRange = 60f;
        [SerializeField, Range(0f, 16f)] private float volumetricDimmer = 1f;

        [Header("Laser")]
        [SerializeField] private Material beamMaterial;
        [SerializeField] private float laserRange = 150f;
        [SerializeField] private float beamWidth = 0.0015f;
        [SerializeField] private float dotSize = 0.012f;

        private static readonly List<TacticalDevice> litFlashlights = new List<TacticalDevice>(16);

        private Light flashlight;
        private LineRenderer beam;
        private Transform dot;

        /// <summary>Flashlights currently on (AI illumination and target conspicuity read these).</summary>
        public static IReadOnlyList<TacticalDevice> LitFlashlights => litFlashlights;

        public TacticalDeviceType Type => type;
        public bool IsOn { get; private set; }
        public Light Flashlight => flashlight;
        /// <summary>Peak beam intensity of the flashlight (cd).</summary>
        public float BeamCandela => candela;
        /// <summary>Where the laser currently lands (beam end).</summary>
        public Vector3 LaserEnd => beam != null ? beam.GetPosition(1) : Vector3.zero;

        public void Configure(TacticalDeviceType deviceType, Transform beamEmitter, Material laserMaterial)
        {
            type = deviceType;
            emitter = beamEmitter;
            beamMaterial = laserMaterial;
        }

        public void Toggle() => SetOn(!IsOn);

        public void SetOn(bool on)
        {
            IsOn = on;
            UpdateRegistration();
            if (dot != null) dot.gameObject.layer = 0; // the dot lands in the world: visible to the scope camera too
            if (flashlight != null) flashlight.enabled = on;
            if (beam != null) beam.enabled = on;
            if (dot != null) dot.gameObject.SetActive(on);
        }

        private void Awake()
        {
            if (emitter == null) emitter = transform;
            if (type == TacticalDeviceType.Flashlight) BuildFlashlight();
            else BuildLaser();
            SetOn(false);
        }

        private void OnEnable() => UpdateRegistration();

        private void OnDisable() => litFlashlights.Remove(this);

        private void UpdateRegistration()
        {
            bool lit = IsOn && flashlight != null && isActiveAndEnabled;
            bool listed = litFlashlights.Contains(this);
            if (lit && !listed) litFlashlights.Add(this);
            else if (!lit && listed) litFlashlights.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => litFlashlights.Clear();

        private void LateUpdate()
        {
            if (!IsOn || beam == null) return;

            Vector3 origin = emitter.position;
            bool hit = Physics.Raycast(origin, emitter.forward, out RaycastHit surface, laserRange, GameLayers.BallisticMask,
                QueryTriggerInteraction.Ignore);
            Vector3 end = hit ? surface.point : origin + emitter.forward * laserRange;
            beam.SetPosition(0, origin);
            beam.SetPosition(1, end);

            if (dot.gameObject.activeSelf != hit) dot.gameObject.SetActive(hit);
            if (hit) dot.SetPositionAndRotation(surface.point + surface.normal * 0.002f, Quaternion.LookRotation(-surface.normal));
        }

        private void BuildFlashlight()
        {
            var lightObject = new GameObject("Flashlight Beam");
            lightObject.transform.SetParent(emitter, false);
            flashlight = lightObject.AddComponent<Light>();
            var data = lightObject.AddComponent<HDAdditionalLightData>();
            flashlight.type = LightType.Spot;
            flashlight.lightUnit = LightUnit.Candela;
            flashlight.intensity = candela;
            flashlight.spotAngle = spotAngle;
            flashlight.innerSpotAngle = spotAngle * 0.3f;
            flashlight.range = lightRange;
            flashlight.shadows = LightShadows.Soft;
            data.affectsVolumetric = true;
            data.volumetricDimmer = volumetricDimmer;
        }

        private void BuildLaser()
        {
            beam = emitter.gameObject.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.startWidth = beam.endWidth = beamWidth;
            beam.sharedMaterial = beamMaterial;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            beam.receiveShadows = false;

            GameObject dotObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dotObject.name = "Laser Dot";
            Destroy(dotObject.GetComponent<Collider>());
            dotObject.GetComponent<MeshRenderer>().sharedMaterial = beamMaterial;
            dot = dotObject.transform;
            dot.SetParent(transform, false);
            dot.localScale = Vector3.one * dotSize;
        }
    }
}
