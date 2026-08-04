using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The "what will this alien buy from me" rows in the post-greeting menu, in one
/// place instead of copy-pasted into every NPC script.
///
/// It exists because the answer is now build-configurable: mushrooms are the
/// live economy, and space dust is VAULTED (FeatureVault.SpaceDustSelling) —
/// still collectable, still in the hotbar, just not sellable while the mushroom
/// loop is the focus. Flipping that const back on has to bring the dust row back
/// on every NPC at once, without four separate index rewrites, so the rows and
/// the actions they map to are built together here.
///
/// Callers add their own head rows (e.g. "Open shop") and their own trailing
/// "Leave", then translate a selected index through <see cref="ActionAt"/>.
/// </summary>
public static class NPCSellRows
{
    public enum SellAction { Mushrooms, Dust }

    /// Appends every sell row this build offers to <paramref name="rows"/>, and
    /// the matching action to <paramref name="actions"/> (parallel lists).
    public static void Append(List<PostGreetingChoicePanel.Row> rows, List<SellAction> actions)
    {
        if (rows == null || actions == null) return;
        actions.Clear();

        int mushrooms = Hotbar.Instance != null
            ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Mushroom)
            : 0;
        rows.Add(new PostGreetingChoicePanel.Row(
            mushrooms > 0 ? "Sell mushrooms" : "Sell mushrooms (none on you)", mushrooms > 0));
        actions.Add(SellAction.Mushrooms);

        if (FeatureVault.SpaceDustSelling)
        {
            bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
            rows.Add(new PostGreetingChoicePanel.Row(
                hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust));
            actions.Add(SellAction.Dust);
        }
    }

    /// Translate a selected menu index into a sell action.
    /// <paramref name="headRows"/> is how many rows the caller put ABOVE the sell
    /// block. Returns false when the index isn't one of the sell rows (i.e. it's
    /// a head row or the trailing "Leave").
    public static bool ActionAt(List<SellAction> actions, int headRows, int index, out SellAction action)
    {
        action = SellAction.Mushrooms;
        if (actions == null) return false;
        int i = index - headRows;
        if (i < 0 || i >= actions.Count) return false;
        action = actions[i];
        return true;
    }

    /// Open the right sell panel for an action. <paramref name="npc"/> supplies
    /// the per-alien mushroom price (NPCMushroomPrice, stable per alien).
    public static void Open(SellAction action, MonoBehaviour npc, string npcName,
                            NPCSellDustOption dustOption, System.Action onClose,
                            System.Action<int> onMushroomsSold = null)
    {
        if (action == SellAction.Mushrooms)
        {
            if (MushroomSellUI.Instance == null) { onClose?.Invoke(); return; }
            var price = NPCMushroomPrice.GetOrAdd(npc);
            MushroomSellUI.Instance.Open(
                npcName: npcName,
                pricePerMushroom: price != null ? price.PricePerMushroom : 20,
                onClose: onClose,
                onSold: onMushroomsSold);
            return;
        }

        if (dustOption == null || SpaceDustSellUI.Instance == null) { onClose?.Invoke(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: npcName,
            acceptChance: dustOption.AcceptChance,
            pricePerDust: dustOption.PricePerDust,
            preferredMaxQty: dustOption.PreferredMaxQty,
            onClose: onClose);
    }

    /// Close whichever sell panel is open — every NPC's StopConversation calls this.
    public static void CloseAny()
    {
        if (MushroomSellUI.Instance != null && MushroomSellUI.Instance.IsOpen)
            MushroomSellUI.Instance.Close();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();
    }
}
