# Jetpack HUD Redesign — V1 Classic Gimbal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current jetpack HUD (small bottom-left speed card + scene-side `BoostMeterUI` canvas) with one unified panel: vertical speed tape · 3D gimbal thrust widget (sphere + 3 cyan rings + 6 pole lights) · 3 vertical segmented fuel bars, all in the locked cyan/navy palette shared with `VitalsHUD`/`WaterFillHUD`/`PlayerWallet`.

**Architecture:** All work lives in `GForceHUD.cs` (procedural singleton, auto-created via `RuntimeInitializeOnLoadMethod`). The 3D widget keeps its existing dedicated camera + RenderTexture + "100,000 units up" off-screen trick — only the geometry rendered inside changes. The legacy scene-side `BoostMeterUI` GameObject is left in place (scene references it) but suppressed via a one-line gate; the new HUD owns the boost-meter rendering.

**Tech Stack:** Unity 2022.3 · uGUI · TMP · `LineRenderer` (for the cyan rings) · `Unlit/Color` shader (existing pattern) · `UIPanelSprites` beveled panel/outline (existing helpers).

**Spec:** `docs/superpowers/specs/2026-05-11-jetpack-hud-redesign-design.md`

**Verification model:** Unity projects in this repo have no CLI tests (per `CLAUDE.md` — "all iteration happens in the Editor"). Each task's "test" step is a **compile-clean check + Editor play-mode visual verification**. Specific play-mode procedure given per task.

---

## Task 1 · Replace 3D widget geometry (cube → sphere + rings + poles)

**Files:**
- Modify: `Assets/3 - Scripts/Ship/GForceHUD.cs` — replace `Build3DWidget()`, add `BuildRing()` helper, add `_ringMat` field, update `OnDestroy()`.

The surrounding canvas layout is untouched in this task. After this commit the existing speed card still shows "SPEED 147 m/s" text + the (now-redesigned) 3D widget rendered into the same `RawImage`. Legacy `BoostMeterUI` still visible. Game playable.

- [ ] **Step 1: Add the `_ringMat` field**

Open `Assets/3 - Scripts/Ship/GForceHUD.cs`. Find the existing 3D widget state block (currently around line 61-68):

```csharp
    // 3D widget state.
    GameObject _widgetRoot;
    Camera _indicatorCam;
    Camera _mainCam;
    RenderTexture _thrustRT;
    Material[] _arrowMats;
    Color[] _currentArrowColors;
    RawImage _thrustImage;
```

Add one new field at the end of that block:

```csharp
    Material _ringMat;
```

- [ ] **Step 2: Update `OnDestroy()` to dispose the ring material**

Find the existing `OnDestroy()` method (around lines 88-95):

```csharp
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_thrustRT != null) { _thrustRT.Release(); DestroyImmediate(_thrustRT); }
        if (_widgetRoot != null) DestroyImmediate(_widgetRoot);
        if (_indicatorCam != null && _indicatorCam.gameObject != null) DestroyImmediate(_indicatorCam.gameObject);
        if (_arrowMats != null) foreach (var m in _arrowMats) if (m != null) DestroyImmediate(m);
    }
```

Add one line at the bottom inside the method:

```csharp
        if (_ringMat != null) DestroyImmediate(_ringMat);
```

- [ ] **Step 3: Replace `Build3DWidget()` with the gimbal version**

Find the existing `Build3DWidget()` method (around lines 189-270). Replace its **entire body** with the version below (keep the method signature `void Build3DWidget()`):

