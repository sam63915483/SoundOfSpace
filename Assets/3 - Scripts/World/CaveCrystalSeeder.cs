using UnityEngine;

/// <summary>
/// Grows crystals out of the cave's walls, roof and floor — a lot more of them
/// than the surface has, so the cave is where you go when you need crystals.
///
/// HOW THEY'RE PLACED
/// Rays are fired outward from random points on the cave's own centre-lines
/// (the CaveVolume capsules) in random directions, and a crystal is planted
/// wherever one hits the cave shell, aligned to the surface normal. That means
/// they land on whatever surface the ray happens to find — wall, ceiling or
/// floor — with no special cases, and they follow the cave automatically if its
/// layout is regenerated.
///
/// Placement is DETERMINISTIC: the RNG is seeded from a fixed number, so the
/// same cave always grows the same crystals rather than a different set every
/// time you load.
///
/// They are ordinary SpawnedCrystals, so the axe, the drops and the +N popup all
/// work exactly as they do on the surface.
///
/// KNOWN LIMIT: mined cave crystals come back when the scene reloads. The
/// surface spawner tracks consumed cells in the save; this doesn't yet. Living
/// with it for now — worth fixing if crystal farming ever matters.
/// </summary>
[RequireComponent(typeof(CaveVolume))]
public class CaveCrystalSeeder : MonoBehaviour
{
    [Tooltip("How many crystals to try to plant. The surface spawner caps out around 20 in a 300 m radius, so this is deliberately far denser.")]
    public int crystalCount = 70;

    [Tooltip("Crystal prefab. Left empty, the seeder borrows whatever the scene's CrystalSpawner uses, so cave crystals always match surface ones.")]
    public GameObject crystalPrefab;

    [Tooltip("Size range. Slightly smaller than surface crystals on average — they're growing out of a wall, not standing in a field.")]
    public float minScale = 0.8f;
    public float maxScale = 2.2f;

    [Tooltip("Bias toward smaller crystals. Higher = more small ones, the odd big one.")]
    public float scaleBiasExponent = 2f;

    [Tooltip("Push into the rock, so a crystal reads as growing OUT of the wall rather than balancing on it.")]
    public float embedDepth = 0.25f;

    [Tooltip("Keeps them apart so they don't grow into clumps.")]
    public float minSpacing = 1.6f;

    [Tooltip("Deterministic — the same cave always grows the same crystals.")]
    public int seed = 90210;

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

        var shell = GetComponentInChildren<MeshCollider>();
        if (shell == null)
        {
            Debug.LogWarning("[CaveCrystalSeeder] No cave collider to plant crystals on.", this);
            return;
        }

        // Own RNG state so seeding can't disturb anyone else's random sequence,
        // and restore it afterwards.
        var previousState = Random.state;
        Random.InitState(seed);

        var spawner = FindObjectOfType<CrystalSpawner>();
        var placed = new System.Collections.Generic.List<Vector3>(crystalCount);
        int n = Mathf.Max(0, crystalCount);
        int attempts = 0, made = 0;

        while (made < n && attempts < n * 12)
        {
            attempts++;

            // A random point on a random passage, then a random direction out
            // from it. Whatever the ray hits is a wall, roof or floor.
            int c = Random.Range(0, volume.capsuleA.Length);
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

            var go = Instantiate(prefab, hit.point - hit.normal * embedDepth * scale,
                                 Quaternion.identity, transform);
            go.name = "CaveCrystal_" + made;
            // Grow along the surface normal, with a random spin so they don't
            // all face the same way.
            go.transform.rotation = Quaternion.LookRotation(hit.normal) *
                                    Quaternion.Euler(90f, 0f, Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * scale;

            var crystal = go.GetComponent<SpawnedCrystal>();
            if (crystal == null) crystal = go.AddComponent<SpawnedCrystal>();
            // slot -1 / a unique id: these are not part of the surface spawner's
            // cell grid, and Mine() just destroys the instance.
            crystal.Init(spawner, -1, made + 1L, scale);

            placed.Add(hit.point);
            made++;
        }

        Random.state = previousState;
        Debug.Log($"[CaveCrystalSeeder] Grew {made} crystals in '{name}' " +
                  $"({attempts} attempts). Surface spawner caps around 20 across a 300 m radius.");
    }

    static GameObject BorrowSurfacePrefab()
    {
        var spawner = FindObjectOfType<CrystalSpawner>();
        return spawner != null ? spawner.crystalPrefab : null;
    }
}
