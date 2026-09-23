using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools ▸ Cave ▸ Install Moon Caves
///
/// Puts three explorable caves on Constant Companion (the moon) and removes
/// the tube that used to run through it. Sam, 2026-09-22: three normal caves,
/// different layouts, splitting tunnels and caverns, rocky interiors, none
/// overlapping. Spec: docs/superpowers/specs/2026-09-22-moon-caves-design.md.
///
/// WHAT IT DOES, IN ORDER
///   1. Deletes `Constant Companion/Tunnel Rig` (with Undo).
///   2. Builds a hidden LOD0 preview of the moon's REAL terrain, exactly the way
///      TutorialGrassBake does (a DontSave generator with the moon's settings,
///      ticked by reflection — the generator itself is not touched).
///   3. For each of three sites: finds the surface point, samples the terrain
///      into a cave-local heightmap (±45 m, 0.5 m), builds the cave solid with
///      that ground, and runs every check in CaveSolid.
///   4. Cross-cave checks: no two caves within reach of each other, nothing
///      near the core or the moon base, every room walkable from the mouth.
///   5. Only if EVERYTHING passed: writes the mesh + prefab assets and places
///      (or updates) the three instances under the moon. Marks the scene dirty
///      and does not save — Sam saves.
///
/// Re-running regenerates in place (same asset GUIDs, instances keep their
/// links). Edit the layouts below and re-run; nothing else needs redoing.
///
/// COORDINATES
/// Layout points are (x, d, s): x metres to the side, d metres BELOW the
/// mouth-level sphere (negative = above ground), s metres of ARC along the
/// tunnel's forward direction. The moon is only 50 m in radius, so 40 m along
/// the surface is 46° round it; authoring in arc coordinates keeps "level"
/// meaning level to the local gravity everywhere in the cave.
/// </summary>
public static class MoonCaveInstaller
{
    const string MoonName = "Constant Companion";
    const string TunnelRigName = "Tunnel Rig";
    const string OutFolder = CaveRockTextures.Folder;
    const string PreviewName = "Body Generator (Moon Cave Preview)";
    const float HeightmapHalf = 45f;
    const float HeightmapCell = 0.5f;
    const float MaxWalkSlopeDeg = 28f;   // floors flatten up to 30°; A's entrance ramp measures 27.4°
    const float MinCaveGap = 1f;           // spare rock beyond both caves' 3 m walls: the two solids must simply never touch
    const float MinCoreRadius = 12f;
    const double TimeoutSeconds = 180.0;

    // Moon base, moon-local (measured 2026-09-22): bounds centre and a sphere
    // that comfortably contains it.
    static readonly Vector3 MoonBaseLocal = new Vector3(7.03f, -59.17f, 10.16f);
    const float MoonBaseRadius = 26f;
    const float MoonBaseGap = 15f;

    class Site
    {
        public string name;          // A / B / C
        public string title;
        public Vector3 dir;          // moon-local unit direction of the mouth
        public CaveSolid.Recipe recipe;
        public int crystals;
        public System.Func<Site, CaveSolid.Layout> build;

        // filled during install
        public Vector3 localPos; public Quaternion localRot; public float R;
        public CaveSolid.Layout layout; public CaveSolid.Result result;
        public List<(Vector3 c, float r)> lightRooms = new List<(Vector3, float)>();
    }

    static readonly Site[] Sites =
    {
        new Site { name = "A", title = "the Warren",  dir = new Vector3( 0.94f, 0.34f,  0.00f).normalized, recipe = CaveSolid.Recipe.Strata,    crystals = 130, build = BuildWarren },
        new Site { name = "B", title = "the Descent", dir = new Vector3(-0.47f, 0.34f,  0.81f).normalized, recipe = CaveSolid.Recipe.Dripstone, crystals = 140, build = BuildDescent },
        new Site { name = "C", title = "the Hall",    dir = new Vector3(-0.47f, 0.34f, -0.81f).normalized, recipe = CaveSolid.Recipe.Collapse,  crystals = 120, build = BuildHall },
    };

    // ── Layouts: a grown tunnel network per cave ─────────────────────────────
    //
    // Sam (2026-09-22, after the first playtest): "use up much more of the
    // inside of the moon, make the tunnels longer and sprawl out throughout the
    // entire inside… I want players to be able to go so deep they get lost."
    // So the hand-drawn layouts became a generator: from a fixed entrance ramp
    // each cave grows ~40 legs by random walk — branching, descending, looping
    // back on itself — inside its own third of the moon, keeping every leg
    // walkable and every passage clear of every other one. Deterministic per
    // seed: the same cave every run.

    class NetParams
    {
        public int seed = 1;
        public int targetLegs = 42, loops = 6;
        public float turnMax = 75f;                 // degrees either side per leg
        public float lenMin = 9f, lenMax = 14f;     // metres of arc per leg
        public float descMin = -0.5f, descMax = 3.5f; // metres of depth gained per leg
        public float branchKeep = 0.55f;            // chance a node stays open for another child
        public float roomProb = 0.45f, roomMin = 4f, roomMax = 7f, roomW = 1.15f, roomH = 1.0f;
        public float rMin = 2.6f, rMax = 3.6f, wMin = 1.2f, wMax = 1.5f, hMin = 0.85f, hMax = 1.05f;
        public float minDepth = 9f, maxDepth = 30f;          // 30: keeps 12 m clear of the core with 3 m walls
        public float alongMax = 78f, xMin = -34f, xMax = 34f, sMin = -60f;
        public float sectorHalfDeg = 60f;                    // each cave owns a 120° wedge of the moon (about its axis)...
        public float sectorMarginM = 10f;                    // ...minus this much rock (room + wall + half the gap) at the wedge edge, so the wedge narrows with depth
        public float gap = 2.5f;                    // metres of rock kept between separate passages (walls are 3 m thick on top)
        public float maxSlopeDeg = 25f;
    }

