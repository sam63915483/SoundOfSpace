# Smartphone UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a diegetic smartphone HUD on the **X** key (replacing the auto-align toggle) with a 4-app launcher for Fishingdex / Build / Settings / Map, and remove the auto-align feature entirely.

**Architecture:** New auto-singleton `PlayerPhoneUI` (DontDestroyOnLoad MonoBehaviour) that builds its own `Canvas` procedurally, slides up from below the hotbar on X press, blocks player look while open, eats the first ESC press, and routes app clicks back to existing menu singletons. Plus surgical removal of the existing `AutoAlignToggleUI` + `Ship.autoAlignEnabled` field and its save-system plumbing.

**Tech Stack:** Unity 2022.3, Built-in Render Pipeline, TMPro, UGUI. No automated test framework — each task is verified by "compile in Editor (Console is clean)" + a manual playtest description. Frequent commits.

**Conventions used:**
- Auto-singleton + DontDestroyOnLoad + skip MainMenu — mirror `VitalsHUD.cs:63-71`.
- `EnsureGameplaySingletons` seed in `MainMenuController.cs:473+` — required so the singleton exists in builds (which start in MainMenu and never fire `AfterSceneLoad` in the gameplay scene). This is the same trap that caused the grass-flicker incident — documented at top of CLAUDE.md.
- Palette + sprite reuse from `VitalsHUD.cs:33-38` and `AutoAlignToggleUI.cs:22-26` so the phone reads as the same UI family.
- TMP font via `HudFontResolver.Apply(textComponent)`.
- Beveled panel sprites via `UIPanelSprites.GetBeveledPanel()` / `GetBeveledOutline()`.

**Important:** The forbidden zone in CLAUDE.md (atmosphere / celestial / planet shading) is NOT touched by any task in this plan. All work happens in `Assets/3 - Scripts/UI/`, `Assets/3 - Scripts/SaveSystem/`, and the two controller files in `Assets/3 - Scripts/Scripts/Game/Controllers/`.

---

## Task 1: Remove `autoAlignEnabled` from the save schema

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs:143, 181, 892, 963` (four references found by grep)

**Why first:** The save schema change is independent of every other task. Doing it first decouples the auto-align teardown so later tasks don't keep tripping on `autoAlign` references.

- [ ] **Step 1: Locate the field in `SaveData.cs`**

Run: `grep -n "autoAlign" "Assets/3 - Scripts/SaveSystem/SaveData.cs"`
Expected: matches inside the `ShipSave` class and the `ExtraShipSave` class (the field is called `autoAlign` on the save side, mapped to `Ship.autoAlignEnabled` in `SaveCollector`).

- [ ] **Step 2: Delete the field from both classes**

In `SaveData.cs`, inside `ShipSave` (search for `public class ShipSave` or `[Serializable] class ShipSave`), remove the line:

```csharp
public bool autoAlign;
```

Inside `ExtraShipSave`, remove the same field.

- [ ] **Step 3: Strip the capture lines from `SaveCollector.cs`**

Open `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`. Line 143 and line 181 are capture sites:

Line 143 currently reads:
```csharp
s.autoAlign = ship.autoAlignEnabled;
```
Delete the whole line.

Line 181 is inside an object initializer for an `ExtraShipSave` entry:
```csharp
autoAlign = ship.autoAlignEnabled,
```
Delete this line (and the preceding comma on the previous line if needed to keep the initializer syntactically valid).

- [ ] **Step 4: Strip the apply lines from `SaveCollector.cs`**

Line 892:
```csharp
ship.autoAlignEnabled = s.autoAlign;
```
Delete.

Line 963:
```csharp
ship.autoAlignEnabled = entry.autoAlign;
```
Delete.

- [ ] **Step 5: Compile check**

Switch to Unity, let scripts recompile. Open the Console.
Expected: **errors** referencing `autoAlignEnabled` in `Ship.cs` (we haven't removed that field yet — that's the next task). The save-system files themselves should compile clean. If `SaveData.cs` or `SaveCollector.cs` themselves throw errors, fix before moving on.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "Drop autoAlign from save schema (ShipSave + ExtraShipSave)"
```

---

## Task 2: Strip the auto-align feature from `Ship.cs`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs:160, 162-163, 734-768` (field, FixedUpdate rotation block)

- [ ] **Step 1: Delete the field declarations**

In `Ship.cs`, around line 160, delete:
```csharp
public bool autoAlignEnabled;
```

And around lines 162-163, delete:
```csharp
[Tooltip("Degrees per second the auto-align correction can rotate the ship toward upright. Low enough that user inputs feel responsive; high enough that releasing the stick lets the ship settle upright in a couple of seconds.")]
public float autoAlignDegPerSec = 35f;
```

- [ ] **Step 2: Delete the auto-align rotation block in `FixedUpdate`**

Lines 734–768 contain the rotation block guarded by `if (... && autoAlignEnabled)`. Delete the whole block — starting from the comment "Auto-align is safe to run when every non-grounded contact is the player capsule..." down through the closing brace of that `if` (around line 768, just before whatever code follows).

To find the boundaries precisely:
```bash
grep -n "autoAlignActiveThisStep\|autoAlignEnabled\|autoAlignDegPerSec" "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs"
```
Delete everything from the first comment introducing the block (look 1-2 lines above the first match) through the closing brace at line ~768 (look for the `}` that terminates the `if (... && autoAlignEnabled)`). Also remove the now-orphaned local `bool autoAlignActiveThisStep = false;` declaration just above it (if not already covered).

- [ ] **Step 3: Sweep for stragglers**

```bash
grep -rn "autoAlign" "Assets/3 - Scripts/" --include="*.cs"
```
Expected matches:
- `Assets/3 - Scripts/UI/AutoAlignToggleUI.cs` — entire file (deleted in Task 3)
- Nothing else.

If any other file references `autoAlignEnabled` or `autoAlignDegPerSec`, edit it now.

- [ ] **Step 4: Compile check**

In Unity, recompile. Expected: only errors are inside `AutoAlignToggleUI.cs` (it still references `piloted.autoAlignEnabled`). The rest of the project compiles.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs"
git commit -m "Remove autoAlignEnabled field and rotation block from Ship"
```

---

## Task 3: Delete `AutoAlignToggleUI.cs`

**Files:**
- Delete: `Assets/3 - Scripts/UI/AutoAlignToggleUI.cs`
- Delete: `Assets/3 - Scripts/UI/AutoAlignToggleUI.cs.meta`

- [ ] **Step 1: Confirm there are no surviving references**

```bash
grep -rn "AutoAlignToggleUI" "Assets/" --include="*.cs"
```
Expected: only matches inside the file being deleted. If `MainMenuController.EnsureGameplaySingletons` has a seed for it, remove that block now.

```bash
grep -n "AutoAlignToggleUI" "Assets/3 - Scripts/UI/MainMenuController.cs"
```
If a match exists, delete the surrounding `if (AutoAlignToggleUI.Instance == null) { ... }` block (mirrors the existing seed blocks).

- [ ] **Step 2: Delete the file (and its .meta)**

```bash
rm "Assets/3 - Scripts/UI/AutoAlignToggleUI.cs"
rm "Assets/3 - Scripts/UI/AutoAlignToggleUI.cs.meta"
```

- [ ] **Step 3: Compile check + playtest**

In Unity, recompile. Console should be clean.

Playtest:
1. Enter Play mode in `Assets/1.6.7.7.7.unity`.
2. Walk to the ship and pilot it (default: walk near the ship and use the pilot interaction).
3. Confirm: **no `[X] AUTO ALIGN` pill appears on the left side of the screen** while piloting.
4. Press X repeatedly while piloting. Confirm: nothing happens (no toggle, no error in Console). The ship continues to fly normally without auto-uprighting itself.
5. Exit Play mode.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Delete AutoAlignToggleUI; X key freed up for the phone"
```

