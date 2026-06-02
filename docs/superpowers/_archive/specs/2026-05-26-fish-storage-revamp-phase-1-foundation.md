# Fish & Storage Revamp — Phase 1: Foundation

**Status:** Draft — awaiting user approval
**Date:** 2026-05-26
**Branch:** `feature/fish-storage-revamp` (off commit `600f74c` on `feature/phone-ai-revamp`)
**Author:** Sam McNeil with Claude

---

## 1. Why this phase exists

The user requested a sweeping fishing/storage/vendor revamp that touches eight subsystems with non-trivial coupling: `Hotbar`, `FishInventory`, `FishingdexManager`, `BonfireInteraction`, `FishMarketNPC`, `StorageUI`, `Alien7Vendor`, and the entire save schema. Shipping that as one monolithic change is how features quietly break and how saves get corrupted.

The revamp is therefore decomposed into four independently-shippable phases:

| Phase | Topic |
|---|---|
| **1** | **Foundation** — hotbar slot count + slot data model + save schema (this doc) |
| 2 | Fish flow + dex revamp + hold-to-eat |
| 3 | Goods vendor expansion + fish bag item |
| 4 | Fish vendor revamp (drag-and-drop sell UI) |

Phase 1 is pure infrastructure. It adds capacity for fish to live in containers (hotbar, storage, future fish bag) **without** changing any player-visible behavior other than the hotbar growing from 5 to 7 tiles. Every existing fishing/cooking/selling/dex flow keeps working exactly as it does today. That conservative scope is the whole point — it gives phases 2–4 a stable foundation to build on without forcing a single huge breaking change.

---

## 2. Goals & non-goals

### Goals

- Hotbar capacity grows from 5 to 7 slots.
- `Hotbar.Slot` can carry a per-fish `FishEntry` payload (weight, tier, color).
- The same slot model works for `Hotbar`, `LootBox._slots`, and the future fish bag.
- Save schema can serialize and restore fish-bearing slots in any container.
- Old saves continue to load with no migration step required at this phase boundary.
- All existing fishing/cooking/selling/dex flows pass a manual regression check.

### Non-goals (deferred to later phases)

- Routing newly caught fish into the hotbar — Phase 2.
- Hold-LMB-1s eat-from-hotbar — Phase 2.
- Removing the eat-raw button from the fishingdex — Phase 2.
- Fishingdex becoming a read-only log — Phase 2.
- Goods vendor selling rod / water bottle / fish bag — Phase 3.
- Fish bag item, its 5-slot side panel, and storage right-click integration — Phase 3.
- Fish vendor drag-and-drop sell UI — Phase 4.

---

## 3. Confirmed design decisions

From brainstorming dialogue:

| Decision | Choice |
|---|---|
| Fish stacking | **One fish per slot, no stacking.** Each caught fish is a distinct item taking one slot. A 7-slot hotbar holds max 7 fish (or 6 + an equipped fish bag = 5 more = 11 fish total). Per-fish weight and color preserved for selling and dex. |
| Eat interaction | **Select slot → hold LMB → progress ring on icon → consume at 1s.** Releasing early cancels. No fish mesh rendered in the player's hand. Matches existing equippable selection model. |
| Slot data model | **Additive `FishEntry fishData` field** on the existing `Hotbar.Slot` struct. Null for non-fish slots. Minimum ripple on existing callers. |
| HUD scaling | **Keep 64px tile size, accept wider HUD.** Hotbar grows from ~390px to ~546px wide on a 1080p screen. |

---

## 4. Architecture changes

### 4.1 `Hotbar.cs`

```csharp
// Before
public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust }

public struct Slot
{
    public ItemId id;
    public int count;
}

const int NumSlots = 5;

// After
public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust, Fish }

public struct Slot
{
    public ItemId id;
    public int count;
    public FishEntry fishData;   // null unless id == Fish
}

const int NumSlots = 7;
```

