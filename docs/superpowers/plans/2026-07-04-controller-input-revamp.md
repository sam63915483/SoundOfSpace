# Controller Input Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xbox + DS4 + DualSense controller support via the Unity Input System, behind the unchanged `TutorialGate`/`PromptGlyphs` facade, with rumble, icon glyphs, completed settings UI, and pad coverage on every player surface.

**Architecture:** Keyboard/mouse reads stay on the legacy Input Manager (untouched, zero risk). Only the **gamepad** portion of every facade helper swaps to the Input System **device API** (`Gamepad.current` — buttons/sticks normalized across Xbox/DS4/DualSense by Unity's layout system). No `.inputactions` asset and no action maps: fixed bindings + single player means the action layer adds only indirection (deviation from spec §1, functionally identical; runtime rebinding remains possible later via a thin action layer or direct control remap). UI navigation swaps `StandaloneInputModule` → `InputSystemUIInputModule` with its default actions.

**Tech Stack:** Unity 2022.3 (built-in RP), `com.unity.inputsystem@1.14.0`, TextMeshPro sprite assets, Kenney Input Prompts (CC0). No asmdefs; everything is Assembly-CSharp.

**Branch:** `feat/controller-revamp` (already created; spec committed).

**Verification tooling:** No CLI tests in this project. After every code step: `mcp__coplay-mcp__check_compile_errors` (Unity must be open). Play-mode smoke tests via `mcp__coplay-mcp__play_game` / `get_unity_logs` where stated. The executor cannot press gamepad buttons — physical pad verification is batched into Task 12 for the user.

**Hard rules (from CLAUDE.md):**
- Never touch the forbidden atmosphere/celestial zones (none of these files are in them — stay that way).
- New serialized fields only at the END of a MonoBehaviour/ScriptableObject class. (No new serialized fields are planned.)
- `git add` every new `.cs` AND its `.meta` (open Unity once so metas generate before committing).
- No `FindObjectOfType`/`Camera.main` in per-frame paths.

---

### Task 1: Pin the Input System package

**Files:**
- Modify: `Packages/manifest.json`

- [ ] **Step 1.1: Add the dependency**

In `Packages/manifest.json`, add to `"dependencies"` (alphabetical position):

```json
    "com.unity.inputsystem": "1.14.0",
```

- [ ] **Step 1.2: Verify Unity resolves it and still compiles**

Unity (already open) auto-resolves. Run `mcp__coplay-mcp__check_compile_errors`.
Expected: no errors. Also confirm `ProjectSettings/ProjectSettings.asset` still has `activeInputHandler: 2` (do NOT change it — legacy code depends on "Both").

- [ ] **Step 1.3: Compile sanity for the new API**

Create a throwaway check: add `using UnityEngine.InputSystem;` at the top of `Assets/3 - Scripts/Tutorial/TutorialGate.cs` (it stays — Task 2 needs it). `check_compile_errors` → expected: none.

- [ ] **Step 1.4: Commit**

```bash
git add Packages/manifest.json Packages/packages-lock.json "Assets/3 - Scripts/Tutorial/TutorialGate.cs"
git commit -m "feat(input): pin com.unity.inputsystem dependency"
```

---

### Task 2: TutorialGate — swap pad internals to Input System (API unchanged)

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/TutorialGate.cs` (the controller-support region, roughly lines 45–584; the tutorial-gating block at 1–43 and `PromptGlyphs` are untouched here)

The public surface that MUST remain source-compatible (consumers depend on all of it):
`ControllerEnabled`, `StickLookSensitivity`, `ShipStickLookSensitivity`, `StickDeadzone`, `InvertLookY`, `TriggerThreshold`, `UISelectionActive()`, `WasUIFocusedThisFrameStart()`, `InputSource`, `LastSource`, `ControllerConnected`, `ControllerType`, `DetectedController`, `PadButton`, `PadHeld/PadPressed/PadReleased` (both overloads), `RightStickX/Y()`, `LTValue/RTValue()`, `DPadDirectionPressed(int)`, `HotbarSlotPressed`, `HotbarCycleStep`, `MoveAxisHorizontal/Vertical`, `LookDelta`, `HeadlightStep`, and every composite helper (`JumpHeld`…`MapTogglePressed`).

- [ ] **Step 2.1: Replace the hardware-mapping block**

Delete lines 94–239 of the current file (`ControllerType` detection comment block through `RefreshControllerType()`, including `XboxButton`, `Ds4Button`, `ActiveJoystickIndex`, `Pad(PadButton)`, `_joystickTypes`, `_joystickConnected`, `LooksLikeDs4`) and lines 241–269 (`ApplyInputModuleBindings` + its cached fields). Also delete `IsAnyJoystickButtonDown()` (578–583) and `MouseDeltaIsZero()` (574–576), and the `_prevLT/_prevRT/_prevDPadX/_prevDPadY` fields (271–272). Replace with:

```csharp
    // ── Controller hardware (Unity Input System) ──────────────────────────
    // The Input System normalizes Xbox / DualShock 4 / DualSense into one
    // Gamepad layout (buttonSouth is A on Xbox and Cross on PlayStation), so
    // all the per-brand button-index math the legacy version needed is gone.
    // "Active pad" = Gamepad.current — the pad that most recently sent input,
    // which reproduces the old press-any-button hot-swap behaviour for free.
    public enum ControllerType { Xbox, DualShock4, DualSense }
    public static ControllerType DetectedController { get; private set; } = ControllerType.Xbox;
    public static bool IsPlayStation => DetectedController != ControllerType.Xbox;

    public enum PadButton { A, B, X, Y, LB, RB, Back, Start, L3, R3 }

    static Gamepad ActivePad => ControllerEnabled ? Gamepad.current : null;

    static UnityEngine.InputSystem.Controls.ButtonControl Resolve(Gamepad g, PadButton b)
    {
        switch (b)
        {
            case PadButton.A:     return g.buttonSouth;
            case PadButton.B:     return g.buttonEast;
            case PadButton.X:     return g.buttonWest;
            case PadButton.Y:     return g.buttonNorth;
            case PadButton.LB:    return g.leftShoulder;
            case PadButton.RB:    return g.rightShoulder;
            case PadButton.Back:  return g.selectButton;
            case PadButton.Start: return g.startButton;
            case PadButton.L3:    return g.leftStickButton;
            case PadButton.R3:    return g.rightStickButton;
        }
        return null;
    }

    public static bool PadHeld(PadButton b)     { var g = ActivePad; return g != null && Resolve(g, b).isPressed; }
    public static bool PadPressed(PadButton b)  { var g = ActivePad; return g != null && Resolve(g, b).wasPressedThisFrame; }
    public static bool PadReleased(PadButton b) { var g = ActivePad; return g != null && Resolve(g, b).wasReleasedThisFrame; }

    // Ability-gated variants for tutorial progression.
    public static bool PadHeld(PadButton b, TutorialAbility a)    => PadHeld(b)    && IsUnlocked(a);
    public static bool PadPressed(PadButton b, TutorialAbility a) => PadPressed(b) && IsUnlocked(a);

    // Stick reads go through ReadValue(), which applies the Input System's
    // default stick deadzone processor — the pause-menu STICK DEADZONE slider
    // drives InputSystem.settings.defaultDeadzoneMin (see InputSettings.
    // PushControllerSettingsToGate), so the stored deadzone finally applies.
    public static float RightStickX() { var g = ActivePad; return g != null ? g.rightStick.ReadValue().x : 0f; }
    public static float RightStickY() { var g = ActivePad; return g != null ? g.rightStick.ReadValue().y : 0f; }

    public static float LTValue() { var g = ActivePad; return g != null ? g.leftTrigger.ReadValue()  : 0f; }
    public static float RTValue() { var g = ActivePad; return g != null ? g.rightTrigger.ReadValue() : 0f; }
    static bool TriggerActive(float v) => v > TriggerThreshold;
    // Triggers are ButtonControls with a 0.5 default press point — matches
    // the old TriggerThreshold edge detection, no prev-frame tracking needed.
    static bool LTEdgePressed() { var g = ActivePad; return g != null && g.leftTrigger.wasPressedThisFrame; }
    static bool RTEdgePressed() { var g = ActivePad; return g != null && g.rightTrigger.wasPressedThisFrame; }

    static void RefreshControllerType(Gamepad g)
    {
        if (g == null || g == _lastTypedPad) return;
        _lastTypedPad = g;
        if (g is UnityEngine.InputSystem.DualShock.DualShockGamepad)
            DetectedController = g.GetType().Name.Contains("DualSense")
                ? ControllerType.DualSense : ControllerType.DualShock4;
        else
            DetectedController = ControllerType.Xbox; // XInput + unknown HID default
    }
    static Gamepad _lastTypedPad;
```

- [ ] **Step 2.2: Rewrite the D-pad / axis / look helpers**

Replace `DPadDirectionPressed` (372–383):

```csharp
    // Edge-detected D-pad, valid for the whole frame (wasPressedThisFrame is
    // stable across Update AND LateUpdate — strictly better than the old
    // prev-frame tracking that needed the LateDriver ordering dance).
    // `dir`: 0=Up, 1=Right, 2=Down, 3=Left.
    public static bool DPadDirectionPressed(int dir)
    {
        var g = ActivePad;
        if (g == null) return false;
        switch (dir)
        {
            case 0: return g.dpad.up.wasPressedThisFrame;
            case 1: return g.dpad.right.wasPressedThisFrame;
            case 2: return g.dpad.down.wasPressedThisFrame;
            case 3: return g.dpad.left.wasPressedThisFrame;
        }
        return false;
    }
```

In `MoveAxisHorizontal` / `MoveAxisVertical`, replace the stick line:

```csharp
        float s = 0f;
        var g = ActivePad;
        if (g != null) s = g.leftStick.ReadValue().x;   // .y in MoveAxisVertical
```

In `LookDelta`, replace the two `Input.GetAxisRaw(AxisRightStick*)` lines:

```csharp
            var g = ActivePad;
            Vector2 stick = g != null ? g.rightStick.ReadValue() : Vector2.zero;
            mx += stick.x * gain;
            my += stick.y * gain * (InvertLookY ? -1f : 1f);
```

`ReloadPressed` (339–341) gets its pad binding, and `HeadlightStep` gets the conflict guard:

```csharp
    // Pistol reload — R key or D-pad down. D-pad down doubles as headlight
    // step-down; PistolController sets SuppressDpadHeadlight while equipped
    // so the two never fire together.
    public static bool ReloadPressed()
    {
        if (Input.GetKeyDown(KeyCode.R)) return true;
        var g = ActivePad;
        return g != null && g.dpad.down.wasPressedThisFrame;
    }

    // Set by PistolController while the pistol is equipped.
    public static bool SuppressDpadHeadlight = false;

    public static float HeadlightStep()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f) return scroll;
        if (SuppressDpadHeadlight) return 0f;
        if (DPadDirectionPressed(0)) return  0.1f;
        if (DPadDirectionPressed(2)) return -0.1f;
        return 0f;
    }
