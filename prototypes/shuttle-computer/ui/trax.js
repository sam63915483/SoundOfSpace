// TRAX — the music app screen.
//
// Talks only to the Instrument. Step 2 rebuilds this exact surface in UGUI, so
// keep the interaction vocabulary simple: knobs, toggles, transport.
//
// ── The arrangement layer ────────────────────────────────────────────────
// The screen now edits a SONG (engine/song.js): an ordered strip of sections,
// each owning a whole track. Everything below the strip — dials, rack, grid —
// is the editor for the SELECTED section, and is exactly the old single-loop
// surface. Two transports: PLAY TRACK chains the sections, LOOP SEC loops the
// selected one while you shape it.

import { DIAL_DEFS } from '../engine/params.js';
import { MODULES } from '../audio/instrument.js';
import { STEPS, BARS, TOTAL_STEPS } from '../engine/patterns.js';
import * as SONG from '../engine/song.js';
import { createKnob } from './knob.js';

const VOICE_MODULE = {
    kick: 'THUMPER', snare: 'THUMPER', hat: 'THUMPER',
    bass: 'GLOWORM', lead: 'SIREN',
    moss: 'MOSS', spindle: 'SPINDLE'
};

// One fixed colour per genre so a section's block, the mix meter and the offer
// chips all agree. UI concern only — the engine never sees colours.
const GENRE_COLORS = {
    GLORP: '#46d17a', DRIFT: '#6f86ff', SKITTER: '#ff9f45', SLUDJ: '#9a6a4a',
    CHIRP: '#ffd94f', NULLGAZE: '#8fa3b5', THRUM: '#b06fff', VOLT: '#3ae0ff',
    WARBLE: '#2fb59a', CLANG: '#ff5a5a'
};

