using System;
using FPS.Core;
using UnityEngine;

namespace FPS.Locomotion
{
    public enum Stance
    {
        Stand,
        Crouch,
        Prone,
    }

    public enum MountType
    {
        None,
        Ledge,
        Corner,
    }

    /// <summary>
    /// Body posture: stance capsule and eye height with clearance checks, prone ground alignment and body
    /// collision, collision-limited tactical lean, and contextual weapon mounting on ledges and corners.
    /// Driven by <see cref="RealisticLocomotionController"/>; owns BodyPivot, LeanPivot and HeadPivot.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(PlayerRig))]
    public sealed class StanceAndLeanController : MonoBehaviour
    {
        [Serializable]
        public struct StanceShape
        {
            [Tooltip("Capsule height (m).")] public float height;
            [Tooltip("Capsule radius (m).")] public float radius;
            [Tooltip("Eye height above the feet (m).")] public float eyeHeight;
            [Tooltip("Torso/lean pivot height above the feet (m).")] public float pivotHeight;
            [Tooltip("Max step the capsule climbs (m).")] public float stepOffset;

            public StanceShape(float height, float radius, float eyeHeight, float pivotHeight, float stepOffset)
            {
                this.height = height;
                this.radius = radius;
                this.eyeHeight = eyeHeight;
                this.pivotHeight = pivotHeight;
                this.stepOffset = stepOffset;
            }
        }

        private const float GroundSkin = 0.05f;
        private const float MinCornerLean = 3f;

        [Header("Stance Shapes")]
        [SerializeField] private StanceShape stand = new StanceShape(1.8f, 0.3f, 1.66f, 1.0f, 0.35f);
        [SerializeField] private StanceShape crouch = new StanceShape(1.15f, 0.3f, 1.02f, 0.6f, 0.3f);
        [SerializeField] private StanceShape prone = new StanceShape(0.4f, 0.2f, 0.28f, 0.2f, 0.1f);

        [Header("Transition Times (s)")]
        [SerializeField, Min(0.01f)] private float standCrouchTime = 0.28f;
        [SerializeField, Min(0.01f)] private float crouchProneTime = 0.65f;
        [SerializeField, Min(0.01f)] private float standProneTime = 0.9f;

        [Header("Prone Body")]
        [Tooltip("Body length ahead of the capsule centre: head, arms and weapon (m).")]
        [SerializeField] private float proneForwardExtent = 0.9f;
        [Tooltip("Body length behind the capsule centre: legs (m).")]
        [SerializeField] private float proneBackExtent = 0.8f;
        [SerializeField] private float proneBodyRadius = 0.18f;
        [SerializeField, Range(0f, 45f)] private float maxProneSlope = 30f;
        [Tooltip("How fast the body pitches to follow the ground (deg/s).")]
        [SerializeField] private float proneAlignSpeed = 90f;

        [Header("Lean")]
        [SerializeField, Range(0f, 30f)] private float maxLeanAngle = 18f;
        [SerializeField] private float leanSmoothTime = 0.16f;
        [Tooltip("Head sphere swept along the lean path; keeps the camera out of walls.")]
        [SerializeField] private float headClearanceRadius = 0.14f;

        [Header("Mounting")]
        [SerializeField] private float mountReach = 0.75f;
        [Tooltip("Weapon (handguard) height below the eye (m).")]
        [SerializeField] private float weaponDropBelowEye = 0.12f;
        [Tooltip("Eye height above a ledge the weapon rests on (m).")]
        [SerializeField] private float restEyeAboveLedge = 0.12f;
        [SerializeField] private float maxMountRaise = 0.15f;
        [SerializeField] private float maxMountLower = 0.32f;
        [Tooltip("Half spacing of the two forward rays that find a vertical edge (m).")]
        [SerializeField] private float cornerProbeHalfSpacing = 0.22f;
        [SerializeField] private float cornerClearance = 0.06f;
        [SerializeField, Range(0f, 1f)] private float mountedSwayMultiplier = 0.2f;
        [SerializeField, Range(0f, 1f)] private float mountedRecoilMultiplier = 0.55f;
        [Tooltip("Allowed (yaw, pitch) half-arc around the mount direction (deg).")]
        [SerializeField] private Vector2 ledgeArc = new Vector2(40f, 18f);
        [SerializeField] private Vector2 cornerArc = new Vector2(25f, 25f);
        [SerializeField] private float mountAdjustSmoothTime = 0.12f;

