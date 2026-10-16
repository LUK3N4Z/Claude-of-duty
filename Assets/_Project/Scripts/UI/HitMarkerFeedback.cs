using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;
using UnityEngine.UI;

namespace FPS.UI
{
    public enum HitMarkerKind
    {
        None,
        /// <summary>Into the body: a soft tick, white marker.</summary>
        Flesh,
        /// <summary>Stopped by a plate or helmet: a ceramic crack, pale blue marker.</summary>
        Armor,
        /// <summary>The hit that downed him: a deep double tick, red marker.</summary>
        Kill,
        /// <summary>A fatal head shot: the kill with a bright ring on top, larger red marker.</summary>
        Headshot,
    }

    public enum HitMarkerMode
    {
        Off,
        AudioOnly,
        VisualOnly,
        AudioAndVisual,
    }

    /// <summary>
    /// MW-style hit confirmation for the player's own hits (any <see cref="DamageEvent"/> credited to this player): a
    /// small X round the screen centre that pops and fades, coloured by what the round did (flesh, armor stopped it,
    /// kill, head-shot kill), and a subtle tick per kind. Off, audio only, visual only or both
    /// (<see cref="Mode"/>, from the settings). Builds its own overlay; lives on the player root.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class HitMarkerFeedback : MonoBehaviour
    {
        private static readonly Color FleshColor = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color ArmorColor = new Color(0.6f, 0.82f, 1f, 0.95f);
        private static readonly Color KillColor = new Color(1f, 0.22f, 0.18f, 1f);

        [SerializeField] private HitMarkerMode mode = HitMarkerMode.AudioAndVisual;
        [SerializeField] private AudioClip fleshTick;
        [SerializeField] private AudioClip armorCrack;
        [SerializeField] private AudioClip killTick;
        [SerializeField] private AudioClip headshotTick;
        [SerializeField, Range(0f, 1f)] private float volume = 0.55f;
        [Tooltip("How long a marker stays up (s); kills hold longer.")]
        [SerializeField] private float duration = 0.22f;
        [SerializeField] private float killDuration = 0.45f;
        [Tooltip("Gap from the screen centre to each stroke and the stroke length (reference pixels).")]
        [SerializeField] private Vector2 strokeLayout = new Vector2(9f, 12f);

        private readonly Image[] strokes = new Image[4];
        private AudioSource source;
        private RectTransform marker;
        private CanvasGroup group;
        private float shownAt = float.NegativeInfinity, showFor, baseScale = 1f;

        public HitMarkerMode Mode
        {
            get => mode;
            set => mode = value;
        }

        /// <summary>The last confirmed hit (None until one lands).</summary>
        public HitMarkerKind LastKind { get; private set; }
        public int Confirmations { get; private set; }
        /// <summary>The marker's current opacity, 0-1.</summary>
        public float MarkerAlpha => group != null ? group.alpha : 0f;
        /// <summary>The tick played for the last hit, or null.</summary>
        public AudioClip LastClip { get; private set; }

        /// <summary>What a hit did: a kill (head-shot kill when the head took it), stopped by armor, or into flesh.</summary>
        public static HitMarkerKind Classify(in DamageEvent hit)
        {
            if (hit.Fatal) return hit.Zone == HitZone.Head ? HitMarkerKind.Headshot : HitMarkerKind.Kill;
            return hit.Armor != null && !hit.ArmorPenetrated ? HitMarkerKind.Armor : HitMarkerKind.Flesh;
        }

        /// <summary>Builder setup: the four ticks.</summary>
        public void Configure(AudioClip flesh, AudioClip armor, AudioClip kill, AudioClip headshot)
        {
            fleshTick = flesh;
            armorCrack = armor;
            killTick = kill;
            headshotTick = headshot;
        }

        private void Awake()
        {
            // Its own object under the camera: the listener's object runs a master-bus filter and must not host sources.
            Camera view = GetComponent<PlayerRig>().PlayerCamera;
            var speaker = new GameObject("Hit Marker Ticks");
            speaker.transform.SetParent(view != null ? view.transform : transform, false);
            source = speaker.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // feedback, not a sound in the world
            source.dopplerLevel = 0f;
            Build();
        }

        private void OnEnable() => EventBus<DamageEvent>.Subscribe(OnDamage);

        private void OnDisable()
        {
            EventBus<DamageEvent>.Unsubscribe(OnDamage);
            group.alpha = 0f;
        }

        private void Update()
        {
            float age = Time.time - shownAt;
            if (age > showFor)
            {
                if (group.alpha > 0f) group.alpha = 0f;
                return;
            }

            // Pops in a little large, settles, then fades over the last half.
            float t = age / showFor;
            marker.localScale = Vector3.one * (baseScale * Mathf.Lerp(1.3f, 1f, Mathf.Clamp01(age / 0.06f)));
            group.alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
        }

        private void OnDamage(in DamageEvent hit)
        {
            if (hit.Attacker != transform || mode == HitMarkerMode.Off) return;
            if (hit.Target != null && hit.Target.transform.IsChildOf(transform)) return; // own grenade on yourself

            HitMarkerKind kind = Classify(hit);
            LastKind = kind;
            Confirmations++;
            LastClip = null;

            if (mode != HitMarkerMode.VisualOnly)
            {
                AudioClip clip = Tick(kind);
                if (clip != null)
                {
                    source.PlayOneShot(clip, volume);
                    LastClip = clip;
                }
            }

            if (mode == HitMarkerMode.AudioOnly) return;
            Color color = kind == HitMarkerKind.Flesh ? FleshColor : kind == HitMarkerKind.Armor ? ArmorColor : KillColor;
            for (int i = 0; i < strokes.Length; i++) strokes[i].color = color;
            baseScale = kind == HitMarkerKind.Headshot ? 1.35f : 1f;
            marker.localScale = Vector3.one * (baseScale * 1.3f);
            showFor = kind == HitMarkerKind.Kill || kind == HitMarkerKind.Headshot ? killDuration : duration;
            shownAt = Time.time;
            group.alpha = 1f;
        }

        private AudioClip Tick(HitMarkerKind kind)
        {
            switch (kind)
            {
                case HitMarkerKind.Armor: return armorCrack;
                case HitMarkerKind.Kill: return killTick;
                case HitMarkerKind.Headshot: return headshotTick != null ? headshotTick : killTick;
                default: return fleshTick;
            }
        }

        private void Build()
        {
            var canvasObject = new GameObject("Hit Markers");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            marker = new GameObject("Marker", typeof(RectTransform)).GetComponent<RectTransform>();
            marker.SetParent(canvasObject.transform, false);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            group = marker.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Four diagonal strokes leaving a gap at the centre: the classic X.
            for (int i = 0; i < strokes.Length; i++)
            {
                float angle = 45f + 90f * i;
                Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                var stroke = new GameObject($"Stroke {i}").AddComponent<Image>();
                stroke.rectTransform.SetParent(marker, false);
                stroke.rectTransform.sizeDelta = new Vector2(strokeLayout.y, 2.5f);
                stroke.rectTransform.anchoredPosition = direction * (strokeLayout.x + strokeLayout.y * 0.5f);
                stroke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                stroke.raycastTarget = false;
                strokes[i] = stroke;
            }
        }
    }
}
