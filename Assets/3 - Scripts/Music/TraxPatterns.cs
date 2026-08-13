using System;

public enum TraxVoice
{
    Kick = 0,
    Snare = 1,
    Hat = 2,
    Bass = 3,
    Lead = 4
}

/// One step of one voice. `on == false` means silence at this step.
public struct TraxStep
{
    public bool on;
    public double vel;      // 0..1
    public double nudge;    // seconds, off-grid push (JITTER)
    public int degree;      // scale degree, melodic voices only
    public int dur;         // length in steps, melodic voices only
    public bool open;       // hat only — long/open hi-hat
}

/// <summary>
/// A generated 4-bar phrase for all five voices.
///
/// PORT OF <c>prototypes/shuttle-computer/engine/patterns.js</c>.
///
/// Bars 0-2 are the base pattern. Bar 3 is the same pattern with its LAST FOUR
/// STEPS replaced from a separate fill stream, so the phrase turns over with a
/// fill instead of just repeating. One seed, still fully deterministic.
///
/// ── Why the draw ORDER is load-bearing ───────────────────────────────────
/// Every call to Rng.Next() advances the stream. Adding, removing or reordering
/// a single draw shifts every subsequent value and re-rolls the whole pattern —
/// which would silently change what every previously printed cassette sounds
/// like. The draws below are written as explicit sequential statements rather
/// than inline in an initializer precisely so the order is impossible to
/// misread. Note especially:
///   • kick step 0 does NOT draw a nudge (the downbeat never drifts)
///   • bass step 0 does NOT draw a degree (it always lands on the root)
///   • the hat fill does NOT draw for `open`
/// Each of those is a skipped draw, and skipping it is part of the contract.
/// </summary>
public sealed class TraxPhrase
{
    public const int Bars = 4;
    public const int Steps = 16;
    public const int VoiceCount = 5;
    public const int TotalSteps = Bars * Steps;
    public const int FillStart = 12;          // fill occupies steps 12..15 ...
    public const int FillBar = Bars - 1;      // ... of the final bar

    readonly TraxStep[] _steps = new TraxStep[VoiceCount * Bars * Steps];

    static int Index(TraxVoice v, int bar, int step)
    {
        return ((int)v * Bars + bar) * Steps + step;
    }

    public TraxStep Get(TraxVoice v, int bar, int step) { return _steps[Index(v, bar, step)]; }
    public void Set(TraxVoice v, int bar, int step, TraxStep s) { _steps[Index(v, bar, step)] = s; }

    /// Global step index → the step for a voice. Wraps in both directions.
    public TraxStep At(TraxVoice v, int globalStep)
    {
        int i = ((globalStep % TotalSteps) + TotalSteps) % TotalSteps;
        return Get(v, i / Steps, i % Steps);
    }

    // ── generation ───────────────────────────────────────────────────────

    // Degree pools, weighted by repetition — root-heavy so the loop keeps a
    // tonal centre no matter how alien the scale is.
    static readonly int[] BassDegrees = { 0, 0, 0, 0, 2, 4, -3, 3, 1 };
    static readonly int[] LeadDegrees = { 0, 2, 4, 5, 7, 3, -1, 6 };

    static int Pick(TraxPrng.Rng rnd, int[] arr)
    {
        return arr[(int)Math.Floor(rnd.Next() * arr.Length)];
    }

    static double NudgeFor(TraxPrng.Rng rnd, TraxParams p)
    {
        // Symmetric push around the grid line. Small, but it's what stops the
        // rhythm sounding like a drum machine.
        return (rnd.Next() * 2 - 1) * p.nudgeSeconds;
    }

    public static TraxPhrase Generate(uint seed, TraxParams p)
    {
        var phrase = new TraxPhrase();

        for (int v = 0; v < VoiceCount; v++)
        {
            TraxVoice voice = (TraxVoice)v;
            TraxStep[] baseBar = BuildBase(voice, TraxPrng.StreamFor(seed, voice), p);

            for (int b = 0; b < Bars; b++)
                for (int s = 0; s < Steps; s++)
                    phrase.Set(voice, b, s, baseBar[s]);

            // The fill draws from its own stream, seeded per voice, so each
            // voice's turnaround differs but stays reproducible.
            // Mirrors streamFor((seed ^ (v * 0x9e37)) >>> 0, 'fill') in JS.
            uint fillSeed = unchecked((seed ^ (uint)(v * 0x9e37)) ^ TraxPrng.VoiceFill);
            ApplyFill(phrase, voice, new TraxPrng.Rng(fillSeed), p);
        }

        return phrase;
    }

    static TraxStep[] BuildBase(TraxVoice voice, TraxPrng.Rng rnd, TraxParams p)
    {
        switch (voice)
        {
            case TraxVoice.Kick:  return MakeKick(rnd, p);
            case TraxVoice.Snare: return MakeSnare(rnd, p);
            case TraxVoice.Hat:   return MakeHat(rnd, p);
            case TraxVoice.Bass:  return MakeBass(rnd, p);
            case TraxVoice.Lead:  return MakeLead(rnd, p);
            default: throw new ArgumentOutOfRangeException("voice");
        }
    }

