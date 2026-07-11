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

    Camera _cam;
    float _nextCamFind;
    Quaternion _lastCamRot;
    bool _hasLast;
    Vector2 _offset, _offsetVel;
    PlayerController _player;
    float _nextPlayerFind;

    void LateUpdate()
    {
        var cfg = HelmetHudConfig.Instance;
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

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (e.rt == null) { _entries.RemoveAt(i); continue; }
            e.rt.anchoredPosition = e.basePos + _offset * e.mult;
        }
    }
}
