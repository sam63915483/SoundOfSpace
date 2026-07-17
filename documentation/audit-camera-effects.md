# Audit: Camera & Effects

Read-only audit of `Assets/3 - Scripts/Camera/` (23 files) and `Assets/3 - Scripts/Effects/` (2 files).
Forbidden-zone files (`Atmosphere.cs`, `CustomImageEffect.cs`, `Post Processing/Planet Effects/`, `Celestial/`, shaders) were not opened for modification; `AtmosphereReloadFix.cs` (which references the forbidden `AtmosphereSettings` by reflection only) is in scope and looks correct.

## Summary

The subsystem is in good shape overall. The FX modules are coordinated by a single persistent `CameraEffectsManager` singleton (correctly seeded in `MainMenuController.EnsureGameplaySingletons` — trap #1 satisfied, verified at `MainMenuController.cs:603/827`), and the two runtime post-effects (`ChromaticAberrationEffect`, `RadialMotionBlurEffect`) correctly create and `DestroyImmediate` their materials in `OnDestroy`. The recent optimization work (gating `enabled`/`Canvas.enabled` on FX flags, throttling lens-flare occlusion rays, caching `Ship.PilotedInstance` instead of `FindObjectsOfType`) is solid.

Findings are mostly low-severity: a handful of per-frame `FindObjectOfType`/`Camera.main`/`GetComponent` calls that only run in fallback paths (usually until a ref resolves), a few runtime-created `Texture2D`/`Sprite` objects never released on destroy (near-zero impact because the owners are `DontDestroyOnLoad` singletons that are effectively never destroyed), one dead diagnostic file, and some stale/misleading comments. One genuine correctness sharp-edge exists in `SlowmoOnKill` around `Time.timeScale`. No forbidden-zone changes are needed.

## Bugs (severity, file:line, description, fix)

### LOW — `CameraEffectsManager.cs:238-249` — per-frame `FindObjectOfType`/`Camera.main` in the un-resolved fallback path
`Update` (line 144) calls `TryAcquireRefs()` whenever `PlayerCamera == null || Input == null`. `TryAcquireRefs` runs `Camera.main` (internal `FindGameObjectWithTag`) and `FindObjectOfType<SettingsMenu>(true)` (full scene scan). In the normal case both resolve within a frame or two and it stops. But if either never resolves (camera missing its `MainCamera` tag, or no `SettingsMenu` in a given scene/interior), it does a full-scene `FindObjectOfType` **every frame for the lifetime of that scene** — the exact pattern CLAUDE.md forbids in `Update`. Practically low-risk since both usually exist, but it is an unbounded per-frame scan on the sad path.
Fix: throttle the retry (e.g. a `_nextAcquireTime` gate at ~1 s like `LightLookAt.cs:24`) instead of retrying every frame.

### LOW — `SlowmoOnKill.cs:60-67` — `Time.timeScale` can be left un-restored / can fight the pause menu
`Routine()` sets `Time.timeScale = 0.15` and restores `1f` only when the loop finishes. Two edge cases: (1) if the routine is ever interrupted (component/GameObject disabled or destroyed mid-slowmo) `Time.timeScale` stays at 0.15 — in practice the owner (`CameraEffectsManager`) is `DontDestroyOnLoad` so this rarely triggers, but nothing resets it on `OnDisable`. (2) If the game is paused (pause menu sets `Time.timeScale = 0`) during an active slow-mo, the coroutine — which loops on `unscaledTime` — will fire `Time.timeScale = 1f` when it ends and silently un-pause the game.
Fix: reset `Time.timeScale = 1f` in `OnDisable` if `_routineRunning`; and/or have the pause system own timescale and slow-mo apply a multiplier rather than an absolute write.

### LOW — `CombatFX.cs:42-50` — damage/death vignette pushes ignore `MasterEnabled`
`Update` pushes the damage-pulse vignette and death-dim vignette gated only on `input.fxDamageVignette` / `input.fxDeathTilt`, not on `mgr.MasterEnabled`. `HandleHealthDropped`/`HandleDeath` do check `MasterEnabled` before arming the state, so turning the master off *before* a hit is fine — but if the master is toggled off while `_damagePulse` is still decaying (or `_dead` is set), the vignette keeps rendering. Inconsistent with every other module, which all gate on `MasterEnabled`.
Fix: add `!mgr.MasterEnabled` to the early-out at line 43, or gate the two `Push` calls on `mgr.MasterEnabled`.

### LOW — `LensFlareRegistry.cs:174,539` — occlusion raycast buffer can silently under-report
`_hitBuf` is fixed at 16 and `RaycastNonAlloc` truncates to that. If a silhouette ray passes through >16 colliders before the sun, real occluders past index 16 are dropped and the sample is counted "clear," letting flare bleed through dense geometry. Throttled to every 3rd frame so cost is fine; this is purely a correctness edge for very cluttered rays.
Fix: none likely needed at solar-system scale; if it ever manifests, sort by distance or grow the buffer.

### VERY LOW — texture/sprite leaks on destroy (multiple files)
Runtime-baked `Texture2D`/`Sprite` objects are never released:
- `FilmGrainOverlay.cs:49-63` — `_noiseTex` (`Texture2D`) has no `OnDestroy` cleanup.
- `LensFlareRegistry.cs:303-486` — halo/corona/ray/orb/hex sprites + their textures are not destroyed in `OnDestroy` (only `_canvas` and `_additiveMat` are, lines 217-221).
- `VignetteOverlay.cs:69`, `SpeedLinesOverlay.cs:308` — sprites are `static`/cached and shared, so intentionally persistent (fine).

Impact is essentially nil in practice: every owner is a child of the `DontDestroyOnLoad` `CameraEffectsManager` singleton, which is created once and never destroyed, so these are one-time allocations, not per-reload leaks. Only worth fixing if these components ever become scene-scoped.

## Redundancies / Dead Code

- **`_TrailerSunAimer.cs` (whole file)** — self-described "TEMPORARY trailer-diagnostic helper … Safe to delete." Uses `FindObjectOfType<PlayerController>()` in `LateUpdate` (line 15, guarded by null so it stops once found). Dead/diagnostic; a candidate for removal.
- **`CameraFOVFX.cs:73-74` and `:110`** — stale/misleading comments referencing a removed `Ship.FindPilotedShip()` ("_ship is REFRESHED in Update via Ship.FindPilotedShip"). There is no `Update` in this class and `_ship` is set from `Ship.PilotedInstance` in `LateUpdate` (line 74). Comment lies about the code.
- **`GateCameraFxEnabled` (`CameraEffectsManager.cs:213-227`)** — for the six overlay modules it disables **both** the `Canvas` and the `MonoBehaviour`. This is deliberate (comment explains: Canvas stops render, behaviour stops the LateUpdate fade math) and correct, not a true redundancy — noted only so a future reader doesn't "simplify" one away.
- **Unnecessary `GraphicRaycaster` on non-interactive overlays** — `VignetteOverlay`, `DamageFlashOverlay`, `FilmGrainOverlay`, `LetterboxBars`, `MoodColorGrade`, `SpeedLinesOverlay` each `AddComponent<GraphicRaycaster>()` on a canvas whose only content has `raycastTarget = false` and `blocksRaycasts = false`. The raycaster does nothing useful and adds each canvas to the pointer-raycast set. Minor; safe to drop. (`LensFlareRegistry` correctly omits it — see `:275`.)

## Performance / Optimization

- **`SpeedLinesOverlay.cs:193-226`** — when active, all 64 streak `Image`s get position + `sizeDelta` + `color` rewritten every frame, dirtying the canvas and forcing a full UGUI rebatch each frame. This is inherent to the effect and it is correctly gated off (Canvas + behaviour disabled) when the flag is off, but while flying it is the single most expensive item in this subsystem. If it ever shows on a profiler, a `CanvasRenderer`/mesh-based approach (one mesh, 64 quads) would remove the per-Image rebuild.
- **`RadialMotionBlurEffect.cs:124`** (`_ship.GetComponent<Rigidbody>()`) and **`CameraFOVFX.cs:99`** (`IsShipBoosting` → `GetComponent<Rigidbody>()`) — `GetComponent` every frame while active. `SpeedLinesOverlay` already avoids this by reading `_ship.RelativeVelocity`; `RadialMotionBlurEffect.ComputeTargetCenter` could do the same, and `CameraFOVFX` could cache the ship rigidbody alongside `_ship`. Tiny cost, easy win.
- **`RadialMotionBlurEffect.cs:101` / `SpeedLinesOverlay.cs:252`** — `FindObjectOfType<PlayerController>(true)` guarded to run only while `_player == null`; if the player controller is genuinely absent (e.g. inside an interior) this becomes a per-frame full-scene scan. Consider a throttle, consistent with `LightLookAt`.
- **`GrogginessImageEffect.cs:26` and `BlackHoleVignetteEffect`** — Groggy uses `material.SetFloat("_Intensity", …)` with a string each frame; `BlackHoleVignetteEffect` (lines 51-58, 67-74) already caches `Shader.PropertyToID`. Groggy could cache the ID too. Negligible.
- **`LensFlareRegistry.cs:533-562` `IsSampleBlocked`** — per hit it calls `CompareTag`, `GetComponentInParent<PlayerController>`, `<Ship>`, `<CelestialBody>`. With 13 rays × up to 16 hits this is a fair amount of `GetComponentInParent` walking, but it is throttled to every 3rd frame (`_occlFrameCounter`, line 658) and only while the sun is on-screen. Acceptable as-is.
- **`CameraEffectsManager.Update` + `GateCameraFxEnabled`** run a fixed block of cheap, change-gated `enabled` writes every frame — well within budget. The Canvas-ref caching (`GateOverlayCanvas`, lines 230-236) is a good touch.

## Notes & Uncertainties

- **`AtmosphereReloadFix.cs`** — correct and self-contained: reflection `FieldInfo` is cached (line 40/65), the scan (`Resources.FindObjectsOfTypeAll<AtmosphereSettings>`) runs only on `sceneLoaded`, and it does not modify forbidden code. Uses `BeforeSceneLoad` + no MainMenu early-return, so it does not need an `EnsureGameplaySingletons` seed (trap #1 N/A). No issues.
- **Trap #1 compliance** — `CameraEffectsManager`, `TrailerFreeCam`, and `TrailerBlackHoleGrow` all have the MainMenu early-return in their `AutoCreate` *and* are seeded in `MainMenuController.EnsureGameplaySingletons` (`MainMenuController.cs:603/607/609`, plus a second `CameraEffectsManager` seed at 827). Verified good.
- **`TrailerFreeCam` / `TrailerBlackHoleGrow`** — these are dev/trailer capture tools (P and J keys). `TrailerBlackHoleGrow.EnsureVisual` does a one-time `FindObjectsOfType<Renderer>(true)` scan on toggle (not per-frame). `TrailerFreeCam.Enter` does several `FindObjectOfType` calls but only on toggle-on. Both fine.
- **`SlowmoOnKill` timescale ownership** — flagged above as LOW; I could not confirm how the pause menu writes `Time.timeScale` without reading pause code, so the "un-pause on slow-mo end" interaction is a plausible-but-unverified edge, not a confirmed repro.
- **`CameraTransformFX` / manual rotation interpolation** — dense but internally consistent; the FixedUpdate-snapshot + LateUpdate-slerp approach matches its own documented rationale and the execution-order attribute (`DefaultExecutionOrder(100)`). No issues spotted.
- **`DamageFlashOverlay.cs:25` / `FilmGrainOverlay.cs:18` / `LetterboxBars.cs:23`** dereference `_image`/`_top`/`_bottom` without null checks, but all are assigned in `Awake` before any `LateUpdate` runs, so this is safe.