```

Composite helpers that referenced deleted internals compile again as-is (`InteractReleased` line 289 and `MapTogglePressed` line 367 currently call `Input.GetKeyUp(Pad(...))`/`Input.GetKeyDown(Pad(...))` — rewrite those two to `PadReleased(PadButton.X)` / `PadPressed(PadButton.Back)` respectively, keeping their `ControllerEnabled && IsUnlocked(a)` guards implicit via the new helpers plus an explicit `IsUnlocked(a)`).

- [ ] **Step 2.3: Rewrite the driver Tick (source tracking + connection)**

Replace `Tick()` (520–572), `EarlyDriver.Update` body (503–510):

```csharp
    // EarlyDriver.Update:
        void Update() {
            _uiFocusedAtFrameStart = UISelectionActive();
        }

    // LateDriver.LateUpdate stays: () => Tick();

    static void Tick()
    {
        ControllerConnected = Gamepad.all.Count > 0;

        var g = Gamepad.current;
        bool controllerActive = false;
        if (ControllerEnabled && g != null)
        {
            controllerActive =
                AnyPadButtonHeld(g) ||
                g.leftStick.ReadValue().sqrMagnitude  > 0.09f ||
                g.rightStick.ReadValue().sqrMagnitude > 0.09f ||
                g.dpad.ReadValue().sqrMagnitude       > 0.25f ||
                g.leftTrigger.ReadValue()  > 0.3f ||
                g.rightTrigger.ReadValue() > 0.3f;
        }

        if (controllerActive)
        {
            LastSource = InputSource.Controller;
            RefreshControllerType(g);
        }
        else if (KeyboardMouseActive())
        {
            LastSource = InputSource.KeyboardMouse;
        }

        GamepadRumble.Tick();   // added in Task 4 — leave a TODO comment until then
    }

    static bool AnyPadButtonHeld(Gamepad g) =>
        g.buttonSouth.isPressed || g.buttonEast.isPressed ||
        g.buttonWest.isPressed  || g.buttonNorth.isPressed ||
        g.leftShoulder.isPressed || g.rightShoulder.isPressed ||
        g.selectButton.isPressed || g.startButton.isPressed ||
        g.leftStickButton.isPressed || g.rightStickButton.isPressed;

    static bool KeyboardMouseActive()
    {
        // Legacy reads on purpose — KBM stays on the old backend everywhere.
        if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f ||
            Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f ||
            Input.GetMouseButton(0) || Input.GetMouseButton(1)) return true;
        // anyKeyDown includes joystick buttons on the legacy backend, so make
        // sure a pad press doesn't masquerade as keyboard.
        if (Input.anyKeyDown)
        {
            var g = Gamepad.current;
            if (g == null || !AnyPadButtonHeld(g)) return true;
        }
        return false;
    }
