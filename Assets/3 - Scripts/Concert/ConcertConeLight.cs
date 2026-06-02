using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Cone-light effect engine — drop on a GameObject (or use ConcertLightProgram's
// auto-attach by name) to get a Unity Spot light that pans and color-cycles
// in time with the music. 16 distinct modes split into 4 intensity tiers
// (Light/Medium/Hard/Extreme); the ConcertLightProgram chooses tier from song
// energy and rotates looks within the tier on every Nth hi-hat hit.
//
// Rig structure (built in EnsureRig, runs at Awake / via the editor menu):
//   <this GO>                    ← parent. Has the visible mesh model. NEVER rotated by us.
//     _ConeRig                   ← child. Holds the Light + visible cone mesh. Rotated by Update.
//       Light (Spot)
//       _ConeVisual              ← grandchild. Procedural cone mesh + additive material.
//
// We rotate _ConeRig (not transform) so the parent's visible spotlight model
// doesn't swing around — only the cone of light moves.
public class ConcertConeLight : MonoBehaviour
{
    public enum ConeLightMode
    {
        // LIGHT tier — slow, deliberate, soft color
        GentleHover, ColorBreath, SlowSweep, AmberHold,
        // MEDIUM tier — moderate motion, palette cycling
        PalettePan, ColorChase, FigureEight, AlternateColors,
        // HARD tier — snappy motion, fast color steps locked to hi-hats
        SnapStep, ZigZag, RotateColors, ColorPunch,
        // EXTREME tier — chaotic, rapid color
        WildSweep, ColorChaos, FastSpiral, RaveCycle,
    }

    public enum PairId { Solo, A, B }

    static readonly Dictionary<ConeLightMode, ConcertLaser.IntensityTier> s_modeTier = new Dictionary<ConeLightMode, ConcertLaser.IntensityTier>
    {
        { ConeLightMode.GentleHover,     ConcertLaser.IntensityTier.Light },
        { ConeLightMode.ColorBreath,     ConcertLaser.IntensityTier.Light },
        { ConeLightMode.SlowSweep,       ConcertLaser.IntensityTier.Light },
        { ConeLightMode.AmberHold,       ConcertLaser.IntensityTier.Light },
        { ConeLightMode.PalettePan,      ConcertLaser.IntensityTier.Medium },
        { ConeLightMode.ColorChase,      ConcertLaser.IntensityTier.Medium },
        { ConeLightMode.FigureEight,     ConcertLaser.IntensityTier.Medium },
        { ConeLightMode.AlternateColors, ConcertLaser.IntensityTier.Medium },
        { ConeLightMode.SnapStep,        ConcertLaser.IntensityTier.Hard },
        { ConeLightMode.ZigZag,          ConcertLaser.IntensityTier.Hard },
        { ConeLightMode.RotateColors,    ConcertLaser.IntensityTier.Hard },
        { ConeLightMode.ColorPunch,      ConcertLaser.IntensityTier.Hard },
        { ConeLightMode.WildSweep,       ConcertLaser.IntensityTier.Extreme },
        { ConeLightMode.ColorChaos,      ConcertLaser.IntensityTier.Extreme },
        { ConeLightMode.FastSpiral,      ConcertLaser.IntensityTier.Extreme },
        { ConeLightMode.RaveCycle,       ConcertLaser.IntensityTier.Extreme },
    };
    public static ConcertLaser.IntensityTier TierOf(ConeLightMode m) =>
        s_modeTier.TryGetValue(m, out var t) ? t : ConcertLaser.IntensityTier.Medium;

    [Header("Mode")]
    public ConeLightMode mode = ConeLightMode.GentleHover;

    [Header("Pair (driven by ConcertLightProgram if not Solo)")]
    public PairId pair = PairId.Solo;

    [Header("Light")]
    public float spotAngle = 30f;
    public float innerSpotAngle = 12f;
    public float range = 40f;
    public Color baseColor = new Color(0.6f, 0.8f, 1f, 1f);

    [Header("Cone Aim (rest pose of the cone — parent transform is never moved)")]
    [Tooltip("Local offset of the cone emitter from the GameObject origin. Move down/forward to align with the spotlight model's actual lens position.")]
    public Vector3 coneEmitterLocalOffset = new Vector3(0f, -0.55f, 0.2f);
    [Tooltip("Initial Euler rotation of the cone rig. Default pitches the cone down toward the stage so it actually hits something.")]
    public Vector3 coneRestEuler = new Vector3(35f, 0f, 0f);

    [Header("Intensity")]
    public float idleIntensity = 0.2f;
    [Tooltip("Peak Unity Light intensity. Keep low (3-5) to avoid washing the ground white via tonemapping. Visible cone mesh handles the visual punch independently — it scales by intensity/peakIntensity ratio, so reducing both proportionally keeps the cone visible while killing ground saturation.")]
    public float peakIntensity = 4f;

    [Header("Visible Beam")]
    [Tooltip("Show the visible cone mesh (additive transparent). Off = invisible Light only — illuminates surfaces but no beam visible in air.")]
    public bool drawVisibleBeam = true;
    [Tooltip("Brightness multiplier for the visible cone mesh. Lower if the beam looks washed out; raise if it looks too dim.")]
    [Range(0f, 4f)] public float beamBrightness = 0.8f;

    [Header("Program-Driven (set by ConcertLightProgram)")]
    public Color[] palette;
    public bool mirrorMotion;
    [Range(0f, 4f)] public float intensity = 1f;
    [Tooltip("Advisory — the program tracks its own tier. This is just a hint for inspector display.")]
    public ConcertLaser.IntensityTier currentTier = ConcertLaser.IntensityTier.Medium;

    [Header("Audio Reactivity")]
    [Range(0f, 1f)] public float audioReactivity = 1f;
    [Range(0f, 6f)] public float kickBrightnessBoost = 2f;

    [Header("Motion Smoothing")]
    [Tooltip("Max degrees per second the cone rig is allowed to rotate. Lower = smoother (mode transitions/snap modes ease into pose). Higher = snappier. 280 = ~180ms for a 50° snap.")]
    [Range(60f, 1440f)] public float motionSmoothness = 280f;

    Transform _coneRig;
    Light _light;
    MeshRenderer _coneRenderer;
    Material _coneMat;
    ConcertAudioDirector _director;
    Quaternion _baseRigRot = Quaternion.identity;
    float _modeStartTime;
    float _stingUntil = -999f;       // When set in the future, override mode dispatch with sting pose.

    // Called by ConcertLightProgram on cross-system unison stings (kick+snare+
    // crash co-occurring). Overrides the cone for 200 ms with a forward-up pose
    // and a bright white burst.
    public void TriggerSting() { _stingUntil = Time.time + 0.20f; }

    // Snapshot state for pose-snap effects (continuous-motion modes use
    // BarPhase directly via RSin/RCos — no accumulator needed).
    int _lastSeenHihatForSnap = -1;
    int _lastSeenHihatForChaos = -1;
    Vector2 _snapStepPose;
    Vector2 _chaosPose;

    void Awake()
    {
        EnsureRig();
        _baseRigRot = _coneRig.localRotation;
    }

    // Live-poll the InputSettings.fxConcertShadows toggle and update the
    // Unity Light's shadow mode accordingly. Default is None (matches the
    // hard-coded value previously set in EnsureRig). When the user flips the
    // toggle in the pause menu's GRAPHICS tab, this picks it up on the next
    // Update tick without needing to rebuild the rig.
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

    // Builds the rig sub-hierarchy: _ConeRig (with Light) + _ConeVisual (cone
    // mesh). Idempotent: if children already exist (e.g., the editor menu
    // pre-built them) it reuses them. Public so the editor menu can call this
    // at edit time to persist the children in the scene file.
    public void EnsureRig()
    {
        var rigT = transform.Find("_ConeRig");
        if (rigT == null)
        {
            var rigGO = new GameObject("_ConeRig");
            rigGO.transform.SetParent(transform, worldPositionStays: false);
            rigGO.transform.localRotation = Quaternion.Euler(coneRestEuler);
            rigT = rigGO.transform;
        }
        _coneRig = rigT;
        // Always sync rig local position + rotation to the inspector fields, so
        // editing them moves / aims the cone without scene re-setup.
        _coneRig.localPosition = coneEmitterLocalOffset;
        _coneRig.localRotation = Quaternion.Euler(coneRestEuler);
        _baseRigRot = _coneRig.localRotation;

        _light = _coneRig.GetComponent<Light>();
        if (_light == null) _light = _coneRig.gameObject.AddComponent<Light>();
        _light.type = LightType.Spot;
        _light.color = baseColor;
        _light.intensity = idleIntensity;
        _light.spotAngle = spotAngle;
        _light.innerSpotAngle = Mathf.Min(innerSpotAngle, spotAngle - 1f);
        _light.range = range;
        _light.shadows = LightShadows.None;
        // ForcePixel — keep the cone shape stable when many stage lights are in
        // frame. Default Auto demoted some to per-vertex/SH as the camera turned,
        // which made the cone lose its falloff and bloom across the ground.
        _light.renderMode = LightRenderMode.ForcePixel;

        var visualT = _coneRig.Find("_ConeVisual");
        GameObject visualGO;
        if (visualT == null)
        {
            visualGO = new GameObject("_ConeVisual");
            visualGO.transform.SetParent(_coneRig, worldPositionStays: false);
            visualGO.transform.localPosition = Vector3.zero;
            visualGO.transform.localRotation = Quaternion.identity;
        }
        else
        {
            visualGO = visualT.gameObject;
        }
        var mf = visualGO.GetComponent<MeshFilter>();
        if (mf == null) mf = visualGO.AddComponent<MeshFilter>();
        // Always rebuild — cheap, and ensures the mesh matches current
        // spotAngle/range and current ConcertBeamShared layout (3-layer cone).
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
    public void SetMode(ConeLightMode newMode, float syncTime)
    {
        if (newMode == mode && Mathf.Approximately(syncTime, _modeStartTime)) return;
        mode = newMode;
        _modeStartTime = syncTime;
        _lastSeenHihatForSnap = -1;
        _lastSeenHihatForChaos = -1;
    }
    public void SetPalette(Color[] p)               { palette = p; }
    public void SetMirrored(bool m)                 { mirrorMotion = m; }
    public void SetIntensity(float v)               { intensity = Mathf.Max(0f, v); }
    public void SetTier(ConcertLaser.IntensityTier t) { currentTier = t; }

    // ─── Audio accessors with graceful fallback ───────────────────────────────
    bool AudioActive => _director != null && _director.IsPlaying && audioReactivity > 0f;
    float Bass    => AudioActive ? _director.Bass    : 0f;
    float Kick    => AudioActive ? _director.Kick    : 0f;
    float BeatPhase => AudioActive ? _director.BeatPhase : 0f;
    int   HihatCount => _director != null ? _director.HihatCount : 0;
    float LastHihatTime => _director != null ? _director.LastHihatTime : -999f;
    float LastCrashTime => _director != null ? _director.LastCrashTime : -999f;
    float DetectedBpm   => _director != null && _director.DetectedBpm > 30f ? _director.DetectedBpm : 120f;
    float Thump => Mathf.Max(Kick, BeatPhase);
    float CrashBoost => DecayEnvelope(LastCrashTime, 0.18f) * audioReactivity;

    static float DecayEnvelope(float lastTime, float tau) =>
        Time.time - lastTime < 0f || tau <= 0f ? 0f : Mathf.Exp(-(Time.time - lastTime) / tau);

    // Beat-locked phase helpers. When the program's enableBeatLockMotion is on
    // AND audio is playing AND BPM has been detected, motion phase locks to
    // the bar (peaks land exactly on beats). Otherwise falls back to time-
    // based math at a default 2-second-per-bar (~120bpm) so silent scenes
    // still animate smoothly.
    bool UseBeatLock => (ConcertLightProgram.Instance == null || ConcertLightProgram.Instance.enableBeatLockMotion);
    float BarLockedPhase(float cyclesPerBar)
    {
        if (UseBeatLock && AudioActive && _director.DetectedBpm >= 30f)
            return Mathf.Repeat(_director.BarPhase * cyclesPerBar, 1f);
        return Mathf.Repeat((Time.time - _modeStartTime) * cyclesPerBar * 0.5f, 1f);
    }
    float RSin(float cyclesPerBar, float phaseOffset = 0f) =>
        Mathf.Sin((BarLockedPhase(cyclesPerBar) + phaseOffset) * Mathf.PI * 2f);
    float RCos(float cyclesPerBar, float phaseOffset = 0f) =>
        Mathf.Cos((BarLockedPhase(cyclesPerBar) + phaseOffset) * Mathf.PI * 2f);

    Color PaletteColor(int idx, Color fallback)
    {
        if (palette == null || palette.Length == 0) return fallback;
        int n = palette.Length;
        return palette[((idx % n) + n) % n];
    }
    Color StepColorOnCount(int count, Color fallback)
    {
        if (palette != null && palette.Length > 0)
        {
            int n = palette.Length;
            return palette[((count % n) + n) % n];
        }
        Color.RGBToHSV(baseColor, out float h0, out _, out _);
        float h = Mathf.Repeat(h0 + count * 0.166f, 1f);
        return Color.HSVToRGB(h, 1f, 1f);
    }

    Quaternion YawPitch(float yawDeg, float pitchDeg)
    {
        float sign = mirrorMotion ? -1f : 1f;
        return Quaternion.Euler(pitchDeg, yawDeg * sign, 0f);
    }
    int MirroredIndex(int idx) => mirrorMotion ? idx + 1 : idx;

    // ─── Update / dispatch ────────────────────────────────────────────────────
    void Update()
    {
        if (_coneRig == null || _light == null) return;

        ApplyShadowsSetting();

        // Hold pose during program-wide silence or pre-drop freeze.
        // Cones snap to rest, dim toward the floor program-level intensity
        // multiplier, no color cycling. The drop / silence-end release is
        // what bursts us out. Cone renderer stays enabled so the visible
        // beam dims with the light instead of vanishing entirely (without
        // this the cone disappeared whenever the choreography engaged a
        // hold, which read as "the lights stopped working").
        var program = ConcertLightProgram.Instance;
        if (program != null && program.IsHolding)
        {
            _coneRig.localRotation = _baseRigRot;
            _light.color = baseColor;
            float holdTarget = idleIntensity * intensity;
            _light.intensity = Mathf.MoveTowards(_light.intensity, holdTarget, Time.deltaTime * 30f);
            if (_coneRenderer != null) _coneRenderer.enabled = drawVisibleBeam;
            return;
        }

        float t = Time.time - _modeStartTime;
        Quaternion offset = Quaternion.identity;
        Color color = baseColor;
        float bright = idleIntensity;

        switch (mode)
        {
            case ConeLightMode.GentleHover:     UpdateGentleHover(t,   ref offset, ref color, ref bright); break;
            case ConeLightMode.ColorBreath:     UpdateColorBreath(t,   ref offset, ref color, ref bright); break;
            case ConeLightMode.SlowSweep:       UpdateSlowSweep(t,     ref offset, ref color, ref bright); break;
            case ConeLightMode.AmberHold:       UpdateAmberHold(t,     ref offset, ref color, ref bright); break;
            case ConeLightMode.PalettePan:      UpdatePalettePan(t,    ref offset, ref color, ref bright); break;
            case ConeLightMode.ColorChase:      UpdateColorChase(t,    ref offset, ref color, ref bright); break;
            case ConeLightMode.FigureEight:     UpdateFigureEight(t,   ref offset, ref color, ref bright); break;
            case ConeLightMode.AlternateColors: UpdateAlternate(t,     ref offset, ref color, ref bright); break;
            case ConeLightMode.SnapStep:        UpdateSnapStep(t,      ref offset, ref color, ref bright); break;
            case ConeLightMode.ZigZag:          UpdateZigZag(t,        ref offset, ref color, ref bright); break;
            case ConeLightMode.RotateColors:    UpdateRotateColors(t,  ref offset, ref color, ref bright); break;
            case ConeLightMode.ColorPunch:      UpdateColorPunch(t,    ref offset, ref color, ref bright); break;
            case ConeLightMode.WildSweep:       UpdateWildSweep(t,     ref offset, ref color, ref bright); break;
            case ConeLightMode.ColorChaos:      UpdateColorChaos(t,    ref offset, ref color, ref bright); break;
            case ConeLightMode.FastSpiral:      UpdateFastSpiral(t,    ref offset, ref color, ref bright); break;
            case ConeLightMode.RaveCycle:       UpdateRaveCycle(t,     ref offset, ref color, ref bright); break;
        }

        // Continuous flow: a soft mid-band brightness underlay that fills the
        // silence between drum events. Plus a subtle treble-driven hue
        // shimmer so the cone feels alive during hi-hat-heavy passages.
        bool flowOn = (ConcertLightProgram.Instance == null || ConcertLightProgram.Instance.enableContinuousFlow);
        if (flowOn && AudioActive)
        {
            bright += _director.Mid * audioReactivity * (peakIntensity - idleIntensity) * 0.15f;
            float tShim = _director.Treble * audioReactivity * Time.deltaTime * 0.4f;
            if (tShim > 0.0001f)
            {
                Color.RGBToHSV(color, out float h, out float s, out float v);
                h = Mathf.Repeat(h + tShim, 1f);
                color = Color.HSVToRGB(h, s, v);
            }
        }

        // Crash + sting brightness accents — keep the current palette color
        // (don't force white). Crash decays over ~180ms; sting is a 200ms boost.
        bright += CrashBoost * (peakIntensity - idleIntensity) * 0.25f;
        if (Time.time < _stingUntil) bright += (peakIntensity - idleIntensity) * 0.4f;

        // Breath pulse: subtle beat-locked brightness ripple (5-10% boost on each
        // beat, decays over ~0.35s). Subliminal sync — the room feels alive.
        if (program != null && program.enableBreathPulse && AudioActive)
            bright *= 1f + program.breathDepth * BeatPhase * audioReactivity;

        // Apply offset to the rig (NOT the parent transform — the visible
        // spotlight model stays put; only the cone direction moves). Rate-clamped
        // via motionSmoothness so mode transitions and snap-modes ease into the
        // new pose instead of teleporting.
        Quaternion targetRot = _baseRigRot * offset;
        _coneRig.localRotation = Quaternion.RotateTowards(_coneRig.localRotation, targetRot,
                                                          motionSmoothness * Time.deltaTime);

        _light.color = color;
        float target = bright * intensity;
        float rate = (target > _light.intensity) ? 80f : 20f;
        _light.intensity = Mathf.MoveTowards(_light.intensity, target, Time.deltaTime * rate);
        _light.spotAngle = spotAngle;
        _light.innerSpotAngle = Mathf.Min(innerSpotAngle, spotAngle - 1f);
        _light.range = range;

        // Update visible beam color/brightness directly on the per-instance material.
        if (_coneRenderer != null)
        {
            _coneRenderer.enabled = drawVisibleBeam;
            if (drawVisibleBeam && _coneMat != null)
            {
                float beamA = Mathf.Clamp01(_light.intensity / Mathf.Max(0.0001f, peakIntensity)) * beamBrightness;
                Color tint = new Color(color.r * beamA, color.g * beamA, color.b * beamA, beamA);
                if (_coneMat.HasProperty(ConcertBeamShared.TintColorId)) _coneMat.SetColor(ConcertBeamShared.TintColorId, tint);
                if (_coneMat.HasProperty(ConcertBeamShared.ColorId))     _coneMat.SetColor(ConcertBeamShared.ColorId,     tint);
                if (_coneMat.HasProperty(ConcertBeamShared.BaseColorId)) _coneMat.SetColor(ConcertBeamShared.BaseColorId, tint);
            }
        }
    }

    // ═══ LIGHT TIER ═══════════════════════════════════════════════════════════

    void UpdateGentleHover(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 1 sweep over 8 bars — very slow lazy hover.
        float yaw   = RSin(0.125f) * 6f;
        float pitch = RSin(0.165f, 0.08f) * 3f;
        offset = YawPitch(yaw, pitch);
        color = PaletteColor(0, baseColor);
        bright = idleIntensity + Bass * audioReactivity * (peakIntensity - idleIntensity) * 0.4f;
    }

    void UpdateColorBreath(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        offset = YawPitch(RSin(0.5f) * 2f, 0f);
        color = StepColorOnCount(MirroredIndex(HihatCount / 2), baseColor);
        bright = idleIntensity + Bass * audioReactivity * (peakIntensity - idleIntensity) * 0.5f
                 + Thump * audioReactivity * idleIntensity * 0.4f;
    }

    void UpdateSlowSweep(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 1 sweep per 4 bars. Color steps on every 4th hi-hat.
        float yaw = RSin(0.25f) * 14f;
        offset = YawPitch(yaw, 0f);
        color = StepColorOnCount(MirroredIndex(HihatCount / 4), baseColor);
        bright = idleIntensity + Thump * audioReactivity * (peakIntensity - idleIntensity) * 0.4f;
    }

    void UpdateAmberHold(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        offset = Quaternion.identity;
        color = PaletteColor(0, new Color(1f, 0.6f, 0.2f));
        bright = idleIntensity + Kick * audioReactivity * (peakIntensity - idleIntensity) * 0.5f;
    }

    // ═══ MEDIUM TIER ══════════════════════════════════════════════════════════

    void UpdatePalettePan(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 1 sweep per 2 bars — slower than v1, peak at end of bar 1 / bar 2.
        float yaw   = RSin(0.5f) * 16f;
        float pitch = RSin(0.25f, 0.08f) * 5f;
        offset = YawPitch(yaw, pitch);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        bright = idleIntensity + Thump * audioReactivity * kickBrightnessBoost * (peakIntensity - idleIntensity) * 0.25f;
    }

    void UpdateColorChase(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // Slow sawtooth — 1 cycle per 2 bars (resets every 2 bars).
        float saw = BarLockedPhase(0.5f);
        float yaw = (saw - 0.5f) * 28f;
        float pitch = RSin(1f) * 4f;
        offset = YawPitch(yaw, pitch);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        bright = idleIntensity + Thump * audioReactivity * kickBrightnessBoost * (peakIntensity - idleIntensity) * 0.3f;
    }

    void UpdateFigureEight(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 1:2 frequency ratio — full figure-8 over 2 bars (lazy).
        float yaw   = RSin(0.5f) * 14f;
        float pitch = RSin(1f) * 6f;
        offset = YawPitch(yaw, pitch);
        color = StepColorOnCount(MirroredIndex(HihatCount / 2), baseColor);
        bright = idleIntensity + Thump * audioReactivity * kickBrightnessBoost * (peakIntensity - idleIntensity) * 0.3f;
    }

    void UpdateAlternate(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        bool oddHihat = (HihatCount & 1) == (mirrorMotion ? 1 : 0);
        float yaw = oddHihat ? -15f : 15f;
        offset = YawPitch(yaw, 0f);
        int idx = oddHihat ? 0 : 1;
        color = PaletteColor(idx, baseColor);
        bright = idleIntensity + DecayEnvelope(LastHihatTime, 0.20f) * (peakIntensity - idleIntensity) * 0.7f;
    }

    // ═══ HARD TIER ════════════════════════════════════════════════════════════

    void UpdateSnapStep(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // Advance pose every 4th hi-hat (was every hi-hat — too twitchy).
        int snapIdx = HihatCount / 4;
        if (snapIdx != _lastSeenHihatForSnap)
        {
            _lastSeenHihatForSnap = snapIdx;
            int idx = MirroredIndex(snapIdx) & 3;
            float[] poses = { -16f, -5f, 5f, 16f };
            _snapStepPose = new Vector2(poses[idx], 0f);
        }
        offset = YawPitch(_snapStepPose.x, _snapStepPose.y);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        bright = idleIntensity * 0.8f + DecayEnvelope(LastHihatTime, 0.12f) * (peakIntensity - idleIntensity)
                 + Thump * audioReactivity * (peakIntensity - idleIntensity) * 0.2f;
    }

    void UpdateZigZag(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 1 zig per bar (was 2 per bar) — slower triangle wave from BarPhase.
        float tri = Mathf.PingPong(BarLockedPhase(1f) * 2f, 1f) * 2f - 1f;
        float yaw = tri * 18f;
        float pitch = RSin(2f) * 4f;
        offset = YawPitch(yaw, pitch);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        bright = idleIntensity + Thump * audioReactivity * kickBrightnessBoost * (peakIntensity - idleIntensity) * 0.4f;
    }

    void UpdateRotateColors(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 1 full circle per 2 bars — slower than v1, gentler radii.
        float dir = mirrorMotion ? -1f : 1f;
        float ang = BarLockedPhase(0.5f) * Mathf.PI * 2f * dir;
        float yaw   = Mathf.Cos(ang) * 12f;
        float pitch = Mathf.Sin(ang) * 7f;
        offset = Quaternion.Euler(pitch, yaw, 0f);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        bright = idleIntensity + Thump * audioReactivity * kickBrightnessBoost * (peakIntensity - idleIntensity) * 0.4f;
    }

    void UpdateColorPunch(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        offset = YawPitch(RSin(0.25f) * 5f, 0f);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        float decay = DecayEnvelope(LastHihatTime, 0.15f);
        bright = idleIntensity * 0.5f + decay * (peakIntensity - idleIntensity) * 1.2f
                 + Thump * audioReactivity * (peakIntensity - idleIntensity) * 0.2f;
    }

    // ═══ EXTREME TIER ═════════════════════════════════════════════════════════

    void UpdateWildSweep(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 1 sweep per bar (was 2) — narrower amplitude so cones stay aimed at stage.
        float yaw   = RSin(1f) * 25f;
        float pitch = RSin(1.3f, 0.07f) * 8f;
        offset = YawPitch(yaw, pitch);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        bright = idleIntensity + Thump * audioReactivity * kickBrightnessBoost * (peakIntensity - idleIntensity) * 0.5f;
    }

    void UpdateColorChaos(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // Re-roll pose every 2 hi-hats (was every hi-hat) with tighter range.
        int chaosIdx = HihatCount / 2;
        if (chaosIdx != _lastSeenHihatForChaos)
        {
            _lastSeenHihatForChaos = chaosIdx;
            int seed = chaosIdx * 7919 + (mirrorMotion ? 31 : 0);
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            float yaw   = ((seed & 0xFFFF) / 65535f * 50f) - 25f;
            float pitch = (((seed >> 16) & 0xFFFF) / 65535f * 14f) - 7f;
            _chaosPose = new Vector2(yaw, pitch);
        }
        offset = Quaternion.Euler(_chaosPose.y, _chaosPose.x, 0f);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        float decay = DecayEnvelope(LastHihatTime, 0.18f);
        bright = idleIntensity + decay * (peakIntensity - idleIntensity)
                 + Thump * audioReactivity * (peakIntensity - idleIntensity) * 0.3f;
    }

    void UpdateFastSpiral(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // Spiral resets every 2 bars (was every bar). Pitch tightened so cones don't aim at floor/sky.
        float u = BarLockedPhase(0.5f);
        float ang = u * Mathf.PI * 2f * (mirrorMotion ? -1f : 1f);
        float yawRadius   = u * 18f;
        float pitchRadius = u * 7f;
        float yaw   = Mathf.Cos(ang) * yawRadius;
        float pitch = Mathf.Sin(ang) * pitchRadius;
        offset = Quaternion.Euler(pitch, yaw, 0f);
        color = StepColorOnCount(MirroredIndex(HihatCount), baseColor);
        bright = idleIntensity + (1f - u) * (peakIntensity - idleIntensity) * 0.6f
                 + Thump * audioReactivity * (peakIntensity - idleIntensity) * 0.3f;
    }

    void UpdateRaveCycle(float t, ref Quaternion offset, ref Color color, ref float bright)
    {
        // 2 sweeps per bar (was 4) — tighter pitch so cones don't pitch up into sky.
        float yaw   = RSin(2f) * 20f;
        float pitch = RCos(2.6f) * 7f;
        offset = YawPitch(yaw, pitch);
        color = StepColorOnCount(MirroredIndex(HihatCount * 2), baseColor);
        bright = idleIntensity + Thump * audioReactivity * kickBrightnessBoost * (peakIntensity - idleIntensity) * 0.7f;
    }
}
