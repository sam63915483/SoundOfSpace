using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds Assets/4 - Scenes/Tutorial.unity — the tutorial box
/// (docs/superpowers/specs/2026-09-14-tutorial-box-design.md, round 4).
///
/// Tools ▸ Solar System ▸ Build Tutorial Scene. Modelled on PlanetGalleryBuilder:
/// the scene is created ADDITIVELY (or rebuilt in place if it is the open
/// scene), populated, saved; the previously active scene is restored. The
/// gameplay scene is never touched. Re-running WIPES and rebuilds the scene.
///
/// Round 4 (Sam): the green slab is replaced by a REAL generated planet — a
/// Humble Abode clone at radius 750 m (1.5× Cyclops), static, with its
/// atmosphere and ocean, so the sky is blue and the sun is up. The player is
/// confined to a 350 m box on the flattest patch of land the builder can find.
/// Everything that made the slab a special case (fake gravity body, baked
/// props, vented oxygen) is gone: the spawners, grass, oxygen, atmosphere and
/// landing all run the gameplay code on a gameplay-style planet.
///
/// What's in the scene:
///   --- Celestial ---   NBodySimulation (both bodies PINNED), SolarSystemSpawner
///                       (300/100/50 like the gameplay scene) + LODHandler — the
///                       pair that turns the placeholder into a generated planet
///                       at load and keeps the mesh assigned.
///   Sun                 CelestialBody(Sun) 25 km out at 55° with the gameplay
///                       directional light + SunShadowCaster (child), the warm
///                       point light the grass shader reads, an emissive ball.
///   Humble Abode        the planet: CelestialBody r=750 g=8 pinned, 'Mesh
///                       Holder' BodyPlaceholder → the cloned settings under
///                       Solar System/Tutorial Earth/, 'waterline' trigger. Named
///                       Humble Abode on purpose: grass onlyBodyName, the suit's
///                       O2 refill zone and more are keyed on that name. Rotated
///                       so a spot with water + a flat dry centre + a hill faces
///                       +Y; placed so the centre's surface is world y = 0.
///     Shuttle_Lander    prefab instance, feet at y = 0 (its authored pose is the
///                       landing target; the hover is 100 m above it)
///   Tutorial Box        4 walls (350 × 450, y −100…350) + ceiling at 350, digit
///                       rain panes, box colliders, LensFlarePassThrough.
///   --- Managers ---    snapshot prefabs of the gameplay spawners (trees,
///                       crystals, mushrooms, cats, grass) with Sam's tuning.
///   --- UI ---          Overlay Canvas ▸ Dot (crosshair), HelmetHudConfig prefab.
///   Player              TutorialPlayer.prefab (snapshot of the gameplay player)
///   Tutorial Director, EventSystem
/// </summary>
public static class TutorialSceneBuilder
{
    const string SceneDir    = "Assets/4 - Scenes";
    const string ScenePath   = TutorialSession.ScenePath;
    const string RainMatPath = SceneDir + "/TutorialDigitRain.mat";
    const string RainWallMatPath = SceneDir + "/TutorialDigitRainWall.mat";
    const string RainShader  = "Custom/TutorialDigitRain";

    // Same GUIDs the gameplay scene / PlanetGalleryBuilder use, so this file
    // has no path into packs that might move.
    const string SkyboxMatGuid     = "e3d301707e23ccd4e84049a21e148e54"; // ESO Milky Way
    const string GreenMatGuid      = "ac038ee5893cf4c648f7a602051dfc36"; // Green.mat — the edit-mode placeholder sphere
    const string SunMatGuid        = "cec4db5828ab9439e899c191ec38b27a"; // Sun.mat (emissive Standard)
    const string ShuttlePrefabGuid = "407ee2e645e2e124a8729ec234a84f8e"; // Shuttle_Lander.prefab
    const string PlayerPrefabGuid  = "30d1ef01b1bfd4cce849c5888b9c1de4"; // Player.prefab (fallback if no snapshot)
    // Humble Abode's generation assets (data, not the forbidden code): cloned, never shared.
    const string HAShapeGuid       = "a286a17239e1e4ac2a580c2e9dc9c041";
    const string HAShadingGuid     = "14d46b34a29044212ba2b6855e8e2fdd";
    const string HAAtmosphereGuid  = "828f0f31423494b5d9315f79af16274d";
    const string HAOceanGuid       = "2dda04ece1bc5461c9c060d959433048";
    const string TutorialEarthDir  = "Assets/5 - External Imports/Celestial Body/Solar System/Tutorial Earth";

