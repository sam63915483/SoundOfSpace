// Runs the SHELF's rules for real, with no Unity in the room.
//
//   python test/verify-library.py
//
// TraxLibrary is pure logic on purpose, so it can be executed rather than only
// compile-checked. What it owns is exactly the stuff that fails quietly: save
// overwrites the right record, a name with odd spacing does not create a twin,
// and a track survives the round trip through the save file EXACTLY — because
// a cassette printed from a project has to keep sounding the same after a
// reload, and "close enough" is not a thing the identity hash tolerates.
//
// The save DTOs are stubbed below rather than referenced, because SaveData.cs
// drags in UnityEngine. They must stay field-for-field identical to the real
// ones; if they drift, this file stops testing what ships.

using System;
using System.Collections.Generic;

public static class TraxLibraryTests
{
    static int _checks, _failures;

    static void Check(bool cond, string what)
    {
        _checks++;
        if (cond) return;
        _failures++;
        Console.WriteLine("  FAIL  " + what);
    }

    static void Eq(object got, object want, string what)
    {
        Check(Equals(got, want), what + ": got " + got + ", want " + want);
    }

    public static int Main()
    {
        NameRules();
        SaveAndOverwrite();
        Deleting();
        RoundTrip();
        SongRoundTrip();
        CorruptRows();
        Plugins();
        Pressings();
        PrintSongChecks();

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("library VERIFIED - " + _checks + " checks, all passed.");
            return 0;
        }
        Console.WriteLine("library FAILED - " + _failures + " of " + _checks + " checks.");
        return 1;
    }

    // ── the arrangement round trip ───────────────────────────────────────

    static void SongRoundTrip()
    {
        Console.WriteLine("song round trip");
        TraxLibrary.Clear();

        // A three-section song: default, faster, stretched.
        var song = TraxSong.FromTrack(TraxTrack.Default());
        int b = song.AddSection(0);
        song.sections[b].track = song.sections[b].track.WithDial(0, 9.0);   // PULSE
        song.AddSection(b);
        song.SetSectionBars(1, 8);
        uint songId = song.SongId();

        TraxLibrary.Record rec = TraxLibrary.Save("Swamp Anthem", null, 100, song);
        Check(rec != null, "a song with no bare track still saves");
        Eq(rec.song.sections.Count, 3, "record keeps its three sections");
        Eq(rec.songId, songId, "saving does not change the song identity");
        Eq(rec.trackId, rec.song.sections[0].track.TrackId(),
           "the legacy track view is the first section");

        // Editing the caller's song after saving must not reach the shelf.
        song.SetSectionBars(0, 2);
        Eq(rec.song.sections[0].bars, 4, "the record's song is a copy, not a reference");

        var blob = TraxLibrary.Capture();
        Eq(blob.projects[0].sections.Count, 3, "capture writes one row per section");
        TraxLibrary.Apply(blob);

        TraxLibrary.Record back = TraxLibrary.FindByName("Swamp Anthem");
        Check(back != null, "the song survives the save file");
        Eq(back.song.sections.Count, 3, "sections survive the round trip");
        Eq(back.song.sections[1].bars, 8, "a stretched section keeps its bars");
        Eq(back.songId, songId, "the song identity survives the round trip EXACTLY");

        // A pre-song row (no sections) loads as a one-section song of its track.
        var legacy = new TraxLibrarySave();
        var row = new TraxProjectSave { id = "pOld", name = "Old One", savedAt = 5, key = 2 };
        for (int d = 0; d < 6; d++) row.dials.Add(5f);
        legacy.projects.Add(row);
        TraxLibrary.Apply(legacy);
        TraxLibrary.Record old = TraxLibrary.FindByName("Old One");
        Check(old != null && old.song != null, "a legacy row still gets a song");
        Eq(old.song.sections.Count, 1, "a legacy row is a one-section song");
        Eq(old.song.sections[0].track.TrackId(), old.trackId,
           "the legacy song wraps exactly the saved track");
    }

    // ── name handling ────────────────────────────────────────────────────

    static void NameRules()
    {
        Console.WriteLine("name rules");
        Eq(TraxLibrary.NormalizeName("  Deep   Cave  "), "Deep Cave", "collapse and trim");
        Eq(TraxLibrary.NormalizeName("a\tb\nc"), "a b c", "control chars become spaces");
        Eq(TraxLibrary.NormalizeName(null), "", "null is empty");
        Eq(TraxLibrary.NormalizeName("   "), "", "whitespace is empty");
        Check(!TraxLibrary.IsValidName("  "), "a blank name is refused");
        Eq(TraxLibrary.NormalizeName(new string('x', 40)).Length, TraxLibrary.NameMax, "capped");
        Eq(TraxLibrary.NameKey("Deep  CAVE"), TraxLibrary.NameKey("deep cave"),
           "case and spacing do not make a different project");
    }

    // ── save semantics ───────────────────────────────────────────────────

    static void SaveAndOverwrite()
    {
        Console.WriteLine("save + overwrite");
        TraxLibrary.Clear();

        TraxLibrary.Record a = TraxLibrary.Save("Deep Cave", TraxTrack.Default(), 1000);
        Eq(TraxLibrary.Count, 1, "one project after the first save");
        Check(a != null && a.id.Length > 0, "the record got an id");

        // Same name, different spelling of the spacing — must overwrite, and
        // must keep the id so anything pointing at it still resolves.
        TraxTrack edited = TraxTrack.Default().WithKey(5).WithActive("MOSS", false);
        TraxLibrary.Record b = TraxLibrary.Save("deep   cave", edited, 2000);
        Eq(TraxLibrary.Count, 1, "overwriting did not make a twin");
        Eq(b.id, a.id, "the id survived the overwrite");
        Eq(b.savedAt, 2000L, "the timestamp moved");
        Eq(b.track.key, 5, "the new track replaced the old one");
        Eq(b.trackId, edited.TrackId(), "the cached identity was recomputed");

        TraxLibrary.Save("Other", TraxTrack.Default(), 3000);
        Eq(TraxLibrary.Count, 2, "a different name appends");

        // Most recent first.
        List<TraxLibrary.Record> sorted = TraxLibrary.SortedRecent();
        Eq(sorted[0].name, "Other", "newest sorts first");

        Check(TraxLibrary.Save("", TraxTrack.Default(), 4000) == null, "a blank name saves nothing");
        Check(TraxLibrary.Save("x", null, 4000) == null, "a null track saves nothing");
        Eq(TraxLibrary.Count, 2, "the refused saves changed nothing");

        // A record owns its track: editing the caller's copy afterwards must
        // not reach into the shelf.
        TraxTrack mine = TraxTrack.Default();
        TraxLibrary.Record rec = TraxLibrary.Save("Mine", mine, 5000);
        mine.active[0] = false;
        Check(rec.track.active[0], "the shelf aliased the caller's track");
    }

    static void Deleting()
    {
        Console.WriteLine("delete");
        TraxLibrary.Clear();
        TraxLibrary.Record a = TraxLibrary.Save("One", TraxTrack.Default(), 1000);
        TraxLibrary.Save("Two", TraxTrack.Default(), 2000);

        Check(!TraxLibrary.Delete("nope"), "deleting an unknown id reports false");
        Eq(TraxLibrary.Count, 2, "and removes nothing");

        Check(TraxLibrary.Delete(a.id), "deleting a real id reports true");
        Eq(TraxLibrary.Count, 1, "and removes exactly one");
        Check(TraxLibrary.FindByName("One") == null, "the deleted project is gone");
        Check(TraxLibrary.FindByName("Two") != null, "the other one is untouched");
    }

    // ── the round trip that matters ──────────────────────────────────────

    static void RoundTrip()
    {
        Console.WriteLine("save file round trip");
        TraxLibrary.Clear();

        // A deliberately awkward track: every dial off its default, a key, odd
        // presets and variations, and two modules muted.
        TraxTrack t = TraxTrack.Default();
        double[] dials = { 1.5, 9.0, 0.0, 6.5, 10.0, 3.5 };
        for (int i = 0; i < dials.Length; i++) t = t.WithDial(i, dials[i]);
        t = t.WithKey(7)
             .WithPreset("THUMPER", 3).WithPreset("MOSS", 2).WithPreset("CAVE", 4)
             .WithVariation("SIREN", 5).WithVariation("SPINDLE", 7)
             .WithActive("MOSS", false).WithActive("CAVE", false);

        uint want = t.TrackId();
        TraxLibrary.Save("Awkward", t, 123456);

        TraxLibrarySave blob = TraxLibrary.Capture();
        TraxLibrary.Clear();
        Eq(TraxLibrary.Count, 0, "cleared before the reload");
        TraxLibrary.Apply(blob);

        Eq(TraxLibrary.Count, 1, "the project came back");
        TraxLibrary.Record r = TraxLibrary.FindByName("Awkward");
        Check(r != null, "found by name after reload");
        Eq(r.trackId, want, "THE TRACK IDENTITY SURVIVED - a printed cassette still matches");
        Eq(r.savedAt, 123456L, "timestamp survived");
        Eq(r.track.key, 7, "key survived");
        Eq(r.track.PresetOf("MOSS"), 2, "preset survived");
        Eq(r.track.VariationOf("SPINDLE"), 7, "variation survived");
        Check(!r.track.ActiveOf("MOSS") && !r.track.ActiveOf("CAVE"), "the mutes survived");
        Check(r.track.ActiveOf("THUMPER") && r.track.ActiveOf("GLOWORM"), "the unmuted stayed on");
        for (int i = 0; i < dials.Length; i++)
            Eq(r.track.dials.Get(i), dials[i], "dial " + i + " survived");

        // Two projects with identical names cannot both survive a reload.
        TraxLibrary.Clear();
        TraxLibrary.Save("Same", TraxTrack.Default(), 10);
        TraxLibrary.Save("Same", TraxTrack.Default().WithKey(3), 20);
        Eq(TraxLibrary.Count, 1, "the second save overwrote rather than duplicated");
    }

    static void CorruptRows()
    {
        Console.WriteLine("hostile save file");
        var blob = new TraxLibrarySave();
        blob.projects.Add(null);                                   // a null row
        blob.projects.Add(new TraxProjectSave { name = "   " });   // a blank name
        blob.projects.Add(new TraxProjectSave                      // everything out of range
        {
            id = "dup", name = "Wild", key = -37,
            dials = new List<float> { -5f, 99f, float.NaN },
            preset = new List<int> { 99, -4 },
            variation = new List<int> { -1 }
        });
        blob.projects.Add(new TraxProjectSave { id = "dup", name = "Twin", savedAt = 5 });

        TraxLibrary.Clear();
        TraxLibrary.Apply(blob);                                   // must not throw

        Eq(TraxLibrary.Count, 2, "the null and blank rows were dropped, the rest kept");
        TraxLibrary.Record wild = TraxLibrary.FindByName("Wild");
        Check(wild != null, "the mangled row still loaded");
        Check(wild.track.key >= 0 && wild.track.key < 12, "key was wrapped into range");
        Eq(wild.track.dials.Get(0), 0.0, "a negative dial clamped to 0");
        Eq(wild.track.dials.Get(1), 10.0, "an over-range dial clamped to 10");
        Check(!double.IsNaN(wild.track.dials.Get(2)), "NaN did not get through");
        Check(wild.track.PresetOf("THUMPER") >= 0 &&
              wild.track.PresetOf("THUMPER") < TraxPresets.PresetCount, "preset wrapped into range");
        Check(wild.track.VariationOf("THUMPER") >= 0 &&
              wild.track.VariationOf("THUMPER") < TraxPresets.VariationCount, "variation wrapped");

        // A row with no active list predates the field; every module must come
        // back ON, because that is how it sounded when it was saved.
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
            Check(wild.track.active[m], "a missing active flag defaulted to playing");

        TraxLibrary.Record twin = TraxLibrary.FindByName("Twin");
        Check(twin != null && twin.id != wild.id, "the duplicate id was reassigned");

        TraxLibrary.Clear();
        TraxLibrary.Apply(null);                                   // must not throw
        Eq(TraxLibrary.Count, 0, "a null save leaves an empty shelf");
    }

    static void Plugins()
    {
        Console.WriteLine("installed plugins");
        TraxLibrary.Clear();
        Check(TraxLibrary.IsInstalled("THUMPER"), "you land with THUMPER");
        Check(TraxLibrary.IsInstalled("GLOWORM"), "you land with GLOWORM");
        Check(!TraxLibrary.IsInstalled("SIREN"), "SIREN is Tev's to sell");
        Check(!TraxLibrary.IsInstalled(null), "a null module is not installed");

        Check(TraxLibrary.Install("SIREN"), "buying SIREN reports a change");
        Check(!TraxLibrary.Install("SIREN"), "buying it twice reports no change");
        Check(TraxLibrary.IsInstalled("SIREN"), "and it stays bought");

        TraxLibrarySave blob = TraxLibrary.Capture();
        TraxLibrary.Clear();
        Check(!TraxLibrary.IsInstalled("SIREN"), "clearing put the rack back to the starting two");
        TraxLibrary.Apply(blob);
        Check(TraxLibrary.IsInstalled("SIREN"), "a bought plugin survived the save file");

        // A save from before plugins existed must not strand you with no rack.
        TraxLibrary.Clear();
        TraxLibrary.Apply(new TraxLibrarySave());
        Check(TraxLibrary.IsInstalled("THUMPER") && TraxLibrary.IsInstalled("GLOWORM"),
              "an old save keeps the starting two rather than an empty rack");

        // New Game must not leak the last world's purchases.
        TraxLibrary.Install("CAVE");
        TraxLibrary.Clear();
        Check(!TraxLibrary.IsInstalled("CAVE"), "New Game forgets what the last world bought");
    }

    // ── pressings ────────────────────────────────────────────────────────
    // The promise being tested: a cassette in your pocket NEVER changes song.

    static void Pressings()
    {
        Console.WriteLine("pressings");
        TraxLibrary.Clear();
        TraxPrints.Clear();

        TraxTrack t = TraxTrack.Default().WithKey(4).WithActive("SIREN", false);
        TraxPrints.Record a = TraxPrints.Register("Deep Cave", t, 1);
        Check(a != null, "a track can be pressed");
        Eq(a.tier, 1, "tier recorded");
        Eq(a.trackId, t.TrackId(), "the pressing carries the track identity");

        // Same song, same tier, pressed again -> the SAME record, so the tapes
        // stack rather than fragmenting into one id per print run.
        TraxPrints.Record again = TraxPrints.Register("Deep Cave", t, 1);
        Eq(again.id, a.id, "re-pressing the same song at the same tier reuses the id");
        Eq(TraxPrints.Count, 1, "and does not add a second record");

        // A different tier is a different product and must not stack with it.
        TraxPrints.Record t2 = TraxPrints.Register("Deep Cave", t, 2);
        Check(t2.id != a.id, "a T2 pressing is its own id");
        Eq(TraxPrints.Count, 2, "both pressings exist");
        Eq(TraxPrints.TierOf(t2.id), 2, "tier resolves from the id");

        // Renaming and reprinting REFRESHES the display name (2026-08-16):
        // the old "first pressing's name sticks" rule meant a renamed project
        // showed its old title on every sell surface while the console showed
        // the new one. One tape, one name, everywhere — latest name wins.
        TraxPrints.Register("A Different Name", t, 1);
        Eq(TraxPrints.DisplayName(a.id), "A Different Name", "a reprint refreshes the tape's name");
        // Rename it back so the later checks read naturally.
        TraxPrints.Register("Deep Cave", t, 1);
        Eq(TraxPrints.DisplayName(a.id), "Deep Cave", "and refreshes again on the next reprint");

        // Editing the caller's track afterwards must not reach into the record.
        TraxTrack mutable = TraxTrack.Default();
        TraxPrints.Record frozen = TraxPrints.Register("Frozen", mutable, 1);
        uint before = frozen.trackId;
        mutable.active[0] = false;
        mutable.key = 9;
        Eq(frozen.trackId, before, "THE PRESSING IS FROZEN - editing the source track did not reach it");
        Check(frozen.track.active[0], "and its active set is untouched");
        Eq(frozen.track.key, 0, "and its key is untouched");

        // Deleting the PROJECT must leave the pressing alone — this is the whole
        // reason prints are a separate table from the shelf.
        TraxLibrary.Record proj = TraxLibrary.Save("Deep Cave", t, 100);
        TraxLibrary.Delete(proj.id);
        Eq(TraxLibrary.Count, 0, "the project is gone");
        Check(TraxPrints.Get(a.id) != null, "the tape printed from it still exists");
        Eq(TraxPrints.DisplayName(a.id), "Deep Cave", "and still knows its name");

        // Round trip.
        var blob = new TraxLibrarySave();
        TraxPrints.Capture(blob);
        // THREE, not four: "A Different Name" was the same song at the same
        // tier as the first pressing, so it reused that record rather than
        // making one. Only Deep Cave T1, Deep Cave T2 and Frozen T1 exist.
        Eq(blob.prints.Count, 3, "every distinct pressing was captured");
        TraxPrints.Clear();
        Eq(TraxPrints.Count, 0, "cleared");
        TraxPrints.Apply(blob);
        Eq(TraxPrints.Count, 3, "every pressing came back");
        TraxPrints.Record reloaded = TraxPrints.Get(a.id);
        Check(reloaded != null, "the tape resolves by the id its stack is keyed on");
        Eq(reloaded.trackId, a.trackId, "THE SONG SURVIVED THE SAVE FILE BYTE-FOR-BYTE");
        Eq(reloaded.tier, 1, "tier survived");
        Check(!reloaded.track.ActiveOf("SIREN"), "the mute survived");
        Eq(reloaded.track.key, 4, "key survived");

        // A hand-edited file that files a song under the wrong id must not
        // produce a tape that sounds like neither.
        var lying = new TraxLibrarySave();
        lying.prints.Add(new TraxPrintSave { id = "t1-deadbeef", name = "Liar", tier = 1 });
        TraxPrints.Apply(lying);
        Check(TraxPrints.Get("t1-deadbeef") == null, "a wrong id was not trusted");
        Eq(TraxPrints.Count, 1, "the row still loaded, under its re-derived id");

        TraxPrints.Apply(null);
        Eq(TraxPrints.Count, 0, "a null save leaves no pressings");

        Eq(TraxPrints.DisplayName("nope"), "CASSETTE", "an unknown tape still reads as something");
    }

    // ── song pressings (2026-08-18 tape formats) ─────────────────────────

    static void PrintSongChecks()
    {
        Console.WriteLine("song pressings");
        TraxPrints.Clear();

        var a = TraxTrack.Default();
        var b = TraxTrack.Default();
        b.dials = b.dials.With(0, 9.0);
        var song = new TraxSong();
        song.sections.Add(new TraxSection(a, 8));
        song.sections.Add(new TraxSection(b, 4));

        var rec = TraxPrints.Register("EPIC", song, TraxKind.Half, 2);
        Eq(rec.kind, TraxKind.Half, "kind stored");
        Check(rec.id[0] == 'h' && rec.id[1] == '2', "kind-prefixed id: " + rec.id);
        Eq(rec.songId, song.SongId(), "songId derived");
        Eq(rec.song.sections.Count, 2, "sections frozen");
        Check(ReferenceEquals(rec.track, rec.song.sections[0].track), "track aliases section 0");
        Eq(rec.trackId, rec.track.TrackId(), "trackId is section-0 lineage");
        Check(System.Math.Abs(rec.FormatMult - song.ValueMult()) < 1e-9, "record carries its mult");
        Eq(TraxPrints.KindOf(rec.id), TraxKind.Half, "KindOf resolves");

        // Demo register (legacy shim) is a 1-section 4-bar song, d-prefixed.
        var demo = TraxPrints.Register("LOOP", a, 1);
        Check(demo.kind == TraxKind.Demo && demo.song.sections.Count == 1
              && demo.song.sections[0].bars == 4 && demo.id[0] == 'd', "legacy shim = demo: " + demo.id);
        Check(System.Math.Abs(demo.FormatMult - 1.0) < 1e-9, "demo mult is 1");

        // Round-trip: sections and kind survive; ids re-derive identically.
        var save = new TraxLibrarySave();
        TraxPrints.Capture(save);
        TraxPrints.Apply(save);
        var back = TraxPrints.Get(rec.id);
        Check(back != null && back.kind == TraxKind.Half && back.song.sections.Count == 2
              && back.song.sections[0].bars == 8, "song round-trips");
        Check(TraxPrints.Get(demo.id) != null, "demo round-trips under the same id");

        // A LEGACY row (no sections, no kind) keeps its old t-prefixed id.
        var legacyRow = new TraxPrintSave { id = "ignored", name = "OLD", tier = 1, key = a.key };
        for (int d = 0; d < TraxPrng.DialCount; d++) legacyRow.dials.Add((float)a.dials.Get(d));
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
        {
            legacyRow.preset.Add(a.preset[m]);
            legacyRow.variation.Add(a.variation[m]);
            legacyRow.active.Add(a.active[m]);
        }
        var legacySave = new TraxLibrarySave();
        legacySave.prints.Add(legacyRow);
        TraxPrints.Apply(legacySave);
        var old = TraxPrints.Get("t1-" + a.TrackId().ToString("x8"));
        Check(old != null && old.kind == TraxKind.Demo && old.song.sections.Count == 1,
              "legacy row loads as demo under its old id");
        TraxPrints.Clear();
    }
}

