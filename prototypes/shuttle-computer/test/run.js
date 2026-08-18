// Zero-dependency test harness.  node test/run.js
//
// Covers what has to survive the C# port, plus the properties the instrument's
// FEEL depends on — "a dial shapes, it doesn't re-roll" is a testable claim,
// and it is the whole reason the track model exists.

import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { fnv1a32, mulberry32, quantizeDials, VOICE_CONST } from '../engine/prng.js';
import { SCALES, VOICE_OCTAVE, VOICE_RANGE, scaleIndexFor, degreeToMidi, degreeToFreq,
         voiceMidi, voiceFreq, isInScale } from '../engine/scales.js';
import { computeParams, DEFAULT_DIALS } from '../engine/params.js';
import { generatePatterns, stepAt, progressionFor, chordTonesFor, VOICES, MELODIC,
         BARS, STEPS, TOTAL_STEPS, FULL_FILL_BAR, FULL_FILL_START,
         HALF_FILL_BAR, HALF_FILL_START } from '../engine/patterns.js';
import { classify, GENRES } from '../engine/classifier.js';
import * as TRACK from '../engine/track.js';
import * as PRESETS from '../engine/presets.js';
import * as SONGMOD from '../engine/song.js';

const HERE = dirname (fileURLToPath (import.meta.url));

let passed = 0, failed = 0;
const failures = [];

function test (name, fn) {
    try { fn (); passed++; console.log ('  ok   ' + name); }
    catch (e) { failed++; failures.push ([name, e.message]); console.log ('  FAIL ' + name + '\n       ' + e.message); }
}
function assert (c, m) { if (!c) throw new Error (m || 'assertion failed'); }
function eq (a, b, m) { if (a !== b) throw new Error ((m || 'not equal') + ': got ' + a + ', want ' + b); }
function deepEq (a, b, m) {
    const sa = JSON.stringify (a), sb = JSON.stringify (b);
    if (sa !== sb) throw new Error ((m || 'not deep-equal') + '\n       got  ' + sa.slice (0, 220) + '\n       want ' + sb.slice (0, 220));
}
function section (s) { console.log ('\n' + s); }

const T = () => TRACK.defaultTrack ();
const gen = (t) => generatePatterns (t, computeParams (t.dials, t.key));
const withDial = (t, k, v) => { const c = TRACK.cloneTrack (t); c.dials[k] = v; return c; };
const dialsOf = (o) => Object.assign ({}, DEFAULT_DIALS, o);

// ---------------------------------------------------------------- PRNG ----
section ('PRNG + hashing');

test ('fnv1a32 matches the reference vectors', () => {
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
});

test ('voice constants are distinct', () => {
    const vals = Object.values (VOICE_CONST);
    eq (new Set (vals).size, vals.length, 'duplicate voice constant');
});

// -------------------------------------------------------------- THE FEEL --
section ('Dials shape, variation re-rolls');

test ('a dial change mostly fills a groove in rather than replacing it', () => {
    // The property the track rework exists to guarantee. Some steps SHOULD
    // change — that is the dial working — but wholesale replacement is the
    // re-rolling behaviour we deliberately removed.
    const base = T ();
    const onsets = (t) => {
        const pat = gen (t);
        const out = {};
        for (const v of VOICES) out[v] = pat[v][0].map (s => s !== null);
        return out;
    };
    const a = onsets (base);
    let moved = 0, total = 0;
    for (const dial of ['pulse', 'jitter', 'void'])
        for (const val of [0, 2.5, 7.5, 10]) {
            const b = onsets (withDial (base, dial, val));
            for (const v of VOICES)
                for (let s = 0; s < STEPS; s++) { total++; if (a[v][s] !== b[v][s]) moved++; }
        }
    assert (moved / total < 0.35,
            'dials are re-rolling, not shaping: ' + (100 * moved / total).toFixed (0) + '% of steps moved');
});

