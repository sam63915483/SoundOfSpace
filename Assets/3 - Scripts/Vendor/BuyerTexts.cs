using UnityEngine;

/// <summary>
/// Every line a buyer sends, rendered on demand from BuyerLedger events —
/// saves store events, never strings (spec §7). Three voice variants per
/// event, chosen by a stable per-buyer hash so Vorn always sounds like Vorn.
/// No LLM anywhere; same philosophy as HAL's templated lines.
/// </summary>
public static class BuyerTexts
{
    static int Voice(string id) => (int)(AlienIdentity.Hash(id + ":voice") % 3u);
    // `tier` on an event now carries a GENRE INDEX — the field name is legacy
    // and kept so the save schema does not move. Genres are shouted in caps
    // everywhere else in the game (the console readout, the classifier), so
    // they stay uppercase here even though the buyers text in lowercase.
    static string Genre(int genreIndex) => TapeTrade.GenreName(genreIndex);
    static string Tapes(int qty) => TapeTrade.TapeWord(qty);
    // The CASSETTE tier rides the event's fourth slot (`c`); 0 = event from
    // before tiers existed — say nothing rather than guess.
    static string TierWord(int c) => c >= 1 ? $" (Type {c})" : "";

    public static string Render(string id, BuyerLedger.Ev e)
    {
        int v = Voice(id);
        switch ((BuyerLedger.EvType)e.type)
        {
            case BuyerLedger.EvType.WantText:
                switch (v)
                {
                    case 0:  return $"after {e.b} {Genre(e.tier)} {Tapes(e.b)}{TierWord(e.c)}. I'll do {e.a} each if you can get here.";
                    case 1:  return $"in the mood for something {Genre(e.tier)}. {e.b} of them{TierWord(e.c)}, {e.a} each. come find me.";
                    default: return $"nothing new to listen to. {e.b} {Genre(e.tier)} {Tapes(e.b)}{TierWord(e.c)}, {e.a} each — you in?";
                }
            case BuyerLedger.EvType.PlayerAccepted:
                return $"on my way — give me {e.a} minutes.";
            case BuyerLedger.EvType.PlayerCountered:
                // b carries the countered quantity (0 on pre-quantity-slider
                // saves — fall back to the old price-only wording).
                return e.b > 0 ? $"I'll do {e.b} — {e.a} each." : $"make it {e.a} a tape.";
            case BuyerLedger.EvType.BuyerCounterBack:
                // b == 1 flags the grudging-acceptance flavor (their counter
                // resolved as Accept at the player's number).
                if (e.b == 1) return $"...fine. {e.a} a tape. don't be late.";
                switch (v)
                {
                    case 0:  return $"steep. {e.a} and we're done talking.";
                    case 1:  return $"can't do that. {e.a}, final.";
                    default: return $"you're pushing it. {e.a}.";
                }
            case BuyerLedger.EvType.BuyerRefused:
                switch (v)
                {
                    case 0:  return "forget it. don't text me numbers like that.";
                    case 1:  return "that's a joke. deal's off.";
                    default: return "no. we're done here.";
                }
            case BuyerLedger.EvType.PlayerDeclined:
                return "can't right now.";
            case BuyerLedger.EvType.Scheduled:
                switch (v)
                {
                    case 0:  return $"good. {e.b} {Genre(e.tier)}{TierWord(e.c)} at {e.a} each. I'll be waiting.";
                    case 1:  return $"deal — {e.b} {Genre(e.tier)}{TierWord(e.c)}, {e.a} each. don't dawdle.";
                    default: return $"see you soon then. {e.b} {Genre(e.tier)}{TierWord(e.c)} at {e.a}.";
                }
            case BuyerLedger.EvType.FulfilledExact:
                switch (v)
                {
                    case 0:  return "pleasure doing business.";
                    case 1:  return "exactly what I wanted. good.";
                    default: return "quality. I'll be in touch.";
                }
            case BuyerLedger.EvType.FulfilledSub:
                return $"not what I asked for... but it's alright. I'll take it.";
            case BuyerLedger.EvType.SubRefused:
                switch (v)
                {
                    case 0:  return "that's not what I ordered. waste of my time.";
                    case 1:  return "no. we had a deal and this isn't it.";
                    default: return "you show up with THAT? forget it.";
                }
            case BuyerLedger.EvType.Missed:
                switch (v)
                {
                    case 0:  return "waited 20 minutes. don't bother next time.";
                    case 1:  return "you never showed. remembering that.";
                    default: return "stood me up. nice.";
                }
            case BuyerLedger.EvType.WalkUpDeal:
                return ""; // rendered as a system line by the thread view, not a bubble
            case BuyerLedger.EvType.DayRecap:
                // A wrap is a frozen snapshot of its day — composed once by
                // DayRecap.Compose and stored, deliberately NOT re-rendered
                // against live state (yesterday's rent line must stay
                // yesterday's).
                return e.s ?? "";
            case BuyerLedger.EvType.NamedRequest:
            {
                // s = "trackId|TRACK NAME|GOSSIPER" (see BuyerMessageDirector).
                string track = "a tape of yours", from = "";
                if (!string.IsNullOrEmpty(e.s))
                {
                    var parts = e.s.Split('|');
                    if (parts.Length > 1 && parts[1].Length > 0) track = parts[1];
                    if (parts.Length > 2) from = parts[2];
                }
                string at = string.IsNullOrEmpty(from) ? "around" : $"at {from}'s";
                switch (v)
                {
                    case 0:  return $"heard {track} {at}. that's yours, right? press me a copy — {e.a} each. come find me.";
                    case 1:  return $"someone played me {track} {at}. I want my own. {e.a} each if you can get here.";
                    default: return $"can't stop thinking about {track} — heard it {at}. {e.a} for a copy? you in?";
                }
            }
            default: return "";
        }
    }

    /// Short index-page preview for the most recent event.
    public static string Preview(string id, BuyerLedger.Ev e)
    {
        string s = Render(id, e);
        if (string.IsNullOrEmpty(s)) return "made a deal in person";
        s = s.Replace('\n', ' ');   // the day wrap is multi-line; the index row isn't
        return s.Length <= 40 ? s : s.Substring(0, 38) + "…";
    }
}
