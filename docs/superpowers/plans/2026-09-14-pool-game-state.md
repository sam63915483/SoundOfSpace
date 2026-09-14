🟢 ACTIVE — 2026-09-14 — Pool game state / tray / 8-ball / ball in hand — implementation plan

# Pool Game State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the free-play pool table into one that tracks stripes/solids per player, shows each player the balls they sank, ends a game on the 8 ball (win/lose banner → auto re-rack), offers ball-in-hand on G, and lets only one player use the table at a time.

**Architecture:** A new plain-C# `PoolGameState` (zero Unity refs, headless-tested with the existing `verify-pool.py` harness) holds the running game and is owned by `PoolTable`. `PoolTable` feeds it pockets/strikes and handles the verdicts (spot the 8, or start the game-over timer → re-rack). `PoolShotSession` gains two states (`BallInHand`, `GameOver`) and pushes tray/group/result to `PoolShotHUD`, which draws the ball tray, the group label and the win/lose banner in code. Spec: `docs/superpowers/specs/2026-09-14-pool-game-state-design.md`.

**Tech Stack:** Unity 2022.3 C# (Built-in RP), UGUI + TMP for HUD, the in-repo Roslyn harness (`py -3 prototypes/pool/test/verify-pool.py`) for headless tests, `py -3 prototypes/shuttle-computer/test/compile-unity.py` for a whole-project compile check without the Editor.

**Ground rules from CLAUDE.md that apply:** append new serialized fields at the END of a MonoBehaviour; no `FindObjectOfType` in Update; `CompareTag`; never assign `transform.position` on a rigidbody (none here — balls are plain transforms); Sam runs the playtests — never enter Play mode.

---

## File map

| File | Role |
|---|---|
| `Assets/3 - Scripts/Pool/PoolGameState.cs` (NEW + `.meta`) | the running game: per-player sunk lists + group, occupant, break flag, 8-ball verdict, GameOver |
| `Assets/3 - Scripts/Pool/PoolPhysics2D.cs` | + `Respot(ball)`, + cue ball lift/place helpers for ball in hand |
| `Assets/3 - Scripts/Pool/PoolTable.cs` | owns `Game`; occupancy + prompt; strike/pocket → game; spot-8; game-over timer → re-rack; ball-in-hand drive + blocked tint |
| `Assets/3 - Scripts/Pool/PoolShotSession.cs` | claim/release; `BallInHand` + `GameOver` states; hint lines; pushes tray/group/result to HUD |
| `Assets/3 - Scripts/Pool/PoolShotHUD.cs` | tray row of ball icons, SOLIDS/STRIPES label with reveal flash, win/lose banner (mockup A) |
| `prototypes/pool/test/PoolSimTests.cs` | + game-state, respot and ball-in-hand checks |
| `prototypes/pool/test/verify-pool.py` | compiles `PoolGameState.cs` too |
| `docs/CURRENT_STATE_AUDIT.md` | addendum |

Test runner: `py -3 prototypes/pool/test/verify-pool.py` — prints `PASS  N checks` or lists `FAIL` lines, exit code 1 on failure. (It is `py -3` on this machine; `python` is not installed.)

---

### Task 1: `PoolGameState` — the game record (headless, TDD)

**Files:**
- Create: `Assets/3 - Scripts/Pool/PoolGameState.cs`
- Modify: `prototypes/pool/test/verify-pool.py` (SOURCES list)
- Modify: `prototypes/pool/test/PoolSimTests.cs` (append checks before the final summary)

- [ ] **Step 1: Add the file to the harness**

In `prototypes/pool/test/verify-pool.py` change `SOURCES` to:

```python
SOURCES = [
    os.path.join(ROOT, "Assets", "3 - Scripts", "Pool", "PoolPhysics2D.cs"),
    os.path.join(ROOT, "Assets", "3 - Scripts", "Pool", "PoolGameState.cs"),
    os.path.join(HERE, "PoolSimTests.cs"),
]
```

- [ ] **Step 2: Write the failing checks**

In `prototypes/pool/test/PoolSimTests.cs`, insert before the line `Console.WriteLine(_failures == 0 ? ...` :

