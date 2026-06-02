# Tutorial UI + Compass Visual Polish — Design

**Date:** 2026-05-09
**Components:** `Assets/3 - Scripts/Tutorial/TutorialUI.cs`, `Assets/3 - Scripts/UI/CompassHUD.cs`
**Status:** Approved (visual choices A + 2)

## Context

The tutorial pill and the top-center compass both render as functional but visually weak HUD elements. Two prior tutorial UI iterations (top-right ornate sci-fi panel → bottom-left minimal pill → top-right pill with mild cyan accents) failed to land. The compass is a flat 800×60 black bar with white triangle markers — it works, but reads as placeholder.

Goal: a single coherent **angular sci-fi UI language** shared between the tutorial pill and compass, leaning on cyan accents and "etched in light" edge treatments. References that informed the choices: Returnal HUD chrome (angular clipped corners, glowing edge accents), Skyrim compass (faded ends, cardinal letters, bearing-projected markers).

User confirmed via visual companion: tutorial style **A — Angular Sci-Fi** + compass style **2 — Sci-Fi Skyrim**. Mockups archived in `.superpowers/brainstorm/252-1778377876/content/`.

## Tutorial pill — full visual spec

### Geometry

- **Anchor:** top-right; `anchorMin = anchorMax = (1, 1)`, `pivot = (1, 1)`
- **Position:** `anchoredPosition = (-rightMargin, -topMargin)` with defaults `rightMargin = 60`, `topMargin = 80` (1920×1080 reference)
- **Width:** fixed 360px (down from current 540px)
- **Height:** auto from VLG + ContentSizeFitter
- **Beveled corners:** top-left and bottom-right cut on a 14px diagonal. The visible silhouette is an angular sci-fi tab, not a rounded rect. Implemented via a procedurally generated sprite that bakes the clipped polygon into its alpha channel (no shader changes — Unity's UI Image samples the sprite's alpha as the mask).

### Colors

| Element | Color | Notes |
|---|---|---|
| Pill body (top) | `rgba(8, 18, 32, 0.88)` | Dark navy; subtle vertical gradient |
| Pill body (bottom) | `rgba(10, 24, 40, 0.92)` | Slightly more opaque — fakes depth |
| Border outline (1px) | `rgba(120, 200, 255, 0.45)` | Cyan-tinted, follows the beveled silhouette |
| LED accent bar | gradient `#5CC8FF` → `#2080D0` | 3px wide, vertical, on the left edge (14px insets top/bottom) |
| LED halo | `rgba(96, 200, 255, 0.18)` | Wider blurred glow behind the LED bar |
| Header tag (`// PROMPT`) | `#5CC8FF` @ 85% | 9px, letter-spacing 3px, uppercase |
| Body text | `#EAF6FF` | 14px, bold, soft cyan glow shadow |
| Sub-line text | `#88DCAA` | 11px, bold |
| Sub-line check icon | `#88DCAA` | 14×14, soft green glow |

### Layout (back-to-front stacking inside the pill)

1. Pill body Image (beveled silhouette sprite, vertical gradient, dark navy)
2. LED halo Image (horizontal sheen, behind the LED bar)
3. LED accent bar Image (cyan gradient strip, pulses dim↔bright)
4. Border outline Image (beveled hollow outline sprite, cyan tint)
5. VLG content stack:
   - Header label (`// PROMPT`, fixed string)
   - Body text (the live tip from `TutorialStep.Tip`)
   - Sub-line container (HLG with check icon + completion text — only `SetActive(true)` after `MarkComplete`)

### Animation

Unchanged from current implementation: pill slides DOWN from `y = topMargin - slideOffset` (40px above rest) into rest position over 0.25s with ease-out cubic, simultaneous alpha 0 → 1. Reverse for hide. `BorderPulse` coroutine drives the LED bar's dim↔bright sine pulse (currently drives pill border opacity — repoint at the LED bar).

### Inline keycap glyphs (kbd-style)

