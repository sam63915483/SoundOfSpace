# Alien Football — solid bodies, contact blocking, contact tackling (2026-09-20)

Design for the third pass over the football prototype. Sam's brief, after the
Phase 2 playtest: "the aliens aren't solid, I can just walk through them, and
they can just walk through each other which makes it look really bad … give them
their actual colliders and make the football game more physics oriented …
blocking is still as shitty as ever and they get around the o line too fast …
take this from feeling like a gimmick to a Madden rival." Sam chose the
**solid-bodies-in-the-sim** route over full Unity physics. `docs/FOOTBALL.md`
stays the live reference for what exists; this file is the *why* for this pass.

## The problem, stated plainly

The sim is fourteen (x, z) points on a plane. Every alien is moved by hand each
tick and the 3D body is a puppet drawn over it. Every collider on the alien model
is stripped at spawn. The only "contact" in the whole game is one nudge that
holds an engaged rusher a metre off his blocker.

Blocking, tackles and catches are all distance thresholds plus dice:

- A block: a defender within 1.6 m of a blocker who is roughly between him and
  the ball is held to 0.12–0.38× speed for a 1–3 s timer, then "shed" and that
  blocker can never hold him again. The pocket timer frees one rusher who
  ignores every block. The engagement ends the instant the rusher's angle puts
  him "past" the lineman, and the lineman never steps to stay in front.
- A tackle: within 1.5 m, roll for a miss (15 %, +22 % mid-juke, +22 % mid-spin).
- The headless analyzer counted engineering faults (teleports, joint snaps,
  overlaps). It cannot see that a block looks fake, because there are no blocks.

That is a stats-and-dice sim with animated bodies laid over it, and it is why it
feels like a gimmick.

## What this pass builds

Three systems are replaced. Everything else — every brain, route, the rig, the
replay, the analytic ball, the huddle / dead-ball choreography, the human QB
wiring — stays as it is.

| Replaced | By |
|---|---|
| `PlayInstance.BlockSlowdown` (engagement timers, shed list, `_engagedScale`, `EngageStart/Keep`, the `BlockContact` nudge) | contact-derived engagements resolved as a shoving match |
| the pocket timer (`_pocketLife`, `DLBrain.free`, "Pressure — X breaks free") | the pocket collapses when a lineman loses |
| `TickTackles` radius + miss dice | contact-started tackles decided by momentum and hit angle |

### 1. Solid bodies (`FootballBodies.cs`, new)

Every `FootballPlayer` gets a **disc** on the field plane:

| Role | Radius | Mass |
|---|---|---|
| OL / DL / C (as a lineman) | 0.50 m | 1.25 |
| LB / S | 0.45 m | 1.10 |
| QB / WR / DB | 0.42 m | 1.00 |
| human QB slot | 0.42 m | 1.00 |
| any man on the ground | 0.55 m | immovable |

Every sim step, **after** the brains have moved everyone and **before** tackles
are judged, a pass over all pairs (91 for 14 men; trivial) finds overlapping
discs and:

1. **Separates** them along the line between centres, each man moving in
   proportion to the other's share of the combined mass (the heavier man moves
   less). Never more than the overlap; no teleports.
2. **Trades momentum** along that line: the closing component of relative
   velocity is exchanged with a restitution of 0 (bodies do not bounce apart),
   so running into a man slows you and shoves him.
3. **Records a contact** `(a, b, normal, closingSpeed, overlap)` for this tick.
   The blocking and tackling rules read this list; nothing else in the sim
   tests distances for "touching" any more.

Bodies are solid in every phase — huddle, lineup, dead ball — not only while
the play is live. Setup steering gains a **sidestep**: a man blocked by a body
between him and his spot for more than 0.3 s steers around it. `SetupTimeout`
(16 s) stays as the backstop and the soak counts every setup that needed it.

A hurdling carrier's disc is **lifted** for the middle of the hurdle so a diver
passes under it (contact with a diving body is ignored while over him; the clip
timing rule is unchanged). A diving man's disc slides at his dive speed.

