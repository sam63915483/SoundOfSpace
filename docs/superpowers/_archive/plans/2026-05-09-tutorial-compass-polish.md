# Tutorial UI + Compass Visual Polish — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current rounded translucent tutorial pill and the flat black compass bar with a coherent angular sci-fi UI language (cyan accents, beveled silhouettes, faded edges), matching the visual mockups approved during brainstorming (`docs/superpowers/specs/2026-05-09-tutorial-compass-polish-design.md`).

**Architecture:** Three self-contained changes to existing files: (1) `TutorialUI.cs` rewritten to render a beveled pill with LED accent bar and inline sub-line; (2) `CompassHUD.cs` rewritten to render a faded-edge cyan strip with cardinal direction letters; (3) `TutorialGate.cs` extended with a `PromptGlyphs.Maybe()` helper plus a runtime TMP `SpriteAsset` for keycap glyphs. No public API changes; no caller modifications.

**Tech Stack:** Unity 2022.3, C#, TextMeshPro, procedurally generated `Texture2D` sprites (no shader changes, no new prefab/scene assets).

**TDD note:** This project has no automated test harness — CLAUDE.md states "No CLI build/test commands — all iteration happens in the Editor." Each task uses Unity's compile-error API as its automated verification (`mcp__coplay-mcp__check_compile_errors`), with the user playtesting the visual outcome between tasks. Steps that show code blocks **must be applied verbatim** unless flagged otherwise.

---

## File Structure

| File | Responsibility | Status |
|---|---|---|
| `Assets/3 - Scripts/Tutorial/TutorialUI.cs` | Renders the tutorial pill HUD. Owns canvas build, slide animations, typewriter, sub-line, keycap atlas. | Modify (full rewrite of `BuildCanvas`, `BorderPulse`, sprite generators; new keycap atlas + sprite asset) |
| `Assets/3 - Scripts/UI/CompassHUD.cs` | Renders the top-center compass strip with bearing-projected waypoints. | Modify (new strip background sprite, cyan recolor, cardinal letters, dimension shrink) |
| `Assets/3 - Scripts/Tutorial/TutorialGate.cs` | Static `PromptGlyphs` class returning input-source-dependent key labels. | Modify (add `Maybe()` helper; wrap 12 properties to emit `<sprite>` when applicable) |

No new files. All existing public APIs preserved exactly.

---

## Task 1: Tutorial pill — beveled angular sci-fi rewrite

Rewrite the visual layer of `TutorialUI.cs` to match style A from the design spec: top-right anchor, 360px-wide pill with beveled top-left + bottom-right corners, 3px cyan LED accent bar on the left edge, `// PROMPT` header tag, body text, and an inline sub-line (✓ Press TAB to continue) that lives INSIDE the pill body. Public API unchanged.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/TutorialUI.cs` (full rewrite of `BuildCanvas`, `BorderPulse`, palette block, `Make*` sprite generators; preserve all public methods/fields and animation/coroutine internals)

- [ ] **Step 1: Read the current file in full**

Run: read `Assets/3 - Scripts/Tutorial/TutorialUI.cs` end-to-end. The current file is ~700 lines and already implements a top-right anchored pill with cyan accent bar — this task tightens the visuals (beveled silhouette, smaller width, header tag, sub-line moved inside the pill).

- [ ] **Step 2: Replace the palette block**

Find the `// ── Palette ──` block (around line 67–95 currently). Replace with:

```csharp
// ── Palette ────────────────────────────────────────────────────────────
// Slight blue-tinted dark for sci-fi character (not pure black).
static readonly Color PillBgTopColor    = new Color32(0x08, 0x12, 0x20, 0xE0); // top of vertical gradient
static readonly Color PillBgBottomColor = new Color32(0x0A, 0x18, 0x28, 0xEB); // bottom — slightly more opaque
// Cyan-tinted border, follows the beveled silhouette.
static readonly Color PillBorderColor   = new Color32(0x78, 0xC8, 0xFF, 0x73); // 45% cyan-white
// LED indicator on the left edge — pulses dim ↔ bright via BorderPulse.
static readonly Color AccentColor       = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
static readonly Color AccentColorDim    = new Color32(0x5C, 0xC8, 0xFF, 0xB3);
static readonly Color AccentGlowColor   = new Color32(0x60, 0xC8, 0xFF, 0x30);
// Header tag (// PROMPT) — small, uppercase, cyan.
static readonly Color HeaderTagColor    = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
// Tip body — soft cool white with cyan glow.
static readonly Color TipColor          = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
static readonly Color TipGlowColor      = new Color(0.38f, 0.78f, 1f, 0.45f);
// Sub-line — soft green, brighter than before so it pops at 11px.
static readonly Color CompletedColor    = new Color32(0x88, 0xDC, 0xAA, 0xFF);
static readonly Color CheckColor        = new Color32(0x88, 0xDC, 0xAA, 0xFF);
static readonly Color CheckGlowColor    = new Color(0.30f, 0.92f, 0.45f, 0.55f);
```

Delete the old `PillBgColor`, `PillBorderDim`, `PillBorderBright`, `PillTopHighlight` constants — they're replaced by the new set above.

- [ ] **Step 3: Replace the inspector layout fields**

Find the `[Header("Layout (1920x1080 reference)")]` block. Replace with:

```csharp
[Header("Layout (1920x1080 reference)")]
[Tooltip("Distance from the right edge of the screen to the right edge of the pill.")]
public float rightMargin = 60f;
[Tooltip("Distance from the top of the screen to the top of the pill.")]
public float topMargin = 80f;
[Tooltip("Fixed width of the pill. Tip text wraps to two lines if it exceeds this.")]
public float pillWidth = 360f;
[Tooltip("Diagonal cut on the top-left and bottom-right corners (pixels).")]
public float bevelSize = 14f;
[Tooltip("Vertical pixels the pill slides down from when first revealed.")]
public float slideOffset = 40f;
```

(The width drops 540 → 360 and a new `bevelSize` field is added.)

- [ ] **Step 4: Update field declarations to match new layout**

In the `// ── Internal state ──` block, add a new field for the header text and remove the unused `pillTopHighlight` field:

```csharp
TextMeshProUGUI headerTagText; // // PROMPT label above tip body
```

Delete the line `Image pillTopHighlight;` if present — the top-edge highlight is removed (the beveled silhouette already does the framing).

- [ ] **Step 5: Replace the static sprite cache section**

Replace the `static Sprite roundedSprite ... static Sprite horizontalSheenSprite;` block with:

```csharp
static Sprite beveledPanelSprite;
static Sprite beveledOutlineSprite;
static Sprite accentBarSprite;
static Sprite checkSprite;
static Sprite horizontalSheenSprite;  // kept for the LED halo behind the accent bar
```

Delete `roundedSprite`, `outlineSprite`, `hudPanelSprite` — replaced by the beveled equivalents.

- [ ] **Step 6: Add the new beveled sprite generators**

Replace the existing `MakeRoundedHudPanelTexture` function with two new generators. Add at the end of the procedural sprite section:

