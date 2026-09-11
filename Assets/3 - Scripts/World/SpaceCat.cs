using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A cat. Lounges, gets curious, strolls a bit, and will trade you a fishing
/// perk for any fish (<see cref="CatPerkTradeUI"/>).
///
/// Locomotion is <see cref="AlienWander"/> — the same proven planet-surface
/// walker the aliens use, with its procedural leg swing switched off because
/// these rigs have real clips. This component is only the BRAIN: it decides
/// when to hold still and loaf versus when to let the wander stroll, and tells
/// <see cref="CatAnimation"/> which pose that means.
///
/// The split matters: everything about walking on a sphere, wading, refusing
/// cliffs and staying leashed to the spawn cell is already solved in
/// AlienWander and is not re-litigated here.
///
/// Added at runtime by <see cref="CatSpawner"/>; nothing here is prefab-serialized.
/// </summary>
public class SpaceCat : Interactable
{
    // Sam's spec, 2026-09-10: "cats are curious and walk around and then sit and
    // clean themselves or loaf and then get up and walk somewheres else", ~30%
    // of the time moving — and, separately: "i never see them loafing and i
    // wanna see them loaf".
    //
    // Two beats, TRAVEL then SETTLE, forever. Travel 9-17s (avg 13) against
    // settle 22-46s (avg 34) puts moving at ~28%.
    // Retuned 2026-09-11. Sam: "they need to walk around and walk a few meters
    // and then stop and sit, then maybe loaf or do other things for a while and
    // then get up and move to a different spot and do it again."
    // Shorter legs, more of them: at ~1.6 m/s a travel beat covers roughly
    // 13-24 m — a few metres and a bit — and settles are half what they were,
    // which puts moving at ~40% instead of ~28%.
    const float TravelMin = 8f,  TravelMax = 15f;
    // A settle has no fixed length any more — BuildSettlePlan decides it.


    /// Inside this distance the cat notices you.
    ///
    /// ⚠️ A SETTLED cat does NOT get up for you — it glances and stays down.
    /// Cats ignore people, and more practically: making them react was the
    /// reason walking over to watch one always stopped the thing worth watching.
    /// Only a cat mid-walk stops for you.
    const float NoticeDistance = 5f;
    const float NoticeSeconds  = 5f;

    AlienWander _wander;
    CatAnimation _anim;
    Transform _player;
    float _stateUntil;
    float _noticeUntil;
    bool _noticing;
    float _refindPlayerAt;
    bool _travelFast;
    bool _heldByUI;

    // Settle-beat state: a short ordered plan per stop.
    readonly List<CatAnimation.Pose> _plan = new List<CatAnimation.Pose>();
    readonly List<float> _planHold = new List<float>();
    int _planStep = -1;
    float _stepUntil;
    // Smoothed planet-local speed in m/s, which drives the walk pose.
    float _speed;

    enum Mood { Travel, Settle }
    Mood _mood = Mood.Settle;

    public void Bind(AlienWander wander, CatAnimation anim)
    {
        _wander = wander;
        _anim = anim;
        _noticing = false;
        _noticeUntil = 0f;
        // Stagger the herd: without this every cat spawned in the same frame
        // marches in lockstep, which reads as clockwork rather than as cats.
        _stateUntil = Time.time + Random.Range(0f, 6f);
        ArmWatchdog();
        EnterMood(Random.value < 0.3f ? Mood.Travel : Mood.Settle);
    }

