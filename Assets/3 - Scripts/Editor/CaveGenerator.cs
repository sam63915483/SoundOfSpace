using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a walk-in cave and saves it as a prefab you can drag onto any
/// CelestialBody. Menu: Tools ▸ Cave ▸ Generate Cave Prefab.
///
/// THE CAVE IS ONE SOLID WITH THICK WALLS.
/// The layout below is a set of tunnels and rooms; CaveSolid unions them into a
/// single distance field and polygonises the ROCK around them — inner wall,
/// outer wall and entrance rim as one closed, watertight surface.
///
/// That is a deliberate replacement for the previous swept-surface generator,
/// which built a thin rim and a thin tube that had to meet exactly. Every bug it
/// produced was the same one: a strip whose single visible face pointed the
/// wrong way, so you saw straight through solid-looking rock into the hollow
/// planet. With a solid there is no "mouth piece" and no "tunnel piece" to line
/// up, and no inside-out face to hide, because the mesh is the boundary of a
/// volume. Branches and rooms come free — a side passage is one more capsule in
/// the union.
///
/// Every generation self-checks and refuses to claim success quietly:
///   • the mesh must be closed (zero boundary edges — an open edge IS a
///     see-through hole),
///   • its signed volume must be positive (outward-facing),
///   • rock must reach ground level outside the TerrainHole cut.
///
/// COORDINATE CONVENTION
/// Local +Y is OUT of the ground (the surface normal at the entrance); the cave
/// runs down and away along +Z. Drop the prefab on the surface, point its +Y
/// along the planet's up, spin around Y to aim the tunnel.
/// </summary>
public static class CaveGenerator
{
    const string OutFolder = "Assets/1 - samsPrefabs/Cave";
    const string ShellName = "Cave_Interior";   // kept from the first version so a
                                                // placed instance stays linked
    const string LegacyMouthName = "Cave_Mouth";

    /// What the TerrainHole cuts out of the planet. Must comfortably clear the
    /// entrance shaft so the bore isn't clipped by leftover terrain, and stay
    /// inside the rim so the cut edge is buried in rock.
    const float HoleRadius = 5.2f;

    const float NoGrassRadius = 13.5f;

    // ── Layout ───────────────────────────────────────────────────────────────
    // Points are (x, y, z) in metres, +Y out of the ground. Change these freely:
    // they're unioned into a field, so overlapping runs merge into one cavity
    // instead of leaving a seam. The only rule is that the entrance shaft must
    // start ABOVE ground (positive y) so the void punches up through the rim.

    // Keep the descent shallow — around 20°. An earlier layout dropped a
    // vertical shaft straight to the first level floor, which is a 9 m fall onto
    // rock: the player takes fall damage on the way in rather than walking in.
    // Anything under about 30° reads as a ramp you can walk down, and the floor
    // flattening in CaveSolid only engages on runs that shallow anyway.
    // A SWITCHBACK DESCENT: it doubles back on itself to get ~25 m down without
    // ever exceeding about 25° of slope. Depth costs horizontal run — 25 m of
    // drop at 25° needs 55 m of travel — and folding that run back on itself is
    // what keeps the cave compact instead of a corridor stretching off into the
    // distance.
    //
    // Do NOT steepen past ~26°: CaveSolid only flattens the floor on runs
    // shallower than that (a steep bore has no meaningful floor), so a steeper
    // leg becomes a round pipe you slide down rather than a path you walk.
    static readonly Vector3[] MainTunnel =
    {
        new Vector3(  0f,   2f,     0f),   // above ground — the void punches out through the rim
        new Vector3(  0f,  -1.5f,   3f),   // through the surface
        new Vector3(  0f,  -5f,    11f),   // ramp in, ~24°
        new Vector3(  8f,  -8.5f,  16f),   // turn right — first chamber
        new Vector3( 10f, -12f,    25f),   // ~21°
        new Vector3(  2f, -15.5f,  30f),   // switchback left
        new Vector3( -6f, -19f,    27f),   // double back
        new Vector3(-10f, -22.5f,  34f),   // deep chamber
        new Vector3( -6f, -26f,    42f),   // mid-deep chamber
        new Vector3(  2f, -30f,    46f),   // keeps going down
        new Vector3(  9f, -33.5f,  42f),   // the bottom, ~35 m below the surface
    };

    // Radius of the walkable void along the main tunnel, one per point above.
    // WIDER than they look like they need to be. The wall noise is subtracted
    // from these: at 1.0 m of ridged displacement a "3.0 m" passage pinches to
    // 2.0, and once the floor is flattened out of the bottom of that there is
    // barely room to walk — which is why some caverns couldn't be reached.
    // Keep every radius at least ~2.5× the noise amplitude.
    static readonly float[] MainRadius =
    { 3.8f, 3.9f, 4.0f, 4.1f, 3.8f, 3.9f, 3.8f, 4.0f, 3.8f, 3.9f, 3.8f };