```csharp
// Tutorial pill silhouette — rectangle with the top-left and bottom-right
// corners cut by a diagonal of `bevel` pixels. Optional vertical gradient
// (top alpha lower than bottom) bakes in the depth shading.
static Texture2D MakeBeveledPanelTexture(int size, int bevel, bool verticalGradient)
{
    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;
    var pixels = new Color[size * size];
    int s = size - 1;
    for (int y = 0; y < size; y++)
    {
        // Vertical gradient: top of texture (y=size-1) is fully visible,
        // bottom slightly less. (Tinted by Image color at runtime.)
        float v = (float)y / s;
        float vAlpha = verticalGradient ? Mathf.Lerp(0.85f, 1.0f, v) : 1.0f;
        for (int x = 0; x < size; x++)
        {
            // Inside the rectangle by default. Carve out the beveled corners.
            // Top-left bevel: pixels where x + (size - 1 - y) < bevel.
            // Bottom-right bevel: pixels where (size - 1 - x) + y < bevel.
            int distTL = x + (s - y);
            int distBR = (s - x) + y;
            float a = 1f;
            if (distTL < bevel) a = Mathf.Clamp01(distTL - (bevel - 1) + 0.5f);
            else if (distBR < bevel) a = Mathf.Clamp01(distBR - (bevel - 1) + 0.5f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, a * vAlpha);
        }
    }
    tex.SetPixels(pixels);
    tex.Apply();
    return tex;
}

// Hollow outline matching MakeBeveledPanelTexture. Outer mask minus inner
// (smaller, inset by `thickness`) mask. The inner uses a smaller bevel so
// the outline corners stay parallel.
static Texture2D MakeBeveledOutlineTexture(int size, int bevel, int thickness)
{
    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;
    var pixels = new Color[size * size];
    int s = size - 1;
    int innerBevel = Mathf.Max(0, bevel - thickness);
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            // Outer mask: full beveled rect.
            int distTL = x + (s - y);
            int distBR = (s - x) + y;
            float outerA = 1f;
            if (distTL < bevel) outerA = Mathf.Clamp01(distTL - (bevel - 1) + 0.5f);
            else if (distBR < bevel) outerA = Mathf.Clamp01(distBR - (bevel - 1) + 0.5f);

            // Inner mask: same but inset by `thickness` on every edge.
            int ix = x - thickness;
            int iy = y - thickness;
            int innerSize = size - 2 * thickness;
            float innerA = 0f;
            if (ix >= 0 && iy >= 0 && ix < innerSize && iy < innerSize)
            {
                int innerS = innerSize - 1;
                int iDistTL = ix + (innerS - iy);
                int iDistBR = (innerS - ix) + iy;
                innerA = 1f;
                if (iDistTL < innerBevel) innerA = Mathf.Clamp01(iDistTL - (innerBevel - 1) + 0.5f);
                else if (iDistBR < innerBevel) innerA = Mathf.Clamp01(iDistBR - (innerBevel - 1) + 0.5f);
            }
            float ringA = Mathf.Clamp01(outerA - innerA);
            pixels[y * size + x] = new Color(1f, 1f, 1f, ringA);
        }
    }
    tex.SetPixels(pixels);
    tex.Apply();
    return tex;
}
```

Delete the now-unused `MakeRoundedHudPanelTexture` function.

- [ ] **Step 7: Replace the sprite-getter functions for the pill**

Find `GetRoundedSprite()`, `GetOutlineSprite()`, `GetHudPanelSprite()`. Replace with:

```csharp
static Sprite GetBeveledPanelSprite()
{
    if (beveledPanelSprite != null) return beveledPanelSprite;
    var tex = MakeBeveledPanelTexture(64, 14, true);
    // 9-slice borders sized to keep the bevel cuts entirely in the corner cells.
    beveledPanelSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                       100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
    beveledPanelSprite.name = "TutorialBeveledPanel";
    return beveledPanelSprite;
}

static Sprite GetBeveledOutlineSprite()
{
    if (beveledOutlineSprite != null) return beveledOutlineSprite;
    var tex = MakeBeveledOutlineTexture(64, 14, 2);
    beveledOutlineSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                         100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
    beveledOutlineSprite.name = "TutorialBeveledOutline";
    return beveledOutlineSprite;
}
```

Keep `GetAccentBarSprite()`, `GetCheckSprite()`, `GetHorizontalSheenSprite()` as-is.

- [ ] **Step 8: Rewrite the BuildCanvas method**

Replace the entire existing `BuildCanvas()` method (and the `Stretch()` helper if needed — keep that one as-is) with the new layout. The pillRoot now has VLG containing pill on top and (later) sub-line below; the pill itself stacks header tag + body text + inline sub-line.

```csharp
void BuildCanvas()
{
    var canvas = gameObject.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 500;
    _canvas = canvas;
    var scaler = gameObject.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight = 0.5f;
    gameObject.AddComponent<GraphicRaycaster>();
    group = gameObject.AddComponent<CanvasGroup>();
    group.interactable = false;
    group.blocksRaycasts = false;

    // ── PromptGroup (the slider, top-right anchored) ────────────────
    pillRoot = NewUI("PromptGroup", transform);
    pillRoot.anchorMin = pillRoot.anchorMax = new Vector2(1f, 1f);
    pillRoot.pivot = new Vector2(1f, 1f);
    pillRoot.anchoredPosition = RestPos();
    pillRoot.sizeDelta = new Vector2(pillWidth, 0f);

    var rootVlg = pillRoot.gameObject.AddComponent<VerticalLayoutGroup>();
    rootVlg.childAlignment = TextAnchor.UpperRight;
    rootVlg.childControlWidth = true;
    rootVlg.childControlHeight = true;
    rootVlg.childForceExpandWidth = true;
    rootVlg.childForceExpandHeight = false;
    rootVlg.spacing = 0f;
    rootVlg.padding = new RectOffset(0, 0, 0, 0);

    var rootFitter = pillRoot.gameObject.AddComponent<ContentSizeFitter>();
    rootFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    rootFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

    // ── Pill ─────────────────────────────────────────────────────────
    pillRect = NewUI("Pill", pillRoot);
    pillRect.anchorMin = new Vector2(0f, 0f);
    pillRect.anchorMax = new Vector2(1f, 0f);
    pillRect.pivot = new Vector2(0.5f, 1f);

    pillBg = pillRect.gameObject.AddComponent<Image>();
    pillBg.sprite = GetBeveledPanelSprite();
    pillBg.type = Image.Type.Sliced;
    pillBg.color = PillBgBottomColor; // tinted; gradient handled inside the texture
    pillBg.raycastTarget = false;

    // LED halo behind the accent bar — soft cyan glow that bleeds inward.
    var accentGlowRT = NewUI("AccentGlow", pillRect);
    accentGlowRT.anchorMin = new Vector2(0f, 0f);
    accentGlowRT.anchorMax = new Vector2(0f, 1f);
    accentGlowRT.pivot = new Vector2(0f, 0.5f);
    accentGlowRT.anchoredPosition = new Vector2(2f, 0f);
    accentGlowRT.sizeDelta = new Vector2(20f, -10f);
    var accentGlow = accentGlowRT.gameObject.AddComponent<Image>();
    accentGlow.sprite = GetHorizontalSheenSprite();
    accentGlow.color = AccentGlowColor;
    accentGlow.raycastTarget = false;

    // The LED bar itself — vertical strip, 3px wide, gradient cyan.
    var accentRT = NewUI("AccentBar", pillRect);
    accentRT.anchorMin = new Vector2(0f, 0f);
    accentRT.anchorMax = new Vector2(0f, 1f);
    accentRT.pivot = new Vector2(0f, 0.5f);
    accentRT.anchoredPosition = new Vector2(8f, 0f);
    accentRT.sizeDelta = new Vector2(3f, -16f);
    accentBar = accentRT.gameObject.AddComponent<Image>();
    accentBar.sprite = GetAccentBarSprite();
    accentBar.color = AccentColor;
    accentBar.raycastTarget = false;

    // Border outline drawn on top of the body + accents.
    var border = NewUI("Border", pillRect);
    Stretch(border, 0f, 0f, 0f, 0f);
    pillBorder = border.gameObject.AddComponent<Image>();
    pillBorder.sprite = GetBeveledOutlineSprite();
    pillBorder.type = Image.Type.Sliced;
    pillBorder.color = PillBorderColor;
    pillBorder.raycastTarget = false;

    // Pill content stack: header tag → body → inline sub-line.
    var pillVlg = pillRect.gameObject.AddComponent<VerticalLayoutGroup>();
    pillVlg.childAlignment = TextAnchor.MiddleLeft;
    pillVlg.childControlWidth = true;
    pillVlg.childControlHeight = true;
    pillVlg.childForceExpandWidth = true;
    pillVlg.childForceExpandHeight = false;
    pillVlg.spacing = 4f;
    pillVlg.padding = new RectOffset(22, 18, 12, 12); // L,R,T,B — left padding clears the LED bar

    var pillFitter = pillRect.gameObject.AddComponent<ContentSizeFitter>();
    pillFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    pillFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

    // Header tag — small uppercase "// PROMPT".
    headerTagText = NewText(pillRect, "Header", "// PROMPT", 9f, FontStyles.Bold | FontStyles.UpperCase, HeaderTagColor);
    headerTagText.alignment = TextAlignmentOptions.MidlineLeft;
    headerTagText.characterSpacing = 6f;

    // Body text — the live tip from TutorialStep.Tip.
    tipText = NewText(pillRect, "Tip", "", 14f, FontStyles.Bold, TipColor);
    tipText.alignment = TextAlignmentOptions.MidlineLeft;
    tipText.lineSpacing = 4f;
    tipText.characterSpacing = 1f;
    tipText.enableWordWrapping = true;
    var tipGlow = tipText.gameObject.AddComponent<Shadow>();
    tipGlow.effectColor = TipGlowColor;
    tipGlow.effectDistance = new Vector2(0f, 0f);
    var tipShadow = tipText.gameObject.AddComponent<Shadow>();
    tipShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
    tipShadow.effectDistance = new Vector2(0f, -2f);

    // Sub-line — INSIDE the pill body now (was a sibling). Hidden until MarkComplete.
    completedRow = NewUI("CompletedRow", pillRect);
    completedRow.gameObject.SetActive(false);
    var rowLayout = completedRow.gameObject.AddComponent<HorizontalLayoutGroup>();
    rowLayout.childAlignment = TextAnchor.MiddleLeft;
    rowLayout.childControlWidth = true;
    rowLayout.childControlHeight = true;
    rowLayout.childForceExpandWidth = false;
    rowLayout.childForceExpandHeight = false;
    rowLayout.spacing = 6f;
    rowLayout.padding = new RectOffset(0, 0, 4, 0);

    var rowFitter = completedRow.gameObject.AddComponent<ContentSizeFitter>();
    rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    rowFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

    var checkRT = NewUI("Check", completedRow);
    var checkLE = checkRT.gameObject.AddComponent<LayoutElement>();
    checkLE.preferredWidth = 14f;
    checkLE.preferredHeight = 14f;
    checkLE.flexibleWidth = 0f;
    checkLE.flexibleHeight = 0f;
    var checkImg = checkRT.gameObject.AddComponent<Image>();
    checkImg.sprite = GetCheckSprite();
    checkImg.color = CheckColor;
    checkImg.raycastTarget = false;
    var checkGlow = checkRT.gameObject.AddComponent<Shadow>();
    checkGlow.effectColor = CheckGlowColor;
    checkGlow.effectDistance = new Vector2(0f, 0f);

    completedText = NewText(completedRow, "Label", "", 11f, FontStyles.Bold, CompletedColor);
    completedText.alignment = TextAlignmentOptions.MidlineLeft;
    completedText.characterSpacing = 1f;
    var labelLE = completedText.gameObject.AddComponent<LayoutElement>();
    labelLE.flexibleWidth = 0f;
    var labelShadow = completedText.gameObject.AddComponent<Shadow>();
    labelShadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
    labelShadow.effectDistance = new Vector2(0f, -1f);
}
```

