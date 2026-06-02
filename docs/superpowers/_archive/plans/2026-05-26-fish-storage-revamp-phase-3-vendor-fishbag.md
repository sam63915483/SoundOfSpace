# Fish & Storage Revamp — Phase 3 (Vendor + Fish Bag) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Alien7Vendor sells fishing rod / water bottle / fish bag. Fish bag is a single-instance non-stackable hotbar item with 5 internal slots; catches route bag → hotbar → destroy. Storage UI right-click on a bag opens a 5-slot side panel for drag-drop. Save round-trips bag contents.

**Architecture:** Additive on Phase 2's slot model. `Slot.bagContents` (length-5 `Hotbar.Slot[]`) is the new payload; `HotbarSlotSave.bagContents` is the recursive save mirror. Vendor gets three new `ShopItemKind` values + matching switch cases + a new `PurchaseResult.NoInventorySpace` for the "hotbar full at bag purchase" case. StorageUI intercepts RMB on `FishBag` slots before the existing pickup-one path.

**Tech Stack:** Unity 2022.3, C#, default `Assembly-CSharp`. `JsonUtility` saves. No test framework — manual Editor regression.

---

## File Structure

| File | Responsibility | Changes |
|---|---|---|
| `Assets/3 - Scripts/Vendor/ShopItem.cs` | `ShopItemKind` enum | 3 new values (FishingRod, WaterBottle, FishBag) appended at end |
| `Assets/3 - Scripts/Vendor/Alien7Vendor.cs` | Purchase flow | `PurchaseResult.NoInventorySpace` value; pre-check for FishBag space; `IsAlreadyOwned` + `GrantItem` switch cases for 3 new kinds |
| `Assets/3 - Scripts/UI/Hotbar.cs` | Inventory data + helpers | `ItemId.FishBag`; `Slot.bagContents` field; `IsSelectOnly` updated; `HasFishBagAnywhere` / `HasEmptyHotbarSlot` / `TryAddBag` / `TryAddFishToBag`; slot-icon paint for FishBag |
| `Assets/3 - Scripts/Fishing/Bobber.cs` | Catch flow | Route order: bag → hotbar → destroy |
| `Assets/3 - Scripts/UI/StorageUI.cs` | Bag side panel | RMB-on-FishBag intercept; build/lifecycle of `_bagPanel` (5 slots, docked right); `RefreshAll` extension |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | Schema | `HotbarSlotSave.bagContents` recursive list field |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Capture / apply | Recursive `SerializeBagContents` / `DeserializeBagContents` helpers; integrated into Hotbar + Storage save paths |
| `Assets/Editor/CreateFishStorageShopItems.cs` | One-shot editor menu | Creates 3 ShopItem ScriptableObject assets via `Tools → Fix → Create Fish/Storage Shop Items` |

11 atomic tasks total.

---

## Verification Strategy

Same as Phase 1 / Phase 2: no test framework. Per-task verification = code change → `mcp__coplay-mcp__check_compile_errors` (or Unity Console) → optional behavioral check → commit.

---

## Task 1 — `ShopItemKind` enum entries

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/ShopItem.cs:3-20`

- [ ] **Step 1: Add three values**

Replace the `ShopItemKind` enum body. Find:

```csharp
public enum ShopItemKind
{
    None = 0,
    Pistol = 1,
    Axe = 2,
    Jetpack = 3,
    // Ship-market entries — handled by ShipMarketNPC, NOT Alien7Vendor.
    ShipFull = 10,
    ShipNoDish = 11,
    ShipHull = 12,
    PartLeftThruster = 20,
    PartRightThruster = 21,
    PartDish = 22,
    PartSolarPanel = 23,
    SpaceDustFilter = 30,
    SpaceNetLeft = 31,
    SpaceNetRight = 32,
}
```

Replace with (appending 40-series for Phase 3 goods):

```csharp
public enum ShopItemKind
{
    None = 0,
    Pistol = 1,
    Axe = 2,
    Jetpack = 3,
    // Ship-market entries — handled by ShipMarketNPC, NOT Alien7Vendor.
    ShipFull = 10,
    ShipNoDish = 11,
    ShipHull = 12,
    PartLeftThruster = 20,
    PartRightThruster = 21,
    PartDish = 22,
    PartSolarPanel = 23,
    SpaceDustFilter = 30,
    SpaceNetLeft = 31,
    SpaceNetRight = 32,
    // Phase 3 — Alien7Vendor goods. FishingRod / WaterBottle unlock the
    // controller (already on Player root). FishBag spawns an inventory
    // item in the first empty hotbar slot; single-instance enforced via
    // Hotbar.HasFishBagAnywhere.
    FishingRod = 40,
    WaterBottle = 41,
    FishBag = 42,
}
```

Explicit numbering preserves the existing values (JsonUtility serializes enums as ints; appending must not shift earlier values).

- [ ] **Step 2: Compile check**

`mcp__coplay-mcp__check_compile_errors` — must report clean.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Vendor/ShopItem.cs"
git commit -m "feat(vendor): ShopItemKind values for FishingRod / WaterBottle / FishBag

Three Phase 3 goods-vendor entries appended at 40+ so earlier values
don't shift. Alien7Vendor switch cases land next."
```

---

## Task 2 — `Hotbar` data model: `ItemId.FishBag` + `Slot.bagContents` + `IsSelectOnly`

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs:9-17` (enum + struct) and the `IsSelectOnly` helper added in Phase 2

- [ ] **Step 1: Add FishBag to ItemId enum**

Replace:
```csharp
public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust, Fish }
```
with:
```csharp
public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust, Fish, FishBag }
```

- [ ] **Step 2: Add `bagContents` to Slot struct**

Replace:
```csharp
public struct Slot
{
    public ItemId id;
    public int count;
    public FishEntry fishData;
}
```
with:
```csharp
public struct Slot
{
    public ItemId id;
    public int count;
    // Populated only when id == ItemId.Fish. Null otherwise.
    public FishEntry fishData;
    // Populated only when id == ItemId.FishBag. null otherwise; always
    // length 5 when populated. Each entry is a regular Hotbar.Slot —
    // typically Fish, but the data layer doesn't enforce content.
    public Hotbar.Slot[] bagContents;
}
```

- [ ] **Step 3: Update `IsSelectOnly`**

Find the existing helper (added in Phase 2):
```csharp
static bool IsSelectOnly(ItemId id) => IsResource(id) || id == ItemId.Fish;
```

Replace with:
```csharp
static bool IsSelectOnly(ItemId id) =>
    IsResource(id) || id == ItemId.Fish || id == ItemId.FishBag;
