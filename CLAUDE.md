# Claude of Duty — Project Guide

Grounded first-person tactical shooter (MW2019/2022 Realism feel): physical weapon weight, true bore-axis
ballistics, Picture-in-Picture optics, procedural inertia, photoreal HDRP.

**Engine:** Unity `6000.6.4f1` · HDRP `17.6.0` · Input System `1.20.0` (pinned in `Packages/manifest.json`).

## Fresh-clone setup

1. Unity Hub → Add → project from disk → this folder (editor 6000.6.4f1).
2. `Tools/FPS Builder/1. Initialize Project Layers & Tags` — Linear color space, HDRP asset
   (`Settings/Rendering/HDRenderPipelineAsset.asset`), layers, collision matrix.
3. `Tools/FPS Builder/2. Create Core Assets` (creates `Data/Core/InputReader.asset` wired to the input actions).
4. `Tools/FPS Builder/3. Create Test Weapon` → `Prefabs/Weapons/TestRifle.prefab` (primitive rifle, 4x PiP scope,
   45° canted irons, every socket) + `Data/Weapons/TestRifle_Data/_Recoil`; re-equips the player prefab.
5. `Tools/FPS Builder/4. Create Player Prefab` → `Prefabs/Player/Player.prefab` (completes an existing prefab in place).
6. `Tools/FPS Builder/5. Build Test Range` → `Scenes/LocomotionTestRange.unity` (ramps, ice, ledges, corner, crouch and
   crawl tunnels, narrow corridor, targets at 25/50/100/200 m).
7. Optional: `Window > Rendering > HDRP Wizard` for any remaining platform checks.

Steps 2–6 are already applied and committed; all are idempotent, rerun after a settings reset.
Active Input Handling is `Input System Package (New)` (`activeInputHandler: 1`).

**Tests** (`Assets/_Project/Tests`, PlayMode, NUnit + `InputTestFixture`): Test Runner window, or headless:
`Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults results.xml`.
New non-trivial logic ships with a test there.
Visual check: set `FPS_CAPTURE_DIR` and run `-testFilter FPS.Tests.RenderCapture` **without** `-nographics`; it renders
hip/ADS/canted PNGs of the test range (ignored otherwise).

## Coding standards

- All game code lives in `Assets/_Project/Scripts/<Assembly>/`, one `.asmdef` per folder. No loose scripts.
- **Zero stub code.** No `TODO`, no placeholder methods, no empty overrides. Every file on disk is complete.
- **Zero allocation on hot paths** (Update/LateUpdate/FixedUpdate, fire, impact):
  `Physics.*NonAlloc` / command batches with preallocated buffers; spawn through `ObjectPoolManager`;
  events are structs on `EventBus<T>`; no LINQ, lambdas capturing locals, boxing, `GetComponent` in loops,
  string concat, or `foreach` over non-`List` interfaces.
- **Domain-reload safe:** Enter Play Mode without domain reload must work. Statics reset via
  `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`.
- **SI units everywhere** (m, kg, s, m/s, J). Imperial values only as authoring fields (e.g. barrel length in
  inches) converted once when data is read.
- **Data-driven:** per-asset data (weapons, calibers, attachments, surfaces) in ScriptableObjects named `*SO`
  (`WeaponDataSO`, `CaliberDataSO`, ...); per-prefab tunables are `[SerializeField]` fields with SI tooltips.
  Never hardcode mesh, clip, or asset paths in runtime code; models bind through socket components
  (`WeaponModelRig`). Editor tools may use paths, defined as `const` at the top of the tool.
- **Input** only through `FPS.Core.InputReader`: continuous state is polled (properties), discrete intents are
  events. Context arbitration (Shift = sprint vs. hold breath while aiming) belongs to the consumer.
- **Editor automation over manual setup:** anything needing hierarchies, SO instances or Animator setup gets a
  `[MenuItem("Tools/FPS Builder/...")]` tool in `Scripts/Editor`, numbered in execution order.
- Naming: `private` fields `camelCase` with `[SerializeField]`; public API `PascalCase` properties/methods;
  constants `PascalCase`. Every MonoBehaviour/ScriptableObject in its own file named after it (Unity requirement).
- Editor code uses namespace `FPS.EditorTools` (not `FPS.Editor`, which would shadow `UnityEditor.Editor`).
- Bullets spawn from the weapon's `MuzzleSocket` along the barrel's forward vector, never from screen center.

## Assemblies & namespaces

