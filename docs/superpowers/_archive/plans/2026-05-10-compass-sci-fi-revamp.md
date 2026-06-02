# Sci-fi Compass Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `CompassHUD` to a Destiny/Halo-style sci-fi compass (heading-number badge + degree-number labels + 10° tick marks alongside the existing cardinal letters) and stop the compass from flashing on the main menu when the player clicks PLAY.

**Architecture:** Single file (`Assets/3 - Scripts/UI/CompassHUD.cs`). Introduce a `WaypointKind` enum so the existing waypoint mechanism handles the new visual variants (Cardinal / DegreeNumber / Tick / Gameplay). Seed 8 degree-number waypoints + 36 tick waypoints alongside the 4 existing cardinals. Add a heading badge above the strip, updated each frame from the player-vs-north bearing math already in `LateUpdate`. Disable the canvas on Awake; enable it the first time `LateUpdate` finds a `PlayerController` (one-shot flip) — kills the main-menu bleed.

**Tech Stack:** Unity 2022.3, TextMeshPro, no test framework (verification = Unity Editor compile + Play mode).

**Spec reference:** `docs/superpowers/specs/2026-05-10-compass-sci-fi-revamp-design.md`

---

## File map

**Modified:**
- `Assets/3 - Scripts/UI/CompassHUD.cs` — every change lives here. No new files.

---

## Conventions used by every task

- **Compile check:** after each change, run `mcp__coplay-mcp__check_compile_errors`. Expected `No compile errors` before continuing.
- **Commit format:** `feat(compass): …` / `fix(compass): …` short messages.
- **`git add` is always specific:** never `git add -A`.
- Working dir: `C:\123\1aughhh1`. No automated tests; user verifies in Play mode.

---

## Task 1: Add `WaypointKind` + extend `BuildWaypointUI` for new variants

**Files:**
- Modify: `Assets/3 - Scripts/UI/CompassHUD.cs`

Foundation refactor. Introduces a `WaypointKind` enum, adds a `Kind` field to the `Waypoint` class, routes the existing cardinal-letter branch through the new `kind == Cardinal` path, and adds the visual branches for `DegreeNumber` and `Tick`. No new waypoints seeded yet — that's Task 2. Visual output should be unchanged after this task (still just 4 cardinal letters).

- [ ] **Step 1: Add the enum + the `Kind` field on `Waypoint`.**

Find the `sealed class Waypoint` block near the top of `CompassHUD.cs` (just below the public-static colour palette / around line 40). Insert an enum declaration above it and add a `Kind` field to the class:

```csharp
    public enum WaypointKind { Gameplay, Cardinal, DegreeNumber, Tick }

    sealed class Waypoint
    {
        public string Id;
        public string Label;
        public string SourceTag;          // empty for dynamic-only (Func) waypoints
        public WaypointKind Kind = WaypointKind.Gameplay;
        public System.Func<Vector3> PositionProvider;
        public Sprite Icon;
        public Color Tint = Color.white;
        public bool Active = true;
        public RectTransform Ui;
        public Image IconImage;
        public TextMeshProUGUI LabelText;
        public CanvasGroup Group;
        public string LastShownLabel;
    }
```

- [ ] **Step 2: Set `Kind = Cardinal` on the existing cardinal seeds.**

Find the `AddCardinal` method (around line 340). Inside the `var wp = new Waypoint { ... }` block, add the `Kind` assignment:

```csharp
    void AddCardinal(string letter, float bearingDegrees)
    {
        var wp = new Waypoint
        {
            Id = "cardinal_" + letter,
            SourceTag = CardinalSourceTag,
            Kind = WaypointKind.Cardinal,
            Label = letter,
            // ... existing PositionProvider lambda unchanged ...
            PositionProvider = () =>
            {
                // (keep existing body)
                if (_playerCached == null) return Vector3.zero;
                Vector3 origin = _playerCached.Rigidbody != null
                    ? _playerCached.Rigidbody.position
                    : _playerCached.transform.position;
                Vector3 surfaceUp = _playerCached.transform.up;
                Vector3 northDir = ComputeSurfaceNorth(origin, surfaceUp);
                if (northDir.sqrMagnitude < 0.0001f) return Vector3.zero;
                Quaternion rot = Quaternion.AngleAxis(bearingDegrees, surfaceUp);
                Vector3 dir = rot * northDir;
                return origin + dir * 100f;
            },
            Tint = CardinalColor,
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }
```

- [ ] **Step 3: Replace `BuildWaypointUI` to switch on `Kind`.**

