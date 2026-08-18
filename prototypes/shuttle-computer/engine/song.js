// What a SONG is: an ordered list of SECTIONS, each owning a whole track plus
// a length in bars. This is the arrangement layer on top of the 4-bar loop —
// "8 bars of one thing, then 2 bars of another".
//
// ── Ownership discipline (same as library records) ───────────────────────
// A section owns a CLONED track, never a pointer into another section or a
// shelf record. Editing section B must never reach into section A, even when B
// was created by duplicating A. Every mutator here returns a NEW song object
// and never touches its input — same style as track.js.
//
// ── Playback semantics ───────────────────────────────────────────────────
// A section's track still generates the usual 4-bar phrase. The section's BARS
// says how long the song stays on it: 8 bars = the phrase twice, 2 bars = just
// its first half (which lands on the half fill — a natural turnaround). So the
// generator is untouched and every existing determinism guarantee holds
// per-section.
//
// Pure logic, no DOM, no audio: ports 1:1 to C# like the rest of engine/.

import { cloneTrack, trackId, defaultTrack } from './track.js';
import { classify } from './classifier.js';
import { fnv1a32, DIAL_ORDER } from './prng.js';
import { STEPS, BARS, FULL_FILL_BAR } from './patterns.js';

export const SECTION_MIN_BARS = 1;
export const SECTION_MAX_BARS = 16;
export const MAX_SECTIONS = 8;

function clampBars (b) {
    const n = Math.round (Number (b));
    if (!Number.isFinite (n)) return 4;
    return Math.min (SECTION_MAX_BARS, Math.max (SECTION_MIN_BARS, n));
}

export function makeSection (track, bars) {
    return { bars: clampBars (bars), track: cloneTrack (track) };
}

export function defaultSong () {
    return { sections: [makeSection (defaultTrack (), 4)] };
}

/// Wraps a legacy single-track project: one 4-bar section. This is what every
/// record saved before songs existed becomes on load.
export function songFromTrack (track) {
    return { sections: [makeSection (track, 4)] };
}

export function cloneSong (song) {
    return { sections: song.sections.map (s => makeSection (s.track, s.bars)) };
}

/// Sections are lettered, not numbered — "SEC B" reads like an arrangement,
/// "SEC 2" reads like an index.
export function sectionLabel (i) {
    return String.fromCharCode (65 + (i % 26));
}

// ------------------------------------------------------------- mutators ----
// All return a new song. An out-of-range index returns the input unchanged
// rather than throwing — the UI's buttons are the guard, this is the backstop.

export function addSection (song, copyIndex) {
    if (song.sections.length >= MAX_SECTIONS) return song;
    const s = cloneSong (song);
    const i = Math.min (Math.max (copyIndex == null ? s.sections.length - 1 : copyIndex, 0),
                        s.sections.length - 1);
    // A new section starts as a copy of the one you were just editing — a blank
    // default in the middle of a song is never what anyone wants.
    s.sections.splice (i + 1, 0, makeSection (s.sections[i].track, 4));
    return s;
}

export function removeSection (song, index) {
    if (song.sections.length <= 1) return song;         // a song is never empty
    if (index < 0 || index >= song.sections.length) return song;
    const s = cloneSong (song);
    s.sections.splice (index, 1);
    return s;
}

export function moveSection (song, index, delta) {
    const to = index + (delta < 0 ? -1 : 1);
    if (index < 0 || index >= song.sections.length) return song;
    if (to < 0 || to >= song.sections.length) return song;
    const s = cloneSong (song);
    const [sec] = s.sections.splice (index, 1);
    s.sections.splice (to, 0, sec);
    return s;
}

export function setSectionBars (song, index, bars) {
    if (index < 0 || index >= song.sections.length) return song;
    const s = cloneSong (song);
    s.sections[index].bars = clampBars (bars);
    return s;
}

export function setSectionTrack (song, index, track) {
    if (index < 0 || index >= song.sections.length) return song;
    const s = cloneSong (song);
    s.sections[index].track = cloneTrack (track);
    return s;
}

// -------------------------------------------------------------- queries ----

export function totalBars (song) {
    let n = 0;
    for (const s of song.sections) n += s.bars;
    return n;
}

export function totalSteps (song) {
    return totalBars (song) * STEPS;
}

/// Identity over EVERYTHING audible: each section's full track identity plus
/// its length, in order. Reordering sections is a different song; so is
/// stretching one. This is what a printed full-track cassette keys on.
export function songId (song) {
    const bytes = [];
    for (const s of song.sections) {
        const id = trackId (s.track);
        bytes.push (id & 0xff, (id >>> 8) & 0xff, (id >>> 16) & 0xff, (id >>> 24) & 0xff);
        bytes.push (s.bars & 0xff);
    }
    return fnv1a32 (bytes);
}

