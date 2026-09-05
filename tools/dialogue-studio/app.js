/* Dialogue Studio — roster / editor / player. Vanilla JS, no build step.
 *
 * The PLAYER here mirrors NpcGraphWalker.cs rule for rule:
 *   start node → routes (first match jumps, lines skipped) → onEnter effects →
 *   lines (one at random if pickRandomLine) → visible responses (requiresFlag /
 *   hiddenIfFlag / conditions) → none visible ⇒ nextNodeId ("" = end) →
 *   pick ⇒ effects + hint track ⇒ nextNodeId.
 * Keep the two in lockstep when either changes.
 *
 * The EDITOR is a "script" view: every node is a card you type into directly
 * (what the NPC says, what the player can reply, where each reply goes).
 * Routes / effects / conditions live under "Details" so the common job —
 * write lines, add replies, branch — needs no extra clicks.
 */
'use strict';

// ───────────────────────── utils ─────────────────────────
const $ = (s, el = document) => el.querySelector(s);
function h(tag, attrs, ...children) {
  const el = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v === null || v === undefined || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'html') el.innerHTML = v;
    else if (k.startsWith('on')) el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === 'value') el.value = v;
    else if (k === 'checked') el.checked = !!v;
    else if (k === 'disabled') el.disabled = !!v;
    else if (k === 'selected') el.selected = !!v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const c of children.flat(Infinity)) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return el;
}
const clone = o => JSON.parse(JSON.stringify(o));
const esc = s => String(s ?? '');
function trunc(s, n) { s = esc(s); return s.length > n ? s.slice(0, n - 1) + '…' : s; }
function lsGet(k, d) { try { const v = localStorage.getItem('ds.' + k); return v === null ? d : JSON.parse(v); } catch { return d; } }
function lsSet(k, v) { try { localStorage.setItem('ds.' + k, JSON.stringify(v)); } catch { /* ignore */ } }

function toast(msg, kind = 'ok', ms = 3800) {
  const t = h('div', { class: 'toast ' + (kind === 'ok' ? '' : kind) }, msg);
  $('#toasts').append(t);
  setTimeout(() => t.remove(), ms);
}

function modal({ title, body, buttons }) {
  return new Promise(resolve => {
    const root = $('#modal-root');
    root.innerHTML = '';
    const close = v => { root.hidden = true; root.innerHTML = ''; resolve(v); };
    const box = h('div', { class: 'modal' },
      h('h2', null, title),
      body,
      h('div', { class: 'buttons' }, buttons.map(b =>
        h('button', { class: b.primary ? 'primary' : (b.danger ? 'danger' : ''), onClick: () => close(b.value) }, b.label))));
    root.append(box);
    root.hidden = false;
    root.onclick = e => { if (e.target === root) close(null); };
    const first = box.querySelector('input,textarea,select');
    if (first) first.focus();
  });
}

async function api(path, opts = {}) {
  const r = await fetch(path, { cache: 'no-store', ...opts });
  const text = await r.text();
  let data = null;
  try { data = JSON.parse(text); } catch { data = { error: text }; }
  if (!r.ok) throw new Error((data && data.error) || r.statusText);
  return data;
}

// ───────────────────────── state ─────────────────────────
const S = {
  vocab: null, rosterData: null,
  file: null, graph: null, savedText: '', selected: null,
  undo: [], redo: [], preEdit: null,
  view: { x: 20, y: 20, k: 0.8 },
  player: null,
  ui: { showAdvanced: lsGet('showAdvanced', false), showMap: lsGet('showMap', true), sideTab: 'map', howtoClosed: lsGet('howtoClosed', false), openAdv: new Set(), openReply: new Set() },
  scrollTo: null,
};

function isDirty() { return S.graph && serializeGraph(S.graph) !== S.savedText; }
window.addEventListener('beforeunload', e => { if (isDirty()) { e.preventDefault(); e.returnValue = ''; } });

// ───────────────────────── schema helpers ─────────────────────────
function normalizeGraph(g, file) {
  g.id = g.id || (file ? file.replace(/\.json$/, '') : '');
  g.kind = g.kind || (file && file.startsWith('conv_') ? 'phone' : 'npc');
  g.displayName = g.displayName || (g.kind === 'phone' ? 'AI' : g.id.replace(/^npc_/, ''));
  g.testPresets = (g.testPresets || []).map(p => ({ name: p.name || '', flags: p.flags || [], money: p.money ?? -1, items: p.items || [], probes: p.probes || [] }));
  g.nodes = (g.nodes || []).map(normalizeNode);
  return g;
}
function normalizeNode(n) {
  n.id = n.id || '';
  n.speaker = n.speaker || '';
  n.lines = n.lines || [];
  n.responses = (n.responses || []).map(normalizeResponse);
  n.routes = (n.routes || []).map(r => ({ conditions: (r.conditions || []).map(normalizeCond), nextNodeId: r.nextNodeId || 'end' }));
  n.onEnter = (n.onEnter || []).map(normalizeEff);
  n.pickRandomLine = !!n.pickRandomLine;
  n.nextNodeId = n.nextNodeId || '';
  return n;
}
function normalizeResponse(r) {
  return {
    buttonText: r.buttonText || '', nextNodeId: r.nextNodeId || 'end',
    effects: (r.effects || []).map(normalizeEff), conditions: (r.conditions || []).map(normalizeCond),
    startHintTrack: r.startHintTrack || '', requiresFlag: r.requiresFlag || '', hiddenIfFlag: r.hiddenIfFlag || '',
  };
}
function normalizeCond(c) { return { kind: c.kind || 'Flag', arg: c.arg || '', num: +c.num || 0, negate: !!c.negate }; }
function normalizeEff(e) { return { kind: e.kind || 'SetFlag', strArg: e.strArg || '', numArg: +e.numArg || 0, boolArg: e.boolArg === undefined ? (e.kind === 'SetFlag') : !!e.boolArg }; }

function serializeGraph(g) {
  const out = { id: g.id, kind: g.kind, displayName: g.displayName, testPresets: g.testPresets.map(p => ({ name: p.name, flags: p.flags, money: p.money, items: p.items, probes: p.probes })), nodes: [] };
  for (const n of g.nodes) {
    const o = { id: n.id, speaker: n.speaker || g.displayName, lines: n.lines.slice() };
    if (n.pickRandomLine) o.pickRandomLine = true;
    if (n.routes.length) o.routes = n.routes.map(r => ({ conditions: r.conditions.map(cleanCond), nextNodeId: r.nextNodeId || 'end' }));
    if (n.onEnter.length) o.onEnter = n.onEnter.map(cleanEff);
    if (n.nextNodeId && n.nextNodeId !== 'end') o.nextNodeId = n.nextNodeId;
    o.responses = n.responses.map(r => {
      const ro = { buttonText: r.buttonText, nextNodeId: r.nextNodeId || 'end' };
      if (r.conditions.length) ro.conditions = r.conditions.map(cleanCond);
      if (r.effects.length) ro.effects = r.effects.map(cleanEff);
      if (r.startHintTrack) ro.startHintTrack = r.startHintTrack;
      if (r.requiresFlag) ro.requiresFlag = r.requiresFlag;
      if (r.hiddenIfFlag) ro.hiddenIfFlag = r.hiddenIfFlag;
      return ro;
    });
    out.nodes.push(o);
  }
  return JSON.stringify(out, null, 2) + '\n';
}
const cleanCond = c => ({ kind: c.kind, arg: c.arg, num: c.num, negate: c.negate });
const cleanEff = e => ({ kind: e.kind, strArg: e.strArg, numArg: e.numArg, boolArg: e.boolArg });

function startNode(g) { return g.nodes.find(n => n.id === 'start') || g.nodes[0] || null; }
function findNode(g, id) { return g.nodes.find(n => n.id === id) || null; }
/// Extra entry points the NPC's script starts at by name (roster.json "entryNodes",
/// e.g. the fish vendor's "bounty"). They count as roots for reachability + layout.
function entryNodeIds(g) {
  const meta = S.rosterData && S.rosterData.roster && S.rosterData.roster.npcs ? S.rosterData.roster.npcs[g.id] : null;
  return (meta && meta.entryNodes) || [];
}
function rootIds(g) { const s = startNode(g); return [...new Set([s ? s.id : null, ...entryNodeIds(g)].filter(id => id && findNode(g, id)))]; }
function reachableFromRoots(g, skipId = null) { const all = new Set(); for (const r of rootIds(g)) for (const id of reachableFrom(g, r, skipId)) all.add(id); return all; }
function nodeTargets(n) {
  const t = [];
  n.routes.forEach(r => t.push(r.nextNodeId));
  n.responses.forEach(r => t.push(r.nextNodeId));
  if (n.nextNodeId) t.push(n.nextNodeId);
  return t.filter(x => x && x !== 'end');
}
function reachableFrom(g, fromId, skipId = null) {
  const seen = new Set(); const q = [fromId];
  while (q.length) {
    const id = q.shift();
    if (!id || seen.has(id) || id === skipId) continue;
    const n = findNode(g, id); if (!n) continue;
    seen.add(id);
    nodeTargets(n).forEach(t => q.push(t));
  }
  return seen;
}
/// Reading order for the script view: breadth-first from the roots, then anything unreachable.
function walkOrder(g) {
  const order = []; const seen = new Set();
  const q = rootIds(g).slice();
  while (q.length) {
    const id = q.shift();
    if (seen.has(id)) continue;
    const n = findNode(g, id); if (!n) continue;
    seen.add(id); order.push(n);
    nodeTargets(n).forEach(t => { if (!seen.has(t)) q.push(t); });
  }
  const orphans = g.nodes.filter(n => !seen.has(n.id));
  return { order, orphans };
}
function condSummary(c) {
  const v = S.vocab;
  const k = (v && v.conditionKinds[c.kind]) || { uses: ['arg', 'num'] };
  let s = c.kind;
  if (c.kind === 'Flag') s = c.arg || '?';
  else if (c.kind === 'Probe') s = 'probe:' + (c.arg || '?');
  else if (c.kind === 'MoneyAtLeast') s = 'money ≥ ' + c.num;
  else if (c.kind === 'HasItem') s = 'has ' + (c.arg || '?') + (c.num > 1 ? ' ×' + c.num : '');
  else if (c.kind === 'CounterAtLeast') s = (c.arg || '?') + ' ≥ ' + c.num;
  else if (c.kind === 'ObjectiveDone') s = 'done:' + (c.arg || '?');
  else if (c.kind === 'Chance') s = c.num + '% chance';
  else s = c.kind + (k.uses.includes('arg') ? ' ' + c.arg : '') + (k.uses.includes('num') ? ' ' + c.num : '');
  return (c.negate ? 'NOT ' : '') + s;
}
function condsSummary(list) { return list.length ? list.map(condSummary).join(' & ') : 'always'; }
function effSummary(e) {
  switch (e.kind) {
    case 'SetFlag': return (e.boolArg ? '+' : '−') + (e.strArg || '?');
    case 'AddMoney': return '+$' + e.numArg;
    case 'SpendMoney': return '−$' + e.numArg;
    case 'GiveItem': return '+' + (e.strArg || '?') + (e.numArg > 1 ? ' ×' + e.numArg : '');
    case 'TakeItem': return '−' + (e.strArg || '?') + (e.numArg > 1 ? ' ×' + e.numArg : '');
    case 'Custom': return 'do:' + (e.strArg || '?');
    case 'HalSay': return 'HAL: "' + trunc(e.strArg, 24) + '"';
    default: return e.kind + (e.strArg ? ' ' + e.strArg : '') + (e.numArg ? ' ' + e.numArg : '');
  }
}

