using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders a HUD cluster card into an off-screen RenderTexture and re-draws
/// it on the host overlay canvas as a HousingScreenWarp quad matching the
/// helmet art's painted screen perspective. Works for TMP text too — the
/// warp maps captured pixels, not vertices.
///
/// The capture rig (world-space canvas + ortho camera) parks far above the
/// main camera — the same keep-out trick as GForceHUD's 3D thrust widget —
/// and is additionally scaled down 100× so even if a frustum ever reaches
/// it, it is sub-pixel. Rig follows the main camera each LateUpdate so the
/// floating origin can never march it into view. The camera only renders
/// while the host canvas is visible.
///
/// Content units inside the rig canvas equal canvas reference units of the
/// painted screen (the rig canvas is sized to the screen quad's average
/// dimensions), so the existing fit-to-glass card math carries over 1:1.
/// </summary>
public class HudScreenProjector : MonoBehaviour
{
    const float ParkHeight = 100000f;   // matches GForceHUD's thrust-widget park
    const float ParkSide = 4000f;       // keeps the two cluster rigs + thrust widget apart
    const float RigScale = 0.01f;       // sub-pixel from any real camera
    const float RTPixelsPerUnit = 3f;   // RT pixels per canvas reference unit (crisp at 4K)

    GameObject _rig;
    RectTransform _rigCanvasRT;
    Camera _cam;
    RenderTexture _rt;
    HousingScreenWarp _warp;
    Canvas _hostCanvas;
    Camera _mainCam;
    Vector3 _parkOffset;

    /// Parent for the cluster card inside the capture rig.
    public RectTransform ContentRoot => _rigCanvasRT;
    public HousingScreenWarp Warp => _warp;

    public static HudScreenProjector Attach(string label, Canvas hostCanvas, Vector2 logicalSize, float parkSideSign)
    {
        var p = hostCanvas.gameObject.AddComponent<HudScreenProjector>();
        p._hostCanvas = hostCanvas;
        p._parkOffset = new Vector3(parkSideSign * ParkSide, ParkHeight, 0f);
        p.BuildRig(label, logicalSize);
        p.BuildWarp(label);
        return p;
    }

    void BuildRig(string label, Vector2 logicalSize)
    {
        _rig = new GameObject(label + "_ScreenRig");
        DontDestroyOnLoad(_rig);
        _rig.transform.position = _parkOffset;
        _rig.transform.localScale = Vector3.one * RigScale;

        var canvasGo = new GameObject("RigCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(_rig.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        _rigCanvasRT = (RectTransform)canvasGo.transform;
        _rigCanvasRT.sizeDelta = logicalSize;

        var camGo = new GameObject("RigCamera");
        camGo.transform.SetParent(_rig.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -300f);
        _cam = camGo.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = 10f;
        _cam.depth = -45f;          // after the thrust-widget cam (-50), before main
        _cam.allowHDR = false;
        _cam.allowMSAA = false;
        _cam.useOcclusionCulling = false;

        SetLogicalSize(logicalSize);
    }

    void BuildWarp(string label)
    {
        var go = new GameObject(label + "_ScreenWarp", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(_hostCanvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _warp = go.AddComponent<HousingScreenWarp>();
        _warp.color = Color.white;
        _warp.raycastTarget = false;
        _warp.SetTexture(_rt);
        // Gain 1.0 — must match the frame art's sway exactly, or the content
        // slides off the painted screens (cards used 0.85 for parallax back
        // when they floated on their own).
        HelmetSway.Register(rt, 1f);
    }

    /// Resize the capture rect (ref units). Recreates the RT if needed.
    public void SetLogicalSize(Vector2 logicalSize)
    {
        _rigCanvasRT.sizeDelta = logicalSize;
        _cam.orthographicSize = logicalSize.y * 0.5f * RigScale;

        int w = Mathf.Max(16, Mathf.RoundToInt(logicalSize.x * RTPixelsPerUnit));
        int h = Mathf.Max(16, Mathf.RoundToInt(logicalSize.y * RTPixelsPerUnit));
        if (_rt == null || _rt.width != w || _rt.height != h)
        {
            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _rt.Create();
            _cam.targetTexture = _rt;
            if (_warp != null) _warp.SetTexture(_rt);
        }
        // AFTER targetTexture — assigning a render target resets the aspect
        // override to the texture's ratio (RT px are rounded; the logical
        // ratio is exact, so a rounded aspect would clip a sliver of canvas).
        _cam.aspect = logicalSize.x / logicalSize.y;
    }

    void LateUpdate()
    {
        // Throttle-free: only a null-check plus transform writes (repo rule
        // covers Find* calls — Camera.main is cached, lazily re-found).
        if (_mainCam == null || !_mainCam.isActiveAndEnabled)
        {
            if (Time.unscaledTime >= _nextCamFind)
            {
                _nextCamFind = Time.unscaledTime + 0.5f;
                _mainCam = Camera.main;
            }
        }
        if (_mainCam != null && _rig != null)
            _rig.transform.position = _mainCam.transform.position + _parkOffset;

        // Don't burn a render while the cluster is hidden (map open, main
        // menu, HUD hidden) — the warp graphic lives on the host canvas, so
        // its visibility gate is the canvas itself.
        bool show = _hostCanvas != null && _hostCanvas.enabled && _hostCanvas.gameObject.activeInHierarchy;
        if (_cam != null && _cam.enabled != show) _cam.enabled = show;
    }

    float _nextCamFind;

    void OnDestroy()
    {
        if (_rt != null) { if (_cam != null) _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
        if (_warp != null) Destroy(_warp.gameObject);
        if (_rig != null) Destroy(_rig);
    }
}
