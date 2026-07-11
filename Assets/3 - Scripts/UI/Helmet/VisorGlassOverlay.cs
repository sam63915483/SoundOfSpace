using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visor "glass" between the world and the HUD clusters: a faint accent tint,
/// a fresnel-style rim glow hugging the screen edges, and subtle scanlines.
/// Own canvas at UILayer.VisorGlass (810) — above hotbar/compass, below the
/// dialogue letterbox (820) and the cluster readouts (830) so text stays
/// crisp. World-view chromatic aberration is NOT rebuilt here — the existing
/// ChromaticAberrationEffect camera module already provides it (CAMERA tab).
/// All layers live on a sway-registered, slightly-overscanned root so helmet
/// sway never reveals a screen-edge gap. Intensities poll HelmetHudConfig
/// with change-detection (live Inspector tuning).
/// </summary>
public class VisorGlassOverlay : MonoBehaviour
{
    Canvas _canvas;
    Image _tint;
    RawImage _fresnel;
    RawImage _scanlines;
    float _lastTintA = -1f, _lastFresnelA = -1f, _lastScanA = -1f;
    int _lastScreenH = -1;
    Color32 _lastAccent;

    const float Overscan = 34f;   // units beyond the screen on every side

    void Awake()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.VisorGlass;
        HUDSceneGate.Register(_canvas);
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        HudVisibility.RegisterHideable(_canvas);

        var rootGo = new GameObject("SwayRoot", typeof(RectTransform));
        rootGo.transform.SetParent(transform, false);
        var root = rootGo.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(-Overscan, -Overscan);
        root.offsetMax = new Vector2(Overscan, Overscan);
        HelmetSway.Register(root, 1f);

        // 1) Flat tint.
        _tint = NewStretched<Image>(root, "Tint");
        // 2) Fresnel rim.
        _fresnel = NewStretched<RawImage>(root, "FresnelRim");
        _fresnel.texture = MakeFresnelTexture(512);
        // 3) Scanlines (uvRect-tiled).
        _scanlines = NewStretched<RawImage>(root, "Scanlines");
        _scanlines.texture = MakeScanlineTexture();

        _lastAccent = HelmetHudPalette.Accent;
    }

    static T NewStretched<T>(RectTransform parent, string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var g = go.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    void Update()
    {
        bool show = HelmetOverlayHUD.FxEnabled() && !HelmetOverlayHUD.InMainMenu();
        if (_canvas.enabled != show && !HelmetOverlayHUD.InMainMenu())
            _canvas.enabled = show;
        if (!show) return;

        var cfg = HelmetHudConfig.Instance;
        float tintA = cfg != null ? cfg.glassTintAlpha : 0.05f;
        float fresA = cfg != null ? cfg.fresnelAlpha   : 0.12f;
        float scanA = cfg != null ? cfg.scanlineAlpha  : 0.03f;

        Color32 accent = HelmetHudPalette.Accent;
        bool accentChanged = accent.r != _lastAccent.r || accent.g != _lastAccent.g
                          || accent.b != _lastAccent.b;
        if (accentChanged) _lastAccent = accent;

        if (accentChanged || !Mathf.Approximately(tintA, _lastTintA))
        { _lastTintA = tintA; _tint.color = WithAlpha(accent, tintA); }
        if (accentChanged || !Mathf.Approximately(fresA, _lastFresnelA))
        { _lastFresnelA = fresA; _fresnel.color = WithAlpha(accent, fresA); }
        if (accentChanged || !Mathf.Approximately(scanA, _lastScanA))
        { _lastScanA = scanA; _scanlines.color = WithAlpha(accent, scanA); }

        // Retile scanlines only when the screen height changes (~3 px period).
        if (Screen.height != _lastScreenH)
        {
            _lastScreenH = Screen.height;
            _scanlines.uvRect = new Rect(0f, 0f, 1f, Mathf.Max(1f, Screen.height / 3f));
        }
    }

    static Color WithAlpha(Color32 c, float a) { Color col = c; col.a = a; return col; }

    // Elliptical rim mask: transparent center, alpha rising toward the edges.
    static Texture2D MakeFresnelTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / size - 0.5f) * 2f;
                float dy = Mathf.Abs((y + 0.5f) / size - 0.5f) * 2f;
                // Blend square-rim (even edge coverage) with euclidean
                // (stronger corners) so the glow hugs all four edges.
                float d = Mathf.Max(dx, dy) * 0.7f
                        + Mathf.Sqrt(dx * dx + dy * dy) / 1.41421f * 0.3f;
                float a = Mathf.Pow(Mathf.Clamp01((d - 0.55f) / 0.45f), 1.8f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // 1×4 repeating strip: one lit row in four → ~3 px scanline period on screen.
    static Texture2D MakeScanlineTexture()
    {
        var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.SetPixels(new[]
        {
            new Color(1f, 1f, 1f, 0f),
            new Color(1f, 1f, 1f, 0f),
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 0f),
        });
        tex.Apply();
        return tex;
    }
}
