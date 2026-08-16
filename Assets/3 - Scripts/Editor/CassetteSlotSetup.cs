using UnityEditor;
using UnityEngine;

/// <summary>
/// Cleanup for the FIRST attempt at the cassette slot (2026-08-14), which built
/// its objects into the shuttle prefab from code.
///
/// ── That approach is dead, and this is all that's left of it ─────────────
/// The generated objects were invisible, had only a TRIGGER collider, and had
/// their `gazeTarget` pointed at ConsoleScreen. `InteractGaze` casts through the
/// crosshair, ignores triggers, and requires a hit on geometry belonging to the
/// aim target — so aiming at the slot on the console stand meant aiming at the
/// stand, not the screen, and the prompt never appeared.
///
/// The replacement is a plain component you drop on your own mesh:
/// add <see cref="CassetteSlot"/> to the insert object you built, and
/// add <see cref="CassetteSlot"/> to the insert object you built and it does
/// everything — the insert, the print, and the tape sliding back out. It wires
/// up its own trigger zone, leaves `gazeTarget` null on purpose, and auto-fills
/// the cassette model. There is no "add" menu item any more, and there should
/// never be one again — placement is Sam's, not a script's.
///
/// This menu item only exists to sweep up the old generated objects if a stale
/// copy of the prefab ever reappears (e.g. Unity re-saving it from memory after
/// the file was reverted).
/// </summary>
public static class CassetteSlotSetup
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";
    const string ScreenName = "ConsoleScreen";

    static readonly string[] Generated = { "CassetteSlot", "CassetteEject" };

    [MenuItem("Tools/TRAX/Remove Generated Cassette Slot Objects")]
    public static void RemoveGenerated()
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
                Debug.Log("[TRAX] No '" + ScreenName + "' — nothing to remove.");
                return;
            }

            bool any = false;
            foreach (string n in Generated)
            {
                Transform t = screen.Find(n);
                if (t == null) continue;
                Object.DestroyImmediate(t.gameObject);
                any = true;
            }

            if (!any)
            {
                Debug.Log("[TRAX] The prefab is already clean — no generated cassette objects.");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[TRAX] Removed the generated cassette objects from " + PrefabPath + ".\n" +
                      "  Drop CassetteSlot on your own insert mesh instead — it\n" +
                      "  handles the insert, the print and the eject on its own.");
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
