# Fish & Storage Revamp — Phase 1 (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bump the Hotbar from 5 → 7 slots, extend every container slot to carry per-fish `FishEntry` payload (weight/color/tier), and round-trip that payload through the existing save system. Zero player-visible behavior change beyond the wider hotbar.

**Architecture:** Additive — extends the existing `Hotbar.Slot` struct with one `FishEntry fishData` field, extends the existing `HotbarSlotSave` schema with one matching field, and threads that field through capture/apply paths plus the drag-and-drop cursor in `SlotOps`. The Hotbar/Storage save plumbing already exists; the spec's "save gap" concern was incorrect (verified during plan-writing — see Task 1).

**Tech Stack:** Unity 2022.3, C#, default `Assembly-CSharp` (no asmdefs). `JsonUtility` for saves. No test framework — verification is manual Editor regression.

---

## File Structure

Files modified (no new files in Phase 1):

| File | Responsibility | Changes |
|---|---|---|
| `Assets/3 - Scripts/UI/Hotbar.cs` | Hotbar singleton + slot data model | `ItemId.Fish` enum value; `Slot.fishData` field; `NumSlots` 5→7; `ApplySlotsFromSave` reads fishData |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | JsonUtility schema | Add `FishEntrySave` class; extend `HotbarSlotSave` with `fishData` field |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Capture/apply singleton state | `CaptureHotbar`, `CaptureStorages`, `ApplyStorages` write/read fishData |
| `Assets/3 - Scripts/UI/SlotOps.cs` | Pure slot-mutation helpers + cursor state | `CursorState.fishData` field; pick/deposit/swap/quick-move preserve fishData |
| `Assets/3 - Scripts/UI/StorageUI.cs` | Storage panel UI | `HotbarSlots` 5→7 + panel resize; `PaintSlot`/`ResolveIcon` handle Fish |
| `docs/superpowers/specs/2026-05-26-fish-storage-revamp-phase-1-foundation.md` | Phase 1 spec | Correct §4.4 — save gap was wrong, schema already exists |

No file is created. No prefab/scene changes. No new singletons or auto-creation.

---

## Verification Strategy

Unity has no test framework here — `Assembly-CSharp` is the only assembly and there are no `.asmdef` files for editor tests. Verification per task is:

1. **Code change** (with full code shown).
2. **Compile check** — open Unity, watch the Console for compile errors. The Console must be clean before continuing.
3. **Behavioral check** — described per task (e.g., "press 6 in play mode, confirm slot 6 highlights"). Skip task-level checks for pure schema additions that have no player-facing effect; cover them in the Task 12 full regression pass.
4. **Commit** — single focused commit per task so any task can be reverted independently.

---

## Task 1 — Correct the spec's save-gap claim

**Files:**
- Modify: `docs/superpowers/specs/2026-05-26-fish-storage-revamp-phase-1-foundation.md` (sections 1, 4.3, 4.4, 8)

The spec, written before reading `SaveCollector.cs`, says `LootBox._slots` doesn't round-trip through saves and flags this as an investigation task. That's wrong. Verified during plan-writing: `SaveData.HotbarSave` + `SaveData.StorageSave` already exist (SaveData.cs:47-71), and `SaveCollector.CaptureHotbar`/`CaptureStorages`/`ApplyStorages` already wire them up (SaveCollector.cs:271-308, 933-966). The `[NonSerialized]` on `LootBox._slots` is only about Unity component serialization (prefab/scene authoring); runtime slot state IS saved via `StorageRegistry.All` + `HotbarSlotSave`.

The actual Phase 1 save work is **extending** the existing schema with a `fishData` field, not building a new save path.

- [ ] **Step 1: Open the spec at section 1**

Find the table at the bottom of section 1. The "Investigation" row in §4.4 referenced from §1 is misleading.

- [ ] **Step 2: Replace section 4.3 last paragraph and section 4.4 entirely**

Find this block in the spec:

```markdown
And `PlacedBuildingSave` (or whichever schema currently saves loot box state — to be confirmed during plan-writing) gains:

```csharp
public List<SlotSave> storageSlots = new List<SlotSave>();
```
```

Replace it with:

```markdown
The save schema already has `HotbarSave` and `StorageSave` types (`SaveData.cs:47-71`). Phase 1 **extends** `HotbarSlotSave` with a `fishData` field; the surrounding `HotbarSave`/`StorageSave` types and the `SaveCollector` capture/apply wiring for both stay unchanged in structure.
```

Then find section 4.4 (the entire `### 4.4 LootBox.cs save gap` section) and replace its body with:

```markdown
The `[System.NonSerialized]` attribute on `LootBox._slots` is **not** a save gap — it only opts out of Unity's *component* serialization (prefab/scene authoring), which is correct since slot contents are runtime state. Runtime slot contents ARE saved: `StorageRegistry.All` enumerates live loot boxes for `SaveCollector.CaptureStorages` (lines 287-308), and `SaveCollector.ApplyStorages` (lines 933-966) restores them by `boxId` match.

No `LootBox.cs` change is needed in Phase 1. The `[NonSerialized]` stays.
```

Also update §8's "files touched" table — remove `LootBox.cs` from the list.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-05-26-fish-storage-revamp-phase-1-foundation.md
git commit -m "docs(fish-revamp): correct spec — no LootBox save gap, schema already exists

Verified during plan-writing: SaveData.HotbarSave + StorageSave already
exist and SaveCollector already wires capture/apply for both. The spec's
LootBox '[NonSerialized] _slots' concern was about Unity component
serialization, not runtime save state. Phase 1 extends HotbarSlotSave
with fishData; no new save path needed."
```

---

## Task 2 — Add `Fish` to `Hotbar.ItemId` enum + `fishData` to `Slot`

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs:9-15`

