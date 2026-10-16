using System;
using System.Collections.Generic;
using FPS.AI;
using FPS.Audio;
using FPS.Core;
using FPS.Locomotion;
using FPS.UI;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPS.Gameplay
{
    /// <summary>
    /// The options menu (Menu: Escape / Start), in four tabs. Video: field of view, PiP scope resolution, motion blur,
    /// upscaling (TAAU everywhere, FSR2/DLSS where the platform reports them) and render scale. Audio: master, effects and
    /// voice volume, master-bus dynamic range. Controls: look and ADS sensitivity and key rebinding (press the key; Escape
    /// cancels; keys shared by tap/hold pairs move together). Gameplay: night-vision phosphor, AI difficulty and hit
    /// markers (off, ticks, markers or both). Changes
    /// apply live and are saved with the profile. Return to Armory and Quit to Desktop leave the operation. While open the
    /// player is paused; Menu at a workbench or the deployment table leaves the station instead.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class HDRPSettingsMenu : MonoBehaviour
    {
        private static readonly int[] ScopeResolutions = { 512, 1024, 2048 };
        private static readonly int[] RenderScales = { 50, 59, 67, 77, 87, 100 };
        private static readonly string[] AudioNames = { "War Tapes", "Studio Reference", "Night Mode" };
        private static readonly string[] TabNames = { "Video", "Audio", "Controls", "Gameplay" };
        private static readonly string[] HitMarkerNames = { "Off", "Ticks only", "Markers only", "Ticks and markers" };
        private static readonly BindingRow[] Bindings =
        {
            new BindingRow("Move forward", "Move", "up"), new BindingRow("Move back", "Move", "down"),
            new BindingRow("Move left", "Move", "left"), new BindingRow("Move right", "Move", "right"),
            new BindingRow("Jump / climb", "Jump"), new BindingRow("Sprint", "Sprint"),
            new BindingRow("Crouch / prone", "Crouch"), new BindingRow("Walk slowly", "WalkSlow"),
            new BindingRow("Lean left", "LeanLeft"), new BindingRow("Lean right", "LeanRight"),
            new BindingRow("Reload", "Reload"), new BindingRow("Interact", "Interact"),
            new BindingRow("Melee / mount", "MountWeapon"), new BindingRow("Fire mode", "ChangeFireMode"),
            new BindingRow("Switch sight", "SwitchSight"), new BindingRow("Inspect", "Inspect"),
            new BindingRow("Weapon light", "ToggleDevice"), new BindingRow("Night vision", "ToggleNightVision"),
            new BindingRow("Lethal grenade", "ThrowLethal"), new BindingRow("Tactical grenade", "ThrowTactical"),
            new BindingRow("Primary weapon", "SelectPrimary"), new BindingRow("Secondary weapon", "SelectSecondary"),
        };

        [SerializeField, Min(1f)] private float fovStep = 5f;
        [SerializeField] private Vector2 fovRange = new Vector2(60f, 100f);
        [SerializeField] private Vector2 sensitivityRange = new Vector2(0.2f, 3f);
        [SerializeField] private Vector2 adsSensitivityRange = new Vector2(0.5f, 1.5f);

        private readonly List<Behaviour> paused = new List<Behaviour>(4);
        private readonly GameSettings unsavedSettings = new GameSettings();
        private readonly List<Action> refreshers = new List<Action>(32);
        private readonly GameObject[] pages = new GameObject[4];
        private readonly Button[] tabs = new Button[4];
        private readonly Button[] bindingButtons = new Button[Bindings.Length];
        private PlayerRig rig;
        private InputReader input;
        private SaveProfileManager profiles;
        private ProceduralADSAndOpticsController ads;
        private RealisticLocomotionController locomotion;
        private NightVisionGoggles goggles;
        private HitMarkerFeedback hitMarkers;
        private DamageDirectionIndicator damageIndicator;
        private HDAdditionalCameraData cameraData;
        private DynamicRangeProcessor dynamics;
        private VolumeProfile volumeProfile;
        private MotionBlur motionBlur;
        private GameObject panel;
        private Button returnButton;
        private PerformDynamicRes scaler;
        private float renderPercentage = 100f;
        private string appliedBindings;
        private int rebindEndedFrame = -10;

        public bool IsOpen { get; private set; }
        /// <summary>The live settings: the profile's when a <see cref="SaveProfileManager"/> is present.</summary>
        public GameSettings Settings => profiles != null ? profiles.Profile.settings : unsavedSettings;
        public float RenderPercentage => renderPercentage;
        public int Tab { get; private set; }
        public int BindingCount => Bindings.Length;

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
            if (input != null && input.IsRebinding || Time.frameCount - rebindEndedFrame <= 1) return; // Escape cancels the rebind
            if (GunsmithWorkbenchController.Active != null)
            {
                GunsmithWorkbenchController.Active.Leave(); // Menu backs out of a station first
                return;
            }

            if (DeploymentStation.Active != null)
            {
                DeploymentStation.Active.Leave();
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
            returnButton.gameObject.SetActive(SceneManager.GetActiveScene().name != SceneFlow.ArmoryScene);
            ShowTab(Tab);
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (input != null) input.CancelRebind();
            Persist(); // slider moves are saved once, on the way out
            IsOpen = false;
            panel.SetActive(false);
            PlayerControl.Resume(paused);
        }

        public void ShowTab(int tab)
        {
            Tab = Mathf.Clamp(tab, 0, pages.Length - 1);
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].SetActive(i == Tab);
                UIFactory.SetSelected(tabs[i], i == Tab);
            }

            Refresh();
        }

        /// <summary>Waits for a key for binding row <paramref name="row"/> (see <see cref="BindingCount"/>) and saves it.</summary>
        public void Rebind(int row)
        {
            if (input == null || input.IsRebinding) return;
            BindingRow binding = Bindings[row];
            int index = input.KeyboardBinding(binding.Action, binding.Part);
            if (index < 0) return;
            UIFactory.SetText(bindingButtons[row], "Press a key  (Esc cancels)");
            input.Rebind(binding.Action, index, bound =>
            {
                rebindEndedFrame = Time.frameCount;
                if (bound) SaveBindings();
                Refresh();
            });
        }

        /// <summary>The key a binding row is on now.</summary>
        public string BindingKey(int row) => input != null ? input.BindingName(Bindings[row].Action, input.KeyboardBinding(Bindings[row].Action, Bindings[row].Part)) : "-";

        public void ResetBindings()
        {
            if (input == null) return;
            input.CancelRebind();
            input.LoadBindingOverrides(string.Empty);
            SaveBindings();
            Refresh();
        }

        /// <summary>Leaves the operation for the hub (behind the loading screen).</summary>
        public void ReturnToArmory()
        {
            Close();
            SceneFlow.ReturnToArmory();
        }

        public void QuitToDesktop() => SceneFlow.Quit();

        /// <summary>Applies <paramref name="settings"/> to camera, optics, post-processing, look, audio, goggles, AI and keys.</summary>
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
            AudioLevels.Master = settings.masterVolume;
            AudioLevels.Effects = settings.effectsVolume;
            AudioLevels.Voice = settings.voiceVolume;
            AudioListener.volume = AudioLevels.ListenerVolume;

            if (locomotion != null)
            {
                locomotion.SensitivityScale = settings.lookSensitivity;
                locomotion.AdsSensitivityScale = settings.adsSensitivity;
            }

            if (goggles != null) goggles.Phosphor = settings.nightVisionPhosphor;
            if (hitMarkers != null) hitMarkers.Mode = settings.hitMarkers;
            if (damageIndicator != null) damageIndicator.Shown = settings.damageIndicator;
            CombatDifficulty.Current = settings.aiDifficulty;
            if (input != null && settings.bindingOverrides != appliedBindings)
            {
                input.LoadBindingOverrides(settings.bindingOverrides);
                appliedBindings = settings.bindingOverrides;
            }

            if (IsOpen) Refresh();
        }

        private void Awake()
        {
            rig = GetComponent<PlayerRig>();
            input = rig.Input;
            TryGetComponent(out profiles);
            TryGetComponent(out ads);
            TryGetComponent(out locomotion);
            TryGetComponent(out goggles);
            TryGetComponent(out hitMarkers);
            TryGetComponent(out damageIndicator);
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

        // ---- Layout ----

        private void Build()
        {
            RectTransform root = UIFactory.Canvas(transform, "Settings Menu", 30, true);
            RectTransform body = UIFactory.Panel(root, "Options", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 820f), UIFactory.PanelColor);
            panel = root.gameObject;
            Text title = UIFactory.Label(body, 32, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(700f, 50f), TextAnchor.MiddleCenter);
            title.text = "Options";

            for (int i = 0; i < TabNames.Length; i++)
            {
                int tab = i;
                tabs[i] = UIFactory.Button(body, TabNames[i], new Vector2(0.5f, 1f), new Vector2(-330f + i * 220f, -100f), new Vector2(200f, 44f), () => ShowTab(tab));
                pages[i] = new GameObject(TabNames[i], typeof(RectTransform));
                var page = pages[i].GetComponent<RectTransform>();
                page.SetParent(body, false);
                page.anchorMin = Vector2.zero;
                page.anchorMax = Vector2.one;
                page.offsetMin = new Vector2(30f, 90f);
                page.offsetMax = new Vector2(-30f, -140f);
            }

            RectTransform video = Page(0);
            Stepper(video, 0, "Field of view", () => Step(-1), () => Step(1), () => $"{Settings.fieldOfView:0}°");
            Stepper(video, 1, "Scope resolution", () => Cycle(ref Settings.scopeResolution, ScopeResolutions, -1),
                () => Cycle(ref Settings.scopeResolution, ScopeResolutions, 1), () => $"{Settings.scopeResolution} px");
            Stepper(video, 2, "Motion blur", () => Settings.motionBlur = (MotionBlurLevel)Wrap((int)Settings.motionBlur - 1, 3),
                () => Settings.motionBlur = (MotionBlurLevel)Wrap((int)Settings.motionBlur + 1, 3), () => Settings.motionBlur.ToString());
            Stepper(video, 3, "Upscaling", () => Settings.upscaler = (UpscalerMode)Wrap((int)Settings.upscaler - 1, 4),
                () => Settings.upscaler = (UpscalerMode)Wrap((int)Settings.upscaler + 1, 4),
                () => IsAvailable(Settings.upscaler) ? Settings.upscaler.ToString() : $"{Settings.upscaler} (unavailable: TAAU)");
            Stepper(video, 4, "Render scale", () => Cycle(ref Settings.renderScale, RenderScales, -1), () => Cycle(ref Settings.renderScale, RenderScales, 1),
                () => Settings.upscaler == UpscalerMode.Native ? "100% (native)" : $"{Settings.renderScale}%");

            RectTransform audioPage = Page(1);
            Fader(audioPage, 0, "Master volume", 0f, 1f, () => Settings.masterVolume, v => Settings.masterVolume = v, v => $"{v * 100f:0}%");
            Fader(audioPage, 1, "Effects volume", 0f, 1f, () => Settings.effectsVolume, v => Settings.effectsVolume = v, v => $"{v * 100f:0}%");
            Fader(audioPage, 2, "Voice volume", 0f, 1f, () => Settings.voiceVolume, v => Settings.voiceVolume = v, v => $"{v * 100f:0}%");
            Stepper(audioPage, 3, "Dynamic range", () => Settings.audioRange = (AudioDynamicRange)Wrap((int)Settings.audioRange - 1, 3),
                () => Settings.audioRange = (AudioDynamicRange)Wrap((int)Settings.audioRange + 1, 3), () => AudioNames[(int)Settings.audioRange]);

            RectTransform controls = Page(2);
            Fader(controls, 0, "Look sensitivity", sensitivityRange.x, sensitivityRange.y, () => Settings.lookSensitivity, v => Settings.lookSensitivity = v, v => $"{v:0.00}x");
            Fader(controls, 1, "ADS sensitivity", adsSensitivityRange.x, adsSensitivityRange.y, () => Settings.adsSensitivity, v => Settings.adsSensitivity = v, v => $"{v:0.00}x");
            int perColumn = (Bindings.Length + 1) / 2;
            for (int i = 0; i < Bindings.Length; i++)
            {
                int row = i;
                float x = i < perColumn ? 0f : 560f;
                float y = -150f - (i % perColumn) * 36f;
                Text name = UIFactory.Label(controls, 18, new Vector2(0f, 1f), new Vector2(x + 130f, y), new Vector2(240f, 34f), TextAnchor.MiddleLeft);
                name.text = Bindings[i].Label;
                bindingButtons[i] = UIFactory.Button(controls, "-", new Vector2(0f, 1f), new Vector2(x + 390f, y), new Vector2(240f, 34f), () => Rebind(row));
                refreshers.Add(() => UIFactory.SetText(bindingButtons[row], BindingKey(row)));
            }

            UIFactory.Button(controls, "Reset keys", new Vector2(1f, 0f), new Vector2(-110f, 10f), new Vector2(180f, 40f), ResetBindings);

            RectTransform gameplay = Page(3);
            Stepper(gameplay, 0, "Night vision phosphor", () => Settings.nightVisionPhosphor = (PhosphorColor)Wrap((int)Settings.nightVisionPhosphor - 1, 2),
                () => Settings.nightVisionPhosphor = (PhosphorColor)Wrap((int)Settings.nightVisionPhosphor + 1, 2), () => Settings.nightVisionPhosphor.ToString());
            Stepper(gameplay, 1, "AI difficulty", () => Settings.aiDifficulty = (AIDifficulty)Wrap((int)Settings.aiDifficulty - 1, 4),
                () => Settings.aiDifficulty = (AIDifficulty)Wrap((int)Settings.aiDifficulty + 1, 4), () => Settings.aiDifficulty.ToString());
            Stepper(gameplay, 2, "Hit markers", () => Settings.hitMarkers = (HitMarkerMode)Wrap((int)Settings.hitMarkers - 1, 4),
                () => Settings.hitMarkers = (HitMarkerMode)Wrap((int)Settings.hitMarkers + 1, 4), () => HitMarkerNames[(int)Settings.hitMarkers]);
            Stepper(gameplay, 3, "Damage direction", () => Settings.damageIndicator = !Settings.damageIndicator,
                () => Settings.damageIndicator = !Settings.damageIndicator, () => Settings.damageIndicator ? "On" : "Off");

            UIFactory.Button(body, "Resume", new Vector2(0.5f, 0f), new Vector2(-300f, 45f), new Vector2(240f, 46f), Close);
            returnButton = UIFactory.Button(body, "Return to Armory", new Vector2(0.5f, 0f), new Vector2(0f, 45f), new Vector2(240f, 46f), ReturnToArmory);
            UIFactory.Button(body, "Quit to Desktop", new Vector2(0.5f, 0f), new Vector2(300f, 45f), new Vector2(240f, 46f), QuitToDesktop);
            panel.SetActive(false);
        }

        private RectTransform Page(int index) => pages[index].GetComponent<RectTransform>();

        private void Stepper(RectTransform page, int index, string label, Action previous, Action next, Func<string> describe)
        {
            float y = -40f - index * 62f;
            Text name = UIFactory.Label(page, 22, new Vector2(0f, 1f), new Vector2(200f, y), new Vector2(360f, 40f), TextAnchor.MiddleLeft);
            name.text = label;
            UIFactory.Button(page, "<", new Vector2(1f, 1f), new Vector2(-380f, y), new Vector2(46f, 40f), () => Change(previous));
            Text value = UIFactory.Label(page, 22, new Vector2(1f, 1f), new Vector2(-220f, y), new Vector2(270f, 40f), TextAnchor.MiddleCenter);
            UIFactory.Button(page, ">", new Vector2(1f, 1f), new Vector2(-60f, y), new Vector2(46f, 40f), () => Change(next));
            refreshers.Add(() => value.text = describe());
        }

        private void Fader(RectTransform page, int index, string label, float min, float max, Func<float> read, Action<float> write, Func<float, string> describe)
        {
            float y = -40f - index * 62f;
            Text name = UIFactory.Label(page, 22, new Vector2(0f, 1f), new Vector2(200f, y), new Vector2(360f, 40f), TextAnchor.MiddleLeft);
            name.text = label;
            Text value = UIFactory.Label(page, 22, new Vector2(1f, 1f), new Vector2(-60f, y), new Vector2(110f, 40f), TextAnchor.MiddleCenter);
            Slider slider = UIFactory.Slider(page, new Vector2(1f, 1f), new Vector2(-300f, y), new Vector2(340f, 30f), min, max, v =>
            {
                write(v);
                value.text = describe(v);
                Apply(Settings);
            });
            refreshers.Add(() =>
            {
                slider.SetValueWithoutNotify(read());
                value.text = describe(read());
            });
        }

        private void Change(Action edit)
        {
            edit();
            Apply(Settings);
            Persist();
            Refresh();
        }

        private void Persist()
        {
            if (profiles != null) profiles.Save();
        }

        private void SaveBindings()
        {
            Settings.bindingOverrides = input.SaveBindingOverrides();
            appliedBindings = Settings.bindingOverrides;
            Persist();
        }

        private void Step(int direction) =>
            Settings.fieldOfView = Mathf.Clamp(Mathf.Round(Settings.fieldOfView / fovStep + direction) * fovStep, fovRange.x, fovRange.y);

        private static void Cycle(ref int value, int[] options, int direction)
        {
            int index = Array.IndexOf(options, value);
            value = options[Wrap((index < 0 ? 0 : index) + direction, options.Length)];
        }

        private static int Wrap(int value, int count) => (value % count + count) % count;

        private void Refresh()
        {
            for (int i = 0; i < refreshers.Count; i++) refreshers[i]();
        }

        private readonly struct BindingRow
        {
            public readonly string Label;
            public readonly string Action;
            public readonly string Part;

            public BindingRow(string label, string action, string part = null)
            {
                Label = label;
                Action = action;
                Part = part;
            }
        }
    }
}
