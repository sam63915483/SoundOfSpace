// Headless stubs for the rent ledger and the cassette machine.
//
// MushroomQuest.cs and CassetteDeck.cs are the two places where the new loop's
// NUMBERS live — what you owe after three missed days, when the plugin embargo
// bites, whether a blank comes back out of the slot intact. All of that is
// arithmetic and state transitions, and all of it is invisible in play until it
// is already wrong, so it gets executed rather than eyeballed.
//
// Everything here is a stand-in for something Unity owns. Keep the SHAPES
// identical to the real ones — a stub that is kinder than the real Hotbar tests
// nothing.

using System.Collections.Generic;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}

/// Story flags + counters. The real one is a MonoBehaviour that rides the save;
/// the only thing that matters here is that it is a persistent key/value store.
public class StoryDirector
{
    public static StoryDirector Instance;

    readonly Dictionary<string, int> _counters = new Dictionary<string, int>();
    readonly Dictionary<string, bool> _flags = new Dictionary<string, bool>();

    public int GetCounter(string k) { int v; return _counters.TryGetValue(k, out v) ? v : 0; }
    public void SetCounter(string k, int v) { _counters[k] = v; }
    public bool GetFlag(string k) { bool v; return _flags.TryGetValue(k, out v) ? v : false; }
    public void SetFlag(string k, bool v) { _flags[k] = v; }

    public static void Reset() { Instance = new StoryDirector(); }
}

public class GalaxyTime
{
    public static GalaxyTime Instance;
    public const int DaysPerWeek = 7;
    public int Day = 1;
    public static void Reset() { Instance = new GalaxyTime(); }
}

public class PlayerWallet
{
    public static PlayerWallet Instance;
    public int Money;

    /// All-or-nothing, exactly like the real wallet: an unaffordable spend
    /// changes nothing and reports false.
    public bool SpendMoney(int amount)
    {
        if (amount <= 0 || amount > Money) return false;
        Money -= amount;
        return true;
    }

    public static void Reset() { Instance = new PlayerWallet(); }
}

/// A deliberately mean Hotbar: a fixed number of stacks with a real cap, so
/// "there was no room" is a state the tests can actually reach.
public class Hotbar
{
    public enum ItemId { None, WaterBottle, FishingRod, Guitar, Axe, Pistol, Wood, Crystal, SpaceDust, Fish, FishBag, Sapling, Mushroom, MushroomSapling, Money, BlankTapeT1, BlankTapeT2, Cassette, BlankTapeHalfT1, BlankTapeHalfT2, BlankTapeFullT1, BlankTapeFullT2 }

    public static Hotbar Instance;

    public const int StackCap = 20;
    public int SlotCount = 8;

    public class Stack { public ItemId id; public string variant; public int count; }
    public readonly List<Stack> Stacks = new List<Stack>();

    public ItemId EquippedId = ItemId.None;
    public string EquippedVariant;

    public ItemId GetEquippedSlotId() { return EquippedId; }

    public int GetResourceTotal(ItemId id)
    {
        int n = 0;
        foreach (var s in Stacks) if (s.id == id) n += s.count;
        return n;
    }

    public int GetVariantTotal(ItemId id, string variant)
    {
        int n = 0;
        foreach (var s in Stacks) if (s.id == id && s.variant == variant) n += s.count;
        return n;
    }

    /// Returns the LEFTOVER — what didn't fit — like the real one.
    public int AddResource(ItemId id, int amount, string variant = null)
    {
        int remaining = amount;
        foreach (var s in Stacks)
        {
            if (remaining <= 0) break;
            if (s.id != id || s.variant != variant) continue;
            int room = StackCap - s.count;
            if (room <= 0) continue;
            int take = room < remaining ? room : remaining;
            s.count += take;
            remaining -= take;
        }
        while (remaining > 0 && Stacks.Count < SlotCount)
        {
            int take = StackCap < remaining ? StackCap : remaining;
            Stacks.Add(new Stack { id = id, variant = variant, count = take });
            remaining -= take;
        }
        return remaining;
    }

    /// All-or-nothing, like the real one.
    public bool SpendResource(ItemId id, int amount, string variant = null)
    {
        int have = variant == null ? GetResourceTotal(id) : GetVariantTotal(id, variant);
        if (have < amount) return false;
        int remaining = amount;
        for (int i = 0; i < Stacks.Count && remaining > 0; i++)
        {
            var s = Stacks[i];
            if (s.id != id) continue;
            if (variant != null && s.variant != variant) continue;
            int take = s.count < remaining ? s.count : remaining;
            s.count -= take;
            remaining -= take;
        }
        Stacks.RemoveAll(s => s.count <= 0);
        return true;
    }

    public int AddCassette(string printId, int amount)
    {
        if (string.IsNullOrEmpty(printId) || amount <= 0) return 0;
        return amount - AddResource(ItemId.Cassette, amount, printId);
    }

    public int GetCassetteTotal(string printId) { return GetVariantTotal(ItemId.Cassette, printId); }

    public static void Reset() { Instance = new Hotbar(); }

    public void Fill(ItemId id, int slots)
    {
        for (int i = 0; i < slots; i++) Stacks.Add(new Stack { id = id, count = StackCap });
    }
}

public static class MushroomRegistry
{
    public static string KeyForSeed(int seed) { return "mush" + seed; }
}

/// Only the flags the two files under test read.
public static class FeatureVault
{
    public const bool TevFrontingEconomy = false;
    public const bool TevLawnWorkOff = false;
}

// ── save DTOs (SaveData.cs pulls in UnityEngine) ──────────────────────────
// Keep field-for-field identical to the real ones or this stops testing what
// ships.

public class TraxProjectSave
{
    public string id;
    public string name;
    public long savedAt;
    public int key;
    public List<float> dials = new List<float>();
    public List<int> preset = new List<int>();
    public List<int> variation = new List<int>();
    public List<bool> active = new List<bool>();
    public List<TraxSectionSave> sections = new List<TraxSectionSave>();
}

public class TraxSectionSave
{
    public int bars;
    public int key;
    public List<float> dials = new List<float>();
    public List<int> preset = new List<int>();
    public List<int> variation = new List<int>();
    public List<bool> active = new List<bool>();
}

public class TraxPrintSave
{
    public string id;
    public string name;
    public int tier;
    public int key;
    public List<float> dials = new List<float>();
    public List<int> preset = new List<int>();
    public List<int> variation = new List<int>();
    public List<bool> active = new List<bool>();
    public int kind;
    public List<TraxSectionSave> sections = new List<TraxSectionSave>();
}

public class TraxLibrarySave
{
    public List<TraxProjectSave> projects = new List<TraxProjectSave>();
    public List<string> installedPlugins = new List<string>();
    public List<TraxPrintSave> prints = new List<TraxPrintSave>();
    public int deckInsertedTier;
    public string deckEjectedPrintId = "";
    public int deckInsertedKind;
}
