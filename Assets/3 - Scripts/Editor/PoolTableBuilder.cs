using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Pool ▸ Build Pool Table Prefab — generates every mesh, texture and
/// material for the low-poly pool table (bed, six jaw-cut cushions, rails with
/// diamond sights, pocket cups, apron, tapered legs, 16 numbered balls, the cue
/// stick, the aim-guide lines) and saves `Assets/1 - samsPrefabs/Pool/PoolTable.prefab`
/// with PoolTable + PoolShotSession wired up. Re-running it rebuilds the assets
/// IN PLACE (same GUIDs), so a placed instance in the scene keeps working.
///
/// Tools ▸ Pool ▸ Place Pool Table near Shlawg's Bar — drops an instance under
/// Humble Abode's TOWN-VILLAGE beside House_03, stood on the local up. Sam
/// nudges it and saves.
///
/// Every dimension is in metres in the table's local space; the sim
/// (PoolPhysics2D) uses the same numbers, so keep them in step.
/// </summary>
public static class PoolTableBuilder
{
    const string PrefabDir = "Assets/1 - samsPrefabs/Pool";
    const string MatDir = "Assets/2 - Materials/Pool";
    const string PrefabPath = PrefabDir + "/PoolTable.prefab";

    // ── dimensions (mirror PoolPhysics2D) ─────────────────────────────────
    const float HL = 0.99f, HW = 0.495f, R = 0.028575f;
    const float FeltY = 0.80f;
    const float CushW = 0.05f, CushH = 0.038f, NoseBevel = 0.014f, NoseLow = 0.020f;
    const float CornerJaw = 0.075f, SideJaw = 0.070f;
    const float RailW = 0.11f, RailTop = FeltY + 0.045f, RailBottom = FeltY - 0.02f, RailChamfer = 0.012f;
    const float ApronH = 0.16f, ApronInset = 0.02f;
    const float LegTopW = 0.12f, LegBotW = 0.09f, FootW = 0.135f, FootH = 0.02f, LegInset = 0.06f;
    const float CornerCupR = 0.075f, SideCupR = 0.070f, CupWall = 0.008f, CupDepth = 0.075f, CupLip = 0.006f;
    const float CornerPocketInset = 0.012f, SidePocketInset = 0.035f;
    const float SightW = 0.016f, SightL = 0.032f;
    /// The cloth/rail hole is this much smaller than the cup, so the cup wall always sits just behind the hole's edge (no slivers).
    const float HoleUnderlap = 0.003f;

    static readonly float OuterX = HL + CushW + RailW;
    static readonly float OuterZ = HW + CushW + RailW;

    // ── colours ───────────────────────────────────────────────────────────
    static readonly Color Felt = new Color(0.11f, 0.44f, 0.22f);
    static readonly Color Wood = new Color(0.27f, 0.15f, 0.08f);
    static readonly Color CupBlack = new Color(0.035f, 0.03f, 0.03f);
    static readonly Color Ivory = new Color(0.93f, 0.89f, 0.78f);
    static readonly Color Maple = new Color(0.82f, 0.64f, 0.40f);
    static readonly Color Wrap = new Color(0.16f, 0.10f, 0.07f);
    static readonly Color TipBlue = new Color(0.22f, 0.36f, 0.68f);
    static readonly Color[] BallColors =
    {
        Color.white,
        new Color(0.98f, 0.80f, 0.12f), new Color(0.12f, 0.32f, 0.85f), new Color(0.86f, 0.14f, 0.12f),
        new Color(0.46f, 0.20f, 0.66f), new Color(0.96f, 0.50f, 0.10f), new Color(0.10f, 0.55f, 0.26f),
        new Color(0.56f, 0.12f, 0.16f), new Color(0.06f, 0.06f, 0.06f),
    };

    // ── menu ──────────────────────────────────────────────────────────────

