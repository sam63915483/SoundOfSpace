using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The shuttle computer as a shared object: the project shelf, the plugin rack,
/// the print table, the cassette machine — and the story counters the landlord
/// keeps his books in.
///
/// ── What is shared and what is personal ──────────────────────────────────
/// THE COMPUTER is shared. One shelf, one rack, one slot. A project your
/// partner saved is on the shelf when you sit down; a plugin either of you buys
/// is in the rack for both, which is Sam's call and what makes a $180 module a
/// household investment rather than a race. The cassette machine has exactly one
/// slot and one eject, so it cannot be anything but shared.
///
/// THE MONEY and THE TAPES are personal. Buying a plugin spends the buyer's own
/// wallet. Seating a blank spends the blank out of the seater's own pack, and
/// whoever lifts the finished tape off the eject is who ends up holding it.
/// Rent is the same split: the debt is the household's, the credits are yours.
///
/// ── Whole-snapshot replication, same as EconomySync ──────────────────────
/// The host ships the entire shelf, rack, print table, machine and story
/// director whenever any of them changes. The save schema is the network
/// schema — TraxLibrarySave and StoryDirectorSave travel verbatim — so there is
/// no second format to drift, and no such thing as a missed delta: two machines
/// cannot disagree about who owns SIREN and stay that way.
///
/// Coalesced through version counters, so a burst of edits in one frame is one
/// message, and only sent when something actually happened.
///
/// ── Why the whole story director ─────────────────────────────────────────
/// Sam asked for rent. Rent lives in StoryDirector's counters, in the same
/// dictionary as the tape-career total and every story flag. Shipping a
/// hand-picked subset would be a second schema that drifts out of step with the
/// first the moment someone adds a counter, so the whole director goes. The
/// guest's own gate cascade and cold-open timer are switched off in
/// StoryDirector.Update to match — host decides, guest renders.
///
/// Named messages, not RPCs: this is a DontDestroyOnLoad singleton with no
/// NetworkObject, exactly like WorldSync and EconomySync.
/// </summary>
public class TraxSync : MonoBehaviour
{
    public static TraxSync Instance { get; private set; }

    const string Msg = "TraxSync";

    const byte KindRequestState  = 0;   // client -> host
    const byte KindStateChunk    = 1;   // host -> client   shelf + rack + prints + deck + story

    // The shelf. A guest edits its own copy of a song freely; SAVING it is a
    // shelf mutation and therefore the host's to perform.
    const byte KindProjectSave   = 2;   // client -> host   name + song JSON
    const byte KindProjectDelete = 3;   // client -> host   project id

    const byte KindPluginInstall = 4;   // client -> host   module name (already paid for)

    // The cassette machine. Every one of these has already moved an ITEM on the
    // asking machine, or is about to be handed one back.
    const byte KindDeckInsert    = 5;   // client -> host   kind + tier (blank already spent)
    const byte KindDeckEject     = 6;   // client -> host   "give me the blank back"
    const byte KindDeckTake      = 7;   // client -> host   "give me the printed tape"
    const byte KindDeckPrint     = 8;   // client -> host   project id + format
    const byte KindDeckReturn    = 9;   // client -> host   "no room, put it back on the eject"

    const byte KindGrantBlank    = 10;  // host -> client   here is a blank (kind + tier)
    const byte KindGrantTape     = 11;  // host -> client   here is a printed tape (print id)
    const byte KindDeckPutBack   = 14;  // client -> host   "no room, seat it" — NEVER granted back

    const byte KindRentPay       = 12;  // client -> host   credits already spent locally

    const byte KindTraxAppInstall = 15; // client -> host   USB stick already consumed locally

    /// Same chunk size WorldSync and EconomySync use. Named messages are
    /// size-capped and a full shelf of eight-section songs exceeds one packet.
    const int ChunkBytes = 8 * 1024;

    /// Floor between broadcasts. Dragging a knob mutates nothing here, but
    /// saving a project and printing from it inside the same second would
    /// otherwise ship the shelf twice.
    const float MinBroadcastInterval = 0.25f;

    bool _registered;

    // host
    int _lastTraxVersion = -1;
    int _lastDeckVersion = -1;
    int _lastStoryVersion = -1;
    float _nextBroadcastAt;

