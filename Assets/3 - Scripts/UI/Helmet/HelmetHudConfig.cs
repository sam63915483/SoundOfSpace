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
    [Range(0f, 0.3f)]  public float glassTintAlpha = 0.05f;
    [Range(0f, 0.5f)]  public float fresnelAlpha   = 0.12f;
    [Range(0f, 0.15f)] public float scanlineAlpha  = 0.03f;

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
}