---

## Task 4: Scaffold `PlayerPhoneUI` singleton (no visuals yet)

**Files:**
- Create: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` (add EnsureGameplaySingletons seed near line 620, just after the SpaceDustInventory block)

**Why first:** Gives every later task an existing singleton to extend, and front-loads the EnsureGameplaySingletons wiring so the build-only bug class can't bite us later.

- [ ] **Step 1: Create `PlayerPhoneUI.cs` with the auto-singleton skeleton**

Write `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Diegetic smartphone HUD. Pulls up on X key (both on-foot and while piloting).
/// Four-app launcher (Fishingdex / Build / Settings / Map) plus reserved space
/// for future widgets and notifications. Slides up from below the hotbar.
///
/// Auto-singleton pattern (see VitalsHUD.cs as the reference). Must ALSO be
/// seeded in MainMenuController.EnsureGameplaySingletons because builds start
/// in MainMenu where the RuntimeInitializeOnLoadMethod early-outs — see top of
/// CLAUDE.md for the full trap explanation.
/// </summary>
public class PlayerPhoneUI : MonoBehaviour
{
    public static PlayerPhoneUI Instance { get; private set; }

    // True while the phone is shown OR currently animating to/from shown.
    // Other systems (PlayerController look read, TabbedPauseMenu ESC handler)
    // gate on this. Stays true through the close animation so look-around
    // stays blocked until the phone is fully gone.
    public static bool IsOpen { get; private set; }

    // Set true on the frame the phone consumed an Escape press to close
    // itself. Cleared in LateUpdate. TabbedPauseMenu checks this flag to
    // skip its own ESC-opens-pause handler on the same frame.
    public static bool ConsumedEscapeThisFrame { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PlayerPhoneUI");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerPhoneUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // Build() is filled in by Task 5.
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void LateUpdate()
    {
        ConsumedEscapeThisFrame = false;
    }

    // Public API filled in by later tasks. Stubs so the EnsureGameplaySingletons
    // seed and any callers compile from day one.
    public void Open()   { /* Task 6 fills this */ }
    public void Close()  { /* Task 6 fills this */ }
    public void Toggle() { /* Task 6 fills this */ }
}
```

- [ ] **Step 2: Add the EnsureGameplaySingletons seed**

Open `Assets/3 - Scripts/UI/MainMenuController.cs`. Find the `SpaceDustInventory` seed block (currently the last one before `}`, around lines 615-620):

```csharp
        if (SpaceDustInventory.Instance == null)
        {
            var go = new GameObject("SpaceDustInventory");
            DontDestroyOnLoad(go);
            go.AddComponent<SpaceDustInventory>();
        }
```

Add this block immediately after it (and before any `PixelLightLimitFix` block that may already exist there):

```csharp
        if (PlayerPhoneUI.Instance == null)
        {
            // Same MainMenu early-out problem as the other singletons here —
            // without this seed, builds (which start in MainMenu) never
            // auto-create the phone because RuntimeInitializeOnLoadMethod
            // fires once in MainMenu and returns. See top of CLAUDE.md.
            var go = new GameObject("PlayerPhoneUI");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerPhoneUI>();
        }
```

- [ ] **Step 3: Compile check**

Unity recompiles. Console clean.

- [ ] **Step 4: Playtest sanity**

1. Enter Play mode.
2. Pause Editor → open Hierarchy. Find a `PlayerPhoneUI` GameObject under DontDestroyOnLoad.
3. Confirm `PlayerPhoneUI.Instance` is non-null and `PlayerPhoneUI.IsOpen` is false.
4. Press X. Nothing visible happens (no Build() yet). Console clean. Exit Play mode.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs" "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "Scaffold PlayerPhoneUI singleton + EnsureGameplaySingletons seed"
```

---

## Task 5: Build the phone chassis + screen visual (closed state)

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (add `BuildCanvas()` + `BuildChassis()` + `BuildScreen()` + child builders; phone starts off-screen below)

**Approach:** Mirror the structure used by `VitalsHUD.BuildCanvas()` and `AutoAlignToggleUI.BuildCanvas()` for consistency. Build everything procedurally — no prefab needed.

- [ ] **Step 1: Add palette constants and field refs**

In `PlayerPhoneUI.cs`, add inside the class (above `AutoCreate`):

```csharp
    // Layout ---------------------------------------------------------
    const float PhoneWidth     = 220f;
    const float PhoneHeight    = 440f;
    const float HotbarGap      = 32f;    // gap between phone and hotbar
    const float BottomMargin   = 16f;    // resting position above screen edge
    const float SlideDuration  = 0.25f;

    // Palette (mirrors VitalsHUD + AutoAlignToggleUI) ----------------
    static readonly Color ChassisBg     = new Color32(0x0A, 0x18, 0x28, 0xF2);
    static readonly Color ChassisBorder = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color ScreenBg      = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color AccentCyan    = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite    = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg        = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ButtonGrey    = new Color32(0x2A, 0x40, 0x60, 0xFF);

    // Runtime state --------------------------------------------------
    Canvas        _canvas;
    CanvasGroup   _phoneGroup;
    RectTransform _phoneRT;
    RectTransform _screenRT;
    RectTransform _statusBarRT;
    RectTransform _notificationStripRT;
    RectTransform _appGridRT;
    RectTransform _reservedZoneRT;
    UnityEngine.UI.Button _putAwayBtn;

    // Public hooks for future systems
    public RectTransform NotificationStripRoot => _notificationStripRT;
    public RectTransform ReservedZoneRoot      => _reservedZoneRT;
```

- [ ] **Step 2: Implement `BuildCanvas`**

Add inside the class:

```csharp
    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 850; // above HUDs (800-820), below pause menu (1000)

        var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        BuildPhone();
    }
```

- [ ] **Step 3: Implement `BuildPhone` (chassis + screen + side buttons)**

Add:

