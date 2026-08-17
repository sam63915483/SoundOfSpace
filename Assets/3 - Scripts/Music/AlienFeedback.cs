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

    // ── The satisfaction word ladder (loop-feel pass A1) ─────────────────
    //
    // ONE five-word vocabulary for "how much did they like it", used by every
    // player-facing surface — the listen reaction, delivery results, the
    // craving maths. Players learn one scale. The bottom two cuts are the
    // taste gates themselves (LikeMaybe / LikeCertain) so the words can never
    // disagree with the verdict; the top two are presentation-only.
    //
    // DRAFT words — Sam's edit pass applies, same as every string here.
    public const double SatCutFlip  = AlienTaste.LikeMaybe;    // 42
    public const double SatCutLiked = AlienTaste.LikeCertain;  // 60
    public const double SatCutLove  = 78.0;
    public const double SatCutPeak  = 92.0;

    /// 0 junk · 1 not-for-me (the flip band) · 2 decent · 3 love it · 4 MASTERPIECE
    public static int SatBand(double s)
    {
        if (s >= SatCutPeak) return 4;
        if (s >= SatCutLove) return 3;
        if (s >= SatCutLiked) return 2;
        if (s >= SatCutFlip) return 1;
        return 0;
    }

    static readonly string[] SatWords = { "junk", "not for me", "decent", "love it", "MASTERPIECE" };

    /// The bare ladder word for a band (see SatBand).
    public static string SatWord(int band)
        => SatWords[band < 0 ? 0 : band >= SatWords.Length ? SatWords.Length - 1 : band];

    /// <summary>
    /// Said on the way into the price question, scaled by how much they liked
    /// it. Every line carries its ladder word so the player learns the scale.
    /// <paramref name="wonCoinFlip"/>: a flip-band tape they happened to take —
    /// the SALE was luck but the spoken word stays honest.
    /// </summary>
    public static string ForLiked(double satisfaction, uint variant)
        => ForLiked(satisfaction, false, variant);

    public static string ForLiked(double satisfaction, bool wonCoinFlip, uint variant)
    {
        if (wonCoinFlip || SatBand(satisfaction) <= 1)
        {
            string[] flip = { "Not really for me... but fine, I'll take it. How much?",
                              "Hm. Not my thing, exactly. But go on — how much?" };
            return flip[variant % (uint)flip.Length];
        }
        switch (SatBand(satisfaction))
        {
            case 4:
            {
                string[] bank = { "A MASTERPIECE. Where did you GET this?",
                                  "Oh, that's the one. A MASTERPIECE. Name a price." };
                return bank[variant % (uint)bank.Length];
            }
            case 3:
            {
                string[] bank = { "Love it. How much?",
                                  "Yeah — love it, I'd play that. How much?" };
                return bank[variant % (uint)bank.Length];
            }
            default:
            {
                string[] bank = { "Decent, I guess. What do you want for it?",
                                  "That's decent. Go on then — how much?" };
                return bank[variant % (uint)bank.Length];
            }
        }
    }

    /// Their verdict spoken AFTER a delivery is paid — no price question, just
    /// the ladder word, which is what makes taste legible on the order path.
    public static string AfterListen(double satisfaction, uint variant)
    {
        switch (SatBand(satisfaction))
        {
            case 4:
            {
                string[] bank = { "This is a MASTERPIECE.", "A MASTERPIECE. I mean it." };
                return bank[variant % (uint)bank.Length];
            }
            case 3:
            {
                string[] bank = { "I love this.", "Love it. Exactly right." };
                return bank[variant % (uint)bank.Length];
            }
            case 2:
            {
                string[] bank = { "It's decent.", "Decent. I'll play it." };
                return bank[variant % (uint)bank.Length];
            }
            default:
            {
                string[] bank = { "...not really for me. But a deal's a deal.",
                                  "Not my thing. Still — we had a deal." };
                return bank[variant % (uint)bank.Length];
            }
        }
    }

    /// Said when a thin arrangement is what's dragging the money down — the
    /// alien names the problem in-world instead of a UI caption doing it
    /// (loop-feel pass A3). DRAFT lines, Sam edits.
    public static string ForThinKit(uint variant)
    {
        string[] bank =
        {
            "Sounds empty. Needs more machines in it.",
            "Where's the rest of it? This is half a band.",
            "One box and a drum. I can hear the gaps.",
            "Thin. You've got more gear than this, I've heard it.",
            "It's a sketch, not a song. Come back when it's crowded.",
        };
        return bank[variant % (uint)bank.Length];
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
