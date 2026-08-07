using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The mushroom deal panel — Schedule 1's haggle, rebuilt 2026-08-06 from the
/// four-concept prototype (Sam picked "C").
///
/// You drag stock onto the table, name a price, and the buyer accepts, counters,
/// or bars you:
///
///   round 1  →  ACCEPT · COUNTER · BARRED 5 min (if the ask is ridiculous)
///   round 2  →  TAKE IT · PUSH ONCE (accept or barred, final) · LEAVE IT
///
/// Three rules hold the design up, all of them load-bearing:
///
///  • <b>The counter is anchored on the BUYER's value, never on your ask.</b>
///    If it split the difference, asking double would drag the counter up with
///    it and "always ask absurd" becomes the optimal play. Anchored, a high ask
///    buys you pure risk — which is what makes the walk threshold the real
///    decision instead of a formality.
///  • <b>LEAVE IT parks the counter instead of losing it</b>
///    (<see cref="MushroomDealState"/>). Otherwise walking away and reopening
///    is a free re-roll of the acceptance dice. With it there's nothing to
///    re-roll, so leaving is safe and pushing is the only way to get barred.
///  • <b>Nothing about the buyer is printed on screen.</b> Sam killed the old
///    "pays 130% of base · patience 34% over" readout: it handed over the two
///    things the player is supposed to learn, and meant nothing to a new player.
///    The panel shows the STRAIN's market value (a property of the mushroom,
///    identical for every buyer), how far over market YOUR ask is, and what this
///    buyer has actually paid you before. Their rate leaks only through a
///    counter-offer, and that costs a failed offer to get.
///
/// Stock still comes from the hotbar and is still ONE SPECIES per deal, but you
/// now move it by hand — left click / right click / hold-and-drag, the same
/// rules as the locker, through the same <see cref="SlotOps"/>.
/// </summary>
public class MushroomSellUI : MonoBehaviour
{
    public static MushroomSellUI Instance { get; private set; }

    static readonly Color32 C_Bg      = new Color32(10, 24, 40, 244);
    static readonly Color32 C_Border  = new Color32(120, 200, 255, 220);
    static readonly Color32 C_Header  = new Color32(226, 120, 126, 255);
    static readonly Color32 C_Label   = new Color32(234, 246, 255, 255);
    static readonly Color32 C_Dim     = new Color32(127, 160, 189, 255);
    static readonly Color32 C_Value   = new Color32(255, 215, 50, 255);
    static readonly Color32 C_BtnSell = new Color32(60, 145, 70, 255);
    static readonly Color32 C_BtnTake = new Color32(47, 111, 140, 255);
    static readonly Color32 C_BtnBack = new Color32(140, 60, 60, 255);
    static readonly Color32 C_Ok      = new Color32(110, 220, 130, 255);
    static readonly Color32 C_Err     = new Color32(255, 110, 110, 255);
    static readonly Color32 C_SlotBg  = new Color32(8, 19, 31, 255);
    static readonly Color32 C_SlotEdge= new Color32(36, 66, 95, 255);

    const int HotbarSlots = 7;
    const float SlotSize  = 72f;
    const float SlotGap   = 6f;

    enum Stage { Open, Countered }

    // ── scene refs ────────────────────────────────────────────────────────
    Canvas _canvas;
    RectTransform _panelRT;
    GameObject _dim;
    TextMeshProUGUI _header, _memoText, _offerText, _totalText, _resultText, _riskText, _cdText, _counterText;
    RectTransform _offerSlotRT, _counterPanel;
    RawImage _offerPreview;
    TextMeshProUGUI _offerCount;
    Image _offerTier;
    TMP_InputField _askInput;
    Button _primaryBtn, _takeBtn, _secondaryBtn, _offerPlusBtn, _offerMinusBtn;
    TextMeshProUGUI _primaryLabel, _takeLabel, _secondaryLabel;
    SlotWidget[] _barSlots = new SlotWidget[HotbarSlots];
    RectTransform _cursorRT;
    RawImage _cursorPreview;
    TextMeshProUGUI _cursorCount;

    // ── deal state ────────────────────────────────────────────────────────
    string _npcName;
    NPCMushroomPrice _price;
    string _buyerId;
    Action _onClose;
    Action<int> _onSold;
    bool _open;
    Stage _stage;
    int _ask;
    int _counter;
    string _offerSpecies;
    int _offerCountN;
    SlotOps.CursorState _cursor;
    Coroutine _resultRoutine;
    bool _suppressInput;

    class SlotWidget
    {
        public RectTransform root;
        public Image bg, border, tier;
        public RawImage preview;
        public TextMeshProUGUI count;
        // Which HOTBAR slot this tile is showing. The panel lists ONLY mushroom
        // stacks, packed left, so tile N is not hotbar slot N — showing the
        // player's axe and water bottle in a mushroom deal was just noise, and
        // implied you could drag them in. -1 = tile unused (hidden).
        public int realIndex = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("MushroomSellUI");
        DontDestroyOnLoad(go);
        go.AddComponent<MushroomSellUI>();
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

    public bool IsOpen => _open;

    // Static forms for TabbedPauseMenu's "don't open pause over a modal" guard.
    // ConsumedEscapeThisFrame exists because both Updates run in the same frame
    // in an undefined order: without it, the Esc that closes this panel would
    // also pop the pause menu on top of the frame it closed.
    public static bool AnyOpen => Instance != null && Instance._open;
    static int s_escFrame = -1;
    public static bool ConsumedEscapeThisFrame => s_escFrame == Time.frameCount;

    /// A scene change kills the conversation this panel belonged to. Leaving it
    /// up was visible as "went back to the main menu and the sell UI was still
    /// on screen" — but the worse half was invisible: PlayerController
    /// .isInModalSlotUI stayed true, and that's a static, so it survives into
    /// the next New Game and locks the player out of their own controls.
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        bool toMainMenu = scene.name == "MainMenu";
        if (_open)
        {
            // Correct for a gameplay reload; harmless on the menu, where the
            // hotbar is about to be rebuilt from scratch anyway.
            ReturnOfferToBar();
            _cursor = default;
        }
        HardHide(restoreGameplayCursor: !toMainMenu);
    }

