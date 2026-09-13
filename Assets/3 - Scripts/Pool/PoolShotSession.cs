using UnityEngine;

/// <summary>
/// The pool "F-mode": borrows the REAL camera (never a second camera — that
/// would lose the atmosphere/ocean post stack, CLAUDE.md trap #2), glides it
/// out of the helmet to a third-person view behind the cue ball, lets the
/// player aim with keys/stick, charge a shot on hold and strike on release,
/// and glides back on F. The astronaut stays standing where they were.
///
/// Camera recipe = SolarMap's: cache the head pose RELATIVE to its parent,
/// SetParent(null) + register with EndlessManager, disable CameraTransformFX
/// AND bail in its LateUpdate (the manager re-enables the component every
/// frame), re-derive the pose every LateUpdate from LIVE anchors (table
/// transform, head anchor) — never integrate — so orbit motion and floating-
/// origin rebases are invisible.
///
/// Every distance here is in TABLE-LOCAL metres and converted with
/// TransformPoint, so scaling the table scales the camera orbit with it.
/// </summary>
[DefaultExecutionOrder(210)]   // after EndlessManager (0) and CameraTransformFX (100)
public class PoolShotSession : MonoBehaviour
{
    public static PoolShotSession Active { get; private set; }
    public static bool IsActive => Active != null;

    public enum State { Closed, Entering, Aiming, Charging, Striking, Rolling, Exiting }

    [Header("Camera")]
    [Tooltip("Seconds for the glide helmet → behind the cue ball (and back).")]
    public float transitionSeconds = 0.9f;
    public float distance = 1.0f;
    public float minDistance = 0.6f;
    public float maxDistance = 1.6f;
    public float pitchDeg = 22f;
    public float minPitch = 8f;
    public float maxPitch = 60f;
    [Tooltip("Metres above the cue ball's centre the camera looks at.")]
    public float lookAbove = 0.03f;
    [Tooltip("How quickly the view re-centres on the cue ball after it has moved (1/s).")]
    public float retargetSharpness = 6f;

    [Header("Aim input")]
    public float yawRateDeg = 70f;
    public float pitchRateDeg = 45f;
    [Tooltip("A key starts at this fraction of full rate and ramps up over rampSeconds, so a tap nudges ~1°.")]
    public float rampStartFraction = 0.3f;
    public float rampSeconds = 0.25f;
    [Tooltip("Rate multiplier while Shift / LT is held.")]
    public float fineAimScale = 0.25f;
    public float zoomPerNotch = 0.12f;

    [Header("Cue + power")]
    public float chargeSeconds = 2f;
    [Tooltip("Metres the tip pulls back at full charge.")]
    public float maxPullBack = 0.22f;
    [Tooltip("Resting gap between the tip and the ball (an inch).")]
    public float tipGap = 0.025f;
    [Tooltip("Butt raised this many degrees above the cloth.")]
    public float cueLiftDeg = 4f;
    public float strikeSeconds = 0.07f;
    public float cueHideSeconds = 0.2f;
    public float minSpeed = 0.6f;
    public float maxSpeed = 9f;

    [Header("Guide")]
    public Color guideColor = new Color(1f, 0.77f, 0.42f, 0.45f);
    public float guideHeight = 0.004f;
    public float stubLength = 0.12f;

    // ── runtime ─────────────────────────────────────────────────────────────
    State _state = State.Closed;
    PoolTable _table;
    float _t;                      // 0 = in the head, 1 = at the shot pose
    float _yaw, _pitch, _dist;
    Vector3 _viewTarget;           // table-local, glides toward the cue ball
    float _turnHeld, _tiltHeld;
    float _charge, _chargeHeld;
    float _strikeT, _strikePullFrom;
    float _cueSlide;               // 0 = in play, 1 = slid away / hidden
    int _openFrame = -1;
    bool _cueVisible = true;

    // camera borrow
    PlayerController _pc;
    Camera _cam;
    Transform _camT, _origParent, _headAnchor;
    Vector3 _headLocalPos;
    Quaternion _headLocalRot;
    CameraTransformFX _camFx;
    bool _camFxWasEnabled, _hudWasHidden;
    EndlessManager _endless;
    Renderer[] _shownBody;
    PoolShotHUD _hud;
    string _hintKb, _hintPad;