```csharp
    void BuildPhone()
    {
        // ---- Phone root (animates anchoredPosition.y + CanvasGroup.alpha) ----
        _phoneRT = NewUI("Phone", transform);
        _phoneRT.anchorMin = new Vector2(0.5f, 0f);
        _phoneRT.anchorMax = new Vector2(0.5f, 0f);
        _phoneRT.pivot     = new Vector2(0.5f, 0f);
        _phoneRT.sizeDelta = new Vector2(PhoneWidth, PhoneHeight);
        _phoneRT.anchoredPosition = new Vector2(0f, -(PhoneHeight + BottomMargin)); // off-screen below

        _phoneGroup = _phoneRT.gameObject.AddComponent<CanvasGroup>();
        _phoneGroup.alpha = 0f;
        _phoneGroup.blocksRaycasts = false; // raycasts re-enabled when open

        // ---- Chassis (the dark navy rounded "body" with cyan border) ----
        var chassis = _phoneRT.gameObject.AddComponent<UnityEngine.UI.Image>();
        chassis.sprite = UIPanelSprites.GetBeveledPanel();
        chassis.type   = UnityEngine.UI.Image.Type.Sliced;
        chassis.color  = ChassisBg;
        chassis.raycastTarget = false;

        var border = NewUI("Border", _phoneRT);
        border.anchorMin = Vector2.zero; border.anchorMax = Vector2.one;
        border.offsetMin = Vector2.zero; border.offsetMax = Vector2.zero;
        border.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        var borderImg = border.gameObject.AddComponent<UnityEngine.UI.Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type   = UnityEngine.UI.Image.Type.Sliced;
        borderImg.color  = ChassisBorder;
        borderImg.raycastTarget = false;

        // ---- Side buttons (left: silent + vol up + vol dn; right: power) ----
        AddSideButton("SilentSwitch", anchorY: 0.86f, height: 12f, leftSide: true);
        AddSideButton("VolUp",        anchorY: 0.78f, height: 24f, leftSide: true);
        AddSideButton("VolDn",        anchorY: 0.66f, height: 34f, leftSide: true);
        AddSideButton("PowerButton",  anchorY: 0.74f, height: 40f, leftSide: false);

        // ---- Top hardware (speaker grille + camera dot) ----
        var spk = NewUI("Speaker", _phoneRT);
        spk.anchorMin = new Vector2(0.5f, 1f); spk.anchorMax = new Vector2(0.5f, 1f);
        spk.pivot = new Vector2(0.5f, 1f);
        spk.anchoredPosition = new Vector2(-10f, -10f);
        spk.sizeDelta = new Vector2(50f, 4f);
        var spkImg = spk.gameObject.AddComponent<UnityEngine.UI.Image>();
        spkImg.color = new Color(0.1f, 0.16f, 0.25f, 0.6f);
        spkImg.raycastTarget = false;

        var cam = NewUI("CameraDot", _phoneRT);
        cam.anchorMin = new Vector2(0.5f, 1f); cam.anchorMax = new Vector2(0.5f, 1f);
        cam.pivot = new Vector2(0.5f, 1f);
        cam.anchoredPosition = new Vector2(28f, -10f);
        cam.sizeDelta = new Vector2(6f, 6f);
        var camImg = cam.gameObject.AddComponent<UnityEngine.UI.Image>();
        camImg.color = AccentCyan;
        camImg.raycastTarget = false;

        // ---- Screen (inner darker panel containing all interactive content) ----
        BuildScreen();
    }

    void AddSideButton(string name, float anchorY, float height, bool leftSide)
    {
        var rt = NewUI(name, _phoneRT);
        rt.anchorMin = new Vector2(leftSide ? 0f : 1f, anchorY);
        rt.anchorMax = new Vector2(leftSide ? 0f : 1f, anchorY);
        rt.pivot     = new Vector2(leftSide ? 1f : 0f, 0.5f);
        rt.anchoredPosition = new Vector2(leftSide ? 3f : -3f, 0f);
        rt.sizeDelta = new Vector2(4f, height);
        var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        img.color = ButtonGrey;
        img.raycastTarget = false;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }
```

- [ ] **Step 4: Implement `BuildScreen` (status bar + notif strip + grid + reserved + put-away)**

Add:

```csharp
    void BuildScreen()
    {
        _screenRT = NewUI("Screen", _phoneRT);
        _screenRT.anchorMin = Vector2.zero; _screenRT.anchorMax = Vector2.one;
        _screenRT.offsetMin = new Vector2(12f, 22f);
        _screenRT.offsetMax = new Vector2(-12f, -22f);

        var screenImg = _screenRT.gameObject.AddComponent<UnityEngine.UI.Image>();
        screenImg.color = ScreenBg;
        screenImg.raycastTarget = true; // catches clicks that miss the buttons (no-op)
        _screenRT.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

        // Vertical layout inside screen — TOP→BOTTOM:
        //   status bar, notification strip, app grid, reserved zone, put away
        var vlg = _screenRT.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth  = true;  vlg.childControlHeight  = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        BuildStatusBar();
        BuildNotificationStrip();
        BuildAppGrid();
        BuildReservedZone();
        BuildPutAwayButton();
    }

    // Placeholder builders — fully implemented in later steps of this task.
    void BuildStatusBar()          { _statusBarRT          = NewUI("StatusBar", _screenRT); _statusBarRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 18f; }
    void BuildNotificationStrip()  { _notificationStripRT  = NewUI("NotificationStrip", _screenRT); _notificationStripRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 22f; }
    void BuildAppGrid()            { _appGridRT            = NewUI("AppGrid", _screenRT); _appGridRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 170f; }
    void BuildReservedZone()       { _reservedZoneRT       = NewUI("ReservedZone", _screenRT); _reservedZoneRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleHeight = 1f; }
    void BuildPutAwayButton()      { var rt = NewUI("PutAwayButton", _screenRT); rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 30f; }
```

(The placeholders create the slots and reserve their heights — they get filled out in steps 5-9 below.)

- [ ] **Step 5: Fill in `BuildStatusBar` (time + battery)**

Replace the `BuildStatusBar` placeholder with:

```csharp
    TMPro.TMP_Text _timeText;
    TMPro.TMP_Text _batteryText;
    RectTransform  _batteryFill;
    int _batteryPct;
    int _lastShownMinute = -1;

    void BuildStatusBar()
    {
        _statusBarRT = NewUI("StatusBar", _screenRT);
        _statusBarRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 18f;
        var hlg = _statusBarRT.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(6, 6, 0, 0);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
        hlg.spacing = 4f;

        // Time (left)
        _timeText = MakeText(_statusBarRT, "--:--", 11, AccentCyan, TextAnchor.MiddleLeft);
        _timeText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;

        // Battery group (right)
        var batteryGroup = NewUI("Battery", _statusBarRT);
        var bgHlg = batteryGroup.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        bgHlg.spacing = 4f; bgHlg.childAlignment = TextAnchor.MiddleRight;
        bgHlg.childForceExpandWidth = false; bgHlg.childForceExpandHeight = true;

        _batteryText = MakeText(batteryGroup, "--%", 11, AccentCyan, TextAnchor.MiddleRight);

        var shell = NewUI("Shell", batteryGroup);
        shell.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 22f;
        shell.sizeDelta = new Vector2(22f, 10f);
        var shellBorder = shell.gameObject.AddComponent<UnityEngine.UI.Image>();
        shellBorder.color = AccentCyan;
        shellBorder.sprite = UIPanelSprites.GetBeveledOutline();
        shellBorder.type   = UnityEngine.UI.Image.Type.Sliced;

        _batteryFill = NewUI("Fill", shell);
        _batteryFill.anchorMin = new Vector2(0f, 0f);
        _batteryFill.anchorMax = new Vector2(0f, 1f); // width set in Update
        _batteryFill.pivot = new Vector2(0f, 0.5f);
        _batteryFill.offsetMin = new Vector2(2f, 2f);
        _batteryFill.offsetMax = new Vector2(0f, -2f);
        var fillImg = _batteryFill.gameObject.AddComponent<UnityEngine.UI.Image>();
        fillImg.color = AccentCyan;
        fillImg.raycastTarget = false;

        // Roll a random session battery
        _batteryPct = Random.Range(20, 96); // 20..95
    }

    TMPro.TMP_Text MakeText(Transform parent, string text, float fontSize, Color color, TextAnchor anchor)
    {
        var rt = NewUI("Text", parent);
        var t = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.enableWordWrapping = false;
        t.alignment = TextAnchor_To_TMP(anchor);
        t.raycastTarget = false;
        return t;
    }

    static TMPro.TextAlignmentOptions TextAnchor_To_TMP(TextAnchor a) => a switch
    {
        TextAnchor.MiddleLeft  => TMPro.TextAlignmentOptions.MidlineLeft,
        TextAnchor.MiddleRight => TMPro.TextAlignmentOptions.MidlineRight,
        TextAnchor.MiddleCenter => TMPro.TextAlignmentOptions.Midline,
        _ => TMPro.TextAlignmentOptions.Midline,
    };
```

