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
export const VOICE_OCTAVE = { bass: -1, lead: 1 };

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

export function degreeToFreq (degree, scaleIdx, octaveOffset) {
    return midiToFreq (degreeToMidi (degree, scaleIdx, octaveOffset));
}

// True iff a MIDI note is a member of the scale, in any octave. Used by the
// test suite to prove no voice ever plays an out-of-scale note.
export function isInScale (midi, scaleIdx) {
    const steps = SCALES[scaleIdx].steps;
    let pc = (midi - ROOT_MIDI) % 12;
    if (pc < 0) pc += 12;
    return steps.indexOf (pc) !== -1;
}
