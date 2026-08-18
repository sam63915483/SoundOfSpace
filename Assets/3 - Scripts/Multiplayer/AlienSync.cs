using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The walking customers, so both players can watch one come for them.
///
/// ── Why this is so much smaller than EnemySync ───────────────────────────
/// Enemies are spawned by a host-only fill loop, so a guest has no bodies at
/// all and EnemySync has to stream spawns, deaths, NetIds and puppets. Aliens
/// are not like that: AlienNPCSpawner is a deterministic hash of (seed, body,
/// cell), exactly like the tree and mushroom spawners, so BOTH machines already
/// spawn the same alien in the same cell with the same prefab. Nothing needs
/// creating, nothing needs an id assigned, and nothing needs a puppet.
///
/// The only thing that differs is where each one has WALKED TO. So that is all
/// that travels.
///
/// ── The key is the alien's identity, not a minted id ─────────────────────
/// (bodySlot, cellId) is what AlienIdentity builds "cell:{slot}:{cell}" from,
/// and that string is the key every bond, deal and craving in BuyerLedger hangs
/// off. Using it here means a pose and a want-text are talking about the same
/// alien by construction. A minted network id would be a second identity that
/// could drift, and every bond in the world would orphan the day it did.
///
/// ── Poses are planet-local because the aliens already are ────────────────
/// AlienWander parents its aliens to the CelestialBody and moves them in
/// localPosition, so the value on the wire is the value it already works in —
/// no conversion, and no world-space coordinate crossing a floating-origin
/// boundary (the rule that has bitten every other sync here).
///
/// Each machine streams around its OWN player, so an alien only one of them can
/// see is simply never sent, and AlienWander's remote drive expires back into
/// local strolling when that happens. The two halves cover each other.
/// </summary>
public class AlienSync : MonoBehaviour
{
    public static AlienSync Instance { get; private set; }

    const string Msg = "AlienSync";

    const byte KindPoses = 0;   // host -> clients   a batch of walking aliens

    /// Poses per message. Matches EnemySync's batch size — the aliens near any
    /// one player are capped at ten by the spawner, so this is nearly always
    /// one message.
    const int PosesPerMessage = 24;

    /// 10 Hz, same as the enemy pose stream. Aliens stroll; they do not need
    /// frame-accurate replication, and the receiver holds the last pose rather
    /// than interpolating.
    const float SendInterval = 0.1f;

    float _nextSendAt;
    bool _registered;

    // Where each alien was the last time we sent it, so a field of idle
    // strollers costs nothing. Keyed by the same (slot, cell) pair the wire
    // uses.
    readonly Dictionary<long, Vector3> _lastSent = new Dictionary<long, Vector3>();