```csharp
    void Build3DWidget()
    {
        // Off-screen render target. Unchanged from previous version.
        _thrustRT = new RenderTexture(rtResolution, rtResolution, 16, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _thrustRT.Create();

        _widgetRoot = new GameObject("ThrustIndicator3D_Widget");
        DontDestroyOnLoad(_widgetRoot);
        _widgetRoot.transform.position = new Vector3(0f, 100000f, 0f);

        // Central sphere — origin marker. Reuses CenterColor3D so the central
        // element's color stays consistent with prior widgets.
        var center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        center.name = "Center";
        center.transform.SetParent(_widgetRoot.transform, false);
        center.transform.localScale = Vector3.one * 0.45f;
        DestroyCollider(center);
        center.GetComponent<Renderer>().material = MakeUnlitMat(CenterColor3D);

        // 3 perpendicular rings (LineRenderer × 64 segments each).
        BuildRing("Ring_XY", Quaternion.identity);                    // lies in the XY plane
        BuildRing("Ring_YZ", Quaternion.Euler(0f, 90f, 0f));          // lies in the YZ plane
        BuildRing("Ring_XZ", Quaternion.Euler(90f, 0f, 0f));          // lies in the XZ plane

        // 6 pole-light spheres at the cardinal axes.
        // INDEX ORDER MUST MATCH ReadThrustKeys() — preserves lighting logic.
        //   0 = +Y (Ctrl)      down-thrust pushes you up
        //   1 = +Z (S)         back-thrust pushes you forward
        //   2 = +X (A)         left-strafe pushes you right
        //   3 = -Y (Space)     jetpack pushes you down (exhaust)
        //   4 = -Z (W)         forward-thrust pushes you back (exhaust)
        //   5 = -X (D)         right-strafe pushes you left (exhaust)
        var dirs = new Vector3[]
        {
            Vector3.up,       // 0 +Y
            Vector3.forward,  // 1 +Z
            Vector3.right,    // 2 +X
            Vector3.down,     // 3 -Y
            Vector3.back,     // 4 -Z
            Vector3.left,     // 5 -X
        };
        _arrowMats = new Material[6];
        _currentArrowColors = new Color[6];
        for (int i = 0; i < dirs.Length; i++)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pole.name = "Pole_" + i;
            pole.transform.SetParent(_widgetRoot.transform, false);
            pole.transform.localPosition = dirs[i] * 0.95f;
            pole.transform.localScale = Vector3.one * 0.16f;
            DestroyCollider(pole);
            var mat = MakeUnlitMat(ArrowIdle3D);
            pole.GetComponent<Renderer>().material = mat;
            _arrowMats[i] = mat;
            _currentArrowColors[i] = ArrowIdle3D;
        }

        // Dedicated camera that ONLY renders to the RT. Unchanged from previous version.
        var camGO = new GameObject("ThrustIndicator3D_Camera");
        DontDestroyOnLoad(camGO);
        _indicatorCam = camGO.AddComponent<Camera>();
        _indicatorCam.targetTexture = _thrustRT;
        _indicatorCam.clearFlags = CameraClearFlags.SolidColor;
        _indicatorCam.backgroundColor = RTBgColor;
        _indicatorCam.fieldOfView = 35f;
        _indicatorCam.nearClipPlane = 0.05f;
        _indicatorCam.farClipPlane = 20f;
        _indicatorCam.depth = -50; // before main camera in render order
        _indicatorCam.allowHDR = false;
        _indicatorCam.allowMSAA = true;
        _indicatorCam.useOcclusionCulling = false;
    }
```

- [ ] **Step 4: Add the `BuildRing()` helper**

Immediately below `Build3DWidget()` (before `DestroyCollider`), add this new method:

```csharp
    void BuildRing(string name, Quaternion localRot)
    {
        if (_ringMat == null)
        {
            var shader = Shader.Find("Sprites/Default");
            _ringMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _ringMat.color = LedColor;
        }

        var go = new GameObject(name);
        go.transform.SetParent(_widgetRoot.transform, false);
        go.transform.localRotation = localRot;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.startWidth = 0.025f;
        lr.endWidth   = 0.025f;
        lr.material   = _ringMat;
        lr.startColor = LedColor;
        lr.endColor   = LedColor;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        lr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        const int segs = 64;
        const float radius = 0.9f;
        lr.positionCount = segs;
        for (int i = 0; i < segs; i++)
        {
            float a = (i / (float)segs) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
    }
```

