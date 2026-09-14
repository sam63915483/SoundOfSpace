using System;

/// <summary>
/// The pool-table ball simulation: 16 discs on a rectangle, in the TABLE'S OWN
/// flat 2D space (metres; x runs along the long side, y along the short side,
/// origin at the centre of the felt).
///
/// Deliberately NOT Unity physics and deliberately zero UnityEngine references:
///  - Humble Abode is swept along its orbital rail at ~85 m/s by MovePosition.
///    Loose rigidbodies resting on anything there survive only through contact
///    friction and jitter against the render pose (see BarCounter / Bobber, both
///    rewritten to stop being physics objects). A pool ball needs millimetres.
///  - Everything here is positioned as a child of the table, so orbit + floating
///    origin are invisible; the only thing PoolTable does is copy X/Y to
///    localPosition.
///  - Fixed internal step + fixed order = deterministic: a shot is one direction
///    and one speed, which is all a co-op peer would ever need.
///  - No UnityEngine → runs headlessly in prototypes/pool/test/verify-pool.py.
///
/// Model: rolling without slip (one velocity per ball, no spin/english), a
/// constant rolling deceleration plus a small linear drag, equal-mass elastic
/// ball collisions with restitution, cushions as the four rectangle edges with
/// jaw gaps beside each pocket, pockets as capture circles.
/// </summary>
public sealed class PoolPhysics2D
{
    public const int BallCount = 16;
    public const int Cue = 0;
    public const float StepDt = 1f / 240f;

    // ── Table geometry (metres; a 7-ft bar table's 1.98 × 0.99 playing surface) ──
    public float HalfLength = 0.99f;
    public float HalfWidth = 0.495f;
    public float BallRadius = 0.028575f;
    /// Corner pocket centres sit this far outside the corner, diagonally.
    public float CornerPocketInset = 0.012f;
    public float CornerPocketRadius = 0.070f;
    /// Side pocket centres sit this far outside the long cushion.
    public float SidePocketInset = 0.035f;
    public float SidePocketRadius = 0.060f;
    /// Half-width of the gap in a cushion beside each pocket (measured along the cushion).
    public float CornerJawHalfWidth = 0.075f;
    public float SideJawHalfWidth = 0.070f;

    // ── Motion ──────────────────────────────────────────────────────────────
    public float RollingDecel = 0.16f;      // m/s² against the direction of travel (felt ≈ 0.1–0.2)
    public float LinearDrag = 0.05f;        // 1/s
    public float StopSpeed = 0.012f;        // below this a ball is at rest
    public float BallRestitution = 0.96f;
    public float CushionRestitution = 0.78f;
    public float MaxStrikeSpeed = 9f;
    public float MinStrikeSpeed = 0.6f;

    // ── State ───────────────────────────────────────────────────────────────
    public readonly float[] X = new float[BallCount];
    public readonly float[] Y = new float[BallCount];
    public readonly float[] VX = new float[BallCount];
    public readonly float[] VY = new float[BallCount];
    public readonly bool[] Active = new bool[BallCount];

    /// Pocket centres: 0..3 corners (−x−y, +x−y, −x+y, +x+y), 4..5 sides (−y, +y).
    public readonly float[] PocketX = new float[6];
    public readonly float[] PocketY = new float[6];
    public readonly float[] PocketR = new float[6];

    public float HeadSpotX => -HalfLength * 0.5f;
    public float FootSpotX => HalfLength * 0.5f;

    public event Action<int, int> BallPocketed;             // ball, pocket
    public event Action<int, int, float> BallsCollided;     // a, b, closing speed
    public event Action<int, float> CushionHit;             // ball, normal speed

    float _accum;

    public PoolPhysics2D() { Configure(); Rack(); }

