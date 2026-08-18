using System.Collections.Generic;

/// <summary>
/// What each alien remembers about you: the songs they have already been
/// played, and how warm they are toward you.
///
/// ── Why this exists when taste is derived ────────────────────────────────
/// Taste is a property of the alien and costs no storage. HISTORY is a
/// property of the relationship, so it has to be written down. This is the only
/// per-alien state the tape economy saves.
///
/// ── Songs are matched by CLOSENESS, not by hash ──────────────────────────
/// The obvious rule — "refuse the exact same track id" — is trivially beaten by
/// nudging one variation, which changes the id and nothing you can hear. So a
/// song counts as already-heard if it lands within <see cref="SameSongDistance"/>
/// of one they have had. That closes the exploit without punishing a genuine
/// rewrite, because a genuine rewrite moves the dials.
///
/// WORLD state, not per-player: an alien who has heard a song has heard it,
/// whichever co-op partner played it to them.
/// </summary>
public static class TapeMemory
{
    /// How close two songs must be, in six-dial space, to count as the same
    /// one. The whole space is ~24 across, so this is deliberately tight — it
    /// catches a re-roll, not a remix.
    public const double SameSongDistance = 1.5;

    public const int MaxSongsRemembered = 40;   // per alien; oldest drops off

    sealed class Entry
    {
        // bond/contact are read from OLD saves and written back out so a file
        // that predates the consolidation still round-trips; nothing reads them
        // for gameplay any more.
        public int bond;
        public bool contact;
        public readonly List<double[]> heard = new List<double[]>();
        // Which TRACKS (TraxTrack.TrackId, tier-independent lineage) this
        // alien has BOUGHT — word-of-mouth source data for named requests
        // (loop-feel D): "heard GORP SLIME at Krib's" needs to know Krib owns
        // it. A subset of heard, but by identity rather than by closeness.
        public readonly List<uint> bought = new List<uint>();
        // Half/Full SONGS heard/bought, by SongId (2026-08-18 tape formats).
        // Songs are a different product from their sections' demos, so they
        // get identity matching, not closeness — see the note below.
        public readonly List<uint> heardSongs = new List<uint>();
        public readonly List<uint> boughtSongs = new List<uint>();
    }

    static readonly Dictionary<string, Entry> _byAlien = new Dictionary<string, Entry>();

    /// Bumped on every change, so UI and multiplayer can watch one counter
    /// rather than diffing — same shape as TraxLibrary.Version.
    public static int Version { get; private set; }

