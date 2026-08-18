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
    public enum SellAction { Mushrooms, Dust, Tape }

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

        // A live scheduled appointment outranks BOTH other states — they're
        // waiting for THIS delivery, so "full" is irrelevant (they text
        // because they're empty) and barred can't coexist with Scheduled
        // (barring cancels the appointment).
        var ledger = id != null ? BuyerLedger.Get(id) : null;
        bool scheduled = ledger != null && ledger.convo == BuyerLedger.Convo.Scheduled
                         && Time.unscaledTime <= ledger.deadline + BuyerDeals.GraceSeconds;

        string label;
        if (scheduled)    label = $"Deliver order — {ledger.askQty} {MushroomSpecies.TierName((MushroomTier)ledger.askTier).ToLowerInvariant()} @ {ledger.offerPerCap}";
        else if (barred)  label = $"Sell mushrooms (not talking to you — {FormatWait(MushroomDealState.SecondsLeft(id))})";
        else if (full)    label = $"Sell mushrooms (they're full — {FormatWait(MushroomDealState.SecondsUntilHungry(id, price.AppetiteMax))})";
        else              label = mushrooms > 0 ? "Sell mushrooms" : "Sell mushrooms (none on you)";

        if (FeatureVault.MushroomSelling)
        {
            rows.Add(new PostGreetingChoicePanel.Row(label, scheduled || (!barred && !full && mushrooms > 0)));
            actions.Add(SellAction.Mushrooms);
        }

        // TAPES. Offered first because it is the live economy — the mushroom
        // row above it is on its way to being vaulted (Phase 6).
        int tapes = 0;
        if (Hotbar.Instance != null)
            for (int i = 0; i < Hotbar.TotalSlots; i++)
            {
                var slot = Hotbar.Instance.SlotAt(i);
                if (slot.id == Hotbar.ItemId.Cassette) tapes += slot.count;
            }
        // A live text order outranks a cold offer: they are standing here
        // WAITING for a specific thing, and burying that under a generic row
        // would make the appointment card on the phone a lie.
        var tapeLedger = id != null ? BuyerLedger.Get(id) : null;
        bool tapeScheduled = tapeLedger != null
                          && tapeLedger.convo == BuyerLedger.Convo.Scheduled
                          && Time.unscaledTime <= tapeLedger.deadline + BuyerDeals.GraceSeconds;

        string tapeLabel;
        bool tapeEnabled;
        if (tapeScheduled)
        {
            int matching = TapeTrade.HeldMatching(tapeLedger.askTier);
            int ordShell = tapeLedger.askTapeTier >= 1 ? tapeLedger.askTapeTier : 1;
            int ordKind = TraxKind.Clamp(tapeLedger.askKind);
            int rightGoods = TapeTrade.HeldMatchingOrder(tapeLedger.askTier, ordShell, ordKind);
            string want = TapeTrade.GenreName(tapeLedger.askTier);
            string ordTier = tapeLedger.askTapeTier >= 1 ? $" T{tapeLedger.askTapeTier}" : "";
            // The order names its FORMAT too (demo is the unmarked default).
            string ordKindWord = ordKind > TraxKind.Demo
                ? " " + TraxKind.Label(ordKind).ToLowerInvariant() + "-length" : "";
            // Right genre in the wrong shell or format still delivers, but
            // pro-rata — the row must say so, not read like a full-price sale.
            tapeLabel = matching <= 0
                ? $"Deliver order — {want}{ordKindWord}{ordTier} (none on you)"
                : rightGoods <= 0
                ? $"Deliver order — {tapeLedger.askQty} {want}{ordKindWord}{ordTier} (lesser goods on you — pays less)"
                : $"Deliver order — {tapeLedger.askQty} {want}{ordKindWord}{ordTier} @ {tapeLedger.offerPerCap}";
            tapeEnabled = matching > 0;
        }
        else
        {
            tapeLabel = tapes > 0 ? "Offer them a tape" : "Offer them a tape (none on you)";
            tapeEnabled = tapes > 0;
        }

        rows.Add(new PostGreetingChoicePanel.Row(tapeLabel, tapeEnabled));
        actions.Add(SellAction.Tape);

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
        if (action == SellAction.Tape || action == SellAction.Mushrooms)
        {
            if (MushroomSellUI.Instance == null) { onClose?.Invoke(); return; }
            // The panel prices tapes from the buyer's IDENTITY now — taste, pay
            // factor and patience all derive from it, so there is no price
            // component to hand across any more.
            MushroomSellUI.Instance.Open(
                npcName: npcName,
                alienId: AlienIdentity.Of(npc),
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
