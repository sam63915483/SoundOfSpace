# Controller pass 2: virtual cursor + pad bug fixes — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the yellow highlight-box pad navigation with a Cyberpunk-style virtual cursor on every mouse-driven screen, keep the highlight box (fixed) for NPC dialogue option lists, and close the pad bugs from Sam's 2026-09-06 playtest (dialogue can't scroll, NAV strands the pad at 100 m, no B-to-close on three screens, sapling placement jumps).

**Architecture:** A new `PadCursor` component (child of the existing `ControllerUINavigator` singleton) wraps Unity's `VirtualMouseInput`, which feeds a virtual `Mouse` device that `InputSystemUIInputModule` already treats as a real pointer — so every UGUI canvas gets hover/click/drag/scroll with no per-screen code. It activates by one rule (pad was last touched **and** the OS cursor is unlocked **and** no dialogue "focus owner" is up **and** no suppress token is held). While it is active, `TutorialGate` mutes the pad's A/X/left stick/right stick for gameplay at its existing choke points. Dialogue option lists get a `ControllerFocusOwner` marker that keeps the old highlight path and stops the navigator from clearing their focus.

**Tech Stack:** Unity 2022.3, Built-in RP, Input System 1.14 (`Gamepad.current` device API + `VirtualMouseInput`), legacy Input Manager for KBM (`activeInputHandler: Both` — never flip), UGUI + TMP 3.0.7. No `.inputactions` asset (actions are built in code). Spec: `docs/superpowers/specs/2026-09-06-controller-virtual-cursor-design.md`.

**Verification model:** this project has no CLI build or test runner (CLAUDE.md). Every task ends with the offline compile check, which must print `compile check: PASS` and emit **zero** `warning CS` lines (the warning baseline is 0; any warning is real):

```
py -3 prototypes/shuttle-computer/test/compile-unity.py
```

Sam runs the single playtest (hard rule: never enter play mode ourselves). Task 13 writes his checklist.

**Conventions that apply to every task** (from CLAUDE.md): append new serialized fields at the END of a MonoBehaviour; `git add` new `.cs` files **and** their `.meta` (Unity generates the `.meta` only when the Editor imports — if no `.meta` exists yet, add the `.cs` alone and note it; the Editor will create it); never `FindObjectOfType` in `Update`; commit after each task.

**Branch:** work on `feat/helmet-hud` (current branch, clean).

---

## File map

| File | Status | Responsibility |
|---|---|---|
| `Assets/3 - Scripts/UI/ControllerFocusOwner.cs` | create | Marker: "this panel owns pad focus" (dialogue option lists). Static active count. |
| `Assets/3 - Scripts/UI/PadCursor.cs` | create | The virtual cursor: activation rule, VirtualMouseInput setup, ring/dot graphic + press morph, bounds clamp, suppress tokens, legacy-read shims. |
| `Assets/3 - Scripts/UI/ControllerUINavigator.cs` | modify | Creates PadCursor; respects focus owners; defers to the cursor (no auto-focus / no border) while it is active; `EnsureEventSystem` becomes public. |
| `Assets/3 - Scripts/Tutorial/TutorialGate.cs` | modify | Cursor mute at the choke points (A/X buttons, left stick, right stick, hotbar cycle, `MovementInputSuppressed`); `LeftStickRaw()` for NAV hover. |
| `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` | modify | Ghost placement: pad A never jumps while placing. |
| `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` | modify | `padCursorSpeed` pref (append-only). |
| `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` | modify | "CURSOR SPEED (PAD)" slider row. |
| `Assets/3 - Scripts/UI/StorageUI.cs`, `UI/FishStagingUI.cs`, `Vendor/MushroomSellUI.cs`, `Music/ShuttleComputerNavUI.cs`, `Music/ShuttleComputerCoopUI.cs` | modify | Raw-mouse reads → `PadCursor` shims. |
| `Assets/3 - Scripts/Vendor/TevPaymentUI.cs`, `Music/ShuttleComputerUI.cs` | modify | Pad B closes / backs out. |
| `Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs` | modify | Focus owner, explicit nav, initial focus, select-paint, B cancel, sorting 905. |
| `Assets/3 - Scripts/Story/NpcGraphWalker.cs`, `NPC_Dialogue/AuthoredNPCTalk.cs`, `Fishing/FishMarketNPC.cs`, `Vendor/Alien7Vendor.cs`, `Vendor/ShipMarketNPC.cs`, `NPC_Dialogue/RandomAlienDialogue.cs`, `NPC_Dialogue/TevDialogue.cs`, `NPC_Dialogue/ShipInstructorDialogue.cs`, `NPC_Dialogue/TevMushroomOnboarding.cs`, `Story/WorldDialogueUI.cs` | modify | Cancel handling / `cancellable:false` / focus-owner marker. |
| `Assets/3 - Scripts/Music/ShuttleComputerNavUI.cs` | modify | Hover hand-off on pad + suppress token + glyph prompts. |
| `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` | modify | Cursor bounds = tablet screen; pad stick no longer closes the phone. |
| `Assets/3 - Scripts/Map/SolarMap.cs`, `Map/SolarMapOverlay.cs`, `Map/MapCameraRig.cs` | modify | Y toggles cursor mode; shimmed click/zoom; hint text; pad fly reads muted while the cursor is up. |
| `Assets/3 - Scripts/Building/BuildMenuUI.cs`, `UI/MainMenuController.cs`, `Tutorial/TutorialPerformanceReview.cs`, `Music/ShuttleComputerUI.cs` | modify | Stop re-adding `StandaloneInputModule`; call the navigator's `EnsureEventSystem`. |
| `docs/PLAYTEST_CONTROLLER_CURSOR.md` | create | Sam's one-pass checklist. |
| `docs/CURRENT_STATE_AUDIT.md` | modify | §31 addendum. |

---

### Task 1: `ControllerFocusOwner` marker + navigator respects it + public `EnsureEventSystem`

**Files:**
- Create: `Assets/3 - Scripts/UI/ControllerFocusOwner.cs`
- Modify: `Assets/3 - Scripts/UI/ControllerUINavigator.cs` (`EnsureEventSystem` ~line 101; `Update` ~line 340)

- [ ] **Step 1: Create the marker**

```csharp
using UnityEngine;

// Marker component (2026-09-06 controller pass 2). Put it on the panel
// GameObject that a dialogue option list toggles on/off (PostGreetingChoicePanel
// "Panel", WorldDialogueUI root). Two effects:
//
//   1. ControllerUINavigator leaves the EventSystem selection alone while it
//      sits under an owner — no migration to a "topmost" canvas, no clearing
//      when the row is mid fade-in (the owner re-selects its own rows).
//   2. PadCursor stays INACTIVE while any owner is active, so dialogue
//      options keep the highlight-box navigation (Sam's call: the cursor is
//      for mouse-style screens; option lists stay stick + A).
//
// Active count is kept in OnEnable/OnDisable so the check is O(1) per frame.
public class ControllerFocusOwner : MonoBehaviour
{
    static int s_active;

    public static bool AnyActive => s_active > 0;

    public static bool Owns(GameObject go) =>
        go != null && go.GetComponentInParent<ControllerFocusOwner>() != null;

    void OnEnable()  { s_active++; }
    void OnDisable() { s_active = Mathf.Max(0, s_active - 1); }
}
```

- [ ] **Step 2: Make `EnsureEventSystem` public and pin the pointer behaviour**

In `ControllerUINavigator.cs` change the signature at ~line 101:

```csharp
    // Public (2026-09-06): the four scripts that used to spawn their own
    // EventSystem + legacy StandaloneInputModule now call this instead, so a
    // stray legacy module can never sit next to the Input System one and
    // silently win in a build (it would not see the virtual mouse).
    public static void EnsureEventSystem()
```

and, inside it, right after `module.AssignDefaultActions();` add:

```csharp
            // All mice (hardware + PadCursor's virtual one) unify into ONE
            // pointer; whichever moved last drives it. This is the default,
            // stated explicitly because PadCursor depends on it.
            module.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
```

`UIPointerBehavior` lives in `UnityEngine.InputSystem.UI`, which the file already imports.

- [ ] **Step 3: Respect focus owners in `Update`**

In `Update`, directly after these two existing lines (~line 340):

```csharp
        var topmost = _cachedTopmost;
        var current = es.currentSelectedGameObject;
```

insert:

```csharp
        // Dialogue option lists own their focus (ControllerFocusOwner): never
        // migrate away from, or clear, a row they selected — including during
        // the row's fade-in, when IsValidSelection would call it invisible and
        // the old code set the selection to null every quarter second.
        if (current != null && ControllerFocusOwner.Owns(current))
        {
            if (TutorialGate.LastSource == TutorialGate.InputSource.Controller) UpdateBorder(current);
            else HideBorder();
            return;
        }
```

