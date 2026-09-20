using UnityEngine;

/// <summary>
/// Phase 2 (Sam, 2026-09-19): the player plays quarterback for the blue team.
///
/// A red button on the home sideline (Interactable, press F) subs the player
/// in: from then on, whenever the blue team has the ball at scrimmage, its QB
/// alien jogs to the bench and disappears, and the QB SLOT is driven by the
/// player — position and facing copied from the real PlayerController every
/// sim step (FootballPlayer.SyncHuman), a HumanQBBrain instead of the CPU one.
/// Nothing else in the sim knows: catches, tackles, throws and the broadcast
/// all work on the slot as before. On kickoffs and defense the alien plays.
///
/// The snap: once everyone is lined up a green cylinder shows the QB spot;
/// walk into it, it vanishes, a second later the centre snaps to your hands.
/// The ball goes into the hotbar (ItemId.Football, auto-equipped) and is held
/// out in front (PlayerPickup.holdPosition). Hold LMB to charge a throw (up to
/// 2 s), aim with the camera; the arc and the landing spot are drawn while you
/// charge; release to throw (to whichever receiver is nearest the spot). Or
/// cross the line and run it. Tackled = the play ends, you're planted for a
/// moment. The ball is dropped at your feet after the whistle for the centre.
/// </summary>
public class FootballHumanQB : MonoBehaviour
{
    public Sprite hotbarIcon;
    public float chargeSeconds = 2f;
    public float minThrow = 6f, maxThrow = 48f;      // metres, at 0 and full charge
    public float fieldMoveScale = 0.8f;              // the astronaut's stride on the field (walk 8 / run 14 would make him uncatchable)
    public float knockdownSeconds = 1.6f;
    [Tooltip("Where the ball sits in the hands, in camera space (right, up, forward).")]
    public Vector3 handOffset = new Vector3(0.16f, -0.26f, 0.55f);

    public bool Active { get; private set; }
    /// Hotbar: the ball is an item while you hold it.
    public bool IsUnlocked => _holding;
    public bool IsEquipped => _holding && _equipped;
    public void ForceEquip() { _equipped = true; }
    public void ForceUnequip() { _equipped = false; }
    public HumanQBBrain Brain => _brain;
    /// Beside the button, off the home sideline: where the alien QB waits.
    public Vector3 BenchSpot => new Vector3(-(FootballField.HalfWidth + 3.5f), 0f, 4f);

    FootballMatch _match;
    Transform _fieldRoot;
    PlayerController _player;
    Transform _hold;
    FootballPlayer _slot;
    HumanQBBrain _brain;
    GameObject _button, _cylinder;
    LineRenderer _arc, _landing;
    bool _holding, _equipped, _wasHolding, _charging;
    float _charge, _downUntil = -1f, _equipRetry = -1f;
    bool _throwReady; Vector3 _throwTarget; FootballPlayer _throwTo;
    bool _scaled;

    void Start()
    {
        _match = FootballMatch.Instance;
        if (_match == null) { enabled = false; return; }
        _fieldRoot = transform.parent != null ? transform.parent : transform;
        _player = FindObjectOfType<PlayerController>();
        // The hands: an anchor under the camera's view frame (the pistol's
        // trick — PlayerPickup.holdPosition is a scene-authored point and in the
        // proto scene it sits 2 m under the turf, so the snap flew into the ground).
        if (_player != null)
        {
            Transform eye = _player.Camera != null ? CameraTransformFX.ViewFrameOf(_player.Camera.transform) : null;
            if (eye == null) eye = _player.Camera != null ? _player.Camera.transform : _player.transform;
            var hg = new GameObject("FootballHold");
            hg.transform.SetParent(eye, false);
            hg.transform.localPosition = handOffset;
            hg.transform.localRotation = Quaternion.identity;
            _hold = hg.transform;
        }
        _brain = new HumanQBBrain(this);
        if (hotbarIcon == null) hotbarIcon = BuildIcon();
        BuildButton();
        BuildAimVisuals();
        _match.PlayEnded += OnPlayEnded;
    }

    void OnDestroy()
    {
        if (_match != null) _match.PlayEnded -= OnPlayEnded;
        RestoreMoveScale();
    }

    // ── the button ─────────────────────────────────────────────────────────

