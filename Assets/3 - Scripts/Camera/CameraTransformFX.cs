using UnityEngine;

/// <summary>
/// Camera-transform effects: strafe roll and death tilt.
///
/// IMPORTANT — this module fights two physics-rotation issues:
///
/// 1. <b>50 Hz parent-rotation stutter.</b> PlayerController.HandleMovement
///    runs in FixedUpdate (50 Hz). It auto-aligns the player's transform.up
///    to planet gravity, sets the player's yaw (transform.Rotate), and sets
///    the camera's pitch (cam.localEulerAngles). The Rigidbody uses
///    Interpolate, which smooths POSITION to the render rate — but Unity
///    does NOT interpolate transform-driven rotation, so the camera's world
///    rotation visibly steps at 50 Hz while position slides smoothly. On a
///    high-refresh display this reads as "double-vision when strafing."
///
///    Fix: snapshot the player's rotation each FixedUpdate, then in
///    LateUpdate slerp between the two most recent snapshots using
///    `(Time.time - _lastFixedTime) / Time.fixedDeltaTime` as the factor —
///    the same trick Rigidbody.Interpolate does internally for position.
///    Set the camera's WORLD pose directly using that smoothed rotation;
///    Unity's parent-child math still works because we re-derive the local
///    pose from world each frame.
///
/// 2. <b>Z-roll compounding between fixed updates.</b> Multiplying the Z
///    roll onto cam.localRotation each LateUpdate compounded the roll on
///    frames where PC's FixedUpdate hadn't run since the previous LateUpdate
///    (i.e., whenever render fps > 50). The Z-roll appeared as 1×, then 2×,
///    then 3×, snapping back to 1× whenever the next FixedUpdate fired.
///    Visible as a "shimmery" or "doubled" roll during strafe.
///
///    Fix: stop compounding. Each LateUpdate, set the camera's intended
///    local rotation from scratch as `Euler(capturedPitch, 0, totalRoll)`,
///    where `capturedPitch` is grabbed in FixedUpdate (after PC writes) and
///    `totalRoll` is the sum of my strafe tilt + death tilt this frame.
///
/// Runs at DefaultExecutionOrder(100) so FixedUpdate runs after
/// PlayerController's FixedUpdate (default order 0), guaranteeing we read
/// freshly-written pitch and rotation snapshots.
/// </summary>
[DefaultExecutionOrder(100)]
public class CameraTransformFX : MonoBehaviour
{
    PlayerController _player;
    Transform _playerTransform;
    Transform _cam;
    Vector3 _camBaseLocalPos;
    bool _cached;

    // ── Manual interpolation snapshots ─────────────────────────────
    Quaternion _prevPlayerRot = Quaternion.identity;
    Quaternion _currPlayerRot = Quaternion.identity;
    float _lastFixedTime = -1f;
    float _capturedPitch;
    float _prevAppliedYaw, _currAppliedYaw;
    // Space free-float: same remainder trick for the body pitch / roll.
    float _prevAppliedPitch, _currAppliedPitch, _prevAppliedRoll, _currAppliedRoll;

    // ── Effect state ───────────────────────────────────────────────
    float _tiltZ;
    float _deathTiltT;
    bool _isDying;

    // ── Third-person view (experiment, 2026-09-15) ─────────────────
    // V cycles first person → close → far → first person. The camera orbits
    // the HEAD (the first-person eye point) by the same look rotation the
    // first-person camera uses, so mouse look still turns the body and
    // pitch swings the camera over / under the astronaut. The offset is
    // blended, not snapped, so the hops read as a pull-back. Held items
    // hang under the camera and ride along with it — known, accepted for
    // the experiment.
    public static int ViewMode { get; private set; }          // 0 = first person, 1 = close, 2 = far
    public static bool ThirdPerson => ViewMode != 0;
    [Tooltip("Third-person offset from the head, in look space: x right, y up, z back (negative = behind).")]
    public Vector3 closeOffset = new Vector3(0f, 0.6f, -2.5f);
    public Vector3 farOffset   = new Vector3(0f, 1.5f, -6f);
    [Tooltip("Metres ahead of the head the third-person camera aims at. Larger = flatter view; 0 = stare at the head.")]
    public float lookAhead = 4f;
    [Tooltip("How fast the offset blends between modes (1/s). ~12 = quarter-second hop.")]
    public float viewBlendSpeed = 12f;
    [Tooltip("Sphere radius used to keep the third-person camera out of the planet / ship.")]
    public float collisionRadius = 0.3f;
    Vector3 _viewOffset;          // current (blended) look-space offset; zero = first person
    LayerMask _collisionMask;