The body text contains key glyph substitutions like `<b>F</b>` from `PromptGlyphs.X`. Replace **the short single-key keyboard glyphs** with TMP inline sprites that render as miniature keycaps (rounded-rect cyan-tinted background with the letter centered, ~22×22px on a 14px text baseline). Multi-word phrases (`WASD`, `Space`, `Shift`, `Ctrl`, `WASD + Shift`, `left click`, `mouse`) and controller glyphs keep their current `<b>...</b>` bold-text rendering — sprite keycaps for arbitrary phrases would require either huge wide sprites or `<mark>`-style highlights, neither of which gives a consistent look. The hybrid "single-letter keys get sprites, words get bold text" reads as natural, not broken (this is how Returnal / Halo do it).

**Audited keyboard glyph set from `TutorialGate.cs:590-633`:**

| PromptGlyph | Keyboard returns | Sprite keycap? |
|---|---|---|
| `Interact` / `InteractPlain` | `F` | ✓ |
| `Flashlight` / `RollRight` | `E` | ✓ |
| `Drop` | `G` | ✓ |
| `Map` | `M` | ✓ |
| `BuildMenu` | `N` | ✓ |
| `Fishingdex` | `B` | ✓ |
| `RollLeft` | `Q` | ✓ |
| `AdvanceTip` | `TAB` | ✓ (multi-letter, but single token — wider sprite) |
| `Pause` / `Cancel` | `Esc` | ✓ (same) |
| `Jump` | `Space` | ✗ (long token — bold text) |
| `Sprint` | `Shift` | ✗ |
| `DownThrust` | `Ctrl` | ✗ |
| `PrimaryFire` | `LMB` | ✗ (3-char abbrev — bold text; could be a sprite later) |
| `SecondaryFire` / `PlacementRotate` | `RMB` | ✗ |
| `Move` / `DirThrustHold` | `WASD` / `WASD + Shift` | ✗ (compound, bold text) |
| `MouseLook` | `mouse` | ✗ |
| `PrimaryClick(Cap)` | `left click` / `Left click` | ✗ |

**Implementation:** runtime-generated TMP `SpriteAsset`.

1. **Atlas texture:** ~256×32 generated at runtime — one row of variable-width cells (most keys 32×32; `TAB`/`Esc` 48×32). Each cell is a rounded-rect with cyan tint border + dark fill + the key label centered in white.
2. **Glyph set (initial):** the keys marked ✓ in the table above — `F E G M N B Q TAB Esc` (9 sprites). Add more later by editing the `_keysWithSprites` constant and rebuilding the atlas.
3. **TMP SpriteAsset construction:**
   ```csharp
   var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
   asset.spriteSheet = atlasTex;
   asset.material = new Material(Shader.Find("TextMeshPro/Sprite"));
   asset.material.SetTexture(ShaderUtilities.ID_MainTex, atlasTex);
   asset.spriteGlyphTable = new List<TMP_SpriteGlyph>();      // one entry per key
   asset.spriteCharacterTable = new List<TMP_SpriteCharacter>(); // name → glyph index
   // populate, then:
   asset.UpdateLookupTables();
   tipText.spriteAsset = asset;
   ```
4. **PromptGlyphs update:** the `Pick()` helper at `TutorialGate.cs:598` returns the keyboard string. Wrap it in a small post-processor: if input source is keyboard AND the bold-stripped value (`"F"` from `"<b>F</b>"`) is in the sprite atlas, return `<sprite name="F">` instead. Controller paths and unsupported keys are untouched. Implementation as a static helper `PromptGlyphs.Maybe(string raw, string keyName)` keeps the per-property assignments tidy.
5. **Fallback:** if atlas building fails (TMP API surprise), `PromptGlyphs.X` returns the current `<b>...</b>` form unchanged. Existing tip rendering keeps working.
6. **Caching:** atlas + sprite asset built once on `TutorialUI.Awake` via a static initializer; reused for the lifetime of the app.

## Compass — full visual spec

### Geometry

- **Strip width:** 560px (down from 800)
- **Strip height:** 36px (down from 60)
- **Top margin:** 32px (down from 40)
- Anchor + cardinal-letter projection unchanged

### Colors

