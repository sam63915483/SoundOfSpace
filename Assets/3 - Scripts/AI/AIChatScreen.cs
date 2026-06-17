using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Procedural chat screen for the phone AI. Built inside the phone's
// _pageHostRT (passed in by PlayerPhoneUI on open). Mirrors the phone's
// existing visual language (AccentCyan, LabelWhite, TileBg, ScreenBg)
// and reuses HudFontResolver + UIPanelSprites for consistency.
//
// Lifecycle:
//   - PlayerPhoneUI.EnterAIChat() instantiates this as a child of
//     _pageHostRT, sets up references, hides page content.
//   - AIChatScreen drives its own UI; pulls history from AIMemoryStore
//     and sends chats through LLMService.
//   - On exit (back arrow / Esc / phone close), calls back into
//     PlayerPhoneUI to restore the page, then fire-and-forget runs
//     AIMemoryExtractor.RunAsync() if memories are dirty.
public class AIChatScreen : MonoBehaviour
{
    // Set true while the TMP_InputField has focus. PlayerController.Update
    // early-returns when this is true so typed WASD letters can't double
    // as movement input.
    public static bool IsTypingActive { get; private set; }

    // First-contact scripted exchange state. Runs on the very first opening
    // of the AI app per save (NameStore.FirstContactComplete == false). Two
    // captures: player's name, then AI's name (with decline path). After
    // the second capture, falls through to normal LLM chat.
    enum FirstContactState { CapturingPlayerName, CapturingAIName, Complete }
    FirstContactState _firstContact = FirstContactState.Complete;

    // Prepends the AI's chosen name to a reply body so the chat bubble shows
    // "{AI_NAME}: <body>". Resolved name falls back to "Assistant" if the
    // player declined to name the AI during first-contact. Empty input
    // returns the prefix alone (used when streaming hasn't delivered any
    // text yet — keeps the bubble showing the prefix while typing dots
    // animate).
    static string WrapAIReply(string body)
    {
        string name = NameStore.ResolvedAIName;
        if (string.IsNullOrEmpty(body)) return name + ": ";
        return name + ": " + body;
    }

    // Palette — duplicated from PlayerPhoneUI so this screen is
    // self-contained. Keep in sync if PlayerPhoneUI's palette changes.
    static readonly Color AccentCyan   = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite   = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg       = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ScreenBg     = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color ButtonGrey   = new Color32(0x2A, 0x40, 0x60, 0xFF);
    // HAL eye colour + disc sprite live in HALVisuals (shared with HALLineHUD).
    Image _halEyeCore;   // the inner solid disc
    Image _halEyeGlow;   // the larger soft halo behind it

    RectTransform        _root;
    RectTransform        _messageContent;       // VerticalLayoutGroup parent
    ScrollRect           _scrollRect;
    TMP_InputField       _inputField;
    Button               _sendButton;
    TextMeshProUGUI      _sendGlyph;
    TextMeshProUGUI      _activeStreamLabel;     // the bubble currently filling
    System.Action        _onExitCallback;
    Coroutine            _typingDotsRoutine;
    DialogueReplyColumn  _replyColumn;
    PhoneDialoguePresenter _presenter;
    DialogueRunner       _runner;

    // Input row sizing — refs kept so Update can grow the row upward as
    // the typed text wraps to multiple lines. MultiLineSubmit mode means
    // Enter still submits, but the visible text wraps.
    RectTransform        _inputRow;
    LayoutElement        _inputRowLE;
    LayoutElement        _inputFieldLE;
    GameObject           _placeholderGO;          // hidden while focused so caret isn't behind it
    int                  _lastInputTextLen = -1;
    const float          InputRowMinHeight = 28f;
    const float          InputRowMaxHeight = 120f;

    // Manual blinking caret. TMP_InputField creates its own caret as the
    // first sibling of the text component's parent, which puts it under
    // everything we render and made it consistently invisible in this
    // setup. We draw our own caret as a thin cyan Image that we position
    // at the end of the typed text every frame. Always visible while the
    // input field has focus.
    RectTransform        _customCaretRT;
    Image                _customCaretImg;
    RectTransform        _textAreaRT;
    TextMeshProUGUI      _textComp;
    float                _caretBlinkTimer;
    const float          CaretBlinkRate = 0.55f;

    // All bubble labels paired with their row LayoutElement, so Update can
    // keep them sized to fit wrapped text as it streams in or as parent
    // width changes. List grows with chat history; never shrinks.
    struct BubbleEntry { public RectTransform Row; public LayoutElement RowLE; public TextMeshProUGUI Label; public int LastShownLen; }
    readonly System.Collections.Generic.List<BubbleEntry> _bubbles = new System.Collections.Generic.List<BubbleEntry>();

    // ── Sticky-at-bottom scroll state ──────────────────────────────────
    // _stickToBottom defaults to true. New bubbles auto-scroll the view to
    // the latest content — UNLESS the player has scrolled up to read
    // history, in which case the view holds its position. When the player
    // scrolls back down to the bottom, stickiness re-engages and the next
    // new bubble (or reveal update) auto-scrolls again. Mirrors the
    // iMessage / Discord / Slack scroll-lock behaviour every modern chat
    // UI uses.
    //
    // ScrollRect.verticalNormalizedPosition: 0f = at bottom of content
    // (latest message visible), 1f = at top (oldest visible).
    bool _stickToBottom = true;
    // Hysteresis thresholds prevent flicker around the edge: we only EXIT
    // sticky mode if the player clearly moves up; we only RE-ENTER sticky
    // if the player gets all the way back to the floor. The gap between
    // these two values is the dead zone where state doesn't change.
    const float StickyExitThreshold  = 0.05f; // > 0.05 from bottom → unsticky
    const float StickyEnterThreshold = 0.005f; // ≤ 0.005 from bottom → sticky

    // ── Custom mouse-wheel inertia ─────────────────────────────────────
    // Unity's ScrollRect.scrollSensitivity moves content per wheel tick
    // INSTANTLY — no momentum, no deceleration. That's why the previous
    // "iPhone-style inertia" commit didn't change anything for players
    // using the mouse wheel: Unity's wheel input bypasses the ScrollRect
    // inertia model entirely (inertia only applies to drag releases).
    //
    // Fix: drive our own velocity accumulator off Input.mouseScrollDelta.
    // Each wheel tick ADDS to velocity instead of teleporting position.
    // Each frame we apply velocity-as-position-delta and exponentially
    // decay it. Net effect: a single tick coasts and stops; repeated
    // ticks compound velocity so flicks travel far; idle frames decay
    // back to zero. Built-in ScrollRect wheel handling is neutralised
    // by setting scrollSensitivity = 0 in BuildUI.
    float _wheelVelocityPx = 0f;
    // Pixels of velocity added per wheel-tick. Tuned so a single tick
    // moves ~200 px of content over its lifetime (matches feel of the
    // dense bubble layout where each bubble is ~30-40 px tall).
    const float WheelImpulsePx     = 700f;
    // Exponential decay rate per second. Higher = faster stop. 6.0
    // gives a perceptible coast (~0.4 s to fade to ~10%) without
    // feeling slippery.
    const float WheelDecayPerSec   = 6f;
    // Below this magnitude we snap velocity to zero — avoids
    // sub-pixel drift that never quite reaches rest.
    const float WheelVelocityFloor = 1f;

