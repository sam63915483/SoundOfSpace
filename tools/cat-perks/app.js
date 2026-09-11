/* Cat Perks — design mockup.
   Three presentations of the same roll, so the choice is about FEEL, not maths.
   The odds engine, the perk effects and the fish list are all shared. */

'use strict';

/* ── the three perks ──────────────────────────────────────────────────────
   Each one names the knob in the real fishing code it would drive, so none of
   this is invented: every effect below already exists as a number the game
   multiplies. */
const PERKS = [
  { id:'luck', name:'LUCKY WHISKERS', short:'LUCK', ic:'🍀', tag:'+20% LUCK',
    blurb:'Bites come sooner, and what bites is better and bigger.',
    hook:'All three at once: uncommon+rare tier weights &times;1.20 (FishingRules.TierWeights), '+
         'bite wait &times;0.90 (WaitMultiplier), weight exponent &times;0.92 (RollWeight).' },
  { id:'weight', name:'WEIGHT GAIN', short:'WEIGHT', ic:'⚖️', tag:'+WEIGHT GAIN',
    blurb:'Same fish, fatter. Nothing changes about WHAT bites.',
    hook:'RollWeight exponent &times;0.80 — the same dial bait already uses '+
         '(Voidmaggots is 0.78), so this is best-bait-grade size on top of your bait.' },
  { id:'frenzy', name:'FISHING FRENZY', short:'FRENZY', ic:'⚡', tag:'+FISHING FRENZY',
    blurb:'Bites land almost instantly. Your odds are untouched — just volume.',
    hook:'Bobber._biteOddsBonus &times;2.0 — the bite countdown already runs through '+
         'this exact multiplier, so this perk is a one-line change.' },
];

const TIERS = [
  { mins:1, label:'1 MIN', css:'--t1', col:'#8CAFFF' },
  { mins:2, label:'2 MIN', css:'--t2', col:'#3CDCBE' },
  { mins:3, label:'3 MIN', css:'--t3', col:'#FFD232' },
];

/* fish, straight off FishingRules.Species */
const FISH = [
  ['Bassk',0,1,8],['Truttle',0,1,6],['Perchik',0,1,5],['Smelk',0,1,3],
  ['Grubbler',0,1,7],['Skrout',0,1,6],['Purchlet',0,1,4],['Wallop',0,2,8],
  ['Emberbass',1,6,18],['Glimtrout',1,5,15],['Nullpike',1,8,22],['Sturgle',1,10,24],
  ['Murkfin',1,6,20],['Flubb',1,5,14],['Knurl',1,9,24],['Tarnish',1,7,18],
  ['Marlorb',2,20,50],['Tarpune',2,15,40],['Muskrellon',2,25,50],['Coelancer',2,18,45],
  ['Zibbet',2,18,44],['Ossum',2,22,50],['Snagg',2,15,40],['Vorm',2,20,48],
];
const FISHCOL = ['#8CAFFF','#3CDCBE','#FFD232'];

/* ── state ───────────────────────────────────────────────────────────────── */
let hardness = 3;          // how many times rarer a 3-min is than a 1-min
let option   = 'A';
let busy     = false;
/* ONE SLOT, never two (Sam, 2026-09-10). Rolling again while a perk is running
   is allowed, and whatever comes out REPLACES what you had — timer and all.
   That is the whole gamble: a running 3-min can be thrown away by a 1-min. */
let current  = null;       // {perk, tier, until, total}
let napSecs  = 0;          // per-cat cooldown, 0 = off (Sam's spec)
let napUntil = 0;
let bag      = [];

const $  = s => document.querySelector(s);
const el = (t,c,h) => { const n=document.createElement(t); if(c)n.className=c;
                        if(h!=null)n.innerHTML=h; return n; };

/* ── odds ────────────────────────────────────────────────────────────────── */
/* Geometric ladder: each step up is equally harder, and 1-min : 3-min lands
   exactly on the rarity number in the slider. */
