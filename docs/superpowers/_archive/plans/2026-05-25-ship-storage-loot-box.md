# Ship storage (loot box) — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-instance 20-slot storage container to the `LootBox` prefab on `SHIP44`, with the Scanner-blueprint UI from the design spec, full drag-and-drop interaction, hotbar↔storage swapping, and save-system persistence.

**Architecture:** Three new gameplay files (`LootBox`, `StorageRegistry`, `StorageUI`) plus one pure helper (`SlotOps`). Reuses the existing `Hotbar.Slot` / `HotbarSlotSave` data shape so storage is just "another container of the same kind of slots". Gating mirrors the existing `isInDialogue` / `isMapOpen` pattern.

**Tech Stack:** Unity 2022.3, C# (Assembly-CSharp, no `.asmdef`), Unity UI (`UnityEngine.UI`, TextMeshPro), `JsonUtility` save system.

**Reference:** Design spec at `docs/superpowers/specs/2026-05-25-ship-storage-loot-box-design.md`.

**Verification note:** This codebase has no automated test framework — all verification happens in the Unity Editor. Tasks include explicit "what to check in Play mode" or "what to inspect via Coplay-MCP" steps instead of `pytest`-style assertions. Pure logic in `SlotOps` (Task 1) gets a runnable Editor menu test as the closest equivalent.

---

## File map

**Create:**
- `Assets/3 - Scripts/UI/SlotOps.cs` — pure static helpers for slot mutation
- `Assets/Editor/TestSlotOps.cs` — `[MenuItem("Tools/Test/Slot Ops")]` runner
- `Assets/3 - Scripts/Ship/StorageRegistry.cs` — static list of live `LootBox` instances
- `Assets/3 - Scripts/Ship/LootBox.cs` — MonoBehaviour on the prefab, data + interact prompt
- `Assets/3 - Scripts/UI/StorageUI.cs` — singleton, scanner-blueprint panel

**Modify:**
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` — add `StorageSave` class + `storages` field
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — add `CaptureStorages` + `ApplyStorages` + wire into `Capture` / `Apply`
- `Assets/3 - Scripts/UI/Hotbar.cs` — patch `TryAddItem` storage check, add `OnStorageOpened` hook
- `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` — add `isInStorage` flag, gate movement input
- `Assets/3 - Scripts/Camera/CameraTransformFX.cs` — gate headbob + strafe-tilt input
- `Assets/3 - Scripts/Camera/CameraFOVFX.cs` — gate sprint-FOV input
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `StorageUI` in `EnsureGameplaySingletons`

---

## Task 1 — `SlotOps` pure helper

**Files:**
- Create: `Assets/3 - Scripts/UI/SlotOps.cs`
- Create: `Assets/Editor/TestSlotOps.cs`

The actual slot-mutation logic lives here as static methods. Pure functions operating on `Hotbar.Slot[]` references — no Unity dependencies beyond `Mathf` and the `Hotbar.ItemId` enum. This isolates the math so the UI layer can be a thin dispatcher.

- [ ] **Step 1.1: Create `SlotOps.cs`**

```csharp
// Assets/3 - Scripts/UI/SlotOps.cs
using UnityEngine;

// Pure slot-mutation helpers shared by StorageUI and (potentially) the
// hotbar/quick-move flow. No Unity scene refs; takes Slot[] arrays + a
// CursorState struct and mutates in place. Same Slot type as Hotbar so
// storage and hotbar can interoperate without a translation layer.
public static class SlotOps
{
    public struct CursorState
    {
        public Hotbar.ItemId id;
        public int count;
        public Hotbar.Slot[] sourceContainer;   // null when not held
        public int sourceIndex;
        public bool IsHeld => id != Hotbar.ItemId.None && count > 0;
    }

    // LMB on a slot: pick up the entire stack (or deposit/swap/merge if held).
    public static void HandleLeftClick(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        if (container == null || idx < 0 || idx >= container.Length) return;
        if (cursor.IsHeld) Deposit(container, idx, ref cursor);
        else               PickUpFull(container, idx, ref cursor);
    }

    // RMB on a slot: pick up one item (or drop one if held with same id).
    public static void HandleRightClick(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        if (container == null || idx < 0 || idx >= container.Length) return;
        if (cursor.IsHeld) DepositOne(container, idx, ref cursor);
        else               PickUpOne(container, idx, ref cursor);
    }

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
        // Spill into empty slots.
        for (int i = 0; i < dest.Length && remaining > 0; i++)
        {
            if (dest[i].id != Hotbar.ItemId.None) continue;
            int take = Mathf.Min(cap, remaining);
            dest[i] = new Hotbar.Slot { id = s.id, count = take };
            remaining -= take;
        }

        if (remaining == 0) source[idx] = default;
        else                source[idx] = new Hotbar.Slot { id = s.id, count = remaining };
    }

    static void PickUpFull(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        var s = container[idx];
        if (s.id == Hotbar.ItemId.None || s.count <= 0) return;
        cursor.id = s.id;
        cursor.count = s.count;
        cursor.sourceContainer = container;
        cursor.sourceIndex = idx;
        container[idx] = default;
    }

    static void PickUpOne(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        var s = container[idx];
        if (s.id == Hotbar.ItemId.None || s.count <= 0) return;
        cursor.id = s.id;
        cursor.count = 1;
        cursor.sourceContainer = container;
        cursor.sourceIndex = idx;
        s.count -= 1;
        if (s.count <= 0) container[idx] = default;
        else              container[idx] = s;
    }

    static void Deposit(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        var s = container[idx];

        // Empty slot — drop the whole cursor here.
        if (s.id == Hotbar.ItemId.None || s.count <= 0)
        {
            container[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count };
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
        container[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count };
        cursor.id = temp.id;
        cursor.count = temp.count;
        // sourceContainer/sourceIndex stay as the original pickup origin —
        // that's where return-on-close should put it.
    }

    static void DepositOne(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        var s = container[idx];

        // Empty slot — drop one.
        if (s.id == Hotbar.ItemId.None || s.count <= 0)
        {
            container[idx] = new Hotbar.Slot { id = cursor.id, count = 1 };
            cursor.count -= 1;
            if (cursor.count <= 0) ClearCursor(ref cursor);
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

    static void ClearCursor(ref CursorState cursor)
    {
        cursor.id = Hotbar.ItemId.None;
        cursor.count = 0;
        cursor.sourceContainer = null;
        cursor.sourceIndex = -1;
    }

    // Return-to-source on close. Best-effort: if source slot is now occupied
    // by something else (defensive — shouldn't happen with single-open-at-a-
    // time UI), spill to first empty slot in source. If no empty, leave on
    // cursor and return false so caller knows to block close.
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
                src[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count };
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
            src[i] = new Hotbar.Slot { id = cursor.id, count = cursor.count };
            ClearCursor(ref cursor);
            return true;
        }
        return false; // caller blocks close
    }
}
```

- [ ] **Step 1.2: Create the editor test runner**

```csharp
// Assets/Editor/TestSlotOps.cs
using UnityEditor;
using UnityEngine;

