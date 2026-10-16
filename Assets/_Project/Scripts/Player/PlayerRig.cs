using System;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using UnityEngine;

namespace FPS.Locomotion
{
    /// <summary>
    /// Socket hub of the first-person player: the input source and the transform chain the player systems drive.
    /// Each transform has exactly one writer:
    /// BodyPivot (stance: torso height, prone pitch) → LeanPivot (stance: lean roll) → HeadPivot (stance: eye offset)
    /// → PitchPivot (locomotion: look pitch) → HeadMotion (headbob and breathing) → CameraOffset (weapon recoil
    /// concussion) → PlayerCamera. The weapon viewmodel hangs off HeadMotion beside CameraOffset.
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
        [SerializeField] private Transform cameraOffset;
        [SerializeField] private Camera playerCamera;

        public InputReader Input => input;
        public Transform BodyPivot => bodyPivot;
        public Transform LeanPivot => leanPivot;
        public Transform HeadPivot => headPivot;
        public Transform PitchPivot => pitchPivot;
        public Transform HeadMotion => headMotion;
        public Transform CameraOffset => cameraOffset;
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
            rig.bodyPivot = EnsureChild(root.transform, "BodyPivot");
            rig.leanPivot = EnsureChild(rig.bodyPivot, "LeanPivot");
            rig.headPivot = EnsureChild(rig.leanPivot, "HeadPivot");
            rig.pitchPivot = EnsureChild(rig.headPivot, "PitchPivot");
            rig.headMotion = EnsureChild(rig.pitchPivot, "HeadMotion");
            rig.cameraOffset = EnsureChild(rig.headMotion, "CameraOffset");

            Transform legacyCamera = rig.headMotion.Find("PlayerCamera"); // rigs built before CameraOffset existed
            if (legacyCamera != null) legacyCamera.SetParent(rig.cameraOffset, false);
            Transform cameraTransform = EnsureChild(rig.cameraOffset, "PlayerCamera");
            if (!cameraTransform.TryGetComponent(out rig.playerCamera))
            {
                rig.playerCamera = cameraTransform.gameObject.AddComponent<Camera>();
                rig.playerCamera.nearClipPlane = 0.02f;
                rig.playerCamera.fieldOfView = 75f;
                cameraTransform.gameObject.AddComponent<AudioListener>();
            }
            cameraTransform.tag = "MainCamera";

            if (!root.TryGetComponent(out CharacterController controller)) controller = root.AddComponent<CharacterController>();
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.03f;
            controller.minMoveDistance = 0f;

            if (!root.TryGetComponent(out TacticalHealthAndArmorSystem _)) root.AddComponent<TacticalHealthAndArmorSystem>();
            if (!root.TryGetComponent(out StanceAndLeanController _)) root.AddComponent<StanceAndLeanController>();
            if (!root.TryGetComponent(out MantleAndVaultController _)) root.AddComponent<MantleAndVaultController>();
            if (!root.TryGetComponent(out RealisticLocomotionController _)) root.AddComponent<RealisticLocomotionController>();
            if (!root.TryGetComponent(out HeadbobAndBreathingSystem _)) root.AddComponent<HeadbobAndBreathingSystem>();
            if (!root.TryGetComponent(out SuppressionFeedback _)) root.AddComponent<SuppressionFeedback>();
            if (!root.TryGetComponent(out FlashbangFeedback _)) root.AddComponent<FlashbangFeedback>();
            if (!root.TryGetComponent(out Interactor _)) root.AddComponent<Interactor>();
            if (!root.TryGetComponent(out AcousticPropagationSystem _)) root.AddComponent<AcousticPropagationSystem>();
            if (!root.TryGetComponent(out SupersonicCrackEmitter _)) root.AddComponent<SupersonicCrackEmitter>();
            if (!root.TryGetComponent(out ImpactEffectsController _)) root.AddComponent<ImpactEffectsController>();

            // Body zones for incoming fire: torso and arms lean with LeanPivot, legs follow the stance/prone pivot.
            EnsureHitbox(rig.headPivot, "Hitbox Head", HitZone.Head, new Vector3(0f, 0.04f, -0.02f), new Vector3(0.2f, 0.24f, 0.22f));
            EnsureHitbox(rig.leanPivot, "Hitbox Thorax", HitZone.Thorax, new Vector3(0f, 0.36f, 0f), new Vector3(0.38f, 0.3f, 0.24f));
            EnsureHitbox(rig.leanPivot, "Hitbox Stomach", HitZone.Stomach, new Vector3(0f, 0.1f, 0f), new Vector3(0.34f, 0.22f, 0.22f));
            EnsureHitbox(rig.leanPivot, "Hitbox Left Arm", HitZone.LeftArm, new Vector3(-0.25f, 0.3f, 0.05f), new Vector3(0.1f, 0.4f, 0.12f));
            EnsureHitbox(rig.leanPivot, "Hitbox Right Arm", HitZone.RightArm, new Vector3(0.25f, 0.3f, 0.05f), new Vector3(0.1f, 0.4f, 0.12f));
            EnsureHitbox(rig.bodyPivot, "Hitbox Left Leg", HitZone.LeftLeg, new Vector3(-0.1f, -0.5f, 0f), new Vector3(0.15f, 0.95f, 0.17f));
            EnsureHitbox(rig.bodyPivot, "Hitbox Right Leg", HitZone.RightLeg, new Vector3(0.1f, -0.5f, 0f), new Vector3(0.15f, 0.95f, 0.17f));

            root.SetActive(wasActive);
            return rig;
        }

        private static void EnsureHitbox(Transform parent, string hitboxName, HitZone zone, Vector3 center, Vector3 size)
        {
            Transform hitboxTransform = EnsureChild(parent, hitboxName);
            hitboxTransform.gameObject.layer = GameLayers.Hitbox;
            hitboxTransform.localPosition = center;
            if (!hitboxTransform.TryGetComponent(out BoxCollider box)) box = hitboxTransform.gameObject.AddComponent<BoxCollider>();
            box.size = size;
            if (!hitboxTransform.TryGetComponent(out Hitbox hitbox)) hitbox = hitboxTransform.gameObject.AddComponent<Hitbox>();
            hitbox.Configure(zone, null);
        }

        /// <summary>Finds a direct child by name or creates it at identity; the child inherits the parent's layer.</summary>
        public static Transform EnsureChild(Transform parent, string childName)
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
