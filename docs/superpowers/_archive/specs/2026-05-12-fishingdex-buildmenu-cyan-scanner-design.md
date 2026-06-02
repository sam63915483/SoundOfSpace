# Fishingdex + Build Menu redesign — cyan scanner

Date: 2026-05-12

## Problem

The fishingdex and build menu look like two unrelated UIs:
- The **fishingdex** is a generic Unity panel — scene-built with inspector-wired Canvas/RawImage/TMP refs and no visual identity. It does not match the rest of the project's UI language (main menu, pause menu, compass HUD, tutorial pill, killstreak HUD, jetpack HUD — all of which use a cohesive cyan / dark-navy "galaxy" palette).
- The **build menu** is procedurally built and somewhat polished, but it uses a dark-navy + **orange** palette (`C_Title = #FFA528`) that doesn't appear anywhere else in the game. Visually it looks like a different game's menu.

The user wants both redesigned to look like sister panels — same shape, same palette — and to fit alongside the rest of the cyan-themed UI.

## Scope

In scope:
- Rebuild both UIs around a shared **3-column "scanner" layout** (list / preview / spec).
- Use the existing codebase cyan palette (`AccentCool = #5BD8FF` on `BgBottomColor = #07051C`-ish dark navy).
- Preserve all current functionality of both UIs (modes, categories, costs, affordability gating, save behaviour, keybindings, controller bindings, tutorial gates).
- Convert the fishingdex from scene-built to procedurally-built so it has the same construction shape as `BuildMenuUI` (no inspector wiring required, one component handles its own canvas).

Out of scope:
- Changes to the fish preview rig (camera + stage) other than re-parenting it under the manager. Existing `RenderFish` math and lighting stay.
- Changes to the build menu's preview rig (`EnsurePreviewRig` / `RenderPrefabPreview`) — it already produces clean rendered icons.
- New gameplay features (no new fish stats, no new buildables, no new sell/cook UX).
- Cleanup of the scene-level fishingdex GameObjects under `HUD_Canvas` in `1.6.7.7.7.unity` — once procedural construction works, those scene objects are inert. They can be deleted in the editor at the user's leisure but the code doesn't require it.

## Design

### Shared cyan palette (one source of truth)

A new static class `Assets/3 - Scripts/UI/CyanScannerPalette.cs`:

```csharp
public static class CyanScannerPalette
{
    public static readonly Color32 PanelBg        = new Color32(0x0A, 0x12, 0x28, 0xF0); // dark navy, slightly translucent
    public static readonly Color32 PanelBorder    = new Color32(0x1C, 0x3A, 0x5C, 0xFF);
    public static readonly Color32 InnerBg        = new Color32(0x0C, 0x1A, 0x32, 0xFF);
    public static readonly Color32 InnerDivider   = new Color32(0x12, 0x28, 0x45, 0xFF);

    public static readonly Color32 Accent         = new Color32(0x5B, 0xD8, 0xFF, 0xFF); // primary cyan
    public static readonly Color32 AccentDim      = new Color32(0x88, 0xC4, 0xDC, 0xFF); // muted cyan
    public static readonly Color32 Text           = new Color32(0xA8, 0xE6, 0xFF, 0xFF); // body text
    public static readonly Color32 TextBright     = new Color32(0xFF, 0xFF, 0xFF, 0xFF); // values
    public static readonly Color32 TextMuted      = new Color32(0x88, 0xC4, 0xDC, 0xCC);

    public static readonly Color32 SelectionFill  = new Color32(0x14, 0x30, 0x55, 0xFF);

    public static readonly Color32 BtnNormal      = new Color32(0x14, 0x30, 0x55, 0xFF);
    public static readonly Color32 BtnNormalEdge  = new Color32(0x2A, 0x50, 0x78, 0xFF);
    public static readonly Color32 BtnPrimary     = Accent;
    public static readonly Color32 BtnPrimaryText = new Color32(0x0A, 0x12, 0x28, 0xFF);

    public static readonly Color32 CostAfford     = Accent;
    public static readonly Color32 CostUnafford   = new Color32(0xFF, 0x5A, 0x5A, 0xFF);

    public static readonly Color32 GridLine       = new Color32(0x5B, 0xD8, 0xFF, 0x10); // ~6% alpha for blueprint grid
    public static readonly Color32 BracketColor   = Accent;
}
```

