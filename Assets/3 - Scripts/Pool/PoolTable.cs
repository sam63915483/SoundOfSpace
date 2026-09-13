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
    [Tooltip("Pause after the table settles before a sunk cue ball comes back.")]
    public float cueRespawnDelay = 0.6f;
    [Tooltip("Pause after the last object ball drops before the table re-racks itself.")]
    public float autoRerackDelay = 1.5f;

    public PoolPhysics2D Sim { get; private set; }
    /// True from a rack until the first strike — the first shot faces the rack.
    public bool FreshRack { get; private set; } = true;
    /// True while the cue ball is off the table (pocketed) and not yet back.
    public bool CueRespawnPending => _cueRespawnPending;

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

        if (_cueRespawnPending && Sim.AllStopped)
        {
            _respawnTimer += dt;
            if (_respawnTimer >= cueRespawnDelay && Sim.RespawnCue())
            {
                _cueRespawnPending = false;
                ShowBall(PoolPhysics2D.Cue);
                SnapBall(PoolPhysics2D.Cue);
            }
        }

        // Only the cue ball left (or nothing): rack it again after a beat.
        int objectBalls = Sim.ActiveCount - (Sim.Active[PoolPhysics2D.Cue] ? 1 : 0);
        if (objectBalls == 0 && Sim.AllStopped)
        {
            _rerackTimer += dt;
            if (_rerackTimer >= autoRerackDelay) ReRack();
        }
        else _rerackTimer = 0f;
    }

    // ── Interactable ────────────────────────────────────────────────────────

    protected override bool CanInteract() => Sim != null && _session != null && !PoolShotSession.IsActive;

    protected override void Interact()
    {
        if (_session != null) _session.Open(this);
    }

    protected override string BuildInteractMessage() => $"Press {PromptGlyphs.Interact} to play pool";

    // ── API for the shot session ────────────────────────────────────────────

    public Vector3 SimToLocal(float x, float y) => new Vector3(x, feltY + ballRadius, y);
    public Vector3 CueBallLocal => SimToLocal(Sim.X[PoolPhysics2D.Cue], Sim.Y[PoolPhysics2D.Cue]);

    public void Strike(Vector2 dir, float speed)
    {
        Sim.Strike(dir.x, dir.y, speed);
        FreshRack = false;
    }

    public void ReRack()
    {
        Sim.Rack();
        FreshRack = true;
        _cueRespawnPending = false;
        _respawnTimer = 0f;
        _rerackTimer = 0f;
        for (int i = 0; i < PoolPhysics2D.BallCount; i++) { _dropT[i] = -1f; _roll[i] = Quaternion.identity; ShowBall(i); }
        SnapVisuals();
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
            if (!Sim.Active[i]) continue;

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
        if (ball == PoolPhysics2D.Cue) { _cueRespawnPending = true; _respawnTimer = 0f; }
    }

    void ShowBall(int i)
    {
        var t = balls != null && i < balls.Length ? balls[i] : null;
        if (t == null) return;
        t.localScale = Vector3.one;
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
    }
}
