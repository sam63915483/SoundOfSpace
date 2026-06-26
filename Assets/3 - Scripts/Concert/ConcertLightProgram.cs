using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Drives the entire concert show — lasers, cone lights, and strobes:
//
// 1. TIER selection — picks Light/Medium/Hard/Extreme each frame based on the
//    song's smoothed energy level. Tier is shared across all three light
//    systems so they escalate together. Each tier has its own Look[] / ConeLook[]
//    / StrobeLook[] pool.
//
// 2. PINK FLOYD SEPARATION — each system advances its look on a different drum
//    part so the show feels independently choreographed:
//      • Lasers   → look swap on cymbal CRASHES (rare, high-impact punctuation)
//      • Cones    → look swap on every Nth HI-HAT (frequent, density-driven)
//      • Strobes  → look swap on every Nth KICK (steady tempo grid)
//    Within each pool, looks cycle through inspector-authored (pairA, pairB,
//    palette, mirror) combinations.
//
// 3. DROP reactions — drops force-jump to Extreme tier and to each pool's
//    designated drop look (full-screen flash + tier boost). Crashes also flash
//    + boost tier to Extreme but advance the laser look rather than jumping.
//
// 4. HI-HAT density bias — when the hat pattern is busy (>5/sec), bias the
//    energy figure upward so songs with steady kicks but intensifying hat
//    patterns still escalate to Hard/Extreme.
//
// 5. BLACKOUT windows — looks can specify "hold lasers dark for N beats then
//    kick in" to create anticipation.
public class ConcertLightProgram : MonoBehaviour
{
    public static ConcertLightProgram Instance { get; private set; }

    [Serializable]
    public struct Look
    {
        public ConcertLaser.LaserMode pairA;
        public ConcertLaser.LaserMode pairB;
        [Range(2, 64)] public int beatDuration;
        public Color[] palette;
        public bool mirrorPairB;
        [Range(0, 8)] public int blackoutBeats;
        public bool isDropLook;
    }

    // Cone-light look — same shape as Look, but typed for ConcertConeLight modes.
    // beatDuration is unused (cones advance on hi-hats, not beats) but kept for
    // inspector parity with Look.
    [Serializable]
    public struct ConeLook
    {
        public ConcertConeLight.ConeLightMode pairA;
        public ConcertConeLight.ConeLightMode pairB;
        public Color[] palette;
        public bool mirrorPairB;
        public bool isDropLook;
    }

    // Strobe-light look — no palette (strobes are white-locked); mirrorPairB
    // antiphases the pair (180° offset) for ping-pong patterns.
    [Serializable]
    public struct StrobeLook
    {
        public ConcertStrobeLight.StrobeLightMode pairA;
        public ConcertStrobeLight.StrobeLightMode pairB;
        public bool mirrorPairB;
        public bool isDropLook;
    }

    // Color family pools. Palettes are grouped by song section so the show
    // holds a coherent color through a verse / chorus, only shifting on
    // section transitions instead of every look-cycle. Wrapper struct because
    // Unity's serializer doesn't handle Color[][] directly.
    [Serializable] public struct PaletteEntry { public Color[] palette; }
    public enum ColorFamily { LowEnergy, MidEnergy, HighEnergy, Drop }

    // ─── Default palettes ────────────────────────────────────────────────────
    static readonly Color[] PaletteCool   = { new Color(0.1f, 0.5f, 1f),  new Color(0.4f, 0f, 1f),    new Color(0f, 1f, 0.7f) };
    static readonly Color[] PaletteWarm   = { new Color(1f, 0.4f, 0.1f), new Color(1f, 0.8f, 0.2f),  new Color(1f, 0.2f, 0.5f) };
    static readonly Color[] PaletteHot    = { new Color(1f, 0.15f, 0.05f), new Color(1f, 0.6f, 0f),  new Color(1f, 0f, 0.4f) };
    static readonly Color[] PaletteUV     = { new Color(0.7f, 0f, 1f), new Color(1f, 0f, 0.7f),     new Color(0.4f, 0f, 1f) };
    static readonly Color[] PaletteRGB    = { Color.red, Color.green, Color.blue };
    static readonly Color[] PaletteWhite  = { Color.white, new Color(0.9f, 0.95f, 1f),              new Color(1f, 0.95f, 0.85f) };
    static readonly Color[] PaletteAcidic = { new Color(0f, 1f, 0.2f),  new Color(1f, 1f, 0f),       new Color(0f, 0.8f, 1f) };
    static readonly Color[] PaletteAmber  = { new Color(1f, 0.6f, 0.2f), new Color(1f, 0.85f, 0.5f) };
    static readonly Color[] PaletteIce    = { new Color(0.6f, 0.9f, 1f), new Color(0.85f, 1f, 1f) };

    [Header("LIGHT-tier pool (calm sections — slow, deliberate)")]
    public Look[] lightLooks = new[]
    {
        new Look { pairA = ConcertLaser.LaserMode.SlowDrift,     pairB = ConcertLaser.LaserMode.TinyLissajous, beatDuration = 32, palette = PaletteCool,  mirrorPairB = true },
        new Look { pairA = ConcertLaser.LaserMode.StaticBeam,    pairB = ConcertLaser.LaserMode.StaticBeam,    beatDuration = 24, palette = PaletteIce                       },
        new Look { pairA = ConcertLaser.LaserMode.TwinSlow,      pairB = ConcertLaser.LaserMode.SlowDrift,     beatDuration = 32, palette = PaletteAmber                     },
        new Look { pairA = ConcertLaser.LaserMode.TinyLissajous, pairB = ConcertLaser.LaserMode.TwinSlow,      beatDuration = 32, palette = PaletteUV,    mirrorPairB = true },
        new Look { pairA = ConcertLaser.LaserMode.StaticBeam,    pairB = ConcertLaser.LaserMode.SlowDrift,     beatDuration = 24, palette = PaletteWarm                      },
    };

    [Header("MEDIUM-tier pool (verse-level — moderate motion)")]
    public Look[] mediumLooks = new[]
    {
        new Look { pairA = ConcertLaser.LaserMode.Pan,            pairB = ConcertLaser.LaserMode.Lissajous,      beatDuration = 16, palette = PaletteCool,   mirrorPairB = true },
        new Look { pairA = ConcertLaser.LaserMode.TripleFan,      pairB = ConcertLaser.LaserMode.AlternatePulse, beatDuration = 16, palette = PaletteWarm                       },
        new Look { pairA = ConcertLaser.LaserMode.Lissajous,      pairB = ConcertLaser.LaserMode.Pan,            beatDuration = 16, palette = PaletteUV,     mirrorPairB = true },
        new Look { pairA = ConcertLaser.LaserMode.AlternatePulse, pairB = ConcertLaser.LaserMode.TripleFan,      beatDuration = 16, palette = PaletteRGB                        },
        new Look { pairA = ConcertLaser.LaserMode.Pan,            pairB = ConcertLaser.LaserMode.TripleFan,      beatDuration = 16, palette = PaletteAcidic                     },
    };

