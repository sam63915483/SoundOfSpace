/// <summary>
/// What an alien says when they hand the tape back.
///
/// ── The lesson has to be actionable ──────────────────────────────────────
/// The line leads with the DIAL, because that is something the player can walk
/// to the console and turn immediately. Their nearest genre comes second: it is
/// useful colour, but "I'm more of a GLORP guy" only helps once you know where
/// GLORP sits in the space, which is knowledge the player does not have on
/// their first rejection.
///
/// Values are computed, wording is authored. DRAFT — Sam's cut pass applies to
/// every string in this file, same as Tev's.
///
/// PURE: no Unity types, so it runs headlessly with the rest of the model.
/// </summary>
public static class AlienFeedback
{
    /// Dial names as the player sees them on the console.
    public static readonly string[] DialNames =
        { "PULSE", "CRUNCH", "GOO", "VOID", "JITTER", "WARP" };

    /// Below this the gap is not worth mentioning — naming a dial that is
    /// nearly right sends the player to move something that was fine.
    public const double MentionThreshold = 1.5;

    /// Above this the miss is glaring enough to be worth stronger wording.
    public const double StrongThreshold = 5.0;

    // Draft phrasings. Two registers so a rejection does not read identically
    // every time; the caller picks by how big the gap is.
    static readonly string[] WantMoreMild   = { "could do with more {d}", "bit more {d} and maybe" };
    static readonly string[] WantMoreStrong = { "nowhere near enough {d}", "where's the {d}?" };
    static readonly string[] WantLessMild   = { "little heavy on the {d}", "ease off the {d}" };
    static readonly string[] WantLessStrong = { "way too much {d}", "drowning in {d}" };

    static string Phrase(int dial, bool wantMore, double gap, uint pick)
    {
        string[] bank = wantMore
            ? (gap >= StrongThreshold ? WantMoreStrong : WantMoreMild)
            : (gap >= StrongThreshold ? WantLessStrong : WantLessMild);
        string name = (dial >= 0 && dial < DialNames.Length) ? DialNames[dial] : "IT";
        return bank[pick % (uint)bank.Length].Replace("{d}", name);
    }

    /// <summary>
    /// The rejection line. <paramref name="variant"/> just picks between
    /// phrasings — pass anything stable per-offer so the same alien does not
    /// reword the same complaint every frame.
    /// </summary>
    public static string ForRejection(string alienId, double[] dials, string nearestGenre,
                                      uint variant)
    {
        bool moreA, moreB;
        double gapA, gapB;
        int first = AlienTaste.BiggestGap(alienId, dials, out moreA, out gapA);

        if (first < 0 || gapA < MentionThreshold)
        {
            // Close on every dial and still refused: there is nothing honest to
            // point at, so do not invent a fault.
            return "Not for me. Close, though.";
        }

        string line = Phrase(first, moreA, gapA, variant);

        int second = AlienTaste.SecondGap(alienId, dials, first, out moreB, out gapB);
        if (second >= 0 && gapB >= MentionThreshold)
            line += ", and " + Phrase(second, moreB, gapB, variant + 1);

        if (!string.IsNullOrEmpty(nearestGenre))
            line += ". I'm more of a " + nearestGenre + " listener.";
        else
            line += ".";

        return char.ToUpperInvariant(line[0]) + line.Substring(1);
    }

    /// <summary>
    /// Tier-aware rejection: same line as above, plus a shell-preference note
    /// when the tape's tier sits wrong with this buyer — so "why didn't they
    /// want it" is never a mystery when the tier contributed (Sam's rule:
    /// they must be able to TELL you they prefer Type 1 or Type 2).
    /// </summary>
    public static string ForRejection(string alienId, double[] dials, string nearestGenre,
                                      uint variant, int tapeTier)
    {
        string line = ForRejection(alienId, dials, nearestGenre, variant);
        if (AlienTaste.TierMismatch(alienId, tapeTier))
        {
            line += AlienTaste.TierPreference(alienId) > 0
                ? " And I only really rate Type 2 tapes."
                : " And Type 2s cost too much — I stick to Type 1s.";
        }
        return line;
    }

    /// Said when they are handed a song they have already been played.
    public static string ForRepeat(uint variant)
    {
        string[] bank =
        {
            "You've played me this one.",
            "This again? I remember it.",
            "Same song. I'm not deaf.",
        };
        return bank[variant % (uint)bank.Length];
    }

    /// Said on the way into the price question, scaled by how much they liked it.
    public static string ForLiked(double satisfaction, uint variant)
    {
        if (satisfaction >= 85)
        {
            string[] bank = { "Where did you get this?", "Oh, that's the one. How much?" };
            return bank[variant % (uint)bank.Length];
        }
        if (satisfaction >= 65)
        {
            string[] bank = { "Yeah. Yeah, I'd play that.", "That'll do nicely. How much?" };
            return bank[variant % (uint)bank.Length];
        }
        string[] mild = { "It's alright. What do you want for it?", "I could live with that one." };
        return mild[variant % (uint)mild.Length];
    }

    /// Said when a greedy ask provokes the take-it-or-leave-it.
    public static string ForFinalOffer(int amount, uint variant)
    {
        string[] bank =
        {
            "Don't try it. " + amount + ", and that's me being kind now.",
            "You're having a laugh. " + amount + ". Take it or don't.",
        };
        return bank[variant % (uint)bank.Length];
    }
}
