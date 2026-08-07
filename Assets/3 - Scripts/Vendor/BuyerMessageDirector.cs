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
        _tickTimer += Time.unscaledDeltaTime;
        if (_tickTimer < TickInterval) return;
        _tickTimer = 0f;
        float now = Time.unscaledTime;

        int openWants = 0;
        foreach (var b in BuyerLedger.All())
            if (b.convo == BuyerLedger.Convo.AwaitingReply || b.convo == BuyerLedger.Convo.AwaitingCounterBack)
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
                       && b.convo != BuyerLedger.Convo.AwaitingCounterBack)) return;
        int agreed = b.convo == BuyerLedger.Convo.AwaitingCounterBack ? b.counterBackPerCap : b.offerPerCap;
        b.offerPerCap = agreed;
        b.counterBackPerCap = 0;
        b.windowMinutes = windowMinutes;
        b.deadline = Time.unscaledTime + windowMinutes * 60f;
        b.convo = BuyerLedger.Convo.Scheduled;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerAccepted, windowMinutes, 0, b.askTier, markUnread: false);
        BuyerLedger.Log(b, BuyerLedger.EvType.Scheduled, agreed, b.askQty, b.askTier, markUnread: false);
    }

    public void Counter(BuyerLedger.Buyer b, int askPerCap)
    {
        if (b == null || b.convo != BuyerLedger.Convo.AwaitingReply) return;
        askPerCap = Mathf.Max(1, askPerCap);
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerCountered, askPerCap, 0, b.askTier, markUnread: false);
        var res = BuyerDeals.ResolveCounter(b.id, (MushroomTier)b.askTier, askPerCap, out int counterBack);
        switch (res)
        {
            case BuyerDeals.CounterResult.Accept:
                b.offerPerCap = askPerCap;
                // Stays AwaitingReply — the thread now shows the window pick.
                // b=1 flags the grudging-acceptance wording in BuyerTexts.
                BuyerLedger.Log(b, BuyerLedger.EvType.BuyerCounterBack, askPerCap, 1, b.askTier);
                break;
            case BuyerDeals.CounterResult.CounterBack:
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
                       && b.convo != BuyerLedger.Convo.AwaitingCounterBack)) return;
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
        return true;
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
