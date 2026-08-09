using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mirrors the stasis pod door across every machine in a session.
///
/// The door is driven by LOCAL proximity — it watches "the player" via
/// FindObjectOfType and opens when they are deep inside. Correct in single
/// player, invisible to everyone else in co-op: the host watched a joining
/// player sit in a sealed pod and then walk out through a shut door.
///
/// ── Why the first attempt didn't work ────────────────────────────────────
/// Registration was lazy and only ran on the machine that SENT. The host never
/// sent, so it never registered a handler, so it silently dropped every message.
/// Registration now happens on every machine, every frame it isn't already done.
///
/// ── Why it isn't just "mirror the state" ─────────────────────────────────
/// Both machines run the proximity rule against their OWN player, so a plain
/// mirror fights itself: the guest stands in the pod and wants it open, the host
/// has nobody inside and wants it shut, and the door flickers or shuts on
/// someone's head. So each machine broadcasts what IT wants and the door opens
/// if ANYONE wants it open — a union, not an overwrite. Closing only happens
/// once no one is asking for it.
///
/// Uses a named message for the same reason SolarSystemSync and the galactic
/// clock do: there is still no RPC layer, and a named message needs no
/// NetworkObject. Clients cannot reach each other, so the host relays.
/// </summary>
public class StasisDoorSync : MonoBehaviour
{
    public static StasisDoorSync Instance { get; private set; }

    const string MsgState = "StasisDoorState";

    /// True when some OTHER machine is holding the door open. The door reads
    /// this and refuses to close while it is set.
    public static bool RemoteWantsOpen { get; private set; }

    static bool _localWantsOpen;
    bool _registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        var go = new GameObject("StasisDoorSync");
        DontDestroyOnLoad(go);
        go.AddComponent<StasisDoorSync>();
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
        // New scene, new NetworkManager, and nobody is asking for the door yet.
        _registered = false;
        RemoteWantsOpen = false;
        _localWantsOpen = false;
    }

    /// Registration has to happen on EVERY machine, not just senders — that was
    /// the bug. Cheap: one bool check per frame once registered.
    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            if (_registered) { _registered = false; RemoteWantsOpen = false; }
            return;
        }
        if (_registered) return;
        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgState, OnStateMessage);
        _registered = true;
        // Announce our current wish so a machine joining mid-session agrees.
        Send(_localWantsOpen);
    }

    /// Called by StasisPodDoor whenever ITS OWN target changes.
    public static void NotifyLocalTarget(float target)
    {
        bool wantsOpen = target > 0.5f;
        if (wantsOpen == _localWantsOpen) return;
        _localWantsOpen = wantsOpen;
        if (Instance != null) Instance.Send(wantsOpen);
    }

    /// Convenience for the guest arrival: hold the door open for everyone.
    public static void BroadcastOpen()
    {
        var door = Object.FindObjectOfType<StasisPodDoor>();
        if (door != null) door.OpenHold();   // this routes through NotifyLocalTarget
        else NotifyLocalTarget(1f);
    }

    void Send(bool wantsOpen)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !_registered) return;

        var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        try
        {
            writer.WriteValueSafe((byte)(wantsOpen ? 1 : 0));
            if (nm.IsServer)
                nm.CustomMessagingManager.SendNamedMessageToAll(
                    MsgState, writer, NetworkDelivery.ReliableSequenced);
            else
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgState, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
        finally { writer.Dispose(); }
    }

    void OnStateMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte raw);
        bool wantsOpen = raw != 0;
        RemoteWantsOpen = wantsOpen;

        var door = Object.FindObjectOfType<StasisPodDoor>();
        if (door != null && wantsOpen) door.ApplyRemoteOpen();

        // Host relays to everyone else so a third machine agrees.
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        try
        {
            writer.WriteValueSafe(raw);
            foreach (var id in nm.ConnectedClientsIds)
            {
                if (id == senderId || id == NetworkManager.ServerClientId) continue;
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgState, id, writer, NetworkDelivery.ReliableSequenced);
            }
        }
        finally { writer.Dispose(); }
    }
}
