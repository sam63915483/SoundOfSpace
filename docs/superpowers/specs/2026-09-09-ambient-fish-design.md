🟢 ACTIVE — built 2026-09-09, playtest pending.

# Ambient fish in planet water

**Goal (Sam, 2026-09-09):** you can see fish swimming in the water on a planet,
and they are the species that actually live on that planet. Purely visual. Must
not cost FPS. The bite animation is untouched.

## Decisions (Sam, answered 2026-09-09)

| Question | Answer |
|---|---|
| Where visible | Everywhere, including from the air |
| Grouping | Scattered loners, not shoals |
| Reaction | React to the player/bobber, but **no gameplay change** |
| Rarity | Rares glow, and are rare to see |

## Architecture

One file, `Assets/3 - Scripts/Fishing/AmbientFishField.cs`, an auto-singleton
modelled on `SpaceDustField` — the house pattern for "endless local field at
constant cost, safe under floating origin".

**No GameObjects.** A fixed pool of fish exists as a struct array (planet-local
position, heading, species, size, tail phase). Each frame that becomes a
`Matrix4x4`; the matrices go out through `Graphics.DrawMeshInstanced`. Nothing is
instantiated or destroyed, nothing allocates per frame, no colliders, no
rigidbodies, no lights.

**All motion is planet-local.** In world space ~98% of a fish's frame delta is
the planet's own orbital motion — the bug `Bobber.PoseFishLocal` documents
(fish aimed along the orbit, read its speed as 85 m/s). World conversion happens
only when the draw matrix is built, so origin rebases need no handling at all.

**Execution order 300**, like `SpaceDustField`: after the origin rebase (0) and
`CameraTransformFX` (100), so the field never lags the camera by a frame.

### The swim look

Lifted from `Bobber.PoseFishLocal`: mouth-anchored body yaw (the models have no
bones, so the whole body sweeps about the nose), tail beat driven by real speed,
45° pitch clamp. **`Bobber` is not modified** beyond two one-line `Disturb`
calls — its pose maths is heavily tuned and there is no reason to touch it for a
cosmetic feature.

Models face **-Z** (confirmed: the `MOUTH` marker in `fish01.prefab` sits at
local z = -1.201).

### Species

`PlanetEconomy.CatchableIndices(bodyName)` — the same list the bite roll uses, so
the fish in the water are by construction the fish that bite there. Tier odds via
`FishingRules.RollTier` (so rares are rare to see), size via `RollWeight` →
`BodyLengthForWeight` / `GirthFactorForWeight` — the same size law as a caught
fish. Bounty species (GRULABU) are filtered out: only a `BountyZone` may show one.

Glow via `FishSpeciesVisuals.EmissionFor`, so a rare in the water matches a rare
in your hand. **No point light on ambient fish** — the hooked fish gets one;
forty would be a real framerate problem in a project that already needs
`PixelLightLimitFix`.

## The sea-bed cache — "only water you can see"

Sam's two requirements: fish only in visible water (never water sealed inside the
terrain), and never swimming through the ground. Both come from one cached
height field.

The sea floor is divided into patches. The first time a patch is needed, **one
raycast** answers it, cast from above the highest terrain straight down along the
radial:

- hits terrain **above** the waterline → land / cave roof / overhang → never fish
- hits terrain **below** the waterline → open water, store the bed radius
- **no hit** in the probe length → water deeper than the probe → deep open water

Starting the ray *above* the terrain (not just under the surface) is load-bearing:
a ray starting inside a mesh does not hit its backfaces, so a probe from under the
waterline classifies a mountain as deep water. The start radius comes from the
terrain collider's bounds, so no assumption about terrain height is made and
nothing in the forbidden `Celestial/` zone is read.

A patch only gets fish if there is a clear column from the bed straight up to the
open surface, which is exactly "water you can see". Because a fish is always
clamped between `bed + clearance` and `surface - 0.4 m`, it can never be inside
rock — and because the first hit from above is the cave *roof*, it can never
enter an air-filled cave below sea level either.

Terrain doesn't move, so the cache never goes stale. Standing still: **zero
raycasts**. Moving: a trickle, hard-capped at 4 probes per frame with the rest
queued, so arriving somewhere new can never spike.

### Avoiding the ground

- **Depth clamp** — the fish's radius is clamped to at least `bed + 0.5 m`, moved
  toward smoothly, so it glides up a slope instead of stepping.
- **Lookahead** — the bed 3 m ahead is a dictionary lookup. Too shallow, unknown,
  or land → the fish turns away. So they veer off before the bank.

Every uncertain case falls to "no fish". The failure mode is a fish that doesn't
spawn, never a fish inside a rock.

### Altitude

Patch size scales with camera altitude — ~4 m near the surface, ~16 m high up.
Flying sweeps across a lot of new patches, and at 4 probes/frame fine patches
could not keep up; from 200 m you cannot see the difference. Field switches off
above ~250 m altitude, where a half-metre fish is under two pixels.

## Reaction

`AmbientFishField.Disturb(worldPos, radius)` — a static no-op when the field
doesn't exist. Called from `Bobber` at the splash and when a bite approach
starts (so the real fish rises into clear water). The camera is a standing
disturbance, so swimming into fish scatters them. Two distance checks per fish
per frame.

## Deliberately not doing

- **No save state.** Purely cosmetic — keeps it clear of the fragile apply order.
- **No new quality slider.** Fixed count, like crystals (20) and the concert
  audience (25) — the "settings = distance, not counts" rule already settled this.
- **Co-op:** fish positions differ per client. They are decoration and nothing
  references them.
- No surface jumping/rippling. Easy to add later.

## Traps respected