```

The bag is select-only: pressing its number key highlights the slot but spawns no controller. Side-panel access is via the storage UI's right-click intercept (Task 7).

- [ ] **Step 4: Compile check**

Console clean.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): ItemId.FishBag + Slot.bagContents payload

Phase 3 data model. FishBag is select-only like Fish (no controller
to equip). bagContents is null for non-bag slots; length 5 when
populated. Storage/save/UI integrations land in subsequent tasks."
```

---

## Task 3 — Hotbar bag helpers: `HasEmptyHotbarSlot`, `HasFishBagAnywhere`, `TryAddBag`, `TryAddFishToBag`

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` — add four methods near the existing Phase 2 fish helpers (`TryAddFish` / `CountFishByTier` / `TakeFirstFishOfTier`)

- [ ] **Step 1: Append helpers after `TakeFirstFishOfTier`**

Find the closing brace of `TakeFirstFishOfTier` and insert immediately after:

```csharp
    // ── Phase 3: Fish bag helpers ────────────────────────────────────

    // Used by Alien7Vendor.Purchase to refuse FishBag purchase when there's
    // no empty slot. Counts any non-None slot as occupied.
    public bool HasEmptyHotbarSlot()
    {
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == ItemId.None) return true;
        return false;
    }

    // Single-instance enforcement: returns true if a FishBag slot exists
    // anywhere — hotbar OR any registered LootBox's slot array. Used by
    // Alien7Vendor.IsAlreadyOwned(FishBag).
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

    // Spawn a fresh bag in the first empty hotbar slot. Returns false if
    // no empty slot — Alien7Vendor refuses the purchase upstream.
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

    // Try to place a fish in the equipped fish bag's first empty internal
    // slot. Returns true if placed; false if no bag is in the hotbar or
    // all 5 internal slots are full. Called BEFORE TryAddFish in Bobber's
    // catch flow so bag fills before hotbar.
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
                OnResourceChanged?.Invoke(ItemId.Fish);
                return true;
            }
        }
        return false;
    }
```

- [ ] **Step 2: Compile check**

Console clean. `StorageRegistry.All` is the same enumeration used by `SaveCollector.CaptureStorages`; it's already accessible without a `using`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): bag helpers — HasEmptyHotbarSlot, HasFishBagAnywhere, TryAddBag, TryAddFishToBag

HasEmptyHotbarSlot is the vendor's pre-check before charging money
for a bag. HasFishBagAnywhere enforces single-instance by scanning
hotbar + every LootBox. TryAddBag spawns a fresh bag with a 5-empty-
slot bagContents array. TryAddFishToBag is the Bobber catch flow's
first-priority placement (bag before hotbar)."
```

---

## Task 4 — Bobber catch flow: bag → hotbar → destroy

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/Bobber.cs:262-273` (the existing Phase 2 catch handler)

- [ ] **Step 1: Replace the catch block**

Find (added in Phase 2):
```csharp
        if (FishInventory.Instance != null)
        {
            // Phase 2: log to lifetime dex first (every fish is always recorded).
            var entry = FishInventory.Instance.AddFish(currentFishType, weight);
            // Then try the hotbar. If full, popup + dex still has it — the
            // fish is "lost" in the sense the player can't carry it, not lost
            // from the catch record.
            if (Hotbar.Instance == null || !Hotbar.Instance.TryAddFish(entry))
            {
                InventoryFullPopup.Show();
            }
        }
```

Replace with:
```csharp
        if (FishInventory.Instance != null)
        {
            // Phase 2: log to lifetime dex first (every fish is always recorded).
            var entry = FishInventory.Instance.AddFish(currentFishType, weight);
            // Phase 3: route bag → hotbar → destroy. The bag (if present in
            // any hotbar slot) gets first crack; this is the buffer that
            // lets the player carry up to 12 fish total (5 bag + 7 hotbar
            // minus the bag's own slot).
            bool placed =
                (Hotbar.Instance != null && Hotbar.Instance.TryAddFishToBag(entry)) ||
                (Hotbar.Instance != null && Hotbar.Instance.TryAddFish(entry));
            if (!placed) InventoryFullPopup.Show();
        }
```

The `||` short-circuit means `TryAddFish` only runs if `TryAddFishToBag` returned false — exact bag-first behavior.

- [ ] **Step 2: Compile check**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/Bobber.cs"
git commit -m "feat(fishing): catch order — bag, then hotbar, then destroy

Phase 3 routing. If a FishBag slot exists in the hotbar with an
empty internal slot, the catch lands there. Otherwise falls back to
Phase 2's TryAddFish (first empty hotbar slot). If both are full,
InventoryFullPopup fires and the fish is discarded (dex still has
the lifetime log entry from AddFish above)."
```

---

