/* ============================================================================
   NAV — live 2D solar map (prototype)

   A working stand-in for the NAV app's planet list. Every number on screen is
   the real one: orbit radii, angles and day lengths come out of
   Assets/1.6.7.7.7.unity (see extract_system.py), the fuel curve is
   ShuttleFuel.CostForMetres / RangeKm, and the in-range / wait maths is the
   same closed form as Assets/3 - Scripts/World/OrbitRange.cs.

   The system is clockwork — every planet rides an exact circular rail in one
   plane — so nothing here is simulated step by step. Positions are a cosine
   and a sine of the clock, and "when does that planet come into range" is one
   arccos and one divide. That exactness is the whole reason this map can
   promise a countdown and be right.
   ========================================================================= */

const $  = s => document.querySelector(s);
const TAU = Math.PI * 2;
const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
const lerp = (a, b, t) => a + (b - a) * t;

let S = null;                    // system.json
let planets = [];                // live list

// ── world clock ─────────────────────────────────────────────────────────────
let clock = 0;                   // seconds since the map opened (planet time)
let rate = 1;                    // prototype clock multiplier
let project = 0;                 // scrubber: seconds AHEAD of now

// ── ship state ──────────────────────────────────────────────────────────────
let here = null;                 // planet the shuttle is parked on
let fuel = 50;
let selected = null;
let hovered = null;
let showLabels = true;

// ── flight ──────────────────────────────────────────────────────────────────
let flight = null;               // {phase, t, target, cost, from:{x,y}}

/* ===========================================================================
   Orbital maths — the same closed form as OrbitRange.cs
   ========================================================================= */

const omega = p => (p.retrograde ? -1 : 1) * TAU / p.periodSec;
const angleAt = (p, t) => p.angleDeg * Math.PI / 180 + omega(p) * t;

function posAt(p, t) {
  const a = angleAt(p, t);
  return { x: Math.cos(a) * p.orbitRadius, y: Math.sin(a) * p.orbitRadius };
}

function distAt(a, b, t) {
  // Cosine rule on two circular rails — no square roots of vectors needed, but
  // this reads better and costs nothing at twelve planets.
  const pa = posAt(a, t), pb = posAt(b, t);
  return Math.hypot(pa.x - pb.x, pa.y - pb.y);
}

const wrapPi = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };

/** Seconds for a relative angle to travel `from` -> `to` at signed rate `rel`. */
function travelTime(from, to, rel) {
  const period = TAU / Math.abs(rel);
  let d = (to - from) / rel;
  while (d < -1e-9) d += period;
  return d;
}

/**
 * Is `b` inside `L` metres of `a` right now, and if not, when will it be?
 *  · Two planets on the same rail (the twins) hold a fixed separation forever,
 *    so the answer is always or never — never a wait.
 *  · Humble Abode runs retrograde, so its angle to a prograde planet closes at
 *    w1 + w2 rather than w1 - w2. Signs come from the data, never assumed.
 */
function window_(a, b, L, t) {
  if (!a || !b || a === b) return { status: 'here' };
  const ra = a.orbitRadius, rb = b.orbitRadius;
  const rel = omega(b) - omega(a);

  if (Math.abs(rel) < 1e-9) {                    // co-orbital: fixed separation
    const d = distAt(a, b, t);
    return d <= L ? { status: 'always', d } : { status: 'never', d, min: d, max: d };
  }

  const minD = Math.abs(ra - rb), maxD = ra + rb;
  if (L >= maxD) return { status: 'always', min: minD, max: maxD };
  if (L <= minD) return { status: 'never', min: minD, max: maxD };

  const half = Math.acos(clamp((ra * ra + rb * rb - L * L) / (2 * ra * rb), -1, 1));
  const dth = wrapPi(angleAt(b, t) - angleAt(a, t));
  const openFor = 2 * half / Math.abs(rel);      // how long a window lasts
  const synodic = TAU / Math.abs(rel);

  if (Math.abs(dth) <= half) {
    const edge = rel > 0 ? half : -half;
    return { status: 'in', closesIn: travelTime(dth, edge, rel),
             half, openFor, synodic, min: minD, max: maxD };
  }
  const opensIn = Math.min(travelTime(dth, half, rel), travelTime(dth, -half, rel));
  return { status: 'wait', opensIn, half, openFor, synodic, min: minD, max: maxD };
}

/* ===========================================================================
   Fuel — ShuttleFuel.cs, verbatim
   ========================================================================= */

const F = () => S.fuel;
const distanceBudget = () => Math.max(0, F().fuelMax - F().launchLandCost);

function costForMetres(m) {
  const f = F();
  if (f.maxJumpKm <= 0.01) return f.launchLandCost;
  const frac = Math.max(0, m) / 1000 / f.maxJumpKm;
  return f.launchLandCost + distanceBudget() * Math.pow(frac, Math.max(0.01, f.jumpCostExponent));
}