function tierWeights(){
  const w = [Math.pow(hardness,1), Math.pow(hardness,0.5), 1];
  const s = w[0]+w[1]+w[2];
  return w.map(x => x/s);
}
function outcomeTable(){
  const tw = tierWeights(), out = [];
  for (const p of PERKS) for (let t=0;t<3;t++)
    out.push({ perk:p, tier:t, p: tw[t]/PERKS.length });
  return out;
}
function roll(){
  const tbl = outcomeTable();
  let r = Math.random(), acc = 0;
  for (const o of tbl){ acc += o.p; if (r <= acc) return { perk:o.perk, tier:o.tier, p:o.p }; }
  const last = tbl[tbl.length-1];
  return { perk:last.perk, tier:last.tier, p:last.p };
}
function randomOutcome(){ const r = roll(); return r; }

/* ── sound (tiny WebAudio, no files) ─────────────────────────────────────── */
let AC = null;
const audioOn = () => $('#snd').checked;
function ac(){ if(!AC) AC = new (window.AudioContext||window.webkitAudioContext)(); return AC; }
function blip(freq, dur, type, vol){
  if (!audioOn()) return;
  const c = ac(), o = c.createOscillator(), g = c.createGain();
  o.type = type||'square'; o.frequency.value = freq;
  g.gain.setValueAtTime(vol||0.05, c.currentTime);
  g.gain.exponentialRampToValueAtTime(0.0001, c.currentTime + (dur||0.05));
  o.connect(g); g.connect(c.destination); o.start(); o.stop(c.currentTime+(dur||0.05)+0.02);
}
const tick  = () => blip(1150 + Math.random()*90, 0.028, 'square', 0.035);
function fanfare(tier){
  if (!audioOn()) return;
  const notes = [[523,0],[659,90]];
  if (tier>=1) notes.push([784,180]);
  if (tier>=2) notes.push([1047,270],[1319,380]);
  notes.forEach(([f,d]) => setTimeout(()=>blip(f,0.26,'triangle',0.09), d));
}
function dud(){ blip(180, 0.16, 'sawtooth', 0.05); }
function meow(){
  if (!audioOn()) return;
  const c = ac(), o = c.createOscillator(), g = c.createGain();
  o.type='sine'; o.frequency.setValueAtTime(700,c.currentTime);
  o.frequency.exponentialRampToValueAtTime(430, c.currentTime+0.34);
  g.gain.setValueAtTime(0.06,c.currentTime);
  g.gain.exponentialRampToValueAtTime(0.0001,c.currentTime+0.38);
  o.connect(g); g.connect(c.destination); o.start(); o.stop(c.currentTime+0.4);
}

/* ── starfield ───────────────────────────────────────────────────────────── */
(function stars(){
  const cv = $('#stars'), cx = cv.getContext('2d');
  function draw(){
    const w = cv.width = cv.offsetWidth, h = cv.height = cv.offsetHeight;
    cx.clearRect(0,0,w,h);
    for (let i=0;i<170;i++){
      const y = Math.random()*h*0.72;
      cx.fillStyle = 'rgba(200,230,255,'+(0.12+Math.random()*0.5)+')';
      cx.fillRect(Math.random()*w, y, 1.2, 1.2);
    }
  }
  draw(); addEventListener('resize', draw);
})();

/* ── card builders ───────────────────────────────────────────────────────── */
function cardEl(o, vertical){
  const t = TIERS[o.tier];
  const c = el('div','card');
  c.style.borderColor = t.col;
  c.style.boxShadow = 'inset 0 -3px 0 ' + t.col;
  if (vertical){
    c.innerHTML = '<div class="ic">'+o.perk.ic+'</div>'+
      '<div class="txt"><div class="nm" style="color:'+t.col+'">'+o.perk.short+'</div>'+
      '<div class="du" style="color:'+t.col+'">'+t.label+'</div></div>';
  } else {
    c.innerHTML = '<div class="ic">'+o.perk.ic+'</div>'+
      '<div class="nm" style="color:'+t.col+'">'+o.perk.short+'</div>'+
      '<div class="du" style="color:'+t.col+'">'+t.label+'</div>';
  }
  return c;
}

/* ── the three presentations ─────────────────────────────────────────────── */
const CASE = $('#case');

