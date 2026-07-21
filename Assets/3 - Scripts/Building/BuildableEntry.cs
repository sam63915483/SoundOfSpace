using System;
using UnityEngine;

public enum BuildableCategory { General, Wall, Floor, Roof, Stair, Furniture, Storage, Decor }

[Serializable]
public class BuildableEntry
{
    public string displayName = "Bonfire";
    [TextArea(3, 8)]
    public string description = "A warm fire to cook food and stay safe at night.";
    public Sprite icon;
    public GameObject prefab;
    public bool addBonfireInteractionOnPlace = true;
    [Tooltip("Wood required to place. 0 = free.")]
    public int woodCost = 0;
    [Tooltip("Crystals required to place (in addition to wood). 0 = none.")]
    public int crystalCost = 0;
    public BuildableCategory category = BuildableCategory.General;
    [Tooltip("Mark this a plantable SAPLING: placing it costs 1 Sapling from the hotbar (not wood), it's named _Sapling (not _Placed), and it gets a SaplingGrowth component so it grows into a tree. Assign any tree prefab — SaplingGrowth scales it down while young.")]
    public bool isSapling = false;
    [Tooltip("Mark this a BUBBLE DOME: uses the same steady ground-snap placement as saplings (sits flat on the surface where you look), placed full-size and saved as a normal _Placed building. The prefab should carry a BubbleDome component + the visible bubble.")]
    public bool isBubbleDome = false;
}
