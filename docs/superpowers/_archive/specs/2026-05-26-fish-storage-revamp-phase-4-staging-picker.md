# Fish & Storage Revamp — Phase 4: Cook + Sell drag-and-drop picker

**Status:** Draft — awaiting user approval
**Date:** 2026-05-26
**Branch:** `feature/fish-storage-revamp` (Phase 3 lands at tag `fish-revamp-phase-3-complete`)
**Author:** Sam McNeil with Claude

---

## 1. Why this phase exists

Phase 2 left the cook + sell panels with a placeholder "Add Fish takes the first hotbar fish of any tier" — minimal migration to keep core game loops alive after the catch routing shift to the hotbar. Phase 4 replaces that placeholder with the proper drag-and-drop **staging picker** the user designed in the original task spec: click "Add Fish" → cursor unlocks → drag fish from hotbar / bag / storage into 10 stage slots → click **Confirm** to commit, or **Cancel** to return them to their exact original slots.

Cook and sell share the same picker — there's no meaningful difference in their staging UX, and one implementation cuts ~half the code, halves the bug surface, and guarantees identical drag behavior between the two flows.

---

## 2. Goals & non-goals

### Goals

- New singleton `FishStagingUI` with a 10-slot (2×5) staging grid + 7-slot hotbar mirror + cursor follower + bag side panel.
- Reuses `SlotOps` for all drag/drop primitives (LMB pickup/deposit/swap, RMB pickup-one/drop-one, Shift+LMB quick-move).
- Reuses Phase 3's RMB-on-FishBag → 5-slot side panel pattern.
- Confirm: gathers `(FishEntry, FishSource)` for each staged fish, invokes caller's callback, closes.
- Cancel: returns each staged fish to its **exact original slot** if still empty; falls back through (same container's next empty → bag → hotbar → destroy + popup).
- `BonfireInteraction.OnAddFishClicked` opens the picker with the cook callback.
- `FishMarketNPC.OnAddFishClicked` opens the picker with the sell callback.
- Cook + sell panels' existing scroll-area stage lists keep working — just receive their fish from the picker now instead of the dex.
- Closing the cook/sell panel mid-session also returns staged fish via the same fallback chain.

### Non-goals (deferred)

- Refactoring `StorageUI`'s bag-side-panel build code into a reusable component shared with `FishStagingUI`. Phase 4 duplicates ~50 lines for safety; refactor candidate later.
- Live cook hunger preview (already exists in the cook panel; not part of the picker).
- Multi-vendor support: only `Alien7Vendor`-style "sell to NPC" flows would reuse the picker. Other vendors keep their existing UIs.
- A separate "see all fish at once" picker variant (e.g. 20-slot for big sells). 10 slots is the chosen cap; commit + reopen for >10 fish.

---

## 3. Confirmed design decisions

| Decision | Choice |
|---|---|
| Shared picker vs separate | One `FishStagingUI` for both cook + sell. |
| Stage slot count | 10 (2 rows × 5). |
| Cancel return behavior | Back to exact original slot; falls back to next-empty-in-container → bag → hotbar → destroy+popup. |
| Source tracking shape | New `FishSource { Slot[] container, int index }` struct; picker keeps a parallel `FishSource[10]` array. Cook/sell stage lists also carry source so their own cancel path works. |
| Cursor source data origin | `SlotOps.CursorState.sourceContainer` / `sourceIndex` (already populated by `PickUpFull` / `PickUpOne`); picker just reads them at drop time. |
| Bag side panel in picker | Yes — RMB on bag in hotbar mirror docks the same 5-slot panel to the right (duplicated logic from `StorageUI` for Phase 4 safety). |

---

## 4. Architecture

### 4.1 `FishStagingUI` singleton

New file: `Assets/3 - Scripts/UI/FishStagingUI.cs`. Modeled after `StorageUI`:

```csharp
public class FishStagingUI : MonoBehaviour
{
    public static FishStagingUI Instance { get; private set; }
    public bool IsOpen { get; private set; }

    // Open the picker. `title` shows at the top ("COOK FISH" / "SELL FISH").
    // `onConfirm` fires when the user clicks the Confirm button; receives the
    // list of staged fish + their original sources. Picker closes after.
    public void Open(string title, System.Action<List<(FishEntry fish, FishSource source)>> onConfirm);

    // External close (e.g. caller's parent panel closing). Cancels with return-to-source.
    public void RequestClose();
}
```

Internals mirror `StorageUI`:
- Canvas + scaler (auto-create, DontDestroyOnLoad).
- 10 stage slot views (`_stageViews[10]`), backed by a runtime `Hotbar.Slot[10] _stageSlots` array.
- 7 hotbar mirror slot views (read live `Hotbar.Instance.RawSlotsRef()`).
- Cursor follower (`_cursorRoot`, `_cursorIcon`, `_cursorFishPreview` — same shape as StorageUI's).
- Bag side panel (`_bagPanel`, `_bagViews[5]`, `_activeBag`) — built identically to StorageUI's.
- Confirm + Cancel buttons docked below stage grid.
- `PlayerController.isInStorage` extended to also flag picker-open state (rename to `isInModalSlotUI` if needed, or add a new flag; plan-writing picks one).

### 4.2 `FishSource` struct

Lives in `SlotOps.cs` (alongside `CursorState`):

```csharp
public struct FishSource
{
    // Reference to the live array the fish was picked from. Mutating
    // container[index] mutates the source slot directly.
    public Hotbar.Slot[] container;
    public int index;

    public bool IsValid => container != null && index >= 0 && index < container.Length;
}
```

### 4.3 Source capture in the picker

When the player drops a fish from cursor into stage slot `k`:
- `SlotOps.HandleLeftClick(_stageSlots, k, ref _cursor)` mutates the slot normally.
- After the call, picker captures: `_sources[k] = new FishSource { container = _cursor.sourceContainer, index = _cursor.sourceIndex };`
- This only matters for slots that just received a deposit (vs the no-op cases); the picker checks "did this drop succeed" by comparing _stageSlots[k] before/after, or simpler: only writes _sources[k] when the slot is now non-empty AND the cursor was previously held.

Actually cleaner: extend `OnSlotClicked` to capture sources after every successful drop. Plan-writing fleshes the exact shape.

### 4.4 Cancel return path

`Cancel()`:
```
for k in 0..9:
    if _stageSlots[k].id != Fish: continue          // skip non-fish (defensive)
    var fe = _stageSlots[k].fishData;
    var src = _sources[k];
    bool placed = TryReturnTo(fe, src);
    if !placed: InventoryFullPopup.Show()           // logged in dex regardless
    _stageSlots[k] = default;
    _sources[k] = default;
Close()
```

`TryReturnTo(FishEntry fe, FishSource src) -> bool`:
1. If `src.IsValid` AND `src.container[src.index].id == None`: place there, return true.
2. Else scan `src.container[]` for next empty slot; if found, place + return true.
3. Else `Hotbar.TryAddFishToBag(fe)` — return true if placed.
4. Else `Hotbar.TryAddFish(fe)` — return true if placed.
5. Else return false (caller pops InventoryFullPopup).

The chain matches the original catch routing's "bag → hotbar → destroy" intent, with the additional first-try of exact-source.

### 4.5 Confirm flow

```csharp
void OnConfirmClicked()
{
    var list = new List<(FishEntry, FishSource)>();
    for (int k = 0; k < 10; k++)
    {
        if (_stageSlots[k].id != Hotbar.ItemId.Fish) continue;
        if (_stageSlots[k].fishData == null) continue;
        list.Add((_stageSlots[k].fishData, _sources[k]));
        _stageSlots[k] = default;
        _sources[k] = default;
    }
    var cb = _onConfirm; _onConfirm = null;
    Close();
    cb?.Invoke(list);
}
```

Caller (cook or sell) processes the list, adding each `(fish, source)` to its own stage list so its own cancel path can also return-to-source if the player closes the cook/sell panel without committing.

### 4.6 Cook panel changes (`BonfireInteraction.cs`)

- `OnAddFishClicked` opens the picker:
  ```csharp
  void OnAddFishClicked()
  {
      if (isCooking || foodReady) return;
      FishStagingUI.Instance?.Open("COOK FISH", entries =>
      {
          foreach (var (fish, source) in entries)
              stagedFish.Add((fish, null, source));   // null RenderTexture; tuple grows by one field
          RefreshUI();
      });
  }
  ```
- `stagedFish` field type grows: `List<(FishEntry fish, RenderTexture rt, FishSource source)>`.
- `OnRemoveFish` (when player clicks "×" on a staged row) returns to source via the same TryReturnTo chain (refactored to a public static on FishStagingUI or duplicated locally).
- `OnCookClicked` cooks the staged fish as before; source info discarded (fish are consumed, no return needed).
- Bonfire-close-with-staged-fish path: same return-to-source flow as cancel.

### 4.7 Sell panel changes (`FishMarketNPC.cs`)

Mirror of cook changes. `OnAddFishClicked` opens picker; `stagedFish` carries source; `OnRemoveFish` returns via chain; `OnConfirmSale` sells (discards sources); cancel path returns.

### 4.8 Cursor / focus integration

`PlayerController.isInStorage` is a static flag StorageUI sets while open. Multiple modal slot UIs would all need their own equivalent. Plan options:

- **Option A:** Add `PlayerController.isInFishStagingPicker` flag. FishStagingUI sets/clears it on Open/Close. Hotbar / Bobber / etc. check both `isInStorage` and `isInFishStagingPicker` to gate input.
- **Option B:** Rename `isInStorage` to `isInModalSlotUI`. Both StorageUI and FishStagingUI set/clear it. Simpler; one flag.

Plan-writing picks B (one flag covers both intents). Rename touches few callsites.

---

## 5. Backward compatibility

- No new save fields. Nothing about the staging picker persists — staged fish are returned to source on close.
- Old saves with fish in cook/sell stage (loaded from a Phase 2 session that crashed mid-stage) are not a concern — cook/sell stages were never saved.
- The Phase 2 cook/sell migration (pull-first-fish) is fully replaced by the picker flow.

---

## 6. Acceptance criteria

After Phase 4 lands, manual Editor regression:

1. **Cook picker opens.** Place bonfire, open cook panel, click Add Fish. New picker overlay appears with 10 slots, hotbar mirror, Confirm + Cancel buttons. Cursor unlocked.
2. **Drag fish hotbar→stage.** LMB on a fish in hotbar mirror picks it up; LMB on empty stage slot drops it. Hotbar slot now empty; stage slot has the fish.
3. **Drag fish from bag.** RMB on bag in hotbar mirror opens 5-slot side panel docked right. Drag a fish from bag slot → stage slot. Works.
4. **Drag stage→hotbar.** Pick up a staged fish and drop it back in hotbar — works (cancels staging for that fish).
5. **Confirm cook.** Stage 3 fish, click Confirm. Picker closes. Cook panel scroll list shows the 3 fish. Click Cook → 10s timer → Eat → hunger up.
6. **Cancel mid-stage.** Stage 3 fish, click Cancel. Picker closes. All 3 fish back in their original hotbar/bag slots (the exact slots they were dragged from).
7. **Bonfire close returns staged fish.** Stage 3 fish, confirm them into cook stage, then close the bonfire panel WITHOUT cooking. The 3 fish return to original sources.
8. **Sell picker.** FishMarketNPC → Add Fish → picker. Same drag flow. Confirm → money up. Cancel → fish back to source.
9. **Source occupied edge case.** Stage a fish from hotbar slot 3. While picker is open, somehow fill hotbar slot 3 (catch via Bobber — though new catches should also route bag-first; for the edge case, manually arrange it). Click Cancel. The staged fish goes to first available slot in hotbar (since slot 3 is occupied), not destroyed.
10. **All-full destroy edge.** Fill hotbar, fill bag, fill source storage. Stage a fish (it has to come from somewhere — assume picked up before everything filled). Cancel. Fish destroyed; InventoryFullPopup; dex still has it.

---

## 7. Files touched (estimate)

| File | Change |
|---|---|
| `Assets/3 - Scripts/UI/FishStagingUI.cs` | **New.** Singleton picker UI: 10 stage slots, hotbar mirror, bag side panel, cursor follower, Confirm/Cancel buttons, `Open` + `Close` + `OnConfirmClicked` + `OnCancelClicked` + `TryReturnTo` |
| `Assets/3 - Scripts/UI/SlotOps.cs` | Add `FishSource` struct (small) |
| `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs` | `OnAddFishClicked` → opens picker; `stagedFish` tuple gains `FishSource`; `OnRemoveFish` + bonfire-close return via TryReturnTo |
| `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` | Same surgery as cook |
| `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` | Rename `isInStorage` → `isInModalSlotUI` (or add a new flag and reference both — plan picks one); update callsites in StorageUI, Hotbar, possibly others |

5 files. New singleton is the bulk of the work (~400 lines mirroring StorageUI shape).

---

## 8. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `FishStagingUI` duplicates ~50 lines of bag-side-panel UI from `StorageUI` | Acknowledged. Refactoring into a shared component is a Phase 5 cleanup task. Phase 4 keeps risk low. |
| Player closes cook/sell panel via Esc / interact-trigger-exit while picker is open on top | Picker's RequestClose triggers from the cook/sell close path too (or just-close-everything in the cook/sell close handler). Sources still returned. |
| Cursor held when picker closes externally | `Close()` calls `SlotOps.ReturnHeldToSource(ref _cursor)` before clearing state. Same defensive pattern StorageUI uses. |
| Source slot's container array (e.g. a bag) gets nulled or replaced between stage and confirm/cancel | `FishSource.container` holds a reference. If the bag is destroyed (single-instance guarantee says no, but defensive), `TryReturnTo` falls through to bag-or-hotbar-or-destroy. Safe. |
| Renaming `PlayerController.isInStorage` breaks callsites outside the fish revamp | Grep before renaming. If many callsites scattered across systems, prefer Option A (add `isInFishStagingPicker` flag, check both). Plan-writing decides. |
| Picker's hotbar mirror clicks could fire Hotbar input (number keys) if not gated | `TickEatHold` and `HandleInput` in Hotbar already gate on `PlayerController.isInStorage` → renamed flag covers picker. |

---

## 9. What comes next

After this spec is approved, `writing-plans` skill turns it into an atomic step-by-step plan at `docs/superpowers/plans/2026-05-26-fish-storage-revamp-phase-4-staging-picker.md`. Once Phase 4 lands and regression passes, the fish/storage revamp is feature-complete. Any cleanup (`StorageUI` bag-panel refactor, dormant `FishInventory` drain methods) would be Phase 5 polish, optional.
