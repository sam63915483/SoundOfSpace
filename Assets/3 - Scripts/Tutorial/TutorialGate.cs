using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum TutorialAbility
{
    ExitPilot, MouseLook, InteractHatch, Move, Jump, Boost,
    DirectionalThrust, DownThrust, Flashlight, Map, Pickup,
    InteractSunlight, TalkToNPC, EnterPilot,
    ShipMouseLook, ShipUpThrust, ShipMove, ShipDownThrust, ShipRoll,
    // Phase 2 — fishing
    Cast,        // gates LMB cast/reel on the equipped fishing rod
    Fishingdex,  // gates the B / RB key that opens the Fishingdex
    // Phase 6 — axe + building
    ChopAxe,     // gates LMB swing on the equipped axe
    BuildMenu,   // gates the N / LB key that opens the build menu
}

public static class TutorialGate
{
    static readonly HashSet<TutorialAbility> _unlocked = new HashSet<TutorialAbility>();
    static bool _enabled;

    public static bool IsUnlocked(TutorialAbility a) => !_enabled || _unlocked.Contains(a);
    public static void Unlock(TutorialAbility a) { _unlocked.Add(a); }
    public static void LockAll() { _unlocked.Clear(); _enabled = true; }
    public static void UnlockAll() { _enabled = false; _unlocked.Clear(); }

    public static bool GetKey(KeyCode k, TutorialAbility a) => IsUnlocked(a) && Input.GetKey(k);
    public static bool GetKeyDown(KeyCode k, TutorialAbility a) => IsUnlocked(a) && Input.GetKeyDown(k);
    public static bool GetKeyUp(KeyCode k, TutorialAbility a) => IsUnlocked(a) && Input.GetKeyUp(k);
    public static float GetAxisRaw(string axis, TutorialAbility a) => IsUnlocked(a) ? Input.GetAxisRaw(axis) : 0f;
    public static float GetAxis(string axis, TutorialAbility a) => IsUnlocked(a) ? Input.GetAxis(axis) : 0f;

    // ───── Save/Load ─────
    public static bool IsGateEnabled => _enabled;
    public static IEnumerable<TutorialAbility> GetUnlocked() => _unlocked;
    public static void ApplyState(bool enabled, System.Collections.Generic.IList<TutorialAbility> unlocked)
    {
        _enabled = enabled;
        _unlocked.Clear();
        if (unlocked != null) foreach (var a in unlocked) _unlocked.Add(a);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONTROLLER SUPPORT (Xbox One controller via legacy InputManager)
    //
    //  Settings here (ControllerEnabled, StickLookSensitivity, InvertLookY,
    //  StickDeadzone) are populated by InputSettings.Begin() from PlayerPrefs
    //  and live-updated by the pause-menu sliders.
    //
    //  All composite helpers gate on the same TutorialAbility enum value as
    //  their keyboard counterparts, so tutorial locks behave identically
    //  whether the player is on keyboard or controller.
    // ═══════════════════════════════════════════════════════════════════════

    public static bool  ControllerEnabled        = true;
    public static float StickLookSensitivity     = 1f;
    public static float ShipStickLookSensitivity = 1f;
    public static float StickDeadzone            = 0.19f;
    public static bool  InvertLookY              = false;
    public  const float TriggerThreshold         = 0.5f;

    // True if a UI Selectable is currently focused (and interactable + active).
    // Gameplay scripts use this to suppress controller / D-pad / left-stick reads
    // while a menu is open, so left-stick navigates the menu instead of also
    // walking the player and the A button activates the button instead of also
    // queueing a jump.
    public static bool UISelectionActive()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return false;
        var go = es.currentSelectedGameObject;
        if (go == null || !go.activeInHierarchy) return false;
        var sel = go.GetComponent<UnityEngine.UI.Selectable>();
        return sel != null && sel.IsInteractable();
    }

