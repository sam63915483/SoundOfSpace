using UnityEngine;

/// <summary>
/// FOV stacking system. Sprint, jetpack, and ship-boost states each
/// contribute a deltaFOV each frame they're active; the module sums them
/// and smooth-damps the camera's fieldOfView from baseFOV → baseFOV + sum.
/// </summary>
public class CameraFOVFX : MonoBehaviour
{
    PlayerController _player;
    Ship _ship;
    Camera _cam;
    float _baseFOV;
    float _currentDelta;
    float _deltaVelocity;
    bool _cached;

    const float SprintThreshold = 4.5f;
    const float SprintFOVDelta = 6f;
    const float JetpackFOVDelta = 5f;
    const float ShipBoostFOVDelta = 8f;

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) { ResetIfCached(); return; }
        var input = mgr.Input;
        if (input == null) return;
        if (!CacheRefs(mgr)) return;

        float targetDelta = 0f;

        // Sprint kick — gated on WASD input being held strongly, not on
        // rb.velocity (which is contaminated by the planet's orbital motion
        // through world space — rb.velocity is huge even when standing still).
        // Suppressed during the groggy wake-up intro (no FOV kick on the first
        // half-speed WASD/sprint steps).
        if (input.fxSprintFovKick && !IntroSequenceController.SuppressGroggyCameraFx)
        {
            // Treat WASD as not-pressed while the AI chat input field has
            // focus so typing doesn't pump the sprint-FOV kick.
            bool typing = AIChatScreen.IsTypingActive || PlayerController.isInModalSlotUI;
            float h = typing ? 0f : UnityEngine.Input.GetAxisRaw("Horizontal");
            float v = typing ? 0f : UnityEngine.Input.GetAxisRaw("Vertical");
            float inputMag = Mathf.Sqrt(h * h + v * v);
            // Sprint key is Shift in most setups, but the simplest signal is
            // "full-magnitude movement input held," which is what the player
            // is doing when they're trying to run.
            if (inputMag > 0.9f) targetDelta += SprintFOVDelta;
        }

        if (input.fxJetpackFovKick && _player != null && IsJetpackActive(_player))
            targetDelta += JetpackFOVDelta;

        // Refresh piloted-ship lookup so an abandoned orbiting ship doesn't
        // keep the boost FOV pinned after the player exits. Also gate on
        // CanThrust so a fuel-empty / power-empty ship doesn't kick FOV when
        // the player sits in the cockpit pressing W.
        // Use the cached static (set by Ship.PilotShip / cleared on exit)
        // instead of FindPilotedShip() — was 0.5ms/frame of FindObjectsOfType.
        _ship = Ship.PilotedInstance;
        if (input.fxShipBoostFov && _ship != null && _ship.CanThrust && IsShipBoosting(_ship))
            targetDelta += ShipBoostFOVDelta;

        _currentDelta = Mathf.SmoothDamp(_currentDelta, targetDelta, ref _deltaVelocity, 0.15f);
        _cam.fieldOfView = _baseFOV + _currentDelta;
    }

    static bool IsJetpackActive(PlayerController p)
    {
        if (!p.JetpackUnlocked) return false;
        // Typing in the AI chat presses Space / Ctrl / Shift as text — must
        // not light up the FOV kick. Mirrors the sprint-FOV typing gate
        // above and the input gates in PlayerController.
        if (AIChatScreen.IsTypingActive || PlayerController.isInModalSlotUI) return false;
        bool jumpHeld = TutorialGate.JumpHeld(TutorialAbility.Boost);
        bool downHeld = TutorialGate.DownThrustHeld(TutorialAbility.DownThrust);
        bool dirHeld  = TutorialGate.DirectionalThrustHeld(TutorialAbility.DirectionalThrust);
        return jumpHeld || downHeld || dirHeld;
    }

    static bool IsShipBoosting(Ship s)
    {
        var rb = s != null ? s.GetComponent<Rigidbody>() : null;
        return rb != null && rb.velocity.magnitude > 6f;
    }

    bool CacheRefs(CameraEffectsManager mgr)
    {
        if (_cached && _cam != null) return true;
        _cam = mgr.PlayerCamera;
        if (_cam == null) return false;
        _baseFOV = _cam.fieldOfView;
        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        // _ship is REFRESHED in Update via Ship.FindPilotedShip; don't cache here.
        _cached = true;
        return true;
    }

    void ResetIfCached()
    {
        if (!_cached || _cam == null) return;
        _cam.fieldOfView = _baseFOV;
        _currentDelta = 0f;
        _deltaVelocity = 0f;
    }
}
