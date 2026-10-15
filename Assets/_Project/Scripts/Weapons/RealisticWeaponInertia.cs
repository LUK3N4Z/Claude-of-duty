using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// The weapon's physical connection to the body: mass/ergonomics-driven spring handling, free-aim lead inside an
    /// elastic deadzone, movement trail and sprint postures, breathing sway, and barrel collision that compresses the
    /// weapon back and tilts it to a high or low ready near walls. Rotates about the stock contact point. Owns
    /// InertiaPivot.
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
        [SerializeField] private Vector3 highReadyEuler = new Vector3(-55f, -10f, 15f);
        [SerializeField] private Vector3 lowReadyEuler = new Vector3(45f, -20f, -10f);
        [SerializeField] private float tuckInSpeed = 2.5f;
        [SerializeField] private float tuckOutSpeed = 0.8f;
        [SerializeField, Range(0f, 1f)] private float tuckedThreshold = 0.35f;

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
        /// <summary>Tucked far enough that aiming and firing are blocked.</summary>
        public bool IsTucked => TuckAmount >= tuckedThreshold;

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
            TuckAmount = 0f;
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

            // Heavy, unwieldy weapons respond slower and overshoot more: that is the felt torque.
            float handling = Mathf.Lerp(0.65f, 1.2f, data.Ergonomics / 100f) / Mathf.Sqrt(data.MassKg / referenceMassKg);
            spring.PositionTuning = new ProceduralSpringSolver.Tuning(positionSpring.frequency * handling, positionSpring.dampingRatio);
            spring.RotationTuning = new ProceduralSpringSolver.Tuning(rotationSpring.frequency * handling, rotationSpring.dampingRatio);

            // The arms swing the weapon ahead of the turn before the shoulders catch up.
            float freeAim = Mathf.Lerp(1f, adsFreeAimMultiplier, ads.AdsBlend) * (stance.IsMounted ? mountedFreeAimMultiplier : 1f);
            Vector2 lead = ClampToEllipse(new Vector2(pitchRate, yawRate) * freeAimLeadTime, freeAimRadius * freeAim);

            Vector3 localVelocity = transform.InverseTransformDirection(locomotion.Velocity);
            Vector3 trail = Vector3.ClampMagnitude(
                new Vector3(-localVelocity.x, -localVelocity.y * 0.5f, -localVelocity.z) * movementTrail, maxTrail);

            bool aiming = locomotion.IsAiming || ads.AdsBlend > 0.01f;
            sprintBlend = Mathf.MoveTowards(sprintBlend, !aiming && locomotion.Pace == LocomotionPace.Sprint ? 1f : 0f, postureBlendSpeed * dt);
            tacticalBlend = Mathf.MoveTowards(tacticalBlend, !aiming && locomotion.Pace == LocomotionPace.TacticalSprint ? 1f : 0f, postureBlendSpeed * dt);

            spring.PositionTarget = trail + sprintPosition * sprintBlend + tacticalSprintPosition * tacticalBlend;
            spring.RotationTarget = new Vector3(lead.x, lead.y, -localVelocity.x * strafeRoll) +
                                    sprintEuler * sprintBlend + tacticalSprintEuler * tacticalBlend;
            spring.Step(dt);

            UpdateTuck(weapon, pivot, dt, out Vector3 barrelDirection, out float compression, out float tilt);

            Vector2 sway = breathing.WeaponSwayEuler;
            Vector3 tuckEuler = (data.BarrelLengthInches >= lowReadyBarrelInches ? lowReadyEuler : highReadyEuler) * tilt;
            Quaternion rotation = Quaternion.Euler(spring.Rotation + new Vector3(sway.x, sway.y, 0f) + tuckEuler);
            Vector3 translation = spring.Position - barrelDirection * compression;

            Vector3 stock = weapon.StockPivotSocket != null ? pivot.InverseTransformPoint(weapon.StockPivotSocket.position) : Vector3.zero;
            pivot.SetLocalPositionAndRotation(stock - rotation * stock + translation, rotation);
        }

        private void UpdateTuck(WeaponModelRig weapon, Transform pivot, float dt, out Vector3 barrelDirection,
            out float compression, out float tilt)
        {
            barrelDirection = Vector3.forward;
            float penetration = 0f;

            if (weapon.MuzzleSocket != null && weapon.StockPivotSocket != null)
            {
                // Probe the untucked pose: points expressed under InertiaPivot, re-placed in its parent's space.
                Vector3 stock = pivot.InverseTransformPoint(weapon.StockPivotSocket.position);
                Vector3 barrel = pivot.InverseTransformPoint(weapon.MuzzleSocket.position) - stock;
                float length = barrel.magnitude;
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
        }
    }
}
