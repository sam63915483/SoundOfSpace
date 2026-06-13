using UnityEngine;

/// <summary>
/// Mission 1 "Taken In" — central registry for the three-way fork's state.
///
/// All state lives as named flags on <see cref="StoryDirector"/> so it round-trips
/// through the save system for free (StoryDirector.SaveTo/LoadFrom already serialise
/// the flag dictionary). This class is just typed, discoverable accessors over those
/// string keys so the rest of the mission code never hand-types a magic flag name.
///
/// See docs/GDD_VerticalSlice_Mission1_Fork.md and
/// docs/superpowers/plans/2026-06-06-mission1-taken-in-vertical-slice.md.
/// </summary>
public static class Mission1
{
    // ── Progress flags (StoryDirector keys) ───────────────────────────────
    public const string FlagMetTevVillage = "m1_met_tev";      // had the village intro with Tev
    public const string FlagExplored      = "m1_explored";     // saw enough discoverables to report
    public const string FlagReported      = "m1_reported";     // gave Tev the report (fork offered)
    public const string FlagPilotStarted  = "m1_pilot_started";// chose Pilot, sent to the instructor
    public const string FlagInstructorBriefed = "m1_instructor_briefed"; // heard the instructor's first briefing
    public const string FlagLicensed      = "m1_licensed";     // passed drone school, license granted
    public const string FlagComplete      = "m1_complete";     // reached Constant Companion → M2 seam

    // ── Branch choice (stored as mutually-exclusive bool flags) ────────────
    public const string FlagBranchPilot = "m1_branch_pilot";
    public const string FlagBranchBuild = "m1_branch_build";
    public const string FlagBranchFish  = "m1_branch_fish";

    public enum Branch { None, Pilot, Build, Fish }

    // ── Discoverables ──────────────────────────────────────────────────────
    // For the slice the two "discoverables" are simply MEETING THE VILLAGE VENDORS
    // (fish vendor + goods vendor). Talking to each is detected in StoryDirector via
    // NPCConversationTracker and recorded on EarlyGameProgress.FishVendorVisited /
    // GoodsVendorVisited (which also drive the phone quest rows + HAL lines).
    //
    // The generic trigger-based discoverable ids below are kept for future explore
    // beats (e.g. the planned fishing dock) — Discoverable.cs still records them — but
    // they are NOT part of the report gate for now.
    public const string DiscVista     = "m1_disc_vista";      // a vista with Constant Companion overhead
    public const string DiscStructure = "m1_disc_structure";  // a strange structure
    public const string DiscFishing   = "m1_disc_fishing";    // a creature / fishing spot

    static readonly string[] AllDiscoverables = { DiscVista, DiscStructure, DiscFishing };

    // ── Vendor "discoverables" (the slice's explore gate) ──────────────────
    public static bool VisitedFishVendor()  => EarlyGameProgress.FishVendorVisited;
    public static bool VisitedGoodsVendor() => EarlyGameProgress.GoodsVendorVisited;
    public static int VendorsVisited() => (VisitedFishVendor() ? 1 : 0) + (VisitedGoodsVendor() ? 1 : 0);

    // ── Flag helpers (all null-safe; no-op if StoryDirector isn't up yet) ──
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

    // ── Discoverables ──────────────────────────────────────────────────────
    /// <summary>Records that a discoverable was seen. Idempotent.</summary>
    public static void MarkSeen(string discoverableId)
    {
        if (string.IsNullOrEmpty(discoverableId)) return;
        Set(discoverableId, true);
    }

    public static bool WasSeen(string discoverableId) => Get(discoverableId);

    public static int SeenCount()
    {
        int n = 0;
        for (int i = 0; i < AllDiscoverables.Length; i++)
            if (Get(AllDiscoverables[i])) n++;
        return n;
    }

    /// <summary>True once the player has met BOTH village vendors — the slice's report gate.</summary>
    public static bool ExploredEnough() => VisitedFishVendor() && VisitedGoodsVendor();

    // ── Branch ──────────────────────────────────────────────────────────────
    public static void SetBranch(Branch b)
    {
        Set(FlagBranchPilot, b == Branch.Pilot);
        Set(FlagBranchBuild, b == Branch.Build);
        Set(FlagBranchFish,  b == Branch.Fish);
    }

    public static Branch GetBranch()
    {
        if (Get(FlagBranchPilot)) return Branch.Pilot;
        if (Get(FlagBranchBuild)) return Branch.Build;
        if (Get(FlagBranchFish))  return Branch.Fish;
        return Branch.None;
    }
}
