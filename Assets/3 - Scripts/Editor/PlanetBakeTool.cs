#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes a celestial body's RUNTIME-GENERATED terrain (the Sebastian-Lague procedural
/// mesh) into static assets: a Mesh asset, a Material asset, and a Prefab carrying
/// MeshFilter + MeshRenderer + MeshCollider on that one mesh.
///
/// WHY: a fixed hub planet (Humble Abode) gains nothing from regenerating every load —
/// but the runtime generation causes a class of bugs (things raycast-placed at startup
/// land on the crude BodyPlaceholder sphere before the real terrain+collider exists →
/// they float; the shader's grass layer can reset; it isn't editor-visible; it costs
/// frames). A baked prefab has its mesh AND collider present from scene load, so
/// placement always hits the real surface.
///
/// SAFE: this only READS the generated output (mesh + the material's already-set
/// scalar/colour properties — EarthShading sets NO runtime textures) and writes new
/// assets. It does NOT modify any generation/shading code (trap #2) and does NOT touch
/// the scene or the generator. Reverting = delete the baked assets.
///
/// USAGE (two steps, because edit-mode generation runs on the next editor tick):
///   1. Tools ▸ Bake Planet ▸ 1. Generate Preview For Selected
///      (or select the body and use Tools ▸ Planet Preview ▸ Show Selected Body)
///   2. Tools ▸ Bake Planet ▸ 2. Bake Selected To Prefab
/// </summary>
public static class PlanetBakeTool
{
    const string OutputDir = "Assets/BakedPlanets";

    [MenuItem("Tools/Bake Planet/1. Generate Preview For Selected")]
    static void GeneratePreviewMenu()
    {
        var body = FindSelectedBody();
        if (body == null) { Warn("Select a celestial body (or a child) in the Hierarchy first."); return; }
        TriggerPreview(body.bodyName);
        Debug.Log($"[PlanetBake] Generating preview terrain for '{body.bodyName}'. Wait for it to appear, then run '2. Bake Selected To Prefab'.");
    }

    [MenuItem("Tools/Bake Planet/2. Bake Selected To Prefab")]
    static void BakeMenu()
    {
        var body = FindSelectedBody();
        if (body == null) { Warn("Select a celestial body (or a child) in the Hierarchy first."); return; }
        string msg = Bake(body.bodyName);
        Debug.Log("[PlanetBake] " + msg);
    }

    // ── grass bake ──────────────────────────────────────────────────────────
    // Freezes the InstancedGrassRenderer's blade layout for a body into a static
    // .bytes asset so grass is loaded (not raycast/streamed) at runtime — killing
    // the "floats on respawn" bug at the root. Two-step like the terrain bake:
    // generates the LOD0 preview if it isn't showing, then bakes on the next run.
    [MenuItem("Tools/Bake Planet/Bake Grass (Selected Body)")]
    static void BakeGrassMenu()
    {
        var body = FindSelectedBody();
        if (body == null) { Warn("Select a celestial body (or a child) in the Hierarchy first."); return; }
        Debug.Log("[PlanetBake] " + BakeGrass(body.bodyName));
    }

    public static string BakeGrass(string bodyName, bool triggerIfMissing = true)
    {
        var body = FindBody(bodyName);
        if (body == null) return "body not found: " + bodyName;

        InstancedGrassRenderer renderer = null;
        foreach (var rnd in Object.FindObjectsOfType<InstancedGrassRenderer>(true))
            if (rnd.onlyBodyName == bodyName) { renderer = rnd; break; }
        if (renderer == null)
            return $"No InstancedGrassRenderer with onlyBodyName='{bodyName}' found in the scene.";

        // The previewed LOD0 terrain (same geometry as the runtime collider) must exist.
        var gen = body.GetComponentInChildren<CelestialBodyGenerator>(true);
        var terrainT = gen != null ? gen.transform.Find("Terrain Mesh") : null;
        var mf = terrainT != null ? terrainT.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null)
        {
            if (triggerIfMissing)
            {
                TriggerPreview(bodyName);
                return "LOD0 terrain not generated yet — triggered the preview. Wait a moment, then run 'Bake Grass (Selected Body)' again.";
            }
            return "LOD0 terrain not generated yet — preview still building (do NOT re-trigger; just wait and bake again).";
        }

        // The preview "Terrain Mesh" has no collider; grass seats by raycasting one.
        // Add a temporary MeshCollider (same LOD0 mesh as the runtime collider).
        var mc = terrainT.GetComponent<MeshCollider>();
        bool addedTempCollider = mc == null;
        if (mc == null) mc = terrainT.gameObject.AddComponent<MeshCollider>();
        if (mc.sharedMesh == null) mc.sharedMesh = mf.sharedMesh;

        int blades = renderer.EditorBake(body, mc, out byte[] data);

        if (addedTempCollider) Object.DestroyImmediate(mc);

        if (data == null || blades == 0)
            return "Bake produced no grass — check that the preview terrain looks right.";

        if (!Directory.Exists(OutputDir)) Directory.CreateDirectory(OutputDir);
        string safe = bodyName.Replace(" ", "_");
        string path = $"{OutputDir}/{safe}_Grass.bytes";
        File.WriteAllBytes(path, data);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (ta == null) return $"Wrote {path} but Unity didn't import it as a TextAsset.";

