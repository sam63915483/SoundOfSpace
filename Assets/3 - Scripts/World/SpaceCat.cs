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
    bool _travelFast;
    bool _heldByUI;

    // Settle-beat state: a short ordered plan per stop.
    readonly List<CatAnimation.Pose> _plan = new List<CatAnimation.Pose>();
    readonly List<float> _planHold = new List<float>();
    int _planStep = -1;
    float _stepUntil;
    // Smoothed planet-local speed in m/s, which drives the walk pose.
    float _speed;
    // Set by Pet()/FedFish(): once the reaction clip and the panel are done,
    // run the contented sequence instead of a normal beat.
    bool _pendingContented;
    bool _contented;
    // When the cat last came to rest mid-travel (-1 = it is moving).
    float _stillSince = -1f;

    enum Mood { Travel, Settle }
    Mood _mood = Mood.Settle;

    public void Bind(AlienWander wander, CatAnimation anim, AudioClip purr, float purrVolume)
    {
        _wander = wander;
        _anim = anim;
        _purrClip = purr;
        _purrVolume = purrVolume;
        _purrUntil = 0f;
        if (_purr != null && _purr.isPlaying) _purr.Stop();
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
            // The purr must keep ticking here, and a one-shot (eating the fish
            // it was just handed, the pet reaction) must NOT be stomped by the
            // "sit up and look interested" default.
            TickPurr();
            ResolvePlayer();
            FacePlayer();
            if (!_anim.OneShotPlaying) _anim.Play(CatAnimation.Pose.Sit, 0.3f);
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

        TickPurr();

        // A one-shot (being petted, eating, washing) owns the body until it
        // finishes. Nothing below may start a beat over the top of it.
        if (_anim.OneShotPlaying)
        {
            if (_wander != null) _wander.Hold = true;
            return;
        }

        // Just petted or fed and the reaction clip has finished: go straight
        // into wash-then-loaf, ahead of the notice/glance logic below, which
        // would otherwise hold the cat staring at you for five seconds first.
        if (_pendingContented)
        {
            _pendingContented = false;
            EnterContented();
            return;
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
        {
            // Don't sit down with your head in a tree. If a travel beat is
            // ending next to something solid, keep walking a few more seconds
            // and let the wander carry the cat somewhere clearer. Capped so a
            // cat boxed in on all sides still settles eventually rather than
            // pacing forever.
            if (_mood == Mood.Travel && _settleRetries < 3 && ObstacleNearby())
            {
                _settleRetries++;
                _stateUntil = Time.time + Random.Range(2.5f, 4.5f);
            }
            else
            {
                _settleRetries = 0;
                EnterMood(_mood == Mood.Travel ? Mood.Settle : Mood.Travel);
            }
        }

        if (_mood == Mood.Travel)
        {
            // THE POSE LEADS THE MOVEMENT. Sam: "whenever they move to walk
            // they should always do the walk animation."
            //
            // Two earlier versions both lagged it. IsMoving flickered per frame.
            // Then measured speed was better, but EnterMood(Travel) played Walk
            // and the VERY NEXT frame saw speed ~0 (the cat had not moved yet)
            // and faded back to Idle -- then faded to Walk again once it was
            // already moving. Every leg started Walk->Idle->Walk through two
            // crossfades, with the cat sliding through the Idle part. That is
            // the "standing position then just slides forward" he still saw.
            //
            // So: in a travel beat the cat WALKS by default. It drops to Idle
            // only once it has genuinely been still for half a second (the
            // wander's own pause between legs), and the moment speed comes
            // back it walks again. Intent first, measurement as a slow veto.
            bool fast = _speed > 0.2f;
            if (fast) _stillSince = -1f;
            else if (_stillSince < 0f) _stillSince = Time.time;
            bool stoppedForAWhile = _stillSince >= 0f && Time.time - _stillSince > 0.5f;

            var want = stoppedForAWhile ? CatAnimation.Pose.Idle
                     : (_travelFast ? CatAnimation.Pose.Trot : CatAnimation.Pose.Walk);
            _anim.Play(want, 0.25f);
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
            // The Action clips found on 2026-09-11 are ONE-SHOTS: they play
            // through once and hand back to Sit by themselves, so their hold is
            // "until the clip ends" (see AdvanceSettlePlan), not a number here.
            // SharpensClaws is deliberately NOT here. It is authored against a
            // scratching post -- the cat rears up on two legs and rakes at
            // something in front of it -- and with nothing there it scratches
            // the air (Sam, 2026-09-11). Still wired, so it could be used
            // properly one day next to a tree. Its share went to Look and Lick.
            if (k < 0.26f)      { pose = CatAnimation.Pose.Sit;     hold = Random.Range(7f, 13f); }
            else if (k < 0.44f) { pose = CatAnimation.Pose.Look;    hold = Random.Range(5f, 9f);  }
            else if (k < 0.65f) { pose = CatAnimation.Pose.Lick;    hold = 0f; }   // washing
            else if (k < 0.72f) { pose = CatAnimation.Pose.Dig;     hold = 0f; }
            else if (k < 0.78f) { pose = CatAnimation.Pose.Shake;   hold = 0f; }
            else if (k < 0.91f) { pose = CatAnimation.Pose.Lie;     hold = Random.Range(11f, 20f); }
            else                { pose = CatAnimation.Pose.Sleep;   hold = Random.Range(14f, 24f); }

            // Never the same pose twice in a row -- that is the flicker he saw.
            if (i > 0 && pose == _plan[0]) { pose = CatAnimation.Pose.Look; hold = Random.Range(5f, 9f); }
            _plan.Add(pose); _planHold.Add(hold);
        }

        // The stretch IS the "getting up" beat before walking off.
        _plan.Add(CatAnimation.Pose.Stretch);  _planHold.Add(2.4f);
        _planStep = -1;
    }

    /// After being petted or fed (Sam, 2026-09-11): wash, then loaf while the
    /// purr fades, then get up and carry on. "after you pet the cat they should
    /// pur for 10-15 seconds and do the cleaning and loafing before doing
    /// something else". The loaf's length is not a number here -- it lasts
    /// exactly until the purr has faded to silence, so the two end together.
    void EnterContented()
    {
        _mood = Mood.Settle;
        _contented = true;
        if (_wander != null) _wander.Hold = true;
        _plan.Clear(); _planHold.Clear();
        _plan.Add(CatAnimation.Pose.Lick);    _planHold.Add(0f);   // one-shot, waits for the clip
        _plan.Add(CatAnimation.Pose.Lie);     _planHold.Add(0f);   // filled in below from the purr
        _plan.Add(CatAnimation.Pose.Stretch); _planHold.Add(2.4f); // and get up
        _planStep = -1;
        _stateUntil = Time.time + 999f;
        AdvanceSettlePlan();
    }

    void AdvanceSettlePlan()
    {
        _planStep++;
        if (_planStep >= _plan.Count) { _contented = false; EnterMood(Mood.Travel); return; }
        var pose = _plan[_planStep];
        if (_contented && pose == CatAnimation.Pose.Lie)
        {
            // Loaf until the purr is gone -- never less than a few seconds, so
            // a cat petted with the purr already mostly spent still settles.
            _planHold[_planStep] = Mathf.Max(5f, _purrUntil - Time.time);
        }
        if (CatAnimation.IsOneShot(pose))
        {
            // Plays once and hands back to Sit on its own; the Update loop
            // holds the beat while OneShotPlaying is true. If the clip was not
            // wired, fall through to a plain sit so the plan never stalls.
            if (_anim.PlayOnce(pose, CatAnimation.Pose.Sit)) { _stepUntil = Time.time + 0.5f; return; }
            pose = CatAnimation.Pose.Sit;
        }
        bool lounging = pose == CatAnimation.Pose.Lie || pose == CatAnimation.Pose.Sleep;
        _anim.Play(pose, lounging ? 0.45f : 0.3f);
        _stepUntil = Time.time + _planHold[_planStep];
    }

    void EnterMood(Mood m)
    {
        _mood = m;
        _contented = false;
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
            _stillSince = -1f;      // a fresh leg always starts in the walk
            _anim.Play(_travelFast ? CatAnimation.Pose.Trot : CatAnimation.Pose.Walk, 0.25f);
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

    // PERF: one lookup shared by every cat, not one per cat. Forty-four cats
    // each calling FindGameObjectWithTag on their own timer was ~30 scene
    // searches a second while the player did not exist yet.
    static Transform s_player;
    static float s_refindAt;

    // -- turning to face the player while talked to ---------------------------

    /// Swing the body round to face the player, about the cat's OWN up so it
    /// stays flat on the ground wherever it is on the planet. Only ever called
    /// while the wander is Held, so nothing else is writing the rotation.
    /// Sam, 2026-09-11: "whenever you press the interact button on a cat to
    /// start dialogue, they turn to face you".
    void FacePlayer()
    {
        if (_player == null) return;
        Vector3 up = transform.up;
        Vector3 to = Vector3.ProjectOnPlane(_player.position - transform.position, up);
        if (to.sqrMagnitude < 0.01f) return;
        Quaternion want = Quaternion.LookRotation(to.normalized, up);
        // Frame-rate independent ease; about a third of a second to come round.
        transform.rotation = Quaternion.Slerp(transform.rotation, want,
                                              1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    // -- obstacle check for settling ------------------------------------------

    static readonly Collider[] s_overlap = new Collider[8];
    int _settleRetries;

    /// Anything SOLID within the cat's own body radius: a tree trunk, a crystal,
    /// a building. Triggers are ignored (every cat's interact volume is one),
    /// and so is the cat itself. Sam, 2026-09-11: a cat "walked up to a tree and
    /// then sat and kept its head in the tree".
    bool ObstacleNearby()
    {
        float sc = Mathf.Max(0.5f, transform.localScale.x);
        Vector3 centre = transform.position + transform.up * (0.3f * sc);
        float radius = 0.5f * sc;
        // World props (trees, crystals, mushrooms), the ship, and Default
        // (buildings). Terrain is on Body and is deliberately NOT here.
        int mask = SpawnerCubeface.WorldSpawnExcludeMask | (1 << 0);
        int n = Physics.OverlapSphereNonAlloc(centre, radius, s_overlap, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = s_overlap[i];
            if (col == null || col.transform.IsChildOf(transform)) continue;
            return true;
        }
        return false;
    }

    void ResolvePlayer()
    {
        if (_player != null) return;
        if (s_player == null)
        {
            if (Time.time < s_refindAt) return;
            s_refindAt = Time.time + 1.5f;
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) s_player = go.transform;
        }
        _player = s_player;
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

    // -- being petted, being fed, purring -----------------------------------

    AudioClip _purrClip;
    float _purrVolume = 0.55f;
    AudioSource _purr;
    float _purrUntil;
    // Sam: "pur for 10-15 seconds". The pet clip (2.7 s) and the wash (6.7 s)
    // come first, so 15 s leaves a ~5-6 s loaf with the fade running across it.
    const float PurrSeconds = 15f;
    const float PurrFadeOut = 4.5f;   // the fade IS the loaf winding down

    /// Pet the cat: the pet reaction that matches how it is sitting right now,
    /// then a purr from the cat itself.
    public void Pet()
    {
        if (_anim == null) return;
        var now = _anim.Current;
        CatAnimation.Pose react, back;
        if (now == CatAnimation.Pose.Lie || now == CatAnimation.Pose.Sleep)
        { react = CatAnimation.Pose.PetLie; back = CatAnimation.Pose.Lie; }
        else if (now == CatAnimation.Pose.Sit || now == CatAnimation.Pose.Lick)
        { react = CatAnimation.Pose.PetSit; back = CatAnimation.Pose.Sit; }
        else
        { react = CatAnimation.Pose.Pet;    back = CatAnimation.Pose.Sit; }

        if (_wander != null) _wander.Hold = true;
        if (!_anim.PlayOnce(react, back, 0.2f, 0.4f)) _anim.Play(back, 0.3f);
        StartPurr();
        _pendingContented = true;
    }

    /// Fed a fish: head down and eat it, then purr.
    public void FedFish()
    {
        if (_anim == null) return;
        if (_wander != null) _wander.Hold = true;
        if (!_anim.PlayOnce(CatAnimation.Pose.Eat, CatAnimation.Pose.Sit, 0.2f, 0.4f))
            _anim.Play(CatAnimation.Pose.Sit, 0.3f);
        StartPurr();
        _pendingContented = true;
    }

    void StartPurr()
    {
        if (_purrClip == null) return;
        if (_purr == null)
        {
            // Built lazily: most cats are never touched, so most never carry an
            // AudioSource at all.
            _purr = gameObject.AddComponent<AudioSource>();
            _purr.playOnAwake = false;
            _purr.loop = true;
            _purr.spatialBlend = 1f;              // FROM the cat
            _purr.rolloffMode = AudioRolloffMode.Logarithmic;
            _purr.minDistance = 2.6f;             // full volume when you are on it
            _purr.maxDistance = 26f;              // gone by here -- "walk away, it gets quieter"
            _purr.dopplerLevel = 0f;              // planets move fast; Doppler would warble
            _purr.spread = 40f;
        }
        _purr.clip = _purrClip;
        _purrUntil = Time.time + PurrSeconds;
        if (!_purr.isPlaying) { _purr.volume = 0f; _purr.Play(); }
    }

    void TickPurr()
    {
        if (_purr == null || !_purr.isPlaying) return;
        float left = _purrUntil - Time.time;
        if (left <= 0f) { _purr.Stop(); return; }
        // Half a second in, three seconds out.
        float inW  = Mathf.Clamp01((PurrSeconds - left) / 0.5f);
        float outW = Mathf.Clamp01(left / PurrFadeOut);
        _purr.volume = _purrVolume * Mathf.Min(inW, outW);
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
        if (_purr != null && _purr.isPlaying) _purr.Stop();
    }
}
