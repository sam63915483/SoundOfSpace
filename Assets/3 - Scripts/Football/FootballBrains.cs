using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The CPU brains (handoff §4, §7). Each is a small state machine that reads
/// the PlayView and answers "where do I go this tick". Shared steering lives
/// in Steer. All positions are field space.
///
/// Blocking is not simulated as contact: PlayInstance slows any defender who
/// has an offensive body in his way (see PlayInstance.BlockSlowdown), so an
/// OL "engages" a rusher just by standing between him and the QB, and a WR
/// "blocks" by running at the nearest defender. The pocket's lifetime and the
/// freed rusher come from PlayInstance too — DLBrain just checks `free`.
///
/// Moves (juke / spin / hurdle / dive) are ACTIONS: the brain asks, the slot
/// plays the motion, PlayInstance resolves what it did to the tackle.
/// </summary>
public static class Steer
{
    public const float ArriveRadius = 0.6f;

    public static Vector3 To(Vector3 from, Vector3 to, float slowRadius = 1.5f)
    {
        Vector3 d = to - from; d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.05f) return Vector3.zero;
        float k = slowRadius > 0f ? Mathf.Clamp01(dist / slowRadius) : 1f;
        return d / dist * k;
    }

    /// Where to run to meet a moving target (two-iteration intercept).
    public static Vector3 InterceptPoint(Vector3 p, float speed, Vector3 tPos, Vector3 tVel)
    {
        float t = 0f;
        for (int i = 0; i < 3; i++)
        {
            Vector3 fut = tPos + tVel * t;
            t = Mathf.Min(3f, Vector3.Distance(p, fut) / Mathf.Max(speed, 0.1f));
        }
        return tPos + tVel * t;
    }

    public static Vector3 Pursue(FootballPlayer self, FootballPlayer target)
        => To(self.Pos, InterceptPoint(self.Pos, self.MaxSpeed, target.Pos, target.Vel), 0f);

    /// Run at the nearest opponent and stand in his way (stop short so the
    /// two capsules don't overlap).
    public static Vector3 Block(FootballPlayer self, PlayView view)
    {
        var opp = view.NearestOpponent(self);
        if (opp == null) return Vector3.zero;
        return BlockMan(self, opp, view.Carrier);
    }

    /// Get in `opp`'s way: a spot a stride ahead of where he's going, on his
    /// line to our carrier — chasing his back never blocks anyone. Once on
    /// him, stay glued (no prediction, eased approach) so the engagement
    /// holds instead of the blocker sprinting past to the next guess.
    public static Vector3 BlockMan(FootballPlayer self, FootballPlayer opp, FootballPlayer carrier)
    {
        bool onHim = Vector3.Distance(self.Pos, opp.Pos) < 1.6f;
        Vector3 ahead = onHim ? opp.Pos : opp.Pos + opp.Vel * 0.35f;
        Vector3 goal = ahead;
        if (carrier != null && carrier.team == self.team)
            goal = ahead + (carrier.Pos - ahead).normalized * 0.9f;
        return To(self.Pos, goal, onHim ? 1.0f : 0f);
    }

    /// Keep a runner off the sideline.
    public static Vector3 InBounds(Vector3 pos, Vector3 move, float margin = 3f)
    {
        float hw = FootballField.HalfWidth - margin;
        if (pos.x > hw)  move += Vector3.left  * ((pos.x - hw) / margin);
        if (pos.x < -hw) move += Vector3.right * ((-hw - pos.x) / margin);
        return move;
    }

    /// True if `self` can be at `point` by `seconds` (with a little slack).
    public static bool CanReach(FootballPlayer self, Vector3 point, float seconds, float slack = 0.35f)
        => Vector3.Distance(self.Pos, point) / self.MaxSpeed <= seconds + slack;

    /// Run to where the ball comes down so as to ARRIVE with it: full speed
    /// if late, paced if early, standing on the spot once there. Sprinting
    /// flat-out at the spot overran it by two metres and handed the ball to
    /// the man trailing.
    public static Vector3 MeetBall(FootballPlayer self, FootballBall ball)
    {
        float left = ball.catchTime - ball.airTime;
        if (left <= 0f) return To(self.Pos, ball.pos, 0f);
        Vector3 d = ball.catchPoint - self.Pos; d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.15f) return Vector3.zero;
        float need = dist / left;                                   // m/s to be there on time
        float k = Mathf.Clamp(need / self.MaxSpeed, 0.2f, 1f);
        return d / dist * k;
    }
}

/// Pre-snap and between plays: walk to a spot and face a way. PlayInstance
/// retargets it every tick when the spot moves (carrying the ball to the
/// centre, fetching a loose ball).
public class MoveToBrain : IPlayerBrain
{
    public Vector3 target;
    /// Stop this far short (handing the ball to a man, not running him over).
    public float stopShort;
    /// Jog (true) rather than walk — a returner bringing the ball 60 m.
    public bool hurry;
    public MoveToBrain(Vector3 t) { target = t; }
    public bool KeepsControlWhenCarrying => true;
    public bool Arrived(FootballPlayer self) => Vector3.Distance(self.Pos, target) < Steer.ArriveRadius + stopShort;
    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        // A jog for a short walk, quicker when the spot is far (the kickoff
        // lineup after a score is 60 m away).
        float dist = Vector3.Distance(self.Pos, target);
        if (dist < stopShort) { o.move = Vector3.zero; return; }
        float far = Mathf.Clamp01((dist - 15f) / 30f);
        float pace = hurry ? 0.92f : Mathf.Lerp(0.78f, 0.92f, far);
        o.move = Steer.To(self.Pos, target, 2f + stopShort) * pace;
    }
}

