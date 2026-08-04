using UnityEngine;
using System.Collections.Generic;

// Single source of truth for "is the player looking at this interactable?".
//
// PURE CROSSHAIR RAYCAST model: one SphereCast straight through the screen center
// each frame. You're "looking at" an object only if that cast hits a collider
// belonging to it — so you must actually point the center dot at the alien / chest
// / button, with no loose screen-rectangle generosity.
//
// The cast ignores trigger colliders (so the big interaction-radius triggers
// don't count) and respects occlusion (a wall between you and the object blocks
// it — you're not looking "through" it).
//
// When that thin cast lands on something else, a second FATTER sweep gets a
// near-miss vote (see NearMissHitsAim): the target still counts if the fat
// sphere reaches it at roughly the same depth as the nearest thing touched.
// That's what makes multi-part props aimable — a locker whose door handle is a
// separate sibling object used to be un-gazeable at its own centre.
//
// Trigger-only exception: a few interactables have NO solid geometry to hit —
// they're invisible "control points" with only a trigger zone (ship hatch/flight
// buttons, the reactor). The raycast can never hit those, so for them ONLY we
// fall back to a TIGHT view cone toward the zone center. Anything with a real
// collider (aliens, chest, pickups, NPCs) is strict-raycast and never uses it.
//
// Per-object `gazeTarget` (on Interactable) overrides what to aim at — point a
// small/empty control at the visible mesh it represents.
//
// Range is still enforced by each interactable's own trigger; this only answers
// the "looking at it" half. Fails OPEN (returns true) if gaze is disabled, there
// is no camera, or the player object is inactive (piloting / cutscenes).
public static class InteractGaze
{
    public static bool RequireGaze = true;

    /// <summary>Radius (world units) of the crosshair SphereCast. A small amount of
    /// fatness keeps aiming from feeling pixel-twitchy; 0 = a razor-thin ray.</summary>
    public static float AimRadius = 0.10f;

    /// <summary>How far the crosshair cast reaches. Just needs to exceed any
    /// interaction range (range itself is gated by each object's trigger).</summary>
    public static float MaxDistance = 25f;

    /// <summary>Extra world-space slack added around a target's silhouette on top
    /// of the crosshair's own radius. 0 keeps the near-miss test exactly as tight
    /// as the crosshair cast itself; raise it only if aiming feels fussy.</summary>
    public static float ExtraAimMargin = 0f;

    /// <summary>How far in front of the target something solid may sit before it
    /// counts as blocking the view. Covers clutter pressed against the target (a
    /// locker's own door handle, a bottle's table top) without letting a real
    /// occluder — a wall, the ship hull — be seen through.</summary>
    public static float ForgiveDepth = 0.25f;

    /// <summary>Tight fallback cone (degrees) for invisible trigger-only zones.</summary>
    const float InvisibleConeDeg = 6f;

    /// <summary>Extra slack (degrees) added around a visible collider-less object's
    /// angular silhouette so its edges are reachable, not just its center.</summary>
    const float ConeSlackDeg = 2.5f;

    static Camera _cam;
    static GameObject _player;

    // One shared crosshair cast per frame.
    static int _castFrame = -1;
    static bool _hasHit;
    static Transform _hitTf;
    static float _hitDist;

    static bool PlayerActive()
    {
        if (_player == null) _player = GameObject.FindGameObjectWithTag("Player");
        return _player != null && _player.activeInHierarchy;
    }

    static Camera Cam()
    {
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        return _cam;
    }

