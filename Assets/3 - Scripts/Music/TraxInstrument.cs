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
    readonly System.Collections.Generic.Dictionary<string, bool> _enabled =
        new System.Collections.Generic.Dictionary<string, bool>();

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

    void Awake()
    {
        Track = TraxTrack.Default();
        Params = TraxParams.Compute(Track.dials, Track.key);
        Phrase = TraxPhrase.Generate(Track, Params);

        for (int i = 0; i < Modules.Length; i++) _enabled[Modules[i].name] = true;

        var go = new GameObject("TraxAudio");
        go.transform.SetParent(transform, false);
        go.AddComponent<AudioSource>();
        _engine = go.AddComponent<TraxAudioEngine>();

        _engine.Publish(Params, Phrase, false);
        _engine.SetMasterVolume(_masterVolume);
        _engine.SetCavePreset(TraxPresets.Cave[Track.PresetOf("CAVE")], Track.VariationOf("CAVE"));
        foreach (var kv in _enabled) _engine.SetModuleEnabled(kv.Key, kv.Value);
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

    // ── rack ─────────────────────────────────────────────────────────────

    public bool IsModuleEnabled(string name)
    {
        bool v;
        return _enabled.TryGetValue(name, out v) && v;
    }

    public void SetModuleEnabled(string name, bool on)
    {
        if (!_enabled.ContainsKey(name)) return;
        _enabled[name] = on;
        if (_engine != null) _engine.SetModuleEnabled(name, on);
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
