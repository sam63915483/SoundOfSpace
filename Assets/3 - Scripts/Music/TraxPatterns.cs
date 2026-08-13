using System;

public enum TraxVoice
{
    Kick = 0,
    Snare = 1,
    Hat = 2,
    Bass = 3,
    Lead = 4,
    Moss = 5,
    Spindle = 6
}

/// One step of one voice. `on == false` means silence at this step.
public struct TraxStep
{
    public bool on;
    public double vel;      // 0..1
    public double nudge;    // seconds, off-grid push (JITTER)
    public int degree;      // absolute scale degree, melodic voices only
    public int off;         // scale-degree offset from the bar's chord root (pre-harmonise)
    public int dur;         // length in steps, melodic voices only
    public bool open;       // hat only — long/open hi-hat
}

/// <summary>
/// A generated 4-bar phrase for all seven voices.
///
/// PORT OF <c>prototypes/shuttle-computer/engine/patterns.js</c>.
///
/// ── What makes this sound like music and not noise ───────────────────────
/// The first version drew every step and every pitch as an independent coin
/// flip, which is why it came out sounding random. Four things fix that, and
/// none of them cost the player a dial:
///   1. HARMONY. A 4-bar chord progression, one chord per bar. Bass plays roots,
///      MOSS holds the triad, SPINDLE arpeggiates it, the lead resolves onto
///      chord tones on strong beats. Everything is consonant with everything
///      else, and bar 3 differs from bar 1 because the harmony moved.
///   2. RHYTHM CELLS. One 8-step cell per voice, tiled twice per bar, instead of
///      16 independent probabilities. Repetition is what makes a groove read as
///      deliberate.
///   3. A MELODIC MOTIF. The lead is one 8-step figure with stepwise motion,
///      repeated and re-harmonised per bar.
///   4. INTERLOCK. Bass onsets are pulled toward the kick; the lead backs off
///      where the snare lands.
///
/// Bars share their RHYTHM but not their PITCH. Two turnarounds punctuate the
/// phrase: a light 2-step fill ending bar 2, the full 4-step fill ending bar 4.
///
/// ── Why the draw ORDER is load-bearing ───────────────────────────────────
/// Every Rng.Next() advances the stream, so adding, removing or reordering a
/// single draw re-rolls the whole pattern. Two places below take a DIFFERENT
/// NUMBER of draws depending on a branch, and both are load-bearing:
///   • the lead's "repeated note" branch draws no direction
///   • MOSS consumes no draws at all inside a fill
/// Anything that looks like harmless tidying here is not.
/// </summary>
public sealed class TraxPhrase
{
    public const int Bars = 4;
    public const int Steps = 16;
    public const int Cell = 8;                 // rhythm cell; tiled twice per bar
    public const int VoiceCount = 7;
    public const int TotalSteps = Bars * Steps;

    // Two turnarounds. The half-phrase one is deliberately SMALLER — two equal
    // fills would split the 4-bar phrase into two 2-bar loops.
    public const int FullFillBar = 3, FullFillStart = 12;
    public const int HalfFillBar = 1, HalfFillStart = 14;

    // Chord progressions in scale degrees, one entry per bar. All start on the
    // tonic so the phrase always has an anchor.
    static readonly int[][] Progressions =
    {
        new[] { 0, 5, 3, 4 },
        new[] { 0, 3, 4, 0 },
        new[] { 0, 4, 5, 3 },
        new[] { 0, 0, 3, 4 },
        new[] { 0, 5, 0, 4 },
        new[] { 0, 2, 3, 4 },
        new[] { 0, 4, 3, 5 },
        new[] { 0, 3, 0, 4 }
    };

    static readonly int[] ChordTones = { 0, 2, 4 };     // triad, in scale degrees
    static readonly int[] ArpOffsets = { 0, 2, 4, 6 };  // what SPINDLE climbs

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

    public static bool IsMelodic(TraxVoice v)
    {
        return v == TraxVoice.Bass || v == TraxVoice.Lead
            || v == TraxVoice.Moss || v == TraxVoice.Spindle;
    }

    /// The triad MOSS should sound for a given root degree.
    public static int[] ChordTonesFor(int root)
    {
        return new[] { root + ChordTones[0], root + ChordTones[1], root + ChordTones[2] };
    }

    public static int ChordToneCount { get { return ChordTones.Length; } }
    public static int ChordToneAt(int root, int i) { return root + ChordTones[i]; }

