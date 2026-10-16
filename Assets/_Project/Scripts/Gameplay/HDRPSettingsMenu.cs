using System.Collections.Generic;
using FPS.Audio;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace FPS.Gameplay
{
    /// <summary>
    /// The options menu (Menu: Escape / Start): field of view, PiP scope resolution, motion blur, upscaling — HDRP TAA
    /// upscale everywhere, FSR2 and DLSS where the platform reports them — with the internal render scale, and the
    /// master-bus dynamic range (War Tapes, Studio Reference, Night Mode). Changes apply live and are saved with the
    /// profile. While open the player is paused; Menu at a workbench leaves the workbench instead.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class HDRPSettingsMenu : MonoBehaviour
    {
        private static readonly int[] ScopeResolutions = { 512, 1024, 2048 };
        private static readonly int[] RenderScales = { 50, 59, 67, 77, 87, 100 };
        private static readonly string[] AudioNames = { "War Tapes", "Studio Reference", "Night Mode" };

        [SerializeField, Min(1f)] private float fovStep = 5f;
        [SerializeField] private Vector2 fovRange = new Vector2(60f, 100f);

        private readonly List<Behaviour> paused = new List<Behaviour>(4);
        private readonly GameSettings unsavedSettings = new GameSettings();
        private readonly Text[] values = new Text[6];
        private PlayerRig rig;
        private InputReader input;
        private SaveProfileManager profiles;
        private ProceduralADSAndOpticsController ads;
        private HDAdditionalCameraData cameraData;
        private DynamicRangeProcessor dynamics;
        private VolumeProfile volumeProfile;
        private MotionBlur motionBlur;
        private GameObject panel;
        private PerformDynamicRes scaler;
        private float renderPercentage = 100f;

        public bool IsOpen { get; private set; }
        /// <summary>The live settings: the profile's when a <see cref="SaveProfileManager"/> is present.</summary>
        public GameSettings Settings => profiles != null ? profiles.Profile.settings : unsavedSettings;
        public float RenderPercentage => renderPercentage;

        public static bool IsAvailable(UpscalerMode mode)
        {
            switch (mode)
            {
                case UpscalerMode.DLSS: return HDDynamicResolutionPlatformCapabilities.DLSSDetected;
                case UpscalerMode.FSR2: return HDDynamicResolutionPlatformCapabilities.FSR2Detected;
                default: return true;
            }
        }

        public void Toggle()
        {
            GunsmithWorkbenchController station = GunsmithWorkbenchController.Active;
            if (station != null)
            {
                station.Leave(); // Menu backs out of a workbench first
                return;
            }

            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            PlayerControl.Pause(gameObject, paused);
            panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            panel.SetActive(false);
            PlayerControl.Resume(paused);
        }

        /// <summary>Applies <paramref name="settings"/> to camera, optics, post-processing and audio.</summary>
        public void Apply(GameSettings settings)
        {
            if (ads != null)
            {
                ads.BaseFieldOfView = settings.fieldOfView;
                ads.ScopeResolution = settings.scopeResolution;
            }
            else
            {
                rig.PlayerCamera.fieldOfView = settings.fieldOfView;
            }

            motionBlur.intensity.value = settings.motionBlur == MotionBlurLevel.Off ? 0f : settings.motionBlur == MotionBlurLevel.Low ? 0.35f : 1f;

            UpscalerMode upscaler = IsAvailable(settings.upscaler) ? settings.upscaler : UpscalerMode.TAAU;
            bool scaled = settings.upscaler != UpscalerMode.Native;
            renderPercentage = scaled ? Mathf.Clamp(settings.renderScale, 25, 100) : 100f;
            cameraData.allowDynamicResolution = scaled;
            cameraData.allowDeepLearningSuperSampling = scaled && upscaler == UpscalerMode.DLSS;
            cameraData.allowFidelityFX2SuperResolution = scaled && upscaler == UpscalerMode.FSR2;
            DynamicResolutionHandler.SetDynamicResScaler(scaler, DynamicResScalePolicyType.ReturnsPercentage);

            if (dynamics != null) dynamics.Preset = settings.audioRange;
            if (IsOpen) Refresh();
        }

        private void Awake()
        {
            rig = GetComponent<PlayerRig>();
            input = rig.Input;
            TryGetComponent(out profiles);
            TryGetComponent(out ads);
            Camera view = rig.PlayerCamera;
            if (!view.TryGetComponent(out cameraData)) cameraData = view.gameObject.AddComponent<HDAdditionalCameraData>();
            if (!view.TryGetComponent(out dynamics)) dynamics = view.gameObject.AddComponent<DynamicRangeProcessor>();
            scaler = () => renderPercentage;

            // Player layer: the main camera's volume mask sees it, the PiP scope camera does not.
            var volumeObject = new GameObject("Graphics Settings") { layer = GameLayers.Player };
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 60f;
            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumeProfile.name = "Graphics Settings";
            volume.sharedProfile = volumeProfile;
            motionBlur = volumeProfile.Add<MotionBlur>();
            motionBlur.intensity.Override(0.35f);

            UIFactory.EnsureEventSystem();
            Build();
        }

        private void Start() => Apply(Settings);

        private void OnEnable()
        {
            if (input == null) return;
            input.EnableSystemInput();
            input.MenuPressed += Toggle;
        }

        private void OnDisable()
        {
            if (input != null) input.MenuPressed -= Toggle;
            Close();
        }

        private void OnDestroy() => Destroy(volumeProfile);

        private void Build()
        {
            RectTransform root = UIFactory.Canvas(transform, "Settings Menu", 30, true);
            RectTransform body = UIFactory.Panel(root, "Options", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 560f), UIFactory.PanelColor);
            panel = root.gameObject;
            Text title = UIFactory.Label(body, 32, new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(700f, 50f), TextAnchor.MiddleCenter);
            title.text = "Options";

            Row(body, 0, "Field of view", () => Step(-1), () => Step(1));
            Row(body, 1, "Scope resolution", () => Cycle(ref Settings.scopeResolution, ScopeResolutions, -1), () => Cycle(ref Settings.scopeResolution, ScopeResolutions, 1));
            Row(body, 2, "Motion blur", () => Settings.motionBlur = (MotionBlurLevel)Wrap((int)Settings.motionBlur - 1, 3),
                () => Settings.motionBlur = (MotionBlurLevel)Wrap((int)Settings.motionBlur + 1, 3));
            Row(body, 3, "Upscaling", () => Settings.upscaler = (UpscalerMode)Wrap((int)Settings.upscaler - 1, 4),
                () => Settings.upscaler = (UpscalerMode)Wrap((int)Settings.upscaler + 1, 4));
            Row(body, 4, "Render scale", () => Cycle(ref Settings.renderScale, RenderScales, -1), () => Cycle(ref Settings.renderScale, RenderScales, 1));
            Row(body, 5, "Audio dynamic range", () => Settings.audioRange = (AudioDynamicRange)Wrap((int)Settings.audioRange - 1, 3),
                () => Settings.audioRange = (AudioDynamicRange)Wrap((int)Settings.audioRange + 1, 3));
            UIFactory.Button(body, "Resume", new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(220f, 46f), Close);
            panel.SetActive(false);
        }

        private void Row(RectTransform body, int index, string label, System.Action previous, System.Action next)
        {
            float y = -120f - index * 62f;
            Text name = UIFactory.Label(body, 22, new Vector2(0f, 1f), new Vector2(190f, y), new Vector2(320f, 40f), TextAnchor.MiddleLeft);
            name.text = label;
            UIFactory.Button(body, "<", new Vector2(1f, 1f), new Vector2(-330f, y), new Vector2(46f, 40f), () => Change(previous));
            values[index] = UIFactory.Label(body, 22, new Vector2(1f, 1f), new Vector2(-190f, y), new Vector2(230f, 40f), TextAnchor.MiddleCenter);
            UIFactory.Button(body, ">", new Vector2(1f, 1f), new Vector2(-50f, y), new Vector2(46f, 40f), () => Change(next));
        }

        private void Change(System.Action edit)
        {
            edit();
            Apply(Settings);
            if (profiles != null) profiles.Save();
        }

        private void Step(int direction) =>
            Settings.fieldOfView = Mathf.Clamp(Mathf.Round(Settings.fieldOfView / fovStep + direction) * fovStep, fovRange.x, fovRange.y);

        private static void Cycle(ref int value, int[] options, int direction)
        {
            int index = System.Array.IndexOf(options, value);
            value = options[Wrap((index < 0 ? 0 : index) + direction, options.Length)];
        }

        private static int Wrap(int value, int count) => (value % count + count) % count;

        private void Refresh()
        {
            GameSettings settings = Settings;
            values[0].text = $"{settings.fieldOfView:0}°";
            values[1].text = $"{settings.scopeResolution} px";
            values[2].text = settings.motionBlur.ToString();
            values[3].text = IsAvailable(settings.upscaler) ? settings.upscaler.ToString() : $"{settings.upscaler} (unavailable: TAAU)";
            values[4].text = settings.upscaler == UpscalerMode.Native ? "100% (native)" : $"{settings.renderScale}%";
            values[5].text = AudioNames[(int)settings.audioRange];
        }
    }
}