    [Header("HARD-tier pool (chorus-level — energetic, snare-synced)")]
    public Look[] hardLooks = new[]
    {
        new Look { pairA = ConcertLaser.LaserMode.AggrSweep,   pairB = ConcertLaser.LaserMode.MultiBurst,   beatDuration = 16, palette = PaletteHot,    mirrorPairB = true                  },
        new Look { pairA = ConcertLaser.LaserMode.MultiBurst,  pairB = ConcertLaser.LaserMode.MultiBurst,   beatDuration = 8,  palette = PaletteRGB,    mirrorPairB = true, isDropLook = true },
        new Look { pairA = ConcertLaser.LaserMode.BeamCurtain, pairB = ConcertLaser.LaserMode.PulseTrio,    beatDuration = 16, palette = PaletteCool                                          },
        new Look { pairA = ConcertLaser.LaserMode.PulseTrio,   pairB = ConcertLaser.LaserMode.AggrSweep,    beatDuration = 16, palette = PaletteWarm                                          },
        new Look { pairA = ConcertLaser.LaserMode.AggrSweep,   pairB = ConcertLaser.LaserMode.BeamCurtain,  beatDuration = 16, palette = PaletteAcidic, mirrorPairB = true                    },
    };

    [Header("EXTREME-tier pool (drops, climax, peak)")]
    public Look[] extremeLooks = new[]
    {
        new Look { pairA = ConcertLaser.LaserMode.MaxBurst,     pairB = ConcertLaser.LaserMode.MaxBurst,     beatDuration = 8,  palette = PaletteHot,   mirrorPairB = true, isDropLook = true },
        new Look { pairA = ConcertLaser.LaserMode.BeatChaos,    pairB = ConcertLaser.LaserMode.BeatChaos,    beatDuration = 12, palette = PaletteRGB,                       isDropLook = true },
        new Look { pairA = ConcertLaser.LaserMode.DoubleStrobe, pairB = ConcertLaser.LaserMode.MaxBurst,     beatDuration = 8,  palette = PaletteWhite, mirrorPairB = true                    },
        new Look { pairA = ConcertLaser.LaserMode.SpiralOut,    pairB = ConcertLaser.LaserMode.DoubleStrobe, beatDuration = 12, palette = PaletteUV,                        isDropLook = true },
        new Look { pairA = ConcertLaser.LaserMode.MaxBurst,     pairB = ConcertLaser.LaserMode.BeatChaos,    beatDuration = 8,  palette = PaletteAcidic, mirrorPairB = true, blackoutBeats = 2, isDropLook = true },
    };

