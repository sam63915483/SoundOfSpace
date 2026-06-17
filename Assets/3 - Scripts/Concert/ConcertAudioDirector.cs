using System;
using UnityEngine;

// Central audio analyzer for concert lights/effects.
//
// Auto-spawns at runtime (no need to drop it in the scene) — mirrors PlayerWallet's
// RuntimeInitializeOnLoadMethod pattern (CLAUDE.md "Currency, fish & market").
//
// Subscribers (lasers, future spotlights/strobes/screens) read smoothed band
// energies (Bass/Mid/Treble/Overall) and BeatPhase per frame, or subscribe to
// the OnBeat event. The contract is the public API on this class — adding a new
// light type is just `_director = ConcertAudioDirector.Instance` plus reads.
public class ConcertAudioDirector : MonoBehaviour
{
    public static ConcertAudioDirector Instance { get; private set; }

    [Header("Analysis")]
    [Tooltip("FFT bin count. 512 → ~47 Hz/bin at 24 kHz Nyquist; good for music.")]
    [Range(64, 4096)] public int spectrumSize = 512;
    public FFTWindow fftWindow = FFTWindow.BlackmanHarris;
    [Tooltip("Higher = smoother (more lag); lower = snappier (more jitter).")]
    [Range(0f, 1f)] public float bandSmoothing = 0.6f;

    [Header("Beat Detection")]
    [Tooltip("Multiplier on rolling-average bass energy. Bass spike above avg×this = beat.")]
    [Range(1.05f, 4f)] public float beatThresholdMultiplier = 1.25f;
    [Tooltip("Minimum time between beats — prevents double-firing on a single kick.")]
    public float beatDebounceSeconds = 0.12f;
    [Tooltip("Time for BeatPhase to decay from 1 back to 0 after a beat.")]
    public float beatPhaseDecaySeconds = 0.35f;
    [Tooltip("Rolling window for the bass-energy average. ~1s = good for most music.")]
    public float beatHistorySeconds = 1.0f;

    [Header("Kick Envelope (transient follower)")]
    [Tooltip("Fast attack on bass increases, slow release. Drives a smooth, always-on 'thump' value that works even for ambient music with no clear beats.")]
    public float kickAttackSeconds = 0.01f;
    public float kickReleaseSeconds = 0.18f;

    [Header("Drop Detection")]
    [Tooltip("Detect 'drops' (sustained energy buildup followed by spike) and fire OnDrop. Subscribers (light program, blinders) react with bigger effects.")]
    public bool detectDrops = true;
    [Tooltip("Short-window average must exceed long-window average by this multiplier to count as a drop.")]
    public float dropEnergyMultiplier = 1.4f;
    [Tooltip("Minimum seconds between drops — prevents back-to-back triggers.")]
    public float dropDebounceSeconds = 6f;
    public float dropShortWindowSec = 1f;
    public float dropLongWindowSec = 4f;

    [Header("Drum Detection (adaptive z-score)")]
    [Tooltip("Detect kick/snare/hihat/crash via spectral-flux z-score (mean + k×stddev). Self-tunes to any song loudness — no manual threshold tuning needed.")]
    public bool detectDrums = true;
    [Tooltip("Standard deviations above the running flux mean to count as an onset. 2.5–3.5 typical; higher = pickier, fewer false positives.")]
    [Range(1.5f, 5f)] public float kickZScore  = 3.0f;
    [Range(1.5f, 5f)] public float snareZScore = 2.8f;
    [Range(1.5f, 5f)] public float hihatZScore = 2.8f;
    [Tooltip("Onset must also exceed this fraction of the song's overall RMS energy. Adaptive noise floor — quiet songs and loud songs both work.")]
    [Range(0f, 0.3f)] public float relativeNoiseFloor = 0.04f;
    [Tooltip("Hi-hat band sustain duration (seconds) for crash detection.")]
    public float crashSustainSeconds = 0.2f;
    public float kickDebounceSeconds = 0.10f;
    public float snareDebounceSeconds = 0.08f;
    public float hihatDebounceSeconds = 0.035f;
    public float crashDebounceSeconds = 0.6f;

    [Header("BPM Auto-Detection")]
    [Tooltip("Auto-detect the song's tempo from kick intervals. Updates DetectedBpm continuously.")]
    public bool autoDetectBpm = true;
    [Tooltip("BPM range to constrain detected tempo. Detected values outside this range are octave-corrected (doubled or halved) until they fit.")]
    [Range(40f, 100f)] public float bpmMinExpected = 70f;
    [Range(120f, 220f)] public float bpmMaxExpected = 180f;

    [Header("Manual BPM Override")]
    [Tooltip("Skip auto-detection and tick beats at fixed manualBpm intervals.")]
    public bool useManualBpm = false;
    [Range(40f, 220f)] public float manualBpm = 120f;

