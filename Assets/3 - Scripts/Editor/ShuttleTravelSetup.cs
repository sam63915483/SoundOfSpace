using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds the shuttle-travel helpers to the shuttle prefab:
///   • ShuttleInteriorVolume — trigger box over the walkable cabin, sized from
///     the Interior group's renderer bounds (the rider occupancy test, D-1).
///   • LandingLamp — a small emissive sphere on the console stand, driven
///     green/red by LandingLamp.cs during hover.
///
/// ⚠️ The Shuttle_Lander prefab is HAND-MAINTAINED by Sam. A past session
/// regenerated it and clobbered 139 overrides. This patches it in place via
/// LoadPrefabContents / SaveAsPrefabAsset — it adds two child objects and never
/// touches anything else. Re-running it is safe: it detects its own work and
/// bails. Both objects are also optional at runtime (ShuttleRiderFrame has a
/// bounds fallback; the lamp is skipped if absent) — this tool just makes them
/// real, tweakable objects. Resize/reposition freely in the prefab afterwards.
/// </summary>
public static class ShuttleTravelSetup
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";
    const string VolumeName = "ShuttleInteriorVolume";
    const string LampName = "LandingLamp";

    [MenuItem("Tools/Shuttle Travel/Add Interior Volume + Landing Lamp To Prefab")]
    public static void Add()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[ShuttleTravel] Could not load " + PrefabPath);
            return;
        }

        try
        {
            bool changed = false;
            changed |= AddVolume(root);
            changed |= AddLamp(root);
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            else Debug.Log("[ShuttleTravel] Nothing to do — both objects already exist.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool AddVolume(GameObject root)
    {
        if (FindDeep(root.transform, VolumeName) != null)
        {
            Debug.Log("[ShuttleTravel] '" + VolumeName + "' already exists.");
            return false;
        }

        Transform interior = FindDeep(root.transform, "Interior");
        // Bounds of the cabin from the Interior group's renderers, in prefab
        // space (the loaded root sits at the origin, so world == root-local).
        Bounds b = new Bounds(root.transform.position + Vector3.up * 1.5f, new Vector3(6f, 3f, 8f));
        if (interior != null)
        {
            var renderers = interior.GetComponentsInChildren<Renderer>(true);
            bool first = true;
            foreach (var r in renderers)
            {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
        }

        var go = new GameObject(VolumeName);
        go.transform.SetParent(root.transform, false);
        go.transform.position = b.center;
        go.layer = root.layer;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = b.size + Vector3.one * 0.5f;   // a little slack at the walls

        Debug.Log("[ShuttleTravel] Added '" + VolumeName + "': centre " + b.center +
                  ", size " + box.size + ". Resize in the prefab if the cabin footprint is off.");
        return true;
    }

    static bool AddLamp(GameObject root)
    {
        if (FindDeep(root.transform, LampName) != null)
        {
            Debug.Log("[ShuttleTravel] '" + LampName + "' already exists.");
            return false;
        }

        Transform anchor = FindDeep(root.transform, "ConsoleStand");
        if (anchor == null) anchor = FindDeep(root.transform, "ConsoleScreen");
        if (anchor == null) anchor = root.transform;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = LampName;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // decoration, not geometry
        go.transform.SetParent(anchor, false);
        // Perch on top of the anchor's render bounds, slightly forward.
        var r = anchor.GetComponentInChildren<Renderer>();
        Vector3 top = r != null ? r.bounds.center + Vector3.up * (r.bounds.extents.y + 0.08f)
                                : anchor.position + Vector3.up * 0.5f;
        go.transform.position = top;
        Vector3 lossy = anchor.lossyScale;
        float inv = 1f / Mathf.Max(0.0001f, (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f);
        go.transform.localScale = Vector3.one * 0.12f * inv;    // ~12 cm bulb regardless of parent scale
        go.layer = anchor.gameObject.layer;
        go.AddComponent<LandingLamp>();

        Debug.Log("[ShuttleTravel] Added '" + LampName + "' on '" + anchor.name +
                  "' at " + go.transform.localPosition + " — nudge it into place in the prefab.");
        return true;
    }

    [MenuItem("Tools/Shuttle Travel/Remove Interior Volume + Landing Lamp From Prefab")]
    public static void Remove()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) return;

        try
        {
            bool changed = false;
            var vol = FindDeep(root.transform, VolumeName);
            if (vol != null) { Object.DestroyImmediate(vol.gameObject); changed = true; }
            var lamp = FindDeep(root.transform, LampName);
            if (lamp != null) { Object.DestroyImmediate(lamp.gameObject); changed = true; }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[ShuttleTravel] " + (changed ? "Removed." : "Nothing to remove."));
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
            Transform found = FindDeep(t.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
