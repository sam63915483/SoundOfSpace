// The alien OS shell: boot, home screen, app switching.
//
// Everything above TRAX is presentation. MAIL / BANK / RADIO are deliberately
// dead — they're placeholders for later phases (messages, economy, the
// per-planet radio milestone), shown greyed so the shape of the OS is legible.

import { Instrument } from '../audio/instrument.js';
import { mountTrax } from './trax.js';
import { mountProjects } from './projects.js';
import { defaultSong, songFromTrack, cloneSong } from '../engine/song.js';
import { makeRecord, upsert, remove } from '../engine/library.js';
import * as store from './store.js';

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
let projectsHandle = null;

// The shelf, held in memory for the session and written through to storage on
// every change. In Unity this is the world save, shared by both players.
let projects = store.load ();

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
//
// TRAX is a two-screen app: the project menu, then the instrument. Both mount
// into the same host inside #view-app, so the OS still only knows about three
// views and the app owns its own navigation.

let setCrumb = () => {};

function appHost (crumb) {
    const view = document.getElementById ('view-app');
    view.innerHTML = '';
    const bar = statusbar ('<b>TRAX</b> • ' + crumb);
    view.appendChild (bar);
    // Saving renames the screen you are on, so the breadcrumb has to be able to
    // change after mount — otherwise it sits on UNTITLED forever.
    setCrumb = (text) => { bar.firstChild.innerHTML = '<b>TRAX</b> • ' + text; };
    const host = document.createElement ('div');
    host.style.cssText = 'flex:1; display:flex; flex-direction:column; min-height:0; position:relative;';
    view.appendChild (host);
    return host;
}

function teardown () {
    if (traxHandle) { traxHandle.stop (); traxHandle.dispose (); traxHandle = null; }
    if (projectsHandle) { projectsHandle.dispose (); projectsHandle = null; }
}

/// The project menu — where TRAX now opens.
function openTrax (startOnList) {
    teardown ();
    const host = appHost ('PROJECTS');
    projectsHandle = mountProjects (host, {
        records: projects,
        persistent: store.isPersistent (),
        startOnList: !!startOnList,
        onNew:    () => openInstrument (null),
        onOpen:   rec => openInstrument (rec),
        onDelete: rec => {
            projects = remove (projects, rec.id);
            store.save (projects);
            openTrax (projects.length > 0);        // rebuild the shelf in place
        },
        onHome:   () => { teardown (); show ('view-home'); }
    });
    show ('view-app');
}

/// The instrument. A null record means an unsaved new project, which starts
/// from a blank one-section song rather than whatever was last loaded.
async function openInstrument (rec) {
    teardown ();
    // The working song is OWNED by the TRAX screen; this copy is what its
    // arranger mutates. Old records without a song block become one section.
    const song = rec ? (rec.song ? cloneSong (rec.song) : songFromTrack (rec.track))
                     : defaultSong ();
    inst.setTrack (song.sections[0].track);
    inst.setSong (song);

    const host = appHost (rec ? rec.name.toUpperCase () : 'UNTITLED');
    traxHandle = mountTrax (host, inst, {
        project: rec,
        song: song,
        existingNames: () => projects.map (p => p.name),
        onExit: () => openTrax (),
        onSave: (name, currentSong) => saveProject (name, currentSong)
    });
    show ('view-app');

    // This click IS the user gesture, so warm the AudioContext now — otherwise
    // the first PLAY eats the resume latency and feels broken.
    try { await inst.init (); }
    catch (e) { console.warn ('audio init failed', e); }
}

/// Writes the current song to the shelf under `name`. Same name overwrites,
/// new name appends — the rule lives in engine/library.js, not here.
function saveProject (name, song) {
    const rec = makeRecord (name, song.sections[0].track, Date.now (), store.nextSeq (), song);
    const res = upsert (projects, rec);
    projects = res.list;
    store.save (projects);
    setCrumb (res.record.name.toUpperCase ());
    return res.record;
}

// ---------- go ----------

buildHome ();
runBoot (() => show ('view-home'));

// Handy while iterating on sound.
window.TRAX = inst;