The slot array `readonly Slot[] slots = new Slot[NumSlots];` grows automatically. The visual array `SlotVisuals[] slotViews = new SlotVisuals[NumSlots];` and animation array `Coroutine[] _slotAnimRoutines = new Coroutine[NumSlots];` also grow automatically.

Input handling (number keys 1–5 today) extends to 1–7. Existing code is a single loop over `NumSlots`; no per-key branch to update.

HUD `BuildUI` re-runs procedurally on Awake; with `NumSlots = 7` the layout naturally renders seven tiles centered on screen with the existing `SlotSpacing` (14px) and `SlotSize` (64px).

### 4.2 `FishEntry` accessibility

`FishEntry` already exists in `FishInventory.cs` and is `[System.Serializable]`. No change needed to make it usable as a `Hotbar.Slot` field. It's a reference type (class), so the extra field in `Slot` adds 8 bytes per slot for the reference — trivially cheap.

### 4.3 Save schema

Add a new serializable type in `SaveData.cs`:

```csharp
[System.Serializable]
public class SlotSave
{
    public Hotbar.ItemId id;
    public int count;
    public FishEntrySave fishData;   // null for non-fish slots
}

[System.Serializable]
public class FishEntrySave
{
    public string fishType;          // "Common" | "Uncommon" | "Rare"
    public int weightLbs;
    public Color fishColor;
}
```

(`FishEntrySave` is a flat DTO mirror of `FishEntry` for `JsonUtility` compatibility. The convert-to/from-runtime helpers live in `FishEntrySave` or as static methods on `SlotSave`.)

Then on `PlayerSave`:

```csharp
public List<SlotSave> hotbarSlots = new List<SlotSave>();
```

The save schema already has `HotbarSave` and `StorageSave` types (`SaveData.cs:47-71`). Phase 1 **extends** `HotbarSlotSave` with a `fishData` field; the surrounding `HotbarSave`/`StorageSave` types and the `SaveCollector` capture/apply wiring for both stay unchanged in structure.

### 4.4 `LootBox.cs` save gap

Loot box slot contents **are already saved.** The `[System.NonSerialized]` attribute on `_slots` is not a save gap — it's a **component serialization signal** (tells Unity's editor-side `MonoBehaviour` inspector not to serialize). At runtime, `SaveCollector` captures storage slots by walking `StorageRegistry.All` (enumerating every registered `LootBox` instance), extracting `_slots`, and serializing via `SlotSave` (new in Phase 1). The inverse path in `SaveCollector.Apply` reconstructs and re-populates slots.

**No schema change needed to existing save paths.** The `StorageSave` type already has `List<SlotSave> slots[]` waiting — Phase 1 only adds the `fishData` field to `SlotSave` itself. Slot saving is already wired.

### 4.5 `StorageUI.cs`

Storage UI currently renders icons + count badges for Wood/Crystal/SpaceDust. With `ItemId.Fish` now possible in storage slots, the renderer needs a Fish branch:

