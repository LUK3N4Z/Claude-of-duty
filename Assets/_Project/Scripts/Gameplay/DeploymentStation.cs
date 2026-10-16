using System;
using System.Collections.Generic;
using FPS.AI;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;
using UnityEngine.UI;

namespace FPS.Gameplay
{
    /// <summary>
    /// The armory's deployment table. Interact to step up: the view drops onto the hologram over the table and the
    /// tactical screen lists the operations (Tactical Raid, Search &amp; Destroy, Hardpoint, the Locomotion Test Range)
    /// with their map schematics and briefings, and the AI difficulty (Recruit, Regular, Hardened, Realism). Selecting an
    /// operation projects its map on the hologram; Deploy saves the difficulty with the profile and loads the operation
    /// behind the loading screen. Back (or Menu) hands control back.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeploymentStation : MonoBehaviour, IInteractable
    {
        private static readonly int UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");
        private static readonly string[] DifficultyNotes =
        {
            "Slow to notice you, slow to fire, loose aim.",
            "Soldiers as trained: the intended fight.",
            "Quick eyes and quick triggers.",
            "They see you first and rarely miss.",
        };

        [Serializable]
        public struct Operation
        {
            public string title;
            [Tooltip("Scene name in the build settings.")]
            public string scene;
            [TextArea] public string briefing;
            public Texture2D preview;
        }

        [SerializeField] private Operation[] operations = new Operation[0];
        [SerializeField] private Camera stationCamera;
        [Tooltip("Hologram over the table: shows the selected operation's map.")]
        [SerializeField] private Renderer hologram;
        [SerializeField] private string prompt = "Open the deployment table";

        private readonly List<Behaviour> paused = new List<Behaviour>(4);
        private readonly List<Button> operationButtons = new List<Button>(8);
        private readonly Button[] difficultyButtons = new Button[4];
        private MaterialPropertyBlock block;
        private PlayerRig user;
        private GameObject ui;
        private Text briefing, difficultyNote;
        private RawImage preview;

        /// <summary>The table in use, or null.</summary>
        public static DeploymentStation Active { get; private set; }
        public string Prompt => prompt;
        public bool IsActive => Active == this;
        public int OperationCount => operations.Length;
        public int SelectedOperation { get; private set; }
        public AIDifficulty SelectedDifficulty { get; private set; } = AIDifficulty.Regular;
        public Operation GetOperation(int index) => operations[index];
        public Camera StationCamera => stationCamera;

        public void Configure(Operation[] list, Camera view, Renderer projector)
        {
            operations = list;
            stationCamera = view;
            hologram = projector;
        }

        public bool CanInteract(GameObject candidate) => Active == null && operations.Length > 0 && !SceneFlow.IsLoading;

        public void Interact(GameObject candidate)
        {
            if (CanInteract(candidate) && candidate.TryGetComponent(out PlayerRig rig)) Enter(rig);
        }

        /// <summary>Steps <paramref name="rig"/> up to the table.</summary>
        public void Enter(PlayerRig rig)
        {
            Active = this;
            user = rig;
            PlayerControl.Pause(rig.gameObject, paused);
            rig.PlayerCamera.enabled = false;
            if (stationCamera != null) stationCamera.enabled = true;
            SaveProfileManager profiles = rig.GetComponent<SaveProfileManager>();
            SelectDifficulty(profiles != null ? profiles.Profile.settings.aiDifficulty : CombatDifficulty.Current);
            SelectOperation(SelectedOperation);
            ui.SetActive(true);
        }

        public void Leave()
        {
            if (!IsActive) return;
            ui.SetActive(false);
            if (stationCamera != null) stationCamera.enabled = false;
            if (user != null) user.PlayerCamera.enabled = true;
            PlayerControl.Resume(paused);
            user = null;
            Active = null;
        }

        public void SelectOperation(int index)
        {
            if (operations.Length == 0) return;
            SelectedOperation = Mathf.Clamp(index, 0, operations.Length - 1);
            Operation operation = operations[SelectedOperation];
            for (int i = 0; i < operationButtons.Count; i++) UIFactory.SetSelected(operationButtons[i], i == SelectedOperation);
            briefing.text = operation.briefing;
            preview.texture = operation.preview;
            preview.enabled = operation.preview != null;
            if (hologram != null)
            {
                hologram.GetPropertyBlock(block);
                block.SetTexture(UnlitColorMapId, operation.preview != null ? operation.preview : Texture2D.blackTexture);
                hologram.SetPropertyBlock(block);
            }
        }

        public void SelectDifficulty(AIDifficulty difficulty)
        {
            SelectedDifficulty = difficulty;
            for (int i = 0; i < difficultyButtons.Length; i++) UIFactory.SetSelected(difficultyButtons[i], i == (int)difficulty);
            difficultyNote.text = DifficultyNotes[(int)difficulty];
        }

        /// <summary>Sets the difficulty (and saves it with the profile) and loads the selected operation.</summary>
        public void Deploy()
        {
            if (!IsActive || operations.Length == 0) return;
            Operation operation = operations[SelectedOperation];
            CombatDifficulty.Current = SelectedDifficulty;
            SaveProfileManager profiles = user.GetComponent<SaveProfileManager>();
            if (profiles != null)
            {
                profiles.Profile.settings.aiDifficulty = SelectedDifficulty;
                profiles.Save();
            }

            Leave();
            SceneFlow.Load(operation.scene, operation.title, operation.preview);
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (stationCamera != null) stationCamera.enabled = false;
            UIFactory.EnsureEventSystem();
            Build();
        }

        private void Start() => SelectOperation(0);

        private void OnDisable() => Leave();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        private void Build()
        {
            RectTransform root = UIFactory.Canvas(transform, "Deployment Screen", 20, true);
            ui = root.gameObject;
            var accent = new Color(0.4f, 0.85f, 0.95f, 0.95f);
            Text header = UIFactory.Label(root, 34, new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(1600f, 50f), TextAnchor.MiddleCenter);
            header.text = "DEPLOYMENT  //  SELECT OPERATION";
            header.color = accent;

            RectTransform list = UIFactory.Panel(root, "Operations", new Vector2(0f, 0.5f), new Vector2(250f, 30f), new Vector2(420f, 640f), UIFactory.PanelColor);
            for (int i = 0; i < operations.Length; i++)
            {
                int index = i;
                operationButtons.Add(UIFactory.Button(list, operations[i].title, new Vector2(0.5f, 1f), new Vector2(0f, -50f - i * 70f), new Vector2(370f, 56f),
                    () => SelectOperation(index)));
            }

            RectTransform map = UIFactory.Panel(root, "Map", new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(640f, 640f), UIFactory.PanelColor);
            preview = UIFactory.Picture(map, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 600f));

            RectTransform side = UIFactory.Panel(root, "Briefing", new Vector2(1f, 0.5f), new Vector2(-250f, 30f), new Vector2(420f, 640f), UIFactory.PanelColor);
            Text briefingTitle = UIFactory.Label(side, 20, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(380f, 30f), TextAnchor.MiddleLeft);
            briefingTitle.text = "BRIEFING";
            briefingTitle.color = accent;
            briefing = UIFactory.Label(side, 19, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(380f, 200f), TextAnchor.UpperLeft);
            Text difficultyTitle = UIFactory.Label(side, 20, new Vector2(0.5f, 1f), new Vector2(0f, -290f), new Vector2(380f, 30f), TextAnchor.MiddleLeft);
            difficultyTitle.text = "AI DIFFICULTY";
            difficultyTitle.color = accent;
            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                var difficulty = (AIDifficulty)i;
                difficultyButtons[i] = UIFactory.Button(side, difficulty.ToString(), new Vector2(0.5f, 1f), new Vector2(0f, -340f - i * 54f), new Vector2(360f, 46f),
                    () => SelectDifficulty(difficulty));
            }

            difficultyNote = UIFactory.Label(side, 17, new Vector2(0.5f, 0f), new Vector2(0f, 45f), new Vector2(380f, 60f), TextAnchor.MiddleLeft);

            UIFactory.Button(root, "DEPLOY", new Vector2(0.5f, 0f), new Vector2(130f, 60f), new Vector2(240f, 56f), Deploy);
            UIFactory.Button(root, "Back", new Vector2(0.5f, 0f), new Vector2(-130f, 60f), new Vector2(240f, 56f), Leave);
            ui.SetActive(false);
        }
    }
}
