using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a TRACK somewhere in the world, away from the shuttle computer.
///
/// The terminal is not the only thing that makes TRAX noise any more. When you
/// offer a cassette to an alien they LISTEN to it in front of you (handoff §5),
/// and later a planet's radio plays one back. Both need the synth running at a
/// position, for a few seconds, with the terminal untouched — a player sitting
/// at the computer in co-op must not have their session hijacked because
/// somebody's customer pressed play.
///
/// ── Why one pooled instance rather than one per listen ───────────────────
/// A TraxAudioEngine allocates its own noise table and two delay lines — call
/// it a megabyte. That is fine to hold forever and wasteful to churn per
/// conversation, and only one alien is ever auditioning a tape at a time,
/// because it is a face-to-face interaction. So this is a lazily created
/// singleton that parents itself to whoever is currently playing.
///
/// Deliberately NOT a RuntimeInitializeOnLoadMethod auto-singleton: it is built
/// on first use, so it sidesteps the MainMenu seeding trap in CLAUDE.md
/// entirely and needs no EnsureGameplaySingletons entry.
/// </summary>
public class TraxTapePlayer : MonoBehaviour
{
    public static TraxTapePlayer Instance { get; private set; }

    TraxAudioEngine _engine;
    Coroutine _autoStop;

    public bool IsPlaying { get { return _engine != null && _engine.IsPlaying; } }

    /// The track currently on the deck, or null. Lets a caller ask "is this the
    /// tape I handed over?" without tracking it themselves.
    public TraxTrack Current { get; private set; }

