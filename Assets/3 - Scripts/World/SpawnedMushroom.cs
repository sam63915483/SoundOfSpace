using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A world mushroom as a HARVEST NODE — the tree of the mushroom economy.
/// Runtime-attached by MushroomSpawner to every streamed mushroom (and by
/// MushroomGrowth to a matured planted one), it mirrors SpawnedTree end to end:
///
///   • chopped with the axe, at HALF a tree's break threshold
///   • a squish per hit, plus a wobble that's deliberately looser and rubberier
///     than the tree's stiff shake — a cap should jiggle, not creak
///   • on break: topples like a felled tree, then shrinks away
///   • drops 3–9 species-matched mushrooms + 0–2 mushroom saplings that spin
///     and bob on the ground exactly like wood logs
///
/// Eating moved off the world node and onto the hotbar (hold fire on a selected
/// mushroom) — a mushroom in the ground is a crop now, not a snack.
///
/// Instances are iterated via the static AllMushrooms list (CLAUDE.md: never
/// FindObjectsOfType in Update).
/// </summary>
public class SpawnedMushroom : MonoBehaviour
{
    static readonly List<SpawnedMushroom> s_all = new List<SpawnedMushroom>();
    public static IReadOnlyList<SpawnedMushroom> AllMushrooms => s_all;

    MushroomSpawner spawner;
    int bodySlot;
    long cellId;
    bool dead;
    bool isPlanted;              // grown from a planted sapling — no seed cell to mark
    string speciesKey;
    int hp;
    int dropCount;
    Vector3 _baseScale;
    Quaternion _restRotation;
    Coroutine _wobbleRoutine;
    Coroutine _fallRoutine;

    // Kept for the world prop's own presentation only. The EATING effect no
    // longer rides on the instance: a harvested cap is an item, and MushroomEffect
    // derives its trip dials from the species key instead (see that file for why).
    float mushroomScale = 1f;

    public bool IsDead => dead;
    public float MushroomScale => mushroomScale;
    public string SpeciesKey => speciesKey;

    void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    /// A streamed, seed-grid mushroom. Harvesting marks its cell consumed so the
    /// streaming loop won't put it back.
    public void Init(MushroomSpawner s, int slot, long id, string species, float scale)
    {
        spawner = s;
        bodySlot = slot;
        cellId = id;
        isPlanted = false;
        speciesKey = species;
        mushroomScale = scale;
        RollHarvest();
        _baseScale = transform.localScale;
        _restRotation = transform.localRotation;
        dead = false;
        SetCollidersEnabled(true);
        StopRoutines();
    }

    /// A player-grown mushroom that has matured. Behaves identically when
    /// chopped, but removes its own instance instead of marking a seed cell.
    public void InitPlanted(string species, float scale)
    {
        spawner = null;
        bodySlot = -1;
        cellId = 0;
        isPlanted = true;
        speciesKey = species;
        mushroomScale = scale;
        RollHarvest();
        _baseScale = transform.localScale;
        _restRotation = transform.localRotation;
        dead = false;
        SetCollidersEnabled(true);
        StopRoutines();
    }

    void RollHarvest()
    {
        // HALF a tree's effort. SpawnedTree rolls hp 4–8; a mushroom rolls 2–4.
        // Deliberately NOT scaled by size: a big cap is a better prize for the
        // same work, which is what makes hunting the big ones worth doing.
        hp = Random.Range(2, 5);

        // Payout scales with SIZE. The handoff's 3–9 band is now the range across
        // the whole 1–5× spread rather than a flat roll: a runt gives 2–4, a
        // monster gives 7–12. Applies identically to wild and cultivated
        // mushrooms, so a 5× you grew yourself is worth a 5× you found.
        float t = Mathf.Clamp01((mushroomScale - 1f) / 4f);
        int lo = Mathf.RoundToInt(Mathf.Lerp(2f, 7f, t));
        int hi = Mathf.RoundToInt(Mathf.Lerp(4f, 12f, t));
        dropCount = Random.Range(lo, hi + 1);
    }

    void StopRoutines()
    {
        if (_wobbleRoutine != null) { StopCoroutine(_wobbleRoutine); _wobbleRoutine = null; }
        if (_fallRoutine != null) { StopCoroutine(_fallRoutine); _fallRoutine = null; }
    }

