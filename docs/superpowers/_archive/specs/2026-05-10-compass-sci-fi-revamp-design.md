# Sci-fi Compass Revamp — Design

## Goal

Two changes to `CompassHUD`:

1. **Visual revamp** — extend the existing top-center strip from "cardinal letters only" to the Destiny/Halo-style sci-fi compass picked from mockup B:
   - **Heading badge** above the strip showing the current bearing as a number plus cardinal short-code (e.g. `045°  NE`).
   - **Degree-number labels** at every 30° between cardinals (`030`, `060`, `120`, `150`, `210`, `240`, `300`, `330`), scrolling with the strip exactly like the cardinal letters do.
   - **Tick marks every 10°** (36 around the full 360°), with every 30° rendered as a taller "major" tick.
   - **Glowing center tick + faded edges + cardinal letters** all preserved from the current implementation.

2. **Main-menu bleed fix** — the compass currently flashes briefly on top of the main menu when the player clicks PLAY. Cause: `MainMenuController.EnsureGameplaySingletons()` creates the `CompassHUD` singleton synchronously, its `Awake` builds the canvas immediately, and that canvas renders on screen during the brief scene-transition window before the gameplay scene takes over. Fix: keep the compass canvas DISABLED until a `PlayerController` is found in the scene; enable it the moment one appears.

## Non-goals

- No changes to waypoint positioning math (`ProjectOnPlane` + `SignedAngle` against surface-up + body-relative north) — that's working and the new degree-number labels reuse it.
- No changes to the save/load path (`CompassSave.WaypointEntry`).
- No changes to the cardinal letter behavior (still seeded once on Awake, anchored at fixed bearings, scroll with player rotation).
- No changes to the gameplay-waypoint API (`AddWaypointByTag` / `RemoveWaypoint`).

---

## Part 1 · Visual revamp

### Heading badge (new)

A single `TextMeshProUGUI` element anchored 4 px above the strip, centered horizontally on the screen's center.

- Background: rounded-rect gradient (`#142C48F2` → `#0E1E34F2`), 1.5 px cyan border (`#78C8FF8C`), soft outer cyan glow.
- Padding: 3 px top/bottom, 14 px left/right.
- Text: 11 px Consolas (or project HUD font fallback chain), letter-spacing 4 px, color `#EAF6FF` with cyan text glow.
- Format: `{deg:000}°  {cardinalCode}` — three-digit zero-padded degree + two spaces + 1-or-2-letter cardinal code (`N`, `NE`, `E`, `SE`, `S`, `SW`, `W`, `NW`).
- Update: per-frame in `LateUpdate`, after the bearing math the rest of the compass already runs. Computed once, fed into the badge's text only when the rounded-degree integer or the cardinal-code string actually changes (avoid per-frame string allocation).

Bearing math:
```
heading = SignedAngle(northDir, forwardOnPlane, surfaceUp)
heading = ((heading % 360) + 360) % 360       // normalize to [0, 360)
cardinalIndex = ((int)((heading + 22.5f) / 45f)) % 8
cardinalCode  = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[cardinalIndex]
```

Where `northDir` is the same body-projected world-north vector the existing `ComputeSurfaceNorth` returns — already in scope, just lift the result.

### Degree-number labels (new, 8 total)

Seeded in `Awake` alongside the cardinals. Same `Waypoint` mechanism (resolves to a virtual world-position at `origin + dir * 100f` where `dir` is rotated from `northDir` by the bearing). Eight bearings:

| Bearing | Label |
|---|---|
| 30° | `030` |
| 60° | `060` |
| 120° | `120` |
| 150° | `150` |
| 210° | `210` |
| 240° | `240` |
| 300° | `300` |
| 330° | `330` |

Visual: same row as cardinal letters but smaller and dimmer. 10 px font, color `rgba(120, 200, 255, 0.65)`. No icon.

To keep the waypoint type system clean, introduce a `WaypointKind` enum on `Waypoint`:
```
enum WaypointKind { Gameplay, Cardinal, DegreeNumber, Tick }
```
Then `BuildWaypointUI` switches on `kind` for layout/font/icon decisions. Existing cardinal-letter path becomes `kind == Cardinal`.

### Tick marks (new, 36 total)

Seeded in `Awake`. Bearings every 10° from 0° through 350°. Same waypoint mechanism for positioning. Visual: thin vertical line (no label, no icon-sprite — a tiny solid-color UI Image). Every third tick (the ones at 0/30/60/... — exactly the bearings shared with cardinals and degree numbers) is taller and brighter ("major"); the rest are short and dim.

- Major tick: 8 px tall, 1 px wide, color `rgba(120,200,255,0.75)`, anchored to the top of the strip.
- Minor tick: 6 px tall, 1 px wide, color `rgba(120,200,255,0.45)`, anchored to the top of the strip.

Ticks at bearings already occupied by a cardinal letter or degree-number label overlap visually but render below them — totally fine since the letters/numbers cover the tick. Simpler than gating the seed loop on "is this bearing already labeled?".

