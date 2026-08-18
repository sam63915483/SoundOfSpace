// The instrument: owns the track, drives the engine, feeds the backend.
//
// The UI talks only to this. Step 2 (Unity UGUI) reimplements the UI against
// this same surface; Step 3 swaps fx.js/voices.js/clock.js underneath it.
//
// ── This class is the choke point, on purpose ────────────────────────────
// EVERY change to the track goes through setTrack(). Nothing else may write
// track state — not the UI widgets, not the terminal, not a future preset
// loader. When full-length recording is built (spec §9.5) it becomes "log what
// passes through here, stamped with the step index" and nothing else changes.

import { computeParams } from '../engine/params.js';
import { generatePatterns, stepAt, chordTonesFor, STEPS, TOTAL_STEPS } from '../engine/patterns.js';
import { voiceFreq } from '../engine/scales.js';
import { classify } from '../engine/classifier.js';
import { totalSteps as songTotalSteps, sectionAtStep } from '../engine/song.js';
import * as TRACK from '../engine/track.js';
import * as PRESETS from '../engine/presets.js';
import { createRack } from './fx.js';
import { Clock } from './clock.js';
import { triggerKick, triggerSnare, triggerHat, triggerBass, triggerLead,
         triggerMoss, triggerSpindle } from './voices.js';

// Ordered the way you'd read a mix: rhythm, low end, harmony, melody, motion,
// space. CAVE has no pattern — its preset picks a space.
export const MODULES = [
    { name: 'THUMPER', desc: 'drums'  },
    { name: 'GLOWORM', desc: 'bass'   },
    { name: 'MOSS',    desc: 'chords' },
    { name: 'SIREN',   desc: 'lead'   },
    { name: 'SPINDLE', desc: 'arp'    },
    { name: 'CAVE',    desc: 'space'  }
];

export class Instrument {
    constructor () {
        this.track = TRACK.defaultTrack ();
        this.params = computeParams (this.track.dials, this.track.key);
        this.patterns = generatePatterns (this.track, this.params);
        this.pending = null;

        // WHICH PLUGINS THE COMPUTER OWNS — world state, not track state. Bought
        // from Tev, shared by both players in co-op, and only ever grows. A
        // module you do not own renders locked in the rack and cannot be
        // switched on. The browser prototype ships with everything installed so
        // the instrument stays fully playable as a dev tool; the game starts
        // with THUMPER + GLOWORM and sells the rest.
        //
        // ⚠️ THIS GATES EDITING ONLY, NEVER PLAYBACK. A track plays exactly as
        // it was written, whoever is listening — otherwise the same cassette
        // would sound different on two machines and the whole determinism
        // contract is dead. _schedule() reads the TRACK, never this.
        this.installed = {
            THUMPER: true, GLOWORM: true, MOSS: true,
            SIREN: true, SPINDLE: true, CAVE: true
        };
        this.masterVolume = 0.5;

        this.ctx = null;
        this.rack = null;
        this.clock = null;

        this.onStepScheduled = null;
        this.onPatternSwap = null;

        // ── Song mode (the arrangement layer) ────────────────────────────
        // When songMode is on, the scheduler ignores this.track/this.patterns
        // and walks the SONG: each section has its own compiled params +
        // patterns, applied to the rack as the playhead crosses into it. The
        // track/patterns pair above stays what it always was — the loop of the
        // section being EDITED — so loop playback and the whole edit surface
        // are untouched.
        this.song = null;           // { sections: [{ bars, track }] }
        this.songCompiled = null;   // per section: { params, patterns }
        this.songMode = false;
        this._songIdx = -1;         // which section's params the rack last got
        this._caveApplied = null;   // 'preset:variation' the rack last got
    }

    get dials () { return this.track.dials; }
    /// Which modules are PLAYING. Lives on the track now, so it prints onto a
    /// cassette and comes back when a project is loaded.
    get enabled () { return this.track.active; }
    get key () { return this.track.key; }
    get keyName () { return TRACK.keyName (this.track.key); }
    get trackId () { return TRACK.trackId (this.track); }
    get genre () { return classify (this.track.dials); }

    presetIndex (module) { return this.track.preset[module]; }
    variationIndex (module) { return this.track.variation[module]; }
    presetName (module) { return PRESETS.presetName (module, this.track.preset[module]); }

    async init () {
        if (this.ctx) {
            if (this.ctx.state === 'suspended') await this.ctx.resume ();
            return;
        }
        const AC = window.AudioContext || window.webkitAudioContext;
        this.ctx = new AC ({ latencyHint: 'interactive' });
        if (this.ctx.state === 'suspended') await this.ctx.resume ();

        this.rack = createRack (this.ctx);
        this.rack.setMasterVolume (this.masterVolume);
        this.rack.apply (this.params);
        this.rack.applyCavePreset (PRESETS.CAVE[this.track.preset.CAVE], this.track.variation.CAVE);
        this._caveApplied = this.track.preset.CAVE + ':' + this.track.variation.CAVE;
        for (const k in this.enabled) this.rack.setModuleEnabled (k, this.enabled[k], this.params);

        this.clock = new Clock (this.ctx, this._schedule.bind (this));
        this.clock.bpm = this.params.bpm;
    }

