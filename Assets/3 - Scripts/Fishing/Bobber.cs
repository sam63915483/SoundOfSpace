using UnityEngine;
using System.Collections;

// Order 250: the pre-cast wind/glue positions the bobber at the rod tip in
// LateUpdate, and the tip is only final after ViewmodelMotor (150) has swayed
// the rod rig. Running earlier would track last frame's tip.
[DefaultExecutionOrder(250)]
public class Bobber : MonoBehaviour
{
    public float shootSpeed = 5f;

    [Header("Bobbing Settings")]
    // Sam, 2026-09-01: the old 0.15 / 0.30 made the bobber travel ~10 inches
    // idle and about two FEET on a run -- it read as a glitch, not as a float
    // sitting in water. A real bobber barely moves: it shivers, dips, and gets
    // TOWED. All the drama now comes from distance and jitter, not altitude.
    [Tooltip("Metres the float rises and falls at rest. Keep this tiny -- 1-2 cm reads as water, anything more reads as a bug.")]
    public float bobAmplitude = 0.015f;
    public float bobFrequency = 1.2f;

    [Header("Fishing Settings")]
    public float minStrikeWaitTime = 1f;
    public float maxStrikeWaitTime = 10f;
    [Tooltip("Shake frequency once a fish is on the hook. Fast and shallow is what reads as a bite.")]
    public float strikeBobFrequency = 16f;
    [Tooltip("Metres of shake during a bite. Sam cut this twice -- it is a float being nibbled, not a buoy in a storm. 2 cm is plenty at this frequency.")]
    public float strikeBobAmplitude = 0.02f;

    [Header("Swim (bite + fight)")]
    // Sam, 2026-09-01: "the bobber moves wayy too much left and right, instead of
    // 30 degrees relative to the player maybe just make it able to travel like a
    // meter left and a meter right." Metres are the right unit: an ANGLE means a
    // wildly different amount of travel at 4 m than at 20 m, which is exactly why
    // 30 degrees looked insane on a long cast.
    [Tooltip("Metres the fish may wander to either side of the line of the cast. A metre reads as a fish nosing about; an angle would swing metres wide on a long cast.")]
    public float maxSwimLateral = 1f;
    [Tooltip("Metres per second the fish drifts sideways. Low = lazy cruising, high = darting.")]
    public float swimLateralSpeed = 0.5f;

    [Header("Retrieve (reeling an empty lure)")]
    [Tooltip("Metres per second the TOW winds a free physics bobber home over land.")]
    public float retrieveSpeed = 3.3f;
    [Tooltip("Metres of line the wound-in bobber HANGS on below the rod tip, still a physics object, until the next cast.")]
    // One unmissable line per session: WHICH code is actually running. The
    // whole late-night hang saga was Sam playtesting stale builds -- twice a
    // freshly built DLL sat on disk while the running game kept old code in
    // memory (launched before the build finished writing). The stamp in the
    // player log ends every "is the fix even in?" debate in one glance.
    public const string BuildStamp = "2026-09-03-BB-authored-npcs";

    public float hangLeash = 0.75f;
    [Tooltip("Metres per second an empty lure slides back through the WATER. Sam asked for 1.5x the tow speed here -- water gives no bumps to fight, so a slightly brisker glide reads right.")]
    public float waterRetrieveSpeed = 3.3f;
    [Tooltip("How much the bite countdown speeds up while you are working the lure across the water. 1.2 = 20% better odds, because a moving lure interests fish.")]
    public float retrieveBiteBonus = 1.2f;
    [Tooltip("Bite-odds multiplier for the REST of this cast after a fish has been and gone. Real fishing: a spot that just produced a bite is still a good spot. Resets when you wind in and cast again, and STACKS with the retrieve bonus.")]
    public float lostBiteBonus = 1.3f;
    [Tooltip("How hard an empty lure loads the rod. Small -- there is nothing on the end of it, just water resistance.")]
    [Range(0f, 1f)] public float emptyLureRodLoad = 0.16f;

    [Header("Reel Home")]
    [Tooltip("Distance from the angler at which the bobber stops sliding across the water and starts being wound UP to the rod tip. Sam: 'about a foot before the bobber hits the bank'.")]
    public float tipReelStartDistance = 0.4f;

    [Header("Shore Catch")]
    [Tooltip("Radius of the check that decides the bobber has reached solid ground. Backstop for the waterline probe below -- and what makes fishing off a cliff work, where you are never within 2 m of the water.")]
    public float shoreTouchRadius = 0.45f;
    [Tooltip("How far AHEAD of the bobber, along the water toward you, to look for the waterline. When that spot is inside terrain the water has run out, so the bobber leaves it here rather than being dragged under the bank.")]
    public float shoreProbeAhead = 0.35f;
    [Tooltip("Radius of the look-ahead probe.")]
    public float shoreProbeRadius = 0.2f;

    [Header("Fight Motion")]
    [Tooltip("Seconds between the sharp downward TUGS a hooked fish gives the float. The single clearest 'there is something on' tell -- scaled by how much fight the fish has left.")]
    public float tugIntervalMin = 0.35f;
    public float tugIntervalMax = 1.1f;
    [Tooltip("Metres the float is yanked under on a tug.")]
    public float tugDepth = 0.09f;
    [Tooltip("Metres of erratic jitter while a hooked fish is fighting. Sideways as well as vertical, so it reads as something alive rather than a bouncing ball.")]
    public float fightJitter = 0.035f;
    [Tooltip("Extra jitter multiplier while the fish is RUNNING. The run reads as the float being towed away from you -- the distance does the work, not the height.")]
    public float runJitterMultiplier = 1.8f;
    [Tooltip("How fast the bobber chases its commanded distance. Higher snaps, lower drifts.")]
    public float fightFollowSpeed = 9f;

    public float commonFishStrikeDuration = 3f;
    public float uncommonFishStrikeDuration = 2f;
    public float rareFishStrikeDuration = 1f;

    [Header("Sound Effects")]
    public AudioClip waterSplashClip;
    public AudioClip biteClip;
    [Range(0, 1)] public float waterSplashVolume = 0.5f;
    [Range(0, 1)] public float biteVolume        = 0.5f;

    private AudioSource audioSource;
    private AudioSource biteSource;

    [Header("Fishing Revamp (Phase 1)")]
    [Tooltip("Optional. Leave empty and the built-in defaults in FishingTuning are used -- everything still works, the knobs just aren't editable without a recompile.")]
    public FishingTuning tuning;

    private Rigidbody rb;
    private bool hasHitWater = false;
    private bool hasHitEnemy = false;
    private Vector3 baseLocalPosition;
    private Vector3 bobUpLocal = Vector3.up;   // outward surface normal, in the planet's local space
    private bool isFishingActive = false;
    private bool isStriking = false;
    private float strikeEndTime;
    private string currentFishType = "";
    private bool fishCaught = false;
    private Coroutine fishingCoroutine;

    // ── Phase 1 revamp state ────────────────────────────────────────────────
    private Transform planetBody;          // what we're parented to; the sun dot needs it
    private int   pendingSpecies = -1;     // rolled ON THE BITE, not on the cast
    private int   pendingWeight;
    private BaitKind pendingBait = BaitKind.None;
    private bool  baitSpent;               // guards double-consume across the hook window
    private FishFightSim fight;            // non-null only while the fight is live
    private Transform fightPlayer;         // who is reeling -- the distance is measured to them
    // Un-jittered fight position, in PLANET-LOCAL space (floating-origin safe --
    // a cached world position would be shredded by an EndlessManager shift).
    // Jitter is added on top for rendering only and never fed back in.
    // Planet-local direction from the player out to where the cast landed. The
    // swim cone is measured off this, so the fish can never work its way behind
    // the angler no matter how long the fight runs.
    // Where the fish actually is, before its cosmetic wander. Planet-local, so a
    // floating-origin shift cannot touch it. THE authority for the fight's maths.
    private Vector3 _anchorLocal;
    private bool _retrieving;          // player is winding an empty lure back in
    private float _retrieveTaut;       // the line's own tautness while retrieving
    private float _biteOddsBonus = 1f; // stacks up as this cast produces near-misses
    private float _nextTug, _tugPhase; // the "something is on" pulse
    private bool _pendingLanding;      // a caught fish waiting to be wound home
    private int _landedSpecies = -1;   // captured at the catch; the roll state resets
    private int _landedWeight;
    private string _landedTier = "";

    /// The rod that owns this bobber. Set on spawn; the tether pulls toward its tip.
    [System.NonSerialized] public FishingRodController rodOwner;

    [Header("Hooked Fish Visual")]
    // Body length now comes from FishingRules.BodyLengthForWeight -- ONE
    // cube-root law shared with the held-in-hand fish, so a 1 lb smelt and a
    // 50 lb marlorb finally look like what they weigh everywhere. (The old
    // linear min/max lerp squeezed a 50x weight span into 3x of length, which
    // is why every fish read as roughly the same animal.)
    [Tooltip("Flip if the fish model faces the wrong way on the line (only used when the prefab has no MOUTH marker).")]
    public bool flipHookedFish;
    GameObject _hookedFish;    // the visible fish with its mouth on the bobber
    Transform _fishMouth;      // Sam's hand-placed "MOUTH" marker inside the prefab
    Vector3 _fishMouthDirLocal; // root-local direction out of the mouth (centre -> MOUTH)
    Vector3 _fishUpLocal;      // root-local up, orthogonal to the mouth direction
    Vector3 _fishMouthInRoot;  // mouth offset in root-local units, scaled -- a CONSTANT
    float _fishHalfLen;
    float _fishPhase;
    // The fish step clamp's memory: the last pose actually written.
    // The pursuit follower's own state: the fish's actual planet-local
    // position and its integrated velocity. The ONLY authority on where the
    // fish is during approach/retreat -- path points are advisory targets it
    // swims toward, never teleports onto.
    Vector3 _fishSwimPos;
    Vector3 _fishSwimVel;
    bool _fishSwimInit;
    // True while a coroutine (the approach rise/circle or the retreat) owns
    // the fish's pose; gates the LateUpdate landed-pose writer off so two
    // writers never fight over one fish.
    bool _fishApproachActive;

    // ── The tow ─────────────────────────────────────────────────────────────
    // The bobber is EXACTLY the physics object it always was: instantiated on
    // the cast, flying and bouncing under GravityObjectSimple, parked (physics
    // removed) when it lands on water. The ONE addition, per Sam: while it is a
    // free physics object — a cast lying on land, or a lure pulled back over
    // the waterline — holding the reel tows it to you on a shortening line.
    float _towLine = -1f;              // metres of line out while towing; <0 = not towing
    float _waterIgnoreUntil;           // re-park cooldown after leaving the water
    bool _hanging;                     // wound home: dangling on hangLeash, still physics
    bool _windingToTip;                // cast clicked: the foot of line retracting
    bool _gluedToTip;                  // held at the tip, waiting for the fling
    const float ReelPickupRange = 1.1f; // this close to the tip = wound in, hang it
    const float WindToTipSeconds = 0.22f; // the pre-cast pull -- 0.1 read as a teleport-flicker at 0.75m leash
    float _windT;                        // 0..1 through that pull
    Vector3 _windOffset;                 // bobber-to-tip offset captured at the click

    // The rod tip's measured velocity: the TRUE frame for the hang and the tow.
    // The player's rigidbody velocity is not enough — walking is driven through
    // MovePosition, so rb.velocity under-reports WASD movement, and damping the
    // bobber toward that under-reported frame is exactly why the hang jittered
    // most while strafing. The thing the bobber must move with is the TIP, so
    // the tip is what gets measured.
    Vector3 _prevTipPos;
    Vector3 _tipVel;
    bool _tipVelInit;

    // The hang: simulated at RENDER RATE with a SINGLE WRITER.
    //
    // Every other approach had two writers or two clocks and jittered:
    // - dynamic rb at 50 Hz vs a render-rate tip = sampling alias on strafe;
    // - recording fixed-step offsets and writing the transform in LateUpdate
    //   fought PhysX, which writes the transform after every step for a
    //   non-kinematic body — two writers alternating = flicker even standing
    //   still (the version Sam called "super worse").
    // So while hanging there is NO rigidbody at all. The same damped-rope maths
    // as the play-proven tow integrates here against the LIVE tip each frame:
    // one clock, one writer, real planet gravity via NBodySimulation. The tow,
    // the casts and the land physics remain genuine rigidbody physics.
    Vector3 _hangOffset;   // tip -> bobber, the ONLY thing the hang simulates
    Vector3 _hangOffVel;   // its velocity (already tip-relative by construction)
    Vector3 _prevTipRender;
    Vector3 _hangTipVel;   // lightly smoothed, used ONLY to derive tip inertia
    bool _hangSimInit;

    // The bobber's RENDERED velocity while it is parked on the water. The
    // water park and the fight animate the transform, so the moment physics
    // re-attaches at the shore, rb.velocity seeded from the rod tip's frame
    // throws away the shoreward glide the player was just watching -- a
    // one-frame stall that reads as a snap. Seed from what was RENDERED and
    // the handoff is invisible (the shuttle landing-pop lesson, again).
    Vector3 _prevRenderPos;
    Vector3 _renderVel;
    bool _renderVelInit;

    // The park pose the water-phase animators write; ApplyParkPose puts it on
    // the transform (planet-local when parented). Parented + transform-driven
    // is the proven park: a kinematic MovePosition park was tried and rendered
    // a constant step of lag off the ocean -- keep it transform-driven.
    Vector3 _parkLocal;

    // ── PHYSICS-CLOCK BIRTH (stamp BA, 2026-09-03) ──────────────────────────
    // The shore release is REQUESTED from Update (retrieve / fight land) and
    // PERFORMED in the next FixedUpdate, so the newborn rigidbody exists before
    // its first simulate and is born at the planet's PHYSICS pose, not the
    // rendered one. See ReleaseFromWaterNow for the arithmetic.
    bool _releasePending;
    CelestialBody _towPlanet;      // the planet the bobber left at the shore
    Vector3 _hangSeedLocal;        // rb pose at BeginHang, planet-local (physics clock)
    bool _hangSeedValid;

    /// <summary>What the rod's mesh bend aims at: the drawn pose, always.
    /// Never rb.position -- the raw pose of an interpolated body sawtooths
    /// against the rendered world every frame, and a shivering bend target
    /// pumps velocity noise into the tow through _tipVel.</summary>
    public Vector3 BendTargetPose => transform.position;

    /// Metres per second the velocity-level leash correction applies per metre
    /// of overshoot. High enough to feel like line, low enough never to launch.
    const float LeashStiffness = 18f;
    const float LeashMaxPull = 25f;    // emergency cap on the correction velocity
    private float _swimLateral;      // metres across the line of the cast
    private float _swimForward;      // metres in / out along it
    private float _swimTargetLat, _swimTargetFwd;
    private float _nextSwimPick;
    private static int _groundMask = -1;
    private float waterRadius;             // distance from planet centre to the water surface
    private float bankedSpin;              // spin banked at the HOOK, paid out on landing
    private int   bankedCombo;

