using FPS.AI;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Hardpoint: one zone is live at a time and moves on a timer. Every scoring interval, a team with living members
    /// alone inside it scores; contested zones score nobody. Respawns are on, placed by line-of-sight spawn evaluation
    /// that also keeps spawns out of the live zone. All squads are sent to hold the live zone.
    /// </summary>
    public sealed class HardpointMode : GameMode
    {
        [SerializeField] private ObjectiveZone[] zones = new ObjectiveZone[0];
        [SerializeField, Min(5f)] private float zoneDuration = 60f;
        [SerializeField, Min(1)] private int scoreLimit = 150;
        [SerializeField, Min(0.1f)] private float scoreInterval = 1f;

        private float zoneTime, scoreTimer;

        public int ActiveZoneIndex { get; private set; }
        public ObjectiveZone ActiveZone => zones.Length > 0 ? zones[ActiveZoneIndex] : null;
        /// <summary>Team holding the zone alone, -1 when empty, -2 when contested.</summary>
        public int Controller { get; private set; } = -1;
        public float ZoneTimeLeft => Mathf.Max(0f, zoneDuration - zoneTime);

        public void Configure(ObjectiveZone[] hardpoints) => zones = hardpoints;

        public override void OnRoundStart()
        {
            ActiveZoneIndex = 0;
            zoneTime = 0f;
            scoreTimer = 0f;
            Controller = -1;
            UpdateObjectiveText();
        }

        public override void OnLive() => OrderSquads();

        public override void Tick(float dt)
        {
            ObjectiveZone zone = ActiveZone;
            if (zone == null) return;

            zoneTime += dt;
            if (zoneTime >= zoneDuration)
            {
                ActiveZoneIndex = (ActiveZoneIndex + 1) % zones.Length;
                zoneTime = 0f;
                zone = ActiveZone;
                OrderSquads();
            }

            int present = 0, holder = -1;
            var combatants = Director.Combatants;
            for (int i = 0; i < combatants.Count; i++)
            {
                Combatant combatant = combatants[i];
                if (!combatant.Alive || !zone.Contains(combatant.Position)) continue;
                if (holder != combatant.Team) present++;
                holder = combatant.Team;
            }

            int controller = present == 0 ? -1 : present > 1 ? -2 : holder;
            if (controller != Controller)
            {
                Controller = controller;
                UpdateObjectiveText();
            }

            scoreTimer += dt;
            while (scoreTimer >= scoreInterval)
            {
                scoreTimer -= scoreInterval;
                if (Controller < 0) continue;
                Director.AddScore(Controller, 1);
                if (Director.Score(Controller) >= scoreLimit)
                {
                    Director.EndMatch(Controller, Controller == MatchDirector.PlayerTeam ? "Score limit reached — victory" : "Score limit reached — defeat");
                    return;
                }
            }
        }

        public override void OnTimeExpired()
        {
            int blue = Director.Score(0), red = Director.Score(1);
            Director.EndMatch(blue == red ? -1 : blue > red ? 0 : 1, blue == red ? "Time — draw" : blue > red ? "Time — victory" : "Time — defeat");
        }

        public override SpawnPoint ChooseSpawn(Combatant combatant, bool initial) =>
            SpawnSelector.Best(combatant.Team, Director.Combatants, initial, ActiveZone) ??
            SpawnSelector.Best(combatant.Team, Director.Combatants, false, ActiveZone) ??
            SpawnSelector.Best(combatant.Team, Director.Combatants, false);

        private void OrderSquads()
        {
            ObjectiveZone zone = ActiveZone;
            if (zone == null) return;
            for (int team = 0; team < MatchDirector.TeamCount; team++)
            {
                var squads = Director.Squads(team);
                for (int i = 0; i < squads.Count; i++) squads[i].SetObjective(ObjectiveKind.Hold, zone.Center, zone.Radius, zone.transform.forward);
            }

            UpdateObjectiveText();
        }

        private void UpdateObjectiveText()
        {
            ObjectiveZone zone = ActiveZone;
            if (zone == null) return;
            string state = Controller == MatchDirector.PlayerTeam ? "held" : Controller == -2 ? "contested" : Controller < 0 ? "neutral" : "enemy held";
            Objective = $"Hardpoint {zone.DisplayName}: {state}";
        }
    }
}
