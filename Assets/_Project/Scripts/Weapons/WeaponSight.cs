using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// Optical description of a sight. Its transform is the aligner: +Z along the line of sight, +Y reticle up, origin at
    /// the rear aperture or eyepiece. Magnified sights render Picture-in-Picture onto <see cref="LensRenderer"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponSight : MonoBehaviour
    {
        [Tooltip("Optical magnification. 1 = iron sights or a red dot (no PiP).")]
        [SerializeField, Min(1f)] private float magnification = 1f;
        [Tooltip("Eye to aligner distance when aiming (m).")]
        [SerializeField, Min(0.01f)] private float eyeRelief = 0.08f;
        [Tooltip("Main camera zoom while aiming through this sight (1 = none).")]
        [SerializeField, Min(1f)] private float adsZoom = 1.1f;
        [Tooltip("Eyepiece surface that displays the magnified image. Its UVs (0-1) map the full scope view.")]
        [SerializeField] private Renderer lensRenderer;
        [Tooltip("Lateral eye offset at which the scope shadow closes in (m).")]
        [SerializeField, Min(0.001f)] private float eyeBoxRadius = 0.006f;
        [Tooltip("Eye-relief error at which the scope shadow closes in (m).")]
        [SerializeField, Min(0.001f)] private float eyeReliefTolerance = 0.02f;

        public float Magnification => magnification;
        public float EyeRelief => eyeRelief;
        public float AdsZoom => adsZoom;
        public Renderer LensRenderer => lensRenderer;
        public float EyeBoxRadius => eyeBoxRadius;
        public float EyeReliefTolerance => eyeReliefTolerance;
        public bool IsMagnified => magnification > 1.01f && lensRenderer != null;

        /// <summary>Configures the sight in one call (editor builders and tests).</summary>
        public void Configure(float magnification, float eyeRelief, float adsZoom, Renderer lensRenderer)
        {
            this.magnification = Mathf.Max(1f, magnification);
            this.eyeRelief = Mathf.Max(0.01f, eyeRelief);
            this.adsZoom = Mathf.Max(1f, adsZoom);
            this.lensRenderer = lensRenderer;
        }
    }
}
