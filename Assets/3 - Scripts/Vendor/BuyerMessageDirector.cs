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

        // Tev restocks when the career crosses a milestone — announced ONCE
        // per milestone, guarded by a StoryDirector counter so it survives
        // saves and is shared in co-op (tape formats, 2026-08-18). Silent
        // while the gate is vaulted — everything is already in stock.
        var sd = FeatureVault.TapeCareerGate ? StoryDirector.Instance : null;
        if (sd != null)
        {
            int unlocked = TapeCareer.UnlockedKind();
            if (unlocked > sd.GetCounter("tapesUnlockAnnounced"))
            {
                sd.SetCounter("tapesUnlockAnnounced", unlocked);
                Notify(unlocked == TraxKind.Full
                    ? "TEV: \"Full-length blanks just came in. You've earned the shelf space.\""
                    : "TEV: \"New stock - half-length blanks. Your demos are selling; time for real songs.\"");
            }
        }

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
            // No appetite/saturation gate for tapes: a song is not produce,
            // and "they are full of music" is not a thing. nextTextAt above is
            // the whole pacing rule, and it already scales with bond.

            SendWantText(b);
            openWants++;
        }

        AmbushSweep(now);
        UpdateCompassMarkers();
    }

    // ── The ambush walk-up (loop-feel C) ──────────────────────────────────
    //
    // A hooked buyer (craving 60+, nothing bought for a day) whose alien is
    // streamed in near the player physically comes to find them, then wants
    // something — the world pursuing the player, which is the whole flywheel.
    // At most ONE ambush per galaxy day. Craving never touches the price:
    // whatever happens next is the completely normal walk-up/sell flow.
    //
    // ⚠️ CO-OP: the sweep runs on the host, but it ambushes ANY player. It used
    // to scan around "the player", which in co-op silently means "the only
    // player" — the trap this codebase has paid for before — so a guest could
    // never be walked up to. It now scans around everyone on the roster and
    // approaches whoever the eligible alien is nearest, and AlienSync streams
    // the walk so both players watch it happen.

    const float AmbushScanRange = 60f;
    const float AmbushStopDistance = 3.5f;
    const float AmbushMaxSeconds = 90f;

    string _ambushBuyerId;
    AlienWander _ambushWander;
    float _ambushStartedAt;
    int _lastAmbushDay;

    void AmbushSweep(float now)
    {
        if (!FeatureVault.CravingSystem) return;
        var gt = GalaxyTime.Instance;
        if (gt == null) return;
        int today = gt.Day;

        // A live ambush: watch it run its course.
        if (_ambushWander != null)
        {
            bool done = !_ambushWander.isActiveAndEnabled          // despawned/killed
                        || _ambushWander.ApproachBlocked           // water/cliff in the way
                        || _ambushWander.ApproachArrived           // made it — speak
                        || now - _ambushStartedAt > AmbushMaxSeconds;
            if (done) FinishAmbush();
            return;
        }
        if (_ambushBuyerId != null) { FinishAmbush(); return; }    // wander component vanished

        if (_lastAmbushDay == today) return;

        var aliens = SpawnedAlienNPC.AllAliens;
        for (int i = 0; i < aliens.Count; i++)
        {
            var alien = aliens[i];
            if (alien == null) continue;

            // Whoever this one is nearest — either player will do, and the
            // alien walks to the one it could actually reach.
            Transform victim = NearestPlayerWithin(alien.transform.position, AmbushScanRange);
            if (victim == null) continue;

            string id = AlienIdentity.Of(alien);
            var b = BuyerLedger.Get(id);
            if (b == null || b.convo != BuyerLedger.Convo.None) continue;
            if (!CravingRules.AmbushEligible(b.craving, b.lastPurchaseDay, today)) continue;

            var wander = alien.GetComponent<AlienWander>();
            if (wander == null || !wander.enabled || wander.Approaching) continue;

            _ambushBuyerId = id;
            _ambushWander = wander;
            _ambushStartedAt = now;
            _lastAmbushDay = today;   // the day's ambush is spent even if it fails
            wander.BeginApproach(victim, AmbushStopDistance);
            return;
        }
    }

    /// <summary>
    /// The closest player to a point, or null if nobody is within range.
    ///
    /// Goes through PlayerRoster rather than the local PlayerController: in
    /// co-op the remote player is a puppet with no controller on it, so the
    /// obvious lookup would find only ourselves and half the household would be
    /// invisible to the flywheel.
    /// </summary>
    static Transform NearestPlayerWithin(Vector3 point, float range)
    {
        float bestSqr = range * range;
        Transform best = null;
        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i].Transform;
            if (t == null) continue;
            float sqr = (t.position - point).sqrMagnitude;
            if (sqr > bestSqr) continue;
            bestSqr = sqr;
            best = t;
        }
        return best;
    }

    /// Whatever ended the approach — arrival, blockage or timeout — the hunger
    /// SPEAKS: the hungry line flashes and a normal want text lands, so the
    /// moment always produces something actionable. Arrival just adds the
    /// theatre of them standing in front of you when it does.
    void FinishAmbush()
    {
        var b = BuyerLedger.Get(_ambushBuyerId);
        if (_ambushWander != null) _ambushWander.EndApproach();
        _ambushWander = null;
        string id = _ambushBuyerId;
        _ambushBuyerId = null;

        if (b == null || b.convo != BuyerLedger.Convo.None) return;
        Notify($"{AlienNames.For(id)}: \"been humming that {AlienTaste.FavouriteGenre(id)} tape all week — got anything new?\"");
        SendWantText(b);
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

    /// Share of orders that NAME a specific track (loop-feel D) when at least
    /// one is eligible for this buyer.
    const float NamedRequestChance = 0.30f;

    void SendWantText(BuyerLedger.Buyer b)
    {
        b.requestTrackId = "";

        // ── word of mouth (loop-feel D): sometimes they ask for a specific
        //    track of yours they heard about. Eligible = sold to someone
        //    ELSE, unheard by THIS buyer, project still on the shelf (so a
        //    reprint is possible) — which is also why the already-heard
        //    refusal can never trip on it, by construction.
        if (Random.value < NamedRequestChance
            && TryPickNamedRequest(b.id, out TraxLibrary.Record wanted, out string gossiper))
        {
            int gIdx = TapeTrade.GenreIndexOf(wanted.track);
            b.askTier = gIdx;
            b.askQty = TapeTrade.PickAskQty(b.id);
            b.askTapeTier = TapeTrade.PickAskTier(b.id);
            // A named request is for THAT track — demo lineage, never a
            // format ask (any pressing of the track fills it).
            b.askKind = TraxKind.Demo;
            b.modulesBasis = Mathf.Max(1, TraxLibrary.InstalledCount);
            // Same quote path as every order — the 1.25x request bonus is
            // already baked into the texted number (Sam's call: a buyer who
            // asks for something specific simply OFFERS more; agreed = paid).
            b.offerPerCap = TapeTrade.OpeningOffer(b.id, gIdx, b.askTapeTier);
            b.requestTrackId = TapeTrade.TrackHex(wanted.trackId);
            b.convo = BuyerLedger.Convo.AwaitingReply;
            BuyerLedger.Log(b, BuyerLedger.EvType.NamedRequest, b.offerPerCap, b.askQty, b.askTier,
                            c: b.askTapeTier,
                            s: $"{b.requestTrackId}|{wanted.name.ToUpperInvariant()}|{AlienNames.For(gossiper)}");
            Notify($"{AlienNames.For(b.id)} sent you a message");
            return;
        }

        // askTier holds a GENRE INDEX and offerPerCap a price per TAPE — the
        // field names are legacy, kept so the save schema does not move.
        int genre = TapeTrade.PickAskGenre(b.id);
        b.askTier = genre;
        b.askQty = TapeTrade.PickAskQty(b.id);
        // Contract terms (2026-08-16): the order names its CASSETTE TIER
        // (their preferred shell) and records the plugin count the quote was
        // priced against — both are goods-spec terms the delivery is graded on.
        b.askTapeTier = TapeTrade.PickAskTier(b.id);
        // The FORMAT they want (2026-08-18): their derived preference, clamped
        // to what the career has unlocked — so full-length commissions start
        // arriving exactly when Tev starts selling the blanks for them.
        b.askKind = TapeTrade.PickAskKind(b.id);
        b.modulesBasis = Mathf.Max(1, TraxLibrary.InstalledCount);
        b.offerPerCap = TapeTrade.OpeningOffer(b.id, genre, b.askTapeTier, b.askKind);
        b.convo = BuyerLedger.Convo.AwaitingReply;
        BuyerLedger.Log(b, BuyerLedger.EvType.WantText, b.offerPerCap, b.askQty, b.askTier,
                        c: b.askTapeTier, k: b.askKind + 1);
        Notify($"{AlienNames.For(b.id)} sent you a message");
    }

    /// A shelf project sold to at least one OTHER buyer that this one hasn't
    /// heard. Random among candidates so the same track isn't everyone's
    /// obsession.
    static bool TryPickNamedRequest(string buyerId, out TraxLibrary.Record wanted, out string gossiper)
    {
        wanted = null; gossiper = null;
        var projects = TraxLibrary.Projects;
        int seen = 0;
        for (int i = 0; i < projects.Count; i++)
        {
            var rec = projects[i];
            if (rec == null || rec.trackId == 0) continue;
            if (!TapeMemory.AnyoneElseBought(rec.trackId, buyerId, out string owner)) continue;
            if (TapeMemory.HasHeard(buyerId, TapeTrade.DialsOf(rec.track))) continue;
            // Reservoir pick: k-th candidate replaces with probability 1/k.
            seen++;
            if (Random.Range(0, seen) == 0) { wanted = rec; gossiper = owner; }
        }
        return wanted != null;
    }

    // ── Player replies (called by MessagesScreen) ──────────────────────────

    /// <param name="tapeTier">1/2 = the player chose a cassette tier on the
    /// accept path ("I'll bring a Type 2 instead") — the price re-derives at
    /// that tier before scheduling. 0 = keep the order's tier (counter-back
    /// and price-agreed accepts, where the number was negotiated already).</param>
    public void Accept(BuyerLedger.Buyer b, int windowMinutes, int tapeTier = 0)
    {
        // On a guest this becomes a request to the host, which performs it and
        // broadcasts the result. Replying locally would be worse than useless:
        // the reply paths below roll dice, so the two machines would end up
        // holding different conversations with the same alien.
        if (b != null && EconomySync.RouteAccept(b.id, windowMinutes, tapeTier)) return;
        if (b == null || (b.convo != BuyerLedger.Convo.AwaitingReply
                       && b.convo != BuyerLedger.Convo.AwaitingCounterBack
                       && b.convo != BuyerLedger.Convo.PriceAgreed)) return;
        if ((tapeTier == 1 || tapeTier == 2)
            && b.convo == BuyerLedger.Convo.AwaitingReply && tapeTier != b.askTapeTier)
        {
            // Deterministic re-quote at the chosen tier — the same number the
            // TierPick chip displayed, so the player accepts exactly what they
            // saw (guest and host derive it identically).
            b.askTapeTier = tapeTier;
            b.offerPerCap = TapeTrade.OpeningOffer(b.id, b.askTier, tapeTier, b.askKind);
        }
        int agreed = b.convo == BuyerLedger.Convo.AwaitingCounterBack ? b.counterBackPerCap : b.offerPerCap;
        b.offerPerCap = agreed;
        b.counterBackPerCap = 0;
        b.windowMinutes = windowMinutes;
        b.deadline = Time.unscaledTime + windowMinutes * 60f;
        b.convo = BuyerLedger.Convo.Scheduled;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerAccepted, windowMinutes, 0, b.askTier, markUnread: false);
        // A named request's confirmation must repeat the NAME — a genre-only
        // "1 VOLT at 29" here would promise something looser than what the
        // delivery grades (the promise/grade law).
        string namedPayload = string.IsNullOrEmpty(b.requestTrackId) ? null
            : $"{b.requestTrackId}|{TapeTrade.RequestTrackName(b.requestTrackId)}|";
        BuyerLedger.Log(b, BuyerLedger.EvType.Scheduled, agreed, b.askQty, b.askTier, markUnread: false,
                        c: b.askTapeTier, s: namedPayload, k: b.askKind + 1);
    }

    /// Player counters with a price AND a quantity (Sam's rule: you can
    /// short their ask — they buy but pay no premium — or oversupply, which
    /// cools them; BuyerDeals.QtyMood does the math). A counter-back is on
    /// PRICE only: your quantity stands.
    public void Counter(BuyerLedger.Buyer b, int askPerCap, int offerQty, int tapeTier = 0)
    {
        // Guest → host. ResolveCounter below rolls for accept / counter-back /
        // refuse, so this must happen on exactly one machine.
        if (b != null && EconomySync.RouteCounter(b.id, askPerCap, offerQty, tapeTier)) return;
        if (b == null || b.convo != BuyerLedger.Convo.AwaitingReply) return;
        askPerCap = Mathf.Max(1, askPerCap);
        offerQty = Mathf.Max(1, offerQty);
        // The counter may propose a different cassette tier ("I only sell
        // Type 2s — 30"): the tier becomes part of the deal being argued and
        // the buyer's ceiling re-derives against it.
        if (tapeTier == 1 || tapeTier == 2) b.askTapeTier = tapeTier;
        int dealTier = b.askTapeTier >= 1 ? b.askTapeTier : 1;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerCountered, askPerCap, offerQty, b.askTier, markUnread: false, c: dealTier);
        var res = TapeTrade.ResolveCounter(b.id, b.askTier, askPerCap,
                                            b.askQty, offerQty, dealTier, b.askKind, out int counterBack);
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
        // Guest → host: the sulk timer that follows is a dice roll too.
        if (b != null && EconomySync.RouteDecline(b.id)) return;
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
