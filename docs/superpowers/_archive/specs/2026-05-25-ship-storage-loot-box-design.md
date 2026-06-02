# Ship storage (loot box) — design

**Status:** approved, ready for implementation plan
**Date:** 2026-05-25

## Summary

Add a per-instance **storage container** to the `LootBox` prefab the user attached to `SHIP44`. Player walks into its trigger, sees a "Press F to open storage" prompt, presses F to open a 20-slot inventory panel that matches the **Scanner blueprint** UI style (the same `CyanScannerPalette` + `ScannerFrame.AddBrackets` look used by `FishingdexManager`, `BuildMenuUI`, and `Alien7Vendor`'s shop). Player movement and movement-driven camera FX freeze while the panel is open; the mouse cursor unlocks for drag-and-drop. Items move between the hotbar and storage via LMB drag, RMB drag-one, and Shift+LMB quick-move. Contents persist through the save system per loot box.

## Scope

**In:** the 5 hotbar equippables (`WaterBottle`, `FishingRod`, `Guitar`, `Axe`, `Pistol` — `count=1`) and the 3 stackable resources (`Wood` max 100, `Crystal` max 20, `SpaceDust` max 100). These are exactly the `ItemId` values the current `Hotbar` already supports.

**Out:** `FishInventory` entries (per-fish weight/colour metadata makes them unfit for the `(ItemId, count)` slot shape), money, ship power, jetpack fuel, notes, build-menu blueprints, cassette state. Storage-to-storage drag is also out — only one storage panel is ever open at a time.

## UX

### Open/close

- The loot box has a trigger `BoxCollider` (already placed by the user). While the player is inside the trigger, `InteractPromptUI` displays **"Press F to open storage"**.
- Open is gated by: `Ship.FindPilotedShip() == null`, `!PlayerController.isInDialogue`, `!PlayerController.isMapOpen`, `!PlayerController.isInStorage`, `!PlayerPhoneUI.IsOpen`. Failing any gate hides the prompt.
- Pressing F opens `StorageUI` for that specific loot box. Re-pressing F (or pressing Esc, or walking out of the trigger) closes it.
- On close while the cursor holds an item: the held stack returns to the slot it came from. If that slot was somehow filled by something else mid-drag (defensive — shouldn't occur in a single-open-at-a-time UI), spill to the first empty slot in the source container; if no empty slot is available, **block the close** until the player manually resolves the held item.

### While open

- `PlayerController.isInStorage = true`. Player movement input ignored.
- Camera FX modules that already check `isInDialogue || isMapOpen` add `|| isInStorage` to their gates — covers headbob, strafe tilt, FOV kick, and any other movement-driven module. (Full audit list in the implementation plan.)
- Cursor unlocks (`Cursor.lockState = None`, `Cursor.visible = true`) and the scanner-blueprint panel is the only thing accepting input.
- Hotbar `UnequipAll()` fires on the open transition (mirrors how dialogue and the phone UI already force-unequip), so the storage isn't fighting an active swing animation.
- The hotbar canvas itself stays visible inside the storage panel (as the bottom row of the modal) so the player can drag between the same UI.

### Drag-and-drop interaction model

A "slot" can be in one of the panel's 20 storage cells or one of the 5 hotbar cells; both are visible while storage is open.

| Input | On slot with item | On empty slot | On slot with cursor held |
|---|---|---|---|
| **LMB** | Pick up full stack onto cursor | (no-op) | Deposit: same item with room → merge as much as fits, remainder stays on cursor. Different item → swap (cursor now holds the slot's previous contents). Empty → place. |
| **RMB** | Pick up exactly 1 item from the stack | (no-op) | Drop 1 from cursor if same item with room. |
| **Shift + LMB** | Quick-move to the other container (storage→hotbar or hotbar→storage). Stackable resource: uses fill-existing-stacks-then-spill logic. Equippable: swaps with first empty slot in the other container. | (no-op) | (cursor must be cleared first) |

`Shift + RMB` is **not** bound (intentional — no Minecraft-style "drag-paint" behaviour in this scope).

### Stack-merge math (for LMB deposit on same-item slot)

```
room       = StackMax(id) - slot.count
moved      = min(room, cursor.count)
slot.count += moved
cursor.count -= moved
if cursor.count == 0: clear cursor
```

For Shift+LMB on a stackable resource source slot, the destination container's `AddResource`-equivalent (fill-existing-then-spill) runs until the source is empty or the destination is full; leftover stays in the source.

## Visuals

Match `CyanScannerPalette` and `ScannerFrame.AddBrackets` exactly (this is the **Scanner blueprint** mockup from the brainstorm — option E at `http://127.0.0.1:8765/index.html` during this session).

- Centered modal, ~540 px wide.
- Panel background: `CyanScannerPalette.PanelBg` (`#0A1228` @ α 0xF0).
- 1 px border in `CyanScannerPalette.PanelBorder` (`#1C3A5C`).
- Four corner brackets via `ScannerFrame.AddBrackets(rt, length=14, thickness=2)` in `CyanScannerPalette.Accent` (`#5BD8FF`).
- Faint cyan blueprint grid background via `ScannerFrame.AddBlueprintGrid(rt, gridSpacing=24)` — alpha is already baked into `CyanScannerPalette.GridLine`.
- Header row: title **"Cargo Hold"** in `CyanScannerPalette.TextBright` (letter-spacing ~4 px, 18 pt) on the left; tagline `"Ship 44 · Bay 1"` (or `"Ship <N> · Bay 1"` for purchased ships) in `CyanScannerPalette.TextMuted` on the right.
- Horizontal divider in `CyanScannerPalette.InnerDivider` between header and grid.
- Section label `"STORAGE · 20 SLOTS"` in `CyanScannerPalette.Accent`, 10 pt, letter-spacing 3 px, with thin divider hairlines flanking it.
- Storage grid: 5 columns × 4 rows of 84 px slots, gap 6 px.
- Section label `"HOTBAR"` in same style.
- Hotbar grid: 5 columns × 1 row of 84 px slots, gap 6 px. Same visual treatment as storage slots (so the player reads them as the same kind of cell).
- Footer: keybind hints on the left (`LMB drag stack · RMB drag one · ⇧+LMB quick-move`) and `F close` on the right, in `CyanScannerPalette.TextMuted` 10 pt. Hint keys rendered as outlined "kbd" pills using `CyanScannerPalette.Accent` text on a transparent `PanelBorder`-outlined background.
- Slot fill: `CyanScannerPalette.InnerBg` (`#0C1A32`), 1 px `PanelBorder` outline, 3 px rounded corners.
- Empty slot: lower-alpha fill (`InnerBg` @ 40%) and lower-alpha border.
- Active/hovered slot: `CyanScannerPalette.SelectionFill` (`#143055`) background, `Accent` border, soft `Accent` glow (`box-shadow`-equivalent 12 px outer glow at ~35% alpha).
- Cursor-held item: a 52 px follower rect parented under the StorageUI canvas, raycast disabled, follows `Input.mousePosition`. Same slot styling, `Accent` 1 px border, soft `Accent` outer glow.

Stack counts render bottom-right in the slot in white with a 1 px drop shadow (same as the existing hotbar). Equippables show no count.

Sortingorder: `StorageUI` canvas at **900** — above the hotbar canvas (830, see `Hotbar.BuildUI`) and the camera-effect overlay canvases (800–820), below the pause menu (1000) and the save UI (2000).

## Architecture

### New files

| File | Type | Purpose |
|---|---|---|
| `Assets/3 - Scripts/Ship/LootBox.cs` | MonoBehaviour | One per loot-box prefab instance. Trigger collider + interact prompt. Holds the `Slot[20]` data and a stable `boxId`. Registers/deregisters with `StorageRegistry` on enable/disable. |
| `Assets/3 - Scripts/UI/StorageUI.cs` | Singleton MonoBehaviour | Procedural Canvas; owns the cursor-held item state and routes LMB / RMB / Shift+LMB clicks to `SlotOps`. Built via the existing auto-singleton pattern (`RuntimeInitializeOnLoadMethod` with `MainMenu` early-return). |
| `Assets/3 - Scripts/UI/SlotOps.cs` | `static class` | Pure functions: `PickUpFull`, `PickUpOne`, `Deposit`, `QuickMove`. Operate on `Hotbar.Slot[]` references plus a `CursorState` struct. No Unity dependencies beyond `Mathf`. |
| `Assets/3 - Scripts/Ship/StorageRegistry.cs` | `static class` | Tracks live `LootBox` instances via `OnEnable`/`OnDisable`. Exposes `IReadOnlyList<LootBox> All` and the `IsItemAnywhere(ItemId)` helper used by `Hotbar.DetectAcquisitions`. Same shape as `EnemyController.ActiveEnemies` / `SpawnedTree.AllTrees`. |

### Reused types

- `Hotbar.Slot` (`{ ItemId id; int count; }`) is the slot data shape for storage as well — same struct, same `StackMax`, same nullability convention (`id == ItemId.None && count == 0` = empty).
- `HotbarSlotSave` (already in the save schema) is the per-slot save record. Storage just stores a `List<HotbarSlotSave>` of 20.

### Modified files

| File | Change |
|---|---|
| `Hotbar.cs` | Patch `TryAddItem` to skip add when `StorageRegistry.IsItemAnywhere(id)` returns true — prevents stored equippables from being auto-re-added to the hotbar each frame. Add an `OnStorageOpened` hook the `StorageUI` calls so the hotbar fires `UnequipAll()` on the open transition. The existing eviction loop in `DetectAcquisitions` needs no change (it only evicts on `IsUnlocked = false`, which storage doesn't affect). |
| `PlayerController.cs` | Add `public static bool isInStorage`. Add it to the movement-input gate (same shape as `isInDialogue` / `isMapOpen`). |
| Camera FX modules (full list during plan): | Each module that currently checks `PlayerController.isInDialogue` or `PlayerController.isMapOpen` adds `|| isInStorage`. Headbob and strafe-tilt are inside `CameraTransformFX`; FOV kick is inside `CameraFOVFX`. The plan must enumerate every module and confirm. |
| `SaveData.cs` | Add `[Serializable] class StorageSave { public string boxId; public List<HotbarSlotSave> slots; }` and `public List<StorageSave> storages = new()` field on `SaveData`. |
| `SaveCollector.cs` | Add `CaptureStorages` (walks `StorageRegistry.All`) called from `Capture`. Add `ApplyStorages` called from `Apply` **after** `ApplyExtraShips` and **after** `ApplyEquipment`. Match by `boxId`; saved boxes whose ID isn't found in the scene are dropped silently. |
| `MainMenuController.cs` | Add `StorageUI` to `EnsureGameplaySingletons` (the singleton skips MainMenu via the standard early-return, but must be seeded before `LoadScene` per the build-time trap documented in `CLAUDE.md`). |
| `LootBox.prefab` | User attaches `LootBox.cs` to the existing prefab (no script reassignment needed in the spec — the user does the inspector wiring). |

### Stable boxId scheme

`LootBox.Awake` computes its own ID once and stores it (the value is captured into the save record and recomputed identically on every load, so no per-instance persistence is needed beyond what's in `StorageSave`):

```
1. Walk up transform.parent.
2. If a BoughtShip ancestor is found → id = "BoughtShip" + bs.shipNumber + "/" + relativePathFromShip
3. Else if a Ship ancestor is found → id = "OriginalShip/" + relativePathFromShip
4. Else → id = absolute scene path
```

`relativePathFromShip` is the `/`-joined hierarchy of GameObject names from the ship root down to this loot box (exclusive of the ship root). Example: a loot box at `--- Player & Ship ---/SHIP44/CargoBay/LootBox` becomes `"OriginalShip/CargoBay/LootBox"`.

This survives:
- Saving/loading the scene's original ship (ID is path-derived, no per-instance state).
- Saving/loading a purchased ship (`BoughtShip.shipNumber` is already persisted in `ExtraShipSave` and re-assigned in `ApplyExtraShips`; the loot box on that ship gets the same ID it had at save time).
- Re-parenting a loot box within the same ship (ID changes — by design, the new location is treated as a different storage).

### Player / camera gate flow

```
StorageUI.Open(LootBox box):
    if (isOpen) return
    _active = box
    PlayerController.isInStorage = true
    Cursor.lockState = CursorLockMode.None
    Cursor.visible = true
    Hotbar.Instance.OnStorageOpened()   // forces UnequipAll
    BuildOrShowCanvas(box)

StorageUI.Close():
    if (!isOpen) return
    if (_cursor.IsHeld) ReturnHeldOrBlock()
    _active = null
    PlayerController.isInStorage = false
    Cursor.lockState = CursorLockMode.Locked
    Cursor.visible = false
    HideCanvas()
```

### Drag/drop dispatch

Each slot UI element gets a small `StorageSlotView` MonoBehaviour with `IPointerClickHandler`. On click, it dispatches to `StorageUI.OnSlotClick(slotView, button, shift)`. The UI layer just forwards container + index + modifiers to `SlotOps`. All actual slot mutation logic lives in `SlotOps` and is pure — easy to unit-test if we ever add tests.

```
SlotOps.HandleClick(
    Slot[] source,        // hotbar.slots OR lootBox.slots
    int    sourceIdx,
    ref CursorState cursor,
    MouseButton btn,
    bool shift,
    Slot[] otherContainer // used only for quick-move
)
```

Quick-move uses the same fill-existing-then-spill logic that `Hotbar.AddResource` already implements (for resources) or a simple "place in first empty slot, otherwise swap with first slot containing the same equippable" (for the 5 equippables — though equippables don't stack, so "first empty" is the only case in practice).

## Save & load

### Schema additions

```csharp
[Serializable]
public class StorageSave {
    public string boxId;
    public List<HotbarSlotSave> slots = new List<HotbarSlotSave>();
}

// In SaveData:
public List<StorageSave> storages = new List<StorageSave>();
```

### Apply order

The actual `SaveCollector.Apply` sequence (abbreviated, from `SaveCollector.cs:701-756`):

```
 ... (CelestialBodies, Tutorial, NPCs, EarlyGame, Notes, BuildMenuLock, Compass)
 6. ApplyResources / Wallet / Wood / Crystals / FishInventory / Equipment / WorldFlags
 7. ApplyShipDamage / ApplyShipTransform
 8. ApplyExtraShips           // spawns purchased ships + their LootBox children
 9. ApplySpaceDust
10. ApplyHotbar               // wipes hotbar then restores saved slot layout
11. ApplyPlayerTransform
12. ApplyBuildings / ApplyLooseParts
13. ApplyEnemies
14. ApplyHeldItem
15. ApplyAIState / Cassette / Flashlight / BonusTutorial / MapTutorial
16. ApplyAlienKills / TreesMined / MushroomsConsumed / CrystalsMined
```

`ApplyStorages` slots in **immediately after `ApplyHotbar` (step 10)** — both deal with `Slot[]` inventory layouts, and grouping them keeps the order obvious. Constraints satisfied:

- **After `ApplyExtraShips`** (step 8): `LootBox` children of purchased ships exist and have their `boxId` computable from `BoughtShip.shipNumber`.
- **After `ApplyHotbar`** (step 10): the hotbar slot layout is canonical before storage decides what to display. Not strictly necessary but consistent.
- **Duplicate-item risk is already handled** by `Hotbar.ApplySlotsFromSave` clearing all hotbar slots before restoring them — even if `Hotbar.Update` ran on the pre-Apply frame and auto-added an item, `ApplyHotbar` wipes that, then `ApplyStorages` restores the box. After Apply finishes, `Hotbar.TryAddItem`'s storage check prevents re-pulling on subsequent frames.

### Idempotency

Saving and re-loading the same state must produce the identical `StorageSave` list, in the same order, with the same `boxId` values. `LootBox.boxId` is path-derived (no random/timestamp components), and `StorageRegistry.All` order tracks registration order (deterministic given scene composition).

## Edge cases & non-obvious behaviours

- **Player walks out of the trigger while the panel is open**: panel closes (same as F press). If cursor holds an item, it returns to source first.
- **Save fires while panel is open**: captured `StorageSave` reflects whatever is currently in the box's `Slot[]`. The cursor-held item (transient UI state) is **not** saved; on load, the panel reopens empty-handed. Practical effect: if the player saves mid-drag and reloads, the held stack is back in its source slot (because the box slot was already cleared when the player picked it up). To avoid losing the held stack, the autosave should not fire while `isInStorage` is true.
- **Hotbar gets an `OnResourceChanged` event for `Wood` / `Crystal` / `SpaceDust`**: storage operations should NOT fire these (they fire only when the **hotbar** changes, since the HUD facades — `WoodInventory`/etc. — subscribe to it for HUD updates). Storage-only operations should silently mutate `LootBox.slots` and refresh only the storage panel.
- **Purchased ship sold/respawned with different `shipNumber`**: its `boxId` changes; the old saved storage record won't match and is dropped silently. (No "ship sell" flow exists yet, so this is theoretical, but worth noting.)
- **Storage on a destroyed `LootBox`**: if a future feature lets the player destroy a loot box, that box's `StorageSave` should also be dropped from the save. Out of scope here — the loot box prefab is permanently attached to ships.
- **Pause menu / map / dialogue / phone opens while panel is open**: those systems all set their own `isInDialogue`/`isMapOpen`/`IsOpen` flags. The storage gate checks them on every frame in `OnTriggerStay`. If one becomes true while storage is open: `StorageUI.Update` notices and force-closes (returning cursor-held item to source first).
- **Multiple loot boxes overlap the player simultaneously**: only the most-recently-entered one shows its prompt; only that one opens on F. (Implementation: `LootBox.OnTriggerEnter` registers itself as the player's "current" candidate; `OnTriggerExit` clears the candidate only if it's still this box.)

## Out of scope (intentional)

- Searchable/filter UI inside storage.
- Tooltips on item hover.
- Storage-to-storage drag (only one panel open at a time; the hotbar is always the "other" container).
- Mouse-wheel stack size adjustment.
- "Take all" / "deposit all" buttons.
- Right-click drag (RMB is single-click pickup-one).
- Sounds on pick-up / deposit (can be added later — fish flop, wood thunk, crystal chime, dust pour).
- Animated open/close (instant show/hide for now).
