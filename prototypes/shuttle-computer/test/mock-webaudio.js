// Strict mock of the Web Audio API, shared by the smoke tests.
//
// Deliberately stricter than a real browser: it throws on exponential ramps to
// zero, non-finite frequencies, out-of-range waveshaper curves, dangling
// connects, and buffer offsets past the end. Disconnect has real semantics
// (including the InvalidAccessError browsers raise) so graph-hygiene bugs
// surface here instead of as a tab that dies after five minutes.

export const stats = { checks: 0 };
export const problems = [];

export function bad (msg) { problems.push (msg); throw new Error (msg); }
export function num (v, what) {
    stats.checks++;
    if (typeof v !== 'number' || !isFinite (v)) bad (what + ' is not a finite number: ' + v);
    return v;
}


export class Param {
    constructor (label, initial) { this.label = label; this.value = initial === undefined ? 0 : initial; }
    _t (t) { num (t, this.label + ' time'); if (t < 0) bad (this.label + ' scheduled at negative time ' + t); }
    setValueAtTime (v, t) { num (v, this.label); this._t (t); this.value = v; return this; }
    linearRampToValueAtTime (v, t) { num (v, this.label); this._t (t); this.value = v; return this; }
    exponentialRampToValueAtTime (v, t) {
        num (v, this.label); this._t (t);
        // Real Web Audio throws on this. It is the single easiest way to make
        // an otherwise-correct synth produce total silence.
        if (v === 0) bad (this.label + ': exponentialRampToValueAtTime(0) throws in browsers');
        if (v < 0) bad (this.label + ': exponential ramp to negative ' + v);
        this.value = v; return this;
    }
    setTargetAtTime (v, t, c) { num (v, this.label); this._t (t); num (c, this.label + ' timeConstant'); this.value = v; return this; }
    cancelScheduledValues () { return this; }
}

class Node {
    constructor (ctx, kind) { this.ctx = ctx; this.kind = kind; this.out = []; }
    connect (dest) {
        if (!dest) bad (this.kind + '.connect(undefined) — a dangling connection means silence');
        this.out.push (dest);
        return dest instanceof Node ? dest : undefined;
    }
    // Real disconnect semantics, including the InvalidAccessError browsers
    // throw when the connection isn't there — so the caller's try/catch is
    // exercised rather than assumed.
    disconnect (dest) {
        if (dest === undefined) { this.out.length = 0; return this; }
        const i = this.out.indexOf (dest);
        if (i === -1) {
            const e = new Error ('InvalidAccessError: not connected');
            e.name = 'InvalidAccessError';
            throw e;
        }
        this.out.splice (i, 1);
        return this;
    }
}

class GainNode extends Node {
    constructor (ctx) { super (ctx, 'gain'); this.gain = new Param ('gain.gain', 1); }
}
class OscNode extends Node {
    constructor (ctx) {
        super (ctx, 'osc');
        this.frequency = new Param ('osc.frequency', 440);
        this.detune = new Param ('osc.detune', 0);
        this._type = 'sine'; this.started = null; this.stopped = null; this.onended = null;
    }
    set type (t) {
        if (['sine', 'sawtooth', 'square', 'triangle'].indexOf (t) === -1) bad ('bad osc type ' + t);
        this._type = t;
    }
    get type () { return this._type; }
    // start()/stop() with no argument are legal and mean "now" (t=0).
    start (t) { if (t === undefined) t = 0; num (t, 'osc.start'); this.started = t; }
    stop (t) {
        if (t === undefined) t = 0;
        num (t, 'osc.stop');
        if (this.started === null) bad ('osc stopped without being started');
        if (t < this.started) bad ('osc.stop before start');
        this.stopped = t;
        this.ctx._pendingEnded.push (this);
    }
}
class BiquadNode extends Node {
    constructor (ctx) {
        super (ctx, 'biquad');
        this.frequency = new Param ('biquad.frequency', 350);
        this.Q = new Param ('biquad.Q', 1);
        this.detune = new Param ('biquad.detune', 0);
        this.gain = new Param ('biquad.gain', 0);
        this._type = 'lowpass';
    }
    set type (t) {
        if (['lowpass', 'highpass', 'bandpass', 'notch', 'peaking'].indexOf (t) === -1) bad ('bad filter type ' + t);
        this._type = t;
    }
    get type () { return this._type; }
}
class DelayNode extends Node {
    constructor (ctx, max) {
        super (ctx, 'delay');
        this.maxDelay = max;
        this.delayTime = new Param ('delay.delayTime', 0);
    }
}
class ShaperNode extends Node {
    constructor (ctx) { super (ctx, 'shaper'); this._curve = null; this.oversample = 'none'; }
    set curve (c) {
        if (!(c instanceof Float32Array)) bad ('waveshaper curve must be a Float32Array');
        if (c.length < 2) bad ('waveshaper curve too short');
        for (let i = 0; i < c.length; i++) {
            if (!isFinite (c[i])) bad ('waveshaper curve has non-finite value at ' + i);
            if (c[i] < -1.001 || c[i] > 1.001) bad ('waveshaper curve out of [-1,1] at ' + i + ': ' + c[i]);
        }
        stats.checks++;
        this._curve = c;
    }
    get curve () { return this._curve; }
}
class CompressorNode extends Node {
    constructor (ctx) {
        super (ctx, 'compressor');
        this.threshold = new Param ('comp.threshold', -24);
        this.knee = new Param ('comp.knee', 30);
        this.ratio = new Param ('comp.ratio', 12);
        this.attack = new Param ('comp.attack', 0.003);
        this.release = new Param ('comp.release', 0.25);
    }
}
class BufferSourceNode extends Node {
    constructor (ctx) {
        super (ctx, 'buffersource');
        this.buffer = null; this.loop = false; this.loopStart = 0; this.loopEnd = 0;
        this.started = null;
    }
    start (t, offset) {
        num (t, 'bufferSource.start');
        if (offset !== undefined) {
            num (offset, 'bufferSource.start offset');
            if (offset < 0) bad ('buffer start offset negative: ' + offset);
            if (this.buffer && offset > this.buffer.duration) bad ('buffer offset past end: ' + offset);
        }
        this.started = t;
    }
    stop (t) { num (t, 'bufferSource.stop'); if (t < this.started) bad ('bufferSource.stop before start'); }
}
class FakeBuffer {
    constructor (ch, len, rate) { this.length = len; this.sampleRate = rate; this.duration = len / rate; this._d = new Float32Array (len); }
    getChannelData () { return this._d; }
}

export class FakeAudioContext {
    constructor () {
        this.currentTime = 0;
        this.sampleRate = 48000;
        this.state = 'running';
        this.destination = new Node (this, 'destination');
        this.nodeCount = 0;
        this._pendingEnded = [];
    }
    async resume () { this.state = 'running'; }
    _n (n) { this.nodeCount++; return n; }
    createGain () { return this._n (new GainNode (this)); }
    createOscillator () { return this._n (new OscNode (this)); }
    createBiquadFilter () { return this._n (new BiquadNode (this)); }
    createDelay (m) { return this._n (new DelayNode (this, m)); }
    createWaveShaper () { return this._n (new ShaperNode (this)); }
    createDynamicsCompressor () { return this._n (new CompressorNode (this)); }
    createBufferSource () { return this._n (new BufferSourceNode (this)); }
    createBuffer (ch, len, rate) { return new FakeBuffer (ch, len, rate); }
    // Fire the onended handlers the voices use to unwire the shared LFO.
    flushEnded () {
        const p = this._pendingEnded; this._pendingEnded = [];
        for (const o of p) if (o.onended) o.onended ();
    }
}

