// Zero-dependency test harness.  node test/run.js
//
// Covers exactly what has to survive the C# port. Everything else about this
// prototype is judged by ear; these are the things ears can't check.

import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { fnv1a32, mulberry32, quantizeDials, seedFromDials, streamFor, VOICE_CONST } from '../engine/prng.js';
import { SCALES, ROOT_MIDI, VOICE_OCTAVE, scaleIndexFor, degreeToMidi, isInScale, midiToFreq } from '../engine/scales.js';
import { computeParams, DEFAULT_DIALS, needsRegen } from '../engine/params.js';
import { generatePatterns, stepAt, VOICES, BARS, STEPS, TOTAL_STEPS, FILL_BAR, FILL_START } from '../engine/patterns.js';
import { classify, GENRES, DEFAULT_BLEND_THRESHOLD } from '../engine/classifier.js';

const HERE = dirname (fileURLToPath (import.meta.url));

let passed = 0, failed = 0;
const failures = [];

function test (name, fn) {
    try { fn (); passed++; console.log ('  ok   ' + name); }
    catch (e) { failed++; failures.push ([name, e.message]); console.log ('  FAIL ' + name + '\n       ' + e.message); }
}
function assert (cond, msg) { if (!cond) throw new Error (msg || 'assertion failed'); }
function eq (a, b, msg) {
    if (a !== b) throw new Error ((msg || 'not equal') + ': got ' + a + ', want ' + b);
}
function deepEq (a, b, msg) {
    const sa = JSON.stringify (a), sb = JSON.stringify (b);
    if (sa !== sb) throw new Error ((msg || 'not deep-equal') + '\n       got  ' + sa.slice (0, 200) + '\n       want ' + sb.slice (0, 200));
}
function section (s) { console.log ('\n' + s); }

const dialsOf = o => Object.assign ({}, DEFAULT_DIALS, o);

// ---------------------------------------------------------------- PRNG ----
section ('PRNG + seeding');

test ('fnv1a32 matches the reference vector for "a"', () => {
    // Canonical FNV-1a 32-bit test vectors. If these break, the C# port and the
    // JS prototype will disagree about what a cassette sounds like.
    eq (fnv1a32 ([0x61]), 0xe40c292c);
    eq (fnv1a32 ([0x61, 0x62, 0x63]), 0x1a47e90b);
    eq (fnv1a32 ([]), 0x811c9dc5);
});

test ('mulberry32 is stable and in range', () => {
    const r = mulberry32 (12345);
    const first = [r (), r (), r (), r ()];
    for (const v of first) assert (v >= 0 && v < 1, 'out of range: ' + v);
    const r2 = mulberry32 (12345);
    deepEq ([r2 (), r2 (), r2 (), r2 ()], first, 'same seed must replay');
});

test ('dials quantize to 0.5 steps and clamp to 0..20', () => {
    deepEq (quantizeDials ({ pulse: 0, crunch: 10, goo: 5, void: 2.4, jitter: 2.3, warp: 7.5 }),
            [0, 20, 10, 5, 5, 15]);
    deepEq (quantizeDials ({ pulse: -3, crunch: 99, goo: 0, void: 0, jitter: 0, warp: 0 })[0], 0);
    eq (quantizeDials ({ pulse: 99, crunch: 0, goo: 0, void: 0, jitter: 0, warp: 0 })[0], 20);
});

test ('sub-quantum dial wiggle does not change the seed', () => {
    eq (seedFromDials (dialsOf ({ goo: 5.0 })), seedFromDials (dialsOf ({ goo: 5.2 })));
    assert (seedFromDials (dialsOf ({ goo: 5.0 })) !== seedFromDials (dialsOf ({ goo: 5.5 })),
            'a half-step move must reseed');
});

test ('voice constants are distinct', () => {
    const vals = Object.values (VOICE_CONST);
    eq (new Set (vals).size, vals.length, 'duplicate voice constant');
});

// ------------------------------------------------------------ DETERMINISM --
section ('Determinism');

test ('same dials generate a byte-identical phrase, twice', () => {
    const d = dialsOf ({ pulse: 7, crunch: 4, goo: 8, void: 2, jitter: 6, warp: 3 });
    const a = generatePatterns (seedFromDials (d), computeParams (d));
    const b = generatePatterns (seedFromDials (d), computeParams (d));
    deepEq (a, b);
});

