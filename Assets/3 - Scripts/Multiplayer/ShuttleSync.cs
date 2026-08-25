using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// Shuttle-travel replication (2026-08-25, handoff §7).
///
/// THE HOST OWNS THE STATE MACHINE (WorldSync's one rule — the stasis door
/// cost three attempts to learn it). Guests are switched to render-only via
/// ShuttleAutopilot.ClientDriven and told:
///   host -> all : PHASE   (reliable, on change + 2 s heartbeat: phase, target
///                 body, elapsed, pilot client id) — ABSOLUTE state, never a
///                 toggle, so duplicates/reorders can't invert anything.
///   host -> all : POSE    (unreliable, 10 Hz: frame body name + local pose +
///                 transit progress — every value absolute, a drop
///                 self-corrects 100 ms later)
///   host -> all : VALID   (reliable, on change + heartbeat)
///   client -> host: travel request, pilot claim, pilot input (~30 Hz
///                 unreliable absolute — the TraxSessionSync dial lesson),
///                 land (reliable one-shot — a dropped land press is not
///                 self-correcting).
///
/// Pilot lease (D-3): first NAV user during HOVER owns steering; the lease
/// lives host-side, rides the phase message, clears on PARKED / disconnect.
/// Named messages, no NetworkObject; ⚠️ NEVER SendNamedMessageToAll (the
/// host-loopback rebroadcast storm — EnemySync's warning).
public class ShuttleSync : MonoBehaviour
{
    const string Msg = "ShuttleSync";

    const byte KindRequestState = 0;   // client -> host
    const byte KindPhase        = 1;   // host -> clients
    const byte KindPose         = 2;   // host -> clients
    const byte KindValid        = 3;   // host -> clients
    const byte KindTravel       = 4;   // client -> host
    const byte KindPilotInput   = 5;   // client -> host
    const byte KindLand         = 6;   // client -> host
    const byte KindPilotClaim   = 7;   // client -> host

    const float PhaseHeartbeat = 2f;
    const float PoseInterval = 0.1f;
    const float PilotInputInterval = 1f / 30f;
    const float ClaimInterval = 1f;

    public static ShuttleSync Instance { get; private set; }