function rangeKm(units) {
  const f = F(), budget = distanceBudget();
  if (budget <= 1e-4) return 0;
  const spendable = Math.max(0, units - f.launchLandCost);
  return f.maxJumpKm * Math.pow(clamp(spendable / budget, 0, 1), 1 / Math.max(0.01, f.jumpCostExponent));
}

const rangeM = () => rangeKm(fuel) * 1000;
const canAfford = m => fuel >= costForMetres(m) - 1e-4;

/* ===========================================================================
   Camera
   ========================================================================= */

const cam = { x: 0, y: 0, k: 0.03 };      // k = px per metre
let cv, cx2d, W = 0, H = 0, dpr = 1;

const toX = wx => (wx - cam.x) * cam.k + W / 2;
const toY = wy => H / 2 - (wy - cam.y) * cam.k;
const toWorld = (sx, sy) => ({ x: (sx - W / 2) / cam.k + cam.x, y: cam.y - (sy - H / 2) / cam.k });

function fit(mode) {
  const pad = 46;
  if (mode === 'range' && here) {
    const p = posAt(here, clock + project);
    cam.x = p.x; cam.y = p.y;
    cam.k = Math.min(W, H) / (Math.max(rangeM(), 3000) * 2.7) ;
  } else if (mode === 'inner') {
    const r = 12800;
    cam.x = 0; cam.y = 0;
    cam.k = (Math.min(W, H) - pad * 2) / (r * 2);
  } else {
    const r = Math.max(...planets.map(p => p.orbitRadius)) * 1.08;
    cam.x = 0; cam.y = 0;
    cam.k = (Math.min(W, H) - pad * 2) / (r * 2);
  }
  document.querySelectorAll('[data-fit]').forEach(el =>
    el.classList.toggle('on', el.dataset.fit === mode));
}

/* ===========================================================================
   Render
   ========================================================================= */

const CSS = getComputedStyle(document.documentElement);
const C = n => CSS.getPropertyValue(n).trim();

const stars = [];
for (let i = 0; i < 300; i++)
  stars.push({ x: Math.random(), y: Math.random(), a: Math.random() * .55 + .12,
               s: Math.random() < .12 ? 1.7 : 1 });

function dotRadius(p) { return clamp(3.4 + Math.sqrt(p.bodyRadius) * 0.52, 4.4, 15); }

function draw() {
  const t = clock + project;
  cx2d.setTransform(dpr, 0, 0, dpr, 0, 0);
  cx2d.clearRect(0, 0, W, H);

  // backdrop
  const g = cx2d.createRadialGradient(W / 2, H / 2, 0, W / 2, H / 2, Math.max(W, H) * .75);
  g.addColorStop(0, '#050e12'); g.addColorStop(1, '#03070a');
  cx2d.fillStyle = g; cx2d.fillRect(0, 0, W, H);

  cx2d.save();
  for (const s of stars) {
    // a whisper of parallax so panning feels like a window, not a texture
    const sx = (s.x * W - cam.x * cam.k * .04 + W) % W;
    const sy = (s.y * H + cam.y * cam.k * .04 + H) % H;
    cx2d.fillStyle = `rgba(160,220,210,${s.a})`;
    cx2d.fillRect(sx, sy, s.s, s.s);
  }
  cx2d.restore();

  drawOrbits(t);
  if (here) drawRangeRing(t);
  drawSun();
  drawGhosts(t);
  drawPlanets(t);
  if (flight) drawFlightPath(t);
  drawScaleBar();
}

function drawOrbits(t) {
  for (const p of planets) {
    cx2d.beginPath();
    cx2d.arc(toX(0), toY(0), p.orbitRadius * cam.k, 0, TAU);
    const lit = p === selected || p === hovered;
    cx2d.strokeStyle = lit ? '#1d5a68' : C('--grid');
    cx2d.lineWidth = lit ? 1.4 : 1;
    cx2d.stroke();
  }

  // For the selected world that is out of reach, light up the stretch of its
  // orbit that IS inside range. This is the whole mechanic in one picture:
  // the planet is not far away forever, it just has to get to that arc.
  const sel = selected || hovered;
  if (!sel || !here || sel === here) return;
  const w = window_(here, sel, rangeM(), t);
  if (!(w.status === 'wait' || w.status === 'in') || w.half == null) return;

  // Amber, not magenta: magenta is where you can go NOW, amber is the future
  // window you are waiting on. Two colours, two different promises.
  const ha = angleAt(here, t);
  cx2d.beginPath();
  cx2d.arc(toX(0), toY(0), sel.orbitRadius * cam.k, -(ha + w.half), -(ha - w.half));
  cx2d.strokeStyle = w.status === 'in' ? 'rgba(121,255,208,.5)' : 'rgba(255,201,79,.6)';
  cx2d.lineWidth = 2.4;
  cx2d.stroke();
}

