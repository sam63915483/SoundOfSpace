using System.Collections.Generic;

/// <summary>
/// Every track that has ever been PRINTED to tape, frozen.
///
/// ── Why this is not just a pointer at the project shelf ──────────────────
/// A cassette in your pocket must never change song. If a tape only referenced
/// a <see cref="TraxLibrary"/> record, then editing that project would silently
/// rewrite tapes already sold, and deleting it would leave them pointing at
/// nothing. So printing COPIES the track in here and the tape carries this id.
/// Records are append-only and never edited — that is the whole point.
///
/// The id is DERIVED, not allocated: it is the track identity plus the tape
/// tier. So printing the same project twice produces the same id and the tapes
/// stack, while a T2 pressing of the same song is its own stack — which is
/// exactly right, because it is worth more.
///
/// World state, like the shelf: in co-op both players' tapes resolve through
/// the same table, so a tape handed across is still the same song.
/// </summary>
public static class TraxPrints
{
    public sealed class Record
    {
        public string id;
        public string name;          // the project name at the moment it was printed
        public int tier;             // 1 or 2
        public TraxTrack track;      // frozen copy
        public uint trackId;
    }

    static readonly Dictionary<string, Record> _byId = new Dictionary<string, Record>();

    public static int Count { get { return _byId.Count; } }

    /// Stable and derived — same song at the same tier is the same print.
    public static string MakeId(uint trackId, int tier)
    {
        return "t" + tier + "-" + trackId.ToString("x8");
    }

    public static Record Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Record r;
        return _byId.TryGetValue(id, out r) ? r : null;
    }

    /// <summary>
    /// Freeze a track as a printable tape and return its record. Printing the
    /// same song at the same tier again returns the EXISTING record rather than
    /// overwriting it — the first pressing's name is the one that sticks, so a
    /// tape you already sold cannot be renamed out from under the buyer.
    /// </summary>
    public static Record Register(string name, TraxTrack track, int tier)
    {
        if (track == null) return null;
        if (tier < 1) tier = 1;
        if (tier > 2) tier = 2;

        uint tid = track.TrackId();
        string id = MakeId(tid, tier);

        Record existing;
        if (_byId.TryGetValue(id, out existing)) return existing;

        var rec = new Record
        {
            id = id,
            name = TraxLibrary.NormalizeName(name),
            tier = tier,
            track = track.Clone(),
            trackId = tid
        };
        _byId[id] = rec;
        return rec;
    }

    /// What to show on a held tape or in a slot. Falls back to something
    /// readable rather than blank if a save ever references a lost record.
    public static string DisplayName(string id)
    {
        Record r = Get(id);
        if (r == null) return "CASSETTE";
        return string.IsNullOrEmpty(r.name) ? "UNTITLED" : r.name;
    }

    public static int TierOf(string id)
    {
        Record r = Get(id);
        return r == null ? 1 : r.tier;
    }

    /// New Game runs no Apply, so a static table would carry the last world's
    /// pressings into the next one. Called from NewGameReset.
    public static void Clear() { _byId.Clear(); }

    // ── save/load ────────────────────────────────────────────────────────

    public static void Capture(TraxLibrarySave save)
    {
        if (save == null) return;
        save.prints.Clear();
        foreach (var kv in _byId)
        {
            Record r = kv.Value;
            var row = new TraxPrintSave
            {
                id = r.id,
                name = r.name,
                tier = r.tier,
                key = r.track.key
            };
            for (int d = 0; d < TraxPrng.DialCount; d++) row.dials.Add((float)r.track.dials.Get(d));
            for (int m = 0; m < TraxPresets.ModuleCount; m++)
            {
                row.preset.Add(r.track.preset[m]);
                row.variation.Add(r.track.variation[m]);
                row.active.Add(r.track.active[m]);
            }
            save.prints.Add(row);
        }
    }

    /// <summary>
    /// Never throws, and never trusts the stored id: the id is RE-DERIVED from
    /// the loaded track. A hand-edited save that claims one song under another
    /// song's id would otherwise make a tape that sounds like neither.
    /// </summary>
    public static void Apply(TraxLibrarySave save)
    {
        Clear();
        if (save == null || save.prints == null) return;

        for (int i = 0; i < save.prints.Count; i++)
        {
            TraxPrintSave row = save.prints[i];
            if (row == null) continue;

            var t = TraxTrack.Default();
            if (row.dials != null)
                for (int d = 0; d < TraxPrng.DialCount && d < row.dials.Count; d++)
                {
                    double v = row.dials[d];
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    t.dials = t.dials.With(d, v < 0 ? 0 : v > 10 ? 10 : v);
                }
            t.key = ((row.key % 12) + 12) % 12;
            for (int m = 0; m < TraxPresets.ModuleCount; m++)
            {
                if (row.preset != null && m < row.preset.Count)
                    t.preset[m] = ((row.preset[m] % TraxPresets.PresetCount) + TraxPresets.PresetCount) % TraxPresets.PresetCount;
                if (row.variation != null && m < row.variation.Count)
                    t.variation[m] = ((row.variation[m] % TraxPresets.VariationCount) + TraxPresets.VariationCount) % TraxPresets.VariationCount;
                t.active[m] = row.active == null || m >= row.active.Count || row.active[m];
            }

            int tier = row.tier < 1 ? 1 : row.tier > 2 ? 2 : row.tier;
            uint tid = t.TrackId();
            string id = MakeId(tid, tier);

            _byId[id] = new Record
            {
                id = id,
                name = TraxLibrary.NormalizeName(row.name),
                tier = tier,
                track = t,
                trackId = tid
            };
        }
    }
}