    /// Recompute the pocket table after changing the geometry fields.
    public void Configure()
    {
        float cx = HalfLength + CornerPocketInset, cy = HalfWidth + CornerPocketInset;
        SetPocket(0, -cx, -cy, CornerPocketRadius);
        SetPocket(1, cx, -cy, CornerPocketRadius);
        SetPocket(2, -cx, cy, CornerPocketRadius);
        SetPocket(3, cx, cy, CornerPocketRadius);
        SetPocket(4, 0f, -(HalfWidth + SidePocketInset), SidePocketRadius);
        SetPocket(5, 0f, HalfWidth + SidePocketInset, SidePocketRadius);
    }

    void SetPocket(int i, float x, float y, float r) { PocketX[i] = x; PocketY[i] = y; PocketR[i] = r; }

    // ── Rack ────────────────────────────────────────────────────────────────

    // Standard 8-ball triangle, apex at the foot spot pointing at the cue:
    // 8 in the middle of the third row, one solid and one stripe on the back corners.
    static readonly int[][] RackRows =
    {
        new[] { 1 },
        new[] { 9, 2 },
        new[] { 3, 8, 10 },
        new[] { 11, 7, 14, 4 },
        new[] { 5, 13, 15, 6, 12 },
    };

    public void Rack()
    {
        _accum = 0f;
        for (int i = 0; i < BallCount; i++) { Active[i] = true; VX[i] = 0f; VY[i] = 0f; }
        float d = BallRadius * 2f + 0.0004f;          // hair of daylight: no overlap on frame 0
        float rowDx = d * 0.8660254f;                  // cos 30°
        for (int row = 0; row < RackRows.Length; row++)
        {
            int[] ids = RackRows[row];
            float x = FootSpotX + row * rowDx;
            for (int j = 0; j < ids.Length; j++)
            {
                float y = (j - (ids.Length - 1) * 0.5f) * d;
                X[ids[j]] = x; Y[ids[j]] = y;
            }
        }
        X[Cue] = HeadSpotX; Y[Cue] = 0f;
    }

    // ── Shots ───────────────────────────────────────────────────────────────