- [ ] **Step 5: Compile-check**

Save the file. In the Unity Editor wait for the recompile (bottom-right activity indicator). Open the Console (`Window > General > Console`) and confirm there are **zero compile errors**.

Expected: clean compile. If you see `'LineRenderer' could not be found` or similar, that's a `using` issue — `LineRenderer` is in `UnityEngine` (no extra `using` needed; the existing file already has `using UnityEngine;` on line 2).

- [ ] **Step 6: Play-mode verification**

In the Editor:
1. Open `Assets/1.6.7.7.7.unity` if not already loaded.
2. Press Play.
3. The HUD should already be hidden because the jetpack isn't unlocked. To unlock it for testing without buying:
   - In the Hierarchy, find the Player.
   - On `PlayerController` component, call `UnlockJetpack()` via Inspector context menu, **or** set `jetpackUnlocked = true` directly via the debug inspector (right-click inspector header → Properties → toggle the private field), **or** use existing cheat code if `Universe.cheatsEnabled` is true (see `CheatCodes.cs`).
4. Confirm the bottom-left card now shows:
   - Speed text at top (unchanged text "0 m/s" or live value).
   - A 3D widget below the text showing: a central sphere, 3 cyan circular rings perpendicular to each other, and 6 small dim-blue spheres at the cardinal axes.
5. Hold each thrust key and confirm the correct pole sphere brightens:
   - **Space** → bottom pole (-Y) brightens
   - **Ctrl** → top pole (+Y) brightens
   - **W** → back pole (-Z, the one toward the camera) brightens
   - **S** → front pole (+Z) brightens
   - **A** → right pole (+X) brightens
   - **D** → left pole (-X) brightens
6. Release keys — pole returns to dim within ~100 ms (existing `dt * 10f` lerp).

If any pole is hidden behind the central sphere when active, move the pole-distance from `0.95f` to `1.05f` in Step 3 and re-test.

If the rings look stair-stepped/jagged at the on-screen size, bump `rtResolution` from `192` to `256` (public field on the component, but defaults are set in code on line 41).

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Ship/GForceHUD.cs"
git commit -m "$(cat <<'EOF'
feat(jetpack-hud): swap 3D widget geometry to gimbal — sphere + 3 cyan rings + 6 pole lights (replaces cube + 6 axis bars). Index order and lighting lerp unchanged.
EOF
)"
```

---

## Task 2 · Rebuild canvas: speed tape · widget · 3 segmented fuel bars

**Files:**
- Modify: `Assets/3 - Scripts/Ship/GForceHUD.cs` — add new color constants and state fields; replace `BuildCanvas()`, `BuildSpeedRow()`, `BuildThrustWidget()`; add `BuildSpeedTape()`, `BuildSegBars()`, `RecolorSegs()`; expand `Update()` to drive the new elements.

After this commit the new panel shows the full layout — speed tape, gimbal widget, and 3 segmented fuel bars. The legacy `BoostMeterUI` is still visible above the panel (it gets suppressed in Task 3); this is a transient one-commit visual duplicate.

- [ ] **Step 1: Add new color constants**

Find the existing color block in `GForceHUD.cs` (around lines 43-52):

```csharp
    static readonly Color CardBgColor     = new Color32(0x0A, 0x18, 0x28, 0xF2);
    static readonly Color CardBorderColor = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color LedColor        = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color HeaderColor     = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color ValueColor      = new Color32(0x7B, 0xE2, 0xFF, 0xFF);
```

Add these four new constants immediately after `ValueColor`:

```csharp
    static readonly Color TrackColor      = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color CellOffColor    = new Color32(0x0F, 0x19, 0x2A, 0xE6);
    static readonly Color CellBorderOff   = new Color32(0x5C, 0xC8, 0xFF, 0x35);
    static readonly Color DimLabelColor   = new Color32(0xEA, 0xF6, 0xFF, 0x73);
