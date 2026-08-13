// Preset banks: the parts you CHOOSE, as opposed to the dials, which shape.
//
// ── Why templates and not more probability tuning ────────────────────────
// Sam: "it doesnt really feel like you are the one creating the music... it
// feels like your just editing some chord and drum loops that are already
// there." The fix is to give the player real decisions whose outcomes are all
// good, rather than a finer grip on the dice.
//
// So the things that should be RECOGNISABLE are authored by hand — drum
// grooves, arp shapes, chord progressions. The things that should vary are
// still generated, with the preset setting their character. Mixing the two is
// deliberate: a named groove you can pick beats a groove you rolled, but a
// melody that is identical every time you pick "SONG" would be the same
// preset-loop problem one level up.
//
// ── Weight strings ───────────────────────────────────────────────────────
// Rhythm templates are 16 characters, one per step:
//    3  always sounds        — the skeleton of the groove
//    2  usually sounds       — thins out at low PULSE
//    1  optional / ghost     — fills in at high PULSE
//    .  never
// The preset gives the shape, PULSE decides how busy, and VARIATION decides
// which of the 2s and 1s actually land. That is what lets one groove get
// busier without becoming a different groove.

export const WEIGHT_ALWAYS = 3;

export function parseWeights (s) {
    const out = new Array(16).fill(0);
    let i = 0;
    for (const ch of s) {
        if (ch === ' ' || ch === '|') continue;      // spacing is decorative
        out[i++] = ch === '.' ? 0 : (ch.charCodeAt(0) - 48);
        if (i >= 16) break;
    }
    return out;
}

// ── THUMPER — five grooves ───────────────────────────────────────────────
export const THUMPER = [
    { name: 'STRAIGHT',
      kick:  '3...2..1 3...2..1',
      snare: '....3..1 ....3..2',
      hat:   '2.2.2.2. 2.2.2.2.' },

    { name: 'BREAK',
      kick:  '3..2..1. ..3.2..1',
      snare: '....3.1. ..1.3..2',
      hat:   '2.211.21 2.211.22' },

    { name: 'STOMP',
      kick:  '3.1.3.1. 3.1.3.1.',
      snare: '....3... ....3...',
      hat:   '3...2... 3...2...' },

    { name: 'HALFTIME',
      kick:  '3.....1. ....2...',
      snare: '........ 3.....1.',
      hat:   '2...1...2...1...' },

    { name: 'SCATTER',
      kick:  '3..1.2.1 3.1..2.1',
      snare: '..1.3.1. .21.3.11',
      hat:   '32323232 32323232' }
];

// ── GLOWORM — five basslines ─────────────────────────────────────────────
// `contour` is scale-degree offsets from the bar's chord root, one per step;
// only the steps that actually sound read theirs.
export const GLOWORM = [
    { name: 'ROOTS',
      hits:    '3...2...3...2...',
      contour: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] },

    { name: 'OCTAVE',
      hits:    '3..12..13..12..1',
      contour: [0, 0, 0, -7, 0, 0, 0, -7, 0, 0, 0, -7, 0, 0, 0, -7] },

    { name: 'WALK',
      hits:    '3.2.2.1.3.2.2.1.',
      contour: [0, 0, 2, 2, 4, 4, 2, 2, 0, 0, 2, 2, 4, 4, 5, 5] },

    { name: 'PULSE',
      hits:    '3232323232323232',
      contour: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] },

    { name: 'SLIDE',
      hits:    '3..2.1.23..2.1.2',
      contour: [0, 0, 0, 4, 0, 2, 0, -7, 0, 0, 0, 4, 0, 2, 0, 5] }
];

// ── MOSS — five progressions ─────────────────────────────────────────────
// One chord per bar, in scale degrees. This is the single biggest musical
// choice in the app: it sets what bass, lead and arp all harmonise to, whether
// or not the pad itself is switched on. All start on the tonic so the phrase
// always has an anchor.
export const MOSS = [
    { name: 'HOLLOW', prog: [0, 5, 3, 4] },
    { name: 'CLIMB',  prog: [0, 2, 3, 4] },
    { name: 'FALL',   prog: [0, 4, 3, 5] },
    { name: 'VAMP',   prog: [0, 0, 3, 4] },
    { name: 'STEP',   prog: [0, 3, 4, 0] }
];

