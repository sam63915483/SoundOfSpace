using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One shared set of aliens, in the same places on both machines.
///
/// ── Who thinks ───────────────────────────────────────────────────────────
/// THE HOST, and only the host. Guests render pose-synced puppets and decide
/// nothing. This is not a preference: `EnemySpawner` rolls dice for placement
/// and the AI reads its own local player, so two machines running the same code
/// do not double-tick the same enemy — they invent DIFFERENT ones and hunt
/// different people. `WorldSync.IsAuthority` is how every other system asks the
/// same question.
///
/// ── Why this makes guests FASTER ─────────────────────────────────────────
/// Before this file, every machine ran full enemy AI: bicone vision tests, LOS
/// raycasts, spot timers, search-and-sniff, pathing, for every enemy. Now only
/// the host does, and a guest just moves puppets to received positions. A
/// guest's per-frame enemy cost goes DOWN and the LOS raycasts — the expensive
/// half of the stealth revamp — disappear from that machine entirely. The host's
/// cost is unchanged; it was already running all of it.
///
/// The new cost is network, and it is small: ~20 enemies × (planet-local pose +
/// two state bytes) at 10 Hz is a few KB/s, less than the player pose sync
/// already sends. If it ever does matter, the lever is the tick rate and a
/// distance cull, not the architecture.
///
/// ── The three rules this file inherits ───────────────────────────────────
/// 1. PLANET-LOCAL COORDINATES ONLY. A world position is stale on arrival:
///    floating-origin rebases fire while standing still, and the two machines
///    rebase independently. Every pose here is expressed against the enemy's
///    CelestialBody, which both machines have.
/// 2. NEVER `SendNamedMessageToAll`. NGO delivers a broadcast back to the host
///    (CustomMessageManager.cs:342); the host's relay step then re-sends what it
///    just received, and that is the Phase 2 REBROADCAST STORM that starved real
///    delivery. Clients are addressed explicitly, exactly as WorldSync.Dispatch
///    does.
/// 3. The authority is never told its own state. Every host→client handler is
///    guarded on `!IsServer`.
///
/// ── Identity ─────────────────────────────────────────────────────────────
/// Enemies are runtime-spawned, so unlike a tree or a mushroom there is no cell
/// id to key on. The host stamps an incrementing `NetId` on any enemy it finds
/// without one and announces it. Doing it as a SWEEP rather than a hook at each
/// `Instantiate` is deliberate — it catches every creation path there is (the
/// population fill loop, a save load, anything added later) with one mechanism,
/// and a path that forgets to call a hook is exactly how an enemy ends up
/// invisible on the other screen with nothing in the log.
/// </summary>
public class EnemySync : MonoBehaviour
{
    public static EnemySync Instance { get; private set; }

    const string Msg = "EnemySync";

    const byte KindRequestAll   = 0;   // client -> host   "give me the field"
    const byte KindSpawn        = 1;   // host -> client
    const byte KindSyncEnd      = 2;   // host -> client   "that was all of them"
    const byte KindPose         = 3;   // host -> clients  batched, 10 Hz
    const byte KindDeath        = 4;   // host -> clients
    const byte KindDespawn      = 5;   // host -> clients
    const byte KindHit          = 6;   // client -> host
    const byte KindPlayerDamage = 7;   // host -> ONE client
    const byte KindGunshot      = 8;   // client -> host   "I fired; wake them up"
    const byte KindPerception   = 9;   // host -> ONE client: that client's own spot-meters

    /// 10 Hz. Fast enough that a guest sees an enemy within a few centimetres of
    /// where the host has it — which is what stops "hit by something I can't
    /// see" — and slow enough that the whole field costs less than the player
    /// sync. Do NOT send every frame.
    const float PoseInterval = 0.1f;

    /// Enemies per pose message. Poses go in ONE batched message per tick rather
    /// than one message per enemy — twenty separate named messages every tick is
    /// where the bandwidth would actually go. The cap exists because the batch is
    /// sent UNRELIABLY and an unreliable packet must fit inside the MTU:
    /// 24 × 36 bytes ≈ 864, comfortably under ~1400. A field at maxPopulation
    /// (33) simply goes out as two messages.
    const int PosesPerMessage = 24;

