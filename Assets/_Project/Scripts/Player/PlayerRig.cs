using System;
using FPS.Core;
using UnityEngine;

namespace FPS.Locomotion
{
    /// <summary>
    /// Socket hub of the first-person player: the input source and the transform chain the player systems drive.
    /// Each transform has exactly one writer:
    /// BodyPivot (stance: torso height, prone pitch) → LeanPivot (stance: lean roll) → HeadPivot (stance: eye offset)
    /// → PitchPivot (locomotion: look pitch) → HeadMotion (headbob and breathing) → PlayerCamera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRig : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private Transform bodyPivot;
        [SerializeField] private Transform leanPivot;
        [SerializeField] private Transform headPivot;
        [SerializeField] private Transform pitchPivot;
        [SerializeField] private Transform headMotion;
        [SerializeField] private Camera playerCamera;

        public InputReader Input => input;
        public Transform BodyPivot => bodyPivot;
        public Transform LeanPivot => leanPivot;
        public Transform HeadPivot => headPivot;
        public Transform PitchPivot => pitchPivot;
        public Transform HeadMotion => headMotion;
        public Camera PlayerCamera => playerCamera;

        /// <summary>
        /// Builds, or completes, a full first-person player on <paramref name="root"/>: the pivot chain, camera,
        /// CharacterController and all locomotion systems. Idempotent; existing children and components are kept.
        /// </summary>
        public static PlayerRig Build(GameObject root, InputReader input)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (input == null) throw new ArgumentNullException(nameof(input));

            bool wasActive = root.activeSelf;
            root.SetActive(false); // systems wake only once everything is wired
            root.layer = GameLayers.Player;

            if (!root.TryGetComponent(out PlayerRig rig)) rig = root.AddComponent<PlayerRig>();
            rig.input = input;
            rig.bodyPivot = Child(root.transform, "BodyPivot");
            rig.leanPivot = Child(rig.bodyPivot, "LeanPivot");
            rig.headPivot = Child(rig.leanPivot, "HeadPivot");
            rig.pitchPivot = Child(rig.headPivot, "PitchPivot");
            rig.headMotion = Child(rig.pitchPivot, "HeadMotion");

            Transform cameraTransform = Child(rig.headMotion, "PlayerCamera");
            if (!cameraTransform.TryGetComponent(out rig.playerCamera))
            {
                rig.playerCamera = cameraTransform.gameObject.AddComponent<Camera>();
                rig.playerCamera.nearClipPlane = 0.03f;
                rig.playerCamera.fieldOfView = 75f;
                cameraTransform.gameObject.AddComponent<AudioListener>();
            }
            cameraTransform.tag = "MainCamera";

            if (!root.TryGetComponent(out CharacterController controller)) controller = root.AddComponent<CharacterController>();
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.03f;
            controller.minMoveDistance = 0f;

            if (!root.TryGetComponent(out StanceAndLeanController _)) root.AddComponent<StanceAndLeanController>();
            if (!root.TryGetComponent(out RealisticLocomotionController _)) root.AddComponent<RealisticLocomotionController>();
            if (!root.TryGetComponent(out HeadbobAndBreathingSystem _)) root.AddComponent<HeadbobAndBreathingSystem>();

            root.SetActive(wasActive);
            return rig;
        }

        private static Transform Child(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(parent, false);
            }

            child.gameObject.layer = parent.gameObject.layer;
            return child;
        }
    }
}