- [ ] **Step 4: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/ControllerFocusOwner.cs" "Assets/3 - Scripts/UI/ControllerFocusOwner.cs.meta" "Assets/3 - Scripts/UI/ControllerUINavigator.cs"
git commit -m "feat(controller): ControllerFocusOwner marker; navigator leaves owned focus alone; EnsureEventSystem public"
```

(If the `.meta` does not exist yet because the Editor has not imported the file, add without it and say so in the task report.)

---

### Task 2: `PadCursor` — the virtual cursor

**Files:**
- Create: `Assets/3 - Scripts/UI/PadCursor.cs`

- [ ] **Step 1: Write the component**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Cyberpunk-style virtual cursor for gamepads (controller pass 2, 2026-09-06).
///
/// HOW IT WORKS. Unity's VirtualMouseInput adds a virtual Mouse device and
/// pushes stick motion / A / X / right-stick scroll into it. The
/// InputSystemUIInputModule (installed by ControllerUINavigator) treats that
/// device as a real pointer, so EVERY UGUI canvas gets hover, click, drag and
/// scroll from the pad with no per-screen code — main menu, pause, vendors,
/// storage, shuttle computer, TRAX knobs, phone apps, map legend.
///
/// WHEN IT IS UP (the one rule — see Wanted()):
///   pad enabled  &&  OS cursor UNLOCKED  &&  pad was the last input source
///   &&  no ControllerFocusOwner active (dialogue option lists keep the
///   highlight box)  &&  no suppress token held (NAV hover flight).
/// Every mouse-driven screen already unlocks the OS cursor on open and
/// re-locks on close, so "cursor unlocked" == "a mouse screen is open".
///
/// WHILE IT IS UP: TutorialGate mutes the pad's A / X / left stick / right
/// stick for gameplay (A and X ARE the mouse buttons now), the navigator
/// stops auto-focusing and drawing the yellow box, and EventSystem
/// navigation events are off (so a stick move can't ALSO hop the selection
/// and A can't ALSO Submit a selected button).
///
/// Legacy readers (Input.mousePosition etc.) can't see the virtual device —
/// they go through PointerPosition / PrimaryDown / PrimaryHeld / ScrollDelta.
/// </summary>
[DefaultExecutionOrder(-990)]   // after ControllerUINavigator (-1000), before everything else
public class PadCursor : MonoBehaviour
{
    public static PadCursor Instance { get; private set; }

    /// True while the virtual cursor owns the pad.
    public static bool IsActive => Instance != null && Instance._active;

    /// Pause menu CONTROLLER tab → InputSettings.padCursorSpeed (0.5–2.0).
    public static float SpeedMultiplier = 1f;

    const float BaseSpeedPxPerSec = 1400f;   // full deflection at 1080p, ×1.0
    const float RingDiameterPx    = 26f;     // at 1080p; scales with screen height
    const float PressInSeconds    = 0.08f;   // ring → dot
    const float ReleaseSeconds    = 0.12f;   // dot → ring, with overshoot
    const float ScrollNotchesPerSecond = 6f; // right stick full deflection, for legacy scroll readers

    // ── suppress tokens (NAV hover flight holds one) ─────────────────────
    static readonly HashSet<string> s_suppress = new HashSet<string>();
    public static void Suppress(string token) { if (!string.IsNullOrEmpty(token)) s_suppress.Add(token); }
    public static void Release(string token)  { s_suppress.Remove(token); }

    // ── legacy-read shims ────────────────────────────────────────────────
    /// Screen-pixel pointer position: the virtual cursor while active, else the mouse.
    public static Vector2 PointerPosition =>
        IsActive ? Instance._vm.virtualMouse.position.ReadValue() : (Vector2)Input.mousePosition;

    /// Primary click edge: pad A while active, else LMB. Reads the pad directly
    /// (TutorialGate mutes A while the cursor is up — this is the one reader
    /// that must still see it).
    public static bool PrimaryDown =>
        IsActive ? (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
                 : Input.GetMouseButtonDown(0);

    public static bool PrimaryHeld =>
        IsActive ? (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed)
                 : Input.GetMouseButton(0);

    /// Scroll notches this frame: right-stick Y while active (fractional,
    /// ~6 notches/s at full deflection), else Input.mouseScrollDelta.y.
    public static float ScrollDelta => IsActive ? Instance._scrollNotches : Input.mouseScrollDelta.y;

    /// Clamp the cursor inside a UI rect (the phone's tablet screen). Null = whole screen.
    public static void SetBounds(RectTransform rt) { if (Instance != null) Instance._bounds = rt; }
    public static void ClearBounds()                { if (Instance != null) Instance._bounds = null; }

    /// Called by ControllerUINavigator.Awake so the cursor exists wherever the
    /// navigator does (MainMenu and gameplay alike — no trap-#1 seeding).
    public static void Create(GameObject host)
    {
        if (Instance == null && host != null) host.AddComponent<PadCursor>();
    }

    // ── state ─────────────────────────────────────────────────────────────
    VirtualMouseInput _vm;
    Canvas _canvas;
    RectTransform _cursorRT, _ringRT, _dotRT;
    Image _ring, _dot;
    RectTransform _bounds;
    bool _active;
    bool _navEventsOff;
    float _pressT;
    float _scrollNotches;
    readonly Vector3[] _corners = new Vector3[4];

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        BuildGraphic();
        BuildVirtualMouse();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_navEventsOff && EventSystem.current != null) EventSystem.current.sendNavigationEvents = true;
    }

    void BuildGraphic()
    {
        // Own overlay canvas, one above the navigator's border canvas (32000).
        // ConstantPixelSize so anchoredPosition == screen pixels, which is what
        // VirtualMouseInput reads/writes. NO GraphicRaycaster — the cursor
        // must never be something the cursor can hit.
        var canvasGO = new GameObject("PadCursorCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32001;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        var cursorGO = new GameObject("Cursor", typeof(RectTransform));
        cursorGO.transform.SetParent(canvasGO.transform, false);
        _cursorRT = (RectTransform)cursorGO.transform;
        _cursorRT.anchorMin = Vector2.zero;
        _cursorRT.anchorMax = Vector2.zero;
        _cursorRT.pivot = new Vector2(0.5f, 0.5f);
        _cursorRT.sizeDelta = new Vector2(64f, 64f);

        _ring = MakeImage(_cursorRT, "Ring", MakeRingSprite(), out _ringRT);
        _dot  = MakeImage(_cursorRT, "Dot",  MakeDotSprite(),  out _dotRT);
        SetAlpha(_dot, 0f);

        cursorGO.SetActive(false);
    }

    static Image MakeImage(RectTransform parent, string name, Sprite sprite, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(64f, 64f);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.color = Color.white;
        return img;
    }

    void BuildVirtualMouse()
    {
        // AddComponent enables it immediately (device added); configure, then
        // park it disabled until the activation rule says otherwise.
        _vm = gameObject.AddComponent<VirtualMouseInput>();
        _vm.cursorMode = VirtualMouseInput.CursorMode.SoftwareCursor;
        _vm.cursorTransform = _cursorRT;
        _vm.cursorGraphic = _ring;       // also how it finds our canvas for its own clamp
        _vm.cursorSpeed = BaseSpeedPxPerSec;
        _vm.scrollSpeed = 20f;           // VirtualMouseInput default — UGUI ScrollRect feel
        _vm.stickAction       = new InputActionProperty(new InputAction("PadCursorStick",  InputActionType.Value,  "<Gamepad>/leftStick"));
        _vm.leftButtonAction  = new InputActionProperty(new InputAction("PadCursorLeft",   InputActionType.Button, "<Gamepad>/buttonSouth"));
        _vm.rightButtonAction = new InputActionProperty(new InputAction("PadCursorRight",  InputActionType.Button, "<Gamepad>/buttonWest"));
        _vm.scrollWheelAction = new InputActionProperty(new InputAction("PadCursorScroll", InputActionType.Value,  "<Gamepad>/rightStick"));
        _vm.enabled = false;
    }

    // ── activation ───────────────────────────────────────────────────────
    static bool Wanted()
    {
        if (!TutorialGate.ControllerEnabled) return false;
        if (Cursor.lockState != CursorLockMode.None) return false;
        if (TutorialGate.LastSource != TutorialGate.InputSource.Controller) return false;
        if (s_suppress.Count > 0) return false;
        if (ControllerFocusOwner.AnyActive) return false;
        return true;
    }

    void SetActive(bool on)
    {
        _active = on;
        if (on)
        {
            _pressT = 0f;
            _scrollNotches = 0f;
            // Spawn at screen centre every time (no memory across screens).
            // VirtualMouseInput.OnEnable seeds the device from this position.
            _cursorRT.anchoredPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _vm.enabled = true;
            _cursorRT.gameObject.SetActive(true);
        }
        else
        {
            _vm.enabled = false;            // OnDisable removes the device — no stale pointer
            _cursorRT.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        bool want = Wanted();
        if (want != _active) SetActive(want);

        // Navigation events off while the cursor owns the pad: otherwise the
        // left stick would ALSO Navigate the EventSystem selection (a clicked
        // button becomes selected) and A would ALSO Submit it. Restored the
        // moment the cursor goes away. Only ever un-does what it did itself.
        var es = EventSystem.current;
        if (es != null)
        {
            if (_active && es.sendNavigationEvents) { es.sendNavigationEvents = false; _navEventsOff = true; }
            else if (!_active && _navEventsOff)     { es.sendNavigationEvents = true;  _navEventsOff = false; }
        }

        if (!_active) return;

        float screenScale = Screen.height / 1080f;
        _vm.cursorSpeed = BaseSpeedPxPerSec * screenScale * Mathf.Clamp(SpeedMultiplier, 0.25f, 4f);
        _cursorRT.localScale = Vector3.one * (screenScale * (RingDiameterPx / 64f) * 2f);

        var g = Gamepad.current;
        float ry = g != null ? g.rightStick.ReadValue().y : 0f;
        _scrollNotches = ry * ScrollNotchesPerSecond * Time.unscaledDeltaTime;

        // Press morph: ring shrinks into a dot on A down, pops back out on release.
        bool held = g != null && g.buttonSouth.isPressed;
        float rate = held ? 1f / PressInSeconds : 1f / ReleaseSeconds;
        _pressT = Mathf.MoveTowards(_pressT, held ? 1f : 0f, rate * Time.unscaledDeltaTime);
        float e = _pressT * _pressT * (3f - 2f * _pressT);      // smoothstep
        float ringScale = Mathf.Lerp(1f, 0.35f, e);
        if (!held && _pressT > 0f)                              // ease-out-back: ~7 % overshoot on the way out
            ringScale += 0.15f * Mathf.Sin(Mathf.Clamp01(_pressT * 2f) * Mathf.PI);
        _ringRT.localScale = Vector3.one * ringScale;
        SetAlpha(_ring, 1f - e);
        _dotRT.localScale = Vector3.one * Mathf.Max(0.01f, e);
        SetAlpha(_dot, e);
    }

    void LateUpdate()
    {
        if (!_active) return;
        // Clamp to the bounds rect (phone screen) or the whole screen.
        Vector2 p = _vm.virtualMouse.position.ReadValue();
        Vector2 min = Vector2.zero, max = new Vector2(Screen.width, Screen.height);
        if (_bounds != null && _bounds.gameObject.activeInHierarchy)
        {
            var canvas = _bounds.GetComponentInParent<Canvas>();
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            Camera cam = (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay) ? root.worldCamera : null;
            _bounds.GetWorldCorners(_corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);
            min = Vector2.Min(a, b);
            max = Vector2.Max(a, b);
        }
        Vector2 c = new Vector2(Mathf.Clamp(p.x, min.x, max.x), Mathf.Clamp(p.y, min.y, max.y));
        if (c != p)
        {
            InputState.Change(_vm.virtualMouse.position, c);
            _cursorRT.anchoredPosition = c;
        }
    }

    static void SetAlpha(Image img, float a)
    {
        var col = img.color; col.a = Mathf.Clamp01(a); img.color = col;
    }

    // ── procedural sprites (no art asset; same approach as the navigator's border) ──
    static Sprite s_ring, s_dot;

    // 64×64: a 2.5 px white ring of radius 14 with a 1 px 50 % dark edge on
    // both sides so it reads on light AND dark backgrounds. Drawn at 2× and
    // scaled down by _cursorRT.localScale for smooth edges.
    static Sprite MakeRingSprite()
    {
        if (s_ring != null) return s_ring;
        s_ring = MakeCircleSprite(radius: 22f, stroke: 4f, filled: false, name: "PadCursorRing");
        return s_ring;
    }

    static Sprite MakeDotSprite()
    {
        if (s_dot != null) return s_dot;
        s_dot = MakeCircleSprite(radius: 9f, stroke: 0f, filled: true, name: "PadCursorDot");
        return s_dot;
    }

    static Sprite MakeCircleSprite(float radius, float stroke, bool filled, string name)
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[S * S];
        float cx = (S - 1) * 0.5f, cy = (S - 1) * 0.5f;
        const float Edge = 1.5f;   // dark halo width
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            // Signed distance to the white shape's boundary (negative = inside white).
            float sd = filled ? d - radius : Mathf.Abs(d - radius) - stroke * 0.5f;
            float white = 1f - Mathf.Clamp01(sd + 0.5f);                  // AA inside the shape
            float halo  = 1f - Mathf.Clamp01(sd - Edge + 0.5f);           // AA shape + edge
            float dark  = Mathf.Clamp01(halo - white) * 0.5f;             // 50 % dark ring outside the white
            px[y * S + x] = new Color(white, white, white, Mathf.Clamp01(white + dark));
        }
        tex.SetPixels(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        sp.name = name;
        return sp;
    }
}
```

