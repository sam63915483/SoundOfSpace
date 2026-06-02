# Hotbar resource stacks — design

**Date:** 2026-05-25
**Author:** Sam McNeil (with Claude)
**Branch context:** `feature/phone-ai-revamp`

## Goal

Move the three collectible resources (**wood**, **crystal**, **space dust**) out of the top-left HUD chips and into the player's 5-slot hotbar as stackable items. Equipping a resource slot is a no-op for now (highlight + name plate only); future work will add ship storage and transfer-between-containers, which this design leaves a clean seam for.

## Non-goals

- Ship storage (future).
- Real resource icons (procedural color swatches now; textures later).
- Action-on-equip behavior for resources (no-op for this slice).
- Re-balancing stack caps after first playtest (caps are wood=100, crystal=20, dust=100, set by the design brief).

## Design decisions (from brainstorming)

| Question | Decision |
|---|---|
| Overflow when hotbar full | Mine still completes for what fits; brief `"INVENTORY FULL"` HUD popup; excess is lost. |
| Stack rule | Minecraft-style — multiple stacks of the same resource allowed across slots; each slot caps at the resource's stack size. |
| Spending | Transparent sum across all stacks; drain leftmost-first; building code untouched. |
| Tool pickup with hotbar full of stacks | Tools never displace stacks (corner case for now — 5 tools + 3 resources can all fit; revisit if it becomes painful). |
| Resource singletons (`WoodInventory` etc.) | Stay as the public API; become thin facades over the Hotbar. |
| Visual placeholder | Color-coded square (wood=brown, crystal=cyan, dust=violet — matching old chip palette) + stack count + name plate. |
| Save | Save full slot layout AND keep legacy totals as fallback for old saves. |

## Data model

`Hotbar` currently uses `ItemId[] slots`. Replace with:

```csharp
public enum ItemId {
    None, WaterBottle, FishingRod, Guitar, Axe, Pistol,
    Wood, Crystal, SpaceDust          // ← new
}

struct Slot { public ItemId id; public int count; }
Slot[] slots = new Slot[5];
```

Stack caps (hard-coded constants on `Hotbar`):
- `Wood` → 100
- `Crystal` → 20
- `SpaceDust` → 100

**Invariants:**
- `slot.id == None ⇔ slot.count == 0`
- Tool slots always have `count == 1` (count is ignored in UI for tools).
- Resource slots always have `1 ≤ count ≤ StackMax(id)`.

## Resource API on `Hotbar`

```csharp
int  GetResourceTotal(ItemId resource);
int  AddResource(ItemId resource, int amount);   // returns leftover (>0 means overflow)
bool SpendResource(ItemId resource, int amount); // all-or-nothing
void SetResourceTotal(ItemId resource, int total); // save-load only
int  StackMax(ItemId resource);

event System.Action<ItemId> OnResourceChanged;
```

**Add algorithm:**
1. Walk slots left-to-right. For each slot with `id == resource && count < StackMax`, add up to `StackMax - count`, decrement amount remaining.
2. If amount remains, walk slots left-to-right for `id == None`, create new stack with up to `StackMax`, decrement amount.
3. Return any remaining amount (overflow).
4. Fires `OnResourceChanged(resource)` if any slot changed.

**Spend algorithm:**
1. Sum across stacks. If `sum < amount`, return `false` (no partial spend).
2. Walk left-to-right: for each stack of `resource`, drain `min(stack.count, remaining)`; if fully drained, set `id=None, count=0`.
3. Return `true`. Fires `OnResourceChanged(resource)`.

**SetResourceTotal (save-load fallback):** clear every slot where `id == resource`, then `AddResource(resource, total)`. Used only by the legacy-save path.

## Resource singleton facades

`WoodInventory`, `CrystalInventory`, `SpaceDustInventory` keep their static API. Internals delegate to `Hotbar`:

```csharp
public int Wood => Hotbar.Instance != null
    ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Wood) : 0;

public void AddWood(int amount) {
    if (amount <= 0 || Hotbar.Instance == null) return;
    int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.Wood, amount);
    if (leftover > 0) InventoryFullPopup.Show();
    OnChanged?.Invoke();
}

public bool SpendWood(int amount) {
    if (Hotbar.Instance == null) return false;
    bool ok = Hotbar.Instance.SpendResource(Hotbar.ItemId.Wood, amount);
    if (ok) OnChanged?.Invoke();
    return ok;
}

public void SetWood(int amount) {
    if (Hotbar.Instance == null) return;
    Hotbar.Instance.SetResourceTotal(Hotbar.ItemId.Wood, amount);
    OnChanged?.Invoke();
}

public bool Has(int amount) => Wood >= amount;
```

