// Headless smoke test for the audio layer.  node test/smoke-audio.js
//
// A mock Web Audio graph that is STRICTER than a real browser: it throws on the
// mistakes that silently kill an instrument (exponential ramps to zero, NaN or
// negative frequencies, buffer offsets past the end, connecting to nothing).
// Drives the instrument across the whole dial space and every rack combination.
//
// This does not prove it sounds good — only that it runs. The ears are Sam's.

import { Instrument, MODULES } from '../audio/instrument.js';
import { DEFAULT_DIALS } from '../engine/params.js';
import { STEPS } from '../engine/patterns.js';
import * as TRACK from '../engine/track.js';
import * as PRESETS from '../engine/presets.js';
import { FakeAudioContext, stats, problems, bad } from './mock-webaudio.js';


global.window = { AudioContext: FakeAudioContext };

// ------------------------------------------------------------------ run ----

function log (s) { console.log (s); }

const inst = new Instrument ();
await inst.init ();
const ctx = inst.ctx;

log ('rack built: ' + ctx.nodeCount + ' nodes');

// Sweep the dial space and schedule a full phrase at each corner plus a spread
// of interior points. 5 values per dial would be 15625 combos — instead walk
// each dial across its range against a moving background, which is what a
// person actually does to an instrument.
const presets = [];
for (const key of ['pulse', 'crunch', 'goo', 'void', 'jitter', 'warp'])
    for (let v = 0; v <= 10; v += 2.5)
        presets.push (Object.assign ({}, DEFAULT_DIALS, { [key]: v }));
// The extremes, where clipping and divide-by-zero live.
presets.push ({ pulse: 0, crunch: 0, goo: 0, void: 0, jitter: 0, warp: 0 });
presets.push ({ pulse: 10, crunch: 10, goo: 10, void: 10, jitter: 10, warp: 10 });
presets.push ({ pulse: 10, crunch: 10, goo: 0, void: 10, jitter: 10, warp: 0 });
presets.push ({ pulse: 0, crunch: 10, goo: 10, void: 0, jitter: 0, warp: 10 });

let scheduled = 0;
let t = 0.1;

for (const preset of presets) {
    // NB: not `t` — that is the running schedule time in this file, and
    // shadowing it silently passes a track object where a timestamp goes.
    const tr = TRACK.cloneTrack (inst.track);
    tr.dials = preset;
    inst.setTrack (tr);
    const stepDur = 60 / inst.params.bpm / 4;
    for (let step = 0; step < 64; step++) {
        ctx.currentTime = t - 0.1 < 0 ? 0 : t - 0.1;
        inst._schedule (step, t, stepDur);
        ctx.flushEnded ();
        t += stepDur;
        scheduled++;
    }
}
log ('scheduled ' + scheduled + ' steps across ' + presets.length + ' dial settings');

// Every part choice, so no preset or variation path throws.
let parts = 0;
for (const m of PRESETS.MODULE_NAMES)
    for (let pi = 0; pi < PRESETS.PRESET_COUNT; pi++)
        for (let vi = 0; vi < PRESETS.VARIATION_COUNT; vi++) {
            inst.setPreset (m, pi);
            inst.setVariation (m, vi);
            const stepDur = 60 / inst.params.bpm / 4;
            for (let step = 0; step < STEPS; step++) {
                ctx.currentTime = t - 0.1;
                inst._schedule (step, t, stepDur);
                ctx.flushEnded ();
                t += stepDur;
            }
            parts++;
        }
log ('exercised ' + parts + ' preset/variation combinations');

// Every key, since key changes pitch on live voices.
for (let k = 0; k < 12; k++) {
    inst.setKey (k);
    const stepDur = 60 / inst.params.bpm / 4;
    for (let step = 0; step < STEPS; step++) {
        ctx.currentTime = t - 0.1;
        inst._schedule (step, t, stepDur);
        ctx.flushEnded ();
        t += stepDur;
    }
}
inst.setKey (0);
log ('exercised all 12 keys');