- [ ] **Step 9: Repoint BorderPulse from the border to the LED bar**

Find `IEnumerator BorderPulse()`. Verify the body already pulses the `accentBar` color between dim and bright (it should from the prior iteration). If it pulses `pillBorder` instead, change the lerp target to `accentBar`. Final body should look like:

```csharp
IEnumerator BorderPulse()
{
    while (this != null)
    {
        float t = (Mathf.Sin(Time.unscaledTime * 1.6f) + 1f) * 0.5f;
        if (accentBar != null)
            accentBar.color = Color.Lerp(AccentColorDim, AccentColor, t);

        if (completedText != null && stepIsComplete && !_autoSkipDeferred && completedRevealRoutine == null)
        {
            bool inOtherUI = (PlayerController.isInDialogue || PlayerController.isMapOpen) && !_ignoreModalGate;
            if (_allowAutoSkip && !inOtherUI && !_autoSkipFired && _autoSkipRemaining > 0f)
            {
                _autoSkipRemaining -= Time.unscaledDeltaTime;
                if (_autoSkipRemaining <= 0f)
                {
                    _autoSkipRemaining = 0f;
                    _autoSkipFired = true;
                }
            }
            completedText.text = $"Press {PromptGlyphs.AdvanceTip} to continue";
            completedText.maxVisibleCharacters = int.MaxValue;
        }
        yield return null;
    }
}
```

- [ ] **Step 10: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

If errors appear:
- "MakeRoundedHudPanelTexture is referenced" → grep the file for stale references and remove them
- "pillTopHighlight is referenced" → grep and remove (BorderPulse should no longer touch it)
- Any sprite-getter mismatch (`GetHudPanelSprite`, `GetOutlineSprite`, `GetRoundedSprite` referenced) → replace each call site with `GetBeveledPanelSprite()` or `GetBeveledOutlineSprite()`

- [ ] **Step 11: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/TutorialUI.cs"
git commit -m "$(cat <<'EOF'
feat(tutorial-ui): beveled angular sci-fi pill

Rewrites TutorialUI's visual layer to match design spec style A:
beveled top-left + bottom-right corners, 360px width (down from 540),
'// PROMPT' header tag above the body text, sub-line moved inside the
pill instead of as a separate row below. Pulsing cyan LED accent bar
on the left edge (BorderPulse repointed from the border outline to
the LED bar). All public API and animation behavior preserved.

See docs/superpowers/specs/2026-05-09-tutorial-compass-polish-design.md.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Compass — sci-fi Skyrim rewrite

Replace the flat black 800×60 compass strip with a 560×36 faded-edge cyan strip: alpha gradient at the left/right ends, top/bottom cyan sheen lines, glowing center tick, cyan triangle waypoints with cool-white labels, and four cardinal direction letters (N/E/S/W) projected at fixed bearings via the same waypoint mechanism.

**Files:**
- Modify: `Assets/3 - Scripts/UI/CompassHUD.cs` (`BuildCanvas`, `BuildWaypointUI`, `MakeDownTriangleTexture` retained, `LateUpdate` extended for cardinal-letter waypoints, new sprite generators, default field values)

- [ ] **Step 1: Read the current file in full**

Run: read `Assets/3 - Scripts/UI/CompassHUD.cs` (~425 lines). The bearing-projection math in `LateUpdate` (lines 181–254) is unchanged — only visual rendering and a small `Awake`-time call to seed cardinal waypoints are added.

- [ ] **Step 2: Update default inspector dimensions**

Find the `[Header("Strip layout (canvas-reference units, 1920×1080)")]` block at the top of the class. Change the defaults:

```csharp
[Header("Strip layout (canvas-reference units, 1920×1080)")]
[Tooltip("Width of the compass strip in canvas units.")]
public float stripWidth = 560f;        // was 800
[Tooltip("Height of the compass strip.")]
public float stripHeight = 36f;        // was 60
[Tooltip("Top margin from the screen top edge.")]
public float topMargin = 32f;          // was 40
```

(Inspector overrides on the existing scene asset — if any — will continue to take precedence.)

- [ ] **Step 3: Add palette constants at the top of the class body**

Insert after the field declarations (just before `static Sprite _defaultMarker;`):