    // client
    bool _synced;
    float _nextRequestAt;
    System.Text.StringBuilder _incoming;
    int _expectedChunks, _receivedChunks;

    /// <summary>
    /// True while a host snapshot is being applied locally. Applying the shelf
    /// runs TraxLibrary.Apply, which bumps the version — without this flag a
    /// host that also happened to be applying would re-broadcast its own state
    /// forever. Nothing below reports while it is set.
    /// </summary>
    public static bool ApplyingRemote { get; private set; }

    /// <summary>
    /// Bumped every time a host snapshot lands, so an open screen can notice
    /// that the shelf or the rack changed under it without polling every field.
    /// The same nudge-a-counter idiom MessagesScreen uses.
    /// </summary>
    public static int RemoteRevision { get; private set; }

    /// <summary>
    /// The whole shared computer in one JsonUtility-safe object.
    ///
    /// TraxLibrarySave already carries the shelf, the installed plugins, the
    /// print table AND the machine's three fields — it is exactly what the world
    /// save writes — so there is nothing to invent here beyond bolting the story
    /// director onto it.
    /// </summary>
    [System.Serializable]
    class TraxState
    {
        public TraxLibrarySave trax = new TraxLibrarySave();
        public StoryDirectorSave story = new StoryDirectorSave();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Does not skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1).
        var go = new GameObject("TraxSync");
        DontDestroyOnLoad(go);
        go.AddComponent<TraxSync>();
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
        _lastTraxVersion = -1;
        _lastDeckVersion = -1;
        _lastStoryVersion = -1;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { _registered = false; _synced = false; return; }

        // Registration happens on EVERY machine, not just senders — a host that
        // never sent used to never register and silently dropped everything the
        // clients told it (EnemySync documents the bug).
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
        int tv = TraxLibrary.Version;
        int dv = CassetteDeck.Version;
        int sv = StoryDirector.Version;
        if (tv == _lastTraxVersion && dv == _lastDeckVersion && sv == _lastStoryVersion) return;
        if (Time.unscaledTime < _nextBroadcastAt) return;

        _lastTraxVersion = tv;
        _lastDeckVersion = dv;
        _lastStoryVersion = sv;
        _nextBroadcastAt = Time.unscaledTime + MinBroadcastInterval;