    [Header("Debug (read-only)")]
    [SerializeField, Tooltip("Live debug — these are written every frame and shown for tuning.")]
    float debug_bass, debug_mid, debug_treble, debug_overall, debug_kick, debug_beatPhase, debug_detectedBpm;
    [SerializeField] int debug_beatCount;
    [SerializeField] bool debug_isPlaying;
    [SerializeField] float debug_kickFlux, debug_snareFlux, debug_hihatFlux;
    [SerializeField] int debug_kickCount, debug_snareCount, debug_hihatCount, debug_crashCount;
    [SerializeField] float debug_hihatRate;
    [SerializeField] float debug_predictedBeatPhase;
    [SerializeField] int debug_beatInBar, debug_barCount;
    [SerializeField] float debug_kickFluxMean, debug_kickFluxStd;

    // ─── Public API (the contract every light depends on) ────────────────────
    public float Bass    { get; private set; }
    public float Mid     { get; private set; }
    public float Treble  { get; private set; }
    public float Overall { get; private set; }
    public float Kick    { get; private set; } // 0..1 envelope follower on bass — punchy
    public float BeatPhase { get; private set; }
    public int   BeatCount { get; private set; }
    public float DetectedBpm { get; private set; }
    public bool  IsPlaying { get; private set; }
    public float BuildupEnergy { get; private set; } // shortAvg / longAvg, ~1 normal, >1.4 = building/drop imminent
    public float EnergyShortAvg => _energyShortAvg;  // ~1s window, used for tier selection
    public float EnergyLongAvg  => _energyLongAvg;   // ~4s window, slower-changing baseline
    // ~60s exponential moving average — used by the energy curve as a song-average reference.
    public float EnergySongAvg { get; private set; } = 0.10f;
    // 0..1 normalized "is this section above or below the song's typical energy?"
    // Self-normalizing across loud and quiet songs because it's a ratio against the song avg.
    // Drives the program's global intensity multiplier: quiet sections fade, climax brightens.
    public float EnergyCurve   { get; private set; }
    // True when audio has been near-silent for at least 0.5 s. Lights freeze and dim
    // during silence; release on OnSilenceEnd is cathartic.
    public bool  IsAudioSilent { get; private set; }

    // Per-drum spectral flux (smoothed 0..1) — useful for continuous reactions.
    public float KickFlux  { get; private set; }
    public float SnareFlux { get; private set; }
    public float HihatFlux { get; private set; }

    // Cumulative drum hit counts. Effects use the count delta to detect "did
    // a drum fire since I last looked" without needing to subscribe.
    public int KickCount   { get; private set; }
    public int SnareCount  { get; private set; }
    public int HihatCount  { get; private set; }
    public int CrashCount  { get; private set; }

    // Last-fire timestamps. Effects use these for decay envelopes.
    public float LastKickTime  { get; private set; } = -999f;
    public float LastSnareTime { get; private set; } = -999f;
    public float LastHihatTime { get; private set; } = -999f;
    public float LastCrashTime { get; private set; } = -999f;

    // Hi-hat density: events/second over a 5-second rolling window. Used by the
    // light program to bias tier selection toward energetic tiers when the hat
    // pattern is busy even if bass is moderate.
    public float HihatRate { get; private set; }
    // Snare density (events/sec, 5s window). Used by buildup detector — rising
    // snare rate is the canonical "drum roll into a drop" signal.
    public float SnareRate { get; private set; }

    // ─── Buildup detector ──────────────────────────────────────────────────
    // Anticipatory state: the music is *building* toward a drop (rising snare
    // rolls + rising treble + rising energy ratio). Fires OnBuildupStart on
    // entry, OnBuildupEnd on exit (or on confidence collapse).
    public float BuildupConfidence { get; private set; }   // 0..1 smoothed
    public bool  IsInBuildup       { get; private set; }

    // ─── Predictive timing API ────────────────────────────────────────────────
    // PredictedBeatPhase ramps from 0 (just hit a kick) to 1 (next kick about to land)
    // based on detected BPM. Lights use this for ANTICIPATORY motion — start moving
    // toward a snap pose at phase 0.85, hit it at phase 0.0.
    public float PredictedBeatPhase
    {
        get
        {
            if (DetectedBpm < 30f || LastKickTime < 0f) return 0f;
            float period = 60f / DetectedBpm;
            return Mathf.Repeat((Time.time - LastKickTime) / period, 1f);
        }
    }
    public float PredictedNextBeatTime =>
        DetectedBpm < 30f ? Time.time : LastKickTime + (60f / DetectedBpm);

    // Bar tracking — assume 4/4. BeatInBar cycles 0→1→2→3 on each kick;
    // BarCount increments when it wraps. Useful for "switch on downbeat only" logic.
    public int BeatInBar { get; private set; }
    public int BarCount  { get; private set; }

