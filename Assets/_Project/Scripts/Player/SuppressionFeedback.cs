using FPS.Ballistics;
using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Locomotion
{
    /// <summary>
    /// Makes the player camera a near-miss listener and turns suppression into feedback: a head flinch away from the
    /// crack and a short focal blur of the world that fades as the suppression level decays.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig), typeof(HeadbobAndBreathingSystem))]
    public sealed class SuppressionFeedback : MonoBehaviour
    {
        [Tooltip("Suppression added per unit of event intensity.")]
        [SerializeField, Range(0f, 1f)] private float gainPerEvent = 0.6f;
        [Tooltip("Time for full suppression to fade (s).")]
        [SerializeField, Min(0.05f)] private float decayTime = 1.5f;
        [Tooltip("Blur volume weight at full suppression; subtle by design.")]
        [SerializeField, Range(0f, 1f)] private float maxBlurWeight = 0.6f;
        [SerializeField] private float blurFarStart = 1.5f;
        [SerializeField] private float blurFarEnd = 12f;

        private PlayerRig rig;
        private HeadbobAndBreathingSystem breathing;
        private Transform listener;
        private Volume blurVolume;
        private VolumeProfile blurProfile;

        /// <summary>Current suppression, 0-1.</summary>
        public float Level { get; private set; }

        private void Awake()
        {
            rig = GetComponent<PlayerRig>();
            breathing = GetComponent<HeadbobAndBreathingSystem>();
            listener = rig.PlayerCamera.transform;

            // Player layer: seen only by the main camera's volume mask (the PiP scope camera excludes it).
            var volumeObject = new GameObject("Suppression Blur") { layer = GameLayers.Player };
            volumeObject.transform.SetParent(transform, false);
            blurVolume = volumeObject.AddComponent<Volume>();
            blurVolume.isGlobal = true;
            blurVolume.priority = 90f;
            blurVolume.weight = 0f;
            blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            blurProfile.name = "Suppression Blur";
            blurVolume.sharedProfile = blurProfile;
            DepthOfField depthOfField = blurProfile.Add<DepthOfField>();
            depthOfField.focusMode.Override(DepthOfFieldMode.Manual);
            depthOfField.nearFocusStart.Override(0f);
            depthOfField.nearFocusEnd.Override(0f);
            depthOfField.farFocusStart.Override(blurFarStart);
            depthOfField.farFocusEnd.Override(blurFarEnd);
        }

        private void OnEnable()
        {
            NearMissListeners.Register(listener, transform);
            EventBus<SuppressionEvent>.Subscribe(OnSuppressed);
        }

        private void OnDisable()
        {
            NearMissListeners.Unregister(listener);
            EventBus<SuppressionEvent>.Unsubscribe(OnSuppressed);
            Level = 0f;
            blurVolume.weight = 0f;
        }

        private void OnDestroy() => Destroy(blurProfile);

        private void Update()
        {
            if (Level <= 0f) return;
            Level = Mathf.Max(0f, Level - Time.deltaTime / decayTime);
            blurVolume.weight = Level * maxBlurWeight;
        }

        private void OnSuppressed(in SuppressionEvent evt)
        {
            if (evt.Listener != listener) return;
            Level = Mathf.Clamp01(Level + evt.Intensity * gainPerEvent);
            blurVolume.weight = Level * maxBlurWeight;
            breathing.Flinch(evt.Point - listener.position, evt.Intensity);
        }
    }
}
