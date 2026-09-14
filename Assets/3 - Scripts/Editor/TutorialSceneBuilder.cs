using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
/// What's in the box (round 2, 2026-09-14):
///   Body Simulation   — NBodySimulation. Needed so the lens flare, the grass,
///                       the cats and the player's gravity find the bodies
///                       (they all read NBodySimulation.Bodies). Both bodies are
///                       PINNED so nothing orbits or falls into the sun.
///   Sun               — a real sun 25 km up and off to the side: CelestialBody
///                       (Sun, pinned), the directional light + SunShadowCaster
///                       exactly like the gameplay scene, the warm point light
///                       the grass shader reads, and an emissive ball (Sun.mat).
///   Tutorial Ground   — the fake planet: kinematic Rigidbody + CelestialBody
///                       (radius 10 km, centre 10 km below the floor, no
///                       generator, surfaceGravity = Humble Abode's 8). The
///                       shuttle autopilot needs a parent CelestialBody to fly;
///                       over a 200 m box its "radial up" is within 0.6° of
///                       straight up. Gravity now comes from the sim like on a
///                       real planet, so the feel matches Humble Abode.
///     Terrain Mesh    — the floor: 200×1×200 cube, top at y = 0, Green.mat,
///                       layer Body, MESH collider named the way the grass
///                       renderer looks for a planet's terrain.
///     Wall ±X / ±Z, Ceiling — quads with the digit-rain material + box
///                       colliders, marked LensFlarePassThrough (glass).
///     Shuttle_Lander  — prefab instance, feet on the floor at the centre
///     Tutorial Props  — TutorialPropField: trees / crystals / mushrooms placed
///                       once at load (the live spawners would scan a 10 km
///                       sphere every tick).
///   Grass             — InstancedGrassRenderer, the gameplay values, pointed at
///                       Tutorial Ground, no baked blob (streams live; cheap).
///   CatSpawner        — the real one, wired like the gameplay scene, small cap.
///   HUD Canvas / Dot  — the crosshair (CrosshairReticle is scene-placed in the
///                       gameplay scene, never seeded).
///   HelmetHudConfig   — prefab snapshot of the gameplay scene's config; without
///                       it the compass / boost / vitals clusters fall back to
///                       their old floating-card look.
///   Player            — Player.prefab with the gameplay scene's overrides,
///                       standing in the stasis pod
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
    const string SunMatGuid        = "cec4db5828ab9439e899c191ec38b27a"; // Sun.mat (emissive Standard)
    const string ShuttlePrefabGuid = "407ee2e645e2e124a8729ec234a84f8e"; // Shuttle_Lander.prefab
    const string PlayerPrefabGuid  = "30d1ef01b1bfd4cce849c5888b9c1de4"; // Player.prefab (the gameplay scene's player)
    const string PlanetEffectsGuid = "2a0830d1f8e1c4c019b5757c93f3297a"; // Planet Effects.asset — stripped (no planets)
    const string CrystalPrefabGuid = "465c505844950da499dcf096a6da4409"; // crystal_17_2.prefab
    const string GrassMatGuid      = "87b4eb48bceaa104f95d1e5547d74e26"; // CG_GameGrass.mat
    const string GrassDepthMatGuid = "edd9f91ec9ce32e4289de3f8cb66de29"; // CG_GrassDepth.mat (must be the real asset — build variant stripping)
    static readonly string[] GrassMeshGuids =
    {
        "fd5189d1aefffec41b3b48de8f67673b", // CartoonGrass/Models/Grass_01.fbx
        "8923950d79ad0da4ebf6771f2a89c6d8", // Grass_02.fbx
        "88be1953c715bea428fdf814f87aabe1", // Grass_03.fbx
    };
    const string TreeDir     = "Assets/1 - samsPrefabs/Trees";
    const string MushroomDir = "Assets/5 - External Imports/Nature & Trees/Low Poly Mushrooms Pack/Prefabs/Mushrooms";

    const float BoxSize       = 200f;
    const float FakeRadius    = 10000f;
    const float FakeGravity   = 8f;      // Humble Abode's surfaceGravity (1.6.7.7.7.unity)
    const int   BodyLayer     = 10;      // "Body" — the terrain layer (player + shuttle ground casts)
    const int   SunLayer      = 11;      // "Sun" — excluded from the sun's own light masks
    const int   UILayer       = 5;
    const int   WalkableMask  = 34304;   // Ship | Body | ShuttleInterior — the gameplay scene's override
    // CrosshairReticle.scale. The gameplay Dot carries 12 and nothing in code
    // shrinks it, yet Sam's round-2 build read as "huge" — a third of that as
    // the starting point; tune on Dot ▸ CrosshairReticle ▸ Scale and mirror here.
    const float ReticleScale  = 4f;

    // The sun: 25 km out along the gameplay light's authored direction
    // (Euler 55, -35, 0 → forward (-0.329, -0.819, 0.470)). 55° elevation, so
    // from anywhere in the box the flare's line of sight leaves through the
    // (pass-through) ceiling. Far enough that it never wins the player's
    // nearest-surface anchor election; close enough to read as a disc (~7°).
    static readonly Vector3 SunDir = new Vector3(0.329f, 0.819f, -0.470f).normalized;
    const float SunDistance = 25000f;
    const float SunRadius   = 1500f;     // the gameplay sun's radius

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
        // atmosphere post, which doesn't exist here) so shadow sides aren't black.
        var skybox = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SkyboxMatGuid));
        if (skybox != null) RenderSettings.skybox = skybox;
        else Debug.LogWarning("[TutorialScene] ESO Milky Way skybox material not found — sky will be black.");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.13f);
        RenderSettings.fog = false;

        // The simulation (both bodies pinned — it exists so NBodySimulation.Bodies
        // has something in it, not to move anything).
        new GameObject("Body Simulation").AddComponent<NBodySimulation>();

        // The Sun.
        var sun = MakeBody("Sun", CelestialBody.BodyType.Sun, SunRadius, 0.0001f, SunDir * SunDistance, SunLayer);
        var caster = new GameObject("Sun Shadow Caster");
        caster.layer = SunLayer;
        caster.transform.SetParent(sun.transform, false);
        caster.transform.LookAt(Vector3.zero);            // authored aim in case SunShadowCaster finds no camera
        var dirLight = caster.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = new Color(0.999387f, 1f, 0.8915094f);
        dirLight.intensity = 1f;
        dirLight.shadows = LightShadows.Soft;
        dirLight.shadowStrength = 1f;
        dirLight.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
        dirLight.shadowBias = 0.05f;                      // Unity defaults — the gameplay 0.16/0.1/10 are tuned for km-scale terrain
        dirLight.shadowNormalBias = 0.4f;
        dirLight.shadowNearPlane = 0.2f;
        dirLight.cullingMask = ~(1 << SunLayer);
        caster.AddComponent<SunShadowCaster>();
        RenderSettings.sun = dirLight;

        var pointGo = new GameObject("Point Light (Sun)");   // InstancedGrassRenderer reads "a point light under 'Sun'"
        pointGo.layer = SunLayer;
        pointGo.transform.SetParent(sun.transform, false);
        var point = pointGo.AddComponent<Light>();
        point.type = LightType.Point;
        point.color = new Color(1f, 0.98721176f, 0.7122642f);
        point.intensity = 1f;
        point.range = 40000f;
        point.shadows = LightShadows.None;
        point.cullingMask = 1855;

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Sun Ball";
        ball.layer = SunLayer;
        ball.transform.SetParent(sun.transform, false);
        ball.transform.localScale = Vector3.one * (SunRadius * 2f);
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        var ballMr = ball.GetComponent<MeshRenderer>();
        var sunMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SunMatGuid));
        if (sunMat != null) ballMr.sharedMaterial = sunMat;
        ballMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ballMr.receiveShadows = false;

        // The fake planet.
        var ground = MakeBody("Tutorial Ground", CelestialBody.BodyType.Planet, FakeRadius, FakeGravity,
                              new Vector3(0f, -FakeRadius, 0f), BodyLayer);

        // Floor: solid green slab, top face at y = 0. Named "Terrain Mesh" with a
        // MeshCollider because InstancedGrassRenderer seats grass ONLY on a
        // collider by that name under the body (else it rescans every frame).
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Terrain Mesh";
        floor.layer = BodyLayer;
        floor.transform.SetParent(ground.transform, false);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(BoxSize, 1f, BoxSize);
        Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.AddComponent<MeshCollider>().sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
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

        // Props: trees / crystals / mushrooms, placed once at load.
        var props = new GameObject("Tutorial Props");
        props.transform.SetParent(ground.transform, false);
        var field = props.AddComponent<TutorialPropField>();
        WireProps(field);

        // Managers: grass (live, streams around the player) + cats (live, small cap).
        var managers = new GameObject("--- Managers ---");
        var grassGo = new GameObject("Grass");
        grassGo.transform.SetParent(managers.transform, false);
        WireGrass(grassGo.AddComponent<InstancedGrassRenderer>());
        var catsGo = new GameObject("CatSpawner");
        catsGo.transform.SetParent(managers.transform, false);
        var cats = catsGo.AddComponent<CatSpawner>();
        WireCatSpawner.WireInto(cats);
        cats.inputSettings = null;      // use spawnRadius, not the view-distance setting
        cats.spawnRadius = 300f;        // > the box: nothing ever despawns
        cats.maxCats = 5;               // the scan stops for good once the cap is met
        cats.cellSize = 45f;            // ~13 candidate cells in the box → the cap fills on the first tick
        cats.seed = 4242;

        // HUD pieces that are scene objects in the gameplay scene (never seeded).
        var uiRoot = new GameObject("--- UI ---");
        BuildCrosshair(uiRoot.transform);
        var helmetCfg = AssetDatabase.LoadAssetAtPath<GameObject>(HelmetHudConfigSnapshot.PrefabPath);
        if (helmetCfg != null)
        {
            var cfg = (GameObject)PrefabUtility.InstantiatePrefab(helmetCfg, scene);
            cfg.transform.SetParent(uiRoot.transform, false);
        }
        else Debug.LogWarning("[TutorialScene] " + HelmetHudConfigSnapshot.PrefabPath + " missing — run Tools ▸ Solar System ▸ Snapshot HelmetHudConfig Prefab with the gameplay scene open, or the compass / boost / vitals clusters fall back to their old look.");

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

    static GameObject MakeBody(string name, CelestialBody.BodyType type, float radius, float gravity, Vector3 pos, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.position = pos;
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var cb = go.AddComponent<CelestialBody>();
        cb.bodyType = type;
        cb.radius = radius;
        cb.surfaceGravity = gravity;
        cb.isPinned = true;              // NBodySimulation never moves it
        cb.bodyName = name;              // CelestialBody.OnValidate names the GameObject after bodyName
        go.name = name;                  // AddComponent fired OnValidate with the default name; set it again
        return go;
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
        go.AddComponent<LensFlarePassThrough>(); // glass: the sun's flare shows through it
    }

    /// The gameplay scene's crosshair: '--- UI --- ▸ User Interface ▸ Overlay Canvas ▸ Dot'
    /// (an Image + CrosshairReticle, values copied from 1.6.7.7.7.unity).
    static void BuildCrosshair(Transform parent)
    {
        var canvasGo = new GameObject("Overlay Canvas", typeof(RectTransform));
        canvasGo.layer = UILayer;
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var dot = new GameObject("Dot", typeof(RectTransform));
        dot.layer = UILayer;
        dot.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)dot.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(8f, 8f);
        var img = dot.AddComponent<Image>();
        img.raycastTarget = false;
        var reticle = dot.AddComponent<CrosshairReticle>();
        reticle.color = new Color(0.749f, 0.914f, 1f, 1f);
        reticle.scale = ReticleScale;
        reticle.thickness = 2f;
        reticle.morphSeconds = 0.2f;
        reticle.showPip = true;
        reticle.requireLookToInteract = true;
        reticle.aimRadius = 0.1f;
        reticle.extraAimMargin = 0f;
        reticle.forgiveDepth = 0.5f;
    }

    static void WireProps(TutorialPropField field)
    {
        // Same 18 Humble Abode variants and rank weights the gameplay TreeSpawner
        // carries (HumbleAbodeTreeVariants: Forest 05,04,01,02,03,06,07,08 → 8..1;
        // Valley 03,08,06,09,01,02,04,05,10,07 → 10..1).
        string[] forest = { "05", "04", "01", "02", "03", "06", "07", "08" };
        string[] valley = { "03", "08", "06", "09", "01", "02", "04", "05", "10", "07" };
        var prefabs = new List<GameObject>();
        var weights = new List<float>();
        for (int i = 0; i < forest.Length; i++) AddTree(prefabs, weights, TreeDir + "/HA_FF_Tree_" + forest[i] + ".prefab", forest.Length - i);
        for (int i = 0; i < valley.Length; i++) AddTree(prefabs, weights, TreeDir + "/HA_FV_Tree_" + valley[i] + ".prefab", valley.Length - i);
        field.treePrefabs = prefabs.ToArray();
        field.treeWeights = weights.ToArray();
        if (prefabs.Count == 0) Debug.LogWarning("[TutorialScene] No HA tree prefabs found under " + TreeDir);

        field.crystalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(CrystalPrefabGuid));
        if (field.crystalPrefab == null) Debug.LogWarning("[TutorialScene] crystal_17_2.prefab not found by GUID");

        var shrooms = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { MushroomDir }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) shrooms.Add(go);
        }
        shrooms.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        field.mushroomPrefabs = shrooms.ToArray();
        if (shrooms.Count == 0) Debug.LogWarning("[TutorialScene] No mushroom prefabs found under " + MushroomDir);

        field.halfExtent = BoxSize * 0.5f;
        field.groundMask = 1 << BodyLayer;
    }

    static void AddTree(List<GameObject> prefabs, List<float> weights, string path, float weight)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) { Debug.LogWarning("[TutorialScene] missing tree prefab " + path); return; }
        prefabs.Add(go);
        weights.Add(weight);
    }

    /// The gameplay scene's InstancedGrassRenderer values verbatim (1.6.7.7.7.unity,
    /// GrassSpawner object), minus the Humble Abode baked blob.
    static void WireGrass(InstancedGrassRenderer g)
    {
        g.onlyBodyName = "Tutorial Ground";
        var meshes = new List<Mesh>();
        foreach (var guid in GrassMeshGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            Mesh mesh = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh m) { mesh = m; break; }
            if (mesh != null) meshes.Add(mesh); else Debug.LogWarning("[TutorialScene] grass mesh missing: " + path);
        }
        g.grassMeshes = meshes.ToArray();
        g.grassMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GrassMatGuid));
        g.depthMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GrassDepthMatGuid));
        g.bakedGrass = null;
        g.seed = 45678;
        g.spawnRadius = 60f;
        g.cellSize = 1.5f;
        g.cellCoverage = 1f;
        g.bladesPerCell = 6;
        g.minScale = 0.77f;
        g.maxScale = 2f;
        g.maxSurfaceAngle = 30f;
        g.waterMargin = 1f;
        g.surfaceLift = -0.05f;
        g.surfaceRayHeight = 100f;
        g.updateInterval = 0.1f;
        g.maxCellsPerUpdate = 200;
        g.groundMask = ~0;
        g.receiveShadows = true;
        g.frustumCull = true;
        g.maxHeightAboveWater = 15f;
        g.patchCells = 12;
        g.patchDensity = 0.35f;
        g.slopeBladeFloor = 0.35f;
        g.slopeScaleFloor = 0.7f;
        g.slopeConform = 0.8f;
        g.densityFade = true;
        g.densityFadeStartFrac = 0.45f;
        g.noCullRadius = 15f;
        g.densityFadeFloor = 0.6f;
        g.miniPatchCells = 3;
        g.miniPatchDensity = 0.2f;
        g.perBladeReseat = true;
        g.depthDilatePixels = 0f;
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
