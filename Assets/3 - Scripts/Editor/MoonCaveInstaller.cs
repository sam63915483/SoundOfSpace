using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools ▸ Cave ▸ Install Moon Caves
///
/// ONE cave network for the whole of Constant Companion (the moon): three
/// mouths in craters, a maze of slim tunnels and caverns filling the interior,
/// and a zero-g cavern at the core. Sam, 2026-09-23: "use the entire inside of
/// the moon… sprawling tunnels that lead to different caverns… a maze… at the
/// centre a cavern that is 0 g and unlocks the player like passing the
/// atmosphere line."
///
/// WHAT IT DOES, IN ORDER
///   1. Removes the old three separate caves (Cave_Moon_A/B/C) and the tube.
///   2. Builds a hidden LOD0 preview of the moon's REAL terrain (TutorialGrassBake's
///      recipe) and turns it into a radius-per-direction function (SphereGround).
///   3. Picks the three mouths: the best crater in each third of the moon.
///   4. Grows one network in moon space from all three entrance ramps: long slim
///      legs that steadily descend, a cavern at most junctions, loops, and a core
///      cavern the branches reach. Every leg walkable (≤ 25°), every passage
///      clear of every other, nothing under the moon base.
///   5. CaveSolid builds the solid (spherical terrain, three mouths), runs its
///      checks, and splits the faceted result into octant meshes.
///   6. Writes one prefab, Cave_Moon, rooted at the moon's centre: rock pieces
///      + mouth skin in the generator's space with the moon's own shader,
///      three TerrainHole markers, CaveVolume (+ path distances), the crystal
///      seeder, the ZeroGZone. Places it under the moon. Sam saves.
/// </summary>
public static class MoonCaveInstaller
{
    const string MoonName = "Constant Companion";
    const string TunnelRigName = "Tunnel Rig";
    const string OutFolder = CaveRockTextures.Folder;
    const string PreviewName = "Body Generator (Moon Cave Preview)";
    const string PrefabName = "Cave_Moon";
    const float MaxWalkSlopeDeg = 28f;
    const float CoreCavernRadius = 13f;      // the zero-g cavern at the centre
    const double TimeoutSeconds = 180.0;
    /// Every stage appends a timestamped line here. If the Editor freezes, this
    /// file says which stage it froze in.
    public static readonly string LogPath = System.IO.Path.GetFullPath("Library/MoonCaves.log");
    static void Log(string line)
    {
        try { System.IO.File.AppendAllText(LogPath, $"{System.DateTime.Now:HH:mm:ss}  {line}\n"); } catch { }
    }

    // Moon base, moon-local (measured 2026-09-22): bounds centre and a sphere
    // that comfortably contains it.
    static readonly Vector3 MoonBaseLocal = new Vector3(7.03f, -59.17f, 10.16f);
    const float MoonBaseRadius = 26f;
    const float MoonBaseGap = 12f;

    class Site
    {
        public string name;
        public Vector3 dir;          // moon-local unit direction of the mouth
        public Vector3 localPos; public Quaternion localRot; public float R;
        public string craterNote = "";
    }

    static readonly Site[] Sites =
    {
        new Site { name = "A", dir = new Vector3( 0.94f, 0.34f,  0.00f).normalized },
        new Site { name = "B", dir = new Vector3(-0.47f, 0.34f,  0.81f).normalized },
        new Site { name = "C", dir = new Vector3(-0.47f, 0.34f, -0.81f).normalized },
    };

    // ── The network, grown in moon space ─────────────────────────────────────

    class NetParams
    {
        public int seed = 7;
        public int targetLegs = 140, loops = 20;
        public float turnMax = 85f;                  // degrees either side per leg, in the tangent plane
        public float lenMin = 12f, lenMax = 20f;
        public float branchKeep = 0.65f;
        public float roomProb = 0.6f, roomMin = 5.5f, roomMax = 9f, roomW = 1.2f, roomH = 1.0f;
        public float bigRoomProb = 0.12f, bigRoomMin = 10f, bigRoomMax = 12f;
        public float rMin = 2.0f, rMax = 2.6f, wMin = 1.15f, wMax = 1.3f, hMin = 0.95f, hMax = 1.1f;
        public float minRadius = 17f;                // node distance from the core (core cavern 13 + wall 3 + 1)
        public float surfaceMargin = 9f;             // nodes stay this far under the surface
        public float gap = 2.0f;                     // spare rock between separate passages (walls are 3 m on top)
        public float maxSlopeDeg = 25f, coreSlopeDeg = 34f;
        public int coreLinks = 4;                    // tunnels that reach the core cavern
    }
    static readonly NetParams Params = new NetParams();

    class Node { public Vector3 p; public Vector3 heading; public float r, w, h; public int children, fails; public bool core; public List<int> links = new List<int>(); }

    class Net
    {
        public List<Node> nodes = new List<Node>();
        public List<(int a, int b, bool openAir)> legs = new List<(int, int, bool)>();
        public List<(int node, float r, float w, float h)> rooms = new List<(int, float, float, float)>();
        public int loops, coreLinks; public float length;
        public int coreNode;
    }

