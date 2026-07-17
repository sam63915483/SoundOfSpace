# Audit: Pickups / Tutorial / Map / Audio / Vid

Read-only audit of:
- `Assets/3 - Scripts/Pickups/`
- `Assets/3 - Scripts/Tutorial/`
- `Assets/3 - Scripts/Map/`
- `Assets/3 - Scripts/Audio/`
- `Assets/3 - Scripts/Vid/`
- `Assets/3 - Scripts/PerfBootstrap.cs`

No files were modified.

## Summary

These systems are, on the whole, in good shape and clearly reflect a lot of hard-won
bug fixes (the floating-origin catch-ups in `MapOrbitLines`, the drop-teleport fix in
`PlayerPickup`, the seeded-singleton handoff in `TutorialUI`). The auto-singleton
seeding trap (#1) is **handled correctly** — every MainMenu-skipping singleton in
scope (`PickupUIManager`, `TutorialUI`, `TutorialPerformanceReview`, `BonusTutorial`,
`MapTutorial`) is mirrored in `MainMenuController.EnsureGameplaySingletons`
(`Assets/3 - Scripts/UI/MainMenuController.cs:563,571,583,640` async path;
`697,715,721,756,934` legacy path).

The findings below are mostly medium/low: one genuine null-deref inconsistency in the
pickup path, several runtime `Material` leaks (worst in the pistol tracer on sustained
fire), a chunk of dead/orphaned editor code (`Vid/CameraController.cs`), and some
one-time allocation / log-spam cleanups.

Positive notes: physics-object registration is handled correctly everywhere
(`PlayerPickup.DropObject` re-registers, `PickupObject`/`ForcePickup` unregister); no
`FindObjectOfType`/`Camera.main`/`GameObject.Find` calls were found in any `Update`/
`LateUpdate`/`FixedUpdate` hot path (all are Start-time or throttled/lazy-null
re-finds); audio sources are consistently created with `playOnAwake=false` and cleaned
up in `Teardown`/`OnDestroy` in the cinematics.

## Bugs (severity, file:line, description, fix)

### MEDIUM — Unchecked `GetComponent<Collider>()` in the pickup path
`Assets/3 - Scripts/Pickups/PlayerPickup.cs:220`
```
obj.GetComponent<Collider>().enabled = false;
```
`PickupObject` disables the collider with **no null check**, while the two sibling
methods that do the same thing DO null-check (`DropObject` line 265
`if (col != null)`, `ForcePickup` line 300–301). The pickup target can be resolved
from a child "PickupHelper" trigger's parent (`UpdateLookAtAndPrompt`, lines 121–128),
and that parent `ShipPart` is not guaranteed to carry its own `Collider` component. If
a part's collider lives only on the helper child, this line throws
`NullReferenceException` on pickup and the part is left half-picked-up (kinematic set,
never parented).
Fix: `var col = obj.GetComponent<Collider>(); if (col != null) col.enabled = false;`
to match `DropObject`/`ForcePickup`.

### MEDIUM — Runtime `Material` leak per pistol shot (tracer)
`Assets/3 - Scripts/Pickups/PistolController.cs:677-687` (`MakeTracerMaterial`), called
from `BuildTracerLine` (604) and the bullet-head build (573) and `SpawnTracer`.
Every shot builds 2–3 fresh `new Material(...)` instances (halo line, optional core
line, optional head sphere) and assigns them to renderers on the `PistolTracer`
GameObject, which is `Destroy`ed after `tracerDuration` (line 663). Unity does **not**
auto-destroy a `Material` assigned via `renderer.material` when the GameObject is
destroyed — those material instances leak until scene unload. Under sustained fire this
accumulates into a steady memory climb.
Fix: cache a single shared `Material` per color the same way `GetTracerShader()` /
`GetSoftGlowTexture()` are cached statically, OR explicitly `Destroy` the materials in
`AnimateTracer`'s teardown alongside `Destroy(root)`.

### LOW/MEDIUM — Runtime `Material` leak per orbit-lines rebuild
`Assets/3 - Scripts/Map/MapOrbitLines.cs:394,513,574` — each `LineRenderer` gets
`new Material(Shader.Find("Sprites/Default"))`. `BuildAndSimulate` (238–241),
`RefreshShipOrbits` and `RefreshPlayerOrbit` destroy the old line **GameObjects** but
never destroy the assigned `Material` instances, so every orbit-lines toggle / ship
refresh leaks one material per line. Also `MapHighlightRing.cs:48` (built once — not a
concern) and `PodThrustFlames`/`PistolController` follow the same pattern.
Fix: null out / `Destroy(line.material)` before destroying the line GameObject, or share
one cached material per line color.

### LOW — `Vid/CameraController.cs` null-deref on unassigned `pivot` + orphaned code
`Assets/3 - Scripts/Vid/CameraController.cs:26`
```
targetZoomDst = (transform.position - pivot.position).magnitude;
```
`Start()` dereferences `pivot` with no null guard — NRE if the field is unassigned.
The class name is referenced **nowhere** in the codebase except its own file (verified
by grep), so it is only reachable if manually attached to a scene object. This is a
verbatim leftover from the upstream Sebastian Lague solar-system template
(alt-drag orbit/zoom editor cam). See "Redundancies / Dead Code."

### LOW — Shipping-build log spam on every tutorial step completion
`Assets/3 - Scripts/Tutorial/TutorialUI.cs:240`
```
Debug.Log($"[TutorialUI] PlayCompleteSound firing: clip=...");
```
Fires an unconditional `Debug.Log` every time a tutorial/bonus/map step completes, in
release builds too. `PerfBootstrap` strips the stack trace so the cost is small, but
it's still per-completion console noise (and two adjacent `LogWarning` diagnostics at
227/232). Recommend gating behind a debug flag or removing.

