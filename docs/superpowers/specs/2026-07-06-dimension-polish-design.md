# Dimension Polish Pass — 2026-07-06 (branch `feat/dimension-polish`)

Goal: take all 19 black-hole observation dimensions (D1–D8 main chain + 11
keepers) from "rough demo" to "close to finished" in one day. Three axes,
approved by the user (photoreal-mundane liminal style, all 19 in scope):

1. **More observation-mechanic content** — props that rearrange/creep/vanish
   while unobserved, in every dimension (e.g. D1 gets full living-room sets per
   maze cell that reroll with the walls).
2. **Textures instead of flat colors** — Coplay-generated tileable photoreal
   surfaces, per dimension.
3. **Atmosphere** — real generated ambience loops replacing sine placeholders,
   one-shot stingers when the world shifts behind you, lighting/fog tuning.

## Shared infrastructure (built first, everything hangs off it)

### `DimensionAssetLibrary` (ScriptableObject)
- Asset at `Assets/2 - Materials/Dimensions/DimensionAssetLibrary.asset`,
  registered in PlayerSettings **Preloaded Assets** (build-safety; OnEnable
  sets a static instance in builds; `#if UNITY_EDITOR` falls back to
  `AssetDatabase.LoadAssetAtPath` at the fixed path).
- Key→Texture2D and key→AudioClip lists (JsonUtility-friendly, no dicts in
  serialized form; runtime lazy Dictionary cache).
- **Null-safe static API — the whole pass degrades gracefully to today's flat
  colors/sine tones if an asset is missing:**
  - `DimensionAssetLibrary.Tex(string key)` → Texture2D or null
  - `DimensionAssetLibrary.Clip(string key)` → AudioClip or null

### `DimensionSceneUtil` additions
- `TexMat(string texKey, Color tint, Vector2 tiling, float smoothness = 0.1f)`
  — Standard mat; texture applied only if the key resolves, else flat tint.
- `EmissiveTexMat(string texKey, Color tint, float emission)` — stained
  glass / neon / CRT screens.
- `AmbienceClip(string key, float fallbackHz, float fallbackVol)` — library
  clip or `ToneClip` fallback.
- `PlayOneShot3D(string key, Vector3 pos, float volume = 1f, float maxDist = 25f)`
  — temp GO + AudioSource, self-destroys; silent no-op if key missing.

### `DimensionPropKit` (static builders)
Primitive-composed props, human scale, layer **Body**, parented, materials
passed in: `Couch, Armchair, CoffeeTable, DiningTable, ChairSimple, FloorLamp
(optional point light), Painting, Shelf, Desk, CrtMonitor, Mug, BookStack,
Bench, PottedPlant, Crate, WaterCooler, Candlestick, WallClock (settable
time)`. Controllers compose anything more specific from primitives directly.

### `PropShuffleSet` (MonoBehaviour, the new observation toy)
For bounded scenes: created via
`PropShuffleSet.Create(name, zoneBounds, shuffleSfxKey, options)`, then
`AddAnchor(position, yaw)` × N and `AddProp(gameObject)` × M (M ≤ N).
Own `ObservationTracker` on the zone; on `justLost`:
- random reassignment of props → anchors (small jitter in pos/yaw),
- optional **creep modes**: `facePlayer` (props rotate to face the camera),
  `countJitter` (occasionally one prop more/fewer than last time),
- plays its one-shot **behind the player** (drag/scrape) via PlayOneShot3D.
D1's infinite maze doesn't use this — its furniture is built per-cell inside
the existing cell reroll (same lifecycle as the walls, pooled).

## Asset key manifest (the contract)

Textures → `Assets/2 - Materials/Dimensions/Textures/<key>.png` (tileable,
1K, photoreal-mundane, top-down/orthogonal surface shots). Key = filename.