- [ ] **Step 1: Edit the enum and struct**

Replace lines 9-15 of `Hotbar.cs` (the `ItemId` enum and `Slot` struct):

```csharp
public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust, Fish }

public struct Slot
{
    public ItemId id;
    public int count;
    // Populated only when id == ItemId.Fish. Null otherwise. Carries the
    // per-fish weight/color/tier so dragging a fish through the cursor or
    // round-tripping through saves preserves the data the dex and sell
    // flow rely on.
    public FishEntry fishData;
}
```

`FishEntry` is in `Assets/3 - Scripts/Fishing/FishInventory.cs` and is `[System.Serializable]`. It's in the global namespace (no `namespace` declaration), same as `Hotbar`, so no `using` needed.

- [ ] **Step 2: Compile check**

Switch to Unity. The Console must show no compile errors.

Note: The `_ => 1` arm in `Hotbar.StackMax` (line 183) already covers `Fish` — fish are non-stackable so a stack-max of 1 is correct. No edit needed.

- [ ] **Step 3: Commit**

```bash
git add Assets/3\ -\ Scripts/UI/Hotbar.cs
git commit -m "feat(hotbar): add ItemId.Fish + Slot.fishData payload field

Foundation for fish-in-hotbar (Phase 2). FishEntry reference is null
for non-fish slots — zero overhead in practice. StackMax(Fish) falls
through to the default arm returning 1, which is correct: fish are
one-per-slot, never stackable."
```

---

## Task 3 — Bump `Hotbar.NumSlots` 5 → 7

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs:17`

- [ ] **Step 1: Edit the constant**

Replace line 17:

```csharp
const int NumSlots = 5;
```

with:

```csharp
const int NumSlots = 7;
```

The HUD layout (`BuildUI`), input (`HandleInput` → `TutorialGate.HotbarSlotPressed(NumSlots)` which loops `KeyCode.Alpha1..AlphaN`), and animation arrays (`_slotAnimRoutines`, `slotViews`) all derive from `NumSlots` — they grow automatically. No other code edit needed in Phase 1.

- [ ] **Step 2: Compile check + visual check**

Open Unity, enter Play mode in `1.6.7.7.7.unity`. The hotbar at the bottom-center of the screen should show **seven** tiles instead of five. The two new tiles will be empty.

- [ ] **Step 3: Input check**

Still in Play mode, with the axe equipped (touch the axe NPC or use a save where the axe is unlocked):

1. Press **1** through **5** — should behave exactly as before (axe equips on whichever slot holds it, others highlight).
2. Press **6** and **7** — slot highlight should land on those new tiles (no item to equip, so nothing visible in hand, but the active-tile lift animation should play).

- [ ] **Step 4: Commit**

```bash
git add Assets/3\ -\ Scripts/UI/Hotbar.cs
git commit -m "feat(hotbar): bump slot capacity 5 -> 7

HUD widens to ~546px (7 tiles × 64px + 6 gaps × 14px = 532 + accent).
Input loop in TutorialGate.HotbarSlotPressed already takes NumSlots
as a parameter, so keys 6 and 7 (KeyCode.Alpha6/7) auto-bind.
BuildUI is procedural and iterates NumSlots — no layout edit needed."
```

---

## Task 4 — Add `FishEntrySave` + extend `HotbarSlotSave`

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs:53-58`

- [ ] **Step 1: Edit HotbarSlotSave and add FishEntrySave**

Replace lines 53-58 of `SaveData.cs` (the `HotbarSlotSave` definition):

```csharp
[Serializable]
public class HotbarSlotSave
{
    public string itemId;  // Hotbar.ItemId enum.ToString(): "None", "Wood", "Pistol", ...
    public int count;
    // Populated only when itemId == "Fish". null otherwise. JsonUtility
    // serializes null-valued class fields as missing-from-JSON, so old
    // saves loading this schema get fishData = null automatically (the
    // correct default for non-fish slots in pre-Phase 1 saves).
    public FishEntrySave fishData;
}

// Flat DTO mirror of FishEntry for JsonUtility. Lives alongside
// HotbarSlotSave so any slot in any container (hotbar, storage, future
// fish bag) can carry per-fish data.
[Serializable]
public class FishEntrySave
{
    public string fishType;          // "Common" | "Uncommon" | "Rare"
    public int weightLbs;
    public Color fishColor;
}
```

`FishInventorySave.Entry` (lines 199-209) has the same shape but lives nested inside `FishInventorySave`. We don't reuse it because slot-level fish data should be addressable as a standalone type — Phase 3 will need it in fish-bag save schemas too.

- [ ] **Step 2: Compile check**

Switch to Unity. Console must be clean.

- [ ] **Step 3: Old-save load smoke test**