## Redundancies / Dead Code

- **`Assets/3 - Scripts/Vid/CameraController.cs`** — entire file appears unused
  (no references anywhere; upstream-template orbit camera). Candidate for deletion
  unless it's wired into a trailer/recording scene by hand. The whole `Vid/` folder
  contains only this one file.
- **`Assets/3 - Scripts/Tutorial/_LegacySteps.cs`** and the lower half of
  `TutorialSteps.cs` (`CatchFirstFishStep`, `CatchFiveFishStep`, `WalkToFireStep`,
  `OpenCookPanelStep`, `MainSwingAxeStep`, `MainGatherWoodStep`, `OpenBuildMenuStep`,
  `MainBuildCabinStep`, plus all of `_LegacySteps.cs`: `PostCrashExamStep`, `StandUpStep`,
  `MouseLookStep`, `HatchStep`, ship-flight steps, etc.) are **intentionally-retained
  dead code** — not in `BuildDefault()` but kept so `TutorialManager.ApplyState`
  resolves old saves by type name (documented at `TutorialSteps.cs:16-20` and
  `_LegacySteps.cs:4-8`). Correct as-is; do **not** delete. Flagged only so it's not
  mistaken for live code.
- **`MapTutorial` step-completion / advance logic** duplicates the shape of
  `TutorialManager.Update` and `BonusTutorial.Update` (three near-identical
  "tick → check IsComplete → PlayCompleteSound → next" loops across three files). Not a
  bug; a future refactor could share a base driver, but the save/state shapes differ
  enough that duplication is defensible.
- **Ship-flight steps exist twice**: `ShipPilotBonusStep` … `ShipRollBonusStep`
  (`BonusTutorial.cs:888-964`) duplicate `PilotShipStep` … `ShipRollStep`
  (`_LegacySteps.cs:373-446`). Intentional (different base classes: `BonusStep` vs
  `TutorialStep`) and noted in-code (`BonusTutorial.cs:876-878`).
- **`MapHighlightRing.cs`** — the 2-circle ring is described as legacy/no-longer-created
  for body markings (`SolarSystemMapController.cs:56-60`); still used for ship markings.
  Kept as a nullable field; fine.
- **`WaterBottleController.fillUI` / `ShowFillUI`** — deliberately reduced to a no-op
  (superseded by `WaterFillHUD`); comment at lines 264-272 explains. Dead but
  intentionally retained for call-site compatibility.

## Performance / Optimization

- **`MapOrbitLines.BuildAndSimulate` main-thread hitch**
  (`MapOrbitLines.cs:289-325`): on every orbit-lines toggle-ON it allocates
  `paths = new Vector3[n][]`, each `new Vector3[maxSimSteps]` (30000), and runs a full
  30 000-step O(n²) N-body integration synchronously. For ~10 bodies that's ~3.6 MB of
  transient allocation plus a potentially multi-ms spike at the moment the player
  presses the toggle. Consider caching the simulated moon paths (they don't change at
  runtime), lowering `maxSimSteps`, or spreading the sim across a few frames /
  coroutine.