```csharp
// ── Palette ────────────────────────────────────────────────────────────
static readonly Color StripBgColor      = new Color32(0x0A, 0x18, 0x28, 0xC8); // dark navy, 78%
static readonly Color StripSheenColor   = new Color32(0x5C, 0xC8, 0xFF, 0x8C); // 1px cyan edge highlight
static readonly Color CenterTickColor   = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
static readonly Color CardinalColor     = new Color32(0x78, 0xC8, 0xFF, 0xB3); // 70% cyan-white
static readonly Color HalfStepColor     = new Color32(0x78, 0xC8, 0xFF, 0x80); // dimmer "·" ticks
static readonly Color MarkerIconColor   = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
static readonly Color MarkerLabelColor  = new Color32(0xC8, 0xEA, 0xFF, 0xFF);
static readonly Color MarkerGlowColor   = new Color(0.36f, 0.78f, 1f, 0.55f);
```

- [ ] **Step 4: Add the new procedural sprite generators**

Insert after the existing `MakeDownTriangleTexture` function:

```csharp
// Compass strip background — opaque in the middle, alpha gradient that
// fades to 0 over the outer `fadeFraction` of the width on each side.
static Sprite _stripBgSprite;
static Sprite GetStripBgSprite()
{
    if (_stripBgSprite != null) return _stripBgSprite;
    var tex = MakeFadedBarTexture(128, 16, 0.18f);
    // 9-slice borders sized so the fade lives inside the corner cells —
    // middle stretches, edges keep the gradient shape.
    _stripBgSprite = Sprite.Create(tex, new Rect(0, 0, 128, 16), new Vector2(0.5f, 0.5f),
                                   100f, 0u, SpriteMeshType.FullRect, new Vector4(28, 0, 28, 0));
    _stripBgSprite.name = "CompassStripBg";
    return _stripBgSprite;
}

// Top/bottom sheen — 1D horizontal gradient, peaks in the middle, fades to
// transparent at the edges. Reused for both top and bottom edge highlights.
static Sprite _sheenSprite;
static Sprite GetSheenSprite()
{
    if (_sheenSprite != null) return _sheenSprite;
    var tex = MakeHorizontalSheenTexture(128, 4);
    _sheenSprite = Sprite.Create(tex, new Rect(0, 0, 128, 4), new Vector2(0.5f, 0.5f), 100f);
    _sheenSprite.name = "CompassSheen";
    return _sheenSprite;
}

static Texture2D MakeFadedBarTexture(int width, int height, float fadeFraction)
{
    var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;
    var pixels = new Color[width * height];
    int fadeWidth = Mathf.Max(1, Mathf.RoundToInt(width * fadeFraction));
    for (int x = 0; x < width; x++)
    {
        // Triangular ease — alpha 0 at edges, 1 in the middle, smoothstep
        // through the fade region.
        float a;
        if (x < fadeWidth)            a = Mathf.SmoothStep(0f, 1f, (float)x / fadeWidth);
        else if (x >= width - fadeWidth) a = Mathf.SmoothStep(0f, 1f, (float)(width - 1 - x) / fadeWidth);
        else                          a = 1f;
        for (int y = 0; y < height; y++)
            pixels[y * width + x] = new Color(1f, 1f, 1f, a);
    }
    tex.SetPixels(pixels);
    tex.Apply();
    return tex;
}

static Texture2D MakeHorizontalSheenTexture(int width, int height)
{
    var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;
    var pixels = new Color[width * height];
    for (int x = 0; x < width; x++)
    {
        float u = (float)x / (width - 1);
        float d = Mathf.Abs(u - 0.5f) * 2f;
        float a = Mathf.Clamp01(1f - d * d * d);
        for (int y = 0; y < height; y++)
            pixels[y * width + x] = new Color(1f, 1f, 1f, a);
    }
    tex.SetPixels(pixels);
    tex.Apply();
    return tex;
}
```

- [ ] **Step 5: Rewrite BuildCanvas**

Replace the existing `BuildCanvas()` method (lines 259–301) with:

```csharp
void BuildCanvas()
{
    _canvas = gameObject.AddComponent<Canvas>();
    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    _canvas.sortingOrder = 300;
    var scaler = gameObject.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight = 0.5f;
    gameObject.AddComponent<GraphicRaycaster>();

    var stripGo = new GameObject("Strip", typeof(RectTransform));
    stripGo.transform.SetParent(transform, false);
    _strip = stripGo.GetComponent<RectTransform>();
    _strip.anchorMin = new Vector2(0.5f, 1f);
    _strip.anchorMax = new Vector2(0.5f, 1f);
    _strip.pivot = new Vector2(0.5f, 1f);
    _strip.sizeDelta = new Vector2(stripWidth, stripHeight);
    _strip.anchoredPosition = new Vector2(0f, -topMargin);

    // Faded-edge dark navy background — replaces the flat black bar.
    var bg = stripGo.AddComponent<Image>();
    bg.sprite = GetStripBgSprite();
    bg.type = Image.Type.Sliced;
    bg.color = StripBgColor;
    bg.raycastTarget = false;

    // Top edge sheen — 1px cyan line that fades at the ends.
    var topSheenGo = new GameObject("TopSheen", typeof(RectTransform));
    topSheenGo.transform.SetParent(_strip, false);
    var topSheenRt = topSheenGo.GetComponent<RectTransform>();
    topSheenRt.anchorMin = new Vector2(0f, 1f);
    topSheenRt.anchorMax = new Vector2(1f, 1f);
    topSheenRt.pivot = new Vector2(0.5f, 1f);
    topSheenRt.sizeDelta = new Vector2(0f, 1f);
    topSheenRt.anchoredPosition = Vector2.zero;
    var topSheen = topSheenGo.AddComponent<Image>();
    topSheen.sprite = GetSheenSprite();
    topSheen.color = StripSheenColor;
    topSheen.raycastTarget = false;

    // Bottom edge sheen — mirror.
    var botSheenGo = new GameObject("BottomSheen", typeof(RectTransform));
    botSheenGo.transform.SetParent(_strip, false);
    var botSheenRt = botSheenGo.GetComponent<RectTransform>();
    botSheenRt.anchorMin = new Vector2(0f, 0f);
    botSheenRt.anchorMax = new Vector2(1f, 0f);
    botSheenRt.pivot = new Vector2(0.5f, 0f);
    botSheenRt.sizeDelta = new Vector2(0f, 1f);
    botSheenRt.anchoredPosition = Vector2.zero;
    var botSheen = botSheenGo.AddComponent<Image>();
    botSheen.sprite = GetSheenSprite();
    botSheen.color = StripSheenColor;
    botSheen.raycastTarget = false;

    stripGo.AddComponent<RectMask2D>();

    // Glowing center tick — replaces the plain white tick.
    var tickGo = new GameObject("CenterTick", typeof(RectTransform));
    tickGo.transform.SetParent(_strip, false);
    var tickRt = tickGo.GetComponent<RectTransform>();
    tickRt.anchorMin = new Vector2(0.5f, 0f);
    tickRt.anchorMax = new Vector2(0.5f, 1f);
    tickRt.pivot = new Vector2(0.5f, 0.5f);
    tickRt.sizeDelta = new Vector2(2f, -8f);  // 8px shorter than strip
    tickRt.anchoredPosition = Vector2.zero;
    var tickImg = tickGo.AddComponent<Image>();
    tickImg.color = CenterTickColor;
    tickImg.raycastTarget = false;
    var tickGlow = tickGo.AddComponent<Shadow>();
    tickGlow.effectColor = MarkerGlowColor;
    tickGlow.effectDistance = new Vector2(0f, 0f);

    SeedCardinalWaypoints();
}
```

- [ ] **Step 6: Add the SeedCardinalWaypoints method**

Insert right after `BuildCanvas()`:

