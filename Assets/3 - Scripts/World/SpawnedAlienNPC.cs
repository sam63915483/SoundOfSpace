using System.Collections.Generic;
using UnityEngine;

public class SpawnedAlienNPC : MonoBehaviour
{
    static readonly List<SpawnedAlienNPC> s_all = new List<SpawnedAlienNPC>();
    public static IReadOnlyList<SpawnedAlienNPC> AllAliens => s_all;

    AlienNPCSpawner spawner;
    int bodySlot;
    long cellId;
    int prefabIndex;

    public int BodySlot => bodySlot;
    public long CellId => cellId;
    public int PrefabIndex => prefabIndex;

    void OnEnable()
    {
        if (!s_all.Contains(this)) s_all.Add(this);
    }

    void OnDisable()
    {
        s_all.Remove(this);
    }

    public void Init(AlienNPCSpawner s, int slot, long id, int idx)
    {
        spawner = s;
        bodySlot = slot;
        cellId = id;
        prefabIndex = idx;
    }
}