Load an existing save (autosave or any save from `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\`). The save should load without errors. Equipment, money, fish-in-dex all restore as before. Nothing visibly changed yet — Tasks 5-8 wire the new field through capture/apply.

- [ ] **Step 4: Commit**

```bash
git add Assets/3\ -\ Scripts/SaveSystem/SaveData.cs
git commit -m "feat(save): HotbarSlotSave.fishData + FishEntrySave DTO

Additive schema change. Old saves loading the new code get fishData
= null on every slot, which is the correct 'no fish here' default
for pre-feature saves. New code paths fill fishData on capture for
slots whose id is Fish (Tasks 5-8 wire this up)."
```

---

## Task 5 — `SaveCollector.CaptureHotbar` writes `fishData`

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs:271-285`

- [ ] **Step 1: Edit CaptureHotbar**

Replace the loop body in `CaptureHotbar` (lines 277-284) with the fishData-aware version:

```csharp
static void CaptureHotbar(HotbarSave s)
{
    if (s == null) return;
    s.slots.Clear();
    if (Hotbar.Instance == null) return;
    var live = Hotbar.Instance.GetSlotsForSave();
    for (int i = 0; i < live.Count; i++)
    {
        var slot = live[i];
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
                : null
        });
    }
}
```

The defensive `slot.fishData != null` check covers the edge case where someone (a bug, future Phase 2 routing) sets `id = Fish` but forgets to populate `fishData`. We log nothing here — capture just emits `null` and the slot will deserialize to an empty-fish entry that the apply path can discard.

- [ ] **Step 2: Compile check**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/3\ -\ Scripts/SaveSystem/SaveCollector.cs
git commit -m "feat(save): CaptureHotbar writes Slot.fishData to FishEntrySave

Defensive: only emits fishData when id == Fish AND the runtime payload
is non-null. Phase 2 will start producing fish-in-hotbar slots; until
then this code path is dormant but verified via Task 12 regression."
```

---

## Task 6 — `Hotbar.ApplySlotsFromSave` reads `fishData`

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs:270-289`

- [ ] **Step 1: Edit ApplySlotsFromSave**

Replace lines 270-289 (the entire `ApplySlotsFromSave` method) with the fishData-aware version:

```csharp
public void ApplySlotsFromSave(List<HotbarSlotSave> saved)
{
    // Clear current.
    for (int i = 0; i < NumSlots; i++) slots[i] = default;
    if (saved == null) return;
    int max = Mathf.Min(saved.Count, NumSlots);
    for (int i = 0; i < max; i++)
    {
        var entry = saved[i];
        if (entry == null) continue;
        if (!System.Enum.TryParse<ItemId>(entry.itemId, out var id)) continue;
        int count = Mathf.Clamp(entry.count, 0, StackMax(id));
        if (id == ItemId.None || count <= 0) { slots[i] = default; continue; }

        FishEntry fish = null;
        if (id == ItemId.Fish)
        {
            if (entry.fishData == null) { slots[i] = default; continue; }
            fish = new FishEntry(entry.fishData.fishType, entry.fishData.weightLbs);
            fish.fishColor = entry.fishData.fishColor;
        }
        slots[i] = new Slot { id = id, count = count, fishData = fish };
    }
    // Notify subscribers (facades) so their OnChanged fires once each.
    OnResourceChanged?.Invoke(ItemId.Wood);
    OnResourceChanged?.Invoke(ItemId.Crystal);
    OnResourceChanged?.Invoke(ItemId.SpaceDust);
}
```

The defensive `entry.fishData == null` skip handles a save where someone wrote `itemId = "Fish"` but no `fishData` — we drop the slot rather than spawning a ghost fish. Same shape as `ApplyFishInventory` (SaveCollector.cs:968-979) — `new FishEntry(type, weight)` followed by direct `fishColor` assignment because FishEntry's constructor randomizes `fishColor` if you let it run unmodified.

- [ ] **Step 2: Compile check**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/3\ -\ Scripts/UI/Hotbar.cs
git commit -m "feat(save): ApplySlotsFromSave restores fishData on Fish slots

Matches ApplyFishInventory's constructor-then-overwrite-color pattern
to preserve the saved color (FishEntry's ctor randomizes fishColor
otherwise). Defensive null-skip on entry.fishData drops malformed
Fish slots rather than spawning ghost entries."
```

---

## Task 7 — `SaveCollector.CaptureStorages` writes `fishData`

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs:287-308`

- [ ] **Step 1: Edit CaptureStorages**

Replace lines 296-307 (the inner-loop slot-copy block) with the fishData-aware version. Full method should read:

```csharp
static void CaptureStorages(System.Collections.Generic.List<StorageSave> list)
{
    if (list == null) return;
    list.Clear();
    var live = StorageRegistry.All;
    for (int i = 0; i < live.Count; i++)
    {
        var box = live[i];
        if (box == null) continue;
        var entry = new StorageSave { boxId = box.BoxId };
        var slots = box.Slots;
        for (int j = 0; j < slots.Length; j++)
        {
            var slot = slots[j];
            entry.slots.Add(new HotbarSlotSave
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
                    : null
            });
        }
        list.Add(entry);
    }
}
```

This is the same fishData branch as Task 5; the only difference is the loop iterates `StorageRegistry.All` instead of `Hotbar.Instance`.

- [ ] **Step 2: Compile check**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/3\ -\ Scripts/SaveSystem/SaveCollector.cs
git commit -m "feat(save): CaptureStorages writes fishData for Fish slots

Same fishData branch as CaptureHotbar — symmetric capture across both
slot-bearing containers. Phase 2 routes catches into hotbar (not
storage), but Phase 3 will allow fish moves into storage via the
existing storage UI drag-drop, so storage needs the fishData path
ready before Phase 3 starts."
```

---

## Task 8 — `SaveCollector.ApplyStorages` reads `fishData`

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs:933-966`

- [ ] **Step 1: Edit ApplyStorages**

Replace the inner restoration loop (lines 953-964) with the fishData-aware version. Full method should read:

```csharp
static void ApplyStorages(System.Collections.Generic.List<StorageSave> list)
{
    if (list == null) return;
    var live = StorageRegistry.All;
    for (int i = 0; i < list.Count; i++)
    {
        var saved = list[i];
        if (saved == null || string.IsNullOrEmpty(saved.boxId)) continue;

        LootBox match = null;
        for (int j = 0; j < live.Count; j++)
        {
            if (live[j] != null && live[j].BoxId == saved.boxId) { match = live[j]; break; }
        }
        if (match == null)
        {
            UnityEngine.Debug.LogWarning($"[Storage] no live LootBox for saved boxId '{saved.boxId}' — dropping");
            continue;
        }

        var slots = match.Slots;
        for (int k = 0; k < slots.Length; k++) slots[k] = default;
        int max = UnityEngine.Mathf.Min(saved.slots.Count, slots.Length);
        for (int k = 0; k < max; k++)
        {
            var e = saved.slots[k];
            if (e == null) continue;
            if (!System.Enum.TryParse<Hotbar.ItemId>(e.itemId, out var id)) continue;
            int count = UnityEngine.Mathf.Clamp(e.count, 0, Hotbar.StackMax(id));
            if (id == Hotbar.ItemId.None || count <= 0) { slots[k] = default; continue; }

            FishEntry fish = null;
            if (id == Hotbar.ItemId.Fish)
            {
                if (e.fishData == null) { slots[k] = default; continue; }
                fish = new FishEntry(e.fishData.fishType, e.fishData.weightLbs);
                fish.fishColor = e.fishData.fishColor;
            }
            slots[k] = new Hotbar.Slot { id = id, count = count, fishData = fish };
        }
    }
}
```

Same fishData restoration shape as Task 6 (`Hotbar.ApplySlotsFromSave`).

- [ ] **Step 2: Compile check**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/3\ -\ Scripts/SaveSystem/SaveCollector.cs
git commit -m "feat(save): ApplyStorages restores fishData on Fish slots

Mirrors ApplySlotsFromSave restoration shape. Defensive null-skip
matches the hotbar path. Closes the storage end of the fishData
round-trip."
```

---

## Task 9 — `SlotOps.CursorState` carries `fishData`

**Files:**
- Modify: `Assets/3 - Scripts/UI/SlotOps.cs:9-16, 70-92, 94-152, 166-201`

This is the largest single task — six handlers need to thread `fishData` from slot to cursor to destination. The pattern is consistent: any time a `Hotbar.Slot { ... }` is constructed, copy `fishData` from wherever the source data came from.

- [ ] **Step 1: Edit CursorState struct**

Replace lines 9-16:

```csharp
public struct CursorState
{
    public Hotbar.ItemId id;
    public int count;
    public Hotbar.Slot[] sourceContainer;   // null when not held
    public int sourceIndex;
    // For Fish cursors: the FishEntry payload travels with the cursor so
    // dropping it into any destination slot restores the full per-fish data.
    // Null for non-Fish cursors.
    public FishEntry fishData;
    public bool IsHeld => id != Hotbar.ItemId.None && count > 0;
}
```

- [ ] **Step 2: Edit PickUpFull**

Replace lines 70-79:

```csharp
static void PickUpFull(Hotbar.Slot[] container, int idx, ref CursorState cursor)
{
    var s = container[idx];
    if (s.id == Hotbar.ItemId.None || s.count <= 0) return;
    cursor.id = s.id;
    cursor.count = s.count;
    cursor.sourceContainer = container;
    cursor.sourceIndex = idx;
    cursor.fishData = s.fishData;   // carry payload onto cursor
    container[idx] = default;
}
```

- [ ] **Step 3: Edit PickUpOne**

Replace lines 81-92:

```csharp
static void PickUpOne(Hotbar.Slot[] container, int idx, ref CursorState cursor)
{
    var s = container[idx];
    if (s.id == Hotbar.ItemId.None || s.count <= 0) return;
    cursor.id = s.id;
    cursor.count = 1;
    cursor.sourceContainer = container;
    cursor.sourceIndex = idx;
    cursor.fishData = s.fishData;   // single-fish slots: payload moves to cursor
    s.count -= 1;
    if (s.count <= 0) container[idx] = default;
    else              container[idx] = s;
}
```

Fish slots have `count = 1` so `PickUpOne` empties the slot completely, identical to `PickUpFull` for fish. The non-fish `count > 1` case (Wood etc.) was already correct because their fishData is null.

- [ ] **Step 4: Edit Deposit**

Replace lines 94-127:

```csharp
static void Deposit(Hotbar.Slot[] container, int idx, ref CursorState cursor)
{
    var s = container[idx];

    // Empty slot — drop the whole cursor here.
    if (s.id == Hotbar.ItemId.None || s.count <= 0)
    {
        container[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData };
        ClearCursor(ref cursor);
        return;
    }

    // Same id — try to merge.
    if (s.id == cursor.id)
    {
        int cap = Hotbar.StackMax(s.id);
        int room = cap - s.count;
        int moved = Mathf.Min(room, cursor.count);
        if (moved <= 0) return; // dest full of same item — no-op
        s.count += moved;
        container[idx] = s;
        cursor.count -= moved;
        if (cursor.count <= 0) ClearCursor(ref cursor);
        return;
    }

    // Different id — swap cursor with slot.
    var temp = s;
    container[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData };
    cursor.id = temp.id;
    cursor.count = temp.count;
    cursor.fishData = temp.fishData;   // swap pulls slot payload onto cursor
    // sourceContainer/sourceIndex stay as the original pickup origin —
    // that's where return-on-close should put it.
}
```

Note: the same-id merge branch is unreachable for Fish because `StackMax(Fish) == 1` so `room = 1 - 1 = 0`. That's correct — two fish dropped onto each other should swap, not merge. The if-`moved <= 0`-no-op early-return covers it.

- [ ] **Step 5: Edit DepositOne**

Replace lines 129-152:

```csharp
static void DepositOne(Hotbar.Slot[] container, int idx, ref CursorState cursor)
{
    var s = container[idx];

    // Empty slot — drop one.
    if (s.id == Hotbar.ItemId.None || s.count <= 0)
    {
        container[idx] = new Hotbar.Slot { id = cursor.id, count = 1, fishData = cursor.fishData };
        cursor.count -= 1;
        if (cursor.count <= 0) ClearCursor(ref cursor);
        else if (cursor.id == Hotbar.ItemId.Fish) cursor.fishData = null;   // single-fish cursor: payload moved off
        return;
    }

    // Different id — RMB-on-different is a no-op (don't swap on right click).
    if (s.id != cursor.id) return;

    // Same id — drop one if room.
    int cap = Hotbar.StackMax(s.id);
    if (s.count >= cap) return;
    s.count += 1;
    container[idx] = s;
    cursor.count -= 1;
    if (cursor.count <= 0) ClearCursor(ref cursor);
}
```

The `cursor.fishData = null` after a Fish drop-one is defensive — for Fish, `cursor.count` was 1 going in, so the `<=0` ClearCursor branch handles it. The explicit null is for the theoretical case where future code lets fish stacks form.

- [ ] **Step 6: Edit ClearCursor**

Replace lines 154-160:

```csharp
static void ClearCursor(ref CursorState cursor)
{
    cursor.id = Hotbar.ItemId.None;
    cursor.count = 0;
    cursor.sourceContainer = null;
    cursor.sourceIndex = -1;
    cursor.fishData = null;
}
```

- [ ] **Step 7: Edit HandleQuickMove**

Replace lines 35-68 (the entire method):

```csharp
// Shift+LMB on a slot: instantly move stack to the other container.
public static void HandleQuickMove(Hotbar.Slot[] source, int idx, Hotbar.Slot[] dest)
{
    if (source == null || dest == null || idx < 0 || idx >= source.Length) return;
    var s = source[idx];
    if (s.id == Hotbar.ItemId.None || s.count <= 0) return;

    int remaining = s.count;
    int cap = Hotbar.StackMax(s.id);

    // Fill existing stacks of the same id first.
    if (cap > 1)
    {
        for (int i = 0; i < dest.Length && remaining > 0; i++)
        {
            if (dest[i].id != s.id) continue;
            int room = cap - dest[i].count;
            if (room <= 0) continue;
            int take = Mathf.Min(room, remaining);
            dest[i].count += take;
            remaining -= take;
        }
    }
    // Spill into empty slots. For Fish, cap == 1 so this is the only branch
    // that runs and fishData transfers cleanly with the slot.
    for (int i = 0; i < dest.Length && remaining > 0; i++)
    {
        if (dest[i].id != Hotbar.ItemId.None) continue;
        int take = Mathf.Min(cap, remaining);
        dest[i] = new Hotbar.Slot { id = s.id, count = take, fishData = s.fishData };
        remaining -= take;
    }

    if (remaining == 0) source[idx] = default;
    else                source[idx] = new Hotbar.Slot { id = s.id, count = remaining, fishData = s.fishData };
}
```

For Fish, `cap = 1` so the merge loop is skipped (correct — fish don't merge). The spill loop runs once, creates a new slot in dest with `fishData = s.fishData`, then sets remaining = 0 and clears source.

- [ ] **Step 8: Edit ReturnHeldToSource**

Replace lines 166-201:

```csharp
public static bool ReturnHeldToSource(ref CursorState cursor)
{
    if (!cursor.IsHeld) return true;
    var src = cursor.sourceContainer;
    if (src == null) return false;
    int idx = cursor.sourceIndex;
    if (idx >= 0 && idx < src.Length)
    {
        var s = src[idx];
        if (s.id == Hotbar.ItemId.None || s.count <= 0)
        {
            src[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData };
            ClearCursor(ref cursor);
            return true;
        }
        if (s.id == cursor.id)
        {
            int cap = Hotbar.StackMax(s.id);
            int room = cap - s.count;
            int moved = Mathf.Min(room, cursor.count);
            s.count += moved;
            src[idx] = s;
            cursor.count -= moved;
            if (cursor.count <= 0) { ClearCursor(ref cursor); return true; }
        }
    }
    // Source slot occupied differently — spill to first empty in source.
    for (int i = 0; i < src.Length; i++)
    {
        if (src[i].id != Hotbar.ItemId.None) continue;
        src[i] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData };
        ClearCursor(ref cursor);
        return true;
    }
    return false; // caller blocks close
}
```

The merge-to-same-id branch is unreachable for Fish (cap == 1) — covered by the spill loop or the empty-source case above.

- [ ] **Step 9: Compile check**

Console clean.

- [ ] **Step 10: Behavioral check — Wood drag still works**

Open Unity, play in `1.6.7.7.7.unity`. Walk to a loot box on the ship, open it (F). With wood in inventory:

1. Shift+click a wood stack in the hotbar — moves to storage.
2. Click + click a wood stack in storage — picks up + drops back.
3. Right-click a stack — splits one off into cursor; right-click again to drop.

All three should work identically to before. (Fish paths aren't testable yet because no fish is in hotbar — Phase 2 wires that.)

- [ ] **Step 11: Commit**

```bash
git add Assets/3\ -\ Scripts/UI/SlotOps.cs
git commit -m "feat(slots): thread Slot.fishData through cursor + all operations