        var ids = nm.ConnectedClientsIds;
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] != nm.LocalClientId) SendStateTo(ids[i]);
    }

    void SendStateTo(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !_registered) return;

        var state = new TraxState();
        state.trax = TraxLibrary.Capture();
        // The machine's own three fields ride the same blob, filled in here
        // rather than inside TraxLibrary.Capture because CassetteDeck talks to
        // the Hotbar and TraxLibrary is compiled with no Unity references.
        CassetteDeck.Capture(state.trax);
        if (StoryDirector.Instance != null) StoryDirector.Instance.SaveTo(state.story);

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

    /// One message straight back to the player who asked, carrying an item the
    /// machine gave up. Host only.
    void SendTo(ulong clientId, System.Action<FastBufferWriter> write, int sizeHint = 256)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !_registered) return;
        if (clientId == nm.LocalClientId) return;   // the host takes items directly

        var w = new FastBufferWriter(sizeHint, Allocator.Temp, 1024 * 64);
        try
        {
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
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
            case KindGrantBlank when !server: HandleGrantBlank(reader); break;
            case KindGrantTape  when !server: HandleGrantTape(reader); break;

            case KindProjectSave   when server: HandleProjectSave(reader); break;
            case KindProjectDelete when server: HandleProjectDelete(reader); break;
            case KindPluginInstall when server: HandlePluginInstall(reader); break;
            case KindDeckInsert    when server: HandleDeckInsert(reader, senderId); break;
            case KindDeckEject     when server: HandleDeckEject(senderId); break;
            case KindDeckTake      when server: HandleDeckTake(senderId); break;
            case KindDeckPrint     when server: HandleDeckPrint(reader); break;
            case KindDeckReturn    when server: HandleDeckReturn(reader); break;
            case KindDeckPutBack   when server: HandleDeckPutBack(reader); break;
            case KindRentPay       when server: HandleRentPay(reader); break;
            case KindTraxAppInstall when server: HandleTraxAppInstall(); break;
        }
    }

    /// The guest's stick has already been consumed on its own machine; the app
    /// install is world state, so the host performs it and the next snapshot
    /// carries it to everyone. Idempotent, so a double-send costs nothing.
    void HandleTraxAppInstall()
    {
        TraxLibrary.InstallApp();
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

        TraxState state;
        try { state = JsonUtility.FromJson<TraxState>(json); }
        catch (System.Exception e)
        {
            Debug.LogError("[TraxSync] Computer state didn't parse: " + e.Message);
            return;
        }
        if (state == null) return;

        ApplyingRemote = true;
        try
        {
            // Same order the save system uses, and for the same reason: the deck
            // validates its ejected tape against the print table that
            // TraxLibrary.Apply restores, and an empty table would silently drop
            // the tape off the machine.
            TraxLibrary.Apply(state.trax);
            CassetteDeck.Apply(state.trax);
            if (StoryDirector.Instance != null) StoryDirector.Instance.LoadFrom(state.story);
        }
        finally { ApplyingRemote = false; }

        _synced = true;
        RemoteRevision++;
    }

    // ── host-side handlers ───────────────────────────────────────────────

    void HandleProjectSave(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string name);
        reader.ReadValueSafe(out string songJson);
        if (string.IsNullOrEmpty(name)) return;

        TraxSong song = TraxSongWire.FromJson(songJson);
        if (song == null || song.sections.Count == 0) return;
        TraxLibrary.Save(name, song.sections[0].track, System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(), song);
    }

    void HandleProjectDelete(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string id);
        if (!string.IsNullOrEmpty(id)) TraxLibrary.Delete(id);
    }

    void HandlePluginInstall(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string module);
        // The guest's wallet has already paid. Install is idempotent, so a
        // double-send costs nothing.
        if (!string.IsNullOrEmpty(module)) TraxLibrary.Install(module);
    }

    void HandleDeckInsert(FastBufferReader reader, ulong senderId)
    {
        reader.ReadValueSafe(out int kind);
        reader.ReadValueSafe(out int tier);
        if (tier <= 0) return;

        // The blank left the guest's pack before this message was sent, so a
        // refusal has to hand it straight back or it is simply gone. This is the
        // whole reason the grant path exists.
        if (CassetteDeck.InsertRemote(kind, tier)) return;

        SendTo(senderId, w => { w.WriteValueSafe(KindGrantBlank); w.WriteValueSafe(kind); w.WriteValueSafe(tier); });
        // ⚠️ And re-send the machine. The guest seated that blank optimistically
        // so the animation would play, and a refusal changes nothing here — so
        // the version counter doesn't move and the ordinary broadcast never
        // fires. Without this the guest is left looking at a cassette that isn't
        // in the machine, permanently.
        SendStateTo(senderId);
    }

    void HandleDeckEject(ulong senderId)
    {
        // Refusals re-send the machine for the same reason KindDeckInsert does:
        // the asker already emptied its own copy, and "nothing happened" would
        // otherwise never be corrected.
        if (!CassetteDeck.EjectBlankRemote(out int kind, out int tier)) { SendStateTo(senderId); return; }
        SendTo(senderId, w => { w.WriteValueSafe(KindGrantBlank); w.WriteValueSafe(kind); w.WriteValueSafe(tier); });
    }

    void HandleDeckTake(ulong senderId)
    {
        if (!CassetteDeck.TakeEjectedRemote(out string printId)) { SendStateTo(senderId); return; }
        SendTo(senderId, w => { w.WriteValueSafe(KindGrantTape); w.WriteValueSafe(printId); },
               printId.Length * 4 + 64);
    }

    void HandleDeckPrint(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string name);
        reader.ReadValueSafe(out int kind);
        reader.ReadValueSafe(out string songJson);

        if (!CassetteDeck.HasCassette || CassetteDeck.HasEjected) return;
        // The format is the seated blank's, and the guest pressed against its
        // own copy of the machine. If those disagree the snapshot that says so
        // is already in flight — refuse rather than press a demo onto a Full.
        if (CassetteDeck.InsertedKind != kind) return;

        TraxSong song = TraxSongWire.FromJson(songJson);
        if (song == null) return;

        var press = TraxPrints.Register(name, song, kind, CassetteDeck.InsertedTier);
        if (press != null) CassetteDeck.PrintTo(press.id);
    }

    void HandleDeckReturn(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string printId);
        if (!string.IsNullOrEmpty(printId)) CassetteDeck.ReturnToEject(printId);
    }

    /// <summary>
    /// A blank coming back because the player it was granted to had no room.
    ///
    /// ⚠️ TERMINAL. Unlike an insert, this never grants anything back on
    /// failure — that mutual hand-off is exactly the loop this message exists
    /// to break. If the slot has been filled in the meantime the blank is
    /// genuinely gone, which is worth a line in the log and is far better than
    /// two machines volleying the whole shelf at each other forever.
    /// </summary>
    void HandleDeckPutBack(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int kind);
        reader.ReadValueSafe(out int tier);
        if (tier <= 0) return;
        if (!CassetteDeck.InsertRemote(kind, tier))
            Debug.LogWarning("[TraxSync] A blank came back to a full machine and a full pack — lost.");
    }

    void HandleRentPay(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int amount);
        // The credits already left the guest's wallet; only the household
        // balance is ours to move.
        MushroomQuest.ApplyRentPayment(amount);
    }

    // ── grants: an item the machine handed back ──────────────────────────

    void HandleGrantBlank(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int kind);
        reader.ReadValueSafe(out int tier);
        if (Hotbar.Instance == null || tier <= 0) return;

        var id = BlankIdFor(kind, tier);
        int leftover = Hotbar.Instance.AddResource(id, 1);
        if (leftover <= 0) return;

        // No room: put it back in the machine rather than letting a TAPE II
        // evaporate — which is also exactly what single player does when the
        // pack is full ("the blank STAYS IN THE MACHINE").
        //
        // ⚠️ A PUT-BACK, not another insert. An insert that the host refuses is
        // granted straight back, and a grant that this full pack refuses would
        // insert again — a message that bounces forever at round-trip rate,
        // shipping the whole shelf every cycle. The put-back is terminal by
        // construction: the host seats it or reports it, and never hands it
        // back a second time.
        Send(w =>
        {
            w.WriteValueSafe(KindDeckPutBack);
            w.WriteValueSafe(kind);
            w.WriteValueSafe(tier);
        });
        StoryImpactNotice.Show("NO ROOM — THE BLANK STAYED IN THE MACHINE.", 3f);
    }

    void HandleGrantTape(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string printId);
        if (Hotbar.Instance == null || string.IsNullOrEmpty(printId)) return;

        if (Hotbar.Instance.AddCassette(printId, 1) > 0) return;
        // Full pack: hand it back to the eject, exactly as the single-player
        // path leaves it sitting on the machine.
        Send(w => { w.WriteValueSafe(KindDeckReturn); w.WriteValueSafe(printId); },
             printId.Length * 4 + 64);
    }

    static Hotbar.ItemId BlankIdFor(int kind, int tier)
    {
        if (kind == TraxKind.Full)
            return tier >= 2 ? Hotbar.ItemId.BlankTapeFullT2 : Hotbar.ItemId.BlankTapeFullT1;
        if (kind == TraxKind.Half)
            return tier >= 2 ? Hotbar.ItemId.BlankTapeHalfT2 : Hotbar.ItemId.BlankTapeHalfT1;
        return tier >= 2 ? Hotbar.ItemId.BlankTapeT2 : Hotbar.ItemId.BlankTapeT1;
    }

    // ── outbound API, called from the computer + shop + landlord ─────────
    //
    // Each returns TRUE when it handled the action by sending it to the host, so
    // the caller knows to skip its local mutation. In single player and on the
    // host they all return false and nothing changes.

    static bool ShouldRoute()
    {
        var nm = NetworkManager.Singleton;
        return Instance != null && !ApplyingRemote
            && nm != null && nm.IsListening && !nm.IsServer;
    }

    /// <summary>
    /// Save a project onto the shared shelf. The song travels whole, as the same
    /// section rows the world save writes, so the host rebuilds exactly what the
    /// guest was looking at.
    /// </summary>
    public static bool RouteProjectSave(string name, TraxSong song)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(name) || song == null) return false;
        string json = TraxSongWire.ToJson(song);
        Instance.Send(w =>
        {
            w.WriteValueSafe(KindProjectSave);
            w.WriteValueSafe(name);
            w.WriteValueSafe(json);
        }, json.Length * 4 + name.Length * 4 + 128);
        return true;
    }

    public static bool RouteProjectDelete(string id)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(id)) return false;
        Instance.Send(w => { w.WriteValueSafe(KindProjectDelete); w.WriteValueSafe(id); },
                      id.Length * 4 + 64);
        return true;
    }

    /// <summary>
    /// A plugin was bought. The wallet has already paid locally — money is
    /// personal — and the rack is world state, so only the install travels.
    /// </summary>
    public static bool RoutePluginInstall(string module)
    {
        if (!ShouldRoute() || string.IsNullOrEmpty(module)) return false;
        Instance.Send(w => { w.WriteValueSafe(KindPluginInstall); w.WriteValueSafe(module); },
                      module.Length * 4 + 64);
        return true;
    }

    /// <summary>
    /// The TRAX app was installed off a USB stick. The stick has already been
    /// consumed locally — it was personal, like the wallet — and the install
    /// is world state, so only that travels. Mirrors RoutePluginInstall.
    /// </summary>
    public static bool RouteTraxAppInstall()
    {
        if (!ShouldRoute()) return false;
        Instance.Send(w => w.WriteValueSafe(KindTraxAppInstall), 64);
        return true;
    }

    /// <summary>
    /// A blank was seated. The caller has ALREADY spent it out of its own pack
    /// (that half is personal and immediate); if the host refuses because the
    /// slot was taken a heartbeat earlier, it hands the blank straight back.
    /// </summary>
    public static bool RouteDeckInsert(int kind, int tier)
    {
        if (!ShouldRoute() || tier <= 0) return false;
        RouteDeckInsertRaw(kind, tier);
        return true;
    }

    static void RouteDeckInsertRaw(int kind, int tier)
    {
        if (Instance == null) return;
        Instance.Send(w =>
        {
            w.WriteValueSafe(KindDeckInsert);
            w.WriteValueSafe(kind);
            w.WriteValueSafe(tier);
        });
    }

    /// Take the unprinted blank back out. The host empties the slot and grants
    /// the blank to whoever asked.
    public static bool RouteDeckEject()
    {
        if (!ShouldRoute()) return false;
        Instance.Send(w => w.WriteValueSafe(KindDeckEject));
        return true;
    }

    /// Lift the finished tape off the machine. The host clears the eject and
    /// grants the tape to whoever asked — first ask wins, and the loser simply
    /// finds the eject empty.
    public static bool RouteDeckTake()
    {
        if (!ShouldRoute()) return false;
        Instance.Send(w => w.WriteValueSafe(KindDeckTake));
        return true;
    }

    /// <summary>
    /// Press the seated blank. The SONG travels rather than a shelf id, because
    /// what gets pressed is what is on the deck right now — unsaved edits and
    /// all — and a lookup by project would quietly print the last saved version
    /// instead. The host mints the id, so both machines agree on the pressing
    /// and identical presses still stack.
    /// </summary>
    public static bool RouteDeckPrint(string name, int kind, TraxSong press)
    {
        if (!ShouldRoute() || press == null) return false;
        string json = TraxSongWire.ToJson(press);
        Instance.Send(w =>
        {
            w.WriteValueSafe(KindDeckPrint);
            w.WriteValueSafe(name ?? "");
            w.WriteValueSafe(kind);
            w.WriteValueSafe(json);
        }, json.Length * 4 + 256);
        return true;
    }

    /// Rent paid. The credits are already gone from the payer's wallet; the
    /// household balance is the host's to reduce.
    public static bool RouteRentPay(int amount)
    {
        if (!ShouldRoute() || amount <= 0) return false;
        Instance.Send(w => { w.WriteValueSafe(KindRentPay); w.WriteValueSafe(amount); });
        return true;
    }

    // ── transport ────────────────────────────────────────────────────────

    /// Client → host only. Everything host → client goes through SendStateTo or
    /// SendTo, which address one client explicitly.
    ///
    /// ⚠️ NEVER SendNamedMessageToAll — NGO delivers a broadcast back to the
    /// host, and a relay step on top of that is the Phase 2 rebroadcast storm.
    void Send(System.Action<FastBufferWriter> write, int sizeHint = 512)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || !_registered) return;

        var w = new FastBufferWriter(sizeHint, Allocator.Temp, 1024 * 1024);
        try
        {
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }
}
