using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the "Sound of Space" main menu entirely at runtime — full-screen nebula
// background, twinkling stars, big gradient title, three buttons (Play / Credits /
// Exit), and a credits panel. The visual language mirrors TutorialUI so the menu
// looks native to the game.
public class MainMenuController : MonoBehaviour
{
    // ── Galaxy palette (matches TutorialUI for visual cohesion) ────────────
    static readonly Color BgTopColor    = new Color32(0x35, 0x18, 0x66, 0xFF); // nebula violet
    static readonly Color BgMidColor    = new Color32(0x1B, 0x0C, 0x42, 0xFF); // deep purple
    static readonly Color BgBottomColor = new Color32(0x07, 0x05, 0x1C, 0xFF); // void black
    static readonly Color AccentCool    = new Color32(0x5B, 0xD8, 0xFF, 0xFF); // cyan
    static readonly Color AccentHot     = new Color32(0xC9, 0x4F, 0xFF, 0xFF); // magenta
    static readonly Color StarWhite     = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    static readonly Color SubtitleColor = new Color32(0xA8, 0xE6, 0xFF, 0xCC); // pale cyan
    static readonly Color ButtonText    = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
    static readonly Color ButtonNormal  = new Color32(0x10, 0x08, 0x2E, 0xE0);
    static readonly Color ButtonHover   = new Color32(0x7A, 0x42, 0xC8, 0xFF);
    static readonly Color ButtonPressed = new Color32(0xA0, 0x66, 0xE6, 0xFF);
    static readonly Color CreditsBackdrop = new Color32(0x00, 0x00, 0x00, 0xC8);

    // Sprites (cached across instances so reload doesn't leak textures)
    static Sprite nebulaSprite;
    static Sprite roundedSprite;
    static Sprite glowSprite;
    static Sprite accentSprite;
    static Sprite starSprite;

    GameObject creditsPanel;
    TextMeshProUGUI titleText;
    // Cached so OnCredits/HideCredits can toggle the menu-button row's
    // active state directly. Deactivating the row is the most reliable way
    // to ensure those buttons cannot be reached by any input system —
    // not by mouse, not by controller nav, not by keyboard tab — while
    // the credits modal is open. The dynamic-suppression pass in the
    // navigator was intermittent in built games; this is deterministic.
    GameObject mainMenuButtonsRoot;

    void Awake()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        AudioListener.volume = 1f;

