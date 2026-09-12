#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click un-bake / re-bake of the Humble Abode village (TOWN-VILLAGE).
///
/// The village is normally mesh-combined by <see cref="MeshCombineTool"/> for draw
/// calls: the originals' MeshRenderers are disabled and a <c>__CombinedMeshes</c>
/// child holds the welded copy. While it is baked, moving or scaling a house only
/// moves its collider — the welded copy stays where it was (the ghost-door trap).
///
/// So: <b>Un-bake</b> before editing houses (re-enables every original renderer,
/// deletes the welded copy), edit freely, then <b>Re-bake</b> when done. Both are
/// undoable. Remember to save the scene after each.
/// </summary>
public static class VillageBakeMenu
{
    const string VillagePath = "--- Celestial ---/Body Simulation/Humble Abode/TOWN-VILLAGE";

    [MenuItem("Tools/Village/Un-bake Village (edit houses)")]
    static void Unbake()
    {
        var village = GameObject.Find(VillagePath);
        if (village == null)
        {
            EditorUtility.DisplayDialog("Village", $"'{VillagePath}' not found in the open scene.", "OK");
            return;
        }
        int n = MeshCombineTool.RevertUnder(village);
        Debug.Log(n == 0
            ? "[Village] Already un-baked — every house renderer is live. Edit away."
            : $"[Village] Un-baked {n} cluster(s): house renderers re-enabled, welded copy removed. Save the scene (Ctrl+S).");
    }

    [MenuItem("Tools/Village/Re-bake Village (after editing)")]
    static void Rebake()
    {
        var village = GameObject.Find(VillagePath);
        if (village == null)
        {
            EditorUtility.DisplayDialog("Village", $"'{VillagePath}' not found in the open scene.", "OK");
            return;
        }
        int draws = MeshCombineTool.RecombineOne(village);
        if (draws < 0)
        {
            EditorUtility.DisplayDialog("Village",
                "Re-bake refused: the village is still baked, or a mesh needs Read/Write Enabled (see the Console).\n\n" +
                "The scene is correct either way, just un-optimised.", "OK");
            return;
        }
        Debug.Log($"[Village] Re-baked TOWN-VILLAGE to {draws} draw group(s). Save the scene (Ctrl+S). " +
                  "If you moved houses, also run Tools ▸ Village ▸ Refresh Building Exclusion Zones.");
    }
}
#endif
