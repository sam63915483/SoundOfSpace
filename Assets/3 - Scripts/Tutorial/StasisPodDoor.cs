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
    /// This is the ONLY open that arms a save: PressArmed = "a person pressed
    /// the valve to get in", and only such an entry may seal + upload.
    public void OpenForSeconds(float openSeconds)
    {
        SetTarget(1f);
        _closeAt = Time.time + openSeconds;
        _pressArmed = true;
    }

    /// Ritual / wake exit point: open and stay open until the occupant has
    /// LEFT — the "stepped fully out" edge closes it. Never times out on
    /// someone still inside (Sam, 2026-09-08: "when the door opens it
    /// shouldn't close until you leave the stasis pod, nor make a save if it
    /// closes with you inside"). Clears the press arming: being let out is
    /// not a request to be sealed in again.
    public void OpenHold()
    {
        SetTarget(1f);
        _closeAt = -1f;
        _pressArmed = false;
    }

    /// True from a valve press until the door is next opened by the game
    /// (OpenHold) or times out with nobody inside. StasisPodSave requires it
    /// for an UPLOAD: sealed-inside-without-a-press is only ever a wake.
    bool _pressArmed;
    public bool PressArmed => _pressArmed;

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

        // Entered the pod proper THROUGH A VALVE-OPENED DOOR → it seals behind
        // you (save ritual). Walking into a door the game held open for you
        // (after a wake / load) seals nothing — press the valve to save.
        if (eff == Zone.Deep && _prevEffective != Zone.Deep && IsOpen && _pressArmed)
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
        // So don't rely on the edge. If the door is open and no close is
        // pending, schedule one. This also cleans up after OpenHold(), which
        // deliberately sets no timer.
        //
        // ⚠️ The condition is `eff != Zone.Doorway`, NOT `eff == Zone.Outside`.
        //
        // Outside-only was the 2026-08-08 co-op bug: a guest WAKES UP inside the
        // pod, so the effective zone is already Deep before the door ever opens.
        // The "sealed behind you" edge at the top needs a transition INTO Deep
        // and there isn't one — they were already there — and OpenHold leaves
        // _closeAt at -1. With the net only covering Outside, nothing scheduled
        // a close until the guest had fully walked away, which is exactly what
        // Sam saw: the door hanging open the entire time they were near the pod.
        //
        // Covering Deep as well means an open door always gets a pending close.
        // Doorway stays excluded so the leaf never lands on someone standing in
        // it, and the execute step below re-checks that at close time anyway.
        //
        // ⚠️ REVISED 2026-09-08 (Sam): an OCCUPIED pod that the game opened
        // (OpenHold after a wake or a load) must NOT time out — the door stays
        // open until the occupant steps out, and the "stepped fully out" edge
        // above closes it. The 5 s Deep grace used to seal a player who stood
        // still after waking, then replayed the DOWNLOADING overlay over them
        // (a fake save on a new game, and on every load). So Deep is covered
        // by the net ONLY when the door was opened by a valve press
        // (_pressArmed): that occupant asked to be sealed in, and
        // DeepExitCloseDelay is just the backstop for a missed Deep edge.
        // The 2026-08-08 co-op case (guest wakes Deep, no transition into
        // Deep) is still handled — by the Outside edge when they walk out.
        if (IsOpen && _closeAt <= 0f && eff != Zone.Doorway && (eff != Zone.Deep || _pressArmed))
            _closeAt = Time.time + (eff == Zone.Deep ? DeepExitCloseDelay : autoCloseDelay);

        // Execute a due close — but never while ANY player straddles the doorway
        // plane, on either machine (the leaf would land on them), and never on
        // an occupant who did not press the valve (a stale timer from before
        // they stepped in must not seal them).
        if (IsOpen && _closeAt > 0f && Time.time >= _closeAt && eff != Zone.Doorway
            && (eff != Zone.Deep || _pressArmed))
        {
            _closeAt = -1f;
            SetTarget(0f);
            // Valve press that nobody used (timed out with the pod empty): disarm,
            // so a later walk-in through a game-opened door can't inherit it.
            if (eff != Zone.Deep) _pressArmed = false;
        }

        AnimateToTarget();
    }

    Zone _prevEffective = Zone.Outside;

    /// Grace period for an open door with someone still INSIDE the pod — long
    /// enough to walk out from a standstill. A const rather than a serialized
    /// field so it cannot be inserted mid-class and shift the existing
    /// serialization (CLAUDE.md convention). Kept equal to StasisValveButton's
    /// default openSeconds, deliberately: both mean "you've been let out".
    const float DeepExitCloseDelay = 5f;

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