The singletons subscribe to `Hotbar.OnResourceChanged` to forward their existing `OnChanged` event, so consumers that listen to `WoodInventory.Instance.OnChanged` keep working without changes.

**No state in the singleton itself** — `Hotbar` is the sole source of truth. The `RuntimeInitializeOnLoadMethod` auto-create stays (so the singleton object exists for callers that hold a reference), but its only job is event forwarding.

**Consumers that keep working unchanged** (verified by grep — these are the existing call sites):
- `BuildMenuUI.cs` (recipe affordability checks)
- `GhostPlacement.cs` (consumes wood on place)
- `BonusTutorial.cs` (gather-wood-60 step)
- `RandomAlienDialogue.cs` (incidental queries)
- `SpaceNet.cs` (drains buffered dust into player)
- `SpaceDustSellUI.cs` / `Alien7Vendor.cs` (vendor spend)
- `SpawnedTree.cs` (chop → AddWood)
- `SpawnedCrystal.cs` (mine → Add)
- `SaveCollector.cs` (set on load — see Save section)
- AI knowledge (`IntentRouter.cs`, `LLMService.cs`, `AIMemoryStore.cs`) reads totals only

## Equip behavior

Equipping becomes **slot-driven** rather than registry-driven.

New internal state on `Hotbar`:
```csharp
int _equippedSlot = -1;   // which slot index is selected (-1 = none)
```

`ToggleSlot(idx)`:
- If `slots[idx].id == None` → call `UnequipAll()`, `_equippedSlot = -1`.
- If `slots[idx]` is a tool and that tool is currently equipped → `UnequipAll()`, `_equippedSlot = -1` (toggle-off behavior preserved).
- If `slots[idx]` is a tool not equipped → `UnequipAll()`, call that tool's `ForceEquip`, `_equippedSlot = idx`.
- If `slots[idx]` is a resource → `UnequipAll()`, `_equippedSlot = idx` (no controller call).

`CycleSlot(step)`:
- Walks `_equippedSlot` left/right with wrap, runs the same dispatch as `ToggleSlot`.

`GetEquipped()`:
- Returns `slots[_equippedSlot].id` if valid, else `None`. This replaces the old registry-poll version.

`UnequipAll()`:
- Unchanged: still iterates registry and calls `ForceUnequip` on the currently-equipped tool. Separately, callers like the dialogue/phone hooks should also set `_equippedSlot = -1`.

Existing dialogue / phone / piloting / map-open auto-unequip hooks all keep working: they were already calling `UnequipAll()`, which will continue to unequip any held tool. For consistency, add `_equippedSlot = -1` to those drop paths so a held-but-no-tool selection (e.g., a resource slot was highlighted) also clears.

## Visual treatment

`SlotVisuals` gains two changes:

**Resource swatch.** When a slot holds a resource, the existing `itemIcon` `Image` displays a procedural rounded-corner colored swatch (generated once and cached, similar to `HotbarRoundedRing`). Tint per resource:
- Wood `#D4A06B` (matches old WOOD chip)
- Crystal `#8CE6FF` (matches old CRYSTAL chip)
- SpaceDust `#B88CFF` (matches old DUST chip / DustPopup)

For tool slots, `itemIcon` displays the tool's `hotbarIcon` sprite as before.

**Stack count.** New `TextMeshProUGUI countText` anchored bottom-right of the slot:
- Resource slot: shows current count (e.g. `"16"`, `"100"`). Bold, 14pt, white with dark drop shadow for legibility against the swatch.
- Tool slot: hidden.
- Empty slot: hidden.

Updates use change-detection (`if (count != _lastCountSeen) ...`) to avoid per-frame string alloc, following the project's `PlayerPickup` / `PickupUIManager` / `CompassHUD` precedent.

**Name plate.** When a resource slot is active, the existing name plate above the bar reads e.g. `"WOOD ×16"`. Format: `$"{displayName} ×{count}"` for resources, plain `displayName` for tools (unchanged).

`DisplayName` for new resources: `"WOOD"`, `"CRYSTAL"`, `"DUST"`.

## Inventory-full popup

New file: `Assets/3 - Scripts/UI/InventoryFullPopup.cs`.

