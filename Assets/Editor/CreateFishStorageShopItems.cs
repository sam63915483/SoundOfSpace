using UnityEditor;
using UnityEngine;

// One-shot editor helper that creates the three Phase 3 ShopItem assets
// Alien7Vendor needs in its inventory[]. Run via Tools menu; assets land
// in Assets/1 - samsPrefabs/ShopItems/. Idempotent — re-running overwrites
// existing assets with the same values, useful for retuning prices.
public static class CreateFishStorageShopItems
{
    const string Folder = "Assets/1 - samsPrefabs/ShopItems";

    [MenuItem("Tools/Fix/Create Fish & Storage ShopItems")]
    public static void CreateAll()
    {
        EnsureFolder();

        Make("ShopItem_FishingRod", new Setup
        {
            kind = ShopItemKind.FishingRod,
            displayName = "Fishing Rod",
            price = 50,
            description = "Cast bait, reel in fish. Required to harvest the planet's seas.",
        });
        Make("ShopItem_WaterBottle", new Setup
        {
            kind = ShopItemKind.WaterBottle,
            displayName = "Water Bottle",
            price = 30,
            description = "A reusable canteen. Refills at any water source. Drink to restore thirst.",
        });
        Make("ShopItem_FishBag", new Setup
        {
            kind = ShopItemKind.FishBag,
            displayName = "Fish Bag",
            price = 100,
            description = "Holds 5 fish in addition to your hotbar. Caught fish go into the bag first.",
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CreateFishStorageShopItems] Created/updated 3 assets in " + Folder + ". Drop them into Alien7Vendor.inventory in the scene inspector.");
    }

    struct Setup
    {
        public ShopItemKind kind;
        public string displayName;
        public int price;
        public string description;
    }

    static void Make(string assetFileName, Setup s)
    {
        string path = $"{Folder}/{assetFileName}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<ShopItem>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<ShopItem>();
            AssetDatabase.CreateAsset(asset, path);
        }
        asset.kind = s.kind;
        asset.displayName = s.displayName;
        asset.price = s.price;
        asset.description = s.description;
        asset.oneTimePurchase = true;
        // previewPrefab left null — vendor UI falls back gracefully; art polish later.
        EditorUtility.SetDirty(asset);
    }

    static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Folder)) return;
        const string Parent = "Assets/1 - samsPrefabs";
        if (!AssetDatabase.IsValidFolder(Parent)) AssetDatabase.CreateFolder("Assets", "1 - samsPrefabs");
        AssetDatabase.CreateFolder(Parent, "ShopItems");
    }
}
