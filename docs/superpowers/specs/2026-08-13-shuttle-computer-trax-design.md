# Shuttle Computer / TRAX — Design (Step 1: audible browser prototype)

Date: 2026-08-13
Source: `docs/Handoff_CassettePivot_ShuttleComputer_v2.md`
Status: approved by Sam 2026-08-13. Step 1 only. Steps 2–3 are context.

---

## 0. What this is

The cassette pivot's Phase 1: a playable music instrument on the shuttle computer.
Six macro dials drive a generative, fully deterministic loop; a live classifier names
the genre. Built first as a browser prototype because audio iteration is fastest there,
then ported into Unity behind the `ConsoleScreen` prop in `Shuttle_Lander.prefab`.

**Step 1 stops at a review gate.** Sam plays it in the browser, tunes the genre
centres by ear, art-directs the UI. No Unity work until he approves.

## 1. Decisions taken at brainstorm (2026-08-13)

| Question | Decision |
|---|---|
| Plugin slots | **MOSS** (chord pad) and **SPINDLE** (arpeggiator) fill the two previously-locked slots, 2026-08-13. They complete the band — drums, bass, chords, lead, arp, space — and are the audible face of the new harmony. A future plugin shop adds slots rather than unlocking these. |
| Dial name | **WARP** replaced HOMESICK (2026-08-13). HOMESICK named a feeling, not a sensation, so it sat oddly beside PULSE/CRUNCH/GOO/VOID/JITTER. WARP runs the opposite way — 0 straight, 10 alien — so the dial semantics and all ten genre centres inverted with it. |
| Loop length | **4-bar phrase (64 steps)**. Bars 0–2 hold the pattern; bar 3 gets a seeded fill in its last 4 steps. |
| Plugin rack | **Working on/off toggles** per voice. Needed so Sam can solo a voice while tuning dials by ear. |
| Today's scope | Audio engine + TRAX **first**, then the OS shell. Both land today. |
| Unity audio backend (Step 3) | **Option A — pure Unity** (`AudioClip.Create` + `PlayScheduled` + built-in filters). No asset purchase. |

Not in scope today: PRINT/cassette persistence (button is visual only), Tev, buyers,
mushrooms, any Unity code, any netcode.

## 2. Architecture — the port boundary

Everything hinges on one rule: **the maths ports 1:1 to C#; the audio and UI get
replaced.** Three layers, strictly one-directional (`ui → engine`, `audio → engine`,
never the reverse; `engine` imports nothing).

```
prototypes/shuttle-computer/
  engine/          PURE LOGIC — no Web Audio, no DOM, no Math.random, no Date.
    prng.js          FNV-1a 32-bit hash + mulberry32
    scales.js        6 scale tables, degree -> MIDI -> Hz
    params.js        dial vector -> flat param object (the macro table, §4)
    patterns.js      seed -> 4 bars x 16 steps per voice
    classifier.js    10 genre centres, euclidean nearest, blend label
  audio/           Web Audio backend. Step 3 replaces ONLY this folder.
    clock.js         lookahead scheduler
    voices.js        THUMPER / GLOWORM / SIREN node graphs
    fx.js            CAVE send bus, drive/bitcrush shaper, master chain
  ui/              DOM. Step 2 replaces ONLY this folder.
    os.js  trax.js  knob.js  styles.css
  index.html
  serve.bat
  test/run.js      zero-dependency node harness
```

`engine/` is the contract with the future C# port. If a value affects what a
cassette sounds like, it is computed in `engine/` and nowhere else.

## 3. Determinism contract

Non-negotiable — this vector later drives printed cassettes, alien reactions and
radio playback, on every machine.

1. Dials are continuous `0–10` in the UI. For seeding they quantize to **0.5 steps →
   six ints `0–20`**.
2. Seed = **FNV-1a 32-bit** over those six bytes, in dial order
   `[PULSE, CRUNCH, GOO, VOID, JITTER, WARP]`.
3. Each voice draws from **its own stream**: `mulberry32(seed ^ VOICE_CONST)`. Adding a
   7th plugin later must not shift the drum pattern of any cassette already printed.
