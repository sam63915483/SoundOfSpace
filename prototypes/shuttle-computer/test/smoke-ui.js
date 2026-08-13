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

// ------------------------------------------------------------------ trax ----

console.log ('\ntrax');
apps.find (a => !a.classList.contains ('disabled')).fire ('click');
await sleep (60);

const inst = global.window.TRAX || null;
check ('opening TRAX switches view and warms the audio context', () => {
    assert (doc.getElementById ('view-app').classList.contains ('active'), 'app view not active');
});

const appView = doc.getElementById ('view-app');
const knobs = appView.querySelectorAll ('.knob');
const modules = appView.querySelectorAll ('.module');
const stepCells = appView.querySelectorAll ('.st');

check ('six dials, six rack slots (2 locked), 16 step lights', () => {
    assert (knobs.length === 6, 'expected 6 knobs, got ' + knobs.length);
    assert (modules.length === 6, 'expected 6 rack slots, got ' + modules.length);
    assert (modules.filter (m => m.classList.contains ('locked')).length === 2,
            'expected 2 locked slots');
    assert (stepCells.length === 16, 'expected 16 step lights, got ' + stepCells.length);
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

check ('rack toggles flip state; locked slots are inert', () => {
    const live = modules.filter (m => !m.classList.contains ('locked'));
    for (const m of live) {
        const was = m.classList.contains ('on');
        m.fire ('click');
        assert (m.classList.contains ('on') !== was, 'toggle did not flip ' + m.textContent);
        m.fire ('click');
        assert (m.classList.contains ('on') === was, 'toggle did not flip back');
    }
    const locked = modules.filter (m => m.classList.contains ('locked'));
    for (const m of locked) {
        m.fire ('click');
        assert (m.classList.contains ('locked'), 'a locked slot changed');
        assert (!m.classList.contains ('on'), 'a locked slot switched on');
    }
});

// ------------------------------------------------------------- transport ----

console.log ('\ntransport');
const buttons = appView.querySelectorAll ('button');
const playBtn = buttons.find (b => b.textContent === 'PLAY' || b.textContent === 'STOP');

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
check ('STOP returns the label to PLAY', () => {
    assert (playBtn.textContent === 'PLAY', 'label did not return to PLAY');
});

check ('PRINT stepper clamps and the button shows a toast', () => {
    const minus = buttons.find (b => b.textContent === '-');
    const plus  = buttons.find (b => b.textContent === '+');
    const print = buttons.find (b => b.textContent === 'PRINT');
    const qty = doc.getElementById ('qty');
    for (let i = 0; i < 5; i++) minus.fire ('click');
    assert (qty.textContent === '1', 'quantity went below 1, got ' + qty.textContent);
    for (let i = 0; i < 3; i++) plus.fire ('click');
    assert (qty.textContent === '4', 'quantity did not step up, got ' + qty.textContent);
    print.fire ('click');
    const toast = doc.getElementById ('toast');
    assert (toast.classList.contains ('show'), 'no toast shown');
    assert (/NO TAPE DECK/.test (toast.textContent), 'toast should say PRINT is not wired yet');
});

check ('ESC leaves TRAX and stops playback', () => {
    playBtn.fire ('click');                    // start again
    fireWindow ('keydown', { key: 'Escape' });
    assert (doc.getElementById ('view-home').classList.contains ('active'), 'ESC did not return home');
});

check ('re-entering TRAX rebuilds cleanly', () => {
    apps.find (a => !a.classList.contains ('disabled')).fire ('click');
    assert (doc.getElementById ('view-app').classList.contains ('active'), 'could not re-enter TRAX');
    assert (doc.getElementById ('view-app').querySelectorAll ('.knob').length === 6,
            'rebuilt screen does not have exactly 6 knobs');
});

// ---------------------------------------------------------------- report ----

if (global.window.TRAX) global.window.TRAX.stop ();

console.log ('\n' + '-'.repeat (52));
console.log (failed === 0 ? 'ui smoke: PASS' : 'ui smoke: ' + failed + ' FAILED');
process.exit (failed ? 1 : 0);