test ('a weight-3 core hit can never be switched off by a dial', () => {
    const base = T ();
    for (let pi = 0; pi < PRESETS.PRESET_COUNT; pi++) {
        const t0 = TRACK.setPreset (base, 'THUMPER', pi);
        const w = PRESETS.parseWeights (PRESETS.THUMPER[pi].kick);
        for (const val of [0, 5, 10]) {
            const pat = gen (withDial (t0, 'pulse', val));
            for (let s = 0; s < STEPS; s++)
                if (w[s] >= 3)
                    assert (pat.kick[0][s] !== null,
                            PRESETS.THUMPER[pi].name + ' lost its core kick at step ' + s + ', PULSE ' + val);
        }
    }
});

test ('changing VARIATION re-rolls that module, and only that module', () => {
    const a = T ();
    const b = TRACK.setVariation (a, 'THUMPER', 3);
    const pa = gen (a), pb = gen (b);
    assert (JSON.stringify (pa.kick) !== JSON.stringify (pb.kick), 'variation did nothing to the drums');
    for (const v of ['bass', 'lead', 'spindle'])
        deepEq (pa[v], pb[v], 'THUMPER variation disturbed ' + v);
});

test ('changing a PRESET changes that module and leaves the others alone', () => {
    const a = T ();
    const b = TRACK.setPreset (a, 'GLOWORM', 2);
    const pa = gen (a), pb = gen (b);
    assert (JSON.stringify (pa.bass) !== JSON.stringify (pb.bass), 'bass preset did nothing');
    for (const v of ['kick', 'snare', 'hat', 'spindle'])
        deepEq (pa[v], pb[v], 'GLOWORM preset disturbed ' + v);
});

test ('MOSS preset re-harmonises everything, because it IS the progression', () => {
    const a = T ();
    const b = TRACK.setPreset (a, 'MOSS', 2);
    deepEq (progressionFor (b), PRESETS.MOSS[2].prog);
    let differs = false;
    const pa = gen (a), pb = gen (b);
    for (let s = 0; s < STEPS; s++)
        if (JSON.stringify (pa.bass[1][s]) !== JSON.stringify (pb.bass[1][s])) differs = true;
    assert (differs, 'changing the progression left the bass unchanged');
});

test ('changing KEY changes no pattern at all', () => {
    const a = T ();
    for (let k = 0; k < 12; k++) deepEq (gen (TRACK.setKey (a, k)), gen (a), 'key ' + k + ' regenerated');
});

test ('key transposes pitch by exactly the right number of semitones', () => {
    const base = degreeToFreq (0, 3, 0, 0);
    for (let k = 0; k < 12; k++) {
        const semis = Math.round (12 * Math.log2 (degreeToFreq (0, 3, 0, k) / base));
        eq (semis, k, 'key ' + k + ' is off');
    }
});

test ('needsRegen fires for presets, variations and shaping dials only', () => {
    const a = T ();
    assert (TRACK.needsRegen (a, TRACK.setPreset (a, 'SIREN', 3)), 'preset must regen');
    assert (TRACK.needsRegen (a, TRACK.setVariation (a, 'SIREN', 3)), 'variation must regen');
    assert (TRACK.needsRegen (a, withDial (a, 'pulse', 9)), 'PULSE must regen');
    assert (!TRACK.needsRegen (a, TRACK.setKey (a, 5)), 'key must NOT regen');
    assert (!TRACK.needsRegen (a, withDial (a, 'crunch', 10)), 'CRUNCH is timbre');
    assert (!TRACK.needsRegen (a, withDial (a, 'goo', 10)), 'GOO is timbre');
});

test ('trackId covers everything that affects the sound', () => {
    const a = T ();
    const ids = new Set ([TRACK.trackId (a)]);
    ids.add (TRACK.trackId (withDial (a, 'pulse', 8)));
    ids.add (TRACK.trackId (TRACK.setKey (a, 4)));
    ids.add (TRACK.trackId (TRACK.setPreset (a, 'MOSS', 3)));
    ids.add (TRACK.trackId (TRACK.setVariation (a, 'MOSS', 3)));
    eq (ids.size, 5, 'trackId is blind to something that changes the music');
    eq (TRACK.trackId (a), TRACK.trackId (T ()), 'trackId is not stable');
});