    // ── First-person eye ───────────────────────────────────────────
    // Everything that used to hang under the Camera (CameraHoldPos and the
    // held-item rigs, the torch, the viewmodel fill light) lives under this
    // transform instead. It is posed every LateUpdate at the FIRST-PERSON
    // camera pose — in first person that is exactly where the camera is, so
    // nothing changes; in third person the camera swings out behind the
    // astronaut while the eye (and so the hands) stay on the head. Scripts
    // that need "the camera the hold point hangs under" call ViewFrameOf and
    // get this instead.
    public static Transform Eye { get; private set; }
    /// <summary>Metres from the rendering camera back to the eye (0 in first
    /// person). Add to any reach measured from the camera so third person
    /// doesn't shorten the player's arms.</summary>
    public static float CameraToEyeDistance { get; private set; }

    /// <summary>The frame a held-item transform should treat as "the camera":
    /// the eye if it hangs under one, else the first Camera up the chain, else
    /// Camera.main. Moves the camera's children onto the eye on first contact.</summary>
    public static Transform ViewFrameOf(Transform from)
    {
        for (Transform t = from; t != null; t = t.parent)
        {
            if (Eye != null && t == Eye) return Eye;
            if (t.GetComponent<Camera>() != null)
            {
                var eye = EnsureEye(t);
                return eye != null ? eye : t;
            }
        }
        if (Eye != null) return Eye;
        return Camera.main != null ? Camera.main.transform : null;
    }

    /// <summary>Create the eye under the player (sibling of the camera) and move
    /// the camera's non-camera, non-UI children onto it. Only acts on a camera
    /// parented directly to the PlayerController; returns null otherwise (the
    /// ship's cockpit camera, a borrowed camera).</summary>
    static Transform EnsureEye(Transform cam)
    {
        if (cam == null) return Eye;
        Transform playerRoot = cam.parent;
        if (playerRoot == null || playerRoot.GetComponent<PlayerController>() == null) return Eye;
        if (Eye == null)
        {
            var go = new GameObject("FirstPersonEye");
            go.transform.SetParent(playerRoot, false);
            go.transform.localPosition = cam.localPosition;
            go.transform.localRotation = cam.localRotation;
            Eye = go.transform;
        }
        for (int i = cam.childCount - 1; i >= 0; i--)
        {
            Transform c = cam.GetChild(i);
            if (c.GetComponent<Camera>() != null || c.GetComponent<Canvas>() != null) continue;
            c.SetParent(Eye, true);
        }
        return Eye;
    }

    void Update()
    {
        // V toggle. Read in Update so a tap is never missed at low physics rates.
        // Gated like other on-foot keys: no menu / typing / phone, and only while
        // the player is the one holding the camera (piloting the ship uses V for
        // match-velocity and the player is inactive then anyway).
        if (!Input.GetKeyDown(KeyCode.V)) return;
        if (TutorialGate.MovementInputSuppressed || PlayerController.isInDialogue) return;
        if (_playerTransform == null || !_playerTransform.gameObject.activeInHierarchy) return;
        if (SolarMap.IsOpen || PoolShotSession.IsActive) return;
        ViewMode = (ViewMode + 1) % 3;
    }

