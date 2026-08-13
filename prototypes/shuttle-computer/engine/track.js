// What a track IS.
//
// Six dials shape the sound, six modules choose the parts, one key sets where
// it sits. That whole thing is the track — and it is what a cassette will
// eventually store.
//
// ── The important change: the pattern seed no longer comes from the dials ──
// It used to. That is why turning PULSE up produced a DIFFERENT track that
// happened to be faster, instead of making your track busier — and why the
// whole thing felt like re-rolling dice rather than writing something.
//
// Now each voice seeds from its module's PRESET and VARIATION only. The dials
// feed the generator as parameters, so they change how a pattern is filled in
// without changing which pattern it is. VARIATION is the re-roll, and it is
// per-module and repeatable, so you can keep drums you like while cycling
// melodies.

import { fnv1a32, VOICE_CONST } from './prng.js';
import { DEFAULT_DIALS } from './params.js';
import { MODULE_NAMES, PRESET_COUNT, VARIATION_COUNT } from './presets.js';

export const MODULE_FOR_VOICE = {
    kick: 'THUMPER', snare: 'THUMPER', hat: 'THUMPER',
    bass: 'GLOWORM', lead: 'SIREN', moss: 'MOSS', spindle: 'SPINDLE'
};

export const KEY_NAMES = ['A', 'A#', 'B', 'C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#'];

export function defaultTrack () {
    return {
        dials: Object.assign ({}, DEFAULT_DIALS),
        key: 0,                                   // semitones above the base root (A)
        preset:    { THUMPER: 0, GLOWORM: 0, MOSS: 0, SIREN: 1, SPINDLE: 0, CAVE: 1 },
        variation: { THUMPER: 0, GLOWORM: 0, MOSS: 0, SIREN: 0, SPINDLE: 0, CAVE: 0 }
    };
}

export function cloneTrack (t) {
    return {
        dials: Object.assign ({}, t.dials),
        key: t.key,
        preset: Object.assign ({}, t.preset),
        variation: Object.assign ({}, t.variation)
    };
}

function wrap (v, n) {
    return ((v % n) + n) % n;
}

export function setPreset (track, module, index) {
    const t = cloneTrack (track);
    t.preset[module] = wrap (index, PRESET_COUNT);
    return t;
}

export function setVariation (track, module, index) {
    const t = cloneTrack (track);
    t.variation[module] = wrap (index, VARIATION_COUNT);
    return t;
}

export function setKey (track, key) {
    const t = cloneTrack (track);
    t.key = wrap (key, 12);
    return t;
}

export function keyName (key) {
    return KEY_NAMES[wrap (key, 12)];
}

/// The stream a voice generates from. Deliberately NOT a function of the dials.
export function voiceSeed (track, voice) {
    const mod = MODULE_FOR_VOICE[voice];
    const bytes = [
        MODULE_NAMES.indexOf (mod) & 0xff,
        wrap (track.preset[mod], PRESET_COUNT) & 0xff,
        wrap (track.variation[mod], VARIATION_COUNT) & 0xff
    ];
    return (fnv1a32 (bytes) ^ VOICE_CONST[voice]) >>> 0;
}

/// Fills get their own stream per voice, so a turnaround never disturbs the
/// groove it decorates.
export function fillSeed (track, voice) {
    return (voiceSeed (track, voice) ^ VOICE_CONST.fill) >>> 0;
}

/// A display/identity hash over EVERYTHING that affects the sound. This is the
/// value a cassette is keyed on, so it must cover dials, key, presets and
/// variations — not just the dials the way the old seed did.
export function trackId (track) {
    const bytes = [];
    for (const k of ['pulse', 'crunch', 'goo', 'void', 'jitter', 'warp']) {
        let q = Math.round (track.dials[k] * 2);
        if (q < 0) q = 0;
        if (q > 20) q = 20;
        bytes.push (q);
    }
    bytes.push (wrap (track.key, 12));
    for (const m of MODULE_NAMES) {
        bytes.push (wrap (track.preset[m], PRESET_COUNT));
        bytes.push (wrap (track.variation[m], VARIATION_COUNT));
    }
    return fnv1a32 (bytes);
}

/// Which changes require regenerating patterns. Presets, variations and the
/// pattern-shaping dials do; key and timbre dials do not — key is applied at
/// note time and timbre rides live.
export function needsRegen (a, b) {
    for (const k of ['pulse', 'void', 'jitter', 'warp'])
        if (Math.round (a.dials[k] * 2) !== Math.round (b.dials[k] * 2)) return true;
    for (const m of MODULE_NAMES) {
        if (a.preset[m] !== b.preset[m]) return true;
        if (a.variation[m] !== b.variation[m]) return true;
    }
    return false;
}
