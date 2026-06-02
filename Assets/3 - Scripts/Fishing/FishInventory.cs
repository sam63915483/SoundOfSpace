using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class FishEntry
{
    public string fishType;
    public int weightLbs;
    public Color fishColor;
    // Phase 2: cached preview texture for hotbar/storage slot rendering.
    // Rendered on first display via FishingdexManager.RenderFish(this, w, h),
    // re-used for the rest of the session. NonSerialized — re-rendered
    // on load. Null until the first paint.
    [System.NonSerialized] public RenderTexture cachedHotbarPreview;

    public FishEntry(string type, int weight)
    {
        fishType  = type;
        weightLbs = weight;
        fishColor = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.6f, 1f);
    }

    public int GetValue()
    {
        int pricePerLb = fishType == "Rare" ? 3 : fishType == "Uncommon" ? 2 : 1;
        return weightLbs * pricePerLb;
    }

    public static float GetXScaleFromWeight(int weightLbs)
    {
        return Mathf.Lerp(1f, 5f, (weightLbs - 1f) / 49f);
    }
}

public class FishInventory : MonoBehaviour
{
    public static FishInventory Instance { get; private set; }

    private List<FishEntry> fish = new List<FishEntry>();

    // Direct counts via single scan instead of fish.FindAll(...).Count, which
    // allocates a new List<FishEntry> on every property access.
    public int CommonCount   => CountByType("Common");
    public int UncommonCount => CountByType("Uncommon");
    public int RareCount     => CountByType("Rare");
    public List<FishEntry> AllFish => fish;

    int CountByType(string type)
    {
        int n = 0;
        for (int i = 0; i < fish.Count; i++)
            if (fish[i].fishType == type) n++;
        return n;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Returns the newly-added FishEntry so the catch flow can also push it
    // into the hotbar without peeking `AllFish` tail. This list IS the
    // lifetime dex log post-Phase 2 — the entry stays here even after
    // it's been consumed/sold from the hotbar.
    public FishEntry AddFish(string fishType, int weightLbs)
    {
        var entry = new FishEntry(fishType, weightLbs);
        fish.Add(entry);
        Debug.Log($"[FishInventory] Added {weightLbs}lb {fishType}. Total fish: {fish.Count}");
        return entry;
    }

    public int CalculateEarnings()
    {
        int total = 0;
        foreach (FishEntry entry in fish)
            total += entry.GetValue();
        return total;
    }

    public int CalculateStagedEarnings(int commonCount, int uncommonCount, int rareCount)
    {
        int total = 0;
        int c = 0, u = 0, r = 0;
        foreach (FishEntry f in fish)
        {
            if      (f.fishType == "Common"   && c < commonCount)   { total += f.GetValue(); c++; }
            else if (f.fishType == "Uncommon" && u < uncommonCount) { total += f.GetValue(); u++; }
            else if (f.fishType == "Rare"     && r < rareCount)     { total += f.GetValue(); r++; }
        }
        return total;
    }

    public void SellStaged(int commonCount, int uncommonCount, int rareCount)
    {
        RemoveByType("Common",   commonCount);
        RemoveByType("Uncommon", uncommonCount);
        RemoveByType("Rare",     rareCount);
    }

    private void RemoveByType(string type, int count)
    {
        int removed = 0;
        for (int i = fish.Count - 1; i >= 0 && removed < count; i--)
        {
            if (fish[i].fishType == type) { fish.RemoveAt(i); removed++; }
        }
    }

    public bool HasFish() => fish.Count > 0;

    public void ClearInventory() => fish.Clear();

    public void RemoveSpecificFish(FishEntry entry) => fish.Remove(entry);

    public void ReturnFish(FishEntry entry) => fish.Add(entry);

    public void ReplaceAll(List<FishEntry> entries)
    {
        fish.Clear();
        if (entries != null) fish.AddRange(entries);
    }
}

// Shared raw-eat consumption — same per-tier values FishingdexManager.OnEatRaw
// used pre-Phase 2. Phase 2's hotbar hold-LMB-eat path calls this; Phase 2 also
// removes OnEatRaw from the dex, so this becomes the single source of truth.
public static class RawFishConsumption
{
    public static void Consume(string tier)
    {
        // Per-tier table: cooked hunger + 5 trip-effect params from
        // FishingdexManager.OnEatRaw. Raw hunger is cooked * 0.5f.
        (float cooked, float ek, float ew, float ed, float lk, float lw) = tier switch
        {
            "Rare"     => (60f, 0f, 1f,  5f, 1.0f, 0f),
            "Uncommon" => (35f, 0f, 1f, 10f, 0.4f, 0f),
            _          => (20f, 0f, 1f, 30f, 0f,   1f),   // Common (fallback)
        };
        ResourceManager.Instance?.ConsumeFood(cooked * 0.5f);
        RawFishTripController.StartTrip(30f, ek, ew, ed, lk, lw);
    }
}