Notes for the implementer:
- `Instance._vm.virtualMouse.position.ReadValue()` is safe while `IsActive` (the device is added in `VirtualMouseInput.OnEnable`, removed in `OnDisable`).
- The sprite colour math: pixels inside the white shape are `(1,1,1,1)`; the 1.5 px halo outside is `(0,0,0,0.5)` premultiplied into the alpha channel as `rgb=0, a=0.5`. That is what `new Color(white, white, white, white + dark)` produces when `white==0`.
- Ring visual size: sprite radius 22 px in a 64 px texture, `localScale = screenScale × (26/64) × 2` → ~18 px radius on screen at 1080p, i.e. roughly a 36 px outer diameter; the dot is ~7 px. If Sam finds it small/large the two constants at the top are the knobs.

- [ ] **Step 2: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines. (PadCursor references `ControllerFocusOwner` from Task 1 and `TutorialGate` members that already exist.)

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/PadCursor.cs" "Assets/3 - Scripts/UI/PadCursor.cs.meta"
git commit -m "feat(controller): PadCursor — VirtualMouseInput-backed virtual cursor with ring/dot morph, bounds, suppress tokens, legacy shims"
```

---

### Task 3: Navigator creates the cursor and defers to it

**Files:**
- Modify: `Assets/3 - Scripts/UI/ControllerUINavigator.cs` (`Awake` ~line 52; `Update` ~line 327)

- [ ] **Step 1: Create PadCursor in `Awake`**

Change:

```csharp
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureEventSystem();
        BuildBorderUI();
    }
```

to:

```csharp
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureEventSystem();
        BuildBorderUI();
        // The virtual cursor lives on this same DontDestroyOnLoad object so it
        // exists in MainMenu and gameplay alike (no EnsureGameplaySingletons
        // seeding needed — trap #1 is avoided by construction).
        PadCursor.Create(gameObject);
    }
```

- [ ] **Step 2: Defer to the cursor in `Update`**

Directly after the existing phone-camera block:

```csharp
        if (PlayerPhoneUI.IsCameraMode)
        {
            if (es.currentSelectedGameObject != null && !IsTextInputSelected(es))
                es.SetSelectedGameObject(null);
            HideBorder();
            return;
        }
```

insert:

```csharp
        // Virtual cursor owns the pad: no auto-focus, no yellow box. A pointer
        // click selects the Button it hit, so clear that stray selection every
        // frame — otherwise a later stick move could Navigate it and A could
        // Submit it as well as click. Same InputField exception as KBM mode.
        // Modal raycaster suppression above still runs (it protects clicks).
        if (PadCursor.IsActive)
        {
            if (es.currentSelectedGameObject != null && !IsTextInputSelected(es))
                es.SetSelectedGameObject(null);
            HideBorder();
            return;
        }
```

- [ ] **Step 3: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/ControllerUINavigator.cs"
git commit -m "feat(controller): navigator spawns PadCursor and stands down while it is active"
```

---

### Task 4: Gameplay stays muted while the cursor is up + sapling jump fix

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/TutorialGate.cs` (~lines 114–121, 155–157, 169–170, 300–310 `HotbarCycleStep`, 380–393 `MoveStick`, 399–418 `LookDelta`)
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` (~line 758)
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (~line 1605)

- [ ] **Step 1: `MovementInputSuppressed` gains the cursor**

```csharp
    public static bool MovementInputSuppressed =>
        AIChatScreen.IsTypingActive ||
        PlayerController.isInModalSlotUI ||
        PlayerPhoneUI.IsOpen ||
        // The pause menu no longer stops the clock in multiplayer, so it has to
        // announce itself here instead of relying on timeScale to do it.
        PauseState.MenuOpen ||
        // Virtual cursor (controller pass 2): the sticks belong to the cursor.
        PadCursor.IsActive ||
        UISelectionActive() || WasUIFocusedThisFrameStart();
```

- [ ] **Step 2: Mute A and X at the raw pad reads**

Replace the three raw readers:

```csharp
    public static bool PadHeld(PadButton b)     { var g = ActivePad; return g != null && Resolve(g, b).isPressed; }
    public static bool PadPressed(PadButton b)  { var g = ActivePad; return g != null && Resolve(g, b).wasPressedThisFrame; }
    public static bool PadReleased(PadButton b) { var g = ActivePad; return g != null && Resolve(g, b).wasReleasedThisFrame; }
```

with:

```csharp
    // While the virtual cursor is up, A and X ARE the mouse buttons (left /
    // right click) and must reach gameplay through nothing else. Every
    // composite (Jump, Interact, Reload, PrimaryAction, swim-up, map fly-up …)
    // funnels through these three, so the mute lives here — one place, not
    // thirty. B / Y / bumpers / triggers / Start are untouched (B closes
    // screens; the rest never double as pointer buttons). PadCursor itself
    // reads A straight off the device for its PrimaryDown shim.
    static bool MutedByCursor(PadButton b) =>
        (b == PadButton.A || b == PadButton.X) && PadCursor.IsActive;

    public static bool PadHeld(PadButton b)     { var g = ActivePad; return g != null && !MutedByCursor(b) && Resolve(g, b).isPressed; }
    public static bool PadPressed(PadButton b)  { var g = ActivePad; return g != null && !MutedByCursor(b) && Resolve(g, b).wasPressedThisFrame; }
    public static bool PadReleased(PadButton b) { var g = ActivePad; return g != null && !MutedByCursor(b) && Resolve(g, b).wasReleasedThisFrame; }
```

- [ ] **Step 3: Mute the sticks**

Replace the two right-stick readers:

```csharp
    public static float RightStickX() { var g = ActivePad; return g != null ? g.rightStick.ReadValue().x : 0f; }
    public static float RightStickY() { var g = ActivePad; return g != null ? g.rightStick.ReadValue().y : 0f; }
```

with:

```csharp
    // Right stick is the scroll wheel while the virtual cursor is up.
    public static float RightStickX() { var g = ActivePad; return (g != null && !PadCursor.IsActive) ? g.rightStick.ReadValue().x : 0f; }
    public static float RightStickY() { var g = ActivePad; return (g != null && !PadCursor.IsActive) ? g.rightStick.ReadValue().y : 0f; }
```

In `MoveStick()` change the first two lines:

```csharp
    static Vector2 MoveStick()
    {
        var g = ActivePad;
        if (g == null) return Vector2.zero;
```

to:

```csharp
    static Vector2 MoveStick() => PadCursor.IsActive ? Vector2.zero : LeftStickRaw();

    // Left stick with the radial deadzone below, NO cursor mute and NO ability
    // gate. Only for readers that fly something while a mouse screen is open
    // and the cursor is deliberately suppressed (ShuttleComputerNavUI hover).
    public static Vector2 LeftStickRaw()
    {
        var g = ActivePad;
        if (g == null) return Vector2.zero;
```

(the rest of the old `MoveStick` body — `Vector2 s = g.leftStick.ReadValue(); … return s * (…)` — now belongs to `LeftStickRaw`; leave it exactly as it was).

In `LookDelta` change:

```csharp
        var g = ActivePad;
        if (g != null)
        {
```

to:

```csharp
        var g = ActivePad;
        if (g != null && !PadCursor.IsActive)
        {
```

- [ ] **Step 4: Hotbar cycling can't fire under a cursor screen**

In `HotbarCycleStep()` add one line after the `ControllerEnabled` check:

```csharp
        if (!ControllerEnabled) return 0;
        if (PadCursor.IsActive) return 0;   // LB/RB page vendor tabs, not the hotbar, while a cursor screen is up
        if (UISelectionActive() || _uiFocusedAtFrameStart) return 0;
```

- [ ] **Step 5: Ghost placement — pad A places, never jumps**

`PlayerController.cs` ~line 758, change:

```csharp
		bool jumpButtonDown = Input.GetKeyDown(KeyCode.Space) ||
			TutorialGate.PadPressed(TutorialGate.PadButton.A);
```

to:

```csharp
		// Pad A is also the ghost-placement confirm (GhostPlacement reads
		// PrimaryActionPressed). While any placement is up — sapling, mushroom,
		// building — A places and must NOT also jump (Sam, 2026-09-06). Keyboard
		// is unaffected: Space jumps, LMB places. Once the last sapling is
		// placed the ghost ends and A jumps again.
		bool jumpButtonDown = Input.GetKeyDown(KeyCode.Space) ||
			(!GhostPlacement.IsPlacing && TutorialGate.PadPressed(TutorialGate.PadButton.A));
```

- [ ] **Step 6: Phone movement-close ignores the pad while the cursor is up**

`PlayerPhoneUI.cs` ~line 1605, change:

```csharp
            bool padMoving = !TutorialGate.UISelectionActive()
                && !TutorialGate.WasUIFocusedThisFrameStart()
```

to:

```csharp
            bool padMoving = !TutorialGate.UISelectionActive()
                && !TutorialGate.WasUIFocusedThisFrameStart()
                && !PadCursor.IsActive                  // the stick is the cursor; B puts the phone away
```

- [ ] **Step 7: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 8: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/TutorialGate.cs" "Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs" "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "feat(controller): mute A/X/sticks for gameplay while PadCursor is up; pad A never jumps during ghost placement"
```

---

### Task 5: Cursor speed setting

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` (load ~line 330, save ~line 447, `PushControllerSettingsToGate` ~line 676, END of class ~line 703)
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` (~line 589, after the STICK DEADZONE row)

- [ ] **Step 1: Append the field at the END of `InputSettings`**

Just before the class's final `}` (after `public bool mirror60Hz = false; …`):

```csharp
	// ── Pad cursor (APPEND-ONLY: serialized fields stay at class end) ──
	[Header("Pad Cursor")]
	[Range(0.5f, 2f)] public float padCursorSpeed = 1f;   // virtual-cursor speed multiplier (pause menu CONTROLLER tab)
```

- [ ] **Step 2: Load / save / push**

Load block (next to `stickLookSensitivity = PlayerPrefs.GetFloat(...)`):

```csharp
		padCursorSpeed       = PlayerPrefs.GetFloat (nameof (padCursorSpeed),       1f);
```

Save block (next to `PlayerPrefs.SetFloat (nameof (stickLookSensitivity), …)`):

```csharp
		PlayerPrefs.SetFloat (nameof (padCursorSpeed),       padCursorSpeed);
```

`PushControllerSettingsToGate`, after `GamepadRumble.Enabled = vibrationEnabled;`:

```csharp
		PadCursor.SpeedMultiplier             = padCursorSpeed;
```

- [ ] **Step 3: Slider row**

In `TabbedPauseMenu.cs`, directly after the STICK DEADZONE `SliderDef` (the one ending `_input.stickDeadzone = v; _input.PushControllerSettingsToGate(); },` + `},`) add:

```csharp
                    new SliderDef {
                        label = "CURSOR SPEED (PAD)", min = 0.5f, max = 2f, wholeNumbers = false, format = "{0:F2}",
                        get  = () => _input != null ? _input.padCursorSpeed : 1f,
                        set  = v  => { if (_input == null) return; _input.padCursorSpeed = v; _input.PushControllerSettingsToGate(); },
                    },
```

- [ ] **Step 4: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs" "Assets/3 - Scripts/UI/TabbedPauseMenu.cs"
git commit -m "feat(controller): CURSOR SPEED (PAD) slider + padCursorSpeed pref"
```

---

### Task 6: Legacy mouse reads → PadCursor shims (non-map)

**Files:**
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs` (~line 255)
- Modify: `Assets/3 - Scripts/UI/FishStagingUI.cs` (~line 250)
- Modify: `Assets/3 - Scripts/Vendor/MushroomSellUI.cs` (~line 334)
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerNavUI.cs` (~line 429)
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerCoopUI.cs` (~lines 374, 379)

- [ ] **Step 1: StorageUI drag ghost**

Change:

```csharp
            Vector2 screen = Input.mousePosition;
            if (TutorialGate.LastSource == TutorialGate.InputSource.Controller)
```

to:

```csharp
            Vector2 screen = PadCursor.PointerPosition;
            // Old pad path (snap the ghost to the FOCUSED slot) only when the
            // cursor is not the pointer — with the cursor up there is no focus.
            if (TutorialGate.LastSource == TutorialGate.InputSource.Controller && !PadCursor.IsActive)
```

- [ ] **Step 2: FishStagingUI drag ghost** — identical edit:

```csharp
            Vector2 screen = PadCursor.PointerPosition;
            if (TutorialGate.LastSource == TutorialGate.InputSource.Controller && !PadCursor.IsActive)
```

- [ ] **Step 3: MushroomSellUI cursor follower**

```csharp
            _cursorRT.anchoredPosition = PadCursor.PointerPosition / scale;
```

- [ ] **Step 4: NAV en-route feed click**

```csharp
        if (ShuttleComputerUI.IsOpen && PadCursor.PrimaryDown)
            pilot.ToggleTransitFeed();
