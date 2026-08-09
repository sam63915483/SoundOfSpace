using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The multiplayer screens on the main menu: the "play together?" prompt, the
/// host's lobby (code + password, going green when it opens), and the join
/// screen (four digits + password).
///
/// Built in code in the menu's galaxy language — GalaxyHudKit sprites, the
/// cyan↔magenta accent, the same card-with-glow shape the credits modal and the
/// save picker use — so it reads as part of the menu rather than a debug box.
///
/// Owns no networking. Everything here calls MultiplayerSession and renders
/// whatever state it reports back.
/// </summary>
public class MultiplayerMenuUI : MonoBehaviour
{
    public static MultiplayerMenuUI Instance { get; private set; }

    enum Screen { None, Ask, Host, Join }

    // Galaxy palette — same values MainMenuController declares.
    static readonly Color AccentCool  = new Color32(0x5B, 0xD8, 0xFF, 0xFF);
    static readonly Color AccentHot   = new Color32(0xC9, 0x4F, 0xFF, 0xFF);
    static readonly Color LiveGreen   = new Color32(0x57, 0xC4, 0x6E, 0xFF);
    static readonly Color BadRed      = new Color32(0xE0, 0x55, 0x55, 0xFF);
    static readonly Color LabelColor  = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
    static readonly Color DimColor    = new Color32(0xA8, 0xE6, 0xFF, 0xCC);
    static readonly Color FieldBg     = new Color32(0x0D, 0x08, 0x1F, 0xF2);
    static readonly Color ButtonNormal= new Color32(0x10, 0x08, 0x2E, 0xE0);
    static readonly Color ButtonHover = new Color32(0x7A, 0x42, 0xC8, 0xFF);
    static readonly Color Backdrop    = new Color32(0x00, 0x00, 0x00, 0xC8);

    Canvas _canvas;
    RectTransform _root, _card;
    Image _cardBorder;
    TextMeshProUGUI _title, _body, _codeLabel, _statusLabel, _rosterLabel;
    TMP_InputField _codeInput, _passInput;
    Button _primaryBtn, _secondaryBtn;
    TextMeshProUGUI _primaryLabel, _secondaryLabel;
    GameObject _codeRow, _passRow, _codeDisplay, _rosterBox;

    Screen _screen = Screen.None;
    System.Action _onSolo;      // "play on my own" — resumes the normal load
    bool _busy;
    string _lastRendered = "";

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        Hide();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── entry points ─────────────────────────────────────────────────────

    /// After a save is picked: offer to open it as a session. `onSolo` is the
    /// normal single-player continuation, so declining costs nothing.
    public void AskPlayTogether(System.Action onSolo)
    {
        _onSolo = onSolo;
        _screen = Screen.Ask;
        Show();
    }

    /// From the menu's MULTIPLAYER button — straight to the join screen.
    public void OpenJoin()
    {
        _onSolo = null;
        _screen = Screen.Join;
        Show();
    }

    public bool IsOpen => _canvas != null && _canvas.enabled;

    void Show()
    {
        if (_canvas != null) _canvas.enabled = true;
        _lastRendered = "";
        Render();
    }

    public void Hide()
    {
        if (_canvas != null) _canvas.enabled = false;
        _screen = Screen.None;
    }

