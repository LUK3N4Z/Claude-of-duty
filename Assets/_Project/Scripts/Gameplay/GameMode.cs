using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// The rules of one game mode, run by the <see cref="MatchDirector"/> on the same object: what the round start
    /// sets up, what the soldiers are told to do, what each live frame checks, how deaths and time running out decide
    /// rounds, and where combatants spawn.
    /// </summary>
    public abstract class GameMode : MonoBehaviour
    {
        [SerializeField] private string displayName = "Mode";
        [Tooltip("Round (or match) length in seconds; 0 for no limit.")]
        [SerializeField, Min(0f)] private float timeLimit;
        [Tooltip("Seconds before the fallen respawn; negative means one life per round.")]
        [SerializeField] private float respawnDelay = -1f;

        protected MatchDirector Director { get; private set; }

        public string DisplayName => displayName;
        public float TimeLimit => timeLimit;
        public float RespawnDelay => respawnDelay;
        public virtual bool UsesRounds => false;
        /// <summary>The player's current task, for the HUD. Update it only when it changes.</summary>
        public string Objective { get; protected set; } = string.Empty;
        /// <summary>Freeze the clock (e.g. while a planted charge decides the round).</summary>
        public virtual bool ClockStopped => false;

        internal void Bind(MatchDirector director) => Director = director;

        /// <summary>Before everyone is (re)spawned for a new round (or the match).</summary>
        public virtual void OnRoundStart()
        {
        }

        /// <summary>A combatant has just been placed: give soldiers their orders here.</summary>
        public virtual void OnSpawned(Combatant combatant)
        {
        }

        /// <summary>The round has gone live.</summary>
        public virtual void OnLive()
        {
        }

        /// <summary>Every live frame.</summary>
        public abstract void Tick(float dt);

        public virtual void OnCombatantDied(Combatant combatant)
        {
        }

        /// <summary>The round (or match) clock ran out.</summary>
        public abstract void OnTimeExpired();

        /// <summary>After a round ends, before the next starts: true when the match is decided.</summary>
        public virtual bool IsMatchDecided(out int winner)
        {
            winner = -1;
            return true;
        }

        public virtual SpawnPoint ChooseSpawn(Combatant combatant, bool initial) =>
            SpawnSelector.Best(combatant.Team, Director.Combatants, initial) ?? SpawnSelector.Best(combatant.Team, Director.Combatants, false);
    }
}
