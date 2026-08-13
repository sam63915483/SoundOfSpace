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
    public int off;         // offset from the bar's chord root, pre-harmonise
    public int dur;         // length in steps, melodic voices only
    public bool open;       // hat only — long/open hi-hat
}

/// <summary>
/// Pattern generation: a track -> a 4-bar phrase per voice.
/// PORT OF <c>prototypes/shuttle-computer/engine/patterns.js</c>.
///
/// ── Preset chooses, dial shapes, variation re-rolls ──────────────────────
/// Rhythms come from hand-authored 16-step weight templates (TraxPresets):
/// 3 always sounds, 2 usually, 1 optionally. The PRESET picks the template —
/// the skeleton of the groove. PULSE decides how much optional material fills
/// in. VARIATION decides which optional hits land. So a groove gets busier
/// without becoming a different groove, which is the whole point.
///
/// ── The draw discipline that makes dials feel like shaping ───────────────
/// Every voice consumes a FIXED number of draws per step, whether or not that
/// step ends up sounding. If draws were only taken on hits, turning PULSE up
/// would shift every later step's velocity and nudge — so the groove would
/// still subtly re-roll and we would be back where we started. Drawing
/// unconditionally means a dial change flips individual hits on and off and
/// disturbs nothing else.
///
/// Per step: drums 4, bass 4, lead 6, pad 2, arp 3, fills 4. Those counts are
/// part of the contract with the JS reference — changing one re-rolls every
/// pattern in the game.
/// </summary>
public sealed class TraxPhrase
{
    public const int Bars = 4;
    public const int Steps = 16;
    public const int VoiceCount = 7;
    public const int TotalSteps = Bars * Steps;

    public const int FullFillBar = 3, FullFillStart = 12;
    public const int HalfFillBar = 1, HalfFillStart = 14;

    static readonly int[] ChordTones = { 0, 2, 4 };

    readonly TraxStep[] _steps = new TraxStep[VoiceCount * Bars * Steps];

    static int Index(TraxVoice v, int bar, int step)
    {
        return ((int)v * Bars + bar) * Steps + step;
    }

    public TraxStep Get(TraxVoice v, int bar, int step) { return _steps[Index(v, bar, step)]; }
    public void Set(TraxVoice v, int bar, int step, TraxStep s) { _steps[Index(v, bar, step)] = s; }

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

    public static int ChordToneCount { get { return ChordTones.Length; } }
    public static int ChordToneAt(int root, int i) { return root + ChordTones[i]; }

    public static int[] ProgressionFor(TraxTrack track)
    {
        int pi = track.PresetOf("MOSS") % TraxPresets.PresetCount;
        return TraxPresets.Moss[pi].prog;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// Does a step sound? 3 always, 2 usually, 1 only when things are busy.
    static bool HitFor(int w, double r, double density)
    {
        if (w >= 3) return true;
        if (w == 2) return r < 0.55 + density * 0.45;
        if (w == 1) return r < density * 0.7;
        return false;
    }

    static int SnapToChord(int off)
    {
        int best = ChordTones[0];
        double bestD = double.PositiveInfinity;
        for (int b = 0; b < ChordTones.Length; b++)
            for (int o = 0; o < 3; o++)
            {
                int oct = o == 0 ? -7 : (o == 1 ? 0 : 7);
                int cand = ChordTones[b] + oct;
                double d = Math.Abs(cand - off);
                if (d < bestD) { bestD = d; best = cand; }
            }
        return best;
    }

    // ── per-voice cells (16 steps, shared by all four bars) ──────────────

    static TraxStep[] MakeDrumCell(TraxPrng.Rng rnd, TraxParams p, string weightStr, TraxVoice kind)
    {
        int[] w = TraxPresets.ParseWeights(weightStr);
        var cell = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            double r = rnd.Next(), rv = rnd.Next(), rn = rnd.Next(), ro = rnd.Next();   // always 4
            if (!HitFor(w[s], r, p.density)) continue;

            bool core = w[s] >= 3;
            double vel;
            if (kind == TraxVoice.Hat) vel = (s % 4 == 0 ? 0.7 : 0.42);
            else if (kind == TraxVoice.Snare) vel = core ? 0.9 : 0.28;
            else vel = core ? 0.95 : 0.6;

            TraxStep st = new TraxStep();
            st.on = true;
            st.vel = vel - rv * 0.08;
            st.nudge = s == 0 ? 0.0 : (rn * 2 - 1) * p.nudgeSeconds;
            if (kind == TraxVoice.Hat) st.open = ro < p.hatScatter * 0.22;
            cell[s] = st;
        }
        return cell;
    }

