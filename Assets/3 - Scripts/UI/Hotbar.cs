using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Hotbar : MonoBehaviour
{
    public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust, Fish, FishBag }

    public struct Slot
    {
        public ItemId id;
        public int count;
        // Populated only when id == ItemId.Fish. Null otherwise. Carries the
        // per-fish weight/color/tier so dragging a fish through the cursor or
        // round-tripping through saves preserves the data the dex and sell
        // flow rely on.
        public FishEntry fishData;
        // Populated only when id == ItemId.FishBag. null otherwise; always
        // length 5 when populated. Each entry is a regular Hotbar.Slot —
        // typically Fish, but the data layer doesn't enforce content.
        public Hotbar.Slot[] bagContents;
    }

    const int NumSlots = 7;
    const float SlotSize = 64f;
    const float ActiveSize = 80f;       // size when slot is the equipped/cursor active slot
    const float ActiveLift = 8f;        // pixels lifted above the row when active
    const float SlotSpacing = 14f;
    const float BottomMargin = 36f;

    static Hotbar instance;
    public static Hotbar Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("Hotbar");
        UnityEngine.Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<Hotbar>();
    }

    readonly Slot[] slots = new Slot[NumSlots];

    // ── Equippable registry ──────────────────────────────────────
    // One row per item. Adding a new equippable is an entry in BuildRegistry()
    // plus an enum value — no new branches in DetectAcquisitions / GetEquipped /
    // Equip / UnequipAll / ItemName. Each delegate captures its controller via
    // closure; the controller is looked up lazily in ResolveRefs.
    sealed class Entry
    {
        public ItemId Id;
        public string DisplayName;
        public MonoBehaviour Controller;       // null until found in scene
        public Sprite Icon;                    // sprite from controller.hotbarIcon
        public System.Func<bool> IsUnlocked;   // gating for DetectAcquisitions
        public System.Func<bool> IsEquipped;
        public System.Action ForceEquip;
        public System.Action ForceUnequip;
    }

    Entry[] _registry;

    WaterBottleController water;
    FishingRodController rod;
    GuitarController guitar;
    AxeController axe;
    PistolController pistol;
    Ship ship;
    bool _wasInDialogue;
    bool _wasPhoneOpen;

    int _animatedActiveIdx = -1;
    Coroutine[] _slotAnimRoutines = new Coroutine[NumSlots];

    // Phase 2: hold-LMB-eat state. _eatProgressSlot is the slot index the
    // player is currently holding LMB on (must be the equipped Fish slot).
    // _eatHeldSeconds counts up while held; consumption fires at EatHoldDuration.
    int _eatProgressSlot = -1;
    float _eatHeldSeconds = 0f;
    const float EatHoldDuration = 1.0f;

    Canvas canvas;
    SlotVisuals[] slotViews = new SlotVisuals[NumSlots];

    RectTransform _namePlateRT;
    Image _namePlateBg;
    Image _namePlateBorder;
    TextMeshProUGUI _namePlateText;
    CanvasGroup _namePlateGroup;

    class SlotVisuals
    {
        public RectTransform root;
        public Image glow;
        public Image border;
        public Image background;
        public Image accent;
        public Image itemIcon;
        public TextMeshProUGUI countText;
        // Phase 2: live-rendered fish preview from FishingdexManager.RenderFish.
        // RawImage (not Image) because the source is a RenderTexture; cached
        // per FishEntry on first paint so we don't re-render every frame.
        // Enabled only when slot.id == Fish; itemIcon disabled in that case.
        public RawImage fishPreview;
    }

    // Phase 2: hold-LMB-eat progress ring rendered at the center of the screen
    // around the player's crosshair / aim dot. Far more visible than a ring on
    // the hotbar at the bottom of the screen — the player is looking at the
    // center while eating. Built once in BuildUI, parented to the canvas root
    // (not the hotbar bar), painted in Refresh based on _eatHeldSeconds.
    Image _centerProgressRing;

    // Acquire-sound gating. The acquire one-shot fires when DetectAcquisitions
    // adds a newly-earned equippable — but NOT during the settle window right
    // after a scene loads (new-game population + save-load restoration both add
    // items then) and NOT when a shop purchase granted the item (the shop plays
    // its own sound). _acquireArmTime is reset on each gameplay scene load.
    float _acquireArmTime = -999f;
    int _suppressAcquireUntilFrame = -10;
    const float AcquireArmDelay = 1.5f;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        _acquireArmTime = Time.time;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedForAcquire;
        BuildUI();
        StartCoroutine(BorderPulse());
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedForAcquire;
    }

    void OnSceneLoadedForAcquire(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Re-arm the settle window each gameplay scene load so the start/restore
        // population doesn't blip the acquire sound for every item.
        if (scene.name != "MainMenu") _acquireArmTime = Time.time;
    }

    // Called by vendors right before granting a purchased equippable so the
    // acquire sound doesn't fire on top of the shop's own purchase sound.
    public void SuppressAcquireSoundOnce() { _suppressAcquireUntilFrame = Time.frameCount + 2; }

    void Update()
    {
        if (!ResolveRefs()) return;
        DetectAcquisitions();
        // Piloted state: pull from "is any ship piloted" — the cached
        // `ship` reference might be the wrong instance now that the player
        // can own multiple ships and teleport between them. Same pattern
        // GForceHUD uses.
        // Use the cached static (set on pilot enter, cleared on exit). The
        // previous fallback called Ship.FindPilotedShip() every frame the
        // player was on foot — FindObjectsOfType per Update.
        bool piloting = Ship.PilotedInstance != null && Ship.PilotedInstance.IsPiloted;
        bool inDialogue = PlayerController.isInDialogue;
        bool phoneOpen  = PlayerPhoneUI.IsOpen;

        if (inDialogue && !_wasInDialogue) UnequipAll();
        _wasInDialogue = inDialogue;

        // Phone uses player's hands — opening the phone (home / AI chat /
        // camera mode / any sub-screen) drops whatever was equipped and
        // blocks new equips while the phone is in use. Same shape as the
        // dialogue rule above.
        if (phoneOpen && !_wasPhoneOpen) UnequipAll();
        _wasPhoneOpen = phoneOpen;

        // Hide the hotbar entirely while piloting (no inventory swaps in
        // the cockpit) and while the system map is open (the map screen
        // hides all HUD canvases; without this check Update() would race
        // the map and immediately re-enable the hotbar). Same isMapOpen
        // gate other HUDs use.
        bool hideHotbar = piloting || PlayerController.isMapOpen;
        if (canvas != null && canvas.enabled == hideHotbar) canvas.enabled = !hideHotbar;

        if (!piloting && !inDialogue && !phoneOpen && !PlayerController.isMapOpen && !PlayerController.isInModalSlotUI)
        {
            HandleInput();
            TickEatHold();
        }
        else
        {
            // Any input gate active resets the hold timer so reopening doesn't
            // resume a stale progress ring.
            if (_eatProgressSlot != -1) { _eatProgressSlot = -1; _eatHeldSeconds = 0f; }
        }
        Refresh(piloting || inDialogue || phoneOpen);
    }

    // Phase 2: tick once per Update when the player is holding LMB on the
    // equipped Fish slot. Releasing the click or switching slots resets.
    void TickEatHold()
    {
        int eq = _equippedSlot;
        bool fishEquipped = eq >= 0 && eq < NumSlots
                         && slots[eq].id == ItemId.Fish
                         && slots[eq].fishData != null;
        if (!fishEquipped || !TutorialGate.FireHeld())
        {
            if (_eatProgressSlot != -1) { _eatProgressSlot = -1; _eatHeldSeconds = 0f; }
            return;
        }

        if (_eatProgressSlot != eq) { _eatProgressSlot = eq; _eatHeldSeconds = 0f; }
        _eatHeldSeconds += Time.deltaTime;

        if (_eatHeldSeconds >= EatHoldDuration)
        {
            ConsumeEquippedFish();
            _eatProgressSlot = -1;
            _eatHeldSeconds = 0f;
        }
    }

    void ConsumeEquippedFish()
    {
        int eq = _equippedSlot;
        if (eq < 0 || eq >= NumSlots) return;
        var slot = slots[eq];
        if (slot.id != ItemId.Fish || slot.fishData == null) return;

        RawFishConsumption.Consume(slot.fishData.fishType);
        slots[eq] = default;
        OnResourceChanged?.Invoke(ItemId.Fish);
    }

    // Throttle the FindObjectOfType re-search. Some equippables (pistol, ship)
    // may not exist for a long time, so searching every frame for a "may never
    // appear" target burns CPU forever (CLAUDE.md: throttle retries, see
    // LightLookAt). Once everything is found this whole block is skipped.
    float _resolveRetryTimer;
    // 2s, not 0.5s: each retry does 6× FindObjectOfType(true) (scans inactive
    // objects too), and pistol/ship simply don't exist for most of the early
    // game — so this fires forever. A 2s cadence cuts that idle cost 4×; an
    // equippable icon appearing up to 2s after the item spawns is imperceptible.
    const float ResolveRetryInterval = 2f;

    bool ResolveRefs()
    {
        bool anyMissing = water == null || rod == null || guitar == null
                          || axe == null || pistol == null || ship == null;
        if (anyMissing)
        {
            _resolveRetryTimer -= Time.unscaledDeltaTime;
            if (_resolveRetryTimer <= 0f)
            {
                _resolveRetryTimer = ResolveRetryInterval;
                if (water == null) water = FindObjectOfType<WaterBottleController>(true);
                if (rod == null) rod = FindObjectOfType<FishingRodController>(true);
                if (guitar == null) guitar = FindObjectOfType<GuitarController>(true);
                if (axe == null) axe = FindObjectOfType<AxeController>(true);
                if (pistol == null) pistol = FindObjectOfType<PistolController>(true);
                if (ship == null) ship = FindObjectOfType<Ship>(true);

                // (Re)build registry whenever a previously-missing controller
                // appears. BuildRegistry is cheap (5 closures).
                if (RegistryNeedsRebuild()) BuildRegistry();
            }
        }
        else if (RegistryNeedsRebuild())
        {
            // Refs all present but a cached controller went stale (scene
            // reload swapped instances) — rebuild and let the next frame
            // re-search via anyMissing.
            BuildRegistry();
        }

        return water != null || rod != null || guitar != null || axe != null || pistol != null;
    }

    bool RegistryNeedsRebuild()
    {
        if (_registry == null) return true;
        // If any cached Controller ref differs from the current scene controller, rebuild.
        for (int i = 0; i < _registry.Length; i++)
        {
            switch (_registry[i].Id)
            {
                case ItemId.WaterBottle: if (_registry[i].Controller != (MonoBehaviour)water) return true; break;
                case ItemId.FishingRod:  if (_registry[i].Controller != (MonoBehaviour)rod) return true; break;
                case ItemId.Guitar:      if (_registry[i].Controller != (MonoBehaviour)guitar) return true; break;
                case ItemId.Axe:         if (_registry[i].Controller != (MonoBehaviour)axe) return true; break;
                case ItemId.Pistol:      if (_registry[i].Controller != (MonoBehaviour)pistol) return true; break;
            }
        }
        return false;
    }

    public static int StackMax(ItemId id)
    {
        return id switch
        {
            ItemId.Wood => 100,
            ItemId.Crystal => 20,
            ItemId.SpaceDust => 100,
            _ => 1,
        };
    }

    public event System.Action<ItemId> OnResourceChanged;

    public int GetResourceTotal(ItemId resource)
    {
        if (!IsResource(resource)) return 0;
        int sum = 0;
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == resource) sum += slots[i].count;
        return sum;
    }

    // Returns leftover amount that didn't fit (0 = fully accepted).
    public int AddResource(ItemId resource, int amount)
    {
        if (!IsResource(resource) || amount <= 0) return amount > 0 ? amount : 0;
        int cap = StackMax(resource);
        int remaining = amount;
        bool changed = false;

        // Fill existing stacks first.
        for (int i = 0; i < NumSlots && remaining > 0; i++)
        {
            if (slots[i].id != resource) continue;
            int room = cap - slots[i].count;
            if (room <= 0) continue;
            int take = Mathf.Min(room, remaining);
            slots[i].count += take;
            remaining -= take;
            changed = true;
        }

        // Spill into empty slots.
        for (int i = 0; i < NumSlots && remaining > 0; i++)
        {
            if (slots[i].id != ItemId.None) continue;
            int take = Mathf.Min(cap, remaining);
            slots[i] = new Slot { id = resource, count = take };
            remaining -= take;
            changed = true;
        }

        if (changed) OnResourceChanged?.Invoke(resource);
        return remaining;
    }

    // All-or-nothing: drain leftmost stacks first, return false if total insufficient.
    public bool SpendResource(ItemId resource, int amount)
    {
        if (!IsResource(resource)) return false;
        if (amount <= 0) return true;
        if (GetResourceTotal(resource) < amount) return false;

        int remaining = amount;
        for (int i = 0; i < NumSlots && remaining > 0; i++)
        {
            if (slots[i].id != resource) continue;
            int take = Mathf.Min(slots[i].count, remaining);
            slots[i].count -= take;
            remaining -= take;
            if (slots[i].count <= 0) slots[i] = default;
        }
        OnResourceChanged?.Invoke(resource);
        return true;
    }

    // ── Phase 2: Fish slot helpers ───────────────────────────────────
    // Used by Bobber (catch flow), BonfireInteraction (cook stage),
    // FishMarketNPC (sell stage), and SaveCollector (old-save migration).

    // Try to place a fish in the first empty hotbar slot. Returns true on
    // success, false if every slot is occupied. Caller decides whether to
    // destroy (and pop InventoryFullPopup) or spill elsewhere.
    public bool TryAddFish(FishEntry entry)
    {
        if (entry == null) return false;
        for (int i = 0; i < NumSlots; i++)
        {
            if (slots[i].id != ItemId.None) continue;
            slots[i] = new Slot { id = ItemId.Fish, count = 1, fishData = entry };
            OnResourceChanged?.Invoke(ItemId.Fish);
            return true;
        }
        return false;
    }

    // Count fish of a given tier across the hotbar. Cook + sell tier-counter
    // UIs read this to show "Common: N" totals.
    public int CountFishByTier(string tier)
    {
        int n = 0;
        for (int i = 0; i < NumSlots; i++)
        {
            var s = slots[i];
            if (s.id == ItemId.Fish && s.fishData != null && s.fishData.fishType == tier) n++;
        }
        return n;
    }

    // Stage-add for cook/sell: find the first fish of the given tier, return
    // its FishEntry, and empty the source slot. Returns null if no match.
    // Pass tier == null or empty to take the first fish of ANY tier (used
    // by the simplified Phase 2 "Add Fish" buttons until Phase 4 brings
    // the drag-and-drop picker).
    public FishEntry TakeFirstFishOfTier(string tier)
    {
        for (int i = 0; i < NumSlots; i++)
        {
            var s = slots[i];
            if (s.id != ItemId.Fish || s.fishData == null) continue;
            if (!string.IsNullOrEmpty(tier) && s.fishData.fishType != tier) continue;
            var entry = s.fishData;
            slots[i] = default;
            OnResourceChanged?.Invoke(ItemId.Fish);
            return entry;
        }
        return null;
    }

    // ── Phase 3: Fish bag helpers ────────────────────────────────────

    // Used by Alien7Vendor.Purchase to refuse FishBag purchase when there's
    // no empty slot. Counts any non-None slot as occupied.
    public bool HasEmptyHotbarSlot()
    {
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == ItemId.None) return true;
        return false;
    }

    // Single-instance enforcement: returns true if a FishBag slot exists
    // anywhere — hotbar OR any registered LootBox's slot array. Used by
    // Alien7Vendor.IsAlreadyOwned(FishBag).
    public bool HasFishBagAnywhere()
    {
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == ItemId.FishBag) return true;
        foreach (var box in StorageRegistry.All)
        {
            if (box == null) continue;
            var s = box.Slots;
            for (int j = 0; j < s.Length; j++)
                if (s[j].id == ItemId.FishBag) return true;
        }
        return false;
    }

    // Spawn a fresh bag in the first empty hotbar slot. Returns false if
    // no empty slot — Alien7Vendor refuses the purchase upstream.
    public bool TryAddBag()
    {
        for (int i = 0; i < NumSlots; i++)
        {
            if (slots[i].id != ItemId.None) continue;
            slots[i] = new Slot
            {
                id = ItemId.FishBag,
                count = 1,
                bagContents = new Slot[5],
            };
            OnResourceChanged?.Invoke(ItemId.FishBag);
            return true;
        }
        return false;
    }

    // Try to place a fish in the equipped fish bag's first empty internal
    // slot. Returns true if placed; false if no bag is in the hotbar or
    // all 5 internal slots are full. Called BEFORE TryAddFish in Bobber's
    // catch flow so bag fills before hotbar.
    public bool TryAddFishToBag(FishEntry entry)
    {
        if (entry == null) return false;
        for (int i = 0; i < NumSlots; i++)
        {
            if (slots[i].id != ItemId.FishBag) continue;
            var bag = slots[i].bagContents;
            if (bag == null) continue;
            for (int j = 0; j < bag.Length; j++)
            {
                if (bag[j].id != ItemId.None) continue;
                bag[j] = new Slot { id = ItemId.Fish, count = 1, fishData = entry };
                OnResourceChanged?.Invoke(ItemId.Fish);
                return true;
            }
        }
        return false;
    }

    // Save-load legacy fallback only. Clears existing stacks then re-adds.
    public void SetResourceTotal(ItemId resource, int total)
    {
        if (!IsResource(resource)) return;
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == resource) slots[i] = default;
        if (total > 0) AddResource(resource, total);
        else OnResourceChanged?.Invoke(resource);
    }

    // Clears all hotbar state for a New Game. The Hotbar is a DontDestroyOnLoad
    // singleton, so without this the previous (unsaved) session's fish / wood /
    // resources / bags survive into a fresh game. Equippables (rod/axe/etc.)
    // already self-evict via DetectAcquisitions when their fresh controllers
    // report locked; this covers the select-only items that have no controller.
    public void ResetForNewGame()
    {
        for (int i = 0; i < NumSlots; i++) slots[i] = default;
        _equippedSlot = -1;
        _cycleCursor = -1;
        OnResourceChanged?.Invoke(ItemId.Wood);
        OnResourceChanged?.Invoke(ItemId.Crystal);
        OnResourceChanged?.Invoke(ItemId.SpaceDust);
    }

    // ── Save / load access ───────────────────────────────────────────
    public IReadOnlyList<Slot> GetSlotsForSave() => slots;

    // Direct mutable access to the slot array — for the storage UI's
    // drag-and-drop flow. GetSlotsForSave returns IReadOnlyList<Slot>
    // which can't be mutated; this exposes the raw array for SlotOps.
    public Slot[] RawSlotsRef() => slots;

    public void ApplySlotsFromSave(List<HotbarSlotSave> saved)
    {
        // Clear current.
        for (int i = 0; i < NumSlots; i++) slots[i] = default;
        if (saved == null) return;
        int max = Mathf.Min(saved.Count, NumSlots);
        for (int i = 0; i < max; i++)
        {
            var entry = saved[i];
            if (entry == null) continue;
            if (!System.Enum.TryParse<ItemId>(entry.itemId, out var id)) continue;
            int count = Mathf.Clamp(entry.count, 0, StackMax(id));
            if (id == ItemId.None || count <= 0) { slots[i] = default; continue; }

            FishEntry fish = null;
            Slot[] bag = null;
            if (id == ItemId.Fish)
            {
                if (entry.fishData == null) { slots[i] = default; continue; }
                fish = new FishEntry(entry.fishData.fishType, entry.fishData.weightLbs);
                fish.fishColor = entry.fishData.fishColor;
            }
            else if (id == ItemId.FishBag)
            {
                bag = SaveCollector.DeserializeBagContentsPublic(entry.bagContents);
            }
            slots[i] = new Slot { id = id, count = count, fishData = fish, bagContents = bag };
        }
        // Notify subscribers (facades) so their OnChanged fires once each.
        OnResourceChanged?.Invoke(ItemId.Wood);
        OnResourceChanged?.Invoke(ItemId.Crystal);
        OnResourceChanged?.Invoke(ItemId.SpaceDust);
    }

    static bool IsResource(ItemId id)
    {
        return id is ItemId.Wood or ItemId.Crystal or ItemId.SpaceDust;
    }

    // Slot-only items: selected via number key but have no controller to equip.
    // Covers stacking resources AND fish AND fish bags (no controller backing
    // any of them). GetEquipped/UnequipAll/ToggleSlot/CycleSlot use this to
    // skip the registry lookup for these slots.
    static bool IsSelectOnly(ItemId id) =>
        IsResource(id) || id == ItemId.Fish || id == ItemId.FishBag;

    // Procedurally-generated thin circular ring sprite used by the hold-LMB-eat
    // progress overlay. Image's Radial360 fillMethod sweeps an angular wedge of
    // the sprite's pixels; a ring shape makes the wedge look like a clock hand
    // drawing the ring stroke, which is what we want. Cached statically.
    static Sprite _progressRingSprite;
    static Sprite GetProgressRingSprite()
    {
        if (_progressRingSprite != null) return _progressRingSprite;
        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        float outerR = size * 0.48f;
        float innerR = size * 0.40f;  // ~8px ring thickness at 96px
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
            // 1-pixel anti-aliased edges on both rims.
            float a;
            if      (d < innerR - 0.5f || d > outerR + 0.5f) a = 0f;
            else if (d < innerR + 0.5f) a = Mathf.Clamp01(d - (innerR - 0.5f));
            else if (d > outerR - 0.5f) a = Mathf.Clamp01((outerR + 0.5f) - d);
            else                        a = 1f;
            pixels[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _progressRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        _progressRingSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return _progressRingSprite;
    }

    static readonly Color WoodSwatchColor    = new Color32(0xD4, 0xA0, 0x6B, 0xFF);
    static readonly Color CrystalSwatchColor = new Color32(0x8C, 0xE6, 0xFF, 0xFF);
    static readonly Color DustSwatchColor    = new Color32(0xB8, 0x8C, 0xFF, 0xFF);
    static readonly Color FishBagSwatchColor = new Color32(0x6F, 0xC0, 0x7A, 0xFF);   // muted green canvas (procedural fallback)

    // Phase 4 polish: picks the right fishbag sprite based on whether the bag
    // holds any fish. Empty bag → "fishingbag"; ≥1 fish → "fishingbagfish".
    // Resources live at Assets/Resources/HotbarIcons/ alongside the wood /
    // crystal / dust icons.
    static Sprite _bagEmptyIcon, _bagFullIcon;
    static bool _bagIconsLoaded;
    public static Sprite ResolveFishBagSprite(Hotbar.Slot[] bagContents)
    {
        if (!_bagIconsLoaded)
        {
            _bagEmptyIcon = Resources.Load<Sprite>("HotbarIcons/fishingbag");
            _bagFullIcon  = Resources.Load<Sprite>("HotbarIcons/fishingbagfish");
            _bagIconsLoaded = true;
        }
        bool hasFish = false;
        if (bagContents != null)
        {
            for (int i = 0; i < bagContents.Length; i++)
                if (bagContents[i].id != ItemId.None) { hasFish = true; break; }
        }
        return hasFish ? _bagFullIcon : _bagEmptyIcon;
    }

    static Color ResourceSwatchColor(ItemId id)
    {
        switch (id)
        {
            case ItemId.Wood:      return WoodSwatchColor;
            case ItemId.Crystal:   return CrystalSwatchColor;
            case ItemId.SpaceDust: return DustSwatchColor;
            default: return Color.white;
        }
    }

    static string ResourceDisplayName(ItemId id)
    {
        switch (id)
        {
            case ItemId.Wood:      return "WOOD";
            case ItemId.Crystal:   return "CRYSTAL";
            case ItemId.SpaceDust: return "DUST";
            default: return "—";
        }
    }

    // Resource icons live in Assets/Resources/HotbarIcons/ so they can be loaded
    // at runtime without scene/prefab wiring (the Hotbar is auto-created, no
    // inspector). Loaded once per session and cached statically.
    static Sprite _woodIcon, _crystalIcon, _dustIcon;
    static bool _iconsLoaded;

    static Sprite ResourceIcon(ItemId id)
    {
        if (!_iconsLoaded)
        {
            _woodIcon    = Resources.Load<Sprite>("HotbarIcons/TransparentWoodLog");
            _crystalIcon = Resources.Load<Sprite>("HotbarIcons/TransparentCrystalShards");
            _dustIcon    = Resources.Load<Sprite>("HotbarIcons/TransparentSpaceDust");
            _iconsLoaded = true;
        }
        switch (id)
        {
            case ItemId.Wood:      return _woodIcon;
            case ItemId.Crystal:   return _crystalIcon;
            case ItemId.SpaceDust: return _dustIcon;
            default: return null;
        }
    }

    void BuildRegistry()
    {
        _registry = new[]
        {
            new Entry { Id = ItemId.WaterBottle, DisplayName = "WATER",  Controller = water,
                        Icon = water != null ? water.hotbarIcon : null,
                        IsUnlocked   = () => water  != null && water.IsUnlocked,
                        IsEquipped   = () => water  != null && water.IsEquipped,
                        ForceEquip   = () => { if (water  != null) water.ForceEquipBottle(); },
                        ForceUnequip = () => { if (water  != null) water.ForceUnequipBottle(); } },
            new Entry { Id = ItemId.FishingRod,  DisplayName = "ROD",    Controller = rod,
                        Icon = rod != null ? rod.hotbarIcon : null,
                        IsUnlocked   = () => rod    != null && rod.IsUnlocked,
                        IsEquipped   = () => rod    != null && rod.IsEquipped,
                        ForceEquip   = () => { if (rod    != null) rod.ForceEquipRod(); },
                        ForceUnequip = () => { if (rod    != null) rod.ForceUnequipRod(); } },
            new Entry { Id = ItemId.Guitar,      DisplayName = "GUITAR", Controller = guitar,
                        Icon = guitar != null ? guitar.hotbarIcon : null,
                        IsUnlocked   = () => guitar != null && guitar.IsUnlocked,
                        IsEquipped   = () => guitar != null && guitar.IsEquipped,
                        ForceEquip   = () => { if (guitar != null) guitar.ForceEquipGuitar(); },
                        ForceUnequip = () => { if (guitar != null) guitar.ForceUnequipGuitar(); } },
            new Entry { Id = ItemId.Axe,         DisplayName = "AXE",    Controller = axe,
                        Icon = axe != null ? axe.hotbarIcon : null,
                        IsUnlocked   = () => axe    != null && axe.IsUnlocked,
                        IsEquipped   = () => axe    != null && axe.IsEquipped,
                        ForceEquip   = () => { if (axe    != null) axe.ForceEquipAxe(); },
                        ForceUnequip = () => { if (axe    != null) axe.ForceUnequipAxe(); } },
            new Entry { Id = ItemId.Pistol,      DisplayName = "PISTOL", Controller = pistol,
                        Icon = pistol != null ? pistol.hotbarIcon : null,
                        IsUnlocked   = () => pistol != null && pistol.IsUnlocked,
                        IsEquipped   = () => pistol != null && pistol.IsEquipped,
                        ForceEquip   = () => { if (pistol != null) pistol.ForceEquipPistol(); },
                        ForceUnequip = () => { if (pistol != null) pistol.ForceUnequipPistol(); } },
        };
    }

    void DetectAcquisitions()
    {
        if (_registry == null) return;
        // While the storage UI is open the player is actively rearranging
        // slots — auto-add/evict would race with their drag operations. In
        // particular, a picked-up equippable lives on the cursor (not in any
        // slot array), so without this gate DetectAcquisitions would see it
        // as "missing from hotbar" and silently duplicate it.
        if (PlayerController.isInModalSlotUI) return;
        // Add anything newly unlocked. A genuinely-new add (outside the
        // post-load settle window and not from a shop purchase) plays the
        // acquire sound — "you earned a new tool."
        bool acquireArmed = Time.time >= _acquireArmTime + AcquireArmDelay
                            && Time.frameCount > _suppressAcquireUntilFrame;
        for (int i = 0; i < _registry.Length; i++)
            if (_registry[i].IsUnlocked() && TryAddItem(_registry[i].Id) && acquireArmed)
                PlayerSuitAudio.Instance?.PlayAcquire();
        // Evict anything that's NO LONGER unlocked — the hotbar is a
        // DontDestroyOnLoad singleton, so its slots survive scene reloads.
        // Without this, loading an older save (where a pistol/guitar/etc.
        // wasn't yet acquired) leaves those items in the hotbar from the
        // previous session.
        for (int i = 0; i < NumSlots; i++)
        {
            var id = slots[i].id;
            if (id == ItemId.None) continue;
            for (int j = 0; j < _registry.Length; j++)
            {
                if (_registry[j].Id != id) continue;
                if (!_registry[j].IsUnlocked()) slots[i] = default;
                break;
            }
        }
    }

    // Returns true only when the item was newly placed into an empty slot
    // (used to drive the acquire sound).
    bool TryAddItem(ItemId id)
    {
        // Already in the hotbar — done.
        for (int i = 0; i < NumSlots; i++) if (slots[i].id == id) return false;
        // Already in some storage — leave it there (player explicitly put it
        // away). Without this check, DetectAcquisitions would auto-re-add it
        // every frame, defeating the storage system.
        if (StorageRegistry.IsItemAnywhere(id)) return false;
        // Spill into first empty hotbar slot.
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == ItemId.None) { slots[i] = new Slot { id = id, count = 1 }; return true; }
        return false;
    }

    // The slot the cycle cursor currently sits on. Tracks D-pad / number-key
    // moves independently of GetEquipped() so the player can land on an empty
    // slot (which unequips everything) and still cycle from there. -1 means
    // "no cursor yet — sync to whatever is currently equipped on first cycle".
    int _cycleCursor = -1;

    // Index of the slot the player has currently selected, regardless of whether
    // its contents is a tool (controller is equipped) or a resource (highlight only).
    // -1 = nothing selected.
    int _equippedSlot = -1;

    // Returns the ItemId of the slot the player has currently selected via 1-5
    // or D-pad cycling, regardless of whether it's a tool or a resource stack.
    // ItemId.None when no slot is selected or the selected slot is empty.
    public ItemId GetEquippedSlotId()
    {
        if (_equippedSlot < 0 || _equippedSlot >= NumSlots) return ItemId.None;
        return slots[_equippedSlot].id;
    }

    void HandleInput()
    {
        // Camera mode blocks hotbar swap entirely — the player is holding the
        // phone up like a camera and shouldn't be able to whip out the axe or
        // pistol with a number key. They have to close the camera first.
        if (PlayerPhoneUI.IsCameraMode) return;

        // Number keys 1..N for direct slot select.
        int slot = TutorialGate.HotbarSlotPressed(NumSlots);
        if (slot > 0) { ToggleSlot(slot - 1); return; }

        // D-pad left / right cycles through slots with wrap. Skips when a UI
        // Selectable is focused (handled inside HotbarCycleStep) so menu nav
        // doesn't double as hotbar nav.
        int step = TutorialGate.HotbarCycleStep();
        if (step != 0) { CycleSlot(step); return; }

        // Mouse wheel cycles the hotbar while on foot. Scroll up = previous slot
        // (toward slot 1), down = next (toward slot 7) — matches the D-pad cycle
        // and Minecraft. Skipped during build placement, where the wheel adjusts
        // the ghost's distance (GhostPlacement). HandleInput already only runs
        // when not piloting / in dialogue / phone / map / modal slot UI.
        if (!GhostPlacement.IsPlacing)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.01f) CycleSlot(-1);
            else if (wheel < -0.01f) CycleSlot(1);
        }
    }

    void CycleSlot(int step)
    {
        // Seed the cursor from whatever's currently equipped the first time
        // the player presses D-pad after equipping via number key / pickup.
        // Phase 3 fix: use _equippedSlot directly instead of scanning by id —
        // with multiple Fish slots, scanning by id picks the first match,
        // not the actually-equipped slot.
        if (_cycleCursor < 0)
        {
            if (_equippedSlot >= 0 && _equippedSlot < NumSlots && slots[_equippedSlot].id != ItemId.None)
                _cycleCursor = _equippedSlot;
        }
        int next = _cycleCursor < 0
            ? (step > 0 ? 0 : NumSlots - 1)
            : ((_cycleCursor + step) % NumSlots + NumSlots) % NumSlots;
        _cycleCursor = next;
        UnequipAll();
        var slot = slots[next];
        if (slot.id == ItemId.None) { _equippedSlot = -1; return; }
        _equippedSlot = next;
        // Select-only (resources + fish): no controller call, slot just highlights.
        if (!IsSelectOnly(slot.id)) Equip(slot.id);
        PlayerSuitAudio.Instance?.PlayEquip();
    }

    void ToggleSlot(int idx)
    {
        var slot = slots[idx];
        // Phase 3 fix: toggle-off compares slot INDEX, not id. With fish
        // (multiple slots can share id == Fish), id-based matching tripped
        // toggle-off whenever the player pressed a different fish slot,
        // resetting _equippedSlot and showing the previous cycle-cursor
        // slot as "active" instead of the one they just pressed.
        bool togglingOff = idx == _equippedSlot && slot.id != ItemId.None;
        UnequipAll();
        if (togglingOff || slot.id == ItemId.None)
        {
            _equippedSlot = -1;
            _cycleCursor = -1;
            if (togglingOff) PlayerSuitAudio.Instance?.PlayUnequip();
            return;
        }
        _cycleCursor = idx;
        _equippedSlot = idx;
        // Select-only (resources + fish + bag): no controller call.
        if (!IsSelectOnly(slot.id)) Equip(slot.id);
        PlayerSuitAudio.Instance?.PlayEquip();
    }

    ItemId GetEquipped()
    {
        // Prefer the slot-driven answer (covers resources).
        if (_equippedSlot >= 0 && _equippedSlot < NumSlots)
        {
            var sid = slots[_equippedSlot].id;
            if (sid != ItemId.None)
            {
                // Select-only (resources + fish) — slot selection IS the equip.
                // No controller registered, so don't reset _equippedSlot below.
                if (IsSelectOnly(sid)) return sid;
                // For tools, double-check the controller — dialogue/phone may have
                // force-unequipped under us. If desynced, clear the slot selection.
                if (_registry != null)
                {
                    for (int i = 0; i < _registry.Length; i++)
                        if (_registry[i].Id == sid && _registry[i].IsEquipped()) return sid;
                }
                _equippedSlot = -1;
                return ItemId.None;
            }
        }
        // Fallback: a controller may have been equipped externally (e.g.,
        // SaveCollector restored axe via ApplyEquipment). Sync _equippedSlot to it.
        if (_registry != null)
        {
            for (int i = 0; i < _registry.Length; i++)
            {
                if (!_registry[i].IsEquipped()) continue;
                for (int j = 0; j < NumSlots; j++)
                    if (slots[j].id == _registry[i].Id) { _equippedSlot = j; break; }
                return _registry[i].Id;
            }
        }
        return ItemId.None;
    }

    // Called by StorageUI.Open(). Force-unequip everything so the player
    // isn't mid-swing when the panel takes over. Same pattern as the
    // dialogue / phone open transitions.
    public void OnStorageOpened()
    {
        UnequipAll();
        _equippedSlot = -1;
    }

    void UnequipAll()
    {
        if (_registry != null)
        {
            for (int i = 0; i < _registry.Length; i++)
                if (_registry[i].IsEquipped()) _registry[i].ForceUnequip();
        }
        // Clear select-only highlight too (resources + fish) — caller sets
        // _equippedSlot if a new slot is being selected immediately after.
        if (_equippedSlot >= 0 && _equippedSlot < NumSlots && IsSelectOnly(slots[_equippedSlot].id))
            _equippedSlot = -1;
    }

    void Equip(ItemId id)
    {
        if (_registry == null || id == ItemId.None) return;
        for (int i = 0; i < _registry.Length; i++)
            if (_registry[i].Id == id) { _registry[i].ForceEquip(); return; }
    }

    string ItemName(ItemId id)
    {
        if (_registry == null) return "—";
        for (int i = 0; i < _registry.Length; i++)
            if (_registry[i].Id == id) return _registry[i].DisplayName;
        return "—";
    }

    void Refresh(bool dimmed)
    {
        ItemId equipped = GetEquipped();
        float groupAlpha = dimmed ? 0.45f : 1f;
        canvas.GetComponent<CanvasGroup>().alpha = groupAlpha;

        // When something is equipped, "active" tracks the equipped slot.
        // When nothing is equipped, "active" tracks the cycle cursor instead
        // so the player can see which empty slot they just landed on while
        // scrolling with D-pad / number keys (otherwise empty slots looked
        // identical and the player couldn't tell where the cursor was).
        for (int i = 0; i < NumSlots; i++)
        {
            var v = slotViews[i];
            ItemId id = slots[i].id;
            bool empty = id == ItemId.None;
            // Phase 3 fix: active = exact-slot match, not id match. id-based
            // matching glowed every slot with the same id (problem for fish
            // and any resource with multiple stacks).
            bool active = (_equippedSlot >= 0 && _equippedSlot < NumSlots)
                ? (i == _equippedSlot && !empty)
                : (i == _cycleCursor);

            // Icon — null sprite means empty / no icon assigned.
            // Resource slots: real PNG icon from Resources/HotbarIcons (falls
            // back to a tinted procedural swatch if the load fails).
            // Tool slots: controller's hotbarIcon. Empty: no icon.
            bool isRes = IsResource(id);
            bool isFish = id == ItemId.Fish;
            bool isFishBag = id == ItemId.FishBag;
            Sprite sprite = null;
            Color iconTint = new Color32(0xF1, 0xF4, 0xFF, 0xC0);
            bool isProceduralSwatch = false;
            if (!empty)
            {
                if (isFish)
                {
                    // Fish slots use a live RenderTexture via RawImage instead
                    // of the sprite path. The sprite stays null so the standard
                    // itemIcon Image is disabled below.
                }
                else if (isFishBag)
                {
                    // Phase 4: real fishbag art. ResolveFishBagSprite picks
                    // between empty + fish-in-bag variants based on the
                    // bag's bagContents. Falls back to the green procedural
                    // swatch if either Resource is missing.
                    sprite = ResolveFishBagSprite(slots[i].bagContents);
                    if (sprite == null)
                    {
                        sprite = HotbarResourceSwatch.GetSprite();
                        iconTint = FishBagSwatchColor;
                        isProceduralSwatch = true;
                    }
                }
                else if (isRes)
                {
                    sprite = ResourceIcon(id);
                    if (sprite == null)
                    {
                        // Fallback: keep the original colored-square placeholder
                        // so the slot isn't blank if the PNG is missing.
                        sprite = HotbarResourceSwatch.GetSprite();
                        iconTint = ResourceSwatchColor(id);
                        isProceduralSwatch = true;
                    }
                }
                else if (_registry != null)
                {
                    for (int r = 0; r < _registry.Length; r++)
                        if (_registry[r].Id == id) { sprite = _registry[r].Icon; break; }
                }
            }
            v.itemIcon.sprite = sprite;
            v.itemIcon.enabled = sprite != null && !isFish;

            // Phase 2: paint the fish preview RawImage for Fish slots. Render
            // via the dex's preview camera if we haven't yet for this entry.
            if (v.fishPreview != null)
            {
                bool fishVisible = isFish && !empty && slots[i].fishData != null;
                if (fishVisible)
                {
                    var fe = slots[i].fishData;
                    if (fe.cachedHotbarPreview == null && FishingdexManager.Instance != null)
                    {
                        fe.cachedHotbarPreview = FishingdexManager.Instance.RenderFish(fe, 64, 64);
                    }
                    v.fishPreview.texture = fe.cachedHotbarPreview;
                    v.fishPreview.enabled = fe.cachedHotbarPreview != null;
                }
                else if (v.fishPreview.enabled)
                {
                    v.fishPreview.enabled = false;
                    v.fishPreview.texture = null;
                }
            }

            // Per-icon scale tweak — some art reads larger or smaller at the
            // default slot size and needs a render-time correction.
            float iconScale = 1f;
            if (!isProceduralSwatch)
            {
                if (id == ItemId.Crystal) iconScale = 1.385f; // (1.8 / 1.3)
                else if (id == ItemId.Pistol) iconScale = 1.3f;
                else if (id == ItemId.FishBag) iconScale = 1.3f;
            }
            var iconRT = v.itemIcon.rectTransform;
            if (iconRT != null && !Mathf.Approximately(iconRT.localScale.x, iconScale))
                iconRT.localScale = new Vector3(iconScale, iconScale, 1f);

            // Stack count text — resource only.
            if (v.countText != null)
            {
                if (isRes && !empty)
                {
                    string countStr = slots[i].count.ToString();
                    if (v.countText.text != countStr) v.countText.text = countStr;
                    v.countText.enabled = true;
                }
                else if (v.countText.enabled)
                {
                    v.countText.enabled = false;
                }
            }

            // Dim non-active filled slots; lighter dim on empty slots.
            if (active)
            {
                v.itemIcon.color = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
                v.background.color = new Color32(0x14, 0x28, 0x44, 0xF8);
            }
            else
            {
                v.itemIcon.color = empty
                    ? new Color32(0xF1, 0xF4, 0xFF, 0x00)
                    : new Color32(0xF1, 0xF4, 0xFF, 0x80);
                v.background.color = empty
                    ? new Color32(0x05, 0x03, 0x12, 0xC0)
                    : GalaxyHudKit.SlotColor;
            }

            // Real PNG resource icons render with their own colours (white tint
            // with active-state alpha). Procedural fallback swatches need the
            // resource colour applied as a tint.
            if (isRes && !empty)
            {
                Color c = isProceduralSwatch ? iconTint : Color.white;
                c.a = active ? 1f : 0.85f;
                v.itemIcon.color = c;
            }

            v.glow.gameObject.SetActive(active);
            v.glow.color = active
                ? new Color32(0x5C, 0xC8, 0xFF, 0xD0)
                : GalaxyHudKit.GlowColor;
            v.accent.color = new Color(1f, 1f, 1f, active ? 0.9f : 0.35f);
        }

        // Slot lift/scale animation — only fire on active-index change.
        int newActive = -1;
        for (int i = 0; i < NumSlots; i++)
        {
            ItemId id = slots[i].id;
            bool empty = id == ItemId.None;
            bool active = (_equippedSlot >= 0 && _equippedSlot < NumSlots)
                ? (i == _equippedSlot && !empty)
                : (i == _cycleCursor);
            if (active) newActive = i;
        }
        if (newActive != _animatedActiveIdx)
        {
            if (_animatedActiveIdx >= 0 && _animatedActiveIdx < NumSlots)
            {
                if (_slotAnimRoutines[_animatedActiveIdx] != null) StopCoroutine(_slotAnimRoutines[_animatedActiveIdx]);
                _slotAnimRoutines[_animatedActiveIdx] = StartCoroutine(AnimateSlotState(_animatedActiveIdx, false));
            }
            if (newActive >= 0)
            {
                if (_slotAnimRoutines[newActive] != null) StopCoroutine(_slotAnimRoutines[newActive]);
                _slotAnimRoutines[newActive] = StartCoroutine(AnimateSlotState(newActive, true));
            }
            _animatedActiveIdx = newActive;
        }

        // Name plate — show only when an active filled slot exists.
        ItemId activeId = (newActive >= 0) ? slots[newActive].id : ItemId.None;
        bool plateShown = activeId != ItemId.None;
        if (plateShown && _namePlateRT != null)
        {
            string label;
            if (activeId == ItemId.Fish && slots[newActive].fishData != null)
            {
                // "COMMON FISH" / "UNCOMMON FISH" / "RARE FISH" + weight.
                label = $"{slots[newActive].fishData.fishType.ToUpper()} FISH · {slots[newActive].fishData.weightLbs} LB";
            }
            else if (activeId == ItemId.FishBag)
            {
                int filled = 0;
                var bag = slots[newActive].bagContents;
                if (bag != null) for (int b = 0; b < bag.Length; b++) if (bag[b].id != ItemId.None) filled++;
                label = $"FISH BAG · {filled}/5";
            }
            else if (IsResource(activeId))
            {
                label = $"{ResourceDisplayName(activeId)} ×{slots[newActive].count}";
            }
            else
            {
                label = ItemName(activeId);
            }
            if (_namePlateText.text != label) _namePlateText.text = label;
            float slotX = slotViews[newActive].root.anchoredPosition.x;
            float barWidth = ((RectTransform)_namePlateRT.parent).sizeDelta.x;
            var p = _namePlateRT.anchoredPosition;
            p.x = barWidth * 0.5f + slotX;
            _namePlateRT.anchoredPosition = p;
        }
        if (_namePlateGroup != null)
        {
            float target = plateShown ? 1f : 0f;
            _namePlateGroup.alpha = Mathf.MoveTowards(_namePlateGroup.alpha, target, Time.unscaledDeltaTime * 8f);
        }

        // Phase 2: center-screen hold-LMB-eat progress ring. Shown when the
        // player is actively holding LMB on an equipped Fish slot; ring sweep
        // visualizes the 0->1s window before consumption fires.
        if (_centerProgressRing != null)
        {
            bool ringActive = _eatProgressSlot >= 0;
            _centerProgressRing.enabled = ringActive;
            _centerProgressRing.fillAmount = ringActive
                ? Mathf.Clamp01(_eatHeldSeconds / EatHoldDuration)
                : 0f;
        }
    }

    IEnumerator AnimateSlotState(int idx, bool active)
    {
        var v = slotViews[idx];
        if (v == null) yield break;
        float dur = 0.12f;
        float t = 0f;
        Vector2 fromSize = v.root.sizeDelta;
        Vector2 toSize = active ? new Vector2(ActiveSize, ActiveSize) : new Vector2(SlotSize, SlotSize);
        Vector2 fromPos = v.root.anchoredPosition;
        // Existing baseline y is +16 (per BuildSlot); active adds ActiveLift on top of that.
        float baselineY = 16f;
        Vector2 toPos = new Vector2(fromPos.x, active ? baselineY + ActiveLift : baselineY);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = 1f - Mathf.Pow(1f - u, 3f);
            v.root.sizeDelta = Vector2.Lerp(fromSize, toSize, k);
            v.root.anchoredPosition = Vector2.Lerp(fromPos, toPos, k);
            yield return null;
        }
        v.root.sizeDelta = toSize;
        v.root.anchoredPosition = toPos;
        _slotAnimRoutines[idx] = null;
    }

    IEnumerator BorderPulse()
    {
        while (this != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.4f) + 1f) * 0.5f;
            Color pulse = Color.Lerp(GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot, t);
            ItemId equipped = GetEquipped();
            for (int i = 0; i < slotViews.Length; i++)
            {
                if (slotViews[i] == null || slotViews[i].border == null) continue;
                bool active = (_equippedSlot >= 0 && _equippedSlot < NumSlots)
                    ? (i == _equippedSlot && slots[i].id != ItemId.None)
                    : (i == _cycleCursor);
                slotViews[i].border.color = active
                    ? pulse
                    : new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.35f);
            }
            yield return null;
        }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("HotbarCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 830; // above LetterboxBars (820) — stays visible during dialogue / cook UI
        HUDSceneGate.Register(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasGo.AddComponent<CanvasGroup>();

        float totalWidth = NumSlots * SlotSize + (NumSlots - 1) * SlotSpacing;
        var bar = NewRT("HotbarRoot", canvasGo.transform);
        HudVisibility.RegisterHideable(bar.gameObject.AddComponent<CanvasGroup>());   // hide for HIDE HUD / pod, independent of the dim group on the canvas
        bar.anchorMin = new Vector2(0.5f, 0f);
        bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        // Sized to sit between the helmet's corner pods: 1.2× smaller than
        // the slots' native layout, raised off the bottom span art. Scale
        // (not layout constants) so the slot-grow animations stay untouched.
        bar.anchoredPosition = new Vector2(0f, BottomMargin + 76f);
        bar.sizeDelta = new Vector2(totalWidth + 32f, ActiveSize + ActiveLift + 32f);
        bar.localScale = new Vector3(1f / 1.2f, 1f / 1.2f, 1f);

        for (int i = 0; i < NumSlots; i++)
        {
            slotViews[i] = BuildSlot(bar, i, totalWidth);
        }
        BuildNamePlate(bar);
        BuildCenterProgressRing(canvasGo.transform);
    }

    // Phase 2: hold-LMB-eat indicator at screen center. Lives on the Hotbar
    // canvas (parented to canvasGo, not the hotbar bar) so it floats at the
    // crosshair instead of on the slot icon. Disabled until _eatProgressSlot
    // becomes non-negative.
    void BuildCenterProgressRing(Transform canvasRoot)
    {
        var ringRT = NewRT("__CenterProgressRing", canvasRoot);
        ringRT.anchorMin = new Vector2(0.5f, 0.5f);
        ringRT.anchorMax = new Vector2(0.5f, 0.5f);
        ringRT.pivot = new Vector2(0.5f, 0.5f);
        ringRT.anchoredPosition = Vector2.zero;
        ringRT.sizeDelta = new Vector2(56f, 56f);   // ring just outside the crosshair dot
        _centerProgressRing = ringRT.gameObject.AddComponent<Image>();
        _centerProgressRing.sprite = GetProgressRingSprite();
        _centerProgressRing.type = Image.Type.Filled;
        _centerProgressRing.fillMethod = Image.FillMethod.Radial360;
        _centerProgressRing.fillOrigin = (int)Image.Origin360.Top;
        _centerProgressRing.fillClockwise = true;
        _centerProgressRing.fillAmount = 0f;
        _centerProgressRing.color = new Color32(0x6F, 0xE9, 0xFF, 0xFF);
        _centerProgressRing.raycastTarget = false;
        _centerProgressRing.enabled = false;
    }

    // Name plate: plain glowing cyan text, no panel background. Matches mockup B.
    void BuildNamePlate(RectTransform parent)
    {
        var rt = NewRT("__NamePlate", parent);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        // Plate sits ABOVE the active slot. Active slot anchored y = 16 (baseline) + ActiveLift,
        // height = ActiveSize, so slot top = 16 + ActiveLift + ActiveSize. Add gap above that.
        rt.anchoredPosition = new Vector2(0f, 16f + ActiveLift + ActiveSize + 14f);
        rt.sizeDelta = new Vector2(140f, 20f);
        _namePlateRT = rt;

        _namePlateGroup = rt.gameObject.AddComponent<CanvasGroup>();
        _namePlateGroup.alpha = 0f;

        _namePlateText = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_namePlateText);
        _namePlateText.text = "";
        _namePlateText.fontSize = 12f;
        _namePlateText.fontStyle = FontStyles.Bold;
        _namePlateText.alignment = TextAlignmentOptions.Center;
        _namePlateText.characterSpacing = 3f;
        _namePlateText.color = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
        _namePlateText.enableWordWrapping = false;
        _namePlateText.raycastTarget = false;

        // Soft cyan glow + crisp dark drop shadow for legibility on any background.
        var glow = rt.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(0.36f, 0.78f, 1f, 0.85f);
        glow.effectDistance = new Vector2(0f, 0f);
        var drop = rt.gameObject.AddComponent<Shadow>();
        drop.effectColor = new Color(0f, 0f, 0f, 0.85f);
        drop.effectDistance = new Vector2(0f, -2f);
    }

    SlotVisuals BuildSlot(RectTransform parent, int index, float totalWidth)
    {
        var v = new SlotVisuals();

        float x = -totalWidth * 0.5f + index * (SlotSize + SlotSpacing) + SlotSize * 0.5f;

        var slotRT = NewRT("Slot" + (index + 1), parent);
        slotRT.anchorMin = new Vector2(0.5f, 0f);
        slotRT.anchorMax = new Vector2(0.5f, 0f);
        slotRT.pivot = new Vector2(0.5f, 0f);
        slotRT.anchoredPosition = new Vector2(x, 16f);
        slotRT.sizeDelta = new Vector2(SlotSize, SlotSize);
        v.root = slotRT;

        var glowRT = NewRT("__Glow", slotRT);
        // Compact rounded-square halo extending ~8 px outside the slot.
        Stretch(glowRT, -8f, -8f, 8f, 8f);
        v.glow = glowRT.gameObject.AddComponent<Image>();
        v.glow.sprite = HotbarHaloGlow.GetSprite();
        v.glow.type = Image.Type.Sliced;
        v.glow.color = new Color32(0x5C, 0xC8, 0xFF, 0x90);
        v.glow.raycastTarget = false;

        var bgRT = NewRT("__Background", slotRT);
        Stretch(bgRT, 0f, 0f, 0f, 0f);
        v.background = bgRT.gameObject.AddComponent<Image>();
        v.background.sprite = GalaxyHudKit.SlotSprite();
        v.background.type = Image.Type.Sliced;
        v.background.color = GalaxyHudKit.SlotColor;
        v.background.raycastTarget = false;

        var nebulaRT = NewRT("__Nebula", slotRT);
        Stretch(nebulaRT, 4f, 4f, -4f, -4f);
        var nebula = nebulaRT.gameObject.AddComponent<Image>();
        nebula.sprite = GalaxyHudKit.NebulaSprite();
        nebula.type = Image.Type.Sliced;
        nebula.color = new Color(1f, 1f, 1f, 0.18f);
        nebula.raycastTarget = false;

        var borderRT = NewRT("__Border", slotRT);
        Stretch(borderRT, 0f, 0f, 0f, 0f);
        v.border = borderRT.gameObject.AddComponent<Image>();
        // Ring (transparent center) — GalaxyHudKit.RoundedSprite is filled, which
        // would cover the slot fill at any meaningful alpha. The ring lets the
        // dark navy fill + icon read through unobstructed.
        v.border.sprite = HotbarRoundedRing.GetSprite();
        v.border.type = Image.Type.Sliced;
        v.border.color = GalaxyHudKit.BorderCool;
        v.border.raycastTarget = false;

        var accentRT = NewRT("__Accent", slotRT);
        accentRT.anchorMin = new Vector2(0f, 1f);
        accentRT.anchorMax = new Vector2(1f, 1f);
        accentRT.pivot = new Vector2(0.5f, 1f);
        accentRT.anchoredPosition = new Vector2(0f, -5f);
        accentRT.sizeDelta = new Vector2(-22f, 2f);
        v.accent = accentRT.gameObject.AddComponent<Image>();
        v.accent.sprite = GalaxyHudKit.AccentSprite();
        v.accent.color = new Color(1f, 1f, 1f, 0.35f);
        v.accent.raycastTarget = false;

        var iconRT = NewRT("__ItemIcon", slotRT);
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = new Vector2(40f, 40f);
        v.itemIcon = iconRT.gameObject.AddComponent<Image>();
        v.itemIcon.preserveAspect = true;
        v.itemIcon.raycastTarget = false;
        v.itemIcon.color = new Color32(0xF1, 0xF4, 0xFF, 0xC0);

        // Phase 2: live fish preview via FishingdexManager.RenderFish. RawImage
        // is required because the source is a RenderTexture; cached per
        // FishEntry so we render once and reuse. Sits at the same anchor /
        // size as itemIcon and is toggled instead of it when slot.id == Fish.
        var fpRT = NewRT("__FishPreview", slotRT);
        fpRT.anchorMin = new Vector2(0.5f, 0.5f);
        fpRT.anchorMax = new Vector2(0.5f, 0.5f);
        fpRT.pivot = new Vector2(0.5f, 0.5f);
        fpRT.anchoredPosition = Vector2.zero;
        fpRT.sizeDelta = new Vector2(48f, 48f);
        v.fishPreview = fpRT.gameObject.AddComponent<RawImage>();
        v.fishPreview.raycastTarget = false;
        v.fishPreview.enabled = false;

        // Stack count overlay (resource slots only). Disabled by default; Refresh() toggles.
        var countRT = NewRT("__Count", slotRT);
        countRT.anchorMin = new Vector2(1f, 0f);
        countRT.anchorMax = new Vector2(1f, 0f);
        countRT.pivot = new Vector2(1f, 0f);
        countRT.anchoredPosition = new Vector2(-6f, 4f);
        countRT.sizeDelta = new Vector2(40f, 16f);
        v.countText = countRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(v.countText);
        v.countText.text = "";
        v.countText.fontSize = 14f;
        v.countText.fontStyle = FontStyles.Bold;
        v.countText.alignment = TextAlignmentOptions.BottomRight;
        v.countText.color = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        v.countText.raycastTarget = false;
        v.countText.enabled = false;
        var countDrop = countRT.gameObject.AddComponent<Shadow>();
        countDrop.effectColor = new Color(0f, 0f, 0f, 0.9f);
        countDrop.effectDistance = new Vector2(0f, -1.5f);

        v.glow.gameObject.SetActive(false);
        return v;
    }

    static RectTransform NewRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }
}

