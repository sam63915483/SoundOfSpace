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
    //  CONTROLLER SUPPORT (Xbox / DS4 / DualSense via the Unity Input System;
    //  keyboard & mouse stay on the legacy InputManager)
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

    // True while any UI that captures movement-shaped input is up (typing,
    // modal slot panels, the phone, or any focused menu Selectable).
    // Camera-FX modules zero their movement reads on this so navigating a
    // menu with the stick/D-pad can't pump FOV kicks or strafe tilt.
    public static bool MovementInputSuppressed =>
        AIChatScreen.IsTypingActive ||
        PlayerController.isInModalSlotUI ||
        PlayerPhoneUI.IsOpen ||
        UISelectionActive() || WasUIFocusedThisFrameStart();

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
        GetKeyUp(KeyCode.F, a) || (IsUnlocked(a) && PadReleased(PadButton.X));

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

    // Pistol reload — R key or pad X (same button as Interact/pickup; safe
    // to share because reload only fires with the pistol equipped, and you
    // can't be shooting while picking something up).
    public static bool ReloadPressed()
    {
        if (Input.GetKeyDown(KeyCode.R)) return true;
        return PadPressed(PadButton.X);
    }

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
        (IsUnlocked(a) && PadPressed(PadButton.Back));

    // ── D-pad slot edge detection (for hotbar / scroll-replacements) ───────
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

    // LB = -1 (previous slot), RB = +1 (next slot), else 0. Moved off the
    // D-pad so the D-pad is free for phone open/navigation. Suppressed while
    // a UI Selectable is focused (now or at frame start) so LB shift-transfer
    // in storage panels doesn't also flip hotbar slots, and while piloting
    // (LB/RB are the ship's roll bindings there).
    public static int HotbarCycleStep()
    {
        if (!ControllerEnabled) return 0;
        if (UISelectionActive() || _uiFocusedAtFrameStart) return 0;
        if (Ship.AnyShipPiloted) return 0;
        if (PadPressed(PadButton.LB)) return -1;
        if (PadPressed(PadButton.RB)) return +1;
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
        var g = ActivePad;
        float s = g != null ? g.leftStick.ReadValue().x : 0f;
        return Mathf.Clamp(k + s, -1f, 1f);
    }

    public static float MoveAxisVertical(TutorialAbility a)
    {
        if (!IsUnlocked(a)) return 0f;
        float k = 0f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) k -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))   k += 1f;
        var g = ActivePad;
        float s = g != null ? g.leftStick.ReadValue().y : 0f;
        return Mathf.Clamp(k + s, -1f, 1f);
    }

    // ── Look-axis composite (mouse delta + right-stick rate-scaled) ───────
    public static Vector2 LookDelta(TutorialAbility a)
    {
        if (!IsUnlocked(a)) return Vector2.zero;

        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");

        var g = ActivePad;
        if (g != null)
        {
            // Right-stick value is steady (-1..1), so scale by deltaTime to
            // get per-frame "movement" comparable to mouse delta. The x60
            // is a perceptual gain factor so default sensitivity 1 feels
            // close to mouse default; player tunes via StickLookSensitivity.
            float dt = Time.unscaledDeltaTime;
            float gain = StickLookSensitivity * dt * 60f;
            Vector2 stick = g.rightStick.ReadValue();
            mx += stick.x * gain;
            my += stick.y * gain * (InvertLookY ? -1f : 1f);
        }
        return new Vector2(mx, my);
    }

    // Headlight cycle: mouse scroll value (≈±0.05–0.15 per notch) OR
    // D-pad up/down edge (returns ±0.1 to match scroll-notch magnitude so the
    // caller's existing scroll-sensitivity multiplier feels the same on both).
    // Only consumed by the ship (piloted context), so it doesn't collide with
    // the on-foot D-pad-up phone-open binding.
    public static float HeadlightStep()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f) return scroll;
        if (DPadDirectionPressed(0)) return  0.1f;
        if (DPadDirectionPressed(2)) return -0.1f;
        return 0f;
    }

    // Level-detected D-pad hold (edge variant above). Same dir codes:
    // 0=Up, 1=Right, 2=Down, 3=Left.
    public static bool DPadDirectionHeld(int dir)
    {
        var g = ActivePad;
        if (g == null) return false;
        switch (dir)
        {
            case 0: return g.dpad.up.isPressed;
            case 1: return g.dpad.right.isPressed;
            case 2: return g.dpad.down.isPressed;
            case 3: return g.dpad.left.isPressed;
            default: return false;
        }
    }

    // ── Ship pilot bindings (pad additions — user-chosen 2026-07-15) ──────
    // All three are read from the PILOTED context only, so the pad inputs
    // don't collide with their on-foot roles (LT = secondary fire on foot;
    // D-pad left/right are ghost-placement rotate while building).

    // Engine ignition: HOLD I (keyboard) or HOLD D-pad Left (pad).
    public static bool EngineIgnitionHeld() =>
        Input.GetKey(KeyCode.I) || DPadDirectionHeld(3);

    // Pilot-seat hatch toggle: H (keyboard) or D-pad Right (pad).
    public static bool HatchTogglePressed() =>
        Input.GetKeyDown(KeyCode.H) || DPadDirectionPressed(1);

    // Ship boost: LeftShift (keyboard) or LT hold (pad).
    public static bool ShipBoostHeld() =>
        Input.GetKey(KeyCode.LeftShift) || (ControllerEnabled && LTValue() > TriggerThreshold);

    // ── Internals ─────────────────────────────────────────────────────────

    public static float LTValue() { var g = ActivePad; return g != null ? g.leftTrigger.ReadValue()  : 0f; }
    public static float RTValue() { var g = ActivePad; return g != null ? g.rightTrigger.ReadValue() : 0f; }
    static bool TriggerActive(float v) => v > TriggerThreshold;
    // Triggers are ButtonControls with a 0.5 default press point — matches
    // the old TriggerThreshold edge detection, no prev-frame tracking needed.
    static bool LTEdgePressed() { var g = ActivePad; return g != null && g.leftTrigger.wasPressedThisFrame; }
    static bool RTEdgePressed() { var g = ActivePad; return g != null && g.rightTrigger.wasPressedThisFrame; }

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
        // Connection state straight from the Input System device list — no
        // per-frame GetJoystickNames() string allocation, works in builds.
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

        GamepadRumble.Tick();
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
}

