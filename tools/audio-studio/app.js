// Audio Studio - the browser half.
//
// LOCKSTEP RULE (inherited from Dialogue Studio, and it has bitten before):
// BUSES and the manifest field names below must match GameAudio.cs in Part 2.
// Change one, change the other, or a sound previews here and does nothing
// in the game.
const BUSES = ["SFX", "Ambience", "UI", "Music"];

let manifest = { version: 1, sounds: [] };
let library = [];
let group = null;
let dirty = false;
let audio = new Audio();
let playingKey = null;

const $ = (s) => document.querySelector(s);

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
  manifest = await (await fetch("/api/manifest")).json();
  library = (await (await fetch("/api/library")).json()).files;
  markDuplicates();
  $("#save").disabled = true;
  render();
  const empty = manifest.sounds.filter((s) => !s.clip).length;
  status(`${manifest.sounds.length} sounds, ${empty} with no clip`);
}

// Several actions sharing one file is worth seeing: eating and dying both
// play augh.mp3, and all three jetpack boosts share one clip.
function markDuplicates() {
  const count = {};
  for (const s of manifest.sounds)
    if (s.clip) count[s.clip] = (count[s.clip] || 0) + 1;
  for (const s of manifest.sounds) s._dup = s.clip && count[s.clip] > 1;
}

function groups() {
  const seen = {};
  for (const s of manifest.sounds) seen[s.group] = (seen[s.group] || 0) + 1;
  return Object.keys(seen).sort().map((g) => [g, seen[g]]);
}

function hasIssue(s) {
  return !s.clip || s.dead || s.missing;
}

function visible() {
  const q = $("#search").value.trim().toLowerCase();
  const onlyIssues = $("#onlyIssues").checked;
  return manifest.sounds.filter((s) => {
    if (group && s.group !== group) return false;
    if (onlyIssues && !hasIssue(s)) return false;
    if (!q) return true;
    return (
      (s.label || "").toLowerCase().includes(q) ||
      (s.key || "").toLowerCase().includes(q) ||
      (s.clip || "").toLowerCase().includes(q)
    );
  });
}

function play(sound) {
  if (playingKey === sound.key) {
    audio.pause();
    playingKey = null;
    render();
    return;
  }
  if (!sound.clip) return;
  audio.pause();
  audio = new Audio("/api/audio?path=" + encodeURIComponent(sound.clip));
  audio.volume = Math.max(0, Math.min(1, Number(sound.volume) || 0));
  audio.onended = () => {
    playingKey = null;
    render();
  };
  audio.onerror = () => {
    playingKey = null;
    status("could not play " + sound.clip);
    render();
  };
  audio.play().catch(() => status("could not play " + sound.clip));
  playingKey = sound.key;
  render();
}

function setField(sound, field, value) {
  sound[field] = value;
  setDirty();
  // A live volume change should be audible immediately.
  if (field === "volume" && playingKey === sound.key)
    audio.volume = Math.max(0, Math.min(1, Number(value) || 0));
}

function tag(cls, text) {
  const s = document.createElement("span");
  s.className = "tag " + cls;
  s.textContent = text;
  return s;
}

