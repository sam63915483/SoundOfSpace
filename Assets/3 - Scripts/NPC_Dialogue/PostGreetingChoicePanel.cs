using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared choice panel used by every NPC after their greeting line, restyled
/// 2026-08-30 to the PHOSPHOR terminal look (Sam's pick from the four mockups
/// in prototypes/dialogue-ui/index.html — palette and builders live in
/// PhosphorUI, shared with PhosphorDialogueBox so the spoken line and the
/// choices read as one machine).
///
/// Rows are label-only — Sam: numbers on the options are redundant. The 1-9
/// digit hotkeys still work as an invisible affordance; the row is chosen by
/// click or by key, never by reading an index off the screen. Rows fade in
/// with a small stagger and light up phosphor-green on hover.
///
/// Singleton — built procedurally on first use, lives on a DontDestroyOnLoad
/// canvas at sortingOrder 900 (above gameplay, below pause menu).
/// </summary>
public class PostGreetingChoicePanel : MonoBehaviour
{
    public static PostGreetingChoicePanel Instance { get; private set; }

    public struct Row
    {
        public string label;
        public bool enabled;
        public Row(string label, bool enabled = true) { this.label = label; this.enabled = enabled; }
    }

    Canvas _canvas;
    RectTransform _panelRT;
    readonly List<GameObject> _rowGOs = new List<GameObject>();
    readonly List<Row> _currentRows = new List<Row>();
    Action<int> _onSelect;
    bool _visible;

    public bool IsVisible => _visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // No MainMenu skip — see SpaceDustSellUI.AutoCreate for rationale.
        // Panel is SetActive(false) by default so the canvas is invisible.
        if (Instance != null) return;
        var go = new GameObject("PostGreetingChoicePanel");
        DontDestroyOnLoad(go);
        go.AddComponent<PostGreetingChoicePanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 900;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        _panelRT = (RectTransform)panel.transform;
        // Same footprint as PhosphorDialogueBox's plate — the choices replace
        // the spoken line in place, so the two must sit identically.
        _panelRT.anchorMin = new Vector2(0.5f, 0f);
        _panelRT.anchorMax = new Vector2(0.5f, 0f);
        _panelRT.pivot     = new Vector2(0.5f, 0f);
        _panelRT.anchoredPosition = new Vector2(0f, 150f);
        _panelRT.sizeDelta = new Vector2(720f, 200f);
        var bg = panel.AddComponent<Image>();
        bg.color = PhosphorUI.Plate;
        bg.raycastTarget = true;

