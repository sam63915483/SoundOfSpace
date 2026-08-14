using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tev's shop — blanks and rack modules, as a real panel instead of a dialogue
/// tree. Ported from prototypes/tev-shop (Sam picked layout "F").
///
/// ── Why this replaced the dialogue tree ──────────────────────────────────
/// The old shop was a list of PostGreetingChoicePanel rows. Every purchase was
/// one click plus one spoken line, so buying ten blanks was ten clicks and ten
/// lines of Tev talking — and a $180 permanent unlock got exactly the same row
/// as a $5 consumable. TevMushroomOnboarding used to argue the shop should stay
/// in the conversation because "he's a bloke on a lawn, not a storefront". That
/// was a fair call when he sold two things; it stopped being one at six.
///
/// ── The three rules the mockups settled ──────────────────────────────────
///  • <b>TWO TABS.</b> Blanks and modules are not the same decision — one is a
///    handful you grab on the way out, the other is a set of six you are
///    completing — so they get a screen each. Sam's pick, over showing both at
///    once: half the rows on screen at a time is the whole point.
///  • <b>EVERY ROW BUYS ITSELF.</b> There is no basket, no running total and no
///    PAY step. The first mockup had one and it was the clutter; it also
///    invited a real bug, because tapes and modules were added in different
///    places and each half could look affordable while the total was not.
///  • <b>MODULES NEVER GET A QUANTITY STEPPER.</b> A stepper on something you
///    can only own once reads as a bug. They get BUY, or INSTALLED.
///
/// Prices live in <see cref="Stock"/> here rather than on the NPC, so "what
/// does Tev sell" has exactly one answer. The ladder's order is load-bearing —
/// see the comment on the array.
/// </summary>
public class TevShopUI : MonoBehaviour
{
    public static TevShopUI Instance { get; private set; }

    // Palette is MushroomSellUI's, so the two vendor panels read as one game.
    static readonly Color32 C_Bg      = new Color32(10, 24, 40, 244);
    static readonly Color32 C_Border  = new Color32(120, 200, 255, 220);
    static readonly Color32 C_Header  = new Color32(226, 120, 126, 255);
    static readonly Color32 C_Label   = new Color32(234, 246, 255, 255);
    static readonly Color32 C_Dim     = new Color32(127, 160, 189, 255);
    static readonly Color32 C_Dimmer  = new Color32(75, 109, 140, 255);
    static readonly Color32 C_Value   = new Color32(255, 215, 50, 255);
    static readonly Color32 C_SlotBg  = new Color32(8, 19, 31, 255);
    static readonly Color32 C_SlotEdge= new Color32(36, 66, 95, 255);
    static readonly Color32 C_Buy     = new Color32(60, 145, 70, 255);
    static readonly Color32 C_BuyOff  = new Color32(27, 50, 67, 255);
    static readonly Color32 C_Back    = new Color32(140, 60, 60, 255);
    static readonly Color32 C_Phos    = new Color32(0x79, 0xFF, 0xD0, 0xFF);
    static readonly Color32 C_Mag     = new Color32(0xFF, 0x4F, 0xD8, 0xFF);
    static readonly Color32 C_Err     = new Color32(255, 110, 110, 255);

    // ── geometry ──────────────────────────────────────────────────────────
    //
    // COORDINATE CONVENTION, same trap the sell panel fell into once: Panel()
    // and Txt() anchor to the parent's TOP edge with pivot top-centre, so a
    // child's y is "pixels DOWN from the top of the parent" and is NEGATIVE.
    // Centre-relative maths here puts things ABOVE the box they belong to.
    const float PanelW   = 760f;
    const float PanelH   = 500f;
    const float RowW     = PanelW - 48f;   // 712
    const float RowH     = 46f;
    const float RowPitch = 51f;
    const float RowTop   = 104f;           // y of the first row, downward
    const int   MaxRows  = 6;              // the rack is the longest tab

    /// <summary>What Tev sells. One entry per row, in the order they appear.</summary>
    public struct Entry
    {
        public string name;          // row label
        public string desc;          // one short line about what it is
        public int price;
        public string plugin;        // non-null for a rack module
        public Hotbar.ItemId item;   // used when plugin is null
        public Color32 chip;
        public bool preInstalled;    // the two you land with; shown, never sold
    }

