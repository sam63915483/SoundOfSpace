# Fish & Storage Revamp — Phase 3: Vendor expansion + Fish bag

**Status:** Draft — awaiting user approval
**Date:** 2026-05-26
**Branch:** `feature/fish-storage-revamp` (Phase 2 lands at tag `fish-revamp-phase-2-complete`)
**Author:** Sam McNeil with Claude

---

## 1. Why this phase exists

Phase 2 put fish into the hotbar and gave them a place to live, but the hotbar's 7 slots fill fast when you're competing for space with equippables. Phase 3 introduces a **fish bag** — a single-instance non-stackable inventory item with 5 internal slots, available from the goods vendor (`Alien7Vendor`). When the bag is equipped in the hotbar, new catches route into the bag first, then the hotbar; only when both are full do fish get discarded.

Same vendor sells the **fishing rod** and **water bottle** for the first time — these were previously unlocked via NPC dialogue / pickup; making them purchasable opens an alternate progression path for players who skip the early-game NPC arc.

The fish bag also gets a **side panel UI** that opens when the player right-clicks the bag inside the storage UI. The panel shows the bag's 5 internal slots and supports drag-and-drop between bag / storage / hotbar.

---

## 2. Goals & non-goals

### Goals

- `Alien7Vendor` sells `FishingRod`, `WaterBottle`, and `FishBag` as one-time purchases.
- Single-instance enforcement on fish bag: `IsAlreadyOwned(FishBag)` returns true if any slot anywhere already holds one.
- New `ItemId.FishBag` + `Slot.bagContents` (length-5 `Hotbar.Slot[]`) data model.
- New `Hotbar.TryAddBag()` and `Hotbar.TryAddFishToBag(FishEntry)` helpers.
- Catch flow: bag-first → hotbar → destroy (still log to dex).
- Storage UI right-click on a bag slot opens / toggles a 5-slot side panel docked to the right of the main panel. Side panel supports the same `SlotOps` drag/drop (LMB / RMB / Shift+LMB).
- Side panel closes on: right-click bag again, or main storage closes.
- Save schema round-trip: `HotbarSlotSave.bagContents` recursive list.
- Old saves (no bag) load fine — `bagContents = null` for every slot.

### Non-goals (deferred to later phases)

- Fish vendor sell UI redesign (drag-and-drop replacing the tier-counter "Add Fish") — Phase 4.
- Reusing the bag side panel inside the new sell UI — Phase 4.
- Multiple fish bags simultaneously — single-instance is final.
- Restricting the bag to fish-only content at the data layer — no enforcement; UI convention only.
- Bag UX shortcut for eating directly from the bag without first moving fish to hotbar — players must drag from bag to hotbar via the side panel, then hold-LMB on the hotbar slot.

---

## 3. Confirmed design decisions

| Decision | Choice |
|---|---|
| How many bags can the player own | One. One-time vendor purchase, single-instance enforcement. |
| Where bag contents live | `Hotbar.Slot.bagContents` — a 5-length array attached to the bag's slot. Travels with the bag through pick-up/drop/storage moves. |
| Side panel position | Docked to the right of the main storage panel, vertical column of 5 slots. |
| Side panel trigger | Right-click on a `FishBag` slot inside `StorageUI` (in the storage grid or hotbar mirror). Right-click again closes it. |
| Side panel lifecycle | Open until right-click on the bag again OR storage UI closes. |
| Bag content restriction | None at data layer. UI calls it "Fish Bag"; players can technically stash anything. |
| Fish routing priority | Bag (if equipped in hotbar) → hotbar (next empty) → destroy + popup. Dex always logs. |
| Bag price | 100 money (default — tunable in the ShopItem inspector). |
| Hotbar full at vendor purchase | Refuse purchase. Vendor's existing denial UI surfaces this. |

---

## 4. Architecture changes

### 4.1 New `ShopItemKind` values

In `ShopItem.cs`:
```csharp
public enum ShopItemKind
{
    None,
    Pistol,
    Axe,
    Jetpack,
    // ... existing ship/part kinds ...
    FishingRod,      // Phase 3
    WaterBottle,     // Phase 3
    FishBag,         // Phase 3
}
```