```csharp
// Internal-only "waypoints" that always render at fixed bearings on the
// surface plane. Used to draw the N/E/S/W letters. Stored in _waypoints
// alongside gameplay waypoints; GetSaveState filters them out via the
// special source tag prefix below.
const string CardinalSourceTag = "__CARDINAL__";

void SeedCardinalWaypoints()
{
    AddCardinal("N",  0f);
    AddCardinal("E",  90f);
    AddCardinal("S",  180f);
    AddCardinal("W", -90f);
}

void AddCardinal(string letter, float bearingDegrees)
{
    string id = "cardinal_" + letter;
    var wp = new Waypoint
    {
        Id = id,
        SourceTag = CardinalSourceTag,
        Label = letter,
        // Resolve to a world position on the surface plane at the given
        // bearing. Cached lazily — _playerCached / _cameraCached are filled
        // by LateUpdate, so before they exist we just return Vector3.zero
        // (waypoint stays inactive until the player + camera show up).
        PositionProvider = () =>
        {
            if (_playerCached == null || _cameraCached == null) return Vector3.zero;
            Vector3 surfaceUp = _playerCached.transform.up;
            // Build a reference forward = world North on the surface plane.
            // Use camera's projected forward at angle 0; rotate by `bearing`
            // around surfaceUp to find this cardinal's direction.
            Vector3 camForward = _cameraCached.transform.forward;
            Vector3 forwardOnPlane = Vector3.ProjectOnPlane(camForward, surfaceUp);
            if (forwardOnPlane.sqrMagnitude < 0.0001f) return Vector3.zero;
            forwardOnPlane.Normalize();
            // The compass projects waypoints by SignedAngle(forward, toTarget,
            // surfaceUp). For a marker to appear at bearing X, toTarget must
            // be camera-forward rotated by X around surfaceUp. Place the
            // virtual target 100m out so it's well past hideWithinDistance.
            Quaternion rot = Quaternion.AngleAxis(bearingDegrees, surfaceUp);
            Vector3 dir = rot * forwardOnPlane;
            return _playerCached.Rigidbody != null
                ? _playerCached.Rigidbody.position + dir * 100f
                : _playerCached.transform.position + dir * 100f;
        },
        Tint = CardinalColor,
    };
    BuildWaypointUI(wp);
    _waypoints.Add(wp);
}
```

(The `bearingDegrees` semantics here: the existing `LateUpdate` at line 236 uses `Vector3.SignedAngle(forwardOnPlane, toTargetOnPlane, surfaceUp)` — positive = right of camera. So passing `+90f` for `E` gives the East letter at bearing +90° relative to camera-forward, which puts it 90° to the right of where the player is looking. As the player rotates, all four letters animate around — this is exactly Skyrim compass behavior.)

**Critical:** the existing approach treats `forwardOnPlane` as the "where the player is looking" reference. So bearings are camera-relative, not world-relative. The cardinal letters as defined above will track camera direction, which IS what we want for a compass — N moves around the strip as you turn. The existing code recomputes `forwardOnPlane` per frame in `LateUpdate`, so the lambda's recomputation is consistent.

- [ ] **Step 7: Recolor BuildWaypointUI**

Find `BuildWaypointUI()` (lines 303–352). Update the icon and label color blocks:

```csharp
// Icon — defaults to a downward-pointing triangle if none supplied.
var iconGo = new GameObject("Icon", typeof(RectTransform));
iconGo.transform.SetParent(containerGo.transform, false);
var iconRt = iconGo.GetComponent<RectTransform>();
iconRt.anchorMin = new Vector2(0.5f, 1f);
iconRt.anchorMax = new Vector2(0.5f, 1f);
iconRt.pivot = new Vector2(0.5f, 1f);
iconRt.sizeDelta = new Vector2(16f, 16f);     // was 24×24 — smaller for the new strip
iconRt.anchoredPosition = new Vector2(0f, -2f);
wp.IconImage = iconGo.AddComponent<Image>();
wp.IconImage.sprite = wp.Icon != null ? wp.Icon : GetDefaultMarkerSprite();
// Cardinal letters render as text, not icons — hide the triangle for them.
if (wp.SourceTag == CardinalSourceTag)
{
    wp.IconImage.enabled = false;
}
else
{
    wp.IconImage.color = wp.Tint == Color.white ? MarkerIconColor : wp.Tint;
}
wp.IconImage.raycastTarget = false;
var iconGlow = iconGo.AddComponent<Shadow>();
iconGlow.effectColor = MarkerGlowColor;
iconGlow.effectDistance = new Vector2(0f, 0f);

// Label below icon (for waypoints) — for cardinal letters, the label is
// the letter itself, sized larger and centered over the strip.
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

if (wp.SourceTag == CardinalSourceTag)
{
    // Cardinal letter — bigger, centered vertically in the strip.
    labelRt.anchorMin = new Vector2(0.5f, 0.5f);
    labelRt.anchorMax = new Vector2(0.5f, 0.5f);
    labelRt.pivot = new Vector2(0.5f, 0.5f);
    labelRt.sizeDelta = new Vector2(40f, stripHeight);
    labelRt.anchoredPosition = Vector2.zero;
    wp.LabelText.fontSize = 14f;
    wp.LabelText.fontStyle = FontStyles.Bold;
    wp.LabelText.alignment = TextAlignmentOptions.Center;
    wp.LabelText.color = CardinalColor;
}
else
{
    wp.LabelText.fontSize = 12f;            // was 14
    wp.LabelText.fontStyle = FontStyles.Bold;
    wp.LabelText.alignment = TextAlignmentOptions.Center;
    wp.LabelText.color = MarkerLabelColor;  // cool white instead of pure
}
wp.LabelText.raycastTarget = false;
wp.LabelText.outlineColor = Color.black;
wp.LabelText.outlineWidth = 0.2f;
wp.LastShownLabel = wp.Label;
```

- [ ] **Step 8: Filter cardinal waypoints out of save state**

Find `GetSaveState()` (line 393). Update the filter so cardinals are skipped:

```csharp
public List<CompassSave.WaypointEntry> GetSaveState()
{
    var list = new List<CompassSave.WaypointEntry>();
    for (int i = 0; i < _waypoints.Count; i++)
    {
        var wp = _waypoints[i];
        // Skip dynamic (Func-based) waypoints AND internal cardinal letters —
        // both are rebuilt at runtime, not loaded from disk.
        if (string.IsNullOrEmpty(wp.SourceTag) || wp.SourceTag == CardinalSourceTag) continue;
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

Find `ApplySaveState()` (line 413). Add a guard so cardinals aren't re-cleared:

```csharp
public void ApplySaveState(List<CompassSave.WaypointEntry> entries)
{
    // Clear gameplay waypoints only — keep the cardinal letters.
    for (int i = _waypoints.Count - 1; i >= 0; i--)
    {
        if (_waypoints[i].SourceTag == CardinalSourceTag) continue;
        if (_waypoints[i].Ui != null) Destroy(_waypoints[i].Ui.gameObject);
        _waypoints.RemoveAt(i);
    }
    if (entries == null) return;
    for (int i = 0; i < entries.Count; i++)
    {
        var e = entries[i];
        if (e == null || string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.sourceTag)) continue;
        if (e.sourceTag == CardinalSourceTag) continue; // safety: never reload these
        AddWaypointByTag(e.id, e.sourceTag, e.label);
        if (!e.active) SetActive(e.id, false);
    }
}
```

(Replaces the previous `ClearAll()` call which would have wiped the cardinals.)

- [ ] **Step 9: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

Common issues:
- "GetStripBgSprite undefined" or "MakeFadedBarTexture undefined" → Step 4's block was inserted in the wrong scope; verify it's inside the class body
- "_strip is referenced before assignment" → the SeedCardinalWaypoints call in BuildCanvas runs after `_strip` is set, so this should be fine; if not, swap the call order
- "CardinalSourceTag not defined" → Step 6's const declaration was lost; re-add at top of class

- [ ] **Step 10: Commit**

```bash
git add "Assets/3 - Scripts/UI/CompassHUD.cs"
git commit -m "$(cat <<'EOF'
feat(compass): sci-fi Skyrim visual rewrite

