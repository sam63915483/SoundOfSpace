using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Astronaut-helmet interior frame. Renders the helmet texture as 10 RawImage
/// pieces on a ScreenSpaceOverlay canvas (UILayer.HelmetFrame): fixed-size
/// corner/brow pieces anchored to their screen corners, stretchable spans
/// between them — so the recessed housings stay put relative to the
/// corner-anchored cluster cards at ANY aspect ratio. One stretched
/// full-screen image would drift against the clusters; pieces don't.
/// Owns the visor-glass + condensation child overlays and the HelmetSway
/// driver. Content waits until a HelmetHudConfig with a texture exists in the
/// scene (throttled find), and rebuilds when config.Version bumps (live
/// Inspector tuning). Gated by InputSettings.fxHelmetOverlay via
/// CameraEffectsManager.Instance.Input.
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md).
/// </summary>
public class HelmetOverlayHUD : MonoBehaviour
{
    public static HelmetOverlayHUD Instance { get; private set; }

    Canvas _frameCanvas;
    RectTransform _swayRoot;
    HelmetHudConfig _config;
    float _nextConfigFind;
    int _builtVersion = int.MinValue;
    bool _clustersSeated;

    /// A housing screen in the art, resolved to screen space: anchor fraction
    /// of the canvas (follows the stretched art at any aspect), size in 16:9
    /// reference units, and the Z-tilt matching the screen's painted perspective.
    public struct HousingRect { public Vector2 anchorFrac; public Vector2 sizeRef; public float tiltDeg; public float contentScale; }

