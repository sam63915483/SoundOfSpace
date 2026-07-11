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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HelmetOverlayHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<HelmetOverlayHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildFrameCanvas();
        gameObject.AddComponent<HelmetSway>();
        CreateChild<VisorGlassOverlay>("VisorGlassOverlay");
        CreateChild<CondensationOverlay>("CondensationOverlay");
        HelmetHudPalette.OnAccentChanged += Retint;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        HelmetHudPalette.OnAccentChanged -= Retint;
    }

    T CreateChild<T>(string name) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
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
        }
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
        img.color = HelmetHudPalette.FrameTint;
        img.raycastTarget = false;
        return img;
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
        if (_swayRoot == null) return;
        var images = _swayRoot.GetComponentsInChildren<RawImage>(true);
        for (int i = 0; i < images.Length; i++) images[i].color = HelmetHudPalette.FrameTint;
    }
}
