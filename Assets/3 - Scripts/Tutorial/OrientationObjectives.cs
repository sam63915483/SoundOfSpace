using System;
using UnityEngine;

/// <summary>
/// The seven orientation objectives on the shuttle whiteboard.
///
/// The board is the WHOLE feature: no order enforcement, no rewards, no popups,
/// no tracker anywhere else. Walk past it and you've opted out; read it and it
/// teaches you every survival system the game has without ever taking the
/// controls off you.
///
/// ── Where progress lives ────────────────────────────────────────────────
/// IN THE WORLD SAVE, inside this character's PlayerBlockSave (Sam, 2026-08-18
/// — everything is a world save now; a character is a name and a suit colour).
/// It used to live on the character itself, which meant a veteran walked into a
/// brand-new world with the board already crossed off; now a new world starts
/// you a new board, which is the right read for a game about being dropped
/// somewhere.
///
/// The co-op property survives the move intact, because the mask is stored PER
/// CHARACTER rather than per world: the board renders the VIEWING player's own
/// mask, so a host who finished it and a guest who just arrived look at the same
/// board and see different things, with no netcode at all. Nothing here syncs,
/// because there is nothing here another machine needs to know.
///
/// Completion is one-way. There is no Uncomplete: an objective that could
/// un-cross would make the board a status display rather than a record of what
/// you've done, and the player would have to wonder whether they'd really done it.
/// </summary>
public static class OrientationObjectives
{
    /// Order here is the order on the board. APPEND ONLY — these are bit
    /// positions inside a persisted mask, so inserting one silently re-labels
    /// every character's completed objectives.
    public enum Objective
    {
        TakeAxeAndBottle = 0,
        DrinkWater       = 1,
        CatchFish        = 2,
        EatCookedFish    = 3,
        ChopTree         = 4,
        PlantSapling     = 5,
        SaveInStasisPod  = 6,
    }

    public const int Count = 7;

    /// Fired when an objective flips to complete. The board listens; nothing
    /// else should need to.
    public static event Action<Objective> Completed;

    /// Player-facing text. Deliberately phrased as instructions rather than
    /// checklist nouns ("Chop down a tree", not "Tree: 0/1") — the board is a
    /// list of things to try, and a counter would imply a quota.
    ///
    /// Keep every line under ~40 characters. The board auto-fits its text, so a
    /// long line doesn't overflow — it WRAPS, and a wrapped line starts back at
    /// the left margin where it reads as an eighth bullet. Shorter lines also
    /// let the auto-fit pick a bigger font for all of them.
    public static string Label(Objective o)
    {
        switch (o)
        {
            case Objective.TakeAxeAndBottle: return "Take the axe and bottle from the locker";
            case Objective.DrinkWater:       return "Fill your water bottle and drink it";
            case Objective.CatchFish:        return "Catch a fish (rod's in Tev's cabin)";
            case Objective.EatCookedFish:    return "Cook a fish on a bonfire and eat it";
            case Objective.ChopTree:         return "Chop down a tree";
            case Objective.PlantSapling:     return "Plant a sapling";
            case Objective.SaveInStasisPod:  return "Save your game in the stasis pod";
            default: return "";
        }
    }

    /// <summary>
    /// The live mask. One in-memory int for the current run, captured into this
    /// character's block by SaveCollector and restored from it on load — the
    /// same deal every other piece of world progress gets.
    ///
    /// Editor workflow keeps working with no character at all: press Play
    /// straight into the gameplay scene, tick lines, and they hold for the
    /// session. There is simply nowhere to persist them to until a save happens.
    /// </summary>
    static int _mask;

    static int Mask => _mask;

    /// What SaveCollector writes into the character's block.
    public static int CurrentMask => _mask;

    /// <summary>
    /// Load a saved board. Replaces rather than merges: this world's record of
    /// what you did here is the whole truth, and OR-ing in whatever the last
    /// world left in memory is how a fresh world would arrive pre-crossed.
    ///
    /// Fires no Completed events — those drive the board's tick animation, and a
    /// load is not seven things happening.
    /// </summary>
    public static void RestoreMask(int mask) { _mask = mask; }

    /// New Game, and a character walking into a world they have never played.
    /// Both mean a blank board.
    public static void ResetForNewWorld() { _mask = 0; }

    public static bool IsComplete(Objective o) => (Mask & (1 << (int)o)) != 0;

    /// True once every line is crossed — the board dims itself at that point
    /// rather than disappearing, so a returning player can still read it.
    public static bool AllComplete
    {
        get
        {
            int all = (1 << Count) - 1;
            return (Mask & all) == all;
        }
    }

    /// Mark an objective done. Idempotent and one-way; safe to call from a hook
    /// that fires every time the player drinks, not just the first time.
    ///
    /// Held in memory until a save commits it. Sam's rule: the stasis pod is
    /// the only save point, so ticking a line here and quitting without
    /// uploading loses the tick — the same deal all world progress gets.
    public static void Complete(Objective o)
    {
        int bit = 1 << (int)o;
        if ((_mask & bit) != 0) return;               // already done
        _mask |= bit;
        Completed?.Invoke(o);
    }
}
