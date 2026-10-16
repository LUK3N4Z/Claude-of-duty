using System;
using FPS.Ballistics;
using FPS.Core;
using UnityEngine;

namespace FPS.Locomotion
{
    public enum LocomotionPace
    {
        Idle,
        SlowTacticalWalk,
        CombatPace,
        Sprint,
        TacticalSprint,
    }

    /// <summary>
    /// Grounded first-person movement: momentum limited by surface traction and carried weapon load, gait paces with
    /// stamina, velocity-driven slide (cancelled by crouch or jump into a crouched ready) and dive-to-prone with slope
    /// friction physics, jumping, vaulting and mantling (Jump/Climb arbitrates: traverse what is ahead, else jump),
    /// door bashes (V facing a door, or sprinting into one), look, and the aim/sprint arbitration every other player
    /// system reads. Owns the root position and yaw and PitchPivot.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(PlayerRig), typeof(StanceAndLeanController))]
    public sealed class RealisticLocomotionController : MonoBehaviour
    {
        private const float GroundSkin = 0.05f;
        private const float TwoPi = Mathf.PI * 2f;

        [Header("Look")]
        [Tooltip("Degrees per pointer pixel.")]
        [SerializeField] private float mouseSensitivity = 0.07f;
        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField] private float stickSensitivity = 160f;
        [SerializeField] private bool toggleAim;
        [SerializeField, Range(60f, 89f)] private float pitchLimit = 85f;
        [Tooltip("Prone look limits (up, down) in pitch degrees; negative looks up.")]
        [SerializeField] private Vector2 pronePitchLimits = new Vector2(-30f, 25f);
        [Tooltip("How fast the view is pushed back inside new pitch limits (deg/s).")]
        [SerializeField] private float pitchLimitRecovery = 120f;

        [Header("Pace Speeds (m/s)")]
        [SerializeField] private float slowWalkSpeed = 1.3f;
        [SerializeField] private float combatPaceSpeed = 3.4f;
        [SerializeField] private float sprintSpeed = 5.8f;
        [SerializeField] private float tacticalSprintSpeed = 7.2f;
        [Tooltip("Lowest scroll-wheel fraction of combat pace. Below 0.5 the gait counts as a slow tactical walk.")]
        [SerializeField, Range(0.1f, 1f)] private float minPaceScale = 0.3f;
        [SerializeField, Range(0.05f, 0.25f)] private float paceScaleStep = 0.1f;