        PhosphorUI.AddBorder(_panelRT);
        _scan = PhosphorUI.AddScanlines(_panelRT);

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(14, 14, 14, 14);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        gameObject.SetActive(true);
        _panelRT.gameObject.SetActive(false);
    }

    public void Show(IList<Row> rows, Action<int> onSelect)
    {
        ClearRows();
        _currentRows.Clear();
        for (int i = 0; i < rows.Count; i++) _currentRows.Add(rows[i]);
        _onSelect = onSelect;
        for (int i = 0; i < rows.Count; i++)
        {
            BuildRow(i, rows[i]);
        }
        _panelRT.gameObject.SetActive(true);
        _visible = true;
        _crtT = 0f;                 // CRT turn-on, mirrors PhosphorDialogueBox
        HideSpokenLine();
        // Free the cursor so the player can click rows with the mouse in
        // addition to the 1-9 hotkeys. NPCDialogue locks the cursor again
        // when its typewriter finishes (NPCDialogue.cs:331), so we have to
        // override that on Show AND keep enforcing it in Update — same
        // pattern SpaceDustSellUI uses while open.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    // The NPC's spoken line and this choice list are both full-width and both
    // sit low on the screen, so they overlapped: the greeting stayed up for the
    // whole conversation (every NPC only clears it in StopConversation), and
    // coming back from the sell panel redrew the rows straight over it.
    //
    // Hidden HERE rather than in each NPC because there are four of them plus
    // NPCDialogue, and a fifth would forget. Safe to hide unconditionally
    // because DialogueTextStyling.RevealChars* re-activates the label before it
    // types — every typewriter in the game goes through there — so a line spoken
    // AFTER a choice (Tev's branching questions) still shows.
    //
    // The label is shared across NPCs and owned by NPCDialogue; resolved once
    // and cached, never per-frame (CLAUDE.md: no FindObjectOfType in a loop).
    static TMPro.TextMeshProUGUI _spokenLine;

    static void HideSpokenLine()
    {
        // Re-resolve whenever it's null rather than caching a "resolved" flag:
        // this is a static on a DontDestroyOnLoad singleton, so a gameplay-scene
        // reload destroys the label while the reference lives on. Unity's null
        // check catches the destroyed object and we look it up again. Show() runs
        // a handful of times per conversation, never per frame, so the occasional
        // FindObjectOfType is free.
        if (_spokenLine == null)
        {
            var owner = FindObjectOfType<NPCDialogue>(true);
            if (owner != null) _spokenLine = owner.dialogueText;
        }
        if (_spokenLine != null && _spokenLine.gameObject.activeSelf)
            _spokenLine.gameObject.SetActive(false);
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        _onSelect = null;
        ClearRows();
        if (_panelRT != null) _panelRT.gameObject.SetActive(false);
        // Re-lock for gameplay. If Hide() was called because the player
        // picked "Sell Dust" / "Sell Items" / etc., the follow-up UI's own
        // Open will immediately set it back to unlocked — brief flicker is
        // fine. If the player picked "Leave", we want the cursor locked
        // for the resumed gameplay.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void ClearRows()
    {
        for (int i = 0; i < _rowGOs.Count; i++)
            if (_rowGOs[i] != null) Destroy(_rowGOs[i]);
        _rowGOs.Clear();
    }

    void BuildRow(int index, Row row)
    {
        var go = new GameObject($"Row{index}", typeof(RectTransform));
        go.transform.SetParent(_panelRT, false);
        var rt = (RectTransform)go.transform;

        // Flat until hovered — the PHOSPHOR look is text-first, not buttons.
        var img = go.AddComponent<Image>();
        img.color = Color.clear;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 40f;

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;   // PhosphorRow paints all states
        btn.interactable = row.enabled;
        int captured = index;
        btn.onClick.AddListener(() => HandleSelect(captured));

        // Left accent bar, lit on hover only.
        var barGO = new GameObject("Accent", typeof(RectTransform));
        barGO.transform.SetParent(go.transform, false);
        var barRT = (RectTransform)barGO.transform;
        barRT.anchorMin = new Vector2(0, 0);
        barRT.anchorMax = new Vector2(0, 1);
        barRT.offsetMin = Vector2.zero;
        barRT.offsetMax = new Vector2(2.5f, 0);
        var bar = barGO.AddComponent<Image>();
        bar.color = PhosphorUI.Phosphor;
        bar.raycastTarget = false;
        bar.enabled = false;

        // "> " marker — the mockup's prompt glyph, NOT a number (Sam: numbers
        // on the options are redundant; the digit hotkeys still work unseen).
        var pre = PhosphorUI.MakeLabel(rt, "Prefix", ">", 19f, PhosphorUI.Border);
        var prt = pre.rectTransform;
        prt.anchorMin = new Vector2(0, 0);
        prt.anchorMax = new Vector2(0, 1);
        prt.offsetMin = new Vector2(12, 0);
        prt.offsetMax = new Vector2(32, 0);
        pre.alignment = TextAlignmentOptions.MidlineLeft;

        var tmp = PhosphorUI.MakeLabel(rt, "Label", row.label, 19f,
                                       row.enabled ? PhosphorUI.RowText : PhosphorUI.RowDim);
        var lblRT = tmp.rectTransform;
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = new Vector2(34, 0);
        lblRT.offsetMax = new Vector2(-14, 0);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = true;      // a long option wraps instead of clipping

        go.AddComponent<PhosphorRow>().Init(img, bar, pre, tmp, row.enabled, index * 0.07f);
        _rowGOs.Add(go);
    }

    /// <summary>
    /// One choice row's look: staggered fade-in on birth, phosphor light-up on
    /// hover. Owns every visual state so the Button's tint machinery (which
    /// can't touch children) stays off.
    /// </summary>
    class PhosphorRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image _bg, _bar;
        TextMeshProUGUI _pre, _label;
        bool _rowEnabled;
        float _delay, _born;
        CanvasGroup _cg;

        public void Init(Image bg, Image bar, TextMeshProUGUI pre, TextMeshProUGUI label,
                         bool rowEnabled, float delay)
        {
            _bg = bg; _bar = bar; _pre = pre; _label = label;
            _rowEnabled = rowEnabled;
            _delay = delay;
            _born = Time.unscaledTime;
            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
        }

        void Update()
        {
            if (_cg == null) return;
            float t = (Time.unscaledTime - _born - _delay) / 0.22f;
            _cg.alpha = Mathf.Clamp01(t);
            if (t >= 1f) enabled = false;   // settled; nothing left to animate
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (!_rowEnabled) return;
            _bg.color = PhosphorUI.RowHoverBg;
            _bar.enabled = true;
            _pre.color = PhosphorUI.Phosphor;
            _label.color = PhosphorUI.RowHot;
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (!_rowEnabled) return;
            _bg.color = Color.clear;
            _bar.enabled = false;
            _pre.color = PhosphorUI.Border;
            _label.color = PhosphorUI.RowText;
        }
    }

    UnityEngine.UI.RawImage _scan;
    float _crtT = 1f;
    float _scanH = -1f;

    void Update()
    {
        if (!_visible) return;

        // CRT turn-on: same curve as the dialogue plate.
        if (_crtT < 1f)
        {
            _crtT = Mathf.Min(1f, _crtT + Time.unscaledDeltaTime / 0.28f);
            float y = _crtT < 0.55f
                ? Mathf.Lerp(0.06f, 1.02f, _crtT / 0.55f)
                : Mathf.Lerp(1.02f, 1f, (_crtT - 0.55f) / 0.45f);
            _panelRT.localScale = new Vector3(1f, y, 1f);
        }

        // Keep the scanline tiling matched to the layout-driven height.
        if (_scan != null && !Mathf.Approximately(_panelRT.rect.height, _scanH))
        {
            _scanH = _panelRT.rect.height;
            _scan.uvRect = new Rect(0, 0, 1, _scanH / 3f);
        }
        // Re-assert cursor unlock every frame while visible — NPC dialogue
        // scripts can re-lock the cursor when their typewriter completes or
        // their typewriter coroutine ticks, so a one-shot unlock in Show
        // gets clobbered. Cheap to keep enforcing.
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;

        for (int i = 0; i < _currentRows.Count && i < 9; i++)
        {
            KeyCode key = (KeyCode)((int)KeyCode.Alpha1 + i);
            if (Input.GetKeyDown(key)) HandleSelect(i);
        }
    }

    void HandleSelect(int index)
    {
        if (index < 0 || index >= _currentRows.Count) return;
        if (!_currentRows[index].enabled) return;
        var cb = _onSelect;
        Hide();
        cb?.Invoke(index);
    }
}