```csharp
        // ── game state ──────────────────────────────────────────────────────
        // stillOnTable helper: everything active except the listed balls
        bool[] Table(params int[] gone)
        {
            var on = new bool[PoolPhysics2D.BallCount];
            for (int i = 0; i < on.Length; i++) on[i] = true;
            foreach (int g in gone) on[g] = false;
            return on;
        }

        // 9. fresh state
        var g = new PoolGameState();
        Check(!g.HasOccupant && !g.BreakTaken && !g.GameOver, "game: fresh = nobody, no break, not over");
        Check(g.SunkBy(0).Count == 0 && g.GroupOf(0) == PoolGameState.Group.None, "game: fresh = empty tray, no group");

        // 10. cue ball never listed, never decides
        g.TryClaim(0); g.OnStrike(0);
        Check(g.OnPocketed(0, 0, false, Table(0)) == PoolGameState.Verdict.None, "game: cue ball → None");
        Check(g.SunkBy(0).Count == 0 && g.GroupOf(0) == PoolGameState.Group.None, "game: cue ball not listed, no group");

        // 11. break sink is listed but decides nothing
        g = new PoolGameState(); g.TryClaim(0);
        Check(!g.BreakTaken, "game: break not taken before first strike");
        g.OnStrike(0);
        Check(g.BreakTaken, "game: break taken after first strike");
        g.OnPocketed(0, 3, true, Table(3));
        Check(g.SunkBy(0).Count == 1 && g.SunkBy(0)[0] == 3, "game: break sink listed");
        Check(g.GroupOf(0) == PoolGameState.Group.None, "game: break sink decides nothing");

        // 12. first post-break sink assigns; second player gets the opposite
        g = new PoolGameState(); g.TryClaim(0); g.Release(0); g.TryClaim(7); g.Release(7); g.TryClaim(0); g.OnStrike(0);
        g.OnPocketed(0, 3, false, Table(3));
        Check(g.GroupOf(0) == PoolGameState.Group.Solids, "game: post-break solid → shooter Solids");
        Check(g.GroupOf(7) == PoolGameState.Group.Stripes, "game: other known player → Stripes");

        // 13. stripe first
        g = new PoolGameState(); g.TryClaim(0); g.Release(0); g.TryClaim(7); g.OnStrike(7);
        g.OnPocketed(7, 12, false, Table(12));
        Check(g.GroupOf(7) == PoolGameState.Group.Stripes && g.GroupOf(0) == PoolGameState.Group.Solids, "game: post-break stripe → shooter Stripes, other Solids");

        // 14. 8 with no group → spot it, not listed, not over; next sink assigns
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0);
        Check(g.OnPocketed(0, 8, false, Table(8)) == PoolGameState.Verdict.Spot8, "game: 8 with no group → Spot8");
        Check(g.SunkBy(0).Count == 0 && !g.GameOver, "game: spotted 8 not listed, game goes on");
        g.OnPocketed(0, 5, false, Table(5));
        Check(g.GroupOf(0) == PoolGameState.Group.Solids, "game: sink after a spotted 8 still assigns");

        // 15. group never changes once set
        g.OnPocketed(0, 11, false, Table(5, 11));
        Check(g.GroupOf(0) == PoolGameState.Group.Solids && g.SunkBy(0).Count == 2 && g.SunkBy(0)[1] == 11, "game: wrong-group ball listed, group unchanged");

        // 16. claims
        g = new PoolGameState();
        Check(g.TryClaim(1), "claim: A claims an empty table");
        Check(!g.CanClaim(2) && !g.TryClaim(2), "claim: B refused while A holds it");
        Check(g.TryClaim(1), "claim: A re-claims fine");
        g.Release(2);
        Check(g.HasOccupant && g.Occupant == 1, "claim: B's release does nothing");
        g.Release(1);
        Check(!g.HasOccupant && g.TryClaim(2), "claim: after A releases, B claims");

        // 17. reset clears everything
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0); g.OnPocketed(0, 3, false, Table(3));
        g.Reset();
        Check(!g.HasOccupant && !g.BreakTaken && !g.GameOver && g.SunkBy(0).Count == 0 && g.GroupOf(0) == PoolGameState.Group.None, "game: reset clears all");

        // 18. order preserved
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0);
        g.OnPocketed(0, 3, false, Table(3)); g.OnPocketed(0, 11, false, Table(3, 11)); g.OnPocketed(0, 5, false, Table(3, 11, 5));
        Check(g.SunkBy(0).Count == 3 && g.SunkBy(0)[0] == 3 && g.SunkBy(0)[1] == 11 && g.SunkBy(0)[2] == 5, "game: tray order preserved");

        // 19. 8 as Solids with a solid left → Lose; later pockets ignored
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0); g.OnPocketed(0, 3, false, Table(3));
        Check(g.OnPocketed(0, 8, false, Table(3, 8)) == PoolGameState.Verdict.Lose, "game: 8 with solids left → Lose");
        Check(g.GameOver && g.SunkBy(0).Count == 2 && g.SunkBy(0)[1] == 8, "game: lose = over, 8 listed");
        Check(g.OnPocketed(0, 4, false, Table(3, 8, 4)) == PoolGameState.Verdict.None && g.SunkBy(0).Count == 2, "game: pockets after game over ignored");

        // 20. 8 as Solids with all solids gone → Win
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0);
        for (int b = 1; b <= 7; b++) g.OnPocketed(0, b, false, Table(1, 2, 3, 4, 5, 6, 7));
        Check(g.OnPocketed(0, 8, false, Table(1, 2, 3, 4, 5, 6, 7, 8)) == PoolGameState.Verdict.Win, "game: 8 with solids cleared → Win");

        // 21. it is YOUR group that counts
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0); g.OnPocketed(0, 9, false, Table(9));
        Check(g.GroupOf(0) == PoolGameState.Group.Stripes, "game: stripes shooter");
        Check(g.OnPocketed(0, 8, false, Table(9, 1, 2, 3, 4, 5, 6, 7, 8)) == PoolGameState.Verdict.Lose, "game: Stripes sinks 8 with stripes left → Lose even with all solids gone");
        Check(PoolGameState.GroupBallsLeft(PoolGameState.Group.Stripes, Table(9, 1, 2, 3, 4, 5, 6, 7, 8)) == 6, "game: GroupBallsLeft counts 6 stripes");
```

- [ ] **Step 3: Run — expect a compile failure**

Run: `py -3 prototypes/pool/test/verify-pool.py`
Expected: `missing source: ...PoolGameState.cs` (exit 3) — the file doesn't exist yet.

- [ ] **Step 4: Write `PoolGameState.cs`**

```csharp
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
```

Also create `Assets/3 - Scripts/Pool/PoolGameState.cs.meta` — copy `PoolPhysics2D.cs.meta` and replace the `guid:` with a fresh 32-hex value (e.g. from `py -3 -c "import uuid;print(uuid.uuid4().hex)"`).

- [ ] **Step 5: Run the harness — expect PASS**

