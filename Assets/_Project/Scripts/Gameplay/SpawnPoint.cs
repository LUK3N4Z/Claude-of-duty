using System.Collections.Generic;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>A place a combatant can (re)enter the match, facing this transform's +Z.</summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPoint : MonoBehaviour
    {
        private static readonly List<SpawnPoint> all = new List<SpawnPoint>(32);

        [Tooltip("Team that may spawn here; -1 for any team (respawn modes).")]
        [SerializeField] private int team = -1;
        [Tooltip("Used for match and round starts; others only for respawns.")]
        [SerializeField] private bool initial = true;

        public static IReadOnlyList<SpawnPoint> All => all;

        public int Team => team;
        public bool Initial => initial;
        public float LastUsed { get; set; } = float.NegativeInfinity;

        public void Configure(int spawnTeam, bool initialSpawn)
        {
            team = spawnTeam;
            initial = initialSpawn;
        }

        public bool Allows(int candidateTeam) => team < 0 || team == candidateTeam;

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable() => all.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => all.Clear();

        private void OnDrawGizmos()
        {
            Gizmos.color = team == 0 ? Color.blue : team == 1 ? Color.red : Color.white;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.35f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward);
        }
    }
}
