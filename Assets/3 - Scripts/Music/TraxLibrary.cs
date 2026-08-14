using System;
using System.Collections.Generic;

/// <summary>
/// The project shelf on the shuttle computer.
/// PORT OF <c>prototypes/shuttle-computer/engine/library.js</c>.
///
/// A record is a NAME plus a whole TRACK — not a pointer to one, a copy. Two
/// projects may hold identical tracks, and editing one must never reach into
/// the other, so every record owns its own cloned track.
///
/// ── World-scoped, deliberately (Sam, 2026-08-13) ─────────────────────────
/// The shelf belongs to the COMPUTER, not to a player. In co-op both players
/// see the same projects, either can save over one, and either can print from
/// one. That is why this is static world state that rides the world save rather
/// than anything per-character.
///
/// The rules below are pure, exactly as in the JS: no Unity types, no clock of
/// its own (callers pass the timestamp), so it stays trivially testable and the
/// two implementations can be read side by side.
/// </summary>
public static class TraxLibrary
{
    public const int NameMax = 24;

    public sealed class Record
    {
        public string id;
        public string name;
        public TraxTrack track;
        public uint trackId;
        public long savedAt;
    }

    static readonly List<Record> _projects = new List<Record>();
    static int _seq;

    /// <summary>
    /// WHICH PLUGINS THE COMPUTER OWNS. World state like the shelf itself, so
    /// one player buying SIREN unlocks it for both — Sam's call, and it makes
    /// the $200 modules a shared investment rather than a race.
    ///
    /// ⚠️ GATES EDITING ONLY, NEVER PLAYBACK. A track plays exactly as written,
    /// whoever is listening; if ownership could silence a voice, the same
    /// cassette would sound different on two machines.
    ///
    /// Starts as the two you land with. Tev sells the rest.
    /// </summary>
    static readonly HashSet<string> _installed =
        new HashSet<string>(new[] { "THUMPER", "GLOWORM" });

    public static bool IsInstalled(string module)
    {
        return !string.IsNullOrEmpty(module) && _installed.Contains(module);
    }

    /// Installing never touches a track, so it cannot change what an already
    /// printed cassette sounds like.
    public static bool Install(string module)
    {
        if (string.IsNullOrEmpty(module) || !_installed.Add(module)) return false;
        Version++;
        return true;
    }

    public static IEnumerable<string> InstalledPlugins { get { return _installed; } }

    /// <summary>
    /// How many voices the computer can actually put on a tape right now.
    ///
    /// This is the honest ceiling on what the player can DELIVER, which is why
    /// the text-order economy prices against it: an alien commissioning a track
    /// quotes for a tape you could plausibly make, not for the six-module one
    /// you cannot build until you have bought every plugin Tev sells. Buying a
    /// plugin therefore raises what orders are worth, which is the whole point
    /// of a $200 module.
    /// </summary>
    public static int InstalledCount
    {
        get { return _installed.Count > TraxPresets.ModuleCount ? TraxPresets.ModuleCount : _installed.Count; }
    }

    /// Bumped on every mutation. The UI watches it instead of rebuilding the
    /// shelf every frame, and multiplayer can watch it to replicate — the same
    /// version-counter shape the economy sync already uses.
    public static int Version { get; private set; }

    public static IList<Record> Projects { get { return _projects; } }
    public static int Count { get { return _projects.Count; } }

    // ── name rules ───────────────────────────────────────────────────────

