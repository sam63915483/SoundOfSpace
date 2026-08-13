// Master chain, drive/crush shapers, and the CAVE send bus.
//
// PORT NOTE (Unity, Option A): this whole file is the part that gets rewritten.
//   drive/crush shaper -> AudioDistortionFilter (+ a custom crush if needed)
//   CAVE                -> AudioEchoFilter / AudioReverbFilter on a send AudioSource
//   master + limiter    -> an AudioMixerGroup
// Nothing here decides *what* is played — only how it sounds coming out.

import { mulberry32 } from '../engine/prng.js';

const CURVE_LEN = 1024;

// Fixed so the Unity port can generate a byte-identical noise buffer.
const NOISE_SEED = 0x5eed0135;

// Drive + amplitude quantization in one transfer curve. Quantizing the curve
// itself IS bit-crushing (amplitude, not sample rate) — no worklet needed.
function makeShaperCurve (drive, levels) {
    const curve = new Float32Array (CURVE_LEN);
    const k = 1 + drive * 30;
    const norm = Math.tanh (k);
    for (let i = 0; i < CURVE_LEN; i++) {
        const x = (i / (CURVE_LEN - 1)) * 2 - 1;
        let y = Math.tanh (k * x) / norm;
        if (levels < 64) y = Math.round (y * levels) / levels;
        curve[i] = y;
    }
    return curve;
}

// One shared noise buffer for snare + hat. Filled from a FIXED seed rather than
// Math.random so the Unity port can generate a byte-identical buffer — two
// builds of the same cassette should not differ even in their noise.
function makeNoiseBuffer (ctx) {
    const len = Math.floor (ctx.sampleRate * 2);
    const buf = ctx.createBuffer (1, len, ctx.sampleRate);
    const data = buf.getChannelData (0);
    const rnd = mulberry32 (NOISE_SEED);
    for (let i = 0; i < len; i++) data[i] = rnd () * 2 - 1;
    return buf;
}

