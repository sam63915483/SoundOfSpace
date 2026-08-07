using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cave as a SOLID with thick walls, built from a distance field.
///
/// WHY THIS REPLACED THE SWEPT-SURFACE VERSION
/// The old generator swept a paper-thin surface: a rim piece and a tunnel piece
/// that had to meet exactly. Every bug it produced was the same bug wearing a
/// different hat — a strip whose single visible face pointed the wrong way, so
/// you looked straight through solid-looking rock into the hollow planet. Making
/// one strip double-sided just moved the hole somewhere else.
///
/// A solid cannot have that bug. The mesh here is the closed boundary of a
/// volume of ROCK: an inner wall, an outer wall, and the rim where they join at
/// the entrance — all one watertight surface with every normal pointing out of
/// the rock. There is no "mouth" and no "tunnel" to line up, because there are
/// no separate pieces: the field is unioned before any triangle exists.
///
/// It also makes branches and rooms free. Adding a side passage is one more
/// capsule in the union — min(a, b) — with no seam to reconcile.
///
/// HOW IT'S BUILT
///   1. VoidField      — the air you walk through: capsules along the tunnels
///                       plus spheres for rooms, with a flattened floor and
///                       noisy walls.
///   2. RockField      — a shell of `WallThickness` around that void, unioned
///                       with a rim mound at the entrance, then the void
///                       subtracted back out so the entrance stays open.
///   3. Surface Nets   — polygonises RockField &lt; 0 into a closed mesh.
///
/// Surface Nets rather than Marching Cubes: no 4096-entry lookup table, the
/// output is manifold by construction, and it gives smoother, more organic rock.
/// </summary>
public static class CaveSolid
{
    // ── Tunables ─────────────────────────────────────────────────────────────

    /// Grid resolution in metres. Smaller = finer rock, more triangles.
    public const float CellSize = 0.5f;

    /// How thick the rock walls are. This is the entire point of the rewrite —
    /// there is no paper-thin geometry anywhere.
    public const float WallThickness = 2.2f;

    /// Void radius is squashed by this below the axis, giving a walkable floor
    /// instead of the bottom of a pipe. Only applied to near-horizontal runs —
    /// the entrance shaft drops vertically and has no meaningful floor.
    const float FloorSquash = 0.55f;

    /// Organic wall roughness, in metres of displacement. Two octaves: the low
    /// one gives the walls their bulges, the high one the broken-rock detail.
    /// Keep the TOTAL comfortably under WallThickness or the roughness can thin
    /// the rock to sub-cell features and break the closure check.
    // Kept to ~1.0 so it can't pinch a passage shut. Roughness comes from the
    // RIDGES (creases and edges), not from sheer displacement — cranking this up
    // just closes tunnels, which is what made some caverns unreachable.
    const float NoiseAmplitude = 1.0f;
    const float NoiseScale = 0.11f;
    const float DetailScale = 0.34f;   // second octave
    const float DetailWeight = 0.5f;
    /// Metres the sample position is displaced before reading the ridges. This
    /// is what stops them running in neat parallel bands.
    const float WarpStrength = 3.5f;

    // The entrance rim: a squashed torus sitting in the ground around the mouth,
    // so the opening reads as a rocky crater rather than a pipe in the dirt. Its
    // outer edge must clear the TerrainHole cut, or the terrain's cut edge is
    // left showing.
    const float RimMajor = 6.6f;    // ring centre-line radius
    const float RimMinor = 2.9f;    // ring tube radius → covers 3.7 .. 9.5
    const float RimSquash = 0.55f;  // flattened vertically
    const float RimHeight = -0.4f;  // centre height; buries the outer edge

    // THE WAY IN.
    //
    // A solid SEALS ITSELF: the envelope wraps the void's top cap too, so the
    // entrance gets a 2.2 m dome of rock over it. The void reaching above ground
    // is not enough — the rock has to be cut away.
    //
    // The cut is a horizontal plane at ground level, applied to the ENVELOPE
    // only (never the rim, which is meant to stand proud). Everything else was
    // tried and failed the closure check: a punched cylinder slices a razor ring
    // off the dome where the two surfaces cross at a shallow angle, and a wider
    // one then slices the descending tunnel's roof the same way. A horizontal
    // plane cuts the dome well below its top, which is a transversal
    // intersection — a clean circle, no sub-cell slivers.
    //
    // Above this plane the only cave rock is the rim, and the rim's inner edge
    // (~3.8) sits outside the entrance void (~3.0), so the way in stays open.
    const float GroundCutY = 0f;

    /// A tunnel or branch: a run of centre points with a radius at each.
    public struct Segment
    {
        public Vector3 a, b;
        public float ra, rb;
    }