// Manual test runner — no NUnit dependency. Run via Tools menu, look at
// Console for PASS / FAIL lines. The pure-logic equivalent of `pytest`
// for this Unity-native codebase.
public static class TestSlotOps
{
    [MenuItem("Tools/Test/Slot Ops")]
    public static void RunAll()
    {
        int pass = 0, fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Debug.Log($"[SlotOps] PASS  {name}"); }
            else    { fail++; Debug.LogError($"[SlotOps] FAIL  {name}  {detail}"); }
        }

        // PickUpFull empties source, fills cursor.
        {
            var src = NewSlots(5);
            src[2] = new Hotbar.Slot { id = Hotbar.ItemId.Wood, count = 30 };
            var c = default(SlotOps.CursorState);
            SlotOps.HandleLeftClick(src, 2, ref c);
            Check("PickUpFull cursor.id",    c.id == Hotbar.ItemId.Wood);
            Check("PickUpFull cursor.count", c.count == 30);
            Check("PickUpFull source clear", src[2].id == Hotbar.ItemId.None);
        }

        // PickUpOne decrements stack and puts 1 on cursor.
        {
            var src = NewSlots(5);
            src[1] = new Hotbar.Slot { id = Hotbar.ItemId.Wood, count = 30 };
            var c = default(SlotOps.CursorState);
            SlotOps.HandleRightClick(src, 1, ref c);
            Check("PickUpOne cursor.count == 1", c.count == 1);
            Check("PickUpOne source.count == 29", src[1].count == 29);
        }

        // Deposit on empty slot places full cursor.
        {
            var src = NewSlots(5);
            var c = new SlotOps.CursorState { id = Hotbar.ItemId.Wood, count = 10, sourceContainer = src, sourceIndex = 0 };
            SlotOps.HandleLeftClick(src, 3, ref c);
            Check("Deposit empty places stack", src[3].id == Hotbar.ItemId.Wood && src[3].count == 10);
            Check("Deposit clears cursor",      !c.IsHeld);
        }

        // Deposit same-id merges respecting stack cap.
        {
            var src = NewSlots(5);
            src[2] = new Hotbar.Slot { id = Hotbar.ItemId.Wood, count = 95 };
            var c = new SlotOps.CursorState { id = Hotbar.ItemId.Wood, count = 20, sourceContainer = src, sourceIndex = 0 };
            SlotOps.HandleLeftClick(src, 2, ref c);
            Check("Merge to cap", src[2].count == 100, $"got {src[2].count}");
            Check("Cursor keeps remainder", c.count == 15, $"got {c.count}");
        }

        // Deposit different-id swaps.
        {
            var src = NewSlots(5);
            src[1] = new Hotbar.Slot { id = Hotbar.ItemId.Pistol, count = 1 };
            var c = new SlotOps.CursorState { id = Hotbar.ItemId.Axe, count = 1, sourceContainer = src, sourceIndex = 0 };
            SlotOps.HandleLeftClick(src, 1, ref c);
            Check("Swap slot",   src[1].id == Hotbar.ItemId.Axe);
            Check("Swap cursor", c.id == Hotbar.ItemId.Pistol);
        }

        // RMB drop-one on same-id.
        {
            var src = NewSlots(5);
            src[1] = new Hotbar.Slot { id = Hotbar.ItemId.Wood, count = 50 };
            var c = new SlotOps.CursorState { id = Hotbar.ItemId.Wood, count = 10, sourceContainer = src, sourceIndex = 0 };
            SlotOps.HandleRightClick(src, 1, ref c);
            Check("Drop-one slot count", src[1].count == 51);
            Check("Drop-one cursor count", c.count == 9);
        }

        // RMB on different-id is a no-op.
        {
            var src = NewSlots(5);
            src[1] = new Hotbar.Slot { id = Hotbar.ItemId.Pistol, count = 1 };
            var c = new SlotOps.CursorState { id = Hotbar.ItemId.Wood, count = 10, sourceContainer = src, sourceIndex = 0 };
            SlotOps.HandleRightClick(src, 1, ref c);
            Check("RMB diff-id no-op slot",   src[1].id == Hotbar.ItemId.Pistol && src[1].count == 1);
            Check("RMB diff-id no-op cursor", c.id == Hotbar.ItemId.Wood && c.count == 10);
        }

        // QuickMove fills existing stacks then spills.
        {
            var src = NewSlots(5);
            var dst = NewSlots(20);
            src[0] = new Hotbar.Slot { id = Hotbar.ItemId.Wood, count = 50 };
            dst[5] = new Hotbar.Slot { id = Hotbar.ItemId.Wood, count = 80 };
            // 50 to move; dst[5] takes 20 (caps at 100), remaining 30 spills to first empty (dst[0]).
            SlotOps.HandleQuickMove(src, 0, dst);
            Check("QuickMove source emptied",  src[0].id == Hotbar.ItemId.None);
            Check("QuickMove existing capped", dst[5].count == 100);
            Check("QuickMove spilled",         dst[0].id == Hotbar.ItemId.Wood && dst[0].count == 30);
        }

        // ReturnHeldToSource restores empty source.
        {
            var src = NewSlots(5);
            var c = new SlotOps.CursorState { id = Hotbar.ItemId.Wood, count = 22, sourceContainer = src, sourceIndex = 1 };
            bool ok = SlotOps.ReturnHeldToSource(ref c);
            Check("Return-to-source success", ok);
            Check("Return slot restored",     src[1].id == Hotbar.ItemId.Wood && src[1].count == 22);
            Check("Return cursor cleared",    !c.IsHeld);
        }

        Debug.Log($"[SlotOps] DONE — {pass} passed, {fail} failed");
    }

    static Hotbar.Slot[] NewSlots(int n) => new Hotbar.Slot[n];
}
```

- [ ] **Step 1.3: Verify in Editor**

In Unity Editor:
1. Wait for compile (check `mcp__coplay-mcp__check_compile_errors` or watch the Console).
2. Run **Tools → Test → Slot Ops** from the menu bar.
3. Console should show `[SlotOps] DONE — 18 passed, 0 failed` (or similar pass count, depending on how I counted Checks). All FAIL lines must be zero.

If any FAIL: stop and read the failing line; the assertion message tells you which expectation broke.

- [ ] **Step 1.4: Commit**

```
git add "Assets/3 - Scripts/UI/SlotOps.cs" "Assets/3 - Scripts/UI/SlotOps.cs.meta" \
        "Assets/Editor/TestSlotOps.cs" "Assets/Editor/TestSlotOps.cs.meta"
git commit -m "feat(storage): SlotOps pure helpers + editor test runner"
```

---

## Task 2 — Save schema additions

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs`

Adds the `StorageSave` class and the `storages` list field. No behavior change — sets the wire format so Task 4 can wire capture/apply.

- [ ] **Step 2.1: Add the `StorageSave` class and list field**

Open `SaveData.cs`. Find the `HotbarSlotSave` class (around line 53) — add the new class right after it. Then find the `SaveData` class and add the field next to the other List collections.

```csharp
// After HotbarSlotSave / HotbarSave (~line 65), add:

[System.Serializable]
public class StorageSave
{
    public string boxId;
    public System.Collections.Generic.List<HotbarSlotSave> slots
        = new System.Collections.Generic.List<HotbarSlotSave>();
}
```

In the `SaveData` class body, alongside the other `public List<X> foo = new();` fields, add:

```csharp
public System.Collections.Generic.List<StorageSave> storages
    = new System.Collections.Generic.List<StorageSave>();
```

- [ ] **Step 2.2: Verify it compiles**

Use `mcp__coplay-mcp__check_compile_errors` — must report zero errors. The field is unused so nothing else should change.

- [ ] **Step 2.3: Verify save JSON contains the new field**

In Unity Editor:
1. Open `Assets/1.6.7.7.7.unity`, press Play.
2. Open pause menu (Esc) → Save → make a new save called `storage-schema-test`.
3. Stop play.
4. Open `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\storage-schema-test.json` in a text editor.
5. Confirm the JSON contains `"storages": []`.

- [ ] **Step 2.4: Commit**

```
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs"
git commit -m "feat(storage): SaveData schema for per-box storage"
```

---

## Task 3 — `StorageRegistry` + `LootBox` data shell

**Files:**
- Create: `Assets/3 - Scripts/Ship/StorageRegistry.cs`
- Create: `Assets/3 - Scripts/Ship/LootBox.cs`

`LootBox` is the MonoBehaviour the user will attach to the loot box prefab. At this stage it just holds the 20-slot data array, computes its stable `boxId`, and registers/deregisters with `StorageRegistry`. No interaction yet — that's Task 7.

- [ ] **Step 3.1: Create `StorageRegistry.cs`**

