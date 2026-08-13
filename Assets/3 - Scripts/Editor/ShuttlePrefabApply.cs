using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pushes scene edits on the Shuttle_Lander instance back into the prefab asset.
///
/// ⚠️ Why this exists instead of just using Overrides ▸ Apply All ─────────────
/// The shuttle instance carries overrides that must NEVER reach the prefab:
///
///   • the ROOT TRANSFORM holds where the shuttle sits in the world
///     (~140.5, -133.69, 64.98 plus its rotation). Apply All would bake that
///     world placement into the asset, so every future instance would spawn
///     pre-rotated at that spot.
///   • the root GameObject's m_Name is an instance rename.
///
/// Both are legitimately scene-local. Everything else — a child moved, a part
/// resized, something switched off — is the kind of edit you actually want in
/// the prefab. So this applies per-object and skips exactly those two.
///
/// It also deliberately does NOT touch added/removed GameObjects or added
/// components. Those are structural, far harder to undo, and this prefab is
/// hand-maintained (a past regen clobbered 139 overrides). They get reported
/// instead, so the call stays yours.
///
/// The prefab is backed up to build/prefab-backup/ before anything is written.
/// </summary>
public static class ShuttlePrefabApply
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";

    [MenuItem("Tools/TRAX/Shuttle ▸ Report Scene Overrides")]
    public static void Report()
    {
        GameObject root = FindInstance();
        if (root == null) return;

        var applies = new List<Object>();
        var skips = new List<Object>();
        Classify(root, applies, skips);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Shuttle] Override report for '" + root.name + "'");

        sb.AppendLine("\nWOULD APPLY to the prefab (" + applies.Count + "):");
        foreach (Object o in applies) sb.AppendLine("   " + Describe(o, root));
        if (applies.Count == 0) sb.AppendLine("   (nothing)");

        sb.AppendLine("\nWOULD SKIP — scene-local by design (" + skips.Count + "):");
        foreach (Object o in skips) sb.AppendLine("   " + Describe(o, root));

        var added = PrefabUtility.GetAddedGameObjects(root);
        var addedComps = PrefabUtility.GetAddedComponents(root);
        var removedComps = PrefabUtility.GetRemovedComponents(root);

        sb.AppendLine("\nSTRUCTURAL — reported only, never touched by this tool:");
        sb.AppendLine("   objects added in the scene:    " + added.Count);
        foreach (var a in added)
            if (a.instanceGameObject != null) sb.AppendLine("      + " + Path(a.instanceGameObject, root));
        sb.AppendLine("   components added in the scene: " + addedComps.Count);
        foreach (var a in addedComps)
            if (a.instanceComponent != null)
                sb.AppendLine("      + " + a.instanceComponent.GetType().Name +
                              " on " + Path(a.instanceComponent.gameObject, root));
        sb.AppendLine("   components removed:            " + removedComps.Count);

        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/TRAX/Shuttle ▸ Apply Scene Edits To Prefab")]
    public static void Apply()
    {
        GameObject root = FindInstance();
        if (root == null) return;

        var applies = new List<Object>();
        var skips = new List<Object>();
        Classify(root, applies, skips);

        if (applies.Count == 0)
        {
            EditorUtility.DisplayDialog("Shuttle",
                "No applicable overrides.\n\nEverything currently overridden is scene-local " +
                "(the shuttle's world position and its instance name), which must not go into " +
                "the prefab.", "OK");
            return;
        }

        string list = "";
        for (int i = 0; i < applies.Count && i < 15; i++) list += "\n   " + Describe(applies[i], root);
        if (applies.Count > 15) list += "\n   ...and " + (applies.Count - 15) + " more";

        if (!EditorUtility.DisplayDialog("Apply shuttle edits to the prefab?",
                "This writes " + applies.Count + " object override(s) into\n" + PrefabPath + ":\n" +
                list + "\n\nSkipping " + skips.Count + " scene-local override(s) — the shuttle's " +
                "world transform and instance name stay in the scene.\n\n" +
                "Added/removed objects are NOT touched.\n\nA backup is written first.",
                "Apply", "Cancel"))
            return;

        string backup = Backup();

        int done = 0;
        foreach (Object o in applies)
        {
            // Per-object rather than Apply All: that is the whole point — it is
            // what lets the root transform stay behind.
            PrefabUtility.ApplyObjectOverride(o, PrefabPath, InteractionMode.AutomatedAction);
            done++;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Shuttle] Applied " + done + " object override(s) to " + PrefabPath +
                  ".\n  Skipped " + skips.Count + " scene-local override(s).\n" +
                  "  Backup: " + backup + "\n" +
                  "  Save the scene to persist the now-cleared overrides.");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Split overrides into "belongs in the prefab" and "belongs to this scene".
    /// The only scene-local ones are the instance root's own Transform and
    /// GameObject — position/rotation in the world, and the instance name.
    /// </summary>
    static void Classify(GameObject root, List<Object> applies, List<Object> skips)
    {
        List<ObjectOverride> overrides = PrefabUtility.GetObjectOverrides(root);
        foreach (ObjectOverride o in overrides)
        {
            Object inst = o.instanceObject;
            if (inst == null) continue;

            bool isRootTransform = inst as Transform != null && (Transform)inst == root.transform;
            bool isRootGameObject = inst as GameObject != null && (GameObject)inst == root;

            if (isRootTransform || isRootGameObject) skips.Add(inst);
            else applies.Add(inst);
        }
    }

    static GameObject FindInstance()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            GameObject found = Search(go.transform);
            if (found != null) return found;
        }
        Debug.LogError("[Shuttle] No Shuttle_Lander prefab instance in the open scene (" +
                       scene.name + "). Open the gameplay scene first.");
        return null;
    }

    static GameObject Search(Transform t)
    {
        GameObject go = t.gameObject;
        if (PrefabUtility.IsAnyPrefabInstanceRoot(go) &&
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == PrefabPath)
            return go;

        for (int i = 0; i < t.childCount; i++)
        {
            GameObject found = Search(t.GetChild(i));
            if (found != null) return found;
        }
        return null;
    }

    static string Describe(Object o, GameObject root)
    {
        var c = o as Component;
        if (c != null) return Path(c.gameObject, root) + "  (" + c.GetType().Name + ")";
        var g = o as GameObject;
        if (g != null) return Path(g, root) + "  (GameObject)";
        return o.name;
    }

    static string Path(GameObject go, GameObject root)
    {
        string p = go.name;
        Transform t = go.transform.parent;
        while (t != null && t.gameObject != root)
        {
            p = t.name + "/" + p;
            t = t.parent;
        }
        return p;
    }

    static string Backup()
    {
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.dataPath), "build", "prefab-backup");
        Directory.CreateDirectory(dir);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dest = System.IO.Path.Combine(dir, "Shuttle_Lander-" + stamp + ".prefab");
        File.Copy(PrefabPath, dest, true);
        return dest;
    }
}