    static TraxStep[] MakeKick(TraxPrng.Rng rnd, TraxParams p)
    {
        var bar = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            double prob;
            if (s == 0) prob = 1;                                            // anchor
            else if (s % 4 == 0) prob = 0.35 + p.density * 0.55;
            else if (s % 2 == 0) prob = p.density * 0.35 * (0.4 + p.syncopation);
            else prob = p.density * 0.22 * p.syncopation;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = (s % 4 == 0 ? 0.95 : 0.6) - rnd.Next() * 0.1;
                st.nudge = s == 0 ? 0.0 : NudgeFor(rnd, p);                  // downbeat never drifts
                bar[s] = st;
            }
        }
        return bar;
    }

    static TraxStep[] MakeSnare(TraxPrng.Rng rnd, TraxParams p)
    {
        var bar = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            bool back = (s == 4 || s == 12);
            double prob;
            if (back) prob = 0.95;
            else if (s % 2 == 1) prob = p.density * 0.28 * p.syncopation;    // ghosts
            else prob = p.density * 0.16 * p.syncopation;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = back ? 0.9 - rnd.Next() * 0.08 : 0.25 + rnd.Next() * 0.15;
                st.nudge = NudgeFor(rnd, p);
                bar[s] = st;
            }
        }
        return bar;
    }

    static TraxStep[] MakeHat(TraxPrng.Rng rnd, TraxParams p)
    {
        // Density picks the subdivision: quarters, eighths, or sixteenths.
        int interval = p.density < 0.4 ? 4 : (p.density < 0.7 ? 2 : 1);
        var bar = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            bool onGrid = s % interval == 0;
            double prob = onGrid ? (1 - p.hatScatter * 0.35) : p.hatScatter * 0.3;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = (s % 4 == 0 ? 0.7 : 0.42) - rnd.Next() * 0.08;
                st.nudge = NudgeFor(rnd, p);
                st.open = rnd.Next() < p.hatScatter * 0.25;
                bar[s] = st;
            }
        }
        return bar;
    }

    static TraxStep[] MakeBass(TraxPrng.Rng rnd, TraxParams p)
    {
        var bar = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            double prob;
            if (s % 4 == 0) prob = 0.5 + p.density * 0.45;
            else if (s % 2 == 0) prob = p.density * 0.35;
            else prob = p.density * 0.2 * p.syncopation;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = 0.75 + rnd.Next() * 0.2;
                st.nudge = NudgeFor(rnd, p);
                st.degree = s == 0 ? 0 : Pick(rnd, BassDegrees);             // land on root
                st.dur = 1 + (int)Math.Floor(rnd.Next() * 2);
                bar[s] = st;
            }
        }
        return bar;
    }

    static TraxStep[] MakeLead(TraxPrng.Rng rnd, TraxParams p)
    {
        var bar = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            double prob;
            if (s % 8 == 0) prob = 0.45 + p.density * 0.25;
            else if (s % 2 == 0) prob = p.density * 0.26;
            else prob = p.density * 0.12 * p.syncopation;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = 0.6 + rnd.Next() * 0.25;
                st.nudge = NudgeFor(rnd, p);
                st.degree = Pick(rnd, LeadDegrees);
                st.dur = 1 + (int)Math.Floor(rnd.Next() * 4);
                bar[s] = st;
            }
        }
        return bar;
    }

    /// <summary>
    /// Overwrite the last four steps of the final bar. Drums get busier, melodic
    /// voices get re-pitched, so the phrase turnaround is audible.
    ///
    /// Note this ASSIGNS every step in the range, hit or miss — a miss clears a
    /// hit that the base pattern had there. That is deliberate: the fill has to
    /// be able to leave a gap.
    /// </summary>
    static void ApplyFill(TraxPhrase phrase, TraxVoice voice, TraxPrng.Rng rnd, TraxParams p)
    {
        for (int s = FillStart; s < Steps; s++)
        {
            TraxStep st = new TraxStep();
            double prob;

            switch (voice)
            {
                case TraxVoice.Kick:
                    prob = 0.3 + p.density * 0.35;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.8 + rnd.Next() * 0.15;
                        st.nudge = NudgeFor(rnd, p);
                    }
                    break;

                case TraxVoice.Snare:
                    prob = 0.6 + p.density * 0.35;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.45 + rnd.Next() * 0.5;
                        st.nudge = NudgeFor(rnd, p);
                    }
                    break;

                case TraxVoice.Hat:
                    prob = 0.75;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.4 + rnd.Next() * 0.35;
                        st.nudge = NudgeFor(rnd, p);
                        st.open = s == Steps - 1;                            // no draw here
                    }
                    break;

                case TraxVoice.Bass:
                    prob = 0.5 + p.density * 0.3;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.8 + rnd.Next() * 0.2;
                        st.nudge = NudgeFor(rnd, p);
                        st.degree = Pick(rnd, BassDegrees);
                        st.dur = 1;
                    }
                    break;

                case TraxVoice.Lead:
                    prob = 0.35 + p.density * 0.3;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.65 + rnd.Next() * 0.3;
                        st.nudge = NudgeFor(rnd, p);
                        st.degree = Pick(rnd, LeadDegrees);
                        st.dur = 1 + (int)Math.Floor(rnd.Next() * 2);
                    }
                    break;
            }

            phrase.Set(voice, FillBar, s, st);
        }
    }
}