(Existing values preserved; new enum entries appended at the end so old saves storing `ShopItemKind` integer values don't shift.)

### 4.2 `Alien7Vendor.cs`

Two switch cases extended:

```csharp
public bool IsAlreadyOwned(ShopItemKind kind)
{
    switch (kind)
    {
        case ShopItemKind.Pistol:    return /* existing */;
        case ShopItemKind.Axe:       return /* existing */;
        case ShopItemKind.Jetpack:   return /* existing */;
        case ShopItemKind.FishingRod:
        {
            var rod = Object.FindObjectOfType<FishingRodController>(true);
            return rod != null && rod.IsUnlocked;
        }
        case ShopItemKind.WaterBottle:
        {
            var bottle = Object.FindObjectOfType<WaterBottleController>(true);
            return bottle != null && bottle.IsUnlocked;
        }
        case ShopItemKind.FishBag:
            return Hotbar.Instance != null && Hotbar.Instance.HasFishBagAnywhere();
    }
    return false;
}

void GrantItem(ShopItemKind kind)
{
    switch (kind)
    {
        // ... existing cases ...
        case ShopItemKind.FishingRod:
        {
            var rod = Object.FindObjectOfType<FishingRodController>(true);
            if (rod != null) rod.Unlock();
            break;
        }
        case ShopItemKind.WaterBottle:
        {
            var bottle = Object.FindObjectOfType<WaterBottleController>(true);
            if (bottle != null) bottle.Unlock();
            break;
        }
        case ShopItemKind.FishBag:
            Hotbar.Instance?.TryAddBag();
            break;
    }
}
```

`TryAddBag` returns `bool`. If false (hotbar full), the vendor purchase flow needs to detect and surface failure. The cleanest hook: `Alien7Vendor.AttemptPurchase` (existing method) already validates money and checks `IsAlreadyOwned`. Add a new `PurchaseResult.NoInventorySpace` value and return it before charging money. Refund-style logic isn't needed because we never debit on the failure path.

### 4.3 `Hotbar.cs` data model + helpers

```csharp
public enum ItemId { /* ... existing ... */, Fish, FishBag }

public struct Slot
{
    public ItemId id;
    public int count;
    public FishEntry fishData;
    // Phase 3: bag's 5 internal slots. null unless id == FishBag; always
    // length 5 when populated. Each entry is a regular Hotbar.Slot —
    // typically Fish, but the data layer doesn't enforce it.
    public Hotbar.Slot[] bagContents;
}

static bool IsSelectOnly(ItemId id) =>
    IsResource(id) || id == ItemId.Fish || id == ItemId.FishBag;
```

New methods:

```csharp
// Returns true if a FishBag slot exists anywhere — hotbar or any LootBox.
// Used by Alien7Vendor.IsAlreadyOwned to enforce single-instance.
public bool HasFishBagAnywhere()
{
    for (int i = 0; i < NumSlots; i++)
        if (slots[i].id == ItemId.FishBag) return true;
    foreach (var box in StorageRegistry.All)
    {
        if (box == null) continue;
        var s = box.Slots;
        for (int j = 0; j < s.Length; j++)
            if (s[j].id == ItemId.FishBag) return true;
    }
    return false;
}

// Spawn a fresh bag in the first empty hotbar slot. Returns false if no
// empty slot — caller (vendor) refuses the purchase.
public bool TryAddBag()
{
    for (int i = 0; i < NumSlots; i++)
    {
        if (slots[i].id != ItemId.None) continue;
        slots[i] = new Slot
        {
            id = ItemId.FishBag,
            count = 1,
            bagContents = new Slot[5],   // 5 empty sub-slots
        };
        OnResourceChanged?.Invoke(ItemId.FishBag);
        return true;
    }
    return false;
}

// Try to place a fish in the equipped fish bag's first empty internal slot.
// Returns true if placed; false if no bag is in the hotbar or all 5 internal
// slots are full. Called BEFORE TryAddFish in Bobber's catch flow.
public bool TryAddFishToBag(FishEntry entry)
{
    if (entry == null) return false;
    for (int i = 0; i < NumSlots; i++)
    {
        if (slots[i].id != ItemId.FishBag) continue;
        var bag = slots[i].bagContents;
        if (bag == null) continue;
        for (int j = 0; j < bag.Length; j++)
        {
            if (bag[j].id != ItemId.None) continue;
            bag[j] = new Slot { id = ItemId.Fish, count = 1, fishData = entry };
            // Slot is mutated in-place via array ref; nothing else to do
            // since the outer hotbar slot still points to the same array.
            OnResourceChanged?.Invoke(ItemId.Fish);
            return true;
        }
    }
    return false;
}
```

### 4.4 Catch flow (`Bobber.cs`)

```csharp
if (FishInventory.Instance != null)
{
    var entry = FishInventory.Instance.AddFish(currentFishType, weight);
    bool placed =
        (Hotbar.Instance != null && Hotbar.Instance.TryAddFishToBag(entry)) ||
        (Hotbar.Instance != null && Hotbar.Instance.TryAddFish(entry));
    if (!placed) InventoryFullPopup.Show();
}
```

Three-step short-circuit: bag → hotbar → destroy.

### 4.5 Storage UI — bag side panel

Add new fields:
```csharp
SlotView[] _bagViews;       // 5 slot views for the docked side panel
RectTransform _bagPanel;    // the side-panel root, hidden by default
Hotbar.Slot[] _activeBag;   // the bagContents array currently being viewed
```

`OnSlotClicked` interception (BEFORE the existing LMB/RMB branches):
```csharp
if (e.button == PointerEventData.InputButton.Right
    && view.container != null
    && view.index >= 0 && view.index < view.container.Length
    && view.container[view.index].id == Hotbar.ItemId.FishBag)
{
    var bag = view.container[view.index].bagContents;
    if (bag == null) return;          // defensive
    ToggleBagPanel(bag);
    return;                            // consume — don't fall through to RMB pickup-one
}
```

`ToggleBagPanel(Hotbar.Slot[] bag)`:
- If `_activeBag == bag`: close (hide panel, null `_activeBag`).
- Else: set `_activeBag = bag`, show panel, repaint 5 slots from `bag[]`.

`RefreshAll` extended: if `_bagPanel != null && _bagPanel.activeSelf`:
```csharp
for (int i = 0; i < _bagViews.Length; i++)
{
    PaintSlot(_bagViews[i], i < _activeBag.Length ? _activeBag[i] : default);
    _bagViews[i].container = _activeBag;
    _bagViews[i].index = i;
}
```

The same `OnSlotClicked` already handles container=`_activeBag` slot clicks — LMB picks up / drops, RMB picks one / drops one, Shift+LMB quick-moves to the OTHER container (storage grid).

`Close()` (storage UI close) also closes the side panel.

Side panel build: vertical column of 5 84px slot tiles, docked to the right of the main panel with a small label "FISH BAG · 5 SLOTS".

### 4.6 Save schema (`SaveData.cs`)

```csharp
[Serializable]
public class HotbarSlotSave
{
    public string itemId;
    public int count;
    public FishEntrySave fishData;
    // Phase 3: recursive bag contents. List instead of array for
    // JsonUtility friendliness. null/empty for non-bag slots.
    public List<HotbarSlotSave> bagContents;
}
```

`SaveCollector.CaptureHotbar` and `CaptureStorages` extend the slot→save translation:
```csharp
fishData = ...,
bagContents = slot.id == Hotbar.ItemId.FishBag && slot.bagContents != null
    ? SerializeBagContents(slot.bagContents)
    : null
```

Where `SerializeBagContents(Slot[] bag)` builds a `List<HotbarSlotSave>` via the same per-slot translation (recursive but only one level deep — bags can't contain bags by current design).

`ApplyHotbarSlots` / `ApplyStorages` reverse: when `entry.itemId == "FishBag"`, allocate `new Slot[5]` and decode each `entry.bagContents[k]` into it. Defensive: if saved `bagContents` has != 5 entries, pad/truncate.

### 4.7 Vendor ScriptableObject assets (Editor)

Three new `ShopItem` assets need to exist as files in the project — they're authored, not code:

- `Assets/<vendor folder>/ShopItem_FishingRod.asset` — kind=FishingRod, displayName="Fishing Rod", price=50, oneTimePurchase=true, icon=rod hotbarIcon, previewPrefab=fishing rod prefab.
- `Assets/<vendor folder>/ShopItem_WaterBottle.asset` — kind=WaterBottle, displayName="Water Bottle", price=30, oneTimePurchase=true, icon=bottle hotbarIcon, previewPrefab=water bottle prefab.
- `Assets/<vendor folder>/ShopItem_FishBag.asset` — kind=FishBag, displayName="Fish Bag", price=100, oneTimePurchase=true, icon=bag icon (placeholder fallback acceptable), previewPrefab=null (no in-world prop).

Plan-writing checks the existing vendor folder layout to confirm the path and asset shape. Drop the three assets into `Alien7Vendor.inventory[]` array via the inspector.

---

## 5. Backward compatibility

- **Old saves before Phase 3:** `HotbarSlotSave.bagContents` defaults to `null` (JsonUtility) — no bag in any slot, no migration needed.
- **Saves between Phase 2 and Phase 3:** no field collision; new field is additive.
- **Single-instance enforcement on load:** if a corrupted save somehow has two `FishBag` slots, both load; the player just owns two bags. No harm beyond capacity exploit. Don't add deduplication — pure additive schema, trust the save.

---

## 6. Acceptance criteria

After Phase 3 lands, manual Editor regression on a fresh session:

1. **Vendor sells all three.** Open `Alien7Vendor` shop UI. Three new items appear: Fishing Rod (50), Water Bottle (30), Fish Bag (100). Each shows the right display name and preview.
2. **Rod purchase unlocks the rod.** Buy the rod (with enough money). Rod appears in hotbar (existing equippable acquisition). Re-opening the vendor: rod shows "Already Owned" / disabled.
3. **Water bottle purchase unlocks the bottle.** Same flow.
4. **Fish bag purchase spawns the bag.** Buy the bag. Bag appears in first empty hotbar slot. Re-opening the vendor: Fish Bag shows "Already Owned". Selling the bag's slot (or moving to storage) doesn't change this — bag is owned globally.
5. **Hotbar full at bag purchase.** Fill all 7 hotbar slots (with equippables + fish). Try to buy bag. Vendor denies with "no inventory space" message; money not charged.
6. **Bag routes catches first.** Have the bag in any hotbar slot (no number-key selection required — presence in the hotbar is enough). Have empty bag slots and empty hotbar slots. Cast & catch a fish. Fish goes to bag's first internal slot (visible via storage right-click). Next catch goes to next bag slot. After 5 catches, bag is full; 6th catch goes to next empty hotbar slot.
7. **Bag full + hotbar full destroys.** Bag full (5 fish), hotbar slots all filled (rod / axe / etc. or other fish). Catch one more. `InventoryFullPopup` flashes. Dex shows the fish anyway.
8. **Storage right-click opens side panel.** Open storage (F on a loot box). Right-click the Fish Bag slot in the hotbar mirror. Side panel docks to the right of the main panel showing 5 slots, populated by bag contents. Drag fish from bag to storage grid: works. Drag fish from storage to bag: works. Right-click bag again: panel closes. Close storage: panel closes.
9. **Save round-trip with fish in bag.** Save with 3 fish in the bag, 2 in hotbar, 1 in storage. Reload. All 6 fish are in the same slots they were in. Bag still routes new catches first.
10. **Single-instance survives storage transitions.** Move the bag from hotbar to storage via drag. Open vendor: bag still shows "Already Owned" (HasFishBagAnywhere scans storage too). Move bag back to hotbar: catches resume routing to bag.

---

## 7. Files touched (estimate)

| File | Change |
|---|---|
| `Assets/3 - Scripts/Vendor/ShopItem.cs` | 3 new `ShopItemKind` enum values (appended) |
| `Assets/3 - Scripts/Vendor/Alien7Vendor.cs` | `IsAlreadyOwned` + `GrantItem` switch cases; `PurchaseResult.NoInventorySpace` enum value; refuse-on-full path |
| `Assets/3 - Scripts/UI/Hotbar.cs` | `ItemId.FishBag`; `Slot.bagContents`; `IsSelectOnly` updated; `HasFishBagAnywhere` / `TryAddBag` / `TryAddFishToBag`; slot-icon paint for bag |
| `Assets/3 - Scripts/Fishing/Bobber.cs` | catch order: bag → hotbar → destroy |
| `Assets/3 - Scripts/UI/StorageUI.cs` | RMB-on-bag intercept; build & lifecycle of `_bagPanel`; `RefreshAll` extension |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | `HotbarSlotSave.bagContents` recursive field |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | capture + apply for `bagContents`; helper to serialize/deserialize the recursive list |
| 3 new `ShopItem` ScriptableObject assets | authored via Editor menu (Create → Game → Shop Item), one per kind, then dropped into `Alien7Vendor.inventory[]` |

---

## 8. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `Slot` struct grows with a third field — risk of forgetting one in slot constructions | Slot already has `fishData` (Phase 1); adding `bagContents` follows the same pattern. Plan task explicitly grep-audits every `new Hotbar.Slot { ... }` site. |
| Side panel position overlaps the main storage panel on narrow screens | Reference resolution is 1920×1080 (set in CanvasScaler); 5-slot column at 84px + gap fits comfortably to the right of the 5×4 storage grid. If a future canvas resize breaks layout, file as polish. |
| Bag-in-storage doesn't auto-route catches (per design) — player may be confused | Acceptance criterion #6 covers the "equipped" requirement explicitly. Add a comment in the catch flow citing the design. |
| Bag side panel gets out of sync when bag is moved to a new slot mid-panel-open | `_activeBag` holds a reference to the underlying `Slot[]` array, which is itself stored by reference in the `Slot.bagContents` field. Moving the OUTER Slot (via drag) moves the inner array reference too — the panel keeps showing the same bag's contents. Verified by Phase 1's `SlotOps` already preserving payload fields through cursor moves. |
| Save schema's `bagContents` recursion could blow up if a future bag holds another bag | Current design doesn't allow nested bags (no UI path). Defensive: if a saved `bagContents[k]` has its own `bagContents` populated, ignore the nested one (only one level deep). |

---

## 9. What comes next

After this spec is approved, `writing-plans` skill turns it into an atomic step-by-step plan at `docs/superpowers/plans/2026-05-26-fish-storage-revamp-phase-3-vendor-fishbag.md`. Each plan step lands as a focused commit on `feature/fish-storage-revamp`. Once Phase 3 ships and regression passes, Phase 4 (fish vendor drag-and-drop sell UI, reusing the bag side panel) begins its own brainstorm.