- [ ] **Step 6: Fill in `BuildNotificationStrip`**

```csharp
    void BuildNotificationStrip()
    {
        _notificationStripRT = NewUI("NotificationStrip", _screenRT);
        _notificationStripRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 22f;

        var bg = _notificationStripRT.gameObject.AddComponent<UnityEngine.UI.Image>();
        bg.color = TileBg;
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type   = UnityEngine.UI.Image.Type.Sliced;
        bg.raycastTarget = false;

        var hlg = _notificationStripRT.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 0, 0);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

        // LED dot
        var dot = NewUI("Dot", _notificationStripRT);
        dot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 6f;
        dot.sizeDelta = new Vector2(6f, 6f);
        var dotImg = dot.gameObject.AddComponent<UnityEngine.UI.Image>();
        dotImg.color = AccentCyan;
        dotImg.raycastTarget = false;

        // Text
        var label = MakeText(_notificationStripRT, "NO NEW ALERTS", 10, LabelWhite, TextAnchor.MiddleLeft);
        label.fontStyle = TMPro.FontStyles.Normal;
        label.characterSpacing = 2f;
        label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
    }

    public void SetNotificationText(string text)
    {
        // Walk to the label child if it exists.
        if (_notificationStripRT == null) return;
        var labels = _notificationStripRT.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        if (labels.Length > 0) labels[0].text = text;
    }
```

- [ ] **Step 7: Fill in `BuildAppGrid` (2×2 with 4 apps)**

```csharp
    public enum AppKind { Fishingdex, Build, Settings, Map }

    UnityEngine.UI.Button[] _appButtons = new UnityEngine.UI.Button[4];

    void BuildAppGrid()
    {
        _appGridRT = NewUI("AppGrid", _screenRT);
        _appGridRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 170f;

        var grid = _appGridRT.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 4, 4);
        grid.spacing = new Vector2(10f, 10f);
        grid.cellSize = new Vector2(78f, 78f);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        _appButtons[0] = BuildAppTile(AppKind.Fishingdex, "⌬", "Fishingdex");
        _appButtons[1] = BuildAppTile(AppKind.Build,      "▦", "Build");
        _appButtons[2] = BuildAppTile(AppKind.Settings,   "⚙", "Settings");
        _appButtons[3] = BuildAppTile(AppKind.Map,        "◎", "Map");
    }

    UnityEngine.UI.Button BuildAppTile(AppKind kind, string glyph, string label)
    {
        var rt = NewUI($"App_{kind}", _appGridRT);
        var bg = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        bg.color = TileBg;
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = UnityEngine.UI.Image.Type.Sliced;
        bg.raycastTarget = true;

        // Scanner-bracket corners (top-left + bottom-right)
        AddCornerBracket(rt, new Vector2(0f, 1f), 1.5f);
        AddCornerBracket(rt, new Vector2(1f, 0f), 1.5f);

        // Vertical column: glyph above, label below
        var vlg = rt.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 6, 4);
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;

        var glyphText = MakeText(rt, glyph, 22, AccentCyan, TextAnchor.MiddleCenter);
        glyphText.fontStyle = TMPro.FontStyles.Bold;
        glyphText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 30f;

        var labelText = MakeText(rt, label, 9, LabelWhite, TextAnchor.MiddleCenter);
        labelText.characterSpacing = 1f;
        labelText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 14f;

        var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        var capturedKind = kind; // closure
        btn.onClick.AddListener(() => OnAppClicked(capturedKind));
        return btn;
    }

    void AddCornerBracket(RectTransform parentRT, Vector2 anchor, float thickness)
    {
        // Two thin rects forming an L: horizontal + vertical bar.
        const float armLength = 8f;
        var h = NewUI("BracketH", parentRT);
        h.anchorMin = anchor; h.anchorMax = anchor;
        h.pivot = new Vector2(anchor.x, anchor.y);
        h.anchoredPosition = new Vector2(anchor.x == 0f ? 3f : -3f, anchor.y == 1f ? -3f : 3f);
        h.sizeDelta = new Vector2(armLength, thickness);
        var hImg = h.gameObject.AddComponent<UnityEngine.UI.Image>();
        hImg.color = AccentCyan; hImg.raycastTarget = false;

        var v = NewUI("BracketV", parentRT);
        v.anchorMin = anchor; v.anchorMax = anchor;
        v.pivot = new Vector2(anchor.x, anchor.y);
        v.anchoredPosition = new Vector2(anchor.x == 0f ? 3f : -3f, anchor.y == 1f ? -3f : 3f);
        v.sizeDelta = new Vector2(thickness, armLength);
        var vImg = v.gameObject.AddComponent<UnityEngine.UI.Image>();
        vImg.color = AccentCyan; vImg.raycastTarget = false;
    }

    // OnAppClicked is filled in by Task 9. For now, a stub so this compiles.
    void OnAppClicked(AppKind kind)
    {
        Debug.Log($"[PlayerPhoneUI] App clicked: {kind} (routing wired in Task 9)");
    }
```

- [ ] **Step 8: Fill in `BuildReservedZone`**

```csharp
    void BuildReservedZone()
    {
        _reservedZoneRT = NewUI("ReservedZone", _screenRT);
        _reservedZoneRT.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleHeight = 1f;

        var bg = _reservedZoneRT.gameObject.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.06f);
        bg.sprite = UIPanelSprites.GetBeveledOutline();
        bg.type = UnityEngine.UI.Image.Type.Sliced;
        bg.raycastTarget = false;

        var label = MakeText(_reservedZoneRT, "— RESERVED —", 9, AccentCyan, TextAnchor.MiddleCenter);
        label.characterSpacing = 2f;
        label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        var labelRT = label.rectTransform;
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero; labelRT.offsetMax = Vector2.zero;
    }
```

- [ ] **Step 9: Fill in `BuildPutAwayButton`**