    static TraxTapePlayer Ensure()
    {
        if (Instance != null) return Instance;

        var go = new GameObject("TraxTapePlayer");
        DontDestroyOnLoad(go);
        var player = go.AddComponent<TraxTapePlayer>();

        var audio = new GameObject("TapeAudio");
        audio.transform.SetParent(go.transform, false);
        audio.AddComponent<AudioSource>();
        player._engine = audio.AddComponent<TraxAudioEngine>();
        player._engine.SetSpatial(true);

        return Instance = player;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Play <paramref name="track"/> from <paramref name="at"/> for
    /// <paramref name="seconds"/>, then stop by itself. Parenting to the
    /// speaker means the sound follows them if they walk, and — because it
    /// rides someone already in the scene — it stays correct under the
    /// floating origin without registering anything.
    /// </summary>
    public static TraxTapePlayer PlayAt(Transform at, TraxTrack track, float seconds)
    {
        if (at == null || track == null) return null;
        var p = Ensure();
        p.transform.SetParent(at, false);
        p.transform.localPosition = Vector3.zero;
        p._engine.SetSpatial(true);
        p.Play(track, seconds);
        return p;
    }

    /// <summary>Which pressing is on the deck right now, or null.</summary>
    public static string CurrentPrintId
    {
        get { return Instance != null && Instance.IsPlaying ? Instance._printId : null; }
    }

    public static bool IsPlayingPrint(string printId)
    {
        return !string.IsNullOrEmpty(printId) && CurrentPrintId == printId;
    }

    /// <summary>
    /// The walkman: hold a tape, hear it, hold again to stop. Returns true if
    /// it STARTED playing, false if it stopped.
    ///
    /// Deliberately 2D and not spatial — this one is in the player's own ears,
    /// where an alien auditioning a tape in front of you is a point in the
    /// world. Same engine, different presentation.
    ///
    /// Not replicated: a co-op partner does not hear your walkman yet. Worth
    /// doing later, but it needs the print id on the wire and a remote engine,
    /// which is Phase 4's problem.
    /// </summary>
    public static bool TogglePersonal(Transform follow, string printId)
    {
        TraxPrints.Record rec = TraxPrints.Get(printId);
        if (rec == null) return false;

        if (IsPlayingPrint(printId)) { Instance.Stop(); return false; }

        var p = Ensure();
        if (follow != null)
        {
            p.transform.SetParent(follow, false);
            p.transform.localPosition = Vector3.zero;
        }
        p._engine.SetSpatial(false);
        p._printId = printId;
        p.PlayRecord(rec, 0f);          // 0 = loop until told otherwise
        return true;
    }

    /// <summary>
    /// Play a raw TRACK in the player's own ears (2D, like the walkman) —
    /// Tev's shop plugin demos (loop-feel A4). seconds &lt;= 0 loops until
    /// StopAll; the shop stops it on tab switch, purchase and close.
    /// </summary>
    public static TraxTapePlayer PlayPersonalTrack(TraxTrack track, float seconds)
    {
        if (track == null) return null;
        var p = Ensure();
        p._engine.SetSpatial(false);
        p._printId = null;
        p.Play(track, seconds);
        return p;
    }

    public static void StopAll() { if (Instance != null) Instance.Stop(); }

    string _printId;

    /// <summary>
    /// Play a PRESSED TAPE — the whole song, section hand-offs, fill-bar
    /// endings and all, looping. Every pressing routes through here (the
    /// walkman and the sell-table listen both come via TogglePersonal), so a
    /// full-length tape actually PLAYS full-length — the audio-form
    /// promise/grade match. A demo is a one-section song, which also gives
    /// demos their whole-section playback (bars included). Raw-track callers
    /// (plugin demos) keep Play(track, seconds).
    /// </summary>
    public void PlayRecord(TraxPrints.Record rec, float seconds)
    {
        if (rec == null || rec.song == null || _engine == null) return;
        StopAutoStop();

        TraxSong song = rec.song;
        int n = song.sections.Count;
        var ps = new TraxParams[n];
        var phrases = new TraxPhrase[n];
        var tracks = new TraxTrack[n];
        var bars = new int[n];
        for (int i = 0; i < n; i++)
        {
            // Same compile the arranger does (TraxInstrument.SetSong): the
            // engine owns per-section actives and CAVE, so a tape sounds
            // identical here and on the computer.
            TraxSection sec = song.sections[i];
            ps[i] = TraxParams.Compute(sec.track.dials, sec.track.key);
            phrases[i] = TraxPhrase.Generate(sec.track, ps[i]);
            tracks[i] = sec.track;
            bars[i] = sec.bars;
        }
        Current = song.sections[0].track.Clone();
        _engine.PublishSong(ps, phrases, tracks, bars);
        _engine.SeekSong(0);
        _engine.StartSongTransport();
        if (seconds > 0f) _autoStop = StartCoroutine(StopAfter(seconds));
    }

    public void Play(TraxTrack track, float seconds)
    {
        if (track == null || _engine == null) return;

        StopAutoStop();

        // A tape plays exactly as it was written — the active set comes off the
        // TRACK, never off whatever plugins this computer happens to own. Two
        // machines must hear the same cassette.
        Current = track.Clone();
        TraxParams p = TraxParams.Compute(Current.dials, Current.key);
        TraxPhrase phrase = TraxPhrase.Generate(Current, p);

        _engine.Publish(p, phrase, false);
        _engine.SetCavePreset(TraxPresets.Cave[Current.PresetOf("CAVE")], Current.VariationOf("CAVE"));
        for (int m = 0; m < TraxInstrument.Modules.Length; m++)
            _engine.SetModuleEnabled(TraxInstrument.Modules[m].name, Current.active[m]);

        _engine.StartTransport();
        if (seconds > 0f) _autoStop = StartCoroutine(StopAfter(seconds));
    }

    public void Stop()
    {
        StopAutoStop();
        if (_engine != null) _engine.StopTransport();
        Current = null;
        _printId = null;
        // Let go of the speaker so a destroyed NPC cannot take the player with
        // it — this object outlives any single scene.
        transform.SetParent(null, true);
    }

    void StopAutoStop()
    {
        if (_autoStop == null) return;
        StopCoroutine(_autoStop);
        _autoStop = null;
    }

    IEnumerator StopAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _autoStop = null;
        Stop();
    }
}
