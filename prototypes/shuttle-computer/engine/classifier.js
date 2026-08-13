// Genre classifier. Ten centres in 6-D dial space; nearest wins.
//
// Centres are placeholders from the handoff — Sam tunes these by ear at the
// review gate. The maths must be identical in the eventual C# port because
// alien reactions and radio playback key off the resulting label.

import { DIAL_ORDER } from './prng.js';

// [PULSE, CRUNCH, GOO, VOID, JITTER, WARP]
export const GENRES = [
    { name: 'GLORP',    adj: 'Glorpy',   vibe: 'wet squelchy bass funk',     c: [6, 3, 9, 3, 5, 6] },
    { name: 'DRIFT',    adj: 'Drifty',   vibe: 'weightless space drone',     c: [1, 1, 3, 9, 1, 4] },
    { name: 'SKITTER',  adj: 'Skittish', vibe: 'fast twitchy scatter-beats', c: [9, 4, 3, 3, 9, 7] },
    { name: 'SLUDJ',    adj: 'Sludjy',   vibe: 'slow crushing heaviness',    c: [2, 9, 6, 5, 2, 8] },
    { name: 'CHIRP',    adj: 'Chirpy',   vibe: 'bright bouncy cute',         c: [7, 2, 2, 2, 4, 1] },
    { name: 'NULLGAZE', adj: 'Null',     vibe: 'hazy sad washed-out',        c: [3, 5, 3, 8, 1, 2] },
    { name: 'THRUM',    adj: 'Thrummy',  vibe: 'hypnotic ritual percussion', c: [5, 3, 5, 4, 7, 9] },
    { name: 'VOLT',     adj: 'Volted',   vibe: 'aggressive electric dance',  c: [8, 7, 4, 2, 5, 5] },
    { name: 'WARBLE',   adj: 'Warbly',   vibe: 'woozy detuned seasick psych', c: [4, 4, 7, 6, 3, 3] },
    { name: 'CLANG',    adj: "Clangin'", vibe: 'metallic industrial banger', c: [6, 8, 2, 5, 8, 9] }
];

// A blend label shows when the runner-up is within this distance OF THE WINNER
// (i.e. d2 - d1 <= threshold), not when its absolute distance is small.
export const DEFAULT_BLEND_THRESHOLD = 1.5;

export function dialsToVector (dials) {
    const v = new Array (DIAL_ORDER.length);
    for (let i = 0; i < DIAL_ORDER.length; i++) v[i] = dials[DIAL_ORDER[i]];
    return v;
}

function distance (a, b) {
    let sum = 0;
    for (let i = 0; i < a.length; i++) {
        const d = a[i] - b[i];
        sum += d * d;
    }
    return Math.sqrt (sum);
}

// Returns { label, primary, secondary, blended, d1, d2 }.
export function classify (dials, threshold) {
    const t = threshold == null ? DEFAULT_BLEND_THRESHOLD : threshold;
    const v = dialsToVector (dials);

    let i1 = 0, d1 = Infinity, i2 = -1, d2 = Infinity;
    for (let i = 0; i < GENRES.length; i++) {
        const d = distance (v, GENRES[i].c);
        if (d < d1) { i2 = i1; d2 = d1; i1 = i; d1 = d; }
        else if (d < d2) { i2 = i; d2 = d; }
    }

    const primary = GENRES[i1];
    const secondary = GENRES[i2];
    const blended = (d2 - d1) <= t;

    return {
        label: blended ? secondary.adj + ' ' + primary.name : primary.name,
        primary: primary,
        secondary: secondary,
        blended: blended,
        d1: d1,
        d2: d2
    };
}
