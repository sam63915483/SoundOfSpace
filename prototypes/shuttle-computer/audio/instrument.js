// The instrument: owns dial state, drives the engine, feeds the backend.
//
// The UI talks only to this. Step 2 (Unity UGUI) reimplements the UI against
// this same surface; Step 3 swaps fx.js/voices.js/clock.js underneath it. The
// engine/ imports below are the parts that must not change in either step.

import { seedFromDials } from '../engine/prng.js';
import { computeParams, DEFAULT_DIALS, needsRegen } from '../engine/params.js';
import { generatePatterns, stepAt, STEPS, TOTAL_STEPS } from '../engine/patterns.js';
import { degreeToFreq, VOICE_OCTAVE } from '../engine/scales.js';
import { classify } from '../engine/classifier.js';
import { createRack } from './fx.js';
import { Clock } from './clock.js';
import { triggerKick, triggerSnare, triggerHat, triggerBass, triggerLead } from './voices.js';

export const MODULES = [
    { name: 'THUMPER', desc: 'drums',  locked: false },
    { name: 'GLOWORM', desc: 'bass',   locked: false },
    { name: 'SIREN',   desc: 'lead',   locked: false },
    { name: 'CAVE',    desc: 'space',  locked: false },
    { name: '??????',  desc: 'locked', locked: true },
    { name: '??????',  desc: 'locked', locked: true }
];

export class Instrument {
    constructor () {
        this.dials = Object.assign ({}, DEFAULT_DIALS);
        this.params = computeParams (this.dials);
        this.patterns = generatePatterns (seedFromDials (this.dials), this.params);
        this.pending = null;

        this.enabled = { THUMPER: true, GLOWORM: true, SIREN: true, CAVE: true };
        this.masterVolume = 0.5;

        this.ctx = null;
        this.rack = null;
        this.clock = null;

        // UI hooks.
        this.onStepScheduled = null;   // (step, time)
        this.onPatternSwap = null;     // ()
    }

    // Must be called from a user gesture — browsers refuse to start audio
    // otherwise, and a silently-suspended context looks exactly like a bug.
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
        for (const k in this.enabled) this.rack.setModuleEnabled (k, this.enabled[k], this.params);

        this.clock = new Clock (this.ctx, this._schedule.bind (this));
        this.clock.bpm = this.params.bpm;
    }

    get playing () {
        return this.clock != null && this.clock.running;
    }

    async play () {
        await this.init ();
        if (this.pending) { this.patterns = this.pending; this.pending = null; }
        this.clock.start ();
    }

    stop () {
        if (this.clock) this.clock.stop ();
    }

    async toggle () {
        if (this.playing) this.stop (); else await this.play ();
    }

    // --- dials ---

    setDial (key, value) {
        const next = Object.assign ({}, this.dials);
        next[key] = value;
        this.setDials (next);
    }

    setDials (next) {
        const prev = this.dials;
        this.dials = Object.assign ({}, next);
        this.params = computeParams (this.dials);

        // BPM rides live — tempo should feel attached to your hand.
        if (this.clock) this.clock.bpm = this.params.bpm;
        // Timbre/FX ramp live; never hard-jumped.
        if (this.rack) this.rack.apply (this.params);

        if (needsRegen (prev, this.dials)) {
            const fresh = generatePatterns (seedFromDials (this.dials), this.params);
            // While playing, hold it until the bar turns over — swapping
            // mid-bar is audible as a stumble. The global step counter keeps
            // running, so the phrase position is preserved across the swap.
            if (this.playing) this.pending = fresh;
            else this.patterns = fresh;
        }
    }

    get genre () {
        return classify (this.dials);
    }

    get seed () {
        return seedFromDials (this.dials);
    }

    // --- rack ---

    setModuleEnabled (name, on) {
        this.enabled[name] = on;
        if (this.rack) this.rack.setModuleEnabled (name, on, this.params);
    }

    setMasterVolume (v) {
        this.masterVolume = v;
        if (this.rack) this.rack.setMasterVolume (v);
    }

    // --- scheduling ---

    _schedule (step, time, stepDur) {
        // Pattern swaps land on bar boundaries only.
        if (step % STEPS === 0 && this.pending) {
            this.patterns = this.pending;
            this.pending = null;
            if (this.onPatternSwap) this.onPatternSwap ();
        }

        const p = this.params;
        const ctxNow = this.ctx.currentTime;
        const at = (st) => Math.max (time + (st.nudge || 0), ctxNow + 0.005);

        if (this.enabled.THUMPER) {
            const k = stepAt (this.patterns, 'kick', step);
            if (k) triggerKick (this.rack, p, at (k), k.vel);
            const s = stepAt (this.patterns, 'snare', step);
            if (s) triggerSnare (this.rack, p, at (s), s.vel);
            const h = stepAt (this.patterns, 'hat', step);
            if (h) triggerHat (this.rack, p, at (h), h.vel, h.open);
        }
        if (this.enabled.GLOWORM) {
            const b = stepAt (this.patterns, 'bass', step);
            if (b) triggerBass (this.rack, p, at (b), b.vel,
                degreeToFreq (b.degree, p.scaleIdx, VOICE_OCTAVE.bass),
                Math.max (0.05, b.dur * stepDur * 0.95));
        }
        if (this.enabled.SIREN) {
            const l = stepAt (this.patterns, 'lead', step);
            if (l) triggerLead (this.rack, p, at (l), l.vel,
                degreeToFreq (l.degree, p.scaleIdx, VOICE_OCTAVE.lead),
                Math.max (0.05, l.dur * stepDur * 0.9));
        }

        if (this.onStepScheduled) this.onStepScheduled (step, time);
    }

    // Read-only snapshot for the UI's step grid.
    gridFor (voice) {
        const rows = [];
        for (let i = 0; i < TOTAL_STEPS; i++) rows.push (stepAt (this.patterns, voice, i) != null);
        return rows;
    }
}