// ───────────────────────── routing ─────────────────────────
window.addEventListener('hashchange', route);
async function boot() {
  try { S.vocab = await api('/api/vocab'); } catch (e) { toast('vocab.json failed: ' + e.message, 'err'); S.vocab = { conditionKinds: {}, effectKinds: {}, flags: [], items: [], probes: {}, actions: {}, counters: [], objectives: [], hintTracks: [], storySteps: [] }; }
  try { S.rosterData = await api('/api/roster'); } catch { /* roster view reports it */ }
  route();
}
async function route() {
  const hash = location.hash || '#/';
  const m = hash.match(/^#\/(edit|play)\/([^?]+)(?:\?(.*))?$/);
  if (S.player) { S.player.cancel(); S.player = null; }
  document.onkeydown = null;
  if (!m) return showRoster();
  const file = decodeURIComponent(m[2]);
  const q = new URLSearchParams(m[3] || '');
  if (S.file !== file || !S.graph) {
    try {
      const raw = await api('/api/file/' + encodeURIComponent(file));
      S.graph = normalizeGraph(raw, file);
      S.file = file; S.savedText = serializeGraph(S.graph);
      S.undo = []; S.redo = []; S.selected = null; S.view = { x: 20, y: 20, k: 0.8 };
      S.ui.openAdv = new Set(); S.ui.openReply = new Set();
    } catch (e) { toast('Could not open ' + file + ': ' + e.message, 'err'); location.hash = '#/'; return; }
  }
  if (m[1] === 'edit') showEditor(); else showPlayer(q.get('node') || null);
}
function setTopbar(mid, right) {
  const M = $('#topbar-mid'), R = $('#topbar-right');
  M.innerHTML = ''; R.innerHTML = '';
  mid.forEach(x => M.append(x)); right.forEach(x => R.append(x));
}

// ───────────────────────── roster ─────────────────────────
async function showRoster() {
  S.file = null; S.graph = null;
  setTopbar([], [h('button', { class: 'primary', onClick: newNpcDialog }, '＋ New NPC / conversation')]);
  const view = $('#view'); view.innerHTML = '';
  let data;
  try { data = await api('/api/roster'); } catch (e) { view.append(h('div', { class: 'roster' }, h('p', { class: 'err' }, 'Server error: ' + e.message))); return; }
  S.rosterData = data;
  const files = new Map(data.files.map(f => [f.file, f]));
  const npcMeta = data.roster.npcs || {}, phoneMeta = data.roster.phone || {};

  const npcCards = [];
  const seen = new Set();
  for (const [id, meta] of Object.entries(npcMeta)) {
    const f = files.get(id + '.json'); seen.add(id + '.json');
    npcCards.push(card({ id, file: f ? f.file : null, info: f, meta }));
  }
  for (const f of data.files) if (f.kind === 'npc' && !seen.has(f.file)) npcCards.push(card({ id: f.id || f.file.replace('.json', ''), file: f.file, info: f, meta: { name: f.displayName || f.id, where: '', script: '', hook: 'full', notes: 'No roster entry yet (tools/dialogue-studio/roster.json).' } }));
  const phoneCards = data.files.filter(f => f.kind === 'phone').map(f => {
    const meta = phoneMeta[f.id] || phoneMeta[f.file.replace('.json', '')] || {};
    return card({ id: f.id, file: f.file, info: f, meta: { name: meta.name || f.id, where: 'Phone / HAL', script: 'Story/DialogueRunner.cs', hook: 'phone', notes: meta.notes || '' } });
  });

  view.append(h('div', { class: 'roster' },
    h('h1', null, 'Who talks'),
    h('div', { class: 'sub' }, h('b', null, 'Edit'), ' opens the talk as a script you type into — lines, replies, and where each reply goes. ', h('b', null, 'Start'), ' plays it like the game would. Saving writes straight into the game; in the Unity Editor the change is live on the next talk.'),
    h('h2', null, 'World NPCs'), h('div', { class: 'cards' }, npcCards),
    h('h2', null, 'Phone / HAL conversations'), h('div', { class: 'cards' }, phoneCards)));

  function card({ id, file, info, meta }) {
    const hook = meta.hook || 'full';
    const badge = hook === 'phone' ? h('span', { class: 'badge phone' }, 'phone')
      : hook === 'full' ? h('span', { class: 'badge full' }, 'whole talk')
      : hook === 'greeting' ? h('span', { class: 'badge greeting' }, 'spoken part only')
      : h('span', { class: 'badge none' }, 'no graph hook');
    const canOpen = !!file && !(info && info.error);
    return h('div', { class: 'card' + (canOpen ? '' : ' disabled') },
      h('div', { class: 'name' }, meta.name || id, badge),
      meta.where ? h('div', { class: 'where' }, meta.where) : null,
      info ? h('div', { class: 'meta' }, info.nodes + ' node' + (info.nodes === 1 ? '' : 's') + ' · ' + info.file) : h('div', { class: 'meta' }, 'no file'),
      info && info.error ? h('div', { class: 'err' }, 'File error: ' + info.error) : null,
      meta.notes ? h('div', { class: 'notes' }, meta.notes) : null,
      h('div', { class: 'actions' },
        h('button', { class: 'primary', disabled: !canOpen, onClick: () => location.hash = '#/edit/' + encodeURIComponent(file) }, '✎ Edit'),
        h('button', { disabled: !canOpen, onClick: () => location.hash = '#/play/' + encodeURIComponent(file) }, '▶ Start')));
  }
}

async function newNpcDialog() {
  const idIn = h('input', { placeholder: 'e.g. grumpy_fisher  (becomes npc_grumpy_fisher.json)' });
  const nameIn = h('input', { placeholder: 'Display name, e.g. Grumpy Fisher' });
  const kindSel = h('select', null, h('option', { value: 'npc' }, 'World NPC (npc_…)'), h('option', { value: 'phone' }, 'Phone / HAL conversation (conv_…)'));
  const body = h('div', null,
    h('div', { class: 'field' }, h('span', null, 'Id (lower-case, letters/numbers/underscore)'), idIn),
    h('div', { class: 'field' }, h('span', null, 'Name shown on the speaker plate'), nameIn),
    h('div', { class: 'field' }, h('span', null, 'Type'), kindSel),
    h('p', { class: 'help' }, 'To wire a new world NPC in Unity: put an AuthoredNPCTalk (or a subclass) on the NPC and set its graphId to the id — or just name the NPC the same and leave graphId empty (Floorbin → npc_floorbin).'));
  const ok = await modal({ title: 'New dialogue file', body, buttons: [{ label: 'Cancel', value: null }, { label: 'Create', value: true, primary: true }] });
  if (!ok) return;
  try {
    const r = await api('/api/new', { method: 'POST', body: JSON.stringify({ id: idIn.value, displayName: nameIn.value, kind: kindSel.value }) });
    toast('Created ' + r.file);
    location.hash = '#/edit/' + encodeURIComponent(r.file);
  } catch (e) { toast(e.message, 'err'); }
}

// ───────────────────────── editor (script view) ─────────────────────────
function showEditor() {
  const view = $('#view'); view.innerHTML = '';
  view.append(h('div', { class: 'editor2' + (S.ui.showMap ? '' : ' nomap'), id: 'editor2' },
    h('div', { class: 'script', id: 'script' }, h('div', { class: 'script-inner', id: 'script-inner' })),
    h('div', { class: 'sidepane', id: 'sidepane' },
      h('div', { class: 'sidetabs', id: 'sidetabs' }),
      h('div', { class: 'sidebody', id: 'sidebody' }))));
  renderTopbarEditor();
  renderScript();
  renderSide();
  document.onkeydown = editorKeys;
}
function renderTopbarEditor() {
  const g = S.graph;
  setTopbar([
    h('strong', null, g.displayName || g.id),
    h('span', { class: 'title-file mono' }, S.file),
    h('span', { id: 'dirty-mark', class: 'dirty' }, isDirty() ? '● unsaved' : ''),
  ], [
    h('label', { class: 'inline', title: 'Show routes, effects, conditions and speaker fields on every card' }, h('input', { type: 'checkbox', checked: S.ui.showAdvanced, onChange: e => { S.ui.showAdvanced = e.target.checked; lsSet('showAdvanced', S.ui.showAdvanced); renderScript(); } }), 'details'),
    h('label', { class: 'inline' }, h('input', { type: 'checkbox', checked: S.ui.showMap, onChange: e => { S.ui.showMap = e.target.checked; lsSet('showMap', S.ui.showMap); $('#editor2').classList.toggle('nomap', !S.ui.showMap); if (S.ui.showMap) renderSide(); } }), 'map'),
    h('button', { onClick: undo, disabled: !S.undo.length, title: 'Ctrl+Z' }, '↶ Undo'),
    h('button', { onClick: redo, disabled: !S.redo.length, title: 'Ctrl+Y' }, '↷ Redo'),
    h('button', { onClick: () => { if (isDirty()) { toast('Save first so the player runs what you see.', 'warn'); } location.hash = '#/play/' + encodeURIComponent(S.file); } }, '▶ Play'),
    h('button', { class: 'primary', onClick: save, title: 'Ctrl+S' }, 'Save'),
  ]);
}
function refreshDirty() { const d = $('#dirty-mark'); if (d) d.textContent = isDirty() ? '● unsaved' : ''; }
function editorKeys(e) {
  const inInput = /INPUT|TEXTAREA|SELECT/.test(document.activeElement && document.activeElement.tagName);
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); save(); }
  else if (!inInput && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { e.preventDefault(); undo(); }
  else if (!inInput && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') { e.preventDefault(); redo(); }
}

// undo / mutation plumbing
function snapshot() { return serializeGraph(S.graph); }
function pushUndo(snap = snapshot()) { S.undo.push(snap); if (S.undo.length > 100) S.undo.shift(); S.redo = []; }
function restore(text) { S.graph = normalizeGraph(JSON.parse(text), S.file); if (S.selected && !findNode(S.graph, S.selected)) S.selected = null; renderAll(); }
function undo() { if (!S.undo.length) return; S.redo.push(snapshot()); restore(S.undo.pop()); }
function redo() { if (!S.redo.length) return; S.undo.push(snapshot()); restore(S.redo.pop()); }
function mutate(fn) { pushUndo(); fn(); renderAll(); }
function renderAll() { renderTopbarEditor(); renderScript(); renderSide(); }
// text fields: remember the pre-edit state on focus, push it on change if anything changed
function bindText(el, get, set, { onChanged = null } = {}) {
  el.addEventListener('focus', () => { S.preEdit = snapshot(); });
  el.addEventListener('input', () => { set(el.value); refreshDirty(); });
  el.addEventListener('change', () => {
    set(el.value);
    const now = snapshot();
    if (S.preEdit && S.preEdit !== now) { S.undo.push(S.preEdit); S.redo = []; }
    S.preEdit = null;
    renderTopbarEditor();
    if (onChanged) onChanged(); else if (S.ui.sideTab === 'map') renderGraph();
  });
  return el;
}
function autosize(ta) {
  const fit = () => { ta.style.height = 'auto'; ta.style.height = Math.max(40, ta.scrollHeight + 2) + 'px'; };
  ta.addEventListener('input', fit);
  requestAnimationFrame(fit);
  return ta;
}

async function save() {
  if (!S.graph) return;
  const issues = validate(S.graph).filter(i => i.level === 'error');
  if (issues.length) {
    const go = await modal({ title: 'There are errors', body: h('div', null, h('p', null, 'The game will still load the file, but these will misbehave:'), h('ul', { class: 'issues' }, issues.map(i => h('li', { class: 'error' }, i.msg)))), buttons: [{ label: 'Go back', value: false }, { label: 'Save anyway', value: true, danger: true }] });
    if (!go) return;
  }
  const text = serializeGraph(S.graph);
  try {
    const r = await api('/api/file/' + encodeURIComponent(S.file), { method: 'PUT', body: text });
    S.savedText = text;
    renderTopbarEditor();
    toast('Saved ' + S.file + (r.backup ? ' (backup kept)' : '') + '. In the Unity Editor it is live on the next talk.');
  } catch (e) { toast('Save failed: ' + e.message, 'err', 7000); }
}

// ── mutations ──
function uniqueId(base) { let id = base, i = 2; while (findNode(S.graph, id) || id === 'end') id = base + '_' + (i++); return id; }
function newNodeObj(id) { return normalizeNode({ id, speaker: S.graph.displayName, lines: [''], responses: [] }); }
/// Insert a new node after `afterNode` (or at the end) and scroll to it once rendered.
function insertNode(id, afterNode = null) {
  const nid = uniqueId(id || 'node');
  const created = newNodeObj(nid);
  const idx = afterNode ? S.graph.nodes.indexOf(afterNode) : -1;
  if (idx >= 0) S.graph.nodes.splice(idx + 1, 0, created); else S.graph.nodes.push(created);
  S.scrollTo = nid; S.selected = nid;
  return nid;
}
function renameNode(oldId, newId) {
  newId = newId.trim().replace(/\s+/g, '_');
  if (!newId || newId === 'end') { toast('Node id can\'t be empty or "end".', 'warn'); return false; }
  if (newId !== oldId && findNode(S.graph, newId)) { toast('There is already a node called ' + newId, 'warn'); return false; }
  if (newId === oldId) return true;
  mutate(() => {
    for (const n of S.graph.nodes) {
      if (n.id === oldId) n.id = newId;
      n.routes.forEach(r => { if (r.nextNodeId === oldId) r.nextNodeId = newId; });
      n.responses.forEach(r => { if (r.nextNodeId === oldId) r.nextNodeId = newId; });
      if (n.nextNodeId === oldId) n.nextNodeId = newId;
    }
    if (S.selected === oldId) S.selected = newId;
    S.scrollTo = newId;
  });
  return true;
}
function relinkTo(ids, target) {
  for (const n of S.graph.nodes) {
    n.routes.forEach(r => { if (ids.has(r.nextNodeId)) r.nextNodeId = target; });
    n.responses.forEach(r => { if (ids.has(r.nextNodeId)) r.nextNodeId = target; });
    if (ids.has(n.nextNodeId)) n.nextNodeId = target === 'end' ? '' : target;
  }
}
async function deleteNode(id) {
  const refs = S.graph.nodes.filter(n => n.id !== id && nodeTargets(n).includes(id)).map(n => n.id);
  const ok = await modal({ title: 'Delete "' + id + '"?', body: h('p', null, refs.length ? 'Replies pointing at it (from ' + refs.join(', ') + ') will end the conversation instead. Nodes after it stay.' : 'Nothing points at it.'), buttons: [{ label: 'Cancel', value: false }, { label: 'Delete', value: true, danger: true }] });
  if (!ok) return;
  mutate(() => { const ids = new Set([id]); relinkTo(ids, 'end'); S.graph.nodes = S.graph.nodes.filter(n => n.id !== id); if (S.selected === id) S.selected = null; });
}
async function deleteBranch(id) {
  const g = S.graph;
  const under = reachableFrom(g, id);
  const stillReachable = reachableFromRoots(g, id);
  const doomed = new Set([...under].filter(x => !stillReachable.has(x)));
  doomed.add(id);
  const list = [...doomed];
  const ok = await modal({ title: 'Delete this whole branch?', body: h('div', null, h('p', null, 'This deletes "' + id + '" and everything that can only be reached through it — ' + list.length + ' node(s):'), h('pre', { class: 'code' }, list.join('\n')), h('p', { class: 'muted small' }, 'Anything else that pointed into the branch will end the conversation instead. Undo (Ctrl+Z) brings it back.')), buttons: [{ label: 'Cancel', value: false }, { label: 'Delete ' + list.length + ' node(s)', value: true, danger: true }] });
  if (!ok) return;
  mutate(() => { relinkTo(doomed, 'end'); g.nodes = g.nodes.filter(n => !doomed.has(n.id)); if (doomed.has(S.selected)) S.selected = null; });
}
function duplicateNode(id) {
  const src = findNode(S.graph, id); if (!src) return;
  mutate(() => { const c = normalizeNode(clone(src)); c.id = uniqueId(id + '_copy'); S.graph.nodes.splice(S.graph.nodes.indexOf(src) + 1, 0, c); S.selected = c.id; S.scrollTo = c.id; });
}
async function renameDialog(n) {
  const inp = h('input', { value: n.id, class: 'mono' });
  const ok = await modal({ title: 'Rename node', body: h('div', null, h('p', { class: 'help' }, 'The id is just a label for linking; the player never sees it. Every reply pointing here updates automatically.'), inp), buttons: [{ label: 'Cancel', value: null }, { label: 'Rename', value: true, primary: true }] });
  if (ok) renameNode(n.id, inp.value);
}
async function nodeMenu(n) {
  const v = await modal({ title: 'Box "' + n.id + '"', body: h('p', { class: 'help' }, 'Deleting only this box re-points replies that led here to the end of the talk. Deleting the whole branch also removes every box that can only be reached through it. Undo (Ctrl+Z) brings either back.'), buttons: [
    { label: 'Cancel', value: null }, { label: 'Rename', value: 'rename' }, { label: 'Duplicate', value: 'dup' },
    { label: 'Delete this node only', value: 'del', danger: true }, { label: 'Delete this whole branch', value: 'branch', danger: true }] });
  if (v === 'rename') renameDialog(n);
  else if (v === 'dup') duplicateNode(n.id);
  else if (v === 'del') deleteNode(n.id);
  else if (v === 'branch') deleteBranch(n.id);
}

// ── script rendering ──
function scrollToCard(id, { flash = true, focus = false } = {}) {
  const card = document.getElementById('card-' + id);
  if (!card) return;
  card.scrollIntoView({ block: 'start', behavior: 'smooth' });
  if (flash) { card.classList.add('flash'); setTimeout(() => card.classList.remove('flash'), 900); }
  if (focus) { const ta = card.querySelector('textarea'); if (ta) setTimeout(() => ta.focus(), 250); }
}
function selectCard(id) {
  if (S.selected === id) return;
  S.selected = id;
  if (S.ui.showMap && S.ui.sideTab === 'map') renderGraph();
}
function renderScript() {
  const pane = $('#script'), inner = $('#script-inner');
  if (!inner) return;
  const keepScroll = pane.scrollTop;
  inner.innerHTML = '';
  const g = S.graph;
  if (!S.ui.howtoClosed) {
    inner.append(h('div', { class: 'howto' },
      h('div', null, h('b', null, 'How this works.'), ' Each box is one thing ', g.displayName || 'the NPC', ' says, in order — first box at the top. Type the lines. Under it, add the replies the player can pick and choose where each reply goes: another box, the end, or ', h('b', null, '＋ new branch'), ' to write a fresh box for that reply.',
        h('ul', null,
          h('li', null, 'A box with no replies just continues to its "after that" box, or ends.'),
          h('li', null, 'Delete a reply with ✕. The ⋯ button on a box can rename it, or delete the box or its whole branch.'),
          h('li', null, 'Save (Ctrl+S) writes into the game. Talk to the NPC again in the Unity Editor and you hear it.'),
          h('li', null, 'Turn on "details" (top right) for the game logic: which version of the talk plays (routes), flags, money, items.'))),
      h('button', { class: 'small ghost close', onClick: () => { S.ui.howtoClosed = true; lsSet('howtoClosed', true); renderScript(); } }, '✕ got it')));
  }
  const { order, orphans } = walkOrder(g);
  order.forEach(n => inner.append(nodeCard(n, false)));
  if (orphans.length) {
    inner.append(h('div', { class: 'orphan-note' }, 'Nothing leads to these boxes — the player can\'t reach them. Point a reply at them, or delete them.'));
    orphans.forEach(n => inner.append(nodeCard(n, true)));
  }
  inner.append(h('div', { class: 'addbar', style: 'margin-top:8px' },
    h('button', { onClick: () => mutate(() => { insertNode('node'); }) }, '＋ New box (unlinked — point a reply at it)')));
  pane.scrollTop = keepScroll;
  if (S.scrollTo) { const id = S.scrollTo; S.scrollTo = null; setTimeout(() => scrollToCard(id, { focus: true }), 30); }
}

function nodeCard(n, orphan) {
  const g = S.graph;
  const start = startNode(g);
  const isStart = start && start.id === n.id;
  const isEntry = entryNodeIds(g).includes(n.id);
  const advOpen = S.ui.showAdvanced || S.ui.openAdv.has(n.id);
  const card = h('div', { class: 'ncard' + (isStart ? ' start' : '') + (orphan ? ' orphan' : ''), id: 'card-' + n.id });
  card.addEventListener('focusin', () => selectCard(n.id));
  card.addEventListener('click', () => selectCard(n.id));

  // header
  const logicChips = [];
  if (n.routes.length && !advOpen) logicChips.push(h('span', { class: 'chip', title: n.routes.map(r => 'if ' + condsSummary(r.conditions) + ' → ' + r.nextNodeId).join('\n'), onClick: () => { S.ui.openAdv.add(n.id); renderScript(); } }, '⇢ ' + n.routes.length + ' route' + (n.routes.length > 1 ? 's' : '') + ' first'));
  if (n.onEnter.length && !advOpen) logicChips.push(h('span', { class: 'chip', title: n.onEnter.map(effSummary).join(', '), onClick: () => { S.ui.openAdv.add(n.id); renderScript(); } }, '⚡ ' + n.onEnter.map(effSummary).join(', ')));
  card.append(h('div', { class: 'head' },
    isStart ? h('span', { class: 'badge start' }, 'START') : (isEntry ? h('span', { class: 'badge switch' }, 'ENTRY') : (orphan ? h('span', { class: 'badge orphan' }, 'unreachable') : null)),
    h('span', { class: 'nid', title: 'node id (click to rename)', onClick: () => renameDialog(n) }, n.id),
    logicChips,
    h('span', { class: 'spacer' }),
    h('button', { class: 'ghost', title: 'Play from this box', onClick: () => location.hash = '#/play/' + encodeURIComponent(S.file) + '?node=' + encodeURIComponent(n.id) }, '▶'),
    h('button', { class: 'ghost', title: 'Rename / duplicate / delete', onClick: () => nodeMenu(n) }, '⋯')));

  // lines
  const who = n.speaker || g.displayName;
  card.append(h('div', { class: 'lbl' }, h('span', { class: 'who' }, who), ' says', n.pickRandomLine ? h('span', { class: 'badge random' }, 'one at random') : null));
  n.lines.forEach((line, i) => {
    const ta = autosize(h('textarea', { value: line, rows: 1, placeholder: 'Type what ' + who + ' says…' }));
    bindText(ta, null, v => { n.lines[i] = v; });
    card.append(h('div', { class: 'line-row' }, ta,
      h('div', { class: 'btns' },
        h('button', { onClick: () => mutate(() => moveItem(n.lines, i, -1)), title: 'move up' }, '↑'),
        h('button', { onClick: () => mutate(() => moveItem(n.lines, i, 1)), title: 'move down' }, '↓'),
        h('button', { onClick: () => mutate(() => { n.lines.splice(i, 1); }), title: 'remove this line' }, '✕'))));
  });
  card.append(h('div', { class: 'addbar' },
    h('button', { onClick: () => mutate(() => { n.lines.push(''); S.scrollTo = null; }) }, '＋ line'),
    n.lines.length > 1 ? h('label', { class: 'inline' }, h('input', { type: 'checkbox', checked: n.pickRandomLine, onChange: e => mutate(() => { n.pickRandomLine = e.target.checked; }) }), 'say only one of these, at random') : null));

  // replies
  card.append(h('div', { class: 'lbl' }, 'player can reply'));
  if (!n.responses.length) card.append(h('div', { class: 'help' }, 'No replies — after the last line the talk continues to the box chosen below.'));
  n.responses.forEach((r, i) => {
    const key = n.id + '#' + i;
    const has = r.conditions.length || r.effects.length || r.requiresFlag || r.hiddenIfFlag || r.startHintTrack;
    const open = S.ui.showAdvanced || S.ui.openReply.has(key);
    const txt = bindText(h('input', { class: 'txt', value: r.buttonText, placeholder: 'What the player says…' }), null, v => { r.buttonText = v; });
    card.append(h('div', { class: 'reply' },
      h('span', { class: 'tri' }, '▸'), txt,
      h('span', { class: 'goto' }, 'then', targetSelect(r.nextNodeId, v => mutate(() => { r.nextNodeId = v; }), { forNode: n })),
      h('button', { class: 'gear' + (has ? ' has' : ''), title: has ? 'This reply has conditions or effects: ' + [...r.conditions.map(condSummary), ...r.effects.map(effSummary)].join(', ') : 'Conditions (when is this reply shown?) and effects (what happens when picked?)', onClick: () => { if (S.ui.openReply.has(key)) S.ui.openReply.delete(key); else S.ui.openReply.add(key); renderScript(); } }, has ? '⚑ ' + [...r.conditions.map(condSummary), ...r.effects.map(effSummary)].join(', ') : '⚙'),
      h('button', { class: 'x', title: 'remove this reply', onClick: () => mutate(() => { n.responses.splice(i, 1); }) }, '✕')));
    if (open) card.append(h('div', { class: 'reply-adv' },
      condList(r.conditions, 'show this reply only when'),
      effList(r.effects, 'when picked, also'),
      (g.kind === 'phone' || r.startHintTrack || r.requiresFlag || r.hiddenIfFlag) ? h('div', { class: 'row small', style: 'margin-top:6px' },
        h('label', { class: 'inline' }, 'requires flag', bindText(h('input', { value: r.requiresFlag, list: datalistFor('flags'), style: 'width:120px' }), null, v => { r.requiresFlag = v; })),
        h('label', { class: 'inline' }, 'hidden if flag', bindText(h('input', { value: r.hiddenIfFlag, list: datalistFor('flags'), style: 'width:120px' }), null, v => { r.hiddenIfFlag = v; })),
        h('label', { class: 'inline' }, 'hint track', bindText(h('input', { value: r.startHintTrack, list: datalistFor('hintTracks'), style: 'width:90px' }), null, v => { r.startHintTrack = v; }))) : null));
  });
  card.append(h('div', { class: 'addbar' },
    h('button', { onClick: () => mutate(() => { n.responses.push(normalizeResponse({ buttonText: '', nextNodeId: 'end' })); }) }, '＋ reply'),
    h('button', { class: 'primary', onClick: () => mutate(() => {
      const nid = insertNode(n.id + '_' + (n.responses.length + 1), n);
      n.responses.push(normalizeResponse({ buttonText: '', nextNodeId: nid }));
    }) }, '＋ reply → new branch')));

  // then
  if (!n.responses.length) card.append(h('div', { class: 'then' }, 'after that →', targetSelect(n.nextNodeId || 'end', v => mutate(() => { n.nextNodeId = v === 'end' ? '' : v; }), { forNode: n })));
  else if (n.nextNodeId && n.nextNodeId !== 'end') card.append(h('div', { class: 'then' }, 'if no reply is visible →', targetSelect(n.nextNodeId, v => mutate(() => { n.nextNodeId = v === 'end' ? '' : v; }), { forNode: n })));

  // advanced
  if (!S.ui.showAdvanced) card.append(h('div', { class: 'advtoggle', onClick: () => { if (S.ui.openAdv.has(n.id)) S.ui.openAdv.delete(n.id); else S.ui.openAdv.add(n.id); renderScript(); } }, (advOpen ? '▾ ' : '▸ ') + 'details — routes, effects, speaker' + (n.routes.length || n.onEnter.length ? ' (' + (n.routes.length + n.onEnter.length) + ')' : '')));
  if (advOpen) card.append(advancedBox(n));
  return card;
}

function advancedBox(n) {
  const g = S.graph;
  const box = h('div', { class: 'advbox' });
  box.append(h('div', { class: 'row' }, h('div', { class: 'field grow' }, h('span', null, 'Speaker plate (blank = ' + g.displayName + ')'), bindText(h('input', { value: n.speaker, placeholder: g.displayName }), null, v => { n.speaker = v; }, { onChanged: renderScript }))));
  box.append(h('div', { class: 'subhead' }, h('span', null, 'ROUTES — checked before the lines; the first that matches jumps somewhere else and this box\'s lines are skipped. Use on START to pick which version of the talk plays.'),
    h('button', { onClick: () => mutate(() => { n.routes.push({ conditions: [normalizeCond({})], nextNodeId: 'end' }); }) }, '＋ route')));
  n.routes.forEach((r, i) => box.append(h('div', { class: 'resp' },
    h('div', { class: 'head' }, h('span', { class: 'muted small' }, 'route ' + (i + 1)), h('span', { class: 'arrow' }, '→'), targetSelect(r.nextNodeId, v => mutate(() => { r.nextNodeId = v; }), { forNode: n }),
      h('button', { class: 'small ghost', onClick: () => mutate(() => moveItem(n.routes, i, -1)) }, '↑'), h('button', { class: 'small ghost', onClick: () => mutate(() => moveItem(n.routes, i, 1)) }, '↓'),
      h('button', { class: 'small ghost', onClick: () => mutate(() => { n.routes.splice(i, 1); }) }, '✕')),
    condList(r.conditions, r.conditions.length ? 'when ALL of these are true' : 'when… (no conditions = always taken)'))));
  box.append(effList(n.onEnter, 'WHEN THIS BOX STARTS — effects fired as the lines begin'));
  return box;
}

function targetSelect(value, onPick, { forNode = null } = {}) {
  const sel = h('select', null,
    h('option', { value: 'end', selected: !value || value === 'end' }, '— end of talk —'),
    S.graph.nodes.map(n => h('option', { value: n.id, selected: n.id === value }, n.id + (forNode && n.id === forNode.id ? ' (this box)' : ''))),
    h('option', { value: '__new__' }, '＋ new branch…'));
  sel.addEventListener('change', () => {
    if (sel.value === '__new__') {
      pushUndo();
      const nid = insertNode(forNode ? forNode.id + '_next' : 'node', forNode);
      onPick(nid);
      return;
    }
    onPick(sel.value);
  });
  sel.addEventListener('click', e => e.stopPropagation());
  return sel;
}
function datalistFor(listName) {
  if (!listName) return null;
  const v = S.vocab; let items = [];
  if (listName === 'probes') items = Object.keys(v.probes || {});
  else if (listName === 'actions') items = Object.keys(v.actions || {});
  else items = v[listName] || [];
  const used = S.graph ? collectRefs(S.graph) : null;
  if (used) {
    if (listName === 'flags') items = [...new Set([...items, ...used.flags])];
    if (listName === 'probes') items = [...new Set([...items, ...used.probes])];
    if (listName === 'actions') items = [...new Set([...items, ...used.actions])];
    if (listName === 'items') items = [...new Set([...items, ...used.items])];
  }
  const id = 'dl_' + listName;
  let dl = document.getElementById(id);
  if (!dl) { dl = h('datalist', { id }); document.body.append(dl); }
  dl.innerHTML = ''; items.forEach(x => dl.append(h('option', { value: x })));
  return id;
}
function condRow(list, idx) {
  const c = list[idx]; const kinds = S.vocab.conditionKinds;
  const spec = kinds[c.kind] || { uses: ['arg', 'num'], argList: '' };
  const kindSel = h('select', null, Object.entries(kinds).map(([k, s]) => h('option', { value: k, selected: k === c.kind }, s.label || k)), !kinds[c.kind] ? h('option', { value: c.kind, selected: true }, c.kind) : null);
  kindSel.addEventListener('change', () => mutate(() => { c.kind = kindSel.value; }));
  const argIn = h('input', { value: c.arg, placeholder: spec.argList ? spec.argList.replace(/s$/, '') : '', list: datalistFor(spec.argList), style: spec.uses.includes('arg') ? '' : 'visibility:hidden' });
  bindText(argIn, null, v => { c.arg = v; });
  const numIn = h('input', { type: 'number', value: c.num, style: spec.uses.includes('num') ? '' : 'visibility:hidden' });
  bindText(numIn, null, v => { c.num = +v || 0; });
  const neg = h('label', { class: 'not' }, h('input', { type: 'checkbox', checked: c.negate, onChange: e => mutate(() => { c.negate = e.target.checked; }) }), 'NOT');
  const del = h('button', { class: 'small ghost', title: 'remove', onClick: () => mutate(() => { list.splice(idx, 1); }) }, '✕');
  return h('div', { class: 'cond', title: spec.help || '' }, kindSel, argIn, numIn, neg, del);
}
function effRow(list, idx) {
  const e = list[idx]; const kinds = S.vocab.effectKinds;
  const spec = kinds[e.kind] || { uses: ['strArg', 'numArg'], argList: '' };
  const kindSel = h('select', null, Object.entries(kinds).map(([k, s]) => h('option', { value: k, selected: k === e.kind }, s.label || k)), !kinds[e.kind] ? h('option', { value: e.kind, selected: true }, e.kind) : null);
  kindSel.addEventListener('change', () => mutate(() => { e.kind = kindSel.value; if (e.kind === 'SetFlag') e.boolArg = true; }));
  const argIn = h('input', { value: e.strArg, placeholder: spec.argList ? spec.argList.replace(/s$/, '') : (e.kind === 'HalSay' ? 'what HAL says' : ''), list: datalistFor(spec.argList), style: spec.uses.includes('strArg') ? '' : 'visibility:hidden' });
  bindText(argIn, null, v => { e.strArg = v; });
  let third;
  if (spec.uses.includes('boolArg')) {
    third = h('select', null, h('option', { value: 'true', selected: e.boolArg }, 'ON'), h('option', { value: 'false', selected: !e.boolArg }, 'OFF'));
    third.addEventListener('change', () => mutate(() => { e.boolArg = third.value === 'true'; }));
  } else {
    third = h('input', { type: 'number', value: e.numArg, style: spec.uses.includes('numArg') ? '' : 'visibility:hidden' });
    bindText(third, null, v => { e.numArg = +v || 0; });
  }
  const del = h('button', { class: 'small ghost', title: 'remove', onClick: () => mutate(() => { list.splice(idx, 1); }) }, '✕');
  return h('div', { class: 'eff', title: spec.help || '' }, kindSel, argIn, third, h('span'), del);
}
function condList(list, label) {
  return h('div', null,
    h('div', { class: 'subhead' }, h('span', null, label), h('button', { onClick: () => mutate(() => { list.push(normalizeCond({})); }) }, '＋ condition')),
    list.map((c, i) => condRow(list, i)));
}
function effList(list, label) {
  return h('div', null,
    h('div', { class: 'subhead' }, h('span', null, label), h('button', { onClick: () => mutate(() => { list.push(normalizeEff({ kind: 'SetFlag', boolArg: true })); }) }, '＋ effect')),
    list.map((e, i) => effRow(list, i)));
}
function moveItem(arr, i, dir) { const j = i + dir; if (j < 0 || j >= arr.length) return; [arr[i], arr[j]] = [arr[j], arr[i]]; }

// ── side pane: map / settings / checks ──
function renderSide() {
  const tabs = $('#sidetabs'), body = $('#sidebody');
  if (!tabs || !body) return;
  tabs.innerHTML = ''; body.innerHTML = '';
  const issues = validate(S.graph); const errs = issues.filter(i => i.level === 'error').length;
  [['map', 'Map'], ['settings', 'Settings'], ['checks', 'Checks' + (issues.length ? ' (' + issues.length + (errs ? '!' : '') + ')' : ' ✓')]].forEach(([k, label]) =>
    tabs.append(h('button', { class: S.ui.sideTab === k ? 'on' : '', onClick: () => { S.ui.sideTab = k; renderSide(); } }, label)));
  if (S.ui.sideTab === 'map') {
    body.append(h('div', { class: 'graph-pane', id: 'graph-pane' },
      h('div', { class: 'graph-toolbar' }, h('button', { class: 'small', onClick: fitGraph }, 'Fit')),
      h('div', { class: 'graph-hint' }, 'click a box to jump to it · drag to pan · wheel to zoom'),
      h('svg', { id: 'graph-svg', xmlns: 'http://www.w3.org/2000/svg' })));
    renderGraph(); installGraphInteraction();
    if (!S._fitDone) { S._fitDone = true; setTimeout(fitGraph, 0); }
  } else if (S.ui.sideTab === 'settings') {
    const box = h('div', { class: 'inspector' }); body.append(box); renderGraphSettings(box);
  } else {
    const box = h('div', { class: 'inspector' }); body.append(box); renderValidation(box);
  }
}

// ── map (SVG graph) ──
const NODE_W = 220, ROW_H = 16, COL_GAP = 70, ROW_GAP = 22, PAD = 8, TITLE_H = 22;
function layoutGraph(g) {
  const depth = new Map();
  {
    const q = rootIds(g).map(id => [id, 0]);
    while (q.length) {
      const [id, d] = q.shift();
      if (depth.has(id)) continue;
      const n = findNode(g, id); if (!n) continue;
      depth.set(id, d);
      nodeTargets(n).forEach(t => { if (!depth.has(t) && findNode(g, t)) q.push([t, d + 1]); });
    }
  }
  const maxD = Math.max(-1, ...depth.values());
  const cols = new Map();
  const boxes = new Map();
  for (const n of g.nodes) {
    const d = depth.has(n.id) ? depth.get(n.id) : maxD + 1;
    const rows = [];
    n.routes.forEach(r => rows.push({ kind: 'route', text: 'if ' + trunc(condsSummary(r.conditions), 26) + ' →', target: r.nextNodeId }));
    if (n.lines.length) rows.push({ kind: 'snippet', text: (n.pickRandomLine ? '🎲 ' : '') + '“' + trunc(n.lines[0], 30) + '”' + (n.lines.length > 1 ? ' +' + (n.lines.length - 1) : '') });
    n.responses.forEach(r => rows.push({ kind: 'resp', text: '▸ ' + trunc(r.buttonText || '(empty)', 28), target: r.nextNodeId }));
    if (!n.responses.length) rows.push({ kind: 'then', text: '→ ' + (n.nextNodeId && n.nextNodeId !== 'end' ? n.nextNodeId : 'end'), target: n.nextNodeId || 'end' });
    const hgt = TITLE_H + PAD + rows.length * ROW_H + PAD;
    const box = { n, d, rows, w: NODE_W, h: Math.max(hgt, 44), orphan: !depth.has(n.id) };
    boxes.set(n.id, box);
    if (!cols.has(d)) cols.set(d, []);
    cols.get(d).push(box);
  }
  for (const [d, list] of cols) {
    let y = 20;
    for (const b of list) { b.x = 20 + d * (NODE_W + COL_GAP); b.y = y; y += b.h + ROW_GAP; }
  }
  const edges = [];
  for (const b of boxes.values()) {
    b.rows.forEach((row, i) => {
      if (!row.target || row.target === 'end') return;
      const tb = boxes.get(row.target); if (!tb) return;
      const sy = b.y + TITLE_H + PAD + i * ROW_H + ROW_H / 2;
      edges.push({ kind: row.kind, x1: b.x + b.w, y1: sy, x2: tb.x, y2: tb.y + 12, from: b.n.id, to: tb.n.id });
    });
  }
  return { boxes: [...boxes.values()], edges };
}
function renderGraph() {
  const svg = $('#graph-svg'); if (!svg) return;
  const g = S.graph;
  const { boxes, edges } = layoutGraph(g);
  const ns = 'http://www.w3.org/2000/svg';
  const el = (t, a, ...kids) => { const e = document.createElementNS(ns, t); for (const [k, v] of Object.entries(a || {})) e.setAttribute(k, v); kids.forEach(k => k && e.append(k)); return e; };
  svg.innerHTML = '';
  const defs = el('defs');
  defs.innerHTML = '<marker id="arr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#6b7787"/></marker><marker id="arrhi" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#ffad5c"/></marker>';
  svg.append(defs);
  const vp = el('g', { id: 'viewport', transform: `translate(${S.view.x},${S.view.y}) scale(${S.view.k})` });
  svg.append(vp);
  for (const e of edges) {
    const back = e.x2 < e.x1;
    const dx = Math.max(50, Math.abs(e.x2 - e.x1) * 0.4);
    const d = back
      ? `M${e.x1},${e.y1} C${e.x1 + 70},${e.y1} ${e.x2 - 70},${e.y2 - 30} ${e.x2},${e.y2}`
      : `M${e.x1},${e.y1} C${e.x1 + dx},${e.y1} ${e.x2 - dx},${e.y2} ${e.x2},${e.y2}`;
    const hi = S.selected && (e.from === S.selected || e.to === S.selected);
    vp.append(el('path', { d, class: 'gedge ' + e.kind + (hi ? ' hi' : ''), 'marker-end': hi ? 'url(#arrhi)' : 'url(#arr)' }));
  }
  const start = startNode(g);
  for (const b of boxes) {
    const n = b.n;
    const grp = el('g', { class: 'gnode' + (S.selected === n.id ? ' selected' : '') + (start && start.id === n.id ? ' start' : '') + (b.orphan ? ' orphan' : ''), transform: `translate(${b.x},${b.y})` });
    grp.append(el('rect', { class: 'box', width: b.w, height: b.h }));
    const tag = start && start.id === n.id ? 'START' : (entryNodeIds(g).includes(n.id) ? 'ENTRY' : (b.orphan ? 'UNREACHABLE' : ''));
    const title = el('text', { class: 'id', x: PAD, y: 15 }); title.textContent = trunc(n.id, tag ? 18 : 28); grp.append(title);
    if (tag) { const t = el('text', { class: 'tag', x: b.w - PAD, y: 14, 'text-anchor': 'end' }); t.textContent = tag; grp.append(t); }
    b.rows.forEach((row, i) => {
      const t = el('text', { class: row.kind === 'snippet' ? 'snippet' : 'rowlabel ' + row.kind, x: PAD, y: TITLE_H + PAD + i * ROW_H + 11 });
      t.textContent = row.text;
      grp.append(t);
    });
    const hit = el('rect', { class: 'hit', width: b.w, height: b.h });
    hit.addEventListener('click', ev => { ev.stopPropagation(); S.selected = n.id; renderGraph(); scrollToCard(n.id); });
    grp.append(hit);
    vp.append(grp);
  }
  svg._bounds = boxes.length ? { x: 0, y: 0, w: Math.max(...boxes.map(b => b.x + b.w)) + 20, h: Math.max(...boxes.map(b => b.y + b.h)) + 20 } : { x: 0, y: 0, w: 400, h: 300 };
}
function installGraphInteraction() {
  const svg = $('#graph-svg'); if (!svg) return;
  let drag = null;
  svg.addEventListener('mousedown', e => { if (e.button !== 0) return; drag = { x: e.clientX, y: e.clientY, vx: S.view.x, vy: S.view.y }; svg.classList.add('panning'); });
  const move = e => {
    if (!drag) return;
    S.view.x = drag.vx + (e.clientX - drag.x); S.view.y = drag.vy + (e.clientY - drag.y);
    const vp = $('#viewport'); if (vp) vp.setAttribute('transform', `translate(${S.view.x},${S.view.y}) scale(${S.view.k})`);
  };
  const up = () => { drag = null; svg.classList.remove('panning'); };
  window.addEventListener('mousemove', move); window.addEventListener('mouseup', up);
  svg.addEventListener('wheel', e => {
    e.preventDefault();
    const rect = svg.getBoundingClientRect();
    const mx = e.clientX - rect.left, my = e.clientY - rect.top;
    const k0 = S.view.k, k1 = Math.min(2.5, Math.max(0.2, k0 * (e.deltaY < 0 ? 1.1 : 0.9)));
    S.view.x = mx - (mx - S.view.x) * (k1 / k0); S.view.y = my - (my - S.view.y) * (k1 / k0); S.view.k = k1;
    const vp = $('#viewport'); if (vp) vp.setAttribute('transform', `translate(${S.view.x},${S.view.y}) scale(${S.view.k})`);
  }, { passive: false });
}
function fitGraph() {
  const svg = $('#graph-svg'); if (!svg || !svg._bounds) return;
  const r = svg.getBoundingClientRect(); const b = svg._bounds;
  if (!r.width) return;
  const k = Math.min(1, Math.max(0.2, Math.min((r.width - 20) / b.w, (r.height - 20) / b.h)));
  S.view = { x: 10, y: 10, k }; renderGraph();
}

// ── settings tab ──
function renderGraphSettings(box) {
  const g = S.graph;
  box.append(h('h3', null, 'This talk'),
    h('div', { class: 'section' },
      h('div', { class: 'field' }, h('span', null, 'Name on the speaker plate / roster card'), bindText(h('input', { value: g.displayName }), null, v => { g.displayName = v; }, { onChanged: renderScript })),
      h('div', { class: 'field' }, h('span', null, 'File'), h('span', { class: 'mono small' }, S.file, ' · ', g.kind))));

  const sec = h('div', { class: 'section' }, h('p', { class: 'help' }, 'One-click pretend game states for ▶ Play. Flags: comma-separated, "name" or "name=false". Items: "ItemId:count". Probes: game checks that read TRUE.'));
  g.testPresets.forEach((p, i) => {
    sec.append(h('div', { class: 'preset' },
      h('div', { class: 'head' }, bindText(h('input', { value: p.name, placeholder: 'Preset name, e.g. Kid following you' }), null, v => { p.name = v; }, { onChanged: () => {} }),
        h('button', { class: 'small ghost', onClick: () => mutate(() => moveItem(g.testPresets, i, -1)) }, '↑'), h('button', { class: 'small ghost', onClick: () => mutate(() => moveItem(g.testPresets, i, 1)) }, '↓'),
        h('button', { class: 'small ghost', onClick: () => mutate(() => { g.testPresets.splice(i, 1); }) }, '✕')),
      h('div', { class: 'grid' },
        'flags', bindText(h('input', { value: p.flags.join(', '), list: datalistFor('flags') }), null, v => { p.flags = v.split(/[,\n]/).map(s => s.trim()).filter(Boolean); }, { onChanged: () => {} }),
        'money', bindText(h('input', { type: 'number', value: p.money, title: '-1 = leave as is' }), null, v => { p.money = v === '' ? -1 : +v; }, { onChanged: () => {} }),
        'items', bindText(h('input', { value: p.items.join(', '), placeholder: 'TraxUsbStick:1, BlankTapeT1:3' }), null, v => { p.items = v.split(/[,\n]/).map(s => s.trim()).filter(Boolean); }, { onChanged: () => {} }),
        'probes true', bindText(h('input', { value: p.probes.join(', '), list: datalistFor('probes') }), null, v => { p.probes = v.split(/[,\n]/).map(s => s.trim()).filter(Boolean); }, { onChanged: () => {} }))));
  });
  sec.append(h('button', { class: 'small', onClick: () => mutate(() => { g.testPresets.push({ name: 'New preset', flags: [], money: -1, items: [], probes: [] }); }) }, '＋ preset'));
  box.append(h('h3', null, 'Test presets (for ▶ Play)'), sec);

  const meta = S.rosterData && S.rosterData.roster.npcs && S.rosterData.roster.npcs[g.id];
  const probes = Object.entries(S.vocab.probes || {}).filter(([, p]) => p.npc === g.id);
  const actions = Object.entries(S.vocab.actions || {}).filter(([, a]) => a.npc === g.id);
  box.append(h('h3', null, 'What this NPC\'s script can do'),
    h('div', { class: 'section small' },
      probes.length ? h('div', null, h('div', { class: 'muted' }, 'Game checks (condition "Game check"):'), h('ul', null, probes.map(([k, p]) => h('li', null, h('span', { class: 'mono' }, k), ' — ', p.desc)))) : null,
      actions.length ? h('div', null, h('div', { class: 'muted' }, 'Game actions (effect "Game action"):'), h('ul', null, actions.map(([k, a]) => h('li', null, h('span', { class: 'mono' }, k), ' — ', a.desc)))) : null,
      !probes.length && !actions.length ? h('div', { class: 'muted' }, 'None — flags, money and items only. New probes/actions need a line of C# in the NPC\'s script (see the README).') : null,
      meta && meta.notes ? h('p', { class: 'muted' }, meta.notes) : null,
      meta && meta.script ? h('p', { class: 'muted mono' }, meta.script) : null));
}

// ── checks ──
function collectRefs(g) {
  const flags = new Set(), items = new Set(), probes = new Set(), counters = new Set(), objectives = new Set(), actions = new Set();
  const cond = c => { if (c.kind === 'Flag') flags.add(c.arg); else if (c.kind === 'HasItem') items.add(c.arg); else if (c.kind === 'Probe') probes.add(c.arg); else if (c.kind === 'CounterAtLeast') counters.add(c.arg); else if (c.kind === 'ObjectiveDone') objectives.add(c.arg); };
  const eff = e => { if (e.kind === 'SetFlag') flags.add(e.strArg); else if (e.kind === 'GiveItem' || e.kind === 'TakeItem') items.add(e.strArg); else if (e.kind === 'Custom') actions.add(e.strArg); else if (e.kind === 'AddCounter' || e.kind === 'SetCounter') counters.add(e.strArg); else if (e.kind === 'StartObjective' || e.kind === 'CompleteObjective') objectives.add(e.strArg); };
  for (const n of g.nodes) {
    n.routes.forEach(r => r.conditions.forEach(cond));
    n.onEnter.forEach(eff);
    n.responses.forEach(r => { r.conditions.forEach(cond); r.effects.forEach(eff); if (r.requiresFlag) flags.add(r.requiresFlag); if (r.hiddenIfFlag) flags.add(r.hiddenIfFlag); });
  }
  for (const p of g.testPresets) { p.flags.forEach(f => flags.add(f.split('=')[0])); p.items.forEach(i => items.add(i.split(':')[0])); p.probes.forEach(pr => probes.add(pr)); }
  [flags, items, probes, counters, objectives, actions].forEach(s => s.delete(''));
  return { flags, items, probes, counters, objectives, actions };
}
function validate(g) {
  const issues = [];
  const add = (level, msg, nodeId = null) => issues.push({ level, msg, nodeId });
  if (!g.nodes.length) { add('error', 'No boxes — the NPC will fall back to the C# conversation.'); return issues; }
  const ids = new Map();
  g.nodes.forEach(n => { if (!n.id) add('error', 'A box has an empty id.'); ids.set(n.id, (ids.get(n.id) || 0) + 1); });
  for (const [id, c] of ids) if (c > 1) add('error', `Two boxes share the id "${id}" — links will hit the first one.`, id);
  const reach = reachableFromRoots(g);
  for (const n of g.nodes) {
    const check = (t, what) => { if (t && t !== 'end' && !findNode(g, t)) add('error', `${n.id}: ${what} points at a missing box "${t}".`, n.id); };
    n.routes.forEach((r, i) => { check(r.nextNodeId, 'route ' + (i + 1)); if (!r.conditions.length && i < n.routes.length - 1) add('warn', `${n.id}: route ${i + 1} has no conditions, so the routes after it never run.`, n.id); });
    n.responses.forEach((r, i) => { check(r.nextNodeId, 'reply ' + (i + 1)); if (!r.buttonText.trim()) add('warn', `${n.id}: reply ${i + 1} has no text.`, n.id); });
    check(n.nextNodeId, '"after that"');
    if (!reach.has(n.id)) add('warn', `${n.id}: nothing leads here — the player can't reach it.`, n.id);
    if (!n.lines.length && !n.routes.length && !n.responses.length && !n.onEnter.length && !n.nextNodeId) add('warn', `${n.id}: empty box — the talk just ends here silently.`, n.id);
    if (n.lines.some(l => !l.trim())) add('warn', `${n.id}: has an empty line (skipped in-game).`, n.id);
    const allConds = [...n.routes.flatMap(r => r.conditions), ...n.responses.flatMap(r => r.conditions)];
    allConds.forEach(c => { if (!S.vocab.conditionKinds[c.kind]) add('error', `${n.id}: unknown condition kind "${c.kind}".`, n.id); if (c.kind === 'Probe' && c.arg) { const p = S.vocab.probes[c.arg]; if (!p || p.npc !== g.id) add('warn', `${n.id}: game check "${c.arg}" is not one this NPC's script answers (reads FALSE in-game).`, n.id); } });
    const allEffs = [...n.onEnter, ...n.responses.flatMap(r => r.effects)];
    allEffs.forEach(e => { if (!S.vocab.effectKinds[e.kind]) add('error', `${n.id}: unknown effect kind "${e.kind}".`, n.id); if (e.kind === 'Custom' && e.strArg) { const a = S.vocab.actions[e.strArg]; if (!a || a.npc !== g.id) add('warn', `${n.id}: game action "${e.strArg}" is not one this NPC's script performs (does nothing in-game).`, n.id); } if ((e.kind === 'GiveItem' || e.kind === 'TakeItem') && e.strArg && !S.vocab.items.includes(e.strArg)) add('error', `${n.id}: "${e.strArg}" is not a Hotbar item id.`, n.id); });
  }
  return issues;
}
function renderValidation(box) {
  const issues = validate(S.graph);
  const errs = issues.filter(i => i.level === 'error').length;
  box.append(h('h3', null, 'Checks ', h('span', { class: 'badge ' + (errs ? 'none' : (issues.length ? 'greeting' : 'full')) }, errs ? errs + ' error' + (errs > 1 ? 's' : '') : (issues.length ? issues.length + ' note' + (issues.length > 1 ? 's' : '') : 'all good'))),
    h('ul', { class: 'issues' }, issues.length ? issues.map(i => h('li', { class: i.level === 'error' ? 'error' : '', onClick: () => { if (i.nodeId && findNode(S.graph, i.nodeId)) { S.selected = i.nodeId; scrollToCard(i.nodeId); } } }, i.msg)) : h('li', { class: 'ok' }, 'No broken links, every box reachable.')));
}

// ───────────────────────── player ─────────────────────────
function makeSim(g) {
  const sim = { flags: {}, money: 25, items: {}, probes: {}, counters: {}, objectives: {} };
  for (const [k, p] of Object.entries(S.vocab.probes || {})) if (p.defaultTrue && p.npc === g.id) sim.probes[k] = true;
  return sim;
}
function applyPreset(sim, p) {
  p.flags.forEach(f => { const [name, v] = f.split('='); sim.flags[name.trim()] = v === undefined ? true : v.trim() !== 'false'; });
  if (p.money >= 0) sim.money = p.money;
  p.items.forEach(s => { const [id, c] = s.split(':'); sim.items[id.trim()] = c === undefined ? 1 : +c; });
  collectRefs(S.graph).probes.forEach(pr => { sim.probes[pr] = false; });
  for (const [k, pp] of Object.entries(S.vocab.probes || {})) if (pp.defaultTrue && pp.npc === S.graph.id) sim.probes[k] = true;
  p.probes.forEach(pr => { sim.probes[pr] = true; });
}
function evalCond(sim, c, log) {
  let r;
  switch (c.kind) {
    case 'Flag': r = !!sim.flags[c.arg]; break;
    case 'MoneyAtLeast': r = sim.money >= c.num; break;
    case 'HasItem': r = (sim.items[c.arg] || 0) >= Math.max(1, c.num); break;
    case 'CounterAtLeast': r = (sim.counters[c.arg] || 0) >= c.num; break;
    case 'ObjectiveDone': r = !!sim.objectives[c.arg]; break;
    case 'Probe': r = !!sim.probes[c.arg]; break;
    case 'Chance': { const roll = Math.random() * 100; r = roll < c.num; log('route', `rolled ${roll.toFixed(0)} vs ${c.num}% → ${r ? 'pass' : 'fail'}`); break; }
    default: r = false; log('warn', 'unknown condition ' + c.kind);
  }
  return c.negate ? !r : r;
}
function applyEffect(sim, e, log, refreshState) {
  switch (e.kind) {
    case 'SetFlag': sim.flags[e.strArg] = e.boolArg; log('fx', `flag ${e.strArg} = ${e.boolArg}`); break;
    case 'AddMoney': sim.money += e.numArg; log('fx', `+$${e.numArg} → $${sim.money}`); break;
    case 'SpendMoney': if (sim.money >= e.numArg) { sim.money -= e.numArg; log('fx', `−$${e.numArg} → $${sim.money}`); } else log('warn', `SpendMoney ${e.numArg}: not enough money ($${sim.money}) — in-game this logs a warning and takes nothing. Gate the reply with Money ≥.`); break;
    case 'GiveItem': sim.items[e.strArg] = (sim.items[e.strArg] || 0) + Math.max(1, e.numArg); log('fx', `+${Math.max(1, e.numArg)} ${e.strArg} → ${sim.items[e.strArg]}`); break;
    case 'TakeItem': { const n = Math.max(1, e.numArg); if ((sim.items[e.strArg] || 0) >= n) { sim.items[e.strArg] -= n; log('fx', `−${n} ${e.strArg} → ${sim.items[e.strArg]}`); } else log('warn', `TakeItem ${e.strArg}: player doesn't have ${n}.`); break; }
    case 'AddCounter': sim.counters[e.strArg] = (sim.counters[e.strArg] || 0) + e.numArg; log('fx', `${e.strArg} += ${e.numArg} → ${sim.counters[e.strArg]}`); break;
    case 'SetCounter': sim.counters[e.strArg] = e.numArg; log('fx', `${e.strArg} = ${e.numArg}`); break;
    case 'HalSay': log('warn', 'HalSay “' + e.strArg + '” — HAL commentary is vaulted, so this does nothing in-game.'); break;
    case 'StartObjective': log('fx', 'objective started: ' + e.strArg); break;
    case 'CompleteObjective': sim.objectives[e.strArg] = true; log('fx', 'objective complete: ' + e.strArg); break;
    case 'Custom': {
      const a = S.vocab.actions[e.strArg];
      if (!a) { log('warn', `game action "${e.strArg}" — not known; in-game the NPC logs "not handled".`); break; }
      if (a.npc !== S.graph.id) log('warn', `game action "${e.strArg}" belongs to ${a.npc}, not this NPC — does nothing in-game.`);
      log('fx', 'GAME ACTION ' + e.strArg + (a.desc ? ' — ' + a.desc : ''));
      (a.sim || []).forEach(op => {
        if (op.op === 'setProbe') sim.probes[op.name] = !!op.value;
        else if (op.op === 'addItem') sim.items[op.item] = (sim.items[op.item] || 0) + (op.count || 1);
        else if (op.op === 'note') log('note', op.text);
      });
      break;
    }
    default: log('fx', e.kind + ' ' + (e.strArg || '') + ' ' + (e.numArg || ''));
  }
  refreshState();
}

class PlayerRun {
  constructor(g, sim, ui) { this.g = g; this.sim = sim; this.ui = ui; this.token = 0; this.waiter = null; }
  cancel() { this.token++; if (this.waiter) { this.waiter(null); this.waiter = null; } }
  wait() { return new Promise(res => { this.waiter = res; }); }
  resolve(v) { const w = this.waiter; this.waiter = null; if (w) w(v); }
  async run(startId) {
    const tok = ++this.token;
    const { g, sim, ui } = this;
    const log = ui.log;
    let node = startId ? findNode(g, startId) : startNode(g);
    if (startId && !node) { log('warn', 'no node called ' + startId); node = startNode(g); }
    let hops = 0;
    ui.begin();
    while (node && tok === this.token) {
      if (++hops > 200) { log('warn', 'more than 200 hops — route loop? stopping.'); break; }
      ui.setNode(node.id);
      let taken = null;
      for (const r of node.routes) if (r.conditions.every(c => evalCond(sim, c, log))) { taken = r; break; }
      if (taken) { log('route', `${node.id}: route [${condsSummary(taken.conditions)}] → ${taken.nextNodeId}`); node = next(taken.nextNodeId); continue; }
      if (node.routes.length) log('route', `${node.id}: no route matched, continuing here`);
      node.onEnter.forEach(e => applyEffect(sim, e, log, ui.refreshState));
      const speaker = node.speaker || g.displayName;
      if (node.lines.length) {
        const lines = node.pickRandomLine ? [node.lines[Math.floor(Math.random() * node.lines.length)]] : node.lines;
        if (node.pickRandomLine) log('sys', `picked 1 of ${node.lines.length} random lines`);
        for (const line of lines) {
          if (!line) continue;
          await ui.say(speaker, line, this);
          if (tok !== this.token) return;
        }
      }
      const visible = node.responses.filter(r => (!r.requiresFlag || sim.flags[r.requiresFlag]) && (!r.hiddenIfFlag || !sim.flags[r.hiddenIfFlag]) && r.conditions.every(c => evalCond(sim, c, log)));
      const hidden = node.responses.length - visible.length;
      if (hidden) log('sys', `${hidden} repl${hidden > 1 ? 'ies' : 'y'} hidden by conditions`);
      if (!visible.length) { node = next(node.nextNodeId); continue; }
      const pick = await ui.choose(speaker, visible.map(r => r.buttonText || '(empty)'), this);
      if (tok !== this.token || pick === null || pick < 0) return;
      const r = visible[pick];
      log('you', '▸ ' + r.buttonText);
      r.effects.forEach(e => applyEffect(sim, e, log, ui.refreshState));
      if (r.startHintTrack) log('fx', 'hint track started: ' + r.startHintTrack);
      node = next(r.nextNodeId);
    }
    if (tok === this.token) ui.end();
    function next(id) { if (!id || id === 'end') return null; const n = findNode(g, id); if (!n) log('warn', `missing node "${id}" — ending`); return n; }
  }
}

function showPlayer(startNodeId) {
  const g = S.graph;
  const view = $('#view'); view.innerHTML = '';
  const sim = S.playerSim && S.playerSim.graphId === g.id ? S.playerSim.sim : makeSim(g);
  S.playerSim = S.playerSim && S.playerSim.graphId === g.id ? S.playerSim : { graphId: g.id, sim };
  if (g.testPresets.length && !S.playerSim.presetApplied) { applyPreset(sim, g.testPresets[0]); S.playerSim.presetApplied = true; }

  const plate = h('div', { class: 'plate' }, (g.displayName || '').toUpperCase());
  const text = h('div', { class: 'text' });
  const hint = h('div', { class: 'hint' });
  const choices = h('div', { class: 'choices' });
  const screen = h('div', { class: 'screen' }, plate, text, hint, choices);
  const transcript = h('div', { class: 'transcript' });
  const statePane = h('div', { class: 'statepane' });
  view.append(h('div', { class: 'player' }, h('div', { class: 'stage' }, screen, transcript), statePane));

  const startSel = h('select', null, h('option', { value: '' }, 'start'), g.nodes.map(n => h('option', { value: n.id, selected: n.id === startNodeId }, n.id)));
  const restart = () => { run.cancel(); transcript.innerHTML = ''; run.run(startSel.value || null); };
  setTopbar([h('strong', null, '▶ ', g.displayName || g.id), h('span', { class: 'title-file mono' }, S.file), h('span', { class: 'muted small' }, 'from'), startSel],
    [h('button', { onClick: restart }, '↻ Restart (keep state)'),
     h('button', { onClick: () => { S.playerSim = null; showPlayer(startNodeId); } }, 'Reset state'),
     h('button', { class: 'primary', onClick: () => location.hash = '#/edit/' + encodeURIComponent(S.file) }, '✎ Edit')]);

  let typing = null;
  const ui = {
    log(kind, msg) { const t = h('div', { class: 't ' + kind }, msg); transcript.append(t); transcript.scrollTop = transcript.scrollHeight; },
    begin() { choices.innerHTML = ''; text.textContent = ''; hint.textContent = ''; screen.querySelectorAll('.ended').forEach(e => e.remove()); ui.log('sys', '— talk starts —'); },
    setNode(id) { ui.currentNode = id; },
    end() { hint.textContent = ''; choices.innerHTML = ''; screen.classList.remove('clickable'); screen.append(h('div', { class: 'ended' }, '— end of talk —  ', h('button', { class: 'small', onClick: restart }, 'talk again'))); ui.log('sys', '— end —'); },
    async say(speaker, line, run) {
      plate.textContent = speaker.toUpperCase() + '   ·   ' + ui.currentNode;
      choices.innerHTML = ''; screen.querySelectorAll('.ended').forEach(e => e.remove());
      hint.textContent = 'click / Space to continue';
      screen.classList.add('clickable');
      const t = h('div', { class: 't say' }, h('span', { class: 'who' }, speaker + ': '), line); transcript.append(t); transcript.scrollTop = transcript.scrollHeight;
      text.innerHTML = ''; const span = h('span'); const cur = h('span', { class: 'cursor' }); text.append(span, cur);
      let i = 0; let done = false;
      await new Promise(res => {
        const finish = () => { if (done) return; done = true; clearInterval(typing); typing = null; span.textContent = line; res(); };
        typing = setInterval(() => { i += 2; span.textContent = line.slice(0, i); if (i >= line.length) finish(); }, 14);
        run.skip = finish;
      });
      run.skip = null;
      if (!run.waiter) await run.wait();
      screen.classList.remove('clickable');
    },
    async choose(speaker, labels, run) {
      plate.textContent = speaker.toUpperCase() + '   ·   ' + ui.currentNode;
      hint.textContent = labels.length ? 'pick a reply (1-' + labels.length + ')' : '';
      choices.innerHTML = '';
      labels.forEach((l, i) => choices.append(h('button', { onClick: () => run.resolve(i) }, h('span', { class: 'k' }, (i + 1) + '.'), l)));
      const v = await run.wait();
      choices.innerHTML = '';
      return v;
    },
    refreshState() { renderStatePane(); },
  };
  const run = new PlayerRun(g, sim, ui);
  S.player = run;
  screen.addEventListener('click', () => { if (run.skip) run.skip(); else if (run.waiter && !choices.children.length) run.resolve(true); });
  document.onkeydown = e => {
    if (/INPUT|TEXTAREA|SELECT/.test(document.activeElement && document.activeElement.tagName)) return;
    if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); if (run.skip) run.skip(); else if (run.waiter && !choices.children.length) run.resolve(true); }
    else if (/^[1-9]$/.test(e.key)) { const b = choices.children[+e.key - 1]; if (b) b.click(); }
    else if (e.key.toLowerCase() === 'r') restart();
  };

  function renderStatePane() {
    statePane.innerHTML = '';
    const refs = collectRefs(g);
    const meta = S.rosterData && S.rosterData.roster.npcs ? S.rosterData.roster.npcs[g.id] : null;
    statePane.append(h('h3', null, 'Presets — set the whole situation'),
      g.testPresets.length ? h('div', { class: 'presets' }, g.testPresets.map(p => h('button', { onClick: () => { applyPreset(sim, p); ui.log('sys', 'preset: ' + p.name); renderStatePane(); restart(); } }, p.name))) : h('p', { class: 'help' }, 'No presets yet — add them in Edit → Settings. Or flip the switches below.'));
    const flagNames = [...new Set([...refs.flags, ...Object.keys(sim.flags)])].sort();
    statePane.append(h('h3', null, 'Flags (saved story bits)'),
      h('div', { class: 'flaglist' }, flagNames.length ? flagNames.map(f => h('label', null, h('input', { type: 'checkbox', checked: !!sim.flags[f], onChange: e => { sim.flags[f] = e.target.checked; ui.log('sys', `you set flag ${f} = ${e.target.checked}`); } }), h('span', { class: 'mono' }, f))) : h('p', { class: 'help' }, 'This talk uses no flags.')),
      addRow('add a flag…', datalistFor('flags'), v => { sim.flags[v] = true; renderStatePane(); }));
    const probeNames = [...new Set([...refs.probes, ...Object.keys(sim.probes), ...Object.keys(S.vocab.probes).filter(k => S.vocab.probes[k].npc === g.id)])].sort();
    if (probeNames.length) statePane.append(h('h3', null, 'Game checks (live state the script answers)'),
      h('div', { class: 'flaglist' }, probeNames.map(pn => { const info = S.vocab.probes[pn] || (meta && meta.probes && { desc: meta.probes[pn] }); return h('div', null, h('label', null, h('input', { type: 'checkbox', checked: !!sim.probes[pn], onChange: e => { sim.probes[pn] = e.target.checked; ui.log('sys', `you set ${pn} = ${e.target.checked}`); } }), h('span', { class: 'mono' }, pn), info && info.npc && info.npc !== g.id ? h('span', { class: 'badge none' }, 'not this NPC') : null), info && info.desc ? h('div', { class: 'desc' }, info.desc) : null); })));
    statePane.append(h('h3', null, 'Money'), h('div', { class: 'money' }, '$', h('input', { type: 'number', value: sim.money, onChange: e => { sim.money = +e.target.value || 0; ui.log('sys', 'you set money to $' + sim.money); } })));
    const itemNames = [...new Set([...refs.items, ...Object.keys(sim.items)])].sort();
    statePane.append(h('h3', null, 'Items in the hotbar'),
      h('div', null, itemNames.map(it => h('div', { class: 'itemrow' }, h('span', { class: 'mono small', style: 'align-self:center' }, it), h('input', { type: 'number', value: sim.items[it] || 0, onChange: e => { sim.items[it] = +e.target.value || 0; } }), h('button', { class: 'small ghost', onClick: () => { delete sim.items[it]; renderStatePane(); } }, '✕')))),
      addRow('add an item id…', datalistFor('items'), v => { sim.items[v] = (sim.items[v] || 0) + 1; renderStatePane(); }));
    if (refs.counters.size || Object.keys(sim.counters).length) {
      const names = [...new Set([...refs.counters, ...Object.keys(sim.counters)])];
      statePane.append(h('h3', null, 'Counters'), h('div', null, names.map(c => h('div', { class: 'itemrow' }, h('span', { class: 'mono small', style: 'align-self:center' }, c), h('input', { type: 'number', value: sim.counters[c] || 0, onChange: e => { sim.counters[c] = +e.target.value || 0; } }), h('span')))));
    }
    if (refs.objectives.size) statePane.append(h('h3', null, 'Objectives done'), h('div', { class: 'flaglist' }, [...refs.objectives].map(o => h('label', null, h('input', { type: 'checkbox', checked: !!sim.objectives[o], onChange: e => { sim.objectives[o] = e.target.checked; } }), h('span', { class: 'mono' }, o)))));
    statePane.append(h('p', { class: 'help', style: 'margin-top:18px' }, 'Changes apply to the next check the talk makes — flip a switch, then Restart (R) to replay from the top with it, or keep going to see the branch it affects.'));
    function addRow(ph, list, onAdd) { const inp = h('input', { placeholder: ph, list }); return h('div', { class: 'addrow' }, inp, h('button', { class: 'small', onClick: () => { const v = inp.value.trim(); if (v) onAdd(v); } }, '＋')); }
  }
  renderStatePane();
  run.run(startNodeId || null);
}

boot();
