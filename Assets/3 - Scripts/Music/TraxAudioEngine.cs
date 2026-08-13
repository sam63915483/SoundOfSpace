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

    volatile bool _playing;
    volatile float _masterVolume = 0.5f;
    volatile float _busLevel = 1f;
    volatile bool _onThumper = true, _onGloworm = true, _onSiren = true, _onCave = true;
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
        public double decayCoef;                  // per-sample multiply after attack
        public int attackLeft;
        public double amp;
        public long endAt;                        // hard stop sample
        public long fadeFrom;                     // start of the anti-click fade
        public double ic1, ic2;                   // TPT state-variable filter memory
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

    float[] _noise;
    float[] _delayA, _delayB;
    int _writeA, _writeB, _lenA, _lenB;
    double _dampA, _dampB, _dampCoef;

    int _sr = 48000;
    long _sample;
    int _step;
    double _nextStepSample;
    double _lfoPhase;

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

        _lenA = Mathf.CeilToInt(0.19f * _sr);
        _lenB = Mathf.CeilToInt(0.31f * _sr);
        _delayA = new float[_lenA];
        _delayB = new float[_lenB];
        // ~2.6kHz one-pole damping in the feedback path.
        _dampCoef = 1.0 - Math.Exp(-2.0 * Math.PI * 2600.0 / _sr);

        _src = GetComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = true;
        _src.spatialBlend = 0f;          // 2D — the UI is fullscreen, not in the world
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
        _sample = 0;
        _step = 0;
        _nextStepSample = 0;
        _evtCount = 0;
        for (int i = 0; i < _vox.Length; i++) _vox[i].active = false;
        Array.Clear(_delayA, 0, _delayA.Length);
        Array.Clear(_delayB, 0, _delayB.Length);
        _uiStep = -1;
        _playing = true;
    }

    public void StopTransport()
    {
        _playing = false;
        _uiStep = -1;
    }

    public void SetMasterVolume(float v) { _masterVolume = Mathf.Clamp01(v); }

    public void SetModuleEnabled(string module, bool on)
    {
        switch (module)
        {
            case "THUMPER": _onThumper = on; break;
            case "GLOWORM": _onGloworm = on; break;
            case "SIREN":   _onSiren = on; break;
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

        TraxParams p = snap.p;
        double samplesPerStep = _sr * 60.0 / p.bpm / 4.0;
        if (samplesPerStep < 1) samplesPerStep = 1;

        // ── per-buffer coefficients ──
        // The LFO advances once per buffer rather than per sample. At <= 3Hz and
        // a 5-20ms buffer that is far finer than the ear can hear, and it keeps
        // tan() out of the inner loop.
        _lfoPhase += (p.lfoRate * frames) / _sr;
        if (_lfoPhase > 1.0) _lfoPhase -= Math.Floor(_lfoPhase);
        double lfo = Math.Sin(_lfoPhase * 2.0 * Math.PI);
        double lfoMul = Math.Pow(2.0, lfo * p.lfoDepthOct);

        double bassA1, bassA2, bassA3, leadA1, leadA2, leadA3;
        double k = 1.0 / Math.Max(0.5, p.filterQ);
        SvfCoef(p.filterBase * 1.0 * lfoMul, k, out bassA1, out bassA2, out bassA3);
        SvfCoef(p.filterBase * 2.2 * lfoMul, k, out leadA1, out leadA2, out leadA3);

        double driveTone = 1.0 + p.drive * 30.0;
        double driveDrum = 1.0 + p.drive * 0.5 * 30.0;
        double normTone = FastTanh(driveTone);
        double normDrum = FastTanh(driveDrum);
        int levelsTone = p.crushLevels;
        int levelsDrum = Math.Min(64, p.crushLevels * 2);

        double sendTone = p.caveSend;
        double sendDrum = p.caveSend * 0.3;
        double fb = Math.Min(0.85, p.caveFeedback);
        double wetMix = _onCave ? p.caveMix : 0.0;
        double outGain = _masterVolume * _busLevel;

        // ── schedule any steps that begin inside this buffer ──
        long bufEnd = _sample + frames;
        while (_nextStepSample < bufEnd)
        {
            EvaluateStep(snap, _step, (long)_nextStepSample, samplesPerStep);
            _step++;
            _nextStepSample += samplesPerStep;
        }

        // ── render ──
        for (int i = 0; i < frames; i++)
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
                double s = RenderVoice(ref _vox[v], now, p,
                                       bassA1, bassA2, bassA3, leadA1, leadA2, leadA3);
                if (_vox[v].kind == TraxVoice.Bass || _vox[v].kind == TraxVoice.Lead) tone += s;
                else drum += s;
            }

            // Drive + amplitude quantization (CRUNCH). Quantizing the shaped
            // signal IS bit-crushing; no worklet or extra pass needed.
            tone = Shape(tone, driveTone, normTone, levelsTone);
            drum = Shape(drum, driveDrum, normDrum, levelsDrum);

            // CAVE: two cross-fed delay lines with damping, read before write.
            double outA = _delayA[_writeA];
            double outB = _delayB[_writeB];
            _dampA += _dampCoef * (outA - _dampA);
            _dampB += _dampCoef * (outB - _dampB);
            double send = tone * sendTone + drum * sendDrum;
            _delayA[_writeA] = (float)(send + _dampB * fb);
            _delayB[_writeB] = (float)(send + _dampA * fb);
            if (++_writeA >= _lenA) _writeA = 0;
            if (++_writeB >= _lenB) _writeB = 0;

            double dry = tone + drum;
            // Ping-pong the two taps for a little width.
            double l = dry + outA * wetMix;
            double r = dry + outB * wetMix;

            l = SoftClip(l * outGain);
            r = SoftClip(r * outGain);

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

        TraxPhrase phrase = snap.phrase;
        TraxParams p = snap.p;
        double stepDur = 60.0 / p.bpm / 4.0;
        long lat = (long)(NudgeLatency * _sr);

        for (int v = 0; v < TraxPhrase.VoiceCount; v++)
        {
            TraxVoice voice = (TraxVoice)v;
            if (!ModuleOn(voice)) continue;

            TraxStep st = phrase.At(voice, step);
            if (!st.on) continue;

            if (_evtCount >= MaxEvents) return;

            Evt e = new Evt();
            e.at = baseSample + lat + (long)(st.nudge * _sr);
            e.voice = voice;
            e.st = st;
            if (voice == TraxVoice.Bass || voice == TraxVoice.Lead)
            {
                e.freq = TraxScales.DegreeToFreq(st.degree, p.scaleIdx, TraxScales.OctaveFor(voice));
                double mult = voice == TraxVoice.Bass ? 0.95 : 0.9;
                e.durSec = Math.Max(0.05, st.dur * stepDur * mult);
            }
            _events[_evtCount++] = e;
        }
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

        double atk, tau, len;

        switch (e.voice)
        {
            case TraxVoice.Kick:
                atk = 0.004; tau = 0.060; len = 0.30;
                // 150 -> 45Hz. The drop IS the kick.
                v.freqFloor = 45.0;
                v.freqSpan = 105.0;
                v.freqEnv = 1.0;
                v.freqCoef = DecayCoefFor(0.018, _sr);
                break;

            case TraxVoice.Snare:
                atk = 0.002; tau = 0.045; len = 0.18;
                v.noiseIdx = (now * 7) % (_noise.Length - 4);
                v.bodyPhase = 0;
                v.bodyInc = 190.0 / _sr;
                v.bodyEnv = 1.0;
                v.bodyCoef = DecayCoefFor(0.030, _sr);
                break;

            case TraxVoice.Hat:
                v.open = e.st.open;
                atk = 0.001; tau = v.open ? 0.045 : 0.012; len = v.open ? 0.14 : 0.04;
                v.noiseIdx = (now * 13) % (_noise.Length - 4);
                break;

            case TraxVoice.Bass:
            case TraxVoice.Lead:
                {
                    atk = 0.012;
                    len = e.durSec;
                    tau = Math.Max(0.05, len * (e.voice == TraxVoice.Bass ? 0.5 : 0.6));

                    // sine -> saw -> square, crossfaded. Matches morphPair() in
                    // the browser's voices.js.
                    double morph = p.oscMorph;
                    v.morphB = morph < 0.5 ? morph * 2.0 : (morph - 0.5) * 2.0;
                    v.morphA = 1.0 - v.morphB;

                    double det = p.detuneCents * 0.5;
                    v.inc  = e.freq * Math.Pow(2.0, -det / 1200.0) / _sr;
                    v.inc2 = e.freq * Math.Pow(2.0,  det / 1200.0) / _sr;
                    v.amp = e.st.vel * (e.voice == TraxVoice.Bass ? 0.85 : 0.5) * ResComp(p.filterQ);
                    break;
                }

            default:
                atk = 0.005; tau = 0.1; len = 0.1;
                break;
        }

        v.attackLeft = Math.Max(1, (int)(atk * _sr));
        v.attackStep = 1.0 / v.attackLeft;
        v.decayCoef = DecayCoefFor(tau, _sr);
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

    double RenderVoice(ref Vox v, long now, TraxParams p,
                       double bA1, double bA2, double bA3,
                       double lA1, double lA2, double lA3)
    {
        if (now >= v.endAt) { v.active = false; return 0; }

        // envelope
        if (v.attackLeft > 0) { v.env += v.attackStep; v.attackLeft--; if (v.env > 1) v.env = 1; }
        else v.env *= v.decayCoef;

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
                    // Cheap bandpass: highpass the noise by subtracting a
                    // one-pole lowpass, then lowpass the result.
                    v.ic1 += 0.35 * (n - v.ic1);           // ~lowpass
                    double hp = n - v.ic1;
                    v.ic2 += 0.45 * (hp - v.ic2);          // band
                    s = v.ic2 * 0.8;

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
                    v.ic1 += 0.75 * (n - v.ic1);
                    s = (n - v.ic1) * 0.7;                 // highpass ≈ 7kHz
                    break;
                }

            case TraxVoice.Bass:
            case TraxVoice.Lead:
                {
                    double a = Osc(ref v.phase, v.inc, p.oscMorph, false);
                    double b = Osc(ref v.phase2, v.inc2, p.oscMorph, true);
                    double raw = a * v.morphA + b * v.morphB;

                    bool bass = v.kind == TraxVoice.Bass;
                    double a1 = bass ? bA1 : lA1;
                    double a2 = bass ? bA2 : lA2;
                    double a3 = bass ? bA3 : lA3;

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