These hex values mirror what `MainMenuController.AccentCool / SubtitleColor` already use, so the new panels live in the same colour space as the rest of the codebase UI. Centralising them in one file means future visual tweaks affect both menus at once.

### Shared "bracket frame" helper

Both panels' preview boxes use 4 corner-bracket sprites instead of a full dashed border. A static helper builds them so the same code constructs them in both menus:

```csharp
public static class ScannerFrame
{
    // Adds 4 RectTransform/Image children to `parent` arranged in TL/TR/BL/BR
    // corners, with 2-px-thick L-shapes pointing inward. Length defaults to
    // 14 px; color defaults to CyanScannerPalette.BracketColor.
    public static void AddBrackets(Transform parent, float length = 14f, float thickness = 2f, Color32? color = null);

    // Optional: 6%-alpha cyan grid lines drawn as a child of `parent`, sized
    // by gridSpacing (default 24 px). Used behind the build menu preview to
    // give the "blueprint" feel.
    public static void AddBlueprintGrid(Transform parent, float gridSpacing = 24f);
}
```

The brackets are 4 individual Images so they never overlap content and don't need a custom shader. The blueprint grid is built from two RectMask2D-clipped tiling Image strips with the cyan colour at 6% alpha.

### Layout — both panels (sister 3-column scanner)

Each panel is a single root rect at fixed pixel size (`960×720` for the dex, same for build menu), centred. Inside:

```
┌────────────────────────────────────────────────────────────────┐
│ HEADER ──────────────────────────────────────────  STAT ─────  │  ← cyan, letter-spaced
├────────────────────────────────────────────────────────────────┤
│ TABS (build menu only — categories)                            │
├──────────────┬──────────────────────────┬─────────────────────┤
│              │                          │                     │
│   LIST       │      PREVIEW             │    SPEC PANEL       │
│   (30%)      │      (38%)               │    (32%)            │
│              │   ┌──┐         ┌──┐      │   ─────────         │
│ entries with │   │  │  3D     │  │      │   K  V              │
│ left-edge    │   └──┘ preview └──┘      │   K  V              │
│ accent on    │   ┌──┐         ┌──┐      │   K  V              │
│ selected     │   │  │         │  │      │   ─────────         │
│              │   └──┘         └──┘      │   description       │
│              │      ↑ bracket           │                     │
├──────────────┴──────────────────────────┴─────────────────────┤
│ [ESC] CLOSE                       [ENTER] PLACE / EAT RAW     │
└────────────────────────────────────────────────────────────────┘
```