// Glyph strings for prompt UIs. Reads TutorialGate.LastSource and
// TutorialGate.DetectedController so prompts track BOTH the input mode
// (KBM vs controller) and the controller brand. On a pad, single buttons
// render as inline TMP sprite icons (<sprite name="xbox_a"> etc., from the
// InputGlyphs sprite asset registered as a global TMP fallback); if the
// sprite asset is missing the old text labels return as a fallback.
// Keyboard prompts stay as bold text — every consumer is a TMP text.
public static class PromptGlyphs
{
    static bool Pad => TutorialGate.LastSource == TutorialGate.InputSource.Controller;
    static bool PS  => TutorialGate.IsPlayStation;

    // One-time check that the glyph sprites actually resolve — if the atlas
    // build failed or the asset is missing, fall back to text labels instead
    // of rendering empty squares.
    static bool? _spritesOk;
    static bool SpritesOk
    {
        get
        {
            if (_spritesOk == null)
            {
                // Same case-sensitive hash the TMP tag parser uses.
                int hash = 0;
                foreach (char c in "xbox_a") hash = ((hash << 5) + hash) ^ c;
                _spritesOk = TMPro.TMP_SpriteAsset.SearchForSpriteByHashCode(
                    TMPro.TMP_Settings.defaultSpriteAsset, hash, true, out _) != null;
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

    public static string Jump          => Pick("<b>Space</b>", "<b>A</b>",     "<b>Cross</b>",    "xbox_a",    "ps_cross");
    public static string Interact      => Pick("<b>F</b>",     "<b>X</b>",     "<b>Square</b>",   "xbox_x",    "ps_square");
    public static string InteractPlain => Pick("F",            "X",            "Square",          "xbox_x",    "ps_square");
    public static string Sprint        => Pick("<b>Shift</b>", "<b>L3</b>",    "<b>L3</b>",       "xbox_l3",   "ps_l3");
    public static string DownThrust    => Pick("<b>Ctrl</b>",  "<b>R3</b>",    "<b>R3</b>",       "xbox_r3",   "ps_r3");
    public static string Flashlight    => Pick("<b>E</b>",     "<b>Y</b>",     "<b>Triangle</b>", "xbox_y",    "ps_triangle");
    public static string Drop          => Pick("<b>G</b>",     "<b>B</b>",     "<b>Circle</b>",   "xbox_b",    "ps_circle");
    public static string Map           => Pick("<b>M</b>",     "<b>View</b>",  "<b>Share</b>",    "xbox_view", "ps_share");
    public static string Pause         => Pick("<b>Esc</b>",   "<b>Start</b>", "<b>Options</b>",  "xbox_menu", "ps_options");
    public static string Cancel        => Pick("<b>Esc</b>",   "<b>B</b>",     "<b>Circle</b>",   "xbox_b",    "ps_circle");
    public static string PrimaryFire   => Pick("<b>LMB</b>",   "<b>RT</b>",    "<b>R2</b>",       "xbox_rt",   "ps_r2");
    public static string SecondaryFire => Pick("<b>RMB</b>",   "<b>LT</b>",    "<b>L2</b>",       "xbox_lt",   "ps_l2");
    public static string RollLeft      => Pick("<b>Q</b>",     "<b>LB</b>",    "<b>L1</b>",       "xbox_lb",   "ps_l1");
    public static string RollRight     => Pick("<b>E</b>",     "<b>RB</b>",    "<b>R1</b>",       "xbox_rb",   "ps_r1");
    // Build menu + Fishingdex have no direct pad button — pad players go
    // through the phone (D-pad up), so the pad glyph shows the phone binding.
    public static string BuildMenu     => Pick("<b>N</b>",     "<b>D-pad up</b>", "<b>D-pad up</b>", "xbox_dpad_up", "ps_dpad_up");
    public static string Fishingdex    => Pick("<b>B</b>",     "<b>D-pad up</b>", "<b>D-pad up</b>", "xbox_dpad_up", "ps_dpad_up");
    // Phone open: X on keyboard, D-pad up on pad (on foot).
    public static string PhoneOpen     => Pick("<b>X</b>",     "<b>D-pad up</b>", "<b>D-pad up</b>", "xbox_dpad_up", "ps_dpad_up");
    // Ship pilot bindings (text-only on pad — no left/right D-pad sprites in
    // the glyph asset, and the bold text reads fine in prompts/QTEs).
    public static string EngineIgnition => Pad ? "<b>D-pad left</b>"  : "<b>I</b>";
    public static string Hatch          => Pad ? "<b>D-pad right</b>" : "<b>H</b>";
    public static string ShipBoost      => Pick("<b>Shift</b>", "<b>LT</b>", "<b>L2</b>", "xbox_lt", "ps_l2");
    public static string AdvanceTip    => Pick("<b>TAB</b>",   "<b>LT</b>",    "<b>L2</b>",       "xbox_lt",   "ps_l2");
    public static string Reload        => Pick("<b>R</b>",     "<b>X</b>",     "<b>Square</b>",   "xbox_x",    "ps_square");
    public static string Move          => Pick("<b>WASD</b>",  "<b>left stick</b>",  "<b>left stick</b>",  "pad_stick_l", "pad_stick_l");
    public static string MouseLook     => Pick("<b>mouse</b>", "<b>right stick</b>", "<b>right stick</b>", "pad_stick_r", "pad_stick_r");
    // Compound phrases keep text + inline glyph:
    public static string PrimaryClick    => Pad ? $"pull {PrimaryFire}" : "<b>left click</b>";
    public static string PrimaryClickCap => Pad ? $"Pull {PrimaryFire}" : "<b>Left click</b>";
    public static string DirThrustHold   => Pad ? $"push {Move} + click {Sprint}" : "<b>WASD + Shift</b>";
    public static string PlacementRotate => Pad ? $"{SecondaryFire} + {MouseLook}" : "<b>RMB</b>";
}