    static Entry Get(string id, bool create)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Entry e;
        if (_byAlien.TryGetValue(id, out e)) return e;
        if (!create) return null;
        e = new Entry();
        _byAlien[id] = e;
        return e;
    }

    // ── bond ─────────────────────────────────────────────────────────────

    // ── bond and contact are NOT here, on purpose ────────────────────────
    //
    // They live on BuyerLedger, which also owns the message threads, the unread
    // counts and the regular-conversion roll. There were briefly TWO bond
    // numbers for the same alien and that is a bug waiting to happen: the phone
    // would show one figure while the price used the other.
    //
    // This class keeps ONLY the song history, which is the part BuyerLedger has
    // no concept of — and keeping it Unity-free is what lets the whole taste
    // model be executed headlessly. Forwarding to BuyerLedger from here would
    // have cost exactly that, which is how the mistake was caught.

    // ── song history ─────────────────────────────────────────────────────

    /// Has this alien already been played something close enough to this?
    public static bool HasHeard(string id, double[] dials)
    {
        Entry e = Get(id, false);
        if (e == null || dials == null) return false;
        for (int i = 0; i < e.heard.Count; i++)
            if (AlienTaste.Distance(dials, e.heard[i]) <= SameSongDistance) return true;
        return false;
    }

    public static void Remember(string id, double[] dials)
    {
        Entry e = Get(id, true);
        if (e == null || dials == null) return;
        if (HasHeard(id, dials)) return;

        var copy = new double[AlienTaste.DialCount];
        for (int i = 0; i < copy.Length && i < dials.Length; i++) copy[i] = dials[i];
        e.heard.Add(copy);
        if (e.heard.Count > MaxSongsRemembered) e.heard.RemoveAt(0);
        Version++;
    }

    public static int HeardCount(string id)
    {
        Entry e = Get(id, false);
        return e == null ? 0 : e.heard.Count;
    }

    // ── song history by IDENTITY (2026-08-18 tape formats) ───────────────
    //
    // Half/Full pressings are remembered by SongId, not by dial closeness:
    // a full song is a DIFFERENT PRODUCT from its section's demo — an alien
    // who bought the demo can still buy the song (that is the growth moment)
    // — but the same song can never be sold to them twice. Known soft spot,
    // accepted v1: nudging one variation changes SongId, so songs lack the
    // demo path's closeness guard; revisit if playtests show farming.

    public static bool HasHeardSong(string id, uint songId)
    {
        Entry e = Get(id, false);
        return e != null && songId != 0 && e.heardSongs.Contains(songId);
    }

    public static void RememberSong(string id, uint songId)
    {
        Entry e = Get(id, true);
        if (e == null || songId == 0 || e.heardSongs.Contains(songId)) return;
        e.heardSongs.Add(songId);
        if (e.heardSongs.Count > MaxSongsRemembered) e.heardSongs.RemoveAt(0);
        Version++;
    }

    public static void RememberBoughtSong(string id, uint songId)
    {
        Entry e = Get(id, true);
        if (e == null || songId == 0 || e.boughtSongs.Contains(songId)) return;
        e.boughtSongs.Add(songId);
        if (e.boughtSongs.Count > MaxSongsRemembered) e.boughtSongs.RemoveAt(0);
        Version++;
    }

    public static bool HasBoughtSong(string id, uint songId)
    {
        Entry e = Get(id, false);
        return e != null && songId != 0 && e.boughtSongs.Contains(songId);
    }

    // ── purchases, by track lineage (loop-feel D) ────────────────────────

    /// This alien bought a pressing of this track (any tier).
    public static void RememberBought(string id, uint trackId)
    {
        Entry e = Get(id, true);
        if (e == null || trackId == 0) return;
        if (e.bought.Contains(trackId)) return;
        e.bought.Add(trackId);
        if (e.bought.Count > MaxSongsRemembered) e.bought.RemoveAt(0);
        Version++;
    }

    public static bool HasBought(string id, uint trackId)
    {
        Entry e = Get(id, false);
        return e != null && trackId != 0 && e.bought.Contains(trackId);
    }

    /// Does any alien OTHER than <paramref name="exceptId"/> own a pressing of
    /// this track? Returns the first such owner (the gossiper in the want
    /// text). Deterministic enough — dictionary order is stable within a run.
    public static bool AnyoneElseBought(uint trackId, string exceptId, out string ownerId)
    {
        ownerId = null;
        if (trackId == 0) return false;
        foreach (var kv in _byAlien)
        {
            if (kv.Key == exceptId) continue;
            if (kv.Value.bought.Contains(trackId)) { ownerId = kv.Key; return true; }
        }
        return false;
    }

    /// New Game runs no Apply, so without this the last world's customers
    /// remember songs from a world that no longer exists.
    public static void Clear()
    {
        _byAlien.Clear();
        Version++;
    }

    // ── save/load ────────────────────────────────────────────────────────

    public static TapeMemorySave Capture()
    {
        var save = new TapeMemorySave();
        foreach (var kv in _byAlien)
        {
            Entry e = kv.Value;
            // Skip aliens with nothing worth remembering, so a world the player
            // has walked across does not accumulate empty rows forever.
            if (e.bond == 0 && !e.contact && e.heard.Count == 0 && e.bought.Count == 0) continue;

            save.ids.Add(kv.Key);
            save.bond.Add(e.bond);
            save.contact.Add(e.contact);
            save.heardCounts.Add(e.heard.Count);
            for (int i = 0; i < e.heard.Count; i++)
                for (int d = 0; d < AlienTaste.DialCount; d++)
                    save.heardDials.Add((float)e.heard[i][d]);
            save.boughtCounts.Add(e.bought.Count);
            for (int i = 0; i < e.bought.Count; i++)
                save.boughtTracks.Add(e.bought[i]);
        }
        return save;
    }

    /// <summary>
    /// Never throws. The flattened dial list is read by COUNT rather than
    /// trusted to line up — a truncated or hand-edited file loses history
    /// rather than throwing, or worse, silently pairing one alien's bond with
    /// another's memory.
    /// </summary>
    public static void Apply(TapeMemorySave save)
    {
        Clear();
        if (save == null || save.ids == null) return;

        int cursor = 0;
        for (int i = 0; i < save.ids.Count; i++)
        {
            string id = save.ids[i];
            if (string.IsNullOrEmpty(id)) continue;

            var e = new Entry();
            if (save.bond != null && i < save.bond.Count) e.bond = Clamp(save.bond[i], 0, 100);
            if (save.contact != null && i < save.contact.Count) e.contact = save.contact[i];

            int count = (save.heardCounts != null && i < save.heardCounts.Count) ? save.heardCounts[i] : 0;
            for (int h = 0; h < count; h++)
            {
                if (save.heardDials == null || cursor + AlienTaste.DialCount > save.heardDials.Count)
                {
                    cursor = save.heardDials == null ? 0 : save.heardDials.Count;
                    break;                       // ran out — keep what loaded
                }
                var dials = new double[AlienTaste.DialCount];
                for (int d = 0; d < AlienTaste.DialCount; d++)
                {
                    float v = save.heardDials[cursor++];
                    if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                    dials[d] = v < 0 ? 0 : v > 10 ? 10 : v;
                }
                e.heard.Add(dials);
            }

            _byAlien[id] = e;
        }

        // Bought-track lineage (loop-feel D) — its own cursor, count-guarded
        // the same way; absent entirely on pre-feature saves.
        if (save.boughtCounts != null && save.boughtTracks != null)
        {
            int bCursor = 0;
            for (int i = 0; i < save.ids.Count && i < save.boughtCounts.Count; i++)
            {
                Entry e = Get(save.ids[i], false);
                int count = save.boughtCounts[i];
                for (int t = 0; t < count && bCursor < save.boughtTracks.Count; t++, bCursor++)
                {
                    if (e == null) continue;
                    uint tid = (uint)save.boughtTracks[bCursor];
                    if (tid != 0 && !e.bought.Contains(tid)) e.bought.Add(tid);
                }
            }
        }
        Version++;
    }

    static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
}