```

Until Task 4 exists, keep the `GamepadRumble.Tick()` line commented: `// GamepadRumble.Tick(); // enabled in rumble task`.

- [ ] **Step 2.4: Compile check + play-mode smoke test**

`check_compile_errors` → expected none (if consumers reference something deleted, the error names the missing API — restore that member as a shim over the new internals rather than editing the consumer).
Then `play_game` in the gameplay scene, `get_unity_logs` — expected: no exceptions from `[TutorialGateDriver]`, keyboard movement works (walk with WASD via MCP is not possible — just confirm no errors and that `TutorialGate` didn't spam).

- [ ] **Step 2.5: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/TutorialGate.cs"
git commit -m "feat(input): TutorialGate pad internals on Input System (Xbox/DS4/DualSense native)"
```

---

### Task 3: UI module swap — InputSystemUIInputModule

**Files:**
- Modify: `Assets/3 - Scripts/UI/ControllerUINavigator.cs:81-95` (`EnsureEventSystem`)

- [ ] **Step 3.1: Replace EnsureEventSystem**

Add `using UnityEngine.InputSystem.UI;` at the top, then:

```csharp
    static void EnsureEventSystem()
    {
        var es = EventSystem.current;
        if (es == null)
        {
            // Find any existing in the loaded scenes (active or inactive).
            es = FindObjectOfType<EventSystem>(true);
            if (es != null)
            {
                if (!es.gameObject.activeSelf) es.gameObject.SetActive(true);
                if (!es.enabled) es.enabled = true;
            }
            else
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
        }

        // Swap any legacy module for the Input System one. The new module
        // natively maps Submit to A/Cross per controller brand, replacing the
        // old Submit_DS4 axis-swap hack in TutorialGate.
        var legacy = es.GetComponent<StandaloneInputModule>();
        if (legacy != null) Object.Destroy(legacy);
        var module = es.GetComponent<InputSystemUIInputModule>();
        if (module == null)
        {
            module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();   // built-in DefaultInputActions: navigate/submit/cancel/point/click on all devices
        }
    }
```

Note this now runs the swap even when `EventSystem.current != null` (the old early-return skipped baked EventSystems — that's exactly where the legacy module lives).

- [ ] **Step 3.2: Compile + play-mode UI check**

`check_compile_errors` → none. `play_game`, open the pause menu is not MCP-drivable — instead check `get_unity_logs` for module errors and use `mcp__coplay-mcp__get_game_object_info` on the `EventSystem` object to confirm `InputSystemUIInputModule` is present and `StandaloneInputModule` is gone. Verify mouse clicks still work on the main menu scene if loaded (`open_scene` MainMenu, play, logs clean).

- [ ] **Step 3.3: Commit**

```bash
git add "Assets/3 - Scripts/UI/ControllerUINavigator.cs"
git commit -m "feat(input): swap StandaloneInputModule for InputSystemUIInputModule"
```

---

### Task 4: GamepadRumble core

**Files:**
- Create: `Assets/3 - Scripts/Input/GamepadRumble.cs` (+ let Unity generate the `.meta`, add both)
- Modify: `Assets/3 - Scripts/Tutorial/TutorialGate.cs` (uncomment `GamepadRumble.Tick();`)

- [ ] **Step 4.1: Write GamepadRumble.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Central haptics hub. Two shapes of feedback:
//   Pulse(low, high, seconds)          — one-shot (shot fired, landed, bite)
//   SetChannel(id, low, high)          — continuous until cleared (ship thrust)
// Output each frame = per-motor max across all live pulses and channels,
// sent to Gamepad.current. Gated on Enabled (pause-menu VIBRATION toggle,
// pushed from InputSettings). Hard-zeroed while the game is paused or
// unfocused so the pad never buzzes over a menu.
public static class GamepadRumble
{
    public static bool Enabled = true;

    struct Pulse { public float low, high, endTime; }
    static readonly List<Pulse> _pulses = new List<Pulse>();
    static readonly Dictionary<string, Vector2> _channels = new Dictionary<string, Vector2>();

    static Gamepad _lastPad;
    static float _sentLow = -1f, _sentHigh = -1f;

    public static void Pulse(float low, float high, float seconds)
    {
        if (!Enabled) return;
        _pulses.Add(new Pulse {
            low = Mathf.Clamp01(low), high = Mathf.Clamp01(high),
            endTime = Time.unscaledTime + seconds });
    }

    public static void SetChannel(string id, float low, float high)
    {
        _channels[id] = new Vector2(Mathf.Clamp01(low), Mathf.Clamp01(high));
    }

    public static void ClearChannel(string id) => _channels.Remove(id);

    public static void StopAll()
    {
        _pulses.Clear();
        _channels.Clear();
        Send(0f, 0f, force: true);
    }

    // Called once per frame from TutorialGate's LateDriver.
    public static void Tick()
    {
        var pad = Gamepad.current;
        if (pad != _lastPad)
        {
            // Pad swapped/disconnected — silence the old one if still present.
            _lastPad?.SetMotorSpeeds(0f, 0f);
            _sentLow = _sentHigh = -1f;
            _lastPad = pad;
        }
        if (pad == null) return;

        float low = 0f, high = 0f;
        bool muted = !Enabled || Time.timeScale == 0f || !Application.isFocused;
        if (!muted)
        {
            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= _pulses[i].endTime) { _pulses.RemoveAt(i); continue; }
                low = Mathf.Max(low, _pulses[i].low);
                high = Mathf.Max(high, _pulses[i].high);
            }
            foreach (var kv in _channels)
            {
                low = Mathf.Max(low, kv.Value.x);
                high = Mathf.Max(high, kv.Value.y);
            }
        }
        Send(low, high, force: false);
    }

    static void Send(float low, float high, bool force)
    {
        var pad = Gamepad.current;
        if (pad == null) return;
        // Only touch the HID when the value changes — SetMotorSpeeds every
        // frame at identical values is wasted output traffic.
        if (!force && Mathf.Approximately(low, _sentLow) && Mathf.Approximately(high, _sentHigh)) return;
        pad.SetMotorSpeeds(low, high);
        _sentLow = low; _sentHigh = high;
    }
}
```

(`Application.isFocused` covers alt-tab; pause covers the menu because this game pauses via `Time.timeScale = 0`.)

- [ ] **Step 4.2: Enable the tick**

In `TutorialGate.Tick()`, replace the commented line with `GamepadRumble.Tick();`.

- [ ] **Step 4.3: Compile + commit**

`check_compile_errors` → none. Open Unity once so the `.meta` generates.

```bash
git add "Assets/3 - Scripts/Input/GamepadRumble.cs" "Assets/3 - Scripts/Input/GamepadRumble.cs.meta" "Assets/3 - Scripts/Input.meta" "Assets/3 - Scripts/Tutorial/TutorialGate.cs"
git commit -m "feat(input): GamepadRumble haptics hub (pulses + continuous channels)"
```

---

### Task 5: Rumble wire points

**Files (all Modify):**
- `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` (~line 694–724, FixedUpdate thrust block)
- `Assets/3 - Scripts/Survival/ResourceManager.cs:158-166` (`TakeDamage`)
- `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs:568-571` (landing)
- `Assets/3 - Scripts/Pickups/PistolController.cs:316-321` (`TriggerShot`)
- `Assets/3 - Scripts/Pickups/AxeController.cs:274-298` (both `ApplyHit` overloads)
- `Assets/3 - Scripts/Fishing/Bobber.cs:207-215` (bite)
- `Assets/3 - Scripts/Pickups/PlayerPickup.cs:188-191` (`PickupObject`)

- [ ] **Step 5.1: Ship thrust channel**

In the FixedUpdate thrust block: where thrust force is applied (after line ~724), set the channel; where the piloted/canFly condition fails (the block's else path — add one if it's an early-skip), clear it:

```csharp
            // inside the thrust branch, after rb.AddForce:
            float rumbleMag = Mathf.Clamp01(_smoothedThrusterInput.magnitude);
            GamepadRumble.SetChannel("ship-thrust",
                rumbleMag * 0.20f,
                _isBoostingThisTick ? 0.45f : rumbleMag * 0.08f);
```

```csharp
            // when not piloted / not thrusting this tick:
            GamepadRumble.ClearChannel("ship-thrust");
```

Make sure the clear runs whenever the set doesn't (simplest: compute the branch condition into a bool and set/clear on both sides every FixedUpdate).

- [ ] **Step 5.2: Damage pulse**

In `ResourceManager.TakeDamage`, after `OnHealthDropped?.Invoke(amount);` (line 165):

```csharp
        GamepadRumble.Pulse(Mathf.Clamp01(0.3f + amount * 0.02f), 0.5f, 0.25f);
```

- [ ] **Step 5.3: Landing pulse**

In `PlayerController.HandleInput`, inside `if (justLanded)` (line 571), scale by airtime:

```csharp
        if (justLanded)
        {
            OnLanded?.Invoke();
            if (airborneTime >= minAirborneForLandSound)
                GamepadRumble.Pulse(Mathf.Clamp01(airborneTime * 0.4f), 0.15f, 0.15f);
        }
```

- [ ] **Step 5.4: Pistol shot pulse**

In `PistolController.TriggerShot`, next to the shoot clip (line ~321): `GamepadRumble.Pulse(0.25f, 0.9f, 0.1f);`

- [ ] **Step 5.5: Axe chop pulses**

In BOTH `AxeController.ApplyHit` overloads, next to the hit clip: `GamepadRumble.Pulse(0.6f, 0.35f, 0.15f);`

- [ ] **Step 5.6: Fishing bite pulse**

In `Bobber` at the strike start (line ~207, next to `isStriking = true;`): `GamepadRumble.Pulse(0.8f, 0.8f, 0.4f);`

- [ ] **Step 5.7: Pickup pulse**

In `PlayerPickup.PickupObject` next to the pickup clip: `GamepadRumble.Pulse(0.1f, 0.3f, 0.08f);`

- [ ] **Step 5.8: Compile + commit**

`check_compile_errors` → none.

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs" "Assets/3 - Scripts/Survival/ResourceManager.cs" "Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs" "Assets/3 - Scripts/Pickups/PistolController.cs" "Assets/3 - Scripts/Pickups/AxeController.cs" "Assets/3 - Scripts/Fishing/Bobber.cs" "Assets/3 - Scripts/Pickups/PlayerPickup.cs"
git commit -m "feat(input): rumble wired to ship thrust, damage, landing, pistol, axe, fishing, pickup"
```

---

### Task 6: Settings — complete the CONTROLS tab + push bridge

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs:104` (default flip) and `:605-611` (`PushControllerSettingsToGate`)
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs:499-533` (CONTROLS tab rows)

- [ ] **Step 6.1: Flip the default + extend the push bridge**

Line 104: `const bool defaultControllerEnabled = true;   // auto-detected; pads work out of the box` — note existing players who never touched the toggle have a saved PlayerPrefs value of 0; ALSO bump the pref key so the new default wins once: in `LoadSettings` change the line to
`controllerEnabled = PlayerPrefs.GetInt("controllerEnabled_v2", defaultControllerEnabled ? 1 : 0) != 0;`
and in `SaveSettings` find the matching `PlayerPrefs.SetInt(nameof(controllerEnabled), ...)` and change its key to `"controllerEnabled_v2"` too.

`PushControllerSettingsToGate` becomes:

```csharp
	public void PushControllerSettingsToGate () {
		TutorialGate.ControllerEnabled        = controllerEnabled;
		TutorialGate.StickLookSensitivity     = stickLookSensitivity;
		TutorialGate.ShipStickLookSensitivity = shipStickSensitivity;
		TutorialGate.StickDeadzone            = stickDeadzone;
		TutorialGate.InvertLookY              = invertLookY;
		GamepadRumble.Enabled                 = vibrationEnabled;
		// Deadzone finally applies: the Input System's default stick processor
		// reads this project-wide setting on every stick ReadValue().
		UnityEngine.InputSystem.InputSystem.settings.defaultDeadzoneMin =
			Mathf.Clamp (stickDeadzone, 0.01f, 0.5f);
	}
```

- [ ] **Step 6.2: Add the CONTROLS rows**

In `TabbedPauseMenu.BuildSettingsList()`, restructure the CONTROLS tab rows to (setters call `_input.PushControllerSettingsToGate()` so every write flows through one choke point — replace the existing toggle's manual `TutorialGate.ControllerEnabled = v;` line with the push call too):

```csharp
                rows = new List<RowDef>
                {
                    new HeaderDef { label = "CONTROLLER" },
                    new ToggleDef {
                        label = "CONTROLLER ENABLED",
                        get  = () => _input != null && _input.controllerEnabled,
                        set  = v  => { if (_input == null) return; _input.controllerEnabled = v; _input.PushControllerSettingsToGate(); },
                    },
                    new SliderDef {
                        label = "STICK LOOK SENSITIVITY", min = 0.1f, max = 5f, wholeNumbers = false, format = "{0:F2}",
                        get  = () => _input != null ? _input.stickLookSensitivity : 1f,
                        set  = v  => { if (_input == null) return; _input.stickLookSensitivity = v; _input.PushControllerSettingsToGate(); },
                    },
                    new SliderDef {
                        label = "SHIP STICK SENSITIVITY", min = 0.1f, max = 5f, wholeNumbers = false, format = "{0:F2}",
                        get  = () => _input != null ? _input.shipStickSensitivity : 1f,
                        set  = v  => { if (_input == null) return; _input.shipStickSensitivity = v; _input.PushControllerSettingsToGate(); },
                    },
                    new SliderDef {
                        label = "STICK DEADZONE", min = 0f, max = 0.5f, wholeNumbers = false, format = "{0:F2}",
                        get  = () => _input != null ? _input.stickDeadzone : 0.19f,
                        set  = v  => { if (_input == null) return; _input.stickDeadzone = v; _input.PushControllerSettingsToGate(); },
                    },
                    new ToggleDef {
                        label = "INVERT LOOK Y",
                        get  = () => _input != null && _input.invertLookY,
                        set  = v  => { if (_input == null) return; _input.invertLookY = v; _input.PushControllerSettingsToGate(); },
                    },
                    new ToggleDef {
                        label = "VIBRATION",
                        get  = () => _input != null && _input.vibrationEnabled,
                        set  = v  => { if (_input == null) return; _input.vibrationEnabled = v; _input.PushControllerSettingsToGate(); if (!v) GamepadRumble.StopAll(); },
                    },
                    new HeaderDef { label = "MOUSE" },
                    // ...existing MOUSE SENSITIVITY + MOUSE SMOOTHING rows unchanged...
                    new HeaderDef { label = "AUDIO" },
                    // ...existing MASTER VOLUME row unchanged...
                },
```

- [ ] **Step 6.3: Compile + play-mode check**

`check_compile_errors` → none. `play_game`, `get_unity_logs` clean; optionally `capture_ui_canvas` on the pause menu to eyeball the new rows (menu must be opened by the user or via `execute_script` calling the pause toggle — if not feasible, defer visual check to Task 12).

- [ ] **Step 6.4: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs" "Assets/3 - Scripts/UI/TabbedPauseMenu.cs"
git commit -m "feat(input): CONTROLS tab gets stick sens/deadzone/invert-Y/vibration; deadzone now applies"
```

---

### Task 7: Icon glyphs — sprite asset + PromptGlyphs

**Files:**
- Create: `Assets/InputPrompts/` (atlas PNG + generated `TMP_SpriteAsset`) and `Assets/Editor/BuildInputGlyphAtlas.cs` (one-shot editor script; keep it, it's rerunnable)
- Modify: `Assets/3 - Scripts/Tutorial/TutorialGate.cs` (`PromptGlyphs`, lines 591–632)

- [ ] **Step 7.1: Get the Kenney pack**

Download Kenney "Input Prompts" (CC0) — https://kenney.nl/assets/input-prompts — via `WebFetch` to locate the zip URL, then PowerShell `Invoke-WebRequest` into the scratchpad and `Expand-Archive`. If the site blocks automation, ask the user to download it (one click) and give you the zip path.

- [ ] **Step 7.2: Copy + normalize the needed sprites**

Copy these into `Assets/InputPrompts/src/` with canonical names (Kenney's filenames vary slightly per release — match by obvious pattern, prefer the "default"/color variants, 64px):

| Canonical name | Kenney sprite |
|---|---|
| `xbox_a` `xbox_b` `xbox_x` `xbox_y` | Xbox colored face buttons |
| `xbox_lb` `xbox_rb` `xbox_lt` `xbox_rt` | shoulder/trigger labels |
| `xbox_l3` `xbox_r3` | stick-click icons |
| `xbox_view` `xbox_menu` | view/menu buttons |
| `xbox_dpad_up` `xbox_dpad_down` `xbox_dpad_left` `xbox_dpad_right` | d-pad directions |
| `ps_cross` `ps_circle` `ps_square` `ps_triangle` | PS face buttons |
| `ps_l1` `ps_r1` `ps_l2` `ps_r2` `ps_l3` `ps_r3` | PS shoulders/triggers/sticks |
| `ps_share` `ps_options` | share/options |
| `ps_dpad_up` `ps_dpad_down` `ps_dpad_left` `ps_dpad_right` | d-pad directions |
| `pad_stick_l` `pad_stick_r` | generic stick icons (used by Move/MouseLook) |

- [ ] **Step 7.3: Editor script — atlas + TMP sprite asset**

`Assets/Editor/BuildInputGlyphAtlas.cs` (menu item, rerunnable):

```csharp
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using TMPro;

public static class BuildInputGlyphAtlas
{
    const string SrcDir = "Assets/InputPrompts/src";
    const string AtlasPath = "Assets/InputPrompts/InputGlyphAtlas.png";
    const string SpriteAssetPath = "Assets/InputPrompts/InputGlyphs.asset";

    [MenuItem("Tools/Input/Build Glyph Atlas + TMP Sprite Asset")]
    public static void Build()
    {
        var pngs = Directory.GetFiles(SrcDir, "*.png").OrderBy(p => p).ToArray();
        // Make sources readable for packing.
        foreach (var p in pngs)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            if (!ti.isReadable) { ti.isReadable = true; ti.SaveAndReimport(); }
        }
        var texs = pngs.Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p)).ToArray();
        var names = pngs.Select(Path.GetFileNameWithoutExtension).ToArray();

        var atlas = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
        var rects = atlas.PackTextures(texs, 4, 1024);
        File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(AtlasPath);
        var ati = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        ati.textureType = TextureImporterType.Sprite;
        ati.mipmapEnabled = false;
        ati.SaveAndReimport();
        var atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

        var sa = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(SpriteAssetPath);
        if (sa == null)
        {
            sa = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(sa, SpriteAssetPath);
        }
        sa.spriteSheet = atlasTex;
        sa.spriteGlyphTable.Clear();
        sa.spriteCharacterTable.Clear();

        for (uint i = 0; i < texs.Length; i++)
        {
            var r = rects[i];
            var gr = new GlyphRect(
                (int)(r.x * atlasTex.width), (int)(r.y * atlasTex.height),
                (int)(r.width * atlasTex.width), (int)(r.height * atlasTex.height));
            // Metrics: sit the icon on the baseline, ~80% ascent like a capital.
            var glyph = new TMP_SpriteGlyph {
                index = i, glyphRect = gr, scale = 1f,
                metrics = new GlyphMetrics(gr.width, gr.height, 0, gr.height * 0.8f, gr.width)
            };
            sa.spriteGlyphTable.Add(glyph);
            sa.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, glyph) { name = names[i], scale = 1f });
        }

        if (sa.material == null)
        {
            var mat = new Material(Shader.Find("TextMeshPro/Sprite"));
            mat.name = "InputGlyphs Material";
            AssetDatabase.AddObjectToAsset(mat, sa);
            sa.material = mat;
        }
        sa.material.mainTexture = atlasTex;
        sa.UpdateLookupTables();
        EditorUtility.SetDirty(sa);

        // Register as a global fallback so <sprite name="..."> resolves in
        // every TMP text without per-text assignment.
        var def = TMP_Settings.defaultSpriteAsset;
        if (def != null)
        {
            if (def.fallbackSpriteAssets == null)
                def.fallbackSpriteAssets = new System.Collections.Generic.List<TMP_SpriteAsset>();
            if (!def.fallbackSpriteAssets.Contains(sa)) def.fallbackSpriteAssets.Add(sa);
            EditorUtility.SetDirty(def);
        }
        else
        {
            var so = new SerializedObject(TMP_Settings.instance);
            so.FindProperty("m_defaultSpriteAsset").objectReferenceValue = sa;
            so.ApplyModifiedProperties();
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[InputGlyphs] Built atlas with {texs.Length} sprites.");
    }
}
```

Run it via `mcp__coplay-mcp__execute_script` → `BuildInputGlyphAtlas.Build()`. Expected log: `[InputGlyphs] Built atlas with N sprites.` (TMP API names drift between versions — if a member doesn't exist, check the installed TMP package source and adjust; the tables are public in TMP 3.x.)

- [ ] **Step 7.4: PromptGlyphs — sprite mode**

Rewrite `PromptGlyphs` keeping every property name. Pattern:

```csharp
public static class PromptGlyphs
{
    static bool Pad => TutorialGate.LastSource == TutorialGate.InputSource.Controller;
    static bool PS  => TutorialGate.IsPlayStation;

