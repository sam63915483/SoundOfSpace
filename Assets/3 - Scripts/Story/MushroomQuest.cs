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
    // ⚠️ VAULTED 2026-08-14 — FeatureVault.TevLawnWorkOff. The rent revamp put
    // the daily money rent back in its place. Nothing here is deleted and the
    // counters stay in the schema; SettleLawn still silences the rent by
    // settling it at a rate of 0, which is exactly what a restored work-off
    // haggle would want.

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

    // ── Lawn rent — DAILY, and never free ────────────────────────────────
    //
    // Reactivated 2026-08-14 (Handoff_RentRevamp_PhysicalPrint_v1) as the money
    // pressure the cassette loop runs on. Three rules, all Sam's, all load-
    // bearing:
    //
    //   1. PER GAME DAY, not per week. A GalaxyTime day is 24 real minutes, so
    //      the bill lands roughly every 24 minutes of play — often enough that
    //      a player who ignores it feels it inside one session.
    //   2. THE FLOOR IS $10 AND IT NEVER REACHES ZERO. Haggling rent away would
    //      delete the pressure the whole loop is built on, so the last rung has
    //      no refusal row (same shape the lawn work-off used).
    //   3. ARREARS STACK LINEARLY. owed = rate × unpaid days. No compounding,
    //      no interest, ever.
    //
    // NOTHING IS AUTO-DEDUCTED. The balance only moves when the player walks up
    // to Tev and hands money over through TevPaymentUI. That is what gives the
    // 5-day plugin lockout teeth: you can be rich and still locked out for
    // ignoring your landlord, which an auto-deducting collector could never do.
    //
    // World state, not per player: in co-op there is one lawn and one ledger.
    // StoryDirector counters are world-scoped, so this is household-shared for
    // free — either player can pay it down, and the lockout hits both.

    /// The four rungs of the rent haggle, in credits PER GAME DAY. The last one
    /// is the floor and has no way out — see rule 2 above.
    public static readonly int[] RentRungs = { 50, 30, 20, 10 };

    /// Unpaid days that trigger Tev's plugin embargo.
    public const int LockoutDays = 5;

    /// True once the player has been through the rent haggle. A rate of 0 means
    /// "never negotiated", not "free" — the ladder cannot land on zero.
    public static bool RentSettled
    {
        get => StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(FlagRentSet);
        set { StoryDirector.Instance?.SetFlag(FlagRentSet, value); }
    }

    /// Credits owed per galactic DAY, as haggled. (Key name still says "week" —
    /// it is a save key, and renaming it would orphan existing saves for no
    /// gain.)
    public static int RentPerDay
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyRent) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyRent, Mathf.Max(0, value)); }
    }

    /// Everything currently owed. Grows by RentPerDay each game day, shrinks
    /// only when the player pays. Never evicts.
    public static int RentBalance
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyArrears) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyArrears, Mathf.Max(0, value)); }
    }

    /// The last GalaxyTime day that has been billed. Stored rather than derived
    /// so a save that skipped several days bills each of them exactly once.
    public static int RentLastBilledDay
    {
        get => StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeyNextDue) : 0;
        set { StoryDirector.Instance?.SetCounter(KeyNextDue, Mathf.Max(0, value)); }
    }

    /// How many days' rent is outstanding, rounded UP: a partial payment that
    /// leaves $1 on a $10 rate is still a day in arrears, which is the honest
    /// reading of "you haven't paid for that day".
    public static int UnpaidDays
    {
        get
        {
            int rate = RentPerDay;
            if (rate <= 0) return 0;
            return (RentBalance + rate - 1) / rate;
        }
    }

    /// Tev's embargo. Plugins only — blanks are ALWAYS purchasable, because the
    /// loop must never be able to soft-lock. The ladder freezes; the treadmill
    /// doesn't.
    public static bool PluginsLocked => UnpaidDays >= LockoutDays;

    /// <summary>
    /// Lock in the negotiated daily rate. Rent accrues FROM THE CONFRONTATION —
    /// today is marked billed, so the first charge lands on the next day roll,
    /// which is what makes the three gift blanks genuinely free while still
    /// starting the clock.
    /// </summary>
    public static void SettleRent(int perDay)
    {
        RentPerDay = Mathf.Max(0, perDay);
        RentSettled = true;
        RentBalance = 0;
        RentLastBilledDay = GalaxyTime.Instance != null ? GalaxyTime.Instance.Day : 1;
    }

    /// <summary>
    /// Bill every day that has elapsed since the last one charged, and return
    /// how much was added. Linear: three missed days at $10 is $30, full stop.
    ///
    /// Safe to call repeatedly and safe to call after a long absence — the
    /// last-billed day is advanced first, so no day is ever billed twice.
    /// </summary>
    public static int AccrueRentTo(int day)
    {
        if (!RentSettled) return 0;
        int rate = RentPerDay;
        if (rate <= 0) return 0;

        int last = RentLastBilledDay;
        if (last <= 0) { RentLastBilledDay = day; return 0; }
        if (day <= last) return 0;

        int days = day - last;
        RentLastBilledDay = day;
        int charge = rate * days;
        RentBalance += charge;
        return charge;
    }

    /// <summary>
    /// Pay Tev. Returns what actually came out of the wallet — capped at the
    /// balance, so the player can never overpay their way into credit.
    ///
    /// ── Two halves, two owners (co-op) ───────────────────────────────────
    /// The MONEY is personal: it leaves the wallet of whoever walked up to Tev,
    /// immediately, on their own machine. The BALANCE is world state owned by
    /// the host, because both players pay down one household debt and a guest
    /// subtracting locally would be overwritten by the next snapshot — the
    /// payment would appear to bounce.
    ///
    /// So the wallet is charged here and the balance is asked for over there.
    /// In single player and on the host the route is a no-op and both halves
    /// happen inline, exactly as before.
    /// </summary>
    public static int PayRent(int amount)
    {
        if (amount <= 0) return 0;
        int pay = Mathf.Min(amount, RentBalance);
        if (pay <= 0) return 0;

        var wallet = PlayerWallet.Instance;
        if (wallet == null || !wallet.SpendMoney(pay)) return 0;

        if (!TraxSync.RouteRentPay(pay)) ApplyRentPayment(pay);
        return pay;
    }

    /// <summary>
    /// The balance half of a payment, with no wallet involved — the host runs
    /// this for a guest whose money has already changed hands. Never charges
    /// anyone, so it is safe to call on a machine that isn't paying.
    /// </summary>
    public static void ApplyRentPayment(int amount)
    {
        if (amount <= 0) return;
        RentBalance = Mathf.Max(0, RentBalance - amount);
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
