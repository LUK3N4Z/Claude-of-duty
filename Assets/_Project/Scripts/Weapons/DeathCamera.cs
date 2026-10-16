using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// The player's death view. <see cref="Fall"/> hides the viewmodel, drops out of ADS (the optics controller restores
    /// the field of view and stops the scope), and lets the camera fall to the floor below the eye over
    /// <see cref="fallTime"/>, accelerating like a body going down, while it rolls onto one side and levels toward the
    /// floor; then it holds there in the world. <see cref="Restore"/> puts everything back for a respawn. Sole writer of
    /// the <see cref="PlayerRig.PlayerCamera"/> transform (identity under CameraOffset while alive); runs after every
    /// pivot above it so the world pose it sets is the one rendered.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ViewmodelRig))]
    public sealed class DeathCamera : MonoBehaviour
    {
        [Tooltip("Time for the view to reach the floor (s).")]
        [SerializeField, Min(0.05f)] private float fallTime = 0.8f;
        [Tooltip("Eye height above the floor when lying (m).")]
        [SerializeField, Min(0.05f)] private float restHeight = 0.22f;
        [Tooltip("Roll onto one side at rest (deg).")]
        [SerializeField, Range(0f, 90f)] private float rollDegrees = 28f;
        [Tooltip("Pitch at rest, below the horizon (deg).")]
        [SerializeField, Range(-30f, 60f)] private float restPitch = 8f;
        [Tooltip("Farthest the floor is searched for below the eye (m).")]
        [SerializeField, Min(0.5f)] private float floorProbe = 3f;

        private ViewmodelRig viewmodel;
        private ProceduralADSAndOpticsController ads;
        private Transform view;
        private Vector3 startPosition, restPosition;
        private Quaternion startRotation, restRotation;
        private float startTime;
        private bool adsWasEnabled;

        /// <summary>True from <see cref="Fall"/> until <see cref="Restore"/>.</summary>
        public bool IsActive { get; private set; }
        /// <summary>Progress of the fall, 0-1 (1 = lying still).</summary>
        public float Progress => IsActive ? Mathf.Clamp01((Time.time - startTime) / fallTime) : 0f;

        public void Fall()
        {
            if (IsActive || view == null) return;
            IsActive = true;
            startTime = Time.time;
            startPosition = view.position;
            startRotation = view.rotation;

            float floor = Physics.Raycast(startPosition, Vector3.down, out RaycastHit hit, floorProbe, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : transform.position.y;
            restPosition = new Vector3(startPosition.x, Mathf.Min(startPosition.y, floor + restHeight), startPosition.z);
            Vector3 heading = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            float yaw = heading.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(heading).eulerAngles.y : transform.eulerAngles.y;
            restRotation = Quaternion.Euler(restPitch, yaw, Random.value < 0.5f ? rollDegrees : -rollDegrees);

            viewmodel.ViewmodelRoot.gameObject.SetActive(false);
            adsWasEnabled = ads != null && ads.enabled;
            if (adsWasEnabled) ads.enabled = false;
        }

        public void Restore()
        {
            if (!IsActive) return;
            IsActive = false;
            view.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            viewmodel.ViewmodelRoot.gameObject.SetActive(true);
            if (adsWasEnabled) ads.enabled = true;
        }

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            TryGetComponent(out ads);
            Camera camera = GetComponent<PlayerRig>().PlayerCamera;
            if (camera != null) view = camera.transform;
        }

        private void LateUpdate()
        {
            if (!IsActive) return;
            float t = Progress;
            float drop = t * t; // gravity: slow off the feet, fast to the floor
            float turn = t * t * (3f - 2f * t);
            view.SetPositionAndRotation(Vector3.LerpUnclamped(startPosition, restPosition, drop),
                Quaternion.Slerp(startRotation, restRotation, turn));
        }
    }
}
