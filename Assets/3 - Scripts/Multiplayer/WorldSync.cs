using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The spine every world system plugs into: who is allowed to decide things,
/// and how a joining player is handed the world that already exists.
///
/// ── The one rule ─────────────────────────────────────────────────────────
/// THE HOST OWNS EVERY TIMER AND EVERY DICE ROLL. Clients report inputs and
/// render what they are told. This is not a preference — the stasis pod door
/// cost three attempts to learn it. Mirroring state failed, and mirroring each
/// machine's "wish" failed, because both left every machine still RUNNING the
/// rules against its own copy. `IsAuthority` is how a system asks.
///
/// ── Why the base world needs no replication at all ───────────────────────
/// MushroomSpawner and TreeSpawner are deterministic cube-face hash functions
/// of (seed, face, cellU, cellV). The seed is authored, so both machines
/// already generate a byte-identical world. Only the DELTA differs — what has
/// been chopped, harvested, planted, built — and SaveData already describes
/// exactly that, compactly and body-relative.
///
/// So the join snapshot is not new serialisation: it is SaveCollector's own
/// capture pointed at a socket, applied through SaveCollector's own ordered
/// restore. A joining guest is, literally, loading the host's world.
///
/// ── Named messages, not RPCs ─────────────────────────────────────────────
/// Same reason StasisDoorSync, SolarSystemSync and GalaxyTime use them: this is
/// a scene-level singleton with no NetworkObject, so there is nothing to hang
/// an RPC on and nothing to wire into a prefab.
///
/// ⚠️ The host RECEIVES ITS OWN SendNamedMessageToAll — NGO delivers it locally
/// (CustomMessageManager.cs:342). Every host→client handler below therefore
/// checks `!IsServer`. Forgetting that is what made the pod door un-closable.
/// </summary>
public class WorldSync : MonoBehaviour
{
    public static WorldSync Instance { get; private set; }

    const string Msg = "WorldSync";

    // client -> host
    const byte KindRequestSnapshot = 0;
    // host -> client
    const byte KindSnapshotChunk   = 1;

    // Deltas. A client REPORTS to the host; the host applies and RELAYS to the
    // other clients. Same byte both ways - the handler tells them apart by
    // IsServer and by who sent it.
    const byte KindPropHit         = 2;   // propKind + bodyName + cellId + newHp
    const byte KindMushroomPlanted = 4;   // JSON PlantedMushroomSave
    const byte KindSaplingPlanted  = 5;   // JSON SaplingSave
    const byte KindBuildingPlaced  = 6;   // JSON PlacedBuildingSave

    /// Which streamed, cell-addressed prop a hit refers to. All three share the
    /// same TakeDamage/Break/RemoteHit shape, so one message covers them.
    public enum PropKind : byte { Tree = 0, Mushroom = 1, Crystal = 2 }

    /// Bytes of JSON per message. Named messages are size-capped, and a world
    /// with a few hundred planted mushrooms comfortably exceeds one packet, so
    /// the snapshot is chunked by hand rather than trusting fragmentation.
    const int ChunkBytes = 8 * 1024;