```csharp
// Assets/3 - Scripts/Ship/StorageRegistry.cs
using System.Collections.Generic;

// Static list of all live LootBox instances. Same shape as
// EnemyController.ActiveEnemies / SpawnedTree.AllTrees — populated via the
// component's OnEnable / OnDisable. Used by:
//   - SaveCollector.CaptureStorages   (iterate live boxes)
//   - SaveCollector.ApplyStorages     (match saved by boxId)
//   - Hotbar.TryAddItem               (don't auto-pull items already stored)
public static class StorageRegistry
{
    static readonly List<LootBox> s_all = new List<LootBox>();
    public static IReadOnlyList<LootBox> All => s_all;

    public static void Register(LootBox box)
    {
        if (box == null) return;
        if (!s_all.Contains(box)) s_all.Add(box);
    }

    public static void Unregister(LootBox box)
    {
        if (box == null) return;
        s_all.Remove(box);
    }

    // Used by Hotbar.TryAddItem so a stored equippable doesn't get re-pulled
    // into the hotbar every frame by DetectAcquisitions.
    public static bool IsItemAnywhere(Hotbar.ItemId id)
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            var slots = s_all[i].Slots;
            for (int j = 0; j < slots.Length; j++)
                if (slots[j].id == id) return true;
        }
        return false;
    }
}
```

- [ ] **Step 3.2: Create `LootBox.cs`**

```csharp
// Assets/3 - Scripts/Ship/LootBox.cs
using UnityEngine;

// Per-loot-box component. User attaches it to the LootBox prefab in the
// Inspector. Holds the 20 storage slots and computes a stable boxId for
// the save system. Interaction (F-prompt + open) is added in a later task.
[DisallowMultipleComponent]
public class LootBox : MonoBehaviour
{
    public const int SlotCount = 20;

    // Data: same Slot type the Hotbar uses. Allocated once at Awake.
    [System.NonSerialized] Hotbar.Slot[] _slots = new Hotbar.Slot[SlotCount];
    public Hotbar.Slot[] Slots => _slots;

    // Stable identifier — derived from the hierarchy path. Format:
    //   "BoughtShip<N>/<relative-path>"  if under a BoughtShip
    //   "OriginalShip/<relative-path>"    if under a Ship that's not bought
    //   "<absolute-scene-path>"           otherwise (future world placement)
    // Computed once at Awake; not serialized because the value is purely
    // derived from scene hierarchy and BoughtShip.shipNumber.
    [System.NonSerialized] string _boxId;
    public string BoxId => _boxId;

    void Awake()
    {
        _boxId = ComputeBoxId();
        if (_slots == null || _slots.Length != SlotCount) _slots = new Hotbar.Slot[SlotCount];
    }

    void OnEnable()  { StorageRegistry.Register(this); }
    void OnDisable() { StorageRegistry.Unregister(this); }

    string ComputeBoxId()
    {
        // Walk up to find the nearest ship ancestor.
        Transform shipRoot = null;
        BoughtShip bought = null;
        for (var t = transform; t != null; t = t.parent)
        {
            var ship = t.GetComponent<Ship>();
            if (ship != null) { shipRoot = t; bought = t.GetComponent<BoughtShip>(); break; }
        }

        if (shipRoot != null)
        {
            string relative = RelativePath(transform, shipRoot);
            string prefix   = bought != null ? $"BoughtShip{bought.shipNumber}" : "OriginalShip";
            return $"{prefix}/{relative}";
        }
        // Non-ship loot box (future): use absolute scene path.
        return AbsolutePath(transform);
    }

    static string RelativePath(Transform leaf, Transform root)
    {
        if (leaf == root) return "";
        var stack = new System.Collections.Generic.Stack<string>();
        for (var t = leaf; t != null && t != root; t = t.parent) stack.Push(t.name);
        return string.Join("/", stack.ToArray());
    }

    static string AbsolutePath(Transform leaf)
    {
        var stack = new System.Collections.Generic.Stack<string>();
        for (var t = leaf; t != null; t = t.parent) stack.Push(t.name);
        return string.Join("/", stack.ToArray());
    }
}
```

- [ ] **Step 3.3: Attach the component to the LootBox prefab**

The user already created the LootBox prefab on SHIP44 and added a trigger collider. Now attach the new component.

Via Coplay-MCP (recommended for reproducibility):
```
mcp__coplay-mcp__add_component
  game_object_path = "(the loot box's hierarchy path in 1.6.7.7.7.unity, e.g. '--- Player & Ship ---/SHIP44/LootBox')"
  component_type = "LootBox"
```

Or in the Editor: open `1.6.7.7.7.unity`, find the LootBox GameObject in the hierarchy, drag `LootBox.cs` onto it. Save the scene.

- [ ] **Step 3.4: Verify the registry populates and the ID is stable**

Add a one-shot debug log inside `LootBox.OnEnable` temporarily:
```csharp
void OnEnable() {
    StorageRegistry.Register(this);
    Debug.Log($"[LootBox] enabled — boxId='{_boxId}', registry count={StorageRegistry.All.Count}");
}
```

Press Play. Console should print exactly one `[LootBox] enabled — boxId='OriginalShip/LootBox' (or similar), registry count=1`. Stop Play, **remove the debug log** before committing.

- [ ] **Step 3.5: Commit**

```
git add "Assets/3 - Scripts/Ship/StorageRegistry.cs" \
        "Assets/3 - Scripts/Ship/StorageRegistry.cs.meta" \
        "Assets/3 - Scripts/Ship/LootBox.cs" \
        "Assets/3 - Scripts/Ship/LootBox.cs.meta" \
        Assets/1.6.7.7.7.unity
git commit -m "feat(storage): LootBox component + StorageRegistry"
```

---

## Task 4 — Save capture & apply

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

Capture iterates `StorageRegistry.All`, emits one `StorageSave` each. Apply matches by `boxId` and restores the slot layout. Slots in by `ApplyStorages` after `ApplyHotbar` (step 10 in the existing apply sequence).

- [ ] **Step 4.1: Add `CaptureStorages` to `SaveCollector.cs`**

Find `CaptureHotbar` (around line 270). Add `CaptureStorages` right after it.

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
            entry.slots.Add(new HotbarSlotSave
            {
                itemId = slots[j].id.ToString(),
                count = slots[j].count
            });
        }
        list.Add(entry);
    }
}
```

- [ ] **Step 4.2: Wire `CaptureStorages` into `Capture`**

Find the `public static SaveData Capture(string name)` method. Find the line that calls `CaptureHotbar(data.hotbar)` (search for it — should be near the equipment/wood/crystal captures). Add immediately after it:

```csharp
CaptureStorages(data.storages);
```

- [ ] **Step 4.3: Add `ApplyStorages` to `SaveCollector.cs`**

Find `ApplyHotbar` (around line 887). Add `ApplyStorages` right after it.

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
            Debug.LogWarning($"[Storage] no live LootBox for saved boxId '{saved.boxId}' — dropping");
            continue;
        }

        var slots = match.Slots;
        for (int k = 0; k < slots.Length; k++) slots[k] = default;
        int max = Mathf.Min(saved.slots.Count, slots.Length);
        for (int k = 0; k < max; k++)
        {
            var e = saved.slots[k];
            if (e == null) continue;
            if (!System.Enum.TryParse<Hotbar.ItemId>(e.itemId, out var id)) continue;
            int count = Mathf.Clamp(e.count, 0, Hotbar.StackMax(id));
            if (id == Hotbar.ItemId.None || count <= 0) { slots[k] = default; continue; }
            slots[k] = new Hotbar.Slot { id = id, count = count };
        }
    }
}
```

- [ ] **Step 4.4: Wire `ApplyStorages` into `Apply`**

Find `ApplyHotbar(data);` inside `public static void Apply(SaveData data)` (around line 733). Add immediately after it:

```csharp
ApplyStorages(data.storages);
```

- [ ] **Step 4.5: Verify round-trip via Coplay-MCP**

1. `mcp__coplay-mcp__check_compile_errors` — must be clean.
2. Open `1.6.7.7.7.unity`, press Play.
3. (You can't easily put items in storage yet — that's Tasks 7-10. For now just verify the empty round-trip.)
4. Save as `storage-round-trip`. Stop Play. Check the JSON file: `"storages": [{ "boxId": "OriginalShip/LootBox", "slots": [...20 empty entries...] }]`.
5. Re-launch, press Play, Load the save. Console: no `[Storage] no live LootBox` warning. The box's slots remain empty.

- [ ] **Step 4.6: Commit**

```
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "feat(storage): SaveCollector capture + apply per-box storage"
```

---

## Task 5 — Hotbar storage-aware patch

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

Patch `TryAddItem` so it skips when the item is already in some storage. Add a public `OnStorageOpened()` hook that StorageUI calls on the open transition to force-unequip everything (mirrors the existing dialogue/phone unequip-on-open behavior).

