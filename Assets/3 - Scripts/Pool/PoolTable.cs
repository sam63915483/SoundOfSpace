using UnityEngine;

/// <summary>
/// The pool table in the world (prefab root, built by Tools ▸ Pool ▸ Build Pool
/// Table Prefab). Owns the ball sim (PoolPhysics2D — our own table-local 2D
/// physics, never Unity rigidbodies: see that file for why) and turns it into
/// ball positions every frame, whether or not anyone is playing. Walk away
/// mid-shot and the balls keep rolling.
///
/// Interactable: look at the table, press F → PoolShotSession.Open() (the
/// component next to this one) borrows the camera and runs the shot.
///
/// Game record: `Game` (PoolGameState) — who is on the table, what each player
/// sank, stripes/solids, the 8-ball verdict. Free play except the 8: the table
/// spots it back if nobody has a group yet, otherwise fires GameResult and
/// re-racks itself after the banner. Ball in hand (G): the cue ball is lifted
/// out of the sim and hovers where the shooter slides it, kitchen only.
///
/// Everything visual is a CHILD of this transform positioned in local metres,
/// so the table rides Humble Abode's orbit and the floating origin for free.
/// Scale the root and the whole game scales with it.
/// </summary>
public class PoolTable : Interactable
{
    [Header("Wiring (set by the builder)")]
    [Tooltip("16 ball roots, index 0 = cue ball. Each is a child positioned in table-local metres.")]
    public Transform[] balls = new Transform[PoolPhysics2D.BallCount];
    [Tooltip("6 pocket anchors on the felt: 0..3 corners (−x−y, +x−y, −x+y, +x+y), 4..5 sides (−y, +y).")]
    public Transform[] pocketAnchors = new Transform[6];
    [Tooltip("Height of the cloth above the table root (metres, local).")]
    public float feltY = 0.80f;
    [Tooltip("Cue stick root: tip at its origin, +Z runs from the tip toward the butt.")]
    public Transform cue;
    public LineRenderer guideLine;
    public LineRenderer contactRing;
    public LineRenderer objectStub;

    [Header("Sim (metres, table-local; mirrors PoolPhysics2D)")]
    public float halfLength = 0.99f;
    public float halfWidth = 0.495f;
    public float ballRadius = 0.028575f;
    public float rollingDecel = 0.16f;
    public float linearDrag = 0.05f;
    public float ballRestitution = 0.96f;
    public float cushionRestitution = 0.78f;

    [Header("Feel")]
    [Tooltip("Seconds a pocketed ball takes to drop out of sight.")]
    public float pocketDropSeconds = 0.35f;
    [Tooltip("How far below the cloth a pocketed ball sinks before it disappears.")]
    public float pocketDropDepth = 0.06f;
    [Tooltip("Pause after the table settles before a scratched cue ball is handed to the shooter (ball in hand).")]
    public float cueRespawnDelay = 0.6f;
    [Tooltip("Pause after the last object ball drops before the table re-racks itself.")]
    public float autoRerackDelay = 1.5f;
    [Tooltip("How long the YOU WIN / YOU LOSE banner stays before the table re-racks itself.")]
    public float bannerSeconds = 2.5f;
    [Tooltip("Ball-in-hand: how fast the cue ball slides (m/s, table-local).")]
    public float handMoveSpeed = 0.5f;
    [Tooltip("Ball-in-hand: how high the cue ball hovers, in ball radii.")]
    public float handLiftRadii = 0.6f;

    public PoolPhysics2D Sim { get; private set; }
    /// The running game on this table (who's on it, trays, groups, 8-ball verdict).
    public PoolGameState Game { get; private set; }
    /// True from a rack until the first strike — the first shot faces the rack.
    public bool FreshRack { get; private set; } = true;
    /// True while the cue ball is off the table (scratched) and not yet placed by hand.
    public bool CueRespawnPending => _cueRespawnPending;
    /// True while the cue ball is lifted for ball in hand.
    public bool BallInHand => _inHand;
    /// True while the ball in hand came from a scratch (G drops it on the head spot instead of "back where it was").
    public bool BallInHandFromScratch { get; private set; }
    /// While in hand: would putting it down here overlap another ball?
    public bool BallInHandBlocked { get; private set; }
    /// Cue ball position while in hand (table-local metres, on the cloth).
    public Vector3 BallInHandLocal => SimToLocal(_handX, _handY);

    /// (win, reason) — the 8 ball just ended the game. The banner lasts `bannerSeconds`, then the table re-racks.
    public event System.Action<bool, string> GameResult;

