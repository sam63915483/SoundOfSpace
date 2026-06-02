using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum SaveLoadMode { Load, Save }

// Builds a procedural save / load panel as a child of the given parent transform.
// Returns the root GameObject so the caller can destroy it when closing.
public static class SaveLoadUI
{
    static readonly Color32 ButtonNormal  = new Color32(0x10, 0x08, 0x2E, 0xE0);
    static readonly Color32 ButtonHover   = new Color32(0x7A, 0x42, 0xC8, 0xFF);
    static readonly Color32 ButtonPressed = new Color32(0xA0, 0x66, 0xE6, 0xFF);
    static readonly Color32 DeleteNormal  = new Color32(0x60, 0x18, 0x18, 0xE0);
    static readonly Color32 DeleteHover   = new Color32(0xC8, 0x42, 0x42, 0xFF);
    static readonly Color32 PanelBg       = new Color32(0x07, 0x05, 0x1C, 0xF8);

    public class Panel
    {
        public GameObject root;
        public Action onClose;
    }

    public static Panel Build(
        Transform parent,
        SaveLoadMode mode,
        Action onSelect,                         // called after user picks/creates and the action completes
        Action<string> onPickSlot,               // (saveName) — load OR overwrite, depending on mode
        Action<string> onCreateOrNew,            // (saveName) — Save mode passes user-typed name; Load mode passes null
        Action onClose)
    {
        var panel = new Panel { onClose = onClose };

        // Build the panel as a self-contained Canvas so it renders correctly regardless
        // of what the caller passed in as `parent` (could be a non-canvas section GO).
        var rootRT = NewUI("SaveLoadPanel", parent);
        Stretch(rootRT, 0, 0, 0, 0);
        var ownCanvas = rootRT.gameObject.AddComponent<Canvas>();
        ownCanvas.overrideSorting = true;
        ownCanvas.sortingOrder = UILayer.SaveDialog;
        var ownScaler = rootRT.gameObject.AddComponent<CanvasScaler>();
        ownScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        ownScaler.referenceResolution = new Vector2(1920, 1080);
        ownScaler.matchWidthOrHeight = 0.5f;
        rootRT.gameObject.AddComponent<GraphicRaycaster>();

        var dim = rootRT.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        panel.root = rootRT.gameObject;
        var parentName = parent != null ? parent.name : "null";
        Debug.Log($"[SaveLoadUI] Built {mode} panel under '{parentName}'.");

        // Card
        var cardRT = NewUI("Card", rootRT);
        cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.sizeDelta = new Vector2(820f, 620f);
        cardRT.anchoredPosition = Vector2.zero;
        var border = cardRT.gameObject.AddComponent<Image>();
        border.sprite = GalaxyHudKit.RoundedSprite();
        border.type = Image.Type.Sliced;
        border.color = GalaxyHudKit.BorderCool;

        var bgRT = NewUI("BG", cardRT);
        Stretch(bgRT, 3, 3, -3, -3);
        var bgImg = bgRT.gameObject.AddComponent<Image>();
        bgImg.sprite = GalaxyHudKit.NebulaSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = Color.white;

        // Title
        var titleRT = NewUI("Title", bgRT);
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -28f);
        titleRT.sizeDelta = new Vector2(0f, 64f);
        var title = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(title);
        title.text = mode == SaveLoadMode.Load ? "START GAME" : "SAVE GAME";
        title.fontSize = 38f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.characterSpacing = 10f;
        title.enableVertexGradient = true;
        title.colorGradient = new VertexGradient(GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot, GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        title.raycastTarget = false;

        // Top accent
        var accent = NewUI("Accent", bgRT);
        accent.anchorMin = new Vector2(0, 1);
        accent.anchorMax = new Vector2(1, 1);
        accent.pivot = new Vector2(0.5f, 1f);
        accent.anchoredPosition = new Vector2(0f, -2f);
        accent.sizeDelta = new Vector2(-60f, 3f);
        var accImg = accent.gameObject.AddComponent<Image>();
        accImg.sprite = GalaxyHudKit.AccentSprite();
        accImg.raycastTarget = false;

        // Subtitle
        var subRT = NewUI("Subtitle", bgRT);
        subRT.anchorMin = new Vector2(0, 1);
        subRT.anchorMax = new Vector2(1, 1);
        subRT.pivot = new Vector2(0.5f, 1f);
        subRT.anchoredPosition = new Vector2(0f, -100f);
        subRT.sizeDelta = new Vector2(0f, 28f);
        var sub = subRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(sub);
        sub.text = mode == SaveLoadMode.Load
            ? "Choose a save to load, or start a new game"
            : "Click a save to overwrite, or create a new one";
        sub.fontSize = 20f;
        sub.fontStyle = FontStyles.Italic;
        sub.alignment = TextAlignmentOptions.Center;
        sub.color = new Color(1f, 1f, 1f, 0.7f);
        sub.raycastTarget = false;

        // Scroll list
        var scrollRT = NewUI("Scroll", bgRT);
        scrollRT.anchorMin = new Vector2(0, 0);
        scrollRT.anchorMax = new Vector2(1, 1);
        scrollRT.offsetMin = new Vector2(40f, 130f);
        scrollRT.offsetMax = new Vector2(-40f, -150f);
        var scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var scrollBg = scrollRT.gameObject.AddComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0.25f);
        scrollBg.raycastTarget = true;

