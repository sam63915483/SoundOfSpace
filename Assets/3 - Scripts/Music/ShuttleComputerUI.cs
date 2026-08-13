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
public class ShuttleComputerUI : MonoBehaviour
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
    const float StatusH = 34f;
    const float ContentTop = StatusH + 12f;      // content starts BELOW the status bar
    const float ContentBottom = 16f;
    const float SidePad = 22f;

    const float PlateH = 116f;
    const float DialsY = -(PlateH + 14f);        // -130
    const float DialsH = 318f;
    const float RackLabelY = DialsY - DialsH - 16f;   // -482
    const float RackLabelH = 24f;
    const float RackY = RackLabelY - RackLabelH - 4f; // -510
    const float RackH = 200f;
    const float StepsY = RackY - RackH - 12f;    // -708
    const float StepsH = 18f;
    const float TransportH = 60f;

    // Resulting column, measured from the top of TraxView (height 878):
    //   status bar   (screen-space)     0 ..  -34
    //   TraxView starts at             -46          <- clears the status bar
    //   genre plate                      0 .. -116
    //   dials                         -130 .. -466
    //   rack label                    -482 .. -506
    //   rack                          -510 .. -696
    //   steps                         -708 .. -726
    //   transport (anchored bottom)   -818 .. -878
    // No overlaps, ~92px of breathing room above the transport. If you change a
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

    bool _prevModalFlag;
    CursorLockMode _prevCursorLock;
    bool _prevCursorVisible;

    TraxInstrument _inst;
    Canvas _canvas;
    GameObject _homeView, _traxView;

    TextMeshProUGUI _genreLabel, _genreVibe, _genreMeta, _readout, _statusText;
    TextMeshProUGUI _playLabel, _toastLabel;
    Image _playBg;
    readonly List<TraxKnob> _knobs = new List<TraxKnob>();
    readonly List<Image> _stepCells = new List<Image>();
    readonly Dictionary<string, Image> _moduleFrames = new Dictionary<string, Image>();
    readonly Dictionary<string, Image> _moduleLeds = new Dictionary<string, Image>();
    readonly Dictionary<string, TextMeshProUGUI> _moduleNames = new Dictionary<string, TextMeshProUGUI>();
    readonly Dictionary<string, TextMeshProUGUI> _moduleDescs = new Dictionary<string, TextMeshProUGUI>();
    readonly Dictionary<string, string> _moduleDefaultDescs = new Dictionary<string, string>();
    CanvasGroup _toastGroup;
    GameObject _printPanel;
    Stepper _printQty;

    int _quantity = 1;
    int _lastStepShown = -1;
    int _lastBarShown = -1;
    float _toastUntil;

    static TMP_FontAsset _font;

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

        ShowHome();
        _canvas.gameObject.SetActive(true);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;

        if (_inst != null) _inst.Stop();
        ClosePrint();
        _canvas.gameObject.SetActive(false);

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

        // The print dialog is modal: ESC dismisses it rather than the whole
        // computer, and the transport shortcut is suppressed while it is up.
        if (PrintOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) ClosePrint();
            return;
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
            Close();
            return;
        }

        if (_traxView != null && _traxView.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Space)) TogglePlay();
            RefreshPlayhead();
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

        BuildStatusBar(srt);
        BuildHome(srt);
        BuildTrax(srt);
        BuildCrtOverlay(srt);         // over the content, under the dialogs
        BuildToast(srt);
        BuildPrintDialog(srt);

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
        var wash = MakePanel(parent, "Wash", new Color(Ink.r, Ink.g, Ink.b, 0.035f));
        var wrt = wash.rectTransform;
        wrt.anchorMin = new Vector2(0, 1);
        wrt.anchorMax = new Vector2(1, 1);
        wrt.pivot = new Vector2(0.5f, 1);
        wrt.sizeDelta = new Vector2(0, 220);
        wrt.anchoredPosition = Vector2.zero;
    }

    void BuildStatusBar(RectTransform parent)
    {
        var bar = MakeRect(parent, "StatusBar");
        bar.anchorMin = new Vector2(0, 1);
        bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1);
        bar.sizeDelta = new Vector2(0, StatusH);
        bar.anchoredPosition = Vector2.zero;

        _statusText = MakeText(bar, "Left", "HOME", 15, InkDim, TextAlignmentOptions.Left);
        Stretch(_statusText.rectTransform, SidePad, 0, 0, 0);
        _statusText.characterSpacing = 14;

        var right = MakeText(bar, "Right", "SYS NOMINAL", 15, InkGhost, TextAlignmentOptions.Right);
        Stretch(right.rectTransform, 0, SidePad, 0, 0);
        right.characterSpacing = 14;

        var rule = MakePanel(bar, "Rule", Grid);
        var rr = rule.rectTransform;
        rr.anchorMin = new Vector2(0, 0);
        rr.anchorMax = new Vector2(1, 0);
        rr.pivot = new Vector2(0.5f, 0);
        rr.sizeDelta = new Vector2(0, 1);
        rr.anchoredPosition = Vector2.zero;
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
                btn.onClick.AddListener(ShowTrax);
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

        BuildGenrePlate(view);
        BuildDials(view);
        BuildRack(view);
        BuildTransport(view);
    }

    void BuildGenrePlate(RectTransform parent)
    {
        var plate = MakePanel(parent, "GenrePlate", PanelHi);
        var rt = plate.rectTransform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, PlateH);
        rt.anchoredPosition = Vector2.zero;
        Outline(plate.transform, Grid);

        // Explicit boxes rather than stretched-with-padding: four labels sharing
        // one rect via offsets is exactly how the caption and the vibe line
        // ended up on top of each other.
        var cap = MakeText(rt, "Caption", "GENRE", 14, InkGhost, TextAlignmentOptions.TopLeft);
        Box(cap.rectTransform, TopLeft, TopLeft, new Vector2(20, -10), new Vector2(320, 18));
        cap.characterSpacing = 32;

        _genreLabel = MakeText(rt, "Label", "—", 52, Accent, TextAlignmentOptions.TopLeft);
        Box(_genreLabel.rectTransform, TopLeft, TopLeft, new Vector2(18, -26), new Vector2(940, 66));
        _genreLabel.characterSpacing = 6;

        _genreVibe = MakeText(rt, "Vibe", "", 16, InkDim, TextAlignmentOptions.BottomLeft);
        Box(_genreVibe.rectTransform, BottomLeft, BottomLeft, new Vector2(20, 10), new Vector2(760, 22));
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

        _playBg = MakeButton(row, "Play", "PLAY", 130, ref x, Ink, Hex("04120eff"), TogglePlay);
        _playLabel = _playBg.GetComponentInChildren<TextMeshProUGUI>();

        _readout = MakeText(row, "Readout", "", 16, InkDim, TextAlignmentOptions.Left);
        var rrt = _readout.rectTransform;
        rrt.anchorMin = new Vector2(0, 0);
        rrt.anchorMax = new Vector2(0, 1);
        rrt.pivot = new Vector2(0, 0.5f);
        rrt.sizeDelta = new Vector2(260, 0);
        rrt.anchoredPosition = new Vector2(x + 14, 0);

        // Right-hand cluster, laid out from the right edge inward.
        float rx = 0;
        MakeButtonRight(row, "Exit", "EXIT", 110, ref rx, PanelHi, InkDim, Close);
        BuildPrintCluster(row, ref rx);
        BuildVolume(row, ref rx);
        BuildKey(row, ref rx);
    }

    void BuildPrintCluster(RectTransform row, ref float rx)
    {
        // One button. The quantity lives in the dialog it opens, so the
        // transport row is not carrying a stepper for something you press once.
        MakeButtonRight(row, "PrintDemo", "PRINT DEMO", 170, ref rx, PanelHi, Ink, OpenPrint);
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
    /// PRINT DEMO's dialog. Deliberately inert beyond choosing a number —
    /// cassettes are a later phase — but it is a real modal so the shape of the
    /// interaction is right when it does get wired up.
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

        var title = MakeText(prt, "Title", "PRINT DEMO", 30, Accent, TextAlignmentOptions.Top);
        Box(title.rectTransform, TopCentre, TopCentre, new Vector2(0, -26), new Vector2(520, 38));
        title.characterSpacing = 14;

        var sub = MakeText(prt, "Sub", "HOW MANY COPIES?", 15, InkDim, TextAlignmentOptions.Top);
        Box(sub.rectTransform, TopCentre, TopCentre, new Vector2(0, -68), new Vector2(520, 22));
        sub.characterSpacing = 18;

        var qtyBox = MakeRect(prt, "QtyBox");
        qtyBox.anchorMin = TopCentre;
        qtyBox.anchorMax = TopCentre;
        qtyBox.pivot = TopCentre;
        qtyBox.sizeDelta = new Vector2(240, 60);
        qtyBox.anchoredPosition = new Vector2(0, -104);

        _printQty = MakeStepper(qtyBox, "Qty", 0,
            () => _quantity.ToString(),
            d => { _quantity = Mathf.Clamp(_quantity + d, 1, 99); _printQty.Refresh(); },
            Ink);
        _printQty.label.fontSize = 30;

        var note = MakeText(prt, "Note", "no tape deck installed", 13, InkGhost,
                            TextAlignmentOptions.Center);
        Box(note.rectTransform, TopCentre, TopCentre, new Vector2(0, -178), new Vector2(520, 20));

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

        var ok = MakePanel(prt, "Confirm", Ink);
        ok.raycastTarget = true;
        Box(ok.rectTransform, Centre, Centre, new Vector2(92, -104), new Vector2(160, 44));
        var okTxt = MakeText(ok.rectTransform, "Label", "PRINT", 17, Hex("04120eff"),
                             TextAlignmentOptions.Center);
        Stretch(okTxt.rectTransform, 0, 0, 0, 0);
        var okBtn = ok.gameObject.AddComponent<Button>();
        okBtn.targetGraphic = ok;
        okBtn.onClick.AddListener(delegate
        {
            ClosePrint();
            Toast("PRINT x" + _quantity + " QUEUED — NO TAPE DECK INSTALLED");
        });

        _printPanel.SetActive(false);
    }

    void OpenPrint()
    {
        if (_printPanel == null) return;
        _printQty.Refresh();
        _printPanel.SetActive(true);
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
        _statusText.text = "HOME";
        if (_inst != null) _inst.Stop();
        SyncPlayButton();
    }

    void ShowTrax()
    {
        _homeView.SetActive(false);
        _traxView.SetActive(true);
        _statusText.text = "TRAX  •  SYNTH CORE";
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

    void TogglePlay()
    {
        _inst.Toggle();
        SyncPlayButton();
        if (!_inst.IsPlaying)
        {
            ClearPlayhead();
            _lastStepShown = -1;
        }
    }

    void SyncPlayButton()
    {
        if (_playLabel == null) return;
        bool playing = _inst != null && _inst.IsPlaying;
        _playLabel.text = playing ? "STOP" : "PLAY";
        _playBg.color = playing ? PanelHi : Ink;
        _playLabel.color = playing ? Ink : Hex("04120eff");
    }

    void RefreshReadouts()
    {
        var g = _inst.Genre;
        _genreLabel.text = g.label;
        _genreVibe.text = g.primary.vibe;
        _genreMeta.text = "TRACK " + _inst.TrackId.ToString("X8") + "\n" +
                          "MARGIN " + (g.d2 - g.d1).ToString("0.00") +
                          (g.blended ? "  BLEND" : "  LOCK");
        UpdateReadoutLine();
    }

    void UpdateReadoutLine()
    {
        int bar = _lastBarShown < 0 ? 0 : _lastBarShown;
        _readout.text = Mathf.RoundToInt((float)_inst.Params.bpm) + " BPM    BAR " +
                        (bar + 1) + "/" + TraxPhrase.Bars;
    }

    void ClearPlayhead()
    {
        for (int i = 0; i < _stepCells.Count; i++)
            _stepCells[i].color = Hex("0d1a1fff");
        _lastBarShown = -1;
    }

    void RefreshPlayhead()
    {
        int gstep = _inst.CurrentStep;
        if (gstep < 0) return;

        int bar = (gstep % TraxPhrase.TotalSteps) / TraxPhrase.Steps;
        int step = gstep % TraxPhrase.Steps;

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