    // Snapshot of UISelectionActive() captured at the very start of each frame,
    // BEFORE Unity's EventSystem fires Button.onClick handlers. The close-button
    // case is the reason this exists: the player presses A on a focused close
    // button → EventSystem.Update() runs, fires onClick → panel deactivates
    // → currentSelectedGameObject is no longer active → UISelectionActive()
    // returns false → PlayerController.HandleInput() runs later in the same
    // frame and reads the A press as a jump. Checking this snapshot keeps
    // gameplay code from seeing inputs that the UI just consumed.
    static bool _uiFocusedAtFrameStart;
    public static bool WasUIFocusedThisFrameStart() => _uiFocusedAtFrameStart;

    public enum InputSource { KeyboardMouse, Controller }
    public static InputSource LastSource { get; private set; } = InputSource.KeyboardMouse;
    public static bool ControllerConnected { get; private set; }

    // ── Controller hardware mapping (Xbox vs DualShock 4) ─────────────────
    // Xbox/XInput and DS4/DirectInput report different button indices and
    // axis numbers on Windows. Detection runs from Tick(); button helpers
    // route through Pad(PadButton) so call sites can be hardware-agnostic.
    public enum ControllerType { Xbox, DualShock4 }
    public static ControllerType DetectedController { get; private set; } = ControllerType.Xbox;

    public enum PadButton { A, B, X, Y, LB, RB, Back, Start, L3, R3 }

    static KeyCode XboxButton(PadButton b) {
        switch (b) {
            case PadButton.A:     return KeyCode.JoystickButton0;
            case PadButton.B:     return KeyCode.JoystickButton1;
            case PadButton.X:     return KeyCode.JoystickButton2;
            case PadButton.Y:     return KeyCode.JoystickButton3;
            case PadButton.LB:    return KeyCode.JoystickButton4;
            case PadButton.RB:    return KeyCode.JoystickButton5;
            case PadButton.Back:  return KeyCode.JoystickButton6;
            case PadButton.Start: return KeyCode.JoystickButton7;
            case PadButton.L3:    return KeyCode.JoystickButton8;
            case PadButton.R3:    return KeyCode.JoystickButton9;
        }
        return KeyCode.None;
    }

    // DualShock 4 (wired, Microsoft default driver). Face-button positions
    // are ROTATED relative to Xbox: PS Cross is bottom (=Xbox A), Square is
    // left (=Xbox X). L3/R3 land on JoyButton 10/11 instead of 8/9 because
    // DS4 reports L2/R2 as buttons too. Share/Options replace Back/Start.
    static KeyCode Ds4Button(PadButton b) {
        switch (b) {
            case PadButton.A:     return KeyCode.JoystickButton1; // Cross  (X)
            case PadButton.B:     return KeyCode.JoystickButton2; // Circle (O)
            case PadButton.X:     return KeyCode.JoystickButton0; // Square
            case PadButton.Y:     return KeyCode.JoystickButton3; // Triangle
            case PadButton.LB:    return KeyCode.JoystickButton4; // L1
            case PadButton.RB:    return KeyCode.JoystickButton5; // R1
            case PadButton.Back:  return KeyCode.JoystickButton8; // Share
            case PadButton.Start: return KeyCode.JoystickButton9; // Options
            case PadButton.L3:    return KeyCode.JoystickButton10;
            case PadButton.R3:    return KeyCode.JoystickButton11;
        }
        return KeyCode.None;
    }

    // The joystick (1-based, matches Unity's "Joystick N" keycode blocks) that
    // most recently fired a button. Pad() reads route to ONLY this joystick so
    // that with both DS4 + Xbox connected, pressing Xbox A doesn't accidentally
    // fire the DS4-mapped action for button 0 (Square = Interact).
    public static int ActiveJoystickIndex { get; private set; } = 1;

    // Resolves a logical PadButton into a JOYSTICK-SCOPED KeyCode (e.g.
    // Joystick2Button0) for the currently active joystick + active type.
    // Layout: KeyCode block per joystick is 20 entries; block N starts at
    // JoystickButton0 + 20*N (so Joystick1Button0 = JoystickButton0 + 20).
    public static KeyCode Pad(PadButton b) {
        KeyCode anyKey = DetectedController == ControllerType.DualShock4 ? Ds4Button(b) : XboxButton(b);
        int btnNum = (int)anyKey - (int)KeyCode.JoystickButton0;
        int joy = ActiveJoystickIndex;
        if (joy < 1 || joy > 16) return anyKey;
        return (KeyCode)((int)KeyCode.JoystickButton0 + 20 * joy + btnNum);
    }

