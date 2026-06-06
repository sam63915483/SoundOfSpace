# Playtest Work Summary — 2026-06-05

A single-session sweep that started from `docs/PLAYTEST_TASKS_2026-06-05.md` (the
7 original tasks) and grew into a large audio/FX polish + bug-fix pass driven by
live play-testing. All work landed on branch `feat/oxygen-atmosphere-system` as
~32 commits (`c540c79` … prefab). Nothing pushed — local commits only.

Spec + plan for the original 7 tasks: `docs/superpowers/specs/2026-06-05-playtest-fixes-design.md`,
`docs/superpowers/plans/2026-06-05-playtest-fixes.md`.

---

## Part 1 — The original 7 playtest tasks

### §5 Ship-specific prompts: 25 m proximity gate
- Added `Ship.PlayerIsNearOrPiloting(radius = 25f)` — per-instance, throttled,
  lazily refinds the player (multi-ship safe). Reused the existing
  `PlayerController.shipProximityRadius` scaffolding.
- Gated the hull VO behind it so "Hull is ajar"/etc. no longer nag from across
  the map. Reactor/hatch/vitals prompts were left ungated on purpose — they're
  already trigger- or piloting-scoped.

### §7 Tip queue (investigation + rebuild)
- **Finding:** the on-screen strip (`HALLineHUD`) already queued correctly; the
  real silent-drop was `HALCommentator.Volunteer()`'s 8 s rate limiter *discarding*
  lines. Fixed it to queue instead of drop.
- Built a visual stacked queue in `HALLineHUD` (primary + smaller previews,
  slide-up promotion) and a `ShowLive(Func<string>)` for live countdowns.
- (Later refined — see Part 3: per-tip dedup, and voiced tips fade with their TTS.)

### §4 "Hull sealed" live countdown + milestone warnings
- `OxygenManager` gained `hullWasFilledOnGround` tracking + a live "Hull sealed —
  m s of air remaining" countdown on the hatch-close edge, plus 4/2/1/0.5-min
  warnings (once each, re-armed on a fresh fill).
- **Gameplay-semantics change:** a sealed, occupied hull is now a *finite* reserve
  that depletes while you're inside (was infinite). `hullMax 300 / 1` ≈ 5 min.

### §6 "Hull exposed to the vacuum of space" tip
- One-shot when the hatch opens into vacuum, re-arming when the exposure clears.

### §3 Phone persistent first-message nag
- `PlayerPhoneUI.HasEverOpened` (saved in `EarlyGameProgressSave`, reset in
  `NewGameReset`). First message shows a non-fading "Press X to open your phone."
  prompt that persists until the phone is opened.

### §2 Suit O2 into the vitals HUD
- Added a cyan SUIT O2 row to `VitalsHUD` (bottom-right). Removed the standalone
  top-left suit bar from `OxygenHUD`; its hull bar stays as the contextual element.

### §1 Main-menu ambient audio
- Generated a space-ambient loop and wired it into `MainMenuController`.
- (Grew into the full UI-sound system — see Part 2.)

---

## Part 2 — Audio & FX system (built out over several rounds)

### Shared infrastructure
- **`StreamingAudio.Load(path, type, cb)`** — one place for runtime clip loading
  from `StreamingAssets/` (lets auto-created singletons / prefab components use
  generated clips without serialized refs).
- **`UiSfxPlayer`** — shared button hover/click SFX via `UiSfxPlayer.Attach(Button)`,
  wired into the main menu, the save/load screen (slots + delete), and the in-game
  pause menu (tabs, buttons, toggles). Also loops the pause-menu ambience.

### Menus
- Hover + click SFX on **every** menu button (not just the 3 main ones).
- Pause menu plays its own **faster/spacier** `PauseAmbience` track (distinct from
  the slow cinematic main-menu loop) while open.

### Ship
- **Pilot startup / shutdown** SFX (power-up on F-to-pilot, power-down on exit),
  gated past 2 s so load-time auto-pilot stays silent.
- **Reactor** core hum (`ReactorBuzz`, volume tracks fuel). The "unstable" red
  events **live-modulate the buzz** (pitch up + louder + perlin wobble) rather
  than a separate canned surge clip.
- **Hatch suction** — a ~3 s windy burst (full 1 s, fade 2 s) when the hatch vents
  in vacuum, only when the cabin is pressurized.
- **Hatch pressurizers** — hiss + a fast downward smoke puff from each of your
  `EMIT1`/`EMIT2` anchors on every hatch open/close (smoke reuses the concert
  cloud material; see Part 3 for the debugging saga).

### Player / suit
- **Breathing** — final curated pool of 13 clips in `StreamingAssets/Audio/Breaths/`
  (7 new + the good originals), fired every 10–15 s, **loudness-normalized** on
  load so quiet clips are boosted to match the good ones.
