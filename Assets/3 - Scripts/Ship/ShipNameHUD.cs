using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Bottom-middle text label that names the ship the player is currently
/// piloting — "Ship 1", "Ship 2", etc. Reads BoughtShip.shipNumber. The main
/// scene ship (no BoughtShip marker) shows "Main Ship".
///
/// Hidden when the player is on foot or the map is open. Same cyan-scanner
/// palette as the rest of the cockpit UI so it reads as part of the suite.
/// Auto-created singleton, mirrors GForceHUD's bootstrap pattern.
/// </summary>
public class ShipNameHUD : MonoBehaviour
{
    public static ShipNameHUD Instance { get; private set; }

    Canvas _canvas;
    TextMeshProUGUI _label;
    Ship _cachedShip;
    string _lastShown = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("ShipNameHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<ShipNameHUD>();
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
        _canvas.sortingOrder = 825; // above HUD background (~800), below pause menu / map (1000+)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var go = new GameObject("ShipName", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 40f);
        rt.sizeDelta = new Vector2(360f, 36f);
        _label = go.AddComponent<TextMeshProUGUI>();
        // Multi-step font fallback — same resolver TutorialUI / GForceHUD
        // use. Single-asset Resources.Load was failing in build, leaving
        // the label with no font and invisible.
        HudFontResolver.Apply(_label);
        _label.fontSize = 22;
        _label.fontStyle = FontStyles.Bold;
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.92f);
        _label.raycastTarget = false;
        _label.outlineColor = GalaxyHudKit.LabelGlow;
        _label.outlineWidth = 0.2f;
    }

    void LateUpdate()
    {
        // Refresh cached piloted ship — handles teleport-between-ships.
        // Use the cached static (set on PilotShip / cleared on exit).
        if (_cachedShip == null || !_cachedShip.IsPiloted) _cachedShip = Ship.PilotedInstance;

        bool show = _cachedShip != null && !PlayerController.isMapOpen;
        if (_canvas != null && _canvas.enabled != show) _canvas.enabled = show;
        if (!show) return;

        string text = ResolveShipName(_cachedShip);
        if (text != _lastShown)
        {
            _lastShown = text;
            _label.text = text;
        }
    }

    static string ResolveShipName(Ship s)
    {
        var marker = s.GetComponent<BoughtShip>();
        if (marker != null && marker.shipNumber > 0) return $"Ship {marker.shipNumber}";
        return "Main Ship";
    }
}