// ── stubs of the real save DTOs (SaveData.cs pulls in UnityEngine) ────────
// Keep field-for-field identical to SaveData.cs.

public class TraxProjectSave
{
    public string id;
    public string name;
    public long savedAt;
    public int key;
    public List<float> dials = new List<float>();
    public List<int> preset = new List<int>();
    public List<int> variation = new List<int>();
    public List<bool> active = new List<bool>();
    public List<TraxSectionSave> sections = new List<TraxSectionSave>();
}

public class TraxSectionSave
{
    public int bars;
    public int key;
    public List<float> dials = new List<float>();
    public List<int> preset = new List<int>();
    public List<int> variation = new List<int>();
    public List<bool> active = new List<bool>();
}

public class TraxPrintSave
{
    public string id;
    public string name;
    public int tier;
    public int key;
    public List<float> dials = new List<float>();
    public List<int> preset = new List<int>();
    public List<int> variation = new List<int>();
    public List<bool> active = new List<bool>();
    public int kind;
    public List<TraxSectionSave> sections = new List<TraxSectionSave>();
}

public class TraxLibrarySave
{
    public List<TraxProjectSave> projects = new List<TraxProjectSave>();
    public List<string> installedPlugins = new List<string>();
    public List<TraxPrintSave> prints = new List<TraxPrintSave>();
}
