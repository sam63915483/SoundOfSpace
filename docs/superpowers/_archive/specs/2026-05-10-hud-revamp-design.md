# HUD Revamp (Vitals + Currencies) — Design

## Goal

Visual overhaul of two on-screen HUD areas, no functional changes:

1. **Top-left vitals** — `HEALTH`, `HUNGER`, `THIRST`, `SHIP POWER` (driven by `ResourceManager`). Currently a scene-authored layout in `ResourceHUD`.
2. **Bottom-right currencies** — `MONEY`, `WOOD`, `AMMO` (driven by `PlayerWallet`, `WoodInventory`, `PistolController`). Currently a procedural orange-on-navy card in `PlayerWallet`.

Visual target (from approved mockup):
- **Vitals** in a beveled HUD card matching the tutorial pill / Press-F prompt language — clipped top-left and bottom-right corners, cyan LED accent bar on the left edge, dark navy fill, a `// VITALS` header tag, then four horizontal stat rows. Each row: full-text label (no abbreviations), gradient fill bar with soft outer glow, percent readout.
- **Currencies** as separate rounded neon chips, one per stat, stacked along the bottom-right. Each chip has a soft cyan border-glow; the value text is accent-colored per resource (gold for money, brown for wood, mint for ammo) with matching glow.

## Non-goals

- No changes to data sources (`ResourceManager` percents, `PlayerWallet.Money`, `WoodInventory.Wood`, `PistolController.CurrentAmmo`).
- No save/load impact — pure render.
- Warning-audio + low-resource pulse behavior unchanged.
- Solar-charging indicator unchanged.
- The legacy scene-bound `ResourceHUD` script and its serialized bar references can stay — the new `VitalsHUD` simply disables the legacy GameObject on Start so they don't double-render. A future scene cleanup can remove the orphans entirely.

---

## Part 1 · `VitalsHUD` (top-left, new singleton)

### Component

New file: `Assets/3 - Scripts/Survival/VitalsHUD.cs`. Modeled on `PlayerWallet`:

- `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` auto-create, skipped in `MainMenu` scene.
- Standard singleton — `Instance` / `Awake` / `OnDestroy` per the CLAUDE.md template.
- `DontDestroyOnLoad`, builds its own `Canvas` (ScreenSpaceOverlay, sortingOrder = 25 — between hotbar at 50 and the resource pause menu).
- Anchored **top-left**, ~20 px from the top + left edges.

### Visual specs

Card:
- Beveled clipped corners (top-left + bottom-right, 14 px bevel — same as the tutorial pill / Press-F prompt) using the same procedural sprite-gen pattern. Sprite generation extracted to a shared helper so we don't depend on TutorialUI's internals.
- Background: dark navy gradient (`#081220E6` → `#0A1828F2`).
- Border outline: cyan, `#78C8FF73`.
- Cyan LED accent bar on the left edge (`#5CC8FF`, 3 px wide), same as the tutorial pill.
- `// VITALS` header tag in cyan (`#5CC8FFD9`), font size 10, letter-spacing 6 px.
- Card width 290 px, padded 14 / 20 / 16 / 26 (top / right / bottom / left).

Stat rows (4 total, top-to-bottom: `HEALTH` → `HUNGER` → `THIRST` → `SHIP POWER`):
- Layout: `[label 92 px] [fill bar flex] [pct 36 px]`
- Label: full text (no abbreviations), 11 px bold, white `#EAF6FF`, letter-spacing 1.5.
- Bar track: 9 px tall, `#0F192A`, 1 px cyan border (`#78C8FF38`), inner shadow.
- Bar fill: per-stat gradient with a soft outer-glow `Shadow` component in the row color:
  - Health: `#FF6B9F` → `#E63952` (existing `GalaxyHudKit.HealthA` / `HealthB`).
  - Hunger: `#FFC477` → `#FF8A4C` (`HungerA` / `HungerB`).
  - Thirst: `#7BE2FF` → `#4A8BFF` (`ThirstA` / `ThirstB`).
  - Ship power: `#B88CFF` → `#C94FFF` (`ShipPowA` / `ShipPowB`).
