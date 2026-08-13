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
// Bars 0-2 are the base pattern. Bar 3 is the same pattern with its LAST FOUR
// STEPS replaced from a separate fill stream, so the phrase turns over with a
// fill instead of just repeating. Reads as music; still one seed.

import { streamFor } from './prng.js';

export const BARS = 4;
export const STEPS = 16;
export const TOTAL_STEPS = BARS * STEPS;
export const FILL_START = 12;          // fill occupies steps 12..15 of bar 3
export const FILL_BAR = BARS - 1;

export const VOICES = ['kick', 'snare', 'hat', 'bass', 'lead'];

// Degree pools, weighted by repetition. Root-heavy so the loop has a tonal
// centre no matter how alien the scale is.
const BASS_DEGREES = [0, 0, 0, 0, 2, 4, -3, 3, 1];
const LEAD_DEGREES = [0, 2, 4, 5, 7, 3, -1, 6];

function pick (rnd, arr) {
    return arr[Math.floor (rnd () * arr.length)];
}

function nudgeFor (rnd, p) {
    // Symmetric push around the grid line. Small, but it's what stops the
    // rhythm sounding like a drum machine.
    return (rnd () * 2 - 1) * p.nudgeSeconds;
}

// --- per-voice base bars -------------------------------------------------

function makeKick (rnd, p) {
    const bar = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        let prob;
        if (s === 0) prob = 1;                                        // anchor
        else if (s % 4 === 0) prob = 0.35 + p.density * 0.55;
        else if (s % 2 === 0) prob = p.density * 0.35 * (0.4 + p.syncopation);
        else prob = p.density * 0.22 * p.syncopation;
        if (rnd () < prob) {
            bar[s] = {
                vel: (s % 4 === 0 ? 0.95 : 0.6) - rnd () * 0.1,
                nudge: s === 0 ? 0 : nudgeFor (rnd, p)                 // never drift the downbeat
            };
        }
    }
    return bar;
}

function makeSnare (rnd, p) {
    const bar = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        let prob;
        if (s === 4 || s === 12) prob = 0.95;                          // backbeat
        else if (s % 2 === 1) prob = p.density * 0.28 * p.syncopation;  // ghosts
        else prob = p.density * 0.16 * p.syncopation;
        if (rnd () < prob) {
            const back = (s === 4 || s === 12);
            bar[s] = {
                vel: back ? 0.9 - rnd () * 0.08 : 0.25 + rnd () * 0.15,
                nudge: nudgeFor (rnd, p)
            };
        }
    }
    return bar;
}

function makeHat (rnd, p) {
    // Density picks the subdivision: quarters, eighths, or sixteenths.
    const interval = p.density < 0.4 ? 4 : (p.density < 0.7 ? 2 : 1);
    const bar = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        const onGrid = s % interval === 0;
        const prob = onGrid ? (1 - p.hatScatter * 0.35) : p.hatScatter * 0.3;
        if (rnd () < prob) {
            bar[s] = {
                vel: (s % 4 === 0 ? 0.7 : 0.42) - rnd () * 0.08,
                nudge: nudgeFor (rnd, p),
                open: rnd () < p.hatScatter * 0.25
            };
        }
    }
    return bar;
}

function makeBass (rnd, p) {
    const bar = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        let prob;
        if (s % 4 === 0) prob = 0.5 + p.density * 0.45;
        else if (s % 2 === 0) prob = p.density * 0.35;
        else prob = p.density * 0.2 * p.syncopation;
        if (rnd () < prob) {
            bar[s] = {
                vel: 0.75 + rnd () * 0.2,
                nudge: nudgeFor (rnd, p),
                degree: s === 0 ? 0 : pick (rnd, BASS_DEGREES),        // land on root
                dur: 1 + Math.floor (rnd () * 2)
            };
        }
    }
    return bar;
}

function makeLead (rnd, p) {
    const bar = new Array (STEPS).fill (null);
    for (let s = 0; s < STEPS; s++) {
        let prob;
        if (s % 8 === 0) prob = 0.45 + p.density * 0.25;
        else if (s % 2 === 0) prob = p.density * 0.26;
        else prob = p.density * 0.12 * p.syncopation;
        if (rnd () < prob) {
            bar[s] = {
                vel: 0.6 + rnd () * 0.25,
                nudge: nudgeFor (rnd, p),
                degree: pick (rnd, LEAD_DEGREES),
                dur: 1 + Math.floor (rnd () * 4)
            };
        }
    }
    return bar;
}

const BUILDERS = {
    kick: makeKick, snare: makeSnare, hat: makeHat, bass: makeBass, lead: makeLead
};

// --- fill ----------------------------------------------------------------

// Overwrite the last four steps of the final bar. Drums get busier, melodic
// voices get re-pitched, so the phrase turnaround is audible.
function applyFill (bar, voice, rnd, p) {
    for (let s = FILL_START; s < STEPS; s++) {
        let prob, step = null;
        switch (voice) {
            case 'kick':
                prob = 0.3 + p.density * 0.35;
                if (rnd () < prob) step = { vel: 0.8 + rnd () * 0.15, nudge: nudgeFor (rnd, p) };
                break;
            case 'snare':
                prob = 0.6 + p.density * 0.35;
                if (rnd () < prob) step = { vel: 0.45 + rnd () * 0.5, nudge: nudgeFor (rnd, p) };
                break;
            case 'hat':
                prob = 0.75;
                if (rnd () < prob) step = { vel: 0.4 + rnd () * 0.35, nudge: nudgeFor (rnd, p), open: s === STEPS - 1 };
                break;
            case 'bass':
                prob = 0.5 + p.density * 0.3;
                if (rnd () < prob) step = { vel: 0.8 + rnd () * 0.2, nudge: nudgeFor (rnd, p), degree: pick (rnd, BASS_DEGREES), dur: 1 };
                break;
            case 'lead':
                prob = 0.35 + p.density * 0.3;
                if (rnd () < prob) step = { vel: 0.65 + rnd () * 0.3, nudge: nudgeFor (rnd, p), degree: pick (rnd, LEAD_DEGREES), dur: 1 + Math.floor (rnd () * 2) };
                break;
        }
        bar[s] = step;
    }
    return bar;
}

// --- public --------------------------------------------------------------

export function generatePatterns (seed, params) {
    const out = {};
    for (let v = 0; v < VOICES.length; v++) {
        const voice = VOICES[v];
        const base = BUILDERS[voice] (streamFor (seed, voice), params);

        const bars = new Array (BARS);
        for (let b = 0; b < BARS; b++) {
            // Copy — the fill must not mutate the bars it was cloned from.
            bars[b] = base.map (function (s) { return s === null ? null : Object.assign ({}, s); });
        }
        // Fill draws from its own stream, seeded per voice so each voice's
        // turnaround is different but reproducible.
        applyFill (bars[FILL_BAR], voice, streamFor ((seed ^ (v * 0x9e37)) >>> 0, 'fill'), params);
        out[voice] = bars;
    }
    return out;
}

// Global step index -> the step object for a voice, or null.
export function stepAt (patterns, voice, globalStep) {
    const i = ((globalStep % TOTAL_STEPS) + TOTAL_STEPS) % TOTAL_STEPS;
    return patterns[voice][Math.floor (i / STEPS)][i % STEPS];
}