Shared: `wood_worn`, `fabric_couch`, `wood_parquet`, `concrete`, `grass_dry`,
`grass_lush`, `burlap`.
Per-dimension: `d1_wall` (aged floral wallpaper), `d1_floor` (worn beige
carpet), `d1_ceiling` (popcorn ceiling); `d2_sand` (rippled desert sand);
`d3_carpet` (dark red hotel runner), `d3_wall` (hotel wallpaper), `d3_door`
(dark wood door w/ grain, full door face, not tiled); `d4_stone` (carved
monolith stone); `d5_books` (packed book spines), `d5_wall` (dark library
wood paneling); `d6_ice` (cracked sea ice, dark beneath); `d7_wall`
(victorian deep-green wallpaper); `d8_stone` (mossy cobble), `d8_statue`
(weathered carved stone); `d9_bark` (red-tinged bark), `d9_ground` (leaf
litter); `d11_metal` (server rack face w/ vents), `d11_floor` (raised
data-center tile); `d12_rock` (dark wet rock), `d12_planks` (dock planks);
`d13_bark` (orchard bark); `d15_wood` (dark pew wood), `d15_stone` (church
stone), `d15_glass1`/`d15_glass2` (stained-glass panels, not tiled);
`d16_asphalt` (wet night asphalt), `d16_neon1`/`d16_neon2`/`d16_neon3`
(glowing sign faces, not tiled); `d18_corn` (dry cornstalk mass), `d18_dirt`
(dry field dirt); `d22_rust` (rusted hull plate), `d22_container` (rusted
container side); `d23_wheat` (dense wheat stalks, for the stalk cards);
`d24_carpet` (grey-blue office carpet tile), `d24_ceiling` (acoustic ceiling
tile), `d24_wall` (off-white office drywall); `d25_wax` (melted wax pools
over dark stone), `d25_stone` (dark shrine stone).