## Task 5 — Alien7Vendor: `PurchaseResult.NoInventorySpace` + 3 new switch cases

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/Alien7Vendor.cs:13` (PurchaseResult enum) and `Purchase` / `IsAlreadyOwned` / `GrantItem` methods

- [ ] **Step 1: Add the enum value**

Find line 13:
```csharp
public enum PurchaseResult { Success, NotEnoughMoney, AlreadyOwned, InvalidItem }
```

Replace with:
```csharp
public enum PurchaseResult { Success, NotEnoughMoney, AlreadyOwned, InvalidItem, NoInventorySpace }
```

- [ ] **Step 2: Pre-check hotbar space in `Purchase` for FishBag**

Find the `Purchase` method (around line 242):
```csharp
public PurchaseResult Purchase(ShopItem item)
{
    if (item == null) return PurchaseResult.InvalidItem;
    if (item.oneTimePurchase && IsAlreadyOwned(item.kind)) return PurchaseResult.AlreadyOwned;
    if (PlayerWallet.Instance == null) return PurchaseResult.InvalidItem;
    if (!PlayerWallet.Instance.SpendMoney(item.price)) return PurchaseResult.NotEnoughMoney;
    GrantItem(item.kind);
    return PurchaseResult.Success;
}
```

Replace with:
```csharp
public PurchaseResult Purchase(ShopItem item)
{
    if (item == null) return PurchaseResult.InvalidItem;
    if (item.oneTimePurchase && IsAlreadyOwned(item.kind)) return PurchaseResult.AlreadyOwned;
    if (PlayerWallet.Instance == null) return PurchaseResult.InvalidItem;
    // Phase 3: refuse FishBag purchase if no empty hotbar slot, BEFORE
    // charging money. The bag has to land somewhere and we don't auto-
    // spill to storage on purchase.
    if (item.kind == ShopItemKind.FishBag
        && Hotbar.Instance != null
        && !Hotbar.Instance.HasEmptyHotbarSlot())
        return PurchaseResult.NoInventorySpace;
    if (!PlayerWallet.Instance.SpendMoney(item.price)) return PurchaseResult.NotEnoughMoney;
    GrantItem(item.kind);
    return PurchaseResult.Success;
}
```

- [ ] **Step 3: Add switch cases to `IsAlreadyOwned`**

Find the existing method (around line 252):
```csharp
public bool IsAlreadyOwned(ShopItemKind kind)
{
    switch (kind)
    {
        case ShopItemKind.Pistol:    /* existing */
        case ShopItemKind.Axe:       /* existing */
        case ShopItemKind.Jetpack:   /* existing */
    }
    return false;
}
```

Add three new cases before the closing brace (preserve existing cases verbatim). The new cases:
```csharp
        case ShopItemKind.FishingRod:
        {
            var rod = UnityEngine.Object.FindObjectOfType<FishingRodController>(true);
            return rod != null && rod.IsUnlocked;
        }
        case ShopItemKind.WaterBottle:
        {
            var bottle = UnityEngine.Object.FindObjectOfType<WaterBottleController>(true);
            return bottle != null && bottle.IsUnlocked;
        }
        case ShopItemKind.FishBag:
            return Hotbar.Instance != null && Hotbar.Instance.HasFishBagAnywhere();
```

Insert immediately before the `}` that closes the switch (the `return false;` stays below — it's the default case).

- [ ] **Step 4: Add switch cases to `GrantItem`**

Find the existing method (around line 270):
```csharp
void GrantItem(ShopItemKind kind)
{
    switch (kind)
    {
        case ShopItemKind.Pistol:    /* existing */
        case ShopItemKind.Axe:       /* existing */
        case ShopItemKind.Jetpack:   /* existing */
    }
    // post-grant logic (BonusTutorial.OfferX etc.)
}
```

Add three cases before the closing brace of the switch:
```csharp
        case ShopItemKind.FishingRod:
        {
            var rod = UnityEngine.Object.FindObjectOfType<FishingRodController>(true);
            if (rod != null) rod.Unlock();
            break;
        }
        case ShopItemKind.WaterBottle:
        {
            var bottle = UnityEngine.Object.FindObjectOfType<WaterBottleController>(true);
            if (bottle != null) bottle.Unlock();
            break;
        }
        case ShopItemKind.FishBag:
            // Pre-check in Purchase already verified space; this should
            // succeed. Returns false only if a race emptied the hotbar
            // between check and grant, which can't happen single-threaded.
            Hotbar.Instance?.TryAddBag();
            break;
```

- [ ] **Step 5: Compile check**

Console clean.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Vendor/Alien7Vendor.cs"
git commit -m "feat(vendor): FishingRod / WaterBottle / FishBag purchase paths

- PurchaseResult.NoInventorySpace for FishBag-at-full-hotbar
- Pre-spend pre-check in Purchase so the player isn't charged for
  a bag they can't receive
- IsAlreadyOwned: rod/bottle check controller.IsUnlocked; bag scans
  hotbar + storage via Hotbar.HasFishBagAnywhere
- GrantItem: rod/bottle call controller.Unlock; bag calls TryAddBag

The three ShopItem ScriptableObject assets that wire these into the
vendor's inventory[] land in Task 9 via an editor menu."
```

---

## Task 6 — Hotbar slot-icon paint for FishBag

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` — the `Refresh` icon-paint loop (around the existing `isRes` / `isFish` branches)

The bag needs a visible icon in the hotbar. Phase 2 added Fish slot rendering via `RawImage fishPreview`. For Phase 3 we'll use a procedural placeholder swatch (square with a "bag" tint) until art is ready — same pattern Phase 1 used for fish before the dex preview wired up.

- [ ] **Step 1: Add a FishBag swatch color**

Find the existing swatch colors near the top of `Hotbar.cs` (around line 296):
```csharp
static readonly Color WoodSwatchColor    = new Color32(0xD4, 0xA0, 0x6B, 0xFF);
static readonly Color CrystalSwatchColor = new Color32(0x8C, 0xE6, 0xFF, 0xFF);
static readonly Color DustSwatchColor    = new Color32(0xB8, 0x8C, 0xFF, 0xFF);
```

Replace with:
```csharp
static readonly Color WoodSwatchColor    = new Color32(0xD4, 0xA0, 0x6B, 0xFF);
static readonly Color CrystalSwatchColor = new Color32(0x8C, 0xE6, 0xFF, 0xFF);
static readonly Color DustSwatchColor    = new Color32(0xB8, 0x8C, 0xFF, 0xFF);
static readonly Color FishBagSwatchColor = new Color32(0x6F, 0xC0, 0x7A, 0xFF);   // muted green canvas
```

- [ ] **Step 2: Extend the icon-paint branch**

In `Refresh`, find the existing Phase 2 paint block (looks like):
```csharp
            bool isRes = IsResource(id);
            bool isFish = id == ItemId.Fish;
            Sprite sprite = null;
            Color iconTint = new Color32(0xF1, 0xF4, 0xFF, 0xC0);
            bool isProceduralSwatch = false;
            if (!empty)
            {
                if (isFish)
                {
                    // Fish slots use a live RenderTexture via RawImage instead
                    // of the sprite path. ...
                }
                else if (isRes)
                {
                    sprite = ResourceIcon(id);
                    if (sprite == null)
                    {
                        sprite = HotbarResourceSwatch.GetSprite();
                        iconTint = ResourceSwatchColor(id);
                        isProceduralSwatch = true;
                    }
                }
                else if (_registry != null)
                {
                    for (int r = 0; r < _registry.Length; r++)
                        if (_registry[r].Id == id) { sprite = _registry[r].Icon; break; }
                }
            }
