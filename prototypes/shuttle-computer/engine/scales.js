// Pitch tables + degree -> Hz. Pure maths, ports verbatim.
//
// Ordered by FAMILIARITY: index 0 is maximally alien, index 5 is familiar and
// melancholic. The WARP dial runs the other way (10 = alien), so params.js
// inverts it before indexing here — this table is NOT the dial.
// The handoff listed whole-tone before chromatic-cluster; swapped here so
// alienness decreases monotonically across the sweep (otherwise the dial feels
// broken in the middle). One-line change if Sam prefers the original.

export const SCALES = [
    { name: 'CLUSTER',   steps: [0, 1, 2, 6, 7, 8] },        // two semitone clusters a tritone apart
    { name: 'WHOLETONE', steps: [0, 2, 4, 6, 8, 10] },       // no leading tone, floats
    { name: 'HIRAJOSHI', steps: [0, 2, 3, 7, 8] },           // japanese pentatonic, alien-but-pretty
    { name: 'PHRYGIAN',  steps: [0, 1, 3, 5, 7, 8, 10] },    // flat-2 darkness
    { name: 'MINORPENT', steps: [0, 3, 5, 7, 10] },          // familiar, safe
    { name: 'NATMINOR',  steps: [0, 2, 3, 5, 7, 8, 10] }     // fully familiar
];

// Master key is fixed for the whole game. A2.
export const ROOT_MIDI = 45;

// Octave offset per melodic voice. Lives here rather than in the audio backend
// so the Web Audio and Unity backends can't drift apart on register — and so
// the test suite checks the same numbers the synth uses.
//
// Bass sits at -1, not -2: at -2 the low end of the CLUSTER scale reaches
// ~19Hz, which is below hearing and does nothing but eat headroom.
// MOSS sits in the middle where a pad belongs — under the lead, above the bass,
// so the triad fills the gap between them instead of fighting either.
export const VOICE_OCTAVE = { bass: -1, lead: 1, moss: 0, spindle: 1 };

// Takes FAMILIARITY (0 = alien, 10 = familiar), not the WARP dial value.
export function scaleIndexFor (familiarity) {
    let i = Math.floor ((familiarity / 10) * SCALES.length);
    if (i < 0) i = 0;
    if (i >= SCALES.length) i = SCALES.length - 1;
    return i;
}

// Degree may run past the end of the table (or negative) — it wraps into higher
// or lower octaves, so callers can just ask for "degree 9" and get something
// sensible instead of clamping to the top note.
export function degreeToMidi (degree, scaleIdx, octaveOffset) {
    const steps = SCALES[scaleIdx].steps;
    const n = steps.length;
    let oct = Math.floor (degree / n);
    let d = degree - oct * n;          // true modulo, negatives included
    return ROOT_MIDI + steps[d] + 12 * (oct + (octaveOffset || 0));
}

export function midiToFreq (midi) {
    return 440 * Math.pow (2, (midi - 69) / 12);
}

// `key` transposes everything by whole semitones. It is applied HERE, at note
// time, rather than being folded into scale degrees — so turning the key knob
// can never regenerate a pattern, it just moves the same one.
export function degreeToFreq (degree, scaleIdx, octaveOffset, key) {
    return midiToFreq (degreeToMidi (degree, scaleIdx, octaveOffset) + (key || 0));
}

// Register each voice is allowed to occupy, in MIDI notes.
//
// Without this the bass drops to ~22Hz on the CLUSTER scale (its lowest degree
// lands two octaves down in a 6-note table) — inaudible rumble that eats
// headroom and does nothing but make everything else quieter. Folding by whole
// OCTAVES keeps the note in the scale, so this can never introduce a wrong
// pitch, only a wrong-by-an-octave one, and only where the alternative was
// silence.
export const VOICE_RANGE = {
    bass:    [28, 55],
    lead:    [52, 84],
    moss:    [45, 74],
    spindle: [55, 88]
};

export function voiceMidi (degree, scaleIdx, voice, key) {
    let m = degreeToMidi (degree, scaleIdx, VOICE_OCTAVE[voice]) + (key || 0);
    const r = VOICE_RANGE[voice];
    if (!r) return m;
    while (m < r[0]) m += 12;
    while (m > r[1]) m -= 12;
    return m;
}

export function voiceFreq (degree, scaleIdx, voice, key) {
    return midiToFreq (voiceMidi (degree, scaleIdx, voice, key));
}

// True iff a MIDI note is a member of the scale, in any octave. Used by the
// test suite to prove no voice ever plays an out-of-scale note.
export function isInScale (midi, scaleIdx) {
    const steps = SCALES[scaleIdx].steps;
    let pc = (midi - ROOT_MIDI) % 12;
    if (pc < 0) pc += 12;
    return steps.indexOf (pc) !== -1;
}
