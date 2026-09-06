using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// THE MAP, v2 (2026-09-06, Sam's spec). Press M (or the phone's MAP app):
/// the REAL player camera lifts off your head, looks back down at where you
/// stand, and glides up until the whole solar system is framed top-down over
/// the sun. Orbit lines and planet name tags fade in. Fly around (same controls
/// as the old map: RMB-drag look, WASD, Space/Ctrl, Shift, Q/E roll), click a
/// planet to match its velocity (click empty space to unmatch), RECENTER (R)
/// snaps back to the top-down view. M or Esc reverses the whole flight back
/// into your head. The astronaut never moves: input is blocked, physics keeps
/// them planted on their planet, and their body is shown so you can watch
/// yourself shrink away.
///
/// ── Why it is seamless ────────────────────────────────────────────────
/// The camera is BORROWED, not duplicated (a second camera loses the
/// atmosphere/ocean post stack — trap #2). Every frame of the flight is
/// re-derived from two LIVE anchors: the head pose (the camera's original
/// parent + local pose, which rides the planet's orbit with the player) and
/// the top-down pose (sun + normal × height). At t = 0 the pose IS the head
/// pose, so re-parenting on arrival changes nothing on screen. Reversing
/// mid-flight just flips the direction of t.
///
/// ── Floating origin ───────────────────────────────────────────────────
/// The player keeps riding their planet (~85 m/s), so EndlessManager rebases
/// every ~12 s. The detached camera is registered with it (shifted in the same
/// step as the planets) AND its pose is always anchor-relative: during the
/// flight from the live head/sun poses, while open as an offset from the sun
/// or the followed body. A rebase is therefore invisible.
///
/// Auto-singleton (SpaceDustInventory pattern) + seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1). The old map
/// (SolarSystemMapController + friends) is vaulted: FeatureVault.LegacySolarMap.
/// </summary>
[DefaultExecutionOrder(210)]   // after EndlessManager (0) and CameraTransformFX (100)
public class SolarMap : MonoBehaviour
{
    public static SolarMap Instance { get; private set; }
    /// True from the first frame of lift-off until the camera is back in the head.
    public static bool IsOpen => Instance != null && Instance._state != State.Closed;
    /// True only while the top-down view is interactive (UI + fly controls live).
    public static bool IsInteractive => Instance != null && Instance._state == State.Open;
    public static bool ConsumedEscapeThisFrame => Instance != null && Instance._escFrame == Time.frameCount;

    public enum State { Closed, Opening, Open, Closing }

    [Header("Flight")]
    [Tooltip("Seconds for the lift-off to the top-down view.")]
    public float openDuration = 6.5f;
    [Tooltip("Seconds for the return flight into the head.")]
    public float closeDuration = 5.5f;
    [Tooltip("Time-warp exponent of the flight curve. >1 = the camera creeps out of the helmet and keeps accelerating the further it gets (Sam: 'slowly rise... gets faster and faster'); the return runs the same curve backwards, so it also settles gently into the head.")]
    public float accelPower = 1.8f;
    [Tooltip("Fraction of the flight time spent turning from the helmet view to face the planet's centre (the rest blends to the top-down view).")]
    public float turnDownFraction = 0.45f;
    [Tooltip("First bend of the flight path: rise this many planet radii (+ flat metres) straight up before curving toward the sun. Big on purpose so the path clears the planet whichever side you stand on.")]
    public float liftRadii = 6f;
    public float liftFlat = 6000f;
    [Tooltip("Top-down height fits the widest orbit × this.")]
    public float fitMargin = 1.12f;