    /// Below this, an alien has not really moved and its pose is not worth a
    /// packet. Comfortably under a single walking step.
    const float ResendDistanceSqr = 0.01f;   // 10 cm

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Does not skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1).
        var go = new GameObject("AlienSync");
        DontDestroyOnLoad(go);
        go.AddComponent<AlienSync>();
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
        _lastSent.Clear();
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { _registered = false; return; }

        // On EVERY machine, not just senders.
        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        if (!nm.IsServer) return;
        if (Time.unscaledTime < _nextSendAt) return;
        _nextSendAt = Time.unscaledTime + SendInterval;
        SendPoses(nm);
    }

    // ── host ─────────────────────────────────────────────────────────────

    void SendPoses(NetworkManager nm)
    {
        var ids = nm.ConnectedClientsIds;
        bool anyone = false;
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] != nm.LocalClientId) { anyone = true; break; }
        if (!anyone) return;

        var all = SpawnedAlienNPC.AllAliens;
        if (all.Count == 0) return;

        var batch = new List<AlienNPCPose>(PosesPerMessage);
        for (int i = 0; i < all.Count; i++)
        {
            var a = all[i];
            if (a == null) continue;
            var wander = a.GetComponent<AlienWander>();
            if (wander == null) continue;

            long key = KeyOf(a.BodySlot, a.CellId);
            Vector3 local = a.transform.localPosition;

            // An alien standing still costs nothing. The receiver holds its
            // last pose, so silence and "unchanged" mean the same thing —
            // except to the remote-drive timer, which is deliberately allowed
            // to expire so a guest the host has walked away from can take the
            // alien back over.
            bool moved = !_lastSent.TryGetValue(key, out Vector3 prev)
                      || (prev - local).sqrMagnitude > ResendDistanceSqr;
            if (!moved) continue;

            _lastSent[key] = local;
            batch.Add(new AlienNPCPose
            {
                key = key,
                localPos = local,
                localRot = a.transform.localRotation,
            });

            if (batch.Count < PosesPerMessage) continue;
            Flush(nm, ids, batch);
        }
        if (batch.Count > 0) Flush(nm, ids, batch);
    }

    struct AlienNPCPose
    {
        public long key;
        public Vector3 localPos;
        public Quaternion localRot;
    }

    void Flush(NetworkManager nm, IReadOnlyList<ulong> ids, List<AlienNPCPose> batch)
    {
        var w = new FastBufferWriter(batch.Count * 40 + 16, Allocator.Temp, 1024 * 64);
        try
        {
            w.WriteValueSafe(KindPoses);
            w.WriteValueSafe(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                var p = batch[i];
                w.WriteValueSafe(p.key);
                // Loose floats rather than the Vector3/Quaternion overloads, so
                // nothing depends on which NGO version supplies which
                // specialisation (the EnemySync convention).
                w.WriteValueSafe(p.localPos.x); w.WriteValueSafe(p.localPos.y); w.WriteValueSafe(p.localPos.z);
                w.WriteValueSafe(p.localRot.x); w.WriteValueSafe(p.localRot.y);
                w.WriteValueSafe(p.localRot.z); w.WriteValueSafe(p.localRot.w);
            }

            // ⚠️ Addressed per client, never SendNamedMessageToAll: NGO delivers
            // a broadcast back to the host itself, which is the rebroadcast
            // storm every sync here is built to avoid.
            //
            // UnreliableSequenced: these are absolute positions on a 10 Hz
            // stream, so a dropped one self-corrects a tenth of a second later
            // and is not worth a retransmit.
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == nm.LocalClientId) continue;
                nm.CustomMessagingManager.SendNamedMessage(
                    Msg, ids[i], w, NetworkDelivery.UnreliableSequenced);
            }
        }
        finally { w.Dispose(); batch.Clear(); }
    }

    // ── client ───────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;
        // ⚠️ !IsServer: the authority is never told where its own aliens are.
        if (kind != KindPoses || nm == null || nm.IsServer) return;

        reader.ReadValueSafe(out int count);
        if (count <= 0 || count > 4096) return;

        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out long key);
            reader.ReadValueSafe(out float px); reader.ReadValueSafe(out float py); reader.ReadValueSafe(out float pz);
            reader.ReadValueSafe(out float rx); reader.ReadValueSafe(out float ry);
            reader.ReadValueSafe(out float rz); reader.ReadValueSafe(out float rw);

            var wander = FindWander(key);
            // Not streamed in on this machine — the host is near an alien we
            // are nowhere near. Nothing to do: when we walk over there our own
            // spawner will produce the same alien from the same seed, and the
            // next pose will land on it.
            if (wander == null) continue;

            wander.RemotePose(new Vector3(px, py, pz), new Quaternion(rx, ry, rz, rw), moved: true);
        }
    }

    /// Linear over the live alien list. It is capped at ten per machine by the
    /// spawner and this runs ten times a second, so a dictionary would cost
    /// more to maintain than it saves — and it could go stale, which this
    /// cannot.
    static AlienWander FindWander(long key)
    {
        var all = SpawnedAlienNPC.AllAliens;
        for (int i = 0; i < all.Count; i++)
        {
            var a = all[i];
            if (a == null || KeyOf(a.BodySlot, a.CellId) != key) continue;
            return a.GetComponent<AlienWander>();
        }
        return null;
    }

    /// <summary>
    /// (bodySlot, cellId) packed into one long — the numeric half of the string
    /// AlienIdentity builds, so it addresses exactly the same alien for a
    /// fraction of the bytes.
    ///
    /// The slot is a position in the spawner's body list, which both machines
    /// build from the same NBodySimulation in the same order; the cell id is a
    /// pure function of the world seed.
    /// </summary>
    static long KeyOf(int bodySlot, long cellId)
    {
        return ((long)bodySlot << 48) ^ (cellId & 0xFFFFFFFFFFFF);
    }
}
