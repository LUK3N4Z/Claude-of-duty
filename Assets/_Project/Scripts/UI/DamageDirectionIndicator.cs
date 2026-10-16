using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;
using UnityEngine.UI;

namespace FPS.UI
{
    /// <summary>
    /// Where incoming damage came from: a thin dark-red arc round the screen centre for every hit the player takes,
    /// turned toward <see cref="DamageEvent.Source"/> (the shooter's muzzle, a grenade's blast centre) and kept on it as
    /// the player turns, holding briefly then fading out over <see cref="Duration"/>. One arc per attacker (four at
    /// most); a new hit from the same attacker refreshes his. Builds its own overlay; lives on the player root.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class DamageDirectionIndicator : MonoBehaviour
    {
        public const float Duration = 1.5f;
        private const float Hold = 0.2f;
        private const int ArcCount = 4;
        private const float TexelsPerPixel = 2f;

        [SerializeField] private bool shown = true;
        [SerializeField] private Color color = new Color(0.72f, 0.09f, 0.05f, 0.85f);
        [Tooltip("Arc radius from the screen centre and band thickness (reference pixels).")]
        [SerializeField] private Vector2 arcLayout = new Vector2(200f, 6f);
        [Tooltip("Angular width of an arc (deg).")]
        [SerializeField, Range(10f, 90f)] private float arcDegrees = 44f;
        [Tooltip("Damage that shows at full strength; lighter hits fade toward half.")]
        [SerializeField, Min(1f)] private float fullStrengthDamage = 40f;

        private readonly RectTransform[] arcs = new RectTransform[ArcCount];
        private readonly Image[] images = new Image[ArcCount];
        private readonly Vector3[] sources = new Vector3[ArcCount];
        private readonly Transform[] attackers = new Transform[ArcCount];
        private readonly float[] hitTimes = new float[ArcCount];
        private readonly float[] strengths = new float[ArcCount];
        private TacticalHealthAndArmorSystem health;
        private Transform view;
        private Texture2D arcTexture;
        private Sprite arcSprite;
        private int latest;

        /// <summary>Off hides the arcs and ignores hits (settings).</summary>
        public bool Shown
        {
            get => shown;
            set
            {
                shown = value;
                if (!value) Clear();
            }
        }

        /// <summary>Arcs currently on screen.</summary>
        public int VisibleCount
        {
            get
            {
                int visible = 0;
                for (int i = 0; i < ArcCount; i++) visible += images[i] != null && images[i].enabled ? 1 : 0;
                return visible;
            }
        }

        /// <summary>Bearing of the most recent hit's source from the view (deg, + clockwise / right, 0 ahead).</summary>
        public float LastBearing { get; private set; }
        /// <summary>The most recent hit's arc opacity, 0-1.</summary>
        public float LastAlpha { get; private set; }

        /// <summary>
        /// Horizontal bearing of <paramref name="source"/> seen from <paramref name="eye"/> looking along
        /// <paramref name="forward"/>: degrees in (-180, 180], positive to the right, 0 ahead (and for a source overhead).
        /// </summary>
        public static float Bearing(Vector3 eye, Vector3 forward, Vector3 source)
        {
            Vector3 toSource = source - eye;
            toSource.y = 0f;
            forward.y = 0f;
            if (toSource.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(forward, toSource, Vector3.up);
        }

        /// <summary>Opacity <paramref name="age"/> seconds after a hit: full for a moment, then a smooth fade to 0 at <see cref="Duration"/>.</summary>
        public static float Fade(float age)
        {
            float left = Mathf.Clamp01((Duration - age) / (Duration - Hold));
            return left * left * (3f - 2f * left);
        }

        private void Awake()
        {
            health = GetComponent<TacticalHealthAndArmorSystem>();
            Camera camera = GetComponent<PlayerRig>().PlayerCamera;
            view = camera != null ? camera.transform : transform;
            for (int i = 0; i < ArcCount; i++) hitTimes[i] = float.NegativeInfinity;
            Build();
        }

        private void OnEnable() => EventBus<DamageEvent>.Subscribe(OnDamage);

        private void OnDisable()
        {
            EventBus<DamageEvent>.Unsubscribe(OnDamage);
            Clear();
        }

        private void OnDestroy()
        {
            Destroy(arcSprite);
            Destroy(arcTexture);
        }

        private void LateUpdate() => Refresh();

        private void OnDamage(in DamageEvent hit)
        {
            if (!shown || health == null || hit.Target != health) return;

            // The attacker's arc if he has one up, else the oldest.
            int slot = 0;
            for (int i = 0; i < ArcCount; i++)
            {
                if (hit.Attacker != null && attackers[i] == hit.Attacker && images[i].enabled)
                {
                    slot = i;
                    break;
                }

                if (hitTimes[i] < hitTimes[slot]) slot = i;
            }

            sources[slot] = hit.Source;
            attackers[slot] = hit.Attacker;
            hitTimes[slot] = Time.time;
            strengths[slot] = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(hit.Damage / fullStrengthDamage));
            images[slot].enabled = true;
            latest = slot;
            Refresh(); // on screen the frame it lands
        }

        /// <summary>Turns every arc up toward its source and fades it; arcs that have faded out are freed.</summary>
        private void Refresh()
        {
            float now = Time.time;
            Vector3 eye = view.position;
            Vector3 forward = transform.forward; // body yaw: stays level when the view pitches or rolls
            LastAlpha = 0f;
            for (int i = 0; i < ArcCount; i++)
            {
                if (!images[i].enabled) continue;
                float alpha = Fade(now - hitTimes[i]) * strengths[i];
                if (alpha <= 0f)
                {
                    images[i].enabled = false;
                    attackers[i] = null;
                    continue;
                }

                float bearing = Bearing(eye, forward, sources[i]);
                arcs[i].localRotation = Quaternion.Euler(0f, 0f, -bearing); // the art points up (ahead); UI turns counter-clockwise
                Color tint = color;
                tint.a *= alpha;
                images[i].color = tint;
                if (i != latest) continue;
                LastBearing = bearing;
                LastAlpha = alpha;
            }
        }

        private void Clear()
        {
            for (int i = 0; i < ArcCount; i++)
            {
                if (images[i] != null) images[i].enabled = false;
                attackers[i] = null;
                hitTimes[i] = float.NegativeInfinity;
            }

            LastAlpha = 0f;
        }

        private void Build()
        {
            var canvasObject = new GameObject("Damage Direction");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 14; // under the hit markers
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // The arc's band, cropped from its circle: the rect pivots on the circle centre (the screen centre).
            float radius = arcLayout.x, thickness = arcLayout.y;
            float half = arcDegrees * 0.5f * Mathf.Deg2Rad;
            float width = 2f * (radius + thickness) * Mathf.Sin(half) + 2f;
            float bottom = (radius - thickness) * Mathf.Cos(half) - 1f;
            float height = radius + thickness + 1f - bottom;
            arcTexture = ArcTexture(width, height, bottom, radius, thickness, arcDegrees * 0.5f);
            arcSprite = Sprite.Create(arcTexture, new Rect(0f, 0f, arcTexture.width, arcTexture.height), new Vector2(0.5f, 0.5f), 100f);

            for (int i = 0; i < ArcCount; i++)
            {
                var image = new GameObject($"Arc {i}").AddComponent<Image>();
                image.sprite = arcSprite;
                image.raycastTarget = false;
                image.enabled = false;
                RectTransform arc = image.rectTransform;
                arc.SetParent(canvasObject.transform, false);
                arc.anchorMin = arc.anchorMax = new Vector2(0.5f, 0.5f);
                arc.sizeDelta = new Vector2(width, height);
                arc.pivot = new Vector2(0.5f, -bottom / height);
                arc.anchoredPosition = Vector2.zero;
                arcs[i] = arc;
                images[i] = image;
            }
        }

        /// <summary>White band of the circle (radius, thickness in reference pixels) feathered at its edges and ends.</summary>
        private static Texture2D ArcTexture(float width, float height, float bottom, float radius, float thickness, float halfDegrees)
        {
            int columns = Mathf.CeilToInt(width * TexelsPerPixel), rows = Mathf.CeilToInt(height * TexelsPerPixel);
            var texture = new Texture2D(columns, rows, TextureFormat.RGBA32, false)
            {
                name = "Damage Arc",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[columns * rows];
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    float px = (x + 0.5f) / TexelsPerPixel - width * 0.5f;
                    float py = bottom + (y + 0.5f) / TexelsPerPixel;
                    float across = Smooth(1f - Mathf.Abs(Mathf.Sqrt(px * px + py * py) - radius) / thickness);
                    float along = Smooth(1f - Mathf.Abs(Mathf.Atan2(px, py) * Mathf.Rad2Deg) / halfDegrees);
                    pixels[y * columns + x] = new Color32(255, 255, 255, (byte)(255f * across * along));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
