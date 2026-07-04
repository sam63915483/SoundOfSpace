# Controller Input Revamp — Design Spec

**Date:** 2026-07-04
**Status:** Approved
**Goal:** Full Xbox + PS4 (DualShock 4) + PS5 (DualSense) controller support, with rumble,
icon button glyphs, completed controller settings UI, and pad coverage across every
player-facing surface — replacing the fragile legacy-Input-Manager gamepad layer.

## Requirements (user-confirmed)

- **Controllers:** Xbox (XInput), DualShock 4, DualSense — all first-class.
- **Rumble:** yes (ship thrust/boost, landings, damage, pistol, axe, fishing bites, pickups).
- **Bindings:** fixed (no runtime rebinding UI). Preserve the current logical layout.
- **Prompts:** real icon glyphs on pad (TMP sprite tags); keyboard keeps bold text labels.
- **Coverage:** every player-facing screen/mechanic pad-playable. Dev tools stay keyboard-only.

## Current state (survey summary)

- Everything runs on the **legacy Input Manager**. `activeInputHandler: 2` ("Both").
  `com.unity.inputsystem@1.14.0` is in PackageCache (transitive) but unused by game code.
- **`Assets/3 - Scripts/Tutorial/TutorialGate.cs`** (633 lines) is the de-facto central input
  hub: ~35 static composite helpers (`JumpPressed`, `LookDelta`, `FireHeld`, …) consumed by
  ~50 gameplay files, plus tutorial ability gating, UI-focus arbitration
  (`UISelectionActive` / `WasUIFocusedThisFrameStart`), input-source tracking, and a
  hand-rolled Xbox-vs-DS4 layer (joystick-name sniffing, per-joystick 20-KeyCode-block math,
  duplicated `DS4_*` axes in InputManager.asset, StandaloneInputModule submit/cancel axis swap).
- **`Assets/3 - Scripts/UI/ControllerUINavigator.cs`** — good, keep: auto-select, focus ring,
  modal migration, raycaster suppression. `SkipControllerNav.cs` marker stays.
- **`InputSettings.cs`** holds controller settings fields (`stickLookSensitivity`,
  `shipStickSensitivity`, `stickDeadzone`, `invertLookY`, `controllerEnabled`,
  `vibrationEnabled`) with PlayerPrefs persistence and a push-bridge to TutorialGate.
- **Broken/half-finished:** vibration setting has no implementation (impossible on legacy
  input); stick sens/deadzone/invert-Y have no pause-menu UI; deadzone is stored but never
  applied; Reload has no pad binding; DualSense is unsupported (scrambled buttons);
  per-frame `GetJoystickNames()` string allocation runs twice per frame.
- **Coverage gaps (raw `Input.*`, no pad support):** PlayerPhoneUI, AIChatScreen, StorageUI,
  FishStagingUI, NoteReadUI, NewspaperReaderUI, MonumentLinkPopupUI, photo galleries,
  vendor UIs, MapCameraRig / SolarSystemMapController / MapTutorial, GhostPlacement /
  BuildMenuUI, cutscene skips, PostGreetingChoicePanel, tutorial step scripts.

## Chosen approach

**Migrate to Unity's new Input System behind the existing `TutorialGate` facade.**
The public API of `TutorialGate` and `PromptGlyphs` stays byte-for-byte identical; only the
internals change. Legacy backend stays enabled ("Both") so vendor/demo/debug scripts are
untouched. Rejected alternatives: hardening the legacy layer (no rumble possible, DualSense
mapping is a driver-dependent mess) and a hybrid detection-only approach (two input stacks
forever, DualSense still scrambled).

## Architecture

### 1. Foundation
- Add `com.unity.inputsystem` to `Packages/manifest.json` (pin the cached 1.14.0).
- Keep `activeInputHandler: 2`.
- New folder **`Assets/3 - Scripts/Input/`**:
  - `GameControls.inputactions` + generated C# wrapper (`GameControls.cs`).
  - `GamepadRumble.cs`, glyph support code.
- Two action maps, bound once against the generic `<Gamepad>` layout (Input System
  normalizes Xbox/DS4/DualSense automatically):
  - **Gameplay:** Move (WASD/arrows + left stick), Look (mouse delta + right stick),
    Jump (Space/South), Interact (F/West), Drop (G/East), SprintDirThrust (LShift/L3),
    DownThrust (LCtrl/R3), Flashlight (E/North), RollLeft (Q/LB), RollRight (E/RB),
    Fire (LMB/RT), SecondaryFire (RMB/LT), Reload (R/D-pad down — new pad binding;
    D-pad down also feeds HeadlightStep, so the pistol controller suppresses headlight
    stepping while the pistol is equipped — contexts are otherwise mutually exclusive),
    Pause (Esc/Start), Cancel (Esc/East), MapToggle (M/Select), TutorialAdvance (Tab/LT),
    HotbarCycle (D-pad left/right), HeadlightStep (scroll + D-pad up/down),
    HotbarSlot1-9 (number keys).
  - **UI:** Navigate / Submit / Cancel / Point / Click for `InputSystemUIInputModule`.