```

Replace with:
```csharp
            bool isRes = IsResource(id);
            bool isFish = id == ItemId.Fish;
            bool isFishBag = id == ItemId.FishBag;
            Sprite sprite = null;
            Color iconTint = new Color32(0xF1, 0xF4, 0xFF, 0xC0);
            bool isProceduralSwatch = false;
            if (!empty)
            {
                if (isFish)
                {
                    // Fish slots use a live RenderTexture via RawImage instead
                    // of the sprite path.
                }
                else if (isFishBag)
                {
                    // Bag uses the resource-swatch procedural sprite tinted
                    // green. Phase 3 doesn't ship a custom bag icon — the
                    // colored tile is unambiguous given the slot is unique.
                    sprite = HotbarResourceSwatch.GetSprite();
                    iconTint = FishBagSwatchColor;
                    isProceduralSwatch = true;
                }
                else if (isRes)
                {
                    sprite = ResourceIcon(id);
                    if (sprite == null)
                    {
                        sprite = HotbarResourceSwatch.GetSprite();
                        iconTint = ResourceSwatchColor(id);
                        isProceduralSwatch = true;
                    }
                }
                else if (_registry != null)
                {
                    for (int r = 0; r < _registry.Length; r++)
                        if (_registry[r].Id == id) { sprite = _registry[r].Icon; break; }
                }
            }
```

- [ ] **Step 3: Hide the fish-preview RawImage for non-Fish slots that aren't bag either**

Find the Phase 2 fish-preview branch (after the icon-paint above):
```csharp
            // Phase 2: paint the fish preview RawImage for Fish slots. ...
            if (v.fishPreview != null)
            {
                bool fishVisible = isFish && !empty && slots[i].fishData != null;
                if (fishVisible) { ... }
                else if (v.fishPreview.enabled) { v.fishPreview.enabled = false; v.fishPreview.texture = null; }
            }
```

No change needed — the existing logic only enables `fishPreview` when `isFish` is true. For `isFishBag`, it'll stay disabled, and the standard `itemIcon` (with `FishBagSwatchColor` tint) takes over.

- [ ] **Step 4: Update name plate for FishBag**

Find the Phase 2 name-plate label block:
```csharp
            string label;
            if (activeId == ItemId.Fish && slots[newActive].fishData != null)
            {
                label = $"{slots[newActive].fishData.fishType.ToUpper()} FISH · {slots[newActive].fishData.weightLbs} LB";
            }
            else if (IsResource(activeId))
            {
                label = $"{ResourceDisplayName(activeId)} ×{slots[newActive].count}";
            }
            else
            {
                label = ItemName(activeId);
            }
```

Replace with (add bag branch + count):
```csharp
            string label;
            if (activeId == ItemId.Fish && slots[newActive].fishData != null)
            {
                label = $"{slots[newActive].fishData.fishType.ToUpper()} FISH · {slots[newActive].fishData.weightLbs} LB";
            }
            else if (activeId == ItemId.FishBag)
            {
                int filled = 0;
                var bag = slots[newActive].bagContents;
                if (bag != null) for (int b = 0; b < bag.Length; b++) if (bag[b].id != ItemId.None) filled++;
                label = $"FISH BAG · {filled}/5";
            }
            else if (IsResource(activeId))
            {
                label = $"{ResourceDisplayName(activeId)} ×{slots[newActive].count}";
            }
            else
            {
                label = ItemName(activeId);
            }
```

- [ ] **Step 5: Compile check**

Console clean.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): FishBag slot icon + name plate

Procedural green swatch (matches the existing resource-swatch fallback
pattern) tinted with FishBagSwatchColor. Name plate reads
'FISH BAG · N/5' showing current fill — a passive UI cue for the
player without needing to open storage.

Custom bag art is deferred to a future polish pass; the swatch is
unambiguous given the single-instance constraint."
```

---

## Task 7 — `StorageUI` bag side panel: build + RMB intercept + lifecycle

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs` — add side-panel fields, build code, RMB intercept in `OnSlotClicked`, paint in `RefreshAll`, close logic

This is the largest Phase 3 task. Three sub-edits.

- [ ] **Step 1: Add fields**

Near the top of the class (around the existing `_storageViews` / `_hotbarViews` declarations), add:
```csharp
    // Phase 3: docked fish-bag side panel — 5 slot views populated from
    // the bagContents of the currently-open bag. null _activeBag means
    // panel is hidden.
    SlotView[] _bagViews = new SlotView[5];
    RectTransform _bagPanel;
    Hotbar.Slot[] _activeBag;
```

- [ ] **Step 2: Build the panel in `BuildCanvas`**

Find the end of `BuildCanvas` (just before `_root = canvasGo;` or wherever the panel is finalized). Add a call to build the bag side panel — and the method itself:

```csharp
        // Phase 3: build the docked fish-bag side panel. Hidden by default;
        // shown when StorageUI.OpenBagPanel is called via RMB on a FishBag
        // slot.
        BuildBagSidePanel(panel);
```

Then add the new method (place it near the existing `BuildExitButton`):

```csharp
    void BuildBagSidePanel(RectTransform mainPanel)
    {
        // Side panel sits to the right of the main panel, aligned to its
        // right edge. Width = 1 slot column + padding; height = 5 slots
        // stacked vertically + label.
        const float SidePanelW = 84f + 24f;
        const float SidePanelH = 5 * 84f + 4 * 6f + 56f;   // 5 slots + label area

        var side = NewRT("BagSidePanel", mainPanel);
        side.anchorMin = new Vector2(1f, 1f);
        side.anchorMax = new Vector2(1f, 1f);
        side.pivot = new Vector2(0f, 1f);   // top-left of side panel attached to top-right of main
        side.anchoredPosition = new Vector2(16f, 0f);   // 16px gap to the right of main
        side.sizeDelta = new Vector2(SidePanelW, SidePanelH);

        var bg = side.gameObject.AddComponent<Image>();
        bg.color = CyanScannerPalette.PanelBg;
        bg.raycastTarget = true;

        AddOutline(side, CyanScannerPalette.PanelBorder);
        ScannerFrame.AddBrackets(side, length: 12f, thickness: 2f);

        // Label
        var label = NewRT("BagLabel", side);
        label.anchorMin = new Vector2(0f, 1f);
        label.anchorMax = new Vector2(1f, 1f);
        label.pivot = new Vector2(0.5f, 1f);
        label.sizeDelta = new Vector2(-12f, 18f);
        label.anchoredPosition = new Vector2(0f, -10f);
        var lTxt = NewText(label, "FISH BAG · 5", TextAlignmentOptions.Center, 10f);
        lTxt.color = CyanScannerPalette.Accent;
        lTxt.characterSpacing = 3f;

        // 5 slots vertical, top-aligned below the label.
        for (int k = 0; k < 5; k++)
        {
            float y = -(30f + k * (84f + 6f) + 42f);   // 30 = label gap; 42 = SlotSize/2
            _bagViews[k] = BuildSlotView(side, "B" + k, new Vector2(SidePanelW * 0.5f, y));
        }

        _bagPanel = side;
        _bagPanel.gameObject.SetActive(false);   // hidden until OpenBagPanel
    }
