// Measures what aliens actually DO with Tev's three demos, to explain a
// playtest report rather than guess at it.
//
//   python test/verify-diagnostic.py
//
// Sam, 2026-08-14: "all 3 npcs said not enough crunch, then something else like
// drowning in void, and they all said they like drift... they all seemed to buy
// southern exposure."

using System;

public static class TasteDiagnostic
{
    // Tev's catalogue, copied from TevDemoTapes (which imports Unity).
    static readonly string[] Names = { "SOUTHERN EXPOSURE", "LAWN ORNAMENT", "NOTHING MUCH HAPPENS" };
    static readonly double[][] Demos =
    {
        new double[] { 2, 9, 6, 5, 2, 8 },   // near SLUDJ
        new double[] { 7, 2, 2, 2, 4, 1 },   // near CHIRP
        new double[] { 1, 1, 3, 9, 1, 4 },   // near DRIFT
    };

    static string[] Ids(int n)
    {
        var ids = new string[n];
        for (int i = 0; i < n; i++)
            ids[i] = i % 3 == 0 ? "scene:Alien" + i : "cell:" + (i % 7) + ":" + (i * 31);
        return ids;
    }

    public static int Main()
    {
        const int N = 500;
        string[] ids = Ids(N);

        // ── FIRST: is this diagnostic even measuring the real thing? ─────
        // The arrays above are hand-copied. Build the track the way
        // TevDemoTapes actually builds it and read the dials back out, so a
        // mismatch between intent and reality cannot hide behind a plausible
        // looking number.
        Console.WriteLine("=== REAL TRACK CONSTRUCTION vs THE ARRAYS ABOVE ===");
        for (int d = 0; d < Demos.Length; d++)
        {
            TraxTrack t = TraxTrack.Default();
            for (int i = 0; i < Demos[d].Length && i < TraxPrng.DialCount; i++)
                t = t.WithDial(i, Demos[d][i]);

            Console.Write("  " + Names[d].PadRight(22) + " intended [");
            for (int i = 0; i < Demos[d].Length; i++) Console.Write(Demos[d][i].ToString("0") + " ");
            Console.Write("]  actual [");
            bool same = true;
            for (int i = 0; i < TraxPrng.DialCount; i++)
            {
                double got = t.dials.Get(i);
                Console.Write(got.ToString("0") + " ");
                if (i < Demos[d].Length && Math.Abs(got - Demos[d][i]) > 1e-9) same = false;
            }
            Console.WriteLine("]  " + (same ? "MATCH" : "*** MISMATCH ***"));
        }
        Console.WriteLine();

        Console.WriteLine("=== WHERE ALIEN EARS ACTUALLY SIT (" + N + " aliens) ===");
        var mean = new double[AlienTaste.DialCount];
        foreach (string id in ids)
        {
            double[] p = AlienTaste.TastePoint(id);
            for (int i = 0; i < p.Length; i++) mean[i] += p[i] / N;
        }
        Console.Write("  mean taste point: ");
        for (int i = 0; i < mean.Length; i++) Console.Write(mean[i].ToString("0.0") + " ");
        Console.WriteLine();

        // How far is a typical ear from the CENTRE of the space?
        var centre = new double[] { 5, 5, 5, 5, 5, 5 };
        double meanFromCentre = 0, minFromCentre = 99, maxFromCentre = 0;
        foreach (string id in ids)
        {
            double d = AlienTaste.Distance(AlienTaste.TastePoint(id), centre);
            meanFromCentre += d / N;
            if (d < minFromCentre) minFromCentre = d;
            if (d > maxFromCentre) maxFromCentre = d;
        }
        Console.WriteLine("  distance from dead centre: mean " + meanFromCentre.ToString("0.00") +
                          ", min " + minFromCentre.ToString("0.00") +
                          ", max " + maxFromCentre.ToString("0.00"));
        Console.WriteLine("  (a uniform cube would put almost everyone at a similar middling");
        Console.WriteLine("   distance — that is the clustering to watch for)");
        Console.WriteLine();

        Console.WriteLine("=== WHAT HAPPENS TO EACH OF TEV'S DEMOS ===");
        for (int d = 0; d < Demos.Length; d++)
        {
            double[] track = Demos[d];
            int liked = 0, coin = 0, rejected = 0;
            double satSum = 0;
            var complaints = new int[AlienTaste.DialCount];

            foreach (string id in ids)
            {
                double sat = AlienTaste.Satisfaction(id, track);
                satSum += sat / N;
                switch (AlienTaste.Gate(sat))
                {
                    case AlienTaste.Verdict.Liked: liked++; break;
                    case AlienTaste.Verdict.CoinFlip: coin++; break;
                    default: rejected++; break;
                }
                bool more; double gap;
                int worst = AlienTaste.BiggestGap(id, track, out more, out gap);
                if (worst >= 0) complaints[worst]++;
            }

            Console.WriteLine();
            Console.WriteLine("  " + Names[d] + "  [" + string.Join(" ", Array.ConvertAll(track, x => x.ToString("0"))) + "]");
            Console.WriteLine("    mean satisfaction " + satSum.ToString("0.0") +
                              "   liked " + Pct(liked, N) +
                              "   coinflip " + Pct(coin, N) +
                              "   rejected " + Pct(rejected, N));
            Console.Write("    top complaint: ");
            int top = 0;
            for (int i = 1; i < complaints.Length; i++) if (complaints[i] > complaints[top]) top = i;
            for (int i = 0; i < complaints.Length; i++)
                if (complaints[i] > 0)
                    Console.Write(AlienFeedback.DialNames[i] + " " + Pct(complaints[i], N) + "  ");
            Console.WriteLine();
            Console.WriteLine("    -> " + AlienFeedback.DialNames[top] + " dominates");
        }

        Console.WriteLine();
        Console.WriteLine("=== IS THE SPACE ACTUALLY BEING USED? ===");
        // If a middling track pleases nearly everyone and an extreme one pleases
        // nearly nobody, then taste is not the thing deciding sales — extremity
        // is. That would flatten the whole "different aliens want different
        // things" fantasy into "make centrist music".
        double[] middling = { 5, 5, 5, 5, 5, 5 };
        int midLiked = 0;
        foreach (string id in ids)
            if (AlienTaste.Gate(AlienTaste.Satisfaction(id, middling)) != AlienTaste.Verdict.Rejected)
                midLiked++;
        Console.WriteLine("  a dead-centre track is not-rejected by " + Pct(midLiked, N) + " of aliens");

        int extremeLiked = 0;
        double[] extreme = { 0, 10, 0, 10, 0, 10 };
        foreach (string id in ids)
            if (AlienTaste.Gate(AlienTaste.Satisfaction(id, extreme)) != AlienTaste.Verdict.Rejected)
                extremeLiked++;
        Console.WriteLine("  an extreme-corner track is not-rejected by " + Pct(extremeLiked, N));

        Console.WriteLine();
        Console.WriteLine("=== DOES PRICE CARRY THE DISCRIMINATION? ===");
        // If acceptance is generous, the difference between a great match and a
        // poor one has to show up in the money, or taste stops mattering.
        int bestPaid = 0, worstPaid = int.MaxValue;
        string bestId = "", worstId = "";
        double[] sludj = Demos[0];
        foreach (string id in ids)
        {
            double sat = AlienTaste.Satisfaction(id, sludj);
            if (AlienTaste.Gate(sat) == AlienTaste.Verdict.Rejected) continue;
            int v = TapeValue.For(6, 1, sat, 0, false, AlienTaste.PayFactor(id));
            if (v > bestPaid) { bestPaid = v; bestId = id; }
            if (v < worstPaid) { worstPaid = v; worstId = id; }
        }
        Console.WriteLine("  same tape, six modules, no bond:");
        Console.WriteLine("    best buyer  $" + bestPaid + "  (" + AlienTaste.FavouriteGenre(bestId) + " fan)");
        Console.WriteLine("    worst buyer $" + worstPaid + "  (" + AlienTaste.FavouriteGenre(worstId) + " fan)");
        Console.WriteLine("    spread " + (bestPaid / (double)Math.Max(1, worstPaid)).ToString("0.0") + "x");
        Console.WriteLine("  (if that spread is small, finding the right buyer is not worth walking for)");

        Console.WriteLine();
        Console.WriteLine("=== THE NUMBER THAT ACTUALLY MATTERS ===");
        Console.WriteLine("  For a track sitting ON a genre centre: what share of that genre's");
        Console.WriteLine("  FANS accept it, versus everyone else? If those two numbers are");
        Console.WriteLine("  close, taste is decorative and the player may as well sell to");
        Console.WriteLine("  whoever is nearest.");
        Console.WriteLine();
        Console.WriteLine("  K     gate    fans accept   others accept   gap");
        double[] ks = { 4.0, 5.0, 5.5, 6.0, 7.0 };
        double[][] gates = { new[] { 50.0, 35.0 }, new[] { 60.0, 42.0 }, new[] { 65.0, 45.0 } };
        foreach (double k in ks)
            foreach (double[] g in gates)
                Console.WriteLine("  " + k.ToString("0.0") + "   " +
                                  g[0].ToString("0") + "/" + g[1].ToString("0") + "   " +
                                  Sweep(ids, k, g[0], g[1], true) + "          " +
                                  Sweep(ids, k, g[0], g[1], false) + "           " +
                                  (SweepRaw(ids, k, g[0], g[1], true) -
                                   SweepRaw(ids, k, g[0], g[1], false)).ToString("0") + " pts");

        Console.WriteLine();
        Console.WriteLine("=== PUSHOVERS: aliens who buy almost anything ===");
        // A listener broad enough to accept a track from the far side of the
        // space is not a broad listener, they are a vending machine. Meeting
        // three in a row is what a run of nine-for-nine looks like.
        int pushover = 0, picky = 0;
        foreach (string id in ids)
        {
            int accepts = 0;
            var genres2 = TraxClassifier.Genres;
            for (int g = 0; g < genres2.Length; g++)
                if (AlienTaste.Gate(AlienTaste.Satisfaction(id, genres2[g].c)) != AlienTaste.Verdict.Rejected)
                    accepts++;
            if (accepts >= genres2.Length - 1) pushover++;     // takes 9 or 10 of 10 genres
            if (accepts <= 2) picky++;
        }
        Console.WriteLine("  buy 9+ of the 10 genre archetypes: " + Pct(pushover, N) + "  <- the problem");
        Console.WriteLine("  buy 2 or fewer:                    " + Pct(picky, N));
        Console.WriteLine("  falloff range " + AlienTaste.MinFalloff.ToString("0.00") +
                          " .. " + AlienTaste.MaxFalloff.ToString("0.00"));

        Console.WriteLine();
        Console.WriteLine("=== ARE FAVOURITE GENRES SPREAD? ===");
        var byGenre = new int[TraxClassifier.Genres.Length];
        foreach (string id in ids) byGenre[AlienTaste.FavouriteGenreIndex(id)]++;
        for (int i = 0; i < byGenre.Length; i++)
            Console.Write(TraxClassifier.Genres[i].name + " " + Pct(byGenre[i], N) + "  ");
        Console.WriteLine();

        return 0;
    }