    // ─── Beat-lock helpers ────────────────────────────────────────────────────
    // BarPhase: 0..1 phase ramping over a 4/4 bar (combines BeatInBar with
    // PredictedBeatPhase). Use this to phase-lock motion to the bar so sweeps
    // peak ON beats — the difference between "lights playing music" and
    // "lights timing-locked to music."
    public float BarPhase
    {
        get
        {
            if (DetectedBpm < 30f || LastKickTime < 0f) return 0f;
            float beatF = BeatInBar + PredictedBeatPhase;     // 0..4 over the bar
            return Mathf.Repeat(beatF * 0.25f, 1f);
        }
    }
    // 0..1 phase ramping over N bars. Use for slow looks (4/8/16-bar cycles).
    public float MultiBarPhase(int bars) =>
        Mathf.Repeat((BarCount + BarPhase) / Mathf.Max(1, bars), 1f);
    // Sin locked to the bar — peaks at BarPhase = 0.25 / 0.75 with cyclesPerBar=1.
    // Use Cos variant to peak at BarPhase = 0 / 0.5 (downbeat / mid-bar).
    public float RhythmSin(float cyclesPerBar) =>
        Mathf.Sin(BarPhase * cyclesPerBar * Mathf.PI * 2f);
    public float RhythmCos(float cyclesPerBar) =>
        Mathf.Cos(BarPhase * cyclesPerBar * Mathf.PI * 2f);

    public event Action OnBeat;
    public event Action OnDrop;
    public event Action OnKick;
    public event Action OnSnare;
    public event Action OnHihat;
    public event Action OnCrash;
    // Buildup detector: fires when BuildupConfidence enters/exits the active range.
    public event Action OnBuildupStart;
    public event Action OnBuildupEnd;
    // Sting: 2+ of {kick, snare, crash} co-occur within ~80 ms. Fired after the
    // triggering drum onset; debounced to ~1.5 s so drum fills don't over-fire.
    public event Action OnSting;
    // Audio silence (Overall < 0.04 sustained 0.5 s). Lights freeze during silence;
    // OnSilenceEnd fires a cathartic release flash when audio resumes.
    public event Action OnSilenceStart;
    public event Action OnSilenceEnd;

    static SpeakerSource s_pendingSource; // registered before Instance existed
    public static void RegisterSource(SpeakerSource s)
    {
        if (Instance != null) Instance._source = s;
        else s_pendingSource = s;
    }

    SpeakerSource _source;
    float[] _spectrum;
    float[] _prevSpectrum;
    float[] _waveform;
    // Per-band rolling mean and variance for z-score adaptive thresholds.
    // Mean tracks the typical flux level; variance is its spread. Onsets fire
    // when current flux exceeds mean + k×stddev — works at any loudness.
    float _kickFluxMean, _kickFluxVar;
    float _snareFluxMean, _snareFluxVar;
    float _hihatFluxMean, _hihatFluxVar;
    // Hi-hat band sustained-energy tracking for crash detection.
    float _hihatBandSustainStartTime = -1f;
    // Hi-hat-rate tracking: ring buffer of recent hi-hat event timestamps.
    const int kHihatRateRingSize = 64;
    float[] _hihatTimes = new float[kHihatRateRingSize];
    int _hihatRingHead;
    // Snare-rate tracking — same shape as hi-hat. Used by the buildup detector
    // (rising snare rate = drum-roll-into-drop pattern).
    const int kSnareRateRingSize = 32;
    float[] _snareTimes = new float[kSnareRateRingSize];
    int _snareRingHead;
    // Sting (drum co-occurrence) detection state.
    const float kStingWindow   = 0.080f;  // 80 ms window
    const float kStingDebounce = 1.5f;    // min seconds between stings
    float _lastStingTime = -999f;
    // Buildup detector state — Schmitt-trigger hysteresis on BuildupConfidence
    // with a hard cap so a missed drop resets us to Normal.
    const float kBuildupEnterThreshold = 0.55f;
    const float kBuildupExitThreshold  = 0.30f;
    const float kBuildupMaxSeconds     = 4f;
    float _buildupStartTime = -999f;
    // Silence detector state — Schmitt-trigger hysteresis on Overall with
    // 0.5 s confirmation before triggering. Prevents momentary quiet blips
    // (between drum hits in a sparse track) from registering as silence.
    //
    // Thresholds lowered for builds: GetOutputData amplitude in standalone
    // is consistently smaller than the editor's at the same listener distance
    // (different DSP / spatial pipeline). With the previous 0.04 enter / 0.10
    // exit, builds got stuck in Silence the whole concert because Overall
    // hovered around 0.04-0.07 and never reached 0.10 to escape — every
    // light ran at 5% intensity (Silence baseMul=0.10 × EnergyCurve=0.5),
    // looked sparse and grayish-white. New thresholds: very low silence
    // entry (only real audio dropout triggers) and an exit threshold below
    // typical build amplitude so the system stays in Normal during play.
    const float kSilenceEnterThreshold = 0.005f;
    const float kSilenceEnterDuration  = 1.0f;
    const float kSilenceExitThreshold  = 0.015f;
    float _silenceCandidateStart = -999f;
    // BPM auto-detection: ring buffer of recent kick timestamps. Median of
    // intervals → beat period → BPM (with octave correction).
    const int kKickRingSize = 16;
    float[] _kickTimes = new float[kKickRingSize];
    int _kickRingHead, _kickRingCount;
    // Rolling history of bass energy for beat detection.
    float[] _bassHistory;
    int _bassHistoryHead;
    float _bassHistorySum;
    int _bassHistoryCount;

