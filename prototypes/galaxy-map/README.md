# GALAXY MAP — prototype

> **PORTED 2026-09-16** to `Assets/3 - Scripts/Music/ShuttleComputerTutorialNavUI.cs`,
> after Sam signed this off. The C# draws through `NavMapGraphic` — the same
> one-mesh renderer the real NAV map uses — so it matches the gameplay map's
> look; only the camera and the catalogue are new. The numbers below
> (LightYearAU 475, resolve at 26 px, fade over 80, zoom 0.02–4000, start at
> 2200) are mirrored exactly in that file. **Change one, change the other.**

**http://localhost:8091/** — double-click `serve.bat` (or `py -3 -m http.server 8091` here).

The shuttle computer's NAV screen, rebuilt as **one continuous zoom** instead of
the discrete levels I shipped first. Same palette and screen size as
`ShuttleComputerUI.cs`, same approach `prototypes/nav-map` used before it was
ported to UGUI.

Sam's brief, 2026-09-16:

> the map should consist of 7 solar systems, each with a sun and planets
> revolving around it … in the tutorial youll be looking at earth, you can zoom
> out and see the other planets revolving around the sun, if you keep zooming
> out youll just see the sun and then name of what system that sun is, so you
> can zoom out and see konkebular and start zooming into it and see the planets
> rotating around the sun and click humble abode and travel to it.

## What to try

It opens on **Earth**, filling the screen, exactly where the tutorial starts.

1. **Wheel back.** Earth shrinks, its orbit appears, then Venus and Mars, then
   the outer planets — all of them actually moving.
2. **Keep going.** Sol's orbits fade out and the name `Sol` fades in under the
   sun. That crossover is the whole trick: there is always exactly one label
   saying where you are.
3. **Keep going.** Six more suns, each named. `Konkebular-7` is ringed.
4. **Click its sun** (or double-click) to fly to it, or just wheel in on it.
   Its planets resolve — Humble Abode ringed in magenta.
5. **Click Humble Abode → TRAVEL.**

Buttons: **EARTH** and **ALL STARS** jump to either end so you never have to
wheel the whole way. **PAUSE** and the orbit-speed slider are prototype-only.

**RULES: TUTORIAL / GAMEPLAY** is the one switch worth arguing about. Tutorial =
Humble Abode only. Gameplay = anything inside Konkebular-7, nothing outside it
("the warp drive is dry; the reactor only moves you planet to planet"). Both
answers come from `canTravel()` and nothing else, so the two modes differ by a
string when this is ported.

## The decisions I want you to check

- **How fast the wheel zooms.** Earth → all stars is ~21 notches now.
- **When a system stops being a place and becomes a dot.** Currently its
  outermost orbit crossing ~26 px, fading over the next 80.
- **How fast the planets orbit.** 24 days/second by default — fast enough to
  read as moving, slow enough not to look silly.
- **The made-up systems and names** — Hab-9, Ordal Reach, Vess, Tharn Belt,
  Bright Fold. All placeholders.
- **The scale.** `lightYearInAU` is deliberately compressed to 475 (a real one
  is 63,241). At true scale the wheel takes ~97 turns to cross that range, which
  reads as a broken control rather than a big galaxy. Everything still resolves
  in the right order; it is a map, not an ephemeris.

## Porting notes

At the bottom of `galaxy.js`. The short version: the camera is
`(centre in AU, pixelsPerAU)`, zoom is multiplicative about the cursor, and ONE
rule — `outermostOrbit * 2 * pixelsPerAU` — decides whether a system is a dot or
a place. Planet positions are `cos/sin(phase + t/period)`, the same circular
rails the game's real planets ride, so none of this has to be undone later.
