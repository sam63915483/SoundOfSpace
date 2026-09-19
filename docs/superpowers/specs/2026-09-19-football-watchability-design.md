# Alien Football — the "great to watch" pass (2026-09-19)

Design for the second pass over the Phase 1 prototype. Sam's brief, in his words:
"nobody wants to watch shitty cpus play football, they want to see a hell of a
game" — better than Football Fusion, close to Madden CPUs. Player-at-QB (Phase 2)
is explicitly *not* this pass. `docs/FOOTBALL.md` stays the live reference for
what exists; this file is the *why* for this pass.

## What Sam asked for

1. **A real snap.** A centre snaps the ball to the QB; the QB has to catch it
   with his hands, holds it in his hands, and the throw leaves his hand.
2. **Seven a side.** Add the centre to the offense and a safety to the defence.
3. **Hands and tucks.** Receivers catch with their hands, then tuck the ball in
   the right arm. Anyone running with the ball has it tucked (the QB too).
4. **Route variety.** Comebacks, outs, slants, posts, zig-zags, curls, double
   moves — not the same go route every snap.
5. **The QB extends plays.** Rollouts and scrambles far more often; on the run
   he looks for shots downfield or takes off.
6. **Madden-style dead ball.** A receiver who moves the chains emotes with the
   ball, jogs back and hands it to the centre. The defence emotes after a stop.
   A loose ball is picked up by the nearest offensive player and brought back.
7. **Open-field moves.** Carriers juke, sidestep and spin. When a defender
   dives, the carrier tries to hurdle; a clipped hurdle is a hard fall with a
   fumble chance.
8. Anything else that makes it read as a real, exciting game.

## Hard rules carried over (Sam)

- Nobody ever teleports. The **ball** now obeys this too — it is carried back
  to the line by a player after every play, never re-placed by code (one
  fallback: if it is still not there after a long timeout, "the officials spot
  the ball" and it is placed — logged so a soak shows it).
- Nothing freezes after a whistle.
- Catch = ball touches the arms; drops stay as they are.
- The sim is field-space and headless-testable. Every new behaviour must
  produce a counter in the soak summary so it can be seen without play mode.

## Design

### Seven a side

New roles `C` (centre) and `S` (safety); one new slot per team that plays C on
offense and S on defence. The centre lines up over the ball at the line and
blocks the nearest rusher after the snap. The safety plays deep middle: stays
deeper than the deepest receiver on his half, breaks on any ball he can reach,
comes up on runs after the read delay. Kickoff lineups grow to 6 + kicker and
6 + returner. Every place that switched on the role set (labels, eligibility,
speed multipliers, prefab picking, formations) is joined.

### The snap

Pre-snap the centre stands over the ball (crouched, hands on it); the QB
stands in the shotgun with his hands out. On the snap the ball is *launched*
from the ground to the QB's hands as a short flight (~0.4 s, flat) with the QB
as the intended receiver. The existing arm-touch catch test resolves it. A
small chance of a wild snap sends it wide; a snap the QB does not catch is a
live ball on the ground (a fumble scramble). The defence starts at the launch.
Catching the snap does **not** swap the QB's brain (unlike every other catch).

### Holding the ball

`HoldStyle` on the player, driven by PlayInstance: `TwoHands` (QB in the
pocket, ball between both hands at the chest), `Tucked` (everyone running with
it — right forearm wrapped over the ball against the ribs), `SnapStance` (the
centre over the ball). The ball's hold point comes from the pose, so it sits in
the hands, not floating at a fixed offset. Throwing already leaves the hand at
58% of the arm swing; unchanged.

### Routes and plays

A route library (go, slant, quick out, deep out, dig, curl, hitch, comeback,
post, corner, sluggo, out-and-up, zig-zag, wheel, drag, deep cross, flat, fade)
and ~14 play *concepts* that assign one route per receiver plus a read order,
plus four formations (spread, trips right, trips left, twins). Play selection is
weighted by down and distance and never repeats the previous call. Routes
carry a `settle` flag (curl/hitch/comeback end with the receiver stopped,
facing the QB) — the QB's openness test accepts a settled receiver, and the DB
reacts to a stopped man by driving on him after his reaction delay. Receivers
throttle into sharp cuts so a break reads as a plant, not an arc. Defensive
alignment mirrors the formation.

### The quarterback

Four styles: Quick 30% / Patient 30% / Deep shot 20% / **Designed rollout**
20%. On top of that a *scramble drill*: when the hold clock is nearly out and
nobody is open, the QB leaves the pocket to the open side and keeps reading
for another ~2 s; receivers whose routes are done work back toward his side.
On the run he still takes a 50/50 ball 10+ yards downfield, otherwise tucks it
and runs. Pressure rollouts stay.

### After the whistle

The next PlayInstance is built the instant the previous one ends; its Setup
phase now begins with **ball return**: whoever holds the ball (after his emote)
carries it to the centre (kicker on a kickoff) and hands it over; a ball on the
grass is fetched by the nearest player of the *new* offense. The centre walks it
to the line and places it; the snap cannot happen until the ball is there and he
is over it. The huddle/break/formation flow is unchanged. Emotes are timed poses
on the player (arms up, first-down signal, flex, chest thump, the "incomplete"
wave) triggered from the play result: first down or score → the carrier; sack,
pass breakup, interception, tackle for loss → the defender. Emoting players
stand still; nobody else waits for them.

### Open-field moves and dives

Defender: a tackle attempt from 1.6–2.8 m at closing speed becomes a **dive**
(~45% of attempts, never when blocked) — a 0.45 s committed lunge along a
predicted line; contact is tested during the middle of the dive; no contact →
he is on the ground as a miss. Standing lunges inside 1.5 m stay.

Carrier (`BallCarrierBrain`): with a defender 2–3.5 m away and closing, roll a
move on a cooldown — **juke** (hard lateral step away from his approach),
**spin** (0.5 s, body rotates a full turn, speed ×0.75) when he comes from the
side or behind, and **hurdle** when the defender is diving. Resolution: a
standing tackle on a juking carrier misses +30%, on a spinning one +35%; a dive
against a hurdling carrier is cleared unless *clipped* (25%): the carrier takes
a hard tumble (longer down time) and fumbles 35% of the time; a dive that
connects with a non-hurdling carrier is a tackle with a 5% fumble. Hurdling
into a standing defender is a normal tackle.

**Fumbles** make the ball live on the ground (bounce, roll). Whoever reaches it
first: 60% falls on it (dead there), 40% scoops and runs. A defensive recovery
is a turnover.

### Clock

Stops on incompletions, out of bounds, turnovers and scores; runs otherwise
(including through the dead-ball return). Quarter length raised so a game still
has ~40 plays.

## Testing

Headless soak (`tools/football/FootballSoak.cs.txt`) extended with counters:
snaps caught / wild, rollouts, scramble drills, jukes, spins, hurdles, clipped
hurdles, dives (hit / miss), fumbles (lost / kept), emotes, ball returns
completed vs officiated, plays per game, seconds per play. Acceptance is Sam
watching a drive from the stands.

## Out of scope

Player at QB, crowd, sound, punts/field goals, penalties, camera work, stadium
on Cyclops.
