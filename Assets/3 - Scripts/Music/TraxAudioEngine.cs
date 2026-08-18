using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// The TRAX synth. Renders every voice sample-by-sample in OnAudioFilterRead.
///
/// ── Why a custom DSP path and not AudioClips + filter components ──────────
/// The handoff's "Option A" sketched AudioClip.Create + AudioSource.pitch +
/// built-in AudioLowPassFilter/AudioEchoFilter components. That path cannot
/// reproduce four things the browser version does, and all four are audible:
///   • sample-accurate amplitude envelopes (AudioSource.volume animates per
///     frame, so every note's attack smears by up to a frame)
///   • a continuous sine→saw→square morph (CRUNCH) rather than three fixed clips
///   • a resonant filter whose cutoff is swept by an LFO (GOO)
///   • per-note filter state, since filter components are per-AudioSource
/// Rendering it directly is the same "pure Unity, no asset purchase" bucket —
/// OnAudioFilterRead is built in — and it keeps the structure identical to the
/// Web Audio graph, which is the point of the whole prototype-first exercise.
///
/// ── Audio-thread rules (OnAudioFilterRead runs OFF the main thread) ───────
/// • NO allocation in the render path. Every buffer, voice slot and event is
///   pre-allocated in Awake. A GC pause here is an audible dropout.
/// • NO Unity API calls (transform, Time, Debug.Log). Nothing here touches them.
/// • Main thread communicates by publishing an immutable <see cref="Snapshot"/>
///   and reading it once per buffer. Pattern swaps land on bar lines only.
///
/// The sequencer lives IN the audio callback, so note onsets are sample-accurate
/// and there is no lookahead scheduler at all — the browser needed one because
/// setTimeout can't be trusted; here the render loop IS the clock.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class TraxAudioEngine : MonoBehaviour
{
    // ── published state (main thread writes, audio thread reads) ─────────

    /// Immutable bundle so params and pattern can never be read half-updated.
    sealed class Snapshot
    {
        public readonly TraxParams p;
        public readonly TraxPhrase phrase;
        public Snapshot(TraxParams p, TraxPhrase phrase) { this.p = p; this.phrase = phrase; }
    }

    Snapshot _live;
    Snapshot _pending;

    // ── song mode (the arrangement layer) ────────────────────────────────
    // When a song snapshot is live and _songMode is set, the sequencer walks
    // SECTIONS — each with its own params, phrase, active set and CAVE space —
    // instead of looping the single live phrase. Loop mode is untouched.

    /// One section, fully compiled for the audio thread. Immutable after
    /// publish.
    public sealed class SongSec
    {
        public TraxParams p;
        public TraxPhrase phrase;
        public bool[] active;        // module order, TraxPresets indices
        public int bars;
        public int startStep;
        public int tapA, tapB;       // CAVE reads, in samples
        public float dampCoef, fbScale;
    }

    sealed class SongSnap
    {
        public SongSec[] secs;
        public int totalSteps;
    }

    SongSnap _songLive;
    volatile bool _songMode;
    /// Where in the song the clock's step 0 lands — the play cursor. While
    /// playing, a seek rewrites it against the running clock.
    volatile int _songOffset;
    volatile int _rawStep;           // the clock's raw step, for seek maths
    volatile int _songSecIdx = -1;   // section under the scheduler, for the UI + per-buffer params

    volatile bool _playing;
    volatile float _masterVolume = 0.5f;
    volatile float _busLevel = 1f;
    volatile bool _onThumper = true, _onGloworm = true, _onSiren = true, _onCave = true;
    volatile bool _onMoss = true, _onSpindle = true;
    volatile int _uiStep = -1;
    volatile bool _swapFlag;

    public int CurrentStep { get { return _uiStep; } }
    public bool IsPlaying { get { return _playing; } }
    /// Set true by the audio thread when a queued pattern actually went live.
    public bool ConsumeSwapFlag()
    {
        if (!_swapFlag) return false;
        _swapFlag = false;
        return true;
    }

    // ── DSP state (audio thread only, after Awake) ───────────────────────

    const int MaxVoices = 28;
    const int MaxEvents = 128;
    const double NudgeLatency = 0.02;   // seconds of slack so a negative nudge is still in the future

    struct Vox
    {
        public bool active;
        public TraxVoice kind;
        public double phase, phase2, inc, inc2;   // oscillators (0..1 phase)
        public double morphA, morphB;             // crossfade gains
        public double env;                        // 0..1
        public double attackStep;                 // per-sample rise during attack
        public double decayCoef;                  // per-sample multiply during RELEASE
        public int attackLeft;
        // Browser ampEnv() parity: rise, HOLD at peak until ~70% of the note,
        // then exponential release. Decaying straight after the attack (the
        // first port) thinned every note's body audibly.
        public long holdUntil;
        public double amp;
        public long endAt;                        // hard stop sample
        public long fadeFrom;                     // start of the anti-click fade
        public double ic1, ic2;                   // TPT state-variable filter memory
        public double bqX1, bqX2, bqY1, bqY2;     // RBJ biquad memory (snare/hat)
        public double noiseIdx;
        public double bodyPhase, bodyInc, bodyEnv, bodyCoef;
        public double freqEnv, freqCoef, freqFloor, freqSpan;   // kick pitch drop
        public bool open;
    }

    Vox[] _vox;

    struct Evt
    {
        public long at;
        public TraxVoice voice;
        public TraxStep st;
        public double freq;
        public double durSec;
    }

    Evt[] _events;
    int _evtCount;

    // Per-buffer filter coefficients, one set per voice, indexed by (int)TraxVoice.
    // Arrays rather than a pile of out-parameters: four melodic voices at three
    // coefficients each is twelve arguments to thread through the render loop.
    // Preallocated, because the render path must not allocate.
    readonly double[] _ca1 = new double[TraxPhrase.VoiceCount];
    readonly double[] _ca2 = new double[TraxPhrase.VoiceCount];
    readonly double[] _ca3 = new double[TraxPhrase.VoiceCount];

    // How far each melodic voice sits above the base cutoff. MOSS is darkest so
    // the pad never competes with the lead; SPINDLE is brightest so the arp cuts.
    static readonly double[] FilterScale = { 1, 1, 1, 1.0, 2.2, 0.8, 3.0 };

    float[] _noise;
    float[] _delayA, _delayB;
    int _writeA, _writeB, _maxDelay;
    double _dampA, _dampB;
    // Written by the main thread, read once per buffer by the audio thread.
    volatile int _tapA = 9120, _tapB = 14880;
    volatile float _dampCoefF = 0.3f;
    volatile float _caveFbScale = 0.8f;

    int _sr = 48000;
    long _sample;
    int _step;
    double _nextStepSample;
    double _lfoPhase;

    // ── transition FX + glide state (audio thread only) ──────────────────

    // Which rack module owns which voice, as indices — string lookups have no
    // place in the sequencer path.
    static readonly int[] VoiceModuleIdx = BuildVoiceModuleIdx();
    static int[] BuildVoiceModuleIdx()
    {
        var a = new int[TraxPhrase.VoiceCount];
        for (int v = 0; v < TraxPhrase.VoiceCount; v++)
            a[v] = TraxPresets.ModuleIndex(TraxModules.For((TraxVoice)v));
        return a;
    }
    static readonly int ThumperModuleIdx = TraxPresets.ModuleIndex("THUMPER");
    static readonly int CaveModuleIdx = TraxPresets.ModuleIndex("CAVE");

    // The transition snare roll: which steps of a section's LAST bar get an
    // extra snare, and how hard — eighths for two beats, then sixteenths into
    // the downbeat. Doubles the phrase's own fill.
    static readonly double[] SnareRoll =
        { 0, 0, 0, 0, 0, 0, 0, 0, 0.45, 0, 0.55, 0, 0.65, 0.75, 0.85, 1.0 };

    // RISER: band-passed noise climbing linearly through a section's last bar.
    bool _riserOn;
    long _riserStart, _riserEnd;
    double _riserIdx, _riserLp;

    // IMPACT: darkening noise splash + low sine thump on the new downbeat.
    bool _impactOn;
    long _impactStart, _impactEnd;
    double _impactIdx, _impactLp, _impactEnv, _impactPhase, _impactFreqEnv, _impactThumpEnv;
    double _impactEnvCoef, _impactThumpCoef, _impactFreqCoef;

    // Glide: in song mode the continuous params one-pole toward the current
    // section's targets (~0.12s), so a boundary morphs instead of snapping.
    // BPM is NOT smoothed — tempo glides sound like a dying turntable.
    bool _smInit;
    double _smFilterBase, _smLfoRate, _smLfoDepth, _smDrive, _smCrush, _smSend, _smFb, _smMix;

    // ── browser-parity DSP (2026-08-17, "the build sounds worse") ────────

    /// The GOO wobble recomputes the melodic filter coefficients every this
    /// many samples. Once per buffer (~21ms) stepped audibly; 32 samples
    /// (0.7ms) is indistinguishable from the browser's per-sample modulation
    /// and costs a handful of tan() calls per buffer.
    const int LfoBlock = 32;

    // Master compressor — port of the browser's DynamicsCompressor (threshold
    // -6dB, knee 6, ratio 12, attack 3ms, release 120ms) INCLUDING Chrome's
    // automatic makeup gain, which is a real part of why the browser mix
    // sounds glued and fat. Sits after the volume, before the safety clip,
    // exactly where the browser's node sits.
    const double CompThresholdDb = -6.0, CompKneeDb = 6.0, CompRatio = 12.0;
    double _compEnv, _compAtk, _compRel, _compMakeup;

    // Snare bandpass (1800Hz, Q 0.9) and hat highpass (7kHz) as real RBJ
    // biquads, matching the browser's BiquadFilterNodes. The one-pole
    // stand-ins of the first port read as a woolly snare and a dull hat.
    double _snB0, _snB1, _snB2, _snA1, _snA2;
    double _htB0, _htB1, _htB2, _htA1, _htA2;

    AudioSource _src;

    // ── lifecycle ────────────────────────────────────────────────────────

    void Awake()
    {
        _sr = AudioSettings.outputSampleRate;
        if (_sr <= 0) _sr = 48000;

        _vox = new Vox[MaxVoices];
        _events = new Evt[MaxEvents];

        // Noise from the SAME fixed seed the browser uses, so the snare and hat
        // are literally the same noise in both builds.
        int noiseLen = _sr * 2;
        _noise = new float[noiseLen];
        var rnd = new TraxPrng.Rng(0x5eed0135u);
        for (int i = 0; i < noiseLen; i++) _noise[i] = (float)(rnd.Next() * 2.0 - 1.0);

        // Sized for the longest CAVE preset (CANYON/VOID) with headroom; the
        // preset then picks a read distance inside that buffer.
        _maxDelay = Mathf.CeilToInt(1.2f * _sr);
        _delayA = new float[_maxDelay];
        _delayB = new float[_maxDelay];

        _impactEnvCoef = DecayCoefFor(0.18, _sr);
        _impactThumpCoef = DecayCoefFor(0.09, _sr);
        _impactFreqCoef = DecayCoefFor(0.05, _sr);

        // Compressor time constants + Chrome-style makeup: gain reduction at
        // full scale is (0 - threshold) * (1 - 1/ratio); Chrome's makeup is
        // (1 / gainAtFullScale)^0.6.
        _compAtk = 1.0 - Math.Exp(-1.0 / (0.003 * _sr));
        _compRel = 1.0 - Math.Exp(-1.0 / (0.120 * _sr));
        double fullScaleReductionDb = -CompThresholdDb * (1.0 - 1.0 / CompRatio);
        _compMakeup = Math.Pow(Math.Pow(10.0, fullScaleReductionDb / 20.0), 0.6);

        BiquadBandpass(1800.0, 0.9, out _snB0, out _snB1, out _snB2, out _snA1, out _snA2);
        BiquadHighpass(7000.0, 0.707, out _htB0, out _htB1, out _htB2, out _htA1, out _htA2);
        SetCavePreset(TraxPresets.Cave[1], 0);          // HALL, matching the default track

        _src = GetComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = true;
        _src.spatialBlend = 0f;          // 2D by default — the terminal UI is fullscreen, not in the world
        _src.volume = 1f;                // bus level is multiplied in by hand, see Update
        _src.bypassEffects = false;
        _src.bypassListenerEffects = false;

        // OnAudioFilterRead is only invoked while the source is playing, so it
        // needs *something* to play. A short silent loop costs nothing; the
        // render callback overwrites the buffer wholesale.
        var silence = AudioClip.Create("TraxSilence", 1024, 1, _sr, false);
        silence.SetData(new float[1024], 0);
        _src.clip = silence;
        _src.Play();
    }

    void Update()
    {
        // GameAudioBus.Register is explicitly NOT the right tool here: it captures
        // src.volume once as "authored", and this source's level is computed every
        // frame. Its own docs say to multiply Level() in by hand for exactly this
        // case. Music bus — this is music.
        _busLevel = GameAudioBus.Level(GameAudioBus.Bus.Music);
    }

    void OnDestroy()
    {
        _playing = false;
    }

    // ── main-thread API ──────────────────────────────────────────────────

    /// <summary>
    /// Make this engine a point in the world rather than a fullscreen UI sound.
    /// Unity spatialises AFTER OnAudioFilterRead, so the synth is unaffected —
    /// only where you hear it from changes. Used when an alien plays a cassette
    /// at you; the terminal stays 2D.
    /// </summary>
    public void SetSpatial(bool on, float minDistance = 3f, float maxDistance = 28f)
    {
        if (_src == null) _src = GetComponent<AudioSource>();
        _src.spatialBlend = on ? 1f : 0f;
        if (!on) return;
        _src.rolloffMode = AudioRolloffMode.Linear;
        _src.minDistance = minDistance;
        _src.maxDistance = maxDistance;
        _src.dopplerLevel = 0f;          // a tape deck does not warble when you walk past it
    }

    public void Publish(TraxParams p, TraxPhrase phrase, bool atBarBoundary)
    {
        var snap = new Snapshot(p, phrase);
        if (atBarBoundary && _playing) Interlocked.Exchange(ref _pending, snap);
        else
        {
            Interlocked.Exchange(ref _live, snap);
            Interlocked.Exchange(ref _pending, null);
        }
    }

    /// Params-only update (timbre/tempo). Keeps the currently playing phrase.
    public void PublishParams(TraxParams p)
    {
        var cur = Volatile.Read(ref _live);
        var phrase = cur != null ? cur.phrase : null;
        if (phrase == null) return;
        Interlocked.Exchange(ref _live, new Snapshot(p, phrase));
    }

    public void StartTransport()
    {
        if (Volatile.Read(ref _live) == null) return;
        _songMode = false;
        StartClock();
    }

    void StartClock()
    {
        _sample = 0;
        _step = 0;
        _rawStep = 0;
        _nextStepSample = 0;
        _evtCount = 0;
        _songSecIdx = -1;
        _riserOn = false;
        _impactOn = false;
        _smInit = false;
        for (int i = 0; i < _vox.Length; i++) _vox[i].active = false;
        Array.Clear(_delayA, 0, _delayA.Length);
        Array.Clear(_delayB, 0, _delayB.Length);
        _uiStep = -1;
        _playing = true;
    }

    public void StopTransport()
    {
        // Freeze the song position into the cursor, so the idle line shows
        // where playback stopped and PLAY TRACK resumes there.
        var song = Volatile.Read(ref _songLive);
        if (_playing && _songMode && song != null && _uiStep >= 0)
            _songOffset = _uiStep % song.totalSteps;
        _playing = false;
        _uiStep = -1;
        _songSecIdx = -1;
    }

    // ── song-mode API ────────────────────────────────────────────────────

    /// <summary>
    /// Publish the whole compiled song. Sections arrive as parallel arrays
    /// from the instrument (which owns pattern generation); the engine adds
    /// the CAVE numbers, which depend on the output sample rate.
    /// </summary>
    public void PublishSong(TraxParams[] ps, TraxPhrase[] phrases, TraxTrack[] tracks, int[] bars)
    {
        if (ps == null || ps.Length == 0) { Interlocked.Exchange(ref _songLive, null); return; }
        var secs = new SongSec[ps.Length];
        int start = 0;
        int caveIdx = TraxPresets.ModuleIndex("CAVE");
        for (int i = 0; i < ps.Length; i++)
        {
            var preset = TraxPresets.Cave[tracks[i].preset[caveIdx]];
            double skew = 1.0 + (tracks[i].variation[caveIdx] - 3.5) * 0.03;
            int max = _maxDelay > 0 ? _maxDelay - 1 : 1;
            var active = new bool[tracks[i].active.Length];
            Array.Copy(tracks[i].active, active, active.Length);
            secs[i] = new SongSec
            {
                p = ps[i],
                phrase = phrases[i],
                active = active,
                bars = bars[i],
                startStep = start,
                tapA = Mathf.Clamp((int)(preset.timeA * skew * _sr), 1, max),
                tapB = Mathf.Clamp((int)(preset.timeB / skew * _sr), 1, max),
                dampCoef = (float)(1.0 - Math.Exp(-2.0 * Math.PI * preset.damp / _sr)),
                fbScale = (float)preset.fb
            };
            start += bars[i] * TraxPhrase.Steps;
        }
        Interlocked.Exchange(ref _songLive, new SongSnap { secs = secs, totalSteps = start });
    }

    public void StartSongTransport()
    {
        if (Volatile.Read(ref _songLive) == null) return;
        _songMode = true;
        StartClock();
    }

    public bool IsSongMode { get { return _songMode; } }

    /// The play cursor as an absolute song step — meaningful while stopped.
    public int SongCursor { get { return _songOffset; } }

    /// Jump the song playhead. While playing the jump lands within a step or
    /// two (the seek races the scheduler by design — bar-level precision is
    /// all the ruler offers anyway); while stopped it just moves the cursor.
    public void SeekSong(int stepPos)
    {
        var song = Volatile.Read(ref _songLive);
        if (song == null || song.totalSteps <= 0) return;
        int total = song.totalSteps;
        int t = ((stepPos % total) + total) % total;
        if (_playing && _songMode)
            _songOffset = (((t - _rawStep) % total) + total) % total;
        else
            _songOffset = t;
        _songSecIdx = -1;                // re-apply section state on landing
    }

    public void SetMasterVolume(float v) { _masterVolume = Mathf.Clamp01(v); }

    /// <summary>
    /// CAVE's preset picks a space: how far back the taps read and how dark
    /// the feedback path is. VARIATION skews the tap ratio a few percent so
    /// the control means something on CAVE too rather than sitting inert.
    /// </summary>
    public void SetCavePreset(TraxPresets.SpacePreset preset, int variation)
    {
        double skew = 1.0 + (variation - 3.5) * 0.03;
        int a = (int)(preset.timeA * skew * _sr);
        int b = (int)(preset.timeB / skew * _sr);
        int max = _maxDelay > 0 ? _maxDelay - 1 : 1;
        _tapA = Mathf.Clamp(a, 1, max);
        _tapB = Mathf.Clamp(b, 1, max);
        _dampCoefF = (float)(1.0 - Math.Exp(-2.0 * Math.PI * preset.damp / _sr));
        _caveFbScale = (float)preset.fb;
    }

    public void SetModuleEnabled(string module, bool on)
    {
        switch (module)
        {
            case "THUMPER": _onThumper = on; break;
            case "GLOWORM": _onGloworm = on; break;
            case "SIREN":   _onSiren = on; break;
            case "MOSS":    _onMoss = on; break;
            case "SPINDLE": _onSpindle = on; break;
            case "CAVE":    _onCave = on; break;
        }
    }

    // ── render ───────────────────────────────────────────────────────────

    void OnAudioFilterRead(float[] data, int channels)
    {
        int frames = channels > 0 ? data.Length / channels : 0;

        var snap = Volatile.Read(ref _live);
        if (!_playing || snap == null || frames == 0)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        // In song mode the buffer's character comes from the section under the
        // scheduler, and CAVE space / module set ride the sections too.
        SongSnap song = _songMode ? Volatile.Read(ref _songLive) : null;
        SongSec bufSec = null;
        if (song != null)
        {
            int si = _songSecIdx;
            if (si < 0 || si >= song.secs.Length) si = 0;
            bufSec = song.secs[si];
        }
        TraxParams p = bufSec != null ? bufSec.p : snap.p;

        // Glide: the continuous params morph toward the section's targets over
        // ~0.12s at every boundary. Discrete things (patterns, taps, module
        // set) still switch exactly on the bar line.
        double gFilterBase = p.filterBase, gLfoRate = p.lfoRate, gLfoDepth = p.lfoDepthOct;
        double gDrive = p.drive, gCrush = p.crushLevels;
        double gSend = p.caveSend, gFb = p.caveFeedback, gMix = p.caveMix;
        if (song != null)
        {
            if (!_smInit)
            {
                _smInit = true;
                _smFilterBase = gFilterBase; _smLfoRate = gLfoRate; _smLfoDepth = gLfoDepth;
                _smDrive = gDrive; _smCrush = gCrush; _smSend = gSend; _smFb = gFb; _smMix = gMix;
            }
            else
            {
                double a = 1.0 - Math.Exp(-((double)frames / _sr) / 0.12);
                _smFilterBase += (gFilterBase - _smFilterBase) * a;
                _smLfoRate += (gLfoRate - _smLfoRate) * a;
                _smLfoDepth += (gLfoDepth - _smLfoDepth) * a;
                _smDrive += (gDrive - _smDrive) * a;
                _smCrush += (gCrush - _smCrush) * a;
                _smSend += (gSend - _smSend) * a;
                _smFb += (gFb - _smFb) * a;
                _smMix += (gMix - _smMix) * a;
            }
            gFilterBase = _smFilterBase; gLfoRate = _smLfoRate; gLfoDepth = _smLfoDepth;
            gDrive = _smDrive; gCrush = _smCrush; gSend = _smSend; gFb = _smFb; gMix = _smMix;
        }

        // Filter resonance for the sub-block coefficient updates below. The
        // LFO itself advances per 32-sample sub-block, not per buffer — per
        // buffer the GOO wobble stepped audibly (browser parity note).
        double k = 1.0 / Math.Max(0.5, p.filterQ);

        double driveTone = 1.0 + gDrive * 30.0;
        double driveDrum = 1.0 + gDrive * 0.5 * 30.0;
        double normTone = FastTanh(driveTone);
        double normDrum = FastTanh(driveDrum);
        int crushNow = (int)Math.Round(gCrush);
        int levelsTone = crushNow;
        int levelsDrum = Math.Min(64, crushNow * 2);

        double sendTone = gSend;
        double sendDrum = gSend * 0.3;
        // VOID (0.97) times a full VOID dial would run away; the cap is what
        // keeps the round trip below unity so the tail always decays.
        double fb = Math.Min(0.9, gFb * (bufSec != null ? bufSec.fbScale : _caveFbScale) * 1.25);
        int tapA = bufSec != null ? bufSec.tapA : _tapA;
        int tapB = bufSec != null ? bufSec.tapB : _tapB;
        double damp = bufSec != null ? bufSec.dampCoef : _dampCoefF;
        bool caveOn = bufSec != null ? bufSec.active[CaveModuleIdx] : _onCave;
        double wetMix = caveOn ? gMix : 0.0;
        double outGain = _masterVolume * _busLevel;

        // ── schedule any steps that begin inside this buffer ──
        // Loop mode: one tempo for the whole buffer. Song mode: each step's
        // duration comes from the section that owns it, so tempo changes land
        // exactly on section boundaries.
        double samplesPerStep = _sr * 60.0 / p.bpm / 4.0;
        if (samplesPerStep < 1) samplesPerStep = 1;
        long bufEnd = _sample + frames;
        while (_nextStepSample < bufEnd)
        {
            if (song != null)
                samplesPerStep = EvaluateStepSong(song, _step, (long)_nextStepSample);
            else
                EvaluateStep(snap, _step, (long)_nextStepSample, samplesPerStep);
            if (samplesPerStep < 1) samplesPerStep = 1;
            _step++;
            _nextStepSample += samplesPerStep;
        }

        // ── render ──
        int i = 0;
        while (i < frames)
        {
            // Sub-block: advance the GOO LFO and refresh the melodic filter
            // coefficients every 32 samples, so the wobble sweeps instead of
            // stepping. A handful of tan() calls per buffer — negligible.
            int blockEnd = i + LfoBlock;
            if (blockEnd > frames) blockEnd = frames;
            _lfoPhase += (gLfoRate * (blockEnd - i)) / _sr;
            if (_lfoPhase > 1.0) _lfoPhase -= Math.Floor(_lfoPhase);
            double lfoMul = Math.Pow(2.0, Math.Sin(_lfoPhase * 2.0 * Math.PI) * gLfoDepth);
            for (int v = (int)TraxVoice.Bass; v < TraxPhrase.VoiceCount; v++)
            {
                double ca1, ca2, ca3;
                SvfCoef(gFilterBase * FilterScale[v] * lfoMul, k, out ca1, out ca2, out ca3);
                _ca1[v] = ca1; _ca2[v] = ca2; _ca3[v] = ca3;
            }

        for (; i < blockEnd; i++)
        {
            long now = _sample + i;

            // Fire any events due at this sample.
            for (int e = 0; e < _evtCount; e++)
            {
                if (_events[e].at > now) continue;
                StartVoice(ref _events[e], now, p);
                _evtCount--;
                _events[e] = _events[_evtCount];
                e--;
            }

            double tone = 0, drum = 0;

            for (int v = 0; v < _vox.Length; v++)
            {
                if (!_vox[v].active) continue;
                double s = RenderVoice(ref _vox[v], now, p);
                if (TraxPhrase.IsMelodic(_vox[v].kind)) tone += s;
                else drum += s;
            }

            // Section-transition one-shots ride the drum bus, so they share
            // its crunch and cannot be silenced by a muted module.
            if (_riserOn || _impactOn) drum += TransitionFxSample(now);

            // Drive + amplitude quantization (CRUNCH). Quantizing the shaped
            // signal IS bit-crushing; no worklet or extra pass needed.
            tone = Shape(tone, driveTone, normTone, levelsTone);
            drum = Shape(drum, driveDrum, normDrum, levelsDrum);

            // CAVE: two cross-fed delay lines with damping, read before write.
            // Read `tap` samples behind the write head. Changing the tap
            // moves the echo without resizing or clearing the line.
            int readA = _writeA - tapA; if (readA < 0) readA += _maxDelay;
            int readB = _writeB - tapB; if (readB < 0) readB += _maxDelay;
            double outA = _delayA[readA];
            double outB = _delayB[readB];
            _dampA += damp * (outA - _dampA);
            _dampB += damp * (outB - _dampB);
            double send = tone * sendTone + drum * sendDrum;
            _delayA[_writeA] = (float)(send + _dampB * fb);
            _delayB[_writeB] = (float)(send + _dampA * fb);
            if (++_writeA >= _maxDelay) _writeA = 0;
            if (++_writeB >= _maxDelay) _writeB = 0;

            double dry = tone + drum;
            // Ping-pong the two taps for a little width.
            double l = dry + outA * wetMix;
            double r = dry + outB * wetMix;

            l *= outGain;
            r *= outGain;

            // Master compressor, linked stereo — the browser's glue. Envelope
            // follows the louder channel; soft knee in dB; makeup restores the
            // level the reduction took (Chrome does the same automatically).
            double lvl = Math.Abs(l) > Math.Abs(r) ? Math.Abs(l) : Math.Abs(r);
            _compEnv += (lvl > _compEnv ? _compAtk : _compRel) * (lvl - _compEnv);
            double envDb = 20.0 * Math.Log10(_compEnv > 1e-6 ? _compEnv : 1e-6);
            double over = envDb - CompThresholdDb;
            double redDb;
            if (over <= -CompKneeDb * 0.5) redDb = 0.0;
            else if (over < CompKneeDb * 0.5)
            {
                double t = over + CompKneeDb * 0.5;
                redDb = t * t / (2.0 * CompKneeDb) * (1.0 - 1.0 / CompRatio);
            }
            else redDb = over * (1.0 - 1.0 / CompRatio);
            double cg = Math.Pow(10.0, -redDb / 20.0) * _compMakeup;

            l = SoftClip(l * cg);
            r = SoftClip(r * cg);

            int idx = i * channels;
            if (channels == 1)
            {
                data[idx] = (float)((l + r) * 0.5);
            }
            else
            {
                data[idx] = (float)l;
                data[idx + 1] = (float)r;
                for (int c = 2; c < channels; c++) data[idx + c] = 0f;
            }
        }
        }

        _sample += frames;
    }

    // ── sequencing ───────────────────────────────────────────────────────

    void EvaluateStep(Snapshot snap, int step, long baseSample, double samplesPerStep)
    {
        // Pattern swaps land on bar lines only. The global step counter keeps
        // running across the swap, so phrase position is preserved and the fill
        // still arrives where it should.
        if (step % TraxPhrase.Steps == 0)
        {
            var pend = Interlocked.Exchange(ref _pending, null);
            if (pend != null)
            {
                Interlocked.Exchange(ref _live, pend);
                snap = pend;
                _swapFlag = true;
            }
        }

        _uiStep = step;
        _rawStep = step;

        EmitStepEvents(snap.phrase, snap.p, null, step, baseSample);
    }

    /// <summary>
    /// One step of SONG playback. Walks the arrangement: the section under the
    /// step supplies params, phrase and active set, the LAST bar of a section
    /// remaps onto the phrase's fill bar, and the boundary FX (riser, snare
    /// roll, impact) arm here. Returns this step's duration in samples, so
    /// tempo changes land exactly on section boundaries.
    /// </summary>
    double EvaluateStepSong(SongSnap song, int step, long baseSample)
    {
        int total = song.totalSteps;
        int pos = (((step + _songOffset) % total) + total) % total;

        int si = 0;
        for (int i = 1; i < song.secs.Length; i++)
            if (pos >= song.secs[i].startStep) si = i; else break;
        SongSec sec = song.secs[si];
        int stepInSection = pos - sec.startStep;
        int secSteps = sec.bars * TraxPhrase.Steps;

        _songSecIdx = si;
        _rawStep = step;
        _uiStep = pos;

        // Fill into the boundary: the last bar always plays the fill bar.
        int barInSection = stepInSection / TraxPhrase.Steps;
        int patBar = barInSection == sec.bars - 1
            ? TraxPhrase.FullFillBar : barInSection % TraxPhrase.Bars;
        int patStep = patBar * TraxPhrase.Steps + (stepInSection % TraxPhrase.Steps);

        EmitStepEvents(sec.phrase, sec.p, sec.active, patStep, baseSample);

        // Boundary FX on EVERY section change, at fixed strength — the
        // smart-intensity gate is off (Sam, 2026-08-17; TraxSong keeps the
        // measure for if it returns). The song is circular, so the tail
        // rises back into the head too.
        if (song.secs.Length > 1)
        {
            long lat = (long)(NudgeLatency * _sr);
            double stepDurSamples = _sr * 60.0 / sec.p.bpm / 4.0;

            if (stepInSection == secSteps - TraxPhrase.Steps)      // one bar out
            {
                _riserStart = baseSample + lat;
                _riserEnd = _riserStart + (long)(TraxPhrase.Steps * stepDurSamples);
                _riserIdx = (baseSample * 11) % (_noise.Length - 4);
                _riserLp = 0;
                _riserOn = true;
            }

            // Accelerating snare roll through the back half of the last bar,
            // via the THUMPER path so a drumless section stays drumless.
            if (stepInSection >= secSteps - TraxPhrase.Steps && sec.active[ThumperModuleIdx])
            {
                double rv = SnareRoll[stepInSection % TraxPhrase.Steps];
                if (rv > 0 && _evtCount < MaxEvents)
                {
                    Evt e = new Evt();
                    e.at = baseSample + lat;
                    e.voice = TraxVoice.Snare;
                    e.st = new TraxStep { on = true, vel = rv };
                    _events[_evtCount++] = e;
                }
            }

            // step > 0: the very first downbeat of a play is a start, not an
            // arrival — no impact for it.
            if (stepInSection == 0 && step > 0)
            {
                _impactStart = baseSample + lat;
                _impactEnd = _impactStart + (long)(0.6 * _sr);
                _impactIdx = (baseSample * 5) % (_noise.Length - 4);
                _impactLp = 0;
                _impactEnv = 1; _impactThumpEnv = 1; _impactFreqEnv = 1; _impactPhase = 0;
                _impactOn = true;
            }
        }

        return _sr * 60.0 / sec.p.bpm / 4.0;
    }

    /// The shared event builder: schedule every audible voice's hit at this
    /// phrase step. `active == null` means loop mode (the volatile module
    /// flags decide); otherwise the SECTION's active set decides.
    void EmitStepEvents(TraxPhrase phrase, TraxParams p, bool[] active, int patStep, long baseSample)
    {
        double stepDur = 60.0 / p.bpm / 4.0;
        long lat = (long)(NudgeLatency * _sr);

        for (int v = 0; v < TraxPhrase.VoiceCount; v++)
        {
            TraxVoice voice = (TraxVoice)v;
            if (active == null ? !ModuleOn(voice) : !active[VoiceModuleIdx[v]]) continue;

            TraxStep st = phrase.At(voice, patStep);
            if (!st.on) continue;

            // MOSS is a chord, so it emits one event per triad note. Three
            // events rather than an array on the Evt keeps the render path
            // allocation-free.
            int notes = voice == TraxVoice.Moss ? TraxPhrase.ChordToneCount : 1;
            for (int n = 0; n < notes; n++)
            {
                if (_evtCount >= MaxEvents) return;

                Evt e = new Evt();
                e.at = baseSample + lat + (long)(st.nudge * _sr);
                e.voice = voice;
                e.st = st;
                if (TraxPhrase.IsMelodic(voice))
                {
                    int degree = voice == TraxVoice.Moss
                        ? TraxPhrase.ChordToneAt(st.degree, n)
                        : st.degree;
                    e.freq = TraxScales.VoiceFreq(degree, p.scaleIdx, voice, p.key);
                    double mult = voice == TraxVoice.Bass ? 0.95
                                : (voice == TraxVoice.Moss ? 0.98 : 0.9);
                    e.durSec = Math.Max(0.05, st.dur * stepDur * mult);
                }
                _events[_evtCount++] = e;
            }
        }
    }

    /// <summary>
    /// The riser + impact, rendered a sample at a time into the drum bus.
    ///
    /// RISER: noise through a rising crude band (one-pole highpass whose
    /// corner sweeps up), gain building LINEARLY over the bar — an exponential
    /// ramp from silence stays inaudible for most of its length — peaking just
    /// before the downbeat, then cutting.
    ///
    /// IMPACT: a darkening noise splash (one-pole lowpass whose corner falls)
    /// over a low sine thump, both decaying exponentially.
    /// </summary>
    double TransitionFxSample(long now)
    {
        double s = 0;

        if (_riserOn && now >= _riserStart)
        {
            if (now >= _riserEnd) _riserOn = false;
            else
            {
                double u = (now - _riserStart) / (double)(_riserEnd - _riserStart);
                double n = Noise(ref _riserIdx);
                double c = 0.03 + u * u * 0.7;                 // corner sweeps up
                _riserLp += c * (n - _riserLp);
                double band = n - _riserLp;
                const double peak = 0.32;                      // 0.4 * 0.8 fixed strength
                double gain = u < 0.95
                    ? 0.002 + (peak - 0.002) * (u / 0.95)
                    : peak * (1.0 - (u - 0.95) / 0.05);
                s += band * gain;
            }
        }

        if (_impactOn && now >= _impactStart)
        {
            if (now >= _impactEnd) _impactOn = false;
            else
            {
                double u = (now - _impactStart) / (double)(_impactEnd - _impactStart);
                double n = Noise(ref _impactIdx);
                double c = 0.55 - u * 0.5;                     // corner falls: bright -> dark
                _impactLp += c * (n - _impactLp);
                _impactEnv *= _impactEnvCoef;
                s += _impactLp * 0.48 * _impactEnv;            // 0.6 * 0.8

                _impactFreqEnv *= _impactFreqCoef;
                double f = 36.0 + 74.0 * _impactFreqEnv;       // 110Hz -> 36Hz
                _impactPhase += f / _sr;
                if (_impactPhase >= 1) _impactPhase -= 1;
                _impactThumpEnv *= _impactThumpCoef;
                s += Math.Sin(_impactPhase * 2.0 * Math.PI) * 0.68 * _impactThumpEnv;   // 0.85 * 0.8
            }
        }

        return s;
    }

    bool ModuleOn(TraxVoice v)
    {
        switch (v)
        {
            case TraxVoice.Kick:
            case TraxVoice.Snare:
            case TraxVoice.Hat:
                return _onThumper;
            case TraxVoice.Bass: return _onGloworm;
            case TraxVoice.Lead: return _onSiren;
            case TraxVoice.Moss: return _onMoss;
            case TraxVoice.Spindle: return _onSpindle;
        }
        return true;
    }

    // ── voices ───────────────────────────────────────────────────────────

    int FindSlot()
    {
        for (int i = 0; i < _vox.Length; i++) if (!_vox[i].active) return i;
        // All busy — steal the one closest to finishing rather than dropping
        // the new note, so a dense pattern thins out instead of stuttering.
        int best = 0;
        long bestEnd = long.MaxValue;
        for (int i = 0; i < _vox.Length; i++)
            if (_vox[i].endAt < bestEnd) { bestEnd = _vox[i].endAt; best = i; }
        return best;
    }

    static double DecayCoefFor(double tauSeconds, int sr)
    {
        if (tauSeconds <= 0) return 0;
        return Math.Exp(-1.0 / (tauSeconds * sr));
    }

    void StartVoice(ref Evt e, long now, TraxParams p)
    {
        int slot = FindSlot();
        ref Vox v = ref _vox[slot];

        v = default(Vox);
        v.active = true;
        v.kind = e.voice;
        v.env = 0;
        v.amp = e.st.vel;
        v.ic1 = 0; v.ic2 = 0;

        double atk, len;

        switch (e.voice)
        {
            case TraxVoice.Kick:
                atk = 0.004; len = 0.25;
                // 150 -> 45Hz. The drop IS the kick.
                v.freqFloor = 45.0;
                v.freqSpan = 105.0;
                v.freqEnv = 1.0;
                v.freqCoef = DecayCoefFor(0.018, _sr);
                break;

            case TraxVoice.Snare:
                atk = 0.002; len = 0.18;
                v.noiseIdx = (now * 7) % (_noise.Length - 4);
                v.bodyPhase = 0;
                v.bodyInc = 190.0 / _sr;
                v.bodyEnv = 1.0;
                v.bodyCoef = DecayCoefFor(0.030, _sr);
                break;

            case TraxVoice.Hat:
                v.open = e.st.open;
                atk = 0.001; len = v.open ? 0.14 : 0.04;
                v.noiseIdx = (now * 13) % (_noise.Length - 4);
                break;

            case TraxVoice.Bass:
            case TraxVoice.Lead:
            case TraxVoice.Moss:
            case TraxVoice.Spindle:
                {
                    if (e.voice == TraxVoice.Moss)
                    {
                        // Swells in under everything rather than announcing itself.
                        atk = 0.12;
                        len = e.durSec;
                    }
                    else if (e.voice == TraxVoice.Spindle)
                    {
                        // Plucked and short, or the arp turns into a drone.
                        atk = 0.004;
                        len = Math.Min(e.durSec, 0.22);
                    }
                    else
                    {
                        atk = 0.012;
                        len = e.durSec;
                    }

                    // sine -> saw -> square, crossfaded. Matches morphPair() in
                    // the browser's voices.js.
                    double morph = p.oscMorph;
                    v.morphB = morph < 0.5 ? morph * 2.0 : (morph - 0.5) * 2.0;
                    v.morphA = 1.0 - v.morphB;

                    double det = p.detuneCents * 0.5;
                    v.inc  = e.freq * Math.Pow(2.0, -det / 1200.0) / _sr;
                    v.inc2 = e.freq * Math.Pow(2.0,  det / 1200.0) / _sr;
                    double level;
                    switch (e.voice)
                    {
                        case TraxVoice.Bass:    level = 0.85; break;
                        case TraxVoice.Moss:    level = 0.22; break;   // three notes at once
                        case TraxVoice.Spindle: level = 0.34; break;
                        default:                level = 0.5;  break;
                    }
                    v.amp = e.st.vel * level * ResComp(p.filterQ);
                    break;
                }

            default:
                atk = 0.005; len = 0.1;
                break;
        }

        // Browser ampEnv() parity: attack (capped at half the note), HOLD at
        // peak until ~70% of the note, then exponential release to silence at
        // its end. The old tau-decay-from-the-attack shape thinned every note.
        double a = Math.Min(atk, len * 0.5);
        v.attackLeft = Math.Max(1, (int)(a * _sr));
        v.attackStep = 1.0 / v.attackLeft;
        long holdSamples = (long)(Math.Max(a, len * 0.7) * _sr);
        v.holdUntil = now + holdSamples;
        double relSamples = len * _sr - holdSamples;
        v.decayCoef = relSamples > 1 ? Math.Exp(Math.Log(1e-4) / relSamples) : 0.0;
        v.endAt = now + (long)(len * _sr);
        // Last 6ms is a linear fade so a hard stop can never click.
        v.fadeFrom = v.endAt - Math.Max(1, (long)(0.006 * _sr));
    }

    /// High resonance is a big gain peak — back the voice off so GOO doesn't
    /// just mean "louder" and duck the whole mix on every squelch.
    static double ResComp(double q)
    {
        return 1.0 / (1.0 + q * 0.06);
    }

    double RenderVoice(ref Vox v, long now, TraxParams p)
    {
        if (now >= v.endAt) { v.active = false; return 0; }

        // envelope: attack, hold at peak, then release (ampEnv parity)
        if (v.attackLeft > 0) { v.env += v.attackStep; v.attackLeft--; if (v.env > 1) v.env = 1; }
        else if (now >= v.holdUntil) v.env *= v.decayCoef;

        double env = v.env;
        if (now >= v.fadeFrom)
        {
            double f = (double)(v.endAt - now) / (v.endAt - v.fadeFrom);
            env *= f < 0 ? 0 : f;
        }

        double s;

        switch (v.kind)
        {
            case TraxVoice.Kick:
                {
                    v.freqEnv *= v.freqCoef;
                    double f = v.freqFloor + v.freqSpan * v.freqEnv;
                    v.phase += f / _sr;
                    if (v.phase >= 1) v.phase -= 1;
                    s = Math.Sin(v.phase * 2.0 * Math.PI);
                    break;
                }

            case TraxVoice.Snare:
                {
                    double n = Noise(ref v.noiseIdx);
                    // Real RBJ bandpass (1800Hz, Q 0.9) — what the browser's
                    // BiquadFilterNode is. The one-pole stand-in was woolly.
                    s = Biquad(ref v, n, _snB0, _snB1, _snB2, _snA1, _snA2) * 0.8;

                    v.bodyEnv *= v.bodyCoef;
                    v.bodyPhase += (120.0 + 70.0 * v.bodyEnv) / _sr;
                    if (v.bodyPhase >= 1) v.bodyPhase -= 1;
                    double tri = 4.0 * Math.Abs(v.bodyPhase - 0.5) - 1.0;
                    s += tri * v.bodyEnv * 0.4;
                    break;
                }

            case TraxVoice.Hat:
                {
                    double n = Noise(ref v.noiseIdx);
                    // Real RBJ highpass at 7kHz, matching the browser's node.
                    s = Biquad(ref v, n, _htB0, _htB1, _htB2, _htA1, _htA2) * 0.7;
                    break;
                }

            case TraxVoice.Bass:
            case TraxVoice.Lead:
            case TraxVoice.Moss:
            case TraxVoice.Spindle:
                {
                    double a = Osc(ref v.phase, v.inc, p.oscMorph, false);
                    double b = Osc(ref v.phase2, v.inc2, p.oscMorph, true);
                    double raw = a * v.morphA + b * v.morphB;

                    int ki = (int)v.kind;
                    double a1 = _ca1[ki], a2 = _ca2[ki], a3 = _ca3[ki];

                    // TPT state-variable filter, lowpass tap.
                    double v3 = raw - v.ic2;
                    double v1 = a1 * v.ic1 + a2 * v3;
                    double v2 = v.ic2 + a2 * v.ic1 + a3 * v3;
                    v.ic1 = 2.0 * v1 - v.ic1;
                    v.ic2 = 2.0 * v2 - v.ic2;
                    s = v2;
                    break;
                }

            default:
                s = 0;
                break;
        }

        return s * env * v.amp;
    }

    double Noise(ref double idx)
    {
        int i = (int)idx;
        if (i < 0 || i >= _noise.Length) { i = 0; idx = 0; }
        double val = _noise[i];
        idx += 1;
        if (idx >= _noise.Length) idx = 0;
        return val;
    }

    // ── oscillators ──────────────────────────────────────────────────────

    /// <summary>
    /// PolyBLEP-corrected saw/square. Without the correction, a saw pitched up
    /// into the lead's register aliases badly and the whole instrument sounds
    /// cheap — Web Audio's oscillators are band-limited, so matching them means
    /// band-limiting these too.
    /// </summary>
    static double Osc(ref double phase, double inc, double morph, bool second)
    {
        phase += inc;
        if (phase >= 1.0) phase -= 1.0;

        // Which pair this oscillator belongs to: below 0.5 we crossfade
        // sine→saw, above it saw→square. `second` picks the upper member.
        bool square = morph >= 0.5 && second;
        bool saw = (morph < 0.5 && second) || (morph >= 0.5 && !second);

        if (square)
        {
            double s = phase < 0.5 ? 1.0 : -1.0;
            s += PolyBlep(phase, inc);
            double p2 = phase + 0.5;
            if (p2 >= 1.0) p2 -= 1.0;
            s -= PolyBlep(p2, inc);
            return s * 0.7;
        }
        if (saw)
        {
            double s = 2.0 * phase - 1.0;
            s -= PolyBlep(phase, inc);
            return s * 0.7;
        }
        return Math.Sin(phase * 2.0 * Math.PI);
    }

    static double PolyBlep(double t, double dt)
    {
        if (dt <= 0) return 0;
        if (t < dt) { t /= dt; return t + t - t * t - 1.0; }
        if (t > 1.0 - dt) { t = (t - 1.0) / dt; return t * t + t + t + 1.0; }
        return 0.0;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// RBJ cookbook bandpass, constant 0dB peak — what a BiquadFilterNode
    /// "bandpass" is.
    void BiquadBandpass(double fc, double q, out double b0, out double b1, out double b2,
                        out double a1, out double a2)
    {
        double w0 = 2.0 * Math.PI * fc / _sr;
        double alpha = Math.Sin(w0) / (2.0 * q);
        double a0 = 1.0 + alpha;
        b0 = alpha / a0;
        b1 = 0.0;
        b2 = -alpha / a0;
        a1 = -2.0 * Math.Cos(w0) / a0;
        a2 = (1.0 - alpha) / a0;
    }

    void BiquadHighpass(double fc, double q, out double b0, out double b1, out double b2,
                        out double a1, out double a2)
    {
        double w0 = 2.0 * Math.PI * fc / _sr;
        double cosw = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * q);
        double a0 = 1.0 + alpha;
        b0 = (1.0 + cosw) / 2.0 / a0;
        b1 = -(1.0 + cosw) / a0;
        b2 = (1.0 + cosw) / 2.0 / a0;
        a1 = -2.0 * cosw / a0;
        a2 = (1.0 - alpha) / a0;
    }

    static double Biquad(ref Vox v, double x,
                         double b0, double b1, double b2, double a1, double a2)
    {
        double y = b0 * x + b1 * v.bqX1 + b2 * v.bqX2 - a1 * v.bqY1 - a2 * v.bqY2;
        v.bqX2 = v.bqX1; v.bqX1 = x;
        v.bqY2 = v.bqY1; v.bqY1 = y;
        return y;
    }

    void SvfCoef(double cutoffHz, double k, out double a1, out double a2, out double a3)
    {
        double nyq = _sr * 0.45;
        if (cutoffHz < 20) cutoffHz = 20;
        if (cutoffHz > nyq) cutoffHz = nyq;
        double g = Math.Tan(Math.PI * cutoffHz / _sr);
        a1 = 1.0 / (1.0 + g * (g + k));
        a2 = g * a1;
        a3 = g * a2;
    }

    /// Padé approximation of tanh — the real one is too slow to run per sample
    /// on two buses, and the difference is inaudible as a saturation curve.
    static double FastTanh(double x)
    {
        if (x < -3.0) return -1.0;
        if (x > 3.0) return 1.0;
        double x2 = x * x;
        return x * (27.0 + x2) / (27.0 + 9.0 * x2);
    }

    static double Shape(double x, double drive, double norm, int levels)
    {
        double y = FastTanh(drive * x) / norm;
        if (levels < 64 && levels > 0) y = Math.Floor(y * levels + 0.5) / levels;
        return y;
    }

    static double SoftClip(double x)
    {
        if (x > 1.2) x = 1.2;
        else if (x < -1.2) x = -1.2;
        return FastTanh(x);
    }
}
