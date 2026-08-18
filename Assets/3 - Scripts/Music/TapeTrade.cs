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
    /// <summary>
    /// ONE. A tape is a specific song, so an alien wanting two copies of it
    /// makes no sense — Sam's call, and it is the right one.
    ///
    /// Copies still matter, just not here: you print five of a good song to
    /// sell to five DIFFERENT people, which the per-alien repeat rule is
    /// exactly what forces. Breadth, not depth.
    ///
    /// Kept as a constant rather than inlined so the quantity is one number to
    /// change if a "boxed set" idea ever turns up.
    /// </summary>
    public const int AskQty = 1;

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

    public static int PickAskQty(string id) { return AskQty; }

    /// The cassette tier a contact's order quotes: their preferred shell
    /// (Type 2 snobs ask for Type 2; everyone else asks for the cheap one).
    public static int PickAskTier(string id) { return AlienTaste.PreferredTier(id); }

    /// The format a contact's order asks for: their derived preference,
    /// clamped to what the career has unlocked (no full-length requests while
    /// Tev doesn't even sell the blanks — the ask must always be fillable).
    public static int PickAskKind(string id)
    {
        int pref = AlienTaste.KindPreference(id);
        int unlocked = TapeCareer.UnlockedKind();
        return pref < unlocked ? pref : unlocked;
    }

    // ── what it is worth to them ─────────────────────────────────────────

    /// <summary>
    /// The satisfaction an order is quoted against: a GOOD delivery, not a
    /// flawless one. Quoting at 100 assumed the player would land every dial
    /// dead on their ear, which nobody does. The number itself lives on the
    /// pure quote core (TapeDeal) so the headless suite exercises the real
    /// figure; this forward exists for older call sites.
    /// </summary>
    public const double OrderSatisfaction = TapeDeal.OrderSatisfaction;

    /// <summary>
    /// What this contact genuinely values one tape of <paramref name="genreIndex"/>
    /// at. Built from the SAME formula as an in-person sale, so a price quoted
    /// over text cannot drift from what the alien would have paid face to face.
    ///
    /// ── Why this is priced against YOUR kit ──────────────────────────────
    /// It used to assume a full six-module arrangement at satisfaction 100 and
    /// collect the request bonus on top. Every one of those three is a best
    /// case, and they MULTIPLY: Sam sold a hand-made tape in person for $29 and
    /// was offered $90 over text for the same kind of tape. Measured across 500
    /// aliens the gap was 2.62x, which made walking up to anyone a mistake.
    ///
    /// A commission SHOULD pay a premium — that is what makes the Messages app
    /// worth reading. So the request bonus stays and the fiction goes: the
    /// quote assumes a tape built from the plugins the computer actually owns,
    /// delivered well. That leaves orders paying about 1.35x an in-person sale,
    /// and it ties order income to plugin investment, so Tev's $200 modules pay
    /// for themselves twice.
    /// </summary>
    public static int TruePricePerTape(string id, int genreIndex)
        => TruePricePerTape(id, genreIndex, 1);

    /// Tier-aware quote: thin Unity wrapper over the PURE quote core
    /// (TapeDeal.TruePrice) — this method only supplies the live kit size and
    /// bond. All price maths live in one parity-tested place.
    public static int TruePricePerTape(string id, int genreIndex, int tapeTier)
        => TruePricePerTape(id, genreIndex, tapeTier, TraxKind.Demo);

    public static int TruePricePerTape(string id, int genreIndex, int tapeTier, int kind)
    {
        int bond = BuyerLedger.Get(id) != null ? BuyerLedger.Get(id).bond : 0;
        return TapeDeal.TruePrice(id, tapeTier, kind, TraxLibrary.InstalledCount, bond);
    }

    /// Their opening number. They lowball a little — that gap is what the
    /// player's counter is for.
    public static int OpeningOffer(string id, int genreIndex)
        => OpeningOffer(id, genreIndex, 1);

    public static int OpeningOffer(string id, int genreIndex, int tapeTier)
        => OpeningOffer(id, genreIndex, tapeTier, TraxKind.Demo);

    public static int OpeningOffer(string id, int genreIndex, int tapeTier, int kind)
    {
        int bond = BuyerLedger.Get(id) != null ? BuyerLedger.Get(id).bond : 0;
        return TapeDeal.OpeningOffer(id, tapeTier, kind, TraxLibrary.InstalledCount, bond);
    }

    /// <summary>
    /// Kept at 1: orders are always for a single tape, so there is no quantity
    /// to be short or long on. The mushroom economy needed this because caps
    /// are fungible and a buyer has an appetite; a song has neither.
    /// </summary>
    public static float QtyMood(int wantQty, int offerQty) { return 1f; }

    /// <summary>
    /// The player counters at <paramref name="ask"/> per tape. One exchange
    /// each, no loops — same three outcomes the mushroom flow uses, so the
    /// reply chips and the thread rendering need no new states.
    /// </summary>
    public static BuyerDeals.CounterResult ResolveCounter(string id, int genreIndex, int ask,
                                                          int wantQty, int offerQty,
                                                          out int counterBack)
        => ResolveCounter(id, genreIndex, ask, wantQty, offerQty, 1, out counterBack);

    public static BuyerDeals.CounterResult ResolveCounter(string id, int genreIndex, int ask,
                                                          int wantQty, int offerQty, int tapeTier,
                                                          out int counterBack)
        => ResolveCounter(id, genreIndex, ask, wantQty, offerQty, tapeTier, TraxKind.Demo, out counterBack);

    public static BuyerDeals.CounterResult ResolveCounter(string id, int genreIndex, int ask,
                                                          int wantQty, int offerQty, int tapeTier, int kind,
                                                          out int counterBack)
    {
        counterBack = 0;
        int truePrice = TruePricePerTape(id, genreIndex, tapeTier, kind);
        float patience = (float)AlienTaste.Patience(id);
        float ceiling = truePrice * patience * QtyMood(wantQty, offerQty);

        if (ask <= ceiling) return BuyerDeals.CounterResult.Accept;

        if (ask <= ceiling * 1.25f)
        {
            int opening = OpeningOffer(id, genreIndex, tapeTier, kind);
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
        var g = TraxClassifier.Classify(track.dials);
        string want = GenreName(genreIndex);
        if (g.primary.name == want) return true;
        // A blend counts for either of its names: the console labels a track
        // "Clangin' VOLT" and a CLANG order must accept the tape the computer
        // itself called clangy — the player has no other vocabulary to go by.
        return g.blended && g.secondary.name == want;
    }

    /// Does any SECTION of this pressing fill an order for the genre? A song
    /// with one CLANG stretch IS a CLANG-carrying tape.
    public static bool Fills(TraxSong song, int genreIndex)
    {
        if (song == null) return false;
        for (int i = 0; i < song.sections.Count; i++)
            if (Fills(song.sections[i].track, genreIndex)) return true;
        return false;
    }

    /// Right genre AND at least the ordered shell — the count that pays the
    /// full agreed price (a lower tier delivers, but pro-rata).
    public static int HeldMatchingTier(int genreIndex, int minTapeTier)
    {
        if (Hotbar.Instance == null) return 0;
        int n = 0;
        for (int i = 0; i < Hotbar.TotalSlots; i++)
        {
            Hotbar.Slot slot = Hotbar.Instance.SlotAt(i);
            if (slot.id != Hotbar.ItemId.Cassette || slot.count <= 0) continue;
            TraxPrints.Record rec = TraxPrints.Get(slot.cassetteId);
            if (rec != null && rec.tier >= minTapeTier && Fills(rec.song, genreIndex)) n += slot.count;
        }
        return n;
    }

    /// Right genre, at least the ordered shell AND format — the count that
    /// pays the full agreed price on a kind-aware order.
    public static int HeldMatchingOrder(int genreIndex, int minTapeTier, int minKind)
    {
        if (Hotbar.Instance == null) return 0;
        int n = 0;
        for (int i = 0; i < Hotbar.TotalSlots; i++)
        {
            Hotbar.Slot slot = Hotbar.Instance.SlotAt(i);
            if (slot.id != Hotbar.ItemId.Cassette || slot.count <= 0) continue;
            TraxPrints.Record rec = TraxPrints.Get(slot.cassetteId);
            if (rec != null && rec.tier >= minTapeTier && rec.kind >= minKind
                && Fills(rec.song, genreIndex)) n += slot.count;
        }
        return n;
    }

    // ── named requests (loop-feel D) ─────────────────────────────────────

    /// The genre index a track classifies as — the same primary-label lookup
    /// the sell panel uses, so a named request's contract genre can never
    /// disagree with what the console called the song.
    public static int GenreIndexOf(TraxTrack track)
    {
        if (track == null) return 0;
        string name = TraxClassifier.Classify(track.dials).primary.name;
        var g = TraxClassifier.Genres;
        for (int i = 0; i < g.Length; i++) if (g[i].name == name) return i;
        return 0;
    }

    /// The hex lineage id a named request stores (TraxTrack.TrackId, tier-
    /// independent — "any pressing of that track" is the promise).
    public static string TrackHex(uint trackId) => trackId.ToString("x8");

    /// The live shelf project behind a named request, or null if it was
    /// deleted since (the request then grades as a plain genre order).
    public static TraxLibrary.Record FindProjectByTrackHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        var projects = TraxLibrary.Projects;
        for (int i = 0; i < projects.Count; i++)
            if (projects[i] != null && TrackHex(projects[i].trackId) == hex) return projects[i];
        return null;
    }

    /// Display name for a requested track, for order headers and the
    /// appointment card. Falls back readably if the project is gone.
    public static string RequestTrackName(string hex)
    {
        var rec = FindProjectByTrackHex(hex);
        return rec != null && !string.IsNullOrEmpty(rec.name)
            ? rec.name.ToUpperInvariant() : "THAT TRACK OF YOURS";
    }

    /// Does this delivered pressing match a named request? Lineage by track
    /// id, ANY tier — a Type 2 pressing of the requested song still is the
    /// requested song (tier shortfalls pro-rate separately).
    public static bool MatchesRequest(TraxPrints.Record delivered, string requestHex)
    {
        return delivered != null && !string.IsNullOrEmpty(requestHex)
            && TrackHex(delivered.trackId) == requestHex;
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
            if (rec != null && Fills(rec.song, genreIndex)) n += slot.count;
        }
        return n;
    }
}
