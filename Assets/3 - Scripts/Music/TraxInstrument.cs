using System;
using UnityEngine;

/// <summary>
/// Owns the track, drives the engine, feeds the audio backend.
/// PORT OF <c>prototypes/shuttle-computer/audio/instrument.js</c>.
///
/// ── This class is the choke point, on purpose ────────────────────────────
/// EVERY change to the track goes through <see cref="SetTrack"/>. Nothing else
/// may write track state — not the UI widgets, not the terminal, not a future
/// preset loader. When full-length recording is built (spec §9.5) it becomes
/// "log what passes through here, stamped with
/// <see cref="TraxAudioEngine.CurrentStep"/>" and nothing else changes.
/// Stamp with the STEP INDEX, never seconds.
/// </summary>
public class TraxInstrument : MonoBehaviour
{
    public struct ModuleDef
    {
        public string name;
        public string desc;
        public ModuleDef(string n, string d) { name = n; desc = d; }
    }

    // Ordered the way you'd read a mix: rhythm, low end, harmony, melody,
    // motion, space. CAVE has no pattern — its preset picks a space.
    public static readonly ModuleDef[] Modules =
    {
        new ModuleDef("THUMPER", "drums"),
        new ModuleDef("GLOWORM", "bass"),
        new ModuleDef("MOSS",    "chords"),
        new ModuleDef("SIREN",   "lead"),
        new ModuleDef("SPINDLE", "arp"),
        new ModuleDef("CAVE",    "space")
    };

    public TraxTrack Track { get; private set; }
    public TraxParams Params { get; private set; }
    public TraxPhrase Phrase { get; private set; }

    TraxAudioEngine _engine;

    float _masterVolume = 0.5f;

    public bool IsPlaying { get { return _engine != null && _engine.IsPlaying; } }
    public int CurrentStep { get { return _engine != null ? _engine.CurrentStep : -1; } }
    public float MasterVolume { get { return _masterVolume; } }

    public TraxDials Dials { get { return Track.dials; } }
    public int Key { get { return Track.key; } }
    public string KeyName { get { return Track.KeyName; } }
    public uint TrackId { get { return Track.TrackId(); } }
    public TraxClassifier.Result Genre { get { return TraxClassifier.Classify(Track.dials); } }

    public int PresetIndex(string module) { return Track.PresetOf(module); }
    public int VariationIndex(string module) { return Track.VariationOf(module); }
    public string PresetName(string module) { return TraxPresets.PresetName(module, Track.PresetOf(module)); }

    /// Raised when a queued pattern actually went live on a bar line.
    public event Action PatternSwapped;

    /// <summary>
    /// A blank track for a NEW PROJECT: the default, with anything you do not
    /// own switched off. <see cref="TraxTrack.Default"/> itself deliberately
    /// stays all-on and ownership-blind — it is the base case of the golden
    /// vectors, so masking it there would rewrite every one of them.
    /// </summary>
    public static TraxTrack NewTrack()
    {
        TraxTrack t = TraxTrack.Default();
        for (int m = 0; m < Modules.Length; m++)
            if (!TraxLibrary.IsInstalled(Modules[m].name)) t.active[m] = false;
        return t;
    }

    void Awake()
    {
        Track = NewTrack();
        Params = TraxParams.Compute(Track.dials, Track.key);
        Phrase = TraxPhrase.Generate(Track, Params);

        var go = new GameObject("TraxAudio");
        go.transform.SetParent(transform, false);
        go.AddComponent<AudioSource>();
        _engine = go.AddComponent<TraxAudioEngine>();

        _engine.Publish(Params, Phrase, false);
        _engine.SetMasterVolume(_masterVolume);
        _engine.SetCavePreset(TraxPresets.Cave[Track.PresetOf("CAVE")], Track.VariationOf("CAVE"));
        for (int i = 0; i < Modules.Length; i++)
            _engine.SetModuleEnabled(Modules[i].name, Track.active[i]);
    }

    void Update()
    {
        if (_engine != null && _engine.ConsumeSwapFlag())
        {
            var h = PatternSwapped;
            if (h != null) h();
        }
    }

    // ── the choke point ──────────────────────────────────────────────────

    public void SetTrack(TraxTrack next)
    {
        TraxTrack prev = Track;
        Track = next;
        Params = TraxParams.Compute(next.dials, next.key);

        if (_engine != null)
        {
            _engine.PublishParams(Params);
            if (prev.PresetOf("CAVE") != next.PresetOf("CAVE") ||
                prev.VariationOf("CAVE") != next.VariationOf("CAVE"))
                _engine.SetCavePreset(TraxPresets.Cave[next.PresetOf("CAVE")], next.VariationOf("CAVE"));

            // LOADING a project changes the active set wholesale, not just when
            // a toggle is clicked — so the engine syncs here, at the choke
            // point, rather than in the toggle handler.
            for (int m = 0; m < Modules.Length; m++)
                if (prev.active[m] != next.active[m])
                    _engine.SetModuleEnabled(Modules[m].name, next.active[m]);
        }

        if (TraxTrack.NeedsRegen(prev, next))
        {
            // Swap on a bar line while playing — mid-bar is audible as a stumble.
            Phrase = TraxPhrase.Generate(next, Params);
            if (_engine != null) _engine.Publish(Params, Phrase, IsPlaying);
        }
    }