CursorState gains fishData; PickUpFull/PickUpOne/Deposit/DepositOne/
HandleQuickMove/ReturnHeldToSource all preserve the payload. Wood/
Crystal/SpaceDust paths unchanged behaviorally — fishData is null
on their slots so the new copy is a no-op. Fish-specific paths are
dormant until Phase 2 produces in-hotbar fish."
```

---

## Task 10 — `StorageUI.HotbarSlots` 5 → 7 + resize panel

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs:30` and verify auto-layout

- [ ] **Step 1: Edit the constant**

Replace line 30:

```csharp
const int HotbarSlots = 5;
```

with:

```csharp
const int HotbarSlots = 7;
```

- [ ] **Step 2: Verify panel auto-resize**

Read the rest of the storage panel BuildCanvas. The relevant calc at lines 377-378:

```csharp
float hotbarW = HotbarSlots * SlotSize + (HotbarSlots - 1) * SlotGap;
```

With HotbarSlots=7 and SlotSize=84, SlotGap=6: `hotbarW = 7*84 + 6*6 = 588 + 36 = 624`. Panel width is `gridW + PanelPad * 2 = 5*84 + 4*6 + 64 = 420 + 24 + 64 = 508`. So the hotbar row (624px) is now wider than the storage grid (420px). The panel width (508px) is dictated by the storage grid, not the hotbar.