export function createRack (ctx) {
    // --- master ---
    const master = ctx.createGain ();
    master.gain.value = 0.5;              // same courtesy as GameAudioBus: never boot at 1.0

    // Safety net. The dials can stack drive + resonance + feedback into
    // something genuinely painful; this keeps it merely loud.
    const limiter = ctx.createDynamicsCompressor ();
    limiter.threshold.value = -6;
    limiter.knee.value = 6;
    limiter.ratio.value = 12;
    limiter.attack.value = 0.003;
    limiter.release.value = 0.12;

    master.connect (limiter);
    limiter.connect (ctx.destination);

    // --- shapers ---
    const toneShaper = ctx.createWaveShaper ();   // GLOWORM + SIREN
    const drumShaper = ctx.createWaveShaper ();   // THUMPER, driven half as hard
    toneShaper.oversample = '2x';
    drumShaper.oversample = '2x';

    // --- CAVE: two cross-fed delay lines with damping ---
    const caveIn = ctx.createGain ();
    const dA = ctx.createDelay (1.0); dA.delayTime.value = 0.19;
    const dB = ctx.createDelay (1.0); dB.delayTime.value = 0.31;
    const dampA = ctx.createBiquadFilter (); dampA.type = 'lowpass'; dampA.frequency.value = 2800;
    const dampB = ctx.createBiquadFilter (); dampB.type = 'lowpass'; dampB.frequency.value = 2400;
    const fbA = ctx.createGain (); fbA.gain.value = 0.4;
    const fbB = ctx.createGain (); fbB.gain.value = 0.4;
    const caveWet = ctx.createGain (); caveWet.gain.value = 0.4;

    caveIn.connect (dA);
    caveIn.connect (dB);
    // Cross-feedback (ping-pong). Loop gain is fbA*fbB, so even at the 0.85 cap
    // the round trip is 0.72 — it decays instead of running away.
    dA.connect (dampA); dampA.connect (fbA); fbA.connect (dB);
    dB.connect (dampB); dampB.connect (fbB); fbB.connect (dA);
    dA.connect (caveWet);
    dB.connect (caveWet);
    caveWet.connect (master);

    // --- rack module gains (the on/off toggles mute here) ---
    const thumper = ctx.createGain ();
    const gloworm = ctx.createGain ();
    const siren   = ctx.createGain ();

    thumper.connect (drumShaper);
    gloworm.connect (toneShaper);
    siren.connect (toneShaper);
    drumShaper.connect (master);
    toneShaper.connect (master);

    // Sends are taken POST-shaper so the CAVE hears the same crunch you do.
    const sendDrums = ctx.createGain (); sendDrums.gain.value = 0;
    const sendTone  = ctx.createGain (); sendTone.gain.value = 0;
    drumShaper.connect (sendDrums); sendDrums.connect (caveIn);
    toneShaper.connect (sendTone);  sendTone.connect (caveIn);

    // --- shared filter LFO (GOO wobble) ---
    // One oscillator drives every voice's filter detune. Voices connect their
    // filter.detune to lfoDepth as they're created.
    const lfo = ctx.createOscillator ();
    lfo.type = 'sine';
    lfo.frequency.value = 1;
    const lfoDepth = ctx.createGain ();
    lfoDepth.gain.value = 0;               // cents
    lfo.connect (lfoDepth);
    lfo.start ();

    const rack = {
        ctx, master, limiter, thumper, gloworm, siren,
        caveWet, caveIn, toneShaper, drumShaper,
        lfo, lfoDepth,
        noise: makeNoiseBuffer (ctx),
        _crunchBucket: -1,

        // Live parameter update. Called on every dial move — must never hard-jump
        // a running node or it clicks.
        apply (p, when) {
            const t = when == null ? ctx.currentTime : when;
            const R = 0.02;                                // setTargetAtTime constant

            // Rebuild shaper curves only when crunch actually moved a bucket —
            // allocating a 1024-float curve per mousemove would be silly.
            const bucket = Math.round (p.drive * 40);
            if (bucket !== this._crunchBucket) {
                this._crunchBucket = bucket;
                toneShaper.curve = makeShaperCurve (p.drive, p.crushLevels);
                drumShaper.curve = makeShaperCurve (p.drive * 0.5, Math.min (64, p.crushLevels * 2));
            }

            sendDrums.gain.setTargetAtTime (p.caveSend * 0.3, t, R);
            sendTone.gain.setTargetAtTime (p.caveSend, t, R);
            fbA.gain.setTargetAtTime (p.caveFeedback, t, R);
            fbB.gain.setTargetAtTime (p.caveFeedback, t, R);
            caveWet.gain.setTargetAtTime (this.caveMuted ? 0 : p.caveMix, t, R);

            lfo.frequency.setTargetAtTime (p.lfoRate, t, R);
            lfoDepth.gain.setTargetAtTime (p.lfoDepthOct * 1200, t, R);
        },

        caveMuted: false,

        setMasterVolume (v) {
            master.gain.setTargetAtTime (v, ctx.currentTime, 0.02);
        },

        // Rack toggles. Ramped, and the pattern keeps running underneath — so
        // unmuting drops you back in time instead of restarting the phrase.
        setModuleEnabled (name, on, p) {
            const t = ctx.currentTime, R = 0.03;
            if (name === 'THUMPER') thumper.gain.setTargetAtTime (on ? 1 : 0, t, R);
            else if (name === 'GLOWORM') gloworm.gain.setTargetAtTime (on ? 1 : 0, t, R);
            else if (name === 'SIREN') siren.gain.setTargetAtTime (on ? 1 : 0, t, R);
            else if (name === 'CAVE') {
                this.caveMuted = !on;
                caveWet.gain.setTargetAtTime (on ? (p ? p.caveMix : 0.4) : 0, t, R);
            }
        }
    };

    return rack;
}