```

- [ ] **Step 2: Update `cardWidth` default + add new state fields**

Find the `[Header("Layout ...")]` block (around lines 33-37):

```csharp
    [Header("Layout (1920×1080 reference)")]
    public float bottomOffset = 170f;
    public float leftOffset = 20f;
    public float cardWidth = 200f;
    public float widgetSize = 100f;
```

Update `cardWidth` to `330f`:

```csharp
    public float cardWidth = 330f;
```

Then find the existing state block (around lines 54-68, before the `_ringMat` field added in Task 1):

```csharp
    Canvas _canvas;
    RectTransform _cardRT;
    TMP_Text _speedValue;
    Ship _ship;
    PlayerController _player;
    int _lastShownSpeed = int.MinValue;

    // 3D widget state.
    GameObject _widgetRoot;
    Camera _indicatorCam;
    Camera _mainCam;
    RenderTexture _thrustRT;
    Material[] _arrowMats;
    Color[] _currentArrowColors;
    RawImage _thrustImage;
    Material _ringMat;
```

Remove the `_speedValue` field (the tape replaces it) and add five new fields. The block becomes:

```csharp
    Canvas _canvas;
    RectTransform _cardRT;
    Ship _ship;
    PlayerController _player;
    int _lastShownSpeed = int.MinValue;

    // Speed-tape rows: index 2 is the highlighted current value.
    TextMeshProUGUI[] _tape;

    // Segmented fuel bars (8 segments per column, bottom-up fill).
    Image[] _upSegs;
    Image[] _dnSegs;
    Image[] _dirSegs;
    int _lastUp = -1, _lastDn = -1, _lastDir = -1;

    // 3D widget state.
    GameObject _widgetRoot;
    Camera _indicatorCam;
    Camera _mainCam;
    RenderTexture _thrustRT;
    Material[] _arrowMats;
    Color[] _currentArrowColors;
    RawImage _thrustImage;
    Material _ringMat;
```

- [ ] **Step 3: Replace `BuildCanvas()` with the horizontal-layout version**

Find the existing `BuildCanvas()` method (around lines 289-352). Replace its **entire body** with:

```csharp
    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 25;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var card = NewUI("Card", transform);
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(0f, 0f);
        card.pivot     = new Vector2(0f, 0f);
        card.anchoredPosition = new Vector2(leftOffset, bottomOffset);
        card.sizeDelta = new Vector2(cardWidth, 0f);
        _cardRT = card;

        // Beveled background panel (same sprite as other HUDs).
        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = CardBgColor;
        bg.raycastTarget = false;

        // Border outline.
        var border = NewUI("Border", card);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = CardBorderColor;
        borderImg.raycastTarget = false;
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // Left LED accent stripe (matches VitalsHUD / WaterFillHUD).
        var led = NewUI("Led", card);
        led.anchorMin = new Vector2(0f, 0f);
        led.anchorMax = new Vector2(0f, 1f);
        led.pivot     = new Vector2(0f, 0.5f);
        led.anchoredPosition = new Vector2(9f, 0f);
        led.sizeDelta = new Vector2(3f, -16f);
        led.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var ledImg = led.gameObject.AddComponent<Image>();
        ledImg.color = LedColor;
        ledImg.raycastTarget = false;

        // Horizontal layout: tape | widget | seg-bars.
        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 12f;
        hlg.padding = new RectOffset(26, 14, 12, 12); // left padding clears the LED

        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        BuildSpeedTape(card);
        BuildThrustWidget(card);
        BuildSegBars(card);
    }
