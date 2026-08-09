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

        if (nm.IsServer) { WorldReady = true; return; }   // the host IS the world

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
}