    // One-time check that the sprite asset actually resolved — if the glyph
    // build failed or the asset is missing, fall back to the old text labels
    // instead of rendering empty squares.
    static bool? _spritesOk;
    static bool SpritesOk
    {
        get
        {
            if (_spritesOk == null)
            {
                var def = TMPro.TMP_Settings.defaultSpriteAsset;
                _spritesOk = def != null &&
                    (TMPro.TMP_SpriteAsset.SearchForSpriteByHashCode(def,
                        TMPro.TMP_TextUtilities.GetSimpleHashCode("xbox_a"), true, out _) != null);
            }
            return _spritesOk.Value;
        }
    }

    static string Pick(string kbm, string xboxText, string psText, string xboxSprite, string psSprite)
    {
        if (!Pad) return kbm;
        if (!SpritesOk) return PS ? psText : xboxText;
        return $"<sprite name=\"{(PS ? psSprite : xboxSprite)}\">";
    }

    public static string Jump          => Pick("<b>Space</b>", "<b>A</b>",     "<b>Cross</b>",   "xbox_a",  "ps_cross");
    public static string Interact      => Pick("<b>F</b>",     "<b>X</b>",     "<b>Square</b>",  "xbox_x",  "ps_square");
    public static string InteractPlain => Interact.Replace("<b>", "").Replace("</b>", "");
    public static string Sprint        => Pick("<b>Shift</b>", "<b>L3</b>",    "<b>L3</b>",      "xbox_l3", "ps_l3");
    public static string DownThrust    => Pick("<b>Ctrl</b>",  "<b>R3</b>",    "<b>R3</b>",      "xbox_r3", "ps_r3");
    public static string Flashlight    => Pick("<b>E</b>",     "<b>Y</b>",     "<b>Triangle</b>","xbox_y",  "ps_triangle");
    public static string Drop          => Pick("<b>G</b>",     "<b>B</b>",     "<b>Circle</b>",  "xbox_b",  "ps_circle");
    public static string Map           => Pick("<b>M</b>",     "<b>View</b>",  "<b>Share</b>",   "xbox_view", "ps_share");
    public static string Pause         => Pick("<b>Esc</b>",   "<b>Start</b>", "<b>Options</b>", "xbox_menu", "ps_options");
    public static string Cancel        => Pick("<b>Esc</b>",   "<b>B</b>",     "<b>Circle</b>",  "xbox_b",  "ps_circle");
    public static string PrimaryFire   => Pick("<b>LMB</b>",   "<b>RT</b>",    "<b>R2</b>",      "xbox_rt", "ps_r2");
    public static string SecondaryFire => Pick("<b>RMB</b>",   "<b>LT</b>",    "<b>L2</b>",      "xbox_lt", "ps_l2");
    public static string RollLeft      => Pick("<b>Q</b>",     "<b>LB</b>",    "<b>L1</b>",      "xbox_lb", "ps_l1");
    public static string RollRight     => Pick("<b>E</b>",     "<b>RB</b>",    "<b>R1</b>",      "xbox_rb", "ps_r1");
    public static string BuildMenu     => Pick("<b>N</b>",     "<b>LB</b>",    "<b>L1</b>",      "xbox_lb", "ps_l1");
    public static string Fishingdex    => Pick("<b>B</b>",     "<b>RB</b>",    "<b>R1</b>",      "xbox_rb", "ps_r1");
    public static string AdvanceTip    => Pick("<b>TAB</b>",   "<b>LT</b>",    "<b>L2</b>",      "xbox_lt", "ps_l2");
    public static string Reload        => Pick("<b>R</b>",     "<b>D-pad ↓</b>", "<b>D-pad ↓</b>", "xbox_dpad_down", "ps_dpad_down");
    public static string Move          => Pick("<b>WASD</b>",  "<b>left stick</b>",  "<b>left stick</b>",  "pad_stick_l", "pad_stick_l");
    public static string MouseLook     => Pick("<b>mouse</b>", "<b>right stick</b>", "<b>right stick</b>", "pad_stick_r", "pad_stick_r");
    // Compound phrases keep text + inline glyph:
    public static string PrimaryClick    => Pad ? $"pull {PrimaryFire}"  : "<b>left click</b>";
    public static string PrimaryClickCap => Pad ? $"Pull {PrimaryFire}"  : "<b>Left click</b>";
    public static string DirThrustHold   => Pad ? $"push {Move} + {Sprint}" : "<b>WASD + Shift</b>";
    public static string PlacementRotate => Pad ? $"{SecondaryFire} + {MouseLook}" : "<b>RMB</b>";
}
```

(If `SearchForSpriteByHashCode`/`GetSimpleHashCode` signatures differ in the installed TMP, use any equivalent existence check — e.g. cache `TMP_Settings.defaultSpriteAsset.fallbackSpriteAssets` scan for a character named `xbox_a`.)

- [ ] **Step 7.5: Verify in play mode**

`check_compile_errors` → none. Play + `capture_scene_object`/`capture_ui_canvas` a prompt that renders `PromptGlyphs.Interact` while forcing pad mode: temporarily `execute_script` setting a test TMP text to `<sprite name="xbox_a">` to confirm the sprite renders (not an empty box). Revert the test.

- [ ] **Step 7.6: Commit**

```bash
git add Assets/InputPrompts Assets/InputPrompts.meta Assets/Editor/BuildInputGlyphAtlas.cs Assets/Editor/BuildInputGlyphAtlas.cs.meta "Assets/3 - Scripts/Tutorial/TutorialGate.cs" "Assets/TextMesh Pro/Resources/TMP Settings.asset"
git commit -m "feat(input): controller button icon glyphs (Kenney CC0) via TMP sprite asset"
```

---

### Task 8: Sweep A — phone + chat

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (raw reads at 1315, 1363, 1370, 1378, 1385, 1399, 1415, 1426, 1451, 1487-1489)
- Modify: `Assets/3 - Scripts/AI/AIChatScreen.cs` (551, 560, 1361)
- Modify: `Assets/3 - Scripts/Pickups/PistolController.cs` (reload + suppression flag)

- [ ] **Step 8.1: PistolController — reload binding + headlight suppression**

Where it reads `Input.GetKeyDown(KeyCode.R)` (or wherever reload triggers), switch to `TutorialGate.ReloadPressed()`. In its equip path set `TutorialGate.SuppressDpadHeadlight = true;`, in unequip (and `OnDisable`) set it `false`.

- [ ] **Step 8.2: PlayerPhoneUI pad mappings**

Apply these composites (pattern: `Input.GetKeyDown(KeyCode.X)` → `Input.GetKeyDown(KeyCode.X) || TutorialGate.PadPressed(TutorialGate.PadButton.Y)`):

| Line | Current | Add pad |
|---|---|---|
| 1315 | `R` rescan | `PadPressed(Y)` |
| 1363 | `F`/`C` enter capture | `PadPressed(A)` |
| 1370 | `X` cancel capture | `PadPressed(B)` |
| 1378 | RMB (capture alt) | `TutorialGate.SecondaryFirePressed()` (replaces the raw read — it already ORs RMB) |
| 1385 | LMB shutter | `TutorialGate.FirePressed()` (already ORs LMB + RT) |
| 1399/1415 | `Escape` close | `PadPressed(B)` |
| 1426 | `C` open camera | `PadPressed(Y)` |
| 1451 | `X` | `PadPressed(B)` |
| 1487-89 | movement-suppression `GetKey(W/A/S/D/Space/Ctrl)` | also check `Mathf.Abs(TutorialGate.MoveAxisHorizontal(TutorialAbility.Move)) > 0.2f \|\| Mathf.Abs(TutorialGate.MoveAxisVertical(TutorialAbility.Move)) > 0.2f \|\| TutorialGate.JumpHeld(TutorialAbility.Jump) \|\| TutorialGate.DownThrustHeld(TutorialAbility.DownThrust)` |

Guard: where a B-press closes AND gameplay Cancel could double-fire, mirror the existing `s_consumedFFrame`-style pattern already used by StorageUI if the phone has one; otherwise rely on `WasUIFocusedThisFrameStart`.

- [ ] **Step 8.3: AIChatScreen**

- 551: `Escape` close → `|| TutorialGate.PadPressed(TutorialGate.PadButton.B)` (still gated on `!IsTypingActive`).
- 560: Enter-to-send stays keyboard (typing implies keyboard) — no change.
- 1361: scroll inertia → add right-stick: `float scrollInput = Input.mouseScrollDelta.y + (-TutorialGate.RightStickY()) * 0.5f;` (adjust sign so stick-up scrolls up; verify in play).

- [ ] **Step 8.4: Compile + commit**

`check_compile_errors` → none.

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs" "Assets/3 - Scripts/AI/AIChatScreen.cs" "Assets/3 - Scripts/Pickups/PistolController.cs"
git commit -m "feat(input): pad support for phone, chat screen, pistol reload"
```

