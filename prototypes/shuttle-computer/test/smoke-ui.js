// Headless smoke test for the UI.  node test/smoke-ui.js
//
// Boots the OS shell against a mock DOM + mock Web Audio, opens TRAX, and
// drives every control. Proves the screen builds and responds; it says nothing
// about how it looks.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { createDocument, parseHTML, installRaf } from './mock-dom.js';
import { FakeAudioContext } from './mock-webaudio.js';

const HERE = dirname (fileURLToPath (import.meta.url));
const ROOT = join (HERE, '..');

let failed = 0;
function check (name, fn) {
    try { fn (); console.log ('  ok   ' + name); }
    catch (e) { failed++; console.log ('  FAIL ' + name + '\n       ' + e.message); }
}
function assert (c, m) { if (!c) throw new Error (m || 'assertion failed'); }

const sleep = (ms) => new Promise (r => setTimeout (r, ms));

// ---------------------------------------------------------------- setup ----

const doc = createDocument ();
parseHTML (readFileSync (join (ROOT, 'index.html'), 'utf8'), doc);

global.document = doc;
const listeners = {};
global.window = {
    AudioContext: FakeAudioContext,
    addEventListener (t, fn) { (listeners[t] = listeners[t] || []).push (fn); },
    removeEventListener (t, fn) {
        const l = listeners[t]; if (!l) return;
        const i = l.indexOf (fn); if (i >= 0) l.splice (i, 1);
    }
};
function fireWindow (type, props) {
    for (const fn of (listeners[type] || []).slice ())
        fn (Object.assign ({ type, preventDefault () {}, target: null }, props || {}));
}
const pump = installRaf (global);

console.log ('markup');
check ('index.html provides every element the JS looks up', () => {
    for (const id of ['boot', 'view-boot', 'view-home', 'view-app'])
        assert (doc.getElementById (id), 'index.html has no #' + id);
    assert (doc.querySelectorAll ('.view').length === 3, 'expected 3 .view elements');
});

// ------------------------------------------------------------------ boot ----

console.log ('\nboot + home');
await import ('../ui/os.js');

check ('boot screen is the active view on load', () => {
    assert (doc.getElementById ('view-boot').classList.contains ('active'), 'boot view not active');
});

// Skip the boot animation the way a player would.
fireWindow ('keydown', { key: 'Enter' });
await sleep (450);

check ('boot completes and hands over to the home screen', () => {
    assert (doc.getElementById ('view-home').classList.contains ('active'), 'home view not active');
    assert (!doc.getElementById ('view-boot').classList.contains ('active'), 'boot view still active');
});

const apps = doc.querySelectorAll ('.app');
check ('home shows 4 apps, 3 of them unlicensed', () => {
    assert (apps.length === 4, 'expected 4 apps, got ' + apps.length);
    const disabled = apps.filter (a => a.classList.contains ('disabled'));
    assert (disabled.length === 3, 'expected 3 disabled apps, got ' + disabled.length);
});

check ('the disabled apps do nothing when clicked', () => {
    for (const a of apps.filter (x => x.classList.contains ('disabled'))) {
        a.fire ('click');
        assert (doc.getElementById ('view-home').classList.contains ('active'),
                'a locked app navigated somewhere');
    }
});

// -------------------------------------------------------------- projects ----
//
// The mock's getElementById registry is permanent — a detached element with an
// id is still returned after a re-render. So everything below queries live
// elements out of #view-app by class and picks them by their text.

const appView = doc.getElementById ('view-app');
const byText = (root, cls, text) => root.querySelectorAll (cls).find (e => e.textContent.indexOf (text) >= 0);
const projBtn = (text) => byText (appView, '.proj-btn', text);
const btn     = (text) => appView.querySelectorAll ('button').find (b => b.textContent === text);

console.log ('\ntrax project menu');
apps.find (a => !a.classList.contains ('disabled')).fire ('click');
await sleep (60);

check ('opening TRAX lands on the project menu, not the instrument', () => {
    assert (appView.classList.contains ('active'), 'app view not active');
    assert (projBtn ('NEW PROJECT'), 'no NEW PROJECT button');
    assert (projBtn ('LOAD PROJECT'), 'no LOAD PROJECT button');
    assert (appView.querySelectorAll ('.knob').length === 0,
            'the instrument mounted before a project was chosen');
});

