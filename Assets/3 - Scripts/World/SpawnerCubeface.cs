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

    // Water (builtin 4), Sun (11) and FishPreview (12) are never valid ground
    // for a surface prop either — a ray landing on any of them would seat the
    // prop on a false surface the same way a low-flying ship hull does.
    public const int WaterLayer = 4;
    public const int SunLayer = 11;
    public const int FishPreviewLayer = 12;

    /// Combined mask of layers the four world-prop spawners must NEVER hit
    /// with their surface raycast. WorldProp keeps spawners from stacking on
    /// each other's instances; Ship keeps low-flying ships from acting as a
    /// false surface. NOTE: buildings must stay HITTABLE — GrassSpawner rejects
    /// spots whose hit sits under a GrassBlocker, which only works if the ray
    /// can land on the building at all. Never add Default (0) or Body (10) here.
    public const int WorldSpawnExcludeMask = WorldPropLayerMask | ShipLayerMask
        | (1 << WaterLayer) | (1 << SunLayer) | (1 << FishPreviewLayer);

    // -- Cell grid ---------------------------------------------------------

    /// <summary>
    /// Widest a cell may be in face-UV terms. A cube face spans -1..1, so this
    /// guarantees at least six cells across every face of every body.
    /// </summary>
    const float MaxFaceUVPerCell = 1f / 3f;

    /// <summary>Bodies under this radius are "small" and get the finer cap
    /// below. Matches CelestialBodyGenerator's own large-body threshold, so the
    /// split lands in the same place the renderer already puts it.</summary>
    const float SmallBodyRadius = 150f;

    /// <summary>Twelve cells across a face, for small bodies only.</summary>
    const float MaxFaceUVPerCellSmall = 1f / 6f;

    /// <summary>
    /// How wide one spawn cell is in face-UV, for a body of this radius.
    ///
    /// The spawners size their grid as <c>cellSize / bodyRadius</c>, which is
    /// right for a big planet and DEGENERATE for a small one: the cell is
    /// measured in metres, the face is measured in radii, so as the body
    /// shrinks the cells swallow the face. Cell centres outside -1..1 are
    /// discarded, so the number of usable cells collapses:
    ///
    ///     Humble Abode  r=200, cellSize 40  ->  10 cells across a face (~600)
    ///     Anvil         r=70                ->   4 cells (~96)
    ///     Hearth        r=45                ->   2 cells (~24 for the WHOLE planet)
    ///
    /// Twenty-four candidate spots on an entire world, before the per-cell hash,
    /// the waterline and the slope test reject most of them. That is why Sam
    /// found Ember "very sparse" and Hearth bare on 2026-09-09.
    ///
    /// CORRECTION, and it matters if you are ever chasing something similar:
    /// CRYSTALS WERE NEVER AFFECTED. CrystalSpawner had already hit this and
    /// fixed it locally, with a public maxFaceUVPerCell (0.3 in the scene)
    /// applied in its own FaceUVPerCell helper, and its doc comment describes
    /// the same "barren small planet" symptom. So crystals were already getting
    /// 6x6 cells a face while trees, mushrooms and alien NPCs were on 2x2. I
    /// claimed all three were starved when I first wrote this; only the other
    /// three were. The clamp here is a no-op for crystals (0.3 is tighter than
    /// 1/3, so theirs still wins) - what it really does is give the other three
    /// spawners the fix crystals already had, in one shared place.
    ///
    /// Clamping the UV width fixes the small bodies and leaves the large ones
    /// untouched: it only binds below a radius of about three times the cell
    /// size, so Humble Abode, the twins and Cyclops keep exactly the grid they
    /// have today. On a small body the cells simply become smaller in metres,
    /// which is what they should have been — a 45 m world does not want 40 m
    /// cells. Total counts stay governed by each spawner's own cap.
    /// </summary>
    public static float FaceUVPerCell(float cellSize, float bodyRadius)
    {
        float uv = cellSize / Mathf.Max(0.001f, bodyRadius);
        // Small bodies get a FINER cap than large ones. Six cells a face rescued
        // the dwarfs from 24 spots to 216, and on a mostly-ocean world like
        // Hearth that is still not many once the waterline takes its share --
        // only the cells that happen to land on the islands can hold anything.
        // Twelve a face gives 864, four times the chances, and costs nothing
        // that matters: the scan is a couple of thousand cheap iterations and
        // the totals are still governed by each spawner's own cap.
        //
        // The threshold is the same 150 m the generator uses to decide a body is
        // "large", so every main planet keeps exactly the grid it has today.
        float cap = bodyRadius < SmallBodyRadius ? MaxFaceUVPerCellSmall : MaxFaceUVPerCell;
        return Mathf.Min(uv, cap);
    }

    // ── Surface raycast ───────────────────────────────────────────────────

    static readonly System.Collections.Generic.Dictionary<int, Collider> _terrain
        = new System.Collections.Generic.Dictionary<int, Collider>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTerrainCache() { _terrain.Clear(); }

    /// <summary>The planet's own terrain collider, cached. Never called per
    /// spawn attempt without the cache — GetComponentInChildren on a planet is
    /// not something to do in a streaming loop.</summary>
    static Collider TerrainColliderOf(CelestialBodyGenerator gen)
    {
        if (gen == null) return null;
        int id = gen.GetInstanceID();
        if (_terrain.TryGetValue(id, out var c) && c != null) return c;
        c = gen.GetComponentInChildren<MeshCollider>();
        if (c != null) _terrain[id] = c;
        return c;
    }

    /// <summary>
    /// A surface raycast that can only ever land on the PLANET'S OWN TERRAIN.
    ///
    /// Sam, 2026-09-09: "trees spawning in the air very far off of the dwarf
    /// planets ... i think this happens when my character or shuttle or other
    /// items interfere with that raycast". Exactly right. The spawners cast
    /// down from <c>surfaceRayHeight</c> (100 m) above the surface, so anything
    /// standing in that column is hit first and the prop is seated on top of
    /// it. Then the blocker walks away and the tree is left hanging.
    ///
    /// <see cref="WorldSpawnExcludeMask"/> already blacklists props, ships,
    /// water and the sun — but it deliberately CANNOT exclude Default or Body,
    /// which is where the player, enemies, dropped items and everything
    /// walkable live. A blacklist was always going to leak: every new kind of
    /// object is a new way to break it.
    ///
    /// So this whitelists instead, down to a single collider. Anything that is
    /// not this planet's terrain means "no ground here" and the spawn is
    /// refused — which is also the behaviour you want anyway: no tree grows
    /// where you are standing, and none inside the parked shuttle. A refused
    /// cell is simply retried the next time it streams in.
    ///
    /// Falls back to accepting the raw hit when the terrain collider cannot be
    /// resolved, so a planet built some other way keeps its old behaviour
    /// rather than losing all its props.
    /// </summary>
    public static bool RaycastPlanetSurface(CelestialBodyGenerator gen, Vector3 origin,
                                            Vector3 dir, float maxDistance, int mask,
                                            out RaycastHit hit)
    {
        if (!Physics.Raycast(origin, dir, out hit, maxDistance, mask,
                             QueryTriggerInteraction.Ignore))
            return false;
        var terrain = TerrainColliderOf(gen);
        if (terrain == null || hit.collider == null) return true;
        if (hit.collider == terrain) return true;
        // Not the exact collider we cached -- but a planet is allowed more than
        // one, and demanding reference equality would silently strip EVERY prop
        // from a planet whose generator happens to hold another collider first.
        // Anything parented under the GENERATOR is still that planet's own
        // surface (props are parented to the CelestialBody, one level up, so
        // they cannot sneak in this way).
        return hit.collider.transform.IsChildOf(gen.transform);
    }

    // ── Physics-frame parenting ───────────────────────────────────────────

    /// Parent a freshly placed prop to its planet using the planet's PHYSICS
    /// pose, not the interpolated render transform.
    ///
    /// Every CelestialBody rigidbody is kinematic + Interpolate, so during
    /// Update() `body.transform` lags `rb.position` by up to one fixed step —
    /// ~2.0 m on Humble Abode at 99 m/s orbital speed. The spawners' surface
    /// raycasts resolve against the COLLIDER, which sits at the physics pose,
    /// so the prop's world position is physics-frame too. A plain
    /// SetParent(body.transform, worldPositionStays: true) would convert that
    /// position through the lagging render pose and freeze a per-spawn-random
    /// 0..2 m world-space offset into the prop's local position — buried where
    /// the terrain rises toward the planet's velocity vector, floating where
    /// it falls away. Converting through rb.position/rb.rotation instead puts
    /// the prop exactly on the rendered terrain, permanently.
    ///
    /// Assumes the planet transform has unit scale (all celestial bodies do).
    public static void ParentToBodyPhysicsFrame(Transform t, CelestialBody body)
    {
        if (t == null || body == null) return;
        var rb = body.Rigidbody;
        if (rb == null) { t.SetParent(body.transform, true); return; }

        Vector3 worldPos = t.position;
        Quaternion worldRot = t.rotation;
        Quaternion inv = Quaternion.Inverse(rb.rotation);
        t.SetParent(body.transform, false);
        t.localPosition = inv * (worldPos - rb.position);
        t.localRotation = inv * worldRot;
    }

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

    /// Inverse of EncodeCell. Needed by the Messages system to point at a
    /// buyer's home cell while they're unstreamed.
    public static void DecodeCell(long id, out int face, out int cellU, out int cellV)
    {
        const long OFFSET = 1L << 19;
        face  = (int)((id >> 40) & 0x7);
        cellU = (int)(((id >> 20) & 0xFFFFFL) - OFFSET);
        cellV = (int)((id & 0xFFFFFL) - OFFSET);
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
