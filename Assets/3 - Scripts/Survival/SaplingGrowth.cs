using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A planted sapling that grows over time into a full tree, at a speed driven
/// by the ambient oxygen at its own position. Below a hard O2 floor it doesn't
/// grow at all — which is why a dead 0% planet needs a Bubble Dome (phase c) to
/// get the first trees going.
///
/// Growth (spec §3.4):
///   growthRate = ambientO2 >= minO2ToGrow ? ambientO2/100 : 0   // 0 = stalled
///   growth    += (growthRate / baseGrowthDuration) * dt         // 0..1
/// At 100% O2 a sapling matures in baseGrowthDuration; at 50% it takes twice as
/// long; below the floor it pauses (keeps its progress, never dies in phase 1).
///
/// While growing it's a small, inert prop (scaled by growth). On maturing it
/// scales to full and gains a SpawnedTree in "planted" mode, so it counts toward
/// local + planet O2 and can be chopped for wood + more saplings — closing the
/// loop. Placed instances are parented to a CelestialBody by the placement flow,
/// so they ride floating-origin shifts for free (no RegisterPhysicsObject).
///
/// NOTE (phase b): planted saplings are NOT streamed like seed trees — each is a
/// persistent GameObject. Fine for the handful a player plants; if that grows
/// large, add culling later. Persistence (growth progress across save/load) is
/// the next sub-step — not wired yet.
/// </summary>
public class SaplingGrowth : MonoBehaviour
{
    static readonly List<SaplingGrowth> s_all = new List<SaplingGrowth>();
    public static IReadOnlyList<SaplingGrowth> AllSaplings => s_all;

    // Freshly-planted scale as a fraction of the mature tree. Shared with
    // GhostPlacement so the placement ghost previews the real planted size.
    public const float DefaultPlantedScale = 0.12f;

    CelestialBody body;
    float growth;          // 0..1
    bool mature;
    Vector3 fullScale;     // the prefab's authored (mature) scale
    float sampleTimer;
    int prefabIndex;

    public bool IsMature => mature;
    public float Growth => growth;
    public CelestialBody Body => body;
    public int PrefabIndex => prefabIndex;   // save capture round-trips this

    void Awake()
    {
        fullScale = transform.localScale;

        // If the planted prefab already carries a SpawnedTree (e.g. a reused
        // tree prefab), strip it — a sapling must NOT count as a mature tree
        // until it grows up. We add a fresh one back on maturing.
        // DestroyImmediate, NOT Destroy: the save-restore path calls
        // RestoreGrowth(growth=1) in the same frame as AddComponent, and its
        // Mature() → GetComponent<SpawnedTree>() would find a deferred-destroy
        // zombie and wire the planted-tree state onto a component that dies at
        // end of frame (tree never counts toward O2 after a load).
        var existing = GetComponent<SpawnedTree>();
        if (existing != null) DestroyImmediate(existing);

        ApplyScale();
    }

    void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    /// Called by the placement flow right after the sapling is placed + parented.
    public void Init(CelestialBody plantedBody, int prefabIdx)
    {
        body = plantedBody;
        prefabIndex = prefabIdx;
        ApplyScale();
    }

    /// Restore a saved sapling's progress (used by the future save hook).
    public void RestoreGrowth(CelestialBody plantedBody, int prefabIdx, float savedGrowth)
    {
        body = plantedBody;
        prefabIndex = prefabIdx;
        growth = Mathf.Clamp01(savedGrowth);
        if (growth >= 1f) Mature();
        else ApplyScale();
    }

    void Update()
    {
        if (mature) return;

        sampleTimer -= Time.deltaTime;
        if (sampleTimer > 0f) return;
        float elapsed = sampleInterval - sampleTimer;   // real time since last sample
        sampleTimer = sampleInterval;

        Vector3 pos = transform.position;
        float o2 = PlanetOxygen.Instance != null ? PlanetOxygen.Instance.AmbientO2At(pos) : 100f;

        float rate = o2 >= minO2ToGrow ? o2 / 100f : 0f;   // stalled below the floor
        if (rate > 0f)
        {
            growth += (rate / Mathf.Max(1f, baseGrowthDuration)) * elapsed;
            if (growth >= 1f) { growth = 1f; Mature(); return; }
            ApplyScale();
        }
    }

    void ApplyScale()
    {
        float f = Mathf.Lerp(minScaleFraction, 1f, growth);
        transform.localScale = fullScale * f;
    }

    void Mature()
    {
        if (mature) return;
        mature = true;
        growth = 1f;
        transform.localScale = fullScale;

        // Become a real, choppable tree that counts toward O2. Planted mode so
        // harvesting removes the instance instead of marking a seed cell.
        var st = GetComponent<SpawnedTree>();
        if (st == null) st = gameObject.AddComponent<SpawnedTree>();
        st.InitPlanted(TreeSpawner.Instance, body, prefabIndex);
    }

    /// How many MATURE planted trees stand on a body — added to the planet's
    /// living-tree count by PlanetOxygen so cultivated forests raise its baseline.
    public static int MatureCountOnBody(CelestialBody b)
    {
        if (b == null) return 0;
        int n = 0;
        for (int i = 0; i < s_all.Count; i++)
        {
            var s = s_all[i];
            if (s != null && s.mature && s.body == b) n++;
        }
        return n;
    }

    /// Tree-equivalent O2 the body's still-growing saplings contribute. Once a
    /// sapling reaches HalfGrownThreshold (50%) it produces HalfGrownFraction
    /// (50%) of a full tree's oxygen, right up until it matures — at which point
    /// it becomes a real tree and is counted by MatureCountOnBody instead. Below
    /// the threshold a sapling produces nothing.
    public static float GrowingO2EquivalentOnBody(CelestialBody b)
    {
        if (b == null) return 0f;
        float sum = 0f;
        for (int i = 0; i < s_all.Count; i++)
        {
            var s = s_all[i];
            if (s != null && !s.mature && s.body == b && s.growth >= HalfGrownThreshold)
                sum += HalfGrownFraction;
        }
        return sum;
    }

    // Public: BubbleDome applies the same half-grown rule to its interior count.
    public const float HalfGrownThreshold = 0.5f;  // growth fraction at which O2 output switches on
    public const float HalfGrownFraction  = 0.5f;  // O2 (fraction of a full tree) a half-grown sapling makes

    // ── Tunables (spec defaults; tune in play then bake into code) ──────────
    [Header("Growth pacing")]
    [Tooltip("TUNE ME: seconds to fully mature at 100% ambient O2 (doubles at 50%, stalls below the floor). Spec target is ~600 (10 min); default kept short here so growth is watchable while iterating.")]
    [SerializeField] float baseGrowthDuration = 90f;
    [Tooltip("Ambient O2 %% below which a sapling doesn't grow at all (keeps its progress). This is what forces a Bubble Dome on a dead planet.")]
    [SerializeField] float minO2ToGrow = 10f;
    [Tooltip("Seconds between growth/O2 samples. Cheap; growth doesn't need per-frame precision.")]
    [SerializeField] float sampleInterval = 0.5f;

    [Header("Appearance")]
    [Tooltip("Scale of a freshly-planted sapling as a fraction of the full mature tree. Grows linearly to 1 as it matures.")]
    [SerializeField] float minScaleFraction = DefaultPlantedScale;
}
