using UnityEngine;

/// <summary>
/// Shared static utilities for the four cell-streaming world-prop spawners
/// (TreeSpawner, MushroomSpawner, AlienNPCSpawner, CrystalSpawner). Pulled out
/// of the spawners to kill ~500 lines of byte-identical copy-paste.
///
/// Contains:
///   • Cubeface streaming math (Hash, EncodeCell, FaceUVToDirection,
///     DirectionToFaceUV) — used by all four spawners to convert between cell
///     IDs and points on the planet sphere.
///   • Prefab seating helper (ComputeLocalBottomY) — used by MushroomSpawner
///     and CrystalSpawner to seat the model on the surface regardless of
///     pivot. AlienNPCSpawner uses a different (Instantiate-and-measure)
///     approach for skinned-mesh aliens; TreeSpawner doesn't need bottomY
///     correction.
///   • WorldProp layer constants + SetLayerRecursively — all spawned props
///     belong on the WorldProp layer (3) so the four spawners can exclude it
///     from each other's surface raycasts. Stops the "mushroom spawned on top
///     of a crystal" cross-contamination cleanly via mask, not collider shape.
/// </summary>
public static class SpawnerCubeface
{
    // ── Layer ─────────────────────────────────────────────────────────────

    /// Layer index for everything the world spawners produce. Defined in
    /// ProjectSettings/TagManager.asset at slot 3 (formerly empty). Spawners
    /// should set every spawned instance to this layer AND OR-out the layer
    /// bit from their own groundMask so a tree's surface raycast doesn't
    /// land mushrooms on top of a crystal, etc.
    public const int WorldPropLayer = 3;
    public const int WorldPropLayerMask = 1 << WorldPropLayer;

    // Ship layer (slot 9 in TagManager). Surface raycasts must exclude this —
    // otherwise a ship flying low (below surfaceRayHeight) sits between the
    // rayOrigin and the ground, so the cast lands on the hull instead of the
    // surface. The slope check then passes for the roughly-horizontal hull top
    // and the prop spawns on the ship; the ship flies on and leaves the prop
    // floating in mid-air along the old flight path.
    public const int ShipLayer = 9;
    public const int ShipLayerMask = 1 << ShipLayer;

    /// Combined mask of layers the four world-prop spawners must NEVER hit
    /// with their surface raycast. WorldProp keeps spawners from stacking on
    /// each other's instances; Ship keeps low-flying ships from acting as a
    /// false surface.
    public const int WorldSpawnExcludeMask = WorldPropLayerMask | ShipLayerMask;

    /// Set the layer of `go` and every child, grandchild, etc. Spawned props
    /// often have child renderers/colliders that ship with their own layer
    /// assignments; we want the entire hierarchy on WorldProp so raycasts
    /// excluding the layer skip the whole thing.
    public static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        var t = go.transform;
        int n = t.childCount;
        for (int i = 0; i < n; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }

    // ── Cell encoding ─────────────────────────────────────────────────────

    /// Pack (face, cellU, cellV) into a single 64-bit cell ID. Used as the
    /// dictionary key for activeMushrooms / activeTrees / activeCrystals /
    /// consumedCells. The OFFSET keeps negative cell coordinates packable.
    public static long EncodeCell(int face, int cellU, int cellV)
    {
        const long OFFSET = 1L << 19;
        long u = (cellU + OFFSET) & 0xFFFFFL;
        long v = (cellV + OFFSET) & 0xFFFFFL;
        return ((long)(face & 0x7) << 40) | (u << 20) | v;
    }

    // ── Cubeface ↔ sphere direction ───────────────────────────────────────

    /// Convert (face, faceU, faceV) → world-space direction on the unit cube
    /// (then projected to sphere via .normalized). Faces are X+/X-/Y+/Y-/Z+/Z-.
    public static Vector3 FaceUVToDirection(int face, float u, float v)
    {
        Vector3 d;
        switch (face)
        {
            case 0: d = new Vector3( 1f,  v, -u); break;
            case 1: d = new Vector3(-1f,  v,  u); break;
            case 2: d = new Vector3( u,   1f, v); break;
            case 3: d = new Vector3( u,  -1f,-v); break;
            case 4: d = new Vector3( u,   v,  1f); break;
            case 5: d = new Vector3(-u,   v, -1f); break;
            default: return Vector3.zero;
        }
        return d.normalized;
    }