### 2. TutorialGate — same API, new internals
- Public surface unchanged: all composite helpers, `TutorialAbility` gating, save/load
  (`IsGateEnabled` / `GetUnlocked` / `ApplyState`), `UISelectionActive`,
  `WasUIFocusedThisFrameStart`, `LastSource`, `ControllerConnected`, `LTValue`/`RTValue`,
  D-pad edge helpers, `MoveAxisHorizontal/Vertical`, `LookDelta`, `HeadlightStep`.
- Internals read the generated wrapper (`WasPressedThisFrame()` / `ReadValue<>()`).
- **Deleted:** `Pad()`/`XboxButton`/`Ds4Button` + 20-KeyCode-block math, `LooksLikeDs4()`,
  `DS4_*` axis-name resolution, `ApplyInputModuleBindings()`, per-frame
  `GetJoystickNames()` scan, `IsAnyJoystickButtonDown()` 20-KeyCode loop.
- Device detection becomes event-driven (`InputSystem.onDeviceChange` + last-actuated
  gamepad); `DetectedController` enum extends to `{Xbox, DualShock4, DualSense}` —
  existing `== DualShock4` consumers keep compiling; glyph selection treats DS4 and
  DualSense both as PlayStation.
- `StickDeadzone` is finally applied (radial deadzone in facade stick reads).
- `EarlyDriver` (-9999, UI-focus snapshot) / `LateDriver` (+9999, edge-state advance)
  architecture stays — backend-independent and load-bearing.
- The `GetKey/GetKeyDown/GetAxis(string, ability)` legacy passthroughs stay for the few
  keyboard-only call sites that use them directly.

### 3. UI navigation
- `ControllerUINavigator.EnsureEventSystem()` swaps any `StandaloneInputModule` for
  `InputSystemUIInputModule` at runtime (covers all scenes incl. MainMenu — no scene
  surgery), wired to the UI action map. Submit natively maps to A/Cross per brand.
- Focus ring, modal migration, raycaster/Selectable suppression unchanged.

### 4. Rumble
- `GamepadRumble` static class ticked by the existing `[TutorialGateDriver]` GameObject
  (which already runs in MainMenu — no `EnsureGameplaySingletons` seeding needed).
- API: `Pulse(low, high, seconds)` one-shots + named continuous channels
  (`SetChannel(id, low, high)` / `ClearChannel(id)`); output = max across channels/pulses.
- Gated on `InputSettings.vibrationEnabled`; hard-stopped on pause, app focus loss,
  and device disconnect.
- Wire points: ship thrust/boost (continuous, throttle-scaled), landing thump, damage
  hits, pistol shot, axe chop impact, fishing bite, pickup confirm.

### 5. Glyphs
- TMP sprite asset from Kenney input-prompts (CC0): Xbox + PlayStation button sets.
- `PromptGlyphs` keeps its property API; on pad it returns `<sprite name="...">` tags,
  on KBM the existing bold text labels. All existing prompt/tutorial consumers get icons
  with zero call-site changes. Verify each consumer is TMP; any legacy `Text` consumer
  falls back to text labels.

### 6. Settings — CONTROLS tab
- Add rows to `TabbedPauseMenu.BuildSettingsList()` CONTROLS tab: stick look sensitivity,
  ship stick sensitivity, stick deadzone, invert Y, vibration (fields + persistence
  already exist in `InputSettings`).
- `controllerEnabled` default flips to **true** (auto-detected, not opt-in).
- No new serialized fields expected; if any are needed they append at the END of the class.

### 7. Coverage sweep
Route the raw-input holdouts through the facade / UI nav so every player surface is
pad-playable: PlayerPhoneUI, AIChatScreen, StorageUI, FishStagingUI, NoteReadUI,
NewspaperReaderUI, MonumentLinkPopupUI, PhotoGalleryUI/CommunityGalleryUI, vendor shop
UIs, MapCameraRig/SolarSystemMapController/MapTutorial, GhostPlacement/BuildMenuUI,
cutscene skips (DeathCutsceneController etc.), PostGreetingChoicePanel, tutorial step
scripts (_LegacySteps, BonusTutorial, TutorialStep).
**Intentionally keyboard-only:** TrailerFreeCam, FPSOverlay, CheatCodes,
LightingDebugToolbox and other dev tools.

## Error handling

- No gamepad connected → all pad reads neutral; KBM unaffected.
- Disconnect mid-play → rumble stops immediately; glyphs fall back to KBM on next
  KBM input; `ControllerConnected` clears via device-change event.
- Unknown/generic HID pad → treated as Xbox layout (best-effort, same as today).
- Missing sprite glyph name → PromptGlyphs falls back to the text label.

## Testing strategy

- Per phase: compile check + Editor play-mode smoke test (Unity MCP).
- Milestones: physical pad testing by the user (Xbox + PS pad).
- Final gate: **a Windows build test** — input behaves differently in builds
  (Windows.Gaming.Input pathway; this project has been bitten before). Verify pad
  works from MainMenu boot, glyph switching, rumble, and UI nav in the build.

## Out of scope

- Runtime rebinding UI (Input System makes it a later bolt-on).
- Steam Input integration.
- Keyboard-key icon glyphs (text labels stay).
- Local multiplayer / multiple simultaneous players.
