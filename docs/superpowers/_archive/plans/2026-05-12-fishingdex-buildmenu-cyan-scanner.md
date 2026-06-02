# Fishingdex + Build Menu Cyan Scanner — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign both menus as sister "scanner" panels (3-column list/preview/spec) using the existing codebase cyan palette, with the fishingdex converted from scene-built to procedural like the build menu.

**Architecture:** Two new shared utility files (`CyanScannerPalette`, `ScannerFrame`) underpin both menus. `BuildMenuUI` is refactored in place — palette swap + collapse of separate list/detail panels into a single 3-column scanner shape. `FishingdexManager` is rewritten to build its UI procedurally (mirroring `BuildMenuUI`'s pattern) and drops all scene-wired inspector refs for the UI side (preview camera/stage gets created at runtime too).

**Tech Stack:** Unity 2022.3, C#, TMPro (LiberationSans SDF), uGUI (Image / RawImage / Button / RectMask2D / ScrollRect / GridLayoutGroup). No automated tests — verification happens in the Unity Editor Play mode (see each task's verification checklist).

**Spec:** `docs/superpowers/specs/2026-05-12-fishingdex-buildmenu-cyan-scanner-design.md`

**Conventions used everywhere in the codebase (reference):**
- Singleton: `Instance` static + `Awake` null-guard + `OnDestroy` clear.
- Cache scene lookups (`FindObjectOfType`) once; refind lazily if null. Never per-frame.
- For per-frame string allocation in TMP labels, use change-detection (compare new value to last shown).
- `Color32` for UI colours, not `Color`.

---

### Task 1: Create `CyanScannerPalette.cs`

**Files:**
- Create: `Assets/3 - Scripts/UI/CyanScannerPalette.cs`

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;

// Single source of truth for the cyan "scanner" palette used by the
// FishingdexManager and BuildMenuUI. Mirrors the AccentCool / SubtitleColor
// values from MainMenuController so all UI surfaces (main menu, pause menu,
// compass HUD, tutorial pill, killstreak HUD, the two scanner menus) live in
// the same colour space.
//
// All values are Color32 so Image.color assignments don't go through implicit
// gamma conversion — Color32 matches what the rest of the codebase uses for
// uGUI tinting.
public static class CyanScannerPalette
{
    public static readonly Color32 PanelBg        = new Color32(0x0A, 0x12, 0x28, 0xF0); // dark navy, slightly translucent
    public static readonly Color32 PanelBorder    = new Color32(0x1C, 0x3A, 0x5C, 0xFF);
    public static readonly Color32 InnerBg        = new Color32(0x0C, 0x1A, 0x32, 0xFF);
    public static readonly Color32 InnerDivider   = new Color32(0x12, 0x28, 0x45, 0xFF);

    public static readonly Color32 Accent         = new Color32(0x5B, 0xD8, 0xFF, 0xFF); // primary cyan
    public static readonly Color32 AccentDim      = new Color32(0x88, 0xC4, 0xDC, 0xFF); // muted cyan
    public static readonly Color32 Text           = new Color32(0xA8, 0xE6, 0xFF, 0xFF);
    public static readonly Color32 TextBright     = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly Color32 TextMuted      = new Color32(0x88, 0xC4, 0xDC, 0xCC);

    public static readonly Color32 SelectionFill  = new Color32(0x14, 0x30, 0x55, 0xFF);

    public static readonly Color32 BtnNormal      = new Color32(0x14, 0x30, 0x55, 0xFF);
    public static readonly Color32 BtnNormalEdge  = new Color32(0x2A, 0x50, 0x78, 0xFF);
    public static readonly Color32 BtnNormalHover = new Color32(0x1F, 0x44, 0x70, 0xFF);
    public static readonly Color32 BtnPrimary     = Accent;
    public static readonly Color32 BtnPrimaryHover= new Color32(0x8C, 0xE6, 0xFF, 0xFF);
    public static readonly Color32 BtnPrimaryText = new Color32(0x0A, 0x12, 0x28, 0xFF);

    public static readonly Color32 CostAfford     = Accent;
    public static readonly Color32 CostUnafford   = new Color32(0xFF, 0x5A, 0x5A, 0xFF);

    public static readonly Color32 GridLine       = new Color32(0x5B, 0xD8, 0xFF, 0x10); // ~6% alpha
    public static readonly Color32 BracketColor   = Accent;

    // Rarity stripe colours for the fishingdex list rows.
    public static readonly Color32 RarityRare     = Accent;
    public static readonly Color32 RarityUncommon = new Color32(0x88, 0xC4, 0xDC, 0xFF);
    public static readonly Color32 RarityCommon   = new Color32(0x3A, 0x60, 0x80, 0xFF);
}
```

- [ ] **Step 2: Verify it compiles**

Open the Unity Editor. The Console must show zero compile errors. If errors appear, fix the typo / namespace and re-save before continuing.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/CyanScannerPalette.cs"
git commit -m "feat(ui): add shared cyan scanner palette"
```

---

### Task 2: Create `ScannerFrame.cs` with bracket + blueprint-grid helpers

**Files:**
- Create: `Assets/3 - Scripts/UI/ScannerFrame.cs`

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;
using UnityEngine.UI;

// Shared visual helpers for the scanner panels (FishingdexManager + BuildMenuUI).
//
// AddBrackets: 4 corner L-shapes built from 8 thin Images (2 per corner). They
// never overlap content because they're absolutely-positioned in the corners.
//
// AddBlueprintGrid: two tiled Image strips (one horizontal, one vertical)
// drawn at a low alpha to suggest engineering paper behind a build preview.
// The fishingdex skips the grid (creatures, not structures); the build menu
// uses it on its preview region.
public static class ScannerFrame
{
    public static void AddBrackets(RectTransform parent, float length = 14f, float thickness = 2f)
    {
        AddBrackets(parent, length, thickness, CyanScannerPalette.BracketColor);
    }

    public static void AddBrackets(RectTransform parent, float length, float thickness, Color32 color)
    {
        AddCornerBracket(parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), length, thickness, color);   // TL
        AddCornerBracket(parent, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), length, thickness, color);   // TR
        AddCornerBracket(parent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), length, thickness, color);   // BL
        AddCornerBracket(parent, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), length, thickness, color);   // BR
    }

    static void AddCornerBracket(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                 float length, float thickness, Color32 color)
    {
        // One horizontal stub + one vertical stub at each corner. The pivot
        // determines which direction they extend inward.
        // For anchorMin == anchorMax == (0,1) (top-left): horizontal extends right (+X), vertical extends down (-Y).
        bool extendRight = pivot.x < 0.5f;
        bool extendUp    = pivot.y < 0.5f;

        // Horizontal stub
        var hGo = new GameObject("BracketH", typeof(RectTransform), typeof(Image));
        hGo.transform.SetParent(parent, false);
        var hRt = (RectTransform)hGo.transform;
        hRt.anchorMin = anchorMin;
        hRt.anchorMax = anchorMax;
        hRt.pivot     = pivot;
        hRt.sizeDelta = new Vector2(length, thickness);
        hRt.anchoredPosition = Vector2.zero;
        hGo.GetComponent<Image>().color = color;
        hGo.GetComponent<Image>().raycastTarget = false;

        // Vertical stub
        var vGo = new GameObject("BracketV", typeof(RectTransform), typeof(Image));
        vGo.transform.SetParent(parent, false);
        var vRt = (RectTransform)vGo.transform;
        vRt.anchorMin = anchorMin;
        vRt.anchorMax = anchorMax;
        vRt.pivot     = pivot;
        vRt.sizeDelta = new Vector2(thickness, length);
        vRt.anchoredPosition = Vector2.zero;
        vGo.GetComponent<Image>().color = color;
        vGo.GetComponent<Image>().raycastTarget = false;
    }

    // Adds a child container with two faint grid strips (horizontal + vertical).
    // Spacing defaults to 24 px; alpha is built into CyanScannerPalette.GridLine.
    public static void AddBlueprintGrid(RectTransform parent, float gridSpacing = 24f)
    {
        var go = new GameObject("BlueprintGrid", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var bg = go.GetComponent<Image>();
        bg.color = new Color32(0, 0, 0, 0);
        bg.raycastTarget = false;

        // Build a single 1×N stripe texture for each axis and tile via UV.
        AddGridStripes(rt, true, gridSpacing);
        AddGridStripes(rt, false, gridSpacing);
    }

    static void AddGridStripes(RectTransform parent, bool horizontal, float spacing)
    {
        // Tile manually via repeated thin Images so we don't need a custom
        // shader. With ~24-px spacing on a ~360-px preview, that's ~15 strips
        // per axis — well under the per-frame draw-call budget for one menu.
        // Build "enough strips to cover the parent rect" then layout-anchor
        // them along the relevant axis.
        const int MaxStripes = 32;
        for (int i = 1; i < MaxStripes; i++)
        {
            var go = new GameObject(horizontal ? "GridH" : "GridV", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            if (horizontal)
            {
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot     = new Vector2(0.5f, 0);
                rt.sizeDelta = new Vector2(0, 1f);
                rt.anchoredPosition = new Vector2(0, i * spacing);
            }
            else
            {
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot     = new Vector2(0, 0.5f);
                rt.sizeDelta = new Vector2(1f, 0);
                rt.anchoredPosition = new Vector2(i * spacing, 0);
            }
            var img = go.GetComponent<Image>();
            img.color = CyanScannerPalette.GridLine;
            img.raycastTarget = false;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Open the Unity Editor. Console must show zero compile errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/ScannerFrame.cs"
git commit -m "feat(ui): add scanner frame helpers (brackets + blueprint grid)"
```

---

### Task 3: Refactor `BuildMenuUI` to cyan scanner layout

**Files:**
- Modify (significant): `Assets/3 - Scripts/Building/BuildMenuUI.cs`

This task replaces the dark-navy + orange palette with the cyan palette AND collapses the separate `listPanel` / `detailPanel` into one scanner panel (3-column body). The preview rig (`EnsurePreviewRig` / `RenderPrefabPreview`), category enum logic, wood-cost tracking, and tutorial gate logic are **unchanged** — only the UI build code is rewritten.

The new file is shown below in full. Steps split it into "delete old block / write new block" so an executor can apply them sequentially without losing track.

- [ ] **Step 1: Delete the existing `C_PanelBg ... C_IconBg` static colour block (lines ~22–33) and the `gridGo`/`_filterActive`/`_activeFilter`/`_tabButtons`/`_tabFilters`/`_cardEntries` private fields**

Find the lines starting with:
```csharp
    static readonly Color32 C_PanelBg     = new Color32(10,  15,  28,  240);
    ...
    static readonly Color32 C_IconBg      = new Color32(15,  22,  40,  255);
```
and delete that whole block. The new code references `CyanScannerPalette` directly.

Find these field declarations:
```csharp
    GameObject gridGo;
    bool       _filterActive;
    BuildableCategory _activeFilter;
    readonly List<Button> _tabButtons = new List<Button>();
    readonly List<BuildableCategory?> _tabFilters = new List<BuildableCategory?>();
    readonly List<(GameObject card, BuildableCategory cat)> _cardEntries = new List<(GameObject, BuildableCategory)>();
```
Replace the `_cardEntries` list with a tuple list that tracks `(GameObject row, BuildableCategory cat, BuildableEntry entry)` — we need the entry reference on row click. Keep the others as-is:

```csharp
    Transform listContent;              // viewport content where rows live
    bool       _filterActive;
    BuildableCategory _activeFilter;
    readonly List<Button> _tabButtons = new List<Button>();
    readonly List<BuildableCategory?> _tabFilters = new List<BuildableCategory?>();
    readonly List<(GameObject row, BuildableCategory cat, BuildableEntry entry)> _rowEntries = new List<(GameObject, BuildableCategory, BuildableEntry)>();
```

Find these old detail-panel field declarations:
```csharp
    GameObject menuRoot;
    GameObject listPanel;
    GameObject detailPanel;
    RawImage    detailImage;
    TextMeshProUGUI detailName;
    TextMeshProUGUI detailDesc;
    TextMeshProUGUI detailCost;
    Button      detailPlaceBtn;
    Button      detailBackBtn;
    BuildableEntry detailEntry;
```

Replace with the consolidated single-panel refs:
```csharp
    GameObject menuRoot;
    GameObject scannerPanel;
    TextMeshProUGUI headerWoodText;
    RawImage    previewImage;
    TextMeshProUGUI specName;
    TextMeshProUGUI specClass;
    TextMeshProUGUI specCost;
    TextMeshProUGUI specSize;
    TextMeshProUGUI specDesc;
    Button      placeBtn;
    TextMeshProUGUI placeBtnLabel;
    Image       placeBtnBg;
    BuildableEntry detailEntry;
```

- [ ] **Step 2: Replace `BuildUI()` body**

Find:
```csharp
    void BuildUI()
    {
        Canvas canvas = FindOrCreateCanvas();

        menuRoot = NewUIObject("BuildMenuRoot", canvas.transform);
        StretchFull(menuRoot.GetComponent<RectTransform>());
        var bg = menuRoot.AddComponent<Image>();
        bg.color = C_PanelBg;
        bg.raycastTarget = true;

        BuildListPanel();
        BuildDetailPanel();

        menuRoot.SetActive(false);
    }
```

Replace with:
```csharp
    void BuildUI()
    {
        Canvas canvas = FindOrCreateCanvas();

        menuRoot = NewUIObject("BuildMenuRoot", canvas.transform);
        StretchFull(menuRoot.GetComponent<RectTransform>());
        var bg = menuRoot.AddComponent<Image>();
        bg.color = new Color32(0, 0, 0, 200);  // dim background — focus on the panel
        bg.raycastTarget = true;

        BuildScannerPanel();
        menuRoot.SetActive(false);
    }
```

- [ ] **Step 3: Delete the old `BuildListPanel()` and `BuildDetailPanel()` methods entirely**

Find the methods `void BuildListPanel()` and `void BuildDetailPanel()` and delete both methods in full. They span roughly lines 297–664 of the current file. The new `BuildScannerPanel` below replaces them.

Also delete the helper `BuildTabRow` and `UpdateTabHighlights` / `RebuildVisibleCards` / `AddListEntry` — they are replaced by the new variants below.

- [ ] **Step 4: Add the new `BuildScannerPanel()` and supporting methods immediately after `BuildUI()`**

Insert the following block right after the closing `}` of `BuildUI()`:

```csharp
    void BuildScannerPanel()
    {
        scannerPanel = NewUIObject("ScannerPanel", menuRoot.transform);
        var prt = scannerPanel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(960, 720);
        prt.anchoredPosition = Vector2.zero;
        var bg = scannerPanel.AddComponent<Image>();
        bg.color = CyanScannerPalette.PanelBg;

        AddPanelBorder(scannerPanel.transform);

        BuildHeader(scannerPanel.transform);
        BuildTabRow(scannerPanel.transform);
        BuildBody(scannerPanel.transform);
        BuildFooter(scannerPanel.transform);

        RebuildVisibleRows();
    }

    void AddPanelBorder(Transform parent)
    {
        // Thin cyan border around the whole panel. Four edge strips so they
        // never overlap the body content.
        AddEdge(parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 1));   // top
        AddEdge(parent, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 1));   // bottom
        AddEdge(parent, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(1, 0));   // left
        AddEdge(parent, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(1, 0));   // right
    }

    static void AddEdge(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeAxis)
    {
        var go = NewUIObject("Edge", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        // sizeAxis encodes "this axis is the edge thickness":
        //   (0, 1) → vertical edge (left/right): thickness=1px in X, stretches in Y
        //   (1, 0) → horizontal edge (top/bottom): stretches in X, 1px in Y
        rt.sizeDelta = new Vector2(sizeAxis.x * 0, sizeAxis.y * 0); // start with 0/0
        rt.sizeDelta = new Vector2(sizeAxis.x == 0 ? 1f : 0f, sizeAxis.y == 0 ? 1f : 0f);
        var img = go.AddComponent<Image>();
        img.color = CyanScannerPalette.PanelBorder;
        img.raycastTarget = false;
    }

    void BuildHeader(Transform parent)
    {
        var headerGo = NewUIObject("Header", parent);
        var hrt = headerGo.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1);
        hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot     = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 40);
        hrt.anchoredPosition = new Vector2(0, -10);

        var title = NewText("Title", headerGo.transform, "> BUILD MENU // BLUEPRINTS", 18,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 5;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(20, 0);
        trt.offsetMax = new Vector2(-20, 0);

        headerWoodText = NewText("Wood", headerGo.transform, "WOOD 0", 14,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        headerWoodText.characterSpacing = 3;
        var wrt = headerWoodText.rectTransform;
        wrt.anchorMin = new Vector2(0, 0);
        wrt.anchorMax = new Vector2(1, 1);
        wrt.offsetMin = new Vector2(20, 0);
        wrt.offsetMax = new Vector2(-20, 0);

        // 1px divider beneath the header
        var div = NewUIObject("HeaderDivider", parent);
        var dimg = div.AddComponent<Image>();
        dimg.color = CyanScannerPalette.PanelBorder;
        dimg.raycastTarget = false;
        var drt = div.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0, 1);
        drt.anchorMax = new Vector2(1, 1);
        drt.pivot     = new Vector2(0.5f, 1);
        drt.sizeDelta = new Vector2(0, 1);
        drt.anchoredPosition = new Vector2(0, -60);
    }

    void BuildTabRow(Transform parent)
    {
        _tabFilters.Clear();
        _tabButtons.Clear();
        _tabFilters.Add(null); // "All" tab
        var present = new HashSet<BuildableCategory>();
        if (buildables != null)
            foreach (var be in buildables) if (be != null) present.Add(be.category);
        foreach (BuildableCategory c in System.Enum.GetValues(typeof(BuildableCategory)))
            if (present.Contains(c)) _tabFilters.Add(c);

        var rowGo = NewUIObject("TabRow", parent);
        var rrt = rowGo.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0, 1);
        rrt.anchorMax = new Vector2(1, 1);
        rrt.pivot     = new Vector2(0.5f, 1);
        rrt.sizeDelta = new Vector2(0, 30);
        rrt.anchoredPosition = new Vector2(0, -68);
        var hLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
        hLayout.padding = new RectOffset(20, 20, 0, 0);
        hLayout.spacing = 4;
        hLayout.childAlignment = TextAnchor.MiddleLeft;
        hLayout.childForceExpandWidth = false;
        hLayout.childForceExpandHeight = true;
        hLayout.childControlWidth = true;
        hLayout.childControlHeight = true;

        for (int i = 0; i < _tabFilters.Count; i++)
        {
            var f = _tabFilters[i];
            string label = f.HasValue ? f.Value.ToString().ToUpper() : "ALL";
            var btn = NewButton("Tab_" + label, rowGo.transform, label,
                CyanScannerPalette.BtnNormal, CyanScannerPalette.BtnNormalHover, 12);
            btn.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 2;
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = Mathf.Max(56f, label.Length * 9f + 18f);
            le.preferredHeight = 26f;
            var captured = f;
            btn.onClick.AddListener(() => {
                _filterActive = captured.HasValue;
                if (captured.HasValue) _activeFilter = captured.Value;
                RebuildVisibleRows();
            });
            _tabButtons.Add(btn);
        }
        UpdateTabHighlights();
    }

    void UpdateTabHighlights()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            var f = _tabFilters[i];
            bool selected = (!_filterActive && !f.HasValue) || (_filterActive && f.HasValue && f.Value == _activeFilter);
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = selected ? CyanScannerPalette.SelectionFill : CyanScannerPalette.BtnNormal;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.color = selected ? CyanScannerPalette.Accent : CyanScannerPalette.AccentDim;
        }
    }

    void BuildBody(Transform parent)
    {
        var body = NewUIObject("Body", parent);
        var brt = body.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0);
        brt.anchorMax = new Vector2(1, 1);
        brt.offsetMin = new Vector2(20, 80);   // leave space for footer
        brt.offsetMax = new Vector2(-20, -110); // leave space for header + tabs

        // List column (30%)
        var listCol = NewUIObject("ListColumn", body.transform);
        var lcrt = listCol.GetComponent<RectTransform>();
        lcrt.anchorMin = new Vector2(0, 0);
        lcrt.anchorMax = new Vector2(0.30f, 1);
        lcrt.offsetMin = Vector2.zero;
        lcrt.offsetMax = new Vector2(-6, 0);
        var listBg = listCol.AddComponent<Image>();
        listBg.color = CyanScannerPalette.InnerBg;

        BuildListScroll(listCol.transform);

        // Preview column (~38%)
        var previewCol = NewUIObject("PreviewColumn", body.transform);
        var pcrt = previewCol.GetComponent<RectTransform>();
        pcrt.anchorMin = new Vector2(0.30f, 0);
        pcrt.anchorMax = new Vector2(0.68f, 1);
        pcrt.offsetMin = new Vector2(6, 0);
        pcrt.offsetMax = new Vector2(-6, 0);
        var pBg = previewCol.AddComponent<Image>();
        pBg.color = CyanScannerPalette.InnerBg;

        BuildPreviewBox(previewCol.transform);

        // Spec column (32%)
        var specCol = NewUIObject("SpecColumn", body.transform);
        var scrt = specCol.GetComponent<RectTransform>();
        scrt.anchorMin = new Vector2(0.68f, 0);
        scrt.anchorMax = new Vector2(1, 1);
        scrt.offsetMin = new Vector2(6, 0);
        scrt.offsetMax = Vector2.zero;
        var sBg = specCol.AddComponent<Image>();
        sBg.color = CyanScannerPalette.InnerBg;

        BuildSpec(specCol.transform);
    }

    void BuildListScroll(Transform parent)
    {
        var scrollGo = NewUIObject("Scroll", parent);
        var srt = scrollGo.GetComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(6, 6);
        srt.offsetMax = new Vector2(-6, -6);
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = NewUIObject("Viewport", scrollGo.transform);
        var vrt = viewportGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        var vpImg = viewportGo.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = NewUIObject("Content", viewportGo.transform);
        var crt = contentGo.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;
        var v = contentGo.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(2, 2, 2, 2);
        v.spacing = 2;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childControlWidth = true;
        v.childControlHeight = true;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vrt;
        scrollRect.content = crt;
        listContent = contentGo.transform;
    }

    void BuildPreviewBox(Transform parent)
    {
        // Centred square with blueprint grid + brackets + RawImage on top.
        var box = NewUIObject("PreviewBox", parent);
        var brt = box.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot     = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(320, 320);
        brt.anchoredPosition = Vector2.zero;
        var bg = box.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.19f, 0.33f, 1f); // matches PreviewColumn but slightly lighter
        bg.raycastTarget = false;

        ScannerFrame.AddBlueprintGrid(brt, 24f);
        ScannerFrame.AddBrackets(brt);

        var imgGo = NewUIObject("Preview", box.transform);
        var irt = imgGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0);
        irt.anchorMax = new Vector2(1, 1);
        irt.offsetMin = new Vector2(12, 12);
        irt.offsetMax = new Vector2(-12, -12);
        previewImage = imgGo.AddComponent<RawImage>();
        previewImage.raycastTarget = false;
        previewImage.color = new Color(1, 1, 1, 0); // hidden until a row is selected
    }

    void BuildSpec(Transform parent)
    {
        var pad = NewUIObject("SpecPad", parent);
        var prt = pad.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(12, 12);
        prt.offsetMax = new Vector2(-12, -12);

        specName  = AddSpecRow(pad.transform, 0,   "NAME",  CyanScannerPalette.Accent);
        AddSpecDivider(pad.transform, 32);
        specClass = AddSpecRow(pad.transform, 44,  "CLASS", CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 72);
        specCost  = AddSpecRow(pad.transform, 84,  "COST",  CyanScannerPalette.CostAfford);
        AddSpecDivider(pad.transform, 112);
        specSize  = AddSpecRow(pad.transform, 124, "SIZE",  CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 152);

        specDesc = NewText("Desc", pad.transform, "", 13,
            CyanScannerPalette.AccentDim, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        var drt = specDesc.rectTransform;
        drt.anchorMin = new Vector2(0, 0);
        drt.anchorMax = new Vector2(1, 1);
        drt.offsetMin = Vector2.zero;
        drt.offsetMax = new Vector2(0, -170);
        specDesc.enableWordWrapping = true;
    }

    TextMeshProUGUI AddSpecRow(Transform parent, float topY, string key, Color32 valueColor)
    {
        var keyTxt = NewText("Key_" + key, parent, key, 12,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        keyTxt.characterSpacing = 3;
        var krt = keyTxt.rectTransform;
        krt.anchorMin = new Vector2(0, 1);
        krt.anchorMax = new Vector2(0.5f, 1);
        krt.pivot     = new Vector2(0, 1);
        krt.sizeDelta = new Vector2(0, 24);
        krt.anchoredPosition = new Vector2(0, -topY);

        var valTxt = NewText("Val_" + key, parent, "—", 14, valueColor, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        var vrt = valTxt.rectTransform;
        vrt.anchorMin = new Vector2(0.5f, 1);
        vrt.anchorMax = new Vector2(1, 1);
        vrt.pivot     = new Vector2(1, 1);
        vrt.sizeDelta = new Vector2(0, 24);
        vrt.anchoredPosition = new Vector2(0, -topY);
        return valTxt;
    }

    void AddSpecDivider(Transform parent, float topY)
    {
        var div = NewUIObject("Divider", parent);
        var img = div.AddComponent<Image>();
        img.color = CyanScannerPalette.InnerDivider;
        img.raycastTarget = false;
        var rt = div.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(0, -topY);
    }

    void BuildFooter(Transform parent)
    {
        var footer = NewUIObject("Footer", parent);
        var frt = footer.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0);
        frt.anchorMax = new Vector2(1, 0);
        frt.pivot     = new Vector2(0.5f, 0);
        frt.sizeDelta = new Vector2(0, 60);
        frt.anchoredPosition = new Vector2(0, 0);

        var closeBtn = NewButton("CloseBtn", footer.transform, "[ESC] CLOSE",
            CyanScannerPalette.BtnNormal, CyanScannerPalette.BtnNormalHover, 14);
        closeBtn.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 2;
        var crt = closeBtn.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 0.5f);
        crt.anchorMax = new Vector2(0, 0.5f);
        crt.pivot     = new Vector2(0, 0.5f);
        crt.sizeDelta = new Vector2(220, 40);
        crt.anchoredPosition = new Vector2(24, 0);
        closeBtn.onClick.AddListener(Close);

        placeBtn = NewButton("PlaceBtn", footer.transform, "[ENTER] PLACE",
            CyanScannerPalette.BtnPrimary, CyanScannerPalette.BtnPrimaryHover, 14);
        placeBtnBg = placeBtn.GetComponent<Image>();
        placeBtnLabel = placeBtn.GetComponentInChildren<TextMeshProUGUI>();
        placeBtnLabel.characterSpacing = 2;
        placeBtnLabel.color = CyanScannerPalette.BtnPrimaryText;
        var prt = placeBtn.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1, 0.5f);
        prt.anchorMax = new Vector2(1, 0.5f);
        prt.pivot     = new Vector2(1, 0.5f);
        prt.sizeDelta = new Vector2(220, 40);
        prt.anchoredPosition = new Vector2(-24, 0);
        placeBtn.onClick.AddListener(OnPlaceClicked);
    }

    void RebuildVisibleRows()
    {
        if (listContent == null) return;

        // First call builds all rows; subsequent calls just toggle visibility.
        if (_rowEntries.Count == 0 && buildables != null)
        {
            foreach (var entry in buildables)
            {
                if (entry == null) continue;
                var row = AddListRow(listContent, entry);
                _rowEntries.Add((row, entry.category, entry));
            }
        }

        foreach (var (row, cat, _) in _rowEntries)
        {
            if (row == null) continue;
            bool visible = !_filterActive || cat == _activeFilter;
            if (row.activeSelf != visible) row.SetActive(visible);
        }
        UpdateTabHighlights();
    }

    GameObject AddListRow(Transform parent, BuildableEntry entry)
    {
        var row = NewUIObject("Row_" + entry.displayName, parent);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 48;
        var rowBg = row.AddComponent<Image>();
        rowBg.color = new Color32(0, 0, 0, 0);
        var btn = row.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color32(0, 0, 0, 0);
        colors.highlightedColor = CyanScannerPalette.SelectionFill;
        colors.pressedColor = CyanScannerPalette.SelectionFill;
        colors.selectedColor = CyanScannerPalette.SelectionFill;
        btn.colors = colors;
        btn.targetGraphic = rowBg;

        // Left accent stripe
        var stripe = NewUIObject("Stripe", row.transform);
        var srt = stripe.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0);
        srt.anchorMax = new Vector2(0, 1);
        srt.pivot     = new Vector2(0, 0.5f);
        srt.sizeDelta = new Vector2(2, 0);
        srt.anchoredPosition = Vector2.zero;
        var stripeImg = stripe.AddComponent<Image>();
        stripeImg.color = CyanScannerPalette.Accent;
        stripeImg.raycastTarget = false;

        // Thumbnail icon
        var iconGo = NewUIObject("Thumb", row.transform);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0.5f);
        irt.anchorMax = new Vector2(0, 0.5f);
        irt.pivot     = new Vector2(0, 0.5f);
        irt.sizeDelta = new Vector2(40, 40);
        irt.anchoredPosition = new Vector2(6, 0);
        var iconBg = iconGo.AddComponent<Image>();
        iconBg.color = CyanScannerPalette.PanelBg;
        iconBg.raycastTarget = false;
        var thumbGo = NewUIObject("ThumbImg", iconGo.transform);
        var trt = thumbGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var thumb = thumbGo.AddComponent<RawImage>();
        thumb.raycastTarget = false;
        if (entry.icon != null) thumb.texture = entry.icon.texture;
        else
        {
            var rt = GetOrRenderPreview(entry.prefab, PreviewSize);
            if (rt != null) thumb.texture = rt;
            else { thumb.color = new Color(1, 1, 1, 0); }
        }

        // Name label
        var name = NewText("Name", row.transform, entry.displayName.ToUpper(), 13,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        name.characterSpacing = 1;
        var nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0, 0);
        nrt.anchorMax = new Vector2(1, 1);
        nrt.offsetMin = new Vector2(54, 0);
        nrt.offsetMax = new Vector2(-50, 0);
        name.raycastTarget = false;

        // Cost label (right aligned)
        var cost = NewText("Cost", row.transform,
            entry.woodCost > 0 ? entry.woodCost + "w" : "free", 12,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        var crt2 = cost.rectTransform;
        crt2.anchorMin = new Vector2(0, 0);
        crt2.anchorMax = new Vector2(1, 1);
        crt2.offsetMin = new Vector2(0, 0);
        crt2.offsetMax = new Vector2(-6, 0);
        cost.raycastTarget = false;

        var captured = entry;
        var capturedRow = row;
        btn.onClick.AddListener(() => SelectRow(captured, capturedRow));
        return row;
    }

    GameObject _selectedRow;
    void SelectRow(BuildableEntry entry, GameObject row)
    {
        // Visual selection: dim previous selection, highlight new one.
        if (_selectedRow != null)
        {
            var prevBg = _selectedRow.GetComponent<Image>();
            if (prevBg != null) prevBg.color = new Color32(0, 0, 0, 0);
            var prevName = _selectedRow.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (prevName != null) prevName.color = CyanScannerPalette.AccentDim;
        }
        _selectedRow = row;
        if (row != null)
        {
            var bg = row.GetComponent<Image>();
            if (bg != null) bg.color = CyanScannerPalette.SelectionFill;
            var name = row.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.color = CyanScannerPalette.Accent;
        }

        ShowDetail(entry);
    }