    // ── Paced reveal state ─────────────────────────────────────────────
    // Tokens arrive from the LLM as fast as the model produces them — on
    // a 3070-CUDA path that's 40–60 tok/s, which lands in the chat as a
    // chaotic burst that breaks the HAL-calm feel. We instead buffer the
    // cumulative target text and reveal it character-by-character at a
    // constant rate. RevealLoop drives the label; the LLM callbacks only
    // update the target. Idle openers (cold opener / standing-by line)
    // reuse the same pipeline.
    TextMeshProUGUI _streamingLabel;
    string          _streamTargetText = "";
    int             _streamRevealedChars;
    float           _streamRevealAccum;
    Coroutine       _revealCoroutine;
    int             _tickCounter;
    bool            _isRevealing = false;
    const float     RevealCharsPerSecond = 40f;
    const int       TickEveryNChars      = 3;  // throttle tick SFX so we don't oversubscribe AudioSource voices

    /// <summary>True while text is actively being revealed character-by-character.</summary>
    public bool IsRevealing => _isRevealing;

    // (The old AIChatScreen-local idle "standing by" timer is gone — it
    // was superseded by HALCommentator's ambient observations, which now
    // fire whether the chat is open or closed and surface as bubbles via
    // HALVolunteeredLog. One stream, not two.)

    // ── Audio ──────────────────────────────────────────────────────────
    // Single AudioSource: hum loops on .clip + .Play(); tick fires via
    // PlayOneShot per revealed character (throttled). Both clips are
    // generated procedurally and cached statically so the file is fully
    // self-contained (no AudioClip imports needed in the project).
    AudioSource     _audio;
    AudioClip       _humClip;
    AudioClip       _tickClip;
    float           _humFadeStartedAt;
    const float     HumFadeInSeconds  = 1.0f;
    const float     HumTargetVolume   = 0.05f;

    public void Init(System.Action onExit)
    {
        _onExitCallback = onExit;
        BuildUI();
        RestoreHistoryToUI();

        // Naming flow retired — the authored briefing is the first interaction (typed input is
        // gone, so the old name-capture would soft-lock the player). Mark first-contact complete
        // so any legacy name-gated code is satisfied; names resolve to their defaults.
        NameStore.FirstContactComplete = true;
        {
            _firstContact = FirstContactState.Complete;
            // Show story dialogue when there's a pending beat OR the opening has begun
            // (FirstContact onward) so the free-time "talk to AI" menu (conv_menu) is always
            // reachable. Before the briefing (ColdOpen) fall back to the ambient cold opener.
            bool hasStory = StoryDirector.Instance != null
                && (StoryDirector.Instance.HasPendingConversation
                    || StoryDirector.Instance.CurrentStoryStep >= StoryStep.FirstContact);
            if (hasStory)
                StartContextualDialogue();
            else
                AddColdOpener();
        }

        // Subscribe to live volunteered lines so any ambient observation,
        // commentator reaction, or enemy-proximity warning that fires while
        // the chat is open shows up as a bubble in real time.
        if (HALVolunteeredLog.Instance != null)
            HALVolunteeredLog.Instance.OnLineAdded += HandleVolunteeredLine;
    }

    void BuildUI()
    {
        // Use this component's own GameObject as the root — no wrapper
        // duplication (the caller has already parented us correctly under
        // PlayerPhoneUI._screenRT with anchors/offsets).
        _root = (RectTransform)transform;

        var rootBg = gameObject.AddComponent<Image>();
        rootBg.color = ScreenBg;
        rootBg.raycastTarget = true;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 4f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        BuildHeader(_root);
        BuildMessageList(_root);
        // Opening revamp: typed input removed. Preset replies are shown by
        // DialogueReplyColumn to the right of the phone chassis (PhoneDialoguePresenter).
        // BuildInputRow(_root);
        BuildAudio();
    }

    void BuildAudio()
    {
        _humClip  = GenerateHumClip();
        _tickClip = GenerateTickClip();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.clip = _humClip;
        _audio.loop = true;
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;  // pure 2D
        _audio.volume = 0f;        // fade in via UpdateHum
        _audio.Play();
        _humFadeStartedAt = Time.unscaledTime;
    }

    void BuildHeader(RectTransform parent)
    {
        var header = NewUI("Header", parent);
        var le = header.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 24f;  // taller than the old "?" header to give the eye room to breathe
        var hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        // Back arrow.
        var backRT = NewUI("Back", header);
        backRT.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;
        var backText = MakeText(backRT, "<", 16, AccentCyan, TextAnchor.MiddleCenter);
        backText.fontStyle = FontStyles.Bold;
        backText.raycastTarget = true;
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(Exit);

        // HAL eye — parented directly to `header` with ignoreLayout so the
        // HorizontalLayoutGroup doesn't reserve space for it. Anchored to
        // (0.5, 0.5) of the header so it's centered against the FULL header
        // width regardless of the back arrow's width on the left.
        var glowRT = NewUI("HalEyeGlow", header);
        glowRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        glowRT.anchorMin = new Vector2(0.5f, 0.5f);
        glowRT.anchorMax = new Vector2(0.5f, 0.5f);
        glowRT.pivot     = new Vector2(0.5f, 0.5f);
        glowRT.sizeDelta = new Vector2(22f, 22f);
        glowRT.anchoredPosition = Vector2.zero;
        _halEyeGlow = glowRT.gameObject.AddComponent<Image>();
        _halEyeGlow.sprite = HALVisuals.Disc();
        _halEyeGlow.color = new Color(HALVisuals.EyeRed.r, HALVisuals.EyeRed.g, HALVisuals.EyeRed.b, 0.25f);
        _halEyeGlow.raycastTarget = false;

        // Inner core — the actual "eye". Solid red, pulses opposite-phase to
        // the glow so the eye reads as alive even when one component is dim.
        var coreRT = NewUI("HalEyeCore", header);
        coreRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        coreRT.anchorMin = new Vector2(0.5f, 0.5f);
        coreRT.anchorMax = new Vector2(0.5f, 0.5f);
        coreRT.pivot     = new Vector2(0.5f, 0.5f);
        coreRT.sizeDelta = new Vector2(10f, 10f);
        coreRT.anchoredPosition = Vector2.zero;
        _halEyeCore = coreRT.gameObject.AddComponent<Image>();
        _halEyeCore.sprite = HALVisuals.Disc();
        _halEyeCore.color = HALVisuals.EyeRed;
        _halEyeCore.raycastTarget = false;
    }

    // (Disc sprite now lives in HALVisuals.Disc() — shared with HALLineHUD.)

    void BuildMessageList(RectTransform parent)
    {
        var viewport = NewUI("ScrollViewport", parent);
        var vpLE = viewport.gameObject.AddComponent<LayoutElement>();
        vpLE.flexibleHeight = 1f;
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0); // invisible but enables raycasting

