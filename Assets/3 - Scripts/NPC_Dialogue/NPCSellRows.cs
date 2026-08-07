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
    /// <param name="npc">The NPC being talked to. Optional, but without it the
    /// row can't know this buyer has barred the player, so pass it.</param>
    public static void Append(List<PostGreetingChoicePanel.Row> rows, List<SellAction> actions,
                              MonoBehaviour npc = null)
    {
        if (rows == null || actions == null) return;
        actions.Clear();

        int mushrooms = Hotbar.Instance != null
            ? Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Mushroom)
            : 0;

        // Two different "no" states, and they must read differently or the
        // player can't tell a punishment from a timer:
        //   BARRED  — you pushed past their counter, they're offended (5 min)
        //   FULL UP — they've simply bought all they want for now
        var price = npc != null ? NPCMushroomPrice.GetOrAdd(npc) : null;
        string id = price != null ? price.Identity : null;
        bool barred = MushroomDealState.IsBarred(id);
        bool full = !barred && price != null
                    && MushroomDealState.IsFull(id, price.AppetiteMax)
                    && MushroomDealState.LastPaid(id) > 0;   // only once they've actually bought

        string label;
        if (barred)       label = $"Sell mushrooms (not talking to you — {FormatWait(MushroomDealState.SecondsLeft(id))})";
        else if (full)    label = $"Sell mushrooms (they're full — {FormatWait(MushroomDealState.SecondsUntilHungry(id, price.AppetiteMax))})";
        else              label = mushrooms > 0 ? "Sell mushrooms" : "Sell mushrooms (none on you)";

        rows.Add(new PostGreetingChoicePanel.Row(label, !barred && !full && mushrooms > 0));
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
            // The whole NPCMushroomPrice goes across, not a single number: the
            // panel needs this buyer's multiplier AND patience to run the
            // haggle, and its identity to look up any parked counter-offer.
            var price = NPCMushroomPrice.GetOrAdd(npc);
            MushroomSellUI.Instance.Open(
                npcName: npcName,
                price: price,
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

    static string FormatWait(int seconds)
    {
        if (seconds >= 60) return $"{Mathf.CeilToInt(seconds / 60f)} min";
        return $"{Mathf.Max(1, seconds)}s";
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
