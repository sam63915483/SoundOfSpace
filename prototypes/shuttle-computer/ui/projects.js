// The TRAX project menu — the first thing you see when you open the app.
//
//   TRAX  ->  [ NEW PROJECT ]      -> straight into the instrument, blank track
//             [ LOAD PROJECT ]     -> the shelf, pick one, it opens
//
// It lives INSIDE the TRAX app view rather than being a fourth OS screen, on
// purpose: this is a menu belonging to one application, not a place on the
// desktop. Step 2 rebuilds it as a UGUI panel in the same host object.

import { classify } from '../engine/classifier.js';
import { sortRecent } from '../engine/library.js';

function pad2 (n) { return (n < 10 ? '0' : '') + n; }

/// ASCII only, deliberately — the Unity font atlas has no fancy glyphs and the
/// port should be able to reuse these strings verbatim.
function stamp (ms) {
    const d = new Date (ms);
    return d.getFullYear () + '-' + pad2 (d.getMonth () + 1) + '-' + pad2 (d.getDate ()) +
           '  ' + pad2 (d.getHours ()) + ':' + pad2 (d.getMinutes ());
}

function hex8 (n) {
    return (n >>> 0).toString (16).toUpperCase ().padStart (8, '0');
}

export function mountProjects (host, opts) {
    const records = opts.records || [];
    host.innerHTML = '';

    const wrap = document.createElement ('div');
    wrap.id = 'projects';

    // ---------- menu pane ----------
    const menu = document.createElement ('div');
    menu.className = 'proj-pane';
    menu.id = 'proj-menu';

    const brand = document.createElement ('div');
    brand.id = 'proj-brand';
    brand.textContent = 'TRAX';

    const tag = document.createElement ('div');
    tag.id = 'proj-tag';
    tag.textContent = 'PATTERN SYNTHESIS SUITE';

    const menuBtns = document.createElement ('div');
    menuBtns.id = 'proj-btns';

    const newBtn = document.createElement ('button');
    newBtn.className = 'proj-btn primary';
    newBtn.id = 'proj-new';
    const newTitle = document.createElement ('div'); newTitle.className = 'pb-title'; newTitle.textContent = 'NEW PROJECT';
    const newSub   = document.createElement ('div'); newSub.className   = 'pb-sub';   newSub.textContent   = 'start from a blank track';
    newBtn.append (newTitle, newSub);
    newBtn.addEventListener ('click', () => opts.onNew ());

    const loadBtn = document.createElement ('button');
    loadBtn.className = 'proj-btn';
    loadBtn.id = 'proj-load';
    const loadTitle = document.createElement ('div'); loadTitle.className = 'pb-title'; loadTitle.textContent = 'LOAD PROJECT';
    const loadSub   = document.createElement ('div'); loadSub.className   = 'pb-sub';
    loadBtn.append (loadTitle, loadSub);
    loadBtn.addEventListener ('click', () => { if (records.length) showList (); });

    menuBtns.append (newBtn, loadBtn);

    const foot = document.createElement ('div');
    foot.id = 'proj-foot';

    const homeBtn = document.createElement ('button');
    homeBtn.className = 'btn ghost';
    homeBtn.id = 'proj-home';
    homeBtn.textContent = 'HOME';
    homeBtn.addEventListener ('click', () => opts.onHome ());
    foot.append (homeBtn);

    menu.append (brand, tag, menuBtns, foot);

    // ---------- list pane ----------
    const list = document.createElement ('div');
    list.className = 'proj-pane';
    list.id = 'proj-list-pane';

    const listHead = document.createElement ('div');
    listHead.id = 'proj-list-head';
    const lhTitle = document.createElement ('div'); lhTitle.id = 'proj-list-title'; lhTitle.textContent = 'SAVED PROJECTS';
    const lhCount = document.createElement ('div'); lhCount.id = 'proj-list-count';
    listHead.append (lhTitle, lhCount);

    const rows = document.createElement ('div');
    rows.id = 'proj-rows';

    const listFoot = document.createElement ('div');
    listFoot.id = 'proj-list-foot';
    const backBtn = document.createElement ('button');
    backBtn.className = 'btn ghost';
    backBtn.id = 'proj-back';
    backBtn.textContent = 'BACK';
    backBtn.addEventListener ('click', () => showMenu ());
    const warnEl = document.createElement ('div');
    warnEl.id = 'proj-warn';
    listFoot.append (backBtn, warnEl);

    list.append (listHead, rows, listFoot);

    wrap.append (menu, list);
    host.appendChild (wrap);

    // ---------- rendering ----------

    function refreshMenu () {
        const n = records.length;
        loadSub.textContent = n === 0 ? 'nothing saved yet'
                            : n === 1 ? '1 project on the shelf'
                            : n + ' projects on the shelf';
        loadBtn.classList.toggle ('disabled', n === 0);
        lhCount.textContent = n + (n === 1 ? ' PROJECT' : ' PROJECTS');
        warnEl.textContent = opts.persistent ? '' : 'NOT PERSISTED - THIS BROWSER BLOCKS STORAGE';
    }

    const delButtons = [];
    function disarmAll () { for (const d of delButtons) if (d.disarm) d.disarm (); }

    function buildRows () {
        rows.innerHTML = '';
        if (!records.length) {
            const empty = document.createElement ('div');
            empty.id = 'proj-empty';
            empty.textContent = 'THE SHELF IS EMPTY';
            rows.appendChild (empty);
            return;
        }

        for (const rec of sortRecent (records)) {
            const row = document.createElement ('div');
            row.className = 'proj-row';

            const main = document.createElement ('div');
            main.className = 'pr-main';
            const nm = document.createElement ('div'); nm.className = 'pr-name';
            nm.textContent = rec.name.toUpperCase ();
            const meta = document.createElement ('div'); meta.className = 'pr-meta';
            meta.textContent = classify (rec.track.dials).label + '   ID ' + hex8 (rec.trackId) +
                               '   ' + stamp (rec.savedAt);
            main.append (nm, meta);

            const open = document.createElement ('button');
            open.className = 'btn primary pr-open';
            open.textContent = 'OPEN';
            open.addEventListener ('click', e => { e.stopPropagation (); opts.onOpen (rec); });

            // Two-step rather than a modal: the second press is the confirm, and
            // it reverts if you click anything else. No blocking dialogs.
            const del = document.createElement ('button');
            del.className = 'btn danger pr-del';
            del.textContent = 'DELETE';
            let armed = false;
            del.addEventListener ('click', e => {
                e.stopPropagation ();
                if (!armed) {
                    disarmAll ();
                    armed = true;
                    del.textContent = 'SURE?';
                    del.classList.add ('armed');
                    return;
                }
                opts.onDelete (rec);
            });
            del.disarm = () => { armed = false; del.textContent = 'DELETE'; del.classList.remove ('armed'); };
            delButtons.push (del);

            row.addEventListener ('click', () => opts.onOpen (rec));
            row.append (main, open, del);
            rows.appendChild (row);
        }
    }

    function showMenu () {
        wrap.classList.remove ('showing-list');
        refreshMenu ();
    }
    function showList () {
        delButtons.length = 0;
        buildRows ();
        refreshMenu ();
        wrap.classList.add ('showing-list');
    }

    function onKey (e) {
        if (e.key !== 'Escape') return;
        if (wrap.classList.contains ('showing-list')) { e.preventDefault (); showMenu (); }
        else opts.onHome ();
    }
    window.addEventListener ('keydown', onKey);

    refreshMenu ();
    if (opts.startOnList && records.length) showList ();

    return {
        dispose () { window.removeEventListener ('keydown', onKey); }
    };
}
