using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-shot helper that wires the three Phase 3 ShopItem assets into
// Alien7Vendor's inventory[] in the gameplay scene. Re-runs are idempotent
// — assets already present in inventory are skipped. After Phase 3 lands
// this script can be deleted; kept for now in case a re-wire is needed.
public static class WireFishStorageShopItems
{
    public static void Execute()
    {
        const string ScenePath = "Assets/1.6.7.7.7.unity";
        const string VendorPath = "--- Celestial ---/Body Simulation/Humble Abode/BakeryMarket_no_dop/Alien7";
        string[] toAdd =
        {
            "Assets/1 - samsPrefabs/ShopItems/ShopItem_FishingRod.asset",
            "Assets/1 - samsPrefabs/ShopItems/ShopItem_WaterBottle.asset",
            "Assets/1 - samsPrefabs/ShopItems/ShopItem_FishBag.asset",
        };

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        var vendorGO = GameObject.Find(VendorPath);
        if (vendorGO == null)
        {
            Debug.LogError($"[WireFishStorageShopItems] Could not find {VendorPath}");
            return;
        }
        var vendor = vendorGO.GetComponent<Alien7Vendor>();
        if (vendor == null)
        {
            Debug.LogError("[WireFishStorageShopItems] Alien7 has no Alien7Vendor component");
            return;
        }

        var serialized = new SerializedObject(vendor);
        var invProp = serialized.FindProperty("inventory");
        if (invProp == null || !invProp.isArray)
        {
            Debug.LogError("[WireFishStorageShopItems] Could not find serialized inventory[] array");
            return;
        }

        // Build a set of GUIDs already in inventory so we don't double-add.
        var existing = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < invProp.arraySize; i++)
        {
            var e = invProp.GetArrayElementAtIndex(i);
            if (e.objectReferenceValue == null) continue;
            string path = AssetDatabase.GetAssetPath(e.objectReferenceValue);
            existing.Add(path);
        }

        int added = 0;
        foreach (var path in toAdd)
        {
            if (existing.Contains(path)) continue;
            var asset = AssetDatabase.LoadAssetAtPath<ShopItem>(path);
            if (asset == null)
            {
                Debug.LogWarning($"[WireFishStorageShopItems] Missing asset {path} — run Tools/Fix/Create Fish & Storage ShopItems first");
                continue;
            }
            invProp.InsertArrayElementAtIndex(invProp.arraySize);
            invProp.GetArrayElementAtIndex(invProp.arraySize - 1).objectReferenceValue = asset;
            added++;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vendor);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[WireFishStorageShopItems] Added {added} ShopItem(s) to Alien7Vendor.inventory; total now {invProp.arraySize}. Scene saved.");
    }
}