// ------------------------------------------------------------- STRUCTURE --
section ('Structure');

test ('same track generates a byte-identical phrase, twice', () => {
    const t = T ();
    deepEq (gen (t), gen (t));
});

test ('phrase shape is 4 bars x 16 steps for every voice', () => {
    const pat = gen (T ());
    for (const v of VOICES) {
        eq (pat[v].length, BARS, v + ' bar count');
        for (const bar of pat[v]) eq (bar.length, STEPS, v + ' step count');
    }
});

test ('every bar shares one rhythm; only the pitches follow the chord', () => {
    const t = TRACK.setPreset (T (), 'SIREN', 1);   // a preset that plays in every bar
    const pat = gen (t);
    for (const v of VOICES)
        for (let b = 1; b < BARS; b++) {
            const limit = b === FULL_FILL_BAR ? FULL_FILL_START
                        : b === HALF_FILL_BAR ? HALF_FILL_START : STEPS;
            for (let s = 0; s < limit; s++) {
                const a = pat[v][0][s], c = pat[v][b][s];
                eq (a === null, c === null, v + ' bar' + b + ' step' + s + ' onset differs');
                if (a !== null) eq (a.vel, c.vel, v + ' bar' + b + ' step' + s + ' velocity differs');
            }
        }
});

test ('both turnarounds fire, and the half-phrase one is lighter', () => {
    let half = 0, full = 0;
    for (let k = 0; k < 24; k++) {
        let t = TRACK.setVariation (T (), 'THUMPER', k % 8);
        t = withDial (t, 'pulse', 3 + (k % 7));
        const pat = gen (t);
        for (const v of VOICES) {
            if (v === 'moss') continue;
            for (let s = HALF_FILL_START; s < STEPS; s++)
                if (JSON.stringify (pat[v][HALF_FILL_BAR][s]) !== JSON.stringify (pat[v][0][s])) half++;
            for (let s = FULL_FILL_START; s < STEPS; s++)
                if (JSON.stringify (pat[v][FULL_FILL_BAR][s]) !== JSON.stringify (pat[v][0][s])) full++;
        }
    }
    assert (half > 0, 'the bar-2 turnaround never changed anything');
    assert (full > half, 'the bar-4 fill should be the bigger event: ' + full + ' vs ' + half);
});

test ('the pad is never cut by a fill, whatever its rhythm', () => {
    for (let v = 0; v < PRESETS.VARIATION_COUNT; v++) {
        const pat = gen (TRACK.setVariation (T (), 'MOSS', v));
        const ref = pat.moss[0];
        for (const b of [HALF_FILL_BAR, FULL_FILL_BAR])
            for (let s = HALF_FILL_START; s < STEPS; s++)
                eq (pat.moss[b][s] === null, ref[s] === null,
                    'a fill punched a hole in the pad (variation ' + v + ', bar ' + b + ')');
    }
});

test ('SIREN ANSWER really does leave bars empty', () => {
    const idx = PRESETS.SIREN.findIndex (s => s.name === 'ANSWER');
    const pat = gen (TRACK.setPreset (T (), 'SIREN', idx));
    const bars = PRESETS.SIREN[idx].bars;
    for (let b = 0; b < BARS; b++) {
        if (bars[b] !== 0) continue;
        for (let s = 0; s < STEPS; s++)
            assert (pat.lead[b][s] === null, 'ANSWER played in bar ' + b + ', which it should rest in');
    }
});

test ('stepAt wraps across the phrase in both directions', () => {
    const pat = gen (T ());
    deepEq (stepAt (pat, 'kick', 0), stepAt (pat, 'kick', TOTAL_STEPS));
    deepEq (stepAt (pat, 'kick', TOTAL_STEPS - 1), stepAt (pat, 'kick', -1));
});