function drawRangeRing(t) {
  const p = posAt(here, t), r = rangeM() * cam.k;
  if (r < 2) return;
  cx2d.save();
  cx2d.beginPath();
  cx2d.arc(toX(p.x), toY(p.y), r, 0, TAU);
  const g = cx2d.createRadialGradient(toX(p.x), toY(p.y), r * .55, toX(p.x), toY(p.y), r);
  g.addColorStop(0, 'rgba(255,79,216,0)');
  g.addColorStop(1, 'rgba(255,79,216,.07)');
  cx2d.fillStyle = g; cx2d.fill();
  cx2d.setLineDash([7, 6]);
  cx2d.strokeStyle = 'rgba(255,79,216,.75)';
  cx2d.lineWidth = 1.4;
  cx2d.stroke();
  cx2d.restore();

  if (showLabels && r > 60) {
    cx2d.save();
    cx2d.fillStyle = 'rgba(255,79,216,.8)';
    cx2d.font = '11px Consolas, monospace';
    cx2d.textAlign = 'center';
    cx2d.fillText(`JUMP RANGE ${(rangeM() / 1000).toFixed(1)} KM`, toX(p.x), toY(p.y) - r - 8);
    cx2d.restore();
  }
}

function drawSun() {
  const x = toX(0), y = toY(0);
  const g = cx2d.createRadialGradient(x, y, 0, x, y, 74);
  g.addColorStop(0, 'rgba(255,214,124,.72)');
  g.addColorStop(.28, 'rgba(255,170,70,.18)');
  g.addColorStop(1, 'rgba(255,150,60,0)');
  cx2d.fillStyle = g;
  cx2d.beginPath(); cx2d.arc(x, y, 74, 0, TAU); cx2d.fill();
  cx2d.fillStyle = '#ffd98a';
  cx2d.beginPath(); cx2d.arc(x, y, 9, 0, TAU); cx2d.fill();
}

/** Faint previews: where things are NOW while the scrubber is ahead, and where
 *  the selected pair will be when its window opens. */
function drawGhosts(t) {
  if (project > 0.5) {
    for (const p of planets) {
      const q = posAt(p, clock);
      cx2d.beginPath();
      cx2d.arc(toX(q.x), toY(q.y), dotRadius(p) * .62, 0, TAU);
      cx2d.strokeStyle = 'rgba(120,255,208,.22)';
      cx2d.lineWidth = 1; cx2d.stroke();
    }
  }

  const sel = selected;
  if (!sel || !here || sel === here || project > 0.5) return;
  const w = window_(here, sel, rangeM(), t);
  if (w.status !== 'wait' || !isFinite(w.opensIn)) return;
  const ft = t + w.opensIn;
  const a = posAt(here, ft), b = posAt(sel, ft);

  cx2d.save();
  cx2d.setLineDash([3, 5]);
  cx2d.strokeStyle = 'rgba(255,201,79,.3)';
  cx2d.beginPath(); cx2d.arc(toX(a.x), toY(a.y), rangeM() * cam.k, 0, TAU); cx2d.stroke();
  cx2d.setLineDash([]);
  for (const [pt, pl] of [[a, here], [b, sel]]) {
    cx2d.beginPath();
    cx2d.arc(toX(pt.x), toY(pt.y), dotRadius(pl) * .8, 0, TAU);
    cx2d.strokeStyle = 'rgba(255,201,79,.75)'; cx2d.lineWidth = 1.3; cx2d.stroke();
  }
  cx2d.fillStyle = 'rgba(255,201,79,.9)';
  cx2d.font = '11px Consolas, monospace';
  cx2d.textAlign = 'center';
  cx2d.fillText('WINDOW OPENS ' + mmss(w.opensIn), toX(b.x), toY(b.y) + dotRadius(sel) + 16);
  cx2d.restore();
}