```

- [ ] **Step 5: Rewrite `ShowDetail`, `ShowList`, `RefreshDetailCost`, and `Update` to use the new refs**

Find the existing methods:
```csharp
    void ShowList() { listPanel.SetActive(true); detailPanel.SetActive(false); }
    void ShowDetail(BuildableEntry e) { ... }
    void RefreshDetailCost() { ... }
```

Replace `ShowList()` with a no-op (kept for any external callers):
```csharp
    void ShowList() { /* single-panel layout — selection clears the spec */ }
```

Replace `ShowDetail(BuildableEntry e)` with:
```csharp
    void ShowDetail(BuildableEntry e)
    {
        detailEntry = e;
        if (specName  != null) specName.text  = e.displayName.ToUpper();
        if (specClass != null) specClass.text = e.category.ToString().ToUpper();
        if (specSize  != null)
        {
            Vector3 size = GetCachedPrefabSize(e.prefab);
            specSize.text = $"{size.x:F1}×{size.z:F1} m";
        }
        if (specDesc  != null) specDesc.text  = e.description;

        if (previewImage != null)
        {
            if (e.icon != null) { previewImage.texture = e.icon.texture; previewImage.color = Color.white; }
            else
            {
                var rt = GetOrRenderPreview(e.prefab, PreviewSize);
                if (rt != null) { previewImage.texture = rt; previewImage.color = Color.white; }
                else            { previewImage.texture = null; previewImage.color = new Color(1, 1, 1, 0); }
            }
        }
        RefreshDetailCost();
    }
