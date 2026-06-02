using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Restyles the in-game pause menu (the Settings panel that opens on Esc) to
// match the rest of the galaxy UI. Builds a centred card with the same layered
// decoration as TutorialUI / MainMenu (outer glow, pulsing cyan↔magenta border,
// nebula background with scattered twinkling stars, top accent strip), plus a
// big "PAUSED" title, then re-parents the existing controls (Back button,
// Main Menu button, mouse-sensitivity input field, mouse-smoothing slider)
// into the card with a clean vertical layout and galaxy-themed visuals.
//
// SettingsMenu.cs's component references (InputField, Slider, Button) are kept
// intact — re-parenting GameObjects in Unity does not break component refs.
public class GalaxyPauseMenuStyler : MonoBehaviour
{
    Image borderImage;
    GameObject card;
    RectTransform cardRT;
    RectTransform canvasRT;
    bool styled;

    void Awake()
    {
        if (styled) return;
        styled = true;
        ElevateSortingOrder();
        Build();
        StartCoroutine(BorderPulse());
    }

    // Auto-shrink the card on screens where the canvas's *scaled* height is
    // smaller than the card. The Overlay Canvas uses matchWidthOrHeight=0 (match
    // width) so on ultrawide displays the canvas height in scaled units drops
    // well below 1080 — without this, the card overflows top + bottom and the
    // user can't reach the buttons. Recomputes each frame to also handle window
    // resizes during play.
    void Update()
    {
        if (cardRT == null || canvasRT == null) return;
        float canvasH = canvasRT.rect.height;
        float cardH = cardRT.sizeDelta.y;
        if (cardH <= 0f) return;

        // 40 px of breathing room top + bottom inside the canvas.
        float availableH = canvasH - 40f;
        float scale = (availableH > 0f && cardH > availableH) ? availableH / cardH : 1f;

        Vector3 desired = Vector3.one * scale;
        Vector3 current = cardRT.localScale;
        if ((current - desired).sqrMagnitude > 0.0000001f)
            cardRT.localScale = desired;
    }

    // The TutorialUI canvas runs at sortingOrder 500 and the wallet HUD at 20,
    // so by default they render *over* the pause menu's dim. Give the Settings
    // GameObject its own Canvas with overrideSorting at a higher order so the
    // entire pause menu (dim + card) draws above every other UI canvas.
    void ElevateSortingOrder()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;

