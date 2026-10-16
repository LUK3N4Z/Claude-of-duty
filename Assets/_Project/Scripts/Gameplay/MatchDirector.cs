using System.Collections.Generic;
using FPS.AI;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Runs a match for the <see cref="GameMode"/> on the same object: the player plus soldier slots per team (built
    /// into squads), phases (pre-round countdown, live, round over, match over), the clock, round wins and scores,
    /// placement through the mode's spawn choice, respawns after the mode's delay, and corpse clean-up. A dead player
    /// loses control until respawned; soldiers are replaced with fresh bodies on every spawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchDirector : MonoBehaviour
    {
        public const int TeamCount = 2;
        public const int PlayerTeam = 0;
        public const string TeamPrefabsFieldName = nameof(teamSoldierPrefabs);
        public const string TeamSoldiersFieldName = nameof(teamSoldiers);

        [Tooltip("Soldier prefab per team (index = team).")]
        [SerializeField] private GameObject[] teamSoldierPrefabs = new GameObject[TeamCount];
        [Tooltip("Soldiers per team; the player fights on team 0 in addition.")]
        [SerializeField] private int[] teamSoldiers = { 0, 4 };
        [SerializeField, Min(1)] private int squadSize = 4;
        [SerializeField, Min(0f)] private float preRoundTime = 3f;
        [SerializeField, Min(0f)] private float roundOverTime = 5f;
        [Tooltip("Bodies of the fallen are cleared after this long in respawn modes (s).")]
        [SerializeField, Min(1f)] private float corpseTime = 20f;

        private readonly List<Combatant> combatants = new List<Combatant>(16);
        private readonly List<SquadCoordinator>[] squads = { new List<SquadCoordinator>(4), new List<SquadCoordinator>(4) };
        private readonly int[] roundWins = new int[TeamCount];
        private readonly int[] scores = new int[TeamCount];
        private readonly List<GameObject> corpses = new List<GameObject>(16);
        private readonly List<float> corpseTimes = new List<float>(16);
        private PlayerRig playerRig;
        private float phaseEnd;

        public GameMode Mode { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Waiting;
        public int Round { get; private set; }
        /// <summary>Clock of the live round or match (infinite without a limit).</summary>
        public float TimeLeft { get; private set; } = float.PositiveInfinity;
        public float PhaseTimeLeft => Mathf.Max(0f, phaseEnd - Time.time);
        public int Winner { get; private set; } = -1;
        public string Message { get; private set; } = string.Empty;
        public Combatant Player { get; private set; }
        public IReadOnlyList<Combatant> Combatants => combatants;

        public int RoundWins(int team) => roundWins[team];
        public int Score(int team) => scores[team];
        public IReadOnlyList<SquadCoordinator> Squads(int team) => squads[team];

        public void Configure(GameObject[] prefabs, int[] soldiersPerTeam)
        {
            teamSoldierPrefabs = prefabs;
            teamSoldiers = soldiersPerTeam;
        }

        public int AliveCount(int team)
        {
            int alive = 0;
            for (int i = 0; i < combatants.Count; i++) alive += combatants[i].Team == team && combatants[i].Alive ? 1 : 0;
            return alive;
        }

        public void AddScore(int team, int points) => scores[team] += points;

        /// <summary>Ends the live round in favour of <paramref name="winner"/> (-1 for a draw).</summary>
        public void EndRound(int winner, string message)
        {
            if (Phase != MatchPhase.Live) return;
            Winner = winner;
            Message = message;
            if (winner >= 0) roundWins[winner]++;
            SetPhase(MatchPhase.RoundOver, roundOverTime);
            SetSoldiersActive(false);
        }

        public void EndMatch(int winner, string message)
        {
            if (Phase == MatchPhase.MatchOver) return;
            Winner = winner;
            Message = message;
            SetPhase(MatchPhase.MatchOver, 0f);
            SetSoldiersActive(false);
        }

        /// <summary>Places the player, view and all (spawns, scripted moves).</summary>
        public void TeleportPlayer(Vector3 position, Quaternion rotation)
        {
            if (playerRig == null) return;
            var controller = playerRig.GetComponent<CharacterController>();
            controller.enabled = false;
            playerRig.transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            if (playerRig.TryGetComponent(out RealisticLocomotionController locomotion)) locomotion.SetLook(rotation.eulerAngles.y, 0f);
            Physics.SyncTransforms();
        }

        private void Awake()
        {
            Mode = GetComponent<GameMode>();
            if (Mode == null)
            {
                Debug.LogError($"{nameof(MatchDirector)} on '{name}' needs a {nameof(GameMode)} component.", this);
                enabled = false;
                return;
            }

            Mode.Bind(this);
        }

        private void Start()
        {
            playerRig = FindAnyObjectByType<PlayerRig>();
            if (playerRig != null)
            {
                Player = new Combatant { Root = playerRig.gameObject, Team = PlayerTeam, IsPlayer = true };
                Player.Health = playerRig.GetComponent<TacticalHealthAndArmorSystem>();
                Player.Target = playerRig.GetComponent<PerceivableTarget>();
                Combatant player = Player;
                Player.Health.Died += () => OnDied(player);
                combatants.Add(Player);
            }

            for (int team = 0; team < TeamCount; team++)
            {
                if (team >= teamSoldiers.Length || teamSoldierPrefabs.Length <= team || teamSoldierPrefabs[team] == null) continue;
                for (int i = 0; i < teamSoldiers[team]; i++) combatants.Add(new Combatant { Team = team });
            }

            BeginRound();
        }

        private void Update()
        {
            float now = Time.time;
            ClearOldCorpses(now);
            switch (Phase)
            {
                case MatchPhase.PreRound:
                    if (now < phaseEnd) return;
                    TimeLeft = Mode.TimeLimit > 0f ? Mode.TimeLimit : float.PositiveInfinity;
                    SetPhase(MatchPhase.Live, 0f);
                    SetSoldiersActive(true);
                    Mode.OnLive();
                    return;

                case MatchPhase.Live:
                    if (!Mode.ClockStopped && !float.IsPositiveInfinity(TimeLeft))
                    {
                        TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
                        if (TimeLeft <= 0f)
                        {
                            Mode.OnTimeExpired();
                            if (Phase != MatchPhase.Live) return;
                        }
                    }

                    Mode.Tick(Time.deltaTime);
                    if (Phase == MatchPhase.Live) Respawns(now);
                    return;

                case MatchPhase.RoundOver:
                    if (now < phaseEnd) return;
                    if (Mode.IsMatchDecided(out int winner)) EndMatch(winner, winner == PlayerTeam ? "Match won" : winner < 0 ? "Match drawn" : "Match lost");
                    else BeginRound();
                    return;
            }
        }

        private void BeginRound()
        {
            Round++;
            Winner = -1;
            Message = string.Empty;
            for (int team = 0; team < TeamCount; team++)
            {
                for (int i = 0; i < squads[team].Count; i++) squads[team][i].ResetTactics();
            }

            Mode.OnRoundStart();
            for (int i = 0; i < combatants.Count; i++) Spawn(combatants[i], true);
            SetPhase(MatchPhase.PreRound, preRoundTime);
            SetSoldiersActive(false);
        }

        private void Respawns(float now)
        {
            if (Mode.RespawnDelay < 0f) return;
            for (int i = 0; i < combatants.Count; i++)
            {
                Combatant combatant = combatants[i];
                if (!combatant.Alive && now >= combatant.RespawnAt) Spawn(combatant, false);
            }
        }

        private void Spawn(Combatant combatant, bool initial)
        {
            SpawnPoint point = Mode.ChooseSpawn(combatant, initial);
            Vector3 position = point != null ? point.transform.position : transform.position;
            Quaternion rotation = point != null ? point.transform.rotation : transform.rotation;
            if (point != null) point.LastUsed = Time.time;

            if (combatant.IsPlayer)
            {
                TeleportPlayer(position, rotation);
                combatant.Health.ResetState();
                if (playerRig.TryGetComponent(out RealisticWeaponController weapons)) weapons.RefillAmmunition();
                if (playerRig.TryGetComponent(out TacticalEquipmentSystem equipment)) equipment.Refill();
                SetPlayerControl(true);
            }
            else
            {
                RetireBody(combatant);
                SquadCoordinator squad = SquadFor(combatant.Team);
                GameObject body = Instantiate(teamSoldierPrefabs[combatant.Team], position, rotation, squad.transform);
                body.name = $"Team {combatant.Team} Soldier";
                body.SetActive(true); // prefab templates may be kept inactive
                combatant.Root = body;
                combatant.Health = body.GetComponent<TacticalHealthAndArmorSystem>();
                combatant.Target = body.GetComponent<PerceivableTarget>();
                combatant.Brain = body.GetComponent<TacticalAIBrain>();
                Combatant slot = combatant;
                combatant.Health.Died += () => OnDied(slot);
                squad.Register(combatant.Brain);
                SetSoldierActive(combatant, Phase == MatchPhase.Live);
            }

            combatant.RespawnAt = float.PositiveInfinity;
            Mode.OnSpawned(combatant);
        }

        /// <summary>Removes a soldier's previous body: the living vanish, the dead stay a while as corpses.</summary>
        private void RetireBody(Combatant combatant)
        {
            if (combatant.Root == null) return;
            if (combatant.Brain != null && combatant.Brain.Squad != null) combatant.Brain.Squad.Unregister(combatant.Brain);
            if (combatant.Health != null && combatant.Health.IsDead && Mode.RespawnDelay >= 0f)
            {
                corpses.Add(combatant.Root);
                corpseTimes.Add(Time.time + corpseTime);
            }
            else
            {
                Destroy(combatant.Root);
            }

            combatant.Root = null;
            combatant.Brain = null;
        }

        private void ClearOldCorpses(float now)
        {
            for (int i = corpses.Count - 1; i >= 0; i--)
            {
                if (now < corpseTimes[i] && Phase != MatchPhase.PreRound) continue;
                if (corpses[i] != null) Destroy(corpses[i]);
                corpses.RemoveAt(i);
                corpseTimes.RemoveAt(i);
            }
        }

        private SquadCoordinator SquadFor(int team)
        {
            List<SquadCoordinator> teamSquads = squads[team];
            for (int i = 0; i < teamSquads.Count; i++)
            {
                if (teamSquads[i].Members.Count < squadSize) return teamSquads[i];
            }

            var squadObject = new GameObject($"Team {team} Squad {teamSquads.Count + 1}");
            squadObject.transform.SetParent(transform, false);
            SquadCoordinator squad = squadObject.AddComponent<SquadCoordinator>();
            teamSquads.Add(squad);
            return squad;
        }

        private void OnDied(Combatant combatant)
        {
            combatant.DiedAt = Time.time;
            combatant.RespawnAt = Mode.RespawnDelay >= 0f ? Time.time + Mode.RespawnDelay : float.PositiveInfinity;
            if (combatant.IsPlayer) SetPlayerControl(false);
            if (Phase == MatchPhase.Live) Mode.OnCombatantDied(combatant);
        }

        private void SetSoldiersActive(bool active)
        {
            for (int i = 0; i < combatants.Count; i++)
            {
                if (!combatants[i].IsPlayer && combatants[i].Alive) SetSoldierActive(combatants[i], active);
            }
        }

        private static void SetSoldierActive(Combatant combatant, bool active)
        {
            TacticalAIBrain brain = combatant.Brain;
            if (brain == null) return;
            brain.enabled = active;
            brain.WeaponHandler.CeaseFire();
            if (brain.Body.Agent.enabled && brain.Body.Agent.isOnNavMesh) brain.Body.Agent.isStopped = !active;
        }

        private void SetPlayerControl(bool active)
        {
            if (playerRig == null) return;
            if (playerRig.TryGetComponent(out RealisticWeaponController weapons))
            {
                weapons.ReleaseTrigger();
                weapons.enabled = active;
            }

            if (playerRig.TryGetComponent(out RealisticLocomotionController locomotion)) locomotion.enabled = active;
            if (playerRig.TryGetComponent(out TacticalEquipmentSystem equipment)) equipment.enabled = active;
            if (playerRig.TryGetComponent(out Interactor interactor)) interactor.enabled = active;
        }

        private void SetPhase(MatchPhase phase, float duration)
        {
            Phase = phase;
            phaseEnd = Time.time + duration;
            EventBus<MatchPhaseEvent>.Raise(new MatchPhaseEvent { Phase = phase, Round = Round, Winner = Winner, Message = Message });
        }
    }
}
