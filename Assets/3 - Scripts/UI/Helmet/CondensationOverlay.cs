using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visor condensation: droplety fog creeping in from the visor edges as suit
/// oxygen runs low — FUNCTIONAL feedback, so it renders ABOVE the cluster
/// readouts (UILayer.HelmetCondensation, 838): a fogged screen IS the warning.
/// Alpha ramps from fogStartPercent down to fogFullPercent of
/// OxygenManager.SuitPercent, breathes in time with the existing O2 alarm
/// below 25%, and creeps (lerped) rather than snapping. Canvas fully disables
/// when clear — no zero-alpha render cost (mirrors CameraEffectsManager's
/// GateOverlayCanvas reasoning). Gated by fxHelmetOverlay AND
/// fxHelmetCondensation.
/// </summary>
public class CondensationOverlay : MonoBehaviour
{
    Canvas _canvas;
    RawImage _img;
    float _alpha;
    float _lastAppliedAlpha = -1f;

    const float Overscan = 34f;
    static readonly Color FogColor = new Color(0.75f, 0.85f, 0.95f, 1f);

    void Awake()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.HelmetCondensation;
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

        var go = new GameObject("Fog", typeof(RectTransform));
        go.transform.SetParent(root, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _img = go.AddComponent<RawImage>();
        _img.texture = MakeFogTexture(512);
        _img.raycastTarget = false;

        _canvas.enabled = false;   // starts clear
    }

    void Update()
    {
        var mgr = CameraEffectsManager.Instance;
        bool fxOn = HelmetOverlayHUD.FxEnabled()
                    && (mgr == null || mgr.Input == null || mgr.Input.fxHelmetCondensation);

        float target = 0f;
        if (fxOn && !HelmetOverlayHUD.InMainMenu())
        {
            float suit = OxygenManager.Instance != null ? OxygenManager.Instance.SuitPercent : 1f;
            var cfg = HelmetHudConfig.Instance;
            float start = cfg != null ? cfg.fogStartPercent : 0.6f;
            float full  = cfg != null ? cfg.fogFullPercent  : 0.1f;
            float max   = cfg != null ? cfg.fogMaxAlpha     : 0.85f;
            float t = Mathf.InverseLerp(start, full, suit);   // 0 above start → 1 at full
            // Urgency pulse below the suit-alarm threshold (0.25) so the fog
            // breathes in time with the O2 alarm beeps.
            if (suit < 0.25f) t *= 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 4.8f);
            target = t * max;
        }

        // Fog creeps, never snaps.
        _alpha = Mathf.Lerp(_alpha, target, Time.unscaledDeltaTime * 1.5f);
        if (_alpha < 0.004f && target <= 0f) _alpha = 0f;

        bool visible = _alpha > 0.004f;
        if (_canvas.enabled != visible && !HelmetOverlayHUD.InMainMenu())
            _canvas.enabled = visible;
        if (!visible) return;

        if (!Mathf.Approximately(_alpha, _lastAppliedAlpha))
        {
            _lastAppliedAlpha = _alpha;
            var c = FogColor; c.a = _alpha;
            _img.color = c;
        }

        // Tiny uv drift sells "living" moisture without moving the edge mask
        // off-center (offsets stay within ±1%).
        float t2 = Time.unscaledTime;
        _img.uvRect = new Rect(Mathf.Sin(t2 * 0.03f) * 0.01f,
                               Mathf.Cos(t2 * 0.021f) * 0.01f, 1f, 1f);
    }

    // Edge-hugging droplet fog: perlin-modulated mask, dense at the rim,
    // clear in the center.
    static Texture2D MakeFogTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float dx = Mathf.Abs(u - 0.5f) * 2f;
                float dy = Mathf.Abs(v - 0.5f) * 2f;
                float d = Mathf.Max(dx, dy) * 0.6f
                        + Mathf.Sqrt(dx * dx + dy * dy) / 1.41421f * 0.4f;
                // Mask starts creeping at 15% from center — the outermost band
                // is hidden behind the opaque helmet frame art, so the fog must
                // reach well into the visible visor to read as feedback.
                float edge = Mathf.Pow(Mathf.Clamp01((d - 0.15f) / 0.85f), 1.4f);
                float noise = 0.55f + 0.45f * Mathf.PerlinNoise(u * 6f, v * 6f);
                // A second octave adds droplet-scale texture.
                noise *= 0.8f + 0.2f * Mathf.PerlinNoise(u * 23f + 7.3f, v * 23f + 3.1f);
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge * noise));
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