check ('LOAD PROJECT is dead while the shelf is empty', () => {
    const load = projBtn ('LOAD PROJECT');
    assert (load.classList.contains ('disabled'), 'LOAD should be disabled with nothing saved');
    load.fire ('click');
    assert (!appView.querySelector ('.proj-pane').parentNode.classList.contains ('showing-list'),
            'LOAD opened the shelf with nothing on it');
});

// ------------------------------------------------------------------ trax ----

console.log ('\ntrax instrument');
projBtn ('NEW PROJECT').fire ('click');
await sleep (60);

const inst = global.window.TRAX || null;
check ('NEW PROJECT opens the instrument on a blank track', () => {
    assert (appView.querySelectorAll ('.knob').length === 6, 'instrument did not mount');
    const state = appView.querySelectorAll ('.pb-label').length;
    assert (state > 0, 'no project bar on the instrument screen');
});

const knobs = appView.querySelectorAll ('.knob');
const modules = appView.querySelectorAll ('.module');
const heads = appView.querySelectorAll ('.m-head');
const steppers = appView.querySelectorAll ('.stepper');
const stepCells = appView.querySelectorAll ('.st');

check ('six dials, six live rack slots, 16 step lights', () => {
    assert (knobs.length === 6, 'expected 6 knobs, got ' + knobs.length);
    assert (modules.length === 6, 'expected 6 rack slots, got ' + modules.length);
    // MOSS and SPINDLE filled the two previously-locked slots, so every module
    // is now playable. A future plugin shop would add slots, not unlock these.
    assert (modules.filter (m => m.classList.contains ('locked')).length === 0,
            'no rack slot should still be locked');
    assert (stepCells.length === 16, 'expected 16 step lights, got ' + stepCells.length);
});

check ('the rack names the full band', () => {
    const names = modules.map (m => m.textContent).join (' ');
    for (const n of ['THUMPER', 'GLOWORM', 'MOSS', 'SIREN', 'SPINDLE', 'CAVE'])
        assert (names.indexOf (n) >= 0, 'rack is missing ' + n);
});

check ('the genre readout is populated', () => {
    const label = doc.getElementById ('genre-label');
    assert (label && label.textContent.length > 0, 'genre label empty');
});

check ('dragging a knob changes its dial and re-classifies', () => {
    const label = doc.getElementById ('genre-label');
    const seen = new Set ();
    const k = knobs[0];                       // PULSE
    for (let i = 0; i < 12; i++) {
        k.fire ('pointerdown', { clientY: 300 });
        k.fire ('pointermove', { clientY: 300 - 40 });   // drag up
        k.fire ('pointerup', {});
        seen.add (label.textContent);
    }
    assert (seen.size > 1, 'dragging PULSE across its range never changed the genre label');
});

check ('knob responds to wheel and keyboard', () => {
    const k = knobs[2];                       // GOO
    const before = k.getAttribute ('aria-valuenow');
    k.fire ('wheel', { deltaY: -1 });
    const afterWheel = k.getAttribute ('aria-valuenow');
    assert (afterWheel !== before, 'wheel did nothing');
    k.fire ('keydown', { key: 'Home' });
    assert (k.getAttribute ('aria-valuenow') === '0.0', 'Home key did not zero the knob');
    k.fire ('keydown', { key: 'End' });
    assert (k.getAttribute ('aria-valuenow') === '10.0', 'End key did not max the knob');
});

check ('knob clamps at both ends', () => {
    const k = knobs[1];
    for (let i = 0; i < 40; i++) { k.fire ('pointerdown', { clientY: 500 }); k.fire ('pointermove', { clientY: 200 }); k.fire ('pointerup', {}); }
    assert (parseFloat (k.getAttribute ('aria-valuenow')) === 10, 'did not clamp at 10');
    for (let i = 0; i < 40; i++) { k.fire ('pointerdown', { clientY: 200 }); k.fire ('pointermove', { clientY: 500 }); k.fire ('pointerup', {}); }
    assert (parseFloat (k.getAttribute ('aria-valuenow')) === 0, 'did not clamp at 0');
});

check ('every rack toggle flips and flips back', () => {
    for (let i = 0; i < heads.length; i++) {
        const m = modules[i];
        const was = m.classList.contains ('on');
        heads[i].fire ('click');
        assert (m.classList.contains ('on') !== was, 'toggle did not flip ' + m.textContent);
        heads[i].fire ('click');
        assert (m.classList.contains ('on') === was, 'toggle did not flip back');
    }
});