```

- [ ] **Step 5: Co-op cursor publish**

```csharp
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _screenRT, PadCursor.PointerPosition, null, out Vector2 local)) return;
```

and

```csharp
        TraxSessionSync.PublishCursor(normalized, view, PadCursor.PrimaryHeld);
```

- [ ] **Step 6: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/UI/StorageUI.cs" "Assets/3 - Scripts/UI/FishStagingUI.cs" "Assets/3 - Scripts/Vendor/MushroomSellUI.cs" "Assets/3 - Scripts/Music/ShuttleComputerNavUI.cs" "Assets/3 - Scripts/Music/ShuttleComputerCoopUI.cs"
git commit -m "feat(controller): raw mouse reads go through PadCursor shims (storage, fish picker, mushroom sell, NAV feed, co-op cursor)"
```

---

### Task 7: B / Circle closes the last Escape-only screens

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/TevPaymentUI.cs` (~line 148)
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerUI.cs` (~lines 415–460)

- [ ] **Step 1: Tev payment**

```csharp
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B)) Close(0);
```

- [ ] **Step 2: Shuttle computer — B walks the same ladder as Escape**

Directly before the `if (SaveOpen)` block insert:

```csharp
        // Pad B walks exactly the Escape ladder below: dialog → view → menu →
        // closed, one level per press (mirrors the phone). ConsumeEscape() is
        // harmless for B — the pause menu opens on Start, not B.
        bool back = Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B);
```

Then replace, in this section only, every `Input.GetKeyDown(KeyCode.Escape)` with `back`. The five sites:

```csharp
        if (SaveOpen)
        {
            if (back) { ConsumeEscape(); CloseSaveDialog(); }
            return;
        }
        if (PrintOpen)
        {
            if (back) { ConsumeEscape(); ClosePrint(); }
            return;
        }
        if (Time.frameCount > _openedFrame && back)
        {
            if (ProjectsOpen && _shelfPane.activeSelf) { ConsumeEscape(); ShowMenuPane(); return; }
            if (_traxView != null && _traxView.activeSelf) { ConsumeEscape(); _inst.Stop(); ShowProjects(); return; }
            if (ProjectsOpen) { ConsumeEscape(); ShowHomeFromProjects(); return; }
            if (NavOpen) { ConsumeEscape(); ShowHome(); return; }
        }
        if (Time.frameCount > _openedFrame && (back || Input.GetKeyDown(KeyCode.F)))
        {
            if (Input.GetKeyDown(KeyCode.F)) s_consumedFFrame = Time.frameCount;
            if (Input.GetKeyDown(KeyCode.Escape)) ConsumeEscape();
            Close();
            return;
        }
```

(Keep the existing comments between these blocks; only the conditions change. The `if (Input.GetKeyDown(KeyCode.Escape)) ConsumeEscape();` inside the last block stays keyed on Escape on purpose.)

- [ ] **Step 3: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Vendor/TevPaymentUI.cs" "Assets/3 - Scripts/Music/ShuttleComputerUI.cs"
git commit -m "feat(controller): pad B closes Tev payment and walks the shuttle computer's Escape ladder"
```

---

### Task 8: Dialogue option lists — focus owner, stick nav, B cancels

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs`
- Modify: `Assets/3 - Scripts/Story/NpcGraphWalker.cs` (~line 64)
- Modify: `Assets/3 - Scripts/NPC_Dialogue/AuthoredNPCTalk.cs` (~line 293)
- Modify: `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` (`HandleChoice` ~line 330), `Vendor/Alien7Vendor.cs`, `Vendor/ShipMarketNPC.cs`, `NPC_Dialogue/RandomAlienDialogue.cs` (their `HandleChoice`)
- Modify: `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs` (~line 452), `NPC_Dialogue/ShipInstructorDialogue.cs` (~lines 207, 219), `NPC_Dialogue/TevMushroomOnboarding.cs` (~line 1418)
- Modify: `Assets/3 - Scripts/Story/WorldDialogueUI.cs` (`BuildUI` ~line 168)

- [ ] **Step 1: PostGreetingChoicePanel — fields, constants, canvas**

Add after `public bool IsVisible => _visible;`:

```csharp
    /// Passed to onSelect when the player backs out with pad B. Every waiter
    /// treats it like walking away (negative = no pick). Distinct from -1 so
    /// coroutines waiting on "choice != -1" wake up.
    public const int Cancelled = -2;

    readonly List<Button> _rowButtons = new List<Button>();
    bool _cancellable = true;
    int _focusIndex = -1;          // row the pad last had focused; restored if the EventSystem loses it
    int _focusArmFrame;            // pad focus is forced only from this frame on (WorldDialogueUI's one-frame deferral)
```

In `BuildCanvas` change `_canvas.sortingOrder = 900;` to:

```csharp
        _canvas.sortingOrder = 905;   // above toasts/storage (900), below FishStagingUI (910) which stacks on top
```

and, right after `_panelRT.sizeDelta = new Vector2(720f, 200f);`, add:

```csharp
        // Owns pad focus: the navigator never migrates/clears our selection and
        // PadCursor stays off — option lists keep the highlight box (spec §4).
        panel.AddComponent<ControllerFocusOwner>();
```

- [ ] **Step 2: `Show` — signature, nav wiring, initial focus**

Replace the whole `Show` method with:

```csharp
    public void Show(IList<Row> rows, Action<int> onSelect, bool cancellable = true)
    {
        ClearRows();
        _currentRows.Clear();
        for (int i = 0; i < rows.Count; i++) _currentRows.Add(rows[i]);
        _onSelect = onSelect;
        _cancellable = cancellable;
        for (int i = 0; i < rows.Count; i++)
        {
            BuildRow(i, rows[i]);
        }
        WireNavigation();
        _focusIndex = FirstEnabledRow();
        _focusArmFrame = Time.frameCount + 1;   // the A that advanced the greeting must not also Submit row 0
        _panelRT.gameObject.SetActive(true);
        _visible = true;
        _crtT = 0f;                 // CRT turn-on, mirrors PhosphorDialogueBox
        HideSpokenLine();
        // Free the cursor so the player can click rows with the mouse in
        // addition to the 1-9 hotkeys. NPCDialogue locks the cursor again
        // when its typewriter finishes (NPCDialogue.cs:331), so we have to
        // override that on Show AND keep enforcing it in Update — same
        // pattern SpaceDustSellUI uses while open.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    int FirstEnabledRow()
    {
        for (int i = 0; i < _currentRows.Count; i++) if (_currentRows[i].enabled) return i;
        return -1;
    }

    // Explicit up/down between ENABLED rows, wrapping. Unity's automatic mode
    // was skipping rows during the fade-in and could wander to other canvases.
    void WireNavigation()
    {
        var live = new List<Button>();
        for (int i = 0; i < _rowButtons.Count; i++)
            if (_rowButtons[i] != null && _rowButtons[i].interactable) live.Add(_rowButtons[i]);
        for (int i = 0; i < live.Count; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp   = live[(i - 1 + live.Count) % live.Count];
            nav.selectOnDown = live[(i + 1) % live.Count];
            live[i].navigation = nav;
        }
    }

    int IndexOfRow(GameObject go)
    {
        if (go == null) return -1;
        for (int i = 0; i < _rowGOs.Count; i++) if (_rowGOs[i] == go) return i;
        return -1;
    }
```

- [ ] **Step 3: `Hide` / `ClearRows` / `BuildRow` bookkeeping**

In `ClearRows()` add `_rowButtons.Clear();` after `_rowGOs.Clear();`.

In `BuildRow`, after `btn.onClick.AddListener(() => HandleSelect(captured));` add:

```csharp
        _rowButtons.Add(btn);
```

- [ ] **Step 4: `PhosphorRow` paints on stick focus too**

Replace the whole nested `PhosphorRow` class with:

```csharp
    /// <summary>
    /// One choice row's look: staggered fade-in on birth, phosphor light-up
    /// while hovered OR focused (stick/D-pad selection). Owns every visual
    /// state so the Button's tint machinery (which can't touch children)
    /// stays off. Hover also moves the EventSystem selection so keyboard
    /// Enter / pad A act on the row under the mouse.
    /// </summary>
    class PhosphorRow : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        Image _bg, _bar;
        TextMeshProUGUI _pre, _label;
        bool _rowEnabled;
        bool _hover, _selected;
        float _delay, _born;
        CanvasGroup _cg;

        public void Init(Image bg, Image bar, TextMeshProUGUI pre, TextMeshProUGUI label,
                         bool rowEnabled, float delay)
        {
            _bg = bg; _bar = bar; _pre = pre; _label = label;
            _rowEnabled = rowEnabled;
            _delay = delay;
            _born = Time.unscaledTime;
            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
        }

        void Update()
        {
            if (_cg == null) return;
            float t = (Time.unscaledTime - _born - _delay) / 0.22f;
            _cg.alpha = Mathf.Clamp01(t);
            if (t >= 1f) enabled = false;   // settled; nothing left to animate
        }

        void Repaint()
        {
            if (!_rowEnabled) return;
            bool lit = _hover || _selected;
            _bg.color = lit ? PhosphorUI.RowHoverBg : Color.clear;
            _bar.enabled = lit;
            _pre.color = lit ? PhosphorUI.Phosphor : PhosphorUI.Border;
            _label.color = lit ? PhosphorUI.RowHot : PhosphorUI.RowText;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _hover = true;
            Repaint();
            if (_rowEnabled && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnPointerExit(PointerEventData e) { _hover = false; Repaint(); }
        public void OnSelect(BaseEventData e)         { _selected = true;  Repaint(); }
        public void OnDeselect(BaseEventData e)       { _selected = false; Repaint(); }
    }
```

