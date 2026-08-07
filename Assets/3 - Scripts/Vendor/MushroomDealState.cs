using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-buyer memory for the mushroom haggle: who's currently refusing to deal,
/// what their outstanding counter-offer is, and what they last actually paid.
///
/// Keyed by <see cref="NPCMushroomPrice.Identity"/> — the same stable id the
/// price hash uses, so a wandering alien that streams out at 300 m and comes
/// back later is still the same buyer, still barred, still holding the same
/// counter.
///
/// Three things live here rather than on the NPC component itself, because a
/// streamed alien's component is destroyed and rebuilt constantly:
///
///  • <b>Barred</b> — push past a counter and they won't deal for 5 minutes.
///    The "Sell mushrooms" row greys out for that whole window.
///  • <b>Counter</b> — "LEAVE IT" parks a counter-offer instead of losing it.
///    Without this, walking away and reopening would be a free re-roll of the
///    acceptance dice; with it there is nothing to re-roll, so leaving costs
///    the player nothing and pushing is the only risky move.
///  • <b>LastPaid</b> — the one thing the panel is allowed to tell you about a
///    buyer, because you earned it by selling to them.
///
/// SESSION STATE, not saved. A 5-minute ban does not survive a save/load or a
/// scene reload. That's a deliberate v1 limitation, not an oversight: it would
/// need three parallel arrays in SaveData (JsonUtility can't do dictionaries),
/// and reloading the game to dodge a five-minute timer is slower than waiting.
/// If it ever needs persisting, mirror the shape SaveCollector uses for the
/// spawner's consumed cells (parallel key/value lists).
/// </summary>
public static class MushroomDealState
{
    /// How long a buyer refuses to deal after you push past their counter.
    public const float BarredSeconds = 300f;   // 5 minutes, Sam's spec

    /// Time for a buyer to go from completely full back to completely empty.
    /// This is the pacing dial for the whole route: too short and one generous
    /// alien is still a vending machine, too long and you run out of buyers.
    public const float AppetiteRefillSeconds = 600f;   // 10 minutes

    static readonly Dictionary<string, float>  _barredUntil    = new Dictionary<string, float>();
    static readonly Dictionary<string, int>    _counterPrice   = new Dictionary<string, int>();
    static readonly Dictionary<string, string> _counterSpecies = new Dictionary<string, string>();
    static readonly Dictionary<string, int>    _lastPaid       = new Dictionary<string, int>();
    static readonly Dictionary<string, int>    _lastQty        = new Dictionary<string, int>();
    // How many caps this buyer has taken recently, and when that was last
    // updated. Decays back to zero over AppetiteRefillSeconds.
    static readonly Dictionary<string, float>  _used           = new Dictionary<string, float>();
    static readonly Dictionary<string, float>  _usedAt         = new Dictionary<string, float>();
    // Tiers the player has actually SOLD this buyer, so "keen on rare" can only
    // ever be knowledge they earned rather than something the panel hands over.
    static readonly Dictionary<string, int>    _tiersSold      = new Dictionary<string, int>();

    // Unscaled so a paused game (or a slow-mo effect) can't stretch the ban.
    static float Now => Time.unscaledTime;

    public static bool IsBarred(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return _barredUntil.TryGetValue(id, out float t) && Now < t;
    }

    public static int SecondsLeft(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        if (!_barredUntil.TryGetValue(id, out float t)) return 0;
        return Mathf.Max(0, Mathf.CeilToInt(t - Now));
    }

    public static void Bar(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _barredUntil[id] = Now + BarredSeconds;
        ClearCounter(id);
    }

