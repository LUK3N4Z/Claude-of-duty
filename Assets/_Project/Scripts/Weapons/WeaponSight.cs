using UnityEngine;

namespace FPS.Weapons
{
    public enum ReticleStyle
    {
        /// <summary>No drawn reticle (irons, red dots).</summary>
        None,
        /// <summary>Etched duplex: thick outer posts, fine centre crosshair, illuminated centre dot.</summary>
        DuplexDot,
        /// <summary>Etched crosshair with an illuminated chevron (prism scopes).</summary>
        Chevron,
    }

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

        [Header("Reticle (drawn by the PiP lens shader)")]
        [SerializeField] private ReticleStyle reticle = ReticleStyle.None;
        [Tooltip("Illumination of the reticle centre in exposed units against a mid-grey scene; the lens scales it with the light in view (HDR: values above 1 bloom).")]
        [SerializeField, ColorUsage(false, true)] private Color reticleGlow = new Color(4f, 0.25f, 0.12f);
        [Tooltip("Reticle extent relative to the lens (1 = the duplex posts reach the rim).")]
        [SerializeField, Range(0.2f, 2f)] private float reticleScale = 1f;

        public float Magnification => magnification;
        public float EyeRelief => eyeRelief;
        public float AdsZoom => adsZoom;
        public Renderer LensRenderer => lensRenderer;
        public float EyeBoxRadius => eyeBoxRadius;
        public float EyeReliefTolerance => eyeReliefTolerance;
        public bool IsMagnified => magnification > 1.01f && lensRenderer != null;
        public ReticleStyle Reticle => reticle;
        public Color ReticleGlow => reticleGlow;
        public float ReticleScale => reticleScale;

        /// <summary>Sets the drawn reticle (editor builders and tests).</summary>
        public void ConfigureReticle(ReticleStyle style, Color glow, float scale)
        {
            reticle = style;
            reticleGlow = glow;
            reticleScale = Mathf.Clamp(scale, 0.2f, 2f);
        }

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