// --------------------------------------------------------------- PRESETS --
section ('Preset banks');

test ('every bank has the advertised size and unique names', () => {
    for (const m of PRESETS.MODULE_NAMES) {
        const bank = PRESETS.BANKS[m];
        eq (bank.length, PRESETS.PRESET_COUNT, m + ' bank size');
        eq (new Set (bank.map (b => b.name)).size, bank.length, m + ' has duplicate preset names');
    }
});

test ('every rhythm template is exactly 16 steps of legal weights', () => {
    const strs = [];
    for (const g of PRESETS.THUMPER) strs.push (g.kick, g.snare, g.hat);
    for (const g of PRESETS.GLOWORM) strs.push (g.hits);
    for (const r of PRESETS.MOSS_RHYTHMS) strs.push (r.hits);
    for (const s of strs) {
        const w = PRESETS.parseWeights (s);
        eq (w.length, 16, 'template "' + s + '" is not 16 steps');
        for (const x of w) assert (x >= 0 && x <= 3, 'illegal weight ' + x + ' in "' + s + '"');
    }
});

test ('every drum groove has a downbeat you can find', () => {
    for (const g of PRESETS.THUMPER)
        assert (PRESETS.parseWeights (g.kick)[0] >= 3, g.name + ' has no guaranteed downbeat kick');
});

test ('every bass preset has a contour entry per step', () => {
    for (const g of PRESETS.GLOWORM) eq (g.contour.length, 16, g.name + ' contour length');
});

test ('every progression has one chord per bar and starts on the tonic', () => {
    for (const m of PRESETS.MOSS) {
        eq (m.prog.length, BARS, m.name + ' progression length');
        eq (m.prog[0], 0, m.name + ' must anchor on the tonic');
    }
});

test ('every preset x variation combination generates without throwing', () => {
    for (const m of PRESETS.MODULE_NAMES)
        for (let pi = 0; pi < PRESETS.PRESET_COUNT; pi++)
            for (let vi = 0; vi < PRESETS.VARIATION_COUNT; vi++) {
                let t = TRACK.setPreset (T (), m, pi);
                t = TRACK.setVariation (t, m, vi);
                const pat = gen (t);
                for (const v of VOICES) eq (pat[v].length, BARS, m + ' ' + pi + '/' + vi);
            }
});

// ---------------------------------------------------------- PITCH SAFETY --
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
            assert (isInScale (degreeToMidi (deg, si, 0), si), SCALES[si].name + ' degree ' + deg);
});

test ('every pitch any voice can play is in the active scale, across the whole space', () => {
    for (let warp = 0; warp <= 10; warp += 2)
        for (let pi = 0; pi < PRESETS.PRESET_COUNT; pi++) {
            let t = withDial (T (), 'warp', warp);
            for (const m of ['GLOWORM', 'SIREN', 'SPINDLE', 'MOSS']) t = TRACK.setPreset (t, m, pi);
            const params = computeParams (t.dials, t.key);
            const pat = generatePatterns (t, params);
            for (const v of MELODIC)
                for (const bar of pat[v])
                    for (const st of bar) {
                        if (!st) continue;
                        const degs = v === 'moss' ? chordTonesFor (st.degree) : [st.degree];
                        for (const d of degs)
                            assert (isInScale (degreeToMidi (d, params.scaleIdx, VOICE_OCTAVE[v]), params.scaleIdx),
                                    v + ' out of scale at warp=' + warp + ' preset=' + pi);
                    }
        }
});

test ('no voice can play outside its register, in any scale, key or degree', () => {
    // The register guard folds by whole octaves, so this also proves it never
    // introduces an out-of-scale note.
    for (let key = 0; key < 12; key++)
        for (let si = 0; si < SCALES.length; si++)
            for (const v of MELODIC)
                for (let deg = -10; deg <= 10; deg++) {
                    const m = voiceMidi (deg, si, v, key);
                    const r = VOICE_RANGE[v];
                    assert (m >= r[0] && m <= r[1],
                            v + ' escaped its register: midi ' + m + ' (scale ' + SCALES[si].name +
                            ', key ' + key + ', degree ' + deg + ')');
                    assert (isInScale (m - key, si),
                            v + ' folded to an out-of-scale note at degree ' + deg);
                }
});

