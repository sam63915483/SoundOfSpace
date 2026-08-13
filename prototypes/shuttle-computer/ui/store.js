// Where the shelf physically lives, in the browser.
//
// This is the ONLY file that knows about localStorage. Step 2 replaces it with
// the Unity world save and everything above it is untouched — the screens talk
// to load()/save() and the rules live in engine/library.js.
//
// Storage is optional on purpose: the headless UI smoke test has no
// localStorage, and a browser can refuse it (private mode, blocked storage).
// Both cases fall back to an in-memory shelf so the app still runs; you just
// lose the shelf when the tab closes.

import { serialize, deserialize } from '../engine/library.js';

const KEY = 'trax.projects.v1';

let memory = [];
let backend = null;

function detect () {
    if (backend !== null) return backend;
    try {
        const ls = (typeof localStorage !== 'undefined') ? localStorage
                 : (typeof globalThis !== 'undefined' ? globalThis.localStorage : null);
        if (!ls) { backend = false; return backend; }
        // Availability is not the same as writability — probe it.
        const probe = KEY + '.probe';
        ls.setItem (probe, '1');
        ls.removeItem (probe);
        backend = ls;
    } catch (e) {
        backend = false;
    }
    return backend;
}

export function isPersistent () {
    return detect () !== false;
}

export function load () {
    const ls = detect ();
    if (ls === false) return memory.slice ();
    const text = ls.getItem (KEY);
    if (!text) return [];
    return deserialize (text, Date.now ());
}

export function save (list) {
    const ls = detect ();
    memory = list.slice ();
    if (ls === false) return false;
    try {
        ls.setItem (KEY, serialize (list));
        return true;
    } catch (e) {
        // Quota or a locked store — keep running on the in-memory copy.
        console.warn ('project shelf could not be written', e);
        return false;
    }
}

/// Monotonic within a session, so two saves in the same millisecond can't
/// collide on an id.
let seq = 0;
export function nextSeq () { return seq++; }
