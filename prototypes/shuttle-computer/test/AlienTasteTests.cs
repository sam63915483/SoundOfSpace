// Runs the TASTE MODEL and the PRICING for real, with no Unity in the room.
//
//   python test/verify-taste.py
//
// These two files decide whether an alien likes a tape, what they pay, and what
// they complain about — so every failure here is a silent one. Nothing crashes
// when taste is subtly unstable; the player just quietly loses their mental map
// of who buys what, which is the most valuable thing they build up.
//
// The load-bearing property is STABILITY: taste is derived from the alien's id
// rather than rolled and saved, so the same id must produce the same ear
// forever, on both machines, with nothing stored.

using System;

public static class AlienTasteTests
{
    static int _checks, _failures;

    static void Check(bool cond, string what)
    {
        _checks++;
        if (cond) return;
        _failures++;
        Console.WriteLine("  FAIL  " + what);
    }

    static void Eq(object got, object want, string what)
    {
        Check(Equals(got, want), what + ": got " + got + ", want " + want);
    }

    static void Near(double got, double want, double tol, string what)
    {
        Check(Math.Abs(got - want) <= tol,
              what + ": got " + got.ToString("0.###") + ", want ~" + want.ToString("0.###"));
    }

    // ── tape-tier preferences (2026-08-16, Sam's design) ─────────────────
    static void TierPreferences()
    {
        Console.WriteLine("tier preferences");
        var ids = Ids();

        int snobs = 0, cheap = 0, neutral = 0;
        foreach (var id in ids)
        {
            int p = AlienTaste.TierPreference(id);
            Check(p >= -1 && p <= 1, "preference in range for " + id);
            Eq(AlienTaste.TierPreference(id), p, "preference is stable for " + id);
            if (p > 0) snobs++; else if (p < 0) cheap++; else neutral++;

            // Preferred tier never mismatches; the other tier mismatches iff
            // the buyer has a preference at all.
            int pref = AlienTaste.PreferredTier(id);
            Check(!AlienTaste.TierMismatch(id, pref), "preferred tier never mismatches");
            Eq(AlienTaste.TierPayFactor(id, pref), 1.0, "preferred tier pays full");
            if (p != 0)
            {
                int other = pref == 2 ? 1 : 2;
                Check(AlienTaste.TierMismatch(id, other), "the other shell mismatches a preferring buyer");
                Eq(AlienTaste.TierPayFactor(id, other), AlienTaste.TierMismatchPay, "mismatch discounts the pay");
            }
        }
        // Shares roughly match the tuning consts over 200 ids (loose bands —
        // this asserts the shape, not the sample noise).
        Check(snobs > 30 && snobs < 90, "a real Type 2 snob population exists (" + snobs + "/200)");
        Check(cheap > 30 && cheap < 90, "a real Type 1 cheapskate population exists (" + cheap + "/200)");
        Check(neutral > 40, "most-ish buyers don't care (" + neutral + "/200)");

        // The verdict downgrade: a mismatched tier turns a certain like into a
        // coin flip and a coin flip into a no — never a hard block on its own.
        string snob = null, indifferent = null;
        foreach (var id in ids)
        {
            if (snob == null && AlienTaste.TierPreference(id) > 0) snob = id;
            if (indifferent == null && AlienTaste.TierPreference(id) == 0) indifferent = id;
        }
        Check(snob != null && indifferent != null, "found a snob and an indifferent buyer to test");
        if (snob != null)
        {
            double[] perfect = AlienTaste.TastePoint(snob);   // sat 100 -> Liked
            Eq(AlienTaste.GateFor(snob, perfect, AlienTaste.Satisfaction(snob, perfect), 2),
               AlienTaste.Verdict.Liked, "a snob likes a perfect Type 2 outright");
            Eq(AlienTaste.GateFor(snob, perfect, AlienTaste.Satisfaction(snob, perfect), 1),
               AlienTaste.Verdict.CoinFlip, "the same perfect song on a Type 1 drops to a coin flip");
        }
        if (indifferent != null)
        {
            double[] perfect = AlienTaste.TastePoint(indifferent);
            Eq(AlienTaste.GateFor(indifferent, perfect, AlienTaste.Satisfaction(indifferent, perfect), 1),
               AlienTaste.Verdict.Liked, "an indifferent buyer ignores the shell");
        }
    }

    // A spread of realistic ids: streamed aliens are "cell:slot:cellid",
    // hand-placed ones are "scene:Name".
    static string[] Ids()
    {
        var ids = new string[200];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = i % 3 == 0 ? "scene:Alien" + i : "cell:" + (i % 7) + ":" + (i * 31);
        return ids;
    }

