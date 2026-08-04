using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Brings the solar system closer together WITHOUT changing planet sizes, orbit
/// shapes, or moon orbits. Tools ▸ Solar System ▸ Rescale.
///
/// The maths, and why it's exact rather than a fudge:
///
/// Planet masses are DERIVED (`CelestialBody.mass = surfaceGravity * radius² / G`),
/// so leaving surfaceGravity and radius alone means every mass is untouched. For
/// a body orbiting the sun at distance r, orbital speed goes as v = √(GM/r).
/// Scale r by k and the orbit stays the same SHAPE — same eccentricity, same
/// plane — as long as v is scaled by 1/√k. (Periods then scale by k^1.5, so a
/// tighter system also orbits faster. That's Kepler, not a side effect to fix.)
///
/// Moons are handled separately on purpose. A naive uniform scale would shrink
/// the moon–planet gap too, and at k=0.4 Constant Companion ends up 378 m from
/// Humble Abode whose surfaces are already 250 m of that — it would practically
/// graze the planet. So each moon KEEPS its exact offset and its exact velocity
/// relative to its parent; only the parent's heliocentric orbit shrinks. Moon
/// orbits come out bit-identical.
///
/// KNOWN CONSEQUENCE — existing saves. `CelestialBodySave` stores ABSOLUTE body
/// positions and velocities and `CelestialBody.ApplySavedState` writes them back
/// verbatim, so loading a save made before a rescale restores the OLD spread-out
/// layout for that session. A rescale therefore only takes effect on NEW GAMES
/// unless the saves are migrated too.
/// </summary>
public static class SolarSystemRescale
{
    // Which moon orbits which planet. Chosen by proximity below, but pinned here
    // so a moon can never be re-parented by an accident of geometry.
    static readonly Dictionary<string, string> MoonParents = new Dictionary<string, string>
    {
        { "Constant Companion", "Humble Abode" },
        { "Watchful Eye",       "Cyclops" },
        { "Tumbling Bean",      "Cyclops" },
    };

