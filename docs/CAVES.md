# Caves — generating and placing them

> **2026-09-22 — THE MOON CAVES are the live version of this system.** The
> Humble Abode cave below is the first-generation prototype (disabled in the
> scene, Sam does not want it back). Everything from "The cave is ONE SOLID"
> onward still applies, but the current generator is `CaveSolid.Build(Layout)`
> driven by `Editor/MoonCaveInstaller.cs`.

## Moon caves (Constant Companion) — `Tools ▸ Cave ▸ Install Moon Caves`

Three explorable caves on the moon, replacing the tube that ran through it.
Spec: `docs/superpowers/specs/2026-09-22-moon-caves-design.md`.

| Cave | Prefab | Recipe | Mouth | Layout |
|---|---|---|---|---|
| A — the Warren | `Cave/Moon/Cave_Moon_A` | Strata (angular ledges, boulders) | eyebrow overhang | ramp, early fork, loop through a low wide cavern to a tall chamber, pockets, lower level |
| B — the Descent | `Cave_Moon_B` | Dripstone (stalactites, columns, flowstone streaks) | tall fissure in a big outcrop | switchbacks through three stacked caverns, side tunnels, a crawl at the bottom |
| C — the Hall | `Cave_Moon_C` | Collapse (broken blocks, rubble, pillars) | wide low collapse | one long hall, branches left and right, rear chamber, deep room |

**Why the moon works where Humble Abode failed:** no atmosphere, no ocean.
Gravity already falls off linearly inside a body. The horizon culler exempts
anything reaching into its body and never touches the caves (they are not on
its cluster-name list), so they are never switched off at a distance — which
is what made the tube read as an empty hole from Humble Abode (its 33 lights
went off past 150 m; an unlit pipe with no sun inside renders black).

**How the installer works.** It deletes `Tunnel Rig`, builds a hidden LOD0
preview of the moon's REAL terrain (TutorialGrassBake's recipe — a DontSave
generator ticked by reflection), and for each site samples the terrain into a
cave-local heightmap (±45 m at 0.5 m). The cave is generated *against that
ground*: a rock slab follows the terrain 0.3 m under it around the mouth, the
outcrop stands on it, and the passage enters through the outcrop. Re-run after
changing a layout; assets are patched in place and instances keep their links.

**Coordinates.** Layouts are authored as (x lateral, d depth below the mouth
sphere, s arc along the tunnel). The moon is only ~51 m in radius, so at 20 m
down the sphere a tunnel runs on is 30 m in radius — the same arc is 40%
shorter and the same drop far steeper. Depths beyond 8 m are scaled by 0.55 for
that reason (`DepthScale`); the installer measures the real slopes. "Up" is the
local radial direction everywhere (floors, cross-sections, strata, exposure).

