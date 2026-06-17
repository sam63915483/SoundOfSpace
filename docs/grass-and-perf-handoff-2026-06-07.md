# Grass overhaul + FPS optimization — handoff (2026-06-07)

Session goal: the game was no longer holding a solid 144 fps after a low-poly grass
pack was added. Driven by the Unity profiler (read live via the Coplay MCP), we
found the real costs, fixed a cluster of them, and substantially reworked the
grass system (look + seating + shading + culling). One issue is still **open**
(a view-direction FPS drop) and one fix is **applied but not yet user-verified**
(the grass see-through fix).

Related auto-memory (more detail, kept current):
- `fps-profiler-offenders.md` — every profiler-found offender + fix.
- `cartoongrass-system.md` — how the grass system works + every trap.

Hardware baseline (dev): Ryzen 3700X, RTX 3070, 32 GB. Target: solid 144 fps.

---

## TL;DR status

| Item | Status |
|---|---|
| Grass streaming CPU spikes (was 18 ms) | ✅ fixed & verified (≈0.8 ms) |
| `ControllerUINavigator` 4.7 ms scan | ✅ fixed & verified (0.88 ms) |
| `HALCommentator` 2.1 ms poll spike | ✅ fixed (throttled) |
| `Hotbar` / `EnemyController` FindObjectOfType | ✅ fixed |
| Concert lights/audience/stage culled by distance | ✅ fixed |
| Grass seating (collider matches visual) | ✅ fixed & verified (`hiRes=True`) |
| Grass patches / slope-conform / size variety / mini-patches | ✅ applied (look approved, mini-patches new) |
| Grass distance density-fade + near no-cull | ✅ applied |
| Grass per-patch colour variation | ✅ applied |
| Grass "see-through against sky" (depth pre-pass) | ⚠️ **applied, NOT yet user-verified** |
| **Concert/village-direction FPS drop (144→70-80)** | ❌ **OPEN — diagnosing** |

**The game is now GPU-bound**, not CPU-bound (profiler worst frames are dominated
by `WaitForTargetFPS` / `Gfx.WaitForPresentOnGfxThread`). Remaining wins are GPU
(overdraw / atmosphere / terrain), not scripts.

---

## How to profile (for whoever continues)

- Profiler reads come from the **Coplay Unity MCP**: `get_worst_cpu_frames`,
  `get_worst_gc_frames`. `get_unity_logs` reads the editor **and** a connected
  build's `Player.log` (build lines are tagged `WindowsPlayer ...`).
- These show CPU/GC in detail but **GPU only as "wait"** (`WaitForTargetFPS`,
  `Gfx.WaitForPresentOnGfxThread`). No per-shader GPU breakdown is available
  through this tool — GPU diagnosis is by reasoning + A/B view comparison.
- `Profiler.FlushMemoryCounters` (~2 ms/frame in captures) is **profiler
  overhead** — it does NOT exist in a normal build. Subtract it mentally.
- **Always test the real metric in a BUILD.** Editor play-mode is 2-3× slower
  and misleading.

---

## Performance fixes (all verified or low-risk)

1. **`InstancedGrassRenderer` streaming** (the big one). Two regressions found &
   fixed: (a) rejected cells were re-raycast every 0.1 s tick → an ~18 ms hitch
   when the height-band rejected lots of cells — fixed by storing every
   evaluated cell (incl. empty) so it's never re-evaluated; (b) per-cell
   allocation + 5 raycasts/cell when moving fast — fixed by **pooling `Cell`
   objects** and **one raycast per cell** (blades scattered on the hit's tangent
   plane). Now ≈0.8 ms.
2. **`ControllerUINavigator.cs`** — was running `FindObjectsOfType<Selectable>(true)`
   (walks every inactive object) + 2× `FindObjectsOfType<Canvas>` every 0.25 s,
   even with no menu open. Now **skips the Selectable scan when no modal is open
   and nothing is suppressed**, and reuses buffers (corners array, non-alloc
   `GetComponentsInParent`). 4.7 ms → 0.88 ms (verified).
3. **`HALCommentator.cs`** — `PollShipDust`/`PollShipOrbit` (iterate every ship +
   `GetComponentsInChildren<SpaceNet>`) moved off the 0.5 s poll onto a separate
   **1.5 s timer**. Kills the 2.1 ms spike frequency.
