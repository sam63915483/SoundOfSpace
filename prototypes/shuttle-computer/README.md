# Shuttle computer — TRAX prototype

Browser prototype of the cassette-pivot music instrument. **Step 1** of
`docs/Handoff_CassettePivot_ShuttleComputer_v2.md`; design spec at
`docs/superpowers/specs/2026-08-13-shuttle-computer-trax-design.md`.

This is the real instrument, prototyped in the browser because audio iteration is
fastest there. All sound is synthesized live — no audio files, no samples.

## Run it

Double-click **`serve.bat`** (or `python -m http.server 8080` in this folder),
then open <http://localhost:8080/>.

It has to be served over http — ES modules don't load from `file://`.

## Using it

Boot screen → click/press any key to skip → **TRAX**.

| Control | |
|---|---|
| Knobs | drag up/down. **Shift** = fine. Wheel works. Double-click resets. Arrow keys / Home / End when focused. |
| **PLAY** | or **Space** |
| Plugin rack | click a module to mute/unmute it — the pattern keeps running underneath, so it drops back in time |
| **ESC** | back to the home screen |
| PRINT | deliberately does nothing yet (Step 4) |

`window.TRAX` is the live instrument in the console, if you want to poke at it.

## What to listen for at the review gate

- Does each dial do what its name says, across its whole range?
- Does the genre label match what your ear says? (centres are in
  `engine/classifier.js`, one table)
- Is the 4-bar turnaround audible without being annoying?
- Does HOMESICK feel like a smooth alien→human sweep, or does it lurch?

Everything visual is a draft — colours are all tokens at the top of
`ui/styles.css`.

## Layout

```
engine/   PURE LOGIC — ports 1:1 to C#. No Web Audio, no DOM, no randomness
          outside the seeded PRNG. This is the contract with the Unity port.
audio/    Web Audio backend. Step 3 replaces ONLY this folder.
ui/       DOM. Step 2 replaces ONLY this folder (UGUI).
test/     node, zero dependencies.
```

Same dials always produce the same loop, on every machine — the dial vector is
hashed to a seed, and each voice draws from its own stream so unlocking a new
plugin later can't change what an already-printed cassette sounds like.

## Tests

```
npm test          # all three suites
```

- `test/run.js` — the engine maths that must survive the C# port: determinism,
  scale safety, classifier, purity.
- `test/smoke-audio.js` — drives the synth across the dial space against a mock
  Web Audio graph that's stricter than a browser.
- `test/smoke-ui.js` — boots the OS shell against a mock DOM and works every
  control.

None of it can tell you whether it sounds good. That part's yours.