    public static bool IsLookingAt(Object target)
    {
        if (!RequireGaze) return true;
        if (target is Interactable ex && !ex.requireGazeToInteract) return true;
        if (!PlayerActive()) return true;          // piloting / cutscene
        var comp = target as Component;
        if (comp == null) return true;

        var cam = Cam();
        if (cam == null) return true;              // fail open

        Transform aim = comp.transform;
        if (target is Interactable it && it.gazeTarget != null) aim = it.gazeTarget;

        // Crosshair cast hits this object → looking at it.
        EnsureCast(cam);
        if (_hasHit && _hitTf != null && BelongsTo(_hitTf, aim)) return true;

        // X-ray option: test the crosshair ray against ONLY the aim's own
        // colliders, ignoring occluders (e.g. the ship hull when opening the
        // hatch from underneath the closed ship).
        if (target is Interactable itw && itw.gazeThroughWalls && AimRayHit(aim, cam))
            return true;

        // Has a real collider the cast could have hit? Then the cast is
        // authoritative about WHAT is in front of us — but not about whether the
        // player meant to point at us. Give it one forgiving near-miss pass first
        // (see NearMissHitsAim); if that fails too, we're genuinely not looked at.
        if (HasSolidCollider(aim)) return NearMissHitsAim(aim, cam);

        // No solid collider to raycast. Point at its actual silhouette — so a
        // long fishing rod works end-to-end rather than only dead-centre, but
        // pointing NEAR it isn't enough. (This used to use the sphere around the
        // object's world axis-aligned box, which for a long thin prop is vastly
        // bigger than the prop and felt sloppy.)
        Vector3 camPos = cam.transform.position;
        Ray meshRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int overlap = CrosshairOverlap(aim, meshRay, out _);
        if (overlap >= 0) return overlap == 1;

        // No mesh geometry at all. World-space UI (the note's paper canvas) still
        // has a silhouette worth aiming at.
        if (TryGetVisualBounds(aim, out Bounds rb))
        {
            Vector3 toC = rb.center - camPos;
            float dist = toC.magnitude;
            if (dist < 0.001f) return true;
            float angRadius = Mathf.Atan2(rb.extents.magnitude, dist) * Mathf.Rad2Deg;
            return Vector3.Angle(cam.transform.forward, toC) <= angRadius + ConeSlackDeg;
        }

        // Truly invisible trigger-only zone (no mesh): tight cone toward center.
        Vector3 to = AimCenter(aim) - camPos;
        if (to.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(cam.transform.forward, to) <= InvisibleConeDeg;
    }

    static readonly Vector3[] _corners = new Vector3[4];

    // Reusable buffers for the per-frame component scans below — the array-returning
    // GetComponentsInChildren<T>() allocates a fresh array on every call, and these
    // run every frame on the current prompt owner. The List overloads reuse storage.
    static readonly List<Renderer> _rendBuf = new List<Renderer>();
    static readonly List<Collider> _colBuf = new List<Collider>();
    static readonly List<UnityEngine.UI.Graphic> _graphicBuf = new List<UnityEngine.UI.Graphic>();
    static readonly List<MeshFilter> _meshBuf = new List<MeshFilter>();
    static readonly List<SkinnedMeshRenderer> _skinBuf = new List<SkinnedMeshRenderer>();

    static bool TryGetVisualBounds(Transform aim, out Bounds b)
    {
        b = default;
        bool any = false;
        aim.GetComponentsInChildren(_rendBuf);
        for (int i = 0; i < _rendBuf.Count; i++)
        {
            var r = _rendBuf[i];
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (any) return true;

        // No mesh renderer — fall back to world-space UI graphics (e.g. the
        // NotePickup "paper" Canvas), so a UI-only interactable is still aimable.
        aim.GetComponentsInChildren(_graphicBuf);
        for (int i = 0; i < _graphicBuf.Count; i++)
        {
            var g = _graphicBuf[i];
            if (g == null || !g.isActiveAndEnabled) continue;
            var canvas = g.canvas;
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;
            g.rectTransform.GetWorldCorners(_corners);
            for (int k = 0; k < 4; k++)
            {
                if (!any) { b = new Bounds(_corners[k], Vector3.zero); any = true; }
                else b.Encapsulate(_corners[k]);
            }
        }
        return any;
    }

    static void EnsureCast(Camera cam)
    {
        if (Time.frameCount == _castFrame) return;
        _castFrame = Time.frameCount;
        _hasHit = false;
        _hitTf = null;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool hit = AimRadius > 0.001f
            ? Physics.SphereCast(ray, AimRadius, out RaycastHit h, MaxDistance,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            : Physics.Raycast(ray, out h, MaxDistance,
                              Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hit && h.collider != null)
        {
            _hasHit = true;
            _hitTf = h.collider.transform;
            _hitDist = h.distance;
        }
    }

    // Does the hit collider belong to the aim object (it, or any descendant)?
    static bool BelongsTo(Transform hit, Transform aim)
    {
        return hit == aim || hit.IsChildOf(aim);
    }

    // ── Near-miss forgiveness ────────────────────────────────────────
    //
    // The thin crosshair cast reports exactly ONE collider, and the gaze only
    // passes if that collider belongs to the aim. That makes props assembled
    // from sibling GameObjects unaimable: the shuttle locker's door handle is
    // its own object sitting a few cm proud of the door, so pointing at the
    // middle of the locker hits the HANDLE and the gaze reads "looking away".
    // Small props suffer too — a strict target's crosshair slack shrinks with
    // distance (the water bottle is down to ~4 deg at 3 m, where a collider-less
    // prop gets a flat 6 deg cone and feels fine).
    //
    // The near-miss test deliberately does NOT use physics. Measured on the
    // rotating planet: a static collider's PhysX pose only advances at physics
    // steps, which don't line up with rendered frames, so the collider world
    // lags the rendered world by a cycling 0–0.5 m (the water bottle's collider
    // bounds cycled through three positions 0.47 m apart while its Transform sat
    // still). Physics.SyncTransforms() does not fix it — static actors aren't
    // re-synced that way. That lag is nothing to a 2.4 m locker and fatal to a
    // 20 cm bottle, which is exactly the bug this fixes.
    //
    // So the forgiveness works in TRANSFORM space, which is always current: is
    // the crosshair inside the target's on-screen silhouette (+ a little slack)?
    // The raycast is still consulted, but only to answer "is something solid in
    // front of it" — a wall or the ship hull still blocks, while clutter pressed
    // against the target (a locker's own door handle) does not.
    static bool NearMissHitsAim(Transform aim, Camera cam)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (CrosshairOverlap(aim, ray, out float nearFace) != 1) return false;

        // Occlusion: the crosshair must not be resting on something solid that
        // sits meaningfully in front of us. A zero distance means the camera
        // started inside that collider (the player's own body) — not an occluder.
        float blocker = (_hasHit && _hitDist > 0.001f) ? _hitDist : float.MaxValue;
        return blocker >= nearFace - ForgiveDepth;
    }

    // "Would the crosshair sphere have hit this object's RENDERED silhouette?"
    //
    // Each mesh is tested in its OWN local space, so a rotated or long thin prop
    // keeps its real shape. Measuring instead by the sphere around a world
    // axis-aligned box inflates twice over — the AABB of a rotated object is
    // already larger than the object, and the circumscribing sphere is larger
    // again (the locker became a 1.63 m sphere around a 0.84 m box: ~47 deg of
    // slack, which read as sloppy). The box is grown by the crosshair's own
    // radius and nothing more, so this is exactly as forgiving as the real cast
    // — it just measures against the drawn pose instead of the lagging
    // physics one.
    //
    // Returns 1 = crosshair is on it, 0 = missed, -1 = object has no mesh
    // geometry to measure (caller falls back to a cone).
    static int CrosshairOverlap(Transform aim, Ray ray, out float entry)
    {
        entry = float.MaxValue;
        bool anyGeometry = false, hit = false;
        float margin = AimRadius + ExtraAimMargin;

        aim.GetComponentsInChildren(_meshBuf);
        for (int i = 0; i < _meshBuf.Count; i++)
        {
            var mf = _meshBuf[i];
            if (mf == null || mf.sharedMesh == null) continue;
            anyGeometry = true;
            if (TestLocalBox(mf.transform, mf.sharedMesh.bounds, ray, margin, ref entry)) hit = true;
        }

        // Skinned props (NPCs, aliens) have no MeshFilter — use the renderer's
        // own local bounds instead.
        //
        // CRITICAL: a SkinnedMeshRenderer's localBounds are expressed relative to
        // its ROOT BONE, not to its own transform. Testing them in sm.transform
        // space puts the box somewhere else entirely — measured on the start-cabin
        // Tev, the box came out 1.7×6.5×6.9 m and ~2 m adrift of a body whose real
        // world bounds are 3.8×3.9×4.7 m. The ray missed from every angle except
        // dead-centre-and-close, so an NPC with no solid collider could not be
        // looked at, which meant no "Press F" prompt and no way to talk to him.
        // Transforming through rootBone reproduces the renderer's world bounds
        // exactly.
        aim.GetComponentsInChildren(_skinBuf);
        for (int i = 0; i < _skinBuf.Count; i++)
        {
            var sm = _skinBuf[i];
            if (sm == null || !sm.enabled) continue;
            anyGeometry = true;
            Transform boundsSpace = sm.rootBone != null ? sm.rootBone : sm.transform;
            if (TestLocalBox(boundsSpace, sm.localBounds, ray, margin, ref entry)) hit = true;
        }

        if (!anyGeometry) return -1;
        return hit ? 1 : 0;
    }

    static bool TestLocalBox(Transform t, Bounds local, Ray ray, float margin, ref float entry)
    {
        // Grow by the crosshair radius, converted into this transform's units so
        // the slack stays a constant world size whatever the object's scale is.
        Vector3 s = t.lossyScale;
        local.Expand(new Vector3(
            2f * margin / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
            2f * margin / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
            2f * margin / Mathf.Max(0.0001f, Mathf.Abs(s.z))));

        // InverseTransformVector, NOT InverseTransformDirection — the latter
        // ignores scale, and mixing a scale-applied origin with a scale-free
        // direction skews the ray (the locker's 0.7/2/0.45 scale made it miss
        // its own box). Ray normalises the direction, which is harmless once it
        // points along the right local line.
        var lr = new Ray(t.InverseTransformPoint(ray.origin),
                         t.InverseTransformVector(ray.direction));
        if (!local.IntersectRay(lr, out float tLocal)) return false;

        // Back to world for a real distance (local units aren't world units
        // once the transform carries scale).
        float d = Vector3.Distance(ray.origin, t.TransformPoint(lr.origin + lr.direction * tLocal));
        if (d < entry) entry = d;
        return true;
    }

    // Tests the crosshair ray directly against the aim's own colliders, ignoring
    // everything else in the world (see-through). Collider.Raycast hits only that
    // collider, so occluders like the hull don't block it.
    static bool AimRayHit(Transform aim, Camera cam)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        aim.GetComponentsInChildren(_colBuf);
        for (int i = 0; i < _colBuf.Count; i++)
        {
            var c = _colBuf[i];
            if (c == null || c.isTrigger) continue;
            if (c.Raycast(ray, out _, MaxDistance)) return true;
        }
        return false;
    }

    static bool HasSolidCollider(Transform aim)
    {
        // Only ENABLED, non-trigger colliders make the crosshair cast authoritative — a
        // disabled collider can't be raycast-hit, so counting it here would make IsLookingAt
        // always fail (it did: a repurposed enemy model kept a disabled CharacterController,
        // which is a Collider, so gaze never resolved on it). Skip disabled + trigger colliders.
        aim.GetComponentsInChildren(_colBuf);
        for (int i = 0; i < _colBuf.Count; i++)
            if (_colBuf[i] != null && _colBuf[i].enabled && !_colBuf[i].isTrigger) return true;
        return false;
    }

    // Center of the aim's geometry — renderer bounds, else collider bounds, else pivot.
    static Vector3 AimCenter(Transform aim)
    {
        aim.GetComponentsInChildren(_rendBuf);
        for (int i = 0; i < _rendBuf.Count; i++)
            if (_rendBuf[i] != null && _rendBuf[i].enabled && !(_rendBuf[i] is ParticleSystemRenderer))
                return _rendBuf[i].bounds.center;

        aim.GetComponentsInChildren(_colBuf);
        for (int i = 0; i < _colBuf.Count; i++)
            if (_colBuf[i] != null) return _colBuf[i].bounds.center;

        return aim.position;
    }
}