test ('different dials generate different phrases', () => {
    const d1 = dialsOf ({ pulse: 2 }), d2 = dialsOf ({ pulse: 9 });
    const a = generatePatterns (seedFromDials (d1), computeParams (d1));
    const b = generatePatterns (seedFromDials (d2), computeParams (d2));
    assert (JSON.stringify (a) !== JSON.stringify (b), 'phrases collided');
});

test ('voice streams are independent of the voice list length', () => {
    // Proves a future 7th plugin cannot shift an existing voice's pattern:
    // each stream is derived from its own constant, not from an index.
    const d = dialsOf ({});
    const seed = seedFromDials (d);
    const a = [], b = [];
    const ra = streamFor (seed, 'kick'), rb = streamFor (seed, 'kick');
    const noise = streamFor (seed, 'lead');
    for (let i = 0; i < 8; i++) { a.push (ra ()); noise (); noise (); b.push (rb ()); }
    deepEq (a, b, 'draining another voice perturbed this one');
});

test ('phrase shape is 4 bars x 16 steps for every voice', () => {
    const d = dialsOf ({});
    const pat = generatePatterns (seedFromDials (d), computeParams (d));
    for (const v of VOICES) {
        eq (pat[v].length, BARS, v + ' bar count');
        for (const bar of pat[v]) eq (bar.length, STEPS, v + ' step count');
    }
});

test ('bars 0-2 are identical; bar 3 differs only in its last 4 steps', () => {
    const d = dialsOf ({ pulse: 8, jitter: 5 });
    const pat = generatePatterns (seedFromDials (d), computeParams (d));
    let anyFillDiff = false;
    for (const v of VOICES) {
        deepEq (pat[v][0], pat[v][1], v + ' bar0 vs bar1');
        deepEq (pat[v][0], pat[v][2], v + ' bar1 vs bar2');
        for (let s = 0; s < FILL_START; s++)
            deepEq (pat[v][FILL_BAR][s], pat[v][0][s], v + ' fill bar leaked into step ' + s);
        for (let s = FILL_START; s < STEPS; s++)
            if (JSON.stringify (pat[v][FILL_BAR][s]) !== JSON.stringify (pat[v][0][s])) anyFillDiff = true;
    }
    assert (anyFillDiff, 'the fill changed nothing at all — bar 3 is a plain repeat');
});

test ('stepAt wraps across the phrase in both directions', () => {
    const d = dialsOf ({});
    const pat = generatePatterns (seedFromDials (d), computeParams (d));
    deepEq (stepAt (pat, 'kick', 0), stepAt (pat, 'kick', TOTAL_STEPS));
    deepEq (stepAt (pat, 'kick', 3), stepAt (pat, 'kick', TOTAL_STEPS * 5 + 3));
    deepEq (stepAt (pat, 'kick', TOTAL_STEPS - 1), stepAt (pat, 'kick', -1));
});

// ---------------------------------------------------------------- SCALES --
section ('Pitch safety');

test ('familiarity sweeps the scale table monotonically, ends inclusive', () => {
    eq (scaleIndexFor (0), 0);
    eq (scaleIndexFor (10), SCALES.length - 1);
    let prev = -1;
    for (let h = 0; h <= 10; h += 0.25) {
        const i = scaleIndexFor (h);
        assert (i >= prev, 'scale index went backwards at familiarity=' + h);
        prev = i;
    }
});

test ('every scale degree lands in-scale, including negatives and wraps', () => {
    for (let si = 0; si < SCALES.length; si++)
        for (let deg = -14; deg <= 14; deg++)
            assert (isInScale (degreeToMidi (deg, si, 0), si),
                    'scale ' + SCALES[si].name + ' degree ' + deg + ' -> ' + degreeToMidi (deg, si, 0));
});

test ('degrees rise monotonically and octaves are exactly 12 semitones', () => {
    for (let si = 0; si < SCALES.length; si++) {
        const n = SCALES[si].steps.length;
        for (let deg = -8; deg < 8; deg++)
            assert (degreeToMidi (deg + 1, si, 0) > degreeToMidi (deg, si, 0), 'not monotonic');
        eq (degreeToMidi (n, si, 0) - degreeToMidi (0, si, 0), 12, 'octave wrap');
        eq (degreeToMidi (0, si, 1) - degreeToMidi (0, si, 0), 12, 'octave offset');
    }
});