        var viewportRT = NewUI("Viewport", scrollRT);
        Stretch(viewportRT, 4, 4, -4, -4);
        viewportRT.gameObject.AddComponent<RectMask2D>();
        scroll.viewport = viewportRT;

        var contentRT = NewUI("Content", viewportRT);
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0f, 16f);  // height grows as rows are added
        scroll.content = contentRT;

        const float rowHeight = 64f;
        const float rowSpacing = 10f;
        const float topPadding = 8f;

        // Footer is built FIRST so the navigation-wiring inside rebuildList
        // has the primary/secondary button references to chain into.
        var footerRT = NewUI("Footer", bgRT);
        footerRT.anchorMin = new Vector2(0, 0);
        footerRT.anchorMax = new Vector2(1, 0);
        footerRT.pivot = new Vector2(0.5f, 0f);
        footerRT.anchoredPosition = new Vector2(0f, 24f);
        footerRT.sizeDelta = new Vector2(0f, 90f);
        var hlg = footerRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.spacing = 24f;
        hlg.padding = new RectOffset(40, 40, 0, 0);

        var primaryLabel = mode == SaveLoadMode.Load ? "NEW GAME" : "CREATE NEW SAVE";
        Button primaryButton = BuildBigButton(footerRT, primaryLabel, () =>
        {
            if (mode == SaveLoadMode.Save)
            {
                ShowNamePrompt(rootRT, name =>
                {
                    onCreateOrNew?.Invoke(name);
                    onSelect?.Invoke();
                });
            }
            else
            {
                onCreateOrNew?.Invoke(null);
                onSelect?.Invoke();
            }
        }, ButtonNormal, ButtonHover, ButtonPressed, 280f);

        Button secondaryButton = BuildBigButton(footerRT, mode == SaveLoadMode.Load ? "BACK" : "CLOSE", () =>
        {
            onClose?.Invoke();
        }, new Color32(0x20, 0x12, 0x40, 0xE0), ButtonHover, ButtonPressed, 200f);

        Action rebuildList = null;
        rebuildList = () =>
        {
            // Clear existing rows. Use DestroyImmediate so contentRT.childCount drops to 0
            // immediately rather than next frame — otherwise re-adds stack on top of stale rows.
            for (int i = contentRT.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(contentRT.GetChild(i).gameObject);

            var saves = SaveSystem.ListSaves();
            Debug.Log($"[SaveLoadUI] Rebuilding row list: {saves.Count} entry(ies).");

            if (saves.Count == 0)
            {
                var emptyRT = NewUI("Empty", contentRT);
                emptyRT.anchorMin = new Vector2(0, 1);
                emptyRT.anchorMax = new Vector2(1, 1);
                emptyRT.pivot = new Vector2(0.5f, 1f);
                emptyRT.anchoredPosition = new Vector2(0f, -topPadding);
                emptyRT.sizeDelta = new Vector2(-16f, 60f);
                var t = emptyRT.gameObject.AddComponent<TextMeshProUGUI>();
                ApplyFont(t);
                t.text = "<i>No saves yet.</i>";
                t.fontSize = 22f;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color(1f, 1f, 1f, 0.55f);
                t.raycastTarget = false;
                contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x, topPadding + 60f + topPadding);
            }
            else
            {
                for (int i = 0; i < saves.Count; i++)
                {
                    float y = -topPadding - i * (rowHeight + rowSpacing);
                    BuildRow(contentRT, saves[i], mode, onPickSlot,
                             () => { onSelect?.Invoke(); },
                             () => rebuildList?.Invoke(),
                             yOffset: y, height: rowHeight);
                }
                float total = topPadding + saves.Count * rowHeight + (saves.Count - 1) * rowSpacing + topPadding;
                contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x, total);
            }

            // Wire explicit controller-navigation across the row chain and out
            // to the footer buttons. Auto-nav doesn't reliably bridge from the
            // ScrollRect Content into the Footer (different parents) — explicit
            // wiring guarantees Down from the last row reaches NEW GAME / SAVE.
            WireSaveLoadNavigation(contentRT, primaryButton, secondaryButton);
        };
        rebuildList();

        return panel;
    }

    // Wires Navigation.Mode = Explicit on each row's Pick + Delete buttons and
    // the footer's primary/secondary buttons so D-pad / left-stick nav reaches
    // every interactable in a predictable order:
    //
    //   row N Pick  ←up/down→  row N±1 Pick     row N Pick  →right→  row N Delete
    //   row N Delete ←up/down→ row N±1 Delete   row N Delete ←left→  row N Pick
    //
    //   last row Pick   ←down→  primary footer button (NEW GAME / CREATE NEW SAVE)
    //   last row Delete ←down→  secondary footer button (BACK / CLOSE)
    //
    //   primary footer  ←up→ last row Pick    primary footer  →right→ secondary footer
    //   secondary footer ←up→ last row Delete  secondary footer ←left→ primary footer
    static void WireSaveLoadNavigation(Transform contentRT, Button primary, Button secondary)
    {
        if (primary == null) return;

        var pickButtons = new List<Button>();
        var deleteButtons = new List<Button>();
        for (int i = 0; i < contentRT.childCount; i++)
        {
            var child = contentRT.GetChild(i);
            if (!child.name.StartsWith("Row_")) continue;
            var pickT = child.Find("Pick");
            var delT  = child.Find("Delete");
            var pick = pickT != null ? pickT.GetComponent<Button>() : null;
            var del  = delT  != null ? delT .GetComponent<Button>() : null;
            if (pick != null && del != null)
            {
                pickButtons.Add(pick);
                deleteButtons.Add(del);
            }
        }

        for (int i = 0; i < pickButtons.Count; i++)
        {
            var pNav = new Navigation { mode = Navigation.Mode.Explicit };
            if (i > 0) pNav.selectOnUp = pickButtons[i - 1];
            if (i < pickButtons.Count - 1) pNav.selectOnDown = pickButtons[i + 1];
            else pNav.selectOnDown = primary;
            pNav.selectOnRight = deleteButtons[i];
            pickButtons[i].navigation = pNav;

            var dNav = new Navigation { mode = Navigation.Mode.Explicit };
            if (i > 0) dNav.selectOnUp = deleteButtons[i - 1];
            if (i < deleteButtons.Count - 1) dNav.selectOnDown = deleteButtons[i + 1];
            else dNav.selectOnDown = secondary != null ? secondary : primary;
            dNav.selectOnLeft = pickButtons[i];
            deleteButtons[i].navigation = dNav;
        }

        var primNav = new Navigation { mode = Navigation.Mode.Explicit };
        if (pickButtons.Count > 0) primNav.selectOnUp = pickButtons[pickButtons.Count - 1];
        if (secondary != null) primNav.selectOnRight = secondary;
        primary.navigation = primNav;

        if (secondary != null)
        {
            var sNav = new Navigation { mode = Navigation.Mode.Explicit };
            if (deleteButtons.Count > 0) sNav.selectOnUp = deleteButtons[deleteButtons.Count - 1];
            else if (pickButtons.Count > 0) sNav.selectOnUp = pickButtons[pickButtons.Count - 1];
            sNav.selectOnLeft = primary;
            secondary.navigation = sNav;
        }
    }

    static void BuildRow(Transform parent, SaveSlotInfo slot, SaveLoadMode mode,
                         Action<string> onPickSlot, Action onAfter, Action onListChanged,
                         float yOffset, float height)
    {
        Debug.Log($"[SaveLoadUI] BuildRow: '{slot.fileName}' at y={yOffset}");
        var rowRT = NewUI("Row_" + slot.fileName, parent);
        rowRT.anchorMin = new Vector2(0, 1);
        rowRT.anchorMax = new Vector2(1, 1);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, yOffset);
        rowRT.sizeDelta = new Vector2(-16f, height);
        var rowImg = rowRT.gameObject.AddComponent<Image>();
        rowImg.sprite = GalaxyHudKit.SlotSprite();
        rowImg.type = Image.Type.Sliced;
        rowImg.color = new Color(0.10f, 0.06f, 0.22f, 0.9f);

        // Main click button (label fills most of the width)
        var pickRT = NewUI("Pick", rowRT);
        pickRT.anchorMin = new Vector2(0, 0);
        pickRT.anchorMax = new Vector2(1, 1);
        pickRT.offsetMin = new Vector2(8, 6);
        pickRT.offsetMax = new Vector2(-80, -6);
        var pickImg = pickRT.gameObject.AddComponent<Image>();
        pickImg.sprite = GalaxyHudKit.RoundedSprite();
        pickImg.type = Image.Type.Sliced;
        pickImg.color = ButtonNormal;
        var pickBtn = pickRT.gameObject.AddComponent<Button>();
        pickBtn.targetGraphic = pickImg;
        var colors = pickBtn.colors;
        colors.normalColor = ButtonNormal;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = ButtonPressed;
        colors.selectedColor = ButtonHover;
        pickBtn.colors = colors;

        var labelRT = NewUI("Label", pickRT);
        Stretch(labelRT, 16, 0, -16, 0);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(label);
        label.text = slot.displayName;
        label.fontSize = 20f;
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(1f, 1f, 1f, 0.95f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;

        var capturedSlot = slot.fileName;
        pickBtn.onClick.AddListener(() => { onPickSlot?.Invoke(capturedSlot); onAfter?.Invoke(); });

        // Delete button
        var delRT = NewUI("Delete", rowRT);
        delRT.anchorMin = new Vector2(1, 0);
        delRT.anchorMax = new Vector2(1, 1);
        delRT.pivot = new Vector2(1, 0.5f);
        delRT.offsetMin = new Vector2(-72, 6);
        delRT.offsetMax = new Vector2(-8, -6);
        var delImg = delRT.gameObject.AddComponent<Image>();
        delImg.sprite = GalaxyHudKit.RoundedSprite();
        delImg.type = Image.Type.Sliced;
        delImg.color = DeleteNormal;
        var delBtn = delRT.gameObject.AddComponent<Button>();
        delBtn.targetGraphic = delImg;
        var dColors = delBtn.colors;
        dColors.normalColor = DeleteNormal;
        dColors.highlightedColor = DeleteHover;
        dColors.pressedColor = new Color(0.5f, 0.1f, 0.1f, 1f);
        delBtn.colors = dColors;

        var delLabelRT = NewUI("X", delRT);
        Stretch(delLabelRT, 0, 0, 0, 0);
        var delLabel = delLabelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(delLabel);
        delLabel.text = "DELETE";
        delLabel.fontSize = 16f;
        delLabel.fontStyle = FontStyles.Bold;
        delLabel.color = Color.white;
        delLabel.alignment = TextAlignmentOptions.Center;
        delLabel.characterSpacing = 4f;
        delLabel.raycastTarget = false;

        delBtn.onClick.AddListener(() =>
        {
            SaveSystem.DeleteSave(capturedSlot);
            onListChanged?.Invoke();
        });
    }

    static void ShowNamePrompt(RectTransform rootRT, Action<string> onSubmit)
    {
        var overlayRT = NewUI("NamePromptOverlay", rootRT);
        Stretch(overlayRT, 0, 0, 0, 0);
        var overlayBg = overlayRT.gameObject.AddComponent<Image>();
        overlayBg.color = new Color(0f, 0f, 0f, 0.7f);

        var cardRT = NewUI("Card", overlayRT);
        cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.sizeDelta = new Vector2(580f, 260f);
        cardRT.anchoredPosition = Vector2.zero;
        var border = cardRT.gameObject.AddComponent<Image>();
        border.sprite = GalaxyHudKit.RoundedSprite();
        border.type = Image.Type.Sliced;
        border.color = GalaxyHudKit.BorderCool;

        var bgRT = NewUI("BG", cardRT);
        Stretch(bgRT, 3, 3, -3, -3);
        var bgImg = bgRT.gameObject.AddComponent<Image>();
        bgImg.sprite = GalaxyHudKit.NebulaSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = Color.white;

        var titleRT = NewUI("Title", bgRT);
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -22f);
        titleRT.sizeDelta = new Vector2(0f, 48f);
        var title = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(title);
        title.text = "NAME YOUR SAVE";
        title.fontSize = 28f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        title.colorGradient = new VertexGradient(GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot, GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        title.raycastTarget = false;

        var inputRT = NewUI("Input", bgRT);
        inputRT.anchorMin = new Vector2(0.5f, 0.5f);
        inputRT.anchorMax = new Vector2(0.5f, 0.5f);
        inputRT.pivot = new Vector2(0.5f, 0.5f);
        inputRT.anchoredPosition = new Vector2(0f, 10f);
        inputRT.sizeDelta = new Vector2(480f, 56f);
        var inputBg = inputRT.gameObject.AddComponent<Image>();
        inputBg.sprite = GalaxyHudKit.RoundedSprite();
        inputBg.type = Image.Type.Sliced;
        inputBg.color = new Color(0.05f, 0.03f, 0.12f, 0.95f);
        var input = inputRT.gameObject.AddComponent<TMP_InputField>();

        var textAreaRT = NewUI("TextArea", inputRT);
        Stretch(textAreaRT, 14, 6, -14, -6);
        textAreaRT.gameObject.AddComponent<RectMask2D>();

        var placeholderRT = NewUI("Placeholder", textAreaRT);
        Stretch(placeholderRT, 0, 0, 0, 0);
        var placeholder = placeholderRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(placeholder);
        placeholder.text = "Enter save name…";
        placeholder.fontSize = 22f;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.raycastTarget = false;

        var textRT = NewUI("Text", textAreaRT);
        Stretch(textRT, 0, 0, 0, 0);
        var text = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(text);
        text.fontSize = 22f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;

        input.textViewport = textAreaRT;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.characterLimit = 32;
        input.lineType = TMP_InputField.LineType.SingleLine;

        var footerRT = NewUI("Footer", bgRT);
        footerRT.anchorMin = new Vector2(0, 0);
        footerRT.anchorMax = new Vector2(1, 0);
        footerRT.pivot = new Vector2(0.5f, 0f);
        footerRT.anchoredPosition = new Vector2(0f, 18f);
        footerRT.sizeDelta = new Vector2(0f, 70f);
        var hlg = footerRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.spacing = 16f;
        hlg.padding = new RectOffset(40, 40, 0, 0);

        Action submit = () =>
        {
            var name = SanitizeName(input.text);
            UnityEngine.Object.Destroy(overlayRT.gameObject);
            onSubmit?.Invoke(name);
        };

        BuildBigButton(footerRT, "SAVE", () => submit(), ButtonNormal, ButtonHover, ButtonPressed, 200f);
        BuildBigButton(footerRT, "CANCEL", () =>
        {
            UnityEngine.Object.Destroy(overlayRT.gameObject);
        }, new Color32(0x20, 0x12, 0x40, 0xE0), ButtonHover, ButtonPressed, 160f);

        input.onSubmit.AddListener(_ => submit());
        input.ActivateInputField();
    }

    static string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SaveSystem.GenerateName();
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in raw.Trim())
        {
            if (System.Array.IndexOf(invalid, c) < 0) sb.Append(c);
        }
        var s = sb.ToString();
        return string.IsNullOrEmpty(s) ? SaveSystem.GenerateName() : s;
    }

    static Button BuildBigButton(Transform parent, string label, Action onClick,
                               Color32 normal, Color32 hover, Color32 pressed, float width)
    {
        var rt = NewUI("Btn_" + label, parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 60f;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = GalaxyHudKit.RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = normal;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = normal;
        colors.highlightedColor = hover;
        colors.pressedColor = pressed;
        colors.selectedColor = hover;
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick?.Invoke());

        var labelRT = NewUI("Label", rt);
        Stretch(labelRT, 0, 0, 0, 0);
        var t = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(t);
        t.text = label;
        t.fontSize = 22f;
        t.fontStyle = FontStyles.Bold;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        t.characterSpacing = 6f;
        t.raycastTarget = false;
        return btn;
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

    static void ApplyFont(TextMeshProUGUI t)
    {
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) t.font = font;
    }
}