    [MenuItem("Tools/Pool/Build Pool Table Prefab")]
    public static void Build()
    {
        EnsureDir(PrefabDir); EnsureDir(PrefabDir + "/Meshes"); EnsureDir(PrefabDir + "/Textures"); EnsureDir(MatDir);

        // materials
        // Smoothness 0 everywhere on the table, like the village pack: the scene has
        // environment reflections OFF, so the only shine is the SUN'S specular, and on a
        // flat-shaded mesh a whole facet flashes white when it lines up — Sam saw the
        // corners "glow" as he orbited (2026-09-13). Balls and cue keep a little.
        var mFelt = StdMat("PoolFelt", Felt, 0f);
        var mWood = StdMat("PoolWood", Wood, 0f);
        // Dead matte, no highlight, no reflections: a smooth near-black dielectric turns
        // WHITE at grazing angles (Fresnel) — Sam saw the corner pockets "glow" as he
        // walked round the table (2026-09-13).
        var mCup = StdMat("PoolPocket", CupBlack, 0f, specular: false);
        var mIvory = StdMat("PoolIvory", Ivory, 0f);
        var mMaple = StdMat("CueMaple", Maple, 0.15f);
        var mWrap = StdMat("CueWrap", Wrap, 0.05f);
        var mTip = StdMat("CueTip", TipBlue, 0.05f);
        var mGuide = GuideMat();

        var root = new GameObject("PoolTable");
        try
        {
            Piece(root, "Felt", BuildFelt(), mFelt);
            var wood = Piece(root, "Wood", BuildWood(), mWood);
            Piece(root, "Pockets", BuildCups(), mCup);
            Piece(root, "Sights", BuildSights(), mIvory);

            // balls
            var ballMesh = SaveMesh("PoolBall", BuildBallMesh());
            var ballsRoot = new GameObject("Balls").transform; ballsRoot.SetParent(root.transform, false);
            var balls = new Transform[16];
            for (int i = 0; i < 16; i++)
            {
                var go = new GameObject("Ball_" + i);
                go.transform.SetParent(ballsRoot, false);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = ballMesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = BallMat(i);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                balls[i] = go.transform;
            }

            // pocket anchors
            var anchorsRoot = new GameObject("PocketAnchors").transform; anchorsRoot.SetParent(root.transform, false);
            var anchors = new Transform[6];
            for (int p = 0; p < 6; p++)
            {
                PocketCentre(p, out float px, out float pz);
                var a = new GameObject("Pocket_" + p); a.transform.SetParent(anchorsRoot, false);
                a.transform.localPosition = new Vector3(px, FeltY, pz);
                anchors[p] = a.transform;
            }

            // cue stick (tip at origin, +z toward the butt)
            var cue = new GameObject("Cue").transform; cue.SetParent(root.transform, false);
            Piece(cue.gameObject, "Tip", Lathe(new[] { (0f, 0.0040f), (0.0025f, 0.0062f), (0.008f, 0.0062f) }, 10, true, false), mTip);
            Piece(cue.gameObject, "Ferrule", Lathe(new[] { (0.008f, 0.0062f), (0.030f, 0.0064f) }, 10, false, false), mIvory);
            Piece(cue.gameObject, "Shaft", Lathe(new[] { (0.030f, 0.0064f), (0.55f, 0.0090f), (0.98f, 0.0118f) }, 10, false, false), mMaple);
            Piece(cue.gameObject, "Butt", Lathe(new[] { (0.98f, 0.0118f), (1.28f, 0.0138f), (1.43f, 0.0150f), (1.45f, 0.0135f) }, 10, false, true), mWrap);
            cue.localPosition = new Vector3(-HL * 0.5f - R - 0.025f, FeltY + R, 0f);
            cue.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            cue.gameObject.SetActive(false);

            // guide lines (table-local)
            var guide = Line(root, "Guide", mGuide, 0.006f, false);
            var ring = Line(root, "ContactRing", mGuide, 0.004f, true);
            var stub = Line(root, "ObjectStub", mGuide, 0.004f, false);

            // Solid collider on the WOOD child, not the root, and the wood is the
            // Interactable's gazeTarget: the crosshair test hits it, and GazeHighlight
            // outlines only ITS renderer. Outlining the whole table put an inflated copy
            // of each hollow pocket cup into the hole, where nothing masks it — a glowing
            // ring on every corner whenever the prompt was up (Sam, 2026-09-13). The
            // root keeps only the runtime trigger sphere (PoolTable.Awake).
            var box = wood.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, RailTop * 0.5f, 0f);
            box.size = new Vector3(OuterX * 2f, RailTop, OuterZ * 2f);

            var table = root.AddComponent<PoolTable>();
            table.balls = balls;
            table.pocketAnchors = anchors;
            table.feltY = FeltY;
            table.cue = cue;
            table.guideLine = guide; table.contactRing = ring; table.objectStub = stub;
            table.halfLength = HL; table.halfWidth = HW; table.ballRadius = R;
            table.interactMessage = "";
            table.gazeTarget = wood.transform;
            var session = root.AddComponent<PoolShotSession>();
            session.guideColor = new Color(1f, 0.77f, 0.42f, 0.45f);

            // rack the visuals so the prefab looks right in the Editor too
            var sim = new PoolPhysics2D();
            for (int i = 0; i < 16; i++) balls[i].localPosition = new Vector3(sim.X[i], FeltY + R, sim.Y[i]);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (!ok) Debug.LogError("[Pool] failed to save " + PrefabPath);
            else { Debug.Log("[Pool] built " + PrefabPath, prefab); EditorGUIUtility.PingObject(prefab); }
        }
        finally { Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Pool/Place Pool Table near Shlawg's Bar")]
    public static void PlaceNearBar()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError("[Pool] build the prefab first (Tools ▸ Pool ▸ Build Pool Table Prefab)."); return; }
        var village = GameObject.Find("--- Celestial ---/Body Simulation/Humble Abode/TOWN-VILLAGE");
        if (village == null) { Debug.LogError("[Pool] TOWN-VILLAGE not found — is 1.6.7.7.7 open?"); return; }
        var house = village.transform.Find("House_03");
        if (house == null) { Debug.LogError("[Pool] House_03 not found under TOWN-VILLAGE."); return; }
        var body = village.GetComponentInParent<CelestialBody>();
        if (body == null) { Debug.LogError("[Pool] TOWN-VILLAGE is not under a CelestialBody."); return; }

