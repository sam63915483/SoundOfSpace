using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stops outside lights from lighting the shuttle cabin THROUGH the hull —
/// without changing anything outside.
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
/// <b>How it used to work, and why it was replaced (2026-09-07).</b> The first
/// version waited until the camera was inside the cabin, then stripped the
/// Default / Ship / Body layers from those lights, and restored them on the
/// way out. But <c>Body</c> is the PLANET, so every time you crossed the
/// doorway the whole landscape lost or regained its sunset fill in one frame —
/// Sam: "whenever I'm in the shuttle or within a foot of it the lighting
/// changes, and it's very noticeable every time I leave."
///
/// <b>Now:</b> the cabin's own renderers live on the <c>ShuttleInterior</c>
/// layer (15), and that one layer is the only thing removed from unshadowed
/// outside lights — permanently, whether you are inside or not. Nothing outside
/// is on that layer, so nothing outside changes, and there is no doorway to
/// snap at. The shadowed sun and the shuttle's own lights keep the layer, so
/// the cabin is lit exactly as before. Lanterns that stream in later are
/// caught by a slow rescan while the shuttle is near the player.
/// </summary>
public class ShuttleInteriorLightGuard : MonoBehaviour
{
    [Tooltip("Kept for scene compatibility; no longer used to decide anything. The guard is always on.")]
    public BoxCollider interiorVolume;

    [Tooltip("The cabin layer. Removed from every unshadowed outside light, always. " +
             "Nothing outside the shuttle should be on it.")]
    public LayerMask strippedLayers = 1 << 15;   // ShuttleInterior

    [Tooltip("Seconds between scans for new lights (lanterns stream in and out).")]
    public float rescanInterval = 2f;

    [Tooltip("Only bother scanning while the player is within this many metres of the shuttle. " +
             "The mask edit is harmless anywhere; this just bounds the FindObjectsOfType cost.")]
    public float activeRange = 120f;

    [Tooltip("Ignored — retained so old scene values don't break on load.")]
    public float margin = 0.3f;

    public bool logChanges = false;

    readonly Dictionary<Light, int> _stripped = new Dictionary<Light, int>();
    float _nextScan, _nextPlayerSearch;
    Transform _playerRoot;

    void Update()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + Mathf.Max(0.5f, rescanInterval);

        if (_playerRoot == null && Time.unscaledTime >= _nextPlayerSearch)
        {
            _nextPlayerSearch = Time.unscaledTime + 1f;
            var p = GameObject.FindWithTag("Player");
            if (p != null) _playerRoot = p.transform;
        }
        if (_playerRoot != null &&
            (_playerRoot.position - transform.position).sqrMagnitude > activeRange * activeRange) return;

        Apply();
    }

    void Apply()
    {
        int strip = strippedLayers.value;
        if (strip == 0) return;
        int changed = 0;
        foreach (var l in FindObjectsOfType<Light>())
        {
            if (l == null || !l.enabled || l.shadows != LightShadows.None) continue;   // shadowed lights are blocked by the hull already
            if (l.transform.IsChildOf(transform)) continue;                             // the shuttle's own lights must light the cabin
            if (_playerRoot != null && l.transform.IsChildOf(_playerRoot)) continue;    // torch / eye light / viewmodel fill
            if (_stripped.ContainsKey(l)) continue;
            if ((l.cullingMask & strip) == 0) continue;
            _stripped[l] = l.cullingMask;
            l.cullingMask &= ~strip;
            changed++;
        }
        if (logChanges && changed > 0)
            Debug.Log($"[ShuttleInteriorLightGuard] masked {changed} unshadowed outside light(s) off the cabin layer ({_stripped.Count} total).");
    }

    void RestoreAll()
    {
        foreach (var kv in _stripped)
            if (kv.Key != null) kv.Key.cullingMask = kv.Value;
        _stripped.Clear();
    }

    void OnDisable() { RestoreAll(); }
}