**The human.** The human slot's disc takes part like any other. When the pass
moves the human disc, the displacement is applied to the real player's rigidbody
as a velocity change (`FootballHumanQB.Shove(worldDeltaV)`), so a defender
running into you moves you. Each alien also gets a **kinematic `Rigidbody` +
`CapsuleCollider`** (height 2 m, radius = disc radius, layer = the player's
walkable layer) so your body cannot walk through them. The replay ghosts get
none. These colliders exist only for the player's body; the sim never reads
them.

**Debug.** F8 gains "contact discs": a gizmo ring per man (red while in contact)
and a short line for the push force.

### 2. Blocking is a shoving match

**Engagement = contact.** A blocker and an opponent who are in contact, both
standing, with the opponent on the carrier's opposite team, are *engaged* for
that tick. No timers, no shed list. `FootballPlayer.blocking` (the rig's
arms-out flag) is set from contact on both men.

**Two forces.** In an engagement each man drives along his own intent:

- the rusher pushes toward the ball with `push = PushBase × (0.6 + 0.8 × passRush) × mass × swing`
- the blocker pushes back with `hold = PushBase × (0.6 + 0.8 × blocking) × mass × swing`
- `swing` is a per-snap random factor in 0.8–1.2 rolled per man in the play's
  constructor (as `DBBrain` already does), so no two reps look alike.

The pair moves along the rusher's drive direction at a speed proportional to
`push − hold` (clamped to ±1.2 m/s). A stronger rusher bull-rushes the tackle
back into the QB slowly; a stronger blocker holds him flat. While engaged, both
men's `speedScale` is the resolved drive speed over their max, not a constant.

**Pass-set (`OLBrain`, `CenterBrain`).** The blocker's spot each tick is the
point on the segment rusher → ball, one radius-sum in front of the rusher: he
mirrors. His lateral speed is capped at `LateralCap` (0.55 × max for linemen,
0.75 × for the centre, 0.85 × for anyone else) — linemen are slow sideways.
He never chases a man who is past him toward the ball at more than 0.5 m; he
turns and pursues like today. The kick-slide pocket point stays for the first
0.5 s so the cup still forms.

**Rush moves (`DLBrain`).** Until contact the rusher runs his edge path as
today. In contact he picks, per snap: **bull** (drive straight, the force rule
above) or **swim** (step laterally at `SwimSpeed` = 0.7 × max toward the side
with the shorter line to the QB, still pushing at half strength). The swim wins
the edge only if his lateral speed beats the blocker's `LateralCap` for long
enough to clear the disc — so an edge rusher beats a tackle, a tackle does not
beat a centre. The pick is weighted by `speed` (swim) vs `passRush` (bull).
Once his disc is past the blocker's along the line to the ball, the
engagement is over by geometry and he pursues.

**The pocket.** `view.pocketCollapsed` becomes: any standing rusher with a clear
line to the QB (no offensive disc within 1.2 m of the segment) inside 3.5 m, or
any engaged blocker driven to within 1.5 m of the QB. The QB brain reads it as
before. The `DLBrain.free` field and the pocket-timer log line are deleted.

**Receivers blocking downfield** use exactly the same rules: a light body with
a weak push loses the shove fast, and a DB's lateral speed clears him. The
special 0.45–0.9 s shove is gone.

**Jams** (`PressReleases`, `_slowUntil`) stay — they are a press-coverage
technique at the snap, not a block.

### 3. Tackling is contact

A tackle attempt begins when a defender's disc **touches** the carrier's (a
contact record with the carrier), or a diving defender's disc touches him during
the dive's contact window. No radius. `TackleRadius`, `DiveReach`,
`MissedTackle*`, `MaxMissChance`, `_tackleRetry` (as a radius retry) go.

On contact the **hit quality** is judged:

- `closing` = closing speed along the contact normal (m/s).
- `square` = how head-on the tackler is to the carrier's line of motion: the
  dot of the contact normal with the carrier's velocity direction (1 = met in
  the chest / from straight behind, 0 = glancing).
- `hit = closing × (0.35 + 0.65 × square) × tacklerMass`.

