# Claude of Duty — Project Guide

Grounded first-person tactical shooter (MW2019/2022 Realism feel): physical weapon weight, true bore-axis
ballistics, Picture-in-Picture optics, procedural inertia, photoreal HDRP.

**Engine:** Unity `6000.6.4f1` · HDRP `17.6.0` · Input System `1.20.0` (pinned in `Packages/manifest.json`).

## Fresh-clone setup

1. Unity Hub → Add → project from disk → this folder (editor 6000.6.4f1).
2. `Tools/FPS Builder/1. Initialize Project Layers & Tags` — Linear color space, HDRP asset
   (`Settings/Rendering/HDRenderPipelineAsset.asset`), layers, collision matrix.
3. `Tools/FPS Builder/2. Create Core Assets` (creates `Data/Core/InputReader.asset` wired to the input actions).
4. `Tools/FPS Builder/3. Create Test Weapons` → calibers in `Data/Ballistics` (5.56, 7.62, 9x19, .300 WM),
   `Prefabs/Weapons/TestRifle` (modular 5.56 closed-bolt receiver with attachment mounts and 45° canted irons),
   `TestMarksman` (.300 WM bolt action, fixed 8x scope), `BattleRifle` (7.62, optic/grip/rail mounts) and `Sidearm`
   (9 mm, reciprocating slide), their data, recoil profiles and droppable magazines, the gunsmith parts in
   `Prefabs/Weapons/Attachments` + `Data/Weapons/Attachments`; re-builds the player prefab. Models come from
   `Art/Models` (Meshy imports) or the procedural fallback (see Asset pipeline).
5. `Tools/FPS Builder/4. Create Player Prefab` → `Prefabs/Player/Player.prefab`: rig, viewmodel, IK arms, loadout
   (carbine primary, marksman secondary). Completes an existing prefab in place.
6. `Tools/FPS Builder/5. Build Test Range` → `Scenes/LocomotionTestRange.unity` (ramps, ice, ledges, corner, crouch and
   crawl tunnels, narrow corridor, targets at 25/50/100/200 m, dusty shoot house, canyon, thin volumetric fog). It also
   (re)creates the AI content — `Prefabs/AI/AI_Soldier`, grenades in `Prefabs/Equipment` + `Data/Equipment`, callout
   voices in `Data/AI` (Windows speech synthesis; silent elsewhere until clips are assigned) — places the firefight
   yard with Squad Alpha, makes the shoot house a `CQBRoom` and bakes `Scenes/LocomotionTestRange_NavMesh`.
7. `Tools/FPS Builder/7. Build Game Mode Scenes` → `Scenes/Mode_Raid`, `Mode_SearchAndDestroy`, `Mode_Hardpoint` (one
   arena: two-room warehouse, west compound, east container yard; each with its director, objectives, spawns and
   NavMesh) and adds them to the build settings. Needs steps 3–4.
8. `Tools/FPS Builder/8. Build Armory` → `Scenes/Armory.unity` (the hub, first in the build settings): workshop with
   the gunsmith workbench, ammo and equipment, opening onto a 30 m indoor range; also `Data/Armory/ArmoryCatalog`
   and HDRP dynamic resolution (TAAU, DLSS, FSR2) in the pipeline asset.
9. Optional: `Window > Rendering > HDRP Wizard` for any remaining platform checks.

**Importing a weapon model:** select the FBX/GLTF → `Tools/FPS Builder/6. Build Weapon From Selected Model` (or
right-click → FPS → Build Weapon Prefab From Model). It wraps the model under a `WeaponModelRig` root (model as child
`Model` so orientation/scale fixes never move sockets), converts materials to HDRP/Lit, strips colliders, sets the
viewmodel layer, guesses sockets (named nodes first, then bounds) and magazine renderers, and creates `<Name>_Data`.
Then open the prefab: the WeaponModelRig inspector lists validation issues and offers fix buttons; place sockets with
the gizmos (red muzzle, magenta stock, cyan sights, green grips), add a `WeaponSight` to optics.

Steps 2–8 are already applied and committed; all are idempotent, rerun after a settings reset.
Active Input Handling is `Input System Package (New)` (`activeInputHandler: 1`).

