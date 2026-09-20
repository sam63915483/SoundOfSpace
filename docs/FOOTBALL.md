# Alien Football — how it works right now (2026-09-20, pass 5: solid bodies)

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
| `FootballBodies.cs` | **Solid bodies (pass 5).** Every man is a disc (radius, mass); `Resolve` separates overlapping pairs (heavier moves less, ≤ 0.12 m a tick), trades closing momentum (no bounce) and fills `PlayView.contacts`. The two formulas: `BlockDrive` (push vs hold) and `TackleHit` (closing × square). |
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

**Blocking is contact** (pass 5, `PlayInstance.ResolveEngagements`): a
defender whose disc collides with a blocker who is between him and the ball
is *engaged* (linemen always; anyone else only once the ball is past the line
or in a runner's hands — a receiver running his route into his corner is not
blocking him). The rusher pushes with `passRush`, the blocker holds with
`blocking`, each × his mass × a 0.8–1.2 swing rolled per snap, and the pair
moves along the rusher's line at the difference (+1.2 m/s driving the blocker
back, −0.15 when the blocker wins: he stands him up, he doesn't carry him).
Linemen mirror (`OLBrain`/`CenterBrain` pass-set: on his line to the ball, one
body in front) at a lateral cap (0.55× for a tackle, 0.75× the centre);
rushers **bull** (pinned at 0.05×, the drive moves the pair) or make a
committed **swim** step (0.75× sideways-and-up for 0.8 s; a stalled bull
tries one after 0.7 s; a failed swim re-pins him for 0.6 s) — whoever is
quicker today wins the edge. A rusher past his man along the line is free by
geometry. **The pocket has no clock**: it collapses when a free rusher has a
clear line inside 4.5 m or a blocker is driven to within 1.5 m of the QB.

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

**Tackles are contact** (pass 5, `PlayInstance.TickTackles`). A tackle
starts when a defender's disc touches the carrier's, a dive lands on it in
its window, or — arms being longer than a disc — a man within `ArmReach`
(0.55 m beyond the discs) who isn't being pulled away gets a hand on him.
`hit = (2.4 + closing) × (0.2 + 0.8 × square) × mass`, ±15 %; `square` is
how head-on the contact is to the runner's line (1 from straight behind or in
front, 0 glancing). `hit ≥ 2.3` = the **wrap** (drag for a stride, pile-on
by touch, and a break judged once at the midpoint: the runner's momentum vs
`hit × 3.8`); below it is an **arm tackle** — the runner stumbles through, a
body that missed falls past him, a reach that slipped only costs the chaser
half a second. A juke or spin misses because the body moved and the contact
went glancing, not because a state added a percentage. A blocked man can only
arm-tackle. Dives from 1.7–2.7 m need the gap to be closing (0.4 m/s); a hurdle
lifts the disc over a diver (the clip rule stays). A big hit jars the ball
loose in proportion to `hit`.

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

## Pass 3 (2026-09-19 evening) — huddles, the big screens, the QB buying time

