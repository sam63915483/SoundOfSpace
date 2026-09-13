using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Village ▸ Lanterns off by day — puts DayNightLight on every village
/// lantern light (the `LanternLight` children of `Lantern_02*` under Humble
/// Abode). Idempotent; registers Undo; Sam saves the scene.
/// </summary>
public static class DayNightLightSetup
{
    [MenuItem("Tools/Village/Lanterns off by day (add DayNightLight)")]
    public static void AddToLanterns()
    {
        int added = 0, had = 0;
        foreach (var steady in Object.FindObjectsOfType<TorchLightSteady>(true))
        {
            if (steady.name != "LanternLight") continue;
            if (steady.GetComponentInParent<CelestialBody>() == null) continue;
            if (steady.GetComponent<DayNightLight>() != null) { had++; continue; }
            Undo.AddComponent<DayNightLight>(steady.gameObject);
            added++;
        }
        Debug.Log($"[Village] DayNightLight: added to {added} lantern light(s), {had} already had it. SAVE the scene.");
    }

    [MenuItem("Tools/Village/Lanterns off by day — include market torches")]
    public static void AddToTorches()
    {
        int added = 0, had = 0;
        foreach (var steady in Object.FindObjectsOfType<TorchLightSteady>(true))
        {
            if (steady.name != "TorchLight") continue;
            if (steady.GetComponentInParent<CelestialBody>() == null) continue;
            if (steady.GetComponent<DayNightLight>() != null) { had++; continue; }
            Undo.AddComponent<DayNightLight>(steady.gameObject);
            added++;
        }
        Debug.Log($"[Village] DayNightLight: added to {added} torch light(s), {had} already had it. SAVE the scene.");
    }
}
