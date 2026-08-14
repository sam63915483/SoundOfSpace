using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contacts getting hungry for music, and asking you for something specific.
///
/// ── Why this is the piece that makes taste matter ────────────────────────
/// Up to now the player could only learn an alien's taste by being refused by
/// them, which means the knowledge arrives attached to a failure. A request
/// turns that around: a contact TELLS you what they want, you go and make it,
/// and the pay-off is a bonus on top of an already-good match. That is the
/// difference between a taste system you endure and one you play.
///
/// Requests are always for the contact's OWN favourite genre. Asking for
/// something they do not like would be a lie the model cannot back up.
///
/// WORLD state, saved. Unlike taste (derived and free), a request is a
/// promise — it has to survive a reload or the player loses work.
/// </summary>
public static class TapeRequests
{
    /// Real seconds between a contact becoming eligible and actually asking,
    /// randomised per contact so they do not all text at once.
    public const float MinGapSeconds = 90f;
    public const float MaxGapSeconds = 300f;

    /// A request expires if ignored, so the list cannot silt up with orders
    /// the player has forgotten and will never fill.
    public const float ExpirySeconds = 1800f;   // 30 real minutes

    public sealed class Request
    {
        public string alienId;
        public string genre;        // always their favourite
        public float askedAt;       // Time.unscaledTime when it arrived
        public bool seen;           // has the player opened the app since?
    }

    static readonly Dictionary<string, Request> _open = new Dictionary<string, Request>();
    static readonly Dictionary<string, float> _nextAsk = new Dictionary<string, float>();

    public static int Version { get; private set; }

    public static int OpenCount { get { return _open.Count; } }

    public static int UnseenCount
    {
        get
        {
            int n = 0;
            foreach (var kv in _open) if (!kv.Value.seen) n++;
            return n;
        }
    }

    public static Request For(string alienId)
    {
        if (string.IsNullOrEmpty(alienId)) return null;
        Request r;
        return _open.TryGetValue(alienId, out r) ? r : null;
    }

    public static IEnumerable<Request> All { get { return _open.Values; } }

    public static void MarkAllSeen()
    {
        bool changed = false;
        foreach (var kv in _open) if (!kv.Value.seen) { kv.Value.seen = true; changed = true; }
        if (changed) Version++;
    }

    /// <summary>
    /// Does this track satisfy <paramref name="alienId"/>'s open request?
    /// Matched on the CLASSIFIER's answer, so the label the computer showed the
    /// player is the label the request is judged against — anything else would
    /// be the game marking its own homework with a different pen.
    /// </summary>
    public static bool Satisfies(string alienId, TraxTrack track)
    {
        Request r = For(alienId);
        if (r == null || track == null) return false;
        return TraxClassifier.Classify(track.dials).primary.name == r.genre;
    }

    public static void Fulfil(string alienId)
    {
        if (string.IsNullOrEmpty(alienId)) return;
        if (_open.Remove(alienId))
        {
            // A filled order buys you a little breathing room before the next.
            _nextAsk[alienId] = Time.unscaledTime + Random.Range(MinGapSeconds, MaxGapSeconds) * 1.5f;
            Version++;
        }
    }

    /// <summary>
    /// Called every so often by the director. Ages out stale requests and lets
    /// one eligible contact ask for something.
    ///
    /// Only ONE open request per contact, and the gap scales DOWN with bond —
    /// a regular pesters you, a stranger you sold one tape to does not.
    /// </summary>
    public static void Tick()
    {
        float now = Time.unscaledTime;

        // Expire.
        List<string> stale = null;
        foreach (var kv in _open)
            if (now - kv.Value.askedAt > ExpirySeconds)
                (stale ?? (stale = new List<string>())).Add(kv.Key);
        if (stale != null)
        {
            for (int i = 0; i < stale.Count; i++) _open.Remove(stale[i]);
            Version++;
        }

        // Ask.
        foreach (string id in TapeMemory.Contacts)
        {
            if (_open.ContainsKey(id)) continue;

            float due;
            if (!_nextAsk.TryGetValue(id, out due))
            {
                // First window after becoming a contact.
                _nextAsk[id] = now + Random.Range(MinGapSeconds, MaxGapSeconds);
                continue;
            }
            if (now < due) continue;

            var req = new Request
            {
                alienId = id,
                genre = AlienTaste.FavouriteGenre(id),
                askedAt = now,
                seen = false,
            };
            _open[id] = req;

            // Bond shortens the next gap: a regular wants more, more often.
            float bondScale = 1f - 0.5f * (TapeMemory.Bond(id) / 100f);
            _nextAsk[id] = now + Random.Range(MinGapSeconds, MaxGapSeconds) * bondScale;
            Version++;
            return;                     // one new ask per tick, so they trickle
        }
    }

    public static void Clear()
    {
        _open.Clear();
        _nextAsk.Clear();
        Version++;
    }

    // ── save/load ────────────────────────────────────────────────────────

    public static TapeRequestSave Capture()
    {
        var save = new TapeRequestSave();
        float now = Time.unscaledTime;
        foreach (var kv in _open)
        {
            save.ids.Add(kv.Key);
            save.genres.Add(kv.Value.genre);
            // RELATIVE, like every other timed thing in this save file — an
            // absolute unscaledTime means nothing after a reload.
            save.secondsAgo.Add(now - kv.Value.askedAt);
            save.seen.Add(kv.Value.seen);
        }
        return save;
    }

    public static void Apply(TapeRequestSave save)
    {
        Clear();
        if (save == null || save.ids == null) return;
        float now = Time.unscaledTime;

        for (int i = 0; i < save.ids.Count; i++)
        {
            string id = save.ids[i];
            if (string.IsNullOrEmpty(id)) continue;
            float ago = (save.secondsAgo != null && i < save.secondsAgo.Count) ? save.secondsAgo[i] : 0f;
            if (ago < 0 || float.IsNaN(ago)) ago = 0f;
            if (ago > ExpirySeconds) continue;          // it expired while away

            _open[id] = new Request
            {
                alienId = id,
                genre = (save.genres != null && i < save.genres.Count) ? save.genres[i]
                                                                       : AlienTaste.FavouriteGenre(id),
                askedAt = now - ago,
                seen = save.seen != null && i < save.seen.Count && save.seen[i],
            };
        }
        Version++;
    }
}
