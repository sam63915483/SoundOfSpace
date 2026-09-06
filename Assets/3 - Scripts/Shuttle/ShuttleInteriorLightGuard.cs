using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stops outside lights from lighting the shuttle cabin THROUGH the hull.
///
/// Why lights get in: a Unity light with shadows off has nothing that can
/// block it — walls don't exist for it. The gameplay scene has two kinds of
/// those near the shuttle: the sun's unshadowed "Point Light (Sun)" (range
/// 40 km, the sunrise/sunset ground fill) which lights every sun-facing wall
/// of the cabin from inside whenever the shuttle is on the day side, and the
/// village lanterns (range 22 m) when parked near them. The shadowed
/// directional sun is NOT the problem: walls block it and it comes through the
/// windows, which is the intended look.
///
/// What this does: while the main camera is inside the ShuttleInteriorVolume,
/// every enabled light that (a) casts no shadows, (b) is not one of the
/// shuttle's own and (c) is not the player's (torch, eye light) gets the hull
/// and player layers removed from its culling mask. Restored the moment you
/// step out (and on disable). Rescans every second while inside, so lanterns
/// that stream in later are caught. Cost: one FindObjectsOfType per second
/// while inside, nothing per frame.
///
/// Side effect to know about: while you are inside, those lights also stop
/// lighting the same layers OUTSIDE (the ground you see through a window
/// loses the sunset warm fill; a lantern's pool on the dirt beside the
/// shuttle goes out). Only while inside; the shadowed sun still lights it all.
/// </summary>
public class ShuttleInteriorLightGuard : MonoBehaviour
{
    [Tooltip("The cabin trigger box (ShuttleInteriorVolume). Inside = camera within it.")]
    public BoxCollider interiorVolume;
    [Tooltip("Layers removed from unshadowed outside lights while inside: Default (astronaut, props), Ship, Body (the hull).")]
    public LayerMask strippedLayers = (1 << 0) | (1 << 9) | (1 << 10);
    public float rescanInterval = 1f;
    [Tooltip("Extra metres of slack around the volume so the guard doesn't flicker at the door.")]
    public float margin = 0.3f;
    public bool logChanges = true;

    readonly Dictionary<Light, int> _stripped = new Dictionary<Light, int>();
    bool _inside;
    float _nextScan, _nextCamSearch;
    Camera _cam;
    Transform _playerRoot;

    void Update()
    {
        if (_cam == null && Time.unscaledTime >= _nextCamSearch)
        {
            _nextCamSearch = Time.unscaledTime + 0.5f;
            var mgr = CameraEffectsManager.Instance;
            _cam = (mgr != null && mgr.PlayerCamera != null) ? mgr.PlayerCamera : Camera.main;
        }
        if (_cam == null || interiorVolume == null) return;

        bool inside = IsInside(_cam.transform.position);
        if (inside != _inside)
        {
            _inside = inside;
            if (inside) Apply(); else RestoreAll();
        }
        else if (inside && Time.unscaledTime >= _nextScan) Apply();
    }

    bool IsInside(Vector3 world)
    {
        Vector3 local = interiorVolume.transform.InverseTransformPoint(world) - interiorVolume.center;
        Vector3 h = interiorVolume.size * 0.5f;
        Vector3 s = interiorVolume.transform.lossyScale;
        float mx = margin / Mathf.Max(0.01f, Mathf.Abs(s.x)), my = margin / Mathf.Max(0.01f, Mathf.Abs(s.y)), mz = margin / Mathf.Max(0.01f, Mathf.Abs(s.z));
        return Mathf.Abs(local.x) <= h.x + mx && Mathf.Abs(local.y) <= h.y + my && Mathf.Abs(local.z) <= h.z + mz;
    }

    void Apply()
    {
        _nextScan = Time.unscaledTime + Mathf.Max(0.2f, rescanInterval);
        if (_playerRoot == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) _playerRoot = p.transform;
        }
        int strip = strippedLayers.value;
        int changed = 0;
        foreach (var l in FindObjectsOfType<Light>())
        {
            if (l == null || !l.enabled || l.shadows != LightShadows.None) continue;   // shadowed lights are blocked by the hull already
            if (l.transform.IsChildOf(transform)) continue;                             // the shuttle's own lights
            if (_playerRoot != null && l.transform.IsChildOf(_playerRoot)) continue;    // torch / eye light
            if (_stripped.ContainsKey(l)) continue;
            if ((l.cullingMask & strip) == 0) continue;
            _stripped[l] = l.cullingMask;
            l.cullingMask &= ~strip;
            changed++;
        }
        if (logChanges && changed > 0)
            Debug.Log($"[ShuttleInteriorLightGuard] inside the cabin: masked {changed} unshadowed outside light(s) off the hull ({_stripped.Count} total).");
    }

    void RestoreAll()
    {
        int n = 0;
        foreach (var kv in _stripped)
            if (kv.Key != null) { kv.Key.cullingMask = kv.Value; n++; }
        _stripped.Clear();
        if (logChanges && n > 0) Debug.Log($"[ShuttleInteriorLightGuard] left the cabin: restored {n} light mask(s).");
    }

    void OnDisable() { if (_inside) { _inside = false; RestoreAll(); } }
}