- [ ] **Step 5.1: Patch `Hotbar.TryAddItem` to check storage**

Find `TryAddItem` (around line 402 — between `DetectAcquisitions` and `_cycleCursor`). Current body:

```csharp
void TryAddItem(ItemId id)
{
    for (int i = 0; i < NumSlots; i++) if (slots[i].id == id) return;
    for (int i = 0; i < NumSlots; i++)
        if (slots[i].id == ItemId.None) { slots[i] = new Slot { id = id, count = 1 }; return; }
}
```

Replace with:

```csharp
void TryAddItem(ItemId id)
{
    // Already in the hotbar — done.
    for (int i = 0; i < NumSlots; i++) if (slots[i].id == id) return;
    // Already in some storage — leave it there (player explicitly put it
    // away). Without this check, DetectAcquisitions would auto-re-add it
    // every frame, defeating the storage system.
    if (StorageRegistry.IsItemAnywhere(id)) return;
    // Spill into first empty hotbar slot.
    for (int i = 0; i < NumSlots; i++)
        if (slots[i].id == ItemId.None) { slots[i] = new Slot { id = id, count = 1 }; return; }
}
```

- [ ] **Step 5.2: Add the `OnStorageOpened` hook**

Find the `UnequipAll` method (around line 526). Add this new method right above it (so it sits next to the unequip logic):

```csharp
// Called by StorageUI.Open(). Force-unequip everything so the player
// isn't mid-swing when the panel takes over. Same pattern as the
// dialogue / phone open transitions.
public void OnStorageOpened()
{
    UnequipAll();
    _equippedSlot = -1;
}
```

- [ ] **Step 5.3: Verify the storage check doesn't break normal hotbar flow**

1. `mcp__coplay-mcp__check_compile_errors` — clean.
2. Press Play in `1.6.7.7.7.unity`.
3. Confirm the hotbar still auto-acquires the water bottle (slot 1 should show it on game start, since `WaterBottleController.IsUnlocked` is always true and the empty storage doesn't contain it).
4. Use cheat code to unlock the axe / pistol — confirm they appear in the hotbar as before. (Cheat keys via `CheatCodes.cs`; or just open the pause menu and play normally to acquire them.)

- [ ] **Step 5.4: Commit**

```
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(storage): Hotbar.TryAddItem respects StorageRegistry"
```

---

## Task 6 — `PlayerController.isInStorage` + camera FX gates

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs`
- Modify: `Assets/3 - Scripts/Camera/CameraTransformFX.cs`
- Modify: `Assets/3 - Scripts/Camera/CameraFOVFX.cs`

Adds the static gate flag and threads it through movement input + camera FX input. Mirrors the exact pattern used by `AIChatScreen.IsTypingActive` already in the FX modules.

- [ ] **Step 6.1: Add the flag to `PlayerController`**

Open `PlayerController.cs`. Find line 220-221:

```csharp
public static bool isInDialogue;
public static bool isMapOpen;
```

Add immediately after:

```csharp
public static bool isInStorage;
```

- [ ] **Step 6.2: Gate movement input in `PlayerController`**

Find where movement input is read in `PlayerController` (search for `GetAxisRaw("Horizontal")` or the movement gating logic — should be in `Update` or a `HandleMovement` method, and should already check `isInDialogue`). Add `isInStorage` to the same gating condition. For example, if the existing check is:

```csharp
if (isInDialogue || isMapOpen) return;
```

change it to:

```csharp
if (isInDialogue || isMapOpen || isInStorage) return;
```

If the gate is implemented as a positive guard (e.g. `bool canMove = !isInDialogue && !isMapOpen;`), add `&& !isInStorage` accordingly. **Search for every read of `isInDialogue` in `PlayerController.cs` and add `isInStorage` to each.**

- [ ] **Step 6.3: Gate camera-FX input — `CameraTransformFX.cs`**

Open `CameraTransformFX.cs`. Three input reads need the gate (lines ~113, 115-116, 135).

Around line 113:
```csharp
if (input.fxHeadbob && _player != null && _player.IsOnGround && !AIChatScreen.IsTypingActive)
```
Change to:
```csharp
if (input.fxHeadbob && _player != null && _player.IsOnGround
    && !AIChatScreen.IsTypingActive && !PlayerController.isInStorage)
```

Lines 115-116 (and the equivalent on line 135) — the input-read pattern `AIChatScreen.IsTypingActive ? 0f : Input.GetAxisRaw(...)` becomes:

```csharp
bool inputLocked = AIChatScreen.IsTypingActive || PlayerController.isInStorage;
float h = inputLocked ? 0f : UnityEngine.Input.GetAxisRaw("Horizontal");
float v = inputLocked ? 0f : UnityEngine.Input.GetAxisRaw("Vertical");
```

Do the same to the strafe-tilt block (line 135 area):
```csharp
float h = (AIChatScreen.IsTypingActive || PlayerController.isInStorage)
          ? 0f : UnityEngine.Input.GetAxisRaw("Horizontal");
```

- [ ] **Step 6.4: Gate camera-FX input — `CameraFOVFX.cs`**

Open `CameraFOVFX.cs`. Lines 40-41 and 71 use `AIChatScreen.IsTypingActive`. Mirror the pattern from Step 6.3 — replace `AIChatScreen.IsTypingActive` with `(AIChatScreen.IsTypingActive || PlayerController.isInStorage)` in those three reads.

- [ ] **Step 6.5: Verify with a manual flag flip**

1. `mcp__coplay-mcp__check_compile_errors` — clean.
2. Press Play.
3. In the Editor's Inspector, find any GameObject and add a temporary script that toggles `PlayerController.isInStorage` on a key press (or open the **Tools → Test → Slot Ops** menu — anything that runs C#), e.g. temporarily add:
   ```csharp
   if (Input.GetKeyDown(KeyCode.F12)) PlayerController.isInStorage = !PlayerController.isInStorage;
   ```
   to a debug script.
4. Press F12 in Play mode: WASD should stop moving the player. Cam FX should not headbob even while holding W. F12 again: movement resumes.
5. **Remove the debug toggle script** before committing.

- [ ] **Step 6.6: Commit**

```
git add "Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs" \
        "Assets/3 - Scripts/Camera/CameraTransformFX.cs" \
        "Assets/3 - Scripts/Camera/CameraFOVFX.cs"
git commit -m "feat(storage): PlayerController.isInStorage gates movement + cam FX"
```

---

## Task 7 — `LootBox` interact (F-prompt + open hook)

**Files:**
- Modify: `Assets/3 - Scripts/Ship/LootBox.cs`

Add `OnTriggerStay` / `OnTriggerExit` to show the "Press F to open storage" prompt and call `StorageUI.Instance.Open(this)` on F. At this stage `StorageUI.Open` is a stub that just logs — UI comes in Task 8.

- [ ] **Step 7.1: Verify the existing `InteractPromptUI` API**

Look at `Assets/3 - Scripts/UI/InteractPromptUI.cs` and an existing caller (e.g. `BonfireNPCDialogue.cs` or `TevDialogue.cs`) for the exact method names. The typical call shape is `InteractPromptUI.Instance.Show("Press F to open storage")` and `InteractPromptUI.Instance.Hide()` — but read the current API and use the actual method names.

- [ ] **Step 7.2: Create a stub `StorageUI.cs`**

```csharp
// Assets/3 - Scripts/UI/StorageUI.cs
using UnityEngine;

// Singleton scanner-blueprint storage panel. STUB at this task — Open()
// just logs and flips the gate flag. Real UI built in Task 8.
public class StorageUI : MonoBehaviour
{
    static StorageUI instance;
    public static StorageUI Instance => instance;

    public bool IsOpen { get; private set; }
    LootBox _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("StorageUI");
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<StorageUI>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    void OnDestroy() { if (instance == this) instance = null; }

    public void Open(LootBox box)
    {
        if (IsOpen || box == null) return;
        _active = box;
        IsOpen = true;
        PlayerController.isInStorage = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Hotbar.Instance != null) Hotbar.Instance.OnStorageOpened();
        Debug.Log($"[StorageUI] STUB Open boxId={box.BoxId}");
    }

    public void Close()
    {
        if (!IsOpen) return;
        _active = null;
        IsOpen = false;
        PlayerController.isInStorage = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("[StorageUI] STUB Close");
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Escape)) Close();
    }
}
```

- [ ] **Step 7.3: Add interact + prompt to `LootBox.cs`**

Add these fields and methods to `LootBox.cs` (inside the existing class body):

```csharp
const string PromptText = "Press F to open storage";

