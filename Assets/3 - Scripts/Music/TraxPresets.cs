/// <summary>
/// Preset banks: the parts you CHOOSE, as opposed to the dials, which shape.
/// PORT OF <c>prototypes/shuttle-computer/engine/presets.js</c>.
///
/// ── Why templates and not more probability tuning ────────────────────────
/// Sam, after playtesting: "it doesnt really feel like you are the one creating
/// the music... it feels like your just editing some chord and drum loops that
/// are already there." The fix is real decisions whose outcomes are all good,
/// not a finer grip on the dice.
///
/// So the things that should be RECOGNISABLE are authored by hand — drum
/// grooves, arp shapes, chord progressions. The things that should vary are
/// still generated, with the preset setting their character. A preset that
/// always played the same melody would be the preset-loop problem one level up.
///
/// ── Weight strings ───────────────────────────────────────────────────────
/// Rhythm templates are 16 steps:
///    3  always sounds     — the skeleton of the groove
///    2  usually sounds    — thins out at low PULSE
///    1  optional / ghost  — fills in at high PULSE
///    .  never
/// The preset gives the shape, PULSE decides how busy, VARIATION decides which
/// of the 2s and 1s land. That is what lets a groove get busier without
/// becoming a different groove.
/// </summary>
public static class TraxPresets
{
    public const int PresetCount = 5;
    public const int VariationCount = 8;

    public static readonly string[] ModuleNames =
        { "THUMPER", "GLOWORM", "MOSS", "SIREN", "SPINDLE", "CAVE" };

    public const int ModuleCount = 6;

    public static int ModuleIndex(string name)
    {
        for (int i = 0; i < ModuleNames.Length; i++)
            if (ModuleNames[i] == name) return i;
        return -1;
    }

    /// Spaces and bars in a template are decorative; only 16 step characters count.
    public static int[] ParseWeights(string s)
    {
        var outv = new int[16];
        int i = 0;
        for (int c = 0; c < s.Length && i < 16; c++)
        {
            char ch = s[c];
            if (ch == ' ' || ch == '|') continue;
            outv[i++] = ch == '.' ? 0 : (ch - '0');
        }
        return outv;
    }

    // ── THUMPER ──────────────────────────────────────────────────────────
    public struct Groove
    {
        public string name, kick, snare, hat;
        public Groove(string n, string k, string s, string h) { name = n; kick = k; snare = s; hat = h; }
    }

    public static readonly Groove[] Thumper =
    {
        new Groove("STRAIGHT", "3...2..1 3...2..1", "....3..1 ....3..2", "2.2.2.2. 2.2.2.2."),
        new Groove("BREAK",    "3..2..1. ..3.2..1", "....3.1. ..1.3..2", "2.211.21 2.211.22"),
        new Groove("STOMP",    "3.1.3.1. 3.1.3.1.", "....3... ....3...", "3...2... 3...2..."),
        new Groove("HALFTIME", "3.....1. ....2...", "........ 3.....1.", "2...1...2...1..."),
        new Groove("SCATTER",  "3..1.2.1 3.1..2.1", "..1.3.1. .21.3.11", "32323232 32323232")
    };

    // ── GLOWORM ──────────────────────────────────────────────────────────
    public struct BassPreset
    {
        public string name, hits;
        public int[] contour;      // scale-degree offsets from the bar's chord root
        public BassPreset(string n, string h, int[] c) { name = n; hits = h; contour = c; }
    }

