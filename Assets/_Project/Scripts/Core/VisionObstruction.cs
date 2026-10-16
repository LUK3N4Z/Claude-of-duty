using System.Collections.Generic;
using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Smoke screens as spheres of extinction that sight lines integrate through: a line crossing a dense cloud's
    /// middle sees nothing, one clipping its thinning edge sees part. Smoke grenades register clouds; AI sight queries
    /// <see cref="Transmittance"/>.
    /// </summary>
    public static class VisionObstruction
    {
        private struct Cloud
        {
            public int Id;
            public Vector3 Center;
            public float Radius;
            public float Extinction;
        }

        private static readonly List<Cloud> clouds = new List<Cloud>(8);
        private static int nextId = 1;

        public static int Count => clouds.Count;

        /// <summary>A new, empty cloud; update it with <see cref="Set"/> and remove it with <see cref="Remove"/>.</summary>
        public static int Add()
        {
            clouds.Add(new Cloud { Id = nextId });
            return nextId++;
        }

        /// <summary>Moves and resizes cloud <paramref name="id"/>; <paramref name="extinction"/> is optical depth per metre.</summary>
        public static void Set(int id, Vector3 center, float radius, float extinction)
        {
            for (int i = 0; i < clouds.Count; i++)
            {
                if (clouds[i].Id != id) continue;
                clouds[i] = new Cloud { Id = id, Center = center, Radius = radius, Extinction = extinction };
                return;
            }
        }

        public static void Remove(int id)
        {
            for (int i = 0; i < clouds.Count; i++)
            {
                if (clouds[i].Id != id) continue;
                clouds.RemoveAt(i);
                return;
            }
        }

        /// <summary>Fraction of light that survives the straight line from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float Transmittance(Vector3 from, Vector3 to)
        {
            if (clouds.Count == 0) return 1f;

            Vector3 segment = to - from;
            float length = segment.magnitude;
            if (length < 1e-4f) return 1f;
            Vector3 direction = segment / length;

            float depth = 0f;
            for (int i = 0; i < clouds.Count; i++)
            {
                Cloud cloud = clouds[i];
                if (cloud.Radius <= 0f || cloud.Extinction <= 0f) continue;
                Vector3 offset = from - cloud.Center;
                float b = Vector3.Dot(offset, direction);
                float c = offset.sqrMagnitude - cloud.Radius * cloud.Radius;
                float discriminant = b * b - c;
                if (discriminant <= 0f) continue;

                float root = Mathf.Sqrt(discriminant);
                float enter = Mathf.Max(0f, -b - root);
                float exit = Mathf.Min(length, -b + root);
                if (exit > enter) depth += (exit - enter) * cloud.Extinction;
            }

            return Mathf.Exp(-depth);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            clouds.Clear();
            nextId = 1;
        }
    }
}
