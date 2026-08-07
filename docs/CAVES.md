# Caves — generating and placing them

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