/// Whoever has the ball: toward the end zone, away from the nearest pursuer,
/// off the sideline. Optional opening waypoints (a sweep to the edge). Rolls
/// the open-field moves (Sam, 2026-09-19): a juke or spin when a man closes,
/// a hurdle when one dives at his legs.
public class BallCarrierBrain : IPlayerBrain
{
    readonly List<Vector3> _path;
    readonly System.Random _rng;
    readonly float _agility;         // 0..1 from the team's speed stat
    int _i;
    float _moveCooldown;
    float _diveSeenAt = -1f; FootballPlayer _diver;
    float _reactDelay;
    public BallCarrierBrain(List<Vector3> openingPath, System.Random rng, float agility)
    {
        _path = openingPath; _rng = rng; _agility = Mathf.Clamp01(agility);
        _reactDelay = 0.02f + (float)rng.NextDouble() * 0.08f;
    }
    public bool KeepsControlWhenCarrying => true;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        // His OWN goal, not the offense's — an interception runs the other way.
        Vector3 forward = Vector3.forward * self.team.attackDir;
        Vector3 move;
        FootballTeam other = self.team == view.offense ? view.defense : view.offense;
        FootballPlayer nearest = view.NearestStanding(self.Pos, other);
        _moveCooldown -= dt;

        // The opening path (a sweep to the edge) is a suggestion: it is dropped
        // the moment someone is on him, and waypoints count as reached from a
        // stride away so he never has to turn back for one.
        if (_path != null && _i < _path.Count && (nearest == null || Vector3.Distance(nearest.Pos, self.Pos) > 1.8f))
        {
            while (_i < _path.Count && Vector3.Distance(self.Pos, _path[_i]) < 2.5f) _i++;
            move = _i < _path.Count ? Steer.To(self.Pos, _path[_i], 0f) : forward;
        }
        else { _i = _path != null ? _path.Count : 0; move = forward; }

        // Pick a lane: score a fan of headings by progress toward the goal
        // minus every defender sitting in that lane (a blocked one counts for
        // less), and lean the path toward the best. Sidestepping only the
        // nearest man ran straight into the second.
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 bestDir = move; float bestScore = float.MinValue;
        for (int k = -3; k <= 3; k++)
        {
            float ang = k * 22f * Mathf.Deg2Rad;
            Vector3 c = (forward * Mathf.Cos(ang) + right * Mathf.Sin(ang)).normalized;
            float score = Vector3.Dot(c, move.sqrMagnitude > 0.01f ? move.normalized : forward) * 1.2f + Vector3.Dot(c, forward) * 0.6f;
            for (int i = 0; i < view.players.Count; i++)
            {
                var q = view.players[i];
                if (q.team != other || q.IsDown) continue;
                Vector3 rel = q.Pos - self.Pos; rel.y = 0f;
                float along = Vector3.Dot(rel, c);
                if (along < -0.5f || along > 9f) continue;
                float across = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, c)));
                float threat = Mathf.Max(0f, 1f - across / 3.2f) * (1f - along / 10f);
                if (q.speedScale < 0.5f) threat *= 0.8f;            // he's being blocked — still has an arm
                score -= threat * 3.0f;
            }
            // Don't lane-pick into the sideline.
            float xAfter = self.Pos.x + c.x * 6f;
            if (Mathf.Abs(xAfter) > FootballField.HalfWidth - 2f) score -= 1.5f;
            if (score > bestScore) { bestScore = score; bestDir = c; }
        }
        move = bestDir;
        move = Steer.InBounds(self.Pos, move, 4f);
        move.y = 0f;
        o.move = move.sqrMagnitude > 1f ? move.normalized : move;

        // ── the moves ──
        if (nearest == null || self.IsJuking || self.IsSpinning || self.IsHurdling) return;
        Vector3 toD = nearest.Pos - self.Pos; toD.y = 0f;
        float dist = toD.magnitude;
        if (dist < 0.05f) return;
        Vector3 dn = toD / dist;
        float ahead = Vector3.Dot(dn, self.Facing);
        float sideOf = Vector3.Dot(dn, self.Right);
        Vector3 relVel = nearest.Vel - self.Vel;
        float closing = -Vector3.Dot(relVel, dn);

        // A man diving at his legs: hurdle him (or step round the dive).
        if (nearest.IsDiving && dist < 3.0f && Vector3.Dot(nearest.Facing, -dn) > 0.4f)
        {
            if (_diver != nearest) { _diver = nearest; _diveSeenAt = view.timeSinceSnap; }
            if (view.timeSinceSnap - _diveSeenAt < _reactDelay) return;
            if (_rng.NextDouble() < 0.72)
            {
                o.action = BrainAction.Hurdle;
            }
            else
            {
                o.action = BrainAction.Juke;
                o.target = self.Right * (sideOf > 0f ? -1f : 1f);
            }
            _moveCooldown = 1.1f;
            return;
        }
        if (_moveCooldown > 0f || dist < 1.7f || dist > 3.6f || closing < 2f) return;
        float agility = 0.45f + 0.55f * _agility;
        if (ahead > 0.35f)
        {
            // Square in front of him: a hard step to the side he isn't.
            if (_rng.NextDouble() < 0.45 * agility)
            {
                o.action = BrainAction.Juke;
                // Toward the open side — away from the defender, unless that's the sideline.
                float side = sideOf > 0f ? -1f : 1f;
                float xAfter = self.Pos.x + self.Right.x * side * 3f;
                if (Mathf.Abs(xAfter) > FootballField.HalfWidth - 2.5f) side = -side;
                o.target = self.Right * side;
                _moveCooldown = 1.1f;
            }
            else _moveCooldown = 0.35f;             // decided to run through him; look again shortly
        }
        else if (Mathf.Abs(sideOf) > 0.6f || ahead < -0.2f)
        {
            // Coming from the side or behind: spin off him.
            if (_rng.NextDouble() < 0.28 * agility) { o.action = BrainAction.Spin; _moveCooldown = 1.3f; }
            else _moveCooldown = 0.35f;
        }
    }
}

