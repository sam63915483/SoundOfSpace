// THE PARITY TEST (review C5): across randomized buyers × contract terms,
// delivering exactly-promised goods at an untouched ask must pay EXACTLY the
// number that was quoted and displayed — agreed × gratitude, no clamp, no
// drift. Every promise/grade bug this economy has shipped was two call sites
// computing money independently; this suite makes that regression loud.
//
// Runs headlessly inside verify-taste.py alongside the taste model.

using System;

public static class DealTests
{
    public static int Checks;
    public static int Failures;

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
            ids[i] = i % 3 == 0 ? "scene:Deal" + i : "cell:" + (i % 5) + ":" + (i * 17 + 3);
        return ids;
    }

    static int RoundAway(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    static DealTerms Terms(string id, int price, int tier, int mods, int qty = 1)
        => new DealTerms { buyerId = id, genreIndex = 0, qty = qty, tapeTier = tier,
                           modulesBasis = mods, pricePerTape = price, windowMinutes = 5 };

    public static void RunAll()
    {
        Console.WriteLine("deal parity");
        var ids = Ids();
        double[] gratitudes = { 1.15, 1.10, 1.05 };

        // ── the headline: promised goods at the untouched ask pay EXACTLY
        //    agreed × gratitude, for every buyer, tier, kit and bond ─────────
        int n = 0;
        foreach (var id in ids)
        {
            int tier = (n % 2) + 1;
            int mods = new[] { 1, 2, 4, 6 }[n % 4];
            int bond = (n % 3) * 40;
            double g = gratitudes[n % 3];
            n++;

            int quote = TapeDeal.OpeningOffer(id, tier, mods, bond);
            Check(quote >= 1, "quote is at least a credit for " + id);

            var r = TapeDeal.Grade(Terms(id, quote, tier, mods), mods,
                                   deliveredModules: mods, deliveredTier: tier,
                                   fillsGenre: true, deliveredQty: 1, alreadyHeard: false,
                                   ask: quote, gratitudeMult: g, substituteWorth: 0);
            Check(r.kind == TapeDeal.GradeKind.Pay, "exact delivery is payable for " + id);
            Eq(r.acceptChance, 1.0, "exact delivery at the agreed price is certain for " + id);
            Check(!r.thin && !r.substituted, "nothing is docked on an exact delivery for " + id);
            Eq(r.perCap, RoundAway(quote * g),
               "PARITY: paid == agreed x gratitude for " + id + " t" + tier + " m" + mods);
        }

        // A haggled-up agreed number is honoured just the same — the grader
        // never second-guesses what the buyer put in writing.
        foreach (var id in new[] { ids[1], ids[7], ids[42] })
        {
            int quote = TapeDeal.OpeningOffer(id, 1, 2, 0);
            int agreed = RoundAway(quote * 1.4);
            var r = TapeDeal.Grade(Terms(id, agreed, 1, 2), 2, 2, 1, true, 1, false,
                                   agreed, 1.15, 0);
            Eq(r.perCap, RoundAway(agreed * 1.15), "a haggled price is honoured in full for " + id);
        }

        // ── the objective goods rule ─────────────────────────────────────────
        {
            string id = ids[3];
            int mods = 4;
            int quote = TapeDeal.OpeningOffer(id, 2, mods, 0);

            // Type 1 on a Type 2 contract, same kit: exactly half (Base x2).
            var low = TapeDeal.Grade(Terms(id, quote, 2, mods), mods, mods, 1, true, 1, false,
                                     quote, 1.0, 0);
            Check(low.thin && low.tierShort, "a lower tier is flagged");
            Eq(low.perCap, Math.Max(1, RoundAway(quote * 0.5)), "Type 1 on a Type 2 deal pays exactly half");

            // Thinner kit: pro-rata on Base.
            var thin = TapeDeal.Grade(Terms(id, quote, 2, mods), mods, 2, 2, true, 1, false,
                                      quote, 1.0, 0);
            double ratio = TapeValue.Base(2, 2) / TapeValue.Base(mods, 2);
            Check(thin.thin && !thin.tierShort, "a thin kit is flagged as thin, not tier-short");
            Eq(thin.perCap, Math.Max(1, RoundAway(quote * ratio)), "a thin kit pays pro-rata on Base");

            // Better goods cap at the agreed number — generosity, not a bonus.
            var up = TapeDeal.Grade(Terms(id, quote, 1, 2), 2, 6, 2, true, 1, false,
                                    quote, 1.0, 0);
            Check(!up.thin, "better goods are never docked");
            Eq(up.perCap, quote, "better goods still pay the agreed number, no more");
        }

        // ── freshness and the overcharge gamble ──────────────────────────────
        {
            string id = ids[9];
            int quote = TapeDeal.OpeningOffer(id, 1, 2, 0);
            var heard = TapeDeal.Grade(Terms(id, quote, 1, 2), 2, 2, 1, true, 1, true,
                                       quote, 1.0, 0);
            Eq(heard.kind, TapeDeal.GradeKind.RefusedHeard, "an already-heard tape is refused, no roll");

            double c10 = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 22, 1.0, 0).acceptChance;
            double c25 = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 25, 1.0, 0).acceptChance;
            double c50 = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 30, 1.0, 0).acceptChance;
            Check(Math.Abs(c10 - 0.8) < 0.011, "+10% over the agreed price -> ~0.8 (" + c10 + ")");
            Check(Math.Abs(c25 - 0.5) < 0.011, "+25% -> ~0.5 (" + c25 + ")");
            Check(Math.Abs(c50 - 0.05) < 0.001, "+50% -> the 5% floor (" + c50 + ")");
            Check(c10 > c25 && c25 > c50, "overcharge odds fall monotonically");

            // An over-ask that LANDS pays the ask (the gamble's upside)…
            var overWin = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 24, 1.15, 0);
            Eq(overWin.perCap, 24, "a landed over-ask pays the ask");
            Check(overWin.substituted, "…but counts as a deviation (no gratitude, halved-bond class)");
        }

        // ── wrong goods keep the worth clamp ─────────────────────────────────
        {
            string id = ids[12];
            var sub = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, false, 1, false,
                                     20, 1.0, substituteWorth: 9);
            Check(Math.Abs(sub.acceptChance - TapeDeal.SubstitutionChance) < 1e-9,
                  "wrong goods at the agreed price run the flat substitution gamble");
            Eq(sub.perCap, 9, "wrong goods pay at most what the tape is worth to them");
            Check(sub.thin && sub.substituted, "a clamped substitute is flagged");
        }

        // ── tier preference shapes the quote itself ──────────────────────────
        foreach (var id in ids)
        {
            int t1 = TapeDeal.OpeningOffer(id, 1, 2, 0);
            int t2 = TapeDeal.OpeningOffer(id, 2, 2, 0);
            Check(t2 > t1, "a Type 2 always quotes above a Type 1 for " + id);
            if (AlienTaste.TierPreference(id) > 0)
                Check(t2 >= RoundAway(t1 * 2.0 * 0.9),
                      "a snob's Type 2 quote carries the full premium for " + id);
        }

        // ── the walk-up rulebook honours YOUR price up to the ceiling ────────
        foreach (var id in new[] { ids[0], ids[5], ids[11], ids[23] })
        {
            int value = TapeOffer.Value(id, 2, 1, 70.0, false, 0);
            int ceiling = TapeOffer.Ceiling(id, value);
            Check(ceiling >= value, "ceiling sits at or above value for " + id);
            var resp = TapeOffer.Judge(id, value, ceiling, out int at);
            Eq(resp, TapeOffer.Response.Accepted, "an ask at the ceiling is accepted for " + id);
            Eq(at, ceiling, "…at YOUR price, not theirs, for " + id);
        }

        Console.WriteLine(Failures == 0 ? "  parity holds" : "  PARITY BROKEN");
    }
}