    get playing () { return this.clock != null && this.clock.running; }
    get playingSong () { return this.playing && this.songMode; }
    get playingLoop () { return this.playing && !this.songMode; }

    async play () {
        await this.init ();
        this.songMode = false;
        // Song playback may have left the rack on some other section's params.
        this._syncRackToTrack ();
        if (this.pending) { this.patterns = this.pending; this.pending = null; }
        this.clock.start ();
    }

    stop () { if (this.clock) this.clock.stop (); }

    async toggle () { if (this.playing) this.stop (); else await this.play (); }

    // --- song mode --------------------------------------------------------

    /// Hand the instrument the whole song. Cheap enough to call after every
    /// edit — a handful of sections regenerate in well under a millisecond —
    /// which keeps one code path instead of a per-section patch API.
    setSong (song) {
        this.song = song;
        this.songCompiled = song.sections.map (s => {
            const params = computeParams (s.track.dials, s.track.key);
            return { params, patterns: generatePatterns (s.track, params) };
        });
        this._songIdx = -1;                   // force a rack re-apply next step
    }

    async playSong () {
        await this.init ();
        this.songMode = true;
        this._songIdx = -1;
        this.clock.start ();
    }

    async toggleSong () { if (this.playing) this.stop (); else await this.playSong (); }

    /// Rack + clock back to the edited track's settings — used when loop
    /// playback (or plain editing) resumes after song playback moved them.
    _syncRackToTrack () {
        if (this.clock) this.clock.bpm = this.params.bpm;
        if (!this.rack) return;
        this.rack.apply (this.params);
        const caveKey = this.track.preset.CAVE + ':' + this.track.variation.CAVE;
        if (caveKey !== this._caveApplied) {
            this.rack.applyCavePreset (PRESETS.CAVE[this.track.preset.CAVE], this.track.variation.CAVE);
            this._caveApplied = caveKey;
        }
        for (const m of PRESETS.MODULE_NAMES)
            this.rack.setModuleEnabled (m, this.enabled[m], this.params);
    }

    // --- the choke point -------------------------------------------------

    setTrack (next) {
        const prev = this.track;
        this.track = next;
        this.params = computeParams (next.dials, next.key);

        if (this.clock) this.clock.bpm = this.params.bpm;
        if (this.rack) {
            this.rack.apply (this.params);
            if (prev.preset.CAVE !== next.preset.CAVE ||
                prev.variation.CAVE !== next.variation.CAVE) {
                this.rack.applyCavePreset (PRESETS.CAVE[next.preset.CAVE], next.variation.CAVE);
                this._caveApplied = next.preset.CAVE + ':' + next.variation.CAVE;
            }
            // LOADING a project changes the active set wholesale, not just when
            // a toggle is clicked — so the rack syncs here, at the choke point,
            // rather than in the toggle handler.
            for (const m of PRESETS.MODULE_NAMES)
                if (prev.active[m] !== next.active[m])
                    this.rack.setModuleEnabled (m, next.active[m], this.params);
        }

        if (TRACK.needsRegen (prev, next)) {
            const fresh = generatePatterns (next, this.params);
            // Swap on a bar line while playing — mid-bar is audible as a stumble.
            if (this.playing) this.pending = fresh;
            else this.patterns = fresh;
        }
    }

    setDial (key, value) {
        const t = TRACK.cloneTrack (this.track);
        t.dials[key] = value;
        this.setTrack (t);
    }

    setPreset (module, index) { this.setTrack (TRACK.setPreset (this.track, module, index)); }
    cyclePreset (module, delta) { this.setPreset (module, this.track.preset[module] + delta); }

    setVariation (module, index) { this.setTrack (TRACK.setVariation (this.track, module, index)); }
    cycleVariation (module, delta) { this.setVariation (module, this.track.variation[module] + delta); }

    // Key never regenerates anything — it is applied when a degree becomes a
    // frequency, so the same phrase just moves.
    setKey (key) { this.setTrack (TRACK.setKey (this.track, key)); }
    cycleKey (delta) { this.setKey (this.track.key + delta); }

    // --- rack ------------------------------------------------------------

    /// Muting is a track edit, so it goes through the choke point like every
    /// other one. Switching ON a module you do not own is refused rather than
    /// silently allowed — the lock is the carrot for Tev's shop.
    setModuleEnabled (name, on) {
        if (on && !this.installed[name]) return false;
        this.setTrack (TRACK.setActive (this.track, name, on));
        return true;
    }

