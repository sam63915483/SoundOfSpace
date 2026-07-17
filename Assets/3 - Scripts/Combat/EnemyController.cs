using UnityEngine;

public enum EnemyKind { Regular, Elite }

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class EnemyController : MonoBehaviour, IDamageable
{
    [Header("Combat")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float damagePerBobberHit = 25f;
    [SerializeField] float contactDamage = 10f;
    [SerializeField] float contactDamageInterval = 0.5f;
    // 3 hits × 34 = 102 ≥ AlienNPCDamageable's default maxHealth of 100.
    [SerializeField] float npcContactDamage = 34f;
    // Distance under which a nearby AlienNPCDamageable is considered "stuck
    // on" this enemy. Tuned so enemy capsule (~1m radius) + NPC live capsule
    // (~0.9m radius) just-touching geometry sits inside the bite envelope
    // with ~0.6m slack. Scaled by transform.localScale.x at runtime.
    [SerializeField] float npcBiteRadius = 2.5f;

    [Header("Save Identity")]
    // Stamped on each prefab so SaveCollector knows which prefab to resolve on
    // load. Toy10 = Regular (default); Toy3 elite prefab must be set to Elite.
    [SerializeField] EnemyKind kind = EnemyKind.Regular;

    [Header("Movement")]
    [SerializeField] float moveSpeed = 3f;
    [SerializeField] float turnSpeedDegPerSec = 180f;
    [SerializeField] float stoppingDistance = 0.8f;
    [SerializeField] float fallSpeed = 15f;
    [SerializeField] float groundedOffset = 1.0f;
    [SerializeField] float maxClimbHeight = 2.5f;
    [SerializeField] float separationRadius = 2.5f;
    [SerializeField] float separationWeight = 2f;
    // Trees aren't on a special layer and the ground probe accepts the top of
    // a short trunk as walkable terrain, so without an explicit steering force
    // enemies climb / get stuck on them. This adds a separation-style push
    // away from any live SpawnedTree within treeAvoidRadius — same shape as
    // the per-enemy separation loop, just against a different list.
    [SerializeField] float treeAvoidRadius = 5.25f;
    [SerializeField] float treeAvoidWeight = 4f;

    [Header("Audio")]
    // Drag any MP3/WAV here. All clips are optional — empty slots are skipped.
    //   runGruntClip   — looping 3D grunt while the enemy is walking toward you.
    //   attackClip     — one-shot per contact-damage tick.
    //   deathClip      — one-shot on death, fires before the ragdoll spawns.
    //   spawnLoopClip  — one-shot on spawn, then re-fired at a random interval
    //                    in [spawnLoopRepeatMin, spawnLoopRepeatMax] for the
    //                    enemy's lifetime. Intended for the elite Toy3's
    //                    presence/breathing motif but available on any enemy.
    //   chargeStartClip — one-shot at the moment a Charge phase begins. Only
    //                     ever fires when useChargeBehavior is on.
    [SerializeField] AudioClip runGruntClip;
    [SerializeField] AudioClip attackClip;
    [SerializeField] AudioClip deathClip;
    [SerializeField] AudioClip spawnLoopClip;
    [SerializeField] AudioClip chargeStartClip;
    [SerializeField, Range(0f, 1f)] float runGruntVolume    = 0.7f;
    [SerializeField, Range(0f, 1f)] float attackVolume      = 0.9f;
    [SerializeField, Range(0f, 1f)] float deathVolume       = 1f;
    [SerializeField, Range(0f, 1f)] float spawnLoopVolume   = 0.85f;
    [SerializeField, Range(0f, 1f)] float chargeStartVolume = 0.9f;
    [SerializeField] float spawnLoopRepeatMin = 10f;
    [SerializeField] float spawnLoopRepeatMax = 20f;
    [SerializeField] float audioMinDistance = 2f;
    [SerializeField] float audioMaxDistance = 60f;

    [Header("Tree-Top Spit Attack (player on tree)")]
    // When the player climbs on top of a tree they're out of contact-damage
    // range, so the enemy switches to a ranged spit: holds at standoff
    // distance, plays a head wind-back / flick animation in LateUpdate, fires
    // a SpitProjectile that homes onto the player Transform, and applies a
    // small fixed damage on impact. spitClip plays at the moment the projectile
    // is launched (mid-flick), routed through the shared one-shot AudioSource.
    [SerializeField] AudioClip spitClip;
    [SerializeField, Range(0f, 1f)] float spitVolume = 0.85f;
    // Hold the spit source at full volume out to the far edge of the
    // standoff band so it lands properly loud when the enemy is backing
    // up to spit at the player.
    [SerializeField] float spitAudioMinDistance = 25f;
    [SerializeField] float spitAudioMaxDistance = 80f;
    [SerializeField] float spitDamage          = 5f;
    // Random gap between spits — re-rolled in [spitIntervalMin, spitIntervalMax]
    // each time a cycle completes so the cadence feels organic, not metronomic.
    [SerializeField] float spitIntervalMin     = 5f;
    [SerializeField] float spitIntervalMax     = 10f;
    [SerializeField] float spitWindUpSeconds   = 0.25f;  // head pitches back
    [SerializeField] float spitFlickSeconds    = 0.08f;  // head snaps forward (projectile fires at end)
    [SerializeField] float spitSettleSeconds   = 0.15f;  // head returns to rest
    [SerializeField] float spitFlightSeconds   = 0.5f;
    [SerializeField] float spitArcHeight       = 2f;
    // Standoff is the distance the enemy holds while spitting. Closer than min
    // → reverse direction (back up). Inside the band → stop and pause. Farther
    // than max → walk closer until they enter the band.
    [SerializeField] float spitStandoffMin     = 15f;
    [SerializeField] float spitStandoffMax     = 22f;
    [SerializeField] float spitWindUpPitchDeg  = 30f;    // chin-up wind back
    [SerializeField] float spitFlickPitchDeg   = 20f;    // chin-down release

    [Header("Charge Behavior (optional)")]
    // When enabled, locomotion runs a Walk → Charge → Cooldown loop instead of
    // using the flat moveSpeed: walks for a random gap, then sprints (Run) at
    // chargeSpeed for chargeDuration seconds, then walks for chargeCooldown
    // seconds before the next charge can start. The Animator gets Speed = 0
    // (Idle), 0.5 (Walk), or 1.0 (Run) so a 3-state controller picks the right
    // clip; Toy10 leaves this off and keeps the original binary 0/1 mapping.
    [SerializeField] bool useChargeBehavior = false;
    [SerializeField] float walkSpeed = 2f;
    [SerializeField] float chargeSpeed = 6f;
    [SerializeField] float chargeDuration = 10f;
    [SerializeField] float chargeCooldown = 5f;
    [SerializeField] float walkBeforeChargeMin = 3f;
    [SerializeField] float walkBeforeChargeMax = 8f;

    [Header("Spawn (read by EnemySpawner from this prefab)")]
    [SerializeField] float minSpawnDistance = 15f;
    [SerializeField] float maxSpawnDistance = 40f;
    [SerializeField] int maxConcurrent = 5;
    [SerializeField] float spawnInterval = 10f;
    [SerializeField] float viewConeAngleDeg = 70f;
    [SerializeField] float darkSideDotThreshold = -0.05f;
    [SerializeField] float spawnSurfaceOffset = 3f;
    [SerializeField] int spawnAttemptsPerTick = 12;
    [SerializeField] float despawnDistance = 80f;

    [Header("Hitbox")]
    [Tooltip("Global multiplier on the per-bone hit / ragdoll capsule radii. 1 = the measured-from-mesh defaults. Bump up/down to fatten/thin the whole hitbox; the Scene-view gizmo (below) updates live as you change it.")]
    [SerializeField] float hitboxRadiusScale = 1f;
    [Tooltip("Draw the hit/ragdoll capsules as red wireframes in the Scene view (editor only — never shows in the game/build). Open the enemy prefab or select an enemy in the scene to see and tune the hitbox.")]
    [SerializeField] bool drawHitboxGizmos = true;

    public float MinSpawnDistance => minSpawnDistance;
    public float MaxSpawnDistance => maxSpawnDistance;
    public int MaxConcurrent => maxConcurrent;
    public float SpawnInterval => spawnInterval;
    public float ViewConeAngleDeg => viewConeAngleDeg;
    public float DarkSideDotThreshold => darkSideDotThreshold;
    public float SpawnSurfaceOffset => spawnSurfaceOffset;
    public int SpawnAttemptsPerTick => spawnAttemptsPerTick;

    public EnemyKind Kind => kind;
    public float CurrentHealth => currentHealth;
    public bool IsDying => _dying;
    public float HitboxRadiusScale => hitboxRadiusScale;   // read by KillShotCam's collider viz

    Rigidbody rb;
    Collider ownCollider;
    EnemyHealthBar healthBar;
    Animator _anim;
    static readonly int _animSpeedHash = Animator.StringToHash("Speed");
    Transform player;
    CelestialBody parentPlanet;
    float currentHealth;
    float nextDamageTime;
    Vector3 _knockbackVel;
    float _knockbackTimer;
    bool _dying;
    bool _walkingThisStep;
    float _currentMoveSpeed;
    bool _isCharging;
    float _phaseTimer;
    AudioSource _runSource;     // looping grunt while walking; null if no clip assigned
    AudioSource _oneShotSource; // shared sink for attack / death / spawn-loop / charge-start one-shots
    // Dedicated spit source. The shared one-shot rolloff (minDistance=2,
    // maxDistance=60, linear) only reaches ~65-78% volume at the 15-22m spit
    // standoff range, which made the spit feel like a whisper from afar. This
    // source uses minDistance=spitAudioMinDistance so it stays at 100% inside
    // the standoff band.
    AudioSource _spitSource;
    Transform   _neckBone;      // *_Neck bone in the rig; cached lazily in TryCacheNeckBone
    bool   _spitting;
    bool   _spitFired;          // projectile already spawned for the current spit cycle
    float  _spitPhaseTime;
    float  _nextSpitTime;
    // groundedOffset is the distance from the rb position down to the visual's
    // feet — but the visual is parented at localPosition.y = -groundedOffset, so
    // when the root is scaled (e.g. enlarged Toy3) the visual feet drop below
    // rb.position by groundedOffset * scale.y, while the field-value
    // groundedOffset stays in absolute units. Cache a scale-aware copy in Awake
    // and use it everywhere ground-snap math runs, so legs sit on the ground
    // regardless of prefab scale.
    float _scaledGroundedOffset;
    float _deathTimer;
    Vector3 _deathStartScale;
    bool _frozenForShrink;

    // ── Stealth revamp: AI state ──
    public enum AIState { Docile, Investigating, Chasing, Searching }
    AIState _state = AIState.Docile;
    public AIState State => _state;
    public EnemyVision Vision => _vision;
    public float SearchProgress01 => searchLookSeconds > 0.01f ? Mathf.Clamp01(_searchTimer / searchLookSeconds) : 0f;
    EnemyVision _vision;
    CelestialBody _sun;
    Vector3 _leashLocalToPlanet;
    bool _hasLeash;
    bool _wanderWalking;
    Vector3 _wanderTargetLocal;
    float _wanderTimer;
    Quaternion _lookTargetRot = Quaternion.identity;
    float _searchTimer;
    float _searchScanTimer;
    Vector3 _investigateLookDir;
    float _nextSniffTime;
    float _loseSightTimer;
    float _burnTimer;
    bool _forceChase;
    Vector3 _stuckRefLocal;      // planet-local position at last watchdog sample
    float _stuckDeadline;        // next watchdog check time
    const float StuckSeconds = 2f;
    float _nextMobRecruit;       // throttle for the mob-mentality recruit sweep
    const float RagdollDuration     = 30f;   // total corpse lifetime before shrink
    const float DeathShrinkDuration = 1.5f;  // shrink-to-zero after ragdoll (long enough to actually read on screen — 0.4s was perceived as instant disappear)
    // Free physics tumble is capped: once the corpse settles (relative to the
    // orbiting planet) or MaxFreeTumbleSeconds elapses, it's frozen + locked to
    // the planet for the REST of RagdollDuration, so it rides the orbit rigidly
    // instead of free-drifting out of frame / grinding the terrain for 30s.
    const float MinFreeTumbleSeconds = 10f;   // always ragdoll at least this long before any freeze
    const float MaxFreeTumbleSeconds = 25f;   // safety cap: freeze by here even if never fully settles (leaves time to shrink at RagdollDuration)
    const float SettleSpeedThreshold = 0.4f;  // m/s relative to the planet — must be nearly stopped, not "moving a teeny bit"

    static readonly RaycastHit[] s_hitBuffer = new RaycastHit[8];
    static readonly System.Collections.Generic.List<EnemyController> s_active =
        new System.Collections.Generic.List<EnemyController>();

    public static event System.Action OnAnyEnemyDeath;

    void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    void OnDisable()
    {
        s_active.Remove(this);
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ownCollider = GetComponent<Collider>();
        _vision = GetComponent<EnemyVision>();

        // Kinematic + parented to the planet means the enemy rides the planet's
        // transform — no need for EndlessManager registration; the floating-origin
        // shift moves the planet, and we follow through the hierarchy.
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        healthBar = GetComponentInChildren<EnemyHealthBar>(true);
        if (healthBar != null) healthBar.gameObject.SetActive(false);

        // Optional — animated enemy prefabs (e.g. the Cursed Toy variants) embed
        // a child Animator. The capsule placeholder won't have one; SetFloat
        // calls are guarded by null on _anim so both prefab shapes work.
        _anim = GetComponentInChildren<Animator>(true);

        currentHealth = maxHealth;

        // Default to walking; charge cycle (if enabled) flips this in FixedUpdate.
        _currentMoveSpeed = useChargeBehavior ? walkSpeed : moveSpeed;
        _phaseTimer = useChargeBehavior ? Random.Range(walkBeforeChargeMin, walkBeforeChargeMax) : 0f;

        _scaledGroundedOffset = groundedOffset * transform.localScale.y;

        // Two AudioSources — one always-looping for the grunt (Pause/Play
        // toggled by the locomotion state in FixedUpdate), one for PlayOneShot
        // attack hits. Skipped if the corresponding clip slot is empty so an
        // unconfigured prefab adds nothing to the scene.
        if (runGruntClip != null)
        {
            _runSource = gameObject.AddComponent<AudioSource>();
            _runSource.clip          = runGruntClip;
            _runSource.loop          = true;
            _runSource.playOnAwake   = false;
            _runSource.spatialBlend  = 1f;
            _runSource.volume        = runGruntVolume;
            _runSource.minDistance   = audioMinDistance;
            _runSource.maxDistance   = audioMaxDistance;
            _runSource.rolloffMode   = AudioRolloffMode.Linear;
        }
        // One shared one-shot AudioSource backs all four PlayOneShot triggers
        // (attack, death, spawn-loop, charge-start). Created if any of them has
        // a clip assigned. Base volume = 1 so per-clip volume sliders work
        // through PlayOneShot's volumeScale parameter.
        bool needsOneShot = attackClip != null || deathClip != null
                         || spawnLoopClip != null || chargeStartClip != null
                         || spitClip != null || sniffClip != null;
        if (needsOneShot)
        {
            _oneShotSource = gameObject.AddComponent<AudioSource>();
            _oneShotSource.playOnAwake  = false;
            _oneShotSource.spatialBlend = 1f;
            _oneShotSource.volume       = 1f;
            _oneShotSource.minDistance  = audioMinDistance;
            _oneShotSource.maxDistance  = audioMaxDistance;
            _oneShotSource.rolloffMode  = AudioRolloffMode.Linear;
        }

        // Separate spit source. Built only when this prefab actually has a
        // spit clip assigned — Toy10 / unrigged variants without spit audio
        // skip the cost.
        if (spitClip != null)
        {
            _spitSource = gameObject.AddComponent<AudioSource>();
            _spitSource.playOnAwake  = false;
            _spitSource.spatialBlend = 1f;
            _spitSource.volume       = 1f;
            _spitSource.minDistance  = spitAudioMinDistance;
            _spitSource.maxDistance  = spitAudioMaxDistance;
            _spitSource.rolloffMode  = AudioRolloffMode.Linear;
        }
    }

    // Shared across ALL enemies. A spawning wave used to have every enemy's
    // Start() call FindObjectOfType<PlayerController>() independently, batching
    // into a ~19 ms delayed-Start hitch while being chased. One cached lookup
    // serves the whole fleet; it re-finds automatically after a scene reload
    // (the old reference goes Unity-null).
    static PlayerController _sharedPlayer;
    static Transform ResolvePlayerTransform()
    {
        if (_sharedPlayer == null) _sharedPlayer = FindObjectOfType<PlayerController>(true);
        return _sharedPlayer != null ? _sharedPlayer.transform : null;
    }

    void Start()
    {
        player = ResolvePlayerTransform();

        parentPlanet = GetComponentInParent<CelestialBody>();
        if (parentPlanet == null) parentPlanet = GetNearestPlanet();
        if (parentPlanet != null)
        {
            // Leash center for docile wander = spawn spot, stored planet-relative so it
            // survives orbital motion / floating-origin shifts.
            _leashLocalToPlanet = parentPlanet.transform.InverseTransformPoint(rb.position);
            _hasLeash = true;
        }
        _sun = GetSun();

        // Bootstrap the per-player tree-contact timer the first time any enemy
        // wakes up. The tracker is global and survives scene transitions; this
        // call is a no-op after the first invocation.
        PlayerTreeContactTracker.EnsureExists();

        if (spawnLoopClip != null && _oneShotSource != null)
            StartCoroutine(SpawnLoopRoutine());

        // Animation-following hit colliders on the rig bones so shots register on
        // the actual limbs (arms / head / legs), not just the static torso
        // capsule. They attach to the kinematic root as a moving compound
        // collider and are torn down in BeginDeath before the ragdoll builds its
        // own. No-op on the unrigged capsule placeholder.
        Transform hitRig = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("Visual_")) { hitRig = child; break; }
        }
        if (hitRig != null) _hitColliders = EnemyRagdollBuilder.BuildHitColliders(hitRig, hitboxRadiusScale);
    }

    System.Collections.IEnumerator SpawnLoopRoutine()
    {
        // Plays the spawn motif immediately, then re-fires every random
        // [spawnLoopRepeatMin, spawnLoopRepeatMax] seconds until BeginDeath
        // sets _dying. The clip is dispatched via PlayOneShot so it never
        // collides with attack/charge/death one-shots on the same source.
        // Recurring scream/presence motif — only while actively chasing (docile
        // enemies are silent; the lock-on scream is played once by EnterChase).
        while (!_dying)
        {
            float wait = Random.Range(spawnLoopRepeatMin, spawnLoopRepeatMax);
            yield return new WaitForSeconds(wait);
            if (_dying) yield break;
            if (_state == AIState.Chasing)
                _oneShotSource.PlayOneShot(spawnLoopClip, spawnLoopVolume);
        }
    }

    void FixedUpdate()
    {
        if (_dying) { TickDeath(); return; }

        if (player == null)
        {
            player = ResolvePlayerTransform();
            if (player == null) return;
        }

        if (parentPlanet == null)
        {
            parentPlanet = GetComponentInParent<CelestialBody>();
            if (parentPlanet == null) parentPlanet = GetNearestPlanet();
            if (parentPlanet == null) return;
        }

        // Sunlight burns them on the lit side — bait a chase into the terminator to kill them.
        TickSunburn();
        if (_dying) return;

        // Un-wedge safety net (reads LAST tick's walking flag — cheap, order-independent).
        TickStuckWatchdog();

        // Despawn if we've drifted out of the player's bubble.
        if ((player.position - rb.position).magnitude > despawnDistance)
        {
            Destroy(gameObject);
            return;
        }

        // Proximity-bite NPCs we're stuck on. Runs EVERY tick regardless of AI state —
        // this is the substitute for OnCollisionStay against AlienNPCDamageable's static
        // colliders (kinematic-vs-static fires no collision events), and enemies must
        // keep threatening NPCs even while Docile/Searching, as they always did.
        TryBiteNearbyNPC();

        // ── Stealth state machine ───────────────────────────────────────────
        // Docile / Investigating / Searching run their own cheap locomotion and
        // return inside UpdateAIState; only Chasing falls through to the full
        // aggression path below (spit, charge, pursuit).
        UpdateAIState();
        if (_state != AIState.Chasing) return;

        // Advance the spit state machine (chase-only; EnterSearch/EnterDocile cancel a
        // mid-flight cycle so it can't freeze half-wound outside Chasing).
        TickSpitState();

        // Reset every tick; the grounded-and-moving branch below sets it true
        // if the enemy is actually walking toward the player this step.
        _walkingThisStep = false;

        // Charge state machine — only runs when the enemy is configured for it
        // (Toy3 elite). Otherwise _currentMoveSpeed stays at moveSpeed from Awake.
        if (useChargeBehavior)
        {
            _phaseTimer -= Time.fixedDeltaTime;
            if (_phaseTimer <= 0f)
            {
                if (_isCharging)
                {
                    _isCharging = false;
                    _phaseTimer = chargeCooldown + Random.Range(walkBeforeChargeMin, walkBeforeChargeMax);
                }
                else
                {
                    _isCharging = true;
                    _phaseTimer = chargeDuration;
                    if (_oneShotSource != null && chargeStartClip != null)
                        _oneShotSource.PlayOneShot(chargeStartClip, chargeStartVolume);
                }
            }
            _currentMoveSpeed = _isCharging ? chargeSpeed : walkSpeed;
        }

        Vector3 fromCenter = rb.position - parentPlanet.Position;
        if (fromCenter.sqrMagnitude < 0.0001f) return;
        Vector3 up = fromCenter.normalized;
        int groundMask = ~((1 << 9) | (1 << 11) | (1 << 12)); // exclude Ship, Sun, FishPreview

        if (_knockbackTimer > 0f)
        {
            Vector3 step = Vector3.ProjectOnPlane(_knockbackVel, up) * Time.fixedDeltaTime;
            Vector3 candidate = rb.position + step;
            Vector3 candUp = (candidate - parentPlanet.Position).normalized;
            if (TryGroundProbe(candidate + candUp * maxClimbHeight, -candUp,
                               maxClimbHeight + _scaledGroundedOffset + 1f, groundMask, out RaycastHit kbHit))
            {
                candidate = kbHit.point + candUp * _scaledGroundedOffset;
            }
            candidate += parentPlanet.velocity * Time.fixedDeltaTime;
            rb.MovePosition(candidate);
            _knockbackTimer -= Time.fixedDeltaTime;

            Vector3 kbFromCenter = candidate - parentPlanet.Position;
            if (kbFromCenter.sqrMagnitude > 0.0001f)
            {
                Vector3 kbUp = kbFromCenter.normalized;
                Vector3 toPlayerKB = player.position - candidate;
                Vector3 forwardKB = Vector3.ProjectOnPlane(toPlayerKB, kbUp);
                if (forwardKB.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(forwardKB.normalized, kbUp);
                    rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot,
                                                             turnSpeedDegPerSec * Time.fixedDeltaTime));
                }
            }
            return;
        }

        Vector3 newPos = rb.position;

        // Grounded check — short probe just below the capsule. We're only "grounded"
        // when ground is within ~groundedOffset below us; otherwise we keep falling
        // toward the planet core. This is what guarantees the enemy never floats up.
        float groundedRange = _scaledGroundedOffset + 0.5f;
        Vector3 probeStart = rb.position + up * 0.1f;
        bool grounded = TryGroundProbe(probeStart, -up, groundedRange, groundMask, out RaycastHit groundHit);

        if (!grounded)
        {
            // Always going down until we touch the ground.
            newPos = rb.position - up * fallSpeed * Time.fixedDeltaTime;
        }
        else
        {
            // Sit on the ground at the proper offset.
            newPos = groundHit.point + up * _scaledGroundedOffset;

            // Walk toward the player along the surface tangent.
            Vector3 toPlayer = player.position - newPos;
            Vector3 horizDir = Vector3.ProjectOnPlane(toPlayer, up);
            float horizDist = horizDir.magnitude;

            // Spit-mode locomotion override: when the player is up on a tree,
            // EVERY enemy halts in place and (eventually) spits from where it
            // stands — no approaching the tree base. Only too-close enemies
            // back up; everything else pins horizDist to stoppingDistance so
            // the walk branch below is gated and they freeze on the spot.
            // Episode state lives on PlayerTreeContactTracker — starts on
            // first tree contact, only clears when the player touches the
            // ground, so jumping in place / between trees doesn't reset it.
            // Spit cycles only ARM after SpitDelaySeconds (10s); during the
            // pre-arm window everyone still freezes / backs up, but no one
            // fires.
            if (PlayerTreeContactTracker.IsActive)
            {
                if (horizDist < spitStandoffMin)
                    horizDir = -horizDir;              // too close — back up
                else
                    horizDist = stoppingDistance;      // anywhere ≥ min — halt

                if (!_spitting
                    && Time.time >= _nextSpitTime
                    && PlayerTreeContactTracker.SpitArmed)
                {
                    _spitting      = true;
                    _spitFired     = false;
                    _spitPhaseTime = 0f;
                    _nextSpitTime  = Time.time + Random.Range(spitIntervalMin, spitIntervalMax);
                }
            }

            if (horizDist > stoppingDistance)
            {
                _walkingThisStep = true;
                // Steer away from neighbouring enemies so they don't pile on the
                // same square. Each contributor falls off linearly inside the radius.
                Vector3 separation = Vector3.zero;
                for (int i = 0; i < s_active.Count; i++)
                {
                    var other = s_active[i];
                    if (other == null || other == this) continue;
                    Vector3 diff = rb.position - other.rb.position;
                    Vector3 diffTangent = Vector3.ProjectOnPlane(diff, up);
                    float d = diffTangent.magnitude;
                    if (d > 0.001f && d < separationRadius)
                    {
                        float falloff = 1f - (d / separationRadius);
                        separation += diffTangent.normalized * (falloff * separationWeight);
                    }
                }

                // Steer around live trees. Cheap sqrMagnitude early-out so a
                // planet covered in hundreds of trees doesn't blow per-tick
                // cost — only the handful within ~treeAvoidRadius do the
                // tangent projection.
                var trees = SpawnedTree.AllTrees;
                float treeCullSqr = (treeAvoidRadius + 1f) * (treeAvoidRadius + 1f);
                for (int i = 0; i < trees.Count; i++)
                {
                    var tree = trees[i];
                    if (tree == null || tree.IsDead) continue;
                    Vector3 diff = rb.position - tree.transform.position;
                    if (diff.sqrMagnitude > treeCullSqr) continue;
                    Vector3 diffTangent = Vector3.ProjectOnPlane(diff, up);
                    float d = diffTangent.magnitude;
                    if (d > 0.001f && d < treeAvoidRadius)
                    {
                        float falloff = 1f - (d / treeAvoidRadius);
                        separation += diffTangent.normalized * (falloff * treeAvoidWeight);
                    }
                }

                Vector3 combined = horizDir.normalized + separation;
                Vector3 stepDir = combined.sqrMagnitude > 0.0001f
                    ? combined.normalized
                    : horizDir.normalized;

                float stepLen = Mathf.Min(_currentMoveSpeed * Time.fixedDeltaTime, horizDist - stoppingDistance);
                stepDir = DeflectAroundObstacles(stepDir, stepLen, up);   // slide along walls, don't grind
                Vector3 candidate = newPos + stepDir * stepLen;

                // Snap candidate to the *local* ground beneath/at-its-feet — probe
                // from a small height above the candidate (maxClimbHeight allows
                // stepping up onto obstacles up to that tall) and search down past
                // the resting offset. The probe starts above the capsule and would
                // hit our own collider first; TryGroundProbe filters that out.
                Vector3 candUp = (candidate - parentPlanet.Position).normalized;
                Vector3 candProbeStart = candidate + candUp * maxClimbHeight;
                float candProbeRange = maxClimbHeight + _scaledGroundedOffset + 1f;
                if (TryGroundProbe(candProbeStart, -candUp, candProbeRange, groundMask, out RaycastHit candHit))
                {
                    Vector3 snapped = candHit.point + candUp * _scaledGroundedOffset;
                    // Reject the step if it would require climbing higher than
                    // a normal stride. Belt-and-braces with the tree avoidance:
                    // anything too tall (short tree the steering didn't fully
                    // dodge, a rock, a low building) doesn't get climbed onto.
                    float climbDelta = Vector3.Dot(snapped - newPos, up);
                    if (climbDelta <= maxClimbHeight)
                        newPos = snapped;
                    // Else: blocked by something too tall — the slide above already bent
                    // the path; hold this tick and next tick's deflection finds a way around.
                }
                else
                {
                    // No ground within probe range below the candidate — a roof lip / ledge.
                    // Take the horizontal step anyway: the grounded check next tick starts
                    // the fall and they drop off naturally. The old "stay put" here was THE
                    // frozen-in-the-air bug — enemies parked forever on roof/crystal edges.
                    newPos = candidate;
                }
            }
        }

        // Inherit the planet's orbital motion. Without this the enemy is computing
        // a world target relative to the planet's *current* position, but the physics
        // step also moves the planet by velocity * dt — so the enemy ends up dt-behind
        // each tick and visibly drifts off the surface.
        newPos += parentPlanet.velocity * Time.fixedDeltaTime;

        rb.MovePosition(newPos);

        // Drive the animator. Toy10 has only Idle/Run (binary 0/1); Toy3 has
        // Idle/Walk/Run, so when charge mode is on we report 0.5 for walking
        // and 1.0 for charging. The 3-state controller's thresholds are 0.1
        // (Idle→Walk) and 0.6 (Walk→Run).
        if (_anim != null)
        {
            float animSpeed = !_walkingThisStep ? 0f
                            : useChargeBehavior ? (_isCharging ? 1f : 0.5f)
                            : 1f;
            _anim.SetFloat(_animSpeedHash, animSpeed);
        }

        // Run-grunt loop tracks the same locomotion flag as the animator.
        // Pause (not Stop) so the clip resumes from its current sample on the
        // next stride — feels more natural than restarting from frame 0.
        if (_runSource != null)
        {
            if (_walkingThisStep && !_runSource.isPlaying) _runSource.Play();
            else if (!_walkingThisStep && _runSource.isPlaying) _runSource.Pause();
        }

        // Visual rotation: face the player while staying upright relative to the planet.
        Vector3 finalFromCenter = newPos - parentPlanet.Position;
        if (finalFromCenter.sqrMagnitude > 0.0001f)
        {
            Vector3 finalUp = finalFromCenter.normalized;
            Vector3 toPlayerFinal = player.position - newPos;
            Vector3 visualForward = Vector3.ProjectOnPlane(toPlayerFinal, finalUp);
            if (visualForward.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(visualForward.normalized, finalUp);
                Quaternion nextRot = Quaternion.RotateTowards(rb.rotation, targetRot,
                                                              turnSpeedDegPerSec * Time.fixedDeltaTime);
                rb.MoveRotation(nextRot);
            }
        }
    }

    // ── Stealth AI ───────────────────────────────────────────────────────────
    void UpdateAIState()
    {
        bool alerted = _forceChase || (_vision != null && _vision.IsAlerted);
        bool canSee  = _vision != null && _vision.CanSeePlayerNow;
        float suspicion = _vision != null ? _vision.Suspicion01 : 0f;

        switch (_state)
        {
            case AIState.Docile:
                if (alerted) { EnterChase(); return; }
                if (canSee && suspicion > 0.01f) { EnterInvestigate(); return; }
                TickDocile();
                return;

            case AIState.Investigating:
                if (alerted) { EnterChase(); return; }
                if (suspicion <= 0.01f) { EnterDocile(false); return; }
                TickInvestigate();
                return;

            case AIState.Searching:
                // Re-spotting during the hunt is NOT instant: seeing the player just fills
                // suspicion at searchFillMult speed (EnemyVision) — chase resumes only when
                // the meter fills (or they're shot). TickSearch stares while it confirms.
                if (alerted) { EnterChase(); return; }
                TickSearch();
                return;

            case AIState.Chasing:
                if (canSee)
                {
                    _loseSightTimer = 0f;
                    _forceChase = false;
                    // Mob mentality (recruit): a chaser WITH visual pulls nearby
                    // non-chasing enemies into the chase — they want in on the fun.
                    if (Time.time >= _nextMobRecruit)
                    {
                        _nextMobRecruit = Time.time + 0.5f;
                        RecruitMob();
                    }
                }
                else if (MobHasVisual())
                {
                    // Mob mentality (shared eyes): I can't see you, but a chasing
                    // packmate within mobLinkRadius CAN — the group stays locked on.
                    _loseSightTimer = 0f;
                }
                else
                {
                    _loseSightTimer += Time.fixedDeltaTime;
                    if (_loseSightTimer >= loseSightGrace) { EnterSearch(); return; }
                }
                return;   // caller falls through to the chase locomotion
        }
    }

    // Pull nearby docile/investigating/searching enemies into the chase. Newly
    // recruited chasers become recruiters themselves next tick, so a scream
    // chains through a tight group naturally.
    void RecruitMob()
    {
        float r2 = mobLinkRadius * mobLinkRadius;
        for (int i = 0; i < s_active.Count; i++)
        {
            var other = s_active[i];
            if (other == null || other == this || other._dying) continue;
            if (other._state == AIState.Chasing) continue;
            if ((other.rb.position - rb.position).sqrMagnitude > r2) continue;
            if (other._vision != null) other._vision.ForceAlert();
            other._forceChase = true;   // their next UpdateAIState → EnterChase (+ scream)
        }
    }

    // True if any OTHER chasing enemy within mobLinkRadius currently sees the player.
    bool MobHasVisual()
    {
        float r2 = mobLinkRadius * mobLinkRadius;
        for (int i = 0; i < s_active.Count; i++)
        {
            var other = s_active[i];
            if (other == null || other == this || other._dying) continue;
            if (other._state != AIState.Chasing) continue;
            if (other._vision == null || !other._vision.CanSeePlayerNow) continue;
            if ((other.rb.position - rb.position).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    /// <summary>Loud-noise alert (gunshots): every living enemy within radius locks
    /// onto the player and charges, regardless of vision.</summary>
    public static void AlertNearby(Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        for (int i = 0; i < s_active.Count; i++)
        {
            var e = s_active[i];
            if (e == null || e._dying) continue;
            if ((e.rb.position - pos).sqrMagnitude > r2) continue;
            if (e._vision != null) e._vision.ForceAlert();
            e._forceChase = true;
        }
    }

    void EnterChase()
    {
        if (_state != AIState.Chasing)
        {
            _state = AIState.Chasing;
            if (_oneShotSource != null && spawnLoopClip != null)
                _oneShotSource.PlayOneShot(spawnLoopClip, spawnLoopVolume);   // scream on lock-on
        }
        _loseSightTimer = 0f;
    }

    void EnterSearch()
    {
        _state = AIState.Searching;
        CancelSpit();
        _searchTimer = 0f;
        _nextSniffTime = Time.time;   // sniff right away
        _forceChase = false;
        Vector3 up = (rb.position - (parentPlanet != null ? parentPlanet.Position : Vector3.zero)).normalized;
        _lookTargetRot = RandomTangentRotation(up);
        if (_runSource != null && _runSource.isPlaying) _runSource.Pause();
    }

    // A spit cycle started during a chase must not outlive the chase: TickSpitState is
    // chase-gated, so a half-wound cycle would otherwise freeze (_spitting stuck true →
    // LateUpdate cranks the neck forever, and the stale cycle fires the instant a later
    // chase begins). Cancel cleanly on any exit from Chasing.
    void CancelSpit()
    {
        _spitting = false;
        _spitFired = false;
        _spitPhaseTime = 0f;
    }

    void EnterDocile(bool reLeashHere)
    {
        _state = AIState.Docile;
        CancelSpit();
        _wanderWalking = false;
        _wanderTimer = 0f;
        _forceChase = false;
        if (_runSource != null && _runSource.isPlaying) _runSource.Pause();
        if (reLeashHere && parentPlanet != null)
        {
            _leashLocalToPlanet = parentPlanet.transform.InverseTransformPoint(rb.position);
            _hasLeash = true;
        }
    }

    Vector3 LeashCenterWorld()
    {
        if (_hasLeash && parentPlanet != null) return parentPlanet.transform.TransformPoint(_leashLocalToPlanet);
        return rb.position;
    }

    void TickDocile()
    {
        _wanderTimer -= Time.fixedDeltaTime;
        if (_wanderWalking)
        {
            Vector3 target = parentPlanet.transform.TransformPoint(_wanderTargetLocal);
            Vector3 up = (rb.position - parentPlanet.Position).normalized;
            float horiz = Vector3.ProjectOnPlane(target - rb.position, up).magnitude;
            if (horiz <= stoppingDistance + 0.3f || _wanderTimer <= 0f)
            {
                _wanderWalking = false;
                _wanderTimer = Random.Range(wanderIdleMin, wanderIdleMax);
                _lookTargetRot = RandomTangentRotation(up);
            }
            else StepTowardWorld(target, wanderSpeed, true);
        }
        else
        {
            StepTowardWorld(rb.position, 0f, false);   // hold ground + inherit orbit
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, _lookTargetRot,
                                                     turnSpeedDegPerSec * 0.25f * Time.fixedDeltaTime));
            if (_wanderTimer <= 0f)
            {
                Vector3 center = LeashCenterWorld();
                Vector3 up = (center - parentPlanet.Position).normalized;
                Vector3 t = Vector3.Cross(up,
                    Mathf.Abs(Vector3.Dot(up, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
                // Anti-clumping: roll a few candidate wander points and take the one
                // farthest from its nearest neighbouring enemy — the field drifts APART
                // over time instead of bunching up.
                Vector3 best = center;
                float bestScore = -1f;
                for (int c = 0; c < 3; c++)
                {
                    Vector3 tangent = Quaternion.AngleAxis(Random.Range(0f, 360f), up) * t;
                    Vector3 cand = center + tangent * Random.Range(1f, wanderRadius);
                    float nearestSqr = float.MaxValue;
                    for (int i = 0; i < s_active.Count; i++)
                    {
                        var o = s_active[i];
                        if (o == null || o == this) continue;
                        float d2 = (o.rb.position - cand).sqrMagnitude;
                        if (d2 < nearestSqr) nearestSqr = d2;
                    }
                    if (nearestSqr > bestScore) { bestScore = nearestSqr; best = cand; }
                }
                _wanderTargetLocal = parentPlanet.transform.InverseTransformPoint(best);
                _wanderWalking = true;
                _wanderTimer = 12f;   // safety cap to reach the point
            }
        }
    }

    void EnterInvestigate()
    {
        _state = AIState.Investigating;
        // Snap to face where we FIRST glimpsed the player, then HOLD that facing (no continuous
        // tracking) — this gives the player a window to slip out of the cone before it fills.
        if (player != null && parentPlanet != null)
        {
            Vector3 up = (rb.position - parentPlanet.Position).normalized;
            Vector3 d = Vector3.ProjectOnPlane(player.position - rb.position, up);
            if (d.sqrMagnitude > 0.0001f) _investigateLookDir = d.normalized;
        }
    }

    void TickInvestigate()
    {
        StepTowardWorld(rb.position, 0f, false);   // plant on the spot, stay grounded
        if (parentPlanet == null) return;
        Vector3 up = (rb.position - parentPlanet.Position).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(_investigateLookDir, up);
        if (fwd.sqrMagnitude > 0.0001f)
        {
            Quaternion tr = Quaternion.LookRotation(fwd.normalized, up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, tr, investigateTurnSpeed * Time.fixedDeltaTime));
        }
    }

    void TickSearch()
    {
        _searchTimer += Time.fixedDeltaTime;
        _searchScanTimer -= Time.fixedDeltaTime;
        Vector3 up = (rb.position - parentPlanet.Position).normalized;

        // A glimpse mid-hunt: stop scanning and STARE at it — the 2× suspicion fill does the
        // confirming, and the player gets one last window to break the stare before re-chase.
        if (_vision != null && _vision.CanSeePlayerNow && player != null)
        {
            Vector3 stare = Vector3.ProjectOnPlane(player.position - rb.position, up);
            if (stare.sqrMagnitude > 0.0001f) _lookTargetRot = Quaternion.LookRotation(stare.normalized, up);
            _searchScanTimer = Mathf.Max(_searchScanTimer, 0.5f);   // hold the random scan while staring
        }

        // Actively sweep — snap to a new look direction often so slipping behind them doesn't work.
        // Combined with the expanded chase/search view range, flanking a searcher gets punished.
        if (_searchScanTimer <= 0f)
        {
            _lookTargetRot = RandomTangentRotation(up);
            _searchScanTimer = Random.Range(searchScanIntervalMin, searchScanIntervalMax);
        }

        Vector3 target = (_vision != null && _vision.HasLastSeen) ? _vision.LastSeenPlayerPos : rb.position;
        float horiz = Vector3.ProjectOnPlane(target - rb.position, up).magnitude;
        if (horiz > searchReachDist) StepTowardWorld(target, searchSpeed, false);   // head there, but face the scan dir
        else                         StepTowardWorld(rb.position, 0f, false);        // arrived — hold and scan

        // Fast head/body turns while scanning (whether en route or arrived).
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, _lookTargetRot,
                                                 turnSpeedDegPerSec * 1.5f * Time.fixedDeltaTime));

        if (sniffClip != null && _oneShotSource != null && Time.time >= _nextSniffTime)
        {
            _oneShotSource.PlayOneShot(sniffClip, sniffVolume);
            _nextSniffTime = Time.time + sniffInterval;
        }
        if (_searchTimer >= searchLookSeconds) EnterDocile(true);
    }

    Quaternion RandomTangentRotation(Vector3 up)
    {
        if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
        Vector3 t = Vector3.Cross(up,
            Mathf.Abs(Vector3.Dot(up, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
        Vector3 dir = Quaternion.AngleAxis(Random.Range(0f, 360f), up) * t;
        return Quaternion.LookRotation(dir, up);
    }

    // Cheap grounded step toward a world target along the surface. Shared by the
    // docile / investigate / search states (chase has its own richer locomotion).
    void StepTowardWorld(Vector3 worldTarget, float speed, bool faceTarget)
    {
        Vector3 fromCenter = rb.position - parentPlanet.Position;
        if (fromCenter.sqrMagnitude < 0.0001f) return;
        Vector3 up = fromCenter.normalized;
        int groundMask = ~((1 << 9) | (1 << 11) | (1 << 12));

        Vector3 newPos = rb.position;
        float groundedRange = _scaledGroundedOffset + 0.5f;
        Vector3 probeStart = rb.position + up * 0.1f;
        bool grounded = TryGroundProbe(probeStart, -up, groundedRange, groundMask, out RaycastHit groundHit);
        bool moved = false;

        if (!grounded)
        {
            newPos = rb.position - up * fallSpeed * Time.fixedDeltaTime;
        }
        else
        {
            newPos = groundHit.point + up * _scaledGroundedOffset;
            if (speed > 0.001f)
            {
                Vector3 horizDir = Vector3.ProjectOnPlane(worldTarget - newPos, up);
                float horizDist = horizDir.magnitude;
                if (horizDist > stoppingDistance)
                {
                    float stepLen = Mathf.Min(speed * Time.fixedDeltaTime, horizDist - stoppingDistance);
                    Vector3 stepDir = DeflectAroundObstacles(horizDir.normalized, stepLen, up);
                    Vector3 candidate = newPos + stepDir * stepLen;
                    Vector3 candUp = (candidate - parentPlanet.Position).normalized;
                    Vector3 candProbeStart = candidate + candUp * maxClimbHeight;
                    float candProbeRange = maxClimbHeight + _scaledGroundedOffset + 1f;
                    if (TryGroundProbe(candProbeStart, -candUp, candProbeRange, groundMask, out RaycastHit candHit))
                    {
                        Vector3 snapped = candHit.point + candUp * _scaledGroundedOffset;
                        if (Vector3.Dot(snapped - newPos, up) <= maxClimbHeight) { newPos = snapped; moved = true; }
                    }
                    else
                    {
                        // Ledge — step off and let gravity take it (mirrors the chase path;
                        // freezing here was the stuck-on-roof-edge bug for wander/search too).
                        newPos = candidate;
                        moved = true;
                    }
                }
            }
        }
        _walkingThisStep = moved;   // feeds the stuck watchdog (and nothing else on this path)

        newPos += parentPlanet.velocity * Time.fixedDeltaTime;
        rb.MovePosition(newPos);

        if (_anim != null)
            _anim.SetFloat(_animSpeedHash, moved ? (useChargeBehavior ? 0.5f : 1f) : 0f);

        if (faceTarget)
        {
            Vector3 finalUp = (newPos - parentPlanet.Position).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(worldTarget - newPos, finalUp);
            if (fwd.sqrMagnitude > 0.0001f)
            {
                Quaternion tr = Quaternion.LookRotation(fwd.normalized, finalUp);
                rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, tr, turnSpeedDegPerSec * Time.fixedDeltaTime));
            }
        }
    }

    float _nextSunRefind;   // throttle: no per-tick body scans when the scene has no sun

    void TickSunburn()
    {
        if (_sun == null)
        {
            if (Time.time < _nextSunRefind) return;
            _nextSunRefind = Time.time + 2f;
            _sun = GetSun();
        }
        if (_sun == null || parentPlanet == null) return;
        Vector3 up = (rb.position - parentPlanet.Position).normalized;
        Vector3 sunDir = (_sun.Position - parentPlanet.Position).normalized;
        if (Vector3.Dot(up, sunDir) > sunLitDotThreshold)
        {
            _burnTimer += Time.fixedDeltaTime;
            if (_burnTimer >= sunBurnSeconds) BeginDeath(false);
        }
        else _burnTimer = Mathf.Max(0f, _burnTimer - Time.fixedDeltaTime * 2f);
    }

    CelestialBody GetSun()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        foreach (var b in bodies)
            if (b != null && b.bodyType == CelestialBody.BodyType.Sun) return b;
        return null;
    }

    // ── Spit attack ───────────────────────────────────────────────────────
    void TickSpitState()
    {
        if (!_spitting) return;
        _spitPhaseTime += Time.fixedDeltaTime;

        // Projectile spawns at the END of the windup, the same instant the
        // head animation snaps from chin-up to chin-down (mid-flick).
        if (!_spitFired && _spitPhaseTime >= spitWindUpSeconds)
        {
            _spitFired = true;
            FireSpit();
        }

        // Whole cycle: windup → flick → settle.
        if (_spitPhaseTime >= spitWindUpSeconds + spitFlickSeconds + spitSettleSeconds)
        {
            _spitting  = false;
            _spitFired = false;
        }
    }

    void FireSpit()
    {
        if (player == null) return;
        TryCacheNeckBone();
        // Aim from a point in front of the head if we have a neck reference,
        // otherwise an above-the-body fallback so projectiles still spawn for
        // unrigged enemies.
        Vector3 mouthPos = _neckBone != null
            ? _neckBone.position + (rb.rotation * Vector3.forward) * 0.4f + (rb.rotation * Vector3.up) * 0.2f
            : rb.position + (rb.rotation * Vector3.forward) * 0.5f + Vector3.up * 0.5f;
        Vector3 planetUp = parentPlanet != null
            ? (rb.position - parentPlanet.Position).normalized
            : Vector3.up;
        // Pass parentPlanet so the projectile parents under it and rides
        // floating-origin shifts without desyncing its start position.
        Transform planetT = parentPlanet != null ? parentPlanet.transform : null;
        SpitProjectile.Spawn(mouthPos, player, spitDamage, spitFlightSeconds, spitArcHeight, planetUp, planetT);

        // Spit uses a dedicated AudioSource (built in Awake) with a longer
        // minDistance so it stays at full volume at the standoff range
        // (15-22m) instead of rolling off to ~70% on the shared one-shot
        // source. Falls back to the one-shot source if no spitClip is set
        // / _spitSource wasn't built.
        if (_spitSource != null && spitClip != null)
            _spitSource.PlayOneShot(spitClip, spitVolume);
        else if (_oneShotSource != null && spitClip != null)
            _oneShotSource.PlayOneShot(spitClip, spitVolume);
    }

    void TryCacheNeckBone()
    {
        if (_neckBone != null) return;
        Transform visualRoot = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("Visual_")) { visualRoot = child; break; }
        }
        if (visualRoot == null) return;
        // Prefer Neck (parent of Head) — rotating it carries the head + face
        // along, so the visible motion is bigger and more legible than nudging
        // the head bone alone. Fall back to Head if a rig has no neck.
        _neckBone = FindBySuffixDeep(visualRoot, "_Neck")
                 ?? FindBySuffixDeep(visualRoot, "_Head");
    }

    static Transform FindBySuffixDeep(Transform root, string suffix)
    {
        if (root.name.EndsWith(suffix)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindBySuffixDeep(root.GetChild(i), suffix);
            if (found != null) return found;
        }
        return null;
    }

    // Bone manipulation must happen in LateUpdate — Update / OnAnimatorIK get
    // overwritten by the Animator (CLAUDE.md). Pitch is applied as a delta
    // around the planet-tangent right axis (cross of planet-up and the body's
    // forward) — that's a world-space axis, so it gives a visible head-nod
    // regardless of the rig's bone orientation. Cursed_Toys_II rigs have a
    // hips bone with -80° X baked in, so child bones inherit a non-standard
    // local frame; rotating around their own local X would barely move the
    // head. The world-space approach sidesteps that entirely.
    void LateUpdate()
    {
        if (!_spitting || _dying) return;
        TryCacheNeckBone();
        if (_neckBone == null || parentPlanet == null) return;

        float pitchDeg;
        if (_spitPhaseTime < spitWindUpSeconds)
        {
            float u = _spitPhaseTime / Mathf.Max(0.001f, spitWindUpSeconds);
            pitchDeg = Mathf.Lerp(0f, -spitWindUpPitchDeg, u);   // chin up (wind back)
        }
        else if (_spitPhaseTime < spitWindUpSeconds + spitFlickSeconds)
        {
            float u = (_spitPhaseTime - spitWindUpSeconds) / Mathf.Max(0.001f, spitFlickSeconds);
            pitchDeg = Mathf.Lerp(-spitWindUpPitchDeg, spitFlickPitchDeg, u); // snap chin down (release)
        }
        else
        {
            float u = (_spitPhaseTime - spitWindUpSeconds - spitFlickSeconds)
                    / Mathf.Max(0.001f, spitSettleSeconds);
            pitchDeg = Mathf.Lerp(spitFlickPitchDeg, 0f, Mathf.Clamp01(u));
        }

        Vector3 up = (rb.position - parentPlanet.Position).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, up);
        if (fwd.sqrMagnitude < 0.0001f) return;
        Vector3 right = Vector3.Cross(up, fwd.normalized);
        // +pitchDeg = chin down (toward target), -pitchDeg = chin up (wind back).
        Quaternion delta = Quaternion.AngleAxis(pitchDeg, right);
        _neckBone.rotation = delta * _neckBone.rotation;
    }

    bool IsGroundHit(RaycastHit hit)
    {
        if (hit.collider == ownCollider) return false;
        if (hit.collider.transform.IsChildOf(transform)) return false;
        if (hit.collider.GetComponentInParent<PlayerController>() != null) return false;
        // Props are NOT floor. Accepting any collider as ground is how enemies ended up
        // perched on tree canopies, crystal tips, NPC heads, and each other — then stuck.
        // These stay OBSTACLES (the wall-slide steers around them); they are never stood on.
        // Houses / moon base remain walkable on purpose (roof edges are handled by the
        // ledge step-off, and the stuck watchdog re-seats anything that still wedges).
        if (hit.collider.GetComponentInParent<EnemyController>() != null) return false;
        if (hit.collider.GetComponentInParent<AlienNPCDamageable>() != null) return false;
        if (hit.collider.GetComponentInParent<SpawnedCrystal>() != null) return false;
        if (hit.collider.GetComponentInParent<SpawnedTree>() != null) return false;
        return true;
    }

    // Steep-obstacle slide: cast along the intended step at chest height; a STEEP surface
    // (house wall, moon-base wall, tree trunk, crystal, another enemy) deflects the step to
    // run ALONG the wall instead of grinding into it — enemies flow around buildings instead
    // of jogging in place. Gentle terrain (normal within ~50° of up) is NOT an obstacle; the
    // existing climb logic handles hills. The player is never dodged.
    Vector3 DeflectAroundObstacles(Vector3 stepDir, float stepLen, Vector3 up)
    {
        Vector3 origin = rb.position + up * 0.5f;
        int n = Physics.RaycastNonAlloc(origin, stepDir, s_hitBuffer, stepLen + 1.0f,
                                        ~((1 << 9) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore);
        RaycastHit best = default;
        float bestD = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = s_hitBuffer[i];
            if (h.collider == ownCollider || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (Vector3.Angle(h.normal, up) < 50f) continue;   // walkable slope, not a wall
            if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
        }
        if (!found) return stepDir;

        Vector3 slide = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(stepDir, best.normal), up);
        if (slide.sqrMagnitude < 0.001f)
        {
            // Dead head-on: pick a direction along the wall face.
            slide = Vector3.Cross(up, best.normal);
            if (Vector3.Dot(slide, stepDir) < 0f) slide = -slide;
        }
        return slide.sqrMagnitude > 0.001f ? slide.normalized : stepDir;
    }

    // ── Stuck watchdog ── If the enemy has been TRYING to walk but hasn't actually moved
    // (planet-relative) for a while, it's wedged somewhere the local probes can't solve
    // (inside a house, on a roof lip, prop intersection). Re-seat it on the real terrain
    // column with a sideways nudge — the same top-down cast the spawner uses. Repeated
    // fires keep nudging randomly until it escapes the footprint. Guarantees no enemy
    // stays frozen mid-air or inside geometry forever.
    void TickStuckWatchdog()
    {
        if (parentPlanet == null) return;
        Vector3 nowLocal = parentPlanet.transform.InverseTransformPoint(rb.position);
        if (!_walkingThisStep)
        {
            _stuckRefLocal = nowLocal;
            _stuckDeadline = Time.time + StuckSeconds;
            return;
        }
        if (Time.time < _stuckDeadline) return;
        if ((nowLocal - _stuckRefLocal).sqrMagnitude < 0.25f) ReseatOnTerrain();
        _stuckRefLocal = parentPlanet.transform.InverseTransformPoint(rb.position);
        _stuckDeadline = Time.time + StuckSeconds;
    }

    void ReseatOnTerrain()
    {
        Vector3 up = (rb.position - parentPlanet.Position).normalized;
        Vector3 t = Vector3.Cross(up,
            Mathf.Abs(Vector3.Dot(up, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
        Vector3 side = Quaternion.AngleAxis(Random.Range(0f, 360f), up) * t * 3f;
        Vector3 dir = (rb.position + side - parentPlanet.Position).normalized;
        Vector3 origin = parentPlanet.Position + dir * (parentPlanet.radius + 100f);
        if (TryGroundProbe(origin, -dir, parentPlanet.radius * 2f,
                           ~((1 << 9) | (1 << 11) | (1 << 12)), out RaycastHit hit))
            rb.position = hit.point + dir * _scaledGroundedOffset;   // kinematic teleport is intended here
    }

    // Closest non-self hit along the ray. Plain Physics.Raycast returns only the
    // first hit, which is often our own capsule when the probe origin sits above
    // it — that filtering happens here.
    bool TryGroundProbe(Vector3 origin, Vector3 dir, float distance, int mask, out RaycastHit best)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, s_hitBuffer, distance, mask, QueryTriggerInteraction.Ignore);
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (!IsGroundHit(s_hitBuffer[i])) continue;
            if (s_hitBuffer[i].distance < bestDist)
            {
                bestDist = s_hitBuffer[i].distance;
                best = s_hitBuffer[i];
                found = true;
            }
        }
        return found;
    }

    // Player damage uses Unity's collision events because the player has a
    // dynamic Rigidbody. NPC damage CAN'T use this path: the enemy is
    // kinematic and AlienNPCDamageable has no Rigidbody, and Unity's
    // collision matrix says kinematic-vs-static produces no collision
    // events. That's why enemies were getting "stuck" on NPCs without
    // damaging them — the OnCollisionStay branch never fired. NPC damage
    // is handled by TryBiteNearbyNPC() called from FixedUpdate instead.
    void OnCollisionStay(Collision collision)
    {
        if (_dying) return; // a tumbling corpse shouldn't keep biting the player
        if (Time.time < nextDamageTime) return;
        if (!collision.collider.CompareTag("Player")) return;
        if (ResourceManager.Instance == null) return;

        ResourceManager.Instance.TakeDamage(contactDamage);
        nextDamageTime = Time.time + contactDamageInterval;
        if (_oneShotSource != null && attackClip != null)
            _oneShotSource.PlayOneShot(attackClip, attackVolume);
    }

    // Proximity-based NPC bite. Runs per-FixedUpdate from the locomotion
    // path. Uses the existing nextDamageTime cooldown so a single enemy
    // can't double-tap an NPC + a player in the same tick. npcBiteRadius
    // is tuned for an enemy capsule (~1m radius) touching an NPC live
    // capsule (~0.9m radius) with a small slack.
    void TryBiteNearbyNPC()
    {
        if (_dying) return;
        if (Time.time < nextDamageTime) return;

        var npcs = AlienNPCDamageable.AllInstances;
        if (npcs == null || npcs.Count == 0) return;

        // Scale-aware: enlarged Toy3 has a bigger physical capsule, so its
        // bite envelope grows proportionally with its root scale on the
        // tangent plane.
        float r = npcBiteRadius * Mathf.Max(0.5f, transform.localScale.x);
        float rSqr = r * r;
        Vector3 myPos = rb.position;

        for (int i = 0; i < npcs.Count; i++)
        {
            var npc = npcs[i];
            if (npc == null || npc.IsDying) continue;
            if ((npc.transform.position - myPos).sqrMagnitude > rSqr) continue;

            npc.TakeDamage(npcContactDamage);
            nextDamageTime = Time.time + contactDamageInterval;
            if (_oneShotSource != null && attackClip != null)
                _oneShotSource.PlayOneShot(attackClip, attackVolume);
            return;
        }
    }

    public void TakeBobberDamage()
    {
        TakeDamage(damagePerBobberHit);
    }

    // Called by SaveCollector.ApplyEnemies on load. Sets currentHealth without
    // running the damage path (no killstreak credit, no death trigger), and
    // brings the health bar up to mirror what the player would have seen at
    // save time if the enemy wasn't at full HP.
    public void SetHealthOnLoad(float h)
    {
        currentHealth = Mathf.Clamp(h, 0f, maxHealth);
        if (healthBar != null && currentHealth < maxHealth)
        {
            healthBar.Show();
            healthBar.SetFill(Mathf.Clamp01(currentHealth / maxHealth));
        }
    }

    public void ApplyKnockback(Vector3 worldDir, float distance, float duration)
    {
        if (duration <= 0f || distance <= 0f) return;
        if (worldDir.sqrMagnitude < 0.0001f) return;
        _knockbackVel = worldDir.normalized * (distance / duration);
        _knockbackTimer = duration;
    }

    public void TakeDamage(float amount) => TakeDamage(amount, true);

    // creditPlayer = false for environmental kills (torch aura, Lebron light)
    // so the killstreak HUD / slow-mo only react to weapon kills, not passive
    // damage from placed buildings or the ship.
    public void TakeDamage(float amount, bool creditPlayer)
    {
        if (amount <= 0f || _dying) return;

        // Getting shot by the PLAYER always alerts them → chase (even from outside their
        // view cone). Environmental damage (torch aura / Lebron light / concert speakers,
        // creditPlayer == false) must NOT alert: ForceAlert writes the player's live position
        // into LastSeen, so a docile enemy grazing a torch would beacon-lock onto a hidden
        // player through walls — turning defensive torches into player-position trackers.
        if (creditPlayer)
        {
            if (_vision != null) _vision.ForceAlert();
            _forceChase = true;
        }

        // Random blood splash at the body centre on any PLAYER hit (gun / axe /
        // fishing rod all route through here). creditPlayer == false is
        // environmental damage (torch, Lebron) and is excluded.
        if (creditPlayer) BloodFX.Instance?.SpawnDamageSplash(rb.position, transform, transform.localScale.x);

        currentHealth -= amount;
        if (healthBar != null)
        {
            healthBar.Show();
            healthBar.SetFill(Mathf.Clamp01(currentHealth / maxHealth));
        }
        if (currentHealth <= 0f)
            BeginDeath(creditPlayer);
    }

    System.Collections.Generic.List<Rigidbody> _registeredBones;
    System.Collections.Generic.List<Collider> _hitColliders; // live per-bone hit colliders (removed on death)

    void BeginDeath(bool creditPlayer)
    {
        if (_dying) return;
        _dying = true;
        if (creditPlayer) OnAnyEnemyDeath?.Invoke();
        // Perception dies with the body: stop EnemyVision (its Update would keep filling
        // suspicion / rendering the cone on a 30s ragdoll corpse) and leave the live-enemy
        // list so the detection HUD and separation steering ignore the corpse.
        if (_vision != null)
        {
            _vision.SetConeVisible(false);
            _vision.enabled = false;
        }
        CancelSpit();
        s_active.Remove(this);
        _knockbackTimer = 0f;
        if (healthBar != null) healthBar.gameObject.SetActive(false);
        if (_runSource != null && _runSource.isPlaying) _runSource.Stop();
        if (_oneShotSource != null && deathClip != null)
            _oneShotSource.PlayOneShot(deathClip, deathVolume);

        // Stop the animator so it doesn't fight the ragdoll. The bones go limp
        // at whatever pose the animator was on this frame.
        if (_anim != null) _anim.enabled = false;

        // Capture motion-carry inputs BEFORE we touch anything else.
        Vector3 forwardDir = rb.rotation * Vector3.forward;
        Vector3 planetVel  = parentPlanet != null ? parentPlanet.velocity : Vector3.zero;
        Vector3 up         = parentPlanet != null
            ? (rb.position - parentPlanet.Position).normalized
            : Vector3.up;

        // The outer body becomes a stationary script container; the ragdoll
        // bones drive all visible motion now. Disable the outer collider so
        // the player can walk through the corpse without bumping a phantom
        // capsule hovering at the death position.
        if (ownCollider != null) ownCollider.enabled = false;

        // Build the ragdoll AT RUNTIME — bones have no physics components
        // during life so the Animator drives them without
        // SkinnedMeshRenderer/interpolation interference. Inject the full
        // RB + collider + joint stack now, already non-kinematic with the
        // inherited velocity.
        // Use the field moveSpeed (3) for the death kick, NOT _currentMoveSpeed —
        // killing a charging Toy3 with chargeSpeed=6 would otherwise launch the
        // ragdoll bones twice as hard as a regular Toy10. The kick should feel
        // the same regardless of which mob died or what phase it was in.
        Vector3 baseVel = planetVel + forwardDir * moveSpeed + up * 1f;

        // Tear down the live hit colliders before the ragdoll injects its own
        // per-bone colliders, so each bone doesn't end up with two.
        if (_hitColliders != null)
        {
            for (int i = 0; i < _hitColliders.Count; i++)
                if (_hitColliders[i] != null) Destroy(_hitColliders[i]);
            _hitColliders = null;
        }

        // Match any visual child by prefix instead of hard-coding "Visual_Toy10",
        // so future variants (Toy3, Toy7, …) all wire up their ragdolls. Build-
        // ToyEnemyPrefab and BuildToy3EnemyPrefab both name the visual child
        // "Visual_<rigName>".
        Transform visualRoot = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("Visual_")) { visualRoot = child; break; }
        }
        _registeredBones = visualRoot != null
            ? EnemyRagdollBuilder.BuildAndActivate(visualRoot, baseVel, hitboxRadiusScale)
            : new System.Collections.Generic.List<Rigidbody>();

        // Keep the corpse parented to its planet. Origin shifts move the
        // planet (registered with EndlessManager); the wrapper, the rig
        // root, the SkinnedMeshRenderer host, and every bone follow in
        // lockstep via Unity's transform hierarchy. Bones still ragdoll
        // locally via their Rigidbodies + CharacterJoints.
        Physics.SyncTransforms();

        // n-body gravity for every bone.
        var grav = gameObject.AddComponent<RagdollGravity>();
        grav.Init(_registeredBones.ToArray());

        _deathTimer      = 0f;
        _deathStartScale = transform.localScale;
    }

    void TickDeath()
    {
        _deathTimer += Time.fixedDeltaTime;

        // Phase 1: free physics tumble — but only until the corpse settles
        // (relative to the orbiting planet) or MaxFreeTumbleSeconds elapses; then
        // fall through and freeze it. Free-simulating world-space bones for the
        // full 30s drifts them out of the planet's orbital frame and grinds the
        // (fat) capsules against the curved terrain — the "not moving relative to
        // the planet / flies off at despawn" weirdness. Freezing early and
        // locking to the planet avoids both.
        if (!_frozenForShrink
            && _deathTimer < MaxFreeTumbleSeconds
            && !(_deathTimer >= MinFreeTumbleSeconds && BonesSettledRelativeToPlanet()))
            return;

        // Phase 2: freeze the corpse, re-anchor it where the bones came to
        // rest, then collapse uniformly to nothing.
        //
        // Prior attempts hit four distinct glitches; this routine fixes each:
        //   1. Non-kinematic bones keep writing rb.position → transform every
        //      FixedUpdate. That fights the parent localScale change and the
        //      mesh stays full-size all the way to scale=0, then snap-destroys.
        //   2. The root rigidbody has been gravitating freely for 30s (its
        //      collider is disabled at BeginDeath) and has drifted far from
        //      the bones — often into space. Shrinking around a drifted root
        //      yanks the bones toward it as the scale collapses.
        //   3. Moving the root displaces children by (newRoot - oldRoot) since
        //      their localPositions are constant — bones jump on the re-anchor.
        //   4. Even with kinematic bones, CharacterJoint enforcement and
        //      collider contacts can produce visible micro-jitter — "they were
        //      shaking on the ground as if they were still a physics object."
        //
        // The cure: snapshot bone world poses, re-anchor the root to the hips,
        // then RIP OUT every runtime-added physics component on each bone
        // (RB + SphereCollider + CharacterJoint + RagdollBoneMarker) plus the
        // root's RagdollGravity. Bones become inert Transforms, the parent
        // scale propagates cleanly down, and there's nothing left to twitch.
        if (!_frozenForShrink)
        {
            _frozenForShrink = true;
            int n = _registeredBones.Count;

            // Freeze every bone FIRST — kinematic + zero velocity — BEFORE the
            // root re-anchor teleport below. A still-dynamic bone moved by the
            // root teleport gets a depenetration impulse from the physics engine
            // (Destroy on the bone RBs is deferred to end-of-frame, so they're
            // still live when the teleport happens). That impulse is the "sudden
            // burst of velocity / fling right as they despawn." Kinematic bodies
            // ignore teleports, so freezing them first kills the fling.
            for (int i = 0; i < n; i++)
            {
                var b = _registeredBones[i];
                if (b == null) continue;
                b.velocity        = Vector3.zero;
                b.angularVelocity = Vector3.zero;
                b.isKinematic     = true;
            }

            // Cache transforms BEFORE we destroy any Rigidbodies — once a
            // Rigidbody is destroyed, the b reference is dead and we'd lose
            // the path to its transform for the restore pass.
            var boneTransforms = new Transform[n];
            var savedPos       = new Vector3[n];
            var savedRot       = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var b = _registeredBones[i];
                if (b == null) continue;
                boneTransforms[i] = b.transform;
                savedPos[i]       = b.transform.position;
                savedRot[i]       = b.transform.rotation;
            }

            // Anchor = the hips bone (index 0 in EnemyRagdollBuilder.Bones,
            // the only spec with parentSuffix == null). Falls back to the
            // current root if hips is missing.
            Vector3 anchor = transform.position;
            if (n > 0 && _registeredBones[0] != null)
                anchor = _registeredBones[0].transform.position;

            // Pin the root: disable n-body gravity, kinematicize the rigidbody,
            // zero velocity, snap to the bone cluster.
            var gravFull   = GetComponent<GravityObject>();
            if (gravFull   != null) gravFull.enabled   = false;
            var gravSimple = GetComponent<GravityObjectSimple>();
            if (gravSimple != null) gravSimple.enabled = false;
            if (rb != null)
            {
                rb.velocity        = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic     = true;
                rb.position        = anchor;
            }
            transform.position = anchor;

            // Rip out every physics component the ragdoll added at death.
            // RagdollBoneMarker.OnDestroy auto-unregisters the bone from
            // RagdollBoneRegistry, so the floating-origin shift list stays
            // clean even though we're not calling Unregister manually.
            var ragGrav = GetComponent<RagdollGravity>();
            if (ragGrav != null) Destroy(ragGrav);
            for (int i = 0; i < n; i++)
            {
                var b = _registeredBones[i];
                if (b == null) continue;
                var go = b.gameObject;
                // Order matters only for Unity's warning suppression — destroy
                // joints first so a parent-RB destruction doesn't log a null
                // connectedBody warning on a still-live joint.
                var joint  = go.GetComponent<CharacterJoint>();
                if (joint  != null) Destroy(joint);
                // Collider, not SphereCollider — the ragdoll uses CapsuleColliders
                // now (head is a sphere); GetComponent<Collider> removes either.
                var col    = go.GetComponent<Collider>();
                if (col    != null) Destroy(col);
                var marker = go.GetComponent<RagdollBoneMarker>();
                if (marker != null) Destroy(marker);
                Destroy(b);
            }

            // Restore each bone's world pose — the root re-anchor would
            // otherwise have dragged them by (anchor - oldRootPos).
            for (int i = 0; i < n; i++)
            {
                var t = boneTransforms[i];
                if (t == null) continue;
                t.SetPositionAndRotation(savedPos[i], savedRot[i]);
            }
            Physics.SyncTransforms();
        }

        // Keep tracking the planet's orbit while frozen — the SAME manual
        // planetVel step the live enemy uses in its locomotion. A frozen
        // kinematic corpse that stops doing this is stationary in world space,
        // so the orbiting planet flies out from under it (the "flies off the
        // planet" bug — it wasn't the freeze flinging it, it was the freeze
        // stopping the orbit tracking). Bones follow via the hierarchy.
        if (rb != null && rb.isKinematic && parentPlanet != null)
            rb.MovePosition(rb.position + parentPlanet.velocity * Time.fixedDeltaTime);

        // Frozen and riding the planet — hold full-size until the corpse's full
        // lifetime, then shrink it away. (Freeze happens early, well before
        // RagdollDuration, so this gate keeps it whole during the frozen hold.)
        if (_deathTimer < RagdollDuration) return;
        float shrinkU = (_deathTimer - RagdollDuration) / DeathShrinkDuration;
        if (shrinkU >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        transform.localScale = _deathStartScale * (1f - shrinkU);
    }

    // True once every ragdoll bone is moving slowly RELATIVE TO THE PLANET (not
    // world space — the planet orbits fast, so a settled corpse still has a big
    // world velocity equal to the planet's). Used to freeze the corpse as soon
    // as it comes to rest instead of free-simulating it for the full 30s.
    bool BonesSettledRelativeToPlanet()
    {
        if (_registeredBones == null || _registeredBones.Count == 0) return true;
        Vector3 planetVel = parentPlanet != null ? parentPlanet.velocity : Vector3.zero;
        float thrSqr = SettleSpeedThreshold * SettleSpeedThreshold;
        for (int i = 0; i < _registeredBones.Count; i++)
        {
            var b = _registeredBones[i];
            if (b == null || b.isKinematic) continue;
            if ((b.velocity - planetVel).sqrMagnitude > thrSqr) return false;
        }
        return true;
    }

    public static System.Collections.Generic.IReadOnlyList<EnemyController> ActiveEnemies => s_active;

    void OnDestroy()
    {
        if (EnemySpawner.Instance != null) EnemySpawner.Instance.OnEnemyDestroyed(this);
    }

    // Scene-view visualization of the bone hit/ragdoll capsules so the hitbox
    // can be seen and tuned (hitboxRadiusScale) without the Physics Debugger.
    // Editor-only; gizmos never render in the game/build. Capsules are drawn as
    // a wire sphere at each end of the bone segment + a connecting line.
    void OnDrawGizmos()
    {
        if (!drawHitboxGizmos) return;
        Transform rig = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            var c = transform.GetChild(i);
            if (c.name.StartsWith("Visual_")) { rig = c; break; }
        }
        if (rig == null) return;

        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.9f);
        foreach (var bc in EnemyRagdollBuilder.GetBoneCapsules(rig, hitboxRadiusScale))
        {
            if (bc.bone == null) continue;
            float wr = bc.radius * bc.bone.lossyScale.x;
            Gizmos.DrawWireSphere(bc.bone.position, wr);
            if (bc.tip != null)
            {
                Gizmos.DrawWireSphere(bc.tip.position, wr);
                Gizmos.DrawLine(bc.bone.position, bc.tip.position);
            }
        }
    }

    CelestialBody GetNearestPlanet()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody nearest = null;
        float minDist = float.PositiveInfinity;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) continue;
            float d = (b.Position - rb.position).magnitude - b.radius;
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }

    // ── Stealth revamp tunables (appended per CLAUDE.md serialization rule) ──
    [Header("Stealth — wander (docile)")]
    [SerializeField] float wanderRadius = 12f;       // leash from the spawn point
    [SerializeField] float wanderSpeed = 1.2f;       // slow amble
    [SerializeField] float wanderIdleMin = 2f;
    [SerializeField] float wanderIdleMax = 5f;

    [Header("Stealth — investigate / search")]
    [SerializeField] float investigateTurnSpeed = 540f;  // fast snap-turn toward the first-glimpse spot
    [SerializeField] float searchSpeed = 2.5f;           // move to last-seen spot
    [SerializeField] float searchLookSeconds = 10f;      // look around before giving up
    [SerializeField] float searchReachDist = 2.5f;
    [SerializeField] float searchScanIntervalMin = 0.7f; // snap to a new look dir this often while searching
    [SerializeField] float searchScanIntervalMax = 1.4f;
    [SerializeField] float loseSightGrace = 1.2f;        // chase → search after losing LOS this long

    [Header("Stealth — sniff (searching audio)")]
    [SerializeField] AudioClip sniffClip;            // regular = soft, brute = louder (set per prefab)
    [SerializeField, Range(0f, 1f)] float sniffVolume = 0.9f;
    [SerializeField] float sniffInterval = 3f;

    [Header("Stealth — sunlight burn")]
    [SerializeField] float sunBurnSeconds = 3.5f;    // time in light before death
    [SerializeField] float sunLitDotThreshold = 0.02f;

    [Header("Stealth — mob mentality")]
    [Tooltip("A chasing enemy WITH visual recruits non-chasing enemies within this radius; chasers within it of a seeing packmate stay locked on without their own visual.")]
    [SerializeField] float mobLinkRadius = 20f;
}
