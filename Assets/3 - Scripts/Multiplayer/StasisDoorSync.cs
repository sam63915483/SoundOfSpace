using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes the stasis pod door open on EVERYONE's screen, not just on the machine
/// whose player is standing in it.
///
/// The pod door is driven by local proximity — it watches "the player" via
/// FindObjectOfType and opens when they are deep inside. That is correct
/// single-player behaviour and invisible to everyone else, so without this the
/// host watches a joining player materialise through a shut door.
///
/// ── Why a named message rather than an RPC ───────────────────────────────
/// Same reason SolarSystemSync and the galactic clock use one: there is no
/// RPC layer in this project yet, and adding one means a NetworkBehaviour on a
/// spawned NetworkObject. A named message needs neither — any machine can send,
/// and the handler is registered wherever the NetworkManager is live.
///
/// A client's request is relayed through the host so that everyone hears it;
/// clients cannot broadcast to each other directly.
/// </summary>
public static class StasisDoorSync
{
    const string MsgOpen = "StasisDoorOpen";

    static bool _registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        if (!FeatureVault.Multiplayer) return;
        SceneManager.sceneLoaded += (_, __) => _registered = false;
    }

    /// Registers lazily — the NetworkManager is a scene object and may not exist
    /// yet, or may have been replaced by a scene reload.
    static bool EnsureRegistered()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { _registered = false; return false; }
        if (_registered) return true;
        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgOpen, OnOpenMessage);
        _registered = true;
        return true;
    }

    /// Poll point — called from the arrival, and cheap enough to call often.
    public static void Tick() => EnsureRegistered();

    /// Open this pod's door for every player in the session.
    public static void BroadcastOpen()
    {
        OpenLocally();   // always do it here, session or not

        if (!EnsureRegistered()) return;
        var nm = NetworkManager.Singleton;

        var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        try
        {
            writer.WriteValueSafe((byte)1);
            if (nm.IsServer)
                nm.CustomMessagingManager.SendNamedMessageToAll(
                    MsgOpen, writer, NetworkDelivery.ReliableSequenced);
            else
                // Clients can only talk to the host; it relays.
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgOpen, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
        finally { writer.Dispose(); }
    }

    static void OnOpenMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte _);
        OpenLocally();

        // Host relays a client's request on to everyone else, so all three
        // machines in a three-player session agree.
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        try
        {
            writer.WriteValueSafe((byte)1);
            foreach (var id in nm.ConnectedClientsIds)
            {
                if (id == senderId || id == NetworkManager.ServerClientId) continue;
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgOpen, id, writer, NetworkDelivery.ReliableSequenced);
            }
        }
        finally { writer.Dispose(); }
    }

    static void OpenLocally()
    {
        var door = Object.FindObjectOfType<StasisPodDoor>();
        if (door != null) door.OpenHold();
    }
}
