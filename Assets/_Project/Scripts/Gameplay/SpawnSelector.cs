using System.Collections.Generic;
using FPS.Core;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// Smart spawn choice: every candidate is scored by whether any living enemy has line of sight to a body standing
    /// there (eye-to-head line cast), how far the nearest enemy is, teammates close by and how recently it was used, so
    /// nobody respawns in front of a muzzle or next to a camping enemy.
    /// </summary>
    public static class SpawnSelector
    {
        /// <summary>Spawns closer than this to an enemy are only used as a last resort (m).</summary>
        public const float SafeDistance = 12f;
        private const float SightRange = 90f;

        public static float Score(SpawnPoint point, int team, IReadOnlyList<Combatant> combatants)
        {
            Vector3 position = point.transform.position;
            Vector3 head = position + Vector3.up * 1.6f;
            float nearestEnemy = float.PositiveInfinity;
            bool seen = false;
            int friends = 0;
            for (int i = 0; i < combatants.Count; i++)
            {
                Combatant other = combatants[i];
                if (!other.Alive) continue;
                float distance = Vector3.Distance(other.Position, position);
                if (other.Team == team)
                {
                    if (distance < 20f) friends++;
                    continue;
                }

                nearestEnemy = Mathf.Min(nearestEnemy, distance);
                if (!seen && distance < SightRange && !Physics.Linecast(other.Eye, head, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                    seen = true;
            }

            float score = Mathf.Min(nearestEnemy, 40f);
            if (seen) score -= 60f;
            if (nearestEnemy < SafeDistance) score -= 100f;
            score += Mathf.Min(friends, 3) * 4f;
            score -= Mathf.Max(0f, 5f - (Time.time - point.LastUsed)) * 10f; // never two spawns on one spot
            return score;
        }

        /// <summary>The best spawn for <paramref name="team"/>, skipping points inside <paramref name="avoid"/>; null when none exist.</summary>
        public static SpawnPoint Best(int team, IReadOnlyList<Combatant> combatants, bool initialOnly, ObjectiveZone avoid = null)
        {
            SpawnPoint best = null;
            float bestScore = float.NegativeInfinity;
            IReadOnlyList<SpawnPoint> points = SpawnPoint.All;
            for (int i = 0; i < points.Count; i++)
            {
                SpawnPoint point = points[i];
                if (!point.Allows(team) || (initialOnly && !point.Initial)) continue;
                if (avoid != null && avoid.Contains(point.transform.position)) continue;
                float score = Score(point, team, combatants);
                if (score <= bestScore) continue;
                bestScore = score;
                best = point;
            }

            return best;
        }
    }
}