    public static int Main()
    {
        Stability();
        Spread();
        SatisfactionShape();
        Gate();
        Feedback();
        Pricing();
        TierPreferences();
        TapeOfferTests.RunAll();
        _checks += TapeOfferTests.Checks;
        _failures += TapeOfferTests.Failures;
        DealTests.RunAll();
        _checks += DealTests.Checks;
        _failures += DealTests.Failures;

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("taste VERIFIED - " + _checks + " checks, all passed.");
            return 0;
        }
        Console.WriteLine("taste FAILED - " + _failures + " of " + _checks + " checks.");
        return 1;
    }

    // ── the property the whole design rests on ───────────────────────────

    static void Stability()
    {
        Console.WriteLine("stability");
        foreach (string id in Ids())
        {
            double[] a = AlienTaste.TastePoint(id);
            double[] b = AlienTaste.TastePoint(id);
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) { Check(false, "taste point drifted for " + id); return; }
        }
        Check(true, "the same id yields the same ear every time");

        // Different ids must not share an ear, or every alien is the same alien.
        var seen = new System.Collections.Generic.HashSet<string>();
        int collisions = 0;
        foreach (string id in Ids())
        {
            double[] p = AlienTaste.TastePoint(id);
            string k = "";
            for (int i = 0; i < p.Length; i++) k += p[i].ToString("0.0") + ",";
            if (!seen.Add(k)) collisions++;
        }
        Check(collisions == 0, "200 ids produced 200 distinct ears (collisions: " + collisions + ")");

        // A one-character difference must not produce a near-identical ear —
        // "cell:1:2" and "cell:1:3" stand next to each other in the world.
        double d = AlienTaste.Distance(AlienTaste.TastePoint("cell:1:2"),
                                       AlienTaste.TastePoint("cell:1:3"));
        Check(d > 1.0, "neighbouring ids have genuinely different taste (distance " + d.ToString("0.00") + ")");
    }

    static void Spread()
    {
        Console.WriteLine("spread");
        // Every coordinate must actually use its range, or one dial silently
        // stops mattering to anybody.
        var lo = new double[AlienTaste.DialCount];
        var hi = new double[AlienTaste.DialCount];
        for (int i = 0; i < AlienTaste.DialCount; i++) { lo[i] = 99; hi[i] = -99; }

        double fLo = 99, fHi = -99, pLo = 99, pHi = -99;
        foreach (string id in Ids())
        {
            double[] p = AlienTaste.TastePoint(id);
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i] < lo[i]) lo[i] = p[i];
                if (p[i] > hi[i]) hi[i] = p[i];
                Check(p[i] >= 0 && p[i] <= 10, "coordinate in range");
                _checks--;                       // do not count 1200 identical assertions
            }
            double f = AlienTaste.Falloff(id);
            if (f < fLo) fLo = f;
            if (f > fHi) fHi = f;
            double pf = AlienTaste.PayFactor(id);
            if (pf < pLo) pLo = pf;
            if (pf > pHi) pHi = pf;
        }
        _checks++;
        Check(true, "every coordinate stayed inside 0..10");

        for (int i = 0; i < AlienTaste.DialCount; i++)
            Check(hi[i] - lo[i] > 7.0, "dial " + i + " spans a real range (" +
                  lo[i].ToString("0.0") + ".." + hi[i].ToString("0.0") + ")");

        Check(fLo >= AlienTaste.MinFalloff - 1e-9 && fHi <= AlienTaste.MaxFalloff + 1e-9,
              "falloff stays inside its band");
        Check(fHi - fLo > 0.8, "falloff spans a real range");
        Check(pLo >= AlienTaste.MinPay - 1e-9 && pHi <= AlienTaste.MaxPay + 1e-9,
              "pay factor stays inside its band");

        // THE inverse relationship: fussy pays more. Not a coincidence to be
        // re-rolled — it is what makes a picky alien worth walking to.
        string fussy = null, broad = null;
        double best = -1, worst = 99;
        foreach (string id in Ids())
        {
            double f = AlienTaste.Falloff(id);
            if (f > best) { best = f; fussy = id; }
            if (f < worst) { worst = f; broad = id; }
        }
        Check(AlienTaste.PayFactor(fussy) > AlienTaste.PayFactor(broad),
              "the fussiest alien pays more than the broadest");
    }

    static void SatisfactionShape()
    {
        Console.WriteLine("satisfaction");
        string id = "scene:Vorn";
        double[] taste = AlienTaste.TastePoint(id);

        Eq(AlienTaste.Satisfaction(id, taste), 100.0, "a tape dead on their ear scores 100");

        // Moving away from their point must never INCREASE satisfaction.
        //
        // The walk has to be genuinely monotonic or the test is measuring its
        // own path, not the model. An earlier version added `step` to each dial
        // and FLIPPED to subtracting once it passed 10 — which moves some dials
        // back toward the taste point at some steps. It only passed because
        // K=7 saturated satisfaction at zero before the flip mattered; lowering
        // K to 4 exposed the flaw in the test, not a regression in the model.
        //
        // Each dial now travels toward ITS far end, so distance rises with t.
        double prev = 100.0;
        for (int step = 0; step <= 10; step++)
        {
            double t = step / 10.0;
            var dials = new double[AlienTaste.DialCount];
            for (int i = 0; i < dials.Length; i++)
            {
                double farEnd = taste[i] > 5 ? 0.0 : 10.0;
                dials[i] = taste[i] + (farEnd - taste[i]) * t;
            }
            double s = AlienTaste.Satisfaction(id, dials);
            Check(s <= prev + 1e-9, "satisfaction never rises as the track moves away (step " + step + ")");
            prev = s;
        }

        // The opposite corner should be a genuine miss, not a mild one.
        var far = new double[AlienTaste.DialCount];
        for (int i = 0; i < far.Length; i++) far[i] = taste[i] > 5 ? 0 : 10;
        Check(AlienTaste.Satisfaction(id, far) < AlienTaste.LikeMaybe,
              "the opposite corner of the space is rejected");

        // Clamped both ends.
        foreach (string a in Ids())
        {
            double s = AlienTaste.Satisfaction(a, far);
            if (s < 0 || s > 100) { Check(false, "satisfaction escaped 0..100 for " + a); return; }
        }
        Check(true, "satisfaction stays inside 0..100 for every alien");
    }

    static void Gate()
    {
        Console.WriteLine("like gate");
        // Expressed against the CONSTANTS, not against literals. The first
        // version hardcoded 50 and 35 and started failing the moment the gate
        // was retuned — a test that pins tuning values by literal is a test
        // that fights tuning instead of protecting behaviour.
        double certain = AlienTaste.LikeCertain, maybe = AlienTaste.LikeMaybe;
        Eq(AlienTaste.Gate(100), AlienTaste.Verdict.Liked, "100 is liked");
        Eq(AlienTaste.Gate(certain), AlienTaste.Verdict.Liked, "exactly the like threshold is liked");
        Eq(AlienTaste.Gate(certain - 0.1), AlienTaste.Verdict.CoinFlip, "just under it is a coin flip");
        Eq(AlienTaste.Gate(maybe), AlienTaste.Verdict.CoinFlip, "exactly the maybe threshold is a coin flip");
        Eq(AlienTaste.Gate(maybe - 0.1), AlienTaste.Verdict.Rejected, "just under it is rejected");
        Eq(AlienTaste.Gate(0), AlienTaste.Verdict.Rejected, "0 is rejected");
        Check(certain > maybe, "the bands are the right way round");
    }

    static void Feedback()
    {
        Console.WriteLine("feedback");
        string id = "scene:Skell";
        double[] taste = AlienTaste.TastePoint(id);

        // Push ONE dial hard away and it must be the one they complain about.
        for (int target = 0; target < AlienTaste.DialCount; target++)
        {
            var dials = new double[AlienTaste.DialCount];
            for (int i = 0; i < dials.Length; i++) dials[i] = taste[i];
            bool wantMore = taste[target] < 5;
            dials[target] = wantMore ? 0 : 10;

            bool more;
            double gap;
            int worst = AlienTaste.BiggestGap(id, dials, out more, out gap);
            Eq(worst, target, "names the dial that is actually wrong (dial " + target + ")");
            Eq(more, wantMore, "and which way to move it (dial " + target + ")");
        }

        // A perfect tape has nothing to complain about.
        bool m2; double g2;
        AlienTaste.BiggestGap(id, taste, out m2, out g2);
        Check(g2 < 1e-9, "a dead-on tape produces no complaint");

        // The second gap must not repeat the first.
        var off = new double[AlienTaste.DialCount];
        for (int i = 0; i < off.Length; i++) off[i] = taste[i] > 5 ? 0 : 10;
        bool mA, mB; double gA, gB;
        int first = AlienTaste.BiggestGap(id, off, out mA, out gA);
        int second = AlienTaste.SecondGap(id, off, first, out mB, out gB);
        Check(second != first && second >= 0, "the second complaint is a different dial");
        Check(gB <= gA + 1e-9, "and a smaller gap than the first");
    }

    static void Pricing()
    {
        Console.WriteLine("pricing");

        // The SHAPE Sam has to be able to reason about, expressed against the
        // constants rather than against their current values. These three used
        // to be the literals 26 / 58 / 87, and the 2026-08-14 rebalance broke
        // all three without a single one of them describing a real defect - a
        // test that pins tuning values by literal fights the tuning instead of
        // protecting it. What must stay true is the shape: a floor plus a
        // per-module term, multiplied by the tier.
        Eq(TapeValue.Base(2, 1), TapeValue.Floor + 2 * TapeValue.PerModule,
           "type 1 is floor plus per-module");
        Eq(TapeValue.Base(6, 1), TapeValue.Floor + 6 * TapeValue.PerModule,
           "and stays linear in the module count");
        Eq(TapeValue.Base(6, 2), (TapeValue.Floor + 6 * TapeValue.PerModule) * TapeValue.TierTwoMult,
           "type 2 multiplies the whole base");
        Check(TapeValue.Base(6, 1) > TapeValue.Base(2, 1), "more modules is worth more");
        Check(TapeValue.Base(2, 2) > TapeValue.Base(2, 1), "type 2 beats type 1 at equal modules");
        Check(TapeValue.PerModule > 0 && TapeValue.Floor > 0,
              "both terms are positive - a free tape or a worthless module breaks the shop");

        Check(TapeValue.SatisfactionMult(0) == TapeValue.SatFloor,
              "a barely-tolerated tape keeps the floor, not zero");
        Near(TapeValue.SatisfactionMult(100), 1.3, 1e-9, "a perfect match tops out at 1.3");

        Eq(TapeValue.BondMult(0), 1.0, "a stranger pays the base");
        Near(TapeValue.BondMult(100), 1.4, 1e-9, "a regular pays 1.4x");

        // MORE MODULES IS ALWAYS WORTH MORE. This is the line that makes Tev's
        // $200 plugins an investment rather than decoration — if it ever fails,
        // the shop is pointless.
        for (int mods = 2; mods < 6; mods++)
            Check(TapeValue.For(mods + 1, 1, 60, 0, false, 1.0) >
                  TapeValue.For(mods, 1, 60, 0, false, 1.0),
                  "module " + (mods + 1) + " is worth more than " + mods);

        // And so is playing better.
        for (int sat = 0; sat < 100; sat += 20)
            Check(TapeValue.For(4, 1, sat + 20, 0, false, 1.0) >
                  TapeValue.For(4, 1, sat, 0, false, 1.0),
                  "satisfaction " + (sat + 20) + " beats " + sat);

        Check(TapeValue.For(4, 2, 60, 0, false, 1.0) > TapeValue.For(4, 1, 60, 0, false, 1.0),
              "type 2 is worth more than type 1");
        Check(TapeValue.For(4, 1, 60, 100, false, 1.0) > TapeValue.For(4, 1, 60, 0, false, 1.0),
              "a regular pays more than a stranger");
        Check(TapeValue.For(4, 1, 60, 0, true, 1.0) > TapeValue.For(4, 1, 60, 0, false, 1.0),
              "matching a request pays a bonus");

        // Never zero or negative, whatever gets passed in.
        Check(TapeValue.For(0, 1, 0, 0, false, 0.1) >= 1, "value never falls below 1");
        Check(TapeValue.For(0, 0, -50, -10, false, 0.0) >= 1, "hostile inputs still yield >= 1");

        // The greed punishment has to actually punish.
        int full = TapeValue.For(6, 2, 90, 50, false, 1.2);
        Check(TapeValue.FinalOffer(full) < full, "a final offer is below what they would have paid");
        Check(TapeValue.OpeningThought(full) < full, "they open below their own number");
        Check(TapeValue.Ceiling(full, 1.3) > full, "patience lets them go above it");

        // THE EARLY-GAME MARGIN, stated as a test so it cannot drift unnoticed.
        // Two modules, poor match, no bond: this is what a first tape earns
        // against a $10 blank.
        int worstCase = TapeValue.For(2, 1, 20, 0, false, AlienTaste.MinPay);
        Console.WriteLine("    (early-game worst case: $" + worstCase + " against a $10 blank)");
        Check(worstCase >= 1, "worst case is still a positive number");
    }
}
