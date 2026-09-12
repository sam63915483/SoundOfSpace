using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hides big static clusters (the moon base, the village) while a planet is
/// between them and the camera, or while they are below the horizon of the
/// planet they stand on.
///
/// Unity only frustum-culls: a moon base that has dipped behind Humble Abode is
/// still inside the view cone, so all 400 of its renderers and 33 lights are
/// culled, sorted and submitted every frame (run 6, 2026-09-11: ~5000 draws
/// while looking "at" a moon you cannot see, 73 fps vs 81 looking elsewhere;
/// Sam: "even when the moon dipped beyond the horizon my fps was still low
/// until I turned away"). Static occlusion culling cannot be baked because the
/// planets move, so this does the one occluder that matters — spheres — by hand.
///
/// Per cluster, 6×/s:
///   • own planet: hidden when the cluster's TOP (centre + bounds radius) is
///     below the camera's horizon — the same formula as a ship's mast dropping
///     behind the sea.
///   • every other body: hidden when the camera→cluster segment passes through
///     that body's sphere shrunk by the cluster radius (so a half-visible
///     cluster stays visible).
///   • its point lights additionally go off when farther than 20× their range
///     from the camera (min 150 m): an 8 m tunnel lamp seen from 300 m
///     contributes nothing to any pixel, but still costs a pass per renderer.
/// Only renderers/lights that were ON when we looked are ever toggled, and
/// only the ones this script turned off are turned back on, so anything else
/// (MeshCombineTool's disabled originals, TerrainHole, LOD scripts) is left alone.
///
/// Clusters are found by name (see ClusterNames); add a name to cover a new
/// settlement. Kill switch: FeatureVault.PlanetChunks doubles for this too —
/// both are "render only what a planet lets you see".
/// </summary>
public class PlanetOcclusionCuller : MonoBehaviour
{
    public static PlanetOcclusionCuller Instance { get; private set; }

    static readonly string[] ClusterNames = { "MoonBaseINTER", "Tunnel Rig", "TOWN-VILLAGE" };
    const float TickSec = 0.16f;
    const float RescanSec = 2f;
    const float FarLightRangeFactor = 20f;
    const float FarLightMinDistance = 150f;

    class Cluster
    {
        public Transform root;
        public CelestialBody home;
        public Vector3 localCentre;      // bounds centre in the home body's local space
        public float radius;             // bounds radius (world)
        public readonly List<Renderer> renderers = new List<Renderer>();
        public readonly List<Light> lights = new List<Light>();
        public readonly List<Renderer> renderersOff = new List<Renderer>();
        public readonly List<Light> lightsOff = new List<Light>();
        public bool hidden, lightsFar;
    }