```

- [ ] **Step 4: Replace `BuildSpeedRow()` with `BuildSpeedTape()`**

Find the existing `BuildSpeedRow()` method (around lines 354-376). Replace it **entirely** (rename the method too) with:

```csharp
    void BuildSpeedTape(RectTransform parent)
    {
        var col = NewUI("SpeedTape", parent);
        var colLE = col.gameObject.AddComponent<LayoutElement>();
        colLE.preferredWidth = 56f;
        colLE.preferredHeight = widgetSize;

        // Track background.
        var bg = col.gameObject.AddComponent<Image>();
        bg.color = TrackColor;
        bg.raycastTarget = false;

        // M/S wire-tag label peeking out top-left.
        var tag = NewUI("MS_Label", col);
        tag.anchorMin = new Vector2(0f, 1f);
        tag.anchorMax = new Vector2(0f, 1f);
        tag.pivot     = new Vector2(0f, 0.5f);
        tag.anchoredPosition = new Vector2(4f, 0f);
        tag.sizeDelta = new Vector2(30f, 12f);
        tag.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var tagBg = tag.gameObject.AddComponent<Image>();
        tagBg.color = new Color32(0x04, 0x10, 0x1E, 0xFF);
        tagBg.raycastTarget = false;
        var tagText = NewText(tag, "Text", "M/S", 8f, FontStyles.Bold, HeaderColor);
        var tagTextRT = tagText.GetComponent<RectTransform>();
        tagTextRT.anchorMin = Vector2.zero;
        tagTextRT.anchorMax = Vector2.one;
        tagTextRT.offsetMin = Vector2.zero;
        tagTextRT.offsetMax = Vector2.zero;
        tagText.alignment = TextAlignmentOptions.Center;
        tagText.characterSpacing = 2f;

        // 5 stacked rows. Middle row (index 2) is the highlighted current value.
        var stack = NewUI("RowStack", col);
        Stretch(stack);
        var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(2, 6, 14, 6); // top padding clears the wire-tag

        _tape = new TextMeshProUGUI[5];
        for (int i = 0; i < 5; i++)
        {
            bool isCurrent = (i == 2);
            var row = NewUI("TapeRow_" + i, stack);
            var rowLE = row.gameObject.AddComponent<LayoutElement>();
            rowLE.preferredHeight = isCurrent ? 20f : 11f;

            if (isCurrent)
            {
                var fill = row.gameObject.AddComponent<Image>();
                fill.color = new Color(0x5C / 255f, 0xC8 / 255f, 0xFF / 255f, 0.18f);
                fill.raycastTarget = false;
            }

            var t = NewText(row, "Value", "0",
                            isCurrent ? 14f : 9f,
                            isCurrent ? FontStyles.Bold : FontStyles.Normal,
                            isCurrent ? ValueColor : DimLabelColor);
            var tRT = t.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.MidlineRight;
            t.margin = new Vector4(0f, 0f, 6f, 0f); // right padding
            _tape[i] = t;
        }
    }
```

- [ ] **Step 5: Replace `BuildThrustWidget()` to use a `LayoutElement` for the new horizontal layout**

Find the existing `BuildThrustWidget()` method (around lines 378-393). Replace its body with:

```csharp
    void BuildThrustWidget(RectTransform parent)
    {
        var widget = NewUI("ThrustWidget", parent);
        var widgetLE = widget.gameObject.AddComponent<LayoutElement>();
        widgetLE.preferredWidth = widgetSize;
        widgetLE.preferredHeight = widgetSize;
        widgetLE.flexibleWidth = 0f;
        widgetLE.flexibleHeight = 0f;

        _thrustImage = widget.gameObject.AddComponent<RawImage>();
        _thrustImage.texture = _thrustRT;
        _thrustImage.color = Color.white;
        _thrustImage.raycastTarget = false;
    }