    float _lastBeatTime = -999f;
    float _lastBeatPhaseSetTime = -999f;
    float _manualBpmTimer;
    // Drop-detection rolling averages.
    float _energyShortAvg, _energyLongAvg;
    float _lastDropTime = -999f;
    // For DetectedBpm smoothing: keep last few inter-beat intervals.
    const int kIntervalSamples = 6;
    float[] _intervals = new float[kIntervalSamples];
    int _intervalHead;
    int _intervalCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[ConcertAudioDirector]");
        DontDestroyOnLoad(go);
        go.AddComponent<ConcertAudioDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _spectrum = new float[spectrumSize];
        _prevSpectrum = new float[spectrumSize];
        _waveform = new float[1024];
        int historySamples = Mathf.Max(8, Mathf.RoundToInt(beatHistorySeconds * 60f));
        _bassHistory = new float[historySamples];

        if (s_pendingSource != null) { _source = s_pendingSource; s_pendingSource = null; }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        var src = ResolveSource();
        IsPlaying = src != null && src.IsPlaying;

        if (!IsPlaying)
        {
            // Decay everything to zero so lights gracefully fall back to procedural.
            float k = 1f - Mathf.Exp(-Time.deltaTime * 5f);
            Bass    = Mathf.Lerp(Bass,    0f, k);
            Mid     = Mathf.Lerp(Mid,     0f, k);
            Treble  = Mathf.Lerp(Treble,  0f, k);
            Overall = Mathf.Lerp(Overall, 0f, k);
            Kick    = Mathf.Lerp(Kick,    0f, k);
            KickFlux  = Mathf.Lerp(KickFlux,  0f, k);
            SnareFlux = Mathf.Lerp(SnareFlux, 0f, k);
            HihatFlux = Mathf.Lerp(HihatFlux, 0f, k);
            BeatPhase = Mathf.Max(0f, BeatPhase - Time.deltaTime / Mathf.Max(0.0001f, beatPhaseDecaySeconds));
            WriteDebugFields();
            return;
        }

        // ─── Spectrum / band energies ────────────────────────────────────────
        var audio = src.Source;
        audio.GetSpectrumData(_spectrum, 0, fftWindow);
        audio.GetOutputData(_waveform, 0);

        // Bin ranges assume sample rate ≈ 48 kHz; works fine at 44.1 kHz too.
        // Sum of FFT magnitudes scaled empirically into 0..1.
        //
        // Multipliers boosted 3x over the original tuning because standalone
        // builds consistently produce lower GetSpectrumData / GetOutputData
        // amplitude than editor play mode at the same listener distance
        // (different DSP pipeline / spatial audio processing). With the
        // original 6/0.8/0.3/4 values the build's EnergyLongAvg hovered at
        // 0.04-0.07 — well below the lightThreshold (0.06), mediumThreshold
        // (0.14), so tier stayed at Light and intensity was minimal. The
        // editor saturated at 1.0 anyway, so boosting doesn't change its
        // perceived behavior — both editor and build now reach usable
        // Hard/Extreme tiers on a typical music drop.
        float bassRaw   = SumBins(_spectrum, 0, 4)  * 18f;
        float midRaw    = SumBins(_spectrum, 5, 42) * 2.4f;
        float trebleRaw = SumBins(_spectrum, 43, _spectrum.Length - 1) * 0.9f;
        float overallRaw = WaveformRMS(_waveform) * 12f;

        bassRaw   = Mathf.Clamp01(bassRaw);
        midRaw    = Mathf.Clamp01(midRaw);
        trebleRaw = Mathf.Clamp01(trebleRaw);
        overallRaw = Mathf.Clamp01(overallRaw);

        // Exponential smoothing toward the raw value. bandSmoothing=0 → no smoothing
        // (snappy/jittery), 1 → frozen. We map it to a per-frame lerp coefficient.
        float lerpK = 1f - Mathf.Exp(-Time.deltaTime * Mathf.Lerp(30f, 2f, bandSmoothing));
        Bass    = Mathf.Lerp(Bass,    bassRaw,   lerpK);
        Mid     = Mathf.Lerp(Mid,     midRaw,    lerpK);
        Treble  = Mathf.Lerp(Treble,  trebleRaw, lerpK);
        Overall = Mathf.Lerp(Overall, overallRaw, lerpK);