    readonly List<Cluster> _clusters = new List<Cluster>();
    readonly HashSet<Transform> _known = new HashSet<Transform>();
    float _tick, _rescan;
    Camera _cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.PlanetChunks) return;
        if (Instance != null) return;
        var go = new GameObject("[PlanetOcclusionCuller]");
        DontDestroyOnLoad(go);
        go.AddComponent<PlanetOcclusionCuller>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var c in _clusters) Restore(c, true, true);
    }

    void OnDisable()
    {
        foreach (var c in _clusters) Restore(c, true, true);
    }

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
        var bodies = NBodySimulation.Bodies;

        for (int i = _clusters.Count - 1; i >= 0; i--)
        {
            var cl = _clusters[i];
            if (cl.root == null || cl.home == null) { Restore(cl, true, true); _clusters.RemoveAt(i); continue; }

            Vector3 centre = cl.home.transform.TransformPoint(cl.localCentre);
            bool hidden = IsHidden(c, centre, cl.radius, cl.home, bodies);
            if (hidden != cl.hidden)
            {
                cl.hidden = hidden;
                if (hidden) HideRenderers(cl); else Restore(cl, true, false);
            }

            // lights: off when hidden, and off when too far to matter
            float dist = (centre - c).magnitude - cl.radius;
            bool far = dist > FarLightMinDistance;
            if (far)
            {
                // "far" is per light (range-based); recheck the ones still on
                for (int k = 0; k < cl.lights.Count; k++)
                {
                    var l = cl.lights[k];
                    if (l == null || !l.enabled) continue;
                    float keep = Mathf.Max(FarLightMinDistance, l.range * FarLightRangeFactor);
                    if ((l.transform.position - c).sqrMagnitude > keep * keep) { l.enabled = false; cl.lightsOff.Add(l); }
                }
                cl.lightsFar = true;
            }
            else if (cl.lightsFar && !hidden)
            {
                Restore(cl, false, true);
                cl.lightsFar = false;
            }
            if (hidden && cl.lightsOff.Count < cl.lights.Count)
            {
                for (int k = 0; k < cl.lights.Count; k++)
                {
                    var l = cl.lights[k];
                    if (l != null && l.enabled) { l.enabled = false; cl.lightsOff.Add(l); }
                }
            }
        }
    }

    /// <summary>Horizon test against the home body, segment test against every other body.</summary>
    static bool IsHidden(Vector3 cam, Vector3 centre, float radius, CelestialBody home, CelestialBody[] bodies)
    {
        Vector3 d = centre - cam;
        float dist = d.magnitude;
        if (dist < 1e-3f) return false;
        foreach (var b in bodies)
        {
            if (b == null || b.radius <= 0f) continue;
            Vector3 bc = b.Position;
            float R = b.radius;
            if (b == home)
            {
                // A cluster that reaches into the body (the moon tube runs THROUGH
                // Constant Companion, so its bounds centre sits near the moon's
                // centre) has no meaningful horizon: the central angle is noise and
                // it was being hidden while you stood next to it (2026-09-12).
                // Such clusters are only ever occluded by OTHER bodies.
                float off = (centre - bc).magnitude;
                if (off < R * 0.6f || radius > R * 0.5f) continue;
                float dc = (cam - bc).magnitude;
                float dp = off + radius;                              // the cluster's highest point
                if (dc <= R || dp <= R) continue;
                float limit = Mathf.Acos(Mathf.Clamp(R / dc, -1f, 1f)) + Mathf.Acos(Mathf.Clamp(R / dp, -1f, 1f));
                float central = Vector3.Angle(cam - bc, centre - bc) * Mathf.Deg2Rad;
                if (central > limit) return true;
            }
            else
            {
                float r = R - radius;
                if (r <= 0f) continue;
                Vector3 m = cam - bc;
                Vector3 dir = d / dist;
                float bq = Vector3.Dot(m, dir);
                float cq = Vector3.Dot(m, m) - r * r;
                if (cq > 0f && bq > 0f) continue;              // sphere behind the camera
                float disc = bq * bq - cq;
                if (disc < 0f) continue;                        // misses
                float tHit = -bq - Mathf.Sqrt(disc);
                if (tHit > 0f && tHit < dist) return true;
            }
        }
        return false;
    }

    static void HideRenderers(Cluster cl)
    {
        for (int k = 0; k < cl.renderers.Count; k++)
        {
            var r = cl.renderers[k];
            if (r != null && r.enabled) { r.enabled = false; cl.renderersOff.Add(r); }
        }
    }

    static void Restore(Cluster cl, bool renderers, bool lights)
    {
        if (renderers)
        {
            foreach (var r in cl.renderersOff) if (r != null) r.enabled = true;
            cl.renderersOff.Clear();
            cl.hidden = false;
        }
        if (lights)
        {
            foreach (var l in cl.lightsOff) if (l != null) l.enabled = true;
            cl.lightsOff.Clear();
            cl.lightsFar = false;
        }
    }

    // scratch lists so the periodic rescan allocates nothing (a GetComponentsInChildren<T>()
    // over a whole planet every 2 s was exactly the kind of GC spike this pass is hunting)
    static readonly List<Transform> _tScratch = new List<Transform>(2048);
    static readonly List<Renderer> _rScratch = new List<Renderer>(512);
    static readonly List<Light> _lScratch = new List<Light>(64);
    readonly HashSet<CelestialBody> _scannedBodies = new HashSet<CelestialBody>();

    void Rescan()
    {
        // new clusters: each body is walked ONCE (clusters are scene objects, not spawned)
        foreach (var body in NBodySimulation.Bodies)
        {
            if (body == null || _scannedBodies.Contains(body)) continue;
            _scannedBodies.Add(body);
            _tScratch.Clear();
            body.GetComponentsInChildren(true, _tScratch);
            for (int j = 0; j < _tScratch.Count; j++)
            {
                var t = _tScratch[j];
                if (t == null || _known.Contains(t)) continue;
                bool wanted = false;
                for (int i = 0; i < ClusterNames.Length; i++) if (t.name == ClusterNames[i]) { wanted = true; break; }
                if (!wanted) continue;
                _known.Add(t);
                _clusters.Add(new Cluster { root = t, home = body });
            }
            _tScratch.Clear();
        }
        // refresh membership + bounds (spawned NPCs, combined meshes, etc.)
        foreach (var cl in _clusters)
        {
            if (cl.root == null) continue;
            bool wasHidden = cl.hidden;
            if (wasHidden) Restore(cl, true, false);          // measure with everything on, then re-hide below
            cl.renderers.Clear(); cl.lights.Clear();
            _rScratch.Clear();
            cl.root.GetComponentsInChildren(false, _rScratch);
            Bounds b = new Bounds(cl.root.position, Vector3.zero);
            bool any = false;
            for (int j = 0; j < _rScratch.Count; j++)
            {
                var r = _rScratch[j];
                if (r == null) continue;
                cl.renderers.Add(r);
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            _rScratch.Clear();
            _lScratch.Clear();
            cl.root.GetComponentsInChildren(false, _lScratch);
            for (int j = 0; j < _lScratch.Count; j++)
            {
                var l = _lScratch[j];
                if (l != null && (l.type == LightType.Point || l.type == LightType.Spot)) cl.lights.Add(l);
            }
            _lScratch.Clear();
            if (any)
            {
                cl.localCentre = cl.home.transform.InverseTransformPoint(b.center);
                cl.radius = b.extents.magnitude;
            }
            if (wasHidden) { cl.hidden = true; HideRenderers(cl); }
        }
    }
}