check ('every module has a PRESET and a VARIATION stepper', () => {
    // 6 modules x 2 steppers, plus the key stepper in the transport.
    assert (steppers.length >= 13,
            'expected at least 13 steppers, got ' + steppers.length);
});

check ('stepping a preset changes its label and cycles back around', () => {
    const preset = appView.querySelectorAll ('.m-preset')[0];
    const label = preset.querySelectorAll ('.st-label')[0];
    const fwd = preset.querySelectorAll ('button')[1];
    const seen = new Set ([label.textContent]);
    for (let i = 0; i < 5; i++) { fwd.fire ('click'); seen.add (label.textContent); }
    assert (seen.size === 5, 'expected 5 distinct preset names, saw ' + seen.size +
            ': ' + [...seen].join (','));
});

check ('stepping a variation changes its number and wraps at 8', () => {
    const varn = appView.querySelectorAll ('.m-var')[0];
    const label = varn.querySelectorAll ('.st-label')[0];
    const fwd = varn.querySelectorAll ('button')[1];
    const first = label.textContent;
    fwd.fire ('click');
    assert (label.textContent !== first, 'variation did not change');
    for (let i = 0; i < 7; i++) fwd.fire ('click');
    assert (label.textContent === first, 'variation did not wrap back after 8');
});

check ('a preset stepper click does not mute its module', () => {
    const wasOn = modules[0].classList.contains ('on');
    appView.querySelectorAll ('.m-preset')[0].querySelectorAll ('button')[1].fire ('click');
    assert (modules[0].classList.contains ('on') === wasOn,
            'choosing a preset toggled the module off');
});

check ('the key stepper walks all twelve keys and returns', () => {
    const key = appView.querySelectorAll ('.key-step')[0];
    assert (key, 'no key stepper found');
    const label = key.querySelectorAll ('.st-label')[0];
    const fwd = key.querySelectorAll ('button')[1];
    const first = label.textContent;
    const seen = new Set ([first]);
    for (let i = 0; i < 12; i++) { fwd.fire ('click'); seen.add (label.textContent); }
    assert (seen.size === 12, 'expected 12 key names, saw ' + seen.size);
    assert (label.textContent === first, 'key did not wrap back to ' + first);
});

// ------------------------------------------------------------- transport ----

console.log ('\ntransport');
const buttons = appView.querySelectorAll ('button');
const playBtn = buttons.find (b => b.textContent === 'PLAY TRACK' || b.textContent === 'STOP');

check ('PLAY starts the transport and the label becomes STOP', async () => {
    assert (playBtn, 'no play button found');
});
playBtn.fire ('click');
await sleep (60);

check ('transport is running', () => {
    assert (playBtn.textContent === 'STOP', 'label did not change to STOP, got ' + playBtn.textContent);
});

check ('frames pump without throwing while playing', () => {
    pump (30);
});

check ('dials still respond while playing (swap is queued, not applied mid-bar)', () => {
    const k = knobs[4];                       // JITTER
    k.fire ('pointerdown', { clientY: 300 });
    k.fire ('pointermove', { clientY: 200 });
    k.fire ('pointerup', {});
    pump (5);
});

playBtn.fire ('click');
await sleep (30);
check ('STOP returns the label to PLAY TRACK', () => {
    assert (playBtn.textContent === 'PLAY TRACK', 'label did not return to PLAY TRACK');
});

check ('PRINT TAPE opens a modal, and CANCEL closes it without printing', () => {
    const scrim = doc.getElementById ('print-scrim');
    assert (scrim, 'no print dialog was built');
    assert (!scrim.classList.contains ('show'), 'the dialog starts open');

    buttons.find (b => b.textContent === 'PRINT TAPE').fire ('click');
    assert (scrim.classList.contains ('show'), 'PRINT TAPE did not open the dialog');

    doc.getElementById ('toast').classList.remove ('show');
    // Scoped to this dialog on purpose — the save dialog has a CANCEL too.
    scrim.querySelectorAll ('button').find (b => b.textContent === 'CANCEL').fire ('click');
    assert (!scrim.classList.contains ('show'), 'CANCEL did not close the dialog');
    assert (!doc.getElementById ('toast').classList.contains ('show'),
            'CANCEL should not print anything');
});