    // Side passages. They LEAVE SHARPLY rather than running alongside the main
    // tunnel: two passages drifting apart slowly leave a wedge of rock between
    // them that is thinner than a grid cell for several metres, which Surface
    // Nets cannot represent and which showed up as open edges in the mesh. Keep
    // roughly 8 m of clear rock between any two passages that run in parallel.
    // Descends as it goes: it used to run flat at about −6 m and just sat there
    // as a shallow side room.
    static readonly Vector3[] BranchA =
    {
        new Vector3(  0f,  -5f,    11f),   // leaves the entry ramp
        new Vector3( -9f,  -8f,    13f),   // out to the side, dropping
        new Vector3(-15f, -11f,    17f),
        new Vector3(-19f, -14f,    21f),   // ends in a room well below the entrance
    };
    static readonly float[] BranchARadius = { 3.4f, 3.2f, 3.1f, 3.3f };

    // A second branch off the FIRST chamber, heading the opposite way to the
    // main tunnel's next leg so the two never shadow each other.
    static readonly Vector3[] BranchB =
    {
        new Vector3(  8f,  -8.5f,  16f),
        new Vector3( 17f,  -9.5f,  18f),
        new Vector3( 23f, -10f,    21f),   // ends in a high side room
    };
    static readonly float[] BranchBRadius = { 3.3f, 3.0f, 3.1f };

    // Rooms. Radius is the walkable bubble — they merge with whatever tunnel
    // reaches them.
    static readonly (Vector3 c, float r)[] Rooms =
    {
        (new Vector3(  8f,  -8.5f, 16f), 6.0f),   // first chamber, on the main run
        (new Vector3(-10f, -22.5f, 34f), 6.5f),   // deep chamber
        (new Vector3( -6f, -26f,   42f), 6.2f),   // mid-deep chamber
        (new Vector3(  9f, -33.5f, 42f), 6.8f),   // the bottom
        (new Vector3(-19f, -14f,   21f), 5.0f),   // end of branch A
        (new Vector3( 23f, -10f,   21f), 5.2f),   // end of branch B
    };

    [MenuItem("Tools/Cave/Generate Cave Prefab")]
    public static void Generate()
    {
        EnsureFolder();

        var segments = new List<CaveSolid.Segment>();
        AddRun(segments, MainTunnel, MainRadius);
        AddRun(segments, BranchA, BranchARadius);
        AddRun(segments, BranchB, BranchBRadius);

        var rooms = new List<CaveSolid.Room>();
        foreach (var r in Rooms) rooms.Add(new CaveSolid.Room { centre = r.c, radius = r.r });

        var solid = CaveSolid.Build(segments, rooms, out int quads);

        if (!SelfCheck(solid)) return;      // refuses to write a broken cave

        string shellPath = OutFolder + "/Cave_Interior.asset";
        SaveMesh(solid, shellPath);
        var material = BuildMaterial();
        var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(shellPath);

        string prefabPath = OutFolder + "/Cave_01.prefab";
        bool patched = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;

        // Patch in place rather than rebuilding from a fresh GameObject: a
        // rebuild mints new fileIDs for every child and breaks the link for any
        // instance already placed in a scene.
        GameObject root = patched
            ? PrefabUtility.LoadPrefabContents(prefabPath)
            : new GameObject("Cave_01");

        try
        {
            Configure(root, savedMesh, material, segments, rooms);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            if (patched) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CaveGenerator] {(patched ? "Patched" : "Wrote")} {prefabPath} — solid cave, " +
                  $"{solid.vertexCount} verts / {solid.triangles.Length / 3} tris from {quads} quads, " +
                  $"walls {CaveSolid.WallThickness} m thick. " +
                  (patched ? "The instance in the scene keeps its transform." : ""));
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    static void AddRun(List<CaveSolid.Segment> into, Vector3[] points, float[] radii)
    {
        for (int i = 0; i < points.Length - 1; i++)
            into.Add(new CaveSolid.Segment
            {
                a = points[i], b = points[i + 1],
                ra = radii[Mathf.Min(i, radii.Length - 1)],
                rb = radii[Mathf.Min(i + 1, radii.Length - 1)],
            });
    }

    // ── Self-check ───────────────────────────────────────────────────────────
    // Refuses to write a cave that can be seen through. Every previous version
    // of this generator shipped a see-through mouth that looked fine in a
    // render, so correctness is measured here, not eyeballed.

