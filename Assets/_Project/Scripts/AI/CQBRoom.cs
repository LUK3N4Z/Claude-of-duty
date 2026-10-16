using System.Collections.Generic;
using UnityEngine;

namespace FPS.AI
{
    /// <summary>
    /// A room a squad clears as a team: its interior box, its entries (doorway centres, +Z pointing into the room) and
    /// its points of domination (corners the entry team takes, in entry order). Also the slicing-the-pie geometry used
    /// on the approach to a doorway.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CQBRoom : MonoBehaviour
    {
        private static readonly List<CQBRoom> all = new List<CQBRoom>(8);

        [Tooltip("Interior extents in local space, centred on this transform (m).")]
        [SerializeField] private Vector3 size = new Vector3(5.6f, 3f, 5.6f);
        [Tooltip("Doorway centres at floor level; +Z points into the room.")]
        [SerializeField] private Transform[] entries = new Transform[0];
        [Tooltip("Corners the entry team takes, first man first.")]
        [SerializeField] private Transform[] dominationPoints = new Transform[0];

        public static IReadOnlyList<CQBRoom> All => all;

        public Vector3 Center => transform.position;
        public int EntryCount => entries.Length;
        public int DominationPointCount => dominationPoints.Length;
        public Transform GetEntry(int index) => entries[index];
        public Transform GetDominationPoint(int index) => dominationPoints[index];

        public void Configure(Vector3 interiorSize, Transform[] doorways, Transform[] corners)
        {
            size = interiorSize;
            entries = doorways;
            dominationPoints = corners;
        }

        public bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            Vector3 half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y + 0.5f && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>The room containing <paramref name="point"/>, or null.</summary>
        public static CQBRoom At(Vector3 point)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Contains(point)) return all[i];
            }

            return null;
        }

        /// <summary>The entry closest to <paramref name="from"/>, or null when the room has none.</summary>
        public Transform NearestEntry(Vector3 from)
        {
            Transform best = null;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < entries.Length; i++)
            {
                float distance = (entries[i].position - from).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = entries[i];
            }

            return best;
        }

        /// <summary>
        /// Slicing the pie: <paramref name="count"/> positions on an arc of <paramref name="radius"/> outside the doorway,
        /// from <paramref name="startAngle"/> degrees off its axis on <paramref name="side"/> (+1 right, -1 left, as seen
        /// entering) to straight in front of it. From each, looking through the doorway reveals a further wedge of the
        /// room, starting with the far corner on the opposite side.
        /// </summary>
        public static int SliceArc(Transform entry, float side, float radius, float startAngle, Vector3[] points, int count)
        {
            count = Mathf.Min(count, points.Length);
            Vector3 outward = -entry.forward;
            outward.y = 0f;
            outward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, -outward);
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) : 1f;
                float angle = Mathf.Lerp(startAngle, 0f, t) * Mathf.Deg2Rad;
                points[i] = entry.position + (outward * Mathf.Cos(angle) + right * (side * Mathf.Sin(angle))) * radius;
            }

            return count;
        }

        /// <summary>Where to aim from <paramref name="position"/> while slicing: through the doorway, into the room.</summary>
        public static Vector3 SliceAim(Transform entry, Vector3 position, float depth)
        {
            Vector3 through = entry.position - position;
            through.y = 0f;
            Vector3 direction = through.sqrMagnitude > 1e-4f ? through.normalized : entry.forward;
            return entry.position + direction * depth + Vector3.up * 1.3f;
        }

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable() => all.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => all.Clear();

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.6f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null) continue;
                Gizmos.color = Color.green;
                Gizmos.DrawRay(entries[i].position, entries[i].forward);
            }

            for (int i = 0; i < dominationPoints.Length; i++)
            {
                if (dominationPoints[i] != null) Gizmos.DrawWireSphere(dominationPoints[i].position, 0.25f);
            }
        }
    }
}
