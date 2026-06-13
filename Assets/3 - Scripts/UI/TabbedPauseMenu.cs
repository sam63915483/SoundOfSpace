using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Procedural pause menu + tabbed settings sub-page. Auto-creates at runtime
/// like VitalsHUD / WaterFillHUD. Listens for the pause key (TutorialGate.
/// PausePressed) and shows a four-button main card (RESUME / SAVE GAME /
/// SETTINGS / MAIN MENU). Clicking SETTINGS swaps the card content to a
/// tabbed sub-page (CONTROLS / GRAPHICS, currently — extensible).
///
/// Settings + tabs are defined in <see cref="BuildSettingsList"/> as plain
/// data: adding a new setting is one entry, adding a tab is one TabDef. The
/// underlying data store stays <see cref="InputSettings"/> — this class
/// only owns the UI layer and reads / writes that asset via lambdas.
///
/// Replaces the old scene-bound SettingsMenu + GalaxyPauseMenuStyler combo;
/// any existing instances of those are disabled at runtime (kept around as
/// inspector references to InputSettings, since the asset isn't otherwise
/// reachable from code).
/// </summary>
public class TabbedPauseMenu : MonoBehaviour
{
    public static TabbedPauseMenu Instance { get; private set; }
    public bool IsOpen => _isPaused;

    // ── Tunables ─────────────────────────────────────────────────────
    const float CardWidth = 720f;
    const float CardMaxHeight = 920f;
    const float CardScreenMargin = 40f;
    const float ButtonWidth = 440f;
    const float ButtonHeight = 72f;
    const float SettingsRowHeight = 36f;

    // ── Palette (matches GalaxyPauseMenuStyler / HUD family) ─────────
    static readonly Color BackdropColor   = new Color32(0x05, 0x02, 0x14, 0xE0);
    static readonly Color CardBgTop       = new Color32(0x0A, 0x14, 0x30, 0xF2);
    static readonly Color CardBgBottom    = new Color32(0x05, 0x0A, 0x1A, 0xF2);
    static readonly Color CardBorderCool  = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color CardBorderHot   = new Color32(0xFF, 0x6B, 0xC8, 0xFF);
    static readonly Color ButtonBg        = new Color32(0x14, 0x30, 0x48, 0x80);
    static readonly Color ButtonBgHover   = new Color32(0x1C, 0x40, 0x60, 0xCC);
    static readonly Color ButtonBorder    = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color ButtonBorderHi  = CardBorderCool;
    static readonly Color LabelColor      = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color LabelDim        = new Color32(0xEA, 0xF6, 0xFF, 0x8C);
    static readonly Color HeaderColor     = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color SliderTrackBg   = new Color32(0x0F, 0x19, 0x2A, 0xE6);
    static readonly Color SliderFillA     = new Color32(0x7B, 0xE2, 0xFF, 0xFF);
    static readonly Color SliderFillB     = new Color32(0x4A, 0x8B, 0xFF, 0xFF);

    // ── Setting / Tab definitions ────────────────────────────────────
    abstract class RowDef
    {
        public string label;
        // When non-null, appends a small orange ★ to the label. Used to
        // mark settings that apply LIVE but whose full FPS impact won't
        // reflect until a process restart — Unity caches material/shader/
        // texture/render-target state that doesn't fully unwind on a
        // QualitySettings change. Affects MSAA, texture quality, and
        // shadow resolution. The setting still applies — the badge just
        // sets the player's expectation.
        public string restartHint;
    }

    class SliderDef : RowDef
    {
        public float min;
        public float max;
        public bool wholeNumbers;
        public string format;
        public Func<float> get;
        public Action<float> set;
        // Optional: when non-null, used INSTEAD of `format` to render the
        // value text. Lets a slider display things that aren't a plain
        // number (e.g. resolution like "1920×1080").
        public Func<float, string> formatFunc;
    }

    class ToggleDef : RowDef
    {
        public Func<bool> get;
        public Action<bool> set;
    }

    class HeaderDef : RowDef { }

    class TabDef
    {
        public string name;
        public List<RowDef> rows;
    }

    // Snap the quality-preset enum back to Custom whenever the user manually
    // edits a preset-controlled GRAPHICS slider (view distance / streaming caps
    // / concert shadows). Without this, a player who picked High and then
    // nudged a slider would see "High" claimed in the preset row while the
    // values no longer match — confusing on next open.
    //
    // Called from inside the relevant setters. Cheap to call repeatedly; the
    // RefreshAllRows is what makes the preset slider's label visibly switch
    // to CUSTOM.
    void MarkCustomQuality()
    {
        if (_input == null) return;
        if (_input.qualityPreset == InputSettings.QualityPreset.Custom) return;
        _input.qualityPreset = InputSettings.QualityPreset.Custom;
        // Don't save immediately — the underlying setter's caller is
        // responsible (per-row sliders don't save on every drag).
        RefreshAllRows();
    }

    // ── Internal state ───────────────────────────────────────────────
    InputSettings _input;
    SettingsMenu _legacyMenu;            // kept around only as a ref to the InputSettings asset
    Canvas _canvas;
    RectTransform _canvasRT;
    RectTransform _cardRT;
    Image _borderImage;
    TextMeshProUGUI _titleText;
    GameObject _mainPanel;
    GameObject _settingsPanel;
    GameObject _saveDialogRoot;
    Image _backdropImage;

    List<TabDef> _tabs;
    List<Button> _tabButtons;
    List<TextMeshProUGUI> _tabButtonLabels;
    List<GameObject> _tabContentPanels;
    int _activeTab;

    // Deduplicated (width, height) resolutions from Screen.resolutions. Built
    // lazily on first use. We dedup by w/h only because Screen.resolutions[]
    // can include multiple refresh-rate variants of the same WxH which would
    // make the resolution slider seem to do nothing for several ticks in a row.
    Vector2Int[] _resCache;

    bool _isPaused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("TabbedPauseMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<TabbedPauseMenu>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Best-effort early acquire (works when seeded after scene Awakes);
        // when seeded before the gameplay scene loads (the EnsureGameplay-
        // Singletons path in MainMenuController), the scene's SettingsMenu
        // doesn't exist yet — we retry in OnSceneLoaded and Update.
        TryAcquireLegacy();

        BuildCanvas();
        BuildCard();
        BuildSettingsList();
        BuildMainPanel();
        BuildSettingsPanel();
        ShowMainPanel();
        SetMenuVisible(false, immediate: true);
        StartCoroutine(BorderPulseRoutine());

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryAcquireLegacy();
        // Pause state must never carry across scene loads — the menu is
        // DontDestroyOnLoad, so without this an open menu would persist into
        // the next scene (and timeScale would stay at 0).
        if (_isPaused)
        {
            _isPaused = false;
            Time.timeScale = 1f;
        }
        ShowMainPanel();
        SetMenuVisible(false, immediate: true);
    }

    void TryAcquireLegacy()
    {
        if (_legacyMenu != null && _input != null) return;
        // The InputSettings asset isn't directly loadable from code; pull it
        // from the scene's SettingsMenu inspector reference. Disable the legacy
        // SettingsMenu + GalaxyPauseMenuStyler so they stop responding to the
        // pause key and stop drawing the old UI.
        var legacy = FindObjectOfType<SettingsMenu>(true);
        if (legacy != null)
        {
            _legacyMenu = legacy;
            if (_input == null) _input = legacy.inputSettings;
            legacy.enabled = false;
            if (legacy.menuPanel != null) legacy.menuPanel.SetActive(false);
        }
        var legacyStyler = FindObjectOfType<GalaxyPauseMenuStyler>(true);
        if (legacyStyler != null) legacyStyler.enabled = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ── Pause toggle ─────────────────────────────────────────────────

    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;
        // Hard skip while in MainMenu — the pause menu is DontDestroyOnLoad
        // and would otherwise pop on ESC inside the main menu, then re-lock
        // the cursor via ClosePauseDirect's InputSettings.lockCursor read.
        // Both bugs (ESC opens pause menu in main menu, cursor locked on
        // return-to-menu) traced to this gate being missing.
        if (SceneManager.GetActiveScene().name == "MainMenu")
        {
            // If we're somehow flagged as paused (shouldn't happen — ReturnToMainMenu
            // clears it — but defensive), force the state clean so a future
            // re-enter of gameplay doesn't carry the wrong _isPaused.
            if (_isPaused) { _isPaused = false; SetMenuVisible(false, immediate: true); Time.timeScale = 1f; }
            return;
        }
        // Retry the legacy acquisition until we have it — handles the case
        // where the gameplay scene loads after we were seeded (build path).
        if (_legacyMenu == null) TryAcquireLegacy();

        if (TutorialGate.PausePressed())
        {
            if (_isPaused) ClosePause();
            // Don't open over the build menu / fishingdex — both advertise
            // [ESC] CLOSE in their footer and handle their own dismissal.
            // Without this guard, ESC inside either menu would close it AND
            // immediately pop the pause menu on top of the same frame.
            else if (!BuildMenuUI.IsOpen && !FishingdexManager.IsOpen
                  && !SolarSystemMapController.IsOpen
                  && !PlayerPhoneUI.IsOpen && !PlayerPhoneUI.ConsumedEscapeThisFrame) OpenPause();
        }

        // Show / hide the SAVE AND APPLY button based on whether any row's
        // current value differs from the snapshot taken on settings-panel
        // open. Cheap — iterates ~30 rows comparing floats / bools.
        if (_saveAndApplyBtnRT != null && _settingsPanel != null && _settingsPanel.activeSelf)
        {
            bool want = HasPendingChanges();
            if (_saveAndApplyBtnRT.gameObject.activeSelf != want)
                _saveAndApplyBtnRT.gameObject.SetActive(want);
        }
    }

    public void OpenAtSettings()
    {
        // Phone's Settings app entry point. Open the pause, then swap to
        // the SETTINGS sub-page directly so the user lands on the
        // CONTROLS / GRAPHICS / CAMERA tabs without an extra click.
        OpenPause();
        ShowSettingsPanel();
    }