| Assembly (`.asmdef`) | Namespace        | References                                   | Scope |
|----------------------|------------------|----------------------------------------------|-------|
| `FPS.Core`           | `FPS.Core`       | Unity.InputSystem                            | Input, EventBus, pooling, layers, spring math, shared interfaces/event structs |
| `FPS.Audio`          | `FPS.Audio`      | Core                                         | Acoustic propagation, weapon audio layers |
| `FPS.Ballistics`     | `FPS.Ballistics` | Core, Audio                                  | Calibers, projectile sim, penetration/ricochet, casings, hitboxes, health/armor |
| `FPS.Player`         | `FPS.Locomotion` | Core, Audio, Ballistics                      | Locomotion, stance/lean/mount, breathing, player camera chain |
| `FPS.Weapons`        | `FPS.Weapons`    | Core, Audio, Ballistics, Player, HDRP runtime | Weapon data, model rig/sockets, viewmodel (inertia, ADS/PiP optics, recoil), controller, attachments |
| `FPS.AI`             | `FPS.AI`         | Core, Audio, Ballistics, Player, Weapons     | Perception, tactical brain, squad coordination |
| `FPS.UI`             | `FPS.UI`         | Core, Ballistics, Weapons, Player            | Diegetic HUD, menus |
| `FPS.Gameplay` (M9)  | `FPS.Gameplay`   | all runtime                                  | Match director, modes, equipment, save profiles |
| `FPS.Editor`         | `FPS.EditorTools`| all + Unity.InputSystem (Editor-only)        | Builders, wizards, validators |

**Rule:** references only point down the table. Upward communication goes through `EventBus<T>` structs, interfaces
declared in `FPS.Core`, or push-style setters (weapons call `RealisticLocomotionController.SetWeaponHandling`).
Weapons sits above Player because the viewmodel reads live player state (aim intent, sway, mount, velocity).
Add HDRP references (`Unity.RenderPipelines.HighDefinition.Runtime`, `Unity.RenderPipelines.Core.Runtime`) to an
assembly only when it first needs them.

## Layers (created by `1. Initialize Project Layers & Tags`)

Names live in `FPS.Core.GameLayers`; always resolve indices through it, never hardcode numbers.

| Layer                  | Preferred index | Purpose |
|------------------------|-----------------|---------|
| `Ignore Raycast`       | 2 (built-in)    | Unity built-in; never hit by queries |
| `Player`               | 8               | Character capsules |
| `FirstPersonViewmodel` | 9               | First-person arms/weapon (rendered in-world by the main camera, culled by the PiP camera), collides with nothing; also hosts scope-only volumes |
| `WorldGeometry`        | 10              | Static/dynamic level geometry; ballistics, barrel tuck and lean casts target it |
| `Hitbox`               | 11              | Per-bone damage colliders (kinematic) |
| `Interactable`         | 12              | Ray-targeted interaction objects |

Collision matrix: `FirstPersonViewmodel` ignores everything; `Player`↔`Hitbox`, `Hitbox`↔`Hitbox`,
`Interactable`↔`Player`, `Interactable`↔`Hitbox` ignored. No custom tags yet; add to the wizard when a system needs one.

## Input (`Assets/_Project/Settings/Input/InputSystem_Actions.inputactions`, map `Player`)

| Action | Keyboard & Mouse | Gamepad | InputReader |
|--------|------------------|---------|-------------|
| Move | WASD | Left stick | `Move` |
| Look | Mouse delta | Right stick | `Look`, `LookIsPointerDelta` |
| Fire | LMB | RT | `FirePressed/Released`, `FireHeld` |
| AimDownSights | RMB | LT | `AimPressed/Released`, `AimHeld` |
| Sprint | LShift | L3 | `SprintPressed`, `SprintHeld` |
| TacticalSprint | LShift ×2 | L3 ×2 | `TacticalSprintPressed` |
| HoldBreath | LShift | L3 | `HoldBreathHeld` |
| WalkSlow | LAlt | — | `WalkSlowHeld` |
| WalkSpeedAdjust | Scroll wheel | — | `WalkSpeedStepped(±1)` |
| Crouch | C (tap) | B (tap) | `CrouchPressed` (slide when sprinting) |
| Prone | C (hold), LCtrl | B (hold) | `PronePressed` (dive when sprinting) |
| LeanLeft / LeanRight | Q / E | D-pad ← / → | `LeanLeftHeld`, `LeanRightHeld` |
| Reload | R (tap) | X (tap) | `ReloadPressed`; 2nd tap in window → `FastReloadPressed` |
| CheckMagazine | R (hold) | X (hold) | `CheckMagazineChanged(bool)` |
| ChangeFireMode | B | D-pad ↓ | `ChangeFireModePressed` |
| SwitchSight | X | D-pad ↑ | `SwitchSightPressed` |
| MountWeapon | V | R3 | `MountWeaponPressed` |

Tap/hold threshold 0.35 s. Fast reload is an *upgrade*: the first tap always starts a tactical reload.

## Player rig (M2)

`PlayerRig` (root) holds the `InputReader` and the transform chain; `PlayerRig.Build(root, input)` creates it.
**One writer per transform**; new systems (M3 springs, recoil, viewmodel) add their own transform, never share one.

