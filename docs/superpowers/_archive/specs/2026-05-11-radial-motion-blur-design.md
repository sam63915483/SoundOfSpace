# Radial Motion Blur — Design

**Date:** 2026-05-11
**Scope:** Replace (or coexist alongside) the UI-overlay speed lines with a screen-space radial motion-blur shader. Apply ONLY *after* the atmosphere/planet/ocean post-processing chain so it can't possibly disturb that pipeline.

## Goals

- A per-pixel radial blur centered on the player's projected velocity vector — pixels far from the VP get smeared in the radial direction, pixels at the VP stay sharp.
- Strength scales with relative-to-planet speed: 0 below ~8 m/s, ramps to a tasteful max around 100 m/s (we cap intensity well below "full screen blur").
- Works in BOTH on-foot (jetpack/falling) AND ship-piloting contexts, like the speed-line overlay does.
- Individually toggleable from the pause menu **CAMERA** tab so it can be disabled or co-toggled with the existing speed lines.

## Non-goals

- **Never touch the existing atmosphere/planet/ocean shaders**, the `Planet Effects/` folder, `CustomPostProcessing.cs`, `Celestial/`, or any of the locked-zone files in CLAUDE.md. Our new effect lives ENTIRELY as a sibling component + shader. The existing pipeline is read-only for us.
- No 6-axis motion blur (translational + rotational separately). Just radial-from-VP, which is what reads as "speed" anyway.
- No depth-aware blur. Sampling pure 2D screen space — the smear hits sky, foreground, and HUD-canvas-overlay-targets equally. (We can revisit if HUD smearing turns out to bother us.)
- No replacement of the existing speed-line system — both coexist with their own toggles. User compares in-game and picks.

## Architecture

### Why this fits without breaking atmosphere

The atmosphere/planet/ocean chain lives inside `CustomPostProcessing.OnRenderImage`, which is marked `[ImageEffectOpaque]`. Unity runs `[ImageEffectOpaque]` callbacks BEFORE transparent geometry and BEFORE non-opaque `OnRenderImage` callbacks. So:

```
[ImageEffectOpaque] CustomPostProcessing.OnRenderImage   ← atmosphere/planet/ocean composite happens here
        ↓
transparent geometry draws on top
        ↓
[OnRenderImage]    RadialMotionBlurEffect.OnRenderImage  ← OUR new effect runs HERE
        ↓
final screen output
```

By NOT marking our new component `[ImageEffectOpaque]`, Unity automatically schedules it after the atmosphere chain. The atmosphere is already fully composited into the source texture by the time our blur reads it; we just smear the final image. Nothing about the atmosphere code path changes.

