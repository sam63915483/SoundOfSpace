using UnityEngine;

/// <summary>
/// Tutorial box: trees, crystals and mushrooms on the slab, placed ONCE at
/// load instead of streamed (docs/superpowers/specs/2026-09-14-tutorial-box-design.md).
///
/// Why not the real spawners: TreeSpawner / CrystalSpawner / MushroomSpawner
/// scan the WHOLE sphere every tick (cells = (radius / cellSize)² × 6). On the
/// tutorial's 10 km fake planet that is ~2 million cells four times a second —
/// a permanent stutter. (Grass streams around the player and cats stop scanning
/// at their cap, so those two run live.) The 10 km radius itself is
/// load-bearing: it is what keeps "radial up" within 0.6° of straight up across
/// the box for the shuttle autopilot and every prop.
///
/// Each prop gets exactly the post-setup the matching spawner gives it —
/// physics-frame parent under the body, WorldProp layer, NPCSeating.Reseat for
/// exact feet, the Spawned* component with a null owner (chopping, mining and
/// harvesting all tolerate that), SpawnFade — so it behaves like the planet's.
/// Layout is a seeded jittered grid, kept clear of the shuttle's landing spot
/// at the centre and of the walls.
/// </summary>
public class TutorialPropField : MonoBehaviour
{
    [Header("Trees (HA variants, rank-weighted like the gameplay TreeSpawner)")]
    public GameObject[] treePrefabs;
    public float[] treeWeights;
    public float treeCellSize = 34f;        // TreeSpawner.cellSize (Sam: 30/80% → 34/75%)
    [Range(0f, 1f)] public float treeChance = 0.75f;
    public Vector2 treeSizeRange = new Vector2(0.85f, 1.25f);
    public Vector2 treeStretchRange = new Vector2(0.9f, 1.15f);

    [Header("Crystals")]
    public GameObject crystalPrefab;
    public int crystalCount = 6;
    public float crystalMinScale = 1f, crystalMaxScale = 3f;

    [Header("Mushrooms")]
    public GameObject[] mushroomPrefabs;
    public int mushroomCount = 10;
    public float mushroomMinScale = 1f, mushroomMaxScale = 5f;

    [Header("Layout")]
    public float halfExtent = 100f;         // box half-size
    public float wallMargin = 8f;           // keep props off the digit walls
    public float centreClear = 22f;         // the shuttle's default landing spot stays clear
    public int seed = 12345;
    public LayerMask groundMask = 1 << 10;  // Body — the floor

    CelestialBody _body;

    void Start()
    {
        _body = GetComponentInParent<CelestialBody>();
        if (_body == null) { Debug.LogError("[TutorialPropField] Must sit under the tutorial's CelestialBody."); return; }
        var rng = new System.Random(seed);

        // Trees: jittered grid.
        if (treePrefabs != null && treePrefabs.Length > 0)
        {
            float usable = halfExtent - wallMargin;
            int n = Mathf.FloorToInt(2f * usable / treeCellSize);
            float start = -usable + (2f * usable - n * treeCellSize) * 0.5f + treeCellSize * 0.5f;
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (rng.NextDouble() > treeChance) continue;
                float x = start + i * treeCellSize + (float)(rng.NextDouble() - 0.5) * treeCellSize * 0.6f;
                float z = start + j * treeCellSize + (float)(rng.NextDouble() - 0.5) * treeCellSize * 0.6f;
                if (x * x + z * z < centreClear * centreClear) continue;
                int idx = PickWeighted(rng);
                float s = Lerp(treeSizeRange, rng), t = Lerp(treeStretchRange, rng);
                SpawnTree(idx, x, z, (float)rng.NextDouble() * 360f, new Vector3(s, s * t, s));
            }
        }

        if (crystalPrefab != null)
            for (int i = 0; i < crystalCount; i++)
            {
                if (!Scatter(rng, 14f, out float x, out float z)) break;
                float u = (float)rng.NextDouble();
                SpawnCrystal(x, z, (float)rng.NextDouble() * 360f, Mathf.Lerp(crystalMinScale, crystalMaxScale, u * u));
            }

