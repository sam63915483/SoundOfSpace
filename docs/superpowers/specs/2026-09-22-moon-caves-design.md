🟢 ACTIVE — 2026-09-22 — three explorable caves on Constant Companion (the moon), replacing the tube

# Moon caves — design

**Sam's ask (2026-09-22):** remove the tube through Constant Companion and put three
normal caves on the moon instead. Different layouts, each with splitting tunnels
and caverns, none overlapping. The old Humble Abode cave "looked too smooth and
round and fake" — the point of this pass is rocky, cave-looking interiors in the
spirit of The Forest's caves. Sam judges which mouth and interior looks best.
The same brief is going to ChatGPT (Astra) in parallel.

## Why the moon works where Humble Abode failed

Constant Companion has **no atmosphere and no ocean** (`Shading.asset`:
`hasAtmosphere: 0`, `hasOcean: 0`). Both systems that broke the Humble Abode cave
do not exist there. Gravity inside a body already falls off linearly
(`Universe.GravityAcceleration`), so the bottom of a cave feels lighter, not
heavier. The horizon culler exempts anything that reaches into its body, and the
caves are not on its cluster-name list at all, so their renderers are never
switched off at any distance — which is what made the tube look like an empty
hole from Humble Abode (its 33 lights go off past 150 m; an unlit pipe with no
sun inside renders black).

## What gets built

### 1. The tube goes

`Constant Companion/Tunnel Rig` (tube mesh, `Tube Lights`, `Bore Guide`,
`TerrainHole - Mouth A/B`) is deleted from the scene by the installer (with Undo).
The moon keeps its `PlanetHolePuncher` (`CaveHoleBinder` adds one if missing).
`"Tunnel Rig"` stays in `PlanetOcclusionCuller.ClusterNames` — harmless dead entry.

### 2. Three caves, three faces, one installer

`Tools ▸ Cave ▸ Install Moon Caves` (`Editor/MoonCaveInstaller.cs`):

1. Finds the moon body, builds a **hidden LOD0 preview of the real terrain** the
   same way `TutorialGrassBake` does (temporary `CelestialBodyGenerator` with the
   moon's settings, `Update` ticked by reflection, `DontSave`, destroyed after).
2. For each of three fixed sites, samples the terrain into a **local heightmap**
   over the cave's footprint (vertical raycasts in cave-local space, 0.5 m grid,
   ±18 m). Local +Y is the moon's radial direction at the mouth.
3. Generates that site's cave with the heightmap as its ground, writes
   `Assets/1 - samsPrefabs/Cave/Moon/Cave_Moon_<X>.prefab` (+ mesh asset), and
   places/updates the instance under the moon.
4. Runs every check (below) and refuses to write anything that fails.

Sites (moon-local directions, chosen ~120° apart and >90° from the moon base,
which sits at local `(0.15, -0.98, 0.14)`):

| Cave | Direction | Layout |
|---|---|---|
| A — the Warren | `(0.94, 0.34, 0)` | ramp in, early fork; one arm loops to a low wide cavern, the other drops to a tall chamber with two side pockets; the loop rejoins |
| B — the Descent | `(-0.47, 0.34, 0.81)` | tall crack in, switchback ramps through three stacked caverns, side tunnels off each, dead-end crawl at the bottom |
| C — the Hall | `(-0.47, 0.34, -0.81)` | wide low mouth into one long broken hall with pillars and rubble, branches left and right into rooms, rear chamber |

Depth 25–35 m, 70–100 m of tunnel each. Descents ≤ 26° so everything is walkable
both ways (5.1 m/s² gravity; no drops that need a jump to return).

### 3. The rock (the real job)

All in `CaveSolid` (editor-only distance-field builder), driven by a `Style`
struct per cave so the three look different:

- **Faceted shading.** Vertices are split per triangle after meshing; flat
  normals. Low-poly rock like the rest of the world, not smooth blobs.
- **Finer, sharper noise.** Cell size 0.4 m. Ridged noise with two more octaves,
  plus **strata**: a height-banded ridge term that puts ledges and shelves on the
  walls instead of bulges. Noise is applied to the *walls*; the flattened floor
  gets a quarter of it so floors are walkable.
- **Non-round passages.** Elliptical cross-sections (wider than tall), floor
  flattening engaging fully up to 30° slopes (was only ~0.1 at 25°, so ramps were
  round pipes), a "broken ceiling" weight that roughens the upper half more.
- **Stalactites, stalagmites, columns, boulders and rubble built into the same
  solid** — cones/capsules/ellipsoids unioned into the rock at deterministic
  positions found by marching the void field to the wall. Boulders keep clear of
  the passage centre-line so the route stays open.
