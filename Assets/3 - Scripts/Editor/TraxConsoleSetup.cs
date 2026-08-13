using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds the computer terminal to the shuttle prefab.
///
/// ⚠️ The Shuttle_Lander prefab is HAND-MAINTAINED by Sam. A past session
/// regenerated it and clobbered 139 overrides. This patches it in place via
/// LoadPrefabContents / SaveAsPrefabAsset — it adds one child object and never
/// touches anything else. Re-running it is safe: it detects its own work and
/// bails.
/// </summary>
public static class TraxConsoleSetup
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";
    const string ScreenName = "ConsoleScreen";
    const string InteractName = "ConsoleScreen_Interact";

    /// Roughly how far from the screen the prompt should appear, in metres.
    const float TriggerRadius = 1.6f;

    [MenuItem("Tools/TRAX/Add Computer Terminal To Shuttle Prefab")]
    public static void AddTerminal()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[TRAX] Could not load " + PrefabPath);
            return;
        }

        try
        {
            Transform screen = FindDeep(root.transform, ScreenName);
            if (screen == null)
            {
                Debug.LogError("[TRAX] No '" + ScreenName + "' under " + PrefabPath +
                               " — nothing changed.");
                return;
            }

            if (screen.Find(InteractName) != null)
            {
                Debug.Log("[TRAX] '" + InteractName + "' already exists on " + ScreenName +
                          ". Nothing to do.");
                return;
            }

            var go = new GameObject(InteractName);
            go.transform.SetParent(screen, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            // The interact volume must sit on a layer that collides with the
            // player. ConsoleScreen's own layer demonstrably does (the player
            // can bump into the screen), so inherit it rather than guessing.
            go.layer = screen.gameObject.layer;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            // ConsoleScreen may be scaled inside the shuttle hierarchy; convert
            // the desired world size into this transform's local units or the
            // trigger ends up the wrong size entirely.
            Vector3 s = screen.lossyScale;
            box.size = new Vector3(
                TriggerRadius * 2f / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                TriggerRadius * 2f / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                TriggerRadius * 2f / Mathf.Max(0.0001f, Mathf.Abs(s.z)));

            var term = go.AddComponent<ShuttleComputerTerminal>();
            term.gazeTarget = screen;                 // look at the screen, not the volume
            term.requireGazeToInteract = true;
            term.interactMessage = "";                // built from PromptGlyphs at runtime

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

            Debug.Log("[TRAX] Added '" + InteractName + "' under '" + ScreenName + "' in " +
                      PrefabPath + ".\n" +
                      "  layer: " + LayerMask.LayerToName(go.layer) + " (" + go.layer + ")\n" +
                      "  ConsoleScreen lossyScale: " + s + "\n" +
                      "  local BoxCollider size: " + box.size +
                      "  (≈ " + (TriggerRadius * 2f) + "m world)\n" +
                      "  Reposition/resize it in the prefab if the prompt range feels wrong.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/TRAX/Remove Computer Terminal From Shuttle Prefab")]
    public static void RemoveTerminal()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) return;

        try
        {
            Transform screen = FindDeep(root.transform, ScreenName);
            Transform existing = screen != null ? screen.Find(InteractName) : null;
            if (existing == null)
            {
                Debug.Log("[TRAX] Nothing to remove.");
                return;
            }
            Object.DestroyImmediate(existing.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[TRAX] Removed '" + InteractName + "'.");
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