4. **`Hotbar.cs`** — `ResolveRefs` did 6× `FindObjectOfType(true)` every frame for
   equippables that may not exist yet. Now **0.5 s retry throttle**, skipped once
   all refs found.
5. **`EnemyController.cs`** — every enemy's `Start` did
   `FindObjectOfType<PlayerController>()`; a spawning wave batched into a ~19 ms
   delayed-Start hitch. Now a **shared `static _sharedPlayer`** (`ResolvePlayerTransform`).
6. **Concert** (`ConcertStageHub.cs`) — visuals now distance-gated via
   `ApplyStageVis(stageRoot, Off/GeometryOnly/Full)` keyed to `concertVisualRadius`
   (160 m): `Off` disables **all** renderers/lights/particles (a far stage no
   longer renders through the planet); `GeometryOnly` = static stage visible,
   lights off (close + day); `Full` = everything (close + night). Also gates the
   global **`ConcertLightProgram`** singleton (a ~2.2 ms/frame Update that runs
   regardless of distance and is NOT under any stage root). Audience
   (`AudienceMember`) was already self-culling renderers at 100 m.
7. **`OxygenManager` / `PlayerController` FixedUpdate FindObjectsOfType** — checked,
   already throttled; left alone.

---

## Grass system rework (`InstancedGrassRenderer` + CartoonGrass shaders/material)

The grass is CPU-driven `Graphics.DrawMeshInstanced` on the procedural planet
"Humble Abode" only (`onlyBodyName`). Component lives on
`--- Managers ---/GrassSpawner`. See `cartoongrass-system.md` for the full
architecture/history.

What changed this session:
- **Water-relative height band** — `maxHeightAboveWater` caps grass elevation
  above sea level (keeps it off cliffs). Currently 15.
- **Slope shaping** — blades tilt toward the surface normal (`slopeConform`) so
  they hug hillsides instead of standing radially ("one side floating" fix); and
  get **sparser + smaller** on slopes (`slopeBladeFloor`, `slopeScaleFloor`).
- **Organic patches** — `Mathf.PerlinNoise` clumps grass into patches with bare
  gaps (`patchCells`, `patchDensity`) instead of a uniform carpet, PLUS a second
  **mini infill-patch octave** (`miniPatchCells`, `miniPatchDensity`) to fill the
  gaps with small dense clumps.
- **Size variety** — `minScale` lowered (smaller blades mixed in).
- **Distance density-fade** — `densityFade` draws fewer blades per cell with
  distance and fills back in as you approach (also cuts GPU overdraw);
  `densityFadeStartFrac`, `densityFadeFloor` (floor so distant grass never thins
  to see-through).
- **Near no-cull** — `noCullRadius` (15 m): cells near the player are never
  frustum-culled (fixes near grass vanishing walking up a slope — the cull box
  sat at the sphere surface, below hilltop blades); the cull box was also enlarged
  by `maxHeightAboveWater`.