test ('the bass never drops into inaudible rumble', () => {
    for (let key = 0; key < 12; key++)
        for (let si = 0; si < SCALES.length; si++)
            for (let deg = -10; deg <= 10; deg++) {
                const f = voiceFreq (deg, si, 'bass', key);
                assert (f > 38, 'bass at ' + f.toFixed (1) + 'Hz (scale ' + SCALES[si].name + ')');
            }
});

test ('the lead moves mostly stepwise rather than leaping about', () => {
    let small = 0, total = 0;
    for (let pi = 0; pi < PRESETS.PRESET_COUNT; pi++)
        for (let vi = 0; vi < 6; vi++) {
            let t = TRACK.setPreset (T (), 'SIREN', pi);
            t = TRACK.setVariation (t, 'SIREN', vi);
            const bar = gen (t).lead[0];
            let prev = null;
            for (let s = 0; s < STEPS; s++) {
                if (bar[s] === null) continue;
                if (prev !== null) { total++; if (Math.abs (bar[s].degree - prev) <= 2) small++; }
                prev = bar[s].degree;
            }
        }
    assert (total > 40, 'not enough lead notes to judge');
    assert (small / total > 0.55, 'lead leaps too much: ' + (100 * small / total).toFixed (0) + '% stepwise');
});

// ---------------------------------------------------------------- PARAMS --
section ('Params');

test ('PULSE maps to the documented BPM range', () => {
    eq (Math.round (computeParams (dialsOf ({ pulse: 0 })).bpm), 60);
    eq (Math.round (computeParams (dialsOf ({ pulse: 10 })).bpm), 170);
});

test ('GOO closes the filter, VOID opens the CAVE, WARP adds detune', () => {
    assert (computeParams (dialsOf ({ goo: 0 })).filterBase > computeParams (dialsOf ({ goo: 10 })).filterBase);
    assert (computeParams (dialsOf ({ void: 10 })).caveSend > computeParams (dialsOf ({ void: 0 })).caveSend);
    assert (computeParams (dialsOf ({ warp: 10 })).detuneCents > computeParams (dialsOf ({ warp: 0 })).detuneCents);
    eq (computeParams (dialsOf ({ warp: 0 })).detuneCents, 0);
    assert (computeParams (dialsOf ({ void: 10 })).caveFeedback < 1, 'CAVE feedback must stay under unity');
});

test ('density stays positive across the whole dial space', () => {
    for (let p = 0; p <= 10; p += 0.5)
        for (let v = 0; v <= 10; v += 0.5) {
            const dens = computeParams (dialsOf ({ pulse: p, void: v })).density;
            assert (dens > 0 && dens <= 1, 'density out of range: ' + dens);
        }
});

// ------------------------------------------------------------ CLASSIFIER --
section ('Classifier');

test ('every genre centre classifies as itself', () => {
    for (const g of GENRES) {
        const d = { pulse: g.c[0], crunch: g.c[1], goo: g.c[2], void: g.c[3], jitter: g.c[4], warp: g.c[5] };
        eq (classify (d).primary.name, g.name, 'centre of ' + g.name);
    }
});

test ('a point midway between two centres reports a blend', () => {
    const a = GENRES[0].c, b = GENRES[8].c;
    const m = a.map ((v, i) => (v + b[i]) / 2);
    const r = classify ({ pulse: m[0], crunch: m[1], goo: m[2], void: m[3], jitter: m[4], warp: m[5] });
    assert (r.blended, 'midpoint should blend');
    assert (r.label.indexOf (' ') > 0, 'blend label should be two words');
});