bool _playerInside;

void OnTriggerEnter(Collider other)
{
    if (other == null || !other.CompareTag("Player")) return;
    _playerInside = true;
}

void OnTriggerExit(Collider other)
{
    if (other == null || !other.CompareTag("Player")) return;
    _playerInside = false;
    // If this box is the one currently open, close it when the player walks out.
    if (StorageUI.Instance != null && StorageUI.Instance.IsOpen)
        StorageUI.Instance.Close();
}

void Update()
{
    if (!_playerInside) return;
    if (!CanInteract()) { InteractPromptUI.Instance?.Hide(); return; }

    InteractPromptUI.Instance?.Show(PromptText);

    if (Input.GetKeyDown(KeyCode.F))
    {
        InteractPromptUI.Instance?.Hide();
        StorageUI.Instance?.Open(this);
    }
}

bool CanInteract()
{
    if (StorageUI.Instance != null && StorageUI.Instance.IsOpen) return false;
    if (PlayerController.isInDialogue) return false;
    if (PlayerController.isMapOpen)    return false;
    if (PlayerPhoneUI.IsOpen)          return false;
    if (Ship.FindPilotedShip() != null) return false;
    return true;
}
```

**Important:** if `InteractPromptUI`'s actual API differs from `Show(string)` / `Hide()` (check Task 7.1), update the call shape to match.

- [ ] **Step 7.4: Verify in Play mode**

1. `mcp__coplay-mcp__check_compile_errors` — clean.
2. Play `1.6.7.7.7.unity`. Walk to the loot box on SHIP44.
3. Walking into the trigger: prompt "Press F to open storage" appears.
4. Press F: prompt hides, Console shows `[StorageUI] STUB Open boxId=OriginalShip/LootBox`. Cursor unlocks (you can see the mouse cursor). WASD doesn't move the player (the gate is active). Headbob is silent if you hold W.
5. Press F again (or Esc): Console shows `[StorageUI] STUB Close`. Cursor relocks. Player can move again.
6. Walk into the trigger and back out without pressing F: prompt appears then hides.
7. Walk into trigger, press F, then while open walk out (you can't, because movement is gated — verify this is the case). The trigger exit happens when the player physically leaves the volume; since they can't move, the close-on-exit path needs the player to be moved by something else. **Known limitation: trigger-exit close only fires when the player exits the volume by means other than walking (e.g. teleport, ship motion, ragdoll).** Document and move on.

- [ ] **Step 7.5: Commit**

```
git add "Assets/3 - Scripts/UI/StorageUI.cs" \
        "Assets/3 - Scripts/UI/StorageUI.cs.meta" \
        "Assets/3 - Scripts/Ship/LootBox.cs"
git commit -m "feat(storage): LootBox F-prompt + StorageUI stub"
```

---

## Task 8 — `StorageUI` canvas + slot rendering

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs`

Build the procedural ScreenSpaceOverlay canvas matching the Scanner-blueprint mockup (option E from the brainstorm). At this task the panel renders with the box's current contents and the hotbar row, but doesn't react to clicks yet.

Reference the existing scanner-blueprint UI for sprite/font conventions: `Assets/3 - Scripts/UI/CyanScannerPalette.cs`, `Assets/3 - Scripts/UI/ScannerFrame.cs`, `Assets/3 - Scripts/UI/HudFontResolver.cs`, and a working caller like `Assets/3 - Scripts/Fishing/FishingdexManager.cs` or `Assets/3 - Scripts/Vendor/GoodsVendorShopUI.cs`.

- [ ] **Step 8.1: Expand `StorageUI` — build the canvas in `Awake`**

Replace the entire `StorageUI.cs` body with the expanded version. Use this skeleton (fill the build helpers to match the spec):

