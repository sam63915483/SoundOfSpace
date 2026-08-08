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
    Vector3 fullScale;     // the prefab's authored scale (== a 1× wild mushroom)
    float sampleTimer;

    // How big this one grows UP TO, as a multiple of the prefab scale. Rolled
    // from the same 1–5× band MushroomSpawner uses for wild mushrooms, so a
    // cultivated patch has the same size variety as one you find in the world —
    // and a lucky 5× planting is worth as much as the best wild cap.
    // Every seedling still STARTS at the same small size; the multiplier is the
    // finish line, not the starting line, so you don't know what you've got
    // until it fills out.
    float sizeMultiplier = 1f;
    public float SizeMultiplier => sizeMultiplier;

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
    /// Rolls this planting's mature size from the wild 1–5× band.
    public void Init(CelestialBody plantedBody, string species)
    {
        body = plantedBody;
        speciesKey = species;
        // Keep the wild 1-5x roll's DISTRIBUTION, then clamp it by an O2-derived
        // ceiling: barren air can only ever produce runts, rich air can produce
        // monsters. Rolled once, at plant time, because that is when the existing
        // roll happens and the finish line has to be fixed before the seedling
        // starts growing toward it.
        //
        // Cultivated only. Wild mushroom size is a pure hash of the cell
        // (MushroomSpawner), which is exactly what makes wild respawn free and
        // species-true — clamping THAT by live O2 would make a wild cap change
        // size as trees grow nearby or as it streams out and back in. Terraforming
        // pays off in the crop you plant, not in the props popping around you.
        sizeMultiplier = Mathf.Min(MushroomSpawner.RollWildScale(), SizeCeilingAt(transform.position));
        ApplyScale();
    }

    /// The biggest a mushroom planted in this air can ever grow, lerped between
    /// the barren and rich ceilings by ambient O2. Floor is always 1x.
    float SizeCeilingAt(Vector3 pos)
    {
        float o2 = PlanetOxygen.Instance != null
            ? PlanetOxygen.Instance.AmbientO2At(pos)
            : 100f;
        // AmbientO2At is 0-100, NOT normalized — normalize before lerping or the
        // t value saturates instantly and every planting caps out at the max.
        return Mathf.Lerp(sizeCeilingLowO2, sizeCeilingHighO2, Mathf.Clamp01(o2 / 100f));
    }

    /// Growth-rate multiplier from any structure the mushroom is sitting in.
    /// Pot and dome stack multiplicatively (1.5 x 2.0 = 3x in a potted dome) —
    /// that's the intended reward for investing in both halves of the Industry
    /// path, and it's the knob to revisit first if pots+domes outrun everything.
    float StructureGrowthMultiplier(Vector3 pos)
    {
        float mult = 1f;
        if (GrowPot.PotContaining(pos) != null) mult *= potGrowthMultiplier;
        if (BubbleDome.DomeContaining(pos) != null) mult *= domeGrowthMultiplier;
        return mult;
    }

    /// Restore a saved planted mushroom's progress. growth >= 1 matures instantly.
    /// A savedSize of 0 is a pre-feature save — those planted at 1× flat.
    public void RestoreGrowth(CelestialBody plantedBody, string species, float savedGrowth, float savedSize)
    {
        body = plantedBody;
        speciesKey = species;
        sizeMultiplier = savedSize > 0.01f ? savedSize : 1f;
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
        // A pot or a dome speeds the crop up. Applied to the RATE so a stalled
        // mushroom stays stalled — a pot on barren rock is still just a pot;
        // it's the dome's own interior O2 (which AmbientO2At already returns)
        // that makes dead worlds farmable, not this multiplier.
        rate *= StructureGrowthMultiplier(transform.position);
        if (rate > 0f)
        {
            growth += (rate / Mathf.Max(1f, baseGrowthDuration)) * elapsed;
            if (growth >= 1f) { growth = 1f; Mature(); return; }
            ApplyScale();
        }
    }

    // Every seedling starts at the same minScaleFraction and grows toward its own
    // rolled sizeMultiplier — so the size a mushroom is GOING to be only reveals
    // itself as it fills out.
    void ApplyScale()
    {
        if (fullScale == Vector3.zero) fullScale = Vector3.one;
        transform.localScale = fullScale * Mathf.Lerp(minScaleFraction, sizeMultiplier, growth);
    }

    void Mature()
    {
        if (mature) return;
        mature = true;
        growth = 1f;
        transform.localScale = fullScale * sizeMultiplier;

        // Hand the size across: SpawnedMushroom pays out by scale, so a 5×
        // cultivated cap is worth the same as a 5× wild one.
        var node = GetComponent<SpawnedMushroom>();
        if (node == null) node = gameObject.AddComponent<SpawnedMushroom>();
        node.InitPlanted(speciesKey, sizeMultiplier);
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

    // -- appended; keep new fields at the END (serialization) --

    [Header("Oxygen scaling (cultivated only)")]
    [Tooltip("Biggest size multiplier a mushroom planted in DEAD air (0% O2) can roll. The 1-5x roll is clamped to this, so barren ground only ever grows runts.")]
    [SerializeField] float sizeCeilingLowO2 = 2f;
    [Tooltip("Biggest size multiplier a mushroom planted in FULL air (100% O2) can roll. 5 = the same ceiling wild mushrooms use, so a fully terraformed world grows monsters.")]
    [SerializeField] float sizeCeilingHighO2 = 5f;

    [Header("Structure multipliers")]
    [Tooltip("Growth-speed multiplier while inside a Grow Pot's radius.")]
    [SerializeField] float potGrowthMultiplier = 1.5f;
    [Tooltip("Growth-speed multiplier while inside a Bubble Dome. Stacks with the pot multiplier (1.5 x 2 = 3x).")]
    [SerializeField] float domeGrowthMultiplier = 2f;
}