Find the existing `BuildWaypointUI(Waypoint wp)` method (around line 410). Replace its entire body with the four-branch version below:

```csharp
    void BuildWaypointUI(Waypoint wp)
    {
        var containerGo = new GameObject($"WP_{wp.Id}", typeof(RectTransform));
        containerGo.transform.SetParent(_strip, false);
        var rt = containerGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(80f, stripHeight);
        rt.anchoredPosition = Vector2.zero;
        wp.Ui = rt;

        wp.Group = containerGo.AddComponent<CanvasGroup>();
        wp.Group.interactable = false;
        wp.Group.blocksRaycasts = false;

        switch (wp.Kind)
        {
            case WaypointKind.Cardinal:      BuildCardinalUI(wp, containerGo);     break;
            case WaypointKind.DegreeNumber:  BuildDegreeNumberUI(wp, containerGo); break;
            case WaypointKind.Tick:          BuildTickUI(wp, containerGo);         break;
            default:                         BuildGameplayUI(wp, containerGo);     break;
        }
        wp.LastShownLabel = wp.Label;
    }

    void BuildCardinalUI(Waypoint wp, GameObject containerGo)
    {
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(containerGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.5f, 0.5f);
        labelRt.anchorMax = new Vector2(0.5f, 0.5f);
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.sizeDelta = new Vector2(40f, stripHeight);
        labelRt.anchoredPosition = Vector2.zero;
        wp.LabelText = labelGo.AddComponent<TextMeshProUGUI>();
        wp.LabelText.text = wp.Label ?? wp.Id;
        wp.LabelText.fontSize = 14f;
        wp.LabelText.fontStyle = FontStyles.Bold;
        wp.LabelText.alignment = TextAlignmentOptions.Center;
        wp.LabelText.color = CardinalColor;
        wp.LabelText.raycastTarget = false;
        wp.LabelText.outlineColor = Color.black;
        wp.LabelText.outlineWidth = 0.2f;
    }

    void BuildDegreeNumberUI(Waypoint wp, GameObject containerGo)
    {
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(containerGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.5f, 0.5f);
        labelRt.anchorMax = new Vector2(0.5f, 0.5f);
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.sizeDelta = new Vector2(36f, stripHeight);
        labelRt.anchoredPosition = new Vector2(0f, 1f);
        wp.LabelText = labelGo.AddComponent<TextMeshProUGUI>();
        wp.LabelText.text = wp.Label ?? wp.Id;
        wp.LabelText.fontSize = 10f;
        wp.LabelText.fontStyle = FontStyles.Bold;
        wp.LabelText.alignment = TextAlignmentOptions.Center;
        wp.LabelText.color = new Color32(0x78, 0xC8, 0xFF, 0xA6);
        wp.LabelText.characterSpacing = 1f;
        wp.LabelText.raycastTarget = false;
    }

    void BuildTickUI(Waypoint wp, GameObject containerGo)
    {
        bool major = wp.SourceTag == "__TICK_MAJOR__";
        var tickGo = new GameObject("Tick", typeof(RectTransform));
        tickGo.transform.SetParent(containerGo.transform, false);
        var tickRt = tickGo.GetComponent<RectTransform>();
        tickRt.anchorMin = new Vector2(0.5f, 1f);
        tickRt.anchorMax = new Vector2(0.5f, 1f);
        tickRt.pivot = new Vector2(0.5f, 1f);
        tickRt.sizeDelta = new Vector2(1f, major ? 9f : 6f);
        tickRt.anchoredPosition = new Vector2(0f, -2f);
        var img = tickGo.AddComponent<Image>();
        img.color = major
            ? new Color32(0x78, 0xC8, 0xFF, 0xBF)
            : new Color32(0x78, 0xC8, 0xFF, 0x73);
        img.raycastTarget = false;
    }

    void BuildGameplayUI(Waypoint wp, GameObject containerGo)
    {
        // Icon
        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(containerGo.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 1f);
        iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot = new Vector2(0.5f, 1f);
        iconRt.sizeDelta = new Vector2(16f, 16f);
        iconRt.anchoredPosition = new Vector2(0f, -2f);
        wp.IconImage = iconGo.AddComponent<Image>();
        wp.IconImage.sprite = wp.Icon != null ? wp.Icon : GetDefaultMarkerSprite();
        wp.IconImage.color = wp.Tint == Color.white ? MarkerIconColor : wp.Tint;
        wp.IconImage.raycastTarget = false;
        var iconGlow = iconGo.AddComponent<Shadow>();
        iconGlow.effectColor = MarkerGlowColor;
        iconGlow.effectDistance = new Vector2(0f, 0f);

        // Label
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(containerGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 0f);
        labelRt.pivot = new Vector2(0.5f, 0f);
        labelRt.sizeDelta = new Vector2(0f, 24f);
        labelRt.anchoredPosition = Vector2.zero;
        wp.LabelText = labelGo.AddComponent<TextMeshProUGUI>();
        wp.LabelText.text = wp.Label ?? wp.Id;
        wp.LabelText.fontSize = 12f;
        wp.LabelText.fontStyle = FontStyles.Bold;
        wp.LabelText.alignment = TextAlignmentOptions.Center;
        wp.LabelText.color = MarkerLabelColor;
        wp.LabelText.raycastTarget = false;
        wp.LabelText.outlineColor = Color.black;
        wp.LabelText.outlineWidth = 0.2f;
    }
```

