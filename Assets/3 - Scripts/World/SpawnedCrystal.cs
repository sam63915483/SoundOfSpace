using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnedCrystal : MonoBehaviour
{
    static readonly List<SpawnedCrystal> s_all = new List<SpawnedCrystal>();
    public static IReadOnlyList<SpawnedCrystal> AllCrystals => s_all;

    CrystalSpawner spawner;
    int bodySlot;
    long cellId;
    int hp;
    int crystalReward;
    bool dead;
    Quaternion _restRotation;
    Coroutine _shakeRoutine;
    Coroutine _shrinkRoutine;

    public int BodySlot => bodySlot;
    public long CellId => cellId;
    public int HP => hp;
    public bool IsDead => dead;

    void OnEnable()
    {
        if (!s_all.Contains(this)) s_all.Add(this);
    }

    void OnDisable()
    {
        s_all.Remove(this);
    }

    public void Init(CrystalSpawner s, int slot, long id, float scale)
    {
        spawner = s;
        bodySlot = slot;
        cellId = id;
        int step = Mathf.Clamp(Mathf.RoundToInt(scale), 1, 3);
        hp = Random.Range(2, 5) * step;
        crystalReward = step * 2;
        dead = false;
        // Don't touch transform.localScale here — CrystalSpawner.SpawnCrystal
        // sets it to (scale, scale, scale) before AddComponent, and uses the
        // same write for pool reuse. Resetting to _baseScale here would
        // overwrite the per-spawn scale on pool re-use (and re-introduce a
        // crystal-too-small bug after the first mine cycle).
        _restRotation = transform.localRotation;
        SetCollidersEnabled(true);
        if (_shakeRoutine  != null) { StopCoroutine(_shakeRoutine);  _shakeRoutine  = null; }
        if (_shrinkRoutine != null) { StopCoroutine(_shrinkRoutine); _shrinkRoutine = null; }
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
        const float amplitude = 4f;
        const float freq = 28f;
        float t = 0f;
        while (t < duration)
        {
            float decay = 1f - (t / duration);
            float angle = Mathf.Sin(t * freq) * amplitude * decay;
            transform.localRotation = _restRotation * Quaternion.Euler(0f, 0f, angle);
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
        if (CrystalInventory.Instance != null)
            CrystalInventory.Instance.Add(crystalReward);
        SpawnPopup();
        PlayBreakSound();
        if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
        if (_shrinkRoutine != null) StopCoroutine(_shrinkRoutine);
        _shrinkRoutine = StartCoroutine(ShrinkAndVanish());
    }

    void PlayBreakSound()
    {
        if (spawner == null || spawner.crystalBreakClip == null) return;
        AudioSource.PlayClipAtPoint(spawner.crystalBreakClip, transform.position, spawner.crystalBreakVolume);
    }

    IEnumerator ShrinkAndVanish()
    {
        Vector3 startScale = transform.localScale;
        const float shrinkDuration = 0.45f;
        float t = 0f;
        while (t < shrinkDuration)
        {
            float u = 1f - t / shrinkDuration;
            transform.localScale = startScale * (u * u);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = Vector3.zero;
        _shrinkRoutine = null;
        Mine();
    }

    void SpawnPopup()
    {
        Vector3 popupPos = transform.position + transform.up * 1.5f;
        CrystalPopup.Spawn(popupPos, crystalReward);
    }

    public void Mine()
    {
        if (spawner != null) spawner.MarkCellMined(bodySlot, cellId);
        Destroy(gameObject);
    }
}
