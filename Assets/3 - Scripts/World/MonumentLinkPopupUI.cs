using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Small Yes/No confirmation shown before a monument opens its song's music video
// in the browser. Nudges the player to play the song in the background (no music
// licensing in-game — we just point at the real source). Code-built auto-singleton,
// same robust pattern as NewspaperReaderUI (survives builds + warm reloads).
public class MonumentLinkPopupUI : MonoBehaviour {

    public static MonumentLinkPopupUI Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    int _consumedEscapeFrame = -1;
    public static bool ConsumedEscapeThisFrame =>
        Instance != null && Instance._consumedEscapeFrame == Time.frameCount;

    const string Message =
        "This will bring you to the music video for this song, its recommended you play it " +
        "in the background or in a pop out player while you enjoy the monument. Or dont, your choice.";

    static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.13f, 0.98f);
    static readonly Color BorderColor = new Color(0.45f, 0.78f, 0.85f, 1f);
    static readonly Color TextColor   = new Color(0.92f, 0.92f, 0.90f, 1f);
    static readonly Color HeaderColor = new Color(0.55f, 0.85f, 0.92f, 1f);
    static readonly Color YesColor    = new Color(0.18f, 0.45f, 0.32f, 1f);
    static readonly Color NoColor     = new Color(0.30f, 0.30f, 0.34f, 1f);
    static readonly Color DimColor    = new Color(0f, 0f, 0f, 0.62f);

    Canvas _canvas;
    CanvasGroup _group;
    TextMeshProUGUI _header;
    string _url;

    bool _prevModalFlag;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate() {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("MonumentLinkPopupUI");
        DontDestroyOnLoad(go);
        go.AddComponent<MonumentLinkPopupUI>();
    }

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        SetVisible(false);
    }

    void OnDestroy() {
        if (Instance != this) return;
        if (IsOpen) ExitState();
        IsOpen = false;
        Instance = null;
    }

    public static void Open(string url, string songLabel) {
        if (Instance == null || string.IsNullOrWhiteSpace(url)) return;
        Instance.OpenInternal(url, songLabel);
    }

    void OpenInternal(string url, string songLabel) {
        if (IsOpen) return;
        _url = url;
        _header.text = string.IsNullOrEmpty(songLabel) ? "Open the music video?" : songLabel;
        IsOpen = true;
        SetVisible(true);
        EnterState();
    }

    void No() {
        if (!IsOpen) return;
        IsOpen = false;
        SetVisible(false);
        ExitState();
        _url = null;
    }

    void Yes() {
        if (!IsOpen) return;
        string u = _url;
        No();                                   // restore state first
        if (!string.IsNullOrWhiteSpace(u)) Application.OpenURL(u);
    }

    void Update() {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.N)) { _consumedEscapeFrame = Time.frameCount; No(); return; }
        if (Input.GetKeyDown(KeyCode.Y) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Yes();
    }

    void EnterState() {
        _prevModalFlag = PlayerController.isInModalSlotUI;
        PlayerController.isInModalSlotUI = true; // lock move/look + suppress camera fx input
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    void ExitState() {
        PlayerController.isInModalSlotUI = _prevModalFlag;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void SetVisible(bool v) {
        if (_group == null) return;
        _group.alpha = v ? 1f : 0f;
        _group.interactable = v;
        _group.blocksRaycasts = v;
        if (_canvas != null) _canvas.enabled = v;
    }

    void BuildCanvas() {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 2100; // above HUD + the newspaper reader
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1.0f; // height-match: ultrawide-safe
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();

        var dim = NewUI("Dimmer", transform);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = DimColor;
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(No); // click outside = No

        var panel = NewUI("Panel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(760f, 380f);
        panel.anchoredPosition = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = PanelColor;

        // accent top border
        var border = NewUI("TopBorder", panel);
        border.anchorMin = new Vector2(0f, 1f); border.anchorMax = new Vector2(1f, 1f); border.pivot = new Vector2(0.5f, 1f);
        border.offsetMin = new Vector2(0f, -4f); border.offsetMax = new Vector2(0f, 0f);
        border.gameObject.AddComponent<Image>().color = BorderColor;

        const float pad = 44f;

        _header = NewText(panel, "Header", "", 28f, FontStyles.Bold, HeaderColor);
        _header.alignment = TextAlignmentOptions.Top;
        TopBand(_header.rectTransform, 34f, 40f, pad);

        var msg = NewText(panel, "Message", Message, 23f, FontStyles.Normal, TextColor);
        msg.alignment = TextAlignmentOptions.Top;
        msg.enableWordWrapping = true;
        msg.lineSpacing = 6f;
        TopBand(msg.rectTransform, 96f, 170f, pad);

        // Yes / No buttons
        var yes = BuildButton(panel, "YesButton", "Yes", YesColor);
        var yRT = yes.GetComponent<RectTransform>();
        yRT.anchorMin = new Vector2(0.5f, 0f); yRT.anchorMax = new Vector2(0.5f, 0f); yRT.pivot = new Vector2(1f, 0f);
        yRT.sizeDelta = new Vector2(230f, 64f); yRT.anchoredPosition = new Vector2(-18f, 40f);
        yes.onClick.AddListener(Yes);

        var no = BuildButton(panel, "NoButton", "No", NoColor);
        var nRT = no.GetComponent<RectTransform>();
        nRT.anchorMin = new Vector2(0.5f, 0f); nRT.anchorMax = new Vector2(0.5f, 0f); nRT.pivot = new Vector2(0f, 0f);
        nRT.sizeDelta = new Vector2(230f, 64f); nRT.anchoredPosition = new Vector2(18f, 40f);
        no.onClick.AddListener(No);
    }

    // ── helpers ──
    static RectTransform NewUI(string name, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }
    static void Stretch(RectTransform rt) {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
    static void TopBand(RectTransform rt, float topY, float height, float sidePad) {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(sidePad, -(topY + height));
        rt.offsetMax = new Vector2(-sidePad, -topY);
    }
    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color) {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color; t.raycastTarget = false;
        return t;
    }
    static Button BuildButton(RectTransform parent, string name, string label, Color bg) {
        var rt = NewUI(name, parent);
        rt.gameObject.AddComponent<Image>().color = bg;
        var btn = rt.gameObject.AddComponent<Button>();
        var t = NewText(rt, "Label", label, 24f, FontStyles.Bold, Color.white);
        t.alignment = TextAlignmentOptions.Center;
        Stretch(t.rectTransform);
        return btn;
    }
}
