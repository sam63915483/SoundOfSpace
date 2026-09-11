🟢 ACTIVE — finding logged 2026-09-11. Cause identified, fix designed, NOT built. Parked at Sam's call.

# The ocean vanishes at distance

**Symptom.** Fly away from Humble Abode and its ocean recedes from the coasts
inward, fully gone by 3–4 km, while the planet and its sky stay perfectly
visible. From another planet Humble Abode — "mostly oceans" — reads as a bare
terrain ball. Other planets do it too, less noticeably. Sam has seen it for
about a month; it is almost certainly older than that and was masked by the
old map / less time spent in orbit.

## Cause — verified, not guessed

The ocean is an **analytic sphere** drawn as a post-process. Water is drawn
where the terrain (from the depth buffer) is *farther* than the ocean surface:

```
oceanViewDepth = min(dstThroughOcean, sceneDepth - dstToOcean)
if (oceanViewDepth > 0) draw water
```

`dstToOcean` is exact. `sceneDepth` comes from a **24-bit depth buffer** whose
quantisation step grows with the square of distance. With the camera's near
plane at **0.1 m**:

| distance | depth step |
|---|---|
| 300 m | 5 cm |
| 1 km | 60 cm |
| 2 km | 2.4 m |
| 3 km | 5.4 m |
| 4 km | 9.5 m |

Once the step is larger than the local **water depth**, the seabed rounds to
"in front of" the surface and that pixel becomes land. Shallow water goes
first, deep basins last — the "recedes and shrinks" — and Humble Abode's seas
are a few metres deep, so it is gone by 3–4 km. **The numbers match the report
exactly**, and adding the step back as a tolerance made the ocean persist
(proof of mechanism) — but flooded the land too, see below.

### Things that were tried and are NOT it (all reverted)

- **Ocean opacity** (`alphaMultiplier` 70→140): the water was not fading.
- **Terrain LOD** (forced LOD0): still receded. The coarse meshes are honest
  sparse samples; the LOD bands (0.2 / 0.1 screen height) happen to coincide
  with the distances, which is what made it look like LOD.
- **Effect culling**: takes sky and sea together; the sky stays.
- **Cave cutout globals**: publishes zero capsules with the cave off.
- **A depth tolerance in `OceanEffect.shader`**: proves the cause, but it is
  *symmetric* — at range the depth buffer cannot tell 5 m of water from 5 m of
  land, so an uncapped tolerance drowned every planet into a smooth
  ocean-coloured ball, and a capped one (1.5% of radius) changed nothing
  visible. **No depth-based test can fix this**: at 10+ km the step is 60 m+
  and the depth buffer contains no information about which pixels are water.
  Shader reverted; forbidden zone is clean.

## The fix — a baked sea mask (not built)

Give the shader a signal that does not come from depth. The generator already
knows, for any direction from the planet centre, whether the terrain is below
sea level — it is how it heights the mesh.

1. **`CelestialBodyGenerator`** — at generation, run the existing height
   function over a 256×128 grid of equirectangular directions and bake
   `height < oceanRadius` into a one-byte-per-texel `Texture2D` (~32 KB per
   planet). ~30 lines. Regenerates at load like the meshes.
2. **`PlanetEffects`** — push it to each ocean material as `_SeaMask`, next to
   the parameters it already sets. ~5 lines.
3. **`OceanEffect.shader`** — `oceanSphereNormal` (already computed) → equirect
   UV → sample. Below ~1 km use the depth test exactly as now; beyond it, the
   mask decides "is this water?" and depth only shades the colour. Blend across
   the crossover so nothing pops. ~15 lines. Planets with no mask fall back to
   today's behaviour.

All three files are in the CLAUDE.md forbidden zone and need Sam's explicit go,
like the cave-cutout and under-water-sky exceptions did. Strictly additive.
Estimate: about an hour, done fresh.

**Optional small win, separate:** the camera near plane is 0.1 m, which is
unusually tight; 0.3 would triple depth precision (~6 km before recession
instead of ~3.5) at the risk of clipping anything held closer than 30 cm.
Not attempted — the held items attach at runtime and could not be verified
safe from the prefab.