- [ ] **Step 4: Update `GetSaveState` to gate on Kind, not the old tag check.**

Find `GetSaveState` (around line 590). Change the skip condition from `SourceTag` checking to `Kind != Gameplay`:

```csharp
    public List<CompassSave.WaypointEntry> GetSaveState()
    {
        var list = new List<CompassSave.WaypointEntry>();
        for (int i = 0; i < _waypoints.Count; i++)
        {
            var wp = _waypoints[i];
            // Only gameplay waypoints persist; everything else (cardinals,
            // degree numbers, ticks) is rebuilt at runtime.
            if (wp.Kind != WaypointKind.Gameplay) continue;
            list.Add(new CompassSave.WaypointEntry
            {
                id = wp.Id,
                label = wp.Label,
                sourceTag = wp.SourceTag,
                active = wp.Active,
            });
        }
        return list;
    }
```

And in `ApplySaveState`, change the cardinal-skip condition the same way:

```csharp
    public void ApplySaveState(List<CompassSave.WaypointEntry> entries)
    {
        // Clear gameplay waypoints only — keep cardinals/numbers/ticks intact.
        for (int i = _waypoints.Count - 1; i >= 0; i--)
        {
            if (_waypoints[i].Kind != WaypointKind.Gameplay) continue;
            if (_waypoints[i].Ui != null) Destroy(_waypoints[i].Ui.gameObject);
            _waypoints.RemoveAt(i);
        }
        if (entries == null) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.sourceTag)) continue;
            if (e.sourceTag == CardinalSourceTag) continue;
            AddWaypointByTag(e.id, e.sourceTag, e.label);
            if (!e.active) SetActive(e.id, false);
        }
    }
```

- [ ] **Step 5: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

If a sub-build method (`BuildCardinalUI`, etc.) reports an error that a field doesn't exist or a name collides, recheck the method names — the four branch helpers are NEW methods (file-scope, inside the `CompassHUD` class).

- [ ] **Step 6: Commit.**

```bash
git add "Assets/3 - Scripts/UI/CompassHUD.cs"
git commit -m "refactor(compass): WaypointKind enum + per-kind UI builders"
```

---

## Task 2: Seed degree-number + tick waypoints

**Files:**
- Modify: `Assets/3 - Scripts/UI/CompassHUD.cs`

Adds 8 degree-number waypoints (every 30° between cardinals) and 36 tick waypoints (every 10° around the full circle). They use the same body-relative bearing math as cardinals.

- [ ] **Step 1: Add seed helpers + call them after `SeedCardinalWaypoints`.**

Find `SeedCardinalWaypoints` (around line 332). Below it, add two new helper methods and update `BuildCanvas` to call them.

In `BuildCanvas` (the existing block that calls `SeedCardinalWaypoints();` at the bottom), replace that single call with:

```csharp
        SeedTickWaypoints();        // bottom layer of the strip's child stack
        SeedDegreeNumberWaypoints();
        SeedCardinalWaypoints();    // letters sit on top, drawn last
```

Order matters because Unity UI draws children in hierarchy order: earlier siblings render BEHIND later siblings. Ticks are seeded first so they render behind the labels.

Now add the two new methods next to `SeedCardinalWaypoints`:

```csharp
    void SeedDegreeNumberWaypoints()
    {
        // Every 30° between cardinals (cardinals at 0/90/180/270 are excluded).
        int[] bearings = { 30, 60, 120, 150, 210, 240, 300, 330 };
        for (int i = 0; i < bearings.Length; i++)
            AddDegreeNumber(bearings[i]);
    }

    void AddDegreeNumber(int bearingDegrees)
    {
        float bearingF = bearingDegrees;
        var wp = new Waypoint
        {
            Id = $"deg_{bearingDegrees:000}",
            SourceTag = CardinalSourceTag,
            Kind = WaypointKind.DegreeNumber,
            Label = $"{bearingDegrees:000}",
            PositionProvider = () => BearingPosition(bearingF),
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }

    void SeedTickWaypoints()
    {
        // 36 ticks every 10° around the full circle. Every third tick (0, 30,
        // 60, …) is "major" — taller and brighter.
        for (int deg = 0; deg < 360; deg += 10)
        {
            bool major = (deg % 30) == 0;
            AddTick(deg, major);
        }
    }

    void AddTick(int bearingDegrees, bool major)
    {
        float bearingF = bearingDegrees;
        var wp = new Waypoint
        {
            Id = $"tick_{bearingDegrees:000}",
            SourceTag = major ? "__TICK_MAJOR__" : "__TICK_MINOR__",
            Kind = WaypointKind.Tick,
            Label = "",
            PositionProvider = () => BearingPosition(bearingF),
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }

    // Helper: virtual world position 100 m out at the given bearing relative
    // to the body-projected world-north reference, from the player's current
    // position. Shared by cardinal/degree-number/tick waypoint providers.
    Vector3 BearingPosition(float bearingDegrees)
    {
        if (_playerCached == null) return Vector3.zero;
        Vector3 origin = _playerCached.Rigidbody != null
            ? _playerCached.Rigidbody.position
            : _playerCached.transform.position;
        Vector3 surfaceUp = _playerCached.transform.up;
        Vector3 northDir = ComputeSurfaceNorth(origin, surfaceUp);
        if (northDir.sqrMagnitude < 0.0001f) return Vector3.zero;
        Quaternion rot = Quaternion.AngleAxis(bearingDegrees, surfaceUp);
        Vector3 dir = rot * northDir;
        return origin + dir * 100f;
    }
```

- [ ] **Step 2 (optional): Simplify `AddCardinal` to reuse `BearingPosition`.**

Now that `BearingPosition` exists, the cardinal-seed lambda can use it too. Replace `AddCardinal`'s `PositionProvider` lambda with the helper call:

```csharp
    void AddCardinal(string letter, float bearingDegrees)
    {
        var wp = new Waypoint
        {
            Id = "cardinal_" + letter,
            SourceTag = CardinalSourceTag,
            Kind = WaypointKind.Cardinal,
            Label = letter,
            PositionProvider = () => BearingPosition(bearingDegrees),
            Tint = CardinalColor,
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }
```

(Cosmetic — three duplicated providers become one.)

- [ ] **Step 3: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 4: Commit.**

```bash
git add "Assets/3 - Scripts/UI/CompassHUD.cs"
git commit -m "feat(compass): seed degree-number and tick waypoints"
```

---

## Task 3: Heading badge above the strip

**Files:**
- Modify: `Assets/3 - Scripts/UI/CompassHUD.cs`

Adds a small monospaced badge above the strip showing the current heading like `045°  NE`. Computed each frame from the player's forward vs body-projected north. Change-detected so the text only writes when the rounded degree integer or the cardinal code changes.

- [ ] **Step 1: Add badge fields + colors near the top of the class.**

Below the existing palette block (`MarkerGlowColor` etc., around line 70), add a new color + the field references:

```csharp
    static readonly Color BadgeBgColor      = new Color32(0x14, 0x2C, 0x48, 0xF2);
    static readonly Color BadgeBorderColor  = new Color32(0x78, 0xC8, 0xFF, 0x8C);
    static readonly Color BadgeTextColor    = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color BadgeTextGlowColor = new Color(0.36f, 0.78f, 1f, 0.55f);

    RectTransform _badgeRT;
    TextMeshProUGUI _badgeText;
    int _lastHeadingShown = int.MinValue;
    string _lastCardinalCode = "";

    static readonly string[] CardinalCodes = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
```

- [ ] **Step 2: Build the badge inside `BuildCanvas`.**

Find `BuildCanvas` (around line 256). After the existing tick-build block (around `tickGlow.effectDistance = new Vector2(0f, 0f);`) and BEFORE the `SeedTickWaypoints / SeedDegreeNumberWaypoints / SeedCardinalWaypoints` calls, add the badge construction:

```csharp
        // Heading badge — sits 4 px above the strip, centered on the screen.
        var badgeGo = new GameObject("HeadingBadge", typeof(RectTransform));
        badgeGo.transform.SetParent(transform, false);
        _badgeRT = badgeGo.GetComponent<RectTransform>();
        _badgeRT.anchorMin = new Vector2(0.5f, 1f);
        _badgeRT.anchorMax = new Vector2(0.5f, 1f);
        _badgeRT.pivot = new Vector2(0.5f, 1f);
        _badgeRT.sizeDelta = new Vector2(120f, 20f);
        _badgeRT.anchoredPosition = new Vector2(0f, -(topMargin - 24f));

        var badgeBg = badgeGo.AddComponent<Image>();
        badgeBg.color = BadgeBgColor;
        badgeBg.raycastTarget = false;
        var badgeOutline = badgeGo.AddComponent<Outline>();
        badgeOutline.effectColor = BadgeBorderColor;
        badgeOutline.effectDistance = new Vector2(1f, -1f);
        var badgeGlow = badgeGo.AddComponent<Shadow>();
        badgeGlow.effectColor = new Color(BadgeBorderColor.r, BadgeBorderColor.g, BadgeBorderColor.b, 0.30f);
        badgeGlow.effectDistance = new Vector2(0f, 0f);

        var badgeTextGo = new GameObject("Text", typeof(RectTransform));
        badgeTextGo.transform.SetParent(badgeGo.transform, false);
        var badgeTextRt = badgeTextGo.GetComponent<RectTransform>();
        badgeTextRt.anchorMin = Vector2.zero;
        badgeTextRt.anchorMax = Vector2.one;
        badgeTextRt.offsetMin = new Vector2(8f, 2f);
        badgeTextRt.offsetMax = new Vector2(-8f, -2f);
        _badgeText = badgeTextGo.AddComponent<TextMeshProUGUI>();
        _badgeText.text = "---°";
        _badgeText.fontSize = 11f;
        _badgeText.fontStyle = FontStyles.Bold;
        _badgeText.alignment = TextAlignmentOptions.Center;
        _badgeText.characterSpacing = 4f;
        _badgeText.color = BadgeTextColor;
        _badgeText.raycastTarget = false;
        _badgeText.enableWordWrapping = false;
        var badgeTextGlow = badgeTextGo.AddComponent<Shadow>();
        badgeTextGlow.effectColor = BadgeTextGlowColor;
        badgeTextGlow.effectDistance = new Vector2(0f, 0f);
```

- [ ] **Step 3: Update `LateUpdate` to compute and push the heading.**

Find `LateUpdate` (around line 189). After the existing block that computes `forwardOnPlane.Normalize();` and just before the waypoint loop, add the heading badge update:

```csharp
        // Heading badge — same bearing math the waypoints use, but reduced
        // to a single 0..360 degree number plus a cardinal short-code.
        UpdateHeadingBadge(playerPos, surfaceUp, forwardOnPlane);
```

Add the new method below `LateUpdate`:

```csharp
    void UpdateHeadingBadge(Vector3 playerPos, Vector3 surfaceUp, Vector3 forwardOnPlane)
    {
        if (_badgeText == null) return;
        Vector3 northDir = ComputeSurfaceNorth(playerPos, surfaceUp);
        if (northDir.sqrMagnitude < 0.0001f) return;

        float heading = Vector3.SignedAngle(northDir, forwardOnPlane, surfaceUp);
        // Convert SignedAngle's -180..180 to 0..360, clockwise from north.
        if (heading < 0f) heading += 360f;
        int headingInt = Mathf.RoundToInt(heading) % 360;
        int cardinalIdx = ((int)((heading + 22.5f) / 45f)) % 8;
        if (cardinalIdx < 0) cardinalIdx += 8;
        string cardinalCode = CardinalCodes[cardinalIdx];

        if (headingInt != _lastHeadingShown || cardinalCode != _lastCardinalCode)
        {
            _lastHeadingShown = headingInt;
            _lastCardinalCode = cardinalCode;
            _badgeText.text = $"{headingInt:000}°  {cardinalCode}";
        }
    }
```

