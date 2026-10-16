using FPS.AI;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Tactical Raid: one life, no resupply — the player infiltrates, completes every objective (hold Interact at each
    /// terminal; finishing one raises the alarm across the garrison) and then extracts by holding out inside the
    /// extraction zone. Ammunition is what you carry plus magazines stripped from fallen soldiers.
    /// </summary>
    public sealed class ExtractionMode : GameMode
    {
        [SerializeField] private HoldInteraction[] objectives = new HoldInteraction[0];
        [SerializeField] private string[] objectiveNames = new string[0];
        [SerializeField] private ObjectiveZone extraction;
        [Tooltip("Time to hold inside the extraction zone until the helicopter lifts (s).")]
        [SerializeField, Min(0.5f)] private float extractTime = 8f;
        [Tooltip("Radius the garrison holds around each objective (m).")]
        [SerializeField, Min(1f)] private float garrisonRadius = 6f;

        private bool[] done = new bool[0];
        private float extractProgress;
        private bool hooked;

        public int CompletedObjectives { get; private set; }
        public int ObjectiveCount => objectives.Length;
        public bool ExtractionOpen => CompletedObjectives >= objectives.Length;
        public float ExtractProgress01 => extractProgress / extractTime;
        public ObjectiveZone Extraction => extraction;

        public void Configure(HoldInteraction[] terminals, string[] names, ObjectiveZone exfil)
        {
            objectives = terminals;
            objectiveNames = names;
            extraction = exfil;
        }

        public override void OnRoundStart()
        {
            done = new bool[objectives.Length];
            CompletedObjectives = 0;
            extractProgress = 0f;
            if (!hooked)
            {
                hooked = true;
                for (int i = 0; i < objectives.Length; i++)
                {
                    int index = i;
                    objectives[i].Allowed = user => Director.Phase == MatchPhase.Live && !done[index] && IsPlayer(user);
                    objectives[i].Completed += user => Complete(index);
                }
            }

            UpdateObjectiveText();
        }

        public override void OnLive()
        {
            // Garrison: each squad holds one objective area.
            var squads = Director.Squads(1);
            for (int i = 0; i < squads.Count && objectives.Length > 0; i++)
            {
                Transform terminal = objectives[i % objectives.Length].transform;
                squads[i].SetObjective(ObjectiveKind.Hold, terminal.position, garrisonRadius, terminal.forward);
            }
        }

        public override void Tick(float dt)
        {
            Combatant player = Director.Player;
            if (!ExtractionOpen || player == null || !player.Alive) return;

            if (extraction != null && extraction.Contains(player.Position))
            {
                extractProgress += dt;
                if (extractProgress >= extractTime) Director.EndMatch(MatchDirector.PlayerTeam, "Extracted — mission complete");
            }
            else
            {
                extractProgress = 0f;
            }
        }

        public override void OnCombatantDied(Combatant combatant)
        {
            if (combatant.IsPlayer) Director.EndMatch(1, "KIA — mission failed");
        }

        public override void OnTimeExpired() => Director.EndMatch(1, "Out of time — mission failed");

        private void Complete(int index)
        {
            if (done[index]) return;
            done[index] = true;
            CompletedObjectives++;
            UpdateObjectiveText();

            // The alarm: every soldier learns where the intruder was.
            Combatant player = Director.Player;
            if (player == null || player.Target == null) return;
            var combatants = Director.Combatants;
            for (int i = 0; i < combatants.Count; i++)
            {
                Combatant soldier = combatants[i];
                if (soldier.Brain != null && soldier.Alive && soldier.Team != player.Team)
                    soldier.Brain.Perception.ShareKnowledge(player.Target, player.Target.AimPoint, Time.time, false);
            }
        }

        private bool IsPlayer(GameObject user) => Director.Player != null && user == Director.Player.Root;

        private void UpdateObjectiveText()
        {
            if (ExtractionOpen)
            {
                Objective = extraction != null ? $"Extract: reach {extraction.DisplayName}" : "Extract";
                return;
            }

            for (int i = 0; i < objectives.Length; i++)
            {
                if (done[i]) continue;
                string label = i < objectiveNames.Length ? objectiveNames[i] : $"Objective {i + 1}";
                Objective = $"{label} ({CompletedObjectives}/{objectives.Length})";
                return;
            }
        }
    }
}
