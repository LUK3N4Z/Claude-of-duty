using FPS.Locomotion;
using UnityEngine;
using UnityEngine.UI;

namespace FPS.Gameplay
{
    /// <summary>
    /// Match overlay built in code: mode and objective (top left), clock and score (top centre), a banner for round and
    /// match results and the pre-round countdown, and a progress bar while the player holds an objective. Text is
    /// rebuilt only when what it shows changes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MatchDirector))]
    public sealed class MatchHUD : MonoBehaviour
    {
        private MatchDirector director;
        private Interactor interactor;
        private Text mode, objective, clock, score, banner;
        private Image progressFill;
        private GameObject progressBar;
        private int shownSeconds = -1, shownBlue = -1, shownRed = -1, shownCountdown = -1;
        private string shownObjective;
        private MatchPhase shownPhase = MatchPhase.Waiting;

        private void Awake()
        {
            director = GetComponent<MatchDirector>();
            Build();
        }

        private void Start()
        {
            var rig = FindAnyObjectByType<PlayerRig>();
            if (rig != null) rig.TryGetComponent(out interactor);
            mode.text = director.Mode != null ? director.Mode.DisplayName : string.Empty;
        }

        private void Update()
        {
            GameMode rules = director.Mode;
            if (rules == null) return;

            if (!ReferenceEquals(shownObjective, rules.Objective))
            {
                shownObjective = rules.Objective;
                objective.text = shownObjective;
            }

            int blue = rules.UsesRounds ? director.RoundWins(0) : director.Score(0);
            int red = rules.UsesRounds ? director.RoundWins(1) : director.Score(1);
            if (blue != shownBlue || red != shownRed)
            {
                shownBlue = blue;
                shownRed = red;
                score.text = $"BLUFOR {blue}  —  {red} OPFOR";
            }

            int seconds = float.IsPositiveInfinity(director.TimeLeft) ? -1 : Mathf.CeilToInt(director.TimeLeft);
            if (director.Phase == MatchPhase.Live && seconds != shownSeconds)
            {
                shownSeconds = seconds;
                clock.text = seconds < 0 ? string.Empty : $"{seconds / 60}:{seconds % 60:00}";
            }

            UpdateBanner();
            UpdateProgress();
        }

        private void UpdateBanner()
        {
            MatchPhase phase = director.Phase;
            if (phase == MatchPhase.PreRound)
            {
                int countdown = Mathf.CeilToInt(director.PhaseTimeLeft);
                if (countdown != shownCountdown || shownPhase != phase)
                {
                    shownCountdown = countdown;
                    banner.text = director.Mode.UsesRounds ? $"Round {director.Round}  —  {countdown}" : $"{director.Mode.DisplayName}  —  {countdown}";
                }
            }
            else if (phase != shownPhase)
            {
                banner.text = phase == MatchPhase.RoundOver || phase == MatchPhase.MatchOver ? director.Message : string.Empty;
            }

            shownPhase = phase;
        }

        private void UpdateProgress()
        {
            var hold = interactor != null ? interactor.Focused as HoldInteraction : null;
            bool holding = hold != null && hold.User == interactor.gameObject && hold.Progress01 > 0f;
            if (progressBar.activeSelf != holding) progressBar.SetActive(holding);
            if (holding) progressFill.fillAmount = hold.Progress01;
        }

        private void Build()
        {
            RectTransform root = UIFactory.Canvas(transform, "Match HUD", 10, false);
            mode = UIFactory.Label(root, 24, new Vector2(0f, 1f), new Vector2(240f, -40f), new Vector2(420f, 34f), TextAnchor.MiddleLeft);
            objective = UIFactory.Label(root, 20, new Vector2(0f, 1f), new Vector2(240f, -72f), new Vector2(420f, 30f), TextAnchor.MiddleLeft);
            clock = UIFactory.Label(root, 26, new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(200f, 34f), TextAnchor.MiddleCenter);
            score = UIFactory.Label(root, 20, new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(420f, 30f), TextAnchor.MiddleCenter);
            banner = UIFactory.Label(root, 40, new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(1200f, 60f), TextAnchor.MiddleCenter);
            banner.text = string.Empty;
            clock.text = string.Empty;

            RectTransform bar = UIFactory.Panel(root, "Hold Progress", new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(300f, 8f),
                new Color(0f, 0f, 0f, 0.5f));
            bar.GetComponent<Image>().raycastTarget = false;
            progressBar = bar.gameObject;
            progressFill = new GameObject("Fill").AddComponent<Image>();
            progressFill.rectTransform.SetParent(bar, false);
            progressFill.rectTransform.anchorMin = Vector2.zero;
            progressFill.rectTransform.anchorMax = Vector2.one;
            progressFill.rectTransform.offsetMin = progressFill.rectTransform.offsetMax = Vector2.zero;
            progressFill.color = new Color(1f, 1f, 1f, 0.85f);
            progressFill.raycastTarget = false;
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressBar.SetActive(false);
        }
    }
}