Run: `py -3 prototypes/pool/test/verify-pool.py`
Expected: last line `PASS  <N> checks` (N ≈ 24 + 30 new).

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Pool/PoolGameState.cs" "Assets/3 - Scripts/Pool/PoolGameState.cs.meta" prototypes/pool/test/PoolSimTests.cs prototypes/pool/test/verify-pool.py
git commit -m "feat(pool): PoolGameState — per-player sunk lists, stripes/solids, occupant, 8-ball verdict (headless-tested)"
```

---

### Task 2: Sim — `Respot(ball)` and ball-in-hand helpers (TDD)

**Files:**
- Modify: `Assets/3 - Scripts/Pool/PoolPhysics2D.cs` (after `RespawnCue`, before `SpotFree`)
- Modify: `prototypes/pool/test/PoolSimTests.cs`

- [ ] **Step 1: Write the failing checks** (append after check 21, before the summary line)

```csharp
        // ── respot + ball in hand ───────────────────────────────────────────
        // 22. respot the 8 on a clear foot spot
        s = new PoolPhysics2D();
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        s.Active[8] = false;
        Check(s.Respot(8), "respot: finds the foot spot");
        Check(s.Active[8] && Math.Abs(s.X[8] - s.FootSpotX) < 1e-6f && Math.Abs(s.Y[8]) < 1e-6f, "respot: 8 sits on the foot spot");

        // 23. respot with the foot spot blocked → nudged toward the foot rail, no overlap
        s = new PoolPhysics2D();
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        s.Active[1] = true; s.X[1] = s.FootSpotX; s.Y[1] = 0f;
        Check(s.Respot(8), "respot: finds a spot when blocked");
        Check(s.X[8] > s.FootSpotX + s.BallRadius * 1.9f || Math.Abs(s.Y[8]) > s.BallRadius * 1.9f, "respot: moved off the blocked spot");
        Check(NoOverlaps(s, 0f, out w), "respot: no overlap (" + w + ")");

        // 24. ball in hand: lift deactivates, blocked spot refused, clear spot placed
        s = new PoolPhysics2D();
        s.LiftCue();
        Check(!s.Active[0], "hand: cue inactive while lifted");
        Check(!s.CanPlaceCue(s.X[1], s.Y[1]), "hand: refuses a spot on top of ball 1");
        float kx = s.KitchenMaxX - 0.1f, ky = 0.1f;
        Check(s.CanPlaceCue(kx, ky), "hand: accepts a clear kitchen spot");
        Check(s.PlaceCue(kx, ky) && s.Active[0] && Math.Abs(s.X[0] - kx) < 1e-6f && Math.Abs(s.Y[0] - ky) < 1e-6f, "hand: placed and active");
        Check(!s.PlaceCue(s.X[1], s.Y[1]), "hand: placing on a ball is refused");
        Check(Math.Abs(s.KitchenMaxX - s.HeadSpotX) < 1e-6f, "hand: kitchen ends at the head string");
```

- [ ] **Step 2: Run — expect compile errors** (`Respot`, `LiftCue`, `CanPlaceCue`, `PlaceCue`, `KitchenMaxX` not defined).

Run: `py -3 prototypes/pool/test/verify-pool.py`
Expected: `COMPILE FAILED:` with `error CS1061`.

- [ ] **Step 3: Implement in `PoolPhysics2D.cs`** — insert directly after the `RespawnCue()` method:

```csharp
    /// Put an object ball back on the foot spot (the 8 after a "nobody's 8 yet"
    /// pocket). Nudged along the long axis toward the foot rail, then sideways,
    /// if something is in the way — the mirror of RespawnCue. False if no room.
    public bool Respot(int ball)
    {
        if (ball <= 0 || ball >= BallCount) return false;
        float d = BallRadius * 2f + 0.001f;
        bool wasActive = Active[ball];
        Active[ball] = false;                      // don't collide with itself in SpotFree
        for (int fwd = 0; fwd < 6; fwd++)
        {
            float x = FootSpotX + fwd * d;
            if (x > HalfLength - BallRadius) break;
            for (int k = 0; k < 15; k++)
            {
                float y = (k == 0) ? 0f : ((k % 2 == 1) ? 1f : -1f) * ((k + 1) / 2) * d;
                if (Math.Abs(y) > HalfWidth - BallRadius) continue;
                if (SpotFree(x, y, d)) { X[ball] = x; Y[ball] = y; VX[ball] = 0f; VY[ball] = 0f; Active[ball] = true; return true; }
            }
        }
        Active[ball] = wasActive;
        return false;
    }

    // ── Ball in hand ────────────────────────────────────────────────────────

    /// The kitchen (where a ball-in-hand cue ball may go) runs from the head rail to the head string.
    public float KitchenMaxX => HeadSpotX;

    /// Take the cue ball off the table (it stops colliding). Pair with PlaceCue.
    public void LiftCue() { Active[Cue] = false; VX[Cue] = 0f; VY[Cue] = 0f; }

    /// True if the cue ball could sit at (x, y) without touching another ball.
    public bool CanPlaceCue(float x, float y) => SpotFree(x, y, BallRadius * 2f + 0.001f);

    /// Put the lifted cue ball down at (x, y). False (and nothing changes) if blocked.
    public bool PlaceCue(float x, float y)
    {
        if (!CanPlaceCue(x, y)) return false;
        X[Cue] = x; Y[Cue] = y; VX[Cue] = 0f; VY[Cue] = 0f; Active[Cue] = true;
        return true;
    }
```

Note `SpotFree` skips ball 0 (it loops from 1) and skips inactive balls, so a lifted cue ball never blocks itself.

- [ ] **Step 4: Run — expect PASS**

Run: `py -3 prototypes/pool/test/verify-pool.py`
Expected: `PASS  <N> checks`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Pool/PoolPhysics2D.cs" prototypes/pool/test/PoolSimTests.cs
git commit -m "feat(pool): sim Respot + ball-in-hand lift/place helpers (headless-tested)"
```

---

### Task 3: `PoolTable` — owns the game, occupancy, verdicts, ball in hand

**Files:**
- Modify: `Assets/3 - Scripts/Pool/PoolTable.cs`

No headless test (MonoBehaviour); verified by the whole-project compile in Task 6 and Sam's playtest.

- [ ] **Step 1: New public surface + fields.** Replace the block from `public PoolPhysics2D Sim { get; private set; }` through `float _rerackTimer;` with:

```csharp
    public PoolPhysics2D Sim { get; private set; }
    /// The running game on this table (who's on it, trays, groups, 8-ball verdict).
    public PoolGameState Game { get; private set; }
    /// True from a rack until the first strike — the first shot faces the rack.
    public bool FreshRack { get; private set; } = true;
    /// True while the cue ball is off the table (pocketed) and not yet back.
    public bool CueRespawnPending => _cueRespawnPending;
    /// True while the cue ball is lifted for ball in hand.
    public bool BallInHand => _inHand;
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
```

- [ ] **Step 2: Append serialized fields at the END of the `[Header("Feel")]` block** (append-only rule — after `autoRerackDelay`):

```csharp
    [Tooltip("How long the YOU WIN / YOU LOSE banner stays before the table re-racks itself.")]
    public float bannerSeconds = 2.5f;
    [Tooltip("Ball-in-hand: how fast the cue ball slides (m/s, table-local).")]
    public float handMoveSpeed = 0.5f;
    [Tooltip("Ball-in-hand: how high the cue ball hovers, in ball radii.")]
    public float handLiftRadii = 0.6f;
```