```

Note: the existing `BuildSlotView` (already used for storage + hotbar slots) is reused. It anchors at `Vector2.center` by default — we override `anchoredPosition` to lay out vertically inside the side panel.

- [ ] **Step 3: Add the RMB intercept in `OnSlotClicked`**

Find the existing `OnSlotClicked`:
```csharp
    void OnSlotClicked(SlotView view, PointerEventData e)
    {
        if (view == null || view.container == null || _active == null) return;
        var hbSlots = Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (e.button == PointerEventData.InputButton.Left)
        {
            // ... existing LMB / shift branches
        }
        else if (e.button == PointerEventData.InputButton.Right)
        {
            SlotOps.HandleRightClick(view.container, view.index, ref _cursor);
        }

        RefreshAll();
        RefreshCursorVisual();
    }
```

Replace with (insert the RMB-on-FishBag intercept ABOVE the existing RMB branch):
```csharp
    void OnSlotClicked(SlotView view, PointerEventData e)
    {
        if (view == null || view.container == null || _active == null) return;
        var hbSlots = Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Phase 3: RMB on a FishBag slot opens (or toggles) the side panel
        // showing its 5 internal slots. Intercept BEFORE the standard RMB
        // pickup-one path. The cursor must not be holding an item — RMB
        // with a held cursor falls through to the regular drop-one branch
        // (which for FishBag slots is a no-op since IDs don't match).
        if (e.button == PointerEventData.InputButton.Right
            && !_cursor.IsHeld
            && view.index >= 0 && view.index < view.container.Length
            && view.container[view.index].id == Hotbar.ItemId.FishBag)
        {
            var bag = view.container[view.index].bagContents;
            if (bag != null) ToggleBagPanel(bag);
            RefreshAll();
            return;
        }

        if (e.button == PointerEventData.InputButton.Left)
        {
            if (shift)
            {
                bool isStorage = ReferenceEquals(view.container, _active.Slots);
                var dest = isStorage ? hbSlots : _active.Slots;
                if (dest != null) SlotOps.HandleQuickMove(view.container, view.index, dest);
            }
            else
            {
                SlotOps.HandleLeftClick(view.container, view.index, ref _cursor);
            }
        }
        else if (e.button == PointerEventData.InputButton.Right)
        {
            SlotOps.HandleRightClick(view.container, view.index, ref _cursor);
        }

        RefreshAll();
        RefreshCursorVisual();
    }
```

- [ ] **Step 4: Add `ToggleBagPanel`**

Add this method right after `OnSlotClicked`:
```csharp
    void ToggleBagPanel(Hotbar.Slot[] bag)
    {
        if (_bagPanel == null) return;
        if (ReferenceEquals(_activeBag, bag) && _bagPanel.gameObject.activeSelf)
        {
            // Same bag, already open — close.
            _activeBag = null;
            _bagPanel.gameObject.SetActive(false);
        }
        else
        {
            _activeBag = bag;
            _bagPanel.gameObject.SetActive(true);
            // Initial paint happens in RefreshAll on the next frame.
        }
    }
```

- [ ] **Step 5: Paint bag slots in `RefreshAll`**

Find the end of `RefreshAll` (after the hotbar-mirror loop). Add:
```csharp
        // Phase 3: paint bag side panel slots when open.
        if (_bagPanel != null && _bagPanel.gameObject.activeSelf && _activeBag != null)
        {
            for (int k = 0; k < _bagViews.Length; k++)
            {
                var v = _bagViews[k];
                PaintSlot(v, k < _activeBag.Length ? _activeBag[k] : default);
                v.container = _activeBag;
                v.index = k;
            }
        }
```

`PaintSlot` already handles Fish slots (fishColor tint + weight badge) from Phase 2 — bag slots full of fish get rendered correctly with no extra code.

- [ ] **Step 6: Close bag panel on storage close**

Find the existing `Close()` method:
```csharp
    public void Close()
    {
        if (!IsOpen) return;
        _active = null;
        _cursor = default;
        IsOpen = false;
        PlayerController.isInStorage = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        HideCanvas();
    }
```

Replace with:
```csharp
    public void Close()
    {
        if (!IsOpen) return;
        _active = null;
        _cursor = default;
        IsOpen = false;
        PlayerController.isInStorage = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        // Phase 3: bag side panel closes with the main UI.
        if (_bagPanel != null) _bagPanel.gameObject.SetActive(false);
        _activeBag = null;
        HideCanvas();
    }
```

- [ ] **Step 7: Compile check**

Console clean.

- [ ] **Step 8: Behavioral check (deferred to Task 10)**

Skip standalone test — bag-panel verification needs a bag to exist, which needs Task 8 (save support) + Task 9 (vendor assets) to be wired up.

- [ ] **Step 9: Commit**

```bash
git add "Assets/3 - Scripts/UI/StorageUI.cs"
git commit -m "feat(storage): fish bag side panel docked to the right of storage

RMB on a FishBag slot in either container (storage grid OR hotbar
mirror) opens a 5-slot vertical column docked to the right of the
main panel. Same SlotOps drag/drop handles LMB / RMB / Shift+LMB on
those slots. Right-click the bag again closes the panel; closing
storage also closes it.