Sam's second round of notes after watching pass 2. Built and looked at by me
in play mode (Sam's exception for this test scene); Sam has not watched it yet.

- **Huddles, both sides.** Offense in a ring 7.5 yd behind the ball facing
  the QB in the middle; defense in a ring 6.5 yd past it facing the LB. Once
  everyone is in, it HOLDS `PlayInstance.HuddleHold` (9 s), then breaks
  ("break the huddle" in the log). The centre carries the ball into the
  huddle and places it after the break. Dead ball is now ~19 s.
- **Stances** (`Stance` on the rig): huddle lean with hands on knees, linemen
  in a three-point, defenders crouched, receivers in a ready stance, and an
  idle breathe / weight-shift for anyone standing — nobody is a statue.
  **Facing turns at a rate** (`FootballPlayer.TurnRate*`), never snaps.
- **Accelerated clock** (`FootballMatch.playClockRunoff`, 25 s): after a play
  where the clock keeps running, 25 s burns fast during the huddle instead of
  real time; the clock runs live only while the play is live. Quarters are
  5:00 in the scene (`quarterSeconds`; a game is ~28 sim-minutes — lower it
  for a shorter game).
- **The pocket.** OL kick-slide to a pocket point (`OLBrain` ctor); DL take an
  edge point round the tackle before turning up (`DLBrain` ctor) — the arc is
  the cup. The pocket timer still frees one rusher.
- **The QB escapes** (`QBBrain_CPU`, Sam: "rolling out and running back
  further to lose yards and deke out the defense and buy time … then launch
  a cannon"): on pressure, or with his clock nearly out and nobody open, he
  leaves the pocket — away from the rusher, deeper while a man is on him (up
  to ~15 yd), a juke on anyone who gets within 2.4 m, one reverse of field
  if he runs out of room — and from deep in the backfield the deep men are
  weighted up and the window he'll accept opens (`floor` −1.9). He runs only
  when a lane opens or he's out of time (`EscapeSeconds` 3.4). Deep sacks are
  the price and Sam wants them.
- **Jumbotrons + replay** (`FootballBroadcast.cs`, created at runtime under
  FieldRoot by `FootballMatch.Boot` if the scene has none): a broadcast
  camera high on the home sideline renders to a texture on two big screens
  (one over each end zone; positions are fields on the component). Live it
  frames the ball — wide pre-snap, in on the huddle, on the QB in the pocket,
  tight on a runner, and on a throw it fits ball + landing spot. After a play
  worth seeing (score, first down, 8+ yd completion, INT, fumble, sack, a
  hurdle or spin) it plays the REPLAY 2 s later: every pose was recorded at
  30 Hz (`FootballPlayer.CapturePose`) and a second set of ghost aliens on
  layer 30 re-enacts it; quarter speed from 1 s before the key moment (catch >
  hurdle/spin/fumble > tackle) to 0.6 s after. Live bodies, labels and the
  ball are on layer 29 so the replay camera can hide them; every other camera
  is told to ignore layer 30. Both layers are unnamed spares.
- **Debug:** a file `build/football_dump.txt` holding a folder path makes the
  broadcast save its picture there every 2 s in play mode (and the player's
  view every 8 s) — how I watch a run without eyes. Delete it after.

Last soak (3 games): 14–21, 21–14, 28–7 · 67–78 plays · 5–9 sacks averaging
an 11-yd loss · 5–7 INT (high — `DefenderDropChance` / `OpenSeparation` are
the knobs) · ~30 throws on the run a game, 6 of them 30+ yards · dead ball
19 s · huddle every play.

## Pass 4 (2026-09-19 night) — the realism batch

- **UI:** the stadium keeps every current auto-HUD (VitalsHUD switches the
  legacy ResourceHUD bars off itself) and the scene has `HUD_Canvas` +
  `HelmetHudConfig` like the tutorial box (that config is what gives the
  compass / boost / vitals their current look). Only LightingDebugToolbox
  and PerfTrace are destroyed (`FootballMatch.Boot`). Tools ▸ Football ▸ Add
  Gameplay HUD / Add HUD Config / Add Jumbotron Anchors.
- **Down markers** on both sidelines at the line (and in replays).
- **Defensive calls** per snap: press / normal / off / blitz (`PickDefense`).
- **Plays:** End Around, Flea Flicker (instant pitch back; the QB keeps it
  once, looks deep), Screen (tight formation, only vs a cushion, line
  releases at 0.6 s, thrown at 1.9 s). Punts on 4th down (`ShouldPunt`: go
  on ≤2, in range ≤6, inside the 25, or trailing late). Moods: trailing in
  the last 3 min = deep shots + a 2 s hurry-up huddle; leading = the ground.
- **QB:** escape can start the instant he has the ball; cornered after a
  second of scrambling he gets rid of it (checkdown or THROWN AWAY over the
  sideline) instead of a second sidestep. Sacks ~5/game, throwaways ~5.
- **Tackles:** a hit is a WRAP (`EndTackle`/`TickWrap`, 0.55 s): arms round
  the waist, the runner drives at 0.4× (forward progress), anyone within
  1.4 m piles on, and a lone wrap can be BROKEN (`BreakTackleChance` × the
  team's speed). Then everyone falls where the ball is.
- **Catches:** two men on the ball = CONTESTED: position decides ~60–90%,
  the loser tips it up 35% of the time (a live 0.9 s pop-up anyone can
  catch, 25% drop). ~9 contested a game, 2 tipped.
- **Heads:** everyone looks at something (`BrainOutput.look`, `Head()` in
  the rig): receivers and corners at the ball, the QB at his read.
- **Blocking is contact** (`BlockContact` 1.0 m nudge) with arms out; an
  engagement ends the moment the rusher is past his man.
- Loose balls out of bounds are dead; routes and live bodies stay on the field.

Soak: 14–21, 14–28, 14–21 · 67–76 plays · 4–7 sacks · 4–5 INT (still a bit
high) · 13 punts / 3 games · 4–6 broken tackles, ~35 pile-ons, 8–10 contested
balls a game · 3–4 fumbles (lost 3 — watch this).

**Ideas not built yet, in the order they'd pay off:** balance the solid-body
game (see Pass 5) · stage 4: batted balls at the line, a loose ball off legs,
downed bodies stepped over · press-release moves at the line (swim/rip vs a jam) ·
stumble-on-contact and a real fall pose per hit direction · speed vs sharp
cuts and receivers looking back late on a comeback · DB ball skills (play the
hands, not the man, when beaten) · a play sheet per team with tendencies the
other side can read · celebrations with two men (chest bump) · a real
run-cycle with hip sway and foot planting · crowd noise keyed to the card.

## Phase 2 (2026-09-19, late) — you play QB

**How to play it:** a red button stands on the home sideline near the player
spawn (field `(-30.2, 0, 0)`). Look at the dome, press **F**. You are now the
blue team's (Twin Terrors) QB; their alien QB walks to the bench and vanishes,
and comes back for kickoffs, punts and defence. Press F again to sub out.

On each blue scrimmage play: the huddle breaks, the aliens line up, and a green
translucent cylinder marks the QB spot. Walk into it — it disappears, the line
sets, and about a second later the centre snaps the ball to your hands. The ball
lands in the hotbar (auto-equipped) and sits in front of the camera. Then:

- **Hold LMB** to charge the ARM (ball speed, 13 → 25 m/s over 3 s); the
  camera's PITCH is the angle. Look level and it is a bullet that carries the
  short routes (a tap ≈ 5 yd, full ≈ 20 yd); look up to loft it (full charge
  at 35° ≈ 70 yd). `FootballHumanQB.Aim` solves the parabola down to catch
  height and hands the sim that exact flight time (`BrainOutput.flight`), so
  the drawn arc, the ball and the receivers agree. **Release** to throw — the
  ball goes to the receiver nearest the landing spot (the normal catch /
  contest / drop rules apply, so a throw into a crowd is a throw into a crowd).
- The huddle breaks the moment you call the play (it used to also wait out the
  CPU's huddle hold and the replay).
- **Or run.** Cross the line of scrimmage with the ball and it is tucked; the
  defence tackles you the same way it tackles an alien. Getting tackled or
  sacked plants you for 1.6 s and ends the play; the ball is set at your feet
  for the centre. Your stride is scaled to 0.8 so you can be caught.

**The huddle (round 3):** when your team gathers, the green cylinder marks your
place in the huddle and everyone waits for you. Walk in and three play cards
come up (route art, first read in gold): **1 / 2 / 3**, or arrows + Enter, or
click. The huddle breaks on your call with that play's formation and routes
(`PlayInstance.ChoosePlay` re-lays the receivers and the men covering them);
calling it fast cuts the replay on the screens. Then the snap-spot cylinder.
Backspace with the other team on the ball throws the play away and gives your
team a 1st & 10 at the 25 (`FootballMatch.SkipToHuman`).
In replays your slot is an astronaut, not an alien: the recorder stores every
bone rotation of the real body (`Player/Astronaut`, the Animator's object) per
frame and a stripped clone under the field root re-enacts it. 🔥 The player
also has an INACTIVE placeholder capsule child literally named `Mesh` — cloning
by that name gives a white capsule. Your live body and the green cylinder are
in the replay camera's hide list (`FootballHumanQB.LiveRenderers`), otherwise
the live you stands on the field inside the replay. The charge is 3 s.

**Playing the ball (round 3b):** jumps and layouts are decided from where the
ball WILL be a jump's rise (0.28 s) from now — `PlayInstance.PlayTheBall` — for
the intended man, the nearest defender and any offensive man within 3 m. Over
his head (2.0–3.1 m, within 1.4 m sideways) = jump; landing short or wide
(1.4–2.9 m) with under half a second left = dive. The old rule only jumped a
man already standing within 2.5 m of the catch spot in a 0.1 s window, so a
receiver a step short let catchable balls sail over him.

**Controls while you are QB:** move and sprint as normal (Shift), scaled to
0.8 so the aliens can catch you. F is only the sideline button. LMB = charge /
throw. **Backspace** skips whatever is making you wait: a replay is cut, a
kickoff or punt ends as a touchback, a huddle breaks at once (one press each).
A tackle knocks the camera over the way the hit sent you (away from the
tackler), holds it down, and stands it back up as control returns
(`CameraTransformFX.TriggerKnockdown`). In third person the astronaut body
stays upright — only the view falls.

**How it is wired (nothing new in the sim):** `FootballHumanQB` (made by
`FootballMatch.Boot`, play mode only) owns the button, cylinder, aim visuals,
hotbar entry and input. `FootballMatch.PrepareScrimmage` hands the away QB slot
`HumanQBBrain` (`KeepsControlWhenCarrying`) and `PlayInstance.SetHuman`. The
slot is an ordinary `FootballPlayer` flagged `humanDriven`: its position,
facing and hand point are copied from the real player every step
(`SyncHuman`), it never integrates movement itself, and `BallHoldPoint()`
returns the player's hand point. The snap flies to that point and the catch
test is the same `TouchDistance` with a generous 1.6 m slack. Throws come out
through the normal `DoAction(Throw)` path (no wind-up for the human), so
receivers, DBs and the replay see a regular pass. The hotbar item is a
registry row (`Hotbar.ItemId.Football`) with a code-drawn icon.

**Traps hit while building it:**
- `PlayerPickup.holdPosition` in `Proto_Football.unity` is a scene-authored
  point 2 m *under* the turf and 3 m to the side. The first snap flew into the
  ground and the centre "scooped it up" every play. Hands are now a
  `FootballHold` anchor hung under the camera's view frame
  (`CameraTransformFX.ViewFrameOf`, the pistol's trick) — `handOffset` on the
  component moves the ball in the hands.
- A test driver that sets `transform.position` on the player does nothing (the
  controller re-applies the rigidbody). Teleport with `rb.position` +
  `Physics.SyncTransforms()` like `TutorialDirector` does.
- The jumbotron replay hides every live alien for its own render and turned
  them all back on afterwards — including the one hidden for the human slot, so
  an alien stood on top of the astronaut during every replay. `CollectLive`
  skips `humanDriven` players now.
- Nothing waits on a timer for you: the play sits at `Setup` until you reach
  the cylinder. Walking away from the cylinder is how you stall the game.

**🔥 BUILD trap (2026-09-20):** the first build had no jumbotrons. `Shader.Find`
for a built-in shader returns null in a player unless some material in the
build uses it or it is on Project Settings ▸ Graphics ▸ Always Included
Shaders; `new Material(null)` throws and the whole screen builder died in
`Start`. Every football material now goes through `FootballShader`
(null-safe, with stand-ins), and Unlit/Color, Standard and
UI/LensFlareAdditive were added to the always-included list. The build's log
(`%AppData%\..\LocalLow\DefaultCompany\Solar System 2\Player.log`) named the
line in one grep — read it before theorising about a build-only bug.

**Not yet verified by a human hand:** the throw charge / arc feel, the hotbar
icon, the ball's position in the hands, the knockdown, receivers catching your
throws. All of it needs Sam's playtest.

## Pass 5 (2026-09-20) — solid bodies

Sam after the Phase 2 playtest: "the aliens aren't solid, I can just walk
through them, and they can just walk through each other … make the football
game more physics oriented … blocking is still as shitty as ever." Spec:
`superpowers/specs/2026-09-20-football-solid-bodies-design.md`; plan:
`superpowers/plans/2026-09-20-football-solid-bodies.md`.

- **`FootballBodies.cs`**: every man is a disc (linemen 0.50 m / 1.25 mass,
  LB/S 0.45 / 1.10, skill 0.42 / 1.00; a downed man 0.55 and immovable). Once
  per step, after the brains and before the tackles (and every step of the
  dead ball), overlapping pairs separate (≤ 0.12 m a tick — never a pop),
  closing momentum is traded (no bounce) and a `BodyContact` list lands on
  `PlayView.contacts`. Solid in every phase.
- **Bodies slide** (`FootballPlayer.Tick`): a man runs along a body in his
  way instead of through it — except a tackler on the carrier, the carrier on
  a tackler, and an engaged blocker holding his ground. Wedged between two
  men he takes the tangent that goes round, or backs out.
- **Setup**: `MoveToBrain` steps round a man in the way after 0.08 s and
  settles a stride short of a spot somebody is already standing on (only a man
  who is truly on his own spot — two men "settled" on each other 16 m from
  their spots was a deadlock). Errands (fetch the ball, carry it to the
  centre) never settle. A **setup timeout now logs who is late and why**
  (`DebugSetupState`): today every one is a lineman jogging 60 m from the
  touchdown celebration to a kickoff.
- **Blocking / the pocket / tackles**: see the rules above. Gone:
  `BlockSlowdown`, the engagement timers and shed list, `_engagedScale`, the
  pocket clock and `DLBrain.free`, `TackleRadius` and every `MissedTackle*`
  dice, `BreakTackleChance`.
- **The human**: the slot is a disc; a shove reaches the rigidbody
  (`FootballHumanQB.shoveGain`); every alien carries a kinematic capsule on
  layer 10 so you can't walk through them (off while the slot is you).
- **F8 → Contact discs**: a ring per man at his disc edge (red touching,
  yellow blocking).
- **Poses**: the spine leans into a shove (`FootballAlienRig.lean`).
- **Contests**: a defender with his arms up (`IsReaching`) gets his full
  reach less `DefenderReachPenalty` (0.35 m) — with solid bodies no corner
  could otherwise get a hand on a ball in a receiver's hands.
- **Tools** (`tools/football/`): `ContactDrill` (the separation pass + the two
  formula tables), `LineProbe` (the pass rush tick by tick: move, who has
  him, when the pocket went), `CoverageProbe` (receiver vs corner to the
  catch, then the carrier and his two nearest chasers to the whistle). The
  soak summary carries contacts / engagements (rush won, hold won) /
  pushback / pocket life / wraps vs arm-tackles by angle / sidesteps / setup
  timeouts / max overlap. The analyzer asserts no standing pair overlaps.

**Balance, honestly (soak, 3 games, seeds 1000–1002):** 28–14, 28–49, 35–56 · 66–74 plays · 55–62 % completions · 9.6–15.7 yds/play · 3–5 INT · 0–1 sacks · pocket collapsed 6–8 times a game (avg 2.1–2.5 s) · 343–423 engagements (rush won ~60 %) · 40–46 wraps, 10–17 arm tackles · 2–8 contested · dead ball 21–24 s · setup timeouts 5–13, all kickoffs · max standing overlap 0 (analyzer). Pass 4 for comparison: 14–28 points, 4–7 sacks, 4–5 INT, ~19 s dead ball.
The physics is in and every diagnostic is green; the football balance is not
back to pass 4 (14–28 points) yet — the levers are listed in the memory note
and the probes above show exactly where a play goes. Two things are known and
cosmetic: men converging on the touchdown celebration briefly overlap and are
pushed apart over a few ticks, and a man standing up out of a pile slides out
over ~0.2 s.

## Numbers from the last headless soak (pass 2) (3 games, seeds 1000–1002)

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
- Phase 2 (the player at QB) is BUILT — see the Phase 2 section. Open: no
  audible / play choice for the human, no run-blocking read, the CPU defence
  does not know a human is slower to release.
