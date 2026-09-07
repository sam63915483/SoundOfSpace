using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the fuel system into the shuttle
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 2): the tank, and a real
/// reactor — the SHIP44 one, mesh, glow and all — that you walk up to holding
/// crystals and press F on.
///
/// <b>Sam's workflow (2026-09-07), which is why this is split in two:</b>
/// the reactor is added to the shuttle IN THE SCENE, not to the prefab, so Sam
/// can drag it exactly where he wants it inside the cabin. Only when he says he
/// is happy does <see cref="ApplyToPrefab"/> push it into
/// <c>Shuttle_Lander.prefab</c>. Doing it the other way round — prefab first —
/// would mean the prefab could later overwrite the position he chose.
///
/// The reactor is a straight duplicate of the manual ship's, so it keeps
/// everything: the <c>Retro_laboratory_Reactor_Core_Tube</c> mesh, both
/// materials, the solid collider you bump into, the larger trigger that notices
/// you, <c>ShipReactor</c> and <c>ReactorGlow</c> — the breathing blue emission,
/// the red out-of-control flickers, the point light and the refuel ping flash.
/// Its <c>ship</c> reference is cleared so both scripts fall through to the
/// shuttle's <c>ShuttleFuel</c> tank instead of a Ship it does not have.
///
/// Menu: <b>Tools ▸ Shuttle ▸ 1. Add Reactor To Scene Shuttle</b>, then, once
/// placed, <b>Tools ▸ Shuttle ▸ 2. Save Reactor Into Prefab</b>.
/// </summary>
public static class ShuttleFuelInstaller
{
    const string ShuttlePrefab = "Assets/1 - samsPrefabs/Shuttle_Lander.prefab";
    const string ShipPrefab    = "Assets/1 - samsPrefabs/SHIP44.prefab";
    const string ReactorName   = "Reactor";
    const string CabinAnchor   = "LightProbe (cabin centre)";

    // ── Step 1: put a real reactor in the scene, for Sam to place ─────────────

    [MenuItem("Tools/Shuttle/1. Add Reactor To Scene Shuttle")]
    public static void AddToScene()
    {
        var shuttle = FindSceneShuttle();
        if (shuttle == null)
        {
            Debug.LogError("[ShuttleFuel] No Shuttle_Lander in the open scene. " +
                           "Open Assets/1.6.7.7.7.unity first.");
            return;
        }

        // The tank lives on the prefab (it has no position, so nothing to place).
        EnsureTankOnPrefab();

        var existing = FindDeep(shuttle.transform, ReactorName);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log($"[ShuttleFuel] The shuttle already has a '{ReactorName}' at local " +
                      $"{existing.localPosition}. Selected and pinged it — move it where you want, " +
                      "then run Tools ▸ Shuttle ▸ 2. Save Reactor Into Prefab.");
            return;
        }

        var src = LoadShipReactorSource();
        if (src == null)
        {
            Debug.LogError("[ShuttleFuel] Could not find the reactor on " + ShipPrefab);
            return;
        }

        // Straight duplicate — mesh, both materials, both colliders, ShipReactor,
        // ReactorGlow, and every tuning value Sam has already dialled in on the ship.
        var go = Object.Instantiate(src, shuttle.transform);
        go.name = ReactorName;
        go.transform.localPosition = ProposeSpot(shuttle);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = src.transform.localScale;

        RetargetToShuttle(go);

        Undo.RegisterCreatedObjectUndo(go, "Add Shuttle Reactor");
        EditorSceneManager.MarkSceneDirty(shuttle.scene);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        SceneView.FrameLastActiveSceneView();

