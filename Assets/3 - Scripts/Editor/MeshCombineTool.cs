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
///   • Swinging doors (a VillageDoor component, or a "DoorPart*" name) — baking
///     one leaves a welded copy in the doorway while the real leaf opens
///     invisibly. Run Tools ▸ Optimize ▸ Un-bake Village Doors on a scene that
///     was combined before this rule existed.
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
///   • Tools ▸ Optimize ▸ Re-bake Clusters With Lost Shadow Settings — repairs
///     clusters baked before shadow modes were preserved (see below).
///
/// ⚠️ SHADOW MODE IS LOAD-BEARING, NOT COSMETIC. Combined output keeps each
/// source's shadowCastingMode, because Built-in RP builds _CameraDepthTexture
/// from the ShadowCaster pass of renderers at queue <= 2500 and SKIPS any with
/// Cast Shadows = Off — and the atmosphere/ocean are [ImageEffectOpaque] posts
/// that read that texture. Forcing every combined mesh to cast shadows once
/// broke the village windows: their panes are authored Cast Shadows = Off
/// exactly so their queue-2450 glass stays out of the depth texture, and with it
/// forced on each pane wrote depth a centimetre from the camera, so looking out
/// a window showed a world with no atmosphere and no ocean.
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

    [MenuItem("Tools/Optimize/Re-bake Clusters With Lost Shadow Settings")]
    static void RebakeLostShadowSettings()
    {
        // Any cluster baked before the shadow-mode fix has a combined output
        // forced to Cast Shadows = On, regardless of what its sources said. The
        // sources themselves are untouched (the bake only DISABLES them), so
        // their authored modes are still on disk and the damage is detectable:
        // a disabled source asking for anything other than On was overridden.
        var victims = new List<GameObject>();
        var why = new StringBuilder();

        foreach (var rootGo in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (var t in rootGo.GetComponentsInChildren<Transform>(true))
        {
            if (t.Find(CombinedRootName) == null) continue;               // not a cluster root
            var combined = t.Find(CombinedRootName);

            int lost = 0;
            foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.transform.IsChildOf(combined)) continue;           // the output, not a source
                if (mr.enabled) continue;                                 // not baked away
                if (mr.shadowCastingMode != ShadowCastingMode.On) lost++;
            }
            if (lost > 0)
            {
                victims.Add(t.gameObject);
                why.AppendLine($"  • {t.name}: {lost} source renderer(s) whose shadow mode was overridden");
            }
        }

        if (victims.Count == 0)
        {
            EditorUtility.DisplayDialog("Re-bake",
                "No combined cluster is carrying overridden shadow settings. Nothing to do.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Re-bake clusters",
                $"{victims.Count} cluster(s) were baked with their shadow settings overridden:\n\n{why}\n" +
                "Re-baking restores the authored modes. This is the fix for glass that kills the " +
                "atmosphere/ocean when you look through it.\n\nRe-bake them now?", "Re-bake", "Cancel"))
            return;

        var report = new StringBuilder();
        foreach (var v in victims)
        {
            int reverted = MeshCombineTool.RevertUnder(v);
            int draws = MeshCombineTool.RecombineOne(v);
            report.AppendLine(draws < 0
                ? $"  • {v.name}: reverted {reverted}, re-combine REFUSED (nothing eligible / mesh not Read-Write) — scene is correct but un-optimised"
                : $"  • {v.name}: re-baked to {draws} draw call(s), shadow modes preserved");
        }
        Debug.Log($"[MeshCombineTool] Re-baked {victims.Count} cluster(s) with lost shadow settings.\n{report}");
        EditorUtility.DisplayDialog("Re-bake",
            $"Re-baked {victims.Count} cluster(s). Save the scene.\n\nDetails in the Console.", "OK");
    }

    [MenuItem("Tools/Optimize/Revert Combined Meshes Under Selection")]
    static void RevertUnderSelection()
    {
        var root = Selection.activeGameObject;
        if (root == null) return;

        int n = RevertUnder(root);
        if (n == 0)
        {
            EditorUtility.DisplayDialog("Revert", $"No combined meshes found under '{root.name}'.", "OK");
            return;
        }
        Debug.Log($"[MeshCombineTool] Reverted {n} cluster(s) under '{root.name}': originals re-enabled, combined meshes removed.");
    }

    /// <summary>Revert every combined cluster under <paramref name="root"/>:
    /// re-enable the originals and delete the combined output. Returns how many
    /// clusters were undone (0 = nothing was combined here). Extracted from the
    /// menu command so the door un-baker can reuse it.</summary>
    internal static int RevertUnder(GameObject root)
    {
        if (root == null) return 0;

        var combinedRoots = new List<Transform>();
        FindCombinedRoots(root.transform, combinedRoots);
        if (combinedRoots.Count == 0) return 0;

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
        return combinedRoots.Count;
    }

    /// <summary>Re-combine one already-reverted cluster root, honouring the
    /// current skip rules. Returns the resulting draw-call count, or -1 if the
    /// cluster had nothing eligible (or a mesh that isn't Read/Write enabled).
    /// Used by the door un-baker, which must revert and re-bake a single cluster
    /// without disturbing the other nineteen on the planet.</summary>
    internal static int RecombineOne(GameObject root)
    {
        if (root == null) return -1;
        if (root.transform.Find(CombinedRootName) != null) return -1;   // still combined

        var rends = new List<MeshRenderer>();
        var unreadable = new HashSet<Mesh>();
        CollectEligible(root.transform, root.transform, rends, unreadable);
        if (rends.Count == 0 || unreadable.Count > 0) return -1;

        Undo.SetCurrentGroupName("Combine Static Meshes");
        int group = Undo.GetCurrentGroup();
        int draws = CombineOneRoot(root, rends);
        Undo.CollapseUndoOperations(group);
        MarkDirty(root);
        return draws;
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

    // Combine one cluster's eligible renderers, grouped by material AND shadow
    // mode, into meshes parented under a __CombinedMeshes child. Returns the
    // draw-call count (= the number of distinct material/shadow-mode groups).
    static int CombineOneRoot(GameObject root, List<MeshRenderer> eligible)
    {
        // Keyed by material AND shadow-casting mode.
        //
        // ⚠️ SHADOW MODE IS NOT COSMETIC — it decides what lands in
        // _CameraDepthTexture. Built-in RP builds that texture from the
        // ShadowCaster pass of renderers at queue <= 2500, skipping any renderer
        // with Cast Shadows = Off. The atmosphere and ocean are
        // [ImageEffectOpaque] post-processes that read it.
        //
        // This used to force ShadowCastingMode.On on every combined output. That
        // ate the village window glass: the pack authors those panes Cast
        // Shadows = Off precisely so their queue-2450 material stays OUT of the
        // depth texture. Forced on, each pane wrote depth about a centimetre
        // from the camera, so the atmosphere and ocean computed ~zero thickness
        // for every window pixel and you looked out at a bare, un-atmosphered,
        // un-oceaned world. (The shuttle's windows escaped it by sitting at
        // queue 3000 and by the ship being excluded from the combine.)
        //
        // Grouping by the mode instead of overriding it keeps the authored
        // intent. Panes and walls already differ by material, so in practice
        // this costs no extra draw calls.
        var byGroup = new Dictionary<(Material mat, ShadowCastingMode shadows), List<CombineInstance>>();
        var layerByGroup = new Dictionary<(Material, ShadowCastingMode), int>();
        var receiveByGroup = new Dictionary<(Material, ShadowCastingMode), bool>();
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
                var key = (mat, mr.shadowCastingMode);
                if (!byGroup.TryGetValue(key, out var list))
                {
                    list = new List<CombineInstance>();
                    byGroup[key] = list;
                    layerByGroup[key] = mr.gameObject.layer;
                    receiveByGroup[key] = mr.receiveShadows;
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
        foreach (var kv in byGroup)
        {
            var mat = kv.Key.mat;
            var instances = kv.Value;

            var mesh = new Mesh { name = $"Combined_{root.name}_{matIndex}", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(instances.ToArray(), mergeSubMeshes: true, useMatrices: true);
            mesh.RecalculateBounds();

            var go = new GameObject($"Combined_{(mat != null ? mat.name : "mat")}_{matIndex}");
            Undo.RegisterCreatedObjectUndo(go, "Combine Static Meshes");
            go.transform.SetParent(combinedRoot.transform, false);
            go.layer = layerByGroup[kv.Key];
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = kv.Key.shadows;      // authored, never overridden
            rend.receiveShadows = receiveByGroup[kv.Key];
            matIndex++;
        }

        // Non-destructive: hide the originals' rendering only.
        foreach (var mr in eligible)
        {
            Undo.RecordObject(mr, "Combine Static Meshes");
            mr.enabled = false;
            EditorUtility.SetDirty(mr);
        }

        return byGroup.Count;
    }

    // ── Collection / filtering ────────────────────────────────────────────────

    static void CollectEligible(Transform t, Transform root, List<MeshRenderer> outList, HashSet<Mesh> unreadable)
    {
        if (t.name == CombinedRootName) return;          // never recombine our own output
        if (t.name.Contains("_Placed")) return;          // save-tracked player builds — skip subtree
        // Doors that swing. Baking one welds a copy of it into the combined mesh
        // and disables its own renderer, so the leaf opens its COLLIDER while a
        // ghost door stays rendered shut in the doorway. Returning (not just
        // skipping this renderer) drops the whole subtree, keeping the handles
        // with their leaf. The name check catches doors not yet stamped with the
        // component — see VillageDoorSetup.
        if (t.GetComponent<VillageDoor>() != null) return;
        if (t.name.StartsWith("DoorPart")) return;
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