    /// Inverse of FaceUVToDirection: which face does this world direction
    /// belong to, and what's its (u, v) on that face. Used by TreeSpawner
    /// when registering pre-placed scene trees against the cubeface grid.
    public static void DirectionToFaceUV(Vector3 dir, out int face, out float u, out float v)
    {
        float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y), az = Mathf.Abs(dir.z);
        if (ax >= ay && ax >= az)
        {
            if (dir.x >= 0f) { face = 0; u = -dir.z / dir.x;  v = dir.y / dir.x; }
            else             { face = 1; u =  dir.z / -dir.x; v = dir.y / -dir.x; }
        }
        else if (ay >= ax && ay >= az)
        {
            if (dir.y >= 0f) { face = 2; u = dir.x / dir.y;  v = dir.z / dir.y; }
            else             { face = 3; u = dir.x / -dir.y; v = -dir.z / -dir.y; }
        }
        else
        {
            if (dir.z >= 0f) { face = 4; u = dir.x / dir.z;  v = dir.y / dir.z; }
            else             { face = 5; u = -dir.x / -dir.z; v = dir.y / -dir.z; }
        }
    }

    // ── Per-cell deterministic hash ───────────────────────────────────────

    /// Murmur-style hash mixing seed + face + cellU + cellV + salt. Salts
    /// are how each spawner gets multiple independent random rolls per cell
    /// (jitterU=2, jitterV=3, prefabIdx=4, yaw=5, scale=6, ...). The hash is
    /// stateless and pure — same inputs always produce the same output, which
    /// is what makes the world layout reproducible across save/load.
    public static uint Hash(int seed, int face, int cellU, int cellV, int salt)
    {
        unchecked
        {
            uint h = (uint)seed;
            h = h * 2654435761u + (uint)face;
            h ^= h >> 13;
            h = h * 2654435761u + (uint)cellU;
            h ^= h >> 13;
            h = h * 2654435761u + (uint)cellV;
            h ^= h >> 13;
            h = h * 2654435761u + (uint)salt;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return h;
        }
    }

    // ── Prefab bottom-Y measurement ───────────────────────────────────────

    /// Walks the prefab's MeshFilter + SkinnedMeshRenderer hierarchy and
    /// returns the lowest Y coordinate in prefab-root local space (with the
    /// prefab's authored localScale stripped out by InverseTransformPoint).
    ///
    /// Used by MushroomSpawner, AlienNPCSpawner, and CrystalSpawner to seat
    /// the spawned model so its mesh-bottom sits ON the surface regardless
    /// of where the artist put the pivot. The seating formula is:
    ///     pos = hit.point - up * (bottomY * scale + groundOffset + groundEmbedPerScale * scale)
    ///
    /// Iterates the ACTUAL VERTICES of each mesh (not the AABB) because:
    ///   (a) For child meshes that are rotated relative to the prefab root,
    ///       the AABB bounds the *axis-aligned* extent of the mesh — which
    ///       extends below the actual lowest vertex when projected onto the
    ///       prefab-root Y axis. Result: bottomY too low → model floats.
    ///   (b) For SkinnedMeshRenderer prefabs, smr.localBounds is often
    ///       padded by the importer / artist for culling safety. That
    ///       padding inflates bottomY by several cm, which scales up to
    ///       a visible foot of float on a 5×-scaled alien.
    /// Reading vertex positions directly bypasses both issues. SkinnedMesh-
    /// Renderer vertices give bind-pose positions (sharedMesh is the asset),
    /// which is the right reference because Animator hasn't ticked yet at
    /// the time this is called from Awake.
    public static float ComputeLocalBottomY(GameObject prefab)
    {
        if (prefab == null) return 0f;
        float minY = float.MaxValue;
        bool any = false;

        var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            var mf = meshFilters[i];
            if (mf == null || mf.sharedMesh == null) continue;
            any |= AccumulateVerticesMinY(mf.transform, prefab.transform, mf.sharedMesh, ref minY);
        }

        var skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            var smr = skinned[i];
            if (smr == null || smr.sharedMesh == null) continue;
            any |= AccumulateVerticesMinY(smr.transform, prefab.transform, smr.sharedMesh, ref minY);
        }

        return any ? minY : 0f;
    }

    static bool AccumulateVerticesMinY(Transform from, Transform root, Mesh mesh, ref float minY)
    {
        if (mesh == null) return false;

        // Vertex iteration is the most accurate, but it only works on meshes
        // imported with "Read/Write Enabled" ticked. Almost every FBX in this
        // project ships with R/W disabled (the default — saves memory by
        // discarding the CPU-side vertex buffer after upload to GPU). On
        // those, mesh.vertices returns an empty array and we'd fall through
        // to bottomY=0, which sinks the model into the ground.
        //
        // Fall back to mesh.bounds (always readable, GPU-independent) via
        // the original 8-corner AABB sweep. Less accurate for rotated child
        // meshes — the AABB after rotation extends below the actual lowest
        // vertex — but for axis-aligned single-mesh prefabs (mushrooms,
        // crystals, most static props) the AABB's lowest corner equals the
        // lowest vertex, so this path is exact for the common case.
        if (mesh.isReadable)
        {
            var verts = mesh.vertices;
            if (verts != null && verts.Length > 0)
            {
                bool any = false;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 vWorld = from.TransformPoint(verts[i]);
                    Vector3 vRoot  = root.InverseTransformPoint(vWorld);
                    if (vRoot.y < minY) { minY = vRoot.y; any = true; }
                }
                return any;
            }
        }

        return AccumulateBoundsCornersMinY(from, root, mesh.bounds, ref minY);
    }

    static bool AccumulateBoundsCornersMinY(Transform from, Transform root, Bounds b, ref float minY)
    {
        Vector3 c = b.center, e = b.extents;
        bool any = false;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            Vector3 cornerLocal = c + new Vector3(sx * e.x, sy * e.y, sz * e.z);
            Vector3 cornerWorld = from.TransformPoint(cornerLocal);
            Vector3 cornerRoot  = root.InverseTransformPoint(cornerWorld);
            if (cornerRoot.y < minY) { minY = cornerRoot.y; any = true; }
        }
        return any;
    }
}