Replaces flat 800×60 black bar with a 560×36 faded-edge cyan strip:
alpha gradient at the left/right ends, top/bottom cyan sheen lines,
glowing center tick, cyan triangle waypoints with cool-white labels,
and four cardinal direction letters (N/E/S/W) projected at fixed
bearings via the existing waypoint mechanism. Cardinal letters are
flagged with a __CARDINAL__ source tag so save/restore doesn't try
to persist them. Bearing-projection math in LateUpdate unchanged.

See docs/superpowers/specs/2026-05-09-tutorial-compass-polish-design.md.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Keycap sprite atlas + PromptGlyphs hybrid

Build a runtime TMP `SpriteAsset` containing 9 cyan-tinted keycap glyphs (`F E G M N B Q TAB Esc`). Add a `PromptGlyphs.Maybe()` helper to `TutorialGate.cs` that emits `<sprite name="...">` when the keyboard key has a sprite in the atlas. Wrap the 12 affected `PromptGlyphs` properties to use it. Multi-word keyboard glyphs and all controller paths are unchanged.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/TutorialUI.cs` (add atlas + sprite-asset construction in `Awake` flow; add `HasKeycapSprite` public helper; assign atlas to `tipText.spriteAsset` and `completedText.spriteAsset`)
- Modify: `Assets/3 - Scripts/Tutorial/TutorialGate.cs` (add `Maybe()` helper; wrap 12 properties)

- [ ] **Step 1: Add the keycap atlas builder to TutorialUI.cs**

Insert at the end of the procedural sprite section (after `MakeBeveledOutlineTexture`):

```csharp
// ── Keycap sprite atlas ───────────────────────────────────────────────
//
// Builds a runtime TMP_SpriteAsset containing one cyan-tinted rounded
// keycap per supported key. Used by PromptGlyphs.Maybe to render inline
// keycap visuals in tip text instead of plain bold letters.
//
// Currently supports the 9 single-token keyboard glyphs from the design
// spec audit. Multi-word glyphs (Space, Shift, Ctrl, WASD, mouse, etc.)
// stay as bold text — sprite keycaps for arbitrary phrases would look
// inconsistent (Returnal/Halo do this same hybrid).

static TMPro.TMP_SpriteAsset _keycapAsset;
static System.Collections.Generic.HashSet<string> _keycapNames;

// Each entry: (display label drawn into the keycap, sprite name used
// in <sprite name="..."> tags). Most have label == name; for TAB/Esc
// the label could differ but we keep them identical for clarity.
static readonly (string label, string name, int width)[] KeycapDefs = new[]
{
    ("F",   "F",   28),
    ("E",   "E",   28),
    ("G",   "G",   28),
    ("M",   "M",   28),
    ("N",   "N",   28),
    ("B",   "B",   28),
    ("Q",   "Q",   28),
    ("TAB", "TAB", 44),
    ("Esc", "Esc", 44),
};

public static bool HasKeycapSprite(string keyName)
{
    if (_keycapNames == null) return false;
    return _keycapNames.Contains(keyName);
}

static void EnsureKeycapAsset()
{
    if (_keycapAsset != null) return;
    try
    {
        const int cellHeight = 28;
        const int padding = 2;
        // Compute total atlas width: sum of widths + padding between cells.
        int totalWidth = padding;
        for (int i = 0; i < KeycapDefs.Length; i++) totalWidth += KeycapDefs[i].width + padding;
        // Round up to power of 2 for safety with TMP material samplers.
        int atlasW = Mathf.NextPowerOfTwo(totalWidth);
        int atlasH = Mathf.NextPowerOfTwo(cellHeight + padding * 2);

        var atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
        atlas.filterMode = FilterMode.Bilinear;
        atlas.wrapMode = TextureWrapMode.Clamp;
        var clear = new Color[atlasW * atlasH];
        for (int i = 0; i < clear.Length; i++) clear[i] = new Color(0, 0, 0, 0);
        atlas.SetPixels(clear);

        var glyphTable = new System.Collections.Generic.List<TMPro.TMP_SpriteGlyph>();
        var charTable  = new System.Collections.Generic.List<TMPro.TMP_SpriteCharacter>();
        var nameSet    = new System.Collections.Generic.HashSet<string>();

        int cursorX = padding;
        int cellY = padding;
        for (int i = 0; i < KeycapDefs.Length; i++)
        {
            var def = KeycapDefs[i];
            int w = def.width;
            // Draw rounded-rect cyan-bordered keycap into the cell.
            DrawKeycapCell(atlas, cursorX, cellY, w, cellHeight, def.label);

            var rect = new UnityEngine.TextCore.GlyphRect(cursorX, cellY, w, cellHeight);
            var metrics = new UnityEngine.TextCore.GlyphMetrics(w, cellHeight, 0, cellHeight - 4, w);
            var glyph = new TMPro.TMP_SpriteGlyph((uint)i, metrics, rect, 1.0f, 0);
            glyphTable.Add(glyph);

            var character = new TMPro.TMP_SpriteCharacter(0, glyph) { name = def.name, glyphIndex = (uint)i };
            charTable.Add(character);
            nameSet.Add(def.name);
            cursorX += w + padding;
        }
        atlas.Apply();

        var asset = ScriptableObject.CreateInstance<TMPro.TMP_SpriteAsset>();
        asset.name = "TutorialKeycapAtlas";
        asset.spriteSheet = atlas;
        var mat = new Material(Shader.Find("TextMeshPro/Sprite"));
        mat.SetTexture(TMPro.ShaderUtilities.ID_MainTex, atlas);
        asset.material = mat;
        asset.spriteGlyphTable = glyphTable;
        asset.spriteCharacterTable = charTable;
        asset.UpdateLookupTables();

        _keycapAsset = asset;
        _keycapNames = nameSet;
    }
    catch (System.Exception ex)
    {
        Debug.LogWarning($"[TutorialUI] Keycap atlas build failed; falling back to bold text. {ex.Message}");
        _keycapAsset = null;
        _keycapNames = null;
    }
}

// Draws one keycap into the atlas at (originX, originY) with size w×h:
// dark fill + cyan border + label centered in white.
static void DrawKeycapCell(Texture2D atlas, int originX, int originY, int w, int h, string label)
{
    // Body fill — dark navy with rounded corners.
    Color body   = new Color(0.04f, 0.07f, 0.12f, 0.92f);
    Color border = new Color(0.36f, 0.78f, 1f, 0.85f);
    int radius = 4;
    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            // Rounded-rect mask.
            float a = RoundedRectAlpha(x, y, Mathf.Min(w, h), radius);
            if (a <= 0f) continue;
            // Distance-from-edge for border ring.
            int distEdge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
            Color c = (distEdge < 1) ? border : body;
            c.a *= a;
            atlas.SetPixel(originX + x, originY + y, c);
        }
    }

    // Render the label using a tiny bitmap font baked from a system Font.
    // Use Unity's built-in Arial as the source, render to a temp texture,
    // then composite into the atlas. This is the simplest path that
    // doesn't require shipping a font asset.
    var tmpFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Liberation Sans" }, 18);
    var labelTex = RenderLabelToTexture(label, w, h, tmpFont, 14);
    if (labelTex != null)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var src = labelTex.GetPixel(x, y);
                if (src.a <= 0f) continue;
                var dst = atlas.GetPixel(originX + x, originY + y);
                // Composite white label over the existing pixel.
                Color lit = new Color(0.95f, 0.97f, 1f, src.a);
                atlas.SetPixel(originX + x, originY + y, Color.Lerp(dst, lit, src.a));
            }
        Object.Destroy(labelTex);
    }
}

