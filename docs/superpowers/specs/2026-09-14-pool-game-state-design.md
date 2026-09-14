🟢 ACTIVE — 2026-09-14 — Pool: game state, stripes/solids reveal, sunk-ball tray, 8-ball win/lose, ball in hand, one-occupant table (Sam approved the design in chat)

# Pool: from free play to "a pool table people can play pool on" — design

Builds on `2026-09-13-pool-table-design.md` (the table, sim, camera and shot).

## What Sam asked for

The table stays **free play — no enforced turns, no fouls**. Players decide the
rules between themselves ("play by yourself and sink all the balls, or take
turns with a friend, and maybe sneak an extra turn if they aren't looking").
The game only gives them the *information* pool players want:

- **Stripes or solids.** Nothing sunk on the break decides anything. The first
  object ball you sink on any shot *after* the break makes you SOLIDS or
  STRIPES (whichever it was), and any other player at that table becomes the
  opposite. Once decided it never changes until a re-rack.
- **A tray of the balls you sank**, shown only while you are in the shot view
  (F-mode), bottom-centre where the hotbar normally sits (the HUD is hidden in
  F-mode). Every object ball you sink is added, newest on the right — including
  the "wrong" group and the 8. The cue ball is never listed. Leave the table
  (F) and the tray goes; come back and it reappears with everything you sank
  this game.
- **One person on the table at a time.** In co-op the table refuses F while
  someone else is shooting; the prompt says so. Solo this is invisible.
- **The 8 ball ends the game — the only rule the table enforces.** Sink it
  with any ball of your group still on the table → **YOU LOSE**. Sink it with
  your group gone → **YOU WIN**. Sink it before any group has been decided
  (on the break, or later with nothing decided) → it is spotted back on the
  foot spot and play goes on. A win/lose banner holds ~2.5 s, then the table
  auto re-racks (trays clear, next game).
- **Ball in hand (scratch).** Fouls are never judged by the game — hit nothing,
  hit the wrong ball first, whatever: the players decide. So there is a button
  for it: **G** (pad: D-pad down) while the balls are still picks the cue ball
  up; WASD / left stick slides it inside the **kitchen** (the quarter of the
  table behind the head string, on the breaking end); it shows red where it
  would overlap another ball; **Space / A** puts it down. Then aim and shoot as
  usual. A sunk cue ball still comes back to the head spot by itself (nobody is
  ever stuck without a ball); G is how the next shooter moves it.
- **The controls line at the bottom of the shot view always lists every key**,
  G included, so a player never has to guess.
- **Re-rack (R, or the auto re-rack when the last object ball drops, or the
  one after a win/lose banner) wipes everything** — all trays, all groups, the
  break flag.

Decisions made in chat (2026-09-14):

- Tray is F-mode only (Sam picked this over an always-visible tray).
- Stripes/solids follows the real rule (break sinks don't decide).
- **Multiplayer sync is NOT built today.** Sam is reworking MP tomorrow with
  both PCs; today builds all the logic with the seams `PoolSync` will need
  (players keyed by client id, all mutations through a few table methods). The
  sync is `StasisDoorSync`-shaped: host runs the sim, clients send
  claim/release/strike, host broadcasts ball state + occupant + game state.
- Nothing is saved to the world save. A reload already re-racks the table, so
  the tray clearing with it is consistent.
- Win/lose banner style = mockup **A** ("bracketed headline"): big YOU WIN /
  YOU LOSE in the upper third framed by the power bar's corner brackets, a
  reason line under it (`SOLIDS CLEARED · 8 BALL DOWN` / `8 BALL DOWN · 3
  SOLIDS LEFT`), a thin line draining down to the re-rack. Accent colour for a
  win, the power bar's "hot" red for a loss. The tray stays visible below.

## Architecture

One new plain C# class, edits to three existing pool files, tests added to the
existing headless harness.

```
Assets/3 - Scripts/Pool/
  PoolGameState.cs   NEW  plain class, zero Unity refs — the running game
  PoolTable.cs       EDIT owns a PoolGameState; occupant claim/release; feeds pockets
  PoolShotSession.cs EDIT claims/releases the table; pushes tray + group to the HUD
  PoolShotHUD.cs     EDIT ball tray row + SOLIDS/STRIPES label + reveal flash
prototypes/pool/test/
  PoolSimTests.cs    EDIT game-state checks
  verify-pool.py     EDIT compile PoolGameState.cs too
```

### `PoolGameState` (plain class, `System` only — stays headless-testable)

```
enum Group { None, Solids, Stripes }

class PoolGameState
{
    // player ids are Netcode client ids (ulong); solo = 0

    bool BreakTaken                       // false on a fresh rack, true after the first strike
    bool HasOccupant; ulong Occupant      // who is on the table right now
    IReadOnlyList<int> SunkBy(ulong id)   // ordered ball numbers 1..15 (empty list if unknown)
    Group GroupOf(ulong id)               // None until decided

    void Reset()                          // re-rack: clears everything, occupant too
    bool CanClaim(ulong id)               // nobody holds it, or `id` holds it (a peek, no change)
    bool TryClaim(ulong id)               // CanClaim + claims; players become "known" here
    void Release(ulong id)                // only the holder can release
    void OnStrike(ulong shooter)          // marks the break taken (call BEFORE the balls roll)
    enum Verdict { None, Win, Lose, Spot8 }
    Verdict OnPocketed(ulong shooter, int ball, bool duringBreak, bool[] stillOnTable)
        // ball 0 → ignored, None. Ball 8: if shooter's group is None → Spot8 (not listed,
        // the table re-spots it); else listed and Win if no ball of the shooter's group
        // is left in `stillOnTable` (indices 1..15, the sim's Active[] AFTER this
        // pocket), Lose otherwise. Any other ball: append to shooter's list; if
        // !duringBreak and shooter's group is None: shooter = IsSolid(ball) ? Solids :
        // Stripes and every OTHER known player with None gets the opposite. Players
        // become "known" on TryClaim or OnPocketed.
    bool GameOver                         // set by a Win/Lose verdict; cleared by Reset
    static bool IsSolid(int ball) => ball >= 1 && ball <= 7
    static bool IsStripe(int ball) => ball >= 9 && ball <= 15
    static int GroupBallsLeft(Group g, bool[] stillOnTable)

    event Action Changed                  // fires after any mutation (HUD refresh hook)
}
```

"During break" = the strike that happens while `BreakTaken == false`. The table
captures `duringBreak` at strike time and holds it until the balls stop, so a
break-sink that rolls in late is still a break-sink.

The 8 ball is the ONLY rule. Once `GameOver` is set every further `OnPocketed`
is ignored (balls still rolling after the 8 dropped don't change the verdict);
the table re-racks after the banner.

### `PoolTable` edits

- `public PoolGameState Game { get; }` created in `Awake`.
- `Strike(dir, speed)` → remembers `_shotWasBreak = !Game.BreakTaken`, then
  `Game.OnStrike(Game.Occupant)`. `OnPocketed` → `Game.OnPocketed(Game.Occupant,
  ball, _shotWasBreak)` for `ball != Cue`.
- `ReRack()` → `Game.Reset()` **except the occupant**: R mid-game re-racks and
  the shooter is still on the table. (`Reset()` clears trays/groups/break;
  `ReRack` re-claims for the current occupant after it.)
- `OnPocketed` handles the verdict: `Spot8` → `Sim.Respot(8)` (new sim method:
  put a ball back on the foot spot, nudged toward the foot rail if blocked —
  the mirror of `RespawnCue`) once the table settles, with the same drop
  animation the cue ball gets; `Win`/`Lose` → `GameResult` event for the
  session/HUD + `_gameOverTimer`; when it passes `bannerSeconds` (2.5 s) AND
  `Sim.AllStopped` → `ReRack()`. The existing auto re-rack (object balls == 0)
  is unchanged and now only triggers if the 8 was spotted rather than sunk —
  in practice the 8 verdict always fires first.
- **Ball in hand:** `BeginBallInHand()` / `MoveBallInHand(dx, dy)` /
  `bool TryPlaceBallInHand()` / `CancelBallInHand()`. While in hand the sim's
  cue ball is `Active = false` (so nothing collides with it) and the table
  drives the visual from `_handX/_handY`, clamped to the kitchen (`x ∈
  [-HalfLength + BallRadius, HeadSpotX]`, `|y| ≤ HalfWidth - BallRadius`) and
  raised `ballRadius * 0.6` off the cloth. `Blocked` = any active ball within
  `2 * BallRadius + 1 mm`. `TryPlace` refuses while blocked, otherwise writes
  X/Y, re-activates, snaps the visual. Allowed only when `Sim.AllStopped`, not
  `GameOver`, cue ball on the table (not respawn-pending).
- `LocalPlayerId` static helper: `PlayerRoster`-derived client id when a
  `MultiplayerSession` is live, `0` otherwise. Lives in `PoolTable` for now;
  `PoolSync` can move it.
- `CanInteract()` → additionally `Game.CanClaim(LocalPlayerId)` (a peek, no
  claim). `BuildInteractMessage()` → "Someone's shooting" when held by another id.
- `Interact()` → `Game.TryClaim(LocalPlayerId)` then open the session; if the claim
  fails, do nothing.

### `PoolShotSession` edits

- `Open` claims via the table (above); `Teardown` releases
  (`Game.Release(LocalPlayerId)`) — covers F, abort and destroy.
- Subscribes to `Game.Changed` while open and pushes `SunkBy(me)` +
  `GroupOf(me)` to the HUD; unsubscribes in `Teardown`. Also pushes once on
  `Open` so a returning player sees their earlier balls immediately.
- New state **`BallInHand`**: entered from `Aiming` on G / D-pad down when the
  table allows it. WASD / stick move the ball in CAMERA-relative table
  directions (W = away from the camera along the aim yaw, D = right) at
  `handMoveSpeed` 0.5 m/s; Space / A → `TryPlaceBallInHand()` → back to
  `Aiming`; G again or B/right-click → `CancelBallInHand()` (ball returns to
  where it was picked up). F leaves as usual (cancels the hand first). The cue
  stick and aim guide hide while in hand; the view target follows the ball.
- New state **`GameOver`**: entered on the table's `GameResult` event from any
  state; input is dead except F (leave); the HUD shows the banner; the table
  re-racks itself and the session returns to `Aiming` (view re-targets the
  cue ball) when `Game.GameOver` flips back to false.
- Hint line now: keyboard `A D turn   W S tilt   Shift fine   hold LMB power
  G ball in hand   R re-rack   F leave`; pad `Stick aim   LT fine   hold RT
  power   D-pad↓ ball in hand   Y re-rack   X leave`. While in hand it swaps
  to `W A S D move   Space place   G cancel   F leave` / `Stick move   A place
  D-pad↓ cancel   X leave`.

### `PoolShotHUD` edits

- **Tray row**, bottom-centre, anchored (0.5, 0) at the hotbar's height
  (`BottomMargin + 36f` scaled like the hotbar: ~y 72 in 1080 reference units),
  icons 34 px, 6 px gap, centred as a group, newest appended on the right. A
  faint baseline hairline under the row (the hotbar's grounding trick) so the
  empty tray still reads as "your rack of sunk balls". The whole tray sits in a
  `CanvasGroup` that fades with the session's `SetVisible`.
- **Ball icon** = code-drawn, no assets: a disc sprite (a shared 64 px
  anti-aliased circle texture built once, like `HALVisuals.Disc()`), tinted the
  builder's `BallColors[n]` for solids, black for the 8, white for stripes. A
  stripe's band is the same disc sprite tinted the ball colour and scaled
  (1, 0.42) on top of the white disc — no masks. Every icon then gets a 15 px
  white number spot with the number in TMP (`HudFontResolver`, bold, 11 pt,
  dark). The ball colours are copied into `PoolShotHUD` as a static table (the
  builder is Editor-only code).
- **Group label** above the row: empty while `Group.None`, otherwise "SOLIDS" /
  "STRIPES" in `HelmetHudPalette.Accent`, 15 pt letter-spaced. When the group
  changes from None the label pops in with the power-bar bracket flash (corner
  brackets appear at `AccentGlow`, scale 1.15 → 1 over 0.35 s) and the label
  glows for ~1 s before settling.
- **Win/lose banner** (mockup A): a centred block at 30 % from the top —
  "YOU WIN" / "YOU LOSE" 64 pt bold letter-spaced with the four corner
  brackets (`BracketArm`/`BracketThick` of the power bar, scaled ×2), the
  reason line 16 pt under it, and a 120 px drain line below that shrinks from
  full to nothing over `bannerSeconds`. Accent for a win; the power bar's hot
  red (`0.94, 0.26, 0.18`) for a loss. Pops in over 0.25 s (scale 1.15 → 1,
  alpha 0 → 1), fades out over 0.2 s at the end.
- **Ball-in-hand feedback:** the hint line swap (above) and the ball itself
  turning red when blocked (a property block tint on the cue ball renderer —
  restored on place/cancel). No extra UI.
- New public API: `SetTray(IReadOnlyList<int> balls)`, `SetGroup(Group g, bool
  animate)`, `ShowResult(bool win, string reason, float seconds)`, `HideResult()`.
  Tray/group change-detected (rebuild icons only when the list length or
  contents differ).

### Not done here

- Network sync (`PoolSync`) — tomorrow's MP pass. (The other player's screen
  showing "<name> wins" is part of that.)
- Turn order, fouls, "hit your own ball first", "call the pocket" — never.
- Saving a game in progress.

## Testing

`py -3 prototypes/pool/test/verify-pool.py` compiles `PoolPhysics2D.cs` +
`PoolGameState.cs` + tests and runs them. New checks:

1. Fresh state: no occupant, no break, empty trays, `Group.None`.
2. Cue ball (0) pocketed → never listed, never decides.
3. Break sink (`duringBreak = true`) of ball 3 → listed for shooter, group stays None.
4. Post-break sink of 3 → shooter Solids; a second known player → Stripes.
5. Post-break sink of 12 first → shooter Stripes, other Solids.
6. 8 with no group → `Spot8`, not listed, no GameOver; next sink of 5 → Solids.
7. Group never changes after being set (sink a stripe as Solids → listed, still Solids).
8. Claim: A claims → B claim fails, A re-claim ok, B release does nothing,
   A release → B claim ok.
9. Reset clears trays, groups, break flag, occupant and GameOver.
10. Order preserved: sink 3, 11, 5 → tray reads [3, 11, 5].
11. 8 as Solids with a solid left → `Lose`, listed, GameOver; later pockets ignored.
12. 8 as Solids with all 7 solids gone → `Win`.
13. 8 as Stripes with all solids gone but a stripe left → `Lose` (it is YOUR group that counts).
14. Sim: `Respot(8)` puts the 8 on the foot spot; when the foot spot is blocked it
    lands nudged toward the foot rail, overlapping nothing.
15. Sim: ball in hand — `Active[0] = false` while lifted; placing on top of ball 3 is
    refused; placing on a clear kitchen spot re-activates at that spot.

Editor/playtest (Sam): break, sink one → tray shows it, no label; sink one on
the next shot → SOLIDS/STRIPES flash; leave and return → tray intact; R → all
cleared; auto re-rack → cleared.