    protected override void Update()
    {
        base.Update();      // F polling + prompt ownership (and the gaze outline)

        if (_anim == null) return;

        // The trade UI owns the cat while it is open: hold still, sit up, look
        // interested. Nothing worse than haggling with a cat that walks off.
        if (CatPerkTradeUI.IsOpen && CatPerkTradeUI.Target == this)
        {
            if (_wander != null) _wander.Hold = true;
            _anim.Play(CatAnimation.Pose.Sit, 0.3f);
            _heldByUI = true;
            return;
        }
        if (_heldByUI)
        {
            // The panel held this cat still; hand it back to its own beat rather
            // than leaving it frozen until the next one happens to come round.
            _heldByUI = false;
            _stateUntil = 0f;
        }

        ResolvePlayer();
        bool near = _player != null &&
                    (_player.position - transform.position).sqrMagnitude < NoticeDistance * NoticeDistance;

        if (near && !_noticing && Time.time >= _noticeUntil)
        {
            _noticing = true;
            _noticeUntil = Time.time + NoticeSeconds;
            if (_mood == Mood.Travel)
            {
                // Mid-walk: stop and watch you.
                if (_wander != null) _wander.Hold = true;
                _anim.Play(CatAnimation.Pose.Look, 0.3f);
            }
            else
            {
                // Already settled: a glance, nothing more. It stays where it is
                // and picks its plan back up when it loses interest.
                _anim.Play(CatAnimation.Pose.Look, 0.35f);
                _stepUntil = Time.time + NoticeSeconds;
            }
        }
        if (_noticing && (Time.time >= _noticeUntil || !near))
        {
            _noticing = false;
            _noticeUntil = Time.time + 6f;      // cool-off, so it can't re-trigger instantly
            // Only a cat that was STOPPED mid-walk needs a fresh beat; a settled
            // one just carries on loafing where it left off.
            if (_mood == Mood.Travel) _stateUntil = 0f;
        }

        // A travelling cat that is watching you stays put until the notice ends.
        if (_noticing && _mood == Mood.Travel) return;

        if (Time.time >= _stateUntil)
            EnterMood(_mood == Mood.Travel ? Mood.Settle : Mood.Travel);

        if (_mood == Mood.Travel)
        {
            // Keyed off MEASURED SPEED, not AlienWander.IsMoving. IsMoving is a
            // per-frame flag that flickers -- the walker does not step on every
            // single frame, and its Update order against this one is undefined --
            // so the pose flickered between Walk and Idle through a 0.22s
            // crossfade, which is why Sam saw a cat "standing straight then just
            // slid foward". Smoothed displacement cannot flicker.
            bool moving = _speed > 0.35f;
            _anim.Play(moving ? (_travelFast ? CatAnimation.Pose.Trot : CatAnimation.Pose.Walk)
                              : CatAnimation.Pose.Idle, 0.25f);
            return;
        }

        // -- settled: work through this stop's little plan --
        if (Time.time < _stepUntil) return;
        AdvanceSettlePlan();
    }

    /// One stop's worth of behaviour, built fresh each time the cat arrives.
    ///
    /// Sam, 2026-09-11: "walk around and then stop and do a few emotes and maybe
    /// loaf for a bit and then yawn and get back up and stretch and walk and find
    /// a new spot and emote over there" -- and separately that it loafed far too
    /// much: "sit up, then loaf, then sit up, then loaf... like 5 times before
    /// walking".
    ///
    /// The old version held ONE loaf pose for the whole stop and stirred out of
    /// it and back again, which IS that oscillation. A stop is now a short
    /// ordered plan -- arrive, a couple of emotes, MAYBE a loaf, a stretch to
    /// get up -- and only about a third of stops contain a loaf at all.
    void BuildSettlePlan()
    {
        _plan.Clear();
        _planHold.Clear();

        // Sam, 2026-09-11: "the cat should at most do 2 different animations
        // before yawning and walking somewheres else". He was watching a cat
        // pass through the standing Idle between every pose, so five poses read
        // as ten. So: AT MOST TWO poses per stop, then the stretch, then go.
        //
        // Poses are also held much longer. A cat that sits for eight seconds
        // reads as a cat; one that sits for three reads as a glitch.
        int count = Random.value < 0.45f ? 1 : 2;
        for (int i = 0; i < count; i++)
        {
            float k = Random.value;
            CatAnimation.Pose pose;
            float hold;
            if (k < 0.40f)      { pose = CatAnimation.Pose.Sit;  hold = Random.Range(7f, 13f); }
            else if (k < 0.62f) { pose = CatAnimation.Pose.Look; hold = Random.Range(5f, 9f);  }
            else if (k < 0.85f) { pose = CatAnimation.Pose.Lie;  hold = Random.Range(11f, 20f); }
            else                { pose = CatAnimation.Pose.Sleep; hold = Random.Range(14f, 24f); }

            // Never the same pose twice in a row -- that is the flicker he saw.
            if (i > 0 && pose == _plan[0]) { pose = CatAnimation.Pose.Look; hold = Random.Range(5f, 9f); }
            _plan.Add(pose); _planHold.Add(hold);
        }

        // The stretch IS the "getting up" beat before walking off.
        _plan.Add(CatAnimation.Pose.Stretch);  _planHold.Add(2.4f);
        _planStep = -1;
    }

