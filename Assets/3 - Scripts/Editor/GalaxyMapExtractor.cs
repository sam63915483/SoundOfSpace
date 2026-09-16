using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Writes KONKEBULAR-7's real planet list out of the gameplay scene, for the
/// shuttle computer's deep-range map.
///
/// Sam, 2026-09-16: "the biggest evidence that you arent using the actual nav
/// app from scene 1.6.7.7.7 is the fact that theres only 4 planets in
/// konkebular, and in the actuall gameplay scene theres many more because
/// theres dwarf planets."
///
/// He was right, and hand-typing the list would just be the same mistake with
/// more entries — it would drift the first time a planet is added, renamed or
/// moved. So the list is EXTRACTED, the way prototypes/nav-map/extract_system.py
/// did it for the prototype, and re-extracting is one menu item.
///
/// Source of truth, matched to the real map exactly:
///   • which bodies  — <see cref="ShuttleAutopilot.CanLandOn"/>, the same test
///                     ShuttleAutopilot.LandablePlanets() uses
///   • orbit radius  — the body's RAIL radius (OrbitRange.TryRail), which is
///                     what NavOrbitRadius draws against
///   • body radius   — CelestialBody.radius, which NavDotRadius sizes dots from
///   • colour        — ShuttleComputerUI's own swatch table, via the same
///                     name-keyed lookup the real map uses
///
/// Output: Assets/StreamingAssets/galaxy_konkebular.json, read at runtime by
/// ShuttleComputerTutorialNavUI. StreamingAssets so a build carries it; the map
/// falls back to its built-in list if the file is missing, so the screen can
/// never come up empty.
///
/// ⚠️ Builds copy StreamingAssets AT BUILD TIME — re-run this, then rebuild, for
/// a build to see a change. The Editor reads it live.
/// </summary>
public static class GalaxyMapExtractor
{
    public const string GameplayScenePath = "Assets/1.6.7.7.7.unity";
    public const string OutputPath = "Assets/StreamingAssets/galaxy_konkebular.json";

