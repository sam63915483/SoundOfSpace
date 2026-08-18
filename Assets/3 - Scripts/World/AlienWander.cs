using System.Collections;
using UnityEngine;

/// <summary>
/// Leashed wandering for spawned alien NPCs: pick a point within a radius of
/// the spawn cell, walk to it along the planet surface, idle, repeat. Never
/// steps below the ocean radius (land-locked to the patch they spawned on),
/// never onto slopes steeper than the spawner's placement limit, and pauses
/// whenever the player is close so talking/selling never chases a moving
/// target. Legs are animated procedurally in LateUpdate (the alien prefabs
/// have no Animator controller — same situation as NPCWaveAnimation, which
/// owns arms + head; this component owns thighs + calves, disjoint bones).
///
/// Added and configured at runtime by AlienNPCSpawner — nothing here is
/// prefab-serialized. All movement is in PLANET-LOCAL space (the spawner
/// parents aliens to their CelestialBody), so orbital motion is inherited
/// from the parent and needs no velocity carry. Raycasts resolve against
/// colliders at the PHYSICS pose, so world&lt;-&gt;local conversions go through
/// rb.position/rb.rotation, mirroring SpawnerCubeface.ParentToBodyPhysicsFrame
/// — converting through the interpolated render transform would re-introduce
/// the parenting-smear bug that ParentToBodyPhysicsFrame exists to fix.
/// </summary>
public class AlienWander : MonoBehaviour
{
    // ── Injected by AlienNPCSpawner.Configure every spawn ────────────────
    CelestialBody _planet;
    float _oceanRadius;        // 0 = no ocean on this body
    LayerMask _groundMask;     // spawner's mask (already excludes props/water/ship)
    float _seatDepth;          // bottomY*scale + groundOffset + embed*scale (spawner's seating formula)
    float _maxSurfaceAngle;    // reject steps onto steeper ground (matches placement rule)
    float _leashRadius;
    float _speed;
    float _idleMin, _idleMax;
    float _pauseDistance;      // player closer than this → hold still

    const float WaterMargin = 0.5f;      // stay this far above the ocean radius
    const float ArriveDistance = 0.5f;
    const float MinStroll = 6f;          // playtest: targets a metre away read as twitching, not walking
    const float ChainChance = 0.45f;     // odds of strolling again immediately instead of idling
    // Walking tolerates steeper ground than SPAWN placement does. The spawner
    // rejects cells over maxSurfaceAngle (35°) so nobody APPEARS on a cliff,
    // but a hillside leash contains plenty of ground steeper than that — and
    // reusing the strict limit froze every hill-spawned alien solid (Sam's
    // playtest): 10 target candidates, all rejected, forever.
    const float WalkSlopeSlack = 18f;
    const float ProbeUp = 3f;            // cast origin height above current feet
    const float ProbeRange = 9f;
    const float TurnSpeed = 4f;          // slerp rate toward walk direction

    Vector3 _homeLocal;        // planet-local spawn point (leash centre)
    Vector3 _targetLocal;
    bool _walkingState;
    float _idleUntil;
    bool _ready;

    // ── Approach mode (craving ambush, loop-feel C) ──────────────────────
    // Overrides the leash: walk toward a live target (the player), stop at a
    // conversational distance, face them. Same water/slope rules — an alien
    // that can't reach you reports Blocked and the director falls back to a
    // phone text instead.
    Transform _approachTarget;
    float _approachStop;
    float _approachGiveUpAt;
    public bool Approaching => _approachTarget != null;
    public bool ApproachArrived { get; private set; }
    public bool ApproachBlocked { get; private set; }

    public void BeginApproach(Transform target, float stopDistance, float giveUpSeconds = 60f)
    {
        _approachTarget = target;
        _approachStop = stopDistance;
        _approachGiveUpAt = Time.time + giveUpSeconds;
        ApproachArrived = false;
        ApproachBlocked = false;
        _walkingState = false;   // drop any stroll in progress
    }

    public void EndApproach()
    {
        _approachTarget = null;
        ApproachArrived = false;
        ApproachBlocked = false;
        _idleUntil = Time.time + Random.Range(_idleMin, _idleMax);
    }

