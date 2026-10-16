using FPS.Core;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.AI
{
    public enum PeekMode
    {
        /// <summary>Concealed but no way to fire from it: a place to hide and reload.</summary>
        None,
        /// <summary>Low cover: crouch to hide, stand up to fire over it.</summary>
        StandUp,
        /// <summary>Tall cover: lean out to the left of it.</summary>
        LeanLeft,
        /// <summary>Tall cover: lean out to the right of it.</summary>
        LeanRight,
    }

    public struct CoverPoint
    {
        public Vector3 Position;
        public PeekMode Peek;
        /// <summary>Concealment holds when standing (tall cover); otherwise only when crouched.</summary>
        public bool Tall;
        public float Score;
    }

    /// <summary>
    /// Finds firing positions against a threat by sampling rings of NavMesh points around the soldier and testing each
    /// with line casts from the threat's eye: concealed crouched (low cover) or standing (tall cover), and whether
    /// standing up or leaning out to either side exposes a firing line. No allocation.
    /// </summary>
    public static class CoverFinder
    {
        private const int Directions = 16;
        private static readonly float[] Rings = { 2.5f, 4.5f, 7f, 10f, 14f };

        /// <summary>
        /// Best cover within <paramref name="maxRadius"/> of <paramref name="from"/> against an eye at
        /// <paramref name="threatEye"/>, skipping points within 1.5 m of <paramref name="avoid"/> (claimed by
        /// teammates). False when nothing conceals.
        /// </summary>
        public static bool Find(Vector3 from, Vector3 threatEye, float maxRadius, float minThreatDistance, Vector3[] avoid, int avoidCount,
            out CoverPoint best)
        {
            best = default;
            best.Score = float.NegativeInfinity;
            bool found = false;

            Vector3 toThreat = threatEye - from;
            toThreat.y = 0f;
            Vector3 threatDirection = toThreat.sqrMagnitude > 1e-4f ? toThreat.normalized : Vector3.forward;

            for (int r = 0; r < Rings.Length; r++)
            {
                float radius = Rings[r];
                if (radius > maxRadius) break;
                for (int d = 0; d < Directions; d++)
                {
                    Vector3 offset = Quaternion.Euler(0f, d * 360f / Directions + r * 11f, 0f) * Vector3.forward * radius;
                    if (!NavMesh.SamplePosition(from + offset, out NavMeshHit hit, 1.2f, NavMesh.AllAreas)) continue;

                    Vector3 point = hit.position;
                    float threatDistance = Vector3.Distance(new Vector3(point.x, threatEye.y, point.z), threatEye);
                    if (threatDistance < minThreatDistance || IsClaimed(point, avoid, avoidCount)) continue;
                    if (!Evaluate(point, threatEye, out PeekMode peek, out bool tall)) continue;

                    // Close, able to fire, not running toward the threat, and not further from it than useful.
                    float travel = Vector3.Distance(from, point);
                    float advance = Vector3.Dot(point - from, threatDirection);
                    float score = -travel + (peek != PeekMode.None ? 6f : 0f) - Mathf.Max(0f, advance) * 0.5f - Mathf.Max(0f, threatDistance - 40f) * 0.2f;
                    if (score <= best.Score) continue;

                    best = new CoverPoint { Position = point, Peek = peek, Tall = tall, Score = score };
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Whether <paramref name="point"/> conceals from <paramref name="threatEye"/>, and how to fire from it.</summary>
        public static bool Evaluate(Vector3 point, Vector3 threatEye, out PeekMode peek, out bool tall)
        {
            peek = PeekMode.None;
            bool crouchedHidden = Blocked(threatEye, point + Vector3.up * AIBody.CrouchedEyeHeight) && Blocked(threatEye, point + Vector3.up * 0.6f);
            tall = crouchedHidden && Blocked(threatEye, point + Vector3.up * AIBody.StandingEyeHeight);
            if (!crouchedHidden) return false;

            if (!tall)
            {
                if (!Blocked(threatEye, point + Vector3.up * AIBody.StandingEyeHeight)) peek = PeekMode.StandUp;
                return true;
            }

            Vector3 toThreat = threatEye - point;
            toThreat.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, toThreat.normalized);
            Vector3 eye = point + Vector3.up * (AIBody.StandingEyeHeight - 0.05f);
            if (!Blocked(threatEye, eye + right * AIBody.LeanReach)) peek = PeekMode.LeanRight;
            else if (!Blocked(threatEye, eye - right * AIBody.LeanReach)) peek = PeekMode.LeanLeft;
            return true;
        }

        /// <summary>A body standing at <paramref name="point"/> is hidden from <paramref name="threatEye"/> at the given eye height.</summary>
        public static bool IsConcealed(Vector3 point, Vector3 threatEye, bool crouched) =>
            Blocked(threatEye, point + Vector3.up * (crouched ? AIBody.CrouchedEyeHeight : AIBody.StandingEyeHeight)) &&
            Blocked(threatEye, point + Vector3.up * 0.6f);

        private static bool Blocked(Vector3 from, Vector3 to) =>
            Physics.Linecast(from, to, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);

        private static bool IsClaimed(Vector3 point, Vector3[] avoid, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if ((avoid[i] - point).sqrMagnitude < 1.5f * 1.5f) return true;
            }

            return false;
        }
    }
}