```

Replace `RefreshDetailCost()` with:
```csharp
    void RefreshDetailCost()
    {
        if (specCost == null || detailEntry == null) return;
        int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
        if (detailEntry.woodCost <= 0)
        {
            specCost.text = "FREE";
            specCost.color = CyanScannerPalette.AccentDim;
        }
        else
        {
            bool affordable = wood >= detailEntry.woodCost;
            specCost.text  = detailEntry.woodCost + " WOOD";
            specCost.color = affordable ? CyanScannerPalette.CostAfford : CyanScannerPalette.CostUnafford;
        }
        if (placeBtn != null)
            placeBtn.interactable = detailEntry.woodCost <= 0 || wood >= detailEntry.woodCost;
        if (placeBtnBg != null)
            placeBtnBg.color = placeBtn.interactable ? CyanScannerPalette.BtnPrimary : new Color32(0x2A, 0x50, 0x78, 0xFF);
    }
```

Find the existing `Update` method's tail block:
```csharp
        if (isOpen)
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            if (wood != _lastWoodSeen)
            {
                _lastWoodSeen = wood;
                if (detailPanel != null && detailPanel.activeSelf && detailEntry != null) RefreshDetailCost();
            }
        }
```

Replace with:
```csharp
        if (isOpen)
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            if (wood != _lastWoodSeen)
            {
                _lastWoodSeen = wood;
                if (headerWoodText != null) headerWoodText.text = "WOOD " + wood;
                if (detailEntry != null) RefreshDetailCost();
            }
        }