    void FixedUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null) return;
        if (!CacheRefs(mgr)) return;

        // Snapshot player rotation for slerp in LateUpdate.
        _prevPlayerRot = _currPlayerRot;
        _currPlayerRot = _playerTransform.rotation;
        _lastFixedTime = Time.fixedTime;

        // ...and the yaw those snapshots correspond to, so LateUpdate can tell
        // how much look input the transform hasn't consumed yet.
        _prevAppliedYaw = _currAppliedYaw;
        _currAppliedYaw = _player != null ? _player.SmoothYaw : _currAppliedYaw;
        _prevAppliedPitch = _currAppliedPitch;
        _prevAppliedRoll  = _currAppliedRoll;
        if (_player != null) { _currAppliedPitch = _player.SmoothFreePitch; _currAppliedRoll = _player.SmoothFreeRoll; }

        // Capture PC's freshly-written pitch. PC writes
        // `cam.localEulerAngles = Vector3.right * smoothPitch` in its own
        // FixedUpdate (HandleMovement). Our DefaultExecutionOrder(100) runs
        // after, so this read picks up PC's value.
        _capturedPitch = _cam.localEulerAngles.x;
    }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null) return;
        // Third person must keep composing the pose even with camera effects
        // switched off in the pause menu (the effects themselves stay zeroed
        // below); otherwise the V key would do nothing for that player.
        bool fx = mgr.MasterEnabled;
        bool thirdPersonLive = ThirdPerson || _viewOffset.sqrMagnitude > 1e-6f;
        if (!fx && !thirdPersonLive) { ResetIfCached(); WeldEyeToCamera(); return; }
        var input = mgr.Input;
        if (input == null) return;
        if (!CacheRefs(mgr)) return;
        // While the player is disabled (e.g. piloting a ship — Ship.PilotShip
        // reparents the camera to camViewPoint and SetActive(false)s the
        // player), this module would otherwise drag the camera back onto the
        // inactive player transform every LateUpdate and the cockpit view
        // would snap to wherever the player was standing. Bail out so the
        // ship can own the camera.
        if (_playerTransform == null || !_playerTransform.gameObject.activeInHierarchy) return;
        // The solar map BORROWS the real camera and flies it across the system
        // (SolarMap, 2026-09-06). Disabling this component is not enough: the
        // manager re-gates .enabled from the fx flag every frame, so this
        // LateUpdate (order 100) kept snapping the camera back onto the head
        // and SolarMap (order 210) re-pinned it — visually fine, but everything
        // that read the camera in between (LODHandler, click raycasts) saw the
        // helmet pose. Bail without resetting; SolarMap restores the head pose.
        if (SolarMap.IsOpen) return;
        // Same borrow, same bail: the pool table's shot camera (PoolShotSession, order 210).
        if (PoolShotSession.IsActive) return;

        float dt = Time.deltaTime;

        // ── Strafe tilt — Z roll proportional to horizontal input.
        //    Suppressed during the groggy wake-up intro (no woozy roll while the
        //    player takes their first half-speed steps).
        if (fx && input.fxStrafeTilt && !IntroSequenceController.SuppressGroggyCameraFx)
        {
            // MovementInputSuppressed covers typing/modal/phone/focused-menu;
            // MoveAxisHorizontal excludes the D-pad (the legacy axis didn't).
            float h = TutorialGate.MovementInputSuppressed ? 0f : TutorialGate.MoveAxisHorizontal(TutorialAbility.Move);
            float target = -h * 4f;
            _tiltZ = Mathf.Lerp(_tiltZ, target, 1f - Mathf.Exp(-dt * 5f));
        }
        else _tiltZ = Mathf.Lerp(_tiltZ, 0f, 1f - Mathf.Exp(-dt * 5f));

        // ── Death tilt: the view tips ~90° as the player collapses, slowed to ~1.5s so
        //    it reads as a fall-over during the death cutscene's lead-in.
        float deathRoll = 0f;
        if (fx && input.fxDeathTilt && _isDying)
        {
            _deathTiltT = Mathf.MoveTowards(_deathTiltT, 1f, dt / 1.5f);
            deathRoll = Mathf.Lerp(0f, -90f, EaseOutCubic(_deathTiltT)); // negative = fall to the RIGHT
        }
        else _deathTiltT = 0f;

        // ── Smooth player rotation manually (Unity doesn't interpolate
        //    transform-driven rotation; we replicate Rigidbody.Interpolate's
        //    behavior for the parent's rotation).
        float interpT = _lastFixedTime > 0f
            ? Mathf.Clamp01((Time.time - _lastFixedTime) / Time.fixedDeltaTime)
            : 1f;
        Quaternion smoothPlayerRot = Quaternion.Slerp(_prevPlayerRot, _currPlayerRot, interpT);

        // ── Yaw the transform hasn't caught up to yet, added on at render rate.
        //    Interpolating between two physics snapshots is smooth but always
        //    LAGS by up to a full fixed step — and a fixed step is 20 ms of real
        //    time at timeScale 1 but ~133 ms during a 0.15x kill slow-mo, which
        //    is what made looking around mid-slow-mo feel like it was running at
        //    single-digit fps. The slerp above lands on the pose matching
        //    `appliedYaw`; the remainder up to the LIVE (unscaled, per-frame)
        //    smoothYaw is applied here, so the view answers the mouse on the
        //    frame the input arrives no matter what timeScale is doing.
        if (_player != null)
        {
            float appliedYaw = Mathf.LerpAngle(_prevAppliedYaw, _currAppliedYaw, interpT);
            float pendingYaw = Mathf.DeltaAngle(appliedYaw, _player.SmoothYaw);
            // Right-multiply = rotate about the player's LOCAL up, matching
            // HandleMovement's transform.Rotate(..., Space.Self).
            smoothPlayerRot *= Quaternion.AngleAxis(pendingYaw, Vector3.up);
            // Free-float body pitch / roll not yet consumed by the transform
            // (zero unless PlayerController.FreeFloating — the targets only
            // move while free). Order + sign mirror HandleMovement.
            float pendingPitch = Mathf.DeltaAngle(Mathf.LerpAngle(_prevAppliedPitch, _currAppliedPitch, interpT), _player.SmoothFreePitch);
            float pendingRoll  = Mathf.DeltaAngle(Mathf.LerpAngle(_prevAppliedRoll,  _currAppliedRoll,  interpT), _player.SmoothFreeRoll);
            smoothPlayerRot *= Quaternion.AngleAxis(pendingPitch, Vector3.right) * Quaternion.AngleAxis(-pendingRoll, Vector3.forward);
        }

        // ── Compose final camera world pose. Setting world pose (rather
        //    than local) overrides the parent-inheritance chain at the
        //    render frame, so the camera sees the smoothed rotation even
        //    though the player transform itself snaps at 50 Hz.
        //    Pitch comes from the LIVE value for the same reason as yaw — the
        //    old _capturedPitch was a FixedUpdate snapshot, so vertical look
        //    froze into ~7 Hz steps for the whole slow-mo.
        float livePitch = _player != null ? _player.SmoothPitch : _capturedPitch;
        // Swimming v2 (2026-09-08): stroke-rhythm roll + surface bob, computed
        // by PlayerController (render rate, eased). Zero out of water.
        float swimRoll = _player != null ? _player.SwimCameraRoll : 0f;
        Vector3 swimBob = _player != null ? Vector3.up * _player.SwimCameraBob : Vector3.zero;
        Quaternion camLocalRot = Quaternion.Euler(livePitch, 0f, _tiltZ + deathRoll + swimRoll);
        Quaternion lookRot = smoothPlayerRot * camLocalRot;

        // Player position is already interpolated by Unity (Rigidbody.Interpolate);
        // reading transform.position returns the smoothed visual value.
        Vector3 desiredCamPos = _playerTransform.position + smoothPlayerRot * (_camBaseLocalPos + swimBob);

        // ── Third person: orbit the head by the look rotation, pull in on
        //    collision, aim a little ahead of the head so the astronaut sits
        //    low in frame and the view looks slightly down at them.
        Vector3 targetOffset = ViewMode == 1 ? closeOffset : ViewMode == 2 ? farOffset : Vector3.zero;
        _viewOffset = Vector3.Lerp(_viewOffset, targetOffset, 1f - Mathf.Exp(-dt * viewBlendSpeed));
        if (_viewOffset.sqrMagnitude > 1e-6f)
        {
            Vector3 head = desiredCamPos;
            Vector3 toCam = lookRot * _viewOffset;
            float dist = toCam.magnitude;
            Vector3 dir = toCam / dist;
            if (Physics.SphereCast(head, collisionRadius, dir, out RaycastHit hit, dist, _collisionMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(hit.distance, 0.05f);
            desiredCamPos = head + dir * dist;
            Vector3 aim = head + lookRot * Vector3.forward * lookAhead;
            Vector3 aimDir = aim - desiredCamPos;
            if (aimDir.sqrMagnitude > 1e-6f) lookRot = Quaternion.LookRotation(aimDir, lookRot * Vector3.up);
        }
        else if (!ThirdPerson) _viewOffset = Vector3.zero;   // settle exactly onto the first-person pose

        _cam.rotation = lookRot;
        _cam.position = desiredCamPos;

        // The eye stays on the head at the first-person pose whatever the
        // camera is doing. Sweep any child something parented to the camera
        // since last frame onto it (a one-int check when there are none).
        if (Eye == null || _cam.childCount > 0) EnsureEye(_cam);
        if (Eye != null)
        {
            Vector3 eyePos = _playerTransform.position + smoothPlayerRot * (_camBaseLocalPos + swimBob);
            Eye.SetPositionAndRotation(eyePos, smoothPlayerRot * camLocalRot);
            CameraToEyeDistance = (desiredCamPos - eyePos).magnitude;
        }
        else CameraToEyeDistance = 0f;
    }

    void WeldEyeToCamera()
    {
        if (Eye == null || _cam == null) return;
        Eye.SetPositionAndRotation(_cam.position, _cam.rotation);
        CameraToEyeDistance = 0f;
    }

    public void TriggerDeathTilt() { _isDying = true; }
    public void ClearDeathTilt()   { _isDying = false; }

    /// Called when the player teleports (e.g. exiting the pilot seat onto
    /// the ship's pilotSeatPoint). The interpolation buffer would
    /// otherwise slerp from the player's pre-teleport rotation into the
    /// new one for ~one frame, causing a brief camera judder. Snap
    /// _prevPlayerRot and _currPlayerRot to the current value so the
    /// next LateUpdate Slerp returns the new rotation immediately.
    public void SnapToCurrentPlayer()
    {
        if (_playerTransform == null) return;
        _currPlayerRot = _playerTransform.rotation;
        _prevPlayerRot = _currPlayerRot;
        _lastFixedTime = Time.time;
        if (_player != null)
        {
            _prevAppliedYaw   = _currAppliedYaw   = _player.SmoothYaw;
            _prevAppliedPitch = _currAppliedPitch = _player.SmoothFreePitch;
            _prevAppliedRoll  = _currAppliedRoll  = _player.SmoothFreeRoll;
        }
    }

    static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    bool CacheRefs(CameraEffectsManager mgr)
    {
        if (_cached && _player != null && _cam != null) return true;
        if (_player == null)
        {
            _player = FindObjectOfType<PlayerController>(true);
            if (_player == null) return false;
            _playerTransform = _player.transform;
        }
        if (_cam == null) _cam = mgr.PlayerCamera != null ? mgr.PlayerCamera.transform : null;
        if (_cam == null) return false;
        if (!_cached)
        {
            // Base from the PLAYER'S canonical value, not a snapshot of
            // cam.localPosition: the snapshot raced the intro/menu-background
            // flows and could capture a cinematic-displaced pose — composing
            // every later frame from it left the camera at the astronaut's
            // kneecaps. Zero means PlayerController.Start hasn't run yet
            // (inactive player on some load paths) — try again next frame.
            Vector3 basePos = _player.CameraBaseLocalPos;
            if (basePos == Vector3.zero) return false;
            _camBaseLocalPos = basePos;
            // Third-person camera collides with what the player can stand on
            // (planet + ship) — never the astronaut's own collider or props.
            _collisionMask = _player.walkableMask;
            // Seed rotation snapshots so the first slerp has sane endpoints.
            _currPlayerRot = _playerTransform.rotation;
            _prevPlayerRot = _currPlayerRot;
            _capturedPitch = _cam.localEulerAngles.x;
            _cached = true;
        }
        return true;
    }

    void ResetIfCached()
    {
        if (!_cached || _cam == null) return;
        _cam.localPosition = _camBaseLocalPos;
        _cam.localRotation = Quaternion.Euler(_capturedPitch, 0f, 0f);
    }
}
