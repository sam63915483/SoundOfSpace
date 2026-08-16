using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pushes the SCENE's shuttle back into `Shuttle_Lander.prefab`.
///
/// ── Why this exists ──────────────────────────────────────────────────────
/// Sam builds on the shuttle IN THE SCENE, not in prefab-edit mode. That makes
/// the scene instance the true version and the prefab asset a stale snapshot:
/// as of 2026-08-14 the instance carried five added GameObjects (including
/// `cassette insert`), an added component and a long list of property overrides
/// that the asset knew nothing about. Anything that rebuilds the shuttle from
/// the prefab — or any future prefab-edit session — silently works from the old
/// version.
///
/// ⚠️ THE PREFAB IS HAND-MAINTAINED. A past session REGENERATED it and clobbered
/// 139 overrides. This does not regenerate anything: it calls Unity's own
/// ApplyPrefabInstance, which is the same operation as Inspector ▸ Overrides ▸
/// Apply All. Every override is carried across rather than rebuilt.
///
/// ── Read before you click Apply ──────────────────────────────────────────
/// Applying makes every scene override GLOBAL and leaves the instance clean. If
/// any of those overrides were deliberately scene-only, they stop being
/// scene-only. That is why REPORT comes first and is the menu item you should
/// use first — it lists exactly what would move, and changes nothing.
///
/// Safe on the "only one instance" front, verified 2026-08-14: `Shuttle_Lander`
/// is instanced in `1.6.7.7.7.unity` and NOWHERE else — not in MainMenu, 1.8,
/// the cutscene scenes, or nested inside another prefab. So an apply cannot
/// reach out and change a shuttle somewhere else. Re-check with a project-wide
/// search for the prefab's guid if that ever stops being true.
/// </summary>
public static class ShuttlePrefabSync
{
    const string PrefabPath = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";

    /// Outside Assets on purpose — a .prefab copied INTO Assets gets imported as
    /// a second real prefab and starts showing up in searches and pickers.
    const string BackupDir = "backups/prefabs";

    [MenuItem("Tools/TRAX/Shuttle/1. Report Scene Overrides (changes nothing)")]
    public static void Report()
    {
        GameObject root = FindInstanceRoot();
        if (root == null) return;

        Debug.Log(BuildReport(root));
    }

    [MenuItem("Tools/TRAX/Shuttle/2. Apply Scene Shuttle To Prefab (backs up first)")]
    public static void Apply()
    {
        GameObject root = FindInstanceRoot();
        if (root == null) return;

        string report = BuildReport(root);

        // The confirm dialog carries the counts, so the last thing seen before
        // committing is what is actually about to move.
        bool ok = EditorUtility.DisplayDialog(
            "Apply scene shuttle to the prefab?",
            report +
            "\n\nThis makes every override above GLOBAL and leaves the scene " +
            "instance clean. The current prefab is backed up first.\n\n" +
            "The prefab is hand-maintained — only do this if the SCENE is the " +
            "version you want to keep.",
            "Apply", "Cancel");
        if (!ok) { Debug.Log("[TRAX] Apply cancelled — nothing changed."); return; }

        string backup = BackUp();

        try
        {
            PrefabUtility.ApplyPrefabInstance(root, InteractionMode.UserAction);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogError("[TRAX] Apply FAILED: " + e.Message +
                           "\n  The prefab is unchanged, and a backup is at " + backup);
            return;
        }

        Debug.Log("[TRAX] Applied the scene shuttle to " + PrefabPath + ".\n" +
                  "  Backup of the previous prefab: " + backup + "\n" +
                  "  (It is outside Assets on purpose — copy it back over the\n" +
                  "   prefab file, or `git checkout` the prefab, to undo.)\n" +
                  "  ⚠ SAVE THE SCENE too: applying rewrites the instance's\n" +
                  "    override list, and that lives in the scene file.");
    }

    // ── the report ───────────────────────────────────────────────────────

    static string BuildReport(GameObject root)
    {
        var added = PrefabUtility.GetAddedGameObjects(root);
        var addedComps = PrefabUtility.GetAddedComponents(root);
        var removedComps = PrefabUtility.GetRemovedComponents(root);
        var mods = PrefabUtility.GetPropertyModifications(root);

        var sb = new StringBuilder();
        sb.AppendLine("[TRAX] Scene shuttle vs " + PrefabPath);
        sb.AppendLine("  instance: " + Path(root.transform));
        sb.AppendLine();
        sb.AppendLine("  " + added.Count + " added GameObject(s)");
        foreach (var a in added)
            if (a.instanceGameObject != null)
                sb.AppendLine("      + " + Path(a.instanceGameObject.transform));

        sb.AppendLine("  " + addedComps.Count + " added component(s)");
        foreach (var c in addedComps)
            if (c.instanceComponent != null)
                sb.AppendLine("      + " + c.instanceComponent.GetType().Name +
                              " on " + Path(c.instanceComponent.transform));

        sb.AppendLine("  " + removedComps.Count + " removed component(s)");
        foreach (var c in removedComps)
            if (c.assetComponent != null)
                sb.AppendLine("      - " + c.assetComponent.GetType().Name);

        // Modifications are the noisy ones (every tweaked position is several
        // rows), so they are summarised by object rather than listed raw.
        int modCount = mods != null ? mods.Length : 0;
        sb.AppendLine("  " + modCount + " property override(s)");
        if (modCount > 0)
        {
            var byTarget = new Dictionary<string, int>();
            foreach (var m in mods)
            {
                if (m.target == null) continue;
                string name = m.target.name;
                byTarget.TryGetValue(name, out int n);
                byTarget[name] = n + 1;
            }
            foreach (var kv in byTarget)
                sb.AppendLine("      ~ " + kv.Key + "  (" + kv.Value + ")");
        }

        return sb.ToString();
    }

    // ── plumbing ─────────────────────────────────────────────────────────

    /// <summary>
    /// The shuttle is nested under a scene organiser, not a scene root, so this
    /// walks every transform rather than only GetRootGameObjects. Matches on the
    /// prefab ASSET PATH, so a differently-named instance still resolves.
    /// </summary>
    static GameObject FindInstanceRoot()
    {
        var hits = new List<GameObject>();

        foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) != PrefabPath) continue;
            hits.Add(go);
        }

        if (hits.Count == 0)
        {
            Debug.LogError("[TRAX] No Shuttle_Lander prefab instance in the open scene. " +
                           "Open 1.6.7.7.7.unity first.");
            return null;
        }
        if (hits.Count > 1)
        {
            // Verified as impossible in 1.6.7.7.7 — but if it ever happens,
            // guessing which one is the real shuttle is exactly the kind of
            // decision that clobbers a hand-maintained prefab.
            var sb = new StringBuilder("[TRAX] Found " + hits.Count +
                                       " Shuttle_Lander instances — refusing to guess:\n");
            foreach (var h in hits) sb.AppendLine("  " + Path(h.transform));
            Debug.LogError(sb.ToString());
            return null;
        }

        return hits[0];
    }

    static string BackUp()
    {
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", BackupDir);
        Directory.CreateDirectory(dir);

        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string dest = System.IO.Path.Combine(dir, "Shuttle_Lander_" + stamp + ".prefab");
        File.Copy(PrefabPath, dest, true);
        return dest;
    }

    static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent)
            sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
