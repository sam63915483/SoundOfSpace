using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared scaffolding for apps that run INSIDE the phone tablet screen
/// (like the AI chat): an opaque panel over the home content with a top bar
/// ("‹ HOME" back button + title + right-slot text) and a two-column body —
/// a scrollable row list on the left, a detail pane on the right. Concrete
/// apps (build menu, fishingdex) fill the columns. Sized for the 4:3
/// tablet's ~546×356 screen; everything intentionally compact.
/// </summary>
public abstract class PhoneAppBase : MonoBehaviour
{
    // Palette — mirrors PlayerPhoneUI's private consts.
    protected static readonly Color ScreenBg   = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    protected static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    protected static readonly Color LabelWhite = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    protected static readonly Color LabelDim   = new Color32(0xA8, 0xD2, 0xEB, 0xB3);
    protected static readonly Color TileBg     = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    protected static readonly Color RowSelect  = new Color32(0x5C, 0xC8, 0xFF, 0x2E);
    protected static readonly Color WarnRed    = new Color32(0xFF, 0x6B, 0x6B, 0xFF);

    protected RectTransform Root;        // full app panel (child of the host)
    protected RectTransform ListContent; // scroll content (VLG) for rows
    protected RectTransform DetailPane;  // right column
    protected TMP_Text TopRightText;     // small status slot in the top bar
    bool _built;

    protected abstract string Title { get; }

