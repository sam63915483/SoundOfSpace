# Eerie Flashlight — Design

**Date:** 2026-05-12
**Author:** Sam (with Claude)
**Status:** Approved for planning

## Goal

Make the player flashlight look and feel eerie/realistic in the style of horror games like Outlast and Amnesia, replacing the current "single Unity Spot light with scroll-wheel intensity" with a layered visual system that adds:

- A visible volumetric beam slicing through the air ("dry/dusty horror" style)
- A bright bloomy halo at the lamp source
- Visible dust particles drifting through the beam
- Warm-white color (~3500 K)
- Subtle continuous flicker, plus rare panic stutters and occasional dying-battery dips
- Medium-amplitude walking sway

Existing toggle (`E` / controller Y) and scroll-wheel brightness control stay unchanged.

## Non-goals

- **Not** a battery / charge mechanic. The "dying battery" dim is a *cosmetic* flicker pattern, not a depletable resource. No HUD addition, no gameplay impact.
- **Not** a redesign of the flashlight model or prefab structure. The existing Light child stays in place.
- **No idle breath sway** (the user picked walk-only sway in brainstorming).
- **No save-system changes**. Flashlight enabled state and intensity already persist via `ApplyFlashlight`; flicker/sway phase are intentionally not saved.

## Visual specification

Locked through visual-companion brainstorming on 2026-05-12:

| Dimension | Choice |
|---|---|
| Overall vibe | **B — Dry / dusty horror** (sharper-edged beam, visible dust storm) |
| Halo at source | **Yes** — bright bloomy halo (added on top of vibe B) |
| Color temperature | **Warm white ~3500 K** — RGB approx `(1.00, 0.94, 0.78)` |
| Flicker character | **Rare panic stutter** (every 6–10 s) **+ occasional dying-battery dip** (every 35–55 s) as seasoning |
| Sway intensity | **Medium ±2°** walking bob, no idle breath |

## Architecture

Four cooperating layers, all parented to the existing flashlight Transform so they rotate together:

| Layer | Component | Render queue | Notes |
|---|---|---|---|
| **Real illumination** | `UnityEngine.Light` (Spot, `ForcePixel`) — existing | n/a | Add a *light cookie* texture (soft radial gradient with faint gobo bands) for surface texture |
| **Visible beam** | Procedurally-generated open-cone mesh + new `FlashlightBeamCone.shader` material | 2700 (Transparent − 300) | Renders *after* opaque atmosphere — beam visibly cuts through haze. Mimics the working `Glass_EarlyQueue.shader` pattern so Unity's StandardShaderGUI doesn't reset the queue |
| **Dust** | `ParticleSystem` child GameObject, ~25 simultaneous billboard quads | 3000 (Transparent default) | Lit by the actual Spot light — particles only glow where the beam hits |
| **Halo** | `LensFlare` component using existing `Assets/Lens Flares/Halogen Bulb.flare` | n/a (Unity internal) | Free since the package is imported. Brightness driven by `flashlight.intensity` |

### Files

