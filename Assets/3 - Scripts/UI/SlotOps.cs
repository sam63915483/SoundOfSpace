using UnityEngine;

// Phase 4: tracks where a staged fish originally came from so the picker
// (FishStagingUI) and cook/sell stage lists can return it to its exact
// slot if the player cancels. container holds a live reference to the
// source's Slot array (hotbar / a bag's bagContents / a LootBox's slots);
// index is the slot position within that array. Top-level so callers in
// other files reference it as `FishSource` without the SlotOps. prefix.
public struct FishSource
{
    public Hotbar.Slot[] container;
    public int index;
    public bool IsValid => container != null && index >= 0 && index < container.Length;
}

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
        // For Fish cursors: the FishEntry payload travels with the cursor so
        // dropping it into any destination slot restores the full per-fish data.
        // Null for non-Fish cursors.
        public FishEntry fishData;
        // For FishBag cursors: the 5-slot internal array travels with the
        // cursor. Without this, picking up the bag dropped the bag's contents
        // entirely — the bag would still appear in inventory but be unable
        // to receive new fish. Null for non-FishBag cursors.
        public Hotbar.Slot[] bagContents;
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
        // Spill into empty slots. For Fish/FishBag, cap == 1 so this is the
        // only branch that runs and fishData/bagContents transfer cleanly.
        for (int i = 0; i < dest.Length && remaining > 0; i++)
        {
            if (dest[i].id != Hotbar.ItemId.None) continue;
            int take = Mathf.Min(cap, remaining);
            dest[i] = new Hotbar.Slot { id = s.id, count = take, fishData = s.fishData, bagContents = s.bagContents };
            remaining -= take;
        }

        if (remaining == 0) source[idx] = default;
        else                source[idx] = new Hotbar.Slot { id = s.id, count = remaining, fishData = s.fishData, bagContents = s.bagContents };
    }

    static void PickUpFull(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        var s = container[idx];
        if (s.id == Hotbar.ItemId.None || s.count <= 0) return;
        cursor.id = s.id;
        cursor.count = s.count;
        cursor.sourceContainer = container;
        cursor.sourceIndex = idx;
        cursor.fishData = s.fishData;       // carry fish payload onto cursor
        cursor.bagContents = s.bagContents; // carry bag's 5-slot array onto cursor
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
        cursor.fishData = s.fishData;       // single-fish slots: payload moves to cursor
        cursor.bagContents = s.bagContents; // single-bag slots: contents move to cursor
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
            container[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData, bagContents = cursor.bagContents };
            ClearCursor(ref cursor);
            return;
        }

        // Same id — try to merge.
        if (s.id == cursor.id)
        {
            int cap = Hotbar.StackMax(s.id);
            int room = cap - s.count;
            int moved = Mathf.Min(room, cursor.count);
            if (moved <= 0) return; // dest full of same item — no-op (covers Fish/FishBag: cap=1 so room=0)
            s.count += moved;
            container[idx] = s;
            cursor.count -= moved;
            if (cursor.count <= 0) ClearCursor(ref cursor);
            return;
        }

        // Different id — swap cursor with slot.
        var temp = s;
        container[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData, bagContents = cursor.bagContents };
        cursor.id = temp.id;
        cursor.count = temp.count;
        cursor.fishData = temp.fishData;        // swap pulls slot payload onto cursor
        cursor.bagContents = temp.bagContents;  // same for bag contents
        // sourceContainer/sourceIndex stay as the original pickup origin —
        // that's where return-on-close should put it.
    }

    static void DepositOne(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        var s = container[idx];

        // Empty slot — drop one.
        if (s.id == Hotbar.ItemId.None || s.count <= 0)
        {
            container[idx] = new Hotbar.Slot { id = cursor.id, count = 1, fishData = cursor.fishData, bagContents = cursor.bagContents };
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
        cursor.fishData = null;
        cursor.bagContents = null;
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
                src[idx] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData, bagContents = cursor.bagContents };
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
            src[i] = new Hotbar.Slot { id = cursor.id, count = cursor.count, fishData = cursor.fishData, bagContents = cursor.bagContents };
            ClearCursor(ref cursor);
            return true;
        }
        return false; // caller blocks close
    }
}