/// Offensive line: stand between your man and the quarterback. Once the ball
/// is out (thrown, handed off, QB gone past the line) just block whoever's near.
public class OLBrain : IPlayerBrain
{
    readonly FootballPlayer _man;
    public OLBrain(FootballPlayer pairedRusher) { _man = pairedRusher; }
    public bool KeepsControlWhenCarrying => false;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var carrier = view.Carrier;
        if (carrier != null && carrier.team != self.team) { o.move = Steer.Pursue(self, carrier); return; }
        // Stay on your man, between him and whoever has the ball (the QB in
        // the pocket, the sweep man, a receiver after the catch).
        if (carrier != null && _man != null && Vector3.Distance(_man.Pos, self.Pos) < 7f)
        {
            // Engaged (he's slowed): hold the lock where it is — running round
            // to the carrier's side as he passes would let go of him.
            Vector3 spot = _man.speedScale < 0.5f
                ? _man.Pos + (self.Pos - _man.Pos).normalized * 0.9f
                : _man.Pos + (carrier.Pos - _man.Pos).normalized * 1.0f;
            o.move = Steer.To(self.Pos, spot, 0.8f);
            return;
        }
        o.move = Steer.Block(self, view);
    }
}

/// The centre: snaps (PlayInstance does the launch), then picks up whoever
/// comes free — the blitzing spy, a rusher who has shed his lineman — and
/// stands between him and the ball.
public class CenterBrain : IPlayerBrain
{
    public bool KeepsControlWhenCarrying => false;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var carrier = view.Carrier;
        if (carrier != null && carrier.team != self.team) { o.move = Steer.Pursue(self, carrier); return; }
        if (view.SnapInFlight) { o.move = Vector3.zero; return; }          // stay set for a beat
        if (carrier == null)
        {
            // Ball in the air / loose: drift toward it.
            o.move = Steer.To(self.Pos, view.ball.state == FootballBall.State.Loose ? view.ball.pos : view.ball.catchPoint, 6f) * 0.5f;
            return;
        }
        // Who is coming that nobody has? Nearest defender to the carrier who
        // isn't engaged, within reach of me.
        FootballPlayer threat = null; float best = float.MaxValue;
        for (int i = 0; i < view.players.Count; i++)
        {
            var d = view.players[i];
            if (d.team == self.team || d.IsDown) continue;
            if (d.speedScale < 0.5f) continue;                                // someone has him
            float toMe = Vector3.Distance(d.Pos, self.Pos);
            if (toMe > 8f) continue;
            float toCarrier = Vector3.Distance(d.Pos, carrier.Pos);
            if (toCarrier < best) { best = toCarrier; threat = d; }
        }
        if (threat != null) { o.move = Steer.BlockMan(self, threat, carrier); return; }
        // Nobody loose: hold a spot a stride in front of the QB, or block downfield.
        if (carrier.role == FootballRole.QB && view.Downfield(carrier.Pos) < 0.5f)
            o.move = Steer.To(self.Pos, carrier.Pos + Vector3.forward * (view.attackDir * 2.2f), 1.2f) * 0.7f;
        else o.move = Steer.Block(self, view);
    }
}

/// Receiver: run the route (throttling into a cut so a break reads as a
/// plant), settle and face the QB on a curl / hitch / comeback, then find
/// grass; work back toward a scrambling QB; go get the ball when it's yours;
/// block once a teammate is carrying; tackle if the other side has it.
public class WRBrain : IPlayerBrain
{
    readonly List<Vector3> _route;
    readonly bool _settleAtEnd;
    int _i;
    float _wander;
    bool _done;
    public WRBrain(List<Vector3> fieldRoute, bool settle) { _route = fieldRoute; _settleAtEnd = settle; }
    public bool KeepsControlWhenCarrying => false;
    /// The waypoints not yet run — a sweep's carrier keeps following them.
    public List<Vector3> Remaining()
    {
        var l = new List<Vector3>();
        if (_route != null) for (int i = _i; i < _route.Count; i++) l.Add(_route[i]);
        return l;
    }

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var ball = view.ball;
        var carrier = view.Carrier;
        self.settled = false;

        if (view.BallAirborne && !ball.isSnap)
        {
            bool mine = ball.intendedReceiver == self;
            if (mine || (ball.intendedReceiver == null && Steer.CanReach(self, ball.catchPoint, ball.catchTime - ball.airTime)))
            {
                o.move = Steer.MeetBall(self, ball);
                return;
            }
        }
        if (view.BallLoose) { o.move = Steer.To(self.Pos, ball.pos, 0f); return; }
        if (carrier != null && carrier.team != self.team) { o.move = Steer.Pursue(self, carrier); return; }
        // A teammate is running with it (a catch, a sweep, the QB past the
        // line): block. On a designed run everyone but the ball-carrier-to-be
        // blocks from the snap. While the QB holds it in the pocket on a pass,
        // run the route.
        bool designedRun = view.play != null && (view.play.kind == FootballPlay.Kind.JetSweep || view.play.kind == FootballPlay.Kind.QbDraw || view.play.kind == FootballPlay.Kind.QbRun)
                           && !(view.play.kind == FootballPlay.Kind.JetSweep && self.roleIndex == 2);
        if (carrier != null && carrier.team == self.team && carrier != self
            && (designedRun || carrier.role != FootballRole.QB || view.Downfield(carrier.Pos) > -0.5f))
        {
            o.move = Steer.Block(self, view);
            return;
        }
        // The snap is still in the air: release off the line anyway.
        var qb = view.FindRole(view.offense, FootballRole.QB);

