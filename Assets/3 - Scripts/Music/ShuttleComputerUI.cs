using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The shuttle computer's fullscreen screen: alien OS home + the TRAX music app.
///
/// PORT OF <c>prototypes/shuttle-computer/ui/</c> (os.js, trax.js, styles.css).
///
/// ── Built in code, not authored as a prefab ──────────────────────────────
/// ~40 elements that have to match the browser layout Sam signed off on, a
/// palette he is still tuning, and no prefab serialization to clobber if it
/// changes. Same choice NewspaperReaderUI made, for the same reasons.
///
/// ── Locking ──────────────────────────────────────────────────────────────
/// Uses PlayerController.isInModalSlotUI (saved and restored, mirroring
/// NewspaperReaderUI / MonumentLinkPopupUI), NOT PauseState. PauseState is the
/// pause menu's gate and stops time in single player; this is a terminal you
/// walk up to in the world, so the world keeps running.
///
/// ── Multiplayer ──────────────────────────────────────────────────────────
/// Entirely client-local. No NetworkBehaviour, no RPCs, 2D audio. A guest using
/// the computer is invisible to the host and cannot affect sync.
/// </summary>
// Partial: the PROJECTS screen (the menu TRAX opens on, the shelf behind LOAD,
// and the save dialog) lives in ShuttleComputerProjectsUI.cs so neither half
// becomes unreadable.
public partial class ShuttleComputerUI : MonoBehaviour
{
    // ── palette (mirrors the CSS tokens in ui/styles.css) ────────────────
    static readonly Color Bg       = Hex("04070aff");
    static readonly Color Panel    = Hex("071014ff");
    static readonly Color PanelHi  = Hex("0c1c22ff");
    static readonly Color Grid     = Hex("12303aff");
    static readonly Color Ink      = Hex("79ffd0ff");
    static readonly Color InkDim   = Hex("3d8f78ff");
    static readonly Color InkGhost = Hex("1d4a3fff");
    static readonly Color Accent   = Hex("ff4fd8ff");
    static readonly Color Locked   = Hex("2a3a40ff");
    static readonly Color Warn     = Hex("ffc94fff");   // unsaved changes, DELETE, a required name

    static Color Hex(string s)
    {
        Color c;
        ColorUtility.TryParseHtmlString("#" + s, out c);
        return c;
    }

    // ── layout budget ────────────────────────────────────────────────────
    // Every row's position derives from these. The first version hand-tuned
    // each offset independently and the genre plate ended up underneath the
    // status bar; keeping the whole column in one place makes that kind of
    // collision visible instead of something you only find by looking at it.
    const float ScreenW = 1500f;
    const float ScreenH = 940f;
    const float BezelPad = 22f;
    // The status bar ("TRAX - UNTITLED / SYS NOMINAL") is GONE — Sam's call,
    // 2026-08-17: it repeated the project bar and squeezed the timeline. The
    // breadcrumb strings it showed live on the project bar already.
    const float ContentTop = 12f;
    const float ContentBottom = 16f;
    const float SidePad = 22f;

    const float PlateH = 92f;
    const float PlateY = 0f;
    const float DialsY = PlateY - PlateH - 14f;       // -106
    const float DialsH = 288f;
    const float ArrY = DialsY - DialsH - 20f;         // -414 (ArrH: arranger partial)
    const float RackLabelY = ArrY - ArrH - 20f;       // -536
    const float RackLabelH = 20f;
    const float RackY = RackLabelY - RackLabelH - 4f; // -560
    const float RackH = 182f;
    const float StepsY = RackY - RackH - 12f;         // -754
    const float StepsH = 18f;
    const float TransportH = 60f;
    const float ProjBarH = 26f;      // the project bar above the genre plate

    // Resulting column, measured from the top of TraxView's Content (886 high
    // after the project bar; the status bar is gone):
    //   genre plate                      0 ..  -92
    //   dials                         -106 .. -394
    //   arranger (hdr+ruler+strip+ctl)-414 .. -536   <- mid-screen, Sam's call
    //   rack label                    -556 .. -576
    //   rack                          -580 .. -762
    //   steps                         -774 .. -792
    //   transport (anchored bottom)   -826 .. -886
    // No overlaps, ~34px of breathing room above the transport. If you change a
    // height here, re-check the whole column — the rows are consecutive.

    public static ShuttleComputerUI Instance { get; private set; }
    public static bool IsOpen { get { return Instance != null && Instance._open; } }

    bool _open;
    int _openedFrame = -1;

    /// Frame on which an F press was spent closing this screen, so the terminal
    /// that opens on F can't reopen it in the same frame. Same idiom as
    /// StorageUI ↔ LootBox.
    static int s_consumedFFrame = -1;
    public static bool FConsumedThisFrame { get { return s_consumedFFrame == Time.frameCount; } }

    /// <summary>
    /// Lets TabbedPauseMenu skip opening on the same Escape this screen just
    /// used to step back. Same idiom as NewspaperReaderUI / MushroomSellUI —
    /// Update order between the two is undefined, so BOTH this and the IsOpen
    /// check are needed to cover either order.
    /// </summary>
    int _consumedEscapeFrame = -1;
    public static bool ConsumedEscapeThisFrame
    {
        get { return Instance != null && Instance._consumedEscapeFrame == Time.frameCount; }
    }

    /// <summary>
    /// TRUE WHILE THE PROJECT-NAME FIELD OWNS THE KEYBOARD.
    ///
    /// Feeds <see cref="AIChatScreen.IsTypingActive"/>, which is this project's
    /// established "a text field is capturing keys" flag — some twenty systems
    /// already consult it (the build menu, the flashlight, the map, the hotbar,
    /// the pause menu, the pistol). Joining it is what stops typing DEEP CAVE
    /// from also opening the build menu on the N.
    /// </summary>
    public static bool IsTypingActive
    {
        get { return Instance != null && Instance._open && Instance.SaveOpen; }
    }

    void ConsumeEscape() { _consumedEscapeFrame = Time.frameCount; }

    bool _prevModalFlag;
    CursorLockMode _prevCursorLock;
    bool _prevCursorVisible;

    TraxInstrument _inst;
    Canvas _canvas;
    GameObject _homeView, _traxView;

    TextMeshProUGUI _genreLabel, _genreVibe, _genreMeta, _readout;
    TextMeshProUGUI _playLabel, _loopLabel, _toastLabel;
    Image _playBg, _loopBg;
    readonly List<TraxKnob> _knobs = new List<TraxKnob>();
    readonly List<Image> _stepCells = new List<Image>();
    readonly Dictionary<string, Image> _moduleFrames = new Dictionary<string, Image>();
    readonly Dictionary<string, Image> _moduleLeds = new Dictionary<string, Image>();
    readonly Dictionary<string, TextMeshProUGUI> _moduleNames = new Dictionary<string, TextMeshProUGUI>();
    readonly Dictionary<string, TextMeshProUGUI> _moduleDescs = new Dictionary<string, TextMeshProUGUI>();
    readonly Dictionary<string, string> _moduleDefaultDescs = new Dictionary<string, string>();
    CanvasGroup _toastGroup;
    GameObject _printPanel;
    Image _saveBg;

    // The tier and quantity steppers went with the physical slot — see
    // BuildPrintDialog. The dialog now reports the machine rather than
    // configuring it.
    TextMeshProUGUI _printSub, _printNote, _printConfirmLabel, _printSlotState;
    Image _printConfirm;
    int _lastStepShown = -1;
    int _lastBarShown = -1;
    float _toastUntil;

    static TMP_FontAsset _font;

    // ── the world-screen mirror ──────────────────────────────────────────
    //
    // The ConsoleScreen mesh shows whatever the computer UI last displayed:
    // a SNAPSHOT of the canvas is rendered into this texture every time the
    // computer closes (nothing changes while nobody's using it, so a frozen
    // frame IS the live picture). ShuttleComputerTerminal puts it on the
    // screen material. Dark until the computer is first used — reads as off.

    static RenderTexture s_mirror;

    public static RenderTexture ScreenMirror
    {
        get
        {
            if (s_mirror == null)
            {
                s_mirror = new RenderTexture(1024, 576, 0, RenderTextureFormat.ARGB32);
                s_mirror.name = "ShuttleScreenMirror";
                s_mirror.Create();
                var prev = RenderTexture.active;
                RenderTexture.active = s_mirror;
                GL.Clear(true, true, new Color(0.02f, 0.05f, 0.08f, 1f));   // powered-off navy
                RenderTexture.active = prev;
            }
            return s_mirror;
        }
    }