function drawPlanets(t) {
  const labels = [];
  for (const p of planets) {
    const q = posAt(p, t);
    const x = toX(q.x), y = toY(q.y), r = dotRadius(p);
    if (x < -80 || x > W + 80 || y < -80 || y > H + 80) continue;

    const isHere = p === here;
    const d = here ? distAt(here, p, t) : 0;
    const reach = isHere || (here && canAfford(d));

    // moons — drawn first so the planet sits on top
    for (const m of p.moons || []) {
      const ma = (t / Math.max(m.period, 1)) * TAU + p.orbitRadius;
      const mr = Math.max(m.orbitRadius * cam.k, r + 5);
      const mx = x + Math.cos(ma) * mr, my = y - Math.sin(ma) * mr;
      cx2d.beginPath(); cx2d.arc(x, y, mr, 0, TAU);
      cx2d.strokeStyle = 'rgba(30,74,63,.5)'; cx2d.lineWidth = 1; cx2d.stroke();
      cx2d.beginPath(); cx2d.arc(mx, my, 2.2, 0, TAU);
      cx2d.fillStyle = reach ? '#7d8c92' : '#39464b'; cx2d.fill();
    }

    // body — lit from the sun, so the map reads as a place and not a chart
    const sunAng = Math.atan2(toY(0) - y, toX(0) - x);
    const gg = cx2d.createRadialGradient(
      x + Math.cos(sunAng) * r * .45, y + Math.sin(sunAng) * r * .45, r * .12, x, y, r);
    gg.addColorStop(0, p.colour);
    gg.addColorStop(1, reach ? shade(p.colour, .42) : shade(p.colour, .16));
    cx2d.beginPath(); cx2d.arc(x, y, r, 0, TAU);
    cx2d.fillStyle = gg; cx2d.fill();
    if (!reach) { cx2d.fillStyle = 'rgba(4,7,10,.42)'; cx2d.fill(); }

    // rings: here / selected / hovered / reachable
    if (isHere) ring(x, y, r + 7, C('--ink'), 1.6, [4, 4], t * 40);
    else if (reach) ring(x, y, r + 4.5, 'rgba(121,255,208,.5)', 1.1);
    if (p === selected) ring(x, y, r + 11, C('--accent'), 2);
    else if (p === hovered) ring(x, y, r + 11, 'rgba(255,79,216,.45)', 1.2);

    if (!showLabels) continue;
    // A distance under EVERY planet was most of the clutter — twelve numbers
    // nobody asked for. Only the one you are pointing at gets a number.
    const showSub = isHere || p === selected || p === hovered;
    labels.push({
      x, y, r, p,
      name: p.name.toUpperCase(),
      nameCol: isHere ? C('--ink') : reach ? '#a9d9cb' : '#4d6a71',
      sub: !showSub ? '' : isHere ? '◆ SHUTTLE' : here ? (d / 1000).toFixed(1) + ' km' : '',
      subCol: isHere ? C('--ink') : reach ? 'rgba(121,255,208,.65)' : 'rgba(255,201,79,.7)',
      // The selected world, the one you are standing on, and anything you can
      // actually fly to win a collision; distant scenery gives way.
      rank: (isHere ? 3 : 0) + (p === selected ? 4 : 0) + (reach ? 1 : 0),
    });
  }
  drawLabels(labels);
}

/**
 * Labels, decluttered. The twins sit 1 km apart on the same rail, so at any
 * sensible zoom their names land on top of each other — the first version of
 * this map read "FIER TWIN" with a planet through the middle of it. Anything
 * that would overlap is nudged clear and gets a leader line back to its dot.
 */
function drawLabels(labels) {
  labels.sort((a, b) => b.rank - a.rank);
  const hits = (a, b) => !(a.x1 < b.x0 || a.x0 > b.x1 || a.y1 < b.y0 || a.y0 > b.y1);
  // Seed with the bodies themselves so no label is ever written across a planet.
  const taken = labels.map(l => ({ x0: l.x - l.r - 3, x1: l.x + l.r + 3,
                                   y0: l.y - l.r - 3, y1: l.y + l.r + 3 }));

  for (const l of labels) {
    cx2d.font = '12px "Segoe UI", sans-serif';
    const halfW = Math.max(cx2d.measureText(l.name).width, 46) / 2 + 3;
    // Clear the selection/hover ring, which sits further out than the body.
    const baseY = l.y - l.r - (l.p === selected || l.p === hovered ? 17 : 9);
    let dy = 0;
    for (const step of [0, -15, 16, -30, 31, -45, 46, -60, 61]) {
      const box = { x0: l.x - halfW, x1: l.x + halfW, y0: baseY + step - 11, y1: baseY + step + 3 };
      if (!taken.some(o => hits(box, o))) { dy = step; taken.push(box); break; }
      dy = step;                       // last resort: take the furthest slot
    }
    const ny = baseY + dy;
    if (Math.abs(dy) > 6) {            // leader line back to the body
      cx2d.strokeStyle = 'rgba(61,143,120,.45)';
      cx2d.lineWidth = 1;
      cx2d.beginPath();
      cx2d.moveTo(l.x, ny + 4);
      cx2d.lineTo(l.x, l.y - l.r - 3);
      cx2d.stroke();
    }
    cx2d.textAlign = 'center';
    cx2d.fillStyle = l.nameCol;
    cx2d.fillText(l.name, l.x, ny);

    if (!l.sub) continue;
    cx2d.font = l.p === here ? '10px Consolas, monospace' : '11px Consolas, monospace';
    const sw = cx2d.measureText(l.sub).width / 2 + 3;
    let placed = null;
    for (const step of [0, 13, 26, 39]) {
      const sy = l.y + l.r + 15 + step;
      const sbox = { x0: l.x - sw, x1: l.x + sw, y0: sy - 9, y1: sy + 3 };
      if (!taken.some(o => hits(sbox, o))) { placed = sy; taken.push(sbox); break; }
    }
    if (placed == null) continue;                   // drop it rather than smear
    cx2d.fillStyle = l.subCol;
    cx2d.fillText(l.sub, l.x, placed);
  }
}