    public static bool PadHeld(PadButton b)    => ControllerEnabled && Input.GetKey    (Pad(b));
    public static bool PadPressed(PadButton b) => ControllerEnabled && Input.GetKeyDown(Pad(b));
    public static bool PadReleased(PadButton b) => ControllerEnabled && Input.GetKeyUp(Pad(b));

    // Ability-gated variants for tutorial progression.
    public static bool PadHeld(PadButton b, TutorialAbility a)    => PadHeld(b)    && IsUnlocked(a);
    public static bool PadPressed(PadButton b, TutorialAbility a) => PadPressed(b) && IsUnlocked(a);

    // Axis name resolution — same logical axis maps to different physical
    // axis numbers on DS4. The "DS4_*" axes are added in InputManager.asset.
    static string AxisRightStickX => DetectedController == ControllerType.DualShock4 ? "DS4_RightStickX" : "RightStickX";
    static string AxisRightStickY => DetectedController == ControllerType.DualShock4 ? "DS4_RightStickY" : "RightStickY";
    static string AxisLeftTrigger => DetectedController == ControllerType.DualShock4 ? "DS4_LeftTrigger" : "LeftTrigger";
    static string AxisRightTrigger => DetectedController == ControllerType.DualShock4 ? "DS4_RightTrigger" : "RightTrigger";
    static string AxisDPadX        => DetectedController == ControllerType.DualShock4 ? "DS4_DPadX" : "DPadX";
    static string AxisDPadY        => DetectedController == ControllerType.DualShock4 ? "DS4_DPadY" : "DPadY";

    // Public axis read helpers for non-TutorialGate call sites.
    public static float RightStickX() => ControllerEnabled ? Input.GetAxisRaw(AxisRightStickX) : 0f;
    public static float RightStickY() => ControllerEnabled ? Input.GetAxisRaw(AxisRightStickY) : 0f;

    // Per-joystick state, indexed 0-based (joystick #1 = index 0, matches
    // Input.GetJoystickNames() ordering).
    static readonly ControllerType[] _joystickTypes = new ControllerType[16];
    static readonly bool[] _joystickConnected = new bool[16];

    static bool LooksLikeDs4(string name) {
        if (string.IsNullOrEmpty(name)) return false;
        string l = name.ToLowerInvariant();
        // Sony-style names cover the common cases on Windows. The raw
        // OS-reported strings include "Wireless Controller" (DS4 BT and
        // wired both report this) plus a few vendor/product variants.
        return l.Contains("wireless controller")
            || l.Contains("dualshock")
            || l.Contains("ds4")
            || l.Contains("ps4")
            || l.Contains("playstation")
            || l.Contains("sony");
    }

    static void RefreshControllerType() {
        var names = Input.GetJoystickNames();

        // Update per-joystick connection + type from Unity's joystick names.
        for (int i = 0; i < _joystickTypes.Length; i++) {
            bool connected = i < names.Length && !string.IsNullOrEmpty(names[i]);
            _joystickConnected[i] = connected;
            if (connected)
                _joystickTypes[i] = LooksLikeDs4(names[i]) ? ControllerType.DualShock4 : ControllerType.Xbox;
        }

        // Switch the "active" joystick to whichever one fired a button this
        // frame. With multiple controllers connected, this lets the player
        // hot-swap simply by picking up another pad and pressing anything —
        // no menu toggle needed. First joystick to fire wins per frame.
        for (int joy = 1; joy <= 16; joy++) {
            int idx = joy - 1;
            if (!_joystickConnected[idx]) continue;
            int blockBase = (int)KeyCode.JoystickButton0 + 20 * joy;
            bool any = false;
            for (int btn = 0; btn < 20; btn++) {
                if (Input.GetKeyDown((KeyCode)(blockBase + btn))) { any = true; break; }
            }
            if (any) { ActiveJoystickIndex = joy; break; }
        }

        // If the active joystick was disconnected, fall back to the first
        // still-connected joystick. Keeps _activeJoystickIndex valid so
        // Pad() always returns a sensible KeyCode.
        int activeIdx = ActiveJoystickIndex - 1;
        if (activeIdx < 0 || activeIdx >= _joystickConnected.Length || !_joystickConnected[activeIdx]) {
            for (int i = 0; i < _joystickConnected.Length; i++) {
                if (_joystickConnected[i]) { ActiveJoystickIndex = i + 1; break; }
            }
            activeIdx = ActiveJoystickIndex - 1;
        }

        DetectedController = (activeIdx >= 0 && activeIdx < _joystickTypes.Length && _joystickConnected[activeIdx])
            ? _joystickTypes[activeIdx]
            : ControllerType.Xbox;

        ApplyInputModuleBindings();
    }