    public State Current => _state;

    // ── open / close ────────────────────────────────────────────────────────

    public void Open(PoolTable table)
    {
        if (_state != State.Closed || Active != null || table == null) return;
        _table = table;
        if (!Setup()) { _table = null; return; }
        Active = this;
        _state = State.Entering;
        _t = 0f;
        _openFrame = Time.frameCount;
        _dist = Mathf.Clamp(distance, minDistance, maxDistance);
        _pitch = Mathf.Clamp(pitchDeg, minPitch, maxPitch);
        _viewTarget = _table.CueBallLocal;
        _charge = 0f; _chargeHeld = 0f; _cueSlide = 0f;
        // First shot after a rack faces the rack; otherwise arrive looking the way you walked up.
        if (_table.FreshRack) _yaw = Mathf.Atan2(-1f, 0f) * Mathf.Rad2Deg;
        else
        {
            Vector3 headLocal = _table.transform.InverseTransformPoint(_headAnchor.TransformPoint(_headLocalPos));
            Vector3 v = _viewTarget - headLocal; v.y = 0f;
            Vector3 back = v.sqrMagnitude > 1e-6f ? -v.normalized : Vector3.back;
            _yaw = Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg;
        }
        ApplyPose();
        if (_hud != null) { _hud.SetVisible(true); _hud.SetCharge(0f, false); }
    }

    public void Close()
    {
        if (_state == State.Closed || _state == State.Exiting) return;
        _state = State.Exiting;
        _charge = 0f;
        if (_hud != null) _hud.SetCharge(0f, false);
        SetGuideVisible(false);
    }

    void OnDisable() { if (_state != State.Closed) Teardown(true); }
    void OnDestroy() { if (_state != State.Closed) Teardown(true); }

    // ── per frame ───────────────────────────────────────────────────────────

    void Update()
    {
        if (_state == State.Closed) return;
        if (_camT == null || _pc == null || _table == null) { Teardown(true); return; }
        float dt = Time.deltaTime;
        bool menu = PauseState.MenuOpen;

        switch (_state)
        {
            case State.Entering:
                _t = Mathf.Min(1f, _t + dt / Mathf.Max(0.05f, transitionSeconds));
                if (_t >= 1f) { _state = State.Aiming; }
                if (!menu && LeavePressed()) Close();
                break;

            case State.Exiting:
                _t = Mathf.Max(0f, _t - dt / Mathf.Max(0.05f, transitionSeconds));
                if (_t <= 0f) { Teardown(false); return; }
                break;

            case State.Aiming:
            case State.Charging:
                if (menu) break;
                if (LeavePressed()) { Close(); break; }
                if (RerackPressed()) { _table.ReRack(); _viewTarget = _table.CueBallLocal; _charge = 0f; _state = State.Aiming; break; }
                TickAim(dt);
                TickCharge(dt);
                break;

            case State.Striking:
                _strikeT += dt;
                float k = Mathf.Clamp01(_strikeT / Mathf.Max(0.01f, strikeSeconds));
                _charge = Mathf.Lerp(_strikePullFrom, -tipGap / Mathf.Max(0.01f, maxPullBack), k * k);   // ease-in lunge to the ball
                if (k >= 1f)
                {
                    float speed = Mathf.Lerp(minSpeed, maxSpeed, _strikePullFrom * _strikePullFrom);
                    _table.Strike(AimDir2D(), speed);
                    _charge = 0f;
                    _state = State.Rolling;
                    SetGuideVisible(false);
                }
                if (!menu && LeavePressed()) Close();
                break;

            case State.Rolling:
                if (!menu && LeavePressed()) { Close(); break; }
                if (!menu && RerackPressed()) { _table.ReRack(); _viewTarget = _table.CueBallLocal; _cueSlide = 0f; _state = State.Aiming; break; }
                if (!menu) TickAim(dt);      // you can already swing the view while the balls roll
                if (_table.Sim.AllStopped && !_table.CueRespawnPending)
                {
                    _state = State.Aiming;
                    _cueSlide = 0f;
                }
                break;
        }
        // Pose the camera HERE too (Update, order 210 — after PlayerController's own
        // camera writes), not only in LateUpdate: the grass frustum cull, HelmetSway,
        // InteractGaze and friends read the camera before LateUpdate 210 and were
        // seeing a stale/wrong pose (grass beyond 15 m vanished, 2026-09-13).
        if (_state != State.Closed && _camT != null && _table != null) ApplyPose();
    }

