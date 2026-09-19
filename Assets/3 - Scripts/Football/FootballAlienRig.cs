using UnityEngine;

/// How a man has the ball (or his hands) — drives the arm pose and where the
/// ball sits. Sam (2026-09-19): the QB HOLDS it in his hands, anyone running
/// with it has it TUCKED in the right arm, the centre is over it.
public enum HoldStyle { None, TwoHands, Tucked, SnapStance, ReadyHands }

/// A dead-ball celebration / reaction (Madden-style, Sam 2026-09-19).
public enum EmoteKind { None, ArmsUp, FirstDown, Flex, ChestThump, IncompleteWave, Point, Dejected, NoFlyZone, Dance, Bow }

/// How a man stands when he is not running: the huddle lean, a lineman's
/// three-point stance, a defender's crouch, a receiver's ready stance.
/// Standing bolt upright between plays was Sam's "they all just freeze".
public enum Stance { None, Huddle, Lineman, Crouch, Ready }

/// <summary>
/// Procedural football animation for the Alien_Pack rigs (thigh/calf/foot,
/// upperarm/lowerarm/hand, spine, head — a UE-style skeleton in a T-pose, no
/// clips shipped). Same situation as AlienWander / NPCWaveAnimation: bones
/// are written in LateUpdate, after any Animator.
///
/// Everything is "aim the shaft": a bone is rotated so the segment to its
/// child points where the pose wants it, in the MODEL's frame (the model is a
/// child of the FootballPlayer, which faces its run direction). Bent arms use
/// a two-bone solve (TwoBone) so a hand can be PUT somewhere — on the ball.
///
/// What it does:
///   • run cycle from speed: legs swing + knee bend, arms pump, torso leans
///   • REACH: both arms aim at a point (the ball) — the catch test uses the
///     analytic hand positions this produces (FootballPlayer.ArmGap)
///   • THROW: the right arm winds back and whips forward; the ball leaves the
///     hand at the release point of that swing (PlayInstance waits for it)
///   • HOLD styles: two hands at the chest (QB), tucked in the right arm
///     (a runner), the centre crouched over the ball, the QB's hands out for
///     the snap
///   • EMOTES: arms up, the first-down signal, a flex, a chest thump, the
///     incomplete wave, pointing to the sky, hands on hips
///   • HURDLE (legs tucked up), DIVE (arms out, legs back), SPIN (arms in),
///     kick (a leg swing)
/// Jump, fall, tumble, dive tilt and spin yaw are on the model root, handled
/// by FootballPlayer.
/// </summary>
public class FootballAlienRig : MonoBehaviour
{
    // ── driven state (FootballPlayer writes these every tick) ──
    [System.NonSerialized] public float speedFrac;        // 0..1 of top speed
    [System.NonSerialized] public float stridePhase;      // radians, advances with distance run
    [System.NonSerialized] public bool reaching;
    [System.NonSerialized] public Vector3 reachTargetWorld;
    [System.NonSerialized] public float throwPhase = -1f; // −1 idle, 0..1 through the motion
    [System.NonSerialized] public Vector3 throwDirWorld = Vector3.forward;
    [System.NonSerialized] public HoldStyle hold = HoldStyle.None;
    [System.NonSerialized] public Vector3 ballWorld;      // where the ball is (snap stance: the ball on the grass)
    [System.NonSerialized] public float kickPhase = -1f;
    [System.NonSerialized] public EmoteKind emote = EmoteKind.None;
    [System.NonSerialized] public float emotePhase;       // 0..1 through the emote
    [System.NonSerialized] public float hurdlePhase = -1f;
    [System.NonSerialized] public float divePhase = -1f;
    [System.NonSerialized] public float spinPhase = -1f;
    [System.NonSerialized] public float hardFall;         // 0..1: flailing while tumbling
    [System.NonSerialized] public Stance stance = Stance.None;
    [System.NonSerialized] public float idleTime;         // seconds standing still: breathing, weight shifts
    [System.NonSerialized] public bool blocking;          // arms out, hands up, holding a man off (or fighting one)
    [System.NonSerialized] public bool looking;           // the head follows lookTargetWorld (the ball, his man, his read)
    [System.NonSerialized] public Vector3 lookTargetWorld;
    [System.NonSerialized] public bool wrapping;          // arms wrapped round the man he is tackling
    [System.NonSerialized] public Vector3 wrapTargetWorld; // that man's waist
    [System.NonSerialized] public bool stiffArm;          // the off arm straight out into a tackler
    [System.NonSerialized] public Vector3 stiffArmTargetWorld;
    [System.NonSerialized] public float stumble;          // 0..1: caught a foot, pitching forward
    /// Where the ball is DRAWN while held, from the posed bones (on the right
    /// forearm for a tuck, between the hands for two hands) — set every
    /// LateUpdate, so the ball is on the arm whatever the arms managed.
    [System.NonSerialized] public Vector3 heldBallWorld; [System.NonSerialized] public bool heldBallValid;