    // Switches the StandaloneInputModule's Submit / Cancel axes so the UI's
    // confirm/cancel buttons match the player's controller. Required because
    // Unity's StandaloneInputModule reads named axes from InputManager.asset,
    // and those axes hard-bind to physical button numbers (Xbox A vs DS4 X
    // are NOT the same button index — Xbox A = button 0, DS4 X = button 1).
    // Without this swap, DS4 users hit Square and the menu submits — wrong.
    //
    // We cache BOTH the module instance and the applied type so a scene
    // transition (which spawns a new EventSystem with a fresh module that
    // defaults submitButton back to "Submit") triggers a re-apply.
    static UnityEngine.EventSystems.StandaloneInputModule _lastAppliedModule;
    static ControllerType _lastAppliedModuleType = (ControllerType)(-1);
    static void ApplyInputModuleBindings() {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return;
        var module = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        if (module == null) return;
        // Skip only if the same module instance still has the same type bindings.
        if (module == _lastAppliedModule && _lastAppliedModuleType == DetectedController) return;
        if (DetectedController == ControllerType.DualShock4) {
            module.submitButton = "Submit_DS4";
            module.cancelButton = "Cancel_DS4";
        } else {
            module.submitButton = "Submit";
            module.cancelButton = "Cancel";
        }
        _lastAppliedModule = module;
        _lastAppliedModuleType = DetectedController;
    }

    static float _prevLT, _prevRT;
    static float _prevDPadX, _prevDPadY;

    // ── Composite "is held" / "is pressed this frame" helpers ──────────────

    public static bool JumpHeld(TutorialAbility a) =>
        GetKey(KeyCode.Space, a) || PadHeld(PadButton.A, a);

    public static bool JumpPressed(TutorialAbility a) =>
        GetKeyDown(KeyCode.Space, a) || PadPressed(PadButton.A, a);

    public static bool InteractPressed(TutorialAbility a) =>
        GetKeyDown(KeyCode.F, a) || PadPressed(PadButton.X, a);

    public static bool InteractHeld(TutorialAbility a) =>
        GetKey(KeyCode.F, a) || PadHeld(PadButton.X, a);

    public static bool InteractReleased(TutorialAbility a) =>
        GetKeyUp(KeyCode.F, a) || (ControllerEnabled && IsUnlocked(a) && Input.GetKeyUp(Pad(PadButton.X)));

    public static bool DropPressed(TutorialAbility a) =>
        GetKeyDown(KeyCode.G, a) || PadPressed(PadButton.B, a);

    // "Primary action" = mouse LMB or controller A. For dialogue advance,
    // place-building confirm, etc. — UI-style submit, not item-use.
    public static bool PrimaryActionPressed() =>
        Input.GetMouseButtonDown(0) || PadPressed(PadButton.A);

    public static bool SprintHeld(TutorialAbility a) =>
        GetKey(KeyCode.LeftShift, a) || PadHeld(PadButton.L3, a);

    public static bool DownThrustHeld(TutorialAbility a) =>
        GetKey(KeyCode.LeftControl, a) || PadHeld(PadButton.R3, a);

    public static bool DownThrustPressed(TutorialAbility a) =>
        GetKeyDown(KeyCode.LeftControl, a) || PadPressed(PadButton.R3, a);

