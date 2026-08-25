# Playtest — Shuttle Autopilot Travel v1 (2026-08-25)

Branch `feat/helmet-hud`. Everything below is built + compile-checked; nothing
has been eyeballed in the Editor yet. Plan:
`docs/superpowers/plans/2026-08-25-shuttle-autopilot-travel.md`.

## One-time setup (before the first run)

1. Open Unity, let it compile, and run **Tools → Shuttle Travel → Add Interior
   Volume + Landing Lamp To Prefab**. It patches `Shuttle_Lander.prefab` in
   place (LoadPrefabContents — never a regen; idempotent; there's a matching
   Remove item). Then eyeball in the prefab:
   - `ShuttleInteriorVolume` — trigger box auto-sized from the Interior
     renderers. Resize so it covers the walkable cabin (it decides who counts
     as "aboard" at countdown 0). *The system works without it — a bounds
     fallback kicks in — but the real box is what you tune.*
   - `LandingLamp` — a small sphere perched on the ConsoleStand. Nudge it
     where you want the physical green/red lamp.
2. Commit the new `.meta` files Unity generates (`Assets/3 - Scripts/Shuttle/`,
   `Editor/ShuttleTravelSetup.cs`, `Music/ShuttleComputerNavUI.cs`,
   `Multiplayer/ShuttleSync.cs`, `prototypes/shuttle-autopilot/`).

## Debug controls (Editor / cheats only)

- **F6** while parked — fly a full leg to the next landable planet, no UI.
- **Alt+WASD / Alt+Q,E** during hover — steer without the NAV app.
- **Alt+Space** — land (only goes if the light is green).

## Phase 2 first — the rider spike (kill criterion)

The handoff's rule: if walking inside a moving shuttle isn't solid, nothing
else matters. Stand in the shuttle, press **F6**, and during the whole flight:

- [ ] Walk, sprint, jump, look around — no sinking through the floor, no
      jitter against the walls, no camera judder.
- [ ] Jump repeatedly during liftoff and mid-transit (the fastest phase).
- [ ] Open the computer, play TRAX, open the locker mid-flight.
- [ ] Drop a held item mid-flight — it should freeze onto the cabin floor,
      and thaw as a normal pickup after landing.
- [ ] Origin-rebase soak: select the EndlessManager in the scene, set
      `distanceThreshold` to ~100, fly a leg — no pops or teleports.
- [ ] On landing: door reopens, walk out, normal gravity, no fall damage, no
      camera snap (the up-blend eases over 1.5 s).

If any of that fails, stop here and tell Claude what it looked like.

## Full loop (NAV app)

- [ ] ConsoleScreen → F → **NAV** tile (new, next to TRAX) → planet grid shows
      every planet, current one greyed "YOU ARE HERE".
- [ ] Select a planet → TRAVEL lights up → click → "TAKING OFF IN 10".
- [ ] Walk OUT during the countdown → shuttle leaves without you (D-1), and
      you watch it go.
- [ ] Countdown with NOBODY inside → "NO CREW ABOARD — LAUNCH CANCELLED",
      back to the list, selection kept.
- [ ] Ride a leg: countdown → liftoff → "EN ROUTE TO X" + progress bar →
      hover with the live downward feed, crosshair, ALT readout.
- [ ] Hover: WASD slides (heavy, ~15 m/s), Q/E yaws, altitude holds ~100 m
      over hills, holds last altitude over holes.
- [ ] Validity: fly over water / Tev's house / trees / an NPC / a steep hill —
      border + console lamp red, SPACE just flashes "NO CLEAR GROUND".
      Flat field — green, SPACE sets it down soft, door opens, NAV shows the
      new YOU ARE HERE.
- [ ] Four legs: Humble Abode → Cyclops → the twins → back. No drift — the
      shuttle should land exactly where the hover left it.
- [ ] ESC in NAV steps back to the desktop; the hover keeps holding. Reopen —
      feed's still live. F closes the computer entirely mid-hover; same deal.

## Save round-trip

- [ ] Park on another planet, save at the pod (note the valve refuses while
      flying), quit to menu, load → shuttle parked on that planet at the same
      spot, door open, NAV list correct. Player standing inside it.
- [ ] Load a PRE-travel save → shuttle on Humble Abode as always (empty
      bodyName = leave the scene pose).

## Co-op (second machine)

- [ ] Guest rides a full leg: same countdown number, same screen through the
      world monitor, same green/red, both walk out on the new planet.
- [ ] Guest opens NAV first during hover → guest steers (host sees
      "PILOT: <name>"); guest closes/disconnects → stick frees after ~0.5 s.
- [ ] Guest-side puppet of the host walks around the cabin smoothly mid-flight
      (frame switch — watch for a snap at liftoff/landing, one is expected at
      the phase edge, none during).
- [ ] Known edge (punt): joining the session while the shuttle is mid-flight
      seats the joiner in a pod that isn't where the world thinks — don't
      join mid-flight this build.

## Known rough edges (v1, by design)

- Countdown has no tick sound; hover/landing have no engine audio ([AUTHOR] —
  strings and sounds are yours).
- Arrival point is "the side of the planet facing where you came from" — hover
  and reposition if it's ugly.
- Landing camera runs at CCTV 15 fps (that's the intro's dust-safe recipe, and
  honestly it reads right).
