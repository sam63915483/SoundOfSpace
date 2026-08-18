using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PERSISTENT per-buyer memory for the messages/repeat-business loop — the
/// saved counterpart to session-only <see cref="MushroomDealState"/>. Keyed by
/// AlienIdentity, same as everything else about a buyer.
///
/// Owns: bond (0–100), deals completed (drives hidden-want reveals), regular
/// status, the open conversation/appointment state machine, and a bounded
/// event log that message threads are RENDERED from (events, not strings — so
/// wording can improve without stale text in saves).
///
/// Timing rules: all timestamps are Time.unscaledTime within a session and
/// are saved RELATIVE (seconds-ago / seconds-remaining), re-anchored on load.
///
/// Spec: docs/superpowers/specs/2026-08-07-messages-app-design.md
/// </summary>
public static class BuyerLedger
{
    // ── Bond tuning (spec §2) ──────────────────────────────────────────────
    public const int BondPerDeal = 8;
    public const int BondKeptAppointment = 4;   // extra
    public const int BondFavouriteTier = 4;     // extra
    public const int BondCounterRefused = 10;   // loss
    public const int BondSubRefused = 5;        // loss
    public const float BondMaxPayBonus = 0.15f; // +15% at bond 100

    public const int MaxEventsPerBuyer = 40;
    public const int RevealCap = 5;

    // PriceAgreed: the buyer accepted the player's counter — the price is
    // LOCKED and only the window pick remains. Without this state the chips
    // fell back to Accept/Counter/Decline and the player could counter off
    // their own accepted number, climbing the price one grudging "fine" at a
    // time (Sam hit this in play 2026-08-07).
    public enum Convo { None = 0, AwaitingReply = 1, AwaitingCounterBack = 2, Scheduled = 3, PriceAgreed = 4 }

    public enum EvType
    {
        WantText = 0,        // a: offerPerCap, b: qty, tier
        PlayerAccepted = 1,  // a: windowMinutes
        PlayerCountered = 2, // a: counterPerCap
        BuyerCounterBack = 3,// a: counterBackPerCap (b: 1 = grudging acceptance flavor)
        BuyerRefused = 4,    // (outrageous counter — deal off, bond ding)
        PlayerDeclined = 5,  // ("not now", or declining a counter-back)
        Scheduled = 6,       // a: agreedPerCap, b: qty, tier (window in PlayerAccepted)
        FulfilledExact = 7,  // a: paidPerCap, b: qty
        FulfilledSub = 8,    // a: paidPerCap, b: qty, tier: what they actually took
        SubRefused = 9,      // a: rolled chance 0-100
        Missed = 10,         // (negative text renders from this)
        WalkUpDeal = 11,     // a: paidPerCap, b: qty, tier — non-scheduled sale
        DayRecap = 12,       // a: tapesSold, b: earned, tier: day — text in s
        NamedRequest = 13,   // a: offerPerCap, b: qty, tier: genre, c: cassette tier — s: "trackId|TRACK NAME|GOSSIPER"
    }

    public class Ev
    {
        public int type;
        public float at;      // Time.unscaledTime when it happened
        public int a, b, tier;
        // Fourth slot (2026-08-16): the CASSETTE tier (1/2) on order events —
        // `tier` was already taken by the genre index (legacy name). 0 on
        // events from before the field existed; renderers treat 0 as "unknown,
        // say nothing".
        public int c;
        // Fifth slot (2026-08-17 loop-feel): frozen text for snapshot events
        // (the day wrap composes once and must not re-render against live
        // state) and the track/gossiper payload on named requests. ""/null on
        // every event that predates the field.
        public string s;
        // Sixth slot (2026-08-18 tape formats): the tape FORMAT + 1 on order
        // and deal events (0 = pre-feature event — renderers say nothing,
        // exactly the `c` convention).
        public int k;
    }