---

### Task 9: Sweep B — panels

**Files (all Modify):**
- `Assets/3 - Scripts/UI/StorageUI.cs` (174, 200)
- `Assets/3 - Scripts/UI/FishStagingUI.cs` (199, 204, 233)
- `Assets/3 - Scripts/World/Newspaper/NewspaperReaderUI.cs` (99-101)
- `Assets/3 - Scripts/World/MonumentLinkPopupUI.cs` (92-93)
- `Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs` (89), `CommunityGalleryUI.cs` (55)
- `Assets/3 - Scripts/UI/Hotbar.cs` (209)

Pattern for all: OR a `TutorialGate.PadPressed(...)` onto the existing key check. Selectable Buttons in these panels are already pad-navigable via ControllerUINavigator + the new UI module — this task only covers the non-Button keyboard shortcuts.

- [ ] **Step 9.1: StorageUI** — 174 close: `|| TutorialGate.PadPressed(TutorialGate.PadButton.B)`; 200 shift-transfer modifier: `|| TutorialGate.PadHeld(TutorialGate.PadButton.LB)`.
- [ ] **Step 9.2: FishStagingUI** — 199 cancel: `|| TutorialGate.PadPressed(TutorialGate.PadButton.B)`; 204 confirm: `|| TutorialGate.PadPressed(TutorialGate.PadButton.Y)` (A is reserved for the focused button); 233 shift modifier: `|| TutorialGate.PadHeld(TutorialGate.PadButton.LB)`. Update the button labels built at 595-614 to append pad glyphs via `PromptGlyphs.Cancel` etc. only if trivial — otherwise skip (labels already say `[ENTER]`/`[ESC]`).
- [ ] **Step 9.3: NewspaperReaderUI** — 99 close: `|| TutorialGate.PadPressed(TutorialGate.PadButton.B)`; 100 prev: `|| TutorialGate.PadPressed(TutorialGate.PadButton.LB)`; 101 next: `|| TutorialGate.PadPressed(TutorialGate.PadButton.RB)`.
- [ ] **Step 9.4: MonumentLinkPopupUI** — 92 No: `|| TutorialGate.PadPressed(TutorialGate.PadButton.B)`. Leave Yes to the focused button (A submits) — do NOT bind A manually (double-fire).
- [ ] **Step 9.5: Photo galleries** — both Escape closes: `|| TutorialGate.PadPressed(TutorialGate.PadButton.B)`.
- [ ] **Step 9.6: Hotbar** — 209 fish-swing gate `!Input.GetMouseButton(0)` → `!TutorialGate.FireHeld()`.
- [ ] **Step 9.7: Compile + commit**

