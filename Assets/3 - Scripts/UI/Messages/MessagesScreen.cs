using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The phone's Messages app, v2 (Sam's mockup pick, 2026-08-07): the
/// "bubble readability + scanner accents" look from
/// docs/superpowers/mockups/, with the SLIDER counter mechanic — click or
/// drag along a green→red risk track, big live price readout, no typing
/// anywhere (controller-ready by construction).
///
/// Structure is unchanged from v1: index (pinned guide thread + buyers),
/// thread view with reply chips, appointment card with live countdown +
/// distance, contact card of earned reveals. Everything renders from
/// BuyerLedger events via BuyerTexts; this class owns no persistent state.
/// Mounted by PlayerPhoneUI.EnterMessages as a full-screen child of the
/// phone screen.
/// </summary>
public class MessagesScreen : MonoBehaviour
{
    /// Kept for AIChatScreen's aggregate typing guard. v2 has no text input
    /// anywhere (the counter is a slider), so this is constant.
    public static bool IsTypingActive => false;

    // ── Palette — the mockup blend: neutral dark bases, cyan/gold accents ──
    static readonly Color ScreenBg   = new Color32(0x0F, 0x11, 0x16, 0xFF);
    static readonly Color HeaderBg   = new Color32(0x15, 0x18, 0x20, 0xFF);
    static readonly Color TrayBg     = new Color32(0x12, 0x15, 0x1C, 0xFF);
    static readonly Color RowBg      = new Color32(0x1A, 0x1D, 0x25, 0xFF);
    static readonly Color ThemBubble = new Color32(0x20, 0x24, 0x2E, 0xFF);
    static readonly Color MeBubble   = new Color32(0x2E, 0x7D, 0x4F, 0xFF);
    static readonly Color TextMain   = new Color32(0xE9, 0xED, 0xF2, 0xFF);
    static readonly Color TextDim    = new Color32(0x8B, 0x95, 0xA3, 0xFF);
    static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color Gold       = new Color32(0xFF, 0xD7, 0x32, 0xFF);
    static readonly Color OkGreen    = new Color32(0x57, 0xC4, 0x6E, 0xFF);
    static readonly Color OkGreenBg  = new Color32(0x1C, 0x3A, 0x26, 0xFF);
    static readonly Color WarnAmber  = new Color32(0xE8, 0xA3, 0x3D, 0xFF);
    static readonly Color WarnBg     = new Color32(0x3A, 0x2F, 0x1A, 0xFF);
    static readonly Color DimBtnBg   = new Color32(0x1A, 0x20, 0x29, 0xFF);
    static readonly Color BadRed     = new Color32(0xE0, 0x55, 0x55, 0xFF);
    static readonly Color UnreadCyan = new Color32(0x4F, 0xC3, 0xF7, 0xFF);
    static readonly Color ApptBg     = new Color32(0x20, 0x30, 0x1F, 0xFF);

    enum View { Index, Thread, Card }
    enum ChipMode { Main, WindowPick, CounterSlider }

    System.Action _onExit;
    System.Action _openHalChat;
    PlayerController _player;   // cached once — screen is short-lived

    RectTransform _root;
    RectTransform _indexRoot, _threadRoot, _cardRoot;
    View _view = View.Index;
    string _openId;

    // Index change-detection (1 Hz rebuild only when something changed).
    RectTransform _indexContent;
    float _refreshTimer;
    long _indexStamp = -1;

    // Thread state.
    RectTransform _threadContent;
    ScrollRect _threadScroll;
    RectTransform _chipsRow;
    TextMeshProUGUI _apptText;
    RectTransform _apptCard;
    ChipMode _chipMode = ChipMode.Main;
    int _threadStamp = -1;
    bool _stickToBottom = true;

    // Counter tray state — TWO sliders (Sam's spec): price per cap on the
    // risk gradient, and how many caps you're offering against their ask.
    Slider _priceSlider, _qtySlider;
    TextMeshProUGUI _sliderPrice, _sliderTotal, _sliderRisk, _sliderSendLabel;
    TextMeshProUGUI _priceHandleLabel, _qtyHandleLabel;
    int _priceMin;
    int _askQtyAtBuild;
    int _lastPriceVal = -1, _lastQtyVal = -1;

    struct BubbleEntry { public RectTransform Row; public LayoutElement RowLE; public TextMeshProUGUI Label; public int LastShownLen; public float LastWidth; }
    readonly List<BubbleEntry> _bubbles = new List<BubbleEntry>();

    public void Init(System.Action onExit, System.Action openHalChat)
    {
        _onExit = onExit;
        _openHalChat = openHalChat;
        _player = FindObjectOfType<PlayerController>();

        _root = (RectTransform)transform;
        var bg = gameObject.AddComponent<Image>();
        bg.color = ScreenBg;
        bg.raycastTarget = true;

        ShowIndex();
    }

    /// Called by EconomySync when the host sends fresh economy state.
    public static void RefreshFromNetwork() => s_netNudge++;
    static int s_netNudge;
    int _seenNetNudge;

    void Update()
    {
        // Esc / pad-B backs out one level (matches AIChatScreen — the phone
        // itself also reacts to Esc, same as it always has with the AI chat).
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            if (_view == View.Card) { ShowThread(_openId); return; }
            if (_view == View.Thread) { ShowIndex(); return; }
            Exit();
            return;
        }

