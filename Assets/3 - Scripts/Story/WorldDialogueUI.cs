using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-surface presenter for the preset branching-dialogue system — the
/// "future Tev view" DialoguePresenter.cs anticipated. PhoneDialoguePresenter
/// renders everything as AI chat bubbles (it ignores the speaker string), so
/// NPC-delivered conversations (Tev's letter, the vendors' offers, the ORG
/// interview, the ice-outpost boards) run through this instead: a procedural
/// bottom panel with a SPEAKER name label, zero-alloc typewriter lines
/// (DialogueTextStyling.RevealCharsTMP), and reply buttons.
///
/// Usage from any trigger/NPC script:
///     WorldDialogueUI.Begin("conv_tev_letter");
/// One conversation at a time; Begin is a no-op while one is open (check
/// WorldDialogueUI.IsOpen). Cursor is freed for the duration and restored
/// after. Ends by destroying itself and reconciling story gates.
/// </summary>
public class WorldDialogueUI : MonoBehaviour, DialoguePresenter
{
    public static bool IsOpen { get; private set; }
    static WorldDialogueUI _active;

    const int SortingOrder = 900;          // above FX overlays (800-820), below pause menu (1000)
    const float CharDelay = 0.015f;

    static readonly Color PanelColor   = new Color(0.05f, 0.07f, 0.10f, 0.92f);
    static readonly Color SpeakerColor = new Color(1.00f, 0.68f, 0.36f);       // warm copper (vendor-UI family)
    static readonly Color BodyColor    = new Color(0.85f, 0.90f, 1.00f);
    static readonly Color ButtonColor  = new Color(0.10f, 0.16f, 0.26f, 0.95f);
    static readonly Color ButtonHover  = new Color(0.16f, 0.26f, 0.42f, 1.00f);

    TextMeshProUGUI _speakerLabel;
    TextMeshProUGUI _body;
    TextMeshProUGUI _advanceHint;
    RectTransform _replyColumn;
    readonly List<GameObject> _replyButtons = new List<GameObject>();
    CursorLockMode _prevLock;
    bool _prevCursorVisible;

    // ── entry point ─────────────────────────────────────────────────────────
    public static void Begin(string conversationId, string startNodeId = null)
    {
        if (_active != null) { Debug.LogWarning("[WorldDialogue] Already open; ignoring " + conversationId); return; }
        var conv = StoryContent.GetConversation(conversationId);
        if (conv == null) { Debug.LogWarning("[WorldDialogue] Unknown conversation: " + conversationId); return; }

        var go = new GameObject("WorldDialogueUI");
        var ui = go.AddComponent<WorldDialogueUI>();
        ui.BuildUI();

        var runner = new DialogueRunner(conv, ui);
        if (!string.IsNullOrEmpty(startNodeId)) runner.StartAt(startNodeId);
        else runner.Start();
    }

    void Awake()
    {
        _active = this;
        IsOpen = true;
        _prevLock = Cursor.lockState;
        _prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void OnDestroy()
    {
        if (_active == this)
        {
            _active = null;
            IsOpen = false;
            Cursor.lockState = _prevLock;
            Cursor.visible = _prevCursorVisible;
        }
    }

    // ── DialoguePresenter ────────────────────────────────────────────────────
    public void ShowLines(string speaker, string[] lines, Action onComplete)
    {
        ClearReplies();
        _speakerLabel.text = string.IsNullOrEmpty(speaker) ? "" : speaker.ToUpperInvariant();
        StartCoroutine(LinesRoutine(lines, onComplete));
    }

    IEnumerator LinesRoutine(string[] lines, Action onComplete)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            _advanceHint.text = "";
            yield return DialogueTextStyling.RevealCharsTMP(_body, lines[i], CharDelay, AdvancePressed);
            // Swallow the frame the skip landed on so it can't also advance.
            yield return null;
            _advanceHint.text = (i < lines.Length - 1) ? "click to continue" : "";
            if (i < lines.Length - 1)
            {
                while (!AdvancePressed()) yield return null;
                yield return null;
            }
        }
        _advanceHint.text = "";
        onComplete?.Invoke();
    }

    static bool AdvancePressed() => Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E);

    public void ShowResponses(List<PlayerResponse> responses, Action<PlayerResponse> onPick)
    {
        ClearReplies();
        foreach (var r in responses)
        {
            var chosen = r; // capture
            var btn = MakeReplyButton(r.buttonText);
            btn.onClick.AddListener(() =>
            {
                ClearReplies();
                onPick?.Invoke(chosen);
            });
        }
    }

    public void EndConversation()
    {
        ClearReplies();
        if (StoryDirector.Instance != null) StoryDirector.Instance.ReconcileGatesNow();
        Destroy(gameObject);
    }

    // ── procedural UI ────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<GraphicRaycaster>();

        // Bottom panel.
        var panel = MakeRect("Panel", transform);
        panel.anchorMin = new Vector2(0.14f, 0.03f);
        panel.anchorMax = new Vector2(0.86f, 0.26f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = PanelColor;

        _speakerLabel = MakeText("Speaker", panel, 26f, SpeakerColor, TextAlignmentOptions.TopLeft);
        SetInset(_speakerLabel.rectTransform, 24f, 14f, 24f, 0f);
        _speakerLabel.rectTransform.anchorMin = new Vector2(0f, 0.72f);
        _speakerLabel.rectTransform.anchorMax = new Vector2(1f, 1f);

        _body = MakeText("Body", panel, 24f, BodyColor, TextAlignmentOptions.TopLeft);
        SetInset(_body.rectTransform, 24f, 6f, 24f, 34f);
        _body.rectTransform.anchorMin = new Vector2(0f, 0f);
        _body.rectTransform.anchorMax = new Vector2(1f, 0.72f);

        _advanceHint = MakeText("AdvanceHint", panel, 15f, new Color(0.55f, 0.62f, 0.75f), TextAlignmentOptions.BottomRight);
        SetInset(_advanceHint.rectTransform, 24f, 0f, 24f, 8f);
        _advanceHint.rectTransform.anchorMin = new Vector2(0f, 0f);
        _advanceHint.rectTransform.anchorMax = new Vector2(1f, 0.25f);

        // Reply column, above the panel's right edge.
        _replyColumn = MakeRect("Replies", transform);
        _replyColumn.anchorMin = new Vector2(0.50f, 0.27f);
        _replyColumn.anchorMax = new Vector2(0.86f, 0.62f);
        _replyColumn.offsetMin = Vector2.zero;
        _replyColumn.offsetMax = Vector2.zero;
        var layout = _replyColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerRight;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 8f;
    }

    Button MakeReplyButton(string label)
    {
        var rt = MakeRect("Reply", _replyColumn);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 44f;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = ButtonColor;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = ButtonHover;
        btn.colors = colors;

        var txt = MakeText("Label", rt, 20f, BodyColor, TextAlignmentOptions.Left);
        SetInset(txt.rectTransform, 16f, 4f, 16f, 4f);
        txt.rectTransform.anchorMin = Vector2.zero;
        txt.rectTransform.anchorMax = Vector2.one;
        txt.text = label;

        _replyButtons.Add(rt.gameObject);
        return btn;
    }

    void ClearReplies()
    {
        for (int i = 0; i < _replyButtons.Count; i++)
            if (_replyButtons[i] != null) Destroy(_replyButtons[i]);
        _replyButtons.Clear();
    }

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static TextMeshProUGUI MakeText(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var rt = MakeRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static void SetInset(RectTransform rt, float left, float top, float right, float bottom)
    {
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
