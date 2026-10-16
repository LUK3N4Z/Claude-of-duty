using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS.Core
{
    /// <summary>
    /// Single source of gameplay input. Continuous state (movement, look, held modifiers) is polled through
    /// properties; discrete intents are C# events. Context arbitration (Shift = sprint vs. hold breath while
    /// aiming) belongs to the consumer, not here.
    /// </summary>
    [CreateAssetMenu(fileName = "InputReader", menuName = "FPS/Core/Input Reader")]
    public sealed class InputReader : ScriptableObject
    {
        public const string PlayerMapName = "Player";
        public const string SystemMapName = "System";
        public const string ActionAssetFieldName = nameof(actionAsset);

        [SerializeField] private InputActionAsset actionAsset;

        [Tooltip("Max seconds between two reload taps for the second one to request a fast (drop-magazine) reload.")]
        [SerializeField, Range(0.15f, 0.6f)] private float fastReloadWindow = 0.35f;

        /// <summary>Trigger pressed.</summary>
        public event Action FirePressed;
        /// <summary>Trigger released. Also raised when input is disabled mid-press.</summary>
        public event Action FireReleased;
        public event Action AimPressed;
        /// <summary>Aim released. Also raised when input is disabled mid-press.</summary>
        public event Action AimReleased;
        public event Action SprintPressed;
        /// <summary>Sprint double-tap: upgrade to tactical sprint.</summary>
        public event Action TacticalSprintPressed;
        /// <summary>Stance key tapped: toggle crouch (slide when sprinting).</summary>
        public event Action CrouchPressed;
        /// <summary>Stance key held or prone key pressed: toggle prone (dive when sprinting).</summary>
        public event Action PronePressed;
        /// <summary>Single reload tap: tactical reload that retains the partial magazine.</summary>
        public event Action ReloadPressed;
        /// <summary>
        /// Second reload tap inside the fast-reload window. Always preceded by <see cref="ReloadPressed"/>, so treat
        /// it as an upgrade of the running reload to a speed reload (drop the magazine).
        /// </summary>
        public event Action FastReloadPressed;
        /// <summary>Reload key held past the tap threshold (true) and then released (false).</summary>
        public event Action<bool> CheckMagazineChanged;
        public event Action ChangeFireModePressed;
        public event Action SwitchSightPressed;
        public event Action MountWeaponPressed;
        /// <summary>Scroll-wheel step (+1 faster / -1 slower) for slow-walk speed.</summary>
        public event Action<int> WalkSpeedStepped;
        /// <summary>Weapon slot request: 0 = primary, 1 = secondary, -1 = cycle to the next slot.</summary>
        public event Action<int> WeaponSlotRequested;
        /// <summary>Use the focused interactable.</summary>
        public event Action InteractPressed;
        /// <summary>Toggle the weapon's flashlight / laser.</summary>
        public event Action ToggleDevicePressed;
        /// <summary>Night vision goggles down / up (N, hold View).</summary>
        public event Action ToggleNightVisionPressed;
        /// <summary>Space / tap A: jump, or vault and mantle when an obstacle is ahead (the consumer arbitrates).</summary>
        public event Action JumpPressed;
        /// <summary>I / hold D-pad down: procedural weapon inspection.</summary>
        public event Action InspectPressed;
        /// <summary>Lethal throwable: press pulls the pin and cooks, release throws.</summary>
        public event Action LethalPressed;
        public event Action LethalReleased;
        /// <summary>Tactical throwable: press pulls the pin and cooks, release throws.</summary>
        public event Action TacticalPressed;
        public event Action TacticalReleased;
        /// <summary>Options menu / leave a station (Escape, Start). Lives in the System map, which stays on with gameplay input off.</summary>
        public event Action MenuPressed;

        private InputActionMap playerMap, systemMap;
        private InputAction menu;
        private InputAction move, look, fire, aim, sprint, tacticalSprint, holdBreath, walkSlow, walkSpeedAdjust;
        private InputAction crouch, prone, leanLeft, leanRight, reload, checkMagazine, changeFireMode, switchSight, mountWeapon;
        private InputAction selectPrimary, selectSecondary, switchWeapon, interact, toggleDevice, throwLethal, throwTactical, toggleNightVision;
        private InputAction jump, inspect;
        private double lastReloadTapTime = double.NegativeInfinity;

        public Vector2 Move => move.ReadValue<Vector2>();

        /// <summary>
        /// Raw look input. A pointer delta in pixels for this frame when <see cref="LookIsPointerDelta"/>;
        /// otherwise a stick rate in [-1, 1] the consumer scales by deltaTime.
        /// </summary>
        public Vector2 Look => look.ReadValue<Vector2>();

        public bool LookIsPointerDelta { get; private set; } = true;
        public bool FireHeld => fire.IsPressed();
        public bool AimHeld => aim.IsPressed();
        public bool SprintHeld => sprint.IsPressed();
        public bool HoldBreathHeld => holdBreath.IsPressed();
        public bool WalkSlowHeld => walkSlow.IsPressed();
        public bool LeanLeftHeld => leanLeft.IsPressed();
        public bool LeanRightHeld => leanRight.IsPressed();
        /// <summary>Interact is held (hold-to-use objectives: plant, defuse, download).</summary>
        public bool InteractHeld => interact.IsPressed();
        /// <summary>Jump is held: airborne players mantle a ledge they reach.</summary>
        public bool JumpHeld => jump.IsPressed();
        public bool IsCheckingMagazine { get; private set; }
        public bool PlayerInputEnabled => playerMap != null && playerMap.enabled;

        /// <summary>Turns on the System map (menu key). Independent of gameplay input.</summary>
        public void EnableSystemInput()
        {
            Resolve();
            systemMap.Enable();
        }

        public void EnablePlayerInput()
        {
            Resolve();
            playerMap.Enable();
        }

        /// <summary>Disables the map. In-flight presses are canceled, so release events still fire.</summary>
        public void DisablePlayerInput()
        {
            if (playerMap == null) return;
            playerMap.Disable();
            lastReloadTapTime = double.NegativeInfinity;
        }

        private void OnEnable()
        {
            if (actionAsset != null) Resolve();
        }

        private void OnDisable()
        {
            if (playerMap == null) return;
            playerMap.actionTriggered -= OnActionTriggered;
            playerMap.Disable();
            playerMap = null;
            systemMap.actionTriggered -= OnSystemActionTriggered;
            systemMap.Disable();
            systemMap = null;
        }

        private void Resolve()
        {
            if (playerMap != null) return;
            if (actionAsset == null)
                throw new InvalidOperationException(
                    $"{name}: no InputActionAsset assigned. Run Tools/FPS Builder/2. Create Core Assets.");

            playerMap = actionAsset.FindActionMap(PlayerMapName, throwIfNotFound: true);
            move = Find("Move");
            look = Find("Look");
            fire = Find("Fire");
            aim = Find("AimDownSights");
            sprint = Find("Sprint");
            tacticalSprint = Find("TacticalSprint");
            holdBreath = Find("HoldBreath");
            walkSlow = Find("WalkSlow");
            walkSpeedAdjust = Find("WalkSpeedAdjust");
            crouch = Find("Crouch");
            prone = Find("Prone");
            leanLeft = Find("LeanLeft");
            leanRight = Find("LeanRight");
            reload = Find("Reload");
            checkMagazine = Find("CheckMagazine");
            changeFireMode = Find("ChangeFireMode");
            switchSight = Find("SwitchSight");
            mountWeapon = Find("MountWeapon");
            selectPrimary = Find("SelectPrimary");
            selectSecondary = Find("SelectSecondary");
            switchWeapon = Find("SwitchWeapon");
            interact = Find("Interact");
            toggleDevice = Find("ToggleDevice");
            toggleNightVision = Find("ToggleNightVision");
            jump = Find("Jump");
            inspect = Find("Inspect");
            throwLethal = Find("ThrowLethal");
            throwTactical = Find("ThrowTactical");
            playerMap.actionTriggered += OnActionTriggered;

            systemMap = actionAsset.FindActionMap(SystemMapName, throwIfNotFound: true);
            menu = systemMap.FindAction("Menu", throwIfNotFound: true);
            systemMap.actionTriggered += OnSystemActionTriggered;
        }

        private InputAction Find(string actionName) => playerMap.FindAction(actionName, throwIfNotFound: true);

        // One map-level callback instead of 36 per-action subscriptions; called for every phase change.
        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            InputAction action = context.action;

            if (action == look)
            {
                if (context.performed) LookIsPointerDelta = context.control.device is Pointer;
            }
            else if (action == fire)
            {
                if (context.performed) FirePressed?.Invoke();
                else if (context.canceled) FireReleased?.Invoke();
            }
            else if (action == aim)
            {
                if (context.performed) AimPressed?.Invoke();
                else if (context.canceled) AimReleased?.Invoke();
            }
            else if (action == throwLethal)
            {
                if (context.performed) LethalPressed?.Invoke();
                else if (context.canceled) LethalReleased?.Invoke();
            }
            else if (action == throwTactical)
            {
                if (context.performed) TacticalPressed?.Invoke();
                else if (context.canceled) TacticalReleased?.Invoke();
            }
            else if (action == checkMagazine)
            {
                // Hold: performed at the threshold, canceled on release (or on an early release, which is a reload tap).
                if (context.performed) SetCheckingMagazine(true);
                else if (context.canceled) SetCheckingMagazine(false);
            }
            else if (context.performed)
            {
                if (action == sprint) SprintPressed?.Invoke();
                else if (action == tacticalSprint) TacticalSprintPressed?.Invoke();
                else if (action == crouch) CrouchPressed?.Invoke();
                else if (action == prone) PronePressed?.Invoke();
                else if (action == reload) OnReloadTap(context.time);
                else if (action == changeFireMode) ChangeFireModePressed?.Invoke();
                else if (action == switchSight) SwitchSightPressed?.Invoke();
                else if (action == mountWeapon) MountWeaponPressed?.Invoke();
                else if (action == selectPrimary) WeaponSlotRequested?.Invoke(0);
                else if (action == selectSecondary) WeaponSlotRequested?.Invoke(1);
                else if (action == switchWeapon) WeaponSlotRequested?.Invoke(-1);
                else if (action == interact) InteractPressed?.Invoke();
                else if (action == toggleDevice) ToggleDevicePressed?.Invoke();
                else if (action == toggleNightVision) ToggleNightVisionPressed?.Invoke();
                else if (action == jump) JumpPressed?.Invoke();
                else if (action == inspect) InspectPressed?.Invoke();
                else if (action == walkSpeedAdjust)
                {
                    float scroll = context.ReadValue<float>();
                    if (scroll != 0f) WalkSpeedStepped?.Invoke(scroll > 0f ? 1 : -1);
                }
            }
        }

        private void OnSystemActionTriggered(InputAction.CallbackContext context)
        {
            if (context.action == menu && context.performed) MenuPressed?.Invoke();
        }

        private void OnReloadTap(double time)
        {
            if (time - lastReloadTapTime <= fastReloadWindow)
            {
                lastReloadTapTime = double.NegativeInfinity; // a third tap starts a new sequence
                FastReloadPressed?.Invoke();
                return;
            }

            lastReloadTapTime = time;
            ReloadPressed?.Invoke();
        }

        private void SetCheckingMagazine(bool checking)
        {
            if (IsCheckingMagazine == checking) return;
            IsCheckingMagazine = checking;
            CheckMagazineChanged?.Invoke(checking);
        }
    }
}