4. **No `Math.random()`, no `Date.now()`** anywhere in `engine/`. Enforced by test.
5. Pattern-affecting changes regenerate the 4-bar phrase and swap it in **at the next
   bar boundary, keeping the current bar index** (so it doesn't feel like it resets).
   Timbre/FX/BPM changes apply live via `setTargetAtTime` — never hard jumps.

Both algorithms are byte-exact in C#: `Math.imul(a,b)` → `unchecked(a * b)` on `int`,
all arithmetic `>>> 0` → `uint`.

## 4. Dials → parameters

Range 0–10 continuous. `p = dial/10`.

| Dial | Drives |
|---|---|
| **PULSE** | BPM `60 + p*110`; note density `0.25 + p*0.5` |
| **CRUNCH** | waveshaper drive; osc morph (sine→saw at p<0.5, saw→square above, by crossfade); amplitude quantization (bitcrush) in the shaper curve |
| **GOO** | lowpass base `400 * 2^((1-p)*3)` Hz (open→squelchy), resonance `Q = 1 + p*18`, filter LFO rate `0.2–3 Hz` and depth up to 2 octaves |
| **VOID** | CAVE send `p*0.8`, feedback `0.2 + p*0.65`; pattern sparseness `density *= (1 - p*0.5)` |
| **JITTER** | syncopation probability `p`; off-grid nudge up to 20 ms; hat scatter `p` |
| **WARP** | scale-table index, INVERTED (0 = familiar, 10 = alien); detune `p*35` cents |

Scale tables, ordered by FAMILIARITY (alien first): `chromatic-cluster [0,1,2,6,7,8]`,
`whole-tone [0,2,4,6,8,10]`, `Hirajoshi [0,2,3,7,8]`, `Phrygian [0,1,3,5,7,8,10]`,
`minor pentatonic [0,3,5,7,10]`, `natural minor [0,2,3,5,7,8,10]`. Master key fixed
(root A, MIDI 45).

**WARP is the one dial that runs backwards** — 0 is straight and melodic, 10 is
maximally warped — so it is inverted (`10 - warp`) exactly once, where the scale
index is computed, and nowhere else. The scale table itself stays alien-first.

*Note:* the handoff listed whole-tone before chromatic-cluster. Swapped so alienness
increases monotonically as WARP rises — otherwise the dial feels broken mid-sweep.
One-line change in `scales.js` if Sam disagrees (and the golden file regenerated).

## 5. Voices and patterns

| Voice | Synthesis | Pattern |
|---|---|---|
| **THUMPER** kick | sine osc, pitch drop 150→45 Hz over 80 ms, gain env 250 ms | downbeat-weighted; density adds 16ths; jitter adds off-grid hits |
| **THUMPER** snare | bandpassed noise burst (~1800 Hz) + triangle body | steps 4 and 12; ghost notes from density/jitter |
| **THUMPER** hat | highpassed noise, 40 ms (120 ms on accents) | every 1–2 steps by density; scatter drops/adds by jitter |
| **GLOWORM** bass | morphing osc → resonant lowpass → gain env, low octave | root/fifth biased, occasional scale wander |
| **SIREN** lead | morphing osc → filter → dry + CAVE send, high octave | sparse, longer notes |
| **MOSS** chords | three morphing oscs (a triad) → dark filter, 120 ms swell | ONE chord per bar, held, never cut by a fill |
| **SPINDLE** arp | morphing osc → bright filter, 4 ms pluck, capped at 220 ms | climbs the bar's chord, 8ths or 16ths by density |
| **CAVE** space | dual feedback delay (0.19 s / 0.31 s) + damping lowpass, shared send bus | n/a — an effect, but rack-toggleable |

## 5.5 Musicality (reworked 2026-08-13, after Sam played it)

Sam: *"I find the music to be a bit random… people aren't like me, they need to be
able to turn dials and create good sounding tunes without tweaking for hours."*

The first generator drew every step and every pitch as an independent coin flip.
Five changes fix that, **all under the hood — still six dials**. The structure is
what stops a non-musician being able to make something bad:

1. **Harmony.** A 4-bar chord progression from a table of 8, one chord per bar,
   on its own PRNG stream. Chords are scale-thirds (`[0,2,4]` in scale degrees),
   so they stay in-scale for every scale table and go appropriately strange on
   the alien ones. Bass plays roots, MOSS holds the triad, SPINDLE arpeggiates
   it, the lead snaps to chord tones on strong beats.
2. **Rhythm cells.** One 8-step cell per voice, tiled twice per bar, instead of
   16 independent probabilities. Repetition is what makes a groove sound meant.
   (Bonus: the snare's cell step 4 tiles onto bar steps 4 and 12 — a backbeat for
   free.)
3. **A melodic motif.** The lead is one 8-step figure that walks mostly ±1–2
   scale degrees with occasional leaps, repeated and re-harmonised per bar.
4. **Interlock.** Bass onsets are pulled toward the kick; the lead backs off
   where the snare lands.
5. **Two turnarounds.** A light 2-step fill ends bar 2, the full 4-step fill ends
   bar 4. The half-phrase one is deliberately smaller — two equal fills would
   split the phrase into two 2-bar loops and lose the longer arc.

**Bars share their RHYTHM but not their PITCH** — each bar re-harmonises against
its own chord. MOSS is the one exception to tiling: it fires once per bar and
holds, because tiling would retrigger the pad mid-bar and overlap it with itself.

**Timing note:** this rewrote every pattern. That was free only because nothing
persists yet (PRINT inert, dials unsaved). Once cassettes are savable it becomes
a breaking change — see §3.

**Rack toggles** mute at the voice's output gain with a short ramp — the pattern keeps
running underneath, so unmuting lands in time and nothing clicks.

## 6. Classifier

Ten genre centres in 6-D dial space (values from handoff §5, placeholders for tuning).
Track vector → euclidean distance to each centre → nearest wins.

⚠️ **The handoff's §5 table is superseded on its last column.** Its HOMESICK values
are the old direction; every centre's 6th coordinate was inverted to `10 - old` when
the dial became WARP. `engine/classifier.js` and `TraxClassifier.cs` are the source
of truth, and they agree.

If `d2 - d1 <= BLEND_THRESHOLD` (start **1.5**, tunable live), show a blend label:
adjective of second-nearest + noun of nearest, e.g. **"Sludjy Glorp"**. Adjectives:
Glorpy, Drifty, Skittish, Sludjy, Chirpy, Null, Thrummy, Volted, Warbly, Clangin'.
Fully deterministic; identical maths in the eventual C#.

## 7. UI

**Home screen:** fullscreen alien OS — boot/status bar, icon grid with **TRAX** live and
MAIL / BANK / RADIO greyed. Chunky retro terminal/CRT: scanlines, phosphor glow, alien
glyphs. **Draft — Sam art-directs at the gate.**

**TRAX:** six rotary knobs (drag vertically, click-to-type value), large live genre
readout, PLAY/STOP transport (functional), plugin rack — 4 working toggles + 2 visibly
LOCKED slots, PRINT button + quantity stepper (visual only), master volume.

Master output defaults to **0.5**, not 1.0 — same courtesy as `GameAudioBus`.

## 8. Testing

`node test/run.js`, zero dependencies. Covers exactly what must survive the C# port:

1. **Determinism** — same dial vector generates a byte-identical pattern twice, and
   across a fresh module load.
2. **Voice isolation** — changing the voice constant set doesn't perturb other voices'
   streams.
3. **Classifier** — every genre centre classifies as itself; blend fires at threshold
   and not below it.
4. **Scale safety** — every generated pitch is a member of the active scale table.
5. **Purity** — no `Math.random`/`Date` token appears anywhere under `engine/`.

No Unity build/test exists in this repo, so this is the only automated coverage; the
real gate is Sam's ears.

## 8.5 Status

- **Step 1 (browser prototype): DONE.** Sam played it 2026-08-13 — "working really
  well". One change requested and made: a GENRE caption over the live readout.
- **Steps 2 + 3 (Unity interaction + audio): BUILT** 2026-08-13, compile-verified,
  engine port verified bit-exact. **Not playtested — nothing has been heard in-game.**

Two deviations from §9 as written, both deliberate:

1. **The audio backend renders in `OnAudioFilterRead` rather than using
   `AudioClip.Create` + filter components.** Still "Option A" in the sense that
   matters (pure Unity, no asset purchase, ships with the game), but the
   AudioSource route cannot reproduce sample-accurate envelopes, a continuous
   osc morph, a resonant LFO-swept filter, or per-note filter state — all four
   are audible, and matching the browser was the stated bar.
2. **The UI is built in code, not authored as a prefab** — same choice
   `NewspaperReaderUI` made, and for the same reasons.

Verification that exists (`prototypes/shuttle-computer/`):
`npm run verify:port` compiles the five C# engine files standalone with zero
Unity references and runs them against golden vectors dumped from the JS engine —
600 checks over 30 dial settings, all bit-exact. `npm run verify:unity`
compile-checks both Unity assemblies without opening the Editor. Neither says
anything about how it sounds.

## 9. Steps 2 and 3 (context only — do not build)

- **Step 2:** `ConsoleScreen` in `Assets/1 - samsPrefabs/Shuttle_Lander.prefab` already
  has a BoxCollider on layer 10, so it can host an `Interactable` subclass + trigger
  zone directly. Look-at + **F** → soft-lock movement/camera, unlock cursor, fullscreen
  UGUI canvas. ESC/F exits. Reuse the existing `isInDialogue` soft-lock pattern.
  **The prefab is hand-maintained by Sam — patch via `LoadPrefabContents`, never
  regenerate.** Client-local UI, no netcode.
- **Step 3:** port `engine/` verbatim to C#; rewrite `audio/` against
  `AudioClip.Create` + `AudioSource.PlayScheduled` on the DSP clock + built-in
  `AudioLowPassFilter` / `AudioDistortionFilter` / `AudioEchoFilter` / `AudioReverbFilter`.

## 9.5 Later: full-length track recording (Sam's idea, 2026-08-13 — DO NOT BUILD YET)

Today a cassette is a **loop**. Sam wants a later mode where you hit RECORD, the
transport keeps running, and you perform the track live — twisting dials to build
tension, drop out, bring the drums back — then STOP and have a **full-length
track** rather than a demo loop. Shareable.

**Why this fits the existing architecture unusually well:** because `engine/` is
fully deterministic, a full track does not need audio recording at all. It is:

```
{ seed, durationSteps, automation: [ { step, dial, value }, ... ] }
```

Replay that against the same engine and you get the identical performance, every
time, on every machine. That means a full track is a few KB of JSON — cheap to
save in the existing save system, cheap to send over the wire in co-op, and the
per-planet radio milestone can re-render it live through the same synth instead
of streaming audio. Rendering to an actual waveform is then only needed if tracks
ever leave the game.

**Two implementation notes to protect now, at zero cost:**

1. **Stamp automation events with the global step index, not wall-clock seconds.**
   Pattern-affecting dials only take effect on bar boundaries (§3.5), so a
   seconds-stamped event replayed at a different BPM lands in a different bar and
   the performance drifts. Step index is BPM-independent and exact.
2. **Keep every dial change going through one choke point.** In the prototype
   that is `Instrument.setDials()`; the Unity port must mirror that rather than
   letting UI widgets write dial state directly. Recording then becomes "log
   what passes through this function", and nothing else has to change.

Open questions for when it's built: what genre label a track gets when the vector
moves during the performance (dominant? modal? a sequence?), whether recording is
free-form or quantized to bars, and whether the player can punch in/out.

## 10. Open (Sam decides at the gate)

- App name (TRAX is a placeholder), genre names and centres, dial names, home-screen
  app list, the whole visual direction.
- `BLEND_THRESHOLD` value.
- WARP scale ordering (see §4 note).
