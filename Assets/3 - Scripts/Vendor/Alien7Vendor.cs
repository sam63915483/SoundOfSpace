using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Goods-vendor NPC. BoxCollider trigger detects the player; F to talk; greeting line
/// types out via the typewriter; left-click after typing opens the shop UI. Owns the
/// inventory list, money validation, and grant dispatch. Mirrors BonfireNPCDialogue's
/// dialogue half and FishMarketNPC's open/close UI handoff.
/// </summary>
public class Alien7Vendor : MonoBehaviour
{
    public enum PurchaseResult { Success, NotEnoughMoney, AlreadyOwned, InvalidItem, NoInventorySpace }

    [Header("UI References (auto-pulled from existing NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Shop UI")]
    [Tooltip("Auto-found at runtime if null. Created procedurally on first vendor open if no instance exists in the scene.")]
    public GoodsVendorShopUI shopUI;

    [Header("Greeting")]
    [TextArea(2, 5)]
    public string greetingLine = "I hope you brought money....";

    [Header("Inventory")]
    [Tooltip("ShopItem assets sold by this vendor. Drag in ShopItem ScriptableObjects.")]
    public ShopItem[] inventory;

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] float typewriterVolume = 0.3f;
    AudioSource typewriterSource;

    [Header("Purchase Sound")]
    [SerializeField] AudioClip purchaseClip;
    [SerializeField, Range(0, 1)] float purchaseVolume = 0.7f;
    AudioSource purchaseSource;

    bool _playerInRange;
    bool _conversationActive;
    bool _shopOpen;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    // Set when the player explicitly picks "Leave" on the post-greeting choice
    // panel. Suppresses the "Press F to talk" prompt and the F-to-start gate
    // until the player physically walks out of the trigger zone and back in —
    // otherwise they'd be stuck in an F → dialog → Leave → F loop while
    // standing in the NPC's trigger. Reset in OnTriggerEnter.
    bool _suppressPromptUntilExit;
    Coroutine _dialogueCoroutine;

    NPCSellDustOption _sellDustOption;

    PistolController _pistolCached;
    AxeController _axeCached;
    PlayerController _playerCached;

    ShopItemKind _pendingTutorialKind = ShopItemKind.None;

    public ShopItem[] Inventory => inventory;

    void Start()
    {
        if (dialogueText == null || talkPromptText == null)
        {
            var existing = FindObjectOfType<NPCDialogue>();
            if (existing != null)
            {
                if (dialogueText == null)   dialogueText   = existing.dialogueText;
                if (talkPromptText == null) talkPromptText = existing.talkPromptText;
            }
        }

        if (dialogueText != null)   dialogueText.gameObject.SetActive(false);
        if (talkPromptText != null) talkPromptText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);

        DialogueTextStyling.ApplyOutline(dialogueText);
        DialogueTextStyling.ApplyOutline(talkPromptText);

        if (shopUI == null) shopUI = FindObjectOfType<GoodsVendorShopUI>(true);
        if (shopUI == null)
        {
            var go = new GameObject("GoodsVendorShopUI");
            shopUI = go.AddComponent<GoodsVendorShopUI>();
        }

        typewriterSource = GetComponent<AudioSource>();
        if (typewriterSource == null) typewriterSource = gameObject.AddComponent<AudioSource>();
        typewriterSource.playOnAwake = false;
        typewriterSource.loop = true;
        typewriterSource.volume = typewriterVolume;

        purchaseSource = gameObject.AddComponent<AudioSource>();
        purchaseSource.playOnAwake = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
        // Fresh entry clears the post-Leave suppression so the prompt comes
        // back when the player returns from outside the trigger zone.
        _suppressPromptUntilExit = false;
        if (!_conversationActive && !_shopOpen) ShowPrompt();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        InteractPromptUI.Clear(this);
        if (_conversationActive) StopConversation();
    }

    void Update()
    {
        if (_shopOpen) return;

        if (_playerInRange && !_conversationActive && !_suppressPromptUntilExit)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }

        if (_playerInRange && !_conversationActive && !_suppressPromptUntilExit && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            StartConversation();
            return;
        }

        if (!_conversationActive) return;

        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping) _skipTyping = true;
            else if (_waitingForClick) _waitingForClick = false;
        }
    }

    void ShowPrompt()
    {
        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
    }

    void StartConversation()
    {
        if (_conversationActive) return;
        _conversationActive = true;
        InteractPromptUI.Clear(this);
        if (dialogueText != null)   dialogueText.gameObject.SetActive(true);
        PlayerController.isInDialogue = true;
        NPCConversationTracker.NotifyStart(this);
        _sellDustOption = NPCSellDustOption.GetOrAdd(this);
        _sellDustOption.RollFresh();
        _dialogueCoroutine = StartCoroutine(PlayDialogueSequence());
    }

    void StopConversation()
    {
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();
        if (_dialogueCoroutine != null)
        {
            StopCoroutine(_dialogueCoroutine);
            _dialogueCoroutine = null;
        }
        _conversationActive = false;
        _isTyping = false;
        _skipTyping = false;
        _waitingForClick = false;
        if (typewriterSource != null && typewriterSource.isPlaying) typewriterSource.Stop();
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        // Don't auto-show the prompt again. If the player picked "Leave" they
        // shouldn't be re-prompted while standing in the same trigger zone —
        // they have to walk out and back in. If they walked out of the trigger
        // mid-conversation, _playerInRange is already false so the suppression
        // is moot. Either way, _suppressPromptUntilExit gates Update's prompt
        // re-assert until OnTriggerEnter clears it.
        _suppressPromptUntilExit = true;
        InteractPromptUI.Clear(this);
    }

    IEnumerator PlayDialogueSequence()
    {
        if (!string.IsNullOrEmpty(greetingLine))
        {
            yield return StartCoroutine(TypewriterLine(greetingLine));
            yield return StartCoroutine(WaitForPlayerClick());
        }
        ShowPostGreetingChoice();
    }

    IEnumerator TypewriterLine(string line)
    {
        if (dialogueText == null) yield break;
        _isTyping = true;
        _skipTyping = false;

        if (typewriterLoopClip != null && typewriterSource != null)
        {
            typewriterSource.clip = typewriterLoopClip;
            typewriterSource.volume = typewriterVolume;
            typewriterSource.Play();
        }

        yield return DialogueTextStyling.RevealCharsTMP(dialogueText, line, charDelay, () => _skipTyping);

        if (typewriterSource != null && typewriterSource.isPlaying)
            typewriterSource.Stop();

        _isTyping = false;
        _skipTyping = false;
    }

    IEnumerator WaitForPlayerClick()
    {
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !_playerInRange);
    }

    void OpenShop()
    {
        if (shopUI == null) { StopConversation(); return; }
        _shopOpen = true;
        _conversationActive = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        if (typewriterSource != null && typewriterSource.isPlaying) typewriterSource.Stop();
        // PlayerController.isInDialogue stays true while the shop is open so movement is suppressed.
        shopUI.Open(this);
    }

    /// <summary>Called by GoodsVendorShopUI when the user closes the shop.</summary>
    public void OnShopClosed()
    {
        _shopOpen = false;
        PlayerController.isInDialogue = false;
        if (_playerInRange) ShowPrompt();
        OfferPendingTutorial();
    }

    public PurchaseResult Purchase(ShopItem item)
    {
        if (item == null) return PurchaseResult.InvalidItem;
        if (item.oneTimePurchase && IsAlreadyOwned(item.kind)) return PurchaseResult.AlreadyOwned;
        if (PlayerWallet.Instance == null) return PurchaseResult.InvalidItem;
        // Phase 3: refuse FishBag purchase before charging money if no empty
        // hotbar slot. The bag has to land somewhere and we don't auto-spill
        // to storage on purchase.
        if (item.kind == ShopItemKind.FishBag
            && Hotbar.Instance != null
            && !Hotbar.Instance.HasEmptyHotbarSlot())
            return PurchaseResult.NoInventorySpace;
        if (!PlayerWallet.Instance.SpendMoney(item.price)) return PurchaseResult.NotEnoughMoney;
        if (purchaseClip != null && purchaseSource != null) purchaseSource.PlayOneShot(purchaseClip, purchaseVolume);
        // Suppress the hotbar's "new item acquired" sound for equippables granted
        // by this purchase — the purchase sound above is the feedback instead.
        Hotbar.Instance?.SuppressAcquireSoundOnce();
        GrantItem(item.kind);
        return PurchaseResult.Success;
    }

    public bool IsAlreadyOwned(ShopItemKind kind)
    {
        switch (kind)
        {
            case ShopItemKind.Pistol:
                if (_pistolCached == null) _pistolCached = FindObjectOfType<PistolController>(true);
                return _pistolCached != null && _pistolCached.IsUnlocked;
            case ShopItemKind.Axe:
                if (_axeCached == null) _axeCached = FindObjectOfType<AxeController>(true);
                return _axeCached != null && _axeCached.IsUnlocked;
            case ShopItemKind.Jetpack:
                if (_playerCached == null) _playerCached = FindObjectOfType<PlayerController>(true);
                return _playerCached != null && _playerCached.JetpackUnlocked;
            // Phase 3 goods
            case ShopItemKind.FishingRod:
            {
                var rod = FindObjectOfType<FishingRodController>(true);
                return rod != null && rod.IsUnlocked;
            }
            case ShopItemKind.WaterBottle:
            {
                var bottle = FindObjectOfType<WaterBottleController>(true);
                return bottle != null && bottle.IsUnlocked;
            }
            case ShopItemKind.FishBag:
                return Hotbar.Instance != null && Hotbar.Instance.HasFishBagAnywhere();
            default:
                return false;
        }
    }

    void GrantItem(ShopItemKind kind)
    {
        switch (kind)
        {
            case ShopItemKind.Pistol:
                if (_pistolCached == null) _pistolCached = FindObjectOfType<PistolController>(true);
                if (_pistolCached != null)
                {
                    // Unlock only — do NOT auto-equip. The player can fire on
                    // their next left-click after closing the shop UI, which
                    // pointed at the vendor sent shots into the NPC.
                    _pistolCached.Unlock();
                }
                break;
            case ShopItemKind.Axe:
                if (_axeCached == null) _axeCached = FindObjectOfType<AxeController>(true);
                if (_axeCached != null)
                {
                    _axeCached.Unlock();
                    _axeCached.ForceEquipAxe();
                }
                break;
            case ShopItemKind.Jetpack:
                if (_playerCached == null) _playerCached = FindObjectOfType<PlayerController>(true);
                if (_playerCached != null) _playerCached.UnlockJetpack();
                break;
            // Phase 3 goods
            case ShopItemKind.FishingRod:
            {
                var rod = FindObjectOfType<FishingRodController>(true);
                if (rod != null) rod.Unlock();
                break;
            }
            case ShopItemKind.WaterBottle:
            {
                var bottle = FindObjectOfType<WaterBottleController>(true);
                if (bottle != null) bottle.Unlock();
                break;
            }
            case ShopItemKind.FishBag:
                // Pre-check in Purchase already verified space; this should
                // succeed. Returns false only if a race emptied the hotbar
                // between check and grant, which can't happen single-threaded.
                Hotbar.Instance?.TryAddBag();
                break;
            // Future: case ShopItemKind.AmmoPack: ... break;
        }
        _pendingTutorialKind = kind;
    }

    void OfferPendingTutorial()
    {
        var kind = _pendingTutorialKind;
        _pendingTutorialKind = ShopItemKind.None;
        switch (kind)
        {
            case ShopItemKind.Axe:     BonusTutorial.OfferAxeBuilding(); break;
            case ShopItemKind.Pistol:  BonusTutorial.OfferPistol();      break;
            case ShopItemKind.Jetpack: BonusTutorial.OfferJetpack();     break;
        }
    }

    void ShowPostGreetingChoice()
    {
        bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row("Open shop", true),
            new PostGreetingChoicePanel.Row(hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust),
            new PostGreetingChoicePanel.Row("Leave", true),
        };
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    void HandleChoice(int index)
    {
        switch (index)
        {
            case 0: OpenShop(); break;
            case 1: OpenSellDust(); break;
            case 2: StopConversation(); break;
        }
    }

    void OpenSellDust()
    {
        if (_sellDustOption == null) { StopConversation(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: "Goods Vendor",
            acceptChance: _sellDustOption.AcceptChance,
            pricePerDust: _sellDustOption.PricePerDust,
            preferredMaxQty: _sellDustOption.PreferredMaxQty,
            onClose: ShowPostGreetingChoice
        );
    }
}
