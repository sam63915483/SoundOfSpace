// Seeded randomness + the dial-vector hash.
//
// PORT NOTE (C#): every operation here is chosen to be byte-exact in C#.
//   Math.imul(a, b)  ->  unchecked(a * b)   on int
//   x >>> 0          ->  cast to uint
//   x >>> n          ->  (uint)x >> n
// Do not "simplify" any of this. Cassettes printed by an old build must sound
// identical on a new one, on every machine.

// FNV-1a, 32-bit. Operates on a byte array.
export function fnv1a32 (bytes) {
    let h = 0x811c9dc5 >>> 0;
    for (let i = 0; i < bytes.length; i++) {
        h ^= bytes[i] & 0xff;
        h = Math.imul (h, 0x01000193) >>> 0;
    }
    return h >>> 0;
}

// mulberry32 — small, fast, good enough distribution, trivially portable.
// Returns a function producing floats in [0, 1).
export function mulberry32 (seed) {
    let a = seed >>> 0;
    return function () {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul (t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul (t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

// Dials are continuous 0-10 in the UI but quantize to 0.5 steps for seeding, so
// nudging a knob by a hair doesn't silently reroll the whole pattern. Six ints
// in 0..20, in fixed dial order.
export const DIAL_ORDER = ['pulse', 'crunch', 'goo', 'void', 'jitter', 'homesick'];

export function quantizeDials (dials) {
    const out = new Array (DIAL_ORDER.length);
    for (let i = 0; i < DIAL_ORDER.length; i++) {
        const v = dials[DIAL_ORDER[i]];
        let q = Math.round ((v == null ? 0 : v) * 2);
        if (q < 0) q = 0;
        if (q > 20) q = 20;
        out[i] = q;
    }
    return out;
}

export function seedFromDials (dials) {
    return fnv1a32 (quantizeDials (dials));
}

// Each voice draws from its own stream. When a 7th plugin unlocks later, its
// constant is simply a new entry — every existing voice's pattern is unchanged,
// so cassettes printed before the unlock still sound the same.
export const VOICE_CONST = {
    kick:  0x9e3779b1,
    snare: 0x85ebca6b,
    hat:   0xc2b2ae35,
    bass:  0x27d4eb2f,
    lead:  0x165667b1,
    // Fills draw from a separate stream so bar 3's variation is independent of
    // the pattern it decorates.
    fill:  0xd3a2646c
};

export function streamFor (seed, voice) {
    const c = VOICE_CONST[voice];
    if (c === undefined) throw new Error ('unknown voice: ' + voice);
    return mulberry32 ((seed ^ c) >>> 0);
}