    [MenuItem("Tools/Solar System/Rescale — dry run (0.6 / 0.5 / 0.4 / 0.3)")]
    public static void DryRunSweep()
    {
        var sb = new StringBuilder();
        foreach (float k in new[] { 1f, 0.6f, 0.5f, 0.4f, 0.3f })
            sb.Append(Report(k, apply: false));
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/Solar System/Rescale — APPLY 0.5")] static void Apply050() => ApplyFactor(0.5f);
    [MenuItem("Tools/Solar System/Rescale — APPLY 0.4")] static void Apply040() => ApplyFactor(0.4f);
    [MenuItem("Tools/Solar System/Rescale — APPLY 0.3")] static void Apply030() => ApplyFactor(0.3f);

    public static void ApplyFactor(float k)
    {
        if (Application.isPlaying) { Debug.LogError("[Rescale] Not in play mode."); return; }
        Debug.Log(Report(k, apply: true));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    public static string Report(float k, bool apply)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n=== SOLAR SYSTEM SCALE {k:0.00}× {(apply ? "(APPLIED)" : "(dry run)")} ===");

        var bodies = Object.FindObjectsOfType<CelestialBody>(true);
        CelestialBody sun = null;
        foreach (var b in bodies) if (b.bodyName == "Sun") sun = b;
        if (sun == null) return "[Rescale] no body named 'Sun' — aborted.\n";

        // Snapshot BEFORE anything moves, so moon offsets are read from the
        // original layout even after their parent has been written.
        var pos0 = new Dictionary<CelestialBody, Vector3>();
        var vel0 = new Dictionary<CelestialBody, Vector3>();
        foreach (var b in bodies)
        {
            pos0[b] = b.transform.position;
            vel0[b] = ReadInitialVelocity(b);
        }

        CelestialBody ByName(string n)
        {
            foreach (var b in bodies) if (b.bodyName == n) return b;
            return null;
        }

        float invSqrtK = 1f / Mathf.Sqrt(k);
        Vector3 sunPos = pos0[sun];

        // Pass 1 — planets (and the sun / static attractors).
        var newPos = new Dictionary<CelestialBody, Vector3>();
        var newVel = new Dictionary<CelestialBody, Vector3>();
        foreach (var b in bodies)
        {
            if (MoonParents.ContainsKey(b.bodyName)) continue;   // pass 2
            newPos[b] = sunPos + (pos0[b] - sunPos) * k;
            // Static attractors (the black hole) don't orbit — leave their
            // velocity at whatever it is, which is zero.
            newVel[b] = b.isStaticAttractor ? vel0[b] : vel0[b] * invSqrtK;
        }

        // Pass 2 — moons keep their parent-relative orbit EXACTLY.
        foreach (var b in bodies)
        {
            if (!MoonParents.TryGetValue(b.bodyName, out string parentName)) continue;
            var parent = ByName(parentName);
            if (parent == null || !newPos.ContainsKey(parent))
            {
                sb.AppendLine($"  !! {b.bodyName}: parent '{parentName}' missing — left alone");
                newPos[b] = pos0[b];
                newVel[b] = vel0[b];
                continue;
            }
            newPos[b] = newPos[parent] + (pos0[b] - pos0[parent]);
            newVel[b] = newVel[parent] + (vel0[b] - vel0[parent]);
        }

        // Report + optionally write.
        sb.AppendLine($"{"body",-20}{"dist->sun",12}{"new dist",12}{"speed",9}{"new speed",11}   note");
        foreach (var b in bodies)
        {
            float d0 = Vector3.Distance(pos0[b], sunPos);
            float d1 = Vector3.Distance(newPos[b], sunPos);
            string note = b == sun ? "sun"
                        : b.isStaticAttractor ? "static attractor — velocity untouched"
                        : MoonParents.TryGetValue(b.bodyName, out var pn) ? $"moon of {pn} — offset+relative velocity preserved"
                        : "planet";
            sb.AppendLine($"{b.bodyName,-20}{d0,12:F0}{d1,12:F0}{vel0[b].magnitude,9:F1}{newVel[b].magnitude,11:F1}   {note}");

            if (!apply) continue;
            b.transform.position = newPos[b];
            var so = new SerializedObject(b);
            so.FindProperty("initialVelocity").vector3Value = newVel[b];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(b);
            EditorUtility.SetDirty(b.gameObject);
        }

        // Clearance check — the thing a naive uniform scale gets wrong.
        sb.AppendLine("  surface-to-surface clearance:");
        foreach (var kv in MoonParents)
        {
            var moon = ByName(kv.Key);
            var planet = ByName(kv.Value);
            if (moon == null || planet == null) continue;
            float gap = Vector3.Distance(newPos[moon], newPos[planet]) - moon.radius - planet.radius;
            sb.AppendLine($"    {kv.Key} ↔ {kv.Value}: {gap:F0} m{(gap < 200f ? "   *** TOO TIGHT ***" : "")}");
        }
        // Closest approach between planet orbits (rough: current separations).
        float minGap = float.MaxValue; string pair = "";
        for (int i = 0; i < bodies.Length; i++)
            for (int j = i + 1; j < bodies.Length; j++)
            {
                var a = bodies[i]; var c = bodies[j];
                if (a == sun || c == sun || a.isStaticAttractor || c.isStaticAttractor) continue;
                if (MoonParents.ContainsKey(a.bodyName) || MoonParents.ContainsKey(c.bodyName)) continue;
                float g = Vector3.Distance(newPos[a], newPos[c]) - a.radius - c.radius;
                if (g < minGap) { minGap = g; pair = $"{a.bodyName} ↔ {c.bodyName}"; }
            }
        if (minGap < float.MaxValue) sb.AppendLine($"    closest planet pair right now: {pair} = {minGap:F0} m");
        return sb.ToString();
    }

    static Vector3 ReadInitialVelocity(CelestialBody b)
    {
        var so = new SerializedObject(b);
        var p = so.FindProperty("initialVelocity");
        return p != null ? p.vector3Value : Vector3.zero;
    }
}
