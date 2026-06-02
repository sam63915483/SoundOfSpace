using UnityEngine;

public enum ShopItemKind
{
    None = 0,
    Pistol = 1,
    Axe = 2,
    Jetpack = 3,
    // Ship-market entries — handled by ShipMarketNPC, NOT Alien7Vendor.
    ShipFull = 10,        // SHIP44 with all 4 parts attached. $2000.
    ShipNoDish = 11,      // SHIP44 without satellite dish (no map tracking). $1500.
    ShipHull = 12,        // SHIP44 with no parts at all (player must build it). $1000.
    PartLeftThruster = 20,  // $150 — auto-equips as a ship-pickup in player's hand.
    PartRightThruster = 21, // $150
    PartDish = 22,          // $250 — needed for map orbit tracking.
    PartSolarPanel = 23,    // $100
    SpaceDustFilter = 30,   // (Deprecated — kept for save compat. Filter system removed; nets gather on their own.)
    SpaceNetLeft = 31,      // Auto-equips as a ship-pickup the player installs on a ship's left mount.
    SpaceNetRight = 32,     // Same, right side.
    // Phase 3 — Alien7Vendor goods. FishingRod / WaterBottle unlock the
    // controller (already on Player root). FishBag spawns an inventory
    // item in the first empty hotbar slot; single-instance enforced via
    // Hotbar.HasFishBagAnywhere.
    FishingRod = 40,
    WaterBottle = 41,
    FishBag = 42,
}

[CreateAssetMenu(fileName = "ShopItem", menuName = "Game/Shop Item", order = 0)]
public class ShopItem : ScriptableObject
{
    [Header("Display")]
    public string displayName = "Unnamed";
    public int price = 100;
    [TextArea(3, 8)]
    public string description = "Item description.";

    [Header("Preview")]
    [Tooltip("Prefab rendered live by the shop UI for the card icon and detail view.")]
    public GameObject previewPrefab;
    [Tooltip("Local-Euler rotation applied to the prefab on the preview stage. Tune so the side faces the camera.")]
    public Vector3 previewRotationEuler = new Vector3(0f, 90f, 0f);
    [Tooltip("Zoom multiplier on top of auto-fit. 1.0 = tight fit (object fills the card); 1.2–1.5 = breathing room; 0.8 = zoom in past the bounding sphere. The shop UI computes the model's bounds and frames the camera automatically.")]
    public float previewCameraDistance = 1.2f;
    [Tooltip("FOV of the preview camera in degrees. Lower = tighter framing.")]
    public float previewCameraFov = 35f;

    [Header("Logic")]
    [Tooltip("Identifies what to grant when this item is purchased. Wired in Alien7Vendor.GrantItem.")]
    public ShopItemKind kind = ShopItemKind.None;
    [Tooltip("If true, the card shows OWNED and is unclickable after the player buys this once. Set false for consumables (ammo, food, etc.).")]
    public bool oneTimePurchase = true;
}
