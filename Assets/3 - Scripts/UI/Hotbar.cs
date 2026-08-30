using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Hotbar : MonoBehaviour
{
    // Append-only (JsonUtility serializes enums by name in the hotbar save, but
    // keep new values at the END so nothing shifts).
    // APPEND ONLY. Slots persist as the enum's NAME (SaveData stores a string and
    // parses it back), so reordering wouldn't corrupt saves — but ItemId is
    // serialized by VALUE on scene/prefab components, so inserting mid-enum
    // silently rewires those. New ids go on the end.
    public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust, Fish, FishBag, Sapling, Mushroom, MushroomSapling, Money, BlankTapeT1, BlankTapeT2, Cassette, BlankTapeHalfT1, BlankTapeHalfT2, BlankTapeFullT1, BlankTapeFullT2, TraxUsbStick }

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
        // Populated only when id == Mushroom / MushroomSapling. The species KEY
        // (the source prefab's name — see MushroomRegistry). Stacks are
        // SPECIES-PURE: two different species never merge into one stack, so
        // every add/spend path has to match on this as well as the id.
        public string mushroomSpecies;
        // Populated only when id == Cassette. The PRINT id — see TraxPrints.
        // Like species, stacks are PURE: two different songs never merge, so
        // every add/spend path matches on this as well as the id.
        public string cassetteId;
    }

    /// True for the two species-carrying item ids. Their stacks match on
    /// <see cref="Slot.mushroomSpecies"/> as well as the id.
    public static bool IsMushroomItem(ItemId id) =>
        id == ItemId.Mushroom || id == ItemId.MushroomSapling;

    /// <summary>
    /// True for every id whose stacks are keyed on more than the id itself.
    /// Mushrooms carry a species, cassettes carry a song. Both need the same
    /// "these two stacks are not interchangeable" rule, so the add/spend paths
    /// below talk about a VARIANT rather than about mushrooms specifically.
    /// </summary>
    public static bool CarriesVariant(ItemId id) => IsMushroomItem(id) || id == ItemId.Cassette;

    /// The variant a slot is carrying, or null if its id does not use one.
    public static string VariantOf(Slot s) =>
        s.id == ItemId.Cassette ? s.cassetteId : IsMushroomItem(s.id) ? s.mushroomSpecies : null;

    static Slot MakeSlot(ItemId id, int count, string variant)
    {
        var slot = new Slot { id = id, count = count };
        if (id == ItemId.Cassette) slot.cassetteId = variant;
        else if (IsMushroomItem(id)) slot.mushroomSpecies = variant;
        return slot;
    }

    // ── Slot layout ────────────────────────────────────────────────────────
    //
    // NumSlots is the ITEM range. Every generic loop in this file — AddResource,
    // SpendResource, TryAddFish, DetectAcquisitions, the equippable registry —
    // is bounded by it and therefore can never see, fill, or drain the money
    // slot. That's the whole reason money is index 7 and NOT part of NumSlots:
    // one stray `for (i < NumSlots)` that reached the money slot would let the
    // build system spend your cash as if it were wood.
    //
    // TotalSlots is what the player SEES and can select: seven item slots plus
    // the money slot. Rendering, hotkeys, cycling and the drag/drop layer use
    // this bound.
    const int NumSlots = 7;
    /// The money slot. Holds ItemId.Money and nothing else, and no other slot
    /// may hold money — see <see cref="SlotAccepts"/>. Its count IS the
    /// player's balance; PlayerWallet is a thin view over it.
    public const int MoneySlotIndex = 7;
    /// Item slots + the money slot. Array length, UI cell count, select range.
    public const int TotalSlots = NumSlots + 1;
    const float SlotSize = 64f;
    const float ActiveSize = 80f;       // size when slot is the equipped/cursor active slot

    // E2 scan-sweep dressing.
    //
    // EVERY slot carries brackets and a scanline — that's what makes an empty
    // slot visible at all now the boxes are gone. Selection is shown by the
    // brackets GROWING and the sweep speeding up, rather than by being the only
    // slot that has them.
    const float BracketIdleSize   = 9f;     // L-arm length on an unselected slot
    const float BracketActiveSize = 12f;    // …and on the selected one
    const float BracketIdleOut    = 2f;     // how far the corner sits outside the slot
    const float BracketActiveOut  = 3f;
    const float BracketGrowSpeed  = 26f;    // px/sec — fast enough to feel snappy, slow enough to read

    const float SweepHeight       = 15f;    // thickness of the travelling scanline
    const float SweepOverhang     = 6f;     // how far it spills past the slot's sides
    const float SweepPeriod       = 1.4f;   // selected slot: one pass, on a tight loop
    // Unselected passes look IDENTICAL to the selected one — same duration, same
    // colour. What separates them is how OFTEN: the selected slot loops
    // continuously, the rest get one pass at a random gap so the bar never
    // pulses in unison.
    const float IdleSweepDuration = SweepPeriod;
    const float IdleSweepGapMin   = 4f;
    const float IdleSweepGapMax   = 13f;
    const float IdleSweepDimFactor = 0.5f;   // unselected passes are half as bright

    // Wake-brightening, matching HudIdleSweep on the vitals / boost / compass
    // clusters: a slot decays toward SlotDimFloor, and its scanline wipes it
    // back to full as it passes. The selected slot sweeps constantly, so it
    // simply never gets a chance to dim.
    // NOTE this multiplies BaselineAlpha, it doesn't replace it — an unselected
    // item ends up at 0.55 × floor. At floor 0.48 that was 0.26, faint enough
    // that reading your own bar got hard. 0.55 lands them at ~0.30 at rest and
    // ~0.55 straight after a pass: a 1.8× swing, still obvious, still legible.
    const float SlotDimFloor      = 0.55f;
    // Idle passes are 4–13 s apart, so the fade is stretched to fill most of
    // that wait. At 0.9 s the slot snapped dark almost immediately and the
    // brighten-then-settle the sweep is supposed to produce wasn't readable.
    const float SlotDecayTime     = 3.2f;

    const float BaselineAlpha     = 0.55f;  // dim applied to every non-active item

    // Cyan backing glow, filled slots only. Rides the same per-slot brightness
    // as everything else, so it swells with each scanline pass and settles back.
    // NEGATIVE spread = the glow sits INSIDE the slot, roughly icon-sized, so it
    // reads as light coming off the item rather than a panel behind it. At +10
    // its footprint was 84 px on a 64 px slot; -11 halves that to 42.
    const float GlowSpread        = -11f;
    const float GlowAlphaIdle     = 0.34f;
    const float GlowAlphaActive   = 0.62f;
    const float IndexAlphaIdle    = 0.42f;
    const float IndexAlphaActive  = 0.95f;
    const float ActiveLift = 8f;        // pixels lifted above the row when active
    // Widened twice on 2026-08-06. The helmet frame art is vaulted now, so the
    // corner clusters moved out to the screen edges and the bar has room.
    const float SlotSpacing = 44f;
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

    readonly Slot[] slots = new Slot[TotalSlots];

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
    Coroutine[] _slotAnimRoutines = new Coroutine[TotalSlots];

    // Phase 2: hold-LMB-eat state. _eatProgressSlot is the slot index the
    // player is currently holding LMB on (must be the equipped Fish slot).
    // _eatHeldSeconds counts up while held; consumption fires at EatHoldDuration.
    int _eatProgressSlot = -1;
    float _eatHeldSeconds = 0f;
    const float EatHoldDuration = 1.0f;
    /// Winding up a tape is TWICE as fast as eating. Eating is a deliberate
    /// commitment (it consumes the thing); pressing play is not, and at the
    /// food speed it felt like the tape was refusing to start.
    const float TapeHoldDuration = 0.5f;
    /// Which duration the CURRENT hold is running to. The progress ring reads
    /// this rather than a constant, or a tape's ring would only ever fill half
    /// way before firing.
    float _holdDuration = EatHoldDuration;
    bool _holdIsTape;

    /// True while the player is holding fire on a selected raw fish.
    /// HeldItemViewmodel reads this to raise the fish to the mouth and run the
    /// chewing loop for exactly as long as the progress ring is filling.
    /// Food only — HeldItemViewmodel uses this to raise the item to the mouth
    /// and run the chewing loop, which a cassette should very much not do.
    public bool IsEatingHeldItem => _eatProgressSlot >= 0 && !_holdIsTape;
    /// 0..1 fill of the eat progress ring — the same value the ring renders.
    public float EatProgress01 =>
        _eatProgressSlot < 0 ? 0f : Mathf.Clamp01(_eatHeldSeconds / _holdDuration);

    Canvas canvas;
    CanvasGroup _canvasGroup;   // cached at build time; Refresh() ran GetComponent every frame otherwise
    SlotVisuals[] slotViews = new SlotVisuals[TotalSlots];

    RectTransform _namePlateRT;
    Image _namePlateBg;
    Image _namePlateBorder;
    TextMeshProUGUI _namePlateText;
    CanvasGroup _namePlateGroup;

    class SlotVisuals
    {
        public RectTransform root;
        // E2 "scan sweep" (2026-08-06). The hotbar was the last surface still
        // using the rounded / nebula / glow language; everything else moved to
        // the flat cyan scanner look. There is no slot box any more: items float,
        // inactive ones are dimmed, and the ACTIVE one gets corner brackets plus
        // a scanline that sweeps down through it. The old glow / border /
        // background / accent images are gone rather than hidden.
        public Image[] brackets;    // 4 corners, on EVERY slot; grow when selected
        public Image sweep;         // the scanline, on every slot
        public Image glow;          // soft cyan backing — FILLED slots only
        public TextMeshProUGUI indexText;   // 1-7, top-left
        public float bracketSize;   // current arm length, eased toward its target
        public float idleSweepStart;// unscaled time this idle pass began; <0 = idle
        public float idleSweepNext; // unscaled time the next idle pass is due
        public float brightness;    // 1 = just scanned, SlotDimFloor = fully decayed
        public float dimAtSweepStart;
        public bool  sweepWasOn;
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
        bool fishEquipped = eq >= 0 && eq < TotalSlots
                         && slots[eq].id == ItemId.Fish
                         && slots[eq].fishData != null;
        // Handoff §3: eating a mushroom is a HELD-ITEM action now, not an
        // interact on the world prop. Same hold-fire ring the raw fish uses.
        bool mushroomEquipped = eq >= 0 && eq < TotalSlots
                             && slots[eq].id == ItemId.Mushroom
                             && slots[eq].count > 0;
        // A printed tape uses the same wind-up ring, but it PLAYS rather than
        // being eaten — nothing is consumed, and holding again stops it.
        bool tapeEquipped = eq >= 0 && eq < TotalSlots
                          && slots[eq].id == ItemId.Cassette
                          && slots[eq].count > 0
                          && !string.IsNullOrEmpty(slots[eq].cassetteId);

        if ((!fishEquipped && !mushroomEquipped && !tapeEquipped) || !TutorialGate.FireHeld())
        {
            if (_eatProgressSlot != -1) { _eatProgressSlot = -1; _eatHeldSeconds = 0f; }
            return;
        }

        if (_eatProgressSlot != eq)
        {
            _eatProgressSlot = eq;
            _eatHeldSeconds = 0f;
            _holdIsTape = tapeEquipped;
            _holdDuration = tapeEquipped ? TapeHoldDuration : EatHoldDuration;
        }
        _eatHeldSeconds += Time.deltaTime;

        if (_eatHeldSeconds >= _holdDuration)
        {
            if (tapeEquipped) ToggleEquippedTape();
            else if (fishEquipped) ConsumeEquippedFish();
            else ConsumeEquippedMushroom();
            _eatProgressSlot = -1;
            _eatHeldSeconds = 0f;
        }
    }

    /// <summary>
    /// Press play on the tape in hand, or stop it if it is already the one
    /// playing. Nothing is consumed — a cassette is reusable, which is the
    /// whole difference between this and eating.
    ///
    /// Playback deliberately CONTINUES when you put the tape away: it is a
    /// walkman, not a held-button. Re-select the tape and hold again to stop.
    /// </summary>
    void ToggleEquippedTape()
    {
        int eq = _equippedSlot;
        if (eq < 0 || eq >= TotalSlots) return;
        string printId = slots[eq].cassetteId;
        if (string.IsNullOrEmpty(printId)) return;

        // No transform to follow: the walkman is 2D, so where the emitter sits
        // is irrelevant. Only the world-positioned case (an alien auditioning a
        // tape in front of you) needs one.
        bool started = TraxTapePlayer.TogglePersonal(null, printId);
        string song = TraxPrints.DisplayName(printId).ToUpperInvariant();
        Debug.Log(started ? "[Tape] PLAYING " + song : "[Tape] STOPPED " + song);
    }

    void ConsumeEquippedMushroom()
    {
        int eq = _equippedSlot;
        if (eq < 0 || eq >= TotalSlots) return;
        var slot = slots[eq];
        if (slot.id != ItemId.Mushroom || slot.count <= 0) return;

        MushroomEffect.Consume(slot.mushroomSpecies);
        PlayerSuitAudio.Instance?.PlayBurpAfterDelay();

        slots[eq].count--;
        if (slots[eq].count <= 0) slots[eq] = default;
        OnResourceChanged?.Invoke(ItemId.Mushroom);
    }

    void ConsumeEquippedFish()
    {
        int eq = _equippedSlot;
        if (eq < 0 || eq >= TotalSlots) return;
        var slot = slots[eq];
        if (slot.id != ItemId.Fish || slot.fishData == null) return;

        RawFishConsumption.Consume(slot.fishData.fishType);
        // Burp reaction, 1-3s later so it reads as a response to the meal rather
        // than landing on top of the last chew. PlayerSuitAudio owns the delay
        // and the random pick.
        PlayerSuitAudio.Instance?.PlayBurpAfterDelay();
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
            ItemId.Sapling => 50,
            // Handoff §3: 20 per stack, species-pure.
            ItemId.Mushroom => 20,
            ItemId.MushroomSapling => 20,
            // Blanks are cheap bulk stock you carry to the computer; printed
            // tapes stack per SONG, so a big cap would just hide how many
            // different tracks you are hauling.
            ItemId.BlankTapeT1 => 20,
            ItemId.BlankTapeT2 => 20,
            ItemId.BlankTapeHalfT1 => 20,
            ItemId.BlankTapeHalfT2 => 20,
            ItemId.BlankTapeFullT1 => 20,
            ItemId.BlankTapeFullT2 => 20,
            ItemId.Cassette => 10,
            // One TRAX install per stick and one install per world — a stack
            // would just be money Tev shouldn't have taken.
            ItemId.TraxUsbStick => 1,
            // Money is UNCAPPED — the stack count is the balance, so a cap here
            // would silently be a cap on how rich the player may be, and any
            // spill logic would need somewhere to spill to. There isn't one:
            // money lives in exactly one hotbar slot.
            ItemId.Money => int.MaxValue,
            _ => 1,
        };
    }

    public event System.Action<ItemId> OnResourceChanged;

    /// Total of a resource across every stack. For mushrooms this is the total
    /// of ALL species — use <see cref="GetMushroomTotal"/> for one species.
    public int GetResourceTotal(ItemId resource)
    {
        if (!IsResource(resource)) return 0;
        int sum = 0;
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == resource) sum += slots[i].count;
        return sum;
    }

    /// Total of ONE mushroom species. Pass a null/empty species to match any.
    public int GetMushroomTotal(ItemId resource, string species) =>
        GetVariantTotal(resource, species);

    /// Total of ONE variant — a mushroom species or a cassette's song. Pass a
    /// null/empty variant to match any.
    public int GetVariantTotal(ItemId resource, string variant)
    {
        if (!CarriesVariant(resource)) return 0;
        bool any = string.IsNullOrEmpty(variant);
        int sum = 0;
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == resource && (any || VariantOf(slots[i]) == variant))
                sum += slots[i].count;
        return sum;
    }

    /// The species of the first stack of <paramref name="resource"/> found, or
    /// null if the player has none. Used by the sell flow to know what it's
    /// selling without asking the player to pick a stack.
    public string FirstMushroomSpecies(ItemId resource)
    {
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == resource && slots[i].count > 0) return slots[i].mushroomSpecies;
        return null;
    }

    // Returns leftover amount that didn't fit (0 = fully accepted).
    public int AddResource(ItemId resource, int amount) => AddResource(resource, amount, null);

    /// Species-aware add. <paramref name="species"/> is ignored for non-mushroom
    /// items; for mushrooms a null species is resolved to the registry's first
    /// species so a mis-wired caller can never create an unidentifiable stack.
    public int AddResource(ItemId resource, int amount, string species)
    {
        if (!IsResource(resource) || amount <= 0) return amount > 0 ? amount : 0;
        bool variant = CarriesVariant(resource);
        if (IsMushroomItem(resource) && string.IsNullOrEmpty(species)) species = MushroomRegistry.AnyKey();
        if (!variant) species = null;

        int cap = StackMax(resource);
        int remaining = amount;
        bool changed = false;

        // Fill existing stacks first — species-pure for mushrooms.
        for (int i = 0; i < NumSlots && remaining > 0; i++)
        {
            if (slots[i].id != resource) continue;
            if (variant && VariantOf(slots[i]) != species) continue;
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
            slots[i] = MakeSlot(resource, take, species);
            remaining -= take;
            changed = true;
        }

        if (changed) OnResourceChanged?.Invoke(resource);
        return remaining;
    }

    /// <summary>
    /// Add printed cassettes of one song. Returns HOW MANY ACTUALLY FIT, which
    /// the printer needs before it spends any blanks — otherwise a full hotbar
    /// would eat the stock and hand back nothing.
    ///
    /// Stacks are song-pure: two different tracks never merge, and a T2 pressing
    /// is a different print id from the T1 of the same song, so they separate
    /// too. That falls out of the variant rule rather than needing its own path.
    /// </summary>
    public int AddCassette(string printId, int amount)
    {
        if (string.IsNullOrEmpty(printId) || amount <= 0) return 0;
        int leftover = AddResource(ItemId.Cassette, amount, printId);
        return amount - leftover;
    }

    /// How many tapes of one song the player is carrying.
    public int GetCassetteTotal(string printId) => GetVariantTotal(ItemId.Cassette, printId);

    /// The song in the first cassette stack found, or null. The sell flow uses
    /// this to know what it is offering without asking the player to pick.
    public string FirstCassetteId()
    {
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == ItemId.Cassette && slots[i].count > 0) return slots[i].cassetteId;
        return null;
    }

    // All-or-nothing: drain leftmost stacks first, return false if total insufficient.
    public bool SpendResource(ItemId resource, int amount) => SpendResource(resource, amount, null);

    /// Species-aware spend. A null species on a mushroom item means "any
    /// species", draining leftmost-first across stacks.
    public bool SpendResource(ItemId resource, int amount, string species)
    {
        if (!IsResource(resource)) return false;
        if (amount <= 0) return true;
        bool variant = CarriesVariant(resource);
        if (!variant) species = null;
        bool anySpecies = !variant || string.IsNullOrEmpty(species);

        int have = anySpecies ? GetResourceTotal(resource) : GetVariantTotal(resource, species);
        if (have < amount) return false;

        int remaining = amount;
        for (int i = 0; i < NumSlots && remaining > 0; i++)
        {
            if (slots[i].id != resource) continue;
            if (!anySpecies && VariantOf(slots[i]) != species) continue;
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
        // TotalSlots, not NumSlots: the money slot has to be wiped too, or the
        // previous session's balance rides across the main menu into New Game
        // (the Hotbar is DontDestroyOnLoad — CLAUDE.md's statics-leak trap).
        for (int i = 0; i < TotalSlots; i++) slots[i] = default;
        _equippedSlot = -1;
        _cycleCursor = -1;
        OnResourceChanged?.Invoke(ItemId.Wood);
        OnResourceChanged?.Invoke(ItemId.Crystal);
        OnResourceChanged?.Invoke(ItemId.SpaceDust);
        OnResourceChanged?.Invoke(ItemId.Sapling);
        OnResourceChanged?.Invoke(ItemId.Mushroom);
        OnResourceChanged?.Invoke(ItemId.MushroomSapling);
        OnResourceChanged?.Invoke(ItemId.Money);
    }

    /// Is this item in the player's hotbar right now? Item slots only — asking
    /// after ItemId.Money would be asking "is the money in the money slot", so
    /// use <see cref="Money"/> for that instead.
    public bool HasItem(ItemId id)
    {
        if (id == ItemId.None) return false;
        for (int i = 0; i < NumSlots; i++)
            if (slots[i].id == id) return true;
        return false;
    }

    // ── Money slot ───────────────────────────────────────────────────
    //
    // The slot IS the wallet. There is no second number anywhere: PlayerWallet
    // reads and writes through here, and every existing caller
    // (vendors, fish market, rent collector, mission payouts, cheats) keeps
    // talking to PlayerWallet and never learns this moved.
    //
    // Money is not an ItemId.Money "resource" — see IsResource. These two
    // methods are the ONLY way the count changes from gameplay code; the
    // drag/drop layer moves it as an item and is bounded by SlotAccepts.

    /// The player's balance. Reading a slot count, not a mirrored field.
    public int Money => slots[MoneySlotIndex].id == ItemId.Money
        ? slots[MoneySlotIndex].count
        : 0;

    /// Set the balance outright. Clamped at 0; a zero balance empties the slot
    /// rather than leaving an ItemId.Money stack of count 0, so the slot renders
    /// empty and the drag layer can't pick up nothing.
    public void SetMoney(int amount)
    {
        int v = Mathf.Max(0, amount);
        slots[MoneySlotIndex] = v > 0
            ? new Slot { id = ItemId.Money, count = v }
            : default;
        OnResourceChanged?.Invoke(ItemId.Money);
    }

    /// Add (or, with a negative amount, subtract) — never below zero.
    public void AddMoney(int delta) => SetMoney(Money + delta);

    /// True when the slot can legally hold this id.
    ///
    /// Two rules, and they are the reason a dupe can't happen: the money slot
    /// takes money and nothing else, and money can't sit in any other hotbar
    /// slot. Storage containers (lockers, bags) are unrestricted — money in a
    /// locker is a feature, and it rides the existing StorageSync lock so co-op
    /// sharing needs no new netcode.
    public static bool SlotAccepts(Slot[] container, int idx, ItemId id)
    {
        if (container == null || idx < 0 || idx >= container.Length) return false;
        // Only the hotbar's own array is restricted. Everything else is storage.
        if (instance == null || !ReferenceEquals(container, instance.slots)) return true;
        return idx == MoneySlotIndex ? id == ItemId.Money : id != ItemId.Money;
    }

    // ── Save / load access ───────────────────────────────────────────
    //
    // Only the ITEM slots round-trip through the slot list. The balance keeps
    // its own long-standing SaveData.money field, applied via PlayerWallet — so
    // the save schema is untouched, old saves load with no migration, and the
    // money slot can't end up described twice in one file.
    public IReadOnlyList<Slot> GetSlotsForSave()
    {
        var list = new List<Slot>(NumSlots);
        for (int i = 0; i < NumSlots; i++) list.Add(slots[i]);
        return list;
    }

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
            slots[i] = new Slot
            {
                id = id,
                count = count,
                fishData = fish,
                bagContents = bag,
                mushroomSpecies = IsMushroomItem(id) ? entry.mushroomSpecies : null,
                cassetteId = id == ItemId.Cassette ? entry.cassetteId : null,
            };
        }
        // Notify subscribers (facades) so their OnChanged fires once each.
        OnResourceChanged?.Invoke(ItemId.Wood);
        OnResourceChanged?.Invoke(ItemId.Crystal);
        OnResourceChanged?.Invoke(ItemId.SpaceDust);
        OnResourceChanged?.Invoke(ItemId.Sapling);
        OnResourceChanged?.Invoke(ItemId.Mushroom);
        OnResourceChanged?.Invoke(ItemId.MushroomSapling);
    }

    // NOTE Money is deliberately NOT a resource. Resources flow through
    // AddResource / SpendResource, which spill across slots and drain
    // leftmost-first; money must never do either. Every balance change goes
    // through PlayerWallet, which is the single writer for the money slot.
    static bool IsResource(ItemId id)
    {
        return id is ItemId.Wood or ItemId.Crystal or ItemId.SpaceDust or ItemId.Sapling
                  or ItemId.Mushroom or ItemId.MushroomSapling
                  or ItemId.BlankTapeT1 or ItemId.BlankTapeT2
                  or ItemId.BlankTapeHalfT1 or ItemId.BlankTapeHalfT2
                  or ItemId.BlankTapeFullT1 or ItemId.BlankTapeFullT2
                  or ItemId.Cassette or ItemId.TraxUsbStick;
    }

    // Slot-only items: selected via number key but have no controller to equip.
    // Covers stacking resources AND fish AND fish bags (no controller backing
    // any of them). GetEquipped/UnequipAll/ToggleSlot/CycleSlot use this to
    // skip the registry lookup for these slots.
    static bool IsSelectOnly(ItemId id) =>
        IsResource(id) || id == ItemId.Fish || id == ItemId.FishBag || id == ItemId.Money;

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
    static readonly Color SaplingSwatchColor = new Color32(0x7B, 0xE6, 0x8C, 0xFF);   // living green (matches AMBIENT O2 bar)
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

    // Blanks are drab, printed tapes glow — you should be able to tell stock
    // from product without reading. T2 is the premium shell.
    static readonly Color BlankT1SwatchColor   = new Color32(0x5A, 0x5F, 0x66, 0xFF);   // grey shell
    static readonly Color BlankT2SwatchColor   = new Color32(0x8C, 0x7A, 0x3F, 0xFF);   // gold shell
    // The bigger formats get their own shell families so DEMO/HALF/FULL read
    // apart at a glance, with T2 always the warmer/richer of the pair.
    static readonly Color BlankHalfT1Swatch    = new Color32(0x4F, 0x6B, 0x8A, 0xFF);   // slate-blue shell
    static readonly Color BlankHalfT2Swatch    = new Color32(0x9A, 0x6B, 0x3F, 0xFF);   // bronze shell
    static readonly Color BlankFullT1Swatch    = new Color32(0x3F, 0x8A, 0x6B, 0xFF);   // green shell
    static readonly Color BlankFullT2Swatch    = new Color32(0xA8, 0x4F, 0x8A, 0xFF);   // violet shell
    static readonly Color CassetteT1Swatch     = new Color32(0x79, 0xFF, 0xD0, 0xFF);   // TRAX phosphor
    static readonly Color CassetteT2Swatch     = new Color32(0xFF, 0x4F, 0xD8, 0xFF);   // TRAX magenta
    static readonly Color TraxUsbSwatchColor   = new Color32(0x79, 0xD0, 0xFF, 0xFF);   // USB cyan — TRAX family, not a tape

    /// A printed tape's colour comes from its tier. Null id falls back to T1.
    static Color CassetteSwatch(string cassetteId) =>
        TraxPrints.TierOf(cassetteId) >= 2 ? CassetteT2Swatch : CassetteT1Swatch;

    // Public faces of the two swatch lookups, so world props (the cassette in
    // the computer's slot, the tape on its eject) are tinted from the SAME
    // table the hotbar slot uses. Two places deciding what colour a TAPE II is
    // would drift the moment either was retuned.
    public static Color SwatchFor(ItemId id) => ResourceSwatchColor(id);
    public static Color CassetteSwatchFor(string cassetteId) => CassetteSwatch(cassetteId);

    static readonly Color MushroomSwatchColor  = new Color32(0xE0, 0x6C, 0x75, 0xFF);   // cap red
    static readonly Color MushSaplingSwatchCol = new Color32(0xC8, 0x9B, 0xE6, 0xFF);   // spore violet

    static Color ResourceSwatchColor(ItemId id)
    {
        switch (id)
        {
            case ItemId.Wood:      return WoodSwatchColor;
            case ItemId.Crystal:   return CrystalSwatchColor;
            case ItemId.SpaceDust: return DustSwatchColor;
            case ItemId.Sapling:   return SaplingSwatchColor;
            case ItemId.Mushroom:  return MushroomSwatchColor;
            case ItemId.MushroomSapling: return MushSaplingSwatchCol;
            case ItemId.BlankTapeT1: return BlankT1SwatchColor;
            case ItemId.BlankTapeT2: return BlankT2SwatchColor;
            case ItemId.BlankTapeHalfT1: return BlankHalfT1Swatch;
            case ItemId.BlankTapeHalfT2: return BlankHalfT2Swatch;
            case ItemId.BlankTapeFullT1: return BlankFullT1Swatch;
            case ItemId.BlankTapeFullT2: return BlankFullT2Swatch;
            // A printed tape takes the colour of its TIER, so a shelf of them
            // reads at a glance even before you check the names.
            case ItemId.Cassette:  return CassetteSwatch(null);
            case ItemId.TraxUsbStick: return TraxUsbSwatchColor;
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
            case ItemId.Sapling:   return "SAPLINGS";
            case ItemId.Mushroom:  return "MUSHROOM";
            case ItemId.MushroomSapling: return "SPORES";
            case ItemId.BlankTapeT1: return "DEMO TAPE";
            case ItemId.BlankTapeT2: return "DEMO TAPE II";
            case ItemId.BlankTapeHalfT1: return "HALF TAPE";
            case ItemId.BlankTapeHalfT2: return "HALF TAPE II";
            case ItemId.BlankTapeFullT1: return "FULL TAPE";
            case ItemId.BlankTapeFullT2: return "FULL TAPE II";
            case ItemId.Cassette:  return "CASSETTE";
            case ItemId.TraxUsbStick: return "TRAX USB";
            case ItemId.Money:     return "MONEY";
            default: return "—";
        }
    }

    // Resource icons live in Assets/Resources/HotbarIcons/ so they can be loaded
    // at runtime without scene/prefab wiring (the Hotbar is auto-created, no
    // inspector). Loaded once per session and cached statically.
    static Sprite _woodIcon, _crystalIcon, _dustIcon, _saplingIcon;
    static bool _iconsLoaded;

    // public: ResourceDrop reuses this as the single source of truth for the
    // world-drop sprite, so hotbar slot and ground item always match.
    public static Sprite ResourceIcon(ItemId id)
    {
        if (!_iconsLoaded)
        {
            _woodIcon    = Resources.Load<Sprite>("HotbarIcons/TransparentWoodLog");
            _crystalIcon = Resources.Load<Sprite>("HotbarIcons/TransparentCrystalShards");
            _dustIcon    = Resources.Load<Sprite>("HotbarIcons/TransparentSpaceDust");
            // Optional — if absent, the slot falls back to the green swatch colour.
            _saplingIcon = Resources.Load<Sprite>("HotbarIcons/TransparentSapling");
            _iconsLoaded = true;
        }
        switch (id)
        {
            case ItemId.Wood:      return _woodIcon;
            case ItemId.Crystal:   return _crystalIcon;
            case ItemId.SpaceDust: return _dustIcon;
            case ItemId.Sapling:   return _saplingIcon;
            case ItemId.Money:     return MoneyIcon();
            case ItemId.BlankTapeT1: return CassetteSprite(BlankT1SwatchColor);
            case ItemId.BlankTapeT2: return CassetteSprite(BlankT2SwatchColor);
            case ItemId.BlankTapeHalfT1: return CassetteSprite(BlankHalfT1Swatch);
            case ItemId.BlankTapeHalfT2: return CassetteSprite(BlankHalfT2Swatch);
            case ItemId.BlankTapeFullT1: return CassetteSprite(BlankFullT1Swatch);
            case ItemId.BlankTapeFullT2: return CassetteSprite(BlankFullT2Swatch);
            // A PRINTED tape's colour is its tier, which the id alone does not
            // give us — the slot renderer calls CassetteSpriteFor instead. This
            // generic shell is the fallback for callers that only have the id
            // (a remote player's held item, a world drop).
            case ItemId.Cassette:  return CassetteSprite(CassetteT1Swatch);
            case ItemId.TraxUsbStick: return TraxUsbIcon();
            default: return null;
        }
    }

    // TRAX USB stick — placeholder drawn in code, same deal as MoneyIcon: drop
    // a real sprite at Resources/HotbarIcons/TransparentTraxUsb and it wins
    // automatically, no code change. (Flagged for Sam's art pass.)
    static Sprite _traxUsbIcon;
    static bool _traxUsbIconTried;
    static Sprite TraxUsbIcon()
    {
        if (_traxUsbIconTried) return _traxUsbIcon;
        _traxUsbIconTried = true;

        _traxUsbIcon = Resources.Load<Sprite>("HotbarIcons/TransparentTraxUsb");
        if (_traxUsbIcon != null) return _traxUsbIcon;

        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color32[size * size];   // starts fully transparent

        var body   = new Color32(0x2A, 0x38, 0x4A, 0xFF);   // dark shell
        var edge   = new Color32(0x16, 0x1E, 0x28, 0xFF);
        var stripe = new Color32(0x79, 0xD0, 0xFF, 0xFF);   // matches the swatch
        var metal  = new Color32(0xB8, 0xC2, 0xCC, 0xFF);   // connector
        var slotCo = new Color32(0x6E, 0x78, 0x82, 0xFF);   // connector holes

        // Upright stick: connector on top, shell below, phosphor stripe = label.
        const int bodyX = 33, bodyW = 30, bodyY = 14, bodyH = 46;
        const int connW = 22, connH = 18;
        int connX = bodyX + (bodyW - connW) / 2, connY = bodyY + bodyH;

        for (int y = 0; y < bodyH; y++)
            for (int x = 0; x < bodyW; x++)
            {
                bool e = x < 2 || x >= bodyW - 2 || y < 2 || y >= bodyH - 2;
                bool label = !e && y >= 10 && y < 22 && x >= 6 && x < bodyW - 6;
                px[(bodyY + y) * size + bodyX + x] = e ? edge : label ? stripe : body;
            }
        for (int y = 0; y < connH; y++)
            for (int x = 0; x < connW; x++)
            {
                bool hole = y >= connH - 8 && y < connH - 3 &&
                            ((x >= 4 && x < 8) || (x >= connW - 8 && x < connW - 4));
                px[(connY + y) * size + connX + x] = hole ? slotCo : metal;
            }

        tex.SetPixels32(px);
        tex.Apply();
        _traxUsbIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _traxUsbIcon;
    }

    // Cash-stack icon. Drawn in code rather than shipped as a PNG because the
    // Hotbar auto-creates with no inspector and there is no art for money yet —
    // drop a sprite at Resources/HotbarIcons/TransparentMoney and it wins
    // automatically, no code change.
    static Sprite _moneyIcon;
    static bool _moneyIconTried;
    static Sprite MoneyIcon()
    {
        if (_moneyIconTried) return _moneyIcon;
        _moneyIconTried = true;

        _moneyIcon = Resources.Load<Sprite>("HotbarIcons/TransparentMoney");
        if (_moneyIcon != null) return _moneyIcon;

        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color32[size * size];   // starts fully transparent

        // Three offset notes, back to front, so the stack reads at 64 px.
        var noteFill   = new Color32(0x4E, 0xA8, 0x6B, 0xFF);
        var noteEdge   = new Color32(0x2C, 0x6B, 0x43, 0xFF);
        var bandFill   = new Color32(0x9F, 0xE3, 0xB4, 0xFF);
        const int noteW = 62, noteH = 34;

        for (int n = 2; n >= 0; n--)
        {
            int x0 = 10 + n * 5;
            int y0 = 22 + n * 9;
            for (int y = 0; y < noteH; y++)
            {
                for (int x = 0; x < noteW; x++)
                {
                    int gx = x0 + x, gy = y0 + y;
                    if (gx < 0 || gx >= size || gy < 0 || gy >= size) continue;
                    bool edge = x < 2 || x >= noteW - 2 || y < 2 || y >= noteH - 2;
                    // Centre oval = the portrait window on a banknote.
                    float dx = (x - noteW * 0.5f) / (noteW * 0.22f);
                    float dy = (y - noteH * 0.5f) / (noteH * 0.42f);
                    bool band = dx * dx + dy * dy <= 1f;
                    px[gy * size + gx] = edge ? noteEdge : band ? bandFill : noteFill;
                }
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        _moneyIcon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _moneyIcon;
    }

    // ── cassette art (placeholder, drawn in code) ────────────────────────
    //
    // There is no cassette art yet and the Hotbar auto-creates with no
    // inspector, so the shell is drawn once per colour and cached — exactly the
    // approach MoneyIcon takes above. Drop a real sprite at
    // Resources/HotbarIcons/TransparentCassette and it wins automatically.
    static readonly Dictionary<uint, Sprite> _cassetteSprites = new Dictionary<uint, Sprite>();
    static Sprite _authoredCassette;
    static bool _authoredCassetteTried;

    /// <summary>A cassette shell in <paramref name="shell"/>: body, label strip
    /// and two hubs. Readable at 64 px, which is all a hotbar slot gives it.</summary>
    public static Sprite CassetteSprite(Color shell)
    {
        if (!_authoredCassetteTried)
        {
            _authoredCassetteTried = true;
            _authoredCassette = Resources.Load<Sprite>("HotbarIcons/TransparentCassette");
        }
        if (_authoredCassette != null) return _authoredCassette;

        var key32 = (Color32)shell;
        uint key = (uint)(key32.r << 16 | key32.g << 8 | key32.b);
        Sprite cached;
        if (_cassetteSprites.TryGetValue(key, out cached) && cached != null) return cached;

        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color32[size * size];        // starts fully transparent

        Color32 body  = key32;
        Color32 edge  = Mul(key32, 0.45f);
        Color32 label = new Color32(0xEC, 0xF2, 0xF6, 0xFF);
        Color32 hub   = Mul(key32, 0.25f);

        const int bx = 10, by = 24, bw = 76, bh = 48;   // the shell
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                int gx = bx + x, gy = by + y;
                // Clipped corners, so it reads as a cassette and not a brick.
                int cut = 5;
                if ((x < cut && y < cut) || (x >= bw - cut && y < cut)) continue;
                bool border = x < 3 || x >= bw - 3 || y < 3 || y >= bh - 3;
                px[gy * size + gx] = border ? edge : body;
            }

        // Label strip across the top two thirds.
        for (int y = by + 26; y < by + 42; y++)
            for (int x = bx + 8; x < bx + bw - 8; x++)
                px[y * size + x] = label;

        // Two hubs in the tape window.
        DrawDisc(px, size, bx + 26, by + 17, 7, hub);
        DrawDisc(px, size, bx + bw - 26, by + 17, 7, hub);

        tex.SetPixels32(px);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        _cassetteSprites[key] = sprite;
        return sprite;
    }

    static Color32 Mul(Color32 c, float f) =>
        new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), c.a);

    static void DrawDisc(Color32[] px, int size, int cx, int cy, int r, Color32 c)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y > r * r) continue;
                int gx = cx + x, gy = cy + y;
                if (gx < 0 || gx >= size || gy < 0 || gy >= size) continue;
                px[gy * size + gx] = c;
            }
    }

    /// The shell colour a printed tape should wear, from its print id.
    public static Sprite CassetteSpriteFor(string cassetteId) =>
        CassetteSprite(CassetteSwatch(cassetteId));

    static readonly Dictionary<uint, Sprite> _cassetteWide = new Dictionary<uint, Sprite>();

    /// <summary>
    /// The same cassette art, cropped to the shell itself.
    ///
    /// CassetteSprite draws a 76x48 shell inside a 96x96 texture. That framing
    /// is right for a SQUARE hotbar slot, but it wastes a wide box: preserveAspect
    /// fits the full 96x96 bounds, transparent margins included, so in a 120x44
    /// tile the tape renders about 35x22 and reads as a stamp rather than a
    /// cassette. This returns a sub-rect sprite over the IDENTICAL texture, so a
    /// wide slot fills properly and not one extra pixel is generated.
    /// </summary>
    public static Sprite CassetteSpriteWide(Color shell)
    {
        Sprite full = CassetteSprite(shell);
        if (full == null) return null;
        // An authored sprite is framed however the artist framed it - cropping
        // to coordinates that describe the GENERATED art would mangle it.
        if (_authoredCassette != null) return full;

        var key32 = (Color32)shell;
        uint key = (uint)(key32.r << 16 | key32.g << 8 | key32.b);
        Sprite cached;
        if (_cassetteWide.TryGetValue(key, out cached) && cached != null) return cached;

        var wide = Sprite.Create(full.texture, new Rect(10, 24, 76, 48), new Vector2(0.5f, 0.5f));
        _cassetteWide[key] = wide;
        return wide;
    }

    public static Sprite CassetteSpriteWideFor(string cassetteId) =>
        CassetteSpriteWide(CassetteSwatch(cassetteId));

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
        if (_equippedSlot < 0 || _equippedSlot >= TotalSlots) return ItemId.None;
        return slots[_equippedSlot].id;
    }

    /// The whole selected slot, not just its id — HeldItemViewmodel needs
    /// fishData (weight/colour/tier) and bagContents to build the held model.
    /// Read one slot. Used by the sell flow to list distinct cassettes without
    /// copying the whole array.
    public Slot SlotAt(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) return default;
        return slots[index];
    }

    public Slot GetEquippedSlot()
    {
        if (_equippedSlot < 0 || _equippedSlot >= TotalSlots) return default;
        return slots[_equippedSlot];
    }

    /// Public face of IsSelectOnly — items that highlight a slot but have no
    /// controller to equip (resources, fish, fish bags). These are exactly the
    /// ones HeldItemViewmodel is responsible for showing in the player's hand.
    public static bool IsSelectOnlyItem(ItemId id) => IsSelectOnly(id);

    void HandleInput()
    {
        // Camera mode blocks hotbar swap entirely — the player is holding the
        // phone up like a camera and shouldn't be able to whip out the axe or
        // pistol with a number key. They have to close the camera first.
        if (PlayerPhoneUI.IsCameraMode) return;

        // Number keys 1..N for direct slot select.
        int slot = TutorialGate.HotbarSlotPressed(TotalSlots);
        if (slot > 0) { ToggleSlot(slot - 1); return; }

        // D-pad left / right cycles through slots with wrap. Skips when a UI
        // Selectable is focused (handled inside HotbarCycleStep) so menu nav
        // doesn't double as hotbar nav.
        int step = TutorialGate.HotbarCycleStep();
        if (step != 0) { CycleSlot(step); return; }

        // Mouse wheel cycles the hotbar while on foot. Scroll up = previous slot
        // (toward slot 1), down = next (toward slot 7) — matches the D-pad cycle
        // and Minecraft. Skipped only during BUILDING placement, where the wheel
        // adjusts the ghost's distance; sapling placement leaves the wheel free
        // (scrolling off the sapling slot also exits planting). HandleInput only
        // runs when not piloting / in dialogue / phone / map / modal slot UI.
        if (!GhostPlacement.WheelControlsPlacement)
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
            if (_equippedSlot >= 0 && _equippedSlot < TotalSlots && slots[_equippedSlot].id != ItemId.None)
                _cycleCursor = _equippedSlot;
        }
        int next = _cycleCursor < 0
            ? (step > 0 ? 0 : TotalSlots - 1)
            : ((_cycleCursor + step) % TotalSlots + TotalSlots) % TotalSlots;
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
        if (_equippedSlot >= 0 && _equippedSlot < TotalSlots)
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
        if (_equippedSlot >= 0 && _equippedSlot < TotalSlots && IsSelectOnly(slots[_equippedSlot].id))
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
        if (_canvasGroup == null && canvas != null) _canvasGroup = canvas.GetComponent<CanvasGroup>();
        if (_canvasGroup != null) _canvasGroup.alpha = groupAlpha;

        // When something is equipped, "active" tracks the equipped slot.
        // When nothing is equipped, "active" tracks the cycle cursor instead
        // so the player can see which empty slot they just landed on while
        // scrolling with D-pad / number keys (otherwise empty slots looked
        // identical and the player couldn't tell where the cursor was).
        for (int i = 0; i < TotalSlots; i++)
        {
            var v = slotViews[i];
            ItemId id = slots[i].id;
            bool empty = id == ItemId.None;
            // Phase 3 fix: active = exact-slot match, not id match. id-based
            // matching glowed every slot with the same id (problem for fish
            // and any resource with multiple stacks).
            bool active = (_equippedSlot >= 0 && _equippedSlot < TotalSlots)
                ? (i == _equippedSlot && !empty)
                : (i == _cycleCursor);

            // Icon — null sprite means empty / no icon assigned.
            // Resource slots: real PNG icon from Resources/HotbarIcons (falls
            // back to a tinted procedural swatch if the load fails).
            // Tool slots: controller's hotbarIcon. Empty: no icon.
            bool isRes = IsResource(id);
            bool isFish = id == ItemId.Fish;
            bool isFishBag = id == ItemId.FishBag;
            // Species likeness (handoff §3): a mushroom slot shows a live render
            // of the species that was chopped, through the same RawImage the
            // fish preview uses. Null until the registry has resolved a spawner,
            // in which case the slot falls back to the tinted swatch.
            bool isMushroom = IsMushroomItem(id);
            RenderTexture mushPreview = (isMushroom && !empty)
                ? MushroomRegistry.Preview(slots[i].mushroomSpecies)
                : null;
            bool mushPreviewVisible = mushPreview != null;
            Sprite sprite = null;
            Color iconTint = new Color32(0xF1, 0xF4, 0xFF, 0xC0);
            bool isProceduralSwatch = false;
            if (!empty)
            {
                if (isMushroom && mushPreviewVisible)
                {
                    // Rendered through the RawImage below; sprite stays null so
                    // the standard itemIcon Image is disabled.
                }
                else if (isFish)
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
                else if (id == ItemId.Cassette)
                {
                    // Tier colour is per-slot, not per-id, so this one resolves
                    // from the stack's print rather than through ResourceIcon.
                    sprite = CassetteSpriteFor(slots[i].cassetteId);
                }
                else if (isRes || id == ItemId.Money)
                {
                    sprite = ResourceIcon(id);
                    if (sprite == null)
                    {
                        // Fallback: keep the original colored-square placeholder
                        // so the slot isn't blank if the PNG is missing.
                        sprite = HotbarResourceSwatch.GetSprite();
                        // A cassette's colour is its TIER, which only this slot
                        // knows — ResourceSwatchColor sees the id alone.
                        iconTint = id == ItemId.Cassette
                            ? CassetteSwatch(slots[i].cassetteId)
                            : ResourceSwatchColor(id);
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
            v.itemIcon.enabled = sprite != null && !isFish && !mushPreviewVisible;

            // Phase 2: paint the fish preview RawImage for Fish slots. Render
            // via the dex's preview camera if we haven't yet for this entry.
            // Mushroom slots reuse the same RawImage for their species render.
            if (v.fishPreview != null)
            {
                bool fishVisible = isFish && !empty && slots[i].fishData != null;
                if (mushPreviewVisible)
                {
                    v.fishPreview.texture = mushPreview;
                    v.fishPreview.enabled = true;
                }
                else if (fishVisible)
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

            // Stack count text — resources, and money (whose "count" is the
            // balance, so it gets a $ and thousands separators; 12450 as a bare
            // number in a 14 px corner label is unreadable at a glance).
            if (v.countText != null)
            {
                if ((isRes || id == ItemId.Money) && !empty)
                {
                    string countStr = id == ItemId.Money
                        ? "$" + slots[i].count.ToString("N0")
                        : slots[i].count.ToString();
                    if (v.countText.text != countStr) v.countText.text = countStr;
                    v.countText.enabled = true;
                }
                else if (v.countText.enabled)
                {
                    v.countText.enabled = false;
                }
            }

            // Sweep first — it produces this slot's brightness, which every
            // colour below is then multiplied by so the scanline visibly wipes
            // the slot back to life in its wake.
            UpdateSweep(v, active);
            float b = v.brightness;

            // No slot box any more, so "selected" is carried entirely by
            // brightness + brackets + the sweep. Everything not held is dimmed.
            v.itemIcon.color = empty
                ? new Color32(0xF1, 0xF4, 0xFF, 0x00)
                : new Color(0.918f, 0.965f, 1f, (active ? 1f : BaselineAlpha) * b);

            // Real PNG resource icons render with their own colours (white tint
            // with active-state alpha). Procedural fallback swatches need the
            // resource colour applied as a tint.
            if (isRes && !empty)
            {
                Color c = isProceduralSwatch ? iconTint : Color.white;
                c.a = (active ? 1f : BaselineAlpha) * b;
                v.itemIcon.color = c;
            }
            if (v.countText != null && v.countText.enabled)
                v.countText.color = new Color(1f, 1f, 1f, (active ? 1f : BaselineAlpha + 0.2f) * b);

            if (v.indexText != null)
                v.indexText.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g,
                    GalaxyHudKit.BorderCool.b, (active ? IndexAlphaActive : IndexAlphaIdle) * b);

            if (v.glow != null)
            {
                // Empty slots never glow — the glow is what says "there's
                // something in here", so lighting an empty one would say nothing.
                bool glowOn = !empty;
                if (v.glow.enabled != glowOn) v.glow.enabled = glowOn;
                if (glowOn)
                {
                    // The glow fades to NOTHING, not to a floor. Everything else
                    // on the slot multiplies straight by `b`, which bottoms out
                    // at SlotDimFloor and so never reaches zero — that left a
                    // permanent faint smudge. Remapping [floor..1] → [0..1]
                    // makes the glow swell with each scanline and vanish
                    // completely between passes.
                    float g = Mathf.InverseLerp(SlotDimFloor, 1f, b);
                    var gc = (Color)GalaxyHudKit.BorderCool;
                    gc.a = (active ? GlowAlphaActive : GlowAlphaIdle) * g * g;
                    v.glow.color = gc;
                }
            }

            UpdateBrackets(v, active, empty, b);
        }

        // Slot lift/scale animation — only fire on active-index change.
        int newActive = -1;
        for (int i = 0; i < TotalSlots; i++)
        {
            ItemId id = slots[i].id;
            bool empty = id == ItemId.None;
            bool active = (_equippedSlot >= 0 && _equippedSlot < TotalSlots)
                ? (i == _equippedSlot && !empty)
                : (i == _cycleCursor);
            if (active) newActive = i;
        }
        if (newActive != _animatedActiveIdx)
        {
            if (_animatedActiveIdx >= 0 && _animatedActiveIdx < TotalSlots)
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
            else if (IsMushroomItem(activeId))
            {
                // Name the SPECIES, not the category — the whole point of
                // species-pure stacks is that the player can tell them apart.
                string sp = MushroomRegistry.DisplayName(slots[newActive].mushroomSpecies).ToUpperInvariant();
                string suffix = activeId == ItemId.MushroomSapling ? " SPORES" : "";
                label = $"{sp}{suffix} ×{slots[newActive].count}";
            }
            else if (activeId == ItemId.Cassette)
            {
                // Name the SONG, not the object. The whole point of printing a
                // tape is that it is a specific thing you made.
                string song = TraxPrints.DisplayName(slots[newActive].cassetteId).ToUpperInvariant();
                string kind = " · " + TraxKind.Label(TraxPrints.KindOf(slots[newActive].cassetteId));
                string tier = TraxPrints.TierOf(slots[newActive].cassetteId) >= 2 ? " II" : "";
                label = $"{song}{kind}{tier} ×{slots[newActive].count}";
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
                ? Mathf.Clamp01(_eatHeldSeconds / _holdDuration)
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

    // Brackets sit on every slot and GROW on the selected one — that's the
    // selection read, along with the slot's own lift/scale. Eased rather than
    // snapped so the growth is legible as a transition.
    void UpdateBrackets(SlotVisuals v, bool active, bool empty, float brightness)
    {
        if (v.brackets == null) return;

        float target = active ? BracketActiveSize : BracketIdleSize;
        v.bracketSize = Mathf.MoveTowards(v.bracketSize, target, BracketGrowSpeed * Time.unscaledDeltaTime);
        // Corners ride outward as the arms lengthen, so the frame opens up
        // around the item instead of closing in on it.
        float t = Mathf.InverseLerp(BracketIdleSize, BracketActiveSize, v.bracketSize);
        float outp = Mathf.Lerp(BracketIdleOut, BracketActiveOut, t);

        Color col = active
            ? (Color)GalaxyHudKit.BorderHot
            : new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b,
                        empty ? 0.28f : 0.55f);
        col.a *= brightness;   // brackets dim and re-light with the rest of the slot

        for (int c = 0; c < v.brackets.Length; c++)
        {
            var img = v.brackets[c];
            if (img == null) continue;
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(v.bracketSize, v.bracketSize);
            Vector2 a = rt.anchorMin;
            rt.anchoredPosition = new Vector2(
                (a.x < 0.5f ? -1f : 1f) * outp,
                (a.y < 0.5f ? -1f : 1f) * outp);
            img.color = col;
        }
    }

    // Two rhythms. The selected slot sweeps continuously on a tight loop; every
    // other slot gets one slower pass at a random interval, so the bar reads as
    // idling equipment rather than seven things blinking in time.
    void UpdateSweep(SlotVisuals v, bool active)
    {
        if (v.sweep == null) return;
        float now = Time.unscaledTime;
        float u = -1f;

        if (active)
        {
            v.idleSweepStart = -1f;
            v.idleSweepNext = now + Random.Range(IdleSweepGapMin, IdleSweepGapMax);
            u = (now % SweepPeriod) / SweepPeriod;
        }
        else
        {
            if (v.idleSweepStart < 0f && now >= v.idleSweepNext) v.idleSweepStart = now;
            if (v.idleSweepStart >= 0f)
            {
                float p = (now - v.idleSweepStart) / IdleSweepDuration;
                if (p >= 1f)
                {
                    v.idleSweepStart = -1f;
                    v.idleSweepNext = now + Random.Range(IdleSweepGapMin, IdleSweepGapMax);
                }
                else u = p;
            }
        }

        bool on = u >= 0f;
        if (v.sweep.enabled != on) v.sweep.enabled = on;

        // Wake-brightening. Mirrors HudIdleSweep on the helmet clusters: the
        // slot decays toward the floor, and the pass wipes it back to full from
        // wherever it had faded to. The selected slot sweeps continuously, so it
        // never falls far — which is exactly the read we want.
        // A pass is DOWN then back UP, matching HudIdleSweep on the helmet
        // clusters: the line wipes down re-brightening as it goes, then rides
        // back up over the now-lit slot, fading out on the rise.
        float travel = u < 0.5f ? u / 0.5f : 1f - (u - 0.5f) / 0.5f;
        float fade   = u < 0.5f ? 1f : 1f - (u - 0.5f) / 0.5f;

        if (active)
        {
            // The held slot stays lit, full stop. Its sweep is a CONTINUOUS
            // loop, so ramping brightness with u would reset to dim every time
            // u wrapped past 1 — a 1.4 s pulse on the one slot that should be
            // the steadiest thing on the bar.
            v.brightness = 1f;
        }
        else if (on)
        {
            if (!v.sweepWasOn) v.dimAtSweepStart = v.brightness;
            // Only the DOWN stroke re-brightens; the rise leaves it lit.
            v.brightness = Mathf.Lerp(v.dimAtSweepStart, 1f, Mathf.Min(1f, u / 0.5f));
        }
        else
        {
            v.brightness = Mathf.MoveTowards(v.brightness, SlotDimFloor,
                (1f - SlotDimFloor) / SlotDecayTime * Time.unscaledDeltaTime);
        }
        v.sweepWasOn = on;
        if (!on) return;

        // Unselected passes are the same speed and shape, just half as bright.
        Color c = GalaxyHudKit.BorderHot;
        c.a *= fade * (active ? 1f : IdleSweepDimFactor);
        v.sweep.color = c;

        float h = v.root.sizeDelta.y;
        v.sweep.rectTransform.anchoredPosition =
            new Vector2(0f, Mathf.Lerp(SweepHeight, -(h + SweepHeight), travel));
    }

    // BorderPulse is gone with the slot borders it pulsed. The travelling
    // scanline is what gives the active slot its motion now.

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
        _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

        float totalWidth = TotalSlots * SlotSize + (TotalSlots - 1) * SlotSpacing;
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

        // Baseline. With the slot boxes gone the row had no top or bottom edge,
        // so it read as items floating at an arbitrary height. One hairline
        // under them, faded at both ends, is enough to ground it — added BEFORE
        // the slots so it draws underneath them.
        var baseRT = NewRT("__Baseline", bar);
        baseRT.anchorMin = new Vector2(0.5f, 0f);
        baseRT.anchorMax = new Vector2(0.5f, 0f);
        baseRT.pivot = new Vector2(0.5f, 0.5f);
        baseRT.sizeDelta = new Vector2(totalWidth + 36f, 1f);
        baseRT.anchoredPosition = new Vector2(0f, 12f);
        var baseImg = baseRT.gameObject.AddComponent<Image>();
        baseImg.sprite = HotbarBaselineFade.GetSprite();
        baseImg.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g,
                                  GalaxyHudKit.BorderCool.b, 0.85f);
        baseImg.raycastTarget = false;

        for (int i = 0; i < TotalSlots; i++)
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

        // Soft cyan backing, FIRST so it draws behind everything else in the
        // slot. Only ever shown on a slot that holds something — an empty slot
        // glowing would undo the whole point of the bracket-only look.
        var glowRT = NewRT("__Glow", slotRT);
        Stretch(glowRT, -GlowSpread, -GlowSpread, GlowSpread, GlowSpread);
        v.glow = glowRT.gameObject.AddComponent<Image>();
        v.glow.sprite = HotbarSoftGlow.GetSprite();
        v.glow.type = Image.Type.Simple;   // NOT Sliced — slicing would flatten the falloff
        v.glow.raycastTarget = false;
        v.glow.enabled = false;

        // Corner brackets — the only chrome on the bar. One L-shaped sprite
        // rotated four ways rather than eight thin strips per slot.
        v.bracketSize = BracketIdleSize;
        v.brightness = 1f;
        v.idleSweepStart = -1f;
        // Stagger the first idle pass so the seven slots never sweep together.
        v.idleSweepNext = Time.unscaledTime + Random.Range(0f, IdleSweepGapMax);
        v.brackets = new Image[4];
        for (int c = 0; c < 4; c++)
        {
            var brRT = NewRT("__Bracket" + c, slotRT);
            // c: 0 = top-left, 1 = top-right, 2 = bottom-right, 3 = bottom-left.
            Vector2 anchor = c == 0 ? new Vector2(0f, 1f)
                           : c == 1 ? new Vector2(1f, 1f)
                           : c == 2 ? new Vector2(1f, 0f)
                                    : new Vector2(0f, 0f);
            brRT.anchorMin = brRT.anchorMax = anchor;
            brRT.pivot = new Vector2(0.5f, 0.5f);
            brRT.sizeDelta = new Vector2(BracketIdleSize, BracketIdleSize);
            // Push each corner OUT of the slot so the brackets frame the item
            // rather than crowd it. Re-applied every frame as the size eases.
            brRT.anchoredPosition = new Vector2(
                (anchor.x < 0.5f ? -1f : 1f) * BracketIdleOut,
                (anchor.y < 0.5f ? -1f : 1f) * BracketIdleOut);
            brRT.localRotation = Quaternion.Euler(0f, 0f, -90f * c);
            var img = brRT.gameObject.AddComponent<Image>();
            img.sprite = HotbarBracket.GetSprite();
            img.color = GalaxyHudKit.BorderCool;
            img.raycastTarget = false;
            v.brackets[c] = img;
        }

        // Slot number, top-left, inside the bracket arm.
        var idxRT = NewRT("__Index", slotRT);
        idxRT.anchorMin = idxRT.anchorMax = new Vector2(0f, 1f);
        idxRT.pivot = new Vector2(0f, 1f);
        idxRT.anchoredPosition = new Vector2(6f, -5f);
        idxRT.sizeDelta = new Vector2(18f, 14f);
        v.indexText = idxRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(v.indexText);
        v.indexText.text = (index + 1).ToString();
        v.indexText.fontSize = 11f;   // 1.5× smaller than 16; faceDilate keeps the weight
        v.indexText.fontStyle = FontStyles.Bold;
        // Thickened 2026-08-06 — bold alone still read as hairline against a
        // bright planet. faceDilate fattens the glyph itself rather than just
        // scaling it up, so the digit stays small but gains weight.
        // .fontMaterial (not fontSharedMaterial) auto-instantiates, so this
        // fattens THIS label without touching every other TMP text in the game.
        v.indexText.fontMaterial.SetFloat(TMPro.ShaderUtilities.ID_FaceDilate, 0.28f);
        v.indexText.alignment = TextAlignmentOptions.TopLeft;
        v.indexText.raycastTarget = false;
        var idxDrop = idxRT.gameObject.AddComponent<Shadow>();
        idxDrop.effectColor = new Color(0f, 0f, 0f, 0.9f);
        idxDrop.effectDistance = new Vector2(1f, -1.5f);

        // The scanline. Parented to the slot so it inherits the active-slot
        // grow/lift animation for free; driven vertically in Refresh.
        var sweepRT = NewRT("__Sweep", slotRT);
        sweepRT.anchorMin = new Vector2(0f, 1f);
        sweepRT.anchorMax = new Vector2(1f, 1f);
        sweepRT.pivot = new Vector2(0.5f, 1f);
        sweepRT.sizeDelta = new Vector2(SweepOverhang * 2f, SweepHeight);
        v.sweep = sweepRT.gameObject.AddComponent<Image>();
        v.sweep.sprite = HotbarScanSweep.GetSprite();
        v.sweep.color = GalaxyHudKit.BorderHot;
        v.sweep.raycastTarget = false;
        v.sweep.enabled = false;

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
// The bar's baseline: a horizontal 1px strip that fades out at both ends so it
// doesn't stop dead in mid-air.
static class HotbarBaselineFade
{
    static Sprite _sprite;

    public static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int w = 64;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
        {
            name = "HotbarBaselineFade",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color[w];
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            // Flat across the middle 64%, easing to nothing over the outer 18%.
            float a = Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.18f);
            px[x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "HotbarBaselineFade";
        return _sprite;
    }
}

// Soft radial glow that reaches ZERO at its edge.
//
// HotbarHaloGlow deliberately does the opposite — its comment says it "keeps the
// slot-edge zone fully opaque so the visible halo reads loud", which was right
// when there was a slot background to fight. With the boxes gone that profile
// reads as a hard rectangle of colour, not a glow, so this one falls off
// smoothly to nothing instead.
static class HotbarSoftGlow
{
    static Sprite _sprite;

    public static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "HotbarSoftGlow",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Radial distance, 0 at centre → 1 at the inscribed circle.
            float dx = (x - half) / half, dy = (y - half) / half;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            // smoothstep gives a plateau in the middle and a gentle shoulder, so
            // there's no visible ring where the falloff starts.
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d));
            px[y * size + x] = new Color(1f, 1f, 1f, a * a);   // squared = softer tail
        }
        tex.SetPixels(px);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "HotbarSoftGlow";
        return _sprite;
    }
}

