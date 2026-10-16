using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>A capture, plant or extraction area: a vertical cylinder standing on this transform.</summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveZone : MonoBehaviour
    {
        [SerializeField] private string displayName = "Zone";
        [SerializeField, Min(0.5f)] private float radius = 4f;
        [SerializeField, Min(0.5f)] private float height = 3f;

        public string DisplayName => displayName;
        public float Radius => radius;
        public Vector3 Center => transform.position;

        public void Configure(string zoneName, float zoneRadius, float zoneHeight)
        {
            displayName = zoneName;
            radius = zoneRadius;
            height = zoneHeight;
        }

        public bool Contains(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            if (offset.y < -0.5f || offset.y > height) return false;
            return offset.x * offset.x + offset.z * offset.z <= radius * radius;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Vector3 center = transform.position;
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float b = (i + 1) * Mathf.PI * 2f / segments;
                Gizmos.DrawLine(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius,
                    center + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius);
            }
        }
    }
}
