# CartoonGrass on Humble Abode — Design

**Date:** 2026-06-06
**Status:** Design (awaiting approval)
**Author:** Sam + Claude

## Goal

Carpet the planet **Humble Abode** with ground foliage from the `Assets/CartoonGrass/`
pack — grass, plants, and flowers — placed as a natural **mix of clustered patches and
lighter even scatter**, at **medium density**. Decorative only (no collision, no harvest).
The system must be reusable so other planets (e.g. Cyclops) can get differently-coloured
grass later with minimal effort.

## Key findings (verified)

- **The pack:** `Assets/CartoonGrass/`. Prefabs live in `Assets/CartoonGrass/Prefabs/`.
- **Rendering — NOT a problem.** Despite the `.unitypackage` being named "URP", all eight
  `.shadergraph` files target **`BuiltInTarget` / `BuiltInLitSubTarget`**, and no URP package
  is installed (only `com.unity.shadergraph` 14.0.12 + its core dependency, which is what makes
  Built-in shader graphs compile). The grass renders correctly as-is — **no material conversion
  needed.** (Correcting an earlier assumption; this was checked against the shader files.)
- **Colour change** is two exposed shader properties: **`TopColor` / `BottomColor`** (a vertical
  gradient, currently greens). The shader also has built-in **wind sway** (`WindSpeed` / `WindStrength`).
- **LODs are baked into the prefabs** (e.g. `Grass_01` has a 3-level LODGroup), so distance
  performance is handled by the prefabs themselves — the spawner does not manage LOD.
- **Prefabs have no colliders** → decorative is the natural state, nothing to strip.
- **Template:** `Assets/3 - Scripts/World/MushroomSpawner.cs` + `SpawnerCubeface.cs` (shared,
  read-only). The cubeface streaming spawner is the proven, floating-origin-safe, orbit-safe
  pattern for scattering props across a runtime-generated planet.

## Prefab set (31 prefabs, weighted by category)

- **Grass (dominant):** `Grass_01, 02, 03, 04-1, 04-2, 05-1, 05-2, 06-1, 06-2, 07-1, 07-2, 08-2`
  — 12 prefabs. **`Grass_08-1` is excluded** (user dislikes it).
- **Plants (occasional):** `Plant_01` … `Plant_13` — 13 prefabs.
- **Flowers (rare accent):** `Flower_01` … `Flower_06` — 6 prefabs.
- **Rocks excluded** (`Rock_01/02` exist in the pack but are out of scope).

Each category has its own inspector array + a **weight**. A spawn first picks a category by
weight (grass ≫ plants > flowers), then a uniform prefab within that category. Default weights
make grass the clear majority with flowers as sparse accents, so it never looks like a flower bed.

## Architecture

### New file: `Assets/3 - Scripts/World/GrassSpawner.cs`

A scene `MonoBehaviour` adapted from `MushroomSpawner`. **Scene component, not an auto-singleton**
— so it sidesteps the MainMenu-seeding trap (#1) entirely. Lives on a GameObject under
`--- Managers ---` in `Assets/1.6.7.7.7.unity`.

**Reused verbatim from the mushroom pattern:**
- Deterministic cubeface cell hashing (`SpawnerCubeface.Hash/EncodeCell/FaceUVToDirection`).
- Per-planet `BodyState`, `excludeBodyNames` (default `{ "Sun" }` → grass on every other body;
  for *this task* we can also restrict to Humble Abode via the same list, see "Scope" below).
- Streaming within a player-distance radius; distance-sorted candidate selection; max-instance cap.
- Surface raycast down, slope reject (`maxSurfaceAngle`), ocean reject (`gen.GetOceanRadius()`),
  `SpawnExclusionZone.IsExcluded` reject (keeps grass off the ship pad / market).
- Parent each instance to `body.transform` (orbit + floating-origin safe), set `WorldPropLayer`,
  `groundMask &= ~SpawnerCubeface.WorldSpawnExcludeMask`, `SpawnFade` fade-in, per-prefab pooling.
- `ComputeLocalBottomY` seating so each blade's base sits on the surface regardless of pivot.

**Dropped vs MushroomSpawner (grass is simpler):**
- No collider, no `MushroomInteraction`, no per-instance colour/breath/kaleido params.
- **No save integration whatsoever.** Grass is deterministic from the seed and never consumed,
  so it regenerates identically every load — there is nothing to persist. (No `SaveData` changes,
  no `SaveCollector` hooks, no `NewGameReset` entry.)

### Structure blocking — no grass under the start cabin

Requirement: grass must **not** spawn on the cabin floor / anywhere beneath the start cabin,
and the **cabin roof is the blocker**.

Mechanic (rides the existing raycast, no new pass): the per-cell surface raycast already fires
*downward from `surfaceRayHeight` (100 m) above the surface point*. We add a check on the hit:

- New marker component **`GrassBlocker`** (`Assets/3 - Scripts/World/GrassBlocker.cs`), an empty
  `MonoBehaviour` attached to the cabin's roof collider (or the cabin root, so all its colliders
  count). It exists only to be found via `GetComponentInParent<GrassBlocker>()`.
