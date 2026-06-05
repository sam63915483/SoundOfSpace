using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Code-built HUD for the OxygenManager pools (mirrors the self-built-canvas
/// pattern used by VitalsHUD / HALLineHUD). Suit meter is always visible; the
/// hull meter fades in while piloting, inside the ship, or while the hull is
/// breaching. Bars use the ResourceHUD fill trick: a left-pivot fill scaled on X.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
public class OxygenHUD : MonoBehaviour
{
    public static OxygenHUD Instance { get; private set; }

    RectTransform hullFill;   // suit bar moved to VitalsHUD (§2)
    CanvasGroup hullGroup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("OxygenHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<OxygenHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildUI()
    {
        var canvasGO = new GameObject("OxygenCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        // Hide over MainMenu like the other HUDs.
        HUDSceneGate.Register(canvas.rootCanvas);

        // §2: the SUIT O2 bar moved into the bottom-right vitals card (VitalsHUD).
        // Only the contextual HULL O2 bar remains here — it fades in while
        // piloting / inside the ship / breaching. Takes the suit bar's old slot.
        hullFill = MakeBar(canvasGO.transform, "HullO2", new Vector2(24f, -300f),
                           new Color32(0xFF, 0xC8, 0x5C, 0xFF), "HULL O2", out var hullRow);
        hullGroup = hullRow.AddComponent<CanvasGroup>();
        hullGroup.alpha = 0f;
    }

    // Builds a label + background + left-pivot fill row anchored top-left.
    // Returns the fill RectTransform (scaled on X each frame); `rowGO` is the
    // whole row container (so the hull row can get a CanvasGroup for fading).
    RectTransform MakeBar(Transform parent, string name, Vector2 anchoredPos,
                          Color color, string labelText, out GameObject rowGO)
    {
        rowGO = new GameObject(name + "Row", typeof(RectTransform));
        var rowRT = (RectTransform)rowGO.transform;
        rowRT.SetParent(parent, false);
        rowRT.anchorMin = rowRT.anchorMax = new Vector2(0f, 1f);
        rowRT.pivot = new Vector2(0f, 1f);
        rowRT.anchoredPosition = anchoredPos;
        rowRT.sizeDelta = new Vector2(260f, 24f);

        // Label (left 66px).
        var labGO = new GameObject("Label", typeof(RectTransform));
        var labRT = (RectTransform)labGO.transform;
        labRT.SetParent(rowRT, false);
        labRT.anchorMin = new Vector2(0f, 0f);
        labRT.anchorMax = new Vector2(0f, 1f);
        labRT.pivot = new Vector2(0f, 0.5f);
        labRT.anchoredPosition = Vector2.zero;
        labRT.sizeDelta = new Vector2(66f, 0f);
        var lab = labGO.AddComponent<TextMeshProUGUI>();
        lab.text = labelText;
        lab.fontSize = 13f;
        lab.alignment = TextAlignmentOptions.MidlineLeft;
        lab.color = Color.white;

        // Background (right of label, stretched).
        var bgGO = new GameObject("BG", typeof(RectTransform));
        var bgRT = (RectTransform)bgGO.transform;
        bgRT.SetParent(rowRT, false);
        bgRT.anchorMin = new Vector2(0f, 0f);
        bgRT.anchorMax = new Vector2(1f, 1f);
        bgRT.pivot = new Vector2(0f, 0.5f);
        bgRT.offsetMin = new Vector2(70f, 4f);
        bgRT.offsetMax = new Vector2(0f, -4f);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.5f);

        // Fill (stretched inside BG, LEFT pivot, scaled on X).
        var fillGO = new GameObject("Fill", typeof(RectTransform));
        var fillRT = (RectTransform)fillGO.transform;
        fillRT.SetParent(bgRT, false);
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = color;

        return fillRT;
    }

    void Update()
    {
        var om = OxygenManager.Instance;
        if (om == null) return;

        SetBar(hullFill, om.HullPercent);

        // Only show the hull bar when the player is actually with the ship —
        // piloting, inside, or (while it's venting) within its 25 m radius.
        // Without the proximity gate the bar lingered on screen when the player
        // had wandered 50 m+ away from a draining ship.
        bool showHull = (om.PlayerPiloting || om.PlayerInsideShip
                        || om.State == OxygenManager.HullState.Draining)
                        && om.ShipPromptsAudible;
        if (hullGroup != null) hullGroup.alpha = showHull ? 1f : 0f;
    }

    static void SetBar(RectTransform fill, float percent)
    {
        if (fill == null) return;
        fill.localScale = new Vector3(Mathf.Clamp01(percent), 1f, 1f);
    }
}