```

- [ ] **Step 6: Add the prefab-size cache helper**

Add this method anywhere inside the class (near `RenderPrefabPreview`):

```csharp
    readonly Dictionary<GameObject, Vector3> _sizeCache = new Dictionary<GameObject, Vector3>();
    Vector3 GetCachedPrefabSize(GameObject prefab)
    {
        if (prefab == null) return Vector3.zero;
        if (_sizeCache.TryGetValue(prefab, out var s)) return s;
        // Trigger a preview render (which computes bounds) so the size is cached
        // in lockstep with the preview texture. The render is cached too, so
        // calling this is at worst a no-op after the first time.
        GetOrRenderPreview(prefab, PreviewSize);
        return _sizeCache.TryGetValue(prefab, out s) ? s : Vector3.one;
    }
```

Find the inside of `RenderPrefabPreview` where the bounds are computed:
```csharp
        Bounds bounds;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            bounds = new Bounds(_previewStage.position, Vector3.one);
        }
```

Right after that block, add:
```csharp
        // Cache the prefab footprint in lockstep with the rendertexture. The
        // spec column reads this to show SIZE without re-instantiating the
        // prefab a second time.
        if (!_sizeCache.ContainsKey(prefab)) _sizeCache[prefab] = bounds.size;
```

- [ ] **Step 7: Verify it compiles**

Open the Unity Editor. The Console must show zero compile errors.

- [ ] **Step 8: Play-mode verification**

Enter Play mode in the gameplay scene. Press **N** to open the build menu and check each item:

- Panel is centred, dark navy with cyan border (no orange anywhere).
- Header reads `> BUILD MENU // BLUEPRINTS` with `WOOD {n}` on the right.
- Tab row reads `ALL` plus one tab per category present. Active tab is cyan-highlighted; others are dim.
- Left column shows a vertical list of rows. Each row has a 2-px cyan stripe on the left, a 40-px thumbnail, an upper-case name, and an `Nw` cost on the right.
- Clicking a row highlights it with a cyan fill and updates the centre preview + right spec column. The preview shows the rendered prefab over a faint blueprint grid with cyan corner brackets.
- Spec column shows NAME, CLASS, COST (red when unaffordable), SIZE in `X.X×X.X m`, then an italic description.
- Footer: `[ESC] CLOSE` left (dim cyan), `[ENTER] PLACE` right (solid cyan with dark text). Place button is disabled (dimmed) when unaffordable.
- Press N again or click CLOSE — the menu disappears, gameplay resumes.
- Click PLACE on an affordable item — ghost placement begins as before.
- Drop wood (chop a tree) — the header `WOOD {n}` updates within one frame, spec COST colour updates.