    /// The local player's id for the game record: the Netcode client id in co-op, 0 solo.
    public static ulong LocalPlayerId
    {
        get
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null && (nm.IsClient || nm.IsServer) ? nm.LocalClientId : 0UL;
        }
    }

    PoolShotSession _session;
    readonly Quaternion[] _roll = new Quaternion[PoolPhysics2D.BallCount];
    readonly float[] _prevX = new float[PoolPhysics2D.BallCount];
    readonly float[] _prevY = new float[PoolPhysics2D.BallCount];
    // drop animation
    readonly float[] _dropT = new float[PoolPhysics2D.BallCount];     // <0 = not dropping
    readonly Vector3[] _dropFrom = new Vector3[PoolPhysics2D.BallCount];
    readonly Vector3[] _dropTo = new Vector3[PoolPhysics2D.BallCount];
    bool _cueRespawnPending;
    float _respawnTimer;
    float _rerackTimer;
    // game
    bool _shotWasBreak;            // captured at strike time: pockets from this shot are break pockets
    bool _spot8Pending;            // the 8 dropped with nobody's group decided → respot when settled
    float _gameOverTimer = -1f;    // ≥0 while the win/lose banner runs
    // ball in hand
    bool _inHand;
    float _handX, _handY, _handFromX, _handFromY;
    MaterialPropertyBlock _cueTint;
    Renderer _cueRenderer;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        Sim = new PoolPhysics2D
        {
            HalfLength = halfLength, HalfWidth = halfWidth, BallRadius = ballRadius,
            RollingDecel = rollingDecel, LinearDrag = linearDrag,
            BallRestitution = ballRestitution, CushionRestitution = cushionRestitution,
        };
        Sim.Configure();
        Sim.Rack();
        Sim.BallPocketed += OnPocketed;
        Game = new PoolGameState();
        _cueTint = new MaterialPropertyBlock();
        if (balls != null && balls.Length > 0 && balls[PoolPhysics2D.Cue] != null)
            _cueRenderer = balls[PoolPhysics2D.Cue].GetComponent<Renderer>();
        _session = GetComponent<PoolShotSession>();

        // Interact range: a trigger sphere, like BeerCupPickup. The root's solid
        // BoxCollider is what the crosshair SphereCast (InteractGaze) hits.
        bool hasTrigger = false;
        foreach (var c in GetComponents<Collider>()) if (c.isTrigger) { hasTrigger = true; break; }
        if (!hasTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 3.5f;
            sc.center = new Vector3(0f, feltY * 0.5f, 0f);
        }

        for (int i = 0; i < PoolPhysics2D.BallCount; i++) { _roll[i] = Quaternion.identity; _dropT[i] = -1f; }
        SnapVisuals();
    }

    protected override void Update()
    {
        base.Update();
        if (Sim == null) return;
        float dt = Time.deltaTime;
        Sim.Advance(dt);
        DriveBalls(dt);

        // A scratched cue ball is NOT put back by the table any more (2026-09-14, Sam):
        // it is handed to the shooter — PoolShotSession.BeginBallInHand once this delay
        // has passed. No session open → the next player to press F gets it in hand.
        if (_cueRespawnPending && Sim.AllStopped) _respawnTimer += dt;

        // The 8 dropped before anyone had a group: back on the foot spot once the table is still.
        if (_spot8Pending && Sim.AllStopped && _dropT[8] < 0f)
        {
            if (Sim.Respot(8)) { _spot8Pending = false; ShowBall(8); SnapBall(8); }
        }

        // Win / lose banner running: re-rack when it ends and the balls are still.
        if (_gameOverTimer >= 0f)
        {
            _gameOverTimer += dt;
            if (_gameOverTimer >= bannerSeconds && Sim.AllStopped) ReRack();
            return;      // no auto re-rack race below
        }

        // Only the cue ball left (or nothing): rack it again after a beat.
        int objectBalls = Sim.ActiveCount - (Sim.Active[PoolPhysics2D.Cue] ? 1 : 0);
        if (objectBalls == 0 && Sim.AllStopped && !_spot8Pending && !_inHand)
        {
            _rerackTimer += dt;
            if (_rerackTimer >= autoRerackDelay) ReRack();
        }
        else _rerackTimer = 0f;

        if (_inHand) DriveHand();
    }

    // ── Interactable ────────────────────────────────────────────────────────

    // Stays interactable while someone else holds the table so the prompt can SAY so
    // (Interactable only shows a message while CanInteract is true); Interact refuses.
    protected override bool CanInteract() => Sim != null && _session != null && !PoolShotSession.IsActive;

    protected override void Interact()
    {
        if (_session == null || Game == null) return;
        if (!Game.TryClaim(LocalPlayerId)) return;
        _session.Open(this);
    }

    protected override string BuildInteractMessage()
    {
        if (Game != null && Game.HasOccupant && Game.Occupant != LocalPlayerId) return "Someone's shooting";
        return $"Press {PromptGlyphs.Interact} to play pool";
    }

    // ── API for the shot session ────────────────────────────────────────────

    public Vector3 SimToLocal(float x, float y) => new Vector3(x, feltY + ballRadius, y);
    public Vector3 CueBallLocal => SimToLocal(Sim.X[PoolPhysics2D.Cue], Sim.Y[PoolPhysics2D.Cue]);

    public void Strike(Vector2 dir, float speed)
    {
        if (_inHand) return;
        _shotWasBreak = !Game.BreakTaken;
        Game.OnStrike(Game.HasOccupant ? Game.Occupant : LocalPlayerId);
        Sim.Strike(dir.x, dir.y, speed);
        FreshRack = false;
    }

    public void ReRack()
    {
        bool hadOcc = Game.HasOccupant; ulong occ = Game.Occupant;
        Game.Reset();
        if (hadOcc) Game.TryClaim(occ);          // R mid-game: the shooter stays on the table
        Sim.Rack();
        FreshRack = true;
        _cueRespawnPending = false;
        _respawnTimer = 0f;
        _rerackTimer = 0f;
        _spot8Pending = false;
        _gameOverTimer = -1f;
        _shotWasBreak = false;
        if (_inHand) { _inHand = false; BallInHandFromScratch = false; SetCueTint(false); }
        for (int i = 0; i < PoolPhysics2D.BallCount; i++) { _dropT[i] = -1f; _roll[i] = Quaternion.identity; ShowBall(i); }
        SnapVisuals();
    }

    // ── Ball in hand ────────────────────────────────────────────────────────

    public bool CanBeginBallInHand()
    {
        if (_inHand || Sim == null || !Sim.AllStopped || Game == null || Game.GameOver || _spot8Pending) return false;
        if (_dropT[PoolPhysics2D.Cue] >= 0f) return false;                       // still dropping into the pocket
        if (Sim.Active[PoolPhysics2D.Cue]) return true;                           // on the table: a voluntary G
        return _cueRespawnPending && _respawnTimer >= cueRespawnDelay;            // scratched: handed over after a beat
    }

    public bool BeginBallInHand()
    {
        if (!CanBeginBallInHand()) return false;
        BallInHandFromScratch = !Sim.Active[PoolPhysics2D.Cue];
        if (BallInHandFromScratch) { _handFromX = Sim.HeadSpotX; _handFromY = 0f; }
        else { _handFromX = Sim.X[PoolPhysics2D.Cue]; _handFromY = Sim.Y[PoolPhysics2D.Cue]; }
        // Start inside the kitchen even if the ball was elsewhere.
        _handX = Mathf.Min(_handFromX, Sim.KitchenMaxX); _handY = _handFromY;
        ClampHand();
        Sim.LiftCue();
        _inHand = true;
        DriveHand();
        return true;
    }

    /// Fast-forward: run the balls to rest now (LMB while they roll). Same end state as waiting.
    public void SettleNow()
    {
        if (Sim == null) return;
        Sim.SettleNow();
    }

    /// dx/dy in table-local metres (already scaled by dt by the caller).
    public void MoveBallInHand(float dx, float dy)
    {
        if (!_inHand) return;
        _handX += dx; _handY += dy;
        ClampHand();
    }

    public bool TryPlaceBallInHand()
    {
        if (!_inHand) return false;
        if (!Sim.PlaceCue(_handX, _handY)) return false;
        EndHand();
        return true;
    }

    /// Put it back where it was picked up — or, after a scratch, on the head spot.
    public void CancelBallInHand()
    {
        if (!_inHand) return;
        if (!Sim.PlaceCue(_handFromX, _handFromY)) Sim.RespawnCue();     // head spot taken → nudged, like a respawn
        EndHand();
    }

    void EndHand()
    {
        _inHand = false;
        _cueRespawnPending = false;
        _respawnTimer = 0f;
        BallInHandFromScratch = false;
        SetCueTint(false);
        SnapBall(PoolPhysics2D.Cue);
    }

    void ClampHand()
    {
        float r = ballRadius;
        _handX = Mathf.Clamp(_handX, -halfLength + r, Sim.KitchenMaxX);
        _handY = Mathf.Clamp(_handY, -halfWidth + r, halfWidth - r);
    }

    void DriveHand()
    {
        BallInHandBlocked = !Sim.CanPlaceCue(_handX, _handY);
        var t = balls != null && balls.Length > 0 ? balls[PoolPhysics2D.Cue] : null;
        if (t == null) return;
        ShowBall(PoolPhysics2D.Cue);
        t.localPosition = SimToLocal(_handX, _handY) + Vector3.up * (ballRadius * handLiftRadii);
        SetCueTint(BallInHandBlocked);
    }

    void SetCueTint(bool red)
    {
        if (_cueRenderer == null) return;
        _cueRenderer.GetPropertyBlock(_cueTint);
        if (red) _cueTint.SetColor(ColorId, new Color(1f, 0.35f, 0.3f, 1f));
        else _cueTint.Clear();
        _cueRenderer.SetPropertyBlock(_cueTint);
    }

    // ── Visuals ─────────────────────────────────────────────────────────────

    void SnapVisuals()
    {
        for (int i = 0; i < PoolPhysics2D.BallCount; i++) SnapBall(i);
    }

    void SnapBall(int i)
    {
        _prevX[i] = Sim.X[i]; _prevY[i] = Sim.Y[i];
        var t = balls != null && i < balls.Length ? balls[i] : null;
        if (t == null) return;
        t.localPosition = SimToLocal(Sim.X[i], Sim.Y[i]);
        t.localRotation = _roll[i];
    }

    void DriveBalls(float dt)
    {
        for (int i = 0; i < PoolPhysics2D.BallCount; i++)
        {
            var t = balls != null && i < balls.Length ? balls[i] : null;
            if (t == null) continue;

            if (_dropT[i] >= 0f)
            {
                _dropT[i] += dt;
                float k = Mathf.Clamp01(_dropT[i] / Mathf.Max(0.05f, pocketDropSeconds));
                float e = k * k;                                   // accelerates like a fall
                t.localPosition = Vector3.Lerp(_dropFrom[i], _dropTo[i], e);
                t.localScale = Vector3.one * Mathf.Lerp(1f, 0.85f, e);
                if (k >= 1f) { _dropT[i] = -1f; t.gameObject.SetActive(false); t.localScale = Vector3.one; }
                continue;
            }
            if (!Sim.Active[i] || (_inHand && i == PoolPhysics2D.Cue)) continue;

            float dx = Sim.X[i] - _prevX[i], dy = Sim.Y[i] - _prevY[i];
            _prevX[i] = Sim.X[i]; _prevY[i] = Sim.Y[i];
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist > 1e-6f)
            {
                // Roll without slip: spin about the horizontal axis perpendicular to travel.
                Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(dx, 0f, dy) / dist);
                float deg = dist / ballRadius * Mathf.Rad2Deg;
                _roll[i] = Quaternion.AngleAxis(deg, axis) * _roll[i];
            }
            t.localPosition = SimToLocal(Sim.X[i], Sim.Y[i]);
            t.localRotation = _roll[i];
        }
    }

    void OnPocketed(int ball, int pocket)
    {
        var t = balls != null && ball < balls.Length ? balls[ball] : null;
        if (t != null)
        {
            Vector3 from = t.localPosition;
            Vector3 to;
            var anchor = pocketAnchors != null && pocket < pocketAnchors.Length ? pocketAnchors[pocket] : null;
            if (anchor != null) to = anchor.localPosition + Vector3.down * pocketDropDepth;
            else to = new Vector3(Sim.PocketX[pocket], feltY - pocketDropDepth, Sim.PocketY[pocket]);
            _dropFrom[ball] = from; _dropTo[ball] = to; _dropT[ball] = 0f;
        }
        if (ball == PoolPhysics2D.Cue) { _cueRespawnPending = true; _respawnTimer = 0f; return; }
        if (Game == null) return;

        ulong shooter = Game.HasOccupant ? Game.Occupant : LocalPlayerId;
        var v = Game.OnPocketed(shooter, ball, _shotWasBreak, Sim.Active);   // Active[] is already false for this ball
        switch (v)
        {
            case PoolGameState.Verdict.Spot8:
                _spot8Pending = true;
                break;
            case PoolGameState.Verdict.Win:
            case PoolGameState.Verdict.Lose:
                bool win = v == PoolGameState.Verdict.Win;
                var grp = Game.GroupOf(shooter);
                string grpName = grp == PoolGameState.Group.Solids ? "SOLIDS" : "STRIPES";
                int left = PoolGameState.GroupBallsLeft(grp, Sim.Active);
                string reason = win ? $"{grpName} CLEARED · 8 BALL DOWN" : $"8 BALL DOWN · {left} {grpName} LEFT";
                _gameOverTimer = 0f;
                GameResult?.Invoke(win, reason);
                break;
        }
    }

    void ShowBall(int i)
    {
        var t = balls != null && i < balls.Length ? balls[i] : null;
        if (t == null) return;
        t.localScale = Vector3.one;
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
    }
}
