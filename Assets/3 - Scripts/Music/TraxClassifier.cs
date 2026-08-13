using System;

/// <summary>
/// Genre classifier. Ten centres in 6-D dial space; nearest wins.
/// PORT OF <c>prototypes/shuttle-computer/engine/classifier.js</c>.
///
/// The centres are placeholders Sam tunes by ear. The MATHS has to stay
/// identical to the JS version because alien reactions and radio playback will
/// key off the resulting label — a track sold as SLUDJ must still be SLUDJ when
/// the buyer evaluates it.
/// </summary>
public static class TraxClassifier
{
    public struct Genre
    {
        public string name;
        public string adj;
        public string vibe;
        public double[] c;      // [PULSE, CRUNCH, GOO, VOID, JITTER, HOMESICK]

        public Genre(string name, string adj, string vibe, double[] c)
        {
            this.name = name; this.adj = adj; this.vibe = vibe; this.c = c;
        }
    }

    public static readonly Genre[] Genres =
    {
        new Genre("GLORP",    "Glorpy",   "wet squelchy bass funk",      new double[] { 6, 3, 9, 3, 5, 4 }),
        new Genre("DRIFT",    "Drifty",   "weightless space drone",      new double[] { 1, 1, 3, 9, 1, 6 }),
        new Genre("SKITTER",  "Skittish", "fast twitchy scatter-beats",  new double[] { 9, 4, 3, 3, 9, 3 }),
        new Genre("SLUDJ",    "Sludjy",   "slow crushing heaviness",     new double[] { 2, 9, 6, 5, 2, 2 }),
        new Genre("CHIRP",    "Chirpy",   "bright bouncy cute",          new double[] { 7, 2, 2, 2, 4, 9 }),
        new Genre("NULLGAZE", "Null",     "hazy sad washed-out",         new double[] { 3, 5, 3, 8, 1, 8 }),
        new Genre("THRUM",    "Thrummy",  "hypnotic ritual percussion",  new double[] { 5, 3, 5, 4, 7, 1 }),
        new Genre("VOLT",     "Volted",   "aggressive electric dance",   new double[] { 8, 7, 4, 2, 5, 5 }),
        new Genre("WARBLE",   "Warbly",   "woozy detuned seasick psych", new double[] { 4, 4, 7, 6, 3, 7 }),
        new Genre("CLANG",    "Clangin'", "metallic industrial banger",  new double[] { 6, 8, 2, 5, 8, 1 })
    };

    /// <summary>
    /// A blend label shows when the runner-up is within this distance OF THE
    /// WINNER (d2 - d1 &lt;= threshold) — NOT when its absolute distance is
    /// small. Reading it the other way makes almost everything blend.
    /// </summary>
    public const double DefaultBlendThreshold = 1.5;

    public struct Result
    {
        public string label;
        public Genre primary;
        public Genre secondary;
        public bool blended;
        public double d1;
        public double d2;
    }

    static double Distance(TraxDials d, double[] centre)
    {
        double sum = 0;
        for (int i = 0; i < TraxPrng.DialCount; i++)
        {
            double diff = d.Get(i) - centre[i];
            sum += diff * diff;
        }
        return Math.Sqrt(sum);
    }

    public static Result Classify(TraxDials dials)
    {
        return Classify(dials, DefaultBlendThreshold);
    }

    public static Result Classify(TraxDials dials, double threshold)
    {
        int i1 = 0, i2 = -1;
        double d1 = double.PositiveInfinity, d2 = double.PositiveInfinity;

        for (int i = 0; i < Genres.Length; i++)
        {
            double d = Distance(dials, Genres[i].c);
            if (d < d1) { i2 = i1; d2 = d1; i1 = i; d1 = d; }
            else if (d < d2) { i2 = i; d2 = d; }
        }

        Result r = new Result();
        r.primary = Genres[i1];
        r.secondary = Genres[i2 < 0 ? 0 : i2];
        r.d1 = d1;
        r.d2 = d2;
        r.blended = (d2 - d1) <= threshold;
        r.label = r.blended ? r.secondary.adj + " " + r.primary.name : r.primary.name;
        return r;
    }
}
