using UnityEngine;

/// <summary>
/// Spawns the real cassette model as a PURE PROP — no physics, no pickup, no
/// gravity. Shared by the insert slot and the eject.
///
/// The model is Sam's existing `Assets/1 - samsPrefabs/CasettePickup.prefab`
/// (spelled "Casette", one s). That prefab is a WORLD PICKUP: it carries
/// GravityObjectSimple, CassettePickup, PickupHoldOffset, a Rigidbody and two
/// colliders. Instantiated as-is next to the computer it would fall through the
/// shuttle, offer its own pickup prompt, and — worst of all — its solid collider
/// would sit in front of the slot and eat the crosshair raycast that the slot
/// needs to be looked at.
///
/// So every instance is STRIPPED down to transforms, mesh filters and renderers
/// before it is ever shown. Nothing that moves it, nothing that can be picked
/// up, nothing the gaze cast can hit.
/// </summary>
public static class CassetteVisual
{
    /// Set true to print what each spawned tape measured and how it was
    /// rotated. See the call site in Spawn.
    public const bool LogOrientation = false;

    /// <summary>
    /// Instantiate the model under an unscaled anchor beneath
    /// <paramref name="parent"/>. Returns null if no prefab was assigned, so
    /// callers degrade to "the deck works, you just can't see the tape" rather
    /// than throwing every frame.
    ///
    /// <paramref name="euler"/> is the tape's rotation relative to the slot,
    /// PLAIN AND UNMODIFIED. Type 90 into an axis and the tape turns 90° about
    /// that axis. Nothing is added to it, derived from it, or composed with it.
    ///
    /// ── Why there is no auto-orientation any more ────────────────────────
    /// There was, three times: a hardcoded 90, then "point the thin axis up",
    /// then "match the slot's thin axis". Each was defensible and each was
    /// wrong in play, and each one silently fought the value in the Inspector —
    /// so setting the field by hand didn't work either, which is the worst
    /// outcome of the three. A slot's orientation is a placement decision with
    /// a human looking at it; measuring geometry is not a substitute for that.
    /// The field below is the whole mechanism now.
    /// </summary>
    public static GameObject Spawn(GameObject prefab, Transform parent, float scale,
                                   Vector3 euler)
    {
        if (prefab == null || parent == null) return null;

        Transform anchor = EnsureAnchor(parent);

        // ⚠️ instantiateInWorldSpace: FALSE. The two-argument
        // Instantiate(prefab, parent) keeps the object's WORLD transform, which
        // for a prefab asset means "put it wherever the asset's own transform
        // sits" and then back-solve a local offset. With floating origin the
        // shuttle lives near world zero, so that dumped the tape a couple of
        // feet off the console, hanging in mid-air. Passing false keeps the
        // prefab's LOCAL transform relative to the anchor, which is what the
        // slot actually wants.
        GameObject go = Object.Instantiate(prefab, anchor, false);
        go.name = "CassetteProp";
        Strip(go);

        // Belt and braces on the same point: the tape starts at the anchor
        // origin and every caller positions it explicitly from there.
        go.transform.localPosition = Vector3.zero;

        // The prefab's own root scale (0.02 on Casette) is the model's real
        // size, so it is kept and only multiplied by the per-slot tweak.
        if (scale <= 0f) scale = 1f;
        go.transform.localScale = prefab.transform.localScale * scale;
        go.transform.localRotation = Quaternion.Euler(euler);

        // Diagnostic from the orientation hunt. Off by default — it fires on
        // every insert and every print, which is console spam once the rotation
        // is set. Flip it on if a tape ever comes out facing the wrong way
        // again: it reports the mesh, the slot, and the angle actually applied,
        // and its ABSENCE proves you are running a stale build.
        if (LogOrientation)
            Debug.Log("[CASSETTE] " + Describe(go) +
                      "  slot=" + MeasureSlot(parent).ToString("F3") +
                      "  tapeEuler=" + euler +
                      "  parent=" + parent.name);

        return go;
    }