function ring(x, y, r, col, w, dash, off) {
  cx2d.save();
  if (dash) { cx2d.setLineDash(dash); cx2d.lineDashOffset = -(off || 0); }
  cx2d.beginPath(); cx2d.arc(x, y, r, 0, TAU);
  cx2d.strokeStyle = col; cx2d.lineWidth = w; cx2d.stroke();
  cx2d.restore();
}

function shade(hex, f) {
  const n = parseInt(hex.slice(1), 16);
  return `rgb(${Math.round((n >> 16 & 255) * f)},${Math.round((n >> 8 & 255) * f)},${Math.round((n & 255) * f)})`;
}

function drawFlightPath(t) {
  const a = flight.from, b = posAt(flight.target, t);
  const prog = flight.phase === 'transit' ? clamp(flight.t / flight.dur, 0, 1) : 0;
  cx2d.save();
  cx2d.setLineDash([6, 7]);
  cx2d.strokeStyle = 'rgba(255,79,216,.55)';
  cx2d.beginPath(); cx2d.moveTo(toX(a.x), toY(a.y)); cx2d.lineTo(toX(b.x), toY(b.y)); cx2d.stroke();
  cx2d.restore();
  const sx = lerp(toX(a.x), toX(b.x), prog), sy = lerp(toY(a.y), toY(b.y), prog);
  cx2d.beginPath(); cx2d.arc(sx, sy, 4, 0, TAU);
  cx2d.fillStyle = C('--accent'); cx2d.fill();
  ring(sx, sy, 9, 'rgba(255,79,216,.5)', 1.2);
}

function drawScaleBar() {
  // Pick a round number of km that lands near 150 px.
  const targetPx = 150;
  const raw = targetPx / cam.k / 1000;
  const pow = Math.pow(10, Math.floor(Math.log10(raw)));
  const km = [1, 2, 5, 10].map(m => m * pow).reduce((a, b) =>
    Math.abs(b - raw) < Math.abs(a - raw) ? b : a);
  const px = km * 1000 * cam.k;
  const x = W - px - 22, y = H - 22;
  cx2d.strokeStyle = '#2a4a52'; cx2d.lineWidth = 1;
  cx2d.beginPath();
  cx2d.moveTo(x, y - 4); cx2d.lineTo(x, y); cx2d.lineTo(x + px, y); cx2d.lineTo(x + px, y - 4);
  cx2d.stroke();
  cx2d.fillStyle = '#2f6b5c'; cx2d.font = '11px Consolas, monospace'; cx2d.textAlign = 'center';
  cx2d.fillText(km >= 1 ? km + ' KM' : (km * 1000) + ' M', x + px / 2, y - 8);
}

/* ===========================================================================
   Side panel
   ========================================================================= */

const mmss = s => {
  if (!isFinite(s)) return '—';
  s = Math.max(0, Math.round(s));
  return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
};

function statusOf(p, t) {
  if (!here || p === here) return { cls: 'ok', text: 'YOU ARE HERE · RELOCATE' };
  const d = distAt(here, p, t);
  if (canAfford(d)) {
    // Only mention the window when it is actually about to shut — saying
    // "closes in 47 minutes" on every planet is noise.
    const w = window_(here, p, rangeM(), t);
    const tail = w.status === 'in' && isFinite(w.closesIn) && w.closesIn < 240
      ? ' · CLOSES ' + mmss(w.closesIn) : '';
    return { cls: 'ok', text: '● IN RANGE' + tail };
  }
  const w = window_(here, p, rangeM(), t);
  if (w.status === 'wait') return { cls: 'wait', text: '● IN RANGE IN ' + mmss(w.opensIn) };
  return { cls: 'never', text: '● OUT OF REACH ON THIS TANK' };
}

/* The roster is built ONCE and then updated in place. Rebuilding the rows every
   tick would throw away :hover mid-mouse and make the list flicker — the same
   change-gate rule the Unity NAV app uses on its own labels. */
const rosterRows = new Map();

