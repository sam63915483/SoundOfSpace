// Static flags for the early-game tutorial progression that don't fit cleanly
// into a single TutorialStep. Steps and dialogues set these as they advance,
// and other steps / NPCs read them to gate their own behavior. Persists across
// scene reloads as static fields; saved/restored via SaveCollector.
//
// Adding a new flag = one line here + matching field in EarlyGameProgressSave.
public static class EarlyGameProgress
{
    // Phase 1
    public static bool NoteRead;

    // Phase 2
    public static bool RodPickedUp;
    public static bool FirstFishCaught;
    public static bool OneOfEachCaught;

    // Phase 3
    public static bool FirstMealEaten;

    // Phase 4
    public static bool WaterBottleDrunk;

    // Phase 5
    public static bool ReturnedHome;
    public static bool TevReturnedDialogueDone;

    // Phase 6
    public static bool CabinBuilt;

    // Phase 7
    public static bool VillageCoordsGiven;
    public static bool FishVendorVisited;
    public static bool GoodsVendorVisited;

    // Story-arc placeholder flag. When set, the phone AI's gated knowledge
    // file (game_knowledge_org_reveal.md) is merged into GameKnowledgeBase
    // by AIStoryController, unlocking Phase 2/3 personas and ORG lore. The
    // story beat that flips this in production hasn't been written yet —
    // for now, dev/cheat code is the only setter. See
    // docs/AI_Companion_Revamp_Plan.md §7 (Knowledge gating).
    public static bool ORG_Reveal;

    // Resets every flag to its fresh-game default (all false). Called by
    // NewGameReset on New Game — these are static fields, so without this a
    // previous unsaved session's story progress carries into the new game.
    public static void ResetAll()
    {
        NoteRead = false;
        RodPickedUp = false;
        FirstFishCaught = false;
        OneOfEachCaught = false;
        FirstMealEaten = false;
        WaterBottleDrunk = false;
        ReturnedHome = false;
        TevReturnedDialogueDone = false;
        CabinBuilt = false;
        VillageCoordsGiven = false;
        FishVendorVisited = false;
        GoodsVendorVisited = false;
        ORG_Reveal = false;
    }
}
