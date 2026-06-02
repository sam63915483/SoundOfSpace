using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One audience member's per-frame dance behavior. Drives bones in LateUpdate so
// it wins over the Animator (same trick NPCWaveAnimation uses). Replaces — does
// not coexist with — NPCWaveAnimation on the same GameObject; the head-track
// and wave motion would fight headbanging.
//
// State machine: Headbang (default), ArmsUpFlick (event-driven), Dance1/2/3.
// Each member runs on its own random phase + state-change timer so the crowd
// looks decorrelated. Music sync subscribes to ConcertAudioDirector events:
// kick punches the headbang, drops/crashes flick the arms up.
public class AudienceMember : MonoBehaviour
{
    public enum DanceState { Headbang, ArmsUpFlick, Dance1, Dance2, Dance3, Dance4, Dance5, Dance6 }

    static readonly List<AudienceMember> s_all = new List<AudienceMember>();
    public static IReadOnlyList<AudienceMember> AllMembers => s_all;

    [Header("Tempo (locked to song beats when ConcertAudioDirector is present)")]
    [Tooltip("Headbang cycles per beat. 1 = pump on every beat, 0.5 = pump every 2 beats, 2 = pump twice per beat.")]
    [SerializeField] float headbangCyclesPerBeat = 1f;
    [Tooltip("Maximum head pitch deflection in degrees during headbang.")]
    [SerializeField] float headbangAngle = 28f;
    [Tooltip("Fallback BPM when no ConcertAudioDirector exists. 120 = 2 beats/sec.")]
    [SerializeField] float fallbackBpm = 120f;

    [Header("Arms Up + Fist Pump (default)")]
    [Tooltip("How high the upper arms raise in the default fist-pump state (degrees).")]
    [SerializeField] float armsUpAngle = 150f;
    [Tooltip("Forearm pump angle, forward + back from rest (degrees). Higher = more pronounced pump.")]
    [SerializeField] float fistPumpAngle = 55f;
    [Tooltip("If the fist pumps in the wrong direction (e.g. sideways instead of forward), flip this. Toggling reverses the sign.")]
    [SerializeField] bool invertFistPump = false;

    [Header("Arms Up Flick (drop / crash)")]
    [Tooltip("Seconds to hold arms-up flick before returning to default state.")]
    [SerializeField] float armsUpDurationMin = 1.2f;
    [SerializeField] float armsUpDurationMax = 2.0f;

    [Header("Dance4 - Jump")]
    [Tooltip("Vertical hop height in metres (applied as pelvis localPosition Y offset).")]
    [SerializeField] float jumpHeight = 0.9f;
    [Tooltip("Jumps per beat. 1 = hop on every beat.")]
    [SerializeField] float jumpCyclesPerBeat = 1f;

    [Header("Dance5 - Weird Pose")]
    [Tooltip("Held twist amplitude on the spine (degrees) for the weird pose dance.")]
    [SerializeField] float weirdSpineTwist = 30f;
    [Tooltip("Held lean amplitude on the pelvis (degrees) for the weird pose dance.")]
    [SerializeField] float weirdPelvisLean = 18f;

    [Header("Dance6 - Flip Jump (backflip / frontflip / cartwheel)")]
    [Tooltip("Vertical clearance for flip jumps (metres). Bigger than the basic jump because the alien needs room to rotate.")]
    [SerializeField] float flipJumpHeight = 3.5f;
    [Tooltip("Seconds for one full flip cycle (prep + airborne + land + rest). Free-running real-time, not synced to song beats — flips look unnatural when locked to BPM.")]
    [SerializeField] float flipCycleSeconds = 3.5f;
    [Tooltip("Fraction of the cycle spent airborne (0..1). The rest is split between prep crouch, landing, and rest.")]
    [Range(0.3f, 0.8f)] [SerializeField] float flipAirborneFraction = 0.55f;
    [Tooltip("Cartwheel lobe arm spread (degrees from rest, T-pose-ish). 90 = arms straight out horizontally.")]
    [SerializeField] float cartwheelArmSpread = 90f;

    [Header("Dance Variety")]
    [Tooltip("Dance move cycles per beat (sway / bob / point). 0.5 = one full motion per 2 beats.")]
    [SerializeField] float danceCyclesPerBeat = 0.5f;
    [Tooltip("Side-to-side hip yaw during Dance1 (sway), degrees.")]
    [SerializeField] float swayHipAngle = 12f;
    [Tooltip("Vertical bob amount during Dance2, world-space metres.")]
    [SerializeField] float bobAmount = 0.10f;
    [Tooltip("Pelvis pitch (forward lean) during Dance2 bob, degrees.")]
    [SerializeField] float bobPelvisPitch = 8f;
    [Tooltip("Shoulder shimmy angle during Dance1, degrees.")]
    [SerializeField] float shoulderShimmy = 18f;

    [Header("State Timing")]
    [SerializeField] float stateChangeMin = 4f;
    [SerializeField] float stateChangeMax = 9f;

    [Header("Blend")]
    [Tooltip("How fast bones blend from current to target rotation. Higher = snappier.")]
    [SerializeField] float blendSpeed = 12f;

    // ── Bones ────────────────────────────────────────────────────────────
    Transform _head, _neck, _spine, _pelvis;
    Transform _upperArmL, _upperArmR, _lowerArmL, _lowerArmR;

    // Rest local rotations (captured after Animator settles).
    Quaternion _headRest, _neckRest, _spineRest, _pelvisRest;
    Quaternion _upperArmLRest, _upperArmRRest, _lowerArmLRest, _lowerArmRRest;
    // Pelvis rest local position — used to add a vertical hop offset for the jump dance.
    Vector3 _pelvisRestPos;
    // Whole-alien rest pose (in body-parent space). Dance6 writes overrides on
    // top of these and falls back to them when the dance ends.
    Vector3 _baseLocalPosition;
    Quaternion _baseLocalRotation;
    bool _baseCaptured;