Cursor-held guard: RMB with a held cursor falls through to the
regular drop-one path (which is a no-op when ids don't match).
"
```

---

## Task 8 — Save schema + capture/apply for `bagContents`

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs` (HotbarSlotSave field)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (helpers + integration into CaptureHotbar / CaptureStorages / ApplyStorages and Hotbar.ApplySlotsFromSave)

- [ ] **Step 1: Add `bagContents` to `HotbarSlotSave`**

In `SaveData.cs`, find:
```csharp
[Serializable]
public class HotbarSlotSave
{
    public string itemId;
    public int count;
    public FishEntrySave fishData;
}
```

Replace with:
```csharp
[Serializable]
public class HotbarSlotSave
{
    public string itemId;
    public int count;
    public FishEntrySave fishData;
    // Phase 3: 5-slot bag contents. null/empty when itemId != "FishBag".
    // JsonUtility serializes null lists as missing-from-JSON so old saves
    // load with bagContents = null — exactly what non-bag slots need.
    // Recursive: each entry can itself carry fishData but NOT another
    // bagContents (no nested bags by design).
    public List<HotbarSlotSave> bagContents;
}
```

- [ ] **Step 2: Add capture/apply helpers in `SaveCollector.cs`**

Add two static helpers near the existing `CaptureHotbar` / `CaptureStorages` methods. Place after `CaptureStorages` and before `CaptureFishInventory`:

```csharp
    // Phase 3: serialize a bag's 5-slot internal array into a list of
    // HotbarSlotSave entries. Recursive but only one level deep — bags
    // can't contain bags by current design; nested bagContents on a
    // saved sub-entry is ignored on load.
    static List<HotbarSlotSave> SerializeBagContents(Hotbar.Slot[] bag)
    {
        var list = new List<HotbarSlotSave>(bag.Length);
        for (int k = 0; k < bag.Length; k++)
        {
            var s = bag[k];
            list.Add(new HotbarSlotSave
            {
                itemId = s.id.ToString(),
                count = s.count,
                fishData = s.id == Hotbar.ItemId.Fish && s.fishData != null
                    ? new FishEntrySave
                      {
                          fishType  = s.fishData.fishType,
                          weightLbs = s.fishData.weightLbs,
                          fishColor = s.fishData.fishColor,
                      }
                    : null,
                bagContents = null,   // no nested bags
            });
        }
        return list;
    }

    // Phase 3: rebuild a 5-element Slot[] from a saved bagContents list.
    // Defensive: pads/truncates to 5 if the saved list is the wrong length.
    static Hotbar.Slot[] DeserializeBagContents(List<HotbarSlotSave> saved)
    {
        var arr = new Hotbar.Slot[5];
        if (saved == null) return arr;
        int max = UnityEngine.Mathf.Min(saved.Count, 5);
        for (int k = 0; k < max; k++)
        {
            var e = saved[k];
            if (e == null) continue;
            if (!System.Enum.TryParse<Hotbar.ItemId>(e.itemId, out var id)) continue;
            int count = UnityEngine.Mathf.Clamp(e.count, 0, Hotbar.StackMax(id));
            if (id == Hotbar.ItemId.None || count <= 0) continue;

            FishEntry fish = null;
            if (id == Hotbar.ItemId.Fish)
            {
                if (e.fishData == null) continue;
                fish = new FishEntry(e.fishData.fishType, e.fishData.weightLbs);
                fish.fishColor = e.fishData.fishColor;
            }
            arr[k] = new Hotbar.Slot { id = id, count = count, fishData = fish };
        }
        return arr;
    }
```

- [ ] **Step 3: Update `CaptureHotbar` to serialize bagContents**

Find `CaptureHotbar` and its slot loop. Inside the loop body, where the `HotbarSlotSave` is constructed, add a `bagContents` field:

Replace:
```csharp
            s.slots.Add(new HotbarSlotSave
            {
                itemId = slot.id.ToString(),
                count = slot.count,
                fishData = slot.id == Hotbar.ItemId.Fish && slot.fishData != null
                    ? new FishEntrySave { ... }
                    : null
            });
```

with:
```csharp
            s.slots.Add(new HotbarSlotSave
            {
                itemId = slot.id.ToString(),
                count = slot.count,
                fishData = slot.id == Hotbar.ItemId.Fish && slot.fishData != null
                    ? new FishEntrySave
                      {
                          fishType  = slot.fishData.fishType,
                          weightLbs = slot.fishData.weightLbs,
                          fishColor = slot.fishData.fishColor,
                      }
                    : null,
                bagContents = slot.id == Hotbar.ItemId.FishBag && slot.bagContents != null
                    ? SerializeBagContents(slot.bagContents)
                    : null,
            });
```

- [ ] **Step 4: Same edit in `CaptureStorages`**

The same `HotbarSlotSave` construction lives inside `CaptureStorages` for storage slots. Apply identical `bagContents` field.

Find the loop body and add the same `bagContents = slot.id == Hotbar.ItemId.FishBag && slot.bagContents != null ? SerializeBagContents(...) : null` field.

- [ ] **Step 5: Update `Hotbar.ApplySlotsFromSave` to restore bagContents**

In `Hotbar.cs`, find `ApplySlotsFromSave` (the Phase 2 version that already handles `fishData`). In the loop body, find:
```csharp
            FishEntry fish = null;
            if (id == ItemId.Fish)
            {
                if (entry.fishData == null) { slots[i] = default; continue; }
                fish = new FishEntry(entry.fishData.fishType, entry.fishData.weightLbs);
                fish.fishColor = entry.fishData.fishColor;
            }
            slots[i] = new Slot { id = id, count = count, fishData = fish };
```

Replace with:
```csharp
            FishEntry fish = null;
            Slot[] bag = null;
            if (id == ItemId.Fish)
            {
                if (entry.fishData == null) { slots[i] = default; continue; }
                fish = new FishEntry(entry.fishData.fishType, entry.fishData.weightLbs);
                fish.fishColor = entry.fishData.fishColor;
            }
            else if (id == ItemId.FishBag)
            {
                bag = SaveCollector.DeserializeBagContentsPublic(entry.bagContents);
            }
            slots[i] = new Slot { id = id, count = count, fishData = fish, bagContents = bag };
```

`DeserializeBagContentsPublic` is a public wrapper around the private `DeserializeBagContents` so `Hotbar.cs` can call it (different file). Add the wrapper in `SaveCollector.cs`:

```csharp
    // Public wrapper so Hotbar.ApplySlotsFromSave can use the same
    // deserializer the storage apply path uses, without exposing the
    // internal HotbarSlotSave-recursion details.
    public static Hotbar.Slot[] DeserializeBagContentsPublic(List<HotbarSlotSave> saved)
        => DeserializeBagContents(saved);
```

- [ ] **Step 6: Same edit in `SaveCollector.ApplyStorages`**

In `ApplyStorages`, find the loop body where `Hotbar.Slot` is constructed and add bag deserialization in the same shape. Replace:
```csharp
                FishEntry fish = null;
                if (id == Hotbar.ItemId.Fish)
                {
                    if (e.fishData == null) { slots[k] = default; continue; }
                    fish = new FishEntry(e.fishData.fishType, e.fishData.weightLbs);
                    fish.fishColor = e.fishData.fishColor;
                }
                slots[k] = new Hotbar.Slot { id = id, count = count, fishData = fish };
```

with:
```csharp
                FishEntry fish = null;
                Hotbar.Slot[] bag = null;
                if (id == Hotbar.ItemId.Fish)
                {
                    if (e.fishData == null) { slots[k] = default; continue; }
                    fish = new FishEntry(e.fishData.fishType, e.fishData.weightLbs);
                    fish.fishColor = e.fishData.fishColor;
                }
                else if (id == Hotbar.ItemId.FishBag)
                {
                    bag = DeserializeBagContents(e.bagContents);
                }
                slots[k] = new Hotbar.Slot { id = id, count = count, fishData = fish, bagContents = bag };
```

- [ ] **Step 7: Compile check**

Console clean. Both `Hotbar.cs` and `SaveCollector.cs` should compile cleanly.

- [ ] **Step 8: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs" "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(save): round-trip bagContents on hotbar + storage slots

HotbarSlotSave.bagContents is a recursive List<HotbarSlotSave> with
one nesting level only (bags can't contain bags). SerializeBagContents
emits the list when slot.id == FishBag; DeserializeBagContents pads/
truncates to 5 on read.

Hotbar.ApplySlotsFromSave + SaveCollector.ApplyStorages both attach
the rebuilt array to slot.bagContents. Old saves load with
bagContents = null — additive schema, no migration needed."
```

---

## Task 9 — Editor menu to create the three ShopItem assets

**Files:**
- Create: `Assets/Editor/CreateFishStorageShopItems.cs`

The three new `ShopItem` ScriptableObject assets that wire Tasks 1-5 into the live vendor inventory must exist as files. Hand-creating each via `Create → Game → Shop Item` works but is tedious. An editor menu makes the operation one click.

- [ ] **Step 1: Create the editor script**

Path: `Assets/Editor/CreateFishStorageShopItems.cs`

```csharp
using UnityEditor;
using UnityEngine;

// One-shot editor helper that creates the three Phase 3 ShopItem assets
// Alien7Vendor needs in its inventory[]. Run via Tools menu; assets land
// in Assets/1 - samsPrefabs/ShopItems/. Idempotent — re-running overwrites
// existing assets with the same values, useful if you tune prices.
public static class CreateFishStorageShopItems
{
    const string Folder = "Assets/1 - samsPrefabs/ShopItems";

    [MenuItem("Tools/Fix/Create Fish & Storage ShopItems")]
    public static void CreateAll()
    {
        EnsureFolder();

        Make("ShopItem_FishingRod", new Setup
        {
            kind = ShopItemKind.FishingRod,
            displayName = "Fishing Rod",
            price = 50,
            description = "Cast bait, reel in fish. Required to harvest the planet's seas.",
        });
        Make("ShopItem_WaterBottle", new Setup
        {
            kind = ShopItemKind.WaterBottle,
            displayName = "Water Bottle",
            price = 30,
            description = "A reusable canteen. Refills at any water source. Drink to restore thirst.",
        });
        Make("ShopItem_FishBag", new Setup
        {
            kind = ShopItemKind.FishBag,
            displayName = "Fish Bag",
            price = 100,
            description = "Holds 5 fish in addition to your hotbar. Caught fish go into the bag first.",
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CreateFishStorageShopItems] Created/updated 3 assets in " + Folder + ". Drop them into Alien7Vendor.inventory in the scene inspector.");
    }

    struct Setup
    {
        public ShopItemKind kind;
        public string displayName;
        public int price;
        public string description;
    }

    static void Make(string assetFileName, Setup s)
    {
        string path = $"{Folder}/{assetFileName}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<ShopItem>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<ShopItem>();
            AssetDatabase.CreateAsset(asset, path);
        }
        asset.kind = s.kind;
        asset.displayName = s.displayName;
        asset.price = s.price;
        asset.description = s.description;
        asset.oneTimePurchase = true;
        // previewPrefab is left null — vendor UI falls back gracefully (or
        // we can wire later for art polish).
        EditorUtility.SetDirty(asset);
    }

    static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Folder)) return;
        const string Parent = "Assets/1 - samsPrefabs";
        if (!AssetDatabase.IsValidFolder(Parent)) AssetDatabase.CreateFolder("Assets", "1 - samsPrefabs");
        AssetDatabase.CreateFolder(Parent, "ShopItems");
    }
}
```

- [ ] **Step 2: Compile check**

Editor menu scripts compile to the `Assembly-CSharp-Editor` assembly automatically. Console clean.

- [ ] **Step 3: Run the menu**

In Unity: `Tools → Fix → Create Fish & Storage ShopItems`. Confirm Console logs `[CreateFishStorageShopItems] Created/updated 3 assets...`. Verify 3 new .asset files exist in `Assets/1 - samsPrefabs/ShopItems/`.

- [ ] **Step 4: Wire assets into Alien7Vendor**

Open the scene (`Assets/1.6.7.7.7.unity`). Find the `Alien7Vendor` GameObject (under `--- NPCs ---/Humble Abode/Alien7` or similar — see hierarchy). In its `Alien7Vendor` component inspector, locate the `inventory` array field. Grow the array by 3 and drag the three new assets into the new slots.

Save the scene.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Editor/CreateFishStorageShopItems.cs" "Assets/1 - samsPrefabs/ShopItems"
git add "Assets/1.6.7.7.7.unity"
git commit -m "feat(vendor): create Fish & Storage shop items + wire into Alien7Vendor

Three ScriptableObject assets (FishingRod 50, WaterBottle 30,
FishBag 100) via Tools/Fix/Create Fish & Storage ShopItems menu.
Dropped into Alien7Vendor.inventory[] in 1.6.7.7.7.unity.

Editor script is idempotent — re-run to update prices/descriptions
in code without manual inspector edits."
```

