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
        public TraxTrack track;      // the FIRST section — legacy readers' view
        public uint trackId;
        public TraxSong song;        // the whole arrangement; never null
        public uint songId;
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
    /// Is the TRAX APP ITSELF on the computer? (First-meeting revamp,
    /// 2026-08-30: the app no longer ships with the shuttle — Tev sells a USB
    /// stick for $20, and opening the computer with it consumes the stick and
    /// installs the app.) World state exactly like the plugin set: one install
    /// serves every player, it rides TraxLibrarySave, and the Version bump
    /// makes TraxSync replicate it for free.
    ///
    /// The ~6 s DOWNLOADING theatre is deliberately NOT here — it's cosmetic,
    /// per-machine and needs Unity's clock, so it lives with the computer UI.
    /// This bit flips at stick consumption, which is also what makes "save
    /// mid-download" snap to installed on load with zero extra state.
    /// </summary>
    static bool _appInstalled;

    public static bool IsAppInstalled { get { return _appInstalled; } }

    public static bool InstallApp()
    {
        if (_appInstalled) return false;
        _appInstalled = true;
        Version++;
        return true;
    }

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
        return Save(name, track, nowUnix, null);
    }

    /// `song` may be null — legacy callers (and the tests) save a bare track,
    /// which becomes a one-section song. The record's `track` is always the
    /// FIRST section so old readers (shelf badge, demo printing) agree with
    /// what the arranger shows.
    public static Record Save(string name, TraxTrack track, long nowUnix, TraxSong song)
    {
        string clean = NormalizeName(name);
        if (clean.Length == 0 || (track == null && song == null)) return null;

        Record existing = FindByName(clean);
        Record rec = existing ?? new Record { id = MakeId(nowUnix, _seq++) };
        rec.name = clean;
        rec.song = song != null ? song.Clone() : TraxSong.FromTrack(track);
        rec.track = rec.song.sections[0].track.Clone();
        rec.trackId = rec.track.TrackId();
        rec.songId = rec.song.SongId();
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
        _appInstalled = false;   // a new world starts with no TRAX — Tev sells it
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
            TraxSong song = r.song ?? TraxSong.FromTrack(r.track);
            for (int s = 0; s < song.sections.Count; s++)
            {
                TraxSection sec = song.sections[s];
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
            save.projects.Add(row);
        }
        foreach (string m in _installed) save.installedPlugins.Add(m);
        save.traxAppInstalled = _appInstalled;
        // Era marker: every save written by this code says so, which is how
        // Apply tells "bought and not installed" (false, era set) apart from
        // "save predates the USB stick" (false, era unset → grandfathered in,
        // because TRAX used to ship with the shuttle).
        save.traxAppEra = true;
        TraxPrints.Capture(save);
        // The cassette machine's own two fields ride this same blob, but they
        // are filled in by SaveCollector rather than here: CassetteDeck talks to
        // the Hotbar, and this file is compiled STANDALONE WITH NO UNITY
        // REFERENCES by the library test suite. One import would break that.
        return save;
    }

    /// One track's worth of saved fields back into a valid track. Shared by
    /// the project row and its sections so both coerce by the same rules.
    static TraxTrack CoerceTrack(List<float> dials, int key, List<int> preset,
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

        // See Capture: pre-USB-stick saves are grandfathered installed. (Clear
        // above already reset the bit, so Version needn't bump again here.)
        _appInstalled = save.traxAppInstalled || !save.traxAppEra;

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

            TraxTrack t = CoerceTrack(row.dials, row.key, row.preset, row.variation, row.active);

            // The song: one coerced section per saved row, capped; a pre-song
            // record (empty list) becomes a one-section song of its track —
            // exactly how it always played.
            TraxSong song = null;
            if (row.sections != null && row.sections.Count > 0)
            {
                song = new TraxSong();
                for (int s = 0; s < row.sections.Count && s < TraxSong.MaxSections; s++)
                {
                    TraxSectionSave srow = row.sections[s];
                    if (srow == null) continue;
                    TraxTrack st = CoerceTrack(srow.dials, srow.key, srow.preset, srow.variation, srow.active);
                    song.sections.Add(new TraxSection(st, srow.bars));
                }
                if (song.sections.Count == 0) song = null;
            }
            if (song == null) song = TraxSong.FromTrack(t);
            else t = song.sections[0].track.Clone();    // the legacy view must agree with the song

            string id = string.IsNullOrEmpty(row.id) ? MakeId(row.savedAt, _seq++) : row.id;
            if (FindById(id) != null) id = MakeId(row.savedAt, _seq++);   // duplicate ids in a hand-edited file

            _projects.Add(new Record
            {
                id = id,
                name = clean,
                track = t,
                trackId = t.TrackId(),
                song = song,
                songId = song.SongId(),
                savedAt = row.savedAt
            });
            if (_seq <= _projects.Count) _seq = _projects.Count + 1;
        }
        Version++;
    }
}
