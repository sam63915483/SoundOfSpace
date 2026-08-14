// The SALE, run for real with no Unity in the room. Companion to
// AlienTasteTests; both are driven by test/verify-taste.py.
//
// Covers the three things that fail silently: an alien's memory getting crossed
// with another alien's through the flattened save list, the repeat rule being
// launderable by nudging a dial, and greed failing to actually cost anything.

using System;

public static class TapeOfferTests
{
    public static int Checks, Failures;

    static void Check(bool cond, string what)
    {
        Checks++;
        if (cond) return;
        Failures++;
        Console.WriteLine("  FAIL  " + what);
    }

    static void Eq(object got, object want, string what)
    {
        Check(Equals(got, want), what + ": got " + got + ", want " + want);
    }

    static string[] Ids()
    {
        var ids = new string[200];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = i % 3 == 0 ? "scene:Alien" + i : "cell:" + (i % 7) + ":" + (i * 31);
        return ids;
    }

    public static void RunAll()
    {
        Memory();
        Negotiation();
        Feedback();
    }

    // ── what an alien remembers ──────────────────────────────────────────

    static void Memory()
    {
        Console.WriteLine("memory");
        TapeMemory.Clear();
        string id = "scene:Vorn";
        var song = new double[] { 5, 5, 5, 5, 5, 5 };

        Check(!TapeMemory.HasHeard(id, song), "a stranger has heard nothing");
        TapeMemory.Remember(id, song);
        Check(TapeMemory.HasHeard(id, song), "and remembers what they were played");
        Check(!TapeMemory.HasHeard("scene:Skell", song), "but only that alien remembers it");

        // THE EXPLOIT THIS EXISTS TO CLOSE: nudging a track slightly must not
        // launder it into a brand new song.
        var nudged = new double[] { 5.4, 5, 5, 5, 5, 5 };
        Check(TapeMemory.HasHeard(id, nudged), "a barely-changed track is still the same song");

        // But a genuine rewrite genuinely is new.
        var rewritten = new double[] { 1, 9, 2, 8, 1, 9 };
        Check(!TapeMemory.HasHeard(id, rewritten), "a real rewrite counts as a new song");

        TapeMemory.Remember(id, song);
        Eq(TapeMemory.HeardCount(id), 1, "remembering the same song twice does not double it");

        Eq(TapeMemory.Bond(id), 0, "bond starts at nothing");
        TapeMemory.AddBond(id, 8);
        Eq(TapeMemory.Bond(id), 8, "bond accrues");
        TapeMemory.AddBond(id, -100);
        Eq(TapeMemory.Bond(id), 0, "bond floors at 0");
        TapeMemory.AddBond(id, 500);
        Eq(TapeMemory.Bond(id), 100, "bond caps at 100");

        Check(!TapeMemory.IsContact(id), "not a contact until they hand over the number");
        TapeMemory.MakeContact(id);
        Check(TapeMemory.IsContact(id), "and then they are");

        // Round trip. The flattened dial list is the risky part.
        TapeMemory.Remember(id, rewritten);
        TapeMemory.Remember("scene:Skell", song);
        TapeMemory.AddBond("scene:Skell", 30);
        int before = TapeMemory.HeardCount(id);

        TapeMemorySave blob = TapeMemory.Capture();
        TapeMemory.Clear();
        Eq(TapeMemory.HeardCount(id), 0, "cleared");
        TapeMemory.Apply(blob);

        Eq(TapeMemory.HeardCount(id), before, "history survived the save file");
        Eq(TapeMemory.Bond(id), 100, "bond survived");
        Check(TapeMemory.IsContact(id), "contact survived");
        Check(TapeMemory.HasHeard(id, song), "the right alien still remembers the right song");
        Eq(TapeMemory.Bond("scene:Skell"), 30, "the second alien's bond did not get crossed");
        Check(!TapeMemory.HasHeard("scene:Skell", rewritten),
              "AND THEIR HISTORIES DID NOT GET CROSSED - the flat list realigned correctly");

        // A file claiming more history than it supplies must lose history, not
        // throw and not mis-pair.
        var broken = new TapeMemorySave();
        broken.ids.Add("scene:Liar");
        broken.bond.Add(50);
        broken.contact.Add(true);
        broken.heardCounts.Add(99);
        TapeMemory.Apply(broken);
        Eq(TapeMemory.Bond("scene:Liar"), 50, "a truncated file still loads what it can");

        TapeMemory.Apply(null);
        Eq(TapeMemory.ContactCount, 0, "a null save leaves nobody");
    }

    // ── the negotiation ──────────────────────────────────────────────────

