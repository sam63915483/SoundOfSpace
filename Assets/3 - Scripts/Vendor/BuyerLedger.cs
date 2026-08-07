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

    public enum Convo { None = 0, AwaitingReply = 1, AwaitingCounterBack = 2, Scheduled = 3 }

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
    }

    public class Ev
    {
        public int type;
        public float at;      // Time.unscaledTime when it happened
        public int a, b, tier;
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
    }

    static readonly Dictionary<string, Buyer> _buyers = new Dictionary<string, Buyer>();
    static float Now => Time.unscaledTime;

    // Story NPCs never become regulars — their threads belong to story systems.
    static readonly string[] ExcludedIdPrefixes = { "scene:Tev", "scene:Kolb" };

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
    public static bool ReportDeal(string id, MushroomTier tier, int pricePerCap, int qty,
                                  bool keptAppointment, bool substituted)
    {
        var b = GetOrCreate(id);
        if (b == null) return false;

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
        b.convo = Convo.None;
        b.counterBackPerCap = 0;
        b.deadline = 0f;
        // nextTextAt is the director's business (it sets pacing on send).
    }

    // ── Events / thread ────────────────────────────────────────────────────

    public static void Log(Buyer b, EvType t, int a, int bb, int tier, bool markUnread = true)
    {
        if (b == null) return;
        b.events.Add(new Ev { type = (int)t, at = Now, a = a, b = bb, tier = tier });
        if (b.events.Count > MaxEventsPerBuyer) b.events.RemoveAt(0);
        // Player-authored events never count as unread; buyer-authored do.
        if (markUnread && t != EvType.PlayerAccepted && t != EvType.PlayerCountered
                       && t != EvType.PlayerDeclined && t != EvType.Scheduled)
            b.unread++;
    }

    public static void MarkRead(string id)
    {
        var b = Get(id);
        if (b != null) b.unread = 0;
    }

    // ── Hidden-want reveals (spec §6) ──────────────────────────────────────

    public static int RevealCount(string id)
    {
        var b = Get(id);
        return b == null ? 0 : Mathf.Min(b.dealsCompleted, RevealCap);
    }

    /// Worded, never numeric. Index 0-4 = the reveal unlocked by deal #1-#5.
    public static string RevealLine(string id, int index)
    {
        switch (index)
        {
            case 0: return $"takes about {NPCMushroomPrice.AppetiteMaxOf(id)} caps before they're full";
            case 1: return $"keen on {MushroomSpecies.TierName(NPCMushroomPrice.FavouriteTierOf(id)).ToLowerInvariant()}";
            case 2: return $"won't pay much for {MushroomSpecies.TierName(NPCMushroomPrice.DislikedTierOf(id)).ToLowerInvariant()}";
            case 3:
            {
                float m = NPCMushroomPrice.MultiplierOf(id);
                if (m > 1.15f) return "pays generously";
                if (m < 0.95f) return "a bit stingy";
                return "an average payer";
            }
            case 4:
                return NPCMushroomPrice.PatienceOf(id) >= 1.26f
                    ? "doesn't mind a cheeky ask"
                    : "walks fast if you push";
            default: return "";
        }
    }

    // ── Reset / save plumbing ──────────────────────────────────────────────

    /// New Game must not inherit another run's regulars (CLAUDE.md: statics
    /// leak across the main menu). Called from NewGameReset.Apply().
    public static void ResetAll() => _buyers.Clear();

    /// Serialize into parallel lists (JsonUtility — no dictionaries). Events
    /// are flattened with a per-buyer count list. Times go out RELATIVE.
    public static void FillSave(BuyerLedgerSave s)
    {
        if (s == null) return;
        s.ids.Clear(); s.bond.Clear(); s.deals.Clear(); s.regular.Clear();
        s.unread.Clear(); s.convo.Clear(); s.askTier.Clear(); s.askQty.Clear();
        s.offerPerCap.Clear(); s.counterBack.Clear(); s.windowMinutes.Clear();
        s.deadlineSecondsLeft.Clear(); s.eventCounts.Clear(); s.events.Clear();
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
            s.eventCounts.Add(b.events.Count);
            for (int i = 0; i < b.events.Count; i++)
            {
                var e = b.events[i];
                s.events.Add(new BuyerLedgerSave.EvSave
                    { type = e.type, secondsAgo = Mathf.Max(0f, now - e.at), a = e.a, b = e.b, tier = e.tier });
            }
        }
    }

    public static void ApplySave(BuyerLedgerSave s)
    {
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
            };
            int n = s.eventCounts[i];
            for (int e = 0; e < n && evCursor < s.events.Count; e++, evCursor++)
            {
                var es = s.events[evCursor];
                b.events.Add(new Ev { type = es.type, at = now - es.secondsAgo, a = es.a, b = es.b, tier = es.tier });
            }
            _buyers[b.id] = b;
        }
    }
}