    static string Pct(int n, int total)
    {
        return (100.0 * n / total).ToString("0") + "%";
    }

    /// Share of aliens who would NOT reject a track sitting on genre centre G,
    /// counting only fans of G (matching=true) or only everyone else.
    /// Recomputes satisfaction locally so a candidate K and gate can be tried
    /// without editing the model.
    static double SweepRaw(string[] ids, double k, double likeCertain, double likeMaybe,
                           bool matching)
    {
        int considered = 0, accepted = 0;
        var genres = TraxClassifier.Genres;
        for (int g = 0; g < genres.Length; g++)
        {
            double[] track = genres[g].c;
            foreach (string id in ids)
            {
                bool isFan = AlienTaste.FavouriteGenreIndex(id) == g;
                if (isFan != matching) continue;
                considered++;
                double dist = AlienTaste.Distance(track, AlienTaste.TastePoint(id));
                double sat = 100.0 - k * AlienTaste.Falloff(id) * dist;
                if (sat < 0) sat = 0;
                // A coin flip counts as half an acceptance.
                if (sat >= likeCertain) accepted += 2;
                else if (sat >= likeMaybe) accepted += 1;
            }
        }
        return considered == 0 ? 0 : 100.0 * accepted / (2.0 * considered);
    }

    static string Sweep(string[] ids, double k, double a, double b, bool matching)
    {
        return SweepRaw(ids, k, a, b, matching).ToString("0") + "%";
    }
}