- **`Assets/3 - Scripts/Player/PlayerFlashlight.cs`** — *modified*. Grows from 75 lines to ~250. Keeps the existing toggle + scroll-brightness logic intact. Adds public refs to the four new layers and embeds the flicker + sway behavior as private methods (one file because everything is per-player-flashlight; no reuse benefit from splitting).
- **`Assets/2 - Materials/FlashlightBeamCone.shader`** — *new*. ~80-line transparent additive surface shader. Inputs: `_Color`, `_InnerStrength`, `_OuterStrength`, `_NoiseScale`, `_FalloffPower`. Render queue baked into `SubShader Tags { "Queue"="Transparent-300" }` per the queue gotcha in `CLAUDE.md`.
- **`Assets/2 - Materials/FlashlightBeam.mat`** — *new*. Material instance using the shader.
- **Cone mesh** — *generated at runtime* in `PlayerFlashlight.Start()` and **rebuilt in `Update()` when `coneAngle` or `coneLength` change** (compare against cached previous values so it's free in the steady state). Open-ended cone, 16 radial segments, normals outward. Not a saved asset — keeps the asset surface small and makes the inspector fields live-tunable per the CLAUDE.md convention.
- **Dust ParticleSystem** — authored as a *child GameObject* in the scene (`Player/Flashlight/DustParticles`). Particle systems are painful to author in code. User commits the prefab/scene change.
- **`Halogen Bulb.flare`** — existing asset, dragged into a `LensFlare` component on the flashlight in the scene.

No save-system changes. `flashlightEnabled` and `flashlightIntensity` already persist via `SaveCollector.ApplyFlashlight` (line 820 of `SaveCollector.cs`).

## Behavior modules

All three loops run inside `PlayerFlashlight.Update()`. None of them ticks while the light is disabled or while piloting the ship.

### Flicker

Pipeline: `final_intensity = base_intensity * micro_drift * scheduled_event_multiplier`

1. **`base_intensity`** — the scroll-wheel-controlled value, clamped to `[minBrightness, maxBrightness]`. Unchanged from current behavior.
2. **Micro-drift** — `1 - microDriftAmount * (Mathf.PerlinNoise(Time.time * 3f, 0) - 0.5f)`. `microDriftAmount` default `0.04` (±2% wobble). Subliminal "the bulb is alive" effect.
3. **Panic stutter coroutine** — fires every `Random.Range(panicStutterInterval.x, panicStutterInterval.y)`. Default interval `(6, 10)` seconds. Executes a hardcoded sequence:
   - Multiplier 0.20 for 1 frame
   - Multiplier 1.00 for 1 frame
   - Multiplier 0.40 for 1 frame
   - Multiplier 1.00 for 1 frame
   - Multiplier 0.15 for 2 frames
   - Multiplier 1.00 — done
   - Total duration: ~120 ms at 60 fps. Reads as a sharp glitch.
4. **Dying-battery dip coroutine** — fires every `Random.Range(dyingDipInterval.x, dyingDipInterval.y)`. Default interval `(35, 55)` seconds. Lerps multiplier from `1.0 → 0.45 → 1.0` over 1.8 seconds with a small Perlin jitter at the bottom of the curve.
5. The two coroutines run **independently**; if their timings overlap, multipliers compose (multiply). Rare but acceptable.

Halo `LensFlare.brightness` is also driven by `final_intensity` so the bloom dims with every flicker — sells the effect cohesively.

### Sway

1. Read `playerController.SurfaceVelocity.magnitude` each frame. `SurfaceVelocity` (defined at `PlayerController.cs:794`) combines `rb.velocity` and the walking-input vector — the latter is bypassed by `MovePosition` and would be missed by `rb.velocity` alone.
2. `targetWalkFactor = Mathf.Clamp01(speed / walkingTopSpeed)`. Default `walkingTopSpeed = 4` m/s.
3. Smooth: `walkFactor = Mathf.SmoothDamp(walkFactor, targetWalkFactor, ref vel, 0.15f)` — beam ramps in/out gracefully when player stops/starts.
4. Compose sway:
   - `pitch = sin(Time.time * walkBobFrequency) * walkBobAmplitude * walkFactor`
   - `yaw   = sin(Time.time * walkBobFrequency * 0.5f) * walkBobAmplitude * 0.6f * walkFactor`
   - The 0.5× frequency on yaw produces a figure-8 trace on the ground (one of the small but recognizable details of a real handheld light).
5. Apply: `flashlight.transform.localRotation = baseLocalRot * Quaternion.Euler(pitch, yaw, 0)`. `baseLocalRot` is captured in `Start()` after auto-finding the Light.

Defaults: `walkBobAmplitude = 2°`, `walkBobFrequency = 7`, `walkingTopSpeed = 4`.

### Brightness (unchanged)

Scroll wheel still drives `_base` in `[minBrightness, maxBrightness]` (defaults `0.5` and `8`). Flicker multiplies on top.

## Inspector surface

All groups are `[Header(...)]`-decorated. Everything is live-tunable in Play mode per the CLAUDE.md "live-tune in Update" convention.

```
[Header("Light")]
  Light flashlight
  KeyCode toggleKey = E
  Texture2D lightCookie

[Header("Brightness")]
  float minBrightness = 0.5
  float maxBrightness = 8
  float scrollSensitivity = 20

[Header("Beam Cone")]
  MeshRenderer beamRenderer
  float coneLength = 30
  float coneAngle = 50         // degrees, half-angle
  Color innerColor = (1, 0.94, 0.78, 0.85)
  Color outerColor = (1, 0.94, 0.78, 0)

[Header("Dust Particles")]
  ParticleSystem dustParticles
  float dustRateScale = 1      // emission multiplier

[Header("Halo")]
  LensFlare halo
  float haloIntensityMultiplier = 1

[Header("Flicker")]
  bool enableFlicker = true
  float microDriftAmount = 0.04
  Vector2 panicStutterInterval = (6, 10)
  Vector2 dyingDipInterval = (35, 55)

[Header("Sway")]
  bool enableSway = true
  float walkBobAmplitude = 2
  float walkBobFrequency = 7
  float walkingTopSpeed = 4
```

## Edge cases & integration

- **Piloting the ship** — existing guard in `Update()` stays: when `_ship.IsPiloted`, return early. Extend it to also hide the beam mesh, particles, and halo by setting `beamRenderer.enabled = false`, calling `dustParticles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear)`, and `halo.enabled = false`. Same gate when the flashlight itself is toggled off via E.
- **Floating origin** — flashlight is parented to the Player, which is already a registered physics object. No `EndlessManager.RegisterPhysicsObject` call needed for the new children since they inherit the Player's parent space.
- **Atmosphere render-queue gotcha** — the cone shader `Queue="Transparent-300"` (= 2700) is **above** the 2500 cutoff, so the beam renders *after* the opaque atmosphere pass and visibly cuts through atmospheric haze. This is the intended look. Particles default to queue 3000 — also above the cutoff, so they render correctly.
- **Performance** — one extra transparent draw call (~32 tris, the cone), one small particle system capped at ~25 active particles, one lens flare component. Negligible relative to the project's existing budget.
- **Save system** — zero changes. Flicker phase, dying-dip cooldown, and sway phase are deliberately ephemeral.
- **First-time scene setup** — manual: drag the cone mesh's `MeshRenderer`, the dust `ParticleSystem` child, and the `LensFlare` component into their respective slots on the `PlayerFlashlight` script on the Player. The implementation plan will include this as a checklist step.

## Open questions resolved

- **Dust as procedural particles or scene-authored?** Scene-authored. Procedural ParticleSystems in code are 3x the work for no real-world benefit when only one instance exists.
- **Halo as Unity LensFlare or custom billboard?** Unity LensFlare. The `Lens Flares` package is already imported and `Halogen Bulb.flare` matches the warm-white target perfectly.
- **Cone mesh as a saved asset or runtime-generated?** Runtime-generated. Lets `coneAngle`/`coneLength` rebuild the mesh from inspector changes in Play mode without re-importing assets.

## Out of scope (deferred)

- Battery / charge mechanic — discussed in non-goals.
- Idle-breath sway — user explicitly picked walk-only.
- Adaptive flicker (e.g., panic stutter rate increases near enemies) — could be a great horror beat but is a future polish pass, not part of this baseline.
- Custom flare prefab — `Halogen Bulb.flare` is good enough for v1.
