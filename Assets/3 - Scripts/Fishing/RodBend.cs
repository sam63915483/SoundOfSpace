using UnityEngine;

/// <summary>
/// Bends the fishing rod's MESH, because the prefab is one rigid lump with no
/// bones and no amount of rotating the whole object looks like a loaded rod.
/// (Sam, 2026-09-01: "the rod doesn't bend at all which makes sense because the
/// prefab doesn't bend, but could you actually make it bend".)
///
/// <b>How.</b> On the first Apply we cache every mesh's vertices and swap in a
/// per-instance copy. Each frame we rotate every vertex about the rod's BUTT by
/// an angle that grows along the rod's length, so the handle stays put and the
/// tip swings — a rod bows near the tip, not in the middle. The growth is
/// quadratic along the length, which gives the classic parabola rather than a
/// hinge.
///
/// <b>Direction.</b> The bend aims at whatever is pulling — the bobber's actual
/// world position — converted into the rod's local space. So it is always
/// correct without a hand-tuned axis, and it re-aims as the fish moves around
/// you. Zero load restores the authored shape exactly.
///
/// <b>Cost.</b> One pass over the rod's vertices, and only when the pose has
/// actually changed: a slack line at rest costs nothing at all. Anything
/// unexpectedly heavy is skipped outright rather than quietly eating frames.
/// </summary>
[DisallowMultipleComponent]
public class RodBend : MonoBehaviour
{
    /// Above this many vertices we refuse to deform — a rod should be a few
    /// hundred, and silently chewing a frame budget is worse than not bending.
    const int MaxVertices = 6000;

    class Part
    {
        public Mesh mesh;               // per-instance copy we write into
        public Vector3[] baseVerts;     // authored positions, in ROD-ROOT space
        public Vector3[] baseNormals;   // authored normals, in ROD-ROOT space (may be null)
        public Vector3[] work;          // scratch, reused — no per-frame allocation
        public Vector3[] workNormals;
        public bool needsConversion;    // true when the mesh lives on a child
        public Transform owner;
    }

    Part[] _parts;
    bool _failed;
    bool _restored = true;      // true when the mesh currently holds its authored shape
    float _minAxis, _axisLength;
    float _appliedAngle;
    Vector3 _appliedDir = Vector3.forward;
    Vector3 _appliedAxis = Vector3.right;

    /// <summary>
    /// Where a point that was authored at <paramref name="worldPoint"/> has been
    /// moved to by the current bend.
    ///
    /// The RodTip marker is a plain child Transform. Deforming the MESH does not
    /// move it, so once the rod started bending the line still launched from the
    /// straight rod's tip and visibly detached (Sam, 2026-09-01: "the line
    /// doesn't match up and attach to the tip of the rod"). Running the tip
    /// through the exact same bend the vertices got puts the line back on the
    /// tip at every load.
    /// </summary>
    public Vector3 BentWorldPoint(Vector3 worldPoint)
    {
        if (_failed || _parts == null || _restored || Mathf.Abs(_appliedAngle) < 0.05f)
            return worldPoint;

        Vector3 local = transform.InverseTransformPoint(worldPoint);
        float t = (local.y - _minAxis) / _axisLength;
        if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
        Vector3 pivot = new Vector3(0f, _minAxis, 0f);
        Quaternion q = Quaternion.AngleAxis(_appliedAngle * t * t, _appliedAxis);
        return transform.TransformPoint(pivot + q * (local - pivot));
    }

    /// <summary>
    /// Bend the rod by <paramref name="degrees"/> toward <paramref name="worldTarget"/>.
    /// Pass 0 to restore the authored shape.
    /// </summary>
    public void Apply(float degrees, Vector3 worldTarget)
    {
        if (_failed) return;
        if (_parts == null && !Build()) return;

        // Slack: restore once and then do nothing at all until load returns.
        if (Mathf.Abs(degrees) < 0.05f)
        {
            if (!_restored) RestoreAll();
            return;
        }

        Vector3 localTarget = transform.InverseTransformPoint(worldTarget);
        // The rod runs along local +Y (RodTip sits at y ~ 2 in the prefab), so
        // the bend plane is spanned by Y and whichever sideways direction points
        // at the fish.
        Vector3 dir = new Vector3(localTarget.x, 0f, localTarget.z);
        if (dir.sqrMagnitude < 1e-6f) dir = _appliedDir;
        dir.Normalize();

        // Nothing meaningful changed — skip the vertex pass.
        if (!_restored
            && Mathf.Abs(_appliedAngle - degrees) < 0.05f
            && Vector3.Dot(_appliedDir, dir) > 0.9995f)
            return;

        Vector3 axis = Vector3.Cross(Vector3.up, dir);
        if (axis.sqrMagnitude < 1e-6f) { if (!_restored) RestoreAll(); return; }
        axis.Normalize();

        _appliedAngle = degrees;
        _appliedDir = dir;
        _appliedAxis = axis;
        _restored = false;

        Vector3 pivot = new Vector3(0f, _minAxis, 0f);

        for (int p = 0; p < _parts.Length; p++)
        {
            var part = _parts[p];
            var src = part.baseVerts;
            var dst = part.work;
            bool doNormals = part.baseNormals != null;

            for (int i = 0; i < src.Length; i++)
            {
                Vector3 v = src[i];
                float t = (v.y - _minAxis) / _axisLength;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                // Quadratic along the length: stiff at the butt, whippy at the tip.
                Quaternion q = Quaternion.AngleAxis(degrees * t * t, axis);
                Vector3 bent = pivot + q * (v - pivot);
                dst[i] = part.needsConversion
                    ? part.owner.InverseTransformPoint(transform.TransformPoint(bent))
                    : bent;
                if (doNormals)
                {
                    Vector3 n = q * part.baseNormals[i];
                    part.workNormals[i] = part.needsConversion
                        ? part.owner.InverseTransformDirection(transform.TransformDirection(n))
                        : n;
                }
            }

            part.mesh.vertices = dst;
            if (doNormals) part.mesh.normals = part.workNormals;
            part.mesh.RecalculateBounds();
        }
    }