Outcomes:

- **Wrap** when `hit ≥ WrapThreshold` (target: a defender arriving at 4+ m/s
  square, or 2.5 m/s at a slight angle, wraps). This is the existing
  `EndTackle` / `TickWrap` / `FinishTackle` — drag for a stride, pile-on,
  forward progress — unchanged except: pile-on joins on **contact** with the
  wrap, and **breaking** it compares the carrier's momentum (`mass × speed ×
  (0.7 + 0.6 × team.speed)`) against the wrapper's hit with a ±15 % swing,
  once, at the wrap's midpoint, only against a lone wrapper.
- **Arm tackle** when `hit < WrapThreshold`: the carrier `StartStumble`s and the
  defender `FallDown`s forward past him for `MissDownSeconds`. This is the
  missed tackle. A juke or spin now produces it *because the body moved* and
  the contact became glancing or late, not because the state added a
  percentage. The stiff-arm stays as an explicit move: contact on the stiff-arm
  side shoves the defender back 0.5 m and lowers `square` to 0.
- **A blocked defender** (engaged this tick) who touches the carrier can only
  arm-tackle.
- **Dives**: the trigger (a free man closing at 2.5+ m/s from 1.7–2.7 m, 45 %)
  is unchanged; a dive connects only through a contact record inside its
  contact window (dive phase 0.3–0.85). A connecting dive is judged like any
  hit with `square` computed against the dive direction. Hurdles: unchanged
  clip rule; a clean hurdle has no contact because the disc is lifted.
- **Fumbles**: the big-hit fumble roll is scaled by `hit` (a harder hit is
  likelier to jar it loose) with the same base chances as today.

**Poses.** The rig gets a `lean` vector (field space): the spine leans into the
push direction in proportion to the engagement's force, so a block reads as a
shove. Engaged arms-out (exists), stumble (exists), fall (exists).

**The human QB** goes through the same path: a defender must touch him to
tackle him, and a sack plants him as today.

## Stage 4 (after Sam's playtest of 1–3; not in this build)

Batted balls: a pass leaving the hand within 0.35 s whose parabola passes
through a standing rusher's disc with a reaching arm (within 2.2 m of the QB) is
a live tipped ball (existing tip mechanics). A loose ball rolling into a
standing disc bounces off it.

## Verification without play mode

- **`tools/football/ContactDrill.cs.txt`** (new, edit-mode script): 100 reps
  each of lineman-vs-rusher and tackler-vs-runner across the stat range,
  printing win rates, time-to-beat and pushback distance. Targets at even
  stats: the rusher wins the edge or drives the blocker into the QB within
  2.5–4.0 s on ~35 % of reps; a square tackle at speed wraps ~85 %; a glancing
  contact wraps ~15 %.
- **`FootballSoak`** gains counters: contacts per play, engagements won by each
  side, mean pocket life, mean pushback, wraps / arm tackles / broken wraps,
  hit angle histogram, setups that needed the sidestep, setups that hit the
  timeout, and the largest disc overlap seen in a tick (must be ~0 after
  separation).
- **`FootballAnalyzer`'s overlap check** becomes an assertion: two standing
  discs overlapping by more than 5 cm after the separation pass is a bug.
- Sacks per game, INTs, yards per play and the score line should stay in the
  range of the pass-4 soak (4–7 sacks, ~5 INT, 14–28 points); this pass
  changes *how* those happen, not how often.

## Delivery

One batch: bodies + blocking + tackling, soak-verified, then Sam's playtest.
Build is on `feat/helmet-hud` (the current branch) as with the rest of the
football work. `docs/FOOTBALL.md` gets a "Pass 5 — solid bodies" section and the
rules-of-the-sim paragraphs for blocking and tackles are rewritten.

## Not in this pass

Full Unity rigidbody players (rejected: bouncy body contact, every brain
rewritten as a force controller, floating-origin + planet gravity on Cyclops).
Downed bodies that can be stepped over. Ragdoll falls. A crowd, sound, camera
work. Press-release moves at the line. Stage 4 above.