    void LateUpdate()
    {
        if (_state == State.Closed || _camT == null || _table == null) return;
        float dt = Time.deltaTime;
        // The view glides to the cue ball except while balls are rolling (watch the shot from where you took it).
        if (_state != State.Rolling && _state != State.Striking)
        {
            Vector3 want = _table.CueBallLocal;
            _viewTarget = Vector3.Lerp(_viewTarget, want, 1f - Mathf.Exp(-retargetSharpness * dt));
        }
        ApplyPose();
        DriveCue(dt);
        DriveGuide();
    }

    // ── input ───────────────────────────────────────────────────────────────

    bool LeavePressed()
    {
        if (Time.frameCount == _openFrame) return false;      // the F that opened us
        return Input.GetKeyDown(KeyCode.F) || TutorialGate.PadPressed(TutorialGate.PadButton.X);
    }

    bool RerackPressed() => Input.GetKeyDown(KeyCode.R) || TutorialGate.PadPressed(TutorialGate.PadButton.Y);

    static bool FireHeld() => Input.GetMouseButton(0) || TutorialGate.RTValue() > 0.5f;
    static bool CancelPressed() => Input.GetMouseButtonDown(1) || TutorialGate.PadPressed(TutorialGate.PadButton.B);

    void TickAim(float dt)
    {
        float turn = 0f, tilt = 0f;
        if (Input.GetKey(KeyCode.D)) turn += 1f;
        if (Input.GetKey(KeyCode.A)) turn -= 1f;
        if (Input.GetKey(KeyCode.W)) tilt += 1f;
        if (Input.GetKey(KeyCode.S)) tilt -= 1f;
        Vector2 stick = TutorialGate.LeftStickRaw();
        if (Mathf.Abs(stick.x) > 0.01f) turn = stick.x;
        if (Mathf.Abs(stick.y) > 0.01f) tilt = stick.y;

        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || TutorialGate.LTValue() > 0.5f;
        float scale = fine ? fineAimScale : 1f;

        _turnHeld = turn != 0f ? _turnHeld + dt : 0f;
        _tiltHeld = tilt != 0f ? _tiltHeld + dt : 0f;
        float turnRamp = Mathf.Lerp(rampStartFraction, 1f, Mathf.Clamp01(_turnHeld / Mathf.Max(0.01f, rampSeconds)));
        float tiltRamp = Mathf.Lerp(rampStartFraction, 1f, Mathf.Clamp01(_tiltHeld / Mathf.Max(0.01f, rampSeconds)));

        _yaw += turn * yawRateDeg * turnRamp * scale * dt;
        _pitch = Mathf.Clamp(_pitch + tilt * pitchRateDeg * tiltRamp * scale * dt, minPitch, maxPitch);

        float wheel = Input.mouseScrollDelta.y;
        if (TutorialGate.DPadDirectionPressed(0)) wheel += 1f;
        if (TutorialGate.DPadDirectionPressed(2)) wheel -= 1f;
        if (wheel != 0f) _dist = Mathf.Clamp(_dist - wheel * zoomPerNotch, minDistance, maxDistance);
    }

    void TickCharge(float dt)
    {
        if (_state == State.Aiming)
        {
            if (FireHeld() && !_table.CueRespawnPending)
            {
                _state = State.Charging;
                _chargeHeld = 0f;
                _charge = 0f;
            }
            if (_hud != null) _hud.SetCharge(0f, false);
            return;
        }
        // Charging
        if (CancelPressed()) { _state = State.Aiming; _charge = 0f; if (_hud != null) _hud.SetCharge(0f, false); return; }
        _chargeHeld += dt;
        _charge = Mathf.Clamp01(_chargeHeld / Mathf.Max(0.05f, chargeSeconds));
        if (_hud != null) _hud.SetCharge(_charge, true);
        if (!FireHeld())
        {
            _strikePullFrom = _charge;
            _strikeT = 0f;
            _state = State.Striking;
            if (_hud != null) _hud.SetCharge(0f, false);
        }
    }

