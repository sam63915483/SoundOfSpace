using UnityEngine;
using TMPro;

/// <summary>
/// One player slot (handoff §4 PlayerSlot): a team, a role, a position on the
/// field and whatever brain is plugged in. Movement is kinematic in FIELD
/// space — the slot is a child of FieldRoot, so localPosition IS the field
/// position and the whole squad moves with the field when it lands on
/// Cyclops. The slot applies the brain's move (accel-limited toward top
/// speed) and forwards its action to PlayInstance; it never decides anything.
///
/// Visual: an Alien_Pack model driven by FootballAlienRig (or a capsule when
/// no model is given — the headless soak). Timed body states, all on this
/// class so the sim can test against them the same tick:
///   Jump()      hop for a catch                 FallDown()  tackled / missed, lies flat
///   HardFall()  a clipped hurdle: a tumble       Emote()     a celebration, standing still
///   StartJuke() sidestep   StartSpin() 360      StartHurdle() leap    StartDive() lunge
/// ReachFor() points both arms at the ball; ArmGap() is the catch test against
/// those arms (Sam: the ball has to touch their arms). BeginThrow() runs the
/// throwing motion; the ball leaves the hand at ThrowReleased. SetHold() says
/// how he carries the ball (two hands / tucked / over it) and BallHoldPoint()
/// is where the ball sits for that.
/// </summary>
public class FootballPlayer : MonoBehaviour
{
    public const float BaseSpeed = 8.0f;     // m/s at speed stat 0.5 — a 4.4 s forty is 8.3
    public const float Accel     = 14f;      // m/s² — reaches top speed in ~0.6 s
    public const float Height    = 2.0f;
    public const float StrideMetres = 2.1f;  // one full stride cycle
    public const float BallRadius = 0.17f;

    public const float JukeSeconds = 0.42f, SpinSeconds = 0.55f, HurdleSeconds = 0.62f, DiveSeconds = 0.5f;
    public const float HurdleHeight = 0.95f;
    public const float HardFallSeconds = 0.7f;      // the tumble itself; he stays down longer
    /// The QB/WR1 tags over every head (Sam, 2026-09-19: not needed).
    public const bool ShowLabels = false;
    /// How far past the lines a live body can go — nobody runs out the back of the end zone.
    public const float FieldMargin = 0.4f;
    /// Facing turns at a rate, never snaps (Sam: "the aliens turn unnaturally").
    public const float TurnRateRunning = 540f, TurnRateStanding = 300f;   // deg/s

    public FootballTeam team;
    /// The role for the CURRENT play — a team's seven play both ways (iron-man
    /// football), so each slot has an offensive and a defensive job and the
    /// match flips them with SetSide before every snap.
    public FootballRole role;
    public int roleIndex;                    // WR1/WR2/WR3 → 0/1/2
    public FootballRole offRole, defRole;
    public int offIndex, defIndex;
    public IPlayerBrain brain;
    /// Set by PlayInstance each tick: 1 = free, less = a blocker in the way.
    [System.NonSerialized] public float speedScale = 1f;
    /// Set by PlayInstance each tick: he is blocking someone, or being blocked (arms out).
    [System.NonSerialized] public bool blocking;
    /// A receiver standing at the end of a curl/hitch/comeback, looking at
    /// the QB: a legitimate target even though he isn't running.
    [System.NonSerialized] public bool settled;

