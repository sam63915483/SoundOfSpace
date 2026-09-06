using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools ▸ Planet Gallery ▸ Build Scene — a fly-around test scene with every
/// planet in a line: the four in-game worlds (Icey Twin, Fiery Twin, Humble
/// Abode, Cyclops) followed by sixteen NEW dwarf planets generated from Sebastian
/// Lague's procedural-planet settings (2026-09-06, Sam's ask: "can we actually
/// make cool planets?" for filling the gaps between the big orbits).
///
/// The scene is built ADDITIVELY and closed again, so whatever scene is open
/// (normally 1.6.7.7.7) is never touched (if the gallery ITSELF is the open
/// scene it is rebuilt in place instead), and nothing here references the
/// gameplay scene. It mirrors the game's own runtime chain exactly —
/// CelestialBody + BodyPlaceholder(bodySettings) → SolarSystemSpawner builds
/// the terrain at Play, LODHandler swaps LODs, the camera carries the same
/// Planet Effects / Bloom / FXAA post stack as the Player prefab — so what you
/// see is what the game would render. There is NO n-body sim here: bodies are
/// kinematic and sit where they're placed.
///
/// Dwarf planet assets are created ONCE under
/// "Solar System/Dwarf Planets/<name>/" (Shape, Shading, Atmosphere, Ocean,
/// Terrain.mat, holder) by cloning the in-game planets' settings and changing
/// seeds / a few knobs. Re-running Build keeps existing dwarf assets (so your
/// Inspector tweaks survive); "Reset Dwarf Assets" wipes and regenerates them.
/// The generation CODE (trap #2) is untouched — this only creates data assets.
///
/// Every atmosphere asset is per-planet on purpose: AtmosphereSettings caches
/// a "settings up to date" flag per asset, so a shared one would only ever
/// configure the first planet that asked (why each in-game planet has its own).
/// Same for terrain materials: edit-mode preview writes colours straight into
/// the material asset, so sharing one makes two planets fight over it.
/// </summary>
public static class PlanetGalleryBuilder
{
    const string SceneDir     = "Assets/4 - Scenes";
    const string ScenePath    = SceneDir + "/PlanetGallery.unity";
    const string SunMatPath   = SceneDir + "/PlanetGallery_Sun.mat";
    const string ManifestPath = "docs/PLANET_GALLERY.md";
    const string SolarDir     = "Assets/5 - External Imports/Celestial Body/Solar System";
    const string DwarfDir     = SolarDir + "/Dwarf Planets";

    // Things the gameplay scene / Player prefab already use, by GUID so this
    // file has no path into packs that might move.
    const string SkyboxMatGuid      = "e3d301707e23ccd4e84049a21e148e54"; // ESO Milky Way
    const string PlaceholderMatGuid = "ac038ee5893cf4c648f7a602051dfc36"; // Green.mat — the crude edit-mode sphere the main scene's placeholders use
    static readonly string[] PostEffectGuids =
    {
        "2a0830d1f8e1c4c019b5757c93f3297a", // Planet Effects (atmosphere + ocean)
        "84b638ca20bd24edf9d42d3686bb3b4d", // Bloom
        "9670340ab10df4930aaaeecbce049971", // FXAA
    };

    // Same LOD resolutions as the gameplay scene's SolarSystemSpawner (300/100/50).
    // Collider is 50 (game: 200): nobody walks here, and bodies ≥150 radius reuse
    // their LOD0 mesh as collider anyway (CelestialBodyGenerator.HighResColliderMinRadius).
    const int Lod0 = 300, Lod1 = 100, Lod2 = 50, ColliderRes = 50;

    enum Template { Earth, Alien, Moat, Shattered, Moon }

    class TemplateSrc { public string dir, shape, shading, atmo, ocean; }