    // Tutorial advance — Tab keyboard, LT-trigger pull on controller. Was on
    // R3 originally, but R3 became the down-thrust binding so tutorial advance
    // moved to LT (which was the old down-thrust binding, now freed up).
    public static bool TutorialAdvancePressed() =>
        Input.GetKeyDown(KeyCode.Tab) ||
        (ControllerEnabled && LTEdgePressed());

    public static bool DirectionalThrustHeld(TutorialAbility a) =>
        // Same input as Sprint (L-stick click) — context decides which fires:
        // PlayerController gates Sprint behind isGrounded and Directional
        // Thrust behind !isGrounded, so L-stick click sprints on the ground
        // and thrusts in the direction of stick input while airborne.
        // Mirrors keyboard: LeftShift on ground = sprint, LeftShift in air = thrust.
        GetKey(KeyCode.LeftShift, a) || PadHeld(PadButton.L3, a);

    public static bool FlashlightPressed(TutorialAbility a) =>
        GetKeyDown(KeyCode.E, a) || PadPressed(PadButton.Y, a);

    public static bool RollLeftHeld(TutorialAbility a) =>
        GetKey(KeyCode.Q, a) || PadHeld(PadButton.LB, a);

    public static bool RollRightHeld(TutorialAbility a) =>
        GetKey(KeyCode.E, a) || PadHeld(PadButton.RB, a);

    // Mouse LMB and RT-pull, edge-detected. For chop / cast / drink / etc.
    public static bool FirePressed() =>
        Input.GetMouseButtonDown(0) || RTEdgePressed();

    public static bool FireHeld() =>
        Input.GetMouseButton(0) || TriggerActive(RTValue());

    // Pistol reload (R key). No controller binding yet.
    public static bool ReloadPressed() =>
        Input.GetKeyDown(KeyCode.R);

    // Secondary fire: RMB or LT (controller). Used for water-bottle fill,
    // build-menu ghost rotate, etc. LT also drives tutorial advance via
    // TutorialAdvancePressed; non-tutorial callers gate their effect on
    // isPiloted / !isGrounded so they don't fire while the player is on
    // foot holding an item. Net behaviour: on foot with item equipped,
    // LT = RMB.
    public static bool SecondaryFireHeld() =>
        Input.GetMouseButton(1) || (ControllerEnabled && LTValue() > TriggerThreshold);

    public static bool SecondaryFirePressed() =>
        Input.GetMouseButtonDown(1) || (ControllerEnabled && LTEdgePressed());

    // Pause toggle (Esc / Start button). P is reserved for the trailer free-cam
    // toggle (TrailerFreeCam); Escape and Start still pause.
    public static bool PausePressed() =>
        Input.GetKeyDown(KeyCode.Escape) ||
        PadPressed(PadButton.Start);

    public static bool CancelPressed() =>
        Input.GetKeyDown(KeyCode.Escape) || PadPressed(PadButton.B);

    // Map toggle (M / Back-View button)
    public static bool MapTogglePressed(TutorialAbility a) =>
        GetKeyDown(KeyCode.M, a) ||
        (ControllerEnabled && IsUnlocked(a) && Input.GetKeyDown(Pad(PadButton.Back)));

    // ── D-pad slot edge detection (for hotbar / scroll-replacements) ───────
    // Returns true on the FRAME the D-pad enters one of the 4 cardinal
    // directions. `dir`: 0=Up, 1=Right, 2=Down, 3=Left.
    public static bool DPadDirectionPressed(int dir)
    {
        if (!ControllerEnabled) return false;
        switch (dir)
        {
            case 0: return _prevDPadY <=  0.5f && Input.GetAxisRaw(AxisDPadY) >  0.5f;
            case 1: return _prevDPadX <=  0.5f && Input.GetAxisRaw(AxisDPadX) >  0.5f;
            case 2: return _prevDPadY >= -0.5f && Input.GetAxisRaw(AxisDPadY) < -0.5f;
            case 3: return _prevDPadX >= -0.5f && Input.GetAxisRaw(AxisDPadX) < -0.5f;
            default: return false;
        }
    }