If anything's wrong, look first at: which TextMeshProUGUI field didn't get assigned (check inspector ref or `null`-guard), which RectTransform is mis-anchored.

- [ ] **Step 9: Commit**

```bash
git add "Assets/3 - Scripts/Building/BuildMenuUI.cs"
git commit -m "feat(ui): rebuild BuildMenu as cyan scanner (3-column list/preview/spec)"
```

---

### Task 4: Rewrite `FishingdexManager` for procedural cyan scanner construction

**Files:**
- Modify (full rewrite): `Assets/3 - Scripts/Fishing/FishingdexManager.cs`
- The existing `FishingdexEntryUI` is **no longer used** by the dex (rows are built inline). The file stays in the project as dead code for now — the spec leaves cleanup to the user.

The rewrite is large enough to ship as one file replacement. Save the entire new content below, replacing the old file.

- [ ] **Step 1: Replace the entire file contents**

Overwrite `Assets/3 - Scripts/Fishing/FishingdexManager.cs` with:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum FishingdexMode { Browse, Sell, Cook }

// Procedural cyan-scanner UI for the fishingdex. Mirrors BuildMenuUI's
// construction shape — no inspector wiring for the UI side; the manager
// builds its own canvas, list, preview RawImage, spec column, and footer
// buttons at runtime. The fish preview camera + stage are also built at
// runtime (no scene refs).
public class FishingdexManager : MonoBehaviour
{
    public static FishingdexManager Instance { get; private set; }

    [Header("Fish Prefabs (Rare=fish01, Common=fish02, Uncommon=fish03)")]
    public GameObject rareFishPrefab;
    public GameObject commonFishPrefab;
    public GameObject uncommonFishPrefab;

    [Header("Preview Camera Tuning")]
    public float previewCameraFOV = 60f;
    public int   fishPreviewLayer = 31;

    // Runtime-built UI refs
    GameObject menuRoot;
    GameObject scannerPanel;
    TextMeshProUGUI headerCountText;
    Transform listContent;
    RawImage previewImage;
    TextMeshProUGUI specType;
    TextMeshProUGUI specMass;
    TextMeshProUGUI specClass;
    TextMeshProUGUI specValue;
    TextMeshProUGUI specCaught;
    TextMeshProUGUI specDesc;
    Button actionBtn;
    TextMeshProUGUI actionBtnLabel;
    Image actionBtnBg;

    // Preview rig (runtime built — no scene refs)
    Camera previewCamera;
    Transform previewStage;

    bool isOpen;
    FishingdexMode currentMode = FishingdexMode.Browse;
    System.Action<FishEntry, RenderTexture> onFishAction;
    GameObject callerPanel;
    FishEntry currentDetailEntry;
    RenderTexture detailRenderTexture;