    /// Their outstanding counter for this species, or 0 if none. Species-scoped
    /// because a counter on Fly Agaric means nothing when you come back holding
    /// Buttoncaps.
    public static int Counter(string id, string species)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(species)) return 0;
        if (!_counterPrice.TryGetValue(id, out int p)) return 0;
        if (!_counterSpecies.TryGetValue(id, out string s) || s != species) return 0;
        return p;
    }

    public static void SetCounter(string id, string species, int price)
    {
        if (string.IsNullOrEmpty(id)) return;
        _counterPrice[id] = price;
        _counterSpecies[id] = species;
    }

    public static void ClearCounter(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _counterPrice.Remove(id);
        _counterSpecies.Remove(id);
    }

    /// What this buyer last actually paid, per cap. 0 = never dealt with them.
    public static int LastPaid(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        return _lastPaid.TryGetValue(id, out int p) ? p : 0;
    }

    /// How many caps they took last time. 0 = never dealt with them.
    public static int LastQty(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        return _lastQty.TryGetValue(id, out int q) ? q : 0;
    }

    // ── Appetite ───────────────────────────────────────────────────────────
    // One buyer is not a market. Without a ceiling the optimal play is to find
    // the most generous alien and stand there forever, which makes per-buyer
    // pricing, their names and the whole route pointless. A buyer takes what
    // they want, then they're full for a while and you move on.

    /// Caps this buyer has taken recently, decayed toward 0 in real time.
    static float UsedNow(string id, int max)
    {
        if (string.IsNullOrEmpty(id) || !_used.TryGetValue(id, out float used)) return 0f;
        _usedAt.TryGetValue(id, out float at);
        float elapsed = Mathf.Max(0f, Now - at);
        float decayed = used - (elapsed / Mathf.Max(1f, AppetiteRefillSeconds)) * max;
        return Mathf.Max(0f, decayed);
    }

    /// How many more caps this buyer will take right now.
    public static int Remaining(string id, int max)
    {
        if (max <= 0) return 0;
        return Mathf.Clamp(Mathf.FloorToInt(max - UsedNow(id, max)), 0, max);
    }

    /// 0 = empty and hungry, 1 = completely full. Drives the price sag: flood a
    /// buyer and what they'll pay slides, which is the same single hidden number
    /// as the volume limit rather than a second one for the player to untangle.
    public static float Fullness(string id, int max)
    {
        if (max <= 0) return 1f;
        return Mathf.Clamp01(UsedNow(id, max) / max);
    }

    /// Appetite regenerates continuously, so a stuffed buyer technically has
    /// room for one more cap within seconds. Re-opening the "Sell mushrooms" row
    /// for a single cap would be a worse experience than leaving it shut — the
    /// player walks back for nothing. A buyer only counts as available again
    /// once they'd take a quarter of their appetite.
    public static int WorthStopping(int max) => Mathf.Max(1, Mathf.CeilToInt(max * 0.25f));

    /// True when this buyer isn't worth walking to yet. Sales already in
    /// progress are NOT gated by this — if they have room for 3 and you're
    /// stood there, they take 3.
    public static bool IsFull(string id, int max) => max > 0 && Remaining(id, max) < WorthStopping(max);

    /// Seconds until this buyer is worth stopping for again. 0 when they are.
    public static int SecondsUntilHungry(string id, int max)
    {
        if (max <= 0) return 0;
        int want = WorthStopping(max);
        float used = UsedNow(id, max);
        float room = max - used;
        if (room >= want) return 0;
        // used must decay to (max - want); it sheds max/Refill per second.
        float need = used - (max - want);
        return Mathf.Max(1, Mathf.CeilToInt(need / max * AppetiteRefillSeconds));
    }

    public static void RecordSale(string id, int pricePerCap, int qty, MushroomTier tier, int appetiteMax)
    {
        if (string.IsNullOrEmpty(id)) return;
        _lastPaid[id] = pricePerCap;
        _lastQty[id] = qty;
        // Decay first, THEN add — otherwise a stale `used` from an hour ago
        // would stack on top of the new sale and the buyer would never refill.
        _used[id] = UsedNow(id, appetiteMax) + qty;
        _usedAt[id] = Now;
        _tiersSold.TryGetValue(id, out int mask);
        _tiersSold[id] = mask | (1 << (int)tier);
        ClearCounter(id);
    }

    /// True once the player has sold this buyer this tier — the only way a taste
    /// note is allowed to appear.
    public static bool HasSoldTier(string id, MushroomTier tier)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return _tiersSold.TryGetValue(id, out int mask) && (mask & (1 << (int)tier)) != 0;
    }

    /// New Game must not inherit bans or remembered counters from the run the
    /// player just backed out of (CLAUDE.md: statics leak across the main menu).
    public static void ResetAll()
    {
        _barredUntil.Clear();
        _counterPrice.Clear();
        _counterSpecies.Clear();
        _lastPaid.Clear();
        _lastQty.Clear();
        _used.Clear();
        _usedAt.Clear();
        _tiersSold.Clear();
    }
}
