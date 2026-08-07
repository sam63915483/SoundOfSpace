using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The phone's Messages app (spec §7, 2026-08-07 design): an index of
/// contacts (pinned guide thread + every buyer with ledger state), a thread
/// view with reply chips (accept / counter / decline, window pick,
/// counter-back), the pending-appointment card with live distance, and a
/// contact card listing the hidden wants the player has earned.
///
/// Mounted by PlayerPhoneUI.EnterMessages the same way AIChatScreen is
/// (full-screen child of _screenRT). Renders everything from BuyerLedger
/// events via BuyerTexts — this class generates no content and owns no
/// persistent state. Mirrors AIChatScreen's visual language and its
/// wrapped-bubble sizing + sticky-scroll patterns.
/// </summary>
public class MessagesScreen : MonoBehaviour
{
    /// True while the counter-offer input field has focus, so typed digits
    /// can't double as movement/hotkey input. ORed with
    /// AIChatScreen.IsTypingActive at the phone/player guards.
    public static bool IsTypingActive { get; private set; }

    // Palette — same values as PlayerPhoneUI / AIChatScreen.
    static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color DimBlue    = new Color32(0x7F, 0xA0, 0xBD, 0xFF);
    static readonly Color TileBg     = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ScreenBg   = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color ButtonGrey = new Color32(0x2A, 0x40, 0x60, 0xFF);
    static readonly Color UnreadRed  = new Color32(0xFF, 0x5A, 0x5A, 0xFF);
    static readonly Color OkGreen    = new Color32(0x6E, 0xDC, 0x82, 0xFF);
    static readonly Color WarnAmber  = new Color32(0xFF, 0xD7, 0x32, 0xFF);

    enum View { Index, Thread, Card }
    enum ChipMode { Main, WindowPick, CounterInput }

    System.Action _onExit;
    System.Action _openHalChat;

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
    TMP_InputField _counterInput;
    ChipMode _chipMode = ChipMode.Main;
    int _threadStamp = -1;
    bool _stickToBottom = true;

    struct BubbleEntry { public RectTransform Row; public LayoutElement RowLE; public TextMeshProUGUI Label; public int LastShownLen; }
    readonly List<BubbleEntry> _bubbles = new List<BubbleEntry>();

    PlayerController _player;   // cached once — screen is short-lived

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

    void OnDestroy() { IsTypingActive = false; }

    void Update()
    {
        IsTypingActive = _counterInput != null && _counterInput.isFocused;

        // Esc / pad-B backs out one level (matches AIChatScreen — note the
        // phone itself also reacts to Esc, same as it always has with the
        // AI chat; X is reserved for the phone's own app handling).
        if ((Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B)) && !IsTypingActive)
        {
            if (_view == View.Card) { ShowThread(_openId); return; }
            if (_view == View.Thread) { ShowIndex(); return; }
            Exit();
            return;
        }

        ResizeBubblesToFit();

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
        if (!string.IsNullOrEmpty(_openId)) BuyerLedger.MarkRead(_openId);
        _openId = null;
        _view = View.Index;
        ClearViews();

