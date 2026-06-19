# Space Dust Field — Design Spec

**Date:** 2026-06-17
**Status:** Approved (brainstorm complete, ready for implementation plan)
**Scope:** Purely-visual atmosphere effect. No gameplay, no collection, no save state.

---

## 1. Goal

Add an "astrophage"-style field of glowing amber specks of dust in open space, inspired by *Project Hail Mary*. The specks:

- Drift slowly toward the black hole (a few m/s), so a stationary player sees slow inflow and a fast player (e.g. 100 m/s) overtakes them — giving a clear, intuitive **motion / direction cue** via pure parallax.
- Are **denser closer to the black hole** and **denser farther from any planet** (gravity has swept the inner-planet space; deep space and the BH's pull accumulate dust).
- Cluster into **galaxy-style logarithmic spiral arms** feeding the black hole, with a *light* touch of turbulent filaments (not heavy chaos).
- **Never appear inside a planet's atmosphere** (so they vanish as you descend and are absent on the surface).
- **Escalate into a dense, dramatic swirl as you fly toward the black hole** — you can fly *into* it.
- Feel **rock-solid through floating-origin rebases** — nothing visibly shifts when `EndlessManager` rebases the world.

**Emotional target:** the player flies up out of the atmosphere, exits the ship, and sees a living, glowing field of dust streaming toward the black hole — an "oh wow" moment that gives the currently-empty space depth and atmosphere.

### Locked creative decisions (from brainstorm)
| Decision | Choice |
|---|---|
| Interactivity | **Purely visual** (no collecting, no link to `SpaceDustInventory`) |
| Colour / glow | **Warm amber embers** (gold/orange motes, soft warm glow) |
| Structure | **Galaxy spiral arms** + a *light* touch of turbulent filaments |
| Baseline density (deep space) | **Noticeable field** — clearly present, gives parallax, doesn't obscure view |
| Black-hole behaviour | **Escalating spectacle** — subtle near planets, thickening into dense arms near the BH; flyable-into |
| Speed effect | **Pure parallax only** (no streaking / light trails) |
| In-game toggle | **Yes** — CAMERA-tab on/off switch for performance |

---

## 2. Chosen approach

**CPU-driven, camera-anchored wrapping volume + GPU-instanced amber billboards, computed relative to the black hole.**

A fixed pool of specks lives in a cube of side `L` that follows the camera. Each speck is effectively **world-fixed** (you fly through them → parallax); when a speck leaves the box it **wraps** to the opposite side, producing an endless local field at constant cost. Per-speck brightness/visibility comes from a **density field** sampled at the speck's world position. All math is done **relative to the black hole's position**, which makes floating-origin rebases automatically invisible.

### Alternatives rejected
- **Pure-GPU compute** (`DrawMeshInstancedIndirect`, positions hashed from cell coords): scales to 100k+ but compute-shader authoring is complex/risky for this codebase and overkill for a "noticeable" field.
- **Unity Shuriken particle system**: least code, but world-space particles *jump* on every origin shift and the spiral-arm density field is very hard to express. Poor fit.

---

## 3. Why floating origin is free here

`EndlessManager` rebases the world by subtracting `originOffset` from every registered transform when the player drifts past `distanceThreshold` (1000 units), firing `PostFloatingOriginUpdate` afterward. The black hole is a `CelestialBody` whose `Position` comes from `NBodySimulation` — so it is rebased **together with the camera**.

Therefore `camRelBH = cameraPos − blackHole.Position` is **invariant under a rebase**. We drive the speck field from the frame-to-frame delta of `camRelBH` (the player's *genuine* movement), so:

- A rebase changes both `cameraPos` and `blackHole.Position` by the same `−originOffset` → `camRelBH` delta for that frame = genuine movement only → **no visual jump**.
- Speck world positions used for density sampling are `cameraPos + local`, which shift with the camera exactly like planets do → all relative distances (to BH, to planets) are invariant.

**No subscription to `PostFloatingOriginUpdate` is required.** The black hole *is* the stable anchor. (Guard: if `camRelBH` delta in a single frame exceeds a sanity threshold — e.g. > `distanceThreshold` — treat it as a glitch and skip parallax that frame, belt-and-suspenders against any frame where BH lookup briefly fails.)

---

## 4. Components

### 4.1 `SpaceDustField.cs` (new)
- **Location:** `Assets/3 - Scripts/World/SpaceDustField.cs` (next to `InstancedGrassRenderer.cs`).
- **Auto-singleton**, mirroring `SpaceDustInventory.cs` exactly:
  - `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` auto-create, early-return if active scene is `MainMenu`.
  - `Instance` guard in `Awake` (`if (Instance != null && Instance != this) { Destroy(gameObject); return; }`), clear in `OnDestroy`.
  - `DontDestroyOnLoad`.
- **MUST be seeded in `MainMenuController.EnsureGameplaySingletons()`** (CLAUDE.md trap #1) — mirror an existing `if (X.Instance == null) {...}` block. Sanity-check in a build.
- Owns: particle pool arrays, per-frame update, density field, instanced draw, all serialized tuning fields (appended at END of class per conventions).

### 4.2 `SpaceDust.shader` (new)
- **Location:** `Assets/3 - Scripts/World/SpaceDust.shader` (+ `.meta`). **Not** in the forbidden zone.
- Unlit, additive, GPU-instancing-enabled billboard:
  - Orients the quad to face the camera **in the vertex shader** (CPU only writes position/scale/colour).
  - Soft radial falloff (samples the runtime glow texture, or computes falloff analytically).
  - `Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }`, `Blend One One` (additive), `ZWrite Off`, `ZTest LEqual` (so specks behind opaque geometry / a planet are correctly occluded), `Cull Off`, no shadow casting/receiving.
  - `#pragma multi_compile_instancing`; per-instance `_Color` (rgb tint, a = brightness) via instanced properties / `MaterialPropertyBlock`.
- **Build-variant caution (memory: grass horizon wash):** code-created materials can have shader variants stripped from builds. Ship the material/shader as a real asset and ensure the instancing variant is kept (assign the material in-scene or add to an Always-Included shader list if needed). Verify in a **build**, not just the Editor.

### 4.3 Runtime glow texture + material
- Generated at runtime (mirrors `ConcertParticleAssets.cs`'s procedural-texture pattern), radial amber gradient, `hideFlags = HideFlags.HideAndDontSave`. Material uses `SpaceDust.shader`.

### 4.4 In-game toggle (CAMERA tab)
- Add an `InputSettings.fxSpaceDust` flag + PlayerPrefs load/save lines (mirror an existing `fx*` flag).
- Add a `ToggleDef` row to the `TabbedPauseMenu` CAMERA tab.
- `SpaceDustField` polls the flag and disables its draw when off.
- (Follows CLAUDE.md "New camera effect" recipe, minus the `CameraEffectsManager` module since this isn't a post-process.)

### 4.5 No save / reset changes
Purely visual and fully deterministic from live positions → **no `SaveData` fields, no `SaveCollector` capture/apply, no `NewGameReset` entry.**

---

## 5. Per-frame update logic

1. **Acquire refs (cached, lazy-refind only if null — never `FindObjectOfType` in Update):**
   - Camera (the main gameplay camera).
   - Black hole: scan `NBodySimulation.Bodies` once for the `isStaticAttractor` body named "Black Hole"; cache it. `NBodySimulation.Bodies` is null-safe (empty array off the solar scene).
   - Planet list: `NBodySimulation.Bodies` minus the static attractor; refresh occasionally (bodies don't appear/disappear at runtime, but be null-safe).
2. **Global early-outs (skip the whole draw):**
   - Toggle off.
   - No black hole found / `Bodies` empty (off the solar-system scene).
   - Camera is inside any atmosphere (nothing would be visible anyway).
3. **Compute genuine camera delta:** `genuineDelta = camRelBH − prevCamRelBH`; if `|genuineDelta| > sanityThreshold` skip parallax this frame (origin-glitch guard); store `prevCamRelBH`.
4. **Update each speck:**
   - `local += driftDir(towardBH) * driftSpeed * dt − genuineDelta`.
   - Wrap each axis of `local` into `[−L/2, L/2]` (toroidal): `local.x -= L * round(local.x / L)`, etc.
   - `worldPos = cameraPos + local`.
   - Evaluate **density** `d ∈ [0,1]` at `worldPos` (see §6). Inside any atmosphere → `d = 0`.
   - Visibility: speck visible iff `d > speck.threshold` (per-speck random in [0,1)); brightness/size scale with `d` and a soft fade near the box edge (`1 − smoothstep` of `|local|` toward `L/2`) to hide wrapping pops. A small per-speck twinkle (sine on a per-speck phase) adds life.
   - Write the instance matrix (position + size) and instance colour (amber × brightness).
5. **Draw:** batched `Graphics.DrawMeshInstanced` (quad mesh, shared material), batches of ≤1023. Skip instances with brightness ≈ 0 (compact into the batch arrays so cost tracks *visible* count).

### Performance notes
- Budget: **~4,000–6,000 specks** initially (tunable; "crank later" is fine). One material, ~4–6 draw calls, no shadows, no depth write.
- Per-frame cost is `O(N × bodies)` for density/atmosphere checks (~5k × ~9 ≈ 45k cheap ops) — fine. If ever needed, throttle by updating a fraction of specks per frame (round-robin) — note only, not in v1.
- Drift is slow (a few m/s) so at ship speeds (~100 m/s) parallax dominates.

---

## 6. Density field `d(worldPos) ∈ [0,1]`

Combine three factors (then clamp):

- **`f_bh` (black-hole proximity):** smooth ramp from ~0 at a far radius to ~1 approaching the BH (e.g. `smoothstep` on distance to `blackHole.Position`, between an outer falloff radius and the event horizon). Drives the escalating spectacle.
- **`f_planet` (planet avoidance):** for the nearest planet, `0` inside its atmosphere `(1 + atmosphereScale) * radius`, rising to `1` past a falloff band. Implemented by testing all non-attractor bodies and taking the minimum (any atmosphere kills the speck).
- **`f_arm` (spiral structure):** project `worldPos` into the black hole's equatorial plane; compute radius `ρ` and angle `θ`; arm phase `= θ − k·ln(ρ)`; brightness peaks near `armCount` (2–3) arms via a raised-cosine of the wrapped phase. Add **light** value-noise turbulence (low amplitude) for the filament touch. `f_arm` modulates clustering (e.g. blends between a low floor and 1 so arms are denser but inter-arm space isn't fully empty).

`d = baseDensity * f_bh * f_planet * lerp(armFloor, 1, f_arm)`. All coefficients are serialized for tuning.

**Atmosphere radius access (read-only):** `(1 + atmosphereScale) * body.radius`, where `atmosphereScale` comes from the body's generator settings (`CelestialBodyGenerator → shading → atmosphereSettings.atmosphereScale`). Cache per body at startup; **do not modify any forbidden-zone file** — read-only inspection only. If a body has no atmosphere settings, treat its atmosphere radius as just its `radius` (or a small multiple) so specks still avoid the solid body.

---

## 7. Tuning fields (serialized, appended at END of class)
`particleCount`, `boxSize L`, `driftSpeed`, `baseDensity`, `bhOuterRadius`/`bhFalloff`, `armCount`, `armTwist k`, `armFloor`, `filamentStrength`, `glowSize`/`sizeJitter`, `amberColorA`/`amberColorB` (gradient), `brightness`, `twinkleAmount`, `edgeFade`, `sanityThreshold`. Sensible defaults baked in; expose for live tweaking in the Inspector.

---

## 8. Risks & mitigations
- **MainMenu singleton trap (CLAUDE.md #1):** must seed in `EnsureGameplaySingletons()` and verify in a build. → explicit plan step.
- **Build shader-variant stripping (memory: grass horizon wash):** ship a real material/shader asset with the instancing variant kept; verify in a build. → explicit plan step.
- **Forbidden zone:** atmosphere/celestial generation is read-only. We only *read* `radius` / `atmosphereScale` and `Position`. No edits to `Atmosphere.cs`, `Celestial/`, planet shaders/materials. → enforced in plan.
- **`Camera.main`/`FindObjectOfType` in Update:** banned by conventions; cache + lazy-refind. → enforced in plan.
- **Atmosphere post-process washing the dust:** dust is culled inside atmospheres and lives in open space, so overlap is minimal; if distant atmosphere bands tint specks, tune render queue. → note for tuning, not a blocker.

---

## 9. Out of scope (possible later phases)
- A giant **distant accretion-disk / swirl backdrop** visible from across the system (a billboarded swirl sprite at the BH). The chosen "escalating spectacle" is delivered by the local field's density ramp; a far-visible mega-swirl is a separate, larger feature.
- Any **collection / harvesting** of the dust (explicitly out — purely visual).
- Audio cues near dense dust.