function openCase(result, fishName){
  busy = true;
  CASE.classList.add('on');
  CASE.innerHTML = '';
  CASE.appendChild(el('div','caption', fishName.toUpperCase()+' &nbsp;→&nbsp; THE CAT IS THINKING'));
  ({A:caseA, B:caseB, C:caseC})[option](result);
}

/* A — horizontal reel. Tension lives in the deceleration. */
function caseA(result){
  const WIN = 54, PITCH = 104, CARD = 96, N = 64, DUR = 6200;
  const box = el('div','reelbox h');
  box.appendChild(el('div','fadeL')); box.appendChild(el('div','fadeR'));
  const mk = el('div','marker','<i class="up"></i><i class="dn"></i>');
  const strip = el('div','strip');
  const cards = [];
  for (let i=0;i<N;i++){
    const o = (i===WIN) ? result : randomOutcome();
    const c = cardEl(o,false); cards.push(c); strip.appendChild(c);
  }
  box.appendChild(strip); box.appendChild(mk);
  CASE.appendChild(box);

  requestAnimationFrame(()=>{
    const W = box.clientWidth, mid = W/2;
    const from = mid - (3*PITCH + CARD/2);
    const jitter = (Math.random()*2-1) * (CARD*0.36);
    const to = mid - (WIN*PITCH + CARD/2) + jitter;
    strip.style.transform = 'translateX('+from+'px)';
    let last = -1, t0 = performance.now();
    (function step(now){
      const t = Math.min(1,(now-t0)/DUR);
      const e = 1 - Math.pow(1-t, 4.1);
      const x = from + (to-from)*e;
      strip.style.transform = 'translateX('+x+'px)';
      const idx = Math.floor((mid - x)/PITCH);
      if (idx !== last){ last = idx; if (t < 0.995) tick(); }
      if (t < 1) requestAnimationFrame(step); else land();
    })(t0);

    function land(){
      cards.forEach((c,i)=>{ if(i!==WIN) c.classList.add('dim'); });
      cards[WIN].classList.add('pop');
      cards[WIN].style.boxShadow = '0 0 26px '+TIERS[result.tier].col+', inset 0 -3px 0 '+TIERS[result.tier].col;
      fanfare(result.tier);
      setTimeout(()=>showResult(result), 780);
    }
  });
}

/* B — vertical tag drop. Same reel, HUD-shaped: this one fits down the side of
   the helmet overlay instead of eating the whole screen. */
function caseB(result){
  const WIN = 46, PITCH = 84, CARD = 78, N = 56, DUR = 5400;
  const box = el('div','slotbox v');
  box.appendChild(el('div','fadeT')); box.appendChild(el('div','fadeB'));
  const strip = el('div','vstrip');
  const cards = [];
  for (let i=0;i<N;i++){
    const o = (i===WIN) ? result : randomOutcome();
    const c = cardEl(o,true); cards.push(c); strip.appendChild(c);
  }
  box.appendChild(strip); box.appendChild(el('div','slotwin'));
  CASE.appendChild(box);

  requestAnimationFrame(()=>{
    const H = box.clientHeight, mid = H/2;
    const from = mid - (3*PITCH + CARD/2);
    const jitter = (Math.random()*2-1) * (CARD*0.20);
    const to = mid - (WIN*PITCH + CARD/2) + jitter;
    strip.style.transform = 'translateY('+from+'px)';
    let last = -1, t0 = performance.now();
    (function step(now){
      const t = Math.min(1,(now-t0)/DUR);
      const e = 1 - Math.pow(1-t, 3.7);
      const y = from + (to-from)*e;
      strip.style.transform = 'translateY('+y+'px)';
      const idx = Math.floor((mid - y)/PITCH);
      if (idx !== last){ last = idx; if (t < 0.995) tick(); }
      if (t < 1) requestAnimationFrame(step); else land();
    })(t0);

    function land(){
      cards.forEach((c,i)=>{ if(i!==WIN) c.classList.add('dim'); });
      cards[WIN].classList.add('pop');
      cards[WIN].style.boxShadow = '0 0 26px '+TIERS[result.tier].col+', inset 0 -3px 0 '+TIERS[result.tier].col;
      fanfare(result.tier);
      setTimeout(()=>showResult(result), 780);
    }
  });
}

