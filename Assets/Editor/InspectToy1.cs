#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class InspectToy1
{
    public static void Execute()
    {
        var toy1Obj = GameObject.Find("--- Celestial ---/Body Simulation/Humble Abode/ShipMarket/Toy1");
        if (toy1Obj == null) { Debug.LogError("[InspectToy1] Toy1 not found"); return; }
        var t = toy1Obj.transform;
        Debug.Log($"[InspectToy1] Toy1 worldPos={t.position}");
        Debug.Log($"[InspectToy1] Toy1 worldUp={t.up}");
        Debug.Log($"[InspectToy1] Toy1 worldForward={t.forward}");

        var humble = GameObject.Find("--- Celestial ---/Body Simulation/Humble Abode");
        if (humble != null)
        {
            Vector3 toCenter = (humble.transform.position - t.position).normalized;
            Vector3 awayCenter = -toCenter;
            Debug.Log($"[InspectToy1] Humble center={humble.transform.position}");
            Debug.Log($"[InspectToy1] Planet up (away from center) = {awayCenter}");
            Debug.Log($"[InspectToy1] dot(Toy1.up, planetUp)={Vector3.Dot(t.up, awayCenter):F3} (1.0 = aligned, -1.0 = upside down, 0 = perpendicular)");
        }

        Vector3 spawnPos = t.position - t.forward * 30f + t.up * 3f;
        Debug.Log($"[InspectToy1] Calculated spawn pos = {spawnPos}");
        if (humble != null)
        {
            float distFromCenter = (spawnPos - humble.transform.position).magnitude;
            Debug.Log($"[InspectToy1] Spawn distance from Humble center = {distFromCenter}");
            // Planet radius? Lookup CelestialBody
            var cb = humble.GetComponent<CelestialBody>();
            if (cb != null) Debug.Log($"[InspectToy1] Humble radius = {cb.radius} (spawn is {(distFromCenter > cb.radius ? "ABOVE" : "BELOW")} surface)");
        }
    }
}
#endif
