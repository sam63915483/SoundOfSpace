🟢 ACTIVE — 2026-09-14 — Pool: game state, stripes/solids reveal, sunk-ball tray, one-occupant table (Sam approved the design in chat)

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
- **Re-rack (R, or the auto re-rack when the last object ball drops) wipes
  everything** — all trays, all groups, the break flag.

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
    void OnPocketed(ulong shooter, int ball, bool duringBreak)
        // ball 0 → ignored. Otherwise append to shooter's list. If !duringBreak and
        // shooter's group is None: shooter = IsSolid(ball) ? Solids : Stripes and every
        // OTHER known player with None gets the opposite. Players become "known" on
        // TryClaim or OnPocketed.
    static bool IsSolid(int ball) => ball >= 1 && ball <= 7   // 8 is neither; sinking it decides nothing
    static bool IsStripe(int ball) => ball >= 9 && ball <= 15

    event Action Changed                  // fires after any mutation (HUD refresh hook)
}
```

"During break" = the strike that happens while `BreakTaken == false`. The table
captures `duringBreak` at strike time and holds it until the balls stop, so a
break-sink that rolls in late is still a break-sink.

The 8 ball: listed in the tray, decides nothing, no rule attached (free play).

### `PoolTable` edits

- `public PoolGameState Game { get; }` created in `Awake`.
- `Strike(dir, speed)` → remembers `_shotWasBreak = !Game.BreakTaken`, then
  `Game.OnStrike(Game.Occupant)`. `OnPocketed` → `Game.OnPocketed(Game.Occupant,
  ball, _shotWasBreak)` for `ball != Cue`.
- `ReRack()` → `Game.Reset()` **except the occupant**: R mid-game re-racks and
  the shooter is still on the table. (`Reset()` clears trays/groups/break;
  `ReRack` re-claims for the current occupant after it.)
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
- The hint line gains nothing (R re-rack is already listed).

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
- New public API: `SetTray(IReadOnlyList<int> balls)`, `SetGroup(Group g, bool
  animate)`. Both change-detected (rebuild icons only when the list length or
  contents differ).

### Not done here

- Network sync (`PoolSync`) — tomorrow's MP pass.
- Any rule enforcement (turns, fouls, 8-ball loss).
- Saving a game in progress.

## Testing

`py -3 prototypes/pool/test/verify-pool.py` compiles `PoolPhysics2D.cs` +
`PoolGameState.cs` + tests and runs them. New checks:

1. Fresh state: no occupant, no break, empty trays, `Group.None`.
2. Cue ball (0) pocketed → never listed, never decides.
3. Break sink (`duringBreak = true`) of ball 3 → listed for shooter, group stays None.
4. Post-break sink of 3 → shooter Solids; a second known player → Stripes.
5. Post-break sink of 12 first → shooter Stripes, other Solids.
6. Post-break sink of 8 → listed, group stays None; next sink of 5 → Solids.
7. Group never changes after being set (sink a stripe as Solids → listed, still Solids).
8. Claim: A claims → B claim fails, A re-claim ok, B release does nothing,
   A release → B claim ok.
9. Reset clears trays, groups, break flag and occupant.
10. Order preserved: sink 3, 11, 8 → tray reads [3, 11, 8].

Editor/playtest (Sam): break, sink one → tray shows it, no label; sink one on
the next shot → SOLIDS/STRIPES flash; leave and return → tray intact; R → all
cleared; auto re-rack → cleared.
