# Black Hole Dimensions — Design Spec

**Date:** 2026-07-05
**Status:** Approved design, pre-implementation
**Goal:** Replace the black hole's direct teleport-to-Backrooms with a chain of four
observation-based dimensions. Geometry/objects obey one rule — *the world is only fixed
while observed* — expressed as a different exit mechanic in each dimension. Playable
end-to-end today; enemies/threats deliberately out of scope.

---

## 1. Player-facing flow

```
Black hole center (existing BlackHoleCapture white-flash)
  → D1 Shifting Halls   (exit: find a door and keep it on screen)
  → D2 Dune Sea         (exit: identify the true well and jump in)
  → D3 Long Dark        (exit: cross a bridge that is only solid while UNobserved)
  → D4 Waiting Field    (exit: a monolith door that only approaches while UNobserved)
  → R1_Backrooms (existing scene, becomes the final hub)
       → EXIT portal        → ReturnToGameplay → cabin on Humble Abode (existing)
       → NEXTLEVEL portal   → PoolroomsDemo → NO EXIT (trap; existing portal)
```

- Failure is never death: wrong choices relocate/disorient the player or drop them back
  to a checkpoint inside the same dimension.
- The Poolrooms trap is in-session only by design: the pre-dive autosave (taken at the
  cabin spawn by `PortalManager.EnterInterior`) means quit + load rescues the player at
  the cabin. Do not "fix" this — it is the intended escape hatch.
- The cabin's BackroomsEntrance1 (→ R1_Backrooms directly) is untouched.

## 2. Shared core: observation system

New folder: `Assets/3 - Scripts/Dimensions/`.

- **`ObserverState`** (static): caches the active camera's frustum planes once per frame
  (frame-stamped lazy compute). API:
  `bool IsObserved(Bounds b, float maxDistance = Mathf.Infinity)` —
  dot-product behind-camera early-out, then `GeometryUtility.TestPlanesAABB`.
  Camera resolution mirrors `BlackHoleCapture.ResolveFxCamera` (CameraEffectsManager →
  `Camera.main` fallback), cached, lazy-refind only when null.
- **`ObservationTracker`** (plain struct/class, not a MonoBehaviour): wraps
  `IsObserved` with a grace window (default ~0.15 s) so screen-edge flicker doesn't
  strobe state. Exposes `WasEverObserved`, `TimeUnobserved`.
- Frustum-only visibility (no occlusion raycasts): "behind a wall but in front of you"
  counts as observed. Forgiving, cheap, and the 180°-turn fantasy still works.
- Spinning fast to scan IS a supported strategy, not an exploit.
- Per-frame cost is O(number of tracked bounds), a few hundred max — no spatial
  structure needed.

## 3. Dimensions

Each dimension is its own Unity scene in `Assets/4 - Scenes/Dimensions/`, added to
Build Settings, chained with the existing `LevelPortal` (`EnterInterior`) plumbing.
Each contains: a copy of the interior player rig (sourced from R1_Backrooms), its own
lighting/fog/skybox, a `DimensionController` script for its mechanic, ambient audio.
No purchased assets: primitive/procedural geometry + Coplay-generated textures and
SFX/ambience.

### D1 — Shifting Halls (`D1_ShiftingHalls`)

Backrooms-flavored endless corridor maze.

- **World:** grid cells (~6 m). Static floor + ceiling planes that follow the player in
  grid-snapped steps (so tiling never swims and the floor can never despawn). Pooled
  wall segments; cells instantiated within ~5 cells of the player.
- **Reshuffle rule:** a cell whose bounds are fully unobserved AND not the player's
  current cell re-rolls its wall layout (new seed, rebuilt immediately — it's
  off-screen, nobody sees the pop). Turn 180° → different maze.
- **Navigability:** cap walls per cell (≤2 of 4 edges) to keep density walkable.
  Momentary sealed pockets are acceptable — looking away from a blocking wall lets it
  reshuffle, so the mechanic self-heals dead ends.
- **Exit:** each reshuffle rolls a small chance (~3–5%, tunable) of placing an exit
  door in that cell. Doors spawn off-screen by construction (only unobserved cells
  reshuffle) — you discover them by turning. Once a door has been observed, if it ever
  leaves the screen past the grace window it despawns (its cell reshuffles). Reaching
  it while keeping it on screen → portal to D2.