        private CharacterController controller;
        private PlayerRig rig;

        private Stance current = Stance.Stand;
        private Stance previous = Stance.Stand;
        private float transitionT = 1f;
        private float transitionDuration = 1f;
        private float fromEye, fromPivot, eyeBase, pivotBase;
        private float pronePitch;

        private int leanInput;
        private float leanAngle, leanVelocity;

        private MountType mount;
        private float mountYaw, mountPitch, mountLean;
        private float mountEyeOffset, mountEyeOffsetTarget, mountEyeVelocity;
        private Vector2 mountArc;

        public Stance Current => current;
        public bool IsTransitioning => transitionT < 1f;
        /// <summary>Getting down into, or up out of, prone. Movement and aiming are restricted meanwhile.</summary>
        public bool IsProneTransition => IsTransitioning && (current == Stance.Prone || previous == Stance.Prone);
        /// <summary>Animated eye height above the feet, including the mount adjustment (m).</summary>
        public float EyeHeight { get; private set; }
        /// <summary>Current lean roll in degrees; positive leans right.</summary>
        public float LeanAngle => leanAngle;
        public MountType Mount => mount;
        public bool IsMounted => mount != MountType.None;
        /// <summary>Multiplier for idle/optic sway: bracing removes most of it.</summary>
        public float SwayMultiplier => IsMounted ? mountedSwayMultiplier : 1f;
        /// <summary>Multiplier for recoil while braced.</summary>
        public float RecoilMultiplier => IsMounted ? mountedRecoilMultiplier : 1f;
        public float MountYaw => mountYaw;
        public float MountPitch => mountPitch;
        /// <summary>Allowed (yaw, pitch) half-arc around <see cref="MountYaw"/>/<see cref="MountPitch"/> while mounted.</summary>
        public Vector2 MountArc => mountArc;

        /// <summary>
        /// Changes stance if the target shape fits here. Lowering is always allowed; raising and prone check
        /// clearance first. Unmounts. Returns false (and changes nothing) when blocked.
        /// </summary>
        public bool TrySetStance(Stance target, float duration = -1f)
        {
            if (target == current) return true;
            if (!HasClearance(target, transform.position, transform.eulerAngles.y)) return false;

            Unmount();
            fromEye = eyeBase;
            fromPivot = pivotBase;
            previous = current;
            current = target;
            transitionDuration = duration > 0f ? duration : TransitionTime(previous, target);
            transitionT = 0f;
            ApplyCapsule(Shape(target));
            return true;
        }

        /// <summary>Capsule height of <paramref name="stance"/> (m).</summary>
        public float HeightOf(Stance stance) => Shape(stance).height;

