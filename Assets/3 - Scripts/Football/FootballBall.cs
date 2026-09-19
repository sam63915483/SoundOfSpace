using UnityEngine;

/// <summary>
/// The ball, simulated in FIELD space (handoff §10): position/velocity are
/// FieldRoot-local, gravity is straight down that frame's Y, and the transform
/// is a child of FieldRoot so rendering is just localPosition. A launch is an
/// exact parabola — so the catch point is known the instant the ball leaves
/// the hand (brains break on it), and a Phase 2 arc preview is the same maths.
///
/// (The handoff asked for a PhysX rigidbody. On Cyclops that would need
/// floating-origin registration and the planet's gravity direction plumbed in;
/// the field-space parabola needs neither and gives the same arc.)
///
/// Two kinds of ball-on-the-ground: a DROPPED pass is dead where it lands; a
/// LIVE ball (a fumble, a wild snap, a kick) bounces and rolls and whoever
/// gets to it has it.
/// </summary>
public class FootballBall : MonoBehaviour
{
    public enum State { Held, Airborne, Loose, Dead }

    public const float Gravity      = 9.81f;
    public const float CatchHeight  = 1.2f;    // chest — where a receiver meets it
    public const float CatchRadius  = 1.1f;    // 3D distance from chest to ball that counts as a touch
    public const float HoldHeight   = 1.0f;
    public const float GroundRadius = 0.11f;   // resting on its side

    public State state = State.Dead;
    public Vector3 pos;              // field space
    public Vector3 vel;
    public FootballPlayer holder;
    public FootballPlayer thrower;
    public FootballPlayer intendedReceiver;
    public bool isKick;
    /// The centre's snap to the QB: a short flat flight nobody else can touch.
    public bool isSnap;
    /// Somebody got a hand on it and it came loose — it is falling, nobody can
    /// catch it, and where it lands the play is dead.
    public bool dropped;
    /// A fumble / wild snap: when it hits the ground it stays LIVE (bounces,
    /// rolls) instead of dying — whoever falls on it has it.
    public bool liveOnGround;
    /// A fumble is in progress (set by Fumble, cleared when someone holds it).
    public bool fumbled;
    int _bounces;

    /// Where and when (sim seconds from launch) the ball descends through
    /// CatchHeight — the point receivers and defenders run to.
    public Vector3 catchPoint;
    public float catchTime;
    /// Where it would hit the ground if nobody touches it.
    public Vector3 landPoint;
    public float landTime;
    public float airTime;            // seconds since launch

    Transform _visual;
    float _spin;
    System.Random _rng = new System.Random(7);

