# Mission 1 — Intro / Wake-Up Sequence + "Soul" Layer (Design Spec)

> **What this is:** the implementation design for the game's revamped cold open. Builds the
> wake-up sequence from `docs/Mission1_Intro_WakeUp.md` and layers in the "homesick astronaut
> far from home" feeling the build spec calls for — through writing, a personal object, and
> ambient sound.
>
> **Supersedes for the open:** integrates with (does NOT replace) the existing Mission 1 flow
> in `docs/GDD_VerticalSlice_Mission1_Fork.md`. The village first-meeting with Tev is preserved.
>
> **Status:** APPROVED v1 · **Date:** June 16, 2026.
>
> **Tags:** `[EXISTS]` live, integrate · `[BUILD]` new code · `[AUTHOR]` writing · `[ASSET]`
> generated content · `[TEST]` must pass.

---

## 0. The feeling we're building (the why)

The player wakes amnesiac in a stranger's home, far from a home they can't even picture. HAL
(the companion AI) narrates their **body** in flat clinical mission-speak; it never narrates
their **heart**. The ache lives in the gap: "you will be returned home" lands on someone with
no memory of home. As the black clears, the player's eye finds a **framed photo of an alien
family** — Tev's family, though the player doesn't know that yet. They project home / family /
a lost Earth onto strangers. The planet is even named **Humble Abode** — another humble home,
not theirs. Nothing says any of this out loud. The machine talks; the photo doesn't.

**Delayed recognition (the payoff):** Tev is **away** when the player wakes. Later, at the
village, the player meets Tev for the first time (existing flow, unchanged) and quietly realizes
*this is whose house I woke in, whose family that was.* The ache pays off twice.

---

## 1. Sequence (in order)

1. **Black screen + "Wake up" loop** `[BUILD]` — full-screen black overlay at alpha 1.
   Movement + look **locked**; LMB still read. HAL repeats **"Wake up"** on a ~2.5s interval.
2. **"Press LMB" prompt @ 10s** `[BUILD]` — centered prompt appears 10s in (separate UI element,
   does not fade with the black). Click-counting becomes active only now; earlier clicks ignored.
3. **Click to surface — 6 clicks** `[BUILD]` — each LMB lowers overlay alpha by `1/6`, lerped over
   ~0.3s. "Wake up" loop continues. On the 6th click: alpha 0 → hide prompt, stop the loop.
4. **Stage-setting briefing** `[BUILD]` `[AUTHOR]` — the briefing lines (see §2), each paced by
   TTS-finished, no overlap.
5. **The body reacts — heartbeat** `[BUILD]` — heartbeat SFX fades in (looping, slow). Optional
   soft red vignette pulsing in sync. A **held silence** here: HAL says nothing while the
   player's eye finds the photo.
6. **HAL re-reads vitals** `[BUILD]` `[AUTHOR]` — "Heart rate elevated. Vitals irregular." The
   stable→irregular flip is **intentional** (the news lands, the body responds). Do not "fix" it.
7. **First clinical reassurance** `[BUILD]` `[AUTHOR]` — the "returned home / try not to think
   about it" beat (see §2).
8. **Hand off to survival** `[INTEGRATE]` — unlock movement + look, fade heartbeat to subtle/off,
   trigger the **existing food/water survival guidance flow**.

---

## 2. The writing — soul layer 1 `[AUTHOR]`

HAL stays **clinical throughout**. It never mentions the photo. Approved lines (tweak freely in
the inspector later; these are the authored defaults):

**Briefing (step 4):**
1. "Good morning, astronaut. Vital signs stable."
2. "You have been in transit for three years. You crash-landed on this world two days ago."
3. "Memory loss is expected after stasis of this length. It will not affect the mission."
4. "While you were unconscious, a local took you in. A native species. You are, currently, their guest."

*(heartbeat fades in — held silence; the player finds the photo)*

**Vitals re-read (step 6):**
5. "Heart rate elevated. Vitals irregular."

**Reassurance (step 7):**
6. "It is normal for those emerging from stasis to have difficulty recalibrating. Remember — when the mission is complete, you will be returned home."
7. "...For now, try not to think about it."

> **Design intent:** line 3 (the amnesia beat) makes "home" a word with no picture behind it —
> the precondition for the photo to do its work. Line 7's ellipsis is the single almost-human
> pause in an otherwise flat delivery; it is the only place HAL's mask slips a millimetre.

---

## 3. The photo — soul layer 2 `[BUILD]` `[ASSET]`

A framed photo prop in the cabin, placed and oriented so the player's default view lands on it as
the black clears (≈ lines 4–5). **Wordless** — no HAL line, no interact prompt, no objective. It
is set dressing that happens to be the emotional core.

**Render approach — HYBRID (approved):**
1. Capture the actual `Assets/5 - External Imports/Alien_Toys/` prefab(s) — Tev's species — so the
   creatures in the photo are exactly who the player meets later (canon-consistent delayed
   recognition). Pose **Tev's family: Tev + a partner + two kids** (four figures, a clear adult
   pair with two smaller children).
2. Run that capture through image-gen as reference to render it as a **worn, faded family
   snapshot**: warm domestic framing, soft focus, aged/curled edges, slight vignette — a keepsake,
   not a screenshot.
3. Apply the resulting texture to a simple framed-photo prop (existing frame prop if one exists,
   else a quad + thin frame mesh) placed in the cabin.

The photo is Tev's family — Tev, his partner, and their two children. The player will not be told
this; the village meeting reveals it implicitly.

---

## 4. Ambient — soul layer 3 `[BUILD]` `[ASSET]`