    static float SegPointDist(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }
    static float SegSegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        float best = float.MaxValue;
        int n1 = Mathf.Max(1, Mathf.CeilToInt((q1 - p1).magnitude / 0.35f));
        for (int i = 0; i <= n1; i++) best = Mathf.Min(best, SegPointDist(p2, q2, Vector3.Lerp(p1, q1, i / (float)n1)));
        return best;
    }

    static Net GrowMoon(float R, System.Func<Vector3, float> terrainRadius, NetParams P, System.Text.StringBuilder log)
    {
        var rng = new System.Random(P.seed);
        float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var net = new Net();
        float Depth(Vector3 p) => terrainRadius(p.normalized) - p.magnitude;    // metres under the surface
        bool Allowed(Vector3 p)
        {
            if (p.magnitude < P.minRadius) return false;
            if (Depth(p) < P.surfaceMargin) return false;
            if ((p - MoonBaseLocal).magnitude < MoonBaseRadius + MoonBaseGap) return false;
            return true;
        }
        float Slope(Vector3 a, Vector3 b)
        {
            float dd = Mathf.Abs(a.magnitude - b.magnitude);
            float len = (a - b).magnitude;
            float run = Mathf.Sqrt(Mathf.Max(0.01f, len * len - dd * dd));
            return Mathf.Atan2(dd, run) * Mathf.Rad2Deg;
        }
        bool Touches(int node, int i, int j)
        {
            if (node == i || node == j) return true;
            if (i >= 0 && net.nodes[i].links.Contains(node)) return true;
            if (j >= 0 && net.nodes[j].links.Contains(node)) return true;
            return false;
        }
        bool Clear(Vector3 pa, Vector3 pb, float rc, int i, int j)
        {
            for (int k = 0; k < net.legs.Count; k++)
            {
                var (a, b, _) = net.legs[k];
                if (Touches(a, i, j) || Touches(b, i, j)) continue;
                float rr = (net.nodes[a].r + net.nodes[b].r) * 0.5f;
                if (SegSegDist(pa, pb, net.nodes[a].p, net.nodes[b].p) - rr - rc < P.gap) return false;
            }
            for (int k = 0; k < net.rooms.Count; k++)
            {
                var rm = net.rooms[k];
                if (Touches(rm.node, i, j)) continue;
                if (SegPointDist(pa, pb, net.nodes[rm.node].p) - rm.r * Mathf.Max(rm.w, rm.h) - rc < P.gap) return false;
            }
            for (int k = 0; k < net.nodes.Count; k++)
            {
                if (Touches(k, i, j)) continue;
                if (SegPointDist(pa, pb, net.nodes[k].p) - net.nodes[k].r - rc < P.gap) return false;
            }
            if (j < 0)
                foreach (int nb in net.nodes[i].links)
                    if ((net.nodes[nb].p - pb).magnitude < P.gap + rc + net.nodes[nb].r) return false;
            return true;
        }
        int Add(Node n) { net.nodes.Add(n); return net.nodes.Count - 1; }
        void Link(int a, int b, bool openAir = false)
        {
            net.legs.Add((a, b, openAir));
            net.nodes[a].links.Add(b); net.nodes[b].links.Add(a);
            net.length += (net.nodes[a].p - net.nodes[b].p).magnitude;
        }

        // The core cavern: a node at the centre with a big room. Legs may end
        // on its surface (a sphere of radius CoreCavernRadius).
        net.coreNode = Add(new Node { p = Vector3.zero, r = 3f, w = 1f, h = 1f, core = true });
        net.rooms.Add((net.coreNode, CoreCavernRadius, 1f, 1f));

        // Entrance ramps, one per mouth, then a first cavern each grows from.
        var frontier = new List<int>();
        foreach (var site in Sites)
        {
            Matrix4x4 m = Matrix4x4.TRS(site.localPos, site.localRot, Vector3.one);
            Vector3 Arc(float x, float d, float s) => m.MultiplyPoint3x4(ArcLocal(site.R, x, d, s));
            int n0 = Add(new Node { p = Arc(0, -1.2f, -2.5f), r = 2.3f, w = 1.15f, h = 0.95f });
            int n1 = Add(new Node { p = Arc(0, 1.2f, 2.5f), r = 2.3f, w = 1.15f, h = 0.95f });
            int n2 = Add(new Node { p = Arc(0, 4.5f, 9.5f), r = 2.4f, w = 1.2f, h = 1.0f });
            int n3 = Add(new Node { p = Arc(0, 7.5f, 16f), r = 2.5f, w = 1.25f, h = 1.0f });
            int n4 = Add(new Node { p = Arc(0, 10.5f, 23f), r = 2.5f, w = 1.25f, h = 1.0f });
            Link(n0, n1, true); Link(n1, n2); Link(n2, n3); Link(n3, n4);
            net.rooms.Add((n4, 6f, 1.2f, 1.0f));
            net.nodes[n4].heading = m.MultiplyVector(Vector3.forward).normalized;
            frontier.Add(n4);
        }

        int tries = 0;
        while (net.legs.Count < P.targetLegs && tries < 120000 && frontier.Count > 0)
        {
            tries++;
            int fi = (rng.NextDouble() < 0.6 && frontier.Count > 3)
                ? frontier[frontier.Count - 1 - rng.Next(Mathf.Min(8, frontier.Count))]
                : frontier[rng.Next(frontier.Count)];
            var f = net.nodes[fi];
            Vector3 up = f.p.normalized;
            Vector3 h0 = f.heading.sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(f.heading, up).normalized : Vector3.zero;
            if (h0.sqrMagnitude < 0.01f) h0 = Vector3.ProjectOnPlane(Vector3.Cross(up, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(up, Vector3.up) : Vector3.right, up).normalized;
            float turn = (f.children == 0 && f.links.Count <= 1 && net.rooms.Exists(rm => rm.node == fi))
                ? Rand(-150f, 150f) : Rand(-P.turnMax, P.turnMax);
            Vector3 heading = Quaternion.AngleAxis(turn, up) * h0;
            float len = Rand(P.lenMin, P.lenMax);
            // Steady descent: most legs drop at 12-23° so the maze reaches the
            // core; some climb so it doubles back over itself.
            float desc = (float)rng.NextDouble() < 0.7 ? Rand(len * 0.22f, len * 0.42f) : Rand(-len * 0.25f, len * 0.1f);
            Vector3 cand = f.p + heading * len - up * desc;
            var c = new Node { p = cand, heading = heading, r = Rand(P.rMin, P.rMax), w = Rand(P.wMin, P.wMax), h = Rand(P.hMin, P.hMax) };

            bool ok = Allowed(cand) && Slope(f.p, cand) <= P.maxSlopeDeg;
            if (ok) ok = Clear(f.p, cand, (f.r + c.r) * 0.5f, fi, -1);
            if (!ok)
            {
                if (++f.fails > 800) frontier.Remove(fi);
                continue;
            }
            int ci = Add(c);
            Link(fi, ci);
            f.children++;
            if (f.children >= 3 || (f.children >= 2 && (float)rng.NextDouble() > P.branchKeep)) frontier.Remove(fi);
            frontier.Add(ci);

            if ((float)rng.NextDouble() < P.roomProb)
            {
                float rr = (float)rng.NextDouble() < P.bigRoomProb ? Rand(P.bigRoomMin, P.bigRoomMax) : Rand(P.roomMin, P.roomMax);
                float maxByCore = (cand.magnitude - CoreCavernRadius - 3f - 3f) / Mathf.Max(P.roomW, P.roomH);
                float maxBySurface = (Depth(cand) - 3f - 1f) / Mathf.Max(P.roomW, P.roomH);
                rr = Mathf.Min(rr, Mathf.Min(maxByCore, maxBySurface));
                if (rr >= P.roomMin * 0.7f)
                {
                    bool roomOk = true;
                    for (int k = 0; k < net.legs.Count && roomOk; k++)
                    {
                        var (a, b, _) = net.legs[k];
                        if (Touches(a, ci, -1) || Touches(b, ci, -1)) continue;
                        float lr = (net.nodes[a].r + net.nodes[b].r) * 0.5f;
                        if (SegPointDist(net.nodes[a].p, net.nodes[b].p, cand) - lr - rr < P.gap * 0.4f) roomOk = false;
                    }
                    for (int k = 0; k < net.rooms.Count && roomOk; k++)
                    {
                        var rm = net.rooms[k];
                        if (Touches(rm.node, ci, -1)) continue;
                        if ((net.nodes[rm.node].p - cand).magnitude - rm.r - rr < P.gap * 0.4f) roomOk = false;
                    }
                    if (roomOk) net.rooms.Add((ci, rr, P.roomW, P.roomH));
                }
            }
        }

        // Loops: join close non-adjacent nodes so the maze doubles back.
        for (int t = 0; t < 30000 && net.loops < P.loops; t++)
        {
            int i = rng.Next(net.nodes.Count), j = rng.Next(net.nodes.Count);
            if (i == j) continue;
            var a = net.nodes[i]; var b = net.nodes[j];
            if (a.core || b.core || a.links.Contains(j)) continue;
            bool near = false; foreach (int l in a.links) if (b.links.Contains(l)) { near = true; break; }
            if (near) continue;
            float dist = (a.p - b.p).magnitude;
            if (dist < 10f || dist > 24f || Slope(a.p, b.p) > P.maxSlopeDeg) continue;
            if (!Clear(a.p, b.p, (a.r + b.r) * 0.5f, i, j)) continue;
            Link(i, j);
            net.loops++;
        }

        // CORE SPOKES: three walkable two-leg descents into the core from
        // junctions spread around the moon. Each landing point is found by
        // sweeping directions and measuring the slope — nothing assumed.
        bool Descend(int from, float targetRadius, float maxSlope, out int made)
        {
            made = -1;
            var n = net.nodes[from];
            Vector3 up = n.p.normalized;
            Frame(up, out Vector3 t0, out Vector3 t1);
            for (float theta = 10f; theta <= 85f; theta += 5f)
                for (int d = 0; d < 12; d++)
                {
                    float ang = d * 30f * Mathf.Deg2Rad;
                    Vector3 side = (t0 * Mathf.Cos(ang) + t1 * Mathf.Sin(ang)).normalized;
                    Vector3 dir = (up * Mathf.Cos(theta * Mathf.Deg2Rad) + side * Mathf.Sin(theta * Mathf.Deg2Rad)).normalized;
                    Vector3 target = dir * targetRadius;
                    float len = (target - n.p).magnitude;
                    if (len < 6f || len > 26f) continue;
                    if (Slope(n.p, target) > maxSlope) continue;
                    if (targetRadius > CoreCavernRadius && (target - MoonBaseLocal).magnitude < MoonBaseRadius + MoonBaseGap) continue;
                    if (!Clear(n.p, target, (n.r + 2.6f) * 0.5f, from, targetRadius < CoreCavernRadius ? net.coreNode : -1)) continue;
                    made = Add(new Node { p = target, heading = Vector3.ProjectOnPlane(dir, target.normalized).normalized, r = 2.6f, w = 1.25f, h = 1.05f });
                    Link(from, made);
                    return true;
                }
            return false;
        }
        var starts = new List<int>();
        for (int i = 0; i < net.nodes.Count; i++)
        {
            var n = net.nodes[i];
            float rad = n.p.magnitude;
            if (n.core || n.links.Count == 0 || rad < 19f || rad > 30f) continue;
            bool farEnough = true;
            foreach (int sIdx in starts) if (Vector3.Angle(net.nodes[sIdx].p, n.p) < 80f) { farEnough = false; break; }
            if (farEnough) starts.Add(i);
            if (starts.Count == 3) break;
        }
        foreach (int start in starts)
        {
            float r0 = net.nodes[start].p.magnitude;
            float midR = (r0 + CoreCavernRadius - 4f) * 0.5f;
            if (!Descend(start, midR, 28f, out int mid)) { log.AppendLine($"[MoonCaves] core spoke from node {start} (r={r0:0.0}): no walkable first leg"); continue; }
            if (!Descend(mid, CoreCavernRadius - 4f, 30f, out int end)) { log.AppendLine($"[MoonCaves] core spoke from node {start}: no walkable second leg"); continue; }
            Link(end, net.coreNode);
            net.coreLinks++;
        }
        float deepest = 0f; foreach (var n in net.nodes) if (!n.core) deepest = Mathf.Max(deepest, R - n.p.magnitude);
        log.AppendLine($"[MoonCaves] Network: {net.legs.Count} legs, {net.length:0} m, {net.rooms.Count - 1} caverns + the core, {net.loops} loops, {net.coreLinks} tunnels into the core, deepest node {deepest:0.0} m under, {tries} tries.");
        return net;
    }

    static void Frame(Vector3 up, out Vector3 t0, out Vector3 t1)
    {
        t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        t1 = Vector3.Cross(up, t0).normalized;
    }

    static Vector3 ArcLocal(float R, float x, float d, float s)
    {
        float along = Mathf.Sqrt(x * x + s * s);
        if (along < 1e-4f) return new Vector3(0f, -d, 0f);
        float theta = along / R;
        Vector3 t = new Vector3(x, 0f, s) / along;
        Vector3 dir = Vector3.up * Mathf.Cos(theta) + t * Mathf.Sin(theta);
        return dir * (R - d) - new Vector3(0f, R, 0f);
    }

    static CaveSolid.Layout BuildLayout(Net net, float R, CaveSolid.Ground ground)
    {
        var L = new CaveSolid.Layout { style = CaveSolid.Style.Preset(CaveSolid.Recipe.Strata), bodyRadius = R, ground = ground, centre = Vector3.zero, centreSet = true };
        foreach (var site in Sites) L.mouths.Add(new CaveSolid.Mouth { pos = site.localPos, rot = site.localRot });
        foreach (var (a, b, openAir) in net.legs)
        {
            var na = net.nodes[a]; var nb = net.nodes[b];
            if (na.core || nb.core) continue;       // the core is a room; legs end on its surface
            bool spoke = Mathf.Min(na.p.magnitude, nb.p.magnitude) < CoreCavernRadius + 8f;
            L.segments.Add(new CaveSolid.Segment { a = na.p, b = nb.p, ra = na.r, rb = nb.r, wa = na.w, wb = nb.w, ha = na.h, hb = nb.h, openAir = openAir, noFeatures = spoke });
        }
        foreach (var rm in net.rooms)
            L.rooms.Add(new CaveSolid.Room { centre = net.nodes[rm.node].p, radius = rm.r, w = rm.w, h = rm.h });
        var st = L.style;
        float k150 = Mathf.Clamp(net.length / 150f, 1f, 16f);
        // Roof features only + columns: floors stay clear for walking and,
        // later, mobs (Sam: "little knubs coming up out of the ground").
        st.stalactites = Mathf.RoundToInt(28 * k150);
        st.stalagmites = 0;
        st.columns = Mathf.RoundToInt(5 * k150);
        st.boulders = 0;
        st.blocks = 0;
        st.rubble = 0;
        st.cellSize = 0.7f;            // 0.5 was ~10 M samples for the whole moon; this is ~3.6 M
        return L;
    }

    // ── Install ──────────────────────────────────────────────────────────────

    static GameObject _holder;
    static Renderer[] _hidden;
    static CelestialBody _moon;
    static double _startedAt;

    [MenuItem("Tools/Cave/Install Moon Caves")]
    public static void Install()
    {
        if (Application.isPlaying) { Debug.LogError("[MoonCaves] Not in play mode."); return; }
        if (_holder != null) { Debug.LogWarning("[MoonCaves] Already running."); return; }

        _moon = null;
        foreach (var cb in Object.FindObjectsOfType<CelestialBody>(true))
            if (cb.bodyName == MoonName) { _moon = cb; break; }
        if (_moon == null) { Debug.LogError($"[MoonCaves] No body named '{MoonName}' in the open scene."); return; }

        var placeholder = _moon.GetComponentInChildren<BodyPlaceholder>(true);
        if (placeholder == null || placeholder.bodySettings == null) { Debug.LogError("[MoonCaves] The moon has no BodyPlaceholder with bodySettings."); return; }
        var spawner = Object.FindObjectOfType<SolarSystemSpawner>();
        if (spawner == null || spawner.resolutionSettings == null) { Debug.LogError("[MoonCaves] No SolarSystemSpawner with resolutionSettings in the scene."); return; }

        foreach (var name in new[] { TunnelRigName, "Cave_Moon_A", "Cave_Moon_B", "Cave_Moon_C" })
        {
            var t = _moon.transform.Find(name);
            if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); Debug.Log($"[MoonCaves] Removed '{name}' from the moon (Undo-able)."); }
        }

        _hidden = placeholder.GetComponentsInChildren<Renderer>(true);
        foreach (var r in _hidden) r.enabled = false;
        _holder = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave, layer = _moon.gameObject.layer };
        var tr = _holder.transform;
        tr.SetParent(_moon.transform, false);
        tr.localRotation = Quaternion.identity;
        tr.localPosition = Vector3.zero;
        tr.localScale = Vector3.one * _moon.radius;
        var generator = _holder.AddComponent<CelestialBodyGenerator>();
        generator.resolutionSettings = spawner.resolutionSettings;
        generator.body = placeholder.bodySettings;
        generator.previewMode = CelestialBodyGenerator.PreviewMode.LOD0;
        generator.OnShapeSettingChanged();
        EditorApplication.QueuePlayerLoopUpdate();
        _startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Debug.Log("[MoonCaves] Generating the moon's LOD0 terrain preview, then building the network…");
    }

    static void Poll()
    {
        if (_holder == null) { Finish(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        var terrainT = _holder.transform.Find("Terrain Mesh");
        var mf = terrainT != null ? terrainT.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
        {
            var gen = _holder.GetComponent<CelestialBodyGenerator>();
            if (gen != null && !EditorApplication.isCompiling)
            {
                var update = typeof(CelestialBodyGenerator).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (update != null) { try { update.Invoke(gen, null); } catch (System.Exception e) { Debug.LogWarning("[MoonCaves] generator tick threw: " + e.InnerException); } }
            }
            terrainT = _holder.transform.Find("Terrain Mesh");
            mf = terrainT != null ? terrainT.GetComponent<MeshFilter>() : null;
        }
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
        {
            if (EditorApplication.timeSinceStartup - _startedAt > TimeoutSeconds)
            {
                Debug.LogError("[MoonCaves] The terrain preview never appeared. Nothing built.");
                Finish();
            }
            return;
        }
        try { InstallAll(terrainT, mf); }
        catch (System.Exception e) { Debug.LogError("[MoonCaves] Install failed: " + e); }
        Finish();
    }

    static void Finish()
    {
        EditorApplication.update -= Poll;
        if (_holder != null) Object.DestroyImmediate(_holder);
        _holder = null;
        if (_hidden != null) foreach (var r in _hidden) if (r != null) r.enabled = true;
        _hidden = null;
    }

    // ── Terrain as radius-per-direction ──────────────────────────────────────

    const int Lat = 90, Lon = 180;
    static Dictionary<int, List<int>> _bins;
    static Vector3[] _tv; static List<Vector4> _tuv;

    static void BuildTerrainLookup(Mesh terrain)
    {
        _tv = terrain.vertices;
        _tuv = new List<Vector4>(); terrain.GetUVs(0, _tuv);
        _bins = new Dictionary<int, List<int>>();
        for (int i = 0; i < _tv.Length; i++)
        {
            if (_tv[i].sqrMagnitude < 1e-6f) continue;
            Vector3 d = _tv[i].normalized;
            int la = Mathf.Clamp((int)((Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) + Mathf.PI * 0.5f) / Mathf.PI * Lat), 0, Lat - 1);
            int lo = Mathf.Clamp((int)((Mathf.Atan2(d.z, d.x) + Mathf.PI) / (2f * Mathf.PI) * Lon), 0, Lon - 1);
            int b = la * Lon + lo;
            if (!_bins.TryGetValue(b, out var l)) _bins[b] = l = new List<int>();
            l.Add(i);
        }
    }

    static int NearestTerrainVertex(Vector3 d)
    {
        int la = Mathf.Clamp((int)((Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) + Mathf.PI * 0.5f) / Mathf.PI * Lat), 0, Lat - 1);
        int lo = Mathf.Clamp((int)((Mathf.Atan2(d.z, d.x) + Mathf.PI) / (2f * Mathf.PI) * Lon), 0, Lon - 1);
        int best = -1; float bestDot = -2f;
        for (int dl = -1; dl <= 1; dl++)
            for (int dn = -1; dn <= 1; dn++)
            {
                int l2 = la + dl; if (l2 < 0 || l2 >= Lat) continue;
                int n2 = ((lo + dn) % Lon + Lon) % Lon;
                if (!_bins.TryGetValue(l2 * Lon + n2, out var list)) continue;
                foreach (int ti in list)
                {
                    float dot = Vector3.Dot(d, _tv[ti].normalized);
                    if (dot > bestDot) { bestDot = dot; best = ti; }
                }
            }
        return best;
    }

    const int TLat = 360, TLon = 720;        // 0.5° cells ≈ 0.45 m at the surface
    static float[] _radiusTable;

    static void BuildRadiusTable()
    {
        _radiusTable = new float[TLat * TLon];
        for (int la = 0; la < TLat; la++)
            for (int lo = 0; lo < TLon; lo++)
            {
                float lat = ((la + 0.5f) / TLat - 0.5f) * Mathf.PI;
                float lon = ((lo + 0.5f) / TLon) * 2f * Mathf.PI - Mathf.PI;
                Vector3 d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                int i = NearestTerrainVertex(d);
                _radiusTable[la * TLon + lo] = (i >= 0 ? _tv[i].magnitude : 1f) * _moon.radius;
            }
    }

    static float TerrainRadius(Vector3 dir)
    {
        if (_radiusTable == null) BuildRadiusTable();
        int la = Mathf.Clamp((int)((Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI + 0.5f) * TLat), 0, TLat - 1);
        int lo = Mathf.Clamp((int)((Mathf.Atan2(dir.z, dir.x) + Mathf.PI) / (2f * Mathf.PI) * TLon), 0, TLon - 1);
        return _radiusTable[la * TLon + lo];
    }

    static void InstallAll(Transform terrainT, MeshFilter mf)
    {
        var mc = terrainT.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;
        BuildTerrainLookup(mf.sharedMesh);
        _radiusTable = null;
        Physics.SyncTransforms();
        try { System.IO.File.WriteAllText(LogPath, ""); } catch { }
        Log("start — whole-moon network");
        CaveSolid.Progress = Log;
        CaveSolid.TimeLimitSeconds = 720;   // 12 minutes, then it aborts itself
        try
        {
            var moonT = _moon.transform;
            var log = new System.Text.StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float R = _moon.radius;
            Log("terrain lookup ready");

            ChooseCraterSites(mf.sharedMesh, log);
            foreach (var site in Sites)
            {
                Vector3 dirW = moonT.TransformDirection(site.dir);
                var ray = new Ray(moonT.position + dirW * (R * 3f), -dirW);
                if (!mc.Raycast(ray, out RaycastHit hit, R * 4f)) { Debug.LogError($"[MoonCaves] Site {site.name}: no terrain under {site.dir}."); return; }
                site.localPos = moonT.InverseTransformPoint(hit.point);
                site.R = site.localPos.magnitude;
                Vector3 up = site.localPos.normalized;
                Vector3 east = Vector3.Cross(Vector3.up, up).normalized;
                if (east.sqrMagnitude < 0.01f) east = Vector3.Cross(Vector3.right, up).normalized;
                site.localRot = Quaternion.LookRotation(east, up);
            }

            var ground = new CaveSolid.SphereGround { centre = Vector3.zero, radiusOfDirection = TerrainRadius };
            BuildRadiusTable();
            Log("radius table built; growing network");
            var net = GrowMoon(R, TerrainRadius, Params, log);
            var L = BuildLayout(net, R, ground);
            double tGrow = clock.Elapsed.TotalSeconds;
            Log($"network grown in {tGrow:0}s: {net.legs.Count} legs, {net.length:0} m; building the solid");

            var res = CaveSolid.Build(L);
            Log("solid built; checks");
            // Prove the core tunnels are OPEN in the rock, not just listed.
            foreach (var sg in L.segments)
            {
                if (Mathf.Min(sg.a.magnitude, sg.b.magnitude) > CoreCavernRadius) continue;
                float len = (sg.b - sg.a).magnitude; int steps = Mathf.CeilToInt(len / 0.5f); float blockedAt = -1f;
                for (int i = 0; i <= steps && blockedAt < 0f; i++)
                {
                    Vector3 pnt = Vector3.Lerp(sg.a, sg.b, i / (float)steps);
                    if (res.SampleField(pnt) < 0f) { blockedAt = i * 0.5f; log.AppendLine("[MoonCaves]   why: " + res.Explain(pnt)); }
                }
                log.AppendLine(blockedAt < 0f ? $"[MoonCaves] core tunnel {sg.a.magnitude:0.0}→{sg.b.magnitude:0.0} m from centre: OPEN"
                                              : $"[MoonCaves] core tunnel {sg.a.magnitude:0.0}→{sg.b.magnitude:0.0} m from centre: BLOCKED {blockedAt:0.0} m in");
                if (blockedAt >= 0f) res.ok = false;
            }
            log.AppendLine($"[MoonCaves] timing: network at {tGrow:0}s, solid at {clock.Elapsed.TotalSeconds:0}s.");
            log.AppendLine($"[MoonCaves] features: {res.featureReport}; {res.islandsDropped} floating triangles dropped.");
            if (!res.ok) { log.AppendLine("[MoonCaves] FAILED: " + res.failure); Debug.Log(log.ToString()); Debug.LogError("[MoonCaves] Not written."); return; }
            int tris = 0; foreach (var pm in res.pieces) tris += pm.triangles.Length / 3;
            log.AppendLine($"[MoonCaves] closed solid, volume {res.signedVolume:0} m³, {res.nonManifold} non-manifold (cosmetic); {res.mouthReport}; {res.roofReport}.");
            log.AppendLine($"[MoonCaves] {res.trisFull} tris → {tris} after trimming, in {res.pieces.Count} pieces (+ mouth skin); mean sky exposure {res.exposureMean:0.00}; {res.seconds:0.0} s.");
            for (int i = 0; i < L.mouths.Count; i++)
                log.AppendLine($"[MoonCaves] mouth {Sites[i].name}: {Sites[i].craterNote}; hole r={L.mouths[i].holeRadius:0.00}.");

            bool ok = true;
            if (!Walkable(L, out string walk)) { log.AppendLine("[MoonCaves] FAILED walkability: " + walk); ok = false; }
            else log.AppendLine("[MoonCaves] walkable: " + walk);
            float baseD = float.MaxValue;
            foreach (var sg in L.segments) baseD = Mathf.Min(baseD, SegPointDist(sg.a, sg.b, MoonBaseLocal) - Mathf.Max(sg.ra, sg.rb) - 3f - MoonBaseRadius);
            foreach (var rm in L.rooms) baseD = Mathf.Min(baseD, (rm.centre - MoonBaseLocal).magnitude - rm.radius * Mathf.Max(rm.w, rm.h) - 3f - MoonBaseRadius);
            log.AppendLine($"[MoonCaves] nearest rock to the moon base: {baseD:0.0} m.");
            if (baseD < 1f) { log.AppendLine("[MoonCaves] FAILED: under the moon base."); ok = false; }   // on top of a 26 m sphere + 3 m walls
            log.AppendLine($"[MoonCaves] All checks done at {clock.Elapsed.TotalSeconds:0}s.");
            Debug.Log(log.ToString());
            if (!ok) { Debug.LogError("[MoonCaves] Not written — fix the failures above and re-run."); return; }

            Log("writing assets");
            WriteAndPlace(L, res);
            Log("saving");
            AssetDatabase.SaveAssets();
            Log("done");
            EditorSceneManager.MarkSceneDirty(_moon.gameObject.scene);
            Debug.Log("[MoonCaves] The moon cave network is installed. SAVE THE SCENE to keep it. Press Play: CaveHoleBinder punches the three mouths on load.");
        }
        catch (System.Exception e)
        {
            Log("ABORTED: " + e.Message);
            throw;
        }
        finally
        {
            CaveSolid.Progress = null;
            Object.DestroyImmediate(mc);
        }
    }

    // ── Craters ──────────────────────────────────────────────────────────────

    static void ChooseCraterSites(Mesh terrain, System.Text.StringBuilder log)
    {
        var verts = terrain.vertices;
        if (verts == null || verts.Length < 1000) { log.AppendLine("[MoonCaves] Crater search skipped — no terrain vertices."); return; }
        const int CLat = 60, CLon = 120;
        var sum = new double[CLat * CLon]; var cnt = new int[CLat * CLon];
        var sumDir = new Vector3[CLat * CLon];
        int Cell(Vector3 d)
        {
            int la = Mathf.Clamp((int)((Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) + Mathf.PI * 0.5f) / Mathf.PI * CLat), 0, CLat - 1);
            int lo = Mathf.Clamp((int)((Mathf.Atan2(d.z, d.x) + Mathf.PI) / (2f * Mathf.PI) * CLon), 0, CLon - 1);
            return la * CLon + lo;
        }
        for (int i = 0; i < verts.Length; i++)
        {
            float r = verts[i].magnitude;
            if (r < 1e-4f) continue;
            int c = Cell(verts[i] / r);
            sum[c] += r; cnt[c]++; sumDir[c] += verts[i] / r;
        }
        float Mean(int c) => cnt[c] > 0 ? (float)(sum[c] / cnt[c]) : float.NaN;
        var score = new float[CLat * CLon];
        for (int la = 0; la < CLat; la++)
            for (int lo = 0; lo < CLon; lo++)
            {
                int c = la * CLon + lo;
                float m = Mean(c);
                if (float.IsNaN(m)) { score[c] = float.NegativeInfinity; continue; }
                double ns = 0; int nc = 0;
                for (int dl = -6; dl <= 6; dl++)
                    for (int dn = -6; dn <= 6; dn++)
                    {
                        if (dl == 0 && dn == 0) continue;
                        int l2 = la + dl; if (l2 < 0 || l2 >= CLat) continue;
                        int n2 = ((lo + dn) % CLon + CLon) % CLon;
                        int c2 = l2 * CLon + n2;
                        if (cnt[c2] == 0) continue;
                        ns += sum[c2]; nc += cnt[c2];
                    }
                score[c] = nc == 0 ? float.NegativeInfinity : (float)(ns / nc) - m;
            }
        foreach (var site in Sites)
        {
            float siteAz = Mathf.Atan2(site.dir.z, site.dir.x) * Mathf.Rad2Deg;
            int best = -1; float bestScore = float.NegativeInfinity;
            for (int la = 0; la < CLat; la++)
                for (int lo = 0; lo < CLon; lo++)
                {
                    int c = la * CLon + lo;
                    if (cnt[c] == 0 || float.IsNegativeInfinity(score[c])) continue;
                    Vector3 d = sumDir[c].normalized;
                    if (d.y < 0.08f || d.y > 0.5f) continue;
                    float az = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
                    float dev = Mathf.Abs(Mathf.DeltaAngle(az, siteAz));
                    if (dev > 40f) continue;
                    float sc = score[c] - dev * 0.0004f;
                    if (sc > bestScore) { bestScore = sc; best = c; }
                }
            if (best < 0) { site.craterNote = "no crater found, kept the default site"; continue; }
            site.dir = sumDir[best].normalized;
            site.craterNote = $"crater floor {bestScore * 51f:0.0} m below its surroundings at dir {site.dir}";
        }
        log.AppendLine("[MoonCaves] Crater search: " + string.Join("; ", System.Array.ConvertAll(Sites, s => s.name + ": " + s.craterNote)));
    }

    // ── Checks ───────────────────────────────────────────────────────────────

    static bool Walkable(CaveSolid.Layout L, out string report)
    {
        var nodes = new List<Vector3>();
        int Node(Vector3 p)
        {
            for (int i = 0; i < nodes.Count; i++) if ((nodes[i] - p).sqrMagnitude < 0.25f) return i;
            nodes.Add(p); return nodes.Count - 1;
        }
        var adj = new List<List<int>>();
        void Ensure() { while (adj.Count < nodes.Count) adj.Add(new List<int>()); }
        void Link(int a, int b) { Ensure(); adj[a].Add(b); adj[b].Add(a); }
        float steepest = 0f;
        var sources = new List<int>();
        foreach (var s in L.segments)
        {
            int a = Node(s.a), b = Node(s.b);
            if (s.openAir) sources.Add(a);
            float dd = Mathf.Abs(s.a.magnitude - s.b.magnitude);
            float len = (s.b - s.a).magnitude;
            float run = Mathf.Sqrt(Mathf.Max(0f, len * len - dd * dd));
            float slope = Mathf.Atan2(dd, Mathf.Max(0.01f, run)) * Mathf.Rad2Deg;
            bool intoCore = Mathf.Min(s.a.magnitude, s.b.magnitude) < CoreCavernRadius + 1.5f;
            if (!intoCore) steepest = Mathf.Max(steepest, slope);
            if (slope <= (intoCore ? 42f : MaxWalkSlopeDeg)) Link(a, b); else Ensure();
        }
        var roomNodes = new List<int>();
        foreach (var r in L.rooms)
        {
            int rn = Node(r.centre);
            roomNodes.Add(rn); Ensure();
            for (int i = 0; i < nodes.Count; i++)
                if (i != rn && (nodes[i] - r.centre).magnitude <= r.radius * Mathf.Max(r.w, 1f) + 1.0f) Link(rn, i);
        }
        Ensure();
        var seen = new bool[nodes.Count];
        var q = new Queue<int>();
        foreach (int sIdx in sources) { seen[sIdx] = true; q.Enqueue(sIdx); }
        while (q.Count > 0) { int n = q.Dequeue(); foreach (int m in adj[n]) if (!seen[m]) { seen[m] = true; q.Enqueue(m); } }
        int unreachedRooms = 0, unreachedNodes = 0;
        foreach (int rn in roomNodes) if (!seen[rn]) unreachedRooms++;
        for (int i = 0; i < nodes.Count; i++) if (!seen[i]) unreachedNodes++;
        int coreIdx = Node(Vector3.zero);
        bool coreReached = coreIdx < seen.Length && seen[coreIdx];
        report = $"{L.rooms.Count} caverns, {nodes.Count} junctions, steepest walking leg {steepest:0.0}° (limit {MaxWalkSlopeDeg}°), core {(coreReached ? "reachable" : "NOT reachable")}"
               + (unreachedRooms + unreachedNodes == 0 ? ", everything reachable from the mouths" : $", {unreachedRooms} cavern(s) and {unreachedNodes} junction(s) NOT reachable");
        return unreachedRooms + unreachedNodes == 0 && coreReached;
    }

    // ── Assets + scene ───────────────────────────────────────────────────────

    static void WriteAndPlace(CaveSolid.Layout L, CaveSolid.Result res)
    {
        if (!AssetDatabase.IsValidFolder("Assets/1 - samsPrefabs/Cave")) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "Cave");
        if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs/Cave", "Moon");

        foreach (var n in new[] { "A", "B", "C" })
            foreach (var suffix in new[] { ".prefab", "_Rock.asset", "_Mouth.asset" })
            {
                string path = $"{OutFolder}/Cave_Moon_{n}{suffix}";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            }

        string prefabPath = $"{OutFolder}/{PrefabName}.prefab";
        bool patched = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
        GameObject root = patched ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(PrefabName);
        try
        {
            Configure(root, L, res);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            if (patched) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var existing = _moon.transform.Find(PrefabName);
        GameObject inst;
        if (existing != null) inst = existing.gameObject;
        else
        {
            inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _moon.gameObject.scene);
            inst.transform.SetParent(_moon.transform, false);
            Undo.RegisterCreatedObjectUndo(inst, "Install moon cave network");
        }
        Undo.RecordObject(inst.transform, "Place moon cave network");
        inst.name = PrefabName;
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;
        inst.SetActive(true);
        EditorUtility.SetDirty(inst);
        Debug.Log($"[MoonCaves] {(patched ? "Patched" : "Wrote")} {prefabPath} and placed '{PrefabName}' under the moon.");
    }

    static void Configure(GameObject root, CaveSolid.Layout L, CaveSolid.Result res)
    {
        float Rgen = _moon.radius;
        Ensure<CaveHoleBinder>(root);
        Ensure<GrassBlocker>(root);
        Ensure<NoGrassVolume>(root).radius = 0f;
        var vol = Ensure<CaveVolume>(root);
        FillVolume(vol, L);
        var seeder = Ensure<CaveCrystalSeeder>(root);
        seeder.crystalCount = Mathf.Clamp(L.rooms.Count * 3, 30, 90);
        seeder.seed = 90210;
        seeder.deepFraction = 0.3f;
        seeder.cavernsOnly = true;
        seeder.glowLightEvery = 2;
        seeder.glowMaterial = CaveRockTextures.GetCrystalGlowMaterial();
        var zone = Ensure<ZeroGZone>(root);
        zone.radius = CoreCavernRadius;
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);

        var moonMat = CaveRockTextures.GetMoonRockMaterial();
        var sync = Ensure<MoonSkinMaterialSync>(root);
        sync.template = moonMat;

        var wanted = new HashSet<string>();
        void Piece(string name, Mesh caveLocal, string assetSuffix)
        {
            wanted.Add(name);
            var tr = root.transform.Find(name);
            GameObject go = tr != null ? tr.gameObject : new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.layer = LayerMask.NameToLayer("Body");
            go.transform.localRotation = Quaternion.identity;
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * Rgen;
            Mesh gm = ToGeneratorSpace(Rgen, caveLocal, name);
            string path = $"{OutFolder}/{PrefabName}_{assetSuffix}.asset";
            SaveMesh(gm, path);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            Ensure<MeshFilter>(go).sharedMesh = saved;
            Ensure<MeshRenderer>(go).sharedMaterial = moonMat;
            var mcol = Ensure<MeshCollider>(go);
            mcol.convex = false;
            mcol.sharedMesh = null;
            mcol.sharedMesh = saved;
        }
        Piece("Cave_MouthSkin", res.mouthSkin, "Mouth");
        for (int i = 0; i < res.pieces.Count; i++) Piece("Cave_Rock_" + i, res.pieces[i], "Rock_" + i);
        var stale = new List<GameObject>();
        foreach (Transform c in root.transform) if ((c.name.StartsWith("Cave_Rock") || c.name == "Cave_MouthSkin") && !wanted.Contains(c.name)) stale.Add(c.gameObject);
        foreach (var g in stale) Object.DestroyImmediate(g);
        for (int i = res.pieces.Count; i < 8; i++)
        {
            string path = $"{OutFolder}/{PrefabName}_Rock_{i}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        }

        var holeNames = new HashSet<string>();
        for (int i = 0; i < L.mouths.Count; i++)
        {
            var m = L.mouths[i];
            string name = "TerrainHole - Mouth " + Sites[i].name;
            holeNames.Add(name);
            var ht = root.transform.Find(name);
            GameObject hole;
            if (ht != null) hole = ht.gameObject;
            else
            {
                hole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hole.name = name;
                hole.transform.SetParent(root.transform, false);
                var c = hole.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            hole.transform.localPosition = m.holeCentre - m.Axis * 0.5f;
            hole.transform.localRotation = m.rot;
            hole.transform.localScale = new Vector3(m.holeRadius * 2f, 5f, m.holeRadius * 2f);
            var th = Ensure<TerrainHole>(hole);
            th.shape = TerrainHole.Shape.Cylinder;
            th.hideAtRuntime = true;
        }
        var staleHoles = new List<GameObject>();
        foreach (Transform c in root.transform) if (c.name.StartsWith("TerrainHole") && !holeNames.Contains(c.name)) staleHoles.Add(c.gameObject);
        foreach (var g in staleHoles) Object.DestroyImmediate(g);
        var staleLights = new List<GameObject>();
        foreach (Transform c in root.transform) if (c.name.StartsWith("CaveLight_")) staleLights.Add(c.gameObject);
        foreach (var g in staleLights) Object.DestroyImmediate(g);
    }

    static Mesh ToGeneratorSpace(float Rgen, Mesh src, string name)
    {
        var v = src.vertices; var n = src.normals; var c = src.colors;
        var outV = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) outV[i] = v[i] / Rgen;
        var uv = new List<Vector4>(v.Length);
        for (int i = 0; i < outV.Length; i++)
        {
            int best = outV[i].sqrMagnitude > 1e-8f ? NearestTerrainVertex(outV[i].normalized) : -1;
            uv.Add(best >= 0 && best < _tuv.Count ? _tuv[best] : Vector4.zero);
        }
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = outV;
        mesh.normals = n;
        mesh.colors = c;
        mesh.SetUVs(0, uv);
        mesh.triangles = src.triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    static void FillVolume(CaveVolume volume, CaveSolid.Layout L)
    {
        var a = new List<Vector3>(); var b = new List<Vector3>(); var r = new List<float>();
        foreach (var s in L.segments)
        {
            a.Add(s.a); b.Add(s.b);
            r.Add(Mathf.Max(s.ra * Mathf.Max(s.wa, s.ha), s.rb * Mathf.Max(s.wb, s.hb)));
        }
        foreach (var room in L.rooms) { a.Add(room.centre); b.Add(room.centre); r.Add(room.radius * Mathf.Max(room.w, room.h)); }
        volume.capsuleA = a.ToArray(); volume.capsuleB = b.ToArray(); volume.capsuleR = r.ToArray();
        volume.radiusPadding = 1.25f;
        volume.suppressOcean = false;
        volume.mouthBubbleRadius = 0f;
        volume.oceanCutoutPadding = 1.15f;
        volume.affectsOcean = false;
        float[] segDist = CaveSolid.SegmentPathDistances(L);
        var dist = new List<float>(segDist);
        foreach (var room in L.rooms)
        {
            float best = float.MaxValue; float rd = 0f;
            for (int i = 0; i < L.segments.Count; i++)
            {
                float d = Mathf.Min((L.segments[i].a - room.centre).magnitude, (L.segments[i].b - room.centre).magnitude);
                if (d < best) { best = d; rd = segDist[i]; }
            }
            dist.Add(rd);
        }
        volume.capsuleDist = dist.ToArray();
    }

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return; }
        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.vertices = mesh.vertices;
        existing.normals = mesh.normals;
        existing.colors = mesh.colors;
        var uv4 = new List<Vector4>(); mesh.GetUVs(0, uv4);
        existing.SetUVs(0, uv4);
        existing.tangents = mesh.tangents;
        existing.triangles = mesh.triangles;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
    }
}