- [ ] **Step 3: `Awake` — create the game and cache the cue renderer.** After `Sim.BallPocketed += OnPocketed;` add:

```csharp
        Game = new PoolGameState();
        _cueTint = new MaterialPropertyBlock();
        if (balls != null && balls.Length > 0 && balls[PoolPhysics2D.Cue] != null)
            _cueRenderer = balls[PoolPhysics2D.Cue].GetComponent<Renderer>();
```

- [ ] **Step 4: `Update` — spot-8, game-over re-rack, ball-in-hand hover.** Replace the whole `Update()` body after `DriveBalls(dt);` with:

```csharp
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
```

- [ ] **Step 5: Interactable — occupancy.** Replace the three Interactable overrides with:

```csharp
    protected override bool CanInteract() =>
        Sim != null && _session != null && !PoolShotSession.IsActive && Game != null && Game.CanClaim(LocalPlayerId);

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
```

Check `Interactable` for how the prompt behaves when `CanInteract()` is false — if the message is only shown while interactable, the "Someone's shooting" text will not appear; in that case look for a non-interactable message hook (e.g. an `InteractPromptUI.Show` overload) and use the same route the beer-cup/vendor code uses for "busy" prompts. If none exists, leave it: refusing F is the requirement, the text is a nicety.

- [ ] **Step 6: Strike / ReRack / OnPocketed.** Replace `Strike` and `ReRack` with:

```csharp
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
        if (_inHand) { _inHand = false; SetCueTint(false); }
        for (int i = 0; i < PoolPhysics2D.BallCount; i++) { _dropT[i] = -1f; _roll[i] = Quaternion.identity; ShowBall(i); }
        SnapVisuals();
    }
```

And extend `OnPocketed` — after the `if (ball == PoolPhysics2D.Cue) {...}` line add:

```csharp
        if (ball == PoolPhysics2D.Cue || Game == null) return;
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
```

- [ ] **Step 7: Ball in hand API + drive + tint.** Add a new section before `// ── Visuals`:

```csharp
    // ── Ball in hand ────────────────────────────────────────────────────────

    public bool CanBeginBallInHand() =>
        !_inHand && Sim != null && Sim.AllStopped && !_cueRespawnPending && Sim.Active[PoolPhysics2D.Cue]
        && Game != null && !Game.GameOver && !_spot8Pending;

    public bool BeginBallInHand()
    {
        if (!CanBeginBallInHand()) return false;
        _handFromX = Sim.X[PoolPhysics2D.Cue]; _handFromY = Sim.Y[PoolPhysics2D.Cue];
        // Start inside the kitchen even if the ball was elsewhere.
        _handX = Mathf.Min(_handFromX, Sim.KitchenMaxX); _handY = _handFromY;
        ClampHand();
        Sim.LiftCue();
        _inHand = true;
        DriveHand();
        return true;
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
        _inHand = false;
        SetCueTint(false);
        SnapBall(PoolPhysics2D.Cue);
        return true;
    }

    public void CancelBallInHand()
    {
        if (!_inHand) return;
        if (!Sim.PlaceCue(_handFromX, _handFromY)) Sim.RespawnCue();     // it came from a legal spot; belt and braces
        _inHand = false;
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
```

