// The alien OS shell: boot, home screen, app switching.
//
// Everything above TRAX is presentation. MAIL / BANK / RADIO are deliberately
// dead — they're placeholders for later phases (messages, economy, the
// per-planet radio milestone), shown greyed so the shape of the OS is legible.

import { Instrument } from '../audio/instrument.js';
import { mountTrax } from './trax.js';

const BOOT_LINES = [
    ['  SHUTTLE COMPUTER', 'ok'],
    ['  FIRMWARE 4.11.2', 'dim'],
    ['', 'dim'],
    ['  MEM CHECK ................ [ OK ]', 'dim'],
    ['  NAV BUS .................. [ OK ]', 'dim'],
    ['  LIFE SUPPORT LINK ........ [ OK ]', 'dim'],
    ['  AUDIO SYNTH CORE ......... [ OK ]', 'ok'],
    ['  TAPE DECK ................ [ ABSENT ]', 'hot'],
    ['  LICENCE SERVER ........... [ NO ROUTE ]', 'hot'],
    ['', 'dim'],
    ['  3 APPLICATIONS UNLICENSED', 'dim'],
    ['  READY', 'ok']
];

const APPS = [
    { id: 'trax',  name: 'TRAX',  glyph: '♫', enabled: true },
    { id: 'mail',  name: 'MAIL',  glyph: '✉', enabled: false },
    { id: 'bank',  name: 'BANK',  glyph: '▤', enabled: false },
    { id: 'radio', name: 'RADIO', glyph: '◉', enabled: false }
];

const inst = new Instrument ();
let traxHandle = null;

function statusbar (left) {
    const bar = document.createElement ('div');
    bar.className = 'statusbar';
    const l = document.createElement ('div');
    l.innerHTML = left;
    const sep = document.createElement ('div');
    sep.className = 'sep';
    const r = document.createElement ('div');
    r.textContent = 'SYS NOMINAL';
    bar.append (l, sep, r);
    return bar;
}

function show (id) {
    for (const v of document.querySelectorAll ('.view')) v.classList.toggle ('active', v.id === id);
}

// ---------- boot ----------

function runBoot (done) {
    const el = document.getElementById ('boot');
    let i = 0, timer = null, finished = false;

    function finish () {
        if (finished) return;
        finished = true;
        clearTimeout (timer);
        el.innerHTML = BOOT_LINES
            .map (([t, c]) => '<span class="' + c + '">' + t + '</span>')
            .join ('\n');
        window.removeEventListener ('keydown', finish);
        el.removeEventListener ('click', finish);
        setTimeout (done, 320);
    }

    function step () {
        if (i >= BOOT_LINES.length) { finish (); return; }
        const [text, cls] = BOOT_LINES[i++];
        const span = document.createElement ('span');
        span.className = cls;
        span.textContent = text + '\n';
        el.appendChild (span);
        timer = setTimeout (step, text === '' ? 60 : 110);
    }

    // Skippable — nobody wants to sit through this on the twentieth reload.
    window.addEventListener ('keydown', finish);
    el.addEventListener ('click', finish);
    step ();
}

// ---------- home ----------

function buildHome () {
    const view = document.getElementById ('view-home');
    view.innerHTML = '';
    view.appendChild (statusbar ('<b>HOME</b>'));

    const body = document.createElement ('div');
    body.id = 'home-body';

    const title = document.createElement ('div');
    title.id = 'home-title';
    title.textContent = 'APPLICATIONS';

    const grid = document.createElement ('div');
    grid.id = 'apps';

    for (const app of APPS) {
        const el = document.createElement ('div');
        el.className = 'app' + (app.enabled ? '' : ' disabled');
        const g = document.createElement ('div'); g.className = 'glyph'; g.textContent = app.glyph;
        const n = document.createElement ('div'); n.className = 'name';  n.textContent = app.name;
        el.append (g, n);
        if (app.enabled) el.addEventListener ('click', () => openTrax ());
        grid.appendChild (el);
    }

    body.append (title, grid);
    view.appendChild (body);
}

// ---------- app ----------

async function openTrax () {
    const view = document.getElementById ('view-app');
    view.innerHTML = '';
    view.appendChild (statusbar ('<b>TRAX</b> • SYNTH CORE'));

    const host = document.createElement ('div');
    host.style.cssText = 'flex:1; display:flex; flex-direction:column; min-height:0; position:relative;';
    view.appendChild (host);

    if (traxHandle) traxHandle.dispose ();
    traxHandle = mountTrax (host, inst, closeTrax);
    show ('view-app');

    // This click IS the user gesture, so warm the AudioContext now — otherwise
    // the first PLAY eats the resume latency and feels broken.
    try { await inst.init (); }
    catch (e) { console.warn ('audio init failed', e); }
}

function closeTrax () {
    if (traxHandle) traxHandle.stop ();
    show ('view-home');
}

// ---------- go ----------

buildHome ();
runBoot (() => show ('view-home'));

// Handy while iterating on sound.
window.TRAX = inst;
