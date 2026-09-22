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
    const float MinCaveGap = 4f;           // clear rock between two caves' walls
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
        public System.Func<float, CaveSolid.Layout> build;

        // filled during install
        public Vector3 localPos; public Quaternion localRot; public float R;
        public CaveSolid.Layout layout; public CaveSolid.Result result;
        public List<(Vector3 c, float r)> lightRooms = new List<(Vector3, float)>();
    }

    static readonly Site[] Sites =
    {
        new Site { name = "A", title = "the Warren",  dir = new Vector3( 0.94f, 0.34f,  0.00f).normalized, recipe = CaveSolid.Recipe.Strata,    crystals = 60, build = BuildWarren },
        new Site { name = "B", title = "the Descent", dir = new Vector3(-0.47f, 0.34f,  0.81f).normalized, recipe = CaveSolid.Recipe.Dripstone, crystals = 70, build = BuildDescent },
        new Site { name = "C", title = "the Hall",    dir = new Vector3(-0.47f, 0.34f, -0.81f).normalized, recipe = CaveSolid.Recipe.Collapse,  crystals = 55, build = BuildHall },
    };

    // ── Layouts ──────────────────────────────────────────────────────────────

    struct P { public float x, d, s, r, w, h; public P(float x, float d, float s, float r, float w = 1f, float h = 1f) { this.x = x; this.d = d; this.s = s; this.r = r; this.w = w; this.h = h; } }

    /// The moon is small: 20 m down, the sphere a tunnel runs on is only 30 m
    /// in radius, so the same arc is 40% shorter and the same drop far steeper.
    /// Authored depths are scaled by this so every leg stays walkable (the
    /// installer measures the real slopes). Heights above ground are not scaled.
    /// The first 8 m of descent are NOT scaled: the entrance has to get under
    /// the terrain at its authored rate or its roof ends up in the ground.
    const float DepthScale = 0.55f;
    const float DepthScaleFrom = 8f;

    static Vector3 Arc(float R, float x, float d, float s)
    {
        if (d > DepthScaleFrom) d = DepthScaleFrom + (d - DepthScaleFrom) * DepthScale;
        float along = Mathf.Sqrt(x * x + s * s);
        if (along < 1e-4f) return new Vector3(0f, -d, 0f);
        float theta = along / R;
        Vector3 t = new Vector3(x, 0f, s) / along;
        Vector3 dir = Vector3.up * Mathf.Cos(theta) + t * Mathf.Sin(theta);
        return dir * (R - d) - new Vector3(0f, R, 0f);
    }

    static void Run(CaveSolid.Layout L, float R, bool firstOpenAir, params P[] pts)
    {
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var a = pts[i]; var b = pts[i + 1];
            L.segments.Add(new CaveSolid.Segment
            {
                a = Arc(R, a.x, a.d, a.s), b = Arc(R, b.x, b.d, b.s),
                ra = a.r, rb = b.r, wa = a.w, wb = b.w, ha = a.h, hb = b.h,
                openAir = firstOpenAir && i == 0,
            });
        }
    }

    static void RoomAt(CaveSolid.Layout L, float R, float x, float d, float s, float r, float w = 1f, float h = 1f)
        => L.rooms.Add(new CaveSolid.Room { centre = Arc(R, x, d, s), radius = r, w = w, h = h });

    /// A — the Warren. Ramp in, early fork; the left arm loops through a low
    /// wide cavern and rejoins at a tall chamber the right arm drops into;
    /// pockets off the chamber, then a lower level to a deep room.
    static CaveSolid.Layout BuildWarren(float R)
    {
        var L = new CaveSolid.Layout { style = CaveSolid.Style.Preset(CaveSolid.Recipe.Strata), bodyRadius = R };
        var M0 = new P(0, -2.4f, -5.5f, 2.8f, 1.3f, 0.9f);
        var M1 = new P(0, -0.6f, -0.5f, 2.8f, 1.3f, 0.95f);
        var M2 = new P(0, 2.9f, 7.0f, 3.0f, 1.3f, 1.0f);
        var M3 = new P(0, 6.2f, 14.0f, 3.3f, 1.35f, 1.0f);
        var F  = new P(3, 8.8f, 20.0f, 3.4f, 1.35f, 1.0f);
        Run(L, R, true, M0, M1, M2, M3, F);
        // left loop
        var L1 = new P(-5, 11.5f, 25.5f, 3.2f, 1.3f, 1.0f);
        var L2 = new P(-12, 14.0f, 32.0f, 3.4f, 1.3f, 1.0f);
        var L3 = new P(-7, 16.5f, 39.0f, 3.2f, 1.3f, 1.0f);
        var L4 = new P(2, 18.5f, 42.0f, 3.3f, 1.3f, 1.0f);
        var T  = new P(11, 18.5f, 38.0f, 3.5f, 1.3f, 1.05f);
        Run(L, R, false, F, L1, L2, L3, L4, T);
        RoomAt(L, R, -12, 14.0f, 32.0f, 6.5f, 1.2f, 0.62f);     // low wide cavern
        // right arm
        var R1 = new P(10, 12.2f, 25.0f, 3.3f, 1.35f, 1.0f);
        var R2 = new P(14, 15.4f, 31.0f, 3.3f, 1.35f, 1.0f);
        Run(L, R, false, F, R1, R2, T);
        RoomAt(L, R, 11, 18.5f, 38.0f, 7.0f, 1.1f, 1.35f);      // tall chamber
        // pockets + lower level
        var S1 = new P(18, 20.5f, 44.0f, 3.0f, 1.2f, 1.0f);
        Run(L, R, false, T, S1);
        RoomAt(L, R, 18, 20.5f, 44.0f, 4.0f);
        var S2 = new P(6, 22.5f, 47.0f, 3.1f, 1.3f, 1.0f);
        var D1 = new P(-3, 25.5f, 51.0f, 3.2f, 1.3f, 1.0f);
        var D  = new P(-10, 28.0f, 54.0f, 3.4f, 1.3f, 1.0f);
        Run(L, R, false, T, S2, D1, D);
        RoomAt(L, R, 6, 22.5f, 47.0f, 4.5f);
        RoomAt(L, R, -10, 28.0f, 54.0f, 6.0f, 1.15f, 1.0f);     // deep room
        // dead-end crawl off the left arm
        var C1 = new P(-11, 12.5f, 21.0f, 2.2f, 1.2f, 0.9f);
        Run(L, R, false, L1, C1);
        RoomAt(L, R, -11, 12.5f, 21.0f, 3.0f);
        return L;
    }

    /// B — the Descent. A tall crack in, switchback ramps down through three
    /// stacked caverns, side tunnels off each, a crawl at the bottom.
    static CaveSolid.Layout BuildDescent(float R)
    {
        var L = new CaveSolid.Layout { style = CaveSolid.Style.Preset(CaveSolid.Recipe.Dripstone), bodyRadius = R };
        var M0 = new P(0, -3.0f, -5.0f, 2.7f, 0.5f, 1.4f);
        var M1 = new P(0, -1.0f, 0.0f, 2.7f, 0.55f, 1.25f);
        var M2 = new P(0, 2.4f, 7.5f, 2.8f, 0.75f, 1.0f);
        var M3 = new P(3, 5.6f, 14.0f, 3.0f, 1.0f, 1.0f);
        var C1 = new P(6, 7.5f, 20.0f, 3.2f, 1.2f, 0.95f);
        Run(L, R, true, M0, M1, M2, M3, C1);
        RoomAt(L, R, 6, 7.5f, 20.0f, 5.5f, 1.2f, 0.9f);        // cavern 1
        var S1 = new P(-2, 10.5f, 24.0f, 3.1f, 1.25f, 1.0f);
        var S2 = new P(-9, 13.5f, 20.0f, 3.1f, 1.25f, 1.0f);
        var C2 = new P(-12, 15.5f, 26.0f, 3.2f, 1.25f, 1.0f);
        Run(L, R, false, C1, S1, S2, C2);
        RoomAt(L, R, -12, 15.5f, 26.0f, 6.0f, 1.15f, 1.0f);    // cavern 2
        var T1 = new P(12, 9.5f, 24.0f, 2.8f, 1.2f, 1.0f);
        var T1e = new P(16, 11.0f, 28.0f, 2.8f, 1.2f, 1.0f);
        Run(L, R, false, C1, T1, T1e);
        RoomAt(L, R, 16, 11.0f, 28.0f, 3.5f);
        var T2 = new P(-18, 17.5f, 32.0f, 2.8f, 1.2f, 1.0f);
        var T2e = new P(-22, 19.0f, 37.0f, 2.8f, 1.2f, 1.0f);
        Run(L, R, false, C2, T2, T2e);
        RoomAt(L, R, -22, 19.0f, 37.0f, 4.0f);
        var S3 = new P(-6, 18.5f, 32.0f, 3.1f, 1.25f, 1.0f);
        var S4 = new P(2, 21.5f, 36.0f, 3.1f, 1.25f, 1.0f);
        var C3 = new P(6, 24.0f, 42.0f, 3.2f, 1.25f, 1.1f);
        Run(L, R, false, C2, S3, S4, C3);
        RoomAt(L, R, 6, 24.0f, 42.0f, 6.5f, 1.1f, 1.2f);        // cavern 3, tall
        var K1 = new P(12, 26.0f, 47.0f, 2.0f, 1.1f, 0.9f);
        var K2 = new P(16, 27.5f, 52.0f, 1.9f, 1.1f, 0.9f);
        Run(L, R, false, C3, K1, K2);
        RoomAt(L, R, 16, 27.5f, 52.0f, 2.6f);
        return L;
    }

    /// C — the Hall. Wide low mouth into one long broken hall with pillars,
    /// branches left and right into rooms, a rear chamber and a deep room.
    static CaveSolid.Layout BuildHall(float R)
    {
        var L = new CaveSolid.Layout { style = CaveSolid.Style.Preset(CaveSolid.Recipe.Collapse), bodyRadius = R };
        var M0 = new P(0, -2.0f, -5.5f, 3.0f, 1.7f, 0.65f);
        var M1 = new P(0, -0.4f, -0.5f, 3.0f, 1.7f, 0.7f);
        var M2 = new P(0, 3.0f, 7.0f, 3.2f, 1.6f, 0.8f);
        var H1 = new P(0, 6.2f, 14.0f, 3.8f, 1.7f, 0.85f);
        var H2 = new P(2, 8.6f, 22.0f, 4.0f, 1.8f, 0.9f);
        var H3 = new P(1, 11.0f, 30.0f, 4.0f, 1.8f, 0.9f);
        var H4 = new P(-2, 13.5f, 38.0f, 3.8f, 1.7f, 0.85f);
        var RC = new P(-4, 16.5f, 46.0f, 3.6f, 1.4f, 1.0f);
        Run(L, R, true, M0, M1, M2, H1, H2, H3, H4, RC);
        RoomAt(L, R, -4, 16.5f, 46.0f, 6.5f, 1.2f, 1.0f);      // rear chamber
        var BL1 = new P(-9, 8.5f, 17.0f, 3.0f, 1.3f, 0.9f);
        var BL  = new P(-15, 10.5f, 21.0f, 3.0f, 1.3f, 0.9f);
        Run(L, R, false, H1, BL1, BL);
        RoomAt(L, R, -15, 10.5f, 21.0f, 4.5f);
        var BR1 = new P(10, 10.5f, 25.0f, 3.0f, 1.3f, 0.9f);
        var BR  = new P(16, 12.5f, 30.0f, 3.0f, 1.3f, 0.9f);
        var BR2 = new P(20, 15.5f, 37.0f, 2.8f, 1.2f, 0.9f);
        var BR3 = new P(18, 18.0f, 44.0f, 2.8f, 1.2f, 0.9f);
        Run(L, R, false, H2, BR1, BR, BR2, BR3);
        RoomAt(L, R, 16, 12.5f, 30.0f, 5.0f);
        RoomAt(L, R, 18, 18.0f, 44.0f, 4.0f);
        var BL2 = new P(-10, 15.5f, 41.0f, 3.0f, 1.3f, 0.9f);
        var BL3 = new P(-16, 18.5f, 46.0f, 3.0f, 1.3f, 0.9f);
        Run(L, R, false, H4, BL2, BL3);
        RoomAt(L, R, -16, 18.5f, 46.0f, 5.5f, 1.2f, 0.9f);
        var D1 = new P(-1, 19.5f, 52.0f, 3.0f, 1.3f, 0.9f);
        var D  = new P(4, 22.0f, 57.0f, 3.0f, 1.3f, 0.9f);
        Run(L, R, false, RC, D1, D);
        RoomAt(L, R, 4, 22.0f, 57.0f, 5.0f);
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

                site.layout = site.build(site.R);
                site.layout.ground = ground;
                site.result = CaveSolid.Build(site.layout);
                var res = site.result;
                log.AppendLine($"[MoonCaves] Cave {site.name} ({site.title}, {site.recipe}) at moon-local {site.localPos} (R={site.R:0.0}), terrain within 16 m of the mouth spans {minH:0.0}..{maxH:0.0} m, heightmap misses {misses}.");
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
        foreach (var room in L.rooms)
        {
            if (room.radius < 5.4f) continue;
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