    // World-space axes used to swing each upper arm up. The right arm's shaft
    // points outward to the right in T-pose, the left arm's shaft mirror; so
    // the cross-product axes are opposite. Sharing one axis between both arms
    // makes the left arm rotate THROUGH the body instead of outward.
    Vector3 _armRaiseAxisR;
    Vector3 _armRaiseAxisL;

    // Smoothed local rotations applied per frame.
    Quaternion _headSmoothed, _spineSmoothed, _pelvisSmoothed;
    Quaternion _upperArmLSmoothed, _upperArmRSmoothed, _lowerArmLSmoothed, _lowerArmRSmoothed;

    // ── State ────────────────────────────────────────────────────────────
    bool _ready;
    DanceState _state = DanceState.Headbang;
    float _phaseOffset;          // 0..2π — used for left/right "weird pose" tie-break
    float _beatPhaseOffsetBeats; // 0..4 — staggered downbeat pick so different aliens land on different beats
    float _beatRateMul;          // {0.5, 1.0, 2.0} — per-alien tempo subdivision so the crowd headbangs at varied rates while still locked to the song
    float _stateTimer;
    float _kickPunchUntil;
    float _kickPunchStrength;
    float _armsUpUntil;
    System.Random _rng;

    // ── Music ────────────────────────────────────────────────────────────
    static ConcertAudioDirector s_director;
    static int s_directorLookups;
    bool _subscribed;

    // ── Idle ────────────────────────────────────────────────────────────
    // Toggled by AudienceSpawner when the concert stops (e.g. dawn, or a
    // LebronLight kicking the stage to "day"). In idle the audience stops
    // dancing, slerps bones back to rest, and periodically waves the right
    // arm + tracks the player's head — same shape as NPCWaveAnimation but
    // staggered per-instance so the crowd doesn't wave in unison.
    bool _idle;
    bool _idleWaving;
    float _idleWaveTimer;
    float _idleWaveProgress;
    Transform _idlePlayer;
    const float kIdleWaveInterval     = 5f;   // base seconds between waves
    const float kIdleWaveDuration     = 2f;
    // Bumped from 25 → 50 because the audience zone is 40×24, so a player
    // standing at the front of the crowd can be 40+m from members at the back.
    const float kIdleHeadTrackDist    = 50f;
    const float kIdleHeadTurnSpeed    = 3f;
    static readonly Vector3 kIdleHeadRotationOffset = new Vector3(-30f, 90f, -90f);

    public void SetIdleMode(bool idle)
    {
        if (_idle == idle) return;
        _idle = idle;
        if (idle)
        {
            // Stagger first wave so the crowd doesn't ripple in unison the
            // instant the concert ends. _phaseOffset is per-alien.
            _idleWaving = false;
            _idleWaveProgress = 0f;
            _idleWaveTimer = (_phaseOffset / (Mathf.PI * 2f)) * kIdleWaveInterval + 0.5f;
        }
    }

    void Awake()
    {
        // Per-instance RNG seeded from instance id so each alien picks
        // different states without sharing UnityEngine.Random's static stream.
        _rng = new System.Random(GetInstanceID());
        _phaseOffset = (float)_rng.NextDouble() * Mathf.PI * 2f;
        // Stagger which beat each alien hits on. 0..4 covers all four downbeats
        // in a 4/4 bar, so some aliens punch on beats 1/3 while others land on
        // 2/4, exactly the "1+3 vs 3+4" variation the user asked for.
        _beatPhaseOffsetBeats = (float)_rng.NextDouble() * 4f;
        // Subdivision pick — half / normal / double tempo. Even with everyone
        // locked to the song, this makes some aliens slow-bob while others
        // rapid-pump, which is what makes the crowd look like a crowd.
        double r = _rng.NextDouble();
        _beatRateMul = (r < 0.34) ? 0.5f : (r < 0.84 ? 1.0f : 2.0f);
        _stateTimer = NextStateLifetime();
    }

    // Returns "beats elapsed" — synced to the audio director when available,
    // otherwise free-running at fallbackBpm. All sin oscillators below use
    // (this + per-instance beat offset) as their phase clock.
    float SyncedBeatTime()
    {
        if (s_director != null)
            return s_director.BarCount * 4f + s_director.BarPhase * 4f;
        return Time.time * (fallbackBpm / 60f);
    }

    void OnEnable()
    {
        if (!s_all.Contains(this)) s_all.Add(this);
        TrySubscribe();
    }

    void OnDisable()
    {
        s_all.Remove(this);
        Unsubscribe();
    }

    void Start()
    {
        FindBones();
        StartCoroutine(InitRestPose());
    }

    void FindBones()
    {
        _head      = FindDeepChild("head");
        _neck      = FindDeepChild("neck_01");
        _spine     = FindDeepChild("spine_01");
        _pelvis    = FindDeepChild("pelvis");
        _upperArmL = FindDeepChild("upperarm_l");
        _upperArmR = FindDeepChild("upperarm_r");
        _lowerArmL = FindDeepChild("lowerarm_l");
        _lowerArmR = FindDeepChild("lowerarm_r");
        // The alien rigs may not have a `head` bone — fall back to neck so the
        // headbang has something to pitch.
        if (_head == null) _head = _neck;
    }

