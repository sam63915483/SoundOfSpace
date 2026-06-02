using UnityEngine;

// Marker component on every ship instantiated by ShipMarketNPC. Lets the save
// system tell vendor/debug-spawned ships apart from any scene-placed ship.
//
// Fields:
//   tier        — which catalog item the player paid for (drives the load
//                 path's re-instantiation with the right attachment config).
//   shipNumber  — player-facing label index ("Ship 1", "Ship 2"...) assigned
//                 at purchase and saved. Stays stable across legend rebuilds
//                 (FindObjectsOfType returns ships in scene-order, which
//                 changes when ships are spawned/destroyed) and across
//                 save/load.
public class BoughtShip : MonoBehaviour
{
    public ShopItemKind tier;
    // Default 0 (not 1) so the AddComponent<BoughtShip>() in
    // ShipMarketNPC.SpawnShipInstance doesn't make the just-added marker count
    // toward NextShipNumber()'s max — which made the first real purchase return
    // 1+1=2 (player saw "Ship 2" for their first bought ship). Saved values
    // override on load via SaveCollector (entry.shipNumber > 0 guard).
    public int shipNumber = 0;
}
