// TRAX — the music app screen.
//
// Talks only to the Instrument. Step 2 rebuilds this exact surface in UGUI, so
// keep the interaction vocabulary simple: knobs, toggles, transport.

import { DIAL_DEFS } from '../engine/params.js';
import { MODULES } from '../audio/instrument.js';
import { STEPS, BARS } from '../engine/patterns.js';
import { createKnob } from './knob.js';

const VOICE_MODULE = {
    kick: 'THUMPER', snare: 'THUMPER', hat: 'THUMPER',
    bass: 'GLOWORM', lead: 'SIREN',
    moss: 'MOSS', spindle: 'SPINDLE'
};

export function mountTrax (root, inst, onExit) {
    root.innerHTML = '';
    const body = document.createElement ('div');
    body.id = 'trax-body';
    root.appendChild (body);

    // ---------- genre plate ----------
    const plate = document.createElement ('div');
    plate.className = 'genre-plate';
    // The big magenta word is meaningless without this caption — a new player
    // has no way to know the readout is naming a genre.
    const gStack = document.createElement ('div'); gStack.className = 'genre-stack';
    const gCap   = document.createElement ('div'); gCap.className = 'genre-cap';
    gCap.textContent = 'GENRE';
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

    for (const m of MODULES) {
        const el = document.createElement ('div');
        el.className = 'module' + (m.locked ? ' locked' : (inst.enabled[m.name] ? ' on' : ''));
        const led = document.createElement ('div'); led.className = 'm-led';
        const nm  = document.createElement ('div'); nm.className = 'm-name'; nm.textContent = m.name;
        const ds  = document.createElement ('div'); ds.className = 'm-desc'; ds.textContent = m.desc;
        el.append (led, nm, ds);
        if (!m.locked) {
            el.addEventListener ('click', () => {
                const on = !inst.enabled[m.name];
                inst.setModuleEnabled (m.name, on);
                el.classList.toggle ('on', on);
                refreshGrid (true);
            });
        }
        rack.appendChild (el);
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

    const playBtn = document.createElement ('button');
    playBtn.className = 'btn primary';
    playBtn.textContent = 'PLAY';

    const readout = document.createElement ('div');
    readout.id = 'readout';

    const sep = document.createElement ('div');
    sep.className = 'sep';

    // PRINT — visual only today. The stepper and button work, nothing is saved.
    const printWrap = document.createElement ('div');
    printWrap.id = 'print-wrap';
    const minus = document.createElement ('button'); minus.className = 'btn tiny ghost'; minus.textContent = '-';
    const qty   = document.createElement ('div');    qty.id = 'qty'; qty.textContent = '1';
    const plus  = document.createElement ('button'); plus.className = 'btn tiny ghost'; plus.textContent = '+';
    const printBtn = document.createElement ('button'); printBtn.className = 'btn'; printBtn.textContent = 'PRINT';
    let quantity = 1;
    minus.addEventListener ('click', () => { quantity = Math.max (1, quantity - 1); qty.textContent = quantity; });
    plus.addEventListener  ('click', () => { quantity = Math.min (99, quantity + 1); qty.textContent = quantity; });
    printBtn.addEventListener ('click', () => {
        toast ('PRINT x' + quantity + ' — NO TAPE DECK INSTALLED');
    });
    printWrap.append (minus, qty, plus, printBtn);

    const volWrap = document.createElement ('div');
    volWrap.className = 'vol-wrap';
    const volLabel = document.createElement ('span'); volLabel.textContent = 'VOL';
    const vol = document.createElement ('input');
    vol.type = 'range'; vol.id = 'vol'; vol.min = '0'; vol.max = '1'; vol.step = '0.01';
    vol.value = String (inst.masterVolume);
    vol.addEventListener ('input', () => inst.setMasterVolume (parseFloat (vol.value)));
    volWrap.append (volLabel, vol);

    const exitBtn = document.createElement ('button');
    exitBtn.className = 'btn ghost';
    exitBtn.textContent = 'EXIT';
    exitBtn.addEventListener ('click', () => onExit ());

    transport.append (playBtn, readout, sep, volWrap, printWrap, exitBtn);
    body.appendChild (transport);

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
        const g = inst.genre;
        gLabel.textContent = g.label;
        gVibe.textContent = g.primary.vibe;
        gMeta.textContent =
            'SEED ' + inst.seed.toString (16).toUpperCase ().padStart (8, '0') +
            '\nMARGIN ' + (g.d2 - g.d1).toFixed (2) + (g.blended ? '  BLEND' : '  LOCK');
        readout.textContent =
            Math.round (inst.params.bpm) + ' BPM   BAR ' + (currentBar + 1) + '/' + BARS;
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
    // audio clock actually reaches them.
    const queue = [];
    inst.onStepScheduled = (step, time) => { queue.push ([step, time]); };
    inst.onPatternSwap = () => refreshGrid (true);

    function frame () {
        if (inst.ctx) {
            const now = inst.ctx.currentTime;
            let shown = -1;
            while (queue.length && queue[0][1] <= now) shown = queue.shift ()[0];
            if (shown >= 0) {
                const bar = Math.floor ((shown % (STEPS * BARS)) / STEPS);
                const s = shown % STEPS;
                if (bar !== currentBar) { currentBar = bar; gridDirty = true; }
                if (s !== currentStep) {
                    if (currentStep >= 0) stepEls[currentStep].classList.remove ('now');
                    currentStep = s;
                    stepEls[s].classList.add ('now');
                    readout.textContent =
                        Math.round (inst.params.bpm) + ' BPM   BAR ' + (currentBar + 1) + '/' + BARS;
                }
            }
        }
        if (gridDirty) drawGrid ();
        requestAnimationFrame (frame);
    }
    requestAnimationFrame (frame);

    // ---------- transport wiring ----------
    async function togglePlay () {
        await inst.toggle ();
        playBtn.textContent = inst.playing ? 'STOP' : 'PLAY';
        playBtn.classList.toggle ('primary', !inst.playing);
        if (!inst.playing) {
            queue.length = 0;
            if (currentStep >= 0) stepEls[currentStep].classList.remove ('now');
            currentStep = -1;
        }
    }
    playBtn.addEventListener ('click', togglePlay);

    function onKey (e) {
        if (e.target && e.target.classList && e.target.classList.contains ('knob')) {
            if (e.key !== ' ' && e.key !== 'Escape') return;
        }
        if (e.code === 'Space') { e.preventDefault (); togglePlay (); }
        else if (e.key === 'Escape') { onExit (); }
    }
    window.addEventListener ('keydown', onKey);

    refreshReadouts ();
    drawGrid ();

    return {
        stop () {
            inst.stop ();
            playBtn.textContent = 'PLAY';
            playBtn.classList.add ('primary');
        },
        dispose () {
            window.removeEventListener ('keydown', onKey);
            inst.onStepScheduled = null;
            inst.onPatternSwap = null;
        }
    };
}
