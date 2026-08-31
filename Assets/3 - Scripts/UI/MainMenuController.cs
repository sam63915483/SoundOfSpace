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

    // §1: looping space-ambient track for the menu. Assign the clip in
    // MainMenu.unity on the MainMenuController object. Left null = silent menu
    // (graceful). Built as a runtime AudioSource so no scene component is needed.
    [Header("Audio")]
    [SerializeField] AudioClip menuAmbience;
    [SerializeField, Range(0f, 1f)] float menuAmbienceVolume = 0.5f;
    AudioSource _ambienceSource;
    // Button hover/click SFX are handled by the shared UiSfxPlayer (used by the
    // save/load UI and the in-game pause menu too) — see UiSfxPlayer.Attach.

    void Awake()
    {
        // Clears the menu-open input block as well as the clock. Quitting to the
        // menu from a paused game already routes through PauseState.Exit, but if
        // any path ever misses it the flag would silently suppress gameplay input
        // in the NEXT session — cheap insurance against a very confusing bug.
        PauseState.Exit();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        // The user's saved master volume, NOT 1f — the hardcoded reset here
        // silently undid the volume slider on every trip to the menu. Read
        // straight from PlayerPrefs because InputSettings.Begin may not have
        // run yet in this scene.
        AudioListener.volume = InputSettings.Active != null
            ? InputSettings.Active.masterVolume
            : PlayerPrefs.GetFloat("masterVolume", InputSettings.defaultMasterVolume);

        BuildCanvas();
        StartMenuAmbience();
    }

    void StartMenuAmbience()
    {
        if (menuAmbience == null) return;   // no clip assigned → silent menu
        _ambienceSource = gameObject.AddComponent<AudioSource>();
        _ambienceSource.clip = menuAmbience;
        _ambienceSource.loop = true;
        _ambienceSource.playOnAwake = false;
        _ambienceSource.volume = menuAmbienceVolume;
        _ambienceSource.spatialBlend = 0f;  // 2D
        _ambienceSource.ignoreListenerPause = true;
        GameAudioBus.Register(_ambienceSource, GameAudioBus.Bus.Music);
        _ambienceSource.Play();
    }

    // ── MenuOrbit 3D background ────────────────────────────────────────────
    // The stripped copy of the gameplay scene (Assets/4 - Scenes/MenuOrbit)
    // loads ADDITIVELY behind this overlay canvas: the shuttle tours the
    // planets while a shot director cuts between camera angles. Additive keeps
    // the active scene named "MainMenu", so every MainMenu-skipping
    // auto-singleton still early-returns (trap #1 in CLAUDE.md). Starting the
    // game loads 1.6.7.7.7 single-mode, which unloads the background
    // automatically.
    GameObject menuBgRoot;   // nebula image + stars, faded out once 3D is live

    IEnumerator LoadOrbitBackground()
    {
        var op = SceneManager.LoadSceneAsync("MenuOrbit", LoadSceneMode.Additive);
        if (op == null) yield break;   // scene missing from build — keep the nebula
        yield return op;

        // The background scene brings the gameplay camera (with the atmosphere
        // post stack) and its own AudioListener — retire this scene's.
        foreach (var cam in FindObjectsOfType<Camera>())
            if (cam.gameObject.scene.name == "MainMenu") cam.enabled = false;
        foreach (var lis in FindObjectsOfType<AudioListener>())
            if (lis.gameObject.scene.name == "MainMenu") lis.enabled = false;

        // Fade the flat nebula out to reveal the live solar system.
        if (menuBgRoot != null)
        {
            var images = menuBgRoot.GetComponentsInChildren<Image>(true);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 1.5f;
                foreach (var img in images)
                {
                    var c = img.color;
                    c.a = Mathf.Lerp(c.a, 0f, t);
                    img.color = c;
                }
                yield return null;
            }
            menuBgRoot.SetActive(false);
        }
    }

    void Start()
    {
        StartCoroutine(LoadOrbitBackground());
        StartCoroutine(TitlePulse());

        // BuildCanvas ran in Awake, which can beat CharacterStore's
        // AfterSceneLoad creation — so the chip was drawn with no store to read.
        // Subscribe and redraw now that it exists.
        SubscribeToCharacterStore();
        RefreshCharacterChip();
    }

    bool subscribedToCharacterStore;

    void SubscribeToCharacterStore()
    {
        if (subscribedToCharacterStore) return;
        if (CharacterStore.Instance == null) return;
        CharacterStore.Instance.Changed += RefreshCharacterChip;
        subscribedToCharacterStore = true;
    }

    void OnDestroy()
    {
        if (subscribedToCharacterStore && CharacterStore.Instance != null)
            CharacterStore.Instance.Changed -= RefreshCharacterChip;
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
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        // Height-matched: every vertical offset in this file is budgeted against a
        // 1080 logical height (title -160, accent -364, chip -392, column top at
        // H/2-84 — see BuildCharacterChip). match=0.5 shrank the logical height on
        // ultrawide (935 at 21:9, 764 at 32:9), driving the center-anchored button
        // column up into the title and EXIT GAME off the bottom of the screen.
        scaler.matchWidthOrHeight = 1f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Background nebula — full-screen. Cached so the MenuOrbit 3D background
        // (loaded additively in Start) can fade it out once the live solar
        // system is rendering behind the canvas; if that scene ever fails to
        // load, the nebula simply stays — graceful either way.
        var bg = NewUI("Background", transform);
        Stretch(bg, 0f, 0f, 0f, 0f);
        var bgImage = bg.gameObject.AddComponent<Image>();
        bgImage.sprite = GetNebulaSprite();
        bgImage.color = Color.white;
        bgImage.raycastTarget = false;
        menuBgRoot = bg.gameObject;

        // Star field — scattered across the whole screen
        AddStars(bg);

        // Title block
        var titleRT = NewUI("Title", transform);
        // Stretch across the full width (was a fixed 1600) so the rect never
        // overhangs on aspects narrower than 16:9; auto-sizing below shrinks the
        // text to fit. Vertical placement unchanged: top edge at -160, 220 tall.
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot     = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -160f);
        titleRT.sizeDelta = new Vector2(-160f, 220f);
        titleText = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(titleText);
        titleText.text = "SOUND OF SPACE";
        titleText.fontSize = 132f;
        // Height-matched scaling means logical width drops below 1920 on aspects
        // narrower than 16:9 (1440 at 4:3); auto-size lets the 1600-wide title
        // shrink instead of overhanging. At 16:9+ it stays at 132.
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 60f;
        titleText.fontSizeMax = 132f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.characterSpacing = 14f;
        titleText.enableVertexGradient = true;
        titleText.colorGradient = new VertexGradient(AccentCool, AccentHot, AccentCool, AccentHot);
        titleText.raycastTarget = false;
        var titleGlow = titleText.gameObject.AddComponent<Shadow>();
        titleGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.6f);
        titleGlow.effectDistance = new Vector2(0f, -3f);

        // The "A Solar System Adventure" subtitle used to sit at -340. Removed
        // 2026-08-09: it was redundant next to the title, and the space it took
        // is what forced the character chip down into the START GAME row.
        // SubtitleColor is still used by the chip and the footer.

        // Accent strip under the title. Moved up from -400 into the space the
        // subtitle vacated, so the chip below it clears the button column.
        var accent = NewUI("TitleAccent", transform);
        accent.anchorMin = new Vector2(0.5f, 1f);
        accent.anchorMax = new Vector2(0.5f, 1f);
        accent.pivot = new Vector2(0.5f, 1f);
        accent.anchoredPosition = new Vector2(0f, -364f);
        accent.sizeDelta = new Vector2(420f, 3f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.sprite = GetAccentSprite();
        accentImg.raycastTarget = false;

        // "PLAYING AS <name>" chip — the whole of the character flow's presence
        // on the main menu. The menu never asks you to pick a character; it uses
        // the one you used last, and this is how you see who that is and change
        // it. Sits between the accent strip and the button column.
        BuildCharacterChip();

        // Button column. Stored on `mainMenuButtonsRoot` for OnCredits/HideCredits
        // to deactivate while the credits modal is open.
        var buttonsRT = NewUI("Buttons", transform);
        mainMenuButtonsRoot = buttonsRT.gameObject;
        buttonsRT.anchorMin = new Vector2(0.5f, 0.5f);
        buttonsRT.anchorMax = new Vector2(0.5f, 0.5f);
        buttonsRT.pivot     = new Vector2(0.5f, 0.5f);
        // Dropped from -120 to -160 to clear the character chip above it.
        // At the 1080 reference height this puts the column's top edge at -456
        // from the canvas top (540 - 160 + 244), i.e. 26 px below the chip.
        buttonsRT.anchoredPosition = new Vector2(0f, -160f);
        // 6 rows x 68 + 5 x 16 gaps = 488. Was 404 for five rows; adding
        // CHARACTERS without growing it would clip the last row (the same bug
        // MULTIPLAYER caused when it was added).
        buttonsRT.sizeDelta = new Vector2(460f, 488f);
        var vlg = buttonsRT.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 16f;

        BuildButton(buttonsRT, "PlayButton", "START GAME", OnPlay);
        if (FeatureVault.Multiplayer)
            BuildButton(buttonsRT, "MultiplayerButton", "MULTIPLAYER", OnMultiplayer);
        // Directly below MULTIPLAYER. Not gated by FeatureVault.Multiplayer —
        // your character is your name and suit in single player too.
        BuildButton(buttonsRT, "CharactersButton", "CHARACTERS", OnCharacters);
        BuildButton(buttonsRT, "CreditsButton", "CREDITS", OnCredits);
        BuildButton(buttonsRT, "GalleryButton", "COMMUNITY GALLERY", OnCommunityGallery);
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
        le.preferredHeight = 68f;
        le.flexibleHeight = 0f;
        BuildButtonContent(btnRT, label, onClick);
    }

    /// Menu option row — the approved "treatment D".
    ///
    /// Left-aligned with a cyan caret, on a rule that stays faint until hover
    /// and then FILLS left-to-right in cyan→magenta. On hover a thin cyan
    /// scanline also wipes down through the row once and leaves it lit.
    ///
    /// The scanline is the reason this treatment was picked: it is the same
    /// motif as the stasis pod's DOWNLOADING screen, which is exactly where a
    /// joining player arrives. The menu and the arrival rhyme.
    ///
    /// Replaces the old centred pill (rounded background + top accent strip).
    void BuildButtonContent(RectTransform btnRT, string label, System.Action onClick)
    {
        // Transparent hit target — the row has no fill of its own now, but a
        // Button still needs a raycastable graphic to be clickable at all.
        var hit = btnRT.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        var btn = btnRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;
        // Colour transitions are driven by MenuOptionRow instead, so the Button
        // must not also tint the (invisible) background.
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick());
        UiSfxPlayer.Attach(btn);   // shared hover + click SFX

        // Lit wash that fades in behind the row on hover.
        var washRT = NewUI("Wash", btnRT);
        Stretch(washRT, 0f, 0f, 0f, 0f);
        var wash = washRT.gameObject.AddComponent<Image>();
        wash.sprite = GetAccentSprite();
        wash.color = new Color(AccentCool.r, AccentCool.g, AccentCool.b, 0f);
        wash.raycastTarget = false;

        // The rule along the bottom, and the gradient that fills it.
        var trackRT = NewUI("Rule", btnRT);
        trackRT.anchorMin = new Vector2(0f, 0f);
        trackRT.anchorMax = new Vector2(1f, 0f);
        trackRT.pivot = new Vector2(0.5f, 0f);
        trackRT.anchoredPosition = Vector2.zero;
        trackRT.sizeDelta = new Vector2(0f, 2f);
        var track = trackRT.gameObject.AddComponent<Image>();
        track.color = new Color(1f, 1f, 1f, 0.14f);
        track.raycastTarget = false;

        var fillRT = NewUI("Fill", trackRT);
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(0f, 1f);
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.anchoredPosition = Vector2.zero;
        fillRT.sizeDelta = new Vector2(0f, 0f);
        var fill = fillRT.gameObject.AddComponent<Image>();
        fill.sprite = GetAccentSprite();
        fill.color = Color.white;
        fill.raycastTarget = false;

        // The scanline that sweeps down once per hover.
        var scanRT = NewUI("Scanline", btnRT);
        scanRT.anchorMin = new Vector2(0f, 1f);
        scanRT.anchorMax = new Vector2(1f, 1f);
        scanRT.pivot = new Vector2(0.5f, 1f);
        scanRT.anchoredPosition = Vector2.zero;
        scanRT.sizeDelta = new Vector2(0f, 2f);
        var scan = scanRT.gameObject.AddComponent<Image>();
        scan.sprite = GetAccentSprite();
        scan.color = new Color(AccentCool.r, AccentCool.g, AccentCool.b, 0f);
        scan.raycastTarget = false;

        // Caret
        var caretRT = NewUI("Caret", btnRT);
        caretRT.anchorMin = new Vector2(0f, 0f);
        caretRT.anchorMax = new Vector2(0f, 1f);
        caretRT.pivot = new Vector2(0f, 0.5f);
        caretRT.anchoredPosition = new Vector2(10f, 0f);
        caretRT.sizeDelta = new Vector2(34f, 0f);
        var caretTMP = caretRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(caretTMP);
        caretTMP.text = ">";
        caretTMP.fontSize = 26f;
        caretTMP.fontStyle = FontStyles.Bold;
        caretTMP.alignment = TextAlignmentOptions.Left;
        caretTMP.color = new Color(AccentCool.r, AccentCool.g, AccentCool.b, 0.55f);
        caretTMP.raycastTarget = false;

        // Label
        var labelRT = NewUI("Label", btnRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(46f, 0f);
        labelRT.offsetMax = new Vector2(-12f, 0f);
        var labelTMP = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(labelTMP);
        labelTMP.text = label;
        labelTMP.fontSize = 28f;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Left;
        labelTMP.characterSpacing = 8f;
        labelTMP.color = new Color(ButtonText.r, ButtonText.g, ButtonText.b, 0.72f);
        labelTMP.raycastTarget = false;
        var labelShadow = labelTMP.gameObject.AddComponent<Shadow>();
        labelShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        labelShadow.effectDistance = new Vector2(0f, -2f);

        var row = btnRT.gameObject.AddComponent<MenuOptionRow>();
        row.Init(labelTMP, caretTMP, fillRT, scan, wash);
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
        // The ONLY place the character flow can interrupt you, and only when you
        // own zero characters. Otherwise this is exactly the old behaviour: one
        // click from the menu to save select.
        EnsureCharacterUI().RequireCharacter(OpenSaveSelectionPanel);
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
                OfferMultiplayerThen(EnterGameplay);
            },
            onCreateOrNew: (_) =>
            {
                // New Game inherits no save, so reset all DontDestroyOnLoad
                // singletons + static progress to fresh defaults once the
                // gameplay scene loads (otherwise the previous unsaved session's
                // hotbar / money / dust / dex / story progress leak in).
                NewGameReset.Schedule();
                OfferMultiplayerThen(EnterGameplay);
            },
            onClose: () =>
            {
                if (saveSelectionPanel != null) Destroy(saveSelectionPanel);
                saveSelectionPanel = null;
            });
        saveSelectionPanel = panel.root;
    }

    /// The single waist both save paths converge on. Kept as one method so the
    /// multiplayer prompt has exactly one place to interpose.
    void EnterGameplay()
    {
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.LoadSceneAndShow("1.6.7.7.7", preSceneSetup: EnsureGameplaySingletonsAsync);
        else { EnsureGameplaySingletons(); SceneManager.LoadScene("1.6.7.7.7"); }
    }

    /// Offers "play together?" and runs `solo` if they decline. With multiplayer
    /// vaulted off this is a straight passthrough, so the prompt never appears
    /// and the menu behaves exactly as it did before any of this existed.
    void OfferMultiplayerThen(System.Action solo)
    {
        if (!FeatureVault.Multiplayer) { solo(); return; }
        EnsureMultiplayerUI();
        if (MultiplayerMenuUI.Instance == null) { solo(); return; }

        // The picker has done its job — the save is chosen either way from here,
        // and leaving it up behind the prompt just gives the player two dialogs
        // to reason about.
        CloseSaveSelectionPanel();

        MultiplayerMenuUI.Instance.AskPlayTogether(solo);
    }

    void CloseSaveSelectionPanel()
    {
        if (saveSelectionPanel == null) return;
        Destroy(saveSelectionPanel);
        saveSelectionPanel = null;
    }

    void EnsureMultiplayerUI()
    {
        if (MultiplayerMenuUI.Instance != null) return;
        var go = new GameObject("MultiplayerMenuUI");
        go.transform.SetParent(transform, false);
        go.AddComponent<MultiplayerMenuUI>();
    }

    public void OnMultiplayer()
    {
        if (!FeatureVault.Multiplayer) return;
        // Same gate as OnPlay — your friends need to see a name and a colour, so
        // a session cannot be started without a character.
        EnsureCharacterUI().RequireCharacter(() =>
        {
            EnsureMultiplayerUI();
            MultiplayerMenuUI.Instance?.OpenJoin();
        });
    }

    // ── Character system ───────────────────────────────────────────────────

    RectTransform characterChipRT;
    TextMeshProUGUI characterChipLabel;
    Image characterChipDot;

    CharacterUI EnsureCharacterUI() => CharacterUI.Ensure(transform, astronautPreviewPrefab);

    public void OnCharacters()
    {
        // Same reachability guard the credits and gallery modals use: the menu
        // rows must be unreachable by mouse, pad or keyboard while a modal is up.
        if (mainMenuButtonsRoot != null) mainMenuButtonsRoot.SetActive(false);
        EnsureCharacterUI().OpenList(() =>
        {
            if (mainMenuButtonsRoot != null) mainMenuButtonsRoot.SetActive(true);
            RefreshCharacterChip();
        });
    }

    /// The chip itself is the "change character" affordance — clicking it opens
    /// the quick picker rather than the full management list.
    void OnCharacterChip()
    {
        if (mainMenuButtonsRoot != null) mainMenuButtonsRoot.SetActive(false);

        System.Action restore = () =>
        {
            if (mainMenuButtonsRoot != null) mainMenuButtonsRoot.SetActive(true);
            RefreshCharacterChip();
        };

        var ui = EnsureCharacterUI();
        var store = CharacterStore.Instance;
        // With nobody to pick from, the chip reads "TAP TO CREATE" — so honour
        // that and go straight to the create screen instead of an empty picker.
        // OpenCreate (not RequireCharacter) because `restore` must run on cancel
        // too, or the menu buttons stay hidden.
        if (store == null || !store.HasAny) ui.OpenCreate(restore);
        else                                ui.OpenPicker(restore);
    }

    void BuildCharacterChip()
    {
        characterChipRT = NewUI("CharacterChip", transform);
        characterChipRT.anchorMin = new Vector2(0.5f, 1f);
        characterChipRT.anchorMax = new Vector2(0.5f, 1f);
        characterChipRT.pivot     = new Vector2(0.5f, 1f);
        // Vertical budget from the canvas top, at the 1080 reference height:
        //   title glyphs end ≈ -347   accent -364   chip -392…-430
        //   button column top -456    (see buttonsRT.anchoredPosition below)
        // That leaves a 26 px gap above START GAME. If you move any of these,
        // move the others — the chip overlapped START GAME on the first pass.
        characterChipRT.anchoredPosition = new Vector2(0f, -392f);
        characterChipRT.sizeDelta = new Vector2(360f, 38f);

        var bg = characterChipRT.gameObject.AddComponent<Image>();
        bg.color = new Color32(0x10, 0x08, 0x2E, 0x8C);

        var btn = characterChipRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OnCharacterChip);
        UiSfxPlayer.Attach(btn);

        characterChipDot = NewUI("Dot", characterChipRT).gameObject.AddComponent<Image>();
        var dotRT = characterChipDot.rectTransform;
        dotRT.anchorMin = new Vector2(0f, 0.5f);
        dotRT.anchorMax = new Vector2(0f, 0.5f);
        dotRT.pivot     = new Vector2(0f, 0.5f);
        dotRT.anchoredPosition = new Vector2(14f, 0f);
        dotRT.sizeDelta = new Vector2(16f, 16f);
        characterChipDot.raycastTarget = false;

        var labelRT = NewUI("Label", characterChipRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(38f, 0f);
        labelRT.offsetMax = new Vector2(-14f, 0f);
        characterChipLabel = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(characterChipLabel);
        characterChipLabel.fontSize = 17f;
        characterChipLabel.characterSpacing = 8f;
        characterChipLabel.alignment = TextAlignmentOptions.Center;
        characterChipLabel.color = new Color(SubtitleColor.r, SubtitleColor.g, SubtitleColor.b, 0.9f);
        characterChipLabel.raycastTarget = false;
        characterChipLabel.overflowMode = TextOverflowModes.Ellipsis;
        characterChipLabel.enableWordWrapping = false;

        RefreshCharacterChip();
    }

    /// Shows who you are, or invites you to become someone if you are nobody.
    /// Called on build, whenever a character modal closes, and from
    /// CharacterStore.Changed.
    void RefreshCharacterChip()
    {
        if (characterChipLabel == null) return;

        var profile = CharacterStore.ActiveProfile;
        if (profile == null)
        {
            characterChipLabel.text = "NO CHARACTER  —  TAP TO CREATE";
            if (characterChipDot != null) characterChipDot.color = new Color(1f, 1f, 1f, 0.25f);
            return;
        }

        characterChipLabel.text = $"PLAYING AS  {profile.name.ToUpperInvariant()}   ▾";
        if (characterChipDot != null)
            characterChipDot.color = SuitPalette.ColorAt(profile.swatchIndex);
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

    // Lazily created on first use so the menu never pays for it unless the
    // player actually opens the gallery. Reused on subsequent opens/closes
    // rather than rebuilt (mirrors creditsPanel's SetActive toggle pattern).
    CommunityGalleryUI communityGalleryUI;

    public void OnCommunityGallery()
    {
        // Same reachability guard as OnCredits: hide the button row so PLAY /
        // CREDITS / GALLERY / EXIT cannot be reached while the modal is open.
        if (mainMenuButtonsRoot != null) mainMenuButtonsRoot.SetActive(false);
        if (communityGalleryUI == null)
        {
            var go = new GameObject("CommunityGalleryUI");
            go.transform.SetParent(transform, false);
            communityGalleryUI = go.AddComponent<CommunityGalleryUI>();
        }
        // Not configured yet (dev hasn't deployed the server) — still open;
        // CommunityGalleryUI shows a "not set up yet" message itself so the
        // button stays honest instead of being hidden.
        communityGalleryUI.Open(HideCommunityGallery);
    }

    public void HideCommunityGallery()
    {
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
        const int Total = 61; // keep in sync with the number of tick() calls below or the loading bar over/undershoots
        int step = 0;
        System.Action<string> tick = (label) =>
        {
            step++;
            report?.Invoke((float)step / Total, "Initializing " + label + "...");
        };

        if (PlayerWallet.Instance == null) { var go = new GameObject("PlayerWallet"); DontDestroyOnLoad(go); go.AddComponent<PlayerWallet>(); }
        tick("wallet");           yield return null;
        // Trap #1: this skips MainMenu in its own AutoCreate, so without seeding
        // here it would never exist in a BUILD and paying Tev would silently
        // fall back to the no-panel path.
        if (TevPaymentUI.Instance == null) { var go = new GameObject("TevPaymentUI"); DontDestroyOnLoad(go); go.AddComponent<TevPaymentUI>(); }
        if (TevTextDirector.Instance == null) { var go = new GameObject("TevTextDirector"); DontDestroyOnLoad(go); go.AddComponent<TevTextDirector>(); }
        if (DayRecapDirector.Instance == null) { var go = new GameObject("[DayRecapDirector]"); DontDestroyOnLoad(go); go.AddComponent<DayRecapDirector>(); }
        if (PlayerLightToggle.Instance == null) { var go = new GameObject("[PlayerLightToggle]"); DontDestroyOnLoad(go); go.AddComponent<PlayerLightToggle>(); }
        if (GrassLightAutoMarker.Instance == null) { var go = new GameObject("[GrassLightAutoMarker]"); DontDestroyOnLoad(go); go.AddComponent<GrassLightAutoMarker>(); }
        if (EclipseShadowGate.Instance == null) { var go = new GameObject("[EclipseShadowGate]"); DontDestroyOnLoad(go); go.AddComponent<EclipseShadowGate>(); }
        tick("Tev payment");      yield return null;
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
        if (DimensionDevLoader.Instance == null) { var go = new GameObject("DimensionDevLoader"); DontDestroyOnLoad(go); go.AddComponent<DimensionDevLoader>(); }
        tick("dimension loader"); yield return null;
        if (TutorialPerformanceReview.Instance == null) { var go = new GameObject("TutorialPerformanceReview"); DontDestroyOnLoad(go); go.AddComponent<TutorialPerformanceReview>(); }
        tick("perf review");      yield return null;
        if (CompassHUD.Instance == null) { var go = new GameObject("CompassHUD"); DontDestroyOnLoad(go); go.AddComponent<CompassHUD>(); }
        tick("compass");          yield return null;
        if (NoteReadUI.Instance == null) { var go = new GameObject("NoteReadUI"); DontDestroyOnLoad(go); go.AddComponent<NoteReadUI>(); }
        tick("note UI");          yield return null;
        if (InteractPromptUI.Instance == null) { var go = new GameObject("InteractPromptUI"); DontDestroyOnLoad(go); go.AddComponent<InteractPromptUI>(); }
        if (GazeHighlight.Instance == null) { var go = new GameObject("GazeHighlight"); DontDestroyOnLoad(go); go.AddComponent<GazeHighlight>(); }
        tick("interact prompt");  yield return null;
        if (NewspaperReaderUI.Instance == null) { var go = new GameObject("NewspaperReaderUI"); DontDestroyOnLoad(go); go.AddComponent<NewspaperReaderUI>(); }
        if (MonumentLinkPopupUI.Instance == null) { var go = new GameObject("MonumentLinkPopupUI"); DontDestroyOnLoad(go); go.AddComponent<MonumentLinkPopupUI>(); }
        if (VitalsHUD.Instance == null) { var go = new GameObject("VitalsHUD"); DontDestroyOnLoad(go); go.AddComponent<VitalsHUD>(); }
        tick("vitals HUD");       yield return null;
        if (OxygenManager.Instance == null) { var go = new GameObject("OxygenManager"); DontDestroyOnLoad(go); go.AddComponent<OxygenManager>(); }
        tick("oxygen system");    yield return null;
        if (PlanetOxygen.Instance == null) { var go = new GameObject("PlanetOxygen"); DontDestroyOnLoad(go); go.AddComponent<PlanetOxygen>(); }
        tick("planet oxygen");    yield return null;
        if (GalaxyTime.Instance == null) { var go = new GameObject("GalaxyTime"); DontDestroyOnLoad(go); go.AddComponent<GalaxyTime>(); }
        tick("galactic time");    yield return null;
        if (GalaxyTimeHUD.Instance == null) { var go = new GameObject("GalaxyTimeHUD"); DontDestroyOnLoad(go); go.AddComponent<GalaxyTimeHUD>(); }
        tick("clock HUD");        yield return null;
        if (TevRentCollector.Instance == null) { var go = new GameObject("TevRentCollector"); DontDestroyOnLoad(go); go.AddComponent<TevRentCollector>(); }
        tick("rent collector");   yield return null;
        if (PhosphorDialogueBox.Instance == null) { var go = new GameObject("PhosphorDialogueBox"); DontDestroyOnLoad(go); go.AddComponent<PhosphorDialogueBox>(); }
        tick("dialogue box");     yield return null;
        if (SaplingPlanter.Instance == null) { var go = new GameObject("SaplingPlanter"); DontDestroyOnLoad(go); go.AddComponent<SaplingPlanter>(); }
        if (MushroomPlanter.Instance == null) { var go = new GameObject("MushroomPlanter"); DontDestroyOnLoad(go); go.AddComponent<MushroomPlanter>(); }
        tick("sapling planter");  yield return null;
        if (DomeBuildRegistrar.Instance == null) { var go = new GameObject("DomeBuildRegistrar"); DontDestroyOnLoad(go); go.AddComponent<DomeBuildRegistrar>(); }
        tick("dome registrar");   yield return null;
        if (GrowPotRegistrar.Instance == null) { var go = new GameObject("GrowPotRegistrar"); DontDestroyOnLoad(go); go.AddComponent<GrowPotRegistrar>(); }
        tick("grow pot");         yield return null;
        if (DomeAudio.Instance == null) { var go = new GameObject("DomeAudio"); DontDestroyOnLoad(go); go.AddComponent<DomeAudio>(); }
        tick("dome audio");       yield return null;
        if (OxygenHUD.Instance == null) { var go = new GameObject("OxygenHUD"); DontDestroyOnLoad(go); go.AddComponent<OxygenHUD>(); }
        tick("oxygen HUD");       yield return null;
        if (WaterFillHUD.Instance == null) { var go = new GameObject("WaterFillHUD"); DontDestroyOnLoad(go); go.AddComponent<WaterFillHUD>(); }
        tick("water HUD");        yield return null;
        if (TabbedPauseMenu.Instance == null) { var go = new GameObject("TabbedPauseMenu"); DontDestroyOnLoad(go); go.AddComponent<TabbedPauseMenu>(); }
        tick("pause menu");       yield return null;
        if (CameraEffectsManager.Instance == null) { var go = new GameObject("CameraEffectsManager"); DontDestroyOnLoad(go); go.AddComponent<CameraEffectsManager>(); }
        tick("camera FX");        yield return null;
        if (HelmetOverlayHUD.Instance == null) { var go = new GameObject("HelmetOverlayHUD"); DontDestroyOnLoad(go); go.AddComponent<HelmetOverlayHUD>(); }
        tick("helmet HUD");       yield return null;
        if (TrailerFreeCam.Instance == null) { var go = new GameObject("TrailerFreeCam"); DontDestroyOnLoad(go); go.AddComponent<TrailerFreeCam>(); }
        tick("trailer free-cam"); yield return null;
        if (TrailerBlackHoleGrow.Instance == null) { var go = new GameObject("TrailerBlackHoleGrow"); DontDestroyOnLoad(go); go.AddComponent<TrailerBlackHoleGrow>(); }
        tick("trailer BH grow"); yield return null;
        // Progression quartet. PlayerProgress MUST come first — the other three
        // read it on their very first frame (the toast and the ceremony subscribe
        // to its static events, ProgressHooks polls it for visited worlds).
        if (PlayerProgress.Instance == null) { var go = new GameObject("PlayerProgress"); DontDestroyOnLoad(go); go.AddComponent<PlayerProgress>(); }
        if (ProgressToastUI.Instance == null) { var go = new GameObject("ProgressToastUI"); DontDestroyOnLoad(go); go.AddComponent<ProgressToastUI>(); }
        if (ProgressHooks.Instance == null) { var go = new GameObject("ProgressHooks"); DontDestroyOnLoad(go); go.AddComponent<ProgressHooks>(); }
        if (LevelUpCeremonyUI.Instance == null) { var go = new GameObject("LevelUpCeremonyUI"); DontDestroyOnLoad(go); go.AddComponent<LevelUpCeremonyUI>(); }
        // The opening's six survival beats. MUST be seeded: it's what tells a
        // brand-new player what to do after the shuttle ramp drops, and in a
        // build the RuntimeInitializeOnLoadMethod never fires (trap #1).
        if (OpeningDirector.Instance == null) { var go = new GameObject("OpeningDirector"); DontDestroyOnLoad(go); go.AddComponent<OpeningDirector>(); }
        // Feeds cave volumes to OceanEffect.shader so caves aren't flooded.
        // MUST be seeded — without it the shader globals are never set in a
        // build and every cave below sea level fills with water (trap #1).
        if (CaveOceanCutout.Instance == null) { var go = new GameObject("CaveOceanCutout"); DontDestroyOnLoad(go); go.AddComponent<CaveOceanCutout>(); }
        tick("progression");    yield return null;
        // PixelLightLimitFix — raises QualitySettings.pixelLightCount to 64
        // so torches stay per-pixel instead of getting demoted per camera
        // frustum. Without this seed the ground breathes brighter/dimmer as
        // the camera rotates (grass-flicker incident — CLAUDE.md top).
        if (PixelLightLimitFix.Instance == null) { var go = new GameObject("[PixelLightLimitFix]"); DontDestroyOnLoad(go); go.AddComponent<PixelLightLimitFix>(); }
        tick("lighting fix");     yield return null;
        if (ViewmodelFillLight.Instance == null) { var go = new GameObject("ViewmodelFillLight"); DontDestroyOnLoad(go); go.AddComponent<ViewmodelFillLight>(); }
        tick("viewmodel light");  yield return null;
        if (HeldItemViewmodel.Instance == null) { var go = new GameObject("HeldItemViewmodel"); DontDestroyOnLoad(go); go.AddComponent<HeldItemViewmodel>(); }
        tick("held items");       yield return null;
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
        // VelocityMarkersHUD skips MainMenu in its AutoCreate like the other
        // ship HUDs — without this seed the prograde/retrograde markers never
        // spawn in builds (trap #1).
        if (VelocityMarkersHUD.Instance == null) { var go = new GameObject("VelocityMarkersHUD"); DontDestroyOnLoad(go); go.AddComponent<VelocityMarkersHUD>(); }
        tick("velocity markers"); yield return null;
        if (KillstreakManager.Instance == null) { var go = new GameObject("KillstreakManager"); DontDestroyOnLoad(go); go.AddComponent<KillstreakManager>(); }
        tick("killstreak mgr");   yield return null;
        if (KillstreakHUD.Instance == null) { var go = new GameObject("KillstreakHUD"); DontDestroyOnLoad(go); go.AddComponent<KillstreakHUD>(); }
        tick("killstreak HUD");   yield return null;
        if (PickupUIManager.Instance == null) { var go = new GameObject("PickupUIManager"); DontDestroyOnLoad(go); go.AddComponent<PickupUIManager>(); }
        tick("pickup UI");        yield return null;
        if (SpaceDustInventory.Instance == null) { var go = new GameObject("SpaceDustInventory"); DontDestroyOnLoad(go); go.AddComponent<SpaceDustInventory>(); }
        tick("space dust");       yield return null;
        if (SpaceDustField.Instance == null) { var go = new GameObject("SpaceDustField"); DontDestroyOnLoad(go); go.AddComponent<SpaceDustField>(); }
        tick("dust field");       yield return null;
        if (AIMemoryStore.Instance == null) { var go = new GameObject("AIMemoryStore"); DontDestroyOnLoad(go); go.AddComponent<AIMemoryStore>(); }
        tick("AI memory");        yield return null;
        if (GameKnowledgeBase.Instance == null) { var go = new GameObject("GameKnowledgeBase"); DontDestroyOnLoad(go); go.AddComponent<GameKnowledgeBase>(); }
        tick("AI knowledge");     yield return null;
        if (AIStoryController.Instance == null) { var go = new GameObject("AIStoryController"); DontDestroyOnLoad(go); go.AddComponent<AIStoryController>(); }
        tick("AI story");         yield return null;
        if (LLMService.Instance == null) { var go = new GameObject("LLMService"); DontDestroyOnLoad(go); go.AddComponent<LLMService>(); }
        tick("AI model");         yield return null;
        if (PlayerPhoneUI.Instance == null) { var go = new GameObject("PlayerPhoneUI"); DontDestroyOnLoad(go); go.AddComponent<PlayerPhoneUI>(); }
        // Messages-app clock (want-texts, appointment deadlines). Shares the
        // phone's tick — it's the phone's back-end. Trap #1 applies.
        if (BuyerMessageDirector.Instance == null) { var go = new GameObject("[BuyerMessageDirector]"); go.AddComponent<BuyerMessageDirector>(); }
        tick("phone UI");         yield return null;
        if (DeathCutsceneController.Instance == null) { var go = new GameObject("DeathCutsceneController"); DontDestroyOnLoad(go); go.AddComponent<DeathCutsceneController>(); }
        tick("death cutscene");   yield return null;
        if (StoryDirector.Instance == null) { var go = new GameObject("StoryDirector"); DontDestroyOnLoad(go); go.AddComponent<StoryDirector>(); }
        tick("story director");   yield return null;
        if (Mission2Director.Instance == null) { var go = new GameObject("Mission2Director"); DontDestroyOnLoad(go); go.AddComponent<Mission2Director>(); }
        if (ColdCompanyDirector.Instance == null) { var go = new GameObject("ColdCompanyDirector"); DontDestroyOnLoad(go); go.AddComponent<ColdCompanyDirector>(); }
        tick("mission 2");        yield return null;
        if (HintTrackRunner.Instance == null) { var go = new GameObject("HintTrackRunner"); DontDestroyOnLoad(go); go.AddComponent<HintTrackRunner>(); }
        tick("hint tracks");       yield return null;
        if (PhotoLibrary.Instance == null) { var go = new GameObject("PhotoLibrary"); DontDestroyOnLoad(go); go.AddComponent<PhotoLibrary>(); }
        tick("photo library");    yield return null;
        if (PhotoGalleryUI.Instance == null) { var go = new GameObject("PhotoGalleryUI"); DontDestroyOnLoad(go); go.AddComponent<PhotoGalleryUI>(); }
        tick("photo gallery");    yield return null;
        if (EnemyDetectionHUD.Instance == null) { var go = new GameObject("EnemyDetectionHUD"); DontDestroyOnLoad(go); go.AddComponent<EnemyDetectionHUD>(); }
        tick("threat indicator"); yield return null;
    }

    // Synchronous wrapper — drains the async coroutine without yielding so
    // callers that can't run a coroutine (LoadingScreen.Instance == null
    // fallback) get identical seeding behaviour. ONE source of truth in
    // EnsureGameplaySingletonsAsync above prevents the async/sync drift
    // that caused the grass-flicker regression (PixelLightLimitFix and 16
    // others were missing from the async version when the chunked seeder
    // first landed).
    // public: MultiplayerSession loads the gameplay scene itself when a session
    // starts, and must seed the same singletons this menu does.
    public static void EnsureGameplaySingletons()
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
        if (NewspaperReaderUI.Instance == null)
        {
            var go = new GameObject("NewspaperReaderUI");
            DontDestroyOnLoad(go);
            go.AddComponent<NewspaperReaderUI>();
        }
        if (MonumentLinkPopupUI.Instance == null)
        {
            var go = new GameObject("MonumentLinkPopupUI");
            DontDestroyOnLoad(go);
            go.AddComponent<MonumentLinkPopupUI>();
        }
        if (VitalsHUD.Instance == null)
        {
            var go = new GameObject("VitalsHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<VitalsHUD>();
        }
        if (OxygenManager.Instance == null)
        {
            var go = new GameObject("OxygenManager");
            DontDestroyOnLoad(go);
            go.AddComponent<OxygenManager>();
        }
        if (OxygenHUD.Instance == null)
        {
            var go = new GameObject("OxygenHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<OxygenHUD>();
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
        if (ViewmodelFillLight.Instance == null)
        {
            // Short-range point light on the camera so held items stay readable
            // in the dark. Same MainMenu-skip trap as the others.
            var go = new GameObject("ViewmodelFillLight");
            DontDestroyOnLoad(go);
            go.AddComponent<ViewmodelFillLight>();
        }
        if (HeldItemViewmodel.Instance == null)
        {
            // Puts select-only hotbar items (wood/crystal/dust/sapling/fish/bag)
            // in the player's hand. Same MainMenu-skip trap as the others.
            var go = new GameObject("HeldItemViewmodel");
            DontDestroyOnLoad(go);
            go.AddComponent<HeldItemViewmodel>();
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
        if (Mission2Director.Instance == null)
        {
            // Mission 2 story wiring hub: phase gates, the StoryDirector→
            // EarlyGameProgress ORG_Reveal bridge, and queued phone beats.
            // Inert until the story-draft conversation JSONs ship in
            // StreamingAssets/Story/. Same MainMenu-skip trap as the others.
            var go = new GameObject("Mission2Director");
            DontDestroyOnLoad(go);
            go.AddComponent<Mission2Director>();
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

    // ⚠️ Serialized fields are APPENDED AT THE END of the class, never inserted
    // mid-class — reordering them corrupts existing scene/prefab serialization
    // (CLAUDE.md coding conventions). Keep new ones below this line.

    [Header("Character system")]
    [Tooltip("The astronaut model shown spinning on the character create screen. " +
             "Drag Assets/5 - External Imports/Graphics/Models/Astronaut.fbx here. " +
             "Leave empty and the create screen falls back to a flat colour plate — " +
             "everything else still works.")]
    [SerializeField] GameObject astronautPreviewPrefab;
}
