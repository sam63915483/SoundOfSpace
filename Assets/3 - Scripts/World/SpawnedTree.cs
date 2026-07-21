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
    CelestialBody plantedBody;
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
    public void InitPlanted(TreeSpawner s, CelestialBody body, int idx)
    {
        spawner = s;
        isPlanted = true;
        plantedBody = body;
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
        if (hp <= 0) Break();
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

    void Break()
    {
        if (dead) return;
        dead = true;
        SetCollidersEnabled(false);
        if (WoodInventory.Instance != null)
            WoodInventory.Instance.AddWood(woodReward);
        // Ecosystem loop: every felled tree also yields saplings — 1 guaranteed,
        // +1 at 25%, +1 more at 10% (max 3) — so cutting a tree hands you the
        // means to replant. Applies to seed trees AND matured planted ones.
        if (Hotbar.Instance != null)
        {
            int saplings = 1;
            if (Random.value < 0.25f) saplings++;
            if (Random.value < 0.10f) saplings++;
            Hotbar.Instance.AddResource(Hotbar.ItemId.Sapling, saplings);
        }
        SpawnPopup();
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

    void SpawnPopup()
    {
        Vector3 popupPos = transform.position + transform.up * 3f;
        WoodPopup.Spawn(popupPos, woodReward);
    }

    public void Mine()
    {
        // Planted trees aren't cell-based: just remove the instance. Its
        // SaplingGrowth.OnDisable drops it from the planet/local O2 counts.
        if (isPlanted) { Destroy(gameObject); return; }
        if (spawner != null) spawner.MarkCellMined(bodySlot, cellId);
    }
}
