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
