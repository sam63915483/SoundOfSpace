# Alien Football — how it works right now (2026-09-19, pass 2)

The live reference for the football prototype. The design briefs are
`Handoff_AlienFootball_Phase1_v1.md` (✅ BUILT — the original *why*) and
`superpowers/specs/2026-09-19-football-watchability-design.md` (the second
pass: seven a side, the real snap, routes, rollouts, Madden dead-ball, jukes /
spins / hurdles / dives / fumbles). This file is the *what*. Phase 1 was
playtested by Sam in `Assets/4 - Scenes/Proto_Football.unity`; pass 2 is
built and soaked, **playtest pending**. Next after that: a stadium on Cyclops,
then Phase 2 (the player subs in at QB).

## Open the scene

`Tools ▸ Football ▸ Open Proto Football Scene`, press Play. The game runs itself:
coin toss → kickoff → drives → four 2-minute quarters (clock stops after
incompletions, out of bounds, turnovers and scores) → final → new game 25 s
later. A game is ~15 minutes at ×1. The player spawns on the home sideline at
the 50 and can walk anywhere (the stadium is on the Body layer). Nothing from
the main game loads.

- **F8** debug panel: sim speed (×0.25–×4), force a play (all 21), whistle, new
  game, flip possession, end quarter. **[ ]** sim speed. **F7** hide the
  play-by-play. A ■ after the clock means it is stopped.
- Sim speed is `FootballMatch.simSpeed`, the sim's OWN multiplier — never
  `Time.timeScale` (safe next to the real world; see the pause rule in CLAUDE.md).
- Play-by-play lines go to the console as `[Football] …`.

`Tools ▸ Football ▸ Build Proto Football Scene` wipes and rebuilds the scene
(stadium ×1.2, field paint, FootballMatch, scoreboard). It keeps the scoreboard
where Sam put it (`CaptureBoardPose`). It also converts the stadium pack's URP
materials to Standard in place (they ship magenta otherwise).

## Files — `Assets/3 - Scripts/Football/`

| File | Owns |
|---|---|
| `FootballField.cs` | Dimensions and the frame. **1 yard = 1 metre** (deliberate: fills the 105 m pitch with a 120-yd field). +Z = away end zone. `ToWorld/ToLocal`. Everything else is FieldRoot-local. |
| `FootballFieldMarkings.cs` | ExecuteAlways paint: yard lines, hashes, numbers + arrows (TMP, face-dilated), mow stripes. |
| `FootballTeam.cs` | Name, colour, the five 0–1 stats (blocking, passRush, coverage, qbAccuracy, speed), score, attackDir. |
| `IPlayerBrain.cs` | The brain interface, `FootballRole` (QB WR OL DL DB LB **C S**), `BrainAction` (Throw Handoff Kick **Juke Spin Hurdle**), `PlayView` (read-only view: `SnapInFlight`, `qbExtending`, `NearestStanding`…), `BrainOutput` (move, action, `face`, `say`). |
| `FootballBall.cs` | Analytic parabola in field space (NOT a rigidbody). `Launch`, `Snap` (centre → QB), `Fumble`, `Drop` (dead where it lands) vs `liveOnGround` (bounces, rolls, anyone can have it), `catchPoint/catchTime`, `TouchDistance`. |
| `FootballPlayer.cs` | The slot: kinematic movement, both-ways roles (`SetSide`), the timed body states — `Jump`, `FallDown`, `HardFall` (tumble onto the back), `Emote`, `StartJuke/Spin/Hurdle/Dive` — `ReachFor`, `ArmGap` (the catch test), `SetHold` + `BallHoldPoint/Rotation` (where the ball sits for a hold style), `settled`. |
| `FootballAlienRig.cs` | Procedural animation for the Alien_Pack rigs: run cycle, reach, throw, `HoldStyle` (TwoHands / Tucked / SnapStance / ReadyHands), `EmoteKind` (ArmsUp, FirstDown, Flex, ChestThump, IncompleteWave, Point, Dejected), hurdle / dive / spin / tumble poses. Two-bone arm solve (`TwoBone`) so hands can be PUT on the ball. |
| `FootballPlays.cs` | `FootballRoutes` (the route tree: go, seam, fade, slant, quick out, deep out, dig, shallow in, curl, hitch, comeback, post, corner, sluggo, out-and-up, zig zag, wheel, drag, deep cross, flat), `FootballFormation` (spread, spread left, trips right/left, tight), `FootballPlay` — 21 concepts weighted by down/distance, never the same call twice running. |
| `FootballBrains.cs` | `Steer` helpers + every CPU brain: `QBBrain_CPU` (4 styles, scramble drill), `WRBrain` (cuts, settle, scramble-drill adjust), `OLBrain`, `CenterBrain`, `DLBrain`, `DBBrain` (drives on a settled man), `SafetyBrain` (deep middle), `LBBrain` (spy), `BallCarrierBrain` (lanes + jukes / spins / hurdles), kick brains, `MoveToBrain` (retargetable; the ball return uses it). |
| `PlayInstance.cs` | One snap or kickoff: ball return → huddle → formation → snap flight → live → whistle. Actions, catches, drops, tackles, dives, hurdles, fumbles, engagements, pocket, emotes, `PlayStats`. |
| `FootballMatch.cs` | The game: state machine, clock + stoppages, downs, scoring, halves, play-by-play, `GameStats`, debug panel, field lines, squads. Builds the NEXT play the instant the previous ends. Phase 2 hooks: `PlayEnded`, `Possession/IsOffense`, `qbBrainOverride[team]`. |
| `FootballScoreboard.cs` | Dot-matrix 7-seg board. Reads `FootballMatch.Instance`. |
| `Editor/FootballProtoSceneBuilder.cs` | The scene builder + the URP→Standard material converter. |