// Renders `text` centered into a w×h transparent texture using the given Font.
// Returns null if the platform can't render dynamic font textures (rare).
static Texture2D RenderLabelToTexture(string text, int w, int h, Font font, int fontSize)
{
    if (font == null) return null;
    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
    var clear = new Color[w * h];
    for (int i = 0; i < clear.Length; i++) clear[i] = new Color(0, 0, 0, 0);
    tex.SetPixels(clear);

    // Use TextGenerator to compute character positions, then sample each
    // glyph from the font's dynamic texture. This is the cheapest way to
    // get readable bitmap labels at runtime without authoring a font asset.
    font.RequestCharactersInTexture(text, fontSize, FontStyle.Bold);
    int cursor = 0;
    var advances = new int[text.Length];
    int totalWidth = 0;
    for (int i = 0; i < text.Length; i++)
    {
        if (font.GetCharacterInfo(text[i], out var info, fontSize, FontStyle.Bold))
        {
            advances[i] = info.advance;
            totalWidth += info.advance;
        }
    }
    int startX = (w - totalWidth) / 2;
    int baselineY = (h + fontSize) / 2 - 4;

    for (int i = 0; i < text.Length; i++)
    {
        if (!font.GetCharacterInfo(text[i], out var info, fontSize, FontStyle.Bold)) continue;
        var atlasTex = font.material.mainTexture as Texture2D;
        if (atlasTex == null) continue;
        // Sample the glyph rect from the font atlas and draw into our texture.
        Rect uv = info.uvBottomLeft.x < info.uvTopRight.x
            ? new Rect(info.uvBottomLeft.x, info.uvBottomLeft.y,
                       info.uvTopRight.x - info.uvBottomLeft.x,
                       info.uvTopRight.y - info.uvBottomLeft.y)
            : new Rect(info.uvTopRight.x, info.uvTopRight.y,
                       info.uvBottomLeft.x - info.uvTopRight.x,
                       info.uvBottomLeft.y - info.uvTopRight.y);
        int gw = (int)info.glyphWidth;
        int gh = (int)info.glyphHeight;
        for (int gy = 0; gy < gh; gy++)
        {
            for (int gx = 0; gx < gw; gx++)
            {
                float u = uv.x + (gx + 0.5f) / atlasTex.width * uv.width;
                float v = uv.y + (gy + 0.5f) / atlasTex.height * uv.height;
                Color c = atlasTex.GetPixelBilinear(u, v);
                if (c.a <= 0f) continue;
                int dx = startX + cursor + gx + (int)info.minX;
                int dy = baselineY - gy - (int)info.maxY;
                if (dx < 0 || dy < 0 || dx >= w || dy >= h) continue;
                var dst = tex.GetPixel(dx, dy);
                tex.SetPixel(dx, dy, Color.Lerp(dst, new Color(1f, 1f, 1f, c.a), c.a));
            }
        }
        cursor += info.advance;
    }
    tex.Apply();
    return tex;
}
```

(The font-rendering code is the trickiest part. If `RequestCharactersInTexture` returns nothing usable on the test machine, the catch in `EnsureKeycapAsset` keeps the keycaps from breaking the rest of the UI — fallback path is plain bold text.)

- [ ] **Step 2: Wire the atlas into BuildCanvas**

In `BuildCanvas()`, immediately after the `tipText` is created and before `headerTagText` if present, call `EnsureKeycapAsset()` and assign:

Find the line `tipText = NewText(pillRect, "Tip", "", 14f, ...);`. Add **after the tipText shadow block (right before the completedRow construction):**

```csharp
// Bind the keycap sprite atlas so <sprite name="F"> etc. resolve in tips.
EnsureKeycapAsset();
if (_keycapAsset != null)
{
    tipText.spriteAsset = _keycapAsset;
    completedText.spriteAsset = _keycapAsset; // for the "Press TAB" sub-line
}
```

Wait — `completedText` doesn't exist yet at that point in BuildCanvas (it's created later in the completedRow block). Move this assignment block to the **end of BuildCanvas**, after both `tipText` and `completedText` exist.

- [ ] **Step 3: Compile check (TutorialUI changes only)**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

Likely issues:
- `using TMPro;` already present — no extra using needed
- `using UnityEngine.TextCore;` may need to be added at the top of the file for `GlyphRect`/`GlyphMetrics` (or fully qualify them as in the code above — `UnityEngine.TextCore.GlyphRect` is fully qualified so no using needed)
- `TMPro.TMP_SpriteCharacter` constructor signature varies across TMP versions: if the `(uint, TMP_SpriteAsset, TMP_SpriteGlyph)` overload is missing, try `new TMP_SpriteCharacter(0, _keycapAsset, glyph)` or fall back to setting fields after construction. Adapt to whichever overload your TMP version exposes.

If the `TMP_SpriteCharacter(0, glyph) { name = ..., glyphIndex = ... }` form fails, try this alternate construction:

```csharp
var character = new TMPro.TMP_SpriteCharacter(0, asset, glyph);
character.name = def.name;
charTable.Add(character);
```

(Where `asset` is in scope — restructure the loop to construct asset first, populate tables, then assign.)

- [ ] **Step 4: Add Maybe() helper to PromptGlyphs**

Find the `public static class PromptGlyphs` block (line 590 of `TutorialGate.cs`). Insert just before the property declarations, after the `Pick()` helper:

```csharp
// When keyboard input is active AND a keycap sprite for `keyName` is in
// the runtime atlas, return a TMP <sprite name=...> tag that renders the
// inline keycap. Otherwise return `raw` unchanged. Controller paths
// always pass through unchanged (they call Pick directly with raw text).
static string Maybe(string raw, string keyName)
{
    if (Pad) return raw;                                  // controller — never use keycap atlas
    if (!TutorialUI.HasKeycapSprite(keyName)) return raw; // unsupported key — fall back
    return $"<sprite name=\"{keyName}\">";
}
```

- [ ] **Step 5: Wrap the 12 affected properties**

Replace these property lines exactly as shown. Each wraps the keyboard string in a `Maybe(...)` call referencing the matching keycap name.

```csharp
public static string Jump          => Pick("<b>Space</b>", "<b>A</b>",        "<b>Cross</b>");
public static string Interact      => Pick(Maybe("<b>F</b>", "F"),     "<b>X</b>",        "<b>Square</b>");
public static string InteractPlain => Pick(Maybe("F", "F"),            "X",               "Square");
public static string Sprint        => Pick("<b>Shift</b>", "<b>L3</b>",       "<b>L3</b>");
public static string DownThrust    => Pick("<b>Ctrl</b>",  "<b>R3</b>",       "<b>R3</b>");
public static string Flashlight    => Pick(Maybe("<b>E</b>", "E"),     "<b>Y</b>",        "<b>Triangle</b>");
public static string Drop          => Pick(Maybe("<b>G</b>", "G"),     "<b>B</b>",        "<b>Circle</b>");
public static string Map           => Pick(Maybe("<b>M</b>", "M"),     "<b>View</b>",     "<b>Share</b>");
public static string Pause         => Pick(Maybe("<b>Esc</b>", "Esc"), "<b>Start</b>",    "<b>Options</b>");
public static string Cancel        => Pick(Maybe("<b>Esc</b>", "Esc"), "<b>B</b>",        "<b>Circle</b>");
public static string PrimaryFire   => Pick("<b>LMB</b>",   "<b>RT</b>",       "<b>R2</b>");
public static string PrimaryClick  => Pick("<b>left click</b>",  "pull <b>RT</b>", "pull <b>R2</b>");
public static string PrimaryClickCap => Pick("<b>Left click</b>","Pull <b>RT</b>", "Pull <b>R2</b>");
public static string SecondaryFire => Pick("<b>RMB</b>",   "<b>LT</b>",       "<b>L2</b>");
public static string RollLeft      => Pick(Maybe("<b>Q</b>", "Q"),     "<b>LB</b>",       "<b>L1</b>");
public static string RollRight     => Pick(Maybe("<b>E</b>", "E"),     "<b>RB</b>",       "<b>R1</b>");
public static string Move          => Pick("<b>WASD</b>",  "<b>left stick</b>",  "<b>left stick</b>");
public static string MouseLook     => Pick("<b>mouse</b>", "<b>right stick</b>", "<b>right stick</b>");
public static string BuildMenu     => Pick(Maybe("<b>N</b>", "N"),     "<b>LB</b>",       "<b>L1</b>");
public static string Fishingdex    => Pick(Maybe("<b>B</b>", "B"),     "<b>RB</b>",       "<b>R1</b>");
public static string AdvanceTip    => Pick(Maybe("<b>TAB</b>", "TAB"), "<b>LT</b>",       "<b>L2</b>");
public static string DirThrustHold => Pick("<b>WASD + Shift</b>",
                                            "push left stick + <b>click L3</b>",
                                            "push left stick + <b>click L3</b>");
