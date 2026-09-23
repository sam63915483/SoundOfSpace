using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cave as a SOLID with thick walls, built from a distance field and
/// polygonised with Surface Nets. Editor-only (the generators run from menu
/// items); nothing here executes at runtime.
///
/// WHY A SOLID (kept from the first version)
/// The old swept-surface generator built a paper-thin rim and a paper-thin tube
/// that had to meet exactly, and every bug it produced was a strip whose one
/// visible face pointed the wrong way, so you looked through "rock" into the
/// hollow planet. A solid cannot have that bug: the mesh is the closed boundary
/// of a volume of rock, every face is visible from the side you see it from,
/// and branches and rooms are free — one more shape in the union.
///
/// WHAT THIS VERSION ADDS (2026-09-22, the moon caves)
/// Sam's verdict on the first cave was "too smooth and round and fake". So:
///   • FACETED shading — vertices are split per triangle. Flat rock reads as
///     rock; smooth blobs read as fake, whatever the noise does.
///   • sharper ridged noise plus STRATA (depth-banded ridges → ledges), applied
///     to the walls; the flattened floor only gets a fraction so it stays walkable.
///   • elliptical, wider-than-tall passages; floor flattening that actually
///     engages on ramps (it used to fade out above ~15°, so ramps were pipes).
///   • stalactites, stalagmites, columns, boulders, blocks and rubble built into
///     the same solid — no props, no seams, same faceted stone.
///   • a CURVED WORLD: on a 50 m moon, "up" 40 m along the tunnel is 50° off
///     "up" at the mouth. Floors, cross-sections and strata all use the local
///     radial direction, and the layouts are authored as (lateral, depth, arc).
///   • the mouth is a rock OUTCROP the passage enters, standing on a slab that
///     follows the REAL terrain (sampled into a heightmap by the installer), so
///     the rock meets the ground everywhere and the cut edge always has rock
///     behind it.
///   • per-vertex SKY EXPOSURE (rays marched through the sampled field) in the
///     vertex alpha; the shader scales sun + ambient by it, so the interior is
///     dark except for the flashlight and the placed lights.
///   • the buried outer hull is trimmed after the closure check — never visible
///     from anywhere reachable, and it halves the triangle count.
///
/// THINGS THAT WILL BITE YOU (all learned the hard way)
///   • Nothing may be thinner than a grid cell. Surface Nets holds one vertex per
///     cell and cannot represent two sheets in one. Sub-cell features (a wedge
///     between diverging passages, a rim tapering to a feather edge) come out as
///     boundary edges — literal holes.
///   • Passages must leave junctions sharply; keep ~8 m between parallel runs.
///     BlendRadius fillets junctions so the wedge is never sub-cell.
///   • Every void point that meets the terrain surface must be inside the
///     TerrainHole. Outside it the terrain sheet is still there — invisible from
///     below (back faces) but SOLID to physics, and visible from above. The
///     installer sizes the hole from the actual crossing points and checks it.
///   • Anything the void touches above the terrain needs rock over it (the
///     outcrop) — the roof check measures that, it is not assumed.
/// </summary>
public static class CaveSolid
{
    // ── Public types ─────────────────────────────────────────────────────────

    /// A tunnel leg. w/h scale the cross-section laterally / vertically (1 = round).
    public struct Segment
    {
        public Vector3 a, b;
        public float ra, rb;
        public float wa, wb, ha, hb;
        /// True for the open-air approach in front of the outcrop: the roof
        /// check skips it (there is nothing above it but sky, by design).
        public bool openAir;
    }

    /// A room — an ellipsoid the tunnels open into.
    public struct Room
    {
        public Vector3 centre;
        public float radius;
        public float w, h;
    }

    public enum Recipe { Strata, Dripstone, Collapse }
    public enum MouthKind { Eyebrow, Fissure, Collapse }

    /// Everything that makes one cave look different from another.
    [Serializable]
    public sealed class Style
    {
        public Recipe recipe = Recipe.Strata;
        public MouthKind mouth = MouthKind.Eyebrow;
        public int seed = 1;

        public float cellSize = 0.4f;
        public float wallThickness = 3.0f;

        // wall noise
        public float noiseAmp = 0.9f;
        public float noiseScale = 0.13f;
        public float detailWeight = 0.6f;
        public float warpStrength = 2.5f;
        public float verticalStretch = 1f;     // <1 = streaks along "down" (flowstone)
        public float strataAmp = 0.5f;
        public float strataFreq = 0.55f;       // bands per metre of depth
        public float ceilingRoughen = 0.4f;    // extra noise weight on the roof
        public float floorNoise = 0.2f;        // fraction of the wall noise the floor gets

        // built-in features
        public int stalactites, stalagmites, columns, boulders, blocks, rubble;
        public int mouthBoulders = 8;

        // outcrop
        public Vector3 moundCentre = new Vector3(0f, 1f, 6f);   // x, height above terrain, z(arc)
        public Vector3 moundRadii = new Vector3(9f, 5.2f, 9.5f);
        public float moundNoise = 0.8f;

        // colour
        public Color rockTint = new Color(0.86f, 0.86f, 0.88f);
        public Color floorTint = new Color(0.95f, 0.93f, 0.88f);
        public Color steepTint = new Color(0.72f, 0.72f, 0.76f);
        public Color moonTint = new Color(0.80f, 0.80f, 0.80f);

        public static Style Preset(Recipe r)
        {
            var s = new Style { recipe = r };
            switch (r)
            {
                case Recipe.Strata:
                    s.mouth = MouthKind.Eyebrow; s.seed = 101;
                    s.noiseAmp = 0.9f; s.noiseScale = 0.14f; s.detailWeight = 0.6f; s.warpStrength = 2.5f;
                    s.strataAmp = 0.5f; s.strataFreq = 0.55f; s.ceilingRoughen = 0.4f; s.floorNoise = 0.2f;
                    s.stalactites = 10; s.stalagmites = 8; s.columns = 0; s.boulders = 18; s.blocks = 4; s.rubble = 12;
                    s.mouthBoulders = 4;
                    s.moundRadii = Vector3.zero;   // flush sinkhole, no outcrop (Sam 2026-09-22)
                    s.rockTint = new Color(0.84f, 0.85f, 0.89f); s.floorTint = new Color(0.95f, 0.94f, 0.90f);
                    s.steepTint = new Color(0.70f, 0.71f, 0.76f);
                    break;
                case Recipe.Dripstone:
                    s.mouth = MouthKind.Fissure; s.seed = 202;
                    s.noiseAmp = 0.7f; s.noiseScale = 0.11f; s.detailWeight = 0.45f; s.warpStrength = 3.5f;
                    s.verticalStretch = 0.45f;
                    s.strataAmp = 0.15f; s.strataFreq = 0.3f; s.ceilingRoughen = 0.3f; s.floorNoise = 0.15f;
                    s.stalactites = 60; s.stalagmites = 40; s.columns = 5; s.boulders = 4; s.blocks = 0; s.rubble = 0;
                    s.mouthBoulders = 3;
                    s.moundRadii = Vector3.zero;   // flush sinkhole, no outcrop (Sam 2026-09-22)
                    s.moundNoise = 0.6f;
                    s.rockTint = new Color(0.90f, 0.82f, 0.70f); s.floorTint = new Color(0.62f, 0.56f, 0.50f);
                    s.steepTint = new Color(0.86f, 0.76f, 0.62f);
                    break;
                case Recipe.Collapse:
                    s.mouth = MouthKind.Collapse; s.seed = 303;
                    s.noiseAmp = 1.0f; s.noiseScale = 0.16f; s.detailWeight = 0.7f; s.warpStrength = 2.0f;
                    s.strataAmp = 0.3f; s.strataFreq = 0.4f; s.ceilingRoughen = 0.7f; s.floorNoise = 0.3f;
                    s.stalactites = 6; s.stalagmites = 4; s.columns = 3; s.boulders = 30; s.blocks = 18; s.rubble = 36;
                    s.mouthBoulders = 5;
                    s.moundRadii = Vector3.zero;   // flush sinkhole, no outcrop (Sam 2026-09-22)
                    s.moundNoise = 1.0f;
                    s.rockTint = new Color(0.70f, 0.70f, 0.73f); s.floorTint = new Color(0.80f, 0.79f, 0.76f);
                    s.steepTint = new Color(0.58f, 0.58f, 0.62f);
                    break;
            }
            return s;
        }
    }

    /// The terrain under the cave as a heightfield in cave-local (x, z), y = height.
    /// Flat (all zero) when nothing is sampled — the legacy Humble Abode cave.
    public sealed class Ground
    {
        public static readonly Ground Flat = new Ground();
        public float[] h;           // row-major, (nz+1) rows of (nx+1)
        public int nx, nz;
        public float cell = 0.5f;
        public float x0, z0;