    void RestoreAll()
    {
        if (_parts == null) return;
        for (int p = 0; p < _parts.Length; p++)
        {
            var part = _parts[p];
            if (part.needsConversion)
            {
                for (int i = 0; i < part.baseVerts.Length; i++)
                    part.work[i] = part.owner.InverseTransformPoint(
                        transform.TransformPoint(part.baseVerts[i]));
                part.mesh.vertices = part.work;
                if (part.baseNormals != null)
                {
                    for (int i = 0; i < part.baseNormals.Length; i++)
                        part.workNormals[i] = part.owner.InverseTransformDirection(
                            transform.TransformDirection(part.baseNormals[i]));
                    part.mesh.normals = part.workNormals;
                }
            }
            else
            {
                part.mesh.vertices = part.baseVerts;
                if (part.baseNormals != null) part.mesh.normals = part.baseNormals;
            }
            part.mesh.RecalculateBounds();
        }
        _restored = true;
        _appliedAngle = 0f;
    }

    void OnDestroy()
    {
        // The mesh copies are ours (Instantiate), so they are ours to clean up.
        // Without this every equip/unequip cycle leaks one mesh for the life of
        // the session.
        if (_parts == null) return;
        for (int i = 0; i < _parts.Length; i++)
            if (_parts[i].mesh != null) Destroy(_parts[i].mesh);
        _parts = null;
    }

    bool Build()
    {
        var filters = GetComponentsInChildren<MeshFilter>(true);
        if (filters == null || filters.Length == 0) { _failed = true; return false; }

        int total = 0;
        for (int i = 0; i < filters.Length; i++)
            if (filters[i].sharedMesh != null) total += filters[i].sharedMesh.vertexCount;
        if (total == 0 || total > MaxVertices)
        {
            Debug.LogWarning($"[RodBend] Not bending '{name}': {total} vertices (cap {MaxVertices}).");
            _failed = true;
            return false;
        }

        var parts = new System.Collections.Generic.List<Part>();
        float minY = float.MaxValue, maxY = float.MinValue;

        for (int i = 0; i < filters.Length; i++)
        {
            var f = filters[i];
            var source = f.sharedMesh;
            if (source == null) continue;

            // Read the authored data BEFORE swapping the mesh — assigning
            // MeshFilter.mesh replaces sharedMesh too.
            var verts = source.vertices;
            var norms = source.normals;
            bool child = f.transform != transform;

            // The bend is measured along the ROD ROOT's Y, so a child part's
            // vertices are converted into root space to be measured and bent,
            // then converted back when written. The rod is one mesh on the root
            // today; this keeps a re-authored prefab working.
            if (child)
            {
                for (int v = 0; v < verts.Length; v++)
                    verts[v] = transform.InverseTransformPoint(f.transform.TransformPoint(verts[v]));
                if (norms != null)
                    for (int v = 0; v < norms.Length; v++)
                        norms[v] = transform.InverseTransformDirection(
                            f.transform.TransformDirection(norms[v]));
            }

            // A per-instance copy: writing to sharedMesh would deform the
            // PROJECT ASSET and every other rod in the scene with it.
            var copy = Instantiate(source);
            copy.name = source.name + " (bendable)";
            copy.MarkDynamic();
            f.mesh = copy;

            var part = new Part
            {
                mesh = copy,
                baseVerts = verts,
                baseNormals = (norms != null && norms.Length == verts.Length) ? norms : null,
                work = new Vector3[verts.Length],
                needsConversion = child,
                owner = f.transform,
            };
            if (part.baseNormals != null) part.workNormals = new Vector3[verts.Length];
            parts.Add(part);

            for (int v = 0; v < verts.Length; v++)
            {
                if (verts[v].y < minY) minY = verts[v].y;
                if (verts[v].y > maxY) maxY = verts[v].y;
            }
        }

        if (parts.Count == 0) { _failed = true; return false; }

        _parts = parts.ToArray();
        _minAxis = minY;
        _axisLength = maxY - minY;
        if (_axisLength <= 0.0001f)
        {
            Debug.LogWarning($"[RodBend] '{name}' has no length along local Y — not bending.");
            _failed = true;
            return false;
        }
        return true;
    }
}