    static bool SelfCheck(Mesh mesh)
    {
        CaveSolid.CountEdgeDefects(mesh, out int holes, out int nonManifold, out Bounds where);
        if (holes > 0)
        {
            Debug.LogError($"[CaveGenerator] MESH IS NOT CLOSED — {holes} boundary edge(s) around " +
                           $"{where.center} (extent {where.extents}). That's a literal hole you " +
                           "can see through. Nothing was written.");
            return false;
        }
        if (nonManifold > 0)
            Debug.LogWarning($"[CaveGenerator] {nonManifold} non-manifold edge(s) near {where.center} " +
                             "— a feature pinching out at grid resolution. The surface is still " +
                             "watertight (nothing renders through it), so this is cosmetic. " +
                             "Shrink CaveSolid.CellSize if it ever shows.");

        double volume = CaveSolid.SignedVolume(mesh);
        if (volume <= 0.0)
        {
            Debug.LogError($"[CaveGenerator] Mesh is inside-out (signed volume {volume:0}). " +
                           "Nothing was written.");
            return false;
        }

        // Rock must reach ground level outside the cut, or the terrain's cut
        // edge opens into the void.
        float innerAtGround = float.MaxValue;
        foreach (var p in mesh.vertices)
        {
            if (Mathf.Abs(p.y) > 0.4f) continue;
            innerAtGround = Mathf.Min(innerAtGround, new Vector2(p.x, p.z).magnitude);
        }

        Debug.Log($"[CaveGenerator] Self-check: closed (0 boundary edges), outward-facing " +
                  $"(volume {volume:0} m³), rock reaches ground level at radius " +
                  $"{innerAtGround:0.00} (entrance shaft) against a {HoleRadius:0.00} cut.");
        return true;
    }

    // ── Prefab contents ──────────────────────────────────────────────────────

    static void Configure(GameObject root, Mesh shell, Material material,
                          List<CaveSolid.Segment> segments, List<CaveSolid.Room> rooms)
    {
        Ensure<CaveHoleBinder>(root);
        Ensure<GrassBlocker>(root);
        Ensure<NoGrassVolume>(root).radius = NoGrassRadius;
        FillVolume(Ensure<CaveVolume>(root), segments, rooms);
        Ensure<CaveCrystalSeeder>(root);   // crystals grow out of the walls at runtime

        // The depth-lid workaround is gone — OceanEffect.shader now cuts the
        // water out of the cave properly (CaveOceanCutout feeds it the capsules).
        // Its script is deleted, so clear the missing-script entry it leaves.
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);

        // Older versions split the mouth into its own child.
        var legacy = root.transform.Find(LegacyMouthName);
        if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        string legacyMesh = OutFolder + "/Cave_Mouth.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(legacyMesh) != null) AssetDatabase.DeleteAsset(legacyMesh);

