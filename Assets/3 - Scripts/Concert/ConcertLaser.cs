using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 16 distinct laser effects organized into 4 intensity tiers (Light / Medium /
// Hard / Extreme), 4 effects per tier. Each effect has its own update function
// with hand-tuned feel — Light effects are inherently slow/dim, Extreme effects
// are fast/dense. Tier metadata lives in s_modeTier (static lookup).
//
// Lasers subscribe directly to the audio director's drum events (OnKick,
// OnSnare, OnHihat, OnCrash) so each effect can react to specific drum hits,
// not just bass loudness. Pair-mates stay phase-synchronized via shared
// syncTime in SetMode and via deterministic seeds keyed off director.SnareCount.
public class ConcertLaser : MonoBehaviour
{
    public enum LaserMode
    {
        AutoByName,
        // LIGHT tier
        SlowDrift, StaticBeam, TinyLissajous, TwinSlow,
        // MEDIUM tier
        Pan, Lissajous, TripleFan, AlternatePulse,
        // HARD tier
        AggrSweep, BeamCurtain, PulseTrio, MultiBurst,
        // EXTREME tier
        MaxBurst, BeatChaos, SpiralOut, DoubleStrobe,
        // Backward-compatibility aliases (preserved so old scenes / older code
        // referencing these names still resolves to a sensible effect).
        FanSweep       = Pan,
        LissajousScan  = Lissajous,
        MultiBeamBurst = MultiBurst,
        PulseStrobe    = PulseTrio
    }

    public enum PairId { Solo, A, B }
    public enum IntensityTier { Light, Medium, Hard, Extreme }

    // ─── Mode-tier mapping (used by ConcertLightProgram for pool selection) ───
    static readonly Dictionary<LaserMode, IntensityTier> s_modeTier = new Dictionary<LaserMode, IntensityTier>
    {
        { LaserMode.SlowDrift,      IntensityTier.Light },
        { LaserMode.StaticBeam,     IntensityTier.Light },
        { LaserMode.TinyLissajous,  IntensityTier.Light },
        { LaserMode.TwinSlow,       IntensityTier.Light },
        { LaserMode.Pan,            IntensityTier.Medium },
        { LaserMode.Lissajous,      IntensityTier.Medium },
        { LaserMode.TripleFan,      IntensityTier.Medium },
        { LaserMode.AlternatePulse, IntensityTier.Medium },
        { LaserMode.AggrSweep,      IntensityTier.Hard },
        { LaserMode.BeamCurtain,    IntensityTier.Hard },
        { LaserMode.PulseTrio,      IntensityTier.Hard },
        { LaserMode.MultiBurst,     IntensityTier.Hard },
        { LaserMode.MaxBurst,       IntensityTier.Extreme },
        { LaserMode.BeatChaos,      IntensityTier.Extreme },
        { LaserMode.SpiralOut,      IntensityTier.Extreme },
        { LaserMode.DoubleStrobe,   IntensityTier.Extreme },
    };
    public static IntensityTier TierOf(LaserMode m) =>
        s_modeTier.TryGetValue(m, out var t) ? t : IntensityTier.Hard;

    [Header("Mode")]
    public LaserMode mode = LaserMode.AutoByName;

    [Header("Pair (driven by ConcertLightProgram if not Solo)")]
    public PairId pair = PairId.Solo;

    [Header("Emitter")]
    public Vector3 emitterLocalOffset = Vector3.zero;
    public Transform emitterOverride;
    public bool autoOffsetEmitterToHousingFront = true;
    public float autoOffsetGap = 0.05f;

    [Header("Beam Visuals")]
    public float coreWidth = 0.05f;
    public float hazeWidth = 0.25f;
    public bool drawHaze = true;
    public float maxBeamDistance = 200f;
    public LayerMask raycastMask = ~0;

    [Header("Color")]
    public Color baseColor = Color.red;
    [Range(0f, 1f)] public float saturation = 1f;
    [Range(0f, 1f)] public float value = 1f;
    [Range(0f, 1f)] public float hazeAlpha = 0.30f;

    [Header("Program-Driven (set by ConcertLightProgram)")]
    public Color[] palette;
    public bool mirrorMotion;
    [Range(0f, 4f)] public float intensity = 1f;
    [Tooltip("Advisory — the program tracks its own tier for pool selection. This is just a hint for inspector display.")]
    public IntensityTier currentTier = IntensityTier.Medium;

    [Header("Audio Reactivity")]
    [Range(0f, 1f)] public float audioReactivity = 1f;
    [Range(0f, 6f)] public float kickBrightnessBoost = 3f;
    [Range(1f, 6f)] public float kickWidthMultiplier = 2.5f;

    // ─── Beam pool ────────────────────────────────────────────────────────────
    const int kBeamPoolSize = 48;
    Beam[] _beams;
    int _activeCount;
    Material _coreMat, _hazeMat;
    float _modeStartTime;
    float _autoFrontOffsetZ;

    class Beam { public LineRenderer core, haze; }

    // ─── Audio director cache ─────────────────────────────────────────────────
    ConcertAudioDirector _director;

    // ─── Per-effect phase accumulators ────────────────────────────────────────
    float _fanPhase;
    float _lissaHuePhase;
    float _burstSpinPhase;
    float _tripleFanPhase;
    float _aggrSweepPhase;
    float _maxBurstPhase;

    // ─── Awake / Start / OnDestroy ────────────────────────────────────────────
    void Awake()
    {
        if (mode == LaserMode.AutoByName)
        {
            // Default mapping for laser1..laser4 GameObjects — picks one effect
            // from each non-Light tier so a fresh scene without a program is varied.
            string n = gameObject.name;
            char last = n.Length > 0 ? n[n.Length - 1] : ' ';
            switch (last)
            {
                case '1': mode = LaserMode.Pan;          break;
                case '2': mode = LaserMode.Lissajous;    break;
                case '3': mode = LaserMode.MultiBurst;   break;
                case '4': mode = LaserMode.PulseTrio;    break;
                default:  mode = LaserMode.Pan;          break;
            }
        }
    }

    void Start()
    {
        if (autoOffsetEmitterToHousingFront) _autoFrontOffsetZ = ComputeAutoFrontOffset();

        _coreMat = MakeBeamMaterial(Color.white);
        _hazeMat = MakeBeamMaterial(Color.white);

        _beams = new Beam[kBeamPoolSize];
        for (int i = 0; i < kBeamPoolSize; i++) _beams[i] = BuildBeam(i);
        _modeStartTime = Time.time;

        _director = ConcertAudioDirector.Instance;
    }

    void OnDestroy()
    {
        if (_coreMat != null) Destroy(_coreMat);
        if (_hazeMat != null) Destroy(_hazeMat);
    }

    // ─── Public API (called by ConcertLightProgram) ───────────────────────────
    public void SetMode(LaserMode newMode, float syncTime)
    {
        if (newMode == mode && Mathf.Approximately(syncTime, _modeStartTime)) return;
        mode = newMode;
        _modeStartTime = syncTime;
        // Reset all phase accumulators so pair-mates start a fresh effect in lockstep.
        _fanPhase = _lissaHuePhase = _burstSpinPhase = 0f;
        _tripleFanPhase = _aggrSweepPhase = _maxBurstPhase = 0f;
        if (_beams != null)
            for (int i = 0; i < _beams.Length; i++) SetBeamEnabled(_beams[i], false);
    }

    public void SetPalette(Color[] p) { palette = p; }
    public void SetMirrored(bool m)   { mirrorMotion = m; }
    public void SetIntensity(float v) { intensity = Mathf.Max(0f, v); }
    public void SetTier(IntensityTier t) { currentTier = t; }

    // Cross-system unison sting — beams brighten for 200 ms. Lighter touch
    // than cone/strobe stings (no pose override) since laser modes already
    // react expressively to drum events.
    float _stingUntil = -999f;
    public void TriggerSting() { _stingUntil = Time.time + 0.20f; }
    bool StingActive => Time.time < _stingUntil;

    // ─── Audio accessors with graceful fallback when no director / silent ─────
    bool AudioActive => _director != null && _director.IsPlaying && audioReactivity > 0f;
    float Bass    => AudioActive ? _director.Bass    : 0f;
    float Mid     => AudioActive ? _director.Mid     : 0f;
    float Treble  => AudioActive ? _director.Treble  : 0f;
    float Overall => AudioActive ? _director.Overall : 0f;
    float Kick    => AudioActive ? _director.Kick    : 0f;
    float HihatFlux => AudioActive ? _director.HihatFlux : 0f;
    float BeatPhase => AudioActive ? _director.BeatPhase : 0f;
    int   BeatCount  => _director != null ? _director.BeatCount  : 0;
    int   KickCount  => _director != null ? _director.KickCount  : 0;
    int   SnareCount => _director != null ? _director.SnareCount : 0;
    int   HihatCount => _director != null ? _director.HihatCount : 0;
    int   CrashCount => _director != null ? _director.CrashCount : 0;
    float LastKickTime  => _director != null ? _director.LastKickTime  : -999f;
    float LastSnareTime => _director != null ? _director.LastSnareTime : -999f;
    float LastHihatTime => _director != null ? _director.LastHihatTime : -999f;
    float LastCrashTime => _director != null ? _director.LastCrashTime : -999f;

    // Combined kick envelope + beat phase for a robust "thump" signal.
    float Thump => Mathf.Max(Kick, BeatPhase);

    // Continuous flow term — treble-band brightness underlay added in mode
    // brightness math. Filled-in by audio between drum events so laser modes
    // never feel idle. Disabled when ConcertLightProgram.enableContinuousFlow is off.
    float TrebleFlow =>
        AudioActive && (ConcertLightProgram.Instance == null || ConcertLightProgram.Instance.enableContinuousFlow)
            ? Treble * audioReactivity * 0.5f : 0f;

    // Decay envelope for one-shot drum events. dt seconds since last hit, tau is decay time constant.
    float DecayEnvelope(float lastTime, float tau) =>
        Time.time - lastTime < 0f || tau <= 0f ? 0f : Mathf.Exp(-(Time.time - lastTime) / tau);

    float SinceKick  => Time.time - LastKickTime;
    float SinceSnare => Time.time - LastSnareTime;
    float SinceCrash => Time.time - LastCrashTime;

    // Crash boost — universal "white flash" reaction to crash cymbals across all effects.
    float CrashBoost => DecayEnvelope(LastCrashTime, 0.18f) * audioReactivity;

    // ─── Color helpers ────────────────────────────────────────────────────────
    Color BoostColor(Color c, float factor) =>
        new Color(c.r * factor, c.g * factor, c.b * factor, c.a);
    Color WithIntensity(Color c)
    {
        float mul = StingActive ? intensity * 1.6f : intensity;
        // Breath pulse — same beat-locked tick as cones/strobes for a unified
        // subliminal sync. Pulled from the program's breathDepth field.
        var program = ConcertLightProgram.Instance;
        if (program != null && program.enableBreathPulse && AudioActive)
            mul *= 1f + program.breathDepth * BeatPhase * audioReactivity;
        return new Color(c.r * mul, c.g * mul, c.b * mul, c.a);
    }

    Color PaletteColor(int beamIndex, Color fallback)
    {
        if (palette == null || palette.Length == 0) return fallback;
        int n = palette.Length;
        int idx = ((beamIndex + BeatCount) % n + n) % n;
        return palette[idx];
    }

    // Hue stepped on each event (kick/snare). Useful for "color cycle locked to drum hit."
    Color StepColorOnCount(int count, Color fallback)
    {
        if (palette != null && palette.Length > 0)
        {
            int n = palette.Length;
            return palette[((count % n) + n) % n];
        }
        Color.RGBToHSV(baseColor, out float h0, out _, out _);
        float h = Mathf.Repeat(h0 + count * 0.166f, 1f);
        return Color.HSVToRGB(h, saturation, value);
    }

    // ─── Direction primitives ─────────────────────────────────────────────────
    Vector3 DirYaw(float yawDeg) => Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
    Vector3 DirYawPitch(float yawDeg, float pitchDeg) =>
        Quaternion.Euler(pitchDeg, yawDeg, 0f) * Vector3.forward;

    // ─── Drawing primitives ───────────────────────────────────────────────────
    void DrawSingle(int beamIdx, Vector3 localDir, Color color, float widthMul = 1f)
    {
        DrawBeam(_beams[beamIdx], localDir, color, coreWidth * widthMul, hazeWidth * widthMul);
    }

    // ─── Update / dispatch ────────────────────────────────────────────────────
    void Update()
    {
        if (_beams == null || _beams.Length == 0) return;

        // Hold during program-wide silence / pre-drop freeze: kill all beams
        // for this frame. Drop / silence-end release brings them back.
        var prog = ConcertLightProgram.Instance;
        if (prog != null && prog.IsHolding)
        {
            for (int i = 0; i < _beams.Length; i++) SetBeamEnabled(_beams[i], false);
            _activeCount = 0;
            return;
        }

        float t = Time.time - _modeStartTime;
        Color hue = baseColor;

        switch (mode)
        {
            case LaserMode.SlowDrift:      UpdateSlowDrift(t, hue);      break;
            case LaserMode.StaticBeam:     UpdateStaticBeam(t, hue);     break;
            case LaserMode.TinyLissajous:  UpdateTinyLissa(t, hue);      break;
            case LaserMode.TwinSlow:       UpdateTwinSlow(t, hue);       break;
            case LaserMode.Pan:            UpdatePan(t, hue);            break;
            case LaserMode.Lissajous:      UpdateLissajous(t, hue);      break;
            case LaserMode.TripleFan:      UpdateTripleFan(t, hue);      break;
            case LaserMode.AlternatePulse: UpdateAlternatePulse(t, hue); break;
            case LaserMode.AggrSweep:      UpdateAggrSweep(t, hue);      break;
            case LaserMode.BeamCurtain:    UpdateBeamCurtain(t, hue);    break;
            case LaserMode.PulseTrio:      UpdatePulseTrio(t, hue);      break;
            case LaserMode.MultiBurst:     UpdateMultiBurst(t, hue);     break;
            case LaserMode.MaxBurst:       UpdateMaxBurst(t, hue);       break;
            case LaserMode.BeatChaos:      UpdateBeatChaos(t, hue);      break;
            case LaserMode.SpiralOut:      UpdateSpiralOut(t, hue);      break;
            case LaserMode.DoubleStrobe:   UpdateDoubleStrobe(t, hue);   break;
        }

        for (int i = _activeCount; i < _beams.Length; i++) SetBeamEnabled(_beams[i], false);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LIGHT TIER — slow, deliberate, intentional negative space
    // ═══════════════════════════════════════════════════════════════════════════

    // L1 SlowDrift — single beam, very slow side-to-side drift over ~10s cycle.
    // Color steps only on snare. Pure Pink Floyd contemplation.
    void UpdateSlowDrift(float t, Color hue)
    {
        _activeCount = 1;
        float mirrorSign = mirrorMotion ? -1f : 1f;
        float yawDeg = Mathf.Sin(t * 0.6f) * 12f * mirrorSign; // 12° amplitude, ~10s cycle
        Color baseCol = StepColorOnCount(SnareCount, hue);
        // Subtle Thump pump + crash flash.
        float bright = 0.7f + Thump * audioReactivity * 1.2f + TrebleFlow + CrashBoost * 4f;
        Color punched = WithIntensity(BoostColor(baseCol, bright));
        DrawSingle(0, DirYaw(yawDeg), punched, 0.7f);
    }

    // L2 StaticBeam — single beam pointing forward, no spatial motion. Color
    // steps on every kick. The "still candle" effect.
    void UpdateStaticBeam(float t, Color hue)
    {
        _activeCount = 1;
        Color baseCol = StepColorOnCount(KickCount, hue);
        float bright = 0.6f + Kick * audioReactivity * 2f + TrebleFlow + CrashBoost * 4f;
        Color punched = WithIntensity(BoostColor(baseCol, bright));
        DrawSingle(0, DirYaw(0f), punched, 0.7f);
    }

    // L3 TinyLissajous — Lissajous with ~3° amplitude, looks almost still but breathes.
    void UpdateTinyLissa(float t, Color hue)
    {
        _activeCount = 1;
        float yaw   = Mathf.Sin(t * 0.7f)        * 3f;
        float pitch = Mathf.Sin(t * 1.1f + 0.5f) * 3f;
        Color baseCol = StepColorOnCount(SnareCount, hue);
        float bright = 0.7f + Thump * audioReactivity * 1.2f + TrebleFlow + CrashBoost * 4f;
        Color punched = WithIntensity(BoostColor(baseCol, bright));
        DrawSingle(0, DirYawPitch(yaw, pitch), punched, 0.7f);
    }

    // L4 TwinSlow — 2 beams in narrow V, slowly opening/closing. Snare flips orientation.
    void UpdateTwinSlow(float t, Color hue)
    {
        _activeCount = 2;
        float opening = (Mathf.Sin(t * 1.0f) * 0.5f + 0.5f) * 5f; // 0..5°
        float snareFlip = (SnareCount & 1) == 0 ? 1f : -1f;
        float halfAngle = opening * snareFlip;
        Color c0 = PaletteColor(0, hue);
        Color c1 = PaletteColor(1, hue);
        float bright = 0.7f + Thump * audioReactivity * 1.2f + TrebleFlow + CrashBoost * 4f;
        Color p0 = WithIntensity(BoostColor(c0, bright));
        Color p1 = WithIntensity(BoostColor(c1, bright));
        DrawSingle(0, DirYaw(-halfAngle), p0, 0.7f);
        DrawSingle(1, DirYaw(+halfAngle), p1, 0.7f);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MEDIUM TIER — moderate motion, verse-level energy
    // ═══════════════════════════════════════════════════════════════════════════

    // M1 Pan — moderate sweep, normal speed. Color steps on snare.
    void UpdatePan(float t, Color hue)
    {
        _activeCount = 1;
        float speedScale = 1f + Bass * audioReactivity * 2f;
        _fanPhase += Time.deltaTime * 1.5f * speedScale;
        float mirrorSign = mirrorMotion ? -1f : 1f;
        float yawDeg = Mathf.Sin(_fanPhase) * 25f * mirrorSign;
        Color baseCol = StepColorOnCount(SnareCount, hue);
        float widthMul = 1f + Thump * audioReactivity * (kickWidthMultiplier - 1f) * 0.5f;
        float bright = 1f + Thump * audioReactivity * kickBrightnessBoost + CrashBoost * 4f;
        Color punched = WithIntensity(BoostColor(baseCol, bright));
        DrawSingle(0, DirYaw(yawDeg), punched, widthMul);
    }

    // M2 Lissajous — full Lissajous figure. Hi-hats add tiny amplitude shimmer.
    void UpdateLissajous(float t, Color hue)
    {
        _activeCount = 1;
        float ampScale = 1f + Overall * audioReactivity * 1.5f + HihatFlux * 0.4f;
        float mirrorSign = mirrorMotion ? -1f : 1f;
        float yaw   = Mathf.Sin(t * 1.7f)        * 30f * ampScale * mirrorSign;
        float pitch = Mathf.Sin(t * 2.3f + 1.1f) * 25f * ampScale;
        // Smooth hue cycle + treble shimmer.
        _lissaHuePhase += Time.deltaTime * 0.6f * (1f + Treble * audioReactivity * 4f);
        Color.RGBToHSV(baseColor, out float h0, out _, out _);
        Color hueCycled = Color.HSVToRGB(Mathf.Repeat(h0 + _lissaHuePhase, 1f), saturation, value);
        Color baseCol = PaletteColor(0, hueCycled);
        float widthMul = 1f + Thump * audioReactivity * (kickWidthMultiplier - 1f) * 0.4f;
        float bright = 1f + Thump * audioReactivity * kickBrightnessBoost * 0.7f + CrashBoost * 4f;
        Color punched = WithIntensity(BoostColor(baseCol, bright));
        DrawSingle(0, DirYawPitch(yaw, pitch), punched, widthMul);
    }

    // M3 TripleFan — 3 beams in small fan, the fan rotates around forward axis.
    // Snare flips rotation direction.
    void UpdateTripleFan(float t, Color hue)
    {
        _activeCount = 3;
        float flip = (SnareCount & 1) == 0 ? 1f : -1f;
        float mirrorSign = mirrorMotion ? -1f : 1f;
        _tripleFanPhase += Time.deltaTime * 30f * flip * mirrorSign;
        float spreadDeg = 8f;
        for (int i = 0; i < 3; i++)
        {
            float u = i - 1f;                                    // -1, 0, +1
            float yaw = u * spreadDeg;
            // Rotate the fan around forward axis by accumulated phase.
            Quaternion rot = Quaternion.AngleAxis(_tripleFanPhase, Vector3.forward);
            Vector3 dir = rot * DirYaw(yaw);
            Color c = PaletteColor(i, hue);
            float bright = 1f + Thump * audioReactivity * 2f + CrashBoost * 4f;
            Color punched = WithIntensity(BoostColor(c, bright));
            DrawSingle(i, dir, punched, 1f);
        }
    }

    // M4 AlternatePulse — 2 beams pulsing in alternation, locked to kick events.
    // Even kicks pulse beam 0, odd kicks pulse beam 1.
    void UpdateAlternatePulse(float t, Color hue)
    {
        _activeCount = 2;
        float decay = DecayEnvelope(LastKickTime, 0.30f);
        bool kickIsEven = (KickCount & 1) == 0;
        float d0 = kickIsEven ? decay : 0f;
        float d1 = kickIsEven ? 0f : decay;
        Color c0 = PaletteColor(0, hue);
        Color c1 = PaletteColor(1, hue);
        Color p0 = WithIntensity(BoostColor(c0, 0.4f + d0 * 5f + CrashBoost * 4f));
        Color p1 = WithIntensity(BoostColor(c1, 0.4f + d1 * 5f + CrashBoost * 4f));
        DrawSingle(0, DirYaw(-6f), p0, 1f + d0 * 2f);
        DrawSingle(1, DirYaw(+6f), p1, 1f + d1 * 2f);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HARD TIER — energetic, chorus-level
    // ═══════════════════════════════════════════════════════════════════════════

    // H1 AggrSweep — fast wide sweep, SNAPS pose on every snare.
    // The signature "synced to the drums" effect.
    int _lastAggrSnareSeen = -1;
    float _aggrSnapPose;
    void UpdateAggrSweep(float t, Color hue)
    {
        _activeCount = 1;
        if (SnareCount != _lastAggrSnareSeen)
        {
            _lastAggrSnareSeen = SnareCount;
            // Deterministic pose per snare-count → pair-mates land on same pose.
            float seed = SnareCount * 1.3471f;
            _aggrSnapPose = (Mathf.Sin(seed) * 28f);
        }
        float mirrorSign = mirrorMotion ? -1f : 1f;
        _aggrSweepPhase += Time.deltaTime * 4f;
        float jitter = Mathf.Sin(_aggrSweepPhase) * 8f;
        float yaw = (_aggrSnapPose + jitter) * mirrorSign;
        Color baseCol = PaletteColor(0, hue);
        float widthMul = 1.2f + Thump * audioReactivity * 1.2f;
        float bright = 1f + Thump * audioReactivity * kickBrightnessBoost * 1.2f + CrashBoost * 5f;
        Color punched = WithIntensity(BoostColor(baseCol, bright));
        DrawSingle(0, DirYaw(yaw), punched, widthMul);
    }

    // H2 BeamCurtain — 8 parallel beams in a horizontal curtain, brightness
    // modulated by hi-hat flux. Visible shimmer chases along the curtain.
    void UpdateBeamCurtain(float t, Color hue)
    {
        _activeCount = 8;
        float curtainHalfSpan = 18f;
        for (int i = 0; i < 8; i++)
        {
            float u = (i / 7f) * 2f - 1f;
            float yaw = u * curtainHalfSpan;
            // Per-beam shimmer phase offset so the shimmer moves along the curtain.
            float shimmer = 0.5f + 0.5f * Mathf.Sin(t * 5f + i * 0.7f);
            float hatBoost = 1f + HihatFlux * audioReactivity * 4f;
            Color c = PaletteColor(i, hue);
            float bright = (0.5f + shimmer * 0.6f) * hatBoost
                         + Thump * audioReactivity * 2f
                         + CrashBoost * 4f;
            Color punched = WithIntensity(BoostColor(c, bright));
            float w = 0.7f + shimmer * 0.4f;
            DrawSingle(i, DirYaw(yaw), punched, w);
        }
    }

    // H3 PulseTrio — 3 thick beams pulsing on every kick (kick-event-driven, not sin).
    void UpdatePulseTrio(float t, Color hue)
    {
        _activeCount = 3;
        float kickDecay = DecayEnvelope(LastKickTime, 0.25f);
        float w = 1f + kickDecay * 3f;
        Color stepCol = StepColorOnCount(KickCount, hue);
        for (int i = 0; i < 3; i++)
        {
            float yawDeg = (i - 1) * 6f;
            Color c = PaletteColor(i, stepCol);
            float bright = 0.5f + kickDecay * 5f + CrashBoost * 4f;
            Color punched = WithIntensity(BoostColor(c, bright));
            DrawSingle(i, DirYaw(yawDeg), punched, w);
        }
    }

    // H4 MultiBurst — 18-beam fan with steady rotation, strobe on Thump.
    void UpdateMultiBurst(float t, Color hue)
    {
        const int n = 18;
        _activeCount = n;
        float mirrorSign = mirrorMotion ? -1f : 1f;
        _burstSpinPhase += Time.deltaTime * 60f * mirrorSign;
        bool strobeOn = Thump > 0.2f;
        Color stepCol = StepColorOnCount(BeatCount, hue);
        float bright = 1f + Thump * audioReactivity * kickBrightnessBoost + CrashBoost * 4f;
        for (int i = 0; i < n; i++)
        {
            if (!strobeOn) { SetBeamEnabled(_beams[i], false); continue; }
            float az = (i / (float)n) * 360f + _burstSpinPhase;
            Vector3 dir = Quaternion.AngleAxis(az, Vector3.forward)
                        * Quaternion.AngleAxis(20f, Vector3.right) * Vector3.forward;
            Color c = WithIntensity(BoostColor(PaletteColor(i, stepCol), bright));
            DrawSingle(i, dir, c, 1f);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EXTREME TIER — drops, climax, peak energy
    // ═══════════════════════════════════════════════════════════════════════════

    // X1 MaxBurst — 40-beam fan, every kick triggers strobe burst.
    void UpdateMaxBurst(float t, Color hue)
    {
        const int n = 40;
        _activeCount = n;
        float mirrorSign = mirrorMotion ? -1f : 1f;
        _maxBurstPhase += Time.deltaTime * 120f * mirrorSign;
        float kickDecay = DecayEnvelope(LastKickTime, 0.15f);
        bool strobeOn = kickDecay > 0.15f;
        Color stepCol = StepColorOnCount(KickCount, hue);
        for (int i = 0; i < n; i++)
        {
            if (!strobeOn) { SetBeamEnabled(_beams[i], false); continue; }
            float az = (i / (float)n) * 360f + _maxBurstPhase;
            Vector3 dir = Quaternion.AngleAxis(az, Vector3.forward)
                        * Quaternion.AngleAxis(35f, Vector3.right) * Vector3.forward;
            float bright = 2f + kickDecay * 6f + CrashBoost * 5f;
            Color c = WithIntensity(BoostColor(PaletteColor(i, stepCol), bright));
            DrawSingle(i, dir, c, 1.2f);
        }
    }

    // X2 BeatChaos — 4 beams re-randomized to new poses on every snare.
    // Deterministic seed per snare-count → pair-mates compute identical poses.
    int _lastChaosSnareSeen = -1;
    Vector3[] _chaosDirs = new Vector3[4];
    void UpdateBeatChaos(float t, Color hue)
    {
        _activeCount = 4;
        if (SnareCount != _lastChaosSnareSeen)
        {
            _lastChaosSnareSeen = SnareCount;
            for (int i = 0; i < 4; i++)
            {
                int seed = SnareCount * 7919 + i * 31;
                // xorshift-ish hash for deterministic per-(snare,beam) random
                seed ^= (seed << 13);
                seed ^= (seed >> 17);
                seed ^= (seed << 5);
                float yaw   = ((seed & 0xFFFF) / 65535f * 60f) - 30f;
                float pitch = (((seed >> 16) & 0xFFFF) / 65535f * 40f) - 20f;
                _chaosDirs[i] = DirYawPitch(yaw, pitch);
            }
        }
        float snareDecay = DecayEnvelope(LastSnareTime, 0.25f);
        float bright = 0.6f + snareDecay * 4f + Thump * audioReactivity * 2f + CrashBoost * 5f;
        for (int i = 0; i < 4; i++)
        {
            Color c = PaletteColor(i, hue);
            Color punched = WithIntensity(BoostColor(c, bright));
            DrawSingle(i, _chaosDirs[i], punched, 1.1f);
        }
    }

    // X3 SpiralOut — single beam tracing fast outward spiral, restarting on kicks.
    void UpdateSpiralOut(float t, Color hue)
    {
        _activeCount = 1;
        float u = Mathf.Clamp01(SinceKick / 0.6f); // 0 just after kick → 1 over 0.6s
        float angleDeg = u * 720f * (mirrorMotion ? -1f : 1f);
        float radius = u * 25f;
        float yaw   = Mathf.Cos(angleDeg * Mathf.Deg2Rad) * radius;
        float pitch = Mathf.Sin(angleDeg * Mathf.Deg2Rad) * radius;
        Color baseCol = PaletteColor(0, hue);
        float bright = 1f + (1f - u) * 5f + CrashBoost * 5f;
        float w = 1.2f + (1f - u) * 1.5f;
        Color punched = WithIntensity(BoostColor(baseCol, bright));
        DrawSingle(0, DirYawPitch(yaw, pitch), punched, w);
    }

    // X4 DoubleStrobe — 8 beams in 2 interleaved phases. Odd beams fire on
    // odd kicks, even on even kicks. Visible "ping-pong" feel.
    void UpdateDoubleStrobe(float t, Color hue)
    {
        _activeCount = 8;
        float kickDecay = DecayEnvelope(LastKickTime, 0.18f);
        bool oddKick = (KickCount & 1) == 1;
        Color stepCol = StepColorOnCount(KickCount, hue);
        for (int i = 0; i < 8; i++)
        {
            bool isOddBeam = (i & 1) == 1;
            bool firing = (isOddBeam == oddKick) && kickDecay > 0.1f;
            if (!firing) { SetBeamEnabled(_beams[i], false); continue; }
            float u = (i / 7f) * 2f - 1f;
            float yaw = u * 25f;
            Color c = PaletteColor(i, stepCol);
            float bright = 1.2f + kickDecay * 5f + CrashBoost * 5f;
            Color punched = WithIntensity(BoostColor(c, bright));
            DrawSingle(i, DirYaw(yaw), punched, 1.5f);
        }
    }

    // ─── Beam construction / drawing ──────────────────────────────────────────
    Beam BuildBeam(int index)
    {
        var b = new Beam();
        var coreGo = new GameObject($"Beam{index}_Core");
        coreGo.transform.SetParent(transform, worldPositionStays: false);
        b.core = ConfigureLine(coreGo.AddComponent<LineRenderer>(), _coreMat, coreWidth);

        var hazeGo = new GameObject($"Beam{index}_Haze");
        hazeGo.transform.SetParent(transform, worldPositionStays: false);
        b.haze = ConfigureLine(hazeGo.AddComponent<LineRenderer>(), _hazeMat, hazeWidth);
        return b;
    }

    static LineRenderer ConfigureLine(LineRenderer lr, Material mat, float width)
    {
        lr.useWorldSpace = false;
        lr.positionCount = 2;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, Vector3.zero);
        lr.startWidth = width;
        lr.endWidth = width;
        lr.material = mat;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 4;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        return lr;
    }

    void DrawBeam(Beam b, Vector3 localDir, Color color, float wCore, float wHaze)
    {
        Vector3 emitOffsetLocal = emitterLocalOffset + new Vector3(0f, 0f, _autoFrontOffsetZ);
        Transform emitT = emitterOverride != null ? emitterOverride : transform;
        Vector3 worldStart = emitT.TransformPoint(emitOffsetLocal);
        Vector3 worldDir = emitT.TransformDirection(localDir).normalized;

        float dist = maxBeamDistance;
        if (Physics.Raycast(worldStart, worldDir, out var hit, maxBeamDistance,
                            raycastMask, QueryTriggerInteraction.Ignore))
        {
            dist = hit.distance;
        }

        Vector3 localStart = emitOffsetLocal;
        Vector3 worldEnd = worldStart + worldDir * dist;
        Vector3 localEnd = transform.InverseTransformPoint(worldEnd);
        if (emitterOverride != null) localStart = transform.InverseTransformPoint(worldStart);

        b.core.enabled = true;
        b.core.startWidth = wCore;
        b.core.endWidth   = wCore;
        b.core.startColor = color;
        b.core.endColor   = color;
        b.core.SetPosition(0, localStart);
        b.core.SetPosition(1, localEnd);

        if (drawHaze)
        {
            Color hazeCol = new Color(color.r, color.g, color.b, hazeAlpha);
            b.haze.enabled = true;
            b.haze.startWidth = wHaze;
            b.haze.endWidth   = wHaze;
            b.haze.startColor = hazeCol;
            b.haze.endColor   = hazeCol;
            b.haze.SetPosition(0, localStart);
            b.haze.SetPosition(1, localEnd);
        }
        else
        {
            b.haze.enabled = false;
        }
    }

    static void SetBeamEnabled(Beam b, bool on)
    {
        if (b.core != null) b.core.enabled = on;
        if (b.haze != null) b.haze.enabled = on;
    }

    float ComputeAutoFrontOffset()
    {
        var filters = GetComponentsInChildren<MeshFilter>(true);
        if (filters == null || filters.Length == 0) return 0f;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        float maxZ = 0f;
        bool any = false;
        foreach (var mf in filters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            Vector3 c = b.center, e = b.extents;
            Matrix4x4 mfToLocal = worldToLocal * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    ((i & 1) == 0 ? -e.x : e.x),
                    ((i & 2) == 0 ? -e.y : e.y),
                    ((i & 4) == 0 ? -e.z : e.z));
                Vector3 inLocal = mfToLocal.MultiplyPoint3x4(corner);
                if (!any || inLocal.z > maxZ) { maxZ = inLocal.z; any = true; }
            }
        }
        return any ? Mathf.Max(0f, maxZ) + autoOffsetGap : 0f;
    }

    // ─── Material helpers ─────────────────────────────────────────────────────
    // Source the additive shader from a Material asset shipped under
    // Resources/ — Unity GUARANTEES anything in Resources is in the build,
    // and a Material asset that references the shader pulls the shader (with
    // all its variants) along with it. Shader.Find alone fails in standalone
    // builds because Unity strips shaders not referenced by any Material or
    // listed in Always Included Shaders; the fallback chain then lands on a
    // shader that ignores LineRenderer vertex colors, and lasers render
    // flat white. The Shader.Find chain is preserved as a last-resort
    // fallback for cases where Resources can't be loaded (e.g., editor
    // freshly opened with no Resources folder yet).
    static Material s_templateMat;
    static Shader s_beamShader;
    static Shader GetBeamShader()
    {
        if (s_beamShader != null) return s_beamShader;
        if (s_templateMat == null)
        {
            s_templateMat = Resources.Load<Material>("ConcertAdditiveMaterial");
            if (s_templateMat == null)
                Debug.LogWarning("[ConcertLaser] Resources/ConcertAdditiveMaterial.mat not found — falling back to Shader.Find. Lasers may render flat white in standalone builds.");
        }
        if (s_templateMat != null && s_templateMat.shader != null)
        {
            s_beamShader = s_templateMat.shader;
            return s_beamShader;
        }
        s_beamShader = Shader.Find("Concert/Additive");
        if (s_beamShader == null) s_beamShader = Shader.Find("Particles/Additive");
        if (s_beamShader == null) s_beamShader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (s_beamShader == null) s_beamShader = Shader.Find("Sprites/Default");
        if (s_beamShader == null) s_beamShader = Shader.Find("Unlit/Color");
        return s_beamShader;
    }

    static Material MakeBeamMaterial(Color color)
    {
        var mat = new Material(GetBeamShader());
        var glow = GetSoftGlowTexture();
        if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", glow);
        if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", glow);
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        return mat;
    }

    static Texture2D s_softGlow;
    static Texture2D GetSoftGlowTexture()
    {
        if (s_softGlow != null) return s_softGlow;
        const int size = 64;
        s_softGlow = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
        s_softGlow.wrapMode = TextureWrapMode.Clamp;
        s_softGlow.filterMode = FilterMode.Bilinear;
        s_softGlow.hideFlags = HideFlags.HideAndDontSave;
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                byte v = (byte)Mathf.RoundToInt(a * 255f);
                pixels[y * size + x] = new Color32(v, v, v, v);
            }
        }
        s_softGlow.SetPixels32(pixels);
        s_softGlow.Apply(false, true);
        return s_softGlow;
    }
}
