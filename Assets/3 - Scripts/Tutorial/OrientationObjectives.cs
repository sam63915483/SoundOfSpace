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
/// On the CHARACTER (CharacterProfile.orientationMask), not the world save.
/// Sam's call, and it's what makes the co-op requirement fall out for free:
/// the board renders the VIEWING player's own mask, so a host who finished it
/// and a guest who just made a character look at the same board and see
/// different things, with no netcode at all. Nothing here syncs, because there
/// is nothing here that another machine needs to know.
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

    // Session fallback for when there is no character to write to. That happens
    // in the Editor's normal workflow — press Play straight into the gameplay
    // scene without ever having made a character in the menu — and on a fresh
    // install before the first character exists. Without it the board silently
    // never ticks and reads as broken. Not persisted, by definition: there's
    // nowhere to persist it TO.
    static int _sessionMask;

    static int Mask
    {
        get
        {
            var p = CharacterStore.ActiveProfile;
            return p != null ? p.orientationMask : _sessionMask;
        }
    }

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
    /// Marks the character book dirty rather than writing to disk. Sam's rule:
    /// the stasis pod is the only save point and it commits character and world
    /// together, so ticking a line here and quitting without uploading loses the
    /// tick — the same deal world progress gets.
    public static void Complete(Objective o)
    {
        int bit = 1 << (int)o;
        if ((Mask & bit) != 0) return;               // already done

        var p = CharacterStore.ActiveProfile;
        if (p != null)
        {
            p.orientationMask |= bit;
            CharacterStore.Instance?.MarkDirty();
        }
        else _sessionMask |= bit;                    // no character — session only

        Completed?.Invoke(o);
    }

    /// Wipe this character's board. NOT called by New Game — a new WORLD does
    /// not make you a beginner again, which is the whole reason the mask lives
    /// on the character. Here for the character-creation flow and for testing.
    public static void ResetActiveCharacter()
    {
        _sessionMask = 0;
        var p = CharacterStore.ActiveProfile;
        if (p == null || p.orientationMask == 0) return;
        p.orientationMask = 0;
        CharacterStore.Instance?.MarkDirty();
    }
}
