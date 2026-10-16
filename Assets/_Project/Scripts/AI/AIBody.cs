using System;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.AI
{
    /// <summary>
    /// Procedural soldier body on a <see cref="NavMeshAgent"/>: stance height, lean roll about the pelvis, body yaw that
    /// follows the aim (or the walk), an aim pivot at the shoulder that swings the weapon with a turn-rate limit and
    /// absorbs recoil, head look, two-bone IK arms on the weapon grips, and a falling death. Writes Pelvis, Torso, Head,
    /// AimPivot and the arm bones; the brain only sets intents.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AIBody : MonoBehaviour
    {
        [Serializable]
        public struct Arm
        {
            public Transform upper;
            public Transform lower;
            public Transform hand;
            [Tooltip("Elbow pole in Torso space (m).")]
            public Vector3 elbowHint;
        }

        public const float StandingEyeHeight = 1.65f;
        public const float CrouchedEyeHeight = 1.1f;
        /// <summary>Sideways head travel at full lean (m).</summary>
        public const float LeanReach = 0.35f;

        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform head;
        [SerializeField] private Transform aimPivot;
        [SerializeField] private Transform legs;
        [SerializeField] private Arm leftArm;
        [SerializeField] private Arm rightArm;

        [Header("Posture")]
        [SerializeField] private float standingPelvis = 1f;
        [SerializeField] private float crouchedPelvis = 0.45f;
        [SerializeField, Min(0.1f)] private float postureSpeed = 2.5f;
        [SerializeField, Range(0f, 40f)] private float leanAngle = 22f;
        [SerializeField] private float leanShift = 0.1f;
        [SerializeField, Min(0.1f)] private float leanSpeed = 4f;

        [Header("Turning")]
        [SerializeField, Min(10f)] private float bodyTurnSpeed = 240f;
        [SerializeField, Min(10f)] private float aimTurnSpeed = 280f;
        [Tooltip("Pitch of the weapon at the low ready (deg, down).")]
        [SerializeField] private float lowReadyPitch = 30f;
        [SerializeField, Min(0.1f)] private float recoilRecovery = 9f;

        private NavMeshAgent agent;
        private WeaponModelRig weapon;
        private Vector3 aimPoint;
        private bool aiming;
        private bool lookOnly;
        private Quaternion aimRotation = Quaternion.identity;
        private Vector2 recoil;
        private float pelvisHeight, lean, leanTarget;
        private Transform droppedWeapon;

        public Transform Pelvis => pelvis;
        public Transform Torso => torso;
        public Transform Chest => chest;
        public Transform Head => head;
        public Transform AimPivot => aimPivot;
        public WeaponModelRig Weapon => weapon;
        public bool IsCrouched { get; private set; }
        public bool IsDead { get; private set; }
        public float Lean => lean;
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        public NavMeshAgent Agent => agent;

        /// <summary>Muzzle (or aim pivot) transform the weapon fires from.</summary>
        public Transform Muzzle => weapon != null && weapon.MuzzleSocket != null ? weapon.MuzzleSocket : aimPivot;

        public void Configure(Transform pelvisBone, Transform torsoBone, Transform chestBone, Transform headBone, Transform aim, Transform legRoot,
            Arm left, Arm right)
        {
            pelvis = pelvisBone;
            torso = torsoBone;
            chest = chestBone;
            head = headBone;
            aimPivot = aim;
            legs = legRoot;
            leftArm = left;
            rightArm = right;
        }

        public void SetCrouched(bool crouched) => IsCrouched = crouched;

        /// <summary>-1 leans left, +1 right, 0 upright.</summary>
        public void SetLean(float amount) => leanTarget = Mathf.Clamp(amount, -1f, 1f);

        /// <summary>Shoulders the weapon and swings it onto <paramref name="point"/>; the body turns to follow.</summary>
        public void AimAt(Vector3 point)
        {
            aimPoint = point;
            aiming = true;
            lookOnly = false;
        }

        /// <summary>Turns head and body toward <paramref name="point"/> with the weapon at the low ready.</summary>
        public void LookAt(Vector3 point)
        {
            aimPoint = point;
            aiming = true;
            lookOnly = true;
        }

        /// <summary>Low ready, facing the direction of travel.</summary>
        public void ClearAim() => aiming = false;

        public bool IsShouldered => aiming && !lookOnly;

        /// <summary>Angle between the bore and the line from the muzzle to <paramref name="point"/> (deg).</summary>
        public float AimError(Vector3 point)
        {
            Transform muzzle = Muzzle;
            return Vector3.Angle(muzzle.forward, point - muzzle.position);
        }

        public void AddRecoil(float pitchDegrees, float yawDegrees) => recoil += new Vector2(pitchDegrees, yawDegrees);

        /// <summary>Holds <paramref name="weaponInstance"/> with its stock pivot at the shoulder (aim pivot origin).</summary>
        public void HoldWeapon(WeaponModelRig weaponInstance)
        {
            weapon = weaponInstance;
            Transform weaponTransform = weapon.transform;
            weaponTransform.SetParent(aimPivot, false);
            weaponTransform.localRotation = Quaternion.identity;
            Vector3 stock = weapon.StockPivotSocket != null ? weaponTransform.InverseTransformPoint(weapon.StockPivotSocket.position) : Vector3.zero;
            weaponTransform.localPosition = -stock;
        }

        /// <summary>Collapses the body (hit along <paramref name="direction"/>) and drops the weapon.</summary>
        public void Die(Vector3 direction)
        {
            if (IsDead) return;
            IsDead = true;
            if (agent != null) agent.enabled = false;

            if (weapon != null)
            {
                Transform dropped = weapon.transform;
                dropped.SetParent(null, true);
                SetLayer(dropped, GameLayers.Debris);
                var box = dropped.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0f, 0.1f);
                box.size = new Vector3(0.06f, 0.2f, 0.8f);
                var weaponBody = dropped.gameObject.AddComponent<Rigidbody>();
                weaponBody.mass = 3.5f;
                weaponBody.linearVelocity = direction * 1.5f + Vector3.up;
                droppedWeapon = dropped;
                weapon = null;
            }

            var body = gameObject.AddComponent<Rigidbody>();
            body.mass = 80f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.AddForceAtPosition(direction.normalized * 160f, head != null ? head.position : transform.position + Vector3.up * 1.5f, ForceMode.Impulse);
        }

        /// <summary>The dropped rifle goes when the body is cleared away.</summary>
        private void OnDestroy()
        {
            if (droppedWeapon != null) Destroy(droppedWeapon.gameObject);
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
            pelvisHeight = standingPelvis;
            aimRotation = transform.rotation * Quaternion.Euler(lowReadyPitch, 0f, 0f);
        }

        private void LateUpdate()
        {
            if (IsDead) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdatePosture(dt);
            UpdateFacing(dt);
            UpdateAim(dt);
            UpdateArms();
        }

        private void UpdatePosture(float dt)
        {
            pelvisHeight = Mathf.MoveTowards(pelvisHeight, IsCrouched ? crouchedPelvis : standingPelvis, postureSpeed * dt);
            pelvis.localPosition = new Vector3(0f, pelvisHeight, 0f);
            if (legs != null) legs.localScale = new Vector3(1f, pelvisHeight / standingPelvis, 1f);

            lean = Mathf.MoveTowards(lean, leanTarget, leanSpeed * dt);
            torso.localPosition = new Vector3(lean * leanShift, 0f, 0f);
            torso.localRotation = Quaternion.Euler(0f, 0f, -lean * leanAngle);
        }

        private void UpdateFacing(float dt)
        {
            Vector3 facing;
            if (aiming) facing = aimPoint - transform.position;
            else
            {
                Vector3 velocity = Velocity;
                facing = velocity.sqrMagnitude > 0.04f ? velocity : transform.forward;
            }

            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), bodyTurnSpeed * dt);
        }

        private void UpdateAim(float dt)
        {
            Quaternion target;
            if (aiming && !lookOnly)
            {
                Vector3 toPoint = aimPoint - aimPivot.position;
                target = toPoint.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toPoint) : aimRotation;
            }
            else
            {
                Vector3 forward = transform.forward;
                target = Quaternion.LookRotation(forward) * Quaternion.Euler(lowReadyPitch, 0f, 0f);
            }

            aimRotation = Quaternion.RotateTowards(aimRotation, target, aimTurnSpeed * dt);
            recoil = Vector2.Lerp(recoil, Vector2.zero, 1f - Mathf.Exp(-recoilRecovery * dt));
            aimPivot.rotation = aimRotation * Quaternion.Euler(-recoil.x, recoil.y, 0f);

            Vector3 look = aiming ? aimPoint - head.position : transform.forward;
            if (look.sqrMagnitude > 1e-4f) head.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        private void UpdateArms()
        {
            if (weapon == null) return;
            if (weapon.RightHandIKGrip != null) Solve(rightArm, weapon.RightHandIKGrip);
            if (weapon.LeftHandIKGrip != null) Solve(leftArm, weapon.LeftHandIKGrip);
        }

        private void Solve(in Arm arm, Transform grip)
        {
            if (arm.upper == null || arm.lower == null || arm.hand == null) return;
            TwoBoneIKSolver.Solve(arm.upper, arm.lower, arm.hand, grip.position, grip.rotation, torso.TransformPoint(arm.elbowHint));
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
        }
    }
}
