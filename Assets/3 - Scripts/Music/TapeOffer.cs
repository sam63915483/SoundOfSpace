/// <summary>
/// One sale, start to finish. The rules an alien follows when you hold a tape
/// out to them.
///
///   offer -> they LISTEN to it -> like gate -> "how much?" -> you name a price
///   -> accept / final offer / walk
///
/// This is the ONE interaction for every in-person WALK-UP sale, and as of
/// 2026-08-16 the sell panel actually routes through it (it spent a while as
/// a documented-but-uncalled rulebook while the panel ran a harsher copy —
/// the review caught it). Text-order deliveries are graded by their own pure
/// rulebook, TapeDeal.Grade, against the recorded DealTerms.
///
/// ── Greed does not end the deal, it costs you ────────────────────────────
/// Push too far and they do not simply walk. They issue a FINAL OFFER,
/// deliberately below what they would have paid — take it or leave it. That is
/// deliberately more interesting than a failure state: the player still gets a
/// decision, and the punishment is legible ("I got greedy and it cost me")
/// rather than a dead end. Refusing still earns the contact, because they liked
/// the song; it just costs bond, because you wasted their time.
///
/// PURE. The caller supplies the coin flip and applies the outcome, so this
/// runs headlessly.
/// </summary>
public static class TapeOffer
{
    // ── bond movements ───────────────────────────────────────────────────
    public const int BondOnSale = 8;
    public const int BondOnGenerousDeal = 3;    // extra when you asked under their value
    public const int BondOnRepeatOffer = -6;    // playing them the same song again
    public const int BondOnRefusedFinal = -4;   // you walked from their last word

    /// How far over their ceiling an ask can be before it reads as greed
    /// rather than as haggling.
    public const double GreedMultiplier = 1.35;

    public enum Reaction { Rejected, Liked, AlreadyHeard }

    public enum Response { Accepted, FinalOffer, TooLow }

    /// <summary>
    /// What happens when the tape goes in their hand. <paramref name="coinFlip"/>
    /// decides the middle satisfaction band — the caller owns randomness so this
    /// stays deterministic and testable.
    /// </summary>
    public static Reaction Listen(string alienId, double[] dials, bool coinFlip,
                                  out double satisfaction)
        => Listen(alienId, dials, 1, coinFlip, out satisfaction);

    /// Tier-aware listen: a shell-preference mismatch (a Type 1 handed to a
    /// Type 2 snob) downgrades the verdict one step via GateFor's overload.
    public static Reaction Listen(string alienId, double[] dials, int tapeTier, bool coinFlip,
                                  out double satisfaction)
        => Listen(alienId, dials, tapeTier, coinFlip, out satisfaction, out _);

    /// <param name="verdict">The RAW taste verdict, exposed so the caller can
    /// distinguish an outright rejection (burns the song into TapeMemory)
    /// from a lost coin flip (does not — bad luck must never permanently burn
    /// a song on a buyer).</param>
    public static Reaction Listen(string alienId, double[] dials, int tapeTier, bool coinFlip,
                                  out double satisfaction, out AlienTaste.Verdict verdict)
    {
        satisfaction = AlienTaste.Satisfaction(alienId, dials);
        verdict = AlienTaste.Verdict.Rejected;

        // Checked BEFORE taste: being played the same song twice is a social
        // failure, not a musical one, and they notice it whether they liked it
        // the first time or not.
        if (TapeMemory.HasHeard(alienId, dials)) return Reaction.AlreadyHeard;

        // GateFor, not Gate: an on-genre tape is never refused by that genre's
        // fan — the hint contract (see AlienTaste.GateFor).
        verdict = AlienTaste.GateFor(alienId, dials, satisfaction, tapeTier);
        if (verdict == AlienTaste.Verdict.Liked) return Reaction.Liked;
        if (verdict == AlienTaste.Verdict.CoinFlip) return coinFlip ? Reaction.Liked : Reaction.Rejected;
        return Reaction.Rejected;
    }

    /// <summary>
    /// What this tape is worth to this alien right now — the number every other
    /// figure in the negotiation is built from.
    /// </summary>
    /// <param name="bond">Passed IN rather than looked up: bond lives on
    /// BuyerLedger, which is a Unity class, and reaching for it from here would
    /// cost this file the ability to be run headlessly.</param>
    public static int Value(string alienId, int activeModules, int tier,
                            double satisfaction, bool matchesRequest, int bond)
    {
        // TierPayFactor: a mismatched shell (Type 1 to a Type 2 snob, or the
        // pricey Type 2 to a dedicated cheapskate) is worth less TO THEM.
        return TapeValue.For(activeModules, tier, satisfaction,
                             bond, matchesRequest,
                             AlienTaste.PayFactor(alienId)
                             * AlienTaste.TierPayFactor(alienId, tier));
    }

    /// The most they will pay without complaint. Patience is per-alien, so two
    /// customers holding the same tape still negotiate differently.
    public static int Ceiling(string alienId, int value)
    {
        return TapeValue.Ceiling(value, AlienTaste.Patience(alienId));
    }

    /// <summary>
    /// They have asked how much. This is what they think of your number.
    ///
    ///   at or under the ceiling      -> Accepted, at YOUR price
    ///   over it but not outrageous   -> TooLow: they counter, deal still alive
    ///   outrageous                   -> FinalOffer: take it or leave it
    /// </summary>
    public static Response Judge(string alienId, int value, int ask, out int counter)
    {
        int ceiling = Ceiling(alienId, value);
        counter = 0;

        if (ask <= ceiling) { counter = ask; return Response.Accepted; }

        if (ask <= (int)System.Math.Round(ceiling * GreedMultiplier))
        {
            // Meet in the middle, capped at what they will actually pay.
            int mid = (TapeValue.OpeningThought(value) + ask) / 2;
            counter = mid > ceiling ? ceiling : mid;
            if (counter < 1) counter = 1;
            return Response.TooLow;
        }

        counter = TapeValue.FinalOffer(value);
        return Response.FinalOffer;
    }

    /// <summary>
    /// Bond earned by a completed sale. Asking UNDER their value is rewarded —
    /// it is the only way a player can choose to be generous, and a regular who
    /// pays more later is the payoff.
    /// </summary>
    public static int BondForSale(int value, int paid)
    {
        int bond = BondOnSale;
        if (paid < value) bond += BondOnGenerousDeal;
        return bond;
    }
}
