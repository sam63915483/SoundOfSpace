// Sound Lab - the audition half of Audio Studio.
//
// The Mixer page is for sounds the game already plays: change the level, swap the
// file. This page is for sounds that DO NOT EXIST YET. Each set is one moment in
// the game; each set holds three takes of it. You play them against each other,
// pick one, and only the winner gets wired into the game.
//
// A take can be listed here long before its file exists - the sheet is written
// first (what the moment is, what each take should sound like) and the audio is
// generated after. The server stamps `exists` on every read, so an ungenerated
// take shows as "not generated yet" and lights up on its own once the file lands.
// Nothing here is referenced by the game.

let sheet = { version: 1, sets: [] };
let dirty = false;
let audio = new Audio();
let playingId = null;
let playingSet = null;

// Per-set position of the rateDemo slider, 0..1. Kept out of the sheet: it is a
// listening aid, not a decision, so it must not be saved or sent back.
const demoT = {};

const $ = (s) => document.querySelector(s);
const takeId = (set, take) => set.id + ":" + take.id;
const lerp = (a, b, t) => a + (b - a) * t;

// A set flagged `rateDemo` is one the GAME will drive: it plays a flat looping
// texture and moves pitch + volume with a gameplay value every frame. Auditioning
// the raw clip tells you almost nothing about that, so the page reproduces the
// drive with a slider. playbackRate must shift PITCH as well as speed to match
// Unity's AudioSource.pitch — browsers time-stretch by default, which would
// preserve the pitch and sound nothing like the game.
function applyDemo(set) {
  const d = set && set.rateDemo;
  if (!d || !audio) return;
  const t = demoT[set.id] === undefined ? 0.35 : demoT[set.id];
  audio.preservesPitch = false;
  audio.mozPreservesPitch = false;
  audio.webkitPreservesPitch = false;
  audio.playbackRate = lerp(d.rate[0], d.rate[1], t);
  audio.volume = Math.max(0, Math.min(1, lerp(d.volume[0], d.volume[1], t)));
}

function status(text, isDirty) {
  const el = $("#status");
  el.textContent = text;
  el.classList.toggle("dirty", !!isDirty);
}

function setDirty() {
  dirty = true;
  $("#save").disabled = false;
  status("unsaved changes", true);
}

async function load() {
  sheet = await (await fetch("/api/candidates")).json();
  $("#save").disabled = true;
  render();
  const undecided = sheet.sets.filter((s) => !s.pick).length;
  status(
    `${sheet.generated}/${sheet.total} clips generated - ` +
      `${undecided} of ${sheet.sets.length} sets still undecided`
  );
}

function play(set, take) {
  const id = takeId(set, take);
  if (playingId === id) {
    audio.pause();
    playingId = null;
    playingSet = null;
    render();
    return;
  }
  if (!take.exists) return;
  audio.pause();
  audio = new Audio("/api/audio?path=" + encodeURIComponent(take.file));
  // Loops are auditioned looping - a tension bed that only sounds right once
  // through is not a tension bed.
  audio.loop = !!take.loop;
  playingSet = set;
  applyDemo(set);
  audio.onended = () => {
    playingId = null;
    playingSet = null;
    render();
  };
  audio.onerror = () => {
    playingId = null;
    playingSet = null;
    status("could not play " + take.file);
    render();
  };
  audio.play().catch(() => status("could not play " + take.file));
  playingId = id;
  render();
}

function pick(set, take) {
  // Clicking the winner again clears it - changing your mind should not need
  // a different control.
  set.pick = set.pick === take.id ? "" : take.id;
  setDirty();
  render();
}

function takeCard(set, take) {
  const el = document.createElement("div");
  const chosen = set.pick === take.id;
  el.className =
    "take" + (chosen ? " picked" : "") + (take.exists ? "" : " pending");

  const head = document.createElement("div");
  head.className = "take-head";

  const playBtn = document.createElement("button");
  const on = playingId === takeId(set, take);
  playBtn.className = "play" + (on ? " on" : "");
  playBtn.textContent = on ? "■" : "▶";
  playBtn.disabled = !take.exists;
  playBtn.title = take.exists ? "Play this take" : "Not generated yet";
  playBtn.onclick = () => play(set, take);

  const name = document.createElement("div");
  name.className = "name";
  const b = document.createElement("b");
  b.textContent = take.id + " · " + take.label;
  if (take.loop) b.appendChild(tag("dup", "loops"));
  if (!take.exists) b.appendChild(tag("empty", "not generated yet"));
  const small = document.createElement("small");
  small.textContent = take.blurb || "";
  small.title = take.blurb || "";
  name.append(b, small);

  const pickBtn = document.createElement("button");
  pickBtn.className = "pickbtn" + (chosen ? " on" : "");
  pickBtn.textContent = chosen ? "✓ picked" : "pick this";
  pickBtn.onclick = () => pick(set, take);

  head.append(playBtn, name, pickBtn);
  el.appendChild(head);

  const prompt = document.createElement("details");
  const sum = document.createElement("summary");
  sum.textContent = "what it was asked for";
  const pre = document.createElement("p");
  pre.className = "prompt";
  pre.textContent = take.prompt || "";
  prompt.append(sum, pre);
  el.appendChild(prompt);

  const ni = document.createElement("input");
  ni.className = "take-note";
  ni.placeholder = "note on this take (e.g. too long, drop the tail)";
  ni.value = take.note || "";
  ni.oninput = () => {
    take.note = ni.value;
    setDirty();
  };
  el.appendChild(ni);
  return el;
}

