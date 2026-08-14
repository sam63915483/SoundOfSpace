/// <summary>
/// One sale, start to finish. The rules an alien follows when you hold a tape
/// out to them.
///
///   offer -> they LISTEN to it -> like gate -> "how much?" -> you name a price
///   -> accept / final offer / walk
///
/// This is the ONE interaction for every in-person sale: your tapes, Tev's
/// fronted tapes, and (Phase 5) handing over a tape somebody ordered by text.
/// One flow means one place where the feel lives.
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
    {
        satisfaction = AlienTaste.Satisfaction(alienId, dials);

        // Checked BEFORE taste: being played the same song twice is a social
        // failure, not a musical one, and they notice it whether they liked it
        // the first time or not.
        if (TapeMemory.HasHeard(alienId, dials)) return Reaction.AlreadyHeard;

        AlienTaste.Verdict v = AlienTaste.Gate(satisfaction);
        if (v == AlienTaste.Verdict.Liked) return Reaction.Liked;
        if (v == AlienTaste.Verdict.CoinFlip) return coinFlip ? Reaction.Liked : Reaction.Rejected;
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
        return TapeValue.For(activeModules, tier, satisfaction,
                             bond, matchesRequest,
                             AlienTaste.PayFactor(alienId));
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
