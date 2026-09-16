/* ───────────────────────────────────────────────────────────────────────────
   GALAXY MAP — prototype

   One continuous zoom, from a planet filling the screen out to seven suns and
   back down into any of them. No modes, no levels: the SAME map the whole way,
   with things appearing as they become big enough to draw. That is what makes
   "keep zooming out and you find Konkebular" work at all.

   Units: AU everywhere. Systems are placed in light years in the JSON and
   converted on load, so in-system distances (0.3 - 30 AU) and inter-system ones
   (~250,000 AU) live on one number line. Zoom spans about 1e7, so it is stored
   and stepped LOGARITHMICALLY - a linear zoom would crawl at one end and jump
   at the other.

   Port notes for the C# version are at the bottom of this file.
   ─────────────────────────────────────────────────────────────────────────── */

const DEG = Math.PI / 180;

const state = {
  data: null,
  LY: 63241,

  // Camera: world point at screen centre, and pixels-per-AU (log-stepped).
  cx: 0, cy: 0,
  ppAU: 300,          // set properly once the data loads

  // Simulated clock. Real orbits are years long; this runs them fast enough to
  // SEE without looking silly. 1 real second = `daysPerSecond` days.
  t: 0,
  daysPerSecond: 24,
  paused: false,

  hover: null,        // {kind:'planet'|'sun', sys, planet}
  selected: null,
  mode: 'tutorial',   // 'tutorial' | 'gameplay'
  travelled: null,
};

// Zoom limits, in pixels per AU.
const PP_MIN = 0.02;     // whole galaxy comfortably inside the pane
const PP_MAX = 4000;     // one planet filling the pane

// ── data ────────────────────────────────────────────────────────────────────

async function load() {
  const r = await fetch('galaxy.json');
  state.data = await r.json();
  state.LY = state.data.lightYearInAU;
  for (const s of state.data.systems) {
    s.x = s.pos[0] * state.LY;
    s.y = s.pos[1] * state.LY;
    for (const p of s.planets) p.sys = s;
  }
  // Start exactly where the tutorial starts: Earth, filling the screen.
  const sol = sysById('sol');
  const earth = sol.planets.find(p => p.here);
  const pe = planetPos(earth, 0);
  state.cx = pe.x; state.cy = pe.y;
  state.ppAU = 2200;
  frame();
}

const sysById = id => state.data.systems.find(s => s.id === id);

// Where a planet is at time t (days). Circular, in the plane — the game's
// planets ride exact circular rails too, so this is not a simplification.
function planetPos(p, tDays) {
  const a = (p.phase + tDays / p.periodDays) * Math.PI * 2;
  return { x: p.sys.x + Math.cos(a) * p.orbitAU, y: p.sys.y + Math.sin(a) * p.orbitAU };
}

// ── canvas ──────────────────────────────────────────────────────────────────

const cv = document.getElementById('map');
const ctx = cv.getContext('2d');
let W = 0, H = 0;