    /// Tear the panel down WITHOUT running the onClose callback — whatever NPC
    /// opened it is gone. Close() is the normal path; this is the scene-change
    /// and safety path.
    void HardHide(bool restoreGameplayCursor)
    {
        _open = false;
        _onClose = null;
        _onSold = null;
        _price = null;
        _offerSpecies = null;
        _offerCountN = 0;
        _cursor = default;
        _stage = Stage.Open;
        _counter = 0;
        if (_dim != null) _dim.SetActive(false);
        if (_panelRT != null) _panelRT.gameObject.SetActive(false);
        if (_cursorRT != null) _cursorRT.gameObject.SetActive(false);
        PlayerController.isInModalSlotUI = false;
        if (restoreGameplayCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <param name="price">This buyer's hidden rate + patience. Never rendered.</param>
    /// <param name="onSold">Number of caps sold, each time a deal closes.</param>
    public void Open(string npcName, NPCMushroomPrice price, Action onClose, Action<int> onSold = null)
    {
        _npcName = string.IsNullOrEmpty(npcName) ? "Buyer" : npcName;
        _price = price;
        _buyerId = price != null ? price.Identity : _npcName;
        _onClose = onClose;
        _onSold = onSold;
        _open = true;
        _cursor = default;
        _offerSpecies = null;
        _offerCountN = 0;
        _ask = 0;
        _stage = Stage.Open;
        _counter = 0;

        if (_dim != null) _dim.SetActive(true);
        _panelRT.gameObject.SetActive(true);
        PlayerController.isInModalSlotUI = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_header != null)
            _header.text = $"// {_npcName.ToUpperInvariant()}  <size=15><color=#7FA0BD>{BuyerLedger.BondPips(_buyerId)}</color></size>";
        SetResult("", C_Ok);
        Refresh();
    }

    public void Close()
    {
        if (!_open) return;

        // Never eat the player's stock. Anything on the table or on the cursor
        // goes back in the bar before the panel closes.
        ReturnOfferToBar();
        if (_cursor.IsHeld)
        {
            var hb = Hotbar.Instance;
            if (hb != null)
                hb.AddResource(_cursor.id, _cursor.count, _cursor.mushroomSpecies);
            _cursor = default;
        }

        _open = false;
        if (_dim != null) _dim.SetActive(false);
        _panelRT.gameObject.SetActive(false);
        PlayerController.isInModalSlotUI = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        var cb = _onClose;
        _onClose = null;
        _onSold = null;
        _price = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (!_open) return;

        // Esc backs out, like every other modal in the game. TabbedPauseMenu
        // checks AnyOpen / ConsumedEscapeThisFrame so this press doesn't also
        // pop the pause menu over the top.
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            s_escFrame = Time.frameCount;
            Close();
            return;
        }

        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;

        // Cursor follower tracks the mouse, same as StorageUI's.
        if (_cursorRT != null && _cursorRT.gameObject.activeSelf && _canvas != null)
        {
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _cursorRT.anchoredPosition = (Vector2)Input.mousePosition / scale;
        }

        // Live countdown while barred.
        if (_cdText != null && MushroomDealState.IsBarred(_buyerId))
            _cdText.text = $"{_npcName.ToUpperInvariant()} WON'T DEAL — {MushroomDealState.SecondsLeft(_buyerId)}s";
    }

    // ══ numbers ═══════════════════════════════════════════════════════════
    // Market is PUBLIC (a property of the strain). Fair and patience are the
    // buyer's and never leave this file.

    int Market => _offerSpecies != null ? MushroomRegistry.BaseValue(_offerSpecies) : 0;
    int Fair   => (_price != null && _offerSpecies != null) ? _price.PriceFor(_offerSpecies) : Market;
    float Patience => _price != null ? _price.Patience : 1.25f;
    int Total  => _ask * _offerCountN;
    bool Barred => MushroomDealState.IsBarred(_buyerId);
    bool HasOffer => _offerSpecies != null && _offerCountN > 0;

    // ══ the deal ══════════════════════════════════════════════════════════

    void MakeOffer()
    {
        if (!HasOffer || Barred || _ask <= 0) return;
        if (RemainingAppetite <= 0)
        {
            int wait = MushroomDealState.SecondsUntilHungry(_buyerId, AppetiteMax);
            SetResult($"\"I'm full up. Try me in {Mathf.CeilToInt(wait / 60f)} minutes.\"", C_Err);
            return;
        }
        int fair = Mathf.Max(1, Fair);
        float m = (float)_ask / fair;

        if (m <= 1.02f) { CloseSale(_ask); return; }

        if (m <= Patience)
        {
            // Anchored on THEIR value, not on the ask — see the class comment.
            // The 0–5% wobble is derived from the buyer id so the same alien
            // always counters at the same number for the same strain.
            uint h = StableHash(_buyerId + ":" + _offerSpecies);
            _counter = Mathf.Max(1, Mathf.RoundToInt(fair * (1f + (h % 6u) / 100f)));
            _stage = Stage.Countered;
            MushroomDealState.SetCounter(_buyerId, _offerSpecies, _counter);
            SetResult($"\"{_ask} a cap? Not a chance. I'll do {_counter}.\"", C_Label);
            Refresh();
            return;
        }

        BarBuyer();
    }

    void PushBack()
    {
        if (!HasOffer || Barred || _stage != Stage.Countered) return;

        // Asking at or under their counter is just taking it.
        if (_ask <= _counter) { CloseSale(_counter); return; }

        float over = (float)_ask / Mathf.Max(1, _counter);
        bool accept = over <= 1.15f
                   && UnityEngine.Random.value < 1f - (over - 1f) / 0.15f * 0.8f;
        if (accept) { SetResult($"\"...fine. {_ask}.\"", C_Ok); CloseSale(_ask); return; }
        BarBuyer();
    }