        _indexRoot = FullRect("IndexView", _root);
        var vlg = _indexRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 4f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        // Header: back arrow + title.
        var header = NewUI("Header", _indexRoot);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        var hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        MakeButton(header, "<", 16f, AccentCyan, Exit, 16f);
        var title = MakeText(header, "MESSAGES", 12, AccentCyan, TextAlignmentOptions.MidlineLeft);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 2f;

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
        cvlg.spacing = 4f;
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
        BuildIndexRow(_indexContent,
            name: NameStore.ResolvedAIName,
            sub: "guide",
            pips: -1, unread: false,
            time: "",
            onTap: () => _openHalChat?.Invoke(),
            accent: true);

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
                name: AlienNames.For(b.id),
                sub: BuyerTexts.Preview(b.id, last),
                pips: BuyerLedger.PipCount(b.id),
                unread: b.unread > 0,
                time: Ago(last.at),
                onTap: () => ShowThread(captured),
                accent: false);
        }

        if (buyers.Count == 0)
        {
            var empty = NewUI("Empty", _indexContent);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var t = MakeText(empty, "no messages yet — regulars will text\nyou when they want more", 9, DimBlue, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Italic;
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

    void BuildIndexRow(RectTransform parent, string name, string sub, int pips,
                       bool unread, string time, System.Action onTap, bool accent)
    {
        var row = NewUI($"Row_{name}", parent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
        var bg = row.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.raycastTarget = true;
        var btn = row.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onTap?.Invoke());

        // Name (top-left).
        var nameT = MakeText(row, name, 11, accent ? AccentCyan : LabelWhite, TextAlignmentOptions.TopLeft);
        var nameRT = (RectTransform)nameT.transform;
        nameRT.anchorMin = new Vector2(0f, 0f); nameRT.anchorMax = new Vector2(0.6f, 1f);
        nameRT.offsetMin = new Vector2(8f, 2f); nameRT.offsetMax = new Vector2(0f, -3f);
        nameT.fontStyle = FontStyles.Bold;

        // Preview (bottom-left, dim, one line).
        var subT = MakeText(row, sub, 8, DimBlue, TextAlignmentOptions.BottomLeft);
        var subRT = (RectTransform)subT.transform;
        subRT.anchorMin = new Vector2(0f, 0f); subRT.anchorMax = new Vector2(1f, 0.55f);
        subRT.offsetMin = new Vector2(8f, 3f); subRT.offsetMax = new Vector2(-24f, 0f);
        subT.enableWordWrapping = false;
        subT.overflowMode = TextOverflowModes.Ellipsis;

        // Time (top-right, dim).
        var timeT = MakeText(row, time, 8, DimBlue, TextAlignmentOptions.TopRight);
        var timeRT = (RectTransform)timeT.transform;
        timeRT.anchorMin = new Vector2(0.7f, 0.45f); timeRT.anchorMax = new Vector2(1f, 1f);
        timeRT.offsetMin = Vector2.zero; timeRT.offsetMax = new Vector2(-22f, -3f);

        // Bond pips (right of the name) — sprite discs, not font glyphs
        // (Techno SDF has no geometric-shape characters).
        if (pips >= 0)
        {
            for (int i = 0; i < 5; i++)
            {
                var pipRT = NewUI($"Pip{i}", row);
                pipRT.anchorMin = new Vector2(0.62f, 0.62f);
                pipRT.anchorMax = new Vector2(0.62f, 0.62f);
                pipRT.pivot = new Vector2(0f, 0.5f);
                pipRT.sizeDelta = new Vector2(5f, 5f);
                pipRT.anchoredPosition = new Vector2(i * 8f, 0f);
                var pip = pipRT.gameObject.AddComponent<Image>();
                pip.sprite = HALVisuals.Disc();
                pip.color = i < pips ? AccentCyan : new Color(DimBlue.r, DimBlue.g, DimBlue.b, 0.35f);
                pip.raycastTarget = false;
            }
        }

        // Unread dot (far right, centered).
        if (unread)
        {
            var dotRT = NewUI("Unread", row);
            dotRT.anchorMin = new Vector2(1f, 0.5f);
            dotRT.anchorMax = new Vector2(1f, 0.5f);
            dotRT.pivot = new Vector2(1f, 0.5f);
            dotRT.sizeDelta = new Vector2(9f, 9f);
            dotRT.anchoredPosition = new Vector2(-7f, 0f);
            var dot = dotRT.gameObject.AddComponent<Image>();
            dot.sprite = HALVisuals.Disc();
            dot.color = UnreadRed;
            dot.raycastTarget = false;
        }
    }

    // ══ Thread ═════════════════════════════════════════════════════════════

    void ShowThread(string id)
    {
        _openId = id;
        _view = View.Thread;
        _chipMode = ChipMode.Main;
        _counterInput = null;
        ClearViews();
        _bubbles.Clear();
        _stickToBottom = true;

        var b = BuyerLedger.Get(id);
        BuyerLedger.MarkRead(id);
        _threadStamp = ThreadStamp(b);

        _threadRoot = FullRect("ThreadView", _root);
        var vlg = _threadRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 4f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        // Header: back, name (tap = contact card), pips.
        var header = NewUI("Header", _threadRoot);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        var hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        MakeButton(header, "<", 16f, AccentCyan, ShowIndex, 16f);
        string captured = id;
        var nameBtn = MakeButton(header, AlienNames.For(id), 12f, LabelWhite, () => ShowCard(captured), 120f);
        nameBtn.GetComponentInChildren<TextMeshProUGUI>().fontStyle = FontStyles.Bold;
        var pipsT = MakeText(header, BuyerLedger.BondPips(id), 10, DimBlue, TextAlignmentOptions.MidlineLeft);
        pipsT.characterSpacing = 2f;

        // Appointment card (only while Scheduled).
        _apptCard = NewUI("ApptCard", _threadRoot);
        _apptCard.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        var cardBg = _apptCard.gameObject.AddComponent<Image>();
        cardBg.color = new Color32(0x14, 0x2A, 0x1C, 0xE0);
        cardBg.raycastTarget = false;
        _apptText = MakeText(_apptCard, "", 9, OkGreen, TextAlignmentOptions.Center);
        var apptTextRT = (RectTransform)_apptText.transform;
        apptTextRT.anchorMin = Vector2.zero; apptTextRT.anchorMax = Vector2.one;
        apptTextRT.offsetMin = new Vector2(6f, 2f); apptTextRT.offsetMax = new Vector2(-6f, -2f);
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
        cvlg.spacing = 6f;
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

        // Reply chips.
        _chipsRow = NewUI("Chips", _threadRoot);
        _chipsRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        var chlg = _chipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        chlg.padding = new RectOffset(2, 2, 1, 1);
        chlg.spacing = 4f;
        chlg.childAlignment = TextAnchor.MiddleCenter;
        chlg.childControlWidth = true; chlg.childControlHeight = true;
        chlg.childForceExpandWidth = true; chlg.childForceExpandHeight = true;
        RebuildChips(b);

        StartCoroutine(ScrollToBottomNextFrame());
    }

    // Events + convo only — deliberately NOT the local chip mode, or the 1 Hz
    // sweep would rebuild the thread (and eject the player from the counter
    // input field) the moment they tapped COUNTER.
    int ThreadStamp(BuyerLedger.Buyer b) =>
        b == null ? 0 : b.events.Count * 16 + (int)b.convo * 4;

    void AddEventBubble(BuyerLedger.Buyer b, BuyerLedger.Ev e)
    {
        var t = (BuyerLedger.EvType)e.type;

        // System lines: in-person deals show as centered dim notes, not bubbles.
        if (t == BuyerLedger.EvType.WalkUpDeal)
        {
            AddSystemLine($"— sold {e.b} {MushroomSpecies.TierName((MushroomTier)e.tier).ToLowerInvariant()} @ {e.a} a cap —");
            return;
        }

        string text = BuyerTexts.Render(b.id, e);
        if (string.IsNullOrEmpty(text)) return;

        bool player = t == BuyerLedger.EvType.PlayerAccepted
                   || t == BuyerLedger.EvType.PlayerCountered
                   || t == BuyerLedger.EvType.PlayerDeclined;
        MakeBubble(text, player);

        // A fulfilled/missed order also gets a system line so the deal's
        // numbers are scannable without reading prose.
        if (t == BuyerLedger.EvType.FulfilledExact || t == BuyerLedger.EvType.FulfilledSub)
            AddSystemLine($"— deal done: {e.b} caps @ {e.a} each = {e.a * e.b} —");
    }

    void AddSystemLine(string text)
    {
        var row = NewUI("SysRow", _threadContent);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 14f;
        le.minHeight = 14f;
        var t = MakeText(row, text, 8, DimBlue, TextAlignmentOptions.Center);
        var rt = (RectTransform)t.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    void MakeBubble(string text, bool player)
    {
        var row = NewUI("BubbleRow", _threadContent);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.minHeight = 20f;
        rowLE.flexibleHeight = 0f;

        var bubble = NewUI("Bubble", row);
        const float MaxFrac = 0.78f;
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
        bg.color = player ? ButtonGrey : TileBg;
        bg.raycastTarget = false;

        var labelRT = NewUI("Label", bubble);
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(6f, 3f); labelRT.offsetMax = new Vector2(-6f, -3f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 10;
        label.color = LabelWhite;
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
            if (len == b.LastShownLen) continue;
            b.Label.ForceMeshUpdate();
            float h = Mathf.Max(20f, b.Label.preferredHeight + 8f);
            if (Mathf.Abs(b.RowLE.preferredHeight - h) > 0.5f)
            {
                b.RowLE.preferredHeight = h;
                b.RowLE.minHeight = h;
                LayoutRebuilder.MarkLayoutForRebuild(b.Row);
            }
            b.LastShownLen = len;
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

        float left = b.deadline + BuyerDeals.GraceSeconds - Time.unscaledTime;
        string clock = left <= 0f ? "0:00"
            : $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}";
        string tierWord = MushroomSpecies.TierName((MushroomTier)b.askTier).ToLowerInvariant();

        string where = "";
        var dir = BuyerMessageDirector.Instance;
        if (dir != null && dir.TryGetBuyerPos(b.id, out Vector3 pos, out string body))
        {
            if (_player != null)
            {
                int dist = Mathf.RoundToInt(Vector3.Distance(_player.transform.position, pos));
                where = string.IsNullOrEmpty(body) ? $" - ~{dist} m" : $" - {body}, ~{dist} m";
            }
        }
        string line = $"MEETUP — {b.askQty} {tierWord} @ {b.offerPerCap} · {clock} left{where}";
        if (_apptText.text != line) _apptText.text = line;
        var col = left < 60f ? WarnAmber : OkGreen;
        if (_apptText.color != col) _apptText.color = col;
    }

    // ── Reply chips ────────────────────────────────────────────────────────

    void RebuildChips(BuyerLedger.Buyer b)
    {
        if (_chipsRow == null) return;
        for (int i = _chipsRow.childCount - 1; i >= 0; i--)
            Destroy(_chipsRow.GetChild(i).gameObject);
        _counterInput = null;

        if (b == null) { _chipsRow.gameObject.SetActive(false); return; }
        var dir = BuyerMessageDirector.Instance;
        bool open = b.convo == BuyerLedger.Convo.AwaitingReply
                 || b.convo == BuyerLedger.Convo.AwaitingCounterBack;
        _chipsRow.gameObject.SetActive(open);
        if (!open || dir == null) return;

        if (_chipMode == ChipMode.WindowPick)
        {
            foreach (int w in BuyerDeals.WindowMinutes)
            {
                int captured = w;
                Chip($"~{w} MIN", OkGreen, () => { dir.Accept(b, captured); AfterChipAction(); });
            }
            Chip("BACK", DimBlue, () => { _chipMode = ChipMode.Main; RebuildChips(b); });
            return;
        }

        if (_chipMode == ChipMode.CounterInput)
        {
            BuildCounterInput(b, dir);
            return;
        }

        if (b.convo == BuyerLedger.Convo.AwaitingCounterBack)
        {
            Chip($"TAKE {b.counterBackPerCap}", OkGreen, () => { _chipMode = ChipMode.WindowPick; RebuildChips(b); });
            Chip("DECLINE", UnreadRed, () => { dir.Decline(b); AfterChipAction(); });
            return;
        }

        // AwaitingReply.
        Chip("ACCEPT", OkGreen, () => { _chipMode = ChipMode.WindowPick; RebuildChips(b); });
        Chip("COUNTER", WarnAmber, () => { _chipMode = ChipMode.CounterInput; RebuildChips(b); });
        Chip("NOT NOW", DimBlue, () => { dir.Decline(b); AfterChipAction(); });
    }

    void AfterChipAction()
    {
        _chipMode = ChipMode.Main;
        // Rebuild the whole thread so the new events render as bubbles.
        if (!string.IsNullOrEmpty(_openId)) ShowThread(_openId);
    }

    void Chip(string label, Color color, System.Action onTap)
    {
        var rt = NewUI($"Chip_{label}", _chipsRow);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onTap?.Invoke());
        var t = MakeText(rt, label, 9, color, TextAlignmentOptions.Center);
        var tRT = (RectTransform)t.transform;
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        t.fontStyle = FontStyles.Bold;
    }

    void BuildCounterInput(BuyerLedger.Buyer b, BuyerMessageDirector dir)
    {
        // Numeric field pre-filled ~10% over their offer, SEND, and a back-out.
        var inputRT = NewUI("CounterInput", _chipsRow);
        var inputBg = inputRT.gameObject.AddComponent<Image>();
        inputBg.color = new Color32(0x08, 0x13, 0x1F, 0xFF);
        inputBg.raycastTarget = true;
        _counterInput = inputRT.gameObject.AddComponent<TMP_InputField>();

        var areaRT = NewUI("TextArea", inputRT);
        areaRT.anchorMin = Vector2.zero; areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(4f, 2f); areaRT.offsetMax = new Vector2(-4f, -2f);
        areaRT.gameObject.AddComponent<RectMask2D>();

        var textRT = NewUI("Text", areaRT);
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero; textRT.offsetMax = Vector2.zero;
        var textComp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        textComp.fontSize = 10;
        textComp.color = LabelWhite;
        textComp.alignment = TextAlignmentOptions.MidlineLeft;
        textComp.raycastTarget = false;
        HudFontResolver.Apply(textComp);

        _counterInput.textComponent = textComp;
        _counterInput.textViewport = areaRT;
        _counterInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        _counterInput.text = Mathf.RoundToInt(b.offerPerCap * 1.1f).ToString();

        Chip("SEND", WarnAmber, () =>
        {
            int ask;
            if (!int.TryParse(_counterInput != null ? _counterInput.text : "", out ask) || ask <= 0) return;
            dir.Counter(b, ask);
            AfterChipAction();
        });
        Chip("BACK", DimBlue, () => { _chipMode = ChipMode.Main; RebuildChips(b); });
    }

    // ══ Contact card ═══════════════════════════════════════════════════════

    void ShowCard(string id)
    {
        _view = View.Card;
        ClearViews();

        _cardRoot = FullRect("CardView", _root);
        var vlg = _cardRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 4, 8);
        vlg.spacing = 6f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var header = NewUI("Header", _cardRoot);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        var hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        string captured = id;
        MakeButton(header, "<", 16f, AccentCyan, () => ShowThread(captured), 16f);
        var title = MakeText(header, AlienNames.For(id), 13, LabelWhite, TextAlignmentOptions.MidlineLeft);
        title.fontStyle = FontStyles.Bold;
        var pipsT = MakeText(header, BuyerLedger.BondPips(id), 10, DimBlue, TextAlignmentOptions.MidlineLeft);
        pipsT.characterSpacing = 2f;

        var b = BuyerLedger.Get(id);
        int deals = b != null ? b.dealsCompleted : 0;
        AddCardLine($"deals done: {deals}", DimBlue);
        AddCardLine(" ", DimBlue);
        AddCardLine("WHAT YOU'VE LEARNED", AccentCyan);

        int reveals = BuyerLedger.RevealCount(id);
        for (int i = 0; i < reveals; i++)
            AddCardLine("· " + BuyerLedger.RevealLine(id, i), LabelWhite);
        if (reveals < BuyerLedger.RevealCap)
            AddCardLine("— deal again to learn more —", new Color(DimBlue.r, DimBlue.g, DimBlue.b, 0.6f));
    }

    void AddCardLine(string text, Color color)
    {
        var row = NewUI("CardLine", _cardRoot);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 16f;
        le.minHeight = 14f;
        var t = MakeText(row, text, 9, color, TextAlignmentOptions.MidlineLeft);
        var rt = (RectTransform)t.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 0f); rt.offsetMax = Vector2.zero;
    }

    // ══ tiny UI helpers (same shapes as AIChatScreen's) ════════════════════

    void ClearViews()
    {
        if (_indexRoot != null) { Destroy(_indexRoot.gameObject); _indexRoot = null; _indexContent = null; }
        if (_threadRoot != null) { Destroy(_threadRoot.gameObject); _threadRoot = null; _threadContent = null; _threadScroll = null; _chipsRow = null; _apptCard = null; _apptText = null; _counterInput = null; }
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

    static Button MakeButton(RectTransform parent, string label, float size, Color color, System.Action onTap, float width)
    {
        var rt = NewUI($"Btn_{label}", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0); // invisible hit target
        img.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onTap?.Invoke());
        var t = MakeText(rt, label, size, color, TextAlignmentOptions.MidlineLeft);
        var tRT = (RectTransform)t.transform;
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        t.fontStyle = FontStyles.Bold;
        return btn;
    }
}