// opts: { project, song, existingNames, onExit, onSave }
//   project           — the record this screen is editing, or null for an
//                       unsaved new one. Only { name } is read.
//   song              — the working song. Owned and mutated by this screen;
//                       the instrument's selected track is section `sel`.
//   existingNames     — names already on the shelf, so SAVE can warn about an
//                       overwrite before it happens.
//   onSave(name,song) — hands both up; the owner writes the shelf and returns
//                       the stored record (or null if it refused).
export function mountTrax (root, inst, opts) {
    opts = opts || {};
    const onExit = opts.onExit || function () {};
    root.innerHTML = '';
    const body = document.createElement ('div');
    body.id = 'trax-body';
    root.appendChild (body);

    let song = opts.song || SONG.defaultSong ();
    let sel = 0;
    // The compiled song inside the instrument only matters during song
    // playback, so edits just mark it stale instead of recompiling on every
    // knob tick of a drag.
    let songStale = false;

    // ---------- project bar ----------
    // Which project you are editing, and whether what you can hear right now is
    // actually on the shelf. Dirtiness is derived from the song identity hash
    // rather than bookkept, so undoing an edit correctly goes back to CLEAN.
    let project = opts.project || null;
    let savedSongId = project ? SONG.songId (song) : -1;

    const projBar = document.createElement ('div');
    projBar.id = 'proj-bar';
    const pbLabel = document.createElement ('div'); pbLabel.className = 'pb-label'; pbLabel.textContent = 'PROJECT';
    const pbName  = document.createElement ('div'); pbName.id = 'proj-bar-name';
    const pbState = document.createElement ('div'); pbState.id = 'proj-bar-state';
    projBar.append (pbLabel, pbName, pbState);
    body.appendChild (projBar);

    let saveBtn = null;                       // built with the transport, below
    function isDirty () { return SONG.songId (song) !== savedSongId; }

    function refreshProjBar () {
        pbName.textContent = project ? project.name.toUpperCase () : 'UNTITLED';
        pbName.classList.toggle ('untitled', !project);
        const dirty = isDirty ();
        pbState.textContent = !project ? 'NEVER SAVED' : dirty ? 'UNSAVED CHANGES' : 'SAVED';
        pbState.classList.toggle ('dirty', dirty);
        if (saveBtn) saveBtn.classList.toggle ('attn', dirty);
    }

    // ---------- arranger ----------
    // The song as a strip of blocks, width proportional to bars, coloured by
    // genre. Click a block to edit that section below. The row underneath is
    // split: section surgery on the left, the song's worth on the right.
    const arranger = document.createElement ('div');
    arranger.id = 'arranger';

    const arrHead = document.createElement ('div');
    arrHead.id = 'arr-head';
    const arrLabel = document.createElement ('div');
    arrLabel.className = 'section-label';
    arrLabel.textContent = 'SONG';
    const arrStats = document.createElement ('div');
    arrStats.id = 'arr-stats';
    arrHead.append (arrLabel, arrStats);

    // Ruler + strip share one positioned wrapper so the playhead line can run
    // down through both. The ruler mirrors the strip's flex maths exactly
    // (same grow factors, gap and end spacer), which is what keeps a tick and
    // the bar it names vertically aligned.
    const timeline = document.createElement ('div');
    timeline.id = 'arr-timeline';

    const ruler = document.createElement ('div');
    ruler.id = 'arr-ruler';

    const strip = document.createElement ('div');
    strip.id = 'arr-strip';

    const playheadEl = document.createElement ('div');
    playheadEl.id = 'arr-playhead';

    timeline.append (ruler, strip, playheadEl);

    const arrRow = document.createElement ('div');
    arrRow.id = 'arr-row';
    const arrCtl = document.createElement ('div');
    arrCtl.id = 'arr-ctl';
    const arrInfo = document.createElement ('div');
    arrInfo.id = 'arr-info';
    arrRow.append (arrCtl, arrInfo);

    arranger.append (arrHead, timeline, arrRow);
    body.appendChild (arranger);

    /// Bar ticks above the strip, numbered song-wide (1-based, cumulative) so
    /// "bar 9" means the same thing whichever section it lands in. Labels sit
    /// on a section's first bar and every 4th after; the rest stay bare ticks
    /// so a 2-bar section never has to fit two numbers.
    function refreshRuler () {
        ruler.innerHTML = '';
        let barNo = 1;
        for (const s of song.sections) {
            const cell = document.createElement ('div');
            cell.className = 'arr-ruler-cell';
            cell.style.flexGrow = s.bars;
            for (let b = 0; b < s.bars; b++) {
                const tick = document.createElement ('div');
                tick.className = 'arr-tick';
                if (b % 4 === 0) tick.textContent = String (barNo);
                // Every tick is a seek target: click a bar to put the playhead
                // there — live jump while playing, cursor move while stopped.
                const stepPos = (barNo - 1) * STEPS;
                tick.title = 'play from bar ' + barNo;
                tick.addEventListener ('click', () => seekToStep (stepPos));
                barNo++;
                cell.appendChild (tick);
            }
            ruler.appendChild (cell);
        }
        // Phantom spacer matching the [+] button, so the last bar's width is
        // the same as its block below.
        const end = document.createElement ('div');
        end.className = 'arr-ruler-end';
        ruler.appendChild (end);
    }

    let playingSec = -1;              // section under the audible playhead

    function refreshStrip () {
        strip.innerHTML = '';
        song.sections.forEach ((s, i) => {
            const g = SONG.genreMix ({ sections: [s] })[0];
            const color = GENRE_COLORS[g.name] || '#79ffd0';
            const el = document.createElement ('div');
            el.className = 'arr-sec' + (i === sel ? ' sel' : '') + (i === playingSec ? ' playing' : '');
            el.style.flexGrow = s.bars;
            el.style.borderColor = color;
            el.style.background = color + (i === sel ? '2e' : '14');
            const top = document.createElement ('div');
            top.className = 'as-top';
            top.textContent = SONG.sectionLabel (i) + ' · ' + s.bars;
            const bot = document.createElement ('div');
            bot.className = 'as-bot';
            bot.style.color = color;
            bot.textContent = g.name;
            el.append (top, bot);
            el.addEventListener ('click', () => sectionClicked (i));
            strip.appendChild (el);
        });
        const add = document.createElement ('button');
        add.id = 'arr-add';
        add.textContent = '+';
        add.title = 'add a section (copies the selected one)';
        add.disabled = song.sections.length >= SONG.MAX_SECTIONS;
        add.addEventListener ('click', () => {
            const next = SONG.addSection (song, sel);
            if (next === song) return;
            song = next;
            songEdited ();
            selectSection (sel + 1);
        });
        strip.appendChild (add);
        arrStats.textContent = song.sections.length + ' SEC · ' + SONG.totalBars (song) + ' BARS';
        refreshRuler ();
        // The blocks were just rebuilt — put the idle cursor back on the new
        // geometry (live playback repositions itself every frame anyway).
        updateIdleCursor ();
    }

    // Two-press arm for section delete, same vocabulary as the project
    // shelf's DELETE → SURE?. Lives at mount level so the armed state survives
    // the row being rebuilt by unrelated refreshes; anything that changes the
    // selection or the song disarms it.
    let delArmed = false, delArmTimer = null;
    function disarmDelete () {
        delArmed = false;
        clearTimeout (delArmTimer);
    }

    function refreshCtl () {
        arrCtl.innerHTML = '';
        const label = SONG.sectionLabel (sel);

        const tag = document.createElement ('div');
        tag.id = 'arr-sec-tag';
        tag.textContent = 'SEC ' + label;
        arrCtl.appendChild (tag);

        // LENGTH mirrors the transport's KEY control — a named group around a
        // stepper — so the player already knows how to read it.
        const lenWrap = document.createElement ('div');
        lenWrap.className = 'arr-len-wrap';
        lenWrap.title = 'how many bars section ' + label + ' plays for';
        const lenLabel = document.createElement ('span');
        lenLabel.textContent = 'LENGTH';
        const barsStep = stepper ('arr-bars', () => song.sections[sel].bars + ' BARS', d => {
            const next = SONG.setSectionBars (song, sel, song.sections[sel].bars + d);
            if (next === song) return;
            song = next;
            songEdited ();
            disarmDelete ();
            refreshStrip (); refreshCtl (); refreshSummary (); refreshProjBar ();
        });
        lenWrap.append (lenLabel, barsStep.el);
        arrCtl.appendChild (lenWrap);

        const del = document.createElement ('button');
        del.className = 'btn tiny' + (delArmed ? ' arm' : '');
        del.textContent = delArmed ? 'SURE?' : 'DELETE SEC ' + label;
        del.disabled = song.sections.length <= 1;
        del.title = del.disabled ? 'a song needs at least one section'
                                 : 'remove section ' + label + ' and its loop from the song';
        del.addEventListener ('click', () => {
            if (!delArmed) {
                delArmed = true;
                clearTimeout (delArmTimer);
                delArmTimer = setTimeout (() => { delArmed = false; refreshCtl (); }, 3500);
                refreshCtl ();
                return;
            }
            disarmDelete ();
            const next = SONG.removeSection (song, sel);
            if (next === song) return;
            song = next; songEdited ();
            selectSection (Math.min (sel, song.sections.length - 1));
            toast ('SEC ' + label + ' DELETED');
        });
        arrCtl.appendChild (del);
    }

    // The economy readout. Numbers are engine/song.js placeholders — the SHAPE
    // is what's being playtested: demos unchanged, a full track worth a
    // multiple that grows with sections and length, and each alien's offer
    // diluted to their genre's share of the bars.
    function refreshSummary () {
        arrInfo.innerHTML = '';
        const mix = SONG.genreMix (song);

        const meter = document.createElement ('div');
        meter.id = 'arr-meter';
        for (const m of mix) {
            const seg = document.createElement ('div');
            seg.style.flexGrow = m.bars;
            seg.style.background = GENRE_COLORS[m.name] || '#79ffd0';
            seg.title = m.name + ' ' + Math.round (m.share * 100) + '%';
            meter.appendChild (seg);
        }

        const value = document.createElement ('div');
        value.id = 'arr-value';
        const mult = SONG.songValueMult (song);
        value.textContent = 'FULL TRACK ×' + mult.toFixed (2) + ' DEMO';

        const offers = document.createElement ('div');
        offers.id = 'arr-offers';
        for (const m of mix.slice (0, 4)) {
            const chip = document.createElement ('span');
            chip.className = 'offer-chip';
            chip.style.color = GENRE_COLORS[m.name] || '#79ffd0';
            chip.textContent = m.name + ' FAN ×' + (mult * m.share).toFixed (2);
            offers.appendChild (chip);
        }

        arrInfo.append (meter, value, offers);
    }

    /// Every edit that changes the song funnels through here: the instrument's
    /// compiled copy goes stale, and if the song is audible right now it is
    /// recompiled immediately so you hear what you did.
    function songEdited () {
        songStale = true;
        if (inst.playingSong) { inst.setSong (song); songStale = false; }
    }

    /// inst.track is replaced (immutably) on every editor edit; pull it back
    /// into the selected section. Called from refreshReadouts, which every
    /// editing path already funnels through. The reference is SHARED, not
    /// cloned — inst.track is never mutated in place, and sharing is what
    /// lets the !== check converge instead of re-flagging every refresh.
    function syncSection () {
        if (song.sections[sel].track !== inst.track) {
            song = { sections: song.sections.map ((s, i) =>
                i === sel ? { bars: s.bars, track: inst.track } : s) };
            songEdited ();
        }
    }

    function selectSection (i) {
        sel = Math.min (Math.max (i, 0), song.sections.length - 1);
        // An armed SURE? must never survive onto a different section.
        disarmDelete ();
        inst.setTrack (song.sections[sel].track);
        refreshAllControls ();
    }

    function sectionStartStep (i) {
        let start = 0;
        for (let j = 0; j < i; j++) start += song.sections[j].bars * STEPS;
        return start;
    }

    /// Clicking a block selects it for editing AND auditions it: the song
    /// playhead jumps to the section's first bar, starting playback if it
    /// wasn't running (Sam's call — hearing it beats silence every time).
    async function sectionClicked (i) {
        selectSection (i);
        if (songStale) { inst.setSong (song); songStale = false; }
        inst.seekSong (sectionStartStep (sel));
        if (!inst.playingSong) {
            inst.stop ();                     // a running LOOP SEC yields
            clearPlayhead ();
            await inst.playSong ();
            refreshTransport ();
        }
        updateIdleCursor ();
    }

    /// Ruler seek. Live jump while the song plays; while stopped it just
    /// moves the cursor PLAY TRACK will start from.
    function seekToStep (stepPos) {
        if (songStale && inst.playingSong) { inst.setSong (song); songStale = false; }
        inst.seekSong (stepPos);
        updateIdleCursor ();
    }

    // ---------- genre plate ----------
    const plate = document.createElement ('div');
    plate.className = 'genre-plate';
    // The big magenta word is meaningless without this caption — a new player
    // has no way to know the readout is naming a genre.
    const gStack = document.createElement ('div'); gStack.className = 'genre-stack';
    const gCap   = document.createElement ('div'); gCap.className = 'genre-cap';
    const gLabel = document.createElement ('div'); gLabel.id = 'genre-label';
    gStack.append (gCap, gLabel);
    const gVibe  = document.createElement ('div'); gVibe.id  = 'genre-vibe';
    const gMeta  = document.createElement ('div'); gMeta.id  = 'genre-meta';
    plate.append (gStack, gVibe, gMeta);
    body.appendChild (plate);

    // ---------- dials ----------
    const dials = document.createElement ('div');
    dials.id = 'dials';
    const knobs = {};
    for (const def of DIAL_DEFS) {
        const k = createKnob (def, inst.dials[def.key], (key, value) => {
            inst.setDial (key, value);
            refreshReadouts ();
        });
        knobs[def.key] = k;
        dials.appendChild (k.el);
    }
    body.appendChild (dials);

    // ---------- rack + step grid ----------
    const rackWrap = document.createElement ('div');
    rackWrap.id = 'rack-wrap';
    const rackLabel = document.createElement ('div');
    rackLabel.className = 'section-label';
    rackLabel.textContent = 'PLUGIN RACK';
    const rack = document.createElement ('div');
    rack.id = 'rack';

    const moduleEls = {};
    for (const m of MODULES) {
        const owned = inst.isInstalled (m.name);
        const el = document.createElement ('div');
        el.className = 'module' + (inst.enabled[m.name] && owned ? ' on' : '') +
                       (owned ? '' : ' locked');

        // The on/off toggle is its own click target, so choosing a preset can
        // never accidentally mute the module you are auditioning.
        const head = document.createElement ('div');
        head.className = 'm-head';
        const led = document.createElement ('div'); led.className = 'm-led';
        const nm  = document.createElement ('div'); nm.className = 'm-name'; nm.textContent = m.name;
        // A locked slot says what it costs to unlock, not just that it is dead.
        const ds  = document.createElement ('div'); ds.className = 'm-desc';
        ds.textContent = owned ? m.desc : 'NOT INSTALLED';
        head.append (led, nm, ds);
        head.addEventListener ('click', () => {
            if (!inst.isInstalled (m.name)) { toast (m.name + ' IS NOT INSTALLED'); return; }
            const on = !inst.enabled[m.name];
            inst.setModuleEnabled (m.name, on);
            el.classList.toggle ('on', on);
            refreshReadouts ();
        });

        // PRESET = which part. VARIATION = which roll of that part. Both are
        // dead on a module you do not own — they would silently change the
        // track identity while changing nothing you can hear.
        const preset = stepper ('m-preset', () => owned ? inst.presetName (m.name) : 'LOCKED',
                                d => { if (!owned) return; inst.cyclePreset (m.name, d); afterPartChange (); });
        const varn = stepper ('m-var', () => owned ? 'VAR ' + (inst.variationIndex (m.name) + 1) : '--',
                              d => { if (!owned) return; inst.cycleVariation (m.name, d); afterPartChange (); });

        el.append (head, preset.el, varn.el);
        rack.appendChild (el);
        moduleEls[m.name] = { el, preset, varn };
    }

    function afterPartChange () {
        for (const k in moduleEls) { moduleEls[k].preset.refresh (); moduleEls[k].varn.refresh (); }
        refreshReadouts ();
        refreshGrid (true);
    }

    /// Everything the editor shows about the selected section, refreshed in
    /// one sweep — used when the selection changes and the whole surface has
    /// to snap to a different track.
    function refreshAllControls () {
        for (const def of DIAL_DEFS) knobs[def.key].set (inst.dials[def.key]);
        for (const m of MODULES) {
            const owned = inst.isInstalled (m.name);
            moduleEls[m.name].el.classList.toggle ('on', owned && inst.enabled[m.name]);
            moduleEls[m.name].preset.refresh ();
            moduleEls[m.name].varn.refresh ();
        }
        if (keyStep) keyStep.refresh ();
        refreshReadouts ();
        refreshGrid (true);
    }

    // A left arrow, a label and a right arrow. That is the entire vocabulary
    // for choosing a part, which is the point: no wrong answers to pick from.
    function stepper (cls, label, onStep) {
        const el = document.createElement ('div');
        el.className = 'stepper ' + cls;
        const back = document.createElement ('button');
        back.className = 'st-arrow';
        back.textContent = '◀';
        const text = document.createElement ('div');
        text.className = 'st-label';
        const fwd = document.createElement ('button');
        fwd.className = 'st-arrow';
        fwd.textContent = '▶';
        back.addEventListener ('click', e => { e.stopPropagation (); onStep (-1); });
        fwd.addEventListener ('click', e => { e.stopPropagation (); onStep (1); });
        el.append (back, text, fwd);
        const api = { el, refresh: () => { text.textContent = label (); } };
        api.refresh ();
        return api;
    }

    const steps = document.createElement ('div');
    steps.id = 'steps';
    const stepEls = [];
    for (let i = 0; i < STEPS; i++) {
        const s = document.createElement ('div');
        s.className = 'st';
        steps.appendChild (s);
        stepEls.push (s);
    }

    rackWrap.append (rackLabel, rack, steps);
    body.appendChild (rackWrap);

    // ---------- transport ----------
    const transport = document.createElement ('div');
    transport.id = 'transport';

    // PLAY TRACK chains the whole song; LOOP SEC is the old behaviour — loop
    // the selected section forever while you shape it.
    const playBtn = document.createElement ('button');
    playBtn.className = 'btn primary';
    playBtn.textContent = 'PLAY TRACK';

    const loopBtn = document.createElement ('button');
    loopBtn.className = 'btn';
    loopBtn.id = 'loop-btn';
    loopBtn.textContent = 'LOOP SECTION';

    const readout = document.createElement ('div');
    readout.id = 'readout';

    const sep = document.createElement ('div');
    sep.className = 'sep';

    // PRINT DEMO - one button that opens a dialog. The quantity lives in there,
    // so the transport row is not carrying a stepper for something you press
    // once. Deliberately inert beyond choosing a number; cassettes are a later
    // phase, but the shape of the interaction is right for when it is wired up.
    let quantity = 1;
    const printWrap = document.createElement ('div');
    printWrap.id = 'print-wrap';
    const printBtn = document.createElement ('button');
    printBtn.className = 'btn';
    printBtn.textContent = 'PRINT TAPE';
    printBtn.addEventListener ('click', () => openPrint ());
    printWrap.append (printBtn);

    // KEY — one control that moves everything, and regenerates nothing.
    const keyWrap = document.createElement ('div');
    keyWrap.className = 'key-wrap';
    const keyLabel = document.createElement ('span'); keyLabel.textContent = 'KEY';
    const keyStep = stepper ('key-step', () => inst.keyName,
                             d => { inst.cycleKey (d); keyStep.refresh (); refreshReadouts (); });
    keyWrap.append (keyLabel, keyStep.el);

    const volWrap = document.createElement ('div');
    volWrap.className = 'vol-wrap';
    const volLabel = document.createElement ('span'); volLabel.textContent = 'VOL';
    const vol = document.createElement ('input');
    vol.type = 'range'; vol.id = 'vol'; vol.min = '0'; vol.max = '1'; vol.step = '0.01';
    vol.value = String (inst.masterVolume);
    vol.addEventListener ('input', () => inst.setMasterVolume (parseFloat (vol.value)));
    volWrap.append (volLabel, vol);

    // SAVE PROJECT sits next to PRINT because they are the two things you do
    // WITH a finished track, as opposed to the controls that shape it.
    saveBtn = document.createElement ('button');
    saveBtn.className = 'btn';
    saveBtn.id = 'save-btn';
    saveBtn.textContent = 'SAVE PROJECT';
    saveBtn.addEventListener ('click', () => openSave ());

    // Leaves the instrument for the project menu, not for the desktop — the
    // menu is one level up, and HOME lives there.
    const exitBtn = document.createElement ('button');
    exitBtn.className = 'btn ghost';
    exitBtn.id = 'exit-btn';
    exitBtn.textContent = 'PROJECTS';
    exitBtn.addEventListener ('click', () => onExit ());

    transport.append (playBtn, loopBtn, readout, sep, keyWrap, volWrap, saveBtn, printWrap, exitBtn);
    body.appendChild (transport);

    // ---------- save dialog ----------
    const saveScrim = document.createElement ('div');
    saveScrim.id = 'save-scrim';
    const savePanel = document.createElement ('div');
    savePanel.id = 'save-panel';
    const sTitle = document.createElement ('div'); sTitle.id = 'save-title'; sTitle.textContent = 'SAVE PROJECT';
    const sSub   = document.createElement ('div'); sSub.id = 'save-sub';     sSub.textContent = 'NAME THIS TRACK';
    const sInput = document.createElement ('input');
    sInput.id = 'save-name';
    sInput.type = 'text';
    sInput.maxLength = 24;
    sInput.placeholder = 'UNTITLED';
    const sNote  = document.createElement ('div'); sNote.id = 'save-note';
    const sRow   = document.createElement ('div'); sRow.id = 'save-row';
    const sCancel = document.createElement ('button'); sCancel.className = 'btn ghost';   sCancel.textContent = 'CANCEL';
    const sOk     = document.createElement ('button'); sOk.className = 'btn primary';     sOk.textContent = 'SAVE';
    sRow.append (sCancel, sOk);
    savePanel.append (sTitle, sSub, sInput, sNote, sRow);
    saveScrim.appendChild (savePanel);
    root.appendChild (saveScrim);

    // Asked for fresh each time the dialog opens — saving under a new name grows
    // the shelf, and the overwrite warning has to know about it.
    function otherNames () {
        const all = typeof opts.existingNames === 'function' ? opts.existingNames ()
                  : (opts.existingNames || []);
        const mine = project ? project.name.toLowerCase () : null;
        return all.filter (n => n.toLowerCase () !== mine);
    }

    function refreshSaveNote () {
        const typed = (sInput.value || '').trim ();
        if (!typed) { sNote.textContent = 'a name is required'; sNote.className = 'warn'; sOk.classList.add ('disabled'); return; }
        sOk.classList.remove ('disabled');
        const clash = otherNames ().some (n => n.toLowerCase () === typed.toLowerCase ());
        if (clash) { sNote.textContent = 'overwrites the project already called that'; sNote.className = 'warn'; }
        else if (project && typed.toLowerCase () === project.name.toLowerCase ()) { sNote.textContent = 'saves over this project'; sNote.className = ''; }
        else { sNote.textContent = 'saves as a new project'; sNote.className = ''; }
    }
    sInput.addEventListener ('input', refreshSaveNote);

    function openSave () {
        sInput.value = project ? project.name : '';
        refreshSaveNote ();
        saveScrim.classList.add ('show');
        if (sInput.focus) sInput.focus ();
        if (sInput.select) sInput.select ();
    }
    function closeSave () { saveScrim.classList.remove ('show'); }
    function saveOpen () { return saveScrim.classList.contains ('show'); }

    function commitSave () {
        const typed = (sInput.value || '').trim ();
        if (!typed) return;
        const rec = opts.onSave ? opts.onSave (typed, song) : null;
        if (!rec) return;
        project = rec;
        savedSongId = SONG.songId (song);
        closeSave ();
        refreshProjBar ();
        toast ('SAVED - ' + rec.name.toUpperCase ());
    }
    sCancel.addEventListener ('click', () => closeSave ());
    sOk.addEventListener ('click', () => commitSave ());
    sInput.addEventListener ('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault (); commitSave (); }
    });

    // ---------- print dialog ----------
    // Two things can go to tape: a DEMO of the selected section's loop (the
    // old, unchanged product) or the FULL TRACK — every section, worth the
    // arranger's multiplier. Both stay inert until the tape deck exists.
    const printScrim = document.createElement ('div');
    printScrim.id = 'print-scrim';
    const printPanel = document.createElement ('div');
    printPanel.id = 'print-panel';
    const pTitle = document.createElement ('div'); pTitle.id = 'print-title'; pTitle.textContent = 'PRINT';
    const pSub   = document.createElement ('div'); pSub.id = 'print-sub';
    let printFull = false;
    const pMode = stepper ('print-mode', () => printFull ? 'FULL TRACK' : 'DEMO · SEC ' + SONG.sectionLabel (sel),
                           () => { printFull = !printFull; pMode.refresh (); refreshPrintSub (); });
    const pQty = stepper ('print-qty', () => String (quantity),
                          d => { quantity = Math.min (99, Math.max (1, quantity + d)); pQty.refresh (); });
    const pNote = document.createElement ('div'); pNote.id = 'print-note'; pNote.textContent = 'no tape deck installed';
    const pRow = document.createElement ('div'); pRow.id = 'print-row';
    const pCancel = document.createElement ('button'); pCancel.className = 'btn ghost'; pCancel.textContent = 'CANCEL';
    const pOk = document.createElement ('button'); pOk.className = 'btn primary'; pOk.textContent = 'PRINT';
    pCancel.addEventListener ('click', () => closePrint ());
    pOk.addEventListener ('click', () => {
        closePrint ();
        const what = printFull ? 'FULL TRACK' : 'DEMO';
        toast ('PRINT ' + what + ' x' + quantity + ' QUEUED — NO TAPE DECK INSTALLED');
    });
    pRow.append (pCancel, pOk);
    printPanel.append (pTitle, pSub, pMode.el, pQty.el, pNote, pRow);
    printScrim.appendChild (printPanel);
    root.appendChild (printScrim);

    function refreshPrintSub () {
        pSub.textContent = printFull
            ? 'ALL SECTIONS · WORTH ×' + SONG.songValueMult (song).toFixed (2) + ' DEMO'
            : 'JUST THIS SECTION’S LOOP · STANDARD DEMO PRICE';
    }
    function openPrint () { pMode.refresh (); pQty.refresh (); refreshPrintSub (); printScrim.classList.add ('show'); }
    function closePrint () { printScrim.classList.remove ('show'); }
    function printOpen () { return printScrim.classList.contains ('show'); }

    // ---------- toast ----------
    const toastEl = document.createElement ('div');
    toastEl.id = 'toast';
    root.appendChild (toastEl);
    let toastTimer = null;
    function toast (msg) {
        toastEl.textContent = msg;
        toastEl.classList.add ('show');
        clearTimeout (toastTimer);
        toastTimer = setTimeout (() => toastEl.classList.remove ('show'), 1800);
    }

    // ---------- live readouts ----------
    function refreshReadouts () {
        syncSection ();
        const g = inst.genre;
        gCap.textContent = 'SEC ' + SONG.sectionLabel (sel) + ' GENRE';
        gLabel.textContent = g.label;
        gVibe.textContent = g.primary.vibe;
        gMeta.textContent =
            'TRACK ' + inst.trackId.toString (16).toUpperCase ().padStart (8, '0') +
            '\nMARGIN ' + (g.d2 - g.d1).toFixed (2) + (g.blended ? '  BLEND' : '  LOCK');
        if (!inst.playing)
            readout.textContent = Math.round (inst.params.bpm) + ' BPM';
        refreshStrip ();
        refreshCtl ();
        refreshSummary ();
        refreshProjBar ();
        refreshGrid (true);
    }

    // ---------- step grid ----------
    let currentBar = 0, currentStep = -1, gridDirty = true;

    function barHits (bar) {
        const out = new Array (STEPS).fill (false);
        for (const v in VOICE_MODULE) {
            if (!inst.enabled[VOICE_MODULE[v]]) continue;
            const b = inst.patterns[v][bar];
            for (let i = 0; i < STEPS; i++) if (b[i]) out[i] = true;
        }
        return out;
    }

    function refreshGrid (force) {
        if (force) gridDirty = true;
    }

    function drawGrid () {
        const hits = barHits (currentBar);
        for (let i = 0; i < STEPS; i++) {
            stepEls[i].classList.toggle ('hit', hits[i]);
        }
        gridDirty = false;
    }

    // The scheduler runs ~100ms ahead of what you hear, so the playhead can't
    // just follow it — queue the scheduled steps and light them up when the
    // audio clock actually reaches them. In song mode the queued step is
    // song-relative; sectionAtStep maps it to a block in the strip.
    const queue = [];
    inst.onStepScheduled = (step, time) => { queue.push ([step, time]); };
    inst.onPatternSwap = () => refreshGrid (true);

    function setPlayingSec (i) {
        if (i === playingSec) return;
        playingSec = i;
        // Class flips only — rebuilding the strip 8 times a bar would fight
        // the click targets under the cursor.
        const blocks = strip.querySelectorAll ('.arr-sec');
        blocks.forEach ((b, j) => b.classList.toggle ('playing', j === playingSec));
    }

    function clearPlayhead () {
        queue.length = 0;
        if (currentStep >= 0) stepEls[currentStep].classList.remove ('now');
        currentStep = -1;
        setPlayingSec (-1);
        phLast = null;
        updateIdleCursor ();
        readout.textContent = Math.round (inst.params.bpm) + ' BPM';
    }

    // The last step the SPEAKERS reached (position + its audio-clock time).
    // The ruler line interpolates between this step and the next each frame,
    // so it glides instead of ticking sixteen times a bar.
    let phLast = null;

    /// Put the line at a song step (+ a fraction of one step). `idle` is the
    /// dimmer where-play-will-start look; live playback uses the hot one.
    function placeLine (songStep, fracWithinStep, idle) {
        const total = SONG.totalSteps (song);
        const p = ((songStep % total) + total) % total;
        const loc = SONG.sectionAtStep (song, p);
        const bl = strip.querySelectorAll ('.arr-sec')[loc.index];
        // offsetWidth is 0 before layout (and absent in the test DOM) — no
        // geometry, no line.
        if (!bl || !bl.offsetWidth) { playheadEl.style.display = 'none'; return; }
        const f = (loc.stepInSection + fracWithinStep) / (song.sections[loc.index].bars * STEPS);
        playheadEl.style.display = 'block';
        playheadEl.classList.toggle ('idle', !!idle);
        playheadEl.style.transform =
            'translateX(' + (bl.offsetLeft + f * bl.offsetWidth).toFixed (1) + 'px)';
    }

    /// While stopped, the line still shows — dimmed — where PLAY TRACK will
    /// start, so a ruler click has visible effect before you hit play.
    function updateIdleCursor () {
        if (inst.playingSong) return;
        placeLine (inst.songCursor, 0, true);
    }

    function updatePlayhead (now) {
        if (!inst.playingSong || !phLast) { updateIdleCursor (); return; }
        const total = SONG.totalSteps (song);
        const loc = SONG.sectionAtStep (song, phLast.pos % total);
        const comp = inst.songCompiled && inst.songCompiled[loc.index];
        const stepDur = comp ? (60 / comp.params.bpm / 4) : 0.125;
        const frac = Math.min (Math.max ((now - phLast.time) / stepDur, 0), 1);
        placeLine (phLast.pos, frac, false);
    }

    function frame () {
        if (inst.ctx && inst.playing) {
            const now = inst.ctx.currentTime;
            let shown = -1, shownTime = 0;
            while (queue.length && queue[0][1] <= now) {
                shown = queue[0][0];
                shownTime = queue[0][1];
                queue.shift ();
            }
            if (shown >= 0) {
                if (inst.songMode) phLast = { pos: shown, time: shownTime };
                if (inst.songMode) {
                    // Guard: the song may have been edited after this step was
                    // queued; clamp rather than crash on a stale position.
                    const total = SONG.totalSteps (song);
                    const loc = SONG.sectionAtStep (song, shown % total);
                    setPlayingSec (loc.index);
                    const sec = song.sections[loc.index];
                    // Same fill-bar remap the scheduler uses, so the grid
                    // lights the bar that is actually sounding.
                    const patStep = SONG.patternStepFor (sec, loc.stepInSection);
                    const bar = Math.floor (patStep / STEPS);
                    const s = patStep % STEPS;
                    // The grid is the SELECTED section's editor — only show the
                    // playhead when the song is actually inside it.
                    if (loc.index === sel) {
                        if (bar !== currentBar) { currentBar = bar; gridDirty = true; }
                        if (s !== currentStep) {
                            if (currentStep >= 0) stepEls[currentStep].classList.remove ('now');
                            currentStep = s;
                            stepEls[s].classList.add ('now');
                        }
                    } else if (currentStep >= 0) {
                        stepEls[currentStep].classList.remove ('now');
                        currentStep = -1;
                    }
                    // The clock's bpm runs ~100ms ahead of the speakers (it is
                    // already on the NEXT section at a boundary) — show the
                    // tempo of the section under the audible playhead instead.
                    const heardBpm = inst.songCompiled && inst.songCompiled[loc.index]
                        ? inst.songCompiled[loc.index].params.bpm : inst.clock.bpm;
                    readout.textContent =
                        Math.round (heardBpm) + ' BPM   SEC ' + SONG.sectionLabel (loc.index) +
                        '  BAR ' + (loc.barInSection + 1) + '/' + sec.bars;
                } else {
                    setPlayingSec (-1);
                    const bar = Math.floor ((shown % (STEPS * BARS)) / STEPS);
                    const s = shown % STEPS;
                    if (bar !== currentBar) { currentBar = bar; gridDirty = true; }
                    if (s !== currentStep) {
                        if (currentStep >= 0) stepEls[currentStep].classList.remove ('now');
                        currentStep = s;
                        stepEls[s].classList.add ('now');
                        readout.textContent =
                            Math.round (inst.params.bpm) + ' BPM   BAR ' + (currentBar + 1) + '/' + BARS +
                            '   LOOP SECTION ' + SONG.sectionLabel (sel);
                    }
                }
            }
            updatePlayhead (now);
        }
        if (gridDirty) drawGrid ();
        requestAnimationFrame (frame);
    }
    requestAnimationFrame (frame);

    // ---------- transport wiring ----------
    function refreshTransport () {
        playBtn.textContent = inst.playingSong ? 'STOP' : 'PLAY TRACK';
        playBtn.classList.toggle ('primary', !inst.playing || inst.playingSong);
        loopBtn.textContent = inst.playingLoop ? 'STOP' : 'LOOP SECTION';
    }

    async function togglePlaySong () {
        if (inst.playingSong) { inst.stop (); clearPlayhead (); }
        else {
            inst.stop ();
            clearPlayhead ();
            if (songStale) { inst.setSong (song); songStale = false; }
            await inst.playSong ();
        }
        refreshTransport ();
    }

    async function togglePlayLoop () {
        if (inst.playingLoop) { inst.stop (); clearPlayhead (); }
        else {
            inst.stop ();
            clearPlayhead ();
            await inst.play ();
        }
        refreshTransport ();
    }

    playBtn.addEventListener ('click', togglePlaySong);
    loopBtn.addEventListener ('click', togglePlayLoop);

    function onKey (e) {
        // Both dialogs are modal: ESC dismisses them rather than leaving TRAX,
        // and the transport shortcut is suppressed while one is up — SPACE has
        // to reach the name field as a space, not as PLAY.
        if (saveOpen ()) {
            if (e.key === 'Escape') { e.preventDefault (); closeSave (); }
            return;
        }
        if (printOpen ()) {
            if (e.key === 'Escape') { e.preventDefault (); closePrint (); }
            return;
        }
        if (e.target && e.target.classList && e.target.classList.contains ('knob')) {
            if (e.key !== ' ' && e.key !== 'Escape') return;
        }
        if (e.code === 'Space') { e.preventDefault (); togglePlaySong (); }
        else if (e.key === 'Escape') { onExit (); }
    }
    window.addEventListener ('keydown', onKey);

    refreshProjBar ();
    refreshReadouts ();
    refreshTransport ();
    drawGrid ();

    return {
        stop () {
            inst.stop ();
            clearPlayhead ();
            refreshTransport ();
        },
        dispose () {
            window.removeEventListener ('keydown', onKey);
            inst.onStepScheduled = null;
            inst.onPatternSwap = null;
        }
    };
}
