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
            if (e.bond == 0 && !e.contact && e.heard.Count == 0) continue;

            save.ids.Add(kv.Key);
            save.bond.Add(e.bond);
            save.contact.Add(e.contact);
            save.heardCounts.Add(e.heard.Count);
            for (int i = 0; i < e.heard.Count; i++)
                for (int d = 0; d < AlienTaste.DialCount; d++)
                    save.heardDials.Add((float)e.heard[i][d]);
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
        Version++;
    }

    static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
}