- **Per-patch colour variation** — `CG_SimpleGrass` hashes a planet-relative cell
  index for subtle per-patch brightness (`_ColorVarScale`, `_ColorVarAmount`);
  the renderer sets a global `_GrassPlanetCenter` each frame (keeps the hash
  input small at the planet's ~±24000 world coords).

### Seating fix (grass/props float or clip) — `CelestialBodyGenerator.cs` ⚠ PROTECTED ZONE
Root cause of the long-standing "everything random-spawned floats/clips": the
collision mesh was resolution 100 with NO `perturbVertices`, but the **visual**
LOD0 mesh is resolution 300 + perturb — so raycasts hit a smoother/coarser
surface than you see. Fix (USER-AUTHORIZED edit to the protected planet-generation
code, per CLAUDE.md trap #2): **large walkable bodies reuse their detailed LOD0
mesh as the MeshCollider** (`worldRadius = lodMeshes[0].bounds.extents.x *
transform.lossyScale.x >= HighResColliderMinRadius` = 150 — Humble Abode ~300
qualifies, moons don't, so no system-wide memory hit). Confirmed working via a
gen-time `Debug.Log` → `Humble Abode ... worldRadius=300.5m, hiRes=True`.
Residual sub-foot float handled by smaller `cellSize` (1.5, so blades stay inside
one surface triangle) + a slight downward `surfaceLift` embed (-0.05).
**That gen-time Debug.Log is still in the code — consider gating it behind
`logTimers` or removing it.**

### See-through fix (blades transparent only when sky is behind them) ⚠ NOT VERIFIED
Symptom: grass silhouetted against the sky on a hilltop washes cyan / looks
see-through; against the ground it's fine. Cause: `Atmosphere.shader` (screen-space,
`[ImageEffectOpaque]`) reads `_CameraDepthTexture`, but **`DrawMeshInstanced`
grass is not in Unity's depth prepass**, so for sky-backed blades it uses the
sky's (far) depth → full in-scatter painted over the blade.

Dead ends tried (do not repeat):
- `ShadowCastingMode.On` — did NOT add grass to `_CameraDepthTexture`, only added
  shadow-map cascades + per-blade shadows + big FPS cost. Reverted (keep OFF).
- Material render queue 2501 (past the atmosphere's 2500 cutoff) — stopped the
  wash BUT the 2500 cutoff is ALSO the shadow-receiving boundary, so it killed
  cabin/tree shadow darkening AND atmosphere day/night. Reverted.

**Final fix (applied):** grass stays **opaque** (queue −1/Geometry → keeps
shadows + atmosphere day/night) and a **depth-only pre-pass** writes the visible
grass into `_CameraDepthTexture`. `InstancedGrassRenderer` mirrors its colour
`DrawMeshInstanced` batches into a `CommandBuffer` at `CameraEvent.AfterDepthTexture`
using the new shader **`CartoonGrass/GrassDepth`** (`ColorMask 0`, `ZWrite On`).
One position-only pass (~0.3 ms). **This is the standard technique but I could not
run it — needs verification.** If the see-through persists, check whether the
command buffer is actually landing in the depth texture on this Unity version
(2022.3 Built-in); if not, the fallback options are documented above (queue 2501 +
a manual sun-direction day/night term, accepting no cast shadows).

---

## OPEN ISSUE: FPS drop looking toward the concert/village (144 → 70-80)

What we know (A/B profiler capture, looking away vs toward):
- **CPU work is identical in both directions** (grass, UI, scripts all the same).
- The extra ~6 ms appears only in the **rendering/GPU** portion → it's GPU,
  view-dependent.
- It is **NOT the concert lights/audience**: user confirmed no lights/lasers
  visible, audience self-culls at 100 m, and the concert is ~460 m away
  (100° around a ~300 m-radius planet, fully below the ~35 m horizon).
- Reproduces from space too: 300 m up, looking at empty space = 144 fps; looking
  down at the planet where the stage is = 70-80 fps.

Current hypotheses (unresolved):
1. The static **stage geometry** still rendering through the planet — addressed by
   `ApplyStageVis.Off` (all renderers off when far); **not yet confirmed it fires.**
2. The **village** (buildings/NPCs) in that direction.
3. **Terrain overdraw / atmosphere** at that part of the planet (the concert is on
   the night side; the atmosphere terminator/limb is the most expensive view).

Diagnostic in place: `ConcertStageHub.UpdateStageVisuals` logs
`[ConcertStageHub] stage '<name>' vis -> Off/Full/GeometryOnly (viewer Nm away)`
on every state change. **Next step:** rebuild, look at the concert from across the
planet, then read `Player.log` via the MCP — if it logged `vis -> Off` and it
STILL drops, the concert is exonerated and the cost is the village/terrain/atmosphere
in that direction (investigate those next; village objects, then consider a
camera far-clip / distance fog, then — only with the user's OK, it's protected —
the atmosphere raymarch sample count in `AtmosphereSettings`).
**Remove the temporary Debug.Log once this is solved.**

---

## Files changed this session

- `Assets/3 - Scripts/World/InstancedGrassRenderer.cs` — heavy: streaming rewrite,
  pooling, 1-raycast/cell, slope/patch/mini-patch/size logic, density-fade,
  no-cull, `_GrassPlanetCenter` global, depth pre-pass command buffer.
- `Assets/CartoonGrass/Shaders/CG_SimpleGrass.shader` — `GrassWrap` lighting
  (shadow-receiving), per-patch colour variation, `_AmbientBoost` ×0.06.
- `Assets/CartoonGrass/Shaders/CG_GrassDepth.shader` — **NEW** depth-only shader.
- `Assets/CartoonGrass/CG_GameGrass.mat` — `_AmbientBoost` 0; `_ColorVarScale` 6,
  `_ColorVarAmount` 0.12; render queue back to default (−1).
- `Assets/3 - Scripts/UI/ControllerUINavigator.cs`
- `Assets/3 - Scripts/AI/HALCommentator.cs`
- `Assets/3 - Scripts/UI/Hotbar.cs`
- `Assets/3 - Scripts/Combat/EnemyController.cs`
- `Assets/3 - Scripts/Concert/ConcertStageHub.cs` — `ApplyStageVis`,
  ConcertLightProgram gate, temp diagnostic log.
- `Assets/3 - Scripts/Scripts/Celestial/CelestialBodyGenerator.cs` — **PROTECTED
  ZONE, user-authorized** collider gate + temp gen-time log.

Remember: new `.cs`/`.shader` files need `git add` (their `.meta` too) — `git
commit -a` skips untracked files.

---

## Current InstancedGrassRenderer values (on `--- Managers ---/GrassSpawner`)

Set live via the MCP. **These live only in the open scene until you SAVE it
(Ctrl+S).** New serialized fields otherwise fall back to their code defaults.

```
spawnRadius          60      cellSize             1.5     cellCoverage        1.0
bladesPerCell        6       minScale             0.77    maxScale            2.0 (unchanged)
maxSurfaceAngle      30      waterMargin          1.0     surfaceLift         -0.05
maxHeightAboveWater  15      maxCellsPerUpdate    200     receiveShadows      true
frustumCull          true    noCullRadius         15
patchCells           12      patchDensity         0.35
miniPatchCells       3       miniPatchDensity     0.2
slopeBladeFloor      0.35    slopeScaleFloor      0.7     slopeConform        0.8
densityFade          true    densityFadeStartFrac 0.45    densityFadeFloor    0.6
```
Material `CG_GameGrass`: `_AmbientBoost 0`, `_ColorVarScale 6`, `_ColorVarAmount 0.12`.
All are Inspector-tunable with tooltips. Quick knobs: `patchDensity` ↓ = clearer
gaps; `bladesPerCell` = thickness; `miniPatchDensity` = gap fill; `densityFadeFloor`
= how solid distant grass stays.

---

## Gotchas / lessons (so the next session doesn't relearn them)

- **MCP `set_property` in PLAY MODE does NOT persist** — Unity reverts runtime
  changes on exit. Always `stop_game`, set in edit mode, then user saves the scene.
- **Built-in pipeline has no occlusion culling** — far-side geometry renders
  through the planet unless distance-culled by script.
- **Queue 2500 is a double boundary**: the atmosphere `[ImageEffectOpaque]` cutoff
  AND the directional-shadow-receiving boundary. You can't be "past the
  atmosphere" and "receive shadows" at once.
- **`DrawMeshInstanced` geometry is excluded from `_CameraDepthTexture`** (the
  depth prepass) — and `ShadowCastingMode.On` does not fix that.
- **Atmosphere/terrain/celestial generation + shaders are a PROTECTED zone**
  (CLAUDE.md trap #2). The `CelestialBodyGenerator` collider change was made only
  after explicit user authorization.
- Verify in a **build**, not editor play-mode.

---

## Suggested next steps

1. **Verify the grass see-through fix** (depth pre-pass) in a build: hilltop
   blades against the sky solid? shadows + dark-side still darken? FPS unaffected?
2. **Settle the concert-direction drop**: rebuild, look at it from across the
   planet, read `Player.log` for the `ApplyStageVis ... vis -> Off` line. Then
   chase the real culprit (village objects → distance fog/far-clip → atmosphere
   quality with user OK).
3. **Clean up temp logs**: the `ConcertStageHub` vis log and the
   `CelestialBodyGenerator` collider log.
4. If still short of 144 once the above is settled, the remaining lever is GPU:
   grass overdraw (two-sided `Cull Off` blades), then atmosphere/terrain.