```csharp
// Assets/3 - Scripts/UI/StorageUI.cs
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StorageUI : MonoBehaviour
{
    static StorageUI instance;
    public static StorageUI Instance => instance;

    public bool IsOpen { get; private set; }
    LootBox _active;

    // Layout constants — match the design spec (option E).
    const int  StorageCols = 5;
    const int  StorageRows = 4;
    const int  HotbarSlots = 5;
    const float SlotSize   = 84f;
    const float SlotGap    = 6f;
    const float PanelPad   = 32f;

    Canvas _canvas;
    GameObject _root;
    SlotView[] _storageViews = new SlotView[StorageCols * StorageRows];
    SlotView[] _hotbarViews  = new SlotView[HotbarSlots];

    class SlotView
    {
        public RectTransform root;
        public Image background;
        public Image border;
        public Image itemIcon;
        public TextMeshProUGUI countText;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("StorageUI");
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<StorageUI>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        BuildCanvas();
        HideCanvas();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    public void Open(LootBox box)
    {
        if (IsOpen || box == null) return;
        _active = box;
        IsOpen = true;
        PlayerController.isInStorage = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Hotbar.Instance != null) Hotbar.Instance.OnStorageOpened();
        ShowCanvas();
        RefreshAll();
    }

    public void Close()
    {
        if (!IsOpen) return;
        _active = null;
        IsOpen = false;
        PlayerController.isInStorage = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        HideCanvas();
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        // Force-close if another UI takes over (pause menu, dialogue, phone, map).
        if (PlayerController.isInDialogue || PlayerController.isMapOpen
            || PlayerPhoneUI.IsOpen || Ship.FindPilotedShip() != null) { Close(); return; }
        RefreshAll();
    }

    void RefreshAll()
    {
        if (_active == null) return;
        var bs = _active.Slots;
        for (int i = 0; i < _storageViews.Length; i++) PaintSlot(_storageViews[i], i < bs.Length ? bs[i] : default);
        if (Hotbar.Instance != null)
        {
            var hs = Hotbar.Instance.GetSlotsForSave();
            for (int i = 0; i < _hotbarViews.Length; i++) PaintSlot(_hotbarViews[i], i < hs.Count ? hs[i] : default);
        }
    }

    void PaintSlot(SlotView v, Hotbar.Slot s)
    {
        if (v == null) return;
        bool empty = s.id == Hotbar.ItemId.None || s.count <= 0;
        v.background.color = empty
            ? new Color32(0x0C, 0x1A, 0x32, 0x66)
            : CyanScannerPalette.InnerBg;
        v.border.color = empty
            ? new Color32(0x1C, 0x3A, 0x5C, 0x80)
            : (Color)CyanScannerPalette.PanelBorder;
        v.itemIcon.enabled = !empty;
        v.itemIcon.sprite  = empty ? null : ResolveIcon(s.id);
        v.countText.enabled = !empty && IsStackable(s.id);
        if (v.countText.enabled) v.countText.text = s.count.ToString();
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
        }
        // Equippables: pull from the live controller's hotbarIcon.
        if (Hotbar.Instance == null) return null;
        // Hotbar exposes per-id icons through its registry — use the same pattern
        // it does internally. For equippables that aren't on the player yet,
        // fall back to null (will render as a coloured swatch via empty icon).
        var controllers = new (Hotbar.ItemId id, MonoBehaviour ctrl, Sprite ico)[]
        {
            (Hotbar.ItemId.WaterBottle, Object.FindObjectOfType<WaterBottleController>(true)  is var w && w != null ? w : null, w != null ? w.hotbarIcon : null),
            (Hotbar.ItemId.FishingRod,  Object.FindObjectOfType<FishingRodController>(true)   is var r && r != null ? r : null, r != null ? r.hotbarIcon : null),
            (Hotbar.ItemId.Guitar,      Object.FindObjectOfType<GuitarController>(true)       is var g && g != null ? g : null, g != null ? g.hotbarIcon : null),
            (Hotbar.ItemId.Axe,         Object.FindObjectOfType<AxeController>(true)          is var a && a != null ? a : null, a != null ? a.hotbarIcon : null),
            (Hotbar.ItemId.Pistol,      Object.FindObjectOfType<PistolController>(true)       is var p && p != null ? p : null, p != null ? p.hotbarIcon : null),
        };
        for (int i = 0; i < controllers.Length; i++)
            if (controllers[i].id == id) return controllers[i].ico;
        return null;
    }

    void ShowCanvas() { if (_root != null) _root.SetActive(true); }
    void HideCanvas() { if (_root != null) _root.SetActive(false); }

    void BuildCanvas()
    {
        // Root canvas — ScreenSpaceOverlay at sortingOrder 900.
        var canvasGo = new GameObject("StorageCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 900;
        HUDSceneGate.Register(_canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Full-screen click-blocker (transparent) so clicks behind the panel
        // don't fall through to the world / other UI.
        var blocker = NewRT("Blocker", canvasGo.transform);
        Stretch(blocker, 0, 0, 0, 0);
        var blockerImg = blocker.gameObject.AddComponent<Image>();
        blockerImg.color = new Color(0f, 0f, 0f, 0.5f);
        blockerImg.raycastTarget = true;

        // Center panel.
        float gridW = StorageCols * SlotSize + (StorageCols - 1) * SlotGap;
        float gridH = StorageRows * SlotSize + (StorageRows - 1) * SlotGap;
        float panelW = gridW + PanelPad * 2;
        float panelH = gridH + SlotSize + PanelPad * 2 + 90f; // +hotbar row + headers/footer

        var panel = NewRT("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(panelW, panelH);
        var panelBg = panel.gameObject.AddComponent<Image>();
        panelBg.color = CyanScannerPalette.PanelBg;
        // Optional: assign a sliced rounded-rect sprite if one exists in UIPanelSprites.
        panelBg.raycastTarget = true;

        // Border (1 px outline via 4 thin Images, or a rounded-rect sprite tinted to PanelBorder).
        AddOutline(panel, CyanScannerPalette.PanelBorder);

        // Corner brackets via ScannerFrame.
        ScannerFrame.AddBrackets(panel, length: 14f, thickness: 2f);

        // Blueprint grid behind contents.
        ScannerFrame.AddBlueprintGrid(panel, gridSpacing: 24f);

        // Header row.
        var header = NewRT("Header", panel);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(-PanelPad * 2, 28f);
        header.anchoredPosition = new Vector2(0f, -PanelPad * 0.6f);
        var titleText = NewText(header, "Cargo Hold", anchor: TextAlignmentOptions.MidlineLeft, size: 18f);
        titleText.color = CyanScannerPalette.TextBright;
        titleText.characterSpacing = 4f;
        titleText.fontStyle = FontStyles.UpperCase;
        var taglineText = NewText(header, $"Ship 44 · Bay 1", anchor: TextAlignmentOptions.MidlineRight, size: 11f);
        taglineText.color = CyanScannerPalette.TextMuted;
        taglineText.characterSpacing = 2f;
        taglineText.fontStyle = FontStyles.UpperCase;

        // Storage grid.
        var storageGrid = NewRT("StorageGrid", panel);
        storageGrid.anchorMin = new Vector2(0.5f, 1f);
        storageGrid.anchorMax = new Vector2(0.5f, 1f);
        storageGrid.pivot = new Vector2(0.5f, 1f);
        storageGrid.sizeDelta = new Vector2(gridW, gridH);
        storageGrid.anchoredPosition = new Vector2(0f, -(PanelPad + 50f));
        for (int r = 0; r < StorageRows; r++)
        {
            for (int c = 0; c < StorageCols; c++)
            {
                int i = r * StorageCols + c;
                float x = -gridW * 0.5f + c * (SlotSize + SlotGap) + SlotSize * 0.5f;
                float y = -r * (SlotSize + SlotGap) - SlotSize * 0.5f + gridH * 0.5f;
                _storageViews[i] = BuildSlotView(storageGrid, "S" + i, new Vector2(x, y));
            }
        }

        // Hotbar row.
        float hotbarW = HotbarSlots * SlotSize + (HotbarSlots - 1) * SlotGap;
        var hotbarRow = NewRT("HotbarRow", panel);
        hotbarRow.anchorMin = new Vector2(0.5f, 0f);
        hotbarRow.anchorMax = new Vector2(0.5f, 0f);
        hotbarRow.pivot = new Vector2(0.5f, 0f);
        hotbarRow.sizeDelta = new Vector2(hotbarW, SlotSize);
        hotbarRow.anchoredPosition = new Vector2(0f, PanelPad + 24f);
        for (int c = 0; c < HotbarSlots; c++)
        {
            float x = -hotbarW * 0.5f + c * (SlotSize + SlotGap) + SlotSize * 0.5f;
            _hotbarViews[c] = BuildSlotView(hotbarRow, "H" + c, new Vector2(x, 0f));
        }

        _root = canvasGo;
    }

    SlotView BuildSlotView(RectTransform parent, string name, Vector2 pos)
    {
        var v = new SlotView();
        var rt = NewRT(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(SlotSize, SlotSize);
        rt.anchoredPosition = pos;
        v.root = rt;

        var bgRt = NewRT("__Bg", rt);
        Stretch(bgRt, 0, 0, 0, 0);
        v.background = bgRt.gameObject.AddComponent<Image>();
        v.background.color = CyanScannerPalette.InnerBg;

        var borderRt = NewRT("__Border", rt);
        Stretch(borderRt, 0, 0, 0, 0);
        v.border = borderRt.gameObject.AddComponent<Image>();
        v.border.color = CyanScannerPalette.PanelBorder;
        // Suggest assigning HotbarRoundedRing.GetSprite() (already in the
        // codebase from Hotbar.cs) so border renders as a ring not a fill.
        v.border.sprite = HotbarRoundedRing.GetSprite();
        v.border.type = Image.Type.Sliced;
        v.border.raycastTarget = false;

        var iconRt = NewRT("__Icon", rt);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(56f, 56f);
        v.itemIcon = iconRt.gameObject.AddComponent<Image>();
        v.itemIcon.preserveAspect = true;
        v.itemIcon.raycastTarget = false;

        var countRt = NewRT("__Count", rt);
        countRt.anchorMin = countRt.anchorMax = new Vector2(1f, 0f);
        countRt.pivot = new Vector2(1f, 0f);
        countRt.anchoredPosition = new Vector2(-6f, 4f);
        countRt.sizeDelta = new Vector2(40f, 20f);
        v.countText = countRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(v.countText);
        v.countText.fontSize = 15f;
        v.countText.fontStyle = FontStyles.Bold;
        v.countText.alignment = TextAlignmentOptions.BottomRight;
        v.countText.color = Color.white;
        v.countText.raycastTarget = false;
        var drop = countRt.gameObject.AddComponent<Shadow>();
        drop.effectColor = new Color(0f, 0f, 0f, 0.9f);
        drop.effectDistance = new Vector2(0f, -1.5f);

        return v;
    }

    void AddOutline(RectTransform parent, Color32 color)
    {
        // Four 1px Image strips along the panel edges.
        void Strip(string n, Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 off)
        {
            var rt = NewRT(n, parent);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = off;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }
        Strip("EdgeT", new Vector2(0,1), new Vector2(1,1), new Vector2(0,1), new Vector2(0,-0.5f));
        Strip("EdgeB", new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(0, 0.5f));
        Strip("EdgeL", new Vector2(0,0), new Vector2(0,1), new Vector2(1,0), new Vector2( 0.5f,0));
        Strip("EdgeR", new Vector2(1,0), new Vector2(1,1), new Vector2(1,0), new Vector2(-0.5f,0));
    }

    static RectTransform NewRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(right, top);
    }

    static TextMeshProUGUI NewText(RectTransform parent, string text, TextAlignmentOptions anchor, float size)
    {
        var rt = NewRT("Text", parent);
        Stretch(rt, 0, 0, 0, 0);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text; t.fontSize = size; t.alignment = anchor; t.raycastTarget = false;
        return t;
    }
}
```

**Caveat for the implementer:** the `ResolveIcon` helper uses `FindObjectOfType` once per repaint. This is unusual for hot-path Hotbar code but acceptable here since `Refresh` runs only while the panel is open (~60 fps but only during a UI session). If perf is a concern, cache per-`Open` call.