check ('the copies stepper clamps between 1 and 99', () => {
    buttons.find (b => b.textContent === 'PRINT TAPE').fire ('click');
    const qty = appView.querySelectorAll ('.print-qty')[0];
    const label = qty.querySelectorAll ('.st-label')[0];
    const back = qty.querySelectorAll ('button')[0];
    const fwd = qty.querySelectorAll ('button')[1];

    for (let i = 0; i < 5; i++) back.fire ('click');
    assert (label.textContent === '1', 'went below 1, got ' + label.textContent);
    for (let i = 0; i < 3; i++) fwd.fire ('click');
    assert (label.textContent === '4', 'did not step up, got ' + label.textContent);
    for (let i = 0; i < 120; i++) fwd.fire ('click');
    assert (label.textContent === '99', 'went above 99, got ' + label.textContent);
});

check ('confirming closes the dialog and says the deck is missing', () => {
    buttons.find (b => b.textContent === 'PRINT').fire ('click');
    const scrim = doc.getElementById ('print-scrim');
    assert (!scrim.classList.contains ('show'), 'PRINT did not close the dialog');
    const toast = doc.getElementById ('toast');
    assert (toast.classList.contains ('show'), 'no toast shown');
    assert (/NO TAPE DECK/.test (toast.textContent), 'toast should say PRINT is not wired yet');
});

// ------------------------------------------------------------ save + load ----

console.log ('\nsave + load');

const nameField = () => appView.querySelectorAll ('input').find (i => i.id === 'save-name');
const projState = () => appView.querySelectorAll ('.pb-label')[0].parentNode.childNodes[2].textContent;

check ('a fresh project reports that it has never been saved', () => {
    assert (/NEVER SAVED/.test (projState ()), 'expected NEVER SAVED, got ' + projState ());
});

check ('muting a module is a track edit, not a UI preference', () => {
    // MOSS is index 2 — THUMPER, GLOWORM, MOSS, SIREN, SPINDLE, CAVE.
    const before = inst.trackId;
    heads[2].fire ('click');
    assert (!inst.enabled.MOSS, 'MOSS did not mute');
    assert (inst.trackId !== before, 'muting did not change the track identity');
    assert (/UNSAVED|NEVER/.test (projState ()), 'muting did not mark the project dirty');
});

check ('SAVE PROJECT opens the name dialog, CANCEL closes it without saving', () => {
    btn ('SAVE PROJECT').fire ('click');
    assert (nameField (), 'no name field in the save dialog');
    btn ('CANCEL').fire ('click');
    assert (/NEVER SAVED/.test (projState ()), 'CANCEL saved the project anyway');
});

check ('an empty name is refused', () => {
    btn ('SAVE PROJECT').fire ('click');
    const f = nameField ();
    f.value = '   ';
    f.fire ('input');
    btn ('SAVE').fire ('click');
    assert (/NEVER SAVED/.test (projState ()), 'a blank name was accepted');
});

let savedTrackId = 0;
check ('naming it and saving puts it on the shelf', () => {
    const f = nameField ();
    f.value = 'DEEP CAVE';
    f.fire ('input');
    btn ('SAVE').fire ('click');
    savedTrackId = inst.trackId;
    assert (/SAVED/.test (projState ()) && !/UNSAVED|NEVER/.test (projState ()),
            'project bar did not go clean after saving, got ' + projState ());
    const nm = appView.querySelectorAll ('.pb-label')[0].parentNode.childNodes[1].textContent;
    assert (nm === 'DEEP CAVE', 'project bar shows ' + nm);
});

check ('editing after a save marks the project dirty', () => {
    const k = knobs[3];                        // VOID
    k.fire ('pointerdown', { clientY: 400 });
    k.fire ('pointermove', { clientY: 250 });
    k.fire ('pointerup', {});
    assert (inst.trackId !== savedTrackId, 'the edit did not change the track identity');
    assert (/UNSAVED CHANGES/.test (projState ()), 'expected UNSAVED CHANGES, got ' + projState ());
});

playBtn.fire ('click');                        // start again — leaving must stop it
await sleep (40);
check ('ESC steps back to the project menu, not out of the app', () => {
    fireWindow ('keydown', { key: 'Escape' });
    assert (appView.classList.contains ('active'), 'ESC left the app entirely');
    assert (projBtn ('NEW PROJECT'), 'ESC did not land on the project menu');
    assert (!inst.playing, 'leaving the instrument did not stop playback');
});

check ('a second ESC leaves TRAX for the desktop', () => {
    fireWindow ('keydown', { key: 'Escape' });
    assert (doc.getElementById ('view-home').classList.contains ('active'), 'ESC did not return home');
});