    readonly System.Collections.Generic.List<RawImage> _pieceImages =
        new System.Collections.Generic.List<RawImage>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HelmetOverlayHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<HelmetOverlayHUD>();
    }

    VisorGlassOverlay _glass;
    CondensationOverlay _condensation;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildFrameCanvas();
        gameObject.AddComponent<HelmetSway>();
        _glass = CreateOverlayRoot<VisorGlassOverlay>("VisorGlassOverlay");
        _condensation = CreateOverlayRoot<CondensationOverlay>("CondensationOverlay");
        HelmetHudPalette.OnAccentChanged += Retint;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        HelmetHudPalette.OnAccentChanged -= Retint;
        if (_glass != null) Destroy(_glass.gameObject);
        if (_condensation != null) Destroy(_condensation.gameObject);
    }

    // NOT parented under this GameObject: this root carries a Canvas, and a
    // Canvas on a descendant becomes a NESTED canvas — its CanvasScaler is
    // ignored (default ~100×100 rect → renders as a small square at screen
    // center) and its sortingOrder needs overrideSorting. A sibling root
    // object gives each layer a true root canvas at its intended sort order.
    static T CreateOverlayRoot<T>(string name) where T : Component
    {
        var go = new GameObject(name);
        DontDestroyOnLoad(go);
        return go.AddComponent<T>();
    }

    /// Shared gate for all helmet layers. Defaults ON when settings aren't up
    /// yet (early boot) — the helmet is the intended look, not a garnish.
    public static bool FxEnabled()
    {
        var mgr = CameraEffectsManager.Instance;
        return mgr == null || mgr.Input == null || mgr.Input.fxHelmetOverlay;
    }

    /// True while the active scene is the main menu — children must not
    /// re-enable their canvases there (HUDSceneGate only fires on sceneLoaded).
    public static bool InMainMenu() => SceneManager.GetActiveScene().name == "MainMenu";

    void Update()
    {
        // Throttled config find — the config is a scene object that can die on
        // scene reload; never FindObjectOfType per frame (repo rule).
        if (_config == null && Time.unscaledTime >= _nextConfigFind)
        {
            _nextConfigFind = Time.unscaledTime + 0.5f;
            _config = HelmetHudConfig.Instance != null
                ? HelmetHudConfig.Instance
                : FindObjectOfType<HelmetHudConfig>();
        }

        bool haveArt = _config != null && _config.helmetTexture != null;
        bool show = haveArt && FxEnabled() && !InMainMenu();
        if (_frameCanvas != null && _frameCanvas.enabled != show && !InMainMenu())
            _frameCanvas.enabled = show;

        if (haveArt && _config.Version != _builtVersion)
        {
            _builtVersion = _config.Version;
            RebuildPieces();
            _clustersSeated = false;   // re-seat with the (possibly retuned) rects
        }
        // Re-seat when the window shape changes — the stretched art's painted
        // angles and housing positions shift with aspect.
        if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
        {
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            _clustersSeated = false;
        }
        // Seat the clusters into the art's built-in screens once all three
        // singletons exist (they auto-create in arbitrary order around us).
        if (haveArt && !_clustersSeated) _clustersSeated = TrySeatClusters();
    }

    int _lastScreenW, _lastScreenH;

    bool TrySeatClusters()
    {
        var c = _config;
        if (!c.artHousingMode) return true;
        if (VitalsHUD.Instance == null || GForceHUD.Instance == null || CompassHUD.Instance == null)
            return false;
        float S = 1.025f * (c.stretchWholeTexture ? c.frameZoom : 1f);
        VitalsHUD.Instance.SeatInArtHousing(ToScreen(c, c.brScreenPx, S, c.brScreenTiltDeg));
        GForceHUD.Instance.SeatInArtHousing(ToScreen(c, c.blScreenPx, S, c.blScreenTiltDeg));
        CompassHUD.Instance.SeatInArtHousing(ToScreen(c, c.browScreenPx, S, 0f));
        return true;
    }

    // Texture-px rect (bottom-left origin) → screen space. The art stretches
    // full-screen, so a texture point at fraction f sits at canvas fraction
    // 0.5 + (f - 0.5) * S (S = overscan × zoom on the sway root). Size maps at
    // 2 px = 1 ref unit (4K art on the 1920-unit canvas), scaled by S.
    static HousingRect ToScreen(HelmetHudConfig c, Rect px, float S, float tiltDeg)
    {
        var tex = c.helmetTexture;
        float cx = (px.x + px.width * 0.5f) / tex.width;
        float cy = (px.y + px.height * 0.5f) / tex.height;
        // The art stretches to the window, so a slope painted for 16:9 renders
        // steeper in a taller window and shallower in a wider one. Correct the
        // authored tilt by the actual aspect so the content stays parallel to
        // the painted screen at ANY window shape (re-seated on resize).
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float effTilt = Mathf.Atan(Mathf.Tan(tiltDeg * Mathf.Deg2Rad) * (16f / 9f) / aspect) * Mathf.Rad2Deg;
        return new HousingRect
        {
            anchorFrac = new Vector2(0.5f + (cx - 0.5f) * S, 0.5f + (cy - 0.5f) * S),
            sizeRef = new Vector2(px.width, px.height) * 0.5f * S,
            tiltDeg = effTilt,
            contentScale = c.screenContentScale,
        };
    }

    void BuildFrameCanvas()
    {
        _frameCanvas = gameObject.AddComponent<Canvas>();
        _frameCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _frameCanvas.sortingOrder = UILayer.HelmetFrame;
        HUDSceneGate.Register(_frameCanvas);
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        HudVisibility.RegisterHideable(_frameCanvas);   // HIDE HUD + pod cinematic

        var rootGo = new GameObject("SwayRoot", typeof(RectTransform));
        rootGo.transform.SetParent(transform, false);
        _swayRoot = rootGo.GetComponent<RectTransform>();
        _swayRoot.anchorMin = Vector2.zero;
        _swayRoot.anchorMax = Vector2.one;
        _swayRoot.offsetMin = Vector2.zero;
        _swayRoot.offsetMax = Vector2.zero;
        // Slight overscale so sway never reveals a gap at the screen edge
        // (max offset 18 units on a 1920-unit canvas ≈ 1%).
        _swayRoot.localScale = new Vector3(1.025f, 1.025f, 1f);
        HelmetSway.Register(_swayRoot, 1f);
    }

    void RebuildPieces()
    {
        for (int i = _swayRoot.childCount - 1; i >= 0; i--)
            Destroy(_swayRoot.GetChild(i).gameObject);
        _pieceImages.Clear();

        var c = _config;
        var tex = c.helmetTexture;

        // Single-stretch mode: one full-screen RawImage (organic art whose
        // recessed screens come from the code bezels, so nothing in the art
        // needs pixel alignment). frameZoom pushes a thick painted rim
        // outward; the overscan factor keeps sway from revealing edge gaps.
        if (c.stretchWholeTexture)
        {
            _swayRoot.localScale = new Vector3(1.025f * c.frameZoom, 1.025f * c.frameZoom, 1f);
            var img = NewPiece("FullFrame", new Rect(0, 0, tex.width, tex.height), tex);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (c.artHousingMode)
            {
                // "Powered glass" beds under the readouts — unify the painted
                // glass with the cluster content so it reads lit, not pasted.
                AddScreenBed("BedBL", c.blScreenPx, c.blScreenTiltDeg);
                AddScreenBed("BedBR", c.brScreenPx, c.brScreenTiltDeg);
                AddScreenBed("BedBrow", c.browScreenPx, 0f);
            }
            return;
        }
        _swayRoot.localScale = new Vector3(1.025f, 1.025f, 1f);
        // Fixed corner/brow pieces (size = px * 0.5 → ref units, 2px = 1 unit).
        AddCorner("TL", c.tlCorner, tex, new Vector2(0f, 1f));
        AddCorner("TR", c.trCorner, tex, new Vector2(1f, 1f));
        AddCorner("BLHousing", c.blHousing, tex, new Vector2(0f, 0f));
        AddCorner("BRHousing", c.brHousing, tex, new Vector2(1f, 0f));
        AddBrow(c.topBrow, tex);
        // Stretch spans between the fixed pieces.
        AddHSpan("TopLeftSpan",  c.topLeftSpan,  tex, 0f,   c.tlCorner.width * 0.5f,  0.5f, -c.topBrow.width * 0.25f, true);
        AddHSpan("TopRightSpan", c.topRightSpan, tex, 0.5f, c.topBrow.width * 0.25f,  1f,   -c.trCorner.width * 0.5f, true);
        AddHSpan("BottomSpan",   c.bottomSpan,   tex, 0f,   c.blHousing.width * 0.5f, 1f,   -c.brHousing.width * 0.5f, false);
        AddVEdge("LeftEdge",  c.leftEdge,  tex, 0f, c.blHousing.height * 0.5f, c.tlCorner.height * 0.5f);
        AddVEdge("RightEdge", c.rightEdge, tex, 1f, c.brHousing.height * 0.5f, c.trCorner.height * 0.5f);
    }

    RawImage NewPiece(string name, Rect px, Texture2D tex)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_swayRoot, false);
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.uvRect = new Rect(px.x / tex.width, px.y / tex.height,
                              px.width / tex.width, px.height / tex.height);
        img.color = _config != null ? _config.frameTint : HelmetHudPalette.FrameTint;
        img.raycastTarget = false;
        _pieceImages.Add(img);
        return img;
    }

    // Dark "powered glass" bed inside an art screen: dim fill + inner shadow +
    // faint accent backlight, drawn on the frame layer beneath the readouts.
    // Anchored by texture fraction inside the sway root, so it tracks the
    // stretched art exactly (the root's scale applies the zoom for us).
    void AddScreenBed(string name, Rect px, float tiltDeg)
    {
        var c = _config;
        float strength = c.screenBedStrength;
        if (strength <= 0f) return;
        var tex = c.helmetTexture;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_swayRoot, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2((px.x + px.width * 0.5f) / tex.width,
                                                  (px.y + px.height * 0.5f) / tex.height);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(px.width * 0.5f, px.height * 0.5f);
        rt.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);

        var fill = go.AddComponent<Image>();
        fill.color = new Color(0.01f, 0.03f, 0.06f, 0.75f * strength);
        fill.raycastTarget = false;

        var glowGo = new GameObject("Backlight", typeof(RectTransform));
        glowGo.transform.SetParent(rt, false);
        var glowRt = (RectTransform)glowGo.transform;
        glowRt.anchorMin = Vector2.zero;
        glowRt.anchorMax = Vector2.one;
        glowRt.offsetMin = new Vector2(4f, 4f);
        glowRt.offsetMax = new Vector2(-4f, -4f);
        var glow = glowGo.AddComponent<Image>();
        glow.sprite = HelmetBezelKit.HaloSprite;
        glow.type = Image.Type.Sliced;
        Color gc = HelmetHudPalette.Accent;
        gc.a = 0.12f * strength;
        glow.color = gc;
        glow.raycastTarget = false;

        var shadowGo = new GameObject("InnerShadow", typeof(RectTransform));
        shadowGo.transform.SetParent(rt, false);
        var shadowRt = (RectTransform)shadowGo.transform;
        shadowRt.anchorMin = Vector2.zero;
        shadowRt.anchorMax = Vector2.one;
        shadowRt.offsetMin = Vector2.zero;
        shadowRt.offsetMax = Vector2.zero;
        var sh = shadowGo.AddComponent<RawImage>();
        sh.texture = GetInnerShadowTexture();
        sh.color = new Color(0f, 0f, 0f, 0.85f * strength);
        sh.raycastTarget = false;

        // Thin accent outline hugging the glass edge, tilted WITH the bed —
        // the parallel-edges cue that makes the readout read as part of the
        // screen instead of a flat sticker (the clusters dropped their own
        // borders when they went integrated).
        var lineGo = new GameObject("EdgeLine", typeof(RectTransform));
        lineGo.transform.SetParent(rt, false);
        var lineRt = (RectTransform)lineGo.transform;
        lineRt.anchorMin = Vector2.zero;
        lineRt.anchorMax = Vector2.one;
        lineRt.offsetMin = new Vector2(3f, 3f);
        lineRt.offsetMax = new Vector2(-3f, -3f);
        var line = lineGo.AddComponent<Image>();
        line.sprite = UIPanelSprites.GetBeveledOutline();
        line.type = Image.Type.Sliced;
        Color lc = HelmetHudPalette.Accent;
        lc.a = 0.22f * strength;
        line.color = lc;
        line.raycastTarget = false;
    }

    // Rectangular edge-dark vignette — the glass recess shading.
    static Texture2D _innerShadow;
    static Texture2D GetInnerShadowTexture()
    {
        if (_innerShadow != null) return _innerShadow;
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / S - 0.5f) * 2f;
                float dy = Mathf.Abs((y + 0.5f) / S - 0.5f) * 2f;
                float d = Mathf.Max(dx, dy);
                float a = Mathf.Pow(Mathf.Clamp01((d - 0.6f) / 0.4f), 1.6f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _innerShadow = tex;
        return tex;
    }

    void AddCorner(string name, Rect px, Texture2D tex, Vector2 corner)
    {
        var img = NewPiece(name, px, tex);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = corner;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(px.width * 0.5f, px.height * 0.5f);
    }

    void AddBrow(Rect px, Texture2D tex)
    {
        var img = NewPiece("TopBrow", px, tex);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(px.width * 0.5f, px.height * 0.5f);
    }

    // Horizontal span pinned to top (top=true) or bottom edge, stretching from
    // (anchorXMin + insetLeft) to (anchorXMax + insetRight); fixed height.
    void AddHSpan(string name, Rect px, Texture2D tex,
                  float anchorXMin, float insetLeft, float anchorXMax, float insetRight, bool top)
    {
        var img = NewPiece(name, px, tex);
        var rt = (RectTransform)img.transform;
        float y = top ? 1f : 0f;
        rt.anchorMin = new Vector2(anchorXMin, y);
        rt.anchorMax = new Vector2(anchorXMax, y);
        rt.pivot = new Vector2(0.5f, y);
        float h = px.height * 0.5f;
        rt.offsetMin = new Vector2(insetLeft,  top ? -h : 0f);
        rt.offsetMax = new Vector2(insetRight, top ? 0f : h);
    }

    // Vertical edge pinned to left (side=0) or right (side=1), stretching
    // between the bottom housing top and the top corner bottom; fixed width.
    void AddVEdge(string name, Rect px, Texture2D tex, float side, float insetBottom, float insetTop)
    {
        var img = NewPiece(name, px, tex);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = new Vector2(side, 0f);
        rt.anchorMax = new Vector2(side, 1f);
        rt.pivot = new Vector2(side, 0.5f);
        float w = px.width * 0.5f;
        rt.offsetMin = new Vector2(side == 0f ? 0f : -w, insetBottom);
        rt.offsetMax = new Vector2(side == 0f ? w : 0f, -insetTop);
    }

    void Retint()
    {
        // Only the art pieces track the frame tint — bed shadows/backlights
        // have their own colors and rebuild on config Version bumps.
        Color tint = _config != null ? _config.frameTint : (Color)HelmetHudPalette.FrameTint;
        for (int i = _pieceImages.Count - 1; i >= 0; i--)
        {
            if (_pieceImages[i] == null) { _pieceImages.RemoveAt(i); continue; }
            _pieceImages[i].color = tint;
        }
    }
}
