# Audio Studio

Mix the game's audio in a browser. No Unity, no recompiling.

**Start it:** double-click `Audio Studio.bat` → http://localhost:8766
(Dialogue Studio is 8765; both can run at once.)

## Two pages

- **Mixer** (`/`) — every sound the game already makes. Levels, categories, clip swaps.
- **Sound Lab** (`/lab.html`) — sounds the game does **not** make yet. Three
  candidate takes per moment; you play them against each other and pick one.
  Nothing on that page is referenced by the game until its take wins. See
  "The Sound Lab" below.

## What you're looking at

One row per sound the game can make in scene `1.6.7.7.7`, grouped by system.
Each row: **▶** plays it *at the volume you've set*, then the volume slider, the
category, **swap** to change the file, and a note box.

Tags:

| Tag | Meaning |
|---|---|
| **no clip** | The slot exists in the code but nothing is wired to it. |
| **never plays** | The field exists but the code deliberately ignores it. |
| **gone from code** | It was in the manifest, but the last scan couldn't find it. Nothing is ever deleted automatically. |
| **shared file** | Another action plays this same file. Sometimes deliberate, sometimes not. |

The **only problems** checkbox filters to just the rows carrying one of the
first three tags.

## Swapping a sound

Press **swap**. Either type a path relative to `Assets/`, or leave it blank and
press OK to upload an `.mp3`/`.wav` from anywhere on your machine. Uploads land
in `Assets/Audio/Studio/<Group>/` with their Unity `.meta` written for you.

## Saving

**Ctrl+S** or the Save button. Every save backs the previous manifest up to
`backups/` (30 kept). The manifest itself is
`Assets/StreamingAssets/Audio/sounds.json` — plain text, in git.

## Rescanning

After code changes add or remove sounds:

    py -3 tools/audio-studio/scan.py            rescan and merge
    py -3 tools/audio-studio/scan.py --report   look, change nothing

**A rescan never overwrites your work.** The scanner owns whether a sound still
exists and whether the code ignores it; you own the label, group, volume,
category and note. A clip you swapped in the browser also survives — the scanner
remembers its own last answer in `scanned_clip`, so it can tell "Sam changed
this" from "someone changed it in Unity".

## Two things that are true but surprising

- **Nothing here changes the game yet.** Part 2 wires the game up to read this
  file. Until then this is a mixing desk with the cables not yet plugged in.
- **A built `.exe` won't see edits until it's rebuilt.** Unity copies
  StreamingAssets into a build at build time. In the Editor it's live.

## The Sound Lab

`http://localhost:8766/lab.html`. The audition page for sounds that do not exist
yet: rod snap, line tension, a fish running, reeling, crystals, the moon-base
lever, village doors, astronaut breathing.

Each **set** is one moment in the game and holds **three takes** of it. Press ▶
on each, then **pick this** on the winner. Loops audition looping. Every take
carries the exact description it was generated from, under "what it was asked
for" — so if none of them land, say what to change and the next three are aimed
better. There is a note box per take and one per set.

The sheet is `tools/audio-studio/candidates.json`; Save writes your picks and
notes back to it. The audio files live under
`Assets/Audio/Studio/_Candidates/<System>/` and **nothing in the game points at
them** — a winner is wired in by hand afterwards.

**A take can exist on this page before its audio does.** The sheet is written
first (what the moment is, what each take should sound like) and generated
after, so a take with no file yet shows as *not generated yet* with its play
button disabled. The server checks the disk on every page load, so it lights up
on its own once the file lands — no edit needed. The header counts how many of
the takes are generated.

## Tests

    cd tools/audio-studio && py -3 -m unittest discover -s tests -t . -v

## How it fits together

| File | Job |
|---|---|
| `audioscan/meta_index.py` | Unity GUID ↔ asset path, from `.meta` files |
| `audioscan/cs_parser.py` | Find serialized clip slots in C# |
| `audioscan/yaml_refs.py` | Read clip assignments from prefab/scene YAML |
| `audioscan/manifest.py` | Scope, keys, and the non-destructive merge |
| `scan.py` | Wires those together into `sounds.json` |
| `serve.py` | The local server |
| `index.html` / `app.js` / `styles.css` | The Mixer page |
| `lab.html` / `lab.js` / `candidates.json` | The Sound Lab page and its audition sheet |

**Lockstep rule:** the bus names in `app.js` must match `GameAudio.cs` once
Part 2 exists. Change one, change the other, or a sound previews here and does
nothing in the game.