/* C — perk first, duration last.
   The perk is settled in about a second, because WHICH perk was never the
   gamble. Then the minutes climb one at a time and you watch to see whether
   the third one lights. Shortest run, biggest stomach-drop. */
function caseC(result){
  const wrap = el('div','climb');
  const box  = el('div','shufbox');
  const card = el('div','shufcard');
  box.appendChild(card);
  const pips = el('div','pips');
  const P = [0,1,2].map(i=>{
    const p = el('div','pip3','<div class="big">'+(i+1)+'</div><div>MIN</div>');
    pips.appendChild(p); return p;
  });
  wrap.appendChild(box); wrap.appendChild(pips);
  CASE.appendChild(wrap);

  function paint(perk, col){
    card.style.borderColor = col;
    card.innerHTML = '<div class="ic">'+perk.ic+'</div><div class="nm" style="color:'+col+'">'+perk.name+'</div>';
  }

  /* 1. shuffle the perk, decelerating */
  let i = 0, delay = 65, t = 0;
  (function shuffle(){
    paint(PERKS[i % 3], '#4A6E59'); i++; tick();
    t += delay; delay *= 1.19;
    if (t < 1250) setTimeout(shuffle, delay);
    else { paint(result.perk, '#EAFFF3'); card.classList.add('pop'); blip(660,0.14,'triangle',0.07);
           setTimeout(()=>climb(0), 620); }
  })();

  /* 2. climb the minutes */
  function climb(n){
    if (n > 2) return finish();
    P[n].classList.add('armed');
    const wait = [520, 900, 1400][n];
    setTimeout(()=>{
      P[n].classList.remove('armed');
      if (n <= result.tier){
        P[n].classList.add('lit');
        P[n].style.background = TIERS[n].col;
        P[n].style.borderColor = TIERS[n].col;
        blip(560 + n*180, 0.2, 'triangle', 0.085);
        climb(n+1);
      } else {
        P[n].classList.add('dud'); dud();
        finish();
      }
    }, wait);
  }
  function finish(){ fanfare(result.tier); setTimeout(()=>showResult(result), 700); }
}

/* ── result + activation ─────────────────────────────────────────────────── */
const mmss = ms => { const s = Math.max(0,Math.ceil(ms/1000));
                     return Math.floor(s/60)+':'+String(s%60).padStart(2,'0'); };

function showResult(result){
  const t = TIERS[result.tier];
  const prev = current;                      // what this roll is about to bin
  CASE.innerHTML = '';
  const r = el('div','result pop');

  /* The replacement line is the honest part of the screen: it tells you what
     the gamble actually cost, in seconds, before you close it. */
  let swap = '';
  if (prev){
    const leftMs = Math.max(0, prev.until - performance.now());
    const better = result.tier > prev.tier;
    const same   = result.tier === prev.tier;
    const col    = better ? 'var(--phos)' : same ? 'var(--rowdim)' : '#C8555A';
    const verb   = better ? 'TRADED UP FROM' : same ? 'SWAPPED' : 'THREW AWAY';
    swap = '<div class="swap" style="color:'+col+'">'+verb+' &nbsp;'+prev.perk.ic+' '+
           prev.perk.tag+' · '+TIERS[prev.tier].label+' &nbsp;(<b>'+mmss(leftMs)+'</b> was left)</div>';
  }

  r.innerHTML =
    '<div class="caption">THE CAT COUGHS SOMETHING UP</div>'+
    '<div class="big" style="color:'+t.col+'">'+result.perk.tag+' &nbsp;·&nbsp; '+t.label+'</div>'+
    '<div class="eff">'+result.perk.blurb+'</div>'+
    swap+
    '<div class="odds">'+(result.p*100).toFixed(1)+'% CHANCE &nbsp;·&nbsp; 1 IN '+Math.round(1/result.p)+'</div>';
  const b = el('button',null,'CLOSE');
  b.onclick = () => { CASE.classList.remove('on'); busy = false; say('Mrrrp.', [['Bye.', reset]]); };
  r.appendChild(b);
  CASE.appendChild(r);

  /* One slot. The new timer is the NEW tier's full length — not the old one
     topped up, and never added to it. */
  const total = (result.tier+1)*60000;
  current = { perk:result.perk, tier:result.tier, total, until: performance.now()+total };
  if (napSecs > 0) napUntil = performance.now() + napSecs*1000;
  logRoll(result, prev);
}