    void TakeCounter()
    {
        if (_stage != Stage.Countered || Barred) return;
        CloseSale(_counter);
    }

    /// Walk away from a counter WITHOUT pushing. Costs nothing — their number
    /// stays on the table for whenever you come back.
    void LeaveIt()
    {
        if (_stage != Stage.Countered) return;
        SetResult($"You leave it. {_npcName}'s {_counter} still stands.", C_Dim);
        _stage = Stage.Open;
        Refresh();
    }

    int AppetiteMax => _price != null ? _price.AppetiteMax : 999;
    int RemainingAppetite => MushroomDealState.Remaining(_buyerId, AppetiteMax);

    void CloseSale(int pricePerCap)
    {
        if (_offerCountN <= 0) return;

        // A buyer takes what they WANT, not whatever you piled on the table.
        // Handing the rest back (rather than refusing the whole deal) is how the
        // player discovers this buyer's appetite — one interaction, no readout.
        int want = RemainingAppetite;
        int qty = Mathf.Min(_offerCountN, want);
        if (qty <= 0)
        {
            SetResult($"\"I'm full up. Come back later.\"", C_Err);
            Refresh();
            return;
        }

        int leftover = _offerCountN - qty;
        var tier = MushroomRegistry.Tier(_offerSpecies);
        string species = _offerSpecies;
        int credits = pricePerCap * qty;

        _offerSpecies = null;   // the caps left the bar when they were dropped in
        _offerCountN = 0;
        _stage = Stage.Open;
        _counter = 0;
        _ask = 0;

        if (leftover > 0 && Hotbar.Instance != null)
            Hotbar.Instance.AddResource(Hotbar.ItemId.Mushroom, leftover, species);

        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(credits);
        MushroomDealState.RecordSale(_buyerId, pricePerCap, qty, tier, AppetiteMax);
        // Central hook: ANY alien buying advances Tev's onboarding, so no NPC
        // has to remember to wire it up (no-ops outside the quest).
        MushroomQuest.NotifySold(qty);
        // Persistent ledger: bond, deal count (reveals), regular conversion.
        // Scheduled-mode fulfilment reports through DeliverOrder instead.
        BuyerLedger.ReportDeal(_buyerId, tier, pricePerCap, qty,
                               keptAppointment: false, substituted: false);
        _onSold?.Invoke(qty);

        SetResult(leftover > 0
            ? $"{_npcName} took {qty} and paid {credits}. They didn't want the other {leftover}."
            : $"{_npcName} paid {credits} credits.", C_Ok);
        Refresh();
    }

    void BarBuyer()
    {
        MushroomDealState.Bar(_buyerId);
        BuyerLedger.CounterRefused(_buyerId);           // −10 bond, spec §2
        BuyerLedger.CancelAppointmentQuietly(_buyerId); // barred kills any appointment, no halving (spec §9)
        _stage = Stage.Open;
        _counter = 0;
        SetResult($"\"Get away from me.\" — {_npcName} won't deal for 5 minutes.", C_Err);
        Refresh();
    }

    static uint StableHash(string s)
    {
        uint h = 2166136261u;
        if (!string.IsNullOrEmpty(s))
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
        h ^= h >> 16; h *= 2246822507u;
        h ^= h >> 13; h *= 3266489909u;
        h ^= h >> 16;
        return h;
    }

    // ══ moving stock ══════════════════════════════════════════════════════

    Hotbar.Slot[] Bar => Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;

    /// Put whatever's on the cursor onto the table. Mushrooms only, ONE species
    /// at a time — a second species is refused rather than silently merged,
    /// because a mixed pile has no single price to haggle over.
    void DepositToOffer()
    {
        if (!_cursor.IsHeld) return;
        if (_cursor.id != Hotbar.ItemId.Mushroom)
        {
            SetResult("They only want mushrooms.", C_Err);
            return;
        }
        if (Barred) { SetResult($"{_npcName} isn't dealing with you right now.", C_Err); return; }
        if (_offerSpecies != null && _cursor.mushroomSpecies != _offerSpecies)
        {
            SetResult($"One kind at a time — take the {MushroomRegistry.DisplayName(_offerSpecies)} back first.", C_Err);
            return;
        }

        bool wasEmpty = _offerSpecies == null;
        _offerSpecies = _cursor.mushroomSpecies;
        _offerCountN += _cursor.count;
        _cursor = default;

        if (wasEmpty)
        {
            // Seed the ask at MARKET, never at the buyer's hidden rate — that
            // would hand over the number the player is meant to learn.
            _ask = Market;
            // A counter this buyer already made on this strain is still live.
            int parked = MushroomDealState.Counter(_buyerId, _offerSpecies);
            if (parked > 0)
            {
                _counter = parked;
                _stage = Stage.Countered;
                SetResult($"{_npcName} is still offering {parked} a cap.", C_Dim);
            }
        }
        Refresh();
    }

    /// Pick the whole table back up onto the cursor.
    void LiftOffer()
    {
        if (_cursor.IsHeld || !HasOffer) return;
        _cursor = new SlotOps.CursorState
        {
            id = Hotbar.ItemId.Mushroom,
            count = _offerCountN,
            mushroomSpecies = _offerSpecies,
            sourceContainer = Bar,
            sourceIndex = -1,        // no exact origin — ReturnHeldToSource spills to the first empty slot
        };
        _offerSpecies = null;
        _offerCountN = 0;
        _stage = Stage.Open;
        Refresh();
    }

    /// Move ONE cap of the offered species from the bar onto the table.
    void OfferPlusOne()
    {
        if (_offerSpecies == null || Barred) return;
        var bar = Bar;
        if (bar == null) return;
        for (int i = 0; i < bar.Length; i++)
        {
            if (bar[i].id != Hotbar.ItemId.Mushroom) continue;
            if (bar[i].mushroomSpecies != _offerSpecies || bar[i].count <= 0) continue;
            var s = bar[i];
            s.count -= 1;
            bar[i] = s.count > 0 ? s : default;
            _offerCountN += 1;
            Refresh();
            return;
        }
        SetResult("That's all of them.", C_Dim);
        Refresh();
    }