- [ ] **Step 8.2: Verify the panel renders**

1. `mcp__coplay-mcp__check_compile_errors` — clean.
2. Press Play. Walk into the LootBox trigger. Press F.
3. Panel appears: navy bg, cyan corner brackets, faint blueprint grid behind, "Cargo Hold" title left + "Ship 44 · Bay 1" tagline right, 5×4 grid of empty slots, hotbar row below.
4. The hotbar row mirrors whatever's in the player's hotbar (water bottle in slot 1, etc.).
5. Press F or Esc: panel disappears.
6. Click around the panel: nothing happens (interactions are Tasks 9-10).

If the layout is off (slots overlap, panel too small/large): tweak `SlotSize`, `SlotGap`, `PanelPad` constants and re-test.

- [ ] **Step 8.3: Commit**

```
git add "Assets/3 - Scripts/UI/StorageUI.cs"
git commit -m "feat(storage): scanner-blueprint StorageUI canvas + slot rendering"
```

---

## Task 9 — LMB pick-up + deposit + cursor follower

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs`

Wire LMB click on slots to `SlotOps.HandleLeftClick`. Render the cursor-held item as a follower under the canvas. The cursor item's icon + count update from `_cursor` state every frame the panel is open.

- [ ] **Step 9.1: Add the cursor state + follower view**

In `StorageUI` add fields near the top:

```csharp
SlotOps.CursorState _cursor;
RectTransform _cursorRoot;
Image _cursorIcon;
TextMeshProUGUI _cursorCount;
```

In `BuildCanvas`, after building the hotbar row but before assigning `_root = canvasGo;`, add:

```csharp
// Cursor follower — sits above all slots, never blocks raycasts.
var cur = NewRT("Cursor", canvasGo.transform);
cur.anchorMin = cur.anchorMax = new Vector2(0f, 0f);
cur.pivot = new Vector2(0.5f, 0.5f);
cur.sizeDelta = new Vector2(64f, 64f);
_cursorRoot = cur;
var curBg = NewRT("__Bg", cur);
Stretch(curBg, 0, 0, 0, 0);
var curBgImg = curBg.gameObject.AddComponent<Image>();
curBgImg.color = new Color32(0x14, 0x30, 0x55, 0xF2);
curBgImg.raycastTarget = false;
var curBorder = NewRT("__Border", cur);
Stretch(curBorder, 0, 0, 0, 0);
var curBorderImg = curBorder.gameObject.AddComponent<Image>();
curBorderImg.color = CyanScannerPalette.Accent;
curBorderImg.sprite = HotbarRoundedRing.GetSprite();
curBorderImg.type = Image.Type.Sliced;
curBorderImg.raycastTarget = false;
var curIcon = NewRT("__Icon", cur);
curIcon.anchorMin = curIcon.anchorMax = new Vector2(0.5f, 0.5f);
curIcon.pivot = new Vector2(0.5f, 0.5f);
curIcon.sizeDelta = new Vector2(44f, 44f);
_cursorIcon = curIcon.gameObject.AddComponent<Image>();
_cursorIcon.preserveAspect = true;
_cursorIcon.raycastTarget = false;
var curCount = NewRT("__Count", cur);
curCount.anchorMin = curCount.anchorMax = new Vector2(1f, 0f);
curCount.pivot = new Vector2(1f, 0f);
curCount.anchoredPosition = new Vector2(-4f, 2f);
curCount.sizeDelta = new Vector2(40f, 16f);
_cursorCount = curCount.gameObject.AddComponent<TextMeshProUGUI>();
HudFontResolver.Apply(_cursorCount);
_cursorCount.fontSize = 14f;
_cursorCount.fontStyle = FontStyles.Bold;
_cursorCount.alignment = TextAlignmentOptions.BottomRight;
_cursorCount.color = Color.white;
_cursorCount.raycastTarget = false;
cur.gameObject.SetActive(false);
```

- [ ] **Step 9.2: Add per-slot click handler**

Replace the `SlotView` class with a version that carries its container reference + index, plus add an `IPointerClickHandler` MonoBehaviour:

```csharp
class SlotView
{
    public RectTransform root;
    public Image background;
    public Image border;
    public Image itemIcon;
    public TextMeshProUGUI countText;
    public Hotbar.Slot[] container;   // either box.Slots or hotbar slots wrapper
    public int index;
    public StorageSlotClick click;
}

class StorageSlotClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
{
    public StorageUI owner;
    public SlotView view;
    public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
    {
        owner.OnSlotClicked(view, e);
    }
}
```

Update `BuildSlotView` to attach the click handler on a child Image that fills the slot and IS a raycast target:

In `BuildSlotView`, change `v.background.raycastTarget` (which was implicitly true) to:

```csharp
v.background.raycastTarget = true;            // accepts pointer clicks
v.click = bgRt.gameObject.AddComponent<StorageSlotClick>();
v.click.owner = this;
v.click.view  = v;
```

In `RefreshAll`, after populating the visuals, assign the `container` + `index` refs:

```csharp
var bs = _active.Slots;
for (int i = 0; i < _storageViews.Length; i++)
{
    var v = _storageViews[i];
    PaintSlot(v, i < bs.Length ? bs[i] : default);
    v.container = bs;
    v.index = i;
}
// Hotbar uses an indirect wrapper: we need direct write access.
var hbSlots = Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;
for (int i = 0; i < _hotbarViews.Length; i++)
{
    var v = _hotbarViews[i];
    PaintSlot(v, hbSlots != null && i < hbSlots.Length ? hbSlots[i] : default);
    v.container = hbSlots;
    v.index = i;
}
```

The line `Hotbar.Instance.RawSlotsRef()` requires a new accessor — add this to `Hotbar.cs`:

```csharp
// Direct mutable access to the slot array — for the storage UI's drag/drop
// flow. Hotbar.GetSlotsForSave returns IReadOnlyList<Slot> which can't be
// mutated; this exposes the raw array for SlotOps. Keep callers limited.
public Slot[] RawSlotsRef() => slots;
```

- [ ] **Step 9.3: Add `OnSlotClicked` + cursor follower update**

In `StorageUI`:

```csharp
public void OnSlotClicked(SlotView view, UnityEngine.EventSystems.PointerEventData e)
{
    if (view == null || view.container == null) return;
    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
    {
        if (shift)
        {
            // Quick-move to the OTHER container.
            var other = (view.container == _active.Slots && Hotbar.Instance != null)
                        ? Hotbar.Instance.RawSlotsRef()
                        : _active.Slots;
            SlotOps.HandleQuickMove(view.container, view.index, other);
        }
        else
        {
            SlotOps.HandleLeftClick(view.container, view.index, ref _cursor);
        }
    }
    else if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
    {
        SlotOps.HandleRightClick(view.container, view.index, ref _cursor);
    }
    RefreshAll();
    RefreshCursorVisual();
}

void RefreshCursorVisual()
{
    if (_cursorRoot == null) return;
    if (!_cursor.IsHeld) { _cursorRoot.gameObject.SetActive(false); return; }
    _cursorRoot.gameObject.SetActive(true);
    _cursorIcon.enabled = true;
    _cursorIcon.sprite = ResolveIcon(_cursor.id);
    if (IsStackable(_cursor.id) && _cursor.count > 1)
    {
        _cursorCount.enabled = true;
        _cursorCount.text = _cursor.count.ToString();
    }
    else _cursorCount.enabled = false;
}
```

Update `Update` to follow the mouse and refresh cursor visual every frame:

```csharp
void Update()
{
    if (!IsOpen) return;
    if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Escape)) { TryClose(); return; }
    if (PlayerController.isInDialogue || PlayerController.isMapOpen
        || PlayerPhoneUI.IsOpen || Ship.FindPilotedShip() != null) { TryClose(); return; }

    if (_cursorRoot != null && _cursorRoot.gameObject.activeSelf)
        _cursorRoot.anchoredPosition = Input.mousePosition / _canvas.scaleFactor;

    RefreshAll();
    RefreshCursorVisual();
}

