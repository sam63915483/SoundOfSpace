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
    public void InitPlanted(string species)
    {
        spawner = null;
        bodySlot = -1;
        cellId = 0;
        isPlanted = true;
        speciesKey = species;
        mushroomScale = 1f;
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
        hp = Random.Range(2, 5);
        // 3–9 caps per mushroom (handoff §3).
        dropCount = Random.Range(3, 10);
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
        int saplings = 0;
        if (Random.value < 0.55f) saplings++;
        if (Random.value < 0.20f) saplings++;
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
}