function buildRoster() {
  const box = $('#list');
  box.innerHTML = '';
  rosterRows.clear();
  for (const p of planets) {
    const el = document.createElement('div');
    el.className = 'chip';
    el.innerHTML = `<span class="dot" style="background:${p.colour};color:${p.colour}"></span>` +
                   `<span class="nm">${p.name.toUpperCase()}</span><span class="km"></span>`;
    el.onclick = () => { selected = p; paintSide(); };
    el.onmouseenter = () => hovered = p;
    el.onmouseleave = () => { if (hovered === p) hovered = null; };
    box.appendChild(el);
    rosterRows.set(p, el);
  }
}

function paintRoster(t) {
  const rows = planets
    .map(p => ({ p, d: here ? distAt(here, p, t) : 0 }))
    .sort((a, b) => (a.p === here ? -1 : b.p === here ? 1 : a.d - b.d));
  const inRange = rows.filter(r => r.p !== here && canAfford(r.d)).length;
  $('#listHd').textContent = `DESTINATIONS — ${inRange} IN RANGE`;

  const box = $('#list');
  rows.forEach(({ p, d }, i) => {
    const el = rosterRows.get(p);
    if (!el) return;
    if (box.children[i] !== el) box.insertBefore(el, box.children[i] || null);
    const isHere = p === here, ok = !isHere && canAfford(d);
    const cls = 'chip' + (p === selected ? ' sel' : '') + (isHere ? ' here' : ok ? '' : ' out');
    if (el.className !== cls) el.className = cls;
    let right;
    if (isHere) right = 'PARKED';
    else if (ok) right = (d / 1000).toFixed(1) + ' km';
    else {
      const w = window_(here, p, rangeM(), t);
      right = w.status === 'wait' ? 'opens ' + mmss(w.opensIn) : 'out of reach';
    }
    const km = el.lastElementChild;
    if (km.textContent !== right) km.textContent = right;
  });
}

function paintSide() {
  const t = clock + project;

  // fuel gauge
  const pct = fuel / F().fuelMax;
  const fill = $('#fuelFill');
  fill.style.width = (pct * 100).toFixed(1) + '%';
  fill.className = pct < .16 ? 'crit' : pct < .34 ? 'low' : '';
  $('#fuelTxt').textContent =
    `FUEL ${Math.round(pct * 100)}%  ·  RANGE ${rangeKm(fuel).toFixed(1)} KM`;
  $('#devFuel').value = Math.round(fuel);
  $('#devFuelV').textContent = Math.round(fuel);

  paintRoster(t);

  // The one destination block: who, whether you can go, and the two numbers
  // that decide it. Everything else the map is already saying.
  const p = selected;
  const btn = $('#travel');
  const dot = $('#pName .dot');

  if (!p) {
    dot.style.background = '#132025'; dot.style.color = 'transparent';
    $('#pName .t').textContent = '—';
    $('#pName').style.color = 'var(--locked)';
    $('#pStat').className = '';
    $('#pStat').textContent = 'SELECT A DESTINATION';
    $('#pDist').textContent = '—'; $('#pCost').textContent = '—';
    $('#pDist').className = ''; $('#pCost').className = '';
    btn.className = ''; btn.innerHTML = 'TRAVEL';
    return;
  }

  const d = here && p !== here ? distAt(here, p, t) : 0;
  const cost = costForMetres(d);
  const st = statusOf(p, t);
  const over = cost > fuel + 1e-4;

  dot.style.background = p.colour; dot.style.color = p.colour;
  $('#pName .t').textContent = p.name.toUpperCase();
  $('#pName').style.color = 'var(--ink)';
  $('#pStat').className = st.cls;
  $('#pStat').textContent = st.text;
  $('#pDist').textContent = p === here ? 'HERE' : (d / 1000).toFixed(1) + ' KM';
  $('#pCost').textContent = `${cost.toFixed(0)} u  ·  ${Math.round(cost / F().fuelMax * 100)}%`;
  $('#pCost').className = over ? 'warn' : '';

  if (flight) { btn.className = ''; btn.innerHTML = 'IN FLIGHT'; }
  else if (over) {
    btn.className = '';
    btn.innerHTML = `NOT ENOUGH FUEL<small>NEED ${cost.toFixed(0)} u · HAVE ${fuel.toFixed(0)} u</small>`;
  } else {
    btn.className = 'live';
    btn.innerHTML = 'TRAVEL';
  }
}

/* ===========================================================================
   Flight
   ========================================================================= */

function startTravel() {
  if (!selected || flight || project > 0.5) return;
  const t = clock;
  const d = selected === here ? 0 : distAt(here, selected, t);
  const cost = costForMetres(d);
  if (cost > fuel + 1e-4) { toast(`NEED ${cost.toFixed(0)} FUEL · HAVE ${fuel.toFixed(0)}`); return; }
  flight = { phase: 'countdown', t: 0, target: selected, cost, startFuel: fuel,
             from: posAt(here, t), dur: clamp(d / 1400, 3.5, 13) };
  $('#flight').classList.add('on');
}