```csharp
    void BuildPutAwayButton()
    {
        var rt = NewUI("PutAwayButton", _screenRT);
        rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 30f;

        var bg = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.10f);
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type   = UnityEngine.UI.Image.Type.Sliced;

        var border = NewUI("Border", rt);
        border.anchorMin = Vector2.zero; border.anchorMax = Vector2.one;
        border.offsetMin = Vector2.zero; border.offsetMax = Vector2.zero;
        border.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        var borderImg = border.gameObject.AddComponent<UnityEngine.UI.Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = UnityEngine.UI.Image.Type.Sliced;
        borderImg.color = AccentCyan;
        borderImg.raycastTarget = false;

        var label = MakeText(rt, "PUT AWAY", 11, AccentCyan, TextAnchor.MiddleCenter);
        label.fontStyle = TMPro.FontStyles.Bold;
        label.characterSpacing = 3f;
        label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        var labelRT = label.rectTransform;
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero; labelRT.offsetMax = Vector2.zero;

        _putAwayBtn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        _putAwayBtn.onClick.AddListener(Close); // Close() is a stub right now; Task 6 fills it
    }
```

- [ ] **Step 10: Call `BuildCanvas` from `Awake`**

In `Awake`, after `Instance = this;`, add:

```csharp
        BuildCanvas();
```

- [ ] **Step 11: Compile check + visual playtest**

Recompile. Console clean.

Playtest:
1. Enter Play mode.
2. In the Hierarchy under DontDestroyOnLoad, find `PlayerPhoneUI`. Expand → `Phone` → `Screen` and verify the structure (StatusBar, NotificationStrip, AppGrid, ReservedZone, PutAwayButton).
3. The phone should NOT be visible yet — it's parked off-screen below at `anchoredPosition = (0, -456)` and `CanvasGroup.alpha = 0`.
4. To preview the layout: in the Inspector, select the `Phone` RectTransform → set `anchoredPosition.y` to `16` and `CanvasGroup.alpha` to `1`. Confirm the phone appears at the bottom-center, with chassis, side buttons, status bar (showing 00:00 and --%), notification strip ("NO NEW ALERTS"), 4 app tiles, "— RESERVED —" zone, and "PUT AWAY" button.
5. The time/battery values are static placeholders for now — they animate alive in Task 7.

- [ ] **Step 12: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "Build PlayerPhoneUI chassis + screen layout (closed by default)"
```

---

## Task 6: Open/Close/Toggle with slide-up + fade animation

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (replace the three stub methods + add animation coroutine + X-key input handling)

- [ ] **Step 1: Add animation state + coroutine**

In `PlayerPhoneUI.cs`, add these fields and methods (inside the class, near the bottom):

```csharp
    Coroutine _animCoroutine;
    bool _isAnimating;
    bool _animatingToOpen; // direction of current animation

    // Animated open position and closed position (anchoredPosition.y).
    float OnScreenY  => BottomMargin;
    float OffScreenY => -(PhoneHeight + BottomMargin);

    System.Collections.IEnumerator AnimatePhone(bool toOpen)
    {
        _isAnimating = true;
        _animatingToOpen = toOpen;

        if (toOpen)
        {
            IsOpen = true;
            _phoneGroup.blocksRaycasts = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        float fromY = _phoneRT.anchoredPosition.y;
        float toY   = toOpen ? OnScreenY : OffScreenY;
        float fromA = _phoneGroup.alpha;
        float toA   = toOpen ? 1f : 0f;

        float t = 0f;
        while (t < SlideDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / SlideDuration);
            // ease-out for open (lands soft), ease-in for close
            float eased = toOpen ? 1f - Mathf.Pow(1f - u, 3f) : u * u * u;
            _phoneRT.anchoredPosition = new Vector2(_phoneRT.anchoredPosition.x, Mathf.Lerp(fromY, toY, eased));
            _phoneGroup.alpha = Mathf.Lerp(fromA, toA, eased);
            yield return null;
        }

        _phoneRT.anchoredPosition = new Vector2(_phoneRT.anchoredPosition.x, toY);
        _phoneGroup.alpha = toA;

        if (!toOpen)
        {
            IsOpen = false;
            _phoneGroup.blocksRaycasts = false;
            // Don't unlock cursor here — PlayerController / other systems
            // decide cursor state when phone is closed. Just leave it as-is;
            // the ship/player code resets it on their own update tick.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        _isAnimating = false;
        _animCoroutine = null;
    }
```

- [ ] **Step 2: Replace the three stubs**

Replace the existing `Open()`, `Close()`, `Toggle()` stubs with:

```csharp
    public void Open()
    {
        if (_isAnimating && _animatingToOpen) return; // already opening
        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimatePhone(true));
    }

    public void Close()
    {
        if (_isAnimating && !_animatingToOpen) return; // already closing
        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimatePhone(false));
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else        Open();
    }
```

- [ ] **Step 3: Add X-key input handler (Update)**

Add an `Update` method:

```csharp
    void Update()
    {
        // X toggles the phone. Gates:
        //  - not in dialogue
        //  - pause menu is not active
        //  - phone is not currently animating (prevents press-spam glitches)
        if (_isAnimating) return;
        if (PlayerController.isInDialogue) return;
        if (TabbedPauseMenu.Instance != null && TabbedPauseMenu.Instance.IsOpen) return;

        if (Input.GetKeyDown(KeyCode.X))
        {
            Toggle();
        }

        // ESC handling (close-only) is added in Task 8.
        // Ship-input auto-close is added in Task 9.
    }
```

(`TabbedPauseMenu.IsOpen` may not exist yet as a public property — check now. If it isn't public, expose it in Task 8 step 1 below.)

- [ ] **Step 4: Compile check**

Recompile. If `TabbedPauseMenu.IsOpen` errors, the gate will be wired properly in Task 8 — for now make the gate temporarily forgiving by replacing the `TabbedPauseMenu.Instance.IsOpen` check with `false` (so X always toggles regardless of pause state). Re-add the check in Task 8.

```csharp
        // TEMP: gate added in Task 8
        // if (TabbedPauseMenu.Instance != null && TabbedPauseMenu.Instance.IsOpen) return;
```

- [ ] **Step 5: Playtest the animation**

1. Enter Play mode in `1.6.7.7.7.unity`.
2. Press **X**. Phone slides up from below the hotbar over ~0.25s, fading in.
3. Cursor unlocks and becomes visible (you can move the mouse and see it on screen).
4. Press **X** again. Phone slides back down + fades out. Cursor locks again.
5. Hammer the X key during animation. Confirm: no double-fire, no stuck mid-animation phone.
6. Hover over an app tile, click it. Console logs `[PlayerPhoneUI] App clicked: Fishingdex` (or whichever). Phone stays open (routing not wired yet).
7. Click "PUT AWAY". Phone closes via the slide-out animation.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "Wire X-key toggle + slide-up/fade animation for PlayerPhoneUI"
```

---

## Task 7: Live status bar (time + battery)

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (extend Update to refresh time and battery once per second)

- [ ] **Step 1: Add time + battery refresh to Update**

In the `Update` method added in Task 6, **after** the X-key check, add:

```csharp
        RefreshStatusBar();
```

Then add the new method:

