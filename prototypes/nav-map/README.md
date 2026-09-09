# NAV solar map — browser prototype

A live 2D map of the real solar system, built to replace the NAV app's grid of
planet tiles on the shuttle computer.

**Run it:** double-click `serve.bat` (or `py -3 -m http.server 8090` in this
folder) → http://localhost:8090/. It has to be served over http — `fetch` will
not read `system.json` from `file://`.

The page draws itself at **1500 × 940**, which is the real size of the shuttle
computer screen (`ShuttleComputerUI.ScreenW/H`), and scales that box to your
window. What you see is what the in-game monitor would show. The strip along the
bottom of the browser window is **prototype-only** and would not exist in game.

---

## Why this instead of the tile list

The current NAV screen is a grid of names with a distance under each, and a line
that says `IN RANGE ~4 MIN` when a planet is out of reach. Every number in it is
correct, but the player has to hold the whole solar system in their head to use
it: which planets are near which, why Bruise is 16 km away today and 5 km away
later, whether waiting nine minutes is better than paying for a longer hop.

The map answers all of that by drawing it:

- **The magenta dashed circle is your tank.** Its radius is exactly
  `ShuttleFuel.RangeKm` in world metres. Everything inside it is somewhere you
  can go right now; everything outside has to come to you. Drain the tank and
  you watch the circle shrink.
- **Selecting a world you cannot reach lights the stretch of its orbit that IS
  inside range, in amber**, and puts a ghost of both planets — and of your range
  circle — where they will be when the window opens. "In range in 8:27" stops
  being a number and becomes a picture of two planets swinging together.
- **The list on the right is the old NAV screen**, kept as it was: every world,
  one number each — kilometres if you can go, `opens 7:25` if you cannot.

## What is real in here

Everything except the planet colours and one-line blurbs.

| Thing | Where it comes from |
|---|---|
| Orbit radius, start angle, day length, body radius, gravity, moons | `Assets/1.6.7.7.7.unity`, read by `extract_system.py` |
| Humble Abode running retrograde, water / fish market flags | `docs/DISTANCE_TABLE.md` |
| Jump cost, range, the 1.6 cost exponent, 15 km max hop | `ShuttleFuel.cs` (`CostForMetres` / `RangeKm`, ported line for line) |
| In range / opens in / stays open for / closest they ever get | the same closed form as `World/OrbitRange.cs` |
| 10-second launch countdown, SKIP | `ShuttleAutopilot` |
| Palette | `ShuttleComputerUI.cs` (`Ink`, `Accent`, `Grid`, `Warn`…) |

Because every planet rides an exact circular rail in one plane, none of this is
simulated step by step — a position is a sine and a cosine of the clock, and
"when does that come into range" is one arccos and one divide. That is why the
countdowns can be promised and be right, and why scrubbing an hour ahead costs
nothing.

Re-run `py -3 extract_system.py` after moving any planet, adding a dwarf, or
changing a day length.

## Controls

| | |
|---|---|
| Scroll | zoom (about the cursor) |
| Drag | pan |
| Click a planet, or a row in the list | select it |
| `MY RANGE` / `WHOLE SYSTEM` | framing presets |
| `Esc` | clear the selection |

Only the world you point at shows a distance on the map. The rest are names —
that is what keeps the picture readable.

Prototype-only strip: tank slider, clock multiplier (×1 is real time — a day on
the twins is 10 minutes), MOVE SHUTTLE to pretend you are parked somewhere else,
FILL TANK.

## Shareable / test URLs

`?here=Hearth&sel=Cyclops&fuel=100&fit=all&t=300&project=600`

| param | |
|---|---|
| `here` | planet the shuttle is parked on (default Icey Twin, the new-game start) |
| `sel` | pre-selected destination |
| `fuel` | tank in units, 0–100 |
| `t` | seconds to advance the whole system before drawing |
| `fit` | `range` / `inner` / `all` |
| `project` | run the whole system N seconds ahead (the orbit scrubber, minus its control) |
| `fly` | start the launch countdown immediately |
| `sim` | run N seconds of the flight forward before the first frame (screenshots) |

## Not built here

This prototype covers the **PARKED** state — the planet list and the TRAVEL
press, which is the part that needed replacing. The autopilot's other screens
(the live camera feed en route, and the hover/landing view with the crosshair
and WASD positioning) are untouched and would keep working exactly as they do
now; the countdown and transit panels here are stand-ins so the loop can be
clicked end to end.