function tickFlight(dt) {
  if (!flight) return;
  flight.t += dt;
  const big = $('#fBig'), sub = $('#fSub'), bar = $('#fBar');
  sub.textContent = 'DESTINATION: ' + flight.target.name.toUpperCase();

  if (flight.phase === 'countdown') {
    const left = S.countdownSeconds - flight.t;
    big.textContent = 'TAKING OFF IN ' + Math.max(0, Math.ceil(left));
    bar.style.width = clamp(flight.t / S.countdownSeconds, 0, 1) * 100 + '%';
    $('#fSkip').style.display = '';
    if (left <= 0) { flight.phase = 'transit'; flight.t = 0; flight.from = posAt(here, clock); }
    return;
  }

  $('#fSkip').style.display = 'none';
  const prog = clamp(flight.t / flight.dur, 0, 1);
  big.textContent = 'EN ROUTE TO ' + flight.target.name.toUpperCase();
  sub.textContent = `AUTOPILOT ENGAGED · ${Math.round(lerp(0, 1400, Math.sin(prog * Math.PI)))} M/S`;
  bar.style.width = prog * 100 + '%';
  // The tank empties as the shuttle actually flies (ShuttleFuel.BurnTo) — the
  // price was locked in at the press, but the gauge falls with the engines.
  fuel = Math.max(0, flight.startFuel - flight.cost * prog);
  if (prog >= 1) {
    here = flight.target;
    selected = here;
    fuel = Math.max(0, flight.startFuel - flight.cost);
    toast('TOUCHDOWN — ' + here.name.toUpperCase());
    flight = null;
    $('#flight').classList.remove('on');
  }
}

let toastT = 0;
function toast(msg) { $('#toast').textContent = msg; $('#toast').classList.add('on'); toastT = 3.4; }

/* ===========================================================================
   Input
   ========================================================================= */

function hitTest(sx, sy) {
  const t = clock + project;
  let best = null, bd = Infinity;
  for (const p of planets) {
    const q = posAt(p, t);
    const d = Math.hypot(toX(q.x) - sx, toY(q.y) - sy);
    if (d <= dotRadius(p) + 12 && d < bd) { best = p; bd = d; }
  }
  return best;
}

function bindInput() {
  let drag = null;
  cv.addEventListener('mousedown', e => {
    const r = cv.getBoundingClientRect(), sc = r.width / W * dpr;
    drag = { x: e.clientX, y: e.clientY, cx: cam.x, cy: cam.y, moved: 0, sc };
  });
  window.addEventListener('mousemove', e => {
    const r = cv.getBoundingClientRect();
    const sx = (e.clientX - r.left) / r.width * W, sy = (e.clientY - r.top) / r.height * H;
    if (drag) {
      const dx = (e.clientX - drag.x) / r.width * W, dy = (e.clientY - drag.y) / r.height * H;
      drag.moved = Math.max(drag.moved, Math.hypot(dx, dy));
      cam.x = drag.cx - dx / cam.k; cam.y = drag.cy + dy / cam.k;
      cv.style.cursor = 'grabbing';
      return;
    }
    if (sx < 0 || sx > W || sy < 0 || sy > H) { hovered = null; return; }
    hovered = hitTest(sx, sy);
    cv.style.cursor = hovered ? 'pointer' : 'crosshair';
  });
  window.addEventListener('mouseup', e => {
    if (drag && drag.moved < 4) {
      const r = cv.getBoundingClientRect();
      const sx = (e.clientX - r.left) / r.width * W, sy = (e.clientY - r.top) / r.height * H;
      const hit = hitTest(sx, sy);
      if (hit) { selected = hit; paintSide(); }
    }
    drag = null; cv.style.cursor = 'crosshair';
  });
  cv.addEventListener('wheel', e => {
    e.preventDefault();
    const r = cv.getBoundingClientRect();
    const sx = (e.clientX - r.left) / r.width * W, sy = (e.clientY - r.top) / r.height * H;
    const before = toWorld(sx, sy);
    cam.k = clamp(cam.k * Math.pow(0.9988, e.deltaY), 4e-4, 1.4);
    const after = toWorld(sx, sy);
    cam.x += before.x - after.x; cam.y += before.y - after.y;
    document.querySelectorAll('[data-fit]').forEach(el => el.classList.remove('on'));
  }, { passive: false });

  document.querySelectorAll('[data-fit]').forEach(el =>
    el.onclick = () => fit(el.dataset.fit));
  $('#travel').onclick = startTravel;
  $('#fSkip').onclick = () => { if (flight && flight.phase === 'countdown') flight.t = S.countdownSeconds; };

  // dev strip
  $('#devFuel').oninput = e => { fuel = +e.target.value; $('#devFuelV').textContent = fuel; paintSide(); };
  $('#devFill').onclick = () => { fuel = F().fuelMax; $('#devFuel').value = fuel; $('#devFuelV').textContent = fuel; paintSide(); };
  $('#devHere').onclick = () => {
    const i = planets.indexOf(here);
    here = planets[(i + 1) % planets.length];
    toast('SHUTTLE MOVED TO ' + here.name.toUpperCase());
    paintSide();
  };
  document.querySelectorAll('[data-rate]').forEach(el => el.onclick = () => {
    rate = +el.dataset.rate;
    document.querySelectorAll('[data-rate]').forEach(o => o.classList.toggle('on', o === el));
  });

  window.addEventListener('keydown', e => {
    if (e.key === 'Escape') { selected = null; paintSide(); }
  });
}