// Every rack combination, so no module toggle path throws.
let combos = 0;
for (let mask = 0; mask < 16; mask++) {
    const names = MODULES.map (m => m.name);
    names.forEach ((n, i) => inst.setModuleEnabled (n, (mask >> i) & 1 ? true : false));
    const stepDur = 60 / inst.params.bpm / 4;
    for (let step = 0; step < STEPS; step++) {
        ctx.currentTime = t - 0.1;
        inst._schedule (step, t, stepDur);
        ctx.flushEnded ();
        t += stepDur;
    }
    combos++;
}
for (const m of MODULES) inst.setModuleEnabled (m.name, true);
log ('exercised ' + combos + ' rack combinations');

// Pattern swaps must land on a bar line, never mid-bar.
inst.clock.timer = 1;                                  // pretend the transport is running
const before = JSON.stringify (inst.patterns);
inst.setVariation ('THUMPER', 5);
if (!inst.pending) bad ('a pattern-affecting dial move while playing did not queue a swap');
if (JSON.stringify (inst.patterns) !== before) bad ('pattern swapped mid-bar');
inst._schedule (5, t, 0.1);                            // mid-bar: must NOT swap
if (JSON.stringify (inst.patterns) !== before) bad ('pattern swapped on a non-bar step');
inst._schedule (STEPS * 3, t, 0.1);                    // bar line: must swap
if (inst.pending) bad ('pattern did not swap on the bar line');
if (JSON.stringify (inst.patterns) === before) bad ('swap left the old pattern in place');
inst.clock.timer = null;
log ('bar-boundary swap: deferred mid-bar, applied on the bar line');

// A timbre-only move must not disturb the pattern at all.
const held = JSON.stringify (inst.patterns);
inst.setDial ('crunch', 10);
inst.setDial ('goo', 9);
if (JSON.stringify (inst.patterns) !== held || inst.pending) bad ('CRUNCH/GOO should be timbre-only');
log ('timbre dials leave the pattern untouched');

// ...and neither must the key, which is applied at note time.
inst.setKey (7);
if (JSON.stringify (inst.patterns) !== held || inst.pending) bad ('KEY must not regenerate');
inst.setKey (0);
log ('key changes leave the pattern untouched');

// Node hygiene: a long session must not grow the graph without bound. Every
// tonal note wires its filter to the shared LFO, so if the unwire-on-ended is
// wrong the fan-out climbs forever and the tab dies after a few minutes.
const beforeConn = inst.rack.lfoDepth.out.length;
for (let step = 0; step < 2000; step++) {
    ctx.currentTime = t - 0.1;
    inst._schedule (step, t, 0.05);
    ctx.flushEnded ();
    t += 0.05;
}
const afterConn = inst.rack.lfoDepth.out.length;
if (afterConn > beforeConn) {
    bad ('LFO fan-out grew ' + beforeConn + ' -> ' + afterConn + ' over 2000 steps — ' +
         'tonal voices are not unwiring from the shared LFO on ended');
}
stats.checks++;
log ('lfo fan-out stable across 2000 steps (' + beforeConn + ' -> ' + afterConn + ')');

// Master volume + CAVE mute paths.
inst.setMasterVolume (0);
inst.setMasterVolume (1);
inst.setMasterVolume (0.5);
inst.setModuleEnabled ('CAVE', false);
inst.setModuleEnabled ('CAVE', true);
for (let i = 0; i < PRESETS.PRESET_COUNT; i++) inst.setPreset ('CAVE', i);
log ('master volume + CAVE mute paths ok');

log ('\n' + '-'.repeat (52));
log ('audio smoke: PASS   (' + stats.checks + ' parameter assertions, ' + ctx.nodeCount + ' nodes created)');
if (problems.length) { console.error (problems); process.exit (1); }