This means the hotbar row will overflow the panel on left and right (~58px each side). The panel needs to grow to accommodate the wider hotbar.

Edit `BuildCanvas` to use the larger of `gridW` and `hotbarW` for `panelW`. Replace lines 306-315:

```csharp
float gridW  = StorageCols * SlotSize + (StorageCols - 1) * SlotGap;
float gridH  = StorageRows * SlotSize + (StorageRows - 1) * SlotGap;
float hotbarRowW = HotbarSlots * SlotSize + (HotbarSlots - 1) * SlotGap;
float panelW = Mathf.Max(gridW, hotbarRowW) + PanelPad * 2;
// Extra 56 px reserved at the bottom for the Exit button + spacing.
float panelH = gridH + SlotSize + PanelPad * 2 + 130f + 56f;
```

(The `hotbarW` local at line 377 then becomes redundant — replace its usage with `hotbarRowW` referenced upward, or leave the local re-declaration and just sync naming. Simplest: rename line 377 to read from the outer var.)

Find line 377-383:

```csharp
float hotbarW = HotbarSlots * SlotSize + (HotbarSlots - 1) * SlotGap;
var hotbarRow = NewRT("HotbarRow", panel);
hotbarRow.anchorMin = new Vector2(0.5f, 0f);
hotbarRow.anchorMax = new Vector2(0.5f, 0f);
hotbarRow.pivot = new Vector2(0.5f, 0f);
hotbarRow.sizeDelta = new Vector2(hotbarW, SlotSize);
hotbarRow.anchoredPosition = new Vector2(0f, PanelPad + ExitArea);
```

