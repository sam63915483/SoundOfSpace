using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the two drag-and-drop vendor prefabs for the planet economy
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 1).
///
/// Sam places roughly ten of these by hand, so the goal is ZERO per-instance
/// configuration: drop the prefab under a planet, snap it to the ground, done.
/// Everything that used to be dragged in per vendor is now either resolved from
/// the hierarchy (<see cref="VendorSite"/> finds the planet) or found at runtime
/// (<c>FishMarketNPC.ResolveSharedHUD</c> finds the shared HUD canvas, exactly
/// as <c>Alien7Vendor</c> already did).
///
/// The prefabs are built FROM THE LIVE SCENE VENDORS rather than authored from
/// scratch, so a new market on a dwarf planet behaves identically to the one on
/// Humble Abode — same greeting, same audio clips, same bounty lines, same
/// collider size. Change the Humble Abode vendor and re-run this to propagate.
///
/// ⚠ The prefab uses the ORIGINAL market-stand prefab, not the scene instance.
/// The Humble Abode stand has been through MeshCombineTool, whose combined
/// meshes live inside the scene file rather than as assets — a prefab built from
/// it would reference meshes that do not exist outside that scene.
/// </summary>
public static class VendorPrefabBuilder
{
    const string MainScenePath = "Assets/1.6.7.7.7.unity";
    const string OutDir        = "Assets/1 - samsPrefabs/Vendors";

    const string FishStandPrefab  = "Assets/5 - External Imports/Village & Buildings/Low-Poly Medieval Market/Prefabs/Fish_market_with_sections.prefab";
    const string GoodsStandPrefab = "Assets/5 - External Imports/Village & Buildings/Low-Poly Medieval Market/Prefabs/BakeryMarket_no_dop.prefab";

    const string FishPrefabPath  = OutDir + "/FishMarket.prefab";
    const string GoodsPrefabPath = OutDir + "/GoodsVendor.prefab";