    public void Init(Transform fieldRoot)
    {
        transform.SetParent(fieldRoot, false);
        if (_visual == null)
        {
            var v = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = v.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            v.name = "BallVisual";
            v.transform.SetParent(transform, false);
            v.transform.localScale = new Vector3(0.2f, 0.2f, 0.34f);
            var m = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.24f, 0.12f) };
            m.SetFloat("_Glossiness", 0.35f);
            v.GetComponent<Renderer>().sharedMaterial = m;
            _visual = v.transform;
        }
    }

    public void Hold(FootballPlayer p)
    {
        holder = p; state = State.Held; thrower = null; intendedReceiver = null; isKick = false; isSnap = false;
        dropped = false; liveOnGround = false; fumbled = false; _bounces = 0;
        vel = Vector3.zero;
        if (p != null) pos = p.BallHoldPoint();
    }

    /// Set the ball down on the grass, dead (the centre spotting it at the line).
    public void Place(Vector3 fieldPos)
    {
        holder = null; state = State.Dead; vel = Vector3.zero; pos = fieldPos; pos.y = GroundRadius;
        dropped = false; liveOnGround = false; fumbled = false; isSnap = false; isKick = false;
    }

    /// Knocked loose at `by`: tumbles off his hands and falls. Dead where it lands.
    public void Drop(Vector3 sideways)
    {
        dropped = true; liveOnGround = false;
        intendedReceiver = null;
        vel = new Vector3(sideways.x, 1.2f, sideways.z);
        state = State.Airborne;
    }

    /// Knocked out of a carrier's arms: pops up and forward, live on the ground.
    public void Fumble(Vector3 carrierVel, Vector3 knock)
    {
        holder = null; thrower = null; intendedReceiver = null;
        isKick = false; isSnap = false; dropped = false;
        liveOnGround = true; fumbled = true; _bounces = 0;
        state = State.Airborne; airTime = 0f;
        vel = carrierVel * 0.5f + knock + Vector3.up * 2.2f;
        catchPoint = pos; catchTime = 0f;
        // Land point (for the men chasing it).
        float disc = vel.y * vel.y + 2f * Gravity * pos.y;
        landTime = (vel.y + Mathf.Sqrt(Mathf.Max(0f, disc))) / Gravity;
        landPoint = pos + new Vector3(vel.x, 0f, vel.z) * landTime; landPoint.y = 0f;
    }

    /// Launch from the holder's hands to come down at `target` (field space,
    /// y ignored) after `flightTime` seconds. Sets catch/land points.
    public void Launch(Vector3 target, float flightTime, FootballPlayer receiver, bool kick)
    {
        thrower = holder; holder = null; intendedReceiver = receiver; isKick = kick; isSnap = false;
        dropped = false; liveOnGround = kick; fumbled = false; _bounces = 0;
        state = State.Airborne; airTime = 0f;
        Vector3 start = pos;
        Vector3 end = new Vector3(target.x, CatchHeight, target.z);
        flightTime = Mathf.Max(0.3f, flightTime);
        // Exact parabola through (start, end) in flightTime.
        vel = (end - start) / flightTime + Vector3.up * (0.5f * Gravity * flightTime);
        catchPoint = end; catchTime = flightTime;
        SolveLanding(start);
    }

    /// The snap: from the ground at the centre's feet to the QB's hands, flat
    /// and quick. The QB is the intended receiver; the flight is live on the
    /// ground if he misses it (a fumbled snap).
    public void Snap(Vector3 handsTarget, float flightTime, FootballPlayer qb, FootballPlayer centre)
    {
        thrower = centre; holder = null; intendedReceiver = qb; isKick = false; isSnap = true;
        dropped = false; liveOnGround = true; fumbled = false; _bounces = 0;
        state = State.Airborne; airTime = 0f;
        Vector3 start = pos;
        Vector3 end = handsTarget;
        flightTime = Mathf.Max(0.25f, flightTime);
        vel = (end - start) / flightTime + Vector3.up * (0.5f * Gravity * flightTime);
        catchPoint = end; catchTime = flightTime;
        SolveLanding(start);
    }

    void SolveLanding(Vector3 start)
    {
        float vy = vel.y, y0 = start.y;
        float disc = vy * vy + 2f * Gravity * y0;
        landTime = (vy + Mathf.Sqrt(Mathf.Max(0f, disc))) / Gravity;
        landPoint = start + new Vector3(vel.x, 0f, vel.z) * landTime;
        landPoint.y = 0f;
    }

    public void Tick(float dt)
    {
        switch (state)
        {
            case State.Held:
                // Ease into the hand (a change of hold style used to teleport it).
                if (holder != null) pos = Vector3.Lerp(pos, holder.BallHoldPoint(), 1f - Mathf.Exp(-22f * dt));
                break;
            case State.Airborne:
                airTime += dt;
                vel += Vector3.down * (Gravity * dt);
                pos += vel * dt;
                if (pos.y <= GroundRadius)
                {
                    pos.y = GroundRadius;
                    if (liveOnGround && !dropped)
                    {
                        // A live ball: an oblong ball bounces unpredictably —
                        // a couple of hops, then it rolls out.
                        _bounces++;
                        if (_bounces <= 2 && Mathf.Abs(vel.y) > 1.5f)
                        {
                            float ang = (float)(_rng.NextDouble() - 0.5) * 1.6f;
                            Vector3 flat = Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f) * new Vector3(vel.x, 0f, vel.z) * 0.3f;
                            vel = new Vector3(flat.x, Mathf.Min(Mathf.Abs(vel.y) * 0.35f, 4f), flat.z);
                        }
                        else
                        {
                            state = State.Loose;
                            vel = new Vector3(vel.x * 0.4f, 0f, vel.z * 0.4f);
                        }
                    }
                    else state = State.Dead;
                }
                break;
            case State.Loose:
                pos += vel * dt;
                pos.y = GroundRadius;
                vel = Vector3.MoveTowards(vel, Vector3.zero, 4f * dt);
                break;
        }
        transform.localPosition = pos;
        if (state == State.Airborne && !isKick && !fumbled) _spin += 900f * dt;           // the spiral
        else if (state != State.Held) _spin += vel.magnitude * 300f * dt;                 // tumbling
        if (state == State.Held && holder != null)
        {
            // Sits along the forearm / across the chest the way the holder carries it.
            transform.localRotation = holder.BallHoldRotation();
        }
        else if (vel.sqrMagnitude > 0.01f) transform.localRotation = Quaternion.LookRotation(vel.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, _spin);
    }

    /// Distance from a chest point to the segment the ball covered this tick —
    /// so a fast ball can't tunnel through a receiver between frames.
    public float TouchDistance(Vector3 chest, Vector3 prevPos)
    {
        Vector3 ab = pos - prevPos;
        float len2 = ab.sqrMagnitude;
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(chest - prevPos, ab) / len2);
        return Vector3.Distance(chest, prevPos + ab * t);
    }
}