    AlienNPCDamageable _damageable;
    Transform _player;

    // ── Remote drive (co-op) ─────────────────────────────────────────────
    //
    // The aliens themselves need no replication: AlienNPCSpawner is a
    // deterministic hash of (seed, body, cell), so both machines already spawn
    // the same aliens in the same cells. Only where they have WALKED TO differs,
    // and that is what AlienSync streams — planet-local, which this component
    // already works in, so nothing has to be converted.
    //
    // A guest stops deciding while poses are arriving. It does NOT stop
    // deciding forever: each machine streams aliens around ITS OWN player, so
    // the host may walk away from an alien the guest is still standing next to
    // and stop sending for it. Rather than leave that alien frozen, the drive
    // EXPIRES — no pose for RemoteHoldSeconds and it goes back to strolling on
    // its own, which is also exactly right when the guest is somewhere the host
    // has never been.
    const float RemoteHoldSeconds = 2f;
    float _remoteUntil;
    bool _remoteMoved;

    /// True while somebody else is deciding where this alien walks.
    public bool RemoteDriven => Time.time < _remoteUntil;

    /// <summary>
    /// Where the authority says this alien is standing. Planet-local, absolute
    /// rather than a delta, so a dropped packet self-corrects on the next one
    /// instead of leaving the two machines permanently out of step.
    ///
    /// `moved` drives the leg swing: the sender knows whether it took a step
    /// this tick, and deriving that from successive positions here would make
    /// the legs stutter whenever a packet was late.
    /// </summary>
    public void RemotePose(Vector3 localPos, Quaternion localRot, bool moved)
    {
        transform.localPosition = localPos;
        transform.localRotation = localRot;
        _remoteMoved = moved;
        _remoteUntil = Time.time + RemoteHoldSeconds;
        if (moved) _stridePhase += Mathf.PI * 0.5f;   // keep the stride advancing between packets
    }

    // ── Procedural leg swing ─────────────────────────────────────────────
    [Header("Leg Animation")]
    [SerializeField] float legSwingAngle = 26f;   // thigh forward/back, degrees
    [SerializeField] float kneeBendAngle = 18f;   // calf bend on the back-swing
    [SerializeField] float strideLength = 0.9f;   // metres per full step at scale 1 (scaled at Configure)

    Transform _thighR, _thighL, _calfR, _calfL;
    Quaternion _thighRRest, _thighLRest, _calfRRest, _calfLRest;
    // Rest poses are captured exactly once per GameObject — same rule as
    // NPCWaveAnimation: a pool despawn can freeze mid-stride, and re-capturing
    // that as "rest" random-walks the pose worse every reuse cycle.
    bool _legRestCaptured;
    // Body-right axis in each thigh's PARENT-local space (derived per spawn —
    // the placement orientation differs every time). Rotating the thigh about
    // this axis swings the leg forward/back in the sagittal plane.
    Vector3 _legAxisLocalR, _legAxisLocalL;
    float _stridePhase;
    float _legBlend;           // 0 = legs at rest, 1 = full swing
    float _scaledStride;
    bool _movedThisFrame;

    public void Configure(CelestialBody planet, float oceanRadius, LayerMask groundMask,
                          float seatDepth, float maxSurfaceAngle, float leashRadius,
                          float speed, float idleMin, float idleMax, float pauseDistance,
                          float scale)
    {
        _planet = planet;
        _oceanRadius = oceanRadius;
        _groundMask = groundMask;
        _seatDepth = seatDepth;
        _maxSurfaceAngle = maxSurfaceAngle;
        _leashRadius = leashRadius;
        _speed = speed;
        _idleMin = idleMin;
        _idleMax = idleMax;
        _pauseDistance = pauseDistance;
        _scaledStride = Mathf.Max(0.2f, strideLength * scale);

        _homeLocal = transform.localPosition;
        _walkingState = false;
        _approachTarget = null;
        ApproachArrived = false;
        ApproachBlocked = false;
        _idleUntil = Time.time + Random.Range(idleMin * 0.25f, idleMax * 0.5f);
        _stridePhase = 0f;
        _legBlend = 0f;
        _ready = false;

        if (_damageable == null) _damageable = GetComponent<AlienNPCDamageable>();
        StopAllCoroutines();
        StartCoroutine(InitBones());
    }