    public void SetDial(int index, double value) { SetTrack(Track.WithDial(index, value)); }

    public void SetPreset(string module, int index) { SetTrack(Track.WithPreset(module, index)); }
    public void CyclePreset(string module, int delta) { SetPreset(module, Track.PresetOf(module) + delta); }

    public void SetVariation(string module, int index) { SetTrack(Track.WithVariation(module, index)); }
    public void CycleVariation(string module, int delta) { SetVariation(module, Track.VariationOf(module) + delta); }

    // Key never regenerates anything — it is applied when a degree becomes a
    // frequency, so the same phrase just moves.
    public void SetKey(int key) { SetTrack(Track.WithKey(key)); }
    public void CycleKey(int delta) { SetKey(Track.key + delta); }

    // ── transport ────────────────────────────────────────────────────────

    public void Play()
    {
        if (_engine == null) return;
        _engine.Publish(Params, Phrase, false);
        _engine.StartTransport();
    }

    public void Stop() { if (_engine != null) _engine.StopTransport(); }

    public void Toggle() { if (IsPlaying) Stop(); else Play(); }

    // ── song mode (the arrangement layer) ────────────────────────────────

    public bool IsPlayingSong { get { return IsPlaying && _engine.IsSongMode; } }
    public bool IsPlayingLoop { get { return IsPlaying && !_engine.IsSongMode; } }
    /// The play cursor as an absolute song step — where PLAY TRACK starts.
    public int SongCursor { get { return _engine != null ? _engine.SongCursor : 0; } }

    /// <summary>
    /// Compile and publish the whole song: one params + phrase pair per
    /// section. Cheap enough to call after any edit — a handful of sections
    /// generate in well under a millisecond — which keeps one code path
    /// instead of a per-section patch API.
    /// </summary>
    public void SetSong(TraxSong song)
    {
        if (_engine == null || song == null || song.sections.Count == 0) return;
        int n = song.sections.Count;
        var ps = new TraxParams[n];
        var phrases = new TraxPhrase[n];
        var tracks = new TraxTrack[n];
        var bars = new int[n];
        for (int i = 0; i < n; i++)
        {
            TraxSection sec = song.sections[i];
            ps[i] = TraxParams.Compute(sec.track.dials, sec.track.key);
            phrases[i] = TraxPhrase.Generate(sec.track, ps[i]);
            tracks[i] = sec.track;
            bars[i] = sec.bars;
        }
        _engine.PublishSong(ps, phrases, tracks, bars);
    }

    /// Starts from the play cursor (SeekSong sets it; STOP freezes it).
    public void PlaySong()
    {
        if (_engine == null) return;
        _engine.StartSongTransport();
    }

    public void SeekSong(int stepPos) { if (_engine != null) _engine.SeekSong(stepPos); }

    // ── rack ─────────────────────────────────────────────────────────────

    /// Which modules are PLAYING. Lives on the track now, so it prints onto a
    /// cassette and comes back when a project is loaded.
    public bool IsModuleEnabled(string name) { return Track.ActiveOf(name); }

    /// Ownership is WORLD state and lives on <see cref="TraxLibrary"/> — the
    /// shelf and the rack belong to the computer, not to this component, so
    /// they survive the terminal being rebuilt and are shared in co-op.
    public bool IsInstalled(string name) { return TraxLibrary.IsInstalled(name); }

    /// Muting is a track edit, so it goes through the choke point like every
    /// other one. Switching ON a module you do not own is refused rather than
    /// silently allowed — the lock is the carrot for Tev's shop.
    public bool SetModuleEnabled(string name, bool on)
    {
        if (on && !IsInstalled(name)) return false;
        SetTrack(Track.WithActive(name, on));
        return true;
    }

    /// Load a saved project onto the deck. Goes through the choke point, so the
    /// audio engine picks up the whole track — dials, key, parts AND which
    /// modules were playing — in one move.
    public void LoadTrack(TraxTrack track)
    {
        if (track != null) SetTrack(track.Clone());
    }

    public void SetMasterVolume(float v)
    {
        _masterVolume = Mathf.Clamp01(v);
        if (_engine != null) _engine.SetMasterVolume(_masterVolume);
    }

    /// True if this voice's rack module is on — used by the UI's step grid.
    public bool VoiceAudible(TraxVoice v)
    {
        return IsModuleEnabled(TraxModules.For(v));
    }
}
