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
            return;   // parked — the authored pose stands, nothing to smooth

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

        // Rider puppets are placed in the shuttle frame — re-place them from
        // the freshly smoothed pose or they trail the cabin by a whole step.
        PlanetRelativeSync.ReplaceShuttleFramePuppets();
    }
}