    /// Trimmed, collapsed and capped, but NOT uppercased — the screen renders
    /// it uppercase, and throwing away what was typed would make a later rename
    /// feel lossy.
    public static string NormalizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new System.Text.StringBuilder(raw.Length);
        bool lastSpace = false;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\r' || c == '\n' || c == '\t') c = ' ';
            if (c == ' ')
            {
                if (lastSpace) continue;
                lastSpace = true;
            }
            else lastSpace = false;
            sb.Append(c);
        }
        string s = sb.ToString().Trim();
        if (s.Length > NameMax) s = s.Substring(0, NameMax);
        return s;
    }

    public static bool IsValidName(string raw) { return NormalizeName(raw).Length > 0; }

    /// Case- and space-insensitive, so "Deep Cave" and "deep  cave" are the same
    /// project and SAVE overwrites instead of quietly making a twin.
    public static string NameKey(string raw) { return NormalizeName(raw).ToLowerInvariant(); }

    // ── lookup ───────────────────────────────────────────────────────────

    public static Record FindByName(string name)
    {
        string k = NameKey(name);
        for (int i = 0; i < _projects.Count; i++)
            if (NameKey(_projects[i].name) == k) return _projects[i];
        return null;
    }

    public static Record FindById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < _projects.Count; i++)
            if (_projects[i].id == id) return _projects[i];
        return null;
    }

    /// Most recently saved first — the shelf is a work queue, not an archive.
    public static List<Record> SortedRecent()
    {
        var copy = new List<Record>(_projects);
        copy.Sort(delegate (Record a, Record b)
        {
            if (a.savedAt != b.savedAt) return b.savedAt.CompareTo(a.savedAt);
            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        });
        return copy;
    }

    // ── mutation ─────────────────────────────────────────────────────────

    /// Ids are derived, never random — same discipline as the rest of the
    /// engine, and it keeps this testable without a clock or an RNG.
    public static string MakeId(long now, int seq)
    {
        return "p" + Convert.ToString(now, 16) + "-" + Convert.ToString(seq, 16);
    }

    /// <summary>
    /// SAVE semantics: same name overwrites in place, keeping its id and its
    /// slot in the list; a new name appends. The id is stable across overwrites
    /// so a cassette printed from a project can keep pointing at it.
    /// </summary>
    public static Record Save(string name, TraxTrack track, long nowUnix)
    {
        string clean = NormalizeName(name);
        if (clean.Length == 0 || track == null) return null;

        Record existing = FindByName(clean);
        Record rec = existing ?? new Record { id = MakeId(nowUnix, _seq++) };
        rec.name = clean;
        rec.track = track.Clone();
        rec.trackId = rec.track.TrackId();
        rec.savedAt = nowUnix;
        if (existing == null) _projects.Add(rec);
        Version++;
        return rec;
    }

    public static bool Delete(string id)
    {
        Record r = FindById(id);
        if (r == null) return false;
        _projects.Remove(r);
        Version++;
        return true;
    }

    /// New Game runs no Apply, so a static shelf would leak across the main
    /// menu into the next world. Called from NewGameReset.
    public static void Clear()
    {
        _projects.Clear();
        _seq = 0;
        _installed.Clear();
        _installed.Add("THUMPER");
        _installed.Add("GLOWORM");
        Version++;
    }

    // ── save/load ────────────────────────────────────────────────────────

    public static TraxLibrarySave Capture()
    {
        var save = new TraxLibrarySave();
        for (int i = 0; i < _projects.Count; i++)
        {
            Record r = _projects[i];
            var row = new TraxProjectSave
            {
                id = r.id,
                name = r.name,
                savedAt = r.savedAt,
                key = r.track.key
            };
            for (int d = 0; d < TraxPrng.DialCount; d++) row.dials.Add((float)r.track.dials.Get(d));
            for (int m = 0; m < TraxPresets.ModuleCount; m++)
            {
                row.preset.Add(r.track.preset[m]);
                row.variation.Add(r.track.variation[m]);
                row.active.Add(r.track.active[m]);
            }
            save.projects.Add(row);
        }
        foreach (string m in _installed) save.installedPlugins.Add(m);
        TraxPrints.Capture(save);
        return save;
    }

    /// <summary>
    /// Never throws. A corrupt shelf loses records; it does not lose the game.
    /// A row saved before a field existed coerces to a sane default rather than
    /// generating a silently wrong pattern — and a MISSING active flag defaults
    /// to ON, because that is how the track sounded when it was saved.
    /// </summary>
    public static void Apply(TraxLibrarySave save)
    {
        Clear();
        TraxPrints.Apply(save);      // frozen pressings, restored before anything resolves a tape
        if (save == null) return;

        // An old save with no plugin list keeps the starting two rather than
        // ending up with an empty rack.
        if (save.installedPlugins != null && save.installedPlugins.Count > 0)
        {
            _installed.Clear();
            for (int i = 0; i < save.installedPlugins.Count; i++)
                if (!string.IsNullOrEmpty(save.installedPlugins[i]))
                    _installed.Add(save.installedPlugins[i]);
        }

        if (save.projects == null) return;

        for (int i = 0; i < save.projects.Count; i++)
        {
            TraxProjectSave row = save.projects[i];
            if (row == null) continue;
            string clean = NormalizeName(row.name);
            if (clean.Length == 0) continue;

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

            string id = string.IsNullOrEmpty(row.id) ? MakeId(row.savedAt, _seq++) : row.id;
            if (FindById(id) != null) id = MakeId(row.savedAt, _seq++);   // duplicate ids in a hand-edited file

            _projects.Add(new Record
            {
                id = id,
                name = clean,
                track = t,
                trackId = t.TrackId(),
                savedAt = row.savedAt
            });
            if (_seq <= _projects.Count) _seq = _projects.Count + 1;
        }
        Version++;
    }
}
