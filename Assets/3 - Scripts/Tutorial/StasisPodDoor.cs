using UnityEngine;

// Persistent stasis-pod door controller (lives on the StasisPod group in the
// Shuttle_Lander prefab). The intro cinematic owns the door while it runs
// (ShuttleArrivalSequence.OpenStasisDoor); afterwards this takes over:
//   • entering the pod (stepping fully onto the plinth) seals the door behind
//     you after autoCloseDelay seconds — the save-ritual trigger,
//   • leaving the pod closes the door autoCloseDelay seconds after exit,
//   • a close never executes while the player straddles the DOORWAY strip,
//   • OpenForSeconds(s) — the valve-wheel button: open now, close s later,
//   • OpenHold() — open and stay open until the exit rule closes it
//     (used by StasisPodSave when the ritual finishes).
// Same retract recipe as the intro: the pivot IS the door's top edge, so
// shrinking scale.y slides the leaf up into its own top line. Re-enables the
// leaf renderers + colliders the intro disabled.
public class StasisPodDoor : MonoBehaviour
{
    public enum Zone { Outside, Doorway, Deep }

    [Tooltip("Seconds for a full open/close slide.")]
    public float animSeconds = 1.2f;
    [Tooltip("Seconds after entering/leaving the pod before the door closes.")]
    public float autoCloseDelay = 2f;
    [Range(0f, 1f)] public float doorVolume = 0.9f;

    [Tooltip("Occupancy box in POD-LOCAL space (covers plinth + doorway).")]
    public Vector3 boxMin = new Vector3(-0.9f, 0f, -0.6f);
    public Vector3 boxMax = new Vector3(0.9f, 3.2f, 0.8f);
    [Tooltip("Pod-local z above which the player counts as in the DOORWAY (close deferred), below as fully inside.")]
    public float doorwayZ = 0.35f;

    Transform _pivot;
    Renderer[] _leafRenderers;
    Collider[] _leafColliders;
    ShuttleArrivalSequence _seq;
    PlayerController _pc;
    AudioSource _src;
    float _openT, _target;      // 0 closed .. 1 open
    float _closeAt = -1f;
    Zone _prevZone = Zone.Outside;
    bool _seqWasActive;
    float _refind;

    public bool IsOpen => _openT > 0.5f;
    public bool IsFullyClosed => _openT <= 0.01f && _target <= 0.01f;
    public bool IsFullyOpen => _openT >= 0.99f && _target >= 0.99f;
    public Zone CurrentZone { get; private set; } = Zone.Outside;

