using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the stasis pod door and its valve button identical on every screen.
///
/// ── Why the first two attempts failed ────────────────────────────────────
/// The door's behaviour is a small state machine — open on a button press,
/// close after a delay, seal behind you when you step in, re-close if you never
/// enter. Attempt one mirrored the door's target; attempt two mirrored each
/// machine's "wish" and took the union. Both fail for the same reason: every
/// machine was still RUNNING that state machine, against its own local player
/// and its own timer, so the two copies drifted apart and fought. The door
/// closed on one screen and stuck open on the other.
///
/// A replicated state machine needs ONE owner. The host has it now:
///
///   client -> host : where my player is standing (ZONE), and "I pressed the
///                    valve" (PRESS). Nothing else. The client's own door logic
///                    is switched off entirely via StasisPodDoor.ClientDriven.
///   host -> all    : the door's target (STATE), and "play the button press"
///                    (ANIM), so the button visibly depresses everywhere.
///
/// The host folds the reported zones into its own so that "nobody is in MY copy
/// of the pod" can't slam the door on a player standing in it elsewhere.
///
/// STATE is also re-sent every couple of seconds. It is one byte, and it means
/// a late joiner or any dropped update self-corrects instead of leaving the
/// door wrong until someone touches it again.
///
/// Named messages rather than RPCs for the same reason as the orbit and clock
/// sync: there is no RPC layer here, and a named message needs no NetworkObject.
/// </summary>
public class StasisDoorSync : MonoBehaviour
{
    public static StasisDoorSync Instance { get; private set; }

    const string Msg = "StasisDoor";

    const byte KindZone  = 0;   // client -> host
    const byte KindPress = 1;   // client -> host
    const byte KindState = 2;   // host -> all
    const byte KindAnim  = 3;   // host -> all
    const byte KindOpen  = 4;   // client -> host, "let me out" with no button

    const float ResendInterval = 2f;
    /// Clients re-announce their zone this often even when it hasn't changed.
    const float ZoneRepeatInterval = 1f;
    /// Host forgets a remote zone it hasn't heard about in this long.
    const float ZoneStaleSeconds = 4f;

    bool _registered;
    float _resendTimer, _zoneTimer;
    float _lastZoneHeard;
    StasisPodDoor.Zone _lastSentZone = (StasisPodDoor.Zone)(-1);

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
        _registered = false;
        _lastSentZone = (StasisPodDoor.Zone)(-1);
        StasisPodDoor.RemoteZone = StasisPodDoor.Zone.Outside;
        StasisPodDoor.ClientDriven = false;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        bool live = nm != null && nm.IsListening;

        if (!live)
        {
            // Back to single player: the door owns itself again.
            if (_registered)
            {
                _registered = false;
                StasisPodDoor.ClientDriven = false;
                StasisPodDoor.RemoteZone = StasisPodDoor.Zone.Outside;
            }
            return;
        }

        // Registration must happen on EVERY machine, not only senders — a host
        // that never sent used to never register, and silently dropped
        // everything a client told it.
        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        StasisPodDoor.ClientDriven = !nm.IsServer;

        var door = FindDoor();
        if (door == null) return;