```

Difference from old version: added `preferredWidth` and zeroed `flexibleWidth/Height` so the horizontal layout sizes it to a 100 × 100 square instead of stretching it.

- [ ] **Step 6: Add `BuildSegBars()` and `RecolorSegs()`**

Immediately below `BuildThrustWidget()`, add these two methods:

```csharp
    void BuildSegBars(RectTransform parent)
    {
        var col = NewUI("SegBars", parent);
        var colLE = col.gameObject.AddComponent<LayoutElement>();
        colLE.preferredWidth = 80f;
        colLE.preferredHeight = widgetSize;
        var hl = col.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.LowerCenter;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.spacing = 6f;

        _upSegs  = BuildSegColumn(col, "UP");
        _dnSegs  = BuildSegColumn(col, "DN");
        _dirSegs = BuildSegColumn(col, "DIR");
    }

    Image[] BuildSegColumn(RectTransform parent, string labelText)
    {
        const int segCount  = 8;
        const float segH    = 6f;
        const float segGap  = 2f;
        const float lblH    = 10f;

        var wrap = NewUI("Col_" + labelText, parent);
        var wrapLE = wrap.gameObject.AddComponent<LayoutElement>();
        wrapLE.preferredWidth = 14f;
        wrapLE.preferredHeight = widgetSize;
        var vl = wrap.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.LowerCenter;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.spacing = 2f;
        vl.padding = new RectOffset(0, 0, 0, 0);

        // Stack of segments (bottom-first → built top-to-bottom, fill logic reads bottom-up).
        var segs = new Image[segCount];
        for (int i = segCount - 1; i >= 0; i--)
        {
            var seg = NewUI("Seg_" + i, wrap);
            var segLE = seg.gameObject.AddComponent<LayoutElement>();
            segLE.preferredHeight = segH;
            var img = seg.gameObject.AddComponent<Image>();
            img.color = CellOffColor;
            img.raycastTarget = false;
            segs[i] = img;
        }

        // Label below the column.
        var lblRT = NewUI("Lbl", wrap);
        lblRT.gameObject.AddComponent<LayoutElement>().preferredHeight = lblH;
        var lbl = NewText(lblRT, "Text", labelText, 8f, FontStyles.Bold, HeaderColor);
        var lblTextRT = lbl.GetComponent<RectTransform>();
        lblTextRT.anchorMin = Vector2.zero;
        lblTextRT.anchorMax = Vector2.one;
        lblTextRT.offsetMin = Vector2.zero;
        lblTextRT.offsetMax = Vector2.zero;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.characterSpacing = 1f;

        return segs;
    }

    static void RecolorSegs(Image[] segs, int litCount)
    {
        for (int i = 0; i < segs.Length; i++)
            segs[i].color = (i < litCount) ? LedColor : CellOffColor;
    }
```

- [ ] **Step 7: Update `Update()` to drive the tape + segs**

Find the existing `Update()` method (around lines 97-129). Replace its **entire body** with:

```csharp
    void Update()
    {
        // HUD visibility gate — only show when the jetpack is unlocked.
        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        bool show = _player != null && _player.JetpackUnlocked;
        if (_canvas != null && _canvas.enabled != show) _canvas.enabled = show;
        if (_indicatorCam != null && _indicatorCam.enabled != show) _indicatorCam.enabled = show;
        if (!show) return;

        // Speed tape — five rows update together on integer-speed change.
        Vector3 vel = ReadActiveVelocity();
        int speed = Mathf.RoundToInt(vel.magnitude);
        if (speed != _lastShownSpeed && _tape != null)
        {
            _lastShownSpeed = speed;
            _tape[0].text = (speed + 50).ToString();
            _tape[1].text = (speed + 25).ToString();
            _tape[2].text = speed.ToString();
            _tape[3].text = Mathf.Max(0, speed - 25).ToString();
            _tape[4].text = Mathf.Max(0, speed - 50).ToString();
        }

        // Segmented fuel bars — change-detected updates.
        if (_upSegs != null && _dnSegs != null && _dirSegs != null)
        {
            int upLit  = Mathf.Clamp(Mathf.RoundToInt(_player.JetpackFuelPercent           * 8f), 0, 8);
            int dnLit  = Mathf.Clamp(Mathf.RoundToInt(_player.DownThrustFuelPercent        * 8f), 0, 8);
            int dirLit = Mathf.Clamp(Mathf.RoundToInt(_player.DirectionalThrustFuelPercent * 8f), 0, 8);
            if (upLit  != _lastUp)  { _lastUp  = upLit;  RecolorSegs(_upSegs,  upLit);  }
            if (dnLit  != _lastDn)  { _lastDn  = dnLit;  RecolorSegs(_dnSegs,  dnLit);  }
            if (dirLit != _lastDir) { _lastDir = dirLit; RecolorSegs(_dirSegs, dirLit); }
        }

        // Pole light fade (unchanged mechanism, now lerping pole-sphere materials).
        if (_arrowMats != null)
        {
            bool[] active = ReadThrustKeys();
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _arrowMats.Length; i++)
            {
                Color target = active[i] ? ArrowActive3D : ArrowIdle3D;
                _currentArrowColors[i] = Color.Lerp(_currentArrowColors[i], target, dt * 10f);
                _arrowMats[i].color = _currentArrowColors[i];
            }
        }
    }