- [ ] **Step 4: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/3 - Scripts/UI/CompassHUD.cs"
git commit -m "feat(compass): heading-number badge above the strip"
```

---

## Task 4: Canvas-disabled-until-player fix (main-menu bleed)

**Files:**
- Modify: `Assets/3 - Scripts/UI/CompassHUD.cs`

Stops the compass from flashing on top of the main menu during the scene transition after the user clicks PLAY. Canvas starts disabled; first `LateUpdate` that finds a `PlayerController` enables it.

- [ ] **Step 1: Disable the canvas at the end of `Awake`.**

Find the `Awake` method (around line 86). Replace its body with:

```csharp
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        // Stay hidden until a PlayerController appears in the active scene.
        // EnsureGameplaySingletons creates the CompassHUD synchronously when
        // the player clicks PLAY, so without this gate the canvas would
        // render on the main menu during the brief scene-transition window.
        if (_canvas != null) _canvas.enabled = false;
    }
```

- [ ] **Step 2: Flip the canvas on the first frame the player is found.**

Find `LateUpdate` (around line 189). After the existing block that does:

```csharp
        if (_playerCached == null) _playerCached = FindObjectOfType<PlayerController>();
        if (_cameraCached == null) _cameraCached = Camera.main;
        if (_playerCached == null || _cameraCached == null) return;
```

Add immediately below it:

```csharp
        // First frame the player exists — turn the compass on. One-shot flip.
        if (_canvas != null && !_canvas.enabled) _canvas.enabled = true;
```

- [ ] **Step 3: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 4: Commit.**

```bash
git add "Assets/3 - Scripts/UI/CompassHUD.cs"
git commit -m "fix(compass): hide canvas until PlayerController is found"
```

---

## Task 5: Verification playthrough

**Files:** none modified.

User-driven Editor playtest, ~3 minutes.

- [ ] **Step 1: Main menu — compass hidden.**

Open `Assets/MainMenu.unity`. Click PLAY → NEW GAME (or load a save). The compass should NOT be visible during the main menu screen, including the brief moment between clicking PLAY and the gameplay scene loading.

- [ ] **Step 2: Gameplay — compass visible with all new elements.**

Once the gameplay scene loads, the compass should be visible at top-center, containing:
- A small monospaced badge above the strip: `XXX°  CC` (e.g., `045°  NE`) reflecting your current facing direction.
- The strip itself with cardinal letters N/E/S/W at their bearings.
- Degree numbers (030, 060, 120, 150, 210, 240, 300, 330) between cardinals.
- Tick marks every 10° along the strip — taller every 30°.
- Glowing center tick at the middle of the strip.
- Faded left/right edges.

- [ ] **Step 3: Heading updates correctly.**

Turn the camera with the mouse. The cardinal letters / degree numbers / ticks scroll across the strip as expected. The badge above updates to match: e.g., turn until you're facing roughly E, badge reads `~090°  E`. Confirm the badge wraps cleanly from 359° → 000° when facing north.

- [ ] **Step 4: Save / load round-trip.**

Save from the pause menu, return to main menu, load. The compass:
- Stays hidden on the main menu during load.
- Re-appears in the gameplay scene with all elements (cardinals + numbers + ticks + badge).
- Any pre-existing gameplay waypoints (e.g., tutorial-added) restore correctly.

- [ ] **Step 5: No commit.**

Verification only — nothing to commit. If anything fails, file a follow-up and fix.

---

## Self-review

**1. Spec coverage:**
- Heading badge (Part 1): Task 3. ✓
- Degree-number labels: Task 2 (`SeedDegreeNumberWaypoints` + DegreeNumber Kind handled in Task 1). ✓
- Tick marks every 10° (36 total, major every 30°): Task 2 (`SeedTickWaypoints` + Tick Kind handled in Task 1). ✓
- Cardinal letters, glowing center tick, faded edges, sort order all preserved: Task 1 keeps existing cardinal path; Task 3 doesn't disturb the strip's existing decorations.
- Main-menu bleed fix: Task 4. ✓
- WaypointKind enum + GetSaveState/ApplySaveState Kind gating: Task 1. ✓

**2. Placeholder scan:** None. All code blocks complete; no "TBD"; commit messages literal.

**3. Type consistency:**
- `WaypointKind` enum is used identically across Task 1 (declaration + AddCardinal + GetSaveState + ApplySaveState + BuildWaypointUI switch), Task 2 (DegreeNumber + Tick seed methods set `Kind`), and Task 4 (no Kind references — just canvas plumbing).
- `BearingPosition(float)` helper is defined in Task 2 step 1 and used by AddCardinal (Task 2 step 2 cosmetic refactor), AddDegreeNumber, AddTick — signature consistent.
- `_badgeRT` / `_badgeText` field types match between declaration (Task 3 step 1) and assignment (Task 3 step 2).
- `CardinalCodes[]` array length 8, indexed by `cardinalIdx` modulo 8 — consistent in Task 3.
