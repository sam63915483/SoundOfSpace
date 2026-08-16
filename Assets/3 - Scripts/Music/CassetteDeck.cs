using System;
using UnityEngine;

/// <summary>
/// The physical state of the shuttle computer's cassette machine: what is
/// SEATED IN THE SLOT, and what is sitting UNCLAIMED ON THE EJECT.
///
/// ── Why this exists ──────────────────────────────────────────────────────
/// Printing used to be a menu action: pick a tier, pick a quantity, press a
/// button, tapes appear in your pocket. It read like buying something. Now it
/// is a ritual you perform on a machine — you put a blank in, the machine has a
/// blank in it, the print comes out the other side and you pick it up.
///
/// That makes three things true that a dialog could not express:
///   • THE SLOT IS THE GATE. No blank seated, no print. The hotbar count is not
///     consulted at print time any more; the thing physically in the machine is.
///   • THE SEATED BLANK'S TIER IS THE PRINT'S TIER. Choosing "TAPE II" from a
///     stepper while holding no TAPE II was always a lie. Now the tier is a
///     property of the object you put in.
///   • ONE TAPE PER PRINT. Quantity was only ever a way to skip the ritual.
///
/// ── World state, not player state ────────────────────────────────────────
/// One computer, one slot, one eject — so in co-op this is shared, exactly like
/// the project shelf and the print table. Host-authoritative for v1 (the
/// locker's one-open-lock rule is the precedent). The ejected tape belongs to
/// whoever picks it up.
///
/// Both fields ride the world save and both clear on New Game.
/// </summary>
public static class CassetteDeck
{
    /// Tier of the blank currently seated: 0 = the slot is empty, 1 or 2 = a
    /// blank of that tier is in the machine. A PRINTED tape is never in here —
    /// printing moves it straight to the eject.
    public static int InsertedTier { get; private set; }

    /// The print id of a finished tape sitting on the eject, unclaimed.
    /// Null/empty when the eject is clear.
    public static string EjectedPrintId { get; private set; }

    /// Fires whenever either changes, so the slot and eject props can show or
    /// hide their cassette without polling in Update.
    public static event Action OnChanged;

    public static bool HasCassette => InsertedTier > 0;
    public static bool HasEjected  => !string.IsNullOrEmpty(EjectedPrintId);

    static Hotbar.ItemId BlankIdFor(int tier) =>
        tier >= 2 ? Hotbar.ItemId.BlankTapeT2 : Hotbar.ItemId.BlankTapeT1;

    /// The blank the player is HOLDING — the selected hotbar slot, not merely
    /// somewhere in the pack. Sam's call: you should see the cassette go in, and
    /// that only reads if it left your hand.
    public static int HeldBlankTier()
    {
        if (Hotbar.Instance == null) return 0;
        Hotbar.ItemId id = Hotbar.Instance.GetEquippedSlotId();
        if (id == Hotbar.ItemId.BlankTapeT1) return 1;
        if (id == Hotbar.ItemId.BlankTapeT2) return 2;
        return 0;
    }

    /// <summary>
    /// Seat the held blank. Consumes exactly one from the hotbar.
    /// Refused if the slot is already occupied — one at a time, always.
    /// </summary>
    public static bool Insert()
    {
        if (HasCassette) return false;
        int tier = HeldBlankTier();
        if (tier <= 0) return false;
        if (Hotbar.Instance == null) return false;
        if (!Hotbar.Instance.SpendResource(BlankIdFor(tier), 1)) return false;

        InsertedTier = tier;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Take the unprinted blank back out. A mis-insert must never be a trap —
    /// especially not with a TAPE II seated by accident, which is real money.
    ///
    /// If the hotbar has no room the blank STAYS IN THE MACHINE rather than
    /// evaporating; the caller says so.
    /// </summary>
    public static bool EjectBlank()
    {
        if (!HasCassette) return false;
        if (Hotbar.Instance == null) return false;
        int leftover = Hotbar.Instance.AddResource(BlankIdFor(InsertedTier), 1);
        if (leftover > 0) return false;

        InsertedTier = 0;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// The print itself: the seated blank becomes the finished tape and moves to
    /// the eject. Returns false if there was nothing to print onto, or if the
    /// last tape is still sitting there unclaimed.
    /// </summary>
    public static bool PrintTo(string printId)
    {
        if (!HasCassette || HasEjected) return false;
        if (string.IsNullOrEmpty(printId)) return false;

        InsertedTier = 0;
        EjectedPrintId = printId;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Pick the finished tape up off the machine. Stacks by print identity in
    /// the hotbar exactly as a printed tape always has. Leaves it on the eject
    /// if there is nowhere to put it.
    /// </summary>
    public static bool TakeEjected()
    {
        if (!HasEjected) return false;
        if (Hotbar.Instance == null) return false;
        if (Hotbar.Instance.AddCassette(EjectedPrintId, 1) <= 0) return false;

        EjectedPrintId = null;
        OnChanged?.Invoke();
        return true;
    }

    // ── save / load ──────────────────────────────────────────────────────

    /// New Game runs no Apply, so a static would carry the last world's machine
    /// state into the next one. Called from NewGameReset.
    public static void Clear()
    {
        InsertedTier = 0;
        EjectedPrintId = null;
        OnChanged?.Invoke();
    }

    public static void Capture(TraxLibrarySave save)
    {
        if (save == null) return;
        save.deckInsertedTier = InsertedTier;
        save.deckEjectedPrintId = EjectedPrintId ?? "";
    }

    /// <summary>
    /// Never trusts the stored print id blindly: a tape whose record didn't
    /// survive the load would be an unpickupable prop welded to the machine, so
    /// an unknown id restores as an empty eject instead.
    ///
    /// Applied AFTER TraxPrints, which is why that check can be made at all.
    /// </summary>
    public static void Apply(TraxLibrarySave save)
    {
        InsertedTier = 0;
        EjectedPrintId = null;

        if (save != null)
        {
            InsertedTier = Mathf.Clamp(save.deckInsertedTier, 0, 2);
            string id = save.deckEjectedPrintId;
            if (!string.IsNullOrEmpty(id) && TraxPrints.Get(id) != null) EjectedPrintId = id;
        }

        OnChanged?.Invoke();
    }
}