- Singleton MonoBehaviour, auto-creates on first `Show()` call, `DontDestroyOnLoad`.
- ScreenSpaceOverlay canvas, sortingOrder 835 (just above hotbar's 830).
- `HUDSceneGate.Register` so it hides in main menu like other HUDs.
- One floating pill: red-tinted background (`#3C1518` with soft `#FF6F70` border + glow), white "INVENTORY FULL" text, 18pt bold. Anchored centered above the hotbar (anchor bottom-center, offset ~140px up).
- `Show()` (parameterless — text is always "INVENTORY FULL"): fade in over 0.15s, hold 1.2s, fade out over 0.4s. Re-calling `Show()` while visible **restarts** the timer (doesn't stack).

## Save / load

### New schema (in `SaveData.cs`)

```csharp
[Serializable]
public class HotbarSlotSave {
    public string itemId;   // ItemId enum.ToString() — e.g. "Wood", "Pistol", "None"
    public int count;
}

[Serializable]
public class HotbarSave {
    public List<HotbarSlotSave> slots = new List<HotbarSlotSave>();
}
```

Added as field `public HotbarSave hotbar = new HotbarSave();` on `SaveData`.

### Capture (`SaveCollector.cs`)

```csharp
static void CaptureHotbar(HotbarSave s) {
    s.slots.Clear();
    if (Hotbar.Instance == null) return;
    foreach (var slot in Hotbar.Instance.GetSlotsForSave()) {
        s.slots.Add(new HotbarSlotSave {
            itemId = slot.id.ToString(),
            count = slot.count
        });
    }
}
```

Hotbar exposes a read-only `IReadOnlyList<Slot> GetSlotsForSave()` for this.

### Apply (`SaveCollector.cs`)

```csharp
static void ApplyHotbar(HotbarSave s, SaveData data) {
    if (Hotbar.Instance == null) return;

    // Preferred: restore exact layout if HotbarSave is non-empty.
    if (s != null && s.slots != null && s.slots.Count > 0) {
        Hotbar.Instance.ApplySlotsFromSave(s.slots);
        return;
    }

    // Legacy fallback: old saves only have totals.
    if (data.wood != null)      Hotbar.Instance.SetResourceTotal(ItemId.Wood, data.wood.wood);
    if (data.crystal != null)   Hotbar.Instance.SetResourceTotal(ItemId.Crystal, data.crystal.count);
    if (data.spaceDust != null) Hotbar.Instance.SetResourceTotal(ItemId.SpaceDust, data.spaceDust.playerDust);
}
```

Hotbar exposes:
```csharp
void ApplySlotsFromSave(List<HotbarSlotSave> slots);
```

This clears the current array, then writes the loaded slots in order (clamping count to `[0, StackMax(id)]` for safety; ignoring unknown enum strings).

### Order in `SaveCollector.Apply`

Insert `ApplyHotbar(data.hotbar, data)` **after** `ApplyEquipment` (which currently runs in step 6 — "singleton state"). Reasoning:
- Bodies / ship / player transforms must already be restored when equipment applies, but the hotbar has no positional dependencies — it just needs the Hotbar singleton to exist, which it does (seeded by `EnsureGameplaySingletons`).
- Equipment apply may equip a tool (e.g., axe) — the hotbar's equipped-slot state should reflect that after restore. We can handle this implicitly: after `ApplyHotbar` fills slots and `ApplyEquipment` flips controller IsEquipped, the next `Hotbar.Update` will resolve `_equippedSlot` to the matching tool slot via a small "sync from controllers" pass on first tick.

### Backwards compat

- Old saves: `HotbarSave.slots` is `null` or empty → fallback distributes legacy totals.
- After this slice ships, new saves carry both `HotbarSave` AND the legacy total fields (we keep capturing `WoodSave.wood` etc. via existing code, since they round-trip the facade's `Wood` property — net zero risk).

## Old UI removal

In `PlayerWallet.cs`:

- Remove `_woodChip`, `_dustChip`, `_crystalChip` field declarations and their `BuildChip` calls inside `CreateCornerHUD`.
- Remove `_lastWoodSeen`, `_lastDustSeen`, `_lastCrystalSeen`, `_dustChipVisible`, `_crystalChipVisible` state.
- Remove `woodText`, `dustText`, `crystalText` field declarations.
- Remove the wood / dust / crystal blocks in `Update()`.
- Remove `RefreshWood()`.
- Remove `WoodValueColor`, `DustValueColor`, `CrystalValueColor` constants.
- Remove `WoodValueColor` / `DustValueColor` / `CrystalValueColor` use sites.
- **Keep** Money chip, Ammo chip, and all their state — untouched.

`WoodPopup`, `CrystalPopup`, `DustPopup` (world-space `+16` floaters at the collect point) **stay** — they're per-event feedback, not inventory display.

## Files touched

| Status | File |
|---|---|
| Major refactor | `Assets/3 - Scripts/UI/Hotbar.cs` |
| Facade rewrite | `Assets/3 - Scripts/Player/WoodInventory.cs` |
| Facade rewrite | `Assets/3 - Scripts/Player/CrystalInventory.cs` |
| Facade rewrite | `Assets/3 - Scripts/Player/SpaceDustInventory.cs` |
| Remove 3 chips | `Assets/3 - Scripts/Player/PlayerWallet.cs` |
| New schema block | `Assets/3 - Scripts/SaveSystem/SaveData.cs` |
| Capture + apply | `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` |
| New file | `Assets/3 - Scripts/UI/InventoryFullPopup.cs` |

## Testing plan

Unity editor verification (no automated tests in this project):

1. **Auto-add on collect:** Chop a tree → 16 wood appears as a stack in first empty hotbar slot. Mine a crystal → cyan stack. Drain a space net → violet stack.
2. **Stack merge:** Mine two trees in a row (32 wood) → single stack of 32 in one slot, not two stacks of 16.
3. **Stack overflow into next slot:** Have 90 wood in slot 1, chop a tree (16) → slot 1 caps at 100, slot 2 gets 6.
4. **Hotbar full popup:** Fill all 5 slots (tools + stacks), trigger another collect → `"INVENTORY FULL"` popup shows; original stacks unchanged; lost resource not added.
5. **Spending sum:** Place a Cabin recipe that costs 60 wood with two stacks (40, 30). Verify it succeeds, slot 1 empties, slot 2 left with 10.
6. **Spending insufficient:** Same setup with cost 80 wood and total 70 → `Has(80)` returns false, recipe locked, nothing drained.
7. **Equip behavior:** Press 1–5 — tool slots equip normally (axe swings, pistol fires). Resource slots highlight + name plate `"WOOD ×40"`, no animation. Press the same number again → unequips.
8. **Old HUD gone:** Top-left has only Money (and Ammo when pistol equipped) — no WOOD / DUST / CRYSTAL chips.
9. **Save round-trip (new):** Save with two wood stacks (90, 12) + one dust stack (45). Reload → exact same layout in exact same slots.
10. **Legacy save load:** Load a pre-refactor save (e.g. one with `wood.wood = 47, spaceDust.playerDust = 8`) → loads cleanly with one stack of 47 wood + one stack of 8 dust in the first available slots.
11. **Build sanity check (per CLAUDE.md MainMenu trap):** Make a build, start from MainMenu → PLAY, verify hotbar resource slots fill correctly when mining. The Hotbar singleton is already seeded in `EnsureGameplaySingletons`, but the new `InventoryFullPopup` is a *new* auto-singleton that must NOT skip MainMenu in the same buggy way — design it to only auto-create on first `Show()` call, not via `RuntimeInitializeOnLoadMethod`, sidestepping the trap.
12. **Floating-origin sanity:** Move 1100m from origin to trigger an `EndlessManager` shift mid-mine; verify resource counts and slot layout survive (they should — they're DontDestroyOnLoad UI state with no world-space dependency).

## Future seam: ship storage

This design intentionally exposes:
- `Hotbar.GetSlotsForSave()` / `ApplySlotsFromSave(...)` — same-shape API a future `ShipStorage` container can use for save/load.
- Internal `Slot` representation `(ItemId, int)` — directly transferable.

A ship-storage feature would:
1. Add a `ShipStorage` MonoBehaviour with a parallel `Slot[]` array.
2. Build a "Storage" UI panel (similar to `SaveLoadUI`'s procedural Canvas) showing both grids and click-to-transfer.
3. Re-use `Hotbar.AddResource` / `RemoveStackFromSlot(idx)` for hotbar→storage moves and the inverse for storage→hotbar.
4. Add a `ShipStorageSave` block parallel to `HotbarSave` in `SaveData`.

No prep needed in this slice beyond keeping the slot-based API clean.