- [ ] **Step 5: `Update` — hold pad focus, B cancels, stop forcing the OS cursor visible**

Replace the tail of `Update` from the comment `// Re-assert cursor unlock every frame while visible` to the end of the method with:

```csharp
        // Re-assert cursor unlock every frame while visible — NPC dialogue
        // scripts can re-lock the cursor when their typewriter completes or
        // their typewriter coroutine ticks, so a one-shot unlock in Show
        // gets clobbered. Cheap to keep enforcing. Visibility is only forced
        // for mouse users: the navigator hides the OS cursor for pad users,
        // and forcing it back on every frame was flipping LastSource to KBM
        // on the slightest mouse jitter (which killed the pad highlight).
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible && TutorialGate.LastSource == TutorialGate.InputSource.KeyboardMouse) Cursor.visible = true;

        // Pad focus. We own it (ControllerFocusOwner on the panel): remember
        // where the stick moved it, and put it back if anything else dropped
        // it (the fade-in makes rows "invalid" to the navigator for 0.2 s).
        if (TutorialGate.ControllerEnabled
            && TutorialGate.LastSource == TutorialGate.InputSource.Controller
            && Time.frameCount >= _focusArmFrame)
        {
            var es = EventSystem.current;
            if (es != null)
            {
                int idx = IndexOfRow(es.currentSelectedGameObject);
                if (idx >= 0) _focusIndex = idx;
                else if (_focusIndex >= 0 && _focusIndex < _rowGOs.Count && _rowGOs[_focusIndex] != null)
                    es.SetSelectedGameObject(_rowGOs[_focusIndex]);
            }
        }

        // Pad B backs out of the conversation — exactly like walking away.
        if (_cancellable && TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            Cancel();
            return;
        }

        for (int i = 0; i < _currentRows.Count && i < 9; i++)
        {
            KeyCode key = (KeyCode)((int)KeyCode.Alpha1 + i);
            if (Input.GetKeyDown(key)) HandleSelect(i);
        }
    }

    /// Back out: hides the panel and reports Cancelled (-2) to the caller.
    public void Cancel()
    {
        if (!_visible) return;
        var cb = _onSelect;
        Hide();
        cb?.Invoke(Cancelled);
    }
```

- [ ] **Step 6: Waiters wake on cancel**

`NpcGraphWalker.ChooseOnSharedPanel` ~line 64:

```csharp
        yield return new WaitUntil(() => box.Value != -1 || (inRange != null && !inRange()));
```

(`Run` already treats `pick2 < 0` as walked away — `Cancelled` is -2, so nothing else changes.)

`AuthoredNPCTalk.Choose` ~line 293:

```csharp
        yield return new WaitUntil(() => _choice != -1 || !InRange);
```

(Its doc comment says "walks away (-1)"; amend to "walks away or backs out with B (negative)". `ShllorbinTalk` compares `LastChoice != 0` / `== 1`, so -2 reads as "not that option", same as -1.)

- [ ] **Step 7: Direct-callback vendors treat cancel as Leave**

At the top of `HandleChoice(int index)` in **each** of `FishMarketNPC.cs`, `Alien7Vendor.cs`, `ShipMarketNPC.cs`, `RandomAlienDialogue.cs` add as the first line:

```csharp
        if (index < 0) { StopConversation(); return; }   // pad B on the option list = Leave
```

(`NPCSellRows.ActionAt` already returns false for negative indices, and every other branch compares against `>= 0` row starts, so this line is belt-and-braces for readability; all four fall through to `StopConversation()` today.)

- [ ] **Step 8: Scripted beats are not cancellable**

Change these `Show` calls to pass `cancellable: false` (walk-away still ends them through their own `_playerInRange` checks):

- `TevDialogue.cs` ~452: `PostGreetingChoicePanel.Instance.Show(rows, i => _forkChoice = i, cancellable: false);`
- `ShipInstructorDialogue.cs` ~207: `PostGreetingChoicePanel.Instance.Show(rows, i => choice = i, cancellable: false);`
- `ShipInstructorDialogue.cs` ~219: `PostGreetingChoicePanel.Instance.Show(confirmRows, i => confirm = i, cancellable: false);`
- `TevMushroomOnboarding.cs` ~1418: `PostGreetingChoicePanel.Instance.Show(list, i => _choice = i, cancellable: false);`

- [ ] **Step 9: WorldDialogueUI keeps the highlight path**

In `WorldDialogueUI.BuildUI`, after `gameObject.AddComponent<GraphicRaycaster>();`:

```csharp
        // Preset story conversations keep the highlight-box pad path (spec §4);
        // PadCursor stays off while this is up.
        gameObject.AddComponent<ControllerFocusOwner>();
```