    bool _registered;
    ShuttleAutopilot _subscribed;
    float _nextPhaseBeatAt;
    float _nextPoseAt;
    float _nextInputSendAt;
    float _nextClaimAt;
    float _nextStateRequestAt;
    bool _phaseHeard;               // guest: stop re-requesting once state lands
    bool _lastSentValid;
    float _nextValidBeatAt;
    ulong _pilotClientId = ulong.MaxValue;   // host-authoritative lease

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Deliberately does NOT skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1).
        var go = new GameObject("ShuttleSync");
        DontDestroyOnLoad(go);
        go.AddComponent<ShuttleSync>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        _registered = false;
        _subscribed = null;
        _phaseHeard = false;
        _pilotClientId = ulong.MaxValue;
    }

    static bool SessionLive
    {
        get { var nm = NetworkManager.Singleton; return nm != null && nm.IsListening; }
    }

    static bool IsHost
    {
        get { var nm = NetworkManager.Singleton; return nm != null && nm.IsListening && nm.IsServer; }
    }

    // ── public API (NAV / autopilot call these; all no-op in single player) ──

    /// May THIS machine steer the hover right now? Single player: always.
    /// In co-op: only the lease holder (or anyone while the lease is free —
    /// the first input claims it implicitly via TryClaimPilot).
    public static bool LocalCanSteer
    {
        get
        {
            if (!SessionLive || Instance == null) return true;
            var nm = NetworkManager.Singleton;
            return Instance._pilotClientId == ulong.MaxValue
                || Instance._pilotClientId == nm.LocalClientId;
        }
    }

    /// Display name of the current pilot, for the non-pilot's NAV overlay.
    public static string PilotName
    {
        get
        {
            if (Instance == null || Instance._pilotClientId == ulong.MaxValue) return "";
            foreach (var p in PlanetRelativeSync.AllPuppets)
            {
                if (p == null || p.OwnerClientId != Instance._pilotClientId) continue;
                var id = p.GetComponent<NetworkPlayerIdentity>();
                if (id != null && !string.IsNullOrEmpty(id.DisplayName)) return id.DisplayName;
            }
            return "PARTNER";
        }
    }

    /// NAV calls this every steering frame; claims the free lease (D-3).
    public static void TryClaimPilot()
    {
        if (!SessionLive || Instance == null) return;
        var inst = Instance;
        if (inst._pilotClientId != ulong.MaxValue) return;
        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            inst._pilotClientId = nm.LocalClientId;
            inst.SendPhaseToAll();   // the lease rides the phase message
        }
        else if (Time.unscaledTime >= inst._nextClaimAt)
        {
            inst._nextClaimAt = Time.unscaledTime + ClaimInterval;
            inst.Send(w => w.WriteValueSafe(KindPilotClaim),
                      NetworkManager.ServerClientId, NetworkDelivery.ReliableSequenced, 8);
        }
    }

    public static void SendTravelRequest(string bodyName)
    {
        if (Instance == null || !SessionLive) return;
        Instance.Send(w => { w.WriteValueSafe(KindTravel); w.WriteValueSafe(bodyName ?? ""); },
                      NetworkManager.ServerClientId, NetworkDelivery.ReliableSequenced,
                      (bodyName?.Length ?? 0) * 4 + 16);
    }

    public static void SendPilotInput(Vector2 move, float yaw)
    {
        if (Instance == null || !SessionLive) return;
        var inst = Instance;
        if (Time.unscaledTime < inst._nextInputSendAt) return;
        inst._nextInputSendAt = Time.unscaledTime + PilotInputInterval;
        TryClaimPilot();
        inst.Send(w =>
        {
            w.WriteValueSafe(KindPilotInput);
            w.WriteValueSafe(move.x); w.WriteValueSafe(move.y); w.WriteValueSafe(yaw);
        }, NetworkManager.ServerClientId, NetworkDelivery.UnreliableSequenced, 20);
    }

    public static void SendLandRequest()
    {
        if (Instance == null || !SessionLive) return;
        Instance.Send(w => w.WriteValueSafe(KindLand),
                      NetworkManager.ServerClientId, NetworkDelivery.ReliableSequenced, 8);
    }

    // ── drive ────────────────────────────────────────────────────────────

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            ShuttleAutopilot.ClientDriven = false;
            _registered = false;
            _phaseHeard = false;
            _pilotClientId = ulong.MaxValue;
            return;
        }

        // Registration must happen on EVERY machine, not just senders — a host
        // that never sent used to silently drop everything a client told it
        // (StasisDoorSync's documented bug).
        if (!_registered && nm.CustomMessagingManager != null)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        var pilot = ShuttleAutopilot.Instance;

        if (!nm.IsServer)
        {
            ShuttleAutopilot.ClientDriven = true;
            // Pull-not-push join: ask for state until the first phase lands.
            if (!_phaseHeard && pilot != null && Time.unscaledTime >= _nextStateRequestAt
                && NBodySimulation.Bodies.Length > 0)
            {
                _nextStateRequestAt = Time.unscaledTime + 3f;
                Send(w => w.WriteValueSafe(KindRequestState),
                     NetworkManager.ServerClientId, NetworkDelivery.ReliableSequenced, 8);
            }
            return;
        }

        // ── host ──
        ShuttleAutopilot.ClientDriven = false;
        if (pilot == null) return;

        if (_subscribed != pilot)
        {
            if (_subscribed != null) _subscribed.OnPhaseChanged -= OnHostPhaseChanged;
            _subscribed = pilot;
            pilot.OnPhaseChanged += OnHostPhaseChanged;
        }

        // Lease housekeeping: a disconnected pilot must not hold the stick.
        if (_pilotClientId != ulong.MaxValue && _pilotClientId != nm.LocalClientId)
        {
            bool connected = false;
            var ids = nm.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++) if (ids[i] == _pilotClientId) { connected = true; break; }
            if (!connected) { _pilotClientId = ulong.MaxValue; SendPhaseToAll(); }
        }

        if (Time.unscaledTime >= _nextPhaseBeatAt)
        {
            _nextPhaseBeatAt = Time.unscaledTime + PhaseHeartbeat;
            SendPhaseToAll();
        }

        if (pilot.CurrentPhase != ShuttleAutopilot.Phase.Parked && Time.unscaledTime >= _nextPoseAt)
        {
            _nextPoseAt = Time.unscaledTime + PoseInterval;
            SendPoseToAll(pilot);
        }

        bool valid = pilot.LandingValid;
        if (valid != _lastSentValid || Time.unscaledTime >= _nextValidBeatAt)
        {
            _lastSentValid = valid;
            _nextValidBeatAt = Time.unscaledTime + PhaseHeartbeat;
            SendToAll(w => { w.WriteValueSafe(KindValid); w.WriteValueSafe((byte)(valid ? 1 : 0)); },
                      NetworkDelivery.ReliableSequenced, 8);
        }
    }

    void OnHostPhaseChanged(ShuttleAutopilot.Phase phase)
    {
        if (phase == ShuttleAutopilot.Phase.Parked) _pilotClientId = ulong.MaxValue;   // lease dies with the flight
        SendPhaseToAll();
        if (ShuttleAutopilot.Instance != null) SendPoseToAll(ShuttleAutopilot.Instance);
    }

    // ── senders ──────────────────────────────────────────────────────────

    void SendPhaseToAll()
    {
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null) return;
        byte phase = (byte)pilot.CurrentPhase;
        string target = pilot.TargetBody != null ? pilot.TargetBody.bodyName : "";
        float elapsed = pilot.PhaseElapsed;
        ulong pilotId = _pilotClientId;
        SendToAll(w =>
        {
            w.WriteValueSafe(KindPhase);
            w.WriteValueSafe(phase);
            w.WriteValueSafe(target);
            w.WriteValueSafe(elapsed);
            w.WriteValueSafe(pilotId);
        }, NetworkDelivery.ReliableSequenced, target.Length * 4 + 32);
    }

    void SendPoseToAll(ShuttleAutopilot pilot)
    {
        pilot.GetPoseForSync(out string body, out Vector3 lp, out Quaternion lr);
        if (string.IsNullOrEmpty(body)) return;
        float progress = pilot.TransitProgress;
        SendToAll(w =>
        {
            w.WriteValueSafe(KindPose);
            w.WriteValueSafe(body);
            w.WriteValueSafe(lp.x); w.WriteValueSafe(lp.y); w.WriteValueSafe(lp.z);
            w.WriteValueSafe(lr.x); w.WriteValueSafe(lr.y); w.WriteValueSafe(lr.z); w.WriteValueSafe(lr.w);
            w.WriteValueSafe(progress);
        }, NetworkDelivery.UnreliableSequenced, body.Length * 4 + 48);
    }

    void SendStateTo(ulong clientId)
    {
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null) return;
        byte phase = (byte)pilot.CurrentPhase;
        string target = pilot.TargetBody != null ? pilot.TargetBody.bodyName : "";
        float elapsed = pilot.PhaseElapsed;
        ulong pilotId = _pilotClientId;
        Send(w =>
        {
            w.WriteValueSafe(KindPhase);
            w.WriteValueSafe(phase);
            w.WriteValueSafe(target);
            w.WriteValueSafe(elapsed);
            w.WriteValueSafe(pilotId);
        }, clientId, NetworkDelivery.ReliableSequenced, target.Length * 4 + 32);
        pilot.GetPoseForSync(out string body, out Vector3 lp, out Quaternion lr);
        if (!string.IsNullOrEmpty(body))
        {
            float progress = pilot.TransitProgress;
            Send(w =>
            {
                w.WriteValueSafe(KindPose);
                w.WriteValueSafe(body);
                w.WriteValueSafe(lp.x); w.WriteValueSafe(lp.y); w.WriteValueSafe(lp.z);
                w.WriteValueSafe(lr.x); w.WriteValueSafe(lr.y); w.WriteValueSafe(lr.z); w.WriteValueSafe(lr.w);
                w.WriteValueSafe(progress);
            }, clientId, NetworkDelivery.ReliableSequenced, body.Length * 4 + 48);
        }
        bool valid = pilot.LandingValid;
        Send(w => { w.WriteValueSafe(KindValid); w.WriteValueSafe((byte)(valid ? 1 : 0)); },
             clientId, NetworkDelivery.ReliableSequenced, 8);
    }

    // ── receive ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;
        var pilot = ShuttleAutopilot.Instance;

        if (nm != null && nm.IsServer)
        {
            switch (kind)
            {
                case KindRequestState:
                    SendStateTo(senderId);
                    break;

                case KindTravel:
                {
                    reader.ReadValueSafe(out string body);
                    if (pilot != null) pilot.RequestTravelByName(body);
                    break;
                }

                case KindPilotClaim:
                    if (_pilotClientId == ulong.MaxValue)
                    {
                        _pilotClientId = senderId;   // first claim wins, in arrival order
                        SendPhaseToAll();
                    }
                    break;

                case KindPilotInput:
                {
                    reader.ReadValueSafe(out float mx);
                    reader.ReadValueSafe(out float my);
                    reader.ReadValueSafe(out float yaw);
                    // Only the lease holder steers — anything else is dropped
                    // (the "two pilots" duplication cannot be expressed).
                    if (pilot != null && senderId == _pilotClientId)
                        pilot.SetPilotInput(new Vector2(Mathf.Clamp(mx, -1f, 1f), Mathf.Clamp(my, -1f, 1f)),
                                            Mathf.Clamp(yaw, -1f, 1f));
                    break;
                }

                case KindLand:
                    if (pilot != null && senderId == _pilotClientId)
                        pilot.RequestLand();
                    break;
            }
            return;
        }

        // ── guest ──
        switch (kind)
        {
            case KindPhase:
            {
                reader.ReadValueSafe(out byte phase);
                reader.ReadValueSafe(out string target);
                reader.ReadValueSafe(out float elapsed);
                reader.ReadValueSafe(out ulong pilotId);
                _pilotClientId = pilotId;
                _phaseHeard = true;
                if (pilot != null)
                    pilot.ApplyRemotePhase((ShuttleAutopilot.Phase)phase, target, elapsed);
                break;
            }

            case KindPose:
            {
                reader.ReadValueSafe(out string body);
                reader.ReadValueSafe(out float px); reader.ReadValueSafe(out float py); reader.ReadValueSafe(out float pz);
                reader.ReadValueSafe(out float rx); reader.ReadValueSafe(out float ry); reader.ReadValueSafe(out float rz); reader.ReadValueSafe(out float rw);
                reader.ReadValueSafe(out float progress);
                if (pilot != null)
                    pilot.ApplyRemotePose(body, new Vector3(px, py, pz), new Quaternion(rx, ry, rz, rw), progress);
                break;
            }

            case KindValid:
            {
                reader.ReadValueSafe(out byte v);
                if (pilot != null) pilot.ApplyRemoteValid(v != 0);
                break;
            }
        }
    }

    // ── plumbing (EnemySync's Write helper: loose floats, never ToAll) ───

    void Send(System.Action<FastBufferWriter> write, ulong onlyClient, NetworkDelivery delivery, int sizeHint)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !_registered) return;
        var w = new FastBufferWriter(Mathf.Max(64, sizeHint), Allocator.Temp, 1024 * 64);
        try
        {
            write(w);
            nm.CustomMessagingManager.SendNamedMessage(Msg, onlyClient, w, delivery);
        }
        finally { w.Dispose(); }
    }

    void SendToAll(System.Action<FastBufferWriter> write, NetworkDelivery delivery, int sizeHint)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || !_registered) return;
        var ids = nm.ConnectedClientsIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == nm.LocalClientId) continue;   // never loop back to ourselves
            Send(write, ids[i], delivery, sizeHint);
        }
    }
}