test ('threshold is a margin over the winner, not an absolute distance', () => {
    assert (classify (DEFAULT_DIALS, 0).blended === false);
    assert (classify (DEFAULT_DIALS, 99).blended === true);
});

// ---------------------------------------------------------------- PURITY --
section ('Engine purity');

test ('engine/ contains no unseeded randomness or wall-clock reads', () => {
    const dir = join (HERE, '..', 'engine');
    const banned = /Math\.random|Date\.now|new Date|performance\.now/;
    for (const f of readdirSync (dir)) {
        if (!f.endsWith ('.js')) continue;
        const src = readFileSync (join (dir, f), 'utf8');
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
        while ((m = re.exec (src)))
            assert (m[1].startsWith ('./'), 'engine/' + f + ' imports ' + m[1]);
    }
});

// ------------------------------------------------------- THE ACTIVE SET ----
// Muting is a compositional choice that has to print onto a cassette, so it
// lives on the track and counts toward its identity. What it must NEVER do is
// disturb the notes — that is the same guarantee that lets a plugin be unlocked
// later without changing a tape someone already owns.

section ('Active set');

test ('a fresh track has every module playing', () => {
    const t = T ();
    for (const m of PRESETS.MODULE_NAMES) assert (t.active[m], m + ' starts muted');
    eq (TRACK.activeCount (t), 6, 'active count');
    eq (TRACK.activeMask (t), 63, 'full mask');
});

test ('the mask is bit 0 = THUMPER, in module order', () => {
    for (let i = 0; i < PRESETS.MODULE_NAMES.length; i++) {
        const t = TRACK.setActive (T (), PRESETS.MODULE_NAMES[i], false);
        eq (TRACK.activeMask (t), 63 & ~(1 << i), 'muting ' + PRESETS.MODULE_NAMES[i]);
    }
});

test ('muting a module changes the track identity', () => {
    const base = TRACK.trackId (T ());
    const seen = new Set ([base]);
    for (const m of PRESETS.MODULE_NAMES) {
        const id = TRACK.trackId (TRACK.setActive (T (), m, false));
        assert (!seen.has (id), 'muting ' + m + ' did not produce a distinct id');
        seen.add (id);
    }
});

test ('muting a module changes NO voice pattern', () => {
    // The load-bearing one. Every voice is generated whether or not it is
    // audible, and each draws from its own constant-keyed stream — so silence
    // is a mix decision, never a generation decision.
    const before = gen (T ());
    for (const m of PRESETS.MODULE_NAMES) {
        const after = gen (TRACK.setActive (T (), m, false));
        for (const v of VOICES)
            deepEq (after[v], before[v], 'muting ' + m + ' moved the ' + v + ' pattern');
    }
});

test ('muting never asks for a regeneration', () => {
    for (const m of PRESETS.MODULE_NAMES)
        assert (!TRACK.needsRegen (T (), TRACK.setActive (T (), m, false)),
                'muting ' + m + ' requested a pattern regen');
});

test ('a track with everything muted still generates a full phrase', () => {
    let t = T ();
    for (const m of PRESETS.MODULE_NAMES) t = TRACK.setActive (t, m, false);
    const p = gen (t);
    for (const v of VOICES) assert (p[v], v + ' vanished when its module was muted');
    deepEq (p, gen (T ()), 'silencing the whole rack changed the phrase');
});

test ('cloning carries the active set', () => {
    const t = TRACK.setActive (T (), 'MOSS', false);
    const c = TRACK.cloneTrack (t);
    eq (c.active.MOSS, false, 'clone lost the mute');
    c.active.MOSS = true;
    eq (t.active.MOSS, false, 'clone aliases the original');
});

// ------------------------------------------------------------------ SONG ----
section ('song (arrangement layer)');

test ('a default song is one 4-bar section of the default track', () => {
    const s = SONGMOD.defaultSong ();
    eq (s.sections.length, 1);
    eq (s.sections[0].bars, 4);
    eq (TRACK.trackId (s.sections[0].track), TRACK.trackId (T ()));
});

