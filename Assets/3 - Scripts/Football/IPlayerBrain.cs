using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The swappable brain (handoff §4): one call per tick, given a read-only view
/// of the play and its own slot, returning where it wants to go and, at most,
/// one action. Brains never touch the world — the slot applies the movement,
/// PlayInstance carries out the action. A human QB in Phase 2 is just another
/// implementation; nothing else in the sim knows the difference.
/// </summary>
public interface IPlayerBrain
{
    void Tick(FootballPlayer self, PlayView view, float dt, ref BrainOutput output);

    /// True for brains that drive the ball themselves once they have it (a
    /// human QB scrambling). CPU brains return false and PlayInstance swaps
    /// in BallCarrierBrain the moment the slot takes possession.
    bool KeepsControlWhenCarrying { get; }
}

/// Seven a side, both ways: QB/LB, C/S, two OL/DL, three WR/DB.
public enum FootballRole { QB, WR, OL, DL, DB, LB, C, S }

public enum BrainAction { None, Throw, Handoff, Kick, Juke, Spin, Hurdle, Dive }

public struct BrainOutput
{
    /// Field-space direction to move (magnitude ≤ 1 = fraction of top speed).
    public Vector3 move;
    public BrainAction action;
    /// Throw / kick: where the ball should come down (field space). Handoff:
    /// unused. Juke: the side to step (field space). Dive: the lunge direction.
    public Vector3 target;
    /// Throw / handoff: intended receiver (may be null for a kick).
    public FootballPlayer targetPlayer;
    /// Throw: 0 = lob, 1 = bullet (flight speed pick).
    public float power;
    /// Face this way when standing still (a settled receiver looks at the QB).
    public Vector3 face;
    /// A point (field space) the head turns to: the ball, his man, his read.
    public Vector3 look;
    /// A line for the play-by-play (optional).
    public string say;
}

/// <summary>
/// What a brain is allowed to see. Everything is field space (FieldRoot local,
/// handoff §10): +Z toward the away end zone, up = +Y, metres. Built once per
/// play and updated by PlayInstance; brains hold no world positions across
/// frames — they re-read this every tick.
/// </summary>
public class PlayView
{
    public FootballBall ball;
    public IReadOnlyList<FootballPlayer> players;
    public FootballTeam offense, defense;
    /// +1 the offense drives toward +Z, −1 toward −Z.
    public int attackDir;
    /// Line of scrimmage, field z. Kickoffs: where the ball is kicked from.
    public float losZ;
    /// Seconds since the snap (negative before it).
    public float timeSinceSnap;
    public bool snapped;
    /// The pass rush has beaten the pocket (one rusher is loose).
    public bool pocketCollapsed;
    public bool isKickoff;
    public FootballPlay play;
    /// timeSinceSnap when the ball last changed hands by a handoff (or the QB
    /// tucked it). The defense takes ReadDelay to notice — that head start is
    /// what makes a sweep or a draw a play at all.
    public float handoffTime = -10f;
    /// 0.4 s to see a scramble; 0.8 s on a designed run (the fake sells).
    public float readDelay = 0.45f;
    public bool DefenseStillReading => timeSinceSnap - handoffTime < readDelay;
    /// The QB has left the pocket and is extending the play (rolling or
    /// scrambling with the ball still in his hands) — receivers adjust.
    public bool qbExtending;
    /// Which side of the field (attack-relative sign of x) the QB is working toward.
    public float qbExtendSide;

    /// Whoever holds the ball right now, or null while it is airborne / loose.
    public FootballPlayer Carrier => ball != null ? ball.holder : null;
    public bool BallAirborne => ball != null && ball.state == FootballBall.State.Airborne;
    public bool BallLoose => ball != null && ball.state == FootballBall.State.Loose;
    /// The snap is in the air (a short flight from the centre to the QB).
    public bool SnapInFlight => BallAirborne && ball.isSnap;

    /// Distance downfield (toward the offense's goal) from the LOS: + is past it.
    public float Downfield(Vector3 fieldPos) => (fieldPos.z - losZ) * attackDir;

    public FootballPlayer Nearest(Vector3 p, FootballTeam team, FootballPlayer except = null)
    {
        FootballPlayer best = null; float bd = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            var q = players[i];
            if (q.team != team || q == except) continue;
            float d = (q.Pos - p).sqrMagnitude;
            if (d < bd) { bd = d; best = q; }
        }
        return best;
    }

    /// Nearest man of `team` who is on his feet (a diver or a tackled man
    /// isn't a threat this instant).
    public FootballPlayer NearestStanding(Vector3 p, FootballTeam team, FootballPlayer except = null)
    {
        FootballPlayer best = null; float bd = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            var q = players[i];
            if (q.team != team || q == except || q.IsDown) continue;
            float d = (q.Pos - p).sqrMagnitude;
            if (d < bd) { bd = d; best = q; }
        }
        return best;
    }

    public FootballPlayer NearestOpponent(FootballPlayer self) => Nearest(self.Pos, self.team == offense ? defense : offense);

    public FootballPlayer FindRole(FootballTeam team, FootballRole role, int slotIndex = 0)
    {
        for (int i = 0; i < players.Count; i++)
        {
            var q = players[i];
            if (q.team == team && q.role == role && q.roleIndex == slotIndex) return q;
        }
        return null;
    }
}