    public class Buyer
    {
        public string id;
        public int bond;
        public int dealsCompleted;
        public bool isRegular;
        public int unread;
        public List<Ev> events = new List<Ev>();

        // Open conversation / appointment. Only meaningful while convo != None.
        public Convo convo;
        public int askTier;
        public int askQty;
        public int offerPerCap;       // their live offer; the AGREED price once Scheduled
        public int counterBackPerCap; // only while AwaitingCounterBack
        public int windowMinutes;     // 5 / 10 / 15 once Scheduled
        public float deadline;        // unscaledTime; Scheduled only (grace added by the director)
        public float nextTextAt;      // director pacing: earliest next want-text

        // ── Contract terms (2026-08-16, appended for save-order stability) ──
        // The cassette tier the order is FOR (1 or 2). 0 on old saves = treat
        // as 1. Part of the goods spec — a Type 1 delivered on a Type 2 deal
        // pays pro-rata (about half), never a surprise refusal.
        public int askTapeTier;
        // How many plugins the quote was priced against (TraxLibrary
        // .InstalledCount at quote time). The other objective contract term:
        // a 2-module sketch delivered on a 4-plugin quote pays pro-rata.
        // 0 on old saves = fall back to the live InstalledCount.
        public int modulesBasis;

        // ── Craving (loop-feel C, 2026-08-17, appended for save order) ──
        // 0..100 demand stat: feeds on good sales, decays when ignored.
        // NEVER touches price or gates a sale — it drives how often this
        // buyer texts and whether they come find the player. 0 on old saves.
        public int craving;
        // GalaxyTime day of their last completed purchase (0 = never) —
        // drives the no-purchase-today decay and ambush eligibility.
        public int lastPurchaseDay;
        // The trackId of an open NAMED request (loop-feel D), "" when the
        // open order is a plain genre want. Saved: dropping it on reload
        // would quietly turn "bring me GORP SLIME" into "bring me a VOLT",
        // and displayed promises must never drift from what's graded.
        public string requestTrackId;

        // ── Tape formats (2026-08-18, appended for save order) ──
        // The FORMAT the open order asks for (TraxKind). 0 on old saves = Demo.
        public int askKind;
        // Completed deals that were Half/Full songs — drives the "grown from
        // demos" moment (first liked song after >=3 prior deals).
        public int songsBought;
    }

    static readonly Dictionary<string, Buyer> _buyers = new Dictionary<string, Buyer>();
    static float Now => Time.unscaledTime;

    /// <summary>
    /// Bumped whenever anything a second player would need to see changes.
    /// EconomySync watches it instead of hooking each mutator, so a path that
    /// forgets to announce itself cannot exist.
    ///
    /// Every conversation change in BuyerMessageDirector writes buyer fields
    /// DIRECTLY (b.convo, b.offerPerCap, …) rather than through a method here —
    /// but each one is always accompanied by a Log() call, so bumping there
    /// catches the lot. `nextTextAt` moves without a Log and deliberately does
    /// not count: it is host-side pacing that no guest renders.
    /// </summary>
    public static int Version { get; private set; }
    public static void Touch() => Version++;

    // Story NPCs never become regulars — their threads belong to story
    // systems. system: is the day-wrap pseudo-thread (loop-feel B).
    static readonly string[] ExcludedIdPrefixes = { "scene:Tev", "scene:Kolb", "system:" };

    /// The day-wrap pseudo-buyer: a thread with no alien behind it, so the
    /// recap rides the existing event/save/snapshot machinery for free.
    public const string WrapThreadId = "system:wrap";

    // ── Today's running totals (loop-feel B) ──────────────────────────────
    // World-scoped like everything else here: saved on BuyerLedgerSave and
    // carried by the economy snapshot. Reset at each day tick by the recap.
    public static int DayTapesSold { get; private set; }
    public static int DayEarned { get; private set; }
    static readonly List<string> _dayBondUps = new List<string>();
    public static IReadOnlyList<string> DayBondUps => _dayBondUps;

