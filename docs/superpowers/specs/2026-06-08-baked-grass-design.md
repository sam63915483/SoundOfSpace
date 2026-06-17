# Baked Grass for Humble Abode — Design

**Date:** 2026-06-08
**Status:** Approved (direction locked; proceeding to implementation)
**Scope:** The `InstancedGrassRenderer` blade carpet on Humble Abode ONLY. The
`GrassSpawner` (prefab plants/flowers) is out of scope for this pass.

## Problem

Grass blades float intermittently after a respawn / warm scene reload, then snap
to the ground after the player moves around. It is not location-specific and not
occlusion culling.

### Root cause (confirmed)

`InstancedGrassRenderer` **streams** grass cells within ~60 m of the player and
**raycasts each blade onto the planet's `"Terrain Mesh"` collider at runtime**,
then caches each placed cell permanently (only clearing it beyond ~1.2× spawn
radius). When the scene reloads/respawns, the procedural planet regenerates its
terrain; for a brief window the only collider present is the smooth
`BodyPlaceholder` sphere (no hills/dips). Any cell streamed during that window
raycasts onto the sphere, seats its blades at the wrong height, and freezes that
result. Walking away and back forces a re-stream against the now-real terrain —
hence "floats, then fixes after moving." This is the exact failure mode
`PlanetBakeTool.cs` already documents for terrain props.

## Goal

Grass that is seated correctly once, is identical every load, can be eyeballed
in the editor before shipping, and can **never** re-seat against the wrong
surface — without touching planet generation / atmosphere code (CLAUDE.md trap
\#2).

## Key insight

The procedural terrain is **deterministic** (same seed + settings → identical
mesh every load), and Humble Abode's runtime collider is its **LOD0** mesh,
which the editor preview reproduces identically. The terrain is never the
problem. Grass floats only because it **re-raycasts at runtime** and sometimes
hits the placeholder. If we bake the blade positions once (against the real
LOD0 surface) and stop raycasting at runtime, the float becomes structurally
impossible — and we never go near the forbidden generation/atmosphere code.

This also drops ~1,300 raycasts/sec of streaming work → a likely small FPS win.

## Design

Almost all of `InstancedGrassRenderer` is preserved — GPU instancing, per-cell
frustum culling, density fade, and the atmosphere depth-prepass command buffer.
**Only the source of blade positions changes:** baked lookup instead of runtime
raycast.

### Component 1 — Baked data file

`Assets/BakedPlanets/Humble_Abode_Grass.bytes` — a compact binary blob:

```
int   magic            // 'GRSB'
int   version
int   seed             // staleness check vs the renderer's seed
int   cellCount
repeat cellCount times:
  long    cellId        // SpawnerCubeface.EncodeCell(face,cu,cv) — same scheme as runtime
  float3  localAnchor   // body-local cell anchor (matches Cell.localAnchor)
  int     bladeCount
  repeat bladeCount times:
    int       meshIndex
    Matrix4x4 local     // 16 floats; body-local TRS, identical to runtime cell.local[k]
```

Full matrices (no decompose) for simplicity. Estimated ~200k–350k blades ≈
15–25 MB. Stored as a `.bytes` file (not a YAML ScriptableObject) to avoid text
serialization bloat; referenced at runtime via a `TextAsset` field.

Positions are **body-local**, so they ride orbit / spin / floating-origin
exactly as today (`l2w * local` in `Draw()`).

### Component 2 — Editor bake tool

Menu: `Tools ▸ Bake Planet ▸ Bake Grass (Selected Body)`.

1. Trigger the existing Planet Preview for the selected body (`PlanetPreviewTool`)
   so the real LOD0 `"Terrain Mesh"` exists in edit mode.
2. Ensure that `"Terrain Mesh"` has a `MeshCollider` (add a temporary one from its
   sharedMesh if absent — same LOD0 geometry as the runtime collider).
3. Call a `#if UNITY_EDITOR` bake method on `InstancedGrassRenderer` that iterates
   **the whole sphere** (all 6 faces, full cell range) and runs the **exact same
   placement logic as `BuildCell`** (single source of truth — baked == runtime
   intent), including patch/coverage, water/height band, slope, GrassBlocker, and
   exclusion-zone rejection.
4. Collect every non-empty cell and write the binary file. Log blade/cell counts.
5. Clean up the temp collider; leave the preview to the existing Clear flow.

Re-running re-bakes (one click) whenever grass density/terrain is retuned.

### Component 3 — Runtime change (surgical)

- Append a `public TextAsset bakedGrass;` field at the END of
  `InstancedGrassRenderer` (serialization-safe per conventions).
- `Awake`: if `bakedGrass != null`, parse it into a read-only
  `Dictionary<long, Cell> _bakedCells` and set `_baked = true`. Backward
  compatible — if unset, behaves exactly as today.
- `Stream()` in baked mode keeps the **windowing** (so `_active` stays small and
  culling/memory bounded) but replaces `BuildCell()` with a membership lookup:
  `if (_bakedCells.TryGetValue(id, out var c)) _active[id] = c;`. No raycasts.
- Baked cells are immutable/shared: in baked mode, removing a cell from `_active`
  does NOT clear or pool it (`ReturnCell`/`RentCell` are bypassed) so the shared
  baked data isn't corrupted.
- `Draw()` is unchanged.

### What is NOT touched

- No changes to `CelestialBodyGenerator`, atmosphere, shading, or any
  `Celestial/` code (trap \#2).
- `GrassSpawner` (plants/flowers) unchanged. If those also float, they get the
  same treatment in a follow-up.
- Git: per standing instruction, the user commits; this work leaves the tree
  dirty and does not commit/branch.

## Validation (performed by user via playtest)

1. Bake grass in editor; confirm grass looks seated in the Scene view.
2. Enter play; confirm grass is on the ground.
3. Reproduce the original bug path: orbit → die → respawn → look around. Grass
   must stay seated everywhere, every time.

## Risks & mitigations

- **Editor vs runtime mesh mismatch** — resolved: both are LOD0 from the same
  deterministic generator; verified in `CelestialBodyGenerator.cs`.
- **Exclusion zones not registered in edit mode** — verify `SpawnExclusionZone`
  population during implementation; water/slope/GrassBlocker rejection (the main
  ones) work in edit mode regardless.
- **File size in git** — ~20 MB binary. Acceptable; can gitignore + document a
  re-bake step if the user prefers (open question for the user).
