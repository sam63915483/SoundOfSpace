using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnedTree : MonoBehaviour
{
    // Maintained automatically via OnEnable/OnDisable so callers like
    // AxeController can iterate all live trees without per-frame
    // FindObjectsOfType<SpawnedTree>() (mirrors EnemyController.ActiveEnemies).
    static readonly List<SpawnedTree> s_all = new List<SpawnedTree>();
    public static IReadOnlyList<SpawnedTree> AllTrees => s_all;

    TreeSpawner spawner;
    int bodySlot;
    long cellId;
    int prefabIndex;
    int hp;
    int woodReward;
    bool dead;
    // Planted trees (matured saplings) aren't part of the seed cell grid, so
    // harvesting one must NOT mark a cell mined — it just removes the instance.
    bool isPlanted;
    // Still-GROWING sapling mode. The component is present so every axe path,
    // aim-assist scan and damage pool works on a sapling unchanged — but it is
    // NOT a tree: it produces no oxygen (PlanetOxygen and BubbleDome skip it;
    // SaplingGrowth accounts for growing saplings separately at half rate) and
    // felling it scores no Tree Killer. Cleared when the sapling matures.
    bool isSapling;
    SaplingGrowth saplingSource;
    CelestialBody plantedBody;
    // Wire identity when isPlanted/isSapling — planted props have no seed cell,
    // so a hit travels keyed by this instead (see SaplingGrowth.PlantedId).
    string plantedId;
    Vector3 _baseScale;
    Quaternion _restRotation;
    Coroutine _shakeRoutine;
    Coroutine _fallRoutine;

    public int BodySlot => bodySlot;
    public long CellId => cellId;
    public int PrefabIndex => prefabIndex;
    public int HP => hp;
    public bool IsDead => dead;
    public bool IsPlanted => isPlanted;
    public string PlantedId => plantedId;
    /// True while this is a still-growing sapling — i.e. NOT a tree for any
    /// oxygen, progression or forest-count purpose.
    public bool IsSapling => isSapling;

    void Awake()
    {
        _baseScale = transform.localScale;
    }

    void OnEnable()
    {
        if (!s_all.Contains(this)) s_all.Add(this);
    }

    void OnDisable()
    {
        s_all.Remove(this);
    }

    public void Init(TreeSpawner s, int slot, long id, int idx)
    {
        spawner = s;
        bodySlot = slot;
        cellId = id;
        plantedId = null;        // pooled instances can arrive from a planted life
        prefabIndex = idx;
        hp = Random.Range(4, 9);
        woodReward = Random.Range(8, 21);
        dead = false;
        transform.localScale = _baseScale;
        _restRotation = transform.localRotation;
        SetCollidersEnabled(true);
        if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
        if (_fallRoutine != null) { StopCoroutine(_fallRoutine); _fallRoutine = null; }
    }

    /// A player-planted tree that has matured (SaplingGrowth grew it to full).
    /// It behaves as a normal choppable tree — drops wood + saplings — but on
    /// harvest it removes its own instance instead of marking a seed cell mined.
    /// It counts toward local + planet O2 while it stands.
    /// A planted sapling that hasn't grown up yet. Choppable — so a sapling put
    /// down in the wrong place can be taken back — but it pays out far less than
    /// a tree: wood scales with how grown it is (nothing at all when freshly
    /// planted), and it returns EXACTLY the one sapling you spent.
    ///
    /// Tree Daddy is refunded on the way out, so replanting the same sapling
    /// over and over can't farm the track. Tree Killer is never scored: a
    /// sapling is not a felled tree.
    public void InitSapling(SaplingGrowth source, CelestialBody body, int idx)
    {
        spawner = null;
        isSapling = true;
        saplingSource = source;
        isPlanted = true;              // harvest removes the instance, no seed cell
        plantedBody = body;
        plantedId = source != null ? source.PlantedId : null;
        bodySlot = -1;
        cellId = 0;
        prefabIndex = idx;
        hp = 3;                        // a couple of hits — it's a stick
        woodReward = 0;                // computed from growth at Break()
        dead = false;
        _baseScale = transform.localScale;
        _restRotation = transform.localRotation;
        if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
        if (_fallRoutine != null) { StopCoroutine(_fallRoutine); _fallRoutine = null; }
    }

    public void InitPlanted(TreeSpawner s, CelestialBody body, int idx, string id = null)
    {
        spawner = s;
        isPlanted = true;
        isSapling = false;             // it grew up — it's a real tree now
        saplingSource = null;
        plantedBody = body;
        plantedId = id;
        bodySlot = -1;
        cellId = 0;
        prefabIndex = idx;
        hp = Random.Range(4, 9);
        woodReward = Random.Range(8, 21);
        dead = false;
        _baseScale = transform.localScale;   // full scale set by SaplingGrowth before this
        _restRotation = transform.localRotation;
        SetCollidersEnabled(true);
        if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
        if (_fallRoutine != null) { StopCoroutine(_fallRoutine); _fallRoutine = null; }
    }

    void SetCollidersEnabled(bool on)
    {
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null) cols[i].enabled = on;
    }

    public void TakeDamage(int amount)
    {
        if (dead || amount <= 0) return;
        hp -= amount;
        // Report the HIT, not the removal. One message then covers the wobble,
        // the topple and the despawn, because the far side runs the same code.
        // Seed trees travel by (body, cell); planted trees AND still-growing
        // saplings by their plantedId — without the second path a chopped farm
        // tree kept standing on the other player's screen.
        if (isPlanted)
            WorldSync.ReportPlantedHit(WorldSync.PropKind.Tree, plantedId, hp);
        else
            WorldSync.ReportPropHit(WorldSync.PropKind.Tree, bodySlot, cellId, hp);
        if (hp <= 0) Break();
        else PlayShake();
    }

    /// <summary>
    /// Somebody else hit this. Plays the SAME reaction a local hit plays - the
    /// wobble, and the topple-and-shrink when it dies - so both screens show the
    /// same thing rather than the prop silently vanishing.
    ///
    /// Takes the resulting HP rather than the damage amount, so a dropped
    /// message self-corrects on the next hit instead of leaving the two
    /// machines' health permanently out of step.
    ///
    /// awardLoot:false on the break - the drops belong to whoever swung. Your
    /// friend seeing your logs would let them steal your wood, and it would
    /// double every progression score.
    /// </summary>
    public void RemoteHit(int newHp)
    {
        if (dead) return;
        hp = newHp;
        if (hp <= 0) Break(awardLoot: false);
        else PlayShake();
    }

    void PlayShake()
    {
        if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    IEnumerator ShakeRoutine()
    {
        const float duration = 0.18f;
        const float amplitude = 3f;
        const float freq = 22f;
        float t = 0f;
        while (t < duration)
        {
            float decay = 1f - (t / duration);
            float angle = Mathf.Sin(t * freq) * amplitude * decay;
            transform.localRotation = _restRotation * Quaternion.Euler(angle, 0f, 0f);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localRotation = _restRotation;
        _shakeRoutine = null;
    }

    void Break() => Break(true);

    void Break(bool awardLoot)
    {
        if (dead) return;
        dead = true;
        SetCollidersEnabled(false);

        if (isSapling) { BreakSapling(awardLoot); return; }

        // Progression: TREE KILLER. Scored here rather than on the axe hit so a
        // tree only ever counts once, however it came down.
        if (awardLoot) PlayerProgress.Instance?.AddTreeFelled();
        // Orientation board line 5. Same gate as the progression score, so a
        // tree that came down without crediting the player doesn't tick it.
        if (awardLoot)
            OrientationObjectives.Complete(OrientationObjectives.Objective.ChopTree);
        // Minecraft-style loot: the wood is no longer handed straight to the
        // hotbar — it scatters as ResourceDrop sprites at the stump that the
        // player walks over to collect. ResourceDrop awards the resource and
        // fires the +N popup on pickup (and falls back to an instant award if
        // the item has no hotbar icon, e.g. saplings without a sprite).
        Transform bodyParent = transform.parent;
        // TREE KILLER perk: +floor(level/2) wood. Read AFTER AddTreeFelled above,
        // so the tree that levels you up already pays the new rate.
        if (awardLoot)
        ResourceDrop.Drop(Hotbar.ItemId.Wood, ProgressPerks.WoodPerTree(woodReward),
                          transform.position, bodyParent);
        // Ecosystem loop: every felled tree also yields saplings — 1 guaranteed,
        // +1 at 25%, +1 more at 10% (max 3) — so cutting a tree hands you the
        // means to replant. Applies to seed trees AND matured planted ones.
        if (awardLoot)
        {
            int saplings = 1;
            if (Random.value < 0.25f) saplings++;
            if (Random.value < 0.10f) saplings++;
            ResourceDrop.Drop(Hotbar.ItemId.Sapling, saplings, transform.position, bodyParent);
        }
        PlayBreakSound();
        if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
        if (_fallRoutine != null) StopCoroutine(_fallRoutine);
        _fallRoutine = StartCoroutine(FallAndShrink());
    }

    // Cutting down a sapling is a TAKE-BACK, not a harvest: you get the one
    // sapling you planted, plus whatever wood the stem has actually put on.
    //
    // The wood curve is what stops this being a free wood printer. A sapling
    // chopped the instant it's planted yields nothing at all, so plant-and-chop
    // in place is pure loss; near-maturity it tops out at MaxSaplingWood, which
    // is still a fraction of the 8–20 a grown tree drops. Letting it finish
    // growing is always the better deal.
    const int MaxSaplingWood = 4;

    void BreakSapling(bool awardLoot)
    {
        float growth = saplingSource != null ? saplingSource.Growth : 0f;
        int wood = Mathf.RoundToInt(Mathf.Lerp(0f, MaxSaplingWood, Mathf.Clamp01(growth)));

        Transform bodyParent = transform.parent;
        if (awardLoot && wood > 0) ResourceDrop.Drop(Hotbar.ItemId.Wood, wood, transform.position, bodyParent);
        // EXACTLY one, never the 1–3 roll a felled tree gets. Uprooting a stick
        // cannot multiply it.
        if (awardLoot) ResourceDrop.Drop(Hotbar.ItemId.Sapling, 1, transform.position, bodyParent);

        // Hand back the Tree Daddy point planting it awarded, so moving a
        // sapling around is progression-neutral. Guarded so a player who somehow
        // uproots more than they planted can't push the track negative.
        var progress = PlayerProgress.Instance;
        if (progress != null && progress.ScoreOf(ProgressTrack.TreeDaddy) > 0)
            progress.Add(ProgressTrack.TreeDaddy, -1);

        PlayBreakSound();
        if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
        if (_fallRoutine != null) StopCoroutine(_fallRoutine);
        _fallRoutine = StartCoroutine(FallAndShrink());
    }

    void PlayBreakSound()
    {
        if (spawner == null || spawner.treeBreakClip == null) return;
        AudioSource.PlayClipAtPoint(spawner.treeBreakClip, transform.position, spawner.treeBreakVolume);
    }

    IEnumerator FallAndShrink()
    {
        Quaternion startRot = _restRotation;
        Quaternion endRot   = startRot * Quaternion.AngleAxis(85f, Vector3.right);

        const float fallDuration = 0.7f;
        float t = 0f;
        while (t < fallDuration)
        {
            float u = t / fallDuration;
            float eased = u * u;
            transform.localRotation = Quaternion.Slerp(startRot, endRot, eased);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localRotation = endRot;

        Vector3 startScale = transform.localScale;
        const float shrinkDuration = 0.4f;
        t = 0f;
        while (t < shrinkDuration)
        {
            float u = 1f - t / shrinkDuration;
            transform.localScale = startScale * u;
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = Vector3.zero;

        _fallRoutine = null;
        Mine();
    }

    public void Mine()
    {
        // Planted trees aren't cell-based: just remove the instance. Its
        // SaplingGrowth.OnDisable drops it from the planet/local O2 counts.
        if (isPlanted) { Destroy(gameObject); return; }
        // No report here: TakeDamage already announced the hit that killed it.
        // Mine() runs at the END of the fall coroutine, seconds after
        // ApplyingRemote was cleared, so reporting here would echo every remote
        // break straight back out.
        if (spawner != null) spawner.MarkCellMined(bodySlot, cellId);
    }
}
