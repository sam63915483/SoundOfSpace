#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: build the seven ShopItem assets for the ship vendor (3 ships +
// 4 parts), wire them into Toy1.ShipMarketNPC.inventory in the active
// scene, and assign the 4 part-pickup-prefab fields on the same component.
// Idempotent: re-running overwrites with the latest config.
public static class WireShipMarketCatalog
{
    const string ItemDir = "Assets/3 - Scripts/Vendor/ShopItems";

    const string Ship44Path = "Assets/1 - samsPrefabs/SHIP44.prefab";
    const string LeftThrusterPath = "Assets/1 - samsPrefabs/LeftThruster.prefab";
    const string RightThrusterPath = "Assets/1 - samsPrefabs/RightThruster.prefab";
    const string DishPath = "Assets/1 - samsPrefabs/satelliteDishP.prefab";
    const string SolarPath = "Assets/transfer/SolarPanel/Assets/GameReadyPrefab/SolarPanelPickup.prefab";

    public static void Execute()
    {
        if (!Directory.Exists(ItemDir)) Directory.CreateDirectory(ItemDir);

        var ship44 = AssetDatabase.LoadAssetAtPath<GameObject>(Ship44Path);
        var leftThruster = AssetDatabase.LoadAssetAtPath<GameObject>(LeftThrusterPath);
        var rightThruster = AssetDatabase.LoadAssetAtPath<GameObject>(RightThrusterPath);
        var dish = AssetDatabase.LoadAssetAtPath<GameObject>(DishPath);
        var solar = AssetDatabase.LoadAssetAtPath<GameObject>(SolarPath);

        if (ship44 == null) { Debug.LogError($"[WireShipMarketCatalog] SHIP44 not at {Ship44Path}"); return; }
        if (leftThruster == null) { Debug.LogError($"[WireShipMarketCatalog] LeftThruster not at {LeftThrusterPath}"); return; }
        if (rightThruster == null) { Debug.LogError($"[WireShipMarketCatalog] RightThruster not at {RightThrusterPath}"); return; }
        if (dish == null) { Debug.LogError($"[WireShipMarketCatalog] satelliteDishP not at {DishPath}"); return; }
        if (solar == null) { Debug.LogError($"[WireShipMarketCatalog] SolarPanelPickup not at {SolarPath}"); return; }

        // Three ship tiers — same prefab, different attachment config applied
        // post-spawn. Use Y-axis 30° for a clean 3/4 view of the hull.
        var shipFull   = CreateOrUpdate("Ship44_Full",   "SHIP44 (Full)",        2000, ShopItemKind.ShipFull,
            "All four parts attached: both thrusters, satellite dish, solar panel.\n\nFlight-ready. Map can track it.",
            ship44, rotEuler: new Vector3(0, 30, 0), camDistance: 1.5f);
        var shipNoDish = CreateOrUpdate("Ship44_NoDish", "SHIP44 (No Dish)",     1500, ShopItemKind.ShipNoDish,
            "Both thrusters + solar panel. Missing the satellite dish — flies fine but the map can't track it.",
            ship44, rotEuler: new Vector3(0, 30, 0), camDistance: 1.5f);
        var shipHull   = CreateOrUpdate("Ship44_Hull",   "SHIP44 (Hull Only)",   1000, ShopItemKind.ShipHull,
            "Just the hull. Buy the thrusters separately and install them before flying.",
            ship44, rotEuler: new Vector3(0, 30, 0), camDistance: 1.5f);

        // Four parts.
        var partLeft  = CreateOrUpdate("Part_LeftThruster",  "Left Thruster",     150, ShopItemKind.PartLeftThruster,
            "Left thruster module. Auto-equips on purchase — walk it back to the ship and mount it.",
            leftThruster, rotEuler: new Vector3(0, 90, 0), camDistance: 1.4f);
        var partRight = CreateOrUpdate("Part_RightThruster", "Right Thruster",    150, ShopItemKind.PartRightThruster,
            "Right thruster module. Auto-equips on purchase.",
            rightThruster, rotEuler: new Vector3(0, 90, 0), camDistance: 1.4f);
        var partDish  = CreateOrUpdate("Part_Dish",          "Satellite Dish",    250, ShopItemKind.PartDish,
            "Satellite uplink dish. Install on a ship to make it visible on the map.",
            dish, rotEuler: new Vector3(0, 30, 0), camDistance: 1.4f);
        var partSolar = CreateOrUpdate("Part_SolarPanel",    "Solar Panel",       100, ShopItemKind.PartSolarPanel,
            "Solar panel. Bolts to the ship for cosmetic + power gain (no flight effect).",
            solar, rotEuler: new Vector3(0, 30, 0), camDistance: 1.4f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Now wire Toy1.
        var scene = EditorSceneManager.GetActiveScene();
        Transform toy1 = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            toy1 = FindByPath(root.transform, "Toy1", "ShipMarket");
            if (toy1 != null) break;
        }
        if (toy1 == null) { Debug.LogError("[WireShipMarketCatalog] Toy1 not found in active scene."); return; }

        var vendor = toy1.GetComponent<ShipMarketNPC>();
        if (vendor == null) { Debug.LogError("[WireShipMarketCatalog] ShipMarketNPC component missing on Toy1."); return; }

        vendor.inventory = new[] { shipFull, shipNoDish, shipHull, partLeft, partRight, partDish, partSolar };
        vendor.leftThrusterPickupPrefab = leftThruster;
        vendor.rightThrusterPickupPrefab = rightThruster;
        vendor.dishPickupPrefab = dish;
        vendor.solarPanelPickupPrefab = solar;

        EditorUtility.SetDirty(vendor);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[WireShipMarketCatalog] 7 ShopItems created, Toy1.ShipMarketNPC wired (inventory + 4 pickup prefabs). Scene saved.");
    }

    static ShopItem CreateOrUpdate(string fileName, string displayName, int price, ShopItemKind kind,
                                   string description, GameObject previewPrefab,
                                   Vector3 rotEuler, float camDistance)
    {
        string path = $"{ItemDir}/{fileName}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ShopItem>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ShopItem>();
            AssetDatabase.CreateAsset(item, path);
        }
        item.displayName = displayName;
        item.price = price;
        item.description = description;
        item.previewPrefab = previewPrefab;
        item.previewRotationEuler = rotEuler;
        item.previewCameraDistance = camDistance;
        item.previewCameraFov = 35f;
        item.kind = kind;
        item.oneTimePurchase = false; // Ships + parts stack; no "OWNED" gate.
        EditorUtility.SetDirty(item);
        return item;
    }

    static Transform FindByPath(Transform root, string name, string parentName)
    {
        if (root.name == name && root.parent != null && root.parent.name == parentName) return root;
        foreach (Transform c in root)
        {
            var hit = FindByPath(c, name, parentName);
            if (hit != null) return hit;
        }
        return null;
    }
}
#endif