```bash
git add "Assets/3 - Scripts/UI/StorageUI.cs" "Assets/3 - Scripts/UI/FishStagingUI.cs" "Assets/3 - Scripts/World/Newspaper/NewspaperReaderUI.cs" "Assets/3 - Scripts/World/MonumentLinkPopupUI.cs" "Assets/3 - Scripts/UI/Photos/PhotoGalleryUI.cs" "Assets/3 - Scripts/UI/Photos/CommunityGalleryUI.cs" "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(input): pad support across storage/fishing/newspaper/monument/photo panels + hotbar"
```

---

### Task 10: Sweep C — map, building, cutscene, tutorials

**Files (all Modify):**
- `Assets/3 - Scripts/Map/SolarSystemMapController.cs` (141), `MapTutorial.cs` (235, 262-267)
- `Assets/3 - Scripts/Building/GhostPlacement.cs` (107, 115, 161)
- `Assets/3 - Scripts/Cutscenes/DeathCutsceneController.cs` (180)
- `Assets/3 - Scripts/Tutorial/BonusTutorial.cs` (869-870, 928-933, 949), `TutorialStep.cs` (34-39)

(MapCameraRig, vendors, NoteReadUI, BuildMenuUI toggle, and most `_LegacySteps` already have pad fallbacks — verify while in each file, extend only if a gap is obvious.)

