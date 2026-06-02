using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// End-of-tutorial report card. Shows per-step durations, a total, and a letter
/// grade with a randomly-selected funny phrase. Procedurally builds its own
/// ScreenSpaceOverlay canvas — same convention as TutorialUI / BonusTutorial /
/// SaveLoadUI / GoodsVendorShopUI.
/// </summary>
public class TutorialPerformanceReview : MonoBehaviour
{
    public static TutorialPerformanceReview Instance { get; private set; }

    public struct Entry { public string label; public float seconds; }

    /// Grade thresholds (TOTAL seconds across all completed steps).
    /// F = anything above d.
    public struct GradeThresholds
    {
        public float a, b, c, d;
        // Default thresholds for the main intro tutorial — about a dozen steps.
        public static GradeThresholds Main =>
            new GradeThresholds { a = 60f, b = 150f, c = 240f, d = 330f };
        // Scaled thresholds for mini tutorials. The main tutorial averages
        // ~5 / 12.5 / 20 / 27.5 seconds per step at A / B / C / D, so we
        // scale per stepCount. Mini tutorials with longer per-step work
        // (e.g. axe-building's "gather 60 wood") will run hotter — players
        // can re-roll the grade by replaying.
        public static GradeThresholds Mini(int stepCount)
        {
            float n = Mathf.Max(1, stepCount);
            return new GradeThresholds
            {
                a = n * 8f,
                b = n * 18f,
                c = n * 32f,
                d = n * 50f,
            };
        }
    }

    GradeThresholds _activeThresholds = GradeThresholds.Main;
    System.Action _onContinue;

    Canvas canvas;
    GameObject root;
    TextMeshProUGUI gradeText;
    TextMeshProUGUI totalText;
    TextMeshProUGUI quipText;
    TextMeshProUGUI _headerText;
    RectTransform rowsContainer;

    CursorLockMode prevLockMode;
    bool prevCursorVisible;

    static readonly Color32 C_Dim       = new Color32(0x00, 0x00, 0x00, 0xCC);
    static readonly Color32 C_Card      = new Color32(0x12, 0x07, 0x2C, 0xF5);
    static readonly Color32 C_Border    = new Color32(0x5B, 0xD8, 0xFF, 0xFF);
    static readonly Color32 C_Header    = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
    static readonly Color32 C_Body      = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
    static readonly Color32 C_RowEven   = new Color32(0xFF, 0xFF, 0xFF, 0x12);
    static readonly Color32 C_Time      = new Color32(0xFF, 0xE3, 0x80, 0xFF);
    static readonly Color32 C_Btn       = new Color32(0x2F, 0x70, 0xC8, 0xFF);
    static readonly Color32 C_BtnHover  = new Color32(0x4A, 0x8E, 0xE6, 0xFF);

    static readonly Color C_GradeA = new Color(0.55f, 0.95f, 0.55f);  // bright green
    static readonly Color C_GradeB = new Color(0.65f, 0.92f, 1.0f);   // light cyan
    static readonly Color C_GradeC = new Color(1.0f,  0.92f, 0.55f);  // yellow
    static readonly Color C_GradeD = new Color(1.0f,  0.65f, 0.45f);  // orange
    static readonly Color C_GradeF = new Color(1.0f,  0.40f, 0.45f);  // red

