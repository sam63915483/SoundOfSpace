using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks the C# engine against golden vectors dumped from the browser
/// prototype's JS engine.
///
/// ── Why this exists ──────────────────────────────────────────────────────
/// The instrument was prototyped and tuned in the browser, then ported to C#.
/// If the two engines disagree by even one bit, the same dial positions produce
/// a different loop — so a cassette printed in one build would sound wrong in
/// the next, and the "same dials always make the same track" promise that the
/// whole cassette economy rests on would quietly be false.
///
/// Pattern generation uses only +, -, *, / and comparisons, which IEEE-754
/// requires to be correctly rounded, so bit-exact agreement is achievable and
/// anything less means a real bug. Math.Pow is NOT guaranteed identical across
/// implementations, so the four Pow-derived audio values are checked with a
/// relative tolerance instead — none of them feed a pattern decision.
///
/// Regenerate the golden file after ANY change to prototypes/shuttle-computer/
/// engine/:   node test/make-golden.js
/// </summary>
public static class TraxPortVerify
{
    const string GoldenRelative = "Trax/trax-golden.txt";
    const double ApproxTolerance = 1e-9;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    class Case
    {
        public int index;
        public TraxDials dials;
        public uint seed;
        public int scaleIdx;
        public string genre;
        public string[] exact;                       // 8 hex bit strings
        public double[] approx;                      // 4 values
        public uint[] hashes;                        // 5 voice hashes
        public Dictionary<string, string> digests = new Dictionary<string, string>();
    }

    [MenuItem("Tools/TRAX/Verify Engine Port")]
    public static void Verify()
    {
        string path = Path.Combine(Application.streamingAssetsPath, GoldenRelative);
        if (!File.Exists(path))
        {
            Debug.LogError("[TRAX] Golden file missing: " + path +
                           "\nGenerate it with:  node test/make-golden.js  " +
                           "(in prototypes/shuttle-computer)");
            return;
        }

        List<Case> cases;
        try { cases = Parse(File.ReadAllLines(path)); }
        catch (Exception e)
        {
            Debug.LogError("[TRAX] Could not parse the golden file: " + e.Message);
            return;
        }

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
                log.AppendLine("case " + c.index + " SEED: got " + seed + ", want " + c.seed +
                               "  — the dial quantizer or FNV-1a differs " +
                               "(check JsRound: C# Math.Round is banker's rounding)");
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

            // Bit-exact block.
            double[] mine =
            {
                p.density, p.bpm, p.syncopation, p.nudgeSeconds,
                p.hatScatter, p.caveSend, p.caveFeedback, p.detuneCents
            };
            string[] names = { "density", "bpm", "syncopation", "nudgeSeconds",
                               "hatScatter", "caveSend", "caveFeedback", "detuneCents" };
            for (int i = 0; i < mine.Length && i < c.exact.Length; i++)
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

            // Tolerant block (Math.Pow-derived).
            double[] approxMine = { p.filterBase, p.filterQ, p.lfoRate, p.lfoDepthOct };
            string[] approxNames = { "filterBase", "filterQ", "lfoRate", "lfoDepthOct" };
            for (int i = 0; i < approxMine.Length && i < c.approx.Length; i++)
            {
                checks++;
                double want = c.approx[i], got = approxMine[i];
                double scale = Math.Max(1e-12, Math.Abs(want));
                if (Math.Abs(got - want) / scale > ApproxTolerance)
                {
                    failures++;
                    log.AppendLine("case " + c.index + " " + approxNames[i] + ": got " +
                                   got.ToString("R", Inv) + ", want " + want.ToString("R", Inv));
                }
            }

            // Pattern hashes.
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
                    log.AppendLine("    " + LocaliseDigest(digest, want));
                else
                    log.AppendLine("    (no raw digest stored for this case — case 0 carries one)");
            }
        }

        if (failures == 0)
        {
            Debug.Log("[TRAX] Engine port VERIFIED — " + checks + " checks across " +
                      cases.Count + " dial settings, all exact.\n" +
                      "The C# engine and the browser prototype produce identical patterns.");
        }
        else
        {
            Debug.LogError("[TRAX] Engine port MISMATCH — " + failures + " of " + checks +
                           " checks failed.\n" + log);
        }
    }

    /// Find the first differing step so a mismatch points at a line of code
    /// rather than just "the hash is wrong".
    static string LocaliseDigest(string got, string want)
    {
        string[] a = got.Split(';');
        string[] b = want.Split(';');
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] == b[i]) continue;
            int bar = i / TraxPhrase.Steps, step = i % TraxPhrase.Steps;
            return "first difference at bar " + bar + " step " + step +
                   (bar == TraxPhrase.FillBar && step >= TraxPhrase.FillStart ? "  (INSIDE THE FILL)" : "") +
                   "\n      got  " + a[i] + "\n      want " + b[i];
        }
        if (a.Length != b.Length)
            return "step COUNT differs: got " + a.Length + ", want " + b.Length;
        return "digests match but hashes differ — the hash function itself is wrong";
    }

    static string Bits(double x)
    {
        return BitConverter.DoubleToInt64Bits(x).ToString("x16", Inv);
    }

    /// Must match voiceDigest() in test/make-golden.js exactly, including the
    /// 'x' placeholders the drum voices use for degree/dur (they're `undefined`
    /// in JS, and 0 in C# — so the placeholder is what makes them comparable).
    static string Digest(TraxPhrase phrase, TraxVoice voice)
    {
        bool melodic = voice == TraxVoice.Bass || voice == TraxVoice.Lead;
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

    // ── parsing ──────────────────────────────────────────────────────────

    static List<Case> Parse(string[] lines)
    {
        var byIndex = new Dictionary<int, Case>();
        var order = new List<Case>();

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] f = line.Split('|');
            if (f.Length < 2) continue;
            if (f[0] == "META") continue;

            int idx;
            if (!int.TryParse(f[1], NumberStyles.Integer, Inv, out idx)) continue;

            Case c;
            if (!byIndex.TryGetValue(idx, out c))
            {
                c = new Case();
                c.index = idx;
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
                    c.hashes = new uint[5];
                    for (int i = 0; i < 5; i++) c.hashes[i] = uint.Parse(f[2 + i], Inv);
                    break;

                case "DIGEST":
                    // f[2] is the voice; everything after is the digest, which
                    // uses ';' internally precisely so it can't contain '|'.
                    c.digests[f[2]] = f[3];
                    break;
            }
        }

        // A half-parsed case would report confusing failures — drop anything
        // that didn't get all its lines.
        var complete = new List<Case>();
        foreach (Case c in order)
            if (c.exact != null && c.approx != null && c.hashes != null && c.genre != null)
                complete.Add(c);

        if (complete.Count == 0) throw new Exception("no complete cases found");
        return complete;
    }

    /// Always InvariantCulture — on a machine with comma decimal separators,
    /// double.Parse("3.5") with the current culture throws or returns 35.
    static double D(string s)
    {
        return double.Parse(s, NumberStyles.Float, Inv);
    }
}
