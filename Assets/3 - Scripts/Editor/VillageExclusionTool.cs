#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drops a <see cref="SpawnExclusionZone"/> on every building under TOWN-VILLAGE so the
/// procedural spawners (trees, crystals, mushrooms, alien NPCs — all of which honour
/// SpawnExclusionZone) never place clutter on or right against the houses.
///
/// Each zone is sized to the building's footprint + a 3 m clearance, centred at the
/// building's ground-level centre — so "nothing within 3 m of the house" holds even for
/// large buildings (a fixed 3 m-from-pivot sphere would leave big houses' corners exposed).
///
/// Idempotent: re-run any time (e.g. after moving/adding buildings) — it updates the
/// existing zones in place rather than duplicating them. Note: the already-baked grass is
/// unaffected (it loads frozen positions and skips the runtime exclusion test); only the
/// live spawners are gated.
/// </summary>
public static class VillageExclusionTool
{
    const string VillagePath = "--- Celestial ---/Body Simulation/Humble Abode/TOWN-VILLAGE";
    const string ZoneChildName = "ClutterExclusion";
    const float ClearanceMargin = 3f;   // metres kept clear beyond each building's footprint

    [MenuItem("Tools/Village/Refresh Building Exclusion Zones (Humble Abode)")]
    public static void Refresh()
    {
        var village = GameObject.Find(VillagePath);
        if (village == null)
        {
            Debug.LogWarning($"[VillageExclusion] '{VillagePath}' not found in the active scene.");
            return;
        }

        // Planet centre — used to get the local "up" so footprints are measured in the
        // building's ground plane, not the (tilted) world axes.
        Vector3 planetCenter = village.transform.parent != null
            ? village.transform.parent.position : village.transform.position;

        int made = 0, updated = 0, skipped = 0;
        var report = new StringBuilder();

        for (int i = 0; i < village.transform.childCount; i++)
        {
            Transform building = village.transform.GetChild(i);
            if (building.name == ZoneChildName) continue;

            var rends = building.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) { skipped++; continue; }

            // World AABB centre is a good enough anchor for the radial direction.
            Bounds wb = rends[0].bounds;
            for (int r = 1; r < rends.Length; r++) wb.Encapsulate(rends[r].bounds);
            Vector3 up = (wb.center - planetCenter).sqrMagnitude > 1e-6f
                ? (wb.center - planetCenter).normalized : Vector3.up;

            // Walk the ACTUAL oriented bounding-box corners (local bounds → world), so a
            // tilted building isn't over-measured the way a world AABB would be. Track the
            // horizontal radius (perp to up) and the radial span (along up) for the centre.
            float maxHorizSq = 0f, minAlong = float.MaxValue, maxAlong = float.MinValue;
            foreach (var rend in rends)
            {
                Bounds lb = rend.localBounds;
                Matrix4x4 l2w = rend.transform.localToWorldMatrix;
                for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3(cx, cy, cz));
                    Vector3 w = l2w.MultiplyPoint3x4(corner);
                    Vector3 d = w - wb.center;
                    float along = Vector3.Dot(d, up);
                    Vector3 horiz = d - along * up;
                    if (horiz.sqrMagnitude > maxHorizSq) maxHorizSq = horiz.sqrMagnitude;
                    if (along < minAlong) minAlong = along;
                    if (along > maxAlong) maxAlong = along;
                }
            }
            float footprintRadius = Mathf.Sqrt(maxHorizSq);
            float radius = footprintRadius + ClearanceMargin;
            // Centre at the building's base (lowest point along up), under its centre — so a
            // ground-level spawn's distance to the zone is essentially horizontal.
            Vector3 center = wb.center + up * minAlong;

            Transform existing = building.Find(ZoneChildName);
            SpawnExclusionZone zone;
            if (existing != null)
            {
                zone = existing.GetComponent<SpawnExclusionZone>();
                if (zone == null) zone = existing.gameObject.AddComponent<SpawnExclusionZone>();
                existing.position = center;
                updated++;
            }
            else
            {
                var go = new GameObject(ZoneChildName);
                Undo.RegisterCreatedObjectUndo(go, "Add Village Exclusion Zone");
                go.transform.SetParent(building, true);
                go.transform.position = center;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                zone = go.AddComponent<SpawnExclusionZone>();
                made++;
            }
            zone.radius = radius;
            EditorUtility.SetDirty(zone);
            report.AppendLine($"  {building.name}: radius {radius:F1} m");
        }

        EditorSceneManager.MarkSceneDirty(village.scene);
        Debug.Log($"[VillageExclusion] {made} created, {updated} updated, {skipped} skipped (no renderers). " +
                  $"Clearance = footprint + {ClearanceMargin} m.\n{report}");
    }
}
#endif
