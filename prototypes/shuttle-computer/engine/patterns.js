// Pattern generation: seed + params -> a 4-bar phrase per voice.
//
// Shape: patterns[voice] = Bar[4], Bar = Step[16], Step = null | {
//   vel:    0..1
//   nudge:  seconds, off-grid push (JITTER)
//   degree: scale degree, melodic voices only
//   dur:    length in steps, melodic voices only
//   open:   hat only, long/open hi-hat
// }
//
// ── What makes this sound like music and not noise ───────────────────────
// The first version drew every step and every pitch as an independent coin
// flip, which is why it came out sounding random. Four things fix that, and
// none of them cost the player a dial — the structure is what stops someone
// who isn't a musician from being able to make something bad:
//
//   1. HARMONY. A 4-bar chord progression is chosen from a table, one chord per
//      bar. Bass plays roots, MOSS holds the triad, SPINDLE arpeggiates it, and
//      the lead resolves to chord tones on strong beats. Everything is now
//      consonant with everything else, and bar 3 feels different from bar 1
//      because the harmony moved rather than because the dice did.
//   2. RHYTHM CELLS. Each voice generates ONE 8-step cell which is tiled twice
//      per bar, instead of 16 independent probabilities. Repetition is what
//      makes a groove read as deliberate.
//   3. A MELODIC MOTIF. The lead is one 8-step figure with stepwise motion —
//      mostly ±1 or ±2 scale degrees, occasional leaps — repeated and
//      re-harmonised per bar. That is the difference between a tune and a
//      sequence of pitches.
//   4. INTERLOCK. Bass onsets are pulled toward the kick; the lead backs off
//      where the snare lands. The voices stop ignoring each other.
//
// Bars share their RHYTHM (the tiled cell) but not their PITCH — each bar
// resolves against its own chord. Two turnarounds punctuate the phrase: a light
// 2-step fill at the end of bar 2, and the full 4-step fill at the end of bar 4.
//
// ── Why the draw ORDER is load-bearing ───────────────────────────────────
// Every call to Rng.Next() advances the stream. Adding, removing or reordering
// a single draw shifts every subsequent value and re-rolls the whole pattern —
// which would silently change what every previously printed cassette sounds
// like. The draws below are written as explicit sequential statements rather
// than inline in an initializer precisely so the order is impossible to
// misread.

import { streamFor } from './prng.js';

export const BARS = 4;
export const STEPS = 16;
export const CELL = 8;                    // rhythm cell length; tiled twice per bar
export const TOTAL_STEPS = BARS * STEPS;

export const VOICES = ['kick', 'snare', 'hat', 'bass', 'lead', 'moss', 'spindle'];
export const MELODIC = ['bass', 'lead', 'moss', 'spindle'];

// Two turnarounds. The half-phrase one is deliberately SMALLER — two identical
// fills would split the 4-bar phrase into two 2-bar loops and lose the longer arc.
export const FULL_FILL_BAR = 3, FULL_FILL_START = 12;   // last 4 steps of bar 4
export const HALF_FILL_BAR = 1, HALF_FILL_START = 14;   // last 2 steps of bar 2

// Chord progressions in scale degrees, one entry per bar. All start on the
// tonic so the phrase always has an anchor. Stacking scale-thirds (see
// CHORD_TONES) means these stay in-scale for every scale table — and go
// appropriately strange on the alien ones, which is free character.
const PROGRESSIONS = [
    [0, 5, 3, 4],
    [0, 3, 4, 0],
    [0, 4, 5, 3],
    [0, 0, 3, 4],
    [0, 5, 0, 4],
    [0, 2, 3, 4],
    [0, 4, 3, 5],
    [0, 3, 0, 4]
];

const CHORD_TONES = [0, 2, 4];            // triad, in scale degrees
const ARP_OFFSETS = [0, 2, 4, 6];         // what SPINDLE climbs

function pick (rnd, arr) {
    return arr[Math.floor (rnd () * arr.length)];
}

function nudgeFor (rnd, p) {
    // Symmetric push around the grid line. Small, but it's what stops the
    // rhythm sounding like a drum machine.
    return (rnd () * 2 - 1) * p.nudgeSeconds;
}