- **Trap #1** — seeded in `MainMenuController.EnsureGameplaySingletons()`, or it
  works in the Editor and is invisible in builds.
- **Instancing material must be a Resources asset.** A runtime
  `new Material(Shader.Find(...))` loses its `INSTANCING_ON` variant in builds and
  `DrawMeshInstanced` draws nothing in the player while working in the Editor —
  `SpaceDustField` documents this exact bug. Hence `Assets/Resources/AmbientFish.mat`.
- **Opaque queue (2000).** The ocean post tints by scene depth, so opaque is what
  makes water hide and fade the fish. Above 2500 they would float visibly on top.
- The Floreswa fish materials point at a URP shader this project doesn't have, so
  they render with the error shader and ignore colour (`FishSpeciesVisuals`
  documents it). Colours are read defensively (`_Color`, else `_BaseColor`, else
  white) and drawn through our own Standard material.
- Skips gallery/test scenes (`GallerySceneQuiet` present).
- No `FindObjectOfType` in the per-frame path; all scene refs cached with
  throttled re-resolve.

## Files

| File | Change |
|---|---|
| `Assets/3 - Scripts/Fishing/AmbientFishField.cs` | new — the whole feature |
| `Assets/Resources/AmbientFish.mat` | new — instancing-enabled Standard base |
| `Assets/3 - Scripts/Fishing/Bobber.cs` | +2 lines: `Disturb` at splash and at approach start |
| `Assets/3 - Scripts/UI/MainMenuController.cs` | +1 line: trap-#1 seeding |

---

# Pass 2 — motion rewrite (2026-09-09, after Sam's first playtest)

Sam: *"sometimes the fish make jerky movements... when they tilt up and down to
swim down the side of a bank"* and *"I can see fish swimming into the bank and
going right through it in some areas."* Both were structural, not tuning.

The motion model is pure maths, so it was ported to Python and **measured**
rather than eyeballed (the same trick `FishingRules` uses via
`verify-fishing.py`; the fishing notes record that hand-iterating this kind of
multi-knob feel overshot twice).

## Measured, before -> after

| | pass 1 | pass 2 |
|---|---|---|
| Penetration into a bank (15-60 deg) | up to **2.19 m** | **0.00 m** |
| Penetration into a boulder the grid cannot see | **0.99 m** | **0.00 m** |
| Worst pitch change, one frame | **27.4 deg** | **0.0 deg** |
| Sea-bed jump per cm travelled | **83.8 cm** | **0.22 cm** |
| Sea-bed raycasts | 4/frame cap | unchanged, **+~2/frame** whisker |

## Root causes

**Jerk - three stacked causes.**

1. Depth used `MoveTowards`: a bang-bang controller whose vertical speed is
   either 0 or the full climb rate. It stepped 0 -> 0.7 m/s in one frame, so the
   pitch snapped `atan(0.7/1.35)` = **27.4 deg** in that frame. On rolling ground
   the 99th-percentile change was 0.0 deg - flat, then a snap. Exactly what Sam saw.
2. The sea bed was a **step function**. `BedAt` read one patch raw. This spec's
   pass-1 text said the bed would be "blended between neighbouring patches" and
   the code never did it.
3. The facing came from the **raw one-frame delta**. `Bobber.PoseFishLocal`
   low-passes its velocity before facing with it and says why - that line was
   not copied.

**Through the bank.** The look-ahead asked *"is there water here at all"*
(a global 1.2 m minimum) instead of *"is there water here for me, at my depth"*.
A fish 5 m down read a bank with 2 m of water over it as clear and swam in; the
depth clamp could then only lift it at 0.7 m/s against ground rising faster.

## Fixes

- **Cube-face patch grid + bilinear interpolation.** Pass 1 quantised in raw 3D,
  where the four patches around a point are not neighbours. Cube-face keying
  makes them neighbours, probes land on patch **centres**, and the bed becomes
  continuous. Round-trip, continuity and key-collision all verified separately.
- **Depth constraint = shallowest bed along the path AHEAD**, not underneath, so
  a fish starts rising before the ground arrives and follows the bottom up.
- **`Mathf.SmoothDamp` on the radius** - continuous vertical velocity.
- **Low-passed velocity for the facing** (Bobber's line, `facingSmoothing`).
- **Proportional steering**, ramped between `turnStartWater` and `minSwimWater`,
  turning toward the **deeper side**, so fish follow the run of the shore.
- **Speed eases** while turning or climbing.
- **Land contributes the waterline** to the interpolated bed, so the shore is a
  smooth ramp in the constraint rather than a cliff.

## The whisker - Sam's "roomba" question, answered

Sam asked whether the fish could just carry a sphere collider. The height field
**structurally cannot** see a boulder, a spire between probe points, or a prop
off the terrain layer - measured, a fish swam 0.99 m into one. So each fish casts
one short ray along its heading, round-robin: **~2 rays/frame for the whole
pool**, against 2,400/s for a ray per fish per frame, with no rigidbodies or
contacts. Predictive, not reactive, so there is never a "fish shoving against a
rock" look.

**It steers only and must never touch the depth target.** Feeding sparse ray data
into the continuous depth constraint spiked the pitch to **71-84 deg/frame** -
worse than the bug being fixed. A fish goes *around* a rock, which is both smooth
and what a fish does.

## Bug caught in review, not by the harness

`Mathf.Clamp` does **not** sort its bounds - given `min > max` it returns the min.
Since land contributes the waterline to the bed, the floor could exceed the
ceiling near a beach and the clamp would hand back a radius **above the water**,
lifting a fish out of the sea for the frames before it turned away. The floor is
now capped to the ceiling first.