Replace with:

```csharp
var hotbarRow = NewRT("HotbarRow", panel);
hotbarRow.anchorMin = new Vector2(0.5f, 0f);
hotbarRow.anchorMax = new Vector2(0.5f, 0f);
hotbarRow.pivot = new Vector2(0.5f, 0f);
hotbarRow.sizeDelta = new Vector2(hotbarRowW, SlotSize);
hotbarRow.anchoredPosition = new Vector2(0f, PanelPad + ExitArea);
```

And update the loop at lines 384-392 to iterate the new `HotbarSlots` value (no edit needed since it already uses the constant) but the width inside the loop needs to use `hotbarRowW`:

```csharp
for (int c = 0; c < HotbarSlots; c++)
{
    float x = -hotbarRowW * 0.5f + c * (SlotSize + SlotGap) + SlotSize * 0.5f;
    _hotbarViews[c] = BuildSlotView(hotbarRow, "H" + c, new Vector2(x, 0f));
}
```

- [ ] **Step 3: Compile check + visual check**

Open Unity. Play `1.6.7.7.7.unity`. Walk to a loot box, press F. The storage panel should open. The hotbar row at the bottom of the panel should show **7** tiles, not 5, and the panel should be wide enough to contain them without overflow.

- [ ] **Step 4: Commit**

```bash
git add Assets/3\ -\ Scripts/UI/StorageUI.cs
git commit -m "feat(storage): storage panel hotbar row grows to 7 tiles

HotbarSlots bumps 5 -> 7. Panel width now uses Max(gridW, hotbarRowW)
so the wider hotbar (624px at 7 tiles × 84px + 6 gaps × 6px) doesn't
overflow the storage grid's narrower 420px footprint. Storage grid
remains 5×4 — only the hotbar mirror at the bottom widens."
```

---

## Task 11 — `StorageUI.PaintSlot` + `ResolveIcon` handle Fish

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs:245-281`

- [ ] **Step 1: Edit IsStackable + PaintSlot + ResolveIcon**

Today `IsStackable` returns true only for resources (count badge shown). Fish is not stackable — count is always 1, weight shown instead.

Replace lines 245-281 with:

```csharp
void PaintSlot(SlotView v, Hotbar.Slot s)
{
    if (v == null) return;
    bool empty = s.id == Hotbar.ItemId.None || s.count <= 0;
    v.background.color = empty
        ? new Color32(0x0C, 0x1A, 0x32, 0x66)
        : (Color)CyanScannerPalette.InnerBg;
    v.border.color = empty
        ? new Color32(0x1C, 0x3A, 0x5C, 0x80)
        : (Color)CyanScannerPalette.PanelBorder;
    v.itemIcon.enabled = !empty && ResolveIcon(s.id) != null;
    v.itemIcon.sprite  = empty ? null : ResolveIcon(s.id);
    // Fish: tint the placeholder icon by the per-fish color so the player
    // can distinguish fish slots visually even with a generic icon.
    v.itemIcon.color = (!empty && s.id == Hotbar.ItemId.Fish && s.fishData != null)
        ? s.fishData.fishColor
        : Color.white;
    // Count badge: stackable items show count, fish shows weight, others empty.
    if (!empty && s.id == Hotbar.ItemId.Fish && s.fishData != null)
    {
        v.countText.enabled = true;
        v.countText.text = s.fishData.weightLbs + " lb";
    }
    else
    {
        v.countText.enabled = !empty && IsStackable(s.id);
        if (v.countText.enabled) v.countText.text = s.count.ToString();
    }
}