    /// <summary>
    /// The rotation that lays THIS model flat, MEASURED rather than assumed.
    ///
    /// ── Why measured ─────────────────────────────────────────────────────
    /// This was hardcoded twice and wrong twice. A cassette is a slab: its
    /// thinnest dimension is perpendicular to the faces you read. So "lying
    /// flat" is exactly "thinnest axis points up", and that is something the
    /// mesh can be asked about instead of guessed at.
    ///
    /// Caset.obj measures 9.36 x 5.82 x 0.77, so its thin axis is Z and it
    /// stands up like a picture frame at rest — but nothing here depends on
    /// those numbers. Swap in a different cassette model, authored along any
    /// axis, and it still lands flat.
    ///
    /// Reads sharedMesh.bounds rather than Renderer.bounds: renderer bounds are
    /// a WORLD-space AABB, which for a rotated or scaled object describes the
    /// box around the object, not the object.
    /// </summary>
    /// <summary>
    /// How long the tape actually is IN THE WORLD, along its longest axis.
    ///
    /// ── Why anything positional should be expressed in these ─────────────
    /// The console this slot lives on is scaled down hard — Shuttle 1.2 →
    /// ConsoleNeck 0.1 → ConsoleStand 0.55/1.3/0.45 → insert 0.41/0.08/0.63,
    /// which lands the slot at roughly three centimetres across. A distance
    /// written as "0.18 metres" is therefore meaningless as a design value: it
    /// is either invisible or absurd depending on a scale chain nobody is
    /// looking at while they tune it. Three separate attempts to pick a number
    /// in metres all read as "no change".
    ///
    /// A cassette length is the unit the eye is actually using. "Push it out
    /// 1.2 tape lengths" means the same thing on a doll's house console and a
    /// life-sized one, and it survives anyone rescaling the shuttle.
    /// </summary>
    public static float WorldLength(GameObject instance)
    {
        if (instance == null) return 0f;

        Vector3 local = MeasureLocal(instance);
        if (local == Vector3.zero) return 0f;

        Vector3 s = instance.transform.lossyScale;
        Vector3 world = new Vector3(local.x * Mathf.Abs(s.x),
                                    local.y * Mathf.Abs(s.y),
                                    local.z * Mathf.Abs(s.z));
        return Mathf.Max(world.x, Mathf.Max(world.y, world.z));
    }

    /// One line describing what the measurement found, for the Console.
    public static string Describe(GameObject instance)
    {
        Vector3 size = MeasureLocal(instance);
        Vector3 thin = ThinAxis(size);
        string axis = thin == Vector3.right ? "X" : thin == Vector3.up ? "Y" : "Z";
        return "mesh=" + size.ToString("F3") + " thinAxis=" + axis;
    }

    /// <summary>
    /// Lay the tape flat IN THE SLOT — align the cassette's face normal with the
    /// slot's face normal.
    ///
    /// ── Why it is measured from the slot, not from "up" ──────────────────
    /// This was hardcoded wrong, then aligned to world up, and neither held.
    /// A cassette slot is a SLIT: a car stereo's is a horizontal slit in a
    /// vertical face, and the tape goes in flat with respect to THE SLIT, not
    /// with respect to the ground. Aligning to up only happens to be right when
    /// the slot faces up.
    ///
    /// Both objects are slabs, so both have a well-defined thinnest axis, and
    /// the whole problem is just "make these two normals agree". Nothing is
    /// assumed about how either the model or the slot mesh was authored, so
    /// re-shaping the insert box or swapping the cassette model keeps working.
    /// </summary>
    public static Quaternion AlignRotation(GameObject instance, Vector3 slotNormal)
    {
        Vector3 size = MeasureLocal(instance);
        if (size == Vector3.zero) return Quaternion.identity;
        return Quaternion.FromToRotation(ThinAxis(size), slotNormal);
    }

    /// <summary>
    /// The slot's own face normal, in its local space: the axis the insert mesh
    /// is THINNEST along once its scale is taken into account.
    ///
    /// The scale is the whole point — a slot built by squashing a cube has a
    /// (1,1,1) mesh and a (0.41, 0.08, 0.63) transform, so the mesh alone says
    /// nothing about which way the slit faces.
    ///
    /// Falls back to up for a slot with no mesh, which is the best guess
    /// available when there is no shape to read.
    /// </summary>
    public static Vector3 SlotNormal(Transform slot)
    {
        Vector3 size = MeasureSlot(slot);
        return size == Vector3.zero ? Vector3.up : ThinAxis(size);
    }

    /// The slot's extents along its own local axes, scale included.
    public static Vector3 MeasureSlot(Transform slot)
    {
        if (slot == null) return Vector3.zero;

        Vector3 local = Vector3.zero;
        foreach (var mf in slot.GetComponents<MeshFilter>())
            if (mf.sharedMesh != null) local = Vector3.Max(local, mf.sharedMesh.bounds.size);

        // No mesh of its own? A collider describes the shape just as well, and a
        // slot built as an empty with a box trigger is a reasonable thing to do.
        if (local == Vector3.zero)
            foreach (var bc in slot.GetComponents<BoxCollider>())
                local = Vector3.Max(local, bc.size);

        if (local == Vector3.zero) return Vector3.zero;

        Vector3 s = slot.lossyScale;
        return new Vector3(local.x * Mathf.Abs(s.x),
                           local.y * Mathf.Abs(s.y),
                           local.z * Mathf.Abs(s.z));
    }