check ('re-entering TRAX rebuilds the menu, and LOAD is now live', () => {
    apps.find (a => !a.classList.contains ('disabled')).fire ('click');
    assert (appView.classList.contains ('active'), 'could not re-enter TRAX');
    const load = projBtn ('LOAD PROJECT');
    assert (load, 'no LOAD PROJECT button');
    assert (!load.classList.contains ('disabled'), 'LOAD still disabled after saving one project');
    assert (/1 project/.test (load.textContent), 'LOAD does not report the shelf count: ' + load.textContent);
});

check ('the shelf lists the saved project', () => {
    projBtn ('LOAD PROJECT').fire ('click');
    const rows = appView.querySelectorAll ('.proj-row');
    assert (rows.length === 1, 'expected 1 row on the shelf, got ' + rows.length);
    assert (rows[0].textContent.indexOf ('DEEP CAVE') >= 0, 'row does not name the project');
});

check ('opening it restores the track that was SAVED, not the one left on screen', () => {
    appView.querySelectorAll ('.proj-row')[0].fire ('click');
    assert (appView.querySelectorAll ('.knob').length === 6, 'the instrument did not mount');
    assert (inst.trackId === savedTrackId,
            'loaded track ' + inst.trackId + ' but saved ' + savedTrackId);
    assert (/SAVED/.test (projState ()) && !/UNSAVED|NEVER/.test (projState ()),
            'a freshly loaded project should be clean, got ' + projState ());
    // The mute survived the round trip, and the rack shows it.
    assert (!inst.enabled.MOSS, 'MOSS came back unmuted');
    const moss = appView.querySelectorAll ('.module')[2];
    assert (!moss.classList.contains ('on'), 'the rack shows MOSS as playing');
});

check ('saving over the same name does not make a second project', () => {
    btn ('SAVE PROJECT').fire ('click');
    const f = nameField ();
    f.value = 'DEEP CAVE';
    f.fire ('input');
    btn ('SAVE').fire ('click');
    fireWindow ('keydown', { key: 'Escape' });          // back to the menu
    projBtn ('LOAD PROJECT').fire ('click');
    const rows = appView.querySelectorAll ('.proj-row');
    assert (rows.length === 1, 'overwriting made a duplicate: ' + rows.length + ' rows');
});

check ('DELETE takes two presses and then empties the shelf', () => {
    const del = appView.querySelectorAll ('.pr-del')[0];
    assert (del.textContent === 'DELETE', 'delete button starts armed');
    del.fire ('click');
    assert (del.textContent === 'SURE?', 'first press did not arm the confirm');
    del.fire ('click');
    assert (appView.querySelectorAll ('.proj-row').length === 0, 'the project was not deleted');
});

// ------------------------------------------------------------- arranger ----

console.log ('\narranger');

projBtn ('NEW PROJECT').fire ('click');
const secs = () => appView.querySelectorAll ('.arr-sec');

check ('a new project starts as one 4-bar selected section', () => {
    assert (secs ().length === 1, 'expected 1 section, got ' + secs ().length);
    assert (secs ()[0].classList.contains ('sel'), 'the only section is not selected');
    assert (/1 SEC · 4 BARS/.test (doc.getElementById ('arr-stats').textContent),
            'stats read ' + doc.getElementById ('arr-stats').textContent);
});

check ('+ adds a copy of the selected section and selects it', () => {
    doc.getElementById ('arr-add').fire ('click');
    assert (secs ().length === 2, 'expected 2 sections, got ' + secs ().length);
    assert (secs ()[1].classList.contains ('sel'), 'the new section is not selected');
});

check ('the bars stepper stretches only the selected section', () => {
    appView.querySelectorAll ('.arr-bars')[0].querySelectorAll ('button')[1].fire ('click');
    // The control row is rebuilt on every arrangement change — re-query.
    const bars = appView.querySelectorAll ('.arr-bars')[0];
    assert (bars.querySelectorAll ('.st-label')[0].textContent === '5 BARS',
            'bars did not step to 5, reads ' + bars.querySelectorAll ('.st-label')[0].textContent);
    assert (/2 SEC · 9 BARS/.test (doc.getElementById ('arr-stats').textContent),
            'stats read ' + doc.getElementById ('arr-stats').textContent);
});

