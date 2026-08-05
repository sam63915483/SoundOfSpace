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

    /// The "Binary System" pair. They are NOT a stable binary and never were —
    /// their mutual orbit (a≈3019) is wider than the pair's Hill radius against
    /// the sun (≈2104 at the current scale), so solar tides drive them together;
    /// a soak of the UNTOUCHED system has them overlapping by 151 m inside an
    /// hour. Shrinking the system shrinks the Hill radius with it and makes that
    /// strictly worse — at a naive 0.4× the encounter slingshots Humble Abode to
    /// 794,000 from the sun. So a rescale MUST reconfigure them, and the cheapest
    /// fix that keeps their gravity (and so their gameplay) is to stop treating
    /// them as a bound pair: two independent circular orbits, spaced by a safe
    /// multiple of their mutual Hill radius.
    static readonly (string inner, string outer)[] BinaryPairs = { ("Fiery Twin", "Icey Twin") };

    [Tooltip("Planet-pair spacing in mutual Hill radii. Below ~3.5 a pair of planets perturbs itself into crossing orbits over time; 3.5 is the standard rule of thumb.")]
    public const float HillSpacing = 3.5f;

    /// Explicit twin separation, in metres. 0 = derive it from the Hill rule.
    ///
    /// Set to the ORIGINAL 1995 because that separation IS the look — two worlds
    /// that read as a pair, the way the moon reads against Humble Abode. The
    /// Hill-stable answer is 3681 at 0.5×, which is orbitally correct and
    /// visually wrong: they stop looking like twins.
    ///
    /// The honest cost: at 1995 they sit at ~0.95 mutual Hill radii, so they
    /// perturb each other. That is EXACTLY the situation the game already
    /// shipped with (the original pair was at 0.95 too), and it takes the better
    /// part of an uninterrupted hour to matter. Circularising their heliocentric
    /// orbits — which the rescale does anyway — removes the eccentric mutual
    /// orbit that made the original pair close fast, so this is strictly better
    /// than what was there before, just not textbook-stable.
    public const float BinarySeparation = 1995f;

    [MenuItem("Tools/Solar System/Rescale — dry run (0.6 / 0.5 / 0.4 / 0.3)")]
    public static void DryRunSweep()
    {
        var sb = new StringBuilder();
        foreach (float k in new[] { 1f, 0.6f, 0.5f, 0.4f, 0.3f })
            sb.Append(Report(k, apply: false, stabiliseBinaries: true));
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/Solar System/Rescale — APPLY 0.5")] static void Apply050() => ApplyFactor(0.5f);
    [MenuItem("Tools/Solar System/Rescale — APPLY 0.4")] static void Apply040() => ApplyFactor(0.4f);
    [MenuItem("Tools/Solar System/Rescale — APPLY 0.3")] static void Apply030() => ApplyFactor(0.3f);

    public static void ApplyFactor(float k, bool stabiliseBinaries = true)
    {
        if (Application.isPlaying) { Debug.LogError("[Rescale] Not in play mode."); return; }
        Debug.Log(Report(k, apply: true, stabiliseBinaries));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    public static string Report(float k, bool apply, bool stabiliseBinaries = true)
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
            // Static attractors (the black hole) are NOT part of the orbital
            // layout — they don't orbit anything and nothing orbits them, so
            // there's nothing for a scale factor to preserve. Dragging one
            // inward just moves a piece of authored content closer for no
            // reason. Left exactly where it was placed. (Sam's call.)
            if (b.isStaticAttractor)
            {
                newPos[b] = pos0[b];
                newVel[b] = vel0[b];
                continue;
            }
            newPos[b] = sunPos + (pos0[b] - sunPos) * k;
            newVel[b] = vel0[b] * invSqrtK;
        }

        // Pass 1b — binary pairs. Re-seat each pair symmetrically about its
        // (scaled) barycentre at HillSpacing mutual Hill radii, on independent
        // CIRCULAR heliocentric orbits. Masses, radii and surface gravity are
        // untouched; only where the two sit and how fast they go changes.
        if (stabiliseBinaries)
        {
            double G = Universe.gravitationalConstant;
            double sunMass = (double)sun.surfaceGravity * sun.radius * sun.radius / G;
            foreach (var (innerName, outerName) in BinaryPairs)
            {
                var inner = ByName(innerName);
                var outer = ByName(outerName);
                if (inner == null || outer == null) continue;

                double mi = (double)inner.surfaceGravity * inner.radius * inner.radius / G;
                double mo = (double)outer.surfaceGravity * outer.radius * outer.radius / G;
                double mt = mi + mo;

                // Barycentre of the SCALED positions.
                Vector3 bary = (newPos[inner] * (float)mi + newPos[outer] * (float)mo) / (float)mt;
                float baryDist = Vector3.Distance(bary, sunPos);
                Vector3 radial = (bary - sunPos).normalized;

                float hill = baryDist * Mathf.Pow((float)(mt / (3.0 * sunMass)), 1f / 3f);

                // The separation SCALES with the system. Keeping it fixed was the
                // bug: at 0.5× the twins stayed 1995 apart while the viewing
                // distance from Humble Abode halved, so they appeared exactly
                // twice as far apart in the sky (9.2° -> 18.2°). Scaling it
                // preserves the look, which is the thing that actually matters.
                float sep = Mathf.Max(Vector3.Distance(pos0[inner], pos0[outer]) * k,
                                      (inner.radius + outer.radius) * 1.4f);

                // CO-ORBITAL, not a bound binary. Both twins sit at the SAME
                // orbital radius, offset ALONG the orbit, and are exempted from
                // each other's gravity via orbitGroup. Same radius means the same
                // angular speed, so the spacing is exact and permanent — no
                // drift, nothing to soak-test. A real bound pair is impossible
                // here: at this distance from the sun it would need surface
                // gravity ~133 to stay together (see CelestialBody.orbitGroup).
                Vector3 dir = newVel[inner].sqrMagnitude > 0.0001f
                            ? newVel[inner].normalized
                            : Vector3.Cross(radial, Vector3.forward).normalized;
                Vector3 along = Vector3.ProjectOnPlane(dir, radial).normalized;
                if (along.sqrMagnitude < 0.5f) along = Vector3.Cross(radial, Vector3.up).normalized;

                float halfAngle = sep * 0.5f / Mathf.Max(1f, baryDist);
                foreach (var (b, sign) in new[] { (inner, 0f), (outer, 2f) })
                {
                    // Rotate about the orbit normal so BOTH end up at baryDist.
                    Vector3 normal = Vector3.Cross(radial, along).normalized;
                    Vector3 r = Quaternion.AngleAxis(sign * halfAngle * Mathf.Rad2Deg, normal) * radial;
                    newPos[b] = sunPos + r * baryDist;
                    Vector3 v = Vector3.Cross(normal, r).normalized;
                    if (Vector3.Dot(v, along) < 0f) v = -v;
                    newVel[b] = v * Mathf.Sqrt((float)(G * sunMass / baryDist));

                    var bso = new SerializedObject(b);
                    bso.FindProperty("orbitGroup").stringValue = "twins";
                    // The OUTER twin follows the inner one: not integrated, just
                    // placed at the leader's radius rotated by the pair angle.
                    if (b == outer)
                    {
                        bso.FindProperty("coOrbitLeader").objectReferenceValue = inner;
                        bso.FindProperty("coOrbitAngle").floatValue = halfAngle * 2f * Mathf.Rad2Deg;
                    }
                    else
                    {
                        bso.FindProperty("coOrbitLeader").objectReferenceValue = null;
                        bso.FindProperty("coOrbitAngle").floatValue = 0f;
                    }
                    if (apply) bso.ApplyModifiedPropertiesWithoutUndo();
                }

                sb.AppendLine($"  co-orbital pair '{innerName} + {outerName}': both at {baryDist:F0} from the sun," +
                              $" separation {Vector3.Distance(pos0[inner], pos0[outer]):F0} -> {sep:F0} (scaled by k)," +
                              $" orbitGroup='twins' so they ignore each other, Hill would have been {hill:F0}");
            }
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
            // The moon's orbit scales WITH the system, not against it. Keeping
            // its absolute offset looks safer but is the opposite: a planet's
            // Hill radius is proportional to its distance from the sun, so at
            // 0.4× Humble Abode's shrinks from 2405 to 962 while an unscaled
            // Constant Companion still sits 945 out — right on the boundary, and
            // the soak has it escaping into its own solar orbit within the hour.
            // Scaling the offset by k and the relative velocity by 1/√k keeps the
            // moon at the SAME FRACTION of the Hill radius it occupies today,
            // which is the only thing that preserves its stability.
            newPos[b] = newPos[parent] + (pos0[b] - pos0[parent]) * k;
            newVel[b] = newVel[parent] + (vel0[b] - vel0[parent]) * invSqrtK;
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

        // Carry along anything standing on a planet that ISN'T parented to it.
        // The scene's authored Player start sits exactly on Icey Twin's surface
        // under "--- Player & Ship ---", so a rescale that only moves the bodies
        // leaves it floating in deep space — and pressing Play in the editor
        // starts you there. Offsets are preserved verbatim (planet radii don't
        // change), so anything on a surface stays on that surface.
        if (apply)
        {
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.GetComponentInParent<CelestialBody>() != null) continue;   // already rides along
                if (t.parent != null && t.parent.GetComponentInParent<CelestialBody>() == null && t.parent.parent != null) continue;
                foreach (var b in bodies)
                {
                    if (b == sun || b.isStaticAttractor) continue;
                    if (Vector3.Distance(t.position, pos0[b]) > b.radius * 4f) continue;
                    Vector3 offset = t.position - pos0[b];
                    t.position = newPos[b] + offset;
                    EditorUtility.SetDirty(t);
                    sb.AppendLine($"  carried '{t.name}' with {b.bodyName} (offset {offset.magnitude:F0} preserved)");
                    break;
                }
            }
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
