# Claude of Duty — Project Guide

Grounded first-person tactical shooter (MW2019/2022 Realism feel): physical weapon weight, true bore-axis
ballistics, Picture-in-Picture optics, procedural inertia, photoreal HDRP.

**Engine:** Unity `6000.6.4f1` · HDRP `17.6.0` · Input System `1.20.0` (pinned in `Packages/manifest.json`).

## Fresh-clone setup

1. Unity Hub → Add → project from disk → this folder (editor 6000.6.4f1).
2. `Window > Rendering > HDRP Wizard` → **Fix All** (creates/assigns the HDRP asset).
3. `Tools/FPS Builder/1. Initialize Project Layers & Tags`
4. `Tools/FPS Builder/2. Create Core Assets` (creates `Data/Core/InputReader.asset` wired to the input actions).

Steps 3–4 are already applied and committed; both are idempotent, rerun after a settings reset.
Active Input Handling is `Input System Package (New)` (`activeInputHandler: 1`).

**Tests** (`Assets/_Project/Tests`, PlayMode, NUnit + `InputTestFixture`): Test Runner window, or headless:
`Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults results.xml`.
New non-trivial logic ships with a test there.

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
- **Data-driven:** tunables in ScriptableObjects named `*SO` (`WeaponDataSO`, `CaliberDataSO`, ...).
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
| `FPS.Weapons`        | `FPS.Weapons`    | Core, Audio, Ballistics                      | Weapon data, model rig/sockets, controller, recoil, optics, attachments |
| `FPS.Player`         | `FPS.Locomotion` | Core, Audio, Ballistics, Weapons             | Locomotion, stance/lean/mount, breathing, camera & viewmodel motion |
| `FPS.AI`             | `FPS.AI`         | Core, Audio, Ballistics, Weapons             | Perception, tactical brain, squad coordination |
| `FPS.UI`             | `FPS.UI`         | Core, Ballistics, Weapons, Player            | Diegetic HUD, menus |
| `FPS.Gameplay` (M9)  | `FPS.Gameplay`   | all runtime                                  | Match director, modes, equipment, save profiles |
| `FPS.Editor`         | `FPS.EditorTools`| all + Unity.InputSystem (Editor-only)        | Builders, wizards, validators |

**Rule:** references only point down the table. Upward communication goes through `EventBus<T>` structs or
interfaces declared in `FPS.Core`. Add HDRP references (`Unity.RenderPipelines.HighDefinition.Runtime`,
`Unity.RenderPipelines.Core.Runtime`) to an assembly only when it first needs them.

## Layers (created by `1. Initialize Project Layers & Tags`)

Names live in `FPS.Core.GameLayers`; always resolve indices through it, never hardcode numbers.

| Layer                  | Preferred index | Purpose |
|------------------------|-----------------|---------|
| `Ignore Raycast`       | 2 (built-in)    | Unity built-in; never hit by queries |
| `Player`               | 8               | Character capsules |
| `FirstPersonViewmodel` | 9               | First-person arms/weapon; rendered by the viewmodel camera, collides with nothing |
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

## Core services

- `EventBus<T>` — static typed bus, `T : struct`, handlers `void On(in T e)`. Subscribe in `OnEnable`,
  unsubscribe in `OnDisable`. Raising never allocates.
- `ObjectPoolManager.Instance` — scene-scoped, auto-created on first use. `Spawn(prefab, pos, rot, parent, lifetime)`,
  `Release(obj)`, `Prewarm(prefab, count, maxSize)`. Pooled prefabs implement `IPoolable` to reset state.

## Milestones

- [x] **M1** Project init, `CLAUDE.md`, assemblies, `InputReader`, `EventBus`, `ObjectPoolManager`, `ProjectSetupWizard`
- [ ] **M2** Locomotion biomechanics, stances, leaning, weapon mounting, breathing
- [ ] **M3** Spring solver, weapon inertia, barrel collision, procedural ADS, PiP optics, recoil
- [ ] **M4** Weapon/caliber data, model rig & sockets, chamber tracking, staged reloads
- [ ] **M5** Ballistics sim, penetration/ricochet, shell casings, health & armor
- [ ] **M6** Acoustic propagation, muzzle VFX, diegetic HUD
- [ ] **M7** Gunsmith & attachment physics
- [ ] **M8** Tactical AI & squad CQB
- [ ] **M9** Game modes, match director, tactical equipment
- [ ] **M10** 3D gunsmith workbench, loadout persistence, HDRP settings menu
