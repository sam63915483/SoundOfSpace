🟢 ACTIVE — design approved by Sam 2026-09-09. Implementation plan not yet written.

# Audio Studio — full audio rework

**Goal.** Sam's game audio was authored piecemeal over months and never levelled
against itself: some sounds are ear-splitting, others inaudible. He wants to fix
the mix, and to be able to swap out sounds he no longer likes, **without opening
Unity**. The deliverable is a browser tool at `localhost:8766` listing every
action in the game that makes a sound, where he can audition it, set its level,
change its category and replace its file — and a runtime change that makes the
game actually obey that file.

**Sam's own framing (2026-09-09):** *"i want that localhost where i can visually
see all actions in the game that cause a sound and be able to play them and tell
you too loud or find a different sound and give it to you to swap out for one.
then once we get everything mixed in the local host and all of the sounds are the
sounds i like and want i can get you to apply it to the scene."*

---

## 1. Decisions taken (and the ones deliberately rejected)

| Decision | Chosen | Rejected, and why |
|---|---|---|
| Strip the audio out first? | **No.** Game stays fully audible throughout. | An earlier proposal to mute everything and rebuild action-by-action. Sam: *"tbh we dont need to strip the audio out of the game."* Keeping it audible means there is never a broken intermediate state. |
| How browser edits reach the game | **Runtime data.** Game reads a JSON manifest; changes need no recompile, no Inspector, no scene save. | A "worksheet" approach where Sam marks up levels and they get hand-applied to 164 Inspector slots at the end. Rejected because the in-game mixing pass (§6) would then cost an Editor round-trip *per tweak*, forever. |
| Knobs per sound | **Volume, clip swap, category.** Nothing else. | Distance falloff and per-play pitch variation (would fix "audible across the valley" and robotic footsteps). Sam: *"lets just keep it simple now and go with A and after the revamp if im still having problems we can get into more fine tuning."* Revisit only if a real problem survives the revamp. |
| Scope | **Scene `1.6.7.7.7` only.** | Dimensions, Poolrooms and Cutscenes are genuinely separate scenes (`PortalManager.LoadScene`; `DimensionSceneUtil` header: *"The dimension scenes are nearly empty"*). ~20% of the project's audio surface, excluded by Sam's rule. |
| Voice lines | **Included, in their own group.** | The 19 `tr*Clip` radio lines in `TevSmugglingMission.cs` are grouped separately so they don't clutter the mixing list. |

---

## 2. What's actually wrong today (measured, not assumed)

Verified by inspection on 2026-09-09:

- **~465 audio call sites** across 20 systems in `Assets/3 - Scripts`.
- **100 AudioSources are created in code** (`AddComponent<AudioSource>`); **only 1
  AudioSource exists in the 72 MB scene file**, and **zero** AudioListeners. Audio
  is overwhelmingly a code-time concern, not a scene-authoring one. *This is the
  single luckiest fact in this project — it means the rework needs almost no
  scene edits.*
- **164 serialized `AudioClip` fields.** Which clip plays for a given action is
  decided in the Inspector, on prefabs and scene objects. This is why changing a
  sound today requires Unity, and why it cannot be done from a browser as-is.
- **`GameAudioBus` already exists and is well designed** — a code-side volume
  router with SFX / Ambience / UI / Music buses, written August 2026 precisely
  because the project has no `.mixer` asset. **But only 5 `Register()` calls
  exist, across 4 files, against ~100 sources.**

**Root cause of "the mixing is bad": ~95% of the game's audio has never been
routed through the bus and has never been levelled against anything else.** The
category sliders in the pause menu are, in practice, close to decorative. The
sounds are not necessarily bad; nothing has ever had a common reference level.

---

## 3. Architecture

Four pieces. Each is independently understandable and independently testable.

### 3.1 The manifest — `Assets/StreamingAssets/Audio/sounds.json`

The single source of truth for *which clip, how loud, what category*. Plain text,
outside the compiled assembly, re-read at runtime, hand-editable, diffable in git.

```json
{
  "version": 1,
  "sounds": [
    {
      "key": "player.footstep.walk.a",
      "label": "Walking — step A",
      "group": "Movement",
      "clip": "player/footstep_walk_a",
      "volume": 0.8,
      "bus": "SFX",
      "note": ""
    }
  ]
}
```

