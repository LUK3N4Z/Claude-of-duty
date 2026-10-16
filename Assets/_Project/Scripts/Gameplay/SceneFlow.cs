using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPS.Gameplay
{
    /// <summary>
    /// Moves between the hub and the operations: loads a scene asynchronously behind a loading screen (operation name,
    /// map schematic, a progress bar and rotating tactical tips), shown at least <c>minimumTime</c> so the tips can be
    /// read, then fades out once the new scene has drawn its first frames. Lives on its own object that survives loads;
    /// created on first use. Also quits the game.
    /// </summary>
    public sealed class SceneFlow : MonoBehaviour
    {
        public const string ArmoryScene = "Armory";

        private static readonly string[] Tips =
        {
            "Hold R to feel how full the magazine is without dropping it.",
            "A tactical reload keeps the partial magazine; tap R twice to drop it and reload faster.",
            "Rounds go where the barrel points, not where the screen centre is. Mind your muzzle near cover.",
            "Supersonic rounds crack from ahead of where they were fired: the shooter is behind the sound.",
            "Hold Interact on a door and scroll to crack it open and peek. Bash it with V to stun whoever waits behind.",
            "A slide cancelled with Space or C comes up into a crouched ready almost at once.",
            "Wood and drywall stop little; concrete and steel stop a lot. Pick cover by what it is made of.",
            "Soldiers hear footsteps and gunfire. Walk slowly (Alt) to close in quietly.",
            "Dark corners hide you; a lit weapon light gives you away.",
            "Cook a frag for a second or two so it cannot be thrown back.",
            "Smoke blocks sight but not bullets. Soldiers fire into it where they last saw you.",
            "Leaning (Q/E) exposes less of you than stepping out of cover.",
            "Near walls the rifle pulls into a compressed ready: it still fires, but you lose the sights.",
            "Night vision turns a laser into an infrared beam only goggles can see.",
        };

        [Tooltip("Shortest time the loading screen stays up (s).")]
        [SerializeField] private float minimumTime = 1.5f;
        [SerializeField] private float tipInterval = 4f;
        [SerializeField] private float fadeTime = 0.35f;

        private static SceneFlow instance;
        private CanvasGroup group;
        private Text titleText, tipText, percentText;
        private RawImage preview;
        private RectTransform bar;
        private int tipIndex;

        /// <summary>A load is under way (loading screen up).</summary>
        public static bool IsLoading { get; private set; }
        /// <summary>The scene being loaded, or last loaded.</summary>
        public static string TargetScene { get; private set; } = string.Empty;
        public static bool LoadingScreenVisible => instance != null && instance.group.alpha > 0.01f;
        public static string CurrentTip => instance != null ? instance.tipText.text : string.Empty;

        /// <summary>Loads <paramref name="sceneName"/> (in the build settings) behind the loading screen. Ignored while loading.</summary>
        public static void Load(string sceneName, string title, Texture mapPreview = null)
        {
            if (IsLoading) return;
            if (instance == null) Create();
            instance.StartCoroutine(instance.Run(sceneName, title, mapPreview));
        }

        /// <summary>Back to the hub.</summary>
        public static void ReturnToArmory() => Load(ArmoryScene, "Armory");

        public static void Quit() => Application.Quit();

        private IEnumerator Run(string sceneName, string title, Texture mapPreview)
        {
            IsLoading = true;
            TargetScene = sceneName;
            titleText.text = title.ToUpperInvariant();
            preview.texture = mapPreview;
            preview.enabled = mapPreview != null;
            tipIndex = Random.Range(0, Tips.Length);
            tipText.text = Tips[tipIndex];
            SetProgress(0f);
            yield return Fade(1f);

            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            load.allowSceneActivation = false;
            float shown = 0f, nextTip = tipInterval;
            while (load.progress < 0.9f || shown < minimumTime)
            {
                shown += Time.unscaledDeltaTime;
                SetProgress(Mathf.Min(load.progress / 0.9f, shown / minimumTime));
                if (shown >= nextTip)
                {
                    nextTip += tipInterval;
                    tipIndex = (tipIndex + 1) % Tips.Length;
                    tipText.text = Tips[tipIndex];
                }

                yield return null;
            }

            SetProgress(1f);
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
            for (int i = 0; i < 3; i++) yield return null; // let exposure and streaming settle behind the screen
            yield return Fade(0f);
            IsLoading = false;
        }

        private IEnumerator Fade(float to)
        {
            float from = group.alpha;
            for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / fadeTime);
                yield return null;
            }

            group.alpha = to;
            group.blocksRaycasts = to > 0.5f;
        }

        private void SetProgress(float progress)
        {
            bar.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            percentText.text = $"{Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f)}%";
        }

        private static void Create()
        {
            var flowObject = new GameObject("Scene Flow");
            DontDestroyOnLoad(flowObject);
            instance = flowObject.AddComponent<SceneFlow>();
            instance.Build();
        }

        private void Build()
        {
            RectTransform root = UIFactory.Canvas(transform, "Loading Screen", 100, false);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            RectTransform backdrop = UIFactory.Panel(root, "Backdrop", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f), new Color(0.02f, 0.03f, 0.035f, 1f));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.sizeDelta = Vector2.zero;

            titleText = UIFactory.Label(root, 44, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1400f, 70f), TextAnchor.MiddleCenter);
            Text subtitle = UIFactory.Label(root, 20, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(1400f, 30f), TextAnchor.MiddleCenter);
            subtitle.text = "DEPLOYING";
            subtitle.color = new Color(0.4f, 0.85f, 0.95f, 0.9f);
            preview = UIFactory.Picture(root, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(520f, 520f));

            Text tipLabel = UIFactory.Label(root, 18, new Vector2(0.5f, 0f), new Vector2(0f, 210f), new Vector2(1400f, 30f), TextAnchor.MiddleCenter);
            tipLabel.text = "FIELD NOTE";
            tipLabel.color = new Color(0.4f, 0.85f, 0.95f, 0.9f);
            tipText = UIFactory.Label(root, 24, new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(1400f, 60f), TextAnchor.MiddleCenter);

            RectTransform track = UIFactory.Panel(root, "Progress", new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(900f, 10f), new Color(0.16f, 0.18f, 0.2f, 1f));
            bar = UIFactory.Panel(track, "Bar", new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.25f, 0.75f, 0.85f, 1f));
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            percentText = UIFactory.Label(root, 18, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(200f, 30f), TextAnchor.MiddleCenter);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            IsLoading = false;
            TargetScene = string.Empty;
        }
    }
}