This is the same render-queue rule the existing `Bloom`/`FXAA` siblings already navigate (they're listed in `CustomPostProcessing.effects` and pass through the same chain). We're adding an even *more* downstream effect that runs entirely outside that array.

### Components

```
Assets/3 - Scripts/Camera/
├── RadialMotionBlurEffect.cs        ← new MonoBehaviour, attached to the main camera
└── RadialMotionBlur.shader          ← new ImageEffect-style shader, samples the source N times along the radial axis
```

`RadialMotionBlurEffect`:

- Attached to the main camera (same GO as `CustomPostProcessing` + `CameraShake` etc.).
- Holds a reference to `RadialMotionBlur.shader` (loaded by name via `Shader.Find` in Awake, like the other effects in this project).
- Each frame:
  1. Compute strength (`0..1`) from `_player.RelativeVelocity` / `_ship.velocity` — same logic as `SpeedLinesOverlay.ComputeTargetIntensity()`. Smooth-damped so it doesn't snap.
  2. Compute center (VP) via the same perspective-projection math `SpeedLinesOverlay.ComputeVanishingPoint()` uses — but expressed as UV-space (0–1) instead of px, since the shader works in UV. Smooth-lerped.
  3. In `OnRenderImage`, set shader params (`_Center` Vector2, `_Strength` float) and `Graphics.Blit(source, destination, material)`.
- Reads the master `fxRadialMotionBlur` toggle from `InputSettings`. When disabled, copies source → destination without applying the shader (cheap pass-through so we don't break the camera output).

`RadialMotionBlur.shader`:

Image-effect-style fragment shader. For each output pixel:
- Compute `dir = uv - center`
- Compute `dist = length(dir)` (squared falloff so pixels near center are barely affected)
- Sample the source N times along that direction at distances `[-half, +half] * blurAmount * dist`, average them
- Output the average

```hlsl
// Pseudocode
float2 dir = (uv - _Center);
float dist = length(dir);
float amount = _Strength * dist * 0.08; // scale tuned in-engine
float4 sum = 0;
const int N = 10;
for (int i = 0; i < N; i++) {
    float t = (i / float(N - 1)) - 0.5; // -0.5 to 0.5
    sum += tex2D(_MainTex, uv + normalize(dir) * t * amount);
}
return sum / N;
```

10 samples is the cheap-but-noticeable sweet spot; we can tune to 6 or 14 later.

### Settings integration

`InputSettings.cs` — add ONE new field next to the other `fx*` toggles:

```csharp
public bool fxRadialMotionBlur = false;  // default OFF so existing players don't get surprised
```

Plus its PlayerPrefs load/save line in the existing `LoadSettings` / `SaveSettings` blocks (matching the pattern Task 1 of the camera-effects plan established).

`TabbedPauseMenu.cs` — add ONE `ToggleDef` to the existing `CAMERA` tab, in the "LENS CHARACTER" section (it's a screen-space "lens-y" effect):

```csharp
new ToggleDef {
    label = "RADIAL MOTION BLUR",
    get = () => _input != null && _input.fxRadialMotionBlur,
    set = v => { if (_input != null) _input.fxRadialMotionBlur = v; }
},
```

That's the entire UI surface. No new tabs, no new sliders. If we want intensity tuning later, add a slider; for v1 we hardcode the constants in the shader/script.

## Effect tuning (initial constants)

| Constant | Value | Why |
|---|---|---|
| `Threshold` (m/s) | 8 (player) / 12 (ship) | Above a normal jump's peak. Matches the speed-line thresholds for consistency. |
| `FullAt` (m/s) | 100 | Saturates well below the player's 200+ m/s top speed. |
| `MaxStrength` | 0.45 | The `_Strength` value at full intensity. Above ~0.6 the blur reads as "the player is dying" rather than "fast" — keep it tasteful. |
| Sample count | 10 | Cheap; produces a smooth-enough smear at typical strengths. |
| Center smoothing | 4/s lerp | Same as SpeedLinesOverlay — direction changes don't snap. |
| Strength smoothing | 2.5/s MoveTowards | Same. |

Pixel-cost ballpark: 10 texture samples per pixel at 1080p × 60fps ≈ 1.3 GS/s. Trivial for any GPU that already runs the atmosphere shader.

## Risk mitigation — atmosphere

The single rule: **do not modify any locked-zone file** (CustomPostProcessing.cs, anything in `Planet Effects/` or `Celestial/`, any shader/compute under those folders, the planet/ocean/atmosphere materials).

Our new effect lives entirely outside that zone:
- New `.cs` file in `Assets/3 - Scripts/Camera/` (already a project-OK folder).
- New `.shader` file in the SAME folder (NOT under `Planet Effects/` or `Celestial/`).
- The MonoBehaviour attaches to the camera at runtime via inspector or auto-attach in `CameraEffectsManager.AttachModules`. No scene-file edits required if we go the auto-attach route.
- The shader uses `Cull Off`, `ZWrite Off`, `ZTest Always` — the standard image-effect shader template — so it can't interact with the depth or stencil buffers.

If the effect causes a visual regression we can't immediately fix:
- The toggle defaults to OFF, so existing users / save-load defaults skip the effect entirely.
- The `Graphics.Blit(src, dst)` pass-through path in `RadialMotionBlurEffect.OnRenderImage` keeps the camera output untouched when disabled.
- Worst case rollback: delete the two new files and the one new field on `InputSettings`. No code in the locked zone changes.

## Implementation phases

1. **Shader.** Write `RadialMotionBlur.shader`. Compile-check via `mcp__coplay-mcp__check_compile_errors`.
2. **Effect component.** Write `RadialMotionBlurEffect.cs`. Auto-attach to the camera in `CameraEffectsManager.AttachModules`. (Same pattern as the existing modules — but this one is attached to the Camera GameObject, not the manager's own GameObject, because `OnRenderImage` only fires on components ON the camera.)
3. **Settings field.** Add `fxRadialMotionBlur` + load/save lines on `InputSettings`.
4. **Pause-menu toggle.** Add the `ToggleDef` row to `TabbedPauseMenu.BuildSettingsList`'s `CAMERA` tab under `LENS CHARACTER`.
5. **Validation pass.** Enable in Play mode, sprint + jetpack at high speed → blur should activate radially. Disable → camera output identical to a fresh checkout. Verify atmosphere/planet/ocean rendering looks pixel-identical in BOTH the OFF state (sanity check) and the ON-at-low-speed state (when strength is 0, output should be ~identical).

## Out of scope (deliberately punted)

- Per-effect intensity slider in v1 — locked to `MaxStrength = 0.45`. Add a slider later if 0.45 feels wrong.
- Depth-aware blur (only blur foreground geometry, leave sky sharp). Possible but needs depth-texture sampling and risks interactions with the depth pipeline atmosphere depends on. Punted.
- Vignette + blur combo (darken edges as blur intensifies). Could be nice but better to land radial blur alone first.
- Removing the speed-line overlay. Coexist with toggles; user picks in play.