        /// <summary>
        /// True if the standing or crouched capsule fits with its feet at <paramref name="feet"/> (traversal landing
        /// checks); prone checks the stretched-out body facing the current yaw.
        /// </summary>
        public bool FitsAt(Stance target, Vector3 feet)
        {
            if (target == Stance.Prone) return IsProneBodyClear(feet, transform.eulerAngles.y);
            StanceShape shape = Shape(target);
            float radius = shape.radius - 0.02f;
            return !Physics.CheckCapsule(feet + Vector3.up * (radius + GroundSkin), feet + Vector3.up * (shape.height - radius), radius,
                GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>True if the stretched-out prone body fits at <paramref name="position"/> facing <paramref name="yaw"/>.</summary>
        public bool IsProneBodyClear(Vector3 position, float yaw)
        {
            return TryGetProneBody(position, yaw, out Vector3 head, out Vector3 feet, out _) &&
                   !Physics.CheckCapsule(head, feet, proneBodyRadius, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>While prone, shortens a planned move so the head and legs never pass into geometry.</summary>
        public Vector3 LimitProneMove(Vector3 delta)
        {
            if (current != Stance.Prone) return delta;

            float distance = delta.magnitude;
            if (distance < 1e-5f) return delta;
            if (!TryGetProneBody(transform.position, transform.eulerAngles.y, out Vector3 head, out Vector3 feet, out _))
                return delta; // too steep to evaluate; let the capsule handle it so the player can crawl out

            Vector3 direction = delta / distance;
            if (Physics.CapsuleCast(head, feet, proneBodyRadius - 0.01f, direction, out RaycastHit hit, distance,
                    GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return direction * Mathf.Max(0f, hit.distance - 0.01f);

            return delta;
        }

        /// <summary>Lean request this frame: -1 left, 0 none, +1 right. Ignored while prone or mounted.</summary>
        public void SetLeanInput(int direction) => leanInput = Mathf.Clamp(direction, -1, 1);

        /// <summary>
        /// Tries to brace the weapon on a ledge in front (raising or lowering the eye to rest on it) or against a
        /// vertical corner edge (leaning out past it). Returns true when mounted.
        /// </summary>
        public bool TryMount(float yaw, float pitch)
        {
            if (IsMounted) return true;
            if (IsTransitioning || current == Stance.Prone) return false;

            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 eye = transform.position + Vector3.up * eyeBase;

            if (TryFindLedge(eye, forward, out float eyeOffset))
            {
                BeginMount(MountType.Ledge, yaw, pitch, ledgeArc);
                mountEyeOffsetTarget = eyeOffset;
                return true;
            }

            if (TryFindCorner(eye, forward, right, out float lean))
            {
                BeginMount(MountType.Corner, yaw, pitch, cornerArc);
                mountLean = lean;
                return true;
            }

            return false;
        }

        public void Unmount()
        {
            mount = MountType.None;
            mountLean = 0f;
            mountEyeOffsetTarget = 0f;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            rig = GetComponent<PlayerRig>();

            ApplyCapsule(stand);
            fromEye = eyeBase = EyeHeight = stand.eyeHeight;
            fromPivot = pivotBase = stand.pivotHeight;
            ApplyPose();
        }

        private void OnValidate()
        {
            // Keep the authored prefab in the standing pose so gizmos and scene view match play mode.
            if (Application.isPlaying) return;
            if (TryGetComponent(out controller)) ApplyCapsule(stand);
            if (TryGetComponent(out rig) && rig.BodyPivot != null && rig.HeadPivot != null)
            {
                eyeBase = EyeHeight = stand.eyeHeight;
                pivotBase = stand.pivotHeight;
                ApplyPose();
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (transitionT < 1f) transitionT = Mathf.Min(1f, transitionT + dt / transitionDuration);
            StanceShape shape = Shape(current);
            float k = Mathf.SmoothStep(0f, 1f, transitionT);
            eyeBase = Mathf.Lerp(fromEye, shape.eyeHeight, k);
            pivotBase = Mathf.Lerp(fromPivot, shape.pivotHeight, k);

            mountEyeOffset = Mathf.SmoothDamp(mountEyeOffset, IsMounted ? mountEyeOffsetTarget : 0f, ref mountEyeVelocity,
                mountAdjustSmoothTime, Mathf.Infinity, dt);
            EyeHeight = eyeBase + mountEyeOffset;

            float targetPitch = 0f;
            if (current == Stance.Prone &&
                TryGetProneBody(transform.position, transform.eulerAngles.y, out _, out _, out float groundPitch))
                targetPitch = groundPitch * k;
            pronePitch = Mathf.MoveTowards(pronePitch, targetPitch, proneAlignSpeed * dt);

            ApplyPose();
            UpdateLean(dt);
        }

        private void UpdateLean(float dt)
        {
            float target = IsMounted ? mountLean
                : current == Stance.Prone || IsProneTransition ? 0f
                : leanInput * maxLeanAngle;
            float allowed = CollisionLimitedLean(target);

            leanAngle = Mathf.SmoothDamp(leanAngle, allowed, ref leanVelocity, leanSmoothTime, Mathf.Infinity, dt);
            // Smoothing must never carry the head past the collision limit.
            if (allowed > 0f && leanAngle > allowed) leanAngle = allowed;
            else if (allowed < 0f && leanAngle < allowed) leanAngle = allowed;

            rig.LeanPivot.localRotation = Quaternion.Euler(0f, 0f, -leanAngle);
        }

        private float CollisionLimitedLean(float target)
        {
            if (Mathf.Abs(target) < 0.01f) return 0f;

            float lever = eyeBase - pivotBase;
            float radians = target * Mathf.Deg2Rad;
            Vector3 pivot = rig.LeanPivot.position;
            Vector3 up = transform.up;
            Vector3 from = pivot + up * lever;
            Vector3 to = pivot + (up * Mathf.Cos(radians) + transform.right * Mathf.Sin(radians)) * lever;
            Vector3 path = to - from;
            float distance = path.magnitude;

            if (Physics.SphereCast(from, headClearanceRadius, path / distance, out RaycastHit hit, distance,
                    GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return target * (hit.distance / distance); // chord fraction ≈ angle fraction for small leans

            return target;
        }

        private bool TryFindLedge(Vector3 eye, Vector3 forward, out float eyeOffset)
        {
            // Valid rest heights: eye can move by [-maxMountLower, +maxMountRaise] to sit restEyeAboveLedge above the top.
            float highest = eye.y - restEyeAboveLedge + maxMountRaise;
            float lowest = eye.y - restEyeAboveLedge - maxMountLower;
            float probeHeight = highest + 0.08f;
            float probeLength = probeHeight - lowest + 0.05f;

            for (int i = 1; i <= 4; i++)
            {
                float distance = mountReach * i / 4f;
                Vector3 origin = new Vector3(eye.x, probeHeight, eye.z) + forward * distance;

                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit top, probeLength, GameLayers.EnvironmentMask,
                        QueryTriggerInteraction.Ignore)) continue;
                if (top.normal.y < 0.8f || top.point.y < lowest || top.point.y > highest) continue;

                // The weapon must reach the ledge without passing through anything.
                Vector3 toOrigin = origin - eye;
                if (Physics.Raycast(eye, toOrigin, toOrigin.magnitude, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                    return Fail(out eyeOffset);

                // Open line of fire just above the ledge.
                Vector3 overLedge = top.point + Vector3.up * 0.08f;
                if (Physics.Raycast(overLedge, forward, 0.5f, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                    continue;

                eyeOffset = top.point.y + restEyeAboveLedge - eye.y;
                return true;
            }

            return Fail(out eyeOffset);
        }

        private bool TryFindCorner(Vector3 eye, Vector3 forward, Vector3 right, out float lean)
        {
            Vector3 weapon = eye - Vector3.up * weaponDropBelowEye;
            bool leftBlocked = Physics.Raycast(weapon - right * cornerProbeHalfSpacing, forward, mountReach,
                GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            bool rightBlocked = Physics.Raycast(weapon + right * cornerProbeHalfSpacing, forward, mountReach,
                GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            if (leftBlocked == rightBlocked) return Fail(out lean); // flat wall or open space: no vertical edge

            // Binary-search the edge between the blocked and the open ray (lateral offset, right positive).
            float openSide = leftBlocked ? 1f : -1f;
            float blocked = -cornerProbeHalfSpacing * openSide;
            float open = cornerProbeHalfSpacing * openSide;
            for (int i = 0; i < 5; i++)
            {
                float mid = (blocked + open) * 0.5f;
                if (Physics.Raycast(weapon + right * mid, forward, mountReach, GameLayers.EnvironmentMask,
                        QueryTriggerInteraction.Ignore))
                    blocked = mid;
                else
                    open = mid;
            }

            // Lean out until the eye clears the edge, bracing the weapon against it.
            float needed = open * openSide + cornerClearance;
            float lever = Mathf.Max(0.05f, eyeBase - pivotBase);
            float sine = Mathf.Clamp(needed / lever, Mathf.Sin(MinCornerLean * Mathf.Deg2Rad), Mathf.Sin(maxLeanAngle * Mathf.Deg2Rad));
            lean = openSide * Mathf.Asin(sine) * Mathf.Rad2Deg;
            return true;
        }

        private bool TryGetProneBody(Vector3 position, float yaw, out Vector3 head, out Vector3 feet, out float pitch)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float headGround = GroundHeightAt(position + forward * proneForwardExtent, position.y);
            float feetGround = GroundHeightAt(position - forward * proneBackExtent, position.y);
            pitch = Mathf.Atan2(feetGround - headGround, proneForwardExtent + proneBackExtent) * Mathf.Rad2Deg;

            float lift = proneBodyRadius + GroundSkin;
            head = position + forward * (proneForwardExtent - proneBodyRadius);
            head.y = headGround + lift;
            feet = position - forward * (proneBackExtent - proneBodyRadius);
            feet.y = feetGround + lift;
            return Mathf.Abs(pitch) <= maxProneSlope;
        }

        private static float GroundHeightAt(Vector3 point, float fallbackY)
        {
            var origin = new Vector3(point.x, fallbackY + 0.5f, point.z);
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1f, GameLayers.EnvironmentMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : fallbackY; // overhang: the body end hangs over a drop
        }

        private bool HasClearance(Stance target, Vector3 position, float yaw)
        {
            if (target == Stance.Prone) return IsProneBodyClear(position, yaw);

            StanceShape shape = Shape(target);
            if (shape.height <= controller.height + 0.001f) return true; // lowering always fits

            float radius = shape.radius - 0.02f;
            Vector3 bottom = position + Vector3.up * (radius + GroundSkin);
            Vector3 top = position + Vector3.up * (shape.height - radius);
            return !Physics.CheckCapsule(bottom, top, radius, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        private void BeginMount(MountType type, float yaw, float pitch, Vector2 arc)
        {
            mount = type;
            mountYaw = yaw;
            mountPitch = pitch;
            mountArc = arc;
            mountLean = 0f;
            mountEyeOffsetTarget = 0f;
        }

        private void ApplyCapsule(StanceShape shape)
        {
            controller.radius = Mathf.Min(shape.radius, shape.height * 0.5f);
            controller.height = shape.height;
            controller.center = new Vector3(0f, shape.height * 0.5f, 0f);
            controller.stepOffset = Mathf.Min(shape.stepOffset, shape.height);
        }

        private void ApplyPose()
        {
            rig.BodyPivot.localPosition = new Vector3(0f, pivotBase, 0f);
            rig.BodyPivot.localRotation = Quaternion.Euler(pronePitch, 0f, 0f);
            rig.HeadPivot.localPosition = new Vector3(0f, EyeHeight - pivotBase, 0f);
        }

        private StanceShape Shape(Stance s) => s == Stance.Stand ? stand : s == Stance.Crouch ? crouch : prone;

        private float TransitionTime(Stance a, Stance b)
        {
            if (a == Stance.Prone || b == Stance.Prone)
                return a == Stance.Stand || b == Stance.Stand ? standProneTime : crouchProneTime;
            return standCrouchTime;
        }

        private static bool Fail(out float value)
        {
            value = 0f;
            return false;
        }
    }
}
