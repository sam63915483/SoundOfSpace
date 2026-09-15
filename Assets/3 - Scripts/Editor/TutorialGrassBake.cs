using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes the tutorial planet's grass into a blob so the tutorial runs the SAME
/// grass path as Humble Abode: gpuResident + residentWholePlanet, every blade
/// uploaded once at load, grow-in by distance, no streaming and no pop-in
/// (Sam, 2026-09-15: "make the tutorial grass use the same grass as in game").
///
/// Without a blob the tutorial streamed cells live around the player — that IS
/// the pop-in. Humble Abode's own blob can't be reused: blades are body-local and
/// its planet is r = 200, the tutorial's is r = 750.
///
/// Same recipe as PlanetBakeTool.BakeGrass, with three differences:
///   • it runs with the tutorial scene open ALONE (a second open scene's
///     colliders would sit in the bake's raycasts and drop blades);
///   • only the box area is baked (InstancedGrassRenderer.EditorBake's keep
///     radius) — a whole r = 750 sphere would be ~14x Humble Abode's blob;
///   • the blob is written next to the tutorial planet's own settings, never
///     over Humble Abode's (both bodies are NAMED "Humble Abode" on purpose).
///
/// Edit-mode terrain generation lands on a later editor tick, so this polls
/// EditorApplication.update until the LOD0 "Terrain Mesh" exists, then bakes,
/// assigns, removes the preview and saves the scene. Re-run after any rebuild
/// that moves the planet or changes the grass seed / band.
/// </summary>
public static class TutorialGrassBake
{
    public const string BlobPath = TutorialSceneBuilder.TutorialEarthDir + "/Tutorial Earth Grass.bytes";

    // Box half-diagonal (350 m box → 247 m) plus a margin; nothing outside the
    // walls is ever visible, so this is all the grass the tutorial can show.
    const float BakeMargin = 30f;
    const double TimeoutSeconds = 180.0;
    const string PreviewName = "Body Generator (Tutorial Grass Bake Preview)";

    static GameObject _holder;
    static CelestialBody _body;
    static InstancedGrassRenderer _grass;
    static Renderer[] _hiddenPlaceholderRenderers;
    static double _startedAt;

    [MenuItem("Tools/Solar System/Bake Tutorial Grass")]
    public static void Run()
    {
        if (Application.isPlaying) { Debug.LogError("[TutorialGrass] Not in play mode."); return; }
        if (_holder != null) { Debug.LogWarning("[TutorialGrass] A bake is already in progress."); return; }
        if (!File.Exists(TutorialSession.ScenePath))
        {
            Debug.LogError("[TutorialGrass] Tutorial scene not built yet — run Tools ▸ Solar System ▸ Build Tutorial Scene first.");
            return;
        }

        // The tutorial scene must be the ONLY open scene (see the class note).
        var scene = SceneManager.GetSceneByPath(TutorialSession.ScenePath);
        if (!(scene.IsValid() && scene.isLoaded) || SceneManager.sceneCount != 1)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(TutorialSession.ScenePath, OpenSceneMode.Single);
        }

