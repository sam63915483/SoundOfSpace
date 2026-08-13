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
        p.Play(track, seconds);
        return p;
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
