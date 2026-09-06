using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools ▸ Solar System ▸ Add Dwarf Planets — puts the eight dwarf planets Sam
/// picked from the Planet Gallery (2026-09-06) into the gameplay scene
/// 1.6.7.7.7, on clockwork rails in the two big orbital gaps:
///
///   twins (6,019) ── Puddle · Hearth · Anvil · Ember · Slag · Shard · Pebble · Bruise ── Humble Abode (12,247)
///   (all eight in the inner gap since 2026-09-06; Cyclops at 24,906 stays the far world)
///
/// Each one is a clone of the Cyclops GameObject (the simplest in-game planet:
/// CelestialBody + kinematic Rigidbody, a "Mesh Holder" BodyPlaceholder scaled
/// to the radius, a "waterline" Water trigger, an orbit LineRenderer) with its
/// name, radius, gravity, orbit, rail period and settings asset swapped. That
/// keeps every downstream system happy without touching any of them:
///   • SolarSystemSpawner builds the terrain from the placeholder at load;
///   • the shuttle NAV app lists every bodyType == Planet automatically;
///   • saves match bodies by name (new names are simply new entries);
///   • trees / mushrooms / crystals / alien NPCs / enemies spawn on every
///     non-excluded body — the two airless rocks are excluded from TREES here;
///   • LOD, atmosphere/ocean post, eclipse gate, lens flares, map, wind,
///     multiplayer sync all iterate NBodySimulation.Bodies.
///
/// Idempotent: a dwarf whose name already exists in the scene is skipped, so
/// re-running after a tweak is safe. "Remove Dwarf Planets" undoes everything.
/// Works whether or not 1.6.7.7.7 is the open scene (it is opened additively
/// and closed again otherwise, so the Planet Gallery can stay open).
///
/// Orbit plane and direction follow the existing planets: the XY plane, moving
/// the same way round as Cyclops. Rails only need the velocity for its
/// DIRECTION (it picks the orbit plane); the magnitude is set to the true
/// circular speed for tidiness. railPeriod IS the planet's day length.
/// </summary>
public static class DwarfPlanetInstaller
{
    const string MainScenePath = "Assets/1.6.7.7.7.unity";
    const string DwarfDir      = "Assets/5 - External Imports/Celestial Body/Solar System/Dwarf Planets";
    const string TemplateBody  = "Cyclops";
    const string SunBody       = "Sun";

    struct Def
    {
        public string name;
        public float radius, gravity, orbitRadius, angleDeg, railPeriod;
        public bool ocean;
        public Def(string n, float r, float g, float orbit, float angle, float period, bool ocean)
        { name = n; radius = r; gravity = g; orbitRadius = orbit; angleDeg = angle; railPeriod = period; this.ocean = ocean; }
    }

    // Orbit radii: 6,900 keeps ~200 m between Puddle and the twins' atmosphere
    // (twins at 6,019, sky out to ~6,500); 10,750 keeps ~1,000 clear of Humble
    // Abode's moon (Constant Companion swings in to ~11,774). Angles spread
    // them round the sun instead of the -X line the big four sit on. Day
    // lengths interpolate between the twins' 600 s and HA's 900 s and are the
    // knob to change if a dwarf's day feels wrong.
    static readonly Def[] Dwarfs =
    {
        // 2026-09-06 (Sam): all eight in the twins → Humble Abode gap so the
        // inner system reads dense and Cyclops stays far. Rails 550 apart:
        // concentric circles never meet, and with different periods the
        // dwarfs lap each other with 550 m between orbits — close flybys,
        // never a collision, never a gravity problem (rails ignore it).
        new Def("Puddle", 50f, 3f,   6900f,  35f,  640f, true),
        new Def("Hearth", 45f, 3f,   7450f, 150f,  670f, true),
        new Def("Anvil",  70f, 4f,   8000f, 250f,  695f, true),
        new Def("Ember",  60f, 3.5f, 8550f,  80f,  720f, true),
        new Def("Slag",   55f, 3.5f, 9100f, 200f,  750f, true),
        new Def("Shard",  40f, 2.5f, 9650f, 320f,  775f, true),
        new Def("Pebble", 30f, 2f,  10200f, 120f,  800f, false),
        new Def("Bruise", 42f, 2.5f,10750f,  20f,  830f, false),
    };