        public float Sample(float x, float z)
        {
            if (h == null) return 0f;
            float fx = Mathf.Clamp((x - x0) / cell, 0f, nx - 1e-4f);
            float fz = Mathf.Clamp((z - z0) / cell, 0f, nz - 1e-4f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            float a = h[iz * (nx + 1) + ix], b = h[iz * (nx + 1) + ix + 1];
            float c = h[(iz + 1) * (nx + 1) + ix], d = h[(iz + 1) * (nx + 1) + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }
    }

    /// One cave to build.
    public sealed class Layout
    {
        public List<Segment> segments = new List<Segment>();
        public List<Room> rooms = new List<Room>();
        public Style style = Style.Preset(Recipe.Strata);
        public Ground ground = Ground.Flat;
        /// Radius of the body the cave is dug into, measured at the mouth. 0 = a
        /// flat world (up is +Y everywhere). Otherwise up is radial about (0,-R,0).
        public float bodyRadius;
        /// Filled by Build: the TerrainHole cylinder that covers every point
        /// where the void meets the terrain.
        public Vector3 holeCentre;
        public float holeRadius;
    }

    public sealed class Result
    {
        public Mesh mesh;
        public int quads, trisFull, trisTrimmed;
        public int holes, nonManifold;
        public Bounds defectBounds;
        public double signedVolume;
        public int trimOpenEdges;
        public float trimShallowest = float.MaxValue;
        public Vector3 trimShallowestAt;
        public bool mouthOk = true, roofOk = true;
        public string mouthReport = "", roofReport = "";
        public float exposureMean;
        public double seconds;
        public bool ok;
        public string failure = "";
    }

    // ── Constants that are not per-style ─────────────────────────────────────

    /// Void radius is squashed by this below the axis, giving a walkable floor.
    const float FloorSquash = 0.55f;

    /// Smooth-union radius at junctions. Must stay comfortably above the cell
    /// size (7 cells at 0.4) — the fillet it creates is what guarantees nothing
    /// at a junction is thinner than a cell.
    const float BlendRadius = 2.8f;

    /// The skirt: a slab of rock under the terrain around the mouth, this much
    /// wider than the hole, 3.2 m thick, top 0.3 m under the ground (0.05 m
    /// inside the hole, where it IS the ground you walk on).
    const float SkirtExtra = 8f;
    const float SlabThickness = 3.2f;
    const float BuryInside = 0.05f;
    const float BuryOutside = 0.30f;

    /// Kept for CaveGenerator's log line.
    public const float WallThickness = 3.0f;
    public const float CellSize = 0.4f;

    // ── Features (rock added inside the void / around the mouth) ─────────────

    enum FeatureKind { Cone, Ellipsoid, Capsule, Box }

    struct Feature
    {
        public FeatureKind kind;
        public Vector3 a, b;        // cone/capsule ends, ellipsoid/box centre in a
        public float ra, rb;        // cone radii / capsule radius
        public Vector3 scale;       // ellipsoid radii / box half extents
        public Quaternion invRot;   // box only
        public bool mouth;          // lives around the mouth (outside the void)
    }

    // ── Build ────────────────────────────────────────────────────────────────

    public static Result Build(Layout L)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var R = new Result();
        var st = L.style;
        float cell = st.cellSize;

        // Fill defaults on the layout.
        for (int i = 0; i < L.segments.Count; i++)
        {
            var s = L.segments[i];
            if (s.wa <= 0f) s.wa = 1f; if (s.wb <= 0f) s.wb = 1f;
            if (s.ha <= 0f) s.ha = 1f; if (s.hb <= 0f) s.hb = 1f;
            L.segments[i] = s;
        }
        for (int i = 0; i < L.rooms.Count; i++)
        {
            var r = L.rooms[i];
            if (r.w <= 0f) r.w = 1f; if (r.h <= 0f) r.h = 1f;
            L.rooms[i] = r;
        }

        var ctx = new Ctx(L);
        ctx.ComputeHole();
        ctx.PlaceFeatures();

        Bounds b = ctx.ComputeBounds();
        int nx = Mathf.CeilToInt(b.size.x / cell) + 1;
        int ny = Mathf.CeilToInt(b.size.y / cell) + 1;
        int nz = Mathf.CeilToInt(b.size.z / cell) + 1;
        Vector3 origin = b.min;
        ctx.origin = origin; ctx.nx = nx; ctx.ny = ny; ctx.nz = nz;
        ctx.BuildBuckets();

        // Sample the field on the grid corners once. Everything else reads this.
        var field = new float[(nx + 1) * (ny + 1) * (nz + 1)];
        var voidGrid = new float[field.Length];
        for (int z = 0; z <= nz; z++)
            for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    if (x == 0 && y == 0) ctx.UseZ(z);
                    Vector3 p = origin + new Vector3(x, y, z) * cell;
                    float d = ctx.RockField(p, out float dVoid);
                    // The outermost shell of samples must read as EMPTY: a
                    // surface touching the grid border leaves open edges.
                    if (x == 0 || y == 0 || z == 0 || x == nx || y == ny || z == nz)
                        d = Mathf.Max(d, cell);
                    int idx = (z * (ny + 1) + y) * (nx + 1) + x;
                    field[idx] = d;
                    voidGrid[idx] = dVoid;
                }
        ctx.field = field; ctx.voidGrid = voidGrid;
        ctx.UseAll();

        // ── Surface Nets ─────────────────────────────────────────────────────
        int Idx(int x, int y, int z) => (z * (ny + 1) + y) * (nx + 1) + x;
        var cellVertex = new int[nx * ny * nz];
        for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;
        int CellIdx(int x, int y, int z) => (z * ny + y) * nx + x;

        var verts = new List<Vector3>();
        int[,] edges =
        {
            {0,1},{1,3},{2,3},{0,2},
            {4,5},{5,7},{6,7},{4,6},
            {0,4},{1,5},{2,6},{3,7},
        };
        var cornerOffset = new[]
        {
            new Vector3Int(0,0,0), new Vector3Int(1,0,0), new Vector3Int(0,1,0), new Vector3Int(1,1,0),
            new Vector3Int(0,0,1), new Vector3Int(1,0,1), new Vector3Int(0,1,1), new Vector3Int(1,1,1),
        };
        var corner = new float[8];
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int mask = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        var o = cornerOffset[c];
                        corner[c] = field[Idx(x + o.x, y + o.y, z + o.z)];
                        if (corner[c] < 0f) mask |= 1 << c;
                    }
                    if (mask == 0 || mask == 255) continue;

