🟢 ACTIVE — written 2026-09-11 for the FPS hunt. Runs 1 and 2 done the same evening (findings in §7/§8). §4b is the RUN 3 check.

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

## 4b. RUN 3 — check the fixes (no toggles needed)

Same dev build settings, rebuild. Play the same three spots: field, village, moon,
and at night if you can. **Num+** at each. No numbered keys (Num8 would fight the
new light gate). Then tell me the marks and whether anything LOOKS wrong:
lantern glow on the ground when you stand next to one (should be unchanged),
lantern glow on the ground seen from far away (now gone past ~33 m), firefly
glow on the ground (now vertex-lit: softer, and only the 4 nearest bugs light
the ground), moon-base interior lighting (unchanged).

## 4c. RUN 4 — planet chunking (the light workaround is reverted; the look is meant to be IDENTICAL)

Rebuild (dev build fine, PerfTrace still records). Play field, village, moon, night.
Watch fps and watch for anything that looks different from before: the ground
itself (seams, holes, missing patches, wrong colours), lanterns/fireflies/torches
lighting the ground at night (should be exactly as before), shadows on the ground,
the moon tunnel. The Player.log will show lines like
`[PlanetChunker] Humble Abode: 2097152 tris → 96 chunks (K=4) in NNN ms`.
Kill switch if anything is wrong: `FeatureVault.PlanetChunks = false`.

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

## 8. Run 2 (19:47, same settings) — what each lever is worth, measured

| Lever | Field, day (100 fps) | In the village (57 fps) | Looking at moon base (65 fps) | Night field (50 fps) |
|---|---|---|---|---|
| all point lights OFF | 0 | −2.3 ms → 68 fps | −4.4 ms → 102 fps | −0.2 (no lights in view) |
| lights vertex-lit | — | **−4.3 ms → 75 fps** | −4.2 ms → 98 fps | **−5.3 ms → 69 fps** |
| lights skip planet mesh | 0 | −2.2 ms → 65 fps | −4.2 ms → 85 fps | −1.9 ms → 63 fps |
| pixel-light cap 64→8 | 0 | +0.3 (worse) | −2.4 ms → 83 fps | — |
| MSAA off | **0** | **0** | — | — |
| cascades 2 + shadow dist 100 | 0 | −1.6 ms → 66 fps | — | — |
| shadows off | −0.4 | — | — | — |
| grass off | −1.3 ms → 118 fps | — | — | — |

Read: the GPU cost is the point lights re-drawing the planet mesh; MSAA is free
on this GPU; the day field is CPU-bound (~9.7 ms) with grass the biggest single
script. Applied after run 2: `PlanetLightGate` (far lights skip the planet mesh),
tunnel cage lights never touch the moon mesh (prefab mask), fireflies vertex-lit.

## 9. Runs 5-6 (20:40 / 20:50) and the final pass — what actually shipped

Run 5 (after grass cache + dust + HUD half-rate): no change (75 fps). The profiler
showed why: the two HUD render-texture cameras cost 3.4 + 2.5 ms **per render**.
Run 6 (after giving them private layers): still 3.1 + 2.4 ms — culling was only 0.1 ms
of it; the rest was Unity rebuilding every dirty canvas (1.5 ms) and finalizing all
renderer bounds (0.8 ms) *inside* each mid-LateUpdate `Camera.Render()`.

Shipped after run 6, all look-neutral:
- **HUD cameras render at end of frame** (after the main camera): the rebuild and the
  bounds work are already done by then, so the same render costs a fraction. One frame
  of latency on the helmet HUD / thrust gauge textures.
- **PlanetOcclusionCuller**: the moon base, tunnel rig and village are switched off
  while a planet is between them and the camera or they are below the horizon (Sam's
  "moon dipped behind the horizon but fps stayed low"). Their lights also go off
  beyond 20× range (min 150 m), where they cannot reach a pixel anyway.
- OrbitClockProbe no longer writes a CSV in real builds.

Measured per-frame CPU after run 6 (dev build, profiler attached, 14.4 ms): grass 2.4,
HUD cameras 2.8, main camera 2.8, dust 0.9, Update scripts 0.9, physics 0.7,
canvases 0.6, profiler 0.6. Expected release build: ~10 ms in the field.

**Two menu settings worth more than any remaining code change** (both measured):
- Shadow cascades 4 → 2 and shadow distance 193 → 100: −1.6 ms in the village.
- Grass distance 1.81× → 1.25×: ~2× fewer blades; the grass loop (2.4 ms CPU) and the
  village's GPU cost (13 ms looking at the ground) scale with it.

## 10. Milestone `perf-milestone-2026-09-11` (release build: 100-120 fps with grass, 130-150 without)

Tag on SoundOfSpace. Everything up to GPU skinning / IMGUI / horizon culler / HUD
end-of-frame. Sam's A/B in that build: grass 0 → 130-150 fps, grass on → 100-120.

Shipped after the milestone (untested, `FeatureVault.GrassGpuBatches` to revert):
**grass batches kept on the GPU** — blade matrices are planet-relative and reused
across frames; only streaming or moving a cell rebuilds them. Shader change in
CG_SimpleGrass / CG_GrassDepth (`_GrassInstanceOffset`). What to look for: grass
seated exactly as before (no float / sink / drift as the planet moves), no popping
at the screen edges when turning, depth silhouette against the sky unchanged in a
BUILD, and the fps gap between grass on/off shrinking to the GPU share only.