        _body = null; _grass = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (_body == null)
                foreach (var cb in root.GetComponentsInChildren<CelestialBody>(true))
                    if (cb.bodyName == TutorialSceneBuilder.PlanetName && cb.bodyType != CelestialBody.BodyType.Sun) { _body = cb; break; }
            if (_grass == null)
                foreach (var g in root.GetComponentsInChildren<InstancedGrassRenderer>(true))
                    if (g.onlyBodyName == TutorialSceneBuilder.PlanetName) { _grass = g; break; }
        }
        if (_body == null) { Debug.LogError("[TutorialGrass] No planet named '" + TutorialSceneBuilder.PlanetName + "' in the tutorial scene."); return; }
        if (_grass == null) { Debug.LogError("[TutorialGrass] No InstancedGrassRenderer for '" + TutorialSceneBuilder.PlanetName + "' in the tutorial scene (run Snapshot Tutorial Spawner Prefabs, then rebuild)."); return; }

        var placeholder = _body.GetComponentInChildren<BodyPlaceholder>(true);
        if (placeholder == null || placeholder.bodySettings == null) { Debug.LogError("[TutorialGrass] The planet has no BodyPlaceholder with bodySettings."); return; }
        var spawner = Object.FindObjectOfType<SolarSystemSpawner>();
        if (spawner == null || spawner.resolutionSettings == null) { Debug.LogError("[TutorialGrass] No SolarSystemSpawner with resolutionSettings in the scene."); return; }

        // Same edit-mode preview PlanetPreviewTool builds: the runtime generator,
        // as a DontSave child, LOD0 (the mesh the grass is seen against).
        _hiddenPlaceholderRenderers = placeholder.GetComponentsInChildren<Renderer>(true);
        foreach (var r in _hiddenPlaceholderRenderers) r.enabled = false;

        _holder = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave, layer = _body.gameObject.layer };
        var t = _holder.transform;
        t.SetParent(_body.transform, false);
        t.localRotation = Quaternion.identity;
        t.localPosition = Vector3.zero;
        t.localScale = Vector3.one * _body.radius;
        var generator = _holder.AddComponent<CelestialBodyGenerator>();
        generator.resolutionSettings = spawner.resolutionSettings;
        generator.body = placeholder.bodySettings;
        generator.previewMode = CelestialBodyGenerator.PreviewMode.LOD0;
        generator.OnShapeSettingChanged();
        EditorApplication.QueuePlayerLoopUpdate();

        _startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Debug.Log("[TutorialGrass] Generating the LOD0 terrain preview, then baking the grass inside the box…");
    }

    static void Poll()
    {
        if (_holder == null) { Finish(); return; }
        EditorApplication.QueuePlayerLoopUpdate();   // keep the edit-mode generator ticking while nothing else repaints

        var terrainT = _holder.transform.Find("Terrain Mesh");
        var mf = terrainT != null ? terrainT.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
        {
            // The generator builds its preview inside its [ExecuteInEditMode]
            // Update, which the editor only runs when a view repaints — with the
            // editor unfocused it never came (the first run timed out at 180 s).
            // Tick it ourselves: the edit-mode generation is synchronous, so one
            // call produces the mesh. Reflection = we USE the generator, we don't
            // change it (trap #2; same approach as AtmosphereReloadFix).
            var gen = _holder.GetComponent<CelestialBodyGenerator>();
            if (gen != null && !EditorApplication.isCompiling)
            {
                var update = typeof(CelestialBodyGenerator).GetMethod("Update",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (update != null) { try { update.Invoke(gen, null); } catch (System.Exception e) { Debug.LogWarning("[TutorialGrass] generator tick threw: " + e.InnerException); } }
            }
            terrainT = _holder.transform.Find("Terrain Mesh");
            mf = terrainT != null ? terrainT.GetComponent<MeshFilter>() : null;
        }
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
        {
            if (EditorApplication.timeSinceStartup - _startedAt > TimeoutSeconds)
            {
                Debug.LogError("[TutorialGrass] The terrain preview never appeared (" + TimeoutSeconds + " s). Nothing baked.");
                Finish();
            }
            return;
        }

        try { Bake(terrainT, mf); }
        catch (System.Exception e) { Debug.LogError("[TutorialGrass] Bake failed: " + e); }
        Finish();
    }

    static void Bake(Transform terrainT, MeshFilter mf)
    {
        // The preview mesh has no collider; the bake seats blades by raycasting one.
        var mc = terrainT.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;
        Physics.SyncTransforms();

        // The box floor centre is the world origin (the builder places the
        // planet so the surface there is y = 0); keep the cells within reach of
        // the box corners.
        Vector3 keepCentreLocal = _body.transform.InverseTransformPoint(Vector3.zero);
        float keepRadius = TutorialSceneBuilder.BoxSize * 0.5f * Mathf.Sqrt(2f) + BakeMargin;

        int blades = _grass.EditorBake(_body, mc, out byte[] data, keepCentreLocal, keepRadius);
        Object.DestroyImmediate(mc);
        if (data == null || blades == 0)
        {
            Debug.LogError("[TutorialGrass] Bake produced no grass — check the grass band (waterMargin / maxHeightAboveWater) against the tutorial planet.");
            return;
        }

        File.WriteAllBytes(BlobPath, data);
        AssetDatabase.ImportAsset(BlobPath, ImportAssetOptions.ForceUpdate);
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(BlobPath);
        if (ta == null) { Debug.LogError("[TutorialGrass] Wrote " + BlobPath + " but Unity didn't import it as a TextAsset."); return; }

        var so = new SerializedObject(_grass);
        var prop = so.FindProperty("bakedGrass");
        if (prop != null) { prop.objectReferenceValue = ta; so.ApplyModifiedProperties(); }
        EditorUtility.SetDirty(_grass);
        EditorSceneManager.MarkSceneDirty(_grass.gameObject.scene);
        Debug.Log($"[TutorialGrass] BAKED {blades} blades inside {keepRadius:0} m of the box centre -> {BlobPath} ({data.Length / 1024f:0} KB); assigned to the tutorial grass. Resident whole-planet grass now runs in the tutorial exactly like Humble Abode.");
    }

    static void Finish()
    {
        EditorApplication.update -= Poll;
        if (_holder != null) Object.DestroyImmediate(_holder);
        _holder = null;
        if (_hiddenPlaceholderRenderers != null)
            foreach (var r in _hiddenPlaceholderRenderers) if (r != null) r.enabled = true;
        _hiddenPlaceholderRenderers = null;
        // Deliberately no SaveScene here: only Sam saves scenes. The blob is on
        // disk; the builder assigns it on every rebuild, and a manual run marks
        // the scene dirty for Ctrl+S (same contract as PlanetBakeTool).
        _grass = null; _body = null;
    }
}