        var glow = go.GetComponent<ReactorGlow>();
        Debug.Log(
            $"[ShuttleFuel] Reactor added to the SCENE shuttle at local {go.transform.localPosition}.\n" +
            $"  It is selected and framed in the Scene view — MOVE IT where you want it.\n" +
            $"  Kept from the ship: mesh + both materials, solid collider, the bigger trigger " +
            $"that notices you, ShipReactor (F to insert) and ReactorGlow " +
            $"({(glow != null ? "present" : "MISSING")}) — breathing blue, red flickers, point " +
            $"light, refuel ping.\n" +
            $"  When you are happy: Tools ▸ Shuttle ▸ 2. Save Reactor Into Prefab.");
    }

    // ── Step 2: once Sam is happy, bake it into the prefab ────────────────────

    [MenuItem("Tools/Shuttle/2. Save Reactor Into Prefab")]
    public static void ApplyToPrefab()
    {
        var shuttle = FindSceneShuttle();
        if (shuttle == null) { Debug.LogError("[ShuttleFuel] No Shuttle_Lander in the open scene."); return; }

        var reactor = FindDeep(shuttle.transform, ReactorName);
        if (reactor == null)
        {
            Debug.LogError("[ShuttleFuel] No 'Reactor' under the scene shuttle — run step 1 first.");
            return;
        }

        Vector3 pos = reactor.localPosition;
        Quaternion rot = reactor.localRotation;
        Vector3 scale = reactor.localScale;

        // Patch the hand-maintained prefab in place. Shuttle_Lander is never
        // regenerated — see the prefab's own note and CLAUDE.md.
        var root = PrefabUtility.LoadPrefabContents(ShuttlePrefab);
        if (root == null) { Debug.LogError("[ShuttleFuel] could not open " + ShuttlePrefab); return; }
        try
        {
            if (root.GetComponent<ShuttleFuel>() == null) root.AddComponent<ShuttleFuel>();

            var old = FindDeep(root.transform, ReactorName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var src = LoadShipReactorSource();
            if (src == null) { Debug.LogError("[ShuttleFuel] reactor source missing"); return; }

            var copy = Object.Instantiate(src, root.transform);
            copy.name = ReactorName;
            copy.transform.localPosition = pos;
            copy.transform.localRotation = rot;
            copy.transform.localScale    = scale;
            RetargetToShuttle(copy);

            PrefabUtility.SaveAsPrefabAsset(root, ShuttlePrefab);
            AssetDatabase.SaveAssets();

            // The scene now has TWO: the loose object Sam positioned (an added-
            // object override) and the one the prefab has just started supplying,
            // stacked at the same pose. Drop the override and keep the
            // prefab-driven one, which leaves a clean instance with no overrides.
            int dropped = RemoveAddedReactorOverride(shuttle);

            Debug.Log($"[ShuttleFuel] Saved the reactor into {ShuttlePrefab} at local {pos}, " +
                      $"rotation {rot.eulerAngles}, scale {scale}.\n" +
                      $"  Removed {dropped} duplicate scene copy (the prefab supplies it now), " +
                      $"leaving {CountReactors(shuttle)} in the shuttle.\n" +
                      "  Prefab and scene agree — nothing will overwrite your placement. Save the scene.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Delete the scene-added reactor left over once the prefab supplies
    /// its own, so the shuttle does not end up with two stacked on each other.
    /// Only ever removes an ADDED override — never the prefab's own copy.</summary>
    static int RemoveAddedReactorOverride(GameObject shuttle)
    {
        var all = shuttle.GetComponentsInChildren<ShipReactor>(true);
        if (all.Length <= 1) return 0;
        for (int i = 0; i < all.Length; i++)
        {
            var go = all[i].gameObject;
            if (!PrefabUtility.IsAddedGameObjectOverride(go)) continue;
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(shuttle.scene);
            return 1;
        }
        Debug.LogWarning("[ShuttleFuel] The shuttle has more than one reactor but none is an " +
                         "added override — leaving them alone rather than guessing. Check the Hierarchy.");
        return 0;
    }

    static int CountReactors(GameObject shuttle) =>
        shuttle.GetComponentsInChildren<ShipReactor>(true).Length;

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Clear the Ship links so both reactor scripts fall through to the
    /// shuttle's own tank. Leaving them set would point the shuttle's gauge and
    /// glow at the manual ship's fuel.</summary>
    static void RetargetToShuttle(GameObject go)
    {
        var r = go.GetComponent<ShipReactor>();
        if (r != null) { r.ship = null; r.glow = go.GetComponent<ReactorGlow>(); }
        var g = go.GetComponent<ReactorGlow>();
        if (g != null) g.ship = null;
    }

    static GameObject LoadShipReactorSource()
    {
        var ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefab);
        if (ship == null) return null;
        var r = ship.GetComponentInChildren<ShipReactor>(true);
        return r != null ? r.gameObject : null;
    }

    /// <summary>The Shuttle_Lander instance in whichever scene is open.</summary>
    static GameObject FindSceneShuttle()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var sc = SceneManager.GetSceneAt(i);
            if (!sc.isLoaded) continue;
            foreach (var root in sc.GetRootGameObjects())
            {
                var hit = FindDeep(root.transform, "Shuttle_Lander");
                if (hit != null) return hit.gameObject;
            }
        }
        return null;
    }

    static void EnsureTankOnPrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(ShuttlePrefab);
        if (root == null) return;
        try
        {
            if (root.GetComponent<ShuttleFuel>() != null) return;
            root.AddComponent<ShuttleFuel>();
            PrefabUtility.SaveAsPrefabAsset(root, ShuttlePrefab);
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>A sane first home: beside the cabin-centre marker at floor height.
    /// Sam moves it; this only has to not be inside a wall.</summary>
    static Vector3 ProposeSpot(GameObject shuttle)
    {
        var anchor = FindDeep(shuttle.transform, CabinAnchor);
        if (anchor == null) return new Vector3(0f, 1f, 0f);
        var p = shuttle.transform.InverseTransformPoint(anchor.position);
        return new Vector3(p.x + 1.2f, p.y - 1.6f, p.z);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