/* HUD chip — exactly one, because there is exactly one perk */
(function hudLoop(){
  const hud = $('#hud');
  function frame(){
    const now = performance.now();
    if (current && current.until <= now) current = null;
    hud.innerHTML = '';
    if (current){
      const t = TIERS[current.tier];
      const f = Math.max(0,(current.until-now)/current.total);
      const c = el('div','chip');
      c.style.borderColor = t.col; c.style.color = t.col;
      c.innerHTML = current.perk.ic+' '+current.perk.tag+' <span style="color:var(--rowdim)">'+
                    mmss(current.until-now)+'</span>';
      const bar = el('div','bar','<i></i>');
      bar.querySelector('i').style.background = t.col;
      bar.querySelector('i').style.transform = 'scaleX('+f+')';
      c.appendChild(bar); hud.appendChild(c);
    }
    if (napSecs > 0 && napUntil > now){
      const c = el('div','chip');
      c.style.borderColor = 'var(--rowdim)'; c.style.color = 'var(--rowdim)';
      c.innerHTML = '💤 CAT NAPPING <span>'+mmss(napUntil-now)+'</span>';
      hud.appendChild(c);
    }
    requestAnimationFrame(frame);
  }
  frame();
})();

/* ── conversation flow ───────────────────────────────────────────────────── */
const plate = $('#plate'), lineEl = $('#line'), choicesEl = $('#choices'),
      gridEl = $('#fishgrid'), prompt = $('#prompt'), cat = $('#cat');

let typeTimer = null;
function say(text, choices, showGrid){
  plate.classList.add('on');
  choicesEl.innerHTML = ''; gridEl.hidden = true;
  clearInterval(typeTimer);
  let i = 0; lineEl.textContent = '';
  typeTimer = setInterval(()=>{
    lineEl.textContent = text.slice(0, ++i);
    if (i >= text.length){
      clearInterval(typeTimer);
      (choices||[]).forEach(([label, fn]) => {
        const b = el('button',null,label); b.onclick = fn; choicesEl.appendChild(b);
      });
      if (showGrid) drawGrid();
    }
  }, 18);
}
function reset(){
  plate.classList.remove('on'); gridEl.hidden = true;
  cat.classList.add('outlined'); prompt.classList.add('on');
}
function drawGrid(){
  gridEl.hidden = false; gridEl.innerHTML = '';

  /* No confirm box. You get told what is on the table and you choose anyway —
     a "are you sure?" prompt would sand the risk off the only gamble here. */
  if (current){
    const t = TIERS[current.tier];
    gridEl.appendChild(el('div','standing',
      '<span style="color:'+t.col+'">'+current.perk.ic+' '+current.perk.tag+' · '+
      TIERS[current.tier].label+'</span> is running &mdash; <b>'+mmss(current.until-performance.now())+
      '</b> left. Feeding the cat again <b>replaces it</b>, better or worse.'));
  }

  if (!bag.length){
    gridEl.appendChild(el('div','hint','Your pockets are empty. '+
      '<b style="cursor:pointer;text-decoration:underline" onclick="refill()">Catch some more</b>.'));
    gridEl.lastChild.style.gridColumn = '1/-1';
    return;
  }
  bag.forEach((f, idx)=>{
    const b = el('button','fishcard');
    b.innerHTML = '<div class="nm"><span class="pip" style="background:'+FISHCOL[f.t]+'"></span>'+f.n+'</div>'+
                  '<div class="wt">'+f.w+' lb</div>';
    b.onclick = () => { bag.splice(idx,1); plate.classList.remove('on');
                        meow(); setTimeout(()=>openCase(roll(), f.n), 260); };
    gridEl.appendChild(b);
  });
}
function refill(){
  bag = [];
  for (let i=0;i<12;i++){
    const r = Math.random();
    const tier = r < 0.58 ? 0 : r < 0.86 ? 1 : 2;
    const pool = FISH.filter(f=>f[1]===tier);
    const f = pool[Math.floor(Math.random()*pool.length)];
    bag.push({ n:f[0], t:tier, w: Math.round(f[2] + Math.pow(Math.random(),2.0)*(f[3]-f[2])) });
  }
  // Only redraw if the picker is actually open. Filling it while it is closed is
  // what left a populated grid sitting under the yes/no in the first place.
  if (!gridEl.hidden) drawGrid();
}
window.refill = refill;

