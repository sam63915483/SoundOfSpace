🟢 ACTIVE — 2026-09-14 — Tutorial box phase 1 playtest checklist (spec: superpowers/specs/2026-09-14-tutorial-box-design.md)

# Playtest: tutorial box, phase 1

Two ways in. Both should behave the same.

- **Editor:** `Tools ▸ Solar System ▸ Open Tutorial Scene`, press Play.
- **Main menu:** MainMenu scene → Play → **TUTORIAL** (third row, under MULTIPLAYER).

## What should happen

1. You are standing in the stasis pod. No black screen, no "open your eyes".
2. The shuttle is already descending — engines lit, the box walls sliding past the pod glass.
3. After ~1 s the pod door opens. You can look and walk around the cabin.
4. ~12 s in, the shuttle stops at the 100 m hover. The cockpit monitor is on the NAV
   landing feed and a hint pill says walk to the computer and press F.
5. F at the console → WASD slides the shuttle over the floor, Q/E turns it, SPACE lands
   it when the border reads CLEAR. It sets down, the ramp opens, the hint disappears.
6. Walk out onto the green slab. The four walls and ceiling rain green 0s and 1s and you
   can see the Milky Way through them. You can't walk or jetpack out of the box.
7. ESC → pause menu (no MULTIPLAYER row) → MAIN MENU returns to the menu cleanly, no
   HUD left over.
8. Then START GAME → New Game, and separately a Load: both exactly as before.
9. `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\` has **no new file** after
   a tutorial run — even if you press the pod valve and seal yourself in (that plays
   the DOWNLOADING animation only).

## Round 2 (2026-09-14) — what changed since your first run

- Crosshair back; compass / boost / vitals in their helmet-pod layout; camera effects on.
- Real sun 25 km up and off to the side (shadows + flare through the glass). The stuck
  flare was a real bug (now fixed in the game too).
- Gravity = Humble Abode's (8 m/s²) via the simulation; suit oxygen on the slab ~55 %.
- Grass streams around you; ~25 trees, 6 crystals, 10 mushrooms, 5 cats. Chop / mine /
  harvest / pet should all work. The landing spot at the centre is kept clear (22 m).
- Digits ~3× smaller. Top-left shuttle text gone (F12 in a flight brings it back).

## Things to look at / tell me

- Does the 12 s descent feel right? (`Tutorial Director ▸ Descent Seconds`.) The shuttle
  does the real intro's nose-down / flip / retro-burn choreography, compressed.
- Start height (`Depart Altitude`, 185 m): does any part of the hull poke through the
  ceiling at the start?
- Digit walls: colour, speed, density, brightness are sliders on
  `Assets/4 - Scenes/TutorialDigitRain.mat`. Say what you want changed.
- Lighting: there is no atmosphere post here, so shadow sides are lit only by a dim
  ambient. Too dark / too flat? Sun angle is a constant in the builder (55° up).
- Prop density / clearing size are fields on `Tutorial Ground ▸ Tutorial Props`; cat
  count on `--- Managers --- ▸ CatSpawner ▸ Max Cats`.
- The tutorial player is the bare Player prefab: no rod, axe, pistol, grapple, or
  flashlight yet. That's phase 2 (needs a TutorialPlayer prefab).

## Known limits

- Rebuilding the scene (`Build Tutorial Scene`) wipes anything placed by hand in it.
- Build (exe) check still needed: TUTORIAL from the built game (CLAUDE.md trap #1).
