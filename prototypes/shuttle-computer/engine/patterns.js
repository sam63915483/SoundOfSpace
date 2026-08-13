// Pattern generation: a track -> a 4-bar phrase per voice.
//
// Shape: patterns[voice] = Bar[4], Bar = Step[16], Step = null | {
//   vel, nudge, degree (melodic), dur (melodic), open (hat)
// }
//
// ── Preset chooses, dial shapes, variation re-rolls ──────────────────────
// Rhythms come from hand-authored 16-step weight templates (see presets.js):
// 3 always sounds, 2 usually, 1 optionally. The PRESET picks the template — the
// skeleton of the groove. PULSE decides how much of the optional material fills
// in. VARIATION decides which optional hits land. So a groove gets busier
// without becoming a different groove, which is the whole point.
//
// ── The draw discipline that makes dials feel like shaping ───────────────
// Every voice consumes a FIXED number of draws per step, whether or not that
// step ends up sounding. If draws were only taken on hits, turning PULSE up
// would shift every later step's velocity and nudge — so the groove would
// still subtly re-roll and we would be back where we started. Drawing
// unconditionally means a dial change flips individual hits on and off and
// disturbs nothing else.
//
// Bars share their RHYTHM; melodic voices re-harmonise against each bar's
// chord. Two turnarounds punctuate the phrase: a light 2-step fill ending bar
// 2, the full 4-step fill ending bar 4.

import { mulberry32 } from './prng.js';
import { voiceSeed, fillSeed, MODULE_FOR_VOICE } from './track.js';
import * as PRESETS from './presets.js';

export const BARS = 4;
export const STEPS = 16;
export const TOTAL_STEPS = BARS * STEPS;

export const VOICES = ['kick', 'snare', 'hat', 'bass', 'lead', 'moss', 'spindle'];
export const MELODIC = ['bass', 'lead', 'moss', 'spindle'];

export const FULL_FILL_BAR = 3, FULL_FILL_START = 12;
export const HALF_FILL_BAR = 1, HALF_FILL_START = 14;

const CHORD_TONES = [0, 2, 4];

/// Does a step sound? 3 always, 2 usually, 1 only when things are busy.
function hitFor (w, r, density) {
    if (w >= 3) return true;
    if (w === 2) return r < 0.55 + density * 0.45;
    if (w === 1) return r < density * 0.7;
    return false;
}

function snapToChord (off) {
    let best = CHORD_TONES[0], bestD = Infinity;
    for (const base of CHORD_TONES) {
        for (const oct of [-7, 0, 7]) {
            const d = Math.abs (base + oct - off);
            if (d < bestD) { bestD = d; best = base + oct; }
        }
    }
    return best;
}

export function progressionFor (track) {
    return PRESETS.MOSS[track.preset.MOSS % PRESETS.MOSS.length].prog;
}

export function chordTonesFor (root) {
    return CHORD_TONES.map (t => root + t);
}

// --- per-voice cells (16 steps, shared by all four bars) -----------------

function makeDrumCell (rnd, p, weightStr, kind) {
    const w = PRESETS.parseWeights (weightStr);
    const cell = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        const r = rnd (), rv = rnd (), rn = rnd (), ro = rnd ();   // always 4
        if (!hitFor (w[s], r, p.density)) continue;
        const core = w[s] >= 3;
        let vel;
        if (kind === 'hat') vel = (s % 4 === 0 ? 0.7 : 0.42);
        else if (kind === 'snare') vel = core ? 0.9 : 0.28;
        else vel = core ? 0.95 : 0.6;
        cell[s] = {
            vel: vel - rv * 0.08,
            nudge: s === 0 ? 0 : (rn * 2 - 1) * p.nudgeSeconds
        };
        if (kind === 'hat') cell[s].open = ro < p.hatScatter * 0.22;
    }
    return cell;
}