    /// Move ONE cap off the table and back into the bar.
    void OfferMinusOne()
    {
        if (!HasOffer) return;
        var hb = Hotbar.Instance;
        if (hb != null && hb.AddResource(Hotbar.ItemId.Mushroom, 1, _offerSpecies) > 0)
        {
            SetResult("No room in your bar for it.", C_Err);
            return;
        }
        _offerCountN -= 1;
        if (_offerCountN <= 0)
        {
            // Emptied the table — clear the species so a different one can go on,
            // and drop any counter, which was priced against this strain.
            _offerCountN = 0;
            _offerSpecies = null;
            _stage = Stage.Open;
            _counter = 0;
            _ask = 0;
        }
        Refresh();
    }

    void ReturnOfferToBar()
    {
        if (!HasOffer) return;
        var hb = Hotbar.Instance;
        int leftover = hb != null ? hb.AddResource(Hotbar.ItemId.Mushroom, _offerCountN, _offerSpecies) : _offerCountN;
        _offerCountN = leftover;
        if (_offerCountN <= 0) { _offerSpecies = null; _stage = Stage.Open; }
    }

    void OnBarSlotClicked(int i, bool rightClick)
    {
        var bar = Bar;
        if (bar == null || i < 0 || i >= bar.Length) return;
        if (rightClick) SlotOps.HandleRightClick(bar, i, ref _cursor);
        else            SlotOps.HandleLeftClick(bar, i, ref _cursor);
        Refresh();
    }

    // ══ UI refresh ════════════════════════════════════════════════════════

    void Refresh()
    {
        bool countered = _stage == Stage.Countered && _counter > 0 && HasOffer;
        bool barred = Barred;

        // Memory line — the ONLY buyer-specific number the panel may show,
        // because the player earned it by selling to them.
        if (_memoText != null)
        {
            int last = MushroomDealState.LastPaid(_buyerId);
            if (last > 0)
            {
                int qty = MushroomDealState.LastQty(_buyerId);
                string line = $"you remember: paid <color=#FFD732>{last}</color> a cap, took <color=#FFD732>{qty}</color>";
                // Earned notes come from the ledger's reveal schedule: one
                // hidden want per completed deal, fixed order (spec §6). The
                // memo fits the taste pair (fav + disliked); the full list
                // lives in the Messages contact card.
                int reveals = BuyerLedger.RevealCount(_buyerId);
                for (int r = 1; r < reveals && r < 3; r++)
                    line += " · " + BuyerLedger.RevealLine(_buyerId, r);
                _memoText.text = line;
            }
            else _memoText.text = "you've never dealt with them";
        }

        // The table.
        if (HasOffer)
        {
            var tier = MushroomRegistry.Tier(_offerSpecies);
            Color32 tc = MushroomSpecies.TierColor(tier);
            string tierHex = ColorUtility.ToHtmlStringRGB(tc);
            _offerText.text =
                $"<b>{MushroomRegistry.DisplayName(_offerSpecies).ToUpperInvariant()}</b>  <size=13><color=#{tierHex}>{MushroomSpecies.TierName(tier)}</color></size>\n" +
                $"<size=13><color=#7FA0BD>market value <color=#FFD732>{Market}</color> a cap — what {_npcName} pays is up to {_npcName}</color></size>";
            _offerPreview.texture = MushroomRegistry.Preview(_offerSpecies);
            _offerPreview.enabled = _offerPreview.texture != null;
            _offerCount.enabled = true;
            _offerCount.text = _offerCountN.ToString();
            _offerTier.color = tc;
            _offerTier.enabled = true;
        }
        else
        {
            _offerText.text = "<color=#4D6F90>DRAG ONE KIND OF MUSHROOM ONTO THE TABLE</color>";
            _offerPreview.enabled = false;
            _offerCount.enabled = false;
            _offerTier.enabled = false;
        }

        // + only when there's another cap of this species left in the bar.
        bool canAdd = false;
        if (HasOffer && !barred)
        {
            var b = Bar;
            if (b != null)
                for (int i = 0; i < b.Length && !canAdd; i++)
                    canAdd = b[i].id == Hotbar.ItemId.Mushroom
                          && b[i].mushroomSpecies == _offerSpecies && b[i].count > 0;
        }
        SetMini(_offerPlusBtn, canAdd);
        SetMini(_offerMinusBtn, HasOffer);

        // The bar — MUSHROOM STACKS ONLY, packed left. Showing the axe and the
        // water bottle in a mushroom deal implied you could drag them in.
        var bar = Bar;
        int tile = 0;
        if (bar != null)
        {
            for (int i = 0; i < bar.Length && tile < _barSlots.Length; i++)
            {
                if (bar[i].id != Hotbar.ItemId.Mushroom || bar[i].count <= 0) continue;
                var w = _barSlots[tile++];
                w.realIndex = i;
                w.root.gameObject.SetActive(true);
                PaintSlot(w, bar[i]);
            }
            // ONE trailing empty tile, pointing at the first free hotbar slot.
            // Hiding the empty slots (part of "mushrooms only, packed left")
            // also removed the only place to PUT CAPS BACK: you could swap a
            // carried stack with another stack, but there was nowhere to drop it
            // so that nothing was selected. This is that somewhere.
            int free = -1;
            for (int i = 0; i < bar.Length; i++)
                if (bar[i].id == Hotbar.ItemId.None || bar[i].count <= 0) { free = i; break; }
            if (free >= 0 && tile < _barSlots.Length)
            {
                var w = _barSlots[tile++];
                w.realIndex = free;
                w.root.gameObject.SetActive(true);
                PaintSlot(w, default);
            }
        }
        for (int t = tile; t < _barSlots.Length; t++)
        {
            _barSlots[t].realIndex = -1;
            _barSlots[t].root.gameObject.SetActive(false);
        }

        // Counter banner.
        if (_counterPanel != null)
        {
            _counterPanel.gameObject.SetActive(countered);
            if (countered)
                _counterText.text =
                    $"<size=13><color=#7CC4EE>{_npcName.ToUpperInvariant()} COUNTERS</color></size>\n" +
                    $"<size=30><b><color=#7CC4EE>{_counter}</color></b></size><size=13><color=#7FA0BD>  per cap  ·  " +
                    $"<color=#FFD732>{_counter * _offerCountN}</color> for the lot</color></size>";
        }

        // Ask control.
        _suppressInput = true;
        _askInput.text = _ask.ToString();
        _suppressInput = false;
        _totalText.text = $"{Total}";

        // The greed read, in words. Measured against MARKET, never against the
        // buyer — market value is a property of the strain, so this reads the
        // same for every buyer and can't be used to sniff out a generous one.
        float over = (HasOffer && Market > 0) ? (float)_ask / Market : 1f;
        int pct = Mathf.RoundToInt(Mathf.Abs(over - 1f) * 100f);
        string band; Color32 bandCol;
        if (!HasOffer)          { band = ""; bandCol = C_Dim; }
        else if (pct == 0)      { band = "asking exactly market value";                bandCol = new Color32(110, 220, 130, 255); }
        else if (over < 1f)     { band = $"asking {pct}% UNDER market value";           bandCol = new Color32(110, 220, 130, 255); }
        else if (over <= 1.25f) { band = $"asking {pct}% over market value";            bandCol = new Color32(159, 216, 110, 255); }
        else if (over <= 1.60f) { band = $"asking {pct}% over market value — pushing it"; bandCol = new Color32(255, 215, 50, 255); }
        else if (over <= 2.00f) { band = $"asking {pct}% over market value — chancing it"; bandCol = new Color32(255, 154, 60, 255); }
        else                    { band = $"asking {pct}% over market value — absurd";   bandCol = new Color32(255, 110, 110, 255); }
        _riskText.text = band;
        _riskText.color = bandCol;

        // Buttons.
        if (countered)
        {
            SetBtn(_takeBtn, _takeLabel, $"TAKE {_counter}/CAP", C_BtnTake, !barred);
            SetBtn(_primaryBtn, _primaryLabel, $"PUSH FOR {_ask}", C_BtnSell, !barred && _ask > 0);
            SetBtn(_secondaryBtn, _secondaryLabel, "LEAVE IT", C_BtnBack, true);
        }
        else
        {
            SetBtn(_takeBtn, _takeLabel, "", C_BtnTake, false, hide: true);
            SetBtn(_primaryBtn, _primaryLabel, "MAKE THE OFFER", C_BtnSell, HasOffer && !barred && _ask > 0);
            SetBtn(_secondaryBtn, _secondaryLabel, "CLOSE", C_BtnBack, true);
        }

        _cdText.gameObject.SetActive(barred);
        if (barred) _cdText.text = $"{_npcName.ToUpperInvariant()} WON'T DEAL — {MushroomDealState.SecondsLeft(_buyerId)}s";

        RefreshCursorVisual();
    }