        // The route.
        if (_route != null && _i < _route.Count)
        {
            Vector3 wp = _route[_i];
            float d = Vector3.Distance(self.Pos, wp);
            bool last = _i == _route.Count - 1;
            if (last && _settleAtEnd)
            {
                // Come to a stop on the spot and turn to the QB.
                if (d < 0.5f || (d < 1.2f && self.Vel.sqrMagnitude < 1f))
                {
                    _done = true; _i = _route.Count;
                }
                else { o.move = Steer.To(self.Pos, wp, 1.6f); return; }
            }
            else
            {
                if (d < 0.9f) _i++;
                if (_i < _route.Count)
                {
                    wp = _route[_i];
                    float pace = 1f;
                    // Plant into a sharp cut: throttle for the last stride and a half.
                    if (_i + 1 < _route.Count)
                    {
                        Vector3 a = wp - (_i > 0 ? _route[_i - 1] : self.Pos), b = _route[_i + 1] - wp; a.y = b.y = 0f;
                        if (a.sqrMagnitude > 0.01f && b.sqrMagnitude > 0.01f)
                        {
                            float ang = Vector3.Angle(a, b);
                            float dd = Vector3.Distance(self.Pos, wp);
                            if (ang > 55f && dd < 1.7f) pace = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01((dd - 0.6f) / 1.1f));
                        }
                    }
                    o.move = Steer.To(self.Pos, wp, 0f) * pace;
                    return;
                }
            }
        }

        // Route done. A scrambling QB: work back toward his side so he has
        // somewhere to go with it (the scramble drill).
        if (view.qbExtending && qb != null && carrier == qb)
        {
            float depth = view.Downfield(self.Pos);
            if (depth < 22f)
            {
                float across = view.qbExtendSide * (7f + 5.5f * self.roleIndex);
                across = Mathf.Clamp(across, -(FootballField.HalfWidth - 4f), FootballField.HalfWidth - 4f);
                Vector3 spot = new Vector3(across, 0f, view.losZ + view.attackDir * (7f + 4.5f * self.roleIndex));
                if (Vector3.Distance(self.Pos, spot) > 1.2f) { o.move = Steer.To(self.Pos, spot, 1.5f) * 0.95f; return; }
                self.settled = true; o.move = Vector3.zero; o.face = qb.Pos - self.Pos;
                return;
            }
            // Deep men keep going: the shot downfield is the point of extending.
            o.move = Steer.InBounds(self.Pos, Vector3.forward * view.attackDir, 3f);
            return;
        }
        if (_settleAtEnd && _done)
        {
            // Settled: stand, hands ready, eyes on the QB. If he holds it a
            // long time, start working back toward him / away from the corner.
            var cov = view.Nearest(self.Pos, view.defense);
            if (cov != null && Vector3.Distance(cov.Pos, self.Pos) < 1.3f && qb != null)
            {
                Vector3 away = self.Pos - cov.Pos; away.y = 0f;
                o.move = (away.normalized * 0.7f + (qb.Pos - self.Pos).normalized * 0.4f) * 0.6f;
                return;
            }
            self.settled = true;
            o.move = Vector3.zero;
            if (qb != null) o.face = qb.Pos - self.Pos;
            return;
        }
        // Improvise: drift away from the nearest defender, keep working
        // downfield, stay in bounds. Slower than a route so it reads as
        // "looking for the ball", not a second sprint.
        var def = view.Nearest(self.Pos, view.defense);
        Vector3 move = Vector3.forward * (view.attackDir * 0.5f);
        if (def != null)
        {
            Vector3 away = self.Pos - def.Pos; away.y = 0f;
            if (away.sqrMagnitude > 0.01f) move += away.normalized * 0.8f;
        }
        _wander += dt;
        move += Vector3.right * (Mathf.Sin(_wander * 1.7f + self.roleIndex) * 0.4f);
        move = Steer.InBounds(self.Pos, move, 3f);
        o.move = Vector3.ClampMagnitude(move, 1f) * 0.65f;
    }
}

/// Pass rusher: at the quarterback, slowed by whoever's in the way, unless
/// PlayInstance has set him free (the pocket collapsed). Then the carrier.
public class DLBrain : IPlayerBrain
{
    public bool free;
    public bool KeepsControlWhenCarrying => false;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var ball = view.ball;
        var carrier = view.Carrier;
        if (view.SnapInFlight)
        {
            var qb = view.FindRole(view.offense, FootballRole.QB);
            if (qb != null) { o.move = Steer.Pursue(self, qb); return; }
        }
        if (view.BallAirborne) { o.move = Steer.To(self.Pos, ball.catchPoint, 0f) * 0.7f; return; }
        if (view.BallLoose) { o.move = Steer.To(self.Pos, ball.pos, 0f); return; }
        if (carrier == null) return;
        if (carrier.team == self.team) { o.move = Steer.Block(self, view); return; }
        if (view.DefenseStillReading && carrier.role != FootballRole.QB)
        {
            var qb = view.FindRole(view.offense, FootballRole.QB);
            if (qb != null) { o.move = Steer.Pursue(self, qb); return; }
        }
        o.move = Steer.Pursue(self, carrier);
    }
}

/// Man coverage: shadow your receiver with a reaction delay and a cushion;
/// break on the ball when you can get there; chase the carrier otherwise.
public class DBBrain : IPlayerBrain
{
    readonly FootballPlayer _man;
    readonly float _reaction;
    readonly float _cushion;
    readonly float _turnAt;
    float _settledSeen = -1f;
    /// Every snap a corner is a little better or worse: the cushion he
    /// gives and how soon he turns to run vary, so some routes are blanketed
    /// and some get a step — that variance is what makes the QB hold or fire.
    public DBBrain(FootballPlayer man, FootballTeam team, System.Random rng)
    {
        _man = man;
        float q = Mathf.Clamp01(team.coverage + ((float)rng.NextDouble() - 0.5f) * 0.6f);
        _reaction = Mathf.Lerp(0.16f, 0.03f, q);
        _cushion = Mathf.Lerp(1.6f, 0.4f, q);
        _turnAt = Mathf.Lerp(0.4f, 2.2f, q);      // how early he turns and runs with him
    }
    public bool KeepsControlWhenCarrying => false;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var ball = view.ball;
        var carrier = view.Carrier;
        if (view.BallAirborne && !ball.isSnap)
        {
            float left = ball.catchTime - ball.airTime;
            bool mine = _man != null && ball.intendedReceiver == _man;
            if (mine || Steer.CanReach(self, ball.catchPoint, left, 0.2f))
            {
                o.move = Steer.MeetBall(self, ball);
                return;
            }
        }
        if (view.BallLoose) { o.move = Steer.To(self.Pos, ball.pos, 0f); return; }
        if (carrier != null && carrier.team == self.team) { o.move = Steer.Block(self, view); return; }
        // The defense doesn't know the call: a quarterback behind his line
        // with the ball is "in the pocket" until he hands off or crosses it.
        bool qbHasIt = (carrier != null && carrier.role == FootballRole.QB) || view.SnapInFlight;
        bool qbInPocket = qbHasIt && (carrier == null || view.Downfield(carrier.Pos) < 0.5f);
        bool reading = view.DefenseStillReading && carrier != null && view.Downfield(carrier.Pos) < 0.5f;
        if (reading && carrier != null && carrier.role != FootballRole.QB) return;          // bit on the fake: a moment flat-footed
        if (carrier != null && !qbInPocket && !reading) { o.move = Steer.Pursue(self, carrier); return; }