    /// A room — a sphere the tunnels open into.
    public struct Room
    {
        public Vector3 centre;
        public float radius;
    }

    // ── Public entry point ───────────────────────────────────────────────────

    public static Mesh Build(List<Segment> segments, List<Room> rooms, out int quadCount)
    {
        Bounds b = ComputeBounds(segments, rooms);

        int nx = Mathf.CeilToInt(b.size.x / CellSize) + 1;
        int ny = Mathf.CeilToInt(b.size.y / CellSize) + 1;
        int nz = Mathf.CeilToInt(b.size.z / CellSize) + 1;
        Vector3 origin = b.min;

        // Sample the field on the grid corners once. Everything else reads this.
        var field = new float[(nx + 1) * (ny + 1) * (nz + 1)];
        int Idx(int x, int y, int z) => (z * (ny + 1) + y) * (nx + 1) + x;

        for (int z = 0; z <= nz; z++)
            for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    Vector3 p = origin + new Vector3(x, y, z) * CellSize;
                    float d = RockField(p, segments, rooms);

                    // Force the outermost shell of grid samples to read as EMPTY.
                    // Surface Nets can only emit a quad for an edge that has all
                    // four surrounding cells, which an edge on the grid boundary
                    // does not — so a surface touching the border leaves open
                    // edges, i.e. a literal hole. The bounds are padded so this
                    // should never bite, but "should" is what produced 24
                    // boundary edges on the first run.
                    if (x == 0 || y == 0 || z == 0 || x == nx || y == ny || z == nz)
                        d = Mathf.Max(d, CellSize);

                    field[Idx(x, y, z)] = d;
                }

        // ── Surface Nets ─────────────────────────────────────────────────────
        // One vertex per cell that straddles the surface, placed at the average
        // of the crossings on that cell's twelve edges. Then one quad per grid
        // edge that changes sign, joining the four cells around it. Manifold by
        // construction: every quad's four corners are cells that must exist,
        // because they all touch a sign-changing edge.
        var cellVertex = new int[nx * ny * nz];
        for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;
        int CellIdx(int x, int y, int z) => (z * ny + y) * nx + x;

        var verts = new List<Vector3>();
        // The 12 edges of a cell, as corner-index pairs into the 8 corners below.
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
                    if (mask == 0 || mask == 255) continue;   // wholly in or out