test ('every pitch any voice can play is in the active scale, across the dial space', () => {
    for (let h = 0; h <= 10; h += 1)
        for (let p = 0; p <= 10; p += 2.5) {
            const d = dialsOf ({ warp: h, pulse: p, void: 0 });
            const params = computeParams (d);
            const pat = generatePatterns (seedFromDials (d), params);
            for (const v of ['bass', 'lead'])
                for (const bar of pat[v])
                    for (const st of bar) {
                        if (!st) continue;
                        assert (isInScale (degreeToMidi (st.degree, params.scaleIdx, VOICE_OCTAVE[v]), params.scaleIdx),
                                v + ' played out of scale at warp=' + h);
                    }
        }
});

test ('audible frequency range: bass floor and lead ceiling stay sane', () => {
    // Walks the actual pitches the generator can emit (the degree pools' extremes)
    // at each voice's real octave, across every scale.
    for (let si = 0; si < SCALES.length; si++) {
        const lowest = midiToFreq (degreeToMidi (-3, si, VOICE_OCTAVE.bass));
        const highest = midiToFreq (degreeToMidi (7, si, VOICE_OCTAVE.lead));
        assert (lowest > 30, 'bass below usable range at scale ' + si + ': ' + lowest.toFixed (1) + 'Hz');
        assert (highest < 2000, 'lead shrill at scale ' + si + ': ' + highest.toFixed (1) + 'Hz');
    }
});

// ---------------------------------------------------------------- PARAMS --
section ('Params');

test ('PULSE maps to the documented BPM range', () => {
    eq (Math.round (computeParams (dialsOf ({ pulse: 0 })).bpm), 60);
    eq (Math.round (computeParams (dialsOf ({ pulse: 10 })).bpm), 170);
});

test ('GOO closes the filter and adds resonance', () => {
    const open = computeParams (dialsOf ({ goo: 0 })), shut = computeParams (dialsOf ({ goo: 10 }));
    assert (open.filterBase > shut.filterBase, 'GOO should close the filter');
    assert (shut.filterQ > open.filterQ, 'GOO should add resonance');
    eq (Math.round (shut.filterBase), 400);
    eq (Math.round (open.filterBase), 3200);
});

test ('VOID thins the pattern and opens the CAVE', () => {
    const dry = computeParams (dialsOf ({ void: 0 })), wet = computeParams (dialsOf ({ void: 10 }));
    assert (wet.density < dry.density, 'VOID should thin density');
    assert (wet.caveSend > dry.caveSend && wet.caveFeedback > dry.caveFeedback, 'VOID should open CAVE');
    assert (wet.caveFeedback < 1, 'CAVE feedback must stay below unity or it runs away');
});

test ('WARP adds detune as it rises, and goes alien at the top', () => {
    // WARP is the one dial that runs the other way: 0 is straight, 10 is warped.
    assert (computeParams (dialsOf ({ warp: 10 })).detuneCents >
            computeParams (dialsOf ({ warp: 0 })).detuneCents, 'warped should be more detuned');
    eq (computeParams (dialsOf ({ warp: 0 })).detuneCents, 0);
    // ...and it must reach both ends of the scale table, inverted.
    eq (computeParams (dialsOf ({ warp: 0 })).scaleIdx, SCALES.length - 1);
    eq (computeParams (dialsOf ({ warp: 10 })).scaleIdx, 0);
});

test ('density stays positive across the whole dial space', () => {
    for (let p = 0; p <= 10; p += 0.5)
        for (let v = 0; v <= 10; v += 0.5) {
            const d = computeParams (dialsOf ({ pulse: p, void: v })).density;
            assert (d > 0 && d <= 1, 'density out of range at pulse=' + p + ' void=' + v + ': ' + d);
        }
});

test ('needsRegen fires on pattern dials only', () => {
    assert (needsRegen (dialsOf ({ jitter: 2 }), dialsOf ({ jitter: 8 })), 'JITTER must regen');
    assert (needsRegen (dialsOf ({ pulse: 2 }), dialsOf ({ pulse: 8 })), 'PULSE must regen');
    assert (!needsRegen (dialsOf ({ crunch: 0 }), dialsOf ({ crunch: 10 })), 'CRUNCH is timbre — no regen');
    assert (!needsRegen (dialsOf ({ goo: 0 }), dialsOf ({ goo: 10 })), 'GOO is timbre — no regen');
    assert (!needsRegen (dialsOf ({ jitter: 5.0 }), dialsOf ({ jitter: 5.2 })), 'sub-quantum must not regen');
});

