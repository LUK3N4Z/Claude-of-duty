using System;
using FPS.Core;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// Two-bone IK for first-person arms: the firing hand holds the weapon's RightHandIKGrip, the support hand follows
    /// the controller's target (handguard grip, magazine during reloads and checks, charging handle when cycling).
    /// Runs after every viewmodel pivot so the hands land on the final weapon pose.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ViewmodelRig))]
    public sealed class FirstPersonArmsIK : MonoBehaviour
    {
        [Serializable]
        public struct ArmChain
        {
            public Transform upper;
            public Transform lower;
            public Transform hand;
            [Tooltip("Elbow pole in ViewmodelRoot space (m).")]
            public Vector3 elbowHint;
        }

        public const string LeftArmFieldName = nameof(leftArm);
        public const string RightArmFieldName = nameof(rightArm);

        [SerializeField] private ArmChain leftArm = new ArmChain { elbowHint = new Vector3(-0.45f, -0.6f, 0.05f) };
        [SerializeField] private ArmChain rightArm = new ArmChain { elbowHint = new Vector3(0.45f, -0.6f, 0f) };
        [Tooltip("How quickly the support hand travels between grip, magazine and charging handle (1/s).")]
        [SerializeField] private float supportHandSpeed = 12f;

        private ViewmodelRig viewmodel;
        private RealisticWeaponController controller;
        private Vector3 supportPosition;
        private Quaternion supportRotation = Quaternion.identity;
        private bool supportPlaced;

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            controller = GetComponent<RealisticWeaponController>();
        }

        private void OnEnable() => viewmodel.WeaponChanged += OnWeaponChanged;

        private void OnDisable() => viewmodel.WeaponChanged -= OnWeaponChanged;

        private void OnWeaponChanged() => supportPlaced = false;

        private void LateUpdate()
        {
            WeaponModelRig weapon = viewmodel.Weapon;
            if (weapon == null) return;

            Transform space = viewmodel.ViewmodelRoot;
            Transform support = controller != null && controller.LeftHandTarget != null ? controller.LeftHandTarget : weapon.LeftHandIKGrip;
            if (support != null)
            {
                // Travel in view space so turning the head never drags the hand behind.
                Vector3 position = space.InverseTransformPoint(support.position);
                Quaternion rotation = Quaternion.Inverse(space.rotation) * support.rotation;
                float k = supportPlaced ? 1f - Mathf.Exp(-supportHandSpeed * Time.deltaTime) : 1f;
                supportPosition = Vector3.Lerp(supportPosition, position, k);
                supportRotation = Quaternion.Slerp(supportRotation, rotation, k);
                supportPlaced = true;
                Solve(leftArm, space.TransformPoint(supportPosition), space.rotation * supportRotation, space);
            }

            if (weapon.RightHandIKGrip != null)
                Solve(rightArm, weapon.RightHandIKGrip.position, weapon.RightHandIKGrip.rotation, space);
        }

        private static void Solve(in ArmChain arm, Vector3 position, Quaternion rotation, Transform space)
        {
            if (arm.upper == null || arm.lower == null || arm.hand == null) return;
            TwoBoneIKSolver.Solve(arm.upper, arm.lower, arm.hand, position, rotation, space.TransformPoint(arm.elbowHint));
        }
    }
}
