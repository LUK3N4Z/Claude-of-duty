using FPS.AI;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Search &amp; Destroy: rounds, one life each. The attackers plant a charge at one of the sites (hold Interact at
    /// its plant point); the defenders stop the plant or defuse the charge before it detonates. Elimination wins the
    /// round unless a planted charge still has to be defused. Sides swap after <c>swapAfter</c> rounds; the first team
    /// to <c>roundsToWin</c> takes the match. Soldiers get orders: attackers push one site with a planter, defenders
    /// split over the sites, and everyone converges on a planted charge.
    /// </summary>
    public sealed class SearchAndDestroyMode : GameMode
    {
        [SerializeField] private ObjectiveZone[] sites = new ObjectiveZone[0];
        [Tooltip("One per site, at its plant spot (needs an Interactable collider).")]
        [SerializeField] private HoldInteraction[] plantPoints = new HoldInteraction[0];
        [SerializeField] private PlantedBomb bombPrefab;
        [SerializeField, Min(0.5f)] private float plantTime = 5f;
        [SerializeField, Min(0.5f)] private float defuseTime = 7f;
        [SerializeField, Min(5f)] private float bombFuse = 40f;
        [SerializeField, Min(1)] private int roundsToWin = 4;
        [SerializeField, Min(1)] private int swapAfter = 3;

        private PlantedBomb planted;
        private int plantedSite = -1, targetSite;
        private bool hooked;

        public override bool UsesRounds => true;
        public override bool ClockStopped => planted != null;
        /// <summary>Team attacking this round (the player's team in the first half).</summary>
        public int AttackingTeam => Director.Round <= swapAfter ? MatchDirector.PlayerTeam : 1 - MatchDirector.PlayerTeam;
        public int DefendingTeam => 1 - AttackingTeam;
        public PlantedBomb Planted => planted;
        public int PlantedSite => plantedSite;
        public int TargetSite => targetSite;

        public void Configure(ObjectiveZone[] siteZones, HoldInteraction[] plants, PlantedBomb bomb)
        {
            sites = siteZones;
            plantPoints = plants;
            bombPrefab = bomb;
        }

        public override void OnRoundStart()
        {
            if (planted != null) Destroy(planted.gameObject);
            planted = null;
            plantedSite = -1;
            targetSite = sites.Length > 0 ? Random.Range(0, sites.Length) : 0;
            if (!hooked)
            {
                hooked = true;
                for (int i = 0; i < plantPoints.Length; i++)
                {
                    int site = i;
                    plantPoints[i].Configure("Hold to plant the charge", plantTime);
                    plantPoints[i].Allowed = user => Director.Phase == MatchPhase.Live && planted == null && IsOnTeam(user, AttackingTeam);
                    plantPoints[i].Completed += user => Plant(site);
                }
            }

            UpdateObjectiveText();
        }

        public override void OnLive() => OrderSquads();

        public override void Tick(float dt)
        {
            // Soldiers plant and defuse by working the objective while nobody is in their sights.
            var combatants = Director.Combatants;
            for (int i = 0; i < combatants.Count; i++)
            {
                Combatant soldier = combatants[i];
                if (soldier.Brain == null || !soldier.Alive || !soldier.Brain.IsWorkingObjective) continue;
                if (planted == null && soldier.Team == AttackingTeam && targetSite < plantPoints.Length)
                    plantPoints[targetSite].Work(soldier.Root, dt);
                else if (planted != null && soldier.Team == DefendingTeam)
                    planted.Defuse.Work(soldier.Root, dt);
                if (Director.Phase != MatchPhase.Live) return;
            }

            if (planted == null && Director.AliveCount(AttackingTeam) == 0) Director.EndRound(DefendingTeam, "Attackers eliminated");
            else if (Director.AliveCount(DefendingTeam) == 0) Director.EndRound(AttackingTeam, "Defenders eliminated");
        }

        public override void OnTimeExpired()
        {
            if (planted == null) Director.EndRound(DefendingTeam, "Time expired — sites held");
        }

        public override bool IsMatchDecided(out int winner)
        {
            winner = Director.RoundWins(0) >= roundsToWin ? 0 : Director.RoundWins(1) >= roundsToWin ? 1 : -1;
            return winner >= 0;
        }

        /// <summary>Arms a charge at <paramref name="site"/> (normally through its plant point).</summary>
        public void Plant(int site)
        {
            if (planted != null || Director.Phase != MatchPhase.Live || site >= plantPoints.Length) return;
            Transform spot = plantPoints[site].transform;
            planted = Instantiate(bombPrefab, spot.position, spot.rotation);
            planted.Arm(bombFuse, defuseTime, user => Director.Phase == MatchPhase.Live && IsOnTeam(user, DefendingTeam));
            planted.Exploded += () => Director.EndRound(AttackingTeam, "Charge detonated");
            planted.Defused += () => Director.EndRound(DefendingTeam, "Charge defused");
            plantedSite = site;
            UpdateObjectiveText();
            OrderSquads();
        }

        private void OrderSquads()
        {
            if (sites.Length == 0) return;
            for (int team = 0; team < MatchDirector.TeamCount; team++)
            {
                var squads = Director.Squads(team);
                for (int i = 0; i < squads.Count; i++)
                {
                    if (planted != null)
                    {
                        Transform bomb = planted.transform;
                        // Defenders converge to defuse; attackers guard their charge.
                        squads[i].SetObjective(team == DefendingTeam ? ObjectiveKind.Interact : ObjectiveKind.Hold, bomb.position,
                            sites[plantedSite].Radius, bomb.forward);
                    }
                    else if (team == AttackingTeam)
                    {
                        Transform spot = plantPoints[targetSite].transform;
                        squads[i].SetObjective(i == 0 ? ObjectiveKind.Interact : ObjectiveKind.Hold, spot.position, sites[targetSite].Radius, spot.forward);
                    }
                    else
                    {
                        ObjectiveZone site = sites[i % sites.Length];
                        squads[i].SetObjective(ObjectiveKind.Hold, site.Center, site.Radius, site.transform.forward);
                    }
                }
            }
        }

        private bool IsOnTeam(GameObject user, int team)
        {
            var combatants = Director.Combatants;
            for (int i = 0; i < combatants.Count; i++)
            {
                if (combatants[i].Root == user) return combatants[i].Team == team && combatants[i].Alive;
            }

            return false;
        }

        private void UpdateObjectiveText()
        {
            bool attacking = AttackingTeam == MatchDirector.PlayerTeam;
            if (planted != null)
            {
                string site = sites[plantedSite].DisplayName;
                Objective = attacking ? $"Defend the charge at {site}" : $"Defuse the charge at {site}";
            }
            else
            {
                Objective = attacking ? "Plant the charge at a site" : "Defend the sites";
            }
        }
    }
}