**Deviation from the spec, on purpose:** no pad-B cancel is added to `WorldDialogueUI`. Its conversations are scripted mission beats (Tev's letter, the ORG interview) whose hosts await a reply and have no walk-away path, so a mid-beat cancel could strand a mission. It already navigates and confirms on pad. Report this in the task summary.

- [ ] **Step 10: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 11: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/PostGreetingChoicePanel.cs" "Assets/3 - Scripts/Story/NpcGraphWalker.cs" "Assets/3 - Scripts/NPC_Dialogue/AuthoredNPCTalk.cs" "Assets/3 - Scripts/Fishing/FishMarketNPC.cs" "Assets/3 - Scripts/Vendor/Alien7Vendor.cs" "Assets/3 - Scripts/Vendor/ShipMarketNPC.cs" "Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs" "Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs" "Assets/3 - Scripts/NPC_Dialogue/ShipInstructorDialogue.cs" "Assets/3 - Scripts/NPC_Dialogue/TevMushroomOnboarding.cs" "Assets/3 - Scripts/Story/WorldDialogueUI.cs"
git commit -m "fix(dialogue): option list owns pad focus — stick/D-pad scroll, rows light on focus, B backs out like walking away"
```

---

### Task 9: NAV hover hand-off on pad

**Files:**
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerNavUI.cs` (prompt ~lines 395–396; `NavInput` ~lines 443–465)
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerUI.cs` (`Update` ~line 471; `Close()` ~line 337; `OnDestroy()` ~line 373)

- [ ] **Step 1: Suppress-token helpers (in `ShuttleComputerNavUI.cs`, the partial class)**

Add near `public bool NavOpen …`:

```csharp
    // While the autopilot hovers and this player is fullscreen on NAV, the pad
    // flies the shuttle: the virtual cursor is suppressed so the left stick
    // positions instead of pointing. Released on any other phase, when NAV or
    // the computer closes, and on destroy — it can never leak.
    const string HoverCursorToken = "nav-hover";
    bool _hoverTokenHeld;

    void SetHoverCursorSuppressed(bool on)
    {
        if (on == _hoverTokenHeld) return;
        _hoverTokenHeld = on;
        if (on) PadCursor.Suppress(HoverCursorToken);
        else    PadCursor.Release(HoverCursorToken);
    }
```

- [ ] **Step 2: Rewrite `NavInput`**

```csharp
    // Hover steering — only while THIS player has the NAV app open fullscreen
    // (the modal flag already keeps these keys away from the player's feet).
    // Pad (Sam's picks, 2026-09-06): left stick = position, LB/RB = yaw
    // (the ship's roll bumpers), A = land. Raw reads on purpose: the cursor
    // mute in TutorialGate would otherwise zero the stick, and no tutorial
    // ability gates the shuttle.
    void NavInput()
    {
        var pilot = ShuttleAutopilot.Instance;
        bool hover = pilot != null && pilot.CurrentPhase == ShuttleAutopilot.Phase.Hover;
        SetHoverCursorSuppressed(hover);
        if (!hover) return;

        // D-3: first NAV user during HOVER owns the stick; everyone else
        // watches the same feed with a "PILOT:" chip instead of the prompt.
        ShuttleSync.TryClaimPilot();
        if (!ShuttleSync.LocalCanSteer) return;

        Vector2 stick = TutorialGate.LeftStickRaw();
        Vector2 move = new Vector2(
            Mathf.Clamp((Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f) + stick.x, -1f, 1f),
            Mathf.Clamp((Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f) + stick.y, -1f, 1f));
        float yaw = ((Input.GetKey(KeyCode.E) || TutorialGate.PadHeld(TutorialGate.PadButton.RB)) ? 1f : 0f)
                  - ((Input.GetKey(KeyCode.Q) || TutorialGate.PadHeld(TutorialGate.PadButton.LB)) ? 1f : 0f);
        pilot.SetPilotInput(move, yaw);

        if (Input.GetKeyDown(KeyCode.Space) || TutorialGate.PadPressed(TutorialGate.PadButton.A))
        {
            if (!pilot.RequestLand())
                _navRedFlashUntil = Time.unscaledTime + 0.8f;   // red flash + "NO CLEAR GROUND"
        }
    }
```

- [ ] **Step 3: Glyph-aware prompt**

Replace the two prompt lines:

```csharp
                else if (pilot.LandingValid) { prompt = "● LANDING ZONE CLEAR · SPACE TO LAND"; promptColor = green; }
                else { prompt = "● NO LANDING ZONE · WASD POSITION · Q/E YAW"; promptColor = red; }
```

with:

```csharp
                // PromptGlyphs picks per input source: keyboard text, or the
                // pad's sprite glyphs (A / left stick / LB / RB).
                else if (pilot.LandingValid) { prompt = "● LANDING ZONE CLEAR · " + PromptGlyphs.Jump + " TO LAND"; promptColor = green; }
                else { prompt = "● NO LANDING ZONE · " + PromptGlyphs.Move + " POSITION · " + PromptGlyphs.RollLeft + "/" + PromptGlyphs.RollRight + " YAW"; promptColor = red; }
```

(Keyboard now reads "**Space** TO LAND" / "**WASD** POSITION · **Q**/**E** YAW" in bold — `PromptGlyphs` wraps keys in `<b>`; `_navHoverPrompt` is TMP with rich text.)

- [ ] **Step 4: Release the token when NAV isn't running**

`ShuttleComputerUI.cs` ~line 471 change:

```csharp
        if (NavOpen) NavInput();
```

to:

```csharp
        if (NavOpen) NavInput();
        else SetHoverCursorSuppressed(false);
```

Add `SetHoverCursorSuppressed(false);` as the **first statement** of `public void Close()` (~line 337) and of `void OnDestroy()` (~line 373).

- [ ] **Step 5: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Music/ShuttleComputerNavUI.cs" "Assets/3 - Scripts/Music/ShuttleComputerUI.cs"
git commit -m "feat(nav): pad flies the hover hand-off — left stick position, LB/RB yaw, A lands; cursor suppressed; glyph prompts"
```

---

### Task 10: Phone tablet — cursor fenced to the screen

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (`AnimatePhone` open branch ~line 1668; close branch ~line 1766; `ForceCloseNoAnim` ~line 338)

- [ ] **Step 1: Set bounds on open**

In the `if (toOpen)` branch of `AnimatePhone`, after:

```csharp
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
```

add:

```csharp
            // Virtual cursor stays on the tablet: it can't wander off into the
            // dark part of the screen where nothing is clickable.
            PadCursor.SetBounds(_screenRT);
```

- [ ] **Step 2: Clear bounds on close**

In the `if (!toOpen)` branch, after:

```csharp
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
```

add:

```csharp
            PadCursor.ClearBounds();
```

And in `ForceCloseNoAnim()`, as its first statement:

```csharp
        PadCursor.ClearBounds();
```

- [ ] **Step 3: Static verification of camera mode on pad (no code change expected)**

Confirm by reading, and record in the task report, that in camera mode:
1. `PlayerPhoneUI` re-locks the OS cursor every frame (~line 1433) → `PadCursor.Wanted()` is false → ring hidden, sticks reach gameplay.
2. `Update` returns before the movement-close block while `IsCameraMode` (~line 1485 `return; // skip the rest of Update while in camera mode`) → walking is allowed.
3. `LookBlocked` is false in camera mode and the navigator clears selection → `PlayerController` look gate (`uiHasFocus`, `phoneBlocksLook`) is open → right stick looks.
4. RT (`TutorialGate.FirePressed`) shoots, LT toggles photo/video, pad B → `ExitCameraMode()`.

If any of the four does not hold, fix it in this task and describe the fix.

- [ ] **Step 4: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "feat(phone): virtual cursor fenced to the tablet screen"
```

---

### Task 11: Solar map — Y toggles cursor mode

**Files:**
- Modify: `Assets/3 - Scripts/Map/SolarMap.cs` (~lines 180–200)
- Modify: `Assets/3 - Scripts/Map/SolarMapOverlay.cs` (`SetCursorHint`)
- Modify: `Assets/3 - Scripts/Map/MapCameraRig.cs` (~lines 76–90)

- [ ] **Step 1: Y toggles, shimmed click and zoom**

In `SolarMap.Update` change:

```csharp
        if (Input.GetKeyDown(cursorLockKey)) SetCursorLocked(!_cursorLocked);
```

to:

```csharp
        // G (keyboard) or Y (pad, Sam's pick) flips cursor mode. Unlocked → the
        // OS cursor is free → PadCursor appears and the left stick moves it.
        if (Input.GetKeyDown(cursorLockKey) || TutorialGate.PadPressed(TutorialGate.PadButton.Y)) SetCursorLocked(!_cursorLocked);
```

Change the click read:

```csharp
        if (Input.GetMouseButtonDown(0) && !_cursorLocked && _cam != null)
        {
            var es = EventSystem.current;
            if (es == null || !es.IsPointerOverGameObject()) { _pendingClick = true; _pendingClickPos = Input.mousePosition; }
        }
```

to:

```csharp
        if (PadCursor.PrimaryDown && !_cursorLocked && _cam != null)
        {
            var es = EventSystem.current;
            if (es == null || !es.IsPointerOverGameObject()) { _pendingClick = true; _pendingClickPos = PadCursor.PointerPosition; }
        }
```

Change the zoom read:

```csharp
        float scroll = Input.mouseScrollDelta.y;
```

to:

```csharp
        float scroll = PadCursor.ScrollDelta;   // wheel notches, or right-stick Y while the cursor is up
```

- [ ] **Step 2: Pad-aware hint**

Replace `SetCursorHint` in `SolarMapOverlay.cs`:

```csharp
    public void SetCursorHint(bool locked)
    {
        if (_cursorHint == null) return;
        // Legacy UI Text here — no TMP sprite glyphs, so the key is spelled out.
        bool pad = TutorialGate.LastSource == TutorialGate.InputSource.Controller;
        string key = pad ? (TutorialGate.IsPlayStation ? "Triangle" : "Y") : "G";
        if (pad)
            _cursorHint.text = locked ? key + "  ·  cursor (left stick moves it)" : key + "  ·  back to stick flight";
        else
            _cursorHint.text = locked ? "G  ·  unlock cursor (mouse look on)" : "G  ·  lock cursor for mouse look";
    }
```

- [ ] **Step 3: Map flight ignores LT / L3 while the cursor is up**

In `MapCameraRig.Tick` change:

```csharp
        bool down = Input.GetKey(KeyCode.LeftControl) ||
                    (TutorialGate.ControllerEnabled && TutorialGate.LTValue() > TutorialGate.TriggerThreshold);
```

to:

```csharp
        bool down = Input.GetKey(KeyCode.LeftControl) ||
                    (TutorialGate.ControllerEnabled && !PadCursor.IsActive && TutorialGate.LTValue() > TutorialGate.TriggerThreshold);
```

and

```csharp
            bool sprint = Input.GetKey(KeyCode.LeftShift) ||
                          TutorialGate.PadHeld(TutorialGate.PadButton.L3);
```

to:

```csharp
            bool sprint = Input.GetKey(KeyCode.LeftShift) ||
                          (!PadCursor.IsActive && TutorialGate.PadHeld(TutorialGate.PadButton.L3));
```

(`up` reads `PadHeld(A)`, the move axes read `MoveAxis*`, and look reads `RightStickX/Y` — all already muted centrally by Task 4.)

- [ ] **Step 4: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Map/SolarMap.cs" "Assets/3 - Scripts/Map/SolarMapOverlay.cs" "Assets/3 - Scripts/Map/MapCameraRig.cs"
git commit -m "feat(map): Y toggles virtual-cursor mode; cursor click/zoom shims; flight reads muted while the cursor is up"
```

---

### Task 12: Stop re-adding the legacy input module

**Files:**
- Modify: `Assets/3 - Scripts/Building/BuildMenuUI.cs` (~line 322)
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerUI.cs` (~line 571)
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` (~lines 130–137)
- Modify: `Assets/3 - Scripts/Tutorial/TutorialPerformanceReview.cs` (~lines 184–190)

- [ ] **Step 1: BuildMenuUI**

```csharp
    // The navigator's version installs the Input System module (the one that
    // sees PadCursor's virtual mouse) and evicts any legacy StandaloneInputModule.
    void EnsureEventSystem() => ControllerUINavigator.EnsureEventSystem();
```

- [ ] **Step 2: ShuttleComputerUI**

```csharp
    static void EnsureEventSystem() => ControllerUINavigator.EnsureEventSystem();
```

- [ ] **Step 3: MainMenuController**

Replace:

```csharp
                if (es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>() == null)
                    es.gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
```

with:

```csharp
                // Re-ensure the INPUT SYSTEM module (never the legacy one — a
                // StandaloneInputModule beside it can win in a build and it
                // cannot see PadCursor's virtual mouse).
                ControllerUINavigator.EnsureEventSystem();
```

- [ ] **Step 4: TutorialPerformanceReview**

Replace:

```csharp
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var es = new GameObject("ReviewEventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            DontDestroyOnLoad(es);
        }
```

with:

```csharp
        ControllerUINavigator.EnsureEventSystem();
```

- [ ] **Step 5: Confirm nothing else adds a legacy module**

Run: `grep -rn "AddComponent<.*StandaloneInputModule>" "Assets/3 - Scripts"`
Expected: no output.

- [ ] **Step 6: Compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS`, no `warning CS` lines.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Building/BuildMenuUI.cs" "Assets/3 - Scripts/Music/ShuttleComputerUI.cs" "Assets/3 - Scripts/UI/MainMenuController.cs" "Assets/3 - Scripts/Tutorial/TutorialPerformanceReview.cs"
git commit -m "fix(input): all EventSystem spawns go through ControllerUINavigator.EnsureEventSystem (no stray StandaloneInputModule)"
```

---

### Task 13: Verification, playtest checklist, docs, memory

**Files:**
- Create: `docs/PLAYTEST_CONTROLLER_CURSOR.md`
- Modify: `docs/CURRENT_STATE_AUDIT.md` (§31 controller — dated addendum)

- [ ] **Step 1: Full compile check**

Run: `py -3 prototypes/shuttle-computer/test/compile-unity.py`
Expected: `compile check: PASS` and `grep -c "warning CS"` on the output = 0.

- [ ] **Step 2: Static trace of pad reads that reach gameplay while the cursor is active**

Run and read every hit:

```
grep -rn "PadPressed(\|PadHeld(\|PadReleased(\|RightStickX\|RightStickY\|MoveAxis\|LookDelta\|LTValue\|RTValue\|LeftStickRaw\|leftStick\|rightStick\|buttonSouth" "Assets/3 - Scripts" --include=*.cs | grep -v "Tutorial/TutorialGate.cs\|UI/PadCursor.cs"
```

For each hit decide: muted by Task 4 (A/X/left stick/right stick), a non-pointer button (B/Y/bumpers/triggers/Start/Back — acceptable), or a deliberate raw read (`ShuttleComputerNavUI.NavInput` only). Write the resulting table into the task report. Any A/X/stick read that bypasses `TutorialGate` and could fire under a cursor screen is a bug: gate it with `!PadCursor.IsActive` and note it.

- [ ] **Step 3: Write Sam's checklist**

Create `docs/PLAYTEST_CONTROLLER_CURSOR.md` with exactly this content:

```markdown
# Controller pass 2 — one-pass playtest (pad only)

Plug in the pad before launching. Expected behaviour per line; tick or note what differed.

## Cursor basics (main menu is the first place you see it)
- [ ] Main menu: touch the left stick → a thin white ring appears mid-screen. Move it over PLAY → button highlights. Press A → ring shrinks to a dot and pops back; PLAY fires.
- [ ] Nudge the mouse → ring disappears, Windows cursor shows. Touch the stick → ring is back at screen centre.
- [ ] Pause menu (Start): ring works on every tab; drag a slider with A held; CONTROLLER tab has "CURSOR SPEED (PAD)". B closes.
- [ ] No yellow highlight box anywhere except NPC option lists.

## Gameplay isolation
- [ ] With the pause menu / any vendor open, moving the left stick does NOT walk; right stick does NOT look; A does NOT jump.
- [ ] Hold a sapling: A places it, you do NOT jump. When the last sapling is placed, A jumps again. Same for a building ghost.

## NPC dialogue (highlight box stays here)
- [ ] Talk to any NPC with options: first option lights up immediately; stick or D-pad up/down moves it and wraps; A picks.
- [ ] B on the option list ends the talk (like walking away). Goods vendor / fish vendor / guitar shop all do this.
- [ ] Tev's mission fork and the ship instructor's options: B does nothing there (scripted beats), stick + A work.

## Vendors and storage (cursor)
- [ ] Goods vendor → Open shop: cursor buys; B closes. Fish vendor → Sell fish: cursor picks fish; right stick scrolls the list; B closes. Ship vendor, Tev shop (LB/RB still page), Tev payment: B closes.
- [ ] Storage locker: cursor moves items (hold A + stick drags); B closes.

## Shuttle computer / NAV / TRAX
- [ ] X opens the computer. Cursor clicks apps. B backs out one level each press (dialog → app → home → closed).
- [ ] NAV: cursor picks a planet → autopilot flies. At the 100 m hover the ring vanishes, prompt shows pad glyphs; left stick positions, LB/RB yaw, A lands. After touchdown the ring is back.
- [ ] TRAX: buttons/grid click; a knob turns with A held + stick, and with right stick; print dialog closes on B.

## Phone
- [ ] D-pad up opens the tablet. Ring is fenced to the screen; stick moving it never closes the phone. A opens apps, B backs out then closes.
- [ ] Camera app: ring gone, you can walk and look, RT shoots, LT toggles photo/video, B back to the phone.

## Solar map
- [ ] Back/View opens the map locked: sticks fly/look. Y → ring appears, legend hint says so; stick moves it, right stick zooms, A clicks a planet / legend row. Y again → locked. B closes from either mode.

## Save / load, galleries, misc
- [ ] Save/load slots, photo gallery, newspaper, note reader: cursor works where a mouse would; B closes.

## Anything wrong? Note the screen + what you pressed. The cursor's activation rule is "OS cursor unlocked + pad last touched", so a screen with NO ring almost certainly forgot to unlock the OS cursor on open.
```

- [ ] **Step 4: Audit addendum**

Append to §31 of `docs/CURRENT_STATE_AUDIT.md` (find the controller section header with `grep -n "31" docs/CURRENT_STATE_AUDIT.md`):

```markdown
**2026-09-06 addendum — controller pass 2 (virtual cursor).** `UI/PadCursor.cs`
(component on the `[ControllerUINavigator]` object, created in its Awake) wraps
Input System `VirtualMouseInput`: a virtual Mouse device driven by left stick /
A (LMB) / X (RMB) / right stick (scroll) that `InputSystemUIInputModule` treats
as a real pointer, so every UGUI screen is pad-usable with no per-screen code.
Activation rule (`PadCursor.Wanted`): pad enabled ∧ `Cursor.lockState == None`
∧ `LastSource == Controller` ∧ no `ControllerFocusOwner` active ∧ no suppress
token. While active: `TutorialGate` mutes A/X (`MutedByCursor`), left stick
(`MoveStick`), right stick (`RightStickX/Y`, `LookDelta`), `HotbarCycleStep`,
and `MovementInputSuppressed` is true; the navigator clears selection and draws
no border; `EventSystem.sendNavigationEvents` is off. Legacy readers use
`PadCursor.PointerPosition / PrimaryDown / PrimaryHeld / ScrollDelta`.
`ControllerFocusOwner` (on `PostGreetingChoicePanel`'s panel and
`WorldDialogueUI`) keeps dialogue option lists on the highlight-box path and
stops the navigator from touching their selection. `PadCursor.Suppress/Release`
tokens: `"nav-hover"` (ShuttleComputerNavUI while the autopilot hovers — left
stick flies, LB/RB yaw, A lands). Map: Y toggles `SetCursorLocked`. Speed:
`InputSettings.padCursorSpeed` → `PadCursor.SpeedMultiplier` (pause menu
CONTROLLER tab). Pad A never jumps while `GhostPlacement.IsPlacing`. All
EventSystem spawns go through `ControllerUINavigator.EnsureEventSystem` (public).
Spec: `docs/superpowers/specs/2026-09-06-controller-virtual-cursor-design.md`.
Playtest: `docs/PLAYTEST_CONTROLLER_CURSOR.md`.
```

- [ ] **Step 5: Commit**

```bash
git add docs/PLAYTEST_CONTROLLER_CURSOR.md docs/CURRENT_STATE_AUDIT.md
git commit -m "docs(controller): playtest checklist + audit addendum for the virtual cursor pass"
```

- [ ] **Step 6: Memory note** (session-level, not a repo file) — update
`controller-revamp.md` in the memory directory with: built 2026-09-06, playtest
pending, the activation rule, the focus-owner and suppress-token traps, the
WorldDialogueUI no-cancel deviation, and the MainMenu additive-load
EventSystem trap now routed through `EnsureEventSystem`.