// ------------------------------------------------------------ CLASSIFIER --
section ('Classifier');

test ('every genre centre classifies as itself', () => {
    for (const g of GENRES) {
        const dials = { pulse: g.c[0], crunch: g.c[1], goo: g.c[2], void: g.c[3], jitter: g.c[4], warp: g.c[5] };
        eq (classify (dials).primary.name, g.name, 'centre of ' + g.name + ' misclassified');
    }
});

test ('a point midway between two centres reports a blend', () => {
    const a = GENRES[0].c, b = GENRES[8].c;      // GLORP <-> WARBLE
    const mid = a.map ((v, i) => (v + b[i]) / 2);
    const r = classify ({ pulse: mid[0], crunch: mid[1], goo: mid[2], void: mid[3], jitter: mid[4], warp: mid[5] });
    assert (r.blended, 'midpoint should blend, d1=' + r.d1.toFixed (2) + ' d2=' + r.d2.toFixed (2));
    assert (r.label.indexOf (' ') > 0, 'blend label should be two words, got ' + r.label);
});

test ('a genre centre far from its neighbours reports a single word', () => {
    // DRIFT sits alone in the corner of the space; at its exact centre nothing
    // else should be within the threshold.
    const g = GENRES[1];
    const r = classify ({ pulse: g.c[0], crunch: g.c[1], goo: g.c[2], void: g.c[3], jitter: g.c[4], warp: g.c[5] });
    eq (r.label, 'DRIFT');
    assert (!r.blended, 'DRIFT centre should not blend (runner-up at ' + r.d2.toFixed (2) + ')');
});

test ('threshold is a margin over the winner, not an absolute distance', () => {
    const d = dialsOf ({});
    assert (classify (d, 0).blended === false, 'zero margin must never blend');
    assert (classify (d, 99).blended === true, 'huge margin must always blend');
});

test ('classification is stable and never returns the same genre twice', () => {
    for (let i = 0; i < 10; i++)
        for (let j = 0; j < 10; j++) {
            const d = dialsOf ({ pulse: i, goo: j, crunch: (i + j) % 11 });
            const r = classify (d);
            assert (r.primary.name !== r.secondary.name, 'primary == secondary');
            deepEq (classify (d).label, r.label, 'classification not stable');
        }
});

// ---------------------------------------------------------------- PURITY --
section ('Engine purity');

test ('engine/ contains no unseeded randomness or wall-clock reads', () => {
    const dir = join (HERE, '..', 'engine');
    const banned = /Math\.random|Date\.now|new Date|performance\.now/;
    for (const f of readdirSync (dir)) {
        if (!f.endsWith ('.js')) continue;
        const src = readFileSync (join (dir, f), 'utf8');
        // Strip comments — the ban is on code, and the files explain the ban.
        const code = src.replace (/\/\*[\s\S]*?\*\//g, '').replace (/^\s*\/\/.*$/gm, '');
        const m = code.match (banned);
        assert (!m, 'engine/' + f + ' uses ' + (m && m[0]));
    }
});

test ('engine/ imports nothing outside engine/', () => {
    const dir = join (HERE, '..', 'engine');
    for (const f of readdirSync (dir)) {
        if (!f.endsWith ('.js')) continue;
        const src = readFileSync (join (dir, f), 'utf8');
        const re = /from\s+['"]([^'"]+)['"]/g;
        let m;
        while ((m = re.exec (src))) {
            assert (m[1].startsWith ('./'), 'engine/' + f + ' imports ' + m[1] + ' — engine must stay portable');
        }
    }
});

// ----------------------------------------------------------------- REPORT --
console.log ('\n' + '-'.repeat (52));
console.log (passed + ' passed, ' + failed + ' failed');
if (failed) {
    console.log ('\nfailures:');
    for (const [n, m] of failures) console.log ('  ' + n + '\n    ' + m);
}
process.exit (failed ? 1 : 0);
