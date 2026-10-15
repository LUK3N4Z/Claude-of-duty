using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace FPS.UI
{
    /// <summary>
    /// Minimal realism HUD built in code: a compass bearing tape, interaction prompts, and fire-mode / magazine status
    /// that only fades in when the player switches modes or checks the magazine. No crosshair unless enabled. Heavy
    /// damage and suppression close in a vignette and drain colour.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class DiegeticTacticalHUD : MonoBehaviour
    {
        private const int HeadingStep = 15;
        private static readonly string[] Bearings = BuildBearings();
        private static readonly string[] HeadingNames = BuildHeadingNames();
        private static readonly string[] FireModeNames = { "SEMI", "BURST", "AUTO" };
        private static readonly string[] EstimateNames =
            { "MAG EMPTY", "MAG NEARLY EMPTY", "MAG LESS THAN HALF", "MAG ABOUT HALF", "MAG MORE THAN HALF", "MAG NEARLY FULL", "MAG FULL" };

        [Header("Compass")]
        [SerializeField] private float compassWidth = 520f;
        [SerializeField] private float pixelsPerDegree = 4f;

        [Header("Status")]
        [SerializeField] private float statusHold = 1.8f;
        [SerializeField] private float statusFade = 0.5f;

        [Header("Crosshair")]
        [SerializeField] private bool showCrosshair;

        [Header("Stress Effects")]
        [SerializeField, Range(0f, 1f)] private float maxVignette = 0.5f;
        [Tooltip("Health fraction below which the world drains of colour.")]
        [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.35f;
        [SerializeField] private float maxDesaturation = -70f;

        private readonly Text[] headingLabels = new Text[360 / HeadingStep];
        private RealisticLocomotionController locomotion;
        private RealisticWeaponController weapons;
        private Interactor interactor;
        private SuppressionFeedback suppression;
        private TacticalHealthAndArmorSystem health;
        private Text bearing, status, prompt;
        private CanvasGroup statusGroup;
        private Image crosshair;
        private Vignette vignette;
        private ColorAdjustments colorAdjustments;
        private VolumeProfile stressProfile;
        private int shownBearing = -1;
        private float statusUntil = float.NegativeInfinity;
        private IInteractable promptedFor;

        public string StatusText => status.text;
        public float StatusAlpha => statusGroup.alpha;
        public string BearingText => bearing.text;
        public string PromptText => prompt.text;
        public bool ShowCrosshair
        {
            get => showCrosshair;
            set
            {
                showCrosshair = value;
                crosshair.enabled = value;
            }
        }

        private void Awake()
        {
            locomotion = GetComponent<RealisticLocomotionController>();
            TryGetComponent(out weapons);
            TryGetComponent(out interactor);
            TryGetComponent(out suppression);
            TryGetComponent(out health);
            BuildCanvas();
            BuildStressVolume();
        }

        private void OnEnable() => EventBus<WeaponActionEvent>.Subscribe(OnWeaponAction);

        private void OnDisable() => EventBus<WeaponActionEvent>.Unsubscribe(OnWeaponAction);

        private void OnDestroy() => Destroy(stressProfile);

        private void Update()
        {
            UpdateCompass();
            UpdatePrompt();

            float fade = Mathf.Clamp01((statusUntil - Time.time) / statusFade);
            statusGroup.alpha = Time.time < statusUntil - statusFade ? 1f : fade;

            float lowHealth = health != null && lowHealthThreshold > 0f
                ? Mathf.Clamp01((lowHealthThreshold - health.Health / health.MaxHealth) / lowHealthThreshold)
                : 0f;
            float stress = Mathf.Max(suppression != null ? suppression.Level : 0f, lowHealth);
            vignette.intensity.Override(stress * maxVignette);
            colorAdjustments.saturation.Override(lowHealth * maxDesaturation);
        }

        private void OnWeaponAction(in WeaponActionEvent action)
        {
            if (action.Owner != transform) return;

            switch (action.Type)
            {
                case WeaponActionType.FireModeChanged when weapons != null && weapons.ActiveState != null:
                    ShowStatus(FireModeNames[(int)weapons.ActiveState.FireMode]);
                    break;
                case WeaponActionType.MagazineChecked:
                    ShowStatus(EstimateNames[Mathf.Clamp(action.Value, 0, EstimateNames.Length - 1)]);
                    break;
            }
        }

        private void ShowStatus(string text)
        {
            status.text = text;
            statusUntil = Time.time + statusHold + statusFade;
        }

        private void UpdateCompass()
        {
            float yaw = locomotion.Yaw;
            float half = compassWidth * 0.5f;
            for (int i = 0; i < headingLabels.Length; i++)
            {
                float x = Mathf.DeltaAngle(yaw, i * HeadingStep) * pixelsPerDegree;
                bool visible = Mathf.Abs(x) < half;
                Text label = headingLabels[i];
                if (label.enabled != visible) label.enabled = visible;
                if (visible) label.rectTransform.anchoredPosition = new Vector2(x, 0f);
            }

            int degrees = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)) % 360;
            if (degrees == shownBearing) return; // avoid rebuilding the text mesh every frame
            shownBearing = degrees;
            bearing.text = Bearings[degrees];
        }

        private void UpdatePrompt()
        {
            IInteractable focused = interactor != null ? interactor.Focused : null;
            if (focused == promptedFor) return;
            promptedFor = focused;
            prompt.text = focused != null ? "[F]  " + focused.Prompt : string.Empty; // composed only when the target changes
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("Tactical HUD");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = canvas.GetComponent<RectTransform>();

            RectTransform strip = Panel(root, "Compass", new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(compassWidth, 34f));
            strip.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < headingLabels.Length; i++)
            {
                bool cardinal = i * HeadingStep % 45 == 0;
                headingLabels[i] = Label(strip, HeadingNames[i], cardinal ? 22 : 15, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 30f));
            }

            Image tick = new GameObject("Centre Tick").AddComponent<Image>();
            tick.rectTransform.SetParent(root, false);
            Place(tick.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(2f, 10f));
            tick.color = new Color(1f, 1f, 1f, 0.8f);

            bearing = Label(root, "Bearing", 16, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(80f, 24f));
            prompt = Label(root, "Prompt", 22, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(600f, 40f));
            prompt.text = string.Empty;

            status = Label(root, "Weapon Status", 20, new Vector2(1f, 0f), new Vector2(-180f, 70f), new Vector2(320f, 30f));
            status.alignment = TextAnchor.MiddleRight;
            statusGroup = status.gameObject.AddComponent<CanvasGroup>();
            statusGroup.alpha = 0f;

            crosshair = new GameObject("Crosshair").AddComponent<Image>();
            crosshair.rectTransform.SetParent(root, false);
            Place(crosshair.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));
            crosshair.color = new Color(1f, 1f, 1f, 0.7f);
            crosshair.enabled = showCrosshair;
        }

        private void BuildStressVolume()
        {
            // Player layer: only the main camera sees it, never the PiP scope.
            var volumeObject = new GameObject("Stress Effects") { layer = GameLayers.Player };
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 80f;
            stressProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            stressProfile.name = "Stress Effects";
            volume.sharedProfile = stressProfile;
            vignette = stressProfile.Add<Vignette>();
            vignette.color.Override(Color.black);
            vignette.smoothness.Override(0.5f);
            vignette.intensity.Override(0f);
            colorAdjustments = stressProfile.Add<ColorAdjustments>();
            colorAdjustments.saturation.Override(0f);
        }

        private static RectTransform Panel(RectTransform parent, string panelName, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var panel = new GameObject(panelName, typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            Place(panel, anchor, position, size);
            return panel;
        }

        private static Text Label(RectTransform parent, string text, int fontSize, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var label = new GameObject(text).AddComponent<Text>();
            label.rectTransform.SetParent(parent, false);
            Place(label.rectTransform, anchor, position, size);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.raycastTarget = false;
            label.text = text;
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            return label;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static string[] BuildBearings()
        {
            var bearings = new string[360];
            for (int i = 0; i < 360; i++) bearings[i] = i.ToString("000");
            return bearings;
        }

        private static string[] BuildHeadingNames()
        {
            var names = new string[360 / HeadingStep];
            for (int i = 0; i < names.Length; i++)
            {
                int heading = i * HeadingStep;
                switch (heading)
                {
                    case 0: names[i] = "N"; break;
                    case 45: names[i] = "NE"; break;
                    case 90: names[i] = "E"; break;
                    case 135: names[i] = "SE"; break;
                    case 180: names[i] = "S"; break;
                    case 225: names[i] = "SW"; break;
                    case 270: names[i] = "W"; break;
                    case 315: names[i] = "NW"; break;
                    default: names[i] = heading.ToString(); break;
                }
            }

            return names;
        }
    }
}