    // Airless rocks get no trees (same rule as the three moons).
    static readonly string[] NoTrees = { "Pebble", "Bruise" };

    [MenuItem("Tools/Solar System/Add Dwarf Planets to 1.6.7.7.7")]
    public static void Install()
    {
        var (scene, opened) = GetMainScene();
        if (!scene.IsValid()) return;
        try
        {
            var bodies = FindBodies(scene);
            CelestialBody template = Find(bodies, TemplateBody), sun = Find(bodies, SunBody);
            if (template == null || sun == null)
            {
                Debug.LogError($"[DwarfPlanets] Need '{TemplateBody}' and '{SunBody}' in {MainScenePath} — found template={template != null} sun={sun != null}.");
                return;
            }
            Transform organizer = template.transform.parent;
            var sb = new StringBuilder("[DwarfPlanets] ");
            int added = 0, skipped = 0;

            foreach (var d in Dwarfs)
            {
                if (Find(bodies, d.name) != null) { skipped++; sb.Append(d.name).Append(" (already there)  "); continue; }
                var settings = AssetDatabase.LoadAssetAtPath<CelestialBodySettings>($"{DwarfDir}/{d.name}/{d.name}.asset");
                if (settings == null) { Debug.LogError($"[DwarfPlanets] {d.name}: settings asset missing — build the Planet Gallery first."); continue; }

                var go = Object.Instantiate(template.gameObject, organizer);
                go.name = d.name;

                // Orbit: XY plane like everything else, angle measured from +X
                // (the big four sit at 180°), moving the same way as Cyclops.
                float th = d.angleDeg * Mathf.Deg2Rad;
                Vector3 rel = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0f) * d.orbitRadius;
                Vector3 dir = new Vector3(Mathf.Sin(th), -Mathf.Cos(th), 0f);
                go.transform.position = sun.transform.position + rel;

                var cb = go.GetComponent<CelestialBody>();
                cb.bodyType = CelestialBody.BodyType.Planet;
                cb.bodyName = d.name;
                cb.radius = d.radius;
                cb.surfaceGravity = d.gravity;
                cb.initialVelocity = dir * (2f * Mathf.PI * d.orbitRadius / d.railPeriod);
                cb.railPeriod = d.railPeriod;
                cb.orbitGroup = "";
                cb.coOrbitLeader = null;
                cb.coOrbitAngle = 0f;
                cb.satelliteOrbitRadius = 0f;
                cb.satellitePeriod = 0f;
                cb.isPinned = false;
                cb.isStaticAttractor = false;
                cb.RecalculateMass();

                var placeholder = go.GetComponentInChildren<BodyPlaceholder>(true);
                if (placeholder == null) { Debug.LogError($"[DwarfPlanets] {d.name}: template has no BodyPlaceholder child?"); Object.DestroyImmediate(go); continue; }
                placeholder.bodySettings = settings;
                placeholder.transform.localScale = Vector3.one * d.radius;   // "Mesh Holder" is scaled to the radius

                // Water trigger at the surface for ocean worlds (PlayerController
                // swim / bobber / water bottle listen for tag Water); none on rocks.
                var water = go.transform.Find("waterline");
                if (water != null)
                {
                    if (d.ocean) { var sc = water.GetComponent<SphereCollider>(); if (sc != null) sc.radius = d.radius; }
                    else Object.DestroyImmediate(water.gameObject);
                }

                EditorUtility.SetDirty(go);
                added++;
                sb.Append($"{d.name} r{d.radius:0} @ {d.orbitRadius:0}/{d.angleDeg:0}° day {d.railPeriod:0}s  ");
            }

            // Tree exclusions (serialized in the scene, so the C# default is not what runs).
            foreach (var ts in FindInScene<TreeSpawner>(scene))
            {
                var list = new List<string>(ts.excludeBodyNames ?? new string[0]);
                bool changed = false;
                foreach (var n in NoTrees) if (!list.Contains(n)) { list.Add(n); changed = true; }
                if (changed) { ts.excludeBodyNames = list.ToArray(); EditorUtility.SetDirty(ts); }
            }

            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) Debug.LogError("[DwarfPlanets] SaveScene failed.");
            }
            Debug.Log(sb + $"→ {added} added, {skipped} already present. Saved {MainScenePath}.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// Apply the table above to dwarfs ALREADY in the scene: orbit, angle, day
    /// length, radius, gravity. Use after editing the table (no delete/re-add).
    [MenuItem("Tools/Solar System/Re-place Dwarf Planets (apply table)")]
    public static void Reposition()
    {
        var (scene, opened) = GetMainScene();
        if (!scene.IsValid()) return;
        try
        {
            var bodies = FindBodies(scene);
            var sun = Find(bodies, SunBody);
            if (sun == null) { Debug.LogError("[DwarfPlanets] no Sun in scene"); return; }
            var sb = new StringBuilder("[DwarfPlanets] re-placed: ");
            int n = 0;
            foreach (var d in Dwarfs)
            {
                var cb = Find(bodies, d.name);
                if (cb == null) { sb.Append(d.name).Append(" (not in scene)  "); continue; }
                float th = d.angleDeg * Mathf.Deg2Rad;
                Vector3 rel = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0f) * d.orbitRadius;
                Vector3 dir = new Vector3(Mathf.Sin(th), -Mathf.Cos(th), 0f);
                cb.transform.position = sun.transform.position + rel;
                cb.radius = d.radius;
                cb.surfaceGravity = d.gravity;
                cb.initialVelocity = dir * (2f * Mathf.PI * d.orbitRadius / d.railPeriod);
                cb.railPeriod = d.railPeriod;
                cb.RecalculateMass();
                var placeholder = cb.GetComponentInChildren<BodyPlaceholder>(true);
                if (placeholder != null) placeholder.transform.localScale = Vector3.one * d.radius;
                var water = cb.transform.Find("waterline");
                if (water != null) { var sc = water.GetComponent<SphereCollider>(); if (sc != null) sc.radius = d.radius; }
                EditorUtility.SetDirty(cb.gameObject);
                n++;
                sb.Append($"{d.name} @ {d.orbitRadius:0}/{d.angleDeg:0}° day {d.railPeriod:0}s  ");
            }
            if (n > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) Debug.LogError("[DwarfPlanets] SaveScene failed.");
            }
            Debug.Log(sb + $"→ {n} updated. Saved {MainScenePath}.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Tools/Solar System/Remove Dwarf Planets from 1.6.7.7.7")]
    public static void Remove()
    {
        if (!EditorUtility.DisplayDialog("Dwarf planets", "Delete the eight dwarf planets from 1.6.7.7.7 and restore the tree-spawner list?", "Remove", "Cancel")) return;
        var (scene, opened) = GetMainScene();
        if (!scene.IsValid()) return;
        try
        {
            int removed = 0;
            var bodies = FindBodies(scene);
            foreach (var d in Dwarfs)
            {
                var b = Find(bodies, d.name);
                if (b != null) { Object.DestroyImmediate(b.gameObject); removed++; }
            }
            foreach (var ts in FindInScene<TreeSpawner>(scene))
            {
                var list = new List<string>(ts.excludeBodyNames ?? new string[0]);
                if (list.RemoveAll(n => System.Array.IndexOf(NoTrees, n) >= 0) > 0) { ts.excludeBodyNames = list.ToArray(); EditorUtility.SetDirty(ts); }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DwarfPlanets] removed {removed} dwarf planets from {MainScenePath}.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    static (Scene scene, bool opened) GetMainScene()
    {
        var s = SceneManager.GetSceneByPath(MainScenePath);
        if (s.IsValid() && s.isLoaded) return (s, false);
        s = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
        if (!s.IsValid()) Debug.LogError("[DwarfPlanets] could not open " + MainScenePath);
        return (s, true);
    }

    static List<CelestialBody> FindBodies(Scene scene) => FindInScene<CelestialBody>(scene);

    static List<T> FindInScene<T>(Scene scene) where T : Component
    {
        var list = new List<T>();
        foreach (var root in scene.GetRootGameObjects())
            list.AddRange(root.GetComponentsInChildren<T>(true));
        return list;
    }

    static CelestialBody Find(List<CelestialBody> bodies, string name)
    {
        foreach (var b in bodies) if (b != null && b.bodyName == name) return b;
        return null;
    }
}