```
Player            [CharacterController, PlayerRig, Locomotion (yaw), StanceAndLean, HeadbobAndBreathing]
└ BodyPivot       StanceAndLean: torso height, prone ground pitch
  └ LeanPivot     StanceAndLean: lean roll (±18°, sphere-cast limited)
    └ HeadPivot   StanceAndLean: eye offset (stance + mount height)
      └ PitchPivot    Locomotion: look pitch
        └ HeadMotion  HeadbobAndBreathing: bob, landing, slide/dive posture, camera share of sway
          ├ CameraOffset      PhysicalRecoilEngine: damped camera concussion
          │ └ PlayerCamera    ADS controller writes fieldOfView
          └ ViewmodelRoot     (ViewmodelRig, M3; layer FirstPersonViewmodel)
            └ InertiaPivot    RealisticWeaponInertia: handling spring, free-aim lead, trail, postures, sway, barrel tuck
              └ RecoilPivot   PhysicalRecoilEngine: per-shot kick about the stock contact point
                └ AdsPivot    ProceduralADSAndOpticsController: hip ↔ solved sight alignment
                  └ weapon model (WeaponModelRig)
```

Pivots compute their pose from points expressed in their own local space (stock socket, muzzle), so rotations act
about the stock and the barrel probe uses the untucked pose; there is no feedback between layers.

Component dependencies point one way: `HeadbobAndBreathingSystem → RealisticLocomotionController → StanceAndLeanController`.
Contracts for later milestones:
- `RealisticLocomotionController.IsAiming` is the arbitrated aim intent (ADS reads it); `Pace`, `IsTacticalSprinting`
  (high-ready posture), `CancelSprint()` (fire/reload), `SetWeaponHandling(massKg, ergonomics)` (on equip).
- `StanceAndLeanController.SwayMultiplier` / `RecoilMultiplier` (mounted bracing), `LeanAngle`, `Mount`.
- `HeadbobAndBreathingSystem.WeaponSwayEuler` (deg, figure-8 + wander + heartbeat) for the weapon/optic.
- Ground traction comes from the collider's `PhysicsMaterial.dynamicFriction` (0.6 = full grip).
- `RealisticLocomotionController.AddLookOffset` (recoil climb) and `LookSensitivityMultiplier` (zoom/optic).

## Viewmodel & optics (M3)

- **Weapon models:** `WeaponModelRig` on the model root holds the sockets (`EnsureSockets()` binds children by name
  and creates missing ones). Weapon +Z forward, +Y up. `WeaponSight` on a sight socket (or an optic child) is the
  aligner: +Z line of sight, +Y reticle up, origin at the aperture/eyepiece; magnification > 1 with a lens renderer
  enables PiP. The lens material is `HDRP/Unlit`; the controller feeds `_UnlitColorMap` through a property block.
- **ADS** is solved, not animated: `ProceduralADSAndOpticsController.SolveSightAlignment` puts the aligner `eyeRelief`
  ahead of the eye. `SwitchSight` rolls onto the canted slot. Tucked barrels block ADS.
- **PiP:** one scope camera per player renders to an `R16G16B16A16_SFloat` target, FOV = lens angular size ÷
  magnification. Volume filtering by layer: main camera excludes `FirstPersonViewmodel` (scope-only volume: vignette
  eye-relief shadow, tonemapping None, no motion blur); scope camera excludes `Player` (main-only peripheral DOF).
- **Recoil:** `PhysicalRecoilEngine.Kick()` once per round (M4 firing calls it). Springs come from `RecoilProfileSO`.
- **Springs:** `FPS.Core.ProceduralSpringSolver` — analytical damped oscillator, sub-stepped target interpolation.

## Core services

- `EventBus<T>` — static typed bus, `T : struct`, handlers `void On(in T e)`. Subscribe in `OnEnable`,
  unsubscribe in `OnDisable`. Raising never allocates.
- `ObjectPoolManager.Instance` — scene-scoped, auto-created on first use. `Spawn(prefab, pos, rot, parent, lifetime)`,
  `Release(obj)`, `Prewarm(prefab, count, maxSize)`. Pooled prefabs implement `IPoolable` to reset state.
- `GameLayers.EnvironmentMask` — Default + WorldGeometry; every environment query uses it.
- `SuppressionEvent` (EventBus) — local-player suppression, `Intensity` 0–1; raised by ballistics (M5).

## Milestones

- [x] **M1** Project init, `CLAUDE.md`, assemblies, `InputReader`, `EventBus`, `ObjectPoolManager`, `ProjectSetupWizard`
- [x] **M2** Locomotion biomechanics, stances, leaning, weapon mounting, breathing
- [x] **M3** Spring solver, weapon inertia, barrel collision, procedural ADS, PiP optics, recoil
- [ ] **M4** Weapon/caliber data, model rig & sockets, chamber tracking, staged reloads
- [ ] **M5** Ballistics sim, penetration/ricochet, shell casings, health & armor
- [ ] **M6** Acoustic propagation, muzzle VFX, diegetic HUD
- [ ] **M7** Gunsmith & attachment physics
- [ ] **M8** Tactical AI & squad CQB
- [ ] **M9** Game modes, match director, tactical equipment
- [ ] **M10** 3D gunsmith workbench, loadout persistence, HDRP settings menu