    /// <summary>
    /// The catalogue. Prices are the 2026-08-14 rebalance
    /// (docs/Plan_MoneyRevamp_v1.md).
    ///
    /// THE PLUGIN LADDER'S ORDER IS LOAD-BEARING. Each rung costs about EIGHT
    /// TAPES at the income the previous rung unlocks, and that rhythm only holds
    /// if the player buys them cheapest-first — which they will, because the
    /// list is sorted. Reordering or repricing one means re-checking the rest.
    /// </summary>
    public static readonly Entry[] Stock =
    {
        new Entry { name = "Type 1", desc = "Ordinary stock.", price = 5,
                    item = Hotbar.ItemId.BlankTapeT1, chip = new Color32(0x79, 0xFF, 0xD0, 0xFF) },
        new Entry { name = "Type 2", desc = "Worth double when you sell it.", price = 15,
                    item = Hotbar.ItemId.BlankTapeT2, chip = new Color32(0xFF, 0x4F, 0xD8, 0xFF) },

        new Entry { name = "THUMPER", desc = "Drums. Kick, snare and hat.",   plugin = "THUMPER", preInstalled = true },
        new Entry { name = "GLOWORM", desc = "Bass. The line under it all.",  plugin = "GLOWORM", preInstalled = true },
        new Entry { name = "SIREN",   desc = "Lead. A generated melody.",     plugin = "SIREN",   price = 60 },
        new Entry { name = "MOSS",    desc = "Pads. Sets what else follows.", plugin = "MOSS",    price = 90 },
        new Entry { name = "SPINDLE", desc = "Arp. Rolling sequences.",       plugin = "SPINDLE", price = 130 },
        new Entry { name = "CAVE",    desc = "Space. Reverb and delay.",      plugin = "CAVE",    price = 180 },
    };

    static bool IsTape(in Entry e) => e.plugin == null;

    enum Tab { Tapes, Plugins }

    // ── scene refs ────────────────────────────────────────────────────────
    Canvas _canvas;
    RectTransform _panelRT;
    GameObject _dim;
    TextMeshProUGUI _header, _status, _tally;
    Image _tapesUnderline, _pluginsUnderline;
    TextMeshProUGUI _tapesTabLabel, _pluginsTabLabel;
    RowWidget[] _rows = new RowWidget[MaxRows];

    bool _open;
    Tab _tab = Tab.Tapes;
    Action _onClose;
    string _pluginLine, _noRoomLine;

    /// Pending quantity per BLANK, keyed by index into Stock. Not a basket —
    /// nothing is owed until the row's own BUY is pressed, which is exactly why
    /// there is no way to overdraw across the two tabs.
    readonly int[] _qty = new int[8];

    class RowWidget
    {
        public RectTransform root;
        public Image bg, border, chip;
        public TextMeshProUGUI name, desc, price, qty, buyLabel, installed;
        public Button minus, plus, buy;
        public RectTransform stepper;
        public int stockIndex = -1;
    }

