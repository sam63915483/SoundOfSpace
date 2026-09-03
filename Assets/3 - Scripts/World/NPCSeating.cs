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
/// This measures the INSTANCE from real vertices: skinned meshes are baked
/// (readable regardless of import settings), plain meshes use vertices when
/// readable. Everything lands in the root's unscaled local space (so the
/// SpawnFade's 5% start scale cancels), and the body is re-seated so that
/// lowest point meets the terrain hit under it -- the same planet-local probe
/// the walker uses. Call once right after spawn and hand the result to
/// AlienWander so every later step keeps the same depth.
///
/// Renderer/skinned BOUNDS are never used for the feet: authored skinned
/// bounds are loose (a first version fell back to them and floated every
/// alien by the slack in those boxes).
/// </summary>
public static class NPCSeating
{
    static Mesh _bake;
    static readonly List<Vector3> _verts = new List<Vector3>(4096);

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
            smr.BakeMesh(_bake, false);
            _bake.GetVertices(_verts);
            if (_verts.Count == 0) continue;

            // BakeMesh's scale semantics differ between Unity versions/overloads.
            // Decide per renderer by comparing the baked size to the bind-pose
            // mesh's own bounds: a bake the size of the mesh is unscaled (push
            // it through the full transform); a bake scaled by the transform's
            // scale must go through rotation + position only.
            Matrix4x4 toWorld = BakeToWorld(smr);
            for (int i = 0; i < _verts.Count; i++)
            {
                float y = root.InverseTransformPoint(toWorld.MultiplyPoint3x4(_verts[i])).y;
                if (y < min) min = y;
            }
            any = true;
        }

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) continue;   // unreadable: no guess from loose bounds
            mesh.GetVertices(_verts);
            for (int i = 0; i < _verts.Count; i++)
            {
                float y = root.InverseTransformPoint(mr.transform.TransformPoint(_verts[i])).y;
                if (y < min) min = y;
            }
            if (_verts.Count > 0) any = true;
        }

        if (!any) return false;
        feetY = min;
        return true;
    }

    static Matrix4x4 BakeToWorld(SkinnedMeshRenderer smr)
    {
        Transform t = smr.transform;
        float meshSize = smr.sharedMesh.bounds.size.magnitude;
        _bake.RecalculateBounds();
        float bakeSize = _bake.bounds.size.magnitude;
        Vector3 ls = t.lossyScale;
        float scaleAvg = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
        if (meshSize < 1e-6f || bakeSize < 1e-6f || Mathf.Abs(scaleAvg - 1f) < 1e-3f)
            return t.localToWorldMatrix;   // no scale to argue about
        float ratio = bakeSize / meshSize;
        bool bakeIsScaled = Mathf.Abs(ratio - scaleAvg) < Mathf.Abs(ratio - 1f);
        return bakeIsScaled
            ? Matrix4x4.TRS(t.position, t.rotation, Vector3.one)
            : t.localToWorldMatrix;
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
        Debug.Log($"[NPCSeating] {root.name}: feetY={feetY:F3} x scale {scale:F2} -> seat {seatDepth:F3} m (embed {embed:F3}).");
        return true;
    }
}
