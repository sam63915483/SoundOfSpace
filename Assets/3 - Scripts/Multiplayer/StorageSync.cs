using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared containers in co-op: the shuttle locker and every other LootBox.
///
/// ── Why a LOCK and not per-slot arbitration ──────────────────────────────
/// This is the first system where two players can genuinely collide: both open
/// the locker, both grab the same axe, both walk away with one. Item
/// duplication, from a single race.
///
/// The thorough fix is to arbitrate every slot operation on the host — but a
/// drag-and-drop inventory has a lot of operations (pick up, place, split,
/// swap, quick-move), and each one becomes a round trip that can fail halfway
/// through a drag. That is a large amount of machinery and a lot of new ways to
/// desync.
///
/// So instead: ONE PLAYER MAY HAVE A BOX OPEN AT A TIME. The host grants the
/// lock, the holder rearranges freely with the existing local UI, and on close
/// the box's contents are published to everyone. Duplication stops being
/// something to detect and becomes something that cannot be expressed — and it
/// is the convention players already know from every other co-op game's chest.
///
/// ── Contents travel as a StorageSave ─────────────────────────────────────
/// Same trick as the rest of the sync layer: the save schema is the network
/// schema. A box's contents already serialise for disk, so they are reused
/// verbatim rather than inventing a second format that could drift.
///
/// ⚠️ Never SendNamedMessageToAll — NGO loops a broadcast back to the host,
/// which is what caused the Phase 2 rebroadcast storm. Everything here
/// addresses clients explicitly through WorldSync.DispatchPublic.
/// </summary>
public class StorageSync : MonoBehaviour
{
    public static StorageSync Instance { get; private set; }

    const string Msg = "StorageSync";

    const byte KindOpenRequest = 0;   // client -> host  "may I open <boxId>?"
    const byte KindOpenReply   = 1;   // host -> client  granted / denied
    const byte KindClosed      = 2;   // client -> host  "done, here are the contents"
    const byte KindState       = 3;   // host -> all     authoritative box contents

    bool _registered;

    /// Host only: which client holds which box. A box absent from here is free.
    readonly Dictionary<string, ulong> _lockedBy = new Dictionary<string, ulong>();

