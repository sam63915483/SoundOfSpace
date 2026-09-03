using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Exact feet-on-ground seating for spawned NPCs (2026-09-03, Sam: "they
/// should all perfectly sit on the ground").
///
/// Why they didn't: the spawners seat a model by its prefab's "lowest point",
/// which SpawnerCubeface.ComputeLocalBottomY takes from mesh AABBs when the
/// mesh isn't CPU-readable (almost every FBX here). An AABB of a rotated
/// child mesh extends well below the real lowest vertex, so some models sat
/// too high, and the hand-tuned 0.37 m embed that compensated on average then
/// buried the ones whose AABB happened to be right. Per-model error, either
/// direction -- exactly the mix Sam saw.
///
/// This measures the INSTANCE: skinned meshes are baked (readable regardless
/// of import settings), plain meshes use vertices when readable and bounds
/// otherwise, everything in the root's unscaled local space (so SpawnFade's
/// 5% start scale cancels), and the body is re-seated so that lowest point
/// lands on the terrain hit under it -- the same planet-local probe the walker
/// uses. Call once right after spawn and hand the result to AlienWander so
/// every later step keeps the same depth.
/// </summary>
public static class NPCSeating
{
    static Mesh _bake;
    static readonly List<Vector3> _verts = new List<Vector3>(4096);
    static readonly Vector3[] _corners = new Vector3[8];

    /// Lowest point of the model along the root's local +Y, in root-local
    /// UNSCALED units (multiply by the root scale for metres).
    public static bool MeasureFeetLocalY(Transform root, out float feetY)
    {
        feetY = 0f;
        float min = float.MaxValue;
        bool any = false;

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            if (_bake == null) _bake = new Mesh();
            // Baked without the renderer's scale, then pushed through its full
            // transform (which applies that scale once) into root space.
            smr.BakeMesh(_bake, false);
            _bake.GetVertices(_verts);
            float localMin = float.MaxValue, localMax = float.MinValue;
            for (int i = 0; i < _verts.Count; i++)
            {
                float y = root.InverseTransformPoint(smr.transform.TransformPoint(_verts[i])).y;
                if (y < localMin) localMin = y;
                if (y > localMax) localMax = y;
            }
            if (_verts.Count == 0) continue;
            // Sanity: the vertex height must agree with the renderer's world
            // bounds (which are definitely scaled). If BakeMesh's scale
            // semantics ever differ, fall back to the bounds for this part.
            float boundsH = BoundsHeightLocal(root, smr.bounds, out float boundsMin);
            float vertH = localMax - localMin;
            if (boundsH > 1e-4f && (vertH > boundsH * 2f || vertH < boundsH * 0.5f)) localMin = boundsMin;
            if (localMin < min) min = localMin;
            any = true;
        }

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) continue;
            float localMin;
            if (mesh.isReadable)
            {
                mesh.GetVertices(_verts);
                localMin = float.MaxValue;
                for (int i = 0; i < _verts.Count; i++)
                {
                    float y = root.InverseTransformPoint(mr.transform.TransformPoint(_verts[i])).y;
                    if (y < localMin) localMin = y;
                }
                if (_verts.Count == 0) continue;
            }
            else
            {
                BoundsHeightLocal(root, mr.bounds, out localMin);
            }
            if (localMin < min) min = localMin;
            any = true;
        }

        if (!any) return false;
        feetY = min;
        return true;
    }

    // Height (and lowest y) of a WORLD bounds in the root's unscaled local space.
    static float BoundsHeightLocal(Transform root, Bounds b, out float minY)
    {
        Vector3 mn = b.min, mx = b.max;
        _corners[0] = new Vector3(mn.x, mn.y, mn.z); _corners[1] = new Vector3(mx.x, mn.y, mn.z);
        _corners[2] = new Vector3(mn.x, mx.y, mn.z); _corners[3] = new Vector3(mx.x, mx.y, mn.z);
        _corners[4] = new Vector3(mn.x, mn.y, mx.z); _corners[5] = new Vector3(mx.x, mn.y, mx.z);
        _corners[6] = new Vector3(mn.x, mx.y, mx.z); _corners[7] = new Vector3(mx.x, mx.y, mx.z);
        minY = float.MaxValue; float maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            float y = root.InverseTransformPoint(_corners[i]).y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return maxY - minY;
    }

    /// <summary>
    /// Re-seat an already-parented body so its lowest point sits <paramref name="embed"/>
    /// metres into the terrain directly under it. Returns the seat depth the
    /// walker must keep using (feet offset x scale + embed).
    /// </summary>
    public static bool Reseat(Transform root, CelestialBody body, LayerMask mask, float scale, float embed,
                              out float seatDepth)
    {
        seatDepth = 0f;
        if (root == null || body == null || body.Rigidbody == null) return false;
        if (!MeasureFeetLocalY(root, out float feetY)) return false;

        var rb = body.Rigidbody;
        Vector3 local = root.localPosition;
        Vector3 up = local.normalized;
        Vector3 originW = rb.rotation * (local + up * 3f) + rb.position;
        if (!Physics.Raycast(originW, rb.rotation * -up, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore))
            return false;

        Vector3 groundLocal = Quaternion.Inverse(rb.rotation) * (hit.point - rb.position);
        seatDepth = feetY * scale + embed;
        root.localPosition = groundLocal - groundLocal.normalized * seatDepth;
        return true;
    }
}
