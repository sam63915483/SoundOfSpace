using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Cockpit overlay drawing prograde (green) and retrograde (red) triangle
/// markers anchored to the projected velocity vector. Helps the player see
/// where they're actually heading — essential for using Match Velocity and
/// Circularize correctly.
///
/// Singleton, auto-created like GForceHUD. Hidden when the player isn't
/// piloting a ship or velocity is negligible.
/// </summary>
public class VelocityMarkersHUD : MonoBehaviour
{
    public static VelocityMarkersHUD Instance { get; private set; }

    static readonly Color PrograConfigColor = new Color(0.36f, 1f, 0.55f, 1f);
    static readonly Color RetroColor        = new Color(1f, 0.30f, 0.30f, 1f);

    Canvas _canvas;
    RectTransform _prograde;
    RectTransform _retrograde;
    Text _proLabel;
    Text _retLabel;
    Image _proImg;
    Image _retImg;
    Camera _mainCam;
    Ship _cachedShip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("VelocityMarkersHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<VelocityMarkersHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 810;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _prograde   = BuildMarker("Prograde",   PrograConfigColor, "PRO", out _proImg, out _proLabel);
        _retrograde = BuildMarker("Retrograde", RetroColor,        "RET", out _retImg, out _retLabel);
    }

    RectTransform BuildMarker(string name, Color color, string label, out Image img, out Text txt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(28f, 28f);

        img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        var labGO = new GameObject("Label", typeof(RectTransform));
        labGO.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)labGO.transform;
        lrt.anchorMin = new Vector2(0.5f, 0f);
        lrt.anchorMax = new Vector2(0.5f, 0f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -2f);
        lrt.sizeDelta = new Vector2(40f, 14f);
        txt = labGO.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 10;
        txt.fontStyle = FontStyle.Bold;
        txt.color = color;
        txt.alignment = TextAnchor.UpperCenter;
        txt.raycastTarget = false;
        return rt;
    }

    void LateUpdate()
    {
        if (_cachedShip == null || !_cachedShip.IsPiloted)
        {
            var ships = FindObjectsOfType<Ship>(true);
            _cachedShip = null;
            for (int i = 0; i < ships.Length; i++)
                if (ships[i] != null && ships[i].IsPiloted) { _cachedShip = ships[i]; break; }
        }
        if (_mainCam == null) _mainCam = Camera.main;

        bool show = _cachedShip != null && _mainCam != null;
        if (!show) { SetMarkersActive(false); return; }

        var rb = _cachedShip.GetComponent<Rigidbody>();
        if (rb == null) { SetMarkersActive(false); return; }

        Vector3 vel = rb.velocity;
        if (vel.sqrMagnitude < 1f) { SetMarkersActive(false); return; }

        // Project velocity from a reference point 100 units in front of the
        // camera along the vel direction — gives a stable screen-space anchor
        // independent of camera world position.
        Vector3 camPos = _mainCam.transform.position;
        Vector3 proPoint = camPos + vel.normalized * 100f;
        Vector3 retPoint = camPos - vel.normalized * 100f;
        Vector3 proView = _mainCam.WorldToViewportPoint(proPoint);
        Vector3 retView = _mainCam.WorldToViewportPoint(retPoint);

        bool proInFront = proView.z > 0f;
        bool retInFront = retView.z > 0f;
        SetMarkerScreen(_prograde, proView, proInFront, _cachedShip.IsMatchingVelocity);
        SetMarkerScreen(_retrograde, retView, retInFront, false);
    }

    void SetMarkerScreen(RectTransform rt, Vector3 viewport, bool inFront, bool emphasize)
    {
        if (!inFront) { rt.gameObject.SetActive(false); return; }
        rt.gameObject.SetActive(true);
        Vector2 px = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        rt.position = px;
        rt.localScale = emphasize ? Vector3.one * 1.25f : Vector3.one;
    }

    void SetMarkersActive(bool active)
    {
        if (_prograde != null) _prograde.gameObject.SetActive(active);
        if (_retrograde != null) _retrograde.gameObject.SetActive(active);
    }
}
