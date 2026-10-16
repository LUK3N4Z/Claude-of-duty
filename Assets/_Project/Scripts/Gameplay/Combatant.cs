using FPS.AI;
using FPS.Ballistics;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>A match participant: the player or one soldier slot (its body is replaced on every respawn).</summary>
    public sealed class Combatant
    {
        public GameObject Root;
        public int Team;
        public bool IsPlayer;
        public TacticalHealthAndArmorSystem Health;
        public PerceivableTarget Target;
        /// <summary>The soldier's brain; null for the player.</summary>
        public TacticalAIBrain Brain;
        public float DiedAt = float.NegativeInfinity;
        public float RespawnAt = float.PositiveInfinity;

        public bool Alive => Root != null && Health != null && !Health.IsDead;
        public Vector3 Position => Root.transform.position;
        public Vector3 Eye => Target != null && Target.Head != null ? Target.Head.position : Position + Vector3.up * 1.6f;
    }
}
