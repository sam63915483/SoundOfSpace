using UnityEngine;

/// <summary>
/// State for Tev's mushroom onboarding (handoff §2.2), stored in StoryDirector
/// flags + counters so it round-trips through saves like every other story beat.
/// Static helper over those keys, same shape as Mission1 / ColdCompany.
///
/// The loop, as Sam specified it:
///   • first talk  → Tev fronts you 3 mushrooms
///   • come back with NONE left and NONE sold → he ridicules you and fronts you
///     another 3. He'll do that <see cref="MaxRefronts"/> times, then he's done
///     and tells you to find your own out in the wild.
///   • come back having SOLD at least one, holding none → he teaches the loop
///     (trees → oxygen → faster mushrooms) and the onboarding completes.
///
/// <see cref="HeldCount"/> deliberately reads the HOTBAR ONLY, never the
/// shuttle locker. That is the intentional "glitch" Sam asked for: stash his
/// caps in the locker, tell him you lost them, and he fronts you more. It's a
/// real exploit with a hard ceiling — five free batches and the tap shuts off —
/// so a player who finds it gets rewarded for being clever exactly as far as the
/// designer allows, and no further.
/// </summary>
public static class MushroomQuest
{
    public enum Stage { NotMet = 0, Given = 1, Complete = 2 }

    // StoryDirector keys.
    const string KeyStage    = "mushQuestStage";      // counter
    const string KeySold     = "mushQuestSold";       // counter — mushrooms sold since he fronted you
    const string KeyRefronts = "mushQuestRefronts";   // counter — extra batches he's handed over

    /// Mushrooms in a batch Tev hands over.
    public const int BatchSize = 3;
    /// How many EXTRA batches he'll front after the first one before he gives up.
    public const int MaxRefronts = 5;

    public static Stage CurrentStage
    {
        get
        {
            var sd = StoryDirector.Instance;
            if (sd == null) return Stage.NotMet;
            return (Stage)Mathf.Clamp(sd.GetCounter(KeyStage), 0, 2);
        }
        set { StoryDirector.Instance?.SetCounter(KeyStage, (int)value); }
    }

    /// How many mushrooms the player has sold since Tev fronted them.
    public static int SoldCount
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeySold) : 0;
        set { StoryDirector.Instance?.SetCounter(KeySold, Mathf.Max(0, value)); }
    }

    /// How many extra batches Tev has fronted (0 = only the original three).
    public static int Refronts
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyRefronts) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyRefronts, Mathf.Max(0, value)); }
    }

    public static bool CanRefront => Refronts < MaxRefronts;

    /// Mushrooms of ANY species currently in the player's HOTBAR. Not the locker
    /// — see the class summary; that gap is the point.
    public static int HeldCount =>
        Hotbar.Instance != null ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Mushroom) : 0;

    /// The species Tev deals in. Fixed per save so his caps are recognisably his.
    /// Stored as a StoryDirector flag pair would be silly for a string, so it's
    /// derived from the registry instead — index 0, deterministic, and it needs
    /// no persistence at all.
    public static string SpeciesKey => MushroomRegistry.KeyForSeed(0);

    /// Called by the sell flow whenever mushrooms change hands. Only counts
    /// while the onboarding is actually live, so post-onboarding sales don't
    /// keep incrementing a number nothing reads.
    public static void NotifySold(int count)
    {
        if (count <= 0) return;
        if (CurrentStage != Stage.Given) return;
        SoldCount += count;
    }

    /// Hand the player a batch. Returns how many actually fit — 0 means their
    /// pack is full and the caller should say so rather than silently eat them.
    public static int GrantBatch()
    {
        if (Hotbar.Instance == null) return 0;
        string species = SpeciesKey;
        if (string.IsNullOrEmpty(species)) return 0;
        int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.Mushroom, BatchSize, species);
        return BatchSize - leftover;
    }
}