        // Kick envelope: fast attack on bass increase, slow release. Drives an
        // obvious "thump" value that works for any music — no beat detection needed.
        float kTau = bassRaw > Kick ? kickAttackSeconds : kickReleaseSeconds;
        float kK = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, kTau));
        Kick = Mathf.Clamp01(Mathf.Lerp(Kick, bassRaw, kK));

        // ─── Drum onset detection (per-band spectral flux) ─────────────────────
        if (detectDrums) DetectDrums();
        // Save current spectrum for next frame's flux calculation.
        Array.Copy(_spectrum, _prevSpectrum, _spectrum.Length);

        // ─── Buildup detector (anticipates drops) ──────────────────────────────
        UpdateBuildupDetection();

        // ─── Energy curve (song narrative arc) ─────────────────────────────────
        UpdateEnergyCurve();

        // ─── Silence detection (held breath) ──────────────────────────────────
        UpdateSilenceDetection();

        // Drop detection: ratio of short-term to long-term energy. When the
        // short window jumps above the long-term floor by dropEnergyMultiplier,
        // and bass is also high, that's a "drop" — like the chorus hitting.
        if (detectDrops)
        {
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float shortK = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, dropShortWindowSec));
            float longK  = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, dropLongWindowSec));
            _energyShortAvg = Mathf.Lerp(_energyShortAvg, Overall, shortK);
            _energyLongAvg  = Mathf.Lerp(_energyLongAvg,  Overall, longK);
            BuildupEnergy = _energyLongAvg > 0.01f ? _energyShortAvg / _energyLongAvg : 1f;

            bool debounced = (Time.time - _lastDropTime) > dropDebounceSeconds;
            bool spike = BuildupEnergy > dropEnergyMultiplier
                         && _energyShortAvg > 0.18f
                         && Bass > 0.3f;
            if (debounced && spike)
            {
                _lastDropTime = Time.time;
                OnDrop?.Invoke();
            }
        }

        // ─── Beat detection ──────────────────────────────────────────────────
        if (useManualBpm)
        {
            float interval = 60f / Mathf.Max(1f, manualBpm);
            _manualBpmTimer += Time.deltaTime;
            if (_manualBpmTimer >= interval)
            {
                _manualBpmTimer -= interval;
                FireBeat();
            }
            DetectedBpm = manualBpm;
        }
        else
        {
            // Compare instantaneous bass to rolling average.
            float avg = _bassHistoryCount > 0 ? _bassHistorySum / _bassHistoryCount : bassRaw;
            bool isSpike = bassRaw > avg * beatThresholdMultiplier && bassRaw > 0.05f;
            bool debounced = (Time.time - _lastBeatTime) > beatDebounceSeconds;
            if (isSpike && debounced)
            {
                FireBeat();
            }

            // Update history AFTER the spike check so the new sample doesn't dilute its own threshold.
            // Replace the oldest sample in the ring buffer.
            int n = _bassHistory.Length;
            float old = _bassHistory[_bassHistoryHead];
            _bassHistorySum = _bassHistorySum - old + bassRaw;
            _bassHistory[_bassHistoryHead] = bassRaw;
            _bassHistoryHead = (_bassHistoryHead + 1) % n;
            if (_bassHistoryCount < n) _bassHistoryCount++;
        }

        // BeatPhase decay: was set to 1 by FireBeat, decays linearly to 0.
        if (beatPhaseDecaySeconds > 0f)
        {
            float elapsed = Time.time - _lastBeatPhaseSetTime;
            BeatPhase = Mathf.Clamp01(1f - elapsed / beatPhaseDecaySeconds);
        }

        WriteDebugFields();
    }

    // Per-band positive spectral flux: sum of max(0, current - prev) over bin range.
    // Positive flux fires only on the *attack* of a transient, not during decay,
    // so it's the right signal for drum-onset detection.
    static float PositiveFlux(float[] cur, float[] prev, int from, int to)
    {
        if (from < 0) from = 0;
        if (to >= cur.Length) to = cur.Length - 1;
        float s = 0f;
        for (int i = from; i <= to; i++)
        {
            float d = cur[i] - prev[i];
            if (d > 0f) s += d;
        }
        return s;
    }

    void DetectDrums()
    {
        // Bin ranges for 512-bin FFT at 48 kHz (~46.875 Hz/bin).
        float kickFluxRaw  = PositiveFlux(_spectrum, _prevSpectrum, 0,   4)   * 6f;
        float snareFluxRaw = PositiveFlux(_spectrum, _prevSpectrum, 15,  40)  * 2f;
        float hihatFluxRaw = PositiveFlux(_spectrum, _prevSpectrum, 90,  200) * 1.5f;

        // Smooth flux for continuous reactions.
        float smoothK = 1f - Mathf.Exp(-Time.deltaTime * 25f);
        KickFlux  = Mathf.Lerp(KickFlux,  Mathf.Clamp01(kickFluxRaw),  smoothK);
        SnareFlux = Mathf.Lerp(SnareFlux, Mathf.Clamp01(snareFluxRaw), smoothK);
        HihatFlux = Mathf.Lerp(HihatFlux, Mathf.Clamp01(hihatFluxRaw), smoothK);

        // Update per-band running mean + variance via exponential moving stats.
        // ~3-second window — long enough to be stable, short enough to track
        // tempo / volume changes within a song.
        float statsK = 1f - Mathf.Exp(-Time.deltaTime / 3.0f);
        UpdateRunningStats(kickFluxRaw,  ref _kickFluxMean,  ref _kickFluxVar,  statsK);
        UpdateRunningStats(snareFluxRaw, ref _snareFluxMean, ref _snareFluxVar, statsK);
        UpdateRunningStats(hihatFluxRaw, ref _hihatFluxMean, ref _hihatFluxVar, statsK);

        float kickStdDev  = Mathf.Sqrt(_kickFluxVar);
        float snareStdDev = Mathf.Sqrt(_snareFluxVar);
        float hihatStdDev = Mathf.Sqrt(_hihatFluxVar);

        // Adaptive noise floor — relative to song's overall RMS, so quiet songs
        // and loud songs both get sensibly-scaled minimum thresholds.
        float kickFloor  = Mathf.Max(0.001f, Overall * relativeNoiseFloor);
        float snareFloor = Mathf.Max(0.001f, Overall * relativeNoiseFloor);
        float hihatFloor = Mathf.Max(0.001f, Overall * relativeNoiseFloor * 0.5f);

        float now = Time.time;

        // ─── Kick onset ────────────────────────────────────────────────────
        bool kickSpike = kickFluxRaw > _kickFluxMean + kickZScore * kickStdDev
                      && kickFluxRaw > kickFloor;
        if (kickSpike && now - LastKickTime > kickDebounceSeconds)
        {
            FireKick(now);
            TryFireSting(now);
        }

        // ─── Snare onset (reject simultaneous kicks — usually just bass spilling into mids) ──
        bool snareSpike = snareFluxRaw > _snareFluxMean + snareZScore * snareStdDev
                       && snareFluxRaw > snareFloor;
        bool kickIsDominant = kickFluxRaw > _kickFluxMean + 1.5f * kickStdDev
                           && kickFluxRaw > snareFluxRaw * 1.5f;
        if (snareSpike && !kickIsDominant && now - LastSnareTime > snareDebounceSeconds)
        {
            LastSnareTime = now;
            SnareCount++;
            _snareTimes[_snareRingHead] = now;
            _snareRingHead = (_snareRingHead + 1) % kSnareRateRingSize;
            OnSnare?.Invoke();
            TryFireSting(now);
        }

        // ─── Hi-hat onset ──────────────────────────────────────────────────
        bool hihatSpike = hihatFluxRaw > _hihatFluxMean + hihatZScore * hihatStdDev
                       && hihatFluxRaw > hihatFloor;
        if (hihatSpike && now - LastHihatTime > hihatDebounceSeconds)
        {
            LastHihatTime = now;
            HihatCount++;
            _hihatTimes[_hihatRingHead] = now;
            _hihatRingHead = (_hihatRingHead + 1) % kHihatRateRingSize;
            OnHihat?.Invoke();
        }

        // Hi-hat rate (events/sec over last 5s).
        int hatsInWindow = 0;
        for (int i = 0; i < kHihatRateRingSize; i++)
            if (_hihatTimes[i] > now - 5f) hatsInWindow++;
        HihatRate = hatsInWindow / 5f;

        // Snare rate (events/sec over last 5s) — buildup detector input.
        int snaresInWindow = 0;
        for (int i = 0; i < kSnareRateRingSize; i++)
            if (_snareTimes[i] > now - 5f) snaresInWindow++;
        SnareRate = snaresInWindow / 5f;

        // ─── Crash detection: hi-hat band sustain ──────────────────────────
        float hihatBandEnergy = SumBins(_spectrum, 100, Mathf.Min(256, _spectrum.Length - 1)) * 0.4f;
        // Adaptive crash threshold: significantly above the typical hi-hat-band noise.
        float crashThreshold = Mathf.Max(0.05f, _hihatFluxMean * 4f + hihatStdDev * 3f);
        if (hihatBandEnergy > crashThreshold)
        {
            if (_hihatBandSustainStartTime < 0f) _hihatBandSustainStartTime = now;
            else if (now - _hihatBandSustainStartTime >= crashSustainSeconds
                     && now - LastCrashTime > crashDebounceSeconds)
            {
                LastCrashTime = now;
                CrashCount++;
                _hihatBandSustainStartTime = -1f;
                OnCrash?.Invoke();
                TryFireSting(now);
            }
        }
        else
        {
            _hihatBandSustainStartTime = -1f;
        }
    }

    // Sting trigger: fired after a drum onset if 2+ of {kick, snare, crash}
    // landed within an 80 ms window. Debounced to avoid metal drum fills
    // turning the show into strobes.
    void TryFireSting(float now)
    {
        int hits = 0;
        if (now - LastKickTime  < kStingWindow) hits++;
        if (now - LastSnareTime < kStingWindow) hits++;
        if (now - LastCrashTime < kStingWindow) hits++;
        if (hits < 2) return;
        if (now - _lastStingTime < kStingDebounce) return;
        _lastStingTime = now;
        OnSting?.Invoke();
    }

    // Buildup confidence: smoothed blend of snare rate, hi-hat rate, energy
    // ratio, and treble. Schmitt-trigger hysteresis fires OnBuildupStart /
    // OnBuildupEnd. Hard-capped to kBuildupMaxSeconds so a missed-drop
    // detection doesn't strand the lights in Anticipation forever.
    void UpdateBuildupDetection()
    {
        float snareSig  = Mathf.Clamp01((SnareRate - 2f) / 6f);
        float hihatSig  = Mathf.Clamp01((HihatRate - 4f) / 8f);
        float energySig = (_energyLongAvg > 0.001f)
            ? Mathf.Clamp01((_energyShortAvg / _energyLongAvg - 1f) / 0.6f)
            : 0f;
        float trebleSig = Mathf.Clamp01(Treble * 3f - 0.3f);
        float raw = snareSig * 0.40f + hihatSig * 0.25f + energySig * 0.25f + trebleSig * 0.10f;
        BuildupConfidence = Mathf.Lerp(BuildupConfidence, raw, 1f - Mathf.Exp(-Time.deltaTime * 1.5f));

        if (!IsInBuildup && BuildupConfidence > kBuildupEnterThreshold)
        {
            IsInBuildup = true;
            _buildupStartTime = Time.time;
            OnBuildupStart?.Invoke();
        }
        else if (IsInBuildup && (BuildupConfidence < kBuildupExitThreshold
                                 || Time.time - _buildupStartTime > kBuildupMaxSeconds))
        {
            IsInBuildup = false;
            OnBuildupEnd?.Invoke();
        }
    }

    // EnergyCurve: 60 s exponential moving average of Overall used as a
    // self-normalizing reference. Light program scales global intensity by
    // (current short-window energy) / (song-window energy). Quiet sections
    // produce <1.0 (dim), loud climaxes produce >1.0 (overdrive).
    void UpdateEnergyCurve()
    {
        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        float k = 1f - Mathf.Exp(-dt / 60f);
        EnergySongAvg = Mathf.Lerp(EnergySongAvg, Overall, k);
        float ratio = EnergySongAvg > 0.005f ? _energyShortAvg / EnergySongAvg : 1f;
        EnergyCurve = Mathf.Clamp01((ratio - 0.4f) / 1.6f);
    }

    // Silence: gated by IsPlaying so audio source gaps between songs don't
    // spuriously trigger. Hysteresis (0.04 enter, 0.10 exit) + 0.5 s confirm
    // window prevents flapping during sparse drum tracks.
    void UpdateSilenceDetection()
    {
        if (!IsPlaying)
        {
            _silenceCandidateStart = -999f;
            if (IsAudioSilent) { IsAudioSilent = false; OnSilenceEnd?.Invoke(); }
            return;
        }
        if (Overall < kSilenceEnterThreshold)
        {
            if (_silenceCandidateStart < 0f) _silenceCandidateStart = Time.time;
            else if (!IsAudioSilent && Time.time - _silenceCandidateStart > kSilenceEnterDuration)
            {
                IsAudioSilent = true;
                OnSilenceStart?.Invoke();
            }
        }
        else if (Overall > kSilenceExitThreshold)
        {
            _silenceCandidateStart = -999f;
            if (IsAudioSilent)
            {
                IsAudioSilent = false;
                OnSilenceEnd?.Invoke();
            }
        }
    }

    // Welford-style exponential moving mean + variance update.
    static void UpdateRunningStats(float sample, ref float mean, ref float var, float k)
    {
        float oldMean = mean;
        mean += (sample - oldMean) * k;
        // Variance uses the OLD mean for a stable estimate (Welford-style).
        var += ((sample - oldMean) * (sample - mean) - var) * k;
        if (var < 0f) var = 0f; // numerical safety
    }

    void FireKick(float now)
    {
        LastKickTime = now;
        KickCount++;
        BeatInBar = (KickCount - 1) % 4;        // cycles 0,1,2,3 — assumes 4/4
        if (BeatInBar == 0 && KickCount > 1) BarCount++;
        // Track interval for BPM detection.
        _kickTimes[_kickRingHead] = now;
        _kickRingHead = (_kickRingHead + 1) % kKickRingSize;
        if (_kickRingCount < kKickRingSize) _kickRingCount++;
        if (autoDetectBpm) UpdateBpmFromKickIntervals();
        OnKick?.Invoke();
    }

    // Median of recent inter-kick intervals → beat period → BPM. Octave-corrected
    // (doubled or halved) until it lands in [bpmMinExpected, bpmMaxExpected].
    // Robust to tempo changes and missing kicks because median ignores outliers.
    void UpdateBpmFromKickIntervals()
    {
        if (_kickRingCount < 3) return;
        // Collect valid intervals (most-recent N pairs).
        float[] intervals = new float[_kickRingCount - 1];
        int count = 0;
        for (int i = 1; i < _kickRingCount; i++)
        {
            int curIdx = (_kickRingHead - i + kKickRingSize) % kKickRingSize;
            int prevIdx = (_kickRingHead - i - 1 + kKickRingSize) % kKickRingSize;
            float a = _kickTimes[curIdx];
            float b = _kickTimes[prevIdx];
            if (a > 0f && b > 0f && a > b)
            {
                float d = a - b;
                if (d > 0.1f && d < 2.5f) intervals[count++] = d;
            }
        }
        if (count < 2) return;
        // Sort and pick median.
        Array.Sort(intervals, 0, count);
        float median = intervals[count / 2];
        float bpm = 60f / median;
        // Octave-correct: real BPM might be a multiple of detected (e.g., kick on
        // 1+3 of 4/4 = half the actual BPM). Push into the expected range.
        int safety = 0;
        while (bpm < bpmMinExpected && safety++ < 4) bpm *= 2f;
        safety = 0;
        while (bpm > bpmMaxExpected && safety++ < 4) bpm *= 0.5f;
        // Smooth toward the new value so single bad detections don't snap the BPM.
        DetectedBpm = DetectedBpm < 30f ? bpm : Mathf.Lerp(DetectedBpm, bpm, 0.4f);
    }

    void FireBeat()
    {
        float now = Time.time;
        float interval = now - _lastBeatTime;
        _lastBeatTime = now;
        _lastBeatPhaseSetTime = now;
        BeatPhase = 1f;
        BeatCount++;

        // Track interval for DetectedBpm reporting (skip the very first beat).
        if (interval > 0.2f && interval < 2f)
        {
            _intervals[_intervalHead] = interval;
            _intervalHead = (_intervalHead + 1) % kIntervalSamples;
            if (_intervalCount < kIntervalSamples) _intervalCount++;

            if (_intervalCount > 0)
            {
                float sum = 0f;
                for (int i = 0; i < _intervalCount; i++) sum += _intervals[i];
                float avgInterval = sum / _intervalCount;
                DetectedBpm = 60f / avgInterval;
            }
        }

        OnBeat?.Invoke();
    }

    SpeakerSource _lastLoggedSource;
    bool _lastLoggedIsPlaying;
    float _nextDiagLogTime;
    float _nextSourceScanTime;

    SpeakerSource ResolveSource()
    {
        // Always re-pick if the currently registered source isn't actively
        // playing — with multi-stage setups (StageGood + StageGood2), each
        // speaker's Start() calls RegisterSource() and the LAST one wins,
        // which is often the day-side stage that ConcertStageHub has
        // immediately Stop()'d. The director then analyses silence from a
        // stopped speaker, EnergyLongAvg stays 0, the color family decays
        // into LowEnergy where the random palette pick can land on
        // PaletteIce (near-white), and every laser/cone reads as flat white
        // — exactly the symptom that shows up in builds where the speaker
        // registration order ends up "wrong". Prefer ANY currently-playing
        // music source; only fall back to a stopped one if nothing is live.
        SpeakerSource picked = (_source != null && _source.IsPlaying) ? _source : null;
        // FindObjectsOfType is the expensive fallback. Off the concert stage no
        // music source is ever playing, so the cheap path above always misses and
        // this scan used to run EVERY frame for nothing (a per-frame allocation +
        // full-scene walk during 99% of gameplay). Throttle the rescan to ~2 Hz —
        // a song starting is picked up within half a second, which the lights'
        // own smoothing/decay hides completely.
        if (picked == null && Time.time >= _nextSourceScanTime)
        {
            _nextSourceScanTime = Time.time + 0.5f;
            var all = FindObjectsOfType<SpeakerSource>();
            SpeakerSource fallback = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].isMusicSource) continue;
                if (all[i].IsPlaying) { _source = all[i]; break; }
                if (fallback == null) fallback = all[i];
            }
            if (_source == null) _source = fallback;
            picked = _source;
        }
        if (picked == null) picked = _source;

        // Diagnostic: log when the resolved source changes, OR once every 5
        // seconds with current state. Lets us verify in Player.log that the
        // director is actually analyzing a PLAYING speaker. Without this we
        // were guessing whether the white-laser bug was source-related.
        bool nowPlaying = picked != null && picked.IsPlaying;
        bool sourceChanged = picked != _lastLoggedSource || nowPlaying != _lastLoggedIsPlaying;
        if (sourceChanged || Time.time >= _nextDiagLogTime)
        {
            _nextDiagLogTime = Time.time + 5f;
            _lastLoggedSource = picked;
            _lastLoggedIsPlaying = nowPlaying;
            Debug.Log($"[ConcertAudioDirector] Resolved source='{(picked != null ? picked.gameObject.name : "<null>")}' IsPlaying={nowPlaying} EnergyLong={EnergyLongAvg:F3} BeatCount={BeatCount} HihatCount={HihatCount}");
        }
        return picked;
    }

    static float SumBins(float[] bins, int from, int to)
    {
        if (from < 0) from = 0;
        if (to >= bins.Length) to = bins.Length - 1;
        float s = 0f;
        for (int i = from; i <= to; i++) s += bins[i];
        return s;
    }

    static float WaveformRMS(float[] samples)
    {
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
        return Mathf.Sqrt(sum / samples.Length);
    }

    void WriteDebugFields()
    {
        debug_bass = Bass;
        debug_mid = Mid;
        debug_treble = Treble;
        debug_overall = Overall;
        debug_kick = Kick;
        debug_beatPhase = BeatPhase;
        debug_beatCount = BeatCount;
        debug_detectedBpm = DetectedBpm;
        debug_isPlaying = IsPlaying;
        debug_kickFlux = KickFlux;
        debug_snareFlux = SnareFlux;
        debug_hihatFlux = HihatFlux;
        debug_kickCount = KickCount;
        debug_snareCount = SnareCount;
        debug_hihatCount = HihatCount;
        debug_crashCount = CrashCount;
        debug_hihatRate = HihatRate;
        debug_predictedBeatPhase = PredictedBeatPhase;
        debug_beatInBar = BeatInBar;
        debug_barCount = BarCount;
        debug_kickFluxMean = _kickFluxMean;
        debug_kickFluxStd = Mathf.Sqrt(_kickFluxVar);
    }
}
