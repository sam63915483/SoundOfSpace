# Controller pass 2: virtual cursor + pad bug fixes — design

Date: 2026-09-06. Branch: `feat/helmet-hud`. Approved by Sam in conversation.

## Goal

Controller support currently "works": the pad drives menus by hopping a yellow
highlight box between buttons (`ControllerUINavigator`). Sam's 2026-09-06 pad
playtest found the seams: dialogue options can't be scrolled, the NAV app
strands the pad at the 100 m hover, TRAX is un-drivable with a highlight box
(too many buttons and dials), and several screens have no B-to-close.

This pass replaces the highlight box with a **Cyberpunk-style virtual cursor**
on every mouse-driven screen, keeps the highlight box only for dialogue option
lists, and fixes the specific pad bugs found. **No button remapping** — the
existing binds (A/B/X/Y, bumpers, triggers, D-pad, Start/Back) stay exactly as
they are. Sam wants to playtest this **once**, so the design favours one
automatic rule over per-screen wiring.

## Non-goals

- No new `.inputactions` asset. Pads keep reading `Gamepad.current` through
  `TutorialGate`; UI keeps the built-in `DefaultInputActions`.
- No rebinding UI, no cursor themes, no per-screen cursor speeds.
- No change to keyboard/mouse behaviour anywhere. Every gate below is on the
  pad path only.

---

## 1. The cursor (`UI/PadCursor.cs`, new)

### Mechanism

Unity's Input System 1.14 ships `UnityEngine.InputSystem.UI.VirtualMouseInput`.
It adds a virtual `Mouse` device driven by a stick and pushes position /
button / scroll state into it. `InputSystemUIInputModule` (already installed
by `ControllerUINavigator.EnsureEventSystem`, default actions bind
`<Mouse>/position` etc.) treats it as a real pointer, so **every UGUI canvas
gets hover, click, drag, and scroll from the pad with no per-screen code.**
The module's default `pointerBehavior` unifies all mice into one pointer;
whichever device moved last owns it, which is exactly the "stick vs desk
mouse" hand-off we want.

Rejected alternatives: a hand-rolled pointer that synthesises
`PointerEventData` (re-implements the module's enter/exit/drag state machine;
fragile) and warping the OS cursor with the stick (fights `Cursor.lockState`,
jitters in windowed mode, invisible while the navigator hides the OS cursor).

### Ownership / lifetime

- `PadCursor` is a component on the `[ControllerUINavigator]` GameObject,
  created in `ControllerUINavigator.Awake` (so it exists in MainMenu and the
  gameplay scene alike — no trap #1 seeding needed; the navigator is already
  DontDestroyOnLoad and auto-created AfterSceneLoad in every scene).
- It owns one `VirtualMouseInput` (mode `SoftwareCursor`) configured in code:
  stick = `<Gamepad>/leftStick`, left button = `<Gamepad>/buttonSouth` (A),
  right button = `<Gamepad>/buttonWest` (X — already the RMB twin in
  StorageUI/FishStagingUI), scroll = `<Gamepad>/rightStick` (Y axis; X
  ignored). Actions are created with `new InputAction(binding: ...)` so no
  asset is needed. The virtual mouse device is added once and kept; when the
  cursor is inactive the component is disabled so its position stops
  changing (a stale pointer that never moves is ignored by the module).