/* ===========================================================================
   Boot + loop
   ========================================================================= */

function layout() {
  // Scale the 1500x940 screen to whatever room the window has, and size its
  // wrapper to the SCALED result — a transform does not change layout size, so
  // without this the page thinks the screen is still 1500 wide and clips it.
  const availW = document.documentElement.clientWidth - 28;
  const availH = window.innerHeight - $('#dev').offsetHeight - 28;
  const scale = clamp(Math.min(availW / 1500, availH / 940), 0.25, 1);
  $('#screen').style.transform = `scale(${scale})`;
  $('#fit').style.width = (1500 * scale) + 'px';
  $('#fit').style.height = (940 * scale) + 'px';

  dpr = clamp((window.devicePixelRatio || 1) * scale, 1, 2.4);
  W = 1030; H = 940 - 62 - 16;                 // CSS px inside the screen box
  cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr);
  cv.style.width = W + 'px'; cv.style.height = H + 'px';
}

let last = performance.now(), sideDue = 0;
function loop(now) {
  const dt = Math.min((now - last) / 1000, .25); last = now;
  clock += dt * rate;
  if (flight) tickFlight(dt);
  if (toastT > 0 && (toastT -= dt) <= 0) $('#toast').classList.remove('on');
  $('#devClock').textContent =
    `T+${mmss(clock)}   ·   ${planets.length} planets   ·   scale ${(cam.k * 1000).toFixed(2)} px/km`;
  draw();
  // The map animates at frame rate; the readouts only need a few times a
  // second, and rewriting their DOM every frame is pure waste.
  if ((sideDue -= dt) <= 0) { sideDue = 1 / 6; paintSide(); }
  requestAnimationFrame(loop);
}

fetch('system.json').then(r => r.json()).then(data => {
  S = data;
  planets = S.planets;
  here = planets.find(p => p.name === 'Icey Twin') || planets[0];
  selected = null;
  fuel = S.fuel.newGameFuel;

  // A view is shareable: ?here=Hearth&sel=Cyclops&fuel=100&fit=all&t=300
  const byName = n => planets.find(p => p.name.toLowerCase() === String(n).toLowerCase());
  const q = new URLSearchParams(location.search);
  if (q.has('here')) here = byName(q.get('here')) || here;
  if (q.has('sel')) selected = byName(q.get('sel')) || null;
  if (q.has('fuel')) fuel = clamp(+q.get('fuel'), 0, S.fuel.fuelMax);
  if (q.has('t')) clock = +q.get('t') || 0;

  cv = $('#map'); cx2d = cv.getContext('2d');
  buildRoster();
  layout();
  window.addEventListener('resize', layout);
  fit(q.get('fit') || 'inner');
  bindInput();
  if (q.has('fly')) startTravel();          // ?sel=Hearth&fly=1 — jump straight to the countdown
  if (q.has('sim')) {                       // &sim=14 — run 14 s forward before the first frame
    const want = clamp(+q.get('sim') || 0, 0, 3600);
    for (let s = 0; s < want; s += 1 / 30) { clock += rate / 30; if (flight) tickFlight(1 / 30); }
  }
  // ?project=600 still works — it flies the whole system 10 minutes ahead — but
  // the scrubber control is off the screen: one gadget too many in a cockpit.
  if (q.has('project')) project = +q.get('project') || 0;
  requestAnimationFrame(loop);   // paintSide syncs the dev strip on its first tick
}).catch(err => {
  document.body.innerHTML =
    '<pre style="color:#ff6b6b;padding:40px;font:14px monospace">' +
    'system.json failed to load — run this over http, not file://\n\n' +
    '   prototypes/nav-map/serve.bat\n\n' + err + '</pre>';
});
