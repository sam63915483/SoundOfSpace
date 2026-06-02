using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared numbered-choice panel used by every NPC after their greeting line.
/// Each row is "<n>. <label>". Player presses the digit key 1-9 OR clicks the
/// row. Closed automatically after a selection or when Hide() is called.
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

    static readonly Color PanelBg     = new Color32(10, 24, 40, 240);
    static readonly Color RowBg       = new Color32(20, 40, 60, 230);
    static readonly Color RowBgHover  = new Color32(40, 70, 100, 240);
    static readonly Color RowText     = new Color32(234, 246, 255, 255);
    static readonly Color RowTextDim  = new Color32(120, 140, 160, 200);
    static readonly Color BorderColor = new Color32(120, 200, 255, 180);

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
        _panelRT.anchorMin = new Vector2(0.5f, 0f);
        _panelRT.anchorMax = new Vector2(0.5f, 0f);
        _panelRT.pivot     = new Vector2(0.5f, 0f);
        _panelRT.anchoredPosition = new Vector2(0f, 220f);
        _panelRT.sizeDelta = new Vector2(520f, 200f);
        var bg = panel.AddComponent<Image>();
        bg.color = PanelBg;
        bg.raycastTarget = true;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(12, 12, 12, 12);
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
        // Free the cursor so the player can click rows with the mouse in
        // addition to the 1-9 hotkeys. NPCDialogue locks the cursor again
        // when its typewriter finishes (NPCDialogue.cs:331), so we have to
        // override that on Show AND keep enforcing it in Update — same
        // pattern SpaceDustSellUI uses while open.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
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
        var img = go.AddComponent<Image>();
        img.color = RowBg;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 42f;

        var btn = go.AddComponent<Button>();
        btn.interactable = row.enabled;
        var colors = btn.colors;
        colors.normalColor = RowBg;
        colors.highlightedColor = RowBgHover;
        colors.pressedColor = new Color(BorderColor.r, BorderColor.g, BorderColor.b, 0.4f);
        colors.disabledColor = new Color(RowBg.r * 0.6f, RowBg.g * 0.6f, RowBg.b * 0.6f, RowBg.a);
        btn.colors = colors;
        int captured = index;
        btn.onClick.AddListener(() => HandleSelect(captured));

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = (RectTransform)lblGO.transform;
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = new Vector2(16, 0);
        lblRT.offsetMax = new Vector2(-16, 0);
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text = $"{index + 1}. {row.label}";
        tmp.fontSize = 22f;
        tmp.color = row.enabled ? RowText : RowTextDim;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;

        _rowGOs.Add(go);
    }

    void Update()
    {
        if (!_visible) return;
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