**Round 2 (same day, after Sam's first playtest):** the outcrops are gone —
each mouth is a flush sinkhole and the slab under the ground roofs the ramp;
the layouts are GROWN networks (`Grow` in the installer: random walk from the
entrance, branches, loops, rooms, confined to the cave's own 120° wedge with a
depth-aware margin; growth rules were tuned in a Python mirror first,
`scratchpad/growsim.py` pattern); the material is MOON ROCK — the moon's own
two flat colours, steep colour and both normal maps (Craters.tif / Rock1.jpg)
with MoonA.shader's steepness rule, vertex G = steepness; no placed lights;
floors clear of small spikes; cave crystals fixed (the prefab is authored at
scale 17 — the seeder now multiplies by it like CrystalSpawner does, and adds
the convex MeshCollider so they can be mined).

**The rock look** (`CaveSolid.Style`, one preset per recipe): faceted shading
(vertices split per triangle), ridged + domain-warped noise with depth-banded
STRATA for ledges, elliptical wider-than-tall passages, floors that flatten up
to 30° of slope, stalactites / stalagmites / columns / boulders / blocks /
rubble built into the same solid, and a triplanar rock material
(`Custom/CaveRock`, `Assets/Shaders/CaveRock.shader`) with procedural albedo +
normal per recipe (`CaveRockTextures`).

**Dark inside.** Each vertex stores SKY EXPOSURE in its colour alpha (rays
marched through the sampled field). The shader multiplies the directional light
and ambient by it; point/spot lights (flashlight on **E**, the pocket lights in
the big rooms) are not scaled. The mouth is lit by real sun, the interior is
black without a light. `_ExposureFloor` on the material is the minimum.

**Checks — the installer refuses to write on any failure:**
1. closed mesh (0 boundary edges), positive signed volume;
2. *the hole sees rock*: at 72 angles around the cut, the highest rock is within 0.6 m of the terrain;
3. *every passage has a roof*: marching up from each roof finds rock within 3.5 m (catches the terrain clip and an outcrop too small to cover the entrance);
4. walkable: every room reachable from the mouth over legs ≤ 27°;
5. no overlap: ≥ 4 m of rock between caves, ≥ 12 m from the core, ≥ 15 m from the moon base;
6. the buried-hull trim (halves the triangle count) created no open edge shallower than 0.8 m.

**Things learned building it (each cost a run):**
- Every void point that meets the terrain must be inside the `TerrainHole`. The
  terrain sheet outside it is invisible from below but SOLID to physics and
  visible from above. The installer sizes the hole from the actual crossing
  points (`ComputeHole`), which is why the moon holes are ~9-10 m in radius.
- The outcrop is not decoration — it is the roof of the entrance ramp until the
  passage is deep enough. Too small an outcrop = open sky over the ramp.
- Roof probes at fixed offsets above a passage false-fail: the wall noise moves
  the rock band by up to a metre, and a tunnel sample inside a big room has the
  room's roof 9 m up. March for rock instead, and skip samples inside rooms.
- Non-manifold edges (stalactite tips pinching at grid resolution) are not
  holes; count only edges used once.
- Walls are 3.0 m thick (2.2 thinned to nothing under roof noise at one room).

**Runtime pieces reused as-is:** `CaveHoleBinder` (adds the puncher),
`PlanetHolePuncher` (cuts all LODs + colliders), `CaveVolume` (capsules; new
`affectsOcean = false` so the 32-capsule ocean cutout ignores the moon caves),
`CaveCrystalSeeder` (minable crystals on the walls), `NoGrassVolume`.

---

## The original Humble Abode cave (prototype, disabled)


**Yes, this works.** `PlanetHolePuncher` already proved it on the moon tunnel;
a cave is the same trick with only one mouth instead of two.

## Why it's possible at all

The planets are heightfields — `CelestialBodyGenerator` pushes each icosphere
vertex out by a single radius per direction. That topology **cannot express an
overhang**, so a cave can never come out of the generator. It has to be cut
afterwards, which is exactly what `PlanetHolePuncher` does: it rebuilds the
triangle index list and drops every triangle with a vertex inside a `TerrainHole`
volume, across all 3 LODs, the collision mesh, and the BodyPlaceholder collider.
The vertex array is untouched, so normals, tangents and the generator's packed
UV0 shading data all stay valid — the surviving terrain shades identically.

Underneath the shell there is simply empty space, and gravity down there is
handled by `Universe.GravityAcceleration`. So a cave is: cut a hole, put a mesh
under it, hide the seam.

## The generator

`Tools ▸ Cave ▸ Generate Cave Prefab` (`Assets/3 - Scripts/Editor/CaveGenerator.cs`)

Writes to `Assets/1 - samsPrefabs/Cave/`:

| Asset | What it is |
|---|---|
| `Cave_01.prefab` | The thing you place |
| `Cave_Interior.asset` | Tunnel + chamber, 861 verts / 1700 tris, inward-facing |
| `Cave_Mouth.asset` | The rock collar that hides the cut, 240 verts / 400 tris |
| `Cave_Rock.mat` + `Cave_Rock_Albedo.asset` | Standard-shader rock, generated tileable texture |

Prefab contents:

```
Cave_01                        CaveHoleBinder, GrassBlocker,
│                              NoGrassVolume (r 11.5), CaveVolume (43 bore points)
├─ Cave_Interior               the whole shell — rim AND tunnel, one mesh.
│                              layer Body, concave MeshCollider
├─ TerrainHole - Cave Mouth    Cylinder marker, ⌀10.4 × 12 deep
├─ CaveLight_Throat            faint fill light
└─ CaveLight_Chamber           faint fill light
```

## The cave is ONE SOLID with thick walls

`CaveSolid.cs` builds it from a distance field: the walkable void (capsules
along the tunnels, spheres for rooms), a rock envelope 2.2 m thick around it, an
entrance rim, and Surface Nets to polygonise the result into a single closed,
watertight mesh.

**This replaced a swept thin-shell generator, and the reason matters.** The old
one built a rim piece and a tunnel piece that had to meet exactly. Every bug it
ever produced was the same one wearing a different hat: a strip whose single
visible face pointed the wrong way, so you looked straight through solid-looking
rock into the hollow planet. Making one strip double-sided just moved the hole.
A solid cannot have that bug — the mesh is the *boundary of a volume*, so every
face is visible from the side you actually see it from, and there are no separate
pieces to line up. Branches and rooms come free: a side passage is one more
capsule in a `min()`.

Every generation self-checks and **refuses to write a broken cave**: zero
boundary edges (an open edge is literally a hole), positive signed volume
(outward-facing), and it reports where any defect is.

### Things that WILL bite you when editing the layout

All of these were hit while building it, and all produce the same symptom — the
self-check reporting boundary edges:

- **Nothing may be thinner than a grid cell.** Surface Nets holds one vertex per
  cell and cannot represent two sheets in one. Every failure was a sub-cell
  feature: a wedge of rock between two passages diverging slowly, an air gap
  between two shells, a rim tapering to a feather edge.
- **Passages must leave sharply,** not drift apart. Keep ~8 m between anything
  running parallel. `BlendRadius` (2.8 m) fillets the junctions so the wedge is
  never sub-cell — it must stay comfortably above `CellSize`.
- **Nothing may graze anything at a shallow angle.** Two surfaces crossing
  near-tangentially leave a razor sliver. The entrance is cut with a horizontal
  plane at ground level for exactly this reason — it crosses the tunnel's dome
  transversally. A punched cylinder, tried twice, slices a ring off instead.
- **A solid seals itself.** The envelope wraps the void's top cap too, so the
  entrance gets a dome of rock over it unless the rock is actively cut away.
  That's not a bug in the field — it's what "solid" means.

### Two rules the OLD thin-shell mouth had to obey (kept for reference)

Both were learned by shipping them broken. Both are now checked, not assumed.

**1. The mouth must be double-sided all the way through the lip-to-bore funnel.**
A swept funnel that narrows monotonically has ONE visible side, and it comes out
facing *down and away* across the whole mouth (measured: `dot(up) = -0.87`). Those
strips then render as nothing from outside and you look straight past the rock
into the hollow planet — which reads as "the tube doesn't line up with the mouth".
The geometry is fine; it's the facing. `AddRimBackFaces` duplicates every mouth
strip with reversed winding, derived from the triangles actually present (not an
assumed order — `FaceInward` may have flipped them first).

**2. The rock must reach ground level OUTSIDE the cut radius.**
If the funnel crosses y=0 *inside* the hole, there's an open ring between the rock
and the terrain's cut edge. With the lip outside the cut, the geometry inverts the
way you want: inside the cut the rock hangs *below* the terrain, backing the cut
edge instead of opening into the void. `WarnIfMouthLeaks()` measures the finished
mesh and logs an error if you break this while retuning. Currently: rock reaches
ground level at radius **6.23** against a **5.20** cut — 1 m of overlap.

**Verify by measuring, never by eye.** Both of these look plausible in a render
from the wrong angle, and I reverted rule 1 once on the strength of a
misread screenshot.

**The rim and the tunnel are ONE mesh.** They started as two, and the seam where
their independent noise didn't agree was a visible gap between the rock mouth and
the bore. Now a single sweep starts as a lathe profile out at the buried skirt,
climbs over the crest, drops to the lip, and just keeps going as the tunnel —
same ring basis, same noise field, one continuous triangle strip. The bore's
first two control points must stay vertically stacked for this to hold.

Shape: entrance flares to 3.6 m, throat narrows to 3.2 m, bends twice over ~38 m
and opens into a 7 m chamber with a flat back wall. The bore's cross-section is
flattened along its lower edge wherever the tunnel runs horizontally, so you walk
on a floor instead of skidding round the bottom of a pipe.

## Placing one

1. Drag `Cave_01` onto the **`Humble Abode`** transform in the hierarchy (it must
   be a child of the `CelestialBody`, or the hole marker won't be found).
2. Move it to where you want the entrance, on the surface.
3. Point its **local +Y away from the planet's centre** (that's "up" out of the
   ground). Spin it around Y to choose which way the tunnel runs.
4. Press Play. `CaveHoleBinder` adds a `PlanetHolePuncher` to Humble Abode
   automatically the first time — the planet doesn't have one yet, and forgetting
   it is a silent failure (cave present, correctly placed, completely sealed).

The hole is re-cut from the marker's current position on **every** Play, so
moving the cave needs nothing redone.

## How the seam is hidden

This is the part that decides whether it looks seamless:

- The `TerrainHole` cylinder cuts a circle of radius **5.2 m**.
- `PlanetHolePuncher.snapRimToHole` (on by default) pulls the ragged
  triangle-edge cut onto that exact circle, so the opening is a clean disc.
- The collar's **inner** radius is 4.2 m — *smaller* than the cut — so it
  overlaps the edge from inside.
- Its **outer** radius is 9.5 m and its outer band sits *below* ground level,
  so it overlaps from outside and buries the transition underground.

If a seam shows after placing it, the fix is almost always the collar being too
small for locally steep terrain: raise `CollarOuter` / `CollarSkirt` in
`CaveGenerator.cs` and re-run the menu item.

## Tuning

Every number is a `const` at the top of `CaveGenerator.cs` — path control
points, radius profile, wall noise, floor flattening, collar dimensions. Change
and re-run; the prefab is overwritten in place, so anything already in the scene
picks up the new mesh.

The wall roughness uses Perlin, not `Random`, so re-running produces the
identical cave rather than a new one each time.

## Grass and water

Two things a cave on this planet runs into, both fixed, both worth knowing about
if you place another one.

**Grass floated over the mouth.** Humble Abode's grass renderer has
`bakedGrass = Humble_Abode_Grass` assigned, which means it uses *frozen*
positions and does no raycasting at all — so a hole punched after the bake is
invisible to it and its blades hang in mid-air. `InstancedGrassRenderer` now
drops baked blades that fall inside a `NoGrassVolume` or any `TerrainHole`, once,
when the blob loads. **No re-bake needed**, and it re-runs if you add another
cave later. The `NoGrassVolume` is wider than the hole on purpose — the rim sits
*on* the ground, so blades left there poke through the rock.

**The cave filled with water.** The ocean is a single trigger sphere the size of
the planet — on Humble Abode radius 200 *is* sea level — so any cave that
descends below it puts you inside the water volume. The placed cave's mouth is at
r≈202 and its deepest point at r≈189, i.e. mostly underwater.

> **`PlanetEffects` is a ScriptableObject ASSET, not a scene component.**
> `FindObjectsOfType<PlanetEffects>()` returns **zero** — the first version of the
> ocean suppression used it, found nothing, and silently did nothing at all, with
> no error to notice. Use `Resources.FindObjectsOfTypeAll<PlanetEffects>()`.
> Because it's a shared asset, a session that ends underground writes
> `displayOceans = false` to disk and every planet loses its ocean, so
> `CaveVolume.SelfHealOceans()` restores it on enable when nothing is suppressing.

`CaveVolume` stores the bore as a capsule chain and fixes both halves:

- `PlayerController` now routes every water read through `InWaterVolume`, which
  is false inside a cave — so you walk instead of swimming, and the swim audio,
  footsteps and landing sounds all agree.
- `PlanetEffects.displayOceans` is switched off while the camera is inside, then
  restored. The ocean post-process draws its full-screen underwater material
  whenever the camera is nearer the centre than the ocean radius, *regardless of
  what's in front of it* — so it flooded the cave visually for the same reason.
  The suppression is reference-counted across caves, and released on disable, so
  it can't strand the world with its oceans switched off.

Note the ocean will still pop on/off if you stand exactly in the mouth looking
out at the sea. Set `suppressOcean = false` on the CaveVolume if you ever *want*
a flooded cave.

## Known limits

- **One mouth.** A second entrance means a second `TerrainHole` marker and a
  branch in the path.
- **Lighting is a placeholder.** Two faint point lights so the first test isn't
  a black rectangle. Decide whether caves are flashlight-only (player has one on
  **E**) or lit with placed torches, then delete them.
- **Not save-aware.** It's static scene geometry, like the moon tunnel — nothing
  to save. Things you *place* inside it save normally.
- **No LODs.** 2100 triangles total, so it doesn't need them yet.