    static void SetMini(Button b, bool on)
    {
        if (b == null) return;
        b.interactable = on;
        var img = b.targetGraphic as Image;
        if (img != null) img.color = on ? new Color32(18, 41, 63, 255) : new Color32(12, 24, 36, 255);
    }

    void SetBtn(Button b, TextMeshProUGUI label, string text, Color32 col, bool on, bool hide = false)
    {
        if (b == null) return;
        b.gameObject.SetActive(!hide);
        if (hide) return;
        b.interactable = on;
        label.text = text;
        var img = b.targetGraphic as Image;
        if (img != null) img.color = on ? (Color)col : new Color(col.r / 255f * 0.4f, col.g / 255f * 0.4f, col.b / 255f * 0.4f, 1f);
    }

    void PaintSlot(SlotWidget w, Hotbar.Slot s)
    {
        if (w == null) return;
        bool empty = s.id == Hotbar.ItemId.None || s.count <= 0;
        bool mush = !empty && Hotbar.IsMushroomItem(s.id);
        var tex = mush ? MushroomRegistry.Preview(s.mushroomSpecies) : null;

        w.bg.color = empty ? new Color32(6, 14, 24, 255) : C_SlotBg;
        w.border.color = empty ? C_SlotEdge : C_Border;
        w.preview.texture = tex;
        w.preview.enabled = tex != null;
        w.count.enabled = !empty && s.count > 0;
        if (w.count.enabled) w.count.text = s.count.ToString();

        // Rarity pip: the thing that actually teaches the tiers.
        if (mush)
        {
            w.tier.enabled = true;
            w.tier.color = MushroomSpecies.TierColor(MushroomRegistry.Tier(s.mushroomSpecies));
        }
        else w.tier.enabled = false;
    }

    void RefreshCursorVisual()
    {
        if (_cursorRT == null) return;
        if (!_cursor.IsHeld) { _cursorRT.gameObject.SetActive(false); return; }
        _cursorRT.gameObject.SetActive(true);
        var tex = Hotbar.IsMushroomItem(_cursor.id) ? MushroomRegistry.Preview(_cursor.mushroomSpecies) : null;
        _cursorPreview.texture = tex;
        _cursorPreview.enabled = tex != null;
        _cursorCount.enabled = _cursor.count > 1;
        if (_cursorCount.enabled) _cursorCount.text = _cursor.count.ToString();
    }

    void SetResult(string text, Color32 color)
    {
        if (_resultText == null) return;
        if (_resultRoutine != null) { StopCoroutine(_resultRoutine); _resultRoutine = null; }
        _resultText.text = text;
        _resultText.color = color;
        if (!string.IsNullOrEmpty(text)) _resultRoutine = StartCoroutine(FadeResult());
    }

    IEnumerator FadeResult()
    {
        yield return new WaitForSecondsRealtime(6f);
        if (_resultText != null) _resultText.text = "";
        _resultRoutine = null;
    }