```

- [ ] **Step 8: Compile-check**

Save the file. Wait for recompile. Open Console. **Zero compile errors expected.**

Common errors and fixes:
- `'_speedValue' does not exist` somewhere → you missed deleting a reference in `Update()`. The new `Update()` replaces the old `_speedValue.text = …` block — make sure you replaced the whole method body.
- `TextMeshProUGUI` not found → `using TMPro;` is already at the top of the file (line 1).

- [ ] **Step 9: Play-mode verification**

1. Press Play. Unlock jetpack as in Task 1 Step 6.
2. Confirm the new panel at bottom-left:
   - Vertical tape on the left with five numbers stacked, middle one bold and slightly larger with a faint cyan highlight strip behind it.
   - Tape numbers update as you move — middle = current speed (whole m/s), neighbors = ±25, ±50.
   - 3D gimbal in the middle (as before).
   - Three vertical segmented bars on the right, each 8 segments tall, labeled `UP` / `DN` / `DIR` below. All bars start fully lit.
3. Hold Space briefly to drain the jetpack — `UP` column drains from the top down. Release → refills from the top down after the refuel delay.
4. Hold Ctrl briefly — `DN` drains.
5. Jump into the air (Space) then hold Shift + WASD — `DIR` drains.
6. Verify the legacy `BoostMeterUI` is still showing somewhere (it'll be hidden in Task 3) — this is the expected transient duplicate.

If text is misaligned, check the `vlg.padding`/`margin` values; if columns overlap or wrap, check `colLE.preferredWidth` values.

- [ ] **Step 10: Commit**

```bash
git add "Assets/3 - Scripts/Ship/GForceHUD.cs"
git commit -m "$(cat <<'EOF'
feat(jetpack-hud): rebuild panel layout — vertical speed tape + gimbal widget + 3 segmented fuel bars in one horizontal card. Legacy BoostMeterUI suppression follows in next commit.
EOF
)"
```

---

## Task 3 · Suppress the legacy `BoostMeterUI` canvas

**Files:**
- Modify: `Assets/3 - Scripts/Ship/GForceHUD.cs` — add a one-line `OwnsBoostMeter` property.
- Modify: `Assets/3 - Scripts/Ship/BoostMeterUI.cs` — add an early-return gate at the top of `Update()`.

- [ ] **Step 1: Add `OwnsBoostMeter` to `GForceHUD`**

Open `Assets/3 - Scripts/Ship/GForceHUD.cs`. Find a clean location at the top of the class body (e.g., immediately after the `Instance` property at line 31). Add:

```csharp
    /// <summary>
    /// True when this HUD owns the boost-meter rendering — the legacy scene-side
    /// BoostMeterUI suppresses itself when this is true.
    /// </summary>
    public bool OwnsBoostMeter => true;
