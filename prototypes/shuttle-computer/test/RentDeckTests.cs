// Executes the rent ledger (MushroomQuest) and the cassette machine
// (CassetteDeck) against the stubs in RentDeckStubs.cs.
//
//   python prototypes/shuttle-computer/test/verify-rent.py
//
// These are the two files the 2026-08-14 rent revamp put the loop's pressure
// into. Everything they decide is invisible until it is already wrong — a
// lockout that fires a day early, a blank that evaporates on a failed eject, an
// ejected tape that doesn't survive a reload — so none of it is left to a
// play-test to discover.

using System;

public static class RentDeckTests
{
    static int _checks;
    static int _failures;

    static void Check(bool cond, string what)
    {
        _checks++;
        if (cond) return;
        _failures++;
        Console.WriteLine("  FAIL - " + what);
    }

    static void Eq(int actual, int expected, string what)
    {
        Check(actual == expected, what + " (expected " + expected + ", got " + actual + ")");
    }

    static void Section(string name)
    {
        Console.WriteLine(name);
    }

    /// Fresh world, rent haggled to `rate`, standing on day 1 with `money`.
    static void NewWorld(int rate, int money)
    {
        StoryDirector.Reset();
        GalaxyTime.Reset();
        PlayerWallet.Reset();
        Hotbar.Reset();
        TraxPrints.Clear();
        CassetteDeck.Clear();

        PlayerWallet.Instance.Money = money;
        GalaxyTime.Instance.Day = 1;
        if (rate > 0) MushroomQuest.SettleRent(rate);
    }

    public static int Main()
    {
        Console.WriteLine();
        Console.WriteLine("=== RENT + CASSETTE MACHINE ===");
        Console.WriteLine();

        Ladder();
        Accrual();
        Lockout();
        Payment();
        Insert();
        Eject();
        Printing();
        Persistence();

        Console.WriteLine();
        if (_failures > 0)
        {
            Console.WriteLine("rent/deck FAILED - " + _failures + " of " + _checks + " checks");
            return 1;
        }
        Console.WriteLine("rent/deck VERIFIED - " + _checks + " checks, all passed.");
        return 0;
    }

    // ── the haggle ───────────────────────────────────────────────────────

    static void Ladder()
    {
        Section("the ladder");

        int[] r = MushroomQuest.RentRungs;
        Eq(r.Length, 4, "four rungs");
        Eq(r[0], 50, "opens at $50/day");
        Eq(r[1], 30, "climbs down to $30");
        Eq(r[2], 20, "then $20");
        Eq(r[3], 10, "floor is $10");

        // THE RULE THAT MATTERS MOST. Haggling rent to zero would delete the
        // money pressure the whole cassette loop runs on.
        for (int i = 0; i < r.Length; i++) Check(r[i] > 0, "no rung is free (rung " + i + ")");
        for (int i = 1; i < r.Length; i++) Check(r[i] < r[i - 1], "rungs descend (rung " + i + ")");

        NewWorld(0, 0);
        Check(!MushroomQuest.RentSettled, "unsettled before the haggle");
        Eq(MushroomQuest.RentPerDay, 0, "no rate before the haggle");

        MushroomQuest.SettleRent(10);
        Check(MushroomQuest.RentSettled, "settled after the haggle");
        Eq(MushroomQuest.RentPerDay, 10, "the haggled rate sticks");
        Eq(MushroomQuest.RentBalance, 0, "you owe nothing on the day you agree");
        Eq(MushroomQuest.RentLastBilledDay, 1, "today counts as billed, so the blanks are free");
    }

    // ── accrual ──────────────────────────────────────────────────────────

    static void Accrual()
    {
        Section("accrual");

        // The handoff's worked example, verbatim: $10/day x 3 missed days = $30.
        NewWorld(10, 0);
        MushroomQuest.AccrueRentTo(4);
        Eq(MushroomQuest.RentBalance, 30, "3 days at $10 is $30");
        Eq(MushroomQuest.UnpaidDays, 3, "which is 3 days behind");

        // LINEAR. Another three days adds another $30 — no interest, no
        // compounding, ever. Sam was explicit.
        MushroomQuest.AccrueRentTo(7);
        Eq(MushroomQuest.RentBalance, 60, "another 3 days adds another $30, not more");

        // Billing the same day twice must not double-charge.
        int before = MushroomQuest.RentBalance;
        MushroomQuest.AccrueRentTo(7);
        MushroomQuest.AccrueRentTo(7);
        Eq(MushroomQuest.RentBalance, before, "re-billing the same day charges nothing");

        // A day that has already passed can't bill backwards either.
        MushroomQuest.AccrueRentTo(3);
        Eq(MushroomQuest.RentBalance, before, "billing an earlier day charges nothing");

        // A long absence lands as ONE linear charge, not a stack of them.
        NewWorld(20, 0);
        MushroomQuest.AccrueRentTo(11);
        Eq(MushroomQuest.RentBalance, 200, "10 days away at $20 is $200");

        // An unsettled world never accrues — you can't owe rent you never agreed.
        NewWorld(0, 0);
        MushroomQuest.AccrueRentTo(30);
        Eq(MushroomQuest.RentBalance, 0, "no haggle, no rent");
    }