The stadium pack is `Assets/LargeFootballStadium/` (a $50 **soccer** stadium,
596 MB, **gitignored** since 2026-09-19 like `Backrooms/` — re-import locally).
The aliens are `Assets/5 - External Imports/Alien_Toys/Alien_Pack/Prefab/Built-In/Alien1–10`
(Humanoid-imported UE-named skeletons, T-pose, ~0.87 m tall, scaled to 2 m, face +Z).

## Rules of the sim (the ones that were hard-won)

**Seven a side, both ways.** QB/LB, C/S, two OL/DL, three WR/DB. The centre is
over the ball; the safety is the deepest man. The C/S slot runs at 0.95× (a big
athlete, NOT lineman speed — at 0.85× the safety could never catch a receiver
and every catch was a touchdown).

**Movement.** Kinematic, field space, accel-limited toward top speed (8 m/s at
speed stat 0.5; OL/DL ×0.85, QB and C/S ×0.95, DB/S ×1.03 on defense). Nobody
is ever teleported mid-session (Sam: it ruins the illusion) — **not even the
ball**. The lineup walks; if it takes too long (`SetupTimeout` 16 s) the play
starts with stragglers running in. `Bench()` only at boot.

**The dead ball is choreographed** (`PlayInstance.TickBallReturn`, run through
the Setup phase of the NEXT play, which `FootballMatch` builds the instant the
previous one ends). Whoever ended with the ball finishes his emote, jogs it to
the centre (the kicker on a kickoff) and hands it over; a ball on the grass is
fetched by the nearest man of the new offense; the centre carries it to the
line and puts it down (`Place`); the QB stands with his hands out; the snap
cannot come until `BallReady`. Everyone else huddles, breaks, lines up as
before. If the ball is *still* not there after `OfficialsSpotAfter` (30 s) the
officials spot it — the one place code moves the ball, logged and counted; a
soak that shows it has found a bug. Dead ball averages ~11 s.

**The snap** (`PlayInstance.Snap`): the centre fires it from the grass to the
QB's hands as a 0.38 s flat flight with the QB as intended receiver; the
existing arm-touch catch resolves it and the QB's brain is NOT swapped. 2%
of snaps are wild (σ 1.3 m); a snap the QB doesn't catch is live on the ground
— usually the QB falls on it. While it is in the air the line blocks as if
the QB already had it (`BlockSlowdown(carrier ?? qb)` — without that the rush
had a free 0.38 s and sacked him 65 times a game).