        BuildCanvas();
    }

    void Start()
    {
        StartCoroutine(TitlePulse());
    }

    // ── Layout build ───────────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Background nebula — full-screen
        var bg = NewUI("Background", transform);
        Stretch(bg, 0f, 0f, 0f, 0f);
        var bgImage = bg.gameObject.AddComponent<Image>();
        bgImage.sprite = GetNebulaSprite();
        bgImage.color = Color.white;
        bgImage.raycastTarget = false;

        // Star field — scattered across the whole screen
        AddStars(bg);

        // Title block
        var titleRT = NewUI("Title", transform);
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot     = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -160f);
        titleRT.sizeDelta = new Vector2(1600f, 220f);
        titleText = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(titleText);
        titleText.text = "SOUND OF SPACE";
        titleText.fontSize = 132f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.characterSpacing = 14f;
        titleText.enableVertexGradient = true;
        titleText.colorGradient = new VertexGradient(AccentCool, AccentHot, AccentCool, AccentHot);
        titleText.raycastTarget = false;
        var titleGlow = titleText.gameObject.AddComponent<Shadow>();
        titleGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.6f);
        titleGlow.effectDistance = new Vector2(0f, -3f);

        // Subtitle — anchored 340 px below the canvas top (was -400 which
        // read too low on ultrawide aspect ratios because CanvasScaler's
        // matchWidthOrHeight=0.5 stretches vertical spacing).
        var subRT = NewUI("Subtitle", transform);
        subRT.anchorMin = new Vector2(0.5f, 1f);
        subRT.anchorMax = new Vector2(0.5f, 1f);
        subRT.pivot     = new Vector2(0.5f, 1f);
        subRT.anchoredPosition = new Vector2(0f, -340f);
        subRT.sizeDelta = new Vector2(1200f, 60f);
        var sub = subRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(sub);
        sub.text = "A Solar System Adventure";
        sub.fontSize = 32f;
        sub.fontStyle = FontStyles.Italic;
        sub.alignment = TextAlignmentOptions.Center;
        sub.color = SubtitleColor;
        sub.characterSpacing = 8f;
        sub.raycastTarget = false;

        // Accent strip under subtitle — shifted up with the subtitle by 60 px.
        var accent = NewUI("TitleAccent", transform);
        accent.anchorMin = new Vector2(0.5f, 1f);
        accent.anchorMax = new Vector2(0.5f, 1f);
        accent.pivot = new Vector2(0.5f, 1f);
        accent.anchoredPosition = new Vector2(0f, -400f);
        accent.sizeDelta = new Vector2(420f, 3f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.sprite = GetAccentSprite();
        accentImg.raycastTarget = false;

        // Button column. Stored on `mainMenuButtonsRoot` for OnCredits/HideCredits
        // to deactivate while the credits modal is open.
        var buttonsRT = NewUI("Buttons", transform);
        mainMenuButtonsRoot = buttonsRT.gameObject;
        buttonsRT.anchorMin = new Vector2(0.5f, 0.5f);
        buttonsRT.anchorMax = new Vector2(0.5f, 0.5f);
        buttonsRT.pivot     = new Vector2(0.5f, 0.5f);
        buttonsRT.anchoredPosition = new Vector2(0f, -120f);
        buttonsRT.sizeDelta = new Vector2(420f, 360f);
        var vlg = buttonsRT.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 22f;

        BuildButton(buttonsRT, "PlayButton", "START GAME", OnPlay);
        BuildButton(buttonsRT, "CreditsButton", "CREDITS", OnCredits);
        BuildButton(buttonsRT, "ExitButton", "EXIT GAME", OnExit);

        // Footer hint
        var footerRT = NewUI("Footer", transform);
        footerRT.anchorMin = new Vector2(0.5f, 0f);
        footerRT.anchorMax = new Vector2(0.5f, 0f);
        footerRT.pivot     = new Vector2(0.5f, 0f);
        footerRT.anchoredPosition = new Vector2(0f, 28f);
        footerRT.sizeDelta = new Vector2(800f, 32f);
        var footer = footerRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(footer);
        footer.text = "Sound of Space";
        footer.fontSize = 16f;
        footer.alignment = TextAlignmentOptions.Center;
        footer.color = new Color(SubtitleColor.r, SubtitleColor.g, SubtitleColor.b, 0.45f);
        footer.raycastTarget = false;

        // Credits panel (modal, hidden by default)
        BuildCreditsPanel();
    }

    void BuildCreditsPanel()
    {
        var panelRT = NewUI("CreditsPanel", transform);
        Stretch(panelRT, 0f, 0f, 0f, 0f);
        creditsPanel = panelRT.gameObject;
        creditsPanel.SetActive(false);

        // Give the credits panel its own override-sorted Canvas + raycaster
        // so the controller-UI navigator's "topmost canvas" logic identifies
        // it as a modal layer above the main menu buttons. Without this, the
        // dim covers the buttons visually but stick-nav still walks among
        // them because everything was on the same canvas at the same sort
        // order.
        var modalCanvas = panelRT.gameObject.AddComponent<Canvas>();
        modalCanvas.overrideSorting = true;
        modalCanvas.sortingOrder = 200;  // above the menu's main canvas (100)
        panelRT.gameObject.AddComponent<GraphicRaycaster>();

        // Backdrop dim
        var dim = panelRT.gameObject.AddComponent<Image>();
        dim.color = CreditsBackdrop;
        dim.raycastTarget = true;

        // Card
        var cardRT = NewUI("Card", panelRT);
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot     = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        cardRT.sizeDelta = new Vector2(900f, 480f);

        // Glow behind the card
        var cardGlow = NewUI("Glow", cardRT);
        Stretch(cardGlow, -32f, -32f, 32f, 32f);
        var cardGlowImg = cardGlow.gameObject.AddComponent<Image>();
        cardGlowImg.sprite = GetGlowSprite();
        cardGlowImg.type = Image.Type.Sliced;
        cardGlowImg.color = new Color(0.43f, 0.50f, 1f, 0.35f);
        cardGlowImg.raycastTarget = false;

        // Card border
        var borderImg = cardRT.gameObject.AddComponent<Image>();
        borderImg.sprite = GetRoundedSprite();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = AccentCool;

        // Background gradient inset
        var cardBg = NewUI("BG", cardRT);
        Stretch(cardBg, 3f, 3f, -3f, -3f);
        var cardBgImg = cardBg.gameObject.AddComponent<Image>();
        cardBgImg.sprite = GetNebulaSprite();
        cardBgImg.color = Color.white;
        cardBgImg.raycastTarget = true;

        // Top accent
        var topAcc = NewUI("Accent", cardBg);
        topAcc.anchorMin = new Vector2(0f, 1f);
        topAcc.anchorMax = new Vector2(1f, 1f);
        topAcc.pivot = new Vector2(0.5f, 1f);
        topAcc.anchoredPosition = new Vector2(0f, -2f);
        topAcc.sizeDelta = new Vector2(-60f, 3f);
        var topAccImg = topAcc.gameObject.AddComponent<Image>();
        topAccImg.sprite = GetAccentSprite();
        topAccImg.raycastTarget = false;

        // Title
        var creditsTitleRT = NewUI("Title", cardBg);
        creditsTitleRT.anchorMin = new Vector2(0.5f, 1f);
        creditsTitleRT.anchorMax = new Vector2(0.5f, 1f);
        creditsTitleRT.pivot     = new Vector2(0.5f, 1f);
        creditsTitleRT.anchoredPosition = new Vector2(0f, -36f);
        creditsTitleRT.sizeDelta = new Vector2(800f, 70f);
        var creditsTitle = creditsTitleRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(creditsTitle);
        creditsTitle.text = "CREDITS";
        creditsTitle.fontSize = 48f;
        creditsTitle.fontStyle = FontStyles.Bold;
        creditsTitle.alignment = TextAlignmentOptions.Center;
        creditsTitle.characterSpacing = 12f;
        creditsTitle.enableVertexGradient = true;
        creditsTitle.colorGradient = new VertexGradient(AccentCool, AccentHot, AccentCool, AccentHot);
        creditsTitle.raycastTarget = false;

        // Body
        var bodyRT = NewUI("Body", cardBg);
        bodyRT.anchorMin = new Vector2(0.5f, 0.5f);
        bodyRT.anchorMax = new Vector2(0.5f, 0.5f);
        bodyRT.pivot     = new Vector2(0.5f, 0.5f);
        bodyRT.anchoredPosition = new Vector2(0f, 0f);
        bodyRT.sizeDelta = new Vector2(780f, 280f);
        var body = bodyRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(body);
        body.text = "This project uses <b>Sebastian Lague's</b> N-body simulation and celestial body generator.\n\n" +
                    "Please check out his YouTube tutorial series if you're interested in how the planet generation and physics work!";
        body.fontSize = 26f;
        body.alignment = TextAlignmentOptions.Center;
        body.color = ButtonText;
        body.lineSpacing = 6f;
        body.enableWordWrapping = true;
        body.raycastTarget = false;

        // Back button
        var backRT = NewUI("BackButtonRT", cardBg);
        backRT.anchorMin = new Vector2(0.5f, 0f);
        backRT.anchorMax = new Vector2(0.5f, 0f);
        backRT.pivot     = new Vector2(0.5f, 0f);
        backRT.anchoredPosition = new Vector2(0f, 36f);
        backRT.sizeDelta = new Vector2(280f, 64f);
        BuildButtonContent(backRT, "BACK", HideCredits);
    }

    void BuildButton(RectTransform parent, string name, string label, System.Action onClick)
    {
        var btnRT = NewUI(name, parent);
        var le = btnRT.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 80f;
        le.flexibleHeight = 0f;
        BuildButtonContent(btnRT, label, onClick);
    }

    void BuildButtonContent(RectTransform btnRT, string label, System.Action onClick)
    {
        // Background image (rounded)
        var bg = btnRT.gameObject.AddComponent<Image>();
        bg.sprite = GetRoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = ButtonNormal;

        var btn = btnRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        var colors = btn.colors;
        colors.normalColor = ButtonNormal;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = ButtonPressed;
        colors.selectedColor = ButtonHover;
        colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        // Inner accent strip (top)
        var topStrip = NewUI("Accent", btnRT);
        topStrip.anchorMin = new Vector2(0f, 1f);
        topStrip.anchorMax = new Vector2(1f, 1f);
        topStrip.pivot = new Vector2(0.5f, 1f);
        topStrip.anchoredPosition = new Vector2(0f, -3f);
        topStrip.sizeDelta = new Vector2(-30f, 2f);
        var stripImg = topStrip.gameObject.AddComponent<Image>();
        stripImg.sprite = GetAccentSprite();
        stripImg.raycastTarget = false;
        stripImg.color = new Color(1f, 1f, 1f, 0.65f);

        // Label
        var labelRT = NewUI("Label", btnRT);
        Stretch(labelRT, 0f, 0f, 0f, 0f);
        var labelTMP = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(labelTMP);
        labelTMP.text = label;
        labelTMP.fontSize = 30f;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.characterSpacing = 8f;
        labelTMP.color = ButtonText;
        labelTMP.raycastTarget = false;
        var labelShadow = labelTMP.gameObject.AddComponent<Shadow>();
        labelShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        labelShadow.effectDistance = new Vector2(0f, -2f);
    }

    void AddStars(RectTransform parent)
    {
        // Hand-tuned scatter with varied size + alpha + phase for a non-grid feel.
        AddStar(parent, new Vector2(0.06f, 0.88f), 4f,   0.85f, 0.3f);
        AddStar(parent, new Vector2(0.13f, 0.32f), 3f,   0.70f, 1.6f);
        AddStar(parent, new Vector2(0.21f, 0.74f), 2.5f, 0.55f, 2.4f);
        AddStar(parent, new Vector2(0.27f, 0.18f), 5f,   0.95f, 0.9f);
        AddStar(parent, new Vector2(0.34f, 0.55f), 2f,   0.50f, 3.1f);
        AddStar(parent, new Vector2(0.41f, 0.84f), 3.5f, 0.80f, 4.2f);
        AddStar(parent, new Vector2(0.48f, 0.12f), 2.5f, 0.60f, 1.8f);
        AddStar(parent, new Vector2(0.55f, 0.46f), 2f,   0.50f, 5.4f);
        AddStar(parent, new Vector2(0.62f, 0.78f), 4f,   0.85f, 0.6f);
        AddStar(parent, new Vector2(0.66f, 0.22f), 3f,   0.70f, 2.9f);
        AddStar(parent, new Vector2(0.71f, 0.59f), 2.5f, 0.55f, 4.7f);
        AddStar(parent, new Vector2(0.78f, 0.86f), 5f,   0.95f, 1.2f);
        AddStar(parent, new Vector2(0.83f, 0.40f), 2f,   0.50f, 3.5f);
        AddStar(parent, new Vector2(0.88f, 0.68f), 3.5f, 0.75f, 2.0f);
        AddStar(parent, new Vector2(0.93f, 0.16f), 4f,   0.85f, 5.1f);
        AddStar(parent, new Vector2(0.96f, 0.48f), 2.5f, 0.60f, 0.4f);
        AddStar(parent, new Vector2(0.18f, 0.05f), 2f,   0.45f, 4.0f);
        AddStar(parent, new Vector2(0.45f, 0.95f), 3f,   0.70f, 1.1f);
        AddStar(parent, new Vector2(0.72f, 0.04f), 2.5f, 0.55f, 3.8f);
        AddStar(parent, new Vector2(0.50f, 0.65f), 2f,   0.45f, 5.6f);
    }

    void AddStar(RectTransform parent, Vector2 anchor01, float size, float baseAlpha, float phase)
    {
        var star = NewUI("Star", parent);
        star.anchorMin = star.anchorMax = anchor01;
        star.pivot = new Vector2(0.5f, 0.5f);
        star.anchoredPosition = Vector2.zero;
        star.sizeDelta = new Vector2(size, size);
        var img = star.gameObject.AddComponent<Image>();
        img.sprite = GetStarSprite();
        img.color = new Color(StarWhite.r, StarWhite.g, StarWhite.b, baseAlpha);
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

    IEnumerator TitlePulse()
    {
        while (titleText != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.0f) + 1f) * 0.5f;
            float scale = Mathf.Lerp(0.985f, 1.015f, t);
            titleText.transform.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
    }

    // ── Button handlers ────────────────────────────────────────────────────

    GameObject saveSelectionPanel;

    public void OnPlay()
    {
        OpenSaveSelectionPanel();
    }

    void OpenSaveSelectionPanel()
    {
        if (saveSelectionPanel != null) return;
        var panel = SaveLoadUI.Build(
            transform,
            SaveLoadMode.Load,
            onSelect: () => { /* called after pick/new — we navigate scenes inside the callbacks */ },
            onPickSlot: (saveName) =>
            {
                var data = SaveSystem.LoadFromDisk(saveName);
                if (data == null)
                {
                    Debug.LogError($"[MainMenu] Failed to load save '{saveName}'.");
                    return;
                }
                PendingLoad.ScheduleLoad(data);
                // Loading screen pops INSTANTLY; the chunked async variant
                // of EnsureGameplaySingletons yields between each singleton
                // creation so the loading bar animates through the seeding
                // block instead of freezing at one value.
                if (LoadingScreen.Instance != null)
                    LoadingScreen.Instance.LoadSceneAndShow("1.6.7.7.7", preSceneSetup: EnsureGameplaySingletonsAsync);
                else { EnsureGameplaySingletons(); SceneManager.LoadScene("1.6.7.7.7"); }
            },
            onCreateOrNew: (_) =>
            {
                // New Game inherits no save, so reset all DontDestroyOnLoad
                // singletons + static progress to fresh defaults once the
                // gameplay scene loads (otherwise the previous unsaved session's
                // hotbar / money / dust / dex / story progress leak in).
                NewGameReset.Schedule();
                if (LoadingScreen.Instance != null)
                    LoadingScreen.Instance.LoadSceneAndShow("1.6.7.7.7", preSceneSetup: EnsureGameplaySingletonsAsync);
                else { EnsureGameplaySingletons(); SceneManager.LoadScene("1.6.7.7.7"); }
            },
            onClose: () =>
            {
                if (saveSelectionPanel != null) Destroy(saveSelectionPanel);
                saveSelectionPanel = null;
            });
        saveSelectionPanel = panel.root;
    }

    public void OnCredits()
    {
        // Hide the menu-button row so PLAY / CREDITS / EXIT cannot be
        // reached by any input source while credits is open. Deactivating
        // the GameObject removes their Selectables from
        // Selectable.allSelectablesArray and stops their raycasters from
        // hit-testing.
        if (mainMenuButtonsRoot != null) mainMenuButtonsRoot.SetActive(false);
        if (creditsPanel != null) creditsPanel.SetActive(true);
    }

    public void HideCredits()
    {
        if (creditsPanel != null) creditsPanel.SetActive(false);
        if (mainMenuButtonsRoot != null) mainMenuButtonsRoot.SetActive(true);
    }

    public void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ⚠️ SINGLE SOURCE OF TRUTH for "what singletons must exist before the
    // gameplay scene loads from a build." Every `RuntimeInitializeOnLoadMethod`
    // singleton that skips MainMenu must be seeded here (CLAUDE.md grass-
    // flicker incident — PixelLightLimitFix in particular). The sync
    // wrapper below drains this coroutine without yielding so callers that
    // can't be async (the LoadingScreen.Instance == null fallback) get
    // identical behaviour. Add new singletons HERE only; the sync version
    // picks them up automatically.
    //
    // `report` callback is invoked with (0..1 fraction, status text) after
    // each step — used by LoadingScreen to drive the bar fill + status
    // label. Pass null when draining synchronously.
    public static System.Collections.IEnumerator EnsureGameplaySingletonsAsync(System.Action<float, string> report)
    {
        const int Total = 37;
        int step = 0;
        System.Action<string> tick = (label) =>
        {
            step++;
            report?.Invoke((float)step / Total, "Initializing " + label + "...");
        };

        if (PlayerWallet.Instance == null) { var go = new GameObject("PlayerWallet"); DontDestroyOnLoad(go); go.AddComponent<PlayerWallet>(); }
        tick("wallet");           yield return null;
        if (TutorialUI.Instance == null) { var go = new GameObject("TutorialUI"); DontDestroyOnLoad(go); go.AddComponent<TutorialUI>(); }
        tick("tutorial UI");      yield return null;
        if (WoodInventory.Instance == null) { var go = new GameObject("WoodInventory"); DontDestroyOnLoad(go); go.AddComponent<WoodInventory>(); }
        tick("wood inventory");   yield return null;
        if (CrystalInventory.Instance == null) { var go = new GameObject("CrystalInventory"); DontDestroyOnLoad(go); go.AddComponent<CrystalInventory>(); }
        tick("crystal inventory"); yield return null;
        if (BonusTutorial.Instance == null) { var go = new GameObject("BonusTutorial"); DontDestroyOnLoad(go); go.AddComponent<BonusTutorial>(); }
        tick("bonus tutorial");   yield return null;
        if (MapTutorial.Instance == null) { var go = new GameObject("MapTutorial"); DontDestroyOnLoad(go); go.AddComponent<MapTutorial>(); }
        tick("map tutorial");     yield return null;
        if (Hotbar.Instance == null) { var go = new GameObject("Hotbar"); DontDestroyOnLoad(go); go.AddComponent<Hotbar>(); }
        tick("hotbar");           yield return null;
        if (StorageUI.Instance == null) { var go = new GameObject("StorageUI"); DontDestroyOnLoad(go); go.AddComponent<StorageUI>(); }
        tick("storage UI");       yield return null;
        if (FishStagingUI.Instance == null) { var go = new GameObject("FishStagingUI"); DontDestroyOnLoad(go); go.AddComponent<FishStagingUI>(); }
        tick("fish staging");     yield return null;
        if (AutosaveManager.Instance == null) { var go = new GameObject("AutosaveManager"); DontDestroyOnLoad(go); go.AddComponent<AutosaveManager>(); }
        tick("autosave");         yield return null;
        if (TutorialPerformanceReview.Instance == null) { var go = new GameObject("TutorialPerformanceReview"); DontDestroyOnLoad(go); go.AddComponent<TutorialPerformanceReview>(); }
        tick("perf review");      yield return null;
        if (CompassHUD.Instance == null) { var go = new GameObject("CompassHUD"); DontDestroyOnLoad(go); go.AddComponent<CompassHUD>(); }
        tick("compass");          yield return null;
        if (NoteReadUI.Instance == null) { var go = new GameObject("NoteReadUI"); DontDestroyOnLoad(go); go.AddComponent<NoteReadUI>(); }
        tick("note UI");          yield return null;
        if (InteractPromptUI.Instance == null) { var go = new GameObject("InteractPromptUI"); DontDestroyOnLoad(go); go.AddComponent<InteractPromptUI>(); }
        tick("interact prompt");  yield return null;
        if (VitalsHUD.Instance == null) { var go = new GameObject("VitalsHUD"); DontDestroyOnLoad(go); go.AddComponent<VitalsHUD>(); }
        tick("vitals HUD");       yield return null;
        if (WaterFillHUD.Instance == null) { var go = new GameObject("WaterFillHUD"); DontDestroyOnLoad(go); go.AddComponent<WaterFillHUD>(); }
        tick("water HUD");        yield return null;
        if (TabbedPauseMenu.Instance == null) { var go = new GameObject("TabbedPauseMenu"); DontDestroyOnLoad(go); go.AddComponent<TabbedPauseMenu>(); }
        tick("pause menu");       yield return null;
        if (CameraEffectsManager.Instance == null) { var go = new GameObject("CameraEffectsManager"); DontDestroyOnLoad(go); go.AddComponent<CameraEffectsManager>(); }
        tick("camera FX");        yield return null;
        // PixelLightLimitFix — raises QualitySettings.pixelLightCount to 64
        // so torches stay per-pixel instead of getting demoted per camera
        // frustum. Without this seed the ground breathes brighter/dimmer as
        // the camera rotates (grass-flicker incident — CLAUDE.md top).
        if (PixelLightLimitFix.Instance == null) { var go = new GameObject("[PixelLightLimitFix]"); DontDestroyOnLoad(go); go.AddComponent<PixelLightLimitFix>(); }
        tick("lighting fix");     yield return null;
        if (HALLineHUD.Instance == null) { var go = new GameObject("HALLineHUD"); DontDestroyOnLoad(go); go.AddComponent<HALLineHUD>(); }
        tick("HAL line HUD");     yield return null;
        if (HALVolunteeredLog.Instance == null) { var go = new GameObject("HALVolunteeredLog"); DontDestroyOnLoad(go); go.AddComponent<HALVolunteeredLog>(); }
        tick("HAL log");          yield return null;
        if (HALVoicePlayer.Instance == null) { var go = new GameObject("HALVoicePlayer"); DontDestroyOnLoad(go); go.AddComponent<HALVoicePlayer>(); }
        tick("HAL voice");        yield return null;
        if (HALCommentator.Instance == null) { var go = new GameObject("HALCommentator"); DontDestroyOnLoad(go); go.AddComponent<HALCommentator>(); }
        tick("HAL commentator");  yield return null;
        if (GForceHUD.Instance == null) { var go = new GameObject("GForceHUD"); DontDestroyOnLoad(go); go.AddComponent<GForceHUD>(); }
        tick("G-force HUD");      yield return null;
        if (FlightAssistStatusHUD.Instance == null) { var go = new GameObject("FlightAssistStatusHUD"); DontDestroyOnLoad(go); go.AddComponent<FlightAssistStatusHUD>(); }
        tick("flight assist");    yield return null;
        if (ShipNameHUD.Instance == null) { var go = new GameObject("ShipNameHUD"); DontDestroyOnLoad(go); go.AddComponent<ShipNameHUD>(); }
        tick("ship name HUD");    yield return null;
        if (KillstreakManager.Instance == null) { var go = new GameObject("KillstreakManager"); DontDestroyOnLoad(go); go.AddComponent<KillstreakManager>(); }
        tick("killstreak mgr");   yield return null;
        if (KillstreakHUD.Instance == null) { var go = new GameObject("KillstreakHUD"); DontDestroyOnLoad(go); go.AddComponent<KillstreakHUD>(); }
        tick("killstreak HUD");   yield return null;
        if (PickupUIManager.Instance == null) { var go = new GameObject("PickupUIManager"); DontDestroyOnLoad(go); go.AddComponent<PickupUIManager>(); }
        tick("pickup UI");        yield return null;
        if (SpaceDustInventory.Instance == null) { var go = new GameObject("SpaceDustInventory"); DontDestroyOnLoad(go); go.AddComponent<SpaceDustInventory>(); }
        tick("space dust");       yield return null;
        if (AIMemoryStore.Instance == null) { var go = new GameObject("AIMemoryStore"); DontDestroyOnLoad(go); go.AddComponent<AIMemoryStore>(); }
        tick("AI memory");        yield return null;
        if (GameKnowledgeBase.Instance == null) { var go = new GameObject("GameKnowledgeBase"); DontDestroyOnLoad(go); go.AddComponent<GameKnowledgeBase>(); }
        tick("AI knowledge");     yield return null;
        if (AIStoryController.Instance == null) { var go = new GameObject("AIStoryController"); DontDestroyOnLoad(go); go.AddComponent<AIStoryController>(); }
        tick("AI story");         yield return null;
        if (LLMService.Instance == null) { var go = new GameObject("LLMService"); DontDestroyOnLoad(go); go.AddComponent<LLMService>(); }
        tick("AI model");         yield return null;
        if (PlayerPhoneUI.Instance == null) { var go = new GameObject("PlayerPhoneUI"); DontDestroyOnLoad(go); go.AddComponent<PlayerPhoneUI>(); }
        tick("phone UI");         yield return null;
        if (DeathCutsceneController.Instance == null) { var go = new GameObject("DeathCutsceneController"); DontDestroyOnLoad(go); go.AddComponent<DeathCutsceneController>(); }
        tick("death cutscene");   yield return null;
        if (StoryDirector.Instance == null) { var go = new GameObject("StoryDirector"); DontDestroyOnLoad(go); go.AddComponent<StoryDirector>(); }
        tick("story director");   yield return null;
        if (HintTrackRunner.Instance == null) { var go = new GameObject("HintTrackRunner"); DontDestroyOnLoad(go); go.AddComponent<HintTrackRunner>(); }
        tick("hint tracks");       yield return null;
    }

    // Synchronous wrapper — drains the async coroutine without yielding so
    // callers that can't run a coroutine (LoadingScreen.Instance == null
    // fallback) get identical seeding behaviour. ONE source of truth in
    // EnsureGameplaySingletonsAsync above prevents the async/sync drift
    // that caused the grass-flicker regression (PixelLightLimitFix and 16
    // others were missing from the async version when the chunked seeder
    // first landed).
    static void EnsureGameplaySingletons()
    {
        var iter = EnsureGameplaySingletonsAsync(null);
        while (iter.MoveNext()) { /* drain — each yield return null is a no-op when iterated this way */ }
    }

    // Dead code below was the previous sync implementation. Kept commented
    // for one rebuild cycle as a safety net in case the coroutine drain
    // has a subtle ordering difference I missed. Delete after testing.
    static void EnsureGameplaySingletons_Legacy()
    {
        // We skip auto-creation while in the menu scene, so going Menu → Play
        // needs to seed these manually before the gameplay scene loads.
        if (PlayerWallet.Instance == null)
        {
            var go = new GameObject("PlayerWallet");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerWallet>();
        }
        if (TutorialUI.Instance == null)
        {
            var go = new GameObject("TutorialUI");
            DontDestroyOnLoad(go);
            go.AddComponent<TutorialUI>();
        }
        if (WoodInventory.Instance == null)
        {
            var go = new GameObject("WoodInventory");
            DontDestroyOnLoad(go);
            go.AddComponent<WoodInventory>();
        }
        if (CrystalInventory.Instance == null)
        {
            var go = new GameObject("CrystalInventory");
            DontDestroyOnLoad(go);
            go.AddComponent<CrystalInventory>();
        }
        if (BonusTutorial.Instance == null)
        {
            var go = new GameObject("BonusTutorial");
            DontDestroyOnLoad(go);
            go.AddComponent<BonusTutorial>();
        }
        if (MapTutorial.Instance == null)
        {
            var go = new GameObject("MapTutorial");
            DontDestroyOnLoad(go);
            go.AddComponent<MapTutorial>();
        }
        if (Hotbar.Instance == null)
        {
            var go = new GameObject("Hotbar");
            DontDestroyOnLoad(go);
            go.AddComponent<Hotbar>();
        }
        if (StorageUI.Instance == null)
        {
            // RuntimeInitializeOnLoadMethod auto-creates once at game start,
            // which in a build is the MainMenu scene where the singleton
            // early-returns. Seed it here on PLAY / LOAD so it exists when
            // the player opens a loot box in the gameplay scene.
            var go = new GameObject("StorageUI");
            DontDestroyOnLoad(go);
            go.AddComponent<StorageUI>();
        }
        if (FishStagingUI.Instance == null)
        {
            // Phase 4 picker — same MainMenu-trap seed pattern as StorageUI.
            var go = new GameObject("FishStagingUI");
            DontDestroyOnLoad(go);
            go.AddComponent<FishStagingUI>();
        }
        if (AutosaveManager.Instance == null)
        {
            var go = new GameObject("AutosaveManager");
            DontDestroyOnLoad(go);
            go.AddComponent<AutosaveManager>();
        }
        if (TutorialPerformanceReview.Instance == null)
        {
            // Auto-create RuntimeInitializeOnLoadMethod runs ONCE at game
            // start. In a build that's the MainMenu scene, where we early-
            // out — so the gameplay scene never gets the singleton. Seed it
            // here on the way out of the menu so it's ready when the
            // tutorial finishes.
            var go = new GameObject("TutorialPerformanceReview");
            DontDestroyOnLoad(go);
            go.AddComponent<TutorialPerformanceReview>();
        }
        if (CompassHUD.Instance == null)
        {
            var go = new GameObject("CompassHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<CompassHUD>();
        }
        if (NoteReadUI.Instance == null)
        {
            var go = new GameObject("NoteReadUI");
            DontDestroyOnLoad(go);
            go.AddComponent<NoteReadUI>();
        }
        if (InteractPromptUI.Instance == null)
        {
            var go = new GameObject("InteractPromptUI");
            DontDestroyOnLoad(go);
            go.AddComponent<InteractPromptUI>();
        }
        if (VitalsHUD.Instance == null)
        {
            var go = new GameObject("VitalsHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<VitalsHUD>();
        }
        if (WaterFillHUD.Instance == null)
        {
            var go = new GameObject("WaterFillHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<WaterFillHUD>();
        }
        if (TabbedPauseMenu.Instance == null)
        {
            var go = new GameObject("TabbedPauseMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<TabbedPauseMenu>();
        }
        if (CameraEffectsManager.Instance == null)
        {
            var go = new GameObject("CameraEffectsManager");
            DontDestroyOnLoad(go);
            go.AddComponent<CameraEffectsManager>();
        }
        if (PixelLightLimitFix.Instance == null)
        {
            // Raises QualitySettings.pixelLightCount to 64 so Unity's per-pixel
            // light cap doesn't demote lights to per-vertex shading per camera
            // frustum — without this seed the ground breathes brighter/dimmer
            // in wedges as the camera rotates. The singleton's AutoCreate
            // RuntimeInitializeOnLoadMethod early-returns when the active
            // scene is MainMenu, so builds (which launch in MainMenu) never
            // get it unless we seed here before the LoadScene call. See the
            // grass-flicker incident write-up at the top of CLAUDE.md.
            var go = new GameObject("[PixelLightLimitFix]");
            DontDestroyOnLoad(go);
            go.AddComponent<PixelLightLimitFix>();
        }
        if (HALLineHUD.Instance == null)
        {
            // HUD strip that surfaces AI-volunteered lines outside the phone.
            // Same MainMenu-skip trap as the others — auto-create early-outs
            // in MainMenu, so we seed here before the gameplay scene loads.
            var go = new GameObject("HALLineHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<HALLineHUD>();
        }
        if (HALVolunteeredLog.Instance == null)
        {
            // In-memory log of volunteered HAL lines. AIChatScreen reads it
            // on open so the player sees a transcript of HAL's notifications
            // alongside their chat history. MUST be seeded before
            // HALCommentator so commentator's first volunteer doesn't race
            // a null-instance check.
            var go = new GameObject("HALVolunteeredLog");
            DontDestroyOnLoad(go);
            go.AddComponent<HALVolunteeredLog>();
        }
        if (HALVoicePlayer.Instance == null)
        {
            // Plays HAL's pre-generated voice clips when HALLineHUD shows
            // a line with a matching entry in HALVoiceManifest. Lazy-loads
            // clips from StreamingAssets/AI/voice/. Same MainMenu-skip
            // trap as everything else here — must be seeded explicitly
            // before the gameplay scene loads.
            var go = new GameObject("HALVoicePlayer");
            DontDestroyOnLoad(go);
            go.AddComponent<HALVoicePlayer>();
        }
        if (HALCommentator.Instance == null)
        {
            // Event subscriber that triggers volunteered HAL lines on game
            // events (death, kill streaks, story phase shifts, first time
            // visiting a body, EarlyGameProgress milestones, enemy
            // proximity, idle ambient observations). Pairs with HALLineHUD
            // and HALVolunteeredLog — the commentator picks the line, the
            // HUD shows it transiently, the log stores it for chat replay.
            var go = new GameObject("HALCommentator");
            DontDestroyOnLoad(go);
            go.AddComponent<HALCommentator>();
        }
        if (GForceHUD.Instance == null)
        {
            var go = new GameObject("GForceHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<GForceHUD>();
        }
        if (FlightAssistStatusHUD.Instance == null)
        {
            // Same MainMenu early-out problem as GForceHUD — seed here so the
            // VELOCITY/ORBIT MATCHED + "Already piloting ship" toasts work in
            // a build (where the first scene is MainMenu and the auto-create
            // RuntimeInitializeOnLoadMethod returns without spawning).
            var go = new GameObject("FlightAssistStatusHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<FlightAssistStatusHUD>();
        }
        if (ShipNameHUD.Instance == null)
        {
            var go = new GameObject("ShipNameHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<ShipNameHUD>();
        }
        if (KillstreakManager.Instance == null)
        {
            var go = new GameObject("KillstreakManager");
            DontDestroyOnLoad(go);
            go.AddComponent<KillstreakManager>();
        }
        if (KillstreakHUD.Instance == null)
        {
            var go = new GameObject("KillstreakHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<KillstreakHUD>();
        }
        if (PickupUIManager.Instance == null)
        {
            // Save-load round-trip calls PickupUIManager.Instance.RegisterPickup
            // during Apply; seed here so the singleton exists before the
            // gameplay scene starts processing.
            var go = new GameObject("PickupUIManager");
            DontDestroyOnLoad(go);
            go.AddComponent<PickupUIManager>();
        }
        if (SpaceDustInventory.Instance == null)
        {
            var go = new GameObject("SpaceDustInventory");
            DontDestroyOnLoad(go);
            go.AddComponent<SpaceDustInventory>();
        }
        if (AIMemoryStore.Instance == null)
        {
            var go = new GameObject("AIMemoryStore");
            DontDestroyOnLoad(go);
            go.AddComponent<AIMemoryStore>();
        }
        if (GameKnowledgeBase.Instance == null)
        {
            var go = new GameObject("GameKnowledgeBase");
            DontDestroyOnLoad(go);
            go.AddComponent<GameKnowledgeBase>();
        }
        if (AIStoryController.Instance == null)
        {
            var go = new GameObject("AIStoryController");
            DontDestroyOnLoad(go);
            go.AddComponent<AIStoryController>();
        }
        if (LLMService.Instance == null)
        {
            var go = new GameObject("LLMService");
            DontDestroyOnLoad(go);
            go.AddComponent<LLMService>();
        }
        if (PlayerPhoneUI.Instance == null)
        {
            var go = new GameObject("PlayerPhoneUI");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerPhoneUI>();
        }
        if (DeathCutsceneController.Instance == null)
        {
            var go = new GameObject("DeathCutsceneController");
            DontDestroyOnLoad(go);
            go.AddComponent<DeathCutsceneController>();
        }
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

    // ── Procedural sprite generation ───────────────────────────────────────

    static Sprite GetNebulaSprite()
    {
        if (nebulaSprite != null) return nebulaSprite;
        var tex = MakeNebulaTexture(256);
        nebulaSprite = Sprite.Create(tex, new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f),
                                      100f, 0u, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
        nebulaSprite.name = "MainMenuNebula";
        return nebulaSprite;
    }

    static Sprite GetRoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        var tex = MakeRoundedRectTexture(64, 18, Color.white);
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                      100f, 0u, SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
        roundedSprite.name = "MainMenuRounded";
        return roundedSprite;
    }

    static Sprite GetGlowSprite()
    {
        if (glowSprite != null) return glowSprite;
        var tex = MakeRadialGlowTexture(96);
        glowSprite = Sprite.Create(tex, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f),
                                    100f, 0u, SpriteMeshType.FullRect, new Vector4(40, 40, 40, 40));
        glowSprite.name = "MainMenuGlow";
        return glowSprite;
    }

    static Sprite GetAccentSprite()
    {
        if (accentSprite != null) return accentSprite;
        var tex = MakeHorizontalGradient(128, 4, AccentCool, AccentHot);
        accentSprite = Sprite.Create(tex, new Rect(0, 0, 128, 4), new Vector2(0.5f, 0.5f), 100f);
        accentSprite.name = "MainMenuAccent";
        return accentSprite;
    }

    static Sprite GetStarSprite()
    {
        if (starSprite != null) return starSprite;
        var tex = MakeStarTexture(32);
        starSprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100f);
        starSprite.name = "MainMenuStar";
        return starSprite;
    }

    static Texture2D MakeNebulaTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / (size - 1);
            // Three-stop vertical gradient: bottom (void) → mid (deep purple) → top (nebula violet)
            Color baseColor = v < 0.5f
                ? Color.Lerp(BgBottomColor, BgMidColor, v * 2f)
                : Color.Lerp(BgMidColor, BgTopColor, (v - 0.5f) * 2f);

            for (int x = 0; x < size; x++)
            {
                float u = (float)x / (size - 1);
                // Two layers of cheap noise for nebula warp.
                float n1 = Mathf.PerlinNoise(u * 2.6f + 4.7f, v * 2.6f + 9.3f);
                float n2 = Mathf.PerlinNoise(u * 6.5f + 11.1f, v * 6.5f + 21.7f);
                float warp = Mathf.SmoothStep(0f, 1f, n1) * 0.35f + n2 * 0.10f;
                Color tinted = Color.Lerp(baseColor,
                                           new Color(0.50f, 0.22f, 0.78f, baseColor.a),
                                           warp);
                pixels[y * size + x] = tinted;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeRoundedRectTexture(int size, int cornerRadius, Color color)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color(color.r, color.g, color.b,
                    color.a * RoundedRectAlpha(x, y, size, cornerRadius));
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeRadialGlowTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.6f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeHorizontalGradient(int width, int height, Color left, Color right)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            float t = (float)x / (width - 1);
            Color c = Color.Lerp(left, right, t);
            for (int y = 0; y < height; y++)
                pixels[y * width + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeStarTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                float angle = Mathf.Atan2(dy, dx);
                float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 6f);
                float core = Mathf.Pow(Mathf.Clamp01(1f - r), 3f);
                float arms = Mathf.Pow(Mathf.Clamp01(1f - r * 0.95f), 6f) * spike;
                float a = Mathf.Clamp01(core + arms * 0.7f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static float RoundedRectAlpha(int x, int y, int size, int radius)
    {
        int dx = 0, dy = 0;
        if (x < radius) dx = radius - x;
        else if (x >= size - radius) dx = x - (size - radius - 1);
        if (y < radius) dy = radius - y;
        else if (y >= size - radius) dy = y - (size - radius - 1);
        if (dx <= 0 || dy <= 0) return 1f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(radius - d + 0.5f);
    }
}