- [ ] **Step 10.1: SolarSystemMapController** — 141 Escape close: `|| TutorialGate.CancelPressed()` guarded so it doesn't double-run with the existing Escape (replace the raw Escape read with `TutorialGate.CancelPressed()` — it ORs Escape + B).
- [ ] **Step 10.2: MapTutorial** — 235 `GetMouseButton(1)` (look-drag step): also accept `Mathf.Abs(TutorialGate.RightStickX()) > 0.3f || Mathf.Abs(TutorialGate.RightStickY()) > 0.3f`; 262-267 WASD/Space/Ctrl progress flags: OR the equivalent `TutorialGate.MoveAxis*/JumpHeld/DownThrustHeld` reads.
- [ ] **Step 10.3: GhostPlacement** — 107 `G` cancel: `|| TutorialGate.PadPressed(TutorialGate.PadButton.B)`; 115 scroll cycle: `+ (TutorialGate.DPadDirectionPressed(0) ? 0.1f : 0f) - (TutorialGate.DPadDirectionPressed(2) ? 0.1f : 0f)`; 161 `R` rotate-snap: `|| TutorialGate.PadPressed(TutorialGate.PadButton.Y)`. Verify the LT+right-stick free-rotate path (184/207) already works via `SecondaryFireHeld` — extend to it if it reads raw RMB only.
- [ ] **Step 10.4: DeathCutsceneController** — 180 skip: `|| TutorialGate.PadPressed(TutorialGate.PadButton.A) || TutorialGate.PadPressed(TutorialGate.PadButton.Start)`.
- [ ] **Step 10.5: BonusTutorial + TutorialStep** — replace raw `GetKey(W/A/S/D)` and `GetAxisRaw("Horizontal"/"Vertical")` movement checks with `TutorialGate.MoveAxisHorizontal/Vertical(TutorialAbility.Move)` magnitude checks (`> 0.2f`); R-reload step at 811 → `TutorialGate.ReloadPressed()`.
- [ ] **Step 10.5b: PostGreetingChoicePanel** (`Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs:202`) — the numeric hotkeys stay; choice rows are Buttons, so pad players navigate + A-submit via the UI module already. Verify in-file that each row is a `Selectable`; no code change expected.
- [ ] **Step 10.6: Compile + play-mode log check, then commit**

