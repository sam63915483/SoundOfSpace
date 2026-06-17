#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-only draw-call optimiser for STATIC scenery that sits on a moving
/// celestial body (the village, cabin, markets, etc.). Unity's automatic Static
/// Batching can't be used here — the buildings ride a planet that orbits and gets
/// re-centred by the floating-origin system, and static batching bakes fixed
/// world positions. This is the manual equivalent: it merges a cluster's meshes
/// BY MATERIAL into a few combined meshes PARENTED TO THE CLUSTER so they still
/// move with the planet, collapsing hundreds of building draw calls into
/// one-per-material.
///
/// SAFE / REVERSIBLE:
///   • Never deletes anything — only DISABLES the original MeshRenderers, so
///     colliders, scripts, transforms, NPCs and the save system's "_Placed"
///     lookups all keep working (grass still raycasts the real colliders).
///   • Combined output lives under a child "__CombinedMeshes" per cluster, so
///     Revert finds it and re-enables the originals. Everything is Undo-wrapped.
///
/// AUTOMATICALLY SKIPS things that must keep their own renderer:
///   • SkinnedMeshRenderers and anything under an Animator / Rigidbody (the
///     waving market NPCs, ragdolls, dynamic props).
///   • "_Placed" objects (player-built, save-tracked).
///   • Generated planet geometry by name ("Mesh Holder", "Terrain", ...).
///   • Whole protected clusters by name in the all-clusters pass (concert
///     STAGEs — their light/laser meshes move via script — atmosphere, ocean,
///     sun, stars). Use the per-selection command to force one of those if ever
///     needed.
///
/// REQUIREMENT: meshes must have "Read/Write Enabled" in their import settings
/// (CombineMeshes reads vertices CPU-side). The tool checks first and, if any are
/// unreadable, lists the exact assets and does nothing (no partial combine).
///
/// TWO WAYS TO RUN:
///   • Tools ▸ Optimize ▸ Combine Selected Static Meshes — combines each object
///     you've selected (Ctrl-click several clusters to do them in one go).
///   • Tools ▸ Optimize ▸ Combine All Static Clusters Under Selection — select
///     the planet root (e.g. Humble Abode); it combines each safe child cluster
///     separately (preserving per-cluster frustum culling).
///   • Tools ▸ Optimize ▸ Revert Combined Meshes Under Selection — undoes either.
/// </summary>
public static class MeshCombineTool
{
    const string CombinedRootName = "__CombinedMeshes";

    // ── Menu commands ────────────────────────────────────────────────────────

