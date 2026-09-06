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
        if (_canvas != null) Destroy(_canvas.gameObject);   // root object — not torn down with us automatically
    }

    void BuildGraphic()
    {
        // Own overlay canvas, one above the navigator's border canvas (32000).
        // ConstantPixelSize so anchoredPosition == screen pixels, which is what
        // VirtualMouseInput reads/writes. NO GraphicRaycaster — the cursor
        // must never be something the cursor can hit.
        // ROOT object on purpose. The navigator's own GameObject already holds a
        // Canvas (the yellow border), so a child canvas here would be NESTED:
        // it inherits the parent rect (a 100×100 box at screen centre) instead
        // of the screen, and anchoredPosition — which VirtualMouseInput writes
        // in screen pixels — lands offset by half a screen. Symptom Sam hit:
        // ring confined to the top-right quadrant, clicks landing elsewhere.
        var canvasGO = new GameObject("PadCursorCanvas", typeof(RectTransform));
        DontDestroyOnLoad(canvasGO);
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
        _cursorRT.localScale = Vector3.one * (screenScale * RingDiameterPx / 48f);   // 48 = ring outer diameter in the 64 px sprite

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

    // 64×64: a 4 px white ring of radius 22 with a 1.5 px 50 % dark halo on
    // both sides so it reads on light AND dark backgrounds. Drawn oversized
    // and scaled down by _cursorRT.localScale for smooth edges.
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