    public const float ThrowSeconds = 0.42f;
    public const float ThrowRelease = 0.58f;              // fraction of the motion where the ball leaves the hand
    public const float KickSeconds = 0.5f;

    Transform _pelvis, _spine, _spine2, _head;
    Transform _thighL, _thighR, _calfL, _calfR, _footL, _footR;
    Transform _upperL, _upperR, _lowerL, _lowerR, _handL, _handR;
    float _upperLen, _lowerLen;
    Vector3 _headFwdLocal = Vector3.forward;              // the head bone's own axis that faces the model's forward
    Vector3 _headDir;                                     // smoothed look direction (world)
    bool _still = true;
    readonly System.Collections.Generic.Dictionary<Transform, Quaternion> _rest = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
    bool _ok;
    Transform _frame;                                      // the FootballPlayer transform: forward/right/up

    public bool Ready => _ok;
    public float ArmLength => _upperLen + _lowerLen;

    /// Shoulder sockets in world space (bone origins: unaffected by arm pose).
    public Vector3 ShoulderR => _upperR != null ? _upperR.position : transform.position + Vector3.up * 1.2f;
    public Vector3 ShoulderL => _upperL != null ? _upperL.position : transform.position + Vector3.up * 1.2f;
    public Vector3 ShoulderMid => (ShoulderR + ShoulderL) * 0.5f;

    public void Init(Transform frame)
    {
        _frame = frame;
        _pelvis = Find("pelvis"); _spine = Find("spine_01"); _spine2 = Find("spine_02"); _head = Find("head");
        _thighL = Find("thigh_l"); _thighR = Find("thigh_r"); _calfL = Find("calf_l"); _calfR = Find("calf_r");
        _footL = Find("foot_l"); _footR = Find("foot_r");
        _upperL = Find("upperarm_l"); _upperR = Find("upperarm_r"); _lowerL = Find("lowerarm_l"); _lowerR = Find("lowerarm_r");
        _handL = Find("hand_l"); _handR = Find("hand_r");
        _ok = _thighL && _thighR && _calfL && _calfR && _upperL && _upperR && _lowerL && _lowerR && _handL && _handR;
        if (_ok)
        {
            _upperLen = Vector3.Distance(_upperR.position, _lowerR.position);
            _lowerLen = Vector3.Distance(_lowerR.position, _handR.position);
            if (_head != null) _headFwdLocal = Quaternion.Inverse(_head.rotation) * frame.forward;
            // The rest pose: every driven bone starts each frame from here, so a
            // frame's aim is a clean rotation from rest — never a rotation on
            // top of last frame's (which is how roll crept in and heads craned).
            foreach (var b in new[] { _spine, _spine2, _head, _thighL, _thighR, _calfL, _calfR, _upperL, _upperR, _lowerL, _lowerR, _handL, _handR })
                if (b != null) _rest[b] = b.localRotation;
        }
        else Debug.LogWarning("[FootballAlienRig] bones missing on " + name + " — playing as a statue");
    }