    [MenuItem("Tools/Vendors/Build Vendor Prefabs")]
    public static void Build()
    {
        var (scene, opened) = GetMainScene();
        if (!scene.IsValid()) return;

        var log = new StringBuilder("[Vendors] ");
        try
        {
            EnsureFolder(OutDir);

            var fishNpc  = FindInScene<FishMarketNPC>(scene);
            var goodsNpc = FindInScene<Alien7Vendor>(scene);

            if (fishNpc == null)
                Debug.LogError("[Vendors] No FishMarketNPC in " + MainScenePath +
                               " to copy settings from — cannot build FishMarket.prefab.");
            else
                log.Append(BuildOne(fishNpc.gameObject, FishStandPrefab, FishPrefabPath,
                                    VendorSite.VendorKind.FishMarket, "FISH MARKET")).Append("  ");

            if (goodsNpc == null)
                Debug.LogWarning("[Vendors] No Alien7Vendor in the scene — skipping GoodsVendor.prefab.");
            else
                log.Append(BuildOne(goodsNpc.gameObject, GoodsStandPrefab, GoodsPrefabPath,
                                    VendorSite.VendorKind.GoodsVendor, "GENERAL GOODS"));

            // Tag the existing Humble Abode stands so they join the economy table
            // alongside the new prefab instances. The scene stand itself is left
            // alone — it is mesh-combined into the village and re-creating it
            // would cost the combine for no gameplay gain.
            int tagged = 0;
            if (fishNpc  != null) tagged += TagExistingSite(fishNpc.gameObject,  VendorSite.VendorKind.FishMarket)  ? 1 : 0;
            if (goodsNpc != null) tagged += TagExistingSite(goodsNpc.gameObject, VendorSite.VendorKind.GoodsVendor) ? 1 : 0;
            if (tagged > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                log.Append("\n[Vendors] Tagged ").Append(tagged)
                   .Append(" existing Humble Abode stand(s) with VendorSite — SAVE THE SCENE to keep that.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(log.ToString());
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Assemble one vendor prefab: a clean stand, the vendor alien parented to it
    /// at the same local pose it has in the scene, every gameplay component
    /// copied verbatim, plus a VendorSite on the root and a readable board.
    /// </summary>
    static string BuildOne(GameObject sceneNpc, string standPrefabPath, string outPath,
                           VendorSite.VendorKind kind, string boardHeading)
    {
        var standSrc = AssetDatabase.LoadAssetAtPath<GameObject>(standPrefabPath);
        if (standSrc == null) return $"MISSING stand prefab {standPrefabPath}";

        var alienSrc = ResolveSourcePrefab(sceneNpc);
        if (alienSrc == null) return $"could not resolve the source prefab behind '{sceneNpc.name}'";

        // Fresh stand from the untouched pack prefab (no combined meshes).
        var root = (GameObject)PrefabUtility.InstantiatePrefab(standSrc);
        root.name = kind == VendorSite.VendorKind.FishMarket ? "FishMarket" : "GoodsVendor";

        try
        {
            // Board FIRST, while the stand is the only thing here: the vendor
            // alien is scaled 3x and would drag the measured height up over its
            // head, hanging the sign in mid air above the stall.
            AddBoard(root, boardHeading);

            // Vendor alien, posed exactly as the working one is posed.
            var alien = (GameObject)PrefabUtility.InstantiatePrefab(alienSrc);
            alien.name = sceneNpc.name;
            alien.transform.SetParent(root.transform, false);
            alien.transform.localPosition = sceneNpc.transform.localPosition;
            alien.transform.localRotation = sceneNpc.transform.localRotation;
            alien.transform.localScale    = sceneNpc.transform.localScale;

            // Copy every gameplay component off the live vendor. CopySerialized
            // brings the audio clips, greeting, bounty lines and collider size
            // across; its references to SCENE objects (the HUD canvas) are
            // dropped when the prefab is saved, which is exactly right —
            // ResolveSharedHUD re-finds them at runtime.
            CopyGameplayComponents(sceneNpc, alien);

            // Root marker: this is what tells the vendor which planet it is on.
            var site = root.GetComponent<VendorSite>();
            if (site == null) site = root.AddComponent<VendorSite>();
            site.kind = kind;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, outPath, out bool ok);
            return ok && saved != null ? $"built {outPath}" : $"FAILED to save {outPath}";
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>Copy the vendor's own components onto the fresh alien, skipping the
    /// ones the prefab already supplies (transform, renderers, animator).</summary>
    static void CopyGameplayComponents(GameObject from, GameObject to)
    {
        var comps = from.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;                       // missing script
            if (c is Transform) continue;
            if (c is Renderer || c is MeshFilter) continue;
            if (c is Animator) continue;

            var type = c.GetType();
            var dst  = to.GetComponent(type);
            if (dst == null) dst = to.AddComponent(type);
            if (dst != null) EditorUtility.CopySerialized(c, dst);
        }
    }

    /// <summary>Add the look-at board to the stand, positioned above the counter.
    /// Sam moves it; the prefab only needs it to exist somewhere sane.</summary>
    static void AddBoard(GameObject root, string heading)
    {
        var existing = root.transform.Find("Board");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject("Board");
        go.transform.SetParent(root.transform, false);

        // Sit it just above the STALL, measured from the single biggest renderer
        // rather than the whole stand: these market props include a fishing rod
        // stood on end, and measuring everything hangs the sign in the air above
        // it. Sam nudges from here; the prefab only owes him a sane start.
        var b = StallBounds(root);
        go.transform.localPosition = new Vector3(b.center.x, b.max.y + 0.45f, b.center.z);
        go.transform.localRotation = Quaternion.identity;

        var board = go.AddComponent<VendorBoard>();
        board.heading = heading;
    }

    /// <summary>Local-space bounds of the stall itself — the single largest
    /// renderer in the stand. Props (fish, knives, a rod stood on end) are all
    /// small next to the structure, so "biggest" reliably picks the building.</summary>
    static Bounds StallBounds(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 2f);

        Renderer best = null;
        float bestVol = -1f;
        for (int i = 0; i < rends.Length; i++)
        {
            var s = rends[i].bounds.size;
            float vol = s.x * s.y * s.z;
            if (vol > bestVol) { bestVol = vol; best = rends[i]; }
        }

        var inv = root.transform.worldToLocalMatrix;
        var wb  = best.bounds;
        var b   = new Bounds(inv.MultiplyPoint3x4(wb.center), Vector3.zero);
        b.Encapsulate(inv.MultiplyPoint3x4(wb.min));
        b.Encapsulate(inv.MultiplyPoint3x4(wb.max));
        return b;
    }

    /// <summary>Give an existing hand-built scene vendor the VendorSite marker so it
    /// takes part in the economy exactly like a prefab instance. Returns true if
    /// something was added.</summary>
    static bool TagExistingSite(GameObject npc, VendorSite.VendorKind kind)
    {
        if (npc.GetComponentInParent<VendorSite>() != null) return false;

        // The stand is the vendor's parent; fall back to the vendor itself if it
        // was placed loose under the planet.
        var host = npc.transform.parent != null &&
                   npc.transform.parent.GetComponent<CelestialBody>() == null
                 ? npc.transform.parent.gameObject
                 : npc;

        var site = host.AddComponent<VendorSite>();
        site.kind = kind;
        EditorUtility.SetDirty(host);
        return true;
    }

    static GameObject ResolveSourcePrefab(GameObject sceneObj)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(sceneObj);
        if (src != null)
        {
            var path = AssetDatabase.GetAssetPath(src);
            if (!string.IsNullOrEmpty(path))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    // ── Scene / asset helpers (mirrors DwarfPlanetInstaller) ───────────────────

    static (Scene scene, bool opened) GetMainScene()
    {
        var s = SceneManager.GetSceneByPath(MainScenePath);
        if (s.IsValid() && s.isLoaded) return (s, false);
        s = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
        if (!s.IsValid()) Debug.LogError("[Vendors] could not open " + MainScenePath);
        return (s, true);
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var hit = root.GetComponentInChildren<T>(true);
            if (hit != null) return hit;
        }
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