    /// Client only: the box we asked about, and whether the answer arrived.
    string _pendingBoxId;
    LootBox _pendingBox;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        var go = new GameObject("StorageSync");
        DontDestroyOnLoad(go);
        go.AddComponent<StorageSync>();
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
        _lockedBy.Clear();
        _pendingBoxId = null;
        _pendingBox = null;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { _registered = false; return; }

        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            nm.OnClientDisconnectCallback += OnClientLeft;
            _registered = true;
        }
    }

    /// A player who disconnects mid-rummage must not hold the box forever.
    void OnClientLeft(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        var stale = new List<string>();
        foreach (var kv in _lockedBy) if (kv.Value == clientId) stale.Add(kv.Key);
        foreach (var id in stale) _lockedBy.Remove(id);
    }

    // ── open: ask before you rummage ─────────────────────────────────────

    /// <summary>
    /// True when this machine may open `box` right now.
    ///
    /// Single player and the host answer immediately — the host holds the lock
    /// table, so it never has to ask anyone. A client sends a request and
    /// returns false; StorageUI opens when the grant arrives, which is one
    /// frame or so later and reads as instant.
    /// </summary>
    public static bool TryOpen(LootBox box)
    {
        if (box == null) return false;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return true;          // single player
        if (Instance == null) return true;

        if (nm.IsServer) return Instance.HostTryClaim(box.BoxId, nm.LocalClientId);

        Instance.RequestOpen(box);
        return false;   // opens on the reply
    }

    /// Call when a box is closed, so the lock is released and everyone is told
    /// what is now inside.
    public static void NotifyClosed(LootBox box)
    {
        if (box == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || Instance == null) return;

        if (nm.IsServer)
        {
            Instance._lockedBy.Remove(box.BoxId);
            Instance.BroadcastState(box, skipClient: ulong.MaxValue);
        }
        else
        {
            Instance.SendClosed(box);
        }
    }

    bool HostTryClaim(string boxId, ulong clientId)
    {
        if (_lockedBy.TryGetValue(boxId, out ulong holder) && holder != clientId) return false;
        _lockedBy[boxId] = clientId;
        return true;
    }

    void RequestOpen(LootBox box)
    {
        _pendingBoxId = box.BoxId;
        _pendingBox = box;
        Send(w => { w.WriteValueSafe(KindOpenRequest); w.WriteValueSafe(box.BoxId); },
             NetworkManager.ServerClientId);
    }

    // ── transport ────────────────────────────────────────────────────────

    void Send(System.Action<FastBufferWriter> write, ulong toClient)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !_registered) return;

        var w = new FastBufferWriter(1024, Allocator.Temp, 1024 * 256);
        try
        {
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(Msg, toClient, w,
                NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }

    void SendClosed(LootBox box)
    {
        string json = JsonUtility.ToJson(Capture(box));
        Send(w => { w.WriteValueSafe(KindClosed); w.WriteValueSafe(box.BoxId); w.WriteValueSafe(json); },
             NetworkManager.ServerClientId);
    }

    /// Host: publish a box's contents to every client except `skipClient`.
    void BroadcastState(LootBox box, ulong skipClient)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        string json = JsonUtility.ToJson(Capture(box));
        var ids = nm.ConnectedClientsIds;
        for (int i = 0; i < ids.Count; i++)
        {
            ulong id = ids[i];
            if (id == nm.LocalClientId || id == skipClient) continue;
            Send(w => { w.WriteValueSafe(KindState); w.WriteValueSafe(box.BoxId); w.WriteValueSafe(json); }, id);
        }
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;
        bool server = nm != null && nm.IsServer;

        switch (kind)
        {
            case KindOpenRequest when server:
            {
                reader.ReadValueSafe(out string boxId);
                bool granted = HostTryClaim(boxId, senderId);
                // Send the current contents WITH the grant, so the client opens
                // onto the truth rather than a stale local copy.
                string json = granted ? JsonUtility.ToJson(Capture(FindBox(boxId))) : "";
                Send(w =>
                {
                    w.WriteValueSafe(KindOpenReply);
                    w.WriteValueSafe(boxId);
                    w.WriteValueSafe((byte)(granted ? 1 : 0));
                    w.WriteValueSafe(json);
                }, senderId);
                break;
            }

            case KindOpenReply when !server:
            {
                reader.ReadValueSafe(out string boxId);
                reader.ReadValueSafe(out byte granted);
                reader.ReadValueSafe(out string json);
                if (boxId != _pendingBoxId) break;

                var box = _pendingBox != null ? _pendingBox : FindBox(boxId);
                _pendingBoxId = null; _pendingBox = null;
                if (box == null) break;

                if (granted == 0)
                {
                    InteractPromptUI.Show(box, "Someone else is in there");
                    break;
                }
                ApplyState(box, json);
                StorageUI.Instance?.OpenGranted(box);
                break;
            }

            case KindClosed when server:
            {
                reader.ReadValueSafe(out string boxId);
                reader.ReadValueSafe(out string json);
                var box = FindBox(boxId);
                if (box != null) ApplyState(box, json);      // trust the holder
                _lockedBy.Remove(boxId);
                if (box != null) BroadcastState(box, skipClient: senderId);
                break;
            }

            case KindState when !server:
            {
                reader.ReadValueSafe(out string boxId);
                reader.ReadValueSafe(out string json);
                var box = FindBox(boxId);
                if (box != null) ApplyState(box, json);
                break;
            }
        }
    }

    // ── contents <-> StorageSave ─────────────────────────────────────────

    static LootBox FindBox(string boxId)
    {
        var all = StorageRegistry.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].BoxId == boxId) return all[i];
        return null;
    }

    static StorageSave Capture(LootBox box)
    {
        var save = new StorageSave { boxId = box != null ? box.BoxId : "" };
        if (box == null) return save;
        var slots = box.Slots;
        for (int i = 0; i < slots.Length; i++)
            save.slots.Add(SaveCollector.SerializeSlotPublic(slots[i]));
        return save;
    }

    static void ApplyState(LootBox box, string json)
    {
        if (box == null || string.IsNullOrEmpty(json)) return;
        StorageSave save;
        try { save = JsonUtility.FromJson<StorageSave>(json); }
        catch (System.Exception e)
        {
            Debug.LogError("[StorageSync] Bad box state: " + e.Message);
            return;
        }
        if (save == null) return;
        SaveCollector.ApplyStorageSlotsPublic(box, save);

        // If it is on screen right now, redraw it.
        if (StorageUI.Instance != null && StorageUI.Instance.IsOpen)
            StorageUI.Instance.RefreshFromNetwork(box);
    }

    // ── starter kit: one axe and one bottle PER PLAYER ───────────────────

    /// <summary>
    /// Host only. Tops the shuttle locker up so a newly-joined player has their
    /// own axe and water bottle to take.
    ///
    /// LootBoxStarterItem seeds exactly ONE of each on a new game, and guards on
    /// StorageRegistry.IsItemAnywhere — correct for single player, but it means
    /// a second player finds an empty locker if the host already took the tools.
    ///
    /// Stocked once per client id, so a rejoin does not keep piling up spares,
    /// and never for the host (their own starter seed covers them).
    /// </summary>
    public static void StockForPlayer(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (Instance == null) return;
        if (!Instance._stockedFor.Add(clientId)) return;

        var box = FindStarterLocker();
        if (box == null) return;

        AddOne(box, Hotbar.ItemId.Axe);
        AddOne(box, Hotbar.ItemId.WaterBottle);
        Instance.BroadcastState(box, skipClient: ulong.MaxValue);
    }

    readonly HashSet<ulong> _stockedFor = new HashSet<ulong>();

    /// The box a LootBoxStarterItem is attached to — that is, by definition, the
    /// one the game already considers the starting locker.
    static LootBox FindStarterLocker()
    {
        var starters = Object.FindObjectsOfType<LootBoxStarterItem>(true);
        for (int i = 0; i < starters.Length; i++)
        {
            var b = starters[i] != null ? starters[i].GetComponent<LootBox>() : null;
            if (b != null) return b;
        }
        var all = StorageRegistry.All;
        return all.Count > 0 ? all[0] : null;
    }

    static void AddOne(LootBox box, Hotbar.ItemId id)
    {
        var slots = box.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].id != Hotbar.ItemId.None) continue;
            slots[i].id = id;
            slots[i].count = 1;
            return;
        }
        Debug.LogWarning($"[StorageSync] Locker full — couldn't stock {id} for the new player.");
    }
}