    IEnumerator InitRestPose()
    {
        // Wait one frame so the Animator has applied its initial pose.
        yield return new WaitForEndOfFrame();

        // Body-down direction from skeleton geometry (same as NPCWaveAnimation:80-84).
        Vector3 bodyDown = Vector3.down;
        if (_pelvis != null && _spine != null)
            bodyDown = (_pelvis.position - _spine.position).normalized;

        // Capture rest rotations and rotate the upper arms down to the sides
        // (T-pose alien rigs ship with arms out). Each arm gets its own raise
        // axis from its own shaft direction so the rotation goes OUTWARD on
        // both sides (same pattern NPCWaveAnimation uses, but we need both
        // arms — that script only ever waved the right).
        if (_upperArmR != null && _lowerArmR != null)
        {
            Vector3 shaftR = (_lowerArmR.position - _upperArmR.position).normalized;
            _armRaiseAxisR = -Vector3.Cross(shaftR, bodyDown).normalized;
            _upperArmRRest = ArmAtSideLocalRot(_upperArmR, _lowerArmR, bodyDown);
            _upperArmR.localRotation = _upperArmRRest;
        }
        if (_upperArmL != null && _lowerArmL != null)
        {
            Vector3 shaftL = (_lowerArmL.position - _upperArmL.position).normalized;
            _armRaiseAxisL = -Vector3.Cross(shaftL, bodyDown).normalized;
            _upperArmLRest = ArmAtSideLocalRot(_upperArmL, _lowerArmL, bodyDown);
            _upperArmL.localRotation = _upperArmLRest;
        }

        yield return null;
        if (_lowerArmR != null) _lowerArmRRest = _lowerArmR.localRotation;
        if (_lowerArmL != null) _lowerArmLRest = _lowerArmL.localRotation;
        if (_head      != null) _headRest      = _head.localRotation;
        // Capture neck rest separately — the idle head-track rotates this
        // bone (not _head) because NPCWaveAnimation's headRotationOffset
        // euler is calibrated for neck_01's local-axis orientation.
        if (_neck      != null) _neckRest      = _neck.localRotation;
        if (_spine     != null) _spineRest     = _spine.localRotation;
        if (_pelvis    != null) { _pelvisRest    = _pelvis.localRotation; _pelvisRestPos = _pelvis.localPosition; }

        // Capture the spawn-time pose for Dance6 (flip jump) to overlay onto.
        _baseLocalPosition = transform.localPosition;
        _baseLocalRotation = transform.localRotation;
        _baseCaptured = true;

        _headSmoothed = _headRest;
        _spineSmoothed = _spineRest;
        _pelvisSmoothed = _pelvisRest;
        _upperArmLSmoothed = _upperArmLRest;
        _upperArmRSmoothed = _upperArmRRest;
        _lowerArmLSmoothed = _lowerArmLRest;
        _lowerArmRSmoothed = _lowerArmRRest;

        _ready = true;
    }

    Quaternion ArmAtSideLocalRot(Transform armBone, Transform forearm, Vector3 bodyDown)
    {
        Vector3 shaft = (forearm.position - armBone.position).normalized;
        Quaternion world = Quaternion.FromToRotation(shaft, bodyDown) * armBone.rotation;
        return Quaternion.Inverse(armBone.parent.rotation) * world;
    }

