using UnityEngine;

/// <summary>
/// Scene-placed settings holder for the helmet HUD. Lives in the gameplay
/// scene so the helmet texture and every feel value are Inspector-assignable
/// (auto-created singletons can't take Inspector references). HelmetOverlayHUD
/// finds it lazily (throttled) and rebuilds whenever Version changes, so every
/// field is live-tweakable in play mode. Piece rects are in TEXTURE PIXELS,
/// BOTTOM-LEFT origin, matching RawImage.uvRect math directly
/// (template: 3840×2160, 2 px = 1 canvas reference unit).
/// </summary>
public class HelmetHudConfig : MonoBehaviour
{
    public static HelmetHudConfig Instance { get; private set; }

    [Header("Art")]
    public Texture2D helmetTexture;

    [Header("Accent (routed through HelmetHudPalette to every layer)")]
    public Color accentColor = new Color32(0x5C, 0xC8, 0xFF, 0xFF);

    [Header("Piece rects (texture px, bottom-left origin — template 3840×2160)")]
    public Rect tlCorner     = new Rect(0,    1800, 480, 360);
    public Rect trCorner     = new Rect(3360, 1800, 480, 360);
    public Rect topBrow      = new Rect(1280, 1980, 1280, 180);
    public Rect topLeftSpan  = new Rect(480,  2064, 800,  96);
    public Rect topRightSpan = new Rect(2560, 2064, 800,  96);
    public Rect leftEdge     = new Rect(0,    460,  96, 1340);
    public Rect rightEdge    = new Rect(3744, 460,  96, 1340);
    public Rect blHousing    = new Rect(0,    0,   840, 560);
    public Rect brHousing    = new Rect(3000, 0,   840, 560);
    public Rect bottomSpan   = new Rect(840,  0,  2160,  96);

    [Header("Visor glass")]
    [Range(0f, 0.3f)]  public float glassTintAlpha = 0.02f;
    [Range(0f, 0.5f)]  public float fresnelAlpha   = 0.05f;
    [Range(0f, 0.15f)] public float scanlineAlpha  = 0.02f;

    [Header("Sway")]
    [Range(0f, 3f)]  public float lookSwayGain   = 1.0f;
    [Range(0f, 3f)]  public float moveSwayGain   = 1.0f;
    [Range(4f, 60f)] public float swayMaxOffset  = 18f;   // canvas reference units
    [Range(1f, 20f)] public float swaySmoothing  = 8f;

    [Header("Frame render mode")]
    [Tooltip("True: stretch the whole texture full-screen — right for organic art (e.g. the AI-generated frame) whose housings come from the code bezels. False: 10-piece corner-anchored mode for template-authored art with baked-in housing cutouts.")]
    public bool stretchWholeTexture = true;
    [Tooltip("Zoom on the stretched frame so a thick painted rim can be pushed outward/off-screen (1 = as authored).")]
    [Range(1f, 1.6f)] public float frameZoom = 1.15f;

    [Header("Condensation (suit O2 feedback)")]
    [Tooltip("Suit O2 fraction where fog starts creeping in.")]
    [Range(0f, 1f)] public float fogStartPercent = 0.6f;
    [Tooltip("Suit O2 fraction where fog reaches full strength.")]
    [Range(0f, 1f)] public float fogFullPercent  = 0.1f;
    [Range(0f, 1f)] public float fogMaxAlpha     = 0.85f;

    /// Bumped on any Inspector change → HelmetOverlayHUD rebuilds/re-reads.
    /// Stays 0 in builds (OnValidate is editor-only), which is fine: the HUD
    /// builds once when it first sees the config.
    public int Version { get; private set; }