    void OpenPause()
    {
        _isPaused = true;
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        UiSfxPlayer.StartPauseAmbience();   // menu ambience while paused / in settings
        // Always return to the main panel when re-opening; never re-open
        // straight into the settings sub-page or save dialog.
        ShowMainPanel();
        SetMenuVisible(true, immediate: false);
    }

    void ClosePause()
    {
        // If user is currently in the settings panel with pending changes,
        // route through the save-or-discard prompt before closing. The
        // prompt's resolver invokes ClosePauseDirect once the user chooses.
        if (_settingsPanel != null && _settingsPanel.activeSelf && HasPendingChanges())
        {
            RequestExitSettings(ClosePauseDirect);
            return;
        }
        ClosePauseDirect();
    }

    void ClosePauseDirect()
    {
        _isPaused = false;
        Time.timeScale = 1f;
        SetMenuVisible(false, immediate: false);
        UiSfxPlayer.StopPauseAmbience();

        if (_input != null)
        {
            // Drop deferral so external SaveSettings / Apply* calls (e.g.
            // autosave manager) work normally outside the pause menu.
            _input.deferApply = false;
            _input.SaveSettings();
            if (_input.lockCursor)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }
    }

    void SetMenuVisible(bool visible, bool immediate)
    {
        if (_canvas != null) _canvas.enabled = visible;
        if (visible) RefreshAllRows();
    }