    void SetCollidersEnabled(bool on)
    {
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null && !cols[i].isTrigger) cols[i].enabled = on;
    }

    public void TakeDamage(int amount)
    {
        if (dead || amount <= 0) return;
        hp -= amount;
        PlayHitSquish();
        if (hp <= 0) Break();
        else PlayWobble();
    }

    // ── Feedback ───────────────────────────────────────────────────────────

    void PlayHitSquish()
    {
        var clip = spawner != null ? spawner.RandomHitSquish() : MushroomSpawner.AnyHitSquish();
        if (clip == null) return;
        float vol = spawner != null ? spawner.squishVolume : 0.85f;
        AudioSource.PlayClipAtPoint(clip, transform.position, vol);
    }

    void PlayBreakSquish()
    {
        var clip = spawner != null ? spawner.BreakSquish() : MushroomSpawner.AnyBreakSquish();
        if (clip == null) { PlayHitSquish(); return; }
        float vol = spawner != null ? spawner.squishVolume : 0.85f;
        AudioSource.PlayClipAtPoint(clip, transform.position, vol);
    }

    void PlayWobble()
    {
        if (_wobbleRoutine != null) StopCoroutine(_wobbleRoutine);
        _wobbleRoutine = StartCoroutine(WobbleRoutine());
    }

    /// Sam's brief: "more shaky and wobbly" than a tree. The tree does one stiff
    /// 0.18s / 3° sine on a single axis. This runs 3.3× longer, ~3× wider, on
    /// TWO tilt axes at slightly different frequencies (so the cap traces a
    /// wobbling ellipse rather than a flat rock), and adds a squash-and-stretch
    /// on the scale — the rubbery part that sells it as a mushroom and not wood.
    IEnumerator WobbleRoutine()
    {
        const float duration  = 0.6f;
        const float amplitude = 9f;     // degrees of tilt at the start
        const float freqA     = 17f;
        const float freqB     = 23f;    // deliberately not a multiple of freqA
        const float squash    = 0.14f;  // fraction of scale wobbled

        float t = 0f;
        while (t < duration)
        {
            // Ease the decay so it keeps jiggling well into the tail instead of
            // dying linearly like the tree's shake.
            float decay = 1f - (t / duration);
            decay *= decay;

            float a = Mathf.Sin(t * freqA) * amplitude * decay;
            float b = Mathf.Sin(t * freqB + 1.1f) * amplitude * 0.7f * decay;
            transform.localRotation = _restRotation * Quaternion.Euler(a, 0f, b);

            float s = Mathf.Sin(t * freqA * 0.5f) * squash * decay;
            transform.localScale = new Vector3(
                _baseScale.x * (1f + s * 0.5f),
                _baseScale.y * (1f - s),
                _baseScale.z * (1f + s * 0.5f));

            t += Time.deltaTime;
            yield return null;
        }
        transform.localRotation = _restRotation;
        transform.localScale = _baseScale;
        _wobbleRoutine = null;
    }

    // ── Break ──────────────────────────────────────────────────────────────

    void Break()
    {
        if (dead) return;
        dead = true;
        SetCollidersEnabled(false);

        Transform bodyParent = transform.parent;
        string species = !string.IsNullOrEmpty(speciesKey) ? speciesKey : MushroomRegistry.AnyKey();

        ResourceDrop.DropMushrooms(Hotbar.ItemId.Mushroom, species, dropCount,
                                   transform.position, bodyParent);

        // Sam's addition to the handoff: breaking a mushroom yields 0–2 mushroom
        // saplings of the SAME species, so the loop closes exactly like trees —
        // chop, replant, the same mushroom grows back. 0 is a real outcome: a
        // mushroom patch has to be worth walking to, not a guaranteed printer.
        // Spores lean on size too, but far more gently than the caps do — the
        // ceiling stays 2, so a big find speeds the loop up without ever making
        // one lucky mushroom self-sustaining.
        //
        // Richer air also throws more spores, and a GROW POT guarantees a floor
        // so a pot farm can never dead-end the way a picked-clean field does.
        // That floor is the whole reason to build one.
        float sizeT = Mathf.Clamp01((mushroomScale - 1f) / 4f);
        int saplings = RollSpores(sizeT);
        if (saplings > 0)
            ResourceDrop.DropMushrooms(Hotbar.ItemId.MushroomSapling, species, saplings,
                                       transform.position, bodyParent);

        PlayBreakSquish();
        StopRoutines();
        _fallRoutine = StartCoroutine(FallAndShrink());
    }

    // Same topple-then-shrink the felled tree uses, a touch faster — a mushroom
    // has no trunk to groan through.
    IEnumerator FallAndShrink()
    {
        Quaternion startRot = _restRotation;
        Quaternion endRot = startRot * Quaternion.AngleAxis(88f, Vector3.right);

        const float fallDuration = 0.5f;
        float t = 0f;
        while (t < fallDuration)
        {
            float u = t / fallDuration;
            transform.localRotation = Quaternion.Slerp(startRot, endRot, u * u);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localRotation = endRot;

        Vector3 startScale = transform.localScale;
        const float shrinkDuration = 0.35f;
        t = 0f;
        while (t < shrinkDuration)
        {
            transform.localScale = startScale * (1f - t / shrinkDuration);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = Vector3.zero;

        _fallRoutine = null;
        Harvest();
    }

    void Harvest()
    {
        // Planted mushrooms aren't cell-based: just remove the instance.
        // Streamed ones ALSO destroy — MarkCellConsumed only drops the cell from
        // the spawner's live/pool bookkeeping so it never streams back in.
        if (!isPlanted && spawner != null) spawner.MarkCellConsumed(bodySlot, cellId);
        Destroy(gameObject);
    }

    /// How many spores this mushroom drops.
    ///
    /// Structure: a guaranteed FLOOR (0 in open ground, 1-2 in a Grow Pot),
    /// then one Bernoulli trial per point of headroom up to the O2-scaled
    /// ceiling. The first extra is likely, the rest are long shots, which is
    /// what keeps a lucky monster from becoming self-sustaining.
    ///
    /// At 0% O2 in open ground this reduces to exactly the two-trial roll the
    /// game shipped with, so nothing about early foraging changes.
    int RollSpores(float sizeT)
    {
        float o2 = PlanetOxygen.Instance != null
            ? PlanetOxygen.Instance.AmbientO2At(transform.position)
            : 100f;
        // AmbientO2At is 0-100; normalize before lerping.
        float t = Mathf.Clamp01(o2 / 100f);

        bool inPot = GrowPot.PotContaining(transform.position) != null;

        int min = inPot ? Mathf.RoundToInt(Mathf.Lerp(potSporeMinLowO2, potSporeMinHighO2, t)) : 0;
        int max = inPot ? Mathf.RoundToInt(Mathf.Lerp(potSporeMaxLowO2, potSporeMaxHighO2, t))
                        : Mathf.RoundToInt(Mathf.Lerp(groundSporeMaxLowO2, groundSporeMaxHighO2, t));
        max = Mathf.Max(max, min);

        int spores = min;
        for (int i = min; i < max; i++)
        {
            float chance = (i == min)
                ? Mathf.Lerp(0.45f, 0.75f, sizeT)   // the likely one
                : Mathf.Lerp(0.10f, 0.35f, sizeT);  // the long shots
            if (Random.value < chance) spores++;
        }
        return spores;
    }

    // -- appended; keep new fields at the END (serialization) --

    [Header("Spores — open ground / wild")]
    [Tooltip("Spore ceiling in DEAD air. 2 = the roll the game shipped with, so barren-world foraging is unchanged.")]
    [SerializeField] int groundSporeMaxLowO2 = 2;
    [Tooltip("Spore ceiling in FULL air. 3 = a terraformed world throws one more spore than a dead one.")]
    [SerializeField] int groundSporeMaxHighO2 = 3;

    [Header("Spores — inside a Grow Pot")]
    [Tooltip("GUARANTEED spores from a potted mushroom in dead air. 1 = a pot always at least replaces itself, which is what stops a pot farm dead-ending.")]
    [SerializeField] int potSporeMinLowO2 = 1;
    [Tooltip("Guaranteed spores from a potted mushroom in full air.")]
    [SerializeField] int potSporeMinHighO2 = 2;
    [Tooltip("Spore ceiling for a potted mushroom in dead air.")]
    [SerializeField] int potSporeMaxLowO2 = 2;
    [Tooltip("Spore ceiling for a potted mushroom in full air.")]
    [SerializeField] int potSporeMaxHighO2 = 4;
}
