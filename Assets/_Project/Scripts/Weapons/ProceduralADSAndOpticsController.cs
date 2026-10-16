using FPS.Audio;
using FPS.Ballistics;
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
    /// sensitivity, and runs dual-render Picture-in-Picture for magnified optics: a secondary camera renders the
    /// magnified view, a lens shader pass adds glass refraction, rim chromatic aberration, the parallax eye-relief
    /// shadow and the etched reticle with its illuminated (blooming) centre, while the main view blurs the periphery.
    /// The scope camera renders scene radiance (no exposure, grading or tonemapping of its own) and the lens shows it as
    /// exposure-weighted emission, so the main camera exposes and grades the scope picture exactly like the world.
    /// With night vision down and a laser fitted it switches to canted active aiming: the weapon rolls 45° under the
    /// goggles and aims by the infrared laser instead of the optic. Owns AdsPivot.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RealisticWeaponInertia))]
    public sealed class ProceduralADSAndOpticsController : MonoBehaviour
    {
        private static readonly int EmissiveColorMapId = Shader.PropertyToID("_EmissiveColorMap");
        private static readonly int ShadowCenterId = Shader.PropertyToID("_ShadowCenter");
        private static readonly int ShadowRadiusId = Shader.PropertyToID("_ShadowRadius");
        private static readonly int ShadowSoftnessId = Shader.PropertyToID("_ShadowSoftness");
        private static readonly int AberrationId = Shader.PropertyToID("_Aberration");
        private static readonly int DistortionId = Shader.PropertyToID("_Distortion");
        private static readonly int ReticleStyleId = Shader.PropertyToID("_ReticleStyle");
        private static readonly int ReticleScaleId = Shader.PropertyToID("_ReticleScale");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
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

        [Header("Lens")]
        [Tooltip("Hidden/FPS/ScopeLens. Without it the lens shows the raw scope image and a vignette shadow.")]
        [SerializeField] private Shader lensShader;
        [Tooltip("Rim chromatic aberration (UV offset per channel at the rim).")]
        [SerializeField, Range(0f, 0.05f)] private float lensAberration = 0.012f;
        [Tooltip("Barrel refraction toward the rim.")]
        [SerializeField, Range(0f, 0.5f)] private float lensDistortion = 0.12f;

        [Header("Active Aiming (night vision)")]
        [Tooltip("Stock contact point in eye space while aiming by laser under goggles (m).")]
        [SerializeField] private Vector3 activeAimStock = new Vector3(0.08f, -0.14f, -0.07f);
        [Tooltip("Weapon cant while active aiming (deg about the bore): the laser comes up under the goggles.")]
        [SerializeField] private float activeAimCant = -45f;

        [Header("Shot Impulse")]
        [Tooltip("FOV kick of an unsuppressed shot at the reference muzzle energy (deg).")]
        [SerializeField] private float shotFovKick = 1.5f;
        [Tooltip("Length of the kick (s).")]
        [SerializeField, Min(0.01f)] private float shotKickDuration = 0.04f;
        [Tooltip("Edge chromatic aberration at the peak of the kick.")]
        [SerializeField, Range(0f, 1f)] private float shotAberration = 0.5f;
        [Tooltip("Muzzle energy that kicks at the nominal amount (J): 5.56 from a 14.5 in barrel.")]
        [SerializeField, Min(1f)] private float referenceMuzzleEnergy = 1700f;

        [Header("Peripheral Blur")]
        [SerializeField] private float blurFarStart = 0.6f;
        [SerializeField] private float blurFarEnd = 4f;

        private readonly Transform[] aligners = new Transform[WeaponModelRig.SightSlotCount];
        private readonly WeaponSight[] sights = new WeaponSight[WeaponModelRig.SightSlotCount];
        private ViewmodelRig viewmodel;
        private RealisticLocomotionController locomotion;
        private RealisticWeaponInertia inertia;
        private RealisticWeaponController weaponController;
        private InputReader input;
        private Camera mainCamera;
        private float baseFieldOfView;

        private int activeSlot;
        private float adsProgress;
        private bool adsPoseValid;
        private Vector3 adsPosition;
        private Quaternion adsRotation = Quaternion.identity;

        private Camera scopeCamera;
        private RenderTexture scopeTexture, lensTexture;
        private Material lensMaterial;
        private TacticalDevice laser;
        private bool laserForced;
        private MaterialPropertyBlock lensBlock;
        private Renderer litLens;
        private Volume blurVolume, impulseVolume;
        private VolumeProfile scopeProfile, blurProfile, impulseProfile;
        private float shotKickStrength, shotKickTime = float.PositiveInfinity;
        private Vignette scopeShadow;

        /// <summary>Eased hip (0) to fully aimed (1) blend.</summary>
        public float AdsBlend { get; private set; }
        public bool IsAimingDownSights => AdsBlend > 0.95f;
        /// <summary>0 = primary sight, 1 = canted sight.</summary>
        public int ActiveSightSlot => activeSlot;
        public Transform ActiveAligner => aligners[activeSlot];
        public WeaponSight ActiveSight => sights[activeSlot];
        public Camera ScopeCamera => scopeCamera;
        /// <summary>The shader lens pass is running (the lens shows the processed image).</summary>
        public bool LensProcessing => lensMaterial != null;
        /// <summary>The image the lens shows: processed when the lens shader runs, else the raw scope render.</summary>
        public RenderTexture LensTexture => lensMaterial != null ? lensTexture : scopeTexture;
        public Material LensMaterial => lensMaterial;
        /// <summary>Aiming by laser with night vision down (weapon canted, no optic).</summary>
        public bool IsActiveAiming { get; private set; }
        /// <summary>Current FOV kick from an unsuppressed shot (deg), on top of the zoomed field of view.</summary>
        public float ShotKickDegrees { get; private set; }

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
                if (litLens != null) SetLensTexture(litLens, LensTexture);
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
            weaponController = GetComponent<RealisticWeaponController>();
            PlayerRig playerRig = GetComponent<PlayerRig>();
            input = playerRig.Input;
            mainCamera = playerRig.PlayerCamera;
            baseFieldOfView = mainCamera.fieldOfView;
            lensBlock = new MaterialPropertyBlock();
            if (lensShader != null && lensShader.isSupported)
                lensMaterial = new Material(lensShader) { name = "PiP Lens", hideFlags = HideFlags.HideAndDontSave };

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
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            EventBus<GunshotEvent>.Subscribe(OnGunshot);
            OnWeaponChanged();
        }

        private void OnDisable()
        {
            viewmodel.WeaponChanged -= OnWeaponChanged;
            if (input != null) input.SwitchSightPressed -= OnSwitchSight;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            EventBus<GunshotEvent>.Unsubscribe(OnGunshot);
            shotKickTime = float.PositiveInfinity;
            ShotKickDegrees = 0f;
            impulseVolume.weight = 0f;
            ReleaseForcedLaser();
            IsActiveAiming = false;
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

            if (lensTexture != null)
            {
                lensTexture.Release();
                Destroy(lensTexture);
            }

            if (lensMaterial != null) Destroy(lensMaterial);
            Destroy(scopeProfile);
            Destroy(blurProfile);
            Destroy(impulseProfile);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateShotKick(dt);

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

            bool wantsAds = locomotion.IsAiming && !inertia.IsTucked && !inertia.IsCompressed &&
                            (weaponController == null || !weaponController.BlocksAiming);
            adsProgress = Mathf.MoveTowards(adsProgress, wantsAds ? 1f : 0f, dt / data.AdsTime);
            float blend = AdsBlend = Mathf.SmoothStep(0f, 1f, adsProgress);

            WeaponSight sight = sights[activeSlot];
            Transform pivot = viewmodel.AdsPivot;
            Vector3 stock = viewmodel.Weapon.StockPivotSocket != null
                ? pivot.InverseTransformPoint(viewmodel.Weapon.StockPivotSocket.position)
                : Vector3.zero;
            SolveSightAlignment(pivot, aligner, sight != null ? sight.EyeRelief : defaultEyeRelief,
                out Vector3 sightPosition, out Quaternion sightRotation);

            // Goggles down: the eye cannot get behind the optic, so the weapon cants under them and aims by laser.
            bool activeAim = NightVision.Active && laser != null;
            if (activeAim != IsActiveAiming)
            {
                IsActiveAiming = activeAim;
                if (!activeAim) ReleaseForcedLaser();
            }

            if (activeAim)
            {
                if (wantsAds && !laser.IsOn)
                {
                    laser.SetOn(true);
                    laserForced = true;
                }

                sightRotation = Quaternion.Euler(0f, 0f, activeAimCant);
                sightPosition = activeAimStock - sightRotation * stock;
            }
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
            Vector3 hipPosition = data.HipStockOffset - hipRotation * stock;

            Vector3 position = Vector3.LerpUnclamped(hipPosition, adsPosition, blend);
            position.y -= Mathf.Sin(blend * Mathf.PI) * adsDip;
            pivot.SetLocalPositionAndRotation(position, Quaternion.Slerp(hipRotation, adsRotation, blend));

            bool magnified = !activeAim && sight != null && sight.IsMagnified;
            float zoom = activeAim ? 1f : Mathf.Lerp(1f, sight != null ? sight.AdsZoom : defaultAdsZoom, blend);
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
            laserForced = false; // the old weapon's laser went with it
            laser = null;
            if (weapon != null)
            {
                foreach (TacticalDevice device in weapon.GetComponentsInChildren<TacticalDevice>(true))
                {
                    if (device.Type == TacticalDeviceType.Laser) laser = device;
                }
            }

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

        /// <summary>FOV kick for a shot of <paramref name="energy"/> J relative to <paramref name="reference"/> J (scales with momentum, ~sqrt E).</summary>
        public static float ShotKickScale(float energy, float reference) => Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, energy) / Mathf.Max(1f, reference)), 0.4f, 1.8f);

        /// <summary>Kick envelope at <paramref name="t"/> s after the shot: up in the first eighth, linear back to 0 by <paramref name="duration"/>.</summary>
        public static float ShotKickEnvelope(float t, float duration)
        {
            if (t < 0f || t >= duration) return 0f;
            float attack = duration * 0.125f;
            return t < attack ? t / attack : 1f - (t - attack) / (duration - attack);
        }

        private void OnGunshot(in GunshotEvent shot)
        {
            if (shot.Shooter != transform || shot.MuzzleFlash <= 0.05f) return; // a suppressor keeps the blast in the can
            WeaponDataSO data = viewmodel.Data;
            CaliberDataSO caliber = data != null ? data.Caliber : null;
            float energy = caliber != null
                ? caliber.KineticEnergy(caliber.MuzzleVelocity(data.BarrelLengthInches) * data.MuzzleVelocityMultiplier)
                : referenceMuzzleEnergy;
            shotKickStrength = ShotKickScale(energy, referenceMuzzleEnergy) * Mathf.Min(1f, shot.MuzzleFlash);
            shotKickTime = 0f;
        }

        private void UpdateShotKick(float dt)
        {
            bool fresh = shotKickTime == 0f;
            if (shotKickTime < shotKickDuration) shotKickTime += dt;
            // The shot's own frame shows the kick, at its peak at most: a long frame must not skip a 40 ms impulse.
            float sample = fresh ? Mathf.Min(shotKickTime, shotKickDuration * 0.125f) : shotKickTime;
            float envelope = ShotKickEnvelope(sample, shotKickDuration) * shotKickStrength;
            ShotKickDegrees = shotFovKick * envelope;
            impulseVolume.weight = Mathf.Clamp01(envelope);
        }

        private void ApplyView(float zoom, float opticMagnification, float blurWeight)
        {
            float fieldOfView = baseFieldOfView / zoom;
            mainCamera.fieldOfView = fieldOfView + ShotKickDegrees;
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
                SetLensTexture(lens, LensTexture);
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
            float closing = Mathf.Clamp01(restingScopeShadow + misalignment * scopeShadowPerMisalignment);
            Vector2 shadowCenter = new Vector2(0.5f, 0.5f) - Vector2.ClampMagnitude(lateral, 1f) * maxShadowShift;
            if (lensMaterial != null)
            {
                // The lens pass draws the parallax shadow, glass effects and reticle; the vignette stays off.
                scopeShadow.intensity.Override(0f);
                lensMaterial.SetVector(ShadowCenterId, shadowCenter);
                lensMaterial.SetFloat(ShadowRadiusId, Mathf.Lerp(1.05f, 0.2f, closing));
                lensMaterial.SetFloat(ShadowSoftnessId, scopeShadowSmoothness * 0.6f);
                lensMaterial.SetFloat(AberrationId, lensAberration);
                lensMaterial.SetFloat(DistortionId, lensDistortion);
                lensMaterial.SetFloat(ReticleStyleId, (float)sight.Reticle);
                lensMaterial.SetFloat(ReticleScaleId, sight.ReticleScale);
                lensMaterial.SetColor(GlowColorId, sight.ReticleGlow);
            }
            else
            {
                scopeShadow.intensity.Override(closing);
                scopeShadow.center.Override(shadowCenter);
            }

            scopeCamera.enabled = true;
        }

        private void DeactivateScope()
        {
            if (scopeCamera != null) scopeCamera.enabled = false;
            if (litLens != null) SetLensTexture(litLens, Texture2D.blackTexture);
            litLens = null;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera rendered)
        {
            if (rendered != scopeCamera || lensMaterial == null || lensTexture == null) return;
            CommandBuffer command = CommandBufferPool.Get("PiP Lens");
            command.Blit(scopeTexture, lensTexture, lensMaterial);
            context.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);
            context.Submit();
        }

        private void ReleaseForcedLaser()
        {
            if (laserForced && laser != null) laser.SetOn(false);
            laserForced = false;
        }

        private void SetLensTexture(Renderer lens, Texture texture)
        {
            lens.GetPropertyBlock(lensBlock);
            lensBlock.SetTexture(EmissiveColorMapId, texture); // the lens material emits it with exposure weight 1
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
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None; // the target is supersampled onto a small lens
            // Scene radiance out: the main camera exposes, grades, tonemaps and blooms the lens once, with the world.
            data.customRenderingSettings = true;
            foreach (FrameSettingsField field in RadianceOnlyFields)
            {
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
            }

            CreateScopeTexture();
        }

        /// <summary>Scope camera effects the main camera applies to the lens instead (the vignette stays: lens-less fallback).</summary>
        public static readonly FrameSettingsField[] RadianceOnlyFields =
        {
            FrameSettingsField.ExposureControl, FrameSettingsField.Tonemapping, FrameSettingsField.ColorGrading,
            FrameSettingsField.Bloom, FrameSettingsField.FilmGrain, FrameSettingsField.Dithering,
            FrameSettingsField.ChromaticAberration, FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField,
        };

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

            if (lensTexture != null)
            {
                lensTexture.Release();
                Destroy(lensTexture);
            }

            lensTexture = new RenderTexture(scopeResolution, scopeResolution, 0, GraphicsFormat.R16G16B16A16_SFloat)
            {
                name = "PiP Lens",
                useMipMap = false,
            };
            lensTexture.Create();
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

            impulseVolume = CreateGlobalVolume("Shot Impulse", GameLayers.Player, out impulseProfile); // main camera only
            ChromaticAberration pulse = impulseProfile.Add<ChromaticAberration>();
            pulse.intensity.Override(shotAberration);
            impulseVolume.weight = 0f;

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
