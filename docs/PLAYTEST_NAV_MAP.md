🟢 ACTIVE — playtest checklist, 2026-09-09

# Playtest — NAV solar map

The NAV app's grid of planet tiles is now a live 2D map of the solar system.
Browser original (approved 2026-09-09): `prototypes/nav-map/` — `serve.bat`,
then http://localhost:8090. The Unity screen is the same design.

**Compile: PASS, 0 warnings** on all three assemblies. Not play-tested — that is
this document.

**Two new script files**, so Unity has to import them and make their `.meta`
before anything shows up: `Assets/3 - Scripts/Music/NavMapGraphic.cs` and
`Assets/3 - Scripts/Music/ShuttleComputerNavMapUI.cs`. Nothing in the scene
changed — the whole screen is still built in code at runtime.

---

## Get to it

Sit in the shuttle, open the computer, press **NAV**. That is the whole setup;
the map is the PARKED screen.

## What you should see

- Sun in the middle, an orbit ring per planet, planets on their rings where they
  actually are right now. A **magenta dashed circle** around the planet you are
  parked on: that is your tank. Its caption says `JUMP RANGE x.x KM`.
- Planets inside the circle are bright with a thin mint ring; planets outside are
  dimmed. Your own planet has a dashed mint ring and `SHUTTLE` under it.
- Right column: every world, one number each — km if you can reach it,
  `opens m:ss` if you cannot, `PARKED` for the one you are on. Header counts how
  many are in range.
- Bottom right: the destination block, empty until you pick something.

## Walk through these

1. **Click a planet on the map.** It gets a magenta ring, the row highlights, the
   destination block fills in (name, `IN RANGE`, distance, fuel cost), and
   TRAVEL lights up. Only the planet you point at shows a distance on the map.
2. **Click a row in the list instead.** Same result — the two are the same
   selection.
3. **Pick something you cannot afford.** Status goes amber, `IN RANGE IN m:ss`,
   the fuel number goes amber, and the button says NOT ENOUGH FUEL and does
   nothing. On the map, the stretch of that planet's orbit that IS inside range
   lights up amber, and a faint ghost shows where both worlds — and your range
   circle — will be when the window opens, labelled `WINDOW OPENS m:ss`.
4. **Watch that countdown for a minute.** It should tick down smoothly and the
   planets should visibly creep along their rings (a day on the twins is ten
   minutes, so this is slow but not still).
5. **Scroll wheel** over the map zooms like a magnifier — whatever is in the
   middle of the map stays in the middle. **Drag** pans. A click that does not
   move selects. `MY RANGE` and `WHOLE SYSTEM` reframe.
6. **Press TRAVEL.** Countdown, SKIP, transit, hover, land — all unchanged from
   before; only the parked screen was replaced. On touchdown, come back to NAV:
   **the map should now be centred on the planet you landed on**, with the range
   circle smaller because the tank is lower.
7. **Relocate** — select the planet you are already on. Block should say
   `YOU ARE HERE · RELOCATE`, distance `HERE`, fuel = the flat launch charge.
8. **Feed the reactor a crystal or two and reopen NAV.** The range circle should
   visibly grow and more planets should light up.

## Fixed after playtest 1 (2026-09-09)

Both of Sam's findings — planets on the map could not be clicked, and zooming
"zoomed in weirdly" — were **one bug**. `ScreenPointToLocalPointInRectangle`
answers relative to a rect's PIVOT, and the map rect is pivoted on its left
edge while everything drawn on it is centred, so every pointer reading was half
a map-width (515 px) to the right of the truth. Picking therefore missed every
planet, and zoom-to-cursor was magnifying a point out in the margin.

Pointer maths now goes through the centred clip rect and subtracts
`rect.center`, so it is right whatever the pivots are. Zoom is now a plain
magnifier (centre stays centre) because that is what Sam asked for and it is
also the sane behaviour under a pad's virtual cursor. Planet hit targets were
widened to at least 20 px — they are 5-pixel dots and they move.

## Things I would look at hardest

- **Anything drawn outside the map box.** Orbit rings are huge circles; they are
  clipped by a mask, and if that is not working you will see rings or planet
  names on top of the destination list.
- **The twins.** They sit 1 km apart on the same rail, so their two labels have
  to dodge each other — names should never overlap or sit on top of a planet.
  Anything nudged clear gets a thin leader line back to its dot.
- **Frame rate with NAV open**, especially zoomed right in. The map redraws 30
  times a second; if it costs anything noticeable, say so and I will drop it to
  15 and cull harder.
- **The cockpit monitor** while someone else is using the terminal — it mirrors
  this screen, and it should show the map, not a frozen or blank pane.
- **Controller.** The virtual cursor should point and click planets, rows and
  buttons. Zoom and pan are wheel/drag verbs — on a pad there is nothing bound
  for them yet, which is the open question below.

## Known, on purpose

- **The list does not re-sort by distance.** Fixed order, sun outward, so rows
  never jump under your cursor mid-click. Tell me if you would rather it sorted.
- **Planet sizes are not to scale** — a 30 m dwarf and a 500 m Cyclops would be
  invisible and a blob respectively. Distances and orbits ARE exact.
- **Planet colours are a table in `ShuttleComputerNavMapUI.cs`** (`NavSwatch`),
  not read from the planets themselves — the shading is a whole asset of
  gradients with no single colour to ask for. Easy to retune.
- **No pad binding for zoom/pan yet.** If the presets are enough, we leave it;
  if not, right stick pans and triggers zoom is the obvious mapping.
- The orbit scrubber from the prototype is not here — you cut it as one gadget
  too many. The code path still exists behind a URL parameter in the browser
  version if you ever want it back.