test ('songId is stable, and covers order, bars and section tracks', () => {
    const base = SONGMOD.addSection (SONGMOD.defaultSong (), 0);
    eq (SONGMOD.songId (base), SONGMOD.songId (SONGMOD.cloneSong (base)), 'clone must hash identically');
    assert (SONGMOD.songId (SONGMOD.setSectionBars (base, 0, 8)) !== SONGMOD.songId (base),
            'stretching a section must change the song');
    const reordered = SONGMOD.moveSection (SONGMOD.setSectionBars (base, 0, 8), 0, 1);
    assert (SONGMOD.songId (reordered) !== SONGMOD.songId (SONGMOD.setSectionBars (base, 0, 8)),
            'reordering sections must change the song');
    const edited = SONGMOD.setSectionTrack (base, 1, withDial (T (), 'pulse', 9));
    assert (SONGMOD.songId (edited) !== SONGMOD.songId (base),
            'editing a section track must change the song');
});

test ('sections own their tracks — duplicating never aliases', () => {
    const s = SONGMOD.addSection (SONGMOD.defaultSong (), 0);
    s.sections[1].track.dials.pulse = 9.5;
    assert (s.sections[0].track.dials.pulse !== 9.5, 'section B reached into section A');
});

test ('sectionAtStep walks the arrangement the way playback does', () => {
    let s = SONGMOD.defaultSong ();                       // A: 4 bars
    s = SONGMOD.addSection (s, 0);                        // + B: 4 bars
    s = SONGMOD.setSectionBars (s, 0, 2);                 // A: 2 bars
    eq (SONGMOD.totalBars (s), 6);
    eq (SONGMOD.sectionAtStep (s, 0).index, 0);
    eq (SONGMOD.sectionAtStep (s, 2 * STEPS - 1).index, 0, 'last step of A');
    const b = SONGMOD.sectionAtStep (s, 2 * STEPS);
    eq (b.index, 1, 'first step of B');
    eq (b.stepInSection, 0);
    eq (SONGMOD.sectionAtStep (s, 5 * STEPS + 3).barInSection, 3, 'bar within B');
});

test ('genre mix weights by bars and sums to one', () => {
    let s = SONGMOD.defaultSong ();
    s = SONGMOD.addSection (s, 0);
    s = SONGMOD.setSectionTrack (s, 1, { ...TRACK.cloneTrack (T ()), dials: dialsOf ({ pulse: 1, void: 9, crunch: 1 }) });
    s = SONGMOD.setSectionBars (s, 0, 6);
    s = SONGMOD.setSectionBars (s, 1, 2);
    const mix = SONGMOD.genreMix (s);
    let total = 0;
    for (const m of mix) total += m.share;
    assert (Math.abs (total - 1) < 1e-9, 'shares must sum to 1');
    assert (mix[0].share >= mix[mix.length - 1].share, 'sorted biggest first');
});

test ('value grows with sections and with bars; offers dilute by share', () => {
    const one = SONGMOD.defaultSong ();
    const two = SONGMOD.addSection (one, 0);
    assert (SONGMOD.songValueMult (two) > SONGMOD.songValueMult (one), 'more sections must pay more');
    assert (SONGMOD.songValueMult (SONGMOD.setSectionBars (one, 0, 12)) > SONGMOD.songValueMult (one),
            'longer must pay more');
    // A pure single-genre song: the fan of that genre gets the whole value.
    const g = classify (one.sections[0].track.dials).primary.name;
    assert (Math.abs (SONGMOD.offerMult (one, g) - SONGMOD.songValueMult (one)) < 1e-9);
    eq (SONGMOD.offerMult (one, g === 'CLANG' ? 'CHIRP' : 'CLANG'), 0, 'absent genre offers nothing');
});