    // ══ construction ══════════════════════════════════════════════════════

    void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Vendor;
        // Belt and braces with OnSceneLoaded: the gate disables this canvas
        // outright on the main menu, so even a state we failed to tear down
        // can't render over the menu.
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
        var dimImg = _dim.AddComponent<Image>();
        dimImg.color = new Color(0, 0, 0, 0.6f);
        _dim.SetActive(false);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        _panelRT = (RectTransform)panel.transform;
        _panelRT.anchorMin = _panelRT.anchorMax = _panelRT.pivot = new Vector2(0.5f, 0.5f);
        _panelRT.sizeDelta = new Vector2(880, 720);
        var bg = panel.AddComponent<Image>();
        bg.color = C_Bg;
        Outline(_panelRT, C_Border);

        _header   = Txt(_panelRT, "// BUYER", new Vector2(0, -18), 820, 30, 24, C_Header, FontStyles.Bold, TextAlignmentOptions.Left);
        _memoText = Txt(_panelRT, "", new Vector2(0, -50), 820, 22, 14, C_Dim, FontStyles.Normal, TextAlignmentOptions.Left);

        // ── the table ──
        var zone = Panel(_panelRT, "OfferZone", new Vector2(0, -84), new Vector2(820, 118), new Color32(6, 16, 27, 255));
        Outline(zone, C_SlotEdge);
        _offerSlotRT = SlotFrame(zone, new Vector2(-820 * 0.5f + 62, 0), 84f, out _offerPreview, out _offerCount, out _offerTier);
        _offerText = Txt(zone, "", new Vector2(70, -26), 600, 76, 17, C_Label, FontStyles.Normal, TextAlignmentOptions.Left);
        var zoneDrag = zone.gameObject.AddComponent<SlotDragProxy>();
        zoneDrag.canBeginDrag = () => !_cursor.IsHeld && HasOffer;
        zoneDrag.beginDrag = LiftOffer;
        zoneDrag.drop = DepositToOffer;
        zoneDrag.returnToSource = () => { SlotOps.ReturnHeldToSource(ref _cursor); Refresh(); };
        var zoneClick = zone.gameObject.AddComponent<ClickRelay>();
        zoneClick.onLeft = () => { if (_cursor.IsHeld) DepositToOffer(); else LiftOffer(); };
        zoneClick.onRight = () => { if (_cursor.IsHeld) DepositToOffer(); };

        // + / − stepping the amount on the table, stacked against the slot's
        // right edge. Parented to the PANEL, not the zone: a Button doesn't
        // implement IBeginDragHandler, so inside the zone a press-and-twitch on
        // one of these would bubble up and start dragging the whole offer out.
        // The zone spans y 84..202 from the panel top; the 84px slot is centred
        // in it at 101..185, so these sit flush with its top and bottom edges.
        _offerPlusBtn  = MkMini(_panelRT, "+", new Vector2(-288, -101), OfferPlusOne);
        _offerMinusBtn = MkMini(_panelRT, "−", new Vector2(-288, -159), OfferMinusOne);

        Txt(_panelRT, "YOUR MUSHROOMS", new Vector2(0, -212), 820, 18, 12, C_Dim, FontStyles.Bold, TextAlignmentOptions.Center);

        // ── the bar: mushroom stacks only ──
        float rowW = HotbarSlots * SlotSize + (HotbarSlots - 1) * SlotGap;
        var row = Panel(_panelRT, "BarRow", new Vector2(0, -234), new Vector2(rowW, SlotSize), new Color(0, 0, 0, 0));
        for (int i = 0; i < HotbarSlots; i++)
        {
            float x = -rowW * 0.5f + i * (SlotSize + SlotGap) + SlotSize * 0.5f;
            _barSlots[i] = BuildBarSlot(row, i, new Vector2(x, 0));
        }
        // No "you're not carrying any mushrooms" line — an empty row already
        // says that, and the table's own prompt says what to do about it.

        // ── counter banner ──
        _counterPanel = Panel(_panelRT, "Counter", new Vector2(0, -340), new Vector2(820, 78), new Color32(14, 39, 64, 255));
        Outline(_counterPanel, new Color32(47, 111, 140, 255));
        _counterText = Txt(_counterPanel, "", new Vector2(0, -8), 780, 66, 14, C_Label, FontStyles.Normal, TextAlignmentOptions.Left);
        _counterPanel.gameObject.SetActive(false);

        // ── ask ──
        // No slider. The number field plus − / + is the whole control: a slider
        // for a value the player wants to set exactly is just a second, worse
        // way to do the same thing, and it made the panel look busier than it is.
        Txt(_panelRT, "ASKING PER CAP", new Vector2(0, -430), 820, 18, 12, C_Dim, FontStyles.Bold, TextAlignmentOptions.Center);

        MkStep(_panelRT, "−", new Vector2(-92, -456), () => { _ask = Mathf.Max(0, _ask - 1); Refresh(); });
        var inputGO = new GameObject("AskInput", typeof(RectTransform));
        inputGO.transform.SetParent(_panelRT, false);
        var inRT = (RectTransform)inputGO.transform;
        inRT.anchorMin = inRT.anchorMax = new Vector2(0.5f, 1f);
        inRT.pivot = new Vector2(0.5f, 1f);
        inRT.sizeDelta = new Vector2(130, 48);
        inRT.anchoredPosition = new Vector2(0, -454);
        var inImg = inputGO.AddComponent<Image>();
        inImg.color = C_SlotBg;
        var itGO = new GameObject("Text", typeof(RectTransform));
        itGO.transform.SetParent(inputGO.transform, false);
        var itRT = (RectTransform)itGO.transform;
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = new Vector2(6, 4); itRT.offsetMax = new Vector2(-6, -4);
        var itTmp = itGO.AddComponent<TextMeshProUGUI>();
        itTmp.fontSize = 22; itTmp.color = C_Value; itTmp.fontStyle = FontStyles.Bold;
        itTmp.alignment = TextAlignmentOptions.Center; itTmp.raycastTarget = false;
        _askInput = inputGO.AddComponent<TMP_InputField>();
        _askInput.textComponent = itTmp;
        _askInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        _askInput.onValueChanged.AddListener(t =>
        {
            if (_suppressInput) return;
            if (!int.TryParse(t, out int v)) v = 0;
            _ask = Mathf.Max(0, v);
            Refresh();
        });
        MkStep(_panelRT, "+", new Vector2(92, -456), () => { _ask += 1; Refresh(); });