// L-shaped corner bracket, drawn once and rotated 90° per corner. Generated in
// code like the rest of the hotbar's sprites (HotbarRoundedRing,
// GalaxyHudKit.RoundedSprite) so it needs no art asset.
static class HotbarBracket
{
    static Sprite _sprite;

    public static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int size = 16;
        const int thick = 6;            // arm thickness in px — doubled 2026-08-06, 3 read as hairline
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "HotbarBracket",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Top edge and left edge of the square = an "⌐" corner. Texture y=0
            // is the BOTTOM row, so the top arm is the high-y rows.
            bool onTop  = y >= size - thick;
            bool onLeft = x < thick;
            px[y * size + x] = (onTop || onLeft) ? Color.white : new Color(1f, 1f, 1f, 0f);
        }
        tex.SetPixels(px);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "HotbarBracket";
        return _sprite;
    }
}

// The travelling scanline: a 1×N vertical gradient, transparent at both ends
// and brightest in the middle, stretched across the slot.
static class HotbarScanSweep
{
    static Sprite _sprite;

    public static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int h = 32;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
        {
            name = "HotbarScanSweep",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color[h];
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            // Smooth bell so the line has no hard edge at either end — a hard
            // edge reads as a rectangle sliding past rather than a scan.
            float a = Mathf.Sin(t * Mathf.PI);
            px[y] = new Color(1f, 1f, 1f, a * a);
        }
        tex.SetPixels(px);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
        _sprite.name = "HotbarScanSweep";
        return _sprite;
    }
}

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