function makeBassCell (rnd, p, preset) {
    const w = PRESETS.parseWeights (preset.hits);
    const cell = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        const r = rnd (), rv = rnd (), rn = rnd (), rd = rnd ();
        if (!hitFor (w[s], r, p.density)) continue;
        cell[s] = {
            vel: 0.78 + rv * 0.18,
            nudge: s === 0 ? 0 : (rn * 2 - 1) * p.nudgeSeconds,
            off: preset.contour[s],
            dur: 1 + Math.floor (rd * 2)
        };
    }
    return cell;
}

/// The lead is still generated — a preset that always played the same tune
/// would be the preset-loop problem one level up. The preset sets its
/// character: how often it plays, how far it jumps, how long the notes are.
function makeLeadCell (rnd, p, cfg) {
    const cell = new Array (STEPS).fill (null);
    let cur = 0;
    for (let s = 0; s < STEPS; s++) {
        const r = rnd (), rv = rnd (), rn = rnd (), rm = rnd (), rdir = rnd (), rl = rnd ();

        let shape = (s % 8 === 0) ? 1.0 : (s % 4 === 0 ? 0.75 : (s % 2 === 0 ? 0.5 : 0.28));
        if (s === 4 || s === 12) shape *= 0.55;              // room for the snare
        const prob = cfg.gate * shape * (0.55 + p.density * 0.6);
        if (r >= prob) continue;

        let move;
        if (rm < cfg.leap) move = rdir < 0.5 ? -4 : 4;
        else if (rm < 0.5) move = rdir < 0.5 ? -1 : 1;
        else if (rm < 0.82) move = rdir < 0.5 ? -2 : 2;
        else move = 0;
        cur += move;
        if (cur > 6) cur -= 7;
        if (cur < -3) cur += 7;
        if (s % 4 === 0) cur = snapToChord (cur);

        cell[s] = {
            vel: 0.6 + rv * 0.25,
            nudge: (rn * 2 - 1) * p.nudgeSeconds,
            off: cur,
            dur: cfg.len + Math.floor (rl * 2)
        };
    }
    return cell;
}

function makeMossCell (rnd, rhythm) {
    const w = PRESETS.parseWeights (rhythm.hits);
    const cell = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        const r = rnd (), rv = rnd ();
        if (!hitFor (w[s], r, 1)) continue;                  // the pad plays its template
        cell[s] = { vel: 0.42 + rv * 0.12, nudge: 0, off: 0, dur: rhythm.dur };
    }
    return cell;
}

function makeSpindleCell (rnd, p, shape) {
    const every = p.density > 0.55 ? 1 : 2;
    const cell = new Array (STEPS).fill (null);
    let i = 0;
    for (let s = 0; s < STEPS; s++) {
        const r = rnd (), rv = rnd (), rn = rnd ();
        if (s % every !== 0) continue;
        const tone = PRESETS.ARP_TONES[shape[i % shape.length] % PRESETS.ARP_TONES.length];
        cell[s] = {
            vel: 0.4 + rv * 0.18,
            nudge: (rn * 2 - 1) * p.nudgeSeconds,
            off: tone,
            dur: every
        };
        i++;
    }
    return cell;
}

// --- assembly ------------------------------------------------------------

function cellFor (track, voice, params) {
    const mod = MODULE_FOR_VOICE[voice];
    const rnd = mulberry32 (voiceSeed (track, voice));
    const pi = track.preset[mod] % PRESETS.PRESET_COUNT;
    const vi = track.variation[mod] % PRESETS.VARIATION_COUNT;

    switch (voice) {
        case 'kick':  return makeDrumCell (rnd, params, PRESETS.THUMPER[pi].kick, 'kick');
        case 'snare': return makeDrumCell (rnd, params, PRESETS.THUMPER[pi].snare, 'snare');
        case 'hat':   return makeDrumCell (rnd, params, PRESETS.THUMPER[pi].hat, 'hat');
        case 'bass':  return makeBassCell (rnd, params, PRESETS.GLOWORM[pi]);
        case 'lead':  return makeLeadCell (rnd, params, PRESETS.SIREN[pi]);
        case 'moss':  return makeMossCell (rnd, PRESETS.MOSS_RHYTHMS[vi]);
        case 'spindle': return makeSpindleCell (rnd, params, PRESETS.SPINDLE[pi].shape);
        default: throw new Error ('unknown voice ' + voice);
    }
}