Render order: ticks first (back layer of the strip's child list), then cardinal letters / degree numbers / waypoint markers on top.

### Strip width + height

Current `stripWidth = 560f`, `stripHeight = 36f`. Keep both. The added elements fit inside the existing strip without size changes; the badge above adds about 24 px of vertical real estate so the overall HUD occupies y ≈ 8 → 76 from the screen top.

### Center tick

Current implementation keeps the player-facing center tick — a 2 px cyan vertical line with a soft glow. No change. It's still the visual anchor for "this is where you're looking."

### Sort order

Compass canvas stays at `sortingOrder = 300` (above VitalsHUD at 25 and the hotbar at 50, below tutorial pill at 500). The new heading badge lives on the same canvas; no z-order conflict.

---

## Part 2 · Main-menu bleed fix

### Diagnosis

Trace:
1. User clicks PLAY in `MainMenuController`.
2. `EnsureGameplaySingletons()` creates the `CompassHUD` GameObject and `AddComponent<CompassHUD>()` synchronously.
3. `Awake` runs immediately: sets `Instance`, calls `BuildCanvas()` which builds the canvas + strip + cardinal/tick/degree waypoints.
4. The canvas is enabled by default → renders on the active scene (still `MainMenu`).
5. Scene transition begins, but for the few frames before the gameplay scene becomes active, the compass is visible on the main menu.

### Fix

Disable the canvas on construction; enable it the first frame a `PlayerController` is found. Specifically:

```csharp
void Awake() {
    // ... existing singleton + BuildCanvas() ...
    if (_canvas != null) _canvas.enabled = false;
}

void LateUpdate() {
    if (_playerCached == null) _playerCached = FindObjectOfType<PlayerController>();
    if (_cameraCached == null) _cameraCached = Camera.main;
    if (_playerCached == null || _cameraCached == null) return;

    // First frame the player exists — turn the compass on.
    if (_canvas != null && !_canvas.enabled) _canvas.enabled = true;

    // ... existing per-frame update ...
}
```

This is a one-shot flip:
- During main-menu time, no PlayerController exists in the active scene → canvas stays disabled → nothing renders.
- Once the gameplay scene loads and spawns the player, the first `LateUpdate` after that finds the player and enables the canvas.

Equivalent for save-load: when restoring from main menu, the compass is created via `EnsureGameplaySingletons` (canvas disabled), then the gameplay scene loads (player exists), then `LateUpdate` enables the canvas. Works for both new-game and load-game flows.

---

## Coding-convention compliance (per `CLAUDE.md`)

- **Lazy-cached scene refs:** the existing `_playerCached` + `_cameraCached` lazy-find pattern stays. The new badge text uses change-detection (last-seen int + last-seen string) so the text field is only written when the value visibly changes.
- **No per-frame allocations:** badge text uses `string.Format` (or `$"{deg:000}°  {code}"`) only when `deg` or `code` changes since last frame. Tick + degree-number elements have no per-frame allocation — their positions are recomputed from existing waypoint math.
- **Singleton pattern unchanged:** the canvas-enable trick is purely defensive plumbing.
- **No `Resources.Load` in user code:** new UI elements use the existing HUD font fallback chain (already in place via the project's other singletons).

## File-change summary

**Modified:**
- `Assets/3 - Scripts/UI/CompassHUD.cs` — add `WaypointKind` enum + `Kind` field on `Waypoint`; extend `BuildWaypointUI` to handle Tick + DegreeNumber visual variants; add 36 tick seeds + 8 degree-number seeds alongside the 4 cardinal seeds; build the heading badge in `BuildCanvas`; update `LateUpdate` to compute heading + update the badge text + enable the canvas on first player-found.

No new files. Save/load schema unchanged.

## Risks / open questions

- **Waypoint count (~48 instead of 4):** every `LateUpdate` iterates all waypoints. Going from 4 → 48 means 12× the per-frame math. Each iteration is a `ProjectOnPlane` + `SignedAngle` + a few component reads. At ~48 calls per frame on a 100 Hz physics target plus per-frame UI updates, this is still <0.2 ms on any modern CPU. Not a concern.
- **Cardinal-letter source-tag check on save:** existing `GetSaveState` skips internal waypoints by checking `SourceTag == CardinalSourceTag`. The new Tick + DegreeNumber waypoints also need to be skipped — easiest fix is to check `Kind != Gameplay` instead of comparing tags. Update accordingly.
- **First-frame canvas-enable:** when the player spawns and the canvas flips on, the compass appears immediately. In practice this happens during the gameplay-scene fade-in / loading transition, so the player is unlikely to perceive a "pop." If it turns out to read as abrupt during playtest, a 150 ms `CanvasGroup.alpha` fade-in can be added later — but the spec doesn't include it by default.
- **Heading badge while the player isn't yet positioned:** during the canvas-disabled window, the badge text is never updated. Once enabled, the first LateUpdate sets it. No stale-value issue.