**Tests** (`Assets/_Project/Tests`, PlayMode, NUnit + `InputTestFixture`): Test Runner window, or headless:
`Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults results.xml`.
New non-trivial logic ships with a test there.
Visual check: set `FPS_CAPTURE_DIR` and run `-testFilter FPS.Tests.RenderCapture` **without** `-nographics`; it renders
the test range (hip/ADS/canted/reload/impacts/devices/night vision/gunsmith builds/AI firefight), the three mode scenes and the
armory (workbench views, indoor range) to PNGs and logs stats (ignored otherwise).

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
| `FPS.Ballistics`     | `FPS.Ballistics` | Core                                         | `CaliberDataSO`, `ShotFiredEvent`, projectile sim, penetration/ricochet, casings, hitboxes, health/armor |
| `FPS.Audio`          | `FPS.Audio`      | Core, Ballistics, HDRP runtime               | World presentation: acoustics, weapon audio profiles, impact decals/dust/audio, particle factory |
| `FPS.Player`         | `FPS.Locomotion` | Core, Audio, Ballistics, HDRP runtime        | Locomotion, stance/lean/mount, breathing, suppression feedback, player camera chain |
| `FPS.Weapons`        | `FPS.Weapons`    | Core, Audio, Ballistics, Player, HDRP runtime | Weapon data, model rig/sockets, viewmodel (inertia, ADS/PiP optics, recoil), controller, attachments |
| `FPS.AI`             | `FPS.AI`         | Core, Audio, Ballistics, Player, Weapons, HDRP runtime | Perception, soldier body/weapon, tactical brain, squad coordination, CQB rooms |
| `FPS.UI`             | `FPS.UI`         | Core, Ballistics, Audio, Player, Weapons, UnityEngine.UI, HDRP | Diegetic HUD, menus |
| `FPS.Gameplay`       | `FPS.Gameplay`   | all runtime above + UnityEngine.UI, HDRP     | Match director, game modes, objectives, spawns, match HUD |
| `FPS.Editor`         | `FPS.EditorTools`| all + Unity.InputSystem, Unity.AI.Navigation (Editor-only) | Builders, wizards, validators |

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
| `Player`               | 8               | Character capsules (player and AI soldiers) |
| `FirstPersonViewmodel` | 9               | First-person arms/weapon (rendered in-world by the main camera, culled by the PiP camera), collides with nothing; also hosts scope-only volumes |
| `WorldGeometry`        | 10              | Static/dynamic level geometry; ballistics, barrel tuck and lean casts target it |
| `Hitbox`               | 11              | Per-bone damage colliders (kinematic): dummies, AI soldiers and the player (`PlayerRig.Build`) |
| `Interactable`         | 12              | Ray-targeted interaction objects |
| `Debris`               | 13              | Shell casings, dropped magazines: hit the world, never players, hitboxes or bullets |

Collision matrix: `FirstPersonViewmodel` ignores everything; `Player`↔`Hitbox`, `Hitbox`↔`Hitbox`,
`Interactable`↔`Player`, `Interactable`↔`Hitbox`, `Debris`↔`Player/Hitbox/Interactable` ignored.
Bullets use `GameLayers.BallisticMask` (Default + WorldGeometry + Hitbox). No custom tags yet; add to the wizard when a system needs one.

## Input (`Assets/_Project/Settings/Input/InputSystem_Actions.inputactions`, map `Player`)