    static readonly string[] PhrasesA = {
        "Mission control just nominated you for Astronaut of the Decade.",
        "Even the universe is impressed.",
        "You move faster than light leaves a black hole.",
        "Do you... live here? In tutorials?",
        "Reflexes detected: cheat codes engaged.",
    };
    static readonly string[] PhrasesB = {
        "Solid showing. The aliens approve.",
        "Above-average human. Stand by for galactic recognition.",
        "Top quartile. The simulation didn't even need a coffee break.",
        "Promising candidate. We'll keep an eye on you.",
        "Slightly above average — your mother would be proud.",
    };
    static readonly string[] PhrasesC = {
        "Average performance. Don't quit your day job (good thing you don't have one).",
        "Adequate. The cosmos shrugs.",
        "Statistically: middle of the pack. Cosmically: unremarkable.",
        "We've seen worse. We've also seen better.",
        "You did the things. Eventually.",
    };
    static readonly string[] PhrasesD = {
        "You finished. Eventually. We made coffee.",
        "Were you... checking your phone?",
        "The planet aged a millennium watching you stand up.",
        "Have you considered a slower pace? Just kidding — please don't.",
        "Mission control sent flowers. They wilted.",
    };
    static readonly string[] PhrasesF = {
        "Even the tutorial is embarrassed.",
        "We strongly recommend a refund.",
        "We dispatched a search party halfway through.",
        "Are you SURE you crashed and didn't just take a nap?",
        "Consider an easier game. Like Solitaire. On easy mode.",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("TutorialPerformanceReview");
        DontDestroyOnLoad(go);
        go.AddComponent<TutorialPerformanceReview>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        if (root != null) root.SetActive(false);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // The review is DontDestroyOnLoad — make sure it can't follow the player
    // back to the main menu (e.g. if they Esc'd to the pause menu, returned to
    // the main menu, and the review canvas leaked over). MenuSceneCleanup
    // also destroys this object, but this hides the canvas immediately on the
    // very first frame of MainMenu in case the cleanup script doesn't run
    // before our Update.
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu" && root != null && root.activeSelf)
        {
            root.SetActive(false);
            PlayerController.isInDialogue = false;
        }
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        // Defensive cursor pinning — NPC dialogue's CloseChoicePanel and other
        // exit hooks Cursor.lockState = Locked at end-of-conversation, which
        // can fire on the same frame the review opens (the third NPC's
        // greeting triggers the talk-to-NPCs step → tutorial finishes →
        // review.Show()). Without this re-assertion the player can't click
        // Continue or scroll. Cheap (one cursor write per frame while open).
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
        // Esc fallback — if the player can't see the cursor for any reason,
        // they can still dismiss the modal from the keyboard.
        if (Input.GetKeyDown(KeyCode.Escape)) OnContinueClicked();
    }

    public void Show(List<Entry> entries) => Show(entries, GradeThresholds.Main, null, "TUTORIAL PERFORMANCE REVIEW");

    public void Show(List<Entry> entries, GradeThresholds thresholds, System.Action onContinue, string headerOverride = null)
    {
        if (root == null) return;
        if (entries == null) entries = new List<Entry>();
        _activeThresholds = thresholds;
        _onContinue = onContinue;
        if (!string.IsNullOrEmpty(headerOverride) && _headerText != null)
            _headerText.text = headerOverride;

        // In standalone builds the gameplay scene may have no EventSystem
        // (the editor often has one auto-spawned by Inspector tooling, the
        // build doesn't). Without an EventSystem the GraphicRaycaster never
        // hands clicks to the Continue button, the dim image swallows all
        // input, and the modal is effectively invisible / stuck. Spawn one
        // on demand if the scene is missing one.
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var es = new GameObject("ReviewEventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            DontDestroyOnLoad(es);
        }
        if (canvas != null) canvas.enabled = true;

        PopulateRows(entries);

        float total = 0f;
        for (int i = 0; i < entries.Count; i++) total += entries[i].seconds;
        if (totalText != null) totalText.text = $"TOTAL: <color=#FFE380>{FormatDuration(total)}</color>";

        char grade = GradeFor(total);
        if (gradeText != null)
        {
            gradeText.text = grade.ToString();
            gradeText.color = ColorFor(grade);
        }
        if (quipText != null) quipText.text = PickQuip(grade);

        prevLockMode = Cursor.lockState;
        prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerController.isInDialogue = true;

        root.SetActive(true);
    }

