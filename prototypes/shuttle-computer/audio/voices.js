// The four instruments. Every voice is synthesized — no samples, no files.
//
// PORT NOTE (Unity, Option A): each trigger function becomes a procedurally
// generated AudioClip + an AudioSource.PlayScheduled call, with the filter
// stage as an AudioLowPassFilter component. The envelope shapes and frequency
// numbers below are the spec for that port.

// Attack/release floors. exponentialRampToValueAtTime cannot touch zero, so
// envelopes ramp to this instead.
const EPS = 0.0001;

// Continuous sine -> saw -> square. Web Audio oscillators can't morph, so we
// crossfade a pair. 0 = sine, 0.5 = saw, 1 = square.
function morphPair (morph) {
    if (morph < 0.5) return ['sine', 'sawtooth', morph * 2];
    return ['sawtooth', 'square', (morph - 0.5) * 2];
}

// High resonance is a big gain peak; back the voice off so GOO doesn't just
// mean "louder". Without this the limiter ducks the whole mix every squelch.
function resComp (q) {
    return 1 / (1 + q * 0.06);
}

function ampEnv (param, t, peak, len, attack) {
    const a = Math.min (attack, len * 0.5);
    param.setValueAtTime (EPS, t);
    param.exponentialRampToValueAtTime (Math.max (peak, EPS), t + a);
    param.setValueAtTime (Math.max (peak, EPS), t + Math.max (a, len * 0.7));
    param.exponentialRampToValueAtTime (EPS, t + len);
}

function noiseSource (rack, t, dur) {
    const src = rack.ctx.createBufferSource ();
    src.buffer = rack.noise;
    // Start at a varying offset so consecutive hits aren't phase-identical —
    // derived from the schedule time, not from randomness.
    src.loop = true;
    src.loopStart = 0;
    src.loopEnd = rack.noise.duration;
    src.start (t, (t * 7.3) % (rack.noise.duration - dur - 0.01));
    src.stop (t + dur + 0.02);
    return src;
}

// ------------------------------------------------------------- THUMPER ----

export function triggerKick (rack, p, t, vel) {
    const ctx = rack.ctx;
    const osc = ctx.createOscillator ();
    const g = ctx.createGain ();
    osc.type = 'sine';
    // The pitch drop IS the kick. 150 -> 45Hz in 80ms.
    osc.frequency.setValueAtTime (150, t);
    osc.frequency.exponentialRampToValueAtTime (45, t + 0.08);
    ampEnv (g.gain, t, vel, 0.25, 0.004);
    osc.connect (g); g.connect (rack.thumper);
    osc.start (t); osc.stop (t + 0.3);
}

export function triggerSnare (rack, p, t, vel) {
    const ctx = rack.ctx;
    const dur = 0.18;

    const src = noiseSource (rack, t, dur);
    const bp = ctx.createBiquadFilter ();
    bp.type = 'bandpass';
    bp.frequency.value = 1800;
    bp.Q.value = 0.9;
    const ng = ctx.createGain ();
    ampEnv (ng.gain, t, vel * 0.8, dur, 0.002);
    src.connect (bp); bp.connect (ng); ng.connect (rack.thumper);

    // A little tuned body underneath, or it reads as a hiss rather than a hit.
    const body = ctx.createOscillator ();
    body.type = 'triangle';
    body.frequency.setValueAtTime (190, t);
    body.frequency.exponentialRampToValueAtTime (120, t + 0.09);
    const bg = ctx.createGain ();
    ampEnv (bg.gain, t, vel * 0.35, 0.1, 0.002);
    body.connect (bg); bg.connect (rack.thumper);
    body.start (t); body.stop (t + 0.14);
}

export function triggerHat (rack, p, t, vel, open) {
    const ctx = rack.ctx;
    const dur = open ? 0.14 : 0.04;
    const src = noiseSource (rack, t, dur);
    const hp = ctx.createBiquadFilter ();
    hp.type = 'highpass';
    hp.frequency.value = 7000;
    const g = ctx.createGain ();
    ampEnv (g.gain, t, vel * 0.5, dur, 0.001);
    src.connect (hp); hp.connect (g); g.connect (rack.thumper);
}

// ----------------------------------------------- GLOWORM / SIREN (tonal) --

