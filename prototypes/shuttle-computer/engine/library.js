// The project library — what a SAVED PROJECT is, and the rules for the shelf.
//
// Pure logic, no DOM and no storage backend: this file ports 1:1 to C# the same
// way the rest of engine/ does. The browser keeps records in localStorage
// (ui/store.js); Unity will keep them in the world save. Neither backend knows
// anything about the rules below, which is the point.
//
// A record is a NAME plus a whole TRACK. Not a pointer to one — a copy. Two
// projects can hold identical tracks, and editing one must never reach into the
// other, so every record owns its own cloned track struct.

import { cloneTrack, trackId, defaultTrack } from './track.js';
import { DEFAULT_DIALS, DIAL_DEFS } from './params.js';
import { MODULE_NAMES, PRESET_COUNT, VARIATION_COUNT } from './presets.js';

export const NAME_MAX = 24;

/// Names are trimmed, collapsed and capped, but NOT uppercased — the screen
/// renders them uppercase, and throwing away what Sam typed would make the
/// eventual "rename" feel lossy.
export function normalizeName (raw) {
    return String (raw == null ? '' : raw)
        .replace (/[\r\n\t]+/g, ' ')
        .replace (/\s+/g, ' ')
        .trim ()
        .slice (0, NAME_MAX);
}

export function isValidName (raw) {
    return normalizeName (raw).length > 0;
}

/// Case- and space-insensitive, so "Deep Cave" and "deep  cave" are the same
/// project and SAVE overwrites instead of quietly making a twin.
export function nameKey (raw) {
    return normalizeName (raw).toLowerCase ();
}

/// Ids are derived, never random — same discipline as the rest of the engine,
/// and it keeps this file testable without a clock or an RNG.
export function makeId (now, seq) {
    return 'p' + Number (now).toString (36) + '-' + Number (seq).toString (36);
}

export function makeRecord (name, track, now, seq) {
    const t = cloneTrack (track);
    return {
        id: makeId (now, seq),
        name: normalizeName (name),
        track: t,
        trackId: trackId (t),
        savedAt: now
    };
}

export function findByName (list, name) {
    const k = nameKey (name);
    for (const r of list) if (nameKey (r.name) === k) return r;
    return null;
}

export function findById (list, id) {
    for (const r of list) if (r.id === id) return r;
    return null;
}

/// SAVE semantics: same name overwrites in place (keeping its id and its slot
/// in the list), a new name appends. The id is stable across overwrites so a
/// cassette printed from a project can keep pointing at it later.
export function upsert (list, record) {
    const out = list.slice ();
    const existing = findByName (out, record.name);
    if (existing) {
        const merged = Object.assign ({}, record, { id: existing.id });
        out[out.indexOf (existing)] = merged;
        return { list: out, record: merged, overwrote: true };
    }
    out.push (record);
    return { list: out, record, overwrote: false };
}

export function remove (list, id) {
    return list.filter (r => r.id !== id);
}

/// Most recently saved first — the shelf is a work queue, not an archive.
export function sortRecent (list) {
    return list.slice ().sort ((a, b) => (b.savedAt - a.savedAt) || a.name.localeCompare (b.name));
}

// ---------------------------------------------------------------- schema ----

/// Coerce anything claiming to be a track into a valid one. A save file that
/// predates a module, or that got hand-edited, must load with sane defaults
/// rather than crash the app or (worse) generate silently wrong patterns.
export function coerceTrack (raw) {
    const t = defaultTrack ();
    if (!raw || typeof raw !== 'object') return t;

    if (raw.dials && typeof raw.dials === 'object') {
        for (const def of DIAL_DEFS) {
            const v = Number (raw.dials[def.key]);
            if (Number.isFinite (v)) t.dials[def.key] = Math.min (10, Math.max (0, v));
            else t.dials[def.key] = DEFAULT_DIALS[def.key];
        }
    }

    const k = Number (raw.key);
    t.key = Number.isFinite (k) ? ((Math.round (k) % 12) + 12) % 12 : 0;

    for (const m of MODULE_NAMES) {
        const p = Number (raw.preset && raw.preset[m]);
        const v = Number (raw.variation && raw.variation[m]);
        if (Number.isFinite (p)) t.preset[m] = ((Math.round (p) % PRESET_COUNT) + PRESET_COUNT) % PRESET_COUNT;
        if (Number.isFinite (v)) t.variation[m] = ((Math.round (v) % VARIATION_COUNT) + VARIATION_COUNT) % VARIATION_COUNT;
        // A record saved before the active set existed has no `active` block.
        // Defaulting a MISSING module to ON is the only safe answer: it is how
        // the track sounded when it was saved.
        if (raw.active && typeof raw.active === 'object' && m in raw.active)
            t.active[m] = !!raw.active[m];
    }
    return t;
}

function coerceRecord (raw, fallbackNow, seq) {
    if (!raw || typeof raw !== 'object') return null;
    const name = normalizeName (raw.name);
    if (!name) return null;
    const track = coerceTrack (raw.track);
    const savedAt = Number.isFinite (Number (raw.savedAt)) ? Number (raw.savedAt) : fallbackNow;
    const id = typeof raw.id === 'string' && raw.id ? raw.id : makeId (savedAt, seq);
    return { id, name, track, trackId: trackId (track), savedAt };
}

export function serialize (list) {
    return JSON.stringify ({ v: 1, projects: list });
}

/// Never throws. A corrupt shelf loses records, it does not lose the app.
export function deserialize (text, fallbackNow) {
    let blob = null;
    try { blob = JSON.parse (text); } catch (e) { return []; }
    const raw = blob && Array.isArray (blob.projects) ? blob.projects
              : Array.isArray (blob) ? blob : [];
    const out = [];
    const seen = new Set ();
    for (let i = 0; i < raw.length; i++) {
        const r = coerceRecord (raw[i], fallbackNow, i);
        if (!r) continue;
        if (seen.has (r.id)) r.id = makeId (r.savedAt, 1000 + i);
        seen.add (r.id);
        out.push (r);
    }
    return out;
}