        if (nm.IsServer)
        {
            // Authority: repeat the truth on a slow tick so nothing stays wrong.
            _resendTimer += Time.unscaledDeltaTime;
            if (_resendTimer >= ResendInterval)
            {
                _resendTimer = 0f;
                if (nm.ConnectedClientsIds.Count > 1) SendState(door.TargetOpen > 0.5f);
            }

            // A remote zone that stops arriving must DECAY, not persist. A stuck
            // "someone is deep in the pod" holds the door open forever and is
            // impossible to tell from a player genuinely standing there.
            if (StasisPodDoor.RemoteZone != StasisPodDoor.Zone.Outside
                && Time.unscaledTime - _lastZoneHeard > ZoneStaleSeconds)
                StasisPodDoor.RemoteZone = StasisPodDoor.Zone.Outside;

            // Nobody connected: there is no remote player to be anywhere.
            if (nm.ConnectedClientsIds.Count <= 1)
                StasisPodDoor.RemoteZone = StasisPodDoor.Zone.Outside;
        }
        else
        {
            // Client: the only thing we volunteer is where we're standing.
            // Re-sent on a timer as well as on change — one dropped or badly
            // timed update would otherwise leave the host believing we are
            // still inside the pod for the rest of the session.
            var zone = door.LocalZone;
            _zoneTimer += Time.unscaledDeltaTime;
            if (zone != _lastSentZone || _zoneTimer >= ZoneRepeatInterval)
            {
                _lastSentZone = zone;
                _zoneTimer = 0f;
                SendToHost(KindZone, (byte)zone);
            }
        }
    }

    static StasisPodDoor _door;
    static StasisPodDoor FindDoor()
    {
        if (_door == null) _door = Object.FindObjectOfType<StasisPodDoor>();
        return _door;
    }

    // ── outbound ─────────────────────────────────────────────────────────

    /// Called by StasisPodDoor.SetTarget on the AUTHORITY only.
    public static void NotifyAuthoritativeTarget(float target)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) return;
        if (Instance == null || !Instance._registered) return;
        Instance.SendState(target > 0.5f);
    }

    /// Called by the valve button. On the host it just opens the door; on a
    /// client it asks the host to, because the client isn't allowed to decide.
    public static void RequestValvePress(StasisPodDoor door, float openSeconds)
    {
        var nm = NetworkManager.Singleton;
        bool live = nm != null && nm.IsListening;

        if (!live || nm.IsServer)
        {
            if (door != null) door.OpenForSeconds(openSeconds);
            // Tell everyone to play the press so the button moves on their
            // screen too, not just the door.
            if (live && Instance != null && Instance._registered) Instance.SendAnim();
            return;
        }
        Instance?.SendToHost(KindPress, 0);
    }

    /// A joining player waking in the pod needs the door opened so they can get
    /// out — but no button was pressed, so this asks WITHOUT the press visual.
    /// On a client the request goes to the host, because only the host may open.
    public static void RequestOpen()
    {
        var nm = NetworkManager.Singleton;
        bool live = nm != null && nm.IsListening;
        var door = FindDoor();

        if (!live || nm.IsServer)
        {
            if (door != null) door.OpenHold();
            return;
        }
        Instance?.SendToHost(KindOpen, 0);
    }

    void SendState(bool open)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        var w = new FastBufferWriter(2, Allocator.Temp);
        try
        {
            w.WriteValueSafe(KindState);
            w.WriteValueSafe((byte)(open ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessageToAll(Msg, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void SendAnim()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        var w = new FastBufferWriter(2, Allocator.Temp);
        try
        {
            w.WriteValueSafe(KindAnim);
            w.WriteValueSafe((byte)0);
            nm.CustomMessagingManager.SendNamedMessageToAll(Msg, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void SendToHost(byte kind, byte payload)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || !_registered) return;
        var w = new FastBufferWriter(2, Allocator.Temp);
        try
        {
            w.WriteValueSafe(kind);
            w.WriteValueSafe(payload);
            nm.CustomMessagingManager.SendNamedMessage(
                Msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out byte payload);

        var nm = NetworkManager.Singleton;
        var door = FindDoor();

        switch (kind)
        {
            case KindZone when nm != null && nm.IsServer:
                StasisPodDoor.RemoteZone = (StasisPodDoor.Zone)payload;
                _lastZoneHeard = Time.unscaledTime;
                break;

            case KindPress when nm != null && nm.IsServer:
                // A client asked. The host is the one allowed to say yes, and
                // its SetTarget broadcasts the result back out.
                if (door != null) door.OpenForSeconds(ValveOpenSeconds(door));
                SendAnim();
                break;

            case KindOpen when nm != null && nm.IsServer:
                if (door != null) door.OpenHold();
                break;

            // ⚠️ `!nm.IsServer` IS THE WHOLE FIX FOR "THE DOOR NEVER CLOSES".
            //
            // SendNamedMessageToAll targets NetworkManager.ConnectedClientsIds,
            // and on a host that list INCLUDES the host's own client id. NGO
            // then delivers it locally — CustomMessageManager.cs:342,
            // `if (IsHost) … if (clientIds[i] == LocalClientId) InvokeNamedMessage(…)`.
            //
            // So the host's own periodic resend came straight back to it, hit
            // this case, and called NetSetTarget(1) → OpenHold() → _closeAt = -1,
            // wiping the pending close every ResendInterval (2s). The door
            // physically could not close while anyone was connected.
            //
            // It also explains the earlier "closes only once they walk away":
            // the old 2s autoCloseDelay was racing the 2s resend and sometimes
            // won. Raising the deep-exit grace to 5s made it lose every time,
            // which is why the door got WORSE rather than better.
            //
            // The host is the authority. Being told its own state is meaningless
            // at best and destructive at worst, so it ignores it.
            case KindState when nm != null && !nm.IsServer:
                if (door != null) door.NetSetTarget(payload != 0 ? 1f : 0f);
                break;

            case KindAnim:
                var valve = Object.FindObjectOfType<StasisValveButton>();
                if (valve != null) valve.PlayPressAnim();
                break;
        }
    }

    /// The valve's own openSeconds, so a host-side open triggered by a client
    /// lasts exactly as long as a local press would.
    static float ValveOpenSeconds(StasisPodDoor door)
    {
        var valve = Object.FindObjectOfType<StasisValveButton>();
        return valve != null ? valve.OpenSeconds : 6f;
    }
}