    // ── pose ────────────────────────────────────────────────────────────────

    // Camera sits at target + dir * dist; the shot goes AWAY from the camera.
    Vector3 OrbitDir()
    {
        float y = _yaw * Mathf.Deg2Rad, p = _pitch * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(p) * Mathf.Sin(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
    }

    Vector2 AimDir2D()
    {
        float y = _yaw * Mathf.Deg2Rad;
        return new Vector2(-Mathf.Sin(y), -Mathf.Cos(y));
    }

    void ShotPose(out Vector3 pos, out Quaternion rot)
    {
        Transform tt = _table.transform;
        Vector3 localPos = _viewTarget + OrbitDir() * _dist;
        Vector3 localLook = _viewTarget + Vector3.up * lookAbove;
        pos = tt.TransformPoint(localPos);
        Vector3 fwd = tt.TransformPoint(localLook) - pos;
        rot = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-8f ? fwd.normalized : tt.forward, tt.up);
    }

    void ApplyPose()
    {
        Vector3 headPos = _headAnchor.TransformPoint(_headLocalPos);
        Quaternion headRot = _headAnchor.rotation * _headLocalRot;
        ShotPose(out Vector3 shotPos, out Quaternion shotRot);
        float s = Mathf.SmoothStep(0f, 1f, _t);
        _camT.position = Vector3.Lerp(headPos, shotPos, s);
        _camT.rotation = Quaternion.Slerp(headRot, shotRot, s);
    }

    // ── cue stick ───────────────────────────────────────────────────────────

