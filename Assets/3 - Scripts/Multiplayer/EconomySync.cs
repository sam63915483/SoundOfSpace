using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The buyers, shared: bonds, want-texts, appointments, and how full each alien
/// is right now.
///
/// ── What is shared and what is personal ──────────────────────────────────
/// THE BUYER is shared. Their bond with you, whether they are a regular, the
/// conversation you have open with them, and how many caps they have taken
/// recently are all one thing that both players see. Walk up to an alien your
/// friend just sold forty caps to and they are full for you as well.
///
/// THE MONEY is personal. Whoever closes the deal banks it — one player can
/// haggle the offer by text while the other sprints across the planet to hand
/// the caps over, and it pays the runner. That is the design's own call and it
/// is why the wallet is deliberately NOT in anything below.
///
/// ── Whole-snapshot replication, deliberately ─────────────────────────────
/// Rather than a message per field, the host ships the entire ledger plus the
/// entire deal state whenever either changes. It is the same trick the join
/// snapshot uses — the save schema is the network schema — and it buys
/// something worth more than the bandwidth: there is no such thing as a missed
/// delta here, so the two machines cannot drift into disagreeing about a bond
/// or an appointment and stay that way.
///
/// The cost is small and bounded. A ledger with a dozen buyers and forty events
/// each is a few tens of KB of JSON, and it is only sent when something
/// actually happened — a want-text, a sale, a reply. Not per frame, not per
/// tick. Coalesced through a version counter so a burst of mutations in one
/// frame produces one message.
///
/// ── Who rolls the dice ───────────────────────────────────────────────────
/// The host, for everything. BuyerMessageDirector.Update already returns early
/// on a client (it rolls Random for text timing, offer sizes and counter
/// outcomes). The three player REPLIES roll dice too — ResolveCounter decides
/// accept / counter-back / refuse — so a guest sends its reply here as a
/// request and the host performs it. That is also what makes "first response
/// wins" true for free: the host applies replies in arrival order and the
/// second one finds the conversation already answered.
/// </summary>
public class EconomySync : MonoBehaviour
{
    public static EconomySync Instance { get; private set; }

    const string Msg = "EconomySync";

    const byte KindRequestState = 0;   // client -> host
    const byte KindStateChunk   = 1;   // host -> client   (ledger + deal state)
    const byte KindReply        = 2;   // client -> host   accept / counter / decline
    const byte KindSaleReport   = 3;   // client -> host   "I closed a deal"
    const byte KindBarReport    = 4;   // client -> host   "I pushed them too far"
    const byte KindSubRefused   = 5;   // client -> host   they refused my substitute
    const byte KindMarkRead     = 6;   // client -> host   thread opened

    /// Reply kinds inside KindReply.
    const byte ReplyAccept  = 0;
    const byte ReplyCounter = 1;
    const byte ReplyDecline = 2;

    /// Same chunk size WorldSync uses. Named messages are size-capped and a busy
    /// ledger comfortably exceeds one packet.
    const int ChunkBytes = 8 * 1024;

    /// Floor between broadcasts. Selling a stack fires several mutations in a
    /// few frames; without this each would ship the whole ledger.
    const float MinBroadcastInterval = 0.25f;

    bool _registered;

    // host
    int _lastLedgerVersion = -1;
    int _lastDealVersion = -1;
    float _nextBroadcastAt;

    // client
    bool _synced;
    float _nextRequestAt;
    System.Text.StringBuilder _incoming;
    int _expectedChunks, _receivedChunks;

