using UnityEngine;

namespace FPS.Ballistics
{
    /// <summary>Tags a collider with its <see cref="SurfaceMaterialSO"/>. Colliders without one use the simulation default.</summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceIdentifier : MonoBehaviour
    {
        [SerializeField] private SurfaceMaterialSO surface;

        public SurfaceMaterialSO Surface
        {
            get => surface;
            set => surface = value;
        }

        /// <summary>The surface on <paramref name="collider"/>'s GameObject, or <paramref name="fallback"/>.</summary>
        public static SurfaceMaterialSO Resolve(Collider collider, SurfaceMaterialSO fallback) =>
            collider.TryGetComponent(out SurfaceIdentifier identifier) && identifier.surface != null ? identifier.surface : fallback;
    }
}
