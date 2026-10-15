using FPS.Core;
using FPS.Locomotion;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Weapons
{
    /// <summary>
    /// Procedural aiming with no baked ADS animation: solves the exact pose that puts the active sight aligner on the
    /// eye's line of sight (primary or 45° canted sight), blends it with the hip pose, drives camera zoom and look
    /// sensitivity, and runs dual-render Picture-in-Picture for magnified optics: a secondary camera renders into the
    /// lens with a geometric eye-relief shadow, while the main view blurs the periphery. Owns AdsPivot.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RealisticWeaponInertia))]
    public sealed class ProceduralADSAndOpticsController : MonoBehaviour
    {
        private static readonly int UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");
        private const float PiPActivationBlend = 0.02f;

        [Header("Aim")]
        [Tooltip("Eye relief for sights without a WeaponSight component (m).")]
        [SerializeField] private float defaultEyeRelief = 0.08f;
        [Tooltip("Zoom for sights without a WeaponSight component.")]
        [SerializeField, Min(1f)] private float defaultAdsZoom = 1.1f;
        [Tooltip("Time to roll between the primary and canted sight (s).")]
        [SerializeField, Min(0.01f)] private float sightSwitchTime = 0.18f;
        [Tooltip("Weapon dip halfway through raising it (m).")]
        [SerializeField] private float adsDip = 0.015f;

        [Header("Picture-in-Picture")]
        [SerializeField, Range(256, 4096)] private int scopeResolution = 1024;
        [SerializeField] private float scopeNearClip = 0.05f;
        [Tooltip("Scope shadow with the eye perfectly placed.")]
        [SerializeField, Range(0f, 1f)] private float restingScopeShadow = 0.25f;
        [Tooltip("Shadow growth per unit of eye misalignment (eye-box radii).")]
        [SerializeField] private float scopeShadowPerMisalignment = 0.35f;
        [SerializeField, Range(0.01f, 1f)] private float scopeShadowSmoothness = 0.45f;
        [Tooltip("How far the bright exit pupil slides with the eye offset (screen fraction).")]
        [SerializeField, Range(0f, 0.5f)] private float maxShadowShift = 0.25f;

        [Header("Peripheral Blur")]
        [SerializeField] private float blurFarStart = 0.6f;
        [SerializeField] private float blurFarEnd = 4f;

        private readonly Transform[] aligners = new Transform[WeaponModelRig.SightSlotCount];
        private readonly WeaponSight[] sights = new WeaponSight[WeaponModelRig.SightSlotCount];
        private ViewmodelRig viewmodel;
        private RealisticLocomotionController locomotion;
        private RealisticWeaponInertia inertia;
        private InputReader input;
        private Camera mainCamera;
        private float baseFieldOfView;

        private int activeSlot;
        private float adsProgress;
        private bool adsPoseValid;
        private Vector3 adsPosition;
        private Quaternion adsRotation = Quaternion.identity;

        private Camera scopeCamera;
        private RenderTexture scopeTexture;
        private MaterialPropertyBlock lensBlock;
        private Renderer litLens;
        private Volume blurVolume;
        private VolumeProfile scopeProfile, blurProfile;
        private Vignette scopeShadow;

        /// <summary>Eased hip (0) to fully aimed (1) blend.</summary>
        public float AdsBlend { get; private set; }
        public bool IsAimingDownSights => AdsBlend > 0.95f;
        /// <summary>0 = primary sight, 1 = canted sight.</summary>
        public int ActiveSightSlot => activeSlot;
        public Transform ActiveAligner => aligners[activeSlot];
        public WeaponSight ActiveSight => sights[activeSlot];
        public Camera ScopeCamera => scopeCamera;

        /// <summary>Vertical field of view at the hip (deg); aiming zooms relative to it.</summary>
        public float BaseFieldOfView
        {
            get => baseFieldOfView;
            set => baseFieldOfView = Mathf.Clamp(value, 50f, 120f);
        }

        /// <summary>Square resolution of the PiP render target (pixels).</summary>
        public int ScopeResolution
        {
            get => scopeResolution;
            set
            {
                int clamped = Mathf.Clamp(value, 256, 4096);
                if (clamped == scopeResolution) return;
                scopeResolution = clamped;
                CreateScopeTexture();
                if (litLens != null) SetLensTexture(litLens, scopeTexture);
            }
        }

        /// <summary>
        /// The AdsPivot pose (in its parent's space, where the eye sits at the origin looking down +Z) that places
        /// <paramref name="aligner"/> exactly <paramref name="eyeRelief"/> ahead of the eye with its up axis upright.
        /// </summary>
        public static void SolveSightAlignment(Transform adsPivot, Transform aligner, float eyeRelief,
            out Vector3 position, out Quaternion rotation)
        {
            Vector3 sightPosition = adsPivot.InverseTransformPoint(aligner.position);
            Quaternion sightRotation = Quaternion.Inverse(adsPivot.rotation) * aligner.rotation;
            rotation = Quaternion.Inverse(sightRotation);
            position = new Vector3(0f, 0f, eyeRelief) - rotation * sightPosition;
        }

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            locomotion = GetComponent<RealisticLocomotionController>();
            inertia = GetComponent<RealisticWeaponInertia>();
            PlayerRig playerRig = GetComponent<PlayerRig>();
            input = playerRig.Input;
            mainCamera = playerRig.PlayerCamera;
            baseFieldOfView = mainCamera.fieldOfView;
            lensBlock = new MaterialPropertyBlock();

            // Volumes are filtered by layer: the main camera never sees the viewmodel layer (scope-only volume),
            // the scope camera never sees the Player layer (main-only blur volume).
            if (!mainCamera.TryGetComponent(out HDAdditionalCameraData mainData))
                mainData = mainCamera.gameObject.AddComponent<HDAdditionalCameraData>();
            mainData.volumeLayerMask = ~(1 << GameLayers.FirstPersonViewmodel);

            CreateScopeCamera();
            CreateVolumes();
        }

        private void OnEnable()
        {
            viewmodel.WeaponChanged += OnWeaponChanged;
            if (input != null) input.SwitchSightPressed += OnSwitchSight;
            OnWeaponChanged();
        }

        private void OnDisable()
        {
            viewmodel.WeaponChanged -= OnWeaponChanged;
            if (input != null) input.SwitchSightPressed -= OnSwitchSight;
            DeactivateScope();
            ApplyView(1f, 1f, 0f);
        }

        private void OnDestroy()
        {
            if (scopeTexture != null)
            {
                scopeTexture.Release();
                Destroy(scopeTexture);
            }

            Destroy(scopeProfile);
            Destroy(blurProfile);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Transform aligner = aligners[activeSlot];
            WeaponDataSO data = viewmodel.Data;
            if (viewmodel.Weapon == null || data == null || aligner == null)
            {
                adsProgress = 0f;
                AdsBlend = 0f;
                DeactivateScope();
                ApplyView(1f, 1f, 0f);
                return;
            }

            bool wantsAds = locomotion.IsAiming && !inertia.IsTucked;
            adsProgress = Mathf.MoveTowards(adsProgress, wantsAds ? 1f : 0f, dt / data.AdsTime);
            float blend = AdsBlend = Mathf.SmoothStep(0f, 1f, adsProgress);

            WeaponSight sight = sights[activeSlot];
            Transform pivot = viewmodel.AdsPivot;
            SolveSightAlignment(pivot, aligner, sight != null ? sight.EyeRelief : defaultEyeRelief,
                out Vector3 sightPosition, out Quaternion sightRotation);
            if (!adsPoseValid)
            {
                adsPosition = sightPosition;
                adsRotation = sightRotation;
                adsPoseValid = true;
            }
            else
            {
                // Rolling onto the canted sight (or back) eases toward the newly solved pose.
                float k = 1f - Mathf.Exp(-3f * dt / sightSwitchTime);
                adsPosition = Vector3.Lerp(adsPosition, sightPosition, k);
                adsRotation = Quaternion.Slerp(adsRotation, sightRotation, k);
            }

            Quaternion hipRotation = Quaternion.Euler(data.HipEuler);
            Vector3 stock = viewmodel.Weapon.StockPivotSocket != null
                ? pivot.InverseTransformPoint(viewmodel.Weapon.StockPivotSocket.position)
                : Vector3.zero;
            Vector3 hipPosition = data.HipStockOffset - hipRotation * stock;

            Vector3 position = Vector3.LerpUnclamped(hipPosition, adsPosition, blend);
            position.y -= Mathf.Sin(blend * Mathf.PI) * adsDip;
            pivot.SetLocalPositionAndRotation(position, Quaternion.Slerp(hipRotation, adsRotation, blend));

            bool magnified = sight != null && sight.IsMagnified;
            float zoom = Mathf.Lerp(1f, sight != null ? sight.AdsZoom : defaultAdsZoom, blend);
            float magnification = magnified ? Mathf.Lerp(1f, sight.Magnification, blend) : 1f;
            ApplyView(zoom, magnification, magnified ? blend : 0f);

            if (magnified && blend > PiPActivationBlend) UpdateScope(aligner, sight);
            else DeactivateScope();
        }

        private void OnWeaponChanged()
        {
            DeactivateScope();
            scopeCamera.transform.SetParent(transform, false); // never let the outgoing weapon take the camera with it

            WeaponModelRig weapon = viewmodel.Weapon;
            for (int slot = 0; slot < aligners.Length; slot++)
            {
                WeaponSight sight = null;
                aligners[slot] = weapon != null ? weapon.GetSightAligner(slot, out sight) : null;
                sights[slot] = sight;
                if (sight != null && sight.LensRenderer != null) SetLensTexture(sight.LensRenderer, Texture2D.blackTexture);
            }

            activeSlot = aligners[0] != null ? 0 : 1;
            adsProgress = 0f;
            AdsBlend = 0f;
            adsPoseValid = false;
        }

        private void OnSwitchSight()
        {
            int other = 1 - activeSlot;
            if (aligners[other] != null) activeSlot = other;
        }

        private void ApplyView(float zoom, float opticMagnification, float blurWeight)
        {
            float fieldOfView = baseFieldOfView / zoom;
            mainCamera.fieldOfView = fieldOfView;
            float zoomSensitivity = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(baseFieldOfView * 0.5f * Mathf.Deg2Rad);
            locomotion.LookSensitivityMultiplier = zoomSensitivity / opticMagnification;
            blurVolume.weight = blurWeight;
        }

        private void UpdateScope(Transform aligner, WeaponSight sight)
        {
            Transform scopeTransform = scopeCamera.transform;
            if (scopeTransform.parent != aligner)
            {
                scopeTransform.SetParent(aligner, false);
                scopeTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            Renderer lens = sight.LensRenderer;
            if (litLens != lens)
            {
                if (litLens != null) SetLensTexture(litLens, Texture2D.blackTexture);
                litLens = lens;
                SetLensTexture(lens, scopeTexture);
            }

            // True magnification: the lens subtends some angle at the eye; the scope shows 1/M of that angle across it.
            Vector3 eye = mainCamera.transform.position;
            Bounds bounds = lens.localBounds;
            Vector3 extents = Vector3.Scale(bounds.extents, lens.transform.lossyScale);
            float lensRadius = Mathf.Max(extents.x, extents.y);
            float lensDistance = Mathf.Max(0.001f, Vector3.Distance(eye, lens.transform.TransformPoint(bounds.center)));
            float lensAngle = 2f * Mathf.Atan2(lensRadius, lensDistance) * Mathf.Rad2Deg;
            scopeCamera.fieldOfView = Mathf.Clamp(lensAngle / sight.Magnification, 0.5f, 60f);

            // Eye-relief shadow: how far the eye sits from the exit pupil, laterally and along the axis.
            Vector3 eyeLocal = aligner.InverseTransformPoint(eye);
            Vector2 lateral = new Vector2(eyeLocal.x, eyeLocal.y) / sight.EyeBoxRadius;
            float axial = (-eyeLocal.z - sight.EyeRelief) / sight.EyeReliefTolerance;
            float misalignment = Mathf.Sqrt(lateral.sqrMagnitude + axial * axial);
            scopeShadow.intensity.Override(Mathf.Clamp01(restingScopeShadow + misalignment * scopeShadowPerMisalignment));
            scopeShadow.center.Override(new Vector2(0.5f, 0.5f) - Vector2.ClampMagnitude(lateral, 1f) * maxShadowShift);

            scopeCamera.enabled = true;
        }

        private void DeactivateScope()
        {
            if (scopeCamera != null) scopeCamera.enabled = false;
            if (litLens != null) SetLensTexture(litLens, Texture2D.blackTexture);
            litLens = null;
        }

        private void SetLensTexture(Renderer lens, Texture texture)
        {
            lens.GetPropertyBlock(lensBlock);
            lensBlock.SetTexture(UnlitColorMapId, texture);
            lens.SetPropertyBlock(lensBlock);
        }

        private void CreateScopeCamera()
        {
            var cameraObject = new GameObject("PiP Scope Camera");
            cameraObject.transform.SetParent(transform, false);
            scopeCamera = cameraObject.AddComponent<Camera>();
            scopeCamera.enabled = false;
            scopeCamera.cullingMask = ~(1 << GameLayers.FirstPersonViewmodel);
            scopeCamera.nearClipPlane = scopeNearClip;
            scopeCamera.farClipPlane = mainCamera.farClipPlane;
            scopeCamera.depth = mainCamera.depth - 1f; // render before the main view samples it

            var data = cameraObject.AddComponent<HDAdditionalCameraData>();
            data.volumeLayerMask = ~(1 << GameLayers.Player);
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            CreateScopeTexture();
        }

        private void CreateScopeTexture()
        {
            if (scopeTexture != null)
            {
                scopeCamera.targetTexture = null;
                scopeTexture.Release();
                Destroy(scopeTexture);
            }

            scopeTexture = new RenderTexture(scopeResolution, scopeResolution, 0, GraphicsFormat.R16G16B16A16_SFloat)
            {
                name = "PiP Scope",
                useMipMap = false,
            };
            scopeTexture.Create();
            scopeCamera.targetTexture = scopeTexture;
        }

        private void CreateVolumes()
        {
            CreateGlobalVolume("PiP Scope Volume", GameLayers.FirstPersonViewmodel, out scopeProfile);
            scopeShadow = scopeProfile.Add<Vignette>();
            scopeShadow.color.Override(Color.black);
            scopeShadow.rounded.Override(true);
            scopeShadow.roundness.Override(1f);
            scopeShadow.smoothness.Override(scopeShadowSmoothness);
            scopeShadow.intensity.Override(1f);
            scopeShadow.center.Override(new Vector2(0.5f, 0.5f));
            scopeProfile.Add<Tonemapping>().mode.Override(TonemappingMode.None); // the main camera tonemaps the lens once
            scopeProfile.Add<MotionBlur>().intensity.Override(0f);

            blurVolume = CreateGlobalVolume("ADS Peripheral Blur", GameLayers.Player, out blurProfile);
            DepthOfField depthOfField = blurProfile.Add<DepthOfField>();
            depthOfField.focusMode.Override(DepthOfFieldMode.Manual);
            depthOfField.nearFocusStart.Override(0f);
            depthOfField.nearFocusEnd.Override(0f);
            depthOfField.farFocusStart.Override(blurFarStart);
            depthOfField.farFocusEnd.Override(blurFarEnd);
            blurVolume.weight = 0f;
        }

        private Volume CreateGlobalVolume(string volumeName, int layer, out VolumeProfile profile)
        {
            var volumeObject = new GameObject(volumeName) { layer = layer };
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = volumeName;
            volume.sharedProfile = profile;
            return volume;
        }
    }
}