- **Suit life-support hum** — constant quiet helmet/air-recycler loop on foot.
- **Low-oxygen alarm** — periodic beep while the suit drains below 25 %.
- **Water splash** — plays on first entry into water, re-arms only after fully
  leaving.

### HAL voice (TTS)
- Generated the missing hull lines (vacuum-exposed + 4/2/1/0.5-min) in the George
  voice and registered them in `HALVoiceManifest`. (Reverted an accidental
  overwrite of the existing vitals/ship/concert family clips.)

---

## Part 3 — Bugs found & fixed during play-testing

- **Vacuum tip repeating every 10 s** → now once per hatch-open (re-arms on clear).
- **Suction firing on any hatch-open** → only when the cabin still has air
  (`hullO2 > 0`).
- **Ship wind heard from far away** — `ShipWindAudio` was 2D and keyed only off the
  ship's own orbital speed; gated it on `PlayerIsNearOrPiloting()`.
- **Hull O2 HUD bar lingering 50 m+ away** — gated on a new
  `OxygenManager.ShipPromptsAudible`.
- **Queued ship tips persisting after leaving** — tips are `shipScoped`;
  `ClearShipScoped()` purges them when the player leaves the radius.
- **Water-entry played the landing thud** — landing SFX suppressed while touching
  water so only the splash plays.
- **Hull O2 countdown frozen on the ground** — sealed air now depletes whenever
  inside (removed the in-atmosphere guard), so the countdown is live from sealing.
- **Milestone warnings spamming on hatch-open** — restricted to hatch-closed
  (Sealed) only; the venting case shows the vacuum tip instead.
- **Hull vents 3× faster** when the hatch is open in vacuum.
- **Pressurizer smoke saga** (multi-step): wrong direction (converged between the
  two units → aimed at ship-down/floor); too short (→ ~2 s sustained); streaming
  into the cabin (the emit points sat under non-unit-scaled parents that amplified
  the local-space velocity → switched to `EMIT1/2` anchors + **neutralized the
  inherited scale** so velocity is in real world units); then made 1.8× taller.
- **Tip stacking** — reverted "1 at a time"; restored the queue with **per-tip
  dedup** (a tip whose key matches the active/queued one is ignored), so spamming
  the hatch can't pile up duplicates. Live tips dedup on a stable key.
- **Voiced tips lingering** — tips with TTS now stay only as long as the narration,
  then fade (1.2 s floor / 14 s ceiling).
- **Quiet breath clips** — automatic loudness normalization (boost-only) so faint
  clips match the good loud ones without changing the good ones.

---

## Key new/changed files

- `Assets/3 - Scripts/Audio/` — `StreamingAudio.cs`, `UiSfxPlayer.cs`,
  `PlayerSuitAudio.cs`, `ShipWindAudio.cs`
- `Assets/3 - Scripts/Survival/` — `OxygenManager.cs`, `OxygenHUD.cs`, `VitalsHUD.cs`
- `Assets/3 - Scripts/UI/` — `HALLineHUD.cs`, `MainMenuController.cs`,
  `TabbedPauseMenu.cs`, `PlayerPhoneUI.cs`
- `Assets/3 - Scripts/AI/` — `HALCommentator.cs`, `HALVoicePlayer.cs`, `HALVoiceManifest.cs`
- `Assets/3 - Scripts/Ship/` — `ReactorGlow.cs`
- `Assets/3 - Scripts/Scripts/Game/Controllers/` — `Ship.cs`, `PlayerController.cs`
- `Assets/3 - Scripts/SaveSystem/` — `SaveData.cs`, `SaveCollector.cs`, `NewGameReset.cs`, `SaveLoadUI.cs`
- `Assets/3 - Scripts/Story/` — `StoryDirector.cs`
- `Assets/MainMenu.unity`, `Assets/1 - samsPrefabs/SHIP44.prefab`
- `Assets/StreamingAssets/Audio/` (+ `Breaths/`), `Assets/StreamingAssets/AI/voice/`

## Notes / follow-ups
- All ship-dependent paths still want a hands-on play-test (the ship isn't in the
  scene until bought).
- The sealed-hull "finite reserve" is a deliberate gameplay change — flagged for a
  feel-check; tunable via `hullBreathConsumeRate`.
- Scene-asset wiring (MainMenu audio clip) was done via direct YAML edits to avoid
  editor scene-switching — verified by compile, not editor-validated.
- Left uncommitted (Sam's separate WIP): `1.6.7.7.7.unity`, blood VFX, `Scateboard/`,
  `LLMManager.json`, `LLMUnityBuild/`, the two playtest docs.
