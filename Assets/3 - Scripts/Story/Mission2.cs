using UnityEngine;

/// <summary>
/// Mission 2 "The Quiet System" — central flag registry for the Act 2/3 story
/// content (docs/MISSIONS_DESIGN.md + docs/story-drafts/). Same shape as
/// <see cref="Mission1"/>: typed accessors over StoryDirector's flag dictionary
/// so mission code never hand-types a magic string. Everything round-trips
/// through the save for free via StoryDirector.SaveTo/LoadFrom, and New Game
/// clears it all via StoryDirector.ResetForNewGame.
///
/// The flag STRINGS must match the conv_*.json drafts in docs/story-drafts/
/// exactly — the JSON sets/reads these via SetFlag effects and
/// requiresFlag/hiddenIfFlag gates.
/// </summary>
public static class Mission2
{
    // ── "Face Down" (N-1) ───────────────────────────────────────────────────
    public const string FlagFaceDownOffered  = "FaceDown_Offered";   // conv_face_down queued once
    public const string FlagFaceDownAccepted = "FaceDown_Accepted";
    public const string FlagFaceDownRefused  = "FaceDown_Refused";
    public const string FlagFaceDownWaitDone = "FaceDown_WaitDone";  // 60s wait completed, _after queued
    public const string FlagFaceDownDone     = "FaceDown_Done";

    // ── Act 2 contracts ─────────────────────────────────────────────────────
    public const string FlagMoonOfferTaken   = "M2_MoonOfferTaken";
    public const string FlagMoonDelivered    = "M2_MoonDelivered";
    public const string FlagClaimsOfferTaken = "M2_ClaimsOfferTaken";
    public const string FlagFieryClaims      = "M2_FieryClaims";
    public const string FlagTevLetterGiven   = "Tev_LetterGiven";
    public const string FlagIceyReached      = "M2_IceyReached";
    public const string FlagIceyVisited      = "M2_IceyVisited";
    public const string FlagLedgerHeld       = "M2_LedgerHeld";
    public const string FlagLedgerToORG      = "Ledger_ToORG";
    public const string FlagLedgerKept       = "Ledger_KeptFromORG";
    public const string FlagLedgerDelivered  = "Ledger_Delivered";
    public const string FlagBeanOfferTaken   = "M2_BeanOfferTaken";
    public const string FlagBeanSalvage      = "M2_BeanSalvage";
    public const string FlagPupilReading     = "M2_PupilReading";
    public const string FlagLightsOnTaken    = "M2_LightsOnTaken";
    public const string FlagShadowRescue     = "M2_ShadowRescue";

    // ── Rebels / concerts ───────────────────────────────────────────────────
    public const string FlagRebelMet         = "M2_RebelMet";
    public const string FlagRebelContact     = "M2_RebelContact";
    public const string FlagCoverSetDone     = "M2_CoverSetDone";

    // ── The Interview (A2-7) ────────────────────────────────────────────────
    public const string FlagInterviewDeniedName = "Interview_DeniedName";
    public const string FlagInterviewLied       = "Interview_LiedAboutHAL";
    public const string FlagInterviewDone       = "Interview_Done";
    public const string FlagOrgReveal           = "ORG_Reveal"; // StoryDirector-side; bridged to EarlyGameProgress by Mission2Director

    // ── Side content ────────────────────────────────────────────────────────
    public const string FlagTradeBackHasFish   = "TradeBack_HasFish";   // precomputed before conv_trade_back
    public const string FlagTradeBackHasGuitar = "TradeBack_HasGuitar";
    public const string FlagCassetteSixOwned   = "CassetteSix_Owned";
    public const string FlagCassetteSixHeard   = "CassetteSix_Heard";
    public const string FlagPaleOneSeen        = "PaleOne_Seen";
    public const string FlagOwnersAllFound     = "Owners_AllFound";
    public const string FlagDimensionReturned  = "M2_DimensionReturned"; // any black-hole dimension round trip

    // ── Act 3 ───────────────────────────────────────────────────────────────
    public const string FlagTalkQueued    = "Talk_Queued";      // conv_we_need_to_talk queued once
    public const string FlagTalkHasKills  = "Talk_HasKills";    // precomputed (see PrecomputeTalkFlags)
    public const string FlagTalkCleanHands= "Talk_CleanHands";
    public const string FlagTalkManyDeaths= "Talk_ManyDeaths";
    public const string FlagTalkAgreed    = "Talk_Agreed";
    public const string FlagAtTheDoor     = "AtTheDoor";
    public const string FlagEndingRelease = "Ending_Release";
    public const string FlagEndingStay    = "Ending_Stay";
    public const string FlagEndingHandover= "Ending_Handover";

    const int ManyDeathsThreshold = 5;

    // ── Flag helpers (null-safe; no-op if StoryDirector isn't up yet) ───────
    public static bool Get(string flag)
    {
        var sd = StoryDirector.Instance;
        return sd != null && sd.GetFlag(flag);
    }

    public static void Set(string flag, bool value = true)
    {
        var sd = StoryDirector.Instance;
        if (sd != null) sd.SetFlag(flag, value);
    }

    // ── Act 2 progress gate ─────────────────────────────────────────────────
    static readonly string[] Act2Missions =
    {
        FlagMoonDelivered, FlagFieryClaims, FlagLedgerHeld,
        FlagBeanSalvage, FlagPupilReading, FlagShadowRescue,
    };

    public static int Act2MissionCount()
    {
        int n = 0;
        for (int i = 0; i < Act2Missions.Length; i++)
            if (Get(Act2Missions[i])) n++;
        return n;
    }

    /// <summary>Call from the portal/dimension code on the first successful
    /// return from any black-hole dimension to the gameplay scene.</summary>
    public static void NotifyDimensionReturn() => Set(FlagDimensionReturned);

    // ── "We Need to Talk" precompute ────────────────────────────────────────
    /// <summary>
    /// conv_we_need_to_talk routes on requiresFlag/hiddenIfFlag, which can only
    /// read StoryDirector flags — so the trigger snapshots live game data into
    /// flags immediately before starting the conversation. Recomputed every
    /// call (never stale), so these need no dedicated save handling.
    /// </summary>
    public static void PrecomputeTalkFlags()
    {
        int deaths = ResourceManager.Instance != null ? ResourceManager.Instance.TotalDeaths : 0;
        bool hasKills = false;
        var spawner = Object.FindObjectOfType<AlienNPCSpawner>();
        if (spawner != null)
        {
            foreach (var _ in spawner.GetKilledPrePlacedNames()) { hasKills = true; break; }
        }
        Set(FlagTalkHasKills, hasKills);
        Set(FlagTalkCleanHands, !hasKills);
        Set(FlagTalkManyDeaths, deaths >= ManyDeathsThreshold);
    }

    /// <summary>Killed pre-placed NPC names joined for the {KILLED_NAMES} token.</summary>
    public static string KilledNamesJoined()
    {
        var spawner = Object.FindObjectOfType<AlienNPCSpawner>();
        if (spawner == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var name in spawner.GetKilledPrePlacedNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(name);
        }
        return sb.ToString();
    }
}