    const float BoxSize       = 350f;    // Sam, round 4: 200 → 350
    const float WallBelow     = 100f;    // panes reach this far below y = 0 (terrain dips)
    const float PlanetRadius  = 750f;    // Sam, round 5: half of 1500 (1.5× Cyclops)
    const float HARadius      = 200f;    // Humble Abode's radius — metre-valued spawner knobs scale by PlanetRadius / HARadius
    const float PlanetGravity = 8f;      // Humble Abode's surfaceGravity
    const string PlanetName   = "Humble Abode";
    const int   BodyLayer     = 10;      // "Body"
    const int   SunLayer      = 11;      // "Sun"
    const int   UILayer       = 5;
    const float ReticleScale  = 1f;      // CrosshairReticle.scale (Sam: 12 → 4 → 1)

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
        var settings = GetOrCreateTutorialEarth();
        if (settings == null) return;
        var rainMat = LoadOrCreateRainMaterial(RainMatPath, 1.6f, radial: true);   // ceiling: streams out from the centre
        var rainWallMat = LoadOrCreateRainMaterial(RainWallMatPath, 1.6f * BoxSize / (BoxSize + WallBelow), radial: false);   // same glyph proportions on the taller pane

        var prevActive = SceneManager.GetActiveScene();
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded)
        {
            // The tutorial scene itself is open: rebuild it IN PLACE.
            SceneManager.SetActiveScene(alreadyOpen);
            try
            {
                foreach (var go in alreadyOpen.GetRootGameObjects()) Object.DestroyImmediate(go);
                Populate(alreadyOpen, shuttlePrefab, playerPrefab, settings, rainMat, rainWallMat);
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
                Populate(scene, shuttlePrefab, playerPrefab, settings, rainMat, rainWallMat);
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

    static void Populate(Scene scene, GameObject shuttlePrefab, GameObject playerPrefab,
                         CelestialBodySettings settings, Material rainMat, Material rainWallMat)
    {
        // Lighting like the gameplay scene: Milky Way skybox, no fog, NO ambient
        // — the atmosphere post lights the night side, and any flat ambient
        // washes a real planet out (PlanetGalleryBuilder's note).
        var skybox = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SkyboxMatGuid));
        if (skybox != null) RenderSettings.skybox = skybox;
        else Debug.LogWarning("[TutorialScene] ESO Milky Way skybox material not found — sky will be black.");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.fog = false;

        // The celestial managers: the sim (pinned bodies — it exists so
        // NBodySimulation.Bodies has something in it), the spawner that turns the
        // placeholder into a generated planet at load, and the LOD handler that
        // assigns the mesh (without it the planet is invisible).
        var celestial = new GameObject("--- Celestial ---");
        celestial.AddComponent<NBodySimulation>();
        var spawner = celestial.AddComponent<SolarSystemSpawner>();
        spawner.resolutionSettings = new CelestialBodyGenerator.ResolutionSettings { lod0 = 300, lod1 = 100, lod2 = 50, collider = 200 };
        celestial.AddComponent<LODHandler>();

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
        dirLight.shadowBias = 0.05f;
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

        // The planet. Find the patch under the box that has water, a flat dry
        // centre and a hill, then rotate the planet so that direction points +Y
        // and place it so the surface at the centre is y = 0 — the box stays
        // axis-aligned at the origin.
        Quaternion planetRot = FindTutorialSpot(settings.shape, PlanetRadius, BoxSize * 0.5f, out float hCentre);
        var planet = MakeBody(PlanetName, CelestialBody.BodyType.Planet, PlanetRadius, PlanetGravity,
                              new Vector3(0f, -hCentre * PlanetRadius, 0f), BodyLayer);
        planet.transform.rotation = planetRot;
        var holder = new GameObject("Mesh Holder");        // the terrain seed: SolarSystemSpawner replaces it at load
        holder.layer = BodyLayer;
        holder.transform.SetParent(planet.transform, false);
        holder.transform.localScale = Vector3.one * PlanetRadius;   // edit-mode cosmetic only
        var bp = holder.AddComponent<BodyPlaceholder>();
        bp.terrainResolution = 50;
        bp.material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GreenMatGuid));
        bp.useBodySettings = true;
        bp.bodySettings = settings;
        bp.generateCollider = false;
        var water = new GameObject("waterline");           // oceanLevel = 1 → sea level == radius
        water.layer = BodyLayer;
        water.tag = "Water";
        water.transform.SetParent(planet.transform, false);
        var wc = water.AddComponent<SphereCollider>();
        wc.isTrigger = true;
        wc.radius = PlanetRadius;

        // The shuttle: named exactly Shuttle_Lander (ShuttleAutopilot attaches by
        // name), child of the planet, feet at y = 0 at the box centre. Its
        // authored pose here IS the landing target — the hover is 100 m above it.
        var shuttle = (GameObject)PrefabUtility.InstantiatePrefab(shuttlePrefab, scene);
        shuttle.name = "Shuttle_Lander";
        shuttle.transform.SetParent(planet.transform, true);
        shuttle.transform.rotation = Quaternion.identity;
        shuttle.transform.position = Vector3.zero;
        float lift = FeetLift(shuttle);
        shuttle.transform.position = new Vector3(0f, lift, 0f);

        // The box: walls reach WallBelow under y = 0 (terrain dips), ceiling at
        // BoxSize. Each quad's local +Z points OUT; the box collider sits just
        // outside the pane so the visible surface is the limit.
        var box = new GameObject("Tutorial Box");
        float h = BoxSize * 0.5f, wallH = BoxSize + WallBelow, wallY = (BoxSize - WallBelow) * 0.5f;
        MakePane(box.transform, rainWallMat, "Wall +X", new Vector3( h, wallY, 0f), Quaternion.Euler(0f,  90f, 0f), BoxSize, wallH);
        MakePane(box.transform, rainWallMat, "Wall -X", new Vector3(-h, wallY, 0f), Quaternion.Euler(0f, -90f, 0f), BoxSize, wallH);
        MakePane(box.transform, rainWallMat, "Wall +Z", new Vector3(0f, wallY,  h), Quaternion.identity,          BoxSize, wallH);
        MakePane(box.transform, rainWallMat, "Wall -Z", new Vector3(0f, wallY, -h), Quaternion.Euler(0f, 180f, 0f), BoxSize, wallH);
        MakePane(box.transform, rainMat,     "Ceiling", new Vector3(0f, BoxSize, 0f), Quaternion.Euler(-90f, 0f, 0f), BoxSize, BoxSize);

        // Managers: the gameplay scene's spawners, with Sam's tuning, from prefab snapshots.
        var managers = new GameObject("--- Managers ---");
        foreach (var (name, _) in TutorialSnapshots.Spawners)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialSnapshots.SpawnerPrefabPath(name));
            if (prefab == null)
            {
                Debug.LogWarning("[TutorialScene] " + TutorialSnapshots.SpawnerPrefabPath(name) + " missing — run Tools ▸ Solar System ▸ Snapshot Tutorial Spawner Prefabs. Skipping " + name + ".");
                continue;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            inst.transform.SetParent(managers.transform, false);
            inst.SetActive(true);
            var grass = inst.GetComponent<InstancedGrassRenderer>();
            if (grass != null)
            {
                grass.onlyBodyName = PlanetName;
                grass.bakedGrass = null;        // Humble Abode's blob is body-local at r = 200; stream live here
                // Grass only seats between waterMargin and maxHeightAboveWater ABOVE SEA
                // LEVEL, in metres. Humble Abode's 1…15 m covers its lowlands; on a
                // planet PlanetRadius/HARadius× bigger the same band is a shoreline
                // strip (Sam, round 4: "only a small patch of grass"). Scale it.
                float k = PlanetRadius / HARadius;
                grass.maxHeightAboveWater *= k;
                grass.waterMargin *= k;
                // The GPU-resident draw path, on trial here first (F11 = A/B against the CPU path).
                grass.gpuResident = true;
                grass.cullCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/3 - Scripts/World/GrassCull.compute");
                if (grass.cullCompute == null) Debug.LogWarning("[TutorialScene] GrassCull.compute not found — grass falls back to the CPU draw.");
            }
            var cats = inst.GetComponent<CatSpawner>();
            if (cats != null) cats.maxCats = 8;  // 44 never fills inside a 350 m box → it would scan forever
        }

        // HUD pieces that are scene objects in the gameplay scene (never seeded).
        var uiRoot = new GameObject("--- UI ---");
        BuildCrosshair(uiRoot.transform);
        var helmetCfg = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialSnapshots.HelmetPrefabPath);
        if (helmetCfg != null)
        {
            var cfg = (GameObject)PrefabUtility.InstantiatePrefab(helmetCfg, scene);
            cfg.transform.SetParent(uiRoot.transform, false);
        }
        else Debug.LogWarning("[TutorialScene] " + TutorialSnapshots.HelmetPrefabPath + " missing — run Tools ▸ Solar System ▸ Snapshot HelmetHudConfig Prefab, or the compass / boost / vitals clusters fall back to their old look.");

        // The player: the snapshot of the gameplay scene's player (audio clips,
        // PlayerSuitAudio, every equippable), in the pod at the parked pose so the
        // first frames already show the pod interior. Post stack kept as-is —
        // Planet Effects is what draws the atmosphere and ocean.
        var snapshot = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialSnapshots.PlayerPrefabPath);
        if (snapshot == null)
            Debug.LogWarning("[TutorialScene] " + TutorialSnapshots.PlayerPrefabPath + " missing — run Tools ▸ Solar System ▸ Snapshot Tutorial Player Prefab. Using the bare Player.prefab (no audio, no equippables).");
        var player = (GameObject)PrefabUtility.InstantiatePrefab(snapshot != null ? snapshot : playerPrefab, scene);
        player.name = "Player";
        player.SetActive(true);         // the gameplay scene keeps it inactive until GameSetUp
        var cam = player.GetComponentInChildren<Camera>(true);
        if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
        Transform pod = null;
        foreach (var t in shuttle.GetComponentsInChildren<Transform>(true))
            if (t.name == "StasisPod") { pod = t; break; }
        if (pod != null)
            player.transform.SetPositionAndRotation(pod.TransformPoint(new Vector3(0f, 1.02f, 0f)),
                                                    Quaternion.LookRotation(pod.forward, pod.up));
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

    // ── the planet's assets: Humble Abode's set, cloned (never shared) ──────

    /// Clones Humble Abode's Shape / Shading / Atmosphere / Ocean / terrain
    /// material into TutorialEarthDir the way PlanetGalleryBuilder.GetOrCreateDwarf
    /// does. Same seed (EarthShape uses only the seed) → Humble Abode's exact
    /// terrain, just 7.5× larger. Idempotent: an existing holder wins.
    static CelestialBodySettings GetOrCreateTutorialEarth()
    {
        string holderPath = TutorialEarthDir + "/Tutorial Earth.asset";
        var existing = AssetDatabase.LoadAssetAtPath<CelestialBodySettings>(holderPath);
        if (existing != null) return existing;

        var shape = AssetDatabase.LoadAssetAtPath<CelestialBodyShape>(AssetDatabase.GUIDToAssetPath(HAShapeGuid));
        var shading = AssetDatabase.LoadAssetAtPath<CelestialBodyShading>(AssetDatabase.GUIDToAssetPath(HAShadingGuid));
        var atmo = AssetDatabase.LoadAssetAtPath<AtmosphereSettings>(AssetDatabase.GUIDToAssetPath(HAAtmosphereGuid));
        var ocean = AssetDatabase.LoadAssetAtPath<OceanSettings>(AssetDatabase.GUIDToAssetPath(HAOceanGuid));
        if (shape == null || shading == null || atmo == null || ocean == null)
        {
            Debug.LogError("[TutorialScene] Humble Abode's Shape / Shading / Atmosphere / Ocean assets not found by GUID — nothing built.");
            return null;
        }
        EnsureFolder(TutorialEarthDir);

        var shapeClone = Object.Instantiate(shape);
        shapeClone.name = "Shape";
        shapeClone.randomize = false;          // EarthShape reads only the seed anyway
        AssetDatabase.CreateAsset(shapeClone, TutorialEarthDir + "/Shape.asset");

        var shadingClone = Object.Instantiate(shading);
        shadingClone.name = "Shading";
        if (shading.terrainMaterial != null)
        {
            string dstMat = TutorialEarthDir + "/Terrain.mat";
            if (AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(shading.terrainMaterial), dstMat))
                shadingClone.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(dstMat);
            else Debug.LogWarning("[TutorialScene] could not copy the terrain material; sharing Humble Abode's (edit-mode preview colours will fight).");
        }
        var atmoClone = Object.Instantiate(atmo);   // AtmosphereSettings caches per asset — a shared one configures only the first planet
        atmoClone.name = "Atmosphere";
        AssetDatabase.CreateAsset(atmoClone, TutorialEarthDir + "/Atmosphere.asset");
        shadingClone.hasAtmosphere = true;
        shadingClone.atmosphereSettings = atmoClone;
        var oceanClone = Object.Instantiate(ocean);
        oceanClone.name = "Ocean";
        AssetDatabase.CreateAsset(oceanClone, TutorialEarthDir + "/Ocean.asset");
        shadingClone.hasOcean = true;
        shadingClone.oceanSettings = oceanClone;
        AssetDatabase.CreateAsset(shadingClone, TutorialEarthDir + "/Shading.asset");

        var holder = ScriptableObject.CreateInstance<CelestialBodySettings>();
        holder.shape = shapeClone;
        holder.shading = shadingClone;
        AssetDatabase.CreateAsset(holder, holderPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[TutorialScene] Created " + holderPath + " (Humble Abode's generation set, cloned).");
        return holder;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    // ── spot search (read-only use of the shape's height compute) ───────────

    /// Samples the shape's heights (the same compute the generator runs) over a
    /// 9×9 grid across the box footprint plus a fine 3×3 patch at the centre,
    /// for a few thousand candidate directions, and returns the rotation that
    /// brings the best one to +Y. "Best" (Sam, round 5): a flat DRY centre to
    /// land on, water somewhere in the box (fishing), and a proper hill. If no
    /// candidate has all three, the requirements relax in order: hill, then
    /// water. hCentre is the height multiplier at the centre (≈1). Falls back
    /// to +Y if compute can't run in edit mode.
    static Quaternion FindTutorialSpot(CelestialBodyShape shape, float R, float halfBox, out float hCentre)
    {
        hCentre = 1f;
        if (shape == null || !ComputeHelper.CanRunEditModeCompute)
        {
            Debug.LogWarning("[TutorialScene] Edit-mode compute unavailable — planet left unrotated (box on +Y, terrain unknown).");
            return Quaternion.identity;
        }
        const int N = 4000, G = 9, C = 3;          // box grid, centre patch grid
        const float CentreHalf = 30f;              // the landing patch: ±30 m
        int per = G * G + C * C;
        var cands = new Vector3[N];
        var dirs = new Vector3[N * per];
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < N; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / N;
            float rr = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float a = golden * i;
            var c = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
            cands[i] = c;
            var t1 = Vector3.Cross(c, Mathf.Abs(c.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var t2 = Vector3.Cross(c, t1);
            int k = 0;
            for (int gx = 0; gx < G; gx++)
            for (int gy = 0; gy < G; gy++)
            {
                float ox = (gx / (G - 1f) * 2f - 1f) * halfBox, oy = (gy / (G - 1f) * 2f - 1f) * halfBox;
                dirs[i * per + k++] = (c * R + t1 * ox + t2 * oy).normalized;
            }
            for (int gx = 0; gx < C; gx++)
            for (int gy = 0; gy < C; gy++)
            {
                float ox = (gx / (C - 1f) * 2f - 1f) * CentreHalf, oy = (gy / (C - 1f) * 2f - 1f) * CentreHalf;
                dirs[i * per + k++] = (c * R + t1 * ox + t2 * oy).normalized;
            }
        }

        float[] h = null;
        ComputeBuffer vb = null;
        try
        {
            ComputeHelper.CreateStructuredBuffer<Vector3>(ref vb, new[] { Vector3.zero });   // CelestialBodyGenerator.Dummy(): first dispatch can read zeros
            shape.CalculateHeights(vb);
            ComputeHelper.CreateStructuredBuffer<Vector3>(ref vb, dirs);
            h = shape.CalculateHeights(vb);
        }
        catch (System.Exception e) { Debug.LogWarning("[TutorialScene] Height sampling failed (" + e.Message + ") — planet left unrotated."); }
        finally
        {
            shape.ReleaseBuffers();
            ComputeHelper.Release(vb);
        }
        if (h == null || h.Length != dirs.Length) return Quaternion.identity;

        // Thresholds in metres, as height multipliers.
        float dry = 1f + 6f / R;            // landing patch ≥ 6 m above sea everywhere
        float flatMax = 8f / R;             // landing patch height spread ≤ 8 m
        float wet = 1f - 3f / R;            // "water": ≥ 3 m under sea level
        float hillMin = 45f / R;            // "hill": ≥ 45 m above the centre somewhere in the box
        for (int pass = 0; pass < 3; pass++)   // 0: water + hill, 1: water only, 2: flat centre only
        {
            int best = -1; float bestScore = float.MinValue;
            for (int i = 0; i < N; i++)
            {
                int b = i * per;
                float cMin = float.MaxValue, cMax = float.MinValue;
                for (int k = G * G; k < per; k++) { float v = h[b + k]; if (v < cMin) cMin = v; if (v > cMax) cMax = v; }
                if (cMin < dry || cMax - cMin > flatMax) continue;
                float hc = h[b + G * G + (C * C) / 2];
                int wetCount = 0; float max = float.MinValue;
                for (int k = 0; k < G * G; k++) { float v = h[b + k]; if (v < wet) wetCount++; if (v > max) max = v; }
                float wetFrac = wetCount / (float)(G * G);
                float hill = max - hc;
                if (pass <= 1 && (wetFrac < 0.10f || wetFrac > 0.45f)) continue;   // some water, not a drowned box
                if (pass == 0 && hill < hillMin) continue;
                // Prefer ~25 % water and the taller hill; the centre flatness is a tiebreak.
                float score = -Mathf.Abs(wetFrac - 0.25f) * 4f + Mathf.Min(hill * R, 120f) / 120f - (cMax - cMin) * R * 0.02f;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) continue;
            int bb = best * per;
            hCentre = h[bb + G * G + (C * C) / 2];
            int wc = 0; float mx = float.MinValue, mn = float.MaxValue;
            for (int k = 0; k < G * G; k++) { float v = h[bb + k]; if (v < wet) wc++; if (v > mx) mx = v; if (v < mn) mn = v; }
            Debug.Log($"[TutorialScene] Spot (pass {pass}): candidate {best}; centre {(hCentre - 1f) * R:0.0} m above sea; " +
                      $"water on {100f * wc / (G * G):0}% of the box; highest point {(mx - hCentre) * R:0.0} m above the centre, lowest {(mn - hCentre) * R:0.0} m.");
            return Quaternion.FromToRotation(cands[best], Vector3.up);
        }
        Debug.LogWarning("[TutorialScene] No spot with a flat dry centre found — planet left unrotated.");
        return Quaternion.identity;
    }

    // ── pieces ──────────────────────────────────────────────────────────────

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
        cb.railPeriod = 0f;
        cb.bodyName = name;              // CelestialBody.OnValidate names the GameObject after bodyName
        go.name = name;                  // AddComponent fired OnValidate with the default name; set it again
        cb.RecalculateMass();
        return go;
    }

    static void MakePane(Transform parent, Material mat, string name, Vector3 worldPos, Quaternion worldRot, float width, float height)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.layer = BodyLayer;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        go.transform.localScale = new Vector3(width, height, 1f);
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

    static Material LoadOrCreateRainMaterial(string path, float aspect, bool radial)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(RainShader)) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetFloat("_Aspect", aspect);
        mat.SetFloat("_Radial", radial ? 1f : 0f);
        EditorUtility.SetDirty(mat);
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
