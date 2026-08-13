using System;
using UnityEngine;

/// <summary>
/// Owns dial state, drives the engine, feeds the audio backend.
/// PORT OF <c>prototypes/shuttle-computer/audio/instrument.js</c>.
///
/// ── This class is the choke point, on purpose ─────────────────────────────
/// EVERY dial change goes through <see cref="SetDials"/>. Nothing else may
/// write dial state — not the UI widgets, not the terminal, not a future
/// preset loader. When full-length track recording is built (spec §9.5), it
/// becomes "log what passes through SetDials, stamped with
/// <see cref="TraxAudioEngine.CurrentStep"/>" and nothing else has to change.
/// Stamp with the STEP INDEX, never seconds: pattern-affecting dials only take
/// effect on bar boundaries, so a seconds-stamped event replayed at a different
/// BPM lands in the wrong bar.
/// </summary>
public class TraxInstrument : MonoBehaviour
{
    public struct ModuleDef
    {
        public string name;
        public string desc;
        public bool locked;
        public ModuleDef(string n, string d, bool l) { name = n; desc = d; locked = l; }
    }

    public static readonly ModuleDef[] Modules =
    {
        new ModuleDef("THUMPER", "drums",  false),
        new ModuleDef("GLOWORM", "bass",   false),
        new ModuleDef("SIREN",   "lead",   false),
        new ModuleDef("CAVE",    "space",  false),
        new ModuleDef("??????",  "locked", true),
        new ModuleDef("??????",  "locked", true)
    };

    public TraxDials Dials { get; private set; }
    public TraxParams Params { get; private set; }
    public TraxPhrase Phrase { get; private set; }

    TraxAudioEngine _engine;
    readonly System.Collections.Generic.Dictionary<string, bool> _enabled =
        new System.Collections.Generic.Dictionary<string, bool>();

    float _masterVolume = 0.5f;

    public bool IsPlaying { get { return _engine != null && _engine.IsPlaying; } }
    public int CurrentStep { get { return _engine != null ? _engine.CurrentStep : -1; } }
    public float MasterVolume { get { return _masterVolume; } }

    public uint Seed { get { return TraxPrng.SeedFromDials(Dials); } }
    public TraxClassifier.Result Genre { get { return TraxClassifier.Classify(Dials); } }

    /// Raised when a queued pattern actually went live on a bar line.
    public event Action PatternSwapped;

    void Awake()
    {
        Dials = TraxDials.Default;
        Params = TraxParams.Compute(Dials);
        Phrase = TraxPhrase.Generate(TraxPrng.SeedFromDials(Dials), Params);

        for (int i = 0; i < Modules.Length; i++)
            if (!Modules[i].locked) _enabled[Modules[i].name] = true;

        var go = new GameObject("TraxAudio");
        go.transform.SetParent(transform, false);
        go.AddComponent<AudioSource>();
        _engine = go.AddComponent<TraxAudioEngine>();

        _engine.Publish(Params, Phrase, false);
        _engine.SetMasterVolume(_masterVolume);
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

    // ── dials ────────────────────────────────────────────────────────────

    public void SetDial(int index, double value)
    {
        SetDials(Dials.With(index, value));
    }

    public void SetDials(TraxDials next)
    {
        TraxDials prev = Dials;
        Dials = next;
        Params = TraxParams.Compute(Dials);

        if (TraxParams.NeedsRegen(prev, Dials))
        {
            // While playing, hold the new phrase until the bar turns over —
            // swapping mid-bar is audible as a stumble. The engine's step
            // counter keeps running across the swap, so phrase position and the
            // bar-3 fill stay where they belong.
            Phrase = TraxPhrase.Generate(TraxPrng.SeedFromDials(Dials), Params);
            if (_engine != null) _engine.Publish(Params, Phrase, IsPlaying);
        }
        else
        {
            // Timbre and tempo ride live — BPM should feel attached to your hand.
            if (_engine != null) _engine.PublishParams(Params);
        }
    }

    // ── transport ────────────────────────────────────────────────────────

    public void Play()
    {
        if (_engine == null) return;
        _engine.Publish(Params, Phrase, false);
        _engine.StartTransport();
    }

    public void Stop()
    {
        if (_engine != null) _engine.StopTransport();
    }

    public void Toggle()
    {
        if (IsPlaying) Stop(); else Play();
    }

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

    /// True if this voice's rack module is currently on — used by the UI's step grid.
    public bool VoiceAudible(TraxVoice v)
    {
        switch (v)
        {
            case TraxVoice.Kick:
            case TraxVoice.Snare:
            case TraxVoice.Hat:
                return IsModuleEnabled("THUMPER");
            case TraxVoice.Bass: return IsModuleEnabled("GLOWORM");
            case TraxVoice.Lead: return IsModuleEnabled("SIREN");
        }
        return false;
    }
}
