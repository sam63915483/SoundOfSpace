using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Saving the world when there are two people in it.
///
/// ── The problem ──────────────────────────────────────────────────────────
/// A world save now contains BOTH players' belongings, one PlayerBlockSave per
/// character. But neither machine holds both halves:
///
///   • The HOST owns the world. Its enemies are real bodies rather than pose
///     puppets, it runs every timer, and its ledgers are the authoritative
///     ones. Only its capture is worth writing.
///   • Each PLAYER owns their own pockets. The host has no idea what is in the
///     guest's hotbar; that state never crosses the wire during normal play,
///     and deliberately so.
///
/// So a save is a handshake, and it goes one of two ways.
///
/// ── Host presses save ────────────────────────────────────────────────────
/// Asks every guest for their block, waits briefly, captures the world, files
/// the blocks it got back, writes. A guest that doesn't answer in time keeps
/// whatever block the world already had for them — stale, but never lost.
///
/// ── Guest presses save ───────────────────────────────────────────────────
/// Sends its own block to the host and asks it to capture. The host does the
/// capture (with the guest's block already filed) and ships the finished
/// SaveData back; the guest writes that to ITS OWN disk under ITS OWN slot
/// name. Both players end up able to keep a copy of the same world, which is
/// what Sam asked for — either of you can be the one who uploads.
///
/// ── Why not just let the guest capture locally ────────────────────────────
/// Because it would be a lie. A guest's SaveCollector.Capture sees pose-puppet
/// enemies with no NetIds, a spawner with no timer state, and whatever its own
/// copy of the world happens to have drifted to. That file would load as a
/// subtly broken world. The host's capture is the only real one.
///
/// Named messages, not RPCs — same reason as every other sync here.
/// </summary>
public class PersonalSync : MonoBehaviour
{
    public static PersonalSync Instance { get; private set; }

    const string Msg = "PersonalSync";

    const byte KindRequestBlocks = 0;   // host -> client   "send me your pockets"
    const byte KindBlock         = 1;   // client -> host   a PlayerBlockSave
    const byte KindRequestSave   = 2;   // client -> host   my block + "capture for me"
    const byte KindSaveChunk     = 3;   // host -> client   the finished SaveData

    const int ChunkBytes = 8 * 1024;

    /// How long a save waits on the other machine before giving up on it. Long
    /// enough for a bad relay round trip, short enough that the pod ritual
    /// doesn't visibly stall — the fill animation is still playing underneath.
    const float BlockTimeout = 3f;

    /// A whole world round trip is a bigger ask than one block, and the guest
    /// has nothing to write until it lands, so it waits longer before failing.
    const float WorldTimeout = 12f;

    bool _registered;

    // host: blocks arriving from guests
    readonly Dictionary<ulong, PlayerBlockSave> _blocks = new Dictionary<ulong, PlayerBlockSave>();