        // Man coverage.
        if (_man == null) return;
        Vector3 fwd = Vector3.forward * view.attackDir;
        float wrDepth = view.Downfield(_man.Pos), myDepth = view.Downfield(self.Pos);

        // He has stopped and turned (a curl / comeback / the scramble drill):
        // after a beat, drive on him from the QB's side and close the window.
        if (_man.settled)
        {
            if (_settledSeen < 0f) _settledSeen = view.timeSinceSnap;
            if (view.timeSinceSnap - _settledSeen > _reaction + 0.35f)
            {
                var qb = view.FindRole(view.offense, FootballRole.QB);
                Vector3 toQb = qb != null ? (qb.Pos - _man.Pos) : -fwd; toQb.y = 0f;
                Vector3 spot = _man.Pos + toQb.normalized * 1.1f;
                o.move = Steer.To(self.Pos, spot, 0.7f);
                return;
            }
        }
        else _settledSeen = -1f;

        if (wrDepth < myDepth - _turnAt)
        {
            // He hasn't reached me yet: mirror him across the field and
            // BACKPEDAL to keep ~2 m — so when he goes by I'm already moving
            // his way and don't have to turn around (that turn was a 5 m head
            // start every snap). Never chase him into the backfield.
            float gap = myDepth - wrDepth;
            Vector3 spot = new Vector3(_man.Pos.x * 0.94f, 0f, self.Pos.z);
            if (gap < 2.0f) spot += fwd * (2.0f - gap);
            if (view.Downfield(spot) < 1.0f) spot.z = view.losZ + view.attackDir * 1.0f;
            o.move = Vector3.ClampMagnitude(Steer.To(self.Pos, spot, 1.0f), 0.85f);
            return;
        }
        // He's past me: trail where he WAS a reaction ago, plus a cushion on
        // the goal side and a lean toward the middle of the field.
        Vector3 seen = _man.Pos - _man.Vel * _reaction;
        Vector3 trail = seen + fwd * _cushion;
        trail.x += (0f - seen.x) * 0.06f;
        o.move = Steer.To(self.Pos, trail, 0.8f);
    }
}

/// The safety: the deepest man. Stays over the top of the deepest route on
/// the field, breaks on any ball he can get to, comes up on a run once the
/// defense has read it — and never lets a man behind him.
public class SafetyBrain : IPlayerBrain
{
    readonly float _depthPad;
    public SafetyBrain(FootballTeam team, System.Random rng)
    {
        float q = Mathf.Clamp01(team.coverage + ((float)rng.NextDouble() - 0.5f) * 0.4f);
        _depthPad = Mathf.Lerp(3.5f, 6.5f, q);
    }
    public bool KeepsControlWhenCarrying => false;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var ball = view.ball;
        var carrier = view.Carrier;
        if (view.BallAirborne && !ball.isSnap)
        {
            float left = ball.catchTime - ball.airTime;
            if (Steer.CanReach(self, ball.catchPoint, left, 0.45f)) { o.move = Steer.MeetBall(self, ball); return; }
            o.move = Steer.To(self.Pos, ball.catchPoint, 0f) * 0.85f;
            return;
        }
        if (view.BallLoose) { o.move = Steer.To(self.Pos, ball.pos, 0f); return; }
        if (carrier != null && carrier.team == self.team) { o.move = Steer.Block(self, view); return; }
        bool qbHasIt = (carrier != null && carrier.role == FootballRole.QB) || view.SnapInFlight;
        bool qbInPocket = qbHasIt && (carrier == null || view.Downfield(carrier.Pos) < 0.5f);
        bool reading = view.DefenseStillReading && carrier != null && view.Downfield(carrier.Pos) < 0.5f;
        if (reading && carrier != null && carrier.role != FootballRole.QB) return;
        if (carrier != null && !qbInPocket && !reading)
        {
            // Come up — but don't overrun it: a runner behind the line is
            // still someone else's problem until he crosses it.
            float depth = view.Downfield(carrier.Pos);
            if (depth < 0f && view.Downfield(self.Pos) > 6f)
            {
                Vector3 hold = new Vector3(Mathf.Lerp(self.Pos.x, carrier.Pos.x, 0.5f), 0f, view.losZ + view.attackDir * 6f);
                o.move = Steer.To(self.Pos, hold, 1.5f);
                return;
            }
            o.move = Steer.Pursue(self, carrier);
            return;
        }
        // Zone: over the top of the deepest receiver, shaded to where the
        // receivers are, and never shallower than 10.
        float deepest = 0f; float xSum = 0f; int n = 0;
        for (int i = 0; i < view.players.Count; i++)
        {
            var p = view.players[i];
            if (p.team != view.offense || p.role != FootballRole.WR) continue;
            float d = view.Downfield(p.Pos);
            if (d > deepest) deepest = d;
            if (d > 6f) { xSum += p.Pos.x; n++; }
        }
        float wantDepth = Mathf.Max(10f, deepest + _depthPad);
        float x = n > 0 ? xSum / n * 0.45f : 0f;
        Vector3 spot = new Vector3(x, 0f, view.losZ + view.attackDir * wantDepth);
        o.move = Vector3.ClampMagnitude(Steer.To(self.Pos, spot, 1.2f), 0.92f);
    }
}

