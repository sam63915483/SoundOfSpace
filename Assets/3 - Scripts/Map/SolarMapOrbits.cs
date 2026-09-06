using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orbit lines for the v2 map. Every planet rides an exact circular rail round
/// the pinned sun and every moon an exact circle round its planet
/// (CelestialBody.railPeriod / satelliteOrbitRadius), so the lines are drawn
/// analytically — no forward simulation like the old MapOrbitLines needed.
/// Positions are rebuilt every LateUpdate from the live body/sun positions, so
/// floating-origin rebases and orbital motion are automatically correct.
/// Width scales with camera distance so lines stay a couple of pixels wide.
/// </summary>
[DefaultExecutionOrder(215)]   // after SolarMap (210) moved the camera
public class SolarMapOrbits : MonoBehaviour
{
    public int segments = 128;
    public float widthFraction = 0.0022f;   // of camera→centre distance
    public float minWidth = 2f;
    public Color planetColor = new Color(0.36f, 0.85f, 1f, 0.75f);
    public Color dwarfColor  = new Color(0.55f, 0.95f, 0.85f, 0.6f);
    public Color moonColor   = new Color(1f, 0.78f, 0.45f, 0.7f);

    public bool Visible { get; set; } = true;
    public float Alpha { get; set; }

    class Orbit
    {
        public CelestialBody body;
        public CelestialBody centre;   // sun or the moon's leader
        public LineRenderer line;
        public Color color;
        public bool satellite;
    }

    readonly List<Orbit> _orbits = new List<Orbit>();
    SolarMap _map;
    Vector3[] _buf;
    Material _mat;

    public void Bind(SolarMap map, CelestialBody[] bodies, CelestialBody sun)
    {
        _map = map;
        foreach (var o in _orbits) if (o.line != null) Destroy(o.line.gameObject);
        _orbits.Clear();
        if (_mat == null) _mat = new Material(Shader.Find("Sprites/Default"));
        _buf = new Vector3[Mathf.Max(8, segments)];

        foreach (var b in bodies)
        {
            if (b == null || b == sun || b.isStaticAttractor || b.bodyType == CelestialBody.BodyType.Sun) continue;
            bool satellite = b.satelliteOrbitRadius > 0f && b.coOrbitLeader != null;
            bool railed = b.railPeriod > 0f || (b.coOrbitLeader != null && !satellite);
            if (!satellite && !railed) continue;
            var o = new Orbit
            {
                body = b,
                centre = satellite ? b.coOrbitLeader : sun,
                satellite = satellite,
                color = satellite ? moonColor : (b.radius < 100f ? dwarfColor : planetColor),
            };
            var go = new GameObject("Orbit " + b.bodyName);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = _mat;
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = _buf.Length;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            o.line = lr;
            _orbits.Add(o);
        }
    }

    void LateUpdate()
    {
        if (_map == null) return;
        var cam = _map.ViewCamera;
        bool show = Visible && Alpha > 0.005f && cam != null && SolarMap.IsOpen;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

        for (int i = 0; i < _orbits.Count; i++)
        {
            var o = _orbits[i];
            if (o.line == null) continue;
            if (!show || o.body == null || o.centre == null) { o.line.enabled = false; continue; }

            Vector3 c = o.centre.transform.position;
            Vector3 rel = o.body.transform.position - c;
            float r = o.satellite ? o.body.satelliteOrbitRadius : rel.magnitude;
            if (r < 1f) { o.line.enabled = false; continue; }

            // Plane: from the body's own motion (moons: the leader's orbit plane).
            Vector3 vel = o.satellite ? o.centre.velocity : o.body.velocity;
            Vector3 relForPlane = o.satellite ? (o.centre.transform.position - SunPos()) : rel;
            Vector3 n = Vector3.Cross(relForPlane, vel);
            if (n.sqrMagnitude < 1e-3f) n = Vector3.forward;
            n.Normalize();
            Vector3 u = Vector3.ProjectOnPlane(rel, n);
            if (u.sqrMagnitude < 1e-3f) u = Vector3.ProjectOnPlane(Vector3.right, n);
            u.Normalize();
            Vector3 w = Vector3.Cross(n, u);

            int count = _buf.Length;
            for (int k = 0; k < count; k++)
            {
                float a = k * (Mathf.PI * 2f / count);
                _buf[k] = c + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * r;
            }
            o.line.SetPositions(_buf);

            float dist = Vector3.Distance(camPos, c);
            float width = Mathf.Max(minWidth, dist * widthFraction);
            o.line.startWidth = width;
            o.line.endWidth = width;
            Color col = o.color; col.a *= Alpha;
            o.line.startColor = col;
            o.line.endColor = col;
            o.line.enabled = true;
        }
    }

    Vector3 SunPos()
    {
        foreach (var o in _orbits) if (!o.satellite && o.centre != null) return o.centre.transform.position;
        return Vector3.zero;
    }
}
