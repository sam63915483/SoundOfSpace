// Lookahead scheduler — the "tale of two clocks" pattern.
//
// A setInterval on the main thread wakes up every 25ms and schedules any steps
// falling inside the next 100ms onto the AudioContext's sample clock. The main
// thread is allowed to be late and jittery; the audio never is, because every
// event already has an exact sample time by the time it matters.
//
// PORT NOTE (Unity, Option A): same shape — a coroutine ticking every ~25ms,
// scheduling onto AudioSettings.dspTime via AudioSource.PlayScheduled. Do NOT
// trigger notes from Update(); frame time is not a musical clock.

const LOOKAHEAD_MS = 25;
const SCHEDULE_AHEAD = 0.1;      // seconds

export class Clock {
    constructor (ctx, onStep) {
        this.ctx = ctx;
        this.onStep = onStep;
        this.bpm = 120;
        this.step = 0;
        this.nextTime = 0;
        this.timer = null;
    }

    // 16 steps per bar = sixteenth notes.
    get stepDuration () {
        return 60 / this.bpm / 4;
    }

    get running () {
        return this.timer !== null;
    }

    start () {
        if (this.timer !== null) return;
        this.step = 0;
        // Small offset so the first step isn't already in the past.
        this.nextTime = this.ctx.currentTime + 0.08;
        this.timer = setInterval (this._tick.bind (this), LOOKAHEAD_MS);
        this._tick ();
    }

    stop () {
        if (this.timer === null) return;
        clearInterval (this.timer);
        this.timer = null;
    }

    _tick () {
        const horizon = this.ctx.currentTime + SCHEDULE_AHEAD;
        // Bounded so a tab that was backgrounded for a minute doesn't try to
        // schedule 3000 steps the instant it wakes up.
        let guard = 256;
        while (this.nextTime < horizon && guard-- > 0) {
            this.onStep (this.step, this.nextTime, this.stepDuration);
            this.nextTime += this.stepDuration;
            this.step++;
        }
        if (guard <= 0) this.nextTime = this.ctx.currentTime + 0.08;
    }
}