    System.Collections.IEnumerator CaptureMirrorThenHide()
    {
        yield return new WaitForEndOfFrame();
        // Reopened in the same frame (F closes and the terminal reopens)?
        // Then the canvas must stay — and the capture would be right anyway.
        if (_open) yield break;
        if (_canvas != null && _canvas.gameObject.activeInHierarchy)
        {
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot != null)
            {
                Graphics.Blit(shot, ScreenMirror);
                Destroy(shot);
            }
            _canvas.gameObject.SetActive(false);
        }
    }

    // ── open / close ─────────────────────────────────────────────────────

    /// <summary>Open the computer. Creates the UI on first use.</summary>
    public static void Open()
    {
        if (Instance == null)
        {
            var go = new GameObject("ShuttleComputerUI");
            Instance = go.AddComponent<ShuttleComputerUI>();
            Instance.Build();
        }
        Instance.DoOpen();
    }

    void DoOpen()
    {
        if (_open) return;
        _open = true;
        _openedFrame = Time.frameCount;

        _prevModalFlag = PlayerController.isInModalSlotUI;
        PlayerController.isInModalSlotUI = true;

        _prevCursorLock = Cursor.lockState;
        _prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // The walkman and the terminal are two separate synths; both running
        // at once is mush. Sitting down at the computer stops the tape.
        TraxTapePlayer.StopAll();

        // RESUME the screen you left, don't reset to HOME — the world monitor
        // shows a freeze-frame of that screen, so coming back anywhere else
        // made the mirror a liar (Sam's playtest note). Build() initialises
        // the view state to HOME for the very first open.
        SyncPlayButton();
        _canvas.gameObject.SetActive(true);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;

        if (_inst != null) _inst.Stop();
        // The freeze-frame and the resumed screen must both show a stopped
        // transport, or the PLAY button lies twice.
        SyncPlayButton();
        ClearPlayhead();
        ClosePrint();
        // The canvas stays up ONE more frame: at end of frame the backbuffer
        // — which IS the computer UI, pixel for pixel — is copied onto the
        // world screen's mirror texture, then the canvas hides. An exact
        // freeze-frame of what the player was just looking at.
        StartCoroutine(CaptureMirrorThenHide());

        // Restore rather than force-clear: another modal UI may have been up
        // when this one opened, and clobbering its flag would strand the player.
        PlayerController.isInModalSlotUI = _prevModalFlag;
        Cursor.lockState = _prevCursorLock;
        Cursor.visible = _prevCursorVisible;
    }

    void OnDestroy()
    {
        // Never leave the player locked out because the scene changed mid-session.
        if (_open)
        {
            PlayerController.isInModalSlotUI = _prevModalFlag;
            Cursor.lockState = _prevCursorLock;
            Cursor.visible = _prevCursorVisible;
        }
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!_open) return;

        // Safety net, same as StorageUI's: if anything else grabs the player
        // (a conversation starts, the map or phone opens, they end up piloting)
        // get out of the way rather than fighting over the modal flag.
        if (PlayerController.isInDialogue || PlayerController.isMapOpen
            || PlayerPhoneUI.IsOpen || Ship.AnyShipPiloted)
        {
            Close();
            return;
        }

        // The save dialog is modal AND has a text field: ESC dismisses it, and
        // every other key belongs to the field. F must not close the computer
        // and SPACE must reach the name as a space, not as PLAY.
        if (SaveOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { ConsumeEscape(); CloseSaveDialog(); }
            return;
        }

        // The print dialog is modal: ESC dismisses it rather than the whole
        // computer, and the transport shortcut is suppressed while it is up.
        if (PrintOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { ConsumeEscape(); ClosePrint(); }
            return;
        }

        // ESC steps back one screen at a time — shelf to menu, instrument to
        // menu — and only leaves the computer from the menu or the desktop.
        // F always leaves outright, because F is what opened it.
        if (Time.frameCount > _openedFrame && Input.GetKeyDown(KeyCode.Escape))
        {
            // Every one of these SPENDS the Escape — without saying so, the same
            // press pops the pause menu on top of the screen you just went back to.
            if (ProjectsOpen && _shelfPane.activeSelf) { ConsumeEscape(); ShowMenuPane(); return; }
            if (_traxView != null && _traxView.activeSelf) { ConsumeEscape(); _inst.Stop(); ShowProjects(); return; }
            if (ProjectsOpen) { ConsumeEscape(); ShowHomeFromProjects(); return; }
        }

        // Not on the frame it opened: the terminal opens on F-down, and Update
        // order between it and this component is undefined — without the guard
        // the same keypress could open and immediately close the screen.
        if (Time.frameCount > _openedFrame &&
            (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F)))
        {
            // Tell the terminal this F is spent so its own handler can't
            // reopen us in the same frame. Mirrors StorageUI/LootBox.
            if (Input.GetKeyDown(KeyCode.F)) s_consumedFFrame = Time.frameCount;
            if (Input.GetKeyDown(KeyCode.Escape)) ConsumeEscape();
            Close();
            return;
        }

        if (_traxView != null && _traxView.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Space)) TogglePlay();
            RefreshPlayhead();
            ArrangerUpdate();
        }

        // The shelf is world state a co-op partner can change while you are
        // looking at it, so it rebuilds off the library's version counter
        // rather than assuming this screen is the only thing that writes.
        if (ProjectsOpen && _shelfPane.activeSelf && _shelfVersionShown != TraxLibrary.Version)
        {
            RebuildShelf();
            RefreshMenuPane();
            _shelfVersionShown = TraxLibrary.Version;
        }

        // A host snapshot replaces every Record object on the shelf, so the one
        // this screen is holding becomes a detached copy that no longer matches
        // anything. Re-point it at the real record by name.
        if (_remoteRevShown != TraxSync.RemoteRevision)
        {
            _remoteRevShown = TraxSync.RemoteRevision;
            RelinkProjectAfterSync();
        }

        if (_toastGroup != null && _toastGroup.alpha > 0f && Time.unscaledTime > _toastUntil)
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, 0f, Time.unscaledDeltaTime * 4f);
    }

    // ── construction ─────────────────────────────────────────────────────

    void Build()
    {
        // Deliberately NOT DontDestroyOnLoad. If this survived a scene change
        // while open, it would carry the modal-lock flag into the next scene
        // and strand the player. Dying with the scene means OnDestroy restores
        // the flag, and the next terminal just rebuilds it.
        if (_font == null)
            _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        EnsureEventSystem();

        var instGo = new GameObject("TraxInstrument");
        instGo.transform.SetParent(transform, false);
        _inst = instGo.AddComponent<TraxInstrument>();
        _inst.PatternSwapped += () => { _lastBarShown = -1; };

        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1000;          // above the HUD, phone and prompts
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var bg = MakePanel(canvasGo.transform, "Backdrop", Hex("000000ff"));
        Stretch(bg.rectTransform, 0, 0, 0, 0);
        bg.raycastTarget = true;      // swallow clicks that miss a control

        // Monitor shell. The bezel is what makes this read as a screen in the
        // world rather than a game menu that happens to be fullscreen — the
        // browser build got that for free from the browser window.
        var bezel = MakePanel(bg.transform, "Bezel", Hex("0d1418ff"));
        var brt = bezel.rectTransform;
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(ScreenW + BezelPad * 2, ScreenH + BezelPad * 2);
        brt.anchoredPosition = Vector2.zero;
        Outline(bezel.transform, Hex("1b2a30ff"));

        var screen = MakePanel(bezel.transform, "Screen", Bg);
        var srt = screen.rectTransform;
        Stretch(srt, BezelPad, BezelPad, BezelPad, BezelPad);

        BuildHome(srt);
        BuildTrax(srt);
        BuildProjects(srt);           // TRAX opens here, not on the dials
        BuildCrtOverlay(srt);         // over the content, under the dialogs
        BuildToast(srt);
        BuildPrintDialog(srt);
        BuildSaveDialog(srt);

        // Initialise the view state once — every view GameObject is born
        // active, and DoOpen resumes rather than resets.
        ShowHome();

        _canvas.gameObject.SetActive(false);
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    void BuildCrtOverlay(RectTransform parent)
    {
        // Scanlines. RawImage rather than Image because tiling via uvRect is
        // exact — an Image with type=Tiled would quantise to whole sprites.
        var scan = new GameObject("Scanlines", typeof(RectTransform));
        scan.transform.SetParent(parent, false);
        var raw = scan.AddComponent<RawImage>();
        raw.texture = TraxUISprites.Scanlines;
        raw.uvRect = new Rect(0, 0, 1, ScreenH / 4f);   // one cell per 4 reference px
        raw.raycastTarget = false;
        Stretch((RectTransform)scan.transform, 0, 0, 0, 0);

        var vig = MakeSprite(parent, "Vignette", TraxUISprites.Vignette, Color.white);
        Stretch(vig.rectTransform, 0, 0, 0, 0);

        // Faint phosphor wash down from the top edge, matching #screen::before.
        // Ends where the genre plate does (content top 12 + project bar 26 +
        // plate 92) — running it down behind the knobs read as a grey band
        // cutting through the dials (Sam's playtest note).
        var wash = MakePanel(parent, "Wash", new Color(Ink.r, Ink.g, Ink.b, 0.035f));
        var wrt = wash.rectTransform;
        wrt.anchorMin = new Vector2(0, 1);
        wrt.anchorMax = new Vector2(1, 1);
        wrt.pivot = new Vector2(0.5f, 1);
        wrt.sizeDelta = new Vector2(0, ContentTop + ProjBarH + PlateH);
        wrt.anchoredPosition = Vector2.zero;
    }

    // ── home ─────────────────────────────────────────────────────────────

    struct AppDef
    {
        public string name; public string glyph; public bool enabled;
        public AppDef(string n, string g, bool e) { name = n; glyph = g; enabled = e; }
    }

    // No glyph strings: ♫ ✉ ▤ ◉ are missing from the TMP atlas for the same
    // reason the arrows were, so each icon is composed from the generated
    // sprites instead.
    static readonly AppDef[] Apps =
    {
        new AppDef("TRAX",  "note",  true),
        new AppDef("MAIL",  "mail",  false),
        new AppDef("BANK",  "bars",  false),
        new AppDef("RADIO", "radio", false)
    };

    /// <summary>
    /// App icons built out of rectangles, discs and rings. Crude, but they
    /// actually draw — which the Unicode glyphs did not.
    /// </summary>
    void MakeAppIcon(RectTransform parent, string kind, Color tint)
    {
        var holder = MakeRect(parent, "Icon");
        holder.anchorMin = new Vector2(0.5f, 0.5f);
        holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.sizeDelta = new Vector2(56, 56);
        holder.anchoredPosition = new Vector2(0, 18);

        switch (kind)
        {
            case "note":
            {
                var head = MakeSprite(holder, "Head", TraxUISprites.Disc, tint);
                Box(head.rectTransform, Centre, Centre, new Vector2(-9, -14), new Vector2(20, 15));
                var stem = MakeSprite(holder, "Stem", TraxUISprites.White, tint);
                Box(stem.rectTransform, Centre, Centre, new Vector2(1, 2), new Vector2(4, 36));
                var flag = MakeSprite(holder, "Flag", TraxUISprites.White, tint);
                Box(flag.rectTransform, Centre, Centre, new Vector2(9, 16), new Vector2(18, 5));
                break;
            }
            case "mail":
            {
                var body = MakeSprite(holder, "Body", TraxUISprites.Border, tint);
                body.type = Image.Type.Sliced;
                Box(body.rectTransform, Centre, Centre, Vector2.zero, new Vector2(48, 32));
                var l = MakeSprite(holder, "FlapL", TraxUISprites.White, tint);
                Box(l.rectTransform, Centre, Centre, new Vector2(-12, 6), new Vector2(28, 2));
                l.rectTransform.localEulerAngles = new Vector3(0, 0, -28);
                var r = MakeSprite(holder, "FlapR", TraxUISprites.White, tint);
                Box(r.rectTransform, Centre, Centre, new Vector2(12, 6), new Vector2(28, 2));
                r.rectTransform.localEulerAngles = new Vector3(0, 0, 28);
                break;
            }
            case "bars":
            {
                for (int i = 0; i < 3; i++)
                {
                    var bar = MakeSprite(holder, "Bar" + i, TraxUISprites.White, tint);
                    Box(bar.rectTransform, Centre, Centre,
                        new Vector2(0, 14 - i * 14), new Vector2(44 - i * 8, 6));
                }
                break;
            }
            default:
            {
                var ring = MakeSprite(holder, "Ring", TraxUISprites.Ring, tint);
                Box(ring.rectTransform, Centre, Centre, Vector2.zero, new Vector2(50, 50));
                var dot = MakeSprite(holder, "Dot", TraxUISprites.Disc, tint);
                Box(dot.rectTransform, Centre, Centre, Vector2.zero, new Vector2(16, 16));
                break;
            }
        }
    }

    static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

    void BuildHome(RectTransform parent)
    {
        var view = MakeRect(parent, "HomeView");
        Stretch(view, SidePad, SidePad, ContentTop, ContentBottom);
        _homeView = view.gameObject;

        var title = MakeText(view, "Title", "APPLICATIONS", 18, InkGhost, TextAlignmentOptions.Center);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0, 0.5f);
        trt.anchorMax = new Vector2(1, 0.5f);
        trt.pivot = new Vector2(0.5f, 0);
        trt.sizeDelta = new Vector2(0, 30);
        trt.anchoredPosition = new Vector2(0, 130);
        title.characterSpacing = 22;

        const float cell = 190f, gap = 26f;
        float total = Apps.Length * cell + (Apps.Length - 1) * gap;

        for (int i = 0; i < Apps.Length; i++)
        {
            AppDef app = Apps[i];
            var frame = MakePanel(view, "App_" + app.name, app.enabled ? Panel : Hex("0a1418ff"));
            frame.raycastTarget = app.enabled;   // locked apps are visibly dead, not silently clickable
            var rt = frame.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(cell, cell);
            rt.anchoredPosition = new Vector2(-total * 0.5f + cell * 0.5f + i * (cell + gap), -10);

            Outline(frame.transform, app.enabled ? Grid : Hex("141d21ff"));

            MakeAppIcon(frame.rectTransform, app.glyph, app.enabled ? Ink : Locked);

            var label = MakeText(frame.rectTransform, "Name", app.name, 17,
                                 app.enabled ? Ink : Locked, TextAlignmentOptions.Center);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(1, 0);
            lrt.pivot = new Vector2(0.5f, 0);
            lrt.sizeDelta = new Vector2(0, 26);
            lrt.anchoredPosition = new Vector2(0, app.enabled ? 30 : 44);
            label.characterSpacing = 12;

            if (!app.enabled)
            {
                var no = MakeText(frame.rectTransform, "NoLicence", "NO LICENCE", 12, Locked,
                                  TextAlignmentOptions.Center);
                var nrt = no.rectTransform;
                nrt.anchorMin = new Vector2(0, 0);
                nrt.anchorMax = new Vector2(1, 0);
                nrt.pivot = new Vector2(0.5f, 0);
                nrt.sizeDelta = new Vector2(0, 20);
                nrt.anchoredPosition = new Vector2(0, 22);
            }
            else
            {
                var btn = frame.gameObject.AddComponent<Button>();
                btn.targetGraphic = frame;
                var cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
                cb.pressedColor = new Color(2f, 2f, 2f, 1f);
                btn.colors = cb;
                btn.onClick.AddListener(ShowProjects);
            }
        }
    }

    // ── trax ─────────────────────────────────────────────────────────────

    void BuildTrax(RectTransform parent)
    {
        var view = MakeRect(parent, "TraxView");
        // ContentTop clears the status bar. The first version used 12 here and
        // the genre plate rendered on top of it.
        Stretch(view, SidePad, SidePad, ContentTop, ContentBottom);
        _traxView = view.gameObject;

        BuildProjectBar(view);

        // Everything below keeps its offsets from the layout budget by hanging
        // off an inner rect pushed down past the bar, rather than each row
        // being re-tuned by hand.
        var inner = MakeRect(view, "Content");
        Stretch(inner, 0, 0, ProjBarH, 0);

        BuildArranger(inner);
        BuildGenrePlate(inner);
        BuildDials(inner);
        BuildRack(inner);
        BuildTransport(inner);
    }

    void BuildGenrePlate(RectTransform parent)
    {
        var plate = MakePanel(parent, "GenrePlate", PanelHi);
        var rt = plate.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, PlateH);
        rt.anchoredPosition = new Vector2(0, PlateY);
        Outline(plate.transform, Grid);

        // Explicit boxes rather than stretched-with-padding: four labels sharing
        // one rect via offsets is exactly how the caption and the vibe line
        // ended up on top of each other. Compressed since the arranger landed
        // above — the plate reads the SELECTED SECTION now.
        var cap = MakeText(rt, "Caption", "SECTION GENRE", 12, InkGhost, TextAlignmentOptions.TopLeft);
        Box(cap.rectTransform, TopLeft, TopLeft, new Vector2(20, -8), new Vector2(320, 16));
        cap.characterSpacing = 32;

        _genreLabel = MakeText(rt, "Label", "—", 42, Accent, TextAlignmentOptions.TopLeft);
        Box(_genreLabel.rectTransform, TopLeft, TopLeft, new Vector2(18, -22), new Vector2(940, 50));
        _genreLabel.characterSpacing = 6;

        _genreVibe = MakeText(rt, "Vibe", "", 14, InkDim, TextAlignmentOptions.BottomLeft);
        Box(_genreVibe.rectTransform, BottomLeft, BottomLeft, new Vector2(20, 8), new Vector2(760, 20));
        _genreVibe.characterSpacing = 8;

        _genreMeta = MakeText(rt, "Meta", "", 14, InkGhost, TextAlignmentOptions.TopRight);
        Box(_genreMeta.rectTransform, TopRight, TopRight, new Vector2(-20, -12), new Vector2(420, 50));
        _genreMeta.lineSpacing = 8;
    }

    void BuildDials(RectTransform parent)
    {
        var row = MakeRect(parent, "Dials");
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.sizeDelta = new Vector2(0, DialsH);
        row.anchoredPosition = new Vector2(0, DialsY);

        var defs = TraxDialDefs.All;
        for (int i = 0; i < defs.Length; i++)
        {
            float w = 1f / defs.Length;
            var cell = MakePanel(row, "Knob_" + defs[i].label, Panel);
            cell.raycastTarget = true;          // MakePanel defaults to false; knobs need pointer events
            var crt = cell.rectTransform;
            crt.anchorMin = new Vector2(i * w, 0);
            crt.anchorMax = new Vector2((i + 1) * w, 1);
            crt.offsetMin = new Vector2(5, 0);
            crt.offsetMax = new Vector2(-5, 0);
            Outline(cell.transform, Grid);

            var name = MakeText(crt, "Name", defs[i].label, 17, Ink, TextAlignmentOptions.Top);
            Stretch(name.rectTransform, 0, 0, 14, 0);
            name.characterSpacing = 16;

            // Arc track + fill. Rotated -135° so the sweep starts lower-left.
            var track = MakeSprite(crt, "Track", TraxUISprites.Ring, Grid);
            CentreSquare(track.rectTransform, 168, 20);
            track.type = Image.Type.Filled;
            track.fillMethod = Image.FillMethod.Radial360;
            track.fillOrigin = (int)Image.Origin360.Top;
            track.fillClockwise = true;
            track.fillAmount = TraxKnob.Sweep;
            track.rectTransform.localEulerAngles = new Vector3(0, 0, -TraxKnob.StartAngle);

            var fill = MakeSprite(crt, "Fill", TraxUISprites.Ring, Ink);
            CentreSquare(fill.rectTransform, 168, 20);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            fill.rectTransform.localEulerAngles = new Vector3(0, 0, -TraxKnob.StartAngle);

            var hub = MakeSprite(crt, "Hub", TraxUISprites.Disc, PanelHi);
            CentreSquare(hub.rectTransform, 96, 20);

            // Pointer pivots at its bottom so rotation swings it around the hub.
            var pointer = MakeSprite(crt, "Pointer", TraxUISprites.White, Accent);
            var prt = pointer.rectTransform;
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(3, 46);
            prt.anchoredPosition = new Vector2(0, 20);

            var val = MakeText(crt, "Value", "0.0", 23, Ink, TextAlignmentOptions.Bottom);
            var vrt = val.rectTransform;
            vrt.anchorMin = new Vector2(0, 0);
            vrt.anchorMax = new Vector2(1, 0);
            vrt.pivot = new Vector2(0.5f, 0);
            vrt.sizeDelta = new Vector2(0, 30);
            vrt.anchoredPosition = new Vector2(0, 48);

            var flav = MakeText(crt, "Flavor", defs[i].flavor, 12, InkGhost, TextAlignmentOptions.Bottom);
            var frt = flav.rectTransform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 0);
            frt.pivot = new Vector2(0.5f, 0);
            frt.sizeDelta = new Vector2(-10, 36);
            frt.anchoredPosition = new Vector2(0, 8);

            var knob = cell.gameObject.AddComponent<TraxKnob>();
            knob.Init(defs[i].index, _inst.Dials.Get(defs[i].index), cell, fill, prt, val,
                      Panel, PanelHi, OnKnobChanged);
            _knobs.Add(knob);
        }
    }

    void BuildRack(RectTransform parent)
    {
        var label = MakeText(parent, "RackLabel", "PLUGIN RACK", 14, InkGhost, TextAlignmentOptions.Left);
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0, 1);
        lrt.anchorMax = new Vector2(1, 1);
        lrt.pivot = new Vector2(0.5f, 1);
        lrt.sizeDelta = new Vector2(0, RackLabelH);
        lrt.anchoredPosition = new Vector2(0, RackLabelY);
        label.characterSpacing = 24;

        var row = MakeRect(parent, "Rack");
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.sizeDelta = new Vector2(0, RackH);
        row.anchoredPosition = new Vector2(0, RackY);

        var mods = TraxInstrument.Modules;
        for (int i = 0; i < mods.Length; i++)
        {
            var m = mods[i];
            string captured = m.name;
            float w = 1f / mods.Length;

            var cell = MakePanel(row, "Mod_" + i, PanelHi);
            var crt = cell.rectTransform;
            crt.anchorMin = new Vector2(i * w, 0);
            crt.anchorMax = new Vector2((i + 1) * w, 1);
            crt.offsetMin = new Vector2(5, 0);
            crt.offsetMax = new Vector2(-5, 0);

            var frame = Outline(cell.transform, InkDim);

            // The on/off toggle is its own click target covering only the head,
            // so choosing a preset can never accidentally mute the module you
            // are auditioning.
            var head = MakePanel(crt, "Head", new Color(0, 0, 0, 0));
            head.raycastTarget = true;
            var hrt = head.rectTransform;
            hrt.anchorMin = new Vector2(0, 1);
            hrt.anchorMax = new Vector2(1, 1);
            hrt.pivot = new Vector2(0.5f, 1);
            hrt.sizeDelta = new Vector2(0, 74);
            hrt.anchoredPosition = Vector2.zero;
            var headBtn = head.gameObject.AddComponent<Button>();
            headBtn.targetGraphic = head;
            headBtn.onClick.AddListener(delegate { ToggleModule(captured); });

            var led = MakeSprite(crt, "Led", TraxUISprites.Disc, Ink);
            Box(led.rectTransform, TopCentre, TopCentre, new Vector2(0, -10), new Vector2(14, 14));

            var nm = MakeText(crt, "Name", m.name, 17, Ink, TextAlignmentOptions.Center);
            Box(nm.rectTransform, TopCentre, TopCentre, new Vector2(0, -26), new Vector2(160, 22));
            nm.characterSpacing = 10;

            var ds = MakeText(crt, "Desc", m.desc, 12, InkGhost, TextAlignmentOptions.Center);
            Box(ds.rectTransform, TopCentre, TopCentre, new Vector2(0, -48), new Vector2(160, 18));

            // PRESET = which part. VARIATION = which roll of that part. Both are
            // dead on a module you do not own — they would silently change the
            // track identity while changing nothing you can hear.
            var preset = MakeStepper(crt, "Preset", -76,
                () => _inst.IsInstalled(captured) ? _inst.PresetName(captured) : "LOCKED",
                d => { if (!_inst.IsInstalled(captured)) return; _inst.CyclePreset(captured, d); AfterPartChange(); }, Ink);
            var varn = MakeStepper(crt, "Var", -110,
                () => _inst.IsInstalled(captured) ? "VAR " + (_inst.VariationIndex(captured) + 1) : "--",
                d => { if (!_inst.IsInstalled(captured)) return; _inst.CycleVariation(captured, d); AfterPartChange(); }, InkDim);

            _moduleFrames[m.name] = frame;
            _moduleLeds[m.name] = led;
            _moduleNames[m.name] = nm;
            _moduleDescs[m.name] = ds;
            _moduleDefaultDescs[m.name] = m.desc;
            _steppers.Add(preset);
            _steppers.Add(varn);
        }

        // Step lights.
        var steps = MakeRect(parent, "Steps");
        steps.anchorMin = new Vector2(0, 1);
        steps.anchorMax = new Vector2(1, 1);
        steps.pivot = new Vector2(0.5f, 1);
        steps.sizeDelta = new Vector2(0, StepsH);
        steps.anchoredPosition = new Vector2(0, StepsY);

        for (int i = 0; i < TraxPhrase.Steps; i++)
        {
            float w = 1f / TraxPhrase.Steps;
            var cell = MakePanel(steps, "Step_" + i, Hex("0d1a1fff"));
            var crt = cell.rectTransform;
            crt.anchorMin = new Vector2(i * w, 0);
            crt.anchorMax = new Vector2((i + 1) * w, 1);
            crt.offsetMin = new Vector2(2, 0);
            crt.offsetMax = new Vector2(-2, 0);
            _stepCells.Add(cell);
        }
    }

    void BuildTransport(RectTransform parent)
    {
        var row = MakeRect(parent, "Transport");
        row.anchorMin = new Vector2(0, 0);
        row.anchorMax = new Vector2(1, 0);
        row.pivot = new Vector2(0.5f, 0);
        row.sizeDelta = new Vector2(0, TransportH);
        row.anchoredPosition = Vector2.zero;

        float x = 0;

        // Two transports: PLAY TRACK chains the sections from the play cursor;
        // LOOP SECTION is the old behaviour — loop the selected section
        // forever while you shape it. Sized so the readout between them and
        // the right-hand cluster never collides with the VOL label — the row
        // has ~575px of left-side room before the cluster starts.
        _playBg = MakeButton(row, "Play", "PLAY TRACK", 150, ref x, Ink, Hex("04120eff"), TogglePlay);
        _playLabel = _playBg.GetComponentInChildren<TextMeshProUGUI>();
        _playLabel.fontSize = 15;

        x += 8;
        _loopBg = MakeButton(row, "Loop", "LOOP SECTION", 175, ref x, PanelHi, Ink, ToggleLoop);
        _loopLabel = _loopBg.GetComponentInChildren<TextMeshProUGUI>();
        _loopLabel.fontSize = 15;
        Outline(_loopBg.transform, InkGhost);

        _readout = MakeText(row, "Readout", "", 14, InkDim, TextAlignmentOptions.Left);
        var rrt = _readout.rectTransform;
        rrt.anchorMin = new Vector2(0, 0);
        rrt.anchorMax = new Vector2(0, 1);
        rrt.pivot = new Vector2(0, 0.5f);
        rrt.sizeDelta = new Vector2(220, 0);
        rrt.anchoredPosition = new Vector2(x + 12, 0);

        // Right-hand cluster, laid out from the right edge inward.
        float rx = 0;
        MakeButtonRight(row, "Exit", "PROJECTS", 150, ref rx, PanelHi, InkDim, ShowProjects);
        BuildPrintCluster(row, ref rx);
        _saveBg = MakeButtonRight(row, "Save", "SAVE PROJECT", 190, ref rx, PanelHi, Ink, OpenSaveDialog);
        BuildVolume(row, ref rx);
        BuildKey(row, ref rx);
    }

    void BuildPrintCluster(RectTransform row, ref float rx)
    {
        // One button. The quantity lives in the dialog it opens, so the
        // transport row is not carrying a stepper for something you press once.
        // "PRINT DEMO" was the name when this pressed a demo of the track. It
        // presses THE tape now, off the blank in the slot.
        MakeButtonRight(row, "PrintDemo", "PRINT", 170, ref rx, PanelHi, Ink, OpenPrint);
    }

    void BuildVolume(RectTransform row, ref float rx)
    {
        rx += 18;

        var holder = MakeRect(row, "Volume");
        holder.anchorMin = new Vector2(1, 0.5f);
        holder.anchorMax = new Vector2(1, 0.5f);
        holder.pivot = new Vector2(1, 0.5f);
        holder.sizeDelta = new Vector2(180, 30);
        holder.anchoredPosition = new Vector2(-rx, 0);
        rx += 180;

        var lbl = MakeText(holder, "Label", "VOL", 13, InkGhost, TextAlignmentOptions.Left);
        var lrt = lbl.rectTransform;
        lrt.anchorMin = new Vector2(0, 0);
        lrt.anchorMax = new Vector2(0, 1);
        lrt.pivot = new Vector2(0, 0.5f);
        lrt.sizeDelta = new Vector2(40, 0);
        lrt.anchoredPosition = Vector2.zero;

        var sliderGo = new GameObject("Slider", typeof(RectTransform));
        sliderGo.transform.SetParent(holder, false);
        var srt = (RectTransform)sliderGo.transform;
        srt.anchorMin = new Vector2(0, 0.5f);
        srt.anchorMax = new Vector2(1, 0.5f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.offsetMin = new Vector2(44, -5);
        srt.offsetMax = new Vector2(0, 5);

        var bgImg = MakeSprite(srt, "Background", TraxUISprites.White, Grid);
        bgImg.raycastTarget = true;             // the whole track must be clickable, not just the handle
        Stretch(bgImg.rectTransform, 0, 0, 0, 0);

        var fillArea = MakeRect(srt, "Fill Area");
        Stretch(fillArea, 0, 0, 0, 0);
        var fillImg = MakeSprite(fillArea, "Fill", TraxUISprites.White, Ink);
        Stretch(fillImg.rectTransform, 0, 0, 0, 0);

        var handleArea = MakeRect(srt, "Handle Slide Area");
        Stretch(handleArea, 0, 0, 0, 0);
        var handleImg = MakeSprite(handleArea, "Handle", TraxUISprites.Disc, Ink);
        handleImg.raycastTarget = true;
        handleImg.rectTransform.sizeDelta = new Vector2(16, 16);

        var slider = sliderGo.AddComponent<Slider>();
        slider.fillRect = fillImg.rectTransform;
        slider.handleRect = handleImg.rectTransform;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = _inst.MasterVolume;
        slider.onValueChanged.AddListener(delegate (float v) { _inst.SetMasterVolume(v); });
    }

    /// KEY — one control that moves everything, and regenerates nothing.
    void BuildKey(RectTransform row, ref float rx)
    {
        rx += 14;
        var holder = MakeRect(row, "Key");
        holder.anchorMin = new Vector2(1, 0.5f);
        holder.anchorMax = new Vector2(1, 0.5f);
        holder.pivot = new Vector2(1, 0.5f);
        holder.sizeDelta = new Vector2(140, 30);
        holder.anchoredPosition = new Vector2(-rx, 0);
        rx += 140;

        var lbl = MakeText(holder, "Label", "KEY", 13, InkGhost, TextAlignmentOptions.Left);
        Box(lbl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(36, 20));

        var box = MakeRect(holder, "Box");
        box.anchorMin = new Vector2(0, 0);
        box.anchorMax = new Vector2(1, 1);
        box.offsetMin = new Vector2(38, 0);
        box.offsetMax = new Vector2(0, 0);

        var st = MakeStepper(box, "Key", 0,
            () => _inst.KeyName,
            d => { _inst.CycleKey(d); AfterPartChange(); }, Accent);
        st.label.fontSize = 16;
        _steppers.Add(st);
    }

    void BuildToast(RectTransform parent)
    {
        var go = MakePanel(parent, "Toast", Hex("040a0cf5"));
        var rt = go.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(560, 44);
        rt.anchoredPosition = new Vector2(0, 150);
        Outline(go.transform, Accent);

        _toastLabel = MakeText(rt, "Text", "", 16, Accent, TextAlignmentOptions.Center);
        Stretch(_toastLabel.rectTransform, 0, 0, 0, 0);

        _toastGroup = go.gameObject.AddComponent<CanvasGroup>();
        _toastGroup.alpha = 0f;
        _toastGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// The PRINT dialog.
    ///
    /// ── What used to be here, and why it's gone ──────────────────────────
    /// A tier stepper and a quantity stepper. Both were removed on 2026-08-14
    /// when the machine grew a physical slot:
    ///
    ///   • TIER is now a property of the blank you physically put in. Choosing
    ///     "TAPE II" from a menu while holding no TAPE II was always a lie the
    ///     dialog had to apologise for afterwards.
    ///   • QUANTITY was only ever a way to skip the ritual. One insert, one
    ///     tape. If you want ten, you feed it ten times — and that is the point
    ///     of it being a machine.
    ///
    /// What's left is a confirmation with two states: PLEASE INSERT CASSETTE, or
    /// READY TO PRINT. The slot is the gate now, not the hotbar count.
    /// </summary>
    void BuildPrintDialog(RectTransform parent)
    {
        // Full-bleed scrim: dims the screen AND swallows clicks, so the
        // controls behind it can't be nudged while the dialog is up.
        var scrim = MakePanel(parent, "PrintScrim", new Color(0, 0, 0, 0.72f));
        scrim.raycastTarget = true;
        Stretch(scrim.rectTransform, 0, 0, 0, 0);
        _printPanel = scrim.gameObject;

        var panel = MakePanel(scrim.rectTransform, "Panel", PanelHi);
        var prt = panel.rectTransform;
        prt.anchorMin = Centre;
        prt.anchorMax = Centre;
        prt.pivot = Centre;
        prt.sizeDelta = new Vector2(560, 300);
        prt.anchoredPosition = Vector2.zero;
        Outline(panel.transform, Accent);

        var title = MakeText(prt, "Title", "PRINT TO TAPE", 30, Accent, TextAlignmentOptions.Top);
        Box(title.rectTransform, TopCentre, TopCentre, new Vector2(0, -26), new Vector2(520, 38));
        title.characterSpacing = 14;

        _printSub = MakeText(prt, "Sub", "", 15, InkDim, TextAlignmentOptions.Top);
        Box(_printSub.rectTransform, TopCentre, TopCentre, new Vector2(0, -66), new Vector2(520, 22));
        _printSub.characterSpacing = 18;

        // THE MACHINE'S STATE, in one big line. This is the whole dialog now:
        // either there is a cassette in the slot or there isn't, and the player
        // reads which from here without going back out to look at the deck.
        _printSlotState = MakeText(prt, "SlotState", "", 26, Accent, TextAlignmentOptions.Center);
        Box(_printSlotState.rectTransform, TopCentre, TopCentre,
            new Vector2(0, -108), new Vector2(520, 40));
        _printSlotState.characterSpacing = 6;

        _printNote = MakeText(prt, "Note", "", 13, InkGhost, TextAlignmentOptions.Center);
        Box(_printNote.rectTransform, TopCentre, TopCentre, new Vector2(0, -190), new Vector2(520, 20));

        // Buttons, laid out from the centre outward.
        var cancel = MakePanel(prt, "Cancel", Panel);
        cancel.raycastTarget = true;
        Box(cancel.rectTransform, Centre, Centre, new Vector2(-92, -104), new Vector2(160, 44));
        Outline(cancel.transform, InkGhost);
        var cancelTxt = MakeText(cancel.rectTransform, "Label", "CANCEL", 17, InkDim,
                                 TextAlignmentOptions.Center);
        Stretch(cancelTxt.rectTransform, 0, 0, 0, 0);
        var cancelBtn = cancel.gameObject.AddComponent<Button>();
        cancelBtn.targetGraphic = cancel;
        cancelBtn.onClick.AddListener(ClosePrint);

        _printConfirm = MakePanel(prt, "Confirm", Ink);
        _printConfirm.raycastTarget = true;
        Box(_printConfirm.rectTransform, Centre, Centre, new Vector2(92, -104), new Vector2(160, 44));
        _printConfirmLabel = MakeText(_printConfirm.rectTransform, "Label", "PRINT", 17,
                                      Hex("04120eff"), TextAlignmentOptions.Center);
        Stretch(_printConfirmLabel.rectTransform, 0, 0, 0, 0);
        var okBtn = _printConfirm.gameObject.AddComponent<Button>();
        okBtn.targetGraphic = _printConfirm;
        okBtn.onClick.AddListener(DoPrint);

        _printPanel.SetActive(false);
    }

    // ── printing ─────────────────────────────────────────────────────────

    void OpenPrint()
    {
        if (_printPanel == null) return;
        _printPanel.SetActive(true);
        RefreshPrintDialog();
    }

    /// <summary>
    /// Everything the dialog says derives from three facts: whether the track
    /// has a name, what is in the SLOT, and whether the last tape is still
    /// sitting on the eject.
    ///
    /// Note the order of the checks — naming first, then the slot. A player who
    /// has neither should be told to save first, because that's the one they can
    /// fix without walking away from the screen.
    /// </summary>
    void RefreshPrintDialog()
    {
        if (_printSub == null) return;

        bool named = _project != null;
        bool loaded = CassetteDeck.HasCassette;
        bool blocked = CassetteDeck.HasEjected;
        int kind = CassetteDeck.InsertedKind;

        // The blank in the slot decides what gets pressed; a song longer than
        // the blank's bar cap is a hard blocker, not a fine-print surprise.
        int totalBars = _song != null ? _song.TotalBars() : 4;
        bool overCap = loaded && kind != TraxKind.Demo && totalBars > TraxKind.BarCap(kind);

        _printSub.text = named ? _project.name.ToUpperInvariant() : "UNNAMED TRACK";
        _printSub.color = named ? InkDim : Warn;

        if (!loaded)
        {
            _printSlotState.text = "PLEASE INSERT CASSETTE";
            _printSlotState.color = Warn;
        }
        else
        {
            _printSlotState.text = "READY TO PRINT  —  " + TraxKind.Label(kind) + " TAPE"
                                 + (CassetteDeck.InsertedTier >= 2 ? " II" : "");
            _printSlotState.color = Accent;
        }

        if (!named)
        {
            // A tape has to carry a name — that is what the alien remembers and
            // what shows in your hand. So saving comes first, always.
            _printNote.text = "save this project before pressing it to tape";
            _printNote.color = Warn;
        }
        else if (!loaded)
        {
            _printNote.text = "put a blank in the slot on the machine";
            _printNote.color = Warn;
        }
        else if (blocked)
        {
            _printNote.text = "take the last tape off the machine first";
            _printNote.color = Warn;
        }
        else if (overCap)
        {
            _printNote.text = "TRACK TOO LONG FOR THIS TAPE - " + totalBars + "/"
                            + TraxKind.BarCap(kind) + " BARS";
            _printNote.color = Warn;
        }
        else if (ProjectDirty)
        {
            // Not a blocker: pressing what is on the deck is legitimate. But it
            // will not be what the shelf holds, and you should know that.
            _printNote.text = "pressing UNSAVED changes";
            _printNote.color = Warn;
        }
        else if (kind == TraxKind.Demo)
        {
            _printNote.text = _song != null && _song.sections.Count > 1
                ? "DEMO blank - presses only SEC " + TraxSong.SectionLabel(_sel)
                  + " (" + _song.sections[_sel].bars + " bars)"
                : "one tape, then it ejects";
            _printNote.color = InkGhost;
        }
        else
        {
            _printNote.text = "presses the FULL TRACK - " + totalBars + "/"
                            + TraxKind.BarCap(kind) + " bars";
            _printNote.color = InkGhost;
        }

        bool canPrint = named && loaded && !blocked && !overCap;
        _printConfirm.color = canPrint ? Ink : Locked;
        _printConfirmLabel.color = canPrint ? Hex("04120eff") : InkGhost;
    }

    /// <summary>
    /// Freeze the track onto the seated blank and spit it out.
    ///
    /// The track pressed is WHAT IS ON THE DECK, not what the shelf holds — if
    /// they differ the dialog says so first. Freezing is what makes the tape
    /// independent of the project forever after.
    ///
    /// THE TIER COMES FROM THE SLOT. Nothing else is consulted: the blank was
    /// already spent when it went in, so there is no hotbar count to reconcile
    /// and no way for the number on screen to disagree with what's in the
    /// machine.
    ///
    /// On success the WHOLE COMPUTER CLOSES. That is deliberate — the tape is
    /// now a thing in the world, and the player has to look up from the screen
    /// and take it.
    /// </summary>
    void DoPrint()
    {
        if (_project == null) { Toast("SAVE THE PROJECT FIRST"); return; }
        if (!CassetteDeck.HasCassette) { Toast("PLEASE INSERT CASSETTE"); return; }
        if (CassetteDeck.HasEjected) { Toast("TAKE THE LAST TAPE OFF THE MACHINE"); return; }

        int kind = CassetteDeck.InsertedKind;
        // What actually gets pressed, built the same way whoever is at the
        // machine — the guest sends this song to the host rather than a shelf
        // id, so an unsaved edit prints exactly as it sounds.
        TraxSong pressSong;
        if (kind == TraxKind.Demo)
        {
            // The whole selected SECTION — its bar length, fill-bar ending —
            // not just the raw 4-bar loop (Sam, 2026-08-18).
            pressSong = new TraxSong();
            int bars = _song != null ? _song.sections[_sel].bars : 4;
            pressSong.sections.Add(new TraxSection(_inst.Track, bars));
        }
        else
        {
            if (_song == null || _song.TotalBars() > TraxKind.BarCap(kind))
            { Toast("TRACK TOO LONG FOR THIS TAPE"); return; }
            pressSong = _song;
        }

        // CO-OP: the machine is the host's. Close the screen either way so the
        // player looks up at it — the tape slides out when the deck state lands.
        if (TraxSync.RouteDeckPrint(_project.name, kind, pressSong))
        {
            ClosePrint();
            Close();
            return;
        }

        TraxPrints.Record press = TraxPrints.Register(_project.name, pressSong, kind,
                                                      CassetteDeck.InsertedTier);
        if (press == null) return;

        // CLOSE FIRST, THEN PRINT. The order is the feature: CassetteSlot plays
        // the tape sliding back out the moment the deck changes, and the player
        // has to be looking at the machine to see it. Printing first would run
        // the ejection behind a full-screen computer UI and the tape would just
        // turn up in their pocket.
        //
        // Nothing can fail between here and the deck call — both refusal cases
        // are checked above — so closing early cannot strand the UI.
        ClosePrint();
        Close();

        // PrintTo owns the transition: the slot empties and the tape becomes the
        // pending ejection in one step, so there is no frame in which the blank
        // has been consumed and no tape exists.
        CassetteDeck.PrintTo(press.id);
    }

    void ClosePrint()
    {
        if (_printPanel != null) _printPanel.SetActive(false);
    }

    public bool PrintOpen { get { return _printPanel != null && _printPanel.activeSelf; } }

    void Toast(string msg)
    {
        _toastLabel.text = msg;
        _toastGroup.alpha = 1f;
        _toastUntil = Time.unscaledTime + 1.8f;
    }

    // ── behaviour ────────────────────────────────────────────────────────

    void ShowHome()
    {
        _homeView.SetActive(true);
        _traxView.SetActive(false);
        if (_projectsView != null) _projectsView.SetActive(false);
        if (_inst != null) _inst.Stop();
        SyncPlayButton();
    }

    void ShowTrax()
    {
        _homeView.SetActive(false);
        if (_projectsView != null) _projectsView.SetActive(false);
        _traxView.SetActive(true);
        RefreshReadouts();
        RefreshRack();
        _lastBarShown = -1;
    }

    void OnKnobChanged(int index, double value)
    {
        _inst.SetDial(index, value);
        RefreshReadouts();
        _lastBarShown = -1;          // optional hits may have moved; redraw the grid
    }

    void ToggleModule(string name)
    {
        if (!_inst.IsInstalled(name)) return;         // locked slots are dead, not silently clickable
        _inst.SetModuleEnabled(name, !_inst.IsModuleEnabled(name));
        RefreshReadouts();                            // muting changes the track id
        RefreshRack();
        _lastBarShown = -1;
    }

    void RefreshRack()
    {
        foreach (var kv in _moduleFrames)
        {
            bool owned = _inst.IsInstalled(kv.Key);
            bool on = owned && _inst.IsModuleEnabled(kv.Key);
            kv.Value.color = !owned ? Locked : on ? InkDim : Grid;

            Image led;
            if (_moduleLeds.TryGetValue(kv.Key, out led))
                led.color = !owned ? Locked : on ? Ink : InkGhost;

            // A locked slot says it is not installed rather than describing a
            // part you cannot hear — it is the carrot for Tev's shop.
            TextMeshProUGUI nm, ds;
            if (_moduleNames.TryGetValue(kv.Key, out nm))
                nm.color = owned ? Ink : Locked;
            if (_moduleDescs.TryGetValue(kv.Key, out ds))
            {
                ds.text = owned ? _moduleDefaultDescs[kv.Key] : "NOT INSTALLED";
                ds.color = owned ? InkGhost : Locked;
            }
        }
        // A locked slot's steppers read LOCKED / -- rather than a part name.
        for (int i = 0; i < _steppers.Count; i++) _steppers[i].Refresh();
    }

    /// PLAY TRACK — the whole arrangement, from the play cursor.
    void TogglePlay()
    {
        if (_inst.IsPlayingSong)
        {
            _inst.Stop();
            ClearPlayhead();
            _lastStepShown = -1;
        }
        else
        {
            _inst.Stop();                 // a running LOOP SECTION yields
            ClearPlayhead();
            EnsureSongFresh();
            _inst.PlaySong();
        }
        SyncPlayButton();
    }

    /// LOOP SECTION — the selected section's 4-bar phrase, forever.
    void ToggleLoop()
    {
        if (_inst.IsPlayingLoop)
        {
            _inst.Stop();
            ClearPlayhead();
            _lastStepShown = -1;
        }
        else
        {
            _inst.Stop();
            ClearPlayhead();
            _inst.Play();
        }
        SyncPlayButton();
    }

    void SyncPlayButton()
    {
        if (_playLabel == null) return;
        bool song = _inst != null && _inst.IsPlayingSong;
        bool loop = _inst != null && _inst.IsPlayingLoop;
        _playLabel.text = song ? "STOP" : "PLAY TRACK";
        _playBg.color = song ? PanelHi : Ink;
        _playLabel.color = song ? Ink : Hex("04120eff");
        if (_loopLabel != null)
        {
            _loopLabel.text = loop ? "STOP" : "LOOP SECTION";
            _loopBg.color = loop ? Ink : PanelHi;
            _loopLabel.color = loop ? Hex("04120eff") : Ink;
        }
    }

    void RefreshReadouts()
    {
        // Every editing path funnels through here, so this is where the
        // selected section learns about the edit and the arranger recolours.
        SyncSection();
        RefreshArranger();

        var g = _inst.Genre;
        _genreLabel.text = g.label;
        _genreVibe.text = g.primary.vibe;
        _genreMeta.text = "TRACK " + _inst.TrackId.ToString("X8") + "\n" +
                          "MARGIN " + (g.d2 - g.d1).ToString("0.00") +
                          (g.blended ? "  BLEND" : "  LOCK");
        UpdateReadoutLine();
        RefreshProjectBar();
        if (_saveBg != null)
        {
            // The SAVE button carries the warning colour while there is
            // something unsaved, so the state is visible without reading the bar.
            var border = _saveBg.transform.Find("Border");
            if (border != null) border.GetComponent<Image>().color = ProjectDirty ? Warn : InkGhost;
        }
    }

    void UpdateReadoutLine()
    {
        int bar = _lastBarShown < 0 ? 0 : _lastBarShown;
        _readout.text = Mathf.RoundToInt((float)_inst.Params.bpm) + " BPM    BAR " +
                        (bar + 1) + "/" + TraxPhrase.Bars;
    }

    int _roSec = -1, _roBar = -1;      // song-readout change detection

    void ClearPlayhead()
    {
        for (int i = 0; i < _stepCells.Count; i++)
            _stepCells[i].color = Hex("0d1a1fff");
        _lastBarShown = -1;
        _roSec = -1; _roBar = -1;
        SetPlayingSec(-1);
    }

    void RefreshPlayhead()
    {
        int gstep = _inst.CurrentStep;
        if (gstep < 0) return;

        int bar, step;
        if (_inst.IsPlayingSong && _song != null)
        {
            // Song position -> section -> the phrase bar actually sounding
            // (the last bar of a section remaps onto the fill bar).
            int total = _song.TotalSteps();
            int pos = total > 0 ? ((gstep % total) + total) % total : 0;
            int idx, stepInSection, barInSection;
            _song.SectionAtStep(pos, out idx, out stepInSection, out barInSection);
            TraxSection sec = _song.sections[idx];
            int patStep = TraxSong.PatternStepFor(sec, stepInSection);
            bar = patStep / TraxPhrase.Steps;
            step = patStep % TraxPhrase.Steps;

            // The grid is the SELECTED section's editor — only light it when
            // the song is actually inside that section.
            bool inSelected = idx == _sel;

            if (bar != _lastBarShown && inSelected)
            {
                _lastBarShown = bar;
                _lastStepShown = -1;
                DrawBarHits(bar);
            }

            if (inSelected)
            {
                if (step != _lastStepShown)
                {
                    if (_lastStepShown >= 0) _stepCells[_lastStepShown].color = HitColor(bar, _lastStepShown);
                    _lastStepShown = step;
                    _stepCells[step].color = Accent;
                }
            }
            else if (_lastStepShown >= 0)
            {
                _stepCells[_lastStepShown].color = HitColor(_lastBarShown < 0 ? 0 : _lastBarShown, _lastStepShown);
                _lastStepShown = -1;
            }

            // Per-frame UI string, gated behind change detection (house rule).
            if (idx != _roSec || barInSection != _roBar)
            {
                _roSec = idx; _roBar = barInSection;
                _readout.text = Mathf.RoundToInt((float)TraxParams.Compute(sec.track.dials, sec.track.key).bpm)
                                + " BPM    SEC " + TraxSong.SectionLabel(idx)
                                + "  BAR " + (barInSection + 1) + "/" + sec.bars;
            }
            return;
        }

        bar = (gstep % TraxPhrase.TotalSteps) / TraxPhrase.Steps;
        step = gstep % TraxPhrase.Steps;

        if (bar != _lastBarShown)
        {
            _lastBarShown = bar;
            _lastStepShown = -1;
            DrawBarHits(bar);
            UpdateReadoutLine();
        }

        if (step != _lastStepShown)
        {
            if (_lastStepShown >= 0) _stepCells[_lastStepShown].color = HitColor(bar, _lastStepShown);
            _lastStepShown = step;
            _stepCells[step].color = Accent;
        }
    }

    void DrawBarHits(int bar)
    {
        for (int i = 0; i < _stepCells.Count; i++) _stepCells[i].color = HitColor(bar, i);
    }

    Color HitColor(int bar, int step)
    {
        var phrase = _inst.Phrase;
        for (int v = 0; v < TraxPhrase.VoiceCount; v++)
        {
            TraxVoice voice = (TraxVoice)v;
            if (!_inst.VoiceAudible(voice)) continue;
            if (phrase.Get(voice, bar, step).on) return InkGhost;
        }
        return Hex("0d1a1fff");
    }

    // ── UGUI helpers ─────────────────────────────────────────────────────

    static RectTransform MakeRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// A left arrow, a label and a right arrow. That is the entire vocabulary
    /// for choosing a part — which is the point: there are no wrong answers to
    /// pick from, so the player cannot make it sound bad.
    /// </summary>
    class Stepper
    {
        public TextMeshProUGUI label;
        public Func<string> read;
        public void Refresh() { if (label != null) label.text = read(); }
    }

    readonly List<Stepper> _steppers = new List<Stepper>();

    Stepper MakeStepper(RectTransform parent, string name, float y,
                        Func<string> read, Action<int> onStep, Color textColor)
    {
        var holder = MakePanel(parent, "Step_" + name, Hex("08161aff"));
        var rt = holder.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(-10, 28);
        rt.anchoredPosition = new Vector2(0, y);
        Outline(holder.transform, Grid);

        MakeArrow(rt, "Back", true, onStep);
        MakeArrow(rt, "Fwd", false, onStep);

        var label = MakeText(rt, "Label", "", 13, textColor, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 18, 18, 0, 0);

        var st = new Stepper { label = label, read = read };
        st.Refresh();
        return st;
    }

    /// <summary>
    /// Arrow drawn as a rotated triangle SPRITE, not a text glyph.
    /// ◀ and ▶ are missing from this project's TMP atlas, so as characters they
    /// rendered as the missing-glyph box — two squares where the arrows should
    /// be. Geometry has no font dependency.
    /// </summary>
    void MakeArrow(RectTransform parent, string name, bool left, Action<int> onStep)
    {
        // Transparent hit area, sized for a comfortable click target...
        var img = MakePanel(parent, name, new Color(0, 0, 0, 0));
        img.raycastTarget = true;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(left ? 0 : 1, 0);
        rt.anchorMax = new Vector2(left ? 0 : 1, 1);
        rt.pivot = new Vector2(left ? 0 : 1, 0.5f);
        rt.sizeDelta = new Vector2(22, 0);
        rt.anchoredPosition = Vector2.zero;

        // ...with a smaller triangle drawn inside it.
        var tri = MakeSprite(rt, "Tri", TraxUISprites.Triangle, InkDim);
        var trt = tri.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.5f);
        trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(9, 11);
        trt.anchoredPosition = Vector2.zero;
        if (left) trt.localEulerAngles = new Vector3(0, 0, 180);

        int delta = left ? -1 : 1;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(delegate { onStep(delta); });
    }

    void AfterPartChange()
    {
        for (int i = 0; i < _steppers.Count; i++) _steppers[i].Refresh();
        RefreshReadouts();
        _lastBarShown = -1;          // the pattern moved; redraw the step grid
    }

    static readonly Vector2 TopCentre = new Vector2(0.5f, 1);
    static readonly Vector2 TopLeft = new Vector2(0, 1);
    static readonly Vector2 TopRight = new Vector2(1, 1);
    static readonly Vector2 BottomLeft = new Vector2(0, 0);

    /// Place a fixed-size box against one corner. Unambiguous about where a
    /// label actually sits, unlike stretching four labels across one rect.
    static void Box(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    static void CentreSquare(RectTransform rt, float size, float yOffset)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(0, yOffset);
    }

    static Image MakePanel(Transform parent, string name, Color c)
    {
        return MakeSprite(parent, name, TraxUISprites.White, c);
    }

    static Image MakeSprite(Transform parent, string name, Sprite sprite, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// A 1px border as a single 9-sliced Image. One object, and recolouring the
    /// whole frame is one assignment — which RefreshRack relies on to light a
    /// rack module when it's switched on.
    /// </summary>
    static Image Outline(Transform parent, Color c)
    {
        var img = MakeSprite(parent, "Border", TraxUISprites.Border, c);
        img.type = Image.Type.Sliced;
        Stretch(img.rectTransform, 0, 0, 0, 0);
        return img;
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size,
                                    Color c, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    Image MakeButton(Transform parent, string name, string label, float width, ref float x,
                     Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
    {
        var img = MakePanel(parent, name, bg);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.sizeDelta = new Vector2(width, -10);
        rt.anchoredPosition = new Vector2(x, 0);
        x += width;

        img.raycastTarget = true;
        var txt = MakeText(rt, "Label", label, 18, fg, TextAlignmentOptions.Center);
        Stretch(txt.rectTransform, 0, 0, 0, 0);
        txt.characterSpacing = 12;

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        return img;
    }

    Image MakeButtonRight(Transform parent, string name, string label, float width, ref float rx,
                          Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
    {
        var img = MakePanel(parent, name, bg);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 0.5f);
        rt.sizeDelta = new Vector2(width, -10);
        rt.anchoredPosition = new Vector2(-rx, 0);
        rx += width + 6;

        img.raycastTarget = true;
        Outline(img.transform, InkGhost);
        var txt = MakeText(rt, "Label", label, 18, fg, TextAlignmentOptions.Center);
        Stretch(txt.rectTransform, 0, 0, 0, 0);

        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        return img;
    }
}
