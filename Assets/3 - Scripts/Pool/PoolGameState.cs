using System;
using System.Collections.Generic;

/// <summary>
/// The running game on ONE pool table — who is on it, what each player has sunk,
/// which group (solids/stripes) each player is, and whether the 8 ball has ended
/// it. Plain C#, no Unity: it is compiled and run headless by
/// prototypes/pool/test/verify-pool.py, and it is exactly the blob a future
/// PoolSync will replicate. Player ids are Netcode client ids; solo = 0.
///
/// The table is FREE PLAY on purpose (Sam, 2026-09-14): no turns, no fouls, no
/// "hit your own ball first". The players decide all of that between themselves.
/// The ONLY rule here is the 8 ball: sink it with your group still up = lose,
/// with your group gone = win, with no group decided yet = it gets spotted back.
/// </summary>
public class PoolGameState
{
    public enum Group { None, Solids, Stripes }
    public enum Verdict { None, Win, Lose, Spot8 }

    class Player
    {
        public readonly List<int> Sunk = new List<int>();
        public Group Group = Group.None;
    }

    static readonly int[] Empty = new int[0];

    readonly Dictionary<ulong, Player> _players = new Dictionary<ulong, Player>();

    public bool BreakTaken { get; private set; }
    public bool GameOver { get; private set; }
    public bool HasOccupant { get; private set; }
    public ulong Occupant { get; private set; }

    /// Fires after any change (HUD refresh hook).
    public event Action Changed;

    public static bool IsSolid(int ball) => ball >= 1 && ball <= 7;
    public static bool IsStripe(int ball) => ball >= 9 && ball <= 15;

    /// How many balls of `g` are still up, given the sim's Active[] (index = ball number).
    public static int GroupBallsLeft(Group g, bool[] stillOnTable)
    {
        if (g == Group.None || stillOnTable == null) return 0;
        int n = 0;
        for (int b = 1; b < stillOnTable.Length && b <= 15; b++)
            if (stillOnTable[b] && (g == Group.Solids ? IsSolid(b) : IsStripe(b))) n++;
        return n;
    }

    public IReadOnlyList<int> SunkBy(ulong id) => _players.TryGetValue(id, out var p) ? (IReadOnlyList<int>)p.Sunk : Empty;
    public Group GroupOf(ulong id) => _players.TryGetValue(id, out var p) ? p.Group : Group.None;

    Player Know(ulong id)
    {
        if (!_players.TryGetValue(id, out var p)) { p = new Player(); _players[id] = p; }
        return p;
    }

    // ── occupancy ───────────────────────────────────────────────────────────

    public bool CanClaim(ulong id) => !HasOccupant || Occupant == id;

    public bool TryClaim(ulong id)
    {
        if (!CanClaim(id)) return false;
        Know(id);
        bool changed = !HasOccupant;
        HasOccupant = true; Occupant = id;
        if (changed) Changed?.Invoke();
        return true;
    }

    public void Release(ulong id)
    {
        if (!HasOccupant || Occupant != id) return;
        HasOccupant = false; Occupant = 0;
        Changed?.Invoke();
    }

    // ── play ────────────────────────────────────────────────────────────────

    /// Re-rack: forget everything, including who was on the table.
    public void Reset()
    {
        _players.Clear();
        BreakTaken = false; GameOver = false;
        HasOccupant = false; Occupant = 0;
        Changed?.Invoke();
    }

    /// Call when the cue ball is struck, BEFORE the balls roll.
    public void OnStrike(ulong shooter)
    {
        Know(shooter);
        if (BreakTaken) return;
        BreakTaken = true;
        Changed?.Invoke();
    }

    /// A ball dropped. `stillOnTable` = the sim's Active[] AFTER this pocket.
    /// `duringBreak` = this pocket belongs to the break shot (captured at strike time).
    public Verdict OnPocketed(ulong shooter, int ball, bool duringBreak, bool[] stillOnTable)
    {
        if (ball <= 0 || ball > 15 || GameOver) return Verdict.None;
        var p = Know(shooter);

        if (ball == 8)
        {
            if (p.Group == Group.None) return Verdict.Spot8;      // nobody's yet — back on the spot
            p.Sunk.Add(8);
            GameOver = true;
            Verdict v = GroupBallsLeft(p.Group, stillOnTable) == 0 ? Verdict.Win : Verdict.Lose;
            Changed?.Invoke();
            return v;
        }

        p.Sunk.Add(ball);
        if (!duringBreak && p.Group == Group.None)
        {
            p.Group = IsSolid(ball) ? Group.Solids : Group.Stripes;
            Group other = p.Group == Group.Solids ? Group.Stripes : Group.Solids;
            foreach (var kv in _players)
                if (kv.Key != shooter && kv.Value.Group == Group.None) kv.Value.Group = other;
        }
        Changed?.Invoke();
        return Verdict.None;
    }
}