- **Three recipes.** A: angular strata, cool grey, ledges, scattered boulders.
  B: dripstone — many stalactites/stalagmites, a few columns, flowstone
  streaks, warm tan tint, darker floors. C: collapsed hall — big broken blocks in
  the floor and half-buried in walls, rubble piles, two floor-to-ceiling
  pillars, dark basalt grey.
- **Buried outer hull trimmed.** After the closure check passes on the full
  solid, triangles that are both far from any void (> 0.6 × wall thickness) and
  > 1 m under the terrain are dropped: never visible from anywhere reachable,
  and it halves the triangle count. The trim logs how many boundary edges it
  created and the shallowest one, so a mistake here is visible.
- **Material `Custom/CaveRock`** (`Assets/Shaders/CaveRock.shader`, Built-in
  surface shader, referenced by a real material asset so it survives build
  stripping). Object-space triplanar albedo + normal map (generated 512² rock
  albedo + normal from a cracks/fBm heightfield), vertex-colour tint (dust on
  floors, darker steep faces, style tint), and **sky exposure in vertex alpha**:
  the directional light and ambient are multiplied by exposure, point/spot
  lights are not. So the interior is black except your flashlight (E), the
  pocket lights and whatever daylight reaches in through the mouth.
- **Sky exposure** is computed per vertex at generation time by marching ~12
  upper-hemisphere rays through the sampled field grid (and the terrain
  heightmap), then smoothed over mesh neighbours. Cheap (array lookups), and it
  handles a skylight over a room correctly.

### 4. The mouths (three different)

The old rim torus is replaced by a **terrain-following skirt**: a 3 m slab of
rock whose top sits 0.3 m under the sampled terrain (0.05 m inside the hole
radius) out to ~14 m, so the rock meets the real ground everywhere and the cut
edge always has rock behind it. On top of the skirt, per cave:

- A: an **eyebrow** — a rock brow/mound over the uphill side so the entrance is a
  dark opening under an overhang, boulders around it.
- B: a **fissure** — a rock outcrop 3–4 m proud of the ground split by a tall
  narrow crack (≈2.4 m wide × 5 m high) you walk into.
- C: a **collapse** — wide low opening (≈8 × 3.5 m) at the foot of a low mound,
  a rubble field of boulders in front and one half-blocking the opening.

Each cave carries its own `TerrainHole` cylinder sized to its mouth (A r≈5.2,
B r≈4.6, C r≈6.2).

### 5. Lighting and contents

- Dark inside (exposure), flashlight is the main light. 3–4 dim warm point
  lights per cave in the biggest rooms (range ~10, intensity ~0.35, no shadows).
- `CaveCrystalSeeder` grows minable crystals on the walls (existing component,
  borrows the surface crystal prefab). ~60 per cave.
- Nothing else. No save state (static geometry; crystals already behave like the
  Humble Abode cave's).

### 6. Small runtime changes outside the generator

- `CaveVolume.affectsOcean` (new serialized bool, appended at the end, default
  true). The installer sets it false for moon caves. `CaveOceanCutout` skips
  volumes with it false — the moon has no ocean, and three caves would blow the
  32-capsule shader limit and spam the warning.
- Nothing in the forbidden zone is touched. The terrain preview is *used* via
  reflection exactly as `TutorialGrassBake` does.

## Checks (the generator refuses to write on any failure)

1. **Closed mesh** (0 boundary edges) and **positive signed volume** — on the
   full solid, before trimming.
2. **The hole sees rock.** Around each mouth at radius `HoleRadius + 0.4`, every
   5°, the highest rock is within 0.6 m of the terrain, and the void does not
   reach outside the hole. From any distance the punched hole shows rock, never
   the hollow moon.
3. **No overlap.** Every capsule of every cave, in moon space, is at least
   `2 × wall + 4 m` from every capsule of every other cave, and no cave reaches
   within 12 m of the moon's centre or within 15 m of the moon base bounds.
4. **Walkable.** From the mouth there is a route to every room using only
   segments with slope ≤ 27°.
5. **Budget.** Logged per cave: triangles after trim (target ≤ 120 k, one draw
   call each), generation time.

## Verification before Sam plays

- Headless compile (`py -3 prototypes/shuttle-computer/test/compile-unity.py`).
- Run the installer in the open Editor via coplay; read the checks in the log.
- Capture scene-view renders inside each cave (temporary light at the camera,
  try/finally) and at each mouth from outside; fix what one pass shows.
- Sam saves the scene. One playtest: walk into each cave, judge look, report.

## Out of scope

Save-aware crystals (known limit, unchanged), sound inside caves, enemies,
loot, any change to the Humble Abode cave (stays disabled), the moon base.
