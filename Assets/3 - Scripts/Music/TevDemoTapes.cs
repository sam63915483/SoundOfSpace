using System.Collections.Generic;

/// <summary>
/// Tev's back catalogue — the tapes he fronts you.
///
/// These are REAL TRAX TRACKS, not props. They go through TraxPrints like
/// anything you press yourself, so listening works, the classifier names them,
/// and (in Phase 4) aliens grade them against their taste exactly as they grade
/// your own. A fake "TevTape" item would have needed a parallel path through
/// every one of those systems.
///
/// ── Why three, and why these three ───────────────────────────────────────
/// They are deliberately far apart in dial space: something heavy, something
/// bright, something empty. The first sales are the player's first lesson in
/// "different aliens want different things", and one tape repeated teaches that
/// lesson once. Three spread across the space teaches it three times.
///
/// It also matters mechanically: the per-alien repeat rule blocks selling the
/// same song twice to the same customer, so a batch of ten identical tapes
/// would strand the player. Copies spread across the catalogue do not.
///
/// Names are his: a man who has been on that lawn a long time.
/// </summary>
public static class TevDemoTapes
{
    public sealed class Demo
    {
        public string name;
        public double[] dials;      // pulse, crunch, goo, void, jitter, warp
        public int key;
        public int[] preset;        // per module, TraxPresets order
        public int[] variation;
    }

    /// Ordered heavy → bright → empty. Kept deliberately near three different
    /// genre centres so the readout names them as three different things.
    public static readonly Demo[] All =
    {
        new Demo {
            name = "SOUTHERN EXPOSURE",
            dials = new[] { 2.0, 9.0, 6.0, 5.0, 2.0, 8.0 },   // near SLUDJ
            key = 0,
            preset    = new[] { 2, 0, 1, 4, 0, 2 },
            variation = new[] { 1, 3, 0, 2, 0, 1 },
        },
        new Demo {
            name = "LAWN ORNAMENT",
            dials = new[] { 7.0, 2.0, 2.0, 2.0, 4.0, 1.0 },   // near CHIRP
            key = 5,
            preset    = new[] { 0, 1, 3, 1, 2, 0 },
            variation = new[] { 0, 2, 1, 5, 3, 0 },
        },
        new Demo {
            name = "NOTHING MUCH HAPPENS",
            dials = new[] { 1.0, 1.0, 3.0, 9.0, 1.0, 4.0 },   // near DRIFT
            key = 9,
            preset    = new[] { 3, 4, 0, 4, 1, 3 },
            variation = new[] { 2, 0, 4, 1, 6, 2 },
        },
    };

    public static TraxTrack TrackFor(Demo d)
    {
        TraxTrack t = TraxTrack.Default();
        for (int i = 0; i < d.dials.Length && i < TraxPrng.DialCount; i++)
            t = t.WithDial(i, d.dials[i]);
        t = t.WithKey(d.key);
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
        {
            if (d.preset != null && m < d.preset.Length)
                t = t.WithPreset(TraxPresets.ModuleNames[m], d.preset[m]);
            if (d.variation != null && m < d.variation.Length)
                t = t.WithVariation(TraxPresets.ModuleNames[m], d.variation[m]);
        }
        // His stuff plays the whole band. He owns all six — he has been at this
        // a while, and the player's two-module rack is the contrast.
        return t;
    }

    /// Freeze all three as Type 1 pressings and return their print ids. Safe to
    /// call repeatedly: TraxPrints.Register is keyed on the track, so this
    /// returns the same three ids every time rather than growing the table.
    public static List<string> EnsurePressed()
    {
        var ids = new List<string>(All.Length);
        for (int i = 0; i < All.Length; i++)
        {
            TraxPrints.Record rec = TraxPrints.Register(All[i].name, TrackFor(All[i]), 1);
            if (rec != null) ids.Add(rec.id);
        }
        return ids;
    }

    /// <summary>
    /// Hand over <paramref name="count"/> tapes SPREAD ACROSS THE CATALOGUE,
    /// dealt round-robin so the mix is even. Returns how many actually fit.
    ///
    /// Copies of one demo share a print id and therefore stack, so ten tapes
    /// occupy three slots rather than ten — which is the only reason a batch
    /// that size fits a seven-slot hotbar at all.
    /// </summary>
    public static int Grant(int count)
    {
        if (count <= 0 || Hotbar.Instance == null) return 0;
        List<string> ids = EnsurePressed();
        if (ids.Count == 0) return 0;

        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            string id = ids[i % ids.Count];
            placed += Hotbar.Instance.AddCassette(id, 1);
        }
        return placed;
    }

    /// True if this print is one of Tev's, so the sell flow knows whose cut it
    /// is. Cheap: three string compares.
    public static bool IsTevTape(string printId)
    {
        if (string.IsNullOrEmpty(printId)) return false;
        List<string> ids = EnsurePressed();
        for (int i = 0; i < ids.Count; i++) if (ids[i] == printId) return true;
        return false;
    }

    /// How many of Tev's tapes the player is carrying, across all three demos.
    public static int HeldCount()
    {
        if (Hotbar.Instance == null) return 0;
        List<string> ids = EnsurePressed();
        int n = 0;
        for (int i = 0; i < ids.Count; i++) n += Hotbar.Instance.GetCassetteTotal(ids[i]);
        return n;
    }
}