- In the spawner, after `Physics.Raycast` succeeds, if
  `hit.collider.GetComponentInParent<GrassBlocker>() != null` → **reject the cell.**
- Because the ray comes from above, a cell under the cabin hits the **roof first**, so the whole
  footprint (floor + interior + roof surface) is excluded automatically. Grass still grows right
  up to the cabin's outer edge.

Prerequisite: the cabin roof must have a **collider on a layer included in `groundMask`** so the
ray actually hits it (verified/ensured during wiring). The component is reusable — mark any future
building's roof to keep grass out from under it. The inherited `SpawnExclusionZone.IsExcluded`
check remains as a coarse positional fallback.

### Patches + scatter ("mix of both")

Per cell, a deterministic hash roll selects a **mode**:

- **Patch cell** (probability `patchChance`): instantiate a **cluster** of `bladesPerPatch`
  (default ~4–7, rolled per cell) instances, each jittered within `patchRadius` of the cell
  point, each with its own surface raycast/seat, random yaw, random scale, and an independently
  weighted prefab pick (so a tuft mixes strands + flat "cards", and occasionally a plant). Reads
  as a dense natural tuft.
- **Scatter cell** (probability `scatterChance`): a **single** instance — fills the gaps between
  patches lightly.
- Otherwise: empty cell.

**Per-cell storage:** each occupied cell maps to a `List<GameObject>` of its blades (1 for scatter,
N for a patch). Despawn returns every blade in the list to its per-prefab pool. The global
max-instance cap counts **total blades**, not cells, so a patch of 6 costs 6 against the cap.

### Colour-change hook (forward design for Cyclops)

Optional, **disabled by default**. Fields: `overrideTint` (bool), `topColor`, `bottomColor`.
When enabled, the spawner applies the colours to each spawned renderer via a
`MaterialPropertyBlock` targeting the shader's `_TopColor` / `_BottomColor` references — this
**never mutates the shared material assets**, so it's per-spawner. Humble Abode uses the default
greens (override off ⇒ zero work). Giving Cyclops a different palette later = a second GrassSpawner
(or per-body tint) with two colour fields set.

## Scope (this task)

**Humble-Abode-only this pass** (decided). Implemented via an `onlyBodyName = "Humble Abode"`
allowlist field (simpler/safer than excluding every other body by name); when set, the spawner
only services the body whose `bodyName` matches. `excludeBodyNames` is still present for later.
Spreading grass to all planets later = clear `onlyBodyName`.

## Inspector tunables (all sliders, tunable live in Play mode)

`onlyBodyName` (= "Humble Abode"), `excludeBodyNames`, `grassPrefabs[]`, `plantPrefabs[]`, `flowerPrefabs[]`,
`grassWeight / plantWeight / flowerWeight`, `seed` (unique, e.g. `45678`),
`cellSize`, `patchChance`, `scatterChance`, `bladesPerPatch (min/max)`, `patchRadius`,
`minScale / maxScale`, `maxSurfaceAngle`, `spawnRadius`, `maxGrass` (cap), `updateInterval`,
`surfaceRayHeight`, `groundOffset / groundEmbedPerScale`, `overrideTint / topColor / bottomColor`.

## Wiring (via Unity MCP)

1. Confirm the project compiles after adding the script (`check_compile_errors`).
2. Create a GameObject `GrassSpawner` under `--- Managers ---` in the gameplay scene.
3. Add the `GrassSpawner` component; set `onlyBodyName = "Humble Abode"`; assign the three prefab
   arrays (31 prefabs) and the `InputSettings` asset if the existing spawners use one for view distance.
4. **Cabin blocker:** locate the start-cabin object on Humble Abode, confirm its roof has a
   collider on a `groundMask`-included layer (add/adjust if missing), and attach `GrassBlocker`
   to the cabin root (or roof collider). Verify a downward ray over the cabin hits a `GrassBlocker`.
5. Verify the grass material render queue is ≤ 2500 (so grass sits correctly *behind* the
   atmosphere/ocean image effects, per the transparent-queue gotcha). Adjust the material's
   queue if needed (asset-level, allowed — not the forbidden generation code).
6. Press Play near the cabin on Humble Abode; tune density/patch sliders together; confirm the
   cabin floor stays bare.

## Out of scope

- Interactive / harvestable grass (decided: decorative).
- Recolouring Humble Abode's grass (stays default green; Cyclops palette is later).
- Grass on other planets in this pass (one `excludeBodyNames` edit away).
- Rocks from the pack.

## Testing / verification

No automated tests (Editor-only project). Verification is in-Editor:
- Compiles clean (no Console errors).
- Grass appears on Humble Abode's surface around the player, in visible patches + lighter scatter.
- Renders with correct green colour (not magenta) and behind atmosphere/ocean.
- Does not appear in ocean, on cliffs, or inside exclusion zones (ship pad/market).
- **No grass on the cabin floor, under the cabin, or on the cabin roof** (GrassBlocker working);
  grass grows up to the cabin's outer walls.
- Appears only on Humble Abode (no grass on other planets while `onlyBodyName` is set).
- Streams in/out with movement; instance count stays under the cap; no frame-time cliff.
- Survives a save/load and a scene reload (atmosphere round-trip) — grass reappears identically.
