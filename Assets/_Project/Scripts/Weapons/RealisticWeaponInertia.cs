using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// The weapon's physical connection to the body: mass/ergonomics-driven spring handling, free-aim lead inside an
    /// elastic deadzone, movement trail and sprint/slide postures, breathing sway, and barrel collision that blends from
    /// the shouldered weapon through a compressed ready (pulled back, stock under the armpit: still fires, no sights) to
    /// a high or low ready full tuck near walls, which holds the firing hand at a point in view so part of the weapon
    /// stays in frame. Rotates about the stock contact point. Owns InertiaPivot.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ViewmodelRig), typeof(HeadbobAndBreathingSystem))]
    public sealed class RealisticWeaponInertia : MonoBehaviour
    {
        [Header("Handling Spring")]
        [SerializeField] private ProceduralSpringSolver.Tuning positionSpring = new ProceduralSpringSolver.Tuning(9f, 0.65f);
        [SerializeField] private ProceduralSpringSolver.Tuning rotationSpring = new ProceduralSpringSolver.Tuning(6.5f, 0.5f);
        [Tooltip("Weapon mass at which the springs run at their authored frequency (kg).")]
        [SerializeField, Min(0.1f)] private float referenceMassKg = 3.4f;

        [Header("Free-Aim Lead")]
        [Tooltip("The weapon leads the view by angular velocity × this (s).")]
        [SerializeField] private float freeAimLeadTime = 0.04f;
        [Tooltip("Elastic deadzone half-extents (pitch, yaw) at the hip (deg).")]
        [SerializeField] private Vector2 freeAimRadius = new Vector2(2.5f, 4f);
        [SerializeField, Range(0f, 1f)] private float adsFreeAimMultiplier = 0.25f;
        [SerializeField, Range(0f, 1f)] private float mountedFreeAimMultiplier = 0.1f;

        [Header("Movement")]
        [Tooltip("Weapon trail per m/s of body velocity (m).")]
        [SerializeField] private float movementTrail = 0.005f;
        [SerializeField] private float maxTrail = 0.03f;
        [Tooltip("Roll per m/s of strafing (deg).")]
        [SerializeField] private float strafeRoll = 1.5f;

        [Header("Postures (m, deg)")]
        [SerializeField] private Vector3 sprintPosition = new Vector3(-0.03f, -0.05f, -0.04f);
        [SerializeField] private Vector3 sprintEuler = new Vector3(12f, -25f, 18f);
        [Tooltip("High-ready carry for tactical sprint.")]
        [SerializeField] private Vector3 tacticalSprintPosition = new Vector3(-0.05f, 0.03f, -0.1f);
        [SerializeField] private Vector3 tacticalSprintEuler = new Vector3(-50f, -12f, 20f);
        [SerializeField] private float postureBlendSpeed = 4f;

        [Header("Barrel Collision")]
        [SerializeField] private float barrelRadius = 0.025f;
        [Tooltip("How far the weapon slides back before it starts tilting (m).")]
        [SerializeField] private float maxCompression = 0.16f;
        [Tooltip("Further penetration over which the tilt goes from 0 to full (m).")]
        [SerializeField] private float tiltRange = 0.25f;
        [Tooltip("Barrels at least this long drop to a low ready; shorter ones rise to a high ready (in).")]
        [SerializeField] private float lowReadyBarrelInches = 16f;
        [Tooltip("Full high ready: the firing hand (RightHandIKGrip) at the target, muzzle up along the right edge of the view.")]
        [SerializeField] private ViewPose highReady = new ViewPose(new Vector3(0.22f, -0.3f, 0.22f), new Vector3(-80f, 10f, 30f));
        [Tooltip("Full low ready: the firing hand (RightHandIKGrip) at the target, muzzle down and inboard across the lower right, the stock out past the right edge.")]
        [SerializeField] private ViewPose lowReady = new ViewPose(new Vector3(0.2f, -0.2f, 0.2f), new Vector3(25f, -30f, 30f));
        [SerializeField] private float tuckInSpeed = 2.5f;
        [SerializeField] private float tuckOutSpeed = 0.8f;
        [Tooltip("Tilt (0-1) beyond which the muzzle is off target: firing and aiming are blocked.")]
        [SerializeField, Range(0f, 1f)] private float tuckedTilt = 0.25f;

        [Header("Compressed Ready")]
        [Tooltip("Pose added at full compression: stock off the shoulder, tucked under the armpit (m).")]
        [SerializeField] private Vector3 compressedPosition = new Vector3(0.02f, -0.06f, -0.04f);
        [SerializeField] private Vector3 compressedEuler = new Vector3(-4f, 8f, 14f);
        [Tooltip("Stock-to-muzzle length that takes the full compressed posture; shorter weapons (no stock to tuck) take a share (m).")]
        [SerializeField, Min(0.05f)] private float compressedPostureLength = 0.6f;

        private readonly ProceduralSpringSolver spring = new ProceduralSpringSolver();
        private ViewmodelRig viewmodel;
        private RealisticLocomotionController locomotion;
        private StanceAndLeanController stance;
        private HeadbobAndBreathingSystem breathing;
        private ProceduralADSAndOpticsController ads;
        private float lastYaw, lastPitch;
        private float sprintBlend, tacticalBlend, tuckDepth;

        /// <summary>0 = free, 1 = fully tucked against geometry.</summary>
        public float TuckAmount { get; private set; }
        /// <summary>Compressed ready, 0 (shouldered) to 1 (pulled back, stock under the arm).</summary>
        public float CompressedReady01 { get; private set; }
        /// <summary>High/low ready tilt, 0-1 (1 = full tuck).</summary>
        public float Tilt01 { get; private set; }
        /// <summary>In a compressed ready: hip fire only, the sights are off the eye.</summary>
        public bool IsCompressed => CompressedReady01 > 0.5f && !IsTucked;
        /// <summary>Tucked far enough that aiming and firing are blocked.</summary>
        public bool IsTucked => Tilt01 >= tuckedTilt;

        /// <summary>Clamps <paramref name="value"/> into the ellipse with half-extents <paramref name="radius"/>.</summary>
        public static Vector2 ClampToEllipse(Vector2 value, Vector2 radius)
        {
            if (radius.x <= 0f || radius.y <= 0f) return Vector2.zero;

            float x = value.x / radius.x;
            float y = value.y / radius.y;
            float extent = x * x + y * y;
            return extent <= 1f ? value : value / Mathf.Sqrt(extent);
        }

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            locomotion = GetComponent<RealisticLocomotionController>();
            stance = GetComponent<StanceAndLeanController>();
            breathing = GetComponent<HeadbobAndBreathingSystem>();
            ads = GetComponent<ProceduralADSAndOpticsController>();
        }

        private void OnEnable()
        {
            viewmodel.WeaponChanged += OnWeaponChanged;
            lastYaw = locomotion.Yaw;
            lastPitch = locomotion.Pitch;
        }

        private void OnDisable() => viewmodel.WeaponChanged -= OnWeaponChanged;

        private void OnWeaponChanged()
        {
            spring.Reset();
            tuckDepth = 0f;
            TuckAmount = CompressedReady01 = Tilt01 = 0f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float yawRate = Mathf.DeltaAngle(lastYaw, locomotion.Yaw) / dt;
            float pitchRate = (locomotion.Pitch - lastPitch) / dt;
            lastYaw = locomotion.Yaw;
            lastPitch = locomotion.Pitch;

            Transform pivot = viewmodel.InertiaPivot;
            WeaponModelRig weapon = viewmodel.Weapon;
            WeaponDataSO data = viewmodel.Data;
            if (weapon == null || data == null)
            {
                pivot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                return;
            }

            // Rotational inertia about the shoulder sets the felt torque: long barrels and suppressors swing slower.
            float referenceInertia = referenceMassKg * WeaponDataSO.TypicalGyrationRadius * WeaponDataSO.TypicalGyrationRadius;
            float handling = Mathf.Lerp(0.65f, 1.2f, data.Ergonomics / 100f) / Mathf.Sqrt(data.MomentOfInertia / referenceInertia);
            spring.PositionTuning = new ProceduralSpringSolver.Tuning(positionSpring.frequency * handling, positionSpring.dampingRatio);
            spring.RotationTuning = new ProceduralSpringSolver.Tuning(rotationSpring.frequency * handling, rotationSpring.dampingRatio);

            // The arms swing the weapon ahead of the turn before the shoulders catch up.
            float freeAim = Mathf.Lerp(1f, adsFreeAimMultiplier, ads.AdsBlend) * (stance.IsMounted ? mountedFreeAimMultiplier : 1f);
            Vector2 lead = ClampToEllipse(new Vector2(pitchRate, yawRate) * freeAimLeadTime, freeAimRadius * freeAim);

            Vector3 localVelocity = transform.InverseTransformDirection(locomotion.Velocity);
            Vector3 trail = Vector3.ClampMagnitude(
                new Vector3(-localVelocity.x, -localVelocity.y * 0.5f, -localVelocity.z) * movementTrail, maxTrail);

            bool aiming = locomotion.IsAiming || ads.AdsBlend > 0.01f;
            // Slides carry it like a sprint; over an obstacle the one-handed carry (ActionPivot) owns the posture.
            bool lowCarry = (locomotion.Pace == LocomotionPace.Sprint || locomotion.IsSliding) && !locomotion.IsTraversing;
            sprintBlend = Mathf.MoveTowards(sprintBlend, !aiming && lowCarry ? 1f : 0f, postureBlendSpeed * dt);
            tacticalBlend = Mathf.MoveTowards(tacticalBlend, !aiming && locomotion.Pace == LocomotionPace.TacticalSprint ? 1f : 0f, postureBlendSpeed * dt);

            spring.PositionTarget = trail + sprintPosition * sprintBlend + tacticalSprintPosition * tacticalBlend;
            spring.RotationTarget = new Vector3(lead.x, lead.y, -localVelocity.x * strafeRoll) +
                                    sprintEuler * sprintBlend + tacticalSprintEuler * tacticalBlend;
            spring.Step(dt);

            UpdateTuck(weapon, pivot, dt, out Vector3 barrelDirection, out float compression, out float tilt, out float length);
            float compressed = CompressedReady01 * Mathf.Clamp01(length / compressedPostureLength);

            Vector2 sway = breathing.WeaponSwayEuler;
            ViewPose ready = data.BarrelLengthInches >= lowReadyBarrelInches ? lowReady : highReady;
            Quaternion rotation = Quaternion.Euler(spring.Rotation + new Vector3(sway.x, sway.y, 0f) + ready.euler * tilt + compressedEuler * compressed);
            Vector3 translation = spring.Position - barrelDirection * compression + compressedPosition * compressed;

            Vector3 stock = weapon.StockPivotSocket != null ? pivot.InverseTransformPoint(weapon.StockPivotSocket.position) : Vector3.zero;
            if (tilt > 0f && weapon.RightHandIKGrip != null) // the hand reaches its ready point by half tilt, keeping the weapon in frame
                translation += ViewPose.Placement(pivot, weapon.RightHandIKGrip, stock, rotation, translation, ready.target) * Mathf.Min(1f, tilt * 2f);
            pivot.SetLocalPositionAndRotation(stock - rotation * stock + translation, rotation);
        }

        private void UpdateTuck(WeaponModelRig weapon, Transform pivot, float dt, out Vector3 barrelDirection,
            out float compression, out float tilt, out float length)
        {
            barrelDirection = Vector3.forward;
            float penetration = 0f;
            length = 0f;

            if (weapon.MuzzleSocket != null && weapon.StockPivotSocket != null)
            {
                // Probe the untucked pose: points expressed under InertiaPivot, re-placed in its parent's space.
                Vector3 stock = pivot.InverseTransformPoint(weapon.StockPivotSocket.position);
                Vector3 barrel = pivot.InverseTransformPoint(weapon.MuzzleSocket.position) - stock;
                length = barrel.magnitude;
                if (length > 0.05f)
                {
                    barrelDirection = barrel / length;
                    Transform parent = pivot.parent;
                    if (Physics.SphereCast(parent.TransformPoint(stock), barrelRadius, parent.TransformDirection(barrelDirection),
                            out RaycastHit hit, length, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                        penetration = length - hit.distance;
                }
            }

            float rate = penetration > tuckDepth ? tuckInSpeed : tuckOutSpeed;
            // Never lag far behind real penetration, or the muzzle would clip while walking into a wall.
            tuckDepth = Mathf.Max(Mathf.MoveTowards(tuckDepth, penetration, rate * dt), penetration * 0.85f);

            compression = Mathf.Min(tuckDepth, maxCompression);
            tilt = Mathf.Clamp01((tuckDepth - maxCompression) / tiltRange);
            TuckAmount = Mathf.Clamp01(tuckDepth / (maxCompression + tiltRange));
            CompressedReady01 = Mathf.SmoothStep(0f, 1f, compression / maxCompression);
            Tilt01 = tilt;
        }
    }
}