    static TraxStep[] MakeBassCell(TraxPrng.Rng rnd, TraxParams p, TraxPresets.BassPreset preset)
    {
        int[] w = TraxPresets.ParseWeights(preset.hits);
        var cell = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            double r = rnd.Next(), rv = rnd.Next(), rn = rnd.Next(), rd = rnd.Next();
            if (!HitFor(w[s], r, p.density)) continue;

            TraxStep st = new TraxStep();
            st.on = true;
            st.vel = 0.78 + rv * 0.18;
            st.nudge = s == 0 ? 0.0 : (rn * 2 - 1) * p.nudgeSeconds;
            st.off = preset.contour[s];
            st.dur = 1 + (int)Math.Floor(rd * 2);
            cell[s] = st;
        }
        return cell;
    }

    /// The lead is still generated — a preset that always played the same tune
    /// would be the preset-loop problem one level up. The preset sets its
    /// character: how often it plays, how far it jumps, how long notes are.
    static TraxStep[] MakeLeadCell(TraxPrng.Rng rnd, TraxParams p, TraxPresets.LeadPreset cfg)
    {
        var cell = new TraxStep[Steps];
        int cur = 0;
        for (int s = 0; s < Steps; s++)
        {
            double r = rnd.Next(), rv = rnd.Next(), rn = rnd.Next();
            double rm = rnd.Next(), rdir = rnd.Next(), rl = rnd.Next();                 // always 6

            double shape = (s % 8 == 0) ? 1.0 : (s % 4 == 0 ? 0.75 : (s % 2 == 0 ? 0.5 : 0.28));
            if (s == 4 || s == 12) shape *= 0.55;                    // room for the snare
            double prob = cfg.gate * shape * (0.55 + p.density * 0.6);
            if (r >= prob) continue;

            int move;
            if (rm < cfg.leap) move = rdir < 0.5 ? -4 : 4;
            else if (rm < 0.5) move = rdir < 0.5 ? -1 : 1;
            else if (rm < 0.82) move = rdir < 0.5 ? -2 : 2;
            else move = 0;
            cur += move;
            if (cur > 6) cur -= 7;
            if (cur < -3) cur += 7;
            if (s % 4 == 0) cur = SnapToChord(cur);

            TraxStep st = new TraxStep();
            st.on = true;
            st.vel = 0.6 + rv * 0.25;
            st.nudge = (rn * 2 - 1) * p.nudgeSeconds;
            st.off = cur;
            st.dur = cfg.len + (int)Math.Floor(rl * 2);
            cell[s] = st;
        }
        return cell;
    }

    static TraxStep[] MakeMossCell(TraxPrng.Rng rnd, TraxPresets.PadRhythm rhythm)
    {
        int[] w = TraxPresets.ParseWeights(rhythm.hits);
        var cell = new TraxStep[Steps];
        for (int s = 0; s < Steps; s++)
        {
            double r = rnd.Next(), rv = rnd.Next();                  // always 2
            if (!HitFor(w[s], r, 1)) continue;                       // the pad plays its template

            TraxStep st = new TraxStep();
            st.on = true;
            st.vel = 0.42 + rv * 0.12;
            st.nudge = 0;
            st.off = 0;
            st.dur = rhythm.dur;
            cell[s] = st;
        }
        return cell;
    }

    static TraxStep[] MakeSpindleCell(TraxPrng.Rng rnd, TraxParams p, int[] shape)
    {
        int every = p.density > 0.55 ? 1 : 2;
        var cell = new TraxStep[Steps];
        int i = 0;
        for (int s = 0; s < Steps; s++)
        {
            double r = rnd.Next(), rv = rnd.Next(), rn = rnd.Next(); // always 3
            if (s % every != 0) continue;

            int tone = TraxPresets.ArpTones[shape[i % shape.Length] % TraxPresets.ArpTones.Length];
            TraxStep st = new TraxStep();
            st.on = true;
            st.vel = 0.4 + rv * 0.18;
            st.nudge = (rn * 2 - 1) * p.nudgeSeconds;
            st.off = tone;
            st.dur = every;
            cell[s] = st;
            i++;
        }
        return cell;
    }

    static TraxStep[] CellFor(TraxTrack track, TraxVoice voice, TraxParams p)
    {
        var rnd = new TraxPrng.Rng(track.VoiceSeed(voice));
        string mod = TraxModules.For(voice);
        int pi = track.PresetOf(mod) % TraxPresets.PresetCount;
        int vi = track.VariationOf(mod) % TraxPresets.VariationCount;

        switch (voice)
        {
            case TraxVoice.Kick:    return MakeDrumCell(rnd, p, TraxPresets.Thumper[pi].kick, voice);
            case TraxVoice.Snare:   return MakeDrumCell(rnd, p, TraxPresets.Thumper[pi].snare, voice);
            case TraxVoice.Hat:     return MakeDrumCell(rnd, p, TraxPresets.Thumper[pi].hat, voice);
            case TraxVoice.Bass:    return MakeBassCell(rnd, p, TraxPresets.Gloworm[pi]);
            case TraxVoice.Lead:    return MakeLeadCell(rnd, p, TraxPresets.Siren[pi]);
            case TraxVoice.Moss:    return MakeMossCell(rnd, TraxPresets.MossRhythms[vi]);
            case TraxVoice.Spindle: return MakeSpindleCell(rnd, p, TraxPresets.Spindle[pi].shape);
        }
        throw new ArgumentOutOfRangeException("voice");
    }

    // ── assembly ─────────────────────────────────────────────────────────

    public static TraxPhrase Generate(TraxTrack track, TraxParams p)
    {
        var phrase = new TraxPhrase();
        int[] prog = ProgressionFor(track);
        int[] leadBars = TraxPresets.Siren[track.PresetOf("SIREN") % TraxPresets.PresetCount].bars;

        for (int v = 0; v < VoiceCount; v++)
        {
            TraxVoice voice = (TraxVoice)v;
            TraxStep[] cell = CellFor(track, voice, p);
            bool melodic = IsMelodic(voice);
            bool isMoss = voice == TraxVoice.Moss;

            for (int b = 0; b < Bars; b++)
            {
                // SIREN's preset can silence whole bars — that is what makes
                // ANSWER read as call-and-response instead of constant noodling.
                bool silent = voice == TraxVoice.Lead && leadBars[b] == 0;
                for (int s = 0; s < Steps; s++)
                {
                    TraxStep st = cell[s];
                    if (silent) st = new TraxStep();
                    else if (st.on && melodic) st.degree = prog[b] + (isMoss ? 0 : st.off);
                    phrase.Set(voice, b, s, st);
                }
            }

            var fillRnd = new TraxPrng.Rng(track.FillSeed(voice));
            ApplyFill(phrase, voice, fillRnd, p, HalfFillBar, HalfFillStart, prog[HalfFillBar], 0.55);
            ApplyFill(phrase, voice, fillRnd, p, FullFillBar, FullFillStart, prog[FullFillBar], 1.0);
        }

        return phrase;
    }

    /// <summary>
    /// Overwrite the tail of a bar. `weight` scales how busy it is, so the
    /// half-phrase turnaround stays lighter than the end-of-phrase one.
    /// </summary>
    static void ApplyFill(TraxPhrase phrase, TraxVoice voice, TraxPrng.Rng rnd, TraxParams p,
                          int bar, int from, int root, double weight)
    {
        // The pad rides straight through a fill — cutting the harmony out from
        // under a turnaround is what makes it sound like a mistake. It also must
        // consume NO draws, or every other voice's fill would shift.
        if (voice == TraxVoice.Moss) return;

        for (int s = from; s < Steps; s++)
        {
            double r = rnd.Next(), rv = rnd.Next(), rn = rnd.Next(), rx = rnd.Next();   // always 4

            double prob;
            switch (voice)
            {
                case TraxVoice.Kick:    prob = (0.3 + p.density * 0.35) * weight; break;
                case TraxVoice.Snare:   prob = (0.6 + p.density * 0.35) * weight; break;
                case TraxVoice.Hat:     prob = 0.75 * weight; break;
                case TraxVoice.Bass:    prob = (0.5 + p.density * 0.3) * weight; break;
                case TraxVoice.Lead:    prob = (0.35 + p.density * 0.3) * weight; break;
                case TraxVoice.Spindle: prob = 0.8 * weight; break;
                default:                prob = 0; break;
            }

            TraxStep st = new TraxStep();
            if (r < prob)
            {
                double nudge = (rn * 2 - 1) * p.nudgeSeconds;
                st.on = true;
                st.nudge = nudge;
                switch (voice)
                {
                    case TraxVoice.Kick:  st.vel = 0.8 + rv * 0.15; break;
                    case TraxVoice.Snare: st.vel = 0.45 + rv * 0.5; break;
                    case TraxVoice.Hat:   st.vel = 0.4 + rv * 0.35; st.open = s == Steps - 1; break;
                    case TraxVoice.Bass:  st.vel = 0.8 + rv * 0.2; st.degree = root; st.dur = 1; break;
                    case TraxVoice.Lead:
                        st.vel = 0.65 + rv * 0.3;
                        st.degree = root + ChordTones[(int)Math.Floor(rx * ChordTones.Length)];
                        st.dur = 1;
                        break;
                    case TraxVoice.Spindle:
                        st.vel = 0.4 + rv * 0.2;
                        st.degree = root + TraxPresets.ArpTones[(int)Math.Floor(rx * TraxPresets.ArpTones.Length)];
                        st.dur = 1;
                        break;
                }
            }
            phrase.Set(voice, bar, s, st);
        }
    }
}