        // Assign the baked asset to the renderer and mark the scene dirty so the
        // reference persists once the user saves.
        var so = new SerializedObject(renderer);
        var prop = so.FindProperty("bakedGrass");
        if (prop != null) { prop.objectReferenceValue = ta; so.ApplyModifiedProperties(); }
        EditorUtility.SetDirty(renderer);
        EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);

        float kb = data.Length / 1024f;
        return $"BAKED grass for '{bodyName}': {blades} blades -> {path} ({kb:F0} KB), " +
               $"assigned to the renderer. SAVE THE SCENE to keep it, then playtest.";
    }

    static CelestialBody FindSelectedBody()
    {
        var go = Selection.activeGameObject;
        return go != null ? go.GetComponentInParent<CelestialBody>() : null;
    }

    static void Warn(string m) => EditorUtility.DisplayDialog("Bake Planet", m, "OK");

    static CelestialBody FindBody(string bodyName)
    {
        foreach (var b in Object.FindObjectsOfType<CelestialBody>(true))
            if (b != null && b.bodyName == bodyName) return b;
        return null;
    }

    // Callable from a script: select the body and run the existing (safe) preview path.
    public static string TriggerPreview(string bodyName)
    {
        var body = FindBody(bodyName);
        if (body == null) return "body not found: " + bodyName;
        Selection.activeGameObject = body.gameObject;
        EditorApplication.ExecuteMenuItem("Tools/Planet Preview/Show Selected Body");
        return "preview triggered for " + bodyName + " (generates on next editor tick)";
    }

    // Callable from a script: bake the (already-generated) preview terrain to assets+prefab.
    public static string Bake(string bodyName)
    {
        var body = FindBody(bodyName);
        if (body == null) return "body not found: " + bodyName;

        var gen = body.GetComponentInChildren<CelestialBodyGenerator>(true);
        if (gen == null) return "No terrain generator under '" + bodyName + "'. Run step 1 (Generate Preview) first.";
        var terrainT = gen.transform.Find("Terrain Mesh");
        if (terrainT == null) return "Preview not generated yet (no 'Terrain Mesh' child). Wait a moment after step 1, then retry.";

        var mf = terrainT.GetComponent<MeshFilter>();
        var mr = terrainT.GetComponent<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null) return "Terrain Mesh has no mesh yet.";
        if (mr == null || mr.sharedMaterial == null) return "Terrain Mesh has no material yet.";

        if (!Directory.Exists(OutputDir)) Directory.CreateDirectory(OutputDir);
        string safe = bodyName.Replace(" ", "_");
        string meshPath = $"{OutputDir}/{safe}_Terrain.asset";
        string matPath  = $"{OutputDir}/{safe}_Terrain.mat";
        string prefPath = $"{OutputDir}/{safe}_BakedTerrain.prefab";

        // Bake mesh (full deep copy incl. UVs/tangents/normals) and material (Earth shader
        // + scalar/colour props; no runtime textures to lose).
        var bakedMesh = Object.Instantiate(mf.sharedMesh);
        bakedMesh.name = safe + "_Terrain";
        AssetDatabase.CreateAsset(bakedMesh, meshPath);

        var bakedMat = new Material(mr.sharedMaterial) { name = safe + "_Terrain" };
        AssetDatabase.CreateAsset(bakedMat, matPath);

        // Prefab: one GO with the mesh + a matching MeshCollider. localScale chosen so that,
        // when parented directly under the body (localPos 0, localRot identity), it renders at
        // the same world size the generator produced (generator holder was scaled by radius).
        Vector3 bodyLossy = body.transform.lossyScale;
        Vector3 terrLossy = terrainT.lossyScale;
        Vector3 prefabScale = new Vector3(
            Safe(terrLossy.x, bodyLossy.x), Safe(terrLossy.y, bodyLossy.y), Safe(terrLossy.z, bodyLossy.z));

        var root = new GameObject(safe + "_BakedTerrain");
        root.layer = body.gameObject.layer;   // Body layer → placement raycasts hit it
        root.transform.localScale = prefabScale;
        var mf2 = root.AddComponent<MeshFilter>(); mf2.sharedMesh = bakedMesh;
        var mr2 = root.AddComponent<MeshRenderer>(); mr2.sharedMaterial = bakedMat;
        var mc2 = root.AddComponent<MeshCollider>(); mc2.sharedMesh = bakedMesh;

        // The Earth shader's heightMinMax / oceanLevel / bodyScale are set at runtime and
        // are NOT shader Properties, so they can't live in the baked material asset (Unity
        // strips undeclared properties). Without heightMinMax the planet renders as solid
        // rock. This component re-applies them on load from values we capture here.
        var shading = root.AddComponent<BakedPlanetShading>();
        shading.heightMinMax = ComputeHeightMinMax(bakedMesh);
        shading.bodyScale = body.radius;
        shading.oceanLevel = 0f;
        var ph = body.GetComponentInChildren<BodyPlaceholder>(true);
        if (ph != null && ph.bodySettings != null && ph.bodySettings.shading != null)
            shading.oceanLevel = ph.bodySettings.shading.oceanLevel;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        float meshR = bakedMesh.bounds.extents.magnitude; // unit-ish
        float worldRApprox = meshR * prefabScale.x * bodyLossy.x;
        return $"BAKED '{bodyName}': mesh={bakedMesh.vertexCount} verts -> {meshPath}; mat -> {matPath}; prefab -> {prefPath}. " +
               $"prefabScale={prefabScale.x:F1} approxWorldRadius={worldRApprox:F1} (prefab='{(prefab!=null?prefab.name:"FAILED")}')";
    }

    static float Safe(float num, float den) => Mathf.Abs(den) < 1e-6f ? num : num / den;

    // heightMinMax = min/max vertex distance from centre (object space). The Earth shader
    // uses length(vertPos) as the terrain height, so this matches what the generator computed.
    static Vector2 ComputeHeightMinMax(Mesh m)
    {
        var v = m.vertices;
        float mn = float.MaxValue, mx = 0f;
        for (int i = 0; i < v.Length; i++) { float r = v[i].magnitude; if (r < mn) mn = r; if (r > mx) mx = r; }
        return new Vector2(mn, mx);
    }
}
#endif