    // client: the world coming back
    System.Text.StringBuilder _incoming;
    int _expectedChunks, _receivedChunks;
    SaveData _receivedWorld;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Does not skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1).
        var go = new GameObject("PersonalSync");
        DontDestroyOnLoad(go);
        go.AddComponent<PersonalSync>();
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
        _blocks.Clear();
        _incoming = null;
        _receivedWorld = null;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { _registered = false; return; }

        // On EVERY machine, not just senders — a host that never sent would
        // never register and would silently drop everything guests told it.
        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }
    }

    /// True when there is anyone else to talk to. Single player and a host
    /// sitting alone in a lobby both answer false, and the save stays the
    /// straightforward local one.
    static bool InSession
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return Instance != null && nm != null && nm.IsListening
                && (!nm.IsServer || nm.ConnectedClientsIds.Count > 1);
        }
    }

    // ── the save itself ──────────────────────────────────────────────────

    /// <summary>
    /// Write the world to `slotName` from this machine, whichever machine it is.
    /// Yields until the file is on disk (or the attempt has given up).
    ///
    /// Single player falls straight through to the ordinary local save on the
    /// first frame, so the pod ritual is unchanged when nobody else is here.
    /// </summary>
    public static IEnumerator SaveWorld(string slotName)
    {
        if (!InSession) { SaveSystem.Save(slotName); yield break; }

        var nm = NetworkManager.Singleton;
        if (nm.IsServer) yield return Instance.HostSave(slotName);
        else             yield return Instance.GuestSave(slotName);
    }

    IEnumerator HostSave(string slotName)
    {
        var nm = NetworkManager.Singleton;
        _blocks.Clear();

        int expected = 0;
        var ids = nm.ConnectedClientsIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == nm.LocalClientId) continue;
            expected++;
            SendTo(ids[i], w => w.WriteValueSafe(KindRequestBlocks), 8);
        }

        float deadline = Time.realtimeSinceStartup + BlockTimeout;
        while (_blocks.Count < expected && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (_blocks.Count < expected)
            Debug.LogWarning($"[PersonalSync] {expected - _blocks.Count} player(s) didn't send their " +
                             "belongings in time — saving with whatever this world already had for them.");

        WriteWithBlocks(slotName);
    }

    /// Capture and file, on the host. Split out because the guest path needs
    /// exactly the same thing done on its behalf.
    SaveData BuildWorld(string slotName)
    {
        var data = SaveCollector.Capture(slotName);
        foreach (var kv in _blocks) SaveCollector.UpsertPersonalBlock(data, kv.Value);
        return data;
    }

    void WriteWithBlocks(string slotName)
    {
        SaveSystem.Write(BuildWorld(slotName), slotName);
    }

    IEnumerator GuestSave(string slotName)
    {
        _receivedWorld = null;
        _incoming = null;

        string json = JsonUtility.ToJson(SaveCollector.CapturePersonalBlock());
        Send(w =>
        {
            w.WriteValueSafe(KindRequestSave);
            w.WriteValueSafe(slotName ?? "");
            w.WriteValueSafe(json);
        }, json.Length * 4 + 256);

        float deadline = Time.realtimeSinceStartup + WorldTimeout;
        while (_receivedWorld == null && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (_receivedWorld == null)
        {
            // Deliberately NOT falling back to a local capture: this machine's
            // world is a rendering of the host's, not a copy of it, and writing
            // it would produce a file that loads as a subtly broken world. Say
            // so and leave the last good save alone.
            Debug.LogError("[PersonalSync] The host never sent the world back — nothing was saved.");
            StoryImpactNotice.Show("UPLOAD FAILED — THE HOST DIDN'T ANSWER.", 4f);
            yield break;
        }

        SaveSystem.Write(_receivedWorld, slotName);
        _receivedWorld = null;
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;
        bool server = nm != null && nm.IsServer;

        switch (kind)
        {
            // ⚠️ !server on every host→client handler: the authority is never
            // told its own state.
            case KindRequestBlocks when !server: SendMyBlock(); break;
            case KindSaveChunk     when !server: ReceiveWorldChunk(reader); break;

            case KindBlock       when server: HandleBlock(reader, senderId); break;
            case KindRequestSave when server: HandleRequestSave(reader, senderId); break;
        }
    }

    void SendMyBlock()
    {
        string json = JsonUtility.ToJson(SaveCollector.CapturePersonalBlock());
        Send(w => { w.WriteValueSafe(KindBlock); w.WriteValueSafe(json); }, json.Length * 4 + 128);
    }

    void HandleBlock(FastBufferReader reader, ulong senderId)
    {
        reader.ReadValueSafe(out string json);
        var block = ParseBlock(json);
        if (block != null) _blocks[senderId] = block;
    }

    /// <summary>
    /// A guest wants to save. Its block arrives with the request — one round
    /// trip instead of two — so the world can be captured and filed in one go
    /// and shipped straight back.
    /// </summary>
    void HandleRequestSave(FastBufferReader reader, ulong senderId)
    {
        reader.ReadValueSafe(out string slotName);
        reader.ReadValueSafe(out string blockJson);

        var block = ParseBlock(blockJson);
        if (block != null) _blocks[senderId] = block;

        // The slot name is the GUEST's, and the file lands on the GUEST's disk.
        // The host's own save keeps its own name — two machines, two files, one
        // world.
        var data = BuildWorld(string.IsNullOrEmpty(slotName) ? "coop" : slotName);
        SendWorldTo(senderId, JsonUtility.ToJson(data));
    }

    static PlayerBlockSave ParseBlock(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var b = JsonUtility.FromJson<PlayerBlockSave>(json);
            return b != null && !string.IsNullOrEmpty(b.characterId) ? b : null;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[PersonalSync] A player's belongings didn't parse: " + e.Message);
            return null;
        }
    }

    void SendWorldTo(ulong clientId, string json)
    {
        int total = Mathf.Max(1, Mathf.CeilToInt(json.Length / (float)ChunkBytes));
        for (int i = 0; i < total; i++)
        {
            int start = i * ChunkBytes;
            int len = Mathf.Min(ChunkBytes, json.Length - start);
            string piece = json.Substring(start, len);
            SendTo(clientId, w =>
            {
                w.WriteValueSafe(KindSaveChunk);
                w.WriteValueSafe(i);
                w.WriteValueSafe(total);
                w.WriteValueSafe(piece);
            }, len * 4 + 64);
        }
    }

    void ReceiveWorldChunk(FastBufferReader reader)
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

        try { _receivedWorld = JsonUtility.FromJson<SaveData>(json); }
        catch (System.Exception e)
        {
            Debug.LogError("[PersonalSync] The world didn't parse: " + e.Message);
            _receivedWorld = null;
        }
    }

    // ── transport ────────────────────────────────────────────────────────

    /// Client → host. ⚠️ Never SendNamedMessageToAll: NGO loops a broadcast back
    /// to the host, which is the rebroadcast storm every sync here avoids.
    void Send(System.Action<FastBufferWriter> write, int sizeHint = 512)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || !_registered) return;

        var w = new FastBufferWriter(sizeHint, Allocator.Temp, 1024 * 1024 * 8);
        try
        {
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }

    /// Host → one client, addressed explicitly.
    void SendTo(ulong clientId, System.Action<FastBufferWriter> write, int sizeHint = 512)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !_registered) return;
        if (clientId == nm.LocalClientId) return;

        var w = new FastBufferWriter(sizeHint, Allocator.Temp, 1024 * 1024 * 8);
        try
        {
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }
}
