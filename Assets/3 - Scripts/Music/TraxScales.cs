using System;

/// <summary>
/// Pitch tables and degree → Hz.
/// PORT OF <c>prototypes/shuttle-computer/engine/scales.js</c>. See TraxPrng for
/// the porting rules.
///
/// Ordered by HOMESICK: index 0 is maximally alien, index 5 is familiar and
/// melancholic. (The handoff listed whole-tone before chromatic-cluster; they're
/// swapped here so alienness falls monotonically across the sweep, otherwise the
/// dial feels broken in the middle. Sam can flip them back in one line — but the
/// same edit must be made in scales.js and the golden file regenerated.)
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
        new ScaleDef("NATMINOR",  new[] { 0, 2, 3, 5, 7, 8, 10 })   // fully homesick
    };

    /// Master key, fixed for the whole game. A2.
    public const int RootMidi = 45;

    // Octave offset per melodic voice. Lives here rather than in the audio
    // backend so both backends agree on register — and so the golden vectors
    // check the same numbers the synth uses.
    //
    // Bass sits at -1, not -2: at -2 the low end of the CLUSTER scale reaches
    // ~19Hz, below hearing, doing nothing but eating headroom.
    public const int BassOctave = -1;
    public const int LeadOctave = 1;

    public static int ScaleIndexFor(double homesick)
    {
        int i = (int)Math.Floor((homesick / 10.0) * Scales.Length);
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

    public static double DegreeToFreq(int degree, int scaleIdx, int octaveOffset)
    {
        return MidiToFreq(DegreeToMidi(degree, scaleIdx, octaveOffset));
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
        return v == TraxVoice.Bass ? BassOctave : LeadOctave;
    }
}