/// The spy: mirror the quarterback across the line, cover the short middle,
/// go get him if he holds the ball too long or leaves the pocket.
public class LBBrain : IPlayerBrain
{
    public float rushAfter = 4.0f;
    public bool KeepsControlWhenCarrying => false;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var ball = view.ball;
        var carrier = view.Carrier;
        if (view.BallAirborne && !ball.isSnap)
        {
            float left = ball.catchTime - ball.airTime;
            if (Steer.CanReach(self, ball.catchPoint, left, 0.3f)) { o.move = Steer.MeetBall(self, ball); return; }
            o.move = Steer.To(self.Pos, ball.catchPoint, 0f) * 0.6f;
            return;
        }
        if (view.BallLoose) { o.move = Steer.To(self.Pos, ball.pos, 0f); return; }
        if (carrier == null) return;
        if (carrier.team == self.team) { o.move = Steer.Block(self, view); return; }
        bool qbInPocket = carrier.role == FootballRole.QB && view.Downfield(carrier.Pos) < 0.5f && Mathf.Abs(carrier.Pos.x) < 7f;
        bool reading = view.DefenseStillReading && view.Downfield(carrier.Pos) < 0.5f;
        if (reading && carrier.role != FootballRole.QB) return;          // bit on the fake
        if ((!qbInPocket && !reading) || view.timeSinceSnap > rushAfter) { o.move = Steer.Pursue(self, carrier); return; }
        // Deep enough to be in front of the short and crossing routes.
        Vector3 spot = new Vector3(carrier.Pos.x * 0.5f, 0f, view.losZ + view.attackDir * 7f);
        o.move = Steer.To(self.Pos, spot, 1f);
    }
}

/// The CPU quarterback (handoff §7): catch the snap, drop, scan, throw the
/// open man; run when nobody is; hand off / keep on the run plays.
///
/// Sam (2026-09-18): "the QB throws fast and only 10–15 yards". So every snap
/// he rolls a STYLE — a quick game, a patient read that waits for the deeper
/// man, a deep shot that ignores the checkdowns until late, or a designed
/// ROLLOUT — and when the pocket goes, or his clock is nearly out with nobody
/// open, he leaves the pocket (the scramble drill: away from the rush, eyes
/// downfield, receivers working back to him) before finally tucking it and
/// running. Sam (2026-09-19): "the qb should be rolling out and extending the
/// play and looking for shots downfield or potentially running it".
/// </summary>
public class QBBrain_CPU : IPlayerBrain
{
    public enum Style { Quick, Patient, DeepShot, Rollout }

    readonly FootballTeam _team;
    readonly System.Random _rng;
    readonly List<FootballPlayer> _readOrder;
    Vector3 _dropSpot;
    float _holdMax, _dropTime;
    bool _thrown;
    public Style style;
    bool _rolling; float _rollSide; float _rollSince = -1f;
    bool _scrambleDrill;
    bool _runOnExpiry;
    float _designedSide;
    public const float DropTime = 1.1f;
    public const float ThrowSpeed = 21f;     // m/s along the ground — a 30 m throw is ~1.4 s in the air
    public const float OpenSeparation = 3.9f;   // ≈ 0.5 s of daylight at the catch: a corner one stride behind is NOT open
    public const float DeepYards = 18f;
    public const float RolloutSeconds = 2.0f;
    public const float ScrambleExtension = 1.8f;

    /// The QB has left the pocket with the ball (receivers adjust).
    public bool Extending => _rolling;
    public float ExtendSide => _rollSide;

    public QBBrain_CPU(FootballTeam team, List<FootballPlayer> readOrder, Vector3 dropSpot, System.Random rng, FootballPlay play, float slotSide)
    {
        _team = team; _readOrder = readOrder; _dropSpot = dropSpot; _rng = rng;
        _designedSide = slotSide == 0f ? 1f : Mathf.Sign(slotSide);
        if (play != null && play.kind == FootballPlay.Kind.Rollout) style = Style.Rollout;
        else
        {
            double r = rng.NextDouble();
            style = r < 0.32 ? Style.Quick : r < 0.63 ? Style.Patient : r < 0.85 ? Style.DeepShot : Style.Rollout;
        }
        switch (style)
        {
            case Style.Quick:    _dropTime = DropTime;  _holdMax = 2.8f + (float)rng.NextDouble() * 0.8f; break;
            case Style.Patient:  _dropTime = 1.5f;      _holdMax = 3.6f + (float)rng.NextDouble() * 0.8f; break;
            case Style.DeepShot: _dropTime = 1.7f;      _holdMax = 4.0f + (float)rng.NextDouble() * 0.8f; break;
            default:             _dropTime = 1.4f;      _holdMax = 3.8f + (float)rng.NextDouble() * 0.9f; break;   // Rollout: routes need a beat before he reads on the move
        }
        _runOnExpiry = rng.NextDouble() < 0.65;
    }
    public bool KeepsControlWhenCarrying => false;

    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var carrier = view.Carrier;
        if (carrier != null && carrier.team != self.team) { o.move = Steer.Pursue(self, carrier) * 0.8f; return; }
        if (view.SnapInFlight && view.ball.intendedReceiver == self)
        {
            // The snap is coming: hands out, a step to meet a bad one.
            o.move = Steer.MeetBall(self, view.ball) * 0.6f;
            var c = view.FindRole(self.team, FootballRole.C);
            if (c != null) o.face = c.Pos - self.Pos;
            return;
        }
        if (carrier != self)
        {
            // Ball is out. Jog toward the play so the QB isn't a statue.
            if (carrier != null) o.move = Steer.To(self.Pos, carrier.Pos, 6f) * 0.3f;
            else if (view.BallLoose) o.move = Steer.To(self.Pos, view.ball.pos, 0f);      // a fumbled snap: go get it
            return;
        }
        float t = view.timeSinceSnap;
        var play = view.play;

        if (play.kind == FootballPlay.Kind.JetSweep)
        {
            var wr = view.FindRole(self.team, FootballRole.WR, 2);
            if (wr != null && (Vector3.Distance(wr.Pos, self.Pos) < 2.6f || t > 2.4f))
            { o.action = BrainAction.Handoff; o.targetPlayer = wr; }
            return;
        }
        if (play.kind == FootballPlay.Kind.QbDraw)
        {
            o.move = Steer.To(self.Pos, _dropSpot, 1f);
            if (t > 1.0f) { o.action = BrainAction.Handoff; o.targetPlayer = self; }
            return;
        }
        if (play.kind == FootballPlay.Kind.QbRun)
        {
            // Designed keeper: tuck it straight off the catch and go.
            if (t > 0.3f) { o.action = BrainAction.Handoff; o.targetPlayer = self; o.say = self.team.shortName + " QB keeps it on the designed run"; }
            return;
        }

