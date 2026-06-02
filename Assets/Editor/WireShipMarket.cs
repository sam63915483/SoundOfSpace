#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WireShipMarket
{
    public static void Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        Transform toy1 = null;
        foreach (var r in roots)
        {
            toy1 = FindByName(r.transform, "Toy1", "ShipMarket");
            if (toy1 != null) break;
        }
        if (toy1 == null) { Debug.LogError("[WireShipMarket] Toy1 not found"); return; }

        var vendor = toy1.GetComponent<ShipMarketNPC>();
        if (vendor == null) { Debug.LogError("[WireShipMarket] ShipMarketNPC missing on Toy1"); return; }

        var item = AssetDatabase.LoadAssetAtPath<ShopItem>("Assets/3 - Scripts/Vendor/ShopItems/Ship44.asset");
        if (item == null) { Debug.LogError("[WireShipMarket] Ship44.asset failed to load"); return; }

        vendor.inventory = new ShopItem[] { item };
        EditorUtility.SetDirty(vendor);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[WireShipMarket] Wired inventory: 1 ShopItem ({item.displayName}) onto Toy1.ShipMarketNPC.");
    }

    static Transform FindByName(Transform root, string name, string parentName)
    {
        // Look for a Toy1 whose immediate parent matches parentName (so we don't grab
        // a different Toy1 elsewhere in the scene by accident).
        if (root.name == name && root.parent != null && root.parent.name == parentName) return root;
        foreach (Transform c in root)
        {
            var hit = FindByName(c, name, parentName);
            if (hit != null) return hit;
        }
        return null;
    }
}
#endif