    static readonly Dictionary<Template, TemplateSrc> Templates = new Dictionary<Template, TemplateSrc>
    {
        { Template.Earth,     new TemplateSrc { dir = SolarDir + "/Humble Abode",               shape = "Shape", shading = "Shading", atmo = "Atmosphere",         ocean = "Ocean" } },
        { Template.Alien,     new TemplateSrc { dir = SolarDir + "/Cyclops",                    shape = "Shape", shading = "Shading", atmo = "Atmosphere Cyclops", ocean = "Ocean 1" } },
        { Template.Moat,      new TemplateSrc { dir = SolarDir + "/Binary System/Fiery Twin",   shape = "Shape", shading = "Shading", atmo = "Atmosphere",         ocean = "Ocean" } },
        { Template.Shattered, new TemplateSrc { dir = SolarDir + "/Binary System/Icey Twin",    shape = "Shape", shading = "Shading", atmo = "Atmosphere",         ocean = "Ocean" } },
        { Template.Moon,      new TemplateSrc { dir = SolarDir + "/Humble Abode/Constant Companion", shape = "Shape", shading = "Shading", atmo = null,           ocean = null } },
    };

    class DwarfDef
    {
        public string name;
        public float radius;
        public Template template;
        public int seed;
        public bool atmosphere, ocean;
        public bool keepColours;   // false = random palette from the seed; true = the template's own hand-picked colours (Humble Abode lookalikes)
        public string note;
        public System.Action<CelestialBodyShape> shape;
        public System.Action<CelestialBodyShading> shading;
        public System.Action<AtmosphereSettings> atmo;
    }