static bool IsStackable(Hotbar.ItemId id) =>
    id == Hotbar.ItemId.Wood || id == Hotbar.ItemId.Crystal || id == Hotbar.ItemId.SpaceDust;

static Sprite ResolveIcon(Hotbar.ItemId id)
{
    switch (id)
    {
        case Hotbar.ItemId.Wood:      return Resources.Load<Sprite>("HotbarIcons/TransparentWoodLog");
        case Hotbar.ItemId.Crystal:   return Resources.Load<Sprite>("HotbarIcons/TransparentCrystalShards");
        case Hotbar.ItemId.SpaceDust: return Resources.Load<Sprite>("HotbarIcons/TransparentSpaceDust");
        // Fish: placeholder uses the crystal icon as a stand-in (a generic
        // angular shape). The fishColor tint applied in PaintSlot is what
        // actually distinguishes one fish from another. Phase 3 will
        // replace this with a real fish sprite.
        case Hotbar.ItemId.Fish:      return Resources.Load<Sprite>("HotbarIcons/TransparentCrystalShards");
    }
    switch (id)
    {
        case Hotbar.ItemId.WaterBottle: { var c = Object.FindObjectOfType<WaterBottleController>(true); return c != null ? c.hotbarIcon : null; }
        case Hotbar.ItemId.FishingRod:  { var c = Object.FindObjectOfType<FishingRodController>(true);  return c != null ? c.hotbarIcon : null; }
        case Hotbar.ItemId.Guitar:      { var c = Object.FindObjectOfType<GuitarController>(true);      return c != null ? c.hotbarIcon : null; }
        case Hotbar.ItemId.Axe:         { var c = Object.FindObjectOfType<AxeController>(true);         return c != null ? c.hotbarIcon : null; }
        case Hotbar.ItemId.Pistol:      { var c = Object.FindObjectOfType<PistolController>(true);      return c != null ? c.hotbarIcon : null; }
    }
    return null;
}
```

The tint reset (`v.itemIcon.color = ... Color.white`) is important — without it, after the player drags a fish out of a slot, that slot's `itemIcon` would still show the previous fish's color tint on whatever item is dropped in next.

`Hotbar.cs` already has `WoodSwatchColor`/`CrystalSwatchColor`/`DustSwatchColor` for hotbar rendering — `StorageUI` doesn't currently use them. Phase 3 polish can unify these if desired; out of scope for Phase 1.

- [ ] **Step 2: Compile check**

Console clean.

- [ ] **Step 3: Visual smoke test**

Open the storage panel in Play. Existing wood/crystal/dust slots should look identical to before (white tint, count badge). No fish in inventory yet (Phase 2), so no fish slot to inspect — Task 12 covers this once we manually inject a fish for testing.

- [ ] **Step 4: Commit**

```bash
git add Assets/3\ -\ Scripts/UI/StorageUI.cs
git commit -m "feat(storage): render Fish slots with fishColor tint + weight badge

