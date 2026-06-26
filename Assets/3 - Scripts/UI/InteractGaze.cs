using UnityEngine;

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
        // authoritative — it didn't hit us, so we're not being looked at.
        if (HasSolidCollider(aim)) return false;

        // No solid collider to raycast. If it's VISIBLE, accept looking anywhere
        // within its on-screen silhouette (angular size of its renderer bounds +
        // a little slack) — so e.g. a long fishing rod works end-to-end, not just
        // dead-center.
        Vector3 camPos = cam.transform.position;
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

    static bool TryGetVisualBounds(Transform aim, out Bounds b)
    {
        b = default;
        bool any = false;
        var rends = aim.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (any) return true;

        // No mesh renderer — fall back to world-space UI graphics (e.g. the
        // NotePickup "paper" Canvas), so a UI-only interactable is still aimable.
        var graphics = aim.GetComponentsInChildren<UnityEngine.UI.Graphic>();
        for (int i = 0; i < graphics.Length; i++)
        {
            var g = graphics[i];
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
        }
    }

    // Does the hit collider belong to the aim object (it, or any descendant)?
    static bool BelongsTo(Transform hit, Transform aim)
    {
        return hit == aim || hit.IsChildOf(aim);
    }

    // Tests the crosshair ray directly against the aim's own colliders, ignoring
    // everything else in the world (see-through). Collider.Raycast hits only that
    // collider, so occluders like the hull don't block it.
    static bool AimRayHit(Transform aim, Camera cam)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        var cols = aim.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null || c.isTrigger) continue;
            if (c.Raycast(ray, out _, MaxDistance)) return true;
        }
        return false;
    }

    static bool HasSolidCollider(Transform aim)
    {
        var cols = aim.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null && !cols[i].isTrigger) return true;
        return false;
    }

    // Center of the aim's geometry — renderer bounds, else collider bounds, else pivot.
    static Vector3 AimCenter(Transform aim)
    {
        var rends = aim.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
            if (rends[i] != null && rends[i].enabled && !(rends[i] is ParticleSystemRenderer))
                return rends[i].bounds.center;

        var cols = aim.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null) return cols[i].bounds.center;

        return aim.position;
    }
}
