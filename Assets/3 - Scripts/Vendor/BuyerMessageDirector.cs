using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The clock behind the Messages app (spec §4–§5): watches every REGULAR's
/// appetite, sends want-texts when they refill (max 3 open at once, staggered
/// after a load so the phone doesn't detonate), resolves appointment
/// deadlines into misses, and answers "where is this buyer" for the distance
/// line. All persistent state lives in <see cref="BuyerLedger"/> — this
/// component is pure timing plus the player-reply entry points the Messages
/// UI calls.
///
/// Auto-singleton; ALSO seeded in MainMenuController.EnsureGameplaySingletons
/// (CLAUDE.md trap #1 — RuntimeInitializeOnLoadMethod never fires post-menu
/// in builds).
/// </summary>
public class BuyerMessageDirector : MonoBehaviour
{
    public static BuyerMessageDirector Instance { get; private set; }

    public const int MaxOpenWants = 3;
    const float TickInterval = 2f;
    const float PostLoadStaggerMin = 20f, PostLoadStaggerMax = 180f;
    const float SulkMin = 300f, SulkMax = 600f;
    const float DeclineRetryMin = 120f, DeclineRetryMax = 300f;

    float _tickTimer;
    AlienNPCSpawner _spawner;
    float _spawnerRetryAt;
    PlayerController _playerCached;
    float _playerRetryAt;
    // Compass waypoints for live appointments, keyed by buyer id. Only shown
    // while the player is on the SAME body as the buyer — the compass is a
    // 2D strip projected on the current planet's surface, so a marker for an
    // alien on another planet would point somewhere meaningless (Sam's call).
    readonly System.Collections.Generic.HashSet<string> _compassIds =
        new System.Collections.Generic.HashSet<string>();
    static readonly System.Collections.Generic.List<string> s_compassScratch =
        new System.Collections.Generic.List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        new GameObject("[BuyerMessageDirector]").AddComponent<BuyerMessageDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    /// After any load, every buyer's appetite reads empty (session state), so
    /// every regular is instantly "hungry". Stagger their first texts over a
    /// few minutes instead of a message storm (spec §4).
    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name == "MainMenu") return;
        foreach (var b in BuyerLedger.All())
            if (b.isRegular && b.convo == BuyerLedger.Convo.None)
                b.nextTextAt = Time.unscaledTime + Random.Range(PostLoadStaggerMin, PostLoadStaggerMax);
    }

    void Update()
    {
        // HOST ONLY. Text timing, offer sizes and bond rolls all use Random, so
        // a client running this would hold its own separate conversation with
        // the same buyer. Sam's spec is the opposite: "if the host makes a bond
        // and gets a text, the clients also get the same texts". Phase 5
        // broadcasts them; until then a client simply does not invent any.
        if (!WorldSync.IsAuthority) return;

        _tickTimer += Time.unscaledDeltaTime;
        if (_tickTimer < TickInterval) return;
        _tickTimer = 0f;
        float now = Time.unscaledTime;

        int openWants = 0;
        foreach (var b in BuyerLedger.All())
            if (b.convo == BuyerLedger.Convo.AwaitingReply
             || b.convo == BuyerLedger.Convo.AwaitingCounterBack
             || b.convo == BuyerLedger.Convo.PriceAgreed)
                openWants++;

        foreach (var b in BuyerLedger.All())
        {
            // Deadline sweep — a lapsed Scheduled appointment is a miss.
            if (b.convo == BuyerLedger.Convo.Scheduled
                && now > b.deadline + BuyerDeals.GraceSeconds)
            {
                BuyerLedger.MissedAppointment(b.id);
                b.nextTextAt = now + Random.Range(SulkMin, SulkMax); // sulk before texting again
                Notify($"{AlienNames.For(b.id)} is not happy.");
                continue;
            }

            // Want-texts: regular, idle, past their pacing gate, hungry, room in the queue.
            if (!b.isRegular || b.convo != BuyerLedger.Convo.None) continue;
            if (openWants >= MaxOpenWants) break;
            if (now < b.nextTextAt) continue;
            int appetite = NPCMushroomPrice.AppetiteMaxOf(b.id);
            if (MushroomDealState.SecondsUntilHungry(b.id, appetite) > 0) continue;

            SendWantText(b);
            openWants++;
        }

        UpdateCompassMarkers();
    }

    /// One compass waypoint per Scheduled appointment, present only while the
    /// player's closest body matches the buyer's home body. Runs at tick rate;
    /// the waypoint's own position provider tracks the moving planet per frame.
    void UpdateCompassMarkers()
    {
        var compass = CompassHUD.Instance;
        if (compass == null) return;

        string playerBody = ClosestBodyName();

        // Drop markers whose appointment ended or whose planet no longer matches.
        s_compassScratch.Clear();
        foreach (var id in _compassIds)
        {
            var b = BuyerLedger.Get(id);
            bool keep = b != null && b.convo == BuyerLedger.Convo.Scheduled
                        && TryGetBuyerPos(id, out _, out string body)
                        && body == playerBody && !string.IsNullOrEmpty(playerBody);
            if (!keep) s_compassScratch.Add(id);
        }
        foreach (var id in s_compassScratch)
        {
            compass.RemoveWaypoint("buyer:" + id);
            _compassIds.Remove(id);
        }

        // Add markers for live appointments on this planet.
        foreach (var b in BuyerLedger.All())
        {
            if (b.convo != BuyerLedger.Convo.Scheduled || _compassIds.Contains(b.id)) continue;
            if (!TryGetBuyerPos(b.id, out Vector3 pos, out string body)) continue;
            if (string.IsNullOrEmpty(playerBody) || body != playerBody) continue;

            // Resolve the position source ONCE — the provider runs per frame,
            // so no string parsing/allocs inside it (CLAUDE.md).
            System.Func<Vector3> provider = null;
            string id = b.id;
            if (id.StartsWith("cell:"))
            {
                int c1 = id.IndexOf(':'), c2 = id.IndexOf(':', c1 + 1);
                int slot; long cell;
                if (c2 < 0
                    || !int.TryParse(id.Substring(c1 + 1, c2 - c1 - 1), out slot)
                    || !long.TryParse(id.Substring(c2 + 1), out cell)) continue;
                var sp = Spawner();
                if (sp == null) continue;
                provider = () =>
                {
                    Vector3 p;
                    return sp != null && sp.TryGetCellWorldPos(slot, cell, out p) ? p : Vector3.zero;
                };
            }
            else
            {
                var go = GameObject.Find(id.Substring("scene:".Length));
                if (go == null) continue;
                var tf = go.transform;
                provider = () => tf != null ? tf.position : Vector3.zero;
            }

            compass.AddWaypoint("buyer:" + id, provider, AlienNames.For(id),
                                tint: new Color32(0x6E, 0xDC, 0x82, 0xFF));
            _compassIds.Add(id);
        }
    }

    /// Name of the body the player is currently on/nearest to, "" if unknown.
    /// Closest-by-surface-distance over NBodySimulation.Bodies (null-safe).
    string ClosestBodyName()
    {
        var player = Player();
        var bodies = NBodySimulation.Bodies;
        if (player == null || bodies == null || bodies.Length == 0) return "";
        Vector3 p = player.transform.position;
        string best = ""; float bestDist = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i];
            if (body == null || body.isStaticAttractor) continue;
            float d = Vector3.Distance(p, body.Position) - body.radius;
            if (d < bestDist) { bestDist = d; best = body.bodyName; }
        }
        // "On" a planet ≈ within 300 m of its surface (the alien stream-in
        // radius); beyond that the compass marker means nothing anyway.
        return bestDist <= 300f ? best : "";
    }

    PlayerController Player()
    {
        if (_playerCached != null) return _playerCached;
        if (Time.unscaledTime < _playerRetryAt) return null;
        _playerRetryAt = Time.unscaledTime + 5f;
        _playerCached = FindObjectOfType<PlayerController>();
        return _playerCached;
    }

    void SendWantText(BuyerLedger.Buyer b)
    {
        var tier = BuyerDeals.PickAskTier(b.id);
        b.askTier = (int)tier;
        b.askQty = BuyerDeals.PickAskQty(b.id);
        b.offerPerCap = BuyerDeals.OpeningOffer(b.id, tier);
        b.convo = BuyerLedger.Convo.AwaitingReply;
        BuyerLedger.Log(b, BuyerLedger.EvType.WantText, b.offerPerCap, b.askQty, b.askTier);
        Notify($"{AlienNames.For(b.id)} sent you a message");
    }

    // ── Player replies (called by MessagesScreen) ──────────────────────────

    public void Accept(BuyerLedger.Buyer b, int windowMinutes)
    {
        if (b == null || (b.convo != BuyerLedger.Convo.AwaitingReply
                       && b.convo != BuyerLedger.Convo.AwaitingCounterBack
                       && b.convo != BuyerLedger.Convo.PriceAgreed)) return;
        int agreed = b.convo == BuyerLedger.Convo.AwaitingCounterBack ? b.counterBackPerCap : b.offerPerCap;
        b.offerPerCap = agreed;
        b.counterBackPerCap = 0;
        b.windowMinutes = windowMinutes;
        b.deadline = Time.unscaledTime + windowMinutes * 60f;
        b.convo = BuyerLedger.Convo.Scheduled;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerAccepted, windowMinutes, 0, b.askTier, markUnread: false);
        BuyerLedger.Log(b, BuyerLedger.EvType.Scheduled, agreed, b.askQty, b.askTier, markUnread: false);
    }

    /// Player counters with a price AND a quantity (Sam's rule: you can
    /// short their ask — they buy but pay no premium — or oversupply, which
    /// cools them; BuyerDeals.QtyMood does the math). A counter-back is on
    /// PRICE only: your quantity stands.
    public void Counter(BuyerLedger.Buyer b, int askPerCap, int offerQty)
    {
        if (b == null || b.convo != BuyerLedger.Convo.AwaitingReply) return;
        askPerCap = Mathf.Max(1, askPerCap);
        offerQty = Mathf.Max(1, offerQty);
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerCountered, askPerCap, offerQty, b.askTier, markUnread: false);
        var res = BuyerDeals.ResolveCounter(b.id, (MushroomTier)b.askTier, askPerCap,
                                            b.askQty, offerQty, out int counterBack);
        switch (res)
        {
            case BuyerDeals.CounterResult.Accept:
                b.offerPerCap = askPerCap;
                b.askQty = offerQty;
                // Price LOCKED — PriceAgreed only offers the window pick, so
                // the player can't counter again off their own accepted number.
                // b=1 flags the grudging-acceptance wording in BuyerTexts.
                b.convo = BuyerLedger.Convo.PriceAgreed;
                BuyerLedger.Log(b, BuyerLedger.EvType.BuyerCounterBack, askPerCap, 1, b.askTier);
                break;
            case BuyerDeals.CounterResult.CounterBack:
                b.askQty = offerQty;   // they take your quantity, argue the price
                b.counterBackPerCap = counterBack;
                b.convo = BuyerLedger.Convo.AwaitingCounterBack;
                BuyerLedger.Log(b, BuyerLedger.EvType.BuyerCounterBack, counterBack, 0, b.askTier);
                Notify($"{AlienNames.For(b.id)} countered");
                break;
            case BuyerDeals.CounterResult.Refuse:
                BuyerLedger.CounterRefused(b.id);
                b.nextTextAt = Time.unscaledTime + Random.Range(SulkMin, SulkMax);
                Notify($"{AlienNames.For(b.id)} is done talking");
                break;
        }
    }

    public void Decline(BuyerLedger.Buyer b)
    {
        if (b == null || (b.convo != BuyerLedger.Convo.AwaitingReply
                       && b.convo != BuyerLedger.Convo.AwaitingCounterBack
                       && b.convo != BuyerLedger.Convo.PriceAgreed)) return;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerDeclined, 0, 0, b.askTier, markUnread: false);
        b.convo = BuyerLedger.Convo.None;
        b.counterBackPerCap = 0;
        b.nextTextAt = Time.unscaledTime + Random.Range(DeclineRetryMin, DeclineRetryMax);
    }

    // ── Location (distance line) ───────────────────────────────────────────

    /// Where does this buyer live? cell:… ids decode to their spawn cell;
    /// scene:… ids resolve to the live scene object when present. Callers
    /// must throttle (the thread view refreshes at 1 Hz, never per frame).
    public bool TryGetBuyerPos(string id, out Vector3 pos, out string bodyName)
    {
        pos = default; bodyName = "";
        if (string.IsNullOrEmpty(id)) return false;
        if (id.StartsWith("cell:"))
        {
            // "cell:{bodySlot}:{cellId}"
            int c1 = id.IndexOf(':'), c2 = id.IndexOf(':', c1 + 1);
            if (c2 < 0) return false;
            if (!int.TryParse(id.Substring(c1 + 1, c2 - c1 - 1), out int slot)) return false;
            if (!long.TryParse(id.Substring(c2 + 1), out long cell)) return false;
            var sp = Spawner();
            if (sp == null) return false;
            bodyName = sp.GetBodyName(slot);
            return sp.TryGetCellWorldPos(slot, cell, out pos);
        }
        if (!id.StartsWith("scene:")) return false;
        var go = GameObject.Find(id.Substring("scene:".Length));
        if (go == null) return false;
        pos = go.transform.position;
        bodyName = BodyNameAt(pos);
        return true;
    }

    /// Closest body to a world position (for scene-anchored buyers, whose id
    /// carries no body). Same surface-distance metric as ClosestBodyName.
    static string BodyNameAt(Vector3 p)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return "";
        string best = ""; float bestDist = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i];
            if (body == null || body.isStaticAttractor) continue;
            float d = Vector3.Distance(p, body.Position) - body.radius;
            if (d < bestDist) { bestDist = d; best = body.bodyName; }
        }
        return best;
    }

    AlienNPCSpawner Spawner()
    {
        if (_spawner != null) return _spawner;
        if (Time.unscaledTime < _spawnerRetryAt) return null;
        _spawnerRetryAt = Time.unscaledTime + 5f;   // throttled refind (CLAUDE.md)
        _spawner = FindObjectOfType<AlienNPCSpawner>();
        return _spawner;
    }

    static void Notify(string text)
    {
        var phone = PlayerPhoneUI.Instance;
        if (phone != null) phone.FlashNotification(text);
    }

    // ── Cheats (Universe.cheatsEnabled only) ───────────────────────────────
    // F6: every regular goes hungry now (pacing gates cleared)
    // F7: fast-forward any Scheduled deadline to lapse ~5 s from now
    void LateUpdate()
    {
        if (!Universe.cheatsEnabled) return;
        if (Input.GetKeyDown(KeyCode.F6))
        {
            foreach (var b in BuyerLedger.All()) b.nextTextAt = 0f;
            MushroomDealState.ResetAll();   // empties appetite → everyone hungry
            Debug.Log("[BuyerMessageDirector] cheat: all regulars hungry, pacing cleared");
        }
        if (Input.GetKeyDown(KeyCode.F7))
        {
            foreach (var b in BuyerLedger.All())
                if (b.convo == BuyerLedger.Convo.Scheduled)
                    b.deadline = Time.unscaledTime + 5f - BuyerDeals.GraceSeconds;
            Debug.Log("[BuyerMessageDirector] cheat: deadlines fast-forwarded");
        }
    }
}
