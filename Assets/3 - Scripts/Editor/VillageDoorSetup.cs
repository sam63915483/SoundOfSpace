#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup for the swinging village doors. Run it once; it is
/// idempotent, so running it again is harmless.
///
/// ── What it is fixing ────────────────────────────────────────────────────
/// The LowPolyFantasyVillage house prefabs each contain a DoorPart_01 /
/// DoorPart_02 leaf that is already hinged correctly (pivot on the hinge edge),
/// and the house bodies have real doorway holes and non-convex colliders. The
/// only thing standing between that and a door you can walk through is
/// MeshCombineTool: it baked the leaves into TOWN-VILLAGE's __CombinedMeshes and
/// disabled their own MeshRenderers, so a door would swing its collider open
/// while a welded copy of itself stayed rendered shut in the doorway.
///
/// So this command does two things, IN THIS ORDER — and the order is
/// load-bearing, which is the whole reason it is one command and not two:
///
///   1. STAMP the shared DoorPart prefabs with VillageDoor + a trigger zone.
///      Every already-placed house inherits the component (none of them removes
///      components), and so does any house dropped in later.
///   2. UN-BAKE, by reverting and re-combining only the clusters that contain a
///      door. MeshCombineTool now skips doors, so the re-bake leaves them out.
///      Step 1 must come first or that skip rule has nothing to match.
///
/// Only clusters containing doors are touched. The other combined clusters on
/// the planet are left exactly as they are.
/// </summary>
public static class VillageDoorSetup
{
    const string CombinedRootName = "__CombinedMeshes";

    /// Reach of the "you can press F here" zone, in the door leaf's own local
    /// units. The houses are placed at scale 1.3, so this lands near 2.6 m.
    const float TriggerRadius = 2.0f;

    [MenuItem("Tools/Optimize/Un-bake Village Doors")]
    static void Run()
    {
        var report = new StringBuilder();

        int stamped = StampDoorPrefabs(report);
        int clusters = UnbakeDoorClusters(report);

        if (stamped == 0 && clusters == 0)
        {
            EditorUtility.DisplayDialog("Village Doors",
                "Nothing to do — the door prefabs are already stamped and no baked doors were found in the open scene.\n\n" +
                "If a door still won't open, check that the house is a prefab instance and that its DoorPart child exists.",
                "OK");
            Debug.Log("[VillageDoorSetup] Nothing to do.\n" + report);
            return;
        }

        Debug.Log($"[VillageDoorSetup] Stamped {stamped} door prefab(s); re-baked {clusters} cluster(s).\n{report}");
        EditorUtility.DisplayDialog("Village Doors",
            $"Stamped {stamped} door prefab(s).\nRe-baked {clusters} mesh cluster(s).\n\n" +
            "Save the scene, then walk up to a village house, aim at the door and press F.\n\n" +
            "Full details in the Console.", "OK");
    }

    // ── 1. stamp the shared door prefabs ─────────────────────────────────

    /// Patch every DoorPart prefab in the project via LoadPrefabContents — the
    /// repo's safe prefab-patch route. Never regenerate a prefab; a regen once
    /// clobbered 139 hand-made overrides on the shuttle.
    static int StampDoorPrefabs(StringBuilder report)
    {
        int changed = 0;

        foreach (var guid in AssetDatabase.FindAssets("DoorPart t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!file.StartsWith("DoorPart")) continue;   // FindAssets matches loosely

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool dirty = false;

                if (root.GetComponent<VillageDoor>() == null)
                {
                    root.AddComponent<VillageDoor>();
                    dirty = true;
                    report.AppendLine($"  • {file}: added VillageDoor");
                }

                if (!HasTrigger(root))
                {
                    var sc = root.AddComponent<SphereCollider>();
                    sc.isTrigger = true;
                    sc.radius = TriggerRadius;
                    sc.center = LeafCentre(root);
                    dirty = true;
                    report.AppendLine($"  • {file}: added interaction trigger (r={TriggerRadius})");
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                }
                else report.AppendLine($"  • {file}: already set up");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        if (changed > 0) AssetDatabase.SaveAssets();
        return changed;
    }

    static bool HasTrigger(GameObject go)
    {
        foreach (var c in go.GetComponents<Collider>())
            if (c.isTrigger) return true;
        return false;
    }

    /// Middle of the door leaf, so the trigger sphere is centred on the door
    /// rather than on its hinge edge (the pivot sits at the hinge, which would
    /// otherwise push half the zone inside the wall).
    static Vector3 LeafCentre(GameObject go)
    {
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) return mf.sharedMesh.bounds.center;
        return Vector3.zero;
    }

    // ── 2. un-bake the clusters that contain doors ───────────────────────

    static int UnbakeDoorClusters(StringBuilder report)
    {
        var clusters = new List<GameObject>();

        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsDoorLeaf(t)) continue;
                var cluster = NearestCombinedCluster(t);
                if (cluster != null && !clusters.Contains(cluster)) clusters.Add(cluster);
            }
        }

        if (clusters.Count == 0)
        {
            report.AppendLine("  • no baked doors in the open scene — nothing to un-bake");
            return 0;
        }

        int done = 0;
        foreach (var cluster in clusters)
        {
            int reverted = MeshCombineTool.RevertUnder(cluster);
            int draws = MeshCombineTool.RecombineOne(cluster);
            if (draws < 0)
            {
                report.AppendLine(
                    $"  • {cluster.name}: reverted {reverted} cluster(s) but the re-combine was refused " +
                    "(nothing eligible, or a mesh needs Read/Write Enabled — see the Console). " +
                    "The scene is CORRECT but un-optimised; fix the mesh and run Tools ▸ Optimize ▸ Combine.");
            }
            else
            {
                report.AppendLine($"  • {cluster.name}: re-baked to {draws} draw call(s), doors excluded");
            }
            done++;
        }

        // Whatever happened above, the doors themselves must now be visible in
        // their own right. Saying so beats discovering it in play mode.
        int stillHidden = 0;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (IsDoorLeaf(t))
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null && !mr.enabled) stillHidden++;
                }

        report.AppendLine(stillHidden == 0
            ? "  • all door renderers are live"
            : $"  • ⚠ {stillHidden} door renderer(s) are STILL disabled — those doors will swing invisibly");

        return done;
    }

    static bool IsDoorLeaf(Transform t)
    {
        // Component first; the name is the fallback for a scene whose instances
        // haven't picked up the stamped component yet.
        return t.GetComponent<VillageDoor>() != null || t.name.StartsWith("DoorPart");
    }

    /// Walk up from a door to the FIRST ancestor holding a __CombinedMeshes
    /// child. That is the cluster whose bake ate this door — TOWN-VILLAGE for
    /// the Humble Abode village. Deliberately the nearest one, so the blast
    /// radius is one cluster and not the whole planet.
    static GameObject NearestCombinedCluster(Transform t)
    {
        for (var p = t.parent; p != null; p = p.parent)
            if (p.Find(CombinedRootName) != null) return p.gameObject;
        return null;
    }
}
#endif