    IEnumerator InitBones()
    {
        // Mirror NPCWaveAnimation's init timing: let the first frame(s) settle
        // (SpawnFade starts at a tiny scale; bone-derived directions are
        // noisier there) before deriving axes from skeleton geometry.
        yield return new WaitForEndOfFrame();
        yield return null;

        if (_thighR == null)
        {
            // FBX names first (alien rig), Unity-humanoid fallbacks second.
            _thighR = FindDeepChild("thigh_r", "RightUpLeg");
            _thighL = FindDeepChild("thigh_l", "LeftUpLeg");
            _calfR  = FindDeepChild("calf_r",  "RightLeg");
            _calfL  = FindDeepChild("calf_l",  "LeftLeg");
        }

        if (_thighR == null || _thighL == null)
        {
            // No legs on this rig — wander with no leg swing rather than not
            // at all (the body still moves; better than a frozen statue).
            Debug.LogWarning("[AlienWander] thigh bones not found on " + name + "; walking without leg animation");
            _ready = true;
            yield break;
        }

        if (!_legRestCaptured)
        {
            _thighRRest = _thighR.localRotation;
            _thighLRest = _thighL.localRotation;
            if (_calfR != null) _calfRRest = _calfR.localRotation;
            if (_calfL != null) _calfLRest = _calfL.localRotation;
            _legRestCaptured = true;
        }

        // Body-right from the HIP SOCKETS (thigh joint origins) — pose-invariant
        // the same way NPCWaveAnimation's shoulder-socket axis is: sockets don't
        // move when the limbs below them rotate, so this reproduces the same
        // axis whether the previous life parked the legs cleanly or not.
        Vector3 bodyRight = _thighR.position - _thighL.position;
        Transform pelvis = FindDeepChild("pelvis", "Hips");
        Transform spine  = FindDeepChild("spine_01", "Spine");
        Vector3 bodyDown = (pelvis != null && spine != null)
            ? (pelvis.position - spine.position).normalized
            : -transform.up;
        bodyRight -= Vector3.Dot(bodyRight, bodyDown) * bodyDown;

        if (bodyRight.sqrMagnitude < 1e-8f)
        {
            Debug.LogWarning("[AlienWander] degenerate hip axis on " + name + "; walking without leg animation");
            _ready = true;
            yield break;
        }
        bodyRight.Normalize();

        // Sign self-test: +legSwingAngle about bodyRight must move the R foot
        // FORWARD (toward cross(bodyDown, bodyRight)), not backward. Testing
        // the rotated shaft against forward directly — the axis flips if the
        // rig's thigh sockets happen to be mirrored.
        Vector3 forward = Vector3.Cross(bodyDown, bodyRight).normalized;
        Vector3 shaft = _calfR != null ? (_calfR.position - _thighR.position).normalized : bodyDown;
        Vector3 swung = Quaternion.AngleAxis(legSwingAngle, bodyRight) * shaft;
        if (Vector3.Dot(swung - shaft, forward) < 0f) bodyRight = -bodyRight;

        _legAxisLocalR = Quaternion.Inverse(_thighR.parent.rotation) * bodyRight;
        _legAxisLocalL = Quaternion.Inverse(_thighL.parent.rotation) * bodyRight;

        _ready = true;
    }

    // Park the bones this component writes before the object is pooled, so
    // the next life re-initialises from a clean pose (NPCWaveAnimation rule).
    void OnDisable()
    {
        if (!_legRestCaptured) return;
        if (_thighR != null) _thighR.localRotation = _thighRRest;
        if (_thighL != null) _thighL.localRotation = _thighLRest;
        if (_calfR  != null) _calfR.localRotation  = _calfRRest;
        if (_calfL  != null) _calfL.localRotation  = _calfLRest;
    }