public static string PlacementRotate => Pick("<b>RMB</b>",
                                              "<b>LT + right stick</b>",
                                              "<b>L2 + right stick</b>");
```

(`Jump`, `Sprint`, `DownThrust`, `PrimaryFire`, `PrimaryClick(Cap)`, `SecondaryFire`, `Move`, `MouseLook`, `DirThrustHold`, `PlacementRotate` are unchanged — their kbm strings are multi-word phrases that don't match the keycap atlas.)

- [ ] **Step 6: Compile check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

If `Maybe` reports "TutorialUI.HasKeycapSprite is inaccessible due to its protection level" → verify `HasKeycapSprite` is `public static` in TutorialUI.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/TutorialUI.cs" "Assets/3 - Scripts/Tutorial/TutorialGate.cs"
git commit -m "$(cat <<'EOF'
feat(tutorial): inline keycap sprite atlas for short keys

Builds a runtime TMP_SpriteAsset with 9 cyan-tinted rounded-rect
keycaps (F E G M N B Q TAB Esc) on TutorialUI.Awake. PromptGlyphs.Maybe
emits <sprite name=...> when keyboard input is active and the key is in
the atlas; falls back to the existing <b>...</b> bold text for the
multi-word glyphs (Space, Shift, WASD, mouse, etc.) and all controller
paths. Hybrid pattern keeps the visual story consistent — single-token
keys get keycap visuals, phrases stay as bold text (Returnal/Halo do
this same thing).

If the runtime atlas build fails (rare TMP API surprise), HasKeycapSprite
returns false and the entire UI falls back to current bold-text
rendering; nothing breaks.

See docs/superpowers/specs/2026-05-09-tutorial-compass-polish-design.md.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Final playtest verification

Walk the game in the Editor and verify each acceptance criterion from the design spec. No code changes in this task — only inspection and bug-fix follow-ups. Sign off by reporting which checks passed.

**Files:** none modified (unless a regression is found and fixed inline)

- [ ] **Step 1: Compile + warning baseline**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

Run: `mcp__coplay-mcp__get_unity_logs` with `show_warnings: true`, `show_errors: true`
Expected: no NEW warnings beyond the pre-existing baseline (`AudienceMember.bobAmount` unused field, FXAA shader warnings — these existed before this work).

- [ ] **Step 2: Tutorial first appearance**

In the Editor, enter Play mode on `Assets/1.6.7.7.7.unity` (the active gameplay scene). The cabin spawn flow runs `TutorialManager.BeginTutorial()` immediately, which fires `WakeUpLookStep`.

Expected:
- Pill slides DOWN from above into the top-right
- Beveled silhouette visible (top-left and bottom-right corners cut)
- Cyan LED bar pulsing on the left edge
- `// PROMPT` header tag visible in cyan above the body text
- Body text typewriters in
- For `WakeUpLookStep`, the `mouse` glyph stays as bold text (multi-word, expected)

- [ ] **Step 3: Keycap sprite check**

Walk forward (`W`) past `WakeUpLookStep` → `WakeUpWalkStep` → `ReadNoteStep` → `PickUpRodStep`. The pickup rod step's tip uses `PromptGlyphs.Interact` which returns `<sprite name="F">` after this change.

Expected: in the `PickUpRodStep` tip ("Press F to pick up the fishing rod"), the `F` renders as a cyan rounded-rect keycap, NOT plain bold text.

If the `F` is missing or shows as a placeholder rectangle: open the Editor Console and grep for `[TutorialUI]` warnings — the catch in `EnsureKeycapAsset` logs failures.

- [ ] **Step 4: Sub-line in pill body**

Satisfy the current step. Expected:
- ✓ green check + "Press TAB to continue" fades in INSIDE the pill (not as a separate row below it)
- The pill grows slightly in height to accommodate the sub-line
- `TAB` in the sub-line renders as a wider keycap sprite (per Step 5 of Task 3)

- [ ] **Step 5: Compass visual**

While outdoors during day, look at the top-center of the screen. Expected:
- 560×36 dark navy strip with faded transparent ends
- 1px cyan sheen lines along the top and bottom edges, fading at the same insets
- Glowing cyan center tick (replaces white tick)
- N / E / S / W cardinal letters visible at their bearings as the player rotates
- Letters animate smoothly as the camera turns; center tick stays put
- No popping or visual stutter

- [ ] **Step 6: Compass waypoints**

Reach a step that adds a tutorial waypoint (e.g. fishing bank). Expected:
- Cyan triangle marker at the correct bearing
- Cool-white label below the marker (e.g. "FISHING BANK")
- Walk so the marker leaves the visible field — fades to 40% alpha at the strip edges before disappearing (existing edge-fade behavior unchanged)

- [ ] **Step 7: Save / load round-trip**

Open the pause menu (Esc) → Save → "TEST_POLISH". Return to main menu → Load → "TEST_POLISH". Expected:
- Tutorial pill appears immediately on scene load with the saved current step's tip
- Compass waypoints restore correctly
- N/E/S/W cardinal letters re-render (they're not saved — built fresh on Awake)

- [ ] **Step 8: Sign-off report**

Post a short summary in chat:
- Pass/fail for each of steps 2–7
- Any visual tweaks the user wants (color saturation, pill width, sheen brightness, etc.) noted as inspector overrides — do not change defaults without user approval
- Confirm `mcp__coplay-mcp__check_compile_errors` is still clean

If any step fails, surface the failure in chat with the specific symptom; do not silently retry. The user playtests the visual outcome and decides on the fix direction.

---

## Self-review

**Spec coverage check:**
- ✓ Tutorial pill geometry (anchor, position, width, beveled corners) — Task 1, Steps 3 + 6 + 8
- ✓ Tutorial pill colors — Task 1, Step 2
- ✓ Tutorial pill stacking order — Task 1, Step 8
- ✓ Tutorial pill animation (slide + fade, BorderPulse repointed) — Task 1, Step 9 (animation coroutines preserved from prior iteration)
- ✓ Inline keycap glyphs (atlas, sprite asset, PromptGlyphs.Maybe, fallback) — Task 3 in full
- ✓ Compass geometry (560×36, top margin 32) — Task 2, Step 2
- ✓ Compass colors (faded bg, cyan sheens, glowing tick, cardinal/marker recolor) — Task 2, Steps 3 + 5 + 7
- ✓ Compass cardinal letters via waypoint mechanism — Task 2, Step 6
- ✓ Save filter for cardinals — Task 2, Step 8
- ✓ Public API contract preserved — explicitly noted in plan header, no API changes in any task
- ✓ Verification — Task 4 in full

**Placeholder scan:** searched plan for "TBD", "TODO", "implement later", "appropriate", "etc." — none found.

**Type consistency:**
- `Maybe(string, string)` defined in Task 3 Step 4, used in Task 3 Step 5 ✓
- `HasKeycapSprite(string)` defined in Task 3 Step 1 (public static), called in Task 3 Step 4 ✓
- `EnsureKeycapAsset()` defined in Task 3 Step 1, called in Task 3 Step 2 ✓
- `_keycapAsset`, `_keycapNames` defined in Task 3 Step 1, used in `HasKeycapSprite` and `BuildCanvas` ✓
- `CardinalSourceTag` defined in Task 2 Step 6, used in Task 2 Steps 7 + 8 ✓
- `PillBgBottomColor` (used in BuildCanvas) and `AccentColorDim`/`AccentColor` (used in BorderPulse) both defined in the palette block — ✓

No drift detected.