```

- [ ] **Step 2: Add the gate to `BoostMeterUI.Update()`**

Open `Assets/3 - Scripts/Ship/BoostMeterUI.cs`. Find the existing `Update()` method (currently lines 42-61):

```csharp
    void Update()
    {
        if (player == null)
            player = FindObjectOfType<PlayerController>(true);
        if (player == null) return;

        // Hide the entire HUD until the jetpack has been purchased from Alien7.
        bool show = player.JetpackUnlocked;
        if (_cg != null)
        {
            _cg.alpha = show ? 1f : 0f;
            _cg.blocksRaycasts = show;
            _cg.interactable = show;
        }
        if (!show) return;

        upThrustFill.fillAmount = player.JetpackFuelPercent;
        downThrustFill.fillAmount = player.DownThrustFuelPercent;
        if (dirThrustFill) dirThrustFill.fillAmount = player.DirectionalThrustFuelPercent;
    }
```

Insert these lines at the very top of the method body (before the `player` null-check):

```csharp
        // If the new GForceHUD owns the boost-meter rendering, suppress this legacy canvas.
        if (GForceHUD.Instance != null && GForceHUD.Instance.OwnsBoostMeter)
        {
            if (_cg != null)
            {
                _cg.alpha = 0f;
                _cg.blocksRaycasts = false;
                _cg.interactable = false;
            }
            return;
        }
```

So the full method becomes:

```csharp
    void Update()
    {
        // If the new GForceHUD owns the boost-meter rendering, suppress this legacy canvas.
        if (GForceHUD.Instance != null && GForceHUD.Instance.OwnsBoostMeter)
        {
            if (_cg != null)
            {
                _cg.alpha = 0f;
                _cg.blocksRaycasts = false;
                _cg.interactable = false;
            }
            return;
        }

        if (player == null)
            player = FindObjectOfType<PlayerController>(true);
        if (player == null) return;

        bool show = player.JetpackUnlocked;
        if (_cg != null)
        {
            _cg.alpha = show ? 1f : 0f;
            _cg.blocksRaycasts = show;
            _cg.interactable = show;
        }
        if (!show) return;

        upThrustFill.fillAmount = player.JetpackFuelPercent;
        downThrustFill.fillAmount = player.DownThrustFuelPercent;
        if (dirThrustFill) dirThrustFill.fillAmount = player.DirectionalThrustFuelPercent;
    }
```

- [ ] **Step 3: Compile-check**

Save both files. Wait for recompile. Console: zero errors.

- [ ] **Step 4: Play-mode verification**

1. Press Play. Unlock jetpack as before.
2. Confirm the legacy `BoostMeterUI` (the three horizontal fill bars in the scene) is now **invisible** — only the new combined panel is visible.
3. The new panel functions exactly as in Task 2 Step 9 (tape, gimbal, segmented bars).
4. Try saving (pause menu → save) and loading (main menu → pick the save) — confirm:
   - The legacy canvas stays hidden after load.
   - Jetpack stays unlocked.
   - Fuel values restore correctly.

(No save schema changed; this is just verifying no regression in load.)

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Ship/GForceHUD.cs" "Assets/3 - Scripts/Ship/BoostMeterUI.cs"
git commit -m "$(cat <<'EOF'
feat(jetpack-hud): suppress legacy BoostMeterUI canvas — GForceHUD now owns boost-meter rendering via segmented bars in the new panel.
EOF
)"
```

---

## Final acceptance walkthrough

After Task 3 is committed, do one end-to-end check:

1. Load `Assets/1.6.7.7.7.unity`, press Play, get the player to Alien7, buy the jetpack with the goods-vendor flow.
2. Watch the panel appear at bottom-left after purchase.
3. Verify the four behaviors from the spec's acceptance criteria:
   - **Pressing space (jetpack)** lights the bottom pole on the gimbal AND drains the `UP` column.
   - **Pressing Ctrl (down-thrust)** lights the top pole AND drains the `DN` column.
   - **Airborne + Shift + WASD** lights the appropriate horizontal poles AND drains the `DIR` column.
   - Speed tape shows current m/s in the middle row, ±25 / ±50 above/below.
4. Confirm colors match `VitalsHUD` (top-left) at a glance — same navy + cyan family.
5. Quit, exit Play mode, no console warnings/errors.

If any step fails, the failing task can be re-opened and iterated without rewriting earlier tasks.
