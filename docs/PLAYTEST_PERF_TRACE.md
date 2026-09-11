🟢 ACTIVE — written 2026-09-11 for the FPS hunt (≈50 fps; worse looking at Constant Companion and at the village, even through the planet).

# Perf run: the FPS hunt

One ~12-minute run in a **Development Build**. The game records every frame to
a file the whole time (no 2000-frame profiler cap), and you press keys to
switch suspects off one at a time while you look at the bad spots. Afterwards
Claude reads the files; you don't need to read anything.

## 1. Build settings (File ▸ Build Settings)

| Setting | Value | Why |
|---|---|---|
| Development Build | **ON** | the recorder only exists in dev builds |
| Autoconnect Profiler | **OFF** | streaming to the Editor costs ~2 ms/frame and hides the real numbers; the recorder writes locally instead |
| Deep Profiling Support | **OFF** | distorts everything and shrinks the capture window |
| Script Debugging | OFF | |

Build, then run the exe. Use your normal save (Humble Abode, near the cabin).
Set graphics as you normally play (that's the fps we're chasing).

## 2. What you see

Top-right there is an orange **PERF TRACE** box: `recording`, a mark counter,
and the toggle list. Leave the F3 overlay on too. **Do not use F-keys during
this run** — F6–F9 currently fire two debug tools at once (FPSOverlay and
LightingDebugToolbox share them), which would muddy the trace.

## 3. The keys

Numpad. (No numpad: PgUp/PgDn moves the `>` in the box, Delete flips it; Home = mark, End = snapshot.)

| Key | Removes | Restore |
|---|---|---|
| Num1 | every point + spot light (lanterns, tunnel cage lights, torches) | press again |
| Num2 | the moon base (Tunnel Rig + MoonBaseINTER on Constant Companion) | press again |
| Num3 | the village (TOWN-VILLAGE) | press again |
| Num4 | the shuttle's 248 renderers | press again |
| Num5 | all shadows | press again |
| Num6 | grass | press again |
| Num7 | space dust | press again |
| Num8 | UI canvases | press again |
| Num9 | pixel-light cap 64 → 4 | press again |
| **Home** | — stamps MARK n into the file | say afterwards what mark n was ("mark 3 = looking at the moon from the cabin") |
| **End** | — writes a 600-frame Profiler snapshot (`.raw`) of the next ~10 s | one per bad spot is plenty |

Rule for a clean A/B: **one toggle at a time**, hold it ~5 s while keeping the
same view, then press it again to restore before trying the next.

## 4. The run (≈12 min)

Stand still for each station; look, don't walk, while a toggle is on.

1. **Cabin, looking away from everything** (sky/sea, no village, no moon): Home. 5 s. This is the baseline.
2. **Cabin, looking toward the village through the planet** (the direction that tanks fps even though the village is over the horizon): Home. End (snapshot). Then Num3 5 s, restore; Num1 5 s, restore; Num5 5 s, restore; Num9 5 s, restore.
3. **Walk into the village, look at the worst direction**: Home. End. Then Num3, Num1, Num5, Num9, Num4 — each 5 s, restore between.
4. **From the ground, look at Constant Companion in the sky**: Home. End. Then Num2 5 s, restore; Num1 5 s, restore; Num5 5 s, restore.
5. **Same spot, look at the ground / horizon 90° away from the moon**: Home, 5 s (moon off-screen control).
6. **Still on the ground, general walking around the cabin**: Num6 5 s, restore; Num7 5 s, restore; Num8 5 s, restore.
7. If you have time: **fly to the moon**, look at the base from orbit: Home, End, Num2, Num1. Then land at the base: Home, End.
8. Quit normally (Alt+F4 is fine — the file flushes every 2 s).

Tell me afterwards what each mark number was (one line each).

## 5. Where the files are

`%AppData%\..\LocalLow\DefaultCompany\Solar System 2\perf\`
(Tools ▸ Solar System ▸ Perf ▸ Open Perf Folder opens it from the Editor.)

- `trace_<date>.csv` — the whole run, one row per frame.
- `<date>_snapNN_markM.raw` — the Profiler snapshots. Claude turns them into
  `.txt` with Tools ▸ Solar System ▸ Perf ▸ Dump Profiler Snapshots (or you
  can load one in Window ▸ Analysis ▸ Profiler ▸ Load).

Analysis: `py -3 tools/perf/analyze_perf_trace.py` (newest trace) — prints fps
by situation (village visible / hidden / moon / elsewhere), what each toggle
saved, and the 3 s around every mark.

## 6. Note on the Editor profiler

Preferences ▸ Analysis ▸ Profiler ▸ Frame Count is now forced to 2000, which is
Unity's hard maximum (~40 s at 50 fps). That is why the trace file exists: it
has no limit. If we ever do a live Editor-attached capture again, 2000 is as
big as it gets.