    void Update()
    {
        _movedThisFrame = false;
        if (!_ready || _planet == null) return;
        if (_damageable != null && _damageable.IsDying) return;

        // Somebody else is walking this one. Render only — the legs still swing
        // (that is the rendering path, and it must keep running) but nothing
        // here decides where the alien goes.
        if (RemoteDriven) { _movedThisFrame = _remoteMoved; return; }

        // Approach outranks everything — including the player-proximity
        // pause, which exists so shopkeepers don't stroll away; an ambusher
        // is SUPPOSED to close the distance.
        if (_approachTarget != null) { StepApproach(); return; }

        // Hold still while A player is close — a shopkeeper that strolls away
        // mid-deal (or mid-typewriter-line) reads as broken.
        //
        // ⚠️ CO-OP: "the player" silently means "the only player" in a co-op
        // rule, which is the trap this codebase has already paid for twice.
        // Aliens are walked by the host, so a check against the host's own body
        // would stroll this one away from a guest mid-sentence. The roster
        // covers everybody; it falls back to the tagged local player in single
        // player, which is the same lazy lookup as before.
        if (AnyPlayerWithin(_pauseDistance)) return;

        if (!_walkingState)
        {
            if (Time.time >= _idleUntil) PickNewTarget();
            return;
        }

        StepTowardTarget();
    }