    /// Called by PlayerPhoneUI when the app tile is tapped.
    public void OpenApp(RectTransform host)
    {
        if (!_built) { BuildChrome(host); _built = true; }
        Root.gameObject.SetActive(true);
        OnOpened();
        // Pad: focus the first list row (fallback: the ‹ HOME button) so stick
        // navigation works immediately inside the app — the phone's movement-
        // close logic reads an UNFOCUSED left stick as walking and would
        // otherwise close the phone on the first nudge.
        if (TutorialGate.ControllerEnabled)
        {
            Selectable first = ListContent != null ? ListContent.GetComponentInChildren<Selectable>() : null;
            if (first == null && Root != null) first = Root.GetComponentInChildren<Selectable>();
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && first != null) es.SetSelectedGameObject(first.gameObject);
        }
    }

    /// Called when backing out to the home screen (or the phone closes).
    public void CloseApp()
    {
        OnClosed();
        if (Root != null) Root.gameObject.SetActive(false);
    }

    protected abstract void OnOpened();
    protected virtual void OnClosed() { }
    protected abstract void BuildBody();

    void BuildChrome(RectTransform host)
    {
        Root = NewUI(Title + "App", host);
        Root.anchorMin = Vector2.zero; Root.anchorMax = Vector2.one;
        Root.offsetMin = Vector2.zero; Root.offsetMax = Vector2.zero;
        var bg = Root.gameObject.AddComponent<Image>();
        bg.color = ScreenBg;
        bg.raycastTarget = true;   // swallow misses inside the app

        // Top bar.
        var bar = NewUI("TopBar", Root);
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 26f);
        bar.anchoredPosition = new Vector2(0f, -2f);

        var back = MakeButton(bar, "‹ HOME", new Vector2(64f, 20f), () =>
        {
            if (PlayerPhoneUI.Instance != null) PlayerPhoneUI.Instance.ClosePhoneApp();
        });
        var backRT = (RectTransform)back.transform;
        backRT.anchorMin = backRT.anchorMax = new Vector2(0f, 0.5f);
        backRT.pivot = new Vector2(0f, 0.5f);
        backRT.anchoredPosition = new Vector2(8f, 0f);

        var title = MakeText(bar, Title, 12f, AccentCyan, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 4f;
        var titleRT = title.rectTransform;
        titleRT.anchorMin = Vector2.zero; titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero; titleRT.offsetMax = Vector2.zero;

        TopRightText = MakeText(bar, "", 10f, LabelDim, TextAlignmentOptions.MidlineRight);
        var trRT = TopRightText.rectTransform;
        trRT.anchorMin = new Vector2(0.6f, 0f); trRT.anchorMax = new Vector2(1f, 1f);
        trRT.offsetMin = Vector2.zero; trRT.offsetMax = new Vector2(-10f, 0f);

        // Left column: scroll list.
        var listFrame = NewUI("ListFrame", Root);
        listFrame.anchorMin = new Vector2(0f, 0f); listFrame.anchorMax = new Vector2(0f, 1f);
        listFrame.pivot = new Vector2(0f, 0.5f);
        listFrame.offsetMin = new Vector2(8f, 8f);
        listFrame.offsetMax = new Vector2(8f + 200f, -32f);
        listFrame.sizeDelta = new Vector2(200f, listFrame.sizeDelta.y);
        var frameImg = listFrame.gameObject.AddComponent<Image>();
        frameImg.color = new Color(0f, 0f, 0f, 0.28f);
        frameImg.raycastTarget = true;

        var scroll = listFrame.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;

        var viewport = NewUI("Viewport", listFrame);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(2f, 2f); viewport.offsetMax = new Vector2(-2f, -2f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0.01f);   // raycast surface for drag-scroll
        scroll.viewport = viewport;

        ListContent = NewUI("Content", viewport);
        ListContent.anchorMin = new Vector2(0f, 1f); ListContent.anchorMax = new Vector2(1f, 1f);
        ListContent.pivot = new Vector2(0.5f, 1f);
        ListContent.anchoredPosition = Vector2.zero;
        // A fresh RectTransform defaults to sizeDelta (100,100) — with
        // stretch-X anchors that makes the content 100 units WIDER than the
        // viewport (rows poked out of the mask). Zero it; the fitter drives Y.
        ListContent.sizeDelta = Vector2.zero;
        var vlg = ListContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        var fitter = ListContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = ListContent;

        // Right column: detail pane.
        DetailPane = NewUI("Detail", Root);
        DetailPane.anchorMin = new Vector2(0f, 0f); DetailPane.anchorMax = new Vector2(1f, 1f);
        DetailPane.offsetMin = new Vector2(8f + 200f + 8f, 8f);
        DetailPane.offsetMax = new Vector2(-8f, -32f);

        BuildBody();
        Root.gameObject.SetActive(false);
    }

    // ── Row + widget helpers ─────────────────────────────────────────

    protected Button AddRow(string leftText, string rightText, Color rightColor, System.Action onClick,
                            out TMP_Text left, out TMP_Text right)
    {
        var row = NewUI("Row", ListContent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        var bg = row.gameObject.AddComponent<Image>();
        bg.color = new Color32(0, 0, 0, 0);
        var btn = row.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick());

        left = MakeText(row, leftText, 10.5f, LabelWhite, TextAlignmentOptions.MidlineLeft);
        var lRT = left.rectTransform;
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(8f, 0f); lRT.offsetMax = new Vector2(-58f, 0f);
        // No Ellipsis here: TMP built inside a layout group starts at zero
        // width, and Ellipsis generated at zero width never recovers after
        // the layout pass — the text just stays blank. Plain overflow is
        // fine; the row mask clips anything too long.
        left.enableWordWrapping = false;

        right = MakeText(row, rightText, 9.5f, rightColor, TextAlignmentOptions.MidlineRight);
        var rRT = right.rectTransform;
        rRT.anchorMin = new Vector2(1f, 0f); rRT.anchorMax = new Vector2(1f, 1f);
        rRT.pivot = new Vector2(1f, 0.5f);
        rRT.offsetMin = new Vector2(-56f, 0f); rRT.offsetMax = new Vector2(-6f, 0f);
        right.enableWordWrapping = false;
        return btn;
    }

    protected void SetRowSelected(Button row, bool selected)
    {
        if (row == null) return;
        var bg = row.GetComponent<Image>();
        if (bg != null) bg.color = selected ? RowSelect : (Color)new Color32(0, 0, 0, 0);
    }

    protected void ClearRows()
    {
        if (ListContent == null) return;
        for (int i = ListContent.childCount - 1; i >= 0; i--)
            Destroy(ListContent.GetChild(i).gameObject);
    }

    protected static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    protected static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var rt = NewUI("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    protected Button MakeButton(Transform parent, string label, Vector2 size, System.Action onClick)
    {
        var rt = NewUI("Btn_" + label, parent);
        rt.sizeDelta = size;
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.10f);
        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.6f);
        outline.effectDistance = new Vector2(1f, -1f);
        var txt = MakeText(rt, label, 10f, AccentCyan, TextAlignmentOptions.Center);
        txt.fontStyle = FontStyles.Bold;
        var txtRT = txt.rectTransform;
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick());
        return btn;
    }

    /// Small "LABEL   value" spec line for detail panes.
    protected TMP_Text AddSpecLine(Transform parent, string label, float y)
    {
        var row = NewUI("Spec_" + label, parent);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, 16f);
        row.anchoredPosition = new Vector2(0f, y);
        var lbl = MakeText(row, label, 8.5f, LabelDim, TextAlignmentOptions.MidlineLeft);
        lbl.characterSpacing = 2f;
        var lblRT = lbl.rectTransform;
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = new Vector2(0.4f, 1f);
        lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
        var val = MakeText(row, "—", 10f, LabelWhite, TextAlignmentOptions.MidlineRight);
        var valRT = val.rectTransform;
        valRT.anchorMin = new Vector2(0.4f, 0f); valRT.anchorMax = Vector2.one;
        valRT.offsetMin = Vector2.zero; valRT.offsetMax = Vector2.zero;
        val.enableWordWrapping = false;
        return val;
    }
}
