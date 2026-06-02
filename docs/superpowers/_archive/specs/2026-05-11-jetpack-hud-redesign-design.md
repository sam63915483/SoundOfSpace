# Jetpack HUD Redesign — V1 Classic Gimbal — Design

## Goal

Visual overhaul of the jetpack-equipped on-screen HUD. No functional changes — purely render. Single unified panel at bottom-left replacing the current 2-component layout:

- The current `GForceHUD` card (procedural, bottom-left): `SPEED 147 m/s` text + 3D cube-with-6-bars thrust gizmo.
- The current `BoostMeterUI` canvas (scene-side): three horizontal `Image.fillAmount` bars.

New unified panel layout (left → right): **vertical speed tape · 3D gimbal thrust widget · three vertical segmented fuel bars**, in the existing cyan/navy palette shared with `VitalsHUD`, `WaterFillHUD`, `PlayerWallet`.

Approved mockup direction: **V1 Classic Gimbal** (sphere + 3 perpendicular cyan rings + 6 cardinal pole lights), recolored to the locked palette:

- BG `#0A1828` @ 95% · Border `#78C8FF` @ 45% · LED accent `#5CC8FF` · Label `#EAF6FF` · Track-off `#0F192A` · Idle cyan `#5CC8FF` @ 20%.

## Non-goals

- No changes to data sources: `PlayerController.RelativeVelocity`, `JetpackFuelPercent`, `DownThrustFuelPercent`, `DirectionalThrustFuelPercent`, the piloted ship's rb velocity.
- No changes to thrust direction mapping (Ctrl→+Y, S→+Z, A→+X, Space→-Y, W→-Z, D→-X — the existing "thrust opposite to motion" convention).
- No changes to the `JetpackUnlocked` visibility gate (the panel stays hidden until the jetpack is purchased from Alien7).
- No save/load impact — the panel is stateless.
- No new player input or behavior.
- No theme variants — only the locked cyan/navy palette.

## Out of scope

- Animations / tweens between value changes (instant snap is fine, matching existing HUDs).
- Per-player customization or pause-menu toggles.
- Replacing the underlying jetpack/thrust mechanics. The forbidden-zone atmosphere/planet code is untouched.

---

## Part 1 · `GForceHUD` panel layout rebuild

### Surrounding card

Replace the current `BuildCanvas` vertical layout (label-on-top, 3D widget below) with a horizontal layout containing three children. Card anchor stays at bottom-left (anchor `(0,0)`, pivot `(0,0)`, `anchoredPosition = (leftOffset, bottomOffset)`).

- Card BG: existing `UIPanelSprites.GetBeveledPanel()` sprite, `CardBgColor = #0A1828F2`.
- Border: existing `UIPanelSprites.GetBeveledOutline()`, `CardBorderColor = #78C8FF73`.
- Left LED stripe: existing 3 px-wide cyan bar, `LedColor = #5CC8FFFF` — kept as-is.
- Outer layout: `HorizontalLayoutGroup` (childControlWidth = childControlHeight = true, childForceExpandWidth = false, spacing 14 px, padding `(26, 14, 12, 12)` to leave room for the LED stripe).
- Card width grows from ~200 px to ~330 px; height ~110 px. `ContentSizeFitter` set to `PreferredSize` on both axes so it auto-fits.

Three children, in order:

### Child 1 · Vertical speed tape (~56 px wide)

- Outer: `RectTransform` with a 1 px border (`CardBorderColor`) and `TrackColor #0F192AD9` background — same look as the `VitalsHUD` row track.
- Tiny `M/S` label, 8 px bold, `HeaderColor`, absolutely positioned top-left, slightly outside/overlapping the top border (to mimic a wire-tag).
- Five TMP_Text rows stacked vertically inside (`VerticalLayoutGroup`, spacing 2 px). From top to bottom:
  - Row 0: speed + 50, dim, 9 px, regular, `rgba(234,246,255,0.45)`.
  - Row 1: speed + 25, dim, 9 px.
  - Row 2: **current speed (highlighted)** — 14 px bold, `#7BE2FFFF`, with a track-fill behind it (`rgba(92,200,255,0.18)`) and 1 px top/bottom cyan separators.
  - Row 3: speed − 25 (clamped ≥ 0), dim, 9 px.
  - Row 4: speed − 50 (clamped ≥ 0), dim, 9 px.