    /// <summary>
    /// May THIS machine decide things — run timers, roll dice, spawn?
    ///
    /// True in single player and on the host; false on a connected client.
    /// Gate the DECISION path on this, never the rendering path: a client still
    /// has to draw the mushroom, it just must not decide when it regrows.
    /// </summary>
    public static bool IsAuthority
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return true;   // single player
            return nm.IsServer;
        }
    }

    /// True once this client has received and applied the host's world. World
    /// systems can use it to hold off on anything that would be overwritten.
    public static bool WorldReady { get; private set; }

    /// <summary>
    /// True while a delta from someone else is being applied locally.
    ///
    /// Applying a remote chop runs the SAME code a local chop runs, which would
    /// report it straight back out - a loop that never settles. Every Report*
    /// below no-ops while this is set.
    /// </summary>
    public static bool ApplyingRemote { get; private set; }

    bool _registered;
    bool _requested;
    float _nextRequestAt;

    // Reassembly state for an incoming snapshot.
    StringBuilder _incoming;
    int _expectedChunks;
    int _receivedChunks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Deliberately does NOT skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons — the same dodge MultiplayerSession and
        // CharacterStore use for CLAUDE.md trap #1.
        var go = new GameObject("WorldSync");
        DontDestroyOnLoad(go);
        go.AddComponent<WorldSync>();
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
        _requested  = false;
        WorldReady  = false;
        _incoming   = null;
        _nextRequestAt = 0f;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        bool live = nm != null && nm.IsListening;

        if (!live)
        {
            // Back to single player: this machine decides everything again.
            if (_registered) { _registered = false; WorldReady = false; }
            return;
        }

        // Registration must happen on EVERY machine, not just senders. A host
        // that never sent used to never register and silently dropped
        // everything a client told it — the exact bug StasisDoorSync documents.
        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        if (nm.IsServer)
        {
            WorldReady = true;
            // One axe and one water bottle per player. Idempotent per client id.
            var ids = nm.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] != nm.LocalClientId) StorageSync.StockForPlayer(ids[i]);
            return;
        }

        // ── client: ask for the world, once the scene can receive it ──
        //
        // Pull, not push. The host cannot know when this machine's spawners and
        // celestial bodies are ready, and applying a snapshot into a half-built
        // scene silently drops content. Asking when WE are ready removes the
        // race entirely; the retry covers a request lost before the host
        // finished loading.
        if (_requested || Time.unscaledTime < _nextRequestAt) return;
        if (!LocalWorldReadyToReceive()) return;

        _nextRequestAt = Time.unscaledTime + 3f;   // retry until a snapshot lands
        SendToHost(KindRequestSnapshot);
    }

    /// The scene has the pieces a snapshot needs to land on: bodies to parent
    /// content to, and the spawners whose consumed-cell ledgers get restored.
    static bool LocalWorldReadyToReceive()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0) return false;
        return Object.FindObjectOfType<TreeSpawner>() != null
            || Object.FindObjectOfType<MushroomSpawner>() != null;
    }

    // ── outbound ─────────────────────────────────────────────────────────

    void SendToHost(byte kind)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || !_registered) return;

        var w = new FastBufferWriter(1, Allocator.Temp);
        try
        {
            w.WriteValueSafe(kind);
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }

    /// Host only: capture the world and stream it to one client.
    void SendSnapshotTo(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // SaveCollector.Capture is the same pass the autosave runs, so the
        // snapshot is by construction whatever a save would have recorded.
        SaveData data;
        try { data = SaveCollector.Capture("__worldsync__"); }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldSync] Couldn't capture the world: {e}");
            return;
        }

        string json = JsonUtility.ToJson(data);
        int total = Mathf.Max(1, Mathf.CeilToInt(json.Length / (float)ChunkBytes));

        for (int i = 0; i < total; i++)
        {
            int start = i * ChunkBytes;
            int len   = Mathf.Min(ChunkBytes, json.Length - start);
            string piece = json.Substring(start, len);

            var w = new FastBufferWriter(len * 4 + 32, Allocator.Temp);
            try
            {
                w.WriteValueSafe(KindSnapshotChunk);
                w.WriteValueSafe(i);
                w.WriteValueSafe(total);
                w.WriteValueSafe(piece);
                nm.CustomMessagingManager.SendNamedMessage(
                    Msg, clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally { w.Dispose(); }
        }

        Debug.Log($"[WorldSync] Sent world snapshot to client {clientId}: {json.Length} bytes in {total} chunk(s).");
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;

        switch (kind)
        {
            case KindRequestSnapshot when nm != null && nm.IsServer:
                SendSnapshotTo(senderId);
                break;

            // ⚠️ !IsServer: SendNamedMessage to a client never loops back, but a
            // future broadcast here would, and the authority must never be told
            // its own state. Keeping the guard makes that impossible to forget.
            case KindSnapshotChunk when nm != null && !nm.IsServer:
                ReceiveChunk(reader);
                break;

            case KindPropHit:
                HandlePropHit(reader, senderId);
                break;

            case KindMushroomPlanted:
            case KindSaplingPlanted:
            case KindBuildingPlaced:
                HandleJsonDelta(kind, reader, senderId);
                break;
        }
    }

    void ReceiveChunk(FastBufferReader reader)
    {
        reader.ReadValueSafe(out int index);
        reader.ReadValueSafe(out int total);
        reader.ReadValueSafe(out string piece);

        if (_incoming == null || total != _expectedChunks)
        {
            _incoming       = new StringBuilder();
            _expectedChunks = total;
            _receivedChunks = 0;
        }

        _incoming.Append(piece);
        _receivedChunks++;

        if (_receivedChunks < _expectedChunks) return;

        string json = _incoming.ToString();
        _incoming = null;
        ApplySnapshot(json);
    }

    void ApplySnapshot(string json)
    {
        SaveData data;
        try { data = JsonUtility.FromJson<SaveData>(json); }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldSync] Snapshot didn't parse: {e.Message}");
            return;
        }
        if (data == null) { Debug.LogError("[WorldSync] Snapshot parsed to null."); return; }

        // The world only — not the host's body, wallet, hotbar or ship.
        SaveCollector.ApplyWorldSubset(data);

        _requested = true;
        WorldReady = true;
        Debug.Log($"[WorldSync] World snapshot applied ({json.Length} bytes). " +
                  $"buildings={Count(data.buildings)} planted={Count(data.plantedMushrooms)} " +
                  $"saplings={Count(data.saplings)} enemies={Count(data.enemies)}");
    }

    static int Count<T>(System.Collections.Generic.List<T> l) => l != null ? l.Count : 0;

    // -- deltas: report what I just did --------------------------------
    //
    // Callers fire these unconditionally; each no-ops in single player, so
    // gameplay code never has to ask whether a session exists.

    /// <summary>
    /// A tree / mushroom / crystal was hit here, and this is its HP afterwards.
    ///
    /// The HIT is what travels, not "the prop is gone". The far side runs the
    /// same TakeDamage-shaped path, so the wobble, the topple-and-shrink and the
    /// despawn all happen there too - one message instead of three, and the prop
    /// stops silently vanishing on the other screen.
    ///
    /// HP is sent as an absolute, not a delta, so a dropped message self-corrects
    /// on the next swing instead of leaving the two machines permanently out of
    /// step. bodySlot below 0 means "not addressable" (a planted mushroom has no
    /// cell), and is simply not sent.
    /// </summary>
    public static void ReportPropHit(PropKind kind, int bodySlot, long cellId, int newHp)
    {
        if (ApplyingRemote || Instance == null || bodySlot < 0) return;

        string body = BodyNameFor(kind, bodySlot);
        if (string.IsNullOrEmpty(body)) return;

        Instance.SendPropHit(kind, body, cellId, newHp, skipClient: ulong.MaxValue);
    }

    static string BodyNameFor(PropKind kind, int bodySlot)
    {
        switch (kind)
        {
            case PropKind.Tree:
                return TreeSpawner.Instance != null ? TreeSpawner.Instance.BodyNameForSlot(bodySlot) : null;
            case PropKind.Mushroom:
            {
                var sp = Object.FindObjectOfType<MushroomSpawner>();
                return sp != null ? sp.BodyNameForSlot(bodySlot) : null;
            }
            default:
            {
                var sp = Object.FindObjectOfType<CrystalSpawner>();
                return sp != null ? sp.BodyNameForSlot(bodySlot) : null;
            }
        }
    }

    // -- delta transport -----------------------------------------------
    //
    // ⚠️ NEVER SendNamedMessageToAll FOR A DELTA.
    //
    // NGO delivers a broadcast back to the host itself
    // (CustomMessageManager.cs:342). The host's relay step then re-sent what it
    // had just received, which arrived back again - an INFINITE REBROADCAST
    // STORM. That was the whole bug behind "the host chops and the client never
    // sees it, and everything lags": the flood starved real delivery.
    //
    // So the host addresses connected CLIENTS explicitly, never itself, and
    // skips whoever reported the event in the first place.

    void SendPropHit(PropKind kind, string bodyName, long cellId, int newHp, ulong skipClient)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !_registered) return;

        var w = new FastBufferWriter(bodyName.Length * 4 + 48, Allocator.Temp);
        try
        {
            w.WriteValueSafe(KindPropHit);
            w.WriteValueSafe((byte)kind);
            w.WriteValueSafe(bodyName);
            w.WriteValueSafe(cellId);
            w.WriteValueSafe(newHp);
            Dispatch(w, skipClient, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void SendJsonDelta(byte kind, string json, ulong skipClient)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !_registered) return;

        var w = new FastBufferWriter(json.Length * 4 + 32, Allocator.Temp);
        try
        {
            w.WriteValueSafe(kind);
            w.WriteValueSafe(json);
            Dispatch(w, skipClient, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }

    /// Host: to every client except itself and `skipClient`. Client: to the host.
    void Dispatch(FastBufferWriter w, ulong skipClient, NetworkDelivery delivery)
    {
        var nm = NetworkManager.Singleton;
        if (!nm.IsServer)
        {
            nm.CustomMessagingManager.SendNamedMessage(Msg, NetworkManager.ServerClientId, w, delivery);
            return;
        }

        var ids = nm.ConnectedClientsIds;
        for (int i = 0; i < ids.Count; i++)
        {
            ulong id = ids[i];
            if (id == nm.LocalClientId) continue;   // never loop back to ourselves
            if (id == skipClient) continue;         // they told us; they already know
            nm.CustomMessagingManager.SendNamedMessage(Msg, id, w, delivery);
        }
    }

    // -- delta application ----------------------------------------------

    void HandlePropHit(FastBufferReader reader, ulong senderId)
    {
        reader.ReadValueSafe(out byte kindByte);
        reader.ReadValueSafe(out string bodyName);
        reader.ReadValueSafe(out long cellId);
        reader.ReadValueSafe(out int newHp);
        var kind = (PropKind)kindByte;

        ApplyPropHit(kind, bodyName, cellId, newHp);

        // The host is the arbiter: it applies, then relays to everyone EXCEPT
        // the client that reported it (they already did it locally).
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer)
            SendPropHit(kind, bodyName, cellId, newHp, skipClient: senderId);
    }

    static void ApplyPropHit(PropKind kind, string bodyName, long cellId, int newHp)
    {
        ApplyingRemote = true;
        try
        {
            switch (kind)
            {
                case PropKind.Tree:
                {
                    var sp = TreeSpawner.Instance;
                    int slot = sp != null ? sp.SlotForBodyNamePublic(bodyName) : -1;
                    if (slot < 0) return;
                    var all = SpawnedTree.AllTrees;
                    for (int i = all.Count - 1; i >= 0; i--)
                    {
                        var t = all[i];
                        if (t == null || t.IsDead || t.IsPlanted || t.IsSapling) continue;
                        if (t.BodySlot != slot || t.CellId != cellId) continue;
                        t.RemoteHit(newHp);
                        return;
                    }
                    // Not streamed in on this machine (too far away to be loaded):
                    // still record the cell so it never streams back.
                    if (newHp <= 0 && sp != null) sp.RemoteMineCell(bodyName, cellId);
                    return;
                }
                case PropKind.Mushroom:
                {
                    var sp = Object.FindObjectOfType<MushroomSpawner>();
                    int slot = sp != null ? sp.SlotForBodyNamePublic(bodyName) : -1;
                    if (slot < 0) return;
                    var all = SpawnedMushroom.AllMushrooms;
                    for (int i = all.Count - 1; i >= 0; i--)
                    {
                        var m = all[i];
                        if (m == null || m.IsDead || m.IsPlanted) continue;
                        if (m.BodySlot != slot || m.CellId != cellId) continue;
                        m.RemoteHit(newHp);
                        return;
                    }
                    if (newHp <= 0 && sp != null) sp.RemoteHarvestCell(bodyName, cellId);
                    return;
                }
                default:
                {
                    var sp = Object.FindObjectOfType<CrystalSpawner>();
                    int slot = sp != null ? sp.SlotForBodyNamePublic(bodyName) : -1;
                    if (slot < 0) return;
                    var all = SpawnedCrystal.AllCrystals;
                    for (int i = all.Count - 1; i >= 0; i--)
                    {
                        var c = all[i];
                        if (c == null || c.IsDead) continue;
                        if (c.BodySlot != slot || c.CellId != cellId) continue;
                        c.RemoteHit(newHp);
                        return;
                    }
                    if (newHp <= 0 && sp != null) sp.RemoteMineCell(bodyName, cellId);
                    return;
                }
            }
        }
        finally { ApplyingRemote = false; }
    }

    void HandleJsonDelta(byte kind, FastBufferReader reader, ulong senderId)
    {
        reader.ReadValueSafe(out string json);
        ApplyJsonDelta(kind, json);

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer) SendJsonDelta(kind, json, skipClient: senderId);
    }

    static void ApplyJsonDelta(byte kind, string json)
    {
        ApplyingRemote = true;
        try
        {
            if (kind == KindMushroomPlanted)
            {
                var save = JsonUtility.FromJson<PlantedMushroomSave>(json);
                if (save != null) SaveCollector.SpawnPlantedMushroom(save);
            }
            else if (kind == KindSaplingPlanted)
            {
                var save = JsonUtility.FromJson<SaplingSave>(json);
                if (save != null) SaveCollector.SpawnSapling(save);
            }
            else if (kind == KindBuildingPlaced)
            {
                var save = JsonUtility.FromJson<PlacedBuildingSave>(json);
                if (save != null) SaveCollector.SpawnPlacedBuilding(save);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[WorldSync] Bad planted-prop delta: " + e.Message);
        }
        finally { ApplyingRemote = false; }
    }

    /// A spore was planted here. Carried as a PlantedMushroomSave so the
    /// receiver rebuilds it through exactly the code the save system uses.
    public static void ReportMushroomPlanted(MushroomGrowth mg)
    {
        if (ApplyingRemote || Instance == null || mg == null) return;
        var body = mg.Body != null ? mg.Body : mg.GetComponentInParent<CelestialBody>();
        if (body == null) return;

        var bt = body.transform;
        var save = new PlantedMushroomSave
        {
            bodyName       = body.bodyName,
            localPos       = bt.InverseTransformPoint(mg.transform.position),
            localRot       = Quaternion.Inverse(bt.rotation) * mg.transform.rotation,
            growth         = mg.IsMature ? 1f : mg.Growth,
            speciesKey     = mg.SpeciesKey,
            sizeMultiplier = mg.SizeMultiplier,
        };
        Instance.SendJsonDelta(KindMushroomPlanted, JsonUtility.ToJson(save), skipClient: ulong.MaxValue);
    }

    /// <summary>
    /// A building was placed here.
    ///
    /// Travels as a PlacedBuildingSave, matched on the far side by prefab NAME
    /// against BuildMenuUI.buildables - the same key ApplyBuildings uses, so a
    /// building placed over the network and one restored from disk are built by
    /// identical code.
    ///
    /// The "_Placed" suffix and CelestialBody parenting are not cosmetic: that
    /// exact naming is how the save system finds placed buildings later
    /// (CLAUDE.md), and SpawnPlacedBuilding preserves it.
    /// </summary>
    public static void ReportBuildingPlaced(GameObject placed, BuildableEntry entry, CelestialBody body)
    {
        if (ApplyingRemote || Instance == null) return;
        if (placed == null || entry == null || entry.prefab == null || body == null) return;

        var bt = body.transform;
        var save = new PlacedBuildingSave
        {
            prefabKey      = entry.prefab.name,
            parentBodyName = body.bodyName,
            localPos       = bt.InverseTransformPoint(placed.transform.position),
            localRot       = Quaternion.Inverse(bt.rotation) * placed.transform.rotation,
        };
        Instance.SendJsonDelta(KindBuildingPlaced, JsonUtility.ToJson(save), skipClient: ulong.MaxValue);
    }

    /// <summary>
    /// A tree sapling was planted here.
    ///
    /// Separate from the spore path because a sapling is a different thing on
    /// disk - SaplingSave carries a prefabIndex into TreeSpawner.treePrefabs
    /// where a mushroom carries a species key - but the shape is identical:
    /// send the save record, rebuild it with the save system's own spawner.
    /// </summary>
    public static void ReportSaplingPlanted(SaplingGrowth sg)
    {
        if (ApplyingRemote || Instance == null || sg == null) return;
        var body = sg.Body != null ? sg.Body : sg.GetComponentInParent<CelestialBody>();
        if (body == null) return;

        var bt = body.transform;
        var save = new SaplingSave
        {
            bodyName    = body.bodyName,
            localPos    = bt.InverseTransformPoint(sg.transform.position),
            localRot    = Quaternion.Inverse(bt.rotation) * sg.transform.rotation,
            growth      = sg.IsMature ? 1f : sg.Growth,
            prefabIndex = sg.PrefabIndex,
        };
        Instance.SendJsonDelta(KindSaplingPlanted, JsonUtility.ToJson(save), skipClient: ulong.MaxValue);
    }
}