    // Ten dwarfs, grouped by recipe so like sits next to like in the line.
    // Radii 20–75 (Humble Abode is 200; Sam halved the first batch 2026-09-06 and asked for
    // extra-small Humble Abode lookalikes — the "Abode" set keeps HA's exact colours). Every one starts from a planet that
    // already looks good in-game and changes only seeds + a couple of knobs.
    static readonly DwarfDef[] Dwarfs =
    {
        new DwarfDef { name = "Puddle", radius = 50, template = Template.Earth, seed = 3141, atmosphere = true, ocean = true,
            note = "Earth-like archipelago: continents pushed below the waterline (continentNoise.verticalShift -0.95).",
            shape = s => { var e = (EarthShape) s; e.continentNoise.verticalShift = -0.95f; } },
        new DwarfDef { name = "Anvil", radius = 70, template = Template.Earth, seed = 2718, atmosphere = true, ocean = true,
            note = "Earth-like, mostly land with taller ranges (verticalShift -0.35, ridge elevation 12).",
            shape = s => { var e = (EarthShape) s; e.continentNoise.verticalShift = -0.35f; e.ridgeNoise.elevation = 12f; },
            atmo = a => { a.wavelengths = new Vector3 (640, 540, 470); } },
        new DwarfDef { name = "Dustbowl", radius = 75, template = Template.Earth, seed = 4444, atmosphere = true, ocean = false,
            note = "Dry world: ocean OFF so the seabeds show, all land (verticalShift 0.1), thin warm haze.",
            shape = s => { var e = (EarthShape) s; e.continentNoise.verticalShift = 0.1f; },
            atmo = a => { a.wavelengths = new Vector3 (700, 610, 520); a.scatteringStrength = 12f; a.atmosphereScale = 0.25f; } },

        new DwarfDef { name = "Ember", radius = 60, template = Template.Alien, seed = 2024, atmosphere = true, ocean = true,
            note = "Cyclops recipe, new seed, sky tuned red (red scatters most).",
            atmo = a => { a.wavelengths = new Vector3 (460, 560, 700); } },
        new DwarfDef { name = "Glassy", radius = 45, template = Template.Alien, seed = 4242, atmosphere = false, ocean = true,
            note = "Airless alien ocean world with big craters (250 craters, up to 0.2), ocean level 0.95.",
            shape = s => { var a = (AlienShape) s; a.craterSettings.numCraters = 250; a.craterSettings.craterSizeMinMax = new Vector2 (0.02f, 0.2f); },
            shading = sh => { sh.oceanLevel = 0.95f; } },

        new DwarfDef { name = "Slag", radius = 55, template = Template.Moat, seed = 616, atmosphere = true, ocean = true,
            note = "Fiery Twin recipe (Moat shape), new seed → new continents, craters and colours." },
        new DwarfDef { name = "Shard", radius = 40, template = Template.Shattered, seed = 1999, atmosphere = true, ocean = true,
            note = "Icey Twin recipe (Shattered shape), new seed, ocean level 0.9.",
            shading = sh => { sh.oceanLevel = 0.9f; } },

        new DwarfDef { name = "Pebble", radius = 30, template = Template.Moon, seed = 4121, atmosphere = false, ocean = false,
            note = "Moon recipe, peppered with 1400 small craters.",
            shape = s => { var m = (MoonShape) s; m.craterSettings.numCraters = 1400; m.craterSettings.craterSizeMinMax = new Vector2 (0.008f, 0.05f); } },
        new DwarfDef { name = "Bruise", radius = 42, template = Template.Moon, seed = 777, atmosphere = false, ocean = false,
            note = "Moon recipe, a few huge craters (90 craters, up to 0.3 of the radius).",
            shape = s => { var m = (MoonShape) s; m.craterSettings.numCraters = 90; m.craterSettings.craterSizeMinMax = new Vector2 (0.04f, 0.3f); } },
        new DwarfDef { name = "Lantern", radius = 35, template = Template.Moon, seed = 31337, atmosphere = false, ocean = false,
            note = "Moon recipe with shape.randomize ON — Sebastian's own random crater/ridge roll for this seed.",
            shape = s => { s.randomize = true; } },

        // Extra-small Humble Abode lookalikes: HA's shape + HA's own colours
        // (keepColours), only the seed and how much land pokes above the sea
        // change. Radii 20–45 — a 25 m world is a 160 m walk around.
        new DwarfDef { name = "Hearth", radius = 45, template = Template.Earth, seed = 101, atmosphere = true, ocean = true, keepColours = true,
            note = "Humble Abode lookalike, HA colours, HA land/sea balance, new seed." },
        new DwarfDef { name = "Porch", radius = 40, template = Template.Earth, seed = 202, atmosphere = true, ocean = true, keepColours = true,
            note = "Humble Abode lookalike, a little more sea (verticalShift -0.75).",
            shape = s => { var e = (EarthShape) s; e.continentNoise.verticalShift = -0.75f; } },
        new DwarfDef { name = "Attic", radius = 35, template = Template.Earth, seed = 303, atmosphere = true, ocean = true, keepColours = true,
            note = "Humble Abode lookalike, a little more land (verticalShift -0.5).",
            shape = s => { var e = (EarthShape) s; e.continentNoise.verticalShift = -0.5f; } },
        new DwarfDef { name = "Nook", radius = 30, template = Template.Earth, seed = 404, atmosphere = true, ocean = true, keepColours = true,
            note = "Humble Abode lookalike, HA balance, new seed." },
        new DwarfDef { name = "Shed", radius = 25, template = Template.Earth, seed = 505, atmosphere = true, ocean = true, keepColours = true,
            note = "Humble Abode lookalike, gentler mountains (ridge elevation 6).",
            shape = s => { var e = (EarthShape) s; e.ridgeNoise.elevation = 6f; } },
        new DwarfDef { name = "Cellar", radius = 20, template = Template.Earth, seed = 606, atmosphere = true, ocean = true, keepColours = true,
            note = "Humble Abode lookalike, the smallest — one island continent (verticalShift -0.85).",
            shape = s => { var e = (EarthShape) s; e.continentNoise.verticalShift = -0.85f; } },
    };

    class Entry
    {
        public string name, kind, note, assetFolder;
        public float radius, gravity;
        public int seed;
        public bool inGame, atmosphere, ocean;
        public CelestialBodySettings settings;
        public Vector3 pos;
    }

