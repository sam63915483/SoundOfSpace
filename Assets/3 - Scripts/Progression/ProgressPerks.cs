using UnityEngine;

/// <summary>
/// What the five progression tracks actually PAY OUT, in one place.
///
/// Until now only Colonizer granted anything (BuildableUnlocks). The other
/// perks were specced in docs/PROGRESSION_PERKS.md but each would have been an
/// ad-hoc PlayerProgress.Instance.LevelOf(...) read buried at its own call
/// site. This class is the dispatch point so the numbers live together and can
/// be retuned without hunting through the codebase.
///
/// ── The one rule ─────────────────────────────────────────────────────────
/// Every perk here DERIVES from the live track level and stores nothing. That
/// is why none of them need save work: PlayerProgress already saves the score,
/// so a load restores the perk for free. Do not add a cached or serialized
/// perk state — it would be a second source of truth that can disagree with
/// the level the player is looking at.
///
/// Null-safe throughout: PlayerProgress is an auto-singleton that may not exist
/// yet on very early frames (or at all, off the gameplay scene), so every
/// accessor falls back to the un-perked base value rather than throwing.
/// </summary>
public static class ProgressPerks
{
    static int LevelOf(ProgressTrack track)
    {
        var p = PlayerProgress.Instance;
        return p != null ? p.LevelOf(track) : 0;
    }

    // ── Tree Killer → more wood per felled tree ──────────────────────────
    /// Wood a felled tree yields at the player's current Tree Killer level.
    /// base + floor(level / 2), so the step lands every other level and stays
    /// legible in the pickup popup ("+4 WOOD", never "+3.6").
    ///
    /// Compounds with the Colonizer unlocks — wood is the build-menu currency —
    /// so the bonus is deliberately flat rather than a multiplier. At L10 it is
    /// +5 on a base of 8–20, a help rather than a firehose.
    public static int WoodPerTree(int baseWood)
        => Mathf.Max(0, baseWood) + LevelOf(ProgressTrack.TreeKiller) / 2;

    // ── Tree Daddy → saplings grow faster ────────────────────────────────
    /// Growth-rate multiplier for PLANTED SAPLINGS at the current Tree Daddy
    /// level: 1 + 0.12 × level, so L10 is ~2.2× planting speed.
    ///
    /// Saplings only, not mushrooms. Trees are the oxygen supply, so this perk
    /// leans on the terraforming half of the loop; letting it speed the crop up
    /// too would make Tree Daddy strictly better than every other track.
    public static float SaplingGrowthMultiplier()
        => 1f + SaplingGrowthPerLevel * LevelOf(ProgressTrack.TreeDaddy);

    const float SaplingGrowthPerLevel = 0.12f;

    // ── Gangsta Rep → better vendor stock ────────────────────────────────
    /// Rep thresholds at which the goods vendor opens another tier of stock.
    /// A ShopItem is offered when its own required tier is <= this.
    public const int RepTier1 = 3;
    public const int RepTier2 = 6;

    /// 0, 1 or 2 — how deep into the vendor's stock list the player has earned
    /// access. Derived at shop-open, never stored, so selling your way up is
    /// visible the very next time you open the shop.
    ///
    /// Note LevelOf floors GangstaRep at 0 even though the score is stored
    /// signed, so going negative shrinks you back to base stock rather than
    /// refusing service. Refusing service is a design question nobody has
    /// answered yet; base stock is the safe read.
    public static int VendorStockTier()
    {
        int rep = LevelOf(ProgressTrack.GangstaRep);
        if (rep >= RepTier2) return 2;
        if (rep >= RepTier1) return 1;
        return 0;
    }
}