/* click the cat / press F */
function talk(){
  if (busy || plate.classList.contains('on')) return;
  cat.classList.remove('outlined'); prompt.classList.remove('on');
  if (napSecs > 0 && napUntil > performance.now()){
    say('Zzz...', [['Leave it alone.', reset]]);
    return;
  }
  meow();
  say('Meow! Spare a fish?', [
    ['Yes.', () => say('Purrrr. Which one?', null, true)],
    ['No.',  () => say('...', [['Leave.', reset]])],
  ]);
}
cat.onclick = talk;
addEventListener('keydown', e => { if (e.key === 'f' || e.key === 'F') talk(); });

/* ── right-hand panel ────────────────────────────────────────────────────── */
function renderOdds(){
  const tw = tierWeights();
  const t = $('#oddstable');
  let h = '<tr><th>OUTCOME</th><th>CHANCE</th><th>1 IN</th></tr>';
  for (const p of PERKS) for (let i=0;i<3;i++){
    const pr = tw[i]/3;
    h += '<tr><td><span class="pip" style="background:'+TIERS[i].col+'"></span>'+
         p.short+' '+TIERS[i].label+'</td><td>'+(pr*100).toFixed(1)+'%</td><td>'+Math.round(1/pr)+'</td></tr>';
  }
  h += '<tr class="sep"><td>ANY 3-MIN</td><td style="color:var(--t3)">'+(tw[2]*100).toFixed(1)+
       '%</td><td style="color:var(--t3)">'+Math.round(1/tw[2])+'</td></tr>'+
       '<tr><td>ANY 2-MIN</td><td>'+(tw[1]*100).toFixed(1)+'%</td><td>'+Math.round(1/tw[1])+'</td></tr>'+
       '<tr><td>ANY 1-MIN</td><td>'+(tw[0]*100).toFixed(1)+'%</td><td>'+Math.round(1/tw[0])+'</td></tr>';
  t.innerHTML = h;
  $('#hardv').textContent = hardness + '×';
  $('#hardnote').innerHTML =
    'A 3-minute perk is <b>'+hardness+'&times;</b> rarer than a 1-minute one, and a 2-minute '+
    'sits exactly halfway up that ladder. At this setting you hand over about <b>'+
    Math.round(1/tierWeights()[2])+' fish</b> per 3-minute buff.';
}
/* What a re-roll does to the perk you are already holding. This is the table
   that decides whether the system has a decision in it or not. */
function renderReroll(){
  const tw = tierWeights();
  const rows = [0,1,2].map(T => {
    let up = 0, same = tw[T], down = 0;
    for (let i=0;i<3;i++){ if (i>T) up += tw[i]; else if (i<T) down += tw[i]; }
    return { T, up, same, down };
  });
  $('#rerolltable').innerHTML =
    '<tr><th>HOLDING</th><th>BETTER</th><th>SAME</th><th>WORSE</th></tr>' +
    rows.map(r =>
      '<tr><td><span class="pip" style="background:'+TIERS[r.T].col+'"></span>'+TIERS[r.T].label+'</td>'+
      '<td style="color:var(--phos)">'+(r.up*100).toFixed(0)+'%</td>'+
      '<td>'+(r.same*100).toFixed(0)+'%</td>'+
      '<td style="color:'+(r.down>0?'#C8555A':'var(--rowdim)')+'">'+(r.down*100).toFixed(0)+'%</td></tr>'
    ).join('');
  $('#rerollnote').innerHTML =
    'Note the top row: holding a 1-minute perk, a re-roll <b>can never lose you '+
    'duration</b> — worst case is another 1-minute with a fresh timer. So a 1-min '+
    'is always worth re-rolling, at any rarity setting. The only brake on that is '+
    'the price of a fish, or a cat that has to sleep it off.';
}
$('#hard').oninput = e => { hardness = parseFloat(e.target.value); renderOdds(); renderReroll(); };
$('#nap').oninput = e => {
  napSecs = parseInt(e.target.value,10);
  $('#napv').textContent = napSecs === 0 ? 'OFF' : napSecs + 's';
};