- Bar fill drive: `RectTransform.localScale.x = percent` (matches the original ResourceHUD approach).
- Percent text: 11 px bold white, right-aligned, "78%" format.
- Row spacing 6 px.

Pulse-when-low (preserved):
- When percent < `pulseThreshold` (0.25, configurable), the fill `Image`'s alpha pulses with `Sin(Time.time * pulseFrequency * 2π)` mapped to `[0.3, 1.0]`.
- Recovery: alpha snaps back to 1 when above threshold.

Warning audio (preserved):
- When percent first crosses below `urgentThreshold` (0.10), play a one-shot `warningClip` from a local `AudioSource`. One latch flag per stat — re-arms when the percent recovers above the threshold.

Charging indicator:
- Solar panel charging label re-rendered as a 5th row that appears only when `SolarPanelCharger.IsCharging` is true. Same beveled style — small green/cyan glyph + "CHARGING" text. Card height grows to accommodate it; otherwise the row is collapsed.

### Disabling the legacy ResourceHUD

On `Start`, `VitalsHUD` searches the active scene for any existing `ResourceHUD` component and disables the root Canvas of its hierarchy (or just its GameObject if no parent canvas). This prevents the old bars from double-rendering alongside the new card. The disable is one-shot (no per-frame churn).

Legacy script + serialized fields stay in the codebase — they're harmless once their UI is hidden. A scene-cleanup pass can remove the GameObject entirely in a follow-up.

### `MainMenuController.EnsureGameplaySingletons`

Add a seeding block for `VitalsHUD`, matching the existing pattern for `PlayerWallet`, `TutorialUI`, `Hotbar`, etc.

---

## Part 2 · `PlayerWallet` rewrite (bottom-right currencies)

### Component

Modify `Assets/3 - Scripts/Player/PlayerWallet.cs`. Keep the singleton + data API intact (`AddMoney`, `SpendMoney`, `SetMoney`, `Money`). Replace `CreateCornerHUD` and its rows with the chip layout.

### Visual specs

Layout:
- Canvas anchored bottom-right, ~24 px from each edge.
- Vertical stack of chips, top-to-bottom: `MONEY` → `WOOD` → `AMMO` (when visible).
- 8 px gap between chips.
- All chips right-aligned to the stack's right edge — they share a common right edge.