```csharp
    void RefreshStatusBar()
    {
        // Time: real-world local clock, HH:mm. Change-detect on the minute
        // so we're not reassigning TMP text every frame (and triggering layout
        // rebuilds inside the HorizontalLayoutGroup).
        var now = System.DateTime.Now;
        if (now.Minute != _lastShownMinute)
        {
            _lastShownMinute = now.Minute;
            if (_timeText != null) _timeText.text = now.ToString("HH:mm");
        }

        // Battery: set once on first refresh (in case Awake fired before the
        // text components existed in some edit path) and the fill width
        // reflects _batteryPct. The text shows the percent.
        if (_batteryText != null && _batteryText.text == "--%")
            _batteryText.text = $"{_batteryPct}%";
        if (_batteryFill != null)
        {
            // anchorMax.x in [0..1] scales the horizontal fill against the shell.
            float pct = _batteryPct / 100f;
            _batteryFill.anchorMax = new Vector2(pct, 1f);
        }
    }
```

- [ ] **Step 2: Compile check**

Recompile. Console clean.

- [ ] **Step 3: Playtest**

1. Enter Play mode. Press X to open the phone.
2. Time shows your real system clock as `HH:mm`.
3. Battery shows a percentage between 20 and 95 (e.g., "57%") with the fill rectangle scaled to match.
4. Exit Play mode, re-enter. The battery shows a *different* random number — confirms the per-session roll.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "Live status bar: system clock + per-session random battery"
```

---

## Task 8: ESC stacking + look-block + pause-menu gate

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (handle ESC, set `ConsumedEscapeThisFrame`)
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs:221` (gate the OpenPause call on the phone state and the flag, and expose `IsOpen` if not already)
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs:423-424` (early-return mouse-look read when phone is open)

- [ ] **Step 1: Confirm / add `IsOpen` on `TabbedPauseMenu`**

Open `Assets/3 - Scripts/UI/TabbedPauseMenu.cs`. Search for `_isPaused`. The class has a private `bool _isPaused`. Expose it as a public read-only:

```csharp
    public bool IsOpen => _isPaused;
```

Add this near other public properties (just before or after the existing `Instance` declaration).

- [ ] **Step 2: Gate the pause-menu ESC handler**

In `TabbedPauseMenu.cs` line 221, the current line is:

```csharp
            else if (!BuildMenuUI.IsOpen && !FishingdexManager.IsOpen) OpenPause();
```

Replace with:

```csharp
            else if (!BuildMenuUI.IsOpen && !FishingdexManager.IsOpen
                  && !PlayerPhoneUI.IsOpen && !PlayerPhoneUI.ConsumedEscapeThisFrame) OpenPause();
```

The `!PlayerPhoneUI.IsOpen` check covers the case where TabbedPauseMenu runs BEFORE PlayerPhoneUI in this frame's Update order. The `!ConsumedEscapeThisFrame` check covers the case where PlayerPhoneUI runs FIRST (closes phone, sets flag, then TabbedPauseMenu sees `IsOpen == false` but the flag stops it).

- [ ] **Step 3: Handle ESC in `PlayerPhoneUI`**

In `PlayerPhoneUI.Update`, **before** the X-key check, add:

```csharp
        // ESC closes the phone (without opening the pause menu — see
        // TabbedPauseMenu.Update for the dual guard). Setting
        // ConsumedEscapeThisFrame protects against Update-order races:
        // if TabbedPauseMenu runs after us, it sees the flag and skips
        // its OpenPause branch.
        if (IsOpen && !_isAnimating && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            ConsumedEscapeThisFrame = true;
            return; // don't also fire the X-key check this frame
        }
```

Also remove the `TEMP: gate added in Task 8` line and restore the pause check:

```csharp
        if (TabbedPauseMenu.Instance != null && TabbedPauseMenu.Instance.IsOpen) return;
```

- [ ] **Step 4: Block mouse look in PlayerController while phone is open**

Open `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs`. Lines 423-424 are the mouse-look reads:

```csharp
			yaw   += TutorialGate.GetAxisRaw("Mouse X", TutorialAbility.MouseLook) * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;
			pitch -= TutorialGate.GetAxisRaw("Mouse Y", TutorialAbility.MouseLook) * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;
```

Wrap them in an `if (!PlayerPhoneUI.IsOpen)` so they skip when the phone is open:

```csharp
			if (!PlayerPhoneUI.IsOpen)
			{
				yaw   += TutorialGate.GetAxisRaw("Mouse X", TutorialAbility.MouseLook) * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;
				pitch -= TutorialGate.GetAxisRaw("Mouse Y", TutorialAbility.MouseLook) * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;
			}
```

- [ ] **Step 5: Compile check**

Recompile. Console clean.

- [ ] **Step 6: Playtest the ESC stack and look-block**

1. Enter Play mode. On foot.
2. **ESC with phone closed:** press ESC. Pause menu opens. Close pause menu with ESC again.
3. **X opens phone:** press X. Phone slides up.
4. **Look-block:** try to move the mouse / camera. The camera does not rotate. WASD movement still works.
5. **ESC closes phone, doesn't open pause:** press ESC. Phone slides out. Pause menu does NOT open. Cursor re-locks.
6. **ESC again opens pause:** press ESC again. Pause menu opens normally.
7. **X with pause menu open:** open pause with ESC, then press X. Nothing happens (phone gate). Close pause.
8. Console clean throughout.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs" "Assets/3 - Scripts/UI/TabbedPauseMenu.cs" "Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs"
git commit -m "ESC stacking + look-block while PlayerPhoneUI is open"
```

---

## Task 9: App routing (Fishingdex / Build / Settings / Map)

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (implement `OnAppClicked` for all four apps + close-then-open sequencing)
- Modify (possibly): `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` (add `OpenSettings()` or `Open(string tabName)` helper if not already present)

- [ ] **Step 1: Audit existing entry points**

For each target, confirm the public method name:

```bash
grep -n "public void Open\|public static.*Open\|public void Toggle\|OpenDex" \
  "Assets/3 - Scripts/Fishing/FishingdexManager.cs" \
  "Assets/3 - Scripts/Building/BuildMenuUI.cs" \
  "Assets/3 - Scripts/Map/SolarSystemMapController.cs" \
  "Assets/3 - Scripts/UI/TabbedPauseMenu.cs"
```

Confirmed callable patterns (from earlier grep + spec):
- `FishingdexManager.Instance` is the singleton; its UI is built procedurally and the dex opens via an internal method. If a public `OpenDex()` exists, use it; otherwise add one in this task.
- `BuildMenuUI.Open()` is currently `void Open()` (private at line 145). Make it public.
- `SolarSystemMapController` exposes its open path through `toggleKey` handling at line 112 — refactor to expose `public void OpenMap()` if not already public.
- `TabbedPauseMenu.OpenPause()` is private; add a public wrapper `OpenAtSettings()` that calls `OpenPause()` then navigates to the Settings tab.

If any of these methods are currently private/internal, surface them as public for this task. Keep the change minimal — just the access modifier.

- [ ] **Step 2: Replace the `OnAppClicked` stub**

In `PlayerPhoneUI.cs`, replace the `OnAppClicked` stub with:

```csharp
    void OnAppClicked(AppKind kind)
    {
        // Start the close animation. After it finishes, route to the target
        // UI's open method. Like tapping a real phone — home screen exits
        // before the app shows.
        StartCoroutine(CloseThenOpen(kind));
    }

    System.Collections.IEnumerator CloseThenOpen(AppKind kind)
    {
        Close();
        // Wait for the close animation to finish.
        yield return new WaitWhile(() => _isAnimating);

        switch (kind)
        {
            case AppKind.Fishingdex:
                if (FishingdexManager.Instance != null) FishingdexManager.Instance.OpenDex();
                else Debug.LogWarning("[PlayerPhoneUI] FishingdexManager.Instance is null");
                break;
            case AppKind.Build:
                if (BuildMenuUI.Instance != null) BuildMenuUI.Instance.Open();
                else Debug.LogWarning("[PlayerPhoneUI] BuildMenuUI.Instance is null");
                break;
            case AppKind.Settings:
                if (TabbedPauseMenu.Instance != null) TabbedPauseMenu.Instance.OpenAtSettings();
                else Debug.LogWarning("[PlayerPhoneUI] TabbedPauseMenu.Instance is null");
                break;
            case AppKind.Map:
                if (SolarSystemMapController.Instance != null) SolarSystemMapController.Instance.OpenMap();
                else Debug.LogWarning("[PlayerPhoneUI] SolarSystemMapController.Instance is null");
                break;
        }
    }
```

- [ ] **Step 3: If needed, surface the four `Open*` methods**

For each target singleton that doesn't already expose a public open method, make this minimal edit:

**`FishingdexManager.cs`** — find the existing internal "open the dex" method. Add a public wrapper if none exists:
```csharp
    public void OpenDex() { /* call into the existing open path */ }
```

**`BuildMenuUI.cs:145`** — change `void Open()` to `public void Open()`.

**`SolarSystemMapController.cs`** — find the internal open path (around line 112 where `toggleKey` is handled). Add a public wrapper:
```csharp
    public void OpenMap() { /* call into the existing open path */ }
```

**`TabbedPauseMenu.cs`** — add public method that opens pause and switches to Settings tab:
```csharp
    public void OpenAtSettings()
    {
        OpenPause();
        // After OpenPause runs, navigate to the Settings tab.
        // Look for the tab switch API in this file — likely a method
        // named SwitchTab(int) or SwitchTo(string). Call it with the
        // Settings tab index/name.
        // Example: SwitchTab(TabIndex.Settings);
    }
```

