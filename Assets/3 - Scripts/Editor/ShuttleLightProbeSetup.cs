using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Shuttle Travel ▸ Add Light Probe + Interior Light Guard To Prefab
/// (2026-09-06). Patches the hand-maintained Shuttle_Lander prefab through
/// LoadPrefabContents / SaveAsPrefabAsset (never regenerates it):
///   • "LightProbe (cabin centre)" — a child at the exact centre of the
///     ShuttleInteriorVolume carrying ShuttleLightProbe (the test object Sam
///     asked for: F1 lists every light reaching the cabin and flags leaks).
///   • ShuttleInteriorLightGuard on the root, wired to the volume — the fix:
///     unshadowed outside lights stop lighting the hull while you're inside.
/// Idempotent; the Remove item undoes both.
/// </summary>
public static class ShuttleLightProbeSetup
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";
    const string VolumeName = "ShuttleInteriorVolume";
    const string ProbeName  = "LightProbe (cabin centre)";

    [MenuItem("Tools/Shuttle Travel/Add Light Probe + Interior Light Guard To Prefab")]
    public static void Add()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var volumeT = FindDeep(root.transform, VolumeName);
            var volume = volumeT != null ? volumeT.GetComponent<BoxCollider>() : null;
            if (volume == null)
            {
                Debug.LogError($"[ShuttleLightProbeSetup] '{VolumeName}' with a BoxCollider not found in {PrefabPath} — run Tools ▸ Shuttle Travel ▸ Add Interior Volume first.");
                return;
            }
            bool changed = false;

            var probeT = FindDeep(root.transform, ProbeName);
            if (probeT == null)
            {
                var go = new GameObject(ProbeName);
                go.transform.SetParent(volumeT, false);
                go.transform.localPosition = volume.center;   // dead centre of the cabin box
                go.transform.localRotation = Quaternion.identity;
                go.layer = volumeT.gameObject.layer;
                go.AddComponent<ShuttleLightProbe>();
                changed = true;
            }

            var guard = root.GetComponent<ShuttleInteriorLightGuard>();
            if (guard == null) { guard = root.AddComponent<ShuttleInteriorLightGuard>(); changed = true; }
            if (guard.interiorVolume != volume) { guard.interiorVolume = volume; changed = true; }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[ShuttleLightProbeSetup] {(changed ? "patched" : "already present in")} {PrefabPath}: probe under {VolumeName} at local {volume.center}, guard on root.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/Shuttle Travel/Remove Light Probe + Interior Light Guard From Prefab")]
    public static void Remove()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            bool changed = false;
            var probeT = FindDeep(root.transform, ProbeName);
            if (probeT != null) { Object.DestroyImmediate(probeT.gameObject); changed = true; }
            var guard = root.GetComponent<ShuttleInteriorLightGuard>();
            if (guard != null) { Object.DestroyImmediate(guard); changed = true; }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[ShuttleLightProbeSetup] {(changed ? "removed from" : "nothing to remove in")} {PrefabPath}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
