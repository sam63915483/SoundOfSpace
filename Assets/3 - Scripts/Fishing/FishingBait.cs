using UnityEngine;

/// <summary>
/// The bait layer: the bridge between the three Hotbar item ids and the
/// Unity-free <see cref="BaitKind"/> the rulebook rolls against. [BUILD] 3.
///
/// The rule that gives fishing its stake: bait is consumed ON THE BITE, not on
/// the cast, and it is lost whether the fish lands, the hook window is missed,
/// or the line snaps. A botched fight costs money.
/// </summary>
public static class FishingBait
{
    public struct Def
    {
        public Hotbar.ItemId item;
        public BaitKind kind;
        public string displayName;
        public int price;
        public string blurb;
    }

    /// Vendor order — cheapest first, the same convention as TevShopUI.Stock.
    public static readonly Def[] All =
    {
        new Def { item = Hotbar.ItemId.BaitGrubs, kind = BaitKind.Grubs,
                  displayName = "GRUBS", price = 1,
                  blurb = "Plain bait. Fish take it; nothing about it flatters your odds." },
        new Def { item = Hotbar.ItemId.BaitGlowworms, kind = BaitKind.Glowworms,
                  displayName = "GLOWWORMS", price = 2,
                  blurb = "Faint light in the water. Draws the bigger mid-water fish." },
        new Def { item = Hotbar.ItemId.BaitVoidmaggots, kind = BaitKind.Voidmaggots,
                  displayName = "VOIDMAGGOTS", price = 4,
                  blurb = "Something is wrong with these. The rare ones come up for them." },
    };

    public static BaitKind KindOf(Hotbar.ItemId id)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].item == id) return All[i].kind;
        return BaitKind.None;
    }

    public static bool IsBait(Hotbar.ItemId id) => KindOf(id) != BaitKind.None;

    public static Hotbar.ItemId ItemFor(BaitKind kind)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].kind == kind) return All[i].item;
        return Hotbar.ItemId.None;
    }

    public static string DisplayName(BaitKind kind)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].kind == kind) return All[i].displayName;
        return "BAIT";
    }

    /// <summary>
    /// The bait the player would fish with right now: the BEST one they carry,
    /// so buying Voidmaggots is never silently wasted by a stack of Grubs
    /// sitting in an earlier slot. Returns None when the player has no bait.
    /// </summary>
    public static BaitKind BestHeld()
    {
        if (Hotbar.Instance == null) return BaitKind.None;
        BaitKind best = BaitKind.None;
        for (int i = 0; i < All.Length; i++)
            if (Hotbar.Instance.GetResourceTotal(All[i].item) > 0) best = All[i].kind;
        return best;
    }

    public static bool HasAny() => BestHeld() != BaitKind.None;

    /// <summary>Spend one of that bait. Returns false if the player had none.</summary>
    public static bool Consume(BaitKind kind)
    {
        if (kind == BaitKind.None || Hotbar.Instance == null) return false;
        return Hotbar.Instance.SpendResource(ItemFor(kind), 1);
    }
}