        // Pass. Drop, then read — not before the routes have had a second to develop.
        var rusher = view.NearestStanding(self.Pos, view.defense);
        float rusherDist = rusher != null ? Vector3.Distance(rusher.Pos, self.Pos) : 99f;
        if (style == Style.Rollout && !_rolling && t > 0.45f)
        {
            // Designed: roll to the slot's side straight off the catch.
            _rolling = true; _rollSince = t; _rollSide = _designedSide * view.attackDir;
            o.say = self.team.shortName + " QB rolls " + (_designedSide > 0f ? "right" : "left") + " by design";
        }
        if (!_rolling && view.pocketCollapsed && !_thrown && t > _dropTime)
        {
            // The pocket's gone: roll out, away from the man coming.
            StartRoll(self, view, rusher, t);
            o.say = self.team.shortName + " QB rolls out under pressure";
        }
        if (_rolling)
        {
            // Sideways and angling up toward the line, sprinting; eyes
            // downfield. Near the numbers, bend it upfield instead of running
            // out of room.
            float room = FootballField.HalfWidth - 6f - Mathf.Abs(self.Pos.x);
            float up = room < 4f ? 0.9f : 0.45f;
            float across = room < 1.5f ? 0f : _rollSide;
            Vector3 dir = new Vector3(across, 0f, up * view.attackDir).normalized;
            o.move = Steer.InBounds(self.Pos, dir, 6f) * 0.95f;
            // At the line with the ball still in his hands: he can't throw from
            // past it, so it's a run now — tuck it (a standing QB at the line
            // was a sack every time).
            if (view.Downfield(self.Pos) > -1.0f)
            {
                o.action = BrainAction.Handoff; o.targetPlayer = self;
                o.say = self.team.shortName + " QB reaches the line and takes off";
                return;
            }
        }
        else o.move = t < _dropTime ? Steer.To(self.Pos, _dropSpot, 1f) : Steer.To(self.Pos, _dropSpot, 1.5f) * 0.4f;
        if (t < _dropTime || _thrown) return;

        // Read. Quick: first open man in the play's order. Patient: the best
        // window, deeper preferred. Deep shot: only the deep men until late.
        bool deepOnly = style == Style.DeepShot && t < _holdMax - 0.8f;
        FootballPlayer best = null; float bestScore = -99f, bestMargin = -99f; Vector3 bestLead = Vector3.zero;
        for (int i = 0; i < _readOrder.Count; i++)
        {
            var wr = _readOrder[i];
            if (wr == null) continue;
            float sep = Openness(self, wr, view, out Vector3 lead);
            float depth = view.Downfield(lead);
            if (deepOnly && depth < DeepYards) continue;
            // A 40 m ball needs a wider window; a quick slant needs less — the
            // ball is out before the corner can undercut it.
            float need = Mathf.Lerp(OpenSeparation * 0.55f, OpenSeparation, Mathf.Clamp01((depth - 6f) / 16f))
                         + Vector3.Distance(self.Pos, lead) * 0.02f;
            // On the run, throwing back across the body is a bad ball.
            if (_rolling && Mathf.Sign(lead.x - self.Pos.x) != Mathf.Sign(_rollSide) && Mathf.Abs(lead.x - self.Pos.x) > 6f) need += 1.8f;
            float margin = sep - need;
            float score = margin + (style == Style.Quick ? 0f : Mathf.Clamp(depth, 0f, 30f) * 0.06f);
            if (style == Style.Quick && margin > 0f) { best = wr; bestMargin = margin; bestLead = lead; break; }   // first open read wins
            if (score > bestScore) { best = wr; bestScore = score; bestMargin = margin; bestLead = lead; }
        }
        float holdMax = _holdMax + (_scrambleDrill ? ScrambleExtension : 0f);
        bool pressure = rusherDist < (_rolling ? 2.6f : 3.0f) && (view.pocketCollapsed || rusherDist < 1.8f);
        bool outOfTime = t > holdMax;
        // On the run, a downfield man in a tight window is still a throw.
        float floor = _rolling ? -1.2f : -0.6f;      // on the run he'll take a 50/50 ball downfield
        if (best != null && (bestMargin > 0f || ((pressure || outOfTime || _rolling) && bestMargin > floor && view.Downfield(bestLead) > 10f)))
        {
            Throw(self, best, bestLead, view, pressure, ref o);
            return;
        }
        // Clock nearly out and nobody open: leave the pocket and buy time
        // (the scramble drill) instead of standing there waiting to be hit.
        if (!_rolling && !_scrambleDrill && t > _holdMax - 0.55f && bestMargin < 0f)
        {
            _scrambleDrill = true;
            StartRoll(self, view, rusher, t);
            o.say = self.team.shortName + " QB (" + style + ") escapes the pocket, buying time";
            return;
        }
        float rollLimit = style == Style.Rollout ? RolloutSeconds * 1.4f : RolloutSeconds;
        if (pressure || (_rolling && t - _rollSince > rollLimit) || (outOfTime && (_runOnExpiry || t > holdMax + 1.2f)))
        {
            o.action = BrainAction.Handoff; o.targetPlayer = self;   // tuck it and run
            o.say = self.team.shortName + " QB tucks it and runs";
        }
    }

    void StartRoll(FootballPlayer self, PlayView view, FootballPlayer rusher, float t)
    {
        _rolling = true; _rollSince = t;
        // Away from the man coming; if he's dead ahead, to the side with more grass.
        float side = rusher != null ? -Mathf.Sign(rusher.Pos.x - self.Pos.x) : 0f;
        if (side == 0f || Mathf.Abs(rusher.Pos.x - self.Pos.x) < 0.8f) side = self.Pos.x >= 0f ? -1f : 1f;
        _rollSide = side;
    }

    /// Yards of daylight the receiver will have when the ball gets there.
    float Openness(FootballPlayer qb, FootballPlayer wr, PlayView view, out Vector3 lead)
    {
        // Lead him by the ball's real flight time (two passes: the lead moves
        // the target, which moves the distance, which moves the flight time).
        float flight = PlayInstance.PassFlightTime(Vector3.Distance(qb.Pos, wr.Pos));
        lead = wr.Pos + wr.Vel * flight;
        flight = PlayInstance.PassFlightTime(Vector3.Distance(qb.Pos, lead));
        lead = wr.Pos + wr.Vel * flight;
        if (Mathf.Abs(lead.x) > FootballField.HalfWidth - 0.5f) return 0f;        // leads him out of bounds
        if (view.Downfield(lead) < 1f) return 0f;                                    // not past the line yet
        // Standing still is not running a route — unless he has settled at
        // the top of a curl / comeback and is looking at me.
        if (wr.Vel.sqrMagnitude < 4f && !wr.settled) return 0f;
        if (wr.settled) lead = wr.Pos;
        // Daylight at the catch: how far each defender is from the spot once
        // he has had the flight time to run at it (after a beat to react).
        // In TIME: the ball arrives at `flight`; a defender arrives at his
        // distance over his speed plus a beat to react. The gap in seconds,
        // as metres of daylight at running pace. A corner a stride behind on
        // a go route is NOT open (he's there a tenth later); a man who has
        // broken away on a cut is.
        float sep = float.MaxValue;
        for (int i = 0; i < view.players.Count; i++)
        {
            var d = view.players[i];
            if (d.team != view.defense || d.IsDown) continue;
            float tDef = 0.15f + Vector3.Distance(d.Pos, lead) / d.MaxSpeed;
            sep = Mathf.Min(sep, (tDef - flight) * 8f);
        }
        // Reward yards: a deep open man beats a short open man.
        return sep + Mathf.Clamp(view.Downfield(lead), 0f, 20f) * 0.05f;
    }

    void Throw(FootballPlayer self, FootballPlayer wr, Vector3 lead, PlayView view, bool pressure, ref BrainOutput o)
    {
        // Throw to the shoulder AWAY from the nearest defender — a ball on the
        // receiver's line is a ball on the trailing corner's line too.
        float dist = Vector3.Distance(self.Pos, lead);
        var nearestDef = view.Nearest(lead, view.defense);
        if (nearestDef != null)
        {
            Vector3 away = lead - (nearestDef.Pos + nearestDef.Vel * 0.5f); away.y = 0f;
            if (away.sqrMagnitude > 0.01f) lead += away.normalized * 1.0f;
        }
        // Error scaled by accuracy, a rusher in the face, and throwing on the run (handoff §7).
        float sigma = (0.5f + 1.8f * (1f - _team.qbAccuracy) + (pressure ? 1.2f : 0f) + (_rolling ? 0.3f : 0f)) * (0.6f + dist / 40f);
        Vector3 err = new Vector3(Gauss() * sigma, 0f, Gauss() * sigma * 0.8f);
        o.action = BrainAction.Throw;
        o.targetPlayer = wr;
        o.target = lead + err;
        o.power = Mathf.Clamp01(dist / 35f);
        var nd = view.Nearest(wr.Pos, view.defense);
        o.say = self.team.shortName + " QB (" + style + (_rolling ? ", on the run" : "") + ") throws after " + view.timeSinceSnap.ToString("0.0") + " s, "
                + Mathf.RoundToInt(FootballField.ToYards(view.Downfield(lead))) + " yds downfield"
                + " [cover " + (nd != null ? Vector3.Distance(nd.Pos, wr.Pos).ToString("0.0") + " m behind " + wr.Label : "-") + ", flight " + PlayInstance.PassFlightTime(dist).ToString("0.00") + "]";
        _thrown = true;
    }

    float Gauss()
    {
        double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
    }
}