    // ─── Cone light looks (light1 / light2 — pan + color cycle) ─────────────
    [Header("CONE LIGHT pools (light1 / light2 — advance on every Nth hi-hat)")]
    public ConeLook[] lightConeLooks = new[]
    {
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.GentleHover, pairB = ConcertConeLight.ConeLightMode.GentleHover, palette = PaletteCool,  mirrorPairB = true },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.ColorBreath, pairB = ConcertConeLight.ConeLightMode.ColorBreath, palette = PaletteIce                       },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.SlowSweep,   pairB = ConcertConeLight.ConeLightMode.GentleHover, palette = PaletteWarm,  mirrorPairB = true },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.AmberHold,   pairB = ConcertConeLight.ConeLightMode.AmberHold,   palette = PaletteAmber                     },
    };
    public ConeLook[] mediumConeLooks = new[]
    {
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.PalettePan,      pairB = ConcertConeLight.ConeLightMode.PalettePan,      palette = PaletteCool,   mirrorPairB = true },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.ColorChase,      pairB = ConcertConeLight.ConeLightMode.ColorChase,      palette = PaletteUV                          },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.FigureEight,     pairB = ConcertConeLight.ConeLightMode.FigureEight,     palette = PaletteWarm,   mirrorPairB = true },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.AlternateColors, pairB = ConcertConeLight.ConeLightMode.AlternateColors, palette = PaletteRGB,    mirrorPairB = true },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.PalettePan,      pairB = ConcertConeLight.ConeLightMode.FigureEight,     palette = PaletteAcidic, mirrorPairB = true },
    };
    public ConeLook[] hardConeLooks = new[]
    {
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.SnapStep,     pairB = ConcertConeLight.ConeLightMode.SnapStep,     palette = PaletteHot,    mirrorPairB = true                  },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.ZigZag,       pairB = ConcertConeLight.ConeLightMode.ZigZag,       palette = PaletteRGB,    mirrorPairB = true                  },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.RotateColors, pairB = ConcertConeLight.ConeLightMode.RotateColors, palette = PaletteUV                                          },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.ColorPunch,   pairB = ConcertConeLight.ConeLightMode.SnapStep,     palette = PaletteAcidic, mirrorPairB = true                  },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.ZigZag,       pairB = ConcertConeLight.ConeLightMode.ColorPunch,   palette = PaletteHot,    mirrorPairB = true, isDropLook = true },
    };
    public ConeLook[] extremeConeLooks = new[]
    {
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.WildSweep,   pairB = ConcertConeLight.ConeLightMode.WildSweep,   palette = PaletteHot,    mirrorPairB = true, isDropLook = true },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.ColorChaos,  pairB = ConcertConeLight.ConeLightMode.ColorChaos,  palette = PaletteRGB,                        isDropLook = true },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.FastSpiral,  pairB = ConcertConeLight.ConeLightMode.FastSpiral,  palette = PaletteWhite,  mirrorPairB = true                    },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.RaveCycle,   pairB = ConcertConeLight.ConeLightMode.RaveCycle,   palette = PaletteUV                                            },
        new ConeLook { pairA = ConcertConeLight.ConeLightMode.WildSweep,   pairB = ConcertConeLight.ConeLightMode.RaveCycle,   palette = PaletteAcidic, mirrorPairB = true, isDropLook = true },
    };

    // ─── Strobe light looks (spotlight1 / spotlight2 — white BPM strobes) ──
    [Header("STROBE LIGHT pools (spotlight1 / spotlight2 — advance on every Nth kick)")]
    public StrobeLook[] lightStrobeLooks = new[]
    {
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.SlowPulse,   pairB = ConcertStrobeLight.StrobeLightMode.SlowPulse   },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.BassBreath,  pairB = ConcertStrobeLight.StrobeLightMode.BassBreath  },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.GentleFlash, pairB = ConcertStrobeLight.StrobeLightMode.GentleFlash },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.BarKick,     pairB = ConcertStrobeLight.StrobeLightMode.BarKick     },
    };
    public StrobeLook[] mediumStrobeLooks = new[]
    {
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.BeatStrobe, pairB = ConcertStrobeLight.StrobeLightMode.BeatStrobe },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.BassDouble, pairB = ConcertStrobeLight.StrobeLightMode.BassDouble },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.OnOffSync,  pairB = ConcertStrobeLight.StrobeLightMode.OnOffSync, mirrorPairB = true },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.AltBass,    pairB = ConcertStrobeLight.StrobeLightMode.AltBass,   mirrorPairB = true },
    };
    public StrobeLook[] hardStrobeLooks = new[]
    {
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.FastStrobe,     pairB = ConcertStrobeLight.StrobeLightMode.FastStrobe                      },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.BassMachineGun, pairB = ConcertStrobeLight.StrobeLightMode.BassMachineGun                  },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.CountInPulse,   pairB = ConcertStrobeLight.StrobeLightMode.CountInPulse                    },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.RipplePulse,    pairB = ConcertStrobeLight.StrobeLightMode.RipplePulse, mirrorPairB = true },
    };
    public StrobeLook[] extremeStrobeLooks = new[]
    {
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.HardStrobe16th, pairB = ConcertStrobeLight.StrobeLightMode.HardStrobe16th,                    isDropLook = true },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.BassChaos,      pairB = ConcertStrobeLight.StrobeLightMode.BassChaos                                            },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.BlitzStrobe,    pairB = ConcertStrobeLight.StrobeLightMode.BlitzStrobe,                       isDropLook = true },
        new StrobeLook { pairA = ConcertStrobeLight.StrobeLightMode.AntiPhase,      pairB = ConcertStrobeLight.StrobeLightMode.AntiPhase,      mirrorPairB = true                   },
    };

    [Tooltip("Cycle looks in order. Off = pick the next one at random (no immediate repeats).")]
    public bool sequential = true;
    [Tooltip("If beat detection isn't firing, advance on time instead.")]
    public bool useTimeFallback = true;
    public float fallbackSecondsPerLook = 8f;

    [Header("Tier Selection (driven by song energy)")]
    public float lightThreshold   = 0.06f;
    public float mediumThreshold  = 0.14f;
    public float hardThreshold    = 0.28f;
    public float extremeThreshold = 0.50f;
    [Tooltip("Hi-hat events/sec above this rate add a bias to perceived energy, biasing toward higher tiers. Helps catch verse→chorus transitions where bass is steady but hats intensify.")]
    public float hihatRateForBias = 5f;
    [Range(0f, 0.3f)] public float hihatEnergyBias = 0.06f;
    [Tooltip("Hysteresis — energy must dwell in the new tier's range this long before tier actually changes.")]
    public float tierDwellSeconds = 1.5f;

    [Header("Drop Reactions")]
    public bool jumpToDropLookOnDrop = true;
    [Tooltip("Full-screen UI flash on drops / crashes / silence-release. Off by default — the in-world strobes / blinders already convey the impact, and the screen flash reads as tacky in close-up POV.")]
    public bool flashOverlayOnDrop = false;
    [Range(0f, 1f)] public float dropFlashAlpha = 0.20f;
    public float dropFlashDecaySeconds = 0.6f;
    public float dropTierBoostSeconds = 6f;

    [Header("Crash Reactions (bigger than drops)")]
    [Range(0f, 1f)] public float crashFlashAlpha = 0.30f;
    public float crashFlashDecaySeconds = 0.4f;
    public float crashTierBoostSeconds = 4f;

    [Header("V1 Sync Features (toggle off if behavior is wrong for a specific song)")]
    [Tooltip("Phase-lock cone & strobe motion to the bar via BarPhase (peaks ON the beat).")]
    public bool enableBeatLockMotion       = true;
    [Tooltip("Buildup detector + bar-of-fade-out + unison release on drop. THE pro-show signature move.")]
    public bool enableBuildupChoreography  = true;
    [Tooltip("Cross-system unison flash + pose when kick + snare + crash co-occur within 80 ms.")]
    public bool enableStingDetection       = true;
    [Tooltip("Continuous frequency-band modulation underneath drum punches so lights never feel idle.")]
    public bool enableContinuousFlow       = true;
    [Tooltip("Color palette follows song section (low/mid/high/drop), holds through a section, shifts on transition.")]
    public bool enableSectionColorFamilies = true;

    [Header("V2 Song Dynamics (subliminal sync + narrative arc)")]
    [Tooltip("Subtle brightness pulse on every beat (5-10% peak boost, decays per beat). Makes the room feel alive even during slow modes.")]
    public bool enableBreathPulse          = true;
    [Range(0f, 0.3f), Tooltip("Magnitude of the beat-locked breath pulse. 0.07 is subliminal; lower = invisible, higher = aggressive.")]
    public float breathDepth               = 0.07f;
    [Tooltip("Long-window energy average drives a global intensity multiplier. Quiet sections fade; climaxes overdrive.")]
    public bool enableEnergyCurve          = true;
    [Tooltip("Range of the energy-curve multiplier (min, max). Default 0.5..1.1.")]
    public Vector2 energyCurveRange        = new Vector2(0.5f, 1.1f);
    [Tooltip("Freeze all lights when audio drops to near-silence. Cathartic release on audio return.")]
    public bool enableSilenceDetection     = true;
    [Tooltip("Snap cones to rest pose + dim everything during the LAST beat of a buildup. The drop becomes the explosion OUT of a held breath.")]
    public bool enablePreDropFreeze        = true;

    [Header("Color Families (palette pools per song section)")]
    [Tooltip("Palettes randomly chosen from this list when the song is in a low-energy section (intro, breakdown).")]
    public PaletteEntry[] lowEnergyFamily;
    [Tooltip("Palettes for verse-level energy.")]
    public PaletteEntry[] midEnergyFamily;
    [Tooltip("Palettes for chorus-level energy.")]
    public PaletteEntry[] highEnergyFamily;
    [Tooltip("Palettes for drop / climax (forced when ChoreoState = Release).")]
    public PaletteEntry[] dropFamily;
    public float colorFamilyLowThreshold  = 0.10f;
    public float colorFamilyMidThreshold  = 0.22f;
    public float colorFamilyHighThreshold = 0.40f;
    [Tooltip("Section must dwell this long before color family changes — prevents palette pumping.")]
    public float colorFamilyDwellSeconds  = 4f;

    [Header("Per-system advance triggers (Pink-Floyd-style separation)")]
    [Tooltip("Lasers advance look on every cymbal crash (the rare punctuation hit). Below is the minimum number of beats to hold a look so a sustained ride doesn't hammer-switch.")]
    [Range(0, 16)] public int laserMinBeatsBetweenCrashes = 2;
    [Tooltip("Cone lights (light1/light2) advance look every Nth hi-hat. 8 ~= one bar of straight 8ths.")]
    [Range(1, 64)] public int hihatsPerConeLook = 8;
    [Tooltip("Strobe lights (spotlight1/spotlight2) advance look every Nth snare. 4 ~= 1 bar of backbeats (snare on 2/4).")]
    [Range(1, 64)] public int snaresPerStrobeLook = 4;
    [Tooltip("Lasers also advance every N bars as a fallback (in case crashes are rare in the current song).")]
    [Range(2, 32)] public int barsPerLaserLook = 8;
    [Tooltip("Auto-attach ConcertConeLight to GameObjects named 'light1'/'light2' and ConcertStrobeLight to 'spotlight1'/'spotlight2' at startup.")]
    public bool autoAttachStageLights = true;

    [Header("Debug (read-only)")]
    [SerializeField] string debug_currentLookDesc;
    [SerializeField] int debug_beatsSinceLastSwitch;
    [SerializeField] string debug_pairA, debug_pairB;
    [SerializeField] string debug_conePairA, debug_conePairB;
    [SerializeField] string debug_strobePairA, debug_strobePairB;
    [SerializeField] ConcertLaser.IntensityTier debug_currentTier;
    [SerializeField] ConcertLaser.IntensityTier debug_pendingTier;
    [SerializeField] float debug_energyAvg;
    [SerializeField] float debug_energyWithBias;
    [SerializeField] float debug_hihatRate;

    int _currentLookInPool;
    int _beatAtLastSwitch;
    float _timeSinceSwitch;
    int _currentConeLookInPool;
    int _currentStrobeLookInPool;
    int _hihatsSinceConeSwitch;
    int _snaresSinceStrobeSwitch;
    int _laserBarOfLastAdvance = -1;

    // Section-aware color family state.
    ColorFamily _currentFamily = ColorFamily.MidEnergy;
    ColorFamily _pendingFamily = ColorFamily.MidEnergy;
    float _pendingFamilyStartTime;
    Color[] _activePalette;

    // Choreography state machine. Controlled by buildup detector + drop event.
    public enum ChoreoState { Normal, Anticipation, Release, Silence }
    ChoreoState _choreoState = ChoreoState.Normal;
    float _choreoStateEnteredTime;
    float _intensityFadeMul = 1f;       // 0..1 multiplier pushed to every light each frame
    float _stingActiveUntil = -999f;    // for cross-system unison stings
    bool _isPreDropFreeze;              // set during last beat of Anticipation

    // Lights read this each frame to know whether to suppress motion + dim
    // to ambient. True during Silence (audio dropped out) or PreDropFreeze
    // (last beat before a predicted drop).
    public bool IsHolding => _choreoState == ChoreoState.Silence || _isPreDropFreeze;
    ConcertLaser.IntensityTier _currentTier = ConcertLaser.IntensityTier.Medium;
    ConcertLaser.IntensityTier _pendingTier = ConcertLaser.IntensityTier.Medium;
    float _pendingTierStartTime;
    float _tierBoostUntil = -999f;
    ConcertLaser.IntensityTier _boostedTier = ConcertLaser.IntensityTier.Extreme;
    ConcertAudioDirector _director;
    bool _subscribedToEvents;
    readonly List<ConcertLaser> _lasers = new List<ConcertLaser>();
    readonly List<ConcertConeLight> _cones = new List<ConcertConeLight>();
    readonly List<ConcertStrobeLight> _strobes = new List<ConcertStrobeLight>();
    Image _flashImage;
    float _flashAlpha;
    float _flashCurrentDecay = 0.6f;

    Look[] CurrentPool
    {
        get
        {
            switch (_currentTier)
            {
                case ConcertLaser.IntensityTier.Light:   return lightLooks   != null && lightLooks.Length   > 0 ? lightLooks   : mediumLooks;
                case ConcertLaser.IntensityTier.Medium:  return mediumLooks  != null && mediumLooks.Length  > 0 ? mediumLooks  : hardLooks;
                case ConcertLaser.IntensityTier.Hard:    return hardLooks    != null && hardLooks.Length    > 0 ? hardLooks    : mediumLooks;
                case ConcertLaser.IntensityTier.Extreme: return extremeLooks != null && extremeLooks.Length > 0 ? extremeLooks : hardLooks;
            }
            return mediumLooks;
        }
    }

    ConeLook[] CurrentConePool
    {
        get
        {
            switch (_currentTier)
            {
                case ConcertLaser.IntensityTier.Light:   return lightConeLooks   != null && lightConeLooks.Length   > 0 ? lightConeLooks   : mediumConeLooks;
                case ConcertLaser.IntensityTier.Medium:  return mediumConeLooks  != null && mediumConeLooks.Length  > 0 ? mediumConeLooks  : hardConeLooks;
                case ConcertLaser.IntensityTier.Hard:    return hardConeLooks    != null && hardConeLooks.Length    > 0 ? hardConeLooks    : mediumConeLooks;
                case ConcertLaser.IntensityTier.Extreme: return extremeConeLooks != null && extremeConeLooks.Length > 0 ? extremeConeLooks : hardConeLooks;
            }
            return mediumConeLooks;
        }
    }

    StrobeLook[] CurrentStrobePool
    {
        get
        {
            switch (_currentTier)
            {
                case ConcertLaser.IntensityTier.Light:   return lightStrobeLooks   != null && lightStrobeLooks.Length   > 0 ? lightStrobeLooks   : mediumStrobeLooks;
                case ConcertLaser.IntensityTier.Medium:  return mediumStrobeLooks  != null && mediumStrobeLooks.Length  > 0 ? mediumStrobeLooks  : hardStrobeLooks;
                case ConcertLaser.IntensityTier.Hard:    return hardStrobeLooks    != null && hardStrobeLooks.Length    > 0 ? hardStrobeLooks    : mediumStrobeLooks;
                case ConcertLaser.IntensityTier.Extreme: return extremeStrobeLooks != null && extremeStrobeLooks.Length > 0 ? extremeStrobeLooks : hardStrobeLooks;
            }
            return mediumStrobeLooks;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var existing = FindObjectOfType<ConcertLightProgram>();
        if (existing != null) return;
        var go = new GameObject("[ConcertLightProgram]");
        DontDestroyOnLoad(go);
        go.AddComponent<ConcertLightProgram>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureFamilyDefaults();
        _activePalette = PickPaletteFromFamily(_currentFamily);
    }

    // Populate palette family pools with sensible defaults if the inspector
    // arrays are empty. Lets the feature work out-of-the-box; user can tune
    // via inspector at any time.
    void EnsureFamilyDefaults()
    {
        if (lowEnergyFamily == null || lowEnergyFamily.Length == 0)
            lowEnergyFamily = new[] {
                new PaletteEntry { palette = PaletteCool },
                new PaletteEntry { palette = PaletteIce },
                new PaletteEntry { palette = PaletteAmber },
            };
        if (midEnergyFamily == null || midEnergyFamily.Length == 0)
            midEnergyFamily = new[] {
                new PaletteEntry { palette = PaletteWarm },
                new PaletteEntry { palette = PaletteUV },
                new PaletteEntry { palette = PaletteAcidic },
            };
        if (highEnergyFamily == null || highEnergyFamily.Length == 0)
            highEnergyFamily = new[] {
                new PaletteEntry { palette = PaletteHot },
                new PaletteEntry { palette = PaletteRGB },
                new PaletteEntry { palette = PaletteAcidic },
            };
        if (dropFamily == null || dropFamily.Length == 0)
            dropFamily = new[] {
                // PaletteWhite removed — was forcing lasers/cones to white during
                // and after every drop, then sticking for 4+ s of dwell. Use only
                // saturated, colorful palettes for drop punctuation.
                new PaletteEntry { palette = PaletteHot },
                new PaletteEntry { palette = PaletteRGB },
                new PaletteEntry { palette = PaletteAcidic },
            };
    }

    ColorFamily FamilyFromEnergy(float energy)
    {
        if (energy >= colorFamilyHighThreshold) return ColorFamily.HighEnergy;
        if (energy >= colorFamilyMidThreshold)  return ColorFamily.MidEnergy;
        if (energy >= colorFamilyLowThreshold)  return ColorFamily.MidEnergy;
        return ColorFamily.LowEnergy;
    }

    Color[] PickPaletteFromFamily(ColorFamily f)
    {
        PaletteEntry[] pool;
        switch (f)
        {
            case ColorFamily.LowEnergy:  pool = lowEnergyFamily;  break;
            case ColorFamily.HighEnergy: pool = highEnergyFamily; break;
            case ColorFamily.Drop:       pool = dropFamily;       break;
            default:                     pool = midEnergyFamily;  break;
        }
        if (pool == null || pool.Length == 0) return null;
        var entry = pool[UnityEngine.Random.Range(0, pool.Length)];
        return entry.palette;
    }

    // Mirrors UpdateTier hysteresis pattern (ConcertLightProgram.cs UpdateTier).
    // Section transitions fire on long-window energy changes that dwell at least
    // colorFamilyDwellSeconds — prevents flicker around thresholds.
    void UpdateColorFamily()
    {
        if (!enableSectionColorFamilies) return;
        ColorFamily target;
        if (_choreoState == ChoreoState.Release)
        {
            target = ColorFamily.Drop;
        }
        else
        {
            float energy = _director != null ? _director.EnergyLongAvg : 0f;
            target = FamilyFromEnergy(energy);
        }
        if (target != _pendingFamily)
        {
            _pendingFamily = target;
            _pendingFamilyStartTime = Time.time;
        }
        // Drop transitions are immediate (mid-Release we want hot colors NOW).
        bool immediate = target == ColorFamily.Drop;
        if (_pendingFamily != _currentFamily &&
            (immediate || Time.time - _pendingFamilyStartTime >= colorFamilyDwellSeconds))
        {
            _currentFamily = _pendingFamily;
            _activePalette = PickPaletteFromFamily(_currentFamily) ?? _activePalette;
            // Push to all existing lights that hold a palette (strobes ignore).
            for (int i = 0; i < _lasers.Count; i++) if (_lasers[i] != null) _lasers[i].SetPalette(_activePalette);
            for (int i = 0; i < _cones.Count;  i++) if (_cones[i]  != null) _cones[i].SetPalette(_activePalette);
        }
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_subscribedToEvents && _director != null)
        {
            _director.OnDrop          -= HandleDrop;
            _director.OnCrash         -= HandleCrash;
            _director.OnKick          -= HandleKick;
            _director.OnSnare         -= HandleSnare;
            _director.OnHihat         -= HandleHihat;
            _director.OnSting         -= HandleSting;
            _director.OnBuildupStart  -= HandleBuildupStart;
            _director.OnBuildupEnd    -= HandleBuildupEnd;
            _director.OnSilenceStart  -= HandleSilenceStart;
            _director.OnSilenceEnd    -= HandleSilenceEnd;
            _subscribedToEvents = false;
        }
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySubscribeToDirector();
        BuildFlashOverlay();
        InitForCurrentScene();
    }

    // Built-game path: ConcertLightProgram auto-creates in MainMenu (via
    // RuntimeInitializeOnLoadMethod) and DontDestroyOnLoad's itself, so Start()
    // runs in MainMenu where there are no concert lights to attach to or to
    // register. Without a sceneLoaded hook, the gameplay scene's Light1/Light2/
    // Spotlight1/Spotlight2 never get their ConcertConeLight/ConcertStrobeLight
    // components added, the laser/cone/strobe lists stay empty, and the lights
    // sit dark or only trigger sporadically (whichever lazy "if list is empty,
    // refresh" path happens to fire). Editor play mode hides this because Start
    // runs in the gameplay scene directly.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        TrySubscribeToDirector();
        InitForCurrentScene();
    }

    void TrySubscribeToDirector()
    {
        if (_subscribedToEvents) return;
        _director = ConcertAudioDirector.Instance;
        if (_director == null) return;
        _director.OnDrop          += HandleDrop;
        _director.OnCrash         += HandleCrash;
        _director.OnKick          += HandleKick;
        _director.OnSnare         += HandleSnare;
        _director.OnHihat         += HandleHihat;
        _director.OnSting         += HandleSting;
        _director.OnBuildupStart  += HandleBuildupStart;
        _director.OnBuildupEnd    += HandleBuildupEnd;
        _director.OnSilenceStart  += HandleSilenceStart;
        _director.OnSilenceEnd    += HandleSilenceEnd;
        _subscribedToEvents = true;
    }

    void InitForCurrentScene()
    {
        if (autoAttachStageLights) AutoAttachStageLights();
        RefreshLaserList();
        RefreshConeList();
        RefreshStrobeList();
        ApplyTierToLasers(_currentTier);
        ApplyTierToConeLights(_currentTier);
        ApplyTierToStrobeLights(_currentTier);
        ApplyLook(_currentLookInPool);
        ApplyConeLook(_currentConeLookInPool);
        ApplyStrobeLook(_currentStrobeLookInPool);
    }

    // ─── Drum-event handlers ──────────────────────────────────────────────────
    //
    // PINK FLOYD SEPARATION: each light system advances on a different drum part
    // so lasers / cones / strobes feel independently choreographed:
    //   • Lasers swap looks on cymbal CRASHES (rare, high-impact punctuation).
    //   • Cone lights swap looks on every Nth HI-HAT (frequent, density-driven).
    //   • Strobes swap looks on every Nth KICK (steady tempo grid).
    // Tier escalation (Light→Extreme based on energy) is shared so all three
    // systems escalate together when the song builds.

    void HandleDrop()
    {
        TriggerFlash(dropFlashAlpha, dropFlashDecaySeconds);
        BoostTier(ConcertLaser.IntensityTier.Extreme, dropTierBoostSeconds);
        if (jumpToDropLookOnDrop)
        {
            JumpToDropLookInPool(CurrentPool);
            JumpToConeDropLookInPool(CurrentConePool);
            JumpToStrobeDropLookInPool(CurrentStrobePool);
        }
        // Choreography: enter Release for the next 2 bars (full unison brightness,
        // forced drop palette via Drop family).
        if (enableBuildupChoreography)
        {
            _choreoState = ChoreoState.Release;
            _choreoStateEnteredTime = Time.time;
            _intensityFadeMul = 1f;
        }
    }

    // Buildup/sting handlers — additive over existing tier/look behavior.
    void HandleBuildupStart()
    {
        if (!enableBuildupChoreography) return;
        if (_choreoState == ChoreoState.Release) return;  // mid-drop; ignore
        _choreoState = ChoreoState.Anticipation;
        _choreoStateEnteredTime = Time.time;
    }

    void HandleBuildupEnd()
    {
        if (_choreoState == ChoreoState.Anticipation) _choreoState = ChoreoState.Normal;
    }

    void HandleSilenceStart()
    {
        if (!enableSilenceDetection) return;
        // Don't override Release — let the drop play through.
        if (_choreoState == ChoreoState.Release) return;
        _choreoState = ChoreoState.Silence;
        _choreoStateEnteredTime = Time.time;
    }

    void HandleSilenceEnd()
    {
        if (_choreoState != ChoreoState.Silence) return;
        // Cathartic release on audio return — small flash + temporary tier boost.
        TriggerFlash(0.30f, 0.40f);
        BoostTier(ConcertLaser.IntensityTier.Hard, 3f);
        _choreoState = ChoreoState.Normal;
    }

    void HandleSting()
    {
        if (!enableStingDetection) return;
        // No flash overlay on stings — the cone/strobe/laser unison is the
        // visual; a screen-wide flash on every accent is too much.
        _stingActiveUntil = Time.time + 0.20f;
        for (int i = 0; i < _cones.Count;   i++) if (_cones[i]   != null) _cones[i].TriggerSting();
        for (int i = 0; i < _strobes.Count; i++) if (_strobes[i] != null) _strobes[i].TriggerSting();
        for (int i = 0; i < _lasers.Count;  i++) if (_lasers[i]  != null) _lasers[i].TriggerSting();
    }

    // Choreography state machine. Runs each frame; computes _intensityFadeMul
    // (0..1) based on current state and pushes it to every light at the end of
    // UpdateLaserIntensities (which we extend to push to cones + strobes too).
    void UpdateChoreoState()
    {
        float bpm = (_director != null && _director.DetectedBpm > 30f) ? _director.DetectedBpm : 120f;
        float beatSec = 60f / bpm;
        float barSec  = beatSec * 4f;

        // Default: ease back toward 1.0 (Normal state target).
        float baseMul = 1f;
        bool prevFreeze = _isPreDropFreeze;
        _isPreDropFreeze = false;

        if (_choreoState == ChoreoState.Silence)
        {
            // Held breath — dim a bit, freeze poses (via IsHolding). NOT
            // 0.10 anymore: in standalone builds the audio amplitude can
            // sit just at the silence boundary the entire concert, and the
            // old value made every light run at ~5% intensity — sparse,
            // grayish-white, indistinguishable from "not working". 0.5
            // keeps the held-breath read while leaving lights legible.
            baseMul = 0.5f;
        }
        else if (_choreoState == ChoreoState.Anticipation && enableBuildupChoreography)
        {
            float elapsed = Time.time - _choreoStateEnteredTime;
            float t = Mathf.Clamp01(elapsed / barSec);
            if (enablePreDropFreeze && t > 0.75f)
            {
                // LAST quarter of the bar — held silence. The drop becomes the
                // explosion OUT of this freeze. Cones snap to rest, lasers/strobes go dark.
                _isPreDropFreeze = true;
                baseMul = 0.05f;
            }
            else
            {
                // Linear fade across the first 75% of the bar.
                baseMul = Mathf.Lerp(1f, 0.20f, t / 0.75f);
            }
            if (elapsed > barSec * 2f) _choreoState = ChoreoState.Normal;
        }
        else if (_choreoState == ChoreoState.Release && enableBuildupChoreography)
        {
            // Full intensity for 2 bars, then return to Normal.
            baseMul = 1f;
            if (Time.time - _choreoStateEnteredTime > barSec * 2f) _choreoState = ChoreoState.Normal;
        }
        else
        {
            baseMul = Mathf.MoveTowards(_intensityFadeMul, 1f, Time.deltaTime * 2f);
        }

        // Apply energy curve as a final multiplier (quiet sections fade, climaxes overdrive).
        float curveMul = 1f;
        if (enableEnergyCurve && _director != null)
            curveMul = Mathf.Lerp(energyCurveRange.x, energyCurveRange.y, _director.EnergyCurve);

        // Floor the combined multiplier so even the deepest fade (silence
        // hold, pre-drop freeze, intro before energy ramps up) keeps the
        // lights visibly on. Without this floor the combined product would
        // drop to ~0.025 in quiet sections (0.05 baseMul × 0.5 curveMul ×
        // idleIntensity 0.2 × 0.5 hold-mul on the cone side) — visually
        // indistinguishable from "lights stopped working".
        _intensityFadeMul = Mathf.Max(0.4f, baseMul * curveMul);
    }

    void HandleCrash()
    {
        TriggerFlash(crashFlashAlpha, crashFlashDecaySeconds);
        BoostTier(ConcertLaser.IntensityTier.Extreme, crashTierBoostSeconds);
        // Lasers advance on cymbal crashes — every cymbal hit changes the look.
        // The min-beats guard prevents a sustained ride from hammer-switching.
        if (_director != null)
        {
            int beatsSince = _director.BeatCount - _beatAtLastSwitch;
            if (beatsSince >= laserMinBeatsBetweenCrashes)
            {
                AdvanceLookSequential();
                _laserBarOfLastAdvance = _director.BarCount;
            }
        }
        else
        {
            AdvanceLookSequential();
        }
    }

    void HandleKick()
    {
        // Kicks no longer advance any system's look — strobes moved to snare.
        // Kept as an event hook for future expansions.
    }

    void HandleSnare()
    {
        // Snares now drive STROBE look advance (and the strobes themselves are
        // snare-synced inside ConcertStrobeLight).
        _snaresSinceStrobeSwitch++;
        if (_snaresSinceStrobeSwitch >= Mathf.Max(1, snaresPerStrobeLook))
        {
            _snaresSinceStrobeSwitch = 0;
            AdvanceStrobeLookSequential();
        }
    }

    void HandleHihat()
    {
        // Hi-hats drive CONE LIGHT look advance — every Nth hi-hat swaps the look.
        _hihatsSinceConeSwitch++;
        if (_hihatsSinceConeSwitch >= Mathf.Max(1, hihatsPerConeLook))
        {
            _hihatsSinceConeSwitch = 0;
            AdvanceConeLookSequential();
        }
    }

    void AdvanceLookSequential()
    {
        var pool = CurrentPool;
        if (pool == null || pool.Length == 0) return;
        int next;
        if (sequential) next = (_currentLookInPool + 1) % pool.Length;
        else if (pool.Length == 1) next = 0;
        else
        {
            do { next = UnityEngine.Random.Range(0, pool.Length); }
            while (next == _currentLookInPool);
        }
        ApplyLook(next);
    }

    void AdvanceConeLookSequential()
    {
        var pool = CurrentConePool;
        if (pool == null || pool.Length == 0) return;
        int next;
        if (sequential) next = (_currentConeLookInPool + 1) % pool.Length;
        else if (pool.Length == 1) next = 0;
        else
        {
            do { next = UnityEngine.Random.Range(0, pool.Length); }
            while (next == _currentConeLookInPool);
        }
        ApplyConeLook(next);
    }

    void AdvanceStrobeLookSequential()
    {
        var pool = CurrentStrobePool;
        if (pool == null || pool.Length == 0) return;
        int next;
        if (sequential) next = (_currentStrobeLookInPool + 1) % pool.Length;
        else if (pool.Length == 1) next = 0;
        else
        {
            do { next = UnityEngine.Random.Range(0, pool.Length); }
            while (next == _currentStrobeLookInPool);
        }
        ApplyStrobeLook(next);
    }

    void JumpToConeDropLookInPool(ConeLook[] pool)
    {
        if (pool == null || pool.Length == 0) return;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i].isDropLook) { ApplyConeLook(i, poolOverride: pool); return; }
        ApplyConeLook(0, poolOverride: pool);
    }

    void JumpToStrobeDropLookInPool(StrobeLook[] pool)
    {
        if (pool == null || pool.Length == 0) return;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i].isDropLook) { ApplyStrobeLook(i, poolOverride: pool); return; }
        ApplyStrobeLook(0, poolOverride: pool);
    }

    void TriggerFlash(float alpha, float decaySeconds)
    {
        if (!flashOverlayOnDrop || _flashImage == null) return;
        _flashAlpha = Mathf.Max(_flashAlpha, alpha);
        _flashCurrentDecay = decaySeconds;
    }

    void BoostTier(ConcertLaser.IntensityTier t, float seconds)
    {
        _boostedTier = t;
        _tierBoostUntil = Time.time + seconds;
        SetTierImmediate(t);
    }

    void JumpToDropLookInPool(Look[] pool)
    {
        if (pool == null || pool.Length == 0) return;
        // Find the first isDropLook in the pool and jump to it.
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i].isDropLook) { ApplyLook(i, poolOverride: pool); return; }
        }
        // No drop look in this pool — just jump to look 0.
        ApplyLook(0, poolOverride: pool);
    }

    // ─── Update / dispatch ────────────────────────────────────────────────────

    void Update()
    {
        UpdateTier();
        UpdateColorFamily();
        UpdateChoreoState();
        UpdateLookSwitching();
        UpdateLaserIntensities();
        UpdateFlashOverlay();
        WriteDebugFields();
    }

    void UpdateTier()
    {
        // Honor active drop/crash boost.
        if (Time.time < _tierBoostUntil)
        {
            if (_currentTier != _boostedTier) SetTierImmediate(_boostedTier);
            return;
        }

        // Pick target tier from energy + hi-hat density bias.
        float baseEnergy = _director != null ? _director.EnergyLongAvg : 0f;
        float bias = (_director != null && _director.HihatRate >= hihatRateForBias) ? hihatEnergyBias : 0f;
        float energy = baseEnergy + bias;
        debug_energyWithBias = energy;

        ConcertLaser.IntensityTier target = TierFromEnergy(energy);

        if (target != _pendingTier)
        {
            _pendingTier = target;
            _pendingTierStartTime = Time.time;
        }

        if (_pendingTier != _currentTier && Time.time - _pendingTierStartTime >= tierDwellSeconds)
        {
            ChangeTier(_pendingTier);
        }
    }

    ConcertLaser.IntensityTier TierFromEnergy(float energy)
    {
        if (energy >= extremeThreshold) return ConcertLaser.IntensityTier.Extreme;
        if (energy >= hardThreshold)    return ConcertLaser.IntensityTier.Hard;
        if (energy >= mediumThreshold)  return ConcertLaser.IntensityTier.Medium;
        return ConcertLaser.IntensityTier.Light;
    }

    void SetTierImmediate(ConcertLaser.IntensityTier t)
    {
        if (t == _currentTier) return;
        ChangeTier(t);
    }

    void ChangeTier(ConcertLaser.IntensityTier t)
    {
        _currentTier = t;
        _pendingTier = t;
        ApplyTierToLasers(t);
        ApplyTierToConeLights(t);
        ApplyTierToStrobeLights(t);
        // Carry look index across tier changes (modulo new pool length) so the
        // laser doesn't snap back to look 0 every time a crash boosts tier
        // and the boost expires. This keeps progression through pool variety
        // even when tier oscillates between base and Extreme.
        var lp = CurrentPool;
        if (lp != null && lp.Length > 0) ApplyLook(_currentLookInPool % lp.Length);
        var cp = CurrentConePool;
        if (cp != null && cp.Length > 0) ApplyConeLook(_currentConeLookInPool % cp.Length);
        var sp = CurrentStrobePool;
        if (sp != null && sp.Length > 0) ApplyStrobeLook(_currentStrobeLookInPool % sp.Length);
    }

    void ApplyTierToLasers(ConcertLaser.IntensityTier t)
    {
        if (_lasers.Count == 0) RefreshLaserList();
        for (int i = 0; i < _lasers.Count; i++)
        {
            if (_lasers[i] != null) _lasers[i].SetTier(t);
        }
    }

    void UpdateLookSwitching()
    {
        var pool = CurrentPool;
        if (pool == null || pool.Length == 0) return;

        // When audio is playing, laser look-switching is driven by HandleCrash
        // (cone by HandleHihat, strobe by HandleSnare). This per-frame path
        // adds a bar-based fallback so cymbal-rare songs still cycle through
        // the full laser pool.
        if (_director != null && _director.IsPlaying)
        {
            debug_beatsSinceLastSwitch = _director.BeatCount - _beatAtLastSwitch;
            // Bar-based fallback advance — guarantees variety even when crashes
            // never fire. Doesn't suppress crash-driven advance; just adds a floor.
            int bar = _director.BarCount;
            if (_laserBarOfLastAdvance < 0) _laserBarOfLastAdvance = bar;
            else if (bar - _laserBarOfLastAdvance >= barsPerLaserLook)
            {
                _laserBarOfLastAdvance = bar;
                AdvanceLookSequential();
            }
            return;
        }
        if (useTimeFallback)
        {
            _timeSinceSwitch += Time.deltaTime;
            if (_timeSinceSwitch >= fallbackSecondsPerLook) AdvanceLookSequential();
        }
    }

    void UpdateLaserIntensities()
    {
        var pool = CurrentPool;
        if (pool == null || pool.Length == 0) return;
        var look = pool[Mathf.Clamp(_currentLookInPool, 0, pool.Length - 1)];
        int blackoutBeats = look.blackoutBeats;
        float blackoutMul = 1f;
        if (blackoutBeats > 0 && _director != null)
        {
            int beatsSince = _director.BeatCount - _beatAtLastSwitch;
            if (beatsSince < blackoutBeats) blackoutMul = 0f;
        }
        // Combined intensity = laser look's blackout * choreography fade.
        float laserTarget = blackoutMul * _intensityFadeMul;
        for (int li = 0; li < _lasers.Count; li++)
        {
            var laser = _lasers[li];
            if (laser == null || laser.pair == ConcertLaser.PairId.Solo) continue;
            laser.SetIntensity(Mathf.MoveTowards(laser.intensity, laserTarget, Time.deltaTime * 6f));
        }
        // Push the choreography fade multiplier to cones and strobes too — they
        // don't use blackoutBeats but still need to dim during Anticipation.
        for (int i = 0; i < _cones.Count;   i++) if (_cones[i]   != null) _cones[i].SetIntensity(Mathf.MoveTowards(_cones[i].intensity, _intensityFadeMul, Time.deltaTime * 6f));
        for (int i = 0; i < _strobes.Count; i++) if (_strobes[i] != null) _strobes[i].SetIntensity(Mathf.MoveTowards(_strobes[i].intensity, _intensityFadeMul, Time.deltaTime * 6f));
    }

    void UpdateFlashOverlay()
    {
        if (_flashImage == null) return;
        if (_flashAlpha > 0f)
        {
            _flashAlpha = Mathf.Max(0f, _flashAlpha - Time.deltaTime / Mathf.Max(0.0001f, _flashCurrentDecay));
            var c = _flashImage.color; c.a = _flashAlpha; _flashImage.color = c;
        }
    }

    void ApplyLook(int i, Look[] poolOverride = null)
    {
        var pool = poolOverride ?? CurrentPool;
        if (pool == null || pool.Length == 0) return;
        i = Mathf.Clamp(i, 0, pool.Length - 1);
        var look = pool[i];
        _currentLookInPool = i;
        _beatAtLastSwitch = _director != null ? _director.BeatCount : 0;
        _timeSinceSwitch = 0f;

        if (_lasers.Count == 0) RefreshLaserList();

        // Pair-mates receive the same syncTime so phase accumulators line up.
        float syncTime = Time.time;
        for (int li = 0; li < _lasers.Count; li++)
        {
            var laser = _lasers[li];
            if (laser == null) continue;
            if (laser.pair == ConcertLaser.PairId.A)
            {
                laser.SetMode(look.pairA, syncTime);
                laser.SetPalette(PaletteForLook(look.palette));
                laser.SetMirrored(false);
            }
            else if (laser.pair == ConcertLaser.PairId.B)
            {
                laser.SetMode(look.pairB, syncTime);
                laser.SetPalette(PaletteForLook(look.palette));
                laser.SetMirrored(look.mirrorPairB);
            }
        }
    }

    // Returns _activePalette if section-color-families is enabled, else falls
    // back to the look's authored palette. Lets users disable family color
    // grading without losing per-look palette overrides.
    Color[] PaletteForLook(Color[] lookPalette) =>
        (enableSectionColorFamilies && _activePalette != null && _activePalette.Length > 0)
            ? _activePalette : lookPalette;

    // Include inactive (`true` arg) so a stage that's currently disabled by the
    // day/night cycle still has its lights tracked — when it becomes active
    // again the program is already driving its components.
    public void RefreshLaserList()
    {
        _lasers.Clear();
        _lasers.AddRange(FindObjectsOfType<ConcertLaser>(true));
    }

    public void RefreshConeList()
    {
        _cones.Clear();
        _cones.AddRange(FindObjectsOfType<ConcertConeLight>(true));
    }

    public void RefreshStrobeList()
    {
        _strobes.Clear();
        _strobes.AddRange(FindObjectsOfType<ConcertStrobeLight>(true));
    }

    void ApplyConeLook(int i, ConeLook[] poolOverride = null)
    {
        var pool = poolOverride ?? CurrentConePool;
        if (pool == null || pool.Length == 0) return;
        i = Mathf.Clamp(i, 0, pool.Length - 1);
        var look = pool[i];
        _currentConeLookInPool = i;
        if (_cones.Count == 0) RefreshConeList();
        float syncTime = Time.time;
        for (int li = 0; li < _cones.Count; li++)
        {
            var cone = _cones[li];
            if (cone == null) continue;
            if (cone.pair == ConcertConeLight.PairId.A)
            {
                cone.SetMode(look.pairA, syncTime);
                cone.SetPalette(PaletteForLook(look.palette));
                cone.SetMirrored(false);
            }
            else if (cone.pair == ConcertConeLight.PairId.B)
            {
                cone.SetMode(look.pairB, syncTime);
                cone.SetPalette(PaletteForLook(look.palette));
                cone.SetMirrored(look.mirrorPairB);
            }
        }
    }

    void ApplyStrobeLook(int i, StrobeLook[] poolOverride = null)
    {
        var pool = poolOverride ?? CurrentStrobePool;
        if (pool == null || pool.Length == 0) return;
        i = Mathf.Clamp(i, 0, pool.Length - 1);
        var look = pool[i];
        _currentStrobeLookInPool = i;
        if (_strobes.Count == 0) RefreshStrobeList();
        float syncTime = Time.time;
        for (int li = 0; li < _strobes.Count; li++)
        {
            var s = _strobes[li];
            if (s == null) continue;
            if (s.pair == ConcertStrobeLight.PairId.A)
            {
                s.SetMode(look.pairA, syncTime);
                s.SetMirrored(false);
            }
            else if (s.pair == ConcertStrobeLight.PairId.B)
            {
                s.SetMode(look.pairB, syncTime);
                s.SetMirrored(look.mirrorPairB);
            }
        }
    }

    void ApplyTierToConeLights(ConcertLaser.IntensityTier t)
    {
        if (_cones.Count == 0) RefreshConeList();
        for (int i = 0; i < _cones.Count; i++)
            if (_cones[i] != null) _cones[i].SetTier(t);
    }

    void ApplyTierToStrobeLights(ConcertLaser.IntensityTier t)
    {
        if (_strobes.Count == 0) RefreshStrobeList();
        for (int i = 0; i < _strobes.Count; i++)
            if (_strobes[i] != null) _strobes[i].SetTier(t);
    }

    // Auto-attach the appropriate component to GameObjects named exactly
    // "light1"/"light2" (cone) or "spotlight1"/"spotlight2" (strobe). Pair A
    // is assigned to the GO whose name ends in '1'; pair B to '2'. Saves the
    // user from manually wiring four inspectors and matches the project's
    // "self-configuring concert components" convention.
    void AutoAttachStageLights()
    {
        // Names are case-sensitive in GameObject.Find, and the actual GOs in the
        // scene are PascalCase (Light1/Light2/Spotlight1/Spotlight2). Search both
        // capitalizations to be tolerant of either convention.
        AutoAttachCone(new[] { "Light1", "light1" },         ConcertConeLight.PairId.A);
        AutoAttachCone(new[] { "Light2", "light2" },         ConcertConeLight.PairId.B);
        AutoAttachStrobe(new[] { "Spotlight1", "spotlight1" }, ConcertStrobeLight.PairId.A);
        AutoAttachStrobe(new[] { "Spotlight2", "spotlight2" }, ConcertStrobeLight.PairId.B);
    }

    // Enumerate EVERY active-or-inactive GameObject in the scene whose name
    // matches one of `names`. Returns all matches so multi-stage setups
    // (stagegood + stagegood2, each with its own Light1/Light2/Spotlight1/
    // Spotlight2 children) get the script attached to BOTH stages' lights.
    // The old FindFirstActive returned only the first match — when the user
    // added stagegood2, ConcertLightProgram.Start would still grab
    // stagegood's lights first, and stagegood2's lights never received a
    // ConcertConeLight/ConcertStrobeLight component, so they sat dark in
    // build (visible only when stagegood was inactive at editor start).
    static void FindAllByNames(string[] names, List<GameObject> results)
    {
        results.Clear();
        var all = FindObjectsOfType<Transform>(true);
        for (int n = 0; n < names.Length; n++)
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == names[n]) results.Add(all[i].gameObject);
    }

    static readonly List<GameObject> s_attachScratch = new List<GameObject>();

    static void AutoAttachCone(string[] names, ConcertConeLight.PairId pair)
    {
        FindAllByNames(names, s_attachScratch);
        for (int i = 0; i < s_attachScratch.Count; i++)
        {
            var go = s_attachScratch[i];
            if (go == null) continue;
            var existing = go.GetComponent<ConcertConeLight>();
            if (existing == null) existing = go.AddComponent<ConcertConeLight>();
            existing.pair = pair;
        }
    }

    static void AutoAttachStrobe(string[] names, ConcertStrobeLight.PairId pair)
    {
        FindAllByNames(names, s_attachScratch);
        for (int i = 0; i < s_attachScratch.Count; i++)
        {
            var go = s_attachScratch[i];
            if (go == null) continue;
            var existing = go.GetComponent<ConcertStrobeLight>();
            if (existing == null) existing = go.AddComponent<ConcertStrobeLight>();
            existing.pair = pair;
        }
    }

    void BuildFlashOverlay()
    {
        var canvasGo = new GameObject("[ConcertFlashCanvas]");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        canvasGo.AddComponent<CanvasScaler>();

        var imgGo = new GameObject("FlashImage");
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rt = imgGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _flashImage = imgGo.AddComponent<Image>();
        _flashImage.color = new Color(1f, 1f, 1f, 0f);
        _flashImage.raycastTarget = false;
    }

    void WriteDebugFields()
    {
        debug_currentTier = _currentTier;
        debug_pendingTier = _pendingTier;
        debug_energyAvg = _director != null ? _director.EnergyLongAvg : 0f;
        debug_hihatRate = _director != null ? _director.HihatRate : 0f;
        var pool = CurrentPool;
        if (pool != null && pool.Length > 0)
        {
            int idx = Mathf.Clamp(_currentLookInPool, 0, pool.Length - 1);
            var lk = pool[idx];
            debug_currentLookDesc = $"{_currentTier}[{idx}]";
            debug_pairA = lk.pairA.ToString();
            debug_pairB = lk.pairB.ToString();
        }
        var conePool = CurrentConePool;
        if (conePool != null && conePool.Length > 0)
        {
            int idx = Mathf.Clamp(_currentConeLookInPool, 0, conePool.Length - 1);
            var lk = conePool[idx];
            debug_conePairA = lk.pairA.ToString();
            debug_conePairB = lk.pairB.ToString();
        }
        var strobePool = CurrentStrobePool;
        if (strobePool != null && strobePool.Length > 0)
        {
            int idx = Mathf.Clamp(_currentStrobeLookInPool, 0, strobePool.Length - 1);
            var lk = strobePool[idx];
            debug_strobePairA = lk.pairA.ToString();
            debug_strobePairB = lk.pairB.ToString();
        }
    }

    public void JumpToLook(int index) => ApplyLook(index);
    public void NextLook()
    {
        var pool = CurrentPool;
        if (pool == null || pool.Length == 0) return;
        int next = sequential ? (_currentLookInPool + 1) % pool.Length : UnityEngine.Random.Range(0, pool.Length);
        ApplyLook(next);
    }
}