    /// <summary>
    /// The whole shared economy in one JsonUtility-safe object. Both halves ride
    /// together because they describe one thing — this buyer — and shipping them
    /// separately would let a guest see a bond update land before the sale that
    /// caused it.
    /// </summary>
    [System.Serializable]
    class EconomyState
    {
        public BuyerLedgerSave ledger = new BuyerLedgerSave();
        public MushroomDealState.Snapshot deals = new MushroomDealState.Snapshot();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Does not skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1).
        var go = new GameObject("EconomySync");
        DontDestroyOnLoad(go);
        go.AddComponent<EconomySync>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _registered = false;
        _synced = false;
        _nextRequestAt = 0f;
        _incoming = null;
        _lastLedgerVersion = -1;
        _lastDealVersion = -1;
    }

    /// True when this machine may run economy dice — the host, or single player.
    /// Kept here so the vendor code reads as one idea rather than repeating the
    /// WorldSync call with a different comment each time.
    public static bool IsAuthority => WorldSync.IsAuthority;

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { _registered = false; _synced = false; return; }

        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        if (nm.IsServer) HostTick(nm);
        else             ClientTick();
    }

    // ── host ─────────────────────────────────────────────────────────────

    void HostTick(NetworkManager nm)
    {
        int lv = BuyerLedger.Version;
        int dv = MushroomDealState.Version;
        if (lv == _lastLedgerVersion && dv == _lastDealVersion) return;
        if (Time.unscaledTime < _nextBroadcastAt) return;

        _lastLedgerVersion = lv;
        _lastDealVersion = dv;
        _nextBroadcastAt = Time.unscaledTime + MinBroadcastInterval;

        var ids = nm.ConnectedClientsIds;
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] != nm.LocalClientId) SendStateTo(ids[i]);
    }

    void SendStateTo(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !_registered) return;

        var state = new EconomyState();
        BuyerLedger.FillSave(state.ledger);
        state.deals = MushroomDealState.Capture();

        string json = JsonUtility.ToJson(state);
        int total = Mathf.Max(1, Mathf.CeilToInt(json.Length / (float)ChunkBytes));

        for (int i = 0; i < total; i++)
        {
            int start = i * ChunkBytes;
            int len = Mathf.Min(ChunkBytes, json.Length - start);
            string piece = json.Substring(start, len);

            var w = new FastBufferWriter(len * 4 + 64, Allocator.Temp, 1024 * 1024);
            try
            {
                w.WriteValueSafe(KindStateChunk);
                w.WriteValueSafe(i);
                w.WriteValueSafe(total);
                w.WriteValueSafe(piece);
                nm.CustomMessagingManager.SendNamedMessage(
                    Msg, clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally { w.Dispose(); }
        }
    }

    // ── client ───────────────────────────────────────────────────────────

    void ClientTick()
    {
        if (_synced) return;
        if (Time.unscaledTime < _nextRequestAt) return;
        if (!WorldSync.WorldReady) return;

        _nextRequestAt = Time.unscaledTime + 3f;   // retry until a state lands
        Send(w => w.WriteValueSafe(KindRequestState));
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;
        bool server = nm != null && nm.IsServer;

        switch (kind)
        {
            case KindRequestState when server: SendStateTo(senderId); break;

            // ⚠️ !server on every host→client handler: the authority is never
            // told its own state.
            case KindStateChunk when !server: ReceiveChunk(reader); break;

            case KindReply when server:      HandleReply(reader); break;
            case KindSaleReport when server: HandleSaleReport(reader); break;
            case KindBarReport when server:  HandleBarReport(reader); break;
            case KindSubRefused when server: HandleSubRefused(reader); break;
            case KindMarkRead when server:   HandleMarkRead(reader); break;
        }
    }

    void ReceiveChunk(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int index);
        reader.ReadValueSafe(out int total);
        reader.ReadValueSafe(out string piece);

        if (_incoming == null || total != _expectedChunks)
        {
            _incoming = new System.Text.StringBuilder();
            _expectedChunks = total;
            _receivedChunks = 0;
        }
        _incoming.Append(piece);
        _receivedChunks++;
        if (_receivedChunks < _expectedChunks) return;

        string json = _incoming.ToString();
        _incoming = null;

        EconomyState state;
        try { state = JsonUtility.FromJson<EconomyState>(json); }
        catch (System.Exception e)
        {
            Debug.LogError("[EconomySync] Economy state didn't parse: " + e.Message);
            return;
        }
        if (state == null) return;

        BuyerLedger.ApplySave(state.ledger);
        MushroomDealState.Apply(state.deals);
        _synced = true;

        // The phone is very likely open — the player just tapped a reply and is
        // watching for the answer — so redraw rather than wait for whatever
        // would otherwise have refreshed it.
        MessagesScreen.RefreshFromNetwork();
    }

    void HandleReply(FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte replyKind);
        reader.ReadValueSafe(out string buyerId);
        reader.ReadValueSafe(out int a);
        reader.ReadValueSafe(out int b);

        var dir = BuyerMessageDirector.Instance;
        var buyer = BuyerLedger.Get(buyerId);
        if (dir == null || buyer == null) return;

        // FIRST RESPONSE WINS, for free: each of these already refuses to act
        // unless the conversation is in the right state, so a second reply that
        // arrives a moment later finds it answered and does nothing. The state
        // broadcast that follows is what tells that player it was already dealt
        // with, rather than their tap vanishing silently.
        switch (replyKind)
        {
            case ReplyAccept:  dir.Accept(buyer, a); break;
            case ReplyCounter: dir.Counter(buyer, a, b); break;
            case ReplyDecline: dir.Decline(buyer); break;
        }
    }

    void HandleSaleReport(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string buyerId);
        reader.ReadValueSafe(out int pricePerCap);
        reader.ReadValueSafe(out int qty);
        reader.ReadValueSafe(out int tier);
        reader.ReadValueSafe(out byte keptAppointment);
        reader.ReadValueSafe(out byte substituted);
        if (string.IsNullOrEmpty(buyerId) || qty <= 0) return;

        int appetite = NPCMushroomPrice.AppetiteMaxOf(buyerId);
        MushroomDealState.RecordSale(buyerId, pricePerCap, qty, (MushroomTier)tier, appetite);
        // ReportDeal rolls for the regular conversion, which is why a guest
        // never runs it locally — two machines would roll differently.
        BuyerLedger.ReportDeal(buyerId, (MushroomTier)tier, pricePerCap, qty,
                               keptAppointment != 0, substituted != 0);
    }

    void HandleBarReport(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string buyerId);
        if (string.IsNullOrEmpty(buyerId)) return;
        MushroomDealState.Bar(buyerId);
        BuyerLedger.CounterRefused(buyerId);
        BuyerLedger.CancelAppointmentQuietly(buyerId);
    }

    void HandleSubRefused(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string buyerId);
        reader.ReadValueSafe(out int rolledPercent);
        if (string.IsNullOrEmpty(buyerId)) return;
        BuyerLedger.SubstitutionRefused(buyerId, rolledPercent);
    }

    void HandleMarkRead(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string buyerId);
        if (!string.IsNullOrEmpty(buyerId)) BuyerLedger.MarkRead(buyerId);
    }

    // ── outbound API, called from the vendor code ────────────────────────
    //
    // Each returns TRUE when it handled the action by sending it to the host,
    // so the caller knows to skip its local mutation. In single player and on
    // the host they all return false and nothing changes.

    static bool ShouldRoute()
    {
        var nm = NetworkManager.Singleton;
        return Instance != null && nm != null && nm.IsListening && !nm.IsServer;
    }

    /// <summary>
    /// A reply to a want-text. Dice live inside Accept/Counter (ResolveCounter
    /// decides accept / counter-back / refuse), so a guest must never run them
    /// — the two machines would hold different conversations with the same
    /// alien. Sent as a request; the answer arrives as a state broadcast.
    /// </summary>
    public static bool RouteAccept(string buyerId, int windowMinutes)
        => SendReply(ReplyAccept, buyerId, windowMinutes, 0);

    public static bool RouteCounter(string buyerId, int askPerCap, int offerQty)
        => SendReply(ReplyCounter, buyerId, askPerCap, offerQty);

    public static bool RouteDecline(string buyerId)
        => SendReply(ReplyDecline, buyerId, 0, 0);

    static bool SendReply(byte replyKind, string buyerId, int a, int b)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(buyerId)) return false;
        Instance.Send(w =>
        {
            w.WriteValueSafe(KindReply);
            w.WriteValueSafe(replyKind);
            w.WriteValueSafe(buyerId);
            w.WriteValueSafe(a);
            w.WriteValueSafe(b);
        });
        return true;
    }

    /// <summary>
    /// A guest closed a deal. The money and the mushrooms already changed hands
    /// on their machine — that half is personal and immediate — but the BUYER's
    /// half (appetite, bond, last paid) belongs to the host, which applies it and
    /// tells everybody.
    /// </summary>
    public static bool ReportSale(string buyerId, int pricePerCap, int qty, MushroomTier tier,
                                  bool keptAppointment, bool substituted)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(buyerId)) return false;
        Instance.Send(w =>
        {
            w.WriteValueSafe(KindSaleReport);
            w.WriteValueSafe(buyerId);
            w.WriteValueSafe(pricePerCap);
            w.WriteValueSafe(qty);
            w.WriteValueSafe((int)tier);
            w.WriteValueSafe((byte)(keptAppointment ? 1 : 0));
            w.WriteValueSafe((byte)(substituted ? 1 : 0));
        });
        return true;
    }

    public static bool ReportBarred(string buyerId)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(buyerId)) return false;
        Instance.Send(w => { w.WriteValueSafe(KindBarReport); w.WriteValueSafe(buyerId); });
        return true;
    }

    public static bool ReportSubstitutionRefused(string buyerId, int rolledPercent)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(buyerId)) return false;
        Instance.Send(w =>
        {
            w.WriteValueSafe(KindSubRefused);
            w.WriteValueSafe(buyerId);
            w.WriteValueSafe(rolledPercent);
        });
        return true;
    }

    public static bool RouteMarkRead(string buyerId)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(buyerId)) return false;
        Instance.Send(w => { w.WriteValueSafe(KindMarkRead); w.WriteValueSafe(buyerId); });
        return true;
    }

    // ── transport ────────────────────────────────────────────────────────

    /// Client → host only. Everything host → client goes through SendStateTo,
    /// which addresses one client explicitly.
    ///
    /// ⚠️ NEVER SendNamedMessageToAll — NGO delivers a broadcast back to the
    /// host, and a relay step on top of that is the Phase 2 rebroadcast storm.
    void Send(System.Action<FastBufferWriter> write)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || !_registered) return;

        var w = new FastBufferWriter(512, Allocator.Temp, 1024 * 64);
        try
        {
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }
}