**Holding the ball** (`HoldStyle`, Sam): the QB holds it in two hands at the
chest until he tucks; a catch is gathered in two hands for `CatchDipSeconds`
then tucked; anyone running with it has it tucked in the right forearm; the
centre is in his stance over it. The ball's position and rotation come from
the pose (`BallHoldPoint/Rotation`), so it sits in the hands.

**Blocking** = sticky engagements with shed timers (`PlayInstance.BlockSlowdown`):
a defender who runs into an offensive body is held to a shove until he sheds.
Linemen (OL/C on DL) lock for 0.9–2.0 s ×1.6 at `_engagedScale`; **a receiver
blocking downfield only gets a 0.45–0.9 s shove at 0.5×** (at 0.3× for 2 s no
pursuit ever closed). Once shed, that blocker can't hold him again. The pocket
timer (1.8–3.6 s from passRush vs blocking) frees one rusher who ignores it.
The centre picks up whoever is loose.

**Reading the play.** After a handoff (or the QB tucking it) the defense is
flat-footed for `PlayView.readDelay` (0.4 s pass / 0.55 s QB Power / 0.8 s
sweep or draw).

**Coverage.** Per-snap variance (`DBBrain` ctor: cushion, reaction, `_turnAt`).
The DB backpedals as the WR approaches, then trails. **A settled receiver**
(curl / hitch / comeback / the scramble drill: stopped, facing the QB,
`FootballPlayer.settled`) is a legal target for the QB, and the DB drives on
him from the QB's side after `_reaction + 0.35 s`. The safety sits 3.5–6.5 yd
over the deepest route (never shallower than 10), breaks on any ball he can
reach, and comes up on a run once it crosses the line.