export function generatePatterns (track, params) {
    const prog = progressionFor (track);
    const leadBars = PRESETS.SIREN[track.preset.SIREN % PRESETS.PRESET_COUNT].bars;
    const out = {};

    for (const voice of VOICES) {
        const cell = cellFor (track, voice, params);
        const melodic = MELODIC.indexOf (voice) !== -1;
        const isMoss = voice === 'moss';
        const bars = new Array (BARS);

        for (let b = 0; b < BARS; b++) {
            // SIREN's preset can silence whole bars — that is what makes
            // ANSWER read as call-and-response instead of constant noodling.
            const silent = voice === 'lead' && leadBars[b] === 0;
            bars[b] = new Array (STEPS);
            for (let s = 0; s < STEPS; s++) {
                const c = cell[s];
                if (c === null || silent) { bars[b][s] = null; continue; }
                const st = Object.assign ({}, c);
                if (melodic) { st.degree = prog[b] + (isMoss ? 0 : st.off); delete st.off; }
                bars[b][s] = st;
            }
        }

        const fillRnd = mulberry32 (fillSeed (track, voice));
        applyFill (bars[HALF_FILL_BAR], voice, fillRnd, params, HALF_FILL_START,
                   prog[HALF_FILL_BAR], 0.55);
        applyFill (bars[FULL_FILL_BAR], voice, fillRnd, params, FULL_FILL_START,
                   prog[FULL_FILL_BAR], 1.0);

        out[voice] = bars;
    }

    return out;
}

/// Overwrite the tail of a bar. `weight` scales how busy it is, so the
/// half-phrase turnaround stays lighter than the end-of-phrase one.
function applyFill (bar, voice, rnd, p, from, root, weight) {
    // The pad rides straight through a fill — cutting the harmony out from
    // under a turnaround is what makes it sound like a mistake. It also must
    // consume NO draws, or every other voice's fill would shift.
    if (voice === 'moss') return;

    for (let s = from; s < STEPS; s++) {
        const r = rnd (), rv = rnd (), rn = rnd (), rx = rnd ();     // always 4
        let prob, step = null;
        switch (voice) {
            case 'kick':    prob = (0.3 + p.density * 0.35) * weight; break;
            case 'snare':   prob = (0.6 + p.density * 0.35) * weight; break;
            case 'hat':     prob = 0.75 * weight; break;
            case 'bass':    prob = (0.5 + p.density * 0.3) * weight; break;
            case 'lead':    prob = (0.35 + p.density * 0.3) * weight; break;
            case 'spindle': prob = 0.8 * weight; break;
            default:        prob = 0;
        }
        if (r < prob) {
            const nudge = (rn * 2 - 1) * p.nudgeSeconds;
            switch (voice) {
                case 'kick':    step = { vel: 0.8 + rv * 0.15, nudge }; break;
                case 'snare':   step = { vel: 0.45 + rv * 0.5, nudge }; break;
                case 'hat':     step = { vel: 0.4 + rv * 0.35, nudge, open: s === STEPS - 1 }; break;
                case 'bass':    step = { vel: 0.8 + rv * 0.2, nudge, degree: root, dur: 1 }; break;
                case 'lead':    step = { vel: 0.65 + rv * 0.3, nudge,
                                         degree: root + CHORD_TONES[Math.floor (rx * CHORD_TONES.length)], dur: 1 }; break;
                case 'spindle': step = { vel: 0.4 + rv * 0.2, nudge,
                                         degree: root + PRESETS.ARP_TONES[Math.floor (rx * PRESETS.ARP_TONES.length)], dur: 1 }; break;
            }
        }
        bar[s] = step;
    }
}

export function stepAt (patterns, voice, globalStep) {
    const i = ((globalStep % TOTAL_STEPS) + TOTAL_STEPS) % TOTAL_STEPS;
    return patterns[voice][Math.floor (i / STEPS)][i % STEPS];
}
