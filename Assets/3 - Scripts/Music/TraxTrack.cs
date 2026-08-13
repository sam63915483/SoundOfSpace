using System;

/// <summary>
/// What a track IS.
/// PORT OF <c>prototypes/shuttle-computer/engine/track.js</c>.
///
/// Six dials shape the sound, six modules choose the parts, one key sets where
/// it sits. That whole thing is the track — and it is what a cassette will
/// eventually store.
///
/// ── The important change: the pattern seed no longer comes from the dials ──
/// It used to. That is why turning PULSE up produced a DIFFERENT track that
/// happened to be faster, instead of making your track busier — and why the
/// whole thing felt like re-rolling dice rather than writing something.
///
/// Now each voice seeds from its module's PRESET and VARIATION only. The dials
/// feed the generator as parameters, so they change how a pattern is filled in
/// without changing which pattern it is. VARIATION is the re-roll, and it is
/// per-module and repeatable, so you can keep drums you like while cycling
/// melodies.
/// </summary>
public sealed class TraxTrack
{
    public TraxDials dials;
    public int key;                                   // semitones above the base root (A)
    public readonly int[] preset = new int[TraxPresets.ModuleCount];
    public readonly int[] variation = new int[TraxPresets.ModuleCount];

    /// WHICH MODULES ARE PLAYING. This lives on the track, not on the
    /// instrument, because muting THUMPER is a compositional decision — a
    /// printed cassette of a drumless track has to stay drumless forever.
    public readonly bool[] active = new bool[TraxPresets.ModuleCount];