function row(sound) {
  const el = document.createElement("div");
  el.className = "row";

  const playBtn = document.createElement("button");
  playBtn.className = "play" + (playingKey === sound.key ? " on" : "");
  playBtn.textContent = playingKey === sound.key ? "■" : "▶";
  playBtn.disabled = !sound.clip;
  playBtn.title = sound.clip ? "Play at this volume" : "No clip to play";
  playBtn.onclick = () => play(sound);

  const name = document.createElement("div");
  name.className = "name";
  const b = document.createElement("b");
  b.textContent = sound.label || sound.key;
  if (!sound.clip) b.appendChild(tag("empty", "no clip"));
  if (sound.dead) b.appendChild(tag("dead", "never plays"));
  if (sound.missing) b.appendChild(tag("dead", "gone from code"));
  if (sound._dup) b.appendChild(tag("dup", "shared file"));
  const small = document.createElement("small");
  small.textContent = sound.clip || sound.key;
  small.title = sound.clip || sound.key;
  name.append(b, small);

  const slider = document.createElement("input");
  slider.type = "range";
  slider.min = 0;
  slider.max = 1;
  slider.step = 0.01;
  slider.value = sound.volume;

  const num = document.createElement("input");
  num.className = "vol";
  num.type = "number";
  num.min = 0;
  num.max = 1;
  num.step = 0.01;
  num.value = Number(sound.volume).toFixed(2);

  slider.oninput = () => {
    num.value = Number(slider.value).toFixed(2);
    setField(sound, "volume", Number(slider.value));
  };
  num.oninput = () => {
    slider.value = num.value;
    setField(sound, "volume", Number(num.value));
  };

  const bus = document.createElement("select");
  for (const name2 of BUSES) {
    const o = document.createElement("option");
    o.value = name2;
    o.textContent = name2;
    if (sound.bus === name2) o.selected = true;
    bus.appendChild(o);
  }
  bus.onchange = () => setField(sound, "bus", bus.value);

  const swap = document.createElement("button");
  swap.className = "swap";
  swap.textContent = "swap";
  swap.onclick = () => openSwap(sound);

  el.append(playBtn, name, slider, num, bus, swap);

  const note = document.createElement("div");
  note.className = "note";
  const ni = document.createElement("input");
  ni.placeholder = "note for Claude (e.g. too loud, find something drier)";
  ni.value = sound.note || "";
  ni.oninput = () => setField(sound, "note", ni.value);
  note.appendChild(ni);
  el.appendChild(note);
  return el;
}

function openSwap(sound) {
  const picked = prompt(
    "Path of the replacement clip, relative to Assets/.\n" +
      "Leave blank and press OK to upload a file from your machine instead.\n\n" +
      "Current: " + (sound.clip || "(none)"),
    sound.clip || ""
  );
  if (picked === null) return;
  if (picked.trim() === "") return upload(sound);
  if (!library.includes(picked.trim()))
    return status("no such file in the project: " + picked.trim());
  setField(sound, "clip", picked.trim());
  markDuplicates();
  render();
}

function upload(sound) {
  const input = document.createElement("input");
  input.type = "file";
  input.accept = ".mp3,.wav";
  input.onchange = async () => {
    const file = input.files[0];
    if (!file) return;
    status("uploading " + file.name + "...");
    const res = await fetch("/api/upload", {
      method: "POST",
      headers: { "X-Filename": file.name, "X-Group": sound.group || "Other" },
      body: await file.arrayBuffer(),
    });
    const out = await res.json();
    if (out.error) return status("upload failed: " + out.error);
    library.push(out.clip);
    setField(sound, "clip", out.clip);
    markDuplicates();
    render();
    status("added " + out.clip, true);
  };
  input.click();
}

function render() {
  const nav = $("#groups");
  nav.innerHTML = "";
  const all = document.createElement("a");
  all.innerHTML = "<span style='color:inherit'>All</span>";
  all.appendChild(
    Object.assign(document.createElement("span"), {
      textContent: manifest.sounds.length,
    })
  );
  all.className = group ? "" : "on";
  all.onclick = () => {
    group = null;
    render();
  };
  nav.appendChild(all);
  for (const [g, n] of groups()) {
    const a = document.createElement("a");
    a.appendChild(Object.assign(document.createElement("span"), { textContent: g }));
    a.appendChild(Object.assign(document.createElement("span"), { textContent: n }));
    a.className = g === group ? "on" : "";
    a.onclick = () => {
      group = g;
      render();
    };
    nav.appendChild(a);
  }

  const list = $("#list");
  list.innerHTML = "";
  const rows = visible();
  if (!rows.length) {
    const p = document.createElement("p");
    p.className = "empty-msg";
    p.textContent = "Nothing matches.";
    list.appendChild(p);
    return;
  }
  let last = null;
  for (const s of rows) {
    if (s.group !== last) {
      const h = document.createElement("h2");
      h.textContent = s.group;
      list.appendChild(h);
      last = s.group;
    }
    list.appendChild(row(s));
  }
}

async function save() {
  status("saving...");
  const res = await fetch("/api/manifest", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(manifest, null, 2),
  });
  const out = await res.json();
  if (out.error) return status("save failed: " + out.error, true);
  dirty = false;
  $("#save").disabled = true;
  status("saved");
}

$("#save").onclick = save;
$("#search").oninput = render;
$("#onlyIssues").onchange = render;
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