| Action | Keyboard & Mouse | Gamepad | InputReader |
|--------|------------------|---------|-------------|
| Move | WASD | Left stick | `Move` |
| Look | Mouse delta | Right stick | `Look`, `LookIsPointerDelta` |
| Fire | LMB | RT | `FirePressed/Released`, `FireHeld` |
| SelectPrimary / SelectSecondary | 1 / 2 | — | `WeaponSlotRequested(0/1)` |
| SwitchWeapon | — | Y | `WeaponSlotRequested(-1)` |
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
| Interact | F | A | `InteractPressed` (`Interactor` → focused `IInteractable`); `InteractHeld` for hold-to-use objectives |
| ToggleDevice | T | View (tap) | `ToggleDevicePressed` (all lights/lasers on the held weapon) |
| ToggleNightVision | N | View (hold 0.4 s) | `ToggleNightVisionPressed` (`NightVisionGoggles`) |
| ThrowLethal | G, mouse forward | RB | `LethalPressed` (pin, cook) / `LethalReleased` (throw) |
| ThrowTactical | H, mouse back | LB | `TacticalPressed` / `TacticalReleased` |
| Menu (map `System`) | Escape | Start | `MenuPressed`; the System map is enabled with `EnableSystemInput()` and stays on while gameplay input is off |

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
                └ ActionPivot RealisticWeaponController: reload, mag check, bolt, holster/draw poses
                  └ AdsPivot  ProceduralADSAndOpticsController: hip ↔ solved sight alignment
                    └ weapon model (WeaponModelRig)
            └ Arms            FirstPersonArmsIK (two-bone IK to the weapon's grip sockets)
```

Hitboxes: head under HeadPivot, thorax/stomach/arms under LeanPivot, legs under BodyPivot (they follow stance, lean
and prone). `FlashbangFeedback` (whiteout through a Player-layer volume) sits on the root with `SuppressionFeedback`.

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

## Weapon mechanics (M4)

- **Data split:** `WeaponDataSO` = firearm (mass, ergonomics, barrel inches, bolt type, fire modes, RPM, MoA, capacity,
  bolt hold-open, stage timings, hip pose, recoil); `CaliberDataSO` = ammunition (mass, diameter, velocity vs. barrel
  length, G1/G7 BC, penetration, damage). Builders and tests set private fields by name — rename fields with care.
- **State:** `WeaponState` per carried weapon survives holstering. Closed bolt / bolt action start at capacity + 1;
  open bolt never has +1 (`RoundChambered` means "bolt cocked"). Closed bolt with hold-open locks back on the last
  round; bolt actions auto-cycle after the trigger is released.
- **Reloads** are staged: `MagazineRemoved → MagazineInserted → BoltCycled` (charge only if nothing is chambered;
  bolt release is 0.6× when locked back). Sprint, dive, weapon swap or firing the chambered round interrupt; the next
  reload press resumes after the last completed stage. Tap R = tactical (partial magazine back in the pouch), second
  tap during removal = speed (pooled physical magazine dropped), hold R = magazine check (`MagazineEstimate`).
- **Firing:** `RealisticWeaponController` raises `ShotFiredEvent` from `MuzzleSocket` along the bore with MoA
  dispersion and barrel-adjusted velocity, then `PhysicalRecoilEngine.Kick()`. Fire cancels sprint (sprint-to-fire
  delay); tucked barrels, dives and prone transitions block it. Every mechanical action raises `WeaponActionEvent`.
- **Imported magazines:** list the magazine meshes in `WeaponModelRig.magazineRenderers`; they hide while it's out.

## Core services

- `EventBus<T>` — static typed bus, `T : struct`, handlers `void On(in T e)`. Subscribe in `OnEnable`,
  unsubscribe in `OnDisable`. Raising never allocates.
- `ObjectPoolManager.Instance` — scene-scoped, auto-created on first use. `Spawn(prefab, pos, rot, parent, lifetime)`,
  `Release(obj)`, `Prewarm(prefab, count, maxSize)`. Pooled prefabs implement `IPoolable` to reset state.
- `GameLayers.EnvironmentMask` — Default + WorldGeometry; every environment query uses it.
- `SuppressionEvent` (EventBus) — `Listener` (head/camera transform), `Intensity` 0–1, closest `Point`, `Direction`;
  consumers ignore events for other listeners.
- `FlashbangEvent` (EventBus) — `Position`, `Intensity`; every eye/ear computes its own `FlashbangEvent.Exposure`
  (distance, facing, occlusion → blindness; deafness passes walls at 60%).
- `BallisticsSimulationManager.Clear()` drops all rounds in flight (round resets, test teardown).
- `VisionObstruction` — smoke clouds as extinction spheres; `Transmittance(from, to)` integrates sight lines through
  them (AI sight and muzzle-flash spotting use it).

## Ballistics & damage (M5)

- `BallisticsSimulationManager` (one per scene, auto-created; place one to set wind, default and flesh surfaces) launches a
  projectile per `ShotFiredEvent`, integrates gravity + G1/G7 drag against the wind (`BallisticsMath`), and resolves
  all segments with one `RaycastCommand` batch per frame. Up to 4 interactions per round per frame.
- **Penetration:** energy cost per metre = `SurfaceMaterialSO.Resistance` (0.007 × density × hardness) × bullet
  cross-section (mm²) × AP factor (1.5 → 0.5 by penetration power). Thickness is measured by casting back from the
  deepest reachable point (`Collider.Raycast`); exit speed comes from the leftover energy. Zero-thickness mesh planes
  cannot be measured and stop rounds — use solid colliders for penetrable walls.
- **Ricochet:** impacts shallower than `ricochetAngle` glance off with probability `1 - angle/ricochetAngle`.
- **Surfaces:** `SurfaceIdentifier` on a collider; otherwise the simulation's default. Builder surfaces: Concrete, Brick,
  Wood, Drywall, Metal, Glass, Dirt, Flesh (each with synthesized casing clinks).
- **Bodies:** `Hitbox` (zone + tissue surface) on Hitbox-layer colliders under a `TacticalHealthAndArmorSystem`.
  Plates/helmet (`ArmorPieceSO`) cover zones by direction; a logistic roll of energy-scaled penetration vs. protection
  (degraded by wear) decides stop (blunt trauma) or defeat (residual absorption). Limb damage → `LegInjury01` (slower
  sprint, no tactical sprint) and `ArmInjury01` (more sway). Raises `DamageEvent`, `Damaged`, `Died`.
- **Near misses:** heads registered in `NearMissListeners` (the player's `SuppressionFeedback` registers its camera)
  get a `SuppressionEvent` when a supersonic round passes within 2 m: heart-rate stress, head flinch, focal blur.
- **Casings:** `CaliberDataSO.casingPrefab` (`PhysicalShellCasing`, pooled, Debris layer) ejected from the
  `ChamberEjectionSocket` on each shot (on the bolt stroke for bolt actions).
- Every bullet-surface interaction raises `ProjectileImpactEvent` (decals and impact audio arrive in M6).

## Audio, VFX & HUD (M6)

- **Acoustics** (`AcousticPropagationSystem`, on the player root, needs the camera's AudioListener): weapons raise
  `GunshotEvent` with their `WeaponAudioProfileSO`. Own shots = first-person transient + mechanism + body layers;
  others arrive after `distance / 343 m/s` as the close or (beyond `distantThreshold`) distant report. Reverb tails
  (indoor / urban / open, one restarting voice each) are weighted by a 9-ray environment probe around the listener.
  Supersonic near misses play a crack at the closest point; suppression and low health sweep an `AudioLowPassFilter`
  on the listener. Weapon handling foley comes from `WeaponActionEvent`.
- **Impacts** (`ImpactEffectsController`, on the player): per `ProjectileImpactEvent`, a pooled HDRP `DecalProjector`
  using `SurfaceMaterialSO.impactDecal` (parented to the hit collider), a dust puff tinted `impactDustColor`, and the
  surface's impact clip delayed by distance.
- **Muzzle** (`MuzzleAndSmokeVFXController`, on the player): one-frame HDRP point-light flash (lumen units), gas burst
  per shot, barrel heat (0–1, exponential cooling) driving distortion heat haze and lingering breech smoke.
  `GunshotEvent.MuzzleFlash` carries the build's flash multiplier (AI sees unsuppressed flashes).
- Impacts on the listener's own body spawn no puffs or decals. Stun grenades deafen through the acoustic system's
  low-pass plus a looping `tinnitus` clip.
- **Particles:** HDRP/Unlit has no vertex colour, so `EffectParticles` systems fade via a 4×4 alpha flipbook texture
  (`Art/VFX/PuffFlipbook`); tint per instance with a `_UnlitColor` property block.
- **HUD** (`DiegeticTacticalHUD`, uGUI built in code, legacy built-in font): compass tape + bearing, interaction
  prompt, fire-mode / magazine status that fades in only on mode switch or magazine check, optional crosshair (off),
  stress vignette + desaturation (main-camera-only volume on the Player layer).
- **Placeholder sound design:** all clips are synthesized by `AudioSynth` (editor) into `Art/Audio`; replace clips in
  the profiles/surfaces with recorded audio without code changes.

## Gunsmith & attachments (M7)

- **Parts:** `AttachmentDataSO` (slot, prefab, mass, part-space centre of mass, ergonomics delta, barrel inches, recoil
  vertical/horizontal/back multipliers, flash and velocity multipliers, audio override, ADS-time multiplier). Slots:
  Barrel, Muzzle, Optic, Underbarrel, Stock, Laser, Light; each attaches at a `{Slot}Mount` transform found anywhere
  under the weapon (`AttachmentDataSO.MountName`), so a part can carry the next mount (barrels carry `MuzzleMount`,
  the optic mount sits under `PrimarySightSocket`). A part may carry `MuzzleSocket`, `LeftHandIKGrip` or
  `StockPivotSocket` children (they replace the receiver's via `WeaponModelRig.SetSocket`) and a `WeaponSight`.
- **Stats:** `GunsmithAssembler.Assemble` returns a runtime **clone** of the weapon data with the build applied (assets
  are never modified; the controller destroys replaced clones). `AttachParts` instantiates parts barrel-first, then
  measures the real centre of mass and the moment of inertia about the stock (receiver gyration 0.08 m + parts as point
  masses). `WeaponDataSO.massKg`/`centerOfMass` describe the bare receiver; `BaseCenterOfMass` stays stable on refits.
- **Effects:** inertia springs scale with 1/sqrt(inertia / (3.4 kg × 0.45² m)), recoil with the multipliers, muzzle
  velocity with barrel inches × velocity multiplier, muzzle flash and gas with the flash multiplier (0 = suppressed),
  ADS time with the multiplier; a suppressor's `audioOverride` replaces the weapon's audio profile.
- **Loadouts:** `LoadoutSlot.attachments` (empty = `WeaponDataSO.defaultAttachments`); `SetAttachment(slot, type, part)`
  refits live, keeping rounds, chamber and reload progress. `GetAttachments(slot)` lists the build.
- **Devices:** `TacticalDevice` (Flashlight: HDRP spot in candela with `affectsVolumetric`; Laser: `LineRenderer` +
  world-layer dot raycast against `BallisticMask`). Fitted part templates may be inactive; fitted copies are activated.
- **Test parts:** barrels 10.5/14.5/20", flash hider, compensator, suppressor (synthesized suppressed profile), 4x
  scope, red dot, vertical/angled grips, fixed/collapsed stocks, laser, light. Default carbine build ≈ 3.5 kg.

## Tactical AI (M8)

- **Soldier** (`Prefabs/AI/AI_Soldier`, layer Player): `NavMeshAgent` + `AIBody` (procedural: stance height, lean roll
  about the pelvis, body yaw follows the aim, aim pivot at the shoulder with turn-rate limit and recoil, two-bone IK
  arms, falling death that drops the rifle), `AIWeaponHandler` (same `WeaponDataSO` + gunsmith build as the player,
  rounds from the `MuzzleSocket` along the bore, magazine/chamber counting, reloads with foley events, muzzle-flash
  light), `AIPerceptionSensor`, `TacticalAIBrain`, `PerceivableTarget` (team 1), `TacticalHealthAndArmorSystem`.
- **Targets:** `PerceivableTarget` on anything AI perceives (the player prefab gets team 0): head/chest/pelvis points,
  conspicuity (stance + motion), footstep noise radius (speed × stance), lit flashlight (`TacticalDevice.LitFlashlights`).
- **Perception** (10 Hz tick, staggered): awareness per target rises with visible fraction (3 line casts), light level
  (`AIIllumination`: sun shadow ray + 5 sky rays + flashlight cones → dark corners shrink sight range to 12%),
  conspicuity, focus vs. peripheral vision and distance; 1 = identified (`TargetIdentified`). Hearing: footsteps,
  gunshots (`MaxAudibleDistance`, halved through walls; suppressed shots located with 30% error vs 8%), never
  identifies. Unsuppressed muzzle flashes in view identify the shooter. Near misses/hits → `Suppression`;
  `FlashbangEvent` → `Blindness` (stunned above 0.5) / `Deafness`. Identified targets are remembered 25 s.
- **Brain states:** Idle/patrol → Suspicious look → Investigate (walk to the LKP pre-aiming past path corners: slicing
  the pie) → Engage (find cover with `CoverFinder`: concealed crouched/standing, `PeekMode` StandUp / LeanLeft /
  LeanRight; peek-and-fire cycles with reaction time, settling and suppression-scaled aim error) → Suppress (bursts
  into the LKP area for 6 s after losing sight; may chew the target's cover, never its own) → Investigate. A target
  hidden near one spot for 4 s gets a cooked frag (`GrenadeBallistics` arc solve + sweep). Squad orders: Flank,
  Stack, Breach (pie arc → doorway → point of domination), Throw; a throw resumes the interrupted order.
- **Squad** (`SquadCoordinator` on the parent of the soldiers): pools knowledge (one sighting engages everyone),
  assigns a flanker after 2.5 s without eyes on the target or when squad fire suppresses it ("Target suppressed!"),
  clears a `CQBRoom` holding the LKP: stack on the nearest entry, flashbang (stack looks away), then one man through
  the door at a time to the domination points, "Clear!". Also grenade cooldown, cover claims, friendly-fire line
  check, "Covering!" for reloads, "Man down!".
- **Callouts:** `CalloutType` → `SquadCalloutEvent` + a clip from the soldier's `AIVoiceProfileSO` (shared callouts
  deduplicated squad-wide). Replace the synthesized clips with recorded lines in the voice profiles.
- **Grenades** (`GrenadeDataSO`, pooled `ThrownGrenade`, Debris layer): frag = 64 fragment projectiles through the
  ballistics sim (`Caliber_Fragment`) + 4 m blast overpressure (`TacticalHealthAndArmorSystem.ApplyDamage`, blocked by
  cover); stun = `FlashbangEvent`. Both flash, report via `GunshotEvent` (explosion profiles) and suppress listeners.
- **Rooms:** `CQBRoom` (interior box, entries with +Z into the room, domination points); `CQBRoom.SliceArc` /
  `SliceAim` give the pie-slicing approach.

## Equipment & game modes (M9)

- **Throwables** (`TacticalEquipmentSystem`, on the player; lethal + tactical `Loadout` slots): press lowers the rifle
  (`RealisticWeaponController.LowerForEquipment`, action `Equipment`, trigger blocked) and pulls the pin (0.35 s);
  then the spoon flies and the fuse burns while held (cooking); release throws along the view +10° plus the player's
  velocity. Held past the fuse it goes off in hand; disabled/killed mid-cook drops it live. Player default: 2 frags,
  2 stun grenades; the range's Equipment Locker swaps stun ↔ smoke; ammo crates restock grenades.
- **Smoke** (`GrenadeType.Smoke`): the canister burns into an HDRP `LocalVolumetricFog` cloud + particle billows that
  builds over 3 s, holds 35 s, fades 6 s, and registers a `VisionObstruction` sphere (AI cannot see through it).
  Frags also shove loose rigid bodies (concussive push). Stun grenades blind by on-screen position for the player
  (`FlashbangEvent.ScreenExposure`) and by facing for AI.
- **Looting:** a fallen soldier's rifle (`DroppedWeapon`, Interact) gives up its loaded magazines to matching weapons
  (`RealisticWeaponController.TakeMagazines`, up to loadout + 2).
- **Match** (`MatchDirector` + one `GameMode` on the same object, `MatchHUD` overlay): the player plus soldier slots per
  team (`teamSoldierPrefabs`: `AI_Soldier_Friendly` team 0, `AI_Soldier` team 1) in squads; phases PreRound → Live →
  RoundOver → MatchOver (`MatchPhaseEvent`); clock (modes may stop it), round wins, scores; spawns from
  `GameMode.ChooseSpawn` → `SpawnSelector` (enemy line of sight to the spawn −60, nearest enemy distance, teammates,
  recent use); respawns after `respawnDelay` (negative = one life); dead player loses control; soldiers get a fresh
  body per spawn; respawn-mode corpses clear after 20 s.
- **Modes:** `ExtractionMode` (Tactical Raid: one life, objectives are `HoldInteraction` terminals — each completed one
  alerts the garrison — then hold out in the extraction `ObjectiveZone`), `SearchAndDestroyMode` (rounds, plant at a
  site's plant point, `PlantedBomb` with beeps and a defuse hold, elimination/time/detonation/defuse, sides swap,
  first to N rounds), `HardpointMode` (rotating zones, uncontested holder scores per second, respawns away from the
  live zone).
- **AI objectives:** `SquadCoordinator.SetObjective(Hold | Interact, point, radius, facing)` — Hold spreads members
  round the point, Interact sends one worker (replaced if killed); brains pursue it in `AIState.Objective` when not
  fighting, and do not chase threats beyond a 25 m leash. Modes advance plants/defuses while
  `TacticalAIBrain.IsWorkingObjective`. Friendly-fire checks cover everyone on the team, the player included.

## Armory, profiles & settings (M10)

- **Catalog** (`ArmoryCatalogSO`, `Data/Armory/ArmoryCatalog`): weapons (model + data), attachments, grenades; ids are
  asset names — renaming an asset orphans it in saved profiles (missing parts are skipped with a warning).
- **Profiles** (`SaveProfileManager`, on the player): `PlayerProfile` JSON (`JsonUtility`) at
  `persistentDataPath/profile.json` — blueprints (weapon + attachment ids), loadout (blueprint per slot, grenade ids)
  and `GameSettings`. Saves write `.tmp` then swap it in, keeping `.bak`; an unreadable file is copied to `.corrupt`
  and the backup (or defaults) loaded. Applies the saved loadout at start. `DirectoryOverride` redirects it (tests).
- **Workbench** (`GunsmithWorkbenchController`, an `IInteractable`): pauses the player (`PlayerControl`), swaps the
  player camera for the bench camera, lays the carried weapon on the turntable (centred between stock and muzzle) with
  a `WorkbenchNode` marker per mount; drag turns it and clicks pick mounts (`WorkbenchInputSurface` on the UI), the part
  list comes from the catalog (`CompatibleParts`), fitting rebuilds the preview (assembled clone, measured inertia) and
  the stats. "Fit to weapon" → `RealisticWeaponController.SetBuild` + `StoreLoadout`; Leave/Menu hands back control.
- **Settings** (`HDRPSettingsMenu`, on the player, Menu key): FOV (`ProceduralADSAndOpticsController.BaseFieldOfView`),
  PiP scope resolution, motion blur (Player-layer volume), upscaling (camera `allowDynamicResolution` /
  `allowDeepLearningSuperSampling` / `allowFidelityFX2SuperResolution` + `DynamicResolutionHandler` percentage; DLSS
  and FSR2 only where detected, else TAAU), audio dynamic range; applied live, saved with the profile; pauses the
  player while open.
- **Dynamic range** (`DynamicRangeProcessor` on the AudioListener, master bus via `OnAudioFilterRead`): compressor +
  limiter presets War Tapes (makeup +4 dB, soft limiter), Studio Reference (transparent), Night Mode (5:1 from −30 dB,
  +12 dB makeup, −6 dB ceiling).
- UI for match HUD, menu and workbench is built in code through `UIFactory` (uGUI, legacy font, Input System UI module
  created on demand).

## Asset pipeline (M11)

- **Sources:** every model has a Meshy spec in `MeshyAssetManifest` (id, category, prompt, texture prompt, fit). A model
  dropped into `Art/Models/<Category>/<Id>/` (FBX/OBJ + PBR textures) wins; otherwise the builders use high-detail
  procedural geometry. The user generates on meshy.ai with their subscription (prompts: `Art/Models/MeshyPrompts.md`,
  `Tools/FPS Builder/Meshy/Asset Queue`); `MeshyApiClient`/`MeshyAssetGenerator` call the REST API only when
  `MESHY_API_KEY` (or the EditorPrefs key) is set — API calls bill separate Meshy API credits.
- **Import:** `ImportedModelPostprocessor` sets model/texture import settings under `Art/Models`; `ImportedModels`
  builds `<Id>_HDRP.mat` (HDRP/Lit) and packs metallic/roughness/occlusion (or ORM) into `<Id>_MaskMap.png`
  (`MaskMapPacker`: R metallic, G AO, B detail mask, A 1 - roughness). Builder 6 uses the same packing for any model
  folder. `ModelFitting` poses a model in its part frame (long axis → +Z, slim or bulky end forward, mass up, uniform
  scale, bounds or end-slice anchor); `fit.json` (`{"euler": {...}}`) overrides the orientation. Sockets on imported
  attachments come from measured end slices (`PartShape`).
- **Procedural:** `MeshKit` (chamfered boxes, lathes, prisms, side-profile extrusions, rails, tori, ellipsoids; faces
  wound from their intended normal), `SurfaceFinishes` (generated 256² base/normal/mask PNGs per finish under
  `Art/Models/Materials`; delete to regenerate), `ModelAssembly` (static geometry merged per finish, moving parts
  separate), `MeshStore` (one mesh asset per piece under `Art/Models/Procedural/<Model>/`, overwritten in place so
  references survive rebuilds). Models: `ProceduralWeapons`, `ProceduralAttachments`, `ProceduralProps` (equipment,
  props as prefabs in `Prefabs/Props`, `Dress` swaps a placeholder block's cube for a prop), `ProceduralOperators`
  (soldier gear and the player's sleeves/gloves).
- **Mechanism:** `WeaponMechanism` (FPS.Weapons, on the weapon root) animates trigger, selector, bolt carrier/slide
  (cycles per shot, held back when locked open), charging handle (charge stage, or rides the carrier) and bolt
  handle (bolt action); `RealisticWeaponController` and `AIWeaponHandler` drive it each LateUpdate.
- **Soldiers:** hitboxes are invisible primitive colliders (Hitbox layer); operator visuals are separate, collider-free
  children of the same bones. Friendly: coyote/tan, hostile: ranger green/olive.
- **Weapon racks:** `WeaponRack` (FPS.Gameplay, IInteractable) swaps a loadout slot to the next rack weapon and
  saves the loadout; Armory west wall and the test range carry a primary and a secondary rack.

## Photoreal look & combat VFX (M12)

- **World look** (`WorldLook`, editor; shared profile `Settings/Rendering/WorldLook.asset`, added by every scene
  builder): physically based sky + generated cumulus `CloudLayer` (casts shadows; `Art/Sky/CloudMap.png`), sun at
  90 000 lux / 5600 K with contact shadows, histogram auto exposure (EV 4.5–15.5), ACES, film grain 0.18, chromatic
  aberration 0.08, split toning, SSR, SSGI (half res), SSAO, contact and micro shadows, thin volumetric fog.
  `EnablePipelineFeatures` turns the matching HDRP asset support flags on.
- **Interiors:** `WorldLook.Interior(parent, name, centre, size)` per room = box-projected realtime reflection probe +
  a local `IndirectLightingController` volume (trigger box on Ignore Raycast) that cuts the open-sky ambient the roof
  blocks. Used for the shoot house, both arena rooms, the workshop and the indoor range.
- **Environment materials** (`EnvironmentMaterials`, `Art/Environment`; delete to regenerate): `Dress(level, ground)`
  gives blockout primitives on WorldGeometry a weathered HDRP/LayeredLit material by ballistic surface — layer 0 the
  surface, layer 1 grime, layer 2 wet patches on ground only (`ConcreteFloor`, asphalt, dirt), all world-space
  triplanar with height-based blending and main-layer influence. `WeatherMask.png`: R grime, G wet, **A = 1** (HDRP
  reads the main layer's weight from alpha). Objects named `*Floor*`/`*Ground*` count as ground.
- **Edge wear:** `MeshKit` writes UV1.x = 0.75 on chamfers/edge bands (0.25 on faces); worn finishes
  (`SurfaceFinishes.WearsAtEdges`) sample `EdgeWear_Detail.png` with UV1 (left half neutral, right half bare metal).
- **Noise helpers:** `SurfaceFinishes.Smoothstep` is the shader smoothstep; `Mathf.SmoothStep(a, b, t)` interpolates
  between a and b and must not be used as a threshold.
- **Impacts:** steel (`SurfaceMaterialSO.sparks`) throws collision-enabled stretched sparks along the reflected path
  (double on ricochet) and leaves an emissive thermal ring decal (`FadingDecal`, (1 − t)² over 3 s, not on ricochet);
  masonry and soil (`lingeringDust`) leave a pooled `VolumetricPuff` (LocalVolumetricFog) that spreads and thins.
- **Muzzle:** per shot a shock cone (lathe mesh, ~45 ms, scaled by √flash), HDR embers, and at most every 0.12 s a
  volumetric smoke wisp; suppressed builds (flash 0) skip shock and embers.
- **Night vision** (`NightVisionGoggles`, player): N / hold View flips a Player-layer global volume in over 0.35 s —
  monochrome white (P45) or green phosphor, auto-brightness exposure (down to EV −4, `gain` EV above normal), bloom
  halos, scintillation grain, round tube vignette. Sets `FPS.Core.NightVision.Active`: lasers swap to their infrared
  material (`TacticalDevice` IR beam), and with a laser fitted aiming becomes canted active aim (45° roll, laser forced
  on, no optic or zoom).
- **Scope lens:** the PiP output goes through `Hidden/FPS/ScopeLens` (`ProceduralADSAndOpticsController.lensShader`):
  barrel distortion, rim chromatic aberration, eye-relief shadow disc, field stop, etched duplex and an HDR lit centre
  (`WeaponSight.ConfigureReticle`: `DuplexDot` = 1, `Chevron` = 2). Without the shader the vignette shadow is used.

## Milestones

- [x] **M1** Project init, `CLAUDE.md`, assemblies, `InputReader`, `EventBus`, `ObjectPoolManager`, `ProjectSetupWizard`
- [x] **M2** Locomotion biomechanics, stances, leaning, weapon mounting, breathing
- [x] **M3** Spring solver, weapon inertia, barrel collision, procedural ADS, PiP optics, recoil
- [x] **M4** Weapon/caliber data, model rig & sockets, chamber tracking, staged reloads
- [x] **M5** Ballistics sim, penetration/ricochet, shell casings, health & armor
- [x] **M6** Acoustic propagation, muzzle VFX, diegetic HUD
- [x] **M7** Gunsmith & attachment physics
- [x] **M8** Tactical AI & squad CQB
- [x] **M9** Game modes, match director, tactical equipment
- [x] **M10** 3D gunsmith workbench, loadout persistence, HDRP settings menu
- [x] **M11** Meshy asset pipeline, high-detail procedural models, weapon mechanisms, battle rifle and sidearm
- [x] **M12** Photoreal HDRP overhaul (SSGI/SSR, contact shadows, physical sky, weathered materials, combat VFX, NVG)
- [ ] **M13** Physical gunshot synthesis, environment tails, Doppler cracks, gear foley
- [ ] **M14** Mantling and vaulting, slide cancel, compressed ready, physical doors, inspect, camera kick
- [ ] **M15** Deployment station, scene flow, loading screen, complete settings and key rebinding
- [ ] **M16** Hit confirmation and diegetic combat polish