    static readonly NetParams WarrenParams = new NetParams
    {
        seed = 11, targetLegs = 60, loops = 10, turnMax = 80f, lenMin = 8f, lenMax = 13f,
        descMin = -0.8f, descMax = 3.0f, branchKeep = 0.6f, roomProb = 0.45f, roomMin = 4f, roomMax = 6.5f,
    };
    static readonly NetParams DescentParams = new NetParams
    {
        seed = 22, targetLegs = 55, loops = 6, turnMax = 100f, lenMin = 9f, lenMax = 14f,
        descMin = 0.5f, descMax = 4.2f, branchKeep = 0.45f, roomProb = 0.42f, roomMin = 4.5f, roomMax = 7.5f, roomH = 1.15f,
    };
    static readonly NetParams HallParams = new NetParams
    {
        seed = 33, targetLegs = 50, loops = 7, turnMax = 75f, lenMin = 10f, lenMax = 15f,
        descMin = -0.5f, descMax = 3.2f, branchKeep = 0.5f, roomProb = 0.4f, roomMin = 5f, roomMax = 8f, roomW = 1.3f, roomH = 0.85f,
        rMin = 3.2f, rMax = 4.0f, wMin = 1.5f, wMax = 1.8f, hMin = 0.8f, hMax = 0.9f,
    };

    struct P { public float x, d, s, r, w, h; public P(float x, float d, float s, float r, float w = 1f, float h = 1f) { this.x = x; this.d = d; this.s = s; this.r = r; this.w = w; this.h = h; } }

    /// (x lateral, d depth below the mouth sphere, s arc along) → cave-local.
    /// Angular position is the surface arc; deeper points sit on a smaller
    /// sphere, so the same arc is a shorter distance there. The generator
    /// measures real slopes and distances on the mapped points.
    static Vector3 Arc(float R, float x, float d, float s)
    {
        float along = Mathf.Sqrt(x * x + s * s);
        if (along < 1e-4f) return new Vector3(0f, -d, 0f);
        float theta = along / R;
        Vector3 t = new Vector3(x, 0f, s) / along;
        Vector3 dir = Vector3.up * Mathf.Cos(theta) + t * Mathf.Sin(theta);
        return dir * (R - d) - new Vector3(0f, R, 0f);
    }

    class Node { public float x, d, s, heading, r, w, h; public int children, fails; public List<int> links = new List<int>(); }

    class Net
    {
        public List<Node> nodes = new List<Node>();
        public List<(int a, int b)> legs = new List<(int, int)>();
        public List<(int node, float r, float w, float h)> rooms = new List<(int, float, float, float)>();
        public int loops; public float length; public float maxDepth;
    }

