using UnityEngine;

/// <summary>
/// Grows crystals on the walls, roof and floor of a cave — in its DEEPER half
/// only, seated on the rock the way the surface spawner seats them on the
/// ground, with a subtle pulsing blue glow.
///
/// HOW THEY'RE PLACED
/// Rays are fired outward from random points on the cave's own centre-lines
/// (the CaveVolume capsules) in random directions; wherever one hits the cave
/// shell a crystal is planted, its base ON the surface (the prefab's mesh
/// bottom is measured and lifted out, exactly as CrystalSpawner does), aligned
/// to the surface normal. Only capsules at least `deepFraction` of the way
/// along the cave (CaveVolume.capsuleDist, path metres from the mouth) are
/// used, so the entrance stays bare and the reward is for going deep.
///
/// Placement is DETERMINISTIC (seeded), so the same cave grows the same crystals.
///
/// They are ordinary SpawnedCrystals: the axe, the drops and the +N popup work
/// as on the surface. The crystal prefab is AUTHORED at scale ~17 and the
/// spawner multiplies by that; so does this (a plain scale of 1-2 made them a
/// few centimetres tall — 130 per cave that nobody could find).
///
/// KNOWN LIMIT: mined cave crystals come back when the scene reloads.
/// </summary>
[RequireComponent(typeof(CaveVolume))]
public class CaveCrystalSeeder : MonoBehaviour
{
    [Tooltip("How many crystals to try to plant.")]
    public int crystalCount = 45;

    [Tooltip("Crystal prefab. Left empty, the seeder borrows whatever the scene's CrystalSpawner uses, so cave crystals always match surface ones.")]
    public GameObject crystalPrefab;

    [Tooltip("Size range, as a multiplier on the prefab's authored scale — same convention as CrystalSpawner.")]
    public float minScale = 0.8f;
    public float maxScale = 2.0f;

    [Tooltip("Bias toward smaller crystals. Higher = more small ones, the odd big one.")]
    public float scaleBiasExponent = 2f;

    [Tooltip("Metres the base is pushed into the rock after seating, so it never floats on a faceted wall.")]
    public float embedDepth = 0.08f;

    [Tooltip("Keeps them apart so they don't grow into clumps.")]
    public float minSpacing = 3.5f;

    [Tooltip("Deterministic — the same cave always grows the same crystals.")]
    public int seed = 90210;

    [Tooltip("Only capsules at least this fraction of the way along the cave (by path distance from the mouth) get crystals.")]
    public float deepFraction = 0.5f;

    [Tooltip("Material with emission enabled (CaveCrystal_Glow.mat). Assigned to every cave crystal so the glow can pulse. Empty = no glow.")]
    public Material glowMaterial;

    [Tooltip("Every Nth crystal also gets a small blue point light. 0 = none.")]
    public int glowLightEvery = 5;

    [Tooltip("Plant only in caverns (the room capsules), never along tunnels.")]
    public bool cavernsOnly = true;

    bool _seeded;

    void Start() { Seed(); }