    void OnContinueClicked()
    {
        root.SetActive(false);
        // If a callback is set, the caller (e.g. BonusTutorial) is responsible
        // for restoring cursor / dialogue state — they may want the cursor to
        // stay free for an immediate follow-up modal. Default behaviour is to
        // re-lock the cursor like the original implementation.
        var cb = _onContinue;
        _onContinue = null;
        if (cb != null)
        {
            cb.Invoke();
            return;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PlayerController.isInDialogue = false;
    }

    char GradeFor(float totalSeconds)
    {
        if (totalSeconds < _activeThresholds.a) return 'A';
        if (totalSeconds < _activeThresholds.b) return 'B';
        if (totalSeconds < _activeThresholds.c) return 'C';
        if (totalSeconds < _activeThresholds.d) return 'D';
        return 'F';
    }

    static Color ColorFor(char grade)
    {
        switch (grade)
        {
            case 'A': return C_GradeA;
            case 'B': return C_GradeB;
            case 'C': return C_GradeC;
            case 'D': return C_GradeD;
            default:  return C_GradeF;
        }
    }

    static string PickQuip(char grade)
    {
        string[] pool;
        switch (grade)
        {
            case 'A': pool = PhrasesA; break;
            case 'B': pool = PhrasesB; break;
            case 'C': pool = PhrasesC; break;
            case 'D': pool = PhrasesD; break;
            default:  pool = PhrasesF; break;
        }
        if (pool == null || pool.Length == 0) return "";
        return pool[Random.Range(0, pool.Length)];
    }

    static string FormatDuration(float seconds)
    {
        int s = Mathf.RoundToInt(seconds);
        if (s < 60) return $"{seconds:0.0}s";
        int m = s / 60;
        int rem = s % 60;
        return $"{m}m {rem:00}s";
    }

    /// <summary>Convert "PostCrashExamStep" → "Post Crash Exam".</summary>
    public static string PrettifyTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "";
        if (typeName.EndsWith("Step")) typeName = typeName.Substring(0, typeName.Length - 4);
        var sb = new System.Text.StringBuilder(typeName.Length + 4);
        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(typeName[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    void PopulateRows(List<Entry> entries)
    {
        if (rowsContainer == null) return;
        for (int i = rowsContainer.childCount - 1; i >= 0; i--)
            Destroy(rowsContainer.GetChild(i).gameObject);

        for (int i = 0; i < entries.Count; i++)
        {
            var rt = NewUI("Row", rowsContainer);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
            le.flexibleWidth = 1f;

            // Optional zebra-stripe background
            if (i % 2 == 0)
            {
                var bg = rt.gameObject.AddComponent<Image>();
                bg.color = C_RowEven;
                bg.raycastTarget = false;
            }

            var label = NewText("Label", rt, entries[i].label, 14f, FontStyles.Normal, C_Body, TextAlignmentOptions.MidlineLeft);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0.7f, 1f);
            lrt.offsetMin = new Vector2(12f, 0f);
            lrt.offsetMax = new Vector2(0f, 0f);

            var time = NewText("Time", rt, FormatDuration(entries[i].seconds), 14f, FontStyles.Bold, C_Time, TextAlignmentOptions.MidlineRight);
            var trt = time.rectTransform;
            trt.anchorMin = new Vector2(0.7f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(0f, 0f);
            trt.offsetMax = new Vector2(-12f, 0f);
        }
    }

    void BuildCanvas()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var dim = NewUI("Dim", transform);
        Stretch(dim, 0, 0, 0, 0);
        var dimImg = dim.gameObject.AddComponent<Image>();
        dimImg.color = C_Dim;
        root = dim.gameObject;

        var card = NewUI("Card", dim);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(720f, 760f);
        card.anchoredPosition = Vector2.zero;
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = C_Card;
        var cardShadow = card.gameObject.AddComponent<Outline>();
        cardShadow.effectColor = C_Border;
        cardShadow.effectDistance = new Vector2(2f, -2f);

        // Top accent bar
        var accent = NewUI("Accent", card);
        accent.anchorMin = new Vector2(0f, 1f);
        accent.anchorMax = new Vector2(1f, 1f);
        accent.pivot = new Vector2(0.5f, 1f);
        accent.anchoredPosition = new Vector2(0f, -2f);
        accent.sizeDelta = new Vector2(-44f, 4f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.color = C_Border;

        // Header
        _headerText = NewText("Header", card,
            "TUTORIAL PERFORMANCE REVIEW", 28f, FontStyles.Bold, C_Header, TextAlignmentOptions.Center);
        var header = _headerText;
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0f, 1f);
        hrt.anchorMax = new Vector2(1f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.sizeDelta = new Vector2(-40f, 50f);
        hrt.anchoredPosition = new Vector2(0f, -28f);
        header.characterSpacing = 4f;

        // Subhead
        var sub = NewText("Subhead", card,
            "Per-step times — review your skill progression",
            16f, FontStyles.Italic, C_Body, TextAlignmentOptions.Center);
        var srt = sub.rectTransform;
        srt.anchorMin = new Vector2(0f, 1f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.sizeDelta = new Vector2(-40f, 24f);
        srt.anchoredPosition = new Vector2(0f, -78f);

        // Step rows container — anchored stretch; bottom edge sits above the
        // Total/Grade/Quip/Continue stack so rows never poke through them.
        // RectMask2D matches the convention used elsewhere (SaveLoadUI) and
        // clips overflow cleanly when more steps land in the tutorial later.
        var rowsHost = NewUI("RowsHost", card);
        rowsHost.anchorMin = new Vector2(0f, 0f);
        rowsHost.anchorMax = new Vector2(1f, 1f);
        rowsHost.pivot = new Vector2(0.5f, 1f);
        rowsHost.offsetMin = new Vector2(28f, 308f);
        rowsHost.offsetMax = new Vector2(-28f, -110f);

        var rowsBg = rowsHost.gameObject.AddComponent<Image>();
        rowsBg.color = new Color(0f, 0f, 0f, 0.25f);
        rowsBg.raycastTarget = false;
        rowsHost.gameObject.AddComponent<RectMask2D>();

        // Inner container with VerticalLayoutGroup for the rows
        rowsContainer = NewUI("Rows", rowsHost);
        Stretch(rowsContainer, 4, 4, -4, -4);
        var vlg = rowsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 1f;
        vlg.padding = new RectOffset(0, 0, 4, 4);
        var fitter = rowsContainer.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Bottom stack (each anchored to the card's bottom edge with pivot
        // (0.5, 0) so anchoredPosition.y reads as "distance above the card
        // floor"). Stacked from card-bottom up: Continue → Quip → Grade →
        // Total. Y values are non-overlapping. Was previously buggy: Quip and
        // Continue both sat at y≈14-18 with overlapping vertical extents,
        // drawing the italic phrase through the button text.

        // Continue button — anchored to the bottom of the card.
        var btn = NewButton("ContinueBtn", card, "CONTINUE", C_Btn, C_BtnHover);
        var brt = btn.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.sizeDelta = new Vector2(220f, 50f);
        brt.anchoredPosition = new Vector2(0f, 20f);
        btn.onClick.AddListener(OnContinueClicked);

        // Quip line above the button.
        quipText = NewText("Quip", card, "", 14f, FontStyles.Italic, C_Body, TextAlignmentOptions.Center);
        var qrt = quipText.rectTransform;
        qrt.anchorMin = new Vector2(0f, 0f);
        qrt.anchorMax = new Vector2(1f, 0f);
        qrt.pivot = new Vector2(0.5f, 0f);
        qrt.sizeDelta = new Vector2(-60f, 40f);
        qrt.anchoredPosition = new Vector2(0f, 80f);
        quipText.enableWordWrapping = true;

        // Big grade letter above the quip.
        gradeText = NewText("Grade", card, "A", 96f, FontStyles.Bold, C_GradeA, TextAlignmentOptions.Center);
        var grt = gradeText.rectTransform;
        grt.anchorMin = new Vector2(0f, 0f);
        grt.anchorMax = new Vector2(1f, 0f);
        grt.pivot = new Vector2(0.5f, 0f);
        grt.sizeDelta = new Vector2(-40f, 100f);
        grt.anchoredPosition = new Vector2(0f, 130f);
        var gradeShadow = gradeText.gameObject.AddComponent<Shadow>();
        gradeShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        gradeShadow.effectDistance = new Vector2(0f, -4f);

        // Total time row above the grade.
        totalText = NewText("Total", card, "TOTAL: 0s", 20f, FontStyles.Bold, C_Body, TextAlignmentOptions.Center);
        var tortt = totalText.rectTransform;
        tortt.anchorMin = new Vector2(0f, 0f);
        tortt.anchorMax = new Vector2(1f, 0f);
        tortt.pivot = new Vector2(0.5f, 0f);
        tortt.sizeDelta = new Vector2(-40f, 28f);
        tortt.anchoredPosition = new Vector2(0f, 240f);
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static Button NewButton(string name, Transform parent, string label, Color32 normal, Color32 hover)
    {
        var rt = NewUI(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = normal;
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = normal;
        colors.highlightedColor = hover;
        colors.pressedColor = new Color32((byte)(normal.r * 0.7f), (byte)(normal.g * 0.7f), (byte)(normal.b * 0.7f), 255);
        colors.selectedColor = hover;
        btn.colors = colors;

        var text = NewText("Label", rt, label, 22f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, 0, 0, 0, 0);
        return btn;
    }
}
