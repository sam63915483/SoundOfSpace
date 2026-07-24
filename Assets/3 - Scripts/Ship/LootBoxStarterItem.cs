using UnityEngine;

/// <summary>
/// Seeds a starting item into this GameObject's LootBox on a NEW game.
/// Lives next to the LootBox component (e.g. the shuttle's Locker_2, which
/// holds the player's starting axe).
///
/// Runs only when PendingLoad.Data is null — every save-load path (main menu
/// LOAD, death reload, backrooms round-trip) schedules a PendingLoad, and on
/// those boots SaveCollector.ApplyStorages owns the box contents. Seeding on
/// the new-game boot is then captured into the save like any other box slot,
/// so the item never respawns after being taken.
///
/// For tool items the matching controller is also Unlock()ed: the hotbar's
/// DetectAcquisitions eviction destroys any LOCKED tool that reaches a hotbar
/// slot, so a tool seeded into storage must be "owned but stored" from the
/// start — Hotbar.TryAddItem sees it via StorageRegistry.IsItemAnywhere and
/// leaves it in the box until the player withdraws it.
/// </summary>
public class LootBoxStarterItem : MonoBehaviour
{
    [Tooltip("Item seeded into the first free slot of this object's LootBox on a new game.")]
    public Hotbar.ItemId itemId = Hotbar.ItemId.Axe;
    [Tooltip("Stack count. Tools cap at 1.")]
    public int count = 1;
    [Tooltip("Unlock the matching tool controller so the item survives withdrawal to the hotbar. Leave on for tools; ignored for materials.")]
    public bool unlockController = true;

    void Start()
    {
        if (PendingLoad.Data != null) return;              // loading — save owns box contents
        if (itemId == Hotbar.ItemId.None || count <= 0) return;
        if (StorageRegistry.IsItemAnywhere(itemId)) return; // already seeded / already owned somewhere

        var box = GetComponent<LootBox>();
        if (box == null)
        {
            Debug.LogWarning($"[LootBoxStarterItem] No LootBox on {name} — nothing to seed.");
            return;
        }

        var slots = box.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].id != Hotbar.ItemId.None) continue;
            slots[i].id = itemId;
            slots[i].count = count;
            if (unlockController) UnlockToolController(itemId);
            return;
        }
        Debug.LogWarning($"[LootBoxStarterItem] {name}: no free slot to seed {itemId}.");
    }

    static void UnlockToolController(Hotbar.ItemId id)
    {
        switch (id)
        {
            case Hotbar.ItemId.Axe:         { var c = Object.FindObjectOfType<AxeController>(true);         if (c != null) c.Unlock(); break; }
            case Hotbar.ItemId.Pistol:      { var c = Object.FindObjectOfType<PistolController>(true);      if (c != null) c.Unlock(); break; }
            case Hotbar.ItemId.FishingRod:  { var c = Object.FindObjectOfType<FishingRodController>(true);  if (c != null) c.Unlock(); break; }
            case Hotbar.ItemId.Guitar:      { var c = Object.FindObjectOfType<GuitarController>(true);      if (c != null) c.SetUnlocked(true); break; }
            case Hotbar.ItemId.WaterBottle: { var c = Object.FindObjectOfType<WaterBottleController>(true); if (c != null) c.Unlock(); break; }
        }
    }
}
