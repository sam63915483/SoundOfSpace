/// <summary>
/// The tape-selling career: one world counter and the two shop milestones it
/// unlocks. StoryDirector-backed like MushroomQuest's rent, so it saves,
/// replicates and resets with zero schema.
///
/// PURE in the MushroomQuest sense — compiles against the headless
/// StoryDirector stub, so verify-rent executes the milestone maths.
/// </summary>
public static class TapeCareer
{
    const string KeySold = "tapesSoldTotal";

    /// Total tapes ever sold in this world (walk-ups + deliveries; both
    /// players' sales in co-op). Incremented in BuyerLedger.ReportTapeDeal —
    /// the one choke point both paths and routed guest sales pass through.
    public static int TapesSold
    {
        get { return StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeySold) : 0; }
        set { if (StoryDirector.Instance != null) StoryDirector.Instance.SetCounter(KeySold, value < 0 ? 0 : value); }
    }

    public static bool HalfUnlocked { get { return TapesSold >= TraxKind.HalfUnlockSales; } }
    public static bool FullUnlocked { get { return TapesSold >= TraxKind.FullUnlockSales; } }

    /// The biggest format Tev currently stocks.
    public static int UnlockedKind()
    {
        return FullUnlocked ? TraxKind.Full : HalfUnlocked ? TraxKind.Half : TraxKind.Demo;
    }
}