// Pad rhythms, chosen by MOSS's VARIATION rather than its preset — so the
// progression and how it is played are separate decisions.
export const MOSS_RHYTHMS = [
    { name: 'held',    hits: '3...............', dur: 16 },
    { name: 'halves',  hits: '3.......3.......', dur: 8 },
    { name: 'quarters',hits: '3...3...3...3...', dur: 4 },
    { name: 'offbeat', hits: '..3...3...3...3.', dur: 3 },
    { name: 'swell',   hits: '3.......2.......', dur: 8 },
    { name: 'stabs',   hits: '3.3.....3.3.....', dur: 2 },
    { name: 'long',    hits: '3...............', dur: 16 },
    { name: 'push',    hits: '3.....3.....3...', dur: 5 }
];

// ── SIREN — five melodic characters ──────────────────────────────────────
// The lead is still GENERATED (a preset that always played the same tune would
// be the preset-loop problem again). The preset sets its character:
//   gate     how often it plays at all
//   leap     chance of a jump instead of stepwise motion
//   len      note length in steps
//   bars     which bars of the phrase it plays in (call and response)
export const SIREN = [
    { name: 'SPARSE', gate: 0.35, leap: 0.10, len: 3, bars: [1, 1, 1, 1] },
    { name: 'SONG',   gate: 0.60, leap: 0.12, len: 2, bars: [1, 1, 1, 1] },
    { name: 'BUSY',   gate: 0.85, leap: 0.18, len: 1, bars: [1, 1, 1, 1] },
    { name: 'ANSWER', gate: 0.65, leap: 0.14, len: 2, bars: [0, 1, 0, 1] },
    { name: 'HELD',   gate: 0.25, leap: 0.06, len: 6, bars: [1, 0, 1, 1] }
];

// ── SPINDLE — five arp shapes ────────────────────────────────────────────
// Exact sequences: an arp that rolled its own order would just be a fast
// random melody. `shape` indexes chord tones, extended upward.
export const SPINDLE = [
    { name: 'UP',     shape: [0, 1, 2, 3] },
    { name: 'DOWN',   shape: [3, 2, 1, 0] },
    { name: 'ROLL',   shape: [0, 1, 2, 3, 2, 1] },
    { name: 'JUMP',   shape: [0, 2, 1, 3] },
    { name: 'TUMBLE', shape: [0, 3, 1, 2, 0, 2] }
];

// Scale-degree offsets the arp shape indexes into.
export const ARP_TONES = [0, 2, 4, 6];

// ── CAVE — five spaces ───────────────────────────────────────────────────
// Effect settings, not patterns. VOID stays under the feedback cap for the
// same reason it always did: the loop has to decay.
export const CAVE = [
    { name: 'ROOM',   timeA: 0.07, timeB: 0.11, damp: 3200, fb: 0.55 },
    { name: 'HALL',   timeA: 0.19, timeB: 0.31, damp: 2600, fb: 0.80 },
    { name: 'CANYON', timeA: 0.37, timeB: 0.53, damp: 1600, fb: 0.92 },
    { name: 'SLAP',   timeA: 0.12, timeB: 0.125, damp: 4200, fb: 0.20 },
    { name: 'VOID',   timeA: 0.29, timeB: 0.47, damp: 1100, fb: 0.97 }
];

export const BANKS = {
    THUMPER: THUMPER, GLOWORM: GLOWORM, MOSS: MOSS,
    SIREN: SIREN, SPINDLE: SPINDLE, CAVE: CAVE
};

export const MODULE_NAMES = ['THUMPER', 'GLOWORM', 'MOSS', 'SIREN', 'SPINDLE', 'CAVE'];
export const PRESET_COUNT = 5;
export const VARIATION_COUNT = 8;

export function presetName (module, index) {
    const bank = BANKS[module];
    return bank[((index % bank.length) + bank.length) % bank.length].name;
}
