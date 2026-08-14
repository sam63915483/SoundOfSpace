/// <summary>
/// What a tape is worth to a particular alien.
///
/// The handoff §6 formula, kept in one place so the number Tev quotes, the
/// number an alien will go up to, and the number a text order names can never
/// drift apart:
///
///   value = (10 + 8*activeModules)
///         * tapeMult(T1 1.0, T2 1.5)
///         * (0.4 + 0.9 * sat/100)
///         * bondMult(1.0 .. 1.4)
///         * requestBonus(1.25 on a match)
///         * payFactor
///
/// ── Read the shape before turning the knobs ──────────────────────────────
/// activeModules is the ONLY term the player controls by spending money, which
/// is what makes Tev's $200 plugins feel like an investment rather than a
/// cosmetic. Satisfaction is the term they control by getting better at the
/// instrument. Everything else is who they happen to be talking to.
///
/// The satisfaction term bottoms out at 0.4 rather than 0, on purpose: a tape
/// somebody merely tolerates is still worth something, and a floor of zero
/// would make a bad match feel like a bug.
///
/// ⚠️ EARLY-GAME MARGIN IS THIN AND THAT IS KNOWN. Two modules at low
/// satisfaction lands near the $10 a blank costs. Sam has the numbers; this is
/// the file to retune.
///
/// PURE — no Unity types, so it runs headlessly with the taste model.
/// </summary>
public static class TapeValue
{
    public const double Floor = 10.0;
    public const double PerModule = 8.0;

    public const double TierOneMult = 1.0;
    public const double TierTwoMult = 1.5;

    public const double SatFloor = 0.4;      // what a barely-tolerated tape keeps
    public const double SatRange = 0.9;

    public const double BondMultMin = 1.0;
    public const double BondMultMax = 1.4;
    public const int BondMaxForMult = 100;

    public const double RequestBonus = 1.25;

    /// The base a tape is worth before anyone hears it: floor plus arrangement.
    public static double Base(int activeModules, int tier)
    {
        double t = tier >= 2 ? TierTwoMult : TierOneMult;
        return (Floor + PerModule * activeModules) * t;
    }

    public static double SatisfactionMult(double satisfaction)
    {
        double s = satisfaction;
        if (s < 0) s = 0;
        if (s > 100) s = 100;
        return SatFloor + SatRange * (s / 100.0);
    }

    /// Bond 0..100 maps to 1.0 .. 1.4. A regular pays more because they are a
    /// regular, not because the tape got better.
    public static double BondMult(int bond)
    {
        double b = bond;
        if (b < 0) b = 0;
        if (b > BondMaxForMult) b = BondMaxForMult;
        return BondMultMin + (BondMultMax - BondMultMin) * (b / (double)BondMaxForMult);
    }

    /// <summary>
    /// The full figure: what THIS alien thinks this tape is worth right now.
    /// The negotiation ceiling is built from this, not from a stored price.
    /// </summary>
    public static int For(int activeModules, int tier, double satisfaction,
                          int bond, bool matchesRequest, double payFactor)
    {
        double v = Base(activeModules, tier)
                 * SatisfactionMult(satisfaction)
                 * BondMult(bond)
                 * (matchesRequest ? RequestBonus : 1.0)
                 * payFactor;
        int rounded = (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
        return rounded < 1 ? 1 : rounded;
    }

    /// <summary>
    /// Their opening thought, before you name a number. They lowball a little —
    /// that is what leaves room for the player to ask for more, which is the
    /// entire negotiation.
    /// </summary>
    public static int OpeningThought(int fullValue)
    {
        int v = (int)System.Math.Round(fullValue * 0.9, System.MidpointRounding.AwayFromZero);
        return v < 1 ? 1 : v;
    }

    /// <summary>
    /// The most they will go to. Above this they either counter or walk.
    /// Patience is per-alien so two customers holding identical tapes still
    /// negotiate differently.
    /// </summary>
    public static int Ceiling(int fullValue, double patience)
    {
        int v = (int)System.Math.Round(fullValue * patience, System.MidpointRounding.AwayFromZero);
        return v < 1 ? 1 : v;
    }

    /// <summary>
    /// The take-it-or-leave-it a greedy ask provokes. DELIBERATELY BELOW what
    /// they would have paid — pushing too hard has to cost something, or there
    /// is no reason ever to name a fair price.
    /// </summary>
    public static int FinalOffer(int fullValue)
    {
        int v = (int)System.Math.Round(fullValue * 0.6, System.MidpointRounding.AwayFromZero);
        return v < 1 ? 1 : v;
    }
}