- **Cabin room-tone:** a low, lonely hum bed under the whole sequence (generated SFX, 2D looping
  AudioSource). The room feels real and far away. Continues quietly into the survival handoff.
- **Heartbeat:** looping SFX, fades in slow at step 5; optional soft red UI vignette pulsing in
  sync. Fades to near-off at handoff (step 8).
- **Held silence:** after briefing line 4 and under the heartbeat — silence used deliberately as
  writing, the player alone with the photo before HAL speaks again.

---

## 5. Components to build

- **`IntroSequenceController`** `[BUILD]` — one MonoBehaviour, coroutine state machine. Owns: the
  "Wake up" loop, the 10s timer, click-counting + alpha lerp, the ordered line firing (paced by
  TTS-finished), the heartbeat + room-tone, and the handoff. Unfade approach: a `targetAlpha`
  decremented per click; `Update` lerps `currentAlpha` toward it.
- **Fade overlay** `[BUILD]` — Screen-Space-Overlay Canvas, full-screen black `Image`, **Raycast
  Target OFF** (clicks read via `Input.GetMouseButtonDown(0)`, not the UI). "Press LMB" is a
  separate TMP element shown/hidden independently.
- **Photo prop** `[BUILD]` `[ASSET]` — §3.
- **Audio** `[BUILD]` `[ASSET]` — room-tone + heartbeat AudioSources (§4).

---

## 6. Integration points (all verified in code)

- **Speak a line:** `HALCommentator.VolunteerExternal(string line)` → HAL HUD + voice + transcript
  (`Assets/3 - Scripts/AI/HALCommentator.cs:341`). *Note:* `Volunteer` rate-limits/queues via
  `HALLineHUD`; for a tightly-scripted sequence we must confirm during implementation that the
  rate-limit does not stall ordered lines — if it does, expose a `bypassRateLimit` path
  (the private `Volunteer` already supports the flag; `VolunteerExternal` does not surface it).
- **Pace by TTS-finished:** poll `HALVoicePlayer.IsPlaying` (`Assets/3 - Scripts/AI/HALVoicePlayer.cs:28`)
  via `WaitUntil`. **Voice only plays if the exact line text is in `HALVoiceManifest`** — so §7
  must generate + register clips, or the lines show silently.
- **Input lock:** `TutorialGate.LockAll()` / `UnlockAll()` (movement + mouse-look). Canonical
  tutorial pattern.
- **New-game detection:** `StoryStep.ColdOpen` is set on new game only (`StartCabinSpawnPoint.cs`);
  load preserves the saved step. Combine with the `IntroPlayed` flag (§8).
- **Survival handoff:** after unlock, the player naturally trips the existing water/food gates
  (`StoryDirector.HandleCleanWater` / `HandleCookedFood` → `CheckGates`), which queue the existing
  guidance. We do not build new survival content — just release control into it.
- **Spawn / cabin:** `StartCabinSpawnPoint.cs` positions the player on new game. The photo prop and
  audio live in / near that cabin scene object.

---

## 7. Assets to generate `[ASSET]`

- **Voice clips** for all 7 lines + the "Wake up" loop line, matching HAL's "George"
  elderly-British-computer voice; saved to `StreamingAssets/AI/voice/` and registered in
  `HALVoiceManifest.Lines` (exact-text keys).
- **Photo texture** — the hybrid render (§3).
- **SFX** — cabin room-tone loop; heartbeat loop.

---

## 8. Save / gating `[BUILD]` `[TEST]`

Add a persisted `IntroPlayed` boolean via the standard 4-touchpoint recipe:
- `EarlyGameProgress.IntroPlayed` (static field) + reset to false in `EarlyGameProgress.ResetAll()`.
- `EarlyGameProgressSave.introPlayed` (DTO mirror, `SaveData.cs`).
- Capture in `SaveCollector.CaptureEarlyGame`; apply in `SaveCollector.ApplyEarlyGame`.
- Confirmed reset on New Game via `NewGameReset.Apply()` (which calls `ResetAll()`).

Guard: the controller runs the sequence only if `!IntroPlayed && CurrentStoryStep == ColdOpen`,
sets `IntroPlayed = true` at start, so reload / clone-respawn never replays it. Clicks before the
10s prompt are ignored.

---

## 9. Acceptance test `[TEST]`

Fresh new game → black screen with "Wake up" repeating → at 10s "Press LMB" appears → clicks
before the prompt do nothing → 6 clicks fully clear the screen → the four briefing lines play in
order without overlapping → heartbeat + room-tone fade in → photo is visible in the player's view
→ "Heart rate elevated. Vitals irregular." → reassurance lines → control unlocks and the food/water
flow begins. Reload/respawn does **not** replay the intro. Later: meeting Tev at the village still
works (existing flow unbroken).

---

## 10. Guards / risks

- **CLAUDE.md trap #2 (atmosphere/procedural planet) is NOT touched** — this is cabin-interior +
  UI + audio + a save flag. Stay out of the forbidden zone.
- **MainMenu singleton trap #1:** `IntroSequenceController` is scene-placed (in the gameplay
  scene), not an auto-singleton, so it does not need `EnsureGameplaySingletons` seeding. If it
  becomes a singleton, mirror the trap-1 recipe.
- **Save order (trap #3):** `IntroPlayed` rides the existing `EarlyGameProgress` capture/apply
  slot — no new ordering introduced.
- **Existing Mission 1 flow must remain intact** — do not alter `TevDialogue` / the village
  meeting. This spec only adds the pre-village wake-up.
- **Rate-limit risk** on `VolunteerExternal` (see §6) — verify early.

---

*End of v1. Next: implementation plan (writing-plans).*