    void Awake()
    {
        Instance = this;
        HelmetHudPalette.SetAccent(accentColor);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnValidate()
    {
        Version++;
        HelmetHudPalette.SetAccent(accentColor);
    }

    // ── Art housing screens (APPEND-ONLY: serialized fields stay at class end) ──
    [Header("Art housing screens (texture px, bottom-left origin)")]
    [Tooltip("True: the art has displays built into the shell — the clusters seat inside the rects below and drop their floating-card chrome (no bg/border/bezels). False: clusters keep the code-drawn bezel-card look at the HelmetHudLayout contract positions.")]
    public bool artHousingMode = true;
    [Tooltip("Bottom-left pod's dark display face (jetpack/boost cluster seats here).")]
    public Rect blScreenPx   = new Rect(150, 90, 760, 500);
    [Tooltip("Bottom-right pod's dark display face (vitals cluster seats here).")]
    public Rect brScreenPx   = new Rect(2930, 90, 760, 500);
    [Tooltip("Brow's slim dark instrument strip (compass seats here).")]
    public Rect browScreenPx = new Rect(1300, 1990, 1240, 110);

    [Header("Blend (art integration)")]
    [Tooltip("Multiply tint on the helmet art — darken it so the interior reads shadowed, not showroom-white.")]
    public Color frameTint = new Color(0.45f, 0.46f, 0.50f, 1f);
    [Tooltip("Z-tilt (deg) on the boost cluster to match the bottom-left screen's painted perspective.")]
    [Range(-15f, 15f)] public float blScreenTiltDeg = 4f;
    [Tooltip("Z-tilt (deg) on the vitals cluster to match the bottom-right screen's painted perspective.")]
    [Range(-15f, 15f)] public float brScreenTiltDeg = -4f;
    [Tooltip("Dark 'powered glass' bed (fill + inner shadow + accent backlight) drawn into each art screen beneath the readouts. 0 = off.")]
    [Range(0f, 1f)] public float screenBedStrength = 0.6f;

    [Tooltip("Extra shrink on the readout content inside each screen (applied on top of fit-to-glass). 0.8 = 1.25x smaller — breathing room between content and bezel.")]
    [Range(0.5f, 1.2f)] public float screenContentScale = 0.8f;

    [Header("3D panel lean (pitch X / yaw Y, deg — combines with the Z tilt above)")]
    [Tooltip("Bottom-left screen: X tips the panel back, Y turns it toward screen center. Overlay UI renders orthographically, so this reads as an angled-panel lean + compression rather than true perspective.")]
    public Vector2 blScreenTilt3D = new Vector2(12f, -30f);
    [Tooltip("Bottom-right screen (mirrored).")]
    public Vector2 brScreenTilt3D = new Vector2(12f, 30f);

    [Tooltip("Position nudge (ref units) for the boost content inside its screen, applied after auto-centering.")]
    public Vector2 blContentOffset = Vector2.zero;
    [Tooltip("Position nudge (ref units) for the vitals content inside its screen, applied after auto-centering.")]
    public Vector2 brContentOffset = Vector2.zero;

    [Header("Manual tweak mode")]
    [Tooltip("Freeze auto-seating AND helmet sway so you can hand-edit the cluster transforms in the play-mode Inspector without them being overwritten. Objects to select (Hierarchy → DontDestroyOnLoad): VitalsHUD/Card, GForceHUD/Card, CompassHUD/Strip + HeadingBadge, HelmetOverlayHUD/SwayRoot. Play-mode edits revert on stop — note your values and bake them into this config.")]
    public bool manualTweakMode;

    // ── Perspective screens (APPEND-ONLY: serialized fields stay at class end) ──
    [Header("Perspective screens (painted quad corners, texture px, bottom-left origin)")]
    [Tooltip("Project the corner clusters onto the art's angled screens with true perspective: each cluster renders to a RenderTexture and is drawn as a homography-warped quad whose corners match the painted screen exactly (rotation alone can't taper toward a vanishing point on an overlay canvas). Off = legacy flat seating via blScreenPx/brScreenPx.")]
    public bool perspectiveScreens = true;
    [Tooltip("Bottom-left pod's dark glass — corners measured from the art (edge-line fits on the dark region).")]
    public Vector2 blQuadTL = new Vector2(690.6f, 513.0f);
    public Vector2 blQuadTR = new Vector2(1287.4f, 542.1f);
    public Vector2 blQuadBL = new Vector2(753.6f, 112.5f);
    public Vector2 blQuadBR = new Vector2(1355.5f, 190.0f);
    [Tooltip("Bottom-right pod's dark glass — corners measured from the art.")]
    public Vector2 brQuadTL = new Vector2(2551.5f, 542.1f);
    public Vector2 brQuadTR = new Vector2(3147.7f, 514.3f);
    public Vector2 brQuadBL = new Vector2(2483.2f, 190.1f);
    public Vector2 brQuadBR = new Vector2(3084.7f, 112.5f);

    [Header("Compass (brow) content")]
    [Tooltip("Scale on the compass strip + heading badge inside the brow glass (replaces screenContentScale for the brow only).")]
    [Range(0.3f, 1.2f)] public float browContentScale = 0.7f;
    [Tooltip("Position nudge (ref units) for the compass strip + heading badge inside the brow glass.")]
    public Vector2 browContentOffset = new Vector2(0f, -8f);

    [Tooltip("Extra scale on the boost (bottom-left) cluster content only — multiplies screenContentScale. Clamped at seat time so the card can never overflow its glass.")]
    [Range(0.8f, 1.5f)] public float blContentBoost = 1.2f;
    [Tooltip("Extra scale on the vitals (bottom-right) cluster content only — multiplies screenContentScale. Clamped at seat time so the card (including the ship rows while piloting) can never overflow its glass.")]
    [Range(0.8f, 1.5f)] public float brContentBoost = 1.3f;
}
