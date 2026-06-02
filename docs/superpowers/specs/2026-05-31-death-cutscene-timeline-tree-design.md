# Death Cutscene — "Timeline Tree" — Design

**Date:** 2026-05-31
**Status:** Approved, implementing v1.

## Goal

Replace the current in-place death respawn with a Steins;Gate-style death
cutscene that visualises the player's death history as a branching tree of
timelines, then reloads the player's most recent save. Each death adds a branch:
the timeline that just ended turns red, a new blue timeline branches off and is
entered through a wormhole vortex. Over a playthrough the screen accumulates into
a tree of red dead-ends with one live branch reaching the end.

## Death flow (new)

1. Health hits 0 → `ResourceManager` increments `totalDeaths`, fires `OnDeath`
   (unchanged). At this point `totalDeaths` already includes the death that just
   happened, so it equals the number of *dead* timelines.
2. `DeathCutsceneController` (subscribed to `OnDeath`) takes over: marks itself
   handling, freezes the player (`isDead`/`isInDialogue` already set by RM),
   raises a full-screen overlay canvas, plays the timeline cutscene on **unscaled
   time** (SlowmoOnKill may have lowered `Time.timeScale`).
3. `ResourceManager.DeathSequence` checks `LegacyRespawnSuppressed` and, when the
   cutscene is handling death, **skips the legacy in-place respawn** (RM is
   DontDestroyOnLoad — its coroutine would otherwise reset vitals on top of the
   reloaded save).
4. At the end of the cutscene the controller picks the newest save
   (`SaveSystem.ListSaves()[0]`), schedules it via `PendingLoad.ScheduleLoad`,
   sets `Time.timeScale = 1`, and reloads `1.6.7.7.7`. Overlay stays up over the
   load.
5. After the save is applied (deferred past `SaveLoadRunner`'s 1 frame + 1
   FixedUpdate via `sceneLoaded`), the controller: re-asserts the live death
   count (`SetTotalDeaths(deathsAtDeath)` — the save's older count would roll it
   back), clears `PlayerController.isInDialogue`, and fades the overlay out →
   player is back where they last saved (the cabin in practice).

### No-save / stale-save correctness

- New Game does not touch disk saves, so a stale `autosave` from a previous run
  could be the newest file. Fix: `NewGameReset.Apply()` ends with a forced
  `AutosaveManager.Autosave()` so the fresh run owns the newest save. This also
  covers the first-launch empty case.
- Belt-and-suspenders: if `ListSaves()` is somehow empty at death, fall back to
  the original in-place respawn (never soft-lock) and log it.

## Timeline tree visual

Pure **ScreenSpace-Overlay UI** (no extra camera, no scene change, no
floating-origin exposure). "Camera" pan/zoom = animating a root container
RectTransform (`anchoredPosition` + `localScale`). Lines = thin rotated/stretched
`Image` segments chained along gentle bezier curves; glow faked with a soft
radial sprite + a bright core (procedural `Texture2D`, matching Hotbar's sprite
generation). Vortex = a continuously-rotating container of blue spiral arms +
pulsing core glow.

Geometry is **deterministic from `totalDeaths` (D)**:

- Branch points march rightward from a root; timeline `i` branches from the
  previous branch point and splays up/down (alternating), each lived length
  growing with `i` ("each goes further than the last").
- Timelines `1..D` are dead → red, with a red node at the tip. The most recent
  (the `D`th) animates blue→red during the cutscene.
- Timeline `D+1` is the newborn → blue, grows during the cutscene, ends in the
  vortex.
- Drawn dead-branch count capped (`MaxVisibleBranches`, ~12) for perf/legibility
  at high death counts; the "camera" always frames the active end so it reads
  correctly regardless of D.

### Cutscene beats (unscaled, ~7s, skippable with Space/Esc)

1. Fade in from black; show accumulated tree, framed on the just-died blue
   timeline `D`.
2. Timeline `D` tip flashes, animates blue→red, red node pops (slight shake).
3. Pan right / zoom out to the latest branch point; a new blue branch grows
   outward.
4. Blue vortex spirals up at the new tip; camera pushes in; fade to blue.
5. Trigger reload; on applied → fade blue→clear over the cabin.

## Files

- **New:** `Assets/3 - Scripts/Cutscenes/DeathCutsceneController.cs` —
  auto-singleton (`RuntimeInitializeOnLoadMethod` + MainMenu early-return),
  seeded in `MainMenuController.EnsureGameplaySingletonsAsync` (trap #1). Builds
  all UI procedurally. Carries the live death count across the reload via a
  `static int`.
- **Edit:** `ResourceManager.cs` — add `static Func<bool> LegacyRespawnSuppressed`
  hook; bail out of the legacy respawn in `DeathSequence` when set.
- **Edit:** `MainMenuController.cs` — seed `DeathCutsceneController` in both the
  async and legacy `EnsureGameplaySingletons` paths.
- **Edit:** `NewGameReset.cs` — force an autosave at the end of `Apply()`.

No save-schema changes — the whole tree reconstructs from `totalDeaths`.

## Out of scope (later)

Sun-clock day counter, manual-save-at-waypoint, vortex shader/art polish, branch
length tied to real per-life progress.