    [MenuItem("Tools/Solar System/Extract Konkebular for the Deep-Range Map")]
    public static void Extract()
    {
        var prevActive = SceneManager.GetActiveScene();
        var gp = SceneManager.GetSceneByPath(GameplayScenePath);
        bool opened = false;

        try
        {
            if (!(gp.IsValid() && gp.isLoaded))
            {
                Debug.Log("[GalaxyExtract] opening " + GameplayScenePath + " additively (read-only) …");
                gp = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            var bodies = new List<CelestialBody>();
            foreach (var root in gp.GetRootGameObjects())
                foreach (var b in root.GetComponentsInChildren<CelestialBody>(true))
                    if (b != null) bodies.Add(b);

            // The same filter the NAV list uses. CanLandOn is the authority on
            // "is this a place the shuttle treats as a destination", so dwarf
            // planets come along exactly as they do in the real map.
            var planets = new List<CelestialBody>();
            foreach (var b in bodies)
                if (ShuttleAutopilot.CanLandOn(b)) planets.Add(b);

            if (planets.Count == 0)
            {
                Debug.LogError("[GalaxyExtract] no landable planets found — nothing written.");
                return;
            }

            // ⚠️ OrbitRange.TryRail reads CelestialBody.railRadius, which is
            // [NonSerialized] runtime state owned by NBodySimulation - in the
            // EDITOR it is 0 for every body, and the first extract duly wrote
            // twelve planets all at the same orbit. The scene places each planet
            // AT its orbital position, so the distance from the Sun is the rail
            // radius, available without pressing Play. TryRail is still
            // preferred when it answers (it will, if this is ever run in play
            // mode).
            CelestialBody sun = null;
            foreach (var b in bodies)
                if (b.bodyType == CelestialBody.BodyType.Sun) { sun = b; break; }
            Vector3 sunAt = sun != null ? sun.transform.position : Vector3.zero;
            if (sun == null) Debug.LogWarning("[GalaxyExtract] no Sun found - orbits measured from the world origin.");

            var rows = new List<(CelestialBody body, float orbit, float angle)>();
            foreach (var b in planets)
            {
                Vector3 rel = b.transform.position - sunAt;
                float r = OrbitRange.TryRail(b, out float rail, out _, out _) && rail > 0.01f
                        ? rail
                        : rel.magnitude;
                // Where it sits on that orbit right now, so the extracted
                // arrangement matches the scene rather than being reshuffled.
                float ang = Mathf.Atan2(rel.z, rel.x);
                rows.Add((b, r, ang));
            }
            rows.Sort((x, y) => x.orbit.CompareTo(y.orbit));

            float maxOrbit = 1f;
            foreach (var r in rows) if (r.orbit > maxOrbit) maxOrbit = r.orbit;

            // Normalise the rails into the map's AU space. The widest orbit
            // becomes OuterAU, so the system fills the same fraction of the pane
            // whatever the scene's real metres are.
            const float OuterAU = 8f;

            var sb = new StringBuilder();
            sb.Append("{\n  \"_generated\": \"Tools ▸ Solar System ▸ Extract Konkebular for the Deep-Range Map, from ")
              .Append(GameplayScenePath).Append("\",\n");
            sb.Append("  \"_note\": \"Orbit radii are the scene's RAIL radii normalised so the widest is ")
              .Append(OuterAU.ToString("0.##")).Append(" AU on the map. bodyRadius is CelestialBody.radius, which NavDotRadius sizes the dot from.\",\n");
            sb.Append("  \"planets\": [\n");

            for (int i = 0; i < rows.Count; i++)
            {
                var b = rows[i].body;
                float orbitAU = maxOrbit > 0.001f ? Mathf.Max(0.18f, rows[i].orbit / maxOrbit * OuterAU) : 1f;
                Color c = ShuttleComputerUI.MapSwatchFor(b.bodyName);
                // ⚠️ NOT the scene's angle. The bodies are authored along one
                // axis and spread onto their rails at RUNTIME by
                // NBodySimulation (railPhase is [NonSerialized] too), so every
                // extracted angle came out as exactly 0 or 180 degrees - twelve
                // planets in a dead straight line out from the sun.
                //
                // A golden-angle spread by index is deterministic (the same
                // every extract), never clumps, and is no less true than a
                // straight line: these are drawn worlds on a picture of a
                // system, and where they sit on their rails at t=0 is not
                // information the player can act on. Co-orbital pairs are pulled
                // back together afterwards by SpreadCoOrbitals.
                float phase = Mathf.Repeat(i * 0.381966f, 1f);   // 1 - 1/phi
                bool isTarget = b.bodyName == "Humble Abode";

                sb.Append("    { \"name\": \"").Append(b.bodyName).Append("\"")
                  .Append(", \"orbitAU\": ").Append(orbitAU.ToString("0.###"))
                  .Append(", \"bodyRadius\": ").Append(Mathf.Max(1f, b.radius).ToString("0.#"))
                  // Kepler-ish: period grows with r^1.5, so the outer worlds
                  // crawl and the inner ones hurry, which is what makes a system
                  // read as turning rather than spinning as one plate.
                  .Append(", \"periodDays\": ").Append((90f * Mathf.Pow(Mathf.Max(0.05f, orbitAU), 1.5f) * 12f).ToString("0"))
                  .Append(", \"phase\": ").Append(phase.ToString("0.###"))
                  .Append(", \"colour\": \"#").Append(ColorUtility.ToHtmlStringRGB(c)).Append("\"")
                  .Append(", \"target\": ").Append(isTarget ? "true" : "false")
                  .Append(" }").Append(i < rows.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutputPath));
            System.IO.File.WriteAllText(OutputPath, sb.ToString());
            AssetDatabase.Refresh();

            var names = new StringBuilder();
            foreach (var r in rows) names.Append(r.body.bodyName).Append("  ");
            Debug.Log($"[GalaxyExtract] wrote {rows.Count} planets to {OutputPath}\n    {names}");
        }
        finally
        {
            if (opened)
            {
                if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
                var still = SceneManager.GetSceneByPath(GameplayScenePath);
                if (still.IsValid() && still.isLoaded) EditorSceneManager.CloseScene(still, true);   // discard
            }
        }
    }
}