    Vector3 _pos, _vel, _facing = Vector3.forward, _facingTarget = Vector3.forward;
    Stance _stance = Stance.None;
    float _idleTime;
    GameObject _modelPrefab;
    float _maxSpeed, _baseMaxSpeed;
    Transform _label;
    Transform _cam;
    float _camRetry;
    Renderer _body;
    MaterialPropertyBlock _mpb;
    TextMeshPro _labelText;
    Transform _bodyT;
    Transform _fieldRoot;
    FootballAlienRig _rig;
    Color _tint = Color.white;
    float _jumpT = -1f;          // seconds into the hop, −1 = not jumping
    float _jumpScale = 1f;       // a throw hop is smaller than a catch jump
    float _downLeft;             // seconds left on the ground
    float _lie;                  // 0 standing … 1 flat
    float _roll;                 // degrees about the body's long axis (a tumble ends on the back)
    float _throwT = -1f;         // seconds into the throw, −1 = none
    bool _throwReleasedTick;
    float _kickT = -1f;
    bool _kickReleasedTick;
    bool _reachThisTick;
    Vector3 _reachWorld;
    HoldStyle _hold = HoldStyle.None;
    float _emoteLeft, _emoteTotal; EmoteKind _emote;
    EmoteKind _queuedEmote; float _queuedSeconds;    // asked for while on the ground: plays once he is up
    float _jukeT = -1f; Vector3 _jukeDir;
    float _spinT = -1f;
    float _hurdleT = -1f;
    float _diveT = -1f; Vector3 _diveDir; float _diveSpeed;
    float _hardT = -1f;
    const float JumpSeconds = 0.55f, JumpHeight = 0.7f;
    public bool IsDown => _downLeft > 0f;
    public bool IsJumping => _jumpT >= 0f;
    public bool IsThrowing => _throwT >= 0f;
    public bool IsEmoting => _emoteLeft > 0f;
    public bool IsJuking => _jukeT >= 0f;
    public bool IsSpinning => _spinT >= 0f;
    public bool IsHurdling => _hurdleT >= 0f;
    public bool IsDiving => _diveT >= 0f;
    /// 0..1 through the hurdle / dive, −1 when not.
    public float HurdlePhase => IsHurdling ? Mathf.Clamp01(_hurdleT / HurdleSeconds) : -1f;
    public float DivePhase => IsDiving ? Mathf.Clamp01(_diveT / DiveSeconds) : -1f;
    public float JukePhase => IsJuking ? Mathf.Clamp01(_jukeT / JukeSeconds) : -1f;
    public float SpinPhase => IsSpinning ? Mathf.Clamp01(_spinT / SpinSeconds) : -1f;
    /// True on the one tick the throwing hand lets go of the ball.
    public bool ThrowReleased => _throwReleasedTick;
    public bool KickReleased => _kickReleasedTick;
    /// Extra reach while airborne (hands up).
    public float JumpReach => IsJumping ? JumpHeight * _jumpScale * Mathf.Sin(Mathf.PI * _jumpT / JumpSeconds) : 0f;
    public bool HasRig => _rig != null && _rig.Ready;
    public HoldStyle Hold => _hold;
    public Stance CurrentStance => _stance;
    public GameObject ModelPrefab => _modelPrefab;
    public Color Tint => _tint;
    public Transform BodyRoot => _bodyT;

    public Vector3 Pos => _pos;
    public Vector3 Vel => _vel;
    public Vector3 Facing => _facing;
    public Vector3 Right => Vector3.Cross(Vector3.up, _facing);
    public Vector3 Chest => _pos + Vector3.up * FootballBall.CatchHeight;
    public float MaxSpeed => _maxSpeed;
    public string Label
    {
        get
        {
            switch (role)
            {
                case FootballRole.WR: case FootballRole.DB: case FootballRole.OL: case FootballRole.DL:
                    return role + (roleIndex + 1).ToString();
                default: return role.ToString();
            }
        }
    }

    // ── setup ──────────────────────────────────────────────────────────────

    /// `modelPrefab` null → capsule.
    public void Init(Transform fieldRoot, FootballTeam t, FootballRole off, int offIdx, FootballRole def, int defIdx, Material bodyMat, GameObject modelPrefab = null)
    {
        team = t; offRole = off; offIndex = offIdx; defRole = def; defIndex = defIdx;
        role = off; roleIndex = offIdx;
        _fieldRoot = fieldRoot;
        transform.SetParent(fieldRoot, false);
        name = t.shortName + " " + Label;

        // Linemen (and the centre) are the slow ones whichever way they're
        // facing; the cover men (WR/DB) are the fast ones, with the corners a
        // hair faster so they run stride for stride.
        // The centre/safety slot is a big athlete, not a lineman: he has to be
        // able to run down a receiver from the deep middle.
        float roleMul = off == FootballRole.OL ? 0.85f : off == FootballRole.QB || off == FootballRole.C ? 0.95f : 1f;
        _baseMaxSpeed = _maxSpeed = BaseSpeed * (0.9f + 0.2f * team.speed) * roleMul;

        _modelPrefab = modelPrefab;
        if (modelPrefab != null) BuildAlien(modelPrefab);
        else BuildCapsule(bodyMat);

        if (!ShowLabels) return;
        var lbl = new GameObject("Label");
        lbl.transform.SetParent(transform, false);
        lbl.transform.localPosition = Vector3.up * (Height + 0.5f);
        var tmp = lbl.AddComponent<TextMeshPro>();
        tmp.text = Label;
        tmp.fontSize = 4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.Lerp(team.color, Color.white, 0.35f);
        tmp.enableWordWrapping = false;
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(4f, 1f);
        _label = lbl.transform;
        _labelText = tmp;
    }

