/// <summary>
/// THE DEAL SLIP, and the one grader that reads it.
///
/// ── Why this exists (review C5, 2026-08-16) ──────────────────────────────
/// Every promise/grade bug this economy has shipped — the label that didn't
/// count, the slider that betrayed the agreed price, the haggle that could
/// never be paid — was the same event: two call sites computing money
/// independently and drifting apart. The cure is structural, not vigilance:
///
///   RULE 1 — a text order's terms live on ONE object (this one), built from
///            the appointment; every surface renders projections of it.
///   RULE 2 — delivery money is computed by Grade(terms, delivered) and
///            NOTHING else. No surface re-derives a price at delivery time.
///
/// The parity test (test/DealTests.cs) holds the contract: across randomized
/// buyers × terms, delivering exactly-promised goods at an untouched ask pays
/// EXACTLY the agreed number — the figure every surface displayed (loop-feel
/// E: the old gratitude multiplier left the money path; on-time pays in bond).
///
/// Walk-up sales have their own single rulebook: TapeOffer (Listen / Value /
/// Judge), which the sell panel routes through as of the same date.
///
/// PURE. No Unity types, no clock, no randomness (the caller rolls
/// acceptChance) — runs headlessly with the rest of the taste model.
/// </summary>
public class DealTerms
{
    public string buyerId;
    public int genreIndex;      // classifier genre the order is for
    public int qty;             // tapes wanted (1 today)
    public int tapeTier;        // cassette tier agreed; 0 = legacy save, treat as 1
    public int modulesBasis;    // plugin count the quote priced; 0 = legacy, use fallback
    public int pricePerTape;    // the agreed number — THE contract price
    public int windowMinutes;   // promised meetup window
}

public static class TapeDeal
{
    /// The satisfaction an order is quoted against: a GOOD delivery, not a
    /// flawless one (see TapeTrade's history note — quoting at 100 made
    /// walk-ups a mistake).
    public const double OrderSatisfaction = 85.0;

    /// Buyers open at 90% of their true number; the gap is what countering
    /// is for.
    public const double OpeningLowball = 0.9;

    /// Wrong-goods substitutions run this flat gamble before the overcharge
    /// factor (spec §5c).
    public const double SubstitutionChance = 0.45;

    // ── the quote ────────────────────────────────────────────────────────

    /// What this buyer genuinely values one tape of this tier at, priced
    /// against the given kit. Same formula as an in-person sale (TapeOffer
    /// .Value at OrderSatisfaction with the request bonus), so a texted
    /// number can never drift from a face-to-face one.
    public static int TruePrice(string buyerId, int tapeTier, int installedModules, int bond)
    {
        int mods = installedModules < 1 ? 1 : installedModules;
        return TapeValue.For(mods, tapeTier, OrderSatisfaction, bond, true,
                             AlienTaste.PayFactor(buyerId)
                             * AlienTaste.TierPayFactor(buyerId, tapeTier));
    }

    public static int OpeningOffer(string buyerId, int tapeTier, int installedModules, int bond)
    {
        int p = (int)System.Math.Round(TruePrice(buyerId, tapeTier, installedModules, bond) * OpeningLowball,
                                       System.MidpointRounding.AwayFromZero);
        return p < 1 ? 1 : p;
    }

    // ── the grade ────────────────────────────────────────────────────────

    /// Re-asking ABOVE the agreed price at the meetup (Sam's rule: agree 20,
    /// show up and ask 30 — they may take it, they may not). At or under: no
    /// penalty. Over: acceptance falls off linearly, +50% or more → 5% floor.
    /// (BuyerDeals.OverchargeFactor is the mushroom twin; this copy exists so
    /// the tape grader stays Unity-free and parity-testable.)
    public static double Overcharge(int ask, int agreed)
    {
        if (agreed <= 0 || ask <= agreed) return 1.0;
        double over = (double)ask / agreed - 1.0;
        double f = 1.0 - over / 0.5;
        return f < 0.05 ? 0.05 : f > 1.0 ? 1.0 : f;
    }