    // Hotbar direct-select: number keys 1..N. D-pad and RB no longer
    // map to slots — D-pad left/right cycles via HotbarCycleStep() and
    // D-pad up/down + RB are reserved for future bindings.
    public static int HotbarSlotPressed(int numSlots)
    {
        for (int i = 0; i < numSlots; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i + 1;
        return 0;
    }

    // D-pad left = -1 (previous slot), right = +1 (next slot), else 0.
    // Suppressed while a UI Selectable is focused (now or at frame start)
    // so navigating a menu with D-pad doesn't also flip hotbar slots.
    public static int HotbarCycleStep()
    {
        if (!ControllerEnabled) return 0;
        if (UISelectionActive() || _uiFocusedAtFrameStart) return 0;
        if (DPadDirectionPressed(3)) return -1; // D-pad left
        if (DPadDirectionPressed(1)) return +1; // D-pad right
        return 0;
    }

    // Movement axis helpers — keyboard arrows/WASD + LeftStickX/Y, EXCLUDING
    // D-pad. Used by PlayerController and MapCameraRig instead of "Horizontal"/
    // "Vertical" so D-pad doesn't walk the player or pan the map. The original
    // axes still aggregate D-pad for menu nav via StandaloneInputModule.
    public static float MoveAxisHorizontal(TutorialAbility a)
    {
        if (!IsUnlocked(a)) return 0f;
        float k = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  k -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) k += 1f;
        float s = ControllerEnabled ? Input.GetAxisRaw("LeftStickX") : 0f;
        return Mathf.Clamp(k + s, -1f, 1f);
    }