    /// Is anybody standing this close? Every player, not just ours.
    bool AnyPlayerWithin(float distance)
    {
        float sqr = distance * distance;
        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i].Transform;
            if (t == null) continue;
            if ((t.position - transform.position).sqrMagnitude < sqr) return true;
        }
        if (all.Count > 0) return false;

        // No roster at all (single player before it has cached anything).
        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) _player = p.transform;
        }
        return _player != null
            && (_player.position - transform.position).sqrMagnitude < sqr;
    }

    void StepApproach()
    {
        if (Time.time > _approachGiveUpAt) { ApproachBlocked = true; return; }

        Vector3 targetLocal;
        var rb = _planet.Rigidbody;
        targetLocal = rb != null
            ? Quaternion.Inverse(rb.rotation) * (_approachTarget.position - rb.position)
            : _planet.transform.InverseTransformPoint(_approachTarget.position);

        Vector3 cur = transform.localPosition;
        Vector3 up = cur.normalized;
        Vector3 flat = Vector3.ProjectOnPlane(targetLocal - cur, up);
        float dist = flat.magnitude;

        if (dist <= _approachStop)
        {
            ApproachArrived = true;
            // Face them while standing there — the head already tracks; the
            // body turning too is what sells "I came here for YOU".
            Vector3 face = Vector3.ProjectOnPlane(flat, up);
            if (face.sqrMagnitude > 1e-6f)
                transform.localRotation = Quaternion.Slerp(transform.localRotation,
                    Quaternion.LookRotation(face.normalized, up), TurnSpeed * Time.deltaTime);
            return;
        }

        Vector3 stepDir = flat / dist;
        float stepLen = Mathf.Min(_speed * 1.35f * Time.deltaTime, dist);   // a little urgency
        Vector3 cand = cur + stepDir * stepLen;

        if (!ProbeGround(cand, out Vector3 groundLocal, out float groundR, out Vector3 normalLocal)
            || (_oceanRadius > 0f && groundR < _oceanRadius + WaterMargin)
            || Vector3.Angle(normalLocal, groundLocal.normalized) > _maxSurfaceAngle + WalkSlopeSlack)
        {
            // Water or a cliff between us — can't get there. Report it; the
            // director sends the hungry text instead.
            ApproachBlocked = true;
            return;
        }

        Vector3 candUp = groundLocal.normalized;
        transform.localPosition = groundLocal - candUp * _seatDepth;
        Vector3 fwd = Vector3.ProjectOnPlane(stepDir, candUp);
        if (fwd.sqrMagnitude > 1e-6f)
            transform.localRotation = Quaternion.Slerp(transform.localRotation,
                Quaternion.LookRotation(fwd.normalized, candUp), TurnSpeed * Time.deltaTime);

        _stridePhase += (stepLen / _scaledStride) * Mathf.PI * 2f;
        _movedThisFrame = true;
    }

    void PickNewTarget()
    {
        Vector3 homeUp = _homeLocal.normalized;
        float homeR = _homeLocal.magnitude;
        // Tangent basis at home, in planet-local space.
        Vector3 t1 = Vector3.Cross(homeUp,
            Mathf.Abs(Vector3.Dot(homeUp, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
        Vector3 t2 = Vector3.Cross(homeUp, t1);

        // Prefer proper strolls (>= MinStroll from where we're standing); keep
        // the first merely-valid candidate as a fallback so a cramped patch
        // (peninsula, cliff pocket) still produces SOME movement.
        bool haveFallback = false;
        Vector3 fallback = default;
        Vector3 curLocal = transform.localPosition;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 r = Random.insideUnitCircle * _leashRadius;
            Vector3 cand = (_homeLocal + t1 * r.x + t2 * r.y).normalized * homeR;
            if (!ProbeGround(cand, out Vector3 groundLocal, out float groundR, out Vector3 normalLocal))
                continue;
            if (_oceanRadius > 0f && groundR < _oceanRadius + WaterMargin) continue;   // land-locked
            if (Vector3.Angle(normalLocal, cand.normalized) > _maxSurfaceAngle + WalkSlopeSlack) continue;
            if (SpawnExclusionZone.IsExcluded(WorldOfLocal(groundLocal))) continue;

            Vector3 curUp = curLocal.normalized;
            float strollDist = Vector3.ProjectOnPlane(groundLocal - curLocal, curUp).magnitude;
            if (strollDist < MinStroll)
            {
                if (!haveFallback) { haveFallback = true; fallback = groundLocal; }
                continue;
            }

            _targetLocal = groundLocal;
            _walkingState = true;
            return;
        }

        if (haveFallback)
        {
            _targetLocal = fallback;
            _walkingState = true;
            return;
        }
        // Nowhere valid this round — sit tight and retry later.
        _idleUntil = Time.time + Random.Range(_idleMin, _idleMax);
    }

    void StepTowardTarget()
    {
        Vector3 cur = transform.localPosition;
        Vector3 up = cur.normalized;
        Vector3 flat = Vector3.ProjectOnPlane(_targetLocal - cur, up);
        float dist = flat.magnitude;
        if (dist < ArriveDistance) { Arrive(); return; }

        Vector3 stepDir = flat / dist;
        float stepLen = Mathf.Min(_speed * Time.deltaTime, dist);
        Vector3 cand = cur + stepDir * stepLen;

        if (!ProbeGround(cand, out Vector3 groundLocal, out float groundR, out Vector3 normalLocal))
        { Arrive(); return; }                                     // lost the ground — stop here
        if (_oceanRadius > 0f && groundR < _oceanRadius + WaterMargin) { Arrive(); return; }
        Vector3 candUp = groundLocal.normalized;
        if (Vector3.Angle(normalLocal, candUp) > _maxSurfaceAngle + WalkSlopeSlack) { Arrive(); return; }

        // Seat the feet with the exact spawner formula so there's no height
        // pop between "just spawned" and "took one step".
        transform.localPosition = groundLocal - candUp * _seatDepth;

        // Face the walk direction, staying upright on the local radial.
        Vector3 fwd = Vector3.ProjectOnPlane(stepDir, candUp);
        if (fwd.sqrMagnitude > 1e-6f)
        {
            Quaternion face = Quaternion.LookRotation(fwd.normalized, candUp);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, face,
                                                       TurnSpeed * Time.deltaTime);
        }

        _stridePhase += (stepLen / _scaledStride) * Mathf.PI * 2f;
        _movedThisFrame = true;
    }

    void Arrive()
    {
        _walkingState = false;
        // Playtest feedback: they stood around too much. Often roll straight
        // into the next stroll — an errand-runner, not a statue with hobbies.
        if (Random.value < ChainChance)
        {
            PickNewTarget();
            if (_walkingState) return;
        }
        _idleUntil = Time.time + Random.Range(_idleMin, _idleMax);
    }

    bool ProbeGround(Vector3 local, out Vector3 groundLocal, out float groundR, out Vector3 normalLocal)
    {
        groundLocal = default; groundR = 0f; normalLocal = default;
        Vector3 up = local.normalized;
        var rb = _planet.Rigidbody;
        Vector3 originWorld, downWorld;
        if (rb != null)
        {
            originWorld = rb.rotation * (local + up * ProbeUp) + rb.position;
            downWorld = rb.rotation * -up;
        }
        else
        {
            originWorld = _planet.transform.TransformPoint(local + up * ProbeUp);
            downWorld = _planet.transform.TransformDirection(-up);
        }
        if (!Physics.Raycast(originWorld, downWorld, out RaycastHit hit, ProbeRange,
                             _groundMask, QueryTriggerInteraction.Ignore))
            return false;

        if (rb != null)
        {
            Quaternion inv = Quaternion.Inverse(rb.rotation);
            groundLocal = inv * (hit.point - rb.position);
            normalLocal = inv * hit.normal;
        }
        else
        {
            groundLocal = _planet.transform.InverseTransformPoint(hit.point);
            normalLocal = _planet.transform.InverseTransformDirection(hit.normal);
        }
        groundR = groundLocal.magnitude;
        return true;
    }

    Vector3 WorldOfLocal(Vector3 local)
    {
        var rb = _planet.Rigidbody;
        return rb != null ? rb.rotation * local + rb.position
                          : _planet.transform.TransformPoint(local);
    }

    // LateUpdate so the leg writes land after any Animator, same slot
    // NPCWaveAnimation uses for arms + head. Bones are disjoint, so order
    // between the two components doesn't matter.
    void LateUpdate()
    {
        if (!_ready || _thighR == null || _thighL == null) return;
        if (_damageable != null && _damageable.IsDying) return;   // never fight the ragdoll

        _legBlend = Mathf.MoveTowards(_legBlend, _movedThisFrame ? 1f : 0f, Time.deltaTime * 5f);
        if (_legBlend <= 0.001f)
        {
            _thighR.localRotation = _thighRRest;
            _thighL.localRotation = _thighLRest;
            if (_calfR != null) _calfR.localRotation = _calfRRest;
            if (_calfL != null) _calfL.localRotation = _calfLRest;
            return;
        }

        float swing = Mathf.Sin(_stridePhase) * legSwingAngle * _legBlend;
        ApplyThigh(_thighR, _thighRRest, _legAxisLocalR, swing);
        ApplyThigh(_thighL, _thighLRest, _legAxisLocalL, -swing);

        // Knee bends as that leg swings backward — a straight-legged
        // back-swing clips the ground and reads robotic.
        if (_calfR != null)
            ApplyThigh(_calfR, _calfRRest, _legAxisLocalR, Mathf.Max(0f, -Mathf.Sin(_stridePhase)) * kneeBendAngle * _legBlend);
        if (_calfL != null)
            ApplyThigh(_calfL, _calfLRest, _legAxisLocalL, Mathf.Max(0f, Mathf.Sin(_stridePhase)) * kneeBendAngle * _legBlend);
    }

    // World-axis rotation applied in local space — the same shape as
    // NPCWaveAnimation's arm raise, so it stays correct at any surface
    // orientation.
    static void ApplyThigh(Transform bone, Quaternion rest, Vector3 axisLocal, float angle)
    {
        Quaternion parentRot = bone.parent.rotation;
        Vector3 axisWorld = parentRot * axisLocal;
        Quaternion worldRest = parentRot * rest;
        Quaternion worldSwung = Quaternion.AngleAxis(angle, axisWorld) * worldRest;
        bone.localRotation = Quaternion.Inverse(parentRot) * worldSwung;
    }

    Transform FindDeepChild(params string[] candidates)
    {
        var all = GetComponentsInChildren<Transform>(true);
        foreach (var n in candidates)
            foreach (Transform t in all)
                if (t.name == n) return t;
        return null;
    }
}
