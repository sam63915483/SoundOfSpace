using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/4 - Scenes/Tutorial.unity — the tutorial box
/// (docs/superpowers/specs/2026-09-14-tutorial-box-design.md).
///
/// Tools ▸ Solar System ▸ Build Tutorial Scene. Modelled on PlanetGalleryBuilder:
/// the scene is created ADDITIVELY, populated, saved and closed, and the
/// previously active scene is restored — the open gameplay scene is never
/// touched. Re-running WIPES and rebuilds the scene (hand-placed extras are
/// lost), so add tutorial content here until we stop regenerating.
///
/// What's in the box:
///   Tutorial Ground   — the fake planet: kinematic Rigidbody + CelestialBody
///                       (radius 10 km, centre 10 km below the floor, no
///                       generator). The shuttle autopilot needs a parent
///                       CelestialBody to fly; over a 200 m box its "radial up"
///                       is within 0.6° of straight up. NO NBodySimulation in
///                       the scene — the player then uses its flat-floor gravity.
///     Floor           — 200×1×200 cube, top at y = 0, Green.mat, layer Body
///     Wall ±X / ±Z, Ceiling — quads with the digit-rain material + box colliders
///     Shuttle_Lander  — prefab instance, feet on the floor at the centre
///   Player            — Player.prefab with the gameplay scene's overrides,
///                       standing in the stasis pod
///   Sun               — directional light
///   Tutorial Director — runs the fly-in (TutorialDirector)
///   EventSystem
/// </summary>
public static class TutorialSceneBuilder
{
    const string SceneDir   = "Assets/4 - Scenes";
    const string ScenePath  = TutorialSession.ScenePath;
    const string RainMatPath = SceneDir + "/TutorialDigitRain.mat";
    const string RainShader = "Custom/TutorialDigitRain";

    // Same GUIDs the gameplay scene / PlanetGalleryBuilder use, so this file
    // has no path into packs that might move.
    const string SkyboxMatGuid     = "e3d301707e23ccd4e84049a21e148e54"; // ESO Milky Way
    const string GreenMatGuid      = "ac038ee5893cf4c648f7a602051dfc36"; // Green.mat
    const string ShuttlePrefabGuid = "407ee2e645e2e124a8729ec234a84f8e"; // Shuttle_Lander.prefab
    const string PlayerPrefabGuid  = "30d1ef01b1bfd4cce849c5888b9c1de4"; // Player.prefab (the gameplay scene's player)
    const string PlanetEffectsGuid = "2a0830d1f8e1c4c019b5757c93f3297a"; // Planet Effects.asset — stripped (no planets)

    const float BoxSize       = 200f;
    const float FakeRadius    = 10000f;
    const int   BodyLayer     = 10;     // "Body" — the terrain layer (player + shuttle ground casts)
    const int   WalkableMask  = 34304;  // Ship | Body | ShuttleInterior — the gameplay scene's override