        if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
    }

    void Build()
    {
        // 1 — Backdrop: existing Image becomes a deep-space dim with a hint of nebula tint.
        var backdrop = transform.Find("Image");
        if (backdrop != null)
        {
            var img = backdrop.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color32(0x05, 0x02, 0x14, 0xE0); // very dark indigo, ~88% alpha
                img.raycastTarget = true;
            }
        }

        // 2 — Card container, centred in screen.
        var cardRT = NewUI("_GalaxyPauseCard", transform);
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        cardRT.sizeDelta = new Vector2(720f, 1072f);
        // Cache for the runtime fit-scale check (Update()).
        this.cardRT = cardRT;
        this.canvasRT = transform as RectTransform;
        card = cardRT.gameObject;

        BuildCardDecoration(cardRT);

        // 3 — Title + subtitle inside the card.
        var titleRT = NewUI("Title", cardRT);
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -32f);
        titleRT.sizeDelta = new Vector2(640f, 86f);
        var title = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(title);
        title.text = "PAUSED";
        title.fontSize = 72f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.characterSpacing = 16f;
        title.enableVertexGradient = true;
        title.colorGradient = new VertexGradient(
            GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot,
            GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        title.raycastTarget = false;
        var titleGlow = title.gameObject.AddComponent<Shadow>();
        titleGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.6f);
        titleGlow.effectDistance = new Vector2(0f, -2f);

        var subRT = NewUI("Subtitle", cardRT);
        subRT.anchorMin = new Vector2(0.5f, 1f);
        subRT.anchorMax = new Vector2(0.5f, 1f);
        subRT.pivot = new Vector2(0.5f, 1f);
        subRT.anchoredPosition = new Vector2(0f, -116f);
        subRT.sizeDelta = new Vector2(500f, 30f);
        var sub = subRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(sub);
        sub.text = "SETTINGS";
        sub.fontSize = 20f;
        sub.alignment = TextAlignmentOptions.Center;
        sub.color = new Color32(0xA8, 0xE6, 0xFF, 0xCC);
        sub.characterSpacing = 12f;
        sub.fontStyle = FontStyles.Italic;
        sub.raycastTarget = false;

        // Divider line under subtitle.
        var divider = NewUI("Divider", cardRT);
        divider.anchorMin = new Vector2(0.5f, 1f);
        divider.anchorMax = new Vector2(0.5f, 1f);
        divider.pivot = new Vector2(0.5f, 1f);
        divider.anchoredPosition = new Vector2(0f, -158f);
        divider.sizeDelta = new Vector2(420f, 2f);
        var divImg = divider.gameObject.AddComponent<Image>();
        divImg.sprite = GalaxyHudKit.AccentSprite();
        divImg.color = new Color(1f, 1f, 1f, 0.55f);
        divImg.raycastTarget = false;

        // 4 — Re-parent the existing controls into a vertical layout inside the card.
        // Top-anchored so rows start right below the divider — previously the
        // content was centred at -42 with a fixed 400 sizeDelta, leaving ~240
        // px of dead air between the divider and the first row.
        var contentRT = NewUI("Content", cardRT);
        contentRT.anchorMin = new Vector2(0.5f, 1f);
        contentRT.anchorMax = new Vector2(0.5f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = new Vector2(0f, -180f);
        contentRT.sizeDelta = new Vector2(560f, 0f);

        BuildMouseSensitivityRow(contentRT, 0f);
        BuildSettingRow(contentRT, "Mouse Smoothing",   1f, useSlider: true);
        BuildSettingRow(contentRT, "Master Volume",     2f, useSlider: true);
        BuildSettingRow(contentRT, "Max Trees",         3f, useSlider: true);
        BuildSettingRow(contentRT, "Max Alien NPCs",    4f, useSlider: true);
        BuildSettingRow(contentRT, "Max Mushrooms",     5f, useSlider: true);
        BuildSettingRow(contentRT, "Max Audience",      6f, useSlider: true);
        BuildViewDistanceRow(contentRT, 7f);
        BuildAutosaveRow(contentRT, 8f);
        BuildStickSensitivityRow(contentRT, 9f);
        BuildShipStickSensitivityRow(contentRT, 10f);

        // 5 — Buttons row at the bottom of the card.
        BuildButtonsRow(cardRT);
    }

    void BuildMouseSensitivityRow(RectTransform parent, float yIndex)
    {
        // Hide the scene-baked Mouse Sensitivity GameObject (which contains a
        // TMP_InputField). We replace it visually with a slider so controller
        // users can adjust without typing. The InputField stays alive but
        // hidden — SettingsMenu still references it for OpenMenu/CloseMenu
        // persistence, and we sync its text on every slider change so that
        // CloseMenu's legacy text→PlayerPrefs path keeps working unchanged.
        var existing = transform.Find("Mouse Sensitivity");
        if (existing != null) existing.gameObject.SetActive(false);

        var menu = GetComponentInParent<SettingsMenu>() ?? FindObjectOfType<SettingsMenu>();
        if (menu == null || menu.inputSettings == null) return;

        var rowRT = NewUI("MouseSensitivity_Row", parent);
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -yIndex * 76f);
        rowRT.sizeDelta = new Vector2(0f, 64f);

        // Left label.
        var labelRT = NewUI("Label", rowRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0.45f, 1f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(0f, 0f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(label);
        label.text = "MOUSE SENSITIVITY";
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        label.characterSpacing = 4f;
        label.raycastTarget = false;
        var labelGlow = label.gameObject.AddComponent<Shadow>();
        labelGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.45f);
        labelGlow.effectDistance = new Vector2(0f, -1.5f);

        // Right side: slider on the left ~70%, value text on the right ~30%.
        var ctrlRT = NewUI("Control", rowRT);
        ctrlRT.anchorMin = new Vector2(0.45f, 0f);
        ctrlRT.anchorMax = new Vector2(1f, 1f);
        ctrlRT.pivot = new Vector2(0.5f, 0.5f);
        ctrlRT.offsetMin = new Vector2(8f, 0f);
        ctrlRT.offsetMax = new Vector2(0f, 0f);

        var sliderRT = NewUI("Slider", ctrlRT);
        sliderRT.anchorMin = new Vector2(0f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.7f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        sliderRT.offsetMin = new Vector2(0f, -14f);
        sliderRT.offsetMax = new Vector2(-8f, 14f);

        var slider = sliderRT.gameObject.AddComponent<Slider>();
        slider.minValue = 10f;
        slider.maxValue = 300f;
        slider.wholeNumbers = true;
        slider.value = Mathf.Clamp(menu.inputSettings.mouseSensitivity, slider.minValue, slider.maxValue);

        // Slider visuals — match StyleSlider's look (cyan→magenta gradient fill).
        var sliderBg = NewUI("Background", sliderRT);
        Stretch(sliderBg, 0f, 0f, 0f, 0f);
        var sliderBgImg = sliderBg.gameObject.AddComponent<Image>();
        sliderBgImg.sprite = GalaxyHudKit.SlotSprite();
        sliderBgImg.type = Image.Type.Sliced;
        sliderBgImg.color = Color.white;

        var fillArea = NewUI("Fill Area", sliderRT);
        fillArea.anchorMin = new Vector2(0f, 0.25f);
        fillArea.anchorMax = new Vector2(1f, 0.75f);
        fillArea.offsetMin = new Vector2(8f, 0f);
        fillArea.offsetMax = new Vector2(-8f, 0f);
        var fill = NewUI("Fill", fillArea);
        Stretch(fill, 0f, 0f, 0f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = GalaxyHudKit.RoundedGradientFillSprite(
            GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        fillImg.type = Image.Type.Sliced;
        fillImg.color = Color.white;
        slider.fillRect = fill;

        var handleArea = NewUI("Handle Slide Area", sliderRT);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);
        var handle = NewUI("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 28f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = GalaxyHudKit.RoundedSprite();
        handleImg.type = Image.Type.Sliced;
        handleImg.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        // Value text on the right.
        var valueRT = NewUI("Value", ctrlRT);
        valueRT.anchorMin = new Vector2(0.7f, 0f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.pivot = new Vector2(0.5f, 0.5f);
        valueRT.offsetMin = new Vector2(8f, 0f);
        valueRT.offsetMax = new Vector2(0f, 0f);
        var valueTmp = valueRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(valueTmp);
        valueTmp.fontSize = 22f;
        valueTmp.fontStyle = FontStyles.Bold;
        valueTmp.alignment = TextAlignmentOptions.Center;
        valueTmp.color = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
        valueTmp.raycastTarget = false;
        valueTmp.text = $"{Mathf.RoundToInt(slider.value)}";

        slider.onValueChanged.AddListener(v =>
        {
            int val = Mathf.RoundToInt(v);
            valueTmp.text = $"{val}";
            // Update the live setting so PlayerController/Ship pick it up immediately.
            menu.inputSettings.mouseSensitivity = val;
            // Sync the hidden InputField text — SettingsMenu.CloseMenu reads
            // from it on close, and we want the persisted value to match.
            if (menu.mouseSensitivity != null) menu.mouseSensitivity.text = val.ToString();
        });
    }

    void BuildStickSensitivityRow(RectTransform parent, float yIndex)
    {
        // Procedural row labelled "STICK SENSITIVITY" — drives the
        // TutorialGate.StickLookSensitivity static (and the underlying
        // InputSettings field) via PlayerPrefs so player + ship right-stick
        // look reads it live. Range 0.5..10 in 0.1 steps.
        var menu = GetComponentInParent<SettingsMenu>() ?? FindObjectOfType<SettingsMenu>();
        if (menu == null || menu.inputSettings == null) return;

        var rowRT = NewUI("StickSensitivity_Row", parent);
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -yIndex * 76f);
        rowRT.sizeDelta = new Vector2(0f, 64f);

        var labelRT = NewUI("Label", rowRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0.45f, 1f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(0f, 0f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(label);
        label.text = "STICK SENSITIVITY";
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        label.characterSpacing = 4f;
        label.raycastTarget = false;
        var labelGlow = label.gameObject.AddComponent<Shadow>();
        labelGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.45f);
        labelGlow.effectDistance = new Vector2(0f, -1.5f);

        var ctrlRT = NewUI("Control", rowRT);
        ctrlRT.anchorMin = new Vector2(0.45f, 0f);
        ctrlRT.anchorMax = new Vector2(1f, 1f);
        ctrlRT.pivot = new Vector2(0.5f, 0.5f);
        ctrlRT.offsetMin = new Vector2(8f, 0f);
        ctrlRT.offsetMax = new Vector2(0f, 0f);

        var sliderRT = NewUI("Slider", ctrlRT);
        sliderRT.anchorMin = new Vector2(0f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.7f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        sliderRT.offsetMin = new Vector2(0f, -14f);
        sliderRT.offsetMax = new Vector2(-8f, 14f);

        var slider = sliderRT.gameObject.AddComponent<Slider>();
        slider.minValue = 0.5f;
        slider.maxValue = 10f;
        slider.wholeNumbers = false;
        slider.value = Mathf.Clamp(menu.inputSettings.stickLookSensitivity, slider.minValue, slider.maxValue);

        var sliderBg = NewUI("Background", sliderRT);
        Stretch(sliderBg, 0f, 0f, 0f, 0f);
        var sliderBgImg = sliderBg.gameObject.AddComponent<Image>();
        sliderBgImg.sprite = GalaxyHudKit.SlotSprite();
        sliderBgImg.type = Image.Type.Sliced;
        sliderBgImg.color = Color.white;

        var fillArea = NewUI("Fill Area", sliderRT);
        fillArea.anchorMin = new Vector2(0f, 0.25f);
        fillArea.anchorMax = new Vector2(1f, 0.75f);
        fillArea.offsetMin = new Vector2(8f, 0f);
        fillArea.offsetMax = new Vector2(-8f, 0f);
        var fill = NewUI("Fill", fillArea);
        Stretch(fill, 0f, 0f, 0f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = GalaxyHudKit.RoundedGradientFillSprite(
            GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        fillImg.type = Image.Type.Sliced;
        fillImg.color = Color.white;
        slider.fillRect = fill;

        var handleArea = NewUI("Handle Slide Area", sliderRT);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);
        var handle = NewUI("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 28f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = GalaxyHudKit.RoundedSprite();
        handleImg.type = Image.Type.Sliced;
        handleImg.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        var valueRT = NewUI("Value", ctrlRT);
        valueRT.anchorMin = new Vector2(0.7f, 0f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.pivot = new Vector2(0.5f, 0.5f);
        valueRT.offsetMin = new Vector2(8f, 0f);
        valueRT.offsetMax = new Vector2(0f, 0f);
        var valueTmp = valueRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(valueTmp);
        valueTmp.fontSize = 22f;
        valueTmp.fontStyle = FontStyles.Bold;
        valueTmp.alignment = TextAlignmentOptions.Center;
        valueTmp.color = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
        valueTmp.raycastTarget = false;
        valueTmp.text = $"{slider.value:0.0}";

        slider.onValueChanged.AddListener(v =>
        {
            valueTmp.text = $"{v:0.0}";
            menu.inputSettings.stickLookSensitivity = v;
            // Push to TutorialGate immediately so the next frame's stick read
            // uses the new value without waiting for SaveSettings on close.
            TutorialGate.StickLookSensitivity = v;
            // Persist immediately so a crash / abrupt close doesn't lose the
            // change. SettingsMenu.CloseMenu also persists, but doing it here
            // covers the player who tunes & exits directly to main menu.
            UnityEngine.PlayerPrefs.SetFloat("stickLookSensitivity", v);
            UnityEngine.PlayerPrefs.Save();
        });
    }

    void BuildViewDistanceRow(RectTransform parent, float yIndex)
    {
        // Procedural row for the world-spawn view-distance slider. Drives
        // InputSettings.viewDistance live (TreeSpawner / MushroomSpawner /
        // AlienNPCSpawner read it each tick). 100–1000 m, integers, default 350.
        var menu = GetComponentInParent<SettingsMenu>() ?? FindObjectOfType<SettingsMenu>();
        if (menu == null || menu.inputSettings == null) return;

        var rowRT = NewUI("ViewDistance_Row", parent);
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -yIndex * 76f);
        rowRT.sizeDelta = new Vector2(0f, 64f);

        var labelRT = NewUI("Label", rowRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0.45f, 1f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(0f, 0f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(label);
        label.text = "VIEW DISTANCE";
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        label.characterSpacing = 4f;
        label.raycastTarget = false;
        var labelGlow = label.gameObject.AddComponent<Shadow>();
        labelGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.45f);
        labelGlow.effectDistance = new Vector2(0f, -1.5f);

        var ctrlRT = NewUI("Control", rowRT);
        ctrlRT.anchorMin = new Vector2(0.45f, 0f);
        ctrlRT.anchorMax = new Vector2(1f, 1f);
        ctrlRT.pivot = new Vector2(0.5f, 0.5f);
        ctrlRT.offsetMin = new Vector2(8f, 0f);
        ctrlRT.offsetMax = new Vector2(0f, 0f);

        var sliderRT = NewUI("Slider", ctrlRT);
        sliderRT.anchorMin = new Vector2(0f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.7f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        sliderRT.offsetMin = new Vector2(0f, -14f);
        sliderRT.offsetMax = new Vector2(-8f, 14f);

        var slider = sliderRT.gameObject.AddComponent<Slider>();
        slider.minValue = 100f;
        slider.maxValue = 1000f;
        slider.wholeNumbers = true;
        slider.value = Mathf.Clamp(menu.inputSettings.viewDistance, slider.minValue, slider.maxValue);

        var sliderBg = NewUI("Background", sliderRT);
        Stretch(sliderBg, 0f, 0f, 0f, 0f);
        var sliderBgImg = sliderBg.gameObject.AddComponent<Image>();
        sliderBgImg.sprite = GalaxyHudKit.SlotSprite();
        sliderBgImg.type = Image.Type.Sliced;
        sliderBgImg.color = Color.white;

        var fillArea = NewUI("Fill Area", sliderRT);
        fillArea.anchorMin = new Vector2(0f, 0.25f);
        fillArea.anchorMax = new Vector2(1f, 0.75f);
        fillArea.offsetMin = new Vector2(8f, 0f);
        fillArea.offsetMax = new Vector2(-8f, 0f);
        var fill = NewUI("Fill", fillArea);
        Stretch(fill, 0f, 0f, 0f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = GalaxyHudKit.RoundedGradientFillSprite(
            GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        fillImg.type = Image.Type.Sliced;
        fillImg.color = Color.white;
        slider.fillRect = fill;

        var handleArea = NewUI("Handle Slide Area", sliderRT);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);
        var handle = NewUI("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 28f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = GalaxyHudKit.RoundedSprite();
        handleImg.type = Image.Type.Sliced;
        handleImg.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        var valueRT = NewUI("Value", ctrlRT);
        valueRT.anchorMin = new Vector2(0.7f, 0f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.pivot = new Vector2(0.5f, 0.5f);
        valueRT.offsetMin = new Vector2(8f, 0f);
        valueRT.offsetMax = new Vector2(0f, 0f);
        var valueTmp = valueRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(valueTmp);
        valueTmp.fontSize = 22f;
        valueTmp.fontStyle = FontStyles.Bold;
        valueTmp.alignment = TextAlignmentOptions.Center;
        valueTmp.color = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
        valueTmp.raycastTarget = false;
        valueTmp.text = $"{Mathf.RoundToInt(slider.value)} m";

        slider.onValueChanged.AddListener(v =>
        {
            int val = Mathf.RoundToInt(v);
            valueTmp.text = $"{val} m";
            menu.inputSettings.viewDistance = val;
            // Persist immediately so a player who tunes + exits to main menu
            // keeps the new value.
            UnityEngine.PlayerPrefs.SetFloat("viewDistance", val);
            UnityEngine.PlayerPrefs.Save();
        });

        // Stash the procedurally-built slider so SettingsMenu's Refresh /
        // Apply paths can still read/write through it on subsequent menu opens.
        menu.viewDistanceSlider = slider;
    }

    void BuildShipStickSensitivityRow(RectTransform parent, float yIndex)
    {
        // Same layout as BuildStickSensitivityRow but drives the ship-only
        // ShipStickLookSensitivity slider. The ship needs its own slider
        // because piloting feel (heavier turning) tolerates less twitch
        // than walking — players can dial these independently.
        var menu = GetComponentInParent<SettingsMenu>() ?? FindObjectOfType<SettingsMenu>();
        if (menu == null || menu.inputSettings == null) return;

        var rowRT = NewUI("ShipStickSensitivity_Row", parent);
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -yIndex * 76f);
        rowRT.sizeDelta = new Vector2(0f, 64f);

        var labelRT = NewUI("Label", rowRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0.45f, 1f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(0f, 0f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(label);
        label.text = "SHIP STICK SENSITIVITY";
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        label.characterSpacing = 4f;
        label.raycastTarget = false;
        var labelGlow = label.gameObject.AddComponent<Shadow>();
        labelGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.45f);
        labelGlow.effectDistance = new Vector2(0f, -1.5f);

        var ctrlRT = NewUI("Control", rowRT);
        ctrlRT.anchorMin = new Vector2(0.45f, 0f);
        ctrlRT.anchorMax = new Vector2(1f, 1f);
        ctrlRT.pivot = new Vector2(0.5f, 0.5f);
        ctrlRT.offsetMin = new Vector2(8f, 0f);
        ctrlRT.offsetMax = new Vector2(0f, 0f);

        var sliderRT = NewUI("Slider", ctrlRT);
        sliderRT.anchorMin = new Vector2(0f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.7f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        sliderRT.offsetMin = new Vector2(0f, -14f);
        sliderRT.offsetMax = new Vector2(-8f, 14f);

        var slider = sliderRT.gameObject.AddComponent<Slider>();
        slider.minValue = 0.5f;
        slider.maxValue = 10f;
        slider.wholeNumbers = false;
        slider.value = Mathf.Clamp(menu.inputSettings.shipStickSensitivity, slider.minValue, slider.maxValue);

        var sliderBg = NewUI("Background", sliderRT);
        Stretch(sliderBg, 0f, 0f, 0f, 0f);
        var sliderBgImg = sliderBg.gameObject.AddComponent<Image>();
        sliderBgImg.sprite = GalaxyHudKit.SlotSprite();
        sliderBgImg.type = Image.Type.Sliced;
        sliderBgImg.color = Color.white;

        var fillArea = NewUI("Fill Area", sliderRT);
        fillArea.anchorMin = new Vector2(0f, 0.25f);
        fillArea.anchorMax = new Vector2(1f, 0.75f);
        fillArea.offsetMin = new Vector2(8f, 0f);
        fillArea.offsetMax = new Vector2(-8f, 0f);
        var fill = NewUI("Fill", fillArea);
        Stretch(fill, 0f, 0f, 0f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = GalaxyHudKit.RoundedGradientFillSprite(
            GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        fillImg.type = Image.Type.Sliced;
        fillImg.color = Color.white;
        slider.fillRect = fill;

        var handleArea = NewUI("Handle Slide Area", sliderRT);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);
        var handle = NewUI("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 28f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = GalaxyHudKit.RoundedSprite();
        handleImg.type = Image.Type.Sliced;
        handleImg.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        var valueRT = NewUI("Value", ctrlRT);
        valueRT.anchorMin = new Vector2(0.7f, 0f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.pivot = new Vector2(0.5f, 0.5f);
        valueRT.offsetMin = new Vector2(8f, 0f);
        valueRT.offsetMax = new Vector2(0f, 0f);
        var valueTmp = valueRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(valueTmp);
        valueTmp.fontSize = 22f;
        valueTmp.fontStyle = FontStyles.Bold;
        valueTmp.alignment = TextAlignmentOptions.Center;
        valueTmp.color = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
        valueTmp.raycastTarget = false;
        valueTmp.text = $"{slider.value:0.0}";

        slider.onValueChanged.AddListener(v =>
        {
            valueTmp.text = $"{v:0.0}";
            menu.inputSettings.shipStickSensitivity = v;
            TutorialGate.ShipStickLookSensitivity = v;
            UnityEngine.PlayerPrefs.SetFloat("shipStickSensitivity", v);
            UnityEngine.PlayerPrefs.Save();
        });
    }

    void BuildAutosaveRow(RectTransform parent, float yIndex)
    {
        // Procedural row — no scene control to re-parent. Builds its own label
        // ("AUTOSAVE EVERY"), slider (1..30 minutes, integer steps) and a live
        // value text ("5 MIN") matching the styling of the other setting rows.
        var rowRT = NewUI("AutosaveInterval_Row", parent);
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -yIndex * 76f);
        rowRT.sizeDelta = new Vector2(0f, 64f);

        // Left label.
        var labelRT = NewUI("Label", rowRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0.45f, 1f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(0f, 0f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(label);
        label.text = "AUTOSAVE EVERY";
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        label.characterSpacing = 4f;
        label.raycastTarget = false;
        var labelGlow = label.gameObject.AddComponent<Shadow>();
        labelGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.45f);
        labelGlow.effectDistance = new Vector2(0f, -1.5f);

        // Right side: slider on the left ~75%, value text on the right ~25%.
        var ctrlRT = NewUI("Control", rowRT);
        ctrlRT.anchorMin = new Vector2(0.45f, 0f);
        ctrlRT.anchorMax = new Vector2(1f, 1f);
        ctrlRT.pivot = new Vector2(0.5f, 0.5f);
        ctrlRT.offsetMin = new Vector2(8f, 0f);
        ctrlRT.offsetMax = new Vector2(0f, 0f);

        var sliderRT = NewUI("Slider", ctrlRT);
        sliderRT.anchorMin = new Vector2(0f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.7f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        sliderRT.offsetMin = new Vector2(0f, -14f);
        sliderRT.offsetMax = new Vector2(-8f, 14f);

        var slider = sliderRT.gameObject.AddComponent<Slider>();
        slider.minValue = AutosaveManager.MinIntervalMinutes;
        slider.maxValue = AutosaveManager.MaxIntervalMinutes;
        slider.wholeNumbers = true;
        slider.value = PlayerPrefs.GetFloat(AutosaveManager.IntervalPrefKey,
                                             AutosaveManager.DefaultIntervalMinutes);

        // Slider visuals — match StyleSlider's look.
        var sliderBg = NewUI("Background", sliderRT);
        Stretch(sliderBg, 0f, 0f, 0f, 0f);
        var sliderBgImg = sliderBg.gameObject.AddComponent<Image>();
        sliderBgImg.sprite = GalaxyHudKit.SlotSprite();
        sliderBgImg.type = Image.Type.Sliced;
        sliderBgImg.color = Color.white;

        var fillArea = NewUI("Fill Area", sliderRT);
        fillArea.anchorMin = new Vector2(0f, 0.25f);
        fillArea.anchorMax = new Vector2(1f, 0.75f);
        fillArea.offsetMin = new Vector2(8f, 0f);
        fillArea.offsetMax = new Vector2(-8f, 0f);
        var fill = NewUI("Fill", fillArea);
        Stretch(fill, 0f, 0f, 0f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = GalaxyHudKit.RoundedGradientFillSprite(
            GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
        fillImg.type = Image.Type.Sliced;
        fillImg.color = Color.white;
        slider.fillRect = fill;

        var handleArea = NewUI("Handle Slide Area", sliderRT);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);
        var handle = NewUI("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 28f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = GalaxyHudKit.RoundedSprite();
        handleImg.type = Image.Type.Sliced;
        handleImg.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        // Value text on the right.
        var valueRT = NewUI("Value", ctrlRT);
        valueRT.anchorMin = new Vector2(0.7f, 0f);
        valueRT.anchorMax = new Vector2(1f, 1f);
        valueRT.pivot = new Vector2(0.5f, 0.5f);
        valueRT.offsetMin = new Vector2(8f, 0f);
        valueRT.offsetMax = new Vector2(0f, 0f);
        var valueTmp = valueRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(valueTmp);
        valueTmp.fontSize = 22f;
        valueTmp.fontStyle = FontStyles.Bold;
        valueTmp.alignment = TextAlignmentOptions.Center;
        valueTmp.color = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
        valueTmp.raycastTarget = false;
        valueTmp.text = $"{Mathf.RoundToInt(slider.value)} MIN";

        slider.onValueChanged.AddListener(v =>
        {
            int minutes = Mathf.RoundToInt(v);
            valueTmp.text = $"{minutes} MIN";
            if (AutosaveManager.Instance != null)
                AutosaveManager.Instance.IntervalMinutes = v;
            else
            {
                // Fallback: write directly so the value is persisted even if
                // the singleton hasn't auto-created yet.
                PlayerPrefs.SetFloat(AutosaveManager.IntervalPrefKey, v);
                PlayerPrefs.Save();
            }
        });
    }

    void BuildCardDecoration(RectTransform card)
    {
        // Glow extends past the card edges.
        var glow = NewUI("__Glow", card);
        Stretch(glow, -28f, -28f, 28f, 28f);
        var glowImg = glow.gameObject.AddComponent<Image>();
        glowImg.sprite = GalaxyHudKit.GlowSprite();
        glowImg.type = Image.Type.Sliced;
        glowImg.color = GalaxyHudKit.GlowColor;
        glowImg.raycastTarget = false;

        // Pulsing border with shadow.
        var border = NewUI("__Border", card);
        Stretch(border, 0f, 0f, 0f, 0f);
        borderImage = border.gameObject.AddComponent<Image>();
        borderImage.sprite = GalaxyHudKit.RoundedSprite();
        borderImage.type = Image.Type.Sliced;
        borderImage.color = GalaxyHudKit.BorderCool;
        borderImage.raycastTarget = false;
        var shadow = border.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.4f, 0.15f, 0.7f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -6f);

        // Nebula gradient inset 3 px.
        var bg = NewUI("__Background", card);
        Stretch(bg, 3f, 3f, -3f, -3f);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = GalaxyHudKit.NebulaSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = Color.white;
        bgImg.raycastTarget = true;

        // Top accent strip (cyan→magenta).
        var accent = NewUI("__Accent", card);
        accent.anchorMin = new Vector2(0f, 1f);
        accent.anchorMax = new Vector2(1f, 1f);
        accent.pivot = new Vector2(0.5f, 1f);
        accent.anchoredPosition = new Vector2(0f, -7f);
        accent.sizeDelta = new Vector2(-72f, 3f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.sprite = GalaxyHudKit.AccentSprite();
        accentImg.color = Color.white;
        accentImg.raycastTarget = false;

        // Twinkling stars scattered over the card.
        AddStar(card, new Vector2(0.06f, 0.86f), 5f,   0.95f, 0.0f);
        AddStar(card, new Vector2(0.13f, 0.62f), 3f,   0.65f, 1.6f);
        AddStar(card, new Vector2(0.21f, 0.92f), 2.5f, 0.55f, 2.4f);
        AddStar(card, new Vector2(0.28f, 0.42f), 4f,   0.85f, 3.1f);
        AddStar(card, new Vector2(0.36f, 0.78f), 2f,   0.50f, 4.2f);
        AddStar(card, new Vector2(0.48f, 0.18f), 3.5f, 0.80f, 5.4f);
        AddStar(card, new Vector2(0.58f, 0.50f), 2f,   0.50f, 0.7f);
        AddStar(card, new Vector2(0.66f, 0.88f), 4f,   0.85f, 2.0f);
        AddStar(card, new Vector2(0.74f, 0.36f), 2.5f, 0.60f, 4.7f);
        AddStar(card, new Vector2(0.83f, 0.72f), 5f,   0.95f, 1.2f);
        AddStar(card, new Vector2(0.92f, 0.22f), 3f,   0.70f, 5.1f);
        AddStar(card, new Vector2(0.96f, 0.58f), 2.5f, 0.55f, 3.5f);
    }

    void BuildSettingRow(RectTransform parent, string controlName, float yIndex, bool useSlider)
    {
        var existing = transform.Find(controlName);
        if (existing == null) return;

        // Row container in the layout area.
        var rowRT = NewUI(controlName + "_Row", parent);
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, -yIndex * 76f);
        rowRT.sizeDelta = new Vector2(0f, 64f);

        // Row label on the left.
        var labelRT = NewUI("Label", rowRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0.45f, 1f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(0f, 0f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(label);
        label.text = controlName.ToUpperInvariant();
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        label.characterSpacing = 4f;
        label.raycastTarget = false;
        var labelGlow = label.gameObject.AddComponent<Shadow>();
        labelGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.45f);
        labelGlow.effectDistance = new Vector2(0f, -1.5f);

        // Re-parent the existing control container into the row, on the right side.
        existing.SetParent(rowRT, worldPositionStays: false);
        var ctrlRT = (RectTransform)existing;
        ctrlRT.anchorMin = new Vector2(0.45f, 0f);
        ctrlRT.anchorMax = new Vector2(1f, 1f);
        ctrlRT.pivot = new Vector2(0.5f, 0.5f);
        ctrlRT.offsetMin = new Vector2(8f, 0f);
        ctrlRT.offsetMax = new Vector2(0f, 0f);
        ctrlRT.localScale = Vector3.one;

        // Hide the original label inside the container (we have our own).
        var origLabel = existing.Find("Text (TMP)");
        if (origLabel != null) origLabel.gameObject.SetActive(false);

        // Show only the requested control type.
        var input = existing.GetComponentInChildren<TMP_InputField>(true);
        var slider = existing.GetComponentInChildren<Slider>(true);
        if (useSlider)
        {
            if (input != null) input.gameObject.SetActive(false);
            StyleSlider(existing);
        }
        else
        {
            if (slider != null) slider.gameObject.SetActive(false);
            StyleInputField(existing);
        }
    }

    void StyleInputField(Transform container)
    {
        var input = container.GetComponentInChildren<TMP_InputField>(true);
        if (input == null) return;

        var inputRT = (RectTransform)input.transform;
        // The original was scale-2 with sizeDelta(300,*). Reset to a clean
        // stretch-horizontally layout so the field fills its row container.
        inputRT.localScale = Vector3.one;
        inputRT.anchorMin = new Vector2(0f, 0.5f);
        inputRT.anchorMax = new Vector2(1f, 0.5f);
        inputRT.pivot = new Vector2(0.5f, 0.5f);
        inputRT.anchoredPosition = Vector2.zero;
        inputRT.offsetMin = new Vector2(0f, -22f);
        inputRT.offsetMax = new Vector2(0f, 22f);

        var bg = input.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = GalaxyHudKit.RoundedSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color32(0x10, 0x08, 0x2E, 0xE0);
        }

        // Style placeholder + entered text.
        var area = input.transform.Find("Text Area");
        if (area != null)
        {
            var placeholder = area.Find("Placeholder")?.GetComponent<TextMeshProUGUI>();
            if (placeholder != null)
            {
                ApplyDefaultFont(placeholder);
                placeholder.color = new Color32(0xA8, 0xE6, 0xFF, 0x80);
                placeholder.fontSize = 22f;
                placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            }
            var text = area.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                ApplyDefaultFont(text);
                text.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
                text.fontSize = 22f;
                text.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        // Cyan highlight on focus / selection.
        var colors = input.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(0xD0, 0xF4, 0xFF, 0xFF);
        colors.pressedColor = new Color32(0xC9, 0x4F, 0xFF, 0xFF);
        colors.selectedColor = new Color32(0x5B, 0xD8, 0xFF, 0xFF);
        colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        input.colors = colors;
    }

    void StyleSlider(Transform container)
    {
        var slider = container.GetComponentInChildren<Slider>(true);
        if (slider == null) return;

        var sliderRT = (RectTransform)slider.transform;
        sliderRT.localScale = Vector3.one;
        sliderRT.anchorMin = new Vector2(0f, 0.5f);
        sliderRT.anchorMax = new Vector2(1f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        sliderRT.offsetMin = new Vector2(0f, -14f);
        sliderRT.offsetMax = new Vector2(0f, 14f);

        var bg = slider.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = GalaxyHudKit.SlotSprite();
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
        }

        var fillArea = slider.transform.Find("Fill Area");
        var fill = fillArea != null ? fillArea.Find("Fill")?.GetComponent<Image>() : null;
        if (fill != null)
        {
            fill.sprite = GalaxyHudKit.RoundedGradientFillSprite(
                GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot);
            fill.type = Image.Type.Sliced;
            fill.color = Color.white;
        }

        var handleArea = slider.transform.Find("Handle Slide Area");
        var handle = handleArea != null ? handleArea.Find("Handle")?.GetComponent<Image>() : null;
        if (handle != null)
        {
            handle.sprite = GalaxyHudKit.RoundedSprite();
            handle.type = Image.Type.Sliced;
            handle.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
            // Make handle a tidy disc-ish size.
            var handleRT = (RectTransform)handle.transform;
            handleRT.sizeDelta = new Vector2(22f, 28f);
        }
    }

    void BuildButtonsRow(RectTransform card)
    {
        var rowRT = NewUI("ButtonsRow", card);
        rowRT.anchorMin = new Vector2(0.5f, 0f);
        rowRT.anchorMax = new Vector2(0.5f, 0f);
        rowRT.pivot = new Vector2(0.5f, 0f);
        rowRT.anchoredPosition = new Vector2(0f, 36f);
        rowRT.sizeDelta = new Vector2(640f, 70f);

        var hlg = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 18f;

        AdoptButton("MainMenuButton", rowRT, "MAIN MENU", isPrimary: false);
        BuildSaveButton(rowRT);
        AdoptButton("Button",         rowRT, "RESUME",    isPrimary: true);
    }

    void BuildSaveButton(RectTransform parent)
    {
        Debug.Log("[GalaxyPauseMenuStyler] Building SAVE button.");
        var rt = NewUI("SaveButton", parent);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = GalaxyHudKit.RoundedSprite();
        bg.type = Image.Type.Sliced;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;

        bg.color = new Color32(0x2A, 0x14, 0x60, 0xF2);
        var colors = btn.colors;
        colors.normalColor = new Color32(0x2A, 0x14, 0x60, 0xF2);
        colors.highlightedColor = new Color32(0x7A, 0x42, 0xC8, 0xFF);
        colors.pressedColor     = new Color32(0xA0, 0x66, 0xE6, 0xFF);
        colors.selectedColor    = new Color32(0x7A, 0x42, 0xC8, 0xFF);
        colors.disabledColor    = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        btn.colors = colors;

        // SettingsMenu may live on a parent GameObject (the styler typically attaches to
        // the dim/panel itself). Walk up first, fall back to FindObjectOfType.
        var menu = GetComponentInParent<SettingsMenu>() ?? FindObjectOfType<SettingsMenu>();
        if (menu != null)
        {
            btn.onClick.AddListener(() => { Debug.Log("[GalaxyPauseMenuStyler] SAVE button clicked."); menu.OpenSaveDialog(); });
        }
        else
        {
            Debug.LogError("[GalaxyPauseMenuStyler] No SettingsMenu found — SAVE button is inert.");
        }

        // Top accent strip.
        var stripRT = NewUI("__Accent", rt);
        stripRT.anchorMin = new Vector2(0f, 1f);
        stripRT.anchorMax = new Vector2(1f, 1f);
        stripRT.pivot = new Vector2(0.5f, 1f);
        stripRT.anchoredPosition = new Vector2(0f, -3f);
        stripRT.sizeDelta = new Vector2(-30f, 2f);
        var stripImg = stripRT.gameObject.AddComponent<Image>();
        stripImg.sprite = GalaxyHudKit.AccentSprite();
        stripImg.color = new Color(1f, 1f, 1f, 0.85f);
        stripImg.raycastTarget = false;

        // Label.
        var lblRT = NewUI("Text (TMP)", rt);
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;
        var tmp = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(tmp);
        tmp.text = "SAVE";
        tmp.fontSize = 28f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = 8f;
        tmp.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
        tmp.raycastTarget = false;
        var sh = lblRT.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.65f);
        sh.effectDistance = new Vector2(0f, -2f);
    }

    void AdoptButton(string objectName, RectTransform parent, string label, bool isPrimary)
    {
        var existing = transform.Find(objectName);
        if (existing == null) return;

        existing.SetParent(parent, worldPositionStays: false);
        var rt = (RectTransform)existing;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        var btn = existing.GetComponent<Button>();
        var bg = existing.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = GalaxyHudKit.RoundedSprite();
            bg.type = Image.Type.Sliced;
            bg.color = isPrimary
                ? new Color32(0x2A, 0x14, 0x60, 0xF2)
                : new Color32(0x10, 0x08, 0x2E, 0xE0);
        }

        if (btn != null)
        {
            // Force-assign the background as the tint target — without this the
            // Main Menu button (saved with empty targetGraphic) won't lighten.
            if (bg != null) btn.targetGraphic = bg;

            var colors = btn.colors;
            colors.normalColor = isPrimary
                ? new Color32(0x2A, 0x14, 0x60, 0xF2)
                : new Color32(0x10, 0x08, 0x2E, 0xE0);
            // Visibly lighter on hover, brightest on press.
            colors.highlightedColor = new Color32(0x7A, 0x42, 0xC8, 0xFF);
            colors.pressedColor     = new Color32(0xA0, 0x66, 0xE6, 0xFF);
            colors.selectedColor    = new Color32(0x7A, 0x42, 0xC8, 0xFF);
            colors.disabledColor    = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.12f;
            btn.colors = colors;
        }

        // Top accent strip on each button.
        var stripRT = NewUI("__Accent", existing);
        stripRT.anchorMin = new Vector2(0f, 1f);
        stripRT.anchorMax = new Vector2(1f, 1f);
        stripRT.pivot = new Vector2(0.5f, 1f);
        stripRT.anchoredPosition = new Vector2(0f, -3f);
        stripRT.sizeDelta = new Vector2(-30f, 2f);
        var stripImg = stripRT.gameObject.AddComponent<Image>();
        stripImg.sprite = GalaxyHudKit.AccentSprite();
        stripImg.color = new Color(1f, 1f, 1f, isPrimary ? 0.85f : 0.55f);
        stripImg.raycastTarget = false;

        // Replace/restyle the existing label (Text (TMP)) inside the button.
        var lbl = existing.Find("Text (TMP)");
        if (lbl != null)
        {
            var tmp = lbl.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                ApplyDefaultFont(tmp);
                tmp.text = label;
                tmp.fontSize = 28f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.characterSpacing = 8f;
                tmp.color = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
                tmp.raycastTarget = false;

                var lblRT = (RectTransform)lbl;
                lblRT.anchorMin = Vector2.zero;
                lblRT.anchorMax = Vector2.one;
                lblRT.offsetMin = Vector2.zero;
                lblRT.offsetMax = Vector2.zero;
                lblRT.localScale = Vector3.one;

                if (lbl.GetComponent<Shadow>() == null)
                {
                    var sh = lbl.gameObject.AddComponent<Shadow>();
                    sh.effectColor = new Color(0f, 0f, 0f, 0.65f);
                    sh.effectDistance = new Vector2(0f, -2f);
                }
            }
        }
    }

    // ── Animation ──────────────────────────────────────────────────────────

    IEnumerator BorderPulse()
    {
        while (this != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.4f) + 1f) * 0.5f;
            if (borderImage != null)
                borderImage.color = Color.Lerp(GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot, t);
            yield return null;
        }
    }

    void AddStar(RectTransform parent, Vector2 anchor01, float size, float baseAlpha, float phase)
    {
        var star = NewUI("__Star", parent);
        star.anchorMin = star.anchorMax = anchor01;
        star.pivot = new Vector2(0.5f, 0.5f);
        star.anchoredPosition = Vector2.zero;
        star.sizeDelta = new Vector2(size, size);
        var img = star.gameObject.AddComponent<Image>();
        img.sprite = GetStarSprite();
        img.color = new Color(1f, 1f, 1f, baseAlpha);
        img.raycastTarget = false;
        StartCoroutine(StarTwinkle(img, baseAlpha, phase));
    }

    IEnumerator StarTwinkle(Image img, float baseAlpha, float phase)
    {
        while (img != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.2f + phase) + 1f) * 0.5f;
            var c = img.color;
            c.a = Mathf.Lerp(baseAlpha * 0.25f, baseAlpha, t);
            img.color = c;
            yield return null;
        }
    }

    static Sprite cachedStar;
    static Sprite GetStarSprite()
    {
        if (cachedStar != null) return cachedStar;
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float dx = x - 15.5f;
                float dy = y - 15.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 16f;
                float angle = Mathf.Atan2(dy, dx);
                float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 6f);
                float core = Mathf.Pow(Mathf.Clamp01(1f - r), 3f);
                float arms = Mathf.Pow(Mathf.Clamp01(1f - r * 0.95f), 6f) * spike;
                float a = Mathf.Clamp01(core + arms * 0.7f);
                pixels[y * 32 + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        cachedStar = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100f);
        cachedStar.name = "GalaxyPauseStar";
        return cachedStar;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) t.font = font;
    }
}