    /// Send the cue ball off along (dirX, dirY) at `speed` m/s (clamped to the strike range).
    public void Strike(float dirX, float dirY, float speed)
    {
        if (!Active[Cue]) return;
        float len = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 1e-6f) return;
        speed = Clamp(speed, MinStrikeSpeed, MaxStrikeSpeed);
        VX[Cue] = dirX / len * speed;
        VY[Cue] = dirY / len * speed;
    }

    public bool AllStopped
    {
        get
        {
            for (int i = 0; i < BallCount; i++)
                if (Active[i] && (VX[i] != 0f || VY[i] != 0f)) return false;
            return true;
        }
    }

    public int ActiveCount
    {
        get { int n = 0; for (int i = 0; i < BallCount; i++) if (Active[i]) n++; return n; }
    }

    /// Put the cue ball back on the head spot (nudged along the head string, then
    /// back toward the head rail, if something is in the way). False if no room.
    public bool RespawnCue()
    {
        float d = BallRadius * 2f + 0.001f;
        for (int back = 0; back < 6; back++)
        {
            float x = HeadSpotX - back * d;
            if (x < -HalfLength + BallRadius) break;
            for (int k = 0; k < 15; k++)
            {
                float y = (k == 0) ? 0f : ((k % 2 == 1) ? 1f : -1f) * ((k + 1) / 2) * d;
                if (Math.Abs(y) > HalfWidth - BallRadius) continue;
                if (SpotFree(x, y, d)) { X[Cue] = x; Y[Cue] = y; VX[Cue] = 0f; VY[Cue] = 0f; Active[Cue] = true; return true; }
            }
        }
        return false;
    }

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

    bool SpotFree(float x, float y, float minDist)
    {
        for (int i = 1; i < BallCount; i++)
        {
            if (!Active[i]) continue;
            float dx = X[i] - x, dy = Y[i] - y;
            if (dx * dx + dy * dy < minDist * minDist) return false;
        }
        return true;
    }

    // ── Stepping ────────────────────────────────────────────────────────────

    /// Advance the table by `seconds` of game time (accumulated into fixed substeps; capped at 0.1 s a call).
    public void Advance(float seconds)
    {
        if (seconds <= 0f) return;
        _accum += Math.Min(seconds, 0.1f);
        while (_accum >= StepDt) { Step(StepDt); _accum -= StepDt; }
    }

    /// Run the table to rest RIGHT NOW (the same fixed substeps as Advance, so the
    /// balls end exactly where waiting would have put them). Capped so a bad
    /// state can't hang the frame.
    public void SettleNow(float maxSeconds = 60f)
    {
        int steps = (int)(maxSeconds / StepDt);
        while (!AllStopped && steps-- > 0) Step(StepDt);
        _accum = 0f;
    }

    void Step(float dt)
    {
        // 1. integrate + friction
        for (int i = 0; i < BallCount; i++)
        {
            if (!Active[i]) continue;
            float vx = VX[i], vy = VY[i];
            if (vx == 0f && vy == 0f) continue;
            X[i] += vx * dt; Y[i] += vy * dt;
            float v = (float)Math.Sqrt(vx * vx + vy * vy);
            float nv = v - RollingDecel * dt;
            nv *= 1f - LinearDrag * dt;
            if (nv <= StopSpeed) { VX[i] = 0f; VY[i] = 0f; }
            else { float s = nv / v; VX[i] = vx * s; VY[i] = vy * s; }
        }

        // 2. ball–ball
        float diam = BallRadius * 2f;
        for (int a = 0; a < BallCount; a++)
        {
            if (!Active[a]) continue;
            for (int b = a + 1; b < BallCount; b++)
            {
                if (!Active[b]) continue;
                float dx = X[b] - X[a], dy = Y[b] - Y[a];
                float d2 = dx * dx + dy * dy;
                if (d2 >= diam * diam || d2 < 1e-12f) continue;
                float d = (float)Math.Sqrt(d2);
                float nx = dx / d, ny = dy / d;
                // separate (half each)
                float pen = (diam - d) * 0.5f + 1e-5f;
                X[a] -= nx * pen; Y[a] -= ny * pen;
                X[b] += nx * pen; Y[b] += ny * pen;
                // impulse only if approaching
                float rvx = VX[b] - VX[a], rvy = VY[b] - VY[a];
                float vn = rvx * nx + rvy * ny;
                if (vn >= 0f) continue;
                float j = -(1f + BallRestitution) * vn * 0.5f;   // equal masses
                VX[a] -= nx * j; VY[a] -= ny * j;
                VX[b] += nx * j; VY[b] += ny * j;
                BallsCollided?.Invoke(a, b, -vn);
            }
        }

        // 3. pockets, then cushions
        float limX = HalfLength - BallRadius, limY = HalfWidth - BallRadius;
        for (int i = 0; i < BallCount; i++)
        {
            if (!Active[i]) continue;
            int p = PocketAt(X[i], Y[i]);
            if (p >= 0)
            {
                Active[i] = false; VX[i] = 0f; VY[i] = 0f;
                BallPocketed?.Invoke(i, p);
                continue;
            }
            bool inCornerJaw = Math.Abs(X[i]) > HalfLength - CornerJawHalfWidth && Math.Abs(Y[i]) > HalfWidth - CornerJawHalfWidth;
            bool inSideJaw = Math.Abs(X[i]) < SideJawHalfWidth;
            // long cushions (±y)
            if (Math.Abs(Y[i]) > limY && !inCornerJaw && !inSideJaw)
            {
                float sgn = Y[i] > 0f ? 1f : -1f;
                Y[i] = sgn * limY;
                if (VY[i] * sgn > 0f) { CushionHit?.Invoke(i, Math.Abs(VY[i])); VY[i] = -VY[i] * CushionRestitution; }
            }
            // short cushions (±x)
            if (Math.Abs(X[i]) > limX && !inCornerJaw)
            {
                float sgn = X[i] > 0f ? 1f : -1f;
                X[i] = sgn * limX;
                if (VX[i] * sgn > 0f) { CushionHit?.Invoke(i, Math.Abs(VX[i])); VX[i] = -VX[i] * CushionRestitution; }
            }
            // Safety net: a ball deep in a jaw that somehow missed its pocket is pocketed anyway.
            if (Math.Abs(X[i]) > HalfLength + BallRadius || Math.Abs(Y[i]) > HalfWidth + BallRadius)
            {
                int q = NearestPocket(X[i], Y[i]);
                Active[i] = false; VX[i] = 0f; VY[i] = 0f;
                BallPocketed?.Invoke(i, q);
            }
        }
    }

    int PocketAt(float x, float y)
    {
        for (int p = 0; p < 6; p++)
        {
            float dx = x - PocketX[p], dy = y - PocketY[p];
            if (dx * dx + dy * dy < PocketR[p] * PocketR[p]) return p;
        }
        return -1;
    }

    int NearestPocket(float x, float y)
    {
        int best = 0; float bd = float.MaxValue;
        for (int p = 0; p < 6; p++)
        {
            float dx = x - PocketX[p], dy = y - PocketY[p];
            float d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    // ── Aim guide ───────────────────────────────────────────────────────────

    /// Sweep the cue ball along (dirX, dirY). Returns false if the cue ball is off
    /// the table. (cx, cy) = the cue ball's centre at first contact; hitBall = the
    /// ball it touches (−1 for a cushion); (outX, outY) = the unit direction that
    /// ball is sent in (the contact normal), or the reflected cue direction for a cushion.
    public bool CastCueBall(float dirX, float dirY, out float cx, out float cy, out int hitBall, out float outX, out float outY)
    {
        cx = X[Cue]; cy = Y[Cue]; hitBall = -1; outX = dirX; outY = dirY;
        if (!Active[Cue]) return false;
        float len = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 1e-6f) return false;
        dirX /= len; dirY /= len;

        float px = X[Cue], py = Y[Cue];
        float best = float.MaxValue;
        // cushions
        float limX = HalfLength - BallRadius, limY = HalfWidth - BallRadius;
        if (dirX > 1e-6f) best = Math.Min(best, (limX - px) / dirX);
        if (dirX < -1e-6f) best = Math.Min(best, (-limX - px) / dirX);
        if (dirY > 1e-6f) best = Math.Min(best, (limY - py) / dirY);
        if (dirY < -1e-6f) best = Math.Min(best, (-limY - py) / dirY);
        if (best < 0f) best = 0f;
        bool cushionX = false;
        {
            float hx = px + dirX * best;
            cushionX = Math.Abs(Math.Abs(hx) - limX) < 1e-4f;
        }
        // balls: |p + d t − c| = 2r
        float diam = BallRadius * 2f;
        for (int i = 1; i < BallCount; i++)
        {
            if (!Active[i]) continue;
            float ox = px - X[i], oy = py - Y[i];
            float b = ox * dirX + oy * dirY;
            float c = ox * ox + oy * oy - diam * diam;
            float disc = b * b - c;
            if (disc < 0f) continue;
            float t = -b - (float)Math.Sqrt(disc);
            if (t < 0f) continue;
            if (t < best) { best = t; hitBall = i; }
        }
        cx = px + dirX * best; cy = py + dirY * best;
        if (hitBall >= 0)
        {
            float nx = X[hitBall] - cx, ny = Y[hitBall] - cy;
            float n = (float)Math.Sqrt(nx * nx + ny * ny);
            if (n > 1e-6f) { outX = nx / n; outY = ny / n; }
        }
        else
        {
            if (cushionX) { outX = -dirX; outY = dirY; } else { outX = dirX; outY = -dirY; }
        }
        return true;
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

    public float Speed(int i) => (float)Math.Sqrt(VX[i] * VX[i] + VY[i] * VY[i]);
}