In `DriveBalls`, the cue ball must not be re-positioned by the sim while in hand: change `if (!Sim.Active[i]) continue;` to `if (!Sim.Active[i] || (_inHand && i == PoolPhysics2D.Cue)) continue;`. Also in `SnapBall` nothing changes (it's only called when placed).

- [ ] **Step 8: Update the class doc comment** (top of file) — add one paragraph:

```
/// Game record: `Game` (PoolGameState) — who is on the table, what each player
/// sank, stripes/solids, the 8-ball verdict. Free play except the 8: the table
/// spots it back if nobody has a group yet, otherwise fires GameResult and
/// re-racks itself after the banner. Ball in hand (G): the cue ball is lifted
/// out of the sim and hovers where the shooter slides it, kitchen only.
```

- [ ] **Step 9: Commit**

```bash
git add "Assets/3 - Scripts/Pool/PoolTable.cs"
git commit -m "feat(pool): PoolTable owns the game — occupancy, break/pocket → verdict, spot-8, banner → re-rack, ball in hand"
```

---

### Task 4: `PoolShotHUD` — tray, group label, win/lose banner

**Files:**
- Modify: `Assets/3 - Scripts/Pool/PoolShotHUD.cs`

- [ ] **Step 1: Constants + fields.** After `const float HotFrom = 0.85f;` add:

```csharp
    // tray (bottom centre, where the hotbar sits when the HUD is up)
    const float TrayY = 72f;
    const float IconSize = 34f;
    const float IconGap = 6f;
    const float NumberSpot = 15f;
    const float LabelAbove = 30f;
    // banner (mockup A)
    const float BannerFromTop = 0.30f;
    const float BannerBracketArm = 14f;
    const float BannerBracketThick = 2f;
    const float DrainWidth = 120f;
    static readonly Color Hot = new Color(0.94f, 0.26f, 0.18f, 1f);
    // the builder's ball colours (Editor-only code, so copied here)
    static readonly Color[] BallColors =
    {
        Color.white,
        new Color(0.98f, 0.80f, 0.12f), new Color(0.12f, 0.32f, 0.85f), new Color(0.86f, 0.14f, 0.12f),
        new Color(0.46f, 0.20f, 0.66f), new Color(0.96f, 0.50f, 0.10f), new Color(0.10f, 0.55f, 0.26f),
        new Color(0.56f, 0.12f, 0.16f), new Color(0.06f, 0.06f, 0.06f),
    };
```

And after `float _barShown, _barWant, _charge;`:

```csharp
    // tray
    RectTransform _tray;
    readonly System.Collections.Generic.List<RectTransform> _icons = new System.Collections.Generic.List<RectTransform>();
    readonly System.Collections.Generic.List<int> _trayBalls = new System.Collections.Generic.List<int>();
    TextMeshProUGUI _group;
    Image[] _groupBrackets;
    float _groupFlash;              // >0 while the reveal flash runs (seconds left)
    // banner
    RectTransform _banner;
    CanvasGroup _bannerGroup;
    TextMeshProUGUI _bannerBig, _bannerSub;
    Image _drain;
    Image[] _bannerBrackets;
    float _bannerT = -1f, _bannerSeconds;
```

- [ ] **Step 2: Public API.** After `SetHint`:

```csharp
    public void SetTray(System.Collections.Generic.IReadOnlyList<int> balls)
    {
        if (_tray == null) return;
        bool same = balls.Count == _trayBalls.Count;
        for (int i = 0; same && i < balls.Count; i++) same = balls[i] == _trayBalls[i];
        if (same) return;
        _trayBalls.Clear(); _trayBalls.AddRange(balls);
        RebuildTray();
    }

    public void SetGroup(PoolGameState.Group g, bool animate)
    {
        if (_group == null) return;
        string want = g == PoolGameState.Group.Solids ? "SOLIDS" : g == PoolGameState.Group.Stripes ? "STRIPES" : "";
        if (_group.text == want) return;
        _group.text = want;
        _groupFlash = animate && want.Length > 0 ? 1f : 0f;
        if (_groupBrackets != null) foreach (var b in _groupBrackets) if (b != null) b.enabled = _groupFlash > 0f;
    }

    public void ShowResult(bool win, string reason, float seconds)
    {
        if (_banner == null) return;
        Color c = win ? (Color)HelmetHudPalette.Accent : Hot;
        _bannerBig.text = win ? "YOU WIN" : "YOU LOSE";
        _bannerBig.color = c;
        _bannerSub.text = reason;
        _bannerSub.color = new Color(c.r, c.g, c.b, 0.8f);
        _drain.color = c;
        foreach (var b in _bannerBrackets) if (b != null) b.color = c;
        _bannerSeconds = Mathf.Max(0.3f, seconds);
        _bannerT = 0f;
        _banner.gameObject.SetActive(true);
    }

    public void HideResult()
    {
        _bannerT = -1f;
        if (_banner != null) _banner.gameObject.SetActive(false);
    }
```

- [ ] **Step 3: Per-frame animation.** At the end of `Update()` (after the power-bar code — restructure so the early `return`s don't skip this: move the bar code into a `TickBar(dt)` method and call `TickBar(dt); TickGroup(dt); TickBanner(dt);` from `Update`). Add:

```csharp
    void TickGroup(float dt)
    {
        if (_group == null) return;
        if (_groupFlash > 0f)
        {
            _groupFlash = Mathf.Max(0f, _groupFlash - dt);
            float k = 1f - _groupFlash;                                   // 0 → 1 over one second
            float pop = Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(k / 0.35f));
            _group.rectTransform.localScale = Vector3.one * pop;
            _group.color = Color.Lerp(Color.white, HelmetHudPalette.Accent, Mathf.Clamp01(k));
            if (_groupFlash <= 0f && _groupBrackets != null) foreach (var b in _groupBrackets) if (b != null) b.enabled = false;
        }
    }

    void TickBanner(float dt)
    {
        if (_bannerT < 0f || _banner == null) return;
        _bannerT += dt;
        float inK = Mathf.Clamp01(_bannerT / 0.25f);
        float outK = Mathf.Clamp01((_bannerSeconds - _bannerT) / 0.2f);
        _bannerGroup.alpha = Mathf.Min(inK, outK);
        _banner.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, inK);
        float drain = Mathf.Clamp01(1f - _bannerT / _bannerSeconds);
        _drain.rectTransform.sizeDelta = new Vector2(DrainWidth * drain, 2f);
        if (_bannerT >= _bannerSeconds + 0.05f) HideResult();
    }
```

- [ ] **Step 4: Build the tray, label and banner.** At the end of `Build()` (after the hint), add:

```csharp
        // ── tray: the balls you sank this game ──
        _tray = NewRect("Tray", canvasGo.transform);
        _tray.anchorMin = _tray.anchorMax = new Vector2(0.5f, 0f);
        _tray.pivot = new Vector2(0.5f, 0f);
        _tray.anchoredPosition = new Vector2(0f, TrayY);
        _tray.sizeDelta = new Vector2(IconSize, IconSize);
        var baseline = NewImage("__Baseline", _tray, new Vector2(IconSize + 24f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f));
        baseline.rectTransform.anchorMin = new Vector2(0.5f, 0f); baseline.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        baseline.color = HelmetHudPalette.AccentGlow;
        baseline.name = "Baseline";

        var groupRt = NewRect("Group", canvasGo.transform);
        groupRt.anchorMin = groupRt.anchorMax = new Vector2(0.5f, 0f);
        groupRt.pivot = new Vector2(0.5f, 0f);
        groupRt.anchoredPosition = new Vector2(0f, TrayY + IconSize + LabelAbove - 12f);
        groupRt.sizeDelta = new Vector2(240f, 24f);
        _group = groupRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_group);
        _group.fontSize = 15f;
        _group.characterSpacing = 12f;
        _group.alignment = TextAlignmentOptions.Center;
        _group.color = HelmetHudPalette.Accent;
        _group.text = "";
        _groupBrackets = Brackets(groupRt, 52f, 11f, BracketArm, BracketThick);
        foreach (var b in _groupBrackets) { b.color = HelmetHudPalette.AccentGlow; b.enabled = false; }

        // ── win / lose banner (mockup A: bracketed headline) ──
        _banner = NewRect("Banner", canvasGo.transform);
        _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
        _banner.anchorMin = _banner.anchorMax = new Vector2(0.5f, 1f - BannerFromTop);
        _banner.pivot = new Vector2(0.5f, 0.5f);
        _banner.anchoredPosition = Vector2.zero;
        _banner.sizeDelta = new Vector2(520f, 120f);
        var bigRt = NewRect("Big", _banner);
        bigRt.anchorMin = bigRt.anchorMax = new Vector2(0.5f, 0.5f); bigRt.pivot = new Vector2(0.5f, 0.5f);
        bigRt.anchoredPosition = new Vector2(0f, 14f); bigRt.sizeDelta = new Vector2(520f, 80f);
        _bannerBig = bigRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_bannerBig);
        _bannerBig.fontSize = 64f; _bannerBig.fontStyle = FontStyles.Bold; _bannerBig.characterSpacing = 14f;
        _bannerBig.alignment = TextAlignmentOptions.Center;
        var subRt = NewRect("Sub", _banner);
        subRt.anchorMin = subRt.anchorMax = new Vector2(0.5f, 0.5f); subRt.pivot = new Vector2(0.5f, 0.5f);
        subRt.anchoredPosition = new Vector2(0f, -30f); subRt.sizeDelta = new Vector2(520f, 24f);
        _bannerSub = subRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_bannerSub);
        _bannerSub.fontSize = 16f; _bannerSub.characterSpacing = 6f;
        _bannerSub.alignment = TextAlignmentOptions.Center;
        _drain = NewImage("Drain", _banner, new Vector2(DrainWidth, 2f), new Vector2(0.5f, 0.5f), new Vector2(0f, -56f));
        _bannerBrackets = Brackets(_banner, 260f, 60f, BannerBracketArm, BannerBracketThick);
        _banner.gameObject.SetActive(false);
```

Then add these helpers next to `NewImage`:

```csharp
    /// Four corner brackets (top-left and bottom-right pairs, the power bar's language) around a centred box.
    static Image[] Brackets(RectTransform parent, float halfW, float halfH, float arm, float thick)
    {
        return new[]
        {
            NewImage("BrL0", parent, new Vector2(arm, thick), new Vector2(0f, 0.5f), new Vector2(-halfW, halfH)),
            NewImage("BrL1", parent, new Vector2(thick, arm), new Vector2(0f, 1f), new Vector2(-halfW, halfH)),
            NewImage("BrR0", parent, new Vector2(arm, thick), new Vector2(1f, 0.5f), new Vector2(halfW, -halfH)),
            NewImage("BrR1", parent, new Vector2(thick, arm), new Vector2(1f, 0f), new Vector2(halfW, -halfH)),
        };
    }

    void RebuildTray()
    {
        foreach (var rt in _icons) if (rt != null) Destroy(rt.gameObject);
        _icons.Clear();
        int n = _trayBalls.Count;
        float total = n * IconSize + Mathf.Max(0, n - 1) * IconGap;
        _tray.sizeDelta = new Vector2(Mathf.Max(IconSize, total), IconSize);
        var baseline = _tray.Find("Baseline") as RectTransform;
        if (baseline != null) baseline.sizeDelta = new Vector2(Mathf.Max(IconSize, total) + 24f, 1f);
        float x = -total * 0.5f + IconSize * 0.5f;
        for (int i = 0; i < n; i++, x += IconSize + IconGap)
            _icons.Add(BallIcon(_trayBalls[i], _tray, new Vector2(x, IconSize * 0.5f)));
    }

    /// A code-drawn pool ball: coloured disc (solids), white disc with a coloured band (stripes), black 8, plus a white number spot.
    static RectTransform BallIcon(int ball, RectTransform parent, Vector2 pos)
    {
        bool stripe = ball >= 9;
        Color c = ball == 8 ? BallColors[8] : BallColors[stripe ? ball - 8 : ball];
        var disc = NewImage("Ball_" + ball, parent, new Vector2(IconSize, IconSize), new Vector2(0.5f, 0.5f), pos);
        disc.rectTransform.anchorMin = disc.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        disc.sprite = HALVisuals.Disc();
        disc.color = stripe ? new Color(0.96f, 0.96f, 0.93f) : c;
        if (stripe)
        {
            var band = NewImage("Band", disc.rectTransform, new Vector2(IconSize, IconSize), new Vector2(0.5f, 0.5f), Vector2.zero);
            band.sprite = HALVisuals.Disc();
            band.color = c;
            band.rectTransform.localScale = new Vector3(1f, 0.42f, 1f);
        }
        var spot = NewImage("Spot", disc.rectTransform, new Vector2(NumberSpot, NumberSpot), new Vector2(0.5f, 0.5f), Vector2.zero);
        spot.sprite = HALVisuals.Disc();
        spot.color = Color.white;
        var numRt = NewRect("Num", spot.rectTransform);
        numRt.anchorMin = numRt.anchorMax = new Vector2(0.5f, 0.5f); numRt.pivot = new Vector2(0.5f, 0.5f);
        numRt.sizeDelta = new Vector2(NumberSpot + 4f, NumberSpot);
        var num = numRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(num);
        num.fontSize = 11f; num.fontStyle = FontStyles.Bold;
        num.alignment = TextAlignmentOptions.Center;
        num.color = new Color(0.08f, 0.08f, 0.1f, 1f);
        num.text = ball.ToString();
        num.raycastTarget = false;
        return disc.rectTransform;
    }
```

Note: `NewImage` sets anchors to centre; the tray icons override to bottom-centre to match the tray's own anchoring — the tray's children are positioned relative to the tray rect, so `pos.y = IconSize/2` puts them on its bottom edge. `HALVisuals.Disc()` is a shared 64 px anti-aliased circle sprite (`AI/HALVisuals.cs`).

`SetVisible(false)` on the canvas hides tray + banner too (they're all under `PoolCanvas`). Also in `SetVisible(false)` call `HideResult()` so a banner never survives leaving the table.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Pool/PoolShotHUD.cs"
git commit -m "feat(pool): HUD — sunk-ball tray, SOLIDS/STRIPES reveal, YOU WIN / YOU LOSE banner (code-drawn)"
```

---

### Task 5: `PoolShotSession` — claim/release, ball in hand, game over, hints, HUD push

**Files:**
- Modify: `Assets/3 - Scripts/Pool/PoolShotSession.cs`

- [ ] **Step 1: States + fields.** Change the enum to:

```csharp
    public enum State { Closed, Entering, Aiming, Charging, Striking, Rolling, BallInHand, GameOver, Exiting }
```

After `string _hintKb, _hintPad;` add:

```csharp
    string _hintHandKb, _hintHandPad;
    bool _subscribed;
    State _resumeAfterGameOver = State.Aiming;
```

Append at the END of the `[Header("Cue + power")]` fields nothing — `handMoveSpeed` lives on the table. No new serialized fields here.

- [ ] **Step 2: Open — subscribe + push.** In `Open`, after `if (_hud != null) { _hud.SetVisible(true); _hud.SetCharge(0f, false); }` add:

```csharp
        Subscribe();
        PushGameToHud(false);
        if (_table.Game.GameOver) EnterGameOver();     // walked up during someone's banner
```

Add these methods in a new `// ── game ──` section:

```csharp
    // ── game (tray / group / result) ────────────────────────────────────────

    void Subscribe()
    {
        if (_subscribed || _table == null) return;
        _table.Game.Changed += OnGameChanged;
        _table.GameResult += OnGameResult;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed || _table == null) { _subscribed = false; return; }
        _table.Game.Changed -= OnGameChanged;
        _table.GameResult -= OnGameResult;
        _subscribed = false;
    }

    void OnGameChanged() => PushGameToHud(true);

    void PushGameToHud(bool animate)
    {
        if (_hud == null || _table == null) return;
        ulong me = PoolTable.LocalPlayerId;
        _hud.SetTray(_table.Game.SunkBy(me));
        _hud.SetGroup(_table.Game.GroupOf(me), animate);
    }

    void OnGameResult(bool win, string reason)
    {
        if (_hud != null) _hud.ShowResult(win, reason, _table.bannerSeconds);
        EnterGameOver();
    }

    void EnterGameOver()
    {
        if (_state == State.Closed || _state == State.Exiting || _state == State.GameOver) return;
        if (_state == State.BallInHand) _table.CancelBallInHand();
        _charge = 0f;
        if (_hud != null) _hud.SetCharge(0f, false);
        SetGuideVisible(false);
        _state = State.GameOver;
    }
```

- [ ] **Step 3: Update — new states.** In `Update`'s switch, change the `Aiming`/`Charging` case to add the G key:

```csharp
            case State.Aiming:
            case State.Charging:
                if (menu) break;
                if (LeavePressed()) { Close(); break; }
                if (RerackPressed()) { _table.ReRack(); _viewTarget = _table.CueBallLocal; _charge = 0f; _state = State.Aiming; break; }
                if (_state == State.Aiming && HandPressed() && _table.BeginBallInHand())
                {
                    _state = State.BallInHand;
                    SetGuideVisible(false);
                    if (_hud != null) _hud.SetHint(TutorialGate.LastSource == TutorialGate.InputSource.Controller ? _hintHandPad : _hintHandKb);
                    break;
                }
                TickAim(dt);
                TickCharge(dt);
                break;
```

Add two new cases after `Rolling`:

```csharp
            case State.BallInHand:
                if (menu) break;
                if (LeavePressed()) { _table.CancelBallInHand(); Close(); break; }
                if (HandPressed() || CancelPressed()) { _table.CancelBallInHand(); LeaveHand(); break; }
                if (PlacePressed()) { if (_table.TryPlaceBallInHand()) LeaveHand(); break; }
                TickHand(dt);
                break;

            case State.GameOver:
                if (!menu && LeavePressed()) { Close(); break; }
                if (!menu) TickAim(dt);                    // you can still look around
                if (!_table.Game.GameOver)                 // the table re-racked itself
                {
                    _viewTarget = _table.CueBallLocal;
                    _cueSlide = 0f;
                    _state = State.Aiming;
                }
                break;
```

Add the helpers next to `RerackPressed`:

```csharp
    bool HandPressed() => Input.GetKeyDown(KeyCode.G) || TutorialGate.DPadDirectionPressed(2);
    static bool PlacePressed() => Input.GetKeyDown(KeyCode.Space) || TutorialGate.PadPressed(TutorialGate.PadButton.A);

    void LeaveHand()
    {
        _state = State.Aiming;
        _viewTarget = _table.CueBallLocal;
        if (_hud != null) _hud.SetHint(TutorialGate.LastSource == TutorialGate.InputSource.Controller ? _hintPad : _hintKb);
    }

    // Slide the lifted cue ball in CAMERA-relative table directions: W = away from the camera, D = right.
    void TickHand(float dt)
    {
        float fwd = 0f, right = 0f;
        if (Input.GetKey(KeyCode.W)) fwd += 1f;
        if (Input.GetKey(KeyCode.S)) fwd -= 1f;
        if (Input.GetKey(KeyCode.D)) right += 1f;
        if (Input.GetKey(KeyCode.A)) right -= 1f;
        Vector2 stick = TutorialGate.LeftStickRaw();
        if (Mathf.Abs(stick.x) > 0.01f) right = stick.x;
        if (Mathf.Abs(stick.y) > 0.01f) fwd = stick.y;
        if (fwd == 0f && right == 0f) return;
        Vector2 a = AimDir2D();                               // away from the camera, table-local (x, y)
        Vector2 r = new Vector2(-a.y, a.x);                   // 90° clockwise seen from above (+y is table "z")
        Vector2 move = (a * fwd + r * right);
        if (move.sqrMagnitude > 1f) move.Normalize();
        move *= _table.handMoveSpeed * dt;
        _table.MoveBallInHand(move.x, move.y);
    }
```

Check the sign of `r` in play: with the camera behind the ball looking along `a`, pressing D must move the ball to the camera's right. `AimDir2D` returns `(-sin y, -cos y)` for table (x, z); the camera sits at `+OrbitDir`, i.e. the opposite side. Camera right = `Vector3.Cross(up, forward)` = for forward `(a.x, 0, a.y)` → `(a.y, 0, -a.x)`... so `r = (a.y, -a.x)`. **Use `Vector2 r = new Vector2(a.y, -a.x);`** — verify against `Quaternion.LookRotation` handedness once in the Editor (Sam's playtest: "D moves the ball right"). If it's mirrored, flip the sign in this one line.

- [ ] **Step 4: LateUpdate — view target follows the ball in hand; cue and guide hidden.** In `LateUpdate` change the retarget block to:

```csharp
        if (_state != State.Rolling && _state != State.Striking)
        {
            Vector3 want = _state == State.BallInHand ? _table.BallInHandLocal : _table.CueBallLocal;
            _viewTarget = Vector3.Lerp(_viewTarget, want, 1f - Mathf.Exp(-retargetSharpness * dt));
        }
```

`DriveCue` already hides the cue for any state not in `{Aiming, Charging, Striking}` and `DriveGuide` only shows for `Aiming`/`Charging` — `BallInHand` and `GameOver` get hidden cue + guide for free.

- [ ] **Step 5: Setup — hint lines.** Replace the hint assignment in `Setup()`:

```csharp
        if (_hintKb == null)
        {
            _hintKb = "A D turn   W S tilt   Shift fine   hold LMB power   G ball in hand   R re-rack   F leave";
            _hintPad = "Stick aim   LT fine   hold RT power   D-pad↓ ball in hand   Y re-rack   X leave";
            _hintHandKb = "W A S D move the cue ball   Space place   G cancel   F leave";
            _hintHandPad = "Stick move the cue ball   A place   D-pad↓ cancel   X leave";
        }
```

- [ ] **Step 6: Teardown — release + unsubscribe.** At the top of `Teardown(bool abort)`, before `_state = State.Closed;`:

```csharp
        if (_table != null)
        {
            if (_state == State.BallInHand) _table.CancelBallInHand();
            Unsubscribe();
            _table.Game.Release(PoolTable.LocalPlayerId);
        }
```

And in `Close()`, nothing changes — `Exiting` → `Teardown(false)` does the release.

- [ ] **Step 7: Guard the Striking/Rolling cases against a banner** — `OnGameResult` can fire mid-`Rolling` (the 8 drops while balls roll). `EnterGameOver` switches state from any state, so the `Rolling` case's `AllStopped → Aiming` line can't fire afterwards (the switch is on `_state`). Confirm by reading: `EnterGameOver` is called synchronously from the table's `Update` (order 0) before the session's `Update` (order 210) in the same frame — fine.

- [ ] **Step 8: Update the class doc comment** — add:

```
/// Game layer (2026-09-14): opening claims the table (one shooter at a time —
/// PoolTable.Game), the HUD shows the balls YOU sank + SOLIDS/STRIPES, G lifts
/// the cue ball for ball in hand (kitchen only, Space places), and the 8 ball
/// ends the game with a banner before the table re-racks itself.
```

- [ ] **Step 9: Commit**

```bash
git add "Assets/3 - Scripts/Pool/PoolShotSession.cs"
git commit -m "feat(pool): shot session — claim/release, ball in hand (G/Space), game-over banner state, full controls line"
```

---

### Task 6: Compile check, docs, final commit

**Files:**
- Modify: `docs/CURRENT_STATE_AUDIT.md` (append an addendum)

- [ ] **Step 1: Headless tests one more time**

Run: `py -3 prototypes/pool/test/verify-pool.py`
Expected: `PASS  <N> checks`.

- [ ] **Step 2: Whole-project compile without the Editor**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: no `error CS` lines for Assembly-CSharp. Warning baseline is ZERO — any new warning in the pool files is a real problem; fix it.

- [ ] **Step 3: Audit addendum.** Append to `docs/CURRENT_STATE_AUDIT.md`:

```markdown
## Addendum 2026-09-14 — pool: game state, tray, 8-ball, ball in hand (built, playtest pending)

Spec `docs/superpowers/specs/2026-09-14-pool-game-state-design.md`, plan
`docs/superpowers/plans/2026-09-14-pool-game-state.md`. The table is still FREE
PLAY (no turns/fouls — the players decide); it now only KNOWS things:

- `Pool/PoolGameState.cs` (plain C#, headless-tested by `verify-pool.py`): per
  player (Netcode client id, 0 solo) the ordered balls they sank + their group;
  `BreakTaken`; `HasOccupant/Occupant`; the 8-ball verdict. Stripes/solids =
  first object ball sunk on a shot AFTER the break; other known players get the
  opposite. The 8: no group yet → `Spot8` (table respots it on the foot spot);
  group's balls all gone → `Win`; else `Lose`. `GameOver` freezes the record.
- `PoolTable`: owns `Game`; F claims the table (`CanInteract` refuses while
  another id holds it, prompt "Someone's shooting"); `Strike` captures "this
  shot is the break"; pockets → verdict; win/lose → `GameResult` event +
  `bannerSeconds` (2.5) → `ReRack()` (wipes trays/groups, keeps the occupant);
  ball in hand (`Begin/Move/TryPlace/CancelBallInHand`): the sim's cue ball is
  lifted (`Active=false`), hovers `handLiftRadii` up, clamped to the kitchen
  (`x ≤ HeadSpotX`), tinted red via a property block where it would overlap.
- `PoolShotSession`: states `BallInHand` (G / D-pad↓; WASD/stick camera-relative
  at `handMoveSpeed`; Space/A places; G/B cancels) and `GameOver` (look only,
  F leaves; returns to Aiming when the table re-racks). The controls line
  always lists every key and swaps while in hand. Releases the claim in
  `Teardown`.
- `PoolShotHUD`: tray of code-drawn ball icons bottom-centre (`HALVisuals.Disc`
  + the builder's colours copied), SOLIDS/STRIPES label with a 1 s reveal
  flash, and the YOU WIN / YOU LOSE banner (mockup A: bracketed headline, reason
  line, draining timer; accent for win, hot red for loss).
- Not saved; not networked yet — `PoolSync` (StasisDoorSync-shaped: host owns
  the sim + game, clients send claim/release/strike/hand) is tomorrow's MP pass.
```

- [ ] **Step 4: Commit**

```bash
git add docs/CURRENT_STATE_AUDIT.md
git commit -m "docs: audit addendum 2026-09-14 (pool game state, tray, 8-ball, ball in hand)"
```

- [ ] **Step 5: Hand over to Sam** — do NOT enter Play mode. Playtest checklist:

1. Walk up, F, break. Sink something on the break → it appears in the tray, no label.
2. Sink a ball on the next shot → SOLIDS/STRIPES flashes above the tray, tray grows.
3. F to leave, F to come back → tray + label are still there.
4. G → cue ball lifts, WASD slides it, it can't leave the kitchen, turns red on a ball, Space drops it, aim + shoot. D moves it to the camera's right (if mirrored: flip one sign in `TickHand`).
5. Sink the 8 with your group up → YOU LOSE banner, re-rack after ~2.5 s, tray empty.
6. Clear your group, sink the 8 → YOU WIN.
7. Sink the 8 before any group is decided → it pops back on the foot spot.
8. R mid-game → everything clears, you're still at the table.
9. Bottom hint line lists G; swaps while in hand.