void TryClose()
{
    // Mid-drag close handled in Task 11 — for now just close.
    Close();
}
```

- [ ] **Step 9.4: Verify LMB drag**

1. `mcp__coplay-mcp__check_compile_errors` — clean.
2. Play. Open the storage. Use a cheat key (or pause-menu cheat) to fill the hotbar with wood (e.g. `WoodInventory.Instance.Add(50)` from a CheatCodes debug key — check what's wired).
3. LMB-click the hotbar slot with wood → cursor shows the full stack, hotbar slot empties.
4. Move the mouse around → the cursor item follows.
5. LMB-click an empty storage slot → wood drops in, cursor clears.
6. LMB-click the storage slot with wood → wood picks up.
7. LMB-click another storage slot that already has wood → stacks merge up to 100; remainder stays on cursor.
8. LMB-click a slot with the pistol while holding wood on cursor → pistol and wood swap.

- [ ] **Step 9.5: Commit**

```
git add "Assets/3 - Scripts/UI/StorageUI.cs" "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(storage): LMB pick-up + deposit + cursor follower"
```

---

## Task 10 — RMB drag-one + Shift+LMB quick-move

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs` (already wired in Task 9 — verify behavior)

The dispatch logic is already in `OnSlotClicked` from Task 9. This task is verification + any tweaks if RMB or shift quick-move misbehave.

- [ ] **Step 10.1: Verify RMB drag-one**

1. Place a stack of 30 wood in a storage slot.
2. RMB-click it → cursor shows 1 wood, slot shows 29.
3. RMB-click another empty slot → cursor drops 1, cursor empties (was holding 1).
4. RMB-click a slot with 5 wood while holding 1 wood on cursor → slot becomes 6, cursor clears.
5. RMB-click a slot with the pistol while holding wood → no-op (different ids, RMB is "drop one" not "swap").

- [ ] **Step 10.2: Verify Shift+LMB quick-move**

1. Place 50 wood in a storage slot. Hotbar has an empty slot.
2. Shift+LMB the storage wood slot → storage slot empties, hotbar gets a 50-wood stack in the first available slot.
3. Put 80 wood in hotbar slot 4 (cheat or normal acquisition). Storage has an existing 50-wood stack.
4. Shift+LMB the hotbar wood → fills existing storage stack to 100 (20 moved), spills remaining 30 into the next empty storage slot. Hotbar wood slot now empty.
5. Shift+LMB an equippable (e.g. pistol) in the hotbar → moves to first empty storage slot. Hotbar slot empties.
6. Shift+LMB the pistol in storage → moves back to hotbar's first empty slot.

- [ ] **Step 10.3: Commit (if any tweaks were needed)**

```
git add "Assets/3 - Scripts/UI/StorageUI.cs"
git commit -m "fix(storage): RMB / Shift+LMB tweaks after manual verification"
```

If no tweaks were needed, skip the commit. The behavior was already wired in Task 9.

---

## Task 11 — Close handling: return-to-source + trigger-exit close

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs`

If the cursor is held and the player tries to close, return the item to its source slot first. If the source can't accept (defensive — shouldn't happen), block close until resolved.

- [ ] **Step 11.1: Implement `TryClose` properly**

Replace the stub `TryClose` from Task 9 with:

```csharp
void TryClose()
{
    if (_cursor.IsHeld)
    {
        bool returned = SlotOps.ReturnHeldToSource(ref _cursor);
        if (!returned)
        {
            // Defensive: source full of different items. Print a hint and
            // block the close until the player manually drops the cursor.
            Debug.LogWarning("[StorageUI] cannot close — cursor held & no room to return. Drop the item first.");
            return;
        }
    }
    Close();
}
```

- [ ] **Step 11.2: Force-close on trigger exit**

`LootBox.OnTriggerExit` already calls `StorageUI.Instance.Close()` directly — change that to `TryClose` so the cursor returns first:

In `LootBox.cs`, replace the `OnTriggerExit` body's close call:
```csharp
if (StorageUI.Instance != null && StorageUI.Instance.IsOpen)
    StorageUI.Instance.RequestClose();
```

And expose `RequestClose` in `StorageUI`:
```csharp
public void RequestClose() => TryClose();
```

- [ ] **Step 11.3: Verify**

1. Open storage. Pick up a stack of wood with LMB. Press F → wood returns to its slot, panel closes.
2. Open storage. Pick up wood. Press Esc → same behavior.
3. Open storage. Pick up wood. Have the player teleport / be moved out of the trigger somehow (e.g. via a cheat) → panel closes, wood returns.
4. Construct the defensive case manually: pick up wood from slot 0, then somehow fill slot 0 with another item, then close. Console prints the warning, close is blocked. Drop the cursor item somewhere first; close now succeeds.

- [ ] **Step 11.4: Commit**

```
git add "Assets/3 - Scripts/UI/StorageUI.cs" "Assets/3 - Scripts/Ship/LootBox.cs"
git commit -m "feat(storage): mid-drag close returns held item to source"
```

---

## Task 12 — `MainMenuController` seeding + integration smoke test

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs`

Per `CLAUDE.md` "MainMenu singleton trap": any singleton with `MainMenu` early-return must also be seeded in `EnsureGameplaySingletons`. Otherwise it works in the Editor (Play starts in the gameplay scene, singleton self-creates) but is missing in builds (Play starts in MainMenu, singleton bails out, never gets a second chance).

- [ ] **Step 12.1: Seed `StorageUI` in `EnsureGameplaySingletons`**

Find `EnsureGameplaySingletons` in `MainMenuController.cs`. Mirror an existing block (e.g. the `Hotbar.Instance == null` block) and add:

```csharp
if (StorageUI.Instance == null)
{
    var go = new GameObject("StorageUI");
    Object.DontDestroyOnLoad(go);
    go.AddComponent<StorageUI>();
}
```

Place it adjacent to other UI singletons (Hotbar, TutorialUI, CompassHUD).

- [ ] **Step 12.2: Integration smoke test in Editor**

In `1.6.7.7.7.unity`, press Play.

- [ ] Walk to loot box. Prompt appears.
- [ ] Press F. Panel opens. Cursor unlocks. Player movement freezes. Headbob is silent.
- [ ] LMB wood in hotbar → cursor holds wood.
- [ ] LMB empty storage slot → wood drops in.
- [ ] LMB wood storage slot → wood picks up. Move mouse, cursor follows.
- [ ] LMB another wood storage slot → stacks merge.
- [ ] RMB a wood stack → cursor holds 1 wood.
- [ ] RMB an empty slot → drops 1.
- [ ] Shift+LMB a hotbar item → moves to storage.
- [ ] Shift+LMB it back → returns to hotbar.
- [ ] Pick up wood, press F → wood returns to source, panel closes.
- [ ] Open pause menu → Save game.
- [ ] Open save file JSON. Confirm `storages[0].boxId == "OriginalShip/LootBox"` and `slots[]` reflects what you put in.
- [ ] Stop Play. Re-enter Play. Load the save.
- [ ] Open loot box. Contents match what you saved.
- [ ] Confirm an equippable stored in the box is **NOT** auto-pulled into the hotbar after load (the `TryAddItem` patch is working).

- [ ] **Step 12.3: Integration smoke test in a build**

Per CLAUDE.md's MainMenu trap warning — this gates against the singleton-missing-in-build bug. Build the project to `Solar System 2.exe` (`File → Build And Run`). In the built game:

- [ ] Click PLAY from the main menu (so the build path through `EnsureGameplaySingletons` is exercised).
- [ ] Reach the loot box, open it, deposit an item, save, re-launch the .exe, LOAD the save, confirm the item is still there.

If the panel fails to open in the build (but worked in Editor), check `Player.log` for null-ref on `StorageUI.Instance` — that's the MainMenu-trap fingerprint; double-check Step 12.1.

- [ ] **Step 12.4: Final commit**

```
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(storage): seed StorageUI in EnsureGameplaySingletons"
```

---

## Out of scope (deferred — see spec)

- Tooltips on hover, "take all" / "deposit all" buttons, drag-paint, mouse-wheel split, audio cues, animated open/close.
- Storage-to-storage drag.
- Searchable / filter UI.
- Autosave-suppression while panel is open (the spec notes the small data-loss window if autosave fires mid-drag; tracked but not implemented in this plan).

If any of these become priorities, they get their own spec + plan.