PaintSlot fork on ItemId.Fish: tints the placeholder icon by
fishData.fishColor and replaces the count badge with the weight in
pounds. ResolveIcon temporarily returns the crystal sprite as the
fish placeholder — Phase 3 swaps in a real fish icon. The white-tint
reset on non-fish slots prevents bleed-through after a fish leaves
a slot."
```

---

## Task 12 — Full manual regression pass

**Files:**
- None (verification only)

Now exercise the six acceptance criteria from the spec (§7). For each one, observe Unity behavior and note pass/fail. If any fail, STOP and diagnose — do not continue to Task 13.

- [ ] **Step 1: Hotbar visual**

Open `Assets/1.6.7.7.7.unity`, press Play. Confirm: 7 tiles render at the bottom-center of the screen, 64px each with 14px gaps. Active-slot lift animation (the "lifted above the row" effect when a slot is equipped) works on at least one new slot (press 6, watch slot 6 lift).

- [ ] **Step 2: Input range**

Still in Play. Trigger Tev's dialogue arc to unlock the axe and pistol (or start from a save where they're unlocked). Press 1–7 in sequence. Each press selects the corresponding slot. 1-5 behave like the legacy hotbar; 6 and 7 highlight but have no item to equip (empty slots).

- [ ] **Step 3: Save round-trip**

Equip axe (key 4 typically). Pick up some wood. Catch a fish if any fish are nearby (or skip — the fishInventory path is unchanged). Open the pause menu → Save Game → name it `phase1-regression-A` → save.

Quit to main menu. Click Load Game → select `phase1-regression-A` → load. Confirm:
- Axe is equipped
- Wood count restored
- Fish-in-dex restored
- Loot box contents restored (open a box on the ship)

- [ ] **Step 4: Fish catch flow unchanged**

In play mode with the rod unlocked, walk to the fishing bank, cast, catch a fish. Open the fishingdex (key `J`). The new fish appears as a card in the dex. The hotbar slots stay unchanged (no fish in hotbar — that's Phase 2).

- [ ] **Step 5: Sell flow unchanged**

Walk to `FishMarketNPC` at Humble Abode. Open the sell panel. Stage one fish per tier using the existing tier counters. Confirm money increases by the expected amount. Sold fish disappear from the dex.

- [ ] **Step 6: Cook flow unchanged**

Place a bonfire (build menu, key `N`). Walk to it, open the cook panel. Add a fish to the Common row. Cook 10 seconds. Eat. Hunger increases by 20. Confirm a raw fish eaten (skip cook) triggers `RawFishTripController.StartTrip` (visible by the screen kaleidoscope effect ramping in).

- [ ] **Step 7: Hotbar storage drag still works**

Open a loot box. Drag wood between hotbar and storage via shift+click, click-pickup-drop, right-click split. All should work as before. The 7-tile hotbar mirror in the storage panel should be drag-compatible end-to-end.

- [ ] **Step 8: Commit a regression-passed marker** (no file changes — empty commit)

Don't commit anything yet — Task 13 does the wrap-up commit. If all 7 scenarios passed, proceed.

If any failed, STOP. Don't commit. Diagnose, fix, return to Task 12.

---

## Task 13 — Phase 1 wrap-up

**Files:**
- None (or update CLAUDE.md if save-system or hotbar conventions changed enough to warrant a note)

- [ ] **Step 1: Update CLAUDE.md if needed**

Read CLAUDE.md's "Save system" section and "Hotbar & equippables" section. Check whether the slot-count (5) or the slot struct shape need updating. If the existing text contradicts Phase 1 reality, edit those sections to match.

Likely edits:
- "5-slot inventory keyed 1-5" → "7-slot inventory keyed 1-7"
- The slot struct description may need a line about `FishEntry fishData`

- [ ] **Step 2: Tag the phase boundary**

```bash
git tag fish-revamp-phase-1-complete
```

- [ ] **Step 3: Final summary commit (if CLAUDE.md was edited)**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for hotbar 7 slots + Slot.fishData

Phase 1 of the fish/storage revamp lands these changes. Slot count
moved 5 -> 7, Slot struct gained a FishEntry payload for fish.
Save schema unchanged in structure — HotbarSlotSave gained a nullable
fishData field. Existing flows (cook, sell, dex, drag-drop) unchanged."
```

- [ ] **Step 4: Confirm clean working tree**

```bash
git status
```

Expected output: `nothing to commit, working tree clean`.

- [ ] **Step 5: Hand off to Phase 2 brainstorming**

Phase 1 is complete. Tell the user:

> Phase 1 done. 13 atomic commits on `feature/fish-storage-revamp`, all manual regression checks passing, tagged `fish-revamp-phase-1-complete`. Ready to brainstorm Phase 2 (fish-flow + dex revamp + hold-to-eat) whenever you are.

---

## Self-Review

Plan checked against spec on 2026-05-26 during writing.

**Spec coverage:**
- ✓ §2 Goal: hotbar 5→7 → Task 3
- ✓ §2 Goal: Slot.fishData → Task 2
- ✓ §2 Goal: same slot model for hotbar/storage/(future) fish bag → Tasks 2 + 7 + 8 (storage shares the type)
- ✓ §2 Goal: save schema supports fish slots in containers → Tasks 4-8
- ✓ §2 Goal: old saves load → Task 4 step 3
- ✓ §2 Goal: existing flows pass regression → Task 12 (6 scenarios)
- ✓ §3 Decision: one fish per slot, no stacking → enforced by `StackMax(Fish)=1` (Task 2 step 1)
- ✓ §3 Decision: hold-LMB to eat → deferred to Phase 2 (out of scope per spec §2)
- ✓ §3 Decision: additive Slot.fishData → Task 2
- ✓ §3 Decision: keep 64px tiles → Task 3 (NumSlots constant only; HUD auto-layouts)
- ✓ §4.1 Hotbar.cs changes → Tasks 2, 3
- ✓ §4.3 Save schema → Task 4
- ✓ §4.4 LootBox save gap → Task 1 (correction — wasn't a gap)
- ✓ §4.5 StorageUI Fish branch → Tasks 10, 11
- ✓ §7 Acceptance criteria 1-6 → Task 12 steps 1-6 (+ Task 12 step 7 adds the storage drag check)
- ✓ §8 Files touched → matches Task file lists (after §8 correction in Task 1)

**Placeholder scan:** No TODOs, no TBDs, no "implement later", no "similar to Task N" without code. Each task has full code blocks.

**Type consistency:**
- `Hotbar.Slot.fishData` (Task 2) ↔ `cursor.fishData` in `SlotOps.CursorState` (Task 9) ↔ `HotbarSlotSave.fishData` (Task 4) ↔ `FishEntrySave` shape with `fishType`/`weightLbs`/`fishColor` mirrors `FishEntry`. Consistent.
- `Hotbar.ItemId.Fish` referenced in Tasks 2, 5, 6, 7, 8, 9, 11. Always with the `Hotbar.ItemId.` prefix when accessed from outside `Hotbar.cs`. Consistent.
- `FishEntry` constructor pattern `new FishEntry(type, weight)` followed by `.fishColor = color` assignment used in Tasks 6 and 8 — matches `SaveCollector.ApplyFishInventory` (the existing code already does this).

**Scope check:** 13 tasks, ~5-15 minutes each, single concern per task, single commit per task. Within range.

No fixes needed.