    public static float MoveAxisVertical(TutorialAbility a)
    {
        if (!IsUnlocked(a)) return 0f;
        float k = 0f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) k -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))   k += 1f;
        float s = ControllerEnabled ? Input.GetAxisRaw("LeftStickY") : 0f;
        return Mathf.Clamp(k + s, -1f, 1f);
    }

    // ── Look-axis composite (mouse delta + right-stick rate-scaled) ───────
    public static Vector2 LookDelta(TutorialAbility a)
    {
        if (!IsUnlocked(a)) return Vector2.zero;

        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");

        if (ControllerEnabled)
        {
            // Right-stick value is steady (-1..1), so scale by deltaTime to
            // get per-frame "movement" comparable to mouse delta. The x60
            // is a perceptual gain factor so default sensitivity 1 feels
            // close to mouse default; player tunes via StickLookSensitivity.
            float dt = Time.unscaledDeltaTime;
            float gain = StickLookSensitivity * dt * 60f;
            mx += Input.GetAxisRaw(AxisRightStickX) * gain;
            my += Input.GetAxisRaw(AxisRightStickY) * gain * (InvertLookY ? -1f : 1f);
        }
        return new Vector2(mx, my);
    }

    // Headlight cycle: mouse scroll value (≈±0.05–0.15 per notch) OR
    // D-pad up/down edge (returns ±0.1 to match scroll-notch magnitude so the
    // caller's existing scroll-sensitivity multiplier feels the same on both).
    public static float HeadlightStep()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f) return scroll;
        if (DPadDirectionPressed(0)) return  0.1f;
        if (DPadDirectionPressed(2)) return -0.1f;
        return 0f;
    }

    // ── Internals ─────────────────────────────────────────────────────────

    public static float LTValue() => ControllerEnabled ? Input.GetAxisRaw(AxisLeftTrigger)  : 0f;
    public static float RTValue() => ControllerEnabled ? Input.GetAxisRaw(AxisRightTrigger) : 0f;
    static bool  TriggerActive(float v) => v > TriggerThreshold;
    static bool  LTEdgePressed() => _prevLT < TriggerThreshold && LTValue() >= TriggerThreshold;
    static bool  RTEdgePressed() => _prevRT < TriggerThreshold && RTValue() >= TriggerThreshold;

    // ── Per-frame driver ──────────────────────────────────────────────────
    // Static class can't host MonoBehaviour callbacks, so we spawn a hidden
    // DontDestroyOnLoad GameObject on first scene load to drive Tick().
    // Tick advances trigger / D-pad edge state and the LastSource hint.

    [UnityEngine.Scripting.Preserve]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureDriver()
    {
        if (_driverGO != null) return;
        _driverGO = new GameObject("[TutorialGateDriver]");
        Object.DontDestroyOnLoad(_driverGO);
        // Two drivers so we can run on opposite ends of the frame:
        //   EarlyDriver.Update  (-9999)  — capture UI focus snapshot before
        //                                  EventSystem fires Button.onClick.
        //   LateDriver.LateUpdate (+9999) — advance D-pad / trigger edge state
        //                                  AFTER every LateUpdate consumer
        //                                  has read it (e.g. the map legend
        //                                  nav in SolarSystemMapController).
        // Without this split, [DefaultExecutionOrder(-9999)] would also make
        // LateUpdate run first, overwriting _prevDPad before legend nav reads it.
        _driverGO.AddComponent<EarlyDriver>();
        _driverGO.AddComponent<LateDriver>();
    }
    static GameObject _driverGO;

    [UnityEngine.Scripting.Preserve]
    [DefaultExecutionOrder(-9999)]
    class EarlyDriver : MonoBehaviour
    {
        void Update() {
            _uiFocusedAtFrameStart = UISelectionActive();
            // Refresh controller type early so StandaloneInputModule reads the
            // correct Submit/Cancel axis on the SAME frame the player presses
            // a button. Without this, the first DS4 X press would still be
            // handled as Xbox A (i.e. miss).
            RefreshControllerType();
        }
    }

    [UnityEngine.Scripting.Preserve]
    [DefaultExecutionOrder(9999)]
    class LateDriver : MonoBehaviour
    {
        void LateUpdate() => Tick();
    }

    static void Tick()
    {
        // Refresh controller-type detection (Xbox vs DS4) every frame so a
        // hot-swap mid-session picks the right button mapping. Cheap — just
        // a string scan over Input.GetJoystickNames().
        RefreshControllerType();

        // Detect controller input by polling the actual axes/buttons rather
        // than Input.GetJoystickNames(). On Windows builds using
        // Windows.Gaming.Input (the new XInput pathway), the legacy joystick-
        // names list comes up empty even when axes and buttons work normally
        // — the names check is unreliable in builds. So if ANY controller axis
        // or button fires, we treat that as proof a controller is present.
        bool controllerActive =
            IsAnyJoystickButtonDown() ||
            Mathf.Abs(Input.GetAxisRaw("LeftStickX"))  > 0.3f ||
            Mathf.Abs(Input.GetAxisRaw("LeftStickY"))  > 0.3f ||
            Mathf.Abs(Input.GetAxisRaw(AxisRightStickX)) > 0.3f ||
            Mathf.Abs(Input.GetAxisRaw(AxisRightStickY)) > 0.3f ||
            Mathf.Abs(Input.GetAxisRaw(AxisDPadX))       > 0.3f ||
            Mathf.Abs(Input.GetAxisRaw(AxisDPadY))       > 0.3f ||
            LTValue() > 0.3f || RTValue() > 0.3f;

        // ControllerConnected stays sticky — once we see ANY controller input,
        // we know one's plugged in. We also fall back to GetJoystickNames so
        // the value is correct even before the player touches anything.
        if (controllerActive) ControllerConnected = true;
        else if (!ControllerConnected)
        {
            var names = Input.GetJoystickNames();
            for (int i = 0; i < names.Length; i++)
                if (!string.IsNullOrEmpty(names[i])) { ControllerConnected = true; break; }
        }

        // Source tracking
        if (Input.anyKeyDown ||
            Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f ||
            Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f)
        {
            if (!IsAnyJoystickButtonDown() && !MouseDeltaIsZero())
                LastSource = InputSource.KeyboardMouse;
        }
        if (ControllerEnabled && controllerActive)
        {
            LastSource = InputSource.Controller;
        }

        // Advance edge-detection trackers.
        _prevLT = LTValue();
        _prevRT = RTValue();
        _prevDPadX = ControllerEnabled ? Input.GetAxisRaw(AxisDPadX) : 0f;
        _prevDPadY = ControllerEnabled ? Input.GetAxisRaw(AxisDPadY) : 0f;
    }

    static bool MouseDeltaIsZero() =>
        Mathf.Abs(Input.GetAxisRaw("Mouse X")) < 0.001f &&
        Mathf.Abs(Input.GetAxisRaw("Mouse Y")) < 0.001f;

    static bool IsAnyJoystickButtonDown()
    {
        for (KeyCode k = KeyCode.JoystickButton0; k <= KeyCode.JoystickButton19; k++)
            if (Input.GetKeyDown(k)) return true;
        return false;
    }
}

