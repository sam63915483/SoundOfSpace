using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the village house doors in the same state on every screen.
///
/// ── Why this is a fraction of the size of StasisDoorSync ─────────────────
/// The stasis pod door is a state MACHINE — it opens on a press, closes on a
/// timer, seals when you step in — and every machine was running that machine
/// against its own player and its own clock, so the two copies drifted and
/// fought. Fixing it needed a single owner and a zone protocol.
///
/// A village door is a BOOLEAN. Nothing moves it but a person pressing F, so
/// there is no simulation to drift. That means the presser can swing its own
/// door immediately and simply tell the host, and the host's broadcast keeps
/// everyone honest.
///
/// Two rules carry the whole thing:
///
///   • State on the wire is ABSOLUTE (open / closed), never a toggle. A
///     duplicated or reordered message therefore cannot invert a door — the
///     failure mode that makes toggle protocols so miserable to debug.
///   • The host re-broadcasts a full SNAPSHOT of every door on a slow tick. A
///     late joiner walks into a village that already looks right, and any
///     dropped update self-corrects within a couple of seconds instead of
///     leaving one player staring at a door the other walked through.
///
/// Named messages rather than RPCs, for the same reason as the orbit, clock and
/// stasis-door syncs: there is no RPC layer here, and a named message needs no
/// NetworkObject.
///
/// CLAUDE.md trap #1: this deliberately does NOT skip MainMenu in AutoCreate,
/// so it never needs seeding in MainMenuController.EnsureGameplaySingletons —
/// the same dodge WorldSync, StorageSync and EnemySync use.
/// </summary>
public class VillageDoorSync : MonoBehaviour
{
    public static VillageDoorSync Instance { get; private set; }

    const string Msg = "VillageDoor";

    const byte KindRequest  = 0;   // client -> host : "I want this door open/closed"
    const byte KindState    = 1;   // host -> all    : one door's truth
    const byte KindSnapshot = 2;   // host -> all    : every door's truth

    /// Full snapshot cadence. Ten doors is a handful of bytes, so this can be
    /// generous without costing anything.
    const float SnapshotInterval = 2f;

    bool _registered;
    float _snapshotTimer;
    int _lastClientCount = -1;

    /// Scratch list for SendSnapshot, so the slow tick allocates nothing.
    readonly System.Collections.Generic.List<VillageDoor> _live =
        new System.Collections.Generic.List<VillageDoor>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Deliberately does NOT skip MainMenu — see the class summary.
        var go = new GameObject("VillageDoorSync");
        DontDestroyOnLoad(go);
        go.AddComponent<VillageDoorSync>();
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
        _lastClientCount = -1;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        bool live = nm != null && nm.IsListening;
        if (!live) { _registered = false; return; }

        // Registration must happen on EVERY machine, not only senders — a host
        // that never sent would never register and would silently drop
        // everything a client told it. (The bug StasisDoorSync documents.)
        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        if (!nm.IsServer) return;

        int clients = nm.ConnectedClientsIds.Count;
        if (clients <= 1) { _lastClientCount = clients; return; }

        // Somebody just joined: don't make them wait out the tick.
        bool joined = clients != _lastClientCount;
        _lastClientCount = clients;

        _snapshotTimer += Time.unscaledDeltaTime;
        if (joined || _snapshotTimer >= SnapshotInterval)
        {
            _snapshotTimer = 0f;
            SendSnapshot();
        }
    }

    // ── outbound ─────────────────────────────────────────────────────────

    /// <summary>Called by VillageDoor the instant a player presses F. The door
    /// has already swung locally; this either publishes it (host) or asks for it
    /// (client).</summary>
    public static void RequestSetOpen(VillageDoor door, bool open)
    {
        if (door == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;                 // single player
        if (Instance == null || !Instance._registered) return;

        if (nm.IsServer) Instance.SendState(door.DoorId, open);
        else             Instance.SendRequest(door.DoorId, open);
    }

    void SendRequest(int doorId, bool open)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer) return;
        var w = new FastBufferWriter(6, Allocator.Temp);
        try
        {
            w.WriteValueSafe(KindRequest);
            w.WriteValueSafe(doorId);
            w.WriteValueSafe((byte)(open ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void SendState(int doorId, bool open)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        var w = new FastBufferWriter(6, Allocator.Temp);
        try
        {
            w.WriteValueSafe(KindState);
            w.WriteValueSafe(doorId);
            w.WriteValueSafe((byte)(open ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessageToAll(Msg, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void SendSnapshot()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Count the LIVE doors before writing any of them. Writing a count and
        // then skipping a destroyed entry would leave the reader parsing five
        // bytes that aren't there and corrupt every field after it.
        _live.Clear();
        var doors = VillageDoor.AllDoors;
        for (int i = 0; i < doors.Count; i++)
            if (doors[i] != null) _live.Add(doors[i]);
        int n = _live.Count;
        if (n == 0) return;

        var w = new FastBufferWriter(5 + n * 5, Allocator.Temp, 1024 * 64);
        try
        {
            w.WriteValueSafe(KindSnapshot);
            w.WriteValueSafe(n);
            for (int i = 0; i < n; i++)
            {
                w.WriteValueSafe(_live[i].DoorId);
                w.WriteValueSafe((byte)(_live[i].IsOpen ? 1 : 0));
            }
            nm.CustomMessagingManager.SendNamedMessageToAll(Msg, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;

        switch (kind)
        {
            case KindRequest when nm != null && nm.IsServer:
            {
                reader.ReadValueSafe(out int doorId);
                reader.ReadValueSafe(out byte open);
                var door = Find(doorId);
                if (door != null) door.NetSetOpen(open != 0, instant: false);
                // Publish it, so the asking client and everyone else agree.
                SendState(doorId, open != 0);
                break;
            }

            // ⚠️ `!nm.IsServer` matters here for the reason StasisDoorSync
            // documents at length: SendNamedMessageToAll targets
            // ConnectedClientsIds, which on a host INCLUDES the host's own id,
            // so the host receives its own broadcasts. Being told its own state
            // is meaningless at best; the host is the authority and ignores it.
            case KindState when nm != null && !nm.IsServer:
            {
                reader.ReadValueSafe(out int doorId);
                reader.ReadValueSafe(out byte open);
                var door = Find(doorId);
                if (door != null) door.NetSetOpen(open != 0, instant: false);
                break;
            }

            case KindSnapshot when nm != null && !nm.IsServer:
            {
                reader.ReadValueSafe(out int count);
                for (int i = 0; i < count; i++)
                {
                    reader.ReadValueSafe(out int doorId);
                    reader.ReadValueSafe(out byte open);
                    var door = Find(doorId);
                    // Snapshots are a correction, not an event: snap silently
                    // rather than swinging and creaking a door nobody touched.
                    if (door != null) door.NetSetOpen(open != 0, instant: true);
                }
                break;
            }
        }
    }

    static VillageDoor Find(int doorId)
    {
        var doors = VillageDoor.AllDoors;
        for (int i = 0; i < doors.Count; i++)
            if (doors[i] != null && doors[i].DoorId == doorId) return doors[i];
        return null;
    }
}
