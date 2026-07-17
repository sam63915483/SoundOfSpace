using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Ship-vendor NPC. Mirrors Alien7Vendor (BoxCollider trigger detects the player,
/// F to talk, typewriter greeting, left-click after typing opens the shop UI),
/// but instead of unlocking a hotbar item, a successful purchase SPAWNS the
/// configured ship prefab 10 m behind the vendor and 1 m up, oriented upright
/// (matching the vendor's rotation so it aligns with planet gravity).
/// Requires NPCWaveAnimation so the NPC waves and tracks the player's head.
/// </summary>
[RequireComponent(typeof(NPCWaveAnimation))]
public class ShipMarketNPC : MonoBehaviour
{
    public enum PurchaseResult { Success, NotEnoughMoney, InvalidItem, NoSpawnPrefab, AlreadyHoldingItem }

    [Header("UI References (auto-pulled from existing NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Shop UI")]
    [Tooltip("Auto-found at runtime if null. Created procedurally on first vendor open if no instance exists in the scene.")]
    public ShipMarketShopUI shopUI;

    [Header("Greeting")]
    [TextArea(2, 5)]
    public string greetingLine = "Need a new ride? Best hulls this side of the asteroid belt. Cash only.";

    [Header("Inventory")]
    [Tooltip("ShopItem assets sold by this vendor. Drag in a ShopItem for the ship (price = 2000).")]
    public ShopItem[] inventory;

    [Header("Ship Spawn")]
    [Tooltip("Prefab instantiated when a ship is purchased. Drag SHIP44 here.")]
    public GameObject shipPrefab;
    [Tooltip("Distance behind the vendor (along -transform.forward) where the new ship spawns.")]
    public float spawnDistanceBack = 30f;
    [Tooltip("Vertical offset above the vendor's local up axis where the new ship spawns.")]
    public float spawnHeightUp = 3f;

    [Header("Ship Part Pickups")]
    [Tooltip("Pickup prefab spawned + auto-equipped on PartLeftThruster purchase.")]
    public GameObject leftThrusterPickupPrefab;
    [Tooltip("Pickup prefab spawned + auto-equipped on PartRightThruster purchase.")]
    public GameObject rightThrusterPickupPrefab;
    [Tooltip("Pickup prefab spawned + auto-equipped on PartDish purchase.")]
    public GameObject dishPickupPrefab;
    [Tooltip("Pickup prefab spawned + auto-equipped on PartSolarPanel purchase.")]
    public GameObject solarPanelPickupPrefab;
    [Tooltip("Pickup prefab spawned + auto-equipped on SpaceNetLeft purchase.")]
    public GameObject spaceNetLeftPickupPrefab;
    [Tooltip("Pickup prefab spawned + auto-equipped on SpaceNetRight purchase.")]
    public GameObject spaceNetRightPickupPrefab;

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

    NPCSellDustOption _sellDustOption;

    bool _playerInRange;
    bool _conversationActive;
    bool _shopOpen;
    // See Alien7Vendor — set when player picks "Leave"; gates the talk prompt
    // and F-to-talk until OnTriggerEnter clears it (player walked out + back).
    bool _suppressPromptUntilExit;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    Coroutine _dialogueCoroutine;

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

        if (shopUI == null) shopUI = FindObjectOfType<ShipMarketShopUI>(true);
        if (shopUI == null)
        {
            var go = new GameObject("ShipMarketShopUI");
            shopUI = go.AddComponent<ShipMarketShopUI>();
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

    // Cached prompt text — rebuilt only when the input source flips (KBM ↔
    // controller), not every frame. Avoids ~1.2 KB/frame GC while in range.
    string _promptCached;
    TutorialGate.InputSource _promptCachedSource = (TutorialGate.InputSource)(-1);

    void Update()
    {
        if (_shopOpen) return;

        if (_playerInRange && !_conversationActive && !_suppressPromptUntilExit)
        {
            var src = TutorialGate.LastSource;
            if (_promptCached == null || src != _promptCachedSource)
            {
                _promptCachedSource = src;
                _promptCached = $"Press {PromptGlyphs.Interact} to talk";
            }
            InteractPromptUI.Show(this, _promptCached);
        }

        if (_playerInRange && !_conversationActive && !_suppressPromptUntilExit && InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
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
            npcName: "Ship Vendor",
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
        // Suppress prompt until player walks out + back in — see field comment.
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

    /// <summary>Called by ShipMarketShopUI when the user closes the shop.</summary>
    public void OnShopClosed()
    {
        _shopOpen = false;
        PlayerController.isInDialogue = false;
        if (_playerInRange) ShowPrompt();
    }

    public PurchaseResult Purchase(ShopItem item)
    {
        if (item == null) return PurchaseResult.InvalidItem;
        if (PlayerWallet.Instance == null) return PurchaseResult.InvalidItem;

        switch (item.kind)
        {
            case ShopItemKind.ShipFull:
            case ShopItemKind.ShipNoDish:
            case ShopItemKind.ShipHull:
                if (shipPrefab == null) return PurchaseResult.NoSpawnPrefab;
                if (!PlayerWallet.Instance.SpendMoney(item.price)) return PurchaseResult.NotEnoughMoney;
                if (purchaseClip != null && purchaseSource != null) purchaseSource.PlayOneShot(purchaseClip, purchaseVolume);
                SpawnShip(item.kind);
                // Cold Company (Main Mission 1): buying a flyable ship advances the mission.
                if (item.kind == ShopItemKind.ShipFull || item.kind == ShopItemKind.ShipNoDish)
                    ColdCompany.NotifyShipBought();
                return PurchaseResult.Success;

            case ShopItemKind.PartLeftThruster:
            case ShopItemKind.PartRightThruster:
            case ShopItemKind.PartDish:
            case ShopItemKind.PartSolarPanel:
            case ShopItemKind.SpaceNetLeft:
            case ShopItemKind.SpaceNetRight:
                {
                    var pickup = FindObjectOfType<PlayerPickup>(true);
                    if (pickup != null && pickup.IsHoldingObject)
                        return PurchaseResult.AlreadyHoldingItem;
                    var prefab = PickupPrefabFor(item.kind);
                    if (prefab == null) return PurchaseResult.NoSpawnPrefab;
                    if (!PlayerWallet.Instance.SpendMoney(item.price)) return PurchaseResult.NotEnoughMoney;
                    if (purchaseClip != null && purchaseSource != null) purchaseSource.PlayOneShot(purchaseClip, purchaseVolume);
                    GrantPartPickup(prefab, item.kind);
                    return PurchaseResult.Success;
                }

            default:
                return PurchaseResult.InvalidItem;
        }
    }

    /// <summary>Ships and parts aren't "owned" per-kind — multiple purchases stack. UI uses this.</summary>
    public bool IsAlreadyOwned(ShopItemKind kind) => false;

    void SpawnShip(ShopItemKind tier)
    {
        if (shipPrefab == null) return;
        Vector3 spawnPos = transform.position
            - transform.forward * spawnDistanceBack
            + transform.up * spawnHeightUp;
        Quaternion spawnRot = transform.rotation;
        SpawnShipInstance(shipPrefab, tier, spawnPos, spawnRot, matchNearestVelocity: true);
    }

    // Used by the save system to re-instantiate a previously bought ship.
    // `attachL/R/D/S` override the tier defaults so a Hull that the player
    // bought parts for and rebuilt loads back fully attached.
    public static GameObject RespawnFromSave(
        ShopItemKind tier,
        Vector3 worldPos, Quaternion worldRot, Vector3 worldVel,
        bool attachL, bool attachR, bool attachD, bool attachSolar)
    {
        var npc = FindObjectOfType<ShipMarketNPC>();
        if (npc == null || npc.shipPrefab == null) return null;
        var go = npc.SpawnShipInstance(npc.shipPrefab, tier, worldPos, worldRot, matchNearestVelocity: false);
        if (go == null) return null;
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.velocity = worldVel;
        var detach = go.GetComponent<ThrusterDetachOnImpact>();
        if (detach != null) detach.ApplyAttachment(attachL, attachR, attachD, attachSolar);
        return go;
    }

    /// Public so GravityDebugUI's "+Ship" button can route through the same
    /// spawn path a real purchase uses — gets the BoughtShip marker, the
    /// stable shipNumber, attachment-by-tier config, and EndlessManager
    /// registration. Without this, debug-spawned ships were untracked by
    /// the save system and broke the "first bought stays Ship 1" ordering.
    public GameObject SpawnShipInstance(GameObject prefab, ShopItemKind tier, Vector3 spawnPos, Quaternion spawnRot, bool matchNearestVelocity)
    {
        var instance = Instantiate(prefab, spawnPos, spawnRot);
        instance.name = prefab.name + "_" + tier;

        // Tag as a bought ship so the save system can find/serialize it
        // separately from the scene's main ship.
        var marker = instance.GetComponent<BoughtShip>();
        if (marker == null) marker = instance.AddComponent<BoughtShip>();
        marker.tier = tier;
        // Assign a stable ship number: max existing + 1. The save system
        // overrides this on load via ApplyExtraShips so saved numbers
        // round-trip exactly.
        marker.shipNumber = NextShipNumber();

        // Half tanks on a fresh spawn — vendor purchase or debug spawn alike.
        // Save-load overrides this in SaveCollector.ApplyExtraShips when a
        // saved ship is re-spawned from disk.
        var freshShip = instance.GetComponent<Ship>();
        if (freshShip != null)
        {
            freshShip.SetPower(freshShip.powerMax * 0.5f);
            freshShip.SetFuel (freshShip.fuelMax  * 0.5f);
        }

        if (matchNearestVelocity)
        {
            var boughtRb = instance.GetComponent<Rigidbody>();
            if (boughtRb != null)
            {
                CelestialBody nearest = FindNearestBody(spawnPos);
                if (nearest != null) boughtRb.velocity = nearest.velocity;
            }
        }

        // ── Apply tier configuration ──────────────────────────────────
        // Full: all 4 parts attached. NoDish: dish off. Hull: nothing
        // attached (player must buy + install thrusters before flying).
        var detach = instance.GetComponent<ThrusterDetachOnImpact>();
        if (detach != null)
        {
            switch (tier)
            {
                case ShopItemKind.ShipFull:
                    detach.ApplyAttachment(true, true, true, true); break;
                case ShopItemKind.ShipNoDish:
                    detach.ApplyAttachment(true, true, false, true); break;
                case ShopItemKind.ShipHull:
                    detach.ApplyAttachment(false, false, false, false); break;
            }
        }

        // SpaceNets: only the Full tier ships them pre-attached. NoDish and
        // Hull spawn without nets — the player buys + installs them as parts.
        // ThrusterDetachOnImpact.SetSpaceNetAttached toggles the on-ship
        // visuals without spawning the loose pickup (that path is reserved
        // for the crash-detach flow).
        if (detach != null)
        {
            bool netsAttached = tier == ShopItemKind.ShipFull;
            detach.SetSpaceNetAttached(netsAttached, netsAttached);
        }

        var em = FindObjectOfType<EndlessManager>();
        if (em != null) em.RegisterPhysicsObject(instance.transform);

        // The Ship's _introInputUnlocked flag is per-instance and starts
        // false; the legacy intro crash flow is the only thing that flips
        // it. New ships bought from the vendor (or spawned by the debug
        // menu) need that lock skipped — the player just teleported in and
        // is trying to fly. Without this, thrust does nothing for the
        // first few seconds of piloting until the ship clips a planet.
        var shipComp = instance.GetComponent<Ship>();
        if (shipComp != null) shipComp.MarkIntroComplete();
        return instance;
    }

    GameObject PickupPrefabFor(ShopItemKind kind)
    {
        switch (kind)
        {
            case ShopItemKind.PartLeftThruster:  return leftThrusterPickupPrefab;
            case ShopItemKind.PartRightThruster: return rightThrusterPickupPrefab;
            case ShopItemKind.PartDish:          return dishPickupPrefab;
            case ShopItemKind.PartSolarPanel:    return solarPanelPickupPrefab;
            case ShopItemKind.SpaceNetLeft:      return spaceNetLeftPickupPrefab;
            case ShopItemKind.SpaceNetRight:     return spaceNetRightPickupPrefab;
            default: return null;
        }
    }

    static int NextShipNumber()
    {
        int max = 0;
        var marked = Object.FindObjectsOfType<BoughtShip>(true);
        if (marked != null)
        {
            for (int i = 0; i < marked.Length; i++)
                if (marked[i] != null && marked[i].shipNumber > max) max = marked[i].shipNumber;
        }
        return max + 1;
    }

    void GrantPartPickup(GameObject prefab, ShopItemKind kind)
    {
        var pickup = FindObjectOfType<PlayerPickup>(true);
        if (pickup == null) return;

        // Spawn the pickup at the player's hold position and immediately
        // hand it to PlayerPickup.ForcePickup — the player walks away with
        // it in their hands exactly as if they just finished a hold-F pickup.
        Vector3 spawnPos = pickup.holdPosition != null
            ? pickup.holdPosition.position
            : pickup.transform.position + pickup.transform.forward * 1.5f;
        Quaternion spawnRot = pickup.holdPosition != null
            ? pickup.holdPosition.rotation
            : pickup.transform.rotation;
        var instance = Instantiate(prefab, spawnPos, spawnRot);
        instance.name = prefab.name + "_Bought";

        // Make sure the pickup is tagged correctly for ThrusterMount /
        // attach systems (PickupObject expects the type string to match).
        var thrusterPickup = instance.GetComponent<ThrusterPickup>();
        if (thrusterPickup != null)
        {
            thrusterPickup.thrusterType = kind switch
            {
                ShopItemKind.PartLeftThruster  => "Left",
                ShopItemKind.PartRightThruster => "Right",
                ShopItemKind.PartDish          => "Dish",
                ShopItemKind.PartSolarPanel    => "Solar",
                _ => thrusterPickup.thrusterType,
            };
        }
        pickup.ForcePickup(instance);
    }

    static CelestialBody FindNearestBody(Vector3 worldPos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.Position - worldPos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = b; }
        }
        return best;
    }
}