    // ── the embargo ──────────────────────────────────────────────────────

    static void Lockout()
    {
        Section("the plugin embargo");

        Eq(MushroomQuest.LockoutDays, 5, "the embargo is a 5-day rule");

        NewWorld(10, 0);
        MushroomQuest.AccrueRentTo(5);           // 4 days elapsed
        Eq(MushroomQuest.UnpaidDays, 4, "4 days behind");
        Check(!MushroomQuest.PluginsLocked, "NOT locked at 4 days");

        MushroomQuest.AccrueRentTo(6);           // 5 days elapsed
        Eq(MushroomQuest.UnpaidDays, 5, "5 days behind");
        Check(MushroomQuest.PluginsLocked, "locked at exactly 5 days");

        // The rule tracks DAYS, not dollars, so it fires at the same point
        // whether the player haggled to $50 or to $10.
        NewWorld(50, 0);
        MushroomQuest.AccrueRentTo(5);
        Check(!MushroomQuest.PluginsLocked, "4 days at $50 is still not locked");
        MushroomQuest.AccrueRentTo(6);
        Check(MushroomQuest.PluginsLocked, "5 days at $50 is locked");

        // A part payment that leaves anything at all still counts that day as
        // unpaid — rounding UP is the honest reading.
        NewWorld(10, 100);
        MushroomQuest.AccrueRentTo(6);           // $50, 5 days
        MushroomQuest.PayRent(41);               // $9 left
        Eq(MushroomQuest.RentBalance, 9, "part payment leaves the remainder");
        Eq(MushroomQuest.UnpaidDays, 1, "$9 of a $10 day is still a day owed");
        Check(!MushroomQuest.PluginsLocked, "and that lifts the embargo");
    }

    // ── paying ───────────────────────────────────────────────────────────

    static void Payment()
    {
        Section("paying Tev");

        // NOTHING IS AUTO-DEDUCTED. A rich player who never visits still owes.
        NewWorld(10, 1000);
        MushroomQuest.AccrueRentTo(8);
        Eq(MushroomQuest.RentBalance, 70, "rent accrued");
        Eq(PlayerWallet.Instance.Money, 1000, "and the wallet was NOT touched");
        Check(MushroomQuest.PluginsLocked, "rich and locked out — that's the point");

        // Partial payment: balance down, wallet down, embargo still on.
        int paid = MushroomQuest.PayRent(20);
        Eq(paid, 20, "paid $20");
        Eq(MushroomQuest.RentBalance, 50, "balance down to $50");
        Eq(PlayerWallet.Instance.Money, 980, "wallet down by exactly that");
        Check(MushroomQuest.PluginsLocked, "still locked at 5 days' worth");

        // Paying to zero lifts it immediately.
        paid = MushroomQuest.PayRent(50);
        Eq(paid, 50, "paid the rest");
        Eq(MushroomQuest.RentBalance, 0, "square");
        Check(!MushroomQuest.PluginsLocked, "embargo lifts on the spot");

        // Overpaying is capped at the balance — no credit ledger.
        NewWorld(10, 500);
        MushroomQuest.AccrueRentTo(3);           // $20
        paid = MushroomQuest.PayRent(500);
        Eq(paid, 20, "you can only pay what you owe");
        Eq(PlayerWallet.Instance.Money, 480, "and only that leaves the wallet");
        Eq(MushroomQuest.RentBalance, 0, "settled");

        // Can't afford it: nothing moves on either side.
        NewWorld(10, 5);
        MushroomQuest.AccrueRentTo(3);           // $20
        paid = MushroomQuest.PayRent(20);
        Eq(paid, 0, "a payment you can't cover moves nothing");
        Eq(MushroomQuest.RentBalance, 20, "balance unchanged");
        Eq(PlayerWallet.Instance.Money, 5, "wallet unchanged");

        // But you can always pay what you have.
        paid = MushroomQuest.PayRent(5);
        Eq(paid, 5, "part payment out of an empty pocket");
        Eq(MushroomQuest.RentBalance, 15, "chips away at it");
    }