// Halo glow with a flat-bright inner zone and a soft fade only in the outer
// corner band. The shared GalaxyHudKit.GlowSprite uses Pow(1-d, 2.6) which
// concentrates all alpha in the dead centre — invisible behind the slot
// background. This profile keeps the slot-edge zone fully opaque, so the
// visible halo around the slot reads loud.
static class HotbarHaloGlow
{
    static Sprite _glow;

    public static Sprite GetSprite()
    {
        if (_glow != null) return _glow;
        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        // Squircle distance (Lp norm with p=2.5): d = (|x|^p + |y|^p)^(1/p).
        // p=2.5 sits between a circle (p=2) and a square (p=∞), heavily
        // weighted toward circular — corners hit d≈1.32 while edges hit
        // d=1.0, so the alpha threshold cuts the corners off much earlier
        // than the edges. Gives a visibly rounded halo, not a square one
        // with faded corners.
        const float coreSize = 0.88f;
        const float fadeRange = 0.25f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dxAbs = Mathf.Abs(x - cx) / cx;
                float dyAbs = Mathf.Abs(y - cy) / cy;
                float dxP = Mathf.Pow(dxAbs, 2.5f);
                float dyP = Mathf.Pow(dyAbs, 2.5f);
                float d = Mathf.Pow(dxP + dyP, 1f / 2.5f);
                float a;
                if (d <= coreSize)
                {
                    a = 1f;
                }
                else
                {
                    float t = Mathf.Clamp01((d - coreSize) / fadeRange);
                    a = Mathf.Pow(1f - t, 1.6f);
                }
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _glow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                              100f, 0u, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
        _glow.name = "HotbarHaloGlow";
        return _glow;
    }
}