test ('moveSectionTo drops a section into an insertion slot', () => {
    // A(4), B(8), C(2) — distinguishable by bars.
    let s = SONGMOD.defaultSong ();
    s = SONGMOD.addSection (s, 0);
    s = SONGMOD.setSectionBars (s, 1, 8);
    s = SONGMOD.addSection (s, 1);
    s = SONGMOD.setSectionBars (s, 2, 2);

    // Sam's example: grab C (index 2), drop between A and B (slot 1) -> A C B.
    const moved = SONGMOD.moveSectionTo (s, 2, 1);
    deepEq (moved.sections.map (x => x.bars), [4, 2, 8], 'expected A C B');

    // Dropping back where it came from is a no-op that returns the input.
    eq (SONGMOD.moveSectionTo (s, 2, 2), s, 'same slot must not clone');
    eq (SONGMOD.moveSectionTo (s, 2, 3), s, 'the slot after itself is the same place');

    // First section to the very end.
    deepEq (SONGMOD.moveSectionTo (s, 0, 3).sections.map (x => x.bars), [8, 2, 4]);
    assert (SONGMOD.songId (moved) !== SONGMOD.songId (s), 'reorder changes the song identity');
});

test ('a song never loses its last section and clamps bars', () => {
    let s = SONGMOD.defaultSong ();
    eq (SONGMOD.removeSection (s, 0), s, 'removing the only section must be refused');
    eq (SONGMOD.setSectionBars (s, 0, 99).sections[0].bars, SONGMOD.SECTION_MAX_BARS);
    eq (SONGMOD.setSectionBars (s, 0, 0).sections[0].bars, SONGMOD.SECTION_MIN_BARS);
});

test ('the last bar of a section always plays the fill bar', () => {
    const five = SONGMOD.makeSection (T (), 5);
    eq (SONGMOD.patternBarFor (five, 4), FULL_FILL_BAR, 'last bar of 5 must remap to the fill');
    eq (SONGMOD.patternBarFor (five, 1), 1, 'inner bars are untouched');
    const four = SONGMOD.makeSection (T (), 4);
    eq (SONGMOD.patternBarFor (four, 3), FULL_FILL_BAR, '4-bar sections land on it naturally');
    eq (SONGMOD.patternStepFor (five, 4 * STEPS + 5), FULL_FILL_BAR * STEPS + 5,
        'step remap keeps the position within the bar');
});

test ('transition intensity is zero for identical sections and grows with distance', () => {
    const a = SONGMOD.makeSection (T (), 4);
    eq (SONGMOD.transitionIntensity (a, a), 0);
    const near = SONGMOD.makeSection (Object.assign (TRACK.cloneTrack (T ()),
        { dials: dialsOf ({ pulse: 5.5 }) }), 4);
    const far = SONGMOD.makeSection (Object.assign (TRACK.cloneTrack (T ()),
        { dials: dialsOf ({ pulse: 10, crunch: 9 }) }), 4);
    assert (SONGMOD.transitionIntensity (a, near) < SONGMOD.TRANSITION_FX_MIN,
            'a hair of dial movement must not trigger boundary FX');
    assert (SONGMOD.transitionIntensity (a, far) >= SONGMOD.TRANSITION_FX_MIN,
            'a genre jump must trigger boundary FX');
    assert (SONGMOD.transitionIntensity (a, far) <= 1, 'clamped to 1');
});

test ('coerceSong salvages what it can and rejects garbage', () => {
    const ct = (raw) => TRACK.defaultTrack ();
    eq (SONGMOD.coerceSong (null, ct), null);
    eq (SONGMOD.coerceSong ({ sections: [] }, ct), null);
    const ok = SONGMOD.coerceSong ({ sections: [{ bars: '8', track: {} }, null] }, ct);
    eq (ok.sections.length, 1);
    eq (ok.sections[0].bars, 8);
});

// ----------------------------------------------------------------- REPORT --
console.log ('\n' + '-'.repeat (52));
console.log (passed + ' passed, ' + failed + ' failed');
if (failed) {
    console.log ('\nfailures:');
    for (const [n, m] of failures) console.log ('  ' + n + '\n    ' + m);
}
process.exit (failed ? 1 : 0);
