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
    const string KeyRent     = "tevRentPerWeek";      // counter — credits per galactic week for the lawn
    const string FlagRentSet = "tevRentSettled";      // flag — the haggle has happened
    const string KeyArrears  = "tevRentArrears";      // counter — unpaid rent owed
    const string KeyNextDue  = "tevRentNextDueDay";   // counter — GalaxyTime day the next bill lands
    const string KeyLawnOwed = "tevLawnTapesOwed";    // counter — HIS tapes still to sell to clear the lawn
    const string FlagLawnDone= "tevLawnCleared";      // flag — the lawn is paid off for good

    /// Mushrooms in a batch Tev hands over.
    public const int BatchSize = 3;

    // ── The lawn, paid in tape sales ─────────────────────────────────────
    //
    // The cassette pivot retires the weekly money rent. Tev knows the player is
    // broke, so he makes them work it off: sell N of HIS tapes and the lawn is
    // settled, once, forever. The haggle is over N — 10 / 8 / 5 / 3 — and it
    // never reaches zero, so the stubborn haggler carries the lightest load
    // rather than getting it free.
    //
    // The money rent system is NOT deleted. Settling calls SettleRent(0), and
    // TevRentCollector already early-returns on a rate of 0 ("Tev waived it"),
    // so no weekly charge can ever fire. Everything about it stays one line
    // away from coming back.

    /// The four rungs of the work-off haggle. Never reaches free.
    public static readonly int[] LawnTapeRungs = { 10, 8, 5, 3 };

    /// How many of Tev's tapes are still to be sold before the lawn is square.
    public static int LawnTapesOwed
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyLawnOwed) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyLawnOwed, Mathf.Max(0, value)); }
    }

    /// True once the debt has been worked off. Distinct from "owes 0" — a
    /// player who has not been through the haggle also owes 0.
    public static bool LawnCleared
    {
        get => StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(FlagLawnDone);
        set { StoryDirector.Instance?.SetFlag(FlagLawnDone, value); }
    }

    /// <summary>
    /// Lock in the haggled tape count. Also settles the MONEY rent at zero, so
    /// the weekly collector stays permanently quiet without anything being
    /// deleted — see the note above.
    /// </summary>
    public static void SettleLawn(int tapes)
    {
        LawnTapesOwed = Mathf.Max(0, tapes);
        LawnCleared = tapes <= 0;
        SettleRent(0);
    }

    /// <summary>
    /// Called when one of TEV'S tapes is sold. Counts down the lawn debt and
    /// returns true on the sale that clears it, so the caller can say so.
    /// </summary>
    public static bool NotifyTevTapeSold(int count = 1)
    {
        if (count <= 0 || LawnCleared) return false;
        int before = LawnTapesOwed;
        if (before <= 0) return false;
        LawnTapesOwed = before - count;
        if (LawnTapesOwed > 0) return false;
        LawnCleared = true;
        return true;
    }
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

    // ── Lawn rent (the Tev haggle) ───────────────────────────────────────
    //
    // Tev opens at 500/week for the shuttle on his lawn, drops to 100 if you
    // push back, and waives it entirely if you push back twice. The amount is
    // whatever the player talked him down to; 0 is a legitimate settled value,
    // which is why "has this been negotiated at all" needs its own flag rather
    // than being inferred from the counter.

    /// True once the player has been through the rent haggle. A counter of 0
    /// means FREE, not "unasked" — don't infer settlement from the amount.
    public static bool RentSettled
    {
        get => StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(FlagRentSet);
        set { StoryDirector.Instance?.SetFlag(FlagRentSet, value); }
    }

    /// Credits owed per galactic week. 0 = Tev waived it.
    public static int RentPerWeek
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyRent) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyRent, Mathf.Max(0, value)); }
    }

    /// Rent the player owed but couldn't cover. Accrues; never evicts.
    public static int RentArrears
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyArrears) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyArrears, Mathf.Max(0, value)); }
    }

    /// GalaxyTime day number the next rent bill falls due on. 0 = not scheduled
    /// (either the haggle hasn't happened or Tev waived the rent).
    public static int RentNextDueDay
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyNextDue) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyNextDue, Mathf.Max(0, value)); }
    }

    /// Lock in the negotiated rate and schedule the first bill one full week
    /// out, so the player never gets charged on the day they land.
    public static void SettleRent(int perWeek)
    {
        RentPerWeek = Mathf.Max(0, perWeek);
        RentSettled = true;
        RentArrears = 0;
        int today = GalaxyTime.Instance != null ? GalaxyTime.Instance.Day : 1;
        RentNextDueDay = perWeek > 0 ? today + GalaxyTime.DaysPerWeek : 0;
    }

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
