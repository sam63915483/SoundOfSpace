using System.Collections.Generic;
using UnityEngine;

// Landing-validity check for the autopilot's HOVER phase (handoff §5).
// Host-authoritative: only the simulating machine casts; guests receive the
// replicated bool via SetRemoteValid. Runs at 10 Hz while active.
//
// Valid = 9 rays (centre + ring at the gear footprint) all hit TERRAIN
// (layer 10 "Body", triggers ignored — ocean waterlines are triggers on Body),
// every hit flatter than MaxSlopeDeg, hit-distance spread under
// MaxFootprintHeightDelta, the touchdown point above the ocean radius, and an
// OverlapSphere on the blocker mask (WorldProp 3 — trees, buildings, NPCs,
// mushrooms, props all live there) plus a player-distance test all clear.
public class ShuttleLandingSensor : MonoBehaviour
{
    public const float FootprintRadius = 6f;    // Sam to confirm (handoff §5)
    const float MaxSlopeDeg = 12f;
    const float MaxFootprintHeightDelta = 1.5f;
    const float MaxRayDistance = 200f;
    const float CheckInterval = 0.1f;
    const int RingRays = 8;

    static readonly int BlockerMask = 1 << 3;   // WorldProp

    bool _active;
    float _nextCheckAt;
    bool _valid;
    CelestialBody _oceanBody;
    float _oceanRadius;
    readonly float[] _distances = new float[RingRays + 1];
    readonly float[] _slopeDots = new float[RingRays + 1];
    readonly Collider[] _overlapHits = new Collider[8];

    public bool Valid => _valid;

    public void SetActive(bool active)
    {
        _active = active;
        if (!active) _valid = false;
        _nextCheckAt = 0f;
    }

    /// Guest-side setter (ShuttleSync applies the host's replicated bool).
    public void SetRemoteValid(bool valid) { _valid = valid; }

    void FixedUpdate()
    {
        if (!_active || ShuttleAutopilot.ClientDriven) return;
        if (Time.fixedTime < _nextCheckAt) return;
        _nextCheckAt = Time.fixedTime + CheckInterval;
        _valid = Evaluate();
    }

    bool Evaluate()
    {
        var pilot = ShuttleAutopilot.Instance;
        var body = pilot != null ? pilot.CurrentBody : GetComponentInParent<CelestialBody>();
        if (body == null) return false;

        Vector3 origin = transform.position;
        Vector3 up = (origin - body.Position).normalized;

        // Yaw-stable ring basis.
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.right, up).normalized;
        Vector3 right = Vector3.Cross(up, fwd);

        Vector3 centreHit = Vector3.zero;
        for (int i = 0; i <= RingRays; i++)
        {
            Vector3 offset = Vector3.zero;
            if (i > 0)
            {
                float a = (i - 1) * (2f * Mathf.PI / RingRays);
                offset = (fwd * Mathf.Cos(a) + right * Mathf.Sin(a)) * FootprintRadius;
            }
            if (Physics.Raycast(origin + offset, -up, out RaycastHit hit, MaxRayDistance,
                                ShuttleAutopilot.GroundMask, QueryTriggerInteraction.Ignore))
            {
                _distances[i] = hit.distance;
                _slopeDots[i] = Vector3.Dot(hit.normal, up);
                if (i == 0) centreHit = hit.point;
            }
            else
            {
                _distances[i] = float.NaN;
                _slopeDots[i] = 0f;
            }
        }

        if (!ShuttleLandingLogic.EvaluateRays(_distances, _slopeDots,
                Mathf.Cos(MaxSlopeDeg * Mathf.Deg2Rad), MaxFootprintHeightDelta))
            return false;

        // No water: the touchdown point must sit above the analytic ocean
        // sphere (the ocean is a post effect — there's no collider to hit).
        float oceanR = OceanRadiusFor(body);
        if (oceanR > 0f && (centreHit - body.Position).magnitude < oceanR + 0.5f)
            return false;

        // Blockers in the footprint: trees, buildings, NPCs, props (all on
        // WorldProp) — triggers included, spawner props keep solid colliders
        // but pickups can be trigger-only.
        int count = Physics.OverlapSphereNonAlloc(centreHit + up * 2f, FootprintRadius + 1f,
                                                  _overlapHits, BlockerMask, QueryTriggerInteraction.Collide);
        if (count > 0) return false;

        // Players (no player layer exists — distance test). Riders are inside
        // the shuttle 100 m up, so they never trip this.
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null && !PlayerController.RiderMode
            && (pc.transform.position - centreHit).sqrMagnitude < (FootprintRadius + 1f) * (FootprintRadius + 1f))
            return false;
        var puppets = PlanetRelativeSync.AllPuppets;
        for (int i = 0; i < puppets.Count; i++)
        {
            var p = puppets[i];
            if (p == null || p.IsOwner) continue;
            if ((p.transform.position - centreHit).sqrMagnitude < (FootprintRadius + 1f) * (FootprintRadius + 1f))
                return false;
        }

        return true;
    }

    // Ocean radius per body, resolved once (recursive GetComponentInChildren is
    // expensive — EnemySpawner's lesson). CelestialBodyGenerator is in the
    // read-only Celestial zone: call it, never edit it.
    float OceanRadiusFor(CelestialBody body)
    {
        if (body == _oceanBody) return _oceanRadius;
        _oceanBody = body;
        _oceanRadius = 0f;
        var gen = body.GetComponentInChildren<CelestialBodyGenerator>();
        if (gen != null) _oceanRadius = gen.GetOceanRadius();
        return _oceanRadius;
    }
}