    // ── rendering ────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsOpen) return;
        // Cheap change-detection: the session's state and roster are polled on a
        // timer, so only repaint when something the player can see has moved.
        var s = MultiplayerSession.Instance;
        string stamp = _screen + "|" + (s == null ? "" :
            s.Current + "|" + s.Code + "|" + s.Status + "|" + s.Roster.Count) + "|" + _busy;
        if (stamp == _lastRendered) return;
        _lastRendered = stamp;
        Render();
    }

    void Render()
    {
        var s = MultiplayerSession.Instance;
        bool open = s != null && s.Current == MultiplayerSession.State.LobbyOpen;
        bool waiting = s != null && s.Current == MultiplayerSession.State.WaitingForHost;
        bool failed = s != null && s.Current == MultiplayerSession.State.Failed;

        _cardBorder.color = open || waiting ? LiveGreen : AccentCool;

        switch (_screen)
        {
            case Screen.Ask:
                _title.text = "PLAY TOGETHER?";
                _body.text = "Open this run as a session your friends can drop into. "
                           + "You can still start on your own and let people join later.";
                SetRow(_codeRow, false); SetRow(_passRow, false);
                SetRow(_codeDisplay, false); SetRow(_rosterBox, false);
                SetButtons("OPEN A SESSION", "PLAY SOLO");
                _statusLabel.text = "";
                break;

            case Screen.Host:
                if (open)
                {
                    _title.text = "SESSION LIVE";
                    _body.text = "Share the code. You can start now and let people "
                               + "drop in later, or wait for them here.";
                    SetRow(_codeRow, false); SetRow(_passRow, false);
                    SetRow(_codeDisplay, true); SetRow(_rosterBox, true);
                    _codeLabel.text = s.Code;
                    _codeLabel.color = LiveGreen;
                    _rosterLabel.text = RosterText(s);
                    SetButtons("START GAME", "CANCEL SESSION");
                }
                else
                {
                    _title.text = "OPEN A SESSION";
                    _body.text = "Set a password if you want one — any length. Leave it "
                               + "blank and anyone with the code can join.";
                    SetRow(_codeRow, false); SetRow(_passRow, true);
                    SetRow(_codeDisplay, false); SetRow(_rosterBox, false);
                    SetButtons(_busy ? "OPENING…" : "START SESSION", "BACK");
                }
                break;

            case Screen.Join:
                if (waiting)
                {
                    _title.text = "IN THE LOBBY";
                    _body.text = "You're in. Waiting for the host to start.";
                    SetRow(_codeRow, false); SetRow(_passRow, false);
                    SetRow(_codeDisplay, true); SetRow(_rosterBox, true);
                    _codeLabel.text = s.Code;
                    _codeLabel.color = LiveGreen;
                    _rosterLabel.text = RosterText(s);
                    SetButtons("", "LEAVE");
                }
                else
                {
                    _title.text = "JOIN A SESSION";
                    _body.text = "Type the four-digit code. Leave the password blank if "
                               + "the host didn't set one.";
                    SetRow(_codeRow, true); SetRow(_passRow, true);
                    SetRow(_codeDisplay, false); SetRow(_rosterBox, false);
                    SetButtons(_busy ? "JOINING…" : "JOIN", "BACK");
                }
                break;
        }

        _statusLabel.text = s != null ? s.Status : "";
        _statusLabel.color = failed ? BadRed : (open || waiting ? LiveGreen : DimColor);
        _primaryBtn.interactable = !_busy && _primaryLabel.text.Length > 0;
        _primaryBtn.gameObject.SetActive(_primaryLabel.text.Length > 0);
    }

    static string RosterText(MultiplayerSession s)
    {
        if (s == null || s.Roster.Count == 0) return "Waiting for players…";
        var sb = new StringBuilder();
        for (int i = 0; i < s.Roster.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append("<color=#57C46E>●</color>  ").Append(s.Roster[i]);
        }
        return sb.ToString();
    }

    static void SetRow(GameObject go, bool on) { if (go != null) go.SetActive(on); }

    void SetButtons(string primary, string secondary)
    {
        _primaryLabel.text = primary;
        _secondaryLabel.text = secondary;
    }

    // ── actions ──────────────────────────────────────────────────────────

    async void OnPrimary()
    {
        var s = MultiplayerSession.Instance;
        if (s == null) return;

        switch (_screen)
        {
            case Screen.Ask:
                _screen = Screen.Host;
                _lastRendered = "";
                Render();
                break;

            case Screen.Host:
                if (s.Current == MultiplayerSession.State.LobbyOpen) { s.BeginGame(); return; }
                // No length rule — whatever you type is the password, and blank
                // means the session is open to anyone with the code.
                _busy = true; _lastRendered = ""; Render();
                await s.CreateSessionAsync(_passInput.text);
                _busy = false; _lastRendered = ""; Render();
                break;

            case Screen.Join:
                if (_codeInput.text.Trim().Length != 4)
                {
                    _statusLabel.text = "The code is four digits.";
                    _statusLabel.color = BadRed;
                    return;
                }
                _busy = true; _lastRendered = ""; Render();
                await s.JoinSessionAsync(_codeInput.text, _passInput.text);
                _busy = false; _lastRendered = ""; Render();
                break;
        }
    }

    void OnSecondary()
    {
        var s = MultiplayerSession.Instance;

        // Solo: close and hand back to the normal single-player load.
        if (_screen == Screen.Ask)
        {
            var go = _onSolo;
            _onSolo = null;
            Hide();
            go?.Invoke();
            return;
        }

        // Anything live gets torn down before we back out, so a cancelled lobby
        // never lingers on the service holding a code.
        if (s != null && s.Current != MultiplayerSession.State.Idle) s.CancelSession();

        if (_screen == Screen.Host && _onSolo != null) { _screen = Screen.Ask; _lastRendered = ""; Render(); return; }
        Hide();
    }

    // ── construction ─────────────────────────────────────────────────────

    void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // MUST override sorting. This canvas is a CHILD of the main menu's, and
        // a nested canvas ignores its own sortingOrder unless it overrides —
        // without this it silently inherits the menu's 100 and draws BEHIND the
        // save picker (which does override, at 2000). Same reason the credits
        // modal sets it.
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = 2100;   // above UILayer.SaveDialog (2000)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _root = NewUI("Root", transform);
        Stretch(_root);
        var dim = _root.gameObject.AddComponent<Image>();
        dim.color = Backdrop;
        dim.raycastTarget = true;

        _card = NewUI("Card", _root);
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(760f, 560f);

        var glow = NewUI("Glow", _card);
        Stretch(glow, -32f);
        var glowImg = glow.gameObject.AddComponent<Image>();
        glowImg.sprite = GalaxyHudKit.GlowSprite();
        glowImg.type = Image.Type.Sliced;
        glowImg.color = new Color(0.43f, 0.50f, 1f, 0.35f);
        glowImg.raycastTarget = false;

        _cardBorder = _card.gameObject.AddComponent<Image>();
        _cardBorder.sprite = GalaxyHudKit.RoundedSprite();
        _cardBorder.type = Image.Type.Sliced;
        _cardBorder.color = AccentCool;

        var bg = NewUI("BG", _card);
        Stretch(bg, -3f);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = GalaxyHudKit.NebulaSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.raycastTarget = false;

        _title = Text(_card, "Title", "", 40f, FontStyles.Bold, LabelColor, TextAlignmentOptions.Center);
        Place(_title.rectTransform, new Vector2(0f, -34f), new Vector2(680f, 52f));
        _title.characterSpacing = 10f;
        _title.enableVertexGradient = true;
        _title.colorGradient = new VertexGradient(AccentCool, AccentHot, AccentCool, AccentHot);

        _body = Text(_card, "Body", "", 20f, FontStyles.Normal, DimColor, TextAlignmentOptions.Top);
        Place(_body.rectTransform, new Vector2(0f, -96f), new Vector2(640f, 72f));
        _body.enableWordWrapping = true;

        // Big code readout (host live / guest waiting)
        _codeDisplay = Text(_card, "CodeDisplay", "0000", 72f, FontStyles.Bold, LiveGreen, TextAlignmentOptions.Center).gameObject;
        _codeLabel = _codeDisplay.GetComponent<TextMeshProUGUI>();
        Place(_codeLabel.rectTransform, new Vector2(0f, -180f), new Vector2(640f, 90f));
        _codeLabel.characterSpacing = 24f;

        _rosterBox = Text(_card, "Roster", "", 20f, FontStyles.Normal, LabelColor, TextAlignmentOptions.Top).gameObject;
        _rosterLabel = _rosterBox.GetComponent<TextMeshProUGUI>();
        Place(_rosterLabel.rectTransform, new Vector2(0f, -286f), new Vector2(640f, 110f));

        _codeRow = Field(_card, "CODE", new Vector2(0f, -186f), out _codeInput, 4,
                         TMP_InputField.ContentType.IntegerNumber, 44f, 14f);
        _passRow = Field(_card, "PASSWORD  (OPTIONAL)", new Vector2(0f, -286f), out _passInput, 64,
                         TMP_InputField.ContentType.Password, 28f, 4f);

        _statusLabel = Text(_card, "Status", "", 19f, FontStyles.Italic, DimColor, TextAlignmentOptions.Center);
        Place(_statusLabel.rectTransform, new Vector2(0f, -404f), new Vector2(660f, 56f));
        _statusLabel.enableWordWrapping = true;

        _primaryBtn = Btn(_card, "Primary", new Vector2(-160f, -480f), new Vector2(280f, 60f), OnPrimary, out _primaryLabel);
        _secondaryBtn = Btn(_card, "Secondary", new Vector2(160f, -480f), new Vector2(280f, 60f), OnSecondary, out _secondaryLabel);
    }

    // ── small builders (menu house style) ────────────────────────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-inset, -inset);
        rt.offsetMax = new Vector2(inset, inset);
    }

    /// Top-anchored placement — every y in Build reads as "pixels down from the
    /// top of the card", matching the rest of the menu's builders.
    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    static TextMeshProUGUI Text(Transform parent, string name, string text, float size,
                                FontStyles style, Color color, TextAlignmentOptions align)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style;
        t.color = color; t.alignment = align; t.raycastTarget = false;
        t.enableWordWrapping = false;
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) t.font = font;
        return t;
    }

    GameObject Field(Transform parent, string caption, Vector2 pos, out TMP_InputField input,
                     int limit, TMP_InputField.ContentType content, float fontSize, float spacing)
    {
        var row = NewUI("Field_" + caption, parent);
        Place(row, pos, new Vector2(640f, 96f));

        var cap = Text(row, "Caption", caption, 16f, FontStyles.Bold, DimColor, TextAlignmentOptions.Left);
        Place(cap.rectTransform, new Vector2(0f, 0f), new Vector2(640f, 22f));
        cap.characterSpacing = 8f;

        var boxRT = NewUI("Box", row);
        Place(boxRT, new Vector2(0f, -26f), new Vector2(640f, 62f));
        var boxImg = boxRT.gameObject.AddComponent<Image>();
        boxImg.sprite = GalaxyHudKit.RoundedSprite();
        boxImg.type = Image.Type.Sliced;
        boxImg.color = FieldBg;

        var textRT = NewUI("Text", boxRT);
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(16f, 6f); textRT.offsetMax = new Vector2(-16f, -6f);
        var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize; tmp.color = LabelColor; tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = spacing;
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) tmp.font = font;

        input = boxRT.gameObject.AddComponent<TMP_InputField>();
        input.textComponent = tmp;
        input.textViewport = textRT;
        input.characterLimit = limit;
        input.contentType = content;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return row.gameObject;
    }

    Button Btn(Transform parent, string name, Vector2 pos, Vector2 size,
               UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI label)
    {
        var rt = NewUI(name, parent);
        Place(rt, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = GalaxyHudKit.RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = ButtonNormal;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var c = btn.colors;
        c.normalColor = ButtonNormal; c.highlightedColor = ButtonHover;
        c.pressedColor = ButtonHover; c.selectedColor = ButtonHover;
        c.fadeDuration = 0.12f;
        btn.colors = c;
        btn.onClick.AddListener(onClick);
        UiSfxPlayer.Attach(btn);

        label = Text(rt, "Label", "", 24f, FontStyles.Bold, LabelColor, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        label.characterSpacing = 6f;
        return btn;
    }
}