        ResizeBubblesToFit();
        UpdateSliderReadout();

        // Co-op: a guest's reply is answered by the HOST, so the result arrives
        // as a state broadcast rather than as a local mutation. The 1 Hz poll
        // below would find it eventually, but a whole second of a dead-looking
        // phone after you tap a chip reads as the tap not registering — so a
        // broadcast forces the very next poll instead of waiting one out.
        if (_seenNetNudge != s_netNudge) { _seenNetNudge = s_netNudge; _refreshTimer = 1f; }

        _refreshTimer += Time.unscaledDeltaTime;
        if (_refreshTimer >= 1f)
        {
            _refreshTimer = 0f;
            if (_view == View.Index)
            {
                long stamp = ComputeIndexStamp();
                if (stamp != _indexStamp) { _indexStamp = stamp; RebuildIndexRows(); }
            }
            else if (_view == View.Thread)
            {
                var b = BuyerLedger.Get(_openId);
                int stamp = ThreadStamp(b);
                if (stamp != _threadStamp) { ShowThread(_openId); }
                else UpdateAppointmentCard(b);
            }
        }
    }

    void Exit()
    {
        var cb = _onExit;
        _onExit = null;
        cb?.Invoke();
        Destroy(gameObject);
    }

    // ══ Index ══════════════════════════════════════════════════════════════

    void ShowIndex()
    {
        if (!string.IsNullOrEmpty(_openId) && !EconomySync.RouteMarkRead(_openId))
            BuyerLedger.MarkRead(_openId);
        _openId = null;
        _view = View.Index;
        ClearViews();

        _indexRoot = FullRect("IndexView", _root);
        var vlg = _indexRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 6f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        BuildHeaderBar(_indexRoot, "MESSAGES", null, Exit, null);

        // Scrollable row list.
        var viewport = NewUI("ScrollViewport", _indexRoot);
        viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0);

        var content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var cvlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.padding = new RectOffset(2, 2, 2, 2);
        cvlg.spacing = 5f;
        cvlg.childControlWidth = true; cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true; cvlg.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;

        _indexContent = content;
        _indexStamp = ComputeIndexStamp();
        RebuildIndexRows();
    }

    long ComputeIndexStamp()
    {
        long s = 17;
        foreach (var b in BuyerLedger.All())
            s = s * 31 + b.events.Count * 7 + b.unread * 3 + (int)b.convo;
        return s;
    }

    void RebuildIndexRows()
    {
        if (_indexContent == null) return;
        for (int i = _indexContent.childCount - 1; i >= 0; i--)
            Destroy(_indexContent.GetChild(i).gameObject);

        // Pinned guide thread (HAL today, Frump later — pure content swap).
        BuildIndexRow(_indexContent, NameStore.ResolvedAIName, "tap to talk",
            pips: -1, unread: false, time: "",
            onTap: () => _openHalChat?.Invoke(), guide: true, id: "guide");

        // Buyers with any thread history, most recent first.
        var buyers = new List<BuyerLedger.Buyer>();
        foreach (var b in BuyerLedger.All())
            if (b.events.Count > 0) buyers.Add(b);
        buyers.Sort((x, y) => LastEvAt(y).CompareTo(LastEvAt(x)));

        foreach (var b in buyers)
        {
            var last = b.events[b.events.Count - 1];
            string captured = b.id;
            BuildIndexRow(_indexContent,
                AlienNames.For(b.id), BuyerTexts.Preview(b.id, last),
                pips: BuyerLedger.PipCount(b.id), unread: b.unread > 0,
                time: Ago(last.at), onTap: () => ShowThread(captured),
                guide: false, id: b.id);
        }

        if (buyers.Count == 0)
        {
            var empty = NewUI("Empty", _indexContent);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            var t = MakeText(empty, "no messages yet — regulars will text\nyou when they want more", 10, TextDim, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Italic;
            Fill(t.rectTransform);
        }
    }

    static float LastEvAt(BuyerLedger.Buyer b) =>
        b.events.Count > 0 ? b.events[b.events.Count - 1].at : 0f;

    static string Ago(float at)
    {
        float s = Mathf.Max(0f, Time.unscaledTime - at);
        if (s < 60f) return "now";
        if (s < 3600f) return $"{Mathf.FloorToInt(s / 60f)}m";
        return $"{Mathf.FloorToInt(s / 3600f)}h";
    }

    /// A stable per-buyer avatar tint (mockup B's identity trick).
    static Color AvatarColor(string id)
    {
        float hue = (AlienIdentity.Hash(id + ":avatar") % 360u) / 360f;
        return Color.HSVToRGB(hue, 0.48f, 0.72f);
    }

    void BuildIndexRow(RectTransform parent, string name, string sub, int pips,
                       bool unread, string time, System.Action onTap, bool guide, string id)
    {
        var row = NewUI($"Row_{name}", parent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
        var bg = row.gameObject.AddComponent<Image>();
        bg.sprite = Rounded(10);
        bg.type = Image.Type.Sliced;
        bg.color = RowBg;
        bg.raycastTarget = true;
        var btn = row.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onTap?.Invoke());

        // Avatar disc + initial.
        var avRT = NewUI("Avatar", row);
        avRT.anchorMin = new Vector2(0f, 0.5f);
        avRT.anchorMax = new Vector2(0f, 0.5f);
        avRT.pivot = new Vector2(0f, 0.5f);
        avRT.sizeDelta = new Vector2(30f, 30f);
        avRT.anchoredPosition = new Vector2(8f, 0f);
        var av = avRT.gameObject.AddComponent<Image>();
        av.sprite = HALVisuals.Disc();
        av.color = guide ? new Color32(0x8E, 0x6A, 0xC8, 0xFF) : AvatarColor(id);
        av.raycastTarget = false;
        var initial = MakeText(avRT, name.Length > 0 ? name.Substring(0, 1) : "?", 14, Color.white, TextAlignmentOptions.Center);
        initial.fontStyle = FontStyles.Bold;
        Fill(initial.rectTransform);

        // Name (top, right of avatar).
        var nameT = MakeText(row, name, 12, guide ? AccentCyan : TextMain, TextAlignmentOptions.TopLeft);
        var nameRT = nameT.rectTransform;
        nameRT.anchorMin = new Vector2(0f, 0f); nameRT.anchorMax = new Vector2(0.55f, 1f);
        nameRT.offsetMin = new Vector2(46f, 2f); nameRT.offsetMax = new Vector2(0f, -5f);
        nameT.fontStyle = FontStyles.Bold;

        // Preview (bottom, dim, one line, ellipsized).
        var subT = MakeText(row, sub, 9, TextDim, TextAlignmentOptions.BottomLeft);
        var subRT = subT.rectTransform;
        subRT.anchorMin = new Vector2(0f, 0f); subRT.anchorMax = new Vector2(1f, 0.5f);
        subRT.offsetMin = new Vector2(46f, 5f); subRT.offsetMax = new Vector2(-28f, 0f);
        subT.enableWordWrapping = false;
        subT.overflowMode = TextOverflowModes.Ellipsis;

        // Time (top-right, dim).
        var timeT = MakeText(row, time, 8, TextDim, TextAlignmentOptions.TopRight);
        var timeRT = timeT.rectTransform;
        timeRT.anchorMin = new Vector2(0.7f, 0.5f); timeRT.anchorMax = new Vector2(1f, 1f);
        timeRT.offsetMin = Vector2.zero; timeRT.offsetMax = new Vector2(-26f, -5f);

        // Guide gets a subtitle chip instead of pips.
        if (guide)
        {
            var g = MakeText(row, "GUIDE", 7, TextDim, TextAlignmentOptions.MidlineLeft);
            var gRT = g.rectTransform;
            gRT.anchorMin = new Vector2(0.55f, 0.55f); gRT.anchorMax = new Vector2(0.7f, 1f);
            gRT.offsetMin = Vector2.zero; gRT.offsetMax = Vector2.zero;
            g.characterSpacing = 2f;
        }

        // Bond pips (sprite discs — Techno SDF has no shape glyphs) + label.
        if (pips >= 0)
        {
            var bondLbl = MakeText(row, "BOND", 7, TextDim, TextAlignmentOptions.MidlineRight);
            var bondRT = bondLbl.rectTransform;
            bondRT.anchorMin = new Vector2(0.55f, 0.58f);
            bondRT.anchorMax = new Vector2(0.55f, 0.58f);
            bondRT.pivot = new Vector2(1f, 0.5f);
            bondRT.sizeDelta = new Vector2(34f, 10f);
            bondRT.anchoredPosition = new Vector2(0f, 0f);
            bondLbl.characterSpacing = 1f;
            for (int i = 0; i < 5; i++)
            {
                var pipRT = NewUI($"Pip{i}", row);
                pipRT.anchorMin = new Vector2(0.55f, 0.58f);
                pipRT.anchorMax = new Vector2(0.55f, 0.58f);
                pipRT.pivot = new Vector2(0f, 0.5f);
                pipRT.sizeDelta = new Vector2(6f, 6f);
                pipRT.anchoredPosition = new Vector2(4f + i * 9f, 0f);
                var pip = pipRT.gameObject.AddComponent<Image>();
                pip.sprite = HALVisuals.Disc();
                pip.color = i < pips ? AccentCyan : new Color(TextDim.r, TextDim.g, TextDim.b, 0.3f);
                pip.raycastTarget = false;
            }
        }

        // Unread dot (far right, centered) — cyan like the mockup.
        if (unread)
        {
            var dotRT = NewUI("Unread", row);
            dotRT.anchorMin = new Vector2(1f, 0.5f);
            dotRT.anchorMax = new Vector2(1f, 0.5f);
            dotRT.pivot = new Vector2(1f, 0.5f);
            dotRT.sizeDelta = new Vector2(10f, 10f);
            dotRT.anchoredPosition = new Vector2(-9f, 0f);
            var dot = dotRT.gameObject.AddComponent<Image>();
            dot.sprite = HALVisuals.Disc();
            dot.color = UnreadCyan;
            dot.raycastTarget = false;
        }
    }

    // ══ Thread ═════════════════════════════════════════════════════════════

    void ShowThread(string id)
    {
        _openId = id;
        _view = View.Thread;
        _chipMode = ChipMode.Main;
        _priceSlider = null; _qtySlider = null;
        ClearViews();
        _bubbles.Clear();
        _stickToBottom = true;

        var b = BuyerLedger.Get(id);
        if (!EconomySync.RouteMarkRead(id)) BuyerLedger.MarkRead(id);
        _threadStamp = ThreadStamp(b);

        _threadRoot = FullRect("ThreadView", _root);
        var vlg = _threadRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 5f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        string captured = id;
        BuildHeaderBar(_threadRoot, AlienNames.For(id), $"BOND {BuyerLedger.BondPips(id)}",
                       ShowIndex, () => ShowCard(captured));

        // Appointment card (only while Scheduled).
        _apptCard = NewUI("ApptCard", _threadRoot);
        _apptCard.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
        var cardBg = _apptCard.gameObject.AddComponent<Image>();
        cardBg.sprite = Rounded(10);
        cardBg.type = Image.Type.Sliced;
        cardBg.color = ApptBg;
        cardBg.raycastTarget = false;
        _apptText = MakeText(_apptCard, "", 10, OkGreen, TextAlignmentOptions.Center);
        var apptTextRT = _apptText.rectTransform;
        apptTextRT.anchorMin = Vector2.zero; apptTextRT.anchorMax = Vector2.one;
        apptTextRT.offsetMin = new Vector2(8f, 2f); apptTextRT.offsetMax = new Vector2(-8f, -2f);
        UpdateAppointmentCard(b);

        // Bubble scroll.
        var viewport = NewUI("ScrollViewport", _threadRoot);
        viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0);

        var content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var cvlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.padding = new RectOffset(4, 4, 4, 4);
        cvlg.spacing = 7f;
        cvlg.childControlWidth = true; cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true; cvlg.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _threadScroll = viewport.gameObject.AddComponent<ScrollRect>();
        _threadScroll.viewport = viewport;
        _threadScroll.content = content;
        _threadScroll.horizontal = false;
        _threadScroll.vertical = true;
        _threadScroll.movementType = ScrollRect.MovementType.Clamped;
        _threadScroll.scrollSensitivity = 18f;
        _threadContent = content;

        // Bubbles from the event log.
        if (b != null)
            for (int i = 0; i < b.events.Count; i++)
                AddEventBubble(b, b.events[i]);

        // Reply tray. Height is driven per-mode (the slider needs more room).
        _chipsRow = NewUI("Tray", _threadRoot);
        var trayBg = _chipsRow.gameObject.AddComponent<Image>();
        trayBg.sprite = Rounded(10);
        trayBg.type = Image.Type.Sliced;
        trayBg.color = TrayBg;
        trayBg.raycastTarget = true;
        RebuildChips(b);

        StartCoroutine(ScrollToBottomNextFrame());
    }

    // Events + convo only — deliberately NOT the local chip mode, or the 1 Hz
    // sweep would rebuild the thread (and yank the slider out from under the
    // player) the moment they tapped COUNTER. ×32 so an event-count change
    // can never alias with a convo change.
    int ThreadStamp(BuyerLedger.Buyer b) =>
        b == null ? 0 : b.events.Count * 32 + (int)b.convo;

    void AddEventBubble(BuyerLedger.Buyer b, BuyerLedger.Ev e)
    {
        var t = (BuyerLedger.EvType)e.type;

        // System lines: in-person deals show as centered dim notes, not bubbles.
        if (t == BuyerLedger.EvType.WalkUpDeal)
        {
            AddSystemLine($"— sold {e.b} {TapeTrade.TapeWord(e.b)} @ {e.a} each —");
            return;
        }

        string text = BuyerTexts.Render(b.id, e);
        if (string.IsNullOrEmpty(text)) return;

        bool player = t == BuyerLedger.EvType.PlayerAccepted
                   || t == BuyerLedger.EvType.PlayerCountered
                   || t == BuyerLedger.EvType.PlayerDeclined;
        MakeBubble(text, player);

        if (t == BuyerLedger.EvType.FulfilledExact || t == BuyerLedger.EvType.FulfilledSub)
            AddSystemLine($"— deal done: {e.b} {TapeTrade.TapeWord(e.b)} @ {e.a} each = {e.a * e.b} —");
    }

    void AddSystemLine(string text)
    {
        var row = NewUI("SysRow", _threadContent);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 15f;
        le.minHeight = 15f;
        var t = MakeText(row, text, 9, TextDim, TextAlignmentOptions.Center);
        Fill(t.rectTransform);
    }

    void MakeBubble(string text, bool player)
    {
        var row = NewUI("BubbleRow", _threadContent);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.minHeight = 24f;
        rowLE.flexibleHeight = 0f;

        var bubble = NewUI("Bubble", row);
        const float MaxFrac = 0.76f;
        if (player)
        {
            bubble.anchorMin = new Vector2(1f - MaxFrac, 0f);
            bubble.anchorMax = new Vector2(1f, 1f);
        }
        else
        {
            bubble.anchorMin = new Vector2(0f, 0f);
            bubble.anchorMax = new Vector2(MaxFrac, 1f);
        }
        bubble.offsetMin = Vector2.zero;
        bubble.offsetMax = Vector2.zero;

        var bg = bubble.gameObject.AddComponent<Image>();
        bg.sprite = Rounded(12);
        bg.type = Image.Type.Sliced;
        bg.color = player ? MeBubble : ThemBubble;
        bg.raycastTarget = false;

        var labelRT = NewUI("Label", bubble);
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(9f, 4f); labelRT.offsetMax = new Vector2(-9f, -4f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 11;
        label.color = TextMain;
        label.alignment = player ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        HudFontResolver.Apply(label);

        _bubbles.Add(new BubbleEntry { Row = row, RowLE = rowLE, Label = label, LastShownLen = -1 });
    }

    void ResizeBubblesToFit()
    {
        for (int i = 0; i < _bubbles.Count; i++)
        {
            var b = _bubbles[i];
            if (b.Label == null || b.RowLE == null || b.Row == null) continue;
            int len = b.Label.text != null ? b.Label.text.Length : 0;
            // Re-measure on WIDTH change too, not just text change — the first
            // measure runs before layout has given the label its real width,
            // so a one-line message wrapped into a tower and the wrong height
            // stuck forever (the giant-bubble bug from the first UI pass).
            float w = b.Label.rectTransform.rect.width;
            if (len == b.LastShownLen && Mathf.Abs(w - b.LastWidth) < 1f) continue;
            b.Label.ForceMeshUpdate();
            float h = Mathf.Max(24f, b.Label.preferredHeight + 10f);
            if (Mathf.Abs(b.RowLE.preferredHeight - h) > 0.5f)
            {
                b.RowLE.preferredHeight = h;
                b.RowLE.minHeight = h;
                LayoutRebuilder.MarkLayoutForRebuild(b.Row);
            }
            b.LastShownLen = len;
            b.LastWidth = w;
            _bubbles[i] = b;
        }
    }

    IEnumerator ScrollToBottomNextFrame()
    {
        yield return null;
        yield return null; // second frame: bubble resize has landed by now
        if (_threadScroll != null && _stickToBottom)
        {
            _threadScroll.verticalNormalizedPosition = 0f;
            _threadScroll.velocity = Vector2.zero;
        }
    }

    void UpdateAppointmentCard(BuyerLedger.Buyer b)
    {
        if (_apptCard == null || _apptText == null) return;
        bool live = b != null && b.convo == BuyerLedger.Convo.Scheduled;
        if (_apptCard.gameObject.activeSelf != live) _apptCard.gameObject.SetActive(live);
        if (!live) return;

        // Show the PROMISED window only — the 60s grace is hidden slack.
        float left = b.deadline - Time.unscaledTime;
        string clock = left > 0f
            ? $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}"
            : "0:00 — they're about to leave";
        // askTier carries a GENRE INDEX for tapes — legacy field name.
        string wantWord = TapeTrade.GenreName(b.askTier);

        string where = "";
        var dir = BuyerMessageDirector.Instance;
        if (dir != null && dir.TryGetBuyerPos(b.id, out Vector3 pos, out string body))
        {
            if (_player != null)
            {
                int dist = Mathf.RoundToInt(Vector3.Distance(_player.transform.position, pos));
                where = string.IsNullOrEmpty(body) ? $" · ~{dist} m" : $" · {body}, ~{dist} m";
            }
        }
        string line = $"MEETUP — {b.askQty} {wantWord} @ {b.offerPerCap} · {clock}{where}";
        if (_apptText.text != line) _apptText.text = line;
        var col = left < 60f ? WarnAmber : OkGreen;
        if (_apptText.color != col) _apptText.color = col;
    }

    // ── Reply tray ─────────────────────────────────────────────────────────

    void RebuildChips(BuyerLedger.Buyer b)
    {
        if (_chipsRow == null) return;
        for (int i = _chipsRow.childCount - 1; i >= 0; i--)
            Destroy(_chipsRow.GetChild(i).gameObject);
        _priceSlider = null; _qtySlider = null;

        var trayLE = _chipsRow.gameObject.GetComponent<LayoutElement>();
        if (trayLE == null) trayLE = _chipsRow.gameObject.AddComponent<LayoutElement>();

        if (b == null) { _chipsRow.gameObject.SetActive(false); return; }
        var dir = BuyerMessageDirector.Instance;
        bool open = b.convo == BuyerLedger.Convo.AwaitingReply
                 || b.convo == BuyerLedger.Convo.AwaitingCounterBack
                 || b.convo == BuyerLedger.Convo.PriceAgreed;
        _chipsRow.gameObject.SetActive(open);
        if (!open || dir == null) return;

        if (_chipMode == ChipMode.CounterSlider && b.convo == BuyerLedger.Convo.AwaitingReply)
        {
            trayLE.preferredHeight = 150f;
            BuildCounterSlider(b, dir);
            return;
        }

        trayLE.preferredHeight = 42f;
        var hlg = _chipsRow.gameObject.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = _chipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.enabled = true;
        hlg.padding = new RectOffset(5, 5, 5, 5);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

        // Price locked by an accepted counter — window pick only (no re-counter).
        if (b.convo == BuyerLedger.Convo.PriceAgreed || _chipMode == ChipMode.WindowPick)
        {
            bool agreed = b.convo == BuyerLedger.Convo.PriceAgreed;
            foreach (int w in BuyerDeals.WindowMinutes)
            {
                int captured = w;
                int pct = Mathf.RoundToInt((BuyerDeals.GratitudeBonus(w) - 1f) * 100f);
                Chip($"~{w} MIN +{pct}%", OkGreen, OkGreenBg, () => { dir.Accept(b, captured); AfterChipAction(); });
            }
            if (agreed) Chip("NOT NOW", TextDim, DimBtnBg, () => { dir.Decline(b); AfterChipAction(); });
            else Chip("BACK", TextDim, DimBtnBg, () => { _chipMode = ChipMode.Main; RebuildChips(b); });
            return;
        }

        if (b.convo == BuyerLedger.Convo.AwaitingCounterBack)
        {
            Chip($"TAKE {b.counterBackPerCap}", OkGreen, OkGreenBg, () => { _chipMode = ChipMode.WindowPick; RebuildChips(b); });
            Chip("DECLINE", BadRed, DimBtnBg, () => { dir.Decline(b); AfterChipAction(); });
            return;
        }

        // AwaitingReply.
        Chip("ACCEPT", OkGreen, OkGreenBg, () => { _chipMode = ChipMode.WindowPick; RebuildChips(b); });
        Chip("COUNTER", WarnAmber, WarnBg, () => { _chipMode = ChipMode.CounterSlider; RebuildChips(b); });
        Chip("NOT NOW", TextDim, DimBtnBg, () => { dir.Decline(b); AfterChipAction(); });
    }

    void AfterChipAction()
    {
        _chipMode = ChipMode.Main;
        if (!string.IsNullOrEmpty(_openId)) ShowThread(_openId);
    }

    void Chip(string label, Color color, Color bgColor, System.Action onTap)
    {
        var rt = NewUI($"Chip_{label}", _chipsRow);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = Rounded(10);
        bg.type = Image.Type.Sliced;
        bg.color = bgColor;
        bg.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onTap?.Invoke());
        var t = MakeText(rt, label, 11, color, TextAlignmentOptions.Center);
        Fill(t.rectTransform);
        t.fontStyle = FontStyles.Bold;
    }

    // ── The counter SLIDER (Sam's pick, mockup option 2) ───────────────────
    // Click anywhere on the gradient track to jump, or drag the thumb for
    // fine control. Big live price + total + risk-in-words readout. Risk is
    // measured against their OFFER (public), never their hidden ceiling.

    void BuildCounterSlider(BuyerLedger.Buyer b, BuyerMessageDirector dir)
    {
        var hlg = _chipsRow.gameObject.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) hlg.enabled = false;   // manual layout in this mode

        _priceMin = b.offerPerCap;
        int priceMax = Mathf.Max(_priceMin + 10, Mathf.RoundToInt(b.offerPerCap * 1.55f));
        int priceStart = Mathf.RoundToInt(b.offerPerCap * 1.1f);
        _askQtyAtBuild = Mathf.Max(1, b.askQty);
        // Quantity range: 1 up to double their ask — shorting AND overselling
        // are both on the table. No appetite term: a tape is a specific song,
        // so "how many can they stomach" is not a question that applies.
        int qtyMax = Mathf.Max(2, _askQtyAtBuild * 2);

        // Readout block (deal line / total / risk).
        _sliderPrice = MakeText(_chipsRow, "", 20, TextMain, TextAlignmentOptions.Center);
        var pRT = _sliderPrice.rectTransform;
        pRT.anchorMin = new Vector2(0f, 1f); pRT.anchorMax = new Vector2(1f, 1f);
        pRT.pivot = new Vector2(0.5f, 1f);
        pRT.sizeDelta = new Vector2(0f, 24f);
        pRT.anchoredPosition = new Vector2(0f, -3f);
        _sliderPrice.fontStyle = FontStyles.Bold;

        _sliderTotal = MakeText(_chipsRow, "", 9, TextDim, TextAlignmentOptions.Center);
        var tRT = _sliderTotal.rectTransform;
        tRT.anchorMin = new Vector2(0f, 1f); tRT.anchorMax = new Vector2(1f, 1f);
        tRT.pivot = new Vector2(0.5f, 1f);
        tRT.sizeDelta = new Vector2(0f, 11f);
        tRT.anchoredPosition = new Vector2(0f, -27f);

        _sliderRisk = MakeText(_chipsRow, "", 9, OkGreen, TextAlignmentOptions.Center);
        var rRT = _sliderRisk.rectTransform;
        rRT.anchorMin = new Vector2(0f, 1f); rRT.anchorMax = new Vector2(1f, 1f);
        rRT.pivot = new Vector2(0.5f, 1f);
        rRT.sizeDelta = new Vector2(0f, 12f);
        rRT.anchoredPosition = new Vector2(0f, -39f);
        _sliderRisk.fontStyle = FontStyles.Bold;

        // PRICE slider on the risk gradient; CAPS slider on a plain track.
        _priceSlider = BuildSliderRow(_chipsRow, "PRICE", -55f, _priceMin, priceMax, priceStart,
                                      RiskGradient(), out _priceHandleLabel);
        _qtySlider = BuildSliderRow(_chipsRow, "CAPS", -79f, 1, qtyMax, _askQtyAtBuild,
                                    null, out _qtyHandleLabel);

        // SEND / BACK buttons along the bottom.
        var btnRow = NewUI("Btns", _chipsRow);
        btnRow.anchorMin = new Vector2(0f, 0f); btnRow.anchorMax = new Vector2(1f, 0f);
        btnRow.pivot = new Vector2(0.5f, 0f);
        btnRow.sizeDelta = new Vector2(-10f, 24f);
        btnRow.anchoredPosition = new Vector2(0f, 4f);
        var bhlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        bhlg.spacing = 6f;
        bhlg.childControlWidth = true; bhlg.childControlHeight = true;
        bhlg.childForceExpandWidth = true; bhlg.childForceExpandHeight = true;

        var sendRT = NewUI("Send", btnRow);
        var sendBg = sendRT.gameObject.AddComponent<Image>();
        sendBg.sprite = Rounded(9);
        sendBg.type = Image.Type.Sliced;
        sendBg.color = WarnBg;
        sendBg.raycastTarget = true;
        var sendLE = sendRT.gameObject.AddComponent<LayoutElement>();
        sendLE.flexibleWidth = 2f;
        var sendBtn = sendRT.gameObject.AddComponent<Button>();
        sendBtn.onClick.AddListener(() =>
        {
            if (_priceSlider == null || _qtySlider == null) return;
            dir.Counter(b, Mathf.RoundToInt(_priceSlider.value), Mathf.RoundToInt(_qtySlider.value));
            AfterChipAction();
        });
        _sliderSendLabel = MakeText(sendRT, "", 11, WarnAmber, TextAlignmentOptions.Center);
        Fill(_sliderSendLabel.rectTransform);
        _sliderSendLabel.fontStyle = FontStyles.Bold;

        var backRT = NewUI("Back", btnRow);
        var backBg = backRT.gameObject.AddComponent<Image>();
        backBg.sprite = Rounded(9);
        backBg.type = Image.Type.Sliced;
        backBg.color = DimBtnBg;
        backBg.raycastTarget = true;
        backRT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(() => { _chipMode = ChipMode.Main; RebuildChips(b); });
        var backT = MakeText(backRT, "BACK", 11, TextDim, TextAlignmentOptions.Center);
        Fill(backT.rectTransform);
        backT.fontStyle = FontStyles.Bold;

        _lastPriceVal = -1;
        _lastQtyVal = -1;
        UpdateSliderReadout();
    }

    /// One labelled slider row. The widget itself now lives in DealSliderKit so
    /// the walk-up sell panel builds the SAME control from the SAME code — two
    /// lookalike copies would drift apart the first time one was retuned.
    Slider BuildSliderRow(RectTransform tray, string caption, float y, int min, int max, int start,
                          Sprite trackSprite, out TextMeshProUGUI handleLabel)
        => DealSliderKit.BuildSliderRow(tray, caption, y, min, max, start, trackSprite, out handleLabel);

    /// Risk wording vs their OFFER (public info only) — never their hidden
    /// accept ceiling. Shared with the sell panel; see DealSliderKit.RiskFor.
    static void RiskFor(int ask, int offer, out string text, out Color col)
        => DealSliderKit.RiskFor(ask, offer, out text, out col);

    /// Change-detected per-frame update while the slider tray is open — the
    /// Sliders' drag paths plus click-to-jump both land here.
    void UpdateSliderReadout()
    {
        if (_priceSlider == null || _qtySlider == null) return;
        int p = Mathf.RoundToInt(_priceSlider.value);
        int q = Mathf.RoundToInt(_qtySlider.value);
        if (p == _lastPriceVal && q == _lastQtyVal) return;
        _lastPriceVal = p;
        _lastQtyVal = q;
        _sliderPrice.text = $"{q} <size=10><color=#8B95A3>{TapeTrade.TapeWord(q)} @</color></size> {p} <size=10><color=#8B95A3>each</color></size>";
        _sliderTotal.text = $"= <color=#FFD732>{p * q}</color> credits  <color=#8B95A3>(they asked for {_askQtyAtBuild})</color>";
        RiskFor(p, _priceMin, out string risk, out Color col);
        _sliderRisk.text = risk;
        _sliderRisk.color = col;
        if (_priceHandleLabel != null) _priceHandleLabel.text = p.ToString();
        if (_qtyHandleLabel != null) _qtyHandleLabel.text = q.ToString();
        if (_sliderSendLabel != null) _sliderSendLabel.text = $"SEND {q} @ {p}";
    }

    // ══ Contact card ═══════════════════════════════════════════════════════

    void ShowCard(string id)
    {
        _view = View.Card;
        ClearViews();

        _cardRoot = FullRect("CardView", _root);
        var vlg = _cardRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 8);
        vlg.spacing = 6f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        string captured = id;
        BuildHeaderBar(_cardRoot, AlienNames.For(id), $"BOND {BuyerLedger.BondPips(id)}",
                       () => ShowThread(captured), null);

        var b = BuyerLedger.Get(id);
        int deals = b != null ? b.dealsCompleted : 0;
        AddCardLine($"deals done: {deals}", TextDim, known: false, plain: true);
        AddCardLine("WHAT YOU'VE LEARNED  <size=8>(one per deal)</size>", AccentCyan, known: false, plain: true);

        int reveals = BuyerLedger.RevealCount(id);
        for (int i = 0; i < reveals; i++)
            AddCardLine(BuyerLedger.RevealLine(id, i), TextMain, known: true, plain: false);
        if (reveals < BuyerLedger.RevealCap)
            AddCardLine("deal again to learn more…", new Color(TextDim.r, TextDim.g, TextDim.b, 0.6f), known: false, plain: false);
    }

    void AddCardLine(string text, Color color, bool known, bool plain)
    {
        var row = NewUI("CardLine", _cardRoot);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = plain ? 18f : 24f;
        le.minHeight = 16f;
        if (!plain)
        {
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(8);
            bg.type = Image.Type.Sliced;
            bg.color = known ? RowBg : new Color(RowBg.r, RowBg.g, RowBg.b, 0.45f);
            bg.raycastTarget = false;
        }
        var t = MakeText(row, text, 10, color, TextAlignmentOptions.MidlineLeft);
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(plain ? 4f : 10f, 0f); rt.offsetMax = new Vector2(-4f, 0f);
    }

    // ══ shared chrome + tiny helpers ═══════════════════════════════════════

    /// The rounded header bar every view shares: back button, title (optionally
    /// tappable → contact card), dim subtitle on the right.
    void BuildHeaderBar(RectTransform parent, string title, string right,
                        System.Action onBack, System.Action onTitleTap)
    {
        var header = NewUI("Header", parent);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        var bg = header.gameObject.AddComponent<Image>();
        bg.sprite = Rounded(10);
        bg.type = Image.Type.Sliced;
        bg.color = HeaderBg;
        bg.raycastTarget = false;

        // Back button — explicit rect (NOT layout-sized; the v1 back button
        // died because an HLG gave a sprite-less Image zero height).
        var backRT = NewUI("Back", header);
        backRT.anchorMin = new Vector2(0f, 0f); backRT.anchorMax = new Vector2(0f, 1f);
        backRT.pivot = new Vector2(0f, 0.5f);
        backRT.sizeDelta = new Vector2(34f, 0f);
        backRT.anchoredPosition = Vector2.zero;
        var backImg = backRT.gameObject.AddComponent<Image>();
        backImg.color = new Color(0, 0, 0, 0);
        backImg.raycastTarget = true;
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(() => onBack?.Invoke());
        var backT = MakeText(backRT, "<", 15, AccentCyan, TextAlignmentOptions.Center);
        Fill(backT.rectTransform);
        backT.fontStyle = FontStyles.Bold;

        var titleRT = NewUI("Title", header);
        titleRT.anchorMin = new Vector2(0f, 0f); titleRT.anchorMax = new Vector2(0.62f, 1f);
        titleRT.offsetMin = new Vector2(38f, 0f); titleRT.offsetMax = Vector2.zero;
        var titleT = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        titleT.text = title;
        titleT.fontSize = 13;
        titleT.color = TextMain;
        titleT.fontStyle = FontStyles.Bold;
        titleT.alignment = TextAlignmentOptions.MidlineLeft;
        HudFontResolver.Apply(titleT);
        if (onTitleTap != null)
        {
            titleT.raycastTarget = true;
            var tBtn = titleRT.gameObject.AddComponent<Button>();
            tBtn.onClick.AddListener(() => onTitleTap.Invoke());
        }
        else titleT.raycastTarget = false;

        if (!string.IsNullOrEmpty(right))
        {
            var rT = MakeText(header, right, 9, TextDim, TextAlignmentOptions.MidlineRight);
            var rRT = rT.rectTransform;
            rRT.anchorMin = new Vector2(0.62f, 0f); rRT.anchorMax = new Vector2(1f, 1f);
            rRT.offsetMin = Vector2.zero; rRT.offsetMax = new Vector2(-10f, 0f);
            rT.characterSpacing = 2f;
        }
    }

    void ClearViews()
    {
        if (_indexRoot != null) { Destroy(_indexRoot.gameObject); _indexRoot = null; _indexContent = null; }
        if (_threadRoot != null) { Destroy(_threadRoot.gameObject); _threadRoot = null; _threadContent = null; _threadScroll = null; _chipsRow = null; _apptCard = null; _apptText = null; _priceSlider = null; _qtySlider = null; }
        if (_cardRoot != null) { Destroy(_cardRoot.gameObject); _cardRoot = null; }
        _bubbles.Clear();
    }

    static RectTransform FullRect(string name, RectTransform parent)
    {
        var rt = NewUI(name, parent);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    static RectTransform NewUI(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI MakeText(RectTransform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var rt = NewUI("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        HudFontResolver.Apply(t);
        return t;
    }

    // ── procedural sprites ─────────────────────────────────────────────────

    static readonly Dictionary<int, Sprite> s_rounded = new Dictionary<int, Sprite>();

    /// 9-sliced rounded-rect sprite (white; tint via Image.color). Same trick
    /// as PlayerPhoneUI.RoundedRectFilled, local so this screen stays
    /// self-contained.
    static Sprite Rounded(int radius)
    {
        if (s_rounded.TryGetValue(radius, out var cached)) return cached;
        int size = radius * 2 + 8;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float cx = Mathf.Clamp(x, radius, size - 1 - radius);
            float cy = Mathf.Clamp(y, radius, size - 1 - radius);
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
            float a = Mathf.Clamp01(radius - d + 1f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                               0, SpriteMeshType.FullRect,
                               new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
        s_rounded[radius] = sp;
        return sp;
    }

    /// Horizontal green→amber→red gradient for the counter slider's risk track.
    /// Shared with the sell panel — see DealSliderKit.RiskGradient.
    static Sprite RiskGradient() => DealSliderKit.RiskGradient();
}