- **Look:** flickering fluorescents, low fog, yellowed/concrete generated textures,
  electrical hum ambience.

### D2 — Dune Sea (`D2_DuneSea`)

Open desert; only the wells play the observation game.

- **World:** static rolling-dune terrain (procedural heightmap mesh or Unity Terrain,
  ~1 km, thick heat fog, harsh directional light). Terrain never changes.
- **Wells:** ~8 stone wells (cylinder primitives + generated texture) placed within a
  band around the player (~40–150 m). Any well that goes unobserved relocates to a new
  height-snapped spot in that band — so the field of wells stays near the player and
  the desert edge is unreachable in practice.
- **The true well:** one designated well. Tells: a quiet looping sound (proximity-scaled
  volume — the player's audio compass, since looking away can relocate even the true
  well) plus a faint interior glow visible only up close.
- **Wrong well:** jump-in trigger → short blackout → teleport to a random distant dune
  (~100–200 m, random facing). Disorienting, never lethal.
- **True well:** jump-in trigger → portal to D3.

### D3 — Long Dark (`D3_LongDark`)

The rule inverts: solid only while UNobserved.

- **World:** black void, faint star particles, a start platform, a glowing beacon tower
  ~150 m away, and a bridge of ~50 segments (~3 m each) between them. Start and end
  platforms are always solid.
- **Bridge rule:** observed segment → dissolves (alpha fade) and drops its collider
  after a short delay (~0.2 s, so a glance isn't instant doom); unobserved segment →
  solid. You cross by walking backwards toward the beacon, trusting what you can't see.
  Looking down at your feet drops you.
- **Falling:** kill volume below → fade → respawn at the start platform. No damage.
- **Teaching:** a stone tablet at the start platform with one carved line (TMP), e.g.
  "IT HOLDS ONLY WHAT YOU CANNOT SEE" — plus the visible dissolve/reform at screen
  edges teaches the inversion.
- **Exit:** trigger at the beacon tower → portal to D4.

### D4 — Waiting Field (`D4_WaitingField`)

Inverted Weeping Angel with no enemy: the exit stalks you.

- **World:** endless quiet grassland plane (simple mesh + generated texture — the
  planet grass system is NOT reused), strange gradient sky, wind ambience.
- **Monolith door:** spawns ~200 m away. While observed it is frozen. While unobserved
  it advances toward the player (~8 m/s of unobserved time, tunable; stops at 3 m).
  A low rumble scales with proximity so you feel it approaching behind you.
- **Exit:** when it arrives, its doorway is open; walking through → portal to
  R1_Backrooms.

## 4. Wiring changes to existing content

- `BlackHoleCapture.backroomsScene` (serialized field, gameplay scene): point at
  `D1_ShiftingHalls`. No code change.
- `D4` exits into `R1_Backrooms` via `PortalManager.EnterInterior` — interior→interior
  hop, same as the existing Backrooms→Poolrooms portal.
- **Verify** in R1_Backrooms: EXIT portal (ReturnToGameplay) and NEXTLEVEL portal
  (→ PoolroomsDemo) exist as documented in `LevelPortal.cs`.
- **Verify** PoolroomsDemo contains no ReturnToGameplay/exit portal; remove or disable
  any found. The Poolrooms is a one-way trap.
- Add the four dimension scenes to Build Settings (enabled).

## 5. Persistence & save system

- No SaveCollector/SaveData changes. Dimensions are session-only, unsaved spaces —
  "timeless" in fiction and in code. Inventory/equipment ride along on the existing
  DontDestroyOnLoad singletons + `PortalManager`'s equipment carry.
- Quitting anywhere in the chain → load restores the pre-dive autosave at the cabin.

## 6. Out of scope (later passes)

- Enemies/threats, timers, sanity systems.
- The other 8 dimensions (the architecture — one scene + one DimensionController each —
  is the template for them).
- Occlusion-aware observation, save-state inside dimensions, HAL commentary hooks.

## 7. Success criteria

1. Diving into the black hole loads D1 (white-flash transition intact).
2. Each dimension is traversable using its intended mechanic, and each mechanic's
   punishment works (door despawn, wrong-well teleport, bridge fall + respawn,
   monolith freeze-when-watched).
3. D4 → Backrooms → EXIT returns to the cabin with inventory intact.
4. Backrooms → Poolrooms has no way back.
5. Playable in-Editor today; no changes to forbidden zones; no console errors.