- All five rows update together via the existing `_lastShownSpeed` change-detection in `Update()`:
  ```csharp
  if (speed != _lastShownSpeed) {
      _lastShownSpeed = speed;
      _tape[0].text = (speed + 50).ToString();
      _tape[1].text = (speed + 25).ToString();
      _tape[2].text = speed.ToString();
      _tape[3].text = Mathf.Max(0, speed - 25).ToString();
      _tape[4].text = Mathf.Max(0, speed - 50).ToString();
  }
  ```
- Font: existing resolved `_hudFont` (Techno SDF / LiberationMono fallback chain).

### Child 2 · 3D gimbal widget (~100 × 100 px, unchanged size)

The dedicated camera + RT + far-from-origin trick stays exactly the same. Only the in-widget geometry changes.

**Remove:**

- The central cube (`Center`) → replace with a sphere (see below).
- The 6 axis bars (`Bar_0..5`) and 6 tip cubes (`Tip_0..5`).

**Add (built in `Build3DWidget`):**

- **Central sphere** — `GameObject.CreatePrimitive(PrimitiveType.Sphere)`, scaled to `Vector3.one * 0.45`. Reuse the existing `CenterColor3D = (0.25, 0.50, 0.75, 1)` constant (currently used by the cube) so the central-element color stays consistent with prior widgets. Plain `Unlit/Color` shader — flat-shaded look matches the existing pole/bar style. Collider destroyed.
- **3 perpendicular rings** — each ring is a `LineRenderer` with `useWorldSpace = false`, `loop = true`, 64 positions evenly spaced around a unit circle of radius `0.9`. Width `0.025`. Material: `Sprites/Default` (transparent capable) tinted `LedColor = #5CC8FF`. Rendered at sort order safely behind the pole lights. Three rings, one per cardinal plane:
  - Ring XY: `localRotation = identity` (lies in the XY plane → "facing camera" ring).
  - Ring YZ: `localRotation = Quaternion.Euler(0, 90, 0)`.
  - Ring XZ: `localRotation = Quaternion.Euler(90, 0, 0)`.
- **6 pole-light spheres** at `±X`, `±Y`, `±Z`, each at distance `0.95` from origin (same outer radius as the rings — they sit ON the rings). Each sphere is a small `Sphere` primitive scaled to `Vector3.one * 0.16`, collider destroyed, with its own `Unlit/Color` material whose color is lerped each frame between idle and active. The 6 pole materials replace `_arrowMats[]` and `_currentArrowColors[]` arrays — same mechanism, same indexing.
  - **Index order MUST match `ReadThrustKeys()` exactly** (preserves the lighting logic):
    - 0 = +Y (Ctrl), 1 = +Z (S), 2 = +X (A), 3 = -Y (Space), 4 = -Z (W), 5 = -X (D).
  - Idle color: `ArrowIdle3D = (0.15, 0.30, 0.45, 1)` — already defined, unchanged.
  - Active color: `ArrowActive3D = (0.50, 0.95, 1.00, 1)` — already defined, unchanged.
- Camera offset unchanged: `(1.0, 1.3, -3.2)` looking at the widget origin, FOV 35°. The 3/4 angle keeps all 6 poles legible.

The existing `LateUpdate` that pins the widget 100,000 units above the main camera stays unchanged.

### Child 3 · Three vertical segmented fuel bars (~70 px wide total)

Built procedurally inside `BuildCanvas` (a new `BuildSegBars` method). One horizontal row of three columns, spacing 6 px between columns.

Each column:

- 14 px wide.
- 8 stacked `Image` segments (`segHeight = 6 px`, `segGap = 2 px`, total column height ≈ 62 px). Bottom segment fills first, building upward.
  - Idle segment color: `CellOffColor = #0F192AE6` bg, border via Image with `CellBorderOff = #5CC8FF35`.
  - Lit segment color: `LedColor = #5CC8FF` bg, brighter `#7BE2FF` border, `Image` color drives both.
  - (For simplicity, each segment is a single `Image` with a 1 px outline sprite; toggle its `color` field rather than swapping sprites.)
- 8 px bold label below, `HeaderColor`, letter-spacing 1 px, text = `"UP"` / `"DN"` / `"DIR"`.

Update each frame in `Update()`:

```csharp
int upLit  = Mathf.Clamp(Mathf.RoundToInt(_player.JetpackFuelPercent          * 8f), 0, 8);
int dnLit  = Mathf.Clamp(Mathf.RoundToInt(_player.DownThrustFuelPercent       * 8f), 0, 8);
int dirLit = Mathf.Clamp(Mathf.RoundToInt(_player.DirectionalThrustFuelPercent* 8f), 0, 8);

if (upLit  != _lastUp)  { _lastUp  = upLit;  RecolorSegs(_upSegs,  upLit);  }
if (dnLit  != _lastDn)  { _lastDn  = dnLit;  RecolorSegs(_dnSegs,  dnLit);  }
if (dirLit != _lastDir) { _lastDir = dirLit; RecolorSegs(_dirSegs, dirLit); }
```

Cached `_lastUp`, `_lastDn`, `_lastDir` start at `-1` (force initial paint). `RecolorSegs` iterates the 8 images and assigns `LitColor` or `IdleColor`. Per-frame allocation: zero — Color assignments are value-type, no string formatting, no GC.

---

## Part 2 · Hide the legacy `BoostMeterUI` canvas

The existing scene-side `BoostMeterUI` script references three `Image.fillAmount` bars in the scene's `--- UI ---` hierarchy. The new HUD owns the same display, so the legacy one must stop rendering — but the GameObject must stay (the scene references it).

Modify `BoostMeterUI.Update`:

```csharp
// If the new GForceHUD owns the boost meter rendering, suppress the legacy canvas.
if (GForceHUD.Instance != null && GForceHUD.Instance.OwnsBoostMeter)
{
    if (_cg != null) { _cg.alpha = 0f; _cg.blocksRaycasts = false; _cg.interactable = false; }
    return;
}
```

Add a read-only property on `GForceHUD`:

```csharp
public bool OwnsBoostMeter => true;
```

(Constant `true` — the new design always owns the boost meter. The property exists so a future toggle is trivial.)

Rationale: easier than deleting the scene canvas + breaking scene refs; cleaner than a runtime `Destroy(boostMeterUI.gameObject)` that would also require finding it. Pure additive change.

---

## Files affected

| File | Change |
|---|---|
| `Assets/3 - Scripts/Ship/GForceHUD.cs` | Rebuild `Build3DWidget` (sphere + 3 LineRenderer rings + 6 pole spheres). Rebuild `BuildCanvas` (horizontal layout: tape \| widget \| seg-bars). Add `BuildSpeedTape`, `BuildSegBars`, `RecolorSegs` methods. Add `OwnsBoostMeter` getter. Add `_tape[]`, `_upSegs[]`, `_dnSegs[]`, `_dirSegs[]`, `_lastUp/_lastDn/_lastDir` fields. Remove `BuildSpeedRow` and `BuildThrustWidget` (replaced). Widget size constants (`cardWidth`, `widgetSize`) adjusted. |
| `Assets/3 - Scripts/Ship/BoostMeterUI.cs` | Add `GForceHUD.Instance.OwnsBoostMeter` check at top of `Update` that zeroes `_cg.alpha` and early-returns. No other changes. |

No other files touched. In particular:

- `PlayerController.cs` — read-only consumers; signature of `JetpackFuelPercent` / `DownThrustFuelPercent` / `DirectionalThrustFuelPercent` / `RelativeVelocity` unchanged.
- `Ship.cs` — untouched.
- `SaveData.cs` / `SaveCollector.cs` — untouched (no new persistent state).
- `MainMenuController.cs` — already seeds `GForceHUD` via `EnsureGameplaySingletons`, no change needed.
- Atmosphere / planet / forbidden-zone files — explicitly untouched.

---

## Behavior parity (verification checklist)

| Behavior | Source of truth | Preserved? |
|---|---|---|
| Panel hidden until jetpack purchased | `_player.JetpackUnlocked` gate in `Update` | yes — same gate at top of `Update` |
| Speed source: player when grounded, ship rb when piloting | `ReadActiveVelocity` | yes — unchanged |
| Speed rounded to nearest integer | `Mathf.RoundToInt(vel.magnitude)` | yes — unchanged |
| 6-direction thrust lighting (opposite-of-motion convention) | `ReadThrustKeys()` index→key map | yes — unchanged, indices preserved |
| Idle/active color lerp at `dt * 10f` | `Color.Lerp` in `Update` | yes — same lerp, now applied to pole-sphere materials instead of bar materials |
| 3D widget kept off-camera at 100,000 units up | `LateUpdate` | yes — unchanged |
| Bottom-left anchor (≈ 20 px left, ≈ 170 px bottom from 1080p) | `leftOffset`, `bottomOffset` | yes — unchanged anchor; card just gets wider |
| Boost fuel values | `PlayerController.JetpackFuelPercent` etc. | yes — same accessors, now read by `GForceHUD` instead of `BoostMeterUI` |

---

## Risk register

- **LineRenderer rings rendering to RT at small UI size.** The RT is 192 × 192, the widget renders at ~100 px UI. LineRenderer width `0.025` at world-unit scale should anti-alias OK with 4× MSAA already set on the RT. **Mitigation:** if rings look stairy, bump `rtResolution` to 256, or fall back to thin Torus mesh procedurally generated at runtime.
- **Pole-sphere occlusion behind the central sphere.** At the fixed `(1.0, 1.3, -3.2)` camera offset, the back pole (+Z forward into screen) will be partially hidden by the central sphere. **Verdict:** acceptable — the lit pole that matters most is the active one, and at this angle all 6 still have at least their outer hemisphere visible past the sphere edge. If unreadable, tweak pole distance from `0.95` outward to `1.05`.
- **`Sprites/Default` material for LineRenderer color tinting** — works but doesn't bloom on its own. The cyan glow look in the mockup is from CSS `box-shadow`; in Unity the equivalent would need a bloom-post pass, which `Atmosphere`/post-process pipeline already provides for HDR colors. **Mitigation:** if rings look flat compared to the rest of the cyan UI, push the ring color brighter (`(2.0, 3.5, 5.0, 1.0)` HDR) so the existing Bloom picks it up. But: the indicator camera is dedicated and renders to RT with `allowHDR = false` — HDR bloom won't apply. So rings will render flat (matches the rest of the existing widget — current arrow bars are also flat-unlit). Accept the flat look; it's consistent with the existing widget.

---

## Open questions (none blocking — all defaults specified above)

None blocking. Reasonable defaults specified for every dimension. Iterate after seeing it in-engine if anything reads wrong.

---

## Acceptance criteria

- Pressing space (jetpack), Ctrl (down-thrust), or WASD (directional, airborne) lights the correct pole on the gimbal widget (parity with current cube-with-bars).
- Speed tape shows current m/s in the middle row, ±25 / ±50 in dim rows above/below; updates only when integer speed changes.
- Three vertical segmented fuel bars fill bottom-up; lit segments count matches `Mathf.Round(fuelPct * 8)`.
- Panel is hidden before purchasing the jetpack from Alien7, appears after.
- Colors match the existing `VitalsHUD` palette (BG `#0A1828`, LED `#5CC8FF`, label `#EAF6FF`).
- Old `BoostMeterUI` canvas is invisible (alpha 0) when the new panel is active.
- No new GC allocations in `Update` (verify with Profiler if doubtful).