                    Vector3 sum = Vector3.zero;
                    int crossings = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        int c0 = edges[e, 0], c1 = edges[e, 1];
                        bool in0 = corner[c0] < 0f, in1 = corner[c1] < 0f;
                        if (in0 == in1) continue;
                        float t = corner[c0] / (corner[c0] - corner[c1]);
                        Vector3 p0 = (Vector3)cornerOffset[c0], p1 = (Vector3)cornerOffset[c1];
                        sum += Vector3.Lerp(p0, p1, t);
                        crossings++;
                    }
                    if (crossings == 0) continue;

                    cellVertex[CellIdx(x, y, z)] = verts.Count;
                    verts.Add(origin + (new Vector3(x, y, z) + sum / crossings) * CellSize);
                }

        var tris = new List<int>();
        quadCount = 0;

        // A quad for each sign-changing grid edge. Only interior edges are
        // considered, so every one of the four surrounding cells exists.
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    bool solid = field[Idx(x, y, z)] < 0f;

                    // +X edge → quad in the YZ plane
                    if (y > 0 && z > 0 && solid != (field[Idx(x + 1, y, z)] < 0f))
                        AddQuad(tris, cellVertex, CellIdx(x, y - 1, z - 1), CellIdx(x, y, z - 1),
                                CellIdx(x, y, z), CellIdx(x, y - 1, z), solid, ref quadCount);

                    // +Y edge → quad in the XZ plane
                    if (x > 0 && z > 0 && solid != (field[Idx(x, y + 1, z)] < 0f))
                        AddQuad(tris, cellVertex, CellIdx(x - 1, y, z - 1), CellIdx(x - 1, y, z),
                                CellIdx(x, y, z), CellIdx(x, y, z - 1), solid, ref quadCount);

                    // +Z edge → quad in the XY plane
                    if (x > 0 && y > 0 && solid != (field[Idx(x, y, z + 1)] < 0f))
                        AddQuad(tris, cellVertex, CellIdx(x - 1, y - 1, z), CellIdx(x, y - 1, z),
                                CellIdx(x, y, z), CellIdx(x - 1, y, z), solid, ref quadCount);
                }

        var mesh = new Mesh { name = "Cave_Solid" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();

        FixOrientation(mesh);
        mesh.SetUVs(0, TriplanarUVs(mesh));
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    static void AddQuad(List<int> tris, int[] cellVertex, int c0, int c1, int c2, int c3,
                        bool solidFirst, ref int quadCount)
    {
        int v0 = cellVertex[c0], v1 = cellVertex[c1], v2 = cellVertex[c2], v3 = cellVertex[c3];
        if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) return;   // shouldn't happen; cheap guard

        // Which way round depends on which end of the edge is inside the rock,
        // so the whole surface ends up consistently wound. FixOrientation below
        // verifies the result rather than trusting this.
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

    // A closed mesh wound outward has POSITIVE signed volume. If it came out
    // negative the whole surface is inside-out — flip once, globally.
    //
    // This is the check the old generator never had: it guessed at winding from
    // one sample triangle, which is exactly how a mouth ended up invisible from
    // outside while measuring perfectly solid.
    static void FixOrientation(Mesh mesh)
    {
        var v = mesh.vertices;
        var t = mesh.triangles;
        double volume = 0.0;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }
        if (volume >= 0.0) return;

        for (int i = 0; i + 2 < t.Length; i += 3) (t[i], t[i + 2]) = (t[i + 2], t[i]);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
    }

    /// Signed volume of the finished mesh. Positive and non-trivial means the
    /// surface really is closed and outward-facing. Used by the generator's
    /// self-check.
    public static double SignedVolume(Mesh mesh)
    {
        var v = mesh.vertices;
        var t = mesh.triangles;
        double volume = 0.0;
        for (int i = 0; i + 2 < t.Length; i += 3)
            volume += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6.0;
        return volume;
    }

    /// Every edge of a closed manifold is shared by exactly two triangles. Any
    /// edge used once is a literal hole in the mesh — which is what "I can see
    /// through the planet" looks like from the inside.
    /// Splits the two very different failures an edge count can represent:
    ///
    ///   • used ONCE  → a genuine boundary: a hole in the surface. This is the
    ///     see-through bug, and it's fatal.
    ///   • used 3+    → non-manifold: three faces meeting along one edge, which
    ///     happens where a feature is about to pinch out at grid resolution. The
    ///     surface is still WATERTIGHT — nothing renders through it and the
    ///     collider is solid — so it's a warning, not a failure.
    ///
    /// Treating both as fatal is what stalled generation on a single edge sitting
    /// exactly where the ground clip runs tangent to a tunnel's dome.
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
        var v = mesh.vertices;
        var t = mesh.triangles;
        var counts = new Dictionary<long, int>(t.Length);
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            AddEdge(counts, t[i], t[i + 1]);
            AddEdge(counts, t[i + 1], t[i + 2]);
            AddEdge(counts, t[i + 2], t[i]);
        }

        int open = 0;
        where = new Bounds();
        bool first = true;
        foreach (var kv in counts)
        {
            if (kv.Value == 2) continue;
            open++;
            // Report WHERE, so a hole can be traced to the layout that caused it
            // instead of guessed at.
            int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
            if (a >= v.Length || b >= v.Length) continue;
            if (first) { where = new Bounds(v[a], Vector3.zero); first = false; }
            where.Encapsulate(v[a]);
            where.Encapsulate(v[b]);
        }
        return open;
    }

    static void AddEdge(Dictionary<long, int> counts, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        counts.TryGetValue(key, out int n);
        counts[key] = n + 1;
    }

    // ── The field ────────────────────────────────────────────────────────────

    /// Negative inside the ROCK: an envelope around the whole cave, plus the
    /// entrance rim, with the walkable void subtracted back out.
    ///
    /// NOT a shell of the void — that was the first attempt and it fails on
    /// branches. A band `0 &lt; dVoid &lt; thickness` wraps each passage
    /// individually, so two passages running 5 m apart get a 0.6 m slab of AIR
    /// trapped between their two shells. Features thinner than a grid cell are
    /// exactly what Surface Nets cannot represent — one vertex per cell can't
    /// hold two sheets — and the mesh came out with 24 open edges right where
    /// the branches diverge.
    ///
    /// An ENVELOPE (union of the same passages, fattened) merges wherever
    /// passages are close, so no thin air can be trapped, and the rock between
    /// two passages is simply however far apart they are.
    static float RockField(Vector3 p, List<Segment> segments, List<Room> rooms)
    {
        float dVoid = VoidField(p, segments, rooms);
        float dHull = HullField(p, segments, rooms);

        // Envelope, clipped to below ground so it can't cap the entrance.
        float dHullClipped = Mathf.Max(dHull, p.y - GroundCutY);
        float rock = Mathf.Max(dHullClipped, -dVoid);  // (envelope ∩ underground) \ void
        float rim = Mathf.Max(RimField(p), -dVoid);   // rim \ void — keeps the way in open

        // Blended, not a hard min. Where the rim's top rides just clear of the
        // tunnel's outer hull there's a wedge of air between the two that
        // narrows to nothing — a sub-cell feature, and it opened a ring of 19
        // boundary edges right around the entrance. A smooth union merges them
        // into one mass of rock before the wedge can get thin enough to matter.
        // Smaller k than the void's: this one can bulge inward, and the entrance
        // is where that would be noticed.
        return SMin(rock, rim, 2.0f);
    }

    /// The outer skin of the rock: the same passages fattened by the wall
    /// thickness. Smooth — no floor clamp and no noise — because it's buried and
    /// its only job is to be comfortably outside every void.
    static float HullField(Vector3 p, List<Segment> segments, List<Room> rooms)
    {
        float d = float.MaxValue;
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            Vector3 ab = s.b - s.a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / len2);
            float r = Mathf.Lerp(s.ra, s.rb, t) + WallThickness;
            // Blended for the same reason as the void: a creased envelope
            // produces sub-cell features on its own outer surface.
            d = SMin(d, (p - (s.a + ab * t)).magnitude - r);
        }
        for (int i = 0; i < rooms.Count; i++)
            d = SMin(d, (p - rooms[i].centre).magnitude - (rooms[i].radius + WallThickness));
        // A little roughness on the outside too — the crater rim is the one part
        // of the envelope the player actually sees. Kept well under the wall
        // thickness so it can't thin the rock to sub-cell features.
        return d + Noise(p) * NoiseAmplitude * 0.7f;
    }

    /// Negative inside the walkable void.
    static float VoidField(Vector3 p, List<Segment> segments, List<Room> rooms)
    {
        float d = float.MaxValue;
        for (int i = 0; i < segments.Count; i++) d = SMin(d, SegmentField(p, segments[i]));
        for (int i = 0; i < rooms.Count; i++) d = SMin(d, RoomField(p, rooms[i]));
        return d + Noise(p) * NoiseAmplitude;
    }

    /// Smooth minimum — a union that BLENDS instead of creasing.
    ///
    /// A hard min() leaves a sharp crease where two passages meet, and the rock
    /// wedge between them starts at literally zero thickness and grows. Surface
    /// Nets holds one vertex per cell, so any feature thinner than a cell can't
    /// be represented and comes out as open edges — which is what a branching
    /// cave produced: 21 boundary edges right at the junctions. Blending gives
    /// the junction a fillet, so the wedge is never sub-cell, and a rounded
    /// Y-junction looks more like real rock anyway.
    // Must stay comfortably above CellSize — the fillet it creates is what
    // guarantees no feature at a junction is thinner than a grid cell. At 2.0 a
    // single ambiguous cell still survived where a branch leaves at a sharp
    // angle (2 boundary edges); 2.8 clears it with room to spare.
    const float BlendRadius = 2.8f;

    static float SMin(float a, float b, float k = BlendRadius)
    {
        if (a == float.MaxValue) return b;
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    static float SegmentField(Vector3 p, Segment s)
    {
        Vector3 ab = s.b - s.a;
        float len2 = ab.sqrMagnitude;
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / len2);
        Vector3 c = s.a + ab * t;
        float r = Mathf.Lerp(s.ra, s.rb, t);
        float d = (p - c).magnitude - r;

        // Flatten the floor on runs that are near-horizontal. On the entrance
        // shaft, where the tunnel IS the vertical, a floor makes no sense and
        // clamping there would slice the shaft off.
        float horizontality = 1f - Mathf.Abs(ab.normalized.y);
        if (horizontality > 0.55f)
        {
            float floorY = c.y - r * FloorSquash;
            float floorCut = floorY - p.y;                      // >0 below the floor
            d = Mathf.Max(d, floorCut * Mathf.InverseLerp(0.55f, 0.8f, horizontality));
        }
        return d;
    }

    static float RoomField(Vector3 p, Room room)
    {
        float d = (p - room.centre).magnitude - room.radius;
        float floorY = room.centre.y - room.radius * FloorSquash;
        return Mathf.Max(d, floorY - p.y);
    }

    /// The entrance rim: the upper half of a vertically squashed torus, sitting
    /// in the ground around the mouth.
    ///
    /// The half matters. A whole torus tapers to a FEATHER EDGE at its inner and
    /// outer silhouettes — the cross-section thins to nothing — and a feather
    /// edge is by definition thinner than a grid cell somewhere, which left one
    /// stubborn pair of boundary edges out at radius ~9.4. Slicing it through
    /// the middle replaces that with a flat annulus cut transversally, so the
    /// rock is a full cross-section thick right up to its edge. The cut sits
    /// just under ground level, so it's buried.
    const float RimBottomY = -0.6f;

    static float RimField(Vector3 p)
    {
        Vector3 q = p - new Vector3(0f, RimHeight, 0f);
        float radial = new Vector2(q.x, q.z).magnitude - RimMajor;
        float vertical = q.y / RimSquash;
        float torus = new Vector2(radial, vertical).magnitude - RimMinor;
        return Mathf.Max(torus, RimBottomY - p.y);
    }

    // Deterministic 3D value noise from three Perlin slices — Mathf has no 3D
    // Perlin, and Random would give a different cave on every regeneration.
    /// RIDGED noise, not plain Perlin.
    ///
    /// Plain Perlin is smooth everywhere, so summing it around a tube gives you
    /// bulges — which is why the first cave read as "soft and round" rather than
    /// rocky. Folding the noise at zero (1 - |n|) puts a CREASE wherever it
    /// crosses, and creases are what read as rock: edges, ledges, fracture
    /// lines. Domain warping — displacing the sample position by another noise
    /// field — then bends those creases so they don't run in obvious parallel
    /// bands.
    static float Noise(Vector3 p)
    {
        // Warp first: without this the ridges line up along the noise grid and
        // look woven rather than broken.
        Vector3 warp = new Vector3(
            Octave(p, NoiseScale * 0.7f, 51.3f),
            Octave(p, NoiseScale * 0.7f, 17.9f),
            Octave(p, NoiseScale * 0.7f, 88.1f)) * WarpStrength;
        Vector3 q = p + warp;

        float ridge = Ridge(q, NoiseScale, 0f)
                    + Ridge(q, DetailScale, 31.7f) * DetailWeight
                    + Ridge(q, DetailScale * 2.6f, 63.4f) * DetailWeight * 0.45f;

        // Ridged noise is one-sided (always ≥ 0), so re-centre it or the whole
        // cave inflates.
        return ridge / (1f + DetailWeight + DetailWeight * 0.45f) - 0.5f;
    }

    static float Ridge(Vector3 p, float scale, float seed)
        => 1f - Mathf.Abs(Octave(p, scale, seed) * 2f);

    static float Octave(Vector3 p, float scale, float seed)
    {
        float xy = Mathf.PerlinNoise(p.x * scale + 3.1f + seed, p.y * scale + 7.7f + seed);
        float yz = Mathf.PerlinNoise(p.y * scale + 11.9f + seed, p.z * scale + 2.3f + seed);
        float zx = Mathf.PerlinNoise(p.z * scale + 5.5f + seed, p.x * scale + 13.1f + seed);
        return (xy + yz + zx) / 3f - 0.5f;
    }

    // ── Support ──────────────────────────────────────────────────────────────

    static Bounds ComputeBounds(List<Segment> segments, List<Room> rooms)
    {
        var b = new Bounds(Vector3.zero, Vector3.zero);
        bool first = true;
        void Grow(Vector3 c, float r)
        {
            var bb = new Bounds(c, Vector3.one * r * 2f);
            if (first) { b = bb; first = false; } else b.Encapsulate(bb);
        }
        foreach (var s in segments) { Grow(s.a, s.ra); Grow(s.b, s.rb); }
        foreach (var r in rooms) Grow(r.centre, r.radius);
        Grow(new Vector3(0f, RimHeight, 0f), RimMajor + RimMinor);

        // Room for the wall thickness, the noise, and three empty cells of pad
        // so the surface never touches the grid boundary (which would leave open
        // edges — a real hole in the mesh).
        b.Expand((WallThickness + NoiseAmplitude + CellSize * 3f) * 2f);
        return b;
    }

    // Surface Nets gives no UVs. Project each vertex along its dominant normal
    // axis — cheap triplanar-style mapping that keeps the rock texture roughly
    // the same size everywhere and never stretches badly.
    static List<Vector2> TriplanarUVs(Mesh mesh)
    {
        const float Scale = 0.25f;   // texture repeats every 4 m
        var verts = mesh.vertices;
        var normals = mesh.normals;
        var uvs = new List<Vector2>(verts.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 n = normals[i], p = verts[i];
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) uvs.Add(new Vector2(p.z, p.y) * Scale);
            else if (ay >= az) uvs.Add(new Vector2(p.x, p.z) * Scale);
            else uvs.Add(new Vector2(p.x, p.y) * Scale);
        }
        return uvs;
    }
}