    // ── menu ────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Planet Gallery/Build Scene")]
    public static void Build()
    {
        var entries = new List<Entry>();
        entries.Add(Main("Icey Twin",    "/Binary System/Icey Twin/BinaryB.asset",  300, 10, "Shattered"));
        entries.Add(Main("Fiery Twin",   "/Binary System/Fiery Twin/BinaryA.asset", 300, 10, "Moat"));
        entries.Add(Main("Humble Abode", "/Humble Abode/Humble Abode.asset",        200,  8, "Earth-like"));
        entries.Add(Main("Cyclops",      "/Cyclops/Cyclops.asset",                  500, 14, "Alien"));
        foreach (var e in entries)
            if (e.settings == null) { Debug.LogError($"[PlanetGallery] Missing settings asset for {e.name} — nothing built."); return; }

        int created = 0;
        foreach (var d in Dwarfs)
        {
            var settings = GetOrCreateDwarf(d, out bool madeNow);
            if (madeNow) created++;
            entries.Add(new Entry
            {
                name = d.name, kind = KindName(d.template), note = d.note, assetFolder = DwarfDir + "/" + d.name,
                radius = d.radius, gravity = Mathf.Round(8f * d.radius / 200f * 2f) / 2f, seed = d.seed,
                atmosphere = d.atmosphere, ocean = d.ocean, settings = settings,
            });
        }
        AssetDatabase.SaveAssets();

        // A straight line along +X. Gap between surfaces = 2.5× the larger
        // radius (min 400) so atmospheres (up to ~1.6× radius) never overlap.
        float x = 0f, prevR = -1f;
        foreach (var e in entries)
        {
            if (prevR >= 0f) x += prevR + Mathf.Max(400f, 2.5f * Mathf.Max(prevR, e.radius)) + e.radius;
            e.pos = new Vector3(x, 0f, 0f);
            prevR = e.radius;
        }

        if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets", "4 - Scenes");
        var prevActive = SceneManager.GetActiveScene();
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded)
        {
            // The gallery itself is open (Sam is looking at it): rebuild it IN
            // PLACE rather than saving over an open scene from a second copy.
            SceneManager.SetActiveScene(alreadyOpen);
            try
            {
                foreach (var go in alreadyOpen.GetRootGameObjects()) Object.DestroyImmediate(go);
                Populate(alreadyOpen, entries);
                EditorSceneManager.MarkSceneDirty(alreadyOpen);
                if (!EditorSceneManager.SaveScene(alreadyOpen))
                    Debug.LogError("[PlanetGallery] SaveScene failed for " + ScenePath);
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
                Populate(scene, entries);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    Debug.LogError("[PlanetGallery] SaveScene failed for " + ScenePath);
            }
            finally
            {
                if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        WriteManifest(entries);
        AssetDatabase.Refresh();
        Debug.Log($"[PlanetGallery] Built {entries.Count} bodies ({created} dwarf asset sets newly created, {Dwarfs.Length - created} kept) → {ScenePath}. " +
                  $"List + knobs: {ManifestPath}. Open via Tools ▸ Planet Gallery ▸ Open Scene, press Play, fly with WASD/QE + mouse, Shift = fast, wheel = speed.");
    }

    [MenuItem("Tools/Planet Gallery/Open Scene")]
    public static void Open()
    {
        if (!File.Exists(ScenePath)) { Debug.LogWarning("[PlanetGallery] Scene not built yet — run Build Scene first."); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    [MenuItem("Tools/Planet Gallery/Reset Dwarf Assets (regenerate)")]
    public static void ResetDwarfs()
    {
        if (!AssetDatabase.IsValidFolder(DwarfDir)) { Build(); return; }
        if (!EditorUtility.DisplayDialog("Planet Gallery",
            "Delete every dwarf planet asset set under\n" + DwarfDir + "\nand regenerate them from the recipes? Any Inspector tweaks you made to them are lost.",
            "Delete and regenerate", "Cancel")) return;
        AssetDatabase.DeleteAsset(DwarfDir);
        AssetDatabase.Refresh();
        Build();
    }

    // ── assets ──────────────────────────────────────────────────────────────

    static Entry Main(string name, string holderRel, float radius, float gravity, string kind)
    {
        string path = SolarDir + holderRel;
        return new Entry
        {
            name = name, kind = kind, inGame = true, radius = radius, gravity = gravity,
            assetFolder = Path.GetDirectoryName(path).Replace('\\', '/'),
            settings = AssetDatabase.LoadAssetAtPath<CelestialBodySettings>(path),
            note = "In the game today, unchanged — here for scale.",
        };
    }

    static string KindName(Template t)
    {
        switch (t)
        {
            case Template.Earth: return "Earth-like";
            case Template.Alien: return "Alien";
            case Template.Moat: return "Moat";
            case Template.Shattered: return "Shattered";
            default: return "Moon";
        }
    }

    /// Existing holder wins (keeps Sam's tweaks). Otherwise clone the template
    /// planet's Shape / Shading / Atmosphere / Ocean / terrain material into
    /// the dwarf's own folder, apply the recipe, and write a settings holder.
    static CelestialBodySettings GetOrCreateDwarf(DwarfDef d, out bool created)
    {
        created = false;
        string folder = DwarfDir + "/" + d.name;
        string holderPath = folder + "/" + d.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<CelestialBodySettings>(holderPath);
        if (existing != null) return existing;

        EnsureFolder(folder);
        var src = Templates[d.template];

        // Shape — clone, then recipe.
        var shape = LoadTemplate<CelestialBodyShape>(src.dir + "/" + src.shape + ".asset");
        var shapeClone = Object.Instantiate(shape);
        shapeClone.name = "Shape";
        shapeClone.randomize = false;
        shapeClone.seed = d.seed;
        if (d.shape != null) d.shape(shapeClone);
        AssetDatabase.CreateAsset(shapeClone, folder + "/Shape.asset");

        // Shading — clone; own material; own atmosphere/ocean; recipe.
        var shading = LoadTemplate<CelestialBodyShading>(src.dir + "/" + src.shading + ".asset");
        var shadingClone = Object.Instantiate(shading);
        shadingClone.name = "Shading";
        shadingClone.randomize = !d.keepColours;   // random palette from the seed (Earth/Alien/Moon shadings all support it), unless the recipe wants the template's colours
        shadingClone.seed = d.seed;

        if (shading.terrainMaterial != null)
        {
            string srcMat = AssetDatabase.GetAssetPath(shading.terrainMaterial);
            string dstMat = folder + "/Terrain.mat";
            if (AssetDatabase.CopyAsset(srcMat, dstMat))
                shadingClone.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(dstMat);
            else
                Debug.LogWarning($"[PlanetGallery] {d.name}: could not copy terrain material {srcMat}; sharing the template's.");
        }

        if (d.atmosphere && src.atmo != null)
        {
            var atmo = Object.Instantiate(LoadTemplate<AtmosphereSettings>(src.dir + "/" + src.atmo + ".asset"));
            atmo.name = "Atmosphere";
            if (d.atmo != null) d.atmo(atmo);
            AssetDatabase.CreateAsset(atmo, folder + "/Atmosphere.asset");
            shadingClone.hasAtmosphere = true;
            shadingClone.atmosphereSettings = atmo;
        }
        else
        {
            shadingClone.hasAtmosphere = false;
        }

        if (d.ocean && src.ocean != null)
        {
            var ocean = Object.Instantiate(LoadTemplate<OceanSettings>(src.dir + "/" + src.ocean + ".asset"));
            ocean.name = "Ocean";
            AssetDatabase.CreateAsset(ocean, folder + "/Ocean.asset");
            shadingClone.hasOcean = true;
            shadingClone.oceanSettings = ocean;
        }
        else
        {
            shadingClone.hasOcean = false;
        }

        if (d.shading != null) d.shading(shadingClone);
        AssetDatabase.CreateAsset(shadingClone, folder + "/Shading.asset");

        var holder = ScriptableObject.CreateInstance<CelestialBodySettings>();
        holder.shape = shapeClone;
        holder.shading = shadingClone;
        AssetDatabase.CreateAsset(holder, holderPath);

        created = true;
        return holder;
    }

    static T LoadTemplate<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) throw new FileNotFoundException("[PlanetGallery] template asset missing: " + path);
        return a;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ── scene ───────────────────────────────────────────────────────────────

    static void Populate(Scene scene, List<Entry> entries)
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var root = new GameObject("--- Planet Gallery ---").transform;
        int bodyLayer = LayerMask.NameToLayer("Body");
        if (bodyLayer < 0) bodyLayer = 0;

        // Managers — the exact pair the gameplay scene uses to turn placeholders into terrain.
        var managers = new GameObject("Managers");
        managers.transform.SetParent(root, false);
        var spawner = managers.AddComponent<SolarSystemSpawner>();
        spawner.resolutionSettings = new CelestialBodyGenerator.ResolutionSettings { lod0 = Lod0, lod1 = Lod1, lod2 = Lod2, collider = ColliderRes };
        managers.AddComponent<LODHandler>();

        float lineEnd = entries[entries.Count - 1].pos.x + entries[entries.Count - 1].radius;

        // Sun: a directional light that the game's SunShadowCaster keeps aimed
        // at the camera (that's how the atmosphere/ocean shaders learn where
        // the sun is), far enough away that every planet is lit the same way,
        // plus a plain bright ball so you can see where it is.
        var sunPos = new Vector3(lineEnd * 0.5f, 15000f, -45000f);
        var sunGo = new GameObject("Sun Shadow Caster");
        sunGo.transform.SetParent(root, false);
        sunGo.transform.position = sunPos;
        sunGo.transform.LookAt(new Vector3(lineEnd * 0.5f, 0f, 0f));
        var light = sunGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.999387f, 1f, 0.8915094f);   // main scene's sun colour
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 1f;
        sunGo.AddComponent<SunShadowCaster>();

        var sunBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sunBall.name = "Sun (visual only)";
        sunBall.transform.SetParent(root, false);
        sunBall.transform.position = sunPos;
        sunBall.transform.localScale = Vector3.one * 2500f;
        Object.DestroyImmediate(sunBall.GetComponent<Collider>());
        sunBall.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateUnlit(SunMatPath, new Color(1f, 0.93f, 0.7f));

        // Lighting like the gameplay scene: Milky Way skybox, no ambient
        // (night sides are lit by the atmosphere post, not by ambient).
        var skybox = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SkyboxMatGuid));
        if (skybox != null) RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.fog = false;
        RenderSettings.sun = light;

        // Bodies.
        var placeholderMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(PlaceholderMatGuid));
        var bodies = new GameObject("Bodies").transform;
        bodies.SetParent(root, false);
        var labels = new GameObject("Labels").transform;
        labels.SetParent(root, false);
        foreach (var e in entries)
        {
            MakeBody(e, bodies, bodyLayer, placeholderMat);

            float cs = Mathf.Max(1.2f, e.radius * 0.02f);
            string title = e.name.ToUpperInvariant();
            string sub = e.inGame
                ? $"r {e.radius:0}  ·  {e.kind}  ·  in the game"
                : $"r {e.radius:0}  ·  {e.kind}  ·  seed {e.seed}" +
                  (e.atmosphere ? "  ·  atmosphere" : "") + (e.ocean ? "  ·  ocean" : "");
            var top = e.pos + Vector3.up * (e.radius * 1.8f + 30f);
            MakeLabel(labels, font, top, title, cs, new Color(1f, 0.95f, 0.7f), TextAnchor.LowerCenter);
            MakeLabel(labels, font, top - Vector3.up * (cs * 2f), sub, cs * 0.45f, new Color(0.75f, 0.85f, 1f), TextAnchor.UpperCenter);
            if (!e.inGame)
                MakeLabel(labels, font, top - Vector3.up * (cs * 6f), "Dwarf Planets/" + e.name, cs * 0.35f, new Color(0.6f, 0.6f, 0.6f), TextAnchor.UpperCenter);
        }

        // Camera: same post stack as the Player prefab's camera, tagged
        // MainCamera (the generator, LOD handler and sun caster all look it up).
        var camGo = new GameObject("Gallery Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(root, false);
        var first = entries[0];
        camGo.transform.position = first.pos + new Vector3(-first.radius * 2.6f, first.radius * 1.4f, -first.radius * 4.5f);
        camGo.transform.LookAt(first.pos + Vector3.right * first.radius * 2.5f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 150000f;
        cam.fieldOfView = 60f;
        camGo.AddComponent<AudioListener>();
        var post = camGo.AddComponent<CustomPostProcessing>();
        var fx = new List<PostProcessingEffect>();
        foreach (var guid in PostEffectGuids)
        {
            var effect = AssetDatabase.LoadAssetAtPath<PostProcessingEffect>(AssetDatabase.GUIDToAssetPath(guid));
            if (effect != null) fx.Add(effect); else Debug.LogWarning("[PlanetGallery] post effect asset missing for GUID " + guid);
        }
        post.effects = fx.ToArray();
        var fly = camGo.AddComponent<TreeGalleryFlyCam>();
        fly.moveSpeed = 400f;
        fly.fastMultiplier = 5f;
        fly.minSpeed = 5f;
        fly.maxSpeed = 12000f;
        camGo.AddComponent<GallerySceneQuiet>();

        // README, floating under the first planet where the camera starts.
        var help = new GameObject("README (select me)");
        help.transform.SetParent(root, false);
        MakeLabel(help.transform, font, first.pos + new Vector3(0f, -(first.radius * 1.6f), 0f),
                  "PLANET GALLERY\n" +
                  "Play → WASD / Q E fly, Shift = fast, wheel = speed (5 … 12,000 m/s), Esc frees the mouse\n" +
                  "Left→right: Icey Twin · Fiery Twin · Humble Abode · Cyclops · then 10 dwarf planets\n" +
                  "Tweak a dwarf: its Shape / Shading / Atmosphere assets live in Solar System/Dwarf Planets/<name>\n" +
                  "See terrain WITHOUT pressing Play: Tools ▸ Planet Preview ▸ Show All Bodies (then Clear)\n" +
                  "Rebuild: Tools ▸ Planet Gallery ▸ Build Scene (keeps your dwarf tweaks)",
                  2.2f, Color.white, TextAnchor.UpperCenter);
    }

    static void MakeBody(Entry e, Transform parent, int layer, Material placeholderMat)
    {
        var go = new GameObject(e.name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = e.pos;

        // Kinematic: there is no NBodySimulation here, and Unity's own gravity
        // must not drop the planet.
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var cb = go.AddComponent<CelestialBody>();
        cb.bodyType = CelestialBody.BodyType.Planet;
        cb.radius = e.radius;
        cb.surfaceGravity = e.gravity;
        cb.bodyName = e.name;
        cb.isPinned = true;

        var ph = new GameObject("Placeholder");
        ph.layer = layer;
        ph.transform.SetParent(go.transform, false);
        var bp = ph.AddComponent<BodyPlaceholder>();
        bp.terrainResolution = 50;
        bp.material = placeholderMat;
        bp.useBodySettings = true;
        bp.bodySettings = e.settings;
        bp.generateCollider = false;
    }

    /// TextMesh readable from the -Z side (where the camera starts).
    static TextMesh MakeLabel(Transform parent, Font font, Vector3 pos, string text, float characterSize, Color color, TextAnchor anchor)
    {
        var go = new GameObject("Label: " + text.Split('\n')[0]);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = characterSize;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.fontStyle = FontStyle.Bold;
        if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return tm;
    }

    static Material LoadOrCreateUnlit(string path, Color color)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Unlit/Color"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── manifest ────────────────────────────────────────────────────────────

    static void WriteManifest(List<Entry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Planet Gallery");
        sb.AppendLine();
        sb.AppendLine($"Generated by `Tools ▸ Planet Gallery ▸ Build Scene` on {System.DateTime.Now:yyyy-MM-dd HH:mm}. Scene: `{ScenePath}`.");
        sb.AppendLine("A straight line of planets along +X: the four in-game worlds first (for scale), then ten new dwarf planets, then six extra-small Humble Abode lookalikes (HA's own colours, new seeds). Press Play, fly (WASD / Q E, Shift fast, wheel = speed, Esc frees the mouse).");
        sb.AppendLine("Nothing here touches `1.6.7.7.7.unity`; the in-game planets are referenced by their existing settings assets and are not modified.");
        sb.AppendLine();
        sb.AppendLine("| # | Name | Radius | Gravity | Recipe | Seed | Atmo | Ocean | X | Assets | Note |");
        sb.AppendLine("|---|------|--------|---------|--------|------|------|-------|---|--------|------|");
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            sb.AppendLine($"| {i + 1} | {e.name} | {e.radius:0} | {e.gravity:0.#} | {e.kind} | {(e.inGame ? "—" : e.seed.ToString())} | " +
                          $"{(e.inGame ? "as game" : (e.atmosphere ? "yes" : "no"))} | {(e.inGame ? "as game" : (e.ocean ? "yes" : "no"))} | {e.pos.x:0} | `{e.assetFolder}` | {e.note} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Changing a dwarf planet");
        sb.AppendLine();
        sb.AppendLine("- Each dwarf owns a full settings set in `" + DwarfDir + "/<name>/`: `Shape.asset` (terrain noise, craters, seed), `Shading.asset` (colours, ocean level, atmosphere/ocean on-off, seed), `Atmosphere.asset` (sky colour = `wavelengths`, thickness = `atmosphereScale`, `scatteringStrength`), `Ocean.asset`, `Terrain.mat`, and `<name>.asset` (the holder the scene points at).");
        sb.AppendLine("- Edit them in the Inspector like any other planet. To see the result without Play: open the gallery, `Tools ▸ Planet Preview ▸ Show All Bodies` (edit-mode terrain; `Clear` when done). With a preview showing, selecting a body's `Body Generator (Preview)` gives you the Generate / Randomize Shape / Randomize Shading buttons.");
        sb.AppendLine("- `Build Scene` again keeps every existing dwarf asset (your tweaks survive) and only re-lays the scene. `Reset Dwarf Assets` deletes the folder and regenerates from the recipes in `PlanetGalleryBuilder.cs` — edit the recipe table there to add an eleventh.");
        sb.AppendLine();
        sb.AppendLine("## Putting one in the real solar system");
        sb.AppendLine();
        sb.AppendLine("`Tools ▸ Solar System ▸ Add Dwarf Planets to 1.6.7.7.7` (DwarfPlanetInstaller.cs) does this for the eight Sam picked — Puddle, Hearth, Anvil between the twins and Humble Abode; Ember, Slag, Shard, Pebble, Bruise between Humble Abode and Cyclops — each on its own clockwork rail; edit its table to change orbits, day lengths or the set. The in-game chain is the same one this scene uses: a `CelestialBody` (radius, surface gravity, `railPeriod` for its orbit) with a child `BodyPlaceholder` whose `bodySettings` points at the dwarf's holder asset; `SolarSystemSpawner` builds the terrain at load. Orbits today: twins at ~6,000 from the sun, Humble Abode at ~12,250, Cyclops at ~24,900 — the gaps fit several dwarfs on their own rails. Things to check before adding one for real: the save system's body list, the LOD handler cost (each planet keeps a ~360k-vertex LOD0 mesh), oxygen/tree systems that assume specific planets, and atmosphere overlap (~1.3–1.6× radius).");
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));
        File.WriteAllText(ManifestPath, sb.ToString());
    }
}