- Cursor graphic lives on its own `ScreenSpaceOverlay` canvas, sorting order
  32001 (above the navigator's border canvas at 32000), `ConstantPixelSize`,
  **no GraphicRaycaster** (the cursor must never hit itself). Modelled on
  `ControllerUINavigator.BuildBorderUI`.

### Activation rule (the load-bearing part)

`PadCursor.IsActive` is true when ALL of:

1. `TutorialGate.ControllerEnabled`
2. `Cursor.lockState == CursorLockMode.None` — every mouse-driven screen in the
   game unlocks the OS cursor on open and re-locks on close (main menu, pause,
   settings, save slots, storage, every vendor, galleries, shuttle computer,
   phone home/apps, map in unlocked mode). This is the single signal that "a
   mouse screen is up".
3. `TutorialGate.LastSource == InputSource.Controller` — the mouse moving
   flips this to KBM, the ring hides, the OS cursor shows (existing
   navigator behaviour); any stick/button flips it back (stick deflection
   already counts as controller activity in `TutorialGate.Tick`).
4. No **focus owner** is topmost (§4) — dialogue option lists keep the
   highlight box.
5. No suppression token is held (§6 NAV hover, §7 phone camera mode uses the
   lock instead).

Transitions: on becoming active the cursor spawns at screen centre (and again
each time it re-activates — no position memory across screens). On becoming
inactive the graphic hides and the component disables. `PadCursor` evaluates
this every frame (cheap: five bools); it never scans the scene itself.

Optional bounds: `PadCursor.SetBounds(RectTransform)` / `ClearBounds()` clamp
the cursor inside a rect in screen space (used by the phone, §7); otherwise
the cursor clamps to the screen.

### Movement feel

- Speed: `baseSpeed * Screen.height / 1080 * cursorSpeedMultiplier`, base
  1400 px/s at full deflection. Deflection response is squared (fine control
  near centre, fast at the rim). Deadzone = `TutorialGate.StickDeadzone`.
- `cursorSpeedMultiplier` 0.5–2.0, default 1.0. Stored in PlayerPrefs
  (`padCursorSpeed`) via `InputSettings` next to `StickLookSensitivity`, with
  a "Cursor speed" slider in the pause menu CONTROLLER tab (mirror the
  existing stick-sensitivity slider row exactly).
- Right stick Y → scroll wheel notches (scroll lists, spin TRAX knobs, map zoom).

### Look and press animation

- Ring: thin white circle (2 px stroke, ~26 px diameter at 1080p, scales with
  screen height) with a 1 px dark outer edge at ~50 % alpha so it reads on
  light and dark backgrounds. Sprite generated procedurally at start (like
  `MakeBorderSprite`); no art asset.
- Press: on A down the ring morphs into a solid dot — implemented as a ring
  Image and a dot Image crossfading: ring scales 1 → 0.35 and fades out, dot
  scales 0 → 1 and fades in, ~0.08 s ease-in. On release the reverse over
  ~0.12 s with an ease-out-back overshoot (~1.15) so it "pops" back. Held A
  keeps the dot (drag feedback). Timings are unscaled (menus can be paused).

### Legacy mouse shim

Seven places read the raw mouse instead of Unity's pointer. Each becomes a
one-line call to `PadCursor.PointerPosition` (virtual position when active,
else `Input.mousePosition`), `PadCursor.PrimaryDown` / `PrimaryHeld` (A when
active, else LMB — wraps `TutorialGate.PrimaryActionPressed` semantics), or
`PadCursor.ScrollDelta` (right-stick notches when active, else
`Input.mouseScrollDelta.y`):

| File | Line(s) | Read |
|---|---|---|
| `UI/StorageUI.cs` | ~255 | drag ghost follows `Input.mousePosition` |
| `UI/FishStagingUI.cs` | ~250 | same |
| `Vendor/MushroomSellUI.cs` | ~334 | custom cursor image follows mouse |
| `Map/SolarMap.cs` | ~188–194 | click raycast + `mousePosition` + `mouseScrollDelta` zoom |
| `Music/ShuttleComputerNavUI.cs` | ~429 | planet pick `GetMouseButtonDown(0)` |
| `Music/ShuttleComputerCoopUI.cs` | ~374–379 | publishes cursor to co-op partner |
| `UI/NoteReadUI.cs` | ~131 | advance on click (already also pad A; shim for consistency) |

`GhostPlacement` RMB, `TrailerFreeCam`, `MenuCameraRig`, `TreeGalleryFlyCam`
are gameplay/camera reads, untouched.

---

## 2. Gameplay stays muted while the cursor is up

All through `TutorialGate` (the one input choke point) — pad branch only,
keyboard unchanged:

- `MovementInputSuppressed` gains `|| PadCursor.IsActive`.
- `MoveAxisHorizontal/Vertical`, `LookDelta`, `RightStickX/Y`: the **pad**
  contribution is 0 while `PadCursor.IsActive` (left stick is the cursor,
  right stick is scroll).
- `JumpPressed/JumpHeld`, `PrimaryActionPressed`, `InteractPressed`,
  `DropPressed`, `FirePressed`, `SecondaryFire*`: pad contribution suppressed
  while `PadCursor.IsActive` **except** `PrimaryActionPressed`, which the
  shimmed UI reads (map planet click, NAV planet pick) rely on. Rule of
  thumb: while the cursor is up, A/X are mouse buttons and nothing else.
- `PlayerController`'s raw reads (`Input.GetKeyDown(Space) ||
  TutorialGate.PadPressed(A)` at ~758, swim-up at ~1403) get the same
  `!PadCursor.IsActive` guard on the pad half.
- `PlayerPhoneUI` movement-close: `padMoving` gains `&& !PadCursor.IsActive`
  (belt and braces — the axes are already 0). On pad the phone is put away
  with B; the "don't move" warning is keyboard-only from now on.

`ControllerUINavigator` while `PadCursor.IsActive`:

- Does **not** force-migrate selection and clears any stray selection (same
  branch it already runs for KBM mode, keeping the InputField exception) —
  so no yellow box and no auto-focused button. Buttons highlight on hover via
  their normal ColorBlock, like a mouse.
- Still runs `UpdateRaycasterSuppression` (protects clicks from reaching
  canvases under a modal).
- Still hides the OS cursor for pad users.

---

## 3. B / Circle closes everything

Existing B handling stays. Add pad B where only Escape works today:

| Screen | File | Today | Change |
|---|---|---|---|
| Tev payment | `Vendor/TevPaymentUI.cs` ~148 | ESC | ESC or `TutorialGate.CancelPressed()` |
| TRAX print dialog | `Music/ShuttleComputerUI.cs` ~427–429 | ESC | + pad B (closes the dialog only) |
| Shuttle computer sub-dialogs | `Music/ShuttleComputerUI.cs` ~421 | ESC | + pad B |
| Shuttle computer main | `Music/ShuttleComputerUI.cs` ~436, 452–457 | ESC / F | + pad B: backs out of an open sub-view/dialog first, then closes |

Where a screen has nested levels (phone app → home → closed; computer dialog
→ app → closed) B backs out **one level per press**, matching the phone's
existing behaviour.

---

## 4. Dialogue option lists keep the highlight box — and get fixed

Surfaces: `NPC_Dialogue/PostGreetingChoicePanel.cs` (every graph-walker NPC,
fish vendor rows, guitar shop, bonfire, Tev onboarding, space-dust sell) and
`Story/WorldDialogueUI.cs` (Tev letter / ORG interview — already works;
receives only the marker + B handling).

Root causes found (PostGreetingChoicePanel):

1. `Show()` never selects a first row; it relies on the navigator's 0.25 s
   rescan.
2. Rows fade in from `CanvasGroup.alpha = 0`; the navigator treats alpha < 0.05
   as invisible, so during the fade every row is "invalid" and selection is
   set to null.
3. `Button.transition = None` and `PhosphorRow` only implements
   `IPointerEnter/Exit` — stick focus never paints the highlight.
4. The panel re-asserts `Cursor.visible = true` every frame, fighting the
   navigator and flipping `LastSource` on mouse jitter.
5. sortingOrder 900 ties with StorageUI/Toast/WorldDialogueUI.

Fixes:

- New marker component `UI/ControllerFocusOwner.cs`. The navigator neither
  migrates nor clears selection while the topmost interactive canvas carries
  it, and `PadCursor` treats "focus owner topmost" as inactive (§1 rule 4).
  `PadCursor` gets this from a new `ControllerUINavigator.TopmostCanvas`
  property (already computed each scan; expose it).
- `PostGreetingChoicePanel.Show()`: add `ControllerFocusOwner`, wire explicit
  vertical `Navigation` (wrap top↔bottom), and select row 0 next frame
  (`WorldDialogueUI` pattern). While showing, if selection is null or not one
  of its rows, re-select the last-highlighted row (survives the fade-in).
- `PhosphorRow` implements `ISelectHandler/IDeselectHandler` → same paint as
  hover. Mouse hover also moves selection so KBM users see one highlight.
- Remove the per-frame `Cursor.visible = true` (line ~324); the `Show()` unlock
  stays.
- Bump `PostGreetingChoicePanel` to sortingOrder 905 (above toasts/storage;
  still under `FishStagingUI` 910 so the fish picker stacks on top). Keep
  `WorldDialogueUI` at 900 (never co-open).
- **Cancel:** pad B or ESC while the panel shows → `PostGreetingChoicePanel.
  Cancel()`: hides the panel and resolves the pending choice as **-1**.
  `NpcGraphWalker.ChooseOnSharedPanel` and every direct caller must treat -1
  exactly like the existing walk-away (`!inRange()`) path. The plan audits
  each caller (FishMarketNPC, ShipMarketNPC, Alien7Vendor, GuitarShopNPC,
  BonfireNPCDialogue, TevMushroomOnboarding, SpaceDustSellUI, AuthoredNPCTalk)
  and confirms -1 is safe (no `rows[-1]`). If a caller cannot tolerate a
  cancel (a forced beat), it passes `cancellable: false` to `Show` and B is
  ignored there.
- A/Cross picks (Submit → onClick, unchanged). Number keys unchanged.

---

## 5. Sapling / ghost placement: A places, never jumps

Bug: with a sapling equipped, pad A both places (`GhostPlacement` reads
`PrimaryActionPressed`) and jumps (`PlayerController` reads `PadPressed(A)`).

Fix: in `PlayerController`, the pad-A half of the jump read (and the
jetpack/boost-queue path it feeds) is ignored while `GhostPlacement.IsPlacing`.
Applies to every ghost placement (sapling, mushroom sapling, buildings) — same
bug class, same button. Keyboard is untouched (Space jumps, LMB places — no
conflict). Once the last sapling is placed the ghost ends, `IsPlacing` goes
false, and A jumps again — exactly Sam's ask.

---

## 6. Shuttle computer, NAV, TRAX

- Opening/closing: X opens (unchanged), cursor drives the whole screen, B
  closes per §3.
- NAV planet pick: shim (§1). Selecting a planet still launches the autopilot.
- **Hover hand-off** (`ShuttleComputerNavUI.NavInput`, ~443–465, keyboard
  only today): rewrite the reads through `TutorialGate` —
  - position: `MoveAxisHorizontal/Vertical` **raw** variants that bypass the
    cursor mute (`TutorialGate.MoveAxisHorizontalRaw` / `VerticalRaw`, new,
    identical minus the `PadCursor` gate),
  - yaw: Q/E or `RollLeftHeld/RollRightHeld` (LB/RB — the existing ship roll
    twins),
  - land: Space or pad A (`PadPressed(A)` raw).
  - While `ShuttleAutopilot.CurrentPhase == Hover` and the NAV view is showing,
    the NAV UI holds a `PadCursor.Suppress("nav-hover")` token so the ring
    hides and the left stick flies. Token released on any other phase or when
    the NAV view closes (also on `OnDisable`, so it can never leak).
  - Prompt strings (~395–396) become glyph-aware: on pad
    "● LANDING ZONE CLEAR · {A} TO LAND" / "● NO LANDING ZONE · {LS} POSITION ·
    {LB}/{RB} YAW" via `PromptGlyphs`; keyboard text unchanged.
- TRAX: nothing bespoke. Buttons/grids click; `TraxKnob` already implements
  `IDragHandler` (A held + stick) and `IScrollHandler` (right stick);
  arranger/projects/co-op are pointer-driven and work through the module.
  Co-op cursor publish shimmed (§1) so a partner sees the pad cursor.

---

## 7. Phone tablet

- Home grid and apps: cursor. `PlayerPhoneUI.AnimatePhone` open →
  `PadCursor.SetBounds(screenRect)` (the tablet's screen RectTransform);
  close / `ForceCloseNoAnim` → `ClearBounds()`. The existing pad forced
  selection of `_appButtons[0]` on open is removed for the cursor path
  (`MovementInputSuppressed` now comes from `PadCursor.IsActive`); the
  explicit grid `Navigation` wiring stays (harmless, keyboard arrows).
- B: app → home → closed (unchanged).
- Camera mode: already RT shoot, LT photo/video toggle, B back to home, C/F
  close all; it re-locks the OS cursor every frame so the ring hides by rule
  2 and look/walk are free (`LookBlocked` false, Update returns before the
  movement-close check). The plan includes a code-path verification of the
  pad look + walk + RT in camera mode (memory says it was never pad-tested)
  and fixes anything found; no behaviour change intended.
- The phone's own screen-space-camera flip tween (0.3 s) is unaffected — the
  bounds rect is re-read each frame from the RectTransform.

---

## 8. Solar map (`Map/SolarMap.cs`, the v2 map; legacy map stays vaulted)

- Opens locked (unchanged): sticks fly/look, Back/M or B closes.
- **Y** toggles `SetCursorLocked(!_cursorLocked)` on pad (G on keyboard,
  unchanged). Unlocked → OS cursor unlocked → `PadCursor` active by rule 2:
  left stick moves the ring, right stick Y zooms (shimmed `ScrollDelta`), A
  clicks a planet name / legend row / empty space (shimmed click), Y locks
  again. B closes from either mode (existing).
- Legend hint (`SolarMapOverlay` ~342) gains a pad line via `PromptGlyphs`:
  "{Y} cursor · {B} close" when `ControllerEnabled`.
- While locked, `PadCursor` is inactive so the flight controls read the sticks
  as before.

---

## 9. Main menu, pause menu, settings, save/load, storage, vendors, galleries

Cursor by rule 2, no code per screen. Two cleanups so the pad cursor can't be
silently disabled in a build:

- `ControllerUINavigator.EnsureEventSystem` becomes `public static`.
- The four places that re-add a legacy `StandaloneInputModule` behind the
  navigator's back (`Building/BuildMenuUI.cs` ~327, `Music/ShuttleComputerUI.cs`
  ~571–576, `UI/MainMenuController.cs` ~126–137,
  `Tutorial/TutorialPerformanceReview.cs` ~188) call
  `ControllerUINavigator.EnsureEventSystem()` instead.

The explicit `Navigation` wiring in StorageUI / FishStagingUI / SaveLoadUI
stays (keyboard arrows still use it; the pad no longer does).

---

## 10. Settings surface

Pause menu → CONTROLLER tab: new "Cursor speed" slider (0.5–2.0, default
1.0), same row style as "Stick sensitivity". PlayerPrefs key `padCursorSpeed`
in `InputSettings`, applied to `PadCursor.SpeedMultiplier`.

---

## 11. Verification

No CLI build/test in this project. Definition of done for the implementer:

1. `python prototypes/shuttle-computer/test/compile-unity.py` passes with
   **0 warnings** (baseline is zero; any warning is real).
2. A static trace, written into the plan's final task, of every
   `TutorialGate` pad read that still reaches gameplay while
   `PadCursor.IsActive` (there should be none except the raw NAV-hover reads).
3. `docs/PLAYTEST_CONTROLLER_CURSOR.md`: a one-pass checklist for Sam, one
   line per screen in §3–§9 with the expected pad behaviour, so a single
   playtest covers everything. Sam runs the playtest (hard rule: never enter
   play mode ourselves).
4. Audit §31 (controller) gets a dated addendum describing PadCursor, the
   activation rule, the focus-owner marker, and the suppress token.

## Risks / what could look different

- The yellow box disappears from every cursor screen, including storage and
  the fish picker where it currently works (Sam's "cursor only" call).
- A bumped desk mouse hides the ring until the stick moves again; the ring
  re-spawns at screen centre.
- A cancelled dialogue (B) behaves like walking away; NPCs that react to
  walk-away (e.g. "come back when you're ready") will say that on B too.
- Screens that never unlock the OS cursor but expect pointer input would get
  no cursor; the survey found none, but the playtest checklist calls each
  screen out so a miss is a one-line fix (`Cursor.lockState = None` on open).