    // ── Canvas build ─────────────────────────────────────────────────

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Pause; // above HUDs, vendor, toasts
        // Deliberately NOT registered with HUDSceneGate — that gate force-enables
        // every registered canvas on each non-MainMenu sceneLoad, which would
        // pop the pause menu open the moment the player starts a new game from
        // the main menu (the menu is seeded by EnsureGameplaySingletons before
        // the gameplay scene loads). Visibility is owned by SetMenuVisible.
        _canvasRT = transform as RectTransform;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Backdrop — full-screen dim that also eats clicks.
        var backdropRT = NewUI("Backdrop", transform);
        Stretch(backdropRT);
        _backdropImage = backdropRT.gameObject.AddComponent<Image>();
        _backdropImage.color = BackdropColor;
        _backdropImage.raycastTarget = true;
    }

    void BuildCard()
    {
        _cardRT = NewUI("Card", transform);
        _cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        _cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        _cardRT.pivot = new Vector2(0.5f, 0.5f);
        _cardRT.anchoredPosition = Vector2.zero;
        _cardRT.sizeDelta = new Vector2(CardWidth, CardMaxHeight);

        // Background (beveled panel sprite).
        var bg = _cardRT.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = CardBgBottom;
        bg.raycastTarget = true;

        // Vertical gradient overlay (top brighter than bottom).
        var grad = NewUI("Gradient", _cardRT);
        Stretch(grad);
        var gradImg = grad.gameObject.AddComponent<Image>();
        gradImg.sprite = UIPanelSprites.GetBeveledPanel();
        gradImg.type = Image.Type.Sliced;
        gradImg.color = CardBgTop;
        gradImg.raycastTarget = false;

        // Border (pulsing).
        var border = NewUI("Border", _cardRT);
        Stretch(border);
        _borderImage = border.gameObject.AddComponent<Image>();
        _borderImage.sprite = UIPanelSprites.GetBeveledOutline();
        _borderImage.type = Image.Type.Sliced;
        _borderImage.color = CardBorderCool;
        _borderImage.raycastTarget = false;

        // Title (changes between PAUSED / SETTINGS depending on which panel
        // is showing).
        var titleRT = NewUI("Title", _cardRT);
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -28f);
        titleRT.sizeDelta = new Vector2(CardWidth - 80f, 86f);
        _titleText = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_titleText);
        _titleText.text = "PAUSED";
        _titleText.fontSize = 64f;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.characterSpacing = 14f;
        _titleText.enableVertexGradient = true;
        _titleText.colorGradient = new VertexGradient(
            CardBorderCool, CardBorderHot, CardBorderCool, CardBorderHot);
        _titleText.raycastTarget = false;
        var titleGlow = _titleText.gameObject.AddComponent<Shadow>();
        titleGlow.effectColor = new Color(0.36f, 0.85f, 1f, 0.55f);
        titleGlow.effectDistance = new Vector2(0f, -2f);
    }

    // ── Main panel (RESUME / SAVE / SETTINGS / MAIN MENU) ────────────

    void BuildMainPanel()
    {
        var panelRT = NewUI("MainPanel", _cardRT);
        StretchWithMargins(panelRT, 60f, 140f, 60f, 60f); // L, T, R, B (T from top via offsetMax neg)
        _mainPanel = panelRT.gameObject;

        var vlg = panelRT.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 12f;
        // childControl* must be true for LayoutElement preferred sizes to
        // actually resize the children — without it, buttons stay at the
        // RectTransform default of 100×100.
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        BuildMenuButton(panelRT, "RESUME",    ClosePause,                            primary: true);
        BuildMenuButton(panelRT, "SAVE GAME", OpenSaveDialog,                        primary: false);
        BuildMenuButton(panelRT, "SETTINGS",  ShowSettingsPanel,                     primary: false);
        BuildMenuButton(panelRT, "MAIN MENU", ReturnToMainMenu,                      primary: false);
    }

    Button BuildMenuButton(RectTransform parent, string label, Action onClick, bool primary)
    {
        var btnRT = NewUI(label, parent);
        var btnLE = btnRT.gameObject.AddComponent<LayoutElement>();
        btnLE.preferredWidth = ButtonWidth;
        btnLE.preferredHeight = ButtonHeight;
        btnLE.flexibleWidth = 0f;

        var bg = btnRT.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = ButtonBg;
        bg.raycastTarget = true;

        var border = NewUI("Border", btnRT);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = primary ? ButtonBorderHi : ButtonBorder;
        borderImg.raycastTarget = false;

        var lbl = NewText(btnRT, "Label", label, 26f, FontStyles.Bold, LabelColor);
        Stretch(lbl.rectTransform);
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.characterSpacing = 8f;
        lbl.raycastTarget = false;

        var btn = btnRT.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = ButtonBg;
        colors.highlightedColor = ButtonBgHover;
        colors.pressedColor = ButtonBgHover;
        colors.selectedColor = ButtonBgHover;
        colors.disabledColor = ButtonBg;
        btn.colors = colors;
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick?.Invoke());
        UiSfxPlayer.Attach(btn);

        // Hover: also tint the border + label cyan.
        var hover = btnRT.gameObject.AddComponent<ButtonHoverTint>();
        hover.Init(borderImg, lbl, primary ? ButtonBorderHi : ButtonBorder, CardBorderCool, LabelColor, CardBorderCool);

        return btn;
    }

    // ── Settings panel ───────────────────────────────────────────────

    void BuildSettingsList()
    {
        BuildResolutionCache();
        _tabs = new List<TabDef>
        {
            new TabDef
            {
                name = "CONTROLS",
                rows = new List<RowDef>
                {
                    new ToggleDef {
                        label = "CONTROLLER ENABLED",
                        get  = () => _input != null && _input.controllerEnabled,
                        set  = v  => {
                            if (_input == null) return;
                            _input.controllerEnabled = v;
                            // Push the change into TutorialGate immediately so all the
                            // PadHeld / PadPressed / RightStickX / etc. reads start
                            // returning 0 (or live values) on the next frame.
                            TutorialGate.ControllerEnabled = v;
                        },
                    },
                    new SliderDef {
                        label = "MOUSE SENSITIVITY", min = 1f, max = 200f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? _input.mouseSensitivity : 100f,
                        set  = v  => { if (_input != null) _input.mouseSensitivity = v; },
                    },
                    new SliderDef {
                        label = "MOUSE SMOOTHING", min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
                        get  = () => _input != null ? _input.mouseSmoothing : 0.2f,
                        set  = v  => { if (_input != null) _input.mouseSmoothing = v; },
                    },
                    new SliderDef {
                        label = "MASTER VOLUME", min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
                        get  = () => _input != null ? _input.masterVolume : 1f,
                        set  = v  => {
                            if (_input != null) _input.masterVolume = v;
                            AudioListener.volume = v;
                        },
                    },
                },
            },
            new TabDef
            {
                name = "CAMERA",
                rows = new List<RowDef>
                {
                    new HeaderDef { label = "MOVEMENT" },
                    new ToggleDef { label = "HEADBOB",            get = () => _input != null && _input.fxHeadbob,            set = v => { if (_input != null) _input.fxHeadbob = v; } },
                    new SliderDef { label = "HEADBOB INTENSITY",  min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
                        get = () => _input != null ? _input.fxHeadbobIntensity : 1f,
                        set = v => { if (_input != null) _input.fxHeadbobIntensity = v; } },
                    new ToggleDef { label = "LANDING DIP",        get = () => _input != null && _input.fxLandingDip,        set = v => { if (_input != null) _input.fxLandingDip = v; } },
                    new ToggleDef { label = "STRAFE TILT",        get = () => _input != null && _input.fxStrafeTilt,        set = v => { if (_input != null) _input.fxStrafeTilt = v; } },
                    new ToggleDef { label = "SPRINT FOV KICK",    get = () => _input != null && _input.fxSprintFovKick,     set = v => { if (_input != null) _input.fxSprintFovKick = v; } },

                    new HeaderDef { label = "VEHICLE" },
                    new ToggleDef { label = "JETPACK FOV KICK",   get = () => _input != null && _input.fxJetpackFovKick,    set = v => { if (_input != null) _input.fxJetpackFovKick = v; } },
                    new ToggleDef { label = "SHIP BOOST FOV",     get = () => _input != null && _input.fxShipBoostFov,      set = v => { if (_input != null) _input.fxShipBoostFov = v; } },
                    new ToggleDef { label = "SPEED LINES",        get = () => _input != null && _input.fxSpeedLines,        set = v => { if (_input != null) _input.fxSpeedLines = v; } },

                    new HeaderDef { label = "COMBAT" },
                    new ToggleDef { label = "DAMAGE FLASH",       get = () => _input != null && _input.fxDamageFlash,       set = v => { if (_input != null) _input.fxDamageFlash = v; } },
                    new ToggleDef { label = "DAMAGE VIGNETTE",    get = () => _input != null && _input.fxDamageVignette,    set = v => { if (_input != null) _input.fxDamageVignette = v; } },
                    new ToggleDef { label = "HIT SHAKE",          get = () => _input != null && _input.fxDirectionalHitShake, set = v => { if (_input != null) _input.fxDirectionalHitShake = v; } },
                    new ToggleDef { label = "ENEMY HIT MICRO-SHAKE", get = () => _input != null && _input.fxEnemyHitMicroShake, set = v => { if (_input != null) _input.fxEnemyHitMicroShake = v; } },
                    new ToggleDef { label = "DEATH TILT",         get = () => _input != null && _input.fxDeathTilt,         set = v => { if (_input != null) _input.fxDeathTilt = v; } },
                    new ToggleDef { label = "SLOWMO ON KILL",     get = () => _input != null && _input.fxSlowmoOnKill,      set = v => { if (_input != null) _input.fxSlowmoOnKill = v; } },

                    new HeaderDef { label = "SURVIVAL & CINEMATIC" },
                    new ToggleDef { label = "LOW HEALTH VIGNETTE", get = () => _input != null && _input.fxLowHealthVignette, set = v => { if (_input != null) _input.fxLowHealthVignette = v; } },
                    new ToggleDef { label = "DIALOGUE VIGNETTE",  get = () => _input != null && _input.fxDialogueVignette,  set = v => { if (_input != null) _input.fxDialogueVignette = v; } },
                    new ToggleDef { label = "LETTERBOX BARS",     get = () => _input != null && _input.fxLetterboxBars,     set = v => { if (_input != null) _input.fxLetterboxBars = v; } },
                    new ToggleDef { label = "MOOD COLOR GRADE",   get = () => _input != null && _input.fxMoodColorGrade,    set = v => { if (_input != null) _input.fxMoodColorGrade = v; } },

                    new HeaderDef { label = "LENS CHARACTER" },
                    new ToggleDef { label = "BLOOM",              get = () => _input != null && _input.fxBloom,             set = v => { if (_input != null) _input.fxBloom = v; } },
                    new ToggleDef { label = "SUBTLE VIGNETTE",    get = () => _input != null && _input.fxSubtleVignette,    set = v => { if (_input != null) _input.fxSubtleVignette = v; } },
                    new SliderDef { label = "VIGNETTE INTENSITY", min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
                        get = () => _input != null ? _input.fxSubtleVignetteIntensity : 0.45f,
                        set = v => { if (_input != null) _input.fxSubtleVignetteIntensity = v; } },
                    new ToggleDef { label = "FILM GRAIN",         get = () => _input != null && _input.fxFilmGrain,         set = v => { if (_input != null) _input.fxFilmGrain = v; } },
                    new SliderDef { label = "GRAIN INTENSITY",    min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
                        get = () => _input != null ? _input.fxFilmGrainIntensity : 0.6f,
                        set = v => { if (_input != null) _input.fxFilmGrainIntensity = v; } },
                    new ToggleDef { label = "CHROMATIC ABERRATION", get = () => _input != null && _input.fxChromaticAberration, set = v => { if (_input != null) _input.fxChromaticAberration = v; } },
                    new SliderDef { label = "CA INTENSITY",       min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
                        get = () => _input != null ? _input.fxChromaticAberrationIntensity : 0.35f,
                        set = v => { if (_input != null) _input.fxChromaticAberrationIntensity = v; } },
                    new ToggleDef { label = "LENS FLARES",        get = () => _input != null && _input.fxLensFlares,        set = v => { if (_input != null) _input.fxLensFlares = v; } },
                    new ToggleDef { label = "RADIAL MOTION BLUR", get = () => _input != null && _input.fxRadialMotionBlur, set = v => { if (_input != null) _input.fxRadialMotionBlur = v; } },
                },
            },
            new TabDef
            {
                name = "GRAPHICS",
                rows = new List<RowDef>
                {
                    // ── Display ─────────────────────────────────────────
                    // Resolution slider cycles through unique (w, h) pairs
                    // reported by Screen.resolutions (cached in _resCache so
                    // the slider range / dedup are stable). Both display
                    // settings persist via InputSettings.SaveSettings →
                    // PlayerPrefs and are re-applied on every game start via
                    // InputSettings.ApplyDisplaySettings.
                    new SliderDef {
                        label = "RESOLUTION", min = 0f, max = Mathf.Max(0f, (_resCache != null ? _resCache.Length : 1) - 1f), wholeNumbers = true,
                        format = "{0:F0}", // unused — formatFunc takes precedence
                        get  = CurrentResolutionIndexAsFloat,
                        set  = SetResolutionFromIndexFloat,
                        formatFunc = FormatResolutionIndex,
                    },
                    new ToggleDef {
                        label = "FULLSCREEN",
                        get  = () => _input != null && _input.displayFullscreen,
                        set  = v  => { if (_input != null) { _input.displayFullscreen = v; _input.ApplyDisplaySettings(); _input.SaveSettings(); } },
                    },

                    // ── Quality preset ─────────────────────────────────
                    // Slider-as-dropdown: whole-numbers 0..4 with a formatFunc
                    // that displays the enum name. Picking a non-Custom value
                    // applies the preset's field bundle (caps, view distance,
                    // concert shadows, lens FX) and refreshes every visible
                    // row so the user sees the new values immediately. Custom
                    // is a no-op — preserves whatever fields the user has
                    // hand-tuned.
                    new HeaderDef { label = "QUALITY" },
                    new SliderDef {
                        label = "QUALITY PRESET", min = 0f, max = 4f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? (float)(int)_input.qualityPreset : (float)(int)InputSettings.QualityPreset.Custom,
                        set  = v  => {
                            if (_input == null) return;
                            var preset = (InputSettings.QualityPreset)Mathf.Clamp(Mathf.RoundToInt(v), 0, 4);
                            _input.ApplyQualityPreset(preset);
                            _input.SaveSettings();
                            RefreshAllRows(); // make the slider rows below show new values
                        },
                        formatFunc = vf => {
                            var p = (InputSettings.QualityPreset)Mathf.Clamp(Mathf.RoundToInt(vf), 0, 4);
                            return p.ToString().ToUpper();
                        },
                    },
                    // ── Unity QualitySettings knobs ───────────────────
                    // Each slider is "wholeNumbers 0..N" with a formatFunc
                    // that prints the enum-value name. Touching any of these
                    // calls ApplyGraphicsQuality so the change is live, then
                    // MarkCustomQuality so the preset slider above snaps to
                    // Custom (so it doesn't lie about state).
                    //
                    // ★ marker: MSAA, texture quality, and shadow resolution
                    // changes apply LIVE but Unity caches render-target /
                    // material / shader-variant state that doesn't fully
                    // unwind on a per-frame QualitySettings change. A
                    // process restart on the chosen setting gives the
                    // cleanest perf — the visual change is immediate either
                    // way, the badge just sets the expectation.
                    new HeaderDef {
                        label = "<color=#FFB060>★</color> MARKS SETTINGS NEEDING RESTART FOR FULL FPS",
                    },
                    new SliderDef {
                        label = "ANTI-ALIASING",
                        restartHint = "MSAA buffer allocation persists in VRAM after toggle.",
                        min = 0f, max = 3f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => {
                            if (_input == null) return 1f;
                            switch (_input.antiAliasing) {
                                case InputSettings.AntiAliasingLevel.Off:    return 0f;
                                case InputSettings.AntiAliasingLevel.MSAA2x: return 1f;
                                case InputSettings.AntiAliasingLevel.MSAA4x: return 2f;
                                case InputSettings.AntiAliasingLevel.MSAA8x: return 3f;
                                default: return 1f;
                            }
                        },
                        set  = v  => {
                            if (_input == null) return;
                            int idx = Mathf.Clamp(Mathf.RoundToInt(v), 0, 3);
                            _input.antiAliasing = idx == 0 ? InputSettings.AntiAliasingLevel.Off
                                              : idx == 1 ? InputSettings.AntiAliasingLevel.MSAA2x
                                              : idx == 2 ? InputSettings.AntiAliasingLevel.MSAA4x
                                              :            InputSettings.AntiAliasingLevel.MSAA8x;
                            _input.ApplyGraphicsQuality();
                            MarkCustomQuality();
                            _input.SaveSettings();
                        },
                        formatFunc = vf => {
                            int idx = Mathf.Clamp(Mathf.RoundToInt(vf), 0, 3);
                            return idx == 0 ? "OFF" : idx == 1 ? "MSAA 2X" : idx == 2 ? "MSAA 4X" : "MSAA 8X";
                        },
                    },
                    new SliderDef {
                        label = "TEXTURE QUALITY",
                        restartHint = "High mips stay cached in VRAM once loaded.",
                        min = 0f, max = 3f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? (float)(int)_input.textureQuality : 0f,
                        set  = v  => {
                            if (_input == null) return;
                            _input.textureQuality = (InputSettings.TextureQualityLevel)
                                Mathf.Clamp(Mathf.RoundToInt(v), 0, 3);
                            _input.ApplyGraphicsQuality();
                            MarkCustomQuality();
                            _input.SaveSettings();
                        },
                        formatFunc = vf => {
                            int idx = Mathf.Clamp(Mathf.RoundToInt(vf), 0, 3);
                            return idx == 0 ? "FULL" : idx == 1 ? "HALF" : idx == 2 ? "QUARTER" : "EIGHTH";
                        },
                    },
                    new SliderDef {
                        label = "SHADOW RESOLUTION",
                        restartHint = "Shadow map buffer allocation persists after downgrade.",
                        min = 0f, max = 3f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? (float)(int)_input.shadowResolution : 2f,
                        set  = v  => {
                            if (_input == null) return;
                            _input.shadowResolution = (InputSettings.ShadowResolutionLevel)
                                Mathf.Clamp(Mathf.RoundToInt(v), 0, 3);
                            _input.ApplyGraphicsQuality();
                            MarkCustomQuality();
                            _input.SaveSettings();
                        },
                        formatFunc = vf => {
                            int idx = Mathf.Clamp(Mathf.RoundToInt(vf), 0, 3);
                            return idx == 0 ? "LOW" : idx == 1 ? "MEDIUM" : idx == 2 ? "HIGH" : "VERY HIGH";
                        },
                    },
                    new SliderDef {
                        label = "SHADOW CASCADES", min = 0f, max = 2f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => {
                            if (_input == null) return 2f;
                            switch (_input.shadowCascades) {
                                case InputSettings.ShadowCascadeCount.Zero: return 0f;
                                case InputSettings.ShadowCascadeCount.Two:  return 1f;
                                case InputSettings.ShadowCascadeCount.Four: return 2f;
                                default: return 2f;
                            }
                        },
                        set  = v  => {
                            if (_input == null) return;
                            int idx = Mathf.Clamp(Mathf.RoundToInt(v), 0, 2);
                            _input.shadowCascades = idx == 0 ? InputSettings.ShadowCascadeCount.Zero
                                                  : idx == 1 ? InputSettings.ShadowCascadeCount.Two
                                                  :            InputSettings.ShadowCascadeCount.Four;
                            _input.ApplyGraphicsQuality();
                            MarkCustomQuality();
                            _input.SaveSettings();
                        },
                        formatFunc = vf => {
                            int idx = Mathf.Clamp(Mathf.RoundToInt(vf), 0, 2);
                            return idx == 0 ? "OFF" : idx == 1 ? "2 SPLIT" : "4 SPLIT";
                        },
                    },
                    new SliderDef {
                        label = "SHADOW DISTANCE", min = 20f, max = 500f, wholeNumbers = false, format = "{0:F0}m",
                        get  = () => _input != null ? _input.shadowDistance : 150f,
                        set  = v  => {
                            if (_input == null) return;
                            _input.shadowDistance = Mathf.Clamp(v, 20f, 500f);
                            _input.ApplyGraphicsQuality();
                            MarkCustomQuality();
                            _input.SaveSettings();
                        },
                    },
                    // Grass render distance. 0 = OFF (no grass), 1× = authored
                    // distance, up to 3× further. Read live by InstancedGrassRenderer
                    // each frame, so it applies instantly. Independent of the quality
                    // preset (not bundled), so it never snaps the preset to Custom.
                    new SliderDef {
                        label = "GRASS DISTANCE", min = 0f, max = 3f, wholeNumbers = false, format = "{0:F1}×",
                        get  = () => _input != null ? _input.grassRenderScale : 1f,
                        set  = v  => {
                            if (_input == null) return;
                            _input.grassRenderScale = Mathf.Clamp(v, 0f, 3f);
                            _input.SaveSettings();
                        },
                        formatFunc = vf => {
                            float s = Mathf.Clamp(vf, 0f, 3f);
                            return s <= 0.001f ? "OFF" : $"{s:F1}×";
                        },
                    },
                    new SliderDef {
                        label = "ANISOTROPIC FILTERING", min = 0f, max = 2f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? (float)(int)_input.anisotropicFiltering : 1f,
                        set  = v  => {
                            if (_input == null) return;
                            _input.anisotropicFiltering = (InputSettings.AnisotropicLevel)
                                Mathf.Clamp(Mathf.RoundToInt(v), 0, 2);
                            _input.ApplyGraphicsQuality();
                            MarkCustomQuality();
                            _input.SaveSettings();
                        },
                        formatFunc = vf => {
                            int idx = Mathf.Clamp(Mathf.RoundToInt(vf), 0, 2);
                            return idx == 0 ? "OFF" : idx == 1 ? "ON" : "FORCE ON";
                        },
                    },
                    new ToggleDef {
                        label = "CONCERT SHADOWS (GPU heavy)",
                        get  = () => _input != null && _input.fxConcertShadows,
                        set  = v  => { if (_input != null) { _input.fxConcertShadows = v; MarkCustomQuality(); } },
                    },
                    // Phone-camera RT resolution. Drives the per-frame cost
                    // of video recording (AsyncGPUReadback + JPEG encode +
                    // AVI write are all O(pixels)). Lower = pixelier but
                    // far cheaper. Tied to the quality preset bundle
                    // (Ultra→Full, High→3/4, Medium→1/2, Low→1/4); manually
                    // changing it snaps the preset to Custom and pushes the
                    // new size to PlayerPhoneUI live so an in-progress
                    // camera session picks it up without a restart.
                    new SliderDef {
                        label = "PHONE CAMERA RES", min = 0f, max = 4f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? (float)(int)_input.phoneResolutionScale : (float)(int)InputSettings.PhoneResolutionScale.Full,
                        set  = v  => {
                            if (_input == null) return;
                            var s = (InputSettings.PhoneResolutionScale)Mathf.Clamp(Mathf.RoundToInt(v), 0, 4);
                            _input.phoneResolutionScale = s;
                            MarkCustomQuality();
                            _input.SaveSettings();
                            if (PlayerPhoneUI.Instance != null) PlayerPhoneUI.Instance.OnPhoneResolutionChanged();
                        },
                        formatFunc = vf => {
                            var s = (InputSettings.PhoneResolutionScale)Mathf.Clamp(Mathf.RoundToInt(vf), 0, 4);
                            switch (s) {
                                case InputSettings.PhoneResolutionScale.Eighth:       return "1/8 (potato)";
                                case InputSettings.PhoneResolutionScale.Quarter:      return "1/4 (pixelated)";
                                case InputSettings.PhoneResolutionScale.Half:         return "1/2";
                                case InputSettings.PhoneResolutionScale.ThreeQuarter: return "3/4";
                                case InputSettings.PhoneResolutionScale.Full:         return "FULL";
                                default:                                              return s.ToString().ToUpper();
                            }
                        },
                    },
                    // Physics tick rate. Independent of the quality preset
                    // (different axis — CPU cost vs precision). Slider-as-
                    // dropdown 0..2 with formatFunc showing the enum name +
                    // the resulting Hz so the user knows what they're picking.
                    new SliderDef {
                        label = "PHYSICS RATE", min = 0f, max = 4f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? (float)(int)_input.physicsRate : (float)(int)InputSettings.PhysicsRate.Ultra,
                        set  = v  => {
                            if (_input == null) return;
                            var rate = (InputSettings.PhysicsRate)Mathf.Clamp(Mathf.RoundToInt(v), 0, 4);
                            _input.ApplyPhysicsRate(rate);
                            _input.SaveSettings();
                        },
                        formatFunc = vf => {
                            var p = (InputSettings.PhysicsRate)Mathf.Clamp(Mathf.RoundToInt(vf), 0, 4);
                            switch (p) {
                                case InputSettings.PhysicsRate.Low:      return "LOW (40 Hz)";
                                case InputSettings.PhysicsRate.Balanced: return "BALANCED (50 Hz)";
                                case InputSettings.PhysicsRate.Ultra:    return "ULTRA (100 Hz)";
                                case InputSettings.PhysicsRate.Max:      return "MAX (144 Hz)";
                                case InputSettings.PhysicsRate.Insane:   return "INSANE (240 Hz)";
                                default: return p.ToString().ToUpper();
                            }
                        },
                    },

                    // ── Streaming / world ─────────────────────────────
                    // These five sliders ARE the spawn-amount knobs the player
                    // wanted available in Custom mode. Editing any of them
                    // snaps qualityPreset back to Custom via MarkCustomQuality
                    // so the preset slider above doesn't lie about state.
                    new HeaderDef { label = "WORLD" },
                    new SliderDef {
                        label = "VIEW DISTANCE", min = 100f, max = 1000f, wholeNumbers = false, format = "{0:F0}m",
                        get  = () => _input != null ? _input.viewDistance : 350f,
                        set  = v  => { if (_input != null) { _input.viewDistance = Mathf.Clamp(v, 100f, 1000f); MarkCustomQuality(); } },
                    },
                    new SliderDef {
                        label = "MAX TREES", min = 20f, max = 100f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? _input.maxTrees : 20f,
                        set  = v  => { if (_input != null) { _input.maxTrees = Mathf.RoundToInt(v); MarkCustomQuality(); } },
                    },
                    new SliderDef {
                        label = "MAX ALIEN NPCS", min = 5f, max = 20f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? _input.maxAlienNPCs : 10f,
                        set  = v  => { if (_input != null) { _input.maxAlienNPCs = Mathf.RoundToInt(v); MarkCustomQuality(); } },
                    },
                    new SliderDef {
                        label = "MAX MUSHROOMS", min = 0f, max = 100f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? _input.maxMushrooms : 20f,
                        set  = v  => { if (_input != null) { _input.maxMushrooms = Mathf.RoundToInt(v); MarkCustomQuality(); } },
                    },
                    new SliderDef {
                        label = "MAX CRYSTALS", min = 0f, max = 60f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? _input.maxCrystals : 20f,
                        set  = v  => { if (_input != null) { _input.maxCrystals = Mathf.RoundToInt(v); MarkCustomQuality(); } },
                    },
                    new SliderDef {
                        label = "MAX AUDIENCE", min = 10f, max = 40f, wholeNumbers = true, format = "{0:F0}",
                        get  = () => _input != null ? _input.maxAudienceSize : 25f,
                        set  = v  => { if (_input != null) { _input.maxAudienceSize = Mathf.RoundToInt(v); MarkCustomQuality(); } },
                    },

                    // ── AI / VRAM ─────────────────────────────────────
                    // Phone AI gates: when off, the language model never
                    // loads — frees ~6 GB VRAM (Hermes-8B Q4_K_M with full
                    // GPU offload). When on, the model loads lazily on
                    // AI chat open and unloads on close, so VRAM is only
                    // committed during chat. Live-toggleable; flipping
                    // false while the model is loaded unloads it now.
                    new HeaderDef { label = "AI" },
                    new ToggleDef {
                        label = "AI ENABLED (frees ~6 GB VRAM when off)",
                        get  = () => _input != null && _input.aiEnabled,
                        set  = v  => {
                            if (_input == null) return;
                            _input.aiEnabled = v;
                            // Immediate unload if disabling and a model is
                            // currently resident — don't wait for save.
                            if (!v && LLMService.Instance != null) LLMService.Instance.UnloadModel();
                        },
                    },
                },
            },
        };
    }

    // ── Display resolution slider helpers ────────────────────────────

    void BuildResolutionCache()
    {
        // Unique (w, h) pairs from Screen.resolutions. The platform-reported
        // list often contains multiple entries per resolution (one per refresh
        // rate); deduping keeps the slider's step count sane.
        var all = Screen.resolutions;
        var seen = new HashSet<long>();
        var list = new List<Vector2Int>();
        for (int i = 0; i < all.Length; i++)
        {
            long key = ((long)all[i].width << 32) | (uint)all[i].height;
            if (!seen.Add(key)) continue;
            list.Add(new Vector2Int(all[i].width, all[i].height));
        }
        // Sort ascending by area so the slider goes small→large left→right.
        list.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
        _resCache = list.ToArray();
        // Belt-and-braces: every platform returns at least one resolution, but
        // if it didn't we'd want SOMETHING in there so the slider doesn't NaN.
        if (_resCache.Length == 0)
            _resCache = new[] { new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height)) };
    }

    int CurrentResolutionIndex()
    {
        if (_resCache == null || _resCache.Length == 0) return 0;
        int w = _input != null && _input.displayWidth > 0 ? _input.displayWidth : Screen.width;
        int h = _input != null && _input.displayHeight > 0 ? _input.displayHeight : Screen.height;
        // Exact match first; then closest by absolute area difference. Closest
        // is a safety net for cases where the saved (w, h) isn't in
        // Screen.resolutions on this machine (e.g. moved between monitors).
        int bestIdx = 0;
        long bestErr = long.MaxValue;
        for (int i = 0; i < _resCache.Length; i++)
        {
            if (_resCache[i].x == w && _resCache[i].y == h) return i;
            long err = System.Math.Abs((long)_resCache[i].x * _resCache[i].y - (long)w * h);
            if (err < bestErr) { bestErr = err; bestIdx = i; }
        }
        return bestIdx;
    }

    float CurrentResolutionIndexAsFloat() => CurrentResolutionIndex();

    void SetResolutionFromIndexFloat(float vf)
    {
        if (_input == null || _resCache == null || _resCache.Length == 0) return;
        int idx = Mathf.Clamp(Mathf.RoundToInt(vf), 0, _resCache.Length - 1);
        var r = _resCache[idx];
        _input.displayWidth = r.x;
        _input.displayHeight = r.y;
        _input.ApplyDisplaySettings();
        _input.SaveSettings();
    }

    string FormatResolutionIndex(float vf)
    {
        if (_resCache == null || _resCache.Length == 0) return "—";
        int idx = Mathf.Clamp(Mathf.RoundToInt(vf), 0, _resCache.Length - 1);
        var r = _resCache[idx];
        return $"{r.x}×{r.y}";
    }

    void BuildSettingsPanel()
    {
        var panelRT = NewUI("SettingsPanel", _cardRT);
        StretchWithMargins(panelRT, 50f, 140f, 50f, 80f);
        _settingsPanel = panelRT.gameObject;
        _settingsPanel.SetActive(false);

        // Tab row (horizontal, underlined active — Style A).
        var tabRowRT = NewUI("Tabs", panelRT);
        tabRowRT.anchorMin = new Vector2(0f, 1f);
        tabRowRT.anchorMax = new Vector2(1f, 1f);
        tabRowRT.pivot = new Vector2(0.5f, 1f);
        tabRowRT.anchoredPosition = Vector2.zero;
        tabRowRT.sizeDelta = new Vector2(0f, 40f);

        var tabHL = tabRowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabHL.childAlignment = TextAnchor.MiddleCenter;
        tabHL.spacing = 24f;
        tabHL.childControlWidth = true;
        tabHL.childControlHeight = true;
        tabHL.childForceExpandWidth = false;
        tabHL.childForceExpandHeight = false;

        var divider = NewUI("Divider", panelRT);
        divider.anchorMin = new Vector2(0f, 1f);
        divider.anchorMax = new Vector2(1f, 1f);
        divider.pivot = new Vector2(0.5f, 1f);
        divider.anchoredPosition = new Vector2(0f, -40f);
        divider.sizeDelta = new Vector2(0f, 1f);
        var divImg = divider.gameObject.AddComponent<Image>();
        divImg.color = ButtonBorder;
        divImg.raycastTarget = false;

        _tabButtons = new List<Button>();
        _tabButtonLabels = new List<TextMeshProUGUI>();
        _tabContentPanels = new List<GameObject>();
        for (int i = 0; i < _tabs.Count; i++)
        {
            int tabIndex = i; // capture for lambda
            BuildTabButton(tabRowRT, _tabs[i].name, () => SwitchTab(tabIndex));
        }

        // Scrollable content area for the active tab's settings.
        var scrollRT = NewUI("ScrollView", panelRT);
        scrollRT.anchorMin = new Vector2(0f, 0f);
        scrollRT.anchorMax = new Vector2(1f, 1f);
        scrollRT.pivot = new Vector2(0.5f, 0.5f);
        // Bottom margin sized for BOTH stacked buttons: Save and Apply (y=12,
        // h=40) on bottom + BACK (y=56, h=40) on top = ~96 px, plus a small
        // 16 px gap so slider rows don't visually touch the top of BACK.
        // Was 64 (back-only era) — at that value, BACK clipped into the
        // bottom 32 px of slider content.
        scrollRT.offsetMin = new Vector2(0f, 112f);
        scrollRT.offsetMax = new Vector2(0f, -50f); // below tab row + divider

        var scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        // Viewport (clips content to scroll bounds).
        var viewportRT = NewUI("Viewport", scrollRT);
        Stretch(viewportRT);
        var viewportImg = viewportRT.gameObject.AddComponent<Image>();
        viewportImg.color = new Color(0f, 0f, 0f, 0f);
        viewportImg.raycastTarget = true;
        viewportRT.gameObject.AddComponent<RectMask2D>();
        scroll.viewport = viewportRT;

        // Content (vertical stack of settings rows; ContentSizeFitter grows as
        // we add rows so the scroll bar appears automatically when needed).
        var contentRT = NewUI("Content", viewportRT);
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0f, 0f);
        scroll.content = contentRT;

        // Per-tab content panels live inside the scroll Content. Only the
        // active tab's panel is enabled at any moment; SwitchTab toggles them.
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tabPanelRT = NewUI(_tabs[i].name + "Panel", contentRT);
            tabPanelRT.anchorMin = new Vector2(0f, 1f);
            tabPanelRT.anchorMax = new Vector2(1f, 1f);
            tabPanelRT.pivot = new Vector2(0.5f, 1f);
            tabPanelRT.anchoredPosition = Vector2.zero;
            tabPanelRT.sizeDelta = new Vector2(0f, 0f);

            var tabVL = tabPanelRT.gameObject.AddComponent<VerticalLayoutGroup>();
            tabVL.childAlignment = TextAnchor.UpperCenter;
            tabVL.spacing = 8f;
            tabVL.padding = new RectOffset(20, 20, 12, 12);
            tabVL.childControlWidth = true;
            // childControlHeight must be true; otherwise rows stay at the
            // default 100 px sizeDelta and end up ~3× their intended height.
            tabVL.childControlHeight = true;
            tabVL.childForceExpandWidth = true;
            tabVL.childForceExpandHeight = false;
            var tabFit = tabPanelRT.gameObject.AddComponent<ContentSizeFitter>();
            tabFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var row in _tabs[i].rows)
            {
                switch (row)
                {
                    case SliderDef s: BuildSettingRow(tabPanelRT, s); break;
                    case ToggleDef t: BuildToggleRow(tabPanelRT, t); break;
                    case HeaderDef h: BuildHeaderRow(tabPanelRT, h); break;
                }
            }

            _tabContentPanels.Add(tabPanelRT.gameObject);
        }

        // Make Content size itself off its children, so the scroll knows how
        // tall the active tab is. Switching tabs swaps which child is active.
        var contentFit = contentRT.gameObject.AddComponent<ContentSizeFitter>();
        contentFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var contentVL = contentRT.gameObject.AddComponent<VerticalLayoutGroup>();
        contentVL.childAlignment = TextAnchor.UpperCenter;
        contentVL.spacing = 0f;
        contentVL.padding = new RectOffset(0, 0, 0, 0);
        contentVL.childControlWidth = true;
        contentVL.childControlHeight = false;
        contentVL.childForceExpandWidth = true;
        contentVL.childForceExpandHeight = false;

        // BACK button — sits ABOVE the Save and Apply button (when shown).
        // Bumped up from y=12 to y=56 so the Save and Apply button can take
        // the bottom slot below it.
        var backRT = NewUI("BackButton", panelRT);
        backRT.anchorMin = new Vector2(0.5f, 0f);
        backRT.anchorMax = new Vector2(0.5f, 0f);
        backRT.pivot = new Vector2(0.5f, 0f);
        backRT.anchoredPosition = new Vector2(0f, 56f);
        backRT.sizeDelta = new Vector2(180f, 40f);
        BuildMenuButtonAt(backRT, "◂ BACK", ShowMainPanel, primary: false);

        // SAVE AND APPLY — sits BELOW BACK at the bottom of the panel; only
        // visible when the user has pending changes (HasPendingChanges
        // drives .gameObject.active from Update). Click commits the staged
        // InputSettings to Unity's QualitySettings / Screen / PlayerPrefs
        // and, if any ★ setting changed, prompts to restart for full effect.
        _saveAndApplyBtnRT = NewUI("SaveAndApplyButton", panelRT);
        _saveAndApplyBtnRT.anchorMin = new Vector2(0.5f, 0f);
        _saveAndApplyBtnRT.anchorMax = new Vector2(0.5f, 0f);
        _saveAndApplyBtnRT.pivot = new Vector2(0.5f, 0f);
        _saveAndApplyBtnRT.anchoredPosition = new Vector2(0f, 12f);
        _saveAndApplyBtnRT.sizeDelta = new Vector2(220f, 40f);
        BuildMenuButtonAt(_saveAndApplyBtnRT, "SAVE AND APPLY", OnSaveAndApplyClicked, primary: true);
        _saveAndApplyBtnRT.gameObject.SetActive(false);

        SwitchTab(0);
    }

    void OnSaveAndApplyClicked()
    {
        bool needsRestart = SaveAndApplyReturnsRestartNeeded();
        if (needsRestart) ShowRestartPrompt();
        else InvokePendingExitOnceAndClear();
    }

    // Refactor: SaveAndApply (above) does the apply + showRestartPrompt
    // inline; OnSaveAndApplyClicked needs to know whether to await the
    // restart prompt or fire the pending exit immediately. This variant
    // computes restart-needed BEFORE re-snapshotting (which would clear it)
    // and skips the inline ShowRestartPrompt call.
    bool SaveAndApplyReturnsRestartNeeded()
    {
        if (_input == null) return false;
        bool needsRestart = PendingChangesRequireRestart();
        _input.deferApply = false;
        _input.ApplyPhysicsRate(_input.physicsRate);
        _input.ApplyGraphicsQuality();
        _input.ApplyDisplaySettings();
        _input.SaveSettings();
        if (PlayerPhoneUI.Instance != null) PlayerPhoneUI.Instance.OnPhoneResolutionChanged();
        CaptureSettingsSnapshot();
        _input.deferApply = true;
        if (_saveAndApplyBtnRT != null) _saveAndApplyBtnRT.gameObject.SetActive(false);
        return needsRestart;
    }

    void InvokePendingExitOnceAndClear()
    {
        var a = _pendingExitAction;
        _pendingExitAction = null;
        a?.Invoke();
    }

    void ShowSavePrompt()
    {
        if (_savePromptRoot == null) _savePromptRoot = BuildModalPrompt(
            "SaveChangesPrompt",
            "Do you want to save your changes?",
            "YES",  OnSavePromptYes,
            "NO",   OnSavePromptNo);
        _savePromptRoot.transform.SetAsLastSibling();
        _savePromptRoot.SetActive(true);
    }

    void HideSavePrompt() { if (_savePromptRoot != null) _savePromptRoot.SetActive(false); }

    void OnSavePromptYes()
    {
        HideSavePrompt();
        bool needsRestart = SaveAndApplyReturnsRestartNeeded();
        if (needsRestart) ShowRestartPrompt();
        else InvokePendingExitOnceAndClear();
    }

    void OnSavePromptNo()
    {
        HideSavePrompt();
        RevertChanges();
        InvokePendingExitOnceAndClear();
    }

    void ShowRestartPrompt()
    {
        if (_restartPromptRoot == null) _restartPromptRoot = BuildModalPrompt(
            "RestartPrompt",
            "You have changed settings that require restart for best FPS.",
            "RESTART", OnRestartPromptRestart,
            "NO",      OnRestartPromptNo);
        _restartPromptRoot.transform.SetAsLastSibling();
        _restartPromptRoot.SetActive(true);
    }

    void HideRestartPrompt() { if (_restartPromptRoot != null) _restartPromptRoot.SetActive(false); }

    void OnRestartPromptRestart()
    {
        HideRestartPrompt();
        // Make sure settings are persisted before we relaunch.
        if (_input != null) { _input.deferApply = false; _input.SaveSettings(); }
        Time.timeScale = 1f;
#if !UNITY_EDITOR
        // Windows-only relaunch: dataPath is "<exepath>_Data"; replacing the
        // suffix yields the .exe. Process.Start launches a fresh instance;
        // Application.Quit closes this one. In-editor this is a no-op.
        try
        {
            string exePath = UnityEngine.Application.dataPath.Replace("_Data", ".exe");
            System.Diagnostics.Process.Start(exePath);
        }
        catch (System.Exception e) { UnityEngine.Debug.LogWarning("[TabbedPauseMenu] Restart failed: " + e.Message); }
        UnityEngine.Application.Quit();
#else
        UnityEngine.Debug.Log("[TabbedPauseMenu] Restart requested (Editor: no-op).");
#endif
    }

    void OnRestartPromptNo()
    {
        HideRestartPrompt();
        InvokePendingExitOnceAndClear();
    }

    // Generic two-button modal popup. Backdrop dims the screen; a centered
    // card holds the prompt text and two buttons. Built once per prompt,
    // shown / hidden via SetActive.
    GameObject BuildModalPrompt(string name, string promptText,
        string btnAText, Action onA, string btnBText, Action onB)
    {
        var root = new GameObject(name);
        root.transform.SetParent(_canvas.transform, false);
        var rootRT = root.AddComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // Backdrop — full-screen semi-transparent black, blocks raycasts.
        var bdRT = NewUI("Backdrop", rootRT);
        bdRT.anchorMin = Vector2.zero;
        bdRT.anchorMax = Vector2.one;
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;
        var bdImg = bdRT.gameObject.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.65f);
        bdImg.raycastTarget = true;

        // Card.
        var cardRT = NewUI("Card", rootRT);
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        cardRT.sizeDelta = new Vector2(520f, 200f);
        var cardImg = cardRT.gameObject.AddComponent<Image>();
        cardImg.color = CardBgTop;
        cardImg.raycastTarget = true;

        // Card border (thin outline).
        var brdRT = NewUI("Border", cardRT);
        brdRT.anchorMin = Vector2.zero;
        brdRT.anchorMax = Vector2.one;
        brdRT.offsetMin = new Vector2(-2f, -2f);
        brdRT.offsetMax = new Vector2(2f, 2f);
        var brdImg = brdRT.gameObject.AddComponent<Image>();
        brdImg.color = new Color(CardBorderCool.r, CardBorderCool.g, CardBorderCool.b, 0.85f);
        brdImg.raycastTarget = false;
        // Send border behind the card body.
        brdRT.SetAsFirstSibling();

        // Prompt text — fills the top half of the card.
        var txt = NewText(cardRT, "Text", promptText, 16f, FontStyles.Bold, LabelColor);
        var txtRT = txt.rectTransform;
        txtRT.anchorMin = new Vector2(0f, 0f);
        txtRT.anchorMax = new Vector2(1f, 1f);
        txtRT.offsetMin = new Vector2(24f, 70f);
        txtRT.offsetMax = new Vector2(-24f, -24f);
        txt.alignment = TextAlignmentOptions.Center;
        txt.enableWordWrapping = true;
        txt.raycastTarget = false;

        // Two buttons across the bottom.
        const float BtnW = 180f, BtnH = 40f, BtnSpacing = 24f;
        float totalW = BtnW * 2 + BtnSpacing;
        float startX = -totalW * 0.5f + BtnW * 0.5f;

        var btnA = NewUI("BtnA", cardRT);
        btnA.anchorMin = new Vector2(0.5f, 0f);
        btnA.anchorMax = new Vector2(0.5f, 0f);
        btnA.pivot = new Vector2(0.5f, 0f);
        btnA.anchoredPosition = new Vector2(startX, 16f);
        btnA.sizeDelta = new Vector2(BtnW, BtnH);
        BuildMenuButtonAt(btnA, btnAText, onA, primary: true);

        var btnB = NewUI("BtnB", cardRT);
        btnB.anchorMin = new Vector2(0.5f, 0f);
        btnB.anchorMax = new Vector2(0.5f, 0f);
        btnB.pivot = new Vector2(0.5f, 0f);
        btnB.anchoredPosition = new Vector2(startX + BtnW + BtnSpacing, 16f);
        btnB.sizeDelta = new Vector2(BtnW, BtnH);
        BuildMenuButtonAt(btnB, btnBText, onB, primary: false);

        root.SetActive(false);
        return root;
    }

    void BuildTabButton(RectTransform parent, string label, Action onClick)
    {
        var btnRT = NewUI(label, parent);
        var btnLE = btnRT.gameObject.AddComponent<LayoutElement>();
        btnLE.preferredWidth = 180f;
        btnLE.preferredHeight = 40f;

        // Underline (visible when active).
        var underline = NewUI("Underline", btnRT);
        underline.anchorMin = new Vector2(0f, 0f);
        underline.anchorMax = new Vector2(1f, 0f);
        underline.pivot = new Vector2(0.5f, 0f);
        underline.anchoredPosition = Vector2.zero;
        underline.sizeDelta = new Vector2(0f, 2f);
        var underlineImg = underline.gameObject.AddComponent<Image>();
        underlineImg.color = CardBorderCool;
        underlineImg.raycastTarget = false;
        underlineImg.enabled = false; // toggled in SwitchTab

        var lbl = NewText(btnRT, "Label", label, 14f, FontStyles.Bold, LabelDim);
        Stretch(lbl.rectTransform);
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.characterSpacing = 6f;
        lbl.raycastTarget = false;

        var btn = btnRT.gameObject.AddComponent<Button>();
        var img = btnRT.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());
        UiSfxPlayer.Attach(btn);

        _tabButtons.Add(btn);
        _tabButtonLabels.Add(lbl);
        // Tag the underline on the button's transform for SwitchTab to find.
        btnRT.name = "TabBtn_" + label;
    }

    void SwitchTab(int newIndex)
    {
        if (_tabContentPanels == null) return;
        _activeTab = Mathf.Clamp(newIndex, 0, _tabContentPanels.Count - 1);
        for (int i = 0; i < _tabContentPanels.Count; i++)
            _tabContentPanels[i].SetActive(i == _activeTab);
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool isActive = i == _activeTab;
            // Underline + label tint.
            var underline = _tabButtons[i].transform.Find("Underline");
            if (underline != null)
            {
                var img = underline.GetComponent<Image>();
                if (img != null) img.enabled = isActive;
            }
            if (i < _tabButtonLabels.Count && _tabButtonLabels[i] != null)
                _tabButtonLabels[i].color = isActive ? CardBorderCool : LabelDim;
        }
        // Refresh slider values on tab switch in case anything changed under
        // us (e.g. AudioListener.volume slider also drives masterVolume).
        RefreshAllRows();
        // Tab switch changes which content panel is active — different tabs
        // have different content heights, so the scroll content needs to
        // re-lay-out for the ScrollRect to know its new bounds.
        RebuildScrollLayout();
    }

    void BuildMenuButtonAt(RectTransform btnRT, string label, Action onClick, bool primary)
    {
        var bg = btnRT.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = ButtonBg;
        bg.raycastTarget = true;

        var border = NewUI("Border", btnRT);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = primary ? ButtonBorderHi : ButtonBorder;
        borderImg.raycastTarget = false;

        var lbl = NewText(btnRT, "Label", label, 14f, FontStyles.Bold, LabelColor);
        Stretch(lbl.rectTransform);
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.characterSpacing = 4f;
        lbl.raycastTarget = false;

        var btn = btnRT.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = ButtonBg;
        colors.highlightedColor = ButtonBgHover;
        colors.pressedColor = ButtonBgHover;
        colors.selectedColor = ButtonBgHover;
        colors.disabledColor = ButtonBg;
        btn.colors = colors;
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick?.Invoke());
        UiSfxPlayer.Attach(btn);

        var hover = btnRT.gameObject.AddComponent<ButtonHoverTint>();
        hover.Init(borderImg, lbl, primary ? ButtonBorderHi : ButtonBorder, CardBorderCool, LabelColor, CardBorderCool);
    }

    // ── Setting row widget ───────────────────────────────────────────

    class SettingRowRefs
    {
        public Slider slider;
        public TextMeshProUGUI valueText;
        public SliderDef def;
    }
    List<SettingRowRefs> _rows = new List<SettingRowRefs>();

    void BuildSettingRow(RectTransform parent, SliderDef def)
    {
        var rowRT = NewUI(def.label + "Row", parent);
        var rowLE = rowRT.gameObject.AddComponent<LayoutElement>();
        rowLE.preferredHeight = SettingsRowHeight;
        rowLE.flexibleHeight = 0f;

        var rowHL = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHL.childAlignment = TextAnchor.MiddleLeft;
        rowHL.spacing = 14f;
        rowHL.childControlWidth = true;
        rowHL.childControlHeight = true;
        rowHL.childForceExpandWidth = false;
        rowHL.childForceExpandHeight = false;

        // Label — append a small orange ★ when restartHint is set. Player
        // sees the marker on heavy settings (MSAA, texture quality, shadow
        // res); the meaning is explained by the header note above the
        // QUALITY block.
        string labelText = string.IsNullOrEmpty(def.restartHint)
            ? def.label
            : def.label + " <size=10><color=#FFB060>★</color></size>";
        var lbl = NewText(rowRT, "Label", labelText, 12f, FontStyles.Bold, HeaderColor);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 3f;
        var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
        lblLE.preferredWidth = 200f;
        lblLE.preferredHeight = 20f;
        lblLE.flexibleWidth = 0f;
        lbl.raycastTarget = false;

        // Slider
        var sliderRT = NewUI("Slider", rowRT);
        var sliderLE = sliderRT.gameObject.AddComponent<LayoutElement>();
        sliderLE.preferredHeight = 16f;
        sliderLE.flexibleWidth = 1f;

        // Background track.
        var trackBg = sliderRT.gameObject.AddComponent<Image>();
        trackBg.color = SliderTrackBg;
        trackBg.raycastTarget = true;

        // Fill area (Unity Slider needs a Fill Area > Fill structure).
        var fillArea = NewUI("FillArea", sliderRT);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.pivot = new Vector2(0.5f, 0.5f);
        fillArea.sizeDelta = new Vector2(-16f, 10f);
        fillArea.anchoredPosition = new Vector2(0f, 0f);

        var fill = NewUI("Fill", fillArea);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.pivot = new Vector2(0.5f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = SliderFillA;
        fillImg.raycastTarget = false;

        // Handle area + handle.
        var handleArea = NewUI("HandleArea", sliderRT);
        handleArea.anchorMin = new Vector2(0f, 0f);
        handleArea.anchorMax = new Vector2(1f, 1f);
        handleArea.pivot = new Vector2(0.5f, 0.5f);
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);

        var handle = NewUI("Handle", handleArea);
        handle.anchorMin = new Vector2(0f, 0.5f);
        handle.anchorMax = new Vector2(0f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(14f, 14f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = GetCircleSprite();
        handleImg.color = CardBorderCool;
        handleImg.raycastTarget = true;
        // Soft glow around the handle.
        var handleGlow = handle.gameObject.AddComponent<Shadow>();
        handleGlow.effectColor = new Color(CardBorderCool.r, CardBorderCool.g, CardBorderCool.b, 0.6f);
        handleGlow.effectDistance = Vector2.zero;

        var slider = sliderRT.gameObject.AddComponent<Slider>();
        slider.minValue = def.min;
        slider.maxValue = def.max;
        slider.wholeNumbers = def.wholeNumbers;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        // Value text.
        var valRT = NewUI("Value", rowRT);
        var valLE = valRT.gameObject.AddComponent<LayoutElement>();
        valLE.preferredWidth = 70f;
        valLE.preferredHeight = 20f;
        valLE.flexibleWidth = 0f;
        var valText = valRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(valText);
        valText.fontSize = 13f;
        valText.fontStyle = FontStyles.Bold;
        valText.color = SliderFillA;
        valText.alignment = TextAlignmentOptions.MidlineRight;
        valText.raycastTarget = false;

        var refs = new SettingRowRefs { slider = slider, valueText = valText, def = def };
        _rows.Add(refs);

        slider.onValueChanged.AddListener(v =>
        {
            def.set?.Invoke(v);
            valText.text = def.formatFunc != null ? def.formatFunc(v) : string.Format(def.format, v);
        });

        // Initial state.
        float current = def.get != null ? def.get() : 0f;
        slider.SetValueWithoutNotify(current);
        valText.text = def.formatFunc != null ? def.formatFunc(current) : string.Format(def.format, current);
    }

    void RefreshAllRows()
    {
        if (_rows != null)
        {
            foreach (var r in _rows)
            {
                if (r == null || r.slider == null || r.def == null || r.def.get == null) continue;
                float current = r.def.get();
                r.slider.SetValueWithoutNotify(current);
                if (r.valueText != null)
                    r.valueText.text = r.def.formatFunc != null ? r.def.formatFunc(current) : string.Format(r.def.format, current);
            }
        }
        if (_toggleRows != null)
            foreach (var t in _toggleRows) RefreshToggle(t);
    }

    // ── Toggle + Header row widgets ──────────────────────────────────

    class SettingRowRefs_Toggle
    {
        public Image bg;
        public TextMeshProUGUI valueText;
        public ToggleDef def;
    }
    List<SettingRowRefs_Toggle> _toggleRows = new List<SettingRowRefs_Toggle>();

    void BuildToggleRow(RectTransform parent, ToggleDef def)
    {
        var rowRT = NewUI(def.label + "Row", parent);
        var rowLE = rowRT.gameObject.AddComponent<LayoutElement>();
        rowLE.preferredHeight = SettingsRowHeight;
        rowLE.flexibleHeight = 0f;

        var rowHL = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHL.childAlignment = TextAnchor.MiddleLeft;
        rowHL.spacing = 14f;
        rowHL.childControlWidth = true;
        rowHL.childControlHeight = true;
        rowHL.childForceExpandWidth = false;
        rowHL.childForceExpandHeight = false;

        var lbl = NewText(rowRT, "Label", def.label, 12f, FontStyles.Bold, HeaderColor);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 3f;
        var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
        lblLE.preferredWidth = 260f;
        lblLE.preferredHeight = 20f;
        lblLE.flexibleWidth = 0f;
        lbl.raycastTarget = false;

        var spacer = NewUI("Spacer", rowRT);
        var spLE = spacer.gameObject.AddComponent<LayoutElement>();
        spLE.flexibleWidth = 1f;

        var toggleRT = NewUI("Toggle", rowRT);
        var tLE = toggleRT.gameObject.AddComponent<LayoutElement>();
        tLE.preferredWidth = 70f;
        tLE.preferredHeight = 22f;
        tLE.flexibleWidth = 0f;

        var bg = toggleRT.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = ButtonBg;
        bg.raycastTarget = true;

        var border = NewUI("Border", toggleRT);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = ButtonBorder;
        borderImg.raycastTarget = false;

        var stateText = NewText(toggleRT, "State", "ON", 11f, FontStyles.Bold, CardBorderCool);
        Stretch(stateText.rectTransform);
        stateText.alignment = TextAlignmentOptions.Center;
        stateText.characterSpacing = 2f;
        stateText.raycastTarget = false;

        var btn = toggleRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        var refs = new SettingRowRefs_Toggle { bg = bg, valueText = stateText, def = def };
        _toggleRows.Add(refs);
        UiSfxPlayer.Attach(btn);

        btn.onClick.AddListener(() =>
        {
            if (def.get == null || def.set == null) return;
            bool newVal = !def.get();
            def.set(newVal);
            RefreshToggle(refs);
        });

        RefreshToggle(refs);
    }

    void RefreshToggle(SettingRowRefs_Toggle refs)
    {
        if (refs == null || refs.def == null || refs.def.get == null) return;
        bool on = refs.def.get();
        refs.valueText.text = on ? "ON" : "OFF";
        refs.valueText.color = on ? CardBorderCool : LabelDim;
        refs.bg.color = on ? new Color32(0x14, 0x40, 0x60, 0xCC) : ButtonBg;
    }

    void BuildHeaderRow(RectTransform parent, HeaderDef def)
    {
        var rowRT = NewUI("// " + def.label, parent);
        var rowLE = rowRT.gameObject.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 26f;
        rowLE.flexibleHeight = 0f;
        var lbl = NewText(rowRT, "Label", "// " + def.label.ToUpperInvariant(), 10f, FontStyles.Bold, HeaderColor);
        Stretch(lbl.rectTransform);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 4f;
        lbl.margin = new Vector4(0f, 8f, 0f, 0f);
        lbl.raycastTarget = false;
    }

    // ── Panel swap + nav actions ─────────────────────────────────────

    // ── Staged settings (pending changes) ────────────────────────────
    //
    // While the settings panel is open, _input.deferApply = true so all
    // slider/toggle setters still mutate the InputSettings field bundle
    // (live-polled by spawners + camera FX for preview) but the Apply*
    // and SaveSettings calls become no-ops. The user must click "SAVE AND
    // APPLY" to commit the changes to Unity's QualitySettings / Screen /
    // PlayerPrefs. Clicking BACK or RESUME with pending changes prompts.
    //
    // _snapshot captures every row's .get() value on settings panel open.
    // HasPendingChanges() compares current to snapshot. The Save button's
    // visibility is driven by that comparison every frame.
    readonly Dictionary<RowDef, float>  _snapshotFloat = new Dictionary<RowDef, float>();
    readonly Dictionary<RowDef, bool>   _snapshotBool  = new Dictionary<RowDef, bool>();
    RectTransform _saveAndApplyBtnRT;        // shown below BACK when pending
    GameObject    _savePromptRoot;           // "Save your changes?" Yes/No
    GameObject    _restartPromptRoot;        // "Restart for best FPS" Restart/No
    Action        _pendingExitAction;        // continuation passed to RequestExitSettings

    void CaptureSettingsSnapshot()
    {
        _snapshotFloat.Clear();
        _snapshotBool.Clear();
        if (_rows != null)
            foreach (var r in _rows)
                if (r != null && r.def != null && r.def.get != null)
                    _snapshotFloat[r.def] = r.def.get();
        if (_toggleRows != null)
            foreach (var t in _toggleRows)
                if (t != null && t.def != null && t.def.get != null)
                    _snapshotBool[t.def] = t.def.get();
    }

    bool HasPendingChanges()
    {
        if (_rows != null)
            foreach (var r in _rows)
            {
                if (r == null || r.def == null || r.def.get == null) continue;
                if (!_snapshotFloat.TryGetValue(r.def, out var snap)) continue;
                if (!Mathf.Approximately(r.def.get(), snap)) return true;
            }
        if (_toggleRows != null)
            foreach (var t in _toggleRows)
            {
                if (t == null || t.def == null || t.def.get == null) continue;
                if (!_snapshotBool.TryGetValue(t.def, out var snap)) continue;
                if (t.def.get() != snap) return true;
            }
        return false;
    }

    // True if any pending change is on a row whose def has a restartHint.
    // Drives the "do you want to restart?" popup after Save and Apply.
    bool PendingChangesRequireRestart()
    {
        if (_rows != null)
            foreach (var r in _rows)
            {
                if (r == null || r.def == null || r.def.get == null) continue;
                if (string.IsNullOrEmpty(r.def.restartHint)) continue;
                if (!_snapshotFloat.TryGetValue(r.def, out var snap)) continue;
                if (!Mathf.Approximately(r.def.get(), snap)) return true;
            }
        if (_toggleRows != null)
            foreach (var t in _toggleRows)
            {
                if (t == null || t.def == null || t.def.get == null) continue;
                if (string.IsNullOrEmpty(t.def.restartHint)) continue;
                if (!_snapshotBool.TryGetValue(t.def, out var snap)) continue;
                if (t.def.get() != snap) return true;
            }
        return false;
    }

    // Restore InputSettings fields to the snapshot taken on menu open. The
    // setter lambdas write the field + call Apply* / SaveSettings — those
    // Apply / Save calls no-op while deferApply is true, so the revert is
    // field-only. Live-polling systems (spawners, camera FX) re-react on
    // the next tick using the reverted fields.
    void RevertChanges()
    {
        if (_input == null) return;
        if (_rows != null)
            foreach (var r in _rows)
            {
                if (r == null || r.def == null || r.def.set == null) continue;
                if (_snapshotFloat.TryGetValue(r.def, out var snap))
                    r.def.set(snap);
            }
        if (_toggleRows != null)
            foreach (var t in _toggleRows)
            {
                if (t == null || t.def == null || t.def.set == null) continue;
                if (_snapshotBool.TryGetValue(t.def, out var snap))
                    t.def.set(snap);
            }
        RefreshAllRows();
        if (_saveAndApplyBtnRT != null) _saveAndApplyBtnRT.gameObject.SetActive(false);
    }

    // Wraps any "exit from settings panel" action. If there are pending
    // changes, shows the Save prompt first; otherwise invokes the action
    // directly. Used by the BACK button and by ClosePause when called
    // while the settings panel is active.
    void RequestExitSettings(Action exitAction)
    {
        if (!HasPendingChanges()) { exitAction?.Invoke(); return; }
        _pendingExitAction = exitAction;
        ShowSavePrompt();
    }

    void ShowMainPanel()
    {
        RequestExitSettings(ShowMainPanelDirect);
    }

    void ShowMainPanelDirect()
    {
        if (_mainPanel != null) _mainPanel.SetActive(true);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        if (_titleText != null) _titleText.text = "PAUSED";
        // Leaving the settings panel — drop deferral so any code that calls
        // SaveSettings / Apply* outside the menu (autosave, etc.) works.
        if (_input != null) _input.deferApply = false;
    }

    void ShowSettingsPanel()
    {
        if (_mainPanel != null) _mainPanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(true);
        if (_titleText != null) _titleText.text = "SETTINGS";
        // Stage mode: setters write to InputSettings fields but their
        // Apply* / SaveSettings calls no-op until the user clicks the
        // Save and Apply button.
        if (_input != null) _input.deferApply = true;
        CaptureSettingsSnapshot();
        if (_saveAndApplyBtnRT != null) _saveAndApplyBtnRT.gameObject.SetActive(false);
        RefreshAllRows();
        // Force the scroll content's layout to finalize immediately. Without
        // this, the FIRST time the settings panel is shown the inactive tab
        // panels haven't been laid out, so the ScrollRect thinks its content
        // height is 0 and scrolling does nothing. Closing+reopening worked
        // because by then the layout had run via Canvas Update.
        RebuildScrollLayout();
    }

    void RebuildScrollLayout()
    {
        if (_tabContentPanels == null) return;
        // Drive layout on the currently-active tab panel and its parents.
        for (int i = 0; i < _tabContentPanels.Count; i++)
        {
            if (_tabContentPanels[i] == null) continue;
            var rt = _tabContentPanels[i].transform as RectTransform;
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
        // Then rebuild the scroll content + the settings panel root so the
        // ScrollRect sees the final sizes.
        var settingsRT = _settingsPanel != null ? _settingsPanel.transform as RectTransform : null;
        if (settingsRT != null) LayoutRebuilder.ForceRebuildLayoutImmediate(settingsRT);
    }

    void OpenSaveDialog()
    {
        if (_saveDialogRoot != null) return;
        // Save panel needs a parent transform; the card itself works.
        var panel = SaveLoadUI.Build(
            _cardRT,
            SaveLoadMode.Save,
            onSelect: () => CloseSaveDialog(),
            onPickSlot: (saveName) => SaveSystem.Save(saveName),
            onCreateOrNew: (name) => SaveSystem.Save(name),
            onClose: CloseSaveDialog);
        _saveDialogRoot = panel.root;
    }

    void CloseSaveDialog()
    {
        if (_saveDialogRoot != null) Destroy(_saveDialogRoot);
        _saveDialogRoot = null;
    }

    void ReturnToMainMenu()
    {
        // Reset pause state BEFORE loading the menu — without this the
        // DontDestroyOnLoad pause-menu persists with _isPaused = true and
        // appears immediately when the player starts a new game session.
        _isPaused = false;
        SetMenuVisible(false, immediate: true);
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        if (_input != null) _input.SaveSettings();
        SceneManager.LoadScene("MainMenu");
    }

    // ── Card auto-fit + border pulse ─────────────────────────────────

    void LateUpdate()
    {
        if (_cardRT == null || _canvasRT == null) return;
        // Shrink the card if the canvas's scaled height can't fit it (ultrawide
        // displays etc.). Same idea as GalaxyPauseMenuStyler.
        float canvasH = _canvasRT.rect.height;
        float cardH = _cardRT.sizeDelta.y;
        if (cardH <= 0f) return;
        float availableH = canvasH - CardScreenMargin;
        float scale = (availableH > 0f && cardH > availableH) ? availableH / cardH : 1f;
        Vector3 desired = Vector3.one * scale;
        if ((_cardRT.localScale - desired).sqrMagnitude > 0.0000001f)
            _cardRT.localScale = desired;
    }

    IEnumerator BorderPulseRoutine()
    {
        while (this != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.2f) + 1f) * 0.5f;
            if (_borderImage != null)
                _borderImage.color = Color.Lerp(CardBorderCool, CardBorderHot, t * 0.55f);
            yield return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void StretchWithMargins(RectTransform rt, float left, float top, float right, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    // Procedurally-baked circle sprite for slider handles. Cached after the
    // first call.
    static Sprite _circleSprite;
    static Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float r = size * 0.5f - 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - r;
                float dy = y - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        _circleSprite.name = "TabbedPauseMenuCircle";
        return _circleSprite;
    }

}

/// <summary>
/// Tiny helper that retints the border + label of a procedural button on
/// hover/exit. Lives here rather than its own file because it's only used by
/// <see cref="TabbedPauseMenu"/>.
/// </summary>
class ButtonHoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    Image border;
    TMP_Text label;
    Color borderNormal, borderHover, labelNormal, labelHover;

    public void Init(Image border, TMP_Text label, Color borderNormal, Color borderHover, Color labelNormal, Color labelHover)
    {
        this.border = border;
        this.label = label;
        this.borderNormal = borderNormal;
        this.borderHover = borderHover;
        this.labelNormal = labelNormal;
        this.labelHover = labelHover;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (border != null) border.color = borderHover;
        if (label != null) label.color = labelHover;
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (border != null) border.color = borderNormal;
        if (label != null) label.color = labelNormal;
    }
}
