🟢 ACTIVE — written 2026-09-11 for the FPS hunt. Run 1 done the same evening (findings in §7); §4 is now the RUN 2 script.

# Perf run: the FPS hunt

A short run in a **Development Build**. The game records every frame to a file
the whole time (no 2000-frame profiler cap), and you press number-pad keys to
switch one suspect off at a time while you look at the bad spots. Afterwards
Claude reads the files; you don't need to read anything.

## 1. Build settings (File ▸ Build Settings)

| Setting | Value | Why |
|---|---|---|
| Development Build | **ON** | the recorder only exists in dev builds |
| Autoconnect Profiler | **OFF** | streaming to the Editor costs ~2 ms/frame and hides the real numbers; the recorder writes locally instead |
| Deep Profiling Support | **OFF** | distorts everything and shrinks the capture window |
| Script Debugging | OFF | |

Build, then run the exe. Use your normal save. Graphics as you normally play.

## 2. What you see

Top-right there is an orange **PERF TRACE** box: `recording`, a mark counter,
and the toggle list. **Do not use F-keys during the run** — F6–F9 currently
fire two debug tools at once (FPSOverlay and LightingDebugToolbox share them).

## 3. The keys (number pad, **Num Lock ON**)

| Key | Does | Restore |
|---|---|---|
| **Num +** | MARK — a bookmark in the file ("mark 2 = village") | — |
| **Num -** | SNAPSHOT — a 300-frame Profiler capture of the next ~5 s | — |
| Num1 | every point + spot light OFF | press again |
| Num2 | every point + spot light → cheap vertex lighting | press again |
| Num3 | the village OFF | press again |
| Num4 | MSAA OFF | press again |
| Num5 | all shadows OFF | press again |
| Num6 | grass OFF | press again |
| Num7 | shadow cascades 4 → 2 and shadow distance → 100 m | press again |
| Num8 | point/spot lights stop lighting the planet mesh | press again |
| Num9 | pixel-light cap 64 → 8 | press again |

No number pad: PgUp/PgDn moves the `>` in the box, Delete flips it; Home = mark, End = snapshot.

Rule for a clean A/B: **one toggle at a time**, hold it ~5 s while keeping the
same view, then press it again to restore before the next.

## 4. RUN 2 (≈8 min) — the levers run 1 pointed at

Stand still at each station; look, don't walk, while a toggle is on.

1. **Field.** Cabin area, daytime if you can, nothing special in view: **Num+**. Then each of these for 5 s, restoring between: **Num4**, **Num7**, **Num5**, **Num6**, **Num8**, **Num9**.
2. **Village.** Stand in it, worst view: **Num+**, **Num-**, hold 5 s. Then each for 5 s, restoring between: **Num1**, **Num2**, **Num8**, **Num9**, **Num4**, **Num7**.
3. **Moon.** Constant Companion in the sky: **Num+**, **Num-**, hold 5 s. Then: **Num1**, **Num2**, **Num8**, **Num9**, **Num4**.
4. **Night** if the day allows: same field spot at night with fireflies about: **Num+**, then **Num1**, **Num8**, **Num2**.
5. Quit (Alt+F4 fine).

Tell me "1 field, 2 village, 3 moon, 4 night".

## 5. Where the files are

`%AppData%\..\LocalLow\DefaultCompany\Solar System 2\perf\`
(Tools ▸ Solar System ▸ Perf ▸ Open Perf Folder opens it from the Editor.)

- `trace_<date>.csv` — the whole run, one row per frame.
- `<date>_snapNN_markM.raw` — Profiler snapshots (~150 MB each). Claude turns
  them into `.txt` with Tools ▸ Solar System ▸ Perf ▸ Dump Profiler Snapshots.

Analysis: `py -3 tools/perf/analyze_perf_trace.py` (newest trace).

## 6. Note on the Editor profiler

Preferences ▸ Analysis ▸ Profiler ▸ Frame Count is now forced to 2000, which is
Unity's hard maximum (~40 s at 50 fps). That is why the trace file exists.

## 7. Run 1 findings (2026-09-11 19:12, laptop RTX 4060, 1080p, 12 min)

Settings the build ran with: **MSAA 4×, 4 shadow cascades, shadow distance 193 m,
pixel-light cap 64** (your saved prefs; the code defaults are the same maxed preset).

- **Both the CPU and the GPU are at the wall, and they are about the same height.**
  Field: main thread 15–20 ms, GPU 14–20 ms. The main thread spends ~3 ms of that
  waiting for the GPU (`DXGI.WaitOnSwapChain`); the render thread is idle 13 ms
  waiting for the main thread. Sea/sky view: GPU 4.9 ms, main 9.8 ms → the CPU floor
  is ~10 ms (≈100 fps) even with nothing to draw.
- **Point lights re-draw the whole planet mesh.** Lights OFF while looking at the
  moon: 41 M → 15 M triangles, −1.8 ms; in the village −2.0 ms. Every point light
  whose range touches a planet mesh re-renders that entire 2 M-triangle mesh (14
  lanterns, 33 tunnel lights, up to 12 firefly lights). Standing still in the field
  fps fell 65 → 50 as night came (fireflies + lanterns).
- **The moon base is CPU-only:** moon base OFF cut draws 3441 → 964 and 2.5 ms of
  main-thread render work, but the GPU didn't move and fps barely did — it's hidden
  behind the GPU wall today, and will show once the GPU is fixed.
- **CPU floor, per frame:** grass 2.3, space dust 1.4, Camera.Render ×4 cameras
  3.6 (helmet-HUD rig camera + thrust indicator camera render every frame),
  canvases 1.5, Update 1.1, physics 0.8, skinned-mesh finalize ~1.0 (the cats).
- Snapshot .raw files were 300–400 MB each at 600 frames → now 300 frames.