        // The greed read, as a SENTENCE rather than an unlabelled coloured bar.
        // The bar said nothing on its own — if the person who commissioned it
        // has to ask what it does, a new player has no chance. Same information,
        // still measured against MARKET (never against the buyer's hidden rate),
        // now readable at a glance.
        _riskText = Txt(_panelRT, "", new Vector2(0, -510), 820, 22, 15, C_Ok, FontStyles.Bold, TextAlignmentOptions.Center);

        Txt(_panelRT, "TOTAL FOR THE LOT", new Vector2(0, -538), 820, 16, 11, C_Dim, FontStyles.Bold, TextAlignmentOptions.Center);
        _totalText = Txt(_panelRT, "0", new Vector2(0, -556), 820, 38, 30, C_Value, FontStyles.Bold, TextAlignmentOptions.Center);

        // result and cooldown BOTH show while barred, so they must not overlap.
        _resultText = Txt(_panelRT, "", new Vector2(0, -596), 820, 26, 17, C_Ok, FontStyles.Bold, TextAlignmentOptions.Center);
        _cdText = Txt(_panelRT, "", new Vector2(0, -624), 820, 22, 14, C_Err, FontStyles.Bold, TextAlignmentOptions.Center);
        _cdText.gameObject.SetActive(false);

        // ── buttons ──
        _takeBtn      = MkBtn(_panelRT, "TakeBtn",  new Vector2(-258, -652), new Vector2(246, 54), C_BtnTake, TakeCounter, out _takeLabel);
        _primaryBtn   = MkBtn(_panelRT, "MainBtn",  new Vector2(0,    -652), new Vector2(246, 54), C_BtnSell, OnPrimary,  out _primaryLabel);
        _secondaryBtn = MkBtn(_panelRT, "BackBtn",  new Vector2(258,  -652), new Vector2(246, 54), C_BtnBack, OnSecondary,out _secondaryLabel);

        VendorMoneyBadge.Attach(_panelRT);
        BuildCursor();