    public static int[] ProgressionFor(uint seed)
    {
        var rnd = new TraxPrng.Rng(seed ^ TraxPrng.VoiceChord);
        return Progressions[(int)Math.Floor(rnd.Next() * Progressions.Length)];
    }

    // ── helpers ──────────────────────────────────────────────────────────

    static int Pick(TraxPrng.Rng rnd, int[] arr)
    {
        return arr[(int)Math.Floor(rnd.Next() * arr.Length)];
    }

    static double NudgeFor(TraxPrng.Rng rnd, TraxParams p)
    {
        return (rnd.Next() * 2 - 1) * p.nudgeSeconds;
    }

    /// Nearest chord tone to a scale-degree offset. Iteration order matters:
    /// strict `<` means the first minimum found wins, and JS iterates base
    /// outer / octave inner.
    static int SnapToChord(int off)
    {
        int best = ChordTones[0];
        double bestD = double.PositiveInfinity;
        for (int b = 0; b < ChordTones.Length; b++)
        {
            for (int o = 0; o < 3; o++)
            {
                int oct = o == 0 ? -7 : (o == 1 ? 0 : 7);
                int cand = ChordTones[b] + oct;
                double d = Math.Abs(cand - off);
                if (d < bestD) { bestD = d; best = cand; }
            }
        }
        return best;
    }

    // ── rhythm cells ─────────────────────────────────────────────────────