    public bool IsInWater => hasHitWater;
    public bool IsStriking => isStriking;
    /// True from the moment the hook lands until the fish is landed, lost or slips.
    public bool IsFighting => fight != null;
    /// Spin banked at the hook, paid out by the rod when the fish lands.
    public float BankedSpin => bankedSpin;
    public int   BankedCombo => bankedCombo;
    public float FightTension01 => fight != null ? fight.TensionFraction : 0f;
    public bool  FightIsRunning => fight != null && fight.IsRunning;
    public bool  FightIsSpent   => fight != null && fight.IsSpent;
    /// 1 = fresh and thrashing, 0 = beaten. Drives how much the fish throws the
    /// float around, which is the ONLY readout of how tired it is.
    public float FightVigour => fight != null ? fight.Vigour : 1f;

    /// True while the player is winding an empty lure back across the water.
    public bool IsRetrieving => _retrieving;
    /// Raised the moment a fish is actually booked -- after it has been wound
    /// home, not when the fight ended. Carries the banked spin and combo.
    public System.Action<float, int> OnFishLanded;

    /// <summary>
    /// The line's tautness, whatever is going on: a fight, a retrieve, or
    /// nothing. ONE number for the line you see and the gates that use it.
    /// </summary>
    public float LineTaut01 => fight != null ? fight.LineTaut : _retrieveTaut;

    /// <summary>
    /// How far the rod is bent, whatever is going on. An empty lure loads the
    /// rod only slightly, and only once the line has actually come tight -- the
    /// same cascade as a fight, just with far less on the end of it.
    /// </summary>
    public float RodLoad01(bool reeling)
    {
        if (fight != null) return fight.RodLoad(reeling);
        if ((_retrieving || _pendingLanding) && _retrieveTaut >= FishFightSim.TautThreshold)
            return emptyLureRodLoad;
        return 0f;
    }

