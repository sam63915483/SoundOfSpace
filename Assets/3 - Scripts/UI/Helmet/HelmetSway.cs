using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Damped helmet sway: registered RectTransforms get a shared 2D offset
/// derived from camera angular velocity (look) and player surface velocity
/// (movement), spring-damped back to center. Driven from camera rotation
/// deltas rather than input axes so mouse, pad, and cinematic cameras all
/// produce consistent sway. Runs in LateUpdate after camera controllers.
/// Offsets are in canvas reference units and multiply per-layer (frame 1.0,
/// clusters 0.85) for a subtle depth parallax.
/// Register AFTER the final layout position is set — base position is
/// snapshotted at registration.
/// </summary>
public class HelmetSway : MonoBehaviour
{
    class Entry { public RectTransform rt; public Vector2 basePos; public float mult; }
    static readonly List<Entry> _entries = new List<Entry>();

    public static void Register(RectTransform rt, float multiplier)
    {
        if (rt == null) return;
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].rt == rt) return;
        _entries.Add(new Entry { rt = rt, basePos = rt.anchoredPosition, mult = multiplier });
    }

    /// Re-snapshot a registered transform's base position after it has been
    /// re-seated (art-housing seating moves anchoredPosition post-Register).
    public static void Reregister(RectTransform rt)
    {
        if (rt == null) return;
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].rt == rt) { _entries[i].basePos = rt.anchoredPosition; return; }
    }

    /// Stop swaying a transform (e.g. a cluster card that moved into an
    /// off-screen RenderTexture rig — its warped on-screen quad sways instead).
    public static void Unregister(RectTransform rt)
    {
        if (rt == null) return;
        for (int i = _entries.Count - 1; i >= 0; i--)
            if (_entries[i].rt == rt) { _entries.RemoveAt(i); return; }
    }

    Camera _cam;
    float _nextCamFind;
    Quaternion _lastCamRot;
    bool _hasLast;
    Vector2 _offset, _offsetVel;
    PlayerController _player;
    float _nextPlayerFind;

    // Helmet bob state — stride phase + a smoothed weight so the bob eases
    // in/out instead of cutting when the player stops or leaves the ground.
    float _bobPhase;
    float _bobWeight;
    Vector2 _bob;

    void LateUpdate()
    {
        var cfg = HelmetHudConfig.Instance;
        // Manual tweak mode: stop driving positions so play-mode Inspector
        // edits on the registered transforms stick.
        if (cfg != null && cfg.manualTweakMode) return;
        // Throttled re-finds — never FindObjectOfType/Camera.main per frame.
        if (_cam == null && Time.unscaledTime >= _nextCamFind)
        { _nextCamFind = Time.unscaledTime + 0.5f; _cam = Camera.main; _hasLast = false; }
        if (_player == null && Time.unscaledTime >= _nextPlayerFind)
        { _nextPlayerFind = Time.unscaledTime + 0.5f; _player = FindObjectOfType<PlayerController>(); }

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        Vector2 target = Vector2.zero;
        if (cfg != null && _cam != null && _cam.isActiveAndEnabled)
        {
            if (_hasLast)
            {
                // Angular delta in local axes → yaw/pitch rates (deg/s). The
                // helmet lags opposite to the head turn, then settles.
                Quaternion delta = Quaternion.Inverse(_lastCamRot) * _cam.transform.rotation;
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                Vector3 rate = axis * (angle / dt);
                target.x += Mathf.Clamp(-rate.y * 0.06f * cfg.lookSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
                target.y += Mathf.Clamp( rate.x * 0.06f * cfg.lookSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
            }
            _lastCamRot = _cam.transform.rotation;
            _hasLast = true;

            if (_player != null && _player.isActiveAndEnabled)
            {
                Vector3 vLocal = _cam.transform.InverseTransformDirection(_player.SurfaceVelocity);
                target.x += Mathf.Clamp(-vLocal.x * 0.8f * cfg.moveSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
                target.y += Mathf.Clamp(-vLocal.y * 0.8f * cfg.moveSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
            }
            target = Vector2.ClampMagnitude(target, cfg.swayMaxOffset);
        }
        else if (_cam != null && !_cam.isActiveAndEnabled)
        {
            _cam = null; // camera swapped (scene change / cinematic) — refind
        }

        float smooth = cfg != null ? 1f / Mathf.Max(0.01f, cfg.swaySmoothing) : 0.12f;
        _offset = Vector2.SmoothDamp(_offset, target, ref _offsetVel, smooth, Mathf.Infinity, dt);

        UpdateBob(cfg, dt);

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (e.rt == null) { _entries.RemoveAt(i); continue; }
            e.rt.anchoredPosition = e.basePos + (_offset + _bob) * e.mult;
        }
    }

    // Stride-matched helmet bob: the whole helmet (frame + seated readouts,
    // via the same registered-entry offsets as sway) dips on each footfall
    // and rocks slightly side to side. Phase advances with DISTANCE traveled
    // so cadence tracks speed — walking bobs gently, sprinting bobs harder
    // (amplitude blends with speed too). Applied outside the sway smoothing:
    // the oscillation is its own motion, not a damped spring response.
    // Gated by InputSettings.fxHelmetBob (CAMERA tab); defaults on pre-init.
    void UpdateBob(HelmetHudConfig cfg, float dt)
    {
        var mgr = CameraEffectsManager.Instance;
        bool enabled = mgr == null || mgr.Input == null || mgr.Input.fxHelmetBob;
        bool striding = enabled
            && _player != null && _player.isActiveAndEnabled && _player.IsOnGround
            && Ship.PilotedInstance == null;

        float speed = striding ? _player.SurfaceVelocity.magnitude : 0f;
        striding &= speed > 0.6f;   // dead-still / drift shouldn't tick steps

        // Ease the bob in/out over ~0.25 s so stopping mid-step settles the
        // helmet instead of freezing it at an offset.
        _bobWeight = Mathf.MoveTowards(_bobWeight, striding ? 1f : 0f, dt * 4f);
        if (_bobWeight <= 0f) { _bob = Vector2.zero; return; }

        float stepsPerMeter = cfg != null ? cfg.bobStepsPerMeter : 1.1f;
        float walkAmp = cfg != null ? cfg.bobWalkAmplitude : 3f;
        float runAmp = cfg != null ? cfg.bobRunAmplitude : 7f;

        if (striding) _bobPhase += speed * dt * stepsPerMeter * Mathf.PI;
        // ~4 m/s walk → ~9 m/s sprint; blend amplitude across that band.
        float amp = Mathf.Lerp(walkAmp, runAmp, Mathf.InverseLerp(4f, 9f, speed)) * _bobWeight;
        // |sin| dips once per step; lateral rocks alternate sides per step.
        _bob = new Vector2(Mathf.Sin(_bobPhase) * amp * 0.35f,
                           -Mathf.Abs(Mathf.Sin(_bobPhase)) * amp);
    }
}