    public static readonly string[] KeyNames =
        { "A", "A#", "B", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#" };

    public static TraxTrack Default()
    {
        var t = new TraxTrack();
        t.dials = TraxDials.Default;
        t.key = 0;
        // SIREN starts on SONG and CAVE on HALL — the most useful defaults to
        // hear the instrument with, rather than the first entry in each bank.
        t.preset[TraxPresets.ModuleIndex("SIREN")] = 1;
        t.preset[TraxPresets.ModuleIndex("CAVE")] = 1;
        for (int m = 0; m < TraxPresets.ModuleCount; m++) t.active[m] = true;
        return t;
    }

    public TraxTrack Clone()
    {
        var t = new TraxTrack();
        t.dials = dials;
        t.key = key;
        Array.Copy(preset, t.preset, preset.Length);
        Array.Copy(variation, t.variation, variation.Length);
        Array.Copy(active, t.active, active.Length);
        return t;
    }

    static int Wrap(int v, int n) { return ((v % n) + n) % n; }

    public int PresetOf(string module) { return preset[TraxPresets.ModuleIndex(module)]; }
    public int VariationOf(string module) { return variation[TraxPresets.ModuleIndex(module)]; }

    public TraxTrack WithPreset(string module, int index)
    {
        var t = Clone();
        t.preset[TraxPresets.ModuleIndex(module)] = Wrap(index, TraxPresets.PresetCount);
        return t;
    }

    public TraxTrack WithVariation(string module, int index)
    {
        var t = Clone();
        t.variation[TraxPresets.ModuleIndex(module)] = Wrap(index, TraxPresets.VariationCount);
        return t;
    }

    public TraxTrack WithKey(int k)
    {
        var t = Clone();
        t.key = Wrap(k, 12);
        return t;
    }

    public bool ActiveOf(string module) { return active[TraxPresets.ModuleIndex(module)]; }

    public TraxTrack WithActive(string module, bool on)
    {
        var t = Clone();
        t.active[TraxPresets.ModuleIndex(module)] = on;
        return t;
    }

    /// The active set as one byte, module order, bit 0 = THUMPER. Used for
    /// identity hashing so JS and C# have an unambiguous thing to agree on.
    public int ActiveMask()
    {
        int mask = 0;
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
            if (active[m]) mask |= (1 << m);
        return mask & 0xff;
    }

    public int ActiveCount()
    {
        int n = 0;
        for (int m = 0; m < TraxPresets.ModuleCount; m++) if (active[m]) n++;
        return n;
    }

    public TraxTrack WithDial(int dialIndex, double value)
    {
        var t = Clone();
        t.dials = t.dials.With(dialIndex, value);
        return t;
    }

    public string KeyName { get { return KeyNames[Wrap(key, 12)]; } }

    // ── seeding ──────────────────────────────────────────────────────────

    /// The stream a voice generates from. Deliberately NOT a function of the dials.
    public uint VoiceSeed(TraxVoice voice)
    {
        int mod = TraxPresets.ModuleIndex(TraxModules.For(voice));
        var bytes = new byte[3];
        bytes[0] = (byte)(mod & 0xff);
        bytes[1] = (byte)(Wrap(preset[mod], TraxPresets.PresetCount) & 0xff);
        bytes[2] = (byte)(Wrap(variation[mod], TraxPresets.VariationCount) & 0xff);
        return TraxPrng.Fnv1a32(bytes) ^ TraxPrng.ConstFor(voice);
    }

    /// Fills get their own stream per voice, so a turnaround never disturbs the
    /// groove it decorates.
    public uint FillSeed(TraxVoice voice)
    {
        return VoiceSeed(voice) ^ TraxPrng.VoiceFill;
    }

    /// <summary>
    /// Identity hash over EVERYTHING that affects the sound. This is what a
    /// cassette is keyed on, so it must cover dials, key, presets, variations
    /// AND which modules were playing — a drumless take of the same arrangement
    /// is a different song, and an alien has to be able to tell.
    /// </summary>
    public uint TrackId()
    {
        var bytes = new byte[TraxPrng.DialCount + 1 + TraxPresets.ModuleCount * 2 + 1];
        int n = 0;
        for (int i = 0; i < TraxPrng.DialCount; i++)
        {
            int q = (int)TraxPrng.JsRound(dials.Get(i) * 2.0);
            if (q < 0) q = 0;
            if (q > 20) q = 20;
            bytes[n++] = (byte)q;
        }
        bytes[n++] = (byte)Wrap(key, 12);
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
        {
            bytes[n++] = (byte)Wrap(preset[m], TraxPresets.PresetCount);
            bytes[n++] = (byte)Wrap(variation[m], TraxPresets.VariationCount);
        }
        bytes[n++] = (byte)ActiveMask();
        return TraxPrng.Fnv1a32(bytes);
    }

    /// <summary>
    /// Which changes require regenerating patterns. Presets, variations and the
    /// pattern-shaping dials do; KEY and the timbre dials do not — key is
    /// applied at note time and timbre rides live.
    ///
    /// The ACTIVE set is deliberately absent. Every voice is generated whether
    /// or not it is audible, and each draws from its own constant-keyed stream,
    /// so muting a module cannot disturb the others. That is the same guarantee
    /// that lets a plugin be unlocked later without changing an already-printed
    /// tape — do not "optimise" it into skipping generation for muted voices.
    /// </summary>
    public static bool NeedsRegen(TraxTrack a, TraxTrack b)
    {
        // PULSE, VOID, JITTER, WARP — the dials that change which optional hits
        // land or which scale is in use.
        int[] shaping = { 0, 3, 4, 5 };
        foreach (int i in shaping)
            if (Q(a.dials.Get(i)) != Q(b.dials.Get(i))) return true;

        for (int m = 0; m < TraxPresets.ModuleCount; m++)
        {
            if (a.preset[m] != b.preset[m]) return true;
            if (a.variation[m] != b.variation[m]) return true;
        }
        return false;
    }

    static int Q(double v) { return (int)TraxPrng.JsRound(v * 2.0); }
}

/// Which rack module owns which voice.
public static class TraxModules
{
    public static string For(TraxVoice v)
    {
        switch (v)
        {
            case TraxVoice.Kick:
            case TraxVoice.Snare:
            case TraxVoice.Hat:     return "THUMPER";
            case TraxVoice.Bass:    return "GLOWORM";
            case TraxVoice.Lead:    return "SIREN";
            case TraxVoice.Moss:    return "MOSS";
            case TraxVoice.Spindle: return "SPINDLE";
        }
        return "THUMPER";
    }
}