    public static void ResetDayTotals()
    {
        DayTapesSold = 0;
        DayEarned = 0;
        _dayBondUps.Clear();
        Touch();
    }

    static void CountDaySale(string id, int price, int qty, int bondGain)
    {
        DayTapesSold += qty;
        DayEarned += price * qty;
        if (bondGain > 0 && !_dayBondUps.Contains(id)) _dayBondUps.Add(id);
    }

    public static bool Eligible(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < ExcludedIdPrefixes.Length; i++)
            if (id.StartsWith(ExcludedIdPrefixes[i])) return false;
        return true;
    }

    public static Buyer Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        _buyers.TryGetValue(id, out var b);
        return b;
    }

    public static Buyer GetOrCreate(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!_buyers.TryGetValue(id, out var b))
        {
            b = new Buyer { id = id };
            _buyers[id] = b;
        }
        return b;
    }

    /// Every buyer that has ANY ledger state (drives the Messages index).
    public static IEnumerable<Buyer> All() => _buyers.Values;

    public static int TotalUnread()
    {
        int n = 0;
        foreach (var b in _buyers.Values) n += b.unread;
        return n;
    }

    // ── Bond ───────────────────────────────────────────────────────────────

    /// 1.0 at bond 0 → 1.15 at bond 100. Safe on unknown buyers.
    public static float BondBonus(string id)
    {
        var b = Get(id);
        return b == null ? 1f : 1f + BondMaxPayBonus * (b.bond / 100f);
    }

    /// One filled pip per 20 bond, out of 5.
    public static int PipCount(string id)
    {
        var b = Get(id);
        return b == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(b.bond / 20f), 0, 5);
    }

    /// 5-pip text readout, one pip per 20 bond. The only bond display ever
    /// shown. ASCII bars, not ●/○ — the HUD's Techno SDF font has no
    /// geometric-shape glyphs, so those would render as tofu boxes. UI that
    /// can draw sprites (Messages index) uses PipCount + disc Images instead.
    public static string BondPips(string id)
    {
        int filled = PipCount(id);
        var sb = new System.Text.StringBuilder(5);
        for (int i = 0; i < 5; i++) sb.Append(i < filled ? '|' : '-');
        return sb.ToString();
    }

    // ── Deal reporting (called from MushroomSellUI) ────────────────────────

    /// <param name="keptAppointment">deal fulfilled a Scheduled appointment in-window</param>
    /// <param name="substituted">fulfilled but not exact (tier differed / qty short)</param>
    /// <returns>true if this deal converted the buyer into a regular.</returns>
    /// <summary>
    /// A TAPE sale. The same ledger, the same bond curve, the same
    /// regular-conversion roll — only "did it match their taste" is asked
    /// differently, because a tape's equivalent of a favourite tier is the
    /// genre they actually like.
    ///
    /// `genreIndex` lands in the event's legacy `tier` slot, which is what
    /// BuyerTexts reads back to name the genre.
    /// </summary>
    /// <param name="satBand">AlienFeedback.SatBand of how the buyer rated the
    /// tape (feeds craving); -1 = unknown, treated as "decent".</param>
    /// <param name="namedRequest">the sale filled a NAMED track request
    /// (loop-feel D) — extra craving.</param>
    /// <param name="kind">TraxKind of the sold tape (0 = demo). Half/Full
    /// sales advance songsBought and can fire the growth moment.</param>
    public static bool ReportTapeDeal(string id, int genreIndex, int price, int qty,
                                      bool keptAppointment, bool matchedTaste, int bondBonus = 0,
                                      int satBand = -1, bool namedRequest = false, int kind = 0)
    {
        var b = GetOrCreate(id);
        if (b == null) return false;
        Touch();

        // The demos-to-songs growth moment: their first Half/Full purchase
        // after a real demo history, and they at least rated it decent. Reads
        // PRE-increment state — the same predicate the sell panel uses to
        // decide whether to speak the ForGrowth line.
        bool firstSong = kind > TraxKind.Demo && b.songsBought == 0
                      && b.dealsCompleted >= 3 && satBand >= 2;

        b.dealsCompleted++;
        if (kind > TraxKind.Demo) b.songsBought++;
        // The career counter — every tape sale lands here (walk-ups,
        // deliveries and routed guest sales), so this is TapeCareer's one
        // choke point. World-scoped via StoryDirector.
        TapeCareer.TapesSold += qty;
        int gain = BondPerDeal
                 + (keptAppointment ? BondKeptAppointment : 0)
                 + (matchedTaste ? BondFavouriteTier : 0)
                 + (firstSong ? 3 : 0)   // the growth moment pays in bond
                 + bondBonus;   // e.g. TapeOffer.BondOnGenerousDeal for asking under their value
        b.bond = Mathf.Clamp(b.bond + gain, 0, 100);
        CountDaySale(id, price, qty, gain);
        if (GalaxyTime.Instance != null) b.lastPurchaseDay = GalaxyTime.Instance.Day;

        if (FeatureVault.CravingSystem)
        {
            // Feed the flywheel, then pace the NEXT want-text from the new
            // hunger. This is the cadence rule the loop never had: before it,
            // a completed deal left nextTextAt stale-in-the-past and the
            // buyer re-texted on the next 2-second tick, bounded only by the
            // open-wants cap.
            b.craving = CravingRules.Clamp(
                b.craving + CravingRules.Gain(satBand < 0 ? 2 : satBand, namedRequest));
            b.nextTextAt = Now + Random.Range(CravingRules.BaseDelayMinSeconds,
                                              CravingRules.BaseDelayMaxSeconds)
                               / (float)CravingRules.FrequencyMult(b.craving);
        }

        if (keptAppointment) Log(b, EvType.FulfilledExact, price, qty, genreIndex, markUnread: false, k: kind + 1);
        else                 Log(b, EvType.WalkUpDeal, price, qty, genreIndex, markUnread: false, k: kind + 1);

        if (keptAppointment) CloseConversation(b);

        bool converted = false;
        if (!b.isRegular && Eligible(id))
        {
            // Same rule as mushrooms: guaranteed when you hit what they like,
            // otherwise one in three. Hitting their genre is the tape economy's
            // version of hitting their favourite tier.
            if (matchedTaste || Random.value < 1f / 3f)
            {
                b.isRegular = true;
                converted = true;
            }
        }
        return converted;
    }

    public static bool ReportDeal(string id, MushroomTier tier, int pricePerCap, int qty,
                                  bool keptAppointment, bool substituted)
    {
        var b = GetOrCreate(id);
        if (b == null) return false;
        Touch();

        b.dealsCompleted++;
        bool fav = tier == NPCMushroomPrice.FavouriteTierOf(id);
        int gain = BondPerDeal
                 + (keptAppointment ? BondKeptAppointment : 0)
                 + (fav ? BondFavouriteTier : 0);
        if (substituted) gain /= 2;
        b.bond = Mathf.Clamp(b.bond + gain, 0, 100);

        if (keptAppointment)
            Log(b, substituted ? EvType.FulfilledSub : EvType.FulfilledExact,
                pricePerCap, qty, (int)tier, markUnread: false);
        else if (b.isRegular)
            Log(b, EvType.WalkUpDeal, pricePerCap, qty, (int)tier, markUnread: false);

        if (keptAppointment) CloseConversation(b);

        bool converted = false;
        if (!b.isRegular && Eligible(id))
        {
            // Spec §3: guaranteed if the deal included their favourite tier,
            // otherwise 1 in 3.
            if (fav || Random.value < 1f / 3f)
            {
                b.isRegular = true;
                converted = true;
                // No fanfare now — the director texts when they next go hungry.
            }
        }
        return converted;
    }

    public static void MissedAppointment(string id)
    {
        var b = Get(id);
        if (b == null || b.convo != Convo.Scheduled) return;
        b.bond /= 2;                              // Sam's spec: halve it
        Log(b, EvType.Missed, 0, 0, b.askTier);
        CloseConversation(b);
    }

    public static void CounterRefused(string id)
    {
        var b = Get(id);
        if (b == null) return;
        b.bond = Mathf.Max(0, b.bond - BondCounterRefused);
        Log(b, EvType.BuyerRefused, 0, 0, b.askTier);
        CloseConversation(b);
    }

    public static void SubstitutionRefused(string id, int rolledPercent)
    {
        var b = Get(id);
        if (b == null) return;
        b.bond = Mathf.Max(0, b.bond - BondSubRefused);
        Log(b, EvType.SubRefused, rolledPercent, 0, b.askTier);
        CloseConversation(b);
    }

    /// In-person barred (pushed past their counter) cancels any appointment
    /// WITHOUT the missed-halving — the −10 already landed (spec §9).
    public static void CancelAppointmentQuietly(string id)
    {
        var b = Get(id);
        if (b == null || b.convo == Convo.None) return;
        CloseConversation(b);
    }

    static void CloseConversation(Buyer b)
    {
        Touch();
        b.convo = Convo.None;
        b.counterBackPerCap = 0;
        b.deadline = 0f;
        b.requestTrackId = "";   // a named request dies with its conversation
        // nextTextAt is the director's business (it sets pacing on send).
    }

    // ── Events / thread ────────────────────────────────────────────────────

    public static void Log(Buyer b, EvType t, int a, int bb, int tier, bool markUnread = true, int c = 0, string s = null, int k = 0)
    {
        if (b == null) return;
        Touch();
        b.events.Add(new Ev { type = (int)t, at = Now, a = a, b = bb, tier = tier, c = c, s = s, k = k });
        if (b.events.Count > MaxEventsPerBuyer) b.events.RemoveAt(0);
        // Player-authored events never count as unread; buyer-authored do.
        if (markUnread && t != EvType.PlayerAccepted && t != EvType.PlayerCountered
                       && t != EvType.PlayerDeclined && t != EvType.Scheduled)
            b.unread++;
    }

    /// Craving delta from anywhere that isn't a completed sale (the +2
    /// heard-only listen, day decay). Clamped; no-op with the vault flag off.
    public static void AddCraving(string id, int amount)
    {
        if (!FeatureVault.CravingSystem) return;
        var b = Get(id);
        if (b == null) return;
        int next = CravingRules.Clamp(b.craving + amount);
        if (next == b.craving) return;
        b.craving = next;
        Touch();
    }

    /// The contact-card craving word, or "" when the system is vaulted or the
    /// buyer is unknown.
    public static string CravingWord(string id)
    {
        if (!FeatureVault.CravingSystem) return "";
        var b = Get(id);
        return b == null ? "" : CravingRules.LadderWord(b.craving);
    }

    /// Walking away from a declared FINAL OFFER. A smaller sting than a
    /// refused counter (they liked the song — you only wasted their time),
    /// and deliberately no 5-minute bar: the final-offer flow's whole point
    /// is that probing a ceiling teaches instead of punishing.
    public static void FinalOfferRefused(string id)
    {
        var b = Get(id);
        if (b == null) return;
        Touch();
        b.bond = Mathf.Clamp(b.bond - 4, 0, 100);
    }

    public static void MarkRead(string id)
    {
        var b = Get(id);
        if (b == null || b.unread == 0) return;
        b.unread = 0;
        Touch();
    }

    // ── Hidden-want reveals (spec §6) ──────────────────────────────────────

    public static int RevealCount(string id)
    {
        var b = Get(id);
        return b == null ? 0 : Mathf.Min(b.dealsCompleted, RevealCap);
    }

    /// <summary>
    /// Worded, never numeric. Index 0-4 = the reveal unlocked by deal #1-#5.
    ///
    /// Rewritten for tapes 2026-08-14. Three of the five used to describe a
    /// MUSHROOM buyer: how many caps they could stomach before they were full,
    /// and which tiers they liked. A tape has no tier ladder and an order is for
    /// exactly one tape, so "takes about 12 caps before they're full" was
    /// describing a mechanic that no longer exists.
    ///
    /// These are the one sanctioned leak of a buyer's hidden numbers, and they
    /// stay worded rather than numeric on purpose: the panel deliberately prints
    /// nothing about a buyer, so what the player earns by dealing five times is a
    /// FEEL for them, not a stat block.
    /// </summary>
    public static string RevealLine(string id, int index)
    {
        switch (index)
        {
            case 0:
                return $"has a soft spot for {AlienTaste.FavouriteGenre(id)}";
            case 1:
            {
                // Thresholds sit against the SKEWED falloff distribution
                // (AlienTaste.FalloffSkew): the median alien is ~1.04, so
                // "easy to please" is the common read and "fussy" is the
                // rare one worth remembering.
                double f = AlienTaste.Falloff(id);
                if (f >= 1.40) return "fussy - it has to be close";
                if (f <= 1.00) return "easy to please";
                return "knows what they like";
            }
            case 2:
            {
                // Bands rebased for the widened 0.55..3.0 pay range.
                double m = AlienTaste.PayFactor(id);
                if (m > 1.6) return "pays generously";
                if (m < 0.85) return "a bit stingy";
                return "an average payer";
            }
            case 3:
                return AlienTaste.Patience(id) >= 1.26
                    ? "doesn't mind a cheeky ask"
                    : "walks fast if you push";
            case 4:
            {
                // Tier preference (2026-08-16). Replaced "never wants the same
                // song twice", which was true of EVERY buyer — a reveal that
                // teaches nothing about THIS one isn't a reveal.
                int p = AlienTaste.TierPreference(id);
                if (p > 0) return "only really rates Type 2 shells";
                if (p < 0) return "sticks to cheap Type 1s";
                return "doesn't care what shell it's on";
            }
            default: return "";
        }
    }

    // ── Reset / save plumbing ──────────────────────────────────────────────

    /// New Game must not inherit another run's regulars (CLAUDE.md: statics
    /// leak across the main menu). Called from NewGameReset.Apply().
    public static void ResetAll()
    {
        _buyers.Clear();
        DayTapesSold = 0;
        DayEarned = 0;
        _dayBondUps.Clear();
        Touch();
    }

    /// Serialize into parallel lists (JsonUtility — no dictionaries). Events
    /// are flattened with a per-buyer count list. Times go out RELATIVE.
    public static void FillSave(BuyerLedgerSave s)
    {
        if (s == null) return;
        s.ids.Clear(); s.bond.Clear(); s.deals.Clear(); s.regular.Clear();
        s.unread.Clear(); s.convo.Clear(); s.askTier.Clear(); s.askQty.Clear();
        s.offerPerCap.Clear(); s.counterBack.Clear(); s.windowMinutes.Clear();
        s.deadlineSecondsLeft.Clear(); s.eventCounts.Clear(); s.events.Clear();
        s.askTapeTier.Clear(); s.modulesBasis.Clear();
        s.craving.Clear(); s.lastPurchaseDay.Clear(); s.requestTrackId.Clear();
        s.askKind.Clear(); s.songsBought.Clear();
        s.dayTapesSold = DayTapesSold;
        s.dayEarned = DayEarned;
        s.dayBondUps.Clear();
        s.dayBondUps.AddRange(_dayBondUps);
        float now = Now;
        foreach (var b in _buyers.Values)
        {
            s.ids.Add(b.id);
            s.bond.Add(b.bond);
            s.deals.Add(b.dealsCompleted);
            s.regular.Add(b.isRegular);
            s.unread.Add(b.unread);
            s.convo.Add((int)b.convo);
            s.askTier.Add(b.askTier);
            s.askQty.Add(b.askQty);
            s.offerPerCap.Add(b.offerPerCap);
            s.counterBack.Add(b.counterBackPerCap);
            s.windowMinutes.Add(b.windowMinutes);
            s.deadlineSecondsLeft.Add(b.convo == Convo.Scheduled ? Mathf.Max(0f, b.deadline - now) : 0f);
            s.askTapeTier.Add(b.askTapeTier);
            s.modulesBasis.Add(b.modulesBasis);
            s.craving.Add(b.craving);
            s.lastPurchaseDay.Add(b.lastPurchaseDay);
            s.requestTrackId.Add(b.requestTrackId ?? "");
            s.askKind.Add(b.askKind);
            s.songsBought.Add(b.songsBought);
            s.eventCounts.Add(b.events.Count);
            for (int i = 0; i < b.events.Count; i++)
            {
                var e = b.events[i];
                s.events.Add(new BuyerLedgerSave.EvSave
                    { type = e.type, secondsAgo = Mathf.Max(0f, now - e.at), a = e.a, b = e.b, tier = e.tier, c = e.c, s = e.s, k = e.k });
            }
        }
    }

    public static void ApplySave(BuyerLedgerSave s)
    {
        Touch();
        _buyers.Clear();
        if (s == null || s.ids == null) return;
        float now = Now;
        int evCursor = 0;
        for (int i = 0; i < s.ids.Count; i++)
        {
            var b = new Buyer
            {
                id = s.ids[i],
                bond = s.bond[i],
                dealsCompleted = s.deals[i],
                isRegular = s.regular[i],
                unread = s.unread[i],
                convo = (Convo)s.convo[i],
                askTier = s.askTier[i],
                askQty = s.askQty[i],
                offerPerCap = s.offerPerCap[i],
                counterBackPerCap = s.counterBack[i],
                windowMinutes = s.windowMinutes[i],
                deadline = (Convo)s.convo[i] == Convo.Scheduled ? now + s.deadlineSecondsLeft[i] : 0f,
                // New lists are absent (empty) on pre-feature saves — every
                // other list here has always been written together, but these
                // two must carry their own guard or old saves throw.
                askTapeTier = (s.askTapeTier != null && i < s.askTapeTier.Count) ? s.askTapeTier[i] : 0,
                modulesBasis = (s.modulesBasis != null && i < s.modulesBasis.Count) ? s.modulesBasis[i] : 0,
                craving = (s.craving != null && i < s.craving.Count) ? s.craving[i] : 0,
                lastPurchaseDay = (s.lastPurchaseDay != null && i < s.lastPurchaseDay.Count) ? s.lastPurchaseDay[i] : 0,
                requestTrackId = (s.requestTrackId != null && i < s.requestTrackId.Count) ? s.requestTrackId[i] : "",
                askKind = (s.askKind != null && i < s.askKind.Count) ? s.askKind[i] : 0,
                songsBought = (s.songsBought != null && i < s.songsBought.Count) ? s.songsBought[i] : 0,
            };
            int n = s.eventCounts[i];
            for (int e = 0; e < n && evCursor < s.events.Count; e++, evCursor++)
            {
                var es = s.events[evCursor];
                b.events.Add(new Ev { type = es.type, at = now - es.secondsAgo, a = es.a, b = es.b, tier = es.tier, c = es.c, s = es.s, k = es.k });
            }
            _buyers[b.id] = b;
        }
        DayTapesSold = s.dayTapesSold;
        DayEarned = s.dayEarned;
        _dayBondUps.Clear();
        if (s.dayBondUps != null) _dayBondUps.AddRange(s.dayBondUps);
    }
}
