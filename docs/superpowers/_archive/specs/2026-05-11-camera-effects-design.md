# Camera Effects — Design

**Date:** 2026-05-11
**Scope:** Add a comprehensive set of camera / screen / lens effects that give the game cinematic game-feel. Every effect must be individually toggleable from the new Graphics tab in the pause menu (so we can disable anything that doesn't feel right without recompiling).

## Goals

- Movement-driven effects (headbob, FOV kicks, tilt, landing dip) so the game feels alive in motion.
- Combat / damage feedback (red flash, vignette, directional shake, micro-shake on hit) so impacts read.
- Cinematic moments (letterbox bars in dialogue, slowmo on kill, dialogue focus vignette).
- Always-on lens character (subtle vignette, grain, chromatic aberration, lens dirt) so the camera feels like a real camera, not a window.
- Light interaction (lens flares on bright sources, anamorphic horizontal streaks).
- Mood color grading (slight tint shifts by context: combat / peaceful / low-health).

## Non-goals

- **Anything that touches the atmosphere/planet/ocean shaders** — locked per CLAUDE.md. We never modify CustomPostProcessing, PlanetEffects, AtmosphereEffect, OceanEffect, or any shader/compute under those folders.
- Depth of field, lens distortion, auto-exposure / eye adaptation, real motion blur — these would need new shaders that risk colliding with the atmosphere pipeline's render queue. Punted to a later spec once the safer set has been validated in-game.
- Per-effect intensity sliders for *every* effect in v1 — only the ones where intensity meaningfully varies between users (headbob amount, vignette strength, FOV kick magnitude). Everything else gets a simple on/off toggle.

## Architecture overview

A single `CameraEffectsManager` singleton (auto-created, seeded in `MainMenuController.EnsureGameplaySingletons` like the other HUDs) owns the whole subsystem and coordinates the modules below. It polls `InputSettings` every frame for the toggle flags so changes from the pause menu take effect live.

```
CameraEffectsManager
├── CameraTransformFX     — headbob, strafe tilt, landing dip, death tilt
├── CameraFOVFX           — stack-based FOV offset (sprint / jetpack / ship boost contributors)
├── CameraShake (existing) — landing impacts, hit shake, enemy-kill micro-shake
├── VignetteOverlay       — composited UI vignette driven by multiple sources
├── DamageFlashOverlay    — red full-screen flash on damage
├── LetterboxBars         — top/bottom black bars
├── SpeedLinesOverlay     — radial streaks at high ship velocity
├── FilmGrainOverlay      — animated UI noise overlay
├── ChromaticAberrationOverlay — UI R/G/B offset trick (no shader)
├── LensDirtOverlay       — UI smudge texture, brightens against bright sources
├── AnamorphicStreaks     — UI horizontal streak sprites placed on registered bright sources
├── MoodColorGrade        — full-screen UI tint, driven by combat / low-health state
├── LensFlareRegistry     — attaches Unity built-in LensFlare components to registered light sources
└── SlowmoOnKill          — brief Time.timeScale dip on enemy death
```

Each module is its own MonoBehaviour child GameObject under `CameraEffectsManager`. Modules read their enable flag from `InputSettings` once per frame and self-suspend when disabled. This keeps each module isolated, testable in the inspector, and easy to delete if a single effect doesn't work out.

### Vignette compositing

Multiple drivers want to show a vignette — damage pulse, low-health pulse, dialogue focus, always-on baseline. Instead of stacking separate UI images, `VignetteOverlay` is a single full-screen radial-gradient `Image` whose color + alpha is recomputed each frame as the max-blend of all active drivers. Drivers register their `intensity` (0–1) and `color`; the overlay picks the strongest contributor or blends them. This avoids overdraw and weird color stacking.

### FOV compositing

Same idea for FOV: `CameraFOVFX` keeps a list of (id, deltaFOV) entries. Each source (sprint, jetpack, ship boost) pushes its delta and removes it when no longer applicable. The final FOV = base + sum of deltas, smoothed over `~0.15s` with `Mathf.SmoothDamp`. The base FOV is what the camera already has at `Awake` time.

### Hooks (where effects are triggered)

| Effect | Triggered from |
|---|---|
| Headbob, strafe tilt | `PlayerController` (read horizontal velocity + WASD input) |
| Landing dip | `PlayerController` `justLanded` flag (already exists) |
| Sprint FOV kick | `PlayerController` velocity threshold |
| Jetpack FOV pulse | `PlayerController.usingJetpack` / `usingDownThrust` / directional thrust flags |
| Ship boost FOV + shake | `Ship` engines-firing state |
| Damage flash + directional shake + damage vignette pulse | `ResourceManager` `OnHealthDropped` event (new — currently health changes silently) |
| Low health pulse vignette | `ResourceManager` threshold check (continuous) |
| Hit-enemy micro-shake | `AxeController.ApplyHit` + `PistolController.TriggerShot` on confirmed hit |
| Death tilt + dim | `ResourceManager` death event |
| Slowmo on kill | `EnemyController.Die` event (new) |
| Letterbox bars | `PlayerController.isInDialogue` + cutscene controllers |
| Dialogue focus vignette | `PlayerController.isInDialogue` |
| Lens flares | Auto-attached to suns + ship engines + bonfires + laser; toggle hides the components |
| Mood color grade | Combat state (`EnemyController.ActiveEnemies.Count > 0` + close to player) / low-health |

For the events that don't yet exist (`OnHealthDropped`, enemy `Die` event), the spec adds one-line `Action<>` events on the relevant classes.

## Settings — `InputSettings` extensions

Add to `InputSettings.cs` (with `PlayerPrefs` load/save matching the existing pattern):

```csharp
// ── Camera Effects (master + per-effect toggles) ─────────────
[Header("Camera Effects")]
public bool cameraEffectsEnabled = true;   // master kill-switch

public bool fxHeadbob = true;
public bool fxLandingDip = true;
public bool fxStrafeTilt = true;
public bool fxSprintFovKick = true;
public bool fxJetpackFovKick = true;
public bool fxShipBoostFov = true;
public bool fxSpeedLines = true;
public bool fxDamageFlash = true;
public bool fxDamageVignette = true;
public bool fxDirectionalHitShake = true;
public bool fxEnemyHitMicroShake = true;
public bool fxDeathTilt = true;
public bool fxLowHealthVignette = true;
public bool fxDialogueVignette = true;
public bool fxSlowmoOnKill = true;
public bool fxSubtleVignette = true;       // always-on
public bool fxFilmGrain = true;
public bool fxChromaticAberration = true;
public bool fxLensDirt = true;
public bool fxLensFlares = true;
public bool fxAnamorphicStreaks = true;
public bool fxLetterboxBars = true;
public bool fxMoodColorGrade = true;

// Intensities (for the effects where it makes sense to tune)
[Range(0f, 1f)] public float fxHeadbobIntensity = 1f;
[Range(0f, 1f)] public float fxFilmGrainIntensity = 0.6f;
[Range(0f, 1f)] public float fxSubtleVignetteIntensity = 0.45f;
[Range(0f, 1f)] public float fxChromaticAberrationIntensity = 0.35f;
```

The master `cameraEffectsEnabled` toggle short-circuits the whole `CameraEffectsManager.Update` so a single switch can disable everything.

## Settings UI — extending `TabbedPauseMenu`

`TabbedPauseMenu`'s settings list currently only supports slider rows. Extend it to support three row types:

1. `SliderDef` — existing
2. `ToggleDef` — checkbox row (label on left, toggle on right, hover/focus matching the sliders)
3. `HeaderDef` — section label, no control (small caps, dim color, used as a divider inside a tab)

Then in `BuildSettingsList`, add a new `CAMERA` tab (between CONTROLS and GRAPHICS) with all the camera-effect toggles grouped under section headers:

```
CAMERA
├── HeaderDef "// MOVEMENT"
├── ToggleDef "HEADBOB"             → fxHeadbob
├── SliderDef "HEADBOB INTENSITY"   → fxHeadbobIntensity
├── ToggleDef "LANDING DIP"         → fxLandingDip
├── ToggleDef "STRAFE TILT"         → fxStrafeTilt
├── ToggleDef "SPRINT FOV KICK"     → fxSprintFovKick
├── HeaderDef "// VEHICLE"
├── ToggleDef "JETPACK FOV KICK"    → fxJetpackFovKick
├── ToggleDef "SHIP BOOST FOV"      → fxShipBoostFov
├── ToggleDef "SPEED LINES"         → fxSpeedLines
├── HeaderDef "// COMBAT"
├── ToggleDef "DAMAGE FLASH"        → fxDamageFlash
├── ToggleDef "DAMAGE VIGNETTE"     → fxDamageVignette
├── ToggleDef "HIT SHAKE"           → fxDirectionalHitShake
├── ToggleDef "ENEMY HIT MICRO-SHAKE" → fxEnemyHitMicroShake
├── ToggleDef "DEATH TILT"          → fxDeathTilt
├── ToggleDef "SLOWMO ON KILL"      → fxSlowmoOnKill
├── HeaderDef "// SURVIVAL & CINEMATIC"
├── ToggleDef "LOW HEALTH VIGNETTE" → fxLowHealthVignette
├── ToggleDef "DIALOGUE VIGNETTE"   → fxDialogueVignette
├── ToggleDef "LETTERBOX BARS"      → fxLetterboxBars
├── ToggleDef "MOOD COLOR GRADE"    → fxMoodColorGrade
├── HeaderDef "// LENS CHARACTER"
├── ToggleDef "SUBTLE VIGNETTE"     → fxSubtleVignette
├── SliderDef "VIGNETTE INTENSITY"  → fxSubtleVignetteIntensity
├── ToggleDef "FILM GRAIN"          → fxFilmGrain
├── SliderDef "GRAIN INTENSITY"     → fxFilmGrainIntensity
├── ToggleDef "CHROMATIC ABERRATION"→ fxChromaticAberration
├── SliderDef "CA INTENSITY"        → fxChromaticAberrationIntensity
├── ToggleDef "LENS DIRT"           → fxLensDirt
├── ToggleDef "LENS FLARES"         → fxLensFlares
└── ToggleDef "ANAMORPHIC STREAKS"  → fxAnamorphicStreaks
```

The Graphics tab keeps its existing world-spawn / view-distance settings unchanged. The CAMERA tab is new; same Style A horizontal underlined tabs, ScrollView already handles the long content.

Tab list becomes `CONTROLS | CAMERA | GRAPHICS` — three tabs.

### Toggle widget visual

Beveled rectangle ~36×20 with the label "ON" / "OFF" inside, cyan when on, dim when off. Matches the existing UI family (same bevel sprite from `UIPanelSprites`).

## Effect implementation notes

| Effect | One-line implementation note |
|---|---|
| Headbob | `LateUpdate`: vertical sine offset on cam local pos, frequency × velocity, amplitude × intensity. Reset to 0 when idle. |
| Landing dip | Coroutine triggered by `justLanded`: lerp cam local Y down ~0.1 then back. |
| Strafe tilt | `LateUpdate`: target Z-roll = -input.x × 2°, smooth-damp current toward target. |
| Sprint FOV kick | If horizontal velocity > sprintThreshold and sustained ~0.3s, add +6° to FOV stack. |
| Jetpack FOV pulse | While `usingJetpack` is true, add +5° to FOV stack. |
| Ship boost FOV + shake | While `Ship.enginesFiring`, add +8° to FOV stack and apply 0.05-magnitude continuous shake. |
| Speed lines | Full-screen UI image with radial streak texture, alpha = `(speed - threshold) / fadeRange`, clamped. |
| Damage flash | `Image` fade: alpha 0.55 → 0 over 0.25s on each `OnHealthDropped`. |
| Damage vignette pulse | `VignetteOverlay` driver: 0.7 intensity, decays over 0.4s. |
| Directional hit shake | `CameraShake.TriggerShake(0.15s, mag scaled to dmg, roughness 6)`. |
| Hit-enemy micro-shake | `CameraShake.TriggerShake(0.05s, 0.1, 4)`. |
| Death tilt + dim | Coroutine: lerp cam local Z-rot to 70°, fade `VignetteOverlay` dim driver to 0.85 over 0.6s. |
| Low health vignette | `VignetteOverlay` driver: red, intensity = `1 - health/0.25` clamped, pulsing sin. |
| Dialogue vignette | `VignetteOverlay` driver: black, intensity 0.4 while `PlayerController.isInDialogue`. |
| Slowmo on kill | `Time.timeScale = 0.3` for 0.15s real-time, then snap back to 1. |
| Subtle vignette | `VignetteOverlay` driver: dark, intensity from inspector (default 0.45), always on. |
| Film grain | Animated noise texture; offset UVs each frame (or just rotate a static noise sprite). Image multiply mode. |
| Chromatic aberration | Three full-screen overlay `Image`s tinted R/G/B, offset by intensity px. Composited via Additive material. Subtle (~1 px). |
| Lens dirt | Full-screen `Image` with smudge texture, base alpha 0; alpha pulses up when scene has any pixel above brightness threshold near screen-space (approximate by sampling dot(camForward, sun dir)). |
| Lens flares | Attach `LensFlare` components to the sun light, ship engines, fire pickups; toggle component `enabled`. Assets from the existing "Lens Flares" external pack. |
| Anamorphic streaks | Register bright sources (sun, fire, laser). Each gets a billboarded streak sprite that brightens when on-screen. |
| Letterbox bars | Two full-width `Image`s (top + bottom), animate height 0 → 80 px on dialogue/cutscene start. |
| Mood color grade | Full-screen UI image with multiply/overlay blend, tint color set by current state (combat = warm desat, low-health = greenish, peaceful = subtle cool). |

## Save-state compatibility

`InputSettings.SaveSettings / LoadSettings` will gain a load/save for each new field. Existing saves missing the keys load with `default` (true for bools, default float values) — i.e. effects are on by default for existing users, which is the expected behavior.

## Out of scope (deliberately punted)

- Depth of field, lens distortion, auto-exposure, real per-object motion blur (need shaders / risk atmosphere conflict).
- Per-effect intensity sliders beyond the four called out in `InputSettings`.
- Controller-rumble integration with camera shake events.
- A separate "presets" dropdown ("Minimal / Normal / Cinematic / All") — could be added later as a convenience on top of the per-toggle list.

## Implementation phases (for the plan)

1. **Foundation:** Extend `InputSettings`; extend `TabbedPauseMenu` to support `ToggleDef` + `HeaderDef`; add the new `CAMERA` tab with all the rows; wire toggles to the new fields with no effect modules attached yet. Result: settings UI looks right, toggles flip booleans, nothing visually changes.
2. **Camera transform + FOV:** Headbob, landing dip, strafe tilt, sprint FOV, jetpack FOV, ship boost FOV + shake. All transform/FOV only, no UI. Hook into PlayerController and Ship.
3. **UI overlay manager + combat feedback:** VignetteOverlay (composited), DamageFlashOverlay, directional hit shake wiring, hit-enemy micro-shake, death tilt + dim. Adds `ResourceManager.OnHealthDropped` event.
4. **Survival + cinematic:** Low health vignette driver, dialogue vignette driver, letterbox bars, slowmo on kill (adds `EnemyController.OnDeath` event).
5. **Lens character (always-on):** Subtle vignette driver, film grain overlay, chromatic aberration overlay, lens dirt overlay.
6. **Light interaction + mood:** Lens flares attached to known light sources; anamorphic streak sprites; mood color grade driver.
7. **Validation pass:** Verify atmosphere is unaffected (sky, planet surface, ocean still look correct), perf check on the additive overlays, polish intensities, write CLAUDE.md update.

Phases 2–6 are largely independent of each other once phase 1 lands — good fit for parallel sub-agents.