    void Seed()
    {
        if (_seeded) return;
        _seeded = true;

        var volume = GetComponent<CaveVolume>();
        if (volume == null || volume.capsuleA == null || volume.capsuleA.Length == 0) return;

        var prefab = crystalPrefab != null ? crystalPrefab : BorrowSurfacePrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[CaveCrystalSeeder] No crystal prefab — assign one, or make sure " +
                             "the scene's CrystalSpawner has one to borrow.", this);
            return;
        }

        // The cave's interior collider — NOT the mouth skin's (a sibling that
        // is moon surface and must stay bare).
        var rockT = transform.Find("Cave_Rock");
        var shell = rockT != null ? rockT.GetComponent<MeshCollider>() : null;
        if (shell == null) shell = GetComponentInChildren<MeshCollider>();
        if (shell == null)
        {
            Debug.LogWarning("[CaveCrystalSeeder] No cave collider to plant crystals on.", this);
            return;
        }

        // The deeper half: capsules by path distance from the mouth.
        int count = volume.capsuleA.Length;
        var deep = new System.Collections.Generic.List<int>(count);
        float maxDist = 0f;
        bool haveDist = volume.capsuleDist != null && volume.capsuleDist.Length == count;
        if (haveDist) for (int i = 0; i < count; i++) maxDist = Mathf.Max(maxDist, volume.capsuleDist[i]);
        for (int i = 0; i < count; i++)
        {
            bool isRoom = (volume.capsuleA[i] - volume.capsuleB[i]).sqrMagnitude < 1e-4f;
            if (cavernsOnly && !isRoom) continue;
            if (!haveDist || volume.capsuleDist[i] >= maxDist * deepFraction) deep.Add(i);
        }
        if (deep.Count == 0) for (int i = 0; i < count; i++) deep.Add(i);

        // Seating: the prefab's mesh bottom in prefab-local units (authored
        // scale stripped), converted to metres with the authored scale — the
        // same maths as CrystalSpawner.TryComputeCrystalPlacement.
        Vector3 baseScale = prefab.transform.localScale;
        float bottomY = SpawnerCubeface.ComputeLocalBottomY(prefab) * baseScale.y;

        var previousState = Random.state;
        Random.InitState(seed);

        var spawner = FindObjectOfType<CrystalSpawner>();
        var placed = new System.Collections.Generic.List<Vector3>(crystalCount);
        int n = Mathf.Max(0, crystalCount);
        int attempts = 0, made = 0, lights = 0;

        while (made < n && attempts < n * 14)
        {
            attempts++;

            int c = deep[Random.Range(0, deep.Count)];
            Vector3 from = transform.TransformPoint(
                Vector3.Lerp(volume.capsuleA[c], volume.capsuleB[c], Random.value));
            Vector3 dir = Random.onUnitSphere;

            float reach = volume.capsuleR[c] * 2.5f;
            if (!shell.Raycast(new Ray(from, dir), out RaycastHit hit, reach)) continue;

            bool tooClose = false;
            for (int i = 0; i < placed.Count; i++)
                if ((placed[i] - hit.point).sqrMagnitude < minSpacing * minSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            float t = Mathf.Pow(Random.value, scaleBiasExponent);
            float scale = Mathf.Lerp(minScale, maxScale, t);

            var go = Instantiate(prefab, hit.point, Quaternion.identity, transform);
            go.name = "CaveCrystal_" + made;
            // Spin about the crystal's OWN up, then point that up along the
            // wall normal — the same construction as CrystalSpawner. (The old
            // LookRotation*Euler(90,0,yaw) spun about the wrong axis, so a yaw
            // near 180° put the tip on the wall and the base in the air.)
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) *
                                    Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
            go.transform.localScale = baseScale * scale;
            // Seat it by MEASURING: the lowest mesh vertex along the surface
            // normal goes exactly onto the hit point (a whisker in). No pivot
            // guesswork — this is what put crystals 95% inside the wall.
            float lowest = float.MaxValue;
            foreach (var mfx in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mfx.sharedMesh == null) continue;
                var verts = mfx.sharedMesh.vertices;
                for (int k = 0; k < verts.Length; k++)
                    lowest = Mathf.Min(lowest, Vector3.Dot(mfx.transform.TransformPoint(verts[k]) - hit.point, hit.normal));
            }
            if (lowest != float.MaxValue) go.transform.position += hit.normal * (-lowest - embedDepth);

            // The prefab ships with no collider; the axe needs one to hit.
            if (go.GetComponentInChildren<Collider>(true) == null)
            {
                var mf = go.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                }
            }

            if (glowMaterial != null)
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = glowMaterial;
                var glow = go.AddComponent<CrystalGlow>();
                if (glowLightEvery > 0 && made % glowLightEvery == 0)
                {
                    var lgo = new GameObject("Glow");
                    lgo.transform.SetParent(go.transform, false);
                    lgo.transform.position = hit.point + hit.normal * 0.6f;
                    var l = lgo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = glow.glow;
                    l.range = 14f;
                    l.intensity = 1.4f;
                    l.shadows = LightShadows.None;
                    glow.pulseLight = l;
                    lights++;
                }
            }

            var crystal = go.GetComponent<SpawnedCrystal>();
            if (crystal == null) crystal = go.AddComponent<SpawnedCrystal>();
            crystal.Init(spawner, -1, made + 1L, scale);

            placed.Add(hit.point);
            made++;
        }

        Random.state = previousState;
        Debug.Log($"[CaveCrystalSeeder] Grew {made} crystals in '{name}' ({attempts} attempts, " +
                  $"{deep.Count}/{count} deep capsules, {lights} glow lights).");
    }

    static GameObject BorrowSurfacePrefab()
    {
        var spawner = FindObjectOfType<CrystalSpawner>();
        return spawner != null ? spawner.crystalPrefab : null;
    }
}