Audio → `Assets/Audio/Dimensions/<key>.wav` (ambience `loop=true`, ~20–25 s;
stingers one-shot). Music (mp3) same folder.
Ambience: `amb_d1` … `amb_d25` (one per dimension; see prompts in generation
task — each matches the dimension's mood).
Stingers: `sfx_furniture_drag`, `sfx_door_slam_distant`, `sfx_wood_creak`,
`sfx_paper_shuffle`, `sfx_chair_scrape`, `sfx_whisper`, `sfx_clock_tick`
(loopable), `sfx_phone_ring`, `sfx_tv_muffled` (loopable), `sfx_crow`,
`sfx_loon`, `sfx_windchime`, `sfx_wax_hiss`.
Music: `mus_d15_organ` (low organ drone), `mus_d24_muzak` (degraded elevator
muzak), `mus_d25_choir` (distant low choir).

**Rule: do NOT replace the mechanic gaze-audio sine tones** (D2 wells, D7
doors etc.) — pure tones being un-locatable by ear IS the audio-radar design.
Ambience layers under them; stingers are additive.

## Per-dimension polish list

| Dim | Props that move unseen | Texture keys | Audio |
|---|---|---|---|
| D1 ShiftingHalls | Living-room sets per cell, rebuilt on reroll (couch, armchair, table, lamp, painting); wall clocks w/ differing wrong times | d1_wall/floor/ceiling, wood_worn, fabric_couch | amb_d1, sfx_furniture_drag on nearby reroll |
| D2 DuneSea | Half-buried domestic junk (fridge/TV/streetlamp shapes) relocating like wells | d2_sand | amb_d2 |
| D3 LongDark | Numbered doors, sconces, room-service trays that slide; muffled TV behind one relocating door | d3_carpet/wall/door | amb_d3, sfx_tv_muffled, sfx_wood_creak |
| D4 WaitingField | Benches; payphone that rings ONLY while unobserved | grass_dry, d4_stone, wood_worn | amb_d4, sfx_phone_ring |
| D5 Archive | Reading desks + lamps; BookStacks restack/topple unseen | d5_books/wall, wood_parquet | amb_d5, sfx_paper_shuffle |
| D6 FrozenSea | Dark shapes frozen under the ice (silhouette quads under a translucent top layer) | d6_ice | amb_d6 |
| D7 HallOfDoors | Waiting chairs between doors reorient to FACE PLAYER unseen; ticking clock | d7_wall, d3_door, wood_parquet | amb_d7, sfx_clock_tick, sfx_chair_scrape |
| D8 Procession | Candle ring + banners; whisper swell during blackouts | d8_stone/statue | amb_d8, sfx_whisper |
| D9 RedForest | Hanging lanterns + mushroom clusters reposition; crow calls move when unseen | d9_bark/ground | amb_d9, sfx_crow |
| D11 ServerFarm | Rolling chairs + mugs migrate; CRT screens change content unseen (emissive tint swap) | d11_metal/floor | amb_d11 |
| D12 MirrorLake | Dock planks + rowboat that drifts unseen | d12_rock/planks | amb_d12, sfx_loon |
| D13 Orchard | Picnic blankets, ladders, fruit baskets hop between trees | d13_bark, grass_lush | amb_d13 |
| D15 Congregation | Stained-glass emissive windows; hymn books + candlesticks rearrange; dust motes | d15_wood/stone/glass1/glass2 | amb_d15, mus_d15_organ |
| D16 NeonGrid | Neon signs change face unseen (texture swap); vending machines shift | d16_asphalt/neon1-3 | amb_d16 |
| D18 StaticField | Farm junk (troughs, crates) creeps WITH the scarecrows; wind chimes ring only during blackout | d18_corn/dirt, burlap | amb_d18, sfx_windchime |
| D22 RustSea | Container stacks reshuffle unseen | d22_rust/container | amb_d22 |
| D23 WheatAtDusk | Windmill relocates; fence lines | d23_wheat, wood_worn | amb_d23 |
| D24 WaitingRoom | Water coolers/plants/magazines migrate; chairs turn toward player; ceiling tiles go missing behind you | d24_carpet/ceiling/wall | amb_d24, mus_d24_muzak, sfx_chair_scrape |
| D25 CandleSea | Wax stalagmites grow while unseen; shrine dressing | d25_wax/stone | amb_d25, mus_d25_choir, sfx_wax_hiss |

## Hard rules for implementers (from memory / CLAUDE.md — violations broke things before)

- Runtime geometry on layer **Body** (`DimensionSceneUtil.Block` does it; any
  hand-made GO must too) or the player slides.
- `Block` positions in WORLD space — build at identity, then place parents.
- Never put a movable AudioSource on a controller's own root.
- Flattened cylinder/sphere colliders balloon — scale uniformly or use BoxCollider.
- Don't cross-reference sibling array entries during Awake build loops (NRE
  strands portals at origin).
- New serialized fields: APPEND at class end only. Prefer constants/library
  keys over new serialized fields (scenes' serialized values win over script
  defaults).
- No `FindObjectOfType`/`Camera.main` in Update paths — use `ObserverState.Cam`.
- Per-frame allocs: reuse scratch lists (the sweep just fixed these).
- New shader keyword/variant in a code-made mat = add an anchor .mat in
  `Assets/2 - Materials/Dimensions/` (build stripping).
- New files need `git add` file + `.meta`.
- Props must never block the exit path or spawn on the player.

## Execution & verification

Order: infra (main session) → fork agents per batch (code only, no Unity MCP,
no git) while the main session runs Coplay asset generation → editor script
fills the library asset + Preloaded Assets → compile check → play-mode pass
through every dimension (Shift+D loader / open_scene), console clean +
screenshot eyeball each → commit (add new files + metas!) → push
`soundofspace feat/dimension-polish`.

Resume (if session dies): task list in harness + this doc are the state.
Check `git log feat/dimension-polish`, `Assets/2 - Materials/Dimensions/Textures/`
and `Assets/Audio/Dimensions/` contents vs the manifest to see what's done.