        Vector3 centre = body.transform.position;
        Vector3 up = (house.position - centre).normalized;
        // Door side of the house, projected onto the ground plane.
        Transform door = house.Find("DoorPart_01");
        Vector3 side = door != null ? Vector3.ProjectOnPlane(door.position - house.position, up) : Vector3.ProjectOnPlane(house.forward, up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.ProjectOnPlane(house.right, up);
        side.Normalize();
        // Beside the door rather than in front of it: swing 60° round the house.
        Vector3 place = house.position + (Quaternion.AngleAxis(60f, up) * side) * 7.5f;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "PoolTable";
        go.transform.SetParent(village.transform, true);
        go.transform.position = place;
        SnapToGround(go, body);
        // In the Editor the planet is only a placeholder sphere (the real terrain is
        // built on load), so the raycast lands too low. The village objects were
        // stood on the real ground: use the bases of the nearest few as the level.
        float neighbourGround = NeighbourGroundRadius(village.transform, go.transform, body, 3);
        float hitRadius = (go.transform.position - body.transform.position).magnitude;
        if (neighbourGround > 0f && neighbourGround > hitRadius + 0.05f)
        {
            Vector3 dir = (go.transform.position - body.transform.position).normalized;
            go.transform.position = body.transform.position + dir * neighbourGround;
            Debug.Log($"[Pool] raised from radius {hitRadius:0.00} (Editor placeholder sphere) to {neighbourGround:0.00} (village neighbours' ground).");
        }
        // Long side faces the house.
        Vector3 toHouse = Vector3.ProjectOnPlane(house.position - go.transform.position, up).normalized;
        go.transform.rotation = Quaternion.LookRotation(toHouse, up);
        Undo.RegisterCreatedObjectUndo(go, "Place Pool Table");
        Selection.activeGameObject = go;
        Debug.Log("[Pool] placed PoolTable beside House_03 — nudge it, then SAVE the scene.", go);
    }