    void BuildButton()
    {
        _button = new GameObject("QB Button");
        _button.transform.SetParent(_fieldRoot, false);
        _button.transform.localPosition = new Vector3(-(FootballField.HalfWidth + 3.5f), 0f, 0f);
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "Post";
        post.transform.SetParent(_button.transform, false);
        post.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        post.transform.localScale = new Vector3(0.35f, 0.5f, 0.35f);
        post.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.25f, 0.28f) };
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Button";
        dome.transform.SetParent(_button.transform, false);
        dome.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        dome.transform.localScale = new Vector3(0.45f, 0.3f, 0.45f);
        var dm = new Material(Shader.Find("Standard")) { color = new Color(0.95f, 0.1f, 0.08f) };
        dm.EnableKeyword("_EMISSION"); dm.SetColor("_EmissionColor", new Color(0.6f, 0.02f, 0.02f));
        dome.GetComponent<Renderer>().sharedMaterial = dm;
        // The shipped interaction: a trigger zone for range, the dome as the gaze
        // target, F to press.
        var zone = _button.AddComponent<SphereCollider>();
        zone.isTrigger = true; zone.radius = 2.6f; zone.center = new Vector3(0f, 1f, 0f);
        var inter = _button.AddComponent<Interactable>();
        inter.interactMessage = "Press F to play QB";
        inter.gazeTarget = dome.transform;
        inter.interactEvent = new UnityEngine.Events.UnityEvent();
        inter.interactEvent.AddListener(OnButton);
        // The post's collider lets the gaze cast land; the dome keeps its own.
    }

    public void OnButton()
    {
        Active = !Active;
        var inter = _button.GetComponent<Interactable>();
        if (inter != null) inter.interactMessage = Active ? "Press F to sub out" : "Press F to play QB";
        InteractPromptUI.ShowOneShot(Active ? "You're in at QB for the " + _match.away.name + " — you take the field when they have the ball" : "Back to the sideline", 4f);
        _match.humanQb = Active ? this : null;
    }

    // ── per frame ──────────────────────────────────────────────────────────

    void Update()
    {
        if (_match == null || _player == null) return;
        var play = _match.CurrentPlay;
        bool driving = play != null && play.HumanDriven && play.humanSlot != null && play.humanSlot.humanDriven;
        _slot = play != null ? play.humanSlot : null;
        var ball = _match.Ball;
        _holding = driving && ball != null && ball.state == FootballBall.State.Held && ball.holder == _slot;

        // The snap spot.
        bool showSpot = driving && play.phase == PlayInstance.Phase.Setup && play.LinedUp && !play.humanAtSpot;
        if (_cylinder != null) _cylinder.SetActive(showSpot);
        if (showSpot)
        {
            Vector3 spot = play.HumanSnapSpot;
            _cylinder.transform.localPosition = spot + Vector3.up * 1.25f;
            Vector3 me = _fieldRoot.InverseTransformPoint(_player.transform.position);
            if (Vector3.Distance(new Vector3(me.x, 0f, me.z), spot) < 1.0f)
            {
                play.humanAtSpot = true;
                InteractPromptUI.ShowOneShot("Set — the snap is coming", 1.5f);
            }
        }

        // The hotbar: the ball appears while held; equip it when it arrives.
        if (_holding && !_wasHolding)
        {
            _equipRetry = 0.6f;
            InteractPromptUI.ShowOneShot("Hold LMB to charge a throw, release to throw — or run it", 3f);
        }
        if (!_holding) { _charging = false; _charge = 0f; if (_arc != null) _arc.enabled = false; if (_landing != null) _landing.enabled = false; }
        if (_equipRetry > 0f)
        {
            _equipRetry -= Time.deltaTime;
            if (Hotbar.Instance != null && Hotbar.Instance.HasItem(Hotbar.ItemId.Football)) { Hotbar.Instance.EquipItem(Hotbar.ItemId.Football); _equipRetry = -1f; }
        }
        _wasHolding = _holding;

        // Throwing.
        bool live = play != null && play.phase == PlayInstance.Phase.Live;
        if (_holding && live && !_brain.Tucked && Time.time >= _downUntil)
        {
            if (Input.GetMouseButtonDown(0)) { _charging = true; _charge = 0f; }
            if (_charging && Input.GetMouseButton(0))
            {
                _charge = Mathf.Min(chargeSeconds, _charge + Time.deltaTime);
                DrawAim(Aim());
            }
            if (_charging && Input.GetMouseButtonUp(0))
            {
                _charging = false;
                Vector3 target = Aim();
                _throwTarget = target;
                _throwTo = NearestReceiver(play, target);
                _throwReady = _throwTo != null;
                if (_arc != null) _arc.enabled = false; if (_landing != null) _landing.enabled = false;
            }
        }

        // Stride: a football stride on the field; planted after a tackle.
        if (driving)
        {
            float scale = Time.time < _downUntil ? 0f : fieldMoveScale;
            _player.introMoveScale = scale; _scaled = true;
        }
        else RestoreMoveScale();
    }

    void RestoreMoveScale()
    {
        if (_scaled && _player != null) { _player.introMoveScale = 1f; _scaled = false; }
    }

    void LateUpdate()
    {
        // The ball in the player's hands: the hold point, after the camera has moved.
        if (_holding && _hold != null && _match.Ball != null)
        {
            var b = _match.Ball.transform;
            b.position = _hold.position + _hold.forward * 0.08f - _hold.up * 0.02f;
            b.rotation = _hold.rotation * Quaternion.Euler(0f, 90f, 20f);
        }
    }

    /// Where the throw comes down (field space): along the camera's flat
    /// forward, further the longer LMB was held, inside the field.
    Vector3 Aim()
    {
        var cam = _player.Camera != null ? _player.Camera.transform : _player.transform;
        Vector3 fwd = _fieldRoot.InverseTransformDirection(cam.forward); fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
        Vector3 me = _fieldRoot.InverseTransformPoint(_player.transform.position); me.y = 0f;
        float dist = Mathf.Lerp(minThrow, maxThrow, _charge / chargeSeconds);
        Vector3 t = me + fwd * dist;
        t.x = Mathf.Clamp(t.x, -(FootballField.HalfWidth - 0.5f), FootballField.HalfWidth - 0.5f);
        t.z = Mathf.Clamp(t.z, -(FootballField.EndLineZ - 0.5f), FootballField.EndLineZ - 0.5f);
        return t;
    }

    static FootballPlayer NearestReceiver(PlayInstance play, Vector3 target)
    {
        FootballPlayer best = null; float bd = float.MaxValue;
        foreach (var p in play.view.players)
        {
            if (p.team != play.view.offense || p == play.humanSlot || p.role == FootballRole.OL || p.role == FootballRole.C) continue;
            float d = Vector3.Distance(p.Pos, target);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    /// The brain asks once per tick: is there a throw to make?
    public bool TakeThrow(out Vector3 target, out FootballPlayer to)
    {
        target = _throwTarget; to = _throwTo;
        if (!_throwReady) return false;
        _throwReady = false;
        return true;
    }

    /// Field-space point the camera looks at (for the slot's head).
    public Vector3 LookPoint()
    {
        var cam = _player.Camera != null ? _player.Camera.transform : _player.transform;
        return _fieldRoot.InverseTransformPoint(cam.position + cam.forward * 12f);
    }

    /// Copy the real player into the QB slot (called by the match before each sim step).
    public void SyncSlot(float dt)
    {
        if (_slot == null || !_slot.humanDriven || _player == null) return;
        Vector3 pos = _fieldRoot.InverseTransformPoint(_player.transform.position); pos.y = 0f;
        var cam = _player.Camera != null ? _player.Camera.transform : _player.transform;
        Vector3 facing = _fieldRoot.InverseTransformDirection(cam.forward); facing.y = 0f;
        Vector3 hands = _hold != null ? _fieldRoot.InverseTransformPoint(_hold.position) : pos + Vector3.up * 1.1f + facing.normalized * 0.4f;
        _slot.SyncHuman(pos, facing, hands, dt);
    }

    void OnPlayEnded(PlayInstance.PlayResult r)
    {
        if (_slot == null || r.carrier != _slot || r.isKickoff) return;
        if (r.outcome == PlayInstance.Outcome.Run || r.outcome == PlayInstance.Outcome.Sack || r.outcome == PlayInstance.Outcome.Fumble || r.outcome == PlayInstance.Outcome.Whistle)
        {
            _downUntil = Time.time + knockdownSeconds;
            InteractPromptUI.ShowOneShot(r.outcome == PlayInstance.Outcome.Sack ? "Sacked!" : "Tackled", 1.5f);
        }
        else if (r.touchdown) InteractPromptUI.ShowOneShot("TOUCHDOWN!", 3f);
    }

    // ── visuals: the snap spot, the aim arc ────────────────────────────────

    void BuildAimVisuals()
    {
        _cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(_cylinder.GetComponent<Collider>());
        _cylinder.name = "SnapSpot";
        _cylinder.transform.SetParent(_fieldRoot, false);
        _cylinder.transform.localScale = new Vector3(2.2f, 1.25f, 2.2f);
        var m = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 1f, 0.3f, 0.35f) };
        m.SetFloat("_Mode", 3f); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000;
        var mr = _cylinder.GetComponent<Renderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _cylinder.SetActive(false);

        _arc = MakeLine("ThrowArc", new Color(1f, 0.9f, 0.3f), 0.06f, false);
        _landing = MakeLine("ThrowLanding", new Color(1f, 0.9f, 0.3f), 0.08f, true);
        _landing.positionCount = 36;
    }

    LineRenderer MakeLine(string name, Color c, float width, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_fieldRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = loop; lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        var sh = Shader.Find("Sprites/Default");
        lr.sharedMaterial = new Material(sh != null ? sh : Shader.Find("Unlit/Color")) { color = Color.white };
        lr.startColor = lr.endColor = c;
        lr.enabled = false;
        return lr;
    }

    /// The parabola the ball will fly (same maths as FootballBall.Launch) and a ring where it lands.
    void DrawAim(Vector3 target)
    {
        if (_arc == null || _slot == null) return;
        Vector3 start = _slot.BallHoldPoint();
        Vector3 end = new Vector3(target.x, FootballBall.CatchHeight, target.z);
        float flight = PlayInstance.PassFlightTime(Vector3.Distance(start, end));
        Vector3 v = (end - start) / flight + Vector3.up * (0.5f * FootballBall.Gravity * flight);
        const int N = 24;
        _arc.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float t = flight * i / (N - 1);
            _arc.SetPosition(i, start + v * t + Vector3.down * (0.5f * FootballBall.Gravity * t * t));
        }
        _arc.enabled = true;
        for (int i = 0; i < 36; i++) { float a = i * Mathf.PI * 2f / 36f; _landing.SetPosition(i, new Vector3(target.x + Mathf.Cos(a) * 1.2f, 0.05f, target.z + Mathf.Sin(a) * 1.2f)); }
        _landing.enabled = true;
    }

    /// A football drawn in code for the hotbar (brown, laces), like the grapple / beer icons.
    static Sprite BuildIcon()
    {
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        var clear = new Color32(0, 0, 0, 0);
        var brown = new Color32(120, 62, 30, 255); var dark = new Color32(80, 40, 18, 255); var white = new Color32(245, 240, 230, 255);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // A tilted ellipse.
                float u = (x - S * 0.5f) / (S * 0.46f), w = (y - S * 0.5f) / (S * 0.28f);
                float ru = u * 0.7071f - w * 0.7071f, rw = u * 0.7071f + w * 0.7071f;
                float d = ru * ru + rw * rw;
                Color32 c = clear;
                if (d <= 1f) { c = d > 0.85f ? dark : brown; if (Mathf.Abs(ru) < 0.35f && Mathf.Abs(rw) < 0.05f) c = white; if (Mathf.Abs(rw) < 0.22f && Mathf.Abs(ru) < 0.03f) c = white; }
                px[y * S + x] = c;
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 96f);
    }
}

