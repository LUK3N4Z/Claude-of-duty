using UnityEngine;

namespace FPS.Ballistics
{
    public enum HitZone
    {
        Head,
        Thorax,
        Stomach,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
    }

    /// <summary>
    /// A body-zone collider on the Hitbox layer, usually on a skeleton bone. Bullets that hit it damage the
    /// <see cref="TacticalHealthAndArmorSystem"/> above it, then keep going through the flesh if they have the energy.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class Hitbox : MonoBehaviour
    {
        [SerializeField] private HitZone zone = HitZone.Thorax;
        [Tooltip("Tissue the bullet passes through; the simulation's flesh surface when empty.")]
        [SerializeField] private SurfaceMaterialSO surface;

        public HitZone Zone => zone;
        public SurfaceMaterialSO Surface => surface;
        public TacticalHealthAndArmorSystem Owner { get; private set; }

        public void Configure(HitZone hitZone, SurfaceMaterialSO tissue)
        {
            zone = hitZone;
            surface = tissue;
        }

        private void Awake() => Owner = GetComponentInParent<TacticalHealthAndArmorSystem>();
    }
}
