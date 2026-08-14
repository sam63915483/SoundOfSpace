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

    // A tape TILE, not a square slot. A mushroom stack was identified by a live
    // species render, so a 72px square was enough; a tape is identified by its
    // NAME, which needs room to be read without picking it up first.
    // 7 x 112 + 6 x 6 = 820, exactly the panel's inner width.
    //
    // COORDINATE CONVENTION - the trap this layout fell into once. Panel() and
    // Txt() both anchor to the parent's TOP edge with pivot top-centre, so a
    // child's y is "pixels DOWN from the top of the parent" and is therefore
    // NEGATIVE. Positioning tile children with centre-relative maths
    // (TileH * 0.5f - 10f = +38) put them 38 px ABOVE the tile instead of just
    // inside it, which is why the counts and the shell art floated free of
    // their tiles. Every y below is negative and measured from the tile top.
    const float TileW = 112f;
    const float TileH = 96f;
    const float TilePad = 8f;
    const float TileArtH = 44f;

    enum Stage { Open, Countered }

    // ── scene refs ────────────────────────────────────────────────────────
    Canvas _canvas;
    RectTransform _panelRT;
    GameObject _dim;
    TextMeshProUGUI _header, _memoText, _offerText, _totalText, _resultText, _riskText, _cdText, _counterText;
    RectTransform _offerSlotRT, _counterPanel;
    RawImage _offerPreview;
    Image _offerArt;
    TextMeshProUGUI _offerCount;
    Image _offerTier;
    // CAPS + PRICE sliders — the same control the Messages app haggles with,
    // built from DealSliderKit so the two negotiation screens stay identical in
    // feel. They replaced a TMP_InputField + four ± steppers.
    UnityEngine.UI.Slider _askSlider;
    TextMeshProUGUI _askHandleLabel;
    Button _primaryBtn, _takeBtn, _secondaryBtn;
    TextMeshProUGUI _primaryLabel, _takeLabel, _secondaryLabel;
    SlotWidget[] _barSlots = new SlotWidget[HotbarSlots];
    RectTransform _cursorRT;
    RawImage _cursorPreview;
    TextMeshProUGUI _cursorCount;

    // ── deal state ────────────────────────────────────────────────────────
    string _npcName;
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
    // Guards the sliders' onValueChanged while Refresh writes their values back
    // from state. _ask / _offerCountN stay the source of truth; the sliders are
    // only ever a view onto them.
    bool _suppressInput;
    // Scheduled-deal mode (Messages app appointment): price is agreed, the
    // haggle is off, and DELIVER runs the exact/substitution flow instead.
    bool _scheduled;
    BuyerLedger.Buyer _appt;

    class SlotWidget
    {
        public RectTransform root;
        public Image bg, border, tier;
        public RawImage preview;
        // The cassette shell. An Image, not the RawImage above: the art is a
        // generated SPRITE and RawImage only speaks Texture — which is exactly
        // why the slots drew empty on the first conversion.
        public Image art;
        public TextMeshProUGUI nameLbl, genreLbl;
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
    /// <param name="onSold">Number of tapes sold, each time a deal closes.</param>
    /// <summary>
    /// Open the sell table for an alien. Takes their IDENTITY rather than a
    /// mushroom price component — the tape economy prices from AlienTaste, and
    /// the panel no longer knows what a strain is.
    /// </summary>
    public void Open(string npcName, string alienId, Action onClose, Action<int> onSold = null)
    {
        _npcName = string.IsNullOrEmpty(npcName) ? "Buyer" : npcName;
        _buyerId = string.IsNullOrEmpty(alienId) ? _npcName : alienId;
        _onClose = onClose;
        _onSold = onSold;
        _open = true;
        _cursor = default;
        _offerSpecies = null;
        _offerCountN = 0;
        _ask = 0;
        _stage = Stage.Open;
        _counter = 0;

        // A live appointment flips the panel into delivery mode. The ask is
        // seeded at the AGREED price but stays editable — you can re-ask at
        // the meetup, at the risk of the whole delivery (spec update
        // 2026-08-07, Sam's playtest).
        var ledger = BuyerLedger.Get(_buyerId);
        _scheduled = ledger != null && ledger.convo == BuyerLedger.Convo.Scheduled
                     && Time.unscaledTime <= ledger.deadline + BuyerDeals.GraceSeconds;
        _appt = _scheduled ? ledger : null;
        if (_scheduled) _ask = _appt.offerPerCap;

        if (_dim != null) _dim.SetActive(true);
        _panelRT.gameObject.SetActive(true);
        PlayerController.isInModalSlotUI = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_header != null)
            _header.text = $"// {_npcName.ToUpperInvariant()}  <size=13><color=#7FA0BD>BOND {BuyerLedger.BondPips(_buyerId)}</color></size>";
        SetResult("", C_Ok);
        Refresh();
    }

    public void Close()
    {
        if (!_open) return;
        TraxTapePlayer.StopAll();

        // Never eat the player's stock. Anything on the table or on the cursor
        // goes back in the bar before the panel closes.
        ReturnOfferToBar();
        if (_cursor.IsHeld)
        {
            var hb = Hotbar.Instance;
            if (hb != null)
                hb.AddResource(_cursor.id, _cursor.count,
                               _cursor.id == Hotbar.ItemId.Cassette ? _cursor.cassetteId
                                                                    : _cursor.mushroomSpecies);
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
        _scheduled = false;
        _appt = null;
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

    /// The tape on the table, or null.
    TraxPrints.Record Press => _offerSpecies != null ? TraxPrints.Get(_offerSpecies) : null;

    /// How well THIS buyer's ear matches what is on the table. Everything below
    /// hangs off it, exactly as the in-person offer flow does.
    double Satisfaction
    {
        get
        {
            var rec = Press;
            return rec == null ? 0.0
                 : AlienTaste.Satisfaction(_buyerId, TapeTrade.DialsOf(rec.track));
        }
    }

    /// PUBLIC value — floor plus arrangement. The player can work this out from
    /// the console, which is what makes naming a price a judgement rather than
    /// a guess.
    int Market
    {
        get
        {
            var rec = Press;
            return rec == null ? 0
                 : Mathf.Max(1, Mathf.RoundToInt((float)TapeValue.Base(rec.track.ActiveCount(), rec.tier)));
        }
    }

    /// What this buyer privately thinks it is worth. Never leaves this file.
    int Fair
    {
        get
        {
            var rec = Press;
            if (rec == null) return Market;
            var led = BuyerLedger.Get(_buyerId);
            return TapeValue.For(rec.track.ActiveCount(), rec.tier, Satisfaction,
                                 led != null ? led.bond : 0, false,
                                 AlienTaste.PayFactor(_buyerId));
        }
    }

    float Patience => (float)AlienTaste.Patience(_buyerId);
    int Total  => _ask * _offerCountN;
    bool Barred => MushroomDealState.IsBarred(_buyerId);
    bool HasOffer => _offerSpecies != null && _offerCountN > 0;

    // ══ the deal ══════════════════════════════════════════════════════════

    void MakeOffer()
    {
        if (!HasOffer || Barred || _ask <= 0) return;

        // ── DO THEY EVEN WANT IT? ────────────────────────────────────────
        // Price is the second question. The first is whether this song is for
        // them at all, and it is the same gate the taste model applies
        // everywhere else — without it the panel would haggle happily over a
        // track the buyer hates, and the whole genre system would only ever
        // move the price rather than decide the sale.
        var rec = Press;
        if (rec != null)
        {
            double[] dials = TapeTrade.DialsOf(rec.track);
            uint variant = StableHash(_buyerId + ":" + _offerSpecies);

            if (TapeMemory.HasHeard(_buyerId, dials))
            {
                SetResult($"\"{AlienFeedback.ForRepeat(variant)}\"", C_Err);
                return;
            }

            double sat = Satisfaction;
            var verdict = AlienTaste.Gate(sat);
            bool liked = verdict == AlienTaste.Verdict.Liked
                      || (verdict == AlienTaste.Verdict.CoinFlip && UnityEngine.Random.value < 0.5f);
            if (!liked)
            {
                // They have now heard it, so re-offering the same song is a
                // repeat even though no money changed hands.
                TapeMemory.Remember(_buyerId, dials);
                SetResult($"\"{AlienFeedback.ForRejection(_buyerId, dials, AlienTaste.FavouriteGenre(_buyerId), variant)}\"", C_Err);
                Refresh();
                return;
            }
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
            SetResult($"\"{_ask} a tape? Not a chance. I'll do {_counter}.\"", C_Label);
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

    // Tapes have no appetite: a song is not produce and nobody fills up on it.
    int AppetiteMax => 999;
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
        var soldRec = Press;
        int soldGenre = GenreIndexOf(soldRec);
        bool matchedTaste = soldGenre == AlienTaste.FavouriteGenreIndex(_buyerId);
        string species = _offerSpecies;
        int credits = pricePerCap * qty;

        _offerSpecies = null;   // the caps left the bar when they were dropped in
        _offerCountN = 0;
        _stage = Stage.Open;
        _counter = 0;
        _ask = 0;

        if (leftover > 0 && Hotbar.Instance != null)
            Hotbar.Instance.AddResource(Hotbar.ItemId.Cassette, leftover, species);

        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(credits);
        // The money and the caps have already changed hands on THIS machine —
        // that half is personal and has to be instant. The buyer's half is
        // shared: appetite so your friend finds them full, bond, last paid.
        // RecordSale is pure arithmetic so it runs here too and the panel
        // updates immediately; ReportDeal ROLLS for the regular conversion, so
        // on a guest only the host may run it (see below).
        // Tapes have no appetite, so there is no saturation to record.
        bool sentToHost = false;
        // Central hook: ANY alien buying advances Tev's onboarding, so no NPC
        // has to remember to wire it up (no-ops outside the quest).
        NotifyTapeSold(species, soldRec, qty);
        // Persistent ledger: bond, deal count (reveals), regular conversion.
        // Scheduled-mode fulfilment reports through DeliverOrder instead.
        if (!sentToHost)
            BuyerLedger.ReportTapeDeal(_buyerId, soldGenre, pricePerCap, qty,
                                       keptAppointment: false, matchedTaste: matchedTaste);
        _onSold?.Invoke(qty);

        SetResult(leftover > 0
            ? $"{_npcName} took {qty} and paid {credits}. They didn't want the other {leftover}."
            : $"{_npcName} paid {credits} credits.", C_Ok);
        Refresh();
    }

    /// Which genre a pressing classifies as, as an index. Resolved BY NAME
    /// against the same table TapeTrade.Fills compares on, so an order check
    /// and a bond award can never disagree about what a song is.
    static int GenreIndexOf(TraxPrints.Record rec)
    {
        if (rec == null) return 0;
        string name = TraxClassifier.Classify(rec.track.dials).primary.name;
        var g = TraxClassifier.Genres;
        for (int i = 0; i < g.Length; i++) if (g[i].name == name) return i;
        return 0;
    }

    /// Everything that has to happen when a tape changes hands, wherever the
    /// sale came from — so the walk-up path and the delivery path cannot drift.
    void NotifyTapeSold(string printId, TraxPrints.Record rec, int qty)
    {
        // Selling one of TEV'S tapes advances his onboarding and works the lawn
        // off. Central, so no NPC has to remember to wire it up.
        if (TevDemoTapes.IsTevTape(printId))
        {
            MushroomQuest.SoldCount += qty;
            for (int i = 0; i < qty; i++) MushroomQuest.NotifyTevTapeSold();
        }
        // A song they have heard is a song they will not buy again.
        if (rec != null) TapeMemory.Remember(_buyerId, TapeTrade.DialsOf(rec.track));
    }

    void BarBuyer()
    {
        // Bar locally either way so the row greys out at once; the bond hit is
        // the host's to apply, so a guest reports instead of double-docking it.
        MushroomDealState.Bar(_buyerId);
        if (!EconomySync.ReportBarred(_buyerId))
        {
            BuyerLedger.CounterRefused(_buyerId);           // −10 bond, spec §2
            BuyerLedger.CancelAppointmentQuietly(_buyerId); // barred kills any appointment, no halving (spec §9)
        }
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
        if (_cursor.id != Hotbar.ItemId.Cassette)
        {
            SetResult("They only want tapes.", C_Err);
            return;
        }
        if (Barred) { SetResult($"{_npcName} isn't dealing with you right now.", C_Err); return; }
        if (_offerSpecies != null && _cursor.cassetteId != _offerSpecies)
        {
            SetResult($"One song at a time — take {TraxPrints.DisplayName(_offerSpecies)} back first.", C_Err);
            return;
        }

        bool wasEmpty = _offerSpecies == null;
        _offerSpecies = _cursor.cassetteId;

        // PUT IT ON AND LISTEN. Dropping a tape on the table plays it, because
        // the buyer deciding what it is worth without hearing it would be
        // absurd — and because the player should hear what they are selling at
        // the moment they are pricing it.
        if (wasEmpty) PlayOnTable(_offerSpecies);
        _offerCountN += _cursor.count;
        _cursor = default;

        if (wasEmpty && _scheduled && _appt != null)
        {
            // Delivery mode: the ask stays seeded at the AGREED price — the
            // market seed below would silently clobber the deal's number.
            _ask = _appt.offerPerCap;
        }
        else if (wasEmpty)
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
                SetResult($"{_npcName} is still offering {parked}.", C_Dim);
            }
        }
        Refresh();
    }

    /// Pick the whole table back up onto the cursor.
    /// <summary>
    /// Put THIS tape on, rather than toggling it.
    ///
    /// TraxTapePlayer.TogglePersonal STOPS the tape when the print asked for is
    /// already the one playing — correct for the hotbar's hold-LMB walkman,
    /// where one button has to do both jobs, and wrong here. Lifting a tape off
    /// the table and dropping it straight back would have silenced it instead
    /// of replaying it, which reads as "sometimes the tape just doesn't play".
    /// </summary>
    static void PlayOnTable(string printId)
    {
        if (string.IsNullOrEmpty(printId)) return;
        if (TraxTapePlayer.IsPlayingPrint(printId)) return;
        if (TraxPrints.Get(printId) == null) return;
        TraxTapePlayer.TogglePersonal(null, printId);
    }

    void LiftOffer()
    {
        if (_cursor.IsHeld || !HasOffer) return;
        // Off the table, off the speakers. The tape plays BECAUSE it is sitting
        // in front of the buyer, so it has no business carrying on once it is
        // in your hand.
        if (TraxTapePlayer.IsPlayingPrint(_offerSpecies)) TraxTapePlayer.StopAll();
        _cursor = new SlotOps.CursorState
        {
            id = Hotbar.ItemId.Cassette,
            count = _offerCountN,
            cassetteId = _offerSpecies,
            sourceContainer = Bar,
            sourceIndex = -1,        // no exact origin — ReturnHeldToSource spills to the first empty slot
        };
        _offerSpecies = null;
        _offerCountN = 0;
        _stage = Stage.Open;
        Refresh();
    }

    /// Caps of the offered species still sitting in the player's bar. The CAPS
    /// slider's headroom — its max is this plus whatever is already on the table.
    ///
    /// Bounded by STOCK only, never by the buyer's remaining appetite: appetite
    /// is hidden information the player is meant to discover by overshooting and
    /// being handed the excess back, and a slider that stopped at their exact
    /// limit would print that secret on screen.
    int BarStockOfOfferSpecies()
    {
        var bar = Bar;
        if (bar == null || _offerSpecies == null) return 0;
        int n = 0;
        for (int i = 0; i < bar.Length; i++)
            if (bar[i].id == Hotbar.ItemId.Cassette
                && bar[i].cassetteId == _offerSpecies && bar[i].count > 0)
                n += bar[i].count;
        return n;
    }

    /// Move ONE cap of the offered species from the bar onto the table.
    /// Returns false when the bar has none left.
    bool OfferPlusOne()
    {
        if (_offerSpecies == null || Barred) return false;
        var bar = Bar;
        if (bar == null) return false;
        for (int i = 0; i < bar.Length; i++)
        {
            if (bar[i].id != Hotbar.ItemId.Cassette) continue;
            if (bar[i].cassetteId != _offerSpecies || bar[i].count <= 0) continue;
            var s = bar[i];
            s.count -= 1;
            bar[i] = s.count > 0 ? s : default;
            _offerCountN += 1;
            return true;
        }
        return false;
    }

    /// Move ONE cap off the table and back into the bar.
    /// Returns false when the bar has no room to take it.
    bool OfferMinusOne()
    {
        if (!HasOffer) return false;
        var hb = Hotbar.Instance;
        if (hb != null && hb.AddResource(Hotbar.ItemId.Cassette, 1, _offerSpecies) > 0)
            return false;
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
        return true;
    }

    /// Drive the pile on the table to `target` caps.
    ///
    /// The CAPS slider routes through here rather than assigning _offerCountN,
    /// because the count is not a number — it is a PHYSICAL transfer. Caps
    /// genuinely leave the hotbar array when they go on the table, and that is
    /// what Close()/ReturnOfferToBar() rely on to give them back. Setting the
    /// field directly would duplicate or destroy the player's mushrooms.
    ///
    /// Steps one at a time so both failure cases the single-step paths already
    /// handle (bar empty, bar full) stop the loop honestly instead of desyncing
    /// the slider from the pile.
    void SetOfferCount(int target)
    {
        if (Barred) { Refresh(); return; }
        target = Mathf.Max(0, target);

        int guard = 0;   // belt-and-braces against a step that reports success without moving
        while (_offerCountN < target && guard++ < 512)
        {
            if (!OfferPlusOne()) { SetResult("That's all of them.", C_Dim); break; }
        }
        while (_offerCountN > target && guard++ < 512)
        {
            if (!OfferMinusOne()) { SetResult("No room in your bar for it.", C_Err); break; }
        }
        Refresh();
    }

    void ReturnOfferToBar()
    {
        if (!HasOffer) return;
        var hb = Hotbar.Instance;
        int leftover = hb != null ? hb.AddResource(Hotbar.ItemId.Cassette, _offerCountN, _offerSpecies) : _offerCountN;
        _offerCountN = leftover;
        if (_offerCountN <= 0) { _offerSpecies = null; _stage = Stage.Open; }
    }

    /// <summary>
    /// Click a tile and exactly ONE of that tape goes on the table.
    ///
    /// No dragging and no quantity: a tape is a specific song, so an alien
    /// buying two copies of it is meaningless (Sam's call). Copies exist to
    /// sell the same song to DIFFERENT people, which the per-alien repeat rule
    /// is what forces — so the shelf is a chooser, not a stack you portion out.
    /// </summary>
    void OnBarSlotClicked(int i, bool rightClick)
    {
        var bar = Bar;
        if (bar == null || i < 0 || i >= bar.Length) return;

        // HOLDING A TAPE? Then this is a PUT, not a pick.
        //
        // Sam dragged a tape off the table onto an empty tile and the shelf
        // refused it. This handler is wired to the tile's drop as well as its
        // click, and it used to fall straight through to the select path below
        // — which bails on an empty slot, so the drop silently bounced back to
        // the table. Deferring to SlotOps means the shelf follows the same
        // deposit / merge / swap rules as every other container in the game,
        // cassette variants included, rather than inventing its own.
        if (_cursor.IsHeld)
        {
            if (rightClick) SlotOps.HandleRightClick(bar, i, ref _cursor);
            else            SlotOps.HandleLeftClick(bar, i, ref _cursor);
            Refresh();
            return;
        }

        if (Barred) { SetResult($"{_npcName} isn't dealing with you right now.", C_Err); return; }

        Hotbar.Slot slot = bar[i];
        if (slot.id != Hotbar.ItemId.Cassette || slot.count <= 0) return;

        // Clicking the one already on the table is a no-op rather than a
        // second copy — there is no second copy to want.
        if (_offerSpecies == slot.cassetteId && _offerCountN > 0) return;

        // Swap: whatever was on the table goes home first, so the player never
        // has to clear it manually to look at something else.
        if (HasOffer) ReturnOfferToBar();

        _offerSpecies = slot.cassetteId;
        _offerCountN = 0;
        _stage = Stage.Open;
        _counter = 0;
        if (!OfferPlusOne()) { _offerSpecies = null; Refresh(); return; }

        // Put it on. The buyer pricing a song without hearing it would be
        // absurd, and the player should hear what they are pricing.
        PlayOnTable(_offerSpecies);

        _ask = Mathf.Max(1, Market);   // seed at market so the slider starts honest
        SetResult("", C_Ok);
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
                string line = $"you remember: paid <color=#FFD732>{last}</color> for a tape";
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
            var rec = Press;
            // The GENRE is a tape's tier: it is what the buyer cares about and
            // what the console already told the player this song is.
            string genre = rec != null
                ? TraxClassifier.Classify(rec.track.dials).primary.name : "";
            Color32 tc = rec != null && rec.tier >= 2
                ? new Color32(0xFF, 0x4F, 0xD8, 0xFF)     // Type 2 shell
                : new Color32(0x79, 0xFF, 0xD0, 0xFF);    // Type 1
            string tierHex = ColorUtility.ToHtmlStringRGB(tc);
            string title = TraxPrints.DisplayName(_offerSpecies).ToUpperInvariant();
            string typeWord = rec != null && rec.tier >= 2 ? "TYPE 2" : "TYPE 1";
            if (_scheduled && _appt != null)
            {
                string want = TapeTrade.GenreName(_appt.askTier);
                int bump = Mathf.RoundToInt((BuyerDeals.GratitudeBonus(_appt.windowMinutes) - 1f) * 100f);
                _offerText.text =
                    $"<b>ORDER</b> — {_appt.askQty} <color=#{tierHex}>{want}</color> @ <color=#FFD732>{_appt.offerPerCap}</color> each agreed" +
                    $"  <size=13><color=#6EDC82>on time (+{bump}%)</color></size>\n" +
                    $"<size=13><color=#7FA0BD>on the table: {title}  <color=#{tierHex}>{genre}</color></color></size>";
            }
            else
            _offerText.text =
                $"<b>{title}</b>  <size=13><color=#{tierHex}>{genre} · {typeWord}</color></size>\n" +
                $"<size=13><color=#7FA0BD>market value <color=#FFD732>{Market}</color> a tape — what {_npcName} pays is up to {_npcName}</color></size>\n" +
                $"<size=12><color=#6EDC82>NOW PLAYING</color></size>";
            // No live render for a cassette: the shell sprite IS the art.
            _offerPreview.enabled = false;
            if (_offerArt != null)
            {
                _offerArt.enabled = true;
                _offerArt.sprite = Hotbar.CassetteSpriteWideFor(_offerSpecies);
            }
            _offerCount.enabled = true;
            _offerCount.text = _offerCountN.ToString();
            _offerTier.color = tc;
            _offerTier.enabled = true;
        }
        else
        {
            _offerText.text = _scheduled && _appt != null
                ? $"<b>ORDER</b> — {_appt.askQty} {TapeTrade.GenreName(_appt.askTier)} @ <color=#FFD732>{_appt.offerPerCap}</color> each agreed\n" +
                  "<color=#4D6F90>CLICK THE TAPE TO PUT IT ON THE TABLE</color>"
                : "<color=#4D6F90>CLICK A TAPE TO PUT IT ON THE TABLE</color>";
            _offerPreview.enabled = false;
            if (_offerArt != null) _offerArt.enabled = false;
            _offerCount.enabled = false;
            _offerTier.enabled = false;
        }

        RefreshSliders(barred);

        // The shelf — TAPES ONLY, packed left. Showing the axe and the water
        // bottle in a tape deal implied you could drag them in.
        var bar = Bar;
        int tile = 0;
        if (bar != null)
        {
            for (int i = 0; i < bar.Length && tile < _barSlots.Length; i++)
            {
                if (bar[i].id != Hotbar.ItemId.Cassette || bar[i].count <= 0) continue;
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
            // Must be a slot that would actually TAKE a tape. The money slot
            // is empty until you earn anything and takes money only, so the
            // naive "first empty" search could point this tile at a slot that
            // silently refuses every drop.
            int free = -1;
            for (int i = 0; i < bar.Length; i++)
            {
                if (bar[i].id != Hotbar.ItemId.None && bar[i].count > 0) continue;
                if (!Hotbar.SlotAccepts(bar, i, Hotbar.ItemId.Cassette)) continue;
                free = i;
                break;
            }
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

        _totalText.text = $"{Total}";

        // The greed read, in words. Measured against MARKET, never against the
        // buyer — market value is a property of the strain, so this reads the
        // same for every buyer and can't be used to sniff out a generous one.
        float over = (HasOffer && Market > 0) ? (float)_ask / Market : 1f;
        int pct = Mathf.RoundToInt(Mathf.Abs(over - 1f) * 100f);
        string band; Color32 bandCol;
        if (_scheduled && _appt != null)
        {
            // Delivery read instead: does the table (and the price) match
            // the order?
            var rec2 = Press;
            bool goodsOk = HasOffer && rec2 != null
                        && TapeTrade.Fills(rec2.track, _appt.askTier)
                        && _offerCountN >= _appt.askQty;
            bool priceOk = _ask <= _appt.offerPerCap;
            if (!HasOffer) { band = ""; bandCol = C_Dim; }
            else if (goodsOk && _ask == _appt.offerPerCap)
            { band = "exactly as agreed"; bandCol = new Color32(110, 220, 130, 255); }
            else if (goodsOk && priceOk)
            { band = "as ordered, under the agreed price"; bandCol = new Color32(110, 220, 130, 255); }
            else if (goodsOk)
            { band = $"asking over the agreed {_appt.offerPerCap} — they may walk"; bandCol = new Color32(255, 154, 60, 255); }
            else if (priceOk)
            { band = "not what they ordered — they might take it anyway"; bandCol = new Color32(255, 215, 50, 255); }
            else
            { band = "wrong goods AND over the agreed price — long odds"; bandCol = new Color32(255, 110, 110, 255); }
        }
        else if (!HasOffer)     { band = ""; bandCol = C_Dim; }
        else if (pct == 0)      { band = "asking exactly market value";                bandCol = new Color32(110, 220, 130, 255); }
        else if (over < 1f)     { band = $"asking {pct}% UNDER market value";           bandCol = new Color32(110, 220, 130, 255); }
        else if (over <= 1.25f) { band = $"asking {pct}% over market value";            bandCol = new Color32(159, 216, 110, 255); }
        else if (over <= 1.60f) { band = $"asking {pct}% over market value — pushing it"; bandCol = new Color32(255, 215, 50, 255); }
        else if (over <= 2.00f) { band = $"asking {pct}% over market value — chancing it"; bandCol = new Color32(255, 154, 60, 255); }
        else                    { band = $"asking {pct}% over market value — absurd";   bandCol = new Color32(255, 110, 110, 255); }
        _riskText.text = band;
        _riskText.color = bandCol;

        // Buttons.
        if (_scheduled)
        {
            SetBtn(_takeBtn, _takeLabel, "", C_BtnTake, false, hide: true);
            SetBtn(_primaryBtn, _primaryLabel, "DELIVER", C_BtnSell, HasOffer);
            SetBtn(_secondaryBtn, _secondaryLabel, "CLOSE", C_BtnBack, true);
        }
        else if (countered)
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

    /// Push state INTO the two sliders. They are a view, never the truth —
    /// _offerCountN and _ask stay authoritative, so every existing reset site
    /// (CloseSale, CompleteScheduled, HardHide, OfferMinusOne) keeps working
    /// untouched and the sliders just follow on the next Refresh.
    ///
    /// Writes are wrapped in _suppressInput because SetValueWithoutNotify alone
    /// isn't enough: clamping a value by changing minValue/maxValue DOES fire
    /// onValueChanged, which would re-enter SetOfferCount mid-Refresh.
    void RefreshSliders(bool barred)
    {
        if (_askSlider == null) return;

        _suppressInput = true;

        // ── PRICE ──
        // Anchored on MARKET in a walk-up (which is what the risk wording below
        // is measured against, so the thumb's position and the sentence agree),
        // and on the AGREED price in scheduled mode, where over-asking is what
        // risks the delivery. Half market to double it spans the whole existing
        // band table, from "asking N% UNDER market" to "absurd".
        int anchor = (_scheduled && _appt != null) ? Mathf.Max(1, _appt.offerPerCap) : Mathf.Max(1, Market);
        int askMin = HasOffer ? Mathf.Max(1, Mathf.RoundToInt(anchor * 0.5f)) : 0;
        int askMax = HasOffer ? Mathf.Max(askMin + 1, Mathf.RoundToInt(anchor * 2f)) : 1;
        _askSlider.minValue = askMin;
        _askSlider.maxValue = askMax;
        _askSlider.SetValueWithoutNotify(Mathf.Clamp(_ask, askMin, askMax));
        _askSlider.interactable = HasOffer && !barred;
        if (_askHandleLabel != null) _askHandleLabel.text = _ask.ToString();

        _suppressInput = false;
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
        bool tape = !empty && s.id == Hotbar.ItemId.Cassette;

        w.bg.color = empty ? new Color32(6, 14, 24, 255) : C_SlotBg;
        w.border.color = empty ? C_SlotEdge : C_Border;
        if (w.preview != null) w.preview.enabled = false;   // tiles draw a sprite, not a render

        // THE SHELL. This is the bug Sam hit: the art is a generated SPRITE,
        // and the old slot drew through a RawImage, which only speaks Texture.
        if (w.art != null)
        {
            w.art.enabled = tape;
            if (tape) w.art.sprite = Hotbar.CassetteSpriteWideFor(s.cassetteId);
        }

        // NAME AND GENRE, always visible. The whole reason a tape needs a tile
        // rather than a square: you choose between songs by name, and having to
        // pick one up to find out which it was made the shelf unreadable.
        if (w.nameLbl != null)
        {
            w.nameLbl.enabled = tape;
            if (tape) w.nameLbl.text = TraxPrints.DisplayName(s.cassetteId).ToUpperInvariant();
        }
        if (w.genreLbl != null)
        {
            w.genreLbl.enabled = tape;
            if (tape)
            {
                var rec = TraxPrints.Get(s.cassetteId);
                string g = rec != null ? TraxClassifier.Classify(rec.track.dials).primary.name : "";
                w.genreLbl.text = g + (rec != null && rec.tier >= 2 ? "   T2" : "   T1");
            }
        }

        w.count.enabled = !empty && s.count > 0;
        if (w.count.enabled) w.count.text = "x" + s.count;

        // Tier pip: Type 1 phosphor vs Type 2 magenta, which is what teaches
        // the two shells apart at a glance.
        if (tape)
        {
            w.tier.enabled = true;
            w.tier.color = TraxPrints.TierOf(s.cassetteId) >= 2
                ? new Color32(0xFF, 0x4F, 0xD8, 0xFF)
                : new Color32(0x79, 0xFF, 0xD0, 0xFF);
        }
        else w.tier.enabled = false;
    }

    void RefreshCursorVisual()
    {
        if (_cursorRT == null) return;
        if (!_cursor.IsHeld) { _cursorRT.gameObject.SetActive(false); return; }
        _cursorRT.gameObject.SetActive(true);
        RenderTexture tex = null;   // cassettes render as their shell sprite, not a preview
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
        _panelRT.sizeDelta = new Vector2(880, 646);
        var bg = panel.AddComponent<Image>();
        bg.color = C_Bg;
        Outline(_panelRT, C_Border);

        _header   = Txt(_panelRT, "// BUYER", new Vector2(0, -18), 820, 30, 24, C_Header, FontStyles.Bold, TextAlignmentOptions.Left);
        _memoText = Txt(_panelRT, "", new Vector2(0, -50), 820, 22, 14, C_Dim, FontStyles.Normal, TextAlignmentOptions.Left);

        // ── the table ──
        var zone = Panel(_panelRT, "OfferZone", new Vector2(0, -84), new Vector2(820, 118), new Color32(6, 16, 27, 255));
        Outline(zone, C_SlotEdge);
        _offerSlotRT = SlotFrame(zone, new Vector2(-820 * 0.5f + 62, 0), 84f, out _offerPreview, out _offerCount, out _offerTier);
        // The shell on the table. Same reason as the tiles: the art is a
        // generated sprite, and the RawImage above cannot draw one.
        var offerArtRT = Panel(_offerSlotRT, "Art", new Vector2(0, -18f), new Vector2(74, 48), Color.white);
        _offerArt = offerArtRT.GetComponent<Image>();
        _offerArt.raycastTarget = false;
        _offerArt.preserveAspect = true;
        _offerText = Txt(zone, "", new Vector2(70, -26), 600, 76, 17, C_Label, FontStyles.Normal, TextAlignmentOptions.Left);
        var zoneDrag = zone.gameObject.AddComponent<SlotDragProxy>();
        zoneDrag.canBeginDrag = () => !_cursor.IsHeld && HasOffer;
        zoneDrag.beginDrag = LiftOffer;
        zoneDrag.drop = DepositToOffer;
        zoneDrag.returnToSource = () => { SlotOps.ReturnHeldToSource(ref _cursor); Refresh(); };
        var zoneClick = zone.gameObject.AddComponent<ClickRelay>();
        zoneClick.onLeft = () => { if (_cursor.IsHeld) DepositToOffer(); else LiftOffer(); };
        zoneClick.onRight = () => { if (_cursor.IsHeld) DepositToOffer(); };

        // The ± minis that used to sit against the slot's right edge are gone —
        // the CAPS slider below does that job now, and does it in one drag.

        Txt(_panelRT, "YOUR TAPES", new Vector2(0, -356), 820, 18, 12, C_Dim, FontStyles.Bold, TextAlignmentOptions.Center);

        // ── the shelf: one tile per tape you are carrying ──
        float rowW = HotbarSlots * TileW + (HotbarSlots - 1) * SlotGap;
        var row = Panel(_panelRT, "BarRow", new Vector2(0, -380), new Vector2(rowW, TileH), new Color(0, 0, 0, 0));
        for (int i = 0; i < HotbarSlots; i++)
        {
            float x = -rowW * 0.5f + i * (TileW + SlotGap) + TileW * 0.5f;
            _barSlots[i] = BuildBarSlot(row, i, new Vector2(x, 0));
        }
        // No "you're not carrying any tapes" line — an empty row already says
        // that, and the table's own prompt says what to do about it.

        // ── counter banner ──
        _counterPanel = Panel(_panelRT, "Counter", new Vector2(0, -208), new Vector2(820, 66), new Color32(14, 39, 64, 255));
        Outline(_counterPanel, new Color32(47, 111, 140, 255));
        _counterText = Txt(_counterPanel, "", new Vector2(0, -8), 780, 66, 14, C_Label, FontStyles.Normal, TextAlignmentOptions.Left);
        _counterPanel.gameObject.SetActive(false);

        // ── the deal: CAPS + PRICE, on the same sliders the phone haggles with ──
        //
        // This used to be a number field with ± steppers, on the argument that a
        // slider is a worse way to set a value you want exact. That argument
        // loses to a bigger one: the Messages app negotiates the identical pair
        // of numbers on sliders, and two different controls for one mechanic is
        // the confusing part. Precision survives — both sliders are whole-number
        // and the live value is printed inside the thumb, so a drag lands on an
        // exact figure and the arrow keys nudge by one.
        //
        // The rows come from DealSliderKit, skinned to this panel's blue-teal
        // palette rather than the phone's greys.
        var sliderStyle = DealSliderKit.Style.VendorPanel();
        // Widen the inset so the track stops short of the panel edge. The kit
        // stretches the track to the row's right edge, so the only way to make
        // room for the price readout beside it is to make the row narrower.
        sliderStyle.sideInset = 320f;

        // NO QUANTITY SLIDER. It was a mushroom control: caps are fungible and
        // a buyer has an appetite, so "how many" was the interesting question.
        // A tape is one specific song and nobody wants two copies of it, so the
        // shelf is a chooser and the only number left to set is the price.

        // PRICE rides the green→amber→red risk gradient — position on the track
        // IS the risk read, same as the phone.
        _askSlider = DealSliderKit.BuildSliderRow(
            _panelRT, "ASK", -284f, 0, 1, 0, DealSliderKit.RiskGradient(), out _askHandleLabel, sliderStyle);
        _askSlider.onValueChanged.AddListener(v =>
        {
            if (_suppressInput) return;
            _ask = Mathf.Max(0, Mathf.RoundToInt(v));
            Refresh();
        });

        // The greed read, as a SENTENCE rather than an unlabelled coloured bar.
        // The bar said nothing on its own — if the person who commissioned it
        // has to ask what it does, a new player has no chance. Same information,
        // still measured against MARKET (never against the buyer's hidden rate),
        // now readable at a glance.
        // The price you are asking, beside its own slider. It used to be a big
        // centred number under the caption "TOTAL FOR THE LOT" - wrong on both
        // counts once a sale is one tape: there is no lot, and a second copy of
        // a number the slider thumb already shows is the kind of redundancy Sam
        // has flagged three times now. Here it reads as the row's value.
        _totalText = Txt(_panelRT, "0", new Vector2(352, -288), 150, 30, 24,
                         C_Value, FontStyles.Bold, TextAlignmentOptions.Right);

        _riskText = Txt(_panelRT, "", new Vector2(0, -324), 820, 22, 15, C_Ok, FontStyles.Bold, TextAlignmentOptions.Center);

        // result and cooldown BOTH show while barred, so they must not overlap.
        _resultText = Txt(_panelRT, "", new Vector2(0, -488), 820, 26, 17, C_Ok, FontStyles.Bold, TextAlignmentOptions.Center);
        _cdText = Txt(_panelRT, "", new Vector2(0, -516), 820, 22, 14, C_Err, FontStyles.Bold, TextAlignmentOptions.Center);
        _cdText.gameObject.SetActive(false);

        // ── buttons ──
        _takeBtn      = MkBtn(_panelRT, "TakeBtn",  new Vector2(-258, -558), new Vector2(246, 54), C_BtnTake, TakeCounter, out _takeLabel);
        _primaryBtn   = MkBtn(_panelRT, "MainBtn",  new Vector2(0,    -558), new Vector2(246, 54), C_BtnSell, OnPrimary,  out _primaryLabel);
        _secondaryBtn = MkBtn(_panelRT, "BackBtn",  new Vector2(258,  -558), new Vector2(246, 54), C_BtnBack, OnSecondary,out _secondaryLabel);

        VendorMoneyBadge.Attach(_panelRT);
        BuildCursor();

        _panelRT.gameObject.SetActive(false);
    }

    void OnPrimary()
    {
        if (_scheduled) DeliverOrder();
        else if (_stage == Stage.Countered) PushBack();
        else MakeOffer();
    }

    /// Scheduled-deal fulfilment (spec §5 + the 2026-08-07 playtest updates).
    /// The delivery is judged on TWO axes, multiplied into one roll:
    ///   goods — exact (agreed tier, ≥ agreed qty) is a certainty; anything
    ///           else runs the substitution chance
    ///   price — at or under the agreed number is a certainty; re-asking OVER
    ///           it decays fast (agree 20, demand 30 → they almost walk)
    /// Untouched price + exact goods pays agreed × gratitude with full bond.
    /// Any deviation that lands pays YOUR ask, but bond gains are halved.
    /// A refusal is −5 bond, kills the appointment AND bars them for 5 min —
    /// no instant re-deal through the walk-up panel (Sam found that exploit).
    void DeliverOrder()
    {
        if (!_scheduled || _appt == null || !HasOffer) return;
        var delivered = Press;
        int agreed = _appt.offerPerCap;
        int ask = Mathf.Max(1, _ask);

        // "Exact" for a tape means the right GENRE and enough of them. There is
        // no tier ladder to substitute along, so a wrong-genre delivery is one
        // flat gamble rather than a graded one.
        bool exactGoods = delivered != null
                       && TapeTrade.Fills(delivered.track, _appt.askTier)
                       && _offerCountN >= _appt.askQty;
        bool exactPrice = ask <= agreed;
        float chance = (exactGoods ? 1f : 0.45f)
                     * BuyerDeals.OverchargeFactor(ask, agreed);

        if (chance >= 0.999f || UnityEngine.Random.value <= chance)
        {
            int perCap;
            int qty;
            if (exactGoods)
            {
                // Gratitude bump only when the deal is honoured as written.
                perCap = (ask == agreed)
                    ? Mathf.RoundToInt(agreed * BuyerDeals.GratitudeBonus(_appt.windowMinutes))
                    : ask;
                qty = Mathf.Min(_offerCountN, _appt.askQty);
            }
            else
            {
                perCap = ask;
                qty = Mathf.Min(_offerCountN, RemainingAppetite);
                if (qty <= 0) { SetResult("\"I'm full up. Come back later.\"", C_Err); return; }
            }
            CompleteScheduled(perCap, qty, substituted: !(exactGoods && exactPrice));
        }
        else
        {
            if (!EconomySync.ReportSubstitutionRefused(_buyerId, Mathf.RoundToInt(chance * 100)))
                BuyerLedger.SubstitutionRefused(_buyerId, Mathf.RoundToInt(chance * 100));
            // Refused deliveries sting: same 5-minute freeze-out as pushing
            // past an in-person counter, or the walk-up panel becomes a free
            // second attempt at any price.
            MushroomDealState.Bar(_buyerId);
            _scheduled = false; _appt = null;
            ReturnOfferToBar();
            SetResult(ask > agreed
                ? $"\"We agreed {agreed}. Get away from me.\" — {_npcName} won't deal for 5 minutes."
                : $"\"That's not what we agreed.\" — {_npcName} won't deal for 5 minutes.", C_Err);
            Refresh();
        }
    }

    void CompleteScheduled(int perCap, int qty, bool substituted)
    {
        int leftover = _offerCountN - qty;
        var soldRec = Press;
        int soldGenre = GenreIndexOf(soldRec);
        bool matchedTaste = soldGenre == AlienTaste.FavouriteGenreIndex(_buyerId);
        string species = _offerSpecies;
        _offerSpecies = null; _offerCountN = 0; _stage = Stage.Open; _counter = 0; _ask = 0;
        if (leftover > 0 && Hotbar.Instance != null)
            Hotbar.Instance.AddResource(Hotbar.ItemId.Cassette, leftover, species);
        int credits = perCap * qty;
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(credits);
        NotifyTapeSold(species, soldRec, qty);
        BuyerLedger.ReportTapeDeal(_buyerId, soldGenre, perCap, qty,
                                   keptAppointment: true, matchedTaste: matchedTaste);
        _scheduled = false; _appt = null;
        _onSold?.Invoke(qty);
        SetResult(substituted
            ? $"{_npcName} grumbled, but took {qty} for {credits}."
            : $"Order delivered. {_npcName} paid {credits} credits.", C_Ok);
        Refresh();
    }

    void OnSecondary()
    {
        // CLOSE, not "DONE": the button leaves the deal, it doesn't confirm one.
        // "Done" read as "finish the sale" right next to a SELL button.
        if (_stage == Stage.Countered) LeaveIt();
        else Close();
    }

    /// <summary>
    /// One tape in the shelf: shell art, NAME, genre and how many you carry.
    /// A mushroom stack got a bare 72px square because a live species render
    /// identified it; a tape is identified by its name, so the tile is wider
    /// and carries text.
    /// </summary>
    SlotWidget BuildBarSlot(RectTransform parent, int index, Vector2 pos)
    {
        var w = new SlotWidget();
        w.root = Panel(parent, "Tile", pos, new Vector2(TileW, TileH), C_SlotEdge);
        w.border = w.root.GetComponent<Image>();
        w.border.raycastTarget = true;

        var fill = Panel(w.root, "Fill", new Vector2(0, -1f), new Vector2(TileW - 2, TileH - 2), C_SlotBg);
        w.bg = fill.GetComponent<Image>();
        w.bg.raycastTarget = false;

        // Shell art across the top: 8 px pad, then a 44 px band, mirroring the
        // mockup's .art. Fed the WIDE sprite - the square hotbar art is a small
        // shell inside a mostly-transparent 96x96 texture, and preserveAspect
        // fits those transparent bounds, so the tape came out a third size.
        var artRT = Panel(w.root, "Art", new Vector2(0, -TilePad),
                          new Vector2(TileW - TilePad * 2f, TileArtH), Color.white);
        w.art = artRT.GetComponent<Image>();
        w.art.raycastTarget = false;
        w.art.preserveAspect = true;

        // Count sits ON the art, top-right, so it never competes with the name.
        w.count = Txt(w.root, "", new Vector2(TileW * 0.5f - 22f, -TilePad - 2f),
                      32, 16, 12, C_Label, FontStyles.Bold, TextAlignmentOptions.Right);

        // Name, then genre - the two things you used to have to pick the tape
        // up to find out.
        w.nameLbl = Txt(w.root, "", new Vector2(0, -(TilePad + TileArtH + 4f)), TileW - TilePad * 2f, 16, 11,
                        C_Label, FontStyles.Normal, TextAlignmentOptions.Left);
        w.nameLbl.overflowMode = TextOverflowModes.Ellipsis;
        w.genreLbl = Txt(w.root, "", new Vector2(0, -(TilePad + TileArtH + 22f)), TileW - TilePad * 2f, 14, 10,
                         C_Dim, FontStyles.Normal, TextAlignmentOptions.Left);

        // Tier pip on the meta row's right: Type 1 phosphor, Type 2 magenta.
        var pip = Panel(w.root, "Pip", new Vector2(TileW * 0.5f - TilePad - 4f, -(TilePad + TileArtH + 25f)),
                        new Vector2(8, 8), Color.white);
        w.tier = pip.GetComponent<Image>();
        w.tier.raycastTarget = false;

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

    // MkMini / MkStep are gone with the ± steppers they built — the CAPS and
    // PRICE sliders replaced both clusters.

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