    void DriveCue(float dt)
    {
        var cue = _table.cue;
        if (cue == null) return;
        bool inPlay = _state == State.Aiming || _state == State.Charging || _state == State.Striking;
        float slideWant = inPlay ? 0f : 1f;
        _cueSlide = Mathf.MoveTowards(_cueSlide, slideWant, dt / Mathf.Max(0.02f, cueHideSeconds));
        bool visible = _cueSlide < 1f && _state != State.Closed && (_state != State.Entering || _t > 0.35f);
        SetCueVisible(visible);
        if (!visible) return;

        Vector2 a = AimDir2D();
        Vector3 aim3 = new Vector3(a.x, 0f, a.y);
        Vector3 back = -aim3;
        float lift = cueLiftDeg * Mathf.Deg2Rad;
        Vector3 buttDir = (back * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift)).normalized;
        float pull = _charge * maxPullBack + _cueSlide * 0.35f;
        Vector3 tip = _table.CueBallLocal + back * (_table.ballRadius + tipGap + pull);
        // The tip rides up with the lift so the shaft clears the cloth.
        cue.localPosition = tip;
        cue.localRotation = Quaternion.LookRotation(buttDir, Vector3.up);
    }

    void SetCueVisible(bool on)
    {
        if (_cueVisible == on) return;
        _cueVisible = on;
        var cue = _table != null ? _table.cue : null;
        if (cue != null && cue.gameObject.activeSelf != on) cue.gameObject.SetActive(on);
    }

    // ── aim guide ───────────────────────────────────────────────────────────

    void DriveGuide()
    {
        bool show = _state == State.Aiming || _state == State.Charging;
        SetGuideVisible(show);
        if (!show) return;
        var sim = _table.Sim;
        Vector2 a = AimDir2D();
        if (!sim.CastCueBall(a.x, a.y, out float cx, out float cy, out int hit, out float ox, out float oy)) { SetGuideVisible(false); return; }
        float h = _table.feltY + guideHeight;
        Vector3 from = new Vector3(sim.X[PoolPhysics2D.Cue], h, sim.Y[PoolPhysics2D.Cue]);
        Vector3 to = new Vector3(cx, h, cy);
        var line = _table.guideLine;
        if (line != null) { line.positionCount = 2; line.SetPosition(0, from); line.SetPosition(1, to); }
        var ring = _table.contactRing;
        if (ring != null)
        {
            const int N = 24;
            if (ring.positionCount != N) ring.positionCount = N;
            float r = _table.ballRadius;
            for (int i = 0; i < N; i++)
            {
                float ang = i / (float)N * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(cx + Mathf.Cos(ang) * r, h, cy + Mathf.Sin(ang) * r));
            }
        }
        var stub = _table.objectStub;
        if (stub != null)
        {
            if (hit >= 0)
            {
                stub.enabled = true;
                Vector3 s0 = new Vector3(sim.X[hit], h, sim.Y[hit]);
                stub.positionCount = 2;
                stub.SetPosition(0, s0);
                stub.SetPosition(1, s0 + new Vector3(ox, 0f, oy) * stubLength);
            }
            else stub.enabled = false;
        }
    }

    void SetGuideVisible(bool on)
    {
        if (_table == null) return;
        if (_table.guideLine != null) _table.guideLine.enabled = on;
        if (_table.contactRing != null) _table.contactRing.enabled = on;
        if (_table.objectStub != null && !on) _table.objectStub.enabled = false;
    }

    // ── setup / teardown (SolarMap recipe) ──────────────────────────────────

    bool Setup()
    {
        _pc = FindObjectOfType<PlayerController>();
        if (_pc == null) return false;
        var mgr = CameraEffectsManager.Instance;
        _cam = (mgr != null && mgr.PlayerCamera != null) ? mgr.PlayerCamera : _pc.Camera;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return false;
        _camT = _cam.transform;

        _origParent = _camT.parent;
        _headAnchor = _origParent != null ? _origParent : _pc.transform;
        _headLocalPos = _headAnchor.InverseTransformPoint(_camT.position);
        _headLocalRot = Quaternion.Inverse(_headAnchor.rotation) * _camT.rotation;

        _camFx = FindObjectOfType<CameraTransformFX>();
        _camFxWasEnabled = _camFx != null && _camFx.enabled;
        _hudWasHidden = HudVisibility.Hidden;

        _camT.SetParent(null, true);
        _endless = FindObjectOfType<EndlessManager>();
        if (_endless != null) _endless.RegisterPhysicsObject(_camT);
        if (_camFx != null) _camFx.enabled = false;
        HudVisibility.SetForceHidden(true);
        ShowAstronautBody();

        if (_hud == null) _hud = PoolShotHUD.Create(transform);
        if (_hintKb == null)
        {
            _hintKb = "A D turn   W S tilt   Shift fine   hold LMB power   R re-rack   F leave";
            _hintPad = "Stick aim   LT fine   hold RT power   Y re-rack   X leave";
        }
        _hud.SetHint(TutorialGate.LastSource == TutorialGate.InputSource.Controller ? _hintPad : _hintKb);
        return true;
    }

    void Teardown(bool abort)
    {
        _state = State.Closed;
        if (Active == this) Active = null;
        if (_endless != null && _camT != null) _endless.UnregisterPhysicsObject(_camT);
        if (_camT != null && _headAnchor != null)
        {
            _camT.SetParent(_origParent, true);
            _camT.position = _headAnchor.TransformPoint(_headLocalPos);
            _camT.rotation = _headAnchor.rotation * _headLocalRot;
        }
        if (_camFx != null) _camFx.enabled = _camFxWasEnabled;
        HudVisibility.SetForceHidden(_hudWasHidden);
        HideAstronautBody();
        if (_hud != null) { _hud.SetCharge(0f, false); _hud.SetVisible(false); }
        SetGuideVisible(false);
        _cueSlide = 1f;
        SetCueVisible(false);
        _charge = 0f;
        _pc = null; _cam = null; _camT = null; _headAnchor = null; _origParent = null; _camFx = null; _endless = null;
        _table = null;
        if (abort) Debug.LogWarning("[Pool] shot session closed abruptly — camera, player or table went away.");
    }

    void ShowAstronautBody()
    {
        _shownBody = null;
        if (_pc == null) return;
        Transform astro = _pc.transform.Find("Astronaut");
        if (astro == null) return;
        var on = new System.Collections.Generic.List<Renderer>();
        foreach (var r in astro.GetComponentsInChildren<Renderer>(true))
            if (r != null && !r.enabled) { r.enabled = true; on.Add(r); }
        _shownBody = on.ToArray();
    }

    void HideAstronautBody()
    {
        if (_shownBody != null) foreach (var r in _shownBody) if (r != null) r.enabled = false;
        _shownBody = null;
    }
}