    static void Negotiation()
    {
        Console.WriteLine("negotiation");
        TapeMemory.Clear();
        string id = "scene:Torv";
        double[] taste = AlienTaste.TastePoint(id);

        double sat;
        Eq(TapeOffer.Listen(id, taste, false, out sat), TapeOffer.Reaction.Liked,
           "a perfect tape is liked even on a losing coin flip");
        Eq(sat, 100.0, "and scores 100");

        var far = new double[AlienTaste.DialCount];
        for (int i = 0; i < far.Length; i++) far[i] = taste[i] > 5 ? 0 : 10;
        Eq(TapeOffer.Listen(id, far, true, out sat), TapeOffer.Reaction.Rejected,
           "a hopeless tape is refused even on a winning flip");

        // The repeat rule is checked before taste: they notice the song first.
        TapeMemory.Remember(id, taste);
        Eq(TapeOffer.Listen(id, taste, true, out sat), TapeOffer.Reaction.AlreadyHeard,
           "the same song again is caught before taste is considered");
        TapeMemory.Clear();

        int value = TapeOffer.Value(id, 6, 1, 100, false);
        int ceiling = TapeOffer.Ceiling(id, value);
        Check(ceiling >= value, "their ceiling is at or above their own valuation");

        int counter;
        Eq(TapeOffer.Judge(id, value, 1, out counter), TapeOffer.Response.Accepted,
           "a giveaway price is accepted");
        Eq(counter, 1, "AT THE PLAYER'S NUMBER - they never talk you upward");

        Eq(TapeOffer.Judge(id, value, ceiling, out counter), TapeOffer.Response.Accepted,
           "asking exactly their ceiling is accepted");

        Eq(TapeOffer.Judge(id, value, ceiling + 1, out counter), TapeOffer.Response.TooLow,
           "one over the ceiling is a haggle, not an insult");
        Check(counter <= ceiling, "their counter never exceeds what they will pay");
        Check(counter >= 1, "and is always a real number");

        int greedy = ceiling * 3 + 50;
        Eq(TapeOffer.Judge(id, value, greedy, out counter), TapeOffer.Response.FinalOffer,
           "an outrageous ask provokes the take-it-or-leave-it");
        Check(counter < value, "AND THE FINAL OFFER IS BELOW WHAT THEY WOULD HAVE PAID - greed costs");

        Check(TapeOffer.BondForSale(value, value / 2) > TapeOffer.BondForSale(value, value),
              "asking under their value earns extra bond");

        // Every alien has to be sellable to. One with an unreachable ceiling
        // would be a dead end the player has no way to diagnose.
        foreach (string a in Ids())
        {
            int v = TapeOffer.Value(a, 6, 1, 100, false);
            if (TapeOffer.Ceiling(a, v) < 1) { Check(false, "unsellable alien: " + a); return; }
        }
        Check(true, "every alien in a sample of 200 can be sold to");

        // A bad tape to a fussy alien is still worth SOMETHING, so a poor match
        // reads as a poor price rather than as a broken interaction.
        foreach (string a in Ids())
        {
            if (TapeOffer.Value(a, 2, 1, 5, false) >= 1) continue;
            Check(false, "a poor tape priced at zero for " + a);
            return;
        }
        Check(true, "even a poor match is worth a positive amount");
    }

    // ── the words ────────────────────────────────────────────────────────

    static void Feedback()
    {
        Console.WriteLine("feedback lines");
        string id = "scene:Grek";
        double[] taste = AlienTaste.TastePoint(id);

        var wrong = new double[AlienTaste.DialCount];
        for (int i = 0; i < wrong.Length; i++) wrong[i] = taste[i];
        wrong[1] = taste[1] > 5 ? 0 : 10;                 // CRUNCH miles off

        string line = AlienFeedback.ForRejection(id, wrong, "GLORP", 0);
        Check(line.Contains("CRUNCH"), "the rejection names the dial that is wrong: " + line);
        Check(line.Contains("GLORP"), "and mentions their genre second");
        Check(line[0] == char.ToUpperInvariant(line[0]), "and starts with a capital");

        string close = AlienFeedback.ForRejection(id, taste, "GLORP", 0);
        Check(!close.Contains("CRUNCH") && !close.Contains("PULSE"),
              "a dead-on tape gets no invented complaint: " + close);

        for (uint v = 0; v < 8; v++)
        {
            bool empty = string.IsNullOrEmpty(AlienFeedback.ForRejection(id, wrong, "DRIFT", v))
                      || string.IsNullOrEmpty(AlienFeedback.ForRepeat(v))
                      || string.IsNullOrEmpty(AlienFeedback.ForLiked(90, v))
                      || string.IsNullOrEmpty(AlienFeedback.ForLiked(70, v))
                      || string.IsNullOrEmpty(AlienFeedback.ForLiked(50, v))
                      || string.IsNullOrEmpty(AlienFeedback.ForFinalOffer(40, v));
            if (empty) { Check(false, "a feedback variant came back empty (v=" + v + ")"); return; }
        }
        Check(true, "every phrasing variant produces a line");

        for (uint v = 0; v < 8; v++)
        {
            if (!AlienFeedback.ForRejection(id, wrong, "VOLT", v).Contains("{d}")) continue;
            Check(false, "a template placeholder leaked into player-facing text");
            return;
        }
        Check(true, "no template placeholder survives into player-facing text");
    }
}
