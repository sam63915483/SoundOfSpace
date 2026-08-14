using UnityEngine;

/// <summary>
/// The tape economy's answer to <see cref="BuyerDeals"/> + NPCMushroomPrice:
/// what a contact wants, what they will pay, and how they react to a counter.
///
/// ── This exists so the MESSAGES app can stay the messages app ────────────
/// BuyerLedger, BuyerMessageDirector, BuyerTexts and MessagesScreen are a
/// working, playtested negotiation-over-text system. Almost none of it cares
/// what is being sold — the coupling was a dozen calls into mushroom pricing
/// and a handful of strings saying "caps". Pointing those at this class
/// converts the whole thing to tapes without rewriting the parts that were
/// hard: the convo state machine, the appointment card, the deadlines, the
/// reply chips and the event log.
///
/// ── Field reuse, deliberately ────────────────────────────────────────────
/// A Buyer's `askTier` now holds a GENRE INDEX and `offerPerCap` a price per
/// TAPE. The names are legacy and stay that way on purpose: renaming them
/// would move the save schema for zero gameplay gain, and every one of them is
/// read through the helpers here rather than raw.
/// </summary>
public static class TapeTrade
{
    /// How many tapes a contact asks for at once. Small on purpose — a tape is
    /// a specific song, not a sack of produce, and asking for eight of them
    /// would mean eight tracks the player has to write.
    public const int MinAskQty = 1;
    public const int MaxAskQty = 3;

    // ── vocabulary ───────────────────────────────────────────────────────

    public static string GenreName(int genreIndex)
    {
        var g = TraxClassifier.Genres;
        if (g == null || g.Length == 0) return "MUSIC";
        int i = genreIndex < 0 ? 0 : genreIndex >= g.Length ? g.Length - 1 : genreIndex;
        return g[i].name;
    }

    /// "tape" / "tapes", so the text lines read naturally at any quantity.
    public static string TapeWord(int qty) { return qty == 1 ? "tape" : "tapes"; }

    /// The six dials as a plain array, which is what the taste model speaks.
    public static double[] DialsOf(TraxTrack track)
    {
        var d = new double[AlienTaste.DialCount];
        if (track == null) return d;
        for (int i = 0; i < d.Length && i < TraxPrng.DialCount; i++) d[i] = track.dials.Get(i);
        return d;
    }

    // ── what they want ───────────────────────────────────────────────────

    /// <summary>
    /// The genre a contact asks for. Almost always their favourite — a request
    /// for something they do not like is a lie the taste model cannot back up,
    /// and the player would be punished for filling it exactly as asked.
    /// </summary>
    public static int PickAskGenre(string id)
    {
        return AlienTaste.FavouriteGenreIndex(id);
    }

    public static int PickAskQty(string id)
    {
        // Bigger orders from people who like you, so a regular is worth more
        // per trip rather than merely more often.
        int bond = BuyerLedger.Get(id) != null ? BuyerLedger.Get(id).bond : 0;
        int max = bond >= 60 ? MaxAskQty : bond >= 25 ? 2 : 1;
        return Random.Range(MinAskQty, max + 1);
    }

    // ── what it is worth to them ─────────────────────────────────────────

    /// <summary>
    /// What this contact genuinely values one tape of <paramref name="genreIndex"/>
    /// at. Built from the SAME formula as an in-person sale, so a price quoted
    /// over text cannot drift from what the alien would have paid face to face.
    ///
    /// Assumes a full six-module arrangement at satisfaction 100, because the
    /// order is for their own genre — this is what a GOOD delivery is worth,
    /// and under-delivering is priced down at the handover instead.
    /// </summary>
    public static int TruePricePerTape(string id, int genreIndex)
    {
        int bond = BuyerLedger.Get(id) != null ? BuyerLedger.Get(id).bond : 0;
        return TapeValue.For(6, 1, 100.0, bond, true, AlienTaste.PayFactor(id));
    }

    /// Their opening number. They lowball a little — that gap is what the
    /// player's counter is for.
    public static int OpeningOffer(string id, int genreIndex)
    {
        return Mathf.Max(1, Mathf.RoundToInt(TruePricePerTape(id, genreIndex) * 0.9f));
    }

    /// <summary>
    /// How the quantity the player proposes shifts what they will tolerate
    /// paying, mirroring BuyerDeals.QtyMood so both economies haggle the same
    /// way. Short of the ask: still interested, no premium. Over it: they will
    /// take them, but they cool on it.
    /// </summary>
    public static float QtyMood(int wantQty, int offerQty)
    {
        if (wantQty <= 0 || offerQty == wantQty) return 1f;
        float ratio = (float)offerQty / wantQty;
        if (ratio < 1f) return Mathf.Lerp(0.85f, 1f, ratio);
        return 1f - 0.15f * Mathf.Min(1f, ratio - 1f);
    }

    /// <summary>
    /// The player counters at <paramref name="ask"/> per tape. One exchange
    /// each, no loops — same three outcomes the mushroom flow uses, so the
    /// reply chips and the thread rendering need no new states.
    /// </summary>
    public static BuyerDeals.CounterResult ResolveCounter(string id, int genreIndex, int ask,
                                                          int wantQty, int offerQty,
                                                          out int counterBack)
    {
        counterBack = 0;
        int truePrice = TruePricePerTape(id, genreIndex);
        float patience = (float)AlienTaste.Patience(id);
        float ceiling = truePrice * patience * QtyMood(wantQty, offerQty);

        if (ask <= ceiling) return BuyerDeals.CounterResult.Accept;

        if (ask <= ceiling * 1.25f)
        {
            int opening = OpeningOffer(id, genreIndex);
            counterBack = Mathf.Min(Mathf.RoundToInt((opening + ask) / 2f), Mathf.FloorToInt(ceiling));
            counterBack = Mathf.Max(counterBack, 1);
            return BuyerDeals.CounterResult.CounterBack;
        }

        return BuyerDeals.CounterResult.Refuse;
    }

    /// <summary>
    /// Does this pressing fill an order for <paramref name="genreIndex"/>?
    /// Judged by the CLASSIFIER, so the label the computer showed the player is
    /// the label the order is graded against.
    /// </summary>
    public static bool Fills(TraxTrack track, int genreIndex)
    {
        if (track == null) return false;
        return TraxClassifier.Classify(track.dials).primary.name == GenreName(genreIndex);
    }

    /// How many tapes of the right genre the player is carrying, for the
    /// "deliver order" row and the sell panel's gating.
    public static int HeldMatching(int genreIndex)
    {
        if (Hotbar.Instance == null) return 0;
        int n = 0;
        for (int i = 0; i < Hotbar.TotalSlots; i++)
        {
            Hotbar.Slot slot = Hotbar.Instance.SlotAt(i);
            if (slot.id != Hotbar.ItemId.Cassette || slot.count <= 0) continue;
            TraxPrints.Record rec = TraxPrints.Get(slot.cassetteId);
            if (rec != null && Fills(rec.track, genreIndex)) n += slot.count;
        }
        return n;
    }
}