        _panelRT.gameObject.SetActive(false);
    }

    void OnPrimary()
    {
        if (_stage == Stage.Countered) PushBack();
        else MakeOffer();
    }

    void OnSecondary()
    {
        // CLOSE, not "DONE": the button leaves the deal, it doesn't confirm one.
        // "Done" read as "finish the sale" right next to a SELL button.
        if (_stage == Stage.Countered) LeaveIt();
        else Close();
    }

    SlotWidget BuildBarSlot(RectTransform parent, int index, Vector2 pos)
    {
        var w = new SlotWidget();
        w.root = SlotFrame(parent, pos, SlotSize, out w.preview, out w.count, out w.tier, out w.bg, out w.border);

        // Closures read w.realIndex at CALL time, not a captured constant —
        // the tile→hotbar mapping is rebuilt every Refresh as stacks empty out.
        var drag = w.root.gameObject.AddComponent<SlotDragProxy>();
        drag.canBeginDrag = () =>
        {
            var bar = Bar;
            int i = w.realIndex;
            return !_cursor.IsHeld && bar != null && i >= 0 && i < bar.Length
                   && bar[i].id != Hotbar.ItemId.None && bar[i].count > 0;
        };
        drag.beginDrag = () => OnBarSlotClicked(w.realIndex, false);
        drag.drop = () => { if (_cursor.IsHeld) OnBarSlotClicked(w.realIndex, false); };
        drag.returnToSource = () => { SlotOps.ReturnHeldToSource(ref _cursor); Refresh(); };

        var click = w.root.gameObject.AddComponent<ClickRelay>();
        click.onLeft  = () => OnBarSlotClicked(w.realIndex, false);
        click.onRight = () => OnBarSlotClicked(w.realIndex, true);
        return w;
    }

    RectTransform SlotFrame(RectTransform parent, Vector2 pos, float size,
                            out RawImage preview, out TextMeshProUGUI count, out Image tier)
        => SlotFrame(parent, pos, size, out preview, out count, out tier, out _, out _);

    RectTransform SlotFrame(RectTransform parent, Vector2 pos, float size,
                            out RawImage preview, out TextMeshProUGUI count, out Image tier,
                            out Image bgOut, out Image borderOut)
    {
        // Outer rect IS the border; an inset child is the fill. Cheaper and
        // more honest than four edge strips, and — the reason it's done this
        // way — it gives PaintSlot a single Image to recolour for the whole
        // border instead of one strip out of four.
        var rt = Panel(parent, "Slot", pos, new Vector2(size, size), C_SlotEdge);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        borderOut = rt.GetComponent<Image>();
        borderOut.raycastTarget = true;   // the slot's whole hit area

        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(rt, false);
        var fillRT = (RectTransform)fill.transform;
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2, 2); fillRT.offsetMax = new Vector2(-2, -2);
        bgOut = fill.AddComponent<Image>();
        bgOut.color = C_SlotBg;
        bgOut.raycastTarget = false;

        var pv = new GameObject("Preview", typeof(RectTransform));
        pv.transform.SetParent(rt, false);
        var pvRT = (RectTransform)pv.transform;
        pvRT.anchorMin = pvRT.anchorMax = pvRT.pivot = new Vector2(0.5f, 0.5f);
        pvRT.sizeDelta = new Vector2(size - 16f, size - 16f);
        preview = pv.AddComponent<RawImage>();
        preview.raycastTarget = false;
        preview.enabled = false;

        var tp = new GameObject("Tier", typeof(RectTransform));
        tp.transform.SetParent(rt, false);
        var tpRT = (RectTransform)tp.transform;
        tpRT.anchorMin = tpRT.anchorMax = new Vector2(0f, 1f);
        tpRT.pivot = new Vector2(0f, 1f);
        tpRT.sizeDelta = new Vector2(9, 9);
        tpRT.anchoredPosition = new Vector2(4, -4);
        tier = tp.AddComponent<Image>();
        tier.raycastTarget = false;
        tier.enabled = false;

        var ct = new GameObject("Count", typeof(RectTransform));
        ct.transform.SetParent(rt, false);
        var ctRT = (RectTransform)ct.transform;
        ctRT.anchorMin = ctRT.anchorMax = new Vector2(1f, 0f);
        ctRT.pivot = new Vector2(1f, 0f);
        ctRT.sizeDelta = new Vector2(44, 22);
        ctRT.anchoredPosition = new Vector2(-4, 2);
        count = ct.AddComponent<TextMeshProUGUI>();
        count.fontSize = 16; count.fontStyle = FontStyles.Bold;
        count.alignment = TextAlignmentOptions.BottomRight;
        count.color = Color.white; count.raycastTarget = false;
        count.enabled = false;
        return rt;
    }

    void BuildCursor()
    {
        var cur = new GameObject("Cursor", typeof(RectTransform));
        cur.transform.SetParent(transform, false);
        _cursorRT = (RectTransform)cur.transform;
        _cursorRT.anchorMin = _cursorRT.anchorMax = new Vector2(0f, 0f);
        _cursorRT.pivot = new Vector2(0.5f, 0.5f);
        _cursorRT.sizeDelta = new Vector2(64, 64);

        var img = cur.AddComponent<Image>();
        img.color = new Color32(11, 28, 46, 235);
        img.raycastTarget = false;

        var pv = new GameObject("Preview", typeof(RectTransform));
        pv.transform.SetParent(_cursorRT, false);
        var pvRT = (RectTransform)pv.transform;
        pvRT.anchorMin = pvRT.anchorMax = pvRT.pivot = new Vector2(0.5f, 0.5f);
        pvRT.sizeDelta = new Vector2(52, 52);
        _cursorPreview = pv.AddComponent<RawImage>();
        _cursorPreview.raycastTarget = false;

        var ct = new GameObject("Count", typeof(RectTransform));
        ct.transform.SetParent(_cursorRT, false);
        var ctRT = (RectTransform)ct.transform;
        ctRT.anchorMin = ctRT.anchorMax = new Vector2(1f, 0f);
        ctRT.pivot = new Vector2(1f, 0f);
        ctRT.sizeDelta = new Vector2(44, 22);
        ctRT.anchoredPosition = new Vector2(-2, 1);
        _cursorCount = ct.AddComponent<TextMeshProUGUI>();
        _cursorCount.fontSize = 16; _cursorCount.fontStyle = FontStyles.Bold;
        _cursorCount.alignment = TextAlignmentOptions.BottomRight;
        _cursorCount.color = Color.white; _cursorCount.raycastTarget = false;

        cur.SetActive(false);
    }

    // ── tiny UGUI helpers ─────────────────────────────────────────────────

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
        // ALWAYS anchored to the parent rect's top edge, pivot top-centre, so
        // every y in BuildUI reads as "pixels down from the top of the parent".
        //
        // This used to branch on parent.pivot.y, which was simply wrong: a
        // child's anchor is relative to the parent's RECT, and has nothing to do
        // with the parent's own pivot. _panelRT's pivot is (0.5, 0.5), so every
        // text on the panel anchored to its CENTRE instead — "TOTAL FOR THE LOT"
        // at y=-542 landed 182 px BELOW the panel's bottom edge. The Panel()-built
        // pieces were top-anchored and correct, which is why only the labels ran
        // off. Not a resolution or CanvasScaler problem: it was wrong at every
        // resolution, because the whole panel scales as one unit.
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
        label = Txt(rt, "", Vector2.zero, size.x, size.y, 18, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        return btn;
    }

    /// Small square stepper. Returns the Button so Refresh can grey it out.
    Button MkMini(RectTransform parent, string glyph, Vector2 pos, Action onClick)
    {
        var rt = Panel(parent, "Mini" + glyph, pos, new Vector2(26, 26), new Color32(18, 41, 63, 255));
        Outline(rt, C_SlotEdge);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = rt.GetComponent<Image>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke());
        Txt(rt, glyph, Vector2.zero, 26, 26, 18, C_Label, FontStyles.Bold, TextAlignmentOptions.Center);
        return btn;
    }

    void MkStep(RectTransform parent, string glyph, Vector2 pos, Action onClick)
    {
        var rt = Panel(parent, "Step" + glyph, pos, new Vector2(40, 40), new Color32(18, 41, 63, 255));
        Outline(rt, C_SlotEdge);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = rt.GetComponent<Image>();
        btn.onClick.AddListener(() => onClick?.Invoke());
        Txt(rt, glyph, Vector2.zero, 40, 40, 22, C_Label, FontStyles.Bold, TextAlignmentOptions.Center);
    }

    static void Outline(RectTransform parent, Color32 col) { OutlineImage(parent, col); }

    /// Four 1px strips. Returns the top strip so callers that recolour the
    /// border (the slots) have something to hold — the other three are static.
    static Image OutlineImage(RectTransform parent, Color32 col)
    {
        Image Strip(string n, Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 off)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = off;
            var img = go.AddComponent<Image>();
            img.color = col; img.raycastTarget = false;
            return img;
        }
        var t = Strip("EdgeT", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 2), new Vector2(0, -1));
        Strip("EdgeB", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), new Vector2(0, 1));
        Strip("EdgeL", new Vector2(0, 0), new Vector2(0, 1), new Vector2(2, 0), new Vector2(1, 0));
        Strip("EdgeR", new Vector2(1, 0), new Vector2(1, 1), new Vector2(2, 0), new Vector2(-1, 0));
        return t;
    }
}