    static string AxisName(Vector3 axis)
    {
        return axis == Vector3.right ? "X" : axis == Vector3.up ? "Y" : "Z";
    }

    /// The model's extents in its OWN local space. Zero if it has no meshes.
    static Vector3 MeasureLocal(GameObject instance)
    {
        if (instance == null) return Vector3.zero;

        Transform root = instance.transform;
        bool any = false;
        Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;

        foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            // Each mesh's bounds are in ITS transform's space; bring all eight
            // corners into the instance root's space before combining, or a
            // child offset silently skews the result.
            Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds b = mesh.bounds;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
                any = true;
            }
        }

        return any ? hi - lo : Vector3.zero;
    }

    /// Which local axis the slab is THINNEST along — the face normal.
    static Vector3 ThinAxis(Vector3 size)
    {
        return size.x <= size.y && size.x <= size.z ? Vector3.right
             : size.y <= size.z ? Vector3.up
             : Vector3.forward;
    }

    /// <summary>
    /// A scale-cancelling child to hang the tape off.
    ///
    /// ── Why this is not optional ─────────────────────────────────────────
    /// The insert Sam built is a stretched box — localScale (0.41, 0.08, 0.63).
    /// Parent a cassette straight to it and the cassette inherits that stretch:
    /// squashed to a twelfth of its height and read as a beer mat. Any slot
    /// mesh built by scaling a cube has the same problem, which is most of them.
    ///
    /// The anchor's localScale is set to the componentwise inverse of the
    /// parent's LOSSY scale, so the anchor sits at world scale 1 no matter what
    /// the whole chain above it is doing. Two things follow, both wanted:
    ///   • the tape renders at exactly the prefab's authored size, and
    ///   • seatedOffset / approachOffset are in REAL METRES along the insert's
    ///     own axes, instead of being multiplied by whatever the box was
    ///     stretched to.
    ///
    /// (Approximate if a non-uniform scale sits above a rotation — Unity's
    /// lossyScale is itself approximate there. Nothing in the shuttle does that.)
    /// </summary>
    public static Transform EnsureAnchor(Transform parent)
    {
        Transform existing = parent.Find(AnchorName);
        if (existing == null)
        {
            var anchor = new GameObject(AnchorName);
            anchor.transform.SetParent(parent, false);
            existing = anchor.transform;
        }

        Vector3 lossy = parent.lossyScale;
        existing.localScale = new Vector3(Inv(lossy.x), Inv(lossy.y), Inv(lossy.z));
        existing.localPosition = Vector3.zero;
        existing.localRotation = Quaternion.identity;
        return existing;
    }

    const string AnchorName = "CassetteAnchor";

    static float Inv(float v)
    {
        return Mathf.Abs(v) < 1e-5f ? 1f : 1f / v;
    }

    /// <summary>
    /// Everything that isn't geometry, gone. Deliberately destroys rather than
    /// disables: a disabled Rigidbody still participates in some queries, and a
    /// disabled Collider is exactly the thing that used to be hard to reason
    /// about when the slot wouldn't take a gaze hit.
    /// </summary>
    public static void Strip(GameObject go)
    {
        if (go == null) return;

        // ── NEUTRALISE FIRST, DESTROY SECOND ─────────────────────────────
        // Object.Destroy is DEFERRED to the end of the frame. That gap is not
        // academic: CasettePickup.prefab carries a Rigidbody and
        // GravityObjectSimple, so a freshly instantiated tape had live physics
        // for a frame or two and PHYSICS WRITES THE TRANSFORM — it re-oriented
        // the cassette out from under the rotation set immediately after.
        //
        // The tell was that inserting looked wrong while ejecting looked right:
        // the ejection REUSES the tape already in the slot, so it never went
        // through Instantiate and never had a Rigidbody to fight.
        //
        // Kinematic + disabled costs nothing and takes effect this instant, so
        // nothing can move or rotate the prop in the window before it is gone.
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;

        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);

        // Any behaviour at all — GravityObjectSimple, CassettePickup,
        // PickupHoldOffset, and anything Sam adds to that prefab later. A prop
        // should never run logic.
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
    }

    static void Destroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