    static TraxStep[] MakeKickCell(TraxPrng.Rng rnd, TraxParams p)
    {
        var cell = new TraxStep[Cell];
        for (int s = 0; s < Cell; s++)
        {
            double prob;
            if (s == 0) prob = 1;                                        // anchor
            else if (s % 4 == 0) prob = 0.4 + p.density * 0.5;
            else if (s % 2 == 0) prob = p.density * 0.3 * (0.4 + p.syncopation);
            else prob = p.density * 0.18 * p.syncopation;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = (s % 4 == 0 ? 0.95 : 0.6) - rnd.Next() * 0.1;
                st.nudge = s == 0 ? 0.0 : NudgeFor(rnd, p);              // downbeat never drifts
                cell[s] = st;
            }
        }
        return cell;
    }

    static TraxStep[] MakeSnareCell(TraxPrng.Rng rnd, TraxParams p)
    {
        var cell = new TraxStep[Cell];
        for (int s = 0; s < Cell; s++)
        {
            // Cell step 4 tiles onto bar steps 4 AND 12 — the backbeat, for free.
            bool back = s == 4;
            double prob;
            if (back) prob = 0.97;
            else if (s % 2 == 1) prob = p.density * 0.22 * p.syncopation;
            else prob = p.density * 0.12 * p.syncopation;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = back ? 0.9 - rnd.Next() * 0.08 : 0.22 + rnd.Next() * 0.14;
                st.nudge = NudgeFor(rnd, p);
                cell[s] = st;
            }
        }
        return cell;
    }

    static TraxStep[] MakeHatCell(TraxPrng.Rng rnd, TraxParams p)
    {
        int interval = p.density < 0.4 ? 4 : (p.density < 0.7 ? 2 : 1);
        var cell = new TraxStep[Cell];
        for (int s = 0; s < Cell; s++)
        {
            bool onGrid = s % interval == 0;
            double prob = onGrid ? (1 - p.hatScatter * 0.3) : p.hatScatter * 0.25;

            if (rnd.Next() < prob)
            {
                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = (s % 4 == 0 ? 0.7 : 0.42) - rnd.Next() * 0.08;
                st.nudge = NudgeFor(rnd, p);
                st.open = rnd.Next() < p.hatScatter * 0.22;
                cell[s] = st;
            }
        }
        return cell;
    }

    /// Bass follows the kick rather than ignoring it — that lock is most of what
    /// makes a rhythm section sound like one instrument instead of two.
    static TraxStep[] MakeBassCell(TraxPrng.Rng rnd, TraxParams p, TraxStep[] kickCell)
    {
        var cell = new TraxStep[Cell];
        for (int s = 0; s < Cell; s++)
        {
            bool onKick = kickCell[s].on;
            double prob;
            if (s == 0) prob = 0.95;
            else if (onKick) prob = 0.55 + p.density * 0.35;
            else if (s % 4 == 0) prob = 0.4 + p.density * 0.3;
            else if (s % 2 == 0) prob = p.density * 0.25;
            else prob = p.density * 0.14 * p.syncopation;

            if (rnd.Next() < prob)
            {
                double r = rnd.Next();
                int off;
                if (r < 0.62) off = 0;
                else if (r < 0.82) off = 4;
                else if (r < 0.94) off = -7;
                else off = 2;

                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = 0.75 + rnd.Next() * 0.2;
                st.nudge = NudgeFor(rnd, p);
                st.off = s == 0 ? 0 : off;                               // downbeat on the root
                st.dur = 1 + (int)Math.Floor(rnd.Next() * 2);
                cell[s] = st;
            }
        }
        return cell;
    }

    /// The lead is a MOTIF: one 8-step figure that walks mostly stepwise, snaps
    /// to chord tones on strong beats, and gets re-harmonised each bar.
    static TraxStep[] MakeLeadMotif(TraxPrng.Rng rnd, TraxParams p)
    {
        var cell = new TraxStep[Cell];
        int cur = 0;
        for (int s = 0; s < Cell; s++)
        {
            double prob;
            if (s == 0) prob = 0.6 + p.density * 0.3;
            else if (s % 4 == 0) prob = 0.4 + p.density * 0.28;
            else if (s % 2 == 0) prob = p.density * 0.3;
            else prob = p.density * 0.16 * p.syncopation;
            if (s == 4) prob *= 0.55;                                    // room for the snare

            if (rnd.Next() < prob)
            {
                double r = rnd.Next();
                int move;
                // NOTE the third branch draws nothing — see the class comment.
                if (r < 0.46) move = rnd.Next() < 0.5 ? -1 : 1;
                else if (r < 0.74) move = rnd.Next() < 0.5 ? -2 : 2;
                else if (r < 0.88) move = 0;
                else move = rnd.Next() < 0.5 ? -4 : 4;

                cur += move;
                if (cur > 6) cur -= 7;
                if (cur < -3) cur += 7;
                if (s % 4 == 0) cur = SnapToChord(cur);

                TraxStep st = new TraxStep();
                st.on = true;
                st.vel = 0.6 + rnd.Next() * 0.25;
                st.nudge = NudgeFor(rnd, p);
                st.off = cur;
                st.dur = 1 + (int)Math.Floor(rnd.Next() * 3);
                cell[s] = st;
            }
        }
        return cell;
    }

    /// MOSS holds the chord for the whole bar — the bed everything sits on.
    static TraxStep[] MakeMossCell(TraxPrng.Rng rnd, TraxParams p)
    {
        var cell = new TraxStep[Cell];
        TraxStep st = new TraxStep();
        st.on = true;
        st.vel = 0.42 + rnd.Next() * 0.12;
        st.nudge = 0;
        st.off = 0;
        st.dur = Steps;
        cell[0] = st;
        return cell;
    }

    /// SPINDLE climbs the chord. Mechanical and always consonant.
    static TraxStep[] MakeSpindleCell(TraxPrng.Rng rnd, TraxParams p)
    {
        int every = p.density > 0.55 ? 1 : 2;
        int dir = rnd.Next() < 0.5 ? 1 : -1;
        int start = (int)Math.Floor(rnd.Next() * ArpOffsets.Length);
        var cell = new TraxStep[Cell];
        int i = 0;
        for (int s = 0; s < Cell; s++)
        {
            if (s % every != 0) continue;
            int idx = (start + i * dir) % ArpOffsets.Length;
            if (idx < 0) idx += ArpOffsets.Length;

            TraxStep st = new TraxStep();
            st.on = true;
            st.vel = 0.4 + rnd.Next() * 0.18;
            st.nudge = NudgeFor(rnd, p);
            st.off = ArpOffsets[idx];
            st.dur = every;
            cell[s] = st;
            i++;
        }
        return cell;
    }

    // ── assembly ─────────────────────────────────────────────────────────

    public static TraxPhrase Generate(uint seed, TraxParams p)
    {
        var phrase = new TraxPhrase();
        int[] prog = ProgressionFor(seed);

        var kickCell = MakeKickCell(TraxPrng.StreamFor(seed, TraxVoice.Kick), p);
        var cells = new TraxStep[VoiceCount][];
        cells[(int)TraxVoice.Kick]    = kickCell;
        cells[(int)TraxVoice.Snare]   = MakeSnareCell(TraxPrng.StreamFor(seed, TraxVoice.Snare), p);
        cells[(int)TraxVoice.Hat]     = MakeHatCell(TraxPrng.StreamFor(seed, TraxVoice.Hat), p);
        cells[(int)TraxVoice.Bass]    = MakeBassCell(TraxPrng.StreamFor(seed, TraxVoice.Bass), p, kickCell);
        cells[(int)TraxVoice.Lead]    = MakeLeadMotif(TraxPrng.StreamFor(seed, TraxVoice.Lead), p);
        cells[(int)TraxVoice.Moss]    = MakeMossCell(TraxPrng.StreamFor(seed, TraxVoice.Moss), p);
        cells[(int)TraxVoice.Spindle] = MakeSpindleCell(TraxPrng.StreamFor(seed, TraxVoice.Spindle), p);

        for (int v = 0; v < VoiceCount; v++)
        {
            TraxVoice voice = (TraxVoice)v;
            TraxStep[] cell = cells[v];
            bool melodic = IsMelodic(voice);
            bool isMoss = voice == TraxVoice.Moss;

            for (int b = 0; b < Bars; b++)
            {
                for (int s = 0; s < Steps; s++)
                {
                    // MOSS is the exception to tiling: one chord per bar, held.
                    // Tiling its cell would retrigger the pad halfway through
                    // every bar, overlapping itself and re-swelling where
                    // nothing changed.
                    TraxStep st = isMoss
                        ? (s == 0 ? cell[0] : new TraxStep())
                        : cell[s % Cell];
                    if (st.on && melodic) st.degree = prog[b] + (isMoss ? 0 : st.off);
                    phrase.Set(voice, b, s, st);
                }
            }

            // ONE fill stream per voice, drawn on by the half-phrase fill first
            // and then the full one — they share a stream, so their order is
            // part of the contract.
            uint fillSeed = unchecked((seed ^ (uint)(v * 0x9e37)) ^ TraxPrng.VoiceFill);
            var fillRnd = new TraxPrng.Rng(fillSeed);
            ApplyFill(phrase, voice, fillRnd, p, HalfFillBar, HalfFillStart, prog[HalfFillBar], 0.55);
            ApplyFill(phrase, voice, fillRnd, p, FullFillBar, FullFillStart, prog[FullFillBar], 1.0);
        }

        return phrase;
    }

    /// <summary>
    /// Overwrite the tail of a bar. `weight` scales how busy the fill is, so the
    /// half-phrase turnaround stays lighter than the end-of-phrase one.
    /// Assigns every step in range, hit or miss — a miss clears the tiled hit
    /// that was there, which is how a fill gets to leave a gap.
    /// </summary>
    static void ApplyFill(TraxPhrase phrase, TraxVoice voice, TraxPrng.Rng rnd, TraxParams p,
                          int bar, int from, int root, double weight)
    {
        // The pad rides straight through a fill. Cutting the harmony out from
        // under a turnaround is what makes it sound like a mistake — and MOSS
        // must consume NO draws here, or every other voice's fill shifts.
        if (voice == TraxVoice.Moss) return;

        for (int s = from; s < Steps; s++)
        {
            TraxStep st = new TraxStep();
            double prob;

            switch (voice)
            {
                case TraxVoice.Kick:
                    prob = (0.3 + p.density * 0.35) * weight;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.8 + rnd.Next() * 0.15;
                        st.nudge = NudgeFor(rnd, p);
                    }
                    break;

                case TraxVoice.Snare:
                    prob = (0.6 + p.density * 0.35) * weight;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.45 + rnd.Next() * 0.5;
                        st.nudge = NudgeFor(rnd, p);
                    }
                    break;

                case TraxVoice.Hat:
                    prob = 0.75 * weight;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.4 + rnd.Next() * 0.35;
                        st.nudge = NudgeFor(rnd, p);
                        st.open = s == Steps - 1;                         // no draw here
                    }
                    break;

                case TraxVoice.Bass:
                    prob = (0.5 + p.density * 0.3) * weight;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.8 + rnd.Next() * 0.2;
                        st.nudge = NudgeFor(rnd, p);
                        st.degree = root;
                        st.dur = 1;
                    }
                    break;

                case TraxVoice.Lead:
                    prob = (0.35 + p.density * 0.3) * weight;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.65 + rnd.Next() * 0.3;
                        st.nudge = NudgeFor(rnd, p);
                        st.degree = root + Pick(rnd, ChordTones);
                        st.dur = 1;
                    }
                    break;

                case TraxVoice.Spindle:
                    prob = 0.8 * weight;
                    if (rnd.Next() < prob)
                    {
                        st.on = true;
                        st.vel = 0.4 + rnd.Next() * 0.2;
                        st.nudge = NudgeFor(rnd, p);
                        st.degree = root + Pick(rnd, ArpOffsets);
                        st.dur = 1;
                    }
                    break;
            }

            phrase.Set(voice, bar, s, st);
        }
    }
}
