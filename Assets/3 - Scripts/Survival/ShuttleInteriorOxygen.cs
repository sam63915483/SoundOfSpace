using System.Collections.Generic;
using UnityEngine;

// Marks the shuttle's cabin as ALWAYS fully breathable (100% ambient O2).
// The lander keeps its own air even with the doors open — it's a permanent
// safe pocket, like a bubble dome that never depletes; step outside and
// you're back on the planet's ambient. Lives on the Shuttle_Lander prefab
// root; PlanetOxygen.AmbientO2At checks AnyContains alongside the domes.
public class ShuttleInteriorOxygen : MonoBehaviour
{
    // Static live-instance list per repo convention (no FindObjectsOfType).
    public static readonly List<ShuttleInteriorOxygen> All = new List<ShuttleInteriorOxygen>();

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public static bool AnyContains(Vector3 worldPos)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].Contains(worldPos)) return true;
        return false;
    }

    bool Contains(Vector3 worldPos)
    {
        // Cabin cylinder in prefab-local space (instance scale handled by the
        // inverse transform).
        Vector3 p = transform.InverseTransformPoint(worldPos);
        if (p.y < interiorYMin || p.y > interiorYMax) return false;
        return p.x * p.x + p.z * p.z <= interiorRadius * interiorRadius;
    }

    [Tooltip("Cabin cylinder: horizontal radius from the shuttle's centre axis (prefab-local units).")]
    public float interiorRadius = 4.1f;

    [Tooltip("Prefab-local height band of the cabin volume.")]
    public float interiorYMin = 0.4f;
    public float interiorYMax = 4.8f;
}
