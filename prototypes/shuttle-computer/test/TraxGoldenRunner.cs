// Headless golden-vector check for the C# engine port.
//
//   python prototypes/shuttle-computer/test/verify-port.py
//
// This is the SAME comparison Tools ▸ TRAX ▸ Verify Engine Port runs inside the
// Editor, minus the Unity dependency, so the port can be proven correct without
// opening Unity at all. It compiles against the five engine files directly —
// which also proves they are genuinely Unity-free, the property that makes them
// portable in the first place.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class TraxGoldenRunner
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    class Case
    {
        public int index;
        public TraxDials dials;
        public uint seed;
        public int scaleIdx;
        public string genre;
        public string[] exact;
        public double[] approx;
        public uint[] hashes;
        public Dictionary<string, string> digests = new Dictionary<string, string>();
    }

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: TraxGoldenRunner <trax-golden.txt>");
            return 2;
        }

        List<Case> cases = Parse(File.ReadAllLines(args[0]));
        int checks = 0, failures = 0;
        var log = new StringBuilder();

        foreach (Case c in cases)
        {
            uint seed = TraxPrng.SeedFromDials(c.dials);
            TraxParams p = TraxParams.Compute(c.dials);
            TraxPhrase phrase = TraxPhrase.Generate(seed, p);
            TraxClassifier.Result g = TraxClassifier.Classify(c.dials);

            checks++;
            if (seed != c.seed)
            {
                failures++;
                log.AppendLine("case " + c.index + " SEED: got " + seed + ", want " + c.seed);
            }

            checks++;
            if (p.scaleIdx != c.scaleIdx)
            {
                failures++;
                log.AppendLine("case " + c.index + " SCALE: got " + p.scaleIdx + ", want " + c.scaleIdx);
            }

            checks++;
            if (g.label != c.genre)
            {
                failures++;
                log.AppendLine("case " + c.index + " GENRE: got '" + g.label + "', want '" + c.genre + "'");
            }

            double[] mine =
            {
                p.density, p.bpm, p.syncopation, p.nudgeSeconds,
                p.hatScatter, p.caveSend, p.caveFeedback, p.detuneCents
            };
            string[] names = { "density", "bpm", "syncopation", "nudgeSeconds",
                               "hatScatter", "caveSend", "caveFeedback", "detuneCents" };
            for (int i = 0; i < mine.Length; i++)
            {
                checks++;
                string got = Bits(mine[i]);
                if (got != c.exact[i])
                {
                    failures++;
                    log.AppendLine("case " + c.index + " " + names[i] + ": got " + got +
                                   " (" + mine[i].ToString("R", Inv) + "), want " + c.exact[i]);
                }
            }

            double[] approxMine = { p.filterBase, p.filterQ, p.lfoRate, p.lfoDepthOct };
            string[] approxNames = { "filterBase", "filterQ", "lfoRate", "lfoDepthOct" };
            for (int i = 0; i < approxMine.Length; i++)
            {
                checks++;
                double want = c.approx[i], got = approxMine[i];
                double scale = Math.Max(1e-12, Math.Abs(want));
                if (Math.Abs(got - want) / scale > 1e-9)
                {
                    failures++;
                    log.AppendLine("case " + c.index + " " + approxNames[i] + ": got " +
                                   got.ToString("R", Inv) + ", want " + want.ToString("R", Inv));
                }
            }

            for (int v = 0; v < TraxPhrase.VoiceCount; v++)
            {
                checks++;
                var voice = (TraxVoice)v;
                string digest = Digest(phrase, voice);
                uint got = TraxPrng.Fnv1a32(digest);
                if (got == c.hashes[v]) continue;

                failures++;
                log.AppendLine("case " + c.index + " PATTERN " + voice + ": hash " + got +
                               ", want " + c.hashes[v]);
                string want;
                if (c.digests.TryGetValue(voice.ToString().ToLowerInvariant(), out want))
                    log.AppendLine("    " + Localise(digest, want));
            }
        }

        if (failures == 0)
        {
            Console.WriteLine("engine port VERIFIED — " + checks + " checks across " +
                              cases.Count + " dial settings, all exact.");
            return 0;
        }

        Console.WriteLine("engine port MISMATCH — " + failures + " of " + checks + " checks failed.");
        Console.WriteLine(log.ToString());
        return 1;
    }

    static string Localise(string got, string want)
    {
        string[] a = got.Split(';');
        string[] b = want.Split(';');
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] == b[i]) continue;
            int bar = i / TraxPhrase.Steps, step = i % TraxPhrase.Steps;
            return "first difference at bar " + bar + " step " + step +
                   (bar == TraxPhrase.HalfFillBar && step >= TraxPhrase.HalfFillStart
                       ? "  (INSIDE THE BAR-2 TURNAROUND)"
                    : bar == TraxPhrase.FullFillBar && step >= TraxPhrase.FullFillStart
                       ? "  (INSIDE THE BAR-4 FILL)" : "") +
                   "\n      got  " + a[i] + "\n      want " + b[i];
        }
        if (a.Length != b.Length) return "step COUNT differs: " + a.Length + " vs " + b.Length;
        return "digests match but hashes differ";
    }

    static string Bits(double x)
    {
        return BitConverter.DoubleToInt64Bits(x).ToString("x16", Inv);
    }

    static string Digest(TraxPhrase phrase, TraxVoice voice)
    {
        bool melodic = TraxPhrase.IsMelodic(voice);
        var sb = new StringBuilder();
        for (int b = 0; b < TraxPhrase.Bars; b++)
        {
            for (int s = 0; s < TraxPhrase.Steps; s++)
            {
                if (sb.Length > 0) sb.Append(';');
                TraxStep st = phrase.Get(voice, b, s);
                if (!st.on) { sb.Append('-'); continue; }
                sb.Append(Bits(st.vel)).Append(',')
                  .Append(Bits(st.nudge)).Append(',')
                  .Append(melodic ? st.degree.ToString(Inv) : "x").Append(',')
                  .Append(melodic ? st.dur.ToString(Inv) : "x").Append(',')
                  .Append(st.open ? '1' : '0');
            }
        }
        return sb.ToString();
    }

    static List<Case> Parse(string[] lines)
    {
        var byIndex = new Dictionary<int, Case>();
        var order = new List<Case>();

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] f = line.Split('|');
            if (f.Length < 2 || f[0] == "META") continue;

            int idx;
            if (!int.TryParse(f[1], NumberStyles.Integer, Inv, out idx)) continue;

            Case c;
            if (!byIndex.TryGetValue(idx, out c))
            {
                c = new Case { index = idx };
                byIndex[idx] = c;
                order.Add(c);
            }

            switch (f[0])
            {
                case "CASE":
                    c.dials = new TraxDials(D(f[2]), D(f[3]), D(f[4]), D(f[5]), D(f[6]), D(f[7]));
                    c.seed = uint.Parse(f[8], Inv);
                    c.scaleIdx = int.Parse(f[9], Inv);
                    c.genre = f[10];
                    break;
                case "EXACT":
                    c.exact = new string[8];
                    for (int i = 0; i < 8; i++) c.exact[i] = f[2 + i];
                    break;
                case "APPROX":
                    c.approx = new double[4];
                    for (int i = 0; i < 4; i++) c.approx[i] = D(f[2 + i]);
                    break;
                case "HASH":
                    c.hashes = new uint[TraxPhrase.VoiceCount];
                    for (int i = 0; i < TraxPhrase.VoiceCount; i++)
                        c.hashes[i] = uint.Parse(f[2 + i], Inv);
                    break;
                case "DIGEST":
                    c.digests[f[2]] = f[3];
                    break;
            }
        }

        var complete = new List<Case>();
        foreach (Case c in order)
            if (c.exact != null && c.approx != null && c.hashes != null && c.genre != null)
                complete.Add(c);
        if (complete.Count == 0) throw new Exception("no complete cases found");
        return complete;
    }

    static double D(string s) { return double.Parse(s, NumberStyles.Float, Inv); }
}