Each chip:
- Min width 170 px, height ~38 px.
- Padding 8 px (top / bottom) × 18 px (left / right).
- Background: gradient `#142C48F2` → `#0E1E34F2`.
- Border: 1.5 px solid cyan `#78C8FF8C`.
- Border radius: 14 px (rounded — matches the hotbar's rounded slots, NOT the beveled vitals card).
- Outer cyan glow: simulated via a `Shadow` component, color `#5CC8FF47`, distance 0 (acts as bloom).
- Layout: `[label] [flex spacer] [value]` — label left-aligned, value right-aligned.
- Label text: 10 px bold, color `rgba(168,210,235,0.8)`, letter-spacing 3 px (uppercase: "MONEY", "WOOD", "AMMO").
- Value text: 22 px bold, accent-colored per resource, with a soft outer glow via `Shadow`:
  - Money: `#FFC24A` (gold), glow same hue at ~0.4 alpha. Format: `$420`.
  - Wood: `#D4A06B` (warm brown), matching glow. Plain integer.
  - Ammo: `#88DCAA` (mint), matching glow. Plain integer.

Ammo chip auto-show/hide:
- Hidden by default.
- Shown when `PistolController.IsEquipped == true` (same condition as today).
- Toggling the chip's `GameObject.SetActive` repositions the stack automatically — chips below the toggled item shift up/down by `chipHeight + gap = 46`. Since AMMO is the bottom chip, no other chips move; only the stack's overall height changes.

Existing change-detection per-frame allocation guards (`_lastWoodSeen`, `_lastAmmoSeen`) carry over unchanged — only update the text when the value changes, exactly like today.

### Behavior preserved

- `RefreshMoney`, `RefreshWood`, `Update` polling for wood/ammo all kept.
- Public API signatures unchanged.
- Save/load wiring unchanged (the wallet is already saved and restored).

---

## Shared procedural sprites

Both `VitalsHUD` (beveled card) and `PlayerWallet` (rounded chips) need procedural sprites. Reuse approach:

- **`HotbarBeveledPanel` / `HotbarBeveledPanel.GetOutlineSprite`** — already exists in `Hotbar.cs` and produces the beveled panel + outline used by the hotbar's name plate. Move these to a shared static helper class at `Assets/3 - Scripts/UI/UIPanelSprites.cs` so both the hotbar name plate and the new vitals card can use them. (Hotbar.cs gets a one-line redirect; behavior unchanged.)
- **Rounded rectangle for chips** — generate a procedural rounded-rect sprite locally inside `PlayerWallet.cs` (same shape as the slot ring but FILLED, not hollow — chips are solid filled pills with a separate border ring).

The extraction of `HotbarBeveledPanel` into a shared util is the only meaningful refactor — it avoids three copies of the same procedural panel-gen across `Hotbar`, `InteractPromptUI`, and the new `VitalsHUD`.

---

## File-change summary

**New:**
- `Assets/3 - Scripts/Survival/VitalsHUD.cs`
- `Assets/3 - Scripts/UI/UIPanelSprites.cs` (extracted from `Hotbar.cs:HotbarBeveledPanel`)

**Modified:**
- `Assets/3 - Scripts/Player/PlayerWallet.cs` — `CreateCornerHUD` rebuilt as the chip stack; row build helpers replaced.
- `Assets/3 - Scripts/UI/Hotbar.cs` — delete local `HotbarBeveledPanel` helper, redirect callers to `UIPanelSprites`.
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `VitalsHUD` in `EnsureGameplaySingletons`.

**Not modified (intentionally):**
- `Assets/3 - Scripts/Survival/ResourceHUD.cs` — kept as-is for backward compat. Its scene UI is disabled by the new `VitalsHUD` on Start.

## Coding-convention compliance (per `CLAUDE.md`)

- **Singleton pattern:** `VitalsHUD` follows the standard `Instance` / `Awake` / `OnDestroy` shape used by `TutorialUI`, `Hotbar`, `PlayerWallet`, `InteractPromptUI`.
- **No `Resources.Load` in user code:** font lookup uses the same `Techno SDF` → fallback chain documented in `TutorialUI` / `InteractPromptUI`. Shared via the helper.
- **No `FindObjectOfType` per-frame:** `VitalsHUD.Start` finds `ResourceHUD` once (to disable). `ResourceManager.Instance` and `SolarPanelCharger` follow the same lazy-cache pattern already in `ResourceHUD`.
- **Change-detection on text writes:** `VitalsHUD` only updates label text + bar scale when the percent changes by ≥0.5 % (or when a pulse/warning state flips) to avoid per-frame string allocation.
- **No forbidden zone touched:** atmosphere / planet generation / save system untouched.

## Risks / open questions

- **Sort-order conflicts:** `VitalsHUD` canvas at `sortingOrder = 25` sits above the hotbar (50) — wait, 25 < 50, so the hotbar renders OVER the vitals. They occupy different corners so no overlap, but if any future UI element spans the top-left edge it'll need explicit z-ordering.
  → Decision: 25 is safe because the top-left zone is reserved for vitals. If a tutorial pill or similar drops there, it sorts above (TutorialUI at 500).
- **Charging indicator UX:** currently the `chargingLabel` is a separate scene-bound `TMP_Text`. In the new card, it appears as an optional 5th row. The legacy `chargingLabel` ref on `ResourceHUD` becomes orphaned — same fate as the bar/label refs. Acceptable.
- **Font fallback divergence:** `VitalsHUD` and the new `PlayerWallet` chip values use the same font chain as `TutorialUI` / `InteractPromptUI` (Techno SDF → … → LiberationSans). Keep this consistent so the entire HUD family reads as one.
- **Pulse-and-warning state on scene swap:** new `VitalsHUD` is `DontDestroyOnLoad`, but `ResourceManager` may or may not be — verify the `_warned` latches don't carry stale state between gameplay-restart cycles.
