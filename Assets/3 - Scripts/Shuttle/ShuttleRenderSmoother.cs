using UnityEngine;

// Render-rate interpolation for the autopilot's fixed-step pose — the manual
// equivalent of RigidbodyInterpolation for a transform-driven object. Each
// LateUpdate the shuttle's LOCAL pose is lerped between the last two fixed
// poses; the parent planet's own transform is interpolated by Unity (its rb is
// Interpolate), so the composed world pose is smooth at any frame rate.
//
// Order 50: after EndlessManager's origin shift (0), before CameraTransformFX
// (100) — the same slot ShuttleArrivalSequence documents. The authoritative
// pose lives in ShuttleAutopilot's fields; this component only ever WRITES
// transform.local*, never reads it back.
[DefaultExecutionOrder(50)]
public class ShuttleRenderSmoother : MonoBehaviour
{
    ShuttleAutopilot _pilot;
    float _lastFixedTime;

    // World-continuity across the mid-transit reparent: the old parent's
    // transform is INTERPOLATED (simulated body) while the new local pose was
    // computed in the physics frame, so the composed render pose snaps by the
    // interpolation offset (~1-2 m at orbital speed) on the switch frame —
    // the "big hitch" of playtest 5. Capture that difference and bleed it out.
    Vector3 _lastRenderWorld;
    Quaternion _lastRenderRot = Quaternion.identity;
    bool _hasLastRenderWorld;
    Vector3 _worldOffset;
    Quaternion _rotOffset = Quaternion.identity;   // rotation continuity across the reparent

    public void Init(ShuttleAutopilot pilot) { _pilot = pilot; }

    void FixedUpdate()
    {
        _lastFixedTime = Time.fixedTime;
    }

    void LateUpdate()
    {
        if (_pilot == null) return;
        if (!_pilot.GetSmoothingPose(out Vector3 prevPos, out Quaternion prevRot,
                                     out Vector3 curPos, out Quaternion curRot, out bool jumped))
        {
            // Parked. SETTLE onto the final pose instead of snapping — the
            // last rendered pose is mid-interpolation (+ any world offset),
            // and jumping straight to the committed parked pose was a visible
            // blip on every touchdown (playtest 9's landing hitch).
            if (_hasLastRenderWorld)
            {
                float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
                transform.localPosition = Vector3.Lerp(transform.localPosition, curPos, k);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, curRot, k);
                if ((transform.localPosition - curPos).sqrMagnitude < 0.0004f)
                {
                    transform.localPosition = curPos;
                    transform.localRotation = curRot;
                    _hasLastRenderWorld = false;   // settled — go idle
                }
                PlanetRelativeSync.ReplaceShuttleFramePuppets();
            }
            _worldOffset = Vector3.zero;
            _rotOffset = Quaternion.identity;
            return;
        }

        if (jumped)
        {
            transform.localPosition = curPos;
            transform.localRotation = curRot;
        }
        else
        {
            float t = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - _lastFixedTime) / Time.fixedDeltaTime)
                : 1f;
            transform.localPosition = Vector3.Lerp(prevPos, curPos, t);
            transform.localRotation = Quaternion.Slerp(prevRot, curRot, t);
        }

        // Reparent hitch removal (see the field comment): on the switch frame,
        // remember how far the composed render pose jumped and cancel it,
        // bleeding the correction to zero over ~a quarter second. Guarded so a
        // same-frame origin rebase (a ~1000 m legitimate jump) never feeds in.
        Vector3 world = transform.position;
        if (jumped && _hasLastRenderWorld)
        {
            Vector3 snap = _lastRenderWorld - world;
            if (snap.sqrMagnitude < 20f * 20f) _worldOffset = snap;
            // Rotation too (playtest 11's mid-flight rotation snap): the
            // composed render rotation shifts by the parents' interpolation
            // difference on the switch frame — carry it and bleed it out.
            Quaternion rotSnap = _lastRenderRot * Quaternion.Inverse(transform.rotation);
            if (Quaternion.Angle(Quaternion.identity, rotSnap) < 30f) _rotOffset = rotSnap;
        }
        if (_worldOffset.sqrMagnitude > 1e-8f)
        {
            // Gentler bleed than the first cut (14): at ~70 ms half-life the
            // reparent correction itself read as a blip (playtest 9).
            _worldOffset *= Mathf.Exp(-8f * Time.deltaTime);
            transform.position = world + _worldOffset;
        }
        if (Quaternion.Angle(Quaternion.identity, _rotOffset) > 0.01f)
        {
            _rotOffset = Quaternion.Slerp(_rotOffset, Quaternion.identity, 1f - Mathf.Exp(-8f * Time.deltaTime));
            transform.rotation = _rotOffset * transform.rotation;
        }
        _lastRenderWorld = transform.position;
        _lastRenderRot = transform.rotation;
        _hasLastRenderWorld = true;

        // Rider puppets are placed in the shuttle frame — re-place them from
        // the freshly smoothed pose or they trail the cabin by a whole step.
        PlanetRelativeSync.ReplaceShuttleFramePuppets();
    }
}
