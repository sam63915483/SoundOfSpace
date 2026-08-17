// THE PARITY TEST (review C5; strengthened by loop-feel E): across randomized
// buyers × contract terms, delivering exactly-promised goods at an untouched
// ask must pay EXACTLY the agreed number — the figure every surface displayed.
// No multiplier, no clamp, no drift. Every promise/grade bug this economy has
// shipped was two call sites computing money independently; this suite makes
// that regression loud.
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

        // ── the headline (loop-feel E, simpler and STRONGER): promised goods
        //    at the untouched ask pay EXACTLY the agreed number — the figure
        //    every surface displayed. No multiplier, no clamp, no fine print,
        //    for every buyer, tier, kit and bond. ──────────────────────────
        int n = 0;
        foreach (var id in ids)
        {
            int tier = (n % 2) + 1;
            int mods = new[] { 1, 2, 4, 6 }[n % 4];
            int bond = (n % 3) * 40;
            n++;

            int quote = TapeDeal.OpeningOffer(id, tier, mods, bond);
            Check(quote >= 1, "quote is at least a credit for " + id);

            var r = TapeDeal.Grade(Terms(id, quote, tier, mods), mods,
                                   deliveredModules: mods, deliveredTier: tier,
                                   fillsGenre: true, deliveredQty: 1, alreadyHeard: false,
                                   ask: quote, substituteWorth: 0);
            Check(r.kind == TapeDeal.GradeKind.Pay, "exact delivery is payable for " + id);
            Eq(r.acceptChance, 1.0, "exact delivery at the agreed price is certain for " + id);
            Check(!r.thin && !r.substituted, "nothing is docked on an exact delivery for " + id);
            Eq(r.perCap, quote,
               "PARITY: paid == agreed, exactly, for " + id + " t" + tier + " m" + mods);
        }

        // A haggled-up agreed number is honoured just the same — the grader
        // never second-guesses what the buyer put in writing.
        foreach (var id in new[] { ids[1], ids[7], ids[42] })
        {
            int quote = TapeDeal.OpeningOffer(id, 1, 2, 0);
            int agreed = RoundAway(quote * 1.4);
            var r = TapeDeal.Grade(Terms(id, agreed, 1, 2), 2, 2, 1, true, 1, false,
                                   agreed, 0);
            Eq(r.perCap, agreed, "a haggled price is honoured in full for " + id);
        }

        // ── the objective goods rule ─────────────────────────────────────────
        {
            string id = ids[3];
            int mods = 4;
            int quote = TapeDeal.OpeningOffer(id, 2, mods, 0);

            // Type 1 on a Type 2 contract, same kit: exactly half (Base x2).
            var low = TapeDeal.Grade(Terms(id, quote, 2, mods), mods, mods, 1, true, 1, false,
                                     quote, 0);
            Check(low.thin && low.tierShort, "a lower tier is flagged");
            Eq(low.perCap, Math.Max(1, RoundAway(quote * 0.5)), "Type 1 on a Type 2 deal pays exactly half");

            // Thinner kit: pro-rata on Base.
            var thin = TapeDeal.Grade(Terms(id, quote, 2, mods), mods, 2, 2, true, 1, false,
                                      quote, 0);
            double ratio = TapeValue.Base(2, 2) / TapeValue.Base(mods, 2);
            Check(thin.thin && !thin.tierShort, "a thin kit is flagged as thin, not tier-short");
            Eq(thin.perCap, Math.Max(1, RoundAway(quote * ratio)), "a thin kit pays pro-rata on Base");

            // Better goods cap at the agreed number — generosity, not a bonus.
            var up = TapeDeal.Grade(Terms(id, quote, 1, 2), 2, 6, 2, true, 1, false,
                                    quote, 0);
            Check(!up.thin, "better goods are never docked");
            Eq(up.perCap, quote, "better goods still pay the agreed number, no more");
        }

        // ── freshness and the overcharge gamble ──────────────────────────────
        {
            string id = ids[9];
            int quote = TapeDeal.OpeningOffer(id, 1, 2, 0);
            var heard = TapeDeal.Grade(Terms(id, quote, 1, 2), 2, 2, 1, true, 1, true,
                                       quote, 0);
            Eq(heard.kind, TapeDeal.GradeKind.RefusedHeard, "an already-heard tape is refused, no roll");

            double c10 = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 22, 0).acceptChance;
            double c25 = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 25, 0).acceptChance;
            double c50 = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 30, 0).acceptChance;
            Check(Math.Abs(c10 - 0.8) < 0.011, "+10% over the agreed price -> ~0.8 (" + c10 + ")");
            Check(Math.Abs(c25 - 0.5) < 0.011, "+25% -> ~0.5 (" + c25 + ")");
            Check(Math.Abs(c50 - 0.05) < 0.001, "+50% -> the 5% floor (" + c50 + ")");
            Check(c10 > c25 && c25 > c50, "overcharge odds fall monotonically");

            // An over-ask that LANDS pays the ask (the gamble's upside)…
            var overWin = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, true, 1, false, 24, 0);
            Eq(overWin.perCap, 24, "a landed over-ask pays the ask");
            Check(overWin.substituted, "…but counts as a deviation (no gratitude, halved-bond class)");
        }

        // ── wrong goods keep the worth clamp ─────────────────────────────────
        {
            string id = ids[12];
            var sub = TapeDeal.Grade(Terms(id, 20, 1, 2), 2, 2, 1, false, 1, false,
                                     20, substituteWorth: 9);
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

        // ── the satisfaction word ladder (loop-feel A1) ──────────────────────
        // One vocabulary, band edges pinned to the taste gates so the word can
        // never disagree with the verdict.
        {
            Eq(AlienFeedback.SatBand(0), 0, "sat 0 is junk");
            Eq(AlienFeedback.SatBand(41.99), 0, "just under the flip gate is junk");
            Eq(AlienFeedback.SatBand(42), 1, "the flip gate opens not-for-me");
            Eq(AlienFeedback.SatBand(59.99), 1, "just under liked stays not-for-me");
            Eq(AlienFeedback.SatBand(60), 2, "the like gate opens decent");
            Eq(AlienFeedback.SatBand(77.99), 2, "just under love stays decent");
            Eq(AlienFeedback.SatBand(78), 3, "78 opens love-it");
            Eq(AlienFeedback.SatBand(91.99), 3, "just under peak stays love-it");
            Eq(AlienFeedback.SatBand(92), 4, "92 opens MASTERPIECE");
            Eq(AlienFeedback.SatBand(100), 4, "100 is MASTERPIECE");
            for (int b = 0; b < 5; b++)
                Check(!string.IsNullOrEmpty(AlienFeedback.SatWord(b)), "ladder word " + b + " exists");

            // The hard rule for the whole pass: no player-visible percentages
            // or multipliers on any spoken selling line.
            for (uint v = 0; v < 6; v++)
            {
                foreach (double s in new[] { 10.0, 50.0, 70.0, 85.0, 95.0 })
                {
                    Check(!AlienFeedback.ForLiked(s, v).Contains("%"), "ForLiked speaks no percentages");
                    Check(!AlienFeedback.AfterListen(s, v).Contains("%"), "AfterListen speaks no percentages");
                }
                Check(!string.IsNullOrEmpty(AlienFeedback.AfterListen(70, v)), "AfterListen produces a line");
                Check(!string.IsNullOrEmpty(AlienFeedback.ForThinKit(v)), "ForThinKit produces a line");
                Check(!AlienFeedback.ForThinKit(v).Contains("%"), "ForThinKit speaks no percentages");
                Check(!string.IsNullOrEmpty(AlienFeedback.ForLiked(50, true, v)),
                      "a won coin flip still gets a spoken line");
            }
            Check(AlienFeedback.ForLiked(50, true, 0).ToLowerInvariant().Contains("not"),
                  "a won flip speaks its true feeling, not fake enthusiasm");
        }

        // ── craving: the flywheel's arithmetic (loop-feel C) ─────────────────
        {
            Eq(CravingRules.Gain(4, false), CravingRules.GainMasterpiece, "masterpiece feeds hardest");
            Eq(CravingRules.Gain(3, false), CravingRules.GainLove, "love-it feeds");
            Eq(CravingRules.Gain(2, false), CravingRules.GainDecent, "decent feeds a little");
            Eq(CravingRules.Gain(1, false), CravingRules.GainBelow, "a tolerated sale still feeds");
            Eq(CravingRules.Gain(0, false), CravingRules.GainBelow, "junk-band sale feeds the floor amount");
            Eq(CravingRules.Gain(2, true), CravingRules.GainDecent + CravingRules.GainNamedRequest,
               "a named request adds its bump");

            Eq(CravingRules.AfterIdleDay(50), 50 - CravingRules.DecayPerIdleDay, "idle day decays");
            Eq(CravingRules.AfterIdleDay(5), 0, "decay floors at zero");
            Eq(CravingRules.Clamp(120), CravingRules.Cap, "craving caps at 100");
            Eq(CravingRules.Clamp(-3), 0, "craving floors at 0");

            Check(Math.Abs(CravingRules.FrequencyMult(0) - 1.0) < 1e-9, "cold buyer texts at base pace");
            Check(Math.Abs(CravingRules.FrequencyMult(100) - 2.5) < 1e-9, "obsessed buyer texts 2.5x as often");
            Check(CravingRules.FrequencyMult(60) > CravingRules.FrequencyMult(30),
                  "frequency rises with craving");

            Eq(CravingRules.LadderBand(0), 0, "0 is curious");
            Eq(CravingRules.LadderBand(19), 0, "19 is curious");
            Eq(CravingRules.LadderBand(20), 1, "20 is interested");
            Eq(CravingRules.LadderBand(59), 1, "59 is interested");
            Eq(CravingRules.LadderBand(60), 2, "60 is hooked (ambush gate)");
            Eq(CravingRules.LadderBand(89), 2, "89 is hooked");
            Eq(CravingRules.LadderBand(90), 3, "90 is obsessed (daily-order gate)");
            for (int c = 0; c <= 100; c += 10)
                Check(!string.IsNullOrEmpty(CravingRules.LadderWord(c)), "craving word exists at " + c);

            Check(CravingRules.AmbushEligible(60, 1, 2), "hooked + a day ignored = ambush");
            Check(!CravingRules.AmbushEligible(59, 1, 2), "under the gate never ambushes");
            Check(!CravingRules.AmbushEligible(80, 2, 2), "bought today = no ambush");
            Check(!CravingRules.AmbushEligible(80, 0, 5), "never bought = no ambush");
        }

        // ── the day wrap composes honestly (loop-feel B) ─────────────────────
        {
            string t = DayRecap.Compose(3, 4, 63, 20, 3, false, "Krib, Vess", "Krib");
            Check(t.Contains("DAY 3 WRAP"), "wrap names its day");
            Check(t.Contains("sold 4 tapes") && t.Contains("$63"), "wrap reports sales and money");
            Check(t.Contains("owes $20") && t.Contains("3 days to plugin lockout"), "wrap reports arrears");
            Check(t.Contains("Krib, Vess"), "wrap names who warmed");
            Check(t.Contains("asking around for more"), "wrap names the hungry");
            Check(!t.Contains("%"), "the wrap speaks no percentages");

            string paid = DayRecap.Compose(1, 1, 8, 0, 0, false, "", "");
            Check(paid.Contains("sold 1 tape ") || paid.Contains("sold 1 tape—")
                  || paid.Contains("sold 1 tape —"), "singular tape reads right");
            Check(paid.Contains("rent: paid up"), "clean rent reads as paid up");
            Check(!paid.Contains("warmer") && !paid.Contains("asking around"),
                  "empty sections are omitted");

            string locked = DayRecap.Compose(9, 0, 0, 50, 0, true, "", "");
            Check(locked.Contains("no tapes sold today"), "a dry day says so");
            Check(locked.Contains("CLOSED"), "the lockout is loud");

            string preRent = DayRecap.Compose(1, 2, 12, -1, 0, false, "", "");
            Check(!preRent.Contains("rent"), "no rent arrangement, no rent line");
        }

        // ── word-of-mouth source data (loop-feel D) ──────────────────────────
        // The eligibility query's pure half: sold-to-someone-else, by track
        // lineage, surviving a save round-trip. (Unheard-by-this-buyer is the
        // existing HasHeard, tested above.)
        {
            TapeMemory.Clear();
            TapeMemory.RememberBought("cell:1:10", 0xABCu);
            TapeMemory.RememberBought("cell:1:10", 0xABCu);   // dedup
            Check(TapeMemory.HasBought("cell:1:10", 0xABCu), "a purchase is remembered by lineage");
            Check(!TapeMemory.HasBought("cell:1:11", 0xABCu), "only the buyer who bought it owns it");
            Check(!TapeMemory.HasBought("cell:1:10", 0xDEFu), "other tracks aren't owned");

            string owner;
            Check(TapeMemory.AnyoneElseBought(0xABCu, "cell:1:11", out owner) && owner == "cell:1:10",
                  "word of mouth finds the owning gossiper");
            Check(!TapeMemory.AnyoneElseBought(0xABCu, "cell:1:10", out _),
                  "the buyer's own purchase is not word of mouth to them");
            Check(!TapeMemory.AnyoneElseBought(0u, "cell:1:11", out _), "lineage 0 never matches");

            var round = TapeMemory.Capture();
            TapeMemory.Clear();
            Check(!TapeMemory.HasBought("cell:1:10", 0xABCu), "clear clears");
            TapeMemory.Apply(round);
            Check(TapeMemory.HasBought("cell:1:10", 0xABCu), "bought lineage survives save round-trip");

            var legacy = new TapeMemorySave();          // pre-feature save shape
            legacy.ids.Add("cell:2:20");
            legacy.bond.Add(0); legacy.contact.Add(false); legacy.heardCounts.Add(0);
            TapeMemory.Apply(legacy);
            Check(!TapeMemory.HasBought("cell:2:20", 0xABCu), "an old save loads clean with no bought data");
            TapeMemory.Clear();
        }

        Console.WriteLine(Failures == 0 ? "  parity holds" : "  PARITY BROKEN");
    }
}