// Glyph strings for prompt UIs. Reads TutorialGate.LastSource and
// TutorialGate.DetectedController so labels track BOTH the input mode
// (KBM vs controller) and the controller hardware (Xbox vs DS4) — a DS4
// player should see "Square" not "X" for Interact, since "X" on DS4 is
// the Cross button (mapped to logical A = Jump).
public static class PromptGlyphs
{
    static bool Pad => TutorialGate.LastSource == TutorialGate.InputSource.Controller;
    static bool DS4 => TutorialGate.DetectedController == TutorialGate.ControllerType.DualShock4;

    // Pick the right label for the current input + hardware. `xbox` is also
    // used as the keyboard fallback when only `kbm` and `xbox` are supplied,
    // and as the controller default when `ps` is omitted (i.e. matches Xbox).
    static string Pick(string kbm, string xbox, string ps)
    {
        if (!Pad) return kbm;
        return DS4 ? ps : xbox;
    }

    public static string Jump          => Pick("<b>Space</b>", "<b>A</b>",        "<b>Cross</b>");
    public static string Interact      => Pick("<b>F</b>",     "<b>X</b>",        "<b>Square</b>");
    public static string InteractPlain => Pick("F",            "X",               "Square");
    public static string Sprint        => Pick("<b>Shift</b>", "<b>L3</b>",       "<b>L3</b>");
    public static string DownThrust    => Pick("<b>Ctrl</b>",  "<b>R3</b>",       "<b>R3</b>");
    public static string Flashlight    => Pick("<b>E</b>",     "<b>Y</b>",        "<b>Triangle</b>");
    public static string Drop          => Pick("<b>G</b>",     "<b>B</b>",        "<b>Circle</b>");
    public static string Map           => Pick("<b>M</b>",     "<b>View</b>",     "<b>Share</b>");
    public static string Pause         => Pick("<b>Esc</b>",   "<b>Start</b>",    "<b>Options</b>");
    public static string Cancel        => Pick("<b>Esc</b>",   "<b>B</b>",        "<b>Circle</b>");
    public static string PrimaryFire   => Pick("<b>LMB</b>",   "<b>RT</b>",       "<b>R2</b>");
    public static string PrimaryClick  => Pick("<b>left click</b>",  "pull <b>RT</b>", "pull <b>R2</b>");
    public static string PrimaryClickCap => Pick("<b>Left click</b>","Pull <b>RT</b>", "Pull <b>R2</b>");
    public static string SecondaryFire => Pick("<b>RMB</b>",   "<b>LT</b>",       "<b>L2</b>");
    public static string RollLeft      => Pick("<b>Q</b>",     "<b>LB</b>",       "<b>L1</b>");
    public static string RollRight     => Pick("<b>E</b>",     "<b>RB</b>",       "<b>R1</b>");
    public static string Move          => Pick("<b>WASD</b>",  "<b>left stick</b>",  "<b>left stick</b>");
    public static string MouseLook     => Pick("<b>mouse</b>", "<b>right stick</b>", "<b>right stick</b>");
    public static string BuildMenu     => Pick("<b>N</b>",     "<b>LB</b>",       "<b>L1</b>");
    public static string Fishingdex    => Pick("<b>B</b>",     "<b>RB</b>",       "<b>R1</b>");
    public static string AdvanceTip    => Pick("<b>TAB</b>",   "<b>LT</b>",       "<b>L2</b>");
    public static string DirThrustHold => Pick("<b>WASD + Shift</b>",
                                                "push left stick + <b>click L3</b>",
                                                "push left stick + <b>click L3</b>");
    public static string PlacementRotate => Pick("<b>RMB</b>",
                                                  "<b>LT + right stick</b>",
                                                  "<b>L2 + right stick</b>");
}
