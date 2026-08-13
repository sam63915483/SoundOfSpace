using System;

/// <summary>
/// The six macro dials. Continuous 0-10.
/// `void` is a C# keyword, so the VOID dial is <c>voidness</c> here — it is
/// still "void" in the JS engine, the golden file and the UI.
/// </summary>
[Serializable]
public struct TraxDials
{
    public double pulse;
    public double crunch;
    public double goo;
    public double voidness;
    public double jitter;
    public double warp;

    public TraxDials(double pulse, double crunch, double goo, double voidness, double jitter, double warp)
    {
        this.pulse = pulse; this.crunch = crunch; this.goo = goo;
        this.voidness = voidness; this.jitter = jitter; this.warp = warp;
    }

    /// Fixed dial order — this ordering is baked into the seed hash. Never reorder.
    public double Get(int i)
    {
        switch (i)
        {
            case 0: return pulse;
            case 1: return crunch;
            case 2: return goo;
            case 3: return voidness;
            case 4: return jitter;
            case 5: return warp;
            default: return 0;
        }
    }

    public TraxDials With(int i, double v)
    {
        TraxDials d = this;
        switch (i)
        {
            case 0: d.pulse = v; break;
            case 1: d.crunch = v; break;
            case 2: d.goo = v; break;
            case 3: d.voidness = v; break;
            case 4: d.jitter = v; break;
            case 5: d.warp = v; break;
        }
        return d;
    }

    public static TraxDials Default
    {
        get { return new TraxDials(5, 3, 5, 4, 4, 5); }
    }
}

/// <summary>
/// The macro table: six dials → every number the audio backend needs.
/// PORT OF <c>prototypes/shuttle-computer/engine/params.js</c>.
///
/// This is the single place dial semantics live, so "what does GOO do" has
/// exactly one answer in the codebase — and the same answer in both backends.
/// </summary>
public struct TraxParams
{
    // clock
    public double bpm;
    public double density;

    // timbre
    public double oscMorph;      // 0 = sine, 0.5 = saw, 1 = square
    public double drive;
    public int crushLevels;

    // filter (GOO)
    public double filterBase;
    public double filterQ;
    public double lfoRate;
    public double lfoDepthOct;

    // CAVE (VOID)
    public double caveSend;
    public double caveFeedback;
    public double caveMix;

    // rhythm (JITTER)
    public double syncopation;
    public double nudgeSeconds;
    public double hatScatter;

    // pitch (WARP)
    public int scaleIdx;
    public double detuneCents;

    public TraxDials dials;

    public static TraxParams Compute(TraxDials d)
    {
        double pulse    = d.pulse    / 10.0;
        double crunch   = d.crunch   / 10.0;
        double goo      = d.goo      / 10.0;
        double voidness = d.voidness / 10.0;
        double jitter   = d.jitter   / 10.0;
        double warp     = d.warp     / 10.0;

        TraxParams p = new TraxParams();

        // VOID eats note density — empty space is partly just fewer events.
        p.density = (0.25 + pulse * 0.5) * (1 - voidness * 0.5);
        p.bpm = 60 + pulse * 110;                                  // 60..170

        p.oscMorph = crunch;
        p.drive = crunch;
        // Amplitude quantization. 16 levels at full crunch is audibly gritty
        // without turning the whole mix into a buzz.
        p.crushLevels = (int)TraxPrng.JsRound(64 - crunch * 48);

        // Open and clean at 0, closed and squelchy at 10.
        p.filterBase = 400 * Math.Pow(2, (1 - goo) * 3);           // 3200Hz .. 400Hz
        p.filterQ = 1 + goo * 18;
        p.lfoRate = 0.2 + goo * 2.8;
        p.lfoDepthOct = goo * 2;

        p.caveSend = voidness * 0.8;
        p.caveFeedback = 0.2 + voidness * 0.65;
        p.caveMix = 0.2 + voidness * 0.6;

        p.syncopation = jitter;
        p.nudgeSeconds = jitter * 0.02;
        p.hatScatter = jitter;

        // WARP runs the opposite way to the other five: 0 is straight and
        // melodic, 10 is maximally warped. The scale table is still ordered
        // alien-first, so the dial is inverted HERE and nowhere else.
        p.scaleIdx = TraxScales.ScaleIndexFor(10 - d.warp);
        p.detuneCents = warp * 35;                                  // warped = detuned

        p.dials = d;
        return p;
    }

    /// <summary>
    /// Which dials require regenerating the pattern (applied at the next bar
    /// boundary) rather than ramping live on the running voices. PULSE is in
    /// both camps — BPM rides live, but its density term needs a regen.
    /// </summary>
    public static bool NeedsRegen(TraxDials a, TraxDials b)
    {
        // Compared at seed resolution — a sub-quantum wiggle isn't a new pattern.
        return Q(a.pulse)    != Q(b.pulse)
            || Q(a.voidness) != Q(b.voidness)
            || Q(a.jitter)   != Q(b.jitter)
            || Q(a.warp)     != Q(b.warp);
    }

    static int Q(double v) { return (int)TraxPrng.JsRound(v * 2.0); }
}

/// UI metadata for the dials. Order matches the seed's dial order.
public static class TraxDialDefs
{
    public struct Def
    {
        public int index;
        public string label;
        public string flavor;
        public Def(int i, string l, string f) { index = i; label = l; flavor = f; }
    }

    public static readonly Def[] All =
    {
        new Def(0, "PULSE",    "how fast it hits"),
        new Def(1, "CRUNCH",   "how mean it sounds"),
        new Def(2, "GOO",      "how wet and squelchy"),
        new Def(3, "VOID",     "how much empty space"),
        new Def(4, "JITTER",   "how twitchy the rhythm"),
        new Def(5, "WARP",     "how warped the pitch is")
    };
}
