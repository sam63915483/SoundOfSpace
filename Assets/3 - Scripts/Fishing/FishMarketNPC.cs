using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class FishMarketNPC : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI talkPromptText;
    public TextMeshProUGUI greetingText;
    public GameObject sellPanel;

    [Header("Greeting")]
    public string greetingMessage = "Welcome to my humble shop traveller!";
    public float  charDelay       = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] private AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] private float typewriterVolume = 0.3f;
    private AudioSource typewriterSource;

    [Header("Sale Sound")]
    [SerializeField] private AudioClip saleClip;
    [SerializeField, Range(0, 1)] private float saleVolume = 0.7f;
    private AudioSource saleSource;

    [Header("Earnings Display")]
    public float earningsDisplayDuration = 3f;
    public float earningsFadeDuration    = 0.5f;

    // Legacy serialized refs kept for scene compat (unused in new flow)
    [HideInInspector] public Button   sellButton;
    [HideInInspector] public Text     earningsText;
    [HideInInspector] public Text     commonInfoText, uncommonInfoText, rareInfoText;
    [HideInInspector] public Text     commonStagedText, uncommonStagedText, rareStagedText, panelTotalText;
    [HideInInspector] public Button   commonPlusButton, uncommonPlusButton, rarePlusButton, browseFishButton;
    [HideInInspector] public FishingRodController fishingRodController;

    // ── Space dust ─────────────────────────────────────────────────────────────
    NPCSellDustOption _sellDustOption;

    // ── State ──────────────────────────────────────────────────────────────────
    bool playerInRange, panelOpen, greetingActive, _isTyping, _skipTyping, _waitingForClick;
    // Set when StopConversation runs (player picked "Leave" on the choice panel).
    // Suppresses the talk prompt + F-to-talk gate until OnTriggerEnter clears it,
    // so the player can't get stuck in an F → dialogue → Leave → F loop while
    // standing in the trigger zone.
    bool _suppressPromptUntilExit;
    // Phase 4: tuple grows by FishSource so per-fish return-to-source works.
    // RenderTexture stays nullable; the picker doesn't produce one.
    readonly List<(FishEntry fish, RenderTexture rt, FishSource source)> stagedFish = new List<(FishEntry, RenderTexture, FishSource)>();

    // ── Built UI refs ──────────────────────────────────────────────────────────
    Button          uiAddBtn, uiSellBtn;
    TextMeshProUGUI uiListHeader, uiTotalText, uiEarningsMsg;
    CanvasGroup     uiEarningsCG;
    Transform       uiListContent;

    // ── Dependencies ───────────────────────────────────────────────────────────
    Coroutine             greetingCoroutine, earningsCoroutine;
    FishingRodController  rodCtrl;
    GuitarController      guitarCtrl;
    WaterBottleController bottleCtrl;

    // ── Palette ────────────────────────────────────────────────────────────────
    static readonly Color32 C_PanelBg  = new Color32(8,   14,  26,  252);
    static readonly Color32 C_CardBg   = new Color32(18,  26,  48,  255);
    static readonly Color32 C_Divider  = new Color32(45,  60,  95,  255);
    static readonly Color32 C_ScrollBg = new Color32(5,   8,   18,  255);
    static readonly Color32 C_Title    = new Color32(60,  220, 190, 255);
    static readonly Color32 C_Label    = new Color32(220, 230, 255, 255);
    static readonly Color32 C_Sub      = new Color32(120, 140, 185, 255);
    static readonly Color32 C_Hint     = new Color32(65,  80,  115, 255);
    static readonly Color32 C_Gold     = new Color32(255, 215, 50,  255);
    static readonly Color32 C_Common   = new Color32(140, 175, 255, 255);
    static readonly Color32 C_Uncommon = new Color32(60,  220, 190, 255);
    static readonly Color32 C_Rare     = new Color32(255, 210, 50,  255);
    static readonly Color32 C_BtnAdd   = new Color32(40,  95,  200, 255);
    static readonly Color32 C_BtnSell  = new Color32(35,  165, 80,  255);
    static readonly Color32 C_BtnRem   = new Color32(170, 32,  32,  255);

    // ── Unity lifecycle ────────────────────────────────────────────────────────
    void Start()
    {
        rodCtrl    = FindObjectOfType<FishingRodController>();
        guitarCtrl = FindObjectOfType<GuitarController>();
        bottleCtrl = FindObjectOfType<WaterBottleController>();

        if (sellPanel != null) { HideOldChildren(); BuildUI(); }
        if (sellPanel       != null) sellPanel.SetActive(false);
        if (talkPromptText  != null) talkPromptText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);
        if (greetingText    != null) greetingText.gameObject.SetActive(false);

        DialogueTextStyling.ApplyOutline(talkPromptText);
        DialogueTextStyling.ApplyOutline(greetingText);

        typewriterSource = GetComponent<AudioSource>();
        if (typewriterSource == null) typewriterSource = gameObject.AddComponent<AudioSource>();
        typewriterSource.playOnAwake = false;
        typewriterSource.loop = true;
        typewriterSource.volume = typewriterVolume;

        saleSource = gameObject.AddComponent<AudioSource>();
        saleSource.playOnAwake = false;
    }

    void HideOldChildren()
    {
        foreach (Transform child in sellPanel.transform)
            child.gameObject.SetActive(false);
    }

    // ── Trigger ────────────────────────────────────────────────────────────────
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        _suppressPromptUntilExit = false;
        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        InteractPromptUI.Clear(this);
        if (greetingActive) CancelGreeting();
        FishingdexManager.Instance?.CloseIfContextOpen();
        if (panelOpen) CloseSellPanel();
    }

    void Update()
    {
        // Live-refresh talk-prompt glyphs — only when no panel or greeting is active.
        // When the sell panel is open, OpenSellPanel already posted the close hint;
        // don't overwrite it with the talk prompt on the next frame.
        if (playerInRange && !panelOpen && !greetingActive && !_suppressPromptUntilExit)
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        if (playerInRange && !_suppressPromptUntilExit && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            if (!panelOpen && !greetingActive)
                greetingCoroutine = StartCoroutine(ShowGreetingThenOpen());
            else if (panelOpen)
                CloseSellPanel();
            return;
        }

        if (!greetingActive || panelOpen) return;
        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping)             _skipTyping      = true;
            else if (_waitingForClick) _waitingForClick = false;
        }
    }

    // ── Greeting ───────────────────────────────────────────────────────────────
    void CancelGreeting()
    {
        if (greetingCoroutine != null) { StopCoroutine(greetingCoroutine); greetingCoroutine = null; }
        greetingActive = _isTyping = _skipTyping = _waitingForClick = false;
        if (typewriterSource != null && typewriterSource.isPlaying) typewriterSource.Stop();
        PlayerController.isInDialogue = false;
        if (greetingText != null) greetingText.gameObject.SetActive(false);
    }

    IEnumerator ShowGreetingThenOpen()
    {
        greetingActive = true;
        InteractPromptUI.Clear(this);
        PlayerController.isInDialogue = true;
        NPCConversationTracker.NotifyStart(this);

        _sellDustOption = NPCSellDustOption.GetOrAdd(this);
        _sellDustOption.RollFresh();

        if (rodCtrl    == null) rodCtrl    = FindObjectOfType<FishingRodController>();
        if (guitarCtrl == null) guitarCtrl = FindObjectOfType<GuitarController>();
        if (bottleCtrl == null) bottleCtrl = FindObjectOfType<WaterBottleController>();
        rodCtrl?.ForceUnequipRod();
        guitarCtrl?.ForceUnequipGuitar();
        bottleCtrl?.ForceUnequipBottle();

        if (greetingText != null) greetingText.gameObject.SetActive(true);
        yield return StartCoroutine(TypewriterLine(greetingMessage, greetingText));

        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !playerInRange);

        if (greetingText != null) greetingText.gameObject.SetActive(false);
        greetingActive   = false;
        greetingCoroutine = null;

        if (playerInRange) ShowPostGreetingChoice();
        else PlayerController.isInDialogue = false;
    }

    IEnumerator TypewriterLine(string line, TextMeshProUGUI target)
    {
        if (target == null) yield break;
        _isTyping   = true;
        _skipTyping = false;

        if (typewriterLoopClip != null && typewriterSource != null)
        {
            typewriterSource.clip = typewriterLoopClip;
            typewriterSource.volume = typewriterVolume;
            typewriterSource.Play();
        }

        // RevealCharsTMP sets the full text up-front and animates
        // maxVisibleCharacters — TMP lays out the wrap once instead of
        // re-flowing on every char (the old `target.text += c` loop made
        // the layout shift mid-typewriter and could clip the line at the
        // word boundary that triggered the wrap).
        yield return DialogueTextStyling.RevealCharsTMP(target, line, charDelay, () => _skipTyping);

        if (typewriterSource != null && typewriterSource.isPlaying)
            typewriterSource.Stop();

        _isTyping   = _skipTyping = false;
    }

    // ── Post-greeting choice ───────────────────────────────────────────────────
    void ShowPostGreetingChoice()
    {
        bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row("Sell fish", true),
            new PostGreetingChoicePanel.Row(hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust),
            new PostGreetingChoicePanel.Row("Leave", true),
        };
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    void HandleChoice(int index)
    {
        switch (index)
        {
            case 0: OpenSellPanel(); break;
            case 1: OpenSellDust(); break;
            case 2: StopConversation(); break;
        }
    }

    void OpenSellDust()
    {
        if (_sellDustOption == null) { StopConversation(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: "Fish Vendor",
            acceptChance: _sellDustOption.AcceptChance,
            pricePerDust: _sellDustOption.PricePerDust,
            preferredMaxQty: _sellDustOption.PreferredMaxQty,
            onClose: ShowPostGreetingChoice
        );
    }

    void StopConversation()
    {
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();
        PlayerController.isInDialogue = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        // Suppress prompt until player physically walks out + back in. See
        // _suppressPromptUntilExit comment by the field declaration.
        _suppressPromptUntilExit = true;
        InteractPromptUI.Clear(this);
    }

    // ── Open / Close ───────────────────────────────────────────────────────────
    void OpenSellPanel()
    {
        // Return any previously staged fish and release their thumbnails
        foreach (var (f, rt, src) in stagedFish) { if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show(); ReleaseRT(rt); }
        stagedFish.Clear();

        panelOpen = true;
        InteractPromptUI.Clear(this);
        if (sellPanel      != null) sellPanel.SetActive(true);
        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to close");
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        RefreshUI();
    }

    static void ReleaseRT(RenderTexture rt) { if (rt != null) { rt.Release(); Destroy(rt); } }

    void CloseSellPanel()
    {
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();

        // Return any staged fish on close and release their thumbnails
        foreach (var (f, rt, src) in stagedFish) { if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show(); ReleaseRT(rt); }
        stagedFish.Clear();

        panelOpen = false;
        if (sellPanel != null) sellPanel.SetActive(false);
        InteractPromptUI.Clear(this);
        PlayerController.isInDialogue = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (playerInRange)
        { InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk"); }
    }

    // ── Callbacks ──────────────────────────────────────────────────────────────
    void OnAddFishClicked()
    {
        if (FishStagingUI.Instance == null) return;
        FishStagingUI.Instance.Open("SELL FISH", entries =>
        {
            foreach (var (fish, source) in entries)
                stagedFish.Add((fish, null, source));
            RefreshUI();
        });
    }

    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt, src) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        // Phase 4: return to exact original source via shared chain.
        if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show();
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }

    void OnConfirmSale()
    {
        if (stagedFish.Count == 0) return;

        int total = 0;
        foreach (var (f, rt, _) in stagedFish) { total += f.GetValue(); ReleaseRT(rt); }

        // Fish already removed from inventory when staged
        stagedFish.Clear();

        if (PlayerWallet.Instance != null)
            PlayerWallet.Instance.AddMoney(total);

        if (saleClip != null && saleSource != null) saleSource.PlayOneShot(saleClip, saleVolume);

        CloseSellPanel();
        ShowEarningsMessage($"Sale complete!  You earned  ${total}!");
    }

    // ── UI Refresh ─────────────────────────────────────────────────────────────
    void RefreshUI()
    {
        if (uiListHeader != null)
            uiListHeader.text = $"Added to Sale  ({stagedFish.Count})";

        RebuildFishCards();
        if (uiListContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(uiListContent.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();

        int totalVal = 0;
        foreach (var (f, _, _) in stagedFish) totalVal += f.GetValue();

        if (uiTotalText != null)
            uiTotalText.text = stagedFish.Count > 0 ? $"Total Value:  ${totalVal}" : "";

        if (uiSellBtn != null)
        {
            uiSellBtn.interactable = stagedFish.Count > 0;
            var col = uiSellBtn.colors;
            col.normalColor = stagedFish.Count > 0 ? (Color)C_BtnSell : new Color32(30, 80, 40, 255);
            uiSellBtn.colors = col;
            var lbl = uiSellBtn.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (lbl != null)
                lbl.text = stagedFish.Count > 0 ? $"Confirm Sale  ({stagedFish.Count})" : "Confirm Sale";
        }
    }

    void RebuildFishCards()
    {
        if (uiListContent == null) return;

        // Destroy() (deferred to end-of-frame) instead of DestroyImmediate() to
        // avoid the synchronous editor stall. SetActive(false) first so the
        // soon-to-be-destroyed cards aren't rendered alongside the new ones in
        // the same frame.
        for (int i = uiListContent.childCount - 1; i >= 0; i--)
        {
            var child = uiListContent.GetChild(i);
            if (child.name == "EmptyHint") continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        var emptyHint = uiListContent.Find("EmptyHint");
        if (emptyHint != null) emptyHint.gameObject.SetActive(stagedFish.Count == 0);

        foreach (var (f, rt, _) in stagedFish)
        {
            var captured = f;
            Color32 dot  = f.fishType == "Rare"     ? C_Rare
                         : f.fishType == "Uncommon" ? C_Uncommon
                         : C_Common;
            string detail = $"{f.weightLbs} lbs  |  ${f.GetValue()} value";

            MkFishCard(uiListContent, f.fishType, detail, dot, rt, () => OnRemoveFish(captured));
        }
    }

    // ── Earnings message ───────────────────────────────────────────────────────
    void ShowEarningsMessage(string message)
    {
        if (uiEarningsMsg == null) return;
        if (earningsCoroutine != null) StopCoroutine(earningsCoroutine);
        earningsCoroutine = StartCoroutine(DisplayEarnings(message));
    }

    IEnumerator DisplayEarnings(string message)
    {
        uiEarningsMsg.text = message;
        uiEarningsCG.alpha = 1f;
        uiEarningsMsg.gameObject.SetActive(true);
        yield return new WaitForSeconds(earningsDisplayDuration);
        float elapsed = 0f;
        while (elapsed < earningsFadeDuration)
        {
            uiEarningsCG.alpha = Mathf.Lerp(1f, 0f, elapsed / earningsFadeDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        uiEarningsCG.alpha = 0f;
        uiEarningsMsg.gameObject.SetActive(false);
        earningsCoroutine = null;
    }

    // ── UI Build ───────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var panelImg = sellPanel.GetComponent<Image>() ?? sellPanel.AddComponent<Image>();
        panelImg.color = C_PanelBg;
        panelImg.type  = Image.Type.Simple;

        var panelRT = sellPanel.GetComponent<RectTransform>();
        panelRT.sizeDelta = new Vector2(460, 580);
        var panelLE = sellPanel.GetComponent<LayoutElement>() ?? sellPanel.AddComponent<LayoutElement>();
        panelLE.ignoreLayout = true;

        var existingVLG = sellPanel.GetComponent<VerticalLayoutGroup>();
        if (existingVLG != null) DestroyImmediate(existingVLG);
        var vlg = sellPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(18, 18, 18, 18);
        vlg.spacing              = 9;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // Title
        MkText(sellPanel.transform, "FISH MARKET", 21, C_Title, 42, FontStyles.Bold, TextAlignmentOptions.Center);
        MkDivider(sellPanel.transform);

        // List header
        uiListHeader = MkText(sellPanel.transform, "Added to Sale  (0)", 13, C_Sub, 22);

        // Scroll list
        uiListContent = MkScrollArea(sellPanel.transform, 260);

        // Total value
        uiTotalText = MkText(sellPanel.transform, "", 15, C_Gold, 26, FontStyles.Bold, TextAlignmentOptions.Right);

        MkDivider(sellPanel.transform);

        // Button row
        var row = MkHRow(sellPanel.transform, 48);
        uiAddBtn  = MkButton(row, "Add Fish",     C_BtnAdd);
        uiSellBtn = MkButton(row, "Confirm Sale", C_BtnSell);
        uiAddBtn.onClick.AddListener(OnAddFishClicked);
        uiSellBtn.onClick.AddListener(OnConfirmSale);

        // Earnings message lives on the canvas so it persists after the sell panel closes
        var canvas    = sellPanel.GetComponentInParent<Canvas>();
        var earnParent = canvas != null ? canvas.transform : sellPanel.transform.parent;
        var earnGO    = new GameObject("MarketEarningsMsg", typeof(RectTransform));
        earnGO.transform.SetParent(earnParent, false);
        var earnRT = earnGO.GetComponent<RectTransform>();
        earnRT.anchorMin        = new Vector2(0.5f, 0.5f);
        earnRT.anchorMax        = new Vector2(0.5f, 0.5f);
        earnRT.pivot            = new Vector2(0.5f, 0.5f);
        earnRT.sizeDelta        = new Vector2(540, 60);
        earnRT.anchoredPosition = new Vector2(0f, 120f);
        uiEarningsMsg           = earnGO.AddComponent<TextMeshProUGUI>();
        uiEarningsMsg.text      = "";
        uiEarningsMsg.fontSize  = 18;
        uiEarningsMsg.fontStyle = FontStyles.Bold;
        uiEarningsMsg.alignment = TextAlignmentOptions.Center;
        uiEarningsMsg.color     = C_Gold;
        uiEarningsCG            = earnGO.AddComponent<CanvasGroup>();
        uiEarningsCG.alpha      = 0f;
        earnGO.SetActive(false);

        RefreshUI();
    }

    // ── UI Helpers ─────────────────────────────────────────────────────────────
    static TextMeshProUGUI MkText(Transform parent, string text, int size, Color32 color, float height,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go  = new GameObject("Txt", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color     = color;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    static void MkDivider(Transform parent)
    {
        var go  = new GameObject("Divider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = 1;
        go.AddComponent<Image>().color = C_Divider;
    }

    static Transform MkScrollArea(Transform parent, float height)
    {
        var scrollGO  = new GameObject("ScrollArea", typeof(RectTransform));
        scrollGO.transform.SetParent(parent, false);
        scrollGO.AddComponent<LayoutElement>().preferredHeight = height;
        scrollGO.AddComponent<Image>().color = C_ScrollBg;
        var sr = scrollGO.AddComponent<ScrollRect>();

        var vpGO = new GameObject("Viewport", typeof(RectTransform));
        vpGO.transform.SetParent(scrollGO.transform, false);
        var vpRT = vpGO.GetComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.sizeDelta = Vector2.zero; vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();

        var cGO = new GameObject("Content", typeof(RectTransform));
        cGO.transform.SetParent(vpGO.transform, false);
        var cRT = cGO.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0, 1); cRT.anchorMax = new Vector2(1, 1);
        cRT.pivot     = new Vector2(0.5f, 1);
        cRT.offsetMin = Vector2.zero;      cRT.offsetMax = Vector2.zero;
        var cVLG = cGO.AddComponent<VerticalLayoutGroup>();
        cVLG.padding = new RectOffset(7, 7, 7, 7);
        cVLG.spacing = 5;
        cVLG.childControlWidth = true; cVLG.childControlHeight = true;
        cVLG.childForceExpandWidth = true; cVLG.childForceExpandHeight = false;
        var csf = cGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport          = vpRT;
        sr.content           = cRT;
        sr.horizontal        = false;
        sr.vertical          = true;
        sr.scrollSensitivity = 30;
        sr.movementType      = ScrollRect.MovementType.Clamped;

        var hint = new GameObject("EmptyHint", typeof(RectTransform));
        hint.transform.SetParent(cGO.transform, false);
        hint.AddComponent<LayoutElement>().preferredHeight = 80;
        var hTMP = hint.AddComponent<TextMeshProUGUI>();
        hTMP.text      = "No fish added yet.\nClick  Add Fish  below.";
        hTMP.fontSize  = 13;
        hTMP.alignment = TextAlignmentOptions.Center;
        hTMP.color     = C_Hint;

        return cGO.transform;
    }

    static Transform MkHRow(Transform parent, float height)
    {
        var go  = new GameObject("HRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
        return go.transform;
    }

    static Button MkButton(Transform parent, string label, Color32 bg, int fontSize = 15)
    {
        var go  = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        var col = btn.colors;
        col.normalColor      = bg;
        col.highlightedColor = new Color32(
            (byte)Mathf.Clamp(bg.r + 40, 0, 255),
            (byte)Mathf.Clamp(bg.g + 40, 0, 255),
            (byte)Mathf.Clamp(bg.b + 40, 0, 255), 255);
        col.pressedColor = new Color32(
            (byte)Mathf.Clamp(bg.r - 40, 0, 255),
            (byte)Mathf.Clamp(bg.g - 40, 0, 255),
            (byte)Mathf.Clamp(bg.b - 40, 0, 255), 255);
        btn.colors = col;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
        lblRT.sizeDelta = Vector2.zero; lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    static void MkFishCard(Transform parent, string fishType, string detail, Color32 dotColor, RenderTexture preview, System.Action onRemove)
    {
        var card = new GameObject("FishCard", typeof(RectTransform));
        card.transform.SetParent(parent, false);
        card.AddComponent<LayoutElement>().preferredHeight = 70;
        card.AddComponent<Image>().color = C_CardBg;
        var hlg = card.AddComponent<HorizontalLayoutGroup>();
        hlg.padding  = new RectOffset(8, 8, 6, 6);
        hlg.spacing  = 8;
        hlg.childControlHeight = true; hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;

        if (preview != null)
        {
            var imgGO = new GameObject("Preview", typeof(RectTransform));
            imgGO.transform.SetParent(card.transform, false);
            var imgLE = imgGO.AddComponent<LayoutElement>();
            imgLE.preferredWidth = 58; imgLE.flexibleHeight = 1;
            var raw = imgGO.AddComponent<RawImage>();
            raw.texture = preview;
        }
        else
        {
            var dot = new GameObject("Dot", typeof(RectTransform));
            dot.transform.SetParent(card.transform, false);
            var dotLE = dot.AddComponent<LayoutElement>();
            dotLE.preferredWidth = 5;
            dot.AddComponent<Image>().color = dotColor;
        }

        var info = new GameObject("Info", typeof(RectTransform));
        info.transform.SetParent(card.transform, false);
        info.AddComponent<LayoutElement>().flexibleWidth = 1;
        var infoVLG = info.AddComponent<VerticalLayoutGroup>();
        infoVLG.childControlWidth = true; infoVLG.childControlHeight = true;
        infoVLG.childForceExpandWidth = true; infoVLG.spacing = 2;

        var nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(info.transform, false);
        nameGO.AddComponent<LayoutElement>().preferredHeight = 20;
        var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.text = fishType; nameTMP.fontSize = 14;
        nameTMP.fontStyle = FontStyles.Bold; nameTMP.color = C_Label;

        var detGO = new GameObject("Detail", typeof(RectTransform));
        detGO.transform.SetParent(info.transform, false);
        detGO.AddComponent<LayoutElement>().preferredHeight = 17;
        var detTMP = detGO.AddComponent<TextMeshProUGUI>();
        detTMP.text = detail; detTMP.fontSize = 11; detTMP.color = C_Sub;

        var remGO = new GameObject("RemoveBtn", typeof(RectTransform));
        remGO.transform.SetParent(card.transform, false);
        var remLE = remGO.AddComponent<LayoutElement>();
        remLE.preferredWidth = 34; remLE.preferredHeight = 34;
        remGO.AddComponent<Image>().color = C_BtnRem;
        var remBtn = remGO.AddComponent<Button>();
        var rCol = remBtn.colors;
        rCol.normalColor = C_BtnRem;
        rCol.highlightedColor = new Color32(220, 60, 60, 255);
        rCol.pressedColor     = new Color32(110, 18, 18, 255);
        remBtn.colors = rCol;
        remBtn.onClick.AddListener(() => onRemove());

        var rLbl = new GameObject("X", typeof(RectTransform));
        rLbl.transform.SetParent(remGO.transform, false);
        var rRT = rLbl.GetComponent<RectTransform>();
        rRT.anchorMin = Vector2.zero; rRT.anchorMax = Vector2.one;
        rRT.sizeDelta = Vector2.zero; rRT.offsetMin = Vector2.zero; rRT.offsetMax = Vector2.zero;
        var rTMP = rLbl.AddComponent<TextMeshProUGUI>();
        rTMP.text = "X"; rTMP.fontSize = 14; rTMP.fontStyle = FontStyles.Bold;
        rTMP.color = Color.white; rTMP.alignment = TextAlignmentOptions.Center;
    }
}