    void Update()
    {
        // Director may not exist when this member spawned — keep retrying
        // cheaply until we get it.
        if (!_subscribed) TrySubscribe();

        if (!_ready) return;

        if (_idle)
        {
            TickIdleWaveTimer();
            return; // skip the dance state machine entirely while idle
        }

        // Force into ArmsUpFlick if a recent drop/crash set _armsUpUntil.
        if (Time.time < _armsUpUntil)
        {
            if (_state != DanceState.ArmsUpFlick) _state = DanceState.ArmsUpFlick;
        }
        else if (_state == DanceState.ArmsUpFlick)
        {
            // Arms-up window expired — fall back to a default state.
            PickRandomDefaultState();
            _stateTimer = NextStateLifetime();
        }

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f && _state != DanceState.ArmsUpFlick)
        {
            PickRandomDefaultState();
            _stateTimer = NextStateLifetime();
        }
    }

    void PickRandomDefaultState()
    {
        // Weighted: headbang (arms-up + fist-pump) dominant, six dances less common.
        // Dance6 (flips) is rarest because it's the most attention-grabbing.
        double r = _rng.NextDouble();
        if      (r < 0.50) _state = DanceState.Headbang;
        else if (r < 0.60) _state = DanceState.Dance1;
        else if (r < 0.70) _state = DanceState.Dance2;
        else if (r < 0.80) _state = DanceState.Dance3;
        else if (r < 0.88) _state = DanceState.Dance4;
        else if (r < 0.95) _state = DanceState.Dance5;
        else               _state = DanceState.Dance6;
    }

    float NextStateLifetime() => Mathf.Lerp(stateChangeMin, stateChangeMax, (float)_rng.NextDouble());

    void TickIdleWaveTimer()
    {
        if (!_idleWaving)
        {
            _idleWaveTimer -= Time.deltaTime;
            if (_idleWaveTimer <= 0f)
            {
                _idleWaving = true;
                _idleWaveProgress = 0f;
            }
        }
        else
        {
            _idleWaveProgress += Time.deltaTime;
            if (_idleWaveProgress >= kIdleWaveDuration)
            {
                _idleWaving = false;
                // Randomize next interval per-alien so the crowd stays
                // decorrelated — kIdleWaveInterval to 2× kIdleWaveInterval.
                _idleWaveTimer = Mathf.Lerp(kIdleWaveInterval, kIdleWaveInterval * 2f, (float)_rng.NextDouble());
            }
        }
    }

    // ── Distance culling ───────────────────────────────────────────
    // Unity's frustum culling can't see through the planet — when the
    // player points the camera toward a concert from the other side of
    // the planet, the audience's bounding boxes are still inside the
    // camera frustum, so every SkinnedMeshRenderer submits a draw call
    // and pays its full vertex-shading cost before the depth test rejects
    // those pixels against the planet's surface. With 40 audience members
    // that was ~1-3 ms of GPU work per frame for invisible content,
    // costing 10-20 FPS when looking in the concert's direction.
    //
    // Fix: when the camera is farther than CullDistanceMeters from this
    // audience member, disable its child Renderers + Animator outright.
    // Re-enables when the player approaches. Cached refs + 0.5 s throttled
    // check so the per-frame overhead is one sqr-distance compare.
    const float CullDistanceMeters = 100f;
    const float CullCheckInterval  = 0.5f;
    static Camera _sharedCam;
    Renderer[] _cullCachedRenderers;
    Animator   _cullCachedAnimator;
    float      _cullNextCheck;
    bool       _cullActive;

    void TickDistanceCull()
    {
        if (_sharedCam == null) _sharedCam = Camera.main;
        if (_sharedCam == null) return;
        if (Time.unscaledTime < _cullNextCheck) return;
        _cullNextCheck = Time.unscaledTime + CullCheckInterval;

        if (_cullCachedRenderers == null) _cullCachedRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        if (_cullCachedAnimator  == null) _cullCachedAnimator  = GetComponentInChildren<Animator>(includeInactive: true);

        float sqrDist = (_sharedCam.transform.position - transform.position).sqrMagnitude;
        bool shouldCull = sqrDist > CullDistanceMeters * CullDistanceMeters;
        if (shouldCull == _cullActive) return;
        _cullActive = shouldCull;

        bool enabled = !shouldCull;
        for (int i = 0; i < _cullCachedRenderers.Length; i++)
            if (_cullCachedRenderers[i] != null) _cullCachedRenderers[i].enabled = enabled;
        if (_cullCachedAnimator != null) _cullCachedAnimator.enabled = enabled;
    }

    // LateUpdate writes win over the Animator (per CLAUDE.md / NPCWaveAnimation).
    void LateUpdate()
    {
        if (!_ready) return;
        TickDistanceCull();
        // When culled by distance, skip ALL bone math — saves CPU on top of
        // the GPU work the renderer-disable already saved. Comes back to
        // life the next time the player gets within CullDistanceMeters.
        if (_cullActive) return;

        if (_idle)
        {
            DoIdleLateUpdate();
            return;
        }

        // Per-instance phased time used by every oscillator below — measured
        // in BEATS (not seconds), synced to ConcertAudioDirector when present.
        // Rate constants below are "cycles per beat".
        float t = SyncedBeatTime() + _beatPhaseOffsetBeats;

        // Pre-compute Dance6 lobe (which flip kind we're in this cycle) so
        // both the arm-pose switch case and the post-bone transform overlay
        // can read it. 0 = backflip, 1 = frontflip, 2 = cartwheel/spin.
        // Dance6 runs on a free-running real-time clock — locking flips to
        // the song beat made them look mechanical. Phase offset per-alien
        // still desyncs the crowd.
        int dance6LobeIdx = 0;
        float dance6Lobe01 = 0f;
        if (_state == DanceState.Dance6)
        {
            float dance6T = (Time.time + _phaseOffset) / Mathf.Max(0.5f, flipCycleSeconds);
            dance6Lobe01 = Mathf.Repeat(dance6T, 1f);
            dance6LobeIdx = ((int)Mathf.Floor(dance6T)) % 3;
            if (dance6LobeIdx < 0) dance6LobeIdx += 3;
        }

        // ── Compute target local rotations for this frame ────────────────
        Quaternion headTarget    = _headRest;
        Quaternion spineTarget   = _spineRest;
        Quaternion pelvisTarget  = _pelvisRest;
        Quaternion uArmLTarget   = _upperArmLRest;
        Quaternion uArmRTarget   = _upperArmRRest;
        Quaternion lArmLTarget   = _lowerArmLRest;
        Quaternion lArmRTarget   = _lowerArmRRest;
        Vector3 pelvisPosTarget  = _pelvisRestPos;

        // Headbang amplitude is shared across states (always-on subtle bob)
        // and pumped harder in the Headbang state. Kick punches add a fast
        // transient on top.
        float kickEnvelope = 0f;
        if (s_director != null) kickEnvelope = s_director.Kick;
        if (Time.time < _kickPunchUntil)
        {
            float remaining = (_kickPunchUntil - Time.time) / 0.18f;
            kickEnvelope = Mathf.Max(kickEnvelope, _kickPunchStrength * Mathf.Clamp01(remaining));
        }

        switch (_state)
        {
            case DanceState.Headbang:
            {
                // DEFAULT STATE: arms up overhead with forearms pumping forward/back
                // (fist pump), and head pitching down on the kick. The pitch is in
                // world space (around the alien's transform.right) — rotating the
                // bone's local X just spins the head left/right because the rig's
                // local axes don't align with "nodding".
                float pitch = Mathf.Sin(t * Mathf.PI * 2f * (headbangCyclesPerBeat * _beatRateMul)) * headbangAngle * 0.6f;
                pitch += kickEnvelope * headbangAngle * 0.7f; // strong nod-down on the kick
                headTarget  = WorldPitchedLocal(_head,  _headRest,  pitch);
                spineTarget = WorldPitchedLocal(_spine, _spineRest, pitch * 0.25f);

                if (_upperArmR != null) uArmRTarget = LocalRaiseRotation(_upperArmR, _upperArmRRest, armsUpAngle, _armRaiseAxisR);
                if (_upperArmL != null) uArmLTarget = LocalRaiseRotation(_upperArmL, _upperArmLRest, armsUpAngle, _armRaiseAxisL);

                // Forearm fist pump in WORLD space (rotate around the alien's
                // right axis), then convert to bone-local. Doing this in
                // bone-local space rotated whatever axis happened to be local-X,
                // which on this rig was the bone shaft — so the forearm spun on
                // its own axis instead of bending at the elbow.
                float pump = Mathf.Sin(t * Mathf.PI * 2f * (headbangCyclesPerBeat * _beatRateMul));
                pump += kickEnvelope * 0.6f; // bigger pump on the kick
                float pumpDir = invertFistPump ? -1f : 1f;
                lArmRTarget = WorldRotatedLocal(_lowerArmR, _lowerArmRRest, transform.right, pump * fistPumpAngle * pumpDir);
                lArmLTarget = WorldRotatedLocal(_lowerArmL, _lowerArmLRest, transform.right, pump * fistPumpAngle * pumpDir);
                break;
            }
            case DanceState.ArmsUpFlick:
            {
                // Drop / crash response — arms up, faster pump, head tilted back.
                float pump = Mathf.Sin(t * Mathf.PI * 2f * 3f);
                if (_upperArmR != null) uArmRTarget = LocalRaiseRotation(_upperArmR, _upperArmRRest, armsUpAngle, _armRaiseAxisR);
                if (_upperArmL != null) uArmLTarget = LocalRaiseRotation(_upperArmL, _upperArmLRest, armsUpAngle, _armRaiseAxisL);
                float pumpDir = invertFistPump ? -1f : 1f;
                lArmRTarget = WorldRotatedLocal(_lowerArmR, _lowerArmRRest, transform.right, pump * fistPumpAngle * 1.4f * pumpDir);
                lArmLTarget = WorldRotatedLocal(_lowerArmL, _lowerArmLRest, transform.right, pump * fistPumpAngle * 1.4f * pumpDir);
                headTarget  = WorldPitchedLocal(_head, _headRest, -15f);
                break;
            }
            case DanceState.Dance1: // sway: hip yaw + shoulder shimmy
            {
                float sway = Mathf.Sin(t * Mathf.PI * 2f * (danceCyclesPerBeat * _beatRateMul));
                pelvisTarget = _pelvisRest * Quaternion.Euler(0f, sway * swayHipAngle, 0f);
                spineTarget  = _spineRest  * Quaternion.Euler(0f, -sway * swayHipAngle * 0.5f, 0f);
                lArmRTarget  = _lowerArmRRest * Quaternion.Euler(0f, sway * shoulderShimmy, 0f);
                lArmLTarget  = _lowerArmLRest * Quaternion.Euler(0f, -sway * shoulderShimmy, 0f);
                float pitch = Mathf.Sin(t * Mathf.PI * 2f * (headbangCyclesPerBeat * _beatRateMul) * 0.8f) * headbangAngle * 0.3f;
                headTarget = WorldPitchedLocal(_head, _headRest, pitch);
                break;
            }
            case DanceState.Dance2: // bob: pelvis pitch + alternating arm swing
            {
                float bob = Mathf.Sin(t * Mathf.PI * 2f * (danceCyclesPerBeat * _beatRateMul) * 1.1f);
                pelvisTarget = _pelvisRest * Quaternion.Euler(bob * bobPelvisPitch, 0f, 0f);
                lArmRTarget  = _lowerArmRRest * Quaternion.Euler(bob * 25f, 0f, 0f);
                lArmLTarget  = _lowerArmLRest * Quaternion.Euler(-bob * 25f, 0f, 0f);
                float pitch = Mathf.Sin(t * Mathf.PI * 2f * (headbangCyclesPerBeat * _beatRateMul)) * headbangAngle * 0.4f;
                headTarget = WorldPitchedLocal(_head, _headRest, pitch);
                spineTarget = _spineRest * Quaternion.Euler(bob * bobPelvisPitch * 0.5f, 0f, 0f);
                break;
            }
            case DanceState.Dance3: // point-and-step: right arm raised forward, left shoulder bobs
            {
                float pulse = Mathf.Sin(t * Mathf.PI * 2f * (danceCyclesPerBeat * _beatRateMul));
                if (_upperArmR != null)
                    uArmRTarget = LocalRaiseRotation(_upperArmR, _upperArmRRest, armsUpAngle * 0.55f, _armRaiseAxisR);
                lArmRTarget = _lowerArmRRest * Quaternion.Euler(0f, 30f + pulse * 10f, 0f);
                pelvisTarget = _pelvisRest * Quaternion.Euler(0f, pulse * swayHipAngle * 0.5f, 0f);
                lArmLTarget  = _lowerArmLRest * Quaternion.Euler(pulse * 30f, 0f, 0f);
                float pitch = Mathf.Sin(t * Mathf.PI * 2f * (headbangCyclesPerBeat * _beatRateMul) * 0.7f) * headbangAngle * 0.3f;
                headTarget = WorldPitchedLocal(_head, _headRest, pitch);
                break;
            }
            case DanceState.Dance4: // jumping: pelvis hops up + both arms up
            {
                // Half-rectified sin gives a "ground → peak → ground" hop shape
                // (no negative excursion, so the alien doesn't sink into the floor).
                float jumpPhase = Mathf.Sin(t * Mathf.PI * 2f * (jumpCyclesPerBeat * _beatRateMul));
                float hop = Mathf.Max(0f, jumpPhase) * jumpHeight;
                // Convert world-up into pelvis-parent-local space so the hop is
                // truly vertical regardless of the rig's bone orientation. (Bone
                // local +Y on this rig points forward, which is why a naive
                // `Vector3.up * hop` produced a forward-back lurch.)
                Vector3 localUp = (_pelvis != null && _pelvis.parent != null)
                    ? _pelvis.parent.InverseTransformDirection(transform.up)
                    : Vector3.up;
                pelvisPosTarget = _pelvisRestPos + localUp * hop;

                if (_upperArmR != null) uArmRTarget = LocalRaiseRotation(_upperArmR, _upperArmRRest, armsUpAngle, _armRaiseAxisR);
                if (_upperArmL != null) uArmLTarget = LocalRaiseRotation(_upperArmL, _upperArmLRest, armsUpAngle, _armRaiseAxisL);

                // Brace pose on landing — small forward lean of the spine, and
                // the head looking forward.
                float lean = Mathf.Max(0f, -jumpPhase) * 12f;
                spineTarget = _spineRest * Quaternion.Euler(lean, 0f, 0f);
                headTarget  = WorldPitchedLocal(_head, _headRest, -8f * Mathf.Max(0f, jumpPhase));
                break;
            }
            case DanceState.Dance5: // weird pose: held twist + slow drift
            {
                // Pick a deterministic "weird" stance from the per-instance RNG so
                // each alien lands in a different contortion. Slow sin drift so it
                // doesn't look frozen.
                float drift = Mathf.Sin(t * Mathf.PI * 2f * 0.4f);
                float twist = weirdSpineTwist + drift * 6f;
                float lean  = weirdPelvisLean + drift * 4f;

                // Direction baked from the phase offset so half the crowd twists
                // left, half right.
                float dir = (_phaseOffset > Mathf.PI) ? 1f : -1f;

                spineTarget  = _spineRest  * Quaternion.Euler(0f, twist * dir, lean * 0.5f * dir);
                pelvisTarget = _pelvisRest * Quaternion.Euler(lean * dir, -twist * 0.4f * dir, 0f);

                // Arms in awkward angles: one half-raised, one out to the side.
                if (_upperArmR != null) uArmRTarget = LocalRaiseRotation(_upperArmR, _upperArmRRest, armsUpAngle * 0.7f, _armRaiseAxisR);
                lArmRTarget = _lowerArmRRest * Quaternion.Euler(40f, 0f, 60f);
                lArmLTarget = _lowerArmLRest * Quaternion.Euler(-30f, 50f, 0f);
                headTarget  = WorldPitchedLocal(_head, _headRest, 18f * dir);
                break;
            }
            case DanceState.Dance6: // flip jump: backflip / frontflip / cartwheel cycle
            {
                // The flip rotation + airborne hop are applied at the bottom
                // of LateUpdate. Here we just set the body POSE for whichever
                // lobe we're in: tucked-arms-up for the two flips, starfish-
                // arms-out for the cartwheel/spin.
                if (dance6LobeIdx == 2)
                {
                    // CARTWHEEL / vertical spin — arms out wide horizontally
                    // (~90° from rest), straight forearms, body upright.
                    if (_upperArmR != null) uArmRTarget = LocalRaiseRotation(_upperArmR, _upperArmRRest, cartwheelArmSpread, _armRaiseAxisR);
                    if (_upperArmL != null) uArmLTarget = LocalRaiseRotation(_upperArmL, _upperArmLRest, cartwheelArmSpread, _armRaiseAxisL);
                    lArmRTarget = _lowerArmRRest;
                    lArmLTarget = _lowerArmLRest;
                    spineTarget = _spineRest; // upright body, no tuck
                }
                else
                {
                    // BACKFLIP / FRONTFLIP — arms overhead, slight forward tuck.
                    if (_upperArmR != null) uArmRTarget = LocalRaiseRotation(_upperArmR, _upperArmRRest, armsUpAngle, _armRaiseAxisR);
                    if (_upperArmL != null) uArmLTarget = LocalRaiseRotation(_upperArmL, _upperArmLRest, armsUpAngle, _armRaiseAxisL);
                    lArmRTarget = _lowerArmRRest;
                    lArmLTarget = _lowerArmLRest;
                    spineTarget = _spineRest * Quaternion.Euler(15f, 0f, 0f);
                }
                break;
            }
        }

        // ── Slerp from smoothed-current to target each frame ─────────────
        float a = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        _headSmoothed       = Quaternion.Slerp(_headSmoothed,       headTarget,   a);
        _spineSmoothed      = Quaternion.Slerp(_spineSmoothed,      spineTarget,  a);
        _pelvisSmoothed     = Quaternion.Slerp(_pelvisSmoothed,     pelvisTarget, a);
        _upperArmLSmoothed  = Quaternion.Slerp(_upperArmLSmoothed,  uArmLTarget,  a);
        _upperArmRSmoothed  = Quaternion.Slerp(_upperArmRSmoothed,  uArmRTarget,  a);
        _lowerArmLSmoothed  = Quaternion.Slerp(_lowerArmLSmoothed,  lArmLTarget,  a);
        _lowerArmRSmoothed  = Quaternion.Slerp(_lowerArmRSmoothed,  lArmRTarget,  a);

        if (_head      != null) _head.localRotation      = _headSmoothed;
        if (_spine     != null) _spine.localRotation     = _spineSmoothed;
        if (_pelvis    != null)
        {
            _pelvis.localRotation = _pelvisSmoothed;
            // Lerp the position too so the jump dance blends in/out cleanly when
            // entering/leaving Dance4.
            _pelvis.localPosition = Vector3.Lerp(_pelvis.localPosition, pelvisPosTarget, a);
        }
        if (_upperArmL != null) _upperArmL.localRotation = _upperArmLSmoothed;
        if (_upperArmR != null) _upperArmR.localRotation = _upperArmRSmoothed;
        if (_lowerArmL != null) _lowerArmL.localRotation = _lowerArmLSmoothed;
        if (_lowerArmR != null) _lowerArmR.localRotation = _lowerArmRSmoothed;

        // ── Whole-alien transform overlay (Dance6 flip jump) ──────────────
        // Lerp the alien's local pose toward base + flip-delta. Doing it
        // every frame (with delta=identity outside Dance6) means leaving
        // Dance6 mid-flip smoothly returns to the spawn-time facing.
        if (_baseCaptured)
        {
            Vector3 deltaLocalPos = Vector3.zero;
            Quaternion deltaLocalRot = Quaternion.identity;

            if (_state == DanceState.Dance6)
            {
                // Lobe phase shape: prep crouch → airborne → landing → rest.
                // The crouch + rest bookends sell the motion as a deliberate
                // gymnast leap rather than a continuous spin.
                float prepEnd = (1f - flipAirborneFraction) * 0.5f;
                float airEnd  = prepEnd + flipAirborneFraction;
                float landEnd = airEnd + (1f - flipAirborneFraction) * 0.25f;

                float airborneT = 0f;     // 0..1 over the airborne window
                float prepDip   = 0f;     // 0..1 small downward bend before launch
                float landBend  = 0f;     // 0..1 small downward bend after landing

                if (dance6Lobe01 < prepEnd)
                {
                    prepDip = Mathf.Sin((dance6Lobe01 / prepEnd) * Mathf.PI);
                }
                else if (dance6Lobe01 < airEnd)
                {
                    airborneT = (dance6Lobe01 - prepEnd) / Mathf.Max(0.0001f, airEnd - prepEnd);
                }
                else if (dance6Lobe01 < landEnd)
                {
                    landBend = Mathf.Sin(((dance6Lobe01 - airEnd) / Mathf.Max(0.0001f, landEnd - airEnd)) * Mathf.PI);
                }
                // else: rest phase, everything zeroed.

                float hop = Mathf.Sin(airborneT * Mathf.PI) * flipJumpHeight
                          - 0.15f * flipJumpHeight * (prepDip + landBend); // small dip on prep + land
                float flipAngle = airborneT * 360f;

                // Pick the rotation axis (in alien-local space, applied AFTER
                // _baseLocalRotation so it's relative to the spawned facing).
                // Cartwheel / spin lobe rotates around the BODY-UP axis — a
                // vertical pirouette while in the starfish pose. (A real
                // around-forward-axis cartwheel had the alien rolling sideways
                // while spinning, which doesn't read well in a dense crowd.)
                Vector3 axisLocal;
                float dirSign;
                switch (dance6LobeIdx)
                {
                    default:
                    case 0: axisLocal = Vector3.right;   dirSign =  1f; break; // backflip
                    case 1: axisLocal = Vector3.right;   dirSign = -1f; break; // frontflip
                    case 2: axisLocal = Vector3.up;      dirSign =  1f; break; // cartwheel = vertical spin
                }
                deltaLocalRot = Quaternion.AngleAxis(flipAngle * dirSign, axisLocal);

                // Hop along the alien's BASE local up axis (gravity-up) so the
                // body lifts straight up, not in whatever direction the spinning
                // body's current up happens to be.
                deltaLocalPos = (_baseLocalRotation * Vector3.up) * hop;
            }

            transform.localPosition = Vector3.Lerp(transform.localPosition, _baseLocalPosition + deltaLocalPos, a);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, _baseLocalRotation * deltaLocalRot, a);
        }
    }

    // Idle rendering: slerp all dance bones back to rest, run an NPCWaveAnimation-
    // style right-arm wave on a per-alien timer, and head-track the player
    // when within range. Skips the Dance6 transform overlay (which would
    // teleport a still-mid-flip alien back to base).
    void DoIdleLateUpdate()
    {
        float a = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);

        // Pelvis hop offset from Dance4 → slerp back to rest, plus
        // transform.localPosition / rotation from Dance6.
        if (_pelvis != null) _pelvis.localPosition = Vector3.Lerp(_pelvis.localPosition, _pelvisRestPos, a);
        if (_baseCaptured)
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition, _baseLocalPosition, a);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, _baseLocalRotation, a);
        }

        // All bones default to rest; we'll override the right arm if waving.
        Quaternion headTarget   = _headRest;
        Quaternion spineTarget  = _spineRest;
        Quaternion pelvisTarget = _pelvisRest;
        Quaternion uArmLTarget  = _upperArmLRest;
        Quaternion uArmRTarget  = _upperArmRRest;
        Quaternion lArmLTarget  = _lowerArmLRest;
        Quaternion lArmRTarget  = _lowerArmRRest;

        // Right-arm wave: phased prep/wave/return (same shape as
        // NPCWaveAnimation). 0-20% raise, 20-80% swing, 80-100% lower.
        if (_idleWaving && _upperArmR != null && _lowerArmR != null)
        {
            float t = _idleWaveProgress / kIdleWaveDuration;
            float raiseBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.2f));
            float lowerBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.8f) / 0.2f));
            float armBlend   = raiseBlend * (1f - lowerBlend);

            Quaternion raised = LocalRaiseRotation(_upperArmR, _upperArmRRest, 121f, _armRaiseAxisR);
            uArmRTarget = Quaternion.Slerp(_upperArmRRest, raised, armBlend);

            if (t >= 0.2f && t <= 0.8f)
            {
                float waveT     = (t - 0.2f) / 0.6f;
                float waveAngle = Mathf.Sin(waveT * Mathf.PI * 3 * 2f) * 40f; // 3 swings, ±40°
                lArmRTarget = _lowerArmRRest * Quaternion.Euler(0f, waveAngle, 0f);
            }
        }

        _headSmoothed       = Quaternion.Slerp(_headSmoothed,      headTarget,   a);
        _spineSmoothed      = Quaternion.Slerp(_spineSmoothed,     spineTarget,  a);
        _pelvisSmoothed     = Quaternion.Slerp(_pelvisSmoothed,    pelvisTarget, a);
        _upperArmLSmoothed  = Quaternion.Slerp(_upperArmLSmoothed, uArmLTarget,  a);
        _upperArmRSmoothed  = Quaternion.Slerp(_upperArmRSmoothed, uArmRTarget,  a);
        _lowerArmLSmoothed  = Quaternion.Slerp(_lowerArmLSmoothed, lArmLTarget,  a);
        _lowerArmRSmoothed  = Quaternion.Slerp(_lowerArmRSmoothed, lArmRTarget,  a);

        if (_head      != null) _head.localRotation      = _headSmoothed;
        if (_spine     != null) _spine.localRotation     = _spineSmoothed;
        if (_pelvis    != null) _pelvis.localRotation    = _pelvisSmoothed;
        if (_upperArmL != null) _upperArmL.localRotation = _upperArmLSmoothed;
        if (_upperArmR != null) _upperArmR.localRotation = _upperArmRSmoothed;
        if (_lowerArmL != null) _lowerArmL.localRotation = _lowerArmLSmoothed;
        if (_lowerArmR != null) _lowerArmR.localRotation = _lowerArmRSmoothed;

        // Head look-at overrides _head.localRotation after the slerp above
        // (so the player can see the alien turn to face them, regardless of
        // whatever rest pose the slerp landed on).
        UpdateIdleHeadLookAt();
    }

    // NPCWaveAnimation-style head tracking: rotates the NECK bone (not the
    // head bone) because the kIdleHeadRotationOffset euler is calibrated
    // for neck_01's local-axis orientation. The head bone naturally
    // inherits the neck's rotation through the hierarchy, so the visible
    // result is the head turning toward the player.
    void UpdateIdleHeadLookAt()
    {
        if (_neck == null) return;
        if (_idlePlayer == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) _idlePlayer = p.transform;
            else return;
        }

        float dist = Vector3.Distance(transform.position, _idlePlayer.position);
        Quaternion target = _neckRest;

        if (dist <= kIdleHeadTrackDist)
        {
            Vector3 dir = (_idlePlayer.position - _neck.position).normalized;
            Quaternion worldRot = Quaternion.LookRotation(dir, transform.up);
            target = Quaternion.Inverse(_neck.parent.rotation) * worldRot * Quaternion.Euler(kIdleHeadRotationOffset);
        }

        _neck.localRotation = Quaternion.Slerp(_neck.localRotation, target, kIdleHeadTurnSpeed * Time.deltaTime);
    }

    // World-space pitch around the alien's right axis. Avoids the rig's bone
    // local-axis ambiguity that made `_headRest * Quaternion.Euler(pitch, 0, 0)`
    // produce a left-right shake instead of an up-down nod.
    Quaternion WorldPitchedLocal(Transform bone, Quaternion restLocal, float pitchDeg)
        => WorldRotatedLocal(bone, restLocal, transform.right, pitchDeg);

    // General world-space rotation: take the bone's rest pose, rotate it in
    // world space around `worldAxis` by `angleDeg`, convert back to local. Used
    // by both the head pitch and the forearm fist pump so the motion is the
    // same in world space regardless of how the rig's bone local axes happen
    // to be oriented.
    Quaternion WorldRotatedLocal(Transform bone, Quaternion restLocal, Vector3 worldAxis, float angleDeg)
    {
        if (bone == null) return restLocal;
        Quaternion worldRest = bone.parent.rotation * restLocal;
        Quaternion worldRotated = Quaternion.AngleAxis(angleDeg, worldAxis) * worldRest;
        return Quaternion.Inverse(bone.parent.rotation) * worldRotated;
    }

    // Build a local rotation that raises the given arm by `angleDeg` along the
    // raise axis appropriate to that arm. Caller passes the per-arm axis
    // because the left and right shafts mirror each other and need opposite
    // rotation axes to both swing OUT to their sides.
    Quaternion LocalRaiseRotation(Transform arm, Quaternion restLocal, float angleDeg, Vector3 raiseAxis)
    {
        Quaternion worldRest   = arm.parent.rotation * restLocal;
        Quaternion worldRaised = Quaternion.AngleAxis(angleDeg, raiseAxis) * worldRest;
        return Quaternion.Inverse(arm.parent.rotation) * worldRaised;
    }

    // ── Music sync ───────────────────────────────────────────────────────
    void TrySubscribe()
    {
        if (_subscribed) return;
        if (s_director == null)
        {
            // Throttle the lookup so missing-singleton scenes don't pay it
            // every frame across every audience member.
            if (s_directorLookups++ % 30 == 0)
                s_director = ConcertAudioDirector.Instance;
            if (s_director == null) return;
        }
        s_director.OnKick  += HandleKick;
        s_director.OnDrop  += HandleDrop;
        s_director.OnCrash += HandleCrash;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed || s_director == null) return;
        s_director.OnKick  -= HandleKick;
        s_director.OnDrop  -= HandleDrop;
        s_director.OnCrash -= HandleCrash;
        _subscribed = false;
    }

    void HandleKick()
    {
        _kickPunchUntil    = Time.time + 0.18f;
        _kickPunchStrength = 1f;
    }

    void HandleDrop()
    {
        _armsUpUntil = Time.time + Mathf.Lerp(armsUpDurationMin, armsUpDurationMax, (float)_rng.NextDouble());
    }

    void HandleCrash()
    {
        // 50/50: arms-up or kick to a random dance (decorrelates the crowd).
        if (_rng.NextDouble() < 0.5)
        {
            _armsUpUntil = Time.time + Mathf.Lerp(armsUpDurationMin, armsUpDurationMax, (float)_rng.NextDouble());
        }
        else
        {
            DanceState pick;
            double r = _rng.NextDouble();
            if      (r < 0.18) pick = DanceState.Dance1;
            else if (r < 0.36) pick = DanceState.Dance2;
            else if (r < 0.54) pick = DanceState.Dance3;
            else if (r < 0.72) pick = DanceState.Dance4;
            else if (r < 0.88) pick = DanceState.Dance5;
            else               pick = DanceState.Dance6;
            _state = pick;
            _stateTimer = NextStateLifetime();
        }
    }

    Transform FindDeepChild(string childName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }
}