function tonalVoice (rack, p, t, vel, freq, len, opts) {
    const ctx = rack.ctx;
    const [typeA, typeB, mix] = morphPair (p.oscMorph);

    const oscA = ctx.createOscillator (); oscA.type = typeA;
    const oscB = ctx.createOscillator (); oscB.type = typeB;
    oscA.frequency.value = freq;
    oscB.frequency.value = freq;
    // WARP high = detuned = alien. The two oscs beat against each other.
    oscA.detune.value = -p.detuneCents * 0.5;
    oscB.detune.value =  p.detuneCents * 0.5;

    const gA = ctx.createGain (); gA.gain.value = 1 - mix;
    const gB = ctx.createGain (); gB.gain.value = mix;

    const filter = ctx.createBiquadFilter ();
    filter.type = 'lowpass';
    filter.frequency.value = Math.min (p.filterBase * opts.filterScale, 16000);
    filter.Q.value = p.filterQ;
    // GOO's wobble: one shared LFO drives every voice's cutoff, in cents.
    rack.lfoDepth.connect (filter.detune);

    const vca = ctx.createGain ();
    ampEnv (vca.gain, t, vel * opts.level * resComp (p.filterQ), len,
            opts.attack === undefined ? 0.012 : opts.attack);

    oscA.connect (gA); gA.connect (filter);
    oscB.connect (gB); gB.connect (filter);
    filter.connect (vca);
    vca.connect (opts.out);

    oscA.start (t); oscB.start (t);
    oscA.stop (t + len + 0.05);
    oscB.stop (t + len + 0.05);

    // Disconnect from the shared LFO once the note is done, or every note ever
    // played stays wired to it and the graph grows without bound.
    oscB.onended = function () {
        try { rack.lfoDepth.disconnect (filter.detune); } catch (e) { /* already gone */ }
    };
}

export function triggerBass (rack, p, t, vel, freq, len) {
    tonalVoice (rack, p, t, vel, freq, len, {
        out: rack.gloworm, filterScale: 1.0, level: 0.85
    });
}

export function triggerLead (rack, p, t, vel, freq, len) {
    tonalVoice (rack, p, t, vel, freq, len, {
        out: rack.siren, filterScale: 2.2, level: 0.5
    });
}

// ------------------------------------------------------- MOSS / SPINDLE ----

// MOSS: the chord, held. Slow attack so it swells in under everything rather
// than announcing itself, and a darker filter so it never competes with the
// lead for the top end.
export function triggerMoss (rack, p, t, vel, freqs, len) {
    for (let i = 0; i < freqs.length; i++) {
        tonalVoice (rack, p, t, vel, freqs[i], len, {
            out: rack.moss,
            filterScale: 0.8,
            level: 0.22,          // three notes at once; each one has to be quiet
            attack: 0.12
        });
    }
}

// SPINDLE: plucked, short, bright. Always a chord tone, so it can't be wrong.
export function triggerSpindle (rack, p, t, vel, freq, len) {
    tonalVoice (rack, p, t, vel, freq, Math.min (len, 0.22), {
        out: rack.spindle, filterScale: 3.0, level: 0.34, attack: 0.004
    });
}

// ---------------------------------------------------------- TRANSITIONS ----
// Section-boundary one-shots. Routed into the drum shaper AFTER the module
// gains, on purpose: they belong to the SONG, not to any rack module, so a
// muted THUMPER can't silence the transition that announces it.

// RISER: a band-passed noise sweep climbing through the last bar of a
// section. LINEAR gain build on purpose — an exponential ramp from silence
// stays inaudible for most of its length and only pops at the very end,
// which reads as nothing at all. Peaks just before the downbeat, then cuts —
// the cut IS the hand-off.
export function triggerRiser (rack, t, dur, intensity) {
    const ctx = rack.ctx;
    const src = noiseSource (rack, t, dur);

    const bp = ctx.createBiquadFilter ();
    bp.type = 'bandpass';
    bp.Q.value = 1.4;
    bp.frequency.setValueAtTime (200, t);
    bp.frequency.exponentialRampToValueAtTime (7000, t + dur);

    const g = ctx.createGain ();
    g.gain.setValueAtTime (0.002, t);
    g.gain.linearRampToValueAtTime (Math.max (0.4 * intensity, EPS), t + dur * 0.95);
    g.gain.linearRampToValueAtTime (0, t + dur);

    src.connect (bp); bp.connect (g); g.connect (rack.drumShaper);
}

// IMPACT: the arrival. A darkening noise splash over a low sine thump, landing
// exactly on the new section's first step.
export function triggerImpact (rack, t, intensity) {
    const ctx = rack.ctx;
    const dur = 0.6;

    const src = noiseSource (rack, t, dur);
    const lp = ctx.createBiquadFilter ();
    lp.type = 'lowpass';
    lp.frequency.setValueAtTime (9000, t);
    lp.frequency.exponentialRampToValueAtTime (400, t + dur);
    const ng = ctx.createGain ();
    ampEnv (ng.gain, t, 0.6 * intensity, dur, 0.002);
    src.connect (lp); lp.connect (ng); ng.connect (rack.drumShaper);

    const osc = ctx.createOscillator ();
    osc.type = 'sine';
    osc.frequency.setValueAtTime (110, t);
    osc.frequency.exponentialRampToValueAtTime (36, t + 0.15);
    const og = ctx.createGain ();
    ampEnv (og.gain, t, 0.85 * intensity, 0.32, 0.003);
    osc.connect (og); og.connect (rack.drumShaper);
    osc.start (t); osc.stop (t + 0.36);
}