    readonly List<(GameObject row, FishEntry entry, RenderTexture rt)> _rowRegistry =
        new List<(GameObject, FishEntry, RenderTexture)>();
    GameObject _selectedRow;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ReleaseAllRowTextures();
        ReleaseDetailTexture();
    }

    void Start()
    {
        EnsurePreviewRig();
        BuildUI();
        if (menuRoot != null) menuRoot.SetActive(false);
    }

    public static event System.Action OnFishingdexOpened;

    void Update()
    {
        bool kbToggle = Input.GetKeyDown(KeyCode.B);

        bool padToggle = false;
        if (TutorialGate.PadPressed(TutorialGate.PadButton.RB))
        {
            var ship = FindObjectOfType<Ship>();
            bool piloting = ship != null && ship.IsPiloted;
            if (!piloting) padToggle = true;
        }
        bool padClose = isOpen && TutorialGate.PadPressed(TutorialGate.PadButton.B);

        if (kbToggle || padToggle || padClose)
        {
            if (isOpen)
            {
                if (currentMode != FishingdexMode.Browse) CloseForCaller();
                else CloseFishingdex();
            }
            else if (!PlayerController.isInDialogue && TutorialGate.IsUnlocked(TutorialAbility.Fishingdex))
                OpenFishingdex();
        }
    }

    // ── Public open / close API ──────────────────────────────────────────

    void OpenFishingdex()
    {
        currentMode = FishingdexMode.Browse;
        onFishAction = null;
        callerPanel  = null;
        isOpen = true;
        PlayerController.isInDialogue = true;
        OnFishingdexOpened?.Invoke();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UnequipAll();
        if (menuRoot != null) menuRoot.SetActive(true);
        PopulateList();
        UpdateActionButtonForMode();
    }

    public void CloseFishingdex()
    {
        isOpen = false;
        PlayerController.isInDialogue = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        currentMode  = FishingdexMode.Browse;
        onFishAction = null;
        callerPanel  = null;
        currentDetailEntry = null;
        ClearList();
        if (menuRoot != null) menuRoot.SetActive(false);
        ReleaseDetailTexture();
    }

    public void OpenForSell(System.Action<FishEntry, RenderTexture> callback, GameObject panel)
    {
        OpenContextMode(FishingdexMode.Sell, callback, panel);
    }

    public void OpenForCook(System.Action<FishEntry, RenderTexture> callback, GameObject panel)
    {
        OpenContextMode(FishingdexMode.Cook, callback, panel);
    }

    void OpenContextMode(FishingdexMode mode, System.Action<FishEntry, RenderTexture> callback, GameObject panel)
    {
        currentMode  = mode;
        onFishAction = callback;
        callerPanel  = panel;
        if (callerPanel != null) callerPanel.SetActive(false);
        isOpen = true;
        if (menuRoot != null) menuRoot.SetActive(true);
        PopulateList();
        UpdateActionButtonForMode();
    }

    void CloseForCaller()
    {
        isOpen = false;
        currentDetailEntry = null;
        ClearList();
        if (menuRoot != null) menuRoot.SetActive(false);
        ReleaseDetailTexture();

        GameObject panel = callerPanel;
        currentMode  = FishingdexMode.Browse;
        onFishAction = null;
        callerPanel  = null;
        if (panel != null) panel.SetActive(true);
    }

    public void CloseIfContextOpen()
    {
        if (isOpen && currentMode != FishingdexMode.Browse)
            CloseForCaller();
    }

    // ── List population / selection ──────────────────────────────────────

    void PopulateList()
    {
        ClearList();
        if (FishInventory.Instance == null) return;

        var allFish = FishInventory.Instance.AllFish;
        if (headerCountText != null) headerCountText.text = allFish.Count + " ENTRIES";

        foreach (FishEntry entry in allFish)
        {
            RenderTexture rt = RenderFish(entry, 128, 128);
            var row = AddListRow(entry, rt);
            _rowRegistry.Add((row, entry, rt));
        }

        // Auto-select the first entry so the preview/spec aren't blank.
        if (_rowRegistry.Count > 0)
            SelectRow(_rowRegistry[0].entry, _rowRegistry[0].row);
        else
            ClearDetailFields();
    }

    void ClearList()
    {
        for (int i = 0; i < _rowRegistry.Count; i++)
        {
            var t = _rowRegistry[i];
            if (t.rt != null) { t.rt.Release(); Destroy(t.rt); }
            if (t.row != null) Destroy(t.row);
        }
        _rowRegistry.Clear();
        _selectedRow = null;
    }

    void SelectRow(FishEntry entry, GameObject row)
    {
        if (_selectedRow != null)
        {
            var prevBg = _selectedRow.GetComponent<Image>();
            if (prevBg != null) prevBg.color = new Color32(0, 0, 0, 0);
            var prevName = _selectedRow.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (prevName != null) prevName.color = CyanScannerPalette.AccentDim;
        }
        _selectedRow = row;
        if (row != null)
        {
            var bg = row.GetComponent<Image>();
            if (bg != null) bg.color = CyanScannerPalette.SelectionFill;
            var name = row.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.color = CyanScannerPalette.Accent;
        }
        ShowDetail(entry);
    }

    void ShowDetail(FishEntry entry)
    {
        currentDetailEntry = entry;
        ReleaseDetailTexture();
        detailRenderTexture = RenderFish(entry, 256, 256);
        if (previewImage != null)
        {
            previewImage.texture = detailRenderTexture;
            previewImage.color = Color.white;
        }
        if (specType   != null) specType.text   = entry.fishType.ToUpper();
        if (specMass   != null) specMass.text   = entry.weightLbs + " LB";
        if (specClass  != null) specClass.text  = GetRarityLabel(entry.fishType);
        if (specValue  != null) specValue.text  = "$" + entry.GetValue();
        if (specCaught != null) specCaught.text = "×" + CountByType(entry.fishType);
        if (specDesc   != null) specDesc.text   = GetRarityDescription(entry.fishType);
        if (actionBtn  != null) actionBtn.interactable = true;
    }

    void ClearDetailFields()
    {
        currentDetailEntry = null;
        if (previewImage != null) previewImage.color = new Color(1, 1, 1, 0);
        if (specType   != null) specType.text   = "—";
        if (specMass   != null) specMass.text   = "—";
        if (specClass  != null) specClass.text  = "—";
        if (specValue  != null) specValue.text  = "—";
        if (specCaught != null) specCaught.text = "—";
        if (specDesc   != null) specDesc.text   = "NO ENTRIES YET. CATCH SOMETHING.";
        if (actionBtn  != null) actionBtn.interactable = false;
    }

    int CountByType(string fishType)
    {
        if (FishInventory.Instance == null) return 0;
        int c = 0;
        foreach (var f in FishInventory.Instance.AllFish) if (f.fishType == fishType) c++;
        return c;
    }

    // Kept for external callers (BonfireInteraction, FishMarketNPC) — they
    // map to "select / clear" since the new panel doesn't have a separate
    // detail screen.
    public void ShowDetail(FishEntry entry, RenderTexture _unused)
    {
        foreach (var t in _rowRegistry)
            if (t.entry == entry) { SelectRow(entry, t.row); return; }
        ShowDetail(entry);
    }
    public void ShowList() { /* single-panel layout, no-op */ }

    // ── Action button ────────────────────────────────────────────────────

    void UpdateActionButtonForMode()
    {
        if (actionBtnLabel == null) return;
        actionBtnLabel.text = currentMode == FishingdexMode.Browse ? "[E] EAT RAW" : "[ENTER] ADD FISH";
    }

    void OnActionClicked()
    {
        if (currentDetailEntry == null) return;
        if (currentMode == FishingdexMode.Browse) OnEatRaw();
        else
        {
            var entry  = currentDetailEntry;
            var action = onFishAction;
            RenderTexture rt = RenderFish(entry, 64, 64);
            CloseForCaller();
            action?.Invoke(entry, rt);
        }
    }

    void OnEatRaw()
    {
        if (currentDetailEntry == null) return;
        (float cooked, float ek, float ew, float ed, float lk, float lw) = currentDetailEntry.fishType switch
        {
            "Rare"     => (60f, 0f, 1f,  5f, 1.0f, 0f),
            "Uncommon" => (35f, 0f, 1f, 10f, 0.4f, 0f),
            _          => (20f, 0f, 1f, 30f, 0f,   1f),
        };
        float raw = cooked * 0.5f;
        ResourceManager.Instance?.ConsumeFood(raw);
        FishInventory.Instance?.RemoveSpecificFish(currentDetailEntry);
        RawFishTripController.StartTrip(30f, ek, ew, ed, lk, lw);
        CloseFishingdex();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    void UnequipAll()
    {
        var rod = FindObjectOfType<FishingRodController>();
        if (rod != null && rod.IsEquipped) rod.ForceUnequipRod();
        var guitar = FindObjectOfType<GuitarController>();
        if (guitar != null && guitar.IsEquipped) guitar.ForceUnequipGuitar();
        var bottle = FindObjectOfType<WaterBottleController>();
        if (bottle != null && bottle.IsEquipped) bottle.ForceUnequipBottle();
        var pickup = FindObjectOfType<PlayerPickup>();
        if (pickup != null) pickup.ForceDropObject();
    }

    void ReleaseDetailTexture()
    {
        if (detailRenderTexture != null)
        {
            detailRenderTexture.Release();
            Destroy(detailRenderTexture);
            detailRenderTexture = null;
        }
    }

    void ReleaseAllRowTextures()
    {
        for (int i = 0; i < _rowRegistry.Count; i++)
        {
            var t = _rowRegistry[i];
            if (t.rt != null) { t.rt.Release(); Destroy(t.rt); }
        }
    }

    GameObject GetPrefabForType(string fishType) => fishType switch
    {
        "Rare"     => rareFishPrefab,
        "Uncommon" => uncommonFishPrefab,
        _          => commonFishPrefab,
    };

    string GetRarityLabel(string fishType) => fishType switch
    {
        "Rare"     => "*** RARE ***",
        "Uncommon" => "** UNCOMMON **",
        _          => "* COMMON *",
    };

    string GetRarityDescription(string fishType) => fishType switch
    {
        "Rare"     => "A rare catch. Eating raw triggers a powerful trip.",
        "Uncommon" => "An uncommon find. Eating raw causes a mild trip.",
        _          => "A common species. Safe to eat with minor effects.",
    };

    Color32 GetStripeColorForType(string fishType) => fishType switch
    {
        "Rare"     => CyanScannerPalette.RarityRare,
        "Uncommon" => CyanScannerPalette.RarityUncommon,
        _          => CyanScannerPalette.RarityCommon,
    };

    // ── Preview rig (runtime built) ──────────────────────────────────────

    void EnsurePreviewRig()
    {
        if (previewCamera != null) return;

        var camGo = new GameObject("FishingdexPreviewCamera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.position = new Vector3(10000f, 10000f, 10000f);
        previewCamera = camGo.AddComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << fishPreviewLayer;
        previewCamera.enabled = false;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = false;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 1000f;
        previewCamera.fieldOfView = previewCameraFOV;

        var stageGo = new GameObject("FishingdexPreviewStage");
        stageGo.transform.SetParent(transform, false);
        stageGo.transform.position = camGo.transform.position;
        previewStage = stageGo.transform;

        Vector3 camOffset = new Vector3(2.5f, 1.2f, -1.5f);
        previewCamera.transform.position = previewStage.position + camOffset;
        previewCamera.transform.LookAt(previewStage.position + new Vector3(0f, -0.1f, 0f));

        AddPreviewLight("FishPreviewKeyLight",
            previewStage.position + new Vector3(-1.5f, 3.5f, -1f),
            intensity: 3f, range: 40f, color: Color.white);
        AddPreviewLight("FishPreviewFillLight",
            previewStage.position + new Vector3(3f, 1f, 2f),
            intensity: 1.2f, range: 30f, color: new Color(0.85f, 0.9f, 1f));
    }

    void AddPreviewLight(string name, Vector3 position, float intensity, float range, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.layer = fishPreviewLayer;
        var l = go.AddComponent<Light>();
        l.type      = LightType.Point;
        l.intensity = intensity;
        l.range     = range;
        l.color     = color;
        l.cullingMask = 1 << fishPreviewLayer;
        l.shadows = LightShadows.None;
    }

    public RenderTexture RenderFish(FishEntry entry, int width, int height)
    {
        EnsurePreviewRig();
        if (previewCamera == null || previewStage == null) return null;
        GameObject prefab = GetPrefabForType(entry.fishType);
        if (prefab == null) return null;

        GameObject fishGO = Instantiate(prefab, previewStage.position, Quaternion.identity);
        fishGO.transform.localScale = new Vector3(FishEntry.GetXScaleFromWeight(entry.weightLbs), 1f, 1f);
        SetLayerRecursive(fishGO, fishPreviewLayer);
        foreach (Renderer r in fishGO.GetComponentsInChildren<Renderer>())
            r.material.color = entry.fishColor;

        RenderTexture rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
        rt.Create();
        previewCamera.fieldOfView = previewCameraFOV;
        previewCamera.enabled = true;
        previewCamera.targetTexture = rt;
        previewCamera.Render();
        previewCamera.targetTexture = null;
        previewCamera.enabled = false;
        DestroyImmediate(fishGO);
        return rt;
    }

    void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }

    // ── UI construction ──────────────────────────────────────────────────

    void BuildUI()
    {
        Canvas canvas = FindOrCreateCanvas();

        menuRoot = NewUIObject("FishingdexRoot", canvas.transform);
        StretchFull(menuRoot.GetComponent<RectTransform>());
        var bg = menuRoot.AddComponent<Image>();
        bg.color = new Color32(0, 0, 0, 200);
        bg.raycastTarget = true;

        BuildScannerPanel();
    }

    Canvas FindOrCreateCanvas()
    {
        var canvases = FindObjectsOfType<Canvas>();
        foreach (var c in canvases) if (c.name == "HUD_Canvas") return c;
        var go = new GameObject("Fishingdex_Canvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    void BuildScannerPanel()
    {
        scannerPanel = NewUIObject("ScannerPanel", menuRoot.transform);
        var prt = scannerPanel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(960, 720);
        prt.anchoredPosition = Vector2.zero;
        var bg = scannerPanel.AddComponent<Image>();
        bg.color = CyanScannerPalette.PanelBg;

        AddPanelBorder(scannerPanel.transform);
        BuildHeader(scannerPanel.transform);
        BuildBody(scannerPanel.transform);
        BuildFooter(scannerPanel.transform);
    }

    void AddPanelBorder(Transform parent)
    {
        AddEdge(parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 1));
        AddEdge(parent, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 1));
        AddEdge(parent, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(1, 0));
        AddEdge(parent, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(1, 0));
    }

    static void AddEdge(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeAxis)
    {
        var go = NewUIObject("Edge", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        rt.sizeDelta = new Vector2(sizeAxis.x == 0 ? 1f : 0f, sizeAxis.y == 0 ? 1f : 0f);
        var img = go.AddComponent<Image>();
        img.color = CyanScannerPalette.PanelBorder;
        img.raycastTarget = false;
    }

    void BuildHeader(Transform parent)
    {
        var headerGo = NewUIObject("Header", parent);
        var hrt = headerGo.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1);
        hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot     = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 40);
        hrt.anchoredPosition = new Vector2(0, -10);

        var title = NewText("Title", headerGo.transform, "> FISHINGDEX // SCAN MODE", 18,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 5;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(20, 0);
        trt.offsetMax = new Vector2(-20, 0);

        headerCountText = NewText("Count", headerGo.transform, "0 ENTRIES", 14,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        headerCountText.characterSpacing = 3;
        var crt = headerCountText.rectTransform;
        crt.anchorMin = new Vector2(0, 0);
        crt.anchorMax = new Vector2(1, 1);
        crt.offsetMin = new Vector2(20, 0);
        crt.offsetMax = new Vector2(-20, 0);

        var div = NewUIObject("HeaderDivider", parent);
        var dimg = div.AddComponent<Image>();
        dimg.color = CyanScannerPalette.PanelBorder;
        dimg.raycastTarget = false;
        var drt = div.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0, 1);
        drt.anchorMax = new Vector2(1, 1);
        drt.pivot     = new Vector2(0.5f, 1);
        drt.sizeDelta = new Vector2(0, 1);
        drt.anchoredPosition = new Vector2(0, -60);
    }

    void BuildBody(Transform parent)
    {
        var body = NewUIObject("Body", parent);
        var brt = body.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0);
        brt.anchorMax = new Vector2(1, 1);
        brt.offsetMin = new Vector2(20, 80);
        brt.offsetMax = new Vector2(-20, -70);

        // List column 30%
        var listCol = NewUIObject("ListColumn", body.transform);
        var lcrt = listCol.GetComponent<RectTransform>();
        lcrt.anchorMin = new Vector2(0, 0);
        lcrt.anchorMax = new Vector2(0.30f, 1);
        lcrt.offsetMin = Vector2.zero;
        lcrt.offsetMax = new Vector2(-6, 0);
        var listBg = listCol.AddComponent<Image>();
        listBg.color = CyanScannerPalette.InnerBg;
        BuildListScroll(listCol.transform);

        // Preview column ~38%
        var previewCol = NewUIObject("PreviewColumn", body.transform);
        var pcrt = previewCol.GetComponent<RectTransform>();
        pcrt.anchorMin = new Vector2(0.30f, 0);
        pcrt.anchorMax = new Vector2(0.68f, 1);
        pcrt.offsetMin = new Vector2(6, 0);
        pcrt.offsetMax = new Vector2(-6, 0);
        var pBg = previewCol.AddComponent<Image>();
        pBg.color = CyanScannerPalette.InnerBg;
        BuildPreviewBox(previewCol.transform);

        // Spec column 32%
        var specCol = NewUIObject("SpecColumn", body.transform);
        var scrt = specCol.GetComponent<RectTransform>();
        scrt.anchorMin = new Vector2(0.68f, 0);
        scrt.anchorMax = new Vector2(1, 1);
        scrt.offsetMin = new Vector2(6, 0);
        scrt.offsetMax = Vector2.zero;
        var sBg = specCol.AddComponent<Image>();
        sBg.color = CyanScannerPalette.InnerBg;
        BuildSpec(specCol.transform);
    }

    void BuildListScroll(Transform parent)
    {
        var scrollGo = NewUIObject("Scroll", parent);
        var srt = scrollGo.GetComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(6, 6);
        srt.offsetMax = new Vector2(-6, -6);
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = NewUIObject("Viewport", scrollGo.transform);
        var vrt = viewportGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        var vpImg = viewportGo.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = NewUIObject("Content", viewportGo.transform);
        var crt = contentGo.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;
        var v = contentGo.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(2, 2, 2, 2);
        v.spacing = 2;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childControlWidth = true;
        v.childControlHeight = true;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vrt;
        scrollRect.content = crt;
        listContent = contentGo.transform;
    }

    GameObject AddListRow(FishEntry entry, RenderTexture rt)
    {
        var row = NewUIObject("Row_" + entry.fishType + "_" + entry.weightLbs, listContent);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 48;
        var rowBg = row.AddComponent<Image>();
        rowBg.color = new Color32(0, 0, 0, 0);
        var btn = row.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color32(0, 0, 0, 0);
        colors.highlightedColor = CyanScannerPalette.SelectionFill;
        colors.pressedColor = CyanScannerPalette.SelectionFill;
        colors.selectedColor = CyanScannerPalette.SelectionFill;
        btn.colors = colors;
        btn.targetGraphic = rowBg;

        var stripe = NewUIObject("Stripe", row.transform);
        var srt = stripe.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0);
        srt.anchorMax = new Vector2(0, 1);
        srt.pivot     = new Vector2(0, 0.5f);
        srt.sizeDelta = new Vector2(3, 0);
        srt.anchoredPosition = Vector2.zero;
        var stripeImg = stripe.AddComponent<Image>();
        stripeImg.color = GetStripeColorForType(entry.fishType);
        stripeImg.raycastTarget = false;

        var iconGo = NewUIObject("Thumb", row.transform);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0.5f);
        irt.anchorMax = new Vector2(0, 0.5f);
        irt.pivot     = new Vector2(0, 0.5f);
        irt.sizeDelta = new Vector2(40, 40);
        irt.anchoredPosition = new Vector2(8, 0);
        var iconBg = iconGo.AddComponent<Image>();
        iconBg.color = CyanScannerPalette.PanelBg;
        iconBg.raycastTarget = false;
        var thumbGo = NewUIObject("ThumbImg", iconGo.transform);
        var trt = thumbGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var thumb = thumbGo.AddComponent<RawImage>();
        thumb.raycastTarget = false;
        if (rt != null) thumb.texture = rt;

        var name = NewText("Name", row.transform,
            entry.fishType.ToUpper() + "." + entry.weightLbs.ToString("D2"), 13,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        name.characterSpacing = 1;
        var nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0, 0);
        nrt.anchorMax = new Vector2(1, 1);
        nrt.offsetMin = new Vector2(56, 0);
        nrt.offsetMax = new Vector2(-8, 0);
        name.raycastTarget = false;

        var captured = entry;
        var capturedRow = row;
        btn.onClick.AddListener(() => SelectRow(captured, capturedRow));
        return row;
    }

    void BuildPreviewBox(Transform parent)
    {
        var box = NewUIObject("PreviewBox", parent);
        var brt = box.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot     = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(320, 320);
        brt.anchoredPosition = Vector2.zero;
        var bg = box.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.19f, 0.33f, 1f);
        bg.raycastTarget = false;

        // Fishingdex previews are creatures — no blueprint grid (that would
        // suggest "blueprint" / "structure", wrong context). Just brackets.
        ScannerFrame.AddBrackets(brt);

        var imgGo = NewUIObject("Preview", box.transform);
        var irt = imgGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0);
        irt.anchorMax = new Vector2(1, 1);
        irt.offsetMin = new Vector2(12, 12);
        irt.offsetMax = new Vector2(-12, -12);
        previewImage = imgGo.AddComponent<RawImage>();
        previewImage.raycastTarget = false;
        previewImage.color = new Color(1, 1, 1, 0);
    }

    void BuildSpec(Transform parent)
    {
        var pad = NewUIObject("SpecPad", parent);
        var prt = pad.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(12, 12);
        prt.offsetMax = new Vector2(-12, -12);

        specType   = AddSpecRow(pad.transform, 0,   "TYPE",   CyanScannerPalette.Accent);
        AddSpecDivider(pad.transform, 32);
        specMass   = AddSpecRow(pad.transform, 44,  "MASS",   CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 72);
        specClass  = AddSpecRow(pad.transform, 84,  "CLASS",  CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 112);
        specValue  = AddSpecRow(pad.transform, 124, "VALUE",  CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 152);
        specCaught = AddSpecRow(pad.transform, 164, "CAUGHT", CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 192);

        specDesc = NewText("Desc", pad.transform, "", 13,
            CyanScannerPalette.AccentDim, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        var drt = specDesc.rectTransform;
        drt.anchorMin = new Vector2(0, 0);
        drt.anchorMax = new Vector2(1, 1);
        drt.offsetMin = Vector2.zero;
        drt.offsetMax = new Vector2(0, -210);
        specDesc.enableWordWrapping = true;
    }

    TextMeshProUGUI AddSpecRow(Transform parent, float topY, string key, Color32 valueColor)
    {
        var keyTxt = NewText("Key_" + key, parent, key, 12,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        keyTxt.characterSpacing = 3;
        var krt = keyTxt.rectTransform;
        krt.anchorMin = new Vector2(0, 1);
        krt.anchorMax = new Vector2(0.5f, 1);
        krt.pivot     = new Vector2(0, 1);
        krt.sizeDelta = new Vector2(0, 24);
        krt.anchoredPosition = new Vector2(0, -topY);

        var valTxt = NewText("Val_" + key, parent, "—", 14, valueColor, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        var vrt = valTxt.rectTransform;
        vrt.anchorMin = new Vector2(0.5f, 1);
        vrt.anchorMax = new Vector2(1, 1);
        vrt.pivot     = new Vector2(1, 1);
        vrt.sizeDelta = new Vector2(0, 24);
        vrt.anchoredPosition = new Vector2(0, -topY);
        return valTxt;
    }

    void AddSpecDivider(Transform parent, float topY)
    {
        var div = NewUIObject("Divider", parent);
        var img = div.AddComponent<Image>();
        img.color = CyanScannerPalette.InnerDivider;
        img.raycastTarget = false;
        var rt = div.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(0, -topY);
    }

    void BuildFooter(Transform parent)
    {
        var footer = NewUIObject("Footer", parent);
        var frt = footer.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0);
        frt.anchorMax = new Vector2(1, 0);
        frt.pivot     = new Vector2(0.5f, 0);
        frt.sizeDelta = new Vector2(0, 60);
        frt.anchoredPosition = new Vector2(0, 0);

        var closeBtn = NewButton("CloseBtn", footer.transform, "[ESC] CLOSE",
            CyanScannerPalette.BtnNormal, CyanScannerPalette.BtnNormalHover, 14);
        closeBtn.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 2;
        var crt = closeBtn.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 0.5f);
        crt.anchorMax = new Vector2(0, 0.5f);
        crt.pivot     = new Vector2(0, 0.5f);
        crt.sizeDelta = new Vector2(220, 40);
        crt.anchoredPosition = new Vector2(24, 0);
        closeBtn.onClick.AddListener(() => {
            if (currentMode != FishingdexMode.Browse) CloseForCaller();
            else CloseFishingdex();
        });

        actionBtn = NewButton("ActionBtn", footer.transform, "[E] EAT RAW",
            CyanScannerPalette.BtnPrimary, CyanScannerPalette.BtnPrimaryHover, 14);
        actionBtnBg = actionBtn.GetComponent<Image>();
        actionBtnLabel = actionBtn.GetComponentInChildren<TextMeshProUGUI>();
        actionBtnLabel.characterSpacing = 2;
        actionBtnLabel.color = CyanScannerPalette.BtnPrimaryText;
        var prt = actionBtn.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1, 0.5f);
        prt.anchorMax = new Vector2(1, 0.5f);
        prt.pivot     = new Vector2(1, 0.5f);
        prt.sizeDelta = new Vector2(220, 40);
        prt.anchoredPosition = new Vector2(-24, 0);
        actionBtn.onClick.AddListener(OnActionClicked);
    }

    // ── UI primitives (mirror BuildMenuUI's helpers) ─────────────────────

    static GameObject NewUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float fontSize,
                                   Color color, FontStyles style, TextAlignmentOptions align)
    {
        var go = NewUIObject(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.enableWordWrapping = true;
        return t;
    }

    static Button NewButton(string name, Transform parent, string label, Color32 normal, Color32 hover, float fontSize)
    {
        var go = NewUIObject(name, parent);
        var img = go.AddComponent<Image>();
        img.color = normal;
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = normal;
        colors.highlightedColor = hover;
        colors.pressedColor = new Color32((byte)(normal.r * 0.7f), (byte)(normal.g * 0.7f), (byte)(normal.b * 0.7f), 255);
        colors.selectedColor = hover;
        btn.colors = colors;
        var labelGo = NewUIObject("Label", go.transform);
        var t = labelGo.AddComponent<TextMeshProUGUI>();
        t.text = label;
        t.fontSize = fontSize;
        t.color = Color.white;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        StretchFull(labelGo.GetComponent<RectTransform>());
        t.raycastTarget = false;
        return btn;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Open the Unity Editor. The Console must show zero compile errors. If the old inspector ref code complains, check that all references to `fishingdexCanvas`, `listPanel`, `detailPanel`, `listContent`, `fishEntryItemPrefab`, `closeButton`, `detailPreviewImage`, `detailTypeText`, `detailWeightText`, `detailRarityText`, `detailValueText`, `backButton`, `detailActionButton`, `detailActionText`, `fishPreviewCamera`, `fishPreviewStage` have been removed.

- [ ] **Step 3: Play-mode verification — Browse mode**

Enter Play mode. Press **B** to open the fishingdex. Check:

- A cyan scanner panel appears (matches the mockup): header `> FISHINGDEX // SCAN MODE` with `N ENTRIES` on the right, three columns, footer.
- Each row in the left list has a rarity-coloured stripe (cyan = rare, light-cyan = uncommon, dim slate = common), a 40 px rendered fish thumbnail, and `TYPE.WEIGHT` text in monospace upper-case.
- The first row auto-selects; preview shows the rendered fish, spec shows TYPE / MASS / CLASS / VALUE / CAUGHT, and a 2-line italic description.
- Clicking other rows updates preview + spec in place; no list/detail screen swap.
- Bracket corners visible at all four corners of the preview box.
- Footer: `[ESC] CLOSE` left, `[E] EAT RAW` right (solid cyan with dark text).
- Click EAT RAW on a rare fish — the trip effect plays as before; the dex closes.
- Empty inventory edge case: catch nothing, open dex — list is empty, preview hidden, spec all `—`, description reads "NO ENTRIES YET. CATCH SOMETHING.", action button is disabled (greyed out).

- [ ] **Step 4: Play-mode verification — Sell + Cook modes**

- Walk to FishMarketNPC, trigger the sell flow → dex opens with action button reading `[ENTER] ADD FISH`. Pick a fish + click ADD FISH → fish is added to the sell list, dex closes back to the FishMarket panel.
- Walk to a placed bonfire, trigger the cook flow → same shape: action button `[ENTER] ADD FISH`, picking a fish hands it to the cook panel.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishingdexManager.cs"
git commit -m "feat(ui): rewrite Fishingdex as procedural cyan scanner"
```

---

### Task 5: Update `CLAUDE.md` to reflect the shared scanner UI

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the "Currency, fish & market" and Hotbar sections**

Find the line in the "Currency, fish & market" section:
```
- `FishInventory` and `FishingdexManager` record caught fish; `FishingdexEntryUI` displays them.
```
Replace with:
```
- `FishInventory` and `FishingdexManager` record caught fish. The dex UI is built procedurally by `FishingdexManager` itself (no scene-wired inspector refs) — same construction shape as `BuildMenuUI`. The old `FishingdexEntryUI` component is unused and can be deleted.
```

- [ ] **Step 2: Append a "Shared UI helpers" note**

Add a new section under "Project layout" (or wherever feels most logical):

```
### Shared scanner UI helpers

Both `FishingdexManager` and `BuildMenuUI` use a cyan "scanner" visual language. Two helpers in `Assets/3 - Scripts/UI/` keep them in lockstep:

- `CyanScannerPalette` — single source of truth for the cyan palette (mirrors `MainMenuController.AccentCool` / `SubtitleColor`). All UI surfaces should pull from here so a future tweak hits every menu at once.
- `ScannerFrame` — `AddBrackets` (4 corner L-shapes) and `AddBlueprintGrid` (faint cyan grid behind a preview). Used by both menus' preview boxes.

When adding a new menu in this family: build a 3-column body (list / preview / spec), use `CyanScannerPalette` for every colour, drop brackets on the preview, and add the blueprint grid if the preview shows a structure (skip the grid for creatures).
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(ui): document shared cyan scanner palette + frame helpers"
```

---

### Task 6: Full menu sweep — verification pass

**Files:** none (verification only).

- [ ] **Step 1: Open both menus back-to-back and confirm they look like sister panels**

In Play mode:
- Open the fishingdex (B). Note the panel position, border colour, header layout, column proportions.
- Close it. Open the build menu (N). Same panel position, same border colour, same column proportions, same footer layout. Different content (blueprint grid behind the preview) but unmistakably part of the same UI family.

If proportions differ visibly (e.g. one has a wider list column than the other), the issue is in one of:
- The `BuildBody` anchor fractions (must both be `0.30 / 0.68`).
- The panel root `sizeDelta` (must both be `960×720`).

- [ ] **Step 2: Confirm gameplay isn't broken**

- Tutorial gate: from a fresh save, ensure `B` and `N` do NOT open the dex / build menu until the tutorial has unlocked those abilities. (`TutorialGate.IsUnlocked(TutorialAbility.Fishingdex|BuildMenu)` is the gate.)
- Piloting suppression: get in the ship, try `LB`/`RB` — neither menu opens.
- Save / load: save the game with at least one fish + one placed building. Reload. Both menus still work the same.
- Build menu re-open after placement: trigger the cabin tutorial step that calls `BuildMenuUI.RequestReopenAfterPlacement()` — after placing the cabin the menu reappears.

- [ ] **Step 3: Final commit (if any fixups were needed)**

If any fixes were committed across the verification pass, no extra commit is needed. Otherwise skip.

---

## Self-Review

Checking the plan against the spec:

**Spec coverage:**
- Shared cyan palette → Task 1 ✓
- Bracket + blueprint-grid helpers → Task 2 ✓
- Build menu palette + layout collapse → Task 3 ✓
- Build menu blueprint grid + brackets on preview → Task 3 Step 4 (BuildPreviewBox) ✓
- Build menu wood-cost live update + affordability colouring → Task 3 Step 5 ✓
- Fishingdex procedural construction → Task 4 ✓
- Fishingdex modes (Browse/Sell/Cook) preserved → Task 4 (`OpenForSell`, `OpenForCook`, `UpdateActionButtonForMode`) ✓
- Fishingdex EAT RAW path → Task 4 (`OnEatRaw`) ✓
- Fishingdex caught-count spec row → Task 4 (`CountByType`) ✓
- Fishingdex preview rig built at runtime → Task 4 (`EnsurePreviewRig`) ✓
- CLAUDE.md update → Task 5 ✓
- Full verification → Task 6 ✓

**Placeholder scan:** Search results — no "TBD"/"TODO"/"implement later"/"fill in details" anywhere. The code blocks are complete and self-contained.

**Type consistency:** `CyanScannerPalette.Accent` / `BtnPrimary` / `SelectionFill` etc. referenced consistently. `ScannerFrame.AddBrackets` / `AddBlueprintGrid` signatures match the call sites in both menus. `previewImage` (RawImage), `specType` / `specClass` etc. (TextMeshProUGUI) match between definition and reference.

**Gaps:** none identified.
