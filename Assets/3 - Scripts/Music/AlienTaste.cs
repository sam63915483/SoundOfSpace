/// <summary>
/// What an alien wants to hear, and how much they'll pay for it.
///
/// ── One number does all the work ─────────────────────────────────────────
/// Every alien is a POINT in the same six-dial space the classifier uses.
/// Satisfaction is distance from that point. Whether they like a tape, what
/// they'll pay, what they complain about, and what they text you asking for
/// later are all read off that one figure — so there is no per-alien authored
/// content anywhere, and a track the player made is graded by exactly the same
/// rule as one of Tev's.
///
/// ── Derived, never stored (Sam's call, 2026-08-13) ───────────────────────
/// Taste comes from hashing the alien's stable identity, the same trick
/// NPCMushroomPrice already uses for prices. That means it costs zero save
/// schema, survives streaming out at 300m and back, is identical on both
/// machines in co-op with nothing on the wire, and an alien who loves SLUDJ
/// loves SLUDJ for the life of that world. Rolling it at spawn would have
/// needed all of that solved and would still have re-rolled everyone's taste
/// on reload — silently invalidating the player's mental map of who buys what,
/// which is the single most valuable thing they build up.
///
/// Each trait is salted separately so two traits can never correlate: an alien
/// with a fussy ear is not therefore a generous payer.
///
/// PURE. No Unity types, no clock, no randomness — so it can be executed
/// headlessly (test/verify-taste.py) rather than only compiled.
/// </summary>
public static class AlienTaste
{
    public const int DialCount = 6;

    // ── Tuning. Every one of these is a knob Sam turns by ear. ───────────

    /// <summary>
    /// Falloff range. LOW = broad listener; HIGH = fussy.
    ///
    /// ── The floor was 0.55 and that was the nine-for-nine bug ────────────
    /// An in-game report over 23 real aliens confirmed the model was working:
    /// 37% of offers accepted, matching the headless prediction, with 23 of 23
    /// distinct identities. The AVERAGE was never the problem. The DISTRIBUTION
    /// was — a third of all aliens would accept 9 or more of the 10 genre
    /// archetypes. That is not a broad listener, it is a vending machine, and
    /// meeting three of them in a row is exactly what nine sales out of nine
    /// looks like from the player's side.
    ///
    /// Measured, sweeping the floor (fans always buy their own genre):
    ///   0.55  others 42%   pushovers 33%   <- shipped, and wrong
    ///   0.70  others 35%   pushovers 22%
    ///   0.80  others 30%   pushovers 15%
    ///   0.90  others 25%   pushovers  7%   <- here
    ///   1.00  others 21%   pushovers  3%
    ///
    /// 0.90 kills the archetype without making everyone fussy: 7% still buy
    /// almost anything, which is a nice thing to stumble across, and 18% buy
    /// almost nothing, which is a nice thing to learn to avoid.
    /// </summary>
    public const double MinFalloff = 0.90;
    /// Widened from 1.70 when the floor rose to 0.90 — otherwise the whole
    /// population squeezes into a 0.80 band and every alien is much the same
    /// customer. A test catches exactly that ("falloff spans a real range"),
    /// which is why it is a real range rather than a relaxed assertion.
    public const double MaxFalloff = 1.75;

    /// <summary>
    /// How fast satisfaction drops per unit of dial distance, before falloff.
    ///
    /// ── Tuned DOWN from 7.0 after measuring, 2026-08-14 ──────────────────
    /// At 7.0 the mean satisfaction across 500 aliens was ~30 and roughly 60%
    /// of every tape was refused outright, which made the first hour a wall of
    /// no. Worse, it put the discrimination in the WRONG PLACE: because
    /// distance-based satisfaction is minimised at the centroid, a bland
    /// centre-of-the-space track was accepted by ~79% of aliens while a
    /// characterful one was accepted by ~17%. That quietly makes "write
    /// centrist music" the winning strategy, which is the opposite of the game.
    ///
    /// At 4.0 most tapes find a buyer, and the difference between a great match
    /// and a poor one is expressed in the PRICE instead (the value formula
    /// spans 0.4x to 1.3x on satisfaction alone). Being paid badly for a tape
    /// nobody loves is a far better lesson than being refused six times.
    /// </summary>
    public const double SatisfactionK = 5.5;

    /// Pay factor scales INVERSELY with breadth: an alien who likes almost
    /// nothing pays a premium when you finally hit it, and one who likes
    /// everything is not worth walking across a planet for.
    public const double MinPay = 0.80;
    public const double MaxPay = 1.45;

