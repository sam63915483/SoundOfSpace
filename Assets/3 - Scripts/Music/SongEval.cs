/// <summary>
/// How an alien hears a SONG — the Option 1 model (spec 2026-08-18).
///
/// SATISFACTION is the bar-weighted mean of per-section satisfaction: their
/// slice pulls it up, the filler pulls it down, and the one number then flows
/// through every existing formula (SatisfactionMult, feedback bands, reveal
/// lines) untouched. VERDICT is the best any section earns through GateFor,
/// so a song containing the alien's favourite genre is never refused — the
/// hint contract holds per-section.
///
/// A one-section song reproduces the single-track numbers EXACTLY (asserted
/// in the taste suite), which is what let every sell-path call site migrate
/// here with zero behaviour change for demos.
///
/// PURE — no Unity types; runs in verify-taste with AlienTaste.
/// </summary>
public static class SongEval
{
    /// The six dials as the taste model speaks them. A deliberate local copy
    /// of TapeTrade.DialsOf — that file imports UnityEngine and depending on
    /// it would cost this class its headless run (same reasoning as
    /// AlienTaste's private hash).
    public static double[] DialsOf(TraxTrack track)
    {
        var d = new double[AlienTaste.DialCount];
        if (track == null) return d;
        for (int i = 0; i < d.Length && i < TraxPrng.DialCount; i++) d[i] = track.dials.Get(i);
        return d;
    }

    /// 0..100, weighted by each section's share of the bars.
    public static double Satisfaction(string alienId, TraxSong song)
    {
        if (song == null || song.sections.Count == 0) return 0.0;
        double total = 0.0, bars = 0.0;
        for (int i = 0; i < song.sections.Count; i++)
        {
            TraxSection sec = song.sections[i];
            total += AlienTaste.Satisfaction(alienId, DialsOf(sec.track)) * sec.bars;
            bars += sec.bars;
        }
        return bars > 0 ? total / bars : 0.0;
    }

    /// Best verdict any section earns — each judged by the full tier-aware
    /// GateFor, so the hint contract and the shell-preference downgrade both
    /// apply per-section. Never call raw Gate from a sale path.
    public static AlienTaste.Verdict GateFor(string alienId, TraxSong song, int tapeTier)
    {
        var best = AlienTaste.Verdict.Rejected;
        if (song == null) return best;
        for (int i = 0; i < song.sections.Count; i++)
        {
            double[] dials = DialsOf(song.sections[i].track);
            double sat = AlienTaste.Satisfaction(alienId, dials);
            AlienTaste.Verdict v = AlienTaste.GateFor(alienId, dials, sat, tapeTier);
            if (v > best) best = v;
            if (best == AlienTaste.Verdict.Liked) return best;
        }
        return best;
    }

    /// Does ANY section classify under their favourite genre (primary or
    /// blend secondary)? Drives the taste-match bond and regular conversion.
    public static bool MatchesFavourite(string alienId, TraxSong song)
    {
        if (song == null) return false;
        for (int i = 0; i < song.sections.Count; i++)
            if (AlienTaste.MatchesFavourite(alienId, DialsOf(song.sections[i].track))) return true;
        return false;
    }

    /// The alien's favourite slice — feeds "the CLANG parts are great" lines
    /// and picks whose dials a rejection complains about.
    public static int BestSection(string alienId, TraxSong song, out double bestSat)
    {
        bestSat = 0.0;
        if (song == null || song.sections.Count == 0) return 0;
        int best = 0;
        for (int i = 0; i < song.sections.Count; i++)
        {
            double s = AlienTaste.Satisfaction(alienId, DialsOf(song.sections[i].track));
            if (i == 0 || s > bestSat) { bestSat = s; best = i; }
        }
        return best;
    }
}