    public static readonly BassPreset[] Gloworm =
    {
        new BassPreset("ROOTS",  "3...2...3...2...",
            new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        new BassPreset("OCTAVE", "3..12..13..12..1",
            new[] { 0, 0, 0, -7, 0, 0, 0, -7, 0, 0, 0, -7, 0, 0, 0, -7 }),
        new BassPreset("WALK",   "3.2.2.1.3.2.2.1.",
            new[] { 0, 0, 2, 2, 4, 4, 2, 2, 0, 0, 2, 2, 4, 4, 5, 5 }),
        new BassPreset("PULSE",  "3232323232323232",
            new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        new BassPreset("SLIDE",  "3..2.1.23..2.1.2",
            new[] { 0, 0, 0, 4, 0, 2, 0, -7, 0, 0, 0, 4, 0, 2, 0, 5 })
    };

    // ── MOSS ─────────────────────────────────────────────────────────────
    // The single biggest musical choice in the app: it sets what bass, lead and
    // arp all harmonise to, whether or not the pad itself is switched on.
    public struct Progression
    {
        public string name;
        public int[] prog;
        public Progression(string n, int[] p) { name = n; prog = p; }
    }

    public static readonly Progression[] Moss =
    {
        new Progression("HOLLOW", new[] { 0, 5, 3, 4 }),
        new Progression("CLIMB",  new[] { 0, 2, 3, 4 }),
        new Progression("FALL",   new[] { 0, 4, 3, 5 }),
        new Progression("VAMP",   new[] { 0, 0, 3, 4 }),
        new Progression("STEP",   new[] { 0, 3, 4, 0 })
    };

    /// Pad rhythms, chosen by MOSS's VARIATION — so the progression and how it
    /// is played are separate decisions.
    public struct PadRhythm
    {
        public string name, hits;
        public int dur;
        public PadRhythm(string n, string h, int d) { name = n; hits = h; dur = d; }
    }

    public static readonly PadRhythm[] MossRhythms =
    {
        new PadRhythm("held",     "3...............", 16),
        new PadRhythm("halves",   "3.......3.......", 8),
        new PadRhythm("quarters", "3...3...3...3...", 4),
        new PadRhythm("offbeat",  "..3...3...3...3.", 3),
        new PadRhythm("swell",    "3.......2.......", 8),
        new PadRhythm("stabs",    "3.3.....3.3.....", 2),
        new PadRhythm("long",     "3...............", 16),
        new PadRhythm("push",     "3.....3.....3...", 5)
    };

    // ── SIREN ────────────────────────────────────────────────────────────
    // The lead is GENERATED; the preset sets its character.
    public struct LeadPreset
    {
        public string name;
        public double gate;     // how often it plays at all
        public double leap;     // chance of a jump instead of stepwise motion
        public int len;         // note length in steps
        public int[] bars;      // which bars of the phrase it plays in
        public LeadPreset(string n, double g, double lp, int l, int[] b)
        { name = n; gate = g; leap = lp; len = l; bars = b; }
    }

    public static readonly LeadPreset[] Siren =
    {
        new LeadPreset("SPARSE", 0.35, 0.10, 3, new[] { 1, 1, 1, 1 }),
        new LeadPreset("SONG",   0.60, 0.12, 2, new[] { 1, 1, 1, 1 }),
        new LeadPreset("BUSY",   0.85, 0.18, 1, new[] { 1, 1, 1, 1 }),
        new LeadPreset("ANSWER", 0.65, 0.14, 2, new[] { 0, 1, 0, 1 }),
        new LeadPreset("HELD",   0.25, 0.06, 6, new[] { 1, 0, 1, 1 })
    };

    // ── SPINDLE ──────────────────────────────────────────────────────────
    // Exact sequences: an arp that rolled its own order would just be a fast
    // random melody.
    public struct ArpPreset
    {
        public string name;
        public int[] shape;
        public ArpPreset(string n, int[] s) { name = n; shape = s; }
    }

    public static readonly ArpPreset[] Spindle =
    {
        new ArpPreset("UP",     new[] { 0, 1, 2, 3 }),
        new ArpPreset("DOWN",   new[] { 3, 2, 1, 0 }),
        new ArpPreset("ROLL",   new[] { 0, 1, 2, 3, 2, 1 }),
        new ArpPreset("JUMP",   new[] { 0, 2, 1, 3 }),
        new ArpPreset("TUMBLE", new[] { 0, 3, 1, 2, 0, 2 })
    };

    public static readonly int[] ArpTones = { 0, 2, 4, 6 };

    // ── CAVE ─────────────────────────────────────────────────────────────
    // Effect settings, not patterns.
    public struct SpacePreset
    {
        public string name;
        public double timeA, timeB, damp, fb;
        public SpacePreset(string n, double a, double b, double d, double f)
        { name = n; timeA = a; timeB = b; damp = d; fb = f; }
    }

    public static readonly SpacePreset[] Cave =
    {
        new SpacePreset("ROOM",   0.07, 0.11,  3200, 0.55),
        new SpacePreset("HALL",   0.19, 0.31,  2600, 0.80),
        new SpacePreset("CANYON", 0.37, 0.53,  1600, 0.92),
        new SpacePreset("SLAP",   0.12, 0.125, 4200, 0.20),
        new SpacePreset("VOID",   0.29, 0.47,  1100, 0.97)
    };

    public static string PresetName(string module, int index)
    {
        int i = ((index % PresetCount) + PresetCount) % PresetCount;
        switch (module)
        {
            case "THUMPER": return Thumper[i].name;
            case "GLOWORM": return Gloworm[i].name;
            case "MOSS":    return Moss[i].name;
            case "SIREN":   return Siren[i].name;
            case "SPINDLE": return Spindle[i].name;
            case "CAVE":    return Cave[i].name;
        }
        return "?";
    }
}