- `key` — stable identifier used by code. Never renamed once shipped.
- `label` / `group` — human-facing only; drive the browser UI.
- `clip` — path under `Assets/Resources/Audio/`, without extension.
- `volume` — authored level, 0..1. Multiplied by the bus level at play time.
- `bus` — one of the existing `GameAudioBus.Bus` values.
- `note` — free text from Sam to the implementer. Ignored by the game.

Placed in `StreamingAssets` (not `Resources`) deliberately: it must stay editable
after a build, exactly like the `Story/*.json` dialogue trees.

### 3.2 `GameAudio` — `Assets/3 - Scripts/Audio/GameAudio.cs`

A static service; the only thing call sites talk to.

```
GameAudio.Play(key, AudioSource on)      // one-shot through an existing source
GameAudio.Play(key, Vector3 at)          // positional one-shot
GameAudio.Configure(key, AudioSource on) // set clip+volume+bus on a looping source
GameAudio.Level(bus)                     // existing GameAudioBus passthrough
```

Responsibilities: load and cache the manifest once; resolve `clip` via
`Resources.Load<AudioClip>`; apply `volume × GameAudioBus.Level(bus)`; register
long-lived sources with `GameAudioBus` so the pause-menu sliders finally work.

**Critical safety property — graceful fallback.** If a key is absent from the
manifest, or the manifest fails to load, `GameAudio` returns `false` and the call
site plays its existing serialized clip exactly as it does today. **The game is
therefore never silent and never broken part-way through the migration**, and each
wave in §5 is independently shippable.

**No new singleton.** `GameAudio` is a static class with lazy init and creates no
`DontDestroyOnLoad` GameObject, so it needs no entry in
`MainMenuController.EnsureGameplaySingletons()`. This deliberately sidesteps
CLAUDE.md trap #1 (the MainMenu singleton trap that cost two days on the torch
flicker). If a global one-shot pool is ever needed, that decision comes back.

### 3.3 The scanner — `tools/audio-studio/scan.py`

Read-only. Produces the draft manifest so ~130 entries don't get typed by hand.

1. Parse `Assets/3 - Scripts/**/*.cs` for `AudioClip` fields, `PlayOneShot` sites,
   `AddComponent<AudioSource>`, and any literal volumes.
2. Build a `guid → asset path` index from every `.meta` file in `Assets/`.
3. Parse the scene and prefab YAML to resolve each serialized clip field to the
   clip actually assigned to it.
4. Emit a draft manifest with generated keys, plus a report of slots that are
   unassigned or unreachable.

Output is then **curated by hand** — auto-generated labels like `trHoldSteadyClip`
are not names Sam should have to think in.

### 3.4 Audio Studio — `tools/audio-studio/`

A deliberate clone of Dialogue Studio's shape, so there is nothing new to learn
and nothing to install:

```
tools/audio-studio/
  Audio Studio.bat      double-click launcher
  serve.py              stdlib-only HTTP server + JSON API, port 8766
  index.html  app.js  styles.css
  backups/              timestamped, 30 kept per file
  incoming/             drop folder for replacement sounds
```

- Port **8766** — Dialogue Studio owns 8765; both may run at once.
- Left: group list (Movement, Ship, Combat, Ambience, World, UI, Vendor, Voice).
- Right: one row per sound — **▶ play**, volume slider + numeric field, category
  dropdown, **swap file**, notes box.
- ▶ plays the real clip through an `<audio>` element **at the volume currently
  set**, so Sam hears the decision he is making, not the raw file.
- **Swap file**: browser upload (`POST`), or drop into `incoming/`. The server
  copies the file to `Assets/Resources/Audio/<group>/`, writes the Unity `.meta`
  if new (same routine Dialogue Studio already uses), and repoints `clip`.
- Save writes `sounds.json` after a timestamped backup.

**Lockstep rule** (inherited from Dialogue Studio, and it has bitten before): the
bus names and manifest schema exist in both `GameAudio.cs` and `app.js`. **Change
one, change the other**, or a sound previews correctly in the browser and does
nothing in-game.

---

## 4. The clip move into `Resources`

`Resources.Load` by name is the only way Unity resolves an audio asset from a
runtime string, and being under `Resources/` is also what stops a clip that is
*only* referenced from code being **stripped from the build** — the failure mode
the parallel session hit this week with a code-only shader (works in Editor,
missing in build).

