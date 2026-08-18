using System.Collections.Generic;

/// <summary>
/// Every song that has ever been PRINTED to tape, frozen.
///
/// ── Why this is not just a pointer at the project shelf ──────────────────
/// A cassette in your pocket must never change song. If a tape only referenced
/// a <see cref="TraxLibrary"/> record, then editing that project would silently
/// rewrite tapes already sold, and deleting it would leave them pointing at
/// nothing. So printing COPIES the song in here and the tape carries this id.
/// Records are append-only and never edited — that is the whole point.
///
/// ── Every pressing is a SONG (2026-08-18 tape formats) ───────────────────
/// A record carries a frozen <see cref="TraxSong"/> plus a FORMAT kind
/// (TraxKind: Demo presses one section, Half/Full the whole arrangement) —
/// one evaluation path for everything, no parallel demo/song cases.
/// <c>track</c> remains as an alias of the FIRST section's track so lineage
/// (named requests) and legacy readers keep working.
///
/// The id is DERIVED, not allocated: kind prefix + tier + the song identity.
/// Printing the same song at the same kind/tier produces the same id and the
/// tapes stack. Rows from saves that predate formats keep deriving their old
/// "t{tier}-{trackId}" ids so cassettes already in hotbars still resolve.
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
        public int kind;             // TraxKind.Demo / Half / Full
        public TraxSong song;        // frozen copy — NEVER null; a demo is one section
        public uint songId;
        public TraxTrack track;      // ALIAS of song.sections[0].track (lineage + legacy readers)
        public uint trackId;

        /// What this pressing multiplies TapeValue.Base by (1.0 for demos).
        public double FormatMult { get { return TraxKind.FormatMult(kind, song); } }
    }

    static readonly Dictionary<string, Record> _byId = new Dictionary<string, Record>();

    public static int Count { get { return _byId.Count; } }

    /// New prints: kind-prefixed over the SONG identity. Same song, same kind,
    /// same tier → the same pressing, and the tapes stack.
    public static string MakeId(int kind, int tier, uint songId)
    {
        return TraxKind.IdPrefix(kind).ToString() + tier + "-" + songId.ToString("x8");
    }

    /// Pre-format saves used "t{tier}-{trackId:x8}". Rows without sections
    /// keep deriving it so cassettes already in hotbars/lockers still resolve.
    static string LegacyId(int tier, uint trackId)
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
    /// Freeze a song as a printable tape and return its record. Printing the
    /// same song at the same kind and tier again returns the EXISTING record
    /// (the data is identical by construction) but REFRESHES its display
    /// name — renaming a project and reprinting used to show the old title on
    /// every sell surface, and one tape must never have two names.
    /// </summary>
    public static Record Register(string name, TraxSong song, int kind, int tier)
    {
        if (song == null || song.sections.Count == 0) return null;
        if (tier < 1) tier = 1;
        if (tier > 2) tier = 2;
        kind = TraxKind.Clamp(kind);

        TraxSong frozen = song.Clone();
        uint sid = frozen.SongId();
        string id = MakeId(kind, tier, sid);

        Record existing;
        if (_byId.TryGetValue(id, out existing))
        {
            string fresh = TraxLibrary.NormalizeName(name);
            if (!string.IsNullOrEmpty(fresh)) existing.name = fresh;
            return existing;
        }

        var rec = new Record
        {
            id = id,
            name = TraxLibrary.NormalizeName(name),
            tier = tier,
            kind = kind,
            song = frozen,
            songId = sid,
            track = frozen.sections[0].track,
            trackId = frozen.sections[0].track.TrackId()
        };
        _byId[id] = rec;
        return rec;
    }

    /// Legacy demo shim (Tev's stock tapes, plugin demos): one 4-bar section.
    public static Record Register(string name, TraxTrack track, int tier)
    {
        if (track == null) return null;
        return Register(name, TraxSong.FromTrack(track), TraxKind.Demo, tier);
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

    public static int KindOf(string id)
    {
        Record r = Get(id);
        return r == null ? TraxKind.Demo : r.kind;
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
                kind = r.kind,
                key = r.track.key
            };
            for (int d = 0; d < TraxPrng.DialCount; d++) row.dials.Add((float)r.track.dials.Get(d));
            for (int m = 0; m < TraxPresets.ModuleCount; m++)
            {
                row.preset.Add(r.track.preset[m]);
                row.variation.Add(r.track.variation[m]);
                row.active.Add(r.track.active[m]);
            }
            for (int s = 0; s < r.song.sections.Count; s++)
            {
                TraxSection sec = r.song.sections[s];
                var srow = new TraxSectionSave { bars = sec.bars, key = sec.track.key };
                for (int d = 0; d < TraxPrng.DialCount; d++) srow.dials.Add((float)sec.track.dials.Get(d));
                for (int m = 0; m < TraxPresets.ModuleCount; m++)
                {
                    srow.preset.Add(sec.track.preset[m]);
                    srow.variation.Add(sec.track.variation[m]);
                    srow.active.Add(sec.track.active[m]);
                }
                row.sections.Add(srow);
            }
            save.prints.Add(row);
        }
    }

    /// One saved track's worth of fields back into a valid track — the same
    /// clamping rules whichever row shape it came from.
    static TraxTrack Coerce(List<float> dials, int key, List<int> preset,
                            List<int> variation, List<bool> active)
    {
        var t = TraxTrack.Default();
        if (dials != null)
            for (int d = 0; d < TraxPrng.DialCount && d < dials.Count; d++)
            {
                double v = dials[d];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                t.dials = t.dials.With(d, v < 0 ? 0 : v > 10 ? 10 : v);
            }
        t.key = ((key % 12) + 12) % 12;
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
        {
            if (preset != null && m < preset.Count)
                t.preset[m] = ((preset[m] % TraxPresets.PresetCount) + TraxPresets.PresetCount) % TraxPresets.PresetCount;
            if (variation != null && m < variation.Count)
                t.variation[m] = ((variation[m] % TraxPresets.VariationCount) + TraxPresets.VariationCount) % TraxPresets.VariationCount;
            t.active[m] = active == null || m >= active.Count || active[m];
        }
        return t;
    }

    /// <summary>
    /// Never throws, and never trusts the stored id. A row WITH sections is a
    /// format-era pressing: its id re-derives from the loaded song. A row
    /// WITHOUT sections predates formats: it loads as a Demo and keeps its
    /// legacy "t"-id so hotbar slots that reference it still resolve.
    /// </summary>
    public static void Apply(TraxLibrarySave save)
    {
        Clear();
        if (save == null || save.prints == null) return;

        for (int i = 0; i < save.prints.Count; i++)
        {
            TraxPrintSave row = save.prints[i];
            if (row == null) continue;

            int tier = row.tier < 1 ? 1 : row.tier > 2 ? 2 : row.tier;
            TraxTrack t = Coerce(row.dials, row.key, row.preset, row.variation, row.active);

            TraxSong song = null;
            if (row.sections != null && row.sections.Count > 0)
            {
                song = new TraxSong();
                for (int s = 0; s < row.sections.Count && s < TraxSong.MaxSections; s++)
                {
                    TraxSectionSave srow = row.sections[s];
                    if (srow == null) continue;
                    TraxTrack st = Coerce(srow.dials, srow.key, srow.preset, srow.variation, srow.active);
                    song.sections.Add(new TraxSection(st, srow.bars));
                }
                if (song.sections.Count == 0) song = null;
            }

            string id;
            int kind;
            if (song == null)
            {
                // Legacy row: a pre-format demo. Its old id is what hotbar
                // slots reference, so it is preserved, not re-derived.
                kind = TraxKind.Demo;
                song = TraxSong.FromTrack(t);
                id = LegacyId(tier, t.TrackId());
            }
            else
            {
                kind = TraxKind.Clamp(row.kind);
                id = MakeId(kind, tier, song.SongId());
            }

            _byId[id] = new Record
            {
                id = id,
                name = TraxLibrary.NormalizeName(row.name),
                tier = tier,
                kind = kind,
                song = song,
                songId = song.SongId(),
                track = song.sections[0].track,
                trackId = song.sections[0].track.TrackId()
            };
        }
    }
}