    void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "StasisDoor_Pivot") { _pivot = t; break; }
        if (_pivot == null) { enabled = false; return; }
        _leafRenderers = _pivot.GetComponentsInChildren<Renderer>(true);
        _leafColliders = _pivot.GetComponentsInChildren<Collider>(true);
        _seq = GetComponentInParent<ShuttleArrivalSequence>();

        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0.8f;
        _src.rolloffMode = AudioRolloffMode.Linear;
        _src.maxDistance = 15f;

        _openT = _target = 1f - Mathf.Clamp01(_pivot.localScale.y);   // adopt authored state
    }

    /// Valve-wheel entry point: open now, close openSeconds later.
    public void OpenForSeconds(float openSeconds)
    {
        SetTarget(1f);
        _closeAt = Time.time + openSeconds;
    }

    /// Ritual exit point: open and stay open — the exit rule closes it.
    public void OpenHold()
    {
        SetTarget(1f);
        _closeAt = -1f;
    }

    /// ── Multiplayer: the HOST owns this door ─────────────────────────────
    ///
    /// The first attempt mirrored each machine's wishes and let both run their
    /// own logic. That cannot work: the open/close decisions are driven by
    /// proximity and a timer, so two machines drift apart and fight — the door
    /// shut on one screen and stuck open on the other, exactly as reported.
    ///
    /// So there is now one state machine, on the host. Clients don't decide
    /// anything; they report where their player is standing and animate toward
    /// whatever target the host sends back.

    /// Set on a machine that is a CONNECTED CLIENT (not the host). Such a
    /// machine skips the decision logic entirely.
    public static bool ClientDriven;

    /// The most-inside zone any REMOTE player occupies. The host folds this in
    /// so its own "nobody's in the pod" reading can't slam the door on someone
    /// standing in it on another screen.
    public static Zone RemoteZone = Zone.Outside;

    /// Network entry: set the door target without re-broadcasting it.
    public void NetSetTarget(float t)
    {
        _suppressBroadcast = true;
        if (t > 0.5f) OpenHold(); else { _closeAt = -1f; SetTarget(0f); }
        _suppressBroadcast = false;
    }

    /// Current target, for the host's periodic state broadcast.
    public float TargetOpen => _target;

    /// Local player's zone, for a client to report upstream.
    public Zone LocalZone => PlayerZone();

    /// Local and remote combined — the deepest wins, because "someone is in the
    /// pod" has to beat "nobody is in MY copy of the pod".
    Zone EffectiveZone(Zone local)
    {
        Zone remote = RemoteZone;
        if (local == Zone.Deep || remote == Zone.Deep) return Zone.Deep;
        if (local == Zone.Doorway || remote == Zone.Doorway) return Zone.Doorway;
        return Zone.Outside;
    }

    bool _suppressBroadcast;

    void SetTarget(float t)
    {
        if (Mathf.Approximately(_target, t)) return;
        _target = t;
        // Only the authority announces. A client never reaches here on its own.
        if (!_suppressBroadcast) StasisDoorSync.NotifyAuthoritativeTarget(t);
        // Passable the moment it starts moving (same rule as the intro).
        if (t > 0.5f && _leafColliders != null)
            foreach (var c in _leafColliders) if (c != null) c.enabled = false;
        if (_seq != null && _seq.stasisDoorClip != null && _src != null)
            _src.PlayOneShot(_seq.stasisDoorClip, doorVolume);
    }

    void Update()
    {
        // The intro cinematic owns the door while it runs — just mirror state.
        if (_seq != null && _seq.IsActive)
        {
            _openT = _target = 1f - Mathf.Clamp01(_pivot.localScale.y);
            CurrentZone = _prevZone = PlayerZone();
            _seqWasActive = true;
            _closeAt = -1f;
            return;
        }
        // Handoff frame: adopt the current zone WITHOUT firing a transition, so
        // waking up already inside the pod doesn't slam the door / fake a save
        // ritual. If the intro left the door open with the player outside,
        // schedule the close ourselves.
        if (_seqWasActive)
        {
            _seqWasActive = false;
            CurrentZone = _prevZone = PlayerZone();
            if (IsOpen && CurrentZone == Zone.Outside) _closeAt = Time.time + autoCloseDelay;
        }

        CurrentZone = PlayerZone();

        // A client decides nothing — it reports its zone upstream and animates
        // toward whatever the host last sent. Running the rules here as well is
        // what made the door disagree between screens.
        if (ClientDriven)
        {
            _prevZone = CurrentZone;
            AnimateToTarget();
            return;
        }

        // Host (or single player): decide on LOCAL + REMOTE together.
        Zone eff = EffectiveZone(CurrentZone);

        // Entered the pod proper → the door seals behind you (save ritual).
        if (eff == Zone.Deep && _prevEffective != Zone.Deep && IsOpen)
            _closeAt = Time.time + autoCloseDelay;
        // Stepped fully out → close behind you.
        else if (eff == Zone.Outside && _prevEffective != Zone.Outside && IsOpen)
            _closeAt = Time.time + autoCloseDelay;
        _prevZone = CurrentZone;
        _prevEffective = eff;

        // STEADY-STATE SAFETY NET — this is what makes the door reliable.
        //
        // Everything above is edge-triggered: it schedules a close when a zone
        // TRANSITION is observed. Miss one edge and the door stays open forever,
        // which is precisely what happened in co-op — a remote player's zone
        // update arriving late, or two transitions landing in the same frame,
        // and the "close behind you" edge never fires.
        //
        // So don't rely on the edge. If the door is open, nobody is anywhere
        // near it, and no close is pending, schedule one. This also cleans up
        // after OpenHold(), which deliberately sets no timer and would otherwise
        // leave the door open indefinitely once its occupant walked away.
        if (IsOpen && _closeAt <= 0f && eff == Zone.Outside)
            _closeAt = Time.time + autoCloseDelay;

        // Execute a due close — but never while ANY player straddles the doorway
        // plane, on either machine (the leaf would land on them).
        if (IsOpen && _closeAt > 0f && Time.time >= _closeAt && eff != Zone.Doorway)
        {
            _closeAt = -1f;
            SetTarget(0f);
        }

        AnimateToTarget();
    }

    Zone _prevEffective = Zone.Outside;

    void AnimateToTarget()
    {

        // Animate toward target.
        if (!Mathf.Approximately(_openT, _target))
        {
            _openT = Mathf.MoveTowards(_openT, _target, Time.deltaTime / Mathf.Max(0.05f, animSeconds));
            Apply(Mathf.SmoothStep(0f, 1f, _openT));
            if (_openT <= 0.001f && _leafColliders != null)
                foreach (var c in _leafColliders) if (c != null) c.enabled = true;   // sealed again
        }
    }

    // openT: 0 closed .. 1 fully retracted.
    void Apply(float openT)
    {
        float s = 1f - openT;
        var sc = _pivot.localScale;
        sc.y = Mathf.Max(0.001f, s);
        _pivot.localScale = sc;
        bool visible = s > 0.001f;
        if (_leafRenderers != null)
            foreach (var r in _leafRenderers) if (r != null) r.enabled = visible;
    }

    Zone PlayerZone()
    {
        if (_pc == null)
        {
            _refind -= Time.deltaTime;
            if (_refind > 0f) return _prevZone;
            _refind = 0.5f;
            _pc = FindObjectOfType<PlayerController>();
            if (_pc == null) return Zone.Outside;
        }
        Vector3 lp = transform.InverseTransformPoint(_pc.transform.position);
        bool inBox = lp.x >= boxMin.x && lp.x <= boxMax.x
                  && lp.y >= boxMin.y && lp.y <= boxMax.y
                  && lp.z >= boxMin.z && lp.z <= boxMax.z;
        if (!inBox) return Zone.Outside;
        return lp.z <= doorwayZ ? Zone.Deep : Zone.Doorway;
    }
}