    isInstalled (name) { return !!this.installed[name]; }

    /// Installing never touches the track, so it cannot change what an already
    /// printed cassette sounds like. Uninstalling is only for testing the
    /// locked state; it leaves an active module playing until it is toggled.
    setInstalled (name, on) {
        this.installed[name] = !!on;
    }

    setMasterVolume (v) {
        this.masterVolume = v;
        if (this.rack) this.rack.setMasterVolume (v);
    }

    // --- scheduling ------------------------------------------------------

    _schedule (step, time, stepDur) {
        if (this.songMode && this.song && this.songCompiled) {
            this._scheduleSong (step, time, stepDur);
            return;
        }

        if (step % STEPS === 0 && this.pending) {
            this.patterns = this.pending;
            this.pending = null;
            if (this.onPatternSwap) this.onPatternSwap ();
        }

        this._trigger (this.patterns, this.params, this.enabled, step, time, stepDur);
        if (this.onStepScheduled) this.onStepScheduled (step, time);
    }

    /// One step of song playback. The song loops top-to-tail; the position the
    /// UI hears about is SONG-relative (0..totalSteps-1), so the arranger
    /// playhead needs no knowledge of how long the clock has been running.
    _scheduleSong (step, time, stepDur) {
        const total = songTotalSteps (this.song);
        const pos = step % total;
        const loc = sectionAtStep (this.song, pos);

        if (loc.index !== this._songIdx) this._applySection (loc.index);

        const sec = this.songCompiled[loc.index];
        const track = this.song.sections[loc.index].track;
        // A section longer than the 4-bar phrase repeats it; stepAt wraps.
        this._trigger (sec.patterns, sec.params, track.active, loc.stepInSection, time, stepDur);
        if (this.onStepScheduled) this.onStepScheduled (pos, time);
    }

    /// Point the rack + clock at a section. Applied the moment the scheduler
    /// crosses the boundary, which runs ~100ms ahead of the speakers — close
    /// enough for the prototype; the Unity build can crossfade if it ever
    /// reads as a click.
    _applySection (i) {
        this._songIdx = i;
        const sec = this.songCompiled[i];
        const track = this.song.sections[i].track;
        if (this.clock) this.clock.bpm = sec.params.bpm;
        if (!this.rack) return;
        this.rack.apply (sec.params);
        const caveKey = track.preset.CAVE + ':' + track.variation.CAVE;
        if (caveKey !== this._caveApplied) {
            this.rack.applyCavePreset (PRESETS.CAVE[track.preset.CAVE], track.variation.CAVE);
            this._caveApplied = caveKey;
        }
        for (const m of PRESETS.MODULE_NAMES)
            this.rack.setModuleEnabled (m, track.active[m], sec.params);
    }

    _trigger (patterns, p, active, step, time, stepDur) {
        const ctxNow = this.ctx.currentTime;
        const at = (st) => Math.max (time + (st.nudge || 0), ctxNow + 0.005);
        const freq = (deg, voice) => voiceFreq (deg, p.scaleIdx, voice, p.key);

        if (active.THUMPER) {
            const k = stepAt (patterns, 'kick', step);
            if (k) triggerKick (this.rack, p, at (k), k.vel);
            const s = stepAt (patterns, 'snare', step);
            if (s) triggerSnare (this.rack, p, at (s), s.vel);
            const h = stepAt (patterns, 'hat', step);
            if (h) triggerHat (this.rack, p, at (h), h.vel, h.open);
        }
        if (active.GLOWORM) {
            const b = stepAt (patterns, 'bass', step);
            if (b) triggerBass (this.rack, p, at (b), b.vel, freq (b.degree, 'bass'),
                Math.max (0.05, b.dur * stepDur * 0.95));
        }
        if (active.SIREN) {
            const l = stepAt (patterns, 'lead', step);
            if (l) triggerLead (this.rack, p, at (l), l.vel, freq (l.degree, 'lead'),
                Math.max (0.05, l.dur * stepDur * 0.9));
        }
        if (active.MOSS) {
            const m = stepAt (patterns, 'moss', step);
            if (m) {
                const tones = chordTonesFor (m.degree);
                const freqs = new Array (tones.length);
                for (let i = 0; i < tones.length; i++) freqs[i] = freq (tones[i], 'moss');
                triggerMoss (this.rack, p, at (m), m.vel, freqs, m.dur * stepDur * 0.98);
            }
        }
        if (active.SPINDLE) {
            const a = stepAt (patterns, 'spindle', step);
            if (a) triggerSpindle (this.rack, p, at (a), a.vel, freq (a.degree, 'spindle'),
                Math.max (0.05, a.dur * stepDur * 0.9));
        }
    }

    gridFor (voice) {
        const rows = [];
        for (let i = 0; i < TOTAL_STEPS; i++) rows.push (stepAt (this.patterns, voice, i) != null);
        return rows;
    }
}