// Nearest chord tone to a scale-degree offset — used to land strong beats on
// the harmony instead of wherever the walk happened to be.
function snapToChord (off) {
    let best = CHORD_TONES[0], bestD = Infinity;
    for (const base of CHORD_TONES) {
        for (const oct of [-7, 0, 7]) {
            const cand = base + oct;
            const d = Math.abs (cand - off);
            if (d < bestD) { bestD = d; best = cand; }
        }
    }
    return best;
}

export function progressionFor (seed) {
    const rnd = streamFor (seed, 'chord');
    return PROGRESSIONS[Math.floor (rnd () * PROGRESSIONS.length)];
}

// --- rhythm cells (8 steps, tiled twice per bar) -------------------------

function makeKickCell (rnd, p) {
    const cell = new Array (CELL).fill (null);
    for (let s = 0; s < CELL; s++) {
        let prob;
        if (s === 0) prob = 1;                                        // anchor
        else if (s % 4 === 0) prob = 0.4 + p.density * 0.5;
        else if (s % 2 === 0) prob = p.density * 0.3 * (0.4 + p.syncopation);
        else prob = p.density * 0.18 * p.syncopation;
        if (rnd () < prob) {
            cell[s] = {
                vel: (s % 4 === 0 ? 0.95 : 0.6) - rnd () * 0.1,
                nudge: s === 0 ? 0 : nudgeFor (rnd, p)                 // never drift the downbeat
            };
        }
    }
    return cell;
}

function makeSnareCell (rnd, p) {
    const cell = new Array (CELL).fill (null);
    for (let s = 0; s < CELL; s++) {
        // Cell step 4 tiles onto bar steps 4 AND 12 — the backbeat, for free.
        const back = s === 4;
        let prob;
        if (back) prob = 0.97;
        else if (s % 2 === 1) prob = p.density * 0.22 * p.syncopation;  // ghosts
        else prob = p.density * 0.12 * p.syncopation;
        if (rnd () < prob) {
            cell[s] = {
                vel: back ? 0.9 - rnd () * 0.08 : 0.22 + rnd () * 0.14,
                nudge: nudgeFor (rnd, p)
            };
        }
    }
    return cell;
}

function makeHatCell (rnd, p) {
    const interval = p.density < 0.4 ? 4 : (p.density < 0.7 ? 2 : 1);
    const cell = new Array (CELL).fill (null);
    for (let s = 0; s < CELL; s++) {
        const onGrid = s % interval === 0;
        const prob = onGrid ? (1 - p.hatScatter * 0.3) : p.hatScatter * 0.25;
        if (rnd () < prob) {
            cell[s] = {
                vel: (s % 4 === 0 ? 0.7 : 0.42) - rnd () * 0.08,
                nudge: nudgeFor (rnd, p),
                open: rnd () < p.hatScatter * 0.22
            };
        }
    }
    return cell;
}

// Bass follows the kick rather than ignoring it — that lock is most of what
// makes a rhythm section sound like one instrument instead of two.
function makeBassCell (rnd, p, kickCell) {
    const cell = new Array (CELL).fill (null);
    for (let s = 0; s < CELL; s++) {
        const onKick = kickCell[s] !== null;
        let prob;
        if (s === 0) prob = 0.95;
        else if (onKick) prob = 0.55 + p.density * 0.35;
        else if (s % 4 === 0) prob = 0.4 + p.density * 0.3;
        else if (s % 2 === 0) prob = p.density * 0.25;
        else prob = p.density * 0.14 * p.syncopation;

        if (rnd () < prob) {
            // Root-heavy, with the fifth and the octave below for movement.
            const r = rnd ();
            let off;
            if (r < 0.62) off = 0;
            else if (r < 0.82) off = 4;
            else if (r < 0.94) off = -7;
            else off = 2;
            cell[s] = {
                vel: 0.75 + rnd () * 0.2,
                nudge: nudgeFor (rnd, p),
                off: s === 0 ? 0 : off,                                // land the downbeat on the root
                dur: 1 + Math.floor (rnd () * 2)
            };
        }
    }
    return cell;
}

