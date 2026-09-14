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

## Round 3 (2026-09-14) — after the 5 fps run

- **5 fps root cause:** the firefly spawner. It idles in a scene with no planets (round 1)
  but with the fake 10 km planet present it scanned 1.5 million cells three times a second
  looking for night-time spots. It is now quiet in the tutorial (the sun never sets there).
- **Crosshair size** is now a knob: `--- UI --- ▸ Overlay Canvas ▸ Dot ▸ Crosshair Reticle ▸
  Scale`. Set to 4 (was 12, the gameplay scene's value). Tell me the number that looks right
  and I'll bake it into the builder.
- **Loading transition:** the bar reaches 100 %, the black screen holds for 0.5 s over the
  live scene, then fades out over 1 s. The shuttle's engines light as the fade starts, so the
  first thing you see is a shuttle already flying. Same behaviour for START GAME.

## Round 3b (2026-09-14)

- **Sounds:** the tutorial now uses `Assets/1 - samsPrefabs/TutorialPlayer.prefab`, a snapshot
  of the gameplay scene's player (footsteps, jump/land, jetpack, breathing + wind, and every
  equippable component). Re-snapshot after changing the real player:
  `Tools ▸ Solar System ▸ Snapshot Tutorial Player Prefab`, then rebuild.
- **Cats** at the gameplay size (2.56–3.76). **Crosshair** scale 1 (4× smaller again).
- **DOWNLOADING** overlay is gone everywhere (load, tutorial, co-op wake): the pod just heals
  and opens. UPLOADING on save is unchanged. Landing hint removed.
- **Grass:** the code checks out; it streams only within 60 m of you, so nothing shows from
  the 100 m hover (same as the real game). Land, walk out, and it should be there. If it
  isn't, tell me — that's a real bug then.
- **Digits:** 640 columns per wall, long trails — green lines from afar, 0s and 1s up close.

## Round 4 (2026-09-14) — real planet

- The slab is gone. You are on a Humble Abode clone 7.5× the size (radius 1500 m), static,
  with the atmosphere and ocean, on the flattest dry patch the builder could find. Blue sky,
  sun up at 55°, real terrain shading. Box is 350 m; descent from 330 m over 14 s.
- Trees / crystals / mushrooms / cats / grass are the gameplay spawners with your tuning.
  Grass streams live (no bake). 15 s after touchdown the console logs the grass cell count —
  if you still see none, send me that line.
- Black bars during the fade are gone (tutorial and new game).
- Expect: terrain is 7.8 m per triangle (HA is 1 m) so it reads chunkier underfoot; hills are
  7.5× taller in metres but the same steepness. Say if you want a flatter noise copy.
- Loading: one planet generates on the activation frame, under the loading screen.

## Round 5 (2026-09-14)

- Planet is now 750 m radius. The box has a lake (water on ~26 % of it), a flat dry landing
  patch at the centre (12 m above sea), and a hill 66 m above the landing patch.
- Grass: found the real cause — it only grows in a band of metres above sea level (1–15 on
  Humble Abode), and that band was a shoreline strip on the big planet. It's scaled now
  (3.75–56 m). Turn GRASS DISTANCE on in settings; you should see patches everywhere below
  the hill line.
- Ceiling: the 1s and 0s stream out from the middle to the walls.

## GPU-resident grass trial (2026-09-14 evening) — tutorial scene only

The tutorial's grass now draws through the new GPU path (`InstancedGrassRenderer.gpuResident`).
The gameplay scene still uses the old CPU path until you've seen this one work.

- **F11** (cheats on) flips ALL grass between the GPU path and the old CPU path — use it for
  a live A/B on fps. Console logs which path is active.
- What to check: (1) fps with grass on vs off, at 1× and at 3× GRASS DISTANCE; (2) turn the
  camera — no see-through / glassy blades, no blades against the sky washing to sky colour;
  (3) walk — blades should not flicker or swap as you move (the thinning is per blade, fixed);
  (4) the shuttle's landing feed and the pause/solar map still look right; (5) a BUILD — the
  procedural instancing variant is the thing most likely to be stripped in a build, and the
  symptom is NO grass at all in the exe while the Editor is fine.
- If it misbehaves, F11 back to the CPU path and tell me what you saw.

## Whole-planet resident grass (2026-09-14, late) — gameplay scene

Humble Abode's baked grass (~150 k blades, 12 MB) is uploaded to the GPU once at load; nothing
streams on the CPU any more; blades grow in from nothing as you approach instead of popping.
Knobs on the gameplay scene's GrassSpawner object ▸ InstancedGrassRenderer: Resident Near
Radius (60 m, full density), Resident Far Radius (350 m, zero), Resident Falloff (2), Resident
Grow Band (0.08). All × the GRASS DISTANCE setting. The tutorial planet has no bake, so it
keeps streaming (GPU-drawn) until it's baked.

Check: jetpack fast across the field — no patches appearing; grass visible far off and thinning
smoothly; frame time; the console line "[InstancedGrassRenderer] Resident grass: N blades".
F11 still drops back to the CPU path.

## Space dust on the GPU (2026-09-14, late) — everywhere

The dust motes (5000) now live on the GPU: a compute shader does the drift, density, fade,
twinkle, ocean test and wrap every frame and one indirect draw renders them. Same maths, same
seed, same queue. The one difference: every mote updates every frame instead of a quarter of
them per frame, so the twinkle is smoother (that was the pre-September-12 look).

- **F2** (cheats) flips dust between the GPU path and the old CPU path. The field is carried
  across the switch so it doesn't jump. Console logs which is active.
- Check: dust looks the same near the black hole, in orbit, at the planet surface (hazed),
  underwater (dimmed), through the free-cam; F2 A/B for frame time.
- If it misbehaves, F2 to the CPU path and tell me what you saw.

## Things to look at / tell me

- Does the 12 s descent feel right? (`Tutorial Director ▸ Descent Seconds`.) The shuttle
  does the real intro's nose-down / flip / retro-burn choreography, compressed.
- Start height (`Depart Altitude`, 185 m): does any part of the hull poke through the
  ceiling at the start?
- Digit walls: colour, speed, density, brightness are sliders on
  `Assets/4 - Scenes/TutorialDigitRain.mat`. Say what you want changed.
- Sun angle is a constant in the builder (55° up). Spawner tuning lives on the prefabs in
  `Assets/1 - samsPrefabs/TutorialSpawners/` (cat cap is a scene override, 8).

## Known limits

- Rebuilding the scene (`Build Tutorial Scene`) wipes anything placed by hand in it.
- Build (exe) check still needed: TUTORIAL from the built game (CLAUDE.md trap #1).
