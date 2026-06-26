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

    // ── Effect state ───────────────────────────────────────────────
    float _tiltZ;
    float _deathTiltT;
    bool _isDying;

    void FixedUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null) return;
        if (!CacheRefs(mgr)) return;

        // Snapshot player rotation for slerp in LateUpdate.
        _prevPlayerRot = _currPlayerRot;
        _currPlayerRot = _playerTransform.rotation;
        _lastFixedTime = Time.fixedTime;

        // Capture PC's freshly-written pitch. PC writes
        // `cam.localEulerAngles = Vector3.right * smoothPitch` in its own
        // FixedUpdate (HandleMovement). Our DefaultExecutionOrder(100) runs
        // after, so this read picks up PC's value.
        _capturedPitch = _cam.localEulerAngles.x;
    }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) { ResetIfCached(); return; }
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

        float dt = Time.deltaTime;

        // ── Strafe tilt — Z roll proportional to horizontal input.
        //    Suppressed during the groggy wake-up intro (no woozy roll while the
        //    player takes their first half-speed steps).
        if (input.fxStrafeTilt && !IntroSequenceController.SuppressGroggyCameraFx)
        {
            float h = (AIChatScreen.IsTypingActive || PlayerController.isInModalSlotUI) ? 0f : UnityEngine.Input.GetAxisRaw("Horizontal");
            float target = -h * 4f;
            _tiltZ = Mathf.Lerp(_tiltZ, target, 1f - Mathf.Exp(-dt * 5f));
        }
        else _tiltZ = Mathf.Lerp(_tiltZ, 0f, 1f - Mathf.Exp(-dt * 5f));

        // ── Death tilt: the view tips ~90° as the player collapses, slowed to ~1.5s so
        //    it reads as a fall-over during the death cutscene's lead-in.
        float deathRoll = 0f;
        if (input.fxDeathTilt && _isDying)
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

        // ── Compose final camera world pose. Setting world pose (rather
        //    than local) overrides the parent-inheritance chain at the
        //    render frame, so the camera sees the smoothed rotation even
        //    though the player transform itself snaps at 50 Hz.
        Quaternion camLocalRot = Quaternion.Euler(_capturedPitch, 0f, _tiltZ + deathRoll);
        _cam.rotation = smoothPlayerRot * camLocalRot;

        // Player position is already interpolated by Unity (Rigidbody.Interpolate);
        // reading transform.position returns the smoothed visual value.
        Vector3 desiredCamPos = _playerTransform.position + smoothPlayerRot * _camBaseLocalPos;

        _cam.position = desiredCamPos;
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
            _camBaseLocalPos = _cam.localPosition;
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
