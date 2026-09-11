using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stops far-away point/spot lights from re-drawing the whole planet.
///
/// Every planet is ONE 2-million-triangle renderer on the Body layer. In the
/// built-in forward renderer, every pixel light whose range touches a
/// renderer's bounds re-draws that renderer once more — and a planet's bounds
/// are the whole planet. So each lantern, torch, tunnel light or shuttle lamp
/// costs a full extra planet pass every frame it is in view, even from the
/// other side of the map. Measured 2026-09-11 (PerfTrace run 2, laptop 4060):
/// looking at the village from afar, lights not touching the planet mesh cut
/// 30.7 M → 14.5 M triangles and 2.2 ms; at the moon base 41.9 M → 14.2 M and
/// 4.2 ms.
///
/// Rule: a light farther from the camera than 1.5× its range (min 12 m) has
/// its pool of ground light too small on screen to matter, so its culling
/// mask drops the Body layer — it still lights houses, props, NPCs and the
/// player normally, it just no longer re-renders the planet. Come within range
/// and the original mask is restored, so the ground glow under a lantern you
/// walk up to is unchanged. Lights whose mask already excludes Body (the moon
/// tunnel cage lights, set in the prefab) are left alone.
///
/// Throttled: distance check 4×/s, light list rescanned every 3 s (spawned
/// lights such as fireflies are picked up on the next rescan). Not a
/// MainMenu-skipping singleton, so it needs no seeding (trap #1).
/// </summary>
public class PlanetLightGate : MonoBehaviour
{
    public static PlanetLightGate Instance { get; private set; }

    const int BodyLayer = 10;              // "Body" (TagManager)
    const float TickSec = 0.25f;
    const float RescanSec = 3f;
    const float RangeFactor = 1.5f;
    const float MinKeepDistance = 12f;

    class Entry { public Light light; public int origMask; public bool gated; }
    readonly List<Entry> _entries = new List<Entry>();
    readonly HashSet<Light> _known = new HashSet<Light>();
    float _tick, _rescan;
    Camera _cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[PlanetLightGate]");
        DontDestroyOnLoad(go);
        go.AddComponent<PlanetLightGate>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RestoreAll();
    }

    void OnDisable() => RestoreAll();

    void Update()
    {
        _tick -= Time.unscaledDeltaTime;
        if (_tick > 0f) return;
        _tick = TickSec;

        _rescan -= TickSec;
        if (_rescan <= 0f) { _rescan = RescanSec; Rescan(); }

        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) return;
        Vector3 c = _cam.transform.position;
        int bit = 1 << BodyLayer;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (e.light == null) { _entries.RemoveAt(i); continue; }
            float keep = Mathf.Max(MinKeepDistance, e.light.range * RangeFactor);
            bool far = (e.light.transform.position - c).sqrMagnitude > keep * keep;
            if (far && !e.gated)
            {
                // Another script may have changed the mask since we last looked;
                // only gate lights that currently touch the planet.
                if ((e.light.cullingMask & bit) == 0) continue;
                e.origMask = e.light.cullingMask;
                e.light.cullingMask = e.origMask & ~bit;
                e.gated = true;
            }
            else if (!far && e.gated)
            {
                e.light.cullingMask = e.origMask;
                e.gated = false;
            }
        }
    }

    void Rescan()
    {
        _known.RemoveWhere(l => l == null);
        var all = FindObjectsOfType<Light>(false);
        int bit = 1 << BodyLayer;
        foreach (var l in all)
        {
            if (l == null || _known.Contains(l)) continue;
            if (l.type != LightType.Point && l.type != LightType.Spot) continue;
            if ((l.cullingMask & bit) == 0) continue;      // already deliberately off the planet
            _known.Add(l);
            _entries.Add(new Entry { light = l, origMask = l.cullingMask });
        }
    }

    void RestoreAll()
    {
        foreach (var e in _entries)
            if (e.light != null && e.gated) { e.light.cullingMask = e.origMask; e.gated = false; }
    }
}