// The lead is a MOTIF, not a sequence of independent pitches: one 8-step figure
// that walks mostly stepwise, snaps to chord tones on strong beats, and is
// re-harmonised each bar.
function makeLeadMotif (rnd, p) {
    const cell = new Array (CELL).fill (null);
    let cur = 0;
    for (let s = 0; s < CELL; s++) {
        let prob;
        if (s === 0) prob = 0.6 + p.density * 0.3;
        else if (s % 4 === 0) prob = 0.4 + p.density * 0.28;
        else if (s % 2 === 0) prob = p.density * 0.3;
        else prob = p.density * 0.16 * p.syncopation;
        // Leave room where the snare lands.
        if (s === 4) prob *= 0.55;

        if (rnd () < prob) {
            const r = rnd ();
            let move;
            if (r < 0.46) move = rnd () < 0.5 ? -1 : 1;                // stepwise, mostly
            else if (r < 0.74) move = rnd () < 0.5 ? -2 : 2;
            else if (r < 0.88) move = 0;                               // repeated note
            else move = rnd () < 0.5 ? -4 : 4;                         // occasional leap
            cur += move;
            if (cur > 6) cur -= 7;
            if (cur < -3) cur += 7;
            // Strong beats resolve onto the harmony.
            if (s % 4 === 0) cur = snapToChord (cur);

            cell[s] = {
                vel: 0.6 + rnd () * 0.25,
                nudge: nudgeFor (rnd, p),
                off: cur,
                dur: 1 + Math.floor (rnd () * 3)
            };
        }
    }
    return cell;
}

// MOSS holds the chord for the whole bar. One step, no rhythm — it is the bed
// everything else sits on.
function makeMossCell (rnd, p) {
    const cell = new Array (CELL).fill (null);
    cell[0] = {
        vel: 0.42 + rnd () * 0.12,
        nudge: 0,
        off: 0,
        dur: STEPS
    };
    return cell;
}

// SPINDLE climbs the chord. Mechanical and always consonant — the cheapest way
// to make a loop sound deliberate without the player doing anything.
function makeSpindleCell (rnd, p) {
    const every = p.density > 0.55 ? 1 : 2;
    const dir = rnd () < 0.5 ? 1 : -1;
    const start = Math.floor (rnd () * ARP_OFFSETS.length);
    const cell = new Array (CELL).fill (null);
    let i = 0;
    for (let s = 0; s < CELL; s++) {
        if (s % every !== 0) continue;
        let idx = (start + i * dir) % ARP_OFFSETS.length;
        if (idx < 0) idx += ARP_OFFSETS.length;
        cell[s] = {
            vel: 0.4 + rnd () * 0.18,
            nudge: nudgeFor (rnd, p),
            off: ARP_OFFSETS[idx],
            dur: every
        };
        i++;
    }
    return cell;
}

// --- assembly ------------------------------------------------------------

// One onset at the top of the bar, holding for the whole bar.
function singleToBar (cell) {
    const bar = new Array (STEPS).fill (null);
    if (cell[0] !== null) bar[0] = Object.assign ({}, cell[0]);
    return bar;
}

function tileToBar (cell) {
    const bar = new Array (STEPS);
    for (let s = 0; s < STEPS; s++) {
        const c = cell[s % CELL];
        bar[s] = c === null ? null : Object.assign ({}, c);
    }
    return bar;
}

// Turn a bar's stored scale-degree OFFSETS into absolute degrees against that
// bar's chord. This is the step that makes four bars of the same rhythm sound
// like a progression rather than a loop.
function harmonise (bar, root, isMoss) {
    for (let s = 0; s < STEPS; s++) {
        const st = bar[s];
        if (st === null) continue;
        st.degree = root + (isMoss ? 0 : st.off);
        delete st.off;
    }
}

function isMelodic (voice) {
    return MELODIC.indexOf (voice) !== -1;
}