        [Header("Speed Multipliers")]
        [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = 0.55f;
        [SerializeField, Range(0f, 1f)] private float proneSpeedMultiplier = 0.22f;
        [SerializeField, Range(0f, 1f)] private float aimSpeedMultiplier = 0.6f;
        [SerializeField, Range(0f, 1f)] private float backwardSpeedMultiplier = 0.65f;
        [SerializeField, Range(0f, 1f)] private float strafeSpeedMultiplier = 0.8f;
        [SerializeField, Range(0f, 1f)] private float proneTransitionSpeedMultiplier = 0.3f;

        [Header("Momentum (m/s²)")]
        [SerializeField] private float groundAcceleration = 8f;
        [SerializeField] private float groundDeceleration = 10f;
        [SerializeField] private float sprintAcceleration = 5f;
        [SerializeField] private float airAcceleration = 1.2f;
        [SerializeField] private float stickToGroundSpeed = 1.5f;

        [Header("Carried Weapon")]
        [SerializeField, Min(0f)] private float weaponMassKg;
        [SerializeField, Range(0f, 100f)] private float weaponErgonomics = 100f;
        [Tooltip("Top-speed loss per kg of weapon.")]
        [SerializeField] private float massSpeedPenaltyPerKg = 0.012f;
        [SerializeField, Range(0.5f, 1f)] private float minLoadSpeedMultiplier = 0.82f;

        [Header("Ground")]
        [SerializeField] private float groundProbeDistance = 0.12f;
        [Tooltip("Dynamic friction treated as full traction (dry concrete). Lower friction scales acceleration down.")]
        [SerializeField] private float referenceFriction = 0.6f;
        [SerializeField, Range(0.01f, 1f)] private float minTraction = 0.15f;

        [Header("Stamina (fraction per second)")]
        [SerializeField] private float sprintDrain = 0.07f;
        [SerializeField] private float tacticalSprintDrain = 0.2f;
        [SerializeField] private float staminaRegen = 0.12f;
        [SerializeField] private float staminaRegenDelay = 1f;
        [Tooltip("Stamina needed to sprint again after running dry.")]
        [SerializeField, Range(0f, 1f)] private float exhaustionRecovery = 0.35f;
        [SerializeField, Range(0f, 1f)] private float tacticalSprintMinStamina = 0.15f;

        [Header("Gait (stride length, m)")]
        [SerializeField] private float walkStride = 1.4f;
        [SerializeField] private float combatStride = 1.6f;
        [SerializeField] private float sprintStride = 2.2f;
        [SerializeField] private float tacticalSprintStride = 2.6f;
        [SerializeField] private float crouchStrideMultiplier = 0.75f;
        [SerializeField] private float proneStride = 0.8f;

        [Header("Injuries")]
        [Tooltip("Sprint speed lost at full leg injury (needs a TacticalHealthAndArmorSystem).")]
        [SerializeField, Range(0f, 1f)] private float legInjurySprintPenalty = 0.45f;
        [Tooltip("Leg injury above which tactical sprint is impossible.")]
        [SerializeField, Range(0f, 1f)] private float legInjuryTacticalLimit = 0.5f;

        [Header("Slide")]
        [SerializeField] private float slideMinSpeed = 4.5f;
        [SerializeField] private float slideBoost = 1.08f;
        [Tooltip("Kinetic friction coefficient of the body on dry ground.")]
        [SerializeField] private float slideFriction = 0.42f;
        [SerializeField] private float slideExitSpeed = 1.8f;
        [SerializeField] private float slideSteerRate = 30f;
        [SerializeField] private float slideMaxDuration = 2.5f;

        [Header("Jump")]
        [Tooltip("Take-off speed (m/s): about 0.45 m of lift with a carried loadout.")]
        [SerializeField] private float jumpSpeed = 3f;
        [SerializeField, Range(0f, 1f)] private float jumpStaminaCost = 0.05f;

        [Header("Slide Cancel")]
        [Tooltip("Speed kept when a slide is cancelled into a crouch.")]
        [SerializeField, Range(0f, 1f)] private float slideCancelSpeedRetention = 0.6f;

        [Header("Door Bash")]
        [Tooltip("Sprint speed needed to bash a door open by running into it (m/s).")]
        [SerializeField] private float sprintBashSpeed = 3.5f;
        [Tooltip("Speed kept after a bash.")]
        [SerializeField, Range(0f, 1f)] private float bashSpeedRetention = 0.55f;

        [Header("Dive To Prone")]
        [SerializeField] private float diveDuration = 0.55f;
        [SerializeField] private float diveFriction = 0.85f;
        [SerializeField, Range(0f, 1f)] private float diveSpeedRetention = 0.9f;
        [SerializeField] private float diveTransitionTime = 0.45f;

        /// <summary>Touched down after a fall; argument is the downward impact speed (m/s).</summary>
        public event Action<float> Landed;

        private CharacterController controller;
        private StanceAndLeanController stance;
        private MantleAndVaultController traversal;
        private Interactor interactor;
        private PlayerRig rig;
        private InputReader input;
        private TacticalHealthAndArmorSystem health;

        private Vector3 planarVelocity;
        private float verticalSpeed;
        private bool grounded;
        private Vector3 groundNormal = Vector3.up;
        private float traction = 1f;
        private float airTime;

        private float yaw, pitch;
        private bool aimRequested, sprintRequested, tacticalRequested, sprinting;
        private bool sliding, diving;
        private float slideTime, diveTime;
        private float stamina = 1f;
        private bool exhausted;
        private float lastStaminaDrainTime = float.NegativeInfinity;
        private float paceScale = 1f;
        private float stepPhase;
        private Vector2 pendingLookOffset;

        public Vector3 Velocity => planarVelocity + Vector3.up * (grounded ? 0f : verticalSpeed); // not the stick-to-ground push
        public float PlanarSpeed => new Vector2(planarVelocity.x, planarVelocity.z).magnitude;
        public LocomotionPace Pace { get; private set; }
        public bool IsGrounded => grounded;
        public bool IsSprinting => sprinting;
        public bool IsTacticalSprinting => sprinting && tacticalRequested;
        public bool IsSliding => sliding;
        public bool IsDiving => diving;
        /// <summary>Vaulting or mantling: the body follows the traversal path.</summary>
        public bool IsTraversing => traversal != null && traversal.IsActive;
        /// <summary>When the last slide was cancelled into a crouch (Time.time), for the quick ready.</summary>
        public float LastSlideCancelTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Aim intent after arbitration (blocked by sprint, dive and prone transitions). ADS reads this.</summary>
        public bool IsAiming { get; private set; }
        public float Stamina01 => stamina;
        public bool IsExhausted => exhausted;
        /// <summary>Surface grip in [minTraction, 1] from the ground collider's physics material.</summary>
        public float Traction => traction;
        /// <summary>Gait cycle phase in radians; one full cycle per stride (two steps).</summary>
        public float StepPhase => stepPhase;
        /// <summary>Scroll-wheel fraction of combat pace.</summary>
        public float PaceScale => paceScale;
        public float Yaw => yaw;
        public float Pitch => pitch;
        /// <summary>Look sensitivity scale set by the aiming system (zoom and optic magnification).</summary>
        public float LookSensitivityMultiplier { get; set; } = 1f;
        /// <summary>The player's look sensitivity setting (1 = authored).</summary>
        public float SensitivityScale { get; set; } = 1f;
        /// <summary>Extra sensitivity factor while aiming (settings), on top of the zoom compensation.</summary>
        public float AdsSensitivityScale { get; set; } = 1f;
        public StanceAndLeanController StanceController => stance;
        /// <summary>Sprint speed multiplier from leg injuries (1 when unhurt or without a health system).</summary>
        public float InjurySprintMultiplier => health != null ? 1f - legInjurySprintPenalty * health.LegInjury01 : 1f;

        /// <summary>Equipped weapon load. Mass slows top speed; mass and ergonomics (0-100) scale acceleration.</summary>
        public void SetWeaponHandling(float massKg, float ergonomics)
        {
            weaponMassKg = Mathf.Max(0f, massKg);
            weaponErgonomics = Mathf.Clamp(ergonomics, 0f, 100f);
        }

        /// <summary>Ends sprinting (e.g. firing or reloading). The player must press sprint again.</summary>
        public void CancelSprint()
        {
            sprintRequested = false;
            tacticalRequested = false;
        }

        /// <summary>
        /// Rotates the view by a permanent offset next frame (recoil climb). Negative pitch looks up. Look limits still apply.
        /// </summary>
        public void AddLookOffset(float pitchDegrees, float yawDegrees) => pendingLookOffset += new Vector2(pitchDegrees, yawDegrees);

        /// <summary>Snaps the view (spawns, scripted placement).</summary>
        public void SetLook(float yawDegrees, float pitchDegrees)
        {
            yaw = Mathf.Repeat(yawDegrees, 360f);
            pitch = pitchDegrees;
            pendingLookOffset = Vector2.zero;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            rig.PitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Spends stamina (fraction 0-1) and restarts the regeneration delay.</summary>
        public void DrainStamina(float amount)
        {
            stamina = Mathf.Max(0f, stamina - amount);
            lastStaminaDrainTime = Time.time;
            if (stamina <= 0f) exhausted = true;
        }

        /// <summary>
        /// Moves <paramref name="velocity"/> toward <paramref name="target"/>: speed along the target direction changes
        /// at <paramref name="acceleration"/> (or <paramref name="deceleration"/> when braking or reversing) while
        /// sideways drift bleeds off at <paramref name="deceleration"/>. Reversing therefore brakes before it accelerates.
        /// </summary>
        public static Vector3 AccelerateTowards(Vector3 velocity, Vector3 target, float acceleration, float deceleration, float dt)
        {
            float targetSpeed = target.magnitude;
            if (targetSpeed < 1e-4f) return Vector3.MoveTowards(velocity, Vector3.zero, deceleration * dt);

            Vector3 direction = target / targetSpeed;
            float along = Vector3.Dot(velocity, direction);
            Vector3 lateral = velocity - direction * along;
            float rate = along < 0f || along > targetSpeed ? deceleration : acceleration;
            along = Mathf.MoveTowards(along, targetSpeed, rate * dt);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero, deceleration * dt);
            return direction * along + lateral;
        }

        /// <summary>
        /// One step of a body sliding on a slope: gravity pulls along the downhill direction (g·sinθ) and kinetic
        /// friction removes μ·g·cosθ of speed. <paramref name="velocity"/> is horizontal; y is ignored.
        /// </summary>
        public static Vector3 IntegrateSlide(Vector3 velocity, Vector3 groundNormal, float friction, float gravity, float dt)
        {
            velocity.y = 0f;
            var downhill = new Vector3(groundNormal.x, 0f, groundNormal.z); // horizontal part of the normal, |·| = sinθ
            velocity += downhill * (gravity * dt);

            float speed = velocity.magnitude;
            float loss = friction * gravity * Mathf.Clamp01(groundNormal.y) * dt;
            return speed <= loss ? Vector3.zero : velocity * ((speed - loss) / speed);
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            stance = GetComponent<StanceAndLeanController>();
            rig = GetComponent<PlayerRig>();
            input = rig.Input;
            TryGetComponent(out health);
            TryGetComponent(out traversal);
            TryGetComponent(out interactor);
            yaw = transform.eulerAngles.y;
        }

        private void OnEnable()
        {
            if (input == null)
            {
                Debug.LogError($"{nameof(PlayerRig)} on '{name}' has no InputReader; locomotion disabled.", this);
                enabled = false;
                return;
            }

            input.AimPressed += OnAimPressed;
            input.AimReleased += OnAimReleased;
            input.SprintPressed += OnSprintPressed;
            input.TacticalSprintPressed += OnTacticalSprintPressed;
            input.CrouchPressed += OnCrouchPressed;
            input.PronePressed += OnPronePressed;
            input.MountWeaponPressed += OnMountPressed;
            input.WalkSpeedStepped += OnWalkSpeedStepped;
            input.JumpPressed += OnJumpPressed;
            input.EnablePlayerInput();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            if (input == null) return;

            input.AimPressed -= OnAimPressed;
            input.AimReleased -= OnAimReleased;
            input.SprintPressed -= OnSprintPressed;
            input.TacticalSprintPressed -= OnTacticalSprintPressed;
            input.CrouchPressed -= OnCrouchPressed;
            input.PronePressed -= OnPronePressed;
            input.MountWeaponPressed -= OnMountPressed;
            input.WalkSpeedStepped -= OnWalkSpeedStepped;
            input.JumpPressed -= OnJumpPressed;
            input.DisablePlayerInput();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateLook(dt);
            if (IsTraversing)
            {
                TraversalStep(dt);
                return;
            }

            ProbeGround(dt);
            if (!grounded && input.JumpHeld && TryTraverse()) // reaching a ledge mid-jump
            {
                TraversalStep(dt);
                return;
            }

            Vector2 move = Vector2.ClampMagnitude(input.Move, 1f);
            UpdateSprintAndStamina(move, dt);
            UpdateAimAndMount(move);

            if (sliding) SlideStep(move, dt);
            else if (diving) DiveStep(dt);
            else WalkStep(move, dt);

            Move(dt);

            bool canLean = grounded && !sprinting && !sliding && !diving && stance.Current != Stance.Prone;
            int lean = (input.LeanRightHeld ? 1 : 0) - (input.LeanLeftHeld ? 1 : 0);
            stance.SetLeanInput(canLean ? lean : 0);

            float gaitSpeed = grounded && !sliding && !diving ? PlanarSpeed : 0f;
            stepPhase = Mathf.Repeat(stepPhase + gaitSpeed / StrideLength() * TwoPi * dt, TwoPi);
        }

        private void UpdateLook(float dt)
        {
            Vector2 raw = input.Look;
            Vector2 delta = (input.LookIsPointerDelta ? raw * mouseSensitivity : raw * (stickSensitivity * dt)) *
                            (LookSensitivityMultiplier * SensitivityScale * (IsAiming ? AdsSensitivityScale : 1f));
            float newYaw = yaw + delta.x + pendingLookOffset.y;
            float newPitch = pitch - delta.y + pendingLookOffset.x;
            pendingLookOffset = Vector2.zero;

            bool prone = stance.Current == Stance.Prone;
            float low = prone ? pronePitchLimits.x : -pitchLimit;
            float high = prone ? pronePitchLimits.y : pitchLimit;
            if (stance.IsMounted)
            {
                Vector2 arc = stance.MountArc;
                newYaw = stance.MountYaw + Mathf.Clamp(Mathf.DeltaAngle(stance.MountYaw, newYaw), -arc.x, arc.x);
                low = Mathf.Max(low, stance.MountPitch - arc.y);
                high = Mathf.Min(high, stance.MountPitch + arc.y);
            }

            // Limits that just tightened (going prone) push the view back gradually instead of snapping it.
            if (pitch < low) low = Mathf.MoveTowards(pitch, low, pitchLimitRecovery * dt);
            if (pitch > high) high = Mathf.MoveTowards(pitch, high, pitchLimitRecovery * dt);
            newPitch = Mathf.Clamp(newPitch, low, high);

            // A prone body cannot swing its legs through walls.
            if (prone && !Mathf.Approximately(newYaw, yaw) && !stance.IsProneBodyClear(transform.position, newYaw))
                newYaw = yaw;

            yaw = Mathf.Repeat(newYaw, 360f);
            pitch = newPitch;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            rig.PitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ProbeGround(float dt)
        {
            bool wasGrounded = grounded;
            float radius = controller.radius;
            float probeRadius = radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (radius + GroundSkin);
            float distance = radius - probeRadius + GroundSkin + groundProbeDistance;
            int mask = GameLayers.EnvironmentMask;

            grounded = false;
            if (verticalSpeed <= 0.01f &&
                Physics.SphereCast(origin, probeRadius, Vector3.down, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
            {
                // Sphere-cast normals round off at edges; re-sample the face for slope physics.
                groundNormal = Physics.Raycast(hit.point + Vector3.up * 0.05f, Vector3.down, out RaycastHit face, 0.1f, mask,
                    QueryTriggerInteraction.Ignore)
                    ? face.normal
                    : hit.normal;
                grounded = groundNormal.y >= Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);

                PhysicsMaterial material = hit.collider.sharedMaterial;
                float friction = material != null ? material.dynamicFriction : referenceFriction;
                traction = Mathf.Clamp(friction / referenceFriction, minTraction, 1f);
            }

            if (grounded)
            {
                if (!wasGrounded && -verticalSpeed > 1.5f) Landed?.Invoke(-verticalSpeed);
                airTime = 0f;
            }
            else
            {
                groundNormal = Vector3.up;
                airTime += dt;
            }
        }

        private void UpdateSprintAndStamina(Vector2 move, float dt)
        {
            bool forward = move.y > 0.45f;
            if (sprintRequested && (!forward || aimRequested || exhausted || stance.IsMounted ||
                                    stance.Current != Stance.Stand))
                CancelSprint();
            if (tacticalRequested && stamina < tacticalSprintMinStamina) tacticalRequested = false; // fall back to sprint

            sprinting = sprintRequested && grounded && !stance.IsTransitioning;
            if (sprinting) DrainStamina((tacticalRequested ? tacticalSprintDrain : sprintDrain) * dt);
            else if (Time.time - lastStaminaDrainTime >= staminaRegenDelay) stamina = Mathf.Min(1f, stamina + staminaRegen * dt);
            if (exhausted && stamina >= exhaustionRecovery) exhausted = false;

            if (sprinting) Pace = tacticalRequested ? LocomotionPace.TacticalSprint : LocomotionPace.Sprint;
            else if (move.sqrMagnitude < 0.0025f) Pace = LocomotionPace.Idle;
            else if (input.WalkSlowHeld || paceScale < 0.5f) Pace = LocomotionPace.SlowTacticalWalk;
            else Pace = LocomotionPace.CombatPace;
        }

        private void UpdateAimAndMount(Vector2 move)
        {
            IsAiming = aimRequested && !sprinting && !diving && !stance.IsProneTransition;
            if (stance.IsMounted && (!IsAiming || !grounded || move.sqrMagnitude > 0.04f || PlanarSpeed > 0.6f))
                stance.Unmount();
        }

        private void WalkStep(Vector2 move, float dt)
        {
            Vector3 wish = transform.right * move.x + transform.forward * move.y;
            float magnitude = wish.magnitude;
            Vector3 target = Vector3.zero;

            if (magnitude > 0.05f && !stance.IsMounted)
            {
                float speed = PaceSpeed() * DirectionalMultiplier(move) * StanceSpeedMultiplier() * LoadSpeedMultiplier;
                if (IsAiming) speed *= aimSpeedMultiplier;
                if (stance.IsProneTransition) speed *= proneTransitionSpeedMultiplier;
                target = wish * speed; // |wish| ≤ 1 keeps analog stick speed proportional
            }

            float acceleration, deceleration;
            if (grounded)
            {
                float handling = Mathf.Lerp(0.75f, 1.05f, weaponErgonomics / 100f) * LoadSpeedMultiplier;
                acceleration = (sprinting ? sprintAcceleration : groundAcceleration) * traction * handling;
                deceleration = groundDeceleration * traction;
            }
            else
            {
                acceleration = deceleration = airAcceleration;
            }

            planarVelocity = AccelerateTowards(planarVelocity, target, acceleration, deceleration, dt);
        }

        private void SlideStep(Vector2 move, float dt)
        {
            slideTime += dt;
            planarVelocity = IntegrateSlide(planarVelocity, groundNormal, slideFriction * traction, -Physics.gravity.y, dt);

            if (move.sqrMagnitude > 0.01f)
            {
                Vector3 wish = (transform.right * move.x + transform.forward * move.y).normalized;
                planarVelocity = Vector3.RotateTowards(planarVelocity, wish * planarVelocity.magnitude,
                    slideSteerRate * Mathf.Deg2Rad * dt, 0f);
            }

            if (PlanarSpeed < slideExitSpeed || slideTime > slideMaxDuration || airTime > 0.25f) sliding = false;
        }

        private void DiveStep(float dt)
        {
            diveTime += dt;
            planarVelocity = IntegrateSlide(planarVelocity, groundNormal, diveFriction * traction, -Physics.gravity.y, dt);
            if (diveTime >= diveDuration) diving = false;
        }

        private void Move(float dt)
        {
            if (grounded) verticalSpeed = -stickToGroundSpeed;
            else verticalSpeed += Physics.gravity.y * dt;

            Vector3 planarDelta = planarVelocity * dt;
            float distance = planarDelta.magnitude;
            if (grounded && distance > 1e-6f)
                planarDelta = Vector3.ProjectOnPlane(planarDelta, groundNormal).normalized * distance; // follow slopes
            planarDelta = stance.LimitProneMove(planarDelta);

            Vector3 before = transform.position;
            CollisionFlags flags = controller.Move(planarDelta + Vector3.up * (verticalSpeed * dt));

            if ((flags & CollisionFlags.Sides) != 0 && sprinting && PlanarSpeed >= sprintBashSpeed) SprintBash();
            if ((flags & CollisionFlags.Sides) != 0)
            {
                // Momentum into a wall is lost; keep only what actually moved (sliding along it).
                Vector3 actual = (transform.position - before) / dt;
                actual.y = 0f;
                if (actual.sqrMagnitude < planarVelocity.sqrMagnitude) planarVelocity = actual;
            }

            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
        }

        private void OnAimPressed()
        {
            aimRequested = !toggleAim || !aimRequested;
            if (aimRequested) CancelSprint(); // aiming wins over sprinting
        }

        private void OnAimReleased()
        {
            if (!toggleAim) aimRequested = false;
        }

        /// <summary>The body follows the traversal path; the capsule is placed, not swept (the solver checked clearance).</summary>
        private void TraversalStep(float dt)
        {
            Vector3 position = traversal.Step(dt);
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            verticalSpeed = 0f;
            planarVelocity = traversal.IsActive ? Vector3.zero : traversal.ExitVelocity;
            sprinting = false;
            IsAiming = false;
            stance.SetLeanInput(0);
        }

        private bool TryTraverse()
        {
            if (traversal == null || sliding || diving || !traversal.TryBegin()) return false;
            CancelSprint();
            stance.Unmount();
            return true;
        }

        private void OnJumpPressed()
        {
            if (sliding)
            {
                CancelSlide(); // tactical slide cancel
                return;
            }

            if (diving || IsTraversing || TryTraverse()) return;
            if (!grounded || stance.IsTransitioning || stance.Current == Stance.Prone) return;
            if (stance.Current == Stance.Crouch)
            {
                stance.TrySetStance(Stance.Stand); // jump from a crouch stands up first
                return;
            }

            stance.Unmount();
            verticalSpeed = jumpSpeed;
            grounded = false;
            DrainStamina(jumpStaminaCost);
        }

        private void CancelSlide()
        {
            sliding = false; // the slide already lowered the body: it stays in a crouch
            planarVelocity *= slideCancelSpeedRetention;
            LastSlideCancelTime = Time.time;
        }

        /// <summary>Ran into something while sprinting: a door in the way bursts open.</summary>
        private void SprintBash()
        {
            Vector3 direction = new Vector3(planarVelocity.x, 0f, planarVelocity.z).normalized;
            Vector3 chest = transform.position + Vector3.up * (controller.height * 0.6f);
            if (!Physics.Raycast(chest, direction, out RaycastHit hit, controller.radius + 0.35f, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return;
            IBashable door = hit.collider.GetComponentInParent<IBashable>();
            if (door != null && door.Bash(gameObject, direction, Mathf.Clamp01(PlanarSpeed / sprintSpeed))) planarVelocity *= bashSpeedRetention;
        }

        private void OnSprintPressed()
        {
            if (aimRequested || diving) return; // Shift while aiming is hold-breath, handled by breathing
            if (sliding) sliding = false; // slide cancel
            if (stance.Current != Stance.Stand && !stance.TrySetStance(Stance.Stand)) return;
            sprintRequested = true;
        }

        private void OnTacticalSprintPressed()
        {
            bool legsHold = health == null || health.LegInjury01 <= legInjuryTacticalLimit;
            if (sprintRequested && !exhausted && legsHold && stamina >= tacticalSprintMinStamina) tacticalRequested = true;
        }

        private void OnCrouchPressed()
        {
            if (diving) return;
            if (sliding)
            {
                CancelSlide();
                return;
            }

            if (sprinting && grounded && PlanarSpeed >= slideMinSpeed)
            {
                StartSlide();
                return;
            }

            stance.TrySetStance(stance.Current == Stance.Crouch ? Stance.Stand : Stance.Crouch);
        }

        private void OnPronePressed()
        {
            if (sliding || diving || !grounded) return;
            if (sprinting && PlanarSpeed >= slideMinSpeed)
            {
                StartDive();
                return;
            }

            stance.TrySetStance(stance.Current == Stance.Prone ? Stance.Stand : Stance.Prone);
        }

        private void OnMountPressed()
        {
            // Context melee: facing a door, V bashes it instead of mounting.
            if (interactor != null && interactor.TryBash(Mathf.Max(0.6f, PlanarSpeed / sprintSpeed)))
            {
                planarVelocity *= bashSpeedRetention;
                return;
            }

            if (stance.IsMounted) stance.Unmount();
            else if (IsAiming && grounded && PlanarSpeed < 0.5f) stance.TryMount(yaw, pitch);
        }

        private void OnWalkSpeedStepped(int direction)
        {
            if (interactor != null && interactor.TryScroll(direction)) return; // cracking a door, not changing pace
            float scaled = paceScale + direction * paceScaleStep;
            paceScale = Mathf.Clamp(Mathf.Round(scaled * 100f) / 100f, minPaceScale, 1f);
        }

        private void StartSlide()
        {
            if (!stance.TrySetStance(Stance.Crouch, 0.18f)) return;
            CancelSprint();
            sliding = true;
            slideTime = 0f;
            planarVelocity *= slideBoost;
        }

        private void StartDive()
        {
            if (!stance.TrySetStance(Stance.Prone, diveTransitionTime)) return;
            CancelSprint();
            diving = true;
            diveTime = 0f;
            planarVelocity *= diveSpeedRetention;
        }

        private float PaceSpeed()
        {
            if (Pace == LocomotionPace.TacticalSprint) return tacticalSprintSpeed * InjurySprintMultiplier;
            if (Pace == LocomotionPace.Sprint) return sprintSpeed * InjurySprintMultiplier;
            return input.WalkSlowHeld ? slowWalkSpeed : combatPaceSpeed * paceScale;
        }

        private float DirectionalMultiplier(Vector2 move)
        {
            float squared = move.sqrMagnitude;
            if (squared < 1e-4f) return 1f;

            float forwardWeight = move.y * move.y / squared; // cos² of the heading relative to forward
            return move.y >= 0f
                ? Mathf.Lerp(strafeSpeedMultiplier, 1f, forwardWeight)
                : Mathf.Lerp(strafeSpeedMultiplier, backwardSpeedMultiplier, forwardWeight);
        }

        private float StanceSpeedMultiplier()
        {
            switch (stance.Current)
            {
                case Stance.Crouch: return crouchSpeedMultiplier;
                case Stance.Prone: return proneSpeedMultiplier;
                default: return 1f;
            }
        }

        private float LoadSpeedMultiplier => Mathf.Clamp(1f - weaponMassKg * massSpeedPenaltyPerKg, minLoadSpeedMultiplier, 1f);

        private float StrideLength()
        {
            if (stance.Current == Stance.Prone) return proneStride;

            float stride;
            switch (Pace)
            {
                case LocomotionPace.TacticalSprint: stride = tacticalSprintStride; break;
                case LocomotionPace.Sprint: stride = sprintStride; break;
                case LocomotionPace.CombatPace: stride = combatStride; break;
                default: stride = walkStride; break;
            }

            return stance.Current == Stance.Crouch ? stride * crouchStrideMultiplier : stride;
        }
    }
}