---

## Task 10 — Full manual regression pass

**Files:** None (verification only)

Run through the 10 acceptance criteria from spec §6. STOP and diagnose any failure; don't continue to Task 11.

- [ ] **Step 1: Vendor sells all three**

Open the Alien7Vendor shop UI in Play. Confirm Fishing Rod (50), Water Bottle (30), Fish Bag (100) appear alongside existing items with correct display names + prices.

- [ ] **Step 2: Rod purchase unlocks the rod**

Buy the rod. Rod appears in hotbar via existing `DetectAcquisitions` flow. Re-open vendor: rod shows "Already Owned" / disabled.

- [ ] **Step 3: Water bottle purchase unlocks the bottle**

Same flow as Step 2.

- [ ] **Step 4: Fish bag purchase spawns the bag**

Buy the bag with money + an empty hotbar slot. Bag appears as a green tile in first empty slot. Re-opening vendor: bag shows "Already Owned".

- [ ] **Step 5: Hotbar full at bag purchase**

Fill all 7 hotbar slots. Try to buy bag. Vendor denies with the new `NoInventorySpace` result. Money NOT charged (check wallet HUD).

- [ ] **Step 6: Bag routes catches first**

With bag in hotbar (any slot) + empty internal bag slots + empty hotbar slots: cast & catch a fish. Open storage (F), right-click the bag — side panel opens to the right showing the new fish in slot 0. Subsequent catches fill slots 1, 2, 3, 4. The 6th catch goes to a hotbar slot.