    [MenuItem("Tools/Solar System/Build Tutorial Scene")]
    public static void Build()
    {
        var shuttlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(ShuttlePrefabGuid));
        var playerPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(PlayerPrefabGuid));
        if (shuttlePrefab == null || playerPrefab == null)
        {
            Debug.LogError("[TutorialScene] Shuttle_Lander.prefab or Player.prefab not found by GUID — nothing built.");
            return;
        }
        if (Shader.Find(RainShader) == null)
        {
            Debug.LogError("[TutorialScene] Shader '" + RainShader + "' not found (Assets/Shaders/TutorialDigitRain.shader) — nothing built.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets", "4 - Scenes");
        var rainMat = LoadOrCreateRainMaterial();

        var prevActive = SceneManager.GetActiveScene();
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded)
        {
            // The tutorial scene itself is open: rebuild it IN PLACE.
            SceneManager.SetActiveScene(alreadyOpen);
            try
            {
                foreach (var go in alreadyOpen.GetRootGameObjects()) Object.DestroyImmediate(go);
                Populate(alreadyOpen, shuttlePrefab, playerPrefab, rainMat);
                EditorSceneManager.MarkSceneDirty(alreadyOpen);
                if (!EditorSceneManager.SaveScene(alreadyOpen))
                    Debug.LogError("[TutorialScene] SaveScene failed for " + ScenePath);
            }
            finally
            {
                if (prevActive.IsValid() && prevActive != alreadyOpen) SceneManager.SetActiveScene(prevActive);
            }
        }
        else
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                Populate(scene, shuttlePrefab, playerPrefab, rainMat);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    Debug.LogError("[TutorialScene] SaveScene failed for " + ScenePath);
            }
            finally
            {
                if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        AddToBuildSettings();
        AssetDatabase.Refresh();
        Debug.Log("[TutorialScene] Built → " + ScenePath + " (added to Build Settings, enabled). " +
                  "Open via Tools ▸ Solar System ▸ Open Tutorial Scene and press Play, or TUTORIAL on the main menu.");
    }

    [MenuItem("Tools/Solar System/Open Tutorial Scene")]
    public static void Open()
    {
        if (!System.IO.File.Exists(ScenePath)) { Debug.LogError("[TutorialScene] Not built yet — run Build Tutorial Scene first."); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ── population ──────────────────────────────────────────────────────────

    static void Populate(Scene scene, GameObject shuttlePrefab, GameObject playerPrefab, Material rainMat)
    {
        // Lighting like the gameplay scene: Milky Way skybox, no fog. A dim flat
        // ambient (the gameplay scene uses none — its night sides are lit by the
        // atmosphere post, which doesn't exist here) so the shuttle's shadow
        // side isn't pitch black.
        var skybox = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SkyboxMatGuid));
        if (skybox != null) RenderSettings.skybox = skybox;
        else Debug.LogWarning("[TutorialScene] ESO Milky Way skybox material not found — sky will be black.");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.13f);
        RenderSettings.fog = false;

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.9f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        RenderSettings.sun = sun;

        // The fake planet.
        var ground = new GameObject("Tutorial Ground");
        ground.layer = BodyLayer;
        ground.transform.position = new Vector3(0f, -FakeRadius, 0f);
        var rb = ground.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var cb = ground.AddComponent<CelestialBody>();
        cb.bodyType = CelestialBody.BodyType.Planet;
        cb.radius = FakeRadius;
        cb.surfaceGravity = 20f;   // unused — no NBodySimulation; the player uses flatGravity (20)
        cb.bodyName = "Tutorial Ground";   // CelestialBody.OnValidate names the GameObject after bodyName
        ground.name = cb.bodyName;         // AddComponent fired OnValidate with the default name; set it again

        // Floor: solid green slab, top face at y = 0.
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.layer = BodyLayer;
        floor.transform.SetParent(ground.transform, false);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(BoxSize, 1f, BoxSize);
        var green = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GreenMatGuid));
        if (green != null) floor.GetComponent<MeshRenderer>().sharedMaterial = green;

        // Walls + ceiling: each quad's local +Z points OUT of the box; the box
        // collider sits just outside the pane so the visible surface is the
        // limit you can walk / fly to.
        float h = BoxSize * 0.5f;
        MakePane(ground.transform, rainMat, "Wall +X",  new Vector3( h, h, 0f), Quaternion.Euler(0f,  90f, 0f));
        MakePane(ground.transform, rainMat, "Wall -X",  new Vector3(-h, h, 0f), Quaternion.Euler(0f, -90f, 0f));
        MakePane(ground.transform, rainMat, "Wall +Z",  new Vector3(0f, h,  h), Quaternion.identity);
        MakePane(ground.transform, rainMat, "Wall -Z",  new Vector3(0f, h, -h), Quaternion.Euler(0f, 180f, 0f));
        MakePane(ground.transform, rainMat, "Ceiling",  new Vector3(0f, BoxSize, 0f), Quaternion.Euler(-90f, 0f, 0f));

        // The shuttle: named exactly Shuttle_Lander (ShuttleAutopilot attaches by
        // name), child of the fake planet, feet on the floor at the centre. Its
        // authored pose here IS the landing target — the hover is 100 m above it.
        var shuttle = (GameObject)PrefabUtility.InstantiatePrefab(shuttlePrefab, scene);
        shuttle.name = "Shuttle_Lander";
        shuttle.transform.SetParent(ground.transform, false);
        shuttle.transform.rotation = Quaternion.identity;
        shuttle.transform.position = Vector3.zero;
        float lift = FeetLift(shuttle);
        shuttle.transform.position = new Vector3(0f, lift, 0f);

        // The player: Player.prefab + the gameplay scene's overrides. Placed in
        // the pod at the PARKED pose so the very first frames already show the
        // pod interior (the director re-seats it once the shuttle jumps up).
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        player.name = "Player";
        var pc = player.GetComponent<PlayerController>();
        if (pc != null) pc.walkableMask = WalkableMask;
        var cam = player.GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.Skybox;
            var post = cam.GetComponent<CustomPostProcessing>();
            if (post != null && post.effects != null)
            {
                var planetFx = AssetDatabase.LoadAssetAtPath<PostProcessingEffect>(AssetDatabase.GUIDToAssetPath(PlanetEffectsGuid));
                post.effects = post.effects.Where(e => e != null && e != planetFx).ToArray();
            }
        }
        Transform pod = null;
        foreach (var t in shuttle.GetComponentsInChildren<Transform>(true))
            if (t.name == "StasisPod") { pod = t; break; }
        if (pod != null)
        {
            player.transform.SetPositionAndRotation(pod.TransformPoint(new Vector3(0f, 1.02f, 0f)),
                                                    Quaternion.LookRotation(pod.forward, pod.up));
        }
        else
        {
            Debug.LogWarning("[TutorialScene] StasisPod not found under the shuttle — player placed beside it.");
            player.transform.position = new Vector3(6f, 1f, 0f);
        }

        var director = new GameObject("Tutorial Director");
        director.AddComponent<TutorialDirector>();

        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    static void MakePane(Transform parent, Material mat, string name, Vector3 worldPos, Quaternion worldRot)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.layer = BodyLayer;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        go.transform.localScale = new Vector3(BoxSize, BoxSize, 1f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // the quad's MeshCollider is one-sided and paper-thin
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 2f);      // 2 m thick (local z is unscaled)
        box.center = new Vector3(0f, 0f, 1f);    // entirely outside the pane
    }

    /// Height to lift the shuttle so its lowest mesh point touches y = 0.
    /// From MeshFilter geometry through each transform's matrix — Collider /
    /// Renderer .bounds are stale in edit mode right after an instantiate +
    /// move (the first build put the shuttle 7.5 km up from exactly that).
    static float FeetLift(GameObject shuttle)
    {
        float minY = float.PositiveInfinity;
        foreach (var mf in shuttle.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var b = mesh.bounds;
            var m = mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                         (i & 2) == 0 ? b.min.y : b.max.y,
                                         (i & 4) == 0 ? b.min.z : b.max.z);
                minY = Mathf.Min(minY, m.MultiplyPoint3x4(corner).y);
            }
        }
        return float.IsInfinity(minY) ? 0f : -minY + 0.02f;
    }

    static Material LoadOrCreateRainMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RainMatPath);
        if (mat != null) return mat;
        mat = new Material(Shader.Find(RainShader)) { name = "TutorialDigitRain" };
        AssetDatabase.CreateAsset(mat, RainMatPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int idx = scenes.FindIndex(s => s.path == ScenePath);
        if (idx >= 0)
        {
            if (scenes[idx].enabled) return;
            scenes[idx].enabled = true;
        }
        else scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