        var content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        var cvlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.padding = new RectOffset(4, 4, 4, 4);
        cvlg.spacing = 6f;
        cvlg.childAlignment = TextAnchor.UpperLeft;
        cvlg.childControlWidth = true; cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true; cvlg.childForceExpandHeight = false;
        var csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _messageContent = content;
        _scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
        _scrollRect.viewport = viewport;
        _scrollRect.content = content;
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        // Clamped (no elastic bounce at the bottom) — chat UX wants the
        // latest message to be a HARD floor: drags can't carry past it,
        // so the player never gets bounced AWAY from the message they
        // just opened the app to read.
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        // scrollSensitivity = 0 disables Unity's built-in wheel handling
        // (which teleports per-tick with no momentum). We handle the wheel
        // ourselves in UpdateWheelInertia — see the _wheelVelocityPx state
        // up top. Drag-and-release input still uses the ScrollRect's own
        // inertia path (next two lines).
        _scrollRect.scrollSensitivity = 0f;
        // iPhone-style momentum for drag releases: release the drag and
        // the view continues to coast, decelerating smoothly to a stop.
        // Repeated drags compound velocity → fast flicks travel farther;
        // quick taps stop quickly.
        _scrollRect.inertia = true;
        _scrollRect.decelerationRate = 0.135f; // Unity default; matches iOS UIScrollView
    }

    void BuildInputRow(RectTransform parent)
    {
        var row = NewUI("InputRow", parent);
        _inputRow = row;
        // Sized dynamically by ResizeInputRowToFit each frame — starts at
        // the min and grows up to InputRowMaxHeight as the text wraps.
        _inputRowLE = row.gameObject.AddComponent<LayoutElement>();
        _inputRowLE.minHeight       = InputRowMinHeight;
        _inputRowLE.preferredHeight = InputRowMinHeight;
        _inputRowLE.flexibleHeight  = 0f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(2, 2, 2, 2);
        hlg.spacing = 4f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

        // Input field.
        var inputRT = NewUI("Input", row);
        _inputFieldLE = inputRT.gameObject.AddComponent<LayoutElement>();
        _inputFieldLE.flexibleWidth   = 1f;
        _inputFieldLE.preferredHeight = InputRowMinHeight - 4f;
        _inputFieldLE.minHeight       = InputRowMinHeight - 4f;
        _inputFieldLE.flexibleHeight  = 0f;
        var inputBg = inputRT.gameObject.AddComponent<Image>();
        inputBg.color = TileBg;
        inputBg.raycastTarget = true;
        _inputField = inputRT.gameObject.AddComponent<TMP_InputField>();

        // TextArea — required wrapper for TMP_InputField. Mirrors the
        // working SaveLoadUI pattern (SaveLoadUI.cs around line 449).
        var textAreaRT = NewUI("TextArea", inputRT);
        _textAreaRT = textAreaRT;
        textAreaRT.anchorMin = Vector2.zero; textAreaRT.anchorMax = Vector2.one;
        textAreaRT.offsetMin = new Vector2(4f, 2f); textAreaRT.offsetMax = new Vector2(-4f, -2f);
        textAreaRT.gameObject.AddComponent<RectMask2D>();

        // Text component (child of TextArea).
        var textRT = NewUI("Text", textAreaRT);
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero; textRT.offsetMax = Vector2.zero;
        var textComp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        textComp.fontSize = 11;
        textComp.color = LabelWhite;
        textComp.alignment = TextAlignmentOptions.MidlineLeft;
        textComp.raycastTarget = false;
        HudFontResolver.Apply(textComp);
        _textComp = textComp;

        // Placeholder (child of TextArea). Positioned BELOW the caret in
        // sibling order — but TMP_InputField parents its runtime-created
        // Caret as the FIRST sibling of the text component's parent, so
        // an empty + focused input would render the placeholder ON TOP of
        // the caret (hiding it). We work around that by hiding the
        // placeholder entirely while the field has focus.
        var placeRT = NewUI("Placeholder", textAreaRT);
        placeRT.anchorMin = Vector2.zero; placeRT.anchorMax = Vector2.one;
        placeRT.offsetMin = Vector2.zero; placeRT.offsetMax = Vector2.zero;
        var placeComp = placeRT.gameObject.AddComponent<TextMeshProUGUI>();
        placeComp.fontSize = 11;
        placeComp.color = new Color(LabelWhite.r, LabelWhite.g, LabelWhite.b, 0.4f);
        placeComp.alignment = TextAlignmentOptions.TopLeft;
        placeComp.text = "Type a message...";
        placeComp.raycastTarget = false;
        HudFontResolver.Apply(placeComp);
        _placeholderGO = placeRT.gameObject;

        // Top-left text alignment matches the multi-line wrap pattern —
        // first line at top, subsequent lines stack downward.
        textComp.alignment = TextAlignmentOptions.TopLeft;
        textComp.enableWordWrapping = true;

        _inputField.textComponent = textComp;
        _inputField.placeholder   = placeComp;
        _inputField.textViewport  = textAreaRT;
        // MultiLineSubmit — text wraps to multiple visible lines, Enter
        // still submits (does NOT insert a newline character). The input
        // rows height grows upward as more lines are needed.
        _inputField.lineType      = TMP_InputField.LineType.MultiLineSubmit;
        _inputField.characterLimit = 500;
        // Make the blinking caret clearly visible — bright cyan, 3 px wide.
        _inputField.caretWidth        = 3;
        _inputField.caretBlinkRate    = 0.85f;
        _inputField.customCaretColor  = true;
        _inputField.caretColor        = AccentCyan;
        _inputField.selectionColor    = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.35f);
        _inputField.onSelect.AddListener(_ =>
        {
            IsTypingActive = true;
            if (_placeholderGO != null) _placeholderGO.SetActive(false);
        });
        _inputField.onDeselect.AddListener(_ =>
        {
            IsTypingActive = false;
            if (_placeholderGO != null && _inputField != null && string.IsNullOrEmpty(_inputField.text))
                _placeholderGO.SetActive(true);
        });
        _inputField.onSubmit.AddListener(_ => OnSendClicked());

        // ── Custom blinking caret ──────────────────────────────────────
        // Sibling of the text component (inside TextArea so RectMask2D
        // clips it). Added LAST so its render order is on top of Text +
        // Placeholder. Anchored to TOP-LEFT of TextArea so the caret
        // sits at the top of the input where text starts (text component
        // uses TopLeft alignment so the first line is at the top). For
        // multi-line content, UpdateCustomCaret moves the caret down by
        // (lineIndex * lineHeight) so it tracks to the current line.
        var caretRT = NewUI("Caret", textAreaRT);
        caretRT.anchorMin = new Vector2(0f, 1f);
        caretRT.anchorMax = new Vector2(0f, 1f);
        caretRT.pivot     = new Vector2(0f, 1f);
        caretRT.sizeDelta = new Vector2(2f, 14f);
        caretRT.anchoredPosition = new Vector2(0f, -1f);  // small top inset
        _customCaretImg = caretRT.gameObject.AddComponent<Image>();
        _customCaretImg.color = AccentCyan;
        _customCaretImg.raycastTarget = false;
        _customCaretImg.enabled = false;   // off until focused
        _customCaretRT = caretRT;

        // Send button.
        var sendRT = NewUI("Send", row);
        var sendLE = sendRT.gameObject.AddComponent<LayoutElement>();
        sendLE.preferredWidth   = 28f;
        sendLE.minWidth         = 28f;
        sendLE.flexibleWidth    = 0f;
        sendLE.preferredHeight  = 24f;
        sendLE.minHeight        = 24f;
        sendLE.flexibleHeight   = 0f;
        var sendBg = sendRT.gameObject.AddComponent<Image>();
        sendBg.color = TileBg;
        sendBg.raycastTarget = true;
        _sendGlyph = MakeText(sendRT, ">", 14, AccentCyan, TextAnchor.MiddleCenter);
        _sendGlyph.fontStyle = FontStyles.Bold;
        _sendButton = sendRT.gameObject.AddComponent<Button>();
        _sendButton.onClick.AddListener(OnSendClicked);
    }

    void RestoreHistoryToUI()
    {
        var store = AIMemoryStore.Instance;
        if (store != null)
        {
            int n = Mathf.Min(store.RecentUserTurns.Count, store.RecentAITurns.Count);
            for (int i = 0; i < n; i++)
            {
                AddUserBubble(store.RecentUserTurns[i]);
                AddAIBubble(WrapAIReply(store.RecentAITurns[i]));
            }
        }
        // Append the persistent HAL volunteered-line transcript after the
        // chat history. We don't have timestamps to interleave perfectly
        // (AIMemoryStore turns don't track them), so chat-replies come
        // first, then HAL's notifications. Same content the player saw in
        // the HUD strip, now scrollable.
        var log = HALVolunteeredLog.Instance;
        if (log != null)
        {
            for (int i = 0; i < log.Lines.Count; i++)
                AddNotificationBubble(WrapAIReply(log.Lines[i]));
        }
        // Chat opening — always land on the latest message and re-arm
        // stickiness, regardless of any previous scroll position. The
        // ForceScrollToBottomNextFrame variant sets _stickToBottom = true
        // BEFORE doing the scroll, so subsequent new bubbles will keep
        // auto-scrolling until the player drags up.
        ForceScrollToBottomNextFrame();
    }

    // Handles a live volunteered line (commentator reaction, ambient
    // observation, or enemy warning) that fired while the chat panel is
    // open. Routes through the paced-reveal pipeline so the bubble appears
    // in HAL's normal cadence, not as a wall of text.
    void HandleVolunteeredLine(string line)
    {
        var label = AddNotificationBubble("");
        StartPacedReveal(label, WrapAIReply(line));
        ScrollToBottomNextFrame();
    }

    // Notification-style AI bubble — same shape as a chat-reply bubble but
    // with HAL-red text instead of cyan so the player can tell at a glance
    // "this was something HAL volunteered" from "this was HAL replying to
    // me". The bubble background stays dark to keep contrast readable.
    TextMeshProUGUI AddNotificationBubble(string text)
    {
        return MakeBubble("HALNotification", text, HALVisuals.EyeRed, ScreenBg, TextAlignmentOptions.MidlineLeft, true);
    }

    void Update()
    {
        // Esc closes the chat (only when NOT typing — otherwise typing Esc
        // in the field would dismiss the chat mid-sentence).
        if (Input.GetKeyDown(KeyCode.Escape) && !IsTypingActive)
        {
            Exit();
            return;
        }

        // Enter sends — handled here as a belt-and-suspenders backup to
        // TMP_InputField.onSubmit, which has historically been finicky.
        if (IsTypingActive &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            OnSendClicked();
            return;
        }

        // Live send-button state.
        bool busy = LLMService.Instance != null && LLMService.Instance.IsResponding;
        if (_sendGlyph != null) _sendGlyph.color = busy ? ButtonGrey : AccentCyan;
        if (_sendButton != null) _sendButton.interactable = !busy;

        // Keep bubble heights in sync with their wrapped text length —
        // length-change-detected so we don't pay TMP ForceMeshUpdate cost
        // when nothing has changed.
        ResizeBubblesToFit();

        // Grow the input row upward as typed text wraps. Length-change-
        // detected like ResizeBubblesToFit so we only re-measure when the
        // string actually changed.
        ResizeInputRowToFit();

        UpdateCustomCaret();
        UpdateHalEye();
        UpdateHum();

        // Custom mouse-wheel inertia (replaces Unity's per-tick teleport).
        // Must run BEFORE UpdateStickyBottomState so the resulting position
        // change is reflected in the sticky read this frame.
        UpdateWheelInertia();

        // Track whether the player is currently parked at the bottom of
        // the chat. If they scrolled up to read, _stickToBottom flips
        // false and new content stops auto-scrolling; if they scroll
        // back down, it flips true and auto-scroll re-engages.
        UpdateStickyBottomState();
    }

    // Fades the ambient hum from 0 → HumTargetVolume over HumFadeInSeconds.
    // No fade-out — Exit() stops the source outright when the chat closes.
    void UpdateHum()
    {
        if (_audio == null) return;
        if (_audio.volume >= HumTargetVolume) return;
        float t = (Time.unscaledTime - _humFadeStartedAt) / HumFadeInSeconds;
        _audio.volume = Mathf.Clamp01(t) * HumTargetVolume;
    }

    // ── Paced reveal pipeline ──────────────────────────────────────────

    void EnsureRevealLoop()
    {
        if (_revealCoroutine == null) _revealCoroutine = StartCoroutine(RevealLoop());
    }

    IEnumerator RevealLoop()
    {
        _isRevealing = true;
        try
        {
            while (true)
            {
                if (_streamingLabel == null)
                {
                    _revealCoroutine = null;
                    yield break;
                }
                if (string.IsNullOrEmpty(_streamTargetText))
                {
                    yield return null;
                    continue;
                }
                if (_streamRevealedChars >= _streamTargetText.Length)
                {
                    // Caught up to the current target. Exit only if no more
                    // text is expected — i.e. the LLM stream is done. Cold
                    // openers and idle lines have IsResponding=false from
                    // the start, so they hit this branch immediately after
                    // the last char and clean up.
                    bool modelBusy = LLMService.Instance != null && LLMService.Instance.IsResponding;
                    if (!modelBusy)
                    {
                        _revealCoroutine = null;
                        yield break;
                    }
                    yield return null;
                    continue;
                }

                // Fractional accumulator avoids rate drift from per-frame
                // rounding when the framerate fluctuates.
                _streamRevealAccum += Time.unscaledDeltaTime * RevealCharsPerSecond;
                int chunk = (int)_streamRevealAccum;
                if (chunk > 0)
                {
                    _streamRevealAccum -= chunk;
                    int newCount = Mathf.Min(_streamRevealedChars + chunk, _streamTargetText.Length);
                    // Tick SFX per Nth non-whitespace char — clicks too dense
                    // would just sound like a buzz, so we sparse them out.
                    for (int i = _streamRevealedChars; i < newCount; i++)
                    {
                        _tickCounter++;
                        if (_tickCounter >= TickEveryNChars
                            && _tickClip != null && _audio != null
                            && !char.IsWhiteSpace(_streamTargetText[i]))
                        {
                            _audio.PlayOneShot(_tickClip, 0.06f);
                            _tickCounter = 0;
                        }
                    }
                    _streamRevealedChars = newCount;
                    _streamingLabel.text = _streamTargetText.Substring(0, _streamRevealedChars);
                }
                yield return null;
            }
        }
        finally
        {
            _isRevealing = false;
        }
    }

    // Immediately complete any in-flight reveal — used when the player
    // sends a new message and we want the previous bubble to land at its
    // final text before we move on. Resets all reveal state.
    void SnapRevealToCompletion()
    {
        if (_streamingLabel != null && !string.IsNullOrEmpty(_streamTargetText))
            _streamingLabel.text = _streamTargetText;
        if (_revealCoroutine != null) { StopCoroutine(_revealCoroutine); _revealCoroutine = null; }
        _streamingLabel       = null;
        _streamTargetText     = "";
        _streamRevealedChars  = 0;
        _streamRevealAccum    = 0f;
        _tickCounter          = 0;
    }

    // Drop a pre-written line straight into the reveal pipeline (no LLM
    // call). Used by the cold opener and idle "standing by" lines.
    void StartPacedReveal(TextMeshProUGUI label, string text)
    {
        SnapRevealToCompletion();
        _streamingLabel       = label;
        _streamTargetText     = text ?? "";
        _streamRevealedChars  = 0;
        _streamRevealAccum    = 0f;
        EnsureRevealLoop();
    }

    // ── First-contact scripted exchange ────────────────────────────────
    // Mirrors the cold opener / volunteered-line path: AddAIBubble +
    // StartPacedReveal so the typewriter cadence is identical to normal AI
    // messages. The state machine progresses by capturing whatever
    // {PLAYER_NAME} types into the input field on each OnSendClicked while
    // _firstContact != Complete.

    void StartFirstContact()
    {
        const string greeting =
            "Hello. I'm the assistant built into your phone — I'm here to help " +
            "you stay alive out here and see your mission through. Fishing, " +
            "water, building, ships, the map: anything you need, just ask. " +
            "Before we begin, though — what should I call you?";
        var label = AddAIBubble("");
        StartPacedReveal(label, WrapAIReply(greeting));
        ScrollToBottomNextFrame();
    }

    // Resume variant — the player named themselves in a previous session
    // but closed the phone before naming the AI. Don't re-ask their name
    // (we already have it in NameStore.PlayerName); pick up where we
    // left off with the AI-naming step.
    void StartFirstContactResumeAtAIName()
    {
        string greeting =
            $"Welcome back, {NameStore.ResolvedPlayerName}. We didn't quite " +
            "finish last time — I still don't have a name. Some people like " +
            "to give their assistant one. Would you like to name me? Say no " +
            "to skip.";
        var label = AddAIBubble("");
        StartPacedReveal(label, WrapAIReply(greeting));
        ScrollToBottomNextFrame();
    }

    // Called from OnSendClicked when _firstContact != Complete. Advances
    // the state, captures the player's input into NameStore, and produces
    // the next scripted message in the exchange. Returns true if the input
    // was consumed by first-contact (and the LLM path should NOT run).
    bool HandleFirstContactInput(string rawInput)
    {
        string cleaned = NameStore.Sanitize(rawInput);

        if (_firstContact == FirstContactState.CapturingPlayerName)
        {
            // Empty input → reprompt without advancing state. We treat
            // first-contact as a required step; if the player gives nothing
            // we ask again rather than defaulting to "Player".
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                var label = AddAIBubble("");
                StartPacedReveal(label, WrapAIReply("I didn't catch that — what should I call you?"));
                ScrollToBottomNextFrame();
                return true;
            }

            NameStore.PlayerName = cleaned;
            string ack =
                $"Lovely to meet you, {cleaned}. We're going to get along just fine. " +
                "One thing first — I don't have a name. Some people like to give " +
                "their assistant one. Would you like to name me? Say no to skip.";
            var label2 = AddAIBubble("");
            StartPacedReveal(label2, WrapAIReply(ack));
            ScrollToBottomNextFrame();
            _firstContact = FirstContactState.CapturingAIName;
            return true;
        }

        if (_firstContact == FirstContactState.CapturingAIName)
        {
            // Decline detection — accept the common short refusals. Anything
            // else is treated as the chosen name.
            string lower = cleaned.ToLowerInvariant();
            bool declined =
                lower == "no" || lower == "no thanks" || lower == "no thank you" ||
                lower == "skip" || lower == "decline" || lower == "pass" ||
                lower == "none" || lower == "nope";

            if (declined || string.IsNullOrWhiteSpace(cleaned))
            {
                NameStore.AIName = ""; // resolved as "Assistant" by NameStore.ResolvedAIName
                string declineMsg =
                    $"That's all right — you can name me later if you change your " +
                    $"mind, just say the word. For now, let's get to work, {NameStore.ResolvedPlayerName}.";
                var label = AddAIBubble("");
                StartPacedReveal(label, WrapAIReply(declineMsg));
            }
            else
            {
                NameStore.AIName = cleaned;
                string ack =
                    $"{cleaned} it is — I like it. We're going to get along just fine, " +
                    $"{NameStore.ResolvedPlayerName}. Now, let's get you on your feet.";
                var label = AddAIBubble("");
                StartPacedReveal(label, WrapAIReply(ack));
            }
            ScrollToBottomNextFrame();
            NameStore.FirstContactComplete = true;
            _firstContact = FirstContactState.Complete;
            return true;
        }

        return false; // _firstContact == Complete — caller handles via LLM
    }

    // ── Cold opener ────────────────────────────────────────────────────

    void AddColdOpener()
    {
        string line = ComputeColdOpener();
        var label = AddAIBubble("");
        StartPacedReveal(label, WrapAIReply(line));
        ScrollToBottomNextFrame();
    }

    // Picks one short observation from live game state. Priority order:
    // critical vitals → mid vitals → planet / generic fallbacks. The "it
    // knows things I didn't tell it" reading is core HAL — the player just
    // opened the app, no conversation has happened, and the entity already
    // reports a number from somewhere inside them.
    static string ComputeColdOpener()
    {
        var rm = ResourceManager.Instance;
        int astronaut = (rm != null ? rm.TotalDeaths : 0) + 1;
        string planet = TokenResolver.Resolve("{CURRENT_PLANET}");

        if (rm != null)
        {
            float h  = rm.HungerPercent * 100f;
            float t  = rm.ThirstPercent * 100f;
            float hp = rm.HealthPercent * 100f;
            var piloted = Ship.PilotedInstance;
            float sp = piloted != null ? piloted.PowerPercent * 100f : -1f;
            float sf = piloted != null ? piloted.FuelPercent  * 100f : -1f;

            if (hp < 35f) return $"Your physical integrity reads {Mathf.RoundToInt(hp)} percent, Astronaut. I would advise caution.";
            if (t  < 25f) return $"Your hydration is at {Mathf.RoundToInt(t)} percent. Address this.";
            if (h  < 25f) return $"Your hunger reads {Mathf.RoundToInt(h)} percent. Locate sustenance.";
            if (sf >= 0f && sf < 20f) return $"The reactor is at {Mathf.RoundToInt(sf)} percent fuel. Insert crystals.";
            if (sp >= 0f && sp < 20f) return $"Ship power is at {Mathf.RoundToInt(sp)} percent. Solar exposure recommended.";
        }

        // Non-critical fallbacks — rotate so repeat opens feel slightly
        // varied without ever feeling chatty. _lastColdOpenerIdx prevents
        // the same fallback firing twice in a row even though the picker
        // is otherwise random.
        var fallbacks = new[]
        {
            $"You are at {planet}, Astronaut.",
            $"Vital signs nominal, Astronaut Number {astronaut}.",
            $"I have been observing your progress.",
            $"Awaiting your query, Astronaut.",
            $"Standing by. Astronaut Number {astronaut} on station."
        };
        int idx;
        do { idx = Random.Range(0, fallbacks.Length); } while (idx == _lastColdOpenerIdx && fallbacks.Length > 1);
        _lastColdOpenerIdx = idx;
        return fallbacks[idx];
    }
    static int _lastColdOpenerIdx = -1;

    // ── Contextual dialogue (preset conversations from StoryDirector) ────
    // Runs when there's a pending conversation queued (takes priority over
    // the cold opener). Builds the reply column and starts the DialogueRunner.

    void StartContextualDialogue()
    {
        var sd = StoryDirector.Instance;
        if (sd == null) { if (_replyColumn != null) _replyColumn.Clear(); return; }

        string convId; string startNode = null;
        if (sd.HasPendingConversation)
        {
            convId = sd.PendingConversationId;
            startNode = sd.PendingNodeId;
            sd.ClearPendingConversation();
        }
        else
        {
            convId = "conv_menu";   // authored free-time "talk to AI" menu (added later; may be absent now)
        }

        var conv = StoryContent.GetConversation(convId);
        if (conv == null) { if (_replyColumn != null) _replyColumn.Clear(); return; }   // nothing to say yet

        // Build the preset-reply column to the right of the phone, then start the conversation.
        var phoneUI  = PlayerPhoneUI.Instance;
        var canvasTf = transform.root;                       // the phone canvas
        var chassis  = phoneUI != null ? phoneUI.PhoneChassisRect : null;
        _replyColumn = DialogueReplyColumn.Create(canvasTf, chassis);
        _presenter   = new PhoneDialoguePresenter(this, _replyColumn);

        _runner = new DialogueRunner(conv, _presenter);
        if (!string.IsNullOrEmpty(startNode)) _runner.StartAt(startNode); else _runner.Start();
    }

    // Invoked by PhoneDialoguePresenter when an authored conversation reaches its
    // end. Reconcile story gates immediately (so finishing first-contact advances
    // to the next beat without waiting for StoryDirector's 0.5s catch-up timer),
    // and if that queued a follow-up conversation, chain straight into it in this
    // same session. Without this, finishing first-contact left the reply column
    // empty and the next beat ("How do I build a shelter?") only appeared after
    // the player closed and reopened the phone.
    public void OnContextualDialogueEnded()
    {
        var sd = StoryDirector.Instance;
        if (sd == null || _presenter == null) return;

        sd.ReconcileGatesNow();
        if (!sd.HasPendingConversation) return;

        string convId    = sd.PendingConversationId;
        string startNode = sd.PendingNodeId;
        sd.ClearPendingConversation();

        var conv = StoryContent.GetConversation(convId);
        if (conv == null) return;

        // Reuse THIS session's reply column + presenter — DialogueReplyColumn.Create
        // spawns a fresh GameObject, which would leak the old column and stack a
        // second one beside the phone.
        _runner = new DialogueRunner(conv, _presenter);
        if (!string.IsNullOrEmpty(startNode)) _runner.StartAt(startNode); else _runner.Start();
    }

    // ── Generated audio clips (cached statically) ──────────────────────

    static AudioClip _cachedHum;
    static AudioClip GenerateHumClip()
    {
        if (_cachedHum != null) return _cachedHum;
        const int sampleRate = 44100;
        const int samples    = sampleRate;  // 1 s loop — integer cycles below means seamless
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            // 55 Hz sub + 110 Hz fundamental + 220 Hz overtone — same harmonic
            // stack as a deep machine room hum. All frequencies are integer
            // multiples of 1 Hz so a 1 s clip loops without a click.
            float w = Mathf.Sin(2f * Mathf.PI *  55f * t) * 0.30f
                    + Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.55f
                    + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.15f;
            data[i] = w * 0.6f;
        }
        var clip = AudioClip.Create("HalHum", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        _cachedHum = clip;
        return clip;
    }

    static AudioClip _cachedTick;
    static AudioClip GenerateTickClip()
    {
        if (_cachedTick != null) return _cachedTick;
        const int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.04f);  // 40 ms
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            // Exponential decay envelope × 1600 Hz sine = a soft serial-terminal
            // tick. Decay constant 90 lands around -60 dB after 40 ms.
            float env  = Mathf.Exp(-t * 90f);
            float wave = Mathf.Sin(2f * Mathf.PI * 1600f * t);
            data[i] = env * wave * 0.7f;
        }
        var clip = AudioClip.Create("HalTick", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        _cachedTick = clip;
        return clip;
    }

    // Pulse the HAL eye — slow sine-wave breathing on the inner core, with
    // the outer halo pulsing in counter-phase so the silhouette is never
    // flat. While the AI is mid-response, breathe faster (3× rate) so the
    // player feels the system "thinking".
    void UpdateHalEye()
    {
        if (_halEyeCore == null || _halEyeGlow == null) return;
        bool busy = LLMService.Instance != null && LLMService.Instance.IsResponding;
        float rate = busy ? 2.4f : 0.8f;            // rad/s
        float phase = Time.unscaledTime * rate;

        // Core: 0.7 → 1.0
        float coreA = 0.7f + 0.3f * (Mathf.Sin(phase) * 0.5f + 0.5f);
        var cc = _halEyeCore.color; cc.a = coreA; _halEyeCore.color = cc;

        // Halo: 0.10 → 0.40, phase-shifted 180° so it brightens as the core dims
        float haloA = 0.10f + 0.30f * (Mathf.Sin(phase + Mathf.PI) * 0.5f + 0.5f);
        var hc = _halEyeGlow.color; hc.a = haloA; _halEyeGlow.color = hc;
    }

    void UpdateCustomCaret()
    {
        if (_customCaretImg == null || _customCaretRT == null || _inputField == null) return;
        if (!IsTypingActive)
        {
            if (_customCaretImg.enabled) _customCaretImg.enabled = false;
            _caretBlinkTimer = 0f;
            return;
        }

        // Blink on/off using a simple timer (unscaled — works while paused).
        _caretBlinkTimer += Time.unscaledDeltaTime;
        bool show = Mathf.Repeat(_caretBlinkTimer, CaretBlinkRate * 2f) < CaretBlinkRate;
        if (_customCaretImg.enabled != show) _customCaretImg.enabled = show;

        // Compute caret X = position right after the last character in
        // the text, and caret Y offset = -(lineIndex * lineStep) since
        // each wrapped line drops down by one line. Caret is top-anchored
        // so y=0 is top of TextArea; the tiny upward bias below aligns
        // the bar with the letter cap-height.
        //
        // IMPORTANT: use characterInfo[last].xAdvance, NOT lineInfo.length —
        // the latter strips trailing whitespace, which made the caret look
        // frozen when the player held space.
        float x = 0f;
        int   lineIdx = 0;
        if (_textComp != null && !string.IsNullOrEmpty(_inputField.text))
        {
            _textComp.ForceMeshUpdate();
            var info = _textComp.textInfo;
            if (info != null && info.characterCount > 0)
            {
                int lastChar = info.characterCount - 1;
                var ci = info.characterInfo[lastChar];
                x = ci.xAdvance;
                lineIdx = ci.lineNumber;
            }
            else
            {
                x = _textComp.preferredWidth;
            }
        }

        // Caret height — TMP fontSize is the EM box height, but actual
        // glyph cap-height is roughly 70%. Use 80% so the caret matches
        // the letter cap-height rather than dwarfing them. Line spacing
        // for the wrapped-line walk is still the full line-height so
        // subsequent lines land on the next baseline correctly.
        float caretH    = _textComp != null ? _textComp.fontSize * 0.8f  : 10f;
        float lineStep  = _textComp != null ? _textComp.fontSize + 2f    : 14f;
        float y         = 1f - lineIdx * lineStep;   // small upward bias to align with text cap-height
        _customCaretRT.sizeDelta = new Vector2(2f, caretH);
        _customCaretRT.anchoredPosition = new Vector2(x + 1f, y);
    }

    void ResizeInputRowToFit()
    {
        if (_inputField == null || _inputRowLE == null || _inputFieldLE == null) return;
        var text = _inputField.textComponent;
        if (text == null) return;
        int len = _inputField.text != null ? _inputField.text.Length : 0;
        if (len == _lastInputTextLen) return;
        _lastInputTextLen = len;
        text.ForceMeshUpdate();
        float textH = text.preferredHeight;
        float desired = Mathf.Clamp(textH + 12f, InputRowMinHeight, InputRowMaxHeight);
        if (Mathf.Abs(_inputRowLE.preferredHeight - desired) > 0.5f)
        {
            _inputRowLE.preferredHeight = desired;
            _inputRowLE.minHeight = desired;
            _inputFieldLE.preferredHeight = desired - 4f;
            _inputFieldLE.minHeight = desired - 4f;
            if (_inputRow != null) LayoutRebuilder.MarkLayoutForRebuild(_inputRow);
        }
    }

    void OnSendClicked()
    {
        if (LLMService.Instance == null) return;
        if (LLMService.Instance.IsResponding) return;
        var msg = _inputField != null ? _inputField.text?.Trim() : null;
        if (string.IsNullOrEmpty(msg)) return;

        // Snap any in-flight reveal (cold opener, previous AI bubble, idle
        // line) to its full target text so the previous bubble doesn't stay
        // truncated when we start a new one.
        SnapRevealToCompletion();

        // Push player bubble immediately.
        AddUserBubble(msg);

        // Clear the input.
        if (_inputField != null) _inputField.text = "";

        // First-contact path — scripted exchange, no LLM. Consume the input
        // and short-circuit before the typing-dots / LLM call below.
        if (_firstContact != FirstContactState.Complete)
        {
            if (HandleFirstContactInput(msg))
            {
                StartCoroutine(ReactivateInputNextFrame());
                return;
            }
        }

        // Re-focus the input field so the player can type the next message
        // immediately without having to click back into it. Deferred one
        // frame so it survives any EventSystem deselection that TMP triggers
        // on Submit / value-change.
        StartCoroutine(ReactivateInputNextFrame());

        // ── No AI model available ────────────────────────────────────
        // LLM weights were removed from StreamingAssets/AI/ pending a
        // preset-dialogue replacement. With no model loaded, skip the
        // AI bubble + typing dots + Chat call so the chat just records
        // the user's message and waits silently. Future preset-dialogue
        // logic can hook in here to pick a canned response.
        if (LLMService.Instance == null || !LLMService.Instance.IsModelAvailable)
        {
            return;
        }

        // Spawn an AI bubble in "typing" state.
        var aiLabel = AddAIBubble("");
        _activeStreamLabel = aiLabel;
        if (_typingDotsRoutine != null) StopCoroutine(_typingDotsRoutine);
        _typingDotsRoutine = StartCoroutine(TypingDotsLoop(aiLabel));

        // LLMUnity's streaming callback delivers the CUMULATIVE text so far.
        // We feed it into _streamTargetText; RevealLoop drives the visible
        // bubble at ~40 chars/sec so the reveal feels HAL-paced regardless
        // of model speed.
        LLMService.Instance.Chat(msg,
            onToken: tok =>
            {
                // If the chat screen was destroyed (player closed the phone
                // before the response arrived) the captured `this` is a dead
                // Object. Unity's overloaded `==` treats destroyed Objects as
                // == null, so this is the canonical guard.
                if (this == null || !isActiveAndEnabled) return;

                // Don't bind the reveal or stop the typing dots while the
                // model is still inside its <think> block. LLMService strips
                // think tags before we see `tok`, so during the think block
                // `tok` arrives as empty (or whitespace) for many seconds.
                // If we wrap "" with WrapAIReply we'd get "claude: " which the
                // reveal pipeline would type out and then freeze on while
                // the model finishes thinking, making the prefix look stuck.
                // Skip empty tokens; let the typing dots keep running until
                // real visible content arrives.
                string visible = tok ?? "";
                if (string.IsNullOrWhiteSpace(visible)) return;

                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                if (aiLabel != null && _streamingLabel != aiLabel)
                {
                    // First REAL token for this bubble — bind the reveal target.
                    _streamingLabel = aiLabel;
                    _streamRevealedChars = 0;
                    _streamRevealAccum = 0f;
                }
                _streamTargetText = WrapAIReply(visible);
                EnsureRevealLoop();
                ScrollToBottomNextFrame();
            },
            onComplete: full =>
            {
                // Same destroyed-check as onToken. The final completion
                // callback also tries StartCoroutine via EnsureRevealLoop,
                // so it must guard too.
                if (this == null || !isActiveAndEnabled) return;
                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                // Use `full` (the stripped final text from LLMService) as the
                // source of truth — NOT _streamTargetText, which was already
                // wrapped by WrapAIReply during streaming and would
                // double-wrap to "claude: claude: ..." if re-wrapped here.
                string finalText = full ?? "";
                if (aiLabel != null && _streamingLabel != aiLabel)
                {
                    _streamingLabel = aiLabel;
                    _streamRevealedChars = 0;
                    _streamRevealAccum = 0f;
                }
                _streamTargetText = WrapAIReply(finalText);
                EnsureRevealLoop();
                _activeStreamLabel = null;

                // Record the raw final text (without the name prefix) — the
                // prefix is a display concern, not part of the model's reply.
                if (AIMemoryStore.Instance != null)
                    AIMemoryStore.Instance.RecordTurn(msg, finalText);

                ScrollToBottomNextFrame();
            });
    }

    IEnumerator ReactivateInputNextFrame()
    {
        yield return null;
        if (_inputField == null) yield break;
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(_inputField.gameObject);
        _inputField.ActivateInputField();
        // Force-set the typing-active flag too — the onSelect listener
        // fires from EventSystem state changes which can race with
        // ActivateInputField on the same frame.
        IsTypingActive = true;
        if (_placeholderGO != null) _placeholderGO.SetActive(false);
    }

    IEnumerator TypingDotsLoop(TextMeshProUGUI label)
    {
        int frame = 0;
        var dots = new[] { ".", "..", "..." };
        while (label != null)
        {
            label.text = dots[frame % dots.Length];
            frame++;
            yield return new WaitForSecondsRealtime(0.35f);
        }
    }

    TextMeshProUGUI AddUserBubble(string text)
    {
        var bubble = MakeBubble("UserBubble", text, LabelWhite, TileBg, TextAlignmentOptions.MidlineRight, false);
        return bubble;
    }

    TextMeshProUGUI AddAIBubble(string text)
    {
        var bubble = MakeBubble("AIBubble", text, AccentCyan, ScreenBg, TextAlignmentOptions.MidlineLeft, true);
        return bubble;
    }

    TextMeshProUGUI MakeBubble(string name, string text, Color textColor, Color bgColor, TextAlignmentOptions align, bool leftAligned)
    {
        // Row spans the full content width and sits at the top of where the
        // VLG places it. Pivot top-left so the row's own height grows
        // downward as the bubble grows.
        var row = NewUI(name + "_Row", _messageContent);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.minHeight = 20f;            // ensure a single-line bubble has visible height
        rowLE.flexibleHeight = 0f;

        // Bubble is anchored to one side of the row directly via anchors —
        // no nested LayoutGroup needed, and no risk of LayoutGroup +
        // pivot math clipping the leftmost glyph.
        var bubble = NewUI(name, row);
        const float BubbleMaxFraction = 0.78f;  // up to 78% of row width
        if (leftAligned)
        {
            bubble.anchorMin = new Vector2(0f, 0f);
            bubble.anchorMax = new Vector2(BubbleMaxFraction, 1f);
        }
        else
        {
            bubble.anchorMin = new Vector2(1f - BubbleMaxFraction, 0f);
            bubble.anchorMax = new Vector2(1f, 1f);
        }
        bubble.offsetMin = Vector2.zero;
        bubble.offsetMax = Vector2.zero;
        bubble.pivot     = new Vector2(0.5f, 0.5f);

        var bg = bubble.gameObject.AddComponent<Image>();
        bg.color = bgColor;
        bg.raycastTarget = false;

        // Text fills the bubble with a small padding inset.
        var labelRT = NewUI(name + "_Label", bubble);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(6f, 3f);
        labelRT.offsetMax = new Vector2(-6f, -3f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 10;
        label.color = textColor;
        label.alignment = leftAligned ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        HudFontResolver.Apply(label);

        // Register so Update can keep the row tall enough for the wrapped
        // text as it streams or wraps differently on resize.
        _bubbles.Add(new BubbleEntry { Row = row, RowLE = rowLE, Label = label, LastShownLen = -1 });

        return label;
    }

    void ResizeBubblesToFit()
    {
        if (_bubbles.Count == 0) return;
        for (int i = 0; i < _bubbles.Count; i++)
        {
            var b = _bubbles[i];
            if (b.Label == null || b.RowLE == null || b.Row == null) continue;
            int currentLen = b.Label.text != null ? b.Label.text.Length : 0;
            if (currentLen == b.LastShownLen) continue; // unchanged
            b.Label.ForceMeshUpdate();
            float h = Mathf.Max(20f, b.Label.preferredHeight + 8f);
            if (Mathf.Abs(b.RowLE.preferredHeight - h) > 0.5f)
            {
                b.RowLE.preferredHeight = h;
                b.RowLE.minHeight = h;
                LayoutRebuilder.MarkLayoutForRebuild(b.Row);
            }
            b.LastShownLen = currentLen;
            _bubbles[i] = b;
        }
    }

    // Called by every new-content path (new bubble, streamed token reveal,
    // notification arrival, history replay) to keep the chat pinned to the
    // latest message — but ONLY when the player is already at or near the
    // bottom (_stickToBottom). If the player has scrolled up to read older
    // history, we leave the view alone. Mirrors iMessage / Discord scroll-
    // lock behaviour.
    void ScrollToBottomNextFrame()
    {
        if (!_stickToBottom) return; // player is reading history — don't yank
        StartCoroutine(ScrollNextFrame());
    }

    // Force scroll-to-bottom regardless of sticky state. Used when the chat
    // opens / re-opens — at that moment we always want the player landing
    // on the latest message, and we want stickiness re-armed so subsequent
    // new content auto-scrolls.
    void ForceScrollToBottomNextFrame()
    {
        _stickToBottom = true;
        StartCoroutine(ScrollNextFrame());
    }

    IEnumerator ScrollNextFrame()
    {
        yield return null;
        if (_scrollRect != null)
        {
            _scrollRect.verticalNormalizedPosition = 0f;
            // Kill any in-flight inertia velocity from a prior drag — otherwise
            // a programmatic jump-to-bottom while the user's flick is still
            // decelerating produces a visible re-bounce after we land.
            _scrollRect.velocity = Vector2.zero;
        }
    }

    // Custom mouse-wheel inertia. Reads Input.mouseScrollDelta.y, adds it
    // to a velocity accumulator (rather than teleporting position the way
    // Unity's built-in handler does), then each frame applies that velocity
    // to the scroll position and exponentially decays it. End result: one
    // wheel tick coasts ~half a second; repeated ticks compound; a flick
    // travels much farther than a tap. Matches the iPhone feel the player
    // asked for. Built-in wheel input is neutralised by scrollSensitivity = 0
    // in BuildUI.
    void UpdateWheelInertia()
    {
        if (_scrollRect == null || _messageContent == null || _scrollRect.viewport == null) return;

        // Read this frame's wheel delta. mouseScrollDelta.y > 0 = wheel
        // rotated forward (player wants OLDER content = scroll content
        // downward visually = vNormPos higher).
        float wheelY = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheelY) > 0.001f)
        {
            _wheelVelocityPx += wheelY * WheelImpulsePx;
        }

        if (Mathf.Abs(_wheelVelocityPx) < WheelVelocityFloor)
        {
            _wheelVelocityPx = 0f;
            return;
        }

        // How much normalized position change does one pixel of content
        // movement correspond to? That depends on the difference between
        // content height and viewport height — the actual scrollable range.
        float scrollRangePx = _messageContent.rect.height - _scrollRect.viewport.rect.height;
        if (scrollRangePx <= 0.5f)
        {
            // Content fits the viewport — nothing to scroll. Bleed velocity.
            _wheelVelocityPx = 0f;
            return;
        }

        float deltaPx = _wheelVelocityPx * Time.unscaledDeltaTime;
        float normalizedDelta = deltaPx / scrollRangePx;
        float newPos = Mathf.Clamp01(_scrollRect.verticalNormalizedPosition + normalizedDelta);
        _scrollRect.verticalNormalizedPosition = newPos;
        // If we hit either boundary, kill velocity so it doesn't build up
        // pushing against a wall.
        if (newPos <= 0f || newPos >= 1f) _wheelVelocityPx = 0f;

        // Exponential decay. unscaledDeltaTime so pause / time-scale tricks
        // don't slow the scroll feel.
        _wheelVelocityPx *= Mathf.Exp(-WheelDecayPerSec * Time.unscaledDeltaTime);
    }

    // Polled every Update to keep _stickToBottom in sync with whatever the
    // player is doing. Hysteresis: scroll AWAY from bottom past
    // StickyExitThreshold → unsticky; scroll BACK within StickyEnterThreshold
    // of bottom → sticky again. Between the two is a dead zone so we don't
    // flicker right at the edge.
    void UpdateStickyBottomState()
    {
        if (_scrollRect == null) return;
        // Only meaningful when there's enough content to scroll. If the
        // content fits the viewport, vNormPos is meaningless — leave sticky
        // on so new bubbles still trigger any layout updates.
        if (_messageContent != null && _scrollRect.viewport != null)
        {
            float contentH  = _messageContent.rect.height;
            float viewportH = _scrollRect.viewport.rect.height;
            if (contentH <= viewportH + 0.5f) { _stickToBottom = true; return; }
        }
        float v = _scrollRect.verticalNormalizedPosition;
        if (_stickToBottom)
        {
            if (v > StickyExitThreshold) _stickToBottom = false;
        }
        else
        {
            if (v <= StickyEnterThreshold) _stickToBottom = true;
        }
    }

    public void Exit()
    {
        IsTypingActive = false;
        if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
        if (_revealCoroutine   != null) { StopCoroutine(_revealCoroutine);   _revealCoroutine   = null; }
        // Unhook the volunteered-line subscription so it doesn't keep
        // calling into a destroyed component.
        if (HALVolunteeredLog.Instance != null)
            HALVolunteeredLog.Instance.OnLineAdded -= HandleVolunteeredLine;
        // Cut ambient hum + drop any queued ticks. Destroy(gameObject) below
        // would handle this too, but explicit Stop() is instant — without it
        // the hum can be heard for a frame as the GameObject tears down.
        if (_audio != null) _audio.Stop();

        // Fire-and-forget extraction (won't block the player).
        var store = AIMemoryStore.Instance;
        if (store != null && store.DirtyForExtraction)
        {
            _ = AIMemoryExtractor.RunAsync();
        }

        // Clean up preset-reply UI (column is parented to the canvas root, not this object).
        if (_replyColumn != null) { Destroy(_replyColumn.gameObject); _replyColumn = null; }
        _runner = null;
        _presenter = null;

        _onExitCallback?.Invoke();
        Destroy(gameObject);
    }

    // ── Public hooks for preset-dialogue system ────────────────────────
    // PhoneDialoguePresenter (Task 12) calls these to post reply bubbles
    // without involving the LLM or text input.

    /// <summary>Post a player-side bubble (used by the preset-reply presenter).</summary>
    public TextMeshProUGUI PostUserLine(string text) => AddUserBubble(text);

    /// <summary>Post an AI bubble and reveal it at the normal paced rate; calls onDone when fully revealed.</summary>
    public void PostAILine(string text, System.Action onDone)
    {
        var label = AddAIBubble("");
        StartPacedReveal(label, WrapAIReply(text));
        StartCoroutine(InvokeAfterReveal(onDone));
    }

    System.Collections.IEnumerator InvokeAfterReveal(System.Action cb)
    {
        while (IsRevealing) yield return null;
        cb?.Invoke();
    }

    // ── Tiny UI helpers (kept local so this file is self-contained) ─
    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        HudFontResolver.Apply(t);
        return t;
    }

    // Compatibility shim for the `TextAnchor`-shaped MakeText calls
    // used in this file. Maps to TMP's TextAlignmentOptions.
    static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, TextAnchor anchor)
    {
        TextAlignmentOptions opt = anchor switch
        {
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleLeft   => TextAlignmentOptions.MidlineLeft,
            TextAnchor.MiddleRight  => TextAlignmentOptions.MidlineRight,
            TextAnchor.UpperLeft    => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter  => TextAlignmentOptions.Top,
            TextAnchor.UpperRight   => TextAlignmentOptions.TopRight,
            _ => TextAlignmentOptions.MidlineLeft,
        };
        return MakeText(parent, text, size, color, opt);
    }
}