    [MenuItem("Tools/Optimize/Combine Selected Static Meshes (by material)")]
    static void CombineSelected()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            EditorUtility.DisplayDialog("Combine Meshes",
                "Select one or more cluster roots in the Hierarchy first (Ctrl-click to pick several).", "OK");
            return;
        }
        RunCombine(new List<GameObject>(sel));
    }

    [MenuItem("Tools/Optimize/Combine All Static Clusters Under Selection")]
    static void CombineAllClusters()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Combine Meshes",
                "Select the planet/body root (e.g. Humble Abode) first.", "OK");
            return;
        }
        var clusters = new List<GameObject>();
        foreach (Transform child in root.transform)
            if (!IsProtectedCluster(child)) clusters.Add(child.gameObject);

        if (clusters.Count == 0)
        {
            EditorUtility.DisplayDialog("Combine Meshes",
                $"No combinable child clusters under '{root.name}' (all were protected: planet mesh, stages, atmosphere, etc.).", "OK");
            return;
        }
        RunCombine(clusters);
    }

    [MenuItem("Tools/Optimize/Revert Combined Meshes Under Selection")]
    static void RevertUnderSelection()
    {
        var root = Selection.activeGameObject;
        if (root == null) return;

        var combinedRoots = new List<Transform>();
        FindCombinedRoots(root.transform, combinedRoots);
        if (combinedRoots.Count == 0)
        {
            EditorUtility.DisplayDialog("Revert", $"No combined meshes found under '{root.name}'.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Revert Combined Meshes");
        int group = Undo.GetCurrentGroup();

        // Re-enable every MeshRenderer that isn't part of a combined-output tree.
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            bool underCombined = false;
            for (int i = 0; i < combinedRoots.Count; i++)
                if (mr.transform.IsChildOf(combinedRoots[i])) { underCombined = true; break; }
            if (underCombined || mr.enabled) continue;
            Undo.RecordObject(mr, "Revert Combined Meshes");
            mr.enabled = true;
            EditorUtility.SetDirty(mr);
        }
        for (int i = 0; i < combinedRoots.Count; i++)
            Undo.DestroyObjectImmediate(combinedRoots[i].gameObject);

        Undo.CollapseUndoOperations(group);
        MarkDirty(root);
        Debug.Log($"[MeshCombineTool] Reverted {combinedRoots.Count} cluster(s) under '{root.name}': originals re-enabled, combined meshes removed.");
    }

    // ── Core ─────────────────────────────────────────────────────────────────

    // Combine a set of roots, each independently. Verifies readability across ALL
    // roots first and aborts cleanly (no partial combine) if any mesh is unreadable.
    static void RunCombine(List<GameObject> roots)
    {
        var jobs = new List<(GameObject root, List<MeshRenderer> rends)>();
        var unreadable = new HashSet<Mesh>();

        foreach (var root in roots)
        {
            if (root == null) continue;
            if (root.transform.Find(CombinedRootName) != null) continue; // already combined
            var rends = new List<MeshRenderer>();
            CollectEligible(root.transform, root.transform, rends, unreadable);
            if (rends.Count > 0) jobs.Add((root, rends));
        }

        if (jobs.Count == 0)
        {
            EditorUtility.DisplayDialog("Combine Meshes",
                "Found no eligible static MeshRenderers (everything was already combined, skinned/animated/physics, '_Placed', or protected).", "OK");
            return;
        }

        if (unreadable.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("These meshes need 'Read/Write Enabled' in their model Import Settings before they can be combined:\n");
            int shown = 0;
            foreach (var m in unreadable)
            {
                if (m == null) continue;
                string path = AssetDatabase.GetAssetPath(m);
                sb.AppendLine("• " + (string.IsNullOrEmpty(path) ? m.name : path));
                if (++shown >= 30) { sb.AppendLine("• …and more (see Console)"); break; }
            }
            sb.AppendLine("\nSelect each model, tick Read/Write Enabled, Apply, then run this again. Nothing was changed.");
            Debug.LogWarning("[MeshCombineTool] " + sb);
            EditorUtility.DisplayDialog("Combine Meshes — meshes not readable", sb.ToString(), "OK");
            return;
        }

        Undo.SetCurrentGroupName("Combine Static Meshes");
        int group = Undo.GetCurrentGroup();

        int totalRenderers = 0, totalDraws = 0;
        var report = new StringBuilder();
        foreach (var (root, rends) in jobs)
        {
            int draws = CombineOneRoot(root, rends);
            totalRenderers += rends.Count;
            totalDraws += draws;
            report.AppendLine($"  • {root.name}: {rends.Count} renderer(s) → {draws} draw call(s)");
        }

        Undo.CollapseUndoOperations(group);
        if (jobs.Count > 0) MarkDirty(jobs[0].root);
        Debug.Log($"[MeshCombineTool] Combined {jobs.Count} cluster(s): {totalRenderers} renderer(s) → {totalDraws} draw call(s). " +
                  $"Originals disabled (reversible via Tools ▸ Optimize ▸ Revert).\n{report}");
    }

    // Combine one cluster's eligible renderers, grouped by material, into meshes
    // parented under a __CombinedMeshes child. Returns the draw-call count (= the
    // number of unique materials).
    static int CombineOneRoot(GameObject root, List<MeshRenderer> eligible)
    {
        var byMaterial = new Dictionary<Material, List<CombineInstance>>();
        var layerByMaterial = new Dictionary<Material, int>();
        Matrix4x4 rootW2L = root.transform.worldToLocalMatrix;

        foreach (var mr in eligible)
        {
            var mf = mr.GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;
            var mats = mr.sharedMaterials;
            int subCount = mesh.subMeshCount;
            Matrix4x4 local = rootW2L * mr.transform.localToWorldMatrix;

            for (int s = 0; s < subCount; s++)
            {
                Material mat = s < mats.Length ? mats[s] : null;
                if (mat == null) continue;
                if (!byMaterial.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    byMaterial[mat] = list;
                    layerByMaterial[mat] = mr.gameObject.layer;
                }
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = local });
            }
        }

        var combinedRoot = new GameObject(CombinedRootName);
        Undo.RegisterCreatedObjectUndo(combinedRoot, "Combine Static Meshes");
        combinedRoot.transform.SetParent(root.transform, false);
        combinedRoot.transform.localPosition = Vector3.zero;
        combinedRoot.transform.localRotation = Quaternion.identity;
        combinedRoot.transform.localScale = Vector3.one;
        combinedRoot.isStatic = false; // moves with the planet — must NOT be static

        int matIndex = 0;
        foreach (var kv in byMaterial)
        {
            var mat = kv.Key;
            var instances = kv.Value;

            var mesh = new Mesh { name = $"Combined_{root.name}_{matIndex}", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(instances.ToArray(), mergeSubMeshes: true, useMatrices: true);
            mesh.RecalculateBounds();

            var go = new GameObject($"Combined_{(mat != null ? mat.name : "mat")}_{matIndex}");
            Undo.RegisterCreatedObjectUndo(go, "Combine Static Meshes");
            go.transform.SetParent(combinedRoot.transform, false);
            go.layer = layerByMaterial[mat];
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = ShadowCastingMode.On;
            rend.receiveShadows = true;
            matIndex++;
        }

        // Non-destructive: hide the originals' rendering only.
        foreach (var mr in eligible)
        {
            Undo.RecordObject(mr, "Combine Static Meshes");
            mr.enabled = false;
            EditorUtility.SetDirty(mr);
        }

        return byMaterial.Count;
    }

    // ── Collection / filtering ────────────────────────────────────────────────

    static void CollectEligible(Transform t, Transform root, List<MeshRenderer> outList, HashSet<Mesh> unreadable)
    {
        if (t.name == CombinedRootName) return;          // never recombine our own output
        if (t.name.Contains("_Placed")) return;          // save-tracked player builds — skip subtree
        // Generated planet surface — forbidden to touch and pointless to combine.
        if (t.name.Contains("Mesh Holder") || t.name.Contains("Terrain Mesh")) return;

        var mr = t.GetComponent<MeshRenderer>();
        var mf = t.GetComponent<MeshFilter>();
        if (mr != null && mr.enabled && mf != null && mf.sharedMesh != null && IsCombinable(t, root))
        {
            if (!mf.sharedMesh.isReadable) unreadable.Add(mf.sharedMesh);
            outList.Add(mr);
        }
        for (int i = 0; i < t.childCount; i++)
            CollectEligible(t.GetChild(i), root, outList, unreadable);
    }

    // Static-only filter. Walk UP from t toward root but NEVER check the root
    // itself — the cluster/planet root is the thing everything is parented to and
    // combined relative to, and the planet root is a Rigidbody for the N-body sim
    // (checking it would disqualify every surface object). Intermediate dynamic
    // parents (an NPC's Animator, a physics prop) still correctly exclude their
    // meshes.
    static bool IsCombinable(Transform t, Transform root)
    {
        if (t.GetComponent<SkinnedMeshRenderer>() != null) return false;
        Transform cur = t;
        while (cur != null && cur != root)
        {
            if (cur.GetComponent<Animator>() != null) return false;
            if (cur.GetComponent<Rigidbody>() != null) return false;
            if (cur.GetComponent<LODGroup>() != null) return false; // let LOD handle these
            cur = cur.parent;
        }
        return true;
    }

    // Whole-cluster skip for the all-clusters pass: generated planet geometry,
    // celestial extras, and concert stages (their light/laser meshes move via
    // script, so the static combine would wrongly weld them in place).
    static bool IsProtectedCluster(Transform t)
    {
        string n = t.name;
        if (n == CombinedRootName) return true;
        // Animated entity at the cluster ROOT (an NPC): its static sub-parts may
        // be bone-attached, and combining would freeze them at the root instead of
        // following the animation. Skip the whole cluster. (NPCs nested INSIDE a
        // static cluster like a market are still handled per-mesh by IsCombinable,
        // since their Animator is an intermediate parent, not the cluster root —
        // so the market's static stalls still combine.)
        if (t.GetComponent<Animator>() != null) return true;
        if (t.GetComponent<SkinnedMeshRenderer>() != null) return true;
        // A Rigidbody at the cluster root means it's a movable/physics entity
        // (a ship, vehicle, dropped prop) — baking its meshes into a combined,
        // body-parented mesh would freeze them and detach added parts. Skip it.
        if (t.GetComponent<Rigidbody>() != null) return true;
        string[] skip = { "Mesh Holder", "Terrain", "Atmosphere", "Ocean", "Water",
                          "Sun", "Star", "STAGE", "Concert", "Cloud", "Light",
                          "Alien", "NPC", "Toy", "Ship", "ship", "Space", "Reactor" };
        for (int i = 0; i < skip.Length; i++)
            if (n.Contains(skip[i])) return true;
        return false;
    }

    static void FindCombinedRoots(Transform t, List<Transform> outList)
    {
        if (t.name == CombinedRootName) { outList.Add(t); return; } // its children are outputs, don't recurse
        for (int i = 0; i < t.childCount; i++)
            FindCombinedRoots(t.GetChild(i), outList);
    }

    static void MarkDirty(GameObject go)
    {
        if (go != null && !Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
    }
}
#endif