    [Header("Open view")]
    public float uiFadeIn = 0.7f;
    public float uiFadeOut = 0.45f;
    public float glideDuration = 1.2f;
    [Tooltip("Fly-to framing: this many radii above a focused body (min 600 m).")]
    public float focusRadii = 6f;
    [Tooltip("While the map is open, LODHandler treats every planet as this many times taller on screen, so a body you fly up to gets its detailed mesh well before it fills half the screen. 1 = the game's normal switching.")]
    public float mapLodBias = 2.5f;
    [Tooltip("Mouse wheel: move toward/away from the anchor by this fraction of the distance per notch.")]
    public float scrollZoomFraction = 0.12f;
    public KeyCode toggleKey = KeyCode.M;
    public KeyCode recenterKey = KeyCode.R;
    public KeyCode cursorLockKey = KeyCode.G;

    // ── runtime ─────────────────────────────────────────────────────────────
    State _state = State.Closed;
    float _t;              // 0 = in the head, 1 = top-down
    int _escFrame = -1;

    PlayerController _pc;
    Camera _cam;
    Transform _camT;
    Transform _origParent;         // the camera's original parent (may be null)
    Transform _headAnchor;         // pose reference: the original parent, or the player if it had none
    Vector3 _headLocalPos;
    Quaternion _headLocalRot;
    CelestialBody _homeBody;       // planet the player stands on (nearest surface)
    CelestialBody _sun;
    Vector3 _viewDir;              // unit: from the sun toward the top-down camera spot
    Vector3 _viewUp;               // in-plane "north" for the top-down view
    float _topHeight;

    CameraTransformFX _camFx;
    EndlessManager _endless;
    MapCameraRig _rig;
    SolarMapOverlay _overlay;
    SolarMapOrbits _orbits;
    MapVelocityHud _toast;

    // open-view anchoring
    CelestialBody _anchor;         // sun, or the followed body (velocity match)
    CelestialBody _followed;
    Vector3 _offset;               // camera position relative to the anchor
    // The map owns the rotation while open. PlayerController still writes the
    // camera's pitch every Update/FixedUpdate (it doesn't know the camera left),
    // so we restore ours before the fly controls run and capture theirs after.
    Quaternion _openRot;
    float _nextDriftLog;
    bool _pendingClick;
    Vector3 _pendingClickPos;
    // Return flight: starts from wherever the camera is (anchor-relative so the
    // start point rides its body), not from the top-down spot.
    CelestialBody _closeAnchor;
    Vector3 _closeStartOffset;
    Quaternion _closeStartRot;
    float _closeTime;
    struct Glide { public bool active; public float t, duration; public Vector3 fromOffset, toOffset; public Quaternion fromRot, toRot; }
    Glide _glide;
    float _uiAlpha;
    bool _namesOn = true, _orbitsOn = true, _cursorLocked;

    // cached restore state
    bool _camFxWasEnabled;
    bool _hudWasHidden;
    CursorLockMode _cursorLockWas;
    bool _cursorVisibleWas;
    Renderer[] _shownBodyRenderers;
    readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

    // ── lifecycle ───────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;   // seeded by EnsureGameplaySingletons in builds
        var go = new GameObject("SolarMap");
        DontDestroyOnLoad(go);
        go.AddComponent<SolarMap>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── input ───────────────────────────────────────────────────────────────
    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;

        bool toggle = TutorialGate.GetKeyDown(toggleKey, TutorialAbility.Map) || TutorialGate.MapTogglePressed(TutorialAbility.Map);
        if (toggle)
        {
            if (_state == State.Closed) { if (Time.timeScale != 0f) Open(); }
            else Close();
            return;
        }
        if (_state == State.Closed) return;