    void AdvanceSettlePlan()
    {
        _planStep++;
        if (_planStep >= _plan.Count) { EnterMood(Mood.Travel); return; }
        var pose = _plan[_planStep];
        bool lounging = pose == CatAnimation.Pose.Lie || pose == CatAnimation.Pose.Sleep;
        _anim.Play(pose, lounging ? 0.45f : 0.3f);
        _stepUntil = Time.time + _planHold[_planStep];
    }

    void EnterMood(Mood m)
    {
        _mood = m;
        if (_anim == null) return;

        if (m == Mood.Travel)
        {
            if (_wander != null)
            {
                _wander.Hold = false;
                // Nearly no standing about during a travel beat — this beat IS
                // the walking, and AlienWander's own idle would eat most of it.
                _wander.IdleScale = 0.1f;
                _travelFast = Random.value < 0.25f;
                _wander.SpeedMultiplier = _travelFast ? 1.8f : 1f;
            }
            _anim.Play(CatAnimation.Pose.Walk, 0.25f);
            _stateUntil = Time.time + Random.Range(TravelMin, TravelMax);
        }
        else
        {
            if (_wander != null) _wander.Hold = true;
            BuildSettlePlan();
            AdvanceSettlePlan();
            // The PLAN decides how long this stop lasts; the beat timer only has
            // to avoid cutting it short.
            _stateUntil = Time.time + 999f;
        }
    }

