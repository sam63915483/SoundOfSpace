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
    // 20°, up from the handoff's 12 (playtest 4: green spots were a 1% treasure
    // hunt on these craggy planets). The shuttle now LANDS TILTED along the
    // fitted plane, so a clean hillside is genuinely fine to sit on.
    const float MaxSlopeDeg = 20f;
    // Measured RELATIVE TO THE FITTED PLANE, not raw ray lengths — a uniform
    // slope has a huge raw spread but zero plane deviation; only real rocks,
    // ridges and cliff lips should trip this.
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
    PlayerController _pcCache;
    readonly float[] _distances = new float[RingRays + 1];
    readonly float[] _slopeDots = new float[RingRays + 1];
    readonly float[] _deviations = new float[RingRays + 1];   // heights off the fitted plane
    readonly Vector3[] _points = new Vector3[RingRays + 1];
    readonly Vector3[] _normals = new Vector3[RingRays + 1];
    readonly Collider[] _overlapHits = new Collider[8];

    public bool Valid => _valid;

    /// Why the last check failed ("" while valid) — the playtest debug
    /// overlay's line, so "can't land" is never a mystery again.
    public string FailReason { get; private set; } = "";

    /// World-space normal of the plane fitted through the 9 hits (average of
    /// the hit normals). The landing tilts the shuttle to this so hillsides
    /// are landable. Only meaningful while Valid.
    public Vector3 PlaneNormal { get; private set; } = Vector3.up;

    /// Highest bump ABOVE the fitted plane inside the footprint (m). The
    /// landing settles the gear this much higher so a spike can never end up
    /// inside the cabin — a rock through the floor squeezes the released
    /// player between two colliders and PhysX ejects them violently (the
    /// Icey Twin touchdown fling, playtest 7).
    public float MaxAboveDeviation { get; private set; }

    /// Centre ray's height OFF the fitted plane (signed; + = bump). The
    /// landing seats the gear on the PLANE, so this must be subtracted from
    /// the raw centre hit — seating straight from the centre hit floated the
    /// shuttle by a bump (or sank it by a dip) a foot or two (playtest 15).
    public float CenterDeviation { get; private set; }

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
        if (body == null) { FailReason = "NO BODY"; return false; }

        Vector3 origin = transform.position;
        Vector3 up = (origin - body.Position).normalized;

        // Yaw-stable ring basis.
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.right, up).normalized;
        Vector3 right = Vector3.Cross(up, fwd);

        Vector3 centreHit = Vector3.zero;
        bool anyMiss = false;
        for (int i = 0; i <= RingRays; i++)
        {
            Vector3 offset = Vector3.zero;
            if (i > 0)
            {
                float a = (i - 1) * (2f * Mathf.PI / RingRays);
                offset = (fwd * Mathf.Cos(a) + right * Mathf.Sin(a)) * FootprintRadius;
            }
            // Through the pilot's self-ignoring ray — the shuttle's own hull
            // and landing gear live on the terrain layer and used to be what
            // these rays hit (permanent red SLOPE from our own skirt).
            if (pilot != null && pilot.GroundRay(origin + offset, -up, MaxRayDistance, out RaycastHit hit))
            {
                _distances[i] = hit.distance;
                _slopeDots[i] = Vector3.Dot(hit.normal, up);
                _points[i] = hit.point;
                _normals[i] = hit.normal;
                if (i == 0) centreHit = hit.point;
            }
            else
            {
                _distances[i] = float.NaN;
                _slopeDots[i] = 0f;
                anyMiss = true;
            }
        }

        // Fit a plane through the hits (average point + average normal) and
        // measure each hit's height OFF that plane. A uniform hillside has a
        // large raw-distance spread but near-zero plane deviation — and the
        // shuttle lands tilted along the plane, so it's genuinely landable.
        if (!anyMiss)
        {
            Vector3 avgP = Vector3.zero, avgN = Vector3.zero;
            for (int i = 0; i <= RingRays; i++) { avgP += _points[i]; avgN += _normals[i]; }
            avgP /= RingRays + 1;
            avgN = avgN.sqrMagnitude > 0.001f ? avgN.normalized : up;
            PlaneNormal = avgN;
            float maxAbove = 0f;
            for (int i = 0; i <= RingRays; i++)
            {
                _deviations[i] = Vector3.Dot(_points[i] - avgP, avgN);
                if (_deviations[i] > maxAbove) maxAbove = _deviations[i];
            }
            MaxAboveDeviation = maxAbove;
            CenterDeviation = _deviations[0];
        }
        else
        {
            for (int i = 0; i <= RingRays; i++) _deviations[i] = float.NaN;
            CenterDeviation = 0f;
        }

        if (!ShuttleLandingLogic.EvaluateRays(_deviations, _slopeDots,
                Mathf.Cos(MaxSlopeDeg * Mathf.Deg2Rad), MaxFootprintHeightDelta))
        {
            // Which sub-check failed, for the overlay: a miss beats slope
            // beats spread (the same priority EvaluateRays rejects in).
            // Name the failing sub-check WITH the measured number, so tuning
            // the thresholds is done from evidence, not another blind guess.
            float worstSlopeDeg = 0f, minD = float.MaxValue, maxD = float.MinValue;
            for (int i = 0; i < _deviations.Length; i++)
            {
                if (float.IsNaN(_deviations[i])) continue;
                float deg = Mathf.Acos(Mathf.Clamp(_slopeDots[i], -1f, 1f)) * Mathf.Rad2Deg;
                if (deg > worstSlopeDeg) worstSlopeDeg = deg;
                if (_deviations[i] < minD) minD = _deviations[i];
                if (_deviations[i] > maxD) maxD = _deviations[i];
            }
            if (anyMiss) FailReason = "NO GROUND";
            else if (worstSlopeDeg > MaxSlopeDeg) FailReason = "SLOPE " + worstSlopeDeg.ToString("0") + "deg";
            else FailReason = "UNEVEN " + (maxD - minD).ToString("0.0") + "m";
            return false;
        }

        // No water: the touchdown point must sit clearly BELOW the analytic
        // ocean sphere to be rejected (the ocean is a post effect — no
        // collider). Strictly below, minus a margin: an icy planet's walkable
        // frozen-sea sheet has its collider AT the ocean radius, and the old
        // `+ 0.5` margin made every spot on it read as water (playtest 2 —
        // couldn't land anywhere on Icey Twin).
        float oceanR = OceanRadiusFor(body);
        if (oceanR > 0f && (centreHit - body.Position).magnitude < oceanR - 0.5f)
        {
            FailReason = "WATER";
            return false;
        }

        // Blockers in the footprint: trees, buildings, NPCs, props (all on
        // WorldProp) — triggers included, spawner props keep solid colliders
        // but pickups can be trigger-only.
        int count = Physics.OverlapSphereNonAlloc(centreHit + up * 2f, FootprintRadius + 1f,
                                                  _overlapHits, BlockerMask, QueryTriggerInteraction.Collide);
        if (count > 0)
        {
            FailReason = "BLOCKED: " + (_overlapHits[0] != null ? _overlapHits[0].name : "?");
            return false;
        }

        // Players (no player layer exists — distance test). Riders are inside
        // the shuttle 100 m up, so they never trip this. Cached — this used to
        // FindObjectOfType ten times a second (playtest 10 hitch hygiene).
        if (_pcCache == null) _pcCache = FindObjectOfType<PlayerController>();
        var pc = _pcCache;
        if (pc != null && !PlayerController.RiderMode
            && (pc.transform.position - centreHit).sqrMagnitude < (FootprintRadius + 1f) * (FootprintRadius + 1f))
        {
            FailReason = "PLAYER BELOW";
            return false;
        }
        var puppets = PlanetRelativeSync.AllPuppets;
        for (int i = 0; i < puppets.Count; i++)
        {
            var p = puppets[i];
            if (p == null || p.IsOwner) continue;
            if ((p.transform.position - centreHit).sqrMagnitude < (FootprintRadius + 1f) * (FootprintRadius + 1f))
            {
                FailReason = "PLAYER BELOW";
                return false;
            }
        }

        FailReason = "";
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