    // ── Like gate ────────────────────────────────────────────────────────
    //
    // Raised from the handoff's 50/35 after measuring the only number that
    // actually decides whether taste matters: what share of a genre's FANS buy
    // a track of that genre, versus what share of everyone else does.
    //
    //   K 4.0, gate 50/35  ->  fans 100%, others 82%   (18 pt gap)
    //   K 5.5, gate 60/42  ->  fans 100%, others 42%   (58 pt gap)
    //
    // At an 18-point gap taste is decorative and the player may as well sell to
    // whoever is nearest — Sam hit exactly that, nine offers in a row accepted.
    // At 58 the shape is right: find your audience and they always take it,
    // sell to a stranger and it lands about two times in five.
    //
    // Since only 1 alien in 10 is a fan of any given genre, that works out at
    // roughly half of all blind offers landing — often enough to keep moving,
    // rarely enough that walking to the right customer is worth doing.
    public const double LikeCertain = 60.0;   // >= this: liked outright
    public const double LikeMaybe   = 42.0;   // in between: a coin flip

    // ── Derived traits ───────────────────────────────────────────────────

    /// 0..1 from a hash, matching NPCMushroomPrice.Unit.
    static double Unit(uint h) { return (h & 0xFFFFFF) / 16777215.0; }

    /// <summary>
    /// FNV-1a + avalanche, the same shape AlienIdentity uses.
    ///
    /// Deliberately a LOCAL copy rather than a call into AlienIdentity: that
    /// lives in a file that imports UnityEngine, and depending on it would cost
    /// this class the ability to be run headlessly — which is the whole reason
    /// the taste model is testable at all. Nothing requires the two to produce
    /// the same numbers; each only has to be stable within itself.
    /// </summary>
    static uint Hash(string s)
    {
        uint h = 2166136261u;
        if (!string.IsNullOrEmpty(s))
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
        h ^= h >> 16; h *= 2246822507u;
        h ^= h >> 13; h *= 3266489909u;
        h ^= h >> 16;
        return h;
    }

    static uint H(string id, string salt) { return Hash(id + salt); }

    /// How far an ear may sit from the genre it loves, per dial. Enough that
    /// two fans of the same genre are not the same customer; small enough that
    /// they are still recognisably fans of it.
    public const double GenreJitter = 1.8;

    /// <summary>
    /// The genre this alien is a fan of, as an index into TraxClassifier.Genres.
    /// </summary>
    public static int FavouriteGenreIndex(string id)
    {
        int n = TraxClassifier.Genres.Length;
        return n <= 0 ? 0 : (int)(H(id, ":genre") % (uint)n);
    }

    public static string FavouriteGenre(string id)
    {
        int i = FavouriteGenreIndex(id);
        var g = TraxClassifier.Genres;
        return (i >= 0 && i < g.Length) ? g[i].name : "";
    }

    /// <summary>
    /// Where this alien's ear sits, one coordinate per dial, each 0..10.
    ///
    /// ── Ears sit near GENRE CENTRES, not anywhere in the cube ────────────
    /// The first version scattered them uniformly, and measurement killed it:
    /// in six dimensions a uniform scatter puts almost every alien at a similar
    /// middling distance from everything, so satisfaction was dominated by HOW
    /// EXTREME THE TRACK WAS rather than by whether this listener matched it. A
    /// dead-centre track was accepted by 70% of aliens and an extreme one by
    /// 13% — which quietly makes the winning strategy "write centrist music"
    /// instead of "find the right customer", and flattens the whole design.
    ///
    /// Anchoring each ear to a genre centre plus jitter fixes it at the root:
    /// a SLUDJ track now delights SLUDJ fans and is refused by everyone else,
    /// which is the fantasy the game is selling. It also makes "I'm more of a
    /// GLORP listener" literally true rather than a label bolted on afterwards.
    ///
    /// Written into <paramref name="into"/> to stay allocation-free.
    /// </summary>
    public static void TastePoint(string id, double[] into)
    {
        if (into == null) return;
        var genres = TraxClassifier.Genres;
        double[] centre = genres.Length > 0 ? genres[FavouriteGenreIndex(id)].c : null;

        for (int i = 0; i < DialCount && i < into.Length; i++)
        {
            double baseline = (centre != null && i < centre.Length) ? centre[i] : 5.0;
            double jitter = (Unit(H(id, ":jitter" + i)) * 2.0 - 1.0) * GenreJitter;
            double v = baseline + jitter;
            into[i] = v < 0 ? 0 : v > 10 ? 10 : v;
        }
    }

    public static double[] TastePoint(string id)
    {
        var p = new double[DialCount];
        TastePoint(id, p);
        return p;
    }