```bash
git add "Assets/3 - Scripts/Map/SolarSystemMapController.cs" "Assets/3 - Scripts/Map/MapTutorial.cs" "Assets/3 - Scripts/Building/GhostPlacement.cs" "Assets/3 - Scripts/Cutscenes/DeathCutsceneController.cs" "Assets/3 - Scripts/Tutorial/BonusTutorial.cs" "Assets/3 - Scripts/Tutorial/TutorialStep.cs"
git commit -m "feat(input): pad support for map, building placement, death cutscene, tutorials"
```

---

### Task 11: Docs + audit update

**Files:**
- Modify: `docs/CURRENT_STATE_AUDIT.md` (add an Input & Controller Support section; fix the stale pause-menu tab list in §20)

- [ ] **Step 11.1:** Add a new numbered section documenting: TutorialGate as the input facade (Input System pad reads + legacy KBM reads), GamepadRumble, ControllerUINavigator + InputSystemUIInputModule, PromptGlyphs sprite glyphs, InputSettings controller fields + push bridge, supported controllers, and the fixed binding table. Correct §20's tab list to CONTROLS / CAMERA / GRAPHICS.
- [ ] **Step 11.2: Commit**

```bash
git add docs/CURRENT_STATE_AUDIT.md
git commit -m "docs: audit section for the revamped controller input system"
```

---

### Task 12: End-to-end verification (user in the loop)

- [ ] **Step 12.1: Editor pad test (user).** Ask the user to plug in each pad they own (Xbox / DS4 / DualSense) and run this checklist in the Editor, in order:
  1. Main menu: navigate with stick/D-pad, focus ring visible, A/Cross activates, B/Circle cancels.
  2. On-foot: move, look, jump, sprint (L3), flashlight (Y/Triangle), interact (X/Square), drop (B/Circle), hotbar D-pad cycle.
  3. Prompts show icon glyphs and swap to keyboard text when the mouse moves.
  4. Ship: fly, boost, rolls (LB/RB), thrust rumble scales with throttle.
  5. Rumble: pistol shot, axe chop, take damage, land from a jump, fishing bite. VIBRATION toggle kills all of it.
  6. Pause menu CONTROLS tab: all new sliders/toggles work live (deadzone slider visibly changes stick response).
  7. Phone, storage, map, build menu, newspaper, photo gallery all navigable/closable on pad.
  8. Hot-swap: press a button on the other pad mid-game → bindings and glyphs follow.
- [ ] **Step 12.2: Fix everything the user reports**, committing per fix.
- [ ] **Step 12.3: Build test (user).** Make a Windows build, verify from MainMenu boot: pad works immediately, glyphs correct, rumble works, no regression in KBM. (Input System's `Windows.Gaming.Input` pathway differs from the Editor — this is the gate that has bitten this project before.)
- [ ] **Step 12.4:** Invoke superpowers:finishing-a-development-branch to merge/PR `feat/controller-revamp` (remote: `soundofspace`, branch target `main`).