/// The player's brain for the QB slot: no movement (the slot is synced to the
/// real player), a throw when the controller has one ready, a tuck the moment
/// he crosses the line with it.
public class HumanQBBrain : IPlayerBrain
{
    readonly FootballHumanQB _h;
    bool _tucked;
    public bool Tucked => _tucked;
    public HumanQBBrain(FootballHumanQB h) { _h = h; }
    public bool KeepsControlWhenCarrying => true;
    public void Reset() { _tucked = false; }

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        o.move = Vector3.zero;
        o.look = _h.LookPoint();
        if (!view.snapped || view.Carrier != self) return;
        if (_h.TakeThrow(out Vector3 target, out FootballPlayer to))
        {
            o.action = BrainAction.Throw; o.target = target; o.targetPlayer = to; o.power = 0.5f;
            o.say = self.team.shortName + " QB (you) throws toward " + to.Label + ", " + Mathf.RoundToInt(FootballField.ToYards(view.Downfield(target))) + " yds downfield";
            return;
        }
        if (!_tucked && view.Downfield(self.Pos) > -0.3f)
        {
            _tucked = true;
            o.action = BrainAction.Handoff; o.targetPlayer = self;
            o.say = self.team.shortName + " QB (you) crosses the line — it's a run";
        }
    }
}
