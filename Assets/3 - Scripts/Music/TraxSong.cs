using System;
using System.Collections.Generic;

/// <summary>
/// What a SONG is: an ordered list of SECTIONS, each owning a whole track plus
/// a length in bars. PORT OF <c>prototypes/shuttle-computer/engine/song.js</c>.
///
/// ── Ownership discipline (same as library records) ───────────────────────
/// A section owns a CLONED track, never a pointer into another section or a
/// shelf record. Editing section B must never reach into section A, even when
/// B was created by duplicating A.
///
/// ── Playback semantics ───────────────────────────────────────────────────
/// A section's track still generates the usual 4-bar phrase. The section's
/// BARS says how long the song stays on it: 8 bars = the phrase twice, 2 bars
/// = just its first half. The LAST bar of a section always remaps onto the
/// phrase's fill bar so every hand-off gets a turnaround.
///
/// Pure logic, NO UnityEngine — compiled standalone by the library test suite
/// alongside TraxLibrary. Keep it that way.
/// </summary>
public sealed class TraxSection
{
    public int bars;
    public TraxTrack track;

    public TraxSection(TraxTrack t, int barCount)
    {
        track = t.Clone();
        bars = TraxSong.ClampBars(barCount);
    }
}

public sealed class TraxSong
{
    public const int SectionMinBars = 1;
    public const int SectionMaxBars = 16;
    public const int MaxSections = 8;

    /// Below this dial-space distance, sections just hand off on the fill.
    /// Kept for parity with the JS engine; the boundary FX are currently
    /// UNGATED (Sam, 2026-08-17) but the measure still scales nothing away.
    public const double TransitionFxMin = 0.25;

    public readonly List<TraxSection> sections = new List<TraxSection>();

    public static int ClampBars(int b)
    {
        return b < SectionMinBars ? SectionMinBars : b > SectionMaxBars ? SectionMaxBars : b;
    }

    /// A one-section song of the given track — what every legacy single-track
    /// record becomes on load, and what NEW PROJECT starts as.
    public static TraxSong FromTrack(TraxTrack track)
    {
        var s = new TraxSong();
        s.sections.Add(new TraxSection(track, 4));
        return s;
    }

    public TraxSong Clone()
    {
        var s = new TraxSong();
        for (int i = 0; i < sections.Count; i++)
            s.sections.Add(new TraxSection(sections[i].track, sections[i].bars));
        return s;
    }

    /// Sections are lettered, not numbered — "SEC B" reads like an
    /// arrangement, "SEC 2" reads like an index.
    public static string SectionLabel(int i)
    {
        return ((char)('A' + (i % 26))).ToString();
    }

    // ── mutators ─────────────────────────────────────────────────────────
    // The song object is owned by one screen at a time, so these mutate in
    // place; identity is always DERIVED via SongId, never bookkept.

    /// A new section starts as a copy of the one being edited — a blank
    /// default in the middle of a song is never what anyone wants. Returns
    /// the new section's index, or -1 if the song is full.
    public int AddSection(int copyIndex)
    {
        if (sections.Count >= MaxSections) return -1;
        int i = copyIndex < 0 ? sections.Count - 1
              : copyIndex >= sections.Count ? sections.Count - 1 : copyIndex;
        sections.Insert(i + 1, new TraxSection(sections[i].track, 4));
        return i + 1;
    }

    /// A song is never empty — removing the last section is refused.
    public bool RemoveSection(int index)
    {
        if (sections.Count <= 1 || index < 0 || index >= sections.Count) return false;
        sections.RemoveAt(index);
        return true;
    }

    public bool SetSectionBars(int index, int bars)
    {
        if (index < 0 || index >= sections.Count) return false;
        int clamped = ClampBars(bars);
        if (sections[index].bars == clamped) return false;
        sections[index].bars = clamped;
        return true;
    }

    // ── queries ──────────────────────────────────────────────────────────

    public int TotalBars()
    {
        int n = 0;
        for (int i = 0; i < sections.Count; i++) n += sections[i].bars;
        return n;
    }

    public int TotalSteps() { return TotalBars() * TraxPhrase.Steps; }

    /// <summary>
    /// Identity over EVERYTHING audible: each section's full track identity
    /// plus its length, in order. Reordering sections is a different song; so
    /// is stretching one. Must match songId() in engine/song.js byte for byte.
    /// </summary>
    public uint SongId()
    {
        var bytes = new byte[sections.Count * 5];
        int n = 0;
        for (int i = 0; i < sections.Count; i++)
        {
            uint id = sections[i].track.TrackId();
            bytes[n++] = (byte)(id & 0xff);
            bytes[n++] = (byte)((id >> 8) & 0xff);
            bytes[n++] = (byte)((id >> 16) & 0xff);
            bytes[n++] = (byte)((id >> 24) & 0xff);
            bytes[n++] = (byte)(sections[i].bars & 0xff);
        }
        return TraxPrng.Fnv1a32(bytes);
    }