/// Kickoff: the kicker boots it, his team runs down under it.
public class KickerBrain : IPlayerBrain
{
    readonly Vector3 _target;
    bool _kicked;
    public KickerBrain(Vector3 target) { _target = target; }
    public bool KeepsControlWhenCarrying => true;    // he holds it until he kicks it
    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        if (!_kicked && view.timeSinceSnap > 0.6f) { o.action = BrainAction.Kick; o.target = _target; _kicked = true; return; }
        if (_kicked) { var c = view.Carrier; if (c != null && c.team != self.team) o.move = Steer.Pursue(self, c) * 0.8f; }
    }
}

/// Kick coverage: down the field, then the returner.
public class CoverageBrain : IPlayerBrain
{
    public bool KeepsControlWhenCarrying => false;
    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var ball = view.ball; var c = view.Carrier;
        if (c != null && c.team != self.team) { o.move = Steer.Pursue(self, c); return; }
        if (c != null && c.team == self.team) { o.move = Steer.Block(self, view); return; }
        if (ball.state == FootballBall.State.Airborne || ball.state == FootballBall.State.Loose)
        {
            Vector3 goal = ball.state == FootballBall.State.Loose ? ball.pos : ball.landPoint;
            o.move = Steer.To(self.Pos, goal, 3f);
            return;
        }
    }
}

/// Kick return: the returner fields it, everyone else finds a man to block.
public class ReturnerBrain : IPlayerBrain
{
    public bool KeepsControlWhenCarrying => false;
    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var ball = view.ball; var c = view.Carrier;
        if (c != null && c.team != self.team) { o.move = Steer.Pursue(self, c); return; }
        if (c != null) { o.move = Steer.Block(self, view); return; }
        if (ball.state == FootballBall.State.Airborne) { o.move = ball.airTime < ball.catchTime ? Steer.MeetBall(self, ball) : Steer.To(self.Pos, ball.pos, 0f); return; }
        if (ball.state == FootballBall.State.Loose) { o.move = Steer.To(self.Pos, ball.pos, 0f); return; }
    }
}

public class ReturnBlockerBrain : IPlayerBrain
{
    public bool KeepsControlWhenCarrying => false;
    public void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput o)
    {
        if (!view.snapped) return;
        var c = view.Carrier;
        if (c != null && c.team != self.team) { o.move = Steer.Pursue(self, c); return; }
        if (c != null) { o.move = Steer.Block(self, view); return; }
        // Ball in the air: drift back toward where it'll land, at a jog.
        o.move = Steer.To(self.Pos, view.ball.landPoint, 8f) * 0.4f;
    }
}
