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
        // For Mushroom / MushroomSapling cursors: the species key travels with
        // the cursor. Mushroom stacks are SPECIES-PURE, so without this a red
        // stack dropped onto a blue one would merge and silently take the
        // destination's species.
        public string mushroomSpecies;
        // For Cassette cursors: the PRINT id travels with the cursor, for
        // exactly the reason the species does. Without it, dragging a tape into
        // a locker would drop which SONG it was and leave a nameless cassette.
        public string cassetteId;
        public bool IsHeld => id != Hotbar.ItemId.None && count > 0;
    }

    /// The variant a cursor is carrying — a mushroom species or a cassette's
    /// song — or null. Mirrors <see cref="Hotbar.VariantOf(Hotbar.Slot)"/> so
    /// the two sides of a drag agree on what makes two stacks incompatible.
    static string VariantOf(in CursorState c) =>
        c.id == Hotbar.ItemId.Cassette ? c.cassetteId
        : Hotbar.IsMushroomItem(c.id) ? c.mushroomSpecies : null;

    /// Can these two stack together? Same id, and for mushrooms the same
    /// species as well — the whole point of species-pure stacks.
    static bool CanStack(Hotbar.Slot slot, in CursorState cursor) =>
        slot.id == cursor.id
        && (!Hotbar.CarriesVariant(slot.id) || Hotbar.VariantOf(slot) == VariantOf(cursor));

    static bool CanStack(Hotbar.Slot a, Hotbar.Slot b) =>
        a.id == b.id
        && (!Hotbar.CarriesVariant(a.id) || Hotbar.VariantOf(a) == Hotbar.VariantOf(b));

    /// Money-slot rule, applied at every write into a container. The hotbar's
    /// money slot takes money and nothing else, and money can't sit in any other
    /// hotbar slot; storage containers are unrestricted, so a locker full of cash
    /// is fine. Enforcing it here rather than in each caller means the rule holds
    /// for click, right-click, shift-quick-move AND return-on-close — miss one and
    /// that's the path a player finds.
    static bool Accepts(Hotbar.Slot[] container, int idx, Hotbar.ItemId id) =>
        Hotbar.SlotAccepts(container, idx, id);

    // LMB on a slot: pick up the entire stack (or deposit/swap/merge if held).
    public static void HandleLeftClick(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        if (container == null || idx < 0 || idx >= container.Length) return;
        if (cursor.IsHeld && !Accepts(container, idx, cursor.id)) return;
        if (cursor.IsHeld) Deposit(container, idx, ref cursor);
        else               PickUpFull(container, idx, ref cursor);
    }

    // RMB on a slot: pick up one item (or drop one if held with same id).
    public static void HandleRightClick(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        if (container == null || idx < 0 || idx >= container.Length) return;
        if (cursor.IsHeld && !Accepts(container, idx, cursor.id)) return;
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
                if (!CanStack(dest[i], s)) continue;
                if (!Accepts(dest, i, s.id)) continue;
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
            if (!Accepts(dest, i, s.id)) continue;
            int take = Mathf.Min(cap, remaining);
            dest[i] = new Hotbar.Slot { id = s.id, count = take, fishData = s.fishData, bagContents = s.bagContents, mushroomSpecies = s.mushroomSpecies, cassetteId = s.cassetteId };
            remaining -= take;
        }

        if (remaining == 0) source[idx] = default;
        else                source[idx] = new Hotbar.Slot { id = s.id, count = remaining, fishData = s.fishData, bagContents = s.bagContents, mushroomSpecies = s.mushroomSpecies, cassetteId = s.cassetteId };
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
        cursor.mushroomSpecies = s.mushroomSpecies;
        cursor.cassetteId = s.cassetteId;
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
        cursor.mushroomSpecies = s.mushroomSpecies;
        cursor.cassetteId = s.cassetteId;
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
            container[idx] = NewSlotFrom(cursor, cursor.count);
            ClearCursor(ref cursor);
            return;
        }

        // Same id (and species, for mushrooms) — try to merge.
        if (CanStack(s, cursor))
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

        // Different item (or a different mushroom species) — swap cursor with slot.
        var temp = s;
        container[idx] = NewSlotFrom(cursor, cursor.count);
        cursor.id = temp.id;
        cursor.count = temp.count;
        cursor.fishData = temp.fishData;        // swap pulls slot payload onto cursor
        cursor.bagContents = temp.bagContents;  // same for bag contents
        cursor.mushroomSpecies = temp.mushroomSpecies;
        cursor.cassetteId = temp.cassetteId;
        // sourceContainer/sourceIndex stay as the original pickup origin —
        // that's where return-on-close should put it.
    }

    static void DepositOne(Hotbar.Slot[] container, int idx, ref CursorState cursor)
    {
        var s = container[idx];

        // Empty slot — drop one.
        if (s.id == Hotbar.ItemId.None || s.count <= 0)
        {
            container[idx] = NewSlotFrom(cursor, 1);
            cursor.count -= 1;
            if (cursor.count <= 0) ClearCursor(ref cursor);
            return;
        }

        // Different item (or species) — RMB-on-different is a no-op (no swap on right click).
        if (!CanStack(s, cursor)) return;

        // Same stack — drop one if room.
        int cap = Hotbar.StackMax(s.id);
        if (s.count >= cap) return;
        s.count += 1;
        container[idx] = s;
        cursor.count -= 1;
        if (cursor.count <= 0) ClearCursor(ref cursor);
    }

    /// Scroll-wheel split. With a stack on the cursor, this shuttles
    /// <paramref name="delta"/> between the cursor and the slot it came from:
    /// positive takes more, negative puts some back. Both numbers stay live, so
    /// "click the 1000 in the locker, scroll down to 500, walk off with half" is
    /// one gesture with no dialog.
    ///
    /// Built generic rather than money-only — it costs nothing to let the player
    /// do the same with a stack of mushrooms, and Tev's payment panel is then
    /// just this mechanic pointed at his slot.
    ///
    /// Returns true if anything moved (so the caller can play a tick and repaint).
    public static bool AdjustCursorAmount(ref CursorState cursor, int delta)
    {
        if (!cursor.IsHeld || delta == 0) return false;
        var src = cursor.sourceContainer;
        int idx = cursor.sourceIndex;
        if (src == null || idx < 0 || idx >= src.Length) return false;

        // Single-item stacks (fish, bags, tools) have nothing to split.
        int cap = Hotbar.StackMax(cursor.id);
        if (cap <= 1) return false;

        var s = src[idx];
        // The source slot must be empty or hold the same thing — if the player
        // dropped something else there mid-drag, there's nothing to split against.
        bool srcEmpty = s.id == Hotbar.ItemId.None || s.count <= 0;
        if (!srcEmpty && !CanStack(s, cursor)) return false;

        if (delta > 0)
        {
            // Take from the source slot onto the cursor.
            if (srcEmpty) return false;
            int take = Mathf.Min(delta, s.count);
            if (take <= 0) return false;
            s.count -= take;
            src[idx] = s.count > 0 ? s : default;
            cursor.count += take;
            return true;
        }

        // Put back — never empty the cursor entirely, or the player loses the
        // drag with nothing to show for it. One always stays on the cursor.
        int give = Mathf.Min(-delta, cursor.count - 1);
        if (give <= 0) return false;
        if (!Accepts(src, idx, cursor.id)) return false;
        if (srcEmpty) src[idx] = NewSlotFrom(cursor, give);
        else
        {
            int room = cap - s.count;
            give = Mathf.Min(give, room);
            if (give <= 0) return false;
            s.count += give;
            src[idx] = s;
        }
        cursor.count -= give;
        return true;
    }

    static void ClearCursor(ref CursorState cursor)
    {
        cursor.id = Hotbar.ItemId.None;
        cursor.count = 0;
        cursor.sourceContainer = null;
        cursor.sourceIndex = -1;
        cursor.fishData = null;
        cursor.bagContents = null;
        cursor.mushroomSpecies = null;
        cursor.cassetteId = null;
    }

    static Hotbar.Slot NewSlotFrom(in CursorState cursor, int count) => new Hotbar.Slot
    {
        id = cursor.id,
        count = count,
        fishData = cursor.fishData,
        bagContents = cursor.bagContents,
        mushroomSpecies = cursor.mushroomSpecies,
        cassetteId = cursor.cassetteId,
    };

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
                src[idx] = NewSlotFrom(cursor, cursor.count);
                ClearCursor(ref cursor);
                return true;
            }
            if (CanStack(s, cursor))
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
            if (!Accepts(src, i, cursor.id)) continue;
            src[i] = NewSlotFrom(cursor, cursor.count);
            ClearCursor(ref cursor);
            return true;
        }
        return false; // caller blocks close
    }
}