Columns are anchored to the body region with fixed-fraction widths (`0.30 / 0.38 / 0.32` of body height-equivalent so they don't reflow weirdly).

### Fishingdex specifics

Header reads `> FISHINGDEX // SCAN MODE` (left) and `<N> ENTRIES` (right, count from `FishInventory`).

**List** is a vertical scroll of one row per `FishEntry`. Row layout:
- Left: 3 px-wide rarity stripe (rare = `#5BD8FF` accent, uncommon = `#88C4DC`, common = `#3A6080`)
- Then: small thumbnail (`64×64` rendered fish at preview-camera output)
- Then: monospaced label `{TYPE}.{WEIGHT}` (e.g. `SUNSCALE.18`) in `AccentDim`
- Selected row: `SelectionFill` background, label colour `Accent`, left-edge inset bracket
- Hover: subtle background lift

The current `FishingdexEntryUI` component is reused for row construction but its prefab can be retired — the new `FishingdexManager` instantiates rows procedurally with a private helper, mirroring `BuildMenuUI.AddListEntry`.

**Preview** is a square aspect-ratio panel filled with the current `RenderFish` rendertexture. Bracket corners on top via `ScannerFrame.AddBrackets`. No blueprint grid (it's a creature, not a structure — grid would be wrong).

**Spec panel** rows (label / value):
- `TYPE` / fish type name
- `MASS` / `{weight} LB`
- `CLASS` / rarity (reuse `GetRarityLabel`'s existing strings: `* COMMON *` / `** UNCOMMON **` / `*** RARE ***`)
- `VALUE` / `${value}`
- `CAUGHT` / `×{count}` (count of this fishType in the inventory — new info, easy to compute via a single pass over `FishInventory.AllFish`)

Below the spec rows, a 2-line italic description per rarity tier (constant strings — no new gameplay data needed).

**Footer buttons** (depend on mode):
- Browse mode: `[ESC] CLOSE` (left, secondary) + `[E] EAT RAW` (right, primary) — primary disabled when `currentDetailEntry == null`
- Sell mode: `[ESC] BACK` + `[ENTER] ADD FISH` (calls existing `onFishAction` callback)
- Cook mode: same as Sell, label `ADD FISH`

All three modes use the same panel — no separate list/detail screens. Selecting a row from the list updates the preview + spec in place. The current Detail/List split (`ShowDetail` / `ShowList`) collapses into one screen since everything is already visible.

The `ShowDetail` and `ShowList` methods stay (called by external code and `backButton`) but become trivial: `ShowDetail` selects the row, `ShowList` clears selection. No panel-swap needed.

### Build menu specifics

Header reads `> BUILD MENU // BLUEPRINTS` (left) and `WOOD {count}` (right, live-updated from `WoodInventory`).

**Tab row** sits between header and body. Tabs auto-built from the `BuildableCategory` enum values that appear in `buildables`, exactly as today (`BuildTabRow`). New styling: each tab is `BtnNormal` background, `AccentDim` text; active tab is `SelectionFill` background with `Accent` text + 1 px `Accent` top border. No padding/font changes from current.

**List** is the same shape as the dex list, but row content is:
- Left: 2 px solid `Accent` indicator (same colour for every category — the category label appears in the spec panel, so the list doesn't need to colour-code by category)
- Then: small `64×64` rendered preview from the existing `_previewCache`
- Then: `{NAME}` label
- Right side of row: cost like `20w`, in `CostAfford` if affordable, `CostUnafford` if not

**Preview** is the existing `RenderPrefabPreview` rendertexture in a square panel. **Blueprint grid** behind it (`ScannerFrame.AddBlueprintGrid`) at 24 px spacing — visually distinguishes "structure being built" from "creature being scanned". Bracket corners on top.

**Spec panel** rows:
- `NAME` / `{displayName}`
- `CLASS` / `{category}` (e.g. `SHELTER`)
- `COST` / `{n} wood` (coloured by affordability)
- `SIZE` / pull from the prefab's bounding box rendered to the preview, formatted as `{x:F1}×{z:F1} m` (new — derivable from the existing bounds calc inside `RenderPrefabPreview`, can be cached alongside the rendertexture per prefab)

Below: 2-3 line description from `BuildableEntry.description` (existing field).

**Footer buttons**:
- `[ESC] CLOSE` (left, secondary)
- `[ENTER] PLACE` (right, primary) — disabled when `detailEntry.woodCost > 0 && wood < detailEntry.woodCost`

The current `BuildMenuUI` has a separate `listPanel` and `detailPanel` screens. These collapse into one panel — same as the dex. Selecting a card updates preview + spec in place. `ShowDetail` and `ShowList` keep their names but become row-selection / row-clear.

### Procedural construction of the fishingdex

`FishingdexManager` currently has inspector refs to scene-built objects:
```
fishingdexCanvas, listPanel, detailPanel,
listContent, fishEntryItemPrefab, closeButton,
detailPreviewImage, detailTypeText, detailWeightText,
detailRarityText, detailValueText, backButton,
detailActionButton, detailActionText
```

These are dropped. The manager builds its own canvas on `Start` like `BuildMenuUI.BuildUI` does:
- Find or create a Screen Space Overlay canvas (look for `HUD_Canvas` first, then create `Fishingdex_Canvas` with sortingOrder 200)
- Procedurally build root panel + header + list + preview + spec + footer
- Cache references to TMP fields it needs to update (typeText, massText, etc.) as private fields

The preview camera (`fishPreviewCamera`) and stage (`fishPreviewStage`) currently come from inspector refs. To match the build menu's procedural rig, the manager builds these too in an `EnsurePreviewRig()` helper called from `Start`. The new rig lives off-screen at `(10000, 10000, 10000)` like the build menu's, with the same fish layer (31) and two warm preview lights — `SetupPreviewCamera` from the existing code moves wholesale into this helper.

**Backwards-compatibility:** the old scene-level `Fishingdex_Canvas` (under `HUD_Canvas`) is now unreferenced and inert. It stays in the scene until the user deletes it manually. The procedural canvas is on a different GameObject so they don't conflict at runtime (the old canvas is never activated by the new code path).

### Behaviour preserved (must not regress)

- **Keybindings**: `B` keyboard / `RB` controller toggles dex; `N` keyboard / `LB` controller toggles build menu.
- **Tutorial gates**: open paths still gated on `TutorialAbility.Fishingdex` and `TutorialAbility.BuildMenu`.
- **Ship piloting suppression**: build menu doesn't open while piloting; dex respects same rule for RB.
- **Cursor state**: same lock/visible behaviour on open/close.
- **Fishingdex modes**: Browse / Sell / Cook still work, with `OpenForSell` / `OpenForCook` callbacks unchanged. Action button label switches between `EAT RAW` (Browse) and `ADD FISH` (Sell/Cook).
- **Build menu wood live update**: every frame, refresh the header `WOOD {n}` text and the spec panel cost colour. Already exists in `Update` / `RefreshDetailCost`; just point it at the new TMP refs.
- **Build menu re-open after placement**: `s_reopenAfterFinish` flag still respected — tutorial / scripted flows that call `RequestReopenAfterPlacement()` keep working.
- **Build menu category enum auto-detection**: `BuildTabRow` logic preserved, only styling changes.
- **Save behaviour**: neither UI saves runtime state. No save-system changes needed.

### Files changed

- `Assets/3 - Scripts/UI/CyanScannerPalette.cs` (new) — shared colour constants
- `Assets/3 - Scripts/UI/ScannerFrame.cs` (new) — bracket + grid helpers
- `Assets/3 - Scripts/Fishing/FishingdexManager.cs` — rewritten to procedural construction
- `Assets/3 - Scripts/Fishing/FishingdexEntryUI.cs` — kept (still wraps a row), but the manager instantiates rows procedurally; the prefab is no longer required
- `Assets/3 - Scripts/Building/BuildMenuUI.cs` — palette swap, layout collapse (list + detail merge), brackets + grid
- `CLAUDE.md` — note that fishingdex is now procedural like build menu; note shared `CyanScannerPalette` / `ScannerFrame` location

### Testing notes

Unity Play-mode only. Manual test plan:

**Fishingdex:**
1. Open with B, scroll list, click rows → preview + spec update in place (no list/detail panel swap).
2. Pause/resume play mode → no NullReference from missing inspector refs.
3. Sell flow: trigger `OpenForSell` from FishMarketNPC → action button reads `ADD FISH`, picking a fish + clicking it fires the callback + returns to FishMarket's sell panel.
4. Cook flow: from a bonfire → same as sell, action button reads `ADD FISH`.
5. EAT RAW path: in Browse mode, click EAT RAW on a rare fish → trip starts (existing RawFishTripController behaviour unchanged).
6. Empty inventory: list shows "NO ENTRIES" placeholder; preview blank; spec empty; action buttons disabled.

**Build menu:**
1. Open with N, switch tabs → list filters; preview/spec clear on tab change.
2. Pick a row → preview + spec update; wood cost reflects affordability.
3. Place button disabled when unaffordable; enabled when affordable.
4. Click Place → ghost placement starts as today.
5. Wood cost live-update: drop wood (via cheat / chopping a tree) → header `WOOD {n}` + spec `COST` colour reflect the new total within one frame.
6. Re-open after placement (cabin tutorial flow): `RequestReopenAfterPlacement()` still re-pops the menu after placement.

### Risks / known gotchas

- The cyan palette must use `Color32` not `Color` for the `Image.color` setters in case any of the helper code is sensitive (the rest of the codebase uses `Color32` for UI; matching avoids implicit gamma conversion quirks).
- `ScannerFrame.AddBlueprintGrid` uses `RectMask2D` with tiled `Image`s. The pause menu uses the same technique elsewhere — confirmed it doesn't fight the `RectMask2D` already on the existing save panel.
- The new procedural fishingdex canvas may render above or below existing HUD elements depending on `sortingOrder`. Use the same value as the build menu (`200`) so they're consistent. The pause menu (`1000`) and save UI (`2000`) still draw on top.
- Empty `buildables` array: today's code handles it gracefully (no tabs, no cards). The new code path needs to do the same — explicit early-out before building the tab row.
- The bracket helper builds 4 child Images per use. With 2 panels' previews + 2 list-row brackets per selected row, that's ~10 brackets max simultaneously. Negligible draw-call cost; below the threshold where batching becomes a worry.
- TMP font: keep using whatever font is in use elsewhere (TMP default LiberationSans SDF). The mockups *look* monospaced because of letter-spacing + all-caps, not because of a literal monospace font. No new font asset needed.
