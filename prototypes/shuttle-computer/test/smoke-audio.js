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
for (const key of ['pulse', 'crunch', 'goo', 'void', 'jitter', 'homesick'])
    for (let v = 0; v <= 10; v += 2.5)
        presets.push (Object.assign ({}, DEFAULT_DIALS, { [key]: v }));
// The extremes, where clipping and divide-by-zero live.
presets.push ({ pulse: 0, crunch: 0, goo: 0, void: 0, jitter: 0, homesick: 0 });
presets.push ({ pulse: 10, crunch: 10, goo: 10, void: 10, jitter: 10, homesick: 10 });
presets.push ({ pulse: 10, crunch: 10, goo: 0, void: 10, jitter: 10, homesick: 0 });
presets.push ({ pulse: 0, crunch: 10, goo: 10, void: 0, jitter: 0, homesick: 10 });

let scheduled = 0;
let t = 0.1;

for (const preset of presets) {
    inst.setDials (preset);
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

// Every rack combination, so no module toggle path throws.
let combos = 0;
for (let mask = 0; mask < 16; mask++) {
    const names = MODULES.filter (m => !m.locked).map (m => m.name);
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
for (const m of MODULES) if (!m.locked) inst.setModuleEnabled (m.name, true);
log ('exercised ' + combos + ' rack combinations');

// Pattern swaps must land on a bar line, never mid-bar.
inst.clock.timer = 1;                                  // pretend the transport is running
const before = JSON.stringify (inst.patterns);
inst.setDials (Object.assign ({}, DEFAULT_DIALS, { jitter: 9.5, pulse: 9 }));
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
inst.setDials (Object.assign ({}, inst.dials, { crunch: 10, goo: 9 }));
if (JSON.stringify (inst.patterns) !== held || inst.pending) bad ('CRUNCH/GOO should be timbre-only');
log ('timbre dials leave the pattern untouched');

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
log ('master volume + CAVE mute paths ok');

log ('\n' + '-'.repeat (52));
log ('audio smoke: PASS   (' + stats.checks + ' parameter assertions, ' + ctx.nodeCount + ' nodes created)');
if (problems.length) { console.error (problems); process.exit (1); }
