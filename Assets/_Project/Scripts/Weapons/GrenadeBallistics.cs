using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>Drag-free throw solutions for hand grenades (dense, slow and short-ranged enough to ignore drag).</summary>
    public static class GrenadeBallistics
    {
        private const int ArcSegments = 10;

        /// <summary>
        /// Launch velocity that carries a throw from <paramref name="origin"/> to <paramref name="target"/> at
        /// <paramref name="speed"/>; the low arc skims through doorways, the high arc lobs over cover. False when the
        /// target is out of reach at that speed.
        /// </summary>
        public static bool SolveLaunch(Vector3 origin, Vector3 target, float speed, bool highArc, out Vector3 velocity)
        {
            float g = -Physics.gravity.y;
            Vector3 delta = target - origin;
            var planar = new Vector3(delta.x, 0f, delta.z);
            float x = planar.magnitude;
            float y = delta.y;
            float v2 = speed * speed;

            if (x < 1e-3f)
            {
                velocity = Vector3.up * (y >= 0f ? speed : -speed);
                return y <= 0f || v2 >= 2f * g * y;
            }

            float discriminant = v2 * v2 - g * (g * x * x + 2f * y * v2);
            if (discriminant < 0f)
            {
                velocity = Vector3.zero;
                return false;
            }

            float root = Mathf.Sqrt(discriminant);
            float angle = Mathf.Atan((v2 + (highArc ? root : -root)) / (g * x));
            velocity = planar / x * (speed * Mathf.Cos(angle)) + Vector3.up * (speed * Mathf.Sin(angle));
            return true;
        }

        /// <summary>Time for a throw with <paramref name="velocity"/> to cover the planar distance to <paramref name="target"/>.</summary>
        public static float FlightTime(Vector3 origin, Vector3 target, Vector3 velocity)
        {
            float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float planarDistance = new Vector2(target.x - origin.x, target.z - origin.z).magnitude;
            return planarSpeed > 1e-3f ? planarDistance / planarSpeed : 0f;
        }

        public static Vector3 Position(Vector3 origin, Vector3 velocity, float time) =>
            origin + velocity * time + 0.5f * time * time * Physics.gravity;

        /// <summary>
        /// Sweeps the arc in segments: false when it strikes geometry more than <paramref name="landingTolerance"/> from
        /// where it should land (a doorframe, a ceiling, the cover it was meant to clear).
        /// </summary>
        public static bool IsArcClear(Vector3 origin, Vector3 velocity, float flightTime, float radius, int mask, float landingTolerance = 0.75f)
        {
            Vector3 landing = Position(origin, velocity, flightTime);
            Vector3 from = origin;
            for (int i = 1; i <= ArcSegments; i++)
            {
                Vector3 to = Position(origin, velocity, flightTime * i / ArcSegments);
                Vector3 step = to - from;
                float length = step.magnitude;
                if (length > 1e-4f && Physics.SphereCast(from, radius, step / length, out RaycastHit hit, length, mask, QueryTriggerInteraction.Ignore))
                    return Vector3.Distance(hit.point, landing) <= landingTolerance;
                from = to;
            }

            return true;
        }
    }
}
