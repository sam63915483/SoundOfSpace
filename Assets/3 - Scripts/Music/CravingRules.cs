/// <summary>
/// CRAVING — the flywheel (loop-feel pass C). Every buyer carries a hidden
/// 0..100 hunger for new music: it feeds when you sell them something they
/// like, decays when you ignore them, and drives how hard the world pursues
/// the player — want-text frequency, the guaranteed daily order at the top,
/// and the ambush walk-up. Canon: everyone knows the black hole is coming,
/// so obsession with music is universal and respectable.
///
/// THE ONE HARD RULE: craving is DEMAND, never a gate. It must not block,
/// discount or upgrade any sale. Bond stays the trust/price stat; craving is
/// the hunger/frequency stat.
///
/// All numbers here are tuning targets, not promises (handoff C, Sam).
/// PURE: no Unity types, runs in the headless suite.
/// </summary>
public static class CravingRules
{
    public const int Cap = 100;

    // ── gain on a completed sale, by the satisfaction ladder band ────────
    // (AlienFeedback.SatBand: 0 junk · 1 not-for-me · 2 decent · 3 love it ·
    //  4 MASTERPIECE)
    public const int GainMasterpiece = 18;
    public const int GainLove = 12;
    public const int GainDecent = 7;
    public const int GainBelow = 3;
    /// Extra on top when the sale filled a NAMED request (loop-feel D).
    public const int GainNamedRequest = 4;
    /// A rejected-but-heard listen still fed the hunger a little — they got
    /// music out of you even if they didn't buy it.
    public const int GainHeardOnly = 2;

    /// End-of-day decay when that buyer bought nothing all day.
    public const int DecayPerIdleDay = 8;

    // ── thresholds ───────────────────────────────────────────────────────
    public const int AmbushThreshold = 60;          // "hooked"
    public const int GuaranteedOrderThreshold = 90; // "obsessed"

    // ── want-text cadence ────────────────────────────────────────────────
    // The base delay between a completed deal and the next want-text, divided
    // by the frequency multiplier below. (There was no post-deal pacing rule
    // before this pass — a comment claimed bond scaled it; nothing did.)
    public const float BaseDelayMinSeconds = 300f;
    public const float BaseDelayMaxSeconds = 480f;
    public const double FreqMultMin = 1.0;
    public const double FreqMultMax = 2.5;

    public static int Gain(int satBand, bool namedRequest)
    {
        int g = satBand >= 4 ? GainMasterpiece
              : satBand == 3 ? GainLove
              : satBand == 2 ? GainDecent
              : GainBelow;
        if (namedRequest) g += GainNamedRequest;
        return g;
    }

    public static int Clamp(int craving)
        => craving < 0 ? 0 : craving > Cap ? Cap : craving;

    public static int AfterIdleDay(int craving)
        => Clamp(craving - DecayPerIdleDay);

    /// 1.0 at craving 0 → 2.5 at 100. DIVIDES the base want-text delay.
    public static double FrequencyMult(int craving)
    {
        double t = Clamp(craving) / (double)Cap;
        return FreqMultMin + (FreqMultMax - FreqMultMin) * t;
    }

    /// Eligible to come find the player: hooked, and at least one full day
    /// since they last bought anything (lastPurchaseDay 0 = never bought —
    /// can't crave what you've never had, so not eligible).
    public static bool AmbushEligible(int craving, int lastPurchaseDay, int today)
        => craving >= AmbushThreshold
        && lastPurchaseDay > 0
        && today - lastPurchaseDay >= 1;

    // ── the 4-word craving ladder (contact card; no numbers anywhere) ────
    // DRAFT words, Sam edits.
    static readonly string[] Words = { "curious", "interested", "hooked", "obsessed" };
    static readonly int[] Cuts = { 20, AmbushThreshold, GuaranteedOrderThreshold };

    public static int LadderBand(int craving)
    {
        int b = 0;
        for (int i = 0; i < Cuts.Length; i++) if (Clamp(craving) >= Cuts[i]) b++;
        return b;
    }

    public static string LadderWord(int craving) => Words[LadderBand(craving)];
}