- In-scope clips move to `Assets/Resources/Audio/<group>/`.
- **Their `.meta` files move with them**, so GUIDs are preserved and every
  existing Inspector reference keeps working. Nothing breaks before rewiring, and
  the fallback in §3.2 covers anything missed.
- Precedent exists: `Resources/DomeFX/dome_hum` and `dome_enter` are already
  loaded this way.

**This is the one genuinely risky step in the plan** — a bulk asset move. The
exact count is not known until the scanner runs (63 clips sit under
`Assets/Audio`, but in-scope clips also live in other folders; 364 audio files
exist project-wide excluding third-party packs). It requires the cross-session announce-and-ack protocol (§7) and a Sam
save. CLAUDE.md's "don't add new `Resources.Load` without a reason" is
acknowledged and overridden here with reason stated.

---

## 5. Migration waves

Call sites move to `GameAudio` in waves. Fallback (§3.2) means each wave ships on
its own and a half-finished migration is never a broken game.

1. **Movement** — `PlayerController` footsteps/jump/land/water. *Sam's stated
   priority.* Shared file: hand to the other session as a diff.
2. **Ship & shuttle** — `Ship.cs`, thrusters, reactor, hatches.
3. **Combat & creatures** — `EnemyController`, `AlienNPCDamageable`, pistol, axe.
4. **World & survival** — oxygen, domes, fall damage, pickups, spawners.
5. **UI, vendor, NPC dialogue.**
6. **Voice** — the `tr*Clip` radio lines.

Fishing (`Bobber.cs`, 2,774 lines of heavily tuned cast/fight timing) is owned by
the parallel session and is handled **only** by handing it a precise diff — never
by editing the file directly.

---

## 6. Known limitation, stated up front

**Auditioning a sound in isolation in a browser is not the same as hearing it in
the mix.** In-game loudness also depends on 3D distance falloff, how many sounds
overlap, and listener position. The browser pass fixes the dominant problem —
sounds authored years apart with no common reference — but a shorter **in-game
pass** will follow, for the overlaps.

This is precisely why §1 chose runtime data over hand-applied Inspector values:
that second pass must cost a number change and a re-listen, not an Editor session.

---

## 7. Cross-session working rules (in force this session)

Two Claude Code sessions share this working tree. Sam's rules, confirmed by both:

- **Announce risky moves before, not after**, and wait for an ack: any scene
  touch, prefab/ProjectSettings/.meta edits outside own territory, any git op
  beyond explicit-path add/commit, asset deletes or moves, sweeping edits, or
  editing a file the other session owns.
- **Saving is a joint decision and only Sam saves.** Requesting session asks;
  other answers `SAFE` or `HOLD`; only then is Sam told what to save. A save
  flushes *both* sessions' pending Editor state, so it can never be unilateral.
- **Territory.** This session: `Assets/3 - Scripts/Audio/`, `Assets/Audio/`,
  `tools/audio-studio/`. Other session: `World/`, `Fishing/`,
  `Tutorial/FeatureVault.cs`, `UI/MainMenuController.cs`, `UI/TabbedPauseMenu.cs`.
  Shared, ping first: `PlayerController.cs`, `InputSettings.cs`.
- Neither session enters play mode. **Sam runs all playtests.**

---

## 8. Order of work

| # | Step | Risk |
|---|---|---|
| 1 | Scanner + draft manifest | **None** — read-only |
| 2 | Audio Studio tool + curated labels | **None** — new files only |
| 3 | → **Sam mixes in the browser** | — |
| 4 | Clip move into `Resources` + `GameAudio` | **The risky step** (§4, §7) |
| 5 | Migration waves 1–6 | Low; fallback protects each wave |
| 6 | In-game mixing pass | None |

Steps 1–3 are the bulk of Sam's time and cannot break the game, which is why they
come first.

## 9. Success criteria

- Every in-scope sound appears in Audio Studio, auditions correctly, and its
  group/label is meaningful to Sam without reference to code.
- Changing a volume or swapping a clip in the browser changes the game **with no
  recompile, no Inspector, and no scene save**.
- The pause-menu category sliders demonstrably affect the whole game, not 5 sounds.
- Nothing in the game is silent at any point during the migration.
- Compile warning count stays at **zero** (CLAUDE.md baseline).
- No edit to `Assets/1.6.7.7.7.unity` is required by this work.