- [ ] **Step 7: Bag full + hotbar full destroys**

Bag full (5 fish), hotbar full of other items, catch one more. `InventoryFullPopup` flashes. Dex still shows the fish.

- [ ] **Step 8: Storage right-click opens side panel + drag-drop**

In storage UI:
- RMB bag in hotbar mirror → side panel opens to the right of main storage.
- Drag fish from bag slot to storage grid (LMB pickup → LMB drop) — fish moves.
- Drag fish from storage grid to bag slot — fish moves.
- Shift+LMB on a bag slot — quick-moves the fish to the storage grid.
- RMB bag again — side panel closes.
- Close storage entirely — side panel also closes.

- [ ] **Step 9: Save round-trip with fish in bag**

Save with 3 fish in bag, 2 in hotbar, 1 in storage. Quit to main menu. Load. All 6 fish in their original slots. Bag still routes new catches first.

- [ ] **Step 10: Single-instance survives storage transitions**

Move bag from hotbar to storage (LMB pickup, LMB drop in storage grid). Open vendor: bag still shows "Already Owned" (because `HasFishBagAnywhere` scans storage). Move bag back to hotbar. Catches resume routing to bag.

---

## Task 11 — Wrap-up: CLAUDE.md + tag

**Files:**
- Modify: `CLAUDE.md`
- Tag: `fish-revamp-phase-3-complete`

- [ ] **Step 1: Update CLAUDE.md**

Add a paragraph to the Hotbar section (right after the Phase 2 paragraph) noting the fish bag:

```
**Fish bag (Phase 3):** single-instance non-stackable hotbar item from `Alien7Vendor` (`ShopItemKind.FishBag`, $100). Carries a 5-slot `Hotbar.Slot[] bagContents` payload that travels with the bag through drag/drop and storage moves. When equipped (present in any hotbar slot), Bobber's catch flow routes fish into the bag's first empty internal slot before falling back to the hotbar. Storage UI: right-click the bag slot to toggle a 5-slot side panel docked to the right; supports the standard `SlotOps` drag/drop. Save schema: `HotbarSlotSave.bagContents` is a recursive (one-level) `List<HotbarSlotSave>`. Single-instance enforcement: `Hotbar.HasFishBagAnywhere` scans both hotbar and every `StorageRegistry.All` LootBox; vendor's `IsAlreadyOwned(FishBag)` consults it.
```

Also update the goods-vendor list (the existing Alien7Vendor row in the NPC roster table) to mention rod / water bottle in addition to "pistol, axe, etc."

- [ ] **Step 2: Tag the phase**

```bash
git add CLAUDE.md
git commit -m "docs: CLAUDE.md notes for Phase 3 fish bag + vendor expansion"
git tag fish-revamp-phase-3-complete
git status
```

Expected: `nothing to commit, working tree clean`.

- [ ] **Step 3: Hand off**

Phase 3 done. 11 atomic commits + the editor-asset commit; tagged `fish-revamp-phase-3-complete`. Ready for Phase 4 (fish vendor drag-and-drop sell UI, reusing the bag side panel built here).

---

## Self-Review

**Spec coverage** (against spec §2):
- ✓ Vendor sells rod / bottle / bag → Tasks 1, 5, 9
- ✓ Single-instance bag enforcement → Task 3 (HasFishBagAnywhere) + Task 5 (IsAlreadyOwned)
- ✓ `ItemId.FishBag` + `Slot.bagContents` → Task 2
- ✓ `TryAddBag` / `TryAddFishToBag` → Task 3
- ✓ Catch routing bag → hotbar → destroy → Task 4
- ✓ Side panel build + RMB intercept + lifecycle → Task 7
- ✓ Save schema + capture/apply → Task 8
- ✓ Old saves load fine → schema is additive (Task 8); regression covered in Task 10 step 9

**Placeholder scan:** No TODOs, no TBDs. Concrete code in every step.

**Type consistency:**
- `ItemId.FishBag` (Task 2) ↔ used in Tasks 3, 5, 7, 8 ✓
- `Slot.bagContents` (Task 2) ↔ Tasks 3, 4, 7, 8 ✓
- `HotbarSlotSave.bagContents` (Task 8) ↔ SerializeBagContents / DeserializeBagContents helpers in Task 8 ✓
- `Hotbar.TryAddBag()` (Task 3) ↔ called from Alien7Vendor.GrantItem in Task 5 ✓
- `Hotbar.TryAddFishToBag(FishEntry)` (Task 3) ↔ called from Bobber in Task 4 ✓
- `Hotbar.HasFishBagAnywhere()` (Task 3) ↔ called from Alien7Vendor.IsAlreadyOwned in Task 5 ✓
- `Hotbar.HasEmptyHotbarSlot()` (Task 3) ↔ called from Alien7Vendor.Purchase in Task 5 ✓
- `PurchaseResult.NoInventorySpace` (Task 5) — new value; no other code branches on it yet (the vendor UI may need a UX message, deferred to polish)
- `SaveCollector.DeserializeBagContentsPublic` (Task 8) ↔ called from Hotbar.ApplySlotsFromSave in same task ✓

**Scope:** 11 tasks, mix of small (Tasks 1, 4) and large (Tasks 7, 8). Single phase, single PR.

No fixes needed.