| Element | Color | Notes |
|---|---|---|
| Strip background (middle) | `rgba(10, 24, 40, 0.78)` | Cyan-tinted dark navy |
| Strip background (edges) | `rgba(0, 0, 0, 0.0)` | Faded transparent — gradient over the outer 12% on each side |
| Top edge sheen | `rgba(92, 200, 255, 0.55)` | 1px line, fades same 12% inset |
| Bottom edge sheen | `rgba(92, 200, 255, 0.55)` | Mirror of top |
| Center tick | `#5CC8FF` | 2px wide, full strip height with 4px insets, soft cyan glow shadow |
| Cardinal letters (N/E/S/W) | `rgba(120, 200, 255, 0.7)` | Monospace, 10px, letter-spacing 2 |
| Half-step ticks (·) | `rgba(120, 200, 255, 0.5)` | Same font, dimmer |
| Waypoint triangle | `#5CC8FF` | 16×16, soft cyan drop-shadow glow |
| Waypoint label | `#C8EAFF` | 12px, bold, dark drop shadow for legibility |

### Layout

Single procedural sprite handles the entire strip background — `MakeFadedBarTexture` bakes the horizontal alpha gradient into a single sliced sprite. Top/bottom sheens are two thin sibling Images using the existing horizontal sheen sprite (already implemented in TutorialUI's `MakeHorizontalSheenTexture` — extract to a shared `UISpriteCache` static helper, OR inline-duplicate in CompassHUD; see implementation notes below).

Cardinal direction letters render via the same waypoint mechanism — internally treated as four "fixed bearing" waypoints (N at 0°, E at 90°, S at 180°, W at 270°) that always render. Stored separately from gameplay waypoints so save/load doesn't see them. Optionally extend to 8 with NE/SE/SW/NW half-step ticks at 45°/135°/225°/315° if the strip looks too sparse during playtest.

### Animation

None (compass is always visible and bearing-driven). Marker edge-fade behavior unchanged.

## Implementation summary

### Files modified

- **`Assets/3 - Scripts/Tutorial/TutorialUI.cs`**
  - Replace `MakeRoundedHudPanelTexture` with `MakeBeveledPanelTexture` for the pill body (clipped corners, vertical gradient)
  - Add `MakeBeveledOutlineTexture` for the matching hollow border
  - Add inline `// PROMPT` header label rendering above the body text
  - Move the sub-line (check icon + "Press TAB to continue") inside the pill body's VLG instead of as a sibling
  - Shrink width to 360, font sizes to 14/11
  - Build runtime TMP `SpriteAsset` for keycap glyphs in `Awake`; assign to `tipText.spriteAsset`
- **`Assets/3 - Scripts/UI/CompassHUD.cs`**
  - Replace flat black `Image` with a `MakeFadedBarTexture` sliced sprite background
  - Add top + bottom sheen child Images
  - Repaint center tick + waypoint triangles + labels in cyan + cool white
  - Add cardinal-letter waypoints (internal-only; not saved)
  - Shrink defaults: `stripWidth = 560`, `stripHeight = 36`, `topMargin = 32`
- **`Assets/3 - Scripts/Tutorial/TutorialGate.cs`** *(small surgical edit)*
  - Add a `PromptGlyphs.Maybe(string raw, string keyName)` helper that emits `<sprite name=...>` when keyboard input is active AND the keycap is in the atlas, otherwise returns `raw` unchanged
  - Wrap the 12 properties whose kbm value is in the keycap set (Interact, InteractPlain, Flashlight, Drop, Map, BuildMenu, Fishingdex, RollLeft, RollRight, AdvanceTip, Pause, Cancel) — e.g. `Interact => Pick(Maybe("<b>F</b>", "F"), "<b>X</b>", "<b>Square</b>")`
  - Controller paths and multi-word keyboard glyphs untouched

### Files NOT modified

- `TutorialManager.cs`, `BonusTutorial.cs`, `TutorialStep.cs`, `TutorialSteps.cs` — no API changes
- `CompassSave.cs`, `SaveCollector.cs` — compass save/restore unaffected; cardinal letters are not waypoints in the persistent sense
- All callers of `TutorialUI.ShowStep / SetTip / MarkComplete / SwingOn / SwingOff` — public API stays the same

### New procedural sprite generators

| Function | Purpose |
|---|---|
| `MakeBeveledPanelTexture(int size, int bevel, bool gradient)` | Tutorial pill body — clipped polygon silhouette with optional vertical gradient |
| `MakeBeveledOutlineTexture(int size, int bevel, int thickness)` | Hollow outline matching the beveled silhouette |
| `MakeFadedBarTexture(int width, int height, float fadeFraction)` | Compass strip background — alpha gradient that's 0 at the left/right edges, 1 in the middle |
| `MakeKeycapGlyphAtlas(string[] keys, int cellSize)` | Single-row texture atlas for TMP sprite asset; one cyan-rounded-rect-with-letter per key |

Each follows the existing `MakeRoundedRectTexture` pattern (per-pixel alpha calc, `Texture2D.SetPixels`, `Apply`).

## Public API contract

No changes to `TutorialUI`'s public surface (`ShowStep`, `SetTip`, `MarkComplete`, `HideAll`, `SwingOff`, `SwingOn`, `IsTipRevealing`, `IsCompletedRevealing`, `IsOffScreen`, `AutoSkipFired`, `charDelay`, `autoSkipDuration`).

New public method on `TutorialUI`: `bool HasKeycapSprite(string keyName)` — used by `PromptGlyphs.X` to decide whether to emit a `<sprite>` tag or fall back to `<b>` text.

`CompassHUD`'s public surface (`AddWaypointByTag`, `AddWaypoint`, `RemoveWaypoint`, `SetActive`, `HasWaypoint`, `ClearAll`, `GetSaveState`, `ApplySaveState`) unchanged.

## Verification

End-to-end Editor playtest:

1. **Tutorial first appearance** — start a new game (cabin start). Tutorial pill slides in from above into the top-right with the new beveled silhouette, cyan LED bar pulsing on the left, `// PROMPT` header visible, body text reveals via typewriter. Keycap glyphs (e.g. `F` in `PickUpRodStep`'s "Press F to pick up the rod" or `M` in the Map step) render as cyan rounded sprites; the multi-word `mouse` glyph in `WakeUpLookStep` stays as bold text (per the hybrid spec).
2. **Sub-line** — satisfy the first step. Sub-line ("✓ Press TAB to continue") fades in INSIDE the pill (not as a separate row below). Tab advances cleanly.
3. **Live counter** — find a step that updates `Tip` per frame. Counter updates inline without re-animating the pill.
4. **Bonus tutorial swap** — trigger axe-NPC's offer. Pill slides up-and-out, bonus tutorial pill slides in (same style). After the bonus chain ends, main pill returns.
5. **Compass — visual** — outdoors during day, the compass reads as a faded-edge strip with a glowing cyan center tick. N/E/S/W letters visible at their bearings. Walk around — letters animate smoothly, center tick stays put.
6. **Compass — waypoints** — trigger a tutorial step that adds a waypoint (e.g. fishing bank). Cyan triangle marker appears at the correct bearing. Walk so it leaves the visible field — fades to 40% alpha at the edge, doesn't pop.
7. **Save/load** — save mid-tutorial, return to main menu, load. Pill appears immediately with correct content. Compass waypoints restore. Cardinal letters re-render (they're not saved — built fresh on `Awake`).
8. **Compile check** — `mcp__coplay-mcp__check_compile_errors` returns no errors.

## Risks / open questions

- **TMP sprite asset runtime build.** `TMP_SpriteAsset` instances are usually authored in the editor; runtime construction is supported via the API but rarely seen in tutorials. If `UpdateLookupTables()` doesn't behave as documented, fallback path (plain bold text) keeps the UI usable. Worst case: spend 10 minutes wiring an editor-baked sprite asset instead and shipping that as a `Resources/` load.
- **Beveled silhouette + 9-slice.** Sliced sprite borders need to be tuned so the bevel doesn't stretch when the pill height grows for a 2-line tip. Solution: render the bevel in the corners only (corner slices) and let the straight middle edges stretch normally. This is exactly what 9-slice is designed for; `Vector4` border params will be set so the corner cuts live entirely in the corner slice cells.
- **Atlas size if the key set grows.** 256×32 covers the initial 9 keycaps (most at 28×28 cells, TAB/Esc at 48×28). If the keyset grows past ~10, switch to a two-row 256×64 layout. Not a near-term concern.
