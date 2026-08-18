using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A <see cref="TraxSong"/> on the wire.
///
/// The save schema is the network schema, so a song travels as exactly the
/// <see cref="TraxSectionSave"/> rows the world save already writes — no second
/// format that can drift, and a song that survives a save round-trip is a song
/// that survives the network by construction.
///
/// This lives in the Multiplayer folder rather than beside TraxSong because
/// JsonUtility is a Unity type and the engine files are compiled STANDALONE
/// WITH NO UNITY REFERENCES by the library test suite. One import over there
/// would break the headless build.
///
/// Coercion on read is deliberately the same clamping TraxLibrary.Apply does:
/// a malformed section becomes a sane default rather than a silently wrong
/// pattern, and nothing here ever throws.
/// </summary>
public static class TraxSongWire
{
    [System.Serializable]
    class SongWire
    {
        public List<TraxSectionSave> sections = new List<TraxSectionSave>();
    }

    public static string ToJson(TraxSong song)
    {
        var wire = new SongWire();
        if (song != null)
        {
            for (int s = 0; s < song.sections.Count; s++)
            {
                TraxSection sec = song.sections[s];
                if (sec == null || sec.track == null) continue;
                var row = new TraxSectionSave { bars = sec.bars, key = sec.track.key };
                for (int d = 0; d < TraxPrng.DialCount; d++) row.dials.Add((float)sec.track.dials.Get(d));
                for (int m = 0; m < TraxPresets.ModuleCount; m++)
                {
                    row.preset.Add(sec.track.preset[m]);
                    row.variation.Add(sec.track.variation[m]);
                    row.active.Add(sec.track.active[m]);
                }
                wire.sections.Add(row);
            }
        }
        return JsonUtility.ToJson(wire);
    }

    /// Null when the JSON is unusable — callers treat that as "ignore this
    /// message" rather than as an empty song, which would wipe the arranger.
    public static TraxSong FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        SongWire wire;
        try { wire = JsonUtility.FromJson<SongWire>(json); }
        catch (System.Exception e)
        {
            Debug.LogError("[TraxSongWire] Song didn't parse: " + e.Message);
            return null;
        }
        if (wire == null || wire.sections == null || wire.sections.Count == 0) return null;

        var song = new TraxSong();
        song.sections.Clear();
        for (int s = 0; s < wire.sections.Count && s < TraxSong.MaxSections; s++)
        {
            TraxSectionSave row = wire.sections[s];
            if (row == null) continue;
            song.sections.Add(new TraxSection(Coerce(row), row.bars));
        }
        return song.sections.Count > 0 ? song : null;
    }

    /// Same rules TraxLibrary.CoerceTrack uses: dials clamped to 0..10, key
    /// wrapped, preset/variation wrapped into range, and a MISSING active flag
    /// defaults to ON because that is how the track sounded when it was sent.
    static TraxTrack Coerce(TraxSectionSave row)
    {
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
        return t;
    }
}