check ('editing section B never reaches into section A', () => {
    const before = inst.dials.pulse;
    const k = appView.querySelectorAll ('.knob')[0];       // PULSE
    k.fire ('pointerdown', { clientY: 300 });
    k.fire ('pointermove', { clientY: 220 });
    k.fire ('pointerup', {});
    assert (inst.dials.pulse !== before, 'the drag did not move PULSE');
    secs ()[0].fire ('click');                             // back to section A
    assert (inst.dials.pulse === before,
            'section A now has B\'s PULSE: ' + inst.dials.pulse + ' vs ' + before);
});

check ('selecting a section snaps the knobs to its dials', () => {
    secs ()[1].fire ('click');
    const val = appView.querySelectorAll ('.knob')[0].querySelectorAll ('.k-val')[0];
    assert (val.textContent === inst.dials.pulse.toFixed (1),
            'knob shows ' + val.textContent + ' but the dial is ' + inst.dials.pulse);
});

check ('the song value grows with the arrangement', () => {
    const v = doc.getElementById ('arr-value').textContent;
    // 2 sections, 9 bars -> 1.25 + 0.25 + 0.02*5 = ×1.60 (2026-08-18 retune)
    assert (/×1\.60/.test (v), 'value reads ' + v);
});

check ('clicking a section only selects it — playback is untouched', () => {
    assert (!inst.playing, 'nothing should be playing before the click');
    secs ()[0].fire ('click');
    assert (!inst.playing, 'selecting a section must not start playback');
    assert (secs ()[0].classList.contains ('sel'), 'the click did not select');
    secs ()[1].fire ('click');                     // back to B for the rest
    assert (secs ()[1].classList.contains ('sel'), 'reselecting B failed');
});

check ('the ruler has a clickable cell per beat, and seeks land on the beat', () => {
    const beats = appView.querySelectorAll ('.arr-beat');
    assert (beats.length === 36, 'expected 36 beat cells (9 bars x 4), got ' + beats.length);
    beats[8].fire ('click');                       // bar 3, beat 1 -> step 32
    assert (inst.songCursor === 32, 'bar seek should be 32, got ' + inst.songCursor);
    beats[9].fire ('click');                       // bar 3, beat 2 -> step 36
    assert (inst.songCursor === 36, 'beat seek should be 36, got ' + inst.songCursor);
});

check ('dragging a block reorders the sections and swallows the click', () => {
    // A(4), B(5). Grab A and drag right; the mock has no layout, so every
    // rect is a zero-point and any positive x maps to "after the last block"
    // -> the order becomes B, A.
    const a = secs ()[0];
    a.fire ('pointerdown', { button: 0, clientX: 100 });
    a.fire ('pointermove', { clientX: 300 });
    a.fire ('pointerup', {});
    const tops = secs ().map (b => b.querySelectorAll ('.as-top')[0].textContent);
    assert (/5/.test (tops[0]) && /4/.test (tops[1]),
            'expected B(5) then A(4), got ' + tops.join (' | '));
    assert (!inst.playing, 'the click after a drag must be swallowed, not start playback');
    assert (secs ()[1].classList.contains ('sel'), 'selection should follow the moved section');
});

check ('DELETE names its section and takes two presses', () => {
    const del = () => appView.querySelectorAll ('button')
        .find (b => /^(DELETE SEC|SURE)/.test (b.textContent));
    assert (del ().textContent === 'DELETE SEC B',
            'delete should name section B, reads ' + del ().textContent);
    del ().fire ('click');
    assert (del ().textContent === 'SURE?', 'first press did not arm the confirm');
    assert (secs ().length === 2, 'arming must not delete anything yet');
    del ().fire ('click');
    assert (secs ().length === 1, 'second press did not remove the section');
    assert (del ().textContent === 'DELETE SEC A', 'button should now name section A');
    assert (del ().disabled, 'delete is not disabled on the last section');
});

check ('the reorder arrows are gone from the section controls', () => {
    const ctl = doc.getElementById ('arr-ctl');
    const arrowBtns = ctl.querySelectorAll ('button')
        .filter (b => b.textContent === '◀' || b.textContent === '▶');
    // Only the LENGTH stepper's arrows remain (inside .stepper), no bare
    // move buttons.
    for (const b of arrowBtns)
        assert (b.className.indexOf ('st-arrow') >= 0,
                'found a bare arrow button outside the LENGTH stepper');
});

// ---------------------------------------------------------------- report ----

if (global.window.TRAX) global.window.TRAX.stop ();

console.log ('\n' + '-'.repeat (52));
console.log (failed === 0 ? 'ui smoke: PASS' : 'ui smoke: ' + failed + ' FAILED');
process.exit (failed ? 1 : 0);