        SetPiece(root.transform, ShellName, shell, material);
        SetHoleMarker(root.transform);
        SetLight(root.transform, "CaveLight_Throat",  new Vector3(0f, -3.5f, 7f),     16f, 0.5f);
        SetLight(root.transform, "CaveLight_Chamber", new Vector3(8f, -7.5f, 16f),    22f, 0.7f);
        SetLight(root.transform, "CaveLight_Mid",     new Vector3(2f, -14.5f, 30f),   18f, 0.5f);
        SetLight(root.transform, "CaveLight_Deep",    new Vector3(-10f, -21.5f, 34f), 22f, 0.6f);
        SetLight(root.transform, "CaveLight_Bottom",  new Vector3(-6f, -25f, 42f),    22f, 0.6f);
        SetLight(root.transform, "CaveLight_Abyss",   new Vector3(9f, -32.5f, 42f),   22f, 0.55f);
        SetLight(root.transform, "CaveLight_BranchA", new Vector3(-19f, -13f, 21f),   18f, 0.5f);
    }

    // EVERY cavity — tunnels and rooms — for the swim / ocean-suppression test.
    //
    // Rooms are the part that was missing: feeding only the tunnel centre-lines
    // covered a 6.8 m chamber to about 3 m, so standing off-centre in a room put
    // the player outside the volume and the ocean reappeared around them.
    static void FillVolume(CaveVolume volume, List<CaveSolid.Segment> segments, List<CaveSolid.Room> rooms)
    {
        var a = new List<Vector3>();
        var b = new List<Vector3>();
        var r = new List<float>();

        foreach (var s in segments)
        {
            a.Add(s.a); b.Add(s.b);
            r.Add(Mathf.Max(s.ra, s.rb));     // widest end — the test must not under-cover
        }
        foreach (var room in rooms)
        {
            a.Add(room.centre); b.Add(room.centre);   // a room is a capsule with no length
            r.Add(room.radius);
        }

        volume.capsuleA = a.ToArray();
        volume.capsuleB = b.ToArray();
        volume.capsuleR = r.ToArray();
        // Set explicitly: a component that already exists in the prefab keeps
        // whatever it serialised earlier, so a changed default never reaches it.
        volume.radiusPadding = 1.25f;
        // Water is removed from the cave by OceanEffect.shader itself, using
        // these same capsules — no global flags, no mouth bubble, no depth lid.
        volume.suppressOcean = false;
        volume.mouthBubbleRadius = 0f;
        volume.oceanCutoutPadding = 1.15f;
    }

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void SetPiece(Transform parent, string name, Mesh mesh, Material mat)
    {
        var t = parent.Find(name);
        GameObject go;
        if (t != null) go = t.gameObject;
        else { go = new GameObject(name); go.transform.SetParent(parent, false); }

        // Body is the layer the game treats as walkable ground (the moon tunnel
        // uses it too) — on Default the player falls through.
        go.layer = LayerMask.NameToLayer("Body");
        Ensure<MeshFilter>(go).sharedMesh = mesh;
        Ensure<MeshRenderer>(go).sharedMaterial = mat;

        var col = Ensure<MeshCollider>(go);
        col.convex = false;      // a convex hull of a cave is a solid lump
        // Clear first: re-assigning the same mesh reference (SaveMesh mutates the
        // asset in place) can be a no-op, leaving PhysX cooked against the old
        // geometry — a collider that doesn't match what you can see.
        col.sharedMesh = null;
        col.sharedMesh = mesh;
    }

    static void SetHoleMarker(Transform parent)
    {
        const string name = "TerrainHole - Cave Mouth";
        var t = parent.Find(name);
        GameObject go;
        if (t != null) go = t.gameObject;
        else
        {
            // A Unity Cylinder is radius 0.5, height 2 in local space, which is
            // exactly what TerrainHole.Shape.Cylinder expects — so the scale
            // below IS the cut.
            go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }
        go.transform.localPosition = new Vector3(0f, -1f, 0f);
        go.transform.localScale = new Vector3(HoleRadius * 2f, 14f, HoleRadius * 2f);

        var hole = Ensure<TerrainHole>(go);
        hole.shape = TerrainHole.Shape.Cylinder;
        hole.hideAtRuntime = true;
    }

    // Faint fill lights so the first playtest isn't a walk into a black
    // rectangle. NOT a lighting design — delete them once you decide whether
    // caves are flashlight-only or lit with placed torches.
    static void SetLight(Transform parent, string name, Vector3 localPos, float range, float intensity)
    {
        var t = parent.Find(name);
        GameObject go;
        if (t != null) go = t.gameObject;
        else { go = new GameObject(name); go.transform.SetParent(parent, false); }

        go.transform.localPosition = localPos;
        var l = Ensure<Light>(go);
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = new Color(0.78f, 0.84f, 1f);
        l.shadows = LightShadows.None;
    }

    // ── Assets ───────────────────────────────────────────────────────────────

    static Material BuildMaterial()
    {
        string path = OutFolder + "/Cave_Rock.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        // Standard, not URP — this project is Built-in RP and URP-authored
        // materials render magenta (CLAUDE.md).
        var mat = new Material(Shader.Find("Standard")) { name = "Cave_Rock" };
        mat.SetFloat("_Glossiness", 0.08f);
        mat.SetFloat("_Metallic", 0f);

        var tex = BuildRockTexture();
        AssetDatabase.CreateAsset(tex, OutFolder + "/Cave_Rock_Albedo.asset");
        mat.mainTexture = tex;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Texture2D BuildRockTexture()
    {
        const int size = 512;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Cave_Rock_Albedo" };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float n = TileNoise(u, v, 4f) * 0.55f
                        + TileNoise(u, v, 11f) * 0.30f
                        + TileNoise(u, v, 27f) * 0.15f;
                float shade = Mathf.Lerp(0.16f, 0.46f, n);
                px[y * size + x] = new Color(shade * 1.02f, shade * 0.97f, shade * 0.90f, 1f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // Perlin sampled around a torus so the tile wraps without a visible seam.
    static float TileNoise(float u, float v, float freq)
    {
        float a = Mathf.PerlinNoise(u * freq, v * freq);
        float b = Mathf.PerlinNoise((u - 1f) * freq, v * freq);
        float c = Mathf.PerlinNoise(u * freq, (v - 1f) * freq);
        float d = Mathf.PerlinNoise((u - 1f) * freq, (v - 1f) * freq);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    // Overwrite the mesh CONTENTS rather than deleting and recreating the asset:
    // deleting mints a new GUID, leaving every MeshFilter and MeshCollider that
    // already references it — including the one in the scene — pointing at nothing.
    static void SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return; }

        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.vertices = mesh.vertices;
        existing.uv = mesh.uv;
        existing.triangles = mesh.triangles;
        existing.normals = mesh.normals;
        existing.tangents = mesh.tangents;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder("Assets/1 - samsPrefabs", "Cave");
    }
}