    // ── lifecycle ─────────────────────────────────────────────────────────
    //
    // No MainMenu early-return, and therefore nothing to seed in
    // MainMenuController.EnsureGameplaySingletons: this mirrors MushroomSellUI,
    // which creates on every scene and lets HUDSceneGate disable the canvas on
    // the menu. Trap #1 in CLAUDE.md only bites auto-singletons that skip
    // MainMenu, and this one does not.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("TevShopUI");
        DontDestroyOnLoad(go);
        go.AddComponent<TevShopUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    /// A scene change kills the conversation this panel belonged to. Same
    /// reasoning as the sell panel: leaving it up is visible, but the worse half
    /// is invisible — PlayerController.isInModalSlotUI is a STATIC, so a panel
    /// that never closed locks the player out of their controls in the next run.
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        if (_open) Close();
    }

    public bool IsOpen => _open;

    // Static forms for TabbedPauseMenu's "don't open pause over a modal" guard.
    // ConsumedEscapeThisFrame exists because both Updates run in the same frame
    // in an undefined order: without it the Esc that closes this panel would
    // also pop the pause menu on top of the frame it closed.
    public static bool AnyOpen => Instance != null && Instance._open;
    static int s_escFrame = -1;
    public static bool ConsumedEscapeThisFrame => s_escFrame == Time.frameCount;

    /// <summary>
    /// Open the shop. <paramref name="pluginLine"/> and <paramref name="noRoomLine"/>
    /// are Tev's own authored lines, passed in from the NPC so his voice survives
    /// the move out of the conversation.
    ///
    /// His blank-bought line is deliberately NOT taken: it reads "One {item}",
    /// which was true when a purchase was one tape and is wrong now that a row
    /// buys a stack. The panel writes that one itself so it can count.
    /// </summary>
    public void Open(Action onClose, string pluginLine = null, string noRoomLine = null)
    {
        _onClose = onClose;
        _pluginLine = pluginLine;
        _noRoomLine = noRoomLine;
        _open = true;
        _tab = Tab.Tapes;
        for (int i = 0; i < _qty.Length; i++) _qty[i] = 0;

        if (_dim != null) _dim.SetActive(true);
        _panelRT.gameObject.SetActive(true);
        PlayerController.isInModalSlotUI = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetStatus("Take your time.", C_Dimmer);
        Refresh();
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        if (_dim != null) _dim.SetActive(false);
        _panelRT.gameObject.SetActive(false);
        PlayerController.isInModalSlotUI = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        var cb = _onClose;
        _onClose = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (!_open) return;

        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            s_escFrame = Time.frameCount;
            Close();
            return;
        }

        // Bumpers flip tabs, for a controller. DELIBERATELY NOT TAB, which is
        // the obvious keyboard choice and already bound: TutorialGate reads it
        // raw as "tutorial advance", so a press meant for this panel would also
        // step a tutorial running behind it. The tabs are clickable and this
        // modal already unlocks the cursor, so there is nothing to gain by
        // fighting for the key.
        if (TutorialGate.PadPressed(TutorialGate.PadButton.LB)
         || TutorialGate.PadPressed(TutorialGate.PadButton.RB))
        {
            _tab = _tab == Tab.Tapes ? Tab.Plugins : Tab.Tapes;
            Refresh();
        }

        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }

    // ── money ─────────────────────────────────────────────────────────────
    static int Money => PlayerWallet.Instance != null ? PlayerWallet.Instance.Money : 0;

    /// Can this blank's stepper go up? Bounded by the stack cap AND the purse,
    /// so the number on screen is always a number you could actually pay for.
    bool CanAdd(int i)
    {
        Entry e = Stock[i];
        if (!IsTape(e)) return false;
        int cap = Hotbar.StackMax(e.item);
        return _qty[i] < cap && (_qty[i] + 1) * e.price <= Money;
    }

    int LineCost(int i) => Stock[i].price * _qty[i];

    // ── buying ────────────────────────────────────────────────────────────

    void BuyTapes(int i)
    {
        Entry e = Stock[i];
        int want = _qty[i];
        if (want <= 0) return;

        var hb = Hotbar.Instance;
        if (hb == null || PlayerWallet.Instance == null) return;

        // AFFORDABILITY IS CLAMPED BEFORE ANYTHING IS HANDED OVER, never undone
        // afterwards. The stepper already bounds the quantity by the purse, so
        // this should be a no-op — but the hotbar has no public "give one back",
        // so a charge that failed AFTER the tapes were added would have no way
        // to unwind. Clamping first means that state cannot arise.
        if (e.price > 0) want = Mathf.Min(want, Money / e.price);
        if (want <= 0) { SetStatus("You can't afford that.", C_Err); return; }

        // CHARGE ONLY FOR WHAT FITS. Carried over from the dialogue shop, where
        // it stopped a full hotbar taking money and giving nothing back. It
        // matters more now: a row can buy twenty at once, so the gap between
        // "what you asked for" and "what fits" can be nineteen tapes.
        int leftover = hb.AddResource(e.item, want);
        int got = want - leftover;
        if (got <= 0)
        {
            SetStatus(string.IsNullOrEmpty(_noRoomLine) ? "You've nowhere to put it." : _noRoomLine, C_Err);
            return;
        }

        int cost = got * e.price;
        PlayerWallet.Instance.SpendMoney(cost);

        _qty[i] = 0;
        SetStatus(got < want
            ? $"Bought {got} x {e.name} for ${cost} — only {got} would fit."
            : $"Bought {got} x {e.name} for ${cost}.", got < want ? C_Err : C_Phos);
        Refresh();
    }

    void BuyPlugin(int i)
    {
        Entry e = Stock[i];
        if (e.plugin == null || e.preInstalled || TraxLibrary.IsInstalled(e.plugin)) return;
        if (PlayerWallet.Instance == null || Money < e.price) return;
        if (!PlayerWallet.Instance.SpendMoney(e.price)) return;

        TraxLibrary.Install(e.plugin);
        SetStatus(string.IsNullOrEmpty(_pluginLine)
            ? $"{e.plugin} installed. It's on the computer next time you open it."
            : _pluginLine.Replace("{item}", e.plugin), C_Phos);
        Refresh();
    }

    void SetStatus(string s, Color32 col)
    {
        if (_status == null) return;
        _status.text = s;
        _status.color = col;
    }

    // ── refresh ───────────────────────────────────────────────────────────

    void Refresh()
    {
        _header.text = "// TEV";
        int owned = 0, total = 0;
        for (int i = 0; i < Stock.Length; i++)
        {
            if (IsTape(Stock[i])) continue;
            total++;
            if (Stock[i].preInstalled || TraxLibrary.IsInstalled(Stock[i].plugin)) owned++;
        }

        bool tapes = _tab == Tab.Tapes;
        _tapesUnderline.enabled = tapes;
        _pluginsUnderline.enabled = !tapes;
        _tapesTabLabel.color = tapes ? (Color)C_Label : (Color)C_Dimmer;
        _pluginsTabLabel.color = tapes ? (Color)C_Dimmer : (Color)C_Label;
        _tally.text = tapes ? "" : $"{owned} OF {total} INSTALLED";

        // Which Stock rows this tab shows. Modules already installed sink below
        // the ones still for sale, so the top of the list is always what you can
        // still buy — and the set you are completing stays visible.
        var order = new System.Collections.Generic.List<int>(MaxRows);
        if (tapes)
        {
            for (int i = 0; i < Stock.Length; i++)
                if (IsTape(Stock[i])) order.Add(i);
        }
        else
        {
            for (int i = 0; i < Stock.Length; i++)
                if (!IsTape(Stock[i]) && !Stock[i].preInstalled && !TraxLibrary.IsInstalled(Stock[i].plugin))
                    order.Add(i);
            for (int i = 0; i < Stock.Length; i++)
                if (!IsTape(Stock[i]) && (Stock[i].preInstalled || TraxLibrary.IsInstalled(Stock[i].plugin)))
                    order.Add(i);
        }

        for (int r = 0; r < _rows.Length; r++)
        {
            RowWidget w = _rows[r];
            if (r >= order.Count) { w.root.gameObject.SetActive(false); w.stockIndex = -1; continue; }
            w.root.gameObject.SetActive(true);
            w.stockIndex = order[r];
            PaintRow(w, order[r]);
        }
    }

    void PaintRow(RowWidget w, int i)
    {
        Entry e = Stock[i];
        bool tape = IsTape(e);
        bool own = !tape && (e.preInstalled || TraxLibrary.IsInstalled(e.plugin));

        w.name.text = e.name;
        w.desc.text = e.desc;
        w.chip.color = tape ? e.chip : own ? new Color32(29, 95, 74, 255) : new Color32(47, 88, 120, 255);
        w.name.color = own ? (Color)C_Phos : (Color)C_Label;
        w.bg.color = C_SlotBg;
        w.border.color = C_SlotEdge;
        w.root.GetComponent<CanvasGroup>().alpha = own ? 0.55f : 1f;

        w.stepper.gameObject.SetActive(tape);
        w.installed.gameObject.SetActive(own);
        w.buy.gameObject.SetActive(!own);

        if (tape)
        {
            int q = _qty[i];
            w.price.text = $"${e.price}";
            w.qty.text = q.ToString();
            w.minus.interactable = q > 0;
            w.plus.interactable = CanAdd(i);
            w.buy.interactable = q > 0;
            w.buy.targetGraphic.color = q > 0 ? C_Buy : C_BuyOff;
            w.buyLabel.text = q > 0 ? $"BUY ${LineCost(i)}" : "BUY";
            w.buyLabel.color = q > 0 ? Color.white : (Color)C_Dimmer;
        }
        else if (!own)
        {
            bool afford = Money >= e.price;
            w.price.text = $"${e.price}";
            w.buy.interactable = afford;
            w.buy.targetGraphic.color = afford ? C_Buy : C_BuyOff;
            w.buyLabel.text = afford ? "BUY" : "TOO DEAR";
            w.buyLabel.color = afford ? Color.white : (Color)C_Err;
        }
        else
        {
            w.price.text = "";
        }
    }

    // ── build ─────────────────────────────────────────────────────────────

    void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Vendor;
        HUDSceneGate.Register(_canvas);
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _dim = new GameObject("Dim", typeof(RectTransform));
        _dim.transform.SetParent(transform, false);
        var dimRT = (RectTransform)_dim.transform;
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
        _dim.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
        _dim.SetActive(false);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        _panelRT = (RectTransform)panel.transform;
        _panelRT.anchorMin = _panelRT.anchorMax = _panelRT.pivot = new Vector2(0.5f, 0.5f);
        _panelRT.sizeDelta = new Vector2(PanelW, PanelH);
        panel.AddComponent<Image>().color = C_Bg;
        Outline(_panelRT, C_Border);

        float left = -PanelW * 0.5f + 24f;      // inner left edge

        _header = Txt(_panelRT, "// TEV", new Vector2(left + 100f, -16f), 200, 30, 22,
                      C_Header, FontStyles.Bold, TextAlignmentOptions.Left);
        VendorMoneyBadge.Attach(_panelRT);

        Panel(_panelRT, "HdrRule", new Vector2(0, -52f), new Vector2(RowW, 1f), C_SlotEdge);

        // ── the two tabs ──
        _tapesTabLabel   = TabBtn("BLANK TAPES",     left + 92f,  Tab.Tapes,   out _tapesUnderline);
        _pluginsTabLabel = TabBtn("PLUGINS FOR SALE", left + 300f, Tab.Plugins, out _pluginsUnderline);

        _tally = Txt(_panelRT, "", new Vector2(PanelW * 0.5f - 24f - 90f, -66f), 180, 18, 11,
                     C_Dimmer, FontStyles.Bold, TextAlignmentOptions.Right);

        Panel(_panelRT, "TabRule", new Vector2(0, -94f), new Vector2(RowW, 1f), C_SlotEdge);

        for (int r = 0; r < _rows.Length; r++)
            _rows[r] = BuildRow(r);

        Panel(_panelRT, "FootRule", new Vector2(0, -414f), new Vector2(RowW, 1f), C_SlotEdge);

        _status = Txt(_panelRT, "", new Vector2(left + 230f, -428f), 460, 22, 13,
                      C_Dimmer, FontStyles.Normal, TextAlignmentOptions.Left);

        MkBtn(_panelRT, "DoneBtn", new Vector2(PanelW * 0.5f - 24f - 90f, -424f),
              new Vector2(180, 42), C_Back, Close, out var doneLabel);
        doneLabel.text = "THAT'S ALL";

        _panelRT.gameObject.SetActive(false);
    }

    TextMeshProUGUI TabBtn(string text, float x, Tab tab, out Image underline)
    {
        var rt = Panel(_panelRT, "Tab_" + tab, new Vector2(x, -58f), new Vector2(184, 32), new Color(0, 0, 0, 0));
        var img = rt.GetComponent<Image>();
        img.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => { _tab = tab; Refresh(); });

        var label = Txt(rt, text, new Vector2(0, -7f), 184, 20, 13,
                        C_Dimmer, FontStyles.Bold, TextAlignmentOptions.Center);
        underline = Panel(rt, "Underline", new Vector2(0, -30f), new Vector2(184, 2f), C_Border)
                    .GetComponent<Image>();
        underline.raycastTarget = false;
        return label;
    }

    /// <summary>
    /// One row, built once and repainted. Laid out from the RIGHT edge inwards,
    /// because the BUY button and the stepper are fixed widths and the
    /// description is the part that should absorb whatever is left.
    /// </summary>
    RowWidget BuildRow(int r)
    {
        var w = new RowWidget();
        float half = RowW * 0.5f;

        w.root = Panel(_panelRT, "Row" + r, new Vector2(0, -(RowTop + r * RowPitch)),
                       new Vector2(RowW, RowH), C_SlotEdge);
        w.border = w.root.GetComponent<Image>();
        w.root.gameObject.AddComponent<CanvasGroup>();

        var fill = Panel(w.root, "Fill", new Vector2(0, -1f), new Vector2(RowW - 2, RowH - 2), C_SlotBg);
        w.bg = fill.GetComponent<Image>();
        w.bg.raycastTarget = false;

        w.chip = Panel(w.root, "Chip", new Vector2(-half + 22f, -16f), new Vector2(14, 14), Color.white)
                 .GetComponent<Image>();
        w.chip.raycastTarget = false;

        w.name = Txt(w.root, "", new Vector2(-half + 40f + 48f, -14f), 96, 20, 14,
                     C_Label, FontStyles.Bold, TextAlignmentOptions.Left);

        // Right-to-left: BUY 112 wide against the right pad, stepper 96, price 64.
        float buyX  = half - 14f - 56f;         // centre of a 112-wide button
        float stepX = half - 14f - 112f - 14f - 48f;
        float priceX= stepX - 48f - 14f - 32f;

        w.desc = Txt(w.root, "", new Vector2(-half + 40f + 96f + 8f + 110f, -15f), 220, 20, 12,
                     C_Dim, FontStyles.Normal, TextAlignmentOptions.Left);
        w.desc.overflowMode = TextOverflowModes.Ellipsis;

        w.price = Txt(w.root, "", new Vector2(priceX, -14f), 64, 20, 14,
                      C_Value, FontStyles.Bold, TextAlignmentOptions.Right);

        // ── stepper ──
        w.stepper = Panel(w.root, "Step", new Vector2(stepX, -10f), new Vector2(96, 26), new Color(0, 0, 0, 0));
        int captured = r;
        w.minus = StepBtn(w.stepper, "-", -34f, () => {
            int i = _rows[captured].stockIndex;
            if (i >= 0 && _qty[i] > 0) { _qty[i]--; Refresh(); }
        });
        var qbg = Panel(w.stepper, "QBg", new Vector2(0, 0f), new Vector2(40, 26), new Color32(11, 26, 41, 255));
        qbg.GetComponent<Image>().raycastTarget = false;
        w.qty = Txt(w.stepper, "0", new Vector2(0, -3f), 40, 22, 15,
                    C_Label, FontStyles.Normal, TextAlignmentOptions.Center);
        w.plus = StepBtn(w.stepper, "+", 34f, () => {
            int i = _rows[captured].stockIndex;
            if (i >= 0 && CanAdd(i)) { _qty[i]++; Refresh(); }
        });

        // ── buy / installed ──
        w.buy = MkBtn(w.root, "Buy", new Vector2(buyX, -8f), new Vector2(112, 30), C_Buy, () => {
            int i = _rows[captured].stockIndex;
            if (i < 0) return;
            if (IsTape(Stock[i])) BuyTapes(i); else BuyPlugin(i);
        }, out w.buyLabel);
        w.buyLabel.fontSize = 12;

        w.installed = Txt(w.root, "INSTALLED", new Vector2(buyX, -14f), 112, 20, 11,
                          C_Phos, FontStyles.Bold, TextAlignmentOptions.Center);

        w.root.gameObject.SetActive(false);
        return w;
    }

    Button StepBtn(RectTransform parent, string glyph, float x, Action onClick)
    {
        var rt = Panel(parent, "Step" + glyph, new Vector2(x, 0f), new Vector2(28, 26), new Color32(18, 40, 62, 255));
        var img = rt.GetComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.ColorTint;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.28f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick?.Invoke());
        Txt(rt, glyph, new Vector2(0, -3f), 28, 22, 16, C_Label, FontStyles.Bold, TextAlignmentOptions.Center);
        return btn;
    }

    // ── the same primitives the sell panel uses ──────────────────────────
    // Both anchor to the parent's TOP edge, pivot top-centre: y is pixels DOWN
    // and therefore negative. See the note on the geometry constants.

    static RectTransform Panel(RectTransform parent, string name, Vector2 pos, Vector2 size, Color col)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.color = col;
        img.raycastTarget = col.a > 0.01f;
        return rt;
    }

    static TextMeshProUGUI Txt(RectTransform parent, string text, Vector2 pos, float w, float h,
                               float size, Color32 col, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = col; t.fontStyle = style;
        t.alignment = align; t.raycastTarget = false; t.richText = true;
        return t;
    }

    static Button MkBtn(RectTransform parent, string name, Vector2 pos, Vector2 size, Color32 col,
                        Action onClick, out TextMeshProUGUI label)
    {
        var rt = Panel(parent, name, pos, size, col);
        var img = rt.GetComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
        label = Txt(rt, "", new Vector2(0, -(size.y - 20f) * 0.5f), size.x, 20, 14,
                    Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        return btn;
    }

    static void Outline(RectTransform parent, Color32 col)
    {
        void Strip(string n, Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 off)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = off;
            var img = go.AddComponent<Image>();
            img.color = col; img.raycastTarget = false;
        }
        Strip("EdgeT", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 2), new Vector2(0, -1));
        Strip("EdgeB", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), new Vector2(0, 1));
        Strip("EdgeL", new Vector2(0, 0), new Vector2(0, 1), new Vector2(2, 0), new Vector2(1, 0));
        Strip("EdgeR", new Vector2(1, 0), new Vector2(1, 1), new Vector2(2, 0), new Vector2(-1, 0));
    }
}