    void ResolvePlayer()
    {
        if (_player != null) return;
        // Throttled — a cat that spawns before the player exists must not call
        // Find every frame forever (LightLookAt's rule).
        if (Time.time < _refindPlayerAt) return;
        _refindPlayerAt = Time.time + 1.5f;
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) _player = go.transform;
    }

    // ── teleport watchdog ────────────────────────────────────────────────
    //
    // Sam, 2026-09-10: cats occasionally "fly across the screen and get in front
    // of the camera" for a frame, near or far, which the wandering aliens never
    // did. Rather than keep guessing at causes, this both CATCHES and STOPS it:
    //
    //  • It measures movement in PLANET-LOCAL space, so the floating origin
    //    shifting the whole world (a huge world-space delta, every object, and
    //    completely normal) is invisible to it. Only a cat genuinely moving
    //    relative to its planet trips it.
    //  • It runs in LateUpdate, which is BEFORE rendering — so a clamped frame
    //    never reaches the screen. That is the difference between diagnosing
    //    this and fixing it.
    //  • It logs the first few with enough state to name the cause, then goes
    //    quiet so it cannot spam a play session.
    //
    // A cat walks at ~1.1 m/s (1.8x when trotting). Two metres in a single frame
    // is already an order of magnitude past anything legitimate.
    const float MaxFrameStep = 2f;
    const int   MaxReports   = 12;
    static int _reports;

    Vector3 _lastLocal;
    bool _haveLast;
    float _armedAt;
    Transform _rootBone;
    bool _meshReported;
    static int _boneReports;

    /// Called whenever the spawner legitimately places this cat, so a deliberate
    /// reposition is never mistaken for a glitch.
    public void ArmWatchdog()
    {
        _haveLast = false;
        // Ignore the first half-second: SpawnFade, NPCSeating.Reseat and the
        // wander's own settle all move the body on purpose in that window.
        _armedAt = Time.time + 0.5f;
    }

    void LateUpdate()
    {
        if (transform.parent == null) { _haveLast = false; return; }

        Vector3 now = transform.localPosition;
        if (_haveLast && Time.time >= _armedAt)
        {
            float d = Vector3.Distance(now, _lastLocal);
            if (d > MaxFrameStep)
            {
                // Put it back BEFORE this frame is drawn.
                transform.localPosition = _lastLocal;
                now = _lastLocal;

                if (_reports < MaxReports)
                {
                    _reports++;
                    string wander = _wander == null ? "none"
                        : $"moving={_wander.IsMoving} hold={_wander.Hold} approach={_wander.Approaching}";
                    Debug.LogWarning(
                        $"[SpaceCat] CLAMPED a {d:F1}m one-frame jump on '{name}' " +
                        $"(dt={Time.deltaTime * 1000f:F0}ms, mood={_mood}, pose={(_anim != null ? _anim.Current.ToString() : "?")}, " +
                        $"{wander}, scale={transform.localScale.x:F2}, parent='{transform.parent.name}'). " +
                        $"Report {_reports}/{MaxReports}.");
                }
            }
        }
        // Smoothed local speed for the walk pose. Planet-local, so the planet's
        // own orbital motion never registers as the cat walking.
        float dt = Time.deltaTime;
        if (_haveLast && dt > 0.0001f)
            _speed = Mathf.Lerp(_speed, Vector3.Distance(now, _lastLocal) / dt,
                                1f - Mathf.Exp(-10f * dt));

        _lastLocal = now;
        _haveLast = true;

        // Second, independent check: has the MESH left the object?
        //
        // ⚠️ This used to compare SkinnedMeshRenderer.bounds (WORLD space) to
        // transform.position and it was a FALSE POSITIVE machine: the planets
        // orbit, so the whole world translates ~16-20m PER FRAME, and renderer
        // bounds lag the transform by a frame. It duly reported every stationary
        // cat as "detached" by 15-23m — which is just the planet's speed — and
        // burned the whole report budget without saying anything true.
        //
        // Comparing two transforms read in the SAME frame from the SAME
        // hierarchy has no lag and no world-motion term, so this is now the Root
        // bone against its own object.
        if (_meshReported || _boneReports >= MaxReports) return;
        if (_rootBone == null)
        {
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == "Root") { _rootBone = all[i]; break; }
            if (_rootBone == null) { _meshReported = true; return; }
        }
        Vector3 bp = _rootBone.position;
        // NaN FIRST. Every comparison against NaN is false, so a degenerate pose
        // (an all-zero-weight animation mixer produces a zero quaternion, which
        // normalises to NaN) sails straight through a distance check — and NaN
        // vertices rasterise as huge triangles across the screen, which is one
        // candidate for the one-frame obstruction. The old check was blind to
        // exactly the case it most needed to catch.
        if (float.IsNaN(bp.x) || float.IsNaN(bp.y) || float.IsNaN(bp.z))
        {
            _meshReported = true;
            _boneReports++;
            Debug.LogWarning($"[SpaceCat] NaN ROOT BONE on '{name}' " +
                             $"(pose={(_anim != null ? _anim.Current.ToString() : "?")}). " +
                             "Degenerate animation pose — this WILL draw garbage geometry.");
            return;
        }

        float boneOff = Vector3.Distance(bp, transform.position);
        if (boneOff > 6f)
        {
            _meshReported = true;
            _boneReports++;
            Debug.LogWarning(
                $"[SpaceCat] ROOT BONE OFF by {boneOff:F1}m on '{name}' " +
                $"(pose={(_anim != null ? _anim.Current.ToString() : "?")}). " +
                "Genuine bone fault — same-frame comparison, no world-motion term.");
        }
    }

    // ── interaction ──────────────────────────────────────────────────────

    void Awake()
    {
        // A cat is a 70 cm animal, often lying down, and the gaze test works off
        // its renderers and colliders. At the default zero latch the prompt
        // dropped whenever the reticle slipped off a small, low target — Sam:
        // "sometimes it doesnt register when im looking right at the cat".
        // A short latch holds the prompt through those gaps without letting it
        // linger once you genuinely look away.
        gazeLatchSeconds = 0.22f;
    }

    protected override bool CanInteract() => !CatPerkTradeUI.IsOpen;

    protected override string BuildInteractMessage()
        => $"Press {PromptGlyphs.Interact} to pet the cat";

    protected override void Interact()
    {
        CatPerkTradeUI.Open(this);
    }

    void OnDisable()
    {
        // Pool reuse: never hand a cat back mid-conversation.
        if (CatPerkTradeUI.IsOpen && CatPerkTradeUI.Target == this) CatPerkTradeUI.Close();

        // A cat can be pooled while the player is still inside its trigger, and
        // OnTriggerExit does not fire on a deactivated object — so the prompt
        // would stay on screen owned by a cat that no longer exists.
        playerInInteractionZone = false;
        InteractPromptUI.ClearIfOwnedBy(gameObject);
        _noticing = false;
    }
}