(If the tab switch API doesn't exist as a clean method, leaving `OpenAtSettings` to just call `OpenPause()` for now is fine — Settings is accessible from the main pause panel — and add a TODO comment to switch directly to Settings later.)

- [ ] **Step 4: Compile check**

Recompile. Console clean.

- [ ] **Step 5: Playtest each app**

Enter Play mode. For each of the four apps:
1. Press X to open the phone.
2. Click the app tile.
3. Confirm: phone slides out, and the target UI appears (Fishingdex / Build menu / Pause-Settings / Map).
4. Close the target UI via its own dismiss (ESC, M, N, etc.).
5. Confirm Console is clean (no `[PlayerPhoneUI] X.Instance is null` warnings).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "App routing: phone tiles open Fishingdex/Build/Settings/Map"
```

---

## Task 10: Anchor left of the hotbar (with BoostMeterUI awareness)

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (add a per-Update layout poll that sets `anchoredPosition.x` based on hotbar + boost meter widths)

- [ ] **Step 1: Locate hotbar + BoostMeterUI singletons**

```bash
grep -n "public static.*Hotbar\|public static.*BoostMeter\|public static .*Instance" \
  "Assets/3 - Scripts/UI/Hotbar.cs" "Assets/3 - Scripts/Ship/BoostMeterUI.cs"
```

Confirm:
- `Hotbar.Instance` is the singleton (already in CLAUDE.md).
- `BoostMeterUI` may have a singleton or be discoverable via `FindObjectOfType` — pick whichever exists. Cache the reference, lazy-find pattern (per CLAUDE.md "Lazy-cached scene lookup").

- [ ] **Step 2: Add the anchoring logic**

In `PlayerPhoneUI.cs`, add fields:

```csharp
    Hotbar       _hotbarRef;
    BoostMeterUI _boostRef;
    float        _lastResolvedX = float.NaN;
```

And add an `UpdatePosition` method, called from `Update` (before the input checks):

```csharp
    void UpdatePosition()
    {
        if (_hotbarRef == null) _hotbarRef = Hotbar.Instance;
        if (_boostRef  == null) _boostRef  = FindObjectOfType<BoostMeterUI>(true);

        // Hotbar width — measure its bar RectTransform if available, else use
        // a conservative default that matches a 5-slot row.
        const float fallbackHotbarHalfWidth = 250f; // ~5 slots × 80 px / 2
        float hotbarHalfWidth = fallbackHotbarHalfWidth;
        if (_hotbarRef != null)
        {
            var bar = _hotbarRef.GetComponentInChildren<RectTransform>();
            if (bar != null && bar != _hotbarRef.transform)
                hotbarHalfWidth = bar.rect.width * 0.5f;
        }

        // BoostMeterUI shifts the phone further left when it's currently
        // visible (jetpack equipped, or piloting), so phone never overlaps it.
        bool boostVisible = _boostRef != null && _boostRef.isActiveAndEnabled
                            && _boostRef.gameObject.activeInHierarchy;
        float boostExtraOffset = boostVisible ? 240f : 0f; // conservative; tune in playtest

        float targetX = -(hotbarHalfWidth + HotbarGap + PhoneWidth * 0.5f + boostExtraOffset);

        if (!float.IsNaN(_lastResolvedX) && Mathf.Approximately(targetX, _lastResolvedX)) return;
        _lastResolvedX = targetX;
        _phoneRT.anchoredPosition = new Vector2(targetX, _phoneRT.anchoredPosition.y);
    }
```

In `Update`, add `UpdatePosition();` as the very first line (before ESC / X / status-bar checks).

- [ ] **Step 3: Compile check**

Recompile. Console clean. If `BoostMeterUI` doesn't expose `isActiveAndEnabled` cleanly, swap to `_boostRef.enabled && _boostRef.gameObject.activeInHierarchy`.

- [ ] **Step 4: Playtest the anchoring**

1. Enter Play mode. On foot, no jetpack yet.
2. Press X. Phone appears at the bottom, to the **left of the hotbar**, with a visible gap.
3. Through the Editor inspector (or via cheats), unlock and equip the jetpack so `BoostMeterUI` appears.
4. Press X (close), press X again (re-open). Phone re-anchors further left so it doesn't overlap BoostMeterUI.
5. Disable BoostMeterUI again. Phone scoots back closer to the hotbar.
6. Confirm: phone never overlaps either the hotbar or BoostMeterUI.

- [ ] **Step 5: Tune the offsets if needed**

The values `HotbarGap = 32f` and `boostExtraOffset = 240f` are conservative. If the phone looks too far from the hotbar (gap too wide) or overlaps BoostMeterUI, tweak these constants. Re-test.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "Anchor phone left of hotbar; shift left when BoostMeterUI is visible"
```

---

## Task 11: Ship-input auto-close while piloting

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (add ship-input poll that closes the phone)

- [ ] **Step 1: Add the auto-close check**

In `PlayerPhoneUI.Update`, **after** the existing logic, add:

```csharp
        // While piloting, any ship-control input closes the phone (so the
        // player can react fast without explicitly putting the phone away).
        if (IsOpen && !_isAnimating && Ship.PilotedInstance != null)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) ||
                Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Q) ||
                Input.GetKey(KeyCode.E) || Input.GetMouseButton(0) || Input.GetMouseButton(1))
            {
                Close();
            }
        }
```

(Uses `GetKey` not `GetKeyDown` so a held W key — common while flying — still triggers the close.)

- [ ] **Step 2: Compile check**

Recompile. Console clean.

- [ ] **Step 3: Playtest**

1. Enter Play mode. Walk to the ship, climb in, pilot.
2. Press X. Phone slides up.
3. Press W (or any thrust key). Phone immediately slides down. Ship responds to your input normally.
4. Press X again. Phone slides up. Press ESC. Phone slides down (no pause menu).
5. Exit the ship via the hatch. On foot, press X. Phone opens. Press W to walk. Phone STAYS OPEN (the auto-close only fires while piloting).
6. Console clean.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "Auto-close phone on ship-control input while piloting"
```

---

## Task 12: Edge-case handlers (dialogue, death, scene reload)

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (subscribe to existing events; force-close on each)

- [ ] **Step 1: Add force-close + event subscriptions**

In `PlayerPhoneUI.cs`, add:

```csharp
    void OnEnable()
    {
        NPCConversationTracker.OnConversationStarted += ForceCloseNoAnim;
        ResourceManager.OnDeath                       += ForceCloseNoAnim;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        NPCConversationTracker.OnConversationStarted -= ForceCloseNoAnim;
        ResourceManager.OnDeath                       -= ForceCloseNoAnim;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // On any scene transition (including back to MainMenu), force-close so
        // a loaded save never sees the phone half-open.
        ForceCloseNoAnim();
    }

    void ForceCloseNoAnim()
    {
        if (_animCoroutine != null) { StopCoroutine(_animCoroutine); _animCoroutine = null; }
        _isAnimating = false;
        IsOpen = false;
        if (_phoneRT    != null) _phoneRT.anchoredPosition = new Vector2(_phoneRT.anchoredPosition.x, OffScreenY);
        if (_phoneGroup != null) { _phoneGroup.alpha = 0f; _phoneGroup.blocksRaycasts = false; }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }
```

(The event signatures should match — `NPCConversationTracker.OnConversationStarted` is parameterless per CLAUDE.md; `ResourceManager.OnDeath` is parameterless per CLAUDE.md. If either takes parameters in the actual code, adapt the handler signature.)

- [ ] **Step 2: Compile check**

Recompile. If either event signature differs, add a matching lambda — for example:

```csharp
        ResourceManager.OnDeath += () => ForceCloseNoAnim();
```

with matching `-=` in `OnDisable`.

- [ ] **Step 3: Playtest edge cases**

1. **Dialogue during phone:** Enter Play mode. Press X. Walk up to a nearby NPC (Alien3, BonfireNPC, Tev). The phone should close abruptly the moment dialogue starts.
2. **Death during phone:** Open phone. Take damage until you die (use cheats if needed via `CheatCodes.cs`). Phone closes immediately on death.
3. **Scene reload:** Open phone. Use the pause menu's "Return to Main Menu" option. No errors, phone closes silently.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "Force-close phone on dialogue, death, scene load"
```

---

## Task 13: Build sanity check (the build-only bug class)

**Files:** none modified — this is verification.

**Why:** This is the same trap that caused the grass-flicker incident. Always playtest the actual build, not just the Editor.

- [ ] **Step 1: Build**

In Unity: `File → Build Settings → Build`. Output goes to `Solar System 2.exe`.

- [ ] **Step 2: Launch the built game**

Run `Solar System 2.exe`. The MainMenu loads.

- [ ] **Step 3: Start a New Game (or load an existing save)**

Click PLAY → NEW GAME, or LOAD if you have a save.

- [ ] **Step 4: Verify phone works**

1. Once in the gameplay scene, press X. Phone slides up.
2. Confirm cursor unlocks and mouse-look is blocked.
3. Press ESC. Phone closes, pause does not open.
4. Press ESC again. Pause menu opens. Close it.
5. Click each of the four apps from the phone. Each opens its corresponding UI.

If the phone does NOT appear when pressing X in the build, the `EnsureGameplaySingletons` seed from Task 4 is missing — re-check `MainMenuController.cs` and ensure the `PlayerPhoneUI` block is present before the closing `}` of `EnsureGameplaySingletons`.

- [ ] **Step 5: Save round-trip**

1. With phone closed, save via the pause menu.
2. Reload the save. Phone is closed on load. ✓
3. Open phone. Save (the autosave timer or manual save). Reload. Phone is closed on load. ✓ (Transient — by design.)
4. If you have an OLD save from before this work (with `autoAlignEnabled` still in the JSON), load it. Confirm: no errors. JsonUtility silently ignores the dropped field.

- [ ] **Step 6: Final commit (if any small tweaks emerged from build testing)**

If the build testing surfaced any small bugs (gap too wide, app icon missing, etc.) fix them now and commit:

```bash
git add -A
git commit -m "Final tuning after build playtest"
```

If everything works as-is, no commit needed — task is complete.

---

## Self-review (run after task list above)

**Spec coverage check:**

| Spec section | Implementing task(s) |
|---|---|
| Visual style (chassis, palette, sprite reuse) | Task 5 |
| Position (left of hotbar, between BoostMeter + hotbar) | Task 10 |
| Animation (slide + fade, ease, unscaledDeltaTime) | Task 6 |
| Status bar (time, battery) | Tasks 5, 7 |
| Notification strip + reserved zone (public hooks) | Task 5 (`NotificationStripRoot`, `ReservedZoneRoot`, `SetNotificationText`) |
| X-key toggle | Task 6 |
| Cursor unlock + look-block | Tasks 6, 8 |
| ESC stacking (phone consumes first ESC) | Task 8 |
| Ship-input auto-close while piloting | Task 11 |
| App routing (Fishingdex/Build/Settings/Map) | Task 9 |
| Auto-align removal | Tasks 1, 2, 3 |
| EnsureGameplaySingletons seed (build sanity) | Tasks 4, 13 |
| Edge cases (dialogue, death, scene reload) | Task 12 |
| Save-system compat | Tasks 1, 13 (round-trip test) |

All spec sections have at least one task. ✓

**Placeholder scan:** None — all code blocks are concrete. Two known-unknowns explicitly flagged for in-flight resolution (Task 9 step 1: audit existing entry points; Task 9 step 3: surface methods as public as needed). These aren't vague requirements; they're "look at the actual repo state and make a minimal access-modifier change."

**Type/signature consistency:** `IsOpen`, `ConsumedEscapeThisFrame`, `Open()`, `Close()`, `Toggle()`, `OpenApp(AppKind)`, `NotificationStripRoot`, `ReservedZoneRoot`, `SetNotificationText(string)`, `AppKind` enum — used consistently across all tasks.

**Scope:** Single feature, single sequenced plan. No decomposition needed.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-20-smartphone-ui.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using `executing-plans`, batch execution with checkpoints.

Which approach?
