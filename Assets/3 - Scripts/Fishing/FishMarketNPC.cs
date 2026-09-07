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
    // The sell panel is ONE shared HUD_Canvas panel; its widgets live in the
    // static Ui block below and every market adopts them on open. Only the
    // earnings toast is kept per-instance because the coroutine drives it.
    TextMeshProUGUI uiEarningsMsg;
    CanvasGroup     uiEarningsCG;
    bool            _indexOpen;      // FISH PRICE INDEX page showing instead of the shelves

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
    static readonly Color32 C_PanelBtnPrices = new Color32(60, 70, 110, 255);
    static readonly Color32 C_BtnIndex   = new Color32(31,  59,  110, 255);
    static readonly Color32 C_CounterBg  = new Color32(11,  22,  38,  255);
    static readonly Color32 C_CounterCard= new Color32(15,  36,  48,  255);

    // ── Unity lifecycle ────────────────────────────────────────────────────────
    void Start()
    {
        rodCtrl    = FindObjectOfType<FishingRodController>();
        guitarCtrl = FindObjectOfType<GuitarController>();
        bottleCtrl = FindObjectOfType<WaterBottleController>();

        ResolveSharedHUD();
        KeepVendorVisible();

        // The sell panel is ONE shared HUD_Canvas panel used by every fish
        // market in the system, so it is built lazily by whoever opens it
        // first (see EnsureBuiltUI) rather than by all of them in Start.
        // Building here would stack one set of widgets per vendor in the scene.
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

    // ── Planet-economy pricing (2026-09-07) ───────────────────────────────────
    //
    // ONE function decides what a fish is worth at THIS market, and it is used
    // for the card the player reads, the running total, and the money actually
    // paid — so displayed always equals paid (the promise/grade trap class).
    // The bucket and appetite live in PlanetEconomy / FishAppetite; this only
    // asks "what does my planet pay for this species right now".

    /// <summary>What this market pays for the fish right now: base value ×
    /// bucket rate × this vendor's current appetite.</summary>
    int PriceAt(FishEntry f)
    {
        int baseValue = f.GetValue();
        string body = BodyName;
        if (string.IsNullOrEmpty(body) || !PlanetEconomy.HasTable(body)) return baseValue;
        float mult = PlanetEconomy.MultiplierNow(body, SpeciesIdOf(f));
        int v = Mathf.RoundToInt(baseValue * mult);
        return v < 1 ? 1 : v;
    }

    /// <summary>The word on the card: Local / Imported / Delicacy / Unlisted.</summary>
    string BucketWordFor(FishEntry f)
    {
        string body = BodyName;
        if (string.IsNullOrEmpty(body) || !PlanetEconomy.HasTable(body)) return "";
        return PlanetEconomy.Word(PlanetEconomy.BucketFor(body, SpeciesIdOf(f)));
    }

    static string SpeciesIdOf(FishEntry f)
    {
        int i = f.ResolveSpecies();
        return i >= 0 && i < FishingRules.Species.Length ? FishingRules.Species[i].id : "";
    }

    // ── Vendor stays visible on every planet (2026-09-07 playtest) ─────────────
    //
    // Sam's report: fly to any of the new markets and the STAND is there but the
    // alien behind it is not. In the Editor all ten are present, active, renderers
    // enabled, identical to the Humble Abode one that works — so nothing is being
    // destroyed or disabled. They are being CULLED.
    //
    // The alien is the only SkinnedMeshRenderer in the stand, and it ships with
    // updateWhenOffscreen = false. That means Unity culls it using bounds cached
    // from the root bone rather than measuring the live pose. NPCWaveAnimation
    // moves those bones every LateUpdate, and the vendor spends the whole session
    // off-screen on a planet nobody has visited — so the cached bounds are never
    // refreshed against reality, the renderer stays classified as off-screen, and
    // it can be invisible even once you are standing in front of it. Humble
    // Abode's vendor escapes it purely because you spawn looking at it, so its
    // bounds are correct from the first frame.
    //
    // updateWhenOffscreen recomputes bounds from the actual bones each frame.
    // That is a per-frame cost for ONE ~70-bone character per planet, which is
    // nothing next to being unable to sell your fish. cullingMode is pinned too so
    // an Animator can never stop updating the bones the bounds are measured from.
    void KeepVendorVisible()
    {
        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && !smr.updateWhenOffscreen) smr.updateWhenOffscreen = true;

        var anim = GetComponent<Animator>();
        if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    // ── Shared HUD wiring (planet economies, 2026-09-07) ───────────────────────
    // Before per-planet fish markets there was exactly ONE vendor, so its three
    // HUD references were dragged in by hand in the scene. A prefab cannot hold
    // a reference to a scene object, so a vendor that is dropped onto a planet
    // finds the shared canvas itself — the same auto-find contract Alien7Vendor
    // already uses. Hand-wired scene instances keep whatever they were given.

    /// <summary>Planet this market trades on, via the VendorSite on its stand.
    /// Empty until the hierarchy resolves. Phase 4 keys the buy list off this.</summary>
    public string BodyName => VendorSite.BodyNameFor(this);

    void ResolveSharedHUD()
    {
        if (talkPromptText != null && greetingText != null && sellPanel != null) return;

        // Borrow from any vendor that IS wired up — cheapest and keeps every
        // market pointing at the identical panel.
        var wired = FindObjectsOfType<FishMarketNPC>(true);
        for (int i = 0; i < wired.Length; i++)
        {
            var o = wired[i];
            if (o == this) continue;
            if (talkPromptText == null) talkPromptText = o.talkPromptText;
            if (greetingText   == null) greetingText   = o.greetingText;
            if (sellPanel      == null) sellPanel      = o.sellPanel;
        }
        if (talkPromptText != null && greetingText != null && sellPanel != null) return;

        // Otherwise find the canvas by name. Inactive objects are included:
        // the sell panel spends nearly all of its life switched off.
        var canvas = GameObject.Find("HUD_Canvas");
        if (canvas == null)
        {
            var anyCanvas = FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < anyCanvas.Length; i++)
                if (anyCanvas[i] != null && anyCanvas[i].name == "HUD_Canvas")
                { canvas = anyCanvas[i].gameObject; break; }
        }
        if (canvas == null)
        {
            Debug.LogWarning("[FishMarketNPC] No HUD_Canvas found — this market " +
                             "cannot show its prompt or sell panel.", this);
            return;
        }

        if (sellPanel == null)
        {
            var t = FindDeep(canvas.transform, "SellPanel");
            if (t != null) sellPanel = t.gameObject;
        }
        if (talkPromptText == null)
        {
            var t = FindDeep(canvas.transform, "TalkPrompt");
            if (t != null) talkPromptText = t.GetComponent<TextMeshProUGUI>();
        }
        if (greetingText == null)
        {
            var t = FindDeep(canvas.transform, "DialogueText");
            if (t != null) greetingText = t.GetComponent<TextMeshProUGUI>();
        }
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    // ── One shared sell panel, many markets ────────────────────────────────────
    // BuildUI writes procedural widgets into the shared panel and binds its
    // buttons to the instance that built them. With a market on every planet
    // that has to happen exactly once, with the buttons re-pointed at whichever
    // vendor the player is standing in front of.
    //
    // Layout (Sam's pick, 2026-09-07, mockup "Two Shelves + page-swap index"):
    //   ┌ FISH MARKET · planet ───────────────────── wallet ┐
    //   │ YOUR BAG            ⇄   ON THE COUNTER            │
    //   │ [fish card]             [fish card]               │  click a fish to move it across
    //   │ ...                     ...                        │
    //   ├ COUNTER $total        [FISH PRICE INDEX] [Sell]  ─┤
    //   └───────────────────────────────────────────────────┘
    //   FISH PRICE INDEX swaps the whole panel for a page listing everything
    //   this market pays for, per lb, right now, with a BACK button.
    static class Ui
    {
        public static bool built => bagContent != null;   // Unity-null on scene reload
        public static FishMarketNPC owner;
        public static GameObject shelvesPage, indexPage;
        public static TextMeshProUGUI planetText, walletText, bagHeader, counterHeader, totalText, indexSub;
        public static Transform bagContent, counterContent, indexBody;
        public static Button btnIndex, btnBack, btnSell;
        public static TextMeshProUGUI earningsMsg;
        public static CanvasGroup earningsCG;
    }

    void EnsureBuiltUI()
    {
        if (sellPanel == null) return;
        if (!Ui.built) { HideOldChildren(); BuildUI(); }

        uiEarningsMsg = Ui.earningsMsg;
        uiEarningsCG  = Ui.earningsCG;

        if (Ui.owner == this) return;
        Ui.owner = this;
        // Re-point the buttons at this market. Only fires when the player walks
        // up to a different planet's vendor, so the alloc is a non-issue.
        Ui.btnIndex.onClick.RemoveAllListeners(); Ui.btnIndex.onClick.AddListener(OpenIndex);
        Ui.btnBack.onClick.RemoveAllListeners();  Ui.btnBack.onClick.AddListener(CloseIndex);
        Ui.btnSell.onClick.RemoveAllListeners();  Ui.btnSell.onClick.AddListener(OnConfirmSale);
    }

    // ── Trigger ────────────────────────────────────────────────────────────────
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        // Planet economy: reaching the counter is what puts this market on the
        // phone's MARKETS page. (The floating sign used to do this; Sam cut the
        // signs on 2026-09-07 — the PRICES tab in this panel is the price list.)
        MarketKnowledge.NoteSeen(BodyName);
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
        // Pad B backs out of the sell panel (gated on the staging picker,
        // which stacks on top and consumes B for its own cancel).
        bool stagingOpen = FishStagingUI.Instance != null && FishStagingUI.Instance.IsOpen;
        // From the FISH PRICE INDEX page, B / Escape goes BACK to the shelves
        // rather than closing the whole panel.
        if (panelOpen && _indexOpen
            && (TutorialGate.PadPressed(TutorialGate.PadButton.B) || Input.GetKeyDown(KeyCode.Escape)))
        {
            CloseIndex();
            return;
        }
        if (panelOpen && !stagingOpen
            && TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            CloseSellPanel();
            return;
        }
        if (playerInRange && !_suppressPromptUntilExit && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            // Opening requires looking at the NPC; closing (below) works anywhere.
            if (!panelOpen && !greetingActive && InteractGaze.IsLookingAt(this))
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

        // Dialogue Studio graph first (npc_fishmarket.json, node "start") —
        // the spoken greeting only; the sell menu after it is unchanged.
        var graph = StoryContent.GetNpcGraph("npc_fishmarket");
        if (graph != null)
        {
            yield return GraphWalker(graph).Run(graph);
        }
        else
        {
            yield return StartCoroutine(TypewriterLine(greetingMessage, greetingText));
            _waitingForClick = true;
            yield return new WaitUntil(() => !_waitingForClick || !playerInRange);
        }

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
    // Sell rows are build-configurable (mushrooms live, space dust vaulted), so
    // the menu is assembled rather than hard-indexed — see NPCSellRows.
    readonly System.Collections.Generic.List<NPCSellRows.SellAction> _sellActions =
        new System.Collections.Generic.List<NPCSellRows.SellAction>();
    const int SellHeadRows = 1;   // "Sell fish" sits above the sell block

    // Where the bait rows sit in the assembled menu. Recomputed every time the
    // menu is built rather than hard-indexed, because NPCSellRows.Append adds a
    // build-configurable number of rows above them (mushrooms live, space dust
    // vaulted) -- a hard index here would silently sell the wrong thing the next
    // time a sell row is vaulted.
    int _baitRowStart = -1;

    void ShowPostGreetingChoice()
    {
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row("Sell fish", true),
        };
        NPCSellRows.Append(rows, _sellActions, this);

        // [BUILD] 3: bait, in the vendor's existing row style. No new screen.
        // Rows are clamped on affordability the way Tev's shop clamps -- an
        // unaffordable row is visible and disabled, never hidden, so the player
        // can see what they are saving toward.
        _baitRowStart = rows.Count;
        int money = PlayerWallet.Instance != null ? PlayerWallet.Instance.Money : 0;
        for (int i = 0; i < FishingBait.All.Length; i++)
        {
            var def = FishingBait.All[i];
            bool afford = money >= def.price;
            rows.Add(new PostGreetingChoicePanel.Row($"Buy {def.displayName}  (${def.price})", afford));
        }

        // The bounty (docs/Handoff_BountyQuest_Grulabu_v1.md, Phase C). The story
        // is always on offer -- it is the WHY and the reward; Floorbin gives the
        // WHERE, and neither gates the other. The turn-in row appears only while
        // the bounty fish is actually on the player.
        _bountyRowStart = rows.Count;
        bool turnedIn = BountyFlag(bountyTurnedInFlag);
        rows.Add(new PostGreetingChoicePanel.Row(turnedIn ? "About that fish..." : "Ask about the bounty fish", true));
        _bountyTurnInRow = -1;
        if (!turnedIn && FindBountyFishOnPlayer() != null)
        {
            _bountyTurnInRow = rows.Count;
            rows.Add(new PostGreetingChoicePanel.Row($"Turn in GRULABU  (${bountyReward})", true));
        }

        rows.Add(new PostGreetingChoicePanel.Row("Leave", true));
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    /// <summary>
    /// Buy one unit of bait. Deliberately one at a time through the existing row
    /// list -- the handoff's "no new screen" -- and the menu reopens so the
    /// player can buy again and watch the affordability clamp bite.
    /// </summary>
    void BuyBait(int baitIndex)
    {
        if (baitIndex < 0 || baitIndex >= FishingBait.All.Length) { ShowPostGreetingChoice(); return; }
        var def = FishingBait.All[baitIndex];

        if (PlayerWallet.Instance == null || PlayerWallet.Instance.Money < def.price)
        {
            ShowEarningsMessage("Not enough money.");
            ShowPostGreetingChoice();
            return;
        }
        if (Hotbar.Instance == null) { ShowPostGreetingChoice(); return; }

        // Take the money only if the bait actually fits, so a full hotbar can
        // never eat a payment.
        int leftover = Hotbar.Instance.AddResource(def.item, 1);
        if (leftover > 0)
        {
            InventoryFullPopup.Show();
            ShowPostGreetingChoice();
            return;
        }
        PlayerWallet.Instance.SpendMoney(def.price);
        ShowEarningsMessage($"Bought 1 {def.displayName}.");
        ShowPostGreetingChoice();
    }

    void HandleChoice(int index)
    {
        if (index < 0) { StopConversation(); return; }   // pad B on the option list = Leave
        if (index == 0) { OpenSellPanel(); return; }
        if (NPCSellRows.ActionAt(_sellActions, SellHeadRows, index, out var action))
        {
            NPCSellRows.Open(action, this, "Fish Vendor", _sellDustOption, ShowPostGreetingChoice);
            return;
        }
        if (_baitRowStart >= 0 && index >= _baitRowStart
                               && index < _baitRowStart + FishingBait.All.Length)
        {
            BuyBait(index - _baitRowStart);
            return;
        }
        if (_bountyRowStart >= 0 && index == _bountyRowStart)
        {
            greetingCoroutine = StartCoroutine(TellBountyStory());
            return;
        }
        if (_bountyTurnInRow >= 0 && index == _bountyTurnInRow)
        {
            TurnInBounty();
            return;
        }
        StopConversation();
    }

    void StopConversation()
    {
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        NPCSellRows.CloseAny();
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
        _indexOpen = false;
        InteractPromptUI.Clear(this);
        EnsureBuiltUI();
        if (sellPanel != null)
        {
            sellPanel.SetActive(true);
            // Shared HUD_Canvas host — promote so pad focus reaches the
            // panel's buttons (see ControllerUINavigator.PromoteToModal).
            ControllerUINavigator.PromoteToModal(sellPanel);
        }
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
        NPCSellRows.CloseAny();

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
    // ── Bag ⇄ counter ──────────────────────────────────────────────────────────
    // "Your bag" is every fish the player is carrying: fish sitting in hotbar
    // slots, and fish inside any fish bag in a hotbar slot. Clicking one lifts
    // it straight out of its slot onto the counter (the same FishSource the old
    // picker used, so a cancelled sale puts it back exactly where it was);
    // clicking a counter card returns it. The picker is no longer part of the
    // sell flow.

    static readonly List<(FishEntry fish, FishSource src)> _bagScratch = new List<(FishEntry, FishSource)>(16);

    static List<(FishEntry fish, FishSource src)> CarriedFish()
    {
        _bagScratch.Clear();
        var hb = Hotbar.Instance;
        if (hb == null) return _bagScratch;
        var slots = hb.RawSlotsRef();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].id == Hotbar.ItemId.Fish && slots[i].fishData != null)
                _bagScratch.Add((slots[i].fishData, new FishSource { container = slots, index = i }));
            else if (slots[i].id == Hotbar.ItemId.FishBag && slots[i].bagContents != null)
            {
                var bag = slots[i].bagContents;
                for (int j = 0; j < bag.Length; j++)
                    if (bag[j].id == Hotbar.ItemId.Fish && bag[j].fishData != null)
                        _bagScratch.Add((bag[j].fishData, new FishSource { container = bag, index = j }));
            }
        }
        return _bagScratch;
    }

    void StageFromBag(FishEntry fish, FishSource src)
    {
        if (fish == null || !src.IsValid) return;
        // [OPEN-3] the bounty fish is not sold by the pound.
        if (FishingRules.IsBounty(fish.ResolveSpecies())) { ShowEarningsMessage(bountyRefuseSaleLine); return; }
        if (src.container[src.index].fishData != fish) return;   // slot changed under us
        src.container[src.index] = default;
        stagedFish.Add((fish, null, src));
        RefreshUI();
    }

    void ReturnToBag(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt, src) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show();
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }

    void OpenIndex()  { _indexOpen = true;  RefreshUI(); }
    void CloseIndex() { _indexOpen = false; RefreshUI(); }

    void OnConfirmSale()
    {
        if (stagedFish.Count == 0) return;

        int total = 0;
        string sellBody = BodyName;
        foreach (var (f, rt, _) in stagedFish)
        {
            // Price FIRST, then note the sale: the fish you are selling is paid
            // at the appetite that existed before it, and cools the market for
            // the next one.
            total += PriceAt(f);
            if (!string.IsNullOrEmpty(sellBody) && PlanetEconomy.HasTable(sellBody))
            {
                var bucket = PlanetEconomy.BucketFor(sellBody, SpeciesIdOf(f));
                if (bucket == PlanetEconomy.Bucket.Imported || bucket == PlanetEconomy.Bucket.Delicacy)
                    FishAppetite.NoteSale(sellBody, SpeciesIdOf(f));
            }
            ReleaseRT(rt);
        }

        // Fish already removed from inventory when staged
        stagedFish.Clear();

        if (PlayerWallet.Instance != null)
            PlayerWallet.Instance.AddMoney(total);

        // Cold Company (Main Mission 1): selling Tev's gift catch funds the first ship.
        ColdCompany.NotifyFishSold();

        if (saleClip != null && saleSource != null) saleSource.PlayOneShot(saleClip, saleVolume);

        CloseSellPanel();
        ShowEarningsMessage($"Sale complete!  You earned  ${total}!");
    }

    // ── UI Refresh ─────────────────────────────────────────────────────────────
    void RefreshUI()
    {
        if (!Ui.built) return;

        Ui.shelvesPage.SetActive(!_indexOpen);
        Ui.indexPage.SetActive(_indexOpen);

        string body = BodyName;
        Ui.planetText.text = string.IsNullOrEmpty(body) ? "" : body;
        Ui.walletText.text = "Wallet  $" + (PlayerWallet.Instance != null ? PlayerWallet.Instance.Money : 0);

        if (_indexOpen) { RebuildIndex(body); return; }

        // Left shelf: whatever the player is carrying.
        var carried = CarriedFish();
        ClearCards(Ui.bagContent, carried.Count == 0);
        foreach (var (fish, src) in carried)
        {
            var f = fish; var sc = src;
            MkShelfCard(Ui.bagContent, f, false, () => StageFromBag(f, sc));
        }
        Ui.bagHeader.text = $"YOUR BAG   {carried.Count} fish";

        // Right shelf: the counter.
        ClearCards(Ui.counterContent, stagedFish.Count == 0);
        foreach (var (f, _, _) in stagedFish)
        {
            var captured = f;
            MkShelfCard(Ui.counterContent, f, true, () => ReturnToBag(captured));
        }
        Ui.counterHeader.text = $"ON THE COUNTER   {stagedFish.Count} fish";

        int totalVal = 0;
        foreach (var (f, _, _) in stagedFish) totalVal += PriceAt(f);
        Ui.totalText.text = $"COUNTER  <color=#FFD732>${totalVal}</color>";

        Ui.btnSell.interactable = stagedFish.Count > 0;
        var col = Ui.btnSell.colors;
        col.normalColor = stagedFish.Count > 0 ? (Color)C_BtnSell : new Color32(30, 80, 40, 255);
        Ui.btnSell.colors = col;
        var lbl = Ui.btnSell.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (lbl != null) lbl.text = stagedFish.Count > 0 ? $"Sell the counter  ({stagedFish.Count})" : "Sell the counter";

        LayoutRebuilder.ForceRebuildLayoutImmediate(Ui.bagContent.GetComponent<RectTransform>());
        LayoutRebuilder.ForceRebuildLayoutImmediate(Ui.counterContent.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>The FISH PRICE INDEX page: everything this market pays for, per lb,
    /// right now (appetite included), grouped Delicacy → Imported → Local, with
    /// how many of each the player is carrying. Same PricePerLbNow as the sale.</summary>
    void RebuildIndex(string body)
    {
        ClearCards(Ui.indexBody, false);
        var entry = PlanetEconomy.EntryFor(body);
        Ui.indexSub.text = string.IsNullOrEmpty(body) ? "" : $"{body}  ·  what this vendor pays, per lb, right now";
        if (entry == null)
        {
            MkText(Ui.indexBody, "This market buys every fish at its base price.", 13, C_Sub, 26);
            return;
        }

        var carried = CarriedFish();
        IndexGroup(body, "DELICACY", "pays top  ·  ×3",     C_Rare,     entry.delicacies, carried, true);
        IndexGroup(body, "IMPORTED", "pays well  ·  ×1.75", C_Uncommon, entry.imports,    carried, true);
        IndexGroup(body, "LOCAL",    "home catch  ·  ×1",   C_Common,   entry.catchable,  carried, false);
        MkText(Ui.indexBody, $"Anything not listed: {PlanetEconomy.BaseMultiplier(PlanetEconomy.Bucket.Unlisted):0.00}× its base price.   " +
                             "Delicacy & Imported prices drop 15% each sale here and recover a step per day.", 11, C_Hint, 30);
        LayoutRebuilder.ForceRebuildLayoutImmediate(Ui.indexBody.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
    }

    void IndexGroup(string body, string title, string descriptor, Color32 color, List<string> ids,
                    List<(FishEntry fish, FishSource src)> carried, bool showAppetite)
    {
        if (ids == null || ids.Count == 0) return;
        var head = MkHRow(Ui.indexBody, 24);
        var t = MkText(head, title, 12, color, 24, FontStyles.Bold);
        t.GetComponent<LayoutElement>().preferredWidth = 110;
        MkText(head, descriptor, 11, C_Sub, 24);

        foreach (var id in ids)
        {
            int inBag = 0;
            foreach (var (fish, _) in carried) if (SpeciesIdOf(fish) == id) inBag++;
            int fullness = showAppetite ? FishAppetite.Fullness(body, id) : 0;
            MkIndexRow(Ui.indexBody, id, inBag, showAppetite, fullness,
                       PlanetEconomy.PricePerLbNow(body, id));
        }
        MkText(Ui.indexBody, "", 8, C_Sub, 6);
    }

    static void ClearCards(Transform content, bool showEmptyHint)
    {
        if (content == null) return;
        // Destroy() (deferred) rather than DestroyImmediate() to avoid the
        // synchronous editor stall; deactivate first so the doomed cards are not
        // drawn alongside the new ones this frame.
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            if (child.name == "EmptyHint") { child.gameObject.SetActive(showEmptyHint); continue; }
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
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
        panelRT.sizeDelta = new Vector2(780, 580);
        var panelLE = sellPanel.GetComponent<LayoutElement>() ?? sellPanel.AddComponent<LayoutElement>();
        panelLE.ignoreLayout = true;

        // The panel itself just stacks the two pages; each page is a VLG.
        var existingVLG = sellPanel.GetComponent<VerticalLayoutGroup>();
        if (existingVLG != null) DestroyImmediate(existingVLG);
        var vlg = sellPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = true;

        // ── Page 1: the shelves ──────────────────────────────────────────────
        Ui.shelvesPage = MkPage(sellPanel.transform, "Shelves");

        var head = MkHRow(Ui.shelvesPage.transform, 34);
        var title = MkText(head, "FISH MARKET", 20, C_Title, 34, FontStyles.Bold);
        title.characterSpacing = 6;
        title.GetComponent<LayoutElement>().preferredWidth = 190;
        Ui.planetText = MkText(head, "", 13, C_Sub, 34);
        Ui.walletText = MkText(head, "", 13, C_Sub, 34, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        MkDivider(Ui.shelvesPage.transform);

        var shelves = MkHRow(Ui.shelvesPage.transform, 400);
        shelves.GetComponent<HorizontalLayoutGroup>().spacing = 0;

        var bagShelf = MkShelf(shelves, out Ui.bagHeader, out Ui.bagContent,
                               "Nothing in your bag.\nGo fish.", false);
        var arrows = MkText(shelves, "⇄", 22, C_Hint, 400, FontStyles.Normal, TextAlignmentOptions.Center);
        arrows.GetComponent<LayoutElement>().preferredWidth = 36;
        var counterShelf = MkShelf(shelves, out Ui.counterHeader, out Ui.counterContent,
                                   "Click a fish on the left\nto put it on the counter.", true);
        MkDivider(Ui.shelvesPage.transform);

        var foot = MkHRow(Ui.shelvesPage.transform, 46);
        Ui.totalText = MkText(foot, "COUNTER  $0", 16, C_Label, 46, FontStyles.Bold);
        Ui.totalText.richText = true;
        Ui.btnIndex = MkButton(foot, "FISH PRICE INDEX", C_BtnIndex, 13);
        Ui.btnIndex.GetComponent<LayoutElement>().preferredWidth = 190;
        Ui.btnSell  = MkButton(foot, "Sell the counter", C_BtnSell, 14);
        Ui.btnSell.GetComponent<LayoutElement>().preferredWidth = 200;

        // ── Page 2: the index ────────────────────────────────────────────────
        Ui.indexPage = MkPage(sellPanel.transform, "Index");
        var ihead = MkHRow(Ui.indexPage.transform, 34);
        var ititle = MkText(ihead, "FISH PRICE INDEX", 20, C_Title, 34, FontStyles.Bold);
        ititle.characterSpacing = 6;
        ititle.GetComponent<LayoutElement>().preferredWidth = 250;
        Ui.indexSub = MkText(ihead, "", 12, C_Sub, 34);
        Ui.btnBack = MkButton(ihead, "‹  BACK", C_PanelBtnPrices, 13);
        Ui.btnBack.GetComponent<LayoutElement>().preferredWidth = 110;
        MkDivider(Ui.indexPage.transform);
        Ui.indexBody = MkScrollArea(Ui.indexPage.transform, 470);
        var ihint = Ui.indexBody.Find("EmptyHint"); if (ihint != null) ihint.gameObject.SetActive(false);
        Ui.indexPage.SetActive(false);

        // Earnings message lives on the canvas so it persists after the panel closes
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
        Ui.earningsMsg           = earnGO.AddComponent<TextMeshProUGUI>();
        Ui.earningsMsg.text      = "";
        Ui.earningsMsg.fontSize  = 18;
        Ui.earningsMsg.fontStyle = FontStyles.Bold;
        Ui.earningsMsg.alignment = TextAlignmentOptions.Center;
        Ui.earningsMsg.color     = C_Gold;
        Ui.earningsCG            = earnGO.AddComponent<CanvasGroup>();
        Ui.earningsCG.alpha      = 0f;
        earnGO.SetActive(false);
    }

    /// <summary>A full-panel page: a VLG with the panel's padding.</summary>
    static GameObject MkPage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(18, 18, 14, 14);
        v.spacing = 8;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return go;
    }

    /// <summary>One shelf: a header line and a scrolling column of cards.</summary>
    static Transform MkShelf(Transform parent, out TextMeshProUGUI header, out Transform content, string emptyHint, bool isCounter)
    {
        var shelf = new GameObject(isCounter ? "CounterShelf" : "BagShelf", typeof(RectTransform));
        shelf.transform.SetParent(parent, false);
        shelf.AddComponent<LayoutElement>().flexibleWidth = 1;
        var v = shelf.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(isCounter ? 10 : 0, isCounter ? 0 : 10, 0, 0);
        v.spacing = 6;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        header = MkText(shelf.transform, "", 11, C_Sub, 20, FontStyles.Bold);
        header.characterSpacing = 4;
        content = MkScrollArea(shelf.transform, 372);
        if (isCounter) content.parent.parent.GetComponent<Image>().color = C_CounterBg;
        var hint = content.Find("EmptyHint")?.GetComponent<TextMeshProUGUI>();
        if (hint != null) hint.text = emptyHint;
        return shelf.transform;
    }

    /// <summary>A fish card the whole of which is a button: thumbnail, name, the
    /// bucket word + $/lb line, and what this market pays for it. On the bag
    /// shelf a click stages it; on the counter a click returns it.</summary>
    void MkShelfCard(Transform parent, FishEntry f, bool onCounter, System.Action onClick)
    {
        var card = new GameObject("FishCard", typeof(RectTransform));
        card.transform.SetParent(parent, false);
        card.AddComponent<LayoutElement>().preferredHeight = 62;
        var bg = card.AddComponent<Image>();
        bg.color = onCounter ? C_CounterCard : C_CardBg;
        var btn = card.AddComponent<Button>();
        var col = btn.colors;
        col.normalColor = Color.white;
        col.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        col.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = col;
        btn.onClick.AddListener(() => onClick());

        var hlg = card.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 10, 6, 6);
        hlg.spacing = 8;
        hlg.childControlHeight = true; hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        // Thumbnail: the same render the hotbar slot uses, cached on the entry.
        RenderTexture preview = f.cachedHotbarPreview;
        if (preview == null && FishingdexManager.Instance != null)
        {
            preview = FishingdexManager.Instance.RenderFish(f, 116, 84);
            f.cachedHotbarPreview = preview;
        }
        if (preview != null)
        {
            var imgGO = new GameObject("Preview", typeof(RectTransform));
            imgGO.transform.SetParent(card.transform, false);
            imgGO.AddComponent<LayoutElement>().preferredWidth = 58;
            var raw = imgGO.AddComponent<RawImage>();
            raw.texture = preview; raw.raycastTarget = false;
        }
        else
        {
            var dot = new GameObject("Dot", typeof(RectTransform));
            dot.transform.SetParent(card.transform, false);
            dot.AddComponent<LayoutElement>().preferredWidth = 5;
            var di = dot.AddComponent<Image>(); di.color = FishSpeciesVisuals.TintOf(f); di.raycastTarget = false;
        }

        var info = new GameObject("Info", typeof(RectTransform));
        info.transform.SetParent(card.transform, false);
        info.AddComponent<LayoutElement>().flexibleWidth = 1;
        var iv = info.AddComponent<VerticalLayoutGroup>();
        iv.childControlWidth = true; iv.childControlHeight = true;
        iv.childForceExpandWidth = true; iv.spacing = 1;
        iv.childAlignment = TextAnchor.MiddleLeft;

        var name = MkText(info.transform, f.DisplayName, 14, C_Label, 20, FontStyles.Bold);
        name.raycastTarget = false;
        string word = BucketWordFor(f);
        string body = BodyName;
        string rate = string.IsNullOrEmpty(body) || !PlanetEconomy.HasTable(body)
            ? "" : $"  ·  ${PlanetEconomy.PricePerLbNow(body, SpeciesIdOf(f)):0.00}/lb";
        var det = MkText(info.transform, $"{f.weightLbs} lb" + (string.IsNullOrEmpty(word) ? "" : $"  ·  <color=#{BucketHex(word)}>{word.ToUpperInvariant()}</color>") + rate, 11, C_Sub, 16);
        det.richText = true; det.raycastTarget = false;

        var price = MkText(card.transform, $"${PriceAt(f)}", 15, C_Gold, 50, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        price.GetComponent<LayoutElement>().preferredWidth = 74;
        price.raycastTarget = false;
    }

    /// <summary>One line of the index: tint, name (+ appetite note), how many are
    /// in the bag, and the $/lb this market pays right now.</summary>
    static void MkIndexRow(Transform parent, string speciesId, int inBag, bool showAppetite, int fullness, float perLb)
    {
        var row = new GameObject("IndexRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = 34;
        row.AddComponent<Image>().color = C_CardBg;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(10, 12, 4, 4); hlg.spacing = 10;
        hlg.childControlHeight = true; hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        int idx = FishingRules.IndexOfId(speciesId);
        var sp = idx >= 0 ? FishingRules.Species[idx] : default;
        var dot = new GameObject("Dot", typeof(RectTransform));
        dot.transform.SetParent(row.transform, false);
        dot.AddComponent<LayoutElement>().preferredWidth = 5;
        dot.AddComponent<Image>().color = idx >= 0 ? new Color32(sp.tintR, sp.tintG, sp.tintB, 255) : (Color32)C_Hint;

        var name = MkText(row.transform, PlanetEconomy.DisplayName(speciesId), 13, C_Label, 26, FontStyles.Bold);
        name.GetComponent<LayoutElement>().preferredWidth = 170;

        string note = !showAppetite ? "" : (fullness <= 0 ? "fresh · full price" : $"cooling · −{fullness * 15}%");
        var noteT = MkText(row.transform, note, 11, C_Sub, 26);
        noteT.GetComponent<LayoutElement>().flexibleWidth = 1;

        var bagT = MkText(row.transform, inBag > 0 ? $"{inBag} IN YOUR BAG" : "", 10, C_Title, 26, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        bagT.characterSpacing = 3;
        bagT.GetComponent<LayoutElement>().preferredWidth = 120;

        var priceT = MkText(row.transform, $"${perLb:0.00}/lb", 14, C_Gold, 26, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        priceT.GetComponent<LayoutElement>().preferredWidth = 90;
    }

    static string BucketHex(string word)
    {
        switch (word)
        {
            case "Delicacy": return ColorUtility.ToHtmlStringRGB(C_Rare);
            case "Imported": return ColorUtility.ToHtmlStringRGB(C_Uncommon);
            case "Local":    return ColorUtility.ToHtmlStringRGB(C_Common);
            default:         return ColorUtility.ToHtmlStringRGB(C_Hint);
        }
    }

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


    // ── The bounty (Phase C) ──────────────────────────────────────────────────
    // Appended at the END so the scene's serialized field order is untouched.
    [Header("Bounty fish (GRULABU)")]
    [Tooltip("The vendor's story, told line by line on the greeting text. Always available.")]
    [TextArea(2, 5)]
    public string[] bountyStoryLines =
    {
        "The bounty fish? Sit down. Well -- stand there, but quietly.",
        "Years back a kid was playing on the north shore of the lake. Big splash, little scream, and the water went flat like nothing happened. They found one shoe. Wet.",
        "GRULABU, the old-timers call it. Red as a warning light, long as a shuttle, and it does not lose.",
        "Bring me that fish, whole, and I pay five hundred. Not for the meat. For the peace of mind.",
    };
    [TextArea(2, 5)]
    public string[] bountyDoneLines =
    {
        "The GRULABU is gone. The lake is quieter. I sleep now. You did that.",
    };
    [TextArea(2, 5)]
    public string[] bountyTurnInLines =
    {
        "...That's it. That's the one. Look at the teeth on it.",
        "Five hundred, as promised. Spend it somewhere far from the water.",
    };
    public string bountyRefuseSaleLine = "That's the bounty fish. Don't sell it by the pound like a truttle -- turn it in and I'll pay the bounty.";
    [Tooltip("The one tunable ([OPEN-4]).")]
    public int bountyReward = 500;
    public string bountyTurnedInFlag = "grulabu_turned_in";

    int _bountyRowStart = -1;
    int _bountyTurnInRow = -1;

    static bool BountyFlag(string flag) =>
        StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(flag);

    static FishEntry FindBountyFishOnPlayer() =>
        Hotbar.Instance != null
            ? Hotbar.Instance.FindFish(f => FishingRules.IsBounty(f.ResolveSpecies()))
            : null;

    /// The story, or the done-state once the bounty is paid. Runs on the
    /// greeting text with the same typewriter/click cadence as the greeting,
    /// then the choice menu comes back.
    IEnumerator TellBountyStory()
    {
        // Dialogue Studio: node "bounty" of npc_fishmarket.json, when present
        // (its routes pick story vs done via Probe bountyTurnedIn).
        var graph = StoryContent.GetNpcGraph("npc_fishmarket");
        if (graph != null && graph.FindNode("bounty") != null)
        {
            yield return SpeakGraphOnGreeting(graph, "bounty");
        }
        else
        {
            var ls = BountyFlag(bountyTurnedInFlag) ? bountyDoneLines : bountyStoryLines;
            yield return SpeakOnGreeting(ls);
        }
        if (playerInRange) ShowPostGreetingChoice();
        else PlayerController.isInDialogue = false;
    }

    // ── Dialogue Studio graph (greeting text as the typewriter surface) ────

    NpcGraphWalker GraphWalker(Conversation graph) => new NpcGraphWalker
    {
        Speak   = SpeakOneOnGreetingText,
        InRange = () => playerInRange,
        Probe   = n => n == "bountyTurnedIn" && BountyFlag(bountyTurnedInFlag),
    };

    IEnumerator SpeakOneOnGreetingText(string line)
    {
        yield return StartCoroutine(TypewriterLine(line, greetingText));
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !playerInRange);
    }

    IEnumerator SpeakGraphOnGreeting(Conversation graph, string startNode)
    {
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        greetingActive = true;
        PlayerController.isInDialogue = true;
        InteractPromptUI.Clear(this);
        if (greetingText != null) greetingText.gameObject.SetActive(true);
        yield return GraphWalker(graph).Run(graph, startNode);
        if (greetingText != null) greetingText.gameObject.SetActive(false);
        greetingActive = false;
        greetingCoroutine = null;
    }

    IEnumerator SpeakOnGreeting(string[] ls)
    {
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        greetingActive = true;
        PlayerController.isInDialogue = true;
        InteractPromptUI.Clear(this);
        if (greetingText != null) greetingText.gameObject.SetActive(true);
        for (int i = 0; ls != null && i < ls.Length && playerInRange; i++)
        {
            yield return StartCoroutine(TypewriterLine(ls[i], greetingText));
            _waitingForClick = true;
            yield return new WaitUntil(() => !_waitingForClick || !playerInRange);
        }
        if (greetingText != null) greetingText.gameObject.SetActive(false);
        greetingActive = false;
        greetingCoroutine = null;
    }

    /// The exact fish leaves the player, the money lands on THIS player (money
    /// is personal in co-op; the interacting machine is the payee), the flag
    /// flips the story to its done state. Double-trigger safe: the row is
    /// rebuilt from the flag and the fish's presence every time.
    void TurnInBounty()
    {
        var fish = FindBountyFishOnPlayer();
        if (fish == null || BountyFlag(bountyTurnedInFlag)) { ShowPostGreetingChoice(); return; }
        if (Hotbar.Instance == null || !Hotbar.Instance.RemoveFishEntry(fish)) { ShowPostGreetingChoice(); return; }

        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(bountyReward);
        if (StoryDirector.Instance != null) StoryDirector.Instance.SetFlag(bountyTurnedInFlag, true);
        if (saleClip != null && saleSource != null) saleSource.PlayOneShot(saleClip, saleVolume);
        Debug.Log($"[FishMarket] Bounty turned in: {fish.DisplayName} {fish.weightLbs}lb -> ${bountyReward}.");

        greetingCoroutine = StartCoroutine(TurnInRoutine());
    }

    IEnumerator TurnInRoutine()
    {
        yield return SpeakOnGreeting(bountyTurnInLines);
        ShowEarningsMessage($"Bounty paid!  ${bountyReward}");
        if (playerInRange) ShowPostGreetingChoice();
        else PlayerController.isInDialogue = false;
    }
}