    /// Sanity ceiling on a damage figure arriving from another machine. The
    /// shooter is trusted for WHETHER a hit landed — that is the same call PvP
    /// makes, and there is no competitive stake here — but a single value that
    /// can delete anything on the planet is worth bounding anyway.
    const float MaxReportedDamage = 500f;


    bool _registered;

    // ── host state ───────────────────────────────────────────────────────
    readonly Dictionary<uint, EnemyController> _known = new Dictionary<uint, EnemyController>();
    readonly HashSet<uint> _reportedDead = new HashSet<uint>();
    readonly List<uint> _sweep = new List<uint>();
    readonly List<EnemyController> _poseBatch = new List<EnemyController>();
    readonly List<EnemyController> _perceptionBatch = new List<EnemyController>();
    readonly HashSet<uint> _perceptionSeen = new HashSet<uint>();
    uint _nextNetId = 1;
    float _nextPoseAt;

    // ── client state ─────────────────────────────────────────────────────
    readonly Dictionary<uint, EnemyController> _puppets = new Dictionary<uint, EnemyController>();
    bool _synced;
    bool _wipedLocalField;
    float _nextRequestAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Deliberately does NOT skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons — the same dodge WorldSync and StorageSync use
        // for CLAUDE.md trap #1.
        var go = new GameObject("EnemySync");
        DontDestroyOnLoad(go);
        go.AddComponent<EnemySync>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Static and surviving scene loads, so it MUST be unsubscribed.
        PistolController.OnLocalShotFired += OnLocalShot;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        PistolController.OnLocalShotFired -= OnLocalShot;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _registered = false;
        _known.Clear();
        _reportedDead.Clear();
        _puppets.Clear();
        _synced = false;
        _wipedLocalField = false;
        _nextRequestAt = 0f;
        _nextPoseAt = 0f;
    }

    /// This machine's client id, or 0 in single player. EnemyController stamps it
    /// on a kill so BeginDeath knows whether the credit is ours.
    public static ulong LocalClientId
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening ? nm.LocalClientId : 0UL;
        }
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            if (_registered || _puppets.Count > 0 || _known.Count > 0) LeaveSession();
            return;
        }

        // Registration must happen on EVERY machine, not just senders. A host
        // that never sent used to never register and silently dropped everything
        // a client told it — the bug StasisDoorSync documents.
        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        if (nm.IsServer) HostTick(nm);
        else             ClientTick();
    }

    /// <summary>
    /// Back to single player. Puppets are torn down (nobody is driving them any
    /// more, and a frozen alien standing in a field forever is worse than none)
    /// and every surviving enemy has its identity cleared, so a second session
    /// in the same process re-announces the field instead of assuming the other
    /// machine already knows about ids it has never heard of.
    /// </summary>
    void LeaveSession()
    {
        foreach (var kv in _puppets)
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        _puppets.Clear();

        var live = EnemyController.ActiveEnemies;
        for (int i = 0; i < live.Count; i++)
            if (live[i] != null) live[i].NetId = 0;

        _known.Clear();
        _reportedDead.Clear();
        _registered = false;
        _synced = false;
        _wipedLocalField = false;
        _nextRequestAt = 0f;
    }

    // ── host ─────────────────────────────────────────────────────────────

    void HostTick(NetworkManager nm)
    {
        AnnounceNewEnemies();

        // Deaths and disappearances go out the FRAME they happen, not on the
        // 10 Hz pose tick: a body that keeps walking for a tenth of a second
        // after it dropped is exactly the sort of thing that reads as a desync.
        _sweep.Clear();
        foreach (var kv in _known) _sweep.Add(kv.Key);
        for (int i = 0; i < _sweep.Count; i++)
        {
            uint id = _sweep[i];
            var e = _known[id];

            if (e == null)
            {
                // Destroyed on the host — despawned out of range, wiped by a
                // load, or a corpse that finished shrinking away.
                SendIdOnly(KindDespawn, id);
                _known.Remove(id);
                _reportedDead.Remove(id);
                continue;
            }

            if (e.IsDying && _reportedDead.Add(id)) SendDeath(e);
        }

        if (Time.unscaledTime < _nextPoseAt) return;
        _nextPoseAt = Time.unscaledTime + PoseInterval;
        SendPoses(nm);
        SendPerception(nm);
    }

    /// <summary>
    /// Each client's OWN spot-meters.
    ///
    /// Suspicion is per player now, so it cannot ride the pose batch — that is
    /// one broadcast message and every client would receive the same number.
    /// Sending it per client is what makes a guest's HUD show how close the
    /// aliens are to noticing THEM rather than how close they are to noticing
    /// the host.
    ///
    /// Only enemies that have actually started noticing that player are listed,
    /// which in practice is nought to a handful — far smaller than the pose
    /// stream it rides alongside.
    /// </summary>
    void SendPerception(NetworkManager nm)
    {
        var ids = nm.ConnectedClientsIds;
        for (int c = 0; c < ids.Count; c++)
        {
            ulong client = ids[c];
            if (client == nm.LocalClientId) continue;

            _perceptionBatch.Clear();
            foreach (var kv in _known)
            {
                var e = kv.Value;
                if (e == null || e.IsDying) continue;
                var v = e.Vision;
                if (v == null) continue;
                if (v.SuspicionFor(client) <= 0.004f) continue;   // below one byte of meter
                _perceptionBatch.Add(e);
            }
            if (_perceptionBatch.Count == 0) continue;

            for (int start = 0; start < _perceptionBatch.Count; start += PosesPerMessage)
            {
                int count = Mathf.Min(PosesPerMessage, _perceptionBatch.Count - start);
                int from = start;
                Write(w =>
                {
                    w.WriteValueSafe(KindPerception);
                    w.WriteValueSafe((ushort)count);
                    for (int i = 0; i < count; i++)
                    {
                        var e = _perceptionBatch[from + i];
                        w.WriteValueSafe(e.NetId);
                        w.WriteValueSafe((byte)Mathf.Clamp(
                            Mathf.RoundToInt(e.Vision.SuspicionFor(client) * 255f), 0, 255));
                    }
                }, client, NetworkDelivery.UnreliableSequenced, 8 + count * 6);
            }
        }
    }

    /// Hand an identity to anything that does not have one yet, and tell
    /// everybody about it. Idempotent on the receiving side, so re-announcing
    /// during a full resync is harmless.
    void AnnounceNewEnemies()
    {
        var live = EnemyController.ActiveEnemies;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || e.IsNetworkPuppet || e.NetId != 0) continue;
            e.NetId = _nextNetId++;
            _known[e.NetId] = e;
            SendSpawn(e, ulong.MaxValue);
        }
    }

    void SendSpawn(EnemyController e, ulong onlyClient)
    {
        var planet = e.ParentPlanet;
        if (planet == null) return;   // not seated yet; the next sweep catches it

        var pt = planet.transform;
        Vector3 localPos    = pt.InverseTransformPoint(e.transform.position);
        Quaternion localRot = Quaternion.Inverse(pt.rotation) * e.transform.rotation;
        string bodyName     = planet.bodyName;

        Write(w =>
        {
            w.WriteValueSafe(KindSpawn);
            w.WriteValueSafe(e.NetId);
            w.WriteValueSafe((byte)e.Kind);
            w.WriteValueSafe(bodyName);
            WriteVec3(w, localPos);
            WriteQuat(w, localRot);
        }, onlyClient, NetworkDelivery.ReliableFragmentedSequenced,
           bodyName.Length * 4 + 64);
    }

    void SendDeath(EnemyController e)
    {
        // The credit travels with the death so exactly ONE machine fires a
        // killstreak: the one whose player actually landed the shot. Without it
        // the host banks the GANGSTA REP for every alien either player kills.
        bool credited = e.KilledByPlayer;
        ulong killer  = e.KillerClientId;
        Write(w =>
        {
            w.WriteValueSafe(KindDeath);
            w.WriteValueSafe(e.NetId);
            w.WriteValueSafe((byte)(credited ? 1 : 0));
            w.WriteValueSafe(killer);
        }, ulong.MaxValue, NetworkDelivery.ReliableSequenced, 32);
    }

    void SendIdOnly(byte kind, uint netId)
    {
        Write(w => { w.WriteValueSafe(kind); w.WriteValueSafe(netId); },
              ulong.MaxValue, NetworkDelivery.ReliableSequenced, 16);
    }

    void SendPoses(NetworkManager nm)
    {
        if (!AnyClients(nm)) return;

        _poseBatch.Clear();
        foreach (var kv in _known)
        {
            var e = kv.Value;
            if (e == null || e.IsDying) continue;      // a corpse runs itself out locally
            if (e.ParentPlanet == null) continue;
            _poseBatch.Add(e);
        }
        if (_poseBatch.Count == 0) return;

        for (int start = 0; start < _poseBatch.Count; start += PosesPerMessage)
        {
            int count = Mathf.Min(PosesPerMessage, _poseBatch.Count - start);
            int from = start;
            Write(w =>
            {
                w.WriteValueSafe(KindPose);
                w.WriteValueSafe((ushort)count);
                for (int i = 0; i < count; i++)
                {
                    var e  = _poseBatch[from + i];
                    var pt = e.ParentPlanet.transform;
                    w.WriteValueSafe(e.NetId);
                    WriteVec3(w, pt.InverseTransformPoint(e.transform.position));
                    WriteQuat(w, Quaternion.Inverse(pt.rotation) * e.transform.rotation);
                    w.WriteValueSafe((byte)Mathf.Clamp(Mathf.RoundToInt(e.CurrentAnimSpeed * 255f), 0, 255));
                    w.WriteValueSafe((byte)e.State);
                }
            },
            // Unreliable: every pose is an ABSOLUTE position, so a dropped one
            // self-corrects 100 ms later. Making them reliable would put the
            // whole stream behind a retransmit whenever one packet is lost, and
            // a stall is far more visible than a skipped frame. Sequenced, so a
            // late packet is discarded rather than rewinding an enemy.
            ulong.MaxValue, NetworkDelivery.UnreliableSequenced, 8 + count * 40);
        }
    }

    static bool AnyClients(NetworkManager nm)
    {
        var ids = nm.ConnectedClientsIds;
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] != nm.LocalClientId) return true;
        return false;
    }

    /// A guest asked for the field. Announce anything still unnamed first, so a
    /// client that joins in the same frame an enemy spawned does not miss it.
    void SendFullListTo(ulong clientId)
    {
        AnnounceNewEnemies();

        foreach (var kv in _known)
        {
            var e = kv.Value;
            if (e == null || e.IsDying) continue;
            SendSpawn(e, clientId);
        }
        Write(w => w.WriteValueSafe(KindSyncEnd), clientId, NetworkDelivery.ReliableSequenced, 8);
    }

    // ── client ───────────────────────────────────────────────────────────

    void ClientTick()
    {
        if (_synced) return;
        if (Time.unscaledTime < _nextRequestAt) return;

        // Pull, not push — the same reasoning as WorldSync's snapshot. The host
        // cannot know when this machine's celestial bodies and spawner exist, and
        // a spawn message applied into a half-built scene is silently dropped.
        // Asking when WE are ready removes the race; the retry covers a request
        // lost before the host finished loading.
        if (!WorldSync.WorldReady) return;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0) return;
        if (EnemySpawner.Instance == null) return;

        if (!_wipedLocalField)
        {
            _wipedLocalField = true;
            // Anything this machine made BEFORE joining (a save loaded, then a
            // session joined) is not part of the host's world and nothing can
            // ever move or kill it. Clear the field before the host's arrives —
            // this is the step SaveCollector.ApplyWorldSubset used to perform,
            // and the reason it no longer applies enemies at all.
            var existing = new List<EnemyController>(EnemyController.ActiveEnemies);
            for (int i = 0; i < existing.Count; i++)
                if (existing[i] != null) Destroy(existing[i].gameObject);
        }

        _nextRequestAt = Time.unscaledTime + 3f;   // retry until a SyncEnd lands
        Write(w => w.WriteValueSafe(KindRequestAll),
              NetworkManager.ServerClientId, NetworkDelivery.ReliableSequenced, 8);
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;
        bool server = nm != null && nm.IsServer;

        switch (kind)
        {
            case KindRequestAll when server:
                SendFullListTo(senderId);
                break;

            // ⚠️ !server throughout: the authority must never be told its own
            // state. SendNamedMessage to a client does not loop back today, but
            // keeping the guard makes it impossible to reintroduce by accident.
            case KindSpawn when !server:        HandleSpawn(reader); break;
            case KindSyncEnd when !server:      _synced = true; break;
            case KindPose when !server:         HandlePoses(reader); break;
            case KindPerception when !server:   HandlePerception(reader); break;
            case KindDeath when !server:        HandleDeath(reader, nm); break;
            case KindDespawn when !server:      HandleDespawn(reader); break;
            case KindPlayerDamage when !server: HandlePlayerDamage(reader); break;

            case KindHit when server:           HandleHit(reader, senderId); break;
            case KindGunshot when server:       HandleGunshot(reader, senderId); break;
        }
    }

    void HandleSpawn(FastBufferReader reader)
    {
        reader.ReadValueSafe(out uint netId);
        reader.ReadValueSafe(out byte kindByte);
        reader.ReadValueSafe(out string bodyName);
        Vector3 localPos    = ReadVec3(reader);
        Quaternion localRot = ReadQuat(reader);

        // Idempotent: a full resync re-announces everything, and a duplicate
        // spawn would leave a second body nothing can address.
        if (_puppets.TryGetValue(netId, out var already) && already != null) return;

        var planet = ResolveBody(bodyName);
        if (planet == null || EnemySpawner.Instance == null)
        {
            Debug.LogWarning($"[EnemySync] Can't place enemy {netId}: " +
                             (planet == null ? $"no body named '{bodyName}'" : "no EnemySpawner"));
            return;
        }

        var pt = planet.transform;
        var ec = EnemySpawner.Instance.SpawnNetworkPuppet(
            (EnemyKind)kindByte, planet,
            pt.TransformPoint(localPos), pt.rotation * localRot, netId);
        if (ec == null) return;

        _puppets[netId] = ec;
        // Seeded standing still and unaware; the first real pose lands within
        // 100 ms and corrects both.
        ec.ReceiveNetworkPose(localPos, localRot, 0f, EnemyController.AIState.Docile);
    }

    void HandlePoses(FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort count);
        bool sawUnknown = false;

        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out uint netId);
            Vector3 localPos    = ReadVec3(reader);
            Quaternion localRot = ReadQuat(reader);
            reader.ReadValueSafe(out byte animByte);
            reader.ReadValueSafe(out byte stateByte);

            // Every entry is read whether or not we can use it — bailing early
            // would leave the rest of the batch unparsed and desync the reader
            // for every enemy after it in the same message.
            if (_puppets.TryGetValue(netId, out var ec) && ec != null)
                ec.ReceiveNetworkPose(localPos, localRot, animByte / 255f,
                                      (EnemyController.AIState)stateByte);
            else
                sawUnknown = true;
        }

        // Self-healing: a pose for an enemy we have never heard of means a Spawn
        // went missing (or arrived before this scene could place it). Ask for the
        // whole field again rather than leaving an alien permanently invisible on
        // this screen while it hunts us. Throttled, and the re-send is idempotent.
        if (sawUnknown && _synced)
        {
            _synced = false;
            _nextRequestAt = Time.unscaledTime + 1f;
        }
    }

    /// <summary>
    /// Our own spot-meters. Every enemy not named in this message has stopped
    /// noticing us, so they are cleared — otherwise an arrow would stick on
    /// screen at whatever value it last heard.
    /// </summary>
    void HandlePerception(FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort count);

        _perceptionSeen.Clear();
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out uint netId);
            reader.ReadValueSafe(out byte suspicionByte);
            _perceptionSeen.Add(netId);
            if (_puppets.TryGetValue(netId, out var ec) && ec != null)
                ec.ReceiveNetworkPerception(suspicionByte / 255f);
        }

        foreach (var kv in _puppets)
            if (kv.Value != null && !_perceptionSeen.Contains(kv.Key))
                kv.Value.ReceiveNetworkPerception(0f);
    }

    void HandleDeath(FastBufferReader reader, NetworkManager nm)
    {
        reader.ReadValueSafe(out uint netId);
        reader.ReadValueSafe(out byte credited);
        reader.ReadValueSafe(out ulong killerClientId);

        if (_puppets.TryGetValue(netId, out var ec) && ec != null)
        {
            bool mine = credited != 0 && nm != null && killerClientId == nm.LocalClientId;
            ec.RemoteDeath(mine);
        }
        // Dropped from the table here: the corpse ragdolls and shrinks away on
        // its own timer, identically on both machines, and needs no more poses.
        _puppets.Remove(netId);
    }

    void HandleDespawn(FastBufferReader reader)
    {
        reader.ReadValueSafe(out uint netId);
        if (_puppets.TryGetValue(netId, out var ec) && ec != null) Destroy(ec.gameObject);
        _puppets.Remove(netId);
    }

    void HandleHit(FastBufferReader reader, ulong senderId)
    {
        reader.ReadValueSafe(out uint netId);
        reader.ReadValueSafe(out float amount);

        if (!_known.TryGetValue(netId, out var e) || e == null) return;
        // Applied through the enemy's NORMAL damage path, so death, ragdoll,
        // loot and the kill-cam all run exactly as they do in single player —
        // and the credit is stamped with the client that fired.
        e.TakeDamageFromRemotePlayer(Mathf.Clamp(amount, 0f, MaxReportedDamage), senderId);
    }

    /// <summary>
    /// A guest fired. Guns are LOUD — every enemy within earshot should lock on
    /// and charge, and on a guest that never happened: PistolController calls
    /// EnemyController.AlertNearby locally, but a guest's enemies are puppets
    /// whose AI never runs, so it set a flag on twenty bodies that were only
    /// ever going to render what the host sent.
    ///
    /// NO POSITION CROSSES THE WIRE. The host already knows where that player is
    /// — their puppet is pose-synced right here — so it reads the position off
    /// its own copy. A world coordinate would have been stale on arrival anyway;
    /// floating-origin rebases fire while standing still.
    /// </summary>
    void HandleGunshot(FastBufferReader reader, ulong senderId)
    {
        reader.ReadValueSafe(out float radius);

        var puppets = PlanetRelativeSync.AllPuppets;
        for (int i = 0; i < puppets.Count; i++)
        {
            var p = puppets[i];
            if (p == null || p.OwnerClientId != senderId) continue;
            EnemyController.AlertNearby(p.transform.position, Mathf.Clamp(radius, 0f, 200f));
            return;
        }
    }

    void HandlePlayerDamage(FastBufferReader reader)
    {
        reader.ReadValueSafe(out float amount);
        var rm = ResourceManager.Instance;
        if (rm != null) rm.TakeDamage(Mathf.Clamp(amount, 0f, MaxReportedDamage));
    }

    // ── outbound API ─────────────────────────────────────────────────────

    /// <summary>
    /// Guest side: "my weapon hit this enemy." The host applies it.
    ///
    /// Only ever called from EnemyController's puppet branch, which is the single
    /// choke point every weapon already funnels through — so the pistol, the axe,
    /// the blade sweep and the fishing bobber all reach the host by the same road
    /// without any of them knowing a network exists.
    /// </summary>
    public static void ReportHitToHost(EnemyController enemy, float amount)
    {
        if (Instance == null || enemy == null || enemy.NetId == 0) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer) return;

        uint id = enemy.NetId;
        Instance.Write(w =>
        {
            w.WriteValueSafe(KindHit);
            w.WriteValueSafe(id);
            w.WriteValueSafe(amount);
        }, NetworkManager.ServerClientId, NetworkDelivery.ReliableSequenced, 24);
    }

    /// <summary>
    /// Host side: an enemy connected with a player who is not on this machine.
    ///
    /// This is the one place a guest is not authoritative over its own health,
    /// and it is deliberate: the host owns the AI, so it is the only machine that
    /// knows a swing landed. Making the victim authoritative instead would let a
    /// guest simply refuse damage. The 10 Hz pose stream is what stops it feeling
    /// unfair — at that rate plus interpolation, the alien that hit you is within
    /// a few centimetres of where you saw it.
    /// </summary>
    public static void DamageRemotePlayer(ulong clientId, float amount)
    {
        if (Instance == null || amount <= 0f) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) return;
        if (clientId == nm.LocalClientId) return;   // that is the host; apply locally instead

        Instance.Write(w =>
        {
            w.WriteValueSafe(KindPlayerDamage);
            w.WriteValueSafe(amount);
        }, clientId, NetworkDelivery.ReliableSequenced, 16);
    }

    // ── analytic hit tests: reaching a body that has no colliders ────────
    //
    // A puppet has no colliders, on purpose (EnemyController.MarkAsNetworkPuppet),
    // so nothing a weapon does through Physics can ever touch one. Every weapon
    // on a guest reaches an alien through here instead: the same ray-vs-capsule
    // test PvP already uses. No colliders, no layers, no collision-matrix edits,
    // and nothing that can push anybody anywhere.

    /// Cheap enough to call per swing. False on the host and in single player,
    /// which is what keeps the melee fallback out of the tuned path entirely.
    public static bool AnyPuppets
    {
        get
        {
            var live = EnemyController.ActiveEnemies;
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && live[i].IsNetworkPuppet) return true;
            return false;
        }
    }

    /// Generosity on the hitbox, in metres. A puppet's pose is a fraction of a
    /// second behind the host's, so a pixel-tight capsule would eat honest hits —
    /// the same reason NetworkPlayerCombat's astronaut capsule is a little fat.
    const float PuppetHitPadding = 0.15f;

    public static bool RayHitsPuppet(Vector3 origin, Vector3 dir, float maxDistance,
                                     out EnemyController best, out float bestDistance)
        => SweepHitsPuppet(origin, dir, maxDistance, 0f, out best, out bestDistance);

    /// <summary>
    /// A THICK ray — the swept blade of the physics axe. Inflating the target
    /// capsule by the sweep radius is exactly equivalent to sweeping a sphere
    /// against the thin one, so this needs no second piece of geometry.
    /// </summary>
    public static bool SweepHitsPuppet(Vector3 origin, Vector3 dir, float maxDistance,
                                       float sweepRadius,
                                       out EnemyController best, out float bestDistance)
    {
        best = null;
        bestDistance = float.MaxValue;
        if (dir.sqrMagnitude < 1e-6f) return false;

        var live = EnemyController.ActiveEnemies;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || !e.IsNetworkPuppet || e.IsDying) continue;

            PuppetBodyCapsule(e, out Vector3 a, out Vector3 b, out float radius);
            if (!NetworkPlayerCombat.RayHitsCapsule(origin, dir, a, b,
                                                    radius + sweepRadius, out float d)) continue;

            // A wall between us stops it. maxDistance is however far the real
            // world raycast reached — and since the puppet has no collider it can
            // never be what that raycast hit, which is exactly what makes this
            // comparison meaningful.
            if (d > maxDistance) continue;
            if (d < bestDistance) { bestDistance = d; best = e; }
        }
        return best != null;
    }

    /// <summary>
    /// The capsule standing in for an alien's body, in world space.
    ///
    /// Read from the enemy's own CapsuleCollider wherever there is one. It is
    /// DISABLED on a puppet, but a disabled collider's geometry is still intact —
    /// so the analytic test matches the shape the host is really colliding
    /// against, including the scaled-up elite, instead of a guessed constant that
    /// would drift the moment a prefab was retuned.
    ///
    /// Public and static purely so it can be exercised directly, for exactly the
    /// reason RayHitsCapsule is: the first version of THAT had a sign error and
    /// missed point-blank chest shots, and nothing on screen would have explained
    /// why. Same risk here — a capsule built along the wrong axis, or scaled by
    /// the wrong component, aims a whole weapon at empty air.
    /// </summary>
    public static void PuppetBodyCapsule(EnemyController e, out Vector3 a, out Vector3 b, out float radius)
    {
        var t = e.transform;
        Vector3 s = t.lossyScale;

        var cap = e.GetComponent<CapsuleCollider>();
        if (cap != null)
        {
            Vector3 axis = cap.direction == 0 ? Vector3.right
                         : cap.direction == 2 ? Vector3.forward
                         : Vector3.up;
            float half = Mathf.Max(0f, cap.height * 0.5f - cap.radius);
            a = t.TransformPoint(cap.center - axis * half);
            b = t.TransformPoint(cap.center + axis * half);
            radius = cap.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)) + PuppetHitPadding;
            return;
        }

        // No capsule authored: fall back to the 2 m body EnemySpawner assumes
        // (its CapsuleHalfHeight is 1), measured either side of the root.
        a = t.TransformPoint(new Vector3(0f, -0.9f, 0f));
        b = t.TransformPoint(new Vector3(0f,  0.9f, 0f));
        radius = 0.7f * Mathf.Abs(s.x) + PuppetHitPadding;
    }

    // ── the guest's own gunfire ──────────────────────────────────────────

    void OnLocalShot(PistolController.ShotInfo shot)
    {
        // Guests only. On the host the pistol's own raycast already struck the
        // enemy's real hit colliders and applied the damage through the ordinary
        // single-player path; doing it again here would double every shot.
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer) return;

        // Wake the field up, hit or miss — guns are loud. See HandleGunshot.
        Write(w => { w.WriteValueSafe(KindGunshot); w.WriteValueSafe(PistolController.GunshotAlertRadius); },
              NetworkManager.ServerClientId, NetworkDelivery.ReliableSequenced, 16);

        if (!RayHitsPuppet(shot.RayOrigin, shot.RayDirection, shot.WorldHitDistance,
                           out var enemy, out _)) return;

        // Straight into the enemy's ordinary damage entry point, which recognises
        // a puppet, reports the hit to the host, and paints the local blood and
        // health bar on the way past. Nothing here has to know about a network.
        enemy.TakeDamage(shot.Damage);
    }

    // ── transport ────────────────────────────────────────────────────────

    /// <summary>
    /// `onlyClient == ulong.MaxValue` means every connected client EXCEPT this
    /// machine; anything else addresses one peer.
    ///
    /// ⚠️ NEVER SendNamedMessageToAll. NGO delivers a broadcast back to the host
    /// itself, and the relay step then re-sends what it just received — the
    /// infinite rebroadcast storm behind "the host chops and the client never
    /// sees it, and everything lags". The flood starved real delivery.
    /// </summary>
    void Write(System.Action<FastBufferWriter> write, ulong onlyClient,
               NetworkDelivery delivery, int sizeHint)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !_registered) return;

        var w = new FastBufferWriter(Mathf.Max(64, sizeHint), Allocator.Temp, 1024 * 256);
        try
        {
            write(w);

            if (onlyClient != ulong.MaxValue)
            {
                nm.CustomMessagingManager.SendNamedMessage(Msg, onlyClient, w, delivery);
                return;
            }

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
                nm.CustomMessagingManager.SendNamedMessage(Msg, id, w, delivery);
            }
        }
        finally { w.Dispose(); }
    }

    // Vectors and quaternions go out as loose floats rather than relying on a
    // struct overload, so nothing here depends on which NGO version supplies
    // which WriteValueSafe specialisation.
    static void WriteVec3(FastBufferWriter w, Vector3 v)
    { w.WriteValueSafe(v.x); w.WriteValueSafe(v.y); w.WriteValueSafe(v.z); }

    static void WriteQuat(FastBufferWriter w, Quaternion q)
    { w.WriteValueSafe(q.x); w.WriteValueSafe(q.y); w.WriteValueSafe(q.z); w.WriteValueSafe(q.w); }

    static Vector3 ReadVec3(FastBufferReader r)
    {
        r.ReadValueSafe(out float x); r.ReadValueSafe(out float y); r.ReadValueSafe(out float z);
        return new Vector3(x, y, z);
    }

    static Quaternion ReadQuat(FastBufferReader r)
    {
        r.ReadValueSafe(out float x); r.ReadValueSafe(out float y);
        r.ReadValueSafe(out float z); r.ReadValueSafe(out float w);
        return new Quaternion(x, y, z, w);
    }

    static CelestialBody ResolveBody(string bodyName)
    {
        if (string.IsNullOrEmpty(bodyName)) return null;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i] != null && bodies[i].bodyName == bodyName) return bodies[i];
        return null;
    }
}