    /// How steeply they fall out of love with a miss.
    public static double Falloff(string id)
    {
        return MinFalloff + (MaxFalloff - MinFalloff) * Unit(H(id, ":falloff"));
    }

    /// <summary>
    /// What they pay relative to market. Derived FROM falloff rather than from
    /// its own hash — the inverse relationship is the design, not a coincidence
    /// to be re-rolled, and tying them together is what makes a fussy alien
    /// worth the walk.
    /// </summary>
    public static double PayFactor(string id)
    {
        double f = Falloff(id);
        double t = (f - MinFalloff) / (MaxFalloff - MinFalloff);   // 0 = broad, 1 = fussy
        return MinPay + (MaxPay - MinPay) * t;
    }

    /// <summary>
    /// How far over their own number you can push before they stop playing
    /// along. Its own salt, so a fussy ear does not imply a hard bargainer —
    /// the interesting customer is the one who is picky AND patient.
    ///
    /// Derived here rather than borrowed from NPCMushroomPrice.PatienceOf so
    /// this file keeps zero Unity dependencies and stays runnable headlessly.
    /// </summary>
    public const double MinPatience = 1.05;
    public const double MaxPatience = 1.45;

    public static double Patience(string id)
    {
        return MinPatience + (MaxPatience - MinPatience) * Unit(H(id, ":patience"));
    }

    // ── Satisfaction ─────────────────────────────────────────────────────

    /// Euclidean distance between a track's dials and a taste point.
    public static double Distance(double[] dials, double[] taste)
    {
        if (dials == null || taste == null) return 0.0;
        double sum = 0.0;
        int n = DialCount;
        if (dials.Length < n) n = dials.Length;
        if (taste.Length < n) n = taste.Length;
        for (int i = 0; i < n; i++)
        {
            double d = dials[i] - taste[i];
            sum += d * d;
        }
        return System.Math.Sqrt(sum);
    }

    /// <summary>
    /// 0..100. 100 is dead on their ear; 0 is as wrong as it gets for them.
    /// </summary>
    public static double Satisfaction(string id, double[] dials)
    {
        double[] taste = TastePoint(id);
        double s = 100.0 - SatisfactionK * Falloff(id) * Distance(dials, taste);
        if (s < 0.0) return 0.0;
        if (s > 100.0) return 100.0;
        return s;
    }

    public enum Verdict { Rejected, CoinFlip, Liked }

    /// <summary>
    /// The like gate. The middle band is deliberately a COIN FLIP rather than a
    /// threshold: it means a marginal tape is worth trying on someone, which is
    /// what stops the player computing the answer and never talking to anyone.
    /// The caller does the flipping — this stays pure.
    /// </summary>
    public static Verdict Gate(double satisfaction)
    {
        if (satisfaction >= LikeCertain) return Verdict.Liked;
        if (satisfaction >= LikeMaybe) return Verdict.CoinFlip;
        return Verdict.Rejected;
    }

    // ── Feedback ─────────────────────────────────────────────────────────

    /// <summary>
    /// The dial this alien most wants moved, and which way. Returns the index
    /// of the largest gap; <paramref name="wantsMore"/> says whether they want
    /// it up or down.
    ///
    /// This is what turns a rejection into a lesson. Naming the DIAL is
    /// actionable at the console straight away, where naming a genre only helps
    /// once the player knows where the genre centres sit — so the caller leads
    /// with this and mentions genre second.
    /// </summary>
    public static int BiggestGap(string id, double[] dials, out bool wantsMore, out double gap)
    {
        wantsMore = false;
        gap = 0.0;
        double[] taste = TastePoint(id);
        int worst = -1;
        for (int i = 0; i < DialCount && i < dials.Length; i++)
        {
            double d = taste[i] - dials[i];
            double mag = d < 0 ? -d : d;
            if (mag <= gap) continue;
            gap = mag;
            worst = i;
            wantsMore = d > 0;
        }
        return worst;
    }

    /// The second-largest gap, so feedback can name two things without
    /// repeating itself. -1 when there isn't a meaningful one.
    public static int SecondGap(string id, double[] dials, int excludeIndex,
                                out bool wantsMore, out double gap)
    {
        wantsMore = false;
        gap = 0.0;
        double[] taste = TastePoint(id);
        int worst = -1;
        for (int i = 0; i < DialCount && i < dials.Length; i++)
        {
            if (i == excludeIndex) continue;
            double d = taste[i] - dials[i];
            double mag = d < 0 ? -d : d;
            if (mag <= gap) continue;
            gap = mag;
            worst = i;
            wantsMore = d > 0;
        }
        return worst;
    }
}