function tag(cls, text) {
  const s = document.createElement("span");
  s.className = "tag " + cls;
  s.textContent = text;
  return s;
}

// The driver, as a slider. Start a take playing, then drag this: the loop speeds
// up and gets louder exactly as the game will drive it. Deliberately sits under
// the three takes rather than inside one, because it applies to whichever is
// playing - that is how you tell which take survives being pushed to the top.
function demoStrip(set) {
  const d = set.rateDemo;
  if (demoT[set.id] === undefined) demoT[set.id] = 0.35;

  const wrap = document.createElement("div");
  wrap.className = "demo";

  const lab = document.createElement("span");
  lab.className = "demo-label";
  lab.textContent = d.label || "DRIVE";

  const lo = document.createElement("small");
  lo.textContent = d.lowHint || "";

  const range = document.createElement("input");
  range.type = "range";
  range.min = 0;
  range.max = 1;
  range.step = 0.01;
  range.value = demoT[set.id];

  const hi = document.createElement("small");
  hi.textContent = d.highHint || "";

  const read = document.createElement("span");
  read.className = "demo-read";
  const show = () => {
    const t = demoT[set.id];
    read.textContent =
      Math.round(t * 100) + "%  ·  " +
      lerp(d.rate[0], d.rate[1], t).toFixed(2) + "x speed";
  };
  show();

  range.oninput = () => {
    demoT[set.id] = Number(range.value);
    show();
    // Only touch the audio if this set is what is actually playing.
    if (playingSet && playingSet.id === set.id) applyDemo(set);
  };

  wrap.append(lab, lo, range, hi, read);
  return wrap;
}

function setBlock(set) {
  const el = document.createElement("div");
  el.className = "set" + (set.pick ? " decided" : "");

  const h = document.createElement("h2");
  h.textContent = set.title;
  if (set.pick) h.appendChild(tag("dup", "picked " + set.pick));
  el.appendChild(h);

  const when = document.createElement("p");
  when.className = "when";
  when.textContent = set.when || "";
  el.appendChild(when);

  const where = document.createElement("p");
  where.className = "where";
  where.textContent = set.target + " — " + (set.targetState || "");
  el.appendChild(where);

  const grid = document.createElement("div");
  grid.className = "takes";
  for (const t of set.takes) grid.appendChild(takeCard(set, t));
  el.appendChild(grid);

  if (set.rateDemo) el.appendChild(demoStrip(set));

  const ni = document.createElement("input");
  ni.className = "set-note";
  ni.placeholder =
    "note for Claude about this whole sound (e.g. none of these, try something wetter)";
  ni.value = set.note || "";
  ni.oninput = () => {
    set.note = ni.value;
    setDirty();
  };
  el.appendChild(ni);
  return el;
}

function render() {
  const host = $("#sets");
  host.innerHTML = "";
  const onlyUndecided = $("#onlyUndecided").checked;
  const shown = sheet.sets.filter((s) => !onlyUndecided || !s.pick);
  if (!shown.length) {
    const p = document.createElement("p");
    p.className = "empty-msg";
    p.textContent = onlyUndecided
      ? "Every set has a winner."
      : "Nothing to audition yet.";
    host.appendChild(p);
    return;
  }
  for (const s of shown) host.appendChild(setBlock(s));
}

async function save() {
  status("saving...");
  const res = await fetch("/api/candidates", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(sheet, null, 2),
  });
  const out = await res.json();
  if (out.error) return status("save failed: " + out.error, true);
  dirty = false;
  $("#save").disabled = true;
  const undecided = sheet.sets.filter((s) => !s.pick).length;
  status(`saved - ${undecided} of ${sheet.sets.length} sets still undecided`);
}

$("#save").onclick = save;
$("#onlyUndecided").onchange = render;
document.addEventListener("keydown", (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key === "s") {
    e.preventDefault();
    save();
  }
});
window.addEventListener("beforeunload", (e) => {
  if (dirty) {
    e.preventDefault();
    e.returnValue = "";
  }
});
load();