/// Which section is under a given song-step (0..totalSteps-1)?
/// Returns { index, stepInSection, barInSection } — the UI playhead and the
/// audio scheduler both use this, so they can never disagree.
export function sectionAtStep (song, step) {
    let start = 0;
    for (let i = 0; i < song.sections.length; i++) {
        const len = song.sections[i].bars * STEPS;
        if (step < start + len)
            return { index: i, stepInSection: step - start, barInSection: Math.floor ((step - start) / STEPS) };
        start += len;
    }
    return { index: 0, stepInSection: 0, barInSection: 0 };
}

// ---------------------------------------------------------- transitions ----
// The section boundary treatment. All of it derives purely from the two
// adjacent sections, so a printed cassette transitions identically on every
// machine — same determinism contract as the patterns themselves.

/// Which bar of the generated 4-bar phrase sounds at this bar of the section.
/// The LAST bar of a section always plays the phrase's fill bar, so every
/// section hands off with a turnaround, whatever its length. (For 4/8/12/16
/// bar sections this is a no-op — their last bar lands on the fill bar
/// naturally.)
export function patternBarFor (section, barInSection) {
    if (barInSection === section.bars - 1) return FULL_FILL_BAR;
    return barInSection % BARS;
}

export function patternStepFor (section, stepInSection) {
    const bar = Math.floor (stepInSection / STEPS);
    return patternBarFor (section, bar) * STEPS + (stepInSection % STEPS);
}

/// How different two sections sound, 0..1 — distance in dial space, since the
/// dials carry tempo, timbre and mood. Identical dials = 0 even if the parts
/// differ (same groove-feel needs no announcement). /8 because ~8 units of
/// 6-D dial distance is already "different genre".
export function transitionIntensity (a, b) {
    let sum = 0;
    for (const k of DIAL_ORDER) {
        const d = a.track.dials[k] - b.track.dials[k];
        sum += d * d;
    }
    return Math.min (1, Math.sqrt (sum) / 8);
}

/// Below this, sections just hand off on the fill; at or above it, the riser
/// announces the change and the impact lands the arrival.
export const TRANSITION_FX_MIN = 0.25;

// ------------------------------------------------------------ genre mix ----

/// How much of the song, by bars, is each genre. A section counts wholly
/// toward its PRIMARY genre — blends are a display nicety, the economy deals
/// in primaries. Sorted biggest share first.
export function genreMix (song) {
    const bars = {};
    for (const s of song.sections) {
        const g = classify (s.track.dials).primary.name;
        bars[g] = (bars[g] || 0) + s.bars;
    }
    const total = totalBars (song);
    const out = [];
    for (const name in bars) out.push ({ name, bars: bars[name], share: bars[name] / total });
    out.sort ((a, b) => (b.share - a.share) || (a.name < b.name ? -1 : 1));
    return out;
}

// -------------------------------------------------------------- economy ----
// ⚠️ TUNING PLACEHOLDERS — Sam sets the real numbers at the review gate. The
// SHAPE is the design decision:
//
//   demo price      — unchanged (a demo is one section's loop; existing rule).
//   full track      — worth a multiple of the demo price, growing with section
//                     count and with length.
//   per-alien offer — full value × the share of the song that is THEIR genre.
//                     An all-genre "super track" therefore sells to everyone
//                     but pays each fan only their slice — broad appeal and
//                     top price cannot both be had. That trade-off is the
//                     whole point; do not add a floor that erases it.

export const DEMO_MULT = 1.0;

/// Full-track value as a multiple of the demo price for the same loop.
/// 1 section / 4 bars → 1.5x (a finished track beats a demo even at the same
/// length). Each extra section +0.5x, each bar past four +0.05x.
export function songValueMult (song) {
    const sections = song.sections.length;
    const bars = totalBars (song);
    return 1.5 + 0.5 * (sections - 1) + 0.05 * (bars - 4);
}

/// What a fan of `genreName` offers, as a multiple of the demo price.
/// Zero if their genre isn't in the song at all.
export function offerMult (song, genreName) {
    const mix = genreMix (song);
    for (const m of mix) if (m.name === genreName) return songValueMult (song) * m.share;
    return 0;
}

// --------------------------------------------------------------- schema ----

/// Coerce anything claiming to be a song. `coerceTrackFn` is injected so this
/// file doesn't depend on library.js (library depends on us).
export function coerceSong (raw, coerceTrackFn) {
    if (!raw || typeof raw !== 'object' || !Array.isArray (raw.sections) || raw.sections.length === 0)
        return null;
    const sections = [];
    for (const s of raw.sections) {
        if (!s || typeof s !== 'object') continue;
        sections.push ({ bars: clampBars (s.bars), track: coerceTrackFn (s.track) });
        if (sections.length >= MAX_SECTIONS) break;
    }
    return sections.length > 0 ? { sections } : null;
}