    /// Which section is under a given song-step (0..TotalSteps-1). The UI
    /// playhead and the audio scheduler both use this, so they can never
    /// disagree.
    public void SectionAtStep(int step, out int index, out int stepInSection, out int barInSection)
    {
        int start = 0;
        for (int i = 0; i < sections.Count; i++)
        {
            int len = sections[i].bars * TraxPhrase.Steps;
            if (step < start + len)
            {
                index = i;
                stepInSection = step - start;
                barInSection = stepInSection / TraxPhrase.Steps;
                return;
            }
            start += len;
        }
        index = 0; stepInSection = 0; barInSection = 0;
    }

    public int SectionStartStep(int index)
    {
        int start = 0;
        for (int i = 0; i < index && i < sections.Count; i++)
            start += sections[i].bars * TraxPhrase.Steps;
        return start;
    }

    // ── transitions ──────────────────────────────────────────────────────
    // All derive purely from the adjacent sections, so a printed cassette
    // transitions identically on every machine.

    /// Which bar of the generated 4-bar phrase sounds at this bar of the
    /// section. The LAST bar always plays the fill bar — a no-op for
    /// 4/8/12/16-bar sections, which land on it naturally.
    public static int PatternBarFor(TraxSection sec, int barInSection)
    {
        if (barInSection == sec.bars - 1) return TraxPhrase.FullFillBar;
        return barInSection % TraxPhrase.Bars;
    }

    public static int PatternStepFor(TraxSection sec, int stepInSection)
    {
        int bar = stepInSection / TraxPhrase.Steps;
        return PatternBarFor(sec, bar) * TraxPhrase.Steps + (stepInSection % TraxPhrase.Steps);
    }

    /// How different two sections sound, 0..1 — distance in dial space, since
    /// the dials carry tempo, timbre and mood. /8 because ~8 units of 6-D dial
    /// distance is already "different genre".
    public static double TransitionIntensity(TraxSection a, TraxSection b)
    {
        double sum = 0;
        for (int i = 0; i < TraxPrng.DialCount; i++)
        {
            double d = a.track.dials.Get(i) - b.track.dials.Get(i);
            sum += d * d;
        }
        double v = Math.Sqrt(sum) / 8.0;
        return v > 1.0 ? 1.0 : v;
    }

    // ── genre mix ────────────────────────────────────────────────────────

    public struct MixEntry
    {
        public string name;
        public int bars;
        public double share;
        public int genreIndex;          // into TraxClassifier.Genres
    }

    static int GenreIndexOf(string name)
    {
        for (int i = 0; i < TraxClassifier.Genres.Length; i++)
            if (TraxClassifier.Genres[i].name == name) return i;
        return 0;
    }

    /// How much of the song, by bars, is each genre. A section counts wholly
    /// toward its PRIMARY genre. Sorted biggest share first.
    public List<MixEntry> GenreMix()
    {
        var barsFor = new Dictionary<int, int>();
        for (int i = 0; i < sections.Count; i++)
        {
            int g = GenreIndexOf(TraxClassifier.Classify(sections[i].track.dials).primary.name);
            int cur;
            barsFor.TryGetValue(g, out cur);
            barsFor[g] = cur + sections[i].bars;
        }
        int total = TotalBars();
        var outList = new List<MixEntry>();
        foreach (var kv in barsFor)
        {
            outList.Add(new MixEntry
            {
                name = TraxClassifier.Genres[kv.Key].name,
                bars = kv.Value,
                share = total > 0 ? (double)kv.Value / total : 0,
                genreIndex = kv.Key
            });
        }
        outList.Sort(delegate (MixEntry x, MixEntry y)
        {
            if (x.share != y.share) return y.share.CompareTo(x.share);
            return string.CompareOrdinal(x.name, y.name);
        });
        return outList;
    }

    // ── economy ──────────────────────────────────────────────────────────
    // ⚠️ TUNING PLACEHOLDERS — Sam sets the real numbers. The SHAPE is the
    // design decision: demos unchanged; a full track worth a multiple growing
    // with section count and length; each alien's offer diluted to their
    // genre's share of the bars — an all-genre "super track" sells to
    // everyone but pays each fan only their slice. Do not add a floor that
    // erases that trade-off.

    /// Full-track value as a multiple of the demo price for the same loop.
    public double ValueMult()
    {
        return 1.5 + 0.5 * (sections.Count - 1) + 0.05 * (TotalBars() - 4);
    }

    /// What a fan of the given genre offers, as a multiple of the demo price.
    /// Zero if their genre isn't in the song at all.
    public double OfferMult(int genreIndex)
    {
        List<MixEntry> mix = GenreMix();
        for (int i = 0; i < mix.Count; i++)
            if (mix[i].genreIndex == genreIndex) return ValueMult() * mix[i].share;
        return 0;
    }
}