**The QB** rolls a style every snap: Quick 32% / Patient 31% / DeepShot 22% /
**Rollout** 15% (and every `Kind.Rollout` play — Roll Flood, Boot — rolls to the
slot's side by design). Openness is in TIME (`OpenSeparation` 3.9 m ≈ 0.5 s).
Leads by the ball's REAL flight time. Throwing back across his body on the run
costs +1.8 m of window. When the pocket goes he rolls away from the rusher;
**when his clock is nearly out with nobody open he leaves the pocket anyway
(the scramble drill, +1.8 s of hold)** while receivers with finished routes
work back to his side and settle. On the run he takes a 50/50 ball 10+ yards
downfield; **a rolling QB who reaches the line tucks it and takes off** (holding
him at the line was a sack every time). 65% of hold-expiries are a run.

**Runs.** Jet Sweep (slot in motion), QB Draw (1.0 s fake), **QB Power** (tucks
at 0.3 s from the tight formation, everyone blocks). Runs are ~15% of calls.

**Passes.** Short balls fly flat and fast (0.6 s); anything past 15 m is lofted
(3 m apex). Receivers `Steer.MeetBall`. Receivers throttle to 0.5 into a cut of
55°+ so a break reads as a plant.

**Catching** (Sam's rule). The man it's for and the nearest defender jump and
point both arms at it; the ball must pass within arm's length of a shoulder
(`ArmGap`). Receiver drops 30%, defender 50%. Linemen and the centre are
ineligible. After the catch the carrier runs at `CarrierSpeed` 0.88 (0.97 for a
QB) with a 0.45 s gather.

**Tackles and dives** (`PlayInstance.TickTackles`). Inside `TackleRadius` 1.5 m
a standing lunge: misses 15% (45% if blocked, 22% from behind, +22% if the
runner is mid-juke, +22% mid-spin, capped at `MaxMissChance` 52%). From 1.7–2.7 m
a free defender closing at 2.5+ m/s **dives** 45% of the time: a 0.5 s committed
lunge at 1.35× speed; contact is tested in the middle of it within `DiveReach`
1.6 m; no contact = he is on the ground alone. A dive that connects: tackle,
5% fumble.

**The carrier's moves** (`BallCarrierBrain`, on a 1.1 s cooldown, odds scaled by
the team's `speed`): a man square in front 1.7–3.6 m away → **juke** (45%×) to
the side he isn't; from the side or behind → **spin** (28%×, speed ×0.7, body
turns 360°); a man **diving** at him → **hurdle** (72%) or step round it. A
hurdle clears the dive unless *clipped* (`HurdleClipChance` 22%, 50% if the
leap started in its first 12% or last 10%): the carrier tumbles onto his back
(`HardFall`, down 2.6 s) and fumbles 35% of the time. Hurdling into a standing
man is a tackle with a 10% fumble.

**Fumbles** are live: the ball pops out, bounces twice (`FootballBall`), rolls.
Whoever gets within 1 m: 60% falls on it (dead there; a defender's recovery is
a turnover), 40% scoops and runs. Nobody catches a fumble in the air.

**The throw** is a 0.42 s arm swing; the ball leaves the hand at 58% of it.

**Emotes** (`PlayInstance.Celebrate`, Madden moments): TD → scorer arms up or a
point, nearby teammates flex; first down on his feet → the first-down signal
(chest thump for 20+); sack → the tackler flexes; interception / lost fumble →
the taker arms up, the loser hands on hips; breakup → the DB's incomplete wave.
A tackled man plays his as he gets up (`FootballPlayer.Emote` queues it).
Emoting men stand still; nobody waits for them beyond the hold.

**After the whistle nothing freezes**: the next play's Setup ticks everyone
through every hold state (`FootballMatch.TickHeld`).

**Kickoffs** land around the 10 (55 ± 7 yd from the 35); the returner paces to
meet it. Returns come out to the 20–30.

**Ball is analytic, not PhysX.** On Cyclops a rigidbody would need floating-origin
registration and the planet's gravity direction; the field-space parabola needs
neither.

## Numbers from the last headless soak (3 games, seeds 1000–1002)

21–14, 28–0, 14–7 · 53–61 plays/game · 45–55% completions · 6–9 yds/play ·
2–6 INT · 4–12 sacks · ~15 rollouts + ~10 scramble drills · ~10 jukes, ~4 spins,
4–8 hurdles (1–6 clipped), ~20 dives (half connect) · 0–2 fumbles · every snap
caught bar 1–2 wild ones · ~65 emotes · dead ball 11 s · officials never spotted
the ball · a game is ~15 sim-minutes. Gains skew big (most completions go
20–34 yards: catches at 18–22 yd depth plus run-after). INTs are a little high.
Both are Sam's call from the stands.

## Testing without play mode

Sam runs every playtest. For everything else, `tools/football/*.cs.txt` are
edit-mode editor scripts (copy to the scratchpad, replace `<scratchpad>/`, run
with Coplay `execute_script`): `FootballSoak` (3 games, play-by-play + the
`GameStats` summary), `FootballProbe` (tick trace), `KickProbe` (one kickoff
tick by tick), `CatchProbe` (pursuit gap after every catch — this is what found
the slow safety and the 2 s downfield blocks), `PoseCheck` (14 rig states
rendered to PNG, with the ball drawn at its hold point). Every new behaviour
has a counter in the summary line so it can be seen without play mode.

🔥 An unfocused editor compiles but does NOT reload the domain — a soak after an
edit can run the OLD code. Check the DLL is newer than the source
(`Library/ScriptAssemblies/Assembly-CSharp.dll`), or run a refresh script
(`AssetDatabase.Refresh` + `CompilationPipeline.RequestScriptCompilation`) and
probe that a new symbol resolves.

## Known gaps / next

- Animation is aim-the-bone math: joints can twist at extremes (FromToRotation
  has no roll control), no head tracking, no collision pose on a tackle. The
  new poses (tuck, two hands, stance, emotes, hurdle, dive, tumble) render
  right in the pose check but have not been watched in motion yet.
- Team tint on the alien skins may not show (their shader may ignore `_Color`);
  the overhead labels are team-coloured.
- No punting, no field goals, no safeties, no penalties, no stiff-arm.
- No crowd, no sound, no sideline, no camera work.
- Phase 2 (the player at QB) is designed in: a brain with
  `KeepsControlWhenCarrying` in `qbBrainOverride[team]`; `PlayInstance` doesn't
  care who is driving the QB slot. The snap catch keeps whatever brain the QB has.
