// Rotary knob. Drag vertically, or wheel, or arrow keys. Shift = fine control,
// double-click = back to default.
//
// Deliberately continuous (0-10 float) even though seeding quantizes to 0.5 —
// the timbre parameters read the raw value, so a hair of movement still does
// something audible even when the pattern hasn't changed.

const SWEEP = 270;          // degrees of travel
const START = -135;
const R = 19;
const CX = 24, CY = 24;

function polar (cx, cy, r, deg) {
    const a = (deg - 90) * Math.PI / 180;
    return [cx + r * Math.cos (a), cy + r * Math.sin (a)];
}

function arcPath (cx, cy, r, startDeg, endDeg) {
    const [x1, y1] = polar (cx, cy, r, startDeg);
    const [x2, y2] = polar (cx, cy, r, endDeg);
    const large = Math.abs (endDeg - startDeg) > 180 ? 1 : 0;
    return 'M ' + x1.toFixed (2) + ' ' + y1.toFixed (2) +
           ' A ' + r + ' ' + r + ' 0 ' + large + ' 1 ' + x2.toFixed (2) + ' ' + y2.toFixed (2);
}

const SVG_NS = 'http://www.w3.org/2000/svg';
function svgEl (tag, attrs) {
    const el = document.createElementNS (SVG_NS, tag);
    for (const k in attrs) el.setAttribute (k, attrs[k]);
    return el;
}

export function createKnob (def, initial, onChange) {
    const root = document.createElement ('div');
    root.className = 'knob';
    root.tabIndex = 0;
    root.setAttribute ('role', 'slider');
    root.setAttribute ('aria-label', def.label);
    root.setAttribute ('aria-valuemin', '0');
    root.setAttribute ('aria-valuemax', '10');

    const name = document.createElement ('div');
    name.className = 'k-name';
    name.textContent = def.label;

    const svg = svgEl ('svg', { width: 48, height: 48, viewBox: '0 0 48 48' });
    const track = svgEl ('path', {
        d: arcPath (CX, CY, R, START, START + SWEEP),
        fill: 'none', stroke: 'var(--grid)', 'stroke-width': 4, 'stroke-linecap': 'round'
    });
    const fill = svgEl ('path', {
        fill: 'none', stroke: 'var(--ink)', 'stroke-width': 4, 'stroke-linecap': 'round'
    });
    const pointer = svgEl ('line', {
        stroke: 'var(--accent)', 'stroke-width': 2, 'stroke-linecap': 'round'
    });
    const hub = svgEl ('circle', {
        cx: CX, cy: CY, r: 11, fill: 'var(--panel-hi)', stroke: 'var(--grid)', 'stroke-width': 1
    });
    svg.appendChild (track); svg.appendChild (fill); svg.appendChild (hub); svg.appendChild (pointer);

    const val = document.createElement ('div');
    val.className = 'k-val';

    const flav = document.createElement ('div');
    flav.className = 'k-flav';
    flav.textContent = def.flavor;

    root.appendChild (name);
    root.appendChild (svg);
    root.appendChild (val);
    root.appendChild (flav);

    let value = initial;

    function render () {
        const end = START + SWEEP * (value / 10);
        // A zero-length arc renders nothing in some browsers; keep a sliver.
        fill.setAttribute ('d', arcPath (CX, CY, R, START, Math.max (end, START + 0.01)));
        const [px, py] = polar (CX, CY, 10, end);
        const [ix, iy] = polar (CX, CY, 4, end);
        pointer.setAttribute ('x1', ix.toFixed (2)); pointer.setAttribute ('y1', iy.toFixed (2));
        pointer.setAttribute ('x2', px.toFixed (2)); pointer.setAttribute ('y2', py.toFixed (2));
        val.textContent = value.toFixed (1);
        root.setAttribute ('aria-valuenow', value.toFixed (1));
    }

    function set (v, notify) {
        const clamped = Math.min (10, Math.max (0, v));
        // Round to 0.05 so the readout doesn't jitter with float noise.
        const next = Math.round (clamped * 20) / 20;
        if (next === value) return;
        value = next;
        render ();
        if (notify !== false) onChange (def.key, value);
    }

    // --- drag ---
    let dragging = false, lastY = 0;

    root.addEventListener ('pointerdown', (e) => {
        dragging = true;
        lastY = e.clientY;
        root.classList.add ('live');
        root.setPointerCapture (e.pointerId);
        e.preventDefault ();
    });
    root.addEventListener ('pointermove', (e) => {
        if (!dragging) return;
        const dy = lastY - e.clientY;
        lastY = e.clientY;
        // ~200px for the full sweep; Shift drops to a fifth of that.
        set (value + dy * (e.shiftKey ? 0.01 : 0.05));
    });
    const end = (e) => {
        if (!dragging) return;
        dragging = false;
        root.classList.remove ('live');
        try { root.releasePointerCapture (e.pointerId); } catch (err) { /* already released */ }
    };
    root.addEventListener ('pointerup', end);
    root.addEventListener ('pointercancel', end);

    root.addEventListener ('wheel', (e) => {
        e.preventDefault ();
        set (value + (e.deltaY < 0 ? 1 : -1) * (e.shiftKey ? 0.05 : 0.25));
    }, { passive: false });

    root.addEventListener ('dblclick', () => set (initial));

    root.addEventListener ('keydown', (e) => {
        const step = e.shiftKey ? 0.1 : 0.5;
        if (e.key === 'ArrowUp' || e.key === 'ArrowRight') { set (value + step); e.preventDefault (); }
        else if (e.key === 'ArrowDown' || e.key === 'ArrowLeft') { set (value - step); e.preventDefault (); }
        else if (e.key === 'Home') { set (0); e.preventDefault (); }
        else if (e.key === 'End') { set (10); e.preventDefault (); }
    });

    render ();

    return {
        el: root,
        get value () { return value; },
        set (v) { set (v, false); }
    };
}
