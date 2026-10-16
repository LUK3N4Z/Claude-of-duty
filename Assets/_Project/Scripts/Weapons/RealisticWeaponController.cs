using System;
using System.Collections.Generic;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FPS.Weapons
{
    public enum WeaponAction
    {
        None,
        Reload,
        MagazineCheck,
        FireModeSwitch,
        BoltCycle,
        Holster,
        Draw,
        /// <summary>Weapon lowered while the off hand works a throwable.</summary>
        Equipment,
    }

    /// <summary>
    /// Weapon mechanics for the carried loadout: true chamber tracking (+1, bolt hold-open, open bolt, manual bolt),
    /// fire modes at the cyclic rate, bore-axis shots with mechanical dispersion, staged and interruptible reloads
    /// (tactical keeps the magazine, speed drops a physical one), magazine checks, fire-mode switching and weapon
    /// swaps. Drives procedural manipulation poses on ActionPivot and the support-hand IK target.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ViewmodelRig), typeof(PhysicalRecoilEngine))]
    public sealed class RealisticWeaponController : MonoBehaviour
    {
        [Serializable]
        public struct LoadoutSlot
        {
            public WeaponModelRig model;
            public WeaponDataSO data;
            [Tooltip("Fitted attachments; empty uses the weapon's default build.")]
            public AttachmentDataSO[] attachments;
        }

        public const string LoadoutFieldName = nameof(loadout);
        private const int MaxShotsPerFrame = 4;

        [Tooltip("Slot 0 = primary, slot 1 = secondary.")]
        [SerializeField] private LoadoutSlot[] loadout = new LoadoutSlot[2];

        [Header("Manipulation Poses (m, deg)")]
        [SerializeField] private ProceduralSpringSolver.Tuning poseSpring = new ProceduralSpringSolver.Tuning(6f, 0.8f);
        [SerializeField] private Vector3 magazineOutPosition = new Vector3(-0.02f, 0.04f, 0.03f);
        [SerializeField] private Vector3 magazineOutEuler = new Vector3(-6f, 10f, 32f);
        [SerializeField] private Vector3 magazineInEuler = new Vector3(-4f, 12f, 36f);
        [SerializeField] private Vector3 chargePosition = new Vector3(0.01f, 0f, -0.02f);
        [SerializeField] private Vector3 chargeEuler = new Vector3(-4f, -8f, -12f);
        [SerializeField] private Vector3 magazineCheckPosition = new Vector3(0f, 0.02f, 0.02f);
        [SerializeField] private Vector3 magazineCheckEuler = new Vector3(18f, 0f, 45f);
        [SerializeField] private Vector3 fireModeEuler = new Vector3(0f, 0f, 8f);
        [SerializeField] private Vector3 manualCycleEuler = new Vector3(-6f, 4f, 18f);
        [SerializeField] private Vector3 loweredPosition = new Vector3(0f, -0.25f, -0.1f);
        [SerializeField] private Vector3 loweredEuler = new Vector3(60f, -15f, 10f);

        [Header("Ejected Cases")]
        [Tooltip("Ejection speed range out of the port (m/s).")]
        [SerializeField] private Vector2 ejectionSpeed = new Vector2(2.5f, 4f);
        [SerializeField] private float ejectionLift = 1.2f;
        [Tooltip("Random tumble (rad/s).")]
        [SerializeField] private float ejectionSpin = 30f;
        [SerializeField] private float casingLifetime = 20f;

        [Header("Dropped Magazines")]
        [SerializeField] private float droppedMagazineLifetime = 30f;
        [Tooltip("Release velocity of a dropped magazine in magazine-socket space (m/s).")]
        [SerializeField] private Vector3 dropVelocity = new Vector3(0f, -0.6f, -0.1f);

        private readonly ProceduralSpringSolver pose = new ProceduralSpringSolver();
        private ViewmodelRig viewmodel;
        private RealisticLocomotionController locomotion;
        private StanceAndLeanController stance;
        private RealisticWeaponInertia inertia;
        private PhysicalRecoilEngine recoil;
        private InputReader input;

        private WeaponState[] states;
        private List<AttachmentDataSO>[] builds;
        private int activeSlot = -1;
        private int pendingSlot = -1;
        private float actionTime, actionDuration;
        private ReloadStage reloadTarget;
        private bool triggerHeld, semiPending, dryFiredThisPull, checkHeld, estimateShown;
        private int burstRemaining;
        private float nextShotTime, fireBlockedUntil;
        private WeaponMechanism mechanism;
        private float lastShotTime = float.NegativeInfinity;

        public WeaponState ActiveState => activeSlot >= 0 ? states[activeSlot] : null;
        public int ActiveSlot => activeSlot;
        public WeaponAction CurrentAction { get; private set; }
        /// <summary>The reload stage in progress (None when not reloading).</summary>
        public ReloadStage ReloadTarget => CurrentAction == WeaponAction.Reload ? reloadTarget : ReloadStage.None;
        public bool BlocksAiming => CurrentAction == WeaponAction.Reload || CurrentAction == WeaponAction.MagazineCheck ||
                                    CurrentAction == WeaponAction.Holster || CurrentAction == WeaponAction.Draw ||
                                    CurrentAction == WeaponAction.Equipment;
        /// <summary>Where the support hand belongs now: grip, magazine or charging handle.</summary>
        public Transform LeftHandTarget { get; private set; }
        public MagazineEstimate LastEstimate { get; private set; }

        public WeaponState GetState(int slot) => slot >= 0 && slot < states.Length ? states[slot] : null;

        /// <summary>
        /// Lowers the weapon so the off hand can work a throwable; a reload or magazine check is interrupted (completed
        /// reload stages are kept). False while a weapon swap is under way or nothing is held.
        /// </summary>
        public bool LowerForEquipment()
        {
            if (ActiveState == null || CurrentAction == WeaponAction.Holster || CurrentAction == WeaponAction.Draw) return false;
            if (CurrentAction == WeaponAction.Reload) InterruptReload();
            CancelTriggerIntent();
            locomotion.CancelSprint();
            StartAction(WeaponAction.Equipment, float.PositiveInfinity);
            return true;
        }

        /// <summary>Brings the weapon back up after a throw.</summary>
        public void RaiseFromEquipment()
        {
            if (CurrentAction != WeaponAction.Equipment) return;
            StartAction(WeaponAction.Draw, ActiveState.Data.DrawTime * 0.6f);
        }

        /// <summary>
        /// Takes loose magazines (rounds per magazine in <paramref name="magazines"/>) for every carried weapon firing
        /// <paramref name="caliber"/>, up to two over its loadout count. Taken entries are set to -1. Returns the count taken.
        /// </summary>
        public int TakeMagazines(CaliberDataSO caliber, int[] magazines)
        {
            int taken = 0;
            foreach (WeaponState state in states)
            {
                if (state == null || state.Data.Caliber != caliber) continue;
                int limit = state.Data.SpareMagazines + 2;
                for (int i = 0; i < magazines.Length && state.SpareMagazines.Count < limit; i++)
                {
                    if (magazines[i] <= 0) continue;
                    state.SpareMagazines.Add(Mathf.Min(magazines[i], state.Data.MagazineCapacity));
                    magazines[i] = -1;
                    taken++;
                }
            }

            return taken;
        }

        /// <summary>Tops every carried weapon's spare magazines back up to its loadout count. Returns rounds added.</summary>
        public int RefillAmmunition()
        {
            int added = 0;
            foreach (WeaponState state in states)
            {
                if (state == null) continue;
                int capacity = state.Data.MagazineCapacity;
                for (int i = 0; i < state.SpareMagazines.Count; i++)
                {
                    added += capacity - state.SpareMagazines[i];
                    state.SpareMagazines[i] = capacity;
                }

                for (int i = state.SpareMagazines.Count; i < state.Data.SpareMagazines; i++)
                {
                    state.SpareMagazines.Add(capacity);
                    added += capacity;
                }
            }

            return added;
        }

        /// <summary>
        /// Puts a weapon into a slot with a fresh, loaded state and the given attachments (null or empty: the weapon's
        /// default build). The active slot (or an empty hand) equips it now.
        /// </summary>
        public void SetLoadout(int slot, WeaponModelRig model, WeaponDataSO data, IReadOnlyList<AttachmentDataSO> attachments = null)
        {
            if (slot < 0 || slot >= loadout.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (data == null) throw new ArgumentNullException(nameof(data));

            loadout[slot] = new LoadoutSlot { model = model, data = data };
            if (states[slot] != null) Destroy(states[slot].Data);
            builds[slot] = new List<AttachmentDataSO>(attachments != null && attachments.Count > 0 ? attachments : data.DefaultAttachments);
            states[slot] = new WeaponState(GunsmithAssembler.Assemble(data, builds[slot]));
            if (activeSlot == slot || activeSlot < 0) BeginEquip(slot);
        }

        /// <summary>The model and base (un-built) data carried in <paramref name="slot"/>; false when it is empty.</summary>
        public bool GetLoadoutWeapon(int slot, out WeaponModelRig model, out WeaponDataSO data)
        {
            bool filled = slot >= 0 && slot < loadout.Length && states[slot] != null;
            model = filled ? loadout[slot].model : null;
            data = filled ? loadout[slot].data : null;
            return filled;
        }

        /// <summary>Replaces the whole build of a slot's weapon in one refit (rounds, chamber and reload progress kept).</summary>
        public void SetBuild(int slot, IReadOnlyList<AttachmentDataSO> parts)
        {
            if (states[slot] == null) throw new InvalidOperationException($"Loadout slot {slot} is empty.");
            List<AttachmentDataSO> build = builds[slot];
            build.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null) build.Add(parts[i]);
            }

            WeaponDataSO previous = states[slot].Data;
            states[slot].Data = GunsmithAssembler.Assemble(loadout[slot].data, build);
            if (slot == activeSlot)
            {
                InterruptReload();
                Equipped(viewmodel.Equip(loadout[slot].model, states[slot].Data, build)).SetMagazineVisible(states[slot].HasMagazine);
            }

            Destroy(previous);
        }

        /// <summary>The attachments fitted to a slot's weapon.</summary>
        public IReadOnlyList<AttachmentDataSO> GetAttachments(int slot) => builds[slot];

        /// <summary>
        /// Fits <paramref name="part"/> in <paramref name="type"/> (null removes it) and recalculates the build live; the
        /// held weapon is refitted in place, keeping its rounds, chamber and reload progress.
        /// </summary>
        public void SetAttachment(int slot, AttachmentSlot type, AttachmentDataSO part)
        {
            if (states[slot] == null) throw new InvalidOperationException($"Loadout slot {slot} is empty.");
            if (part != null && part.Slot != type) throw new ArgumentException($"{part.DisplayName} fits {part.Slot}, not {type}.", nameof(part));

            List<AttachmentDataSO> build = builds[slot];
            build.RemoveAll(fitted => fitted == null || fitted.Slot == type);
            if (part != null) build.Add(part);

            WeaponDataSO previous = states[slot].Data;
            states[slot].Data = GunsmithAssembler.Assemble(loadout[slot].data, build);
            if (slot == activeSlot)
            {
                InterruptReload();
                Equipped(viewmodel.Equip(loadout[slot].model, states[slot].Data, build)).SetMagazineVisible(states[slot].HasMagazine);
            }

            Destroy(previous);
        }

        /// <summary>Toggles every flashlight and laser on the held weapon.</summary>
        public void ToggleDevices()
        {
            if (viewmodel.Weapon == null) return;
            foreach (TacticalDevice device in viewmodel.Weapon.GetComponentsInChildren<TacticalDevice>()) device.Toggle();
        }

        /// <summary>Holsters the current weapon and draws another: 0 primary, 1 secondary, -1 the next filled slot.</summary>
        public void SelectSlot(int slot)
        {
            int target = slot < 0 ? NextFilledSlot() : slot;
            if (target < 0 || target >= states.Length || states[target] == null) return;
            if (target == activeSlot || target == pendingSlot || CurrentAction == WeaponAction.Holster) return;

            if (activeSlot < 0)
            {
                BeginEquip(target);
                return;
            }

            InterruptReload();
            CancelTriggerIntent();
            pendingSlot = target;
            StartAction(WeaponAction.Holster, ActiveState.Data.HolsterTime);
        }

        public void PullTrigger()
        {
            triggerHeld = true;
            dryFiredThisPull = false;
            WeaponState state = ActiveState;
            if (state == null) return;

            switch (state.FireMode)
            {
                case FireMode.Semi: semiPending = true; break;
                case FireMode.Burst: burstRemaining = state.Data.BurstCount; break;
            }
        }

        public void ReleaseTrigger() => triggerHeld = false;

        /// <summary>
        /// Starts, resumes or upgrades a reload. A speed request during magazine removal turns a tactical reload into a
        /// drop; a pending reload continues from its last completed stage.
        /// </summary>
        public void RequestReload(bool speed)
        {
            WeaponState state = ActiveState;
            if (state == null) return;

            if (CurrentAction == WeaponAction.Reload)
            {
                if (speed && reloadTarget == ReloadStage.MagazineRemoved && !state.SpeedReload)
                {
                    state.SpeedReload = true;
                    actionDuration = Mathf.Min(actionDuration, actionTime + state.Data.SpeedMagazineRemoveTime);
                }

                return;
            }

            if (CurrentAction != WeaponAction.None) return;

            if (!state.ReloadPending)
            {
                int spare = state.BestSpareIndex();
                int current = state.HasMagazine ? state.MagazineRounds : -1;
                bool betterMagazine = spare >= 0 && state.SpareMagazines[spare] > current;
                if (!betterMagazine)
                {
                    // Nothing better to insert: at most chamber a round from the magazine already in place.
                    if (state.RoundChambered || current <= 0) return;
                    state.CompletedStage = ReloadStage.MagazineInserted;
                }

                state.SpeedReload = speed;
            }

            CancelTriggerIntent();
            locomotion.CancelSprint(); // reloading slows you out of a sprint; sprinting again interrupts it
            BeginNextReloadStage(state);
        }

        /// <summary>Hold to tilt the weapon and read the magazine; release to stop.</summary>
        public void SetMagazineCheck(bool held)
        {
            checkHeld = held;
            WeaponState state = ActiveState;
            if (!held || state == null || !state.HasMagazine || CurrentAction != WeaponAction.None) return;

            estimateShown = false;
            StartAction(WeaponAction.MagazineCheck, state.Data.MagazineCheckTime);
        }

        public void CycleFireMode()
        {
            WeaponState state = ActiveState;
            if (state == null || CurrentAction != WeaponAction.None || state.Data.FireModeCount < 2) return;

            state.FireModeIndex = (state.FireModeIndex + 1) % state.Data.FireModeCount;
            CancelTriggerIntent();
            StartAction(WeaponAction.FireModeSwitch, state.Data.FireModeSwitchTime);
            Raise(WeaponActionType.FireModeChanged, state.FireModeIndex);
        }

        /// <summary>
        /// Bore direction with mechanical dispersion: a uniform point in the group circle (<paramref name="moa"/> is the
        /// group diameter in minutes of angle) mapped onto the muzzle frame.
        /// </summary>
        public static Vector3 ApplyDispersion(Vector3 forward, Vector3 right, Vector3 up, float moa, Vector2 unitDiscSample)
        {
            float radius = Mathf.Tan(moa / 120f * Mathf.Deg2Rad);
            return (forward + right * (unitDiscSample.x * radius) + up * (unitDiscSample.y * radius)).normalized;
        }

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            locomotion = GetComponent<RealisticLocomotionController>();
            stance = GetComponent<StanceAndLeanController>();
            inertia = GetComponent<RealisticWeaponInertia>();
            recoil = GetComponent<PhysicalRecoilEngine>();
            input = GetComponent<PlayerRig>().Input;
            pose.PositionTuning = poseSpring;
            pose.RotationTuning = poseSpring;

            states = new WeaponState[loadout.Length];
            builds = new List<AttachmentDataSO>[loadout.Length];
            for (int i = 0; i < loadout.Length; i++)
            {
                if (loadout[i].model == null || loadout[i].data == null) continue;
                AttachmentDataSO[] requested = loadout[i].attachments;
                builds[i] = new List<AttachmentDataSO>(requested != null && requested.Length > 0 ? requested : loadout[i].data.DefaultAttachments);
                states[i] = new WeaponState(GunsmithAssembler.Assemble(loadout[i].data, builds[i]));
            }
        }

        private void OnEnable()
        {
            if (input == null) return;
            input.FirePressed += PullTrigger;
            input.FireReleased += ReleaseTrigger;
            input.ReloadPressed += OnReloadPressed;
            input.FastReloadPressed += OnFastReloadPressed;
            input.CheckMagazineChanged += SetMagazineCheck;
            input.ChangeFireModePressed += CycleFireMode;
            input.WeaponSlotRequested += SelectSlot;
            input.ToggleDevicePressed += ToggleDevices;
        }

        private void OnDisable()
        {
            if (input == null) return;
            input.FirePressed -= PullTrigger;
            input.FireReleased -= ReleaseTrigger;
            input.ReloadPressed -= OnReloadPressed;
            input.FastReloadPressed -= OnFastReloadPressed;
            input.CheckMagazineChanged -= SetMagazineCheck;
            input.ChangeFireModePressed -= CycleFireMode;
            input.WeaponSlotRequested -= SelectSlot;
            input.ToggleDevicePressed -= ToggleDevices;
        }

        private void OnDestroy()
        {
            if (states == null) return;
            foreach (WeaponState state in states)
            {
                if (state != null) Destroy(state.Data); // build clones only; assets are never stored in states
            }
        }

        private void Start()
        {
            if (activeSlot < 0)
            {
                int first = NextFilledSlot();
                if (first >= 0) BeginEquip(first);
            }
        }

        private void Update()
        {
            WeaponState state = ActiveState;
            if (state == null || viewmodel.Weapon == null) return;

            float now = Time.time;
            if (CurrentAction == WeaponAction.Reload && (locomotion.IsSprinting || locomotion.IsDiving)) InterruptReload();
            if (CurrentAction == WeaponAction.MagazineCheck && (!checkHeld || locomotion.IsSprinting)) CurrentAction = WeaponAction.None;

            if (CurrentAction != WeaponAction.None)
            {
                actionTime += Time.deltaTime;
                if (actionTime >= actionDuration)
                {
                    if (CurrentAction == WeaponAction.MagazineCheck) ShowEstimate(state);
                    else CompleteAction(state);
                }
            }

            UpdateTrigger(state, now);

            // Bolt actions run the bolt on their own once the trigger is released.
            if (state.Data.BoltType == BoltType.ManualBoltAction && CurrentAction == WeaponAction.None && !triggerHeld &&
                !state.RoundChambered && state.HasMagazine && state.MagazineRounds > 0 && now >= nextShotTime)
                StartAction(WeaponAction.BoltCycle, state.Data.ManualCycleTime);

            LeftHandTarget = HandTarget(viewmodel.Weapon);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            WeaponModelRig weapon = viewmodel.Weapon;
            if (dt <= 0f || weapon == null) return;

            PoseTarget(out Vector3 position, out Vector3 euler);
            pose.PositionTarget = position;
            pose.RotationTarget = euler;
            pose.Step(dt);

            Transform pivot = viewmodel.ActionPivot;
            Quaternion rotation = Quaternion.Euler(pose.Rotation);
            Vector3 stock = weapon.StockPivotSocket != null ? pivot.InverseTransformPoint(weapon.StockPivotSocket.position) : Vector3.zero;
            pivot.SetLocalPositionAndRotation(stock - rotation * stock + pose.Position, rotation);
            DriveMechanism(dt);
        }

        private void DriveMechanism(float dt)
        {
            WeaponState state = ActiveState;
            if (mechanism == null || state == null) return;

            float progress = actionDuration > 0f ? Mathf.Clamp01(actionTime / actionDuration) : 0f;
            bool boltAction = state.Data.BoltType == BoltType.ManualBoltAction;
            bool cycling = CurrentAction == WeaponAction.Reload && reloadTarget == ReloadStage.BoltCycled;
            mechanism.Drive(new MechanismState
            {
                TriggerPressed = triggerHeld && CurrentAction == WeaponAction.None,
                FireMode = state.FireModeIndex,
                SinceShot = Time.time - lastShotTime,
                CycleTime = state.Data.CycleTime,
                LockedBack = state.BoltLockedBack,
                Charge = cycling && !boltAction ? progress : 0f,
                BoltWork = CurrentAction == WeaponAction.BoltCycle || (cycling && boltAction) ? progress : 0f,
            }, dt);
        }

        private WeaponModelRig Equipped(WeaponModelRig weapon)
        {
            mechanism = weapon != null ? weapon.GetComponent<WeaponMechanism>() : null;
            return weapon;
        }

        private void UpdateTrigger(WeaponState state, float now)
        {
            if (!WantsShot(state)) return;

            if (locomotion.IsSprinting)
            {
                // Bring the weapon up out of the sprint first.
                locomotion.CancelSprint();
                fireBlockedUntil = now + state.Data.SprintToFireTime;
                return;
            }

            if (now < fireBlockedUntil) return;
            if (inertia.IsTucked || locomotion.IsDiving || stance.IsProneTransition)
            {
                CancelTriggerIntent();
                return;
            }

            switch (CurrentAction)
            {
                case WeaponAction.Equipment:
                    CancelTriggerIntent(); // a grenade in hand: the trigger finger is busy, nothing queues up
                    return;
                case WeaponAction.Holster:
                case WeaponAction.Draw:
                case WeaponAction.FireModeSwitch:
                case WeaponAction.BoltCycle:
                    return; // the shot goes off once the weapon is ready
                case WeaponAction.MagazineCheck:
                    CurrentAction = WeaponAction.None;
                    break;
                case WeaponAction.Reload:
                    if (!state.CanFire)
                    {
                        CancelTriggerIntent();
                        return;
                    }

                    InterruptReload(); // fire the chambered round mid-reload; completed stages are kept
                    break;
            }

            for (int i = 0; i < MaxShotsPerFrame && now >= nextShotTime && WantsShot(state); i++)
            {
                if (!state.CanFire)
                {
                    if (!dryFiredThisPull)
                    {
                        dryFiredThisPull = true;
                        Raise(WeaponActionType.DryFire, 0);
                    }

                    CancelTriggerIntent();
                    return;
                }

                Fire(state);
                float cycle = state.Data.CycleTime;
                nextShotTime = (now - nextShotTime > cycle ? now : nextShotTime) + cycle; // hold cadence across frames
                if (state.FireMode == FireMode.Semi) semiPending = false;
                else if (state.FireMode == FireMode.Burst) burstRemaining--;
            }
        }

        private bool WantsShot(WeaponState state)
        {
            switch (state.FireMode)
            {
                case FireMode.Semi: return semiPending;
                case FireMode.Burst: return burstRemaining > 0;
                default: return triggerHeld;
            }
        }

        private void Fire(WeaponState state)
        {
            switch (state.Data.BoltType)
            {
                case BoltType.OpenBolt:
                    state.MagazineRounds--;
                    if (state.MagazineRounds <= 0) state.RoundChambered = false; // bolt rides forward onto an empty chamber
                    break;
                case BoltType.ClosedBolt:
                    state.RoundChambered = false;
                    if (state.HasMagazine && state.MagazineRounds > 0)
                    {
                        state.MagazineRounds--;
                        state.RoundChambered = true;
                    }
                    else if (state.Data.BoltHoldOpen && state.HasMagazine)
                    {
                        state.BoltLockedBack = true; // the follower lifts the bolt catch
                        Raise(WeaponActionType.BoltLockedBack, 0);
                    }

                    break;
                default:
                    state.RoundChambered = false; // spent case stays until the bolt is worked
                    break;
            }

            EmitShot(state);
            lastShotTime = Time.time;
            if (state.Data.BoltType != BoltType.ManualBoltAction) EjectCasing(state);
            recoil.Kick(); // after the shot: the bullet has left before the muzzle rises
        }

        private void EmitShot(WeaponState state)
        {
            Transform muzzle = viewmodel.Weapon.MuzzleSocket != null ? viewmodel.Weapon.MuzzleSocket : viewmodel.Weapon.transform;
            CaliberDataSO caliber = state.Data.Caliber;
            EventBus<ShotFiredEvent>.Raise(new ShotFiredEvent
            {
                Origin = muzzle.position,
                Direction = ApplyDispersion(muzzle.forward, muzzle.right, muzzle.up, state.Data.MoaAccuracy, Random.insideUnitCircle),
                MuzzleVelocity = caliber != null ? caliber.MuzzleVelocity(state.Data.BarrelLengthInches) * state.Data.MuzzleVelocityMultiplier : 0f,
                Caliber = caliber,
                Shooter = transform,
            });
            EventBus<GunshotEvent>.Raise(new GunshotEvent
            {
                Position = muzzle.position,
                Direction = muzzle.forward,
                Shooter = transform,
                Profile = state.Data.AudioProfile,
                MuzzleFlash = state.Data.MuzzleFlashMultiplier,
                RoundsLeft = state.LoadedRounds,
            });
        }

        private void BeginNextReloadStage(WeaponState state)
        {
            ReloadStage next;
            switch (state.CompletedStage)
            {
                case ReloadStage.None: next = state.HasMagazine ? ReloadStage.MagazineRemoved : ReloadStage.MagazineInserted; break;
                case ReloadStage.MagazineRemoved: next = ReloadStage.MagazineInserted; break;
                case ReloadStage.MagazineInserted: next = ReloadStage.BoltCycled; break;
                default: next = ReloadStage.None; break;
            }

            bool nothingToChamber = state.RoundChambered || !state.HasMagazine || state.MagazineRounds <= 0;
            if (next == ReloadStage.MagazineInserted && state.BestSpareIndex() < 0)
            {
                CurrentAction = WeaponAction.None; // no magazine left to insert; the stage stays pending
                return;
            }

            if (next == ReloadStage.None || (next == ReloadStage.BoltCycled && nothingToChamber))
            {
                state.CompletedStage = ReloadStage.None;
                state.SpeedReload = false;
                CurrentAction = WeaponAction.None;
                return;
            }

            reloadTarget = next;
            WeaponDataSO data = state.Data;
            float duration = next == ReloadStage.MagazineRemoved
                ? state.SpeedReload ? data.SpeedMagazineRemoveTime : data.MagazineRemoveTime
                : next == ReloadStage.MagazineInserted
                    ? data.MagazineInsertTime
                    : data.BoltCycleTime * (state.BoltLockedBack ? 0.6f : 1f); // bolt release beats racking the handle
            StartAction(WeaponAction.Reload, duration);
        }

        private void CompleteAction(WeaponState state)
        {
            switch (CurrentAction)
            {
                case WeaponAction.Reload:
                    CompleteReloadStage(state);
                    return;
                case WeaponAction.BoltCycle:
                    EjectCasing(state); // working the bolt throws the spent case
                    Chamber(state);
                    Raise(WeaponActionType.BoltCycled, 0);
                    break;
                case WeaponAction.Holster:
                    Raise(WeaponActionType.Holstered, activeSlot);
                    int next = pendingSlot;
                    pendingSlot = -1;
                    BeginEquip(next);
                    return;
                case WeaponAction.Draw:
                    Raise(WeaponActionType.Drawn, activeSlot);
                    break;
            }

            CurrentAction = WeaponAction.None;
        }

        private void CompleteReloadStage(WeaponState state)
        {
            WeaponModelRig weapon = viewmodel.Weapon;
            switch (reloadTarget)
            {
                case ReloadStage.MagazineRemoved:
                    if (state.SpeedReload)
                    {
                        DropMagazine(state, weapon);
                        Raise(WeaponActionType.MagazineDropped, state.MagazineRounds);
                    }
                    else
                    {
                        if (state.MagazineRounds > 0) state.SpareMagazines.Add(state.MagazineRounds); // back into the pouch
                        Raise(WeaponActionType.MagazineRemoved, state.MagazineRounds);
                    }

                    state.HasMagazine = false;
                    state.MagazineRounds = 0;
                    weapon.SetMagazineVisible(false);
                    break;
                case ReloadStage.MagazineInserted:
                    int best = state.BestSpareIndex();
                    state.MagazineRounds = state.SpareMagazines[best];
                    state.SpareMagazines.RemoveAt(best);
                    state.HasMagazine = true;
                    weapon.SetMagazineVisible(true);
                    Raise(WeaponActionType.MagazineInserted, state.MagazineRounds);
                    break;
                case ReloadStage.BoltCycled:
                    if (state.Data.BoltType == BoltType.ManualBoltAction) EjectCasing(state); // last spent case comes out now
                    Chamber(state);
                    Raise(WeaponActionType.BoltCycled, 0);
                    break;
            }

            state.CompletedStage = reloadTarget;
            BeginNextReloadStage(state);
        }

        private static void Chamber(WeaponState state)
        {
            state.BoltLockedBack = false;
            if (state.Data.BoltType == BoltType.OpenBolt)
            {
                state.RoundChambered = true; // cocked; rounds stay in the magazine until fired
                return;
            }

            if (state.HasMagazine && state.MagazineRounds > 0)
            {
                state.MagazineRounds--;
                state.RoundChambered = true;
            }
        }

        private void EjectCasing(WeaponState state)
        {
            PhysicalShellCasing prefab = state.Data.Caliber != null ? state.Data.Caliber.CasingPrefab : null;
            WeaponModelRig weapon = viewmodel.Weapon;
            if (prefab == null || weapon == null || weapon.ChamberEjectionSocket == null) return;

            Transform port = weapon.ChamberEjectionSocket;
            PhysicalShellCasing casing = ObjectPoolManager.Instance.Spawn(prefab, port.position, port.rotation, null, casingLifetime);
            Vector3 velocity = port.forward * Random.Range(ejectionSpeed.x, ejectionSpeed.y) + port.up * ejectionLift + locomotion.Velocity;
            casing.Eject(velocity, Random.insideUnitSphere * ejectionSpin);
        }

        private void DropMagazine(WeaponState state, WeaponModelRig weapon)
        {
            Rigidbody prefab = state.Data.DroppedMagazinePrefab;
            if (prefab == null) return;

            Transform socket = weapon.MagazineSocket != null ? weapon.MagazineSocket : weapon.transform;
            Rigidbody magazine = ObjectPoolManager.Instance.Spawn(prefab, socket.position, socket.rotation, null, droppedMagazineLifetime);
            magazine.linearVelocity = locomotion.Velocity + socket.rotation * dropVelocity;
            magazine.angularVelocity = Random.insideUnitSphere * 6f;
        }

        private void ShowEstimate(WeaponState state)
        {
            if (estimateShown) return;
            estimateShown = true;
            LastEstimate = WeaponState.Estimate(state.MagazineRounds, state.Data.MagazineCapacity);
            Raise(WeaponActionType.MagazineChecked, (int)LastEstimate);
        }

        private void BeginEquip(int slot)
        {
            activeSlot = slot;
            WeaponState state = states[slot];
            WeaponModelRig weapon = Equipped(viewmodel.Equip(loadout[slot].model, state.Data, builds[slot]));
            weapon.SetMagazineVisible(state.HasMagazine);
            lastShotTime = float.NegativeInfinity;
            if (state.Data.Caliber != null && state.Data.Caliber.CasingPrefab != null)
                ObjectPoolManager.Instance.Prewarm(state.Data.Caliber.CasingPrefab, 24, 96);
            CancelTriggerIntent();
            nextShotTime = 0f;
            pose.Teleport(loweredPosition, loweredEuler);
            StartAction(WeaponAction.Draw, state.Data.DrawTime);
        }

        private void InterruptReload()
        {
            if (CurrentAction == WeaponAction.Reload) CurrentAction = WeaponAction.None;
        }

        private void StartAction(WeaponAction action, float duration)
        {
            CurrentAction = action;
            actionTime = 0f;
            actionDuration = duration;
        }

        private void CancelTriggerIntent()
        {
            semiPending = false;
            burstRemaining = 0;
        }

        private int NextFilledSlot()
        {
            for (int offset = 1; offset <= states.Length; offset++)
            {
                int slot = (Mathf.Max(activeSlot, 0) + offset) % states.Length;
                if (activeSlot < 0) slot = offset - 1;
                if (states[slot] != null && slot != activeSlot) return slot;
            }

            return -1;
        }

        private Transform HandTarget(WeaponModelRig weapon)
        {
            Transform target = null;
            if (CurrentAction == WeaponAction.MagazineCheck ||
                (CurrentAction == WeaponAction.Reload && reloadTarget != ReloadStage.BoltCycled))
                target = weapon.MagazineSocket;
            else if (CurrentAction == WeaponAction.BoltCycle || ReloadTarget == ReloadStage.BoltCycled)
                target = weapon.ChargingHandleSocket;

            return target != null ? target : weapon.LeftHandIKGrip;
        }

        private void PoseTarget(out Vector3 position, out Vector3 euler)
        {
            position = Vector3.zero;
            euler = Vector3.zero;
            float progress = actionDuration > 0f ? Mathf.Clamp01(actionTime / actionDuration) : 1f;
            switch (CurrentAction)
            {
                case WeaponAction.Reload:
                    if (reloadTarget == ReloadStage.MagazineRemoved)
                    {
                        position = magazineOutPosition;
                        euler = magazineOutEuler;
                    }
                    else if (reloadTarget == ReloadStage.MagazineInserted)
                    {
                        position = magazineOutPosition;
                        euler = magazineInEuler;
                    }
                    else
                    {
                        position = chargePosition;
                        euler = chargeEuler;
                    }

                    break;
                case WeaponAction.MagazineCheck:
                    position = magazineCheckPosition;
                    euler = magazineCheckEuler;
                    break;
                case WeaponAction.FireModeSwitch:
                    euler = fireModeEuler;
                    break;
                case WeaponAction.BoltCycle:
                    euler = manualCycleEuler;
                    break;
                case WeaponAction.Holster:
                    position = loweredPosition * progress;
                    euler = loweredEuler * progress;
                    break;
                case WeaponAction.Draw:
                    position = loweredPosition * (1f - progress);
                    euler = loweredEuler * (1f - progress);
                    break;
                case WeaponAction.Equipment:
                    float lowered = Mathf.Clamp01(actionTime / 0.2f);
                    position = loweredPosition * lowered;
                    euler = loweredEuler * lowered;
                    break;
            }
        }

        private void Raise(WeaponActionType type, int value)
        {
            Transform weapon = viewmodel.Weapon != null ? viewmodel.Weapon.transform : transform;
            EventBus<WeaponActionEvent>.Raise(new WeaponActionEvent { Type = type, Position = weapon.position, Owner = transform, Value = value });
        }

        private void OnReloadPressed() => RequestReload(false);

        private void OnFastReloadPressed() => RequestReload(true);
    }
}