    // Median radial height of the lowest renderer corner of the `count` nearest direct
    // children of `village` (excluding `self`) — i.e. where the neighbours' feet are.
    static float NeighbourGroundRadius(Transform village, Transform self, CelestialBody body, int count)
    {
        Vector3 centre = body.transform.position;
        var found = new List<(float dist, float ground)>();
        foreach (Transform child in village)
        {
            if (child == self || child.name.StartsWith("__")) continue;
            var mfs = child.GetComponentsInChildren<MeshFilter>();
            if (mfs.Length == 0) continue;
            Vector3 up = (child.position - centre).normalized;
            float lowest = float.MaxValue;
            foreach (var mf in mfs)
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;                       // tight local box, not the world AABB
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mf.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    float h = Vector3.Dot(c - centre, up);
                    if (h < lowest) lowest = h;
                }
            }
            if (lowest == float.MaxValue) continue;
            found.Add((Vector3.Distance(child.position, self.position), lowest));
            if (found.Count <= 12) Debug.Log($"[Pool] neighbour {child.name}: base radius {lowest:0.00}, {found[found.Count - 1].dist:0.0} m away");
        }
        if (found.Count == 0) return -1f;
        found.Sort((a, b) => a.dist.CompareTo(b.dist));
        int n = Mathf.Min(count, found.Count);
        var grounds = new List<float>();
        for (int i = 0; i < n; i++) grounds.Add(found[i].ground);
        grounds.Sort();
        return grounds[n / 2];
    }

    // VendorPlacement's recipe: fire down at the core from well above, stand on the hit.
    static void SnapToGround(GameObject go, CelestialBody body)
    {
        Vector3 centre = body.transform.position;
        Vector3 up = (go.transform.position - centre).normalized;
        float startHeight = Mathf.Max(body.radius * 0.35f, 200f);
        Vector3 origin = centre + up * (Vector3.Dot(go.transform.position - centre, up) + startHeight);
        var hits = Physics.RaycastAll(origin, -up, startHeight * 3f);
        float best = float.MaxValue; Vector3 surface = Vector3.zero; bool found = false;
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(go.transform) || h.collider.isTrigger) continue;
            if (h.distance < best) { best = h.distance; surface = h.point; found = true; }
        }
        if (found) go.transform.position = surface;
        else if (body.radius > 0.01f)
        {
            go.transform.position = centre + up * body.radius;
            Debug.LogWarning("[Pool] no terrain collider in the Editor — placed on the sphere radius; nudge it down onto the ground.", go);
        }
    }

    // ── mesh pieces ───────────────────────────────────────────────────────

    // The cloth: a flat rectangle under everything, notched around the six pocket cups.
    // Built as vertical strips (quads between the bottom and top notch profile), so the
    // concave side-pocket notches need no triangulation.
    static Mesh BuildFelt()
    {
        var m = new FlatMesh();
        float x0 = -OuterX + ApronInset, x1 = OuterX - ApronInset;
        const int cols = 160;
        float zMax = OuterZ - ApronInset;
        for (int c = 0; c < cols; c++)
        {
            float xa = Mathf.Lerp(x0, x1, c / (float)cols), xb = Mathf.Lerp(x0, x1, (c + 1) / (float)cols);
            float ta = NotchTop(xa, zMax), tb = NotchTop(xb, zMax);
            m.Face(Vector3.up, new Vector3(xa, FeltY, -ta), new Vector3(xa, FeltY, ta), new Vector3(xb, FeltY, tb), new Vector3(xb, FeltY, -tb));
        }
        return m.Build("PoolFelt");
    }

    // Top extent of the cloth at column x (symmetric in z): full width, cut by any pocket cup circle.
    static float NotchTop(float x, float zMax)
    {
        float top = zMax;
        for (int p = 0; p < 6; p++)
        {
            PocketCentre(p, out float px, out float pz);
            if (pz < 0f) continue;                                   // symmetric: use the +z pockets
            float r = (p < 4 ? CornerCupR : SideCupR) - HoleUnderlap;
            float dx = x - px;
            if (Mathf.Abs(dx) >= r) continue;
            float z = pz - Mathf.Sqrt(r * r - dx * dx);
            if (z < top) top = z;
        }
        return Mathf.Max(top, 0.001f);
    }

    static void PocketCentre(int p, out float x, out float z)
    {
        switch (p)
        {
            case 0: x = -(HL + CornerPocketInset); z = -(HW + CornerPocketInset); break;
            case 1: x = (HL + CornerPocketInset); z = -(HW + CornerPocketInset); break;
            case 2: x = -(HL + CornerPocketInset); z = (HW + CornerPocketInset); break;
            case 3: x = (HL + CornerPocketInset); z = (HW + CornerPocketInset); break;
            case 4: x = 0f; z = -(HW + SidePocketInset); break;
            default: x = 0f; z = (HW + SidePocketInset); break;
        }
    }

    // Rails (with mitred corners + top chamfer), apron, legs, feet — all one wood mesh —
    // plus the six felt-covered cushions go on the felt mesh; they are built here into
    // whichever mesh is passed.
    static Mesh BuildWood()
    {
        var m = new FlatMesh();
        // Four rails, each built as strips along its length so the pocket holes are cut
        // straight through the wood (a ball rolls into a real hole, not up to a wall).
        RailStrips(m, true, 1f); RailStrips(m, true, -1f);
        RailStrips(m, false, 1f); RailStrips(m, false, -1f);

        // apron
        float ax = OuterX - ApronInset, az = OuterZ - ApronInset;
        float apronTop = RailBottom, apronBot = RailBottom - ApronH;
        m.Box(new Vector3(0f, (apronTop + apronBot) * 0.5f, 0f), new Vector3(ax * 2f, apronTop - apronBot, az * 2f));

        // legs + feet
        float lx = ax - LegInset - LegTopW * 0.5f, lz = az - LegInset - LegTopW * 0.5f;
        foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                Vector3 c = new Vector3(sx * lx, 0f, sz * lz);
                m.TaperedBox(c, FootH, apronBot + 0.001f, LegBotW, LegTopW);
                m.Box(c + Vector3.up * FootH * 0.5f, new Vector3(FootW, FootH, FootW));
            }
        return m.Build("PoolWood");
    }

    // One rail as column strips. `alongX`: the rail runs along x (a long rail at z = ±OuterZ);
    // otherwise along z (a short rail at x = ±OuterX). `sign` picks the side. In rail
    // coordinates t runs along the rail and w across it (w > 0 outward); the inner edge
    // w = RailWIn(t) is the cushion back, the 45° mitre near the ends, and the pocket holes.
    static void RailStrips(FlatMesh m, bool alongX, float sign)
    {
        float L = alongX ? OuterX : OuterZ;
        float wOut = alongX ? OuterZ : OuterX;
        Vector3 outward = alongX ? new Vector3(0f, 0f, sign) : new Vector3(sign, 0f, 0f);
        Vector3 P(float t, float y, float w) => alongX ? new Vector3(t, y, sign * w) : new Vector3(sign * w, y, t);
        const int cols = 240;
        for (int c = 0; c < cols; c++)
        {
            float t0 = Mathf.Lerp(-L, L, c / (float)cols), t1 = Mathf.Lerp(-L, L, (c + 1) / (float)cols);
            float w0 = RailWIn(alongX, sign, t0, out bool cut0), w1 = RailWIn(alongX, sign, t1, out bool cut1);
            if (w0 >= wOut - 1e-4f && w1 >= wOut - 1e-4f) continue;
            w0 = Mathf.Min(w0, wOut); w1 = Mathf.Min(w1, wOut);
            float wc = wOut - RailChamfer, yc = RailTop - RailChamfer;
            // Height of the rail's upper surface across it: flat top, then the chamfer slope.
            float YAt(float w) => w <= wc ? RailTop : Mathf.Lerp(RailTop, yc, (w - wc) / RailChamfer);
            // Top: only the part of this column inside the chamfer line (at the mitred
            // corners the inner edge crosses it — never fold a quad past that line).
            if (w0 < wc || w1 < wc)
                m.Face(Vector3.up, P(t0, RailTop, Mathf.Min(w0, wc)), P(t0, RailTop, wc), P(t1, RailTop, wc), P(t1, RailTop, Mathf.Min(w1, wc)));
            // Chamfer: from wherever this column's inner edge sits within the band (or its inner line) out to the edge.
            float c0 = Mathf.Max(w0, wc), c1 = Mathf.Max(w1, wc);
            if (c0 < wOut - 1e-5f || c1 < wOut - 1e-5f)
                m.Face(outward + Vector3.up, P(t0, YAt(c0), c0), P(t0, yc, wOut), P(t1, yc, wOut), P(t1, YAt(c1), c1));
            m.Face(outward, P(t0, yc, wOut), P(t0, RailBottom, wOut), P(t1, RailBottom, wOut), P(t1, yc, wOut));        // outer side
            m.Face(Vector3.down, P(t0, RailBottom, w0), P(t1, RailBottom, w1), P(t1, RailBottom, wOut), P(t0, RailBottom, wOut)); // underside
            // Inner face: the sliver above the cushion. Not inside a pocket hole (the cup
            // lines that) and not along the mitre (buried inside the neighbouring rail).
            float limit = alongX ? HL + CushW : HW + CushW;
            if (!cut0 && !cut1 && Mathf.Abs(t0) <= limit && Mathf.Abs(t1) <= limit)
                m.Face(-outward, P(t0, RailBottom, w0), P(t0, RailTop, w0), P(t1, RailTop, w1), P(t1, RailBottom, w1));
        }
    }

    // Inner edge of a rail at length coordinate t (see RailStrips). `cut` = inside a pocket hole.
    static float RailWIn(bool alongX, float sign, float t, out bool cut)
    {
        float baseW = alongX ? HW + CushW : HL + CushW;
        float limit = alongX ? HL + CushW : HW + CushW;
        float w = Mathf.Abs(t) > limit ? baseW + (Mathf.Abs(t) - limit) : baseW;    // 45° mitre at the corners
        cut = false;
        for (int p = 0; p < 6; p++)
        {
            PocketCentre(p, out float px, out float pz);
            float pt = alongX ? px : pz, pw = alongX ? sign * pz : sign * px;
            if (pw < 0f) continue;                                                    // the other side's pockets
            float r = (p < 4 ? CornerCupR : SideCupR) - HoleUnderlap;
            float dt = t - pt;
            if (Mathf.Abs(dt) >= r) continue;
            float edge = pw + Mathf.Sqrt(r * r - dt * dt);
            if (edge > w) { w = edge; cut = true; }
        }
        return w;
    }

    static Vector3 At(Vector3 p, float y) => new Vector3(p.x, y, p.z);

    // Six cushions on the felt mesh (they share the felt material): built as a separate
    // mesh so the felt piece stays one draw with them.
    static void AddCushions(FlatMesh m)
    {
        float flareC = CushW * 0.9f, flareS = CushW * 0.55f;
        // long cushions (±z), split at the side pocket
        foreach (var sz in new[] { -1f, 1f })
        {
            Vector3 inward = new Vector3(0f, 0f, -sz);
            Cushion(m, new Vector3(-(HL - CornerJaw), 0, sz * HW), new Vector3(-SideJaw, 0, sz * HW), inward, flareC, flareS);
            Cushion(m, new Vector3(SideJaw, 0, sz * HW), new Vector3(HL - CornerJaw, 0, sz * HW), inward, flareS, flareC);
        }
        // short cushions (±x)
        foreach (var sx in new[] { -1f, 1f })
        {
            Vector3 inward = new Vector3(-sx, 0f, 0f);
            Cushion(m, new Vector3(sx * HL, 0, -(HW - CornerJaw)), new Vector3(sx * HL, 0, HW - CornerJaw), inward, flareC, flareC);
        }
    }

    // noseA→noseB is the nose line on the cloth; the back sits CushW outward and flares
    // past each end by flareA/flareB (the angled jaw cut beside a pocket).
    static void Cushion(FlatMesh m, Vector3 noseA, Vector3 noseB, Vector3 inward, float flareA, float flareB)
    {
        Vector3 along = (noseB - noseA).normalized;
        Vector3 outward = -inward;
        Vector3 backA = noseA + outward * CushW - along * flareA;
        Vector3 backB = noseB + outward * CushW + along * flareB;
        Vector3 nA0 = At(noseA, FeltY), nB0 = At(noseB, FeltY);
        Vector3 nA1 = At(noseA, FeltY + NoseLow), nB1 = At(noseB, FeltY + NoseLow);
        Vector3 tA = At(noseA + outward * NoseBevel, FeltY + CushH), tB = At(noseB + outward * NoseBevel, FeltY + CushH);
        Vector3 bA1 = At(backA, FeltY + CushH), bB1 = At(backB, FeltY + CushH);
        Vector3 bA0 = At(backA, FeltY), bB0 = At(backB, FeltY);
        m.Face(inward, nA0, nA1, nB1, nB0);                  // nose, lower vertical
        m.Face(inward + Vector3.up, nA1, tA, tB, nB1);       // nose bevel
        m.Face(Vector3.up, tA, bA1, bB1, tB);                // top
        m.Face(outward, bA0, bB0, bB1, bA1);                 // back (inside the rail, cheap insurance)
        m.Face(-along, nA0, bA0, bA1, tA, nA1);              // end A
        m.Face(along, nB0, nB1, tB, bB1, bB0);               // end B
    }

    // Pocket cups: hollow 12-sided cylinders through the rail with a floor.
    // Pocket cups. Below the cloth: a full bowl the ball drops into. Above it: only the
    // arc that sits out under the rail rises as a liner — on the table side there is
    // NO wall, so a ball rolls straight into the hole (Sam, 2026-09-13: the old full ring
    // "looked like it blocked the ball, but the ball just goes through it").
    static Mesh BuildCups()
    {
        var m = new FlatMesh();
        const int sides = 24;
        for (int p = 0; p < 6; p++)
        {
            PocketCentre(p, out float px, out float pz);
            float r = p < 4 ? CornerCupR : SideCupR, ri = r - CupWall;
            Vector3 c = new Vector3(px, 0f, pz);
            float bot = FeltY - CupDepth, lowTop = FeltY - 0.001f, highTop = RailTop + CupLip;
            bool[] high = new bool[sides];
            for (int i = 0; i < sides; i++)
            {
                float am = (i + 0.5f) / sides * Mathf.PI * 2f;
                Vector3 mid = c + new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am)) * r;
                high[i] = Mathf.Abs(mid.x) > HL + CushW || Mathf.Abs(mid.z) > HW + CushW;
            }
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                float top = high[i] ? highTop : lowTop;
                Vector3 hint = d0 + d1;
                m.Face(hint, c + d0 * r + Vector3.up * bot, c + d1 * r + Vector3.up * bot, c + d1 * r + Vector3.up * top, c + d0 * r + Vector3.up * top);      // outer
                m.Face(-hint, c + d0 * ri + Vector3.up * bot, c + d1 * ri + Vector3.up * bot, c + d1 * ri + Vector3.up * top, c + d0 * ri + Vector3.up * top);  // inner
                m.Face(Vector3.up, c + d0 * ri + Vector3.up * top, c + d0 * r + Vector3.up * top, c + d1 * r + Vector3.up * top, c + d1 * ri + Vector3.up * top); // rim
                // Close the step where the liner starts/ends.
                int prev = (i + sides - 1) % sides;
                if (high[i] != high[prev])
                {
                    Vector3 tangent = new Vector3(-d0.z, 0f, d0.x) * (high[i] ? -1f : 1f);
                    m.Face(tangent, c + d0 * ri + Vector3.up * lowTop, c + d0 * r + Vector3.up * lowTop, c + d0 * r + Vector3.up * highTop, c + d0 * ri + Vector3.up * highTop);
                }
            }
            m.Disc(c, ri, bot + 0.002f, sides, true);   // floor
        }
        return m.Build("PoolPockets");
    }

    static Mesh BuildSights()
    {
        var m = new FlatMesh();
        float y = RailTop + 0.0015f;
        float zc = HW + CushW + RailW * 0.5f, xc = HL + CushW + RailW * 0.5f;
        foreach (var f in new[] { 0.25f, 0.5f, 0.75f })
            foreach (var s in new[] { -1f, 1f })
            {
                Diamond(m, new Vector3(s * HL * f, y, zc), Vector3.right);
                Diamond(m, new Vector3(s * HL * f, y, -zc), Vector3.right);
            }
        foreach (var f in new[] { -0.5f, 0f, 0.5f })
        {
            Diamond(m, new Vector3(xc, y, HW * f), Vector3.forward);
            Diamond(m, new Vector3(-xc, y, HW * f), Vector3.forward);
        }
        return m.Build("PoolSights");
    }

    static void Diamond(FlatMesh m, Vector3 c, Vector3 along)
    {
        Vector3 across = Vector3.Cross(Vector3.up, along).normalized;
        m.Face(Vector3.up, c + along * SightW * 0.5f, c + across * SightL * 0.5f, c - along * SightW * 0.5f, c - across * SightL * 0.5f);
    }

    // UV sphere, smooth normals (balls should look round). u runs WITH the longitude
    // angle: Unity is left-handed, so that is what reads correctly from outside (the
    // mirrored version was tried first and came out backwards — check a render).
    static Mesh BuildBallMesh()
    {
        const int lon = 20, lat = 14;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int j = 0; j <= lat; j++)
        {
            float vv = j / (float)lat;
            float theta = vv * Mathf.PI;                         // 0 = north pole
            for (int i = 0; i <= lon; i++)
            {
                float uu = i / (float)lon;
                float phi = uu * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                v.Add(d * R); n.Add(d); uv.Add(new Vector2(uu, 1f - vv));
            }
        }
        for (int j = 0; j < lat; j++)
            for (int i = 0; i < lon; i++)
            {
                int a = j * (lon + 1) + i, b = a + lon + 1;
                t.Add(a); t.Add(a + 1); t.Add(b);
                t.Add(a + 1); t.Add(b + 1); t.Add(b);
            }
        var mesh = new Mesh { name = "PoolBall" };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Lathe around +z: profile = (z, radius) pairs, flat-shaded rings.
    static Mesh Lathe((float z, float r)[] prof, int sides, bool capStart, bool capEnd)
    {
        var m = new FlatMesh();
        for (int k = 0; k < prof.Length - 1; k++)
        {
            var (z0, r0) = prof[k]; var (z1, r1) = prof[k + 1];
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 p00 = new Vector3(Mathf.Cos(a0) * r0, Mathf.Sin(a0) * r0, z0), p10 = new Vector3(Mathf.Cos(a1) * r0, Mathf.Sin(a1) * r0, z0);
                Vector3 p01 = new Vector3(Mathf.Cos(a0) * r1, Mathf.Sin(a0) * r1, z1), p11 = new Vector3(Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1, z1);
                Vector3 hint = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), Mathf.Sin((a0 + a1) * 0.5f), 0f);
                m.Face(hint, p00, p10, p11, p01);
            }
        }
        if (capStart) m.DiscZ(prof[0].z, prof[0].r, sides, Vector3.back);
        if (capEnd) m.DiscZ(prof[prof.Length - 1].z, prof[prof.Length - 1].r, sides, Vector3.forward);
        return m.Build("Lathe");
    }

    // ── textures ──────────────────────────────────────────────────────────

    static readonly string[] Digits =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
    };

    static Texture2D BallTexture(int n)
    {
        const int W = 256, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        bool stripe = n >= 9;
        Color baseCol = n == 0 ? Color.white : (stripe ? Color.white : BallColors[n]);
        Color bandCol = stripe ? BallColors[n - 8] : baseCol;
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)H;
            bool inBand = Mathf.Abs(v - 0.5f) < 0.24f;
            Color c = inBand ? bandCol : baseCol;
            for (int x = 0; x < W; x++) px[y * W + x] = c;
        }
        if (n > 0)
        {
            foreach (int cx in new[] { 64, 192 })
            {
                const int cy = 64, rad = 22;
                for (int y = cy - rad; y <= cy + rad; y++)
                    for (int x = cx - rad; x <= cx + rad; x++)
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rad * rad) px[y * W + x] = Color.white;
                string s = n.ToString();
                int scale = s.Length == 1 ? 4 : 3;
                int gw = 3 * scale, gh = 5 * scale, gap = scale;
                int total = s.Length * gw + (s.Length - 1) * gap;
                int x0 = cx - total / 2, y0 = cy - gh / 2;
                for (int d = 0; d < s.Length; d++)
                {
                    string g = Digits[s[d] - '0'];
                    for (int r = 0; r < 5; r++)
                        for (int c = 0; c < 3; c++)
                        {
                            if (g[r * 3 + c] != '1') continue;
                            // texture rows grow upward; glyph row 0 is the top
                            int gx = x0 + d * (gw + gap) + c * scale, gy = y0 + (4 - r) * scale;
                            for (int yy = 0; yy < scale; yy++) for (int xx = 0; xx < scale; xx++)
                                px[(gy + yy) * W + (gx + xx)] = Color.black;
                        }
                }
            }
        }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    // ── assets ────────────────────────────────────────────────────────────

    static Material BallMat(int n)
    {
        string texPath = $"{PrefabDir}/Textures/Ball_{n:00}.png";
        var tex = BallTexture(n);
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (imp != null && (imp.wrapMode != TextureWrapMode.Repeat || imp.mipmapEnabled != true))
        {
            imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.SaveAndReimport();
        }
        var texAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var mat = StdMat($"Ball_{n:00}", Color.white, 0.6f);
        mat.mainTexture = texAsset;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material StdMat(string name, Color color, float smoothness, bool specular = true)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("Standard");
        mat.color = color;
        mat.SetFloat("_Glossiness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        // Standard's highlight/reflection toggles are keyword-driven; the float alone does nothing.
        mat.SetFloat("_SpecularHighlights", specular ? 1f : 0f);
        mat.SetFloat("_GlossyReflections", specular ? 1f : 0f);
        if (specular) { mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); mat.DisableKeyword("_GLOSSYREFLECTIONS_OFF"); }
        else { mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material GuideMat()
    {
        string path = $"{MatDir}/PoolGuide.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(mat, path); }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mesh SaveMesh(string name, Mesh built)
    {
        string path = $"{PrefabDir}/Meshes/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { built.name = name; AssetDatabase.CreateAsset(built, path); return built; }
        existing.Clear();
        existing.vertices = built.vertices; existing.normals = built.normals; existing.uv = built.uv;
        existing.triangles = built.triangles;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(built);
        return existing;
    }

    static GameObject Piece(GameObject parent, string name, Mesh mesh, Material mat)
    {
        if (name == "Felt") { var fm = new FlatMesh(); AddCushions(fm); mesh = FlatMesh.Merge(mesh, fm.Build("cushions")); }
        var saved = SaveMesh(mesh.name == "Lathe" ? "Cue" + name : mesh.name, mesh);
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = saved;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static LineRenderer Line(GameObject parent, string name, Material mat, float width, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.sharedMaterial = mat;
        lr.startWidth = lr.endWidth = width;
        lr.loop = loop;
        lr.positionCount = 0;
        lr.startColor = lr.endColor = new Color(1f, 0.77f, 0.42f, 0.45f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.View;
        lr.enabled = false;
        return lr;
    }

    static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureDir(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ── flat-shaded mesh builder ──────────────────────────────────────────

    class FlatMesh
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _t = new List<int>();

        /// Convex polygon as a fan; every face gets its own vertices (hard edges). The
        /// winding is fixed so the normal agrees with `outwardHint`.
        public void Face(Vector3 outwardHint, params Vector3[] p)
        {
            if (p.Length < 3) return;
            Vector3 n = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            if (n.sqrMagnitude < 1e-14f) { n = Vector3.Cross(p[2] - p[0], p[p.Length - 1] - p[0]); }
            if (n.sqrMagnitude < 1e-14f) return;
            bool flip = Vector3.Dot(n, outwardHint) < 0f;
            n = (flip ? -n : n).normalized;
            int b = _v.Count;
            for (int i = 0; i < p.Length; i++)
            {
                _v.Add(p[i]); _n.Add(n);
                _uv.Add(new Vector2(p[i].x + p[i].z, p[i].y + p[i].z) * 2f);
            }
            for (int i = 1; i < p.Length - 1; i++)
            {
                if (flip) { _t.Add(b); _t.Add(b + i + 1); _t.Add(b + i); }
                else { _t.Add(b); _t.Add(b + i); _t.Add(b + i + 1); }
            }
        }

        public void Box(Vector3 c, Vector3 s)
        {
            Vector3 h = s * 0.5f;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z), p100 = c + new Vector3(h.x, -h.y, -h.z), p010 = c + new Vector3(-h.x, h.y, -h.z), p110 = c + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z), p101 = c + new Vector3(h.x, -h.y, h.z), p011 = c + new Vector3(-h.x, h.y, h.z), p111 = c + new Vector3(h.x, h.y, h.z);
            Face(Vector3.up, p010, p011, p111, p110);
            Face(Vector3.down, p000, p100, p101, p001);
            Face(Vector3.forward, p001, p101, p111, p011);
            Face(Vector3.back, p000, p010, p110, p100);
            Face(Vector3.right, p100, p110, p111, p101);
            Face(Vector3.left, p000, p001, p011, p010);
        }

        // Square-section leg: width wBot at yBot, wTop at yTop.
        public void TaperedBox(Vector3 c, float yBot, float yTop, float wBot, float wTop)
        {
            float hb = wBot * 0.5f, ht = wTop * 0.5f;
            Vector3 b0 = new Vector3(c.x - hb, yBot, c.z - hb), b1 = new Vector3(c.x + hb, yBot, c.z - hb), b2 = new Vector3(c.x + hb, yBot, c.z + hb), b3 = new Vector3(c.x - hb, yBot, c.z + hb);
            Vector3 t0 = new Vector3(c.x - ht, yTop, c.z - ht), t1 = new Vector3(c.x + ht, yTop, c.z - ht), t2 = new Vector3(c.x + ht, yTop, c.z + ht), t3 = new Vector3(c.x - ht, yTop, c.z + ht);
            Face(Vector3.back, b0, b1, t1, t0);
            Face(Vector3.right, b1, b2, t2, t1);
            Face(Vector3.forward, b2, b3, t3, t2);
            Face(Vector3.left, b3, b0, t0, t3);
            Face(Vector3.down, b0, b1, b2, b3);
            Face(Vector3.up, t0, t1, t2, t3);
        }

        public void Tube(Vector3 c, float r, float y0, float y1, int sides, bool outward)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 hint = (d0 + d1) * (outward ? 1f : -1f);
                Face(hint, c + d0 * r + Vector3.up * y0, c + d1 * r + Vector3.up * y0, c + d1 * r + Vector3.up * y1, c + d0 * r + Vector3.up * y1);
            }
        }

        public void Annulus(Vector3 c, float rIn, float rOut, float y, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 up = Vector3.up * y;
                Face(Vector3.up, c + d0 * rIn + up, c + d0 * rOut + up, c + d1 * rOut + up, c + d1 * rIn + up);
            }
        }

        public void Disc(Vector3 c, float r, float y, int sides, bool faceUp)
        {
            var pts = new Vector3[sides];
            for (int i = 0; i < sides; i++) { float a = i / (float)sides * Mathf.PI * 2f; pts[i] = c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }
            Face(faceUp ? Vector3.up : Vector3.down, pts);
        }

        public void DiscZ(float z, float r, int sides, Vector3 hint)
        {
            var pts = new Vector3[sides];
            for (int i = 0; i < sides; i++) { float a = i / (float)sides * Mathf.PI * 2f; pts[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z); }
            Face(hint, pts);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }

        public static Mesh Merge(Mesh a, Mesh b)
        {
            var m = new Mesh { name = a.name };
            var ci = new[] { new CombineInstance { mesh = a, transform = Matrix4x4.identity }, new CombineInstance { mesh = b, transform = Matrix4x4.identity } };
            m.CombineMeshes(ci, true, false);
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
            return m;
        }
    }
}
