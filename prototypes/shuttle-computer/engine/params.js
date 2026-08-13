// The macro table: six dials -> every number the audio backend needs.
//
// This is the single place dial semantics live. The Web Audio backend and the
// future Unity backend both consume this same flat object, so "what does GOO
// do" has exactly one answer in the codebase.

import { scaleIndexFor } from './scales.js';

export const DIAL_DEFS = [
    { key: 'pulse',    label: 'PULSE',    flavor: 'how fast it hits' },
    { key: 'crunch',   label: 'CRUNCH',   flavor: 'how mean it sounds' },
    { key: 'goo',      label: 'GOO',      flavor: 'how wet and squelchy' },
    { key: 'void',     label: 'VOID',     flavor: 'how much empty space' },
    { key: 'jitter',   label: 'JITTER',   flavor: 'how twitchy the rhythm' },
    { key: 'warp',     label: 'WARP',     flavor: 'how warped the pitch is' }
];

export const DEFAULT_DIALS = {
    pulse: 5, crunch: 3, goo: 5, void: 4, jitter: 4, warp: 5
};

export function computeParams (dials, key) {
    const pulse    = dials.pulse    / 10;
    const crunch   = dials.crunch   / 10;
    const goo      = dials.goo      / 10;
    const voidness = dials.void     / 10;
    const jitter   = dials.jitter   / 10;
    const warp     = dials.warp     / 10;

    // VOID eats note density — empty space is partly just fewer events.
    const density = (0.25 + pulse * 0.5) * (1 - voidness * 0.5);

    return {
        // --- clock ---
        bpm: 60 + pulse * 110,               // 60..170
        density: density,

        // --- timbre ---
        // 0 = sine, 0.5 = saw, 1 = square. Backends crossfade a pair of oscs.
        oscMorph: crunch,
        drive: crunch,                        // waveshaper amount
        // Amplitude quantization. 16 levels at full crunch is audibly gritty
        // without turning the whole mix into a buzz.
        crushLevels: Math.round (64 - crunch * 48),

        // --- filter (GOO) ---
        // Open and clean at 0, closed and squelchy at 10.
        filterBase: 400 * Math.pow (2, (1 - goo) * 3),   // 3200Hz .. 400Hz
        filterQ: 1 + goo * 18,
        lfoRate: 0.2 + goo * 2.8,             // Hz
        lfoDepthOct: goo * 2,                 // octaves of cutoff sweep

        // --- CAVE (VOID) ---
        caveSend: voidness * 0.8,
        caveFeedback: 0.2 + voidness * 0.65,
        caveMix: 0.2 + voidness * 0.6,

        // --- rhythm (JITTER) ---
        syncopation: jitter,                  // probability of off-beat placement
        nudgeSeconds: jitter * 0.02,          // max off-grid push, 0..20ms
        hatScatter: jitter,

        // --- pitch (WARP) ---
        // WARP runs the opposite way to the other five: 0 is straight and
        // melodic, 10 is maximally warped. The scale table is still ordered
        // alien-first, so the dial is inverted HERE and nowhere else.
        scaleIdx: scaleIndexFor (10 - dials.warp),
        detuneCents: warp * 35,               // warped = detuned

        // Transposition, in semitones. Applied at note time, not baked into
        // degrees — so changing key never regenerates a pattern.
        key: key === undefined ? 0 : key,

        // Kept so backends and the classifier readout can see the raw vector.
        dials: Object.assign ({}, dials)
    };
}

// Which dials reshape the pattern (applied at the next bar boundary) vs. which
// ride live. PULSE is in both camps — BPM follows your hand, but its density
// term changes which optional hits land.
//
// needsRegen now lives in track.js, because presets and variations regenerate
// too and the decision has to consider the whole track, not just the dials.
export const PATTERN_KEYS = ['pulse', 'void', 'jitter', 'warp'];
