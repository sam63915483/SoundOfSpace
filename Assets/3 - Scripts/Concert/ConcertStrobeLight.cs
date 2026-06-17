using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Pure-white strobe — drop on a GameObject (or use ConcertLightProgram's
// auto-attach) for a Unity Spot light that flashes synced to the bass + BPM.
// 16 modes split into 4 intensity tiers (Light/Medium/Hard/Extreme).
//
// Strobe pulse is the SUM of two drivers stacked:
//   1. BPM-derived metronome via PredictedBeatPhase + tempo sub-divisions —
//      keeps the show ticking on tempo even during quiet passages.
//   2. Bass-kick punches via OnKick decay envelope — adds the "boom" punch
//      synced to the actual kick events.
//
// Color is locked to white. mirrorMotion antiphases the pair-mate so
// spotlight1 and spotlight2 visibly ping-pong.
//
// Rig structure (see ConcertConeLight for the same pattern — the parent
// transform stays still; the cone Light + visible beam mesh live on a child
// rig so the visible spotlight model on the parent doesn't move):
//   <this GO>      ← parent (visible spotlight model)
//     _StrobeRig   ← child. Spot Light + visible cone mesh.
//       _StrobeVisual
public class ConcertStrobeLight : MonoBehaviour
{
    public enum StrobeLightMode
    {
        // LIGHT tier
        SlowPulse, BassBreath, GentleFlash, BarKick,
        // MEDIUM tier
        BeatStrobe, BassDouble, OnOffSync, AltBass,
        // HARD tier
        FastStrobe, BassMachineGun, CountInPulse, RipplePulse,
        // EXTREME tier
        HardStrobe16th, BassChaos, BlitzStrobe, AntiPhase,
    }

    public enum PairId { Solo, A, B }

    static readonly Dictionary<StrobeLightMode, ConcertLaser.IntensityTier> s_modeTier = new Dictionary<StrobeLightMode, ConcertLaser.IntensityTier>
    {
        { StrobeLightMode.SlowPulse,       ConcertLaser.IntensityTier.Light },
        { StrobeLightMode.BassBreath,      ConcertLaser.IntensityTier.Light },
        { StrobeLightMode.GentleFlash,     ConcertLaser.IntensityTier.Light },
        { StrobeLightMode.BarKick,         ConcertLaser.IntensityTier.Light },
        { StrobeLightMode.BeatStrobe,      ConcertLaser.IntensityTier.Medium },
        { StrobeLightMode.BassDouble,      ConcertLaser.IntensityTier.Medium },
        { StrobeLightMode.OnOffSync,       ConcertLaser.IntensityTier.Medium },
        { StrobeLightMode.AltBass,         ConcertLaser.IntensityTier.Medium },
        { StrobeLightMode.FastStrobe,      ConcertLaser.IntensityTier.Hard },
        { StrobeLightMode.BassMachineGun,  ConcertLaser.IntensityTier.Hard },
        { StrobeLightMode.CountInPulse,    ConcertLaser.IntensityTier.Hard },
        { StrobeLightMode.RipplePulse,     ConcertLaser.IntensityTier.Hard },
        { StrobeLightMode.HardStrobe16th,  ConcertLaser.IntensityTier.Extreme },
        { StrobeLightMode.BassChaos,       ConcertLaser.IntensityTier.Extreme },
        { StrobeLightMode.BlitzStrobe,     ConcertLaser.IntensityTier.Extreme },
        { StrobeLightMode.AntiPhase,       ConcertLaser.IntensityTier.Extreme },
    };
    public static ConcertLaser.IntensityTier TierOf(StrobeLightMode m) =>
        s_modeTier.TryGetValue(m, out var t) ? t : ConcertLaser.IntensityTier.Medium;

    [Header("Mode")]
    public StrobeLightMode mode = StrobeLightMode.BeatStrobe;

    [Header("Pair (driven by ConcertLightProgram if not Solo)")]
    public PairId pair = PairId.Solo;

    [Header("Light")]
    public float spotAngle = 25f;
    public float innerSpotAngle = 8f;
    public float range = 50f;

    [Header("Cone Aim (rest pose of the cone — parent transform is never moved)")]
    [Tooltip("Local offset of the cone emitter from the GameObject origin. Move down/forward to align with the spotlight model's actual lens position.")]
    public Vector3 coneEmitterLocalOffset = new Vector3(0f, -0.55f, 0.2f);
    [Tooltip("Initial Euler rotation of the cone rig. Default pitches the cone forward/down toward the audience.")]
    public Vector3 coneRestEuler = new Vector3(20f, 0f, 0f);

    [Header("Intensity")]
    public float idleIntensity = 0f;        // Strobes are mostly OFF between pulses
    [Tooltip("Peak Unity Light intensity. Kept very low (0.3) so ground splash is minimal — visible beam mesh handles 100% of the visual punch.")]
    public float peakIntensity = 0.3f;

    [Header("Audio Reactivity")]
    [Range(0f, 4f)] public float strobeBassReactivity = 1f;
    [Tooltip("How fast the light returns to dark after a flash (higher = sharper strobe).")]
    public float decayRate = 60f;
    [Tooltip("How fast the light ramps up on a flash.")]
    public float attackRate = 200f;

    [Header("Visible Beam")]
    [Tooltip("Show the visible cone mesh (additive transparent). Off = invisible Light only.")]
    public bool drawVisibleBeam = true;
    [Range(0f, 4f)] public float beamBrightness = 0.4f;

    [Header("Slow Yaw Drift (smooth, occasional)")]
    [Tooltip("Strobes occasionally retarget yaw on snares. ±this many degrees max from rest.")]
    [Range(0f, 60f)] public float yawDriftAmplitude = 22f;
    [Tooltip("Retarget the drift yaw every Nth snare. 4 ~= once per bar.")]
    [Range(1, 32)] public int snaresPerYawRetarget = 4;
    [Tooltip("Max degrees per second the strobe rig is allowed to rotate. Low = very smooth drift, high = snappy.")]
    [Range(1f, 90f)] public float driftRateDegreesPerSec = 9f;

    [Header("Program-Driven (set by ConcertLightProgram)")]
    [Tooltip("If true, this strobe is antiphase from its pair-mate. Used by OnOffSync/AntiPhase/RipplePulse/AltBass to ping-pong the pair.")]
    public bool mirrorMotion;
    [Range(0f, 4f)] public float intensity = 1f;
    [Tooltip("Advisory — the program tracks its own tier. This is just a hint for inspector display.")]
    public ConcertLaser.IntensityTier currentTier = ConcertLaser.IntensityTier.Medium;

    Transform _strobeRig;
    Light _light;
    MeshRenderer _coneRenderer;
    Material _coneMat;
    ConcertAudioDirector _director;
    float _modeStartTime;
    float _stingUntil = -999f;
    Quaternion _strobeBaseRot = Quaternion.identity;  // cached Quaternion.Euler(coneRestEuler)
    float _driftTargetYaw;
    int _lastSnareCountForDrift = -1;

    // Cross-system unison sting — blast white for 200ms, override normal mode.
    public void TriggerSting() { _stingUntil = Time.time + 0.20f; }

    void Awake()
    {
        EnsureRig();
    }

    // Live-poll the InputSettings.fxConcertShadows toggle. Default None matches
    // the hard-coded value in EnsureRig; flipping the toggle in the pause menu
    // promotes to Soft shadows on the next Update tick.
    void ApplyShadowsSetting()
    {
        if (_light == null) return;
        var cem = CameraEffectsManager.Instance;
        bool want = cem != null && cem.Input != null && cem.Input.fxConcertShadows;
        var wantedMode = want ? LightShadows.Soft : LightShadows.None;
        if (_light.shadows != wantedMode) _light.shadows = wantedMode;
    }

    void Start()
    {
        _director = ConcertAudioDirector.Instance;
        _modeStartTime = Time.time;
    }

    public void EnsureRig()
    {
        var rigT = transform.Find("_StrobeRig");
        if (rigT == null)
        {
            var rigGO = new GameObject("_StrobeRig");
            rigGO.transform.SetParent(transform, worldPositionStays: false);
            rigGO.transform.localRotation = Quaternion.Euler(coneRestEuler);
            rigT = rigGO.transform;
        }
        _strobeRig = rigT;
        _strobeRig.localPosition = coneEmitterLocalOffset;
        _strobeBaseRot = Quaternion.Euler(coneRestEuler);
        _strobeRig.localRotation = _strobeBaseRot;

        _light = _strobeRig.GetComponent<Light>();
        if (_light == null) _light = _strobeRig.gameObject.AddComponent<Light>();
        _light.type = LightType.Spot;
        _light.color = Color.white;
        _light.intensity = idleIntensity;
        _light.spotAngle = spotAngle;
        _light.innerSpotAngle = Mathf.Min(innerSpotAngle, spotAngle - 1f);
        _light.range = range;
        _light.shadows = LightShadows.None;
        // ForcePixel — keep the cone shape stable when many stage lights are in
        // frame. Default Auto demoted some to per-vertex/SH as the camera turned,
        // which made the cone lose its falloff and bloom across the ground.
        _light.renderMode = LightRenderMode.ForcePixel;

        // Also light the GPU-instanced grass (see ConcertConeLight) — white strobe.
        var grassPL = _light.GetComponent<GrassPointLight>();
        if (grassPL == null) grassPL = _light.gameObject.AddComponent<GrassPointLight>();
        grassPL.grassStrength = 0.5f;

        var visualT = _strobeRig.Find("_StrobeVisual");
        GameObject visualGO;
        if (visualT == null)
        {
            visualGO = new GameObject("_StrobeVisual");
            visualGO.transform.SetParent(_strobeRig, worldPositionStays: false);
            visualGO.transform.localPosition = Vector3.zero;
            visualGO.transform.localRotation = Quaternion.identity;
        }
        else
        {
            visualGO = visualT.gameObject;
        }
        var mf = visualGO.GetComponent<MeshFilter>();
        if (mf == null) mf = visualGO.AddComponent<MeshFilter>();
        mf.sharedMesh = ConcertBeamShared.BuildConeMesh(spotAngle * 0.5f, range, 28);
        _coneRenderer = visualGO.GetComponent<MeshRenderer>();
        if (_coneRenderer == null) _coneRenderer = visualGO.AddComponent<MeshRenderer>();
        if (_coneMat == null) _coneMat = ConcertBeamShared.MakeBeamMaterial();
        _coneRenderer.sharedMaterial = _coneMat;
        _coneRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _coneRenderer.receiveShadows = false;
        _coneRenderer.lightProbeUsage = LightProbeUsage.Off;
        _coneRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _coneRenderer.enabled = drawVisibleBeam;
    }

    void OnDestroy()
    {
        if (_coneMat != null) Destroy(_coneMat);
    }

    // ─── Public API (called by ConcertLightProgram) ───────────────────────────
    public void SetMode(StrobeLightMode newMode, float syncTime)
    {
        if (newMode == mode && Mathf.Approximately(syncTime, _modeStartTime)) return;
        mode = newMode;
        _modeStartTime = syncTime;
    }
    public void SetPalette(Color[] _) { /* white-only — palette ignored */ }
    public void SetMirrored(bool m)   { mirrorMotion = m; }
    public void SetIntensity(float v) { intensity = Mathf.Max(0f, v); }
    public void SetTier(ConcertLaser.IntensityTier t) { currentTier = t; }

    // ─── Audio accessors ──────────────────────────────────────────────────────
    bool AudioActive => _director != null && _director.IsPlaying;
    float Bass => AudioActive ? _director.Bass : 0f;
    int   KickCount  => _director != null ? _director.KickCount  : 0;
    int   SnareCount => _director != null ? _director.SnareCount : 0;
    float LastKickTime  => _director != null ? _director.LastKickTime  : -999f;
    float LastSnareTime => _director != null ? _director.LastSnareTime : -999f;
    float LastCrashTime => _director != null ? _director.LastCrashTime : -999f;
    float DetectedBpm   => _director != null && _director.DetectedBpm > 30f ? _director.DetectedBpm : 120f;
    float PredictedBeatPhase => _director != null ? _director.PredictedBeatPhase : 0f;
    int   BeatInBar => _director != null ? _director.BeatInBar : 0;

    static float DecayEnvelope(float lastTime, float tau) =>
        Time.time - lastTime < 0f || tau <= 0f ? 0f : Mathf.Exp(-(Time.time - lastTime) / tau);

    float SubBeatPhase(float divisions)
    {
        float p = mirrorMotion ? PredictedBeatPhase + 0.5f : PredictedBeatPhase;
        return Mathf.Repeat(p * divisions, 1f);
    }

    // ─── Update / dispatch ────────────────────────────────────────────────────
    void Update()
    {
        if (_light == null) return;

        ApplyShadowsSetting();

        // Hold during program-wide silence / pre-drop freeze: strobes go dark.
        var program = ConcertLightProgram.Instance;
        if (program != null && program.IsHolding)
        {
            float holdTarget = idleIntensity * 0.5f * intensity;
            _light.intensity = Mathf.MoveTowards(_light.intensity, holdTarget, Time.deltaTime * 30f);
            if (_coneRenderer != null) _coneRenderer.enabled = false;
            return;
        }

        // Sting override — cross-system unison (toned down so strobes don't blow out the screen).
        if (Time.time < _stingUntil)
        {
            _light.intensity = peakIntensity * intensity;
            _light.color = Color.white;
            _light.spotAngle = spotAngle;
            _light.innerSpotAngle = Mathf.Min(innerSpotAngle, spotAngle - 1f);
            _light.range = range;
            if (_coneRenderer != null && _coneMat != null && drawVisibleBeam)
            {
                _coneRenderer.enabled = true;
                Color tint = Color.white * beamBrightness;
                if (_coneMat.HasProperty(ConcertBeamShared.TintColorId)) _coneMat.SetColor(ConcertBeamShared.TintColorId, tint);
                if (_coneMat.HasProperty(ConcertBeamShared.ColorId))     _coneMat.SetColor(ConcertBeamShared.ColorId,     tint);
                if (_coneMat.HasProperty(ConcertBeamShared.BaseColorId)) _coneMat.SetColor(ConcertBeamShared.BaseColorId, tint);
            }
            return;
        }

        float pulse = 0f;
        switch (mode)
        {
            case StrobeLightMode.SlowPulse:       pulse = ModeSlowPulse();       break;
            case StrobeLightMode.BassBreath:      pulse = ModeBassBreath();      break;
            case StrobeLightMode.GentleFlash:     pulse = ModeGentleFlash();     break;
            case StrobeLightMode.BarKick:         pulse = ModeBarKick();         break;
            case StrobeLightMode.BeatStrobe:      pulse = ModeBeatStrobe();      break;
            case StrobeLightMode.BassDouble:      pulse = ModeBassDouble();      break;
            case StrobeLightMode.OnOffSync:       pulse = ModeOnOffSync();       break;
            case StrobeLightMode.AltBass:         pulse = ModeAltBass();         break;
            case StrobeLightMode.FastStrobe:      pulse = ModeFastStrobe();      break;
            case StrobeLightMode.BassMachineGun:  pulse = ModeBassMachineGun();  break;
            case StrobeLightMode.CountInPulse:    pulse = ModeCountInPulse();    break;
            case StrobeLightMode.RipplePulse:     pulse = ModeRipplePulse();     break;
            case StrobeLightMode.HardStrobe16th:  pulse = ModeHardStrobe16th();  break;
            case StrobeLightMode.BassChaos:       pulse = ModeBassChaos();       break;
            case StrobeLightMode.BlitzStrobe:     pulse = ModeBlitzStrobe();     break;
            case StrobeLightMode.AntiPhase:       pulse = ModeAntiPhase();       break;
        }

        // Crash boost — softened so cymbals don't pin every strobe at full intensity.
        float crash = DecayEnvelope(LastCrashTime, 0.15f) * 0.5f;
        pulse = Mathf.Max(pulse, crash);

        // Slow yaw drift: every Nth snare we pick a new target yaw (deterministic
        // from SnareCount so pair-mates stay in lockstep when mirrorMotion=false,
        // antiphase when mirrorMotion=true). Smoothly lerp toward target each frame.
        int snareCount = _director != null ? _director.SnareCount : 0;
        if (snareCount != _lastSnareCountForDrift)
        {
            _lastSnareCountForDrift = snareCount;
            if (snareCount > 0 && (snareCount % Mathf.Max(1, snaresPerYawRetarget)) == 0)
            {
                int seed = snareCount * 1289 + (mirrorMotion ? 17 : 0);
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                float u = (seed & 0xFFFF) / 65535f;          // 0..1
                _driftTargetYaw = (u * 2f - 1f) * yawDriftAmplitude;
            }
        }
        // Smooth approach to the target yaw (very low rate = barely-perceptible drift).
        Quaternion targetRot = _strobeBaseRot * Quaternion.Euler(0f, _driftTargetYaw, 0f);
        _strobeRig.localRotation = Quaternion.RotateTowards(_strobeRig.localRotation, targetRot,
                                                            driftRateDegreesPerSec * Time.deltaTime);

        // Continuous flow underlay disabled — was the main contributor to
        // "always on" feel. Strobes are now event-driven only (snare punches +
        // crash decay + occasional bass swells in BassBreath mode).

        // Breath pulse: small beat-locked boost on top of pulse (no effect when
        // pulse is 0 — strobes stay dark between hits).
        if (program != null && program.enableBreathPulse && AudioActive && _director != null)
            pulse = Mathf.Min(1f, pulse * (1f + program.breathDepth * _director.BeatPhase));

        // Pink Floyd-style restraint: strobes are SILENT in Light tier, very
        // subtle in Medium, active in Hard, full in Extreme. Verses get dark
        // stages; choruses get strobes — that's what makes the chorus feel
        // BIG. The audience hits the chorus and the strobes appear.
        float tierMul;
        switch (currentTier)
        {
            case ConcertLaser.IntensityTier.Light:   tierMul = 0f;    break;
            case ConcertLaser.IntensityTier.Medium:  tierMul = 0.25f; break;
            case ConcertLaser.IntensityTier.Hard:    tierMul = 0.75f; break;
            default:                                 tierMul = 1.0f;  break;  // Extreme
        }
        pulse *= tierMul;

        float target = (idleIntensity + Mathf.Clamp01(pulse) * (peakIntensity - idleIntensity)) * intensity;
        float rate = (target > _light.intensity) ? attackRate : decayRate;
        _light.intensity = Mathf.MoveTowards(_light.intensity, target, Time.deltaTime * rate);
        _light.color = Color.white;
        _light.spotAngle = spotAngle;
        _light.innerSpotAngle = Mathf.Min(innerSpotAngle, spotAngle - 1f);
        _light.range = range;

        if (_coneRenderer != null)
        {
            _coneRenderer.enabled = drawVisibleBeam;
            if (drawVisibleBeam && _coneMat != null)
            {
                float beamA = Mathf.Clamp01(_light.intensity / Mathf.Max(0.0001f, peakIntensity)) * beamBrightness;
                Color tint = new Color(beamA, beamA, beamA, beamA);
                if (_coneMat.HasProperty(ConcertBeamShared.TintColorId)) _coneMat.SetColor(ConcertBeamShared.TintColorId, tint);
                if (_coneMat.HasProperty(ConcertBeamShared.ColorId))     _coneMat.SetColor(ConcertBeamShared.ColorId,     tint);
                if (_coneMat.HasProperty(ConcertBeamShared.BaseColorId)) _coneMat.SetColor(ConcertBeamShared.BaseColorId, tint);
            }
        }
    }

    // ═══ LIGHT TIER ═══════════════════════════════════════════════════════════

    float ModeSlowPulse()
    {
        if (!AudioActive) return 0f;
        float p = SubBeatPhase(0.5f);
        float bpmPulse = p < 0.10f ? 0.5f : 0f;
        float kickPulse = DecayEnvelope(LastSnareTime, 0.25f) * strobeBassReactivity * 0.3f;
        return Mathf.Max(bpmPulse, kickPulse);
    }

    float ModeBassBreath()
    {
        // Was continuous = "always on." Now gated on bass threshold so strobes
        // are silent during quiet passages and only swell during loud bass.
        float bass = Bass;
        if (bass < 0.30f) return 0f;
        return Mathf.Clamp01((bass - 0.30f) / 0.40f) * 0.6f * strobeBassReactivity;
    }

    float ModeGentleFlash()
    {
        if (!AudioActive) return 0f;
        float p = SubBeatPhase(1f);
        float bpmPulse = Mathf.Clamp01(1f - p / 0.4f) * 0.5f;
        float kickPulse = DecayEnvelope(LastSnareTime, 0.20f) * strobeBassReactivity * 0.4f;
        return Mathf.Max(bpmPulse, kickPulse);
    }

    float ModeBarKick()
    {
        if (!AudioActive) return 0f;
        if (BeatInBar != 0) return 0f;
        return DecayEnvelope(LastSnareTime, 0.35f) * strobeBassReactivity;
    }

    // ═══ MEDIUM TIER ══════════════════════════════════════════════════════════

    float ModeBeatStrobe()
    {
        if (!AudioActive) return 0f;
        float p = SubBeatPhase(1f);
        float bpmPulse = p < 0.15f ? 1f : 0f;
        float kickPulse = DecayEnvelope(LastSnareTime, 0.10f) * strobeBassReactivity;
        return Mathf.Max(bpmPulse, kickPulse);
    }

    float ModeBassDouble()
    {
        float since = Time.time - LastSnareTime;
        float p = 0f;
        if (since >= 0f && since < 0.06f) p = 1f - since / 0.06f;
        else if (since >= 0.10f && since < 0.16f) p = 1f - (since - 0.10f) / 0.06f;
        return p * strobeBassReactivity;
    }

    float ModeOnOffSync()
    {
        // Pair A flashes on even snares, pair B on odd (mirrorMotion).
        // Strobe is dark whenever it's the OTHER pair's snare.
        bool oddSnare = (SnareCount & 1) == 1;
        bool light = mirrorMotion ? oddSnare : !oddSnare;
        if (!light) return 0f;
        return DecayEnvelope(LastSnareTime, 0.18f) * strobeBassReactivity;
    }

    float ModeAltBass()
    {
        bool oddKick = (SnareCount & 1) == 1;
        bool light = mirrorMotion ? oddKick : !oddKick;
        if (!light) return 0f;
        return DecayEnvelope(LastSnareTime, 0.18f) * strobeBassReactivity;
    }

    // ═══ HARD TIER ════════════════════════════════════════════════════════════

    float ModeFastStrobe()
    {
        if (!AudioActive) return 0f;
        float p = SubBeatPhase(2f);
        float bpmPulse = p < 0.20f ? 0.7f : 0f;
        float kickPulse = DecayEnvelope(LastSnareTime, 0.10f) * strobeBassReactivity;
        return Mathf.Max(bpmPulse, kickPulse);
    }

    float ModeBassMachineGun()
    {
        float since = Time.time - LastSnareTime;
        if (since < 0f || since > 0.32f) return 0f;
        const float windowSize = 0.080f;
        float idx = Mathf.Floor(since / windowSize);
        if (idx > 3f) return 0f;
        float frac = (since - idx * windowSize) / windowSize;
        return (frac < 0.4f ? 1f : 0f) * strobeBassReactivity;
    }

    float ModeCountInPulse()
    {
        if (!AudioActive) return 0f;
        float p = SubBeatPhase(1f);
        if (BeatInBar < 3) return p < 0.10f ? 1f : 0f;
        return p < 0.40f ? 1f : 0f;
    }

    float ModeRipplePulse()
    {
        float offset = mirrorMotion ? 0.080f : 0f;
        float since = Time.time - LastSnareTime - offset;
        if (since < 0f || since > 0.20f) return 0f;
        return Mathf.Clamp01(1f - since / 0.15f) * strobeBassReactivity;
    }

    // ═══ EXTREME TIER ═════════════════════════════════════════════════════════

    float ModeHardStrobe16th()
    {
        // Fast 1/16 burst for 280 ms after each snare, then DARK until the next.
        float since = Time.time - LastSnareTime;
        if (since < 0f || since > 0.28f) return 0f;
        const float period = 0.062f;   // ~1/16 at 120bpm
        float frac = (since % period) / period;
        return (frac < 0.45f ? 1f : 0f) * strobeBassReactivity;
    }

    float ModeBassChaos()
    {
        if (!AudioActive) return 0f;
        int seed = SnareCount * 1493 + (mirrorMotion ? 7 : 0);
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        float div = ((seed & 0x3) + 1) * 2f;
        float p = SubBeatPhase(div);
        float bpmPulse = p < 0.25f ? 1f : 0f;
        float kickPulse = DecayEnvelope(LastSnareTime, 0.08f);
        return Mathf.Max(bpmPulse, kickPulse * strobeBassReactivity);
    }

    float ModeBlitzStrobe()
    {
        float since = Time.time - LastSnareTime;
        if (since < 0f || since > 0.25f) return 0f;
        const float period = 0.030f;
        float frac = (since % period) / period;
        return (frac < 0.4f ? 1f : 0f) * strobeBassReactivity;
    }

    float ModeAntiPhase()
    {
        // 1/16 burst after snare, pair B offset by 30 ms for visible ping-pong.
        float offset = mirrorMotion ? 0.030f : 0f;
        float since = Time.time - LastSnareTime - offset;
        if (since < 0f || since > 0.25f) return 0f;
        const float period = 0.062f;
        float frac = (since % period) / period;
        return (frac < 0.45f ? 1f : 0f) * strobeBassReactivity;
    }
}
