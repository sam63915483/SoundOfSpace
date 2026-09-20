# Football test harness (editor scripts, NOT compiled into the project)

These are `.cs.txt` so Unity ignores them. To use one: copy it somewhere outside
`Assets/` (the session scratchpad), replace `<scratchpad>/` with a real folder,
and run it with the Coplay MCP `execute_script` tool (or any "run an editor
script" mechanism). None of them enter play mode — Sam runs every playtest.

- `FootballSoak.cs.txt` — builds a DontSave FieldRoot + FootballMatch in edit
  mode and plays 3 full games with `FootballMatch.Simulate`, capturing the
  play-by-play to `soak.txt`. The FINAL lines carry `GameStats` (moves, dives,
  fumbles, snaps, rollouts, emotes, dead-ball time, officials). A
  `probe.txt.force` file next to it containing a play name forces that play
  every snap. ~1 s for 3 games.
- `FootballProbe.cs.txt` — tick-by-tick trace (carrier, nearest opponent,
  catches, interceptions) of the first N plays to `probe.txt`.
- `KickProbe.cs.txt` — one kickoff tick by tick (ball, returner) to `kick.txt`.
- `CatchProbe.cs.txt` — after every completed catch, the gap to the nearest
  standing defender every 0.25 s until the play ends (`catch.txt`). Use this
  for "why is every catch a touchdown".
- `FootballAnalyzer.cs.txt` — THE WATCHER: one full game with every man and the
  ball sampled every tick and the rigs driven, reporting teleports, facing and
  joint snaps, overlaps, frozen men, ball-vs-hand mismatches, long plays, and
  a pass-by-pass diagnosis (caught / dropped / defended / off target / picked,
  by depth) to `analysis.txt`. Takes ~90 s (the execute_script call times out
  at 60 s; the file still lands — poll for it).
- `ContactDrill.cs.txt` — solid bodies (pass 5): two discs pushed together
  come out separated with one contact and the momentum traded; a downed man
  is immovable; tables of `BlockDrive` (pass rush × blocking) and `TackleHit`
  (closing × angle) against the wrap threshold. Instant.
- `LineProbe.cs.txt` — the pass rush tick by tick for the first 14 plays:
  each rusher's move (bull / SWIM), who has him, his speed scale, distance to
  the QB, when the pocket collapsed, how the play ended (`line.txt`). Use it
  for "they get round the line too fast / never".
- `CoverageProbe.cs.txt` — receiver vs corner every 0.2 s to the catch, then
  the carrier and his two nearest chasers (down / diving / stumbling /
  wrapping) to the whistle, plus that play's log lines (`coverage.txt`). Use
  it for "every catch is a 75-yard touchdown".
- `PoseCheck.cs.txt` — instantiates 14 aliens in rig states (idle, run, reach,
  throw, carry, kick, two hands, snap stance, ready, hurdle, dive, arms up,
  flex, first down), calls the rig's LateUpdate by reflection, draws the ball at
  its hold point, renders PNGs.

See `docs/FOOTBALL.md`. Trap: an unfocused editor compiles but doesn't reload
the domain — check the DLL timestamp before trusting a soak.