export function generatePatterns (seed, params) {
    const prog = progressionFor (seed);

    const kickCell = makeKickCell (streamFor (seed, 'kick'), params);
    const cells = {
        kick:    kickCell,
        snare:   makeSnareCell (streamFor (seed, 'snare'), params),
        hat:     makeHatCell (streamFor (seed, 'hat'), params),
        bass:    makeBassCell (streamFor (seed, 'bass'), params, kickCell),
        lead:    makeLeadMotif (streamFor (seed, 'lead'), params),
        moss:    makeMossCell (streamFor (seed, 'moss'), params),
        spindle: makeSpindleCell (streamFor (seed, 'spindle'), params)
    };

    const out = {};
    for (let v = 0; v < VOICES.length; v++) {
        const voice = VOICES[v];
        const bars = new Array (BARS);
        for (let b = 0; b < BARS; b++) {
            // MOSS is the exception to tiling: one chord per bar, held. Tiling
            // its cell would retrigger the pad halfway through every bar, so it
            // would overlap itself and re-swell where nothing changed.
            bars[b] = voice === 'moss' ? singleToBar (cells[voice]) : tileToBar (cells[voice]);
            if (isMelodic (voice)) harmonise (bars[b], prog[b], voice === 'moss');
        }

        // Fills draw from their own stream, seeded per voice, so each voice's
        // turnaround differs but stays reproducible.
        const fillRnd = streamFor ((seed ^ (v * 0x9e37)) >>> 0, 'fill');
        applyFill (bars[HALF_FILL_BAR], voice, fillRnd, params, HALF_FILL_START,
                   prog[HALF_FILL_BAR], 0.55);
        applyFill (bars[FULL_FILL_BAR], voice, fillRnd, params, FULL_FILL_START,
                   prog[FULL_FILL_BAR], 1.0);

        out[voice] = bars;
    }

    return out;
}

/// Overwrite the tail of a bar. `weight` scales how busy the fill is, so the
/// half-phrase turnaround stays lighter than the end-of-phrase one.
/// Assigns every step in range, hit or miss — a miss clears the tiled hit that
/// was there, which is how a fill gets to leave a gap.
function applyFill (bar, voice, rnd, p, from, root, weight) {
    for (let s = from; s < STEPS; s++) {
        let prob, step = null;
        switch (voice) {
            case 'kick':
                prob = (0.3 + p.density * 0.35) * weight;
                if (rnd () < prob) step = { vel: 0.8 + rnd () * 0.15, nudge: nudgeFor (rnd, p) };
                break;
            case 'snare':
                prob = (0.6 + p.density * 0.35) * weight;
                if (rnd () < prob) step = { vel: 0.45 + rnd () * 0.5, nudge: nudgeFor (rnd, p) };
                break;
            case 'hat':
                prob = 0.75 * weight;
                if (rnd () < prob) step = { vel: 0.4 + rnd () * 0.35, nudge: nudgeFor (rnd, p), open: s === STEPS - 1 };
                break;
            case 'bass':
                prob = (0.5 + p.density * 0.3) * weight;
                if (rnd () < prob) step = { vel: 0.8 + rnd () * 0.2, nudge: nudgeFor (rnd, p), degree: root, dur: 1 };
                break;
            case 'lead':
                prob = (0.35 + p.density * 0.3) * weight;
                if (rnd () < prob) step = { vel: 0.65 + rnd () * 0.3, nudge: nudgeFor (rnd, p), degree: root + pick (rnd, CHORD_TONES), dur: 1 };
                break;
            case 'spindle':
                prob = 0.8 * weight;
                if (rnd () < prob) step = { vel: 0.4 + rnd () * 0.2, nudge: nudgeFor (rnd, p), degree: root + pick (rnd, ARP_OFFSETS), dur: 1 };
                break;
            case 'moss':
                // The pad rides straight through a fill. Cutting the harmony out
                // from under a turnaround is what makes it sound like a mistake.
                continue;
        }
        bar[s] = step;
    }
}

// Global step index -> the step object for a voice, or null.
export function stepAt (patterns, voice, globalStep) {
    const i = ((globalStep % TOTAL_STEPS) + TOTAL_STEPS) % TOTAL_STEPS;
    return patterns[voice][Math.floor (i / STEPS)][i % STEPS];
}

// The triad MOSS should sound for a given root degree.
export function chordTonesFor (root) {
    return CHORD_TONES.map (t => root + t);
}