// Hollow rounded-rect ring used for the hotbar slot border. The shared
// GalaxyHudKit.RoundedSprite is a *filled* rounded rect, which would cover
// the dark slot fill at any meaningful alpha — this ring leaves the centre
// transparent so the fill + icon read through.
static class HotbarRoundedRing
{
    static Sprite _ring;

    public static Sprite GetSprite()
    {
        if (_ring != null) return _ring;
        var tex = MakeRing(64, 18, 2);
        _ring = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                              100f, 0u, SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
        _ring.name = "HotbarRoundedRing";
        return _ring;
    }

    static Texture2D MakeRing(int size, int radius, int thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        int innerSize = size - 2 * thickness;
        int innerRadius = Mathf.Max(0, radius - thickness);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float outerA = RoundedAlpha(x, y, size, radius);
                int ix = x - thickness;
                int iy = y - thickness;
                float innerA = 0f;
                if (ix >= 0 && iy >= 0 && ix < innerSize && iy < innerSize)
                    innerA = RoundedAlpha(ix, iy, innerSize, innerRadius);
                float a = Mathf.Clamp01(outerA - innerA);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static float RoundedAlpha(int x, int y, int size, int radius)
    {
        int dx = 0, dy = 0;
        if (x < radius) dx = radius - x;
        else if (x >= size - radius) dx = x - (size - radius - 1);
        if (y < radius) dy = radius - y;
        else if (y >= size - radius) dy = y - (size - radius - 1);
        if (dx <= 0 || dy <= 0) return 1f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(radius - d + 0.5f);
    }
}

// Procedural colored rounded-corner swatch used as a placeholder icon for
// resource stacks (wood/crystal/dust). One sprite shared, color applied via
// Image.color tint. Replace with real textures later.
static class HotbarResourceSwatch
{
    static Sprite _swatch;

    public static Sprite GetSprite()
    {
        if (_swatch != null) return _swatch;
        const int size = 48, radius = 10;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color(1f, 1f, 1f, RoundedAlpha(x, y, size, radius));
        tex.SetPixels(pixels);
        tex.Apply();
        _swatch = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                100f, 0u, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
        _swatch.name = "HotbarResourceSwatch";
        return _swatch;
    }

    static float RoundedAlpha(int x, int y, int size, int radius)
    {
        int dx = 0, dy = 0;
        if (x < radius) dx = radius - x;
        else if (x >= size - radius) dx = x - (size - radius - 1);
        if (y < radius) dy = radius - y;
        else if (y >= size - radius) dy = y - (size - radius - 1);
        if (dx <= 0 || dy <= 0) return 1f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(radius - d + 0.5f);
    }
}
