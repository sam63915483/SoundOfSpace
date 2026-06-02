using UnityEditor;
using UnityEngine;

// Manual test runner — no NUnit dependency. Run via Tools menu, look at
// Console for PASS / FAIL lines. The pure-logic equivalent of pytest
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
