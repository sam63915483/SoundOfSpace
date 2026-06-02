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
    public BuildableCategory category = BuildableCategory.General;
}
