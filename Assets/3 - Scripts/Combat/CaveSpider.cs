using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A hostile cave spider (Sam, 2026-09-23). Lives in the moon's cave network,
/// spawned by <see cref="CaveSpiderSpawner"/>.
///
/// HOW IT BEHAVES
///   Territory — the network's top level is no-man's-land. Spiders never climb
///   up into it, and a chase ends the moment the player reaches it: they are
///   pushing you out of their home, not hunting you across the moon.
///   Noticing — PROXIMITY, so you can sneak: a spider only notices a player
///   inside its notice radius with a clear line to them (larger if the player
///   sprints, larger for the big ones). It turns and hisses, then comes.
///   Chasing — each spider has a LANE: floor, wall or roof. Wall and roof
///   runners peel off to the side, run up the tunnel wall and along the
///   ceiling toward you, and POUNCE when they get close. Floor runners just
///   come and bite. Spiders on the floor never leap at a player on the floor;
///   a floating player gets leapt at from anywhere.
///
/// HOW IT MOVES
///   Glued to a surface: position + (smoothed, multi-sample) surface normal.
///   Each step probes ahead (a wall in the way: climb it), down (follow the
///   surface), then back under an edge (wrap round a lip). All three miss →
///   it lets go and falls. Airborne it is ballistic in the moon's own pull
///   (Universe.GravityAcceleration: tiny inside the moon, zero in the core).
///
///   The whole pose lives in the MOON'S LOCAL SPACE and is interpolated there
///   every rendered frame (Update). A kinematic body written in world space
///   under an orbiting, spinning moon renders a step behind it — the jitter
///   the first version had. The Rigidbody is only here so the collider moves
///   cheaply; nothing writes its world pose.
///
/// Sight is not <see cref="EnemyVision"/> any more: Sam wanted proximity,
/// which is a line check inside a radius, not a view cone with a meter.
/// Damage still comes in through <see cref="IDamageable"/>, the door the
/// pistol, axe and blade already use.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CaveSpider : MonoBehaviour, IDamageable
{
    public static readonly List<CaveSpider> AllInstances = new List<CaveSpider>();

    public enum Mode { Docile, Alert, Chasing, Searching, Returning }
    public enum Lane { Floor, Wall, Roof }

    [Header("Health")]
    [Tooltip("At scale 1. Multiplied by the spider's size.")]
    public float maxHealth = 45f;

    [Header("Crawl")]
    public float wanderSpeed = 1.0f;
    public float chaseSpeed = 4.2f;
    public float searchSpeed = 2.6f;
    [Tooltip("How far a wandering spider strays from home before it heads back.")]
    public float wanderRadius = 9f;
    [Tooltip("How quickly the body settles onto a new surface angle (higher = snappier, lower = smoother).")]
    public float surfaceAlignRate = 12f;

    [Header("Noticing you (proximity)")]
    [Tooltip("Metres, at scale 1. Walk closer than this in the open and it notices you.")]
    public float noticeRadius = 5.5f;
    [Tooltip("Sprinting multiplies the notice radius.")]
    public float sprintNoticeMult = 1.8f;
    [Tooltip("The turn-and-hiss before it comes for you.")]
    public float alertSeconds = 0.6f;
    [Tooltip("While chasing it keeps track of you out to this range, if it can see you.")]
    public float chaseSightRange = 30f;
    [Tooltip("Lost you this long → it goes to look where it last saw you.")]
    public float loseSightGrace = 3f;
    public float searchSeconds = 8f;
    [Tooltip("A spider that starts a chase wakes the spiders within this range.")]
    public float swarmRadius = 8f;
    [Tooltip("Docile spiders farther than this from every player stop moving (cost saver).")]
    public float sleepDistance = 40f;

    [Header("Bite")]
    public float biteDamage = 7f;
    public float biteInterval = 0.9f;
    [Tooltip("Reach, metres at scale 1 (grows with the spider).")]
    public float biteRange = 0.9f;

    [Header("Pounce / leap")]
    public float leapDamage = 11f;
    [Tooltip("Launch speed, metres per second. Low gravity: this is what makes it floaty.")]
    public float leapSpeed = 6.5f;
    [Tooltip("The crouch before a pounce — the tell that lets you dodge.")]
    public float leapWindup = 0.45f;
    public float leapCooldown = 2.5f;
    [Tooltip("Wall / roof runners pounce from this close (metres, grows a little with size).")]
    public float pounceRange = 5f;
    [Tooltip("A floating player (nothing under their feet) is leapt at from this far, from any surface.")]
    public float leapRangeFloating = 12f;
    [Tooltip("How much of your drift it aims ahead of you (0 = where you are now).")]
    public float leapLead = 0.6f;

    [Header("Lanes (chance per spider; the rest run the floor)")]
    [Range(0, 1)] public float roofRunnerChance = 0.4f;
    [Range(0, 1)] public float wallRunnerChance = 0.3f;
    [Tooltip("Floor spiders look this far to either side for a wall to run up.")]
    public float laneWallSearch = 7f;

    [Header("Audio")]
    public AudioClip biteClip;
    public AudioClip alertClip;
    public AudioClip walkLoopClip;
    public AudioClip deathClip;

    [Header("Animation")]
    [Tooltip("Crawl speed (m/s at scale 1) at which the walk clip plays at normal speed.")]
    public float walkAnimReferenceSpeed = 1.2f;
    [Tooltip("Fastest the body can turn to face a new way, degrees per second (big spiders turn slower). Stops the spin-in-place when its path flips.")]
    public float bodyTurnDegPerSec = 280f;

    // ── Runtime ─────────────────────────────────────────────────────────────
    Rigidbody _rb;
    Animator _anim;
    CelestialBody _moon;
    CaveSpiderSpawner _spawner;
    AudioSource _loop, _oneShot;
    Collider[] _ownColliders;

    Mode _mode = Mode.Docile;
    public Mode State => _mode;
    public bool IsDead => _dead;
    Lane _lane;
    float _sideSign = 1f;

    float _scale = 1f;
    float _health;
    bool _dead;
    float _deathTimer;
    Vector3 _deathScale;

    // Territory (off for test spiders dropped on other bodies).
    bool _territory;
    float _giveUpDepth, _minDepth;

    // Pose, MOON-LOCAL.
    bool _attached;
    Vector3 _pos, _n, _heading;              // _n is the smoothed surface normal
    Vector3 _airVel;                         // local, relative to the moon
    float _airTime;
    Vector3 _prevRenderPos, _curRenderPos;
    Quaternion _prevRenderRot, _curRenderRot;

    // Behaviour.
    Vector3 _home;
    Transform _target;
    Vector3 _lastSeen;                       // local
    bool _hasLastSeen;
    float _modeTimer, _loseSightTimer, _nextSense, _wanderTurnTimer, _wanderPauseUntil;
    float _nextBite, _nextLeap, _leapWindupT = -1f, _attackPoseUntil;
    bool _leapHitLanded;
    float _stuckTimer, _detourUntil;
    Vector3 _stuckRef;
    float _laneProbeAt;
    bool _laneWallBeside, _laneRoofAbove;

    // Visual facing (local, on the surface plane) — turns at a capped rate.
    Vector3 _facing;
    // The surface it last climbed OFF, briefly, so it can't flip straight back
    // onto it (the floor↔wall ping-pong in a corner was the wall spin).
    Vector3 _leftNormal;
    float _leftTime = -10f;
    float _nextIgnoreRefresh;

    // Animation.
    float _speedSmoothed;
    bool _walkingAnim;

    static readonly int HashWalk = Animator.StringToHash("isWalking");
    static readonly int HashAttack = Animator.StringToHash("isAttacking");
    static readonly int HashDead1 = Animator.StringToHash("isDead1");
    static readonly int HashDead2 = Animator.StringToHash("isDead2");
    static readonly int HashDead3 = Animator.StringToHash("isDead3");

    // Ship / Sun / FishPreview / IgnoreRaycast are never surfaces.
    const int SurfaceMask = ~((1 << 2) | (1 << 9) | (1 << 11) | (1 << 12));
    static readonly RaycastHit[] s_hits = new RaycastHit[16];

    void OnEnable() { if (!AllInstances.Contains(this)) AllInstances.Add(this); }
    void OnDisable() { AllInstances.Remove(this); }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.None;   // we interpolate in moon space ourselves
        _anim = GetComponentInChildren<Animator>(true);
        if (_anim != null) _anim.applyRootMotion = false;
        _ownColliders = GetComponentsInChildren<Collider>(true);

        if (walkLoopClip != null)
        {
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.clip = walkLoopClip;
            _loop.loop = true;
            _loop.playOnAwake = false;
            Configure3D(_loop, 0.35f);
        }
        _oneShot = gameObject.AddComponent<AudioSource>();
        _oneShot.playOnAwake = false;
        Configure3D(_oneShot, 1f);
    }

    static void Configure3D(AudioSource s, float volume)
    {
        s.spatialBlend = 1f;
        s.volume = volume;
        s.minDistance = 2f;
        s.maxDistance = 30f;
        s.rolloffMode = AudioRolloffMode.Linear;
    }

    /// Called by the spawner right after Instantiate + SetParent(moon).
    /// `giveUpDepth` ≤ 0 → no territory (test spiders on other bodies).
    public void Init(CaveSpiderSpawner spawner, CelestialBody moon, Vector3 surfacePoint, Vector3 surfaceNormal,
                     float scale, float giveUpDepth, float minDepth)
    {
        _spawner = spawner;
        _moon = moon;
        _scale = scale;
        _territory = giveUpDepth > 0f;
        _giveUpDepth = giveUpDepth;
        _minDepth = minDepth;

        float parentScale = transform.parent != null ? Mathf.Max(1e-4f, transform.parent.lossyScale.x) : 1f;
        transform.localScale = Vector3.one * (scale / parentScale);
        _health = maxHealth * scale;

        float r = Random.value;
        _lane = r < roofRunnerChance ? Lane.Roof : r < roofRunnerChance + wallRunnerChance ? Lane.Wall : Lane.Floor;
        _sideSign = Random.value < 0.5f ? -1f : 1f;

        _pos = ToLocalPoint(surfacePoint);
        _n = ToLocalDir(surfaceNormal).normalized;
        _heading = RandomTangent(_n);
        _facing = _heading;
        _home = _pos;
        _attached = true;
        _curRenderPos = _prevRenderPos = _pos;
        _curRenderRot = _prevRenderRot = Quaternion.LookRotation(_heading, _n);
        ApplyRenderPose(1f);
        _nextLeap = Time.time + Random.Range(0.5f, 2f);
        _nextSense = Time.time + Random.Range(0f, 0.25f);
    }

    // ── Frames ──────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (_moon == null) return;
        float dt = Time.fixedDeltaTime;
        _prevRenderPos = _curRenderPos;
        _prevRenderRot = _curRenderRot;
        Vector3 before = _pos;

        if (_dead) TickDeath(dt);
        else if (_mode == Mode.Docile && !AnyPlayerWithin(sleepDistance)) { /* asleep: hold */ }
        else
        {
            Sense();
            if (!_attached) TickAirborne(dt);
            else switch (_mode)
            {
                case Mode.Docile:    TickWander(dt, wanderSpeed); break;
                case Mode.Alert:     TickAlert(dt); break;
                case Mode.Chasing:   TickChase(dt); break;
                case Mode.Searching: TickSearch(dt); break;
                case Mode.Returning: TickReturn(dt); break;
            }
        }

        _curRenderPos = _pos;
        Vector3 want = Vector3.ProjectOnPlane(!_attached && _airVel.sqrMagnitude > 0.01f ? _airVel : _heading, _n);
        Vector3 cur = Vector3.ProjectOnPlane(_facing, _n);            // carry the facing onto the new surface
        if (cur.sqrMagnitude < 1e-6f) cur = want;
        if (want.sqrMagnitude > 1e-6f && cur.sqrMagnitude > 1e-6f)
        {
            float maxRad = bodyTurnDegPerSec / Mathf.Sqrt(Mathf.Max(1f, _scale)) * Mathf.Deg2Rad * dt;
            _facing = Vector3.RotateTowards(cur.normalized, want.normalized, maxRad, 0f);
        }
        if (_facing.sqrMagnitude > 1e-6f && Vector3.Cross(_facing, _n).sqrMagnitude > 1e-6f)
            _curRenderRot = Quaternion.LookRotation(_facing.normalized, _n);

        if (Time.time >= _nextIgnoreRefresh) { _nextIgnoreRefresh = Time.time + 1f; IgnorePlayerContact(); }

        // Crawl speed drives the walk animation (debounced in Update — flicking
        // between walk and idle every other step was the "weird animation" glitch).
        float v = _attached ? (_pos - before).magnitude / dt : 0f;
        _speedSmoothed = Mathf.Lerp(_speedSmoothed, v, 1f - Mathf.Exp(-10f * dt));
    }

    void Update()
    {
        if (_moon == null) return;
        float a = Mathf.Clamp01((Time.time - Time.fixedTime) / Mathf.Max(1e-4f, Time.fixedDeltaTime));
        ApplyRenderPose(a);
        if (_dead || _anim == null) return;

        bool walk = _walkingAnim ? _speedSmoothed > 0.12f : _speedSmoothed > 0.3f;
        if (walk != _walkingAnim)
        {
            _walkingAnim = walk;
            _anim.SetBool(HashWalk, walk);
            if (_loop != null) { if (walk) _loop.Play(); else _loop.Pause(); }
        }
        _anim.SetBool(HashAttack, Time.time < _attackPoseUntil);
        float refSpeed = walkAnimReferenceSpeed * Mathf.Sqrt(_scale);   // big legs cycle slower
        _anim.speed = walk ? Mathf.Clamp(_speedSmoothed / refSpeed, 0.6f, 3f) : 1f;
    }

    void ApplyRenderPose(float a)
    {
        transform.localPosition = Vector3.Lerp(_prevRenderPos, _curRenderPos, a);
        transform.localRotation = Quaternion.Slerp(_prevRenderRot, _curRenderRot, a);
    }

    // ── Senses / modes ──────────────────────────────────────────────────────

    void Sense()
    {
        if (Time.time < _nextSense) return;
        _nextSense = Time.time + 0.2f;

        Vector3 eye = World(_pos + _n * 0.3f * _scale);

        if (_mode == Mode.Chasing || _mode == Mode.Alert)
        {
            if (_target == null) { StartReturn(); return; }
            if (_territory && Depth(_target.position) < _giveUpDepth) { StartReturn(); return; }   // you made it out
            bool sees = (_target.position - eye).sqrMagnitude < chaseSightRange * chaseSightRange
                        && ClearLine(eye, _target.position);
            if (sees) { _loseSightTimer = 0f; _lastSeen = ToLocalPoint(_target.position); _hasLastSeen = true; }
            else if ((_loseSightTimer += 0.2f) >= loseSightGrace && _mode == Mode.Chasing)
            { _mode = Mode.Searching; _modeTimer = 0f; }
            return;
        }

        // Docile / Searching / Returning: proximity notice.
        float sizeBoost = Mathf.Lerp(1f, 1.5f, Mathf.InverseLerp(0.7f, 3f, _scale));
        float searchBoost = _mode == Mode.Searching ? 1.6f : 1f;
        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i].Transform;
            if (t == null) continue;
            if (_territory && Depth(t.position) < _giveUpDepth) continue;   // up top you're left alone
            float r = noticeRadius * sizeBoost * searchBoost * (all[i].IsSprinting ? sprintNoticeMult : 1f);
            if ((t.position - eye).sqrMagnitude > r * r) continue;
            if (!ClearLine(eye, t.position)) continue;
            Notice(t);
            return;
        }
    }

    void Notice(Transform t)
    {
        _target = t;
        _lastSeen = ToLocalPoint(t.position);
        _hasLastSeen = true;
        _loseSightTimer = 0f;
        if (_mode == Mode.Searching) { EnterChase(); return; }   // already hunting: no second hiss
        _mode = Mode.Alert;
        _modeTimer = 0f;
        if (alertClip != null) _oneShot.PlayOneShot(alertClip, 0.9f);
    }

    void EnterChase()
    {
        _mode = Mode.Chasing;
        _loseSightTimer = 0f;
        _stuckTimer = 0f;
        _stuckRef = _pos;

        // Swarm: a spider going for you wakes the ones right around it.
        float r2 = swarmRadius * swarmRadius;
        Vector3 me = World(_pos);
        for (int i = 0; i < AllInstances.Count; i++)
        {
            var o = AllInstances[i];
            if (o == null || o == this || o._dead || o._moon == null) continue;
            if (o._mode == Mode.Chasing || o._mode == Mode.Alert) continue;
            if ((o.World(o._pos) - me).sqrMagnitude > r2) continue;
            o.Notice(_target);
        }
    }

    /// Gunshots: every spider within `radius` of the bang comes for the shooter.
    public static void AlertNearby(Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        var shooter = PlayerRoster.Nearest(pos, out _);
        if (shooter == null) return;
        for (int i = 0; i < AllInstances.Count; i++)
        {
            var s = AllInstances[i];
            if (s == null || s._dead || s._moon == null) continue;
            if ((s.World(s._pos) - pos).sqrMagnitude > r2) continue;
            if (s._territory && s.Depth(shooter.position) < s._giveUpDepth) continue;
            if (s._mode != Mode.Chasing && s._mode != Mode.Alert) s.Notice(shooter);
        }
    }

    void StartReturn()
    {
        _mode = Mode.Returning;
        _modeTimer = 0f;
        _target = null;
        _leapWindupT = -1f;
    }

    // ── Behaviours (attached) ───────────────────────────────────────────────

    void TickWander(float dt, float speed)
    {
        _wanderTurnTimer -= dt;
        if (_wanderTurnTimer <= 0f)
        {
            _wanderTurnTimer = Random.Range(1.5f, 4f);
            Vector3 toHome = _home - _pos;
            if (toHome.magnitude > wanderRadius) _heading = TangentOr(toHome, _n, _heading);
            else _heading = Quaternion.AngleAxis(Random.Range(-100f, 100f), _n) * _heading;
            if (Random.value < 0.35f) _wanderPauseUntil = Time.time + Random.Range(1f, 3f);
        }
        if (Time.time < _wanderPauseUntil) return;   // sits still for a beat
        Crawl(_heading, speed * dt, dt);
    }

    void TickAlert(float dt)
    {
        _modeTimer += dt;
        if (_target != null) Face(ToLocalPoint(_target.position) - _pos, dt, 10f);
        if (_modeTimer >= alertSeconds) EnterChase();
    }

    void TickSearch(float dt)
    {
        _modeTimer += dt;
        if (_modeTimer >= searchSeconds || !_hasLastSeen) { StartReturn(); return; }
        Vector3 to = _lastSeen - _pos;
        if (to.magnitude < 2f) { TickWander(dt, wanderSpeed); return; }
        Crawl(Time.time < _detourUntil ? _heading : TangentOr(to, _n, _heading), searchSpeed * dt, dt);
        TickStuck(false);
    }

    void TickReturn(float dt)
    {
        _modeTimer += dt;
        Vector3 to = _home - _pos;
        if (to.magnitude < 2.5f || _modeTimer > 40f) { _mode = Mode.Docile; return; }
        Crawl(Time.time < _detourUntil ? _heading : TangentOr(to, _n, _heading), searchSpeed * dt, dt);
        TickStuck(false);
    }

    void TickChase(float dt)
    {
        if (_target == null) { StartReturn(); return; }
        Vector3 toT = ToLocalPoint(_target.position) - _pos;
        float dist = toT.magnitude;
        Vector3 up = LocalUp(_pos);
        float upDot = Vector3.Dot(_n, up);
        bool onFloor = upDot > 0.55f;

        // Crouched, about to pounce: hold still, then go.
        if (_leapWindupT >= 0f)
        {
            _leapWindupT += dt;
            Face(toT, dt, 12f);
            if (_leapWindupT >= leapWindup) Launch();
            return;
        }

        float reach = biteRange * _scale + 0.5f;
        if (dist <= reach) { Face(toT, dt, 12f); TryBite(); return; }

        if (Time.time >= _nextLeap && WantsPounce(dist, onFloor))
        {
            _leapWindupT = 0f;
            _leapHitLanded = false;
            return;
        }

        Vector3 heading = Time.time < _detourUntil ? _heading : LaneHeading(toT, dist, up, upDot);
        Crawl(heading, chaseSpeed * dt, dt);
        TickStuck(true);
    }

    /// Where to run this step. Floor runners go straight for you; wall and roof
    /// runners peel off to the side, up the wall and (roof) onto the ceiling.
    Vector3 LaneHeading(Vector3 toT, float dist, Vector3 up, float upDot)
    {
        Vector3 direct = TangentOr(toT, _n, _heading);
        if (_lane == Lane.Floor || dist < pounceRange * 0.8f) return direct;

        // Cheap wall / roof look-around, a few times a second.
        if (Time.time >= _laneProbeAt)
        {
            _laneProbeAt = Time.time + 0.4f;
            Vector3 eye = World(_pos + _n * 0.3f * _scale);
            Vector3 sideL = Vector3.Cross(up, direct);
            if (sideL.sqrMagnitude < 1e-6f) sideL = Vector3.Cross(_n, direct);
            Vector3 sideW = World(sideL.normalized * _sideSign, true);
            _laneWallBeside = Probe(eye, sideW, laneWallSearch, out _);
            if (!_laneWallBeside && Probe(eye, -sideW, laneWallSearch, out _)) { _sideSign = -_sideSign; _laneWallBeside = true; }
            _laneRoofAbove = Probe(eye, World(up, true), laneWallSearch * 1.3f, out _);
        }

        if (upDot > 0.55f)
        {
            // On the floor: angle across to the wall.
            if (!_laneWallBeside) return direct;
            Vector3 side = Vector3.Cross(up, direct);
            if (side.sqrMagnitude < 1e-6f) return direct;
            return TangentOr(direct * 0.45f + side.normalized * _sideSign, _n, direct);
        }
        if (upDot > -0.55f)
        {
            // On a wall: roof runners climb, wall runners hold their height.
            Vector3 wallUp = Vector3.ProjectOnPlane(up, _n);
            if (wallUp.sqrMagnitude < 1e-6f) return direct;
            wallUp.Normalize();
            float vert = Vector3.Dot(direct, wallUp);
            Vector3 along = direct - wallUp * vert;
            if (_lane == Lane.Roof && _laneRoofAbove) return TangentOr(along * 0.7f + wallUp, _n, direct);
            return TangentOr(along + wallUp * Mathf.Max(vert, 0f) * 0.3f, _n, direct);
        }
        // On the roof: straight at you along the ceiling.
        return direct;
    }

    bool WantsPounce(float dist, bool onFloor)
    {
        if (dist < 1.2f || _target == null) return false;
        float range;
        if (IsFloating(_target)) range = leapRangeFloating;
        else if (onFloor) return false;                       // floor to floor: it runs and bites
        else range = pounceRange * Mathf.Lerp(1f, 1.4f, Mathf.InverseLerp(0.7f, 3f, _scale));
        if (dist > range) return false;
        // You have to be on the open side of whatever it clings to.
        if (Vector3.Dot(ToLocalPoint(_target.position) - _pos, _n) < 0.1f) return false;
        return ClearPath(World(_pos + _n * 0.35f * _scale), _target.position, 0.2f * _scale);
    }

    // A chase or search that has made no headway (a maze corner the straight
    // crawl can't solve): pounce if allowed, or commit to a detour for a moment.
    void TickStuck(bool chasing)
    {
        _stuckTimer += Time.fixedDeltaTime;
        if (_stuckTimer < 2f) return;
        _stuckTimer = 0f;
        if ((_pos - _stuckRef).sqrMagnitude < 0.6f * 0.6f)
        {
            bool canPounce = chasing && _target != null && Time.time >= _nextLeap
                             && WantsPounce((ToLocalPoint(_target.position) - _pos).magnitude, Vector3.Dot(_n, LocalUp(_pos)) > 0.55f);
            if (canPounce) { _leapWindupT = 0f; _leapHitLanded = false; }
            else
            {
                _heading = Quaternion.AngleAxis(Random.Range(60f, 300f), _n) * _heading;
                _detourUntil = Time.time + 1.5f;
            }
        }
        _stuckRef = _pos;
    }

    void TryBite()
    {
        if (Time.time < _nextBite) return;
        _nextBite = Time.time + biteInterval;
        _attackPoseUntil = Time.time + 0.45f;
        if (biteClip != null) _oneShot.PlayOneShot(biteClip, 1f);
        DamageTarget(biteDamage);
    }

    void DamageTarget(float amount)
    {
        // Each machine runs its own spiders and only ever hurts its own player.
        if (_target == null || !PlayerRoster.IsLocalPlayer(_target)) return;
        ResourceManager.Instance?.TakeDamage(amount);
    }

    // ── Leap / airborne ─────────────────────────────────────────────────────

    void Launch()
    {
        _leapWindupT = -1f;
        _nextLeap = Time.time + leapCooldown;
        if (_target == null) return;

        Vector3 start = World(_pos + _n * 0.3f * _scale);
        Vector3 aim = _target.position;
        var trb = _target.GetComponentInParent<Rigidbody>();
        if (trb != null) aim += (trb.velocity - _moon.velocity) * leapLead * ((aim - start).magnitude / leapSpeed);

        float T = Mathf.Max(0.15f, (aim - start).magnitude / leapSpeed);
        Vector3 g = Universe.GravityAcceleration((start + aim) * 0.5f, _moon);
        Vector3 vWorld = (aim - start) / T - 0.5f * g * T;
        _pos += _n * 0.3f * _scale;          // step off the surface so the first sweep isn't touching it
        Detach(ToLocalDir(vWorld));
        if (alertClip != null) _oneShot.PlayOneShot(alertClip, 0.6f);
    }

    void TickAirborne(float dt)
    {
        _airTime += dt;
        Vector3 posW = World(_pos);
        Vector3 gW = Universe.GravityAcceleration(posW, _moon);
        _airVel += ToLocalDir(gW) * dt;
        Vector3 stepL = _airVel * dt;
        float len = stepL.magnitude;
        float radius = 0.2f * _scale;

        // Hit the player mid-flight: bite and bounce off.
        if (!_leapHitLanded && !_dead && _target != null
            && (_target.position - posW).sqrMagnitude < Sq(biteRange * _scale + 0.5f))
        {
            _leapHitLanded = true;
            _attackPoseUntil = Time.time + 0.45f;
            if (biteClip != null) _oneShot.PlayOneShot(biteClip, 1f);
            DamageTarget(leapDamage);
            _airVel = -_airVel * 0.25f;
            stepL = _airVel * dt;
            len = stepL.magnitude;
        }

        if (len > 1e-5f && SphereProbe(posW, radius, World(stepL / len, true), len + radius, out RaycastHit hit))
        {
            _pos = ToLocalPoint(hit.point);
            _n = ToLocalDir(hit.normal).normalized;   // land square; crawl smoothing takes over
            _heading = TangentOr(_airVel, _n, _heading);
            _attached = true;
            _airTime = 0f;
            _stuckRef = _pos;
            return;
        }

        _pos += stepL;
        // In flight the legs turn toward "down" slowly, so it lands feet-first-ish.
        if (gW.sqrMagnitude > 1e-8f)
            _n = Vector3.Slerp(_n, -ToLocalDir(gW).normalized, 1f - Mathf.Exp(-2f * dt)).normalized;

        // Lost in open space (knocked out of a mouth?) — give up quietly.
        if (_airTime > 20f && !_dead) Destroy(gameObject);
    }

    // ── Surface crawling (pose local, physics queries world) ────────────────

    void Crawl(Vector3 dir, float dist, float dt)
    {
        float s = _scale;
        dir = TangentOr(dir, _n, _heading);
        _heading = dir;
        Vector3 pW = World(_pos), nW = World(_n, true), dW = World(dir, true);

        // 1. A real wall in the way (not a bump) → step onto it, heading up it.
        if (Probe(pW + nW * 0.3f * s, dW, dist + 0.35f * s, out RaycastHit wall)
            && Vector3.Angle(wall.normal, nW) > 50f)
        {
            if (!AllowedDepth(wall.point)) { TurnBack(); return; }
            Vector3 wallL = ToLocalDir(wall.normal).normalized;
            if (Time.time - _leftTime < 0.6f && Vector3.Angle(wallL, _leftNormal) < 35f)
            {
                // That's the surface it just climbed off: run along the corner instead of flipping back.
                Vector3 slide = Vector3.ProjectOnPlane(dW, wall.normal);
                if (slide.sqrMagnitude < 1e-4f) return;                 // dead into the corner: wait a beat
                dW = slide.normalized;
                dir = TangentOr(ToLocalDir(dW), _n, dir);
                _heading = dir;
            }
            else
            {
                _leftNormal = _n; _leftTime = Time.time;
                SetSurface(wall.point, wall.normal, TangentOr(_n, wallL, dir), dt, 3f);
                return;
            }
        }

        Vector3 cW = pW + dW * dist;

        // 2. Follow the surface under the next footfall (normal averaged over three feet).
        if (Probe(cW + nW * 0.4f * s, -nW, 1.0f * s, out RaycastHit floor))
        {
            if (!AllowedDepth(floor.point)) { TurnBack(); return; }
            SetSurface(floor.point, AveragedNormal(cW, nW, dW, floor.normal), dir, dt, 1f);
            return;
        }

        // 3. Over a lip → wrap round onto the face below it.
        if (Probe(cW - nW * 0.3f * s, -dW, dist + 0.6f * s, out RaycastHit lip))
        {
            if (!AllowedDepth(lip.point)) { TurnBack(); return; }
            _leftNormal = _n; _leftTime = Time.time;
            SetSurface(lip.point, lip.normal, TangentOr(-_n, ToLocalDir(lip.normal), dir), dt, 3f);
            return;
        }

        // 4. Nothing to hold — let go.
        Detach(dir * (dist / Mathf.Max(dt, 1e-4f)) * 0.5f);
    }

    Vector3 AveragedNormal(Vector3 cW, Vector3 nW, Vector3 dW, Vector3 centreNormal)
    {
        float s = _scale;
        Vector3 sideW = Vector3.Cross(nW, dW).normalized * 0.3f * s;
        Vector3 sum = centreNormal * 2f;
        if (Probe(cW + sideW + nW * 0.4f * s, -nW, 1.0f * s, out RaycastHit a)) sum += a.normal;
        if (Probe(cW - sideW + nW * 0.4f * s, -nW, 1.0f * s, out RaycastHit b)) sum += b.normal;
        return sum.sqrMagnitude > 1e-6f ? sum.normalized : centreNormal;
    }

    /// Put the spider on a surface point, easing its normal toward the new one.
    /// `rateMult` > 1 for corner transitions so it doesn't lag half-way round.
    void SetSurface(Vector3 pointW, Vector3 normalW, Vector3 headingLocal, float dt, float rateMult)
    {
        Vector3 nL = ToLocalDir(normalW).normalized;
        _n = Vector3.Slerp(_n, nL, 1f - Mathf.Exp(-surfaceAlignRate * rateMult * dt)).normalized;
        _pos = ToLocalPoint(pointW);
        _heading = TangentOr(headingLocal, _n, _heading);
        _attached = true;
    }

    void Face(Vector3 toLocal, float dt, float rate)
    {
        Vector3 want = TangentOr(toLocal, _n, _heading);
        _heading = Vector3.Slerp(_heading, want, 1f - Mathf.Exp(-rate * dt)).normalized;
    }

    void TurnBack()
    {
        // Never into the top level: turn toward the deep instead.
        _heading = TangentOr(-LocalUp(_pos), _n, -_heading);
        _detourUntil = Time.time + 1.2f;
    }

    bool AllowedDepth(Vector3 pointW)
    {
        if (!_territory) return true;
        float d = Depth(pointW);
        return d >= _minDepth || d >= Depth(World(_pos));     // a shallow spider may always go deeper
    }

    void Detach(Vector3 relVelLocal)
    {
        _attached = false;
        _airVel = relVelLocal;
        _airTime = 0f;
    }

    // ── Space helpers ───────────────────────────────────────────────────────

    Vector3 World(Vector3 local, bool direction = false) =>
        direction ? _moon.transform.TransformDirection(local) : _moon.transform.TransformPoint(local);
    Vector3 ToLocalPoint(Vector3 w) => _moon.transform.InverseTransformPoint(w);
    Vector3 ToLocalDir(Vector3 w) => _moon.transform.InverseTransformDirection(w);

    /// "Up" at a moon-local point = away from the moon's centre.
    Vector3 LocalUp(Vector3 local)
    {
        Vector3 u = local - ToLocalPoint(_moon.Position);
        return u.sqrMagnitude > 1e-6f ? u.normalized : _n;
    }

    float Depth(Vector3 worldPoint) => _moon.radius - (worldPoint - _moon.Position).magnitude;

    static Vector3 TangentOr(Vector3 v, Vector3 n, Vector3 fallback)
    {
        Vector3 t = Vector3.ProjectOnPlane(v, n);
        if (t.sqrMagnitude > 1e-6f) return t.normalized;
        t = Vector3.ProjectOnPlane(fallback, n);
        return t.sqrMagnitude > 1e-6f ? t.normalized : RandomTangent(n);
    }

    static Vector3 RandomTangent(Vector3 n)
    {
        Vector3 a = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        return Quaternion.AngleAxis(Random.Range(0f, 360f), n) * a;
    }

    static float Sq(float x) => x * x;

    // ── Physics queries (world space) ───────────────────────────────────────

    bool IsSurface(Collider c)
    {
        if (c == null || c.isTrigger) return false;
        for (int i = 0; i < _ownColliders.Length; i++) if (_ownColliders[i] == c) return false;
        if (c.GetComponentInParent<CaveSpider>() != null) return false;
        if (c.GetComponentInParent<PlayerController>() != null) return false;
        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;   // loose props
        return true;
    }

    bool Probe(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, s_hits, dist, SurfaceMask, QueryTriggerInteraction.Ignore);
        return Closest(n, out best);
    }

    bool SphereProbe(Vector3 origin, float radius, Vector3 dir, float dist, out RaycastHit best)
    {
        int n = Physics.SphereCastNonAlloc(origin, radius, dir, s_hits, dist, SurfaceMask, QueryTriggerInteraction.Ignore);
        if (!Closest(n, out best)) return false;
        // A cast that starts touching something reports point = zero; use a ray instead.
        if (best.distance <= 0f && best.point == Vector3.zero)
            return Probe(origin, dir, dist + radius, out best);
        return true;
    }

    bool Closest(int n, out RaycastHit best)
    {
        best = default;
        float bestD = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (!IsSurface(s_hits[i].collider)) continue;
            if (s_hits[i].distance < bestD) { bestD = s_hits[i].distance; best = s_hits[i]; found = true; }
        }
        return found;
    }

    bool ClearLine(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 1e-3f) return true;
        return !Probe(from, d / len, len, out _);
    }

    bool ClearPath(Vector3 from, Vector3 to, float radius)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 1e-3f) return true;
        int n = Physics.SphereCastNonAlloc(from, radius, d / len, s_hits, Mathf.Max(0f, len - 0.5f), SurfaceMask, QueryTriggerInteraction.Ignore);
        return !Closest(n, out _);
    }

    bool IsFloating(Transform t)
    {
        if (t == null) return false;
        if (ZeroGZone.All.Count > 0 && ZeroGZone.Contains(t.position)) return true;
        Vector3 g = Universe.GravityAcceleration(t.position, _moon);
        Vector3 down = g.sqrMagnitude > 1e-8f ? g.normalized : (_moon.Position - t.position).normalized;
        int n = Physics.RaycastNonAlloc(t.position, down, s_hits, 2.2f, SurfaceMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (IsSurface(s_hits[i].collider)) return false;
        return true;
    }

    bool AnyPlayerWithin(float r)
    {
        var all = PlayerRoster.All();
        float r2 = r * r;
        Vector3 me = World(_pos);
        for (int i = 0; i < all.Count; i++)
            if (all[i].Transform != null && (all[i].Transform.position - me).sqrMagnitude < r2) return true;
        return false;
    }

    /// Spiders are solid to bullets and blades but NOT to the player: a kinematic
    /// body crawling into you shoves you around (and in low gravity, sends you
    /// flying). Contact with the local player's colliders is switched off pair by
    /// pair; re-applied every second so a respawned or rebuilt player is covered.
    void IgnorePlayerContact()
    {
        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].IsLocal || all[i].Transform == null) continue;
            // The player's own object, never .root: a player parented under a planet
            // would otherwise sweep in every collider on that planet.
            var pc = all[i].Transform.GetComponentInParent<PlayerController>();
            (pc != null ? pc.transform : all[i].Transform).GetComponentsInChildren(true, s_playerCols);
            for (int a = 0; a < _ownColliders.Length; a++)
            {
                if (_ownColliders[a] == null) continue;
                for (int b = 0; b < s_playerCols.Count; b++)
                    if (s_playerCols[b] != null && s_playerCols[b].GetComponentInParent<CaveSpider>() == null)
                        Physics.IgnoreCollision(_ownColliders[a], s_playerCols[b], true);
            }
        }
    }
    static readonly List<Collider> s_playerCols = new List<Collider>();

    // ── Damage / death ──────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (_dead || amount <= 0f) return;
        _health -= amount;
        // The short splash only — the pistol's long wound fountain is skipped for spiders.
        BloodFX.Instance?.SpawnDamageSplash(transform.position + transform.up * 0.25f * _scale, transform, _scale);
        if (_health <= 0f) { Die(); return; }
        if (_mode != Mode.Chasing)
        {
            var shooter = PlayerRoster.Nearest(transform.position, out _);
            if (shooter != null) { Notice(shooter); EnterChase(); }
        }
    }

    /// Axe / blade knockback: in the moon's low gravity a good hit sends it flying.
    public void ApplyKnockback(Vector3 worldDir, float distance, float duration)
    {
        if (_dead || _moon == null || duration <= 0f || distance <= 0f || worldDir.sqrMagnitude < 1e-4f) return;
        _leapWindupT = -1f;
        Vector3 vW = worldDir.normalized * (distance / duration) * 0.6f;
        _pos += _n * 0.2f * _scale;
        Detach(ToLocalDir(vW) + _n * 1.2f);
        _leapHitLanded = true;   // a spider batted away doesn't bite on the way past
    }

    void Die()
    {
        _dead = true;
        _deathTimer = 0f;
        _deathScale = transform.localScale;
        if (_loop != null) _loop.Stop();
        if (deathClip != null) _oneShot.PlayOneShot(deathClip, 1f);
        if (_anim != null)
        {
            _anim.speed = 1f;
            _anim.SetBool(HashWalk, false);
            _anim.SetBool(HashAttack, false);
            int pick = Random.Range(0, 3);
            _anim.SetBool(pick == 0 ? HashDead1 : pick == 1 ? HashDead2 : HashDead3, true);
        }
        for (int i = 0; i < _ownColliders.Length; i++) if (_ownColliders[i] != null) _ownColliders[i].enabled = false;
        _spawner?.OnSpiderDied(this);
    }

    const float CorpseHold = 8f, CorpseShrink = 1.2f;

    void TickDeath(float dt)
    {
        _deathTimer += dt;
        if (!_attached) TickAirborne(dt);   // killed mid-leap: still falls and lands
        if (_deathTimer < CorpseHold) return;
        float u = (_deathTimer - CorpseHold) / CorpseShrink;
        if (u >= 1f) { Destroy(gameObject); return; }
        transform.localScale = _deathScale * (1f - u);
    }

    void OnDestroy() => _spawner?.OnSpiderGone(this);
}