    void BuildCapsule(Material bodyMat)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Kill(body.GetComponent<Collider>());
        body.name = "Body";
        body.transform.SetParent(transform, false);
        body.transform.localPosition = Vector3.up * (Height * 0.5f);
        body.transform.localScale = new Vector3(0.7f, Height * 0.5f, 0.7f);
        _body = body.GetComponent<Renderer>();
        _body.sharedMaterial = bodyMat;
        _bodyT = body.transform;

        // A "nose" so facing reads at a glance.
        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Kill(nose.GetComponent<Collider>());
        nose.name = "Nose";
        nose.transform.SetParent(body.transform, false);
        nose.transform.localPosition = new Vector3(0f, 0.6f, 0.55f);
        nose.transform.localScale = new Vector3(0.35f, 0.15f, 0.35f);
        nose.GetComponent<Renderer>().sharedMaterial = bodyMat;
    }

    /// The alien: scaled to Height, feet on the ground, tinted to the team,
    /// bones handed to the rig. The model root is the body the jump/fall move.
    void BuildAlien(GameObject prefab)
    {
        var model = Instantiate(prefab, transform);
        model.name = "Body";
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Kill(c);
        var anim = model.GetComponent<Animator>();
        if (anim != null) anim.enabled = false;      // no controller anyway; the rig owns the bones
        // Height from the skinned bounds at scale 1.
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        float h = smr != null ? smr.bounds.size.y : 0.9f;
        float scale = Height / Mathf.Max(0.3f, h);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        _bodyT = model.transform;
        _body = smr;
        // Team tint over the skin.
        _tint = Color.Lerp(Color.white, team.color, 0.6f);
        if (_body != null)
        {
            _mpb = new MaterialPropertyBlock();
            _mpb.SetColor("_Color", _tint);
            _body.SetPropertyBlock(_mpb);
        }
        _rig = model.AddComponent<FootballAlienRig>();
        _rig.Init(transform);
    }

    /// Offense or defense for the coming play.
    public void SetSide(bool offense)
    {
        role = offense ? offRole : defRole;
        roleIndex = offense ? offIndex : defIndex;
        _maxSpeed = _baseMaxSpeed * (role == FootballRole.DB || role == FootballRole.S ? 1.03f : 1f);
        if (_labelText != null) _labelText.text = Label;
    }

    /// Edit-mode safe destroy — the headless soak (FootballSoakTest) builds
    /// squads outside play mode.
    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    /// Turn to face a direction without moving (lining up).
    /// A small positional correction from contact (a blocker holding a
    /// rusher off). Not a teleport: a few centimetres a tick.
    public void Nudge(Vector3 fieldPos)
    {
        _pos = fieldPos; _pos.y = 0f;
        transform.localPosition = _pos;
    }

    /// Turn to face a direction (at the turn rate) and stop.
    public void Face(Vector3 facing)
    {
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.01f) _facingTarget = facing.normalized;
        _vel = Vector3.zero;
    }

    public void Teleport(Vector3 fieldPos, Vector3 facing)
    {
        _pos = fieldPos; _pos.y = 0f;
        _vel = Vector3.zero;
        if (facing.sqrMagnitude > 0.01f) _facing = _facingTarget = facing.normalized;
        Apply();
    }

    /// How he stands when still (huddle lean, three-point, crouch, ready).
    public void SetStance(Stance s) { _stance = s; }

    /// Go up for the ball (visual + a little extra reach).
    public void Jump()
    {
        if (IsDown || IsJumping || IsHurdling || IsDiving) return;
        _jumpT = 0f; _jumpScale = 1f;
    }

    /// Hit the deck for this long (a tackle, made or missed). Can't move meanwhile.
    public void FallDown(float seconds)
    {
        _downLeft = Mathf.Max(_downLeft, seconds);
        _jumpT = -1f; _throwT = -1f; _jukeT = -1f; _spinT = -1f; _hurdleT = -1f; _diveT = -1f;
        _emoteLeft = 0f; _emote = EmoteKind.None;
    }

    /// Clipped mid-hurdle: a tumble that ends on his back, down for `seconds`.
    public void HardFall(float seconds)
    {
        FallDown(seconds);
        _hardT = 0f;
    }

    public void StandUp() { _downLeft = 0f; }

    /// A celebration or a reaction: stands still and plays it for `seconds`.
    public void Emote(EmoteKind kind, float seconds)
    {
        if (kind == EmoteKind.None) return;
        if (IsDown) { _queuedEmote = kind; _queuedSeconds = seconds; return; }
        _emote = kind; _emoteLeft = _emoteTotal = Mathf.Max(0.3f, seconds);
    }

    /// A hard sidestep toward `sideField` (field-space direction) while still moving forward.
    public void StartJuke(Vector3 sideField)
    {
        if (IsDown || IsJuking || IsSpinning || IsHurdling || IsDiving) return;
        sideField.y = 0f;
        if (sideField.sqrMagnitude < 0.01f) return;
        _jukeT = 0f; _jukeDir = sideField.normalized;
    }

    /// A full turn on the run.
    public void StartSpin()
    {
        if (IsDown || IsJuking || IsSpinning || IsHurdling || IsDiving) return;
        _spinT = 0f;
    }

    /// Leap over a diving tackler; keeps the current heading.
    public void StartHurdle()
    {
        if (IsDown || IsSpinning || IsHurdling || IsDiving) return;
        _hurdleT = 0f; _jukeT = -1f; _jumpT = -1f;
    }

    /// Commit to a lunge along `dirField`: no steering until he is on the ground.
    public void StartDive(Vector3 dirField)
    {
        if (IsDown || IsDiving) return;
        dirField.y = 0f;
        if (dirField.sqrMagnitude < 0.01f) dirField = _facing;
        _diveDir = dirField.normalized; _facing = _facingTarget = _diveDir;
        _diveSpeed = Mathf.Max(_vel.magnitude, _maxSpeed * 0.8f) * 1.35f;
        _diveT = 0f; _jukeT = -1f; _spinT = -1f; _hurdleT = -1f; _jumpT = -1f;
    }

    /// Point both arms at a spot (field space) for this tick — the ball coming in.
    public void ReachFor(Vector3 fieldPos)
    {
        _reachThisTick = true;
        _reachWorld = _fieldRoot != null ? _fieldRoot.TransformPoint(fieldPos) : fieldPos;
    }

    /// Start the throwing motion toward a field-space direction. ThrowReleased
    /// goes true on the tick the hand opens; the ball launches then.
    public void BeginThrow(Vector3 fieldDir)
    {
        if (IsThrowing) return;
        _throwT = 0f;
        // Square up to the target (fast), and a hop off the back foot if he
        // is on the move — no throwing downfield while running backwards.
        fieldDir.y = 0f;
        if (fieldDir.sqrMagnitude > 0.01f) _facingTarget = fieldDir.normalized;
        if (_vel.sqrMagnitude > 4f && !IsJumping) { _jumpT = 0f; _jumpScale = 0.45f; }
        if (_rig != null) _rig.throwDirWorld = _fieldRoot != null ? _fieldRoot.TransformDirection(fieldDir).normalized : fieldDir.normalized;
    }

    public void CancelThrow() { _throwT = -1f; _throwReleasedTick = false; }

    public void BeginKick() { if (_kickT < 0f) _kickT = 0f; }

    /// How this man has the ball (or his hands) right now.
    public void SetHold(HoldStyle style) { _hold = style; }

    /// Where the ball sits while this man holds it (field space): in the hands
    /// for the current hold style, following the throwing arm during a throw.
    public Vector3 BallHoldPoint()
    {
        if (HasRig)
        {
            Vector3 w;
            if (IsThrowing) w = _rig.HandR(_rig.ThrowArmDir(Mathf.Clamp01(_throwT / FootballAlienRig.ThrowSeconds)));
            else w = _rig.HoldPoint(_hold);
            return _fieldRoot != null ? _fieldRoot.InverseTransformPoint(w) : w;
        }
        Vector3 p = _pos + Vector3.up * FootballBall.HoldHeight + _facing * 0.35f;
        if (_hold == HoldStyle.Tucked) p += Right * 0.22f - Vector3.up * 0.15f;
        return p;
    }

    /// How the ball lies in the hands (field space): along the forearm when
    /// tucked, across the body in two hands, along the throw otherwise.
    public Quaternion BallHoldRotation()
    {
        if (IsThrowing) return Quaternion.LookRotation(_facing, Vector3.up);
        switch (_hold)
        {
            case HoldStyle.Tucked:   return Quaternion.LookRotation((_facing * 0.85f + Vector3.up * 0.3f - Right * 0.15f).normalized, Vector3.up);
            case HoldStyle.TwoHands: return Quaternion.LookRotation((Right * 0.8f + _facing * 0.25f).normalized, Vector3.up);
            default:                 return Quaternion.LookRotation(_facing, Vector3.up);
        }
    }

    /// How far outside this man's reach the ball's path came this tick, in
    /// metres (≤ 0 = it touched his arms/hands). With a rig the arms point at
    /// the ball, so reach is the arm's length from either shoulder; a capsule
    /// keeps the old chest sphere. `lowHands`: reacting to a ball he wasn't
    /// expecting — he doesn't get the full stretch.
    public float ArmGap(FootballBall ball, Vector3 prevBallPos, bool lowHands)
    {
        if (HasRig && _fieldRoot != null)
        {
            float reach = _rig.ArmLength * (lowHands ? 0.6f : 1.15f) + BallRadius + 0.08f;
            Vector3 sr = _fieldRoot.InverseTransformPoint(_rig.ShoulderR);
            Vector3 sl = _fieldRoot.InverseTransformPoint(_rig.ShoulderL);
            float d = Mathf.Min(ball.TouchDistance(sr, prevBallPos), ball.TouchDistance(sl, prevBallPos));
            return d - reach;
        }
        Vector3 hands = Chest + Vector3.up * (JumpReach + (lowHands ? -0.25f : 0.25f));
        float r = lowHands ? 0.7f : 1.3f;
        return ball.TouchDistance(hands, prevBallPos) - r;
    }

    /// Ball-carrier / spotlight tint: a brighter copy of the team colour.
    public void SetHighlight(bool on)
    {
        if (_body == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        if (on) _mpb.SetColor("_Color", Color.Lerp(_tint, Color.white, 0.5f));
        else if (HasRig) _mpb.SetColor("_Color", _tint);
        if (on || HasRig) _body.SetPropertyBlock(_mpb);
        else _body.SetPropertyBlock(null);
    }

    // ── per tick ───────────────────────────────────────────────────────────

    /// Runs the brain and moves. Returns the brain's output so PlayInstance
    /// can carry out any action.
    public BrainOutput Tick(PlayView view, float dt)
    {
        var o = new BrainOutput();
        if (brain != null && !IsDown) brain.Tick(this, view, dt, ref o);

        // Timers: the hop, time on the ground, the throw, the kick, the moves.
        _throwReleasedTick = false; _kickReleasedTick = false;
        if (_jumpT >= 0f) { _jumpT += dt; if (_jumpT >= JumpSeconds) _jumpT = -1f; }
        if (_throwT >= 0f)
        {
            float before = _throwT / FootballAlienRig.ThrowSeconds;
            _throwT += dt;
            float after = _throwT / FootballAlienRig.ThrowSeconds;
            if (before < FootballAlienRig.ThrowRelease && after >= FootballAlienRig.ThrowRelease) _throwReleasedTick = true;
            if (after >= 1f) _throwT = -1f;
        }
        if (_kickT >= 0f)
        {
            float before = _kickT / FootballAlienRig.KickSeconds;
            _kickT += dt;
            float after = _kickT / FootballAlienRig.KickSeconds;
            if (before < 0.7f && after >= 0.7f) _kickReleasedTick = true;
            if (after >= 1f) _kickT = -1f;
        }
        if (_jukeT >= 0f) { _jukeT += dt; if (_jukeT >= JukeSeconds) _jukeT = -1f; }
        if (_spinT >= 0f) { _spinT += dt; if (_spinT >= SpinSeconds) _spinT = -1f; }
        if (_hurdleT >= 0f) { _hurdleT += dt; if (_hurdleT >= HurdleSeconds) _hurdleT = -1f; }
        if (_diveT >= 0f)
        {
            _diveT += dt;
            // A dive ends on the ground whatever it hit.
            if (_diveT >= DiveSeconds) { _diveT = -1f; if (!IsDown) FallDown(0.9f); }
        }
        if (_hardT >= 0f) { _hardT += dt; if (_hardT >= HardFallSeconds) _hardT = -1f; }
        if (_emoteLeft > 0f) { _emoteLeft -= dt; if (_emoteLeft <= 0f) { _emoteLeft = 0f; _emote = EmoteKind.None; } }
        if (_downLeft > 0f) { _downLeft -= dt; o.move = Vector3.zero; o.action = BrainAction.None; }
        _lie = Mathf.MoveTowards(_lie, IsDown ? 1f : 0f, dt / (IsDown ? 0.22f : 0.35f));
        if (_queuedEmote != EmoteKind.None && !IsDown && _lie < 0.15f)
        {
            _emote = _queuedEmote; _emoteLeft = _emoteTotal = Mathf.Max(0.3f, _queuedSeconds);
            _queuedEmote = EmoteKind.None;
        }
        // A tumble rolls him onto his back; he rolls back over as he gets up.
        float rollTarget = _hardT >= 0f ? 180f * Mathf.SmoothStep(0f, 1f, _hardT / HardFallSeconds) : (IsDown && _roll > 90f ? 180f : 0f);
        _roll = Mathf.MoveTowards(_roll, rollTarget, dt * (_hardT >= 0f ? 600f : 400f));

        Vector3 want = o.move;
        want.y = 0f;
        if (want.sqrMagnitude > 1f) want.Normalize();
        // Planted while throwing or kicking; standing still for a celebration.
        if (IsThrowing || _kickT >= 0f) want *= 0.15f;
        if (IsEmoting) want = Vector3.zero;
        // The moves override steering: a juke is a hard sidestep, a hurdle
        // keeps the line, a spin carries on at a jog, a dive is a lunge.
        float moveScale = 1f;
        if (IsJuking) { want = (_jukeDir * 0.85f + _facing * 0.55f).normalized; }
        else if (IsHurdling) { want = _facing; moveScale = 0.95f; }
        else if (IsSpinning) { want = want.sqrMagnitude > 0.01f ? want : _facing; moveScale = 0.7f; }
        Vector3 targetVel = want * (_maxSpeed * speedScale * moveScale);
        // On the ground: skid to a stop.
        if (IsDown) targetVel = Vector3.zero;
        if (IsDiving)
        {
            float p = DivePhase;
            _vel = _diveDir * (_diveSpeed * (1f - 0.6f * p));
        }
        else _vel = Vector3.MoveTowards(_vel, targetVel, (IsDown ? Accel * 2.5f : Accel) * dt);
        _pos += _vel * dt;
        _pos.y = 0f;
        // A soft wall while the play is live: the field ends at the lines.
        if (view != null && view.snapped)
        {
            float hw = FootballField.HalfWidth + FieldMargin, el = FootballField.EndLineZ + FieldMargin;
            if (_pos.x > hw) { _pos.x = hw; if (_vel.x > 0f) _vel.x = 0f; }
            if (_pos.x < -hw) { _pos.x = -hw; if (_vel.x < 0f) _vel.x = 0f; }
            if (_pos.z > el) { _pos.z = el; if (_vel.z > 0f) _vel.z = 0f; }
            if (_pos.z < -el) { _pos.z = -el; if (_vel.z < 0f) _vel.z = 0f; }
        }
        if (IsDiving) { }
        else if (IsSpinning || IsHurdling) { }                                        // heading locked through the move
        else if (_vel.sqrMagnitude > 0.25f && !IsThrowing && !IsJuking) _facingTarget = _vel.normalized;
        else if (_vel.sqrMagnitude <= 0.25f && o.face.sqrMagnitude > 0.01f) { o.face.y = 0f; _facingTarget = o.face.normalized; }
        // Turn toward it at a rate: quick on the run, a body turn when standing.
        float rate = IsThrowing ? 900f : _vel.sqrMagnitude > 4f ? TurnRateRunning : TurnRateStanding;
        _facing = Vector3.RotateTowards(_facing, _facingTarget, rate * Mathf.Deg2Rad * dt, 0f);
        _facing.y = 0f; if (_facing.sqrMagnitude < 1e-4f) _facing = _facingTarget; _facing.Normalize();
        _idleTime = _vel.sqrMagnitude < 0.25f ? _idleTime + dt : 0f;
        Apply(dt);
        return o;
    }

    void Apply() { Apply(0f); }

    void Apply(float dt)
    {
        transform.localPosition = _pos;
        transform.localRotation = Quaternion.LookRotation(_facing, Vector3.up);
        if (_bodyT == null) return;
        // Body root: hop for a catch, lift for a hurdle, a pop on a tumble;
        // tip forward onto the ground when down (or into a dive); yaw through
        // a spin; roll onto the back after a tumble; lean into a juke.
        float hop = IsJumping ? JumpHeight * _jumpScale * Mathf.Sin(Mathf.PI * _jumpT / JumpSeconds) : 0f;
        if (IsHurdling) hop += HurdleHeight * Mathf.Sin(Mathf.PI * HurdlePhase);
        if (_hardT >= 0f) hop += 0.55f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_hardT / HardFallSeconds));
        if (IsDiving) hop += 0.3f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(DivePhase * 1.4f));
        float tip = _lie * 90f;
        if (IsDiving) tip = Mathf.Max(tip, 78f * Mathf.SmoothStep(0f, 1f, DivePhase * 1.15f));
        else if (IsHurdling) tip = Mathf.Max(tip, 14f * Mathf.Sin(Mathf.PI * HurdlePhase));
        float yaw = IsSpinning ? 360f * SpinPhase : 0f;
        float lean = 0f;
        if (IsJuking)
        {
            float side = Vector3.Dot(_jukeDir, Right);
            lean = -side * 20f * Mathf.Sin(Mathf.PI * JukePhase);
        }
        if (HasRig)
        {
            // The model's pivot is at the feet: tip about them, lift for the hop.
            _bodyT.localRotation = Quaternion.Euler(tip, yaw, lean) * Quaternion.AngleAxis(_roll, Vector3.forward);
            _bodyT.localPosition = new Vector3(0f, hop + _lie * 0.15f, 0f);
            _rig.speedFrac = Mathf.Clamp01(_vel.magnitude / Mathf.Max(1f, _maxSpeed));
            _rig.stridePhase += _vel.magnitude * dt * (2f * Mathf.PI / StrideMetres);
            _rig.reaching = _reachThisTick;
            _rig.reachTargetWorld = _reachWorld;
            _rig.throwPhase = IsThrowing ? Mathf.Clamp01(_throwT / FootballAlienRig.ThrowSeconds) : -1f;
            _rig.kickPhase = _kickT >= 0f ? Mathf.Clamp01(_kickT / FootballAlienRig.KickSeconds) : -1f;
            _rig.hold = _hold;
            _rig.emote = IsEmoting ? _emote : EmoteKind.None;
            _rig.emotePhase = IsEmoting ? 1f - _emoteLeft / _emoteTotal : 0f;
            _rig.hurdlePhase = HurdlePhase;
            _rig.divePhase = DivePhase;
            _rig.spinPhase = SpinPhase;
            _rig.hardFall = _hardT >= 0f ? Mathf.Clamp01(_hardT / HardFallSeconds) : 0f;
            _rig.stance = _stance;
            _rig.idleTime = _idleTime;
            _rig.blocking = blocking;
            _reachThisTick = false;
            return;
        }
        float radius = 0.35f;
        // Rotating about the feet: the capsule's centre swings forward and down.
        Vector3 centre = Quaternion.Euler(tip, 0f, 0f) * new Vector3(0f, Height * 0.5f, 0f);
        centre.y = Mathf.Max(centre.y, radius) + hop;
        _bodyT.localPosition = centre;
        _bodyT.localRotation = Quaternion.Euler(tip, yaw, lean);
        _reachThisTick = false;
    }

    /// Everything the replay needs to redraw this man on a ghost: where he is,
    /// how the body root is posed, and the rig's inputs. Field space throughout.
    public struct PoseFrame
    {
        public Vector3 pos, facing, bodyLocalPos; public Quaternion bodyLocalRot;
        public float speedFrac, stridePhase, throwPhase, kickPhase, emotePhase, hurdlePhase, divePhase, spinPhase, hardFall, idleTime;
        public bool reaching; public Vector3 reachField, throwDirField, ballField;
        public HoldStyle hold; public EmoteKind emote; public Stance stance;
    }

    public PoseFrame CapturePose()
    {
        var f = new PoseFrame { pos = _pos, facing = _facing, hold = _hold, stance = _stance, idleTime = _idleTime };
        if (_bodyT != null) { f.bodyLocalPos = _bodyT.localPosition; f.bodyLocalRot = _bodyT.localRotation; }
        if (_rig != null)
        {
            f.speedFrac = _rig.speedFrac; f.stridePhase = _rig.stridePhase; f.reaching = _rig.reaching;
            f.reachField = _fieldRoot != null ? _fieldRoot.InverseTransformPoint(_rig.reachTargetWorld) : _rig.reachTargetWorld;
            f.throwPhase = _rig.throwPhase; f.throwDirField = _fieldRoot != null ? _fieldRoot.InverseTransformDirection(_rig.throwDirWorld) : _rig.throwDirWorld;
            f.ballField = _fieldRoot != null ? _fieldRoot.InverseTransformPoint(_rig.ballWorld) : _rig.ballWorld;
            f.kickPhase = _rig.kickPhase; f.emote = _rig.emote; f.emotePhase = _rig.emotePhase;
            f.hurdlePhase = _rig.hurdlePhase; f.divePhase = _rig.divePhase; f.spinPhase = _rig.spinPhase; f.hardFall = _rig.hardFall;
        }
        return f;
    }

    /// Pose a ghost (a bare model + rig under `ghostRoot`, a child of FieldRoot) from a frame.
    public static void ApplyPose(Transform ghostRoot, Transform body, FootballAlienRig rig, Transform fieldRoot, in PoseFrame f)
    {
        ghostRoot.localPosition = f.pos;
        ghostRoot.localRotation = Quaternion.LookRotation(f.facing.sqrMagnitude > 0.01f ? f.facing : Vector3.forward, Vector3.up);
        if (body != null) { body.localPosition = f.bodyLocalPos; body.localRotation = f.bodyLocalRot; }
        if (rig == null) return;
        rig.speedFrac = f.speedFrac; rig.stridePhase = f.stridePhase; rig.reaching = f.reaching;
        rig.reachTargetWorld = fieldRoot.TransformPoint(f.reachField);
        rig.throwPhase = f.throwPhase; rig.throwDirWorld = fieldRoot.TransformDirection(f.throwDirField);
        rig.ballWorld = fieldRoot.TransformPoint(f.ballField);
        rig.kickPhase = f.kickPhase; rig.hold = f.hold; rig.emote = f.emote; rig.emotePhase = f.emotePhase;
        rig.hurdlePhase = f.hurdlePhase; rig.divePhase = f.divePhase; rig.spinPhase = f.spinPhase; rig.hardFall = f.hardFall;
        rig.stance = f.stance; rig.idleTime = f.idleTime;
    }

    /// The ball on the grass in front of the centre (world) — his hands go to it.
    public void SetBallWorld(Vector3 fieldPos)
    {
        if (_rig != null) _rig.ballWorld = _fieldRoot != null ? _fieldRoot.TransformPoint(fieldPos) : fieldPos;
    }

    void LateUpdate()
    {
        // Label faces the camera. Camera.main is cached; re-found on a throttle
        // if it goes missing (scene load, pod cams), never every frame.
        if (_label == null) return;
        if (_cam == null && Time.unscaledTime >= _camRetry)
        {
            var c = Camera.main;
            _cam = c != null ? c.transform : null;
            _camRetry = Time.unscaledTime + 1f;
        }
        if (_cam != null) _label.rotation = _cam.rotation;
    }
}
