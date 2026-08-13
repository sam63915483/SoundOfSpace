# Handoff — Cassette Pivot, Phase 1: The Shuttle Computer (v2)

Date: Aug 13, 2026
From: Sam (via Claude chat)
Supersedes v1. Change from v1: **the day-1 browser build must make real sound.** Not a visual mockup — a playable instrument. Sam needs to turn the dials and hear the loop change live, today.

---

## 0. Protocol (standing rules, unchanged)

1. **State your build plan first** and wait for Sam's approval before implementing (GDD_StoryBible_v2.md §0 rule 4).
2. **Browser prototype comes FIRST** (Step 1). STOP after it and wait for Sam's review. No Unity work until approved.
3. Sam places/repositions GameObjects. If you create a placeholder object, report its exact name.
4. Do NOT modify core systems: floating origin, n-body physics, the multiplayer spine (WorldSync / PlanetRelativeSync / SolarSystemSync).
5. Do NOT delete, vault, or refactor any mushroom/cultivation/economy systems. Untouched until Sam says otherwise.

---

## 1. THE PIVOT — read this first

**The commodity is changing from mushrooms to music cassettes.** The player makes music on the shuttle computer, prints it to tape, and sells tapes to aliens. The loop SHAPE is identical to the current Schedule 1-style loop, so nearly everything built gets reused:

- [EXISTS] BuyerLedger (bond, price bonus, regular conversion, hidden-want reveals) → alien music taste + fan conversion; want-texts become genre requests.
- [EXISTS] BuyerDeals negotiation → unchanged, sells tapes.
- [EXISTS] Tev dialogue architecture + haggle chains + fronting/payment UI → Tev becomes a music-store owner (LATER phase; standing rule: report Tev's current conversation flow to Sam before touching dialogue).
- [EXISTS] Per-planet milestone structure → "get on this planet's radio" (later phase).

Mushrooms' fate (vault vs. second product line) is an open design decision — not yours to resolve. **Today: the computer, with working sound.**

---

## 2. STEP 1 — [BUILD] Audible browser prototype (first, then STOP)

Plain HTML/CSS/JS + **Web Audio API**, no framework, served on localhost. This is the real instrument, prototyped where audio iteration is fastest. All synthesis is generated — no audio files, no samples, nothing pre-recorded.

### 2a. Screens
**Home screen:** fullscreen alien OS — boot/status bar, app icon grid: **TRAX** (music app, name reviewable) + greyed placeholders (MAIL, BANK, RADIO). Click TRAX → music app. Visual starting direction: chunky retro terminal/CRT, scanlines, alien glyphs. Sam art-directs at review; treat aesthetics as a draft.

**Music app (TRAX):** 6 dials (§4), **live genre readout** (real classifier, §5), PLAY/STOP transport (functional), plugin rack — 4 active modules (THUMPER, GLOWORM, SIREN, CAVE) + 2–3 visibly LOCKED slots, PRINT button + quantity stepper (visual only today).

### 2b. Audio engine ([BUILD] — the core deliverable)
- **Clock:** 16-step loop, lookahead scheduler (the standard "tale of two clocks" pattern: ~25ms setTimeout tick scheduling events ~100ms ahead on the AudioContext clock). Never schedule note-by-note on the main thread timer alone.
- **Voices (all synthesized in Web Audio):**
  - **THUMPER** (drums): kick = sine osc with fast pitch-drop envelope; snare = bandpass-filtered noise burst; hat = highpass-filtered short noise.
  - **GLOWORM** (bass): oscillator → lowpass filter → gain envelope. Low octave.
  - **SIREN** (lead): oscillator (waveform morphs sine→saw→square with CRUNCH) → filter → send to CAVE.
  - **CAVE** (space): feedback delay network + wet/dry mix, shared send bus. (Approximate reverb with multi-tap/feedback delay; no impulse files.)
- **Musicality guardrails:** every pitch quantized to a scale table; all voices locked to the one clock. Scale tables (HOMESICK selects, low→high): [alien: whole-tone, chromatic-cluster] → [Hirajoshi, Phrygian] → [minor pentatonic, natural minor]. Master key fixed.
- **Deterministic patterns:** each voice's 16-step pattern is generated from a seeded PRNG (e.g. mulberry32). Seed = hash of the quantized dial vector. Same dials = same loop, always, on every machine. Dial changes that affect pattern parameters regenerate the pattern on the next bar boundary (no mid-bar glitching); timbre/FX parameters apply instantly. **No randomness outside the seeded PRNG** — this vector later drives cassettes, alien reactions, and radio playback.
- **Latency/clicks:** all parameter changes via `setTargetAtTime`/short ramps, never hard jumps on live nodes.

### 2c. [TEST] / review gate
Sam plays it in the browser: turns dials, hears the loop change live, checks the genre label tracks his ear, art-directs the UI. He will tune §5 genre centers by ear here. **STOP — do not start Step 3 until he explicitly approves.**

---

## 3. STEP 2 — [BUILD] Unity interactable + UI (after approval)

- [EXISTS] Shuttle interior (locker, whiteboard, stasis pod). If no computer prop exists, create a placeholder (monitor on desk/console) and **report the object name**.
- Press **F** while looking at it (existing look-at interact pattern) → lock movement/camera, unlock cursor, fullscreen UGUI canvas: home screen → TRAX. ESC (and/or F) exits cleanly.
- Recreate the approved browser UI in UGUI. Reuse the existing interaction soft-lock pattern (see the isInDialogue soft-lock fix) so a player can't get stuck.
- Multiplayer: client-local UI, no netcode, must not break sync. [TEST] enter/exit repeatedly, cursor state, MP session unaffected.

## 3.5 STEP 3 — [BUILD] Unity audio port (same day if time allows; expect this to be the spillover item)

Port the engine logic (patterns, scales, macros, classifier — ports 1:1) onto a Unity audio backend. **Two options — ASK SAM which path before building:**

- **Option A — no purchase, pure Unity:** procedurally generate waveforms at runtime (`AudioClip.Create` — sine/saw/square/noise, code-generated, no assets); pitch via `AudioSource.pitch`; clock via `AudioSource.PlayScheduled` on the DSP clock; filters/FX via built-in `AudioLowPassFilter`, `AudioDistortionFilter`, `AudioEchoFilter`, `AudioReverbFilter` components (all live-tweakable). Fully self-contained, ships with the game, no dependency.
- **Option B — Audio Helm (Asset Store):** native synth + sequencer built for exactly this. Better sound ceiling, less DSP plumbing. **Requires Sam to purchase and import the asset first — Claude Code cannot do this step.** If Sam picks B, he imports it, then you wire sequencer + patches to the macro dials.

Recommendation: A today (zero blockers, good enough to prove the loop in-game), evaluate B when the sound ceiling starts to matter.

---

## 4. THE DIALS (6 master macros, range 0–10 continuous)

| Dial | Flavor | Audio mapping (implement in §2b) |
|---|---|---|
| **PULSE** | how fast it hits | BPM 60→170 + note density across voices |
| **CRUNCH** | how mean it sounds | waveshaper drive + osc morph sine→saw→square + bitcrush feel |
| **GOO** | how wet and squelchy | filter cutoff/resonance + slow filter LFO wobble |
| **VOID** | how much empty space | CAVE send/feedback/mix + pattern sparseness |
| **JITTER** | how twitchy the rhythm | syncopation probability + off-grid nudge + hat scatter |
| **HOMESICK** | how human it feels | scale table index (0 = alien, 10 = familiar/melancholic) + detune (low = detuned) |

---

## 5. THE GENRES (10) + CLASSIFIER

Each genre = a center in 6-D dial space. Values are placeholders; Sam tunes by ear at the review gate.

| Genre | Vibe | PULSE | CRUNCH | GOO | VOID | JITTER | HOMESICK |
|---|---|---|---|---|---|---|---|
| **GLORP** | wet squelchy bass funk | 6 | 3 | 9 | 3 | 5 | 4 |
| **DRIFT** | weightless space drone | 1 | 1 | 3 | 9 | 1 | 6 |
| **SKITTER** | fast twitchy scatter-beats | 9 | 4 | 3 | 3 | 9 | 3 |
| **SLUDJ** | slow crushing heaviness | 2 | 9 | 6 | 5 | 2 | 2 |
| **CHIRP** | bright bouncy cute | 7 | 2 | 2 | 2 | 4 | 9 |
| **NULLGAZE** | hazy sad washed-out | 3 | 5 | 3 | 8 | 1 | 8 |
| **THRUM** | hypnotic ritual percussion | 5 | 3 | 5 | 4 | 7 | 1 |
| **VOLT** | aggressive electric dance | 8 | 7 | 4 | 2 | 5 | 5 |
| **WARBLE** | woozy detuned seasick psych | 4 | 4 | 7 | 6 | 3 | 7 |
| **CLANG** | metallic industrial banger | 6 | 8 | 2 | 5 | 8 | 1 |

**Classifier (identical math in JS and, later, C#):** track vector = 6 dial values → Euclidean distance to each center → nearest = genre. If second-nearest is within `BLEND_THRESHOLD` (start 1.5, tunable), show blend label: adjective of second-nearest + noun of nearest (Glorpy, Drifty, Skittish, Sludjy, Chirpy, Null, Thrummy, Volted, Warbly, Clangin') — e.g. **"Sludjy Glorp"**. Fully deterministic; no randomness in this path.

---

## 6. LATER PHASES — context only, DO NOT BUILD

- Cassette item + PRINT saves the dial vector; tape stock/costs via Tev.
- Tev music-store rework (report current dialogue first), demo-first selling, buyer genre reskin.
- Per-planet radio milestone (radio re-renders the saved vector through the same engine), plugin shop unlocking the locked rack slots.

## 7. [OPEN]

- Music app name (TRAX placeholder).
- Unity audio backend: Option A vs B (Sam decides; B requires his purchase).
- Genre names/centers, dial names, home-screen app list — all Sam-reviewable at the gate.