        if (mushroomPrefabs != null && mushroomPrefabs.Length > 0)
            for (int i = 0; i < mushroomCount; i++)
            {
                if (!Scatter(rng, 6f, out float x, out float z)) break;
                SpawnMushroom(mushroomPrefabs[rng.Next(mushroomPrefabs.Length)], x, z,
                              (float)rng.NextDouble() * 360f, Lerp(new Vector2(mushroomMinScale, mushroomMaxScale), rng));
            }
    }

    // ── placement helpers ────────────────────────────────────────────────────

    static float Lerp(Vector2 range, System.Random rng) => Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());

    int PickWeighted(System.Random rng)
    {
        float total = 0f;
        for (int i = 0; i < treePrefabs.Length; i++) total += Weight(i);
        float u = (float)rng.NextDouble() * total;
        for (int i = 0; i < treePrefabs.Length; i++) { u -= Weight(i); if (u < 0f) return i; }
        return treePrefabs.Length - 1;
    }
    float Weight(int i) => treeWeights != null && i < treeWeights.Length && treeWeights[i] > 0f ? treeWeights[i] : 1f;

    readonly System.Collections.Generic.List<Vector2> _placed = new System.Collections.Generic.List<Vector2>();

    /// A random floor spot at least `minGap` from every earlier scatter pick,
    /// off the walls and out of the landing clearing.
    bool Scatter(System.Random rng, float minGap, out float x, out float z)
    {
        float usable = halfExtent - wallMargin;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            x = (float)(rng.NextDouble() * 2.0 - 1.0) * usable;
            z = (float)(rng.NextDouble() * 2.0 - 1.0) * usable;
            if (x * x + z * z < centreClear * centreClear) continue;
            bool ok = true;
            for (int i = 0; i < _placed.Count && ok; i++)
                if ((_placed[i] - new Vector2(x, z)).sqrMagnitude < minGap * minGap) ok = false;
            if (!ok) continue;
            _placed.Add(new Vector2(x, z));
            return true;
        }
        x = z = 0f;
        return false;
    }

    /// World pose on the floor at (x, z): up = radial from the fake planet's core
    /// (what every spawner does — within 0.6° of world up here).
    void FloorPose(float x, float z, float yawDeg, out Vector3 pos, out Quaternion rot)
    {
        pos = new Vector3(x, 0f, z);
        Vector3 up = (pos - _body.Position).normalized;
        rot = Quaternion.AngleAxis(yawDeg, up) * Quaternion.FromToRotation(Vector3.up, up);
    }

    // ── one of each, mirroring the spawners' SpawnX ──────────────────────────

    void SpawnTree(int idx, float x, float z, float yaw, Vector3 sizeMul)
    {
        var prefab = treePrefabs[idx];
        if (prefab == null) return;
        FloorPose(x, z, yaw, out var pos, out var rot);
        var tree = Instantiate(prefab, pos, rot);
        SpawnerCubeface.ParentToBodyPhysicsFrame(tree.transform, _body);
        SpawnerCubeface.SetLayerRecursively(tree, SpawnerCubeface.WorldPropLayer);
        var st = tree.GetComponent<SpawnedTree>();
        if (st == null) st = tree.AddComponent<SpawnedTree>();
        st.Init(null, -1, 0, idx);                       // no owner: choppable, no cell bookkeeping
        tree.transform.localScale = Vector3.Scale(prefab.transform.localScale, sizeMul);
        NPCSeating.Reseat(tree.transform, _body, groundMask, tree.transform.localScale.y, 0.005f, out _);
        var fade = tree.GetComponent<SpawnFade>();
        if (fade == null) fade = tree.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }

    void SpawnCrystal(float x, float z, float yaw, float scale)
    {
        FloorPose(x, z, yaw, out var pos, out var rot);
        var crystal = Instantiate(crystalPrefab, pos, rot);
        Vector3 baseScale = crystalPrefab.transform.localScale == Vector3.zero ? Vector3.one : crystalPrefab.transform.localScale;
        crystal.transform.localScale = baseScale * scale;
        // The crystal_17_2 prefab ships without a collider; a unit SphereCollider
        // at its 17× authored scale is ~17 m wide, so hug the mesh (CrystalSpawner).
        if (crystal.GetComponentsInChildren<Collider>(true).Length == 0)
        {
            var mf = crystal.GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null)
            {
                var mc = crystal.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true;
            }
        }
        if (crystal.GetComponent<SpawnedCrystal>() == null) crystal.AddComponent<SpawnedCrystal>();
        SpawnerCubeface.ParentToBodyPhysicsFrame(crystal.transform, _body);
        NPCSeating.Reseat(crystal.transform, _body, groundMask, crystal.transform.localScale.y, 0.005f, out _);
        SpawnerCubeface.SetLayerRecursively(crystal, SpawnerCubeface.WorldPropLayer);
        crystal.GetComponent<SpawnedCrystal>().Init(null, -1, 0, scale);   // after the reparent (rest pose)
        var fade = crystal.GetComponent<SpawnFade>();
        if (fade == null) fade = crystal.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }

    void SpawnMushroom(GameObject prefab, float x, float z, float yaw, float scale)
    {
        if (prefab == null) return;
        FloorPose(x, z, yaw, out var pos, out var rot);
        var mushroom = Instantiate(prefab, pos, rot);
        mushroom.transform.localScale = Vector3.one * scale;
        MushroomSpawner.EnsureSolidColliderOn(mushroom);    // harvest node: the axe sweep needs a solid
        var legacy = mushroom.GetComponent<MushroomInteraction>();
        if (legacy != null) Destroy(legacy);
        SpawnerCubeface.ParentToBodyPhysicsFrame(mushroom.transform, _body);
        NPCSeating.Reseat(mushroom.transform, _body, groundMask, mushroom.transform.localScale.y, 0.005f, out _);
        SpawnerCubeface.SetLayerRecursively(mushroom, SpawnerCubeface.WorldPropLayer);
        var node = mushroom.GetComponent<SpawnedMushroom>();
        if (node == null) node = mushroom.AddComponent<SpawnedMushroom>();
        node.Init(null, -1, 0, prefab.name, scale);
        var fade = mushroom.GetComponent<SpawnFade>();
        if (fade == null) fade = mushroom.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }
}
