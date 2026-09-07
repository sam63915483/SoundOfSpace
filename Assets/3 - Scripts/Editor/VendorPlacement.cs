using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Placement helper for the planet-economy vendor stands
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 1).
///
/// Sam places roughly ten stands by hand across ten planets. Doing that by
/// dragging gizmos in a scene where "down" is a different direction on every
/// world is miserable, so this does the tedious half: drop the stand onto the
/// planet surface and stand it upright on the local normal. Where it goes and
/// which way it faces is Sam's call — nudge it after.
///
/// Menu: <b>Tools ▸ Vendors ▸ Snap to Ground</b> (Ctrl+G with a vendor selected).
/// </summary>
public static class VendorPlacement
{
    [MenuItem("Tools/Vendors/Snap to Ground %g")]
    public static void SnapToGround()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            Debug.LogWarning("[Vendors] Select the vendor stand(s) you want to snap first.");
            return;
        }

        int done = 0, failed = 0;
        foreach (var go in sel)
        {
            if (go == null) continue;
            Undo.RecordObject(go.transform, "Snap Vendor to Ground");
            if (Snap(go)) done++; else failed++;
        }

        Debug.Log($"[Vendors] Snapped {done} stand(s) to the ground" +
                  (failed > 0 ? $", {failed} could not find a surface (see warnings)." : "."));
    }

    [MenuItem("Tools/Vendors/Snap to Ground %g", true)]
    static bool SnapToGroundValidate() => Selection.gameObjects != null && Selection.gameObjects.Length > 0;

    static bool Snap(GameObject go)
    {
        var body = go.GetComponentInParent<CelestialBody>();
        if (body == null)
        {
            Debug.LogWarning($"[Vendors] '{go.name}' is not parented under a CelestialBody. " +
                             "Drag it under the planet you want it on, then snap.", go);
            return false;
        }

        Vector3 centre = body.transform.position;
        Vector3 up     = (go.transform.position - centre);
        if (up.sqrMagnitude < 0.0001f) up = go.transform.up;   // sitting exactly on the core
        up.Normalize();

        // Start well above whatever is there and fire straight down at the core,
        // so the stand lands on the first solid thing under it — terrain, or a
        // village rooftop if Sam parked it over one.
        float startHeight = Mathf.Max(body.radius * 0.35f, 200f);
        Vector3 origin    = centre + up * (Vector3.Dot(go.transform.position - centre, up) + startHeight);

        Vector3 surface;
        if (TryRaycast(go, origin, -up, startHeight * 3f, out surface))
        {
            go.transform.position = surface;
        }
        else if (body.radius > 0.01f)
        {
            // Editor fallback: several planets only build their terrain mesh when
            // the game loads, so there is nothing to hit in edit mode. Drop the
            // stand onto the ideal sphere instead — the right neighbourhood and
            // the right orientation, which is all Sam needs before nudging.
            go.transform.position = centre + up * body.radius;
            Debug.LogWarning($"[Vendors] '{go.name}': no terrain collider on {body.bodyName} in the " +
                             "Editor, so it was placed on the planet's sphere radius instead. " +
                             "Enter Play once to build the terrain, or nudge it down onto the ground.", go);
        }
        else
        {
            Debug.LogWarning($"[Vendors] '{go.name}': no surface found on {body.bodyName}.", go);
            return false;
        }

        // Stand upright on the planet, keeping whatever direction it was facing.
        Vector3 fwd = Vector3.ProjectOnPlane(go.transform.forward, up);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(go.transform.right, up);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.Cross(up, Vector3.right);
        go.transform.rotation = Quaternion.LookRotation(fwd.normalized, up);

        EditorUtility.SetDirty(go.transform);
        return true;
    }

    /// <summary>Raycast that ignores the stand's own colliders — otherwise the
    /// first thing every stand lands on is itself.</summary>
    static bool TryRaycast(GameObject self, Vector3 origin, Vector3 dir, float dist, out Vector3 point)
    {
        point = Vector3.zero;

        var mine = new HashSet<Collider>(self.GetComponentsInChildren<Collider>(true));
        var hits = Physics.RaycastAll(origin, dir, dist, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (mine.Contains(hits[i].collider)) continue;
            point = hits[i].point;
            return true;
        }
        return false;
    }
}