$('#sim').onclick = () => {
  const N = 10000, count = {};
  for (let i=0;i<N;i++){ const r = roll(); const k = r.perk.short+'|'+r.tier;
    count[k] = (count[k]||0)+1; }
  let h = '<tr class="sep"><th>10,000 ROLLS</th><th>GOT</th><th>%</th></tr>';
  for (const p of PERKS) for (let i=0;i<3;i++){
    const c = count[p.short+'|'+i]||0;
    h += '<tr><td><span class="pip" style="background:'+TIERS[i].col+'"></span>'+p.short+' '+TIERS[i].label+
         '</td><td>'+c+'</td><td>'+(c/N*100).toFixed(1)+'%</td></tr>';
  }
  let t3 = 0; for (const p of PERKS) t3 += count[p.short+'|2']||0;
  h += '<tr class="sep"><td style="color:var(--t3)">ANY 3-MIN</td><td style="color:var(--t3)">'+t3+
       '</td><td style="color:var(--t3)">'+(t3/N*100).toFixed(1)+'%</td></tr>';
  $('#simtable').innerHTML = h;
};

function logRoll(r, prev){
  const l = $('#log');
  let tail = '';
  if (prev){
    const better = r.tier > prev.tier, same = r.tier === prev.tier;
    tail = ' <span style="width:auto;height:auto;color:'+
           (better?'var(--phos)':same?'var(--rowdim)':'#C8555A')+'">'+
           (better?'↑ up from':same?'= swapped':'↓ lost')+' '+TIERS[prev.tier].label+'</span>';
  }
  const d = el('div', null, '<span style="background:'+TIERS[r.tier].col+'"></span>'+
             r.perk.short+' · '+TIERS[r.tier].label+tail);
  l.prepend(d);
  while (l.children.length > 40) l.lastChild.remove();
}

$('#perkdocs').innerHTML = PERKS.map(p =>
  '<p class="hint"><b style="color:'+'var(--phos)'+'">'+p.ic+' '+p.name+'</b><br>'+
  p.blurb+'<br><span style="color:#3f5f50">'+p.hook+'</span></p>').join('');

$('#questions').innerHTML = [
  ['DECIDED · one slot','No stacking. A new roll replaces the running perk and sets the timer to the NEW tier\'s full length — never topped up, never added. Built.'],
  ['DECIDED · re-rolling is allowed','You can feed a cat with a perk already running. The picker tells you what is on the table and how long is left, and then lets you do it anyway — no "are you sure?" box, because the risk is the point.'],
  ['DECIDED · duration only','1/2/3 min are the same strength. A 3-min +20% luck is a longer 1-min +20% luck, nothing more.'],
  ['STILL OPEN · cat cooldown','This got more important, not less, once re-rolling became legal. See SHOULD I RE-ROLL above: a 1-minute perk is always worth re-rolling because you cannot lose duration. Without something slowing the cat down, the optimal play is to stand there feeding it until a 3-min drops. My pick is the CAT NAP slider (30-60s) — diegetic, needs no UI, and the cat falling asleep reads instantly.'],
  ['STILL OPEN · does a rare fish deserve better odds?','Right now any fish rolls the same table, exactly as you said. That makes cats a clean dump for junk commons, which is good. Worth knowing: a rare sells for ~$100, so nobody will ever feed one — which is probably fine, and probably the point.'],
].map(([q,a]) => '<p class="q'+(q.startsWith('DECIDED')?' done':'')+'"><b>'+q+'</b><br>'+a+'</p>').join('');

/* ── boot ────────────────────────────────────────────────────────────────── */
document.querySelectorAll('#opts button').forEach(b => b.onclick = () => {
  document.querySelectorAll('#opts button').forEach(x=>x.classList.remove('on'));
  b.classList.add('on'); option = b.dataset.opt;
});
refill(); renderOdds(); renderReroll(); reset();