    // ── the slot ─────────────────────────────────────────────────────────

    static void Insert()
    {
        Section("inserting a blank");

        NewWorld(10, 0);
        Check(!CassetteDeck.HasCassette, "the slot starts empty");
        Check(!CassetteDeck.Insert(), "nothing in hand, nothing goes in");

        // Blanks in the pack but NOT selected: refused. Sam wants to see the
        // cassette leave your hand.
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 3);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.Axe;
        Check(!CassetteDeck.Insert(), "holding an axe over a pack of blanks won't do");
        Eq(Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.BlankTapeT1), 3, "and nothing was consumed");

        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT1;
        Check(CassetteDeck.Insert(), "holding a blank, it goes in");
        Eq(CassetteDeck.InsertedTier, 1, "seated as a TAPE I");
        Eq(Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.BlankTapeT1), 2,
           "exactly one blank left the hotbar");

        // ONE AT A TIME.
        Check(!CassetteDeck.Insert(), "a second blank is refused");
        Eq(Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.BlankTapeT1), 2, "and is not eaten");

        // Tier follows the object, not a menu.
        NewWorld(10, 0);
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT2, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT2;
        Check(CassetteDeck.Insert(), "a TAPE II goes in");
        Eq(CassetteDeck.InsertedTier, 2, "seated as a TAPE II");
    }

    static void Eject()
    {
        Section("taking it back out");

        NewWorld(10, 0);
        Check(!CassetteDeck.EjectBlank(), "nothing to eject from an empty slot");

        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT2, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT2;
        CassetteDeck.Insert();
        Eq(Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.BlankTapeT2), 0, "hotbar emptied of T2");

        // A mis-insert must never be a trap, least of all with a T2 — that is
        // real money sitting in the machine.
        Check(CassetteDeck.EjectBlank(), "the unprinted blank comes back");
        Eq(Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.BlankTapeT2), 1, "the SAME tier came back");
        Check(!CassetteDeck.HasCassette, "and the slot is empty again");

        // No room: it stays in the machine rather than evaporating.
        NewWorld(10, 0);
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT1;
        CassetteDeck.Insert();
        Hotbar.Instance.Fill(Hotbar.ItemId.Wood, Hotbar.Instance.SlotCount);
        Check(!CassetteDeck.EjectBlank(), "a full hotbar refuses the eject");
        Check(CassetteDeck.HasCassette, "and the blank is STILL IN THE MACHINE, not destroyed");
    }

    // ── printing ─────────────────────────────────────────────────────────

    /// A pressing of a distinct song. The print id is DERIVED from the track
    /// itself, not from the name — two projects called different things but
    /// holding identical tracks are the same pressing and stack together — so
    /// the tests vary the KEY to get genuinely different songs.
    static string Press(string name, int tier, int key = 0)
    {
        var t = TraxTrack.Default();
        t.key = ((key % 12) + 12) % 12;
        var rec = TraxPrints.Register(name, t, tier);
        return rec == null ? null : rec.id;
    }

    static void Printing()
    {
        Section("printing");

        NewWorld(10, 0);
        string id = Press("SONG", 1);
        Check(!CassetteDeck.PrintTo(id), "no cassette in the slot, no print");

        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT1;
        CassetteDeck.Insert();

        Check(CassetteDeck.PrintTo(id), "with one seated, it prints");
        Check(!CassetteDeck.HasCassette, "the slot is empty afterwards");
        Check(CassetteDeck.HasEjected, "and the tape is on the eject");
        Check(CassetteDeck.EjectedPrintId == id, "it is the tape that was pressed");

        // The eject holds ONE. Print again with a tape still sitting there and
        // the machine refuses rather than overwriting it — the first tape must
        // not vanish.
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        CassetteDeck.Insert();
        string id2 = Press("OTHER SONG", 1, 5);
        Check(id2 != id, "a genuinely different song is a different print");
        Check(!CassetteDeck.PrintTo(id2), "won't print over an unclaimed tape");
        Check(CassetteDeck.EjectedPrintId == id, "the first tape is untouched");
        Check(CassetteDeck.HasCassette, "and the second blank stays seated");

        // Collect it: stacks by print identity like any printed tape.
        Check(CassetteDeck.TakeEjected(), "the tape comes off the machine");
        Check(!CassetteDeck.HasEjected, "the eject is clear");
        Eq(Hotbar.Instance.GetCassetteTotal(id), 1, "and it's in the hotbar");
        Check(!CassetteDeck.TakeEjected(), "nothing to take twice");

        // Now the second one can print.
        Check(CassetteDeck.PrintTo(id2), "and the machine is free again");
        Check(CassetteDeck.TakeEjected(), "second tape collected");
        Eq(Hotbar.Instance.GetCassetteTotal(id2), 1, "two different songs, two stacks");
        Eq(Hotbar.Instance.GetCassetteTotal(id), 1, "and the first stack didn't absorb it");

        // Same song again stacks rather than making a second pile.
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        CassetteDeck.Insert();
        CassetteDeck.PrintTo(id);
        CassetteDeck.TakeEjected();
        Eq(Hotbar.Instance.GetCassetteTotal(id), 2, "a second pressing of the same song stacks");

        // A TAPE II pressing of the same song is a DIFFERENT print id.
        string idT2 = Press("SONG", 2);
        Check(idT2 != id, "the T2 pressing is its own print");

        // THE PRINT SEQUENCE as the slot performs it: print → the tape becomes
        // the pending ejection → (2 s pause, then the slide, both cosmetic) →
        // it WAITS at the mouth until the player presses F, which is the
        // TakeEjected call below. Nothing collects it automatically.
        NewWorld(10, 0);
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT1;
        CassetteDeck.Insert();
        string idHand = Press("STRAIGHT TO HAND", 1, 4);
        Check(CassetteDeck.PrintTo(idHand), "printing hands the tape to the slot");
        Check(!CassetteDeck.HasCassette, "the blank was consumed");
        Check(CassetteDeck.HasEjected, "and is now the tape coming out");
        Check(CassetteDeck.TakeEjected(), "pressing F at the mouth takes it");
        Eq(Hotbar.Instance.GetCassetteTotal(idHand), 1, "the tape is in the hotbar");
        Check(!CassetteDeck.HasEjected, "and the machine is clear");

        // Nowhere to put it: the tape stays on the machine.
        NewWorld(10, 0);
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT1;
        CassetteDeck.Insert();
        string id3 = Press("NO ROOM", 1);
        CassetteDeck.PrintTo(id3);
        Hotbar.Instance.Fill(Hotbar.ItemId.Wood, Hotbar.Instance.SlotCount);
        Check(!CassetteDeck.TakeEjected(), "a full hotbar refuses the pickup");
        Check(CassetteDeck.HasEjected, "and the tape waits on the machine");
    }

    // ── save round-trip ──────────────────────────────────────────────────

    static void Persistence()
    {
        Section("save round-trip");

        // A blank seated in the slot survives a reload.
        NewWorld(10, 0);
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT2, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT2;
        CassetteDeck.Insert();

        var save = new TraxLibrarySave();
        TraxPrints.Capture(save);
        CassetteDeck.Capture(save);
        Eq(save.deckInsertedTier, 2, "the seated tier is written out");

        CassetteDeck.Clear();
        TraxPrints.Apply(save);
        CassetteDeck.Apply(save);
        Eq(CassetteDeck.InsertedTier, 2, "and comes back as the same tier");

        // An ejected, unclaimed tape survives too — with its song.
        NewWorld(10, 0);
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT1;
        CassetteDeck.Insert();
        string id = Press("SURVIVOR", 1);
        CassetteDeck.PrintTo(id);

        save = new TraxLibrarySave();
        TraxPrints.Capture(save);
        CassetteDeck.Capture(save);

        CassetteDeck.Clear();
        TraxPrints.Clear();
        TraxPrints.Apply(save);
        CassetteDeck.Apply(save);
        Check(CassetteDeck.HasEjected, "the unclaimed tape is still on the machine");
        Check(CassetteDeck.EjectedPrintId == id, "and it is still the same song");
        Check(TraxPrints.DisplayName(CassetteDeck.EjectedPrintId) == "SURVIVOR", "by name too");

        // A tape whose record didn't survive must NOT restore as an
        // unpickupable prop welded to the machine.
        save.prints.Clear();
        TraxPrints.Clear();
        TraxPrints.Apply(save);
        CassetteDeck.Apply(save);
        Check(!CassetteDeck.HasEjected, "an orphaned tape restores as an empty eject");

        // New Game clears the machine — statics leak across the main menu.
        NewWorld(10, 0);
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeT1;
        CassetteDeck.Insert();
        CassetteDeck.PrintTo(Press("OLD WORLD", 1));
        CassetteDeck.Clear();
        Check(!CassetteDeck.HasCassette && !CassetteDeck.HasEjected, "New Game empties the machine");

        // And the rent ledger clears with the story flags.
        StoryDirector.Reset();
        Check(!MushroomQuest.RentSettled, "New Game forgets the haggle");
        Eq(MushroomQuest.RentBalance, 0, "and the debt");
    }
}