    /// <summary>Driven by the rod every frame: is the player winding in?</summary>
    public void SetRetrieving(bool on)
    {
        _retrieving = on && fight == null && !hasHitEnemy;
    }
    public System.Action OnFishEscaped;
    /// Raised when the line snaps — the rod plays its recoil and drops the line.
    public System.Action OnLineSnapped;
    /// <summary>
    /// Raised when a fight ends, carrying HOW it ended. The rod winds in on a
    /// landing or a snapped line, and deliberately does NOT on a spat hook --
    /// that cast is still live.
    /// </summary>
    public System.Action<FightOutcome> OnFightEnded;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        biteSource = gameObject.AddComponent<AudioSource>();
        biteSource.playOnAwake = false;
        biteSource.loop = true;
        biteSource.volume = biteVolume;

        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null) em.RegisterPhysicsObject(transform);

        // Cached HERE, not in StopOnWater: a cast that lands on dry ground never
        // reaches StopOnWater, and without an angler to reel toward there was no
        // way to get the bobber back at all except swapping hotbar slots.
        var pc0 = FindObjectOfType<PlayerController>();
        if (pc0 != null) fightPlayer = pc0.transform;

        Debug.Log($"[Bobber] Spawned and registered (code {BuildStamp}).");
    }

    void Update()
    {
        // The FIGHT is driven from TickFight, not here, so the order is always
        // sync-reality -> step-the-sim -> move-the-bobber.
        if (fight != null) return;

        // Line tautness bookkeeping, every phase: it drives the visible sag and
        // gates the tether (a slack line cannot pull).
        var tune = FishingTuning.Active;
        bool pulling = _retrieving || _pendingLanding;
        _retrieveTaut = Mathf.MoveTowards(_retrieveTaut, pulling ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.01f, pulling ? tune.lineTautSeconds : tune.lineSlackSeconds));

        // (The old hang watchdog lived here and was the bunching bug: it
        // measured the tip in Update, HALF A FRAME before LateUpdate moved the
        // rod with the camera, so one quick mouse flick read as the bobber
        // being metres adrift -- and its per-frame "rescue" snap then pinned
        // the bobber to the tip, overpowering the sim. The player log caught
        // it red-handed. The offset-space rope already hard-caps the distance
        // every frame, so no watchdog is needed or wanted.)

        // Only the WATER is moved from here — it is not a physics surface, so
        // the float is animated. Everywhere else the bobber is a real rigidbody:
        // flying, bouncing, or being towed by FixedUpdate.
        if (!hasHitWater) return;

        // A release has been requested and will happen in the next FixedUpdate
        // (physics-clock birth, stamp BA). Until then the retrieve keeps its
        // glide going (it simply re-requests, harmlessly); a landed fish holds
        // still -- IdleShake would start easing it back toward the ORIGINAL
        // cast seat, which a float pinned at the bank must not do.
        if (_releasePending)
        {
            if (_retrieving) RetrieveStep(Time.deltaTime);
            return;
        }

        if (_retrieving) { RetrieveStep(Time.deltaTime); return; }
        if (isStriking) BiteWander();
        else IdleShake();
    }

    // ── Tow lifecycle ───────────────────────────────────────────────────────

    // The water→shore handoff: the bobber becomes a real physics object at
    // the bank and is dragged up over the ground.
    //
    // Callers (RetrieveStep, TickFight) run from Update. The release itself is
    // DEFERRED to the next FixedUpdate — see ReleaseFromWaterNow for why.
    void ReleaseFromWater()
    {
        _releasePending = true;
    }

    // ── STAMP BA (2026-09-03): PHYSICS-CLOCK BIRTH ──────────────────────────
    // The snap, frame by frame. The parked bobber is a CHILD of the planet, so
    // it renders at the planet's INTERPOLATED transform — Unity draws every
    // interpolated rigidbody between its last two physics poses, i.e. up to
    // one step (0.02 s) behind rb.position. On a rail-planet that is up to
    // vel x 0.02 s along the orbit. The old release created the rigidbody at
    // transform.position (that rendered pose) from Update, so the body was
    // BORN that far behind the ground it floated over; its own interpolation
    // then rendered it at that offset from the next frame on. The jump onto
    // the offset was the snap (stamp AD measured it: dLocal 0.73-0.81 m).
    //
    // Two things fix it, both physics-side, NO transform writes afterwards:
    //  1. Birth happens in FixedUpdate, before that step's simulate, so the
    //     newborn body has a real (prev, curr) pose pair by the time the frame
    //     renders — no frame renders a raw or stale pose.
    //  2. The birth pose is the same PLANET-LOCAL point, re-expressed on the
    //     planet's RIGIDBODY pose (physics clock) instead of its transform
    //     (render clock). Planet-local is clock-invariant; the conversion is
    //     exact arithmetic, and the seed velocity carries the planet's exact
    //     rail sweep (CelestialBody.velocity) plus the glide that was being
    //     rendered. The body then renders — through Unity's own interpolation,
    //     the same one the ground uses — exactly where the park rendered it.
    //
    // Why stamp AD's identical conversion "failed": it shipped bundled with a
    // hold that WROTE THE TRANSFORM every frame. autoSyncTransforms=false does
    // not make transform writes cosmetic — Unity syncs them into the body at
    // the next physics step (docs: "prior to the physics simulation step").
    // Every draw-side smoother in the war was therefore a per-step physics
    // teleport; that, not the seed, was the runaway. This stamp writes the
    // transform ONCE, before the Rigidbody exists, and never again.
    void ReleaseFromWaterNow()
    {

        if (fishingCoroutine != null) { StopCoroutine(fishingCoroutine); fishingCoroutine = null; }
        isFishingActive = false;
        isStriking = false;
        pendingSpecies = -1;
        currentFishType = "";
        _biteOddsBonus = 1f;
        if (biteSource != null && biteSource.isPlaying) biteSource.Stop();


        // A fish still mid-approach (not hooked) has no business riding the
        // shore tow -- it loses interest the moment the lure leaves the water.
        if (_fishApproachActive) DespawnHookedFish();

        // The planet we are leaving: its rigidbody is the physics clock, its
        // transform the render clock. Kept for the hang hand-off too.
        CelestialBody planet = planetBody != null ? planetBody.GetComponent<CelestialBody>() : null;
        Rigidbody planetRb = planet != null ? planet.Rigidbody : null;
        Transform planetTf = planetBody;
        _towPlanet = planetRb != null ? planet : null;

        transform.SetParent(null, true);
        planetBody = null;
        hasHitWater = false;
        // Physics-state churn can re-fire OnTriggerEnter for the water volume
        // the bobber is still inside; without this cooldown the release at the
        // waterline re-parked itself in the same frame. A full second, per Sam,
        // so it can be pulled clear of the water before the trigger re-arms.
        _waterIgnoreUntil = Time.time + 1f;

        // PLANET-FRAME velocity, not zero. Zero WORLD velocity on a planet that
        // is riding its sun-orbit rail meant the ground swept into the bobber at
        // orbital speed the instant it was released -- burying it in the bank
        // ("glitches through the land") at some longitudes and flinging it away
        // at others, rotating with the orbit exactly as Sam observed.
        // The frame: the planet's EXACT per-step rail sweep when we have it
        // (set by NBodySimulation at order -10, i.e. before this FixedUpdate).
        // The tip's finite-differenced velocity is only the fallback: sampled
        // per physics step from a per-frame-rendered tip, it jitters by the
        // frame/step phase and would seed the body up to metres per second
        // off the ground on a fast rail.
        Vector3 frameVel = planetRb != null ? planet.velocity
            : _tipVelInit ? _tipVel
            : rodOwner != null ? rodOwner.OwnerVelocity : Vector3.zero;
        Vector3 seed = frameVel;
        if (_renderVelInit)
        {
            // Keep the glide the player was watching, capped relative to the
            // frame so a thrash spike can never launch the release.
            Vector3 rel = _renderVel - frameVel;
            if (rel.sqrMagnitude > 64f) rel = rel.normalized * 8f;
            seed = frameVel + rel;
        }
        _renderVelInit = false;

        // Birth pose on the PHYSICS clock: the rendered planet-local point,
        // re-expressed on the planet's rigidbody pose. Written to the transform
        // BEFORE the Rigidbody exists, so the body is simply created there —
        // no teleport, no interpolation reset, and the only transform write
        // this release ever makes.
        if (planetRb != null)
        {
            Vector3 rendered = transform.position;
            Vector3 local = Quaternion.Inverse(planetTf.rotation) * (rendered - planetTf.position);
            Vector3 phys = planetRb.position + planetRb.rotation * local;
            Vector3 lifted = ResolveBankOverlap(phys, planet, planetRb, planetTf);
            Debug.Log($"[Bobber] Shore release: clock offset {(phys - rendered).magnitude:F2} m, "
                    + $"overlap shift {(lifted - phys).magnitude:F3} m (code {BuildStamp}).");
            transform.position = lifted;
        }

        AttachPhysics(seed);
        _towLine = -1f;
    }

    /// <summary>
    /// The park seats the float at the ocean radius, and the shore tests fire
    /// on a 0.45 m touch radius while the real collider is 0.1 m — so the
    /// birth pose normally does NOT overlap the bank. On a very shallow shelf
    /// it can, and PhysX would then eject the newborn at up to 10 m/s, which
    /// the tow's velocity re-basing keeps alive as a hop. Resolve any overlap
    /// deterministically instead: minimal translation out of the terrain,
    /// computed against the terrain collider AT ITS PHYSICS POSE (the same
    /// clock as the birth pose), capped so a bad answer can only nudge.
    /// </summary>
    Vector3 ResolveBankOverlap(Vector3 phys, CelestialBody planet, Rigidbody planetRb, Transform planetTf)
    {
        var sphere = GetComponent<SphereCollider>();
        int mask = GroundMask();
        if (sphere == null || mask == 0) return phys;

        Vector3 s = transform.lossyScale;
        float r = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        var hits = Physics.OverlapSphere(phys, r, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return phys;

        Vector3 shift = Vector3.zero;
        Quaternion tfToRb = planetRb.rotation * Quaternion.Inverse(planetTf.rotation);
        foreach (var h in hits)
        {
            if (h == null || h.transform.IsChildOf(transform)) continue;
            // The hit collider's pose on the physics clock: a child of the
            // planet renders on the transform clock, so re-base it exactly
            // like the birth pose. Anything else is static and needs nothing.
            Vector3 hp = h.transform.position;
            Quaternion hr = h.transform.rotation;
            if (h.transform.IsChildOf(planet.transform))
            {
                hp = planetRb.position + tfToRb * (hp - planetTf.position);
                hr = tfToRb * hr;
            }
            if (Physics.ComputePenetration(sphere, phys + shift, transform.rotation, h, hp, hr,
                                           out Vector3 dir, out float dist))
                shift += dir * (dist + 0.005f);
        }
        if (shift.sqrMagnitude > 0.3f * 0.3f) shift = shift.normalized * 0.3f;
        return phys + shift;
    }

    /// <summary>
    /// (Re)attach a fresh Rigidbody and GravityObjectSimple, configured exactly
    /// the way a cast configures them. Both must be FRESH components:
    /// GravityObjectSimple caches its Rigidbody once in Start, so re-enabling an
    /// old one would push on a destroyed reference.
    /// </summary>
    void AttachPhysics(Vector3 startVelocity)
    {
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // Interpolate, like the planets themselves -- the trace proved the "fix"
        // of stepping it raw broke lockstep with the smoothly-rendered ground.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Depenetration stays at full default speed ON PURPOSE: a bobber that
        // ever ends up inside the bank must escape in one step (capping this is
        // what kept it buried and dragged under the terrain in the build). The
        // shore birth pose is overlap-resolved beforehand, so this rarely fires.
        rb.velocity = startVelocity;

        if (GetComponent<GravityObjectSimple>() == null)
            gameObject.AddComponent<GravityObjectSimple>();

        // Re-assert the player-collision ignore pairs. IgnoreCollision state is
        // silently CLEARED when a collider is disabled, and SetupAttached
        // disables every bobber collider for the equip glue -- so the pairs set
        // at spawn are gone by the first throw without this.
        if (rodOwner != null) rodOwner.IgnorePlayerCollisions(gameObject);

        var em = FindObjectOfType<EndlessManager>();
        if (em != null) em.RegisterPhysicsObject(transform);
    }

    /// <summary>
    /// Wound all the way home: the bobber HANGS a foot below the tip, still a
    /// real physics object on its line — Sam's design. It swings as you move
    /// because it is simply the tow constraint with a fixed line length, which
    /// is the mechanism already proven in play: frame-relative velocity, damped,
    /// velocity-level only. (The two earlier hang attempts died precisely for
    /// lacking the damping and the moving-planet frame.)
    /// </summary>
    void BeginHang()
    {
        _retrieving = false;
        _hanging = true;
        hasHitWater = false;
        _hangSimInit = false;

        // Single writer: no rigidbody while hanging. Kinematic-first before the
        // deferred Destroy, as everywhere else.
        var hangGrav = GetComponent<GravityObjectSimple>();
        if (hangGrav != null) Destroy(hangGrav);
        Vector3 carryVel = Vector3.zero;
        _hangSeedValid = false;
        if (rb != null)
        {
            carryVel = rb.velocity - (_tipVelInit ? _tipVel : Vector3.zero);
            if (_towPlanet != null && _towPlanet.Rigidbody != null)
            {
                // Stamp BA, the hang seam: this runs in FixedUpdate, and the
                // body goes kinematic before the simulate, so this frame
                // renders its RAW physics pose — up to a step behind the
                // rendered ground. HangSimStep used to seed its offset from
                // that pose (the second spike on the mega tape). Capture the
                // pose planet-locally on the physics clock instead; the hang's
                // first frame re-expresses it on the planet's rendered pose.
                // Carry velocity relative to the planet's exact sweep, capped,
                // rather than the phase-jittery tip estimate.
                var prb = _towPlanet.Rigidbody;
                _hangSeedLocal = Quaternion.Inverse(prb.rotation) * (rb.position - prb.position);
                _hangSeedValid = true;
                carryVel = rb.velocity - _towPlanet.velocity;
                if (carryVel.sqrMagnitude > 64f) carryVel = carryVel.normalized * 8f;
            }
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            Destroy(rb);
            rb = null;
        }
        _hangOffVel = carryVel;   // tip-relative swing carries over from the tow
        // The leash starts at the arrival distance and reels down to rest --
        // the smooth take-up rather than a first-frame yank.
        _towLine = rodOwner != null
            ? Mathf.Max(hangLeash, (transform.position - rodOwner.LineOriginWorld).magnitude)
            : hangLeash;
        Debug.Log($"[Bobber] Wound in - hanging off the rod tip (leash={hangLeash:F2}m, code {BuildStamp}).");
    }

    /// <summary>
    /// A freshly equipped rod's bobber: glued to the tip from the very first
    /// frame, riding the equip animation as a prop. The prefab carries a
    /// Rigidbody, so it is made kinematic IMMEDIATELY (Destroy is deferred a
    /// frame, and one frame of live physics on the tip is exactly how the very
    /// first resting bobber flew away) and then removed.
    /// </summary>
    public void SetupAttached(FishingRodController rod)
    {
        rodOwner = rod;
        _gluedToTip = true;

        // A prop on the rod tip must not collide with anything, and must be
        // invisible to raycasts: the player's ground probes finding a collider
        // at chest height on equip is what nudged the player into the ground.
        // AttachPhysics re-enables every collider at the throw.
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;  // before kinematic
            rb.isKinematic = true;
            var grav = GetComponent<GravityObjectSimple>();
            if (grav != null) Destroy(grav);
            Destroy(rb);
            rb = null;
        }
    }

    /// <summary>
    /// The equip animation has finished: the bobber gets its physics and drops
    /// off the tip onto its length of line.
    /// </summary>
    public void DropToHang()
    {
        if (!_gluedToTip) return;
        _gluedToTip = false;
        _hanging = true;
        // The line PAYS OUT rather than appearing: the leash starts at nothing
        // and grows to hangLeash at hangPayOutSpeed, so the bobber visibly
        // lowers off the tip on its line instead of instantly having the full
        // foot of line and dropping in a blink. Sam, 2026-09-02: "when the
        // animation settles the bobber drops down and hangs from the line --
        // this should look better than just it appearing out of nowhere."
        _towLine = 0.02f;
        hasHitWater = false;
        _hangSimInit = false;
        _hangSeedValid = false;
        _hangOffVel = Vector3.zero;   // starts at the tip; the payout lowers it
        Debug.Log($"[Bobber] Dropped to hang - paying out to {hangLeash:F2}m (code {BuildStamp}).");
    }

    /// <summary>Hanging on its foot of line, ready to cast.</summary>
    public bool IsHanging => _hanging;

    /// <summary>Being pulled to / held at the tip for the throw.</summary>
    public bool IsReadyForLaunch => _windingToTip || _gluedToTip;

    /// <summary>
    /// The cast clicked: over the rod's pull-back, the foot of line retracts and
    /// the bobber stops being a physics object, ending stuck to the tip so the
    /// fling launches it from the rod. Physics teardown is the PROVEN water-park
    /// pattern (destroy rb + grav); the relaunch that follows is the proven
    /// AttachPhysics pattern. Nothing new happens here mechanically — only the
    /// choreography is new.
    /// </summary>
    public void WindToTip()
    {
        if (!_hanging) return;
        _hanging = false;
        _windingToTip = true;
        _towLine = -1f;

        _windT = 0f;
        _windOffset = rodOwner != null
            ? transform.position - rodOwner.LineOriginWorld
            : Vector3.zero;
        var em = FindObjectOfType<EndlessManager>();
        if (em != null) em.UnregisterPhysicsObject(transform);
        var grav = GetComponent<GravityObjectSimple>();
        if (grav != null) Destroy(grav);
        if (rb != null)
        {
            // Kinematic IMMEDIATELY: Destroy is deferred a frame, and one frame
            // of a live dynamic body fighting the wind's transform writes is the
            // exact class of glitch this feature keeps re-learning.
            rb.velocity = Vector3.zero;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            Destroy(rb);
            rb = null;
        }
    }

    /// <summary>
    /// The retracting foot of line, tracking the moving tip.
    ///
    /// A TIMED lerp, not a speed chase — Sam foresaw this failure exactly. The
    /// pull-back swings the rod ~50 degrees in 0.15 s, which sweeps the tip at
    /// 8+ m/s; the old fixed-speed MoveTowards (4.5 m/s) could never catch it,
    /// so at the release the bobber launched from wherever it was mid-chase —
    /// sometimes inside the ground. Lerping by normalised TIME converges by
    /// construction: at t = 1 the position IS the tip, whatever the tip did,
    /// and from then on it is glued and rides the whole fling.
    /// </summary>
    void LateUpdate()
    {
        if (rodOwner == null) return;

        if (hasHitWater)
        {
            float rdt = Time.deltaTime;
            Vector3 rpos = transform.position;
            if (_renderVelInit && rdt > 0f)
            {
                Vector3 rd = rpos - _prevRenderPos;
                if (rd.sqrMagnitude < 25f) _renderVel = rd / rdt;   // >5 m = teleport
            }
            _prevRenderPos = rpos;
            _renderVelInit = true;

        }
        if (_windingToTip)
        {
            // The bobber rides the tip EXACTLY from the first frame of the
            // wind, with its captured offset smoothly shrinking to zero. The
            // earlier version lerped the position toward the moving tip, which
            // compounds into an erratic curve while the rod swings — Sam saw the
            // bobber "move sporadically" on the click. In the tip's own frame
            // the motion is a clean straight shrink, whatever the rod is doing.
            _windT += Time.deltaTime / WindToTipSeconds;
            float k = Mathf.Clamp01(_windT);
            k = k * k * (3f - 2f * k);                     // smoothstep
            transform.position = rodOwner.LineOriginWorld + _windOffset * (1f - k);
            if (_windT >= 1f)
            {
                _windingToTip = false;
                _gluedToTip = true;
            }
        }
        else if (_gluedToTip)
        {
            transform.position = rodOwner.LineOriginWorld;
        }
        else if (_hanging)
        {
            HangSimStep(Time.deltaTime);
        }

        // The landed catch (and the fish holding the float through the strike
        // window), posed in WORLD space every frame. The parent link is
        // lifecycle only -- left to the parent transform the fish spins with
        // the rolling bobber. Gated off while an approach/retreat coroutine
        // owns the fish's pose: one fish, one writer.
        if (_hookedFish != null && fight == null
            && (isStriking || !_fishApproachActive)) PlaceHookedFish(1f);

    }

    /// <summary>
    /// The hang, simulated in OFFSET space: the state is the vector from the
    /// tip to the bobber, and the render is always <c>tip + offset</c>.
    ///
    /// This is the root fix for the WASD jitter, not a filter. The tip itself
    /// carries fixed-step position noise when the player walks (movement is
    /// physics-stepped; mouse-look is per-frame, which is why the mouse was
    /// always smooth). Any sim that chases the tip through world space converts
    /// that noise into RELATIVE motion -- visible jitter on a taut line.
    /// Rendering tip + offset moves the bobber WITH every tremor of the tip, so
    /// the relative pose is noise-free by construction -- the same reason the
    /// wind-to-tip animation (offset shrink) already looks clean.
    ///
    /// The rope is HARD, the gravity is real, and walking still swings it: the
    /// tip's (smoothed) acceleration is fed back as inertia, the same force a
    /// pendulum bob feels when its pivot accelerates.
    /// </summary>
    void HangSimStep(float dt)
    {
        if (dt <= 0f) return;
        Vector3 tip = rodOwner.LineOriginWorld;

        // Tip inertia: differentiate a lightly smoothed tip velocity. The
        // smoothing shapes only the swing FORCE -- the render anchor is the raw
        // tip, so no lag is ever visible.
        Vector3 tipVelNow = _hangTipVel;
        if (_hangSimInit)
        {
            Vector3 dTip = tip - _prevTipRender;
            if (dTip.sqrMagnitude < 25f) tipVelNow = dTip / dt;   // >5 m = teleport
        }
        _prevTipRender = tip;
        Vector3 tipVelSmoothed = Vector3.Lerp(_hangTipVel, tipVelNow, 1f - Mathf.Exp(-10f * dt));
        Vector3 tipAccel = (tipVelSmoothed - _hangTipVel) / dt;
        if (tipAccel.sqrMagnitude > 900f) tipAccel = tipAccel.normalized * 30f;
        _hangTipVel = tipVelSmoothed;

        if (!_hangSimInit)
        {
            _hangSimInit = true;
            Vector3 seedPos = transform.position;
            if (_hangSeedValid && _towPlanet != null)
            {
                // Render-clock version of the pose captured in BeginHang: the
                // planet's transform is already this frame's interpolated
                // pose, so planet-local + transform == where the ground-glued
                // bobber is drawn this frame. (Stamp BA.)
                Transform pt = _towPlanet.transform;
                seedPos = pt.position + pt.rotation * _hangSeedLocal;
            }
            _hangSeedValid = false;
            _hangOffset = seedPos - tip;
            tipAccel = Vector3.zero;
        }

        // The leash converges on its rest length from EITHER side: reeled in
        // from wherever the hang began (the smooth take-up rather than a
        // first-frame yank), or PAID OUT from the tip after an equip so the
        // bobber visibly lowers on its line instead of appearing on it.
        if (_towLine < hangLeash)
            _towLine = Mathf.Min(hangLeash, _towLine + hangPayOutSpeed * dt);
        else
            _towLine = Mathf.Max(hangLeash, _towLine - retrieveSpeed * dt);

        // A caught fish books the moment the take-up reaches the resting foot
        // of line -- the lure, fish and all, is back at the rod. This is the
        // lift-off path's version of "reeled back up before it counts".
        if (_pendingLanding && _towLine <= hangLeash + 0.05f)
        {
            _pendingLanding = false;
            LandFish();
        }

        // Spring-damper straight to the rest point: a full leash-length below
        // the tip, along the player's own up (gravity-aligned by definition).
        // The rest pose is GUARANTEED by construction -- it no longer depends on
        // integrating a sampled gravity field, which is what was quietly leaving
        // the bobber bunched at the tip no matter what the leash said. Slightly
        // under-damped, so it sways naturally and the tip-inertia kick still
        // swings it when you move.
        Vector3 restUp = planetBody != null
            ? (tip - planetBody.position).normalized
            : rodOwner.transform.up;
        // Where the spring pulls. At rest length that is the dangle point, a
        // leash-length BELOW the tip -- but during a long take-up (the shore
        // lift-off can start metres out) that point is UNDERGROUND, and a
        // spring diving the bobber at the bank is the exact artifact this
        // path exists to kill. So while the line is long, the rest point lies
        // ALONG the current bearing at the allowed length: the shortening
        // rope winches the bobber straight toward the tip, and the dangle
        // target blends in only as the line approaches its resting foot.
        Vector3 restDown = -restUp * _towLine;
        Vector3 offsetDir = _hangOffset.sqrMagnitude > 0.0001f
            ? _hangOffset.normalized : -restUp;
        float takeUpK = Mathf.Clamp01((_towLine - hangLeash)
                                      / Mathf.Max(0.01f, hangLeash * 2f));
        Vector3 rest = Vector3.Lerp(restDown, offsetDir * _towLine, takeUpK);
        _hangOffVel += (rest - _hangOffset) * 45f * dt;
        _hangOffVel -= tipAccel * dt;
        _hangOffVel *= Mathf.Clamp01(1f - 7f * dt);
        if (_hangOffVel.sqrMagnitude > 400f) _hangOffVel = _hangOffVel.normalized * 20f;

        _hangOffset += _hangOffVel * dt;
        float len = _hangOffset.magnitude;
        if (len > _towLine && len > 0.0001f)
        {
            Vector3 dir = _hangOffset / len;
            _hangOffset = dir * _towLine;                     // HARD rope, as before
            float outward = Vector3.Dot(_hangOffVel, dir);
            if (outward > 0f) _hangOffVel -= dir * outward;   // the line does not stretch
        }

        transform.position = tip + _hangOffset;
    }
    /// <summary>The fling: throw the bobber that is stuck to the tip.</summary>
    public void RelaunchFromTip(Vector3 inheritedVelocity, Vector3 direction, float speed,
                                Quaternion rotation)
    {
        if (!_windingToTip && !_gluedToTip) return;
        _windingToTip = false;
        _gluedToTip = false;
        _biteOddsBonus = 1f;
        hasHitEnemy = false;
        _waterIgnoreUntil = 0f;
        _towPlanet = null;            // a fresh cast: the shore planet is stale
        _releasePending = false;

        // Belt and braces: whatever happened during the wind, the throw leaves
        // from the tip. This is the same proven shoot-out as a fresh spawn —
        // position at the tip, physics attached, velocity applied.
        if (rodOwner != null) transform.position = rodOwner.LineOriginWorld;
        transform.rotation = rotation;
        AttachPhysics(inheritedVelocity + direction * speed);
        Debug.Log("Bobber released.");
    }

    /// <summary>
    /// The tow: reeling a FREE bobber (never a floating one — the water slide
    /// handles that). Runs on the real rigidbody, so the bobber is dragged over
    /// every bump and lip of the terrain by its collisions, exactly as Sam
    /// described: "apply thrust back towards the player and it will naturally
    /// navigate the curves of the ground because it's a physics object."
    ///
    /// The pull is a VELOCITY-LEVEL line constraint — remove outward velocity,
    /// add a bounded inward pull — plus damping. It never writes a position, so
    /// PhysX depenetration cannot be compounded into a launch, and the damping
    /// is what the first tether fatally lacked: without it every nudge of
    /// tangential velocity was conserved forever and the bobber wound itself
    /// into a permanent orbit ("rapidly spinning around the tip").
    /// </summary>
    void FixedUpdate()
    {
        // Track the tip's velocity continuously (cheap), whatever state we are
        // in, so any state that needs the frame has a fresh estimate.
        if (rodOwner != null)
        {
            Vector3 tipNow = rodOwner.LineOriginWorld;
            if (_tipVelInit)
            {
                Vector3 dTip = tipNow - _prevTipPos;
                // A jump of metres in one step is a teleport or an origin shift,
                // not motion — keep the previous estimate rather than spiking.
                if (dTip.sqrMagnitude < 25f)
                    _tipVel = dTip / Time.fixedDeltaTime;
            }
            _prevTipPos = tipNow;
            _tipVelInit = true;
        }

        // The deferred shore release (stamp BA): performed here, before this
        // step's simulate, so the newborn body is interpolated from its very
        // first rendered frame. Skipped if the water state was torn down in
        // between (nothing else can clear the flag).
        if (_releasePending)
        {
            _releasePending = false;
            if (hasHitWater) ReleaseFromWaterNow();
        }

        if (hasHitWater || fight != null || rb == null || rodOwner == null) return;

        bool towing = _retrieving || _pendingLanding;
        if (!towing) { _towLine = -1f; return; }
        if (_retrieveTaut < FishFightSim.TautThreshold) return;   // line comes tight first

        float dt = Time.fixedDeltaTime;
        Vector3 tip = rodOwner.LineOriginWorld;
        Vector3 d = rb.position - tip;
        float dist = d.magnitude;

        // Wound all the way in: the fish (if any) is booked, and the bobber
        // hangs off the tip for the next throw.
        if (dist <= ReelPickupRange)
        {
            if (_pendingLanding) { _pendingLanding = false; LandFish(); }
            BeginHang();
            return;
        }

        // The line: starts at the current distance, only ever shortens.
        if (_towLine < 0f || dist + 0.15f < _towLine) _towLine = dist + 0.15f;
        // A badly snagged line cannot be cranked shorter — the reel skips
        // rather than winching the planet.
        if (dist <= _towLine + 0.6f)
            _towLine = Mathf.Max(0.2f, _towLine - retrieveSpeed * dt);

        // ── ALL velocity maths in the ANGLER'S frame ─────────────────────────
        // The planet rides its orbital rail, so "the ground" is a fast-moving
        // frame. Damping the bobber's WORLD velocity dragged it toward world-
        // zero — a huge velocity relative to the ground whose direction depends
        // on where you stand and rotates with the orbit. That was the "retrieve
        // works here but not halfway around the planet" bug. The player's
        // rigidbody tracks the ground by construction, so it IS the frame.
        Vector3 frameVel = _tipVelInit ? _tipVel : rodOwner.OwnerVelocity;
        Vector3 rel = rb.velocity - frameVel;

        // Damping BEFORE the constraint: kills sideways swing instead of
        // conserving it into an orbit.
        rel *= Mathf.Clamp01(1f - 4f * dt);

        // Sanity rail (kept from the war): nothing in fishing legitimately
        // moves 30 m/s relative to the ground; below that this line changes
        // nothing at all.
        if (rel.sqrMagnitude > 900f) rel = rel.normalized * 30f;

        if (dist > _towLine && dist > 0.0001f)
        {
            Vector3 dir = d / dist;
            float outward = Vector3.Dot(rel, dir);
            if (outward > 0f) rel -= dir * outward;
            float excess = dist - _towLine;
            rel -= dir * Mathf.Min(excess * LeashStiffness, LeashMaxPull);
        }

        rb.velocity = frameVel + rel;
    }

    /// <summary>
    /// Wind the lure back across the water. Sam, 2026-09-01: "instead of just
    /// instantly reeling back in, make it so that the bobber slides across the
    /// water back to you".
    ///
    /// This is not just cosmetic. A worked lure fishes: the bite countdown runs
    /// <see cref="retrieveBiteBonus"/> faster while it is moving, and because the
    /// tier roll reads the CURRENT distance, a strike out in the deep water is a
    /// far better fish than one taken at your feet. So winding in is a real
    /// decision with a real trade-off, instead of a wasted cast.
    /// </summary>
    void RetrieveStep(float dt)
    {
        // Only a TIGHT line moves the lure — the cascade again.
        if (_retrieveTaut < FishFightSim.TautThreshold)
        {
            IdleShake();
            return;
        }

        // Move at the EMPTY-LURE speed, not the fight's. Passing a target of 0
        // let the per-frame clamp (reelSpeed + 4) do the work, which dragged the
        // float in at ~10 m/s -- Sam: "it just moves kinda alien and fast through
        // the water". A float being wound in is slow and heavy.
        float arc = HorizontalDistanceToAngler();
        StepAnchorToward(Mathf.Max(0f, arc - waterRetrieveSpeed * dt), dt);
        AdvanceSwim(0.25f, dt);       // an empty lure barely wanders
        RenderWander(_anchorLocal, 0.25f);

        if (LureHasArrived()) ReleaseFromWater();
    }

    /// <summary>
    /// A fish nosing the bait, before the hook is set. The float gets pushed
    /// around the surface; it does not pump up and down.
    ///
    /// The wander is a bounded OFFSET from where the bobber actually floats — it
    /// is never rebuilt from the player's position. That distinction is the whole
    /// bug history of this file: a stored "cast bearing" went stale the moment
    /// Sam walked along the bank, and reconstructing the position from it flung
    /// the bobber across the water ("the fish swims a crazy distance really fast
    /// in the water and it just looks bugged"). An offset can only ever rotate
    /// by a metre, however wrong its reference direction becomes.
    /// </summary>
    void BiteWander()
    {
        AdvanceSwim(1f, Time.deltaTime);
        RenderWander(_anchorLocal, 1f);
    }

    /// <summary>
    /// Wander the fish sideways and in/out, in METRES. Picked as occasional
    /// targets rather than a sine wave, so it reads as a decision rather than a
    /// machine.
    ///
    /// <paramref name="vigour"/> is how much fight the fish has left, 1 down to
    /// 0. A fresh fish throws the float around; a beaten one barely stirs it —
    /// Sam: "the more tired it gets the less it should be moving the bobber in
    /// the water, showing that the fish is getting tired". That taper IS the
    /// stamina readout; there is no bar for it and there should not be one.
    /// </summary>
    void AdvanceSwim(float vigour, float dt)
    {
        if (Time.time >= _nextSwimPick)
        {
            // A tired fish also changes its mind less often.
            _nextSwimPick = Time.time + Random.Range(0.6f, 1.8f) / Mathf.Max(0.3f, vigour);
            _swimTargetLat = Random.Range(-maxSwimLateral, maxSwimLateral);
            _swimTargetFwd = Random.Range(-maxSwimLateral, maxSwimLateral) * 0.7f;
        }
        float speed = swimLateralSpeed * Mathf.Lerp(0.25f, 1f, vigour) * dt;
        _swimLateral = Mathf.MoveTowards(_swimLateral, _swimTargetLat * vigour, speed);
        _swimForward = Mathf.MoveTowards(_swimForward, _swimTargetFwd * vigour, speed);
        _swimLateral = Mathf.Clamp(_swimLateral, -maxSwimLateral, maxSwimLateral);
        _swimForward = Mathf.Clamp(_swimForward, -maxSwimLateral, maxSwimLateral);
    }

    /// <summary>
    /// Draw the bobber at its anchor plus the wander and the shiver, seated on
    /// the water. Everything added here is COSMETIC and bounded — the anchor is
    /// the only thing the fight's maths ever reads back.
    /// </summary>
    void RenderWander(Vector3 anchorLocal, float vigour)
    {
        if (anchorLocal.sqrMagnitude < 0.0001f || waterRadius <= 0.01f)
        {
            IdleShake();
            return;
        }

        Vector3 up = anchorLocal.normalized;

        // Axes for "toward the angler" and "across". Recomputed from the CURRENT
        // player position every frame, so walking the bank simply re-aims a one
        // metre offset instead of relocating the fish.
        Vector3 fwd = Vector3.forward;
        if (fightPlayer != null && planetBody != null)
        {
            Vector3 playerLocal = planetBody.InverseTransformPoint(fightPlayer.position);
            Vector3 v = Vector3.ProjectOnPlane(anchorLocal - playerLocal, up);
            if (v.sqrMagnitude > 0.0001f) fwd = v.normalized;
        }
        Vector3 side = Vector3.Cross(up, fwd);

        // How deep the float rides right now -- Sam's choreography, 2026-09-02:
        // "bobber hits the water, slowly bobs a bit ON the surface, when
        // there's a bite it gets pulled down into the water a little bit and
        // thrashed, then as you set the hook and pull it back in it stays
        // under the surface." An empty lure being worked stays ON the surface
        // (submerge 0); a nibble pulls it under a little; a hooked fish holds
        // it clearly under. The 22/s follow lerp below is what eases each
        // transition, and the SlippedOff settle deliberately snaps -- a float
        // POPPING back up is exactly what a fish letting go looks like.
        float submerge = fight != null ? fightSubmerge : (isStriking ? biteSubmerge : 0f);

        Vector3 p = anchorLocal + fwd * _swimForward + side * _swimLateral;
        if (p.sqrMagnitude > 0.0001f) p = p.normalized * (waterRadius - submerge);

        // Shiver: small, fast, and biased DOWNWARD — a hooked float is pulled
        // under, not launched. Fades out with the fish.
        float amp = fightJitter * Mathf.Lerp(0.2f, 1f, vigour);
        if (fight != null && fight.IsRunning) amp *= runJitterMultiplier;

        float t = Time.time;
        float lateral  = (Mathf.Sin(t * 13.7f) * 0.6f + Mathf.Sin(t * 27.1f) * 0.4f) * amp;
        float along    = (Mathf.Sin(t * 9.3f)  * 0.5f + Mathf.Sin(t * 19.7f) * 0.5f) * amp;
        float vertical = (Mathf.Sin(t * strikeBobFrequency) * 0.5f - 0.35f)
                       * strikeBobAmplitude * Mathf.Lerp(0.3f, 1f, vigour);

        // ── THE TUG ──────────────────────────────────────────────────────────
        // Sharp downward yanks at irregular intervals: the single clearest way
        // to say "there is something alive on the end of this". Sam wanted the
        // fish-is-on state to be unmistakable, and a float that periodically
        // gets pulled UNDER reads as that instantly -- where a smooth shimmer
        // just reads as water. Its absence is the other half of the message:
        // when the tugs stop, the fish is gone.
        if (fight != null)
        {
            if (Time.time >= _nextTug)
            {
                _nextTug = Time.time + Random.Range(tugIntervalMin, tugIntervalMax)
                                     / Mathf.Max(0.25f, vigour);
                _tugPhase = 1f;
            }
            if (_tugPhase > 0f)
            {
                _tugPhase = Mathf.Max(0f, _tugPhase - Time.deltaTime * 5f);
                // Quick snap under, slower rise back — the shape of a real tug.
                float k = _tugPhase * _tugPhase;
                vertical -= tugDepth * k * Mathf.Lerp(0.35f, 1f, vigour);
                lateral  += Mathf.Sin(_tugPhase * 12f) * tugDepth * 0.4f * k;
            }
        }

        p += side * lateral + fwd * along;
        if (p.sqrMagnitude > 0.0001f) p = p.normalized * (waterRadius - submerge + vertical);

        // Follow rather than snap. The wander axes are derived from the player's
        // CURRENT position, so walking the bank rotates them -- and rotating a
        // one metre offset instantly would read as a small pop. This costs a few
        // centimetres of lag and removes it entirely.
        _parkLocal = Vector3.Lerp(_parkLocal, p, 1f - Mathf.Exp(-22f * Time.deltaTime));
        baseLocalPosition = _parkLocal;
        ApplyParkPose();
    }

    /// <summary>
    /// Move the fight's ANCHOR along the water so its distance from the angler
    /// matches the fight's, then draw the fish around it.
    ///
    /// <b>Incremental and bounded, deliberately.</b> Earlier versions rebuilt the
    /// position from the player's every frame, and every way that construction
    /// could be wrong — a degenerate tangent, a stale world-space cache after a
    /// floating-origin shift, a bearing captured before the player walked 20 m
    /// down the bank — turned into an instant teleport. Now the anchor's own
    /// planet-local position is the authority and each frame's step is clamped,
    /// so a bad input can only ever cause a slow, visible drift.
    /// </summary>
    void DragToFightDistance(bool reeling, float dt)
    {
        if (planetBody == null || fightPlayer == null || waterRadius <= 0.01f || dt <= 0f)
        {
            IdleShake();
            return;
        }

        Vector3 playerLocal = planetBody.InverseTransformPoint(fightPlayer.position);
        if (playerLocal.sqrMagnitude < 0.0001f || _anchorLocal.sqrMagnitude < 0.0001f)
        {
            IdleShake();
            return;
        }

        StepAnchorToward(fight.Distance, dt);

        AdvanceSwim(FightVigour, dt);
        RenderWander(_anchorLocal, FightVigour);
    }

    /// <summary>
    /// Move the anchor along the water until it sits <paramref name="targetArc"/>
    /// metres from the angler. Shared by the fight and the retrieve.
    ///
    /// <b>The clamp is the safety property.</b> No single frame can move the
    /// anchor further than a reel or a fish plausibly could, so nothing upstream
    /// -- a stale reference, a mis-resolved planet, a floating-origin shift --
    /// can ever teleport the bobber. The worst case is a slow, visible drift.
    /// </summary>
    void StepAnchorToward(float targetArc, float dt)
    {
        if (planetBody == null || fightPlayer == null || waterRadius <= 0.01f) return;
        Vector3 playerLocal = planetBody.InverseTransformPoint(fightPlayer.position);
        if (playerLocal.sqrMagnitude < 0.0001f || _anchorLocal.sqrMagnitude < 0.0001f) return;

        Vector3 up = playerLocal.normalized;
        Vector3 anchorDir = _anchorLocal.normalized;

        float separation = Mathf.Deg2Rad * Vector3.Angle(up, anchorDir);
        float currentArc = separation * waterRadius;
        float delta = currentArc - targetArc;

        float maxStep = (FishingTuning.Active.reelSpeed + 4f) * dt;
        delta = Mathf.Clamp(delta, -maxStep, maxStep);

        if (Mathf.Abs(delta) > 0.0001f && separation > 0.0001f)
        {
            float stepRad = Mathf.Abs(delta) / waterRadius;
            Vector3 goal = delta > 0f ? up : -up;
            Vector3 moved = Vector3.RotateTowards(anchorDir, goal, stepRad, 0f);
            if (moved.sqrMagnitude > 0.0001f) _anchorLocal = moved.normalized * waterRadius;
        }
        else _anchorLocal = anchorDir * waterRadius;
    }

    /// The float sitting in the water doing nothing dramatic. Also the safe
    /// fallback whenever the fight cannot be positioned — better a bobber that
    /// sits still than one that teleports.
    void IdleShake()
    {
        // DIP-ONLY: the bob runs from the seat DOWN into the water and back,
        // never above it. Sam, 2026-09-02: "when it moves up it moves and
        // floats out of the water" -- a real float displaces downward; it never
        // levitates clear of the surface.
        float bob = (Mathf.Sin(Time.time * bobFrequency) - 1f) * bobAmplitude;
        Vector3 target = baseLocalPosition + bobUpLocal * bob;
        // BUOYANT, not teleporting: approach the rest pose at a float's rise
        // speed. In normal idling the target is millimetres away, so this
        // tracks exactly -- but after a fight ends with the float pulled
        // under (a snapped line, a spat hook), the ~30 cm back to the surface
        // is covered in half a second of gentle rising. Sam: "the bobber just
        // floats back up to the surface."
        _parkLocal = Vector3.MoveTowards(_parkLocal, target, 0.6f * Time.deltaTime);
        ApplyParkPose();
    }

    /// <summary>
    /// Write the park pose to the transform: planet-local when parented (the
    /// classic park -- rendering rides the planet's own frame, which is what
    /// keeps the float glued to the ocean), world otherwise.
    /// </summary>
    void ApplyParkPose()
    {
        if (planetBody != null) transform.localPosition = _parkLocal;
        else transform.position = _parkLocal;
    }

    static int GroundMask()
    {
        if (_groundMask == -1) _groundMask = LayerMask.GetMask("Body");
        return _groundMask;
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasHitWater || hasHitEnemy) return;
        // On the rod (hanging, winding, glued): the line is holding it. It does
        // not park on a puddle the player wades through and it does not spend
        // itself on an enemy that brushes past — those are flight/float rules.
        if (_hanging || _windingToTip || _gluedToTip) return;

        if (other.CompareTag("Enemy"))
        {
            HitEnemy(other);
            return;
        }

        if (other.CompareTag("Water"))
        {
            // Physics-state churn can re-fire triggers for a volume we are
            // still inside; a bobber freshly released at the waterline must not
            // be instantly re-parked by its own release.
            if (Time.time < _waterIgnoreUntil) return;
            hasHitWater = true;
            StopOnWater(other);
        }
    }

    // Bobber's own colliders aren't triggers, and the enemy's capsule isn't a
    // trigger either, so enemy hits go through the physical-collision path.
    void OnCollisionEnter(Collision collision)
    {
        if (hasHitWater || hasHitEnemy) return;

        if (collision.collider.CompareTag("Enemy"))
            HitEnemy(collision.collider);
    }

    // The bobber prefab has three non-trigger sub-colliders, so a single visual
    // hit on an enemy can fire OnCollisionEnter up to three times in the same
    // frame before Destroy takes effect. The flag guarantees one damage per cast.
    void HitEnemy(Collider enemyCollider)
    {
        hasHitEnemy = true;
        var enemy = enemyCollider.GetComponentInParent<EnemyController>();
        if (enemy != null) enemy.TakeBobberDamage();
        Destroy(gameObject);
    }

    void StopOnWater(Collider waterCollider)
    {
        Debug.Log("[Bobber] Hit water. Stopping and setting up...");

        if (waterSplashClip != null && audioSource != null)
        {
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(waterSplashClip, waterSplashVolume);
        }

        CelestialBody planet = waterCollider.GetComponentInParent<CelestialBody>();
        if (planet == null)
        {
            CelestialBody[] bodies = FindObjectsOfType<CelestialBody>();
            float nearestDist = Mathf.Infinity;
            foreach (CelestialBody body in bodies)
            {
                float dist = Vector3.Distance(transform.position, body.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    planet = body;
                }
            }
        }

        // THE CLASSIC PARK, restored on Sam's order (stamp AA, 2026-09-02):
        // "just revert the bobber casting back to how it was before you
        // introduced the snap when casting." On water the bobber stops being a
        // physics object at all -- destroy the Rigidbody and gravity, parent
        // to the planet, animate the transform. Parented, it renders in the
        // planet's own frame, which is why this park sat glued to the ocean
        // for weeks. The kinematic MovePosition park (stamps Z-Z3) rendered a
        // constant ~vel x lag off the planet's time-base -- the "hits the
        // surface then snaps a foot under" -- and is dead. Do not rebuild it.
        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null) em.UnregisterPhysicsObject(transform);

        var grav = GetComponent<GravityObjectSimple>();
        if (grav != null) Destroy(grav);
        if (rb != null)
        {
            Destroy(rb);
            rb = null;
        }

        if (planet != null)
            transform.SetParent(planet.transform, true);
        planetBody = planet != null ? planet.transform : null;
        FishingTuning.Use(tuning);

        // Cached once, here, so the bite roll can ask how far this cast went
        // without a per-frame lookup. Sam, 2026-09-01: a long cast should pull
        // rarer and bigger fish than a lob at your feet.
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null) fightPlayer = pc.transform;

        // The park pose: planet-local when parented (the classic park), world
        // when there is no planet (off-world water, e.g. the poolrooms).
        _parkLocal = planetBody != null ? transform.localPosition : transform.position;

        // Bob along the SURFACE NORMAL, not the planet's local +Y: measured
        // from the planet's centre, the outward direction at the bobber is just
        // its local position normalised. Vector3.up made the bobber bob
        // sideways through the water everywhere but the planet's north pole.
        bobUpLocal = Vector3.up;
        if (planet != null && _parkLocal.sqrMagnitude > 0.0001f)
            bobUpLocal = _parkLocal.normalized;

        // ── SEAT ON THE VISIBLE OCEAN, NOT ON THE TRIGGER (stamp Z3) ─────────
        // The root of "hits the surface then snaps under": the park has always
        // seated the bobber at the TRIGGER-HIT radius, and the Water trigger is
        // scene data that drifts from the rendered ocean -- WaterlineAlign
        // exists for exactly that and tolerates up to 0.5 m of drift as "true
        // to the water". So the seat could sit half a metre under the visible
        // sea, and the old +/-15 cm bob (a prefab pin) was what periodically
        // lifted it into view -- Sam's "bobs too high out of the water" and
        // his "snaps under the surface" were the SAME seat, with and without
        // the mask. The rendered ocean is the source of truth: same read-only
        // GetOceanRadius call the spawners and WaterlineAlign use, with the
        // hit radius as the fallback for baked/generator-less planets.
        float hitRadius = planet != null ? _parkLocal.magnitude : 0f;
        float oceanR = 0f;
        if (planet != null)
        {
            var oceanGen = planet.GetComponentInChildren<CelestialBodyGenerator>();
            if (oceanGen != null) { try { oceanR = oceanGen.GetOceanRadius(); } catch { oceanR = 0f; } }
        }
        if (oceanR > 0.01f && _parkLocal.sqrMagnitude > 0.0001f)
            _parkLocal = bobUpLocal * oceanR;
        waterRadius = oceanR > 0.01f ? oceanR : hitRadius;

        // Seat: centre 1 cm UNDER the visible waterline, so the float rides IN
        // the water with just its top standing proud. Sam's second pass
        // (2026-09-02): at +1.5 cm it still read "kinda on top rather than in
        // it". The dip-only bob takes it a touch deeper from here, never up.
        _parkLocal -= bobUpLocal * 0.01f;
        baseLocalPosition = _parkLocal;
        _anchorLocal = _parkLocal;
        ApplyParkPose();

        // DO NOT re-register with EndlessManager here. The bobber is a CHILD of
        // the CelestialBody, which is itself registered and shifted -- so the
        // parent's shift already carries it. Registering it too DOUBLE-SHIFTS
        // it (one-frame jumps on every origin shift; hit three times in this
        // project: the concert crowd, the menu-orbit player, and this bobber).
        // The Start() registration is still right for FLIGHT, and StopOnWater
        // unregisters above, which is where it stops needing it.

        // One glance in the Player.log settles whether the trigger-vs-ocean
        // drift was the culprit on this planet: if these two radii differ, the
        // old code was parking that far under (or over) the visible sea.
        Debug.Log($"[Bobber] Parked: trigger hit r={hitRadius:F1}, visible ocean r="
                + (oceanR > 0.01f ? oceanR.ToString("F1") : "n/a")
                + $", seated on waterRadius={waterRadius:F1} ({BuildStamp}).");
        StartFishing();
    }

    public void StartFishing()
    {
        if (!hasHitWater)
        {
            Debug.LogWarning("[Bobber] StartFishing called but not in water!");
            return;
        }
        if (isFishingActive)
        {
            Debug.Log("[Bobber] Fishing already active.");
            return;
        }
        isFishingActive = true;
        fishingCoroutine = StartCoroutine(FishingRoutine());
        Debug.Log("[Bobber] Fishing coroutine started.");
    }

    /// <summary>
    /// Sun elevation over the water right here, right now. Re-read every time a
    /// new bite timer starts (not just on the cast) so a player who sits still
    /// while the terminator sweeps over them feels the band change. [BUILD] 2.
    /// </summary>
    float CurrentSunDot() => FishingSun.SunDot(transform.position, planetBody);

    /// <summary>
    /// A fish came and went on this cast. Real fishing: water that just produced
    /// a bite is still good water, so the rest of THIS cast fishes better. Not
    /// cumulative -- one near-miss is enough to mark the spot. Cleared only by
    /// winding in and casting again, because the bobber is destroyed on a recast.
    /// </summary>
    void NoteLostBite()
    {
        if (_biteOddsBonus < lostBiteBonus) _biteOddsBonus = lostBiteBonus;
    }

    /// <summary>
    /// How far the bobber is from the angler ALONG THE WATER, ignoring height.
    ///
    /// Height has to be ignored or fishing from anywhere elevated breaks: Sam
    /// pointed out you might be "on a mountain fishing off a cliff, 20 m above
    /// the water", where a straight-line distance never falls below 20 no matter
    /// how far in you reel. Measuring the arc along the water surface is the only
    /// version that means the same thing from a beach and from a clifftop.
    /// </summary>
    public float HorizontalDistanceToAngler()
    {
        if (fightPlayer == null || planetBody == null || waterRadius <= 0.01f)
            return FishingRules.ShortCast;

        Vector3 playerLocal = planetBody.InverseTransformPoint(fightPlayer.position);
        // ALWAYS the anchor, never the rendered position.
        //
        // This used to fall back to the transform whenever there was no fight on,
        // which meant the RETRIEVE measured its distance from the rendered
        // bobber (jittered, and lagging behind the anchor by the render lerp)
        // while StepAnchorToward moved the ANCHOR. Feeding one loop from two
        // different notions of "where the bobber is" made the retrieve fight
        // itself: it would wind in a little and then stall short of the bank,
        // exactly as Sam reported. One position, one source of truth.
        Vector3 fishLocal = _anchorLocal.sqrMagnitude > 0.0001f
            ? _anchorLocal : _parkLocal;
        if (playerLocal.sqrMagnitude < 0.0001f || fishLocal.sqrMagnitude < 0.0001f)
            return FishingRules.ShortCast;

        float sep = Mathf.Deg2Rad * Vector3.Angle(playerLocal.normalized, fishLocal.normalized);
        return sep * waterRadius;
    }

    float CastDistance() => HorizontalDistanceToAngler();

    /// <summary>
    /// Has the fish reached solid ground?
    ///
    /// Sam's rule, and it is the right one: "as soon as the bobber touches the
    /// terrain mesh ground it counts as a catch, so as soon as you get the fish
    /// to the shore it counts". It works from a beach, from a jetty and from the
    /// top of a cliff, where no distance threshold can. The distance rule stays
    /// as a backstop for water with no shore in reach.
    /// </summary>
    /// <summary>
    /// Has whatever is on the line reached the angler?
    ///
    /// <b>Two ways, and BOTH are needed.</b> Distance alone cannot finish a
    /// retrieve: the bobber is confined to the water sphere while the angler
    /// stands up the bank, so if they are five metres inland the lure reaches
    /// the shoreline and then simply stops, still "too far away" forever. That
    /// is exactly what Sam hit -- "it reels in fine and then stops, like it
    /// doesn't wanna come any closer to the bank". The shore test is what
    /// actually ends it, and it is also the only thing that works from a
    /// clifftop. The fight had this; the retrieve did not. Now there is one
    /// test and both use it.
    /// </summary>
    bool LureHasArrived()
    {
        if (HorizontalDistanceToAngler()
            <= FishingTuning.Active.landDistance + tipReelStartDistance) return true;
        return AtWaterline() || TouchingShore();
    }

    /// <summary>
    /// Is the water about to run out just ahead of the bobber?
    ///
    /// The water is an analytic SPHERE, so it carries on happily underneath the
    /// bank -- and a bobber told only to "get closer to the angler" will follow
    /// it straight into the hillside and then pop out. Sam: "it doesn't keep
    /// dragging along the water line which goes under the ground and makes the
    /// bobber go through the ground then pop up, which looks bad."
    ///
    /// So rather than waiting until the bobber is IN the terrain, this looks a
    /// short way ahead along its path and asks whether that piece of water is
    /// still water. The moment it isn't, the bobber leaves the surface -- about
    /// a foot short of the waterline, which is exactly where a real one lifts.
    /// </summary>
    bool AtWaterline()
    {
        if (planetBody == null || fightPlayer == null || waterRadius <= 0.01f) return false;
        int mask = GroundMask();
        if (mask == 0) return false;

        Vector3 playerLocal = planetBody.InverseTransformPoint(fightPlayer.position);
        if (playerLocal.sqrMagnitude < 0.0001f || _anchorLocal.sqrMagnitude < 0.0001f) return false;

        Vector3 ahead = Vector3.RotateTowards(_anchorLocal.normalized, playerLocal.normalized,
                                              shoreProbeAhead / waterRadius, 0f);
        if (ahead.sqrMagnitude < 0.0001f) return false;
        Vector3 probe = planetBody.TransformPoint(ahead.normalized * waterRadius);
        return Physics.CheckSphere(probe, shoreProbeRadius, mask, QueryTriggerInteraction.Ignore);
    }

    bool TouchingShore()
    {
        int mask = GroundMask();
        if (mask == 0) return false;
        return Physics.CheckSphere(transform.position, shoreTouchRadius,
                                   mask, QueryTriggerInteraction.Ignore);
    }

    IEnumerator FishingRoutine()
    {
        Debug.Log("[Bobber] FishingRoutine entered.");
        var tune = FishingTuning.Active;

        while (true)
        {
            // Wait for a bite, paced by the sun angle AND the bait on the hook.
            // Bait is read here rather than at the cast so swapping bait mid-sit
            // takes effect on the next bite -- the player is never punished for
            // buying better bait after casting.
            float dot = CurrentSunDot();
            BaitKind waitingBait = FishingBait.BestHeld();
            float mult = FishingRules.WaitMultiplier(dot)
                       * FishingRules.BaitWaitMultiplier(waitingBait);
            float waitTime = Random.Range(tune.baseWaitMin, tune.baseWaitMax) * mult;
            // Counted down rather than waited out in one go, so working the lure
            // partway through a wait still pays -- see the loop below.
            float remaining = waitTime;
            Debug.Log($"[Bobber] Waiting {waitTime:F1}s for a bite ({FishingSun.BandName(dot)}, dot {dot:F2}, bait {waitingBait}, x{mult:F2}).");
            while (remaining > 0f)
            {
                // A lure being worked across the water draws fish: the countdown
                // runs faster while retrieving. The pay-off is asymmetric on
                // purpose -- the tier roll below reads the CURRENT distance, so a
                // strike taken out deep is a far better fish than one taken as
                // the lure reaches your feet.
                // The two bonuses STACK: working the lure, on water that has
                // already produced a bite, is the best fishing there is
                // (1.2 x 1.3 = 1.56x).
                remaining -= Time.deltaTime
                           * (_retrieving ? retrieveBiteBonus : 1f)
                           * _biteOddsBonus;
                yield return null;
            }

            // Roll the fish: tier first, then a uniform species roll inside it.
            // Re-read the dot so the roll uses the light at the moment of the bite.
            dot = CurrentSunDot();
            pendingBait = FishingBait.BestHeld();
            // How far out this cast actually landed. Feeds BOTH the tier roll
            // and the weight roll, so distance buys rarity and size together.
            float castDist = CastDistance();
            FishTier tier = FishingRules.RollTier(dot, pendingBait, castDist, Random.value);
            pendingSpecies = FishingRules.RollSpeciesInTier(tier, Random.value);
            pendingWeight = Mathf.Max(1, Mathf.RoundToInt(
                FishingRules.RollWeight(pendingSpecies, Random.value, castDist, pendingBait)));
            // Bounty water: inside an armed BountyZone each bite has the zone's
            // chance of being its bounty species instead (docs/Handoff_BountyQuest_Grulabu_v1.md).
            if (BountyZone.TryRoll(transform.position, Random.value, out int bountySpecies))
            {
                pendingSpecies = bountySpecies;
                tier = FishingRules.Species[bountySpecies].tier;
                pendingWeight = Mathf.Max(1, Mathf.RoundToInt(
                    FishingRules.RollWeight(bountySpecies, Random.value, castDist, pendingBait)));
                Debug.Log("[Bobber] BOUNTY bite: " + FishingRules.Species[bountySpecies].displayName);
            }
            currentFishType = tier.ToString();

            // The APPROACH: the rolled fish rises out of the deep, circles
            // the float, and takes it -- ~3.5 s of telegraph in front of the
            // strike window. Purely cosmetic; a recast or a shore arrival
            // kills this coroutine and the fish with it.
            yield return FishApproach();

            // The bait is spent HERE: on the bite, never on the cast, and it is
            // gone whether the player lands the fish, fluffs the hook window, or
            // snaps the line. That is the whole stake of the loop ([BUILD] 3).
            baitSpent = FishingBait.Consume(pendingBait);

            strikeEndTime = Time.time + tune.HookWindowFor(tier);
            isStriking = true;
            _retrieving = false;   // a bite interrupts the wind-in
            fishCaught = false;
            GamepadRumble.Pulse(0.8f, 0.8f, 0.4f);
            Debug.Log($"[Bobber] FISH ON! {FishingRules.Species[pendingSpecies].displayName} "
                    + $"({tier}, {pendingWeight}lb) bait={pendingBait} cast={castDist:F1}m "
                    + $"window={strikeEndTime - Time.time:F1}s");

            if (biteClip != null && biteSource != null)
            {
                biteSource.clip = biteClip;
                biteSource.volume = biteVolume;
                biteSource.Play();
            }

            while (Time.time < strikeEndTime && !fishCaught)
                yield return null;

            if (biteSource != null && biteSource.isPlaying)
                biteSource.Stop();

            isStriking = false;

            if (fishCaught)
            {
                // Hooked: the fight owns the bobber from here. TickFight ends it.
                while (fight != null) yield return null;
            }
            else
            {
                // Missed the window. [OPEN] 3's default: it costs the bait.
                // The fish loses interest and swims back down out of view.
                Debug.Log("[Bobber] Hook window missed - fish and bait gone.");
                StartCoroutine(FishRetreatRoutine());
                NoteLostBite();
                OnFishEscaped?.Invoke();
            }

            pendingSpecies = -1;
            currentFishType = "";
        }
    }

    // The fight.

    /// <summary>
    /// The strike-window click. Replaces the old instant catch: a successful
    /// click now HOOKS the fish and opens the fight rather than landing it.
    ///
    /// Spin is banked here rather than measured during the fight (Sam's call,
    /// 2026-09-01) - the jump-spin trick is timed against the strike window
    /// exactly as it always was, and the combo simply pays out on the landing
    /// instead of on the hook.
    /// </summary>
    public bool TryHookFish(Transform reeledBy, float spinDegrees = 0f, int spinCombo = 0)
    {
        if (!isStriking || fishCaught || pendingSpecies < 0) return false;
        fishCaught = true;
        bankedSpin  = spinDegrees;
        bankedCombo = spinCombo;

        if (biteSource != null && biteSource.isPlaying)
            biteSource.Stop();

        var tune = FishingTuning.Active;
        float stamina = FishingRules.StaminaFor(pendingSpecies, pendingWeight) * tune.staminaScale;
        float resist  = FishingRules.ResistFor(pendingSpecies, pendingWeight);
        var tier = FishingRules.Species[pendingSpecies].tier;

        // The fight starts at the REAL cast distance, not a constant -- a short
        // cast really is a shorter fight, which is a decision the player gets to
        // make with the cast itself.
        fightPlayer = reeledBy;
        _anchorLocal = _parkLocal;
        float startDistance = HorizontalDistanceToAngler();
        startDistance = Mathf.Max(startDistance, tune.landDistance + 1f);

        uint seed = (uint)Random.Range(1, int.MaxValue);
        fight = new FishFightSim(tier, stamina, startDistance, resist, seed,
                                 tune.reelRate, tune.relaxRate, tune.drainRate,
                                 tune.slackEscapeSeconds, tune.reelSpeed, tune.landDistance,
                                 tune.lineTautSeconds, tune.lineSlackSeconds);

        // The drag assumes the player and the bobber are on the SAME sphere. If
        // the water collider resolved to a different CelestialBody than the one
        // the player is stood on, that assumption is false and the fight would
        // drag the bobber somewhere absurd. The step clamp already makes that
        // survivable; this says so out loud rather than leaving Sam to guess.
        if (planetBody != null && reeledBy != null && waterRadius > 0.01f)
        {
            float playerRadius = Vector3.Distance(reeledBy.position, planetBody.position);
            float slack = Mathf.Max(6f, waterRadius * 0.15f);
            if (Mathf.Abs(playerRadius - waterRadius) > slack)
                Debug.LogWarning($"[Bobber] Player radius {playerRadius:F1} vs water radius "
                               + $"{waterRadius:F1} on '{planetBody.name}' - the bobber and the "
                               + "angler do not look like they are on the same body. The fight "
                               + "will still be bounded, but check the water collider's parent.");
        }

        // The approach's fish is adopted by the fight (SpawnHookedFish
        // early-returns when it already exists); the fight's own pose driver
        // owns it from here.
        _fishApproachActive = false;
        SpawnHookedFish();

        Debug.Log($"[Bobber] HOOKED {FishingRules.Species[pendingSpecies].displayName} "
                + $"- {startDistance:F1}m out, {stamina:F1}s of run, resist {resist:F2}");
        return true;
    }

    /// <summary>
    /// The fish itself, visible on the line: mouth at the bobber, body pointing
    /// away, swimming. Built by the Fishingdex's own preview recipe (tier prefab,
    /// weight-scaled width, species tint), stripped to a pure visual.
    /// </summary>
    void SpawnHookedFish()
    {
        if (_hookedFish != null || pendingSpecies < 0) return;
        if (!SpawnFishVisual()) return;
        PlaceHookedFish(1f);
        Debug.Log($"[Bobber] Hooked fish visual on the line - body {(_fishHalfLen * 2f):F2}m.");
    }

    /// <summary>
    /// Build the fish visual for the rolled species/weight: dex prefab,
    /// weight-driven size, species tint, physics stripped, parented to the
    /// float. Where it POSES is the caller's business -- the approach starts
    /// it deep under the float, the hook snaps it mouth-on-float.
    /// </summary>
    bool SpawnFishVisual()
    {
        if (_hookedFish != null || pendingSpecies < 0) return false;
        var dex = FishingdexManager.Instance != null
            ? FishingdexManager.Instance
            : FindObjectOfType<FishingdexManager>();
        if (dex == null) return false;
        var prefab = dex.PrefabForTier(FishingRules.Species[pendingSpecies].tier);
        if (prefab == null) return false;

        _hookedFish = Instantiate(prefab, transform.position, Quaternion.identity);
        // Kinematic IMMEDIATELY before the deferred Destroy — the one-frame live
        // rigidbody is a lesson this feature has paid for already.
        foreach (var frb in _hookedFish.GetComponentsInChildren<Rigidbody>(true))
        {
            frb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            frb.isKinematic = true;
            Destroy(frb);
        }
        foreach (var fc in _hookedFish.GetComponentsInChildren<Collider>(true)) Destroy(fc);

        // Size: NORMALIZE to a believable in-water body length driven by the
        // rolled weight -- the shared cube-root law, so weight reads as SIZE
        // (1 lb ~ 0.34 m, 8 lb ~ 0.68 m, 50 lb ~ 1.25 m). Normalizing (rather
        // than scaling the authored transform) matters: display-sized prefabs
        // once put _fishHalfLen at metres and the fish floated nowhere near
        // the bobber.
        float bodyLen = FishingRules.BodyLengthForWeight(pendingWeight);
        ViewmodelMotor.NormalizeSize(_hookedFish, bodyLen);
        _fishHalfLen = bodyLen * 0.5f;

        // Girth: the same weight-driven fattening the held fish gets, so the
        // fish is one shape everywhere. The models are authored facing -Z, so
        // local X = width (full factor) and Y = belly depth (60% of it);
        // length (Z) stays BodyLengthForWeight's.
        float girth = FishingRules.GirthFactorForWeight(pendingWeight);
        _hookedFish.transform.localScale = Vector3.Scale(
            _hookedFish.transform.localScale,
            new Vector3(girth, 1f + (girth - 1f) * 0.6f, 1f));

        Color tint = FishSpeciesVisuals.TintOf(pendingSpecies);
        foreach (var r in _hookedFish.GetComponentsInChildren<Renderer>())
        {
            r.material.color = tint;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // The dex previews these prefabs on a dedicated layer; make sure OUR
        // copy is on a layer the main camera actually draws.
        SetLayerRecursiveLocal(_hookedFish, gameObject.layer);
        _fishPhase = Random.value * 10f;

        // Sam's hand-authored anchor, the permanent fix for "the mouth is not
        // on the bobber": an empty child named MOUTH placed at the mouth of each
        // fish prefab. The out-of-the-mouth DIRECTION is derived from geometry
        // (model centre -> MOUTH), so the marker's own rotation does not matter
        // and no assumption is made about which way the model was authored —
        // these fish turned out to face -Z, which is why every guess before the
        // markers was flipped. Without a marker, the fallback below still works.
        _fishMouth = FindDeepChildLocal(_hookedFish.transform, "MOUTH");
        if (_fishMouth == null) _fishMouth = FindDeepChildLocal(_hookedFish.transform, "MouthPoint");
        if (_fishMouth != null)
        {
            Bounds fb = default;
            bool fbHas = false;
            foreach (var r in _hookedFish.GetComponentsInChildren<Renderer>())
            {
                if (!fbHas) { fb = r.bounds; fbHas = true; }
                else fb.Encapsulate(r.bounds);
            }
            Vector3 centreLocal = _hookedFish.transform.InverseTransformPoint(
                fbHas ? fb.center : _hookedFish.transform.position);
            Vector3 mouthLocal = _hookedFish.transform.InverseTransformPoint(_fishMouth.position);
            // The mouth offset as a CONSTANT, in WORLD METRES (root-frame
            // direction, every scale in the chain baked in). Captured ONCE,
            // BEFORE parenting, so posing never reads live transform state --
            // and never mixes unit systems. The AT tape caught the previous
            // capture in bobber units (the bobber root is scaled 0.2!) being
            // subtracted from planet-metre path coordinates.
            _fishMouthInRoot = Quaternion.Inverse(_hookedFish.transform.rotation)
                             * (_fishMouth.position - _hookedFish.transform.position);
            Vector3 dir = mouthLocal - centreLocal;
            _fishMouthDirLocal = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            // A local up orthogonal to the mouth axis, so the fish sits level.
            Vector3 upLocal = Vector3.up - _fishMouthDirLocal * Vector3.Dot(Vector3.up, _fishMouthDirLocal);
            _fishUpLocal = upLocal.sqrMagnitude > 0.0001f ? upLocal.normalized : Vector3.back;
        }

        // Parented to the PLANET, not the bobber (Sam's diagnosis, verbatim:
        // "I think they are using the bobber as reference so moving it moves
        // them" -- reeling was dragging the escaping fish). The planet is
        // scale-1, so planet-local metres ARE metres (the bobber root is
        // scaled 0.2, which silently shrank every under-the-bobber pose to a
        // fifth -- the AT tape's fishL 7.2 vs relBob 1.4). Lifecycle is
        // explicit: every path that ends a fish calls DespawnHookedFish, and
        // the bobber's OnDestroy sweeps up whatever remains.
        _hookedFish.transform.SetParent(planetBody != null ? planetBody : transform, true);
        _fishSwimInit = false;   // a fresh fish's follower seeds at its first path point
        return true;
    }

    /// <summary>
    /// Pose the fish swimming: centre at <paramref name="worldPos"/>, nose
    /// along <paramref name="swimDir"/>, using the authored MOUTH frame when
    /// the prefab has one (the same maths as the fight pose, so the model's
    /// -Z authoring never bites again).
    /// </summary>
    /// <summary>
    /// Pose the fish ENTIRELY IN LOCAL SPACE -- planet-local inputs, assigned
    /// through the bobber's STORED local pose, no world-coordinate sampling
    /// anywhere. The AR FishTape caught the old world-space version stealing
    /// single frames onto a second track exactly one frame of orbital carry
    /// (~55 cm) away: converting through the planet's transform at
    /// coroutine-time let the rail-planet's pose timing into the fish's
    /// stored position. The bobber itself never glitched because it never
    /// converts -- it lives planet-local and rides the parent chain. Now the
    /// fish does too: whatever the planet's transform does frame to frame,
    /// fish and water move in lockstep BY CONSTRUCTION.
    /// (mouthLocal/swimDirL/upL are all PLANET-local; the fish is a child of
    /// the bobber, whose local pose is stored numbers, so every conversion
    /// below is pure arithmetic on stored values.)
    /// </summary>
    void PoseFishLocal(Vector3 mouthLocal, Vector3 swimDirL, Vector3 upL, float tailDeg)
    {
        if (_hookedFish == null) return;

        // ── THE PURSUIT FOLLOWER — the final answer to the rogue writes. The
        // AX tape convicted threshold-rejection: a borderline rogue slipped
        // UNDER the gate, put the fish on the rogue track, and the gate then
        // rejected REALITY (growing 0.68->0.72m "rogues" that were the true
        // pounce) until the streak cap snapped it 72cm back. Any gate that
        // can quarantine the truth is the wrong tool. So the fish now OWNS
        // its position and SWIMS toward the path point at a capped fish
        // speed, every frame, unconditionally: a one-frame rogue tugs it
        // ~4cm and the very next frame bends it back -- no thresholds, no
        // streaks, no cliffs, nothing to snap. Facing comes from the
        // follower's own integrated velocity, smooth by construction.
        if (!_fishSwimInit)
        {
            _fishSwimPos = mouthLocal;
            _fishSwimVel = Vector3.zero;
            _fishSwimInit = true;
        }
        float dt = Mathf.Max(Time.deltaTime, 0.001f);
        Vector3 newPos = Vector3.MoveTowards(_fishSwimPos, mouthLocal, 6f * dt);
        Vector3 instVel = (newPos - _fishSwimPos) / dt;
        _fishSwimVel = Vector3.Lerp(_fishSwimVel, instVel, 1f - Mathf.Exp(-12f * dt));
        _fishSwimPos = newPos;
        mouthLocal = _fishSwimPos;
        if (_fishSwimVel.sqrMagnitude > 0.0025f) swimDirL = _fishSwimVel;

        if (swimDirL.sqrMagnitude < 0.0001f) swimDirL = Vector3.forward;
        swimDirL = swimDirL.normalized;

        // Sam's 45-degree law: a fish never points steeper than 45 degrees
        // off horizontal, climbing or diving, in any phase. Hard clamp.
        Vector3 horiz = Vector3.ProjectOnPlane(swimDirL, upL);
        if (horiz.sqrMagnitude > 0.000001f)
        {
            float vert = Vector3.Dot(swimDirL, upL);
            float maxVert = horiz.magnitude;              // tan(45) = 1
            if (Mathf.Abs(vert) > maxVert)
                swimDirL = (horiz + upL * (Mathf.Sign(vert) * maxVert)).normalized;
        }

        // THE MOUTH IS THE ANCHOR (Sam's spec): our models can't articulate a
        // tail, so the kick is the whole BODY yawing about the MOUTH -- the
        // mouth holds the line of travel and the tail does the sweeping.
        Vector3 noseDir = tailDeg > 0.01f
            ? Quaternion.AngleAxis(Mathf.Sin(_fishPhase) * tailDeg, upL) * swimDirL
            : swimDirL;

        Vector3 frameUp = Mathf.Abs(Vector3.Dot(noseDir, upL)) > 0.98f
            ? Vector3.Cross(noseDir, Vector3.right).normalized : upL;
        // The fish is a child of the PLANET (scale 1), so planet-local pose
        // assigns DIRECTLY: no other frame, no unit conversion, no live
        // transform reads. ABSOLUTE mouth anchoring: pose = pure function of
        // (path point, rotation, spawn-time constant) -- nothing to corrupt,
        // nothing to alternate, no coupling to the bobber whatsoever.
        Quaternion planetRot = Quaternion.LookRotation(noseDir, frameUp);
        Quaternion targetLocalRot;
        Vector3 targetLocalPos;
        if (_fishMouth != null)
        {
            Quaternion localFrame = Quaternion.LookRotation(_fishMouthDirLocal, _fishUpLocal);
            targetLocalRot = planetRot * Quaternion.Inverse(localFrame);
            targetLocalPos = mouthLocal - targetLocalRot * _fishMouthInRoot;
        }
        else
        {
            Vector3 nose = flipHookedFish ? -noseDir : noseDir;
            targetLocalRot = Quaternion.LookRotation(nose, frameUp);
            targetLocalPos = mouthLocal - noseDir * _fishHalfLen;
        }

        _hookedFish.transform.localRotation = targetLocalRot;
        _hookedFish.transform.localPosition = targetLocalPos;
    }

    /// <summary>
    /// The APPROACH (Sam's design, 2026-09-02): the fish spawns deep below
    /// the float — where the analytic ocean swallows it, so it FADES IN for
    /// free as it rises — spirals up, circles the float once near the
    /// surface, then darts in and takes it. The strike window opens when it
    /// takes the float, exactly where the old instant bite was; only the
    /// telegraph in front of it is new. Purely cosmetic, planet-local,
    /// transform-driven — nothing here touches physics.
    /// </summary>
    IEnumerator FishApproach()
    {
        if (_hookedFish != null) DespawnHookedFish();
        if (planetBody == null || !SpawnFishVisual()) yield break;
        _fishApproachActive = true;

        // Basis around the float, planet-local, so a worked lure carries its
        // suitor with it and origin shifts are irrelevant.
        Vector3 upL = bobUpLocal.sqrMagnitude > 0.0001f ? bobUpLocal.normalized : Vector3.up;

        // The fish comes in from OPEN WATER -- the far side of the float from
        // the angler, where it is deepest -- never from under the bank.
        Vector3 awayL = Vector3.ProjectOnPlane(Vector3.forward, upL).normalized;
        if (fightPlayer != null)
        {
            Vector3 playerL = planetBody.InverseTransformPoint(fightPlayer.position);
            Vector3 a = Vector3.ProjectOnPlane(_parkLocal - playerL, upL);
            if (a.sqrMagnitude > 0.0001f) awayL = a.normalized;
        }
        Vector3 sideL = Vector3.Cross(upL, awayL);

        // Start as deep as the water actually allows: the point is that the
        // ocean fully swallows the entry (Sam: "it needs to come up from very
        // deep so it doesn't just appear"), but never under the lake bed.
        float bedDepth = 8f;
        int gm = GroundMask();
        if (gm != 0 && Physics.Raycast(transform.position,
                -planetBody.TransformDirection(upL), out RaycastHit bedHit, 10f,
                gm, QueryTriggerInteraction.Ignore))
            bedDepth = bedHit.distance;
        float startDepth = Mathf.Clamp(bedDepth - 0.4f, 1.2f, 7f);

        // ── THE RISING HELIX — Sam's spec, verbatim (2026-09-02 v3) ─────────
        // "A fish coming up out of the depths moving its tail like a fish and
        // swimming in a spring-like swirl under the bobber, but on like an
        // angle swimming up." So: ONE continuous WIDE helix — starting deep
        // and ~2.8 m out, climbing at a shallow pitch (~12 degrees: the
        // instantaneous motion is almost all horizontal-tangential, never a
        // vertical climb), tightening toward the float as it rises, slow
        // enough to watch (a lap every ~3 s, tangential speed a believable
        // 2-5 m/s). The v2 version's laps were 0.5-0.9 m at 1.2 s/lap and the
        // wide entry ran where the water hides it — all Sam could see was a
        // tight fast spin ("the fish just spins underneath the bobber").
        // Then a half-beat level-off just under the float, and the pounce.
        const float HelixSeconds = 4.5f, LevelSeconds = 0.6f, PounceSeconds = 0.28f;
        const float HelixLaps = 1.4f;
        float circDir = Random.value < 0.5f ? 1f : -1f;
        float helixW = circDir * (HelixLaps * 2f * Mathf.PI) / HelixSeconds;

        Vector3 CirclePoint(float ang, float radius, float depth)
            => _parkLocal - upL * depth
             + (awayL * Mathf.Cos(ang) + sideL * Mathf.Sin(ang)) * radius;

        Vector3 prevLocal = Vector3.zero;
        bool hasPrev = false;
        float total = HelixSeconds + LevelSeconds + PounceSeconds;
        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            if (_hookedFish == null || planetBody == null) { _fishApproachActive = false; yield break; }

            Vector3 local;
            float tailDeg;
            if (t < HelixSeconds)
            {
                float k = t / HelixSeconds;
                float ang = helixW * t;
                // Sam's path law: it starts swimming HORIZONTALLY -- the first
                // stretch of the spiral is flat -- and only then angles
                // upward, gently (the 45-degree body clamp is in PoseFish; the
                // path itself never demands more than ~15 degrees).
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, k));
                local = CirclePoint(ang,
                                    Mathf.Lerp(2.8f, 0.65f, k),
                                    Mathf.Lerp(startDepth, 0.5f, rise));
                tailDeg = 15f;
            }
            else if (t < HelixSeconds + LevelSeconds)
            {
                // Levels off just under the float for half a beat -- the
                // moment of decision before the take.
                float k = (t - HelixSeconds) / LevelSeconds;
                float ang = helixW * t;
                local = CirclePoint(ang, Mathf.Lerp(0.65f, 0.5f, k),
                                    Mathf.Lerp(0.5f, 0.42f, k));
                tailDeg = 10f;
            }
            else
            {
                float k = (t - HelixSeconds - LevelSeconds) / PounceSeconds;
                k = k * k;   // accelerating lunge
                // The pounce is the SPIRAL'S OWN final tightening -- the
                // circle never stops turning, its radius just collapses onto
                // the float while the depth rises THROUGH the surface to the
                // exact point the strike pose glues the mouth to (float
                // +4 cm). Direction stays continuous the whole way, and the
                // handoff to the strike pose has zero positional gap -- the
                // straight-line lunge this replaced snap-turned at its start
                // and popped 8 cm at its end (Sam's "glitches out for a
                // second and then bites").
                float ang = helixW * t;
                local = CirclePoint(ang, Mathf.Lerp(0.5f, 0.02f, k),
                                    Mathf.Lerp(0.42f, -0.04f, k));
                tailDeg = 4f;   // locked on
            }

            // Direction and speed from PLANET-LOCAL deltas. The world delta is
            // ~98% the planet's own orbital motion, and using it aimed the
            // fish along the ORBIT (Sam's "mouth up, tail down") and read its
            // speed as ~85 m/s, which drove the tail phase at flicker rates
            // (his "jittering"). The local delta IS the swim.
            Vector3 dLocal = hasPrev ? local - prevLocal
                : (circDir > 0f ? sideL : -sideL) * 0.01f;
            float speed = hasPrev && Time.deltaTime > 0f
                ? dLocal.magnitude / Time.deltaTime : 2f;
            _fishPhase += (3.5f + speed * 1.8f) * Time.deltaTime;   // readable kicks
            PoseFishLocal(local, dLocal, upL, tailDeg);
            prevLocal = local;
            hasPrev = true;
            yield return null;
        }
        // The strike window opens next (the caller sets isStriking); the
        // gated LateUpdate writer then holds the fish mouth-on-float.
    }

    /// <summary>
    /// The fish gives up — spat hook, snapped line, or a missed window — and
    /// swims back down out of view, where the ocean swallows it, then
    /// despawns. Sam: "the fish just disengages and swims back down and out
    /// of view and disappears."
    /// </summary>
    IEnumerator FishRetreatRoutine()
    {
        // Own THIS fish specifically: if a fast next bite spawns a new fish
        // mid-retreat, this coroutine must bow out without touching shared
        // state -- the new fish's approach owns the flags then.
        GameObject myFish = _hookedFish;
        if (myFish == null) yield break;
        _fishApproachActive = true;   // this coroutine owns the pose now

        // PLANET-LOCAL, like the approach: a world-anchored flee path is left
        // behind by the rail-planet within a frame (the fish visibly smeared
        // off through the water at orbital speed -- Sam's "looks terrible").
        if (planetBody == null) { DespawnHookedFish(); yield break; }
        Vector3 upL2 = bobUpLocal.sqrMagnitude > 0.0001f ? bobUpLocal.normalized
            : planetBody.InverseTransformDirection(
                (transform.position - planetBody.position).normalized);
        // The fish is planet-parented: its stored localPosition IS the
        // planet-local start point. No sampling, no conversion.
        Vector3 startLocal = myFish.transform.localPosition;

        // Wake the pursuit follower HERE. During the fight the fight system
        // poses the fish and the follower sleeps -- its memory still holds
        // the bite point from the end of the approach. Without this reset, a
        // snapped line teleported the fish ~10 m back to where it was first
        // hooked, then swam it toward the angler chasing the retreat's start
        // (Sam's report, every leg of it). Re-seeding makes the first retreat
        // frame start exactly where the fish actually is.
        _fishSwimInit = false;

        // It bolts the way it was already going: AWAY from the angler (during
        // the fight it faces away, so this continues its own motion -- Sam:
        // "it's already swimming away from you with the bobber, just make it
        // detach and keep kicking its tail and swim away and down").
        Transform anglerT = fightPlayer != null ? fightPlayer
            : (rodOwner != null ? rodOwner.transform : null);
        Vector3 fleeL = anglerT != null
            ? Vector3.ProjectOnPlane(
                startLocal - planetBody.InverseTransformPoint(anglerT.position), upL2)
            : Vector3.Cross(upL2, Vector3.right);
        if (fleeL.sqrMagnitude < 0.0001f) fleeL = Vector3.Cross(upL2, Vector3.right);
        fleeL.Normalize();

        // Dive until the water has fully swallowed it -- as deep as the lake
        // bed allows, so it never blinks out of thin air.
        float bedDepth = 8f;
        int gm = GroundMask();
        if (gm != 0 && Physics.Raycast(myFish.transform.position,
                -planetBody.TransformDirection(upL2), out RaycastHit bedHit, 10f,
                gm, QueryTriggerInteraction.Ignore))
            bedDepth = bedHit.distance;
        float diveDepth = Mathf.Clamp(bedDepth - 0.4f, 1.2f, 7f);

        const float RetreatSeconds = 2.4f;
        Vector3 prevL = startLocal;
        bool hasPrevL = false;
        for (float t = 0f; t < RetreatSeconds; t += Time.deltaTime)
        {
            if (myFish == null || _hookedFish != myFish) yield break;
            float k = t / RetreatSeconds;
            // Mostly horizontal at first, the dive steepening as it goes --
            // fleeing on an angle, never sinking like a stone. (The 45-degree
            // body clamp in PoseFish holds regardless.)
            Vector3 local = startLocal + fleeL * (5.5f * k) - upL2 * (diveDepth * k * k)
                          + Vector3.Cross(upL2, fleeL) * (Mathf.Sin(t * 5f) * 0.18f * k);
            Vector3 dLocal = hasPrevL ? local - prevL : fleeL * 0.01f;
            float speed = hasPrevL && Time.deltaTime > 0f
                ? dLocal.magnitude / Time.deltaTime : 2f;
            _fishPhase += (5f + speed * 2.5f) * Time.deltaTime;
            // Hard panicked kicks off the mark, settling as it escapes.
            PoseFishLocal(local, dLocal, upL2, Mathf.Lerp(20f, 9f, k));
            prevL = local;
            hasPrevL = true;
            yield return null;
        }
        if (_hookedFish == myFish) DespawnHookedFish();
    }

    static Transform FindDeepChildLocal(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeepChildLocal(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static void SetLayerRecursiveLocal(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursiveLocal(child.gameObject, layer);
    }

    /// <summary>
    /// The fish swims. Fresh: fast, wide thrashing that visibly hauls at the
    /// float; running: leaning into it; spent: it settles to a slow, beaten
    /// sway. Vigour drives all of it, so the fish tiring IS the animation.
    /// </summary>
    void UpdateHookedFish()
    {
        PlaceHookedFish(1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    /// <summary>
    /// Put the fish where a hooked fish IS: mouth on the float, body trailing
    /// away from the angler, wiggling by however much fight it has left.
    /// <paramref name="k"/> = 1 snaps; anything found beyond arm's reach of the
    /// float snaps too, so no state can ever leave the fish stranded.
    /// Works with or without a live fight (after the shore fuse, the pose is
    /// the settled, beaten one).
    /// </summary>
    void PlaceHookedFish(float k)
    {
        if (_hookedFish == null) return;
        Transform anglerT = fightPlayer != null ? fightPlayer
            : (rodOwner != null ? rodOwner.transform : null);
        if (anglerT == null) return;

        Vector3 bobberPos = transform.position;
        Vector3 up = planetBody != null
            ? (bobberPos - planetBody.position).normalized
            : (rodOwner != null ? rodOwner.transform.up : Vector3.up);

        // During the strike window (bit, not yet hooked) the fish is ALIVE on
        // the float -- full vigour, fight pose -- not the beaten landed pose.
        bool landed = fight == null && !isStriking;
        float vig = fight != null ? fight.Vigour : (isStriking ? 1f : 0f);
        bool run = fight != null && fight.IsRunning;

        Vector3 body;
        if (landed)
        {
            // Out of the water, beaten: mouth on the float, dragged along by
            // the line -- but at an ANGLE, not nose-first at the tip. Facing
            // the tip dead-on pointed the fish straight at the camera, and a
            // fish seen head-on foreshortens to a fraction of its length --
            // Sam's "the fish shrinks at the shore" (2026-09-02). It never
            // changed size; it lost its profile. 65 degrees off the line
            // keeps the flank visible while still reading as hauled in.
            Vector3 toTip = rodOwner != null
                ? rodOwner.LineOriginWorld - bobberPos
                : anglerT.position - bobberPos;
            body = toTip.sqrMagnitude > 0.0001f ? toTip.normalized : up;
            _fishPhase += Time.deltaTime * 1.6f;
            body = Quaternion.AngleAxis(65f + Mathf.Sin(_fishPhase) * 4f, up) * body;
        }
        else
        {
            Vector3 away = Vector3.ProjectOnPlane(bobberPos - anglerT.position, up);
            away = away.sqrMagnitude > 0.0001f
                ? away.normalized
                : Vector3.Cross(up, Vector3.right).normalized;

            // A fish making a BREAK swims like one: the tail rate rides vigour
            // and better than doubles on a run, then the whole display dies
            // down as the fish tires on its way in to the bank.
            _fishPhase += Time.deltaTime * Mathf.Lerp(2f, 10f, vig) * (run ? 2.4f : 1f);
            float yawAmp = Mathf.Lerp(4f, 30f, vig) * (run ? 1.5f : 1f);
            body = Quaternion.AngleAxis(Mathf.Sin(_fishPhase) * yawAmp, up) * away;
        }

        // ABOVE the surface, deliberately. The ocean is an analytic post-effect
        // that paints over everything below the waterline (the reason the cave
        // cutouts exist), so a fish placed even centimetres under water is
        // simply invisible — which is why it only ever appeared once towed up
        // the bank. Mouth at the float, head tilted up, tail trailing down into
        // the water: the visible half thrashes at the surface.
        // Heavy fish ride proportionally higher out of the water -- a 45 lb
        // slab showing only a sliver of back reads skinny no matter how big
        // it is (Sam, 2026-09-02). Scaled by body size: ~4 cm for a 1-pounder
        // up to ~8 cm for the big rares.
        Vector3 biteAt = bobberPos
            + (landed ? Vector3.zero : up * (0.02f + 0.1f * _fishHalfLen));

        // ── The authored anchor path ─────────────────────────────────────────
        // Rotate the fish so its geometric out-of-the-mouth direction points at
        // the bite, then translate so Sam's MOUTH marker sits ON the float.
        // Exact for any pivot, nesting, scale or authored facing.
        if (_fishMouth != null)
        {
            // Nose pointing AWAY from the angler -- the fish is running from you
            // with the float at its face (Sam: "rotated 180 degrees around the
            // bobber... facing away from me running away with the bobber").
            Vector3 mouthDirWorld = body;
            // Landed, the mouth direction runs up the line toward the tip and
            // can approach the frame's up -- fall back to the angler's forward
            // so LookRotation never degenerates.
            Vector3 frameUp = Mathf.Abs(Vector3.Dot(mouthDirWorld, up)) > 0.98f
                ? anglerT.forward : up;
            Quaternion worldFrame = Quaternion.LookRotation(mouthDirWorld, frameUp);
            if (!landed)
            {
                worldFrame *= Quaternion.Euler(-12f, 0f, 0f);   // head up out of the water
                if (run) worldFrame *= Quaternion.Euler(10f, 0f, 0f);   // leaning in
            }
            Quaternion localFrame = Quaternion.LookRotation(_fishMouthDirLocal, _fishUpLocal);
            Quaternion mouthRot = worldFrame * Quaternion.Inverse(localFrame);
            // During the STRIKE window the fish TURNS into its thrash pose
            // instead of teleporting to it -- the approach hands over facing
            // wherever the spiral ended, and an instant flip to the
            // away-from-angler pose was the last hard cut in Sam's "glitches
            // out then bites". The mouth stays glued regardless (position is
            // re-anchored after the rotation). The fight keeps its raw pose:
            // its motion is continuous by construction.
            if (fight == null && isStriking)
                mouthRot = Quaternion.Slerp(_hookedFish.transform.rotation, mouthRot,
                                            1f - Mathf.Exp(-9f * Time.deltaTime));
            _hookedFish.transform.rotation = mouthRot;
            _hookedFish.transform.position += biteAt - _fishMouth.position;
            return;
        }

        // ── Fallback: pivot sits mid-body on these models, so the CENTRE goes
        // half a body behind the float — which puts the MOUTH on the float.
        Vector3 targetPos = bobberPos - body * _fishHalfLen
            + (landed ? Vector3.zero : up * 0.02f);
        Vector3 nose = flipHookedFish ? -body : body;
        Vector3 fbUp = Mathf.Abs(Vector3.Dot(nose, up)) > 0.98f ? anglerT.forward : up;
        Quaternion targetRot = Quaternion.LookRotation(nose, fbUp);
        if (run) targetRot *= Quaternion.Euler(10f, 0f, 0f);   // leaning into the pull

        bool farAdrift = (targetPos - _hookedFish.transform.position).sqrMagnitude > 1f;
        if (k >= 1f || farAdrift)
        {
            _hookedFish.transform.position = targetPos;
            _hookedFish.transform.rotation = targetRot;
            return;
        }
        _hookedFish.transform.position = Vector3.Lerp(_hookedFish.transform.position, targetPos, k);
        _hookedFish.transform.rotation = Quaternion.Slerp(_hookedFish.transform.rotation, targetRot, k);
    }

    /// <summary>
    /// Advance the fight one frame. Driven by FishingRodController, which owns
    /// the reel input. Returns the outcome so the rod can play the right anim.
    /// </summary>
    public FightOutcome TickFight(bool holding, float dt)
    {
        if (fight == null) return FightOutcome.Fighting;

        // Order matters, and doing it here rather than across two Update methods
        // is what guarantees it: push REALITY in, step the sim, then move the
        // bobber to the sim's answer. The landing therefore fires on where the
        // bobber actually is, not on a running total that had drifted from it.
        fight.SyncDistance(HorizontalDistanceToAngler());

        var outcome = fight.Step(dt, holding);

        // The shore beats the distance rule. Reeled up onto solid ground is a
        // caught fish whatever the numbers say -- and off a clifftop, where the
        // angler is never within 2 m of any water, it is the ONLY rule that can
        // fire. Small grace period so a bobber that landed against a rock does
        // not count the instant it is hooked.
        if (outcome == FightOutcome.Fighting && fight.Elapsed > 0.4f && LureHasArrived())
        {
            Debug.Log("[Bobber] Fish reached the shore - landed.");
            outcome = FightOutcome.Landed;
        }

        FishingTensionHUD.Set(fight.TensionFraction, fight.IsRunning);

        switch (outcome)
        {
            case FightOutcome.Fighting:
                DragToFightDistance(holding, dt);
                UpdateHookedFish();
                return outcome;

            case FightOutcome.Landed:
                // Do NOT book the catch yet. Sam: "even when catching a fish the
                // bobber should be reeled back up to its resting position before
                // it counts." The fish leaves the water as a physics object at
                // the shore and the tow winds it home over the bank; LandFish
                // runs from the tow's FixedUpdate at the pickup.
                _pendingLanding = true;
                _landedSpecies = pendingSpecies;
                _landedWeight = pendingWeight;
                _landedTier = currentFishType;
                fight = null;
                _nextTug = 0f;
                _tugPhase = 0f;
                FishingTensionHUD.Hide();
                // The catch is already parented to the float (it has been since
                // the hook). Snap it into the settled mouth-on-float pose and it
                // rides the proven tow up the bank as part of the bobber.
                if (_hookedFish != null) PlaceHookedFish(1f);
                ReleaseFromWater();
                return outcome;

            case FightOutcome.Snapped:
                // Sam's design, 2026-09-02: a snapped line no longer costs
                // the whole rig. The float bobs back up to the surface (the
                // buoyant rise in IdleShake) and the fish disengages and
                // swims down out of view. The rod still plays its recoil and
                // snap sound via OnLineSnapped; the fish and bait are gone.
                Debug.Log("[Bobber] LINE SNAPPED - fish and bait lost; the float pops back up.");
                StartCoroutine(FishRetreatRoutine());
                OnLineSnapped?.Invoke();
                OnFishEscaped?.Invoke();
                break;

            default:   // SlippedOff
                // The fish spat the hook because the player stopped reeling.
                // The RIG IS FINE -- nothing broke and nothing was landed, so
                // the bobber stays exactly where it is and simply goes still.
                // Sam: "the bobber should just go from moving to being still and
                // staying in the water." That stillness is the message; the
                // fishing routine picks straight back up waiting for the next
                // bite, and this spot is now a better bet than it was.
                Debug.Log("[Bobber] Slack too long - the fish shook the hook. Bobber stays out.");
                StartCoroutine(FishRetreatRoutine());
                NoteLostBite();
                OnFishEscaped?.Invoke();
                break;
        }

        fight = null;
        _nextTug = 0f;
        _tugPhase = 0f;

        // Settle onto the anchor and re-derive the surface normal HERE, not at
        // the original landing spot: a fight can drag the float tens of metres
        // across a curved planet, and bobbing along the old normal after that
        // would tip it through the water.
        if ((outcome == FightOutcome.SlippedOff || outcome == FightOutcome.Snapped)
            && _anchorLocal.sqrMagnitude > 0.0001f)
        {
            bobUpLocal = _anchorLocal.normalized;
            baseLocalPosition = _anchorLocal - bobUpLocal * 0.01f;   // the in-water seat
            // Deliberately no instant reposition: _parkLocal is still at the
            // fight's submerged pose, and IdleShake's buoyant MoveTowards
            // FLOATS it back up to this rest pose over ~half a second.
        }

        FishingTensionHUD.Hide();
        OnFightEnded?.Invoke(outcome);
        return outcome;
    }

    /// <summary>The existing catch flow, now species-aware.</summary>
    /// <summary>
    /// Book the catch. Runs when the bobber gets home to the rod, not when the
    /// fight ends, so it reads the values captured AT the landing rather than
    /// the live roll state, which has been cleared by then.
    /// </summary>
    void LandFish()
    {
        if (_landedSpecies < 0) return;
        Debug.Log($"[Bobber] LANDED {FishingRules.Species[_landedSpecies].displayName} "
                + $"{_landedWeight}lb. Spin: {bankedSpin:F0}deg Combo: {bankedCombo}");

        if (FishingRules.IsBounty(_landedSpecies))
        {
            // One per world: the zone retires, the story knows.
            BountyZone.NoteCaught(_landedSpecies);
            if (HALCommentator.Instance != null)
                HALCommentator.Instance.VolunteerExternal("That is not a fish. That is an event. The fish vendor will want to see it.");
        }
        if (FishInventory.Instance != null)
        {
            var entry = FishInventory.Instance.AddFish(_landedSpecies, _landedWeight);
            bool placed =
                (Hotbar.Instance != null && Hotbar.Instance.TryAddFishToBag(entry)) ||
                (Hotbar.Instance != null && Hotbar.Instance.TryAddFish(entry));
            if (!placed) InventoryFullPopup.Show();
        }
        OrientationObjectives.Complete(OrientationObjectives.Objective.CatchFish);
        if (FishCatchUI.Instance != null)
            FishCatchUI.Instance.ShowFishCaught(_landedTier, _landedWeight, bankedSpin, bankedCombo);

        OnFishLanded?.Invoke(bankedSpin, bankedCombo);
        _landedSpecies = -1;

        // Off the line and into the hotbar.
        DespawnHookedFish();
    }

    void DespawnHookedFish()
    {
        _fishApproachActive = false;
        if (_hookedFish == null) return;
        Destroy(_hookedFish);
        _hookedFish = null;
    }

    // Weight distribution: 15% chance 1 lb, 5% chance 50 lbs, 80% across 2-49 lbs biased low.
    public static int GenerateFishWeight()
    {
        float rand = Random.value;
        if (rand < 0.15f) return 1;   // 15%: 1 lb
        if (rand < 0.20f) return 50;  // 5%: 50 lbs
        // 80%: 2-49 lbs with a low-weight bias (power curve)
        float t = Mathf.Pow(Random.value, 1.5f);
        return Mathf.RoundToInt(Mathf.Lerp(2f, 49f, t));
    }

    void OnDestroy()
    {
        // Explicitly stop the fishing coroutine — Unity stops coroutines when the
        // owning MonoBehaviour is destroyed, but doing it here makes the lifecycle
        // explicit and protects against subtle ordering issues if a derived class
        // ever spawns sub-coroutines that should also be cancelled.
        if (fishingCoroutine != null) { StopCoroutine(fishingCoroutine); fishingCoroutine = null; }

        // Whatever killed this bobber -- a catch, a snap, an unequip, a scene
        // change -- the fight is over and the bar must go with it. So must any
        // fish still on the line (a fused one dies as our child; a fighting one
        // is unparented and needs this).
        DespawnHookedFish();
        if (fight != null) { fight = null; FishingTensionHUD.Hide(); }

        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null) em.UnregisterPhysicsObject(transform);
    }

    // -- appended after initial release; keep field order (serialization) --

    [Header("Equip Drop")]
    [Tooltip("Metres per second of line paid out when the freshly equipped bobber drops off the tip to its hang. Slower = a more deliberate lower; the drop takes hangLeash / this seconds.")]
    public float hangPayOutSpeed = 1.4f;

    [Header("Submerge (bite / fight)")]
    [Tooltip("Metres the float is pulled UNDER the surface while a fish nibbles, before the hook is set. 'Pulled down into the water a little bit and thrashed' -- Sam.")]
    public float biteSubmerge = 0.12f;
    [Tooltip("Metres the float rides under the surface for the whole fight once the hook is set. The empty-lure retrieve stays ON the surface; only a fish takes it down.")]
    public float fightSubmerge = 0.28f;

}