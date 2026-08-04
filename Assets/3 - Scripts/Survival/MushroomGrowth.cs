using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A planted mushroom sapling ("spores") growing back into a full mushroom —
/// the mushroom twin of <see cref="SaplingGrowth"/>, and the other half of the
/// loop Sam asked for: chop a mushroom, get 0–2 spores of that species, plant
/// them, and the SAME mushroom grows.
///
/// Deliberately NOT a subclass of / reuse of SaplingGrowth, because a mushroom
/// is not a tree in the one way that matters to the rest of the game: it makes
/// no oxygen. PlanetOxygen, BubbleDome and the Tree Daddy / Tree Killer
/// progression tracks all count SaplingGrowth instances, and quietly slipping
/// mushrooms into that list would inflate the planet's O2 off fungus.
///
/// It DOES share the growth model — speed scales with the ambient O2 at its own
/// position and stalls below a floor — because that's the link the handoff wants
/// later: more trees → richer air → faster mushrooms.
///
/// While growing it's a small inert prop scaled by progress. On maturing it
/// scales to full and gains a SpawnedMushroom in planted mode, so it chops like
/// any wild mushroom (and removes its own instance instead of marking a cell).
/// </summary>
public class MushroomGrowth : MonoBehaviour
{
    static readonly List<MushroomGrowth> s_all = new List<MushroomGrowth>();
    public static IReadOnlyList<MushroomGrowth> AllPlanted => s_all;

    /// Freshly-planted scale as a fraction of the mature mushroom. Shared with
    /// GhostPlacement so the placement ghost previews the real planted size.
    public const float DefaultPlantedScale = 0.15f;

    CelestialBody body;
    string speciesKey;
    float growth;          // 0..1
    bool mature;
    Vector3 fullScale;
    float sampleTimer;

    public bool IsMature => mature;
    public float Growth => growth;
    public CelestialBody Body => body;
    public string SpeciesKey => speciesKey;

    void Awake()
    {
        fullScale = transform.localScale;
        ApplyScale();
    }

    void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    /// Called by the placement flow right after the spores are placed + parented.
    public void Init(CelestialBody plantedBody, string species)
    {
        body = plantedBody;
        speciesKey = species;
        ApplyScale();
    }

    /// Restore a saved planted mushroom's progress. growth >= 1 matures instantly.
    public void RestoreGrowth(CelestialBody plantedBody, string species, float savedGrowth)
    {
        body = plantedBody;
        speciesKey = species;
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

        float o2 = PlanetOxygen.Instance != null
            ? PlanetOxygen.Instance.AmbientO2At(transform.position)
            : 100f;

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
        if (fullScale == Vector3.zero) fullScale = Vector3.one;
        transform.localScale = fullScale * Mathf.Lerp(minScaleFraction, 1f, growth);
    }

    void Mature()
    {
        if (mature) return;
        mature = true;
        growth = 1f;
        transform.localScale = fullScale;

        var node = GetComponent<SpawnedMushroom>();
        if (node == null) node = gameObject.AddComponent<SpawnedMushroom>();
        node.InitPlanted(speciesKey);
    }

    // ── Tunables ───────────────────────────────────────────────────────────
    [Header("Growth pacing")]
    [Tooltip("Seconds to fully mature at 100% ambient O2 (doubles at 50%, stalls below the floor). Kept EQUAL to SaplingGrowth.baseGrowthDuration (90) at Sam's request 2026-08-04 — mushrooms and trees grow at the same rate for now. If one of these is retuned, retune the other or the pair drifts apart silently.")]
    [SerializeField] float baseGrowthDuration = 90f;
    [Tooltip("Ambient O2 %% below which a planted mushroom doesn't grow at all (keeps its progress).")]
    [SerializeField] float minO2ToGrow = 10f;
    [Tooltip("Seconds between growth/O2 samples. Cheap; growth doesn't need per-frame precision.")]
    [SerializeField] float sampleInterval = 0.5f;

    [Header("Appearance")]
    [Tooltip("Scale of freshly-planted spores as a fraction of the full mushroom. Grows linearly to 1 as it matures.")]
    [SerializeField] float minScaleFraction = DefaultPlantedScale;
}
