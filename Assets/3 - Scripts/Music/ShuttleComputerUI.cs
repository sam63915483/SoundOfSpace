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
    TextMeshProUGUI _playLabel, _qtyLabel, _toastLabel;
    Image _playBg;
    readonly List<TraxKnob> _knobs = new List<TraxKnob>();
    readonly List<Image> _stepCells = new List<Image>();
    readonly Dictionary<string, Image> _moduleFrames = new Dictionary<string, Image>();
    readonly Dictionary<string, Image> _moduleLeds = new Dictionary<string, Image>();
    CanvasGroup _toastGroup;

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

        var bg = MakePanel(canvasGo.transform, "Backdrop", Bg);
        Stretch(bg.rectTransform, 0, 0, 0, 0);

        // The screen area. 4:3-ish, centred, so it reads as a monitor rather
        // than as a game menu that happens to be fullscreen.
        var screen = MakePanel(bg.transform, "Screen", Bg);
        var srt = screen.rectTransform;
        srt.anchorMin = new Vector2(0.5f, 0.5f);
        srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(1500, 940);
        srt.anchoredPosition = Vector2.zero;

        BuildStatusBar(srt);
        BuildHome(srt);
        BuildTrax(srt);
        BuildToast(srt);

        _canvas.gameObject.SetActive(false);
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    void BuildStatusBar(RectTransform parent)
    {
        var bar = MakeRect(parent, "StatusBar");
        Stretch(bar, 0, 0, 0, 0);
        bar.anchorMin = new Vector2(0, 1);
        bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1);
        bar.sizeDelta = new Vector2(0, 34);
        bar.anchoredPosition = Vector2.zero;

        _statusText = MakeText(bar, "Left", "HOME", 16, InkDim, TextAlignmentOptions.Left);
        Stretch(_statusText.rectTransform, 16, 0, 0, 0);

        var right = MakeText(bar, "Right", "SYS NOMINAL", 16, InkGhost, TextAlignmentOptions.Right);
        Stretch(right.rectTransform, 0, 16, 0, 0);

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

    static readonly AppDef[] Apps =
    {
        new AppDef("TRAX",  "♫", true),
        new AppDef("MAIL",  "✉", false),
        new AppDef("BANK",  "▤", false),
        new AppDef("RADIO", "◉", false)
    };

    void BuildHome(RectTransform parent)
    {
        var view = MakeRect(parent, "HomeView");
        Stretch(view, 0, 0, 0, 44);
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

            var glyph = MakeText(frame.rectTransform, "Glyph", app.glyph, 54,
                                 app.enabled ? Ink : Locked, TextAlignmentOptions.Center);
            var grt = glyph.rectTransform;
            Stretch(grt, 0, 0, 0, 0);
            grt.anchoredPosition = new Vector2(0, 20);

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
        Stretch(view, 12, 12, 12, 44);
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
        rt.sizeDelta = new Vector2(0, 104);
        rt.anchoredPosition = Vector2.zero;
        Outline(plate.transform, Grid);

        // The caption Sam asked for — without it the big magenta word is just a
        // word changing on screen with nothing saying what it means.
        var cap = MakeText(rt, "Caption", "GENRE", 14, InkGhost, TextAlignmentOptions.TopLeft);
        Stretch(cap.rectTransform, 18, 0, 10, 0);
        cap.characterSpacing = 30;

        _genreLabel = MakeText(rt, "Label", "—", 46, Accent, TextAlignmentOptions.TopLeft);
        Stretch(_genreLabel.rectTransform, 16, 0, 30, 0);

        _genreVibe = MakeText(rt, "Vibe", "", 16, InkDim, TextAlignmentOptions.BottomLeft);
        Stretch(_genreVibe.rectTransform, 18, 0, 0, 12);

        _genreMeta = MakeText(rt, "Meta", "", 14, InkGhost, TextAlignmentOptions.BottomRight);
        Stretch(_genreMeta.rectTransform, 0, 18, 0, 12);
    }

    void BuildDials(RectTransform parent)
    {
        var row = MakeRect(parent, "Dials");
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.sizeDelta = new Vector2(0, 250);
        row.anchoredPosition = new Vector2(0, -114);

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

            var name = MakeText(crt, "Name", defs[i].label, 16, Ink, TextAlignmentOptions.Top);
            Stretch(name.rectTransform, 0, 0, 10, 0);
            name.characterSpacing = 14;

            // Arc track + fill. Rotated -135° so the sweep starts lower-left.
            var track = MakeSprite(crt, "Track", TraxUISprites.Ring, Grid);
            CentreSquare(track.rectTransform, 128, 6);
            track.type = Image.Type.Filled;
            track.fillMethod = Image.FillMethod.Radial360;
            track.fillOrigin = (int)Image.Origin360.Top;
            track.fillClockwise = true;
            track.fillAmount = TraxKnob.Sweep;
            track.rectTransform.localEulerAngles = new Vector3(0, 0, -TraxKnob.StartAngle);

            var fill = MakeSprite(crt, "Fill", TraxUISprites.Ring, Ink);
            CentreSquare(fill.rectTransform, 128, 6);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            fill.rectTransform.localEulerAngles = new Vector3(0, 0, -TraxKnob.StartAngle);

            var hub = MakeSprite(crt, "Hub", TraxUISprites.Disc, PanelHi);
            CentreSquare(hub.rectTransform, 74, 6);

            // Pointer pivots at its bottom so rotation swings it around the hub.
            var pointer = MakeSprite(crt, "Pointer", TraxUISprites.White, Accent);
            var prt = pointer.rectTransform;
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(3, 34);
            prt.anchoredPosition = new Vector2(0, 6);

            var val = MakeText(crt, "Value", "0.0", 20, Ink, TextAlignmentOptions.Bottom);
            var vrt = val.rectTransform;
            vrt.anchorMin = new Vector2(0, 0);
            vrt.anchorMax = new Vector2(1, 0);
            vrt.pivot = new Vector2(0.5f, 0);
            vrt.sizeDelta = new Vector2(0, 26);
            vrt.anchoredPosition = new Vector2(0, 34);

            var flav = MakeText(crt, "Flavor", defs[i].flavor, 12, InkGhost, TextAlignmentOptions.Bottom);
            var frt = flav.rectTransform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 0);
            frt.pivot = new Vector2(0.5f, 0);
            frt.sizeDelta = new Vector2(-8, 32);
            frt.anchoredPosition = new Vector2(0, 4);

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
        lrt.sizeDelta = new Vector2(0, 22);
        lrt.anchoredPosition = new Vector2(0, -372);
        label.characterSpacing = 24;

        var row = MakeRect(parent, "Rack");
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.sizeDelta = new Vector2(0, 116);
        row.anchoredPosition = new Vector2(0, -396);

        var mods = TraxInstrument.Modules;
        for (int i = 0; i < mods.Length; i++)
        {
            var m = mods[i];
            float w = 1f / mods.Length;
            var cell = MakePanel(row, "Mod_" + i, m.locked ? Panel : PanelHi);
            cell.raycastTarget = !m.locked;
            var crt = cell.rectTransform;
            crt.anchorMin = new Vector2(i * w, 0);
            crt.anchorMax = new Vector2((i + 1) * w, 1);
            crt.offsetMin = new Vector2(5, 0);
            crt.offsetMax = new Vector2(-5, 0);

            var frame = Outline(cell.transform, m.locked ? Hex("17242aff") : InkDim);

            var led = MakeSprite(crt, "Led", TraxUISprites.Disc,
                                 m.locked ? Locked : Ink);
            var lrt2 = led.rectTransform;
            lrt2.anchorMin = new Vector2(0.5f, 1);
            lrt2.anchorMax = new Vector2(0.5f, 1);
            lrt2.pivot = new Vector2(0.5f, 1);
            lrt2.sizeDelta = new Vector2(14, 14);
            lrt2.anchoredPosition = new Vector2(0, -16);

            var nm = MakeText(crt, "Name", m.name, 16, m.locked ? Locked : Ink,
                              TextAlignmentOptions.Center);
            Stretch(nm.rectTransform, 0, 0, 40, 30);
            nm.characterSpacing = 12;

            var ds = MakeText(crt, "Desc", m.desc, 12, m.locked ? Locked : InkGhost,
                              TextAlignmentOptions.Bottom);
            var drt = ds.rectTransform;
            drt.anchorMin = new Vector2(0, 0);
            drt.anchorMax = new Vector2(1, 0);
            drt.pivot = new Vector2(0.5f, 0);
            drt.sizeDelta = new Vector2(0, 24);
            drt.anchoredPosition = new Vector2(0, 8);

            if (!m.locked)
            {
                _moduleFrames[m.name] = frame;
                _moduleLeds[m.name] = led;
                string captured = m.name;
                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = cell;
                btn.onClick.AddListener(delegate { ToggleModule(captured); });
            }
        }

        // Step lights.
        var steps = MakeRect(parent, "Steps");
        steps.anchorMin = new Vector2(0, 1);
        steps.anchorMax = new Vector2(1, 1);
        steps.pivot = new Vector2(0.5f, 1);
        steps.sizeDelta = new Vector2(0, 14);
        steps.anchoredPosition = new Vector2(0, -520);

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
        row.sizeDelta = new Vector2(0, 56);
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
    }

    void BuildPrintCluster(RectTransform row, ref float rx)
    {
        MakeButtonRight(row, "Print", "PRINT", 120, ref rx, PanelHi, Ink, delegate
        {
            // Deliberately inert — cassettes are a later phase. Saying so is
            // better than a dead button.
            Toast("PRINT x" + _quantity + " — NO TAPE DECK INSTALLED");
        });

        MakeButtonRight(row, "Plus", "+", 42, ref rx, PanelHi, InkDim, delegate
        {
            _quantity = Mathf.Min(99, _quantity + 1);
            _qtyLabel.text = _quantity.ToString();
        });

        var qty = MakeText(row, "Qty", "1", 20, Ink, TextAlignmentOptions.Center);
        var qrt = qty.rectTransform;
        qrt.anchorMin = new Vector2(1, 0);
        qrt.anchorMax = new Vector2(1, 1);
        qrt.pivot = new Vector2(1, 0.5f);
        qrt.sizeDelta = new Vector2(44, 0);
        qrt.anchoredPosition = new Vector2(-rx, 0);
        rx += 44;
        _qtyLabel = qty;

        MakeButtonRight(row, "Minus", "-", 42, ref rx, PanelHi, InkDim, delegate
        {
            _quantity = Mathf.Max(1, _quantity - 1);
            _qtyLabel.text = _quantity.ToString();
        });
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
        _lastBarShown = -1;          // pattern may have changed; redraw the grid
    }

    void ToggleModule(string name)
    {
        _inst.SetModuleEnabled(name, !_inst.IsModuleEnabled(name));
        RefreshRack();
        _lastBarShown = -1;
    }

    void RefreshRack()
    {
        foreach (var kv in _moduleFrames)
        {
            bool on = _inst.IsModuleEnabled(kv.Key);
            kv.Value.color = on ? InkDim : Grid;
            Image led;
            if (_moduleLeds.TryGetValue(kv.Key, out led))
                led.color = on ? Ink : InkGhost;
        }
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
        _genreMeta.text = "SEED " + _inst.Seed.ToString("X8") + "\n" +
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