        // Esc / pad B close the map and never reach the pause menu (TabbedPauseMenu checks ConsumedEscapeThisFrame).
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            _escFrame = Time.frameCount;
            if (_state != State.Closing) Close();
            return;
        }
        if (_state != State.Open) return;

        if (Input.GetKeyDown(recenterKey)) Recenter();
        if (Input.GetKeyDown(cursorLockKey)) SetCursorLocked(!_cursorLocked);

        // Click a PLANET (its real terrain) = match its velocity; click empty
        // space = unmatch. Name tags and legend rows arrive through the UI
        // (SetFollow / FocusAndFollow) and are skipped here as pointer-over-UI.
        // The raycast itself runs in LateUpdate AFTER the camera is re-pinned
        // to the map pose — during Update other scripts may have parked it
        // back on the helmet, and a ray from there hits nothing useful.
        if (Input.GetMouseButtonDown(0) && !_cursorLocked && _cam != null)
        {
            var es = EventSystem.current;
            if (es == null || !es.IsPointerOverGameObject()) { _pendingClick = true; _pendingClickPos = Input.mousePosition; }
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.001f && !_glide.active && _camT != null && _anchor != null)
        {
            // Zoom along the view direction by a fraction of the distance to the anchor.
            float dist = _offset.magnitude;
            _offset += _camT.forward * (scroll * scrollZoomFraction * dist);
            float minDist = _anchor.radius * 1.5f + 5f;
            if (_offset.magnitude < minDist) _offset = _offset.normalized * minDist;
        }
    }

    // ── public API (phone, HAL, legend, markers) ────────────────────────────
    public void Open()
    {
        if (_state != State.Closed) return;
        if (!Setup()) return;
        _state = State.Opening;
        _t = 0f;
        _uiAlpha = 0f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void Close()
    {
        if (_state == State.Closed || _state == State.Closing) return;
        // Start the return from the CURRENT pose. While open that is anchor +
        // offset (rides the followed body); mid-lift-off it is simply where the
        // camera is, expressed against the sun.
        if (_state == State.Open && _anchor != null)
        {
            _closeAnchor = _anchor;
            _closeStartOffset = _offset;
            _closeStartRot = _openRot;
        }
        else
        {
            _closeAnchor = _sun;
            _closeStartOffset = _camT.position - _sun.transform.position;
            _closeStartRot = _camT.rotation;
        }
        // Shorter trip when you are already near home; never below 30% of the full time.
        float dist = Vector3.Distance(_closeAnchor.transform.position + _closeStartOffset, _headAnchor.TransformPoint(_headLocalPos));
        _closeTime = closeDuration * Mathf.Clamp(dist / Mathf.Max(1f, _topHeight), 0.3f, 1f);
        _t = 1f;
        _state = State.Closing;
        _glide.active = false;
        SetCursorLocked(true);
        if (_overlay != null) _overlay.SetInteractive(false);
    }

    public void Toggle() { if (_state == State.Closed) Open(); else Close(); }

    /// Velocity match: the camera rides along with this body (the sun counts too).
    public void SetFollow(CelestialBody body)
    {
        if (body == null || _state != State.Open || _camT == null) return;
        if (_followed == body) return;
        _followed = body;
        SwitchAnchor(body);
        if (_toast != null) _toast.ShowMatched(body);
        if (_overlay != null) _overlay.SetFollowed(body);
    }

    /// Legend click: fly to the body AND ride with it, in one go.
    public void FocusAndFollow(CelestialBody body)
    {
        if (body == null || _state != State.Open) return;
        bool wasFollowing = _followed == body;
        FocusOn(body);
        if (!wasFollowing && _toast != null) _toast.ShowMatched(body);
    }

    public void Unfollow()
    {
        if (_followed == null) return;
        Debug.Log($"[SolarMap] unmatch from {_followed.bodyName}; cam {(_camT != null ? _camT.position : Vector3.zero)}");
        _followed = null;
        SwitchAnchor(_sun);
        if (_toast != null) _toast.ShowUnmatched();
        if (_overlay != null) _overlay.SetFollowed(null);
    }

    /// Fly to a body (frames it from above its orbit plane) and follow it. Opens the map first if needed (HAL).
    public void FocusOn(CelestialBody body)
    {
        if (body == null) return;
        if (_state == State.Closed) { Open(); _pendingFocus = body; return; }
        if (_state != State.Open) { _pendingFocus = body; return; }
        _followed = body;
        SwitchAnchor(body);
        float dist = Mathf.Max(600f, body.radius * focusRadii);
        Vector3 toOffset = _viewDir * dist;
        StartGlide(toOffset, Quaternion.LookRotation(-_viewDir, _viewUp));
        if (_overlay != null) _overlay.SetFollowed(body);
    }
    CelestialBody _pendingFocus;

    public void Recenter()
    {
        if (_state != State.Open) return;
        _followed = null;
        SwitchAnchor(_sun);
        if (_overlay != null) _overlay.SetFollowed(null);
        TopDownPose(out Vector3 pos, out Quaternion rot);
        StartGlide(pos - _sun.transform.position, rot);
    }

    public bool NamesOn => _namesOn;
    public bool OrbitsOn => _orbitsOn;
    public CelestialBody Followed => _followed;
    public CelestialBody HomeBody => _homeBody;
    public Camera ViewCamera => _cam;
    public float UiAlpha => _uiAlpha;

    public void ToggleNames() { _namesOn = !_namesOn; if (_overlay != null) _overlay.SetNamesVisible(_namesOn); }
    public void ToggleOrbits() { _orbitsOn = !_orbitsOn; if (_orbits != null) _orbits.Visible = _orbitsOn; }

    // ── per-frame pose ──────────────────────────────────────────────────────
    void LateUpdate()
    {
        if (_state == State.Closed) return;
        if (_camT == null || _pc == null || _sun == null || _headAnchor == null) { Teardown(true); return; }

        float dt = Time.unscaledDeltaTime;
        ReassertHidden();

        switch (_state)
        {
            case State.Opening:
            {
                _t = Mathf.Min(1f, _t + dt / Mathf.Max(0.1f, openDuration));
                TopDownPose(out Vector3 farPos, out Quaternion farRot);
                ApplyFlightPose(_t, farPos, farRot);
                if (_t >= 1f) Arrive();
                break;
            }

            case State.Closing:
            {
                _t = Mathf.Max(0f, _t - dt / Mathf.Max(0.1f, _closeTime));
                _uiAlpha = Mathf.Max(0f, _uiAlpha - dt / Mathf.Max(0.05f, uiFadeOut));
                PushAlpha();
                Vector3 farPos = (_closeAnchor != null ? _closeAnchor.transform.position : _sun.transform.position) + _closeStartOffset;
                ApplyFlightPose(_t, farPos, _closeStartRot);
                if (_t <= 0f) Teardown(false);
                break;
            }

            case State.Open:
                TickOpen(dt);
                break;
        }
    }

    void Arrive()
    {
        _state = State.Open;
        _t = 1f;
        _anchor = _sun;
        _followed = null;
        _offset = _camT.position - _sun.transform.position;
        _openRot = _camT.rotation;
        _glide.active = false;
        LODHandler.ScreenHeightBias = Mathf.Max(1f, mapLodBias);
        LODHandler.CameraOverride = _cam;
        if (_rig == null) _rig = _camT.gameObject.AddComponent<MapCameraRig>();
        _rig.Activate();
        SetCursorLocked(false);
        if (_overlay != null) { _overlay.SetInteractive(true); _overlay.SetFollowed(null); }
        if (_pendingFocus != null) { var b = _pendingFocus; _pendingFocus = null; FocusOn(b); }
    }

    void TickOpen(float dt)
    {
        // Fade the diagram in once we've arrived.
        _uiAlpha = Mathf.Min(1f, _uiAlpha + dt / Mathf.Max(0.05f, uiFadeIn));
        PushAlpha();

        // Camera = anchor + offset (origin-shift and orbit-motion proof).
        Vector3 anchorPos = _anchor != null ? _anchor.transform.position : _sun.transform.position;

        if (_glide.active)
        {
            _glide.t = Mathf.Min(1f, _glide.t + dt / Mathf.Max(0.05f, _glide.duration));
            float s = Smoother(_glide.t);
            _offset = Vector3.Lerp(_glide.fromOffset, _glide.toOffset, s);
            _camT.position = anchorPos + _offset;
            _camT.rotation = Quaternion.Slerp(_glide.fromRot, _glide.toRot, s);
            _openRot = _camT.rotation;
            _pendingClick = false;
            PublishViewPose();
            if (_glide.t >= 1f) { _glide.active = false; if (_rig != null) _rig.Activate(); }
            return;
        }

        Vector3 basePos = anchorPos + _offset;
        // Diagnostic (Sam, 2026-09-06: "unmatch teleports me back to Humble Abode"):
        // if the camera is not where our bookkeeping left it, something else moved
        // it between frames. Logged at most once a second; harmless.
        if ((_camT.position - basePos).sqrMagnitude > 100f * 100f && Time.unscaledTime > _nextDriftLog)
        {
            _nextDriftLog = Time.unscaledTime + 1f;
            float toHead = Vector3.Distance(_camT.position, _headAnchor.TransformPoint(_headLocalPos));
            Debug.Log($"[SolarMap] camera moved externally by {(_camT.position - basePos).magnitude:0} m since last frame (anchor {(_anchor != null ? _anchor.bodyName : "none")}, now {toHead:0} m from the helmet, parent {(_camT.parent != null ? _camT.parent.name : "none")}, CameraTransformFX enabled {(_camFx != null && _camFx.enabled)}); re-pinned.");
        }
        _camT.position = basePos;
        _camT.rotation = _openRot;              // undo PlayerController's per-frame pitch write
        ResolvePendingClick();
        if (_rig != null) _rig.Tick();          // free-fly controls move the transform
        _offset += _camT.position - basePos;    // fold ONLY the controls' movement back into the anchor-relative offset
        _openRot = _camT.rotation;
        PublishViewPose();
    }

    // Position + rotation along a flight for parameter t (0 = in the head,
    // 1 = the far pose), from LIVE anchors: the head pose rides the planet, and
    // the far pose is the top-down spot (opening) or wherever the camera was
    // when M/Esc was pressed (closing). Same curve both ways.
    void ApplyFlightPose(float t, Vector3 topPos, Quaternion topRot)
    {
        float s = FlightEase(t);
        Vector3 headPos = _headAnchor.TransformPoint(_headLocalPos);
        Quaternion headRot = _headAnchor.rotation * _headLocalRot;

        Vector3 up = _homeBody != null ? (headPos - _homeBody.transform.position).normalized : (headRot * Vector3.up);
        float planetR = _homeBody != null ? _homeBody.radius : 200f;
        Vector3 p0 = headPos;
        float lift = Mathf.Min(liftRadii * planetR + liftFlat, 0.6f * Vector3.Distance(headPos, topPos) + planetR);
        Vector3 p1 = headPos + up * lift;
        Vector3 p3 = topPos;
        Vector3 p2 = Vector3.Lerp(p3, p1, 0.35f);
        float u = 1f - s;
        Vector3 pos = u * u * u * p0 + 3f * u * u * s * p1 + 3f * u * s * s * p2 + s * s * s * p3;

        // Rotation (on TIME, not on the eased position, so the turn happens while
        // the camera is still creeping upward): helmet view → face the planet's
        // centre → top-down over the sun.
        Vector3 upHint = Vector3.Slerp(headRot * Vector3.up, topRot * Vector3.up, s);
        Vector3 centre = _homeBody != null ? _homeBody.transform.position : headPos - up * planetR;
        Vector3 toCentre = centre - pos;
        Quaternion faceDown = toCentre.sqrMagnitude > 1f ? Quaternion.LookRotation(toCentre.normalized, upHint) : headRot;
        float split = Mathf.Clamp(turnDownFraction, 0.1f, 0.9f);
        Quaternion rot = t < split
            ? Quaternion.Slerp(headRot, faceDown, Mathf.SmoothStep(0f, 1f, t / split))
            : Quaternion.Slerp(faceDown, topRot, Mathf.SmoothStep(0f, 1f, (t - split) / (1f - split)));

        _camT.position = pos;
        _camT.rotation = rot;
        PublishViewPose();
    }

    void TopDownPose(out Vector3 pos, out Quaternion rot)
    {
        pos = _sun.transform.position + _viewDir * _topHeight;
        rot = Quaternion.LookRotation(-_viewDir, _viewUp);
    }

    static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }

    // Cosine ease on TIME-WARPED t: starts near-stationary (first metres take a
    // second), accelerates most of the way, and eases out at the top-down spot.
    float FlightEase(float t)
    {
        t = Mathf.Clamp01(t);
        float w = Mathf.Pow(t, Mathf.Max(1f, accelPower));
        return 0.5f - 0.5f * Mathf.Cos(w * Mathf.PI);
    }

    void StartGlide(Vector3 toOffset, Quaternion toRot)
    {
        Vector3 anchorPos = _anchor != null ? _anchor.transform.position : _sun.transform.position;
        _glide.active = true;
        _glide.t = 0f;
        _glide.duration = glideDuration;
        _glide.fromOffset = _offset;
        _glide.fromRot = _openRot;
        _glide.toOffset = toOffset;
        _glide.toRot = toRot;
    }

    // Re-express the camera's position relative to a new anchor WITHOUT moving
    // it. Uses the bookkeeping (old anchor + offset), not the transform, so
    // nothing that touched the camera between frames can leak in.
    void SwitchAnchor(CelestialBody body)
    {
        if (body == null) body = _sun;
        Vector3 camWorld = _anchor != null ? _anchor.transform.position + _offset : _camT.position;
        _offset = camWorld - body.transform.position;
        _anchor = body;
        _glide.active = false;
        Debug.Log($"[SolarMap] anchor -> {body.bodyName}: cam {camWorld}, offset {_offset.magnitude:0} m");
    }

    void ResolvePendingClick()
    {
        if (!_pendingClick) return;
        _pendingClick = false;
        if (_cam == null) return;
        CelestialBody hitBody = null;
        Ray ray = _cam.ScreenPointToRay(_pendingClickPos);
        if (Physics.Raycast(ray, out RaycastHit hit, _cam.farClipPlane, ~0, QueryTriggerInteraction.Ignore))
            hitBody = hit.collider.GetComponentInParent<CelestialBody>();
        if (hitBody != null) SetFollow(hitBody);
        else if (_followed != null) Unfollow();
    }

    // Where the camera is really rendered from this frame — read by LODHandler,
    // whose Update may see the camera parked elsewhere by other scripts.
    void PublishViewPose()
    {
        LODHandler.HasViewPosOverride = true;
        LODHandler.ViewPosOverride = _camT.position;
    }

    void PushAlpha()
    {
        if (_overlay != null) _overlay.SetAlpha(_uiAlpha);
        if (_orbits != null) _orbits.Alpha = _uiAlpha;
    }

    // ── setup / teardown ────────────────────────────────────────────────────
    bool Setup()
    {
        _pc = FindObjectOfType<PlayerController>();
        if (_pc == null) return false;
        var mgr = CameraEffectsManager.Instance;
        _cam = (mgr != null && mgr.PlayerCamera != null) ? mgr.PlayerCamera : _pc.Camera;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return false;
        _camT = _cam.transform;

        var bodies = NBodySimulation.Bodies;
        _sun = null; _homeBody = null;
        float best = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) _sun = b;
            if (b.isStaticAttractor || b.bodyType == CelestialBody.BodyType.Sun) continue;
            float d = Vector3.Distance(_camT.position, b.transform.position) - b.radius;
            if (d < best) { best = d; _homeBody = b; }
        }
        if (_sun == null) { Debug.LogWarning("[SolarMap] no Sun in scene"); return false; }

        // Orbit plane from any railed planet (all orbits are coplanar). View from the
        // side AWAY from the black hole: it then sits BEHIND the sun from the camera,
        // visible and reachable, never between you and the map (Sam's call).
        Vector3 n = Vector3.forward;
        foreach (var b in bodies)
        {
            if (b == null || b.railPeriod <= 0f) continue;
            Vector3 rel = b.transform.position - _sun.transform.position;
            Vector3 c = Vector3.Cross(rel, b.velocity);
            if (c.sqrMagnitude > 1e-3f) { n = c.normalized; break; }
        }
        float side = 1f;
        foreach (var b in bodies)
            if (b != null && b.isStaticAttractor)
            {
                float dsgn = Vector3.Dot(n, b.transform.position - _sun.transform.position);
                if (Mathf.Abs(dsgn) > 1f) side = dsgn > 0f ? -1f : 1f;
                break;
            }
        _viewDir = n * side;
        _viewUp = Vector3.ProjectOnPlane(Vector3.up, _viewDir);
        if (_viewUp.sqrMagnitude < 1e-4f) _viewUp = Vector3.ProjectOnPlane(Vector3.right, _viewDir);
        _viewUp.Normalize();

        // Height that fits the widest orbit (vertical FOV is the limiting one on a landscape screen).
        float fit = 1000f;
        foreach (var b in bodies)
        {
            if (b == null || b.isStaticAttractor || b.bodyType == CelestialBody.BodyType.Sun) continue;
            float r = Vector3.ProjectOnPlane(b.transform.position - _sun.transform.position, _viewDir).magnitude + b.radius * 2f;
            if (r > fit) fit = r;
        }
        float halfTan = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        if (_cam.aspect < 1f) halfTan *= _cam.aspect;
        _topHeight = Mathf.Min(fit * fitMargin / Mathf.Max(0.05f, halfTan), _cam.farClipPlane * 0.6f);

        // Cache the head pose (relative to its parent so it rides the planet).
        _origParent = _camT.parent;
        _headAnchor = _origParent != null ? _origParent : _pc.transform;
        _headLocalPos = _headAnchor.InverseTransformPoint(_camT.position);
        _headLocalRot = Quaternion.Inverse(_headAnchor.rotation) * _camT.rotation;

        _camFx = FindObjectOfType<CameraTransformFX>();
        _camFxWasEnabled = _camFx != null && _camFx.enabled;
        _hudWasHidden = HudVisibility.Hidden;
        _cursorLockWas = Cursor.lockState;
        _cursorVisibleWas = Cursor.visible;

        _camT.SetParent(null, true);
        LODHandler.CameraOverride = _cam;
        _endless = FindObjectOfType<EndlessManager>();
        if (_endless != null) _endless.RegisterPhysicsObject(_camT);

        if (_camFx != null) _camFx.enabled = false;
        PlayerController.isMapOpen = true;     // blocks look/move/ship input; physics keeps the player planted
        HudVisibility.SetForceHidden(true);
        HideOtherCanvases();
        ShowAstronautBody();

        // Diagram + orbit lines + toast (built once, reused).
        if (_overlay == null) _overlay = new GameObject("SolarMapOverlay").AddComponent<SolarMapOverlay>();
        _overlay.transform.SetParent(transform, false);
        _overlay.Bind(this, bodies, _pc.transform);
        if (_orbits == null) _orbits = new GameObject("SolarMapOrbits").AddComponent<SolarMapOrbits>();
        _orbits.transform.SetParent(transform, false);
        _orbits.Bind(this, bodies, _sun);
        _orbits.Visible = _orbitsOn;
        _orbits.Alpha = 0f;
        if (_toast == null) { _toast = new GameObject("SolarMapToast").AddComponent<MapVelocityHud>(); _toast.transform.SetParent(transform, false); }
        _toast.SetVisible(true);
        _overlay.SetAlpha(0f);
        _overlay.SetNamesVisible(_namesOn);
        _overlay.SetInteractive(false);
        return true;
    }

    // Restores everything cached in Setup. `abort` = something vanished mid-flight.
    void Teardown(bool abort)
    {
        _state = State.Closed;
        _glide.active = false;
        _pendingFocus = null;
        LODHandler.ScreenHeightBias = 1f;
        LODHandler.CameraOverride = null;
        LODHandler.HasViewPosOverride = false;
        _pendingClick = false;

        if (_rig != null) { Destroy(_rig); _rig = null; }
        if (_endless != null && _camT != null) _endless.UnregisterPhysicsObject(_camT);

        if (_camT != null && _headAnchor != null)
        {
            // Back where it came from, at exactly the pose the flight ended on.
            _camT.SetParent(_origParent, true);
            _camT.position = _headAnchor.TransformPoint(_headLocalPos);
            _camT.rotation = _headAnchor.rotation * _headLocalRot;
        }

        if (_camFx != null) _camFx.enabled = _camFxWasEnabled;
        PlayerController.isMapOpen = false;
        HudVisibility.SetForceHidden(_hudWasHidden);
        RestoreCanvases();
        HideAstronautBody();
        Cursor.lockState = _cursorLockWas;
        Cursor.visible = _cursorVisibleWas;
        _cursorLocked = false;

        if (_overlay != null) { _overlay.SetAlpha(0f); _overlay.SetInteractive(false); }
        if (_orbits != null) { _orbits.Alpha = 0f; }
        if (_toast != null) _toast.SetVisible(false);
        _uiAlpha = 0f;
        _followed = null; _anchor = null;
        _pc = null; _cam = null; _camT = null; _headAnchor = null; _origParent = null; _homeBody = null; _camFx = null; _endless = null;
        if (abort) Debug.LogWarning("[SolarMap] closed abruptly — camera or player went away mid-flight.");
    }

    void SetCursorLocked(bool locked)
    {
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        if (_overlay != null) _overlay.SetCursorHint(locked);
    }

    // Every canvas that isn't ours (helmet frame, hotbar, phone…) goes dark for
    // the whole flight; the tutorial pill stays. Some HUDs re-enable themselves
    // every frame, so ReassertHidden repeats it while open.
    void HideOtherCanvases()
    {
        _hiddenCanvases.Clear();
        Canvas tutorialCanvas = TutorialUI.Instance != null ? TutorialUI.Instance.TutorialCanvas : null;
        foreach (var c in FindObjectsOfType<Canvas>(true))
        {
            if (c == null || !c.enabled) continue;
            if (c == tutorialCanvas) continue;
            if (c.transform.IsChildOf(transform)) continue;
            c.enabled = false;
            _hiddenCanvases.Add(c);
        }
    }

    void ReassertHidden()
    {
        for (int i = 0; i < _hiddenCanvases.Count; i++)
        {
            var c = _hiddenCanvases[i];
            if (c != null && c.enabled) c.enabled = false;
        }
    }

    void RestoreCanvases()
    {
        for (int i = 0; i < _hiddenCanvases.Count; i++)
            if (_hiddenCanvases[i] != null) _hiddenCanvases[i].enabled = true;
        _hiddenCanvases.Clear();
    }

    void ShowAstronautBody()
    {
        _shownBodyRenderers = null;
        if (_pc == null) return;
        Transform astro = _pc.transform.Find("Astronaut");
        if (astro == null) return;
        var on = new List<Renderer>();
        foreach (var r in astro.GetComponentsInChildren<Renderer>(true))
            if (r != null && !r.enabled) { r.enabled = true; on.Add(r); }
        _shownBodyRenderers = on.ToArray();
    }

    void HideAstronautBody()
    {
        if (_shownBodyRenderers != null)
            foreach (var r in _shownBodyRenderers) if (r != null) r.enabled = false;
        _shownBodyRenderers = null;
    }

}
