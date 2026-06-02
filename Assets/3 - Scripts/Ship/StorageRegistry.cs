using System.Collections.Generic;

// Static list of all live LootBox instances. Same shape as
// EnemyController.ActiveEnemies / SpawnedTree.AllTrees — populated via the
// component's OnEnable / OnDisable. Used by:
//   - SaveCollector.CaptureStorages   (iterate live boxes)
//   - SaveCollector.ApplyStorages     (match saved by boxId)
//   - Hotbar.TryAddItem               (don't auto-pull items already stored)
public static class StorageRegistry
{
    static readonly List<LootBox> s_all = new List<LootBox>();
    public static IReadOnlyList<LootBox> All => s_all;

    public static void Register(LootBox box)
    {
        if (box == null) return;
        if (!s_all.Contains(box)) s_all.Add(box);
    }

    public static void Unregister(LootBox box)
    {
        if (box == null) return;
        s_all.Remove(box);
    }

    // Used by Hotbar.TryAddItem so a stored equippable doesn't get re-pulled
    // into the hotbar every frame by DetectAcquisitions.
    public static bool IsItemAnywhere(Hotbar.ItemId id)
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            var slots = s_all[i].Slots;
            for (int j = 0; j < slots.Length; j++)
                if (slots[j].id == id) return true;
        }
        return false;
    }
}