                    Vector3 sum = Vector3.zero;
                    int crossings = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        int c0 = edges[e, 0], c1 = edges[e, 1];
                        bool in0 = corner[c0] < 0f, in1 = corner[c1] < 0f;
                        if (in0 == in1) continue;
                        float t = corner[c0] / (corner[c0] - corner[c1]);
                        sum += Vector3.Lerp((Vector3)cornerOffset[c0], (Vector3)cornerOffset[c1], t);
                        crossings++;
                    }
                    if (crossings == 0) continue;
                    cellVertex[CellIdx(x, y, z)] = verts.Count;
                    verts.Add(origin + (new Vector3(x, y, z) + sum / crossings) * cell);
                }

        var tris = new List<int>();
        int quadCount = 0;
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    bool solid = field[Idx(x, y, z)] < 0f;
                    if (y > 0 && z > 0 && solid != (field[Idx(x + 1, y, z)] < 0f))
                        AddQuad(tris, cellVertex, CellIdx(x, y - 1, z - 1), CellIdx(x, y, z - 1),
                                CellIdx(x, y, z), CellIdx(x, y - 1, z), solid, ref quadCount);
                    if (x > 0 && z > 0 && solid != (field[Idx(x, y + 1, z)] < 0f))
                        AddQuad(tris, cellVertex, CellIdx(x - 1, y, z - 1), CellIdx(x - 1, y, z),
                                CellIdx(x, y, z), CellIdx(x, y, z - 1), solid, ref quadCount);
                    if (x > 0 && y > 0 && solid != (field[Idx(x, y, z + 1)] < 0f))
                        AddQuad(tris, cellVertex, CellIdx(x - 1, y - 1, z), CellIdx(x, y - 1, z),
                                CellIdx(x, y, z), CellIdx(x - 1, y, z), solid, ref quadCount);
                }
        R.quads = quadCount;

        var mesh = new Mesh { name = "Cave_Solid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        FixOrientation(mesh);
        R.trisFull = mesh.triangles.Length / 3;

        // ── Checks on the full, closed solid ─────────────────────────────────
        CountEdgeDefects(mesh, out R.holes, out R.nonManifold, out R.defectBounds);
        R.signedVolume = SignedVolume(mesh);
        if (R.holes > 0) { R.failure = $"MESH IS NOT CLOSED — {R.holes} boundary edge(s) around {R.defectBounds.center} (extent {R.defectBounds.extents})"; R.mesh = mesh; R.seconds = sw.Elapsed.TotalSeconds; return R; }
        if (R.signedVolume <= 0.0) { R.failure = $"mesh is inside-out (signed volume {R.signedVolume:0})"; R.mesh = mesh; R.seconds = sw.Elapsed.TotalSeconds; return R; }

        R.mouthOk = ctx.CheckMouth(out R.mouthReport);
        R.roofOk = ctx.CheckRoof(out R.roofReport);
        if (!R.mouthOk) { R.failure = "THE HOLE WOULD SHOW THE HOLLOW MOON — " + R.mouthReport; R.mesh = mesh; R.seconds = sw.Elapsed.TotalSeconds; return R; }
        if (!R.roofOk) { R.failure = "A PASSAGE HAS NO ROOF — " + R.roofReport; R.mesh = mesh; R.seconds = sw.Elapsed.TotalSeconds; return R; }

        // ── Exposure + colour on the smooth mesh, then trim, then facet ──────
        var v = mesh.vertices;
        var n = mesh.normals;
        var tri = mesh.triangles;
        float[] exposure = ctx.ComputeExposure(v, n, tri);
        float mean = 0f; for (int i = 0; i < exposure.Length; i++) mean += exposure[i];
        R.exposureMean = exposure.Length > 0 ? mean / exposure.Length : 0f;
        Color[] colours = ctx.VertexColours(v, n, exposure);

        int[] kept = ctx.TrimBuried(v, tri, out R.trimOpenEdges, out R.trimShallowest);
        R.trimShallowestAt = ctx.shallowestAt;
        R.trisTrimmed = kept.Length / 3;

        var final = Facet(v, colours, kept);
        final.name = "Cave_Solid";
        R.mesh = final;
        R.seconds = sw.Elapsed.TotalSeconds;
        R.ok = true;
        return R;
    }

    // ── Context: the field for one layout ────────────────────────────────────

    sealed class Ctx
    {
        readonly Layout L;
        readonly Style st;
        readonly List<Segment> segs;
        readonly List<Room> rooms;
        readonly Ground ground;
        readonly float R;                 // body radius (0 = flat)
        readonly Vector3 centre;          // body centre in cave space
        readonly List<Feature> interior = new List<Feature>();
        readonly List<Feature> mouth = new List<Feature>();
        readonly System.Random rng;
        float skirtR;
        float moundGround;                // terrain height under the mound centre

        public Vector3 origin; public int nx, ny, nz;
        public float[] field, voidGrid;

        // Per-z-plane shortlist of shapes that can influence the field there.
        // SMin is exact when the omitted shape is more than BlendRadius further
        // than the nearest kept one, and the margin below guarantees that near
        // any surface. A 40-leg cave would otherwise evaluate every leg at every
        // one of ~4 million samples.
        List<int>[] _segsAtZ, _roomsAtZ, _featAtZ;
        List<int> _curSegs, _curRooms, _curFeat;
        List<int> _allSegs, _allRooms, _allFeat;

        public void BuildBuckets()
        {
            float margin = st.wallThickness + st.noiseAmp * 2f + BlendRadius + 3f;
            _segsAtZ = new List<int>[nz + 1]; _roomsAtZ = new List<int>[nz + 1]; _featAtZ = new List<int>[nz + 1];
            for (int z = 0; z <= nz; z++)
            {
                float pz = origin.z + z * st.cellSize;
                var sl = new List<int>(); var rl = new List<int>(); var fl = new List<int>();
                for (int i = 0; i < segs.Count; i++)
                {
                    var sg = segs[i];
                    float rr = Mathf.Max(sg.ra * Mathf.Max(sg.wa, sg.ha), sg.rb * Mathf.Max(sg.wb, sg.hb)) + margin;
                    if (pz >= Mathf.Min(sg.a.z, sg.b.z) - rr && pz <= Mathf.Max(sg.a.z, sg.b.z) + rr) sl.Add(i);
                }
                for (int i = 0; i < rooms.Count; i++)
                {
                    float rr = rooms[i].radius * Mathf.Max(rooms[i].w, rooms[i].h) + margin;
                    if (Mathf.Abs(pz - rooms[i].centre.z) <= rr) rl.Add(i);
                }
                for (int i = 0; i < interior.Count; i++)
                {
                    var f = interior[i];
                    float rr = Mathf.Max(f.ra, Mathf.Max(f.rb, Mathf.Max(f.scale.x, Mathf.Max(f.scale.y, f.scale.z)))) + 3f;
                    if (pz >= Mathf.Min(f.a.z, f.b.z) - rr && pz <= Mathf.Max(f.a.z, f.b.z) + rr) fl.Add(i);
                }
                _segsAtZ[z] = sl; _roomsAtZ[z] = rl; _featAtZ[z] = fl;
            }
            UseAll();
        }

        public void UseZ(int z) { _curSegs = _segsAtZ[z]; _curRooms = _roomsAtZ[z]; _curFeat = _featAtZ[z]; }
        public void UseAll()
        {
            _allSegs = new List<int>(); for (int i = 0; i < segs.Count; i++) _allSegs.Add(i);
            _allRooms = new List<int>(); for (int i = 0; i < rooms.Count; i++) _allRooms.Add(i);
            _allFeat = new List<int>(); for (int i = 0; i < interior.Count; i++) _allFeat.Add(i);
            _curSegs = _allSegs; _curRooms = _allRooms; _curFeat = _allFeat;
        }

        public Ctx(Layout layout)
        {
            L = layout; st = layout.style; segs = layout.segments; rooms = layout.rooms;
            ground = layout.ground ?? Ground.Flat;
            R = layout.bodyRadius;
            centre = new Vector3(0f, -R, 0f);
            rng = new System.Random(st.seed);
        }

        // ── geometry helpers ─────────────────────────────────────────────

        public Vector3 Up(Vector3 p)
        {
            if (R <= 0f) return Vector3.up;
            Vector3 d = p - centre;
            float m = d.magnitude;
            return m < 1e-4f ? Vector3.up : d / m;
        }

        /// Metres below the mouth-level sphere (positive = deeper).
        float Depth(Vector3 p) => R <= 0f ? -p.y : R - (p - centre).magnitude;

        float G(Vector3 p) => ground.Sample(p.x, p.z);

        static void Frame(Vector3 n, Vector3 up, out Vector3 lat, out Vector3 vert)
        {
            lat = Vector3.Cross(up, n);
            if (lat.sqrMagnitude < 0.01f) lat = Vector3.Cross(Vector3.right, n);
            if (lat.sqrMagnitude < 0.01f) lat = Vector3.Cross(Vector3.forward, n);
            lat.Normalize();
            vert = Vector3.Cross(n, lat).normalized;
            if (Vector3.Dot(vert, up) < 0f) { vert = -vert; lat = -lat; }
        }

        /// Elliptical capsule with a flattened floor. Returns the raw (noiseless)
        /// distance plus how much of the nearest surface is floor (0..1) and how
        /// high in the cross-section the point sits (0 = floor level, 1 = roof).
        float SegmentField(Vector3 p, in Segment s, out float floorness, out float upness)
        {
            Vector3 ab = s.b - s.a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / len2);
            Vector3 c = s.a + ab * t;
            float r = Mathf.Lerp(s.ra, s.rb, t);
            float w = Mathf.Lerp(s.wa, s.wb, t);
            float h = Mathf.Lerp(s.ha, s.hb, t);
            Vector3 n = len2 < 1e-6f ? Vector3.forward : ab / Mathf.Sqrt(len2);
            Vector3 up = Up(c);
            Frame(n, up, out Vector3 lat, out Vector3 vert);

            Vector3 o = p - c;
            float l = Vector3.Dot(o, lat), v = Vector3.Dot(o, vert), ax = Vector3.Dot(o, n);
            float sc = Mathf.Min(w, h) * r;
            Vector3 q = new Vector3(l / (w * r), v / (h * r), ax / r);
            float dTube = (q.magnitude - 1f) * sc;

            // Floor: full flattening up to ~30° of slope, fading out by ~45°.
            float horizontality = 1f - Mathf.Abs(Vector3.Dot(n, up));
            float fw = Mathf.InverseLerp(0.29f, 0.5f, horizontality);
            float floorV = -h * r * FloorSquash;
            float floorCut = (floorV - v) * fw;
            float d = Mathf.Max(dTube, floorCut);
            floorness = fw > 0f ? Mathf.Clamp01((floorCut - dTube) / 0.8f + 0.5f) : 0f;
            upness = Mathf.Clamp01(v / (h * r));
            return d;
        }

        float RoomField(Vector3 p, in Room room, out float floorness, out float upness)
        {
            Vector3 up = Up(room.centre);
            Frame(Vector3.forward, up, out Vector3 lat, out Vector3 vert);
            Vector3 lat2 = Vector3.Cross(up, lat).normalized;
            Vector3 o = p - room.centre;
            float l1 = Vector3.Dot(o, lat), v = Vector3.Dot(o, up), l2 = Vector3.Dot(o, lat2);
            float r = room.radius, w = room.w, h = room.h;
            Vector3 q = new Vector3(l1 / (w * r), v / (h * r), l2 / (w * r));
            float d = (q.magnitude - 1f) * Mathf.Min(w, h) * r;
            float floorV = -h * r * FloorSquash;
            float floorCut = floorV - v;
            floorness = Mathf.Clamp01((floorCut - d) / 0.8f + 0.5f);
            upness = Mathf.Clamp01(v / (h * r));
            return Mathf.Max(d, floorCut);
        }

        /// Noiseless void: smooth union of every passage and room.
        float VoidRaw(Vector3 p, out float floorness, out float upness)
        {
            float d = float.MaxValue, fl = 0f, upn = 0f, best = float.MaxValue;
            if (_curSegs == null) UseAll();
            var sl = _curSegs; var rl = _curRooms;
            for (int k = 0; k < sl.Count; k++)
            {
                float di = SegmentField(p, segs[sl[k]], out float f, out float u);
                if (di < best) { best = di; fl = f; upn = u; }
                d = SMin(d, di);
            }
            for (int k = 0; k < rl.Count; k++)
            {
                float di = RoomField(p, rooms[rl[k]], out float f, out float u);
                if (di < best) { best = di; fl = f; upn = u; }
                d = SMin(d, di);
            }
            floorness = fl; upness = upn;
            return d;
        }

        float WallNoise(Vector3 p, float floorness, float upness)
        {
            float n = Noise(p);
            float wall = st.noiseAmp * n * Mathf.Lerp(1f, st.floorNoise, floorness) * (1f + st.ceilingRoughen * upness);
            float strata = Strata(p) * (1f - floorness) * (1f - 0.5f * upness);
            return wall + strata;
        }

        float VoidField(Vector3 p)
        {
            float raw = VoidRaw(p, out float fl, out float upn);
            return raw + WallNoise(p, fl, upn);
        }

        /// The outer skin: the same passages fattened by the wall thickness.
        float Hull(Vector3 p)
        {
            float d = float.MaxValue;
            float wall = st.wallThickness;
            if (_curSegs == null) UseAll();
            var sl = _curSegs; var rl = _curRooms;
            for (int k = 0; k < sl.Count; k++)
            {
                var s = segs[sl[k]];
                Vector3 ab = s.b - s.a;
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / len2);
                Vector3 c = s.a + ab * t;
                float r = Mathf.Lerp(s.ra, s.rb, t);
                float w = Mathf.Lerp(s.wa, s.wb, t), h = Mathf.Lerp(s.ha, s.hb, t);
                Vector3 n = len2 < 1e-6f ? Vector3.forward : ab / Mathf.Sqrt(len2);
                Frame(n, Up(c), out Vector3 lat, out Vector3 vert);
                Vector3 o = p - c;
                float l = Vector3.Dot(o, lat), v = Vector3.Dot(o, vert), ax = Vector3.Dot(o, n);
                float rw = w * r + wall, rh = h * r + wall, ra = r + wall;
                Vector3 q = new Vector3(l / rw, v / rh, ax / ra);
                d = SMin(d, (q.magnitude - 1f) * Mathf.Min(rw, rh));
            }
            for (int k = 0; k < rl.Count; k++)
            {
                var room = rooms[rl[k]];
                Vector3 up = Up(room.centre);
                Frame(Vector3.forward, up, out Vector3 lat, out Vector3 vert);
                Vector3 lat2 = Vector3.Cross(up, lat).normalized;
                Vector3 o = p - room.centre;
                float rw = room.w * room.radius + wall, rh = room.h * room.radius + wall;
                Vector3 q = new Vector3(Vector3.Dot(o, lat) / rw, Vector3.Dot(o, up) / rh, Vector3.Dot(o, lat2) / rw);
                d = SMin(d, (q.magnitude - 1f) * Mathf.Min(rw, rh));
            }
            return d + Noise(p) * st.noiseAmp * 0.3f;
        }

        float Bury(Vector3 p)
        {
            float rXZ = new Vector2(p.x - L.holeCentre.x, p.z - L.holeCentre.z).magnitude;
            return Mathf.Lerp(BuryInside, BuryOutside, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(L.holeRadius - 0.8f, L.holeRadius + 1.5f, rXZ)));
        }

        bool InSkirtRegion(Vector3 p, float g)
        {
            float rXZ = new Vector2(p.x - L.holeCentre.x, p.z - L.holeCentre.z).magnitude;
            if (rXZ > skirtR + 3f) return false;
            return p.y > g - SlabThickness - 3f && p.y < g + st.moundRadii.y + st.moundCentre.y + 4f;
        }

        float Slab(Vector3 p, float g, float bury)
        {
            float rXZ = new Vector2(p.x - L.holeCentre.x, p.z - L.holeCentre.z).magnitude;
            float top = p.y - (g - bury);
            float bottom = (g - SlabThickness) - p.y;
            float d = Mathf.Max(Mathf.Max(top, bottom), rXZ - skirtR);
            // A little roughness only where the slab is the ground you walk on.
            if (rXZ < L.holeRadius + 0.5f) d += Noise(p) * 0.15f;
            return d;
        }

        float Mound(Vector3 p)
        {
            if (st.moundRadii.x <= 0f) return float.MaxValue;     // no outcrop: a flush sinkhole
            Vector3 c = new Vector3(st.moundCentre.x, moundGround + st.moundCentre.y, st.moundCentre.z);
            float d = SdEllipsoid(p - c, st.moundRadii);
            return d + Noise(p) * st.noiseAmp * st.moundNoise;
        }

        /// Negative inside the ROCK. Also hands back the (noisy) void distance
        /// so the caller can store it for trimming and exposure.
        public float RockField(Vector3 p, out float dVoid)
        {
            float raw = VoidRaw(p, out float fl, out float upn);
            float g = G(p);
            bool skirt = InSkirtRegion(p, g);

            // Far from every passage AND outside the mouth region: nothing here.
            if (raw > 7f && !skirt) { dVoid = raw; return raw; }

            dVoid = raw + WallNoise(p, fl, upn);
            float bury = Bury(p);
            float hullClipped = Mathf.Max(Hull(p), p.y - (g - bury));
            float rock = Mathf.Max(hullClipped, -dVoid);

            if (skirt)
            {
                float slab = Mathf.Max(Slab(p, g, bury), -dVoid);
                rock = SMin(rock, slab, 1.5f);
                float mound = Mound(p);
                if (mound != float.MaxValue) rock = SMin(rock, Mathf.Max(mound, -dVoid), 2.0f);
                for (int i = 0; i < mouth.Count; i++) rock = Mathf.Min(rock, FeatureField(p, mouth[i]));
            }
            if (raw < 5f)
            {
                if (_curFeat == null) UseAll();
                var fl2 = _curFeat;
                for (int k = 0; k < fl2.Count; k++) rock = Mathf.Min(rock, FeatureField(p, interior[fl2[k]]));
            }
            return rock;
        }

        float FeatureField(Vector3 p, in Feature f)
        {
            float d;
            switch (f.kind)
            {
                case FeatureKind.Cone: d = SdCappedCone(p, f.a, f.b, f.ra, f.rb); break;
                case FeatureKind.Ellipsoid: d = SdEllipsoid(p - f.a, f.scale); break;
                case FeatureKind.Capsule: d = SdCapsule(p, f.a, f.b, f.ra); break;
                default:
                    Vector3 q = f.invRot * (p - f.a);
                    d = SdRoundBox(q, f.scale, 0.25f);
                    break;
            }
            if (d > 2f) return d;
            return d + Noise(p) * st.noiseAmp * 0.3f;
        }

        // ── noise ─────────────────────────────────────────────────────────

        /// Ridged, domain-warped noise, roughly ±0.5. Creases (folds at zero)
        /// are what read as rock — edges, ledges, fracture lines — where plain
        /// Perlin only ever gives bulges.
        float Noise(Vector3 p)
        {
            if (st.verticalStretch != 1f)
            {
                // Streaks along "down": squash the sample position along up.
                Vector3 up = Up(p);
                float a = Vector3.Dot(p, up);
                p += up * (a * (1f / st.verticalStretch - 1f));
            }
            float s = st.noiseScale;
            Vector3 warp = new Vector3(
                Octave(p, s * 0.7f, 51.3f),
                Octave(p, s * 0.7f, 17.9f),
                Octave(p, s * 0.7f, 88.1f)) * st.warpStrength;
            Vector3 q = p + warp;
            float dw = st.detailWeight;
            float ridge = Ridge(q, s, 0f)
                        + Ridge(q, s * 3.1f, 31.7f) * dw
                        + Ridge(q, s * 8.0f, 63.4f) * dw * 0.45f
                        + Ridge(q, s * 17f, 9.2f) * dw * 0.2f;
            return ridge / (1f + dw + dw * 0.45f + dw * 0.2f) - 0.5f;
        }

        /// Horizontal(ish) bands by depth → shelves and ledges on the walls.
        float Strata(Vector3 p)
        {
            if (st.strataAmp <= 0f) return 0f;
            float wob = Octave(p, 0.05f, 7.3f) * 3f;
            float band = (Depth(p) + wob) * st.strataFreq;
            float tri = 1f - Mathf.Abs(2f * (band - Mathf.Floor(band)) - 1f);
            return (tri - 0.5f) * st.strataAmp;
        }

        static float Ridge(Vector3 p, float scale, float seed) => 1f - Mathf.Abs(Octave(p, scale, seed) * 2f);

        static float Octave(Vector3 p, float scale, float seed)
        {
            float xy = Mathf.PerlinNoise(p.x * scale + 3.1f + seed, p.y * scale + 7.7f + seed);
            float yz = Mathf.PerlinNoise(p.y * scale + 11.9f + seed, p.z * scale + 2.3f + seed);
            float zx = Mathf.PerlinNoise(p.z * scale + 5.5f + seed, p.x * scale + 13.1f + seed);
            return (xy + yz + zx) / 3f - 0.5f;
        }

        // ── the hole: where the void meets the terrain ────────────────────

        /// Finds every point where the walkable void crosses the terrain surface
        /// and sizes the TerrainHole cylinder to cover them all. Also fixes the
        /// skirt radius and the terrain height under the outcrop.
        public void ComputeHole()
        {
            var pts = new List<Vector2>();
            foreach (var s in segs)
            {
                float len = (s.b - s.a).magnitude;
                int steps = Mathf.Max(2, Mathf.CeilToInt(len / 0.5f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector3 c = Vector3.Lerp(s.a, s.b, t);
                    float r = Mathf.Lerp(s.ra, s.rb, t), w = Mathf.Lerp(s.wa, s.wb, t), h = Mathf.Lerp(s.ha, s.hb, t);
                    Vector3 n = (s.b - s.a).normalized;
                    Frame(n, Up(c), out Vector3 lat, out Vector3 vert);
                    float g = G(c);
                    // Noise can push the roof ~1 m out, so the crossing band is padded.
                    float top = c.y + h * r + 1.0f, floor = c.y - h * r * FloorSquash - 0.6f;
                    if (g < floor || g > top) continue;          // wholly above or below the ground here
                    for (int k = 0; k < 24; k++)
                    {
                        float a = k / 24f * Mathf.PI * 2f;
                        Vector3 bp = c + lat * (w * r * Mathf.Cos(a) * 1.15f) + vert * (h * r * Mathf.Sin(a) * 1.15f);
                        pts.Add(new Vector2(bp.x, bp.z));
                    }
                }
            }
            foreach (var room in rooms)
            {
                float g = G(room.centre);
                if (room.centre.y + room.h * room.radius + 0.5f < g) continue;
                for (int k = 0; k < 24; k++)
                {
                    float a = k / 24f * Mathf.PI * 2f;
                    pts.Add(new Vector2(room.centre.x + Mathf.Cos(a) * room.w * room.radius * 1.15f,
                                        room.centre.z + Mathf.Sin(a) * room.w * room.radius * 1.15f));
                }
            }

            if (pts.Count == 0)
            {
                L.holeCentre = Vector3.zero; L.holeRadius = 5f;
            }
            else
            {
                // Smallest enclosing circle, the cheap way: bounding-box centre,
                // then a few passes pulling toward the farthest point.
                Vector2 mn = pts[0], mx = pts[0];
                foreach (var q in pts) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                Vector2 cc = (mn + mx) * 0.5f;
                float rad = 0f;
                for (int pass = 0; pass < 40; pass++)
                {
                    rad = 0f; Vector2 far = cc;
                    foreach (var q in pts) { float d = (q - cc).magnitude; if (d > rad) { rad = d; far = q; } }
                    float second = 0f;
                    foreach (var q in pts) { float d = (q - far).magnitude; if (d > second) second = d; }
                    if (second * 0.5f >= rad - 0.05f) break;
                    cc += (far - cc) * 0.08f;
                }
                L.holeCentre = new Vector3(cc.x, 0f, cc.y);
                L.holeRadius = rad + 1.2f;
            }
            skirtR = L.holeRadius + SkirtExtra;
            moundGround = ground.Sample(st.moundCentre.x, st.moundCentre.z);
        }

        // ── features ──────────────────────────────────────────────────────

        float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        /// Marches from a point on a passage axis outward until it leaves the
        /// (noisy) void. Returns false if it never does within `reach`.
        bool WallPoint(Vector3 from, Vector3 dir, float reach, out Vector3 hit)
        {
            hit = from;
            for (float d = 0.2f; d < reach; d += 0.15f)
            {
                Vector3 p = from + dir * d;
                if (VoidField(p) > 0f) { hit = p; return true; }
            }
            return false;
        }

        struct Host { public Vector3 c; public float r, w, h; public Vector3 n, up, lat, vert; public bool room; }

        Host RandomHost()
        {
            // Weighted by length so long passages get their share.
            float total = 0f;
            foreach (var s in segs) if (!s.openAir) total += (s.b - s.a).magnitude;
            foreach (var r in rooms) total += r.radius * 2f;
            float pick = Rand(0f, total), acc = 0f;
            foreach (var s in segs)
            {
                if (s.openAir) continue;
                float len = (s.b - s.a).magnitude;
                acc += len;
                if (pick <= acc)
                {
                    float t = Rand(0.08f, 0.92f);
                    var hst = new Host { c = Vector3.Lerp(s.a, s.b, t), r = Mathf.Lerp(s.ra, s.rb, t), w = Mathf.Lerp(s.wa, s.wb, t), h = Mathf.Lerp(s.ha, s.hb, t), n = (s.b - s.a).normalized };
                    hst.up = Up(hst.c); Frame(hst.n, hst.up, out hst.lat, out hst.vert);
                    return hst;
                }
            }
            var room = rooms[rooms.Count - 1];
            foreach (var r in rooms) { acc += r.radius * 2f; if (pick <= acc) { room = r; break; } }
            var hr = new Host { c = room.centre, r = room.radius, w = room.w, h = room.h, room = true };
            hr.up = Up(hr.c);
            float ang = Rand(0f, Mathf.PI * 2f);
            Frame(Vector3.forward, hr.up, out Vector3 l0, out Vector3 v0);
            Vector3 l1 = Vector3.Cross(hr.up, l0).normalized;
            hr.n = (l0 * Mathf.Cos(ang) + l1 * Mathf.Sin(ang)).normalized;
            Frame(hr.n, hr.up, out hr.lat, out hr.vert);
            return hr;
        }

        public void PlaceFeatures()
        {
            if (segs.Count == 0) return;

            // Stalactites: hang from the roof, tip kept ≥ 2.4 m above the floor.
            for (int i = 0; i < st.stalactites; i++)
            {
                var hst = RandomHost();
                float side = Rand(-0.75f, 0.75f);
                Vector3 dir = (hst.vert * Mathf.Cos(side) + hst.lat * Mathf.Sin(side)).normalized;
                if (!WallPoint(hst.c, dir, hst.r * hst.h * 2.2f, out Vector3 wp)) continue;
                float floorY = -hst.h * hst.r * FloorSquash;           // in vert units from the axis
                float wpV = Vector3.Dot(wp - hst.c, hst.vert);
                float maxLen = Mathf.Max(0.5f, (wpV - floorY) - 2.4f);
                float len = Mathf.Min(Rand(0.9f, 3.2f), maxLen);
                float rb = Rand(0.35f, 0.8f);
                interior.Add(new Feature { kind = FeatureKind.Cone, a = wp + dir * 0.9f, b = wp - hst.up * len, ra = rb * 1.3f, rb = 0.14f });
            }
            // Stalagmites and boulders: on the floor, off the centre line.
            for (int i = 0; i < st.stalagmites + st.boulders + st.rubble; i++)
            {
                bool stal = i < st.stalagmites;
                bool rub = i >= st.stalagmites + st.boulders;
                var hst = RandomHost();
                float lateral = Rand(0.4f, 0.88f) * hst.w * hst.r * (rng.Next(2) == 0 ? -1f : 1f);
                Vector3 from = hst.c + hst.lat * lateral;
                if (!WallPoint(from, -hst.vert, hst.r * hst.h * 2f, out Vector3 wp)) continue;
                if (stal)
                {
                    float len = Rand(0.6f, 2.2f);
                    float rb = Rand(0.45f, 0.9f);
                    interior.Add(new Feature { kind = FeatureKind.Cone, a = wp - hst.up * 0.9f, b = wp + hst.up * len, ra = rb * 1.3f, rb = 0.16f });
                }
                else if (rub)
                {
                    float r = Rand(0.45f, 0.8f);
                    interior.Add(new Feature { kind = FeatureKind.Ellipsoid, a = wp - hst.up * (r * 0.45f), scale = new Vector3(r * Rand(0.8f, 1.3f), r * Rand(0.6f, 0.9f), r * Rand(0.8f, 1.3f)) });
                }
                else
                {
                    float r = Rand(0.7f, 1.8f);
                    interior.Add(new Feature { kind = FeatureKind.Ellipsoid, a = wp - hst.up * (r * 0.35f), scale = new Vector3(r * Rand(0.8f, 1.3f), r * Rand(0.7f, 1.0f), r * Rand(0.8f, 1.3f)) });
                }
            }
            // Blocks: broken slabs lying in the floor, tilted.
            for (int i = 0; i < st.blocks; i++)
            {
                var hst = RandomHost();
                float lateral = Rand(0.45f, 0.9f) * hst.w * hst.r * (rng.Next(2) == 0 ? -1f : 1f);
                if (!WallPoint(hst.c + hst.lat * lateral, -hst.vert, hst.r * hst.h * 2f, out Vector3 wp)) continue;
                Vector3 half = new Vector3(Rand(0.7f, 1.6f), Rand(0.35f, 0.7f), Rand(0.7f, 1.5f));
                var rot = Quaternion.LookRotation(hst.n, hst.up) * Quaternion.Euler(Rand(-25f, 25f), Rand(0f, 360f), Rand(-25f, 25f));
                interior.Add(new Feature { kind = FeatureKind.Box, a = wp - hst.up * (half.y * 0.4f), scale = half, invRot = Quaternion.Inverse(rot) });
            }
            // Columns: floor to roof, off the centre line.
            for (int i = 0; i < st.columns; i++)
            {
                var hst = RandomHost();
                float lateral = Rand(0.45f, 0.72f) * hst.w * hst.r * (rng.Next(2) == 0 ? -1f : 1f);
                Vector3 mid = hst.c + hst.lat * lateral;
                float half = hst.h * hst.r;
                interior.Add(new Feature { kind = FeatureKind.Capsule, a = mid - hst.up * (half * FloorSquash + 1.2f), b = mid + hst.up * (half + 1.5f), ra = Rand(0.5f, 0.95f) });
            }
            // Mouth boulders: on the terrain around the outcrop, clear of the approach.
            for (int i = 0; i < st.mouthBoulders * 3 && CountMouth() < st.mouthBoulders; i++)
            {
                float ang = Rand(0f, Mathf.PI * 2f);
                float rad = Rand(L.holeRadius + 1.5f, L.holeRadius + 7.5f);
                float x = L.holeCentre.x + Mathf.Cos(ang) * rad, z = L.holeCentre.z + Mathf.Sin(ang) * rad;
                if (Mathf.Abs(x) < 4.5f && z < st.moundCentre.z - 2f) continue;      // the way in
                float g = ground.Sample(x, z);
                float r = Rand(0.45f, 1.0f);
                mouth.Add(new Feature { kind = FeatureKind.Ellipsoid, a = new Vector3(x, g - r * 0.3f, z), scale = new Vector3(r * Rand(0.8f, 1.3f), r * Rand(0.7f, 1.0f), r * Rand(0.8f, 1.3f)), mouth = true });
            }
            if (st.mouth == MouthKind.Collapse)
            {
                // Rubble spilling out of the mouth itself.
                for (int i = 0; i < 6; i++)
                {
                    float x = Rand(-3.5f, 3.5f), z = Rand(st.moundCentre.z - st.moundRadii.z - 3f, st.moundCentre.z - st.moundRadii.z + 1f);
                    float g = ground.Sample(x, z);
                    float r = Rand(0.5f, 1.1f);
                    mouth.Add(new Feature { kind = FeatureKind.Ellipsoid, a = new Vector3(x, g - r * 0.35f, z), scale = new Vector3(r * Rand(0.8f, 1.3f), r * 0.8f, r * Rand(0.8f, 1.3f)), mouth = true });
                }
            }
        }

        int CountMouth() => mouth.Count;

        public Bounds ComputeBounds()
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            void Grow(Vector3 c, float r)
            {
                var bb = new Bounds(c, Vector3.one * r * 2f);
                if (first) { b = bb; first = false; } else b.Encapsulate(bb);
            }
            foreach (var s in segs) { Grow(s.a, s.ra * Mathf.Max(s.wa, s.ha)); Grow(s.b, s.rb * Mathf.Max(s.wb, s.hb)); }
            foreach (var r in rooms) Grow(r.centre, r.radius * Mathf.Max(r.w, r.h));
            Grow(new Vector3(L.holeCentre.x, moundGround, L.holeCentre.z), skirtR + 1f);
            if (st.moundRadii.x > 0f)
                Grow(new Vector3(st.moundCentre.x, moundGround + st.moundCentre.y, st.moundCentre.z), Mathf.Max(st.moundRadii.x, Mathf.Max(st.moundRadii.y, st.moundRadii.z)) + 1f);
            b.Expand((st.wallThickness + st.noiseAmp + st.cellSize * 3f) * 2f);
            return b;
        }

        // ── grid sampling ─────────────────────────────────────────────────

        float SampleGrid(float[] grid, Vector3 p)
        {
            Vector3 f = (p - origin) / st.cellSize;
            int x = Mathf.RoundToInt(f.x), y = Mathf.RoundToInt(f.y), z = Mathf.RoundToInt(f.z);
            if (x < 0 || y < 0 || z < 0 || x > nx || y > ny || z > nz) return float.MaxValue;
            return grid[(z * (ny + 1) + y) * (nx + 1) + x];
        }

        float SampleGridTrilinear(float[] grid, Vector3 p)
        {
            Vector3 f = (p - origin) / st.cellSize;
            int x = Mathf.FloorToInt(f.x), y = Mathf.FloorToInt(f.y), z = Mathf.FloorToInt(f.z);
            if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) return float.MaxValue;
            float tx = f.x - x, ty = f.y - y, tz = f.z - z;
            float G8(int dx, int dy, int dz) => grid[((z + dz) * (ny + 1) + (y + dy)) * (nx + 1) + (x + dx)];
            float c00 = Mathf.Lerp(G8(0, 0, 0), G8(1, 0, 0), tx), c10 = Mathf.Lerp(G8(0, 1, 0), G8(1, 1, 0), tx);
            float c01 = Mathf.Lerp(G8(0, 0, 1), G8(1, 0, 1), tx), c11 = Mathf.Lerp(G8(0, 1, 1), G8(1, 1, 1), tx);
            return Mathf.Lerp(Mathf.Lerp(c00, c10, ty), Mathf.Lerp(c01, c11, ty), tz);
        }

        // ── checks ────────────────────────────────────────────────────────

        /// Around the hole, at every angle, the highest rock must be within
        /// 0.6 m of the terrain — so the cut edge is always backed by rock and
        /// the hole never shows the hollow moon, from any distance.
        public bool CheckMouth(out string report)
        {
            int bad = 0; float worst = 0f; float worstAng = 0f;
            float rr = L.holeRadius + 0.45f;
            for (int k = 0; k < 72; k++)
            {
                float a = k * 5f * Mathf.Deg2Rad;
                float x = L.holeCentre.x + Mathf.Cos(a) * rr, z = L.holeCentre.z + Mathf.Sin(a) * rr;
                float g = ground.Sample(x, z);
                float top = float.NegativeInfinity;
                for (float y = g + 9f; y > g - 5f; y -= st.cellSize * 0.5f)
                    if (SampleGridTrilinear(field, new Vector3(x, y, z)) < 0f) { top = y; break; }
                float gap = g - top;
                if (gap > 0.6f) { bad++; if (gap > worst) { worst = gap; worstAng = k * 5f; } }
            }
            report = bad == 0
                ? $"hole r={L.holeRadius:0.00} at ({L.holeCentre.x:0.0}, {L.holeCentre.z:0.0}): rock backs the cut edge at all 72 angles"
                : $"hole r={L.holeRadius:0.00}: {bad}/72 angles have no rock within 0.6 m of the terrain (worst {worst:0.00} m at {worstAng}°)";
            return bad == 0;
        }

        /// Every passage and room must have ≥ ~1.2 m of rock above its roof.
        /// Catches both a roof clipped away by the terrain and an outcrop that
        /// is too small to cover the entrance.
        public bool CheckRoof(out string report)
        {
            int bad = 0; Vector3 where = Vector3.zero; float worst = 0f;
            bool InsideRoom(Vector3 p)
            {
                for (int i = 0; i < rooms.Count; i++)
                    if (RoomField(p, rooms[i], out _, out _) < 0.5f) return true;
                return false;
            }
            void Test(Vector3 c, Vector3 up, float halfH)
            {
                // A tunnel sample that lies inside a room has the ROOM's roof
                // above it, 9 m up — not a missing roof. Rooms test themselves.
                if (InsideRoom(c + up * (halfH + 1.6f))) return;
                // Inside the sinkhole the sky IS the roof: the terrain there is
                // cut away and the ramp is open by design.
                float rXZ = new Vector2(c.x - L.holeCentre.x, c.z - L.holeCentre.z).magnitude;
                if (rXZ < L.holeRadius - 0.5f && c.y > G(c) - 7f) return;
                // Deep passages cannot lose their roof: the only thing that
                // removes rock is the terrain clip, and it stops at the ground.
                // Down there "no rock within 3.5 m" just means another passage
                // runs directly overhead — the solid is still closed. Only the
                // near-surface band is tested.
                if (c.y + halfH < G(c) - 6f) return;
                // March up from the nominal roof: the wall noise moves the rock
                // band in and out by up to a metre, so look for rock ANYWHERE in
                // the next 3.5 m rather than at fixed offsets. A closed solid
                // always has it — unless the terrain clip or a too-small
                // outcrop took it away, which is exactly what this catches.
                Vector3 top = c + up * halfH;
                float lowest = float.MaxValue;
                for (float k = 0.3f; k <= 3.5f; k += 0.15f)
                {
                    float f = SampleGridTrilinear(field, top + up * k);
                    if (f < 0f) return;
                    lowest = Mathf.Min(lowest, f);
                }
                bad++;
                if (lowest > worst) { worst = lowest; where = c; }
            }
            foreach (var s in segs)
            {
                float len = (s.b - s.a).magnitude;
                int steps = Mathf.Max(2, Mathf.CeilToInt(len / 1.0f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector3 c = Vector3.Lerp(s.a, s.b, t);
                    float r = Mathf.Lerp(s.ra, s.rb, t), h = Mathf.Lerp(s.ha, s.hb, t);
                    Frame((s.b - s.a).normalized, Up(c), out _, out Vector3 vert);
                    // The open-air approach needs no roof while its floor is
                    // still above the ground; once it dips below, it does.
                    if (s.openAir && (c - vert * (h * r * FloorSquash)).y > G(c)) continue;
                    Test(c, vert, h * r);
                }
            }
            foreach (var room in rooms)
            {
                Vector3 up = Up(room.centre);
                Vector3 top = room.centre + up * (room.h * room.radius);
                if (top.y < G(room.centre) - 6f) continue;      // deep: see above
                bool found = false; float lowest = float.MaxValue;
                for (float k = 0.3f; k <= 3.5f && !found; k += 0.15f)
                {
                    float f = SampleGridTrilinear(field, top + up * k);
                    if (f < 0f) found = true; else lowest = Mathf.Min(lowest, f);
                }
                if (!found) { bad++; if (lowest > worst) { worst = lowest; where = room.centre; } }
            }
            report = bad == 0 ? "every passage and room has rock above it"
                              : $"{bad} roof sample(s) open to nothing — worst near {where} (field {worst:0.00})";
            return bad == 0;
        }

        // ── exposure ──────────────────────────────────────────────────────

        /// Fraction of upper-hemisphere rays (about the local up) that reach
        /// open sky without passing through rock or buried ground.
        public float[] ComputeExposure(Vector3[] v, Vector3[] n, int[] t)
        {
            var dirs = new List<Vector3>();
            dirs.Add(Vector3.up);
            foreach (var (elev, count) in new[] { (72f, 3), (48f, 5), (24f, 8) })
                for (int i = 0; i < count; i++)
                {
                    float az = (i + 0.5f * (elev == 48f ? 1 : 0)) / count * Mathf.PI * 2f;
                    float e = elev * Mathf.Deg2Rad;
                    dirs.Add(new Vector3(Mathf.Cos(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(az) * Mathf.Cos(e)));
                }
            float step = st.cellSize * 0.8f;
            float maxDist = 34f;
            var exposure = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 up = Up(v[i]);
                Frame(Vector3.forward, up, out Vector3 lat, out _);
                Vector3 fwd = Vector3.Cross(lat, up).normalized;                // lat, fwd ⟂ up
                Vector3 start = v[i] + n[i] * (st.cellSize * 0.9f);
                int open = 0;
                for (int d = 0; d < dirs.Count; d++)
                {
                    Vector3 dir = lat * dirs[d].x + up * dirs[d].y + fwd * dirs[d].z;
                    bool blocked = false;
                    for (float s = step; s < maxDist; s += step)
                    {
                        Vector3 p = start + dir * s;
                        float rock = SampleGrid(field, p);
                        if (rock == float.MaxValue)
                        {
                            // Left the grid: sky if above the terrain, buried otherwise.
                            blocked = p.y < ground.Sample(p.x, p.z) - 0.5f;
                            break;
                        }
                        if (rock < 0f) { blocked = true; break; }
                        float g = ground.Sample(p.x, p.z);
                        if (p.y < g - 0.5f && SampleGrid(voidGrid, p) > 0.3f) { blocked = true; break; }
                        if (p.y > g + 2.5f) break;   // clear of the ground: sky
                    }
                    if (!blocked) open++;
                }
                exposure[i] = open / (float)dirs.Count;
            }

            // Smooth over neighbours twice so the facets don't flicker between
            // lit and dark where a single ray grazed something.
            var adj = new List<int>[v.Length];
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                (adj[a] ??= new List<int>()).Add(b); adj[a].Add(c);
                (adj[b] ??= new List<int>()).Add(a); adj[b].Add(c);
                (adj[c] ??= new List<int>()).Add(a); adj[c].Add(b);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                var next = new float[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    if (adj[i] == null) { next[i] = exposure[i]; continue; }
                    float sum = exposure[i]; int cnt = 1;
                    foreach (int j in adj[i]) { sum += exposure[j]; cnt++; }
                    next[i] = sum / cnt;
                }
                exposure = next;
            }
            return exposure;
        }

        // ── colour ────────────────────────────────────────────────────────

        public Color[] VertexColours(Vector3[] v, Vector3[] n, float[] exposure)
        {
            var c = new Color[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 up = Up(v[i]);
                float ny = Vector3.Dot(n[i], up);
                Color col = st.rockTint;
                if (ny > 0.55f) col = Color.Lerp(col, st.floorTint, (ny - 0.55f) / 0.45f * 0.75f);
                else if (Mathf.Abs(ny) < 0.35f) col = Color.Lerp(col, st.steepTint, 0.5f);
                // Above the terrain (the outcrop and the ground inside the hole):
                // drift toward the moon's own grey so the mouth belongs to it.
                float g = ground.Sample(v[i].x, v[i].z);
                float above = Mathf.Clamp01((v[i].y - (g - 0.5f)) / 1.2f);
                col = Color.Lerp(col, st.moonTint, above * 0.85f);
                // Per-vertex variation so no two facets are quite the same.
                float var = 1f + (Octave(v[i], 0.9f, 4.4f)) * 0.28f;
                col *= var;
                col.a = Mathf.Clamp01(exposure[i]);
                c[i] = col;
            }
            return c;
        }

        // ── trim ──────────────────────────────────────────────────────────

        /// Drops triangles that are both far from every void and well under the
        /// terrain — the outer hull nobody can ever see. Reports the boundary
        /// edges this creates and how shallow the shallowest one is, so a
        /// mistake here shows up in the log, not in the game.
        public Vector3 shallowestAt;

        public int[] TrimBuried(Vector3[] v, int[] t, out int openEdges, out float shallowest)
        {
            var kept = new List<int>(t.Length);
            float farVoid = st.wallThickness * 0.6f;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                // EVERY vertex a metre under, so no open edge can ever surface.
                bool buried = true;
                for (int k = 0; k < 3 && buried; k++)
                {
                    Vector3 p = v[t[i + k]];
                    buried = p.y < ground.Sample(p.x, p.z) - 1.0f;
                }
                bool far = buried && SampleGridTrilinear(voidGrid, c) > farVoid;
                if (far) continue;
                kept.Add(t[i]); kept.Add(t[i + 1]); kept.Add(t[i + 2]);
            }
            var arr = kept.ToArray();
            var counts = new Dictionary<long, int>(arr.Length);
            for (int i = 0; i + 2 < arr.Length; i += 3)
            {
                AddEdge(counts, arr[i], arr[i + 1]); AddEdge(counts, arr[i + 1], arr[i + 2]); AddEdge(counts, arr[i + 2], arr[i]);
            }
            openEdges = 0; shallowest = float.MaxValue; shallowestAt = Vector3.zero;
            foreach (var kv in counts)
            {
                // Only an edge used ONCE is a hole. Edges used 3+ times are the
                // non-manifold pinches (stalactite tips) the closure check
                // already reported as cosmetic — they exist everywhere, not
                // only where the trim cut.
                if (kv.Value != 1) continue;
                openEdges++;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
                foreach (int vi in new[] { a, b })
                {
                    float depth = ground.Sample(v[vi].x, v[vi].z) - v[vi].y;
                    if (depth < shallowest) { shallowest = depth; shallowestAt = v[vi]; }
                }
            }
            return arr;
        }
    }

    // ── mesh helpers ─────────────────────────────────────────────────────────

    static void AddQuad(List<int> tris, int[] cellVertex, int c0, int c1, int c2, int c3, bool solidFirst, ref int quadCount)
    {
        int v0 = cellVertex[c0], v1 = cellVertex[c1], v2 = cellVertex[c2], v3 = cellVertex[c3];
        if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) return;
        if (solidFirst)
        {
            tris.Add(v0); tris.Add(v1); tris.Add(v2);
            tris.Add(v0); tris.Add(v2); tris.Add(v3);
        }
        else
        {
            tris.Add(v0); tris.Add(v2); tris.Add(v1);
            tris.Add(v0); tris.Add(v3); tris.Add(v2);
        }
        quadCount++;
    }

    /// Split every triangle into its own three vertices with the face normal,
    /// so the rock shades as facets. UVs are a cheap triplanar projection (the
    /// shader does real triplanar from object position; these only feed tangents).
    static Mesh Facet(Vector3[] v, Color[] colours, int[] tris)
    {
        int n = tris.Length;
        var fv = new Vector3[n];
        var fn = new Vector3[n];
        var fc = new Color[n];
        var fuv = new Vector2[n];
        var ft = new int[n];
        for (int i = 0; i < n; i += 3)
        {
            Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
            Vector3 fnorm = Vector3.Cross(b - a, c - a);
            float m = fnorm.magnitude;
            fnorm = m > 1e-8f ? fnorm / m : Vector3.up;
            for (int k = 0; k < 3; k++)
            {
                int src = tris[i + k];
                fv[i + k] = v[src];
                fn[i + k] = fnorm;
                fc[i + k] = colours[src];
                Vector3 p = v[src];
                float ax = Mathf.Abs(fnorm.x), ay = Mathf.Abs(fnorm.y), az = Mathf.Abs(fnorm.z);
                const float Scale = 0.25f;
                if (ax >= ay && ax >= az) fuv[i + k] = new Vector2(p.z, p.y) * Scale;
                else if (ay >= az) fuv[i + k] = new Vector2(p.x, p.z) * Scale;
                else fuv[i + k] = new Vector2(p.x, p.y) * Scale;
                ft[i + k] = i + k;
            }
        }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = fv;
        mesh.normals = fn;
        mesh.colors = fc;
        mesh.uv = fuv;
        mesh.triangles = ft;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    static void FixOrientation(Mesh mesh)
    {
        var v = mesh.vertices;
        var t = mesh.triangles;
        double volume = 0.0;
        for (int i = 0; i + 2 < t.Length; i += 3)
            volume += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6.0;
        if (volume >= 0.0) return;
        for (int i = 0; i + 2 < t.Length; i += 3) (t[i], t[i + 2]) = (t[i + 2], t[i]);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
    }

    public static double SignedVolume(Mesh mesh)
    {
        var v = mesh.vertices;
        var t = mesh.triangles;
        double volume = 0.0;
        for (int i = 0; i + 2 < t.Length; i += 3)
            volume += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6.0;
        return volume;
    }

    /// Edges used once are literal holes (fatal); edges used 3+ times are
    /// non-manifold pinches (cosmetic — still watertight).
    public static void CountEdgeDefects(Mesh mesh, out int holes, out int nonManifold, out Bounds where)
    {
        var v = mesh.vertices;
        var t = mesh.triangles;
        var counts = new Dictionary<long, int>(t.Length);
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            AddEdge(counts, t[i], t[i + 1]);
            AddEdge(counts, t[i + 1], t[i + 2]);
            AddEdge(counts, t[i + 2], t[i]);
        }
        holes = 0; nonManifold = 0;
        where = new Bounds();
        bool first = true;
        foreach (var kv in counts)
        {
            if (kv.Value == 2) continue;
            if (kv.Value == 1) holes++; else nonManifold++;
            int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
            if (a >= v.Length || b >= v.Length) continue;
            if (first) { where = new Bounds(v[a], Vector3.zero); first = false; }
            where.Encapsulate(v[a]);
            where.Encapsulate(v[b]);
        }
    }

    public static int CountBoundaryEdges(Mesh mesh, out Bounds where)
    {
        CountEdgeDefects(mesh, out int holes, out int nonManifold, out where);
        return holes + nonManifold;
    }

    static void AddEdge(Dictionary<long, int> counts, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        counts.TryGetValue(key, out int n);
        counts[key] = n + 1;
    }

    // ── SDF primitives ───────────────────────────────────────────────────────

    static float SMin(float a, float b, float k = BlendRadius)
    {
        if (a == float.MaxValue) return b;
        if (b == float.MaxValue) return a;
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    static float SdEllipsoid(Vector3 p, Vector3 r)
    {
        Vector3 q1 = new Vector3(p.x / r.x, p.y / r.y, p.z / r.z);
        Vector3 q2 = new Vector3(p.x / (r.x * r.x), p.y / (r.y * r.y), p.z / (r.z * r.z));
        float k0 = q1.magnitude, k1 = q2.magnitude;
        return k1 < 1e-6f ? -Mathf.Min(r.x, Mathf.Min(r.y, r.z)) : k0 * (k0 - 1f) / k1;
    }

    static float SdCapsule(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        Vector3 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Mathf.Max(1e-6f, Vector3.Dot(ba, ba)));
        return (pa - ba * h).magnitude - r;
    }

    // Inigo Quilez's capped cone, exact.
    static float SdCappedCone(Vector3 p, Vector3 a, Vector3 b, float ra, float rb)
    {
        float rba = rb - ra;
        Vector3 ba = b - a;
        float baba = Vector3.Dot(ba, ba);
        Vector3 pa = p - a;
        float papa = Vector3.Dot(pa, pa);
        float paba = Vector3.Dot(pa, ba) / Mathf.Max(1e-6f, baba);
        float x = Mathf.Sqrt(Mathf.Max(0f, papa - paba * paba * baba));
        float cax = Mathf.Max(0f, x - (paba < 0.5f ? ra : rb));
        float cay = Mathf.Abs(paba - 0.5f) - 0.5f;
        float k = rba * rba + baba;
        float f = Mathf.Clamp01((rba * (x - ra) + paba * baba) / Mathf.Max(1e-6f, k));
        float cbx = x - ra - f * rba;
        float cby = paba - f;
        float s = (cbx < 0f && cay < 0f) ? -1f : 1f;
        return s * Mathf.Sqrt(Mathf.Min(cax * cax + cay * cay * baba, cbx * cbx + cby * cby * baba));
    }

    static float SdRoundBox(Vector3 p, Vector3 half, float r)
    {
        Vector3 q = new Vector3(Mathf.Abs(p.x) - half.x, Mathf.Abs(p.y) - half.y, Mathf.Abs(p.z) - half.z);
        Vector3 qp = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f));
        return qp.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - r;
    }
}