- **`PickupUIManager.LateUpdate` per-frame canvas churn**
  (`PickupUIManager.cs:311-341`): `SortByDistanceSibling` runs an O(N²) selection sort
  **and** calls `SetSiblingIndex` on every marker every frame whenever any pickups are
  registered — each `SetSiblingIndex` dirties the canvas and forces a UGUI rebuild even
  when the order didn't change. N is small (a couple dozen) so it's tolerable, but
  gating the re-sort/`SetSiblingIndex` on an actual order change would remove a constant
  per-frame canvas rebuild while ship-crash pickups are on screen.
- **`TutorialUI` keycap atlas build**
  (`TutorialUI.cs:951-986`, `EnsureKeycapAsset`): `DrawKeycapCell` calls
  `Font.CreateDynamicFontFromOSFont(...)` once per keycap (9×) inside the loop and never
  destroys the created fonts, and allocates a temp `Texture2D` per cell. One-time cost
  (lazy, cached via `_keycapAsset`), but the font should be created once outside the
  loop and the temp textures/fonts are minor leaks. Low priority.
- **`AtmosphericWind.Factor`** (`AtmosphericWind.cs:18-42`) linearly scans all bodies
  each call; it's invoked every frame by `PlayerSuitAudio.Update` and once per ship by
  `ShipWindAudio.Update`. Fine at current body/ship counts; would want a cached
  nearest-body if ship counts grow large.
- **`TutorialPerformanceReview` / `BonusTutorial` / `MapVelocityHud`** call
  `Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")` per text
  element built (e.g. `TutorialPerformanceReview.cs:491`, `MapVelocityHud.cs:111`,
  `BonusTutorial.cs:584`). This is the one allowed `Resources.Load` (the TMP font) and
  only runs at UI-build time, but caching the returned asset in a static would avoid
  repeated loads when many rows/labels are built.

## Notes & Uncertainties

- **Auto-singleton trap #1 — verified clean.** All in-scope MainMenu-skipping
  singletons are seeded in `EnsureGameplaySingletons`. `UiSfxPlayer`
  (`Audio/UiSfxPlayer.cs`) deliberately does **not** skip MainMenu (it services menu
  buttons too) and self-creates via `Ensure()` on first use — correct and trap-immune,
  as its own header comment explains.
- **`AxeController.DetectSwingHit` bestCrystal reset** (`AxeController.cs:207-272`): the
  enemy/alien cone-scan branches (222, 239) reset `bestTree` but not `bestCrystal`. This
  is **not** currently a live bug only because the crystal scan runs last and
  `bestCrystal` is null until then; but the asymmetry is fragile — if the loop order is
  ever reordered it would mis-dispatch. Worth a defensive `bestCrystal = null;` in those
  branches. Left as a note, not a bug, given current ordering.
- **`PlayerSuitAudio.Awake`** (`PlayerSuitAudio.cs:72-74`) sets `Instance = this`
  without the standard `if (Instance != null && Instance != this)` guard. It's a
  singular scene component on the Player prefab, so a duplicate is unlikely, but it
  doesn't follow the project's singleton convention. Low risk.
- **`ShipWindAudio`/`PlayerSuitAudio` wind loops** call `_windSrc.Play()` every frame if
  `!isPlaying` (e.g. `PlayerSuitAudio.cs:186`, `ShipWindAudio.cs:55`). The source is set
  `loop=true` and volume-driven to 0 rather than stopped, so `isPlaying` stays true and
  the guard rarely fires — fine.
- I did not run the project (no CLI build/test per repo conventions); all findings are
  from static reading. Runtime confirmation of the `PlayerPickup.cs:220` NRE depends on
  whether any `ShipPart` prefab actually lacks a root `Collider` — I could not verify
  prefab contents, only the code inconsistency.
- The two cinematics (`PodArrivalSequence`, `IntroSequenceController`) are large but
  well-guarded: idempotent `Teardown`, statics reset in `OnDestroy`, audio sources torn
  down. `PodArrivalSequence` correctly uses `DestroyImmediate` on its `GrogginessImageEffect`
  (line 440) with a documented reason ([DisallowMultipleComponent] hand-off). No issues found.