    static Net Grow(Site site, NetParams P)
    {
        float R = site.R;
        var rng = new System.Random(P.seed);
        float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var net = new Net();
        Vector3 Pos(Node n) => Arc(R, n.x, n.d, n.s);
        Matrix4x4 toMoon = Matrix4x4.TRS(site.localPos, site.localRot, Vector3.one);
        float mouthAz = Mathf.Atan2(site.localPos.z, site.localPos.x) * Mathf.Rad2Deg;
        bool Allowed(Node n)
        {
            if (n.d < P.minDepth || n.d > P.maxDepth) return false;
            if (n.s < P.sMin || n.x < P.xMin || n.x > P.xMax) return false;
            if (Mathf.Sqrt(n.x * n.x + n.s * n.s) > P.alongMax) return false;
            Vector3 m = toMoon.MultiplyPoint3x4(Pos(n));
            if (m.normalized.y < -0.25f) return false;         // stay off the moon-base hemisphere
            // Stay inside this cave's wedge of the moon (about the moon's own
            // axis), so three sprawling networks can never meet. The margin is
            // metres of rock, so in degrees it grows as the passage nears the
            // axis — deep caves keep further apart, and nothing crowds the pole.
            float az = Mathf.Atan2(m.z, m.x) * Mathf.Rad2Deg;
            float rho = Mathf.Sqrt(m.x * m.x + m.z * m.z);
            if (rho < P.sectorMarginM * 1.2f) return false;
            float marginDeg = Mathf.Asin(Mathf.Clamp01(P.sectorMarginM / rho)) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(az, mouthAz)) <= P.sectorHalfDeg - marginDeg;
        }
        bool SlopeOk(Node a, Node b)
        {
            float dd = Mathf.Abs(a.d - b.d);
            float len = (Pos(a) - Pos(b)).magnitude;
            float run = Mathf.Sqrt(Mathf.Max(0.01f, len * len - dd * dd));
            return Mathf.Atan2(dd, run) * Mathf.Rad2Deg <= P.maxSlopeDeg;
        }
        // Clearance of a leg (i→j, j may be -1 for a new node) against every
        // passage that is not within one hop of its endpoints. Legs that share
        // a junction with it meet there by definition — the smooth union
        // fillets that joint — so they are not "separate passages". The turn
        // limit keeps a leg from folding back onto the one it came from.
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
                var (a, b) = net.legs[k];
                if (Touches(a, i, j) || Touches(b, i, j)) continue;
                float rr = (net.nodes[a].r + net.nodes[b].r) * 0.5f;
                if (SegSegDist(pa, pb, Pos(net.nodes[a]), Pos(net.nodes[b])) - rr - rc < P.gap) return false;
            }
            for (int k = 0; k < net.rooms.Count; k++)
            {
                var rm = net.rooms[k];
                if (Touches(rm.node, i, j)) continue;
                if (SegPointDist(pa, pb, Pos(net.nodes[rm.node])) - rm.r * Mathf.Max(rm.w, rm.h) - rc < P.gap) return false;
            }
            for (int k = 0; k < net.nodes.Count; k++)
            {
                if (Touches(k, i, j)) continue;
                if (SegPointDist(pa, pb, Pos(net.nodes[k])) - net.nodes[k].r - rc < P.gap) return false;
            }
            // The far end must not land on top of a neighbour's passage either.
            if (j < 0)
                foreach (int nb in net.nodes[i].links)
                    if ((Pos(net.nodes[nb]) - pb).magnitude < P.gap + rc + net.nodes[nb].r) return false;
            return true;
        }
        int Add(Node n) { net.nodes.Add(n); return net.nodes.Count - 1; }
        void Link(int a, int b)
        {
            net.legs.Add((a, b));
            net.nodes[a].links.Add(b); net.nodes[b].links.Add(a);
            net.length += (Pos(net.nodes[a]) - Pos(net.nodes[b])).magnitude;
        }

        // Fixed entrance: a flush sinkhole ramp, 25° down, into the network.
        int n0 = Add(new Node { x = 0, d = -1.2f, s = -2.5f, r = 2.3f, w = 1.15f, h = 0.95f });
        int n1 = Add(new Node { x = 0, d = 1.2f, s = 2.5f, r = 2.3f, w = 1.15f, h = 0.95f });
        int n2 = Add(new Node { x = 0, d = 4.5f, s = 9.5f, r = 2.6f, w = 1.2f, h = 1.0f });
        int n3 = Add(new Node { x = 0, d = 7.5f, s = 16f, r = 3.0f, w = 1.3f, h = 1.0f });
        Link(n0, n1); Link(n1, n2); Link(n2, n3);
        // A first junction room so the cave opens up right after the ramp.
        int n4 = Add(new Node { x = 0, d = 10.5f, s = 23f, r = 3.2f, w = 1.3f, h = 1.0f, heading = 0f });
        Link(n3, n4);
        net.rooms.Add((n4, 5.5f, 1.2f, 1.0f));

        var frontier = new List<int> { n4 };
        int tries = 0;
        while (net.legs.Count < P.targetLegs + 4 && tries < 20000 && frontier.Count > 0)
        {
            tries++;
            // Prefer the newest open nodes so the cave sprawls outward, but keep
            // every open node available so side branches keep coming.
            int fi = (rng.NextDouble() < 0.7 && frontier.Count > 3)
                ? frontier[frontier.Count - 1 - rng.Next(Mathf.Min(6, frontier.Count))]
                : frontier[rng.Next(frontier.Count)];
            var f = net.nodes[fi];
            var c = new Node
            {
                // The first branches off the entrance room fan out in every
                // direction; after that each leg turns from its parent.
                heading = fi == n4 ? Rand(-150f, 150f) * Mathf.Deg2Rad
                                   : f.heading + Rand(-P.turnMax, P.turnMax) * Mathf.Deg2Rad,
                r = Rand(P.rMin, P.rMax), w = Rand(P.wMin, P.wMax), h = Rand(P.hMin, P.hMax),
            };
            float len = Rand(P.lenMin, P.lenMax);
            c.x = f.x + Mathf.Sin(c.heading) * len;
            c.s = f.s + Mathf.Cos(c.heading) * len;
            c.d = Mathf.Clamp(f.d + Rand(P.descMin, P.descMax), P.minDepth, P.maxDepth);
            bool ok = Allowed(c) && SlopeOk(f, c);
            if (ok)
            {
                Vector3 pa = Pos(f), pb = Pos(c);
                float rc = (f.r + c.r) * 0.5f;
                ok = Clear(pa, pb, rc, fi, -1);
            }
            if (!ok)
            {
                if (++f.fails > 120) frontier.Remove(fi);
                continue;
            }
            int ci = Add(c);
            Link(fi, ci);
            f.children++;
            if (f.children >= 3 || (f.children >= 2 && (float)rng.NextDouble() > P.branchKeep)) frontier.Remove(fi);
            frontier.Add(ci);
            net.maxDepth = Mathf.Max(net.maxDepth, c.d);

            if ((float)rng.NextDouble() < P.roomProb)
            {
                float rr = Rand(P.roomMin, P.roomMax);
                // A room must not eat a neighbouring passage.
                bool roomOk = true;
                Vector3 pc = Pos(c);
                // Same rule as legs: passages that meet this junction are part
                // of the room, not obstacles to it.
                for (int k = 0; k < net.legs.Count && roomOk; k++)
                {
                    var (a, b) = net.legs[k];
                    if (Touches(a, ci, -1) || Touches(b, ci, -1)) continue;
                    float lr = (net.nodes[a].r + net.nodes[b].r) * 0.5f;
                    if (SegPointDist(Pos(net.nodes[a]), Pos(net.nodes[b]), pc) - lr - rr < P.gap) roomOk = false;
                }
                for (int k = 0; k < net.rooms.Count && roomOk; k++)
                {
                    var rm = net.rooms[k];
                    if (Touches(rm.node, ci, -1)) continue;
                    if ((Pos(net.nodes[rm.node]) - pc).magnitude - rm.r - rr < P.gap) roomOk = false;
                }
                if (roomOk) net.rooms.Add((ci, rr, P.roomW, P.roomH));
            }
        }

        // Loops: join two non-adjacent nodes that happen to be close, so the
        // cave doubles back on itself and the way out is not obvious.
        for (int t = 0; t < 4000 && net.loops < P.loops; t++)
        {
            int i = 5 + rng.Next(Mathf.Max(1, net.nodes.Count - 5)), j = 5 + rng.Next(Mathf.Max(1, net.nodes.Count - 5));
            if (i >= net.nodes.Count || j >= net.nodes.Count || i == j) continue;
            var a = net.nodes[i]; var b = net.nodes[j];
            if (a.links.Contains(j)) continue;
            // not already one hop apart via a shared neighbour
            bool near = false; foreach (int l in a.links) if (b.links.Contains(l)) { near = true; break; }
            if (near) continue;
            float dist = (Pos(a) - Pos(b)).magnitude;
            if (dist < 8f || dist > 18f || !SlopeOk(a, b)) continue;
            if (!Clear(Pos(a), Pos(b), (a.r + b.r) * 0.5f, i, j)) continue;
            Link(i, j);
            net.loops++;
        }
        return net;
    }

    static CaveSolid.Layout BuildFromNet(Site site, Net net, CaveSolid.Recipe recipe)
    {
        float R = site.R;
        var L = new CaveSolid.Layout { style = CaveSolid.Style.Preset(recipe), bodyRadius = R };
        Vector3 Pos(Node n) => Arc(R, n.x, n.d, n.s);
        for (int k = 0; k < net.legs.Count; k++)
        {
            var (a, b) = net.legs[k];
            var na = net.nodes[a]; var nb = net.nodes[b];
            L.segments.Add(new CaveSolid.Segment
            {
                a = Pos(na), b = Pos(nb), ra = na.r, rb = nb.r, wa = na.w, wb = nb.w, ha = na.h, hb = nb.h,
                openAir = k == 0,
            });
        }
        foreach (var rm in net.rooms)
            L.rooms.Add(new CaveSolid.Room { centre = Pos(net.nodes[rm.node]), radius = rm.r, w = rm.w, h = rm.h });

        // Built-in features scale with how much tunnel there is (presets are per ~150 m).
        float k150 = Mathf.Clamp(net.length / 150f, 1f, 5f);
        var st = L.style;
        st.stalactites = Mathf.RoundToInt(st.stalactites * k150);
        st.stalagmites = Mathf.RoundToInt(st.stalagmites * k150);
        st.columns = Mathf.RoundToInt(st.columns * k150);
        st.boulders = Mathf.RoundToInt(st.boulders * k150);
        st.blocks = Mathf.RoundToInt(st.blocks * k150);
        st.rubble = Mathf.RoundToInt(st.rubble * k150);
        return L;
    }

    static CaveSolid.Layout BuildWarren(Site site) => BuildFromNet(site, Grow(site, WarrenParams), CaveSolid.Recipe.Strata);
    static CaveSolid.Layout BuildDescent(Site site) => BuildFromNet(site, Grow(site, DescentParams), CaveSolid.Recipe.Dripstone);
    static CaveSolid.Layout BuildHall(Site site) => BuildFromNet(site, Grow(site, HallParams), CaveSolid.Recipe.Collapse);

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

        // 1. The tube goes.
        var rig = _moon.transform.Find(TunnelRigName);
        if (rig != null)
        {
            Undo.DestroyObjectImmediate(rig.gameObject);
            Debug.Log("[MoonCaves] Removed the moon tube (Tunnel Rig: tube, lights, bore guide, both mouth markers). Undo-able.");
        }

        // 2. Hidden preview of the real terrain, TutorialGrassBake's recipe.
        _hidden = placeholder.GetComponentsInChildren<Renderer>(true);
        foreach (var r in _hidden) r.enabled = false;
        _holder = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave, layer = _moon.gameObject.layer };
        var t = _holder.transform;
        t.SetParent(_moon.transform, false);
        t.localRotation = Quaternion.identity;
        t.localPosition = Vector3.zero;
        t.localScale = Vector3.one * _moon.radius;
        var generator = _holder.AddComponent<CelestialBodyGenerator>();
        generator.resolutionSettings = spawner.resolutionSettings;
        generator.body = placeholder.bodySettings;
        generator.previewMode = CelestialBodyGenerator.PreviewMode.LOD0;
        generator.OnShapeSettingChanged();
        EditorApplication.QueuePlayerLoopUpdate();
        _startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Debug.Log("[MoonCaves] Generating the moon's LOD0 terrain preview, then building three caves…");
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

    static void InstallAll(Transform terrainT, MeshFilter mf)
    {
        var mc = terrainT.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;
        Physics.SyncTransforms();
        try
        {
            var moonT = _moon.transform;
            Vector3 moonCentre = moonT.position;
            var log = new System.Text.StringBuilder();
            bool allOk = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            // 3. Per site: frame, heightmap, build.
            foreach (var site in Sites)
            {
                Vector3 dirW = moonT.TransformDirection(site.dir);
                var ray = new Ray(moonCentre + dirW * (_moon.radius * 3f), -dirW);
                if (!mc.Raycast(ray, out RaycastHit hit, _moon.radius * 4f))
                {
                    Debug.LogError($"[MoonCaves] Site {site.name}: no terrain under direction {site.dir}.");
                    allOk = false; continue;
                }
                site.localPos = moonT.InverseTransformPoint(hit.point);
                site.R = site.localPos.magnitude;
                Vector3 up = site.localPos.normalized;
                Vector3 east = Vector3.Cross(Vector3.up, up).normalized;     // tangent, "around the pole"
                if (east.sqrMagnitude < 0.01f) east = Vector3.Cross(Vector3.right, up).normalized;
                site.localRot = Quaternion.LookRotation(east, up);

                Matrix4x4 caveToWorld = moonT.localToWorldMatrix * Matrix4x4.TRS(site.localPos, site.localRot, Vector3.one);
                Matrix4x4 worldToCave = caveToWorld.inverse;
                Vector3 upW = caveToWorld.MultiplyVector(Vector3.up).normalized;

                int n = Mathf.RoundToInt(HeightmapHalf * 2f / HeightmapCell);
                var ground = new CaveSolid.Ground { nx = n, nz = n, cell = HeightmapCell, x0 = -HeightmapHalf, z0 = -HeightmapHalf, h = new float[(n + 1) * (n + 1)] };
                int misses = 0; float minH = float.MaxValue, maxH = float.MinValue;
                for (int iz = 0; iz <= n; iz++)
                    for (int ix = 0; ix <= n; ix++)
                    {
                        float x = -HeightmapHalf + ix * HeightmapCell, z = -HeightmapHalf + iz * HeightmapCell;
                        Vector3 from = caveToWorld.MultiplyPoint3x4(new Vector3(x, 40f, z));
                        float hgt;
                        if (mc.Raycast(new Ray(from, -upW), out RaycastHit hh, 120f)) hgt = worldToCave.MultiplyPoint3x4(hh.point).y;
                        else
                        {
                            // Off the far side of a small moon: fall back to the sphere.
                            misses++;
                            float rr = site.R * site.R - x * x - z * z;
                            hgt = rr > 0f ? Mathf.Sqrt(rr) - site.R : -site.R;
                        }
                        ground.h[iz * (n + 1) + ix] = hgt;
                        if (Mathf.Abs(x) <= 16f && Mathf.Abs(z) <= 16f) { minH = Mathf.Min(minH, hgt); maxH = Mathf.Max(maxH, hgt); }
                    }

                double tHeight = clock.Elapsed.TotalSeconds;
                site.layout = site.build(site);
                double tGrow = clock.Elapsed.TotalSeconds;
                site.layout.ground = ground;
                site.result = CaveSolid.Build(site.layout);
                var res = site.result;
                log.AppendLine($"[MoonCaves]   timing: heightmap done at {tHeight:0}s, network grown at {tGrow:0}s, solid built at {clock.Elapsed.TotalSeconds:0}s since install began.");
                float totalLen = 0f; foreach (var sg in site.layout.segments) totalLen += (sg.b - sg.a).magnitude;
                float deepest = 0f; foreach (var sg in site.layout.segments) { deepest = Mathf.Max(deepest, site.R - (sg.a - new Vector3(0f, -site.R, 0f)).magnitude); deepest = Mathf.Max(deepest, site.R - (sg.b - new Vector3(0f, -site.R, 0f)).magnitude); }
                log.AppendLine($"[MoonCaves] Cave {site.name} ({site.title}, {site.recipe}) at moon-local {site.localPos} (R={site.R:0.0}): {site.layout.segments.Count} legs, {totalLen:0} m of tunnel, {site.layout.rooms.Count} rooms, deepest {deepest:0.0} m; terrain within 16 m of the mouth spans {minH:0.0}..{maxH:0.0} m.");
                if (!res.ok)
                {
                    log.AppendLine($"[MoonCaves]   FAILED: {res.failure}");
                    allOk = false; continue;
                }
                log.AppendLine($"[MoonCaves]   closed solid, volume {res.signedVolume:0} m³, {res.nonManifold} non-manifold (cosmetic); {res.mouthReport}; {res.roofReport}.");
                log.AppendLine($"[MoonCaves]   {res.trisFull} tris → {res.trisTrimmed} after trimming the buried hull ({res.trimOpenEdges} buried open edges, shallowest {res.trimShallowest:0.00} m under at {res.trimShallowestAt}); mean sky exposure {res.exposureMean:0.00}; {res.seconds:0.0} s.");
                if (res.trimShallowest < 0.8f) { log.AppendLine("[MoonCaves]   FAILED: a trimmed edge is less than 0.8 m under the terrain."); allOk = false; }

                if (!Walkable(site.layout, out string walk)) { log.AppendLine("[MoonCaves]   FAILED walkability: " + walk); allOk = false; }
                else log.AppendLine("[MoonCaves]   walkable: " + walk);
            }

            // 4. Cross-cave checks.
            if (allOk)
            {
                for (int i = 0; i < Sites.Length; i++)
                {
                    var caps = Capsules(Sites[i]);
                    float core = float.MaxValue, baseD = float.MaxValue;
                    foreach (var c in caps)
                    {
                        core = Mathf.Min(core, SegPointDist(c.a, c.b, Vector3.zero) - c.r);
                        baseD = Mathf.Min(baseD, SegPointDist(c.a, c.b, MoonBaseLocal) - c.r - MoonBaseRadius);
                    }
                    log.AppendLine($"[MoonCaves] Cave {Sites[i].name}: nearest rock to the moon's centre {core:0.0} m, to the moon base {baseD:0.0} m.");
                    if (core < MinCoreRadius) { log.AppendLine("[MoonCaves]   FAILED: too close to the core."); allOk = false; }
                    if (baseD < MoonBaseGap) { log.AppendLine("[MoonCaves]   FAILED: too close to the moon base."); allOk = false; }
                    for (int j = i + 1; j < Sites.Length; j++)
                    {
                        var other = Capsules(Sites[j]);
                        float best = float.MaxValue;
                        foreach (var a in caps) foreach (var b in other)
                            best = Mathf.Min(best, SegSegDist(a.a, a.b, b.a, b.b) - a.r - b.r);
                        log.AppendLine($"[MoonCaves] Caves {Sites[i].name}/{Sites[j].name}: {best:0.0} m of rock between them at the closest point.");
                        if (best < MinCaveGap) { log.AppendLine("[MoonCaves]   FAILED: caves too close."); allOk = false; }
                    }
                }
            }

            log.AppendLine($"[MoonCaves] All checks done at {clock.Elapsed.TotalSeconds:0}s.");
            Debug.Log(log.ToString());
            if (!allOk)
            {
                Debug.LogError("[MoonCaves] Not written — fix the failures above and re-run. The scene was not touched except for the tube removal.");
                return;
            }

            // 5. Write + place.
            foreach (var site in Sites) WriteAndPlace(site);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(_moon.gameObject.scene);
            Debug.Log("[MoonCaves] Three caves installed under Constant Companion. SAVE THE SCENE to keep them. Press Play: CaveHoleBinder punches the mouths on load.");
        }
        finally
        {
            Object.DestroyImmediate(mc);
        }
    }

    // ── Checks ───────────────────────────────────────────────────────────────

    struct Cap { public Vector3 a, b; public float r; }

    /// Every passage and room as a moon-local capsule, fattened by the wall.
    static List<Cap> Capsules(Site site)
    {
        var list = new List<Cap>();
        var m = Matrix4x4.TRS(site.localPos, site.localRot, Vector3.one);
        float wall = site.layout.style.wallThickness;
        foreach (var s in site.layout.segments)
            list.Add(new Cap { a = m.MultiplyPoint3x4(s.a), b = m.MultiplyPoint3x4(s.b), r = Mathf.Max(s.ra * Mathf.Max(s.wa, s.ha), s.rb * Mathf.Max(s.wb, s.hb)) + wall });
        foreach (var r in site.layout.rooms)
        {
            Vector3 c = m.MultiplyPoint3x4(r.centre);
            list.Add(new Cap { a = c, b = c, r = r.radius * Mathf.Max(r.w, r.h) + wall });
        }
        return list;
    }

    /// Breadth-first from the mouth over segments no steeper than the walk
    /// limit; every room and every passage end must be reached.
    static bool Walkable(CaveSolid.Layout L, out string report)
    {
        float R = L.bodyRadius;
        Vector3 C = new Vector3(0f, -R, 0f);
        float Depth(Vector3 p) => R > 0f ? R - (p - C).magnitude : -p.y;

        var nodes = new List<Vector3>();
        int Node(Vector3 p)
        {
            for (int i = 0; i < nodes.Count; i++) if ((nodes[i] - p).sqrMagnitude < 0.25f) return i;
            nodes.Add(p); return nodes.Count - 1;
        }
        var adj = new List<List<int>>();
        void Link(int a, int b) { while (adj.Count < nodes.Count) adj.Add(new List<int>()); adj[a].Add(b); adj[b].Add(a); }

        float steepest = 0f;
        foreach (var s in L.segments)
        {
            int a = Node(s.a), b = Node(s.b);
            float dd = Mathf.Abs(Depth(s.a) - Depth(s.b));
            float len = (s.b - s.a).magnitude;
            float run = Mathf.Sqrt(Mathf.Max(0f, len * len - dd * dd));
            float slope = Mathf.Atan2(dd, Mathf.Max(0.01f, run)) * Mathf.Rad2Deg;
            steepest = Mathf.Max(steepest, slope);
            if (slope <= MaxWalkSlopeDeg) Link(a, b);
            else { while (adj.Count < nodes.Count) adj.Add(new List<int>()); }
        }
        var roomNodes = new List<int>();
        foreach (var r in L.rooms)
        {
            int rn = Node(r.centre);
            roomNodes.Add(rn);
            while (adj.Count < nodes.Count) adj.Add(new List<int>());
            for (int i = 0; i < nodes.Count; i++)
                if (i != rn && (nodes[i] - r.centre).magnitude <= r.radius * Mathf.Max(r.w, 1f) + 0.5f) Link(rn, i);
        }
        int start = Node(L.segments[0].a);
        while (adj.Count < nodes.Count) adj.Add(new List<int>());
        var seen = new bool[nodes.Count];
        var q = new Queue<int>();
        seen[start] = true; q.Enqueue(start);
        while (q.Count > 0) { int n = q.Dequeue(); foreach (int m in adj[n]) if (!seen[m]) { seen[m] = true; q.Enqueue(m); } }
        int unreachedRooms = 0, unreachedNodes = 0;
        foreach (int rn in roomNodes) if (!seen[rn]) unreachedRooms++;
        for (int i = 0; i < nodes.Count; i++) if (!seen[i]) unreachedNodes++;
        report = $"{L.rooms.Count} rooms, {nodes.Count} junctions, steepest leg {steepest:0.0}° (limit {MaxWalkSlopeDeg}°)"
               + (unreachedRooms + unreachedNodes == 0 ? ", all reachable from the mouth" : $", {unreachedRooms} room(s) and {unreachedNodes} junction(s) NOT reachable at walking slopes");
        return unreachedRooms + unreachedNodes == 0;
    }

    static float SegPointDist(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    static float SegSegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        // Sampled — exact enough at 0.25 m for a 4 m margin, and simple.
        float best = float.MaxValue;
        int n1 = Mathf.Max(1, Mathf.CeilToInt((q1 - p1).magnitude / 0.25f));
        for (int i = 0; i <= n1; i++)
            best = Mathf.Min(best, SegPointDist(p2, q2, Vector3.Lerp(p1, q1, i / (float)n1)));
        return best;
    }

    // ── Assets + scene ───────────────────────────────────────────────────────

    static void WriteAndPlace(Site site)
    {
        if (!AssetDatabase.IsValidFolder("Assets/1 - samsPrefabs/Cave")) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "Cave");
        if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/1 - samsPrefabs/Cave", "Moon");

        string prefabName = $"Cave_Moon_{site.name}";
        string meshPath = $"{OutFolder}/{prefabName}_Rock.asset";
        SaveMesh(site.result.mesh, meshPath);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        var material = CaveRockTextures.GetMaterial(site.recipe);

        string prefabPath = $"{OutFolder}/{prefabName}.prefab";
        bool patched = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
        GameObject root = patched ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(prefabName);
        try
        {
            Configure(root, site, mesh, material);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            if (patched) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var existing = _moon.transform.Find(prefabName);
        GameObject inst;
        if (existing != null) inst = existing.gameObject;
        else
        {
            inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _moon.gameObject.scene);
            inst.transform.SetParent(_moon.transform, false);
            Undo.RegisterCreatedObjectUndo(inst, "Install moon cave " + site.name);
        }
        Undo.RecordObject(inst.transform, "Place moon cave");
        inst.name = prefabName;
        inst.transform.localPosition = site.localPos;
        inst.transform.localRotation = site.localRot;
        inst.transform.localScale = Vector3.one;
        inst.SetActive(true);
        EditorUtility.SetDirty(inst);
        Debug.Log($"[MoonCaves] {(patched ? "Patched" : "Wrote")} {prefabPath} ({site.result.trisTrimmed} tris) and placed '{prefabName}' under the moon.");
    }

    static void Configure(GameObject root, Site site, Mesh mesh, Material material)
    {
        var L = site.layout;
        Ensure<CaveHoleBinder>(root);
        Ensure<GrassBlocker>(root);
        Ensure<NoGrassVolume>(root).radius = L.holeRadius + 9f;
        var vol = Ensure<CaveVolume>(root);
        FillVolume(vol, L);
        var seeder = Ensure<CaveCrystalSeeder>(root);
        seeder.crystalCount = site.crystals;
        seeder.seed = 90210 + site.name[0];
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);

        // Rock
        var rockT = root.transform.Find("Cave_Rock");
        GameObject rock = rockT != null ? rockT.gameObject : new GameObject("Cave_Rock");
        rock.transform.SetParent(root.transform, false);
        rock.layer = LayerMask.NameToLayer("Body");
        Ensure<MeshFilter>(rock).sharedMesh = mesh;
        Ensure<MeshRenderer>(rock).sharedMaterial = material;
        var col = Ensure<MeshCollider>(rock);
        col.convex = false;
        col.sharedMesh = null;
        col.sharedMesh = mesh;

        // Hole marker: a cylinder over the computed crossing zone.
        const string holeName = "TerrainHole - Mouth";
        var ht = root.transform.Find(holeName);
        GameObject hole;
        if (ht != null) hole = ht.gameObject;
        else
        {
            hole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hole.name = holeName;
            hole.transform.SetParent(root.transform, false);
            var c = hole.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
        float gHole = L.ground.Sample(L.holeCentre.x, L.holeCentre.z);
        hole.transform.localPosition = new Vector3(L.holeCentre.x, gHole - 0.5f, L.holeCentre.z);
        hole.transform.localRotation = Quaternion.identity;
        hole.transform.localScale = new Vector3(L.holeRadius * 2f, 5f, L.holeRadius * 2f);
        var th = Ensure<TerrainHole>(hole);
        th.shape = TerrainHole.Shape.Cylinder;
        th.hideAtRuntime = true;

        // Pocket lights in the biggest rooms — dim, warm, no shadows.
        int li = 0;
        var wanted = new HashSet<string>();
        var litRooms = new List<CaveSolid.Room>(L.rooms);
        litRooms.Sort((p, q) => q.radius.CompareTo(p.radius));
        if (litRooms.Count > 6) litRooms.RemoveRange(6, litRooms.Count - 6);
        foreach (var room in litRooms)
        {
            if (room.radius < 4.5f) continue;
            string name = "CaveLight_" + li++;
            wanted.Add(name);
            var lt = root.transform.Find(name);
            GameObject lgo = lt != null ? lt.gameObject : new GameObject(name);
            lgo.transform.SetParent(root.transform, false);
            Vector3 up = (room.centre - new Vector3(0f, -L.bodyRadius, 0f)).normalized;
            lgo.transform.localPosition = room.centre + up * (room.radius * 0.25f);
            var l = Ensure<Light>(lgo);
            l.type = LightType.Point;
            l.range = room.radius * 1.9f;
            l.intensity = 0.35f;
            l.color = new Color(1f, 0.82f, 0.62f);
            l.shadows = LightShadows.None;
        }
        // drop lights from a previous layout
        var stale = new List<GameObject>();
        foreach (Transform c in root.transform) if (c.name.StartsWith("CaveLight_") && !wanted.Contains(c.name)) stale.Add(c.gameObject);
        foreach (var g in stale) Object.DestroyImmediate(g);
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
        volume.affectsOcean = false;      // the moon has no ocean
    }

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    // Overwrite the mesh CONTENTS so existing references (prefab, scene) hold.
    static void SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return; }
        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.vertices = mesh.vertices;
        existing.normals = mesh.normals;
        existing.colors = mesh.colors;
        existing.uv = mesh.uv;
        existing.tangents = mesh.tangents;
        existing.triangles = mesh.triangles;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
    }

    // ── Preview helpers (for looking inside from the Scene view without Play) ─

    /// Puts the Scene view camera inside cave `index` at room `room`, looking
    /// along `yawDeg`, with a temporary DontSave light so the dark interior is
    /// visible. Call ClearPreview afterwards.
    public static string Look(int index, int room, float yawDeg, float pitchDeg = 8f)
    {
        var moon = FindMoon();
        if (moon == null) return "no moon";
        var site = Sites[Mathf.Clamp(index, 0, Sites.Length - 1)];
        var inst = moon.transform.Find($"Cave_Moon_{site.name}");
        if (inst == null) return "cave not installed";
        var vol = inst.GetComponent<CaveVolume>();
        if (vol == null || vol.capsuleA == null) return "no volume";
        // rooms are the capsules with a == b, listed after the segments
        var rooms = new List<int>();
        for (int i = 0; i < vol.capsuleA.Length; i++) if ((vol.capsuleA[i] - vol.capsuleB[i]).sqrMagnitude < 1e-4f) rooms.Add(i);
        int idx = rooms.Count > 0 ? rooms[Mathf.Clamp(room, 0, rooms.Count - 1)] : 0;
        Vector3 local = vol.capsuleA[idx];
        Vector3 upL = (local - new Vector3(0f, -moon.radius, 0f)).normalized;
        Vector3 eye = inst.TransformPoint(local - upL * (vol.capsuleR[idx] * 0.35f) + upL * 1.7f);
        Vector3 upW = inst.TransformDirection(upL);
        Vector3 fwdL = Quaternion.AngleAxis(yawDeg, upL) * Vector3.ProjectOnPlane(Vector3.forward, upL).normalized;
        Vector3 fwdW = inst.TransformDirection(fwdL);
        var rot = Quaternion.LookRotation(fwdW, upW) * Quaternion.Euler(-pitchDeg, 0f, 0f);

        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        sv.LookAt(eye + rot * Vector3.forward * 0.5f, rot, 0.5f, false, true);
        sv.pivot = eye + rot * Vector3.forward * 0.5f; sv.rotation = rot; sv.size = 0.5f;
        var lgo = GameObject.Find("CavePreviewLight");
        if (lgo == null) lgo = new GameObject("CavePreviewLight") { hideFlags = HideFlags.DontSave };
        lgo.transform.position = eye;
        var l = lgo.GetComponent<Light>();
        if (l == null) l = lgo.AddComponent<Light>();     // never ?? on a Unity object
        l.type = LightType.Point; l.range = 30f; l.intensity = 1.6f; l.color = new Color(1f, 0.95f, 0.85f); l.shadows = LightShadows.None;
        sv.Repaint();
        return $"looking in cave {site.name} room {idx} from {eye}";
    }

    public static string LookAtMouth(int index, float distance = 22f, float heightDeg = 18f, float sideDeg = 20f)
    {
        var moon = FindMoon();
        if (moon == null) return "no moon";
        var site = Sites[Mathf.Clamp(index, 0, Sites.Length - 1)];
        var inst = moon.transform.Find($"Cave_Moon_{site.name}");
        if (inst == null) return "cave not installed";
        Vector3 target = inst.TransformPoint(new Vector3(0f, 1.5f, 1f));
        Vector3 back = inst.TransformDirection(Quaternion.AngleAxis(sideDeg, Vector3.up) * -Vector3.forward);
        Vector3 up = inst.TransformDirection(Vector3.up);
        Vector3 eye = target + (back * Mathf.Cos(heightDeg * Mathf.Deg2Rad) + up * Mathf.Sin(heightDeg * Mathf.Deg2Rad)) * distance;
        var rot = Quaternion.LookRotation(target - eye, up);
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        sv.pivot = eye + rot * Vector3.forward * 0.5f; sv.rotation = rot; sv.size = 0.5f;
        var lgo = GameObject.Find("CavePreviewLight");
        if (lgo != null) Object.DestroyImmediate(lgo);
        sv.Repaint();
        return $"looking at mouth {site.name} from {eye}";
    }

    public static string ClearPreview()
    {
        var lgo = GameObject.Find("CavePreviewLight");
        if (lgo != null) Object.DestroyImmediate(lgo);
        return "cleared";
    }

    static CelestialBody FindMoon()
    {
        foreach (var cb in Object.FindObjectsOfType<CelestialBody>(true)) if (cb.bodyName == MoonName) return cb;
        return null;
    }
}