    Transform Find(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    /// Where the right hand IS for the current pose — computed, not read from
    /// the bone, so the sim can use it the same tick it decides.
    public Vector3 HandR(Vector3 aimWorld) => ShoulderR + aimWorld.normalized * ArmLength;
    public Vector3 HandL(Vector3 aimWorld) => ShoulderL + aimWorld.normalized * ArmLength;

    /// Where the ball sits for a hold style (world). Analytic, from the
    /// shoulders and the model's axes, so the ball and the hands agree.
    public Vector3 HoldPoint(HoldStyle style)
    {
        Vector3 f = transform.forward, u = transform.up, r = transform.right;
        float a = ArmLength;
        switch (style)
        {
            // (These bodies are thick: the shoulder bones sit inside the chest, so
            // anything less than ~0.4 arm-lengths forward was buried in the torso.)
            // Absolute metres (scaled by the body): these arms are only ~0.5 m and
            // the shoulder bone sits ~0.17 m inside the chest, so anything in
            // arm-length multiples ended up inside the torso (the analyzer: 7 cm
            // in front of the chest plane).
            case HoldStyle.TwoHands: return ShoulderMid + f * Mathf.Min(0.42f, a * 0.85f) - u * 0.16f;          // held out in front, elbows a little bent
            case HoldStyle.Tucked:   return ShoulderR + f * Mathf.Min(0.40f, a * 0.8f) - u * 0.28f - r * 0.05f;      // on the forearm, in front of the ribs
            default:                 return ShoulderR + f * (a * 0.5f) - r * (a * 0.25f) - u * (a * 0.35f);
        }
    }

    /// The direction the right arm points during a throw at `phase`.
    public Vector3 ThrowArmDir(float phase)
    {
        Vector3 f = transform.forward, u = transform.up, r = transform.right;
        Vector3 back = (-f * 0.6f + u * 0.7f + r * 0.35f).normalized;        // cocked behind the head
        Vector3 fwd = (throwDirWorld * 0.85f + u * 0.5f).normalized;           // the release point
        Vector3 through = (f * 0.5f - u * 0.8f - r * 0.3f).normalized;        // follow-through across the body
        if (phase < 0.35f) return Vector3.Slerp((f * 0.3f - u).normalized, back, phase / 0.35f);
        if (phase < ThrowRelease) return Vector3.Slerp(back, fwd, (phase - 0.35f) / (ThrowRelease - 0.35f));
        return Vector3.Slerp(fwd, through, (phase - ThrowRelease) / (1f - ThrowRelease));
    }

    void LateUpdate()
    {
        if (!_ok || _frame == null) return;
        Pose();
        PlaceHeldBall(transform.forward, transform.right, transform.up);
    }

    void Pose()
    {
        // The MODEL's axes, not the player's: when FootballPlayer tips the
        // model over for a tackle, the legs and arms must tip with it. Posing
        // against world-up drove the legs straight down through the grass and
        // left the body buried to the waist.
        Vector3 f = transform.forward, r = transform.right, u = transform.up;
        float s = speedFrac;
        float swing = Mathf.Sin(stridePhase);
        foreach (var kv in _rest) kv.Key.localRotation = kv.Value;
        // Standing vs running with hysteresis: a man hovering at the threshold
        // used to flip poses every tick (the analyzer counted thousands).
        if (_still && s > 0.16f) _still = false; else if (!_still && s < 0.06f) _still = true;

        // ── torso ──
        Vector3 spineDir = Quaternion.AngleAxis(10f * s, r) * u;               // lean into the run
        bool still = _still;
        if (still && stance == Stance.Huddle) spineDir = (u * 0.75f + f * 0.62f).normalized;           // leaning in, hands on knees
        else if (still && stance == Stance.Lineman) spineDir = (u * 0.5f + f * 0.85f).normalized;      // three-point
        else if (still && stance == Stance.Crouch) spineDir = (u * 0.88f + f * 0.32f).normalized;
        else if (still && stance == Stance.Ready) spineDir = (u * 0.92f + f * 0.22f).normalized;
        if (still && hold == HoldStyle.None && emote == EmoteKind.None && throwPhase < 0f && !reaching && hardFall <= 0f)
        {
            // Breathing and a slow weight shift so nobody is a statue.
            float breathe = Mathf.Sin(idleTime * 1.7f) * 1.5f, sway = Mathf.Sin(idleTime * 0.9f + 1f) * 1.2f;
            spineDir = Quaternion.AngleAxis(breathe, r) * Quaternion.AngleAxis(sway, f) * spineDir;
        }
        if (hold == HoldStyle.SnapStance) spineDir = (u * 0.45f + f * 0.9f).normalized;       // bent over the ball
        else if (hold == HoldStyle.ReadyHands) spineDir = (u * 0.9f + f * 0.25f).normalized;  // a slight crouch
        else if (divePhase >= 0f) spineDir = (u * 0.8f + f * 0.4f).normalized;
        else if (emote == EmoteKind.ArmsUp || emote == EmoteKind.Point) spineDir = (u * 0.95f - f * 0.15f).normalized;
        else if (emote == EmoteKind.Dejected) spineDir = (u * 0.85f + f * 0.35f).normalized;
        else if (emote == EmoteKind.Bow) spineDir = (u * 0.4f + f * 0.9f).normalized;
        else if (emote == EmoteKind.Dance) spineDir = Quaternion.AngleAxis(Mathf.Sin(emotePhase * 25f) * 8f, f) * (u * 0.95f + f * 0.1f).normalized;
        if (stumble > 0f) spineDir = Quaternion.AngleAxis(28f * Mathf.Sin(Mathf.PI * stumble), r) * spineDir;
        if (_spine != null) Aim(_spine, _spine2 != null ? _spine2 : _head, spineDir);

        // ── legs ──
        float legSwing = 32f * s;
        float kneeR = Mathf.Max(0f, -swing) * 60f * s, kneeL = Mathf.Max(0f, swing) * 60f * s;
        Vector3 thighDirR = Quaternion.AngleAxis(-legSwing * swing, r) * -u;    // −angle about right = forward
        Vector3 thighDirL = Quaternion.AngleAxis(legSwing * swing, r) * -u;
        if (kickPhase >= 0f)
        {
            float k = kickPhase < 0.4f ? -kickPhase / 0.4f * 40f : Mathf.Lerp(-40f, 80f, (kickPhase - 0.4f) / 0.6f);
            thighDirR = Quaternion.AngleAxis(-k, r) * -u;
            kneeR = 30f;
        }
        else if (hurdlePhase >= 0f)
        {
            // Knees up to the chest through the middle of the hop, one leg leading.
            float k = Mathf.Sin(Mathf.PI * Mathf.Clamp01(hurdlePhase));
            thighDirR = Quaternion.AngleAxis(-95f * k, r) * -u;
            thighDirL = Quaternion.AngleAxis(-70f * k, r) * -u;
            kneeR = 115f * k; kneeL = 95f * k;
        }
        else if (divePhase >= 0f)
        {
            // Legs trail straight behind.
            thighDirR = Quaternion.AngleAxis(12f, r) * -u; thighDirL = Quaternion.AngleAxis(6f, r) * -u;
            kneeR = 10f; kneeL = 18f;
        }
        else if (hold == HoldStyle.SnapStance)
        {
            thighDirR = Quaternion.AngleAxis(-48f, r) * -u; thighDirL = Quaternion.AngleAxis(-48f, r) * -u;
            kneeR = kneeL = 78f;
        }
        else if (hold == HoldStyle.ReadyHands)
        {
            thighDirR = Quaternion.AngleAxis(-14f, r) * -u; thighDirL = Quaternion.AngleAxis(-14f, r) * -u;
            kneeR = kneeL = 26f;
        }
        else if (still && stance == Stance.Huddle)
        {
            thighDirR = Quaternion.AngleAxis(-22f, r) * -u; thighDirL = Quaternion.AngleAxis(-22f, r) * -u;
            kneeR = kneeL = 38f;
        }
        else if (still && stance == Stance.Lineman)
        {
            // Deep squat, one leg staggered back.
            thighDirR = Quaternion.AngleAxis(-55f, r) * -u; thighDirL = Quaternion.AngleAxis(-30f, r) * -u;
            kneeR = 95f; kneeL = 60f;
        }
        else if (still && stance == Stance.Crouch)
        {
            thighDirR = Quaternion.AngleAxis(-24f, r) * -u; thighDirL = Quaternion.AngleAxis(-24f, r) * -u;
            kneeR = kneeL = 44f;
        }
        else if (still && stance == Stance.Ready)
        {
            // One foot forward, weight on the balls of the feet.
            thighDirR = Quaternion.AngleAxis(-26f, r) * -u; thighDirL = Quaternion.AngleAxis(4f, r) * -u;
            kneeR = 40f; kneeL = 12f;
        }
        else if (emote == EmoteKind.Dance)
        {
            float bob = 0.5f + 0.5f * Mathf.Sin(emotePhase * 25f);
            thighDirR = Quaternion.AngleAxis(-12f - 18f * bob, r) * -u; thighDirL = Quaternion.AngleAxis(-12f - 18f * (1f - bob), r) * -u;
            kneeR = 24f + 30f * bob; kneeL = 24f + 30f * (1f - bob);
        }
        else if (still)
        {
            float shift = Mathf.Sin(idleTime * 0.9f + 1f) * 4f;
            thighDirR = Quaternion.AngleAxis(-3f + shift, r) * -u; thighDirL = Quaternion.AngleAxis(-3f - shift, r) * -u;
            kneeR = kneeL = 6f;
        }
        else if (hardFall > 0f)
        {
            float k = Mathf.Sin(hardFall * 9f);
            thighDirR = Quaternion.AngleAxis(-40f + 25f * k, r) * -u; thighDirL = Quaternion.AngleAxis(-40f - 25f * k, r) * -u;
            kneeR = kneeL = 50f;
        }
        Aim(_thighR, _calfR, thighDirR);
        Aim(_thighL, _calfL, thighDirL);
        Aim(_calfR, _footR, Quaternion.AngleAxis(kneeR, r) * thighDirR);      // +angle about right = back = knee
        Aim(_calfL, _footL, Quaternion.AngleAxis(kneeL, r) * thighDirL);

        // ── head: follows the ball / his man / his read ──
        Head(f, u);

        // ── arms ──
        if (reaching)
        {
            Vector3 dR = (reachTargetWorld - _upperR.position).normalized;
            Vector3 dL = (reachTargetWorld - _upperL.position).normalized;
            Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, dR);
            Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, dL);
            return;
        }
        if (throwPhase >= 0f)
        {
            Vector3 dR = ThrowArmDir(throwPhase);
            Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, dR);
            // Off arm points where he's throwing, then drops.
            Vector3 dL = throwPhase < ThrowRelease ? (throwDirWorld + u * 0.2f).normalized : (-u + f * 0.3f).normalized;
            Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, dL);
            return;
        }
        if (hardFall > 0f)
        {
            float k = Mathf.Sin(hardFall * 7f);
            Vector3 dR = (f * 0.6f + u * (0.5f + 0.4f * k) + r * 0.5f).normalized;
            Vector3 dL = (f * 0.6f + u * (0.5f - 0.4f * k) - r * 0.5f).normalized;
            Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, dR);
            Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, dL);
            return;
        }
        if (emote != EmoteKind.None) { Emote(f, r, u); return; }
        if (wrapping)
        {
            // Both arms round the runner's waist — the wrap. Hands meet behind him.
            Vector3 w = wrapTargetWorld;
            TwoBone(_upperR, _lowerR, _handR, w + r * 0.28f, (r * 0.9f - u * 0.3f).normalized);
            TwoBone(_upperL, _lowerL, _handL, w - r * 0.28f, (-r * 0.9f - u * 0.3f).normalized);
            Head(f, u);
            return;
        }
        if (blocking && hold == HoldStyle.None)
        {
            // Arms out in front at chest height, elbows a little bent, a shove
            // in them — the block (Sam: they should be extending their arms).
            float shove = Mathf.Sin(stridePhase * 1.5f) * 0.04f;
            Vector3 pR = ShoulderR + f * (ArmLength * (0.82f + shove)) + r * 0.14f - u * 0.05f;
            Vector3 pL = ShoulderL + f * (ArmLength * (0.82f - shove)) - r * 0.14f - u * 0.05f;
            TwoBone(_upperR, _lowerR, _handR, pR, (-u * 0.6f + r * 0.8f).normalized);
            TwoBone(_upperL, _lowerL, _handL, pL, (-u * 0.6f - r * 0.8f).normalized);
            return;
        }
        if (divePhase >= 0f)
        {
            // Superman: both arms straight out ahead, a little apart.
            Vector3 dR = (f * 0.95f + r * 0.2f + u * 0.1f).normalized, dL = (f * 0.95f - r * 0.2f + u * 0.1f).normalized;
            Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, dR);
            Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, dL);
            return;
        }
        if (hurdlePhase >= 0f && hold != HoldStyle.Tucked)
        {
            Vector3 dR = (f * 0.7f + u * 0.5f + r * 0.4f).normalized, dL = (f * 0.7f + u * 0.5f - r * 0.4f).normalized;
            Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, dR);
            Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, dL);
            return;
        }
        if (stiffArm && hold == HoldStyle.Tucked)
        {
            // Ball tucked right; the LEFT arm straight into the man.
            Vector3 ball = HoldPoint(HoldStyle.Tucked);
            TwoBone(_upperR, _lowerR, _handR, ball + f * 0.16f - u * 0.02f, (-u * 0.9f - f * 0.3f).normalized);
            Vector3 dL = (stiffArmTargetWorld - _upperL.position).normalized;
            Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, dL);
            return;
        }
        switch (hold)
        {
            case HoldStyle.TwoHands:
            {
                // Both hands on the ball at the chest, elbows out and down.
                Vector3 ball = HoldPoint(HoldStyle.TwoHands);
                TwoBone(_upperR, _lowerR, _handR, ball + r * 0.06f, (-u * 0.6f + r * 0.8f).normalized);
                TwoBone(_upperL, _lowerL, _handL, ball - r * 0.06f, (-u * 0.6f - r * 0.8f).normalized);
                return;
            }
            case HoldStyle.Tucked:
            {
                // Right forearm wrapped over the ball against the ribs, hand on
                // its front tip; the left arm pumps (or hugs in on a spin).
                Vector3 ball = HoldPoint(HoldStyle.Tucked);
                TwoBone(_upperR, _lowerR, _handR, ball + f * 0.14f - u * 0.04f, (-u * 0.9f - r * 0.4f).normalized);   // elbow down and out, forearm under the ball
                if (spinPhase >= 0f)
                {
                    Vector3 dL = (-u * 0.6f + f * 0.5f + r * 0.3f).normalized;
                    Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, (f * 0.3f + r * 0.9f).normalized);
                }
                else ArmPump(_upperL, _lowerL, _handL, swing, Mathf.Max(s, 0.25f), -1f, f, r, u);
                return;
            }
            case HoldStyle.SnapStance:
            {
                // Hands down to the ball on the grass (straight arms — it is a stretch).
                Vector3 target = ballWorld;
                Vector3 dR = (target - _upperR.position).normalized, dL = (target - _upperL.position).normalized;
                Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, dR);
                Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, dL);
                return;
            }
            case HoldStyle.ReadyHands:
            {
                // Hands out in front of the hips, ready for the snap.
                Vector3 p = ShoulderMid + f * (ArmLength * 0.55f) - u * (ArmLength * 0.75f);
                TwoBone(_upperR, _lowerR, _handR, p + r * 0.1f, (-u * 0.3f + r * 0.9f).normalized);
                TwoBone(_upperL, _lowerL, _handL, p - r * 0.1f, (-u * 0.3f - r * 0.9f).normalized);
                return;
            }
        }
        if (still && stance == Stance.Huddle)
        {
            // Hands on the knees.
            Vector3 kR = _calfR.position + f * 0.05f, kL = _calfL.position + f * 0.05f;
            TwoBone(_upperR, _lowerR, _handR, kR, (r * 0.8f - f * 0.3f).normalized);
            TwoBone(_upperL, _lowerL, _handL, kL, (-r * 0.8f - f * 0.3f).normalized);
            return;
        }
        if (still && stance == Stance.Lineman)
        {
            // Right hand on the grass, left forearm across the thigh.
            Vector3 ground = _footR.position + f * 0.45f + r * 0.15f;
            Vector3 dR = (ground - _upperR.position).normalized;
            Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, dR);
            TwoBone(_upperL, _lowerL, _handL, _calfL.position + f * 0.1f - r * 0.05f, (-r * 0.7f - f * 0.4f).normalized);
            return;
        }
        if (still && (stance == Stance.Crouch || stance == Stance.Ready))
        {
            // Arms bent, hands in front, a little bounce.
            float b = Mathf.Sin(idleTime * 2.2f) * 0.03f;
            Vector3 pR = ShoulderR + f * (ArmLength * 0.45f) - u * (ArmLength * 0.55f + b) + r * 0.12f;
            Vector3 pL = ShoulderL + f * (ArmLength * 0.45f) - u * (ArmLength * 0.55f + b) - r * 0.12f;
            TwoBone(_upperR, _lowerR, _handR, pR, (-u * 0.4f + r * 0.9f).normalized);
            TwoBone(_upperL, _lowerL, _handL, pL, (-u * 0.4f - r * 0.9f).normalized);
            return;
        }
        if (still)
        {
            // Hanging arms with a breath in them.
            float sw = Mathf.Sin(idleTime * 1.7f) * 3f;
            Vector3 dR = Quaternion.AngleAxis(sw, r) * (-u + r * 0.22f).normalized, dL = Quaternion.AngleAxis(sw, r) * (-u - r * 0.22f).normalized;
            Aim(_upperR, _lowerR, dR); Aim(_lowerR, _handR, Quaternion.AngleAxis(-14f, r) * dR);
            Aim(_upperL, _lowerL, dL); Aim(_lowerL, _handL, Quaternion.AngleAxis(-14f, r) * dL);
            return;
        }
        ArmPump(_upperR, _lowerR, _handR, -swing, s, 1f, f, r, u);
        ArmPump(_upperL, _lowerL, _handL, swing, s, -1f, f, r, u);
    }

    /// Turn the head toward the look target, within a cone of the body's
    /// forward (no owls). Receivers watch the ball in, corners watch their
    /// man, the QB looks off his read.
    void Head(Vector3 f, Vector3 u)
    {
        if (_head == null) return;
        // The head is at REST here (reset above): its forward is the body's.
        Vector3 want = f;
        if (looking)
        {
            Vector3 dir = lookTargetWorld - _head.position;
            if (dir.sqrMagnitude > 0.01f)
            {
                dir.Normalize();
                // Within 60° of the body's forward and 30° of pitch — a glance, not an owl.
                Vector3 flat = Vector3.ProjectOnPlane(dir, u);
                if (flat.sqrMagnitude > 1e-4f)
                {
                    float yaw = Mathf.Clamp(Vector3.SignedAngle(f, flat.normalized, u), -60f, 60f);
                    float pitch = Mathf.Clamp(Vector3.SignedAngle(flat.normalized, dir, Vector3.Cross(u, flat.normalized)), -30f, 30f);
                    want = Quaternion.AngleAxis(yaw, u) * f;
                    want = Quaternion.AngleAxis(pitch, Vector3.Cross(u, want)) * want;
                }
            }
        }
        if (_headDir.sqrMagnitude < 0.01f) _headDir = want;
        _headDir = Vector3.Slerp(_headDir, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
        Vector3 cur = _head.rotation * _headFwdLocal;
        _head.rotation = Quaternion.FromToRotation(cur, _headDir) * _head.rotation;
    }

    /// Called at the end of LateUpdate: the ball's drawn position from the
    /// posed bones.
    void PlaceHeldBall(Vector3 f, Vector3 r, Vector3 u)
    {
        heldBallValid = false;
        if (hold == HoldStyle.Tucked)
        {
            // Along the right forearm, resting on top of it and against the ribs.
            Vector3 elbow = _lowerR.position, hand = _handR.position;
            Vector3 along = hand - elbow;
            Vector3 mid = elbow + along * 0.55f;
            Vector3 outward = Vector3.Cross(along.normalized, u); if (Vector3.Dot(outward, r) < 0f) outward = -outward;
            heldBallWorld = mid + u * 0.09f - outward * 0.05f + f * 0.02f;
            heldBallValid = true;
        }
        else if (hold == HoldStyle.TwoHands)
        {
            heldBallWorld = (_handR.position + _handL.position) * 0.5f + f * 0.06f;
            heldBallValid = true;
        }
    }

    /// The celebrations. `emotePhase` runs 0..1 over the emote's length; the
    /// first 15% eases in from wherever the arms were.
    void Emote(Vector3 f, Vector3 r, Vector3 u)
    {
        float p = Mathf.Clamp01(emotePhase);
        float ease = Mathf.SmoothStep(0f, 1f, p / 0.15f);
        Vector3 hangR = (-u + r * 0.22f).normalized, hangL = (-u - r * 0.22f).normalized;
        switch (emote)
        {
            case EmoteKind.ArmsUp:
            {
                float sway = Mathf.Sin(p * 14f) * 0.12f;
                Vector3 dR = Vector3.Slerp(hangR, (u + r * (0.25f + sway) + f * 0.1f).normalized, ease);
                Vector3 dL = Vector3.Slerp(hangL, (u - r * (0.25f - sway) + f * 0.1f).normalized, ease);
                Straight(_upperR, _lowerR, _handR, dR); Straight(_upperL, _lowerL, _handL, dL);
                break;
            }
            case EmoteKind.FirstDown:
            {
                // The signal: right arm cocked up, then swept forward to level. Twice.
                float cyc = Mathf.Repeat(p * 2f, 1f);
                Vector3 cocked = (u * 0.8f - f * 0.3f + r * 0.3f).normalized, level = (f * 0.95f + r * 0.2f).normalized;
                Vector3 dR = cyc < 0.35f ? Vector3.Slerp(hangR, cocked, cyc / 0.35f) : Vector3.Slerp(cocked, level, Mathf.SmoothStep(0f, 1f, (cyc - 0.35f) / 0.4f));
                Straight(_upperR, _lowerR, _handR, Vector3.Slerp(hangR, dR, ease));
                // Left hand holds the ball tucked if he has it, else hangs.
                if (hold == HoldStyle.Tucked || hold == HoldStyle.TwoHands)
                {
                    Vector3 ball = HoldPoint(HoldStyle.Tucked);
                    TwoBone(_upperL, _lowerL, _handL, ball + f * 0.14f, (-u * 0.9f - f * 0.3f).normalized);
                }
                else Straight(_upperL, _lowerL, _handL, hangL);
                break;
            }
            case EmoteKind.Flex:
            {
                float pump = 0.5f + 0.5f * Mathf.Sin(p * 12f);
                Vector3 outR = (r * 0.95f + u * 0.15f).normalized, outL = (-r * 0.95f + u * 0.15f).normalized;
                Aim(_upperR, _lowerR, Vector3.Slerp(hangR, outR, ease));
                Aim(_lowerR, _handR, Vector3.Slerp(hangR, (u * 0.9f + f * 0.25f - r * (0.3f * pump)).normalized, ease));
                Aim(_upperL, _lowerL, Vector3.Slerp(hangL, outL, ease));
                Aim(_lowerL, _handL, Vector3.Slerp(hangL, (u * 0.9f + f * 0.25f + r * (0.3f * pump)).normalized, ease));
                break;
            }
            case EmoteKind.ChestThump:
            {
                // Right fist to the chest, three beats; left arm down or on the ball.
                float beat = Mathf.Abs(Mathf.Sin(p * 3f * Mathf.PI));
                Vector3 chest = ShoulderMid + f * 0.16f - u * 0.1f;
                Vector3 outp = ShoulderR + f * 0.45f + r * 0.25f - u * 0.2f;
                TwoBone(_upperR, _lowerR, _handR, Vector3.Lerp(outp, chest, ease * beat), (-u * 0.7f + r * 0.7f).normalized);
                if (hold == HoldStyle.Tucked)
                {
                    Vector3 ball = HoldPoint(HoldStyle.Tucked);
                    TwoBone(_upperL, _lowerL, _handL, ball + f * 0.14f, (-u * 0.9f - f * 0.3f).normalized);
                }
                else Straight(_upperL, _lowerL, _handL, hangL);
                break;
            }
            case EmoteKind.IncompleteWave:
            {
                // Arms out wide, crossed in front, out wide — the wave-off.
                float w = Mathf.Sin(p * 3f * Mathf.PI);            // −1..1, three swings
                Vector3 wideR = (r * 0.95f + u * 0.1f).normalized, crossR = (f * 0.7f - r * 0.6f).normalized;
                Vector3 wideL = (-r * 0.95f + u * 0.1f).normalized, crossL = (f * 0.7f + r * 0.6f).normalized;
                Vector3 dR = Vector3.Slerp(crossR, wideR, 0.5f + 0.5f * w), dL = Vector3.Slerp(crossL, wideL, 0.5f + 0.5f * w);
                Straight(_upperR, _lowerR, _handR, Vector3.Slerp(hangR, dR, ease));
                Straight(_upperL, _lowerL, _handL, Vector3.Slerp(hangL, dL, ease));
                break;
            }
            case EmoteKind.Point:
            {
                Vector3 dR = Vector3.Slerp(hangR, (u * 0.95f + f * 0.2f + r * 0.1f).normalized, ease);
                Straight(_upperR, _lowerR, _handR, dR);
                if (hold == HoldStyle.Tucked)
                {
                    Vector3 ball = HoldPoint(HoldStyle.Tucked);
                    TwoBone(_upperL, _lowerL, _handL, ball + f * 0.14f, (-u * 0.9f - f * 0.3f).normalized);
                }
                else Straight(_upperL, _lowerL, _handL, hangL);
                break;
            }
            case EmoteKind.NoFlyZone:
            {
                // Arms crossed over the chest, hands to the opposite shoulders. Clamps.
                Vector3 shR = ShoulderR + f * 0.12f - u * 0.02f, shL = ShoulderL + f * 0.12f - u * 0.02f;
                TwoBone(_upperR, _lowerR, _handR, Vector3.Lerp(HandR(hangR), shL + f * 0.06f, ease), (-u * 0.5f + r * 0.6f + f * 0.4f).normalized);
                TwoBone(_upperL, _lowerL, _handL, Vector3.Lerp(HandL(hangL), shR + f * 0.10f, ease), (-u * 0.5f - r * 0.6f + f * 0.4f).normalized);
                break;
            }
            case EmoteKind.Dance:
            {
                // Arms alternate up and down with the knee bob; the whole line does it together.
                float bob = Mathf.Sin(p * 25f);
                Vector3 dR = Vector3.Slerp(hangR, (u * 0.9f + r * 0.35f + f * 0.1f).normalized, 0.5f + 0.5f * bob);
                Vector3 dL = Vector3.Slerp(hangL, (u * 0.9f - r * 0.35f + f * 0.1f).normalized, 0.5f - 0.5f * bob);
                Aim(_upperR, _lowerR, Vector3.Slerp(hangR, dR, ease)); Aim(_lowerR, _handR, Vector3.Slerp(hangR, Quaternion.AngleAxis(-35f, r) * dR, ease));
                Aim(_upperL, _lowerL, Vector3.Slerp(hangL, dL, ease)); Aim(_lowerL, _handL, Vector3.Slerp(hangL, Quaternion.AngleAxis(-35f, r) * dL, ease));
                break;
            }
            case EmoteKind.Bow:
            {
                Vector3 dR = (-u * 0.9f + f * 0.3f).normalized, dL = (-u * 0.9f + f * 0.3f).normalized;
                Straight(_upperR, _lowerR, _handR, Vector3.Slerp(hangR, dR, ease)); Straight(_upperL, _lowerL, _handL, Vector3.Slerp(hangL, dL, ease));
                break;
            }
            case EmoteKind.Dejected:
            {
                // Hands on hips, head down (the spine does the slump).
                Vector3 hipR = ShoulderR - u * (ArmLength * 0.85f) + r * 0.08f + f * 0.02f;
                Vector3 hipL = ShoulderL - u * (ArmLength * 0.85f) - r * 0.08f + f * 0.02f;
                TwoBone(_upperR, _lowerR, _handR, Vector3.Lerp(HandR(hangR), hipR, ease), (r * 0.9f - f * 0.4f).normalized);
                TwoBone(_upperL, _lowerL, _handL, Vector3.Lerp(HandL(hangL), hipL, ease), (-r * 0.9f - f * 0.4f).normalized);
                break;
            }
        }
    }

    /// Running arm: hangs a little out from the body, swings opposite the
    /// same-side leg, elbow bent more the faster he goes.
    void ArmPump(Transform upper, Transform lower, Transform hand, float swing, float s, float side, Vector3 f, Vector3 r, Vector3 u)
    {
        float pump = 28f * s * swing;
        Vector3 rest = (-u + r * (0.22f * side)).normalized;
        Vector3 dU = Quaternion.AngleAxis(-pump, r) * rest;
        Aim(upper, lower, dU);
        float elbow = 25f + 55f * s;
        Aim(lower, hand, Quaternion.AngleAxis(-elbow, r) * dU);
    }

    void Straight(Transform upper, Transform lower, Transform hand, Vector3 dir)
    {
        Aim(upper, lower, dir); Aim(lower, hand, dir);
    }

    /// Put the hand AT `target` (world): bend the elbow toward `elbowHint`.
    void TwoBone(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 elbowHint)
    {
        Vector3 s = upper.position;
        Vector3 d = target - s;
        float dist = d.magnitude;
        if (dist < 1e-4f) return;
        float a = _upperLen, b = _lowerLen;
        float reach = Mathf.Clamp(dist, Mathf.Abs(a - b) + 0.01f, a + b - 0.005f);
        float cosA = (a * a + reach * reach - b * b) / (2f * a * reach);
        float ang = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 dn = d / dist;
        Vector3 axis = Vector3.Cross(dn, elbowHint);
        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(dn, transform.up);
        if (axis.sqrMagnitude < 1e-6f) axis = transform.right;
        axis.Normalize();
        // Rotating dn about (dn × hint) by +ang swings it toward the hint: the
        // elbow goes where the hint says.
        Vector3 upperDir = Quaternion.AngleAxis(ang, axis) * dn;
        Aim(upper, lower, upperDir);
        Vector3 elbow = s + upperDir * a;
        Aim(lower, hand, target - elbow);
    }

    /// Rotate `bone` so its shaft (bone → child) points along `dir` (world) —
    /// at a limited rate, so a pose change (a reach, a block, a stance) is a
    /// motion and not a snap between frames. Fast enough for a throw.
    readonly System.Collections.Generic.Dictionary<Transform, Vector3> _lastDir = new System.Collections.Generic.Dictionary<Transform, Vector3>();
    const float AimRateDeg = 1000f;

    void Aim(Transform bone, Transform child, Vector3 dir)
    {
        if (bone == null || child == null) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f || dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();
        if (_lastDir.TryGetValue(bone, out var last) && last.sqrMagnitude > 0.5f && Application.isPlaying)
            dir = Vector3.RotateTowards(last, dir, AimRateDeg * Mathf.Deg2Rad * Time.deltaTime, 0f);
        _lastDir[bone] = dir;
        bone.rotation = Quaternion.FromToRotation(cur.normalized, dir) * bone.rotation;
    }
}