    public enum GradeKind
    {
        RefusedHeard,   // they've been played this song — no roll, deal stays open
        Pay,            // roll acceptChance; on success pay perCap × qty
    }

    public struct GradeResult
    {
        public GradeKind kind;
        public double acceptChance;   // 1.0 = certain; caller rolls otherwise
        public int perCap;
        public int qty;
        public bool substituted;      // goods or price deviated from the terms
        public bool thin;             // paid under the asked number (shortfall)
        public bool tierShort;        // specifically: delivered a lower tier
    }

    /// <summary>
    /// THE one place delivery money is decided.
    ///
    /// Exact goods (right genre — or the named track, on a named request —
    /// and enough tapes): THE AGREED NUMBER IS THE PAID NUMBER (loop-feel E,
    /// Sam's GO). The agreed price is sacred against taste — the buyer
    /// commissioned sight-unseen and that risk is theirs — and no multiplier
    /// touches it any more: the old +15/10/5% gratitude bump left the money
    /// path entirely (on-time delivery pays in RELATIONSHIP: the existing +4
    /// kept-appointment bond and a thanks line). Money scales ONLY on the
    /// objective goods ratio Base(deliveredMods, deliveredTier) /
    /// Base(contractMods, contractTier): a Type 1 on a Type 2 deal at the
    /// same kit is exactly half; a thinner arrangement pays pro-rata; BETTER
    /// goods cap at 1 (the player's generosity).
    ///
    /// Wrong goods: the agreed number never covered this tape, so they honour
    /// it only up to <paramref name="substituteWorth"/> — what it is
    /// genuinely worth to them (full value formula, caller-computed because
    /// it needs the live satisfaction).
    /// </summary>
    public static GradeResult Grade(DealTerms terms, int contractModsFallback,
                                    int deliveredModules, int deliveredTier,
                                    bool fillsGenre, int deliveredQty, bool alreadyHeard,
                                    int ask, int substituteWorth)
    {
        var r = new GradeResult();
        if (alreadyHeard) { r.kind = GradeKind.RefusedHeard; return r; }

        r.kind = GradeKind.Pay;
        int agreed = terms.pricePerTape < 1 ? 1 : terms.pricePerTape;
        if (ask < 1) ask = 1;

        bool exactGoods = fillsGenre && deliveredQty >= terms.qty;
        bool exactPrice = ask <= agreed;
        r.acceptChance = (exactGoods ? 1.0 : SubstitutionChance) * Overcharge(ask, agreed);
        r.substituted = !(exactGoods && exactPrice);

        if (exactGoods)
        {
            r.perCap = ask;   // your number — untouched, lowered or pushed
            r.qty = deliveredQty < terms.qty ? deliveredQty : terms.qty;

            int contractTier = terms.tapeTier >= 1 ? terms.tapeTier : 1;
            int contractMods = terms.modulesBasis >= 1 ? terms.modulesBasis
                             : (contractModsFallback < 1 ? 1 : contractModsFallback);
            double contractGoods = TapeValue.Base(contractMods, contractTier);
            double deliveredGoods = TapeValue.Base(deliveredModules, deliveredTier);
            if (contractGoods > 0 && deliveredGoods < contractGoods)
            {
                r.perCap = (int)System.Math.Round(r.perCap * (deliveredGoods / contractGoods),
                                                  System.MidpointRounding.AwayFromZero);
                if (r.perCap < 1) r.perCap = 1;
                r.thin = true;
                r.tierShort = deliveredTier < contractTier;
            }
        }
        else
        {
            r.perCap = ask;
            r.qty = deliveredQty;
            if (r.perCap > substituteWorth)
            {
                r.perCap = substituteWorth < 1 ? 1 : substituteWorth;
                r.thin = true;
            }
        }
        return r;
    }
}