- Show a placeholder fish icon (re-use FishingdexManager's per-tier swatch palette or a single neutral fish silhouette).
- Tint by `fishData.fishColor`. (When `id == Fish`, `fishData` is always populated — that invariant is documented at the field declaration.)
- Show weight label (e.g. "3 lb") instead of count (count is always 1 for fish).

This rendering is a **placeholder** for Phase 1. Phase 3 polishes the icon set.

---

## 5. Backward compatibility

- **Old saves load unchanged.** They have no `hotbarSlots` field; deserialization defaults to an empty list; hotbar comes up empty. Wood/Crystal/SpaceDust counts continue to flow through their existing singleton save paths. Every caught fish still lives in `FishInventory` exactly as before.
- **New saves coexist.** Fish in hotbar are saved via `hotbarSlots`; fish in `FishInventory` (the dex log) are still saved via the existing `FishInventorySave`. Both paths are active simultaneously during the Phase 1 → Phase 2 transition.
- **No migration step is required at the Phase 1 → Phase 2 boundary.** Phase 2 will introduce the routing change (caught fish go to hotbar, not FishInventory) but won't need to move existing inventory entries because Phase 1 produces no fish-in-hotbar entries in old saves.

---

## 6. Out-of-scope safeguards

- **No changes to `FishingRodController` catch flow.** Phase 1 leaves `FishInventory.AddFish(...)` calls intact wherever they exist today.
- **No changes to `BonfireInteraction` cook panel.** Cook reads fish counts from `FishInventory` — that still works.
- **No changes to `FishMarketNPC` / `SellPanel`.** Sell flow still reads from `FishInventory.CalculateStagedEarnings` etc.
- **No changes to `FishingdexManager`.** Dex still displays `FishInventory` contents.
- **No new fish prefabs, meshes, or in-hand rendering.** Phase 2 worries about how a fish slot is visualized when selected.

---

## 7. Acceptance criteria (manual Editor regression)

After Phase 1 lands, all six scenarios must pass on a fresh Editor session:

1. **Hotbar visual.** Seven 64px tiles render, centered, 14px spacing. Active-slot lift/glow animations work on every slot.
2. **Input.** Number keys 1–7 each select the corresponding slot. Existing equippables (water bottle, rod, axe, pistol, guitar) still equip via their normal flow (NPC unlock → number key → in-hand visual).
3. **Save round-trip.** Start a session, equip the axe, place some Wood into a loot box (which already works today), catch a fish (it goes into `FishInventory` dex), save. Load the save. Axe equipped, wood in loot box, fish in dex.
4. **Fish catch flow unchanged.** `FishingRodController` catch → fish lands in `FishInventory`. Dex shows it. No fish appears in hotbar (that's Phase 2).
5. **Sell flow unchanged.** Walk to `FishMarketNPC`, open sell panel, stage fish by tier counters, sell, money received. Same UX as today.
6. **Cook flow unchanged.** Place a bonfire, open cook panel, add fish, cook, eat, hunger restored, raw-fish trip when eating raw.

---

## 8. Files touched (estimate)

| File | Change |
|---|---|
| `Assets/3 - Scripts/UI/Hotbar.cs` | `NumSlots` 5→7, `Slot.fishData` field, `ItemId.Fish` enum value, key bindings extend to 7 |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | Add `SlotSave` and `FishEntrySave` types; add `hotbarSlots` to `PlayerSave`; add slot array to whichever schema saves storage |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Capture/apply for hotbar slots + storage slots; convert between runtime `Slot` and `SlotSave` |
| `Assets/3 - Scripts/UI/StorageUI.cs` | Add Fish rendering branch (placeholder icon + weight label) |

Anything beyond this list is scope creep and gets deferred.

---

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Adding `Slot.fishData` field breaks struct copy semantics somewhere | The field is a reference type (FishEntry is a class); struct copy still works. Verify by grepping all `Slot` reads/writes. |
| `LootBox._slots` save gap is bigger than expected | Phase 1 will fix it. If the existing save path is via `StorageRegistry`, we just add slot serialization to that path. If there's no path, we add one. |
| Hotbar HUD looks unbalanced at 7 tiles vs. wallet/vitals card on the right | Screenshot after implementation; if the layout is jarring, file as a Phase 1.5 polish task. Doesn't block. |
| `ItemId.Fish` placeholder rendering looks ugly in storage | It's explicitly a placeholder — Phase 3 polishes the icon set. Don't gold-plate Phase 1. |
| Old saves with a future `Phase 2` fish-routing field fail to load | Not a risk in Phase 1 (no routing change yet). |

---

## 10. What comes next

After this spec is approved:

1. **`writing-plans` skill** turns this spec into a step-by-step implementation plan with explicit review checkpoints. The plan will live at `docs/superpowers/plans/2026-05-26-fish-storage-revamp-phase-1-foundation.md`.
2. Each plan step is a small atomic commit on `feature/fish-storage-revamp`.
3. Phase 1 ships as its own PR (or merges into the AI revamp branch — to be decided based on how the AI revamp is tracking).
4. Phase 2 brainstorm + spec begins after Phase 1 lands and the regression checks pass.