function resize() {
  const r = cv.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  W = r.width; H = r.height;
  cv.width = Math.round(W * dpr);
  cv.height = Math.round(H * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}
new ResizeObserver(resize).observe(cv);

const sx = wx => (wx - state.cx) * state.ppAU + W / 2;
const sy = wy => (wy - state.cy) * state.ppAU + H / 2;
const wxOf = px => (px - W / 2) / state.ppAU + state.cx;
const wyOf = py => (py - H / 2) / state.ppAU + state.cy;

// ── what is visible at this zoom ────────────────────────────────────────────
//
// One rule decides everything: how many PIXELS does this system's outermost
// orbit take up? Below a few pixels it is a star; above, it is a place.
function systemSpanPx(s) {
  const outer = s.planets.reduce((m, p) => Math.max(m, p.orbitAU), 1);
  return outer * 2 * state.ppAU;
}

function draw() {
  ctx.clearRect(0, 0, W, H);
  const t = state.t;

  // Starfield: fixed to the SCREEN, not the world — it is the backdrop, not a
  // layer to fly through, and parallaxing it at these scales looks like noise.
  drawStars();

  const systems = state.data.systems;

  // Pass 1: orbits + planets for any system big enough to resolve.
  for (const s of systems) {
    const span = systemSpanPx(s);
    if (span < 26) continue;
    const ssx = sx(s.x), ssy = sy(s.y);
    if (ssx < -span || ssx > W + span || ssy < -span || ssy > H + span) continue;

    const fade = clamp01((span - 26) / 80);
    ctx.globalAlpha = fade;
    for (const p of s.planets) {
      const r = p.orbitAU * state.ppAU;
      if (r < 3) continue;
      ctx.beginPath();
      ctx.arc(ssx, ssy, r, 0, Math.PI * 2);
      ctx.strokeStyle = p.target ? 'rgba(255,79,216,.34)' : 'rgba(29,74,63,.85)';
      ctx.lineWidth = 1;
      ctx.stroke();
    }
    ctx.globalAlpha = 1;
  }

  // Pass 2: the bodies themselves.
  for (const s of systems) {
    const span = systemSpanPx(s);
    const ssx = sx(s.x), ssy = sy(s.y);
    const onScreen = ssx > -200 && ssx < W + 200 && ssy > -200 && ssy < H + 200;

    // The sun. Always at least a dot, so a system can never vanish.
    const sunPx = Math.max(2.2, s.sunAU * state.ppAU);
    if (onScreen) {
      glow(ssx, ssy, sunPx * 3.2, s.sunColour, 0.16);
      disc(ssx, ssy, sunPx, s.sunColour);
    }

    // The system's NAME is what you read when it is too small to enter. It
    // fades out as the planets fade in, so there is always exactly one label
    // telling you where you are.
    const nameAlpha = 1 - clamp01((span - 26) / 140);
    if (onScreen && nameAlpha > 0.02) {
      label(ssx, ssy + sunPx + 13, s.name, `rgba(121,255,208,${0.35 + 0.55 * nameAlpha})`, 12);
    }

    if (span < 26) continue;

    const fade = clamp01((span - 26) / 80);
    for (const p of s.planets) {
      const pp = planetPos(p, t);
      const px = sx(pp.x), py = sy(pp.y);
      if (px < -60 || px > W + 60 || py < -60 || py > H + 60) continue;
      const rad = Math.max(1.8, p.sizeAU * state.ppAU);

      ctx.globalAlpha = fade;
      if (p.target) glow(px, py, rad * 4.5, '#ff4fd8', 0.30);
      if (p.here) glow(px, py, rad * 4.0, '#79ffd0', 0.22);
      disc(px, py, rad, p.colour);

      if (p === state.selected) ring(px, py, rad + 7, '#79ffd0', 2);
      else if (p.target) ring(px, py, rad + 7, '#ff4fd8', 1.5);
      if (p === state.hover) ring(px, py, rad + 11, '#ffffff', 1);

      // Planet names once they are worth reading.
      if (rad > 2.4 && state.ppAU * p.orbitAU > 46)
        label(px, py + rad + 12, p.name.toUpperCase(),
              p.target ? '#ff4fd8' : p.here ? '#79ffd0' : 'rgba(61,143,120,.95)', 11);
      ctx.globalAlpha = 1;
    }
  }

  drawScaleBar();
}

function drawStars() {
  // Deterministic: the same specks every frame, so nothing shimmers.
  ctx.fillStyle = 'rgba(255,255,255,.45)';
  let seed = 1337;
  const rnd = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
  for (let i = 0; i < 220; i++) {
    const x = rnd() * W, y = rnd() * H, r = rnd() * 0.9 + 0.2;
    ctx.globalAlpha = 0.12 + rnd() * 0.35;
    ctx.fillRect(x, y, r, r);
  }
  ctx.globalAlpha = 1;
}

function drawScaleBar() {
  // A bar whose length is a round number of AU or light years. Without it the
  // zoom is unreadable: every view looks the same when there is no ruler.
  const targetPx = 150;
  let au = targetPx / state.ppAU;
  const inLY = au > 4000;
  let v = inLY ? au / state.LY : au;
  const pow = Math.pow(10, Math.floor(Math.log10(v)));
  const nice = [1, 2, 5, 10].map(m => m * pow).reduce((a, b) =>
    Math.abs(b - v) < Math.abs(a - v) ? b : a);
  const px = (inLY ? nice * state.LY : nice) * state.ppAU;

  const x0 = 16, y0 = H - 20;
  ctx.strokeStyle = 'rgba(61,143,120,.9)'; ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(x0, y0 - 5); ctx.lineTo(x0, y0); ctx.lineTo(x0 + px, y0); ctx.lineTo(x0 + px, y0 - 5);
  ctx.stroke();
  label(x0 + px / 2, y0 - 9, `${fmt(nice)} ${inLY ? 'ly' : 'AU'}`, 'rgba(61,143,120,.95)', 11);
}

const fmt = n => n >= 1 ? String(Math.round(n * 100) / 100) : String(Math.round(n * 1000) / 1000);
const clamp01 = v => v < 0 ? 0 : v > 1 ? 1 : v;

function disc(x, y, r, c) {
  ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fillStyle = c; ctx.fill();
}
function ring(x, y, r, c, w) {
  ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2);
  ctx.strokeStyle = c; ctx.lineWidth = w; ctx.stroke();
}
function glow(x, y, r, c, a) {
  const g = ctx.createRadialGradient(x, y, 0, x, y, r);
  g.addColorStop(0, hexA(c, a)); g.addColorStop(1, hexA(c, 0));
  ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fillStyle = g; ctx.fill();
}
function hexA(c, a) {
  if (c.startsWith('#')) {
    const n = parseInt(c.slice(1), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
  }
  return c;
}
function label(x, y, text, colour, size) {
  ctx.font = `${size}px "Cascadia Mono",Consolas,ui-monospace,monospace`;
  ctx.textAlign = 'center'; ctx.textBaseline = 'top';
  ctx.fillStyle = colour;
  ctx.fillText(text, x, y);
}

// ── picking ─────────────────────────────────────────────────────────────────

function pick(px, py) {
  let best = null, bestD = 22 * 22;
  for (const s of state.data.systems) {
    const span = systemSpanPx(s);
    if (span >= 26) {
      for (const p of s.planets) {
        const pp = planetPos(p, state.t);
        const dx = sx(pp.x) - px, dy = sy(pp.y) - py;
        const d = dx * dx + dy * dy;
        if (d < bestD) { bestD = d; best = { kind: 'planet', sys: s, planet: p }; }
      }
    }
    const dx = sx(s.x) - px, dy = sy(s.y) - py;
    const d = dx * dx + dy * dy;
    if (d < bestD) { bestD = d; best = { kind: 'sun', sys: s, planet: null }; }
  }
  return best;
}

// ── travel rules ────────────────────────────────────────────────────────────
//
// The ONE place that decides what you may fly to, so the tutorial and the game
// differ by a string and nothing else.
function canTravel(p) {
  if (!p) return { ok: false, why: '' };
  if (p.here) return { ok: false, why: 'You are already here.' };
  if (state.mode === 'tutorial') {
    return p.target
      ? { ok: true, why: 'Charted colony site.' }
      : { ok: false, why: 'No landing clearance — the warp is plotted for Humble Abode.' };
  }
  // gameplay: anywhere inside Konkebular-7, nothing outside it.
  return p.sys.id === 'konkebular7'
    ? { ok: true, why: 'In range — reactor fuel, planet to planet.' }
    : { ok: false, why: 'Another system. The warp drive is dry; the reactor only moves you planet to planet.' };
}

// ── input ───────────────────────────────────────────────────────────────────

cv.addEventListener('wheel', e => {
  e.preventDefault();
  // Zoom about the CURSOR: the thing under the pointer stays put, which is what
  // makes "aim at Konkebular and zoom in" feel like one motion.
  const wx = wxOf(e.offsetX), wy = wyOf(e.offsetY);
  const k = Math.exp(-e.deltaY * 0.005);
  state.ppAU = Math.min(PP_MAX, Math.max(PP_MIN, state.ppAU * k));
  state.cx = wx - (e.offsetX - W / 2) / state.ppAU;
  state.cy = wy - (e.offsetY - H / 2) / state.ppAU;
}, { passive: false });

let drag = null;
cv.addEventListener('pointerdown', e => {
  drag = { x: e.offsetX, y: e.offsetY, cx: state.cx, cy: state.cy, moved: false };
  cv.setPointerCapture(e.pointerId);
});
cv.addEventListener('pointermove', e => {
  if (drag) {
    const dx = e.offsetX - drag.x, dy = e.offsetY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
    state.cx = drag.cx - dx / state.ppAU;
    state.cy = drag.cy - dy / state.ppAU;
  }
  state.hover = pick(e.offsetX, e.offsetY)?.planet || null;
});
cv.addEventListener('pointerup', e => {
  const wasDrag = drag && drag.moved;
  drag = null;
  if (wasDrag) return;
  const hit = pick(e.offsetX, e.offsetY);
  if (!hit) { state.selected = null; sync(); return; }
  if (hit.kind === 'planet') state.selected = hit.planet;
  else { state.selected = null; flyTo(hit.sys); }
  sync();
});
cv.addEventListener('dblclick', e => {
  const hit = pick(e.offsetX, e.offsetY);
  if (hit && hit.kind === 'sun') flyTo(hit.sys);
});

// Ease the camera onto a system and zoom until its planets resolve.
function flyTo(s) {
  const outer = s.planets.reduce((m, p) => Math.max(m, p.orbitAU), 1);
  anim = { cx: s.x, cy: s.y, ppAU: Math.min(PP_MAX, (Math.min(W, H) * 0.42) / outer), t0: performance.now() };
}
let anim = null;

// ── chrome ──────────────────────────────────────────────────────────────────

const $ = id => document.getElementById(id);

function sync() {
  const p = state.selected;
  const v = canTravel(p);
  $('selName').textContent = p ? p.name.toUpperCase() : '—';
  $('selSys').textContent = p ? p.sys.name : '';
  $('selBlurb').textContent = p ? (p.blurb || v.why || '') : 'Select a world.';
  const go = $('go');
  go.classList.toggle('on', v.ok);
  go.textContent = v.ok ? 'TRAVEL' : 'NO CLEARANCE';
  $('modeT').classList.toggle('on', state.mode === 'tutorial');
  $('modeG').classList.toggle('on', state.mode === 'gameplay');
  $('zoomTxt').textContent = zoomWord();
}

function zoomWord() {
  // Which system is nearest the centre, and can we resolve it?
  let near = null, nd = Infinity;
  for (const s of state.data.systems) {
    const d = (s.x - state.cx) ** 2 + (s.y - state.cy) ** 2;
    if (d < nd) { nd = d; near = s; }
  }
  if (!near) return '';
  const span = systemSpanPx(near);
  if (span < 26) return `LOCAL STARS · ${state.data.systems.length} SYSTEMS`;
  return `${near.name.toUpperCase()} SYSTEM`;
}

$('go').onclick = () => {
  const v = canTravel(state.selected);
  if (!v.ok) return;
  state.travelled = state.selected;
  $('overlay').classList.add('show');
  $('overlayTxt').textContent = `COURSE LOCKED — ${state.selected.name.toUpperCase()}`;
  setTimeout(() => $('overlay').classList.remove('show'), 2600);
};
$('modeT').onclick = () => { state.mode = 'tutorial'; sync(); };
$('modeG').onclick = () => { state.mode = 'gameplay'; sync(); };
$('pause').onclick = () => { state.paused = !state.paused; $('pause').classList.toggle('on', state.paused); };
$('speed').oninput = e => { state.daysPerSecond = Number(e.target.value); $('speedTxt').textContent = `${state.daysPerSecond} d/s`; };
$('home').onclick = () => {
  const sol = sysById('sol');
  const earth = sol.planets.find(p => p.here);
  const pe = planetPos(earth, state.t);
  anim = { cx: pe.x, cy: pe.y, ppAU: 2200, t0: performance.now() };
};
$('out').onclick = () => { anim = { cx: 0, cy: 0, ppAU: PP_MIN * 2.4, t0: performance.now() }; };

// ── frame ───────────────────────────────────────────────────────────────────

let last = performance.now();
function frame() {
  const now = performance.now();
  const dt = Math.min(0.1, (now - last) / 1000);
  last = now;
  if (!state.paused) state.t += dt * state.daysPerSecond;

  if (anim) {
    // Log-space interpolation on the zoom, linear on the centre. Lerping the
    // zoom linearly across four orders of magnitude spends the whole animation
    // at one end of the range.
    const k = clamp01((now - anim.t0) / 900);
    const e = k * k * (3 - 2 * k);
    state.cx += (anim.cx - state.cx) * e * 0.35;
    state.cy += (anim.cy - state.cy) * e * 0.35;
    state.ppAU = Math.exp(Math.log(state.ppAU) + (Math.log(anim.ppAU) - Math.log(state.ppAU)) * e * 0.35);
    if (k >= 1) anim = null;
  }

  draw();
  $('zoomTxt').textContent = zoomWord();
  requestAnimationFrame(frame);
}

load().then(sync);

/* ───────────────────────────────────────────────────────────────────────────
   PORTING NOTES (for ShuttleComputerUI)

   • The camera is (centre AU, pixelsPerAU). Zoom steps multiplicatively, never
     additively, and always about the cursor.
   • ONE visibility rule: systemSpanPx = outermost orbit * 2 * ppAU. Under ~26 px
     a system is a named dot; over it, orbits and planets fade in over ~80 px.
     The system NAME fades out as they fade in, so there is always exactly one
     label saying where you are.
   • Suns clamp to a minimum radius (2.2 px) so a system can never disappear.
   • Planet positions are cos/sin of (phase + t/period) — the same circular rails
     the game's real planets ride, so this is not a simplification that will have
     to be undone.
   • canTravel() is the only place that knows the rules. Tutorial: the one
     target. Gameplay: anything inside Konkebular-7.
   ─────────────────────────────────────────────────────────────────────────── */
