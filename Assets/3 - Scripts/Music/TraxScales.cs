using System;

/// <summary>
/// Pitch tables and degree → Hz.
/// PORT OF <c>prototypes/shuttle-computer/engine/scales.js</c>. See TraxPrng for
/// the porting rules.
///
/// Ordered by FAMILIARITY: index 0 is maximally alien, index 5 is familiar and
/// melancholic. The WARP dial runs the other way (10 = alien), so TraxParams
/// inverts it before indexing here — this table is NOT the dial.
/// (The handoff listed whole-tone before chromatic-cluster; they're swapped here
/// so alienness falls monotonically across the sweep, otherwise the dial feels
/// broken in the middle. Sam can flip them back in one line — but the same edit
/// must be made in scales.js and the golden file regenerated.)
/// </summary>
public static class TraxScales
{
    public struct ScaleDef
    {
        public string name;
        public int[] steps;
        public ScaleDef(string n, int[] s) { name = n; steps = s; }
    }

    public static readonly ScaleDef[] Scales =
    {
        new ScaleDef("CLUSTER",   new[] { 0, 1, 2, 6, 7, 8 }),      // two semitone clusters a tritone apart
        new ScaleDef("WHOLETONE", new[] { 0, 2, 4, 6, 8, 10 }),     // no leading tone, floats
        new ScaleDef("HIRAJOSHI", new[] { 0, 2, 3, 7, 8 }),         // alien but pretty
        new ScaleDef("PHRYGIAN",  new[] { 0, 1, 3, 5, 7, 8, 10 }),  // flat-2 darkness
        new ScaleDef("MINORPENT", new[] { 0, 3, 5, 7, 10 }),        // familiar, safe
        new ScaleDef("NATMINOR",  new[] { 0, 2, 3, 5, 7, 8, 10 })   // fully familiar
    };

    /// Master key, fixed for the whole game. A2.
    public const int RootMidi = 45;

    // Octave offset per melodic voice. Lives here rather than in the audio
    // backend so both backends agree on register — and so the golden vectors
    // check the same numbers the synth uses.
    //
    // Bass sits at -1, not -2: at -2 the low end of the CLUSTER scale reaches
    // ~19Hz, below hearing, doing nothing but eating headroom.
    // MOSS sits in the middle where a pad belongs — under the lead, above the
    // bass, so the triad fills the gap instead of fighting either.
    public const int BassOctave = -1;
    public const int LeadOctave = 1;
    public const int MossOctave = 0;
    public const int SpindleOctave = 1;

    /// Takes FAMILIARITY (0 = alien, 10 = familiar), not the WARP dial value.
    public static int ScaleIndexFor(double familiarity)
    {
        int i = (int)Math.Floor((familiarity / 10.0) * Scales.Length);
        if (i < 0) i = 0;
        if (i >= Scales.Length) i = Scales.Length - 1;
        return i;
    }

    /// <summary>
    /// Degree may run past either end of the table — it wraps into higher or
    /// lower octaves, so callers can ask for "degree 9" or "degree -3" and get
    /// something sensible instead of a clamp.
    ///
    /// NOTE the (double) cast: `degree / n` on two ints truncates toward zero in
    /// C#, which is wrong for negative degrees. JS's `Math.floor(degree / n)`
    /// floors real division. Trap 3 in TraxPrng.
    /// </summary>
    public static int DegreeToMidi(int degree, int scaleIdx, int octaveOffset)
    {
        int[] steps = Scales[scaleIdx].steps;
        int n = steps.Length;
        int oct = (int)Math.Floor((double)degree / n);
        int d = degree - oct * n;                     // true modulo, negatives included
        return RootMidi + steps[d] + 12 * (oct + octaveOffset);
    }

    public static double MidiToFreq(int midi)
    {
        return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    }

    /// `key` transposes by whole semitones. Applied HERE, at note time, rather
    /// than folded into scale degrees — so turning the key knob can never
    /// regenerate a pattern, it just moves the same one.
    public static double DegreeToFreq(int degree, int scaleIdx, int octaveOffset, int key)
    {
        return MidiToFreq(DegreeToMidi(degree, scaleIdx, octaveOffset) + key);
    }

    /// <summary>
    /// Register each voice is allowed to occupy, in MIDI notes.
    ///
    /// Without this the bass drops to ~22Hz on the CLUSTER scale (its lowest
    /// degree lands two octaves down in a 6-note table) — inaudible rumble that
    /// eats headroom and does nothing but make everything else quieter. Folding
    /// by whole OCTAVES keeps the note in the scale, so this can never introduce
    /// a wrong pitch, only a wrong-by-an-octave one, and only where the
    /// alternative was silence.
    /// </summary>
    public static void RangeFor(TraxVoice v, out int lo, out int hi)
    {
        switch (v)
        {
            case TraxVoice.Bass:    lo = 28; hi = 55; return;
            case TraxVoice.Lead:    lo = 52; hi = 84; return;
            case TraxVoice.Moss:    lo = 45; hi = 74; return;
            case TraxVoice.Spindle: lo = 55; hi = 88; return;
            default:                lo = 0;  hi = 127; return;
        }
    }

    public static int VoiceMidi(int degree, int scaleIdx, TraxVoice voice, int key)
    {
        int m = DegreeToMidi(degree, scaleIdx, OctaveFor(voice)) + key;
        int lo, hi;
        RangeFor(voice, out lo, out hi);
        while (m < lo) m += 12;
        while (m > hi) m -= 12;
        return m;
    }

    public static double VoiceFreq(int degree, int scaleIdx, TraxVoice voice, int key)
    {
        return MidiToFreq(VoiceMidi(degree, scaleIdx, voice, key));
    }

    /// True iff a MIDI note belongs to the scale, in any octave.
    public static bool IsInScale(int midi, int scaleIdx)
    {
        int[] steps = Scales[scaleIdx].steps;
        int pc = (midi - RootMidi) % 12;
        if (pc < 0) pc += 12;
        for (int i = 0; i < steps.Length; i++) if (steps[i] == pc) return true;
        return false;
    }

    public static int OctaveFor(TraxVoice v)
    {
        switch (v)
        {
            case TraxVoice.Bass:    return BassOctave;
            case TraxVoice.Moss:    return MossOctave;
            case TraxVoice.Spindle: return SpindleOctave;
            default:                return LeadOctave;
        }
    }
}
