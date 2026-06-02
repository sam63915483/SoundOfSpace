# HUD Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the top-left scene-bound vitals UI with a new procedural `VitalsHUD` (beveled card matching the tutorial pill family), and rebuild `PlayerWallet`'s bottom-right card as three rounded neon chips. No functional changes — same data sources, same audio/pulse/charging behavior, same save/load.

**Architecture:** Mirror the existing singleton pattern used by `PlayerWallet`, `TutorialUI`, `Hotbar`, `InteractPromptUI`. New singleton `VitalsHUD` auto-creates its canvas at scene load and disables the legacy `ResourceHUD` UI to avoid double-render. Procedural sprite generation for the beveled panel is extracted from `Hotbar.cs` into a shared `UIPanelSprites` helper so both the hotbar name plate and the new vitals card share one source.

**Tech Stack:** Unity 2022.3, TextMeshPro, runtime `Texture2D`/`Sprite` generation, no test framework (verification is Unity Editor compile + Play mode).

**Spec reference:** `docs/superpowers/specs/2026-05-10-hud-revamp-design.md`

---

## File map

**New:**
- `Assets/3 - Scripts/UI/UIPanelSprites.cs` — shared beveled-panel + outline sprite helper (extracted from `Hotbar.cs:HotbarBeveledPanel`).
- `Assets/3 - Scripts/Survival/VitalsHUD.cs` — new singleton, procedural beveled card with 4 vitals rows + optional charging row, pulse + warning audio.

**Modified:**
- `Assets/3 - Scripts/UI/Hotbar.cs` — drop the local `HotbarBeveledPanel` static class, redirect callers to `UIPanelSprites`.
- `Assets/3 - Scripts/Player/PlayerWallet.cs` — replace `CreateCornerHUD` and `BuildResourceRow` with chip-stack layout.
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `VitalsHUD` in `EnsureGameplaySingletons`.

**Untouched:**
- `Assets/3 - Scripts/Survival/ResourceHUD.cs` — legacy script kept; its scene UI disabled at runtime by `VitalsHUD.Start`.

---

## Conventions used by every task

- **Compile check:** after each code change, run `mcp__coplay-mcp__check_compile_errors` (or focus the Unity Editor; it auto-compiles). Inspect Console — must be zero red errors before continuing.
- **Commit format:** match repo style (`feat(scope): ...` / `fix(scope): ...` / `refactor(scope): ...`).
- **`git add` is always specific:** never `git add -A`.
- Working dir: `C:\123\1aughhh1`. No automated tests; user verifies in Play mode.

---

## Task 1: Extract shared `UIPanelSprites` helper

**Files:**
- Create: `Assets/3 - Scripts/UI/UIPanelSprites.cs`
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` (delete local `HotbarBeveledPanel` class, redirect calls)

The new helper is identical content to the existing `HotbarBeveledPanel` static class in `Hotbar.cs` (around lines 733–800). Moving it to a shared file so both the hotbar name plate and the new vitals card use one source. No behavior change.

- [ ] **Step 1: Create the new file.**

Write `Assets/3 - Scripts/UI/UIPanelSprites.cs` with this exact content:

```csharp
using UnityEngine;

// Procedural beveled-panel + outline sprites used by the tutorial/prompt-pill
// family (TutorialUI, InteractPromptUI, Hotbar name plate, VitalsHUD).
// Cached statics — first call generates, subsequent calls reuse.
public static class UIPanelSprites
{
    static Sprite _panel, _outline;

    /// <summary>Filled beveled panel — clipped top-left + bottom-right corners. 64x64 source, 18 px slice borders.</summary>
    public static Sprite GetBeveledPanel()
    {
        if (_panel != null) return _panel;
        var tex = MakePanel(64, 14);
        _panel = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                               100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        _panel.name = "UIBeveledPanel";
        return _panel;
    }

    /// <summary>Hollow beveled outline — 2 px ring matching the panel shape.</summary>
    public static Sprite GetBeveledOutline()
    {
        if (_outline != null) return _outline;
        var tex = MakeOutline(64, 14, 2);
        _outline = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                 100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        _outline.name = "UIBeveledOutline";
        return _outline;
    }

    static Texture2D MakePanel(int size, int bevel)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        int s = size - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int distTL = x + (s - y);
                int distBR = (s - x) + y;
                float a = 1f;
                if (distTL < bevel) a = Mathf.Clamp01(distTL - (bevel - 1) + 0.5f);
                else if (distBR < bevel) a = Mathf.Clamp01(distBR - (bevel - 1) + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeOutline(int size, int bevel, int thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        int s = size - 1;
        int innerBevel = Mathf.Max(0, bevel - thickness);
        int innerSize = size - 2 * thickness;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int distTL = x + (s - y);
                int distBR = (s - x) + y;
                float outerA = 1f;
                if (distTL < bevel) outerA = Mathf.Clamp01(distTL - (bevel - 1) + 0.5f);
                else if (distBR < bevel) outerA = Mathf.Clamp01(distBR - (bevel - 1) + 0.5f);

                int ix = x - thickness;
                int iy = y - thickness;
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
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
```

- [ ] **Step 2: Delete the local `HotbarBeveledPanel` class in `Hotbar.cs`.**

Find the `static class HotbarBeveledPanel` block at the bottom of `Hotbar.cs` (around lines 733–end). Delete the entire class definition (the class declaration + all its members + closing brace).

- [ ] **Step 3: Redirect callers in `Hotbar.cs`.**

Run:
```
Grep pattern: HotbarBeveledPanel  output_mode: content  glob: Hotbar.cs
```
Expected 2 hits (one for `GetSprite`, one for `GetOutlineSprite` — inside `BuildNamePlate`).

Replace:
- `HotbarBeveledPanel.GetSprite()` → `UIPanelSprites.GetBeveledPanel()`
- `HotbarBeveledPanel.GetOutlineSprite()` → `UIPanelSprites.GetBeveledOutline()`

- [ ] **Step 4: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/3 - Scripts/UI/UIPanelSprites.cs" "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "refactor(ui): extract beveled panel sprites to shared helper"
```

---

## Task 2: Create `VitalsHUD` singleton

**Files:**
- Create: `Assets/3 - Scripts/Survival/VitalsHUD.cs`

Full procedural rebuild of the top-left vitals UI. Disables the legacy `ResourceHUD` Canvas on Start so it doesn't double-render. Mirrors `PlayerWallet`'s shape (auto-create singleton + `CreateCornerHUD` + per-frame `Update` that drives bars).

- [ ] **Step 1: Create the file.**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Top-left vitals HUD. Reads ResourceManager percents and renders a beveled
/// card (matching the tutorial pill / Press-F prompt family) with four
/// horizontal stat rows: HEALTH, HUNGER, THIRST, SHIP POWER. Optional fifth
/// row appears when SolarPanelCharger.IsCharging is true.
///
/// Mirrors PlayerWallet's auto-creating singleton pattern. Disables the
/// legacy ResourceHUD on Start so the two HUDs don't double-render.
/// </summary>
public class VitalsHUD : MonoBehaviour
{
    public static VitalsHUD Instance { get; private set; }

    [Header("Card layout")]
    public float cardWidth = 290f;
    public float topMargin = 20f;
    public float leftMargin = 20f;

    [Header("Pulse / warning")]
    [Tooltip("Below this percent, the bar fill alpha pulses to warn the player.")]
    public float pulseThreshold = 0.25f;
    [Tooltip("Below this percent, the warningClip is played once until percent recovers.")]
    public float urgentThreshold = 0.10f;
    public float pulseFrequency = 1f;
    public AudioClip warningClip;

    // ── Palette (matches TutorialUI / InteractPromptUI) ──────────────
    static readonly Color PillBgColor    = new Color32(0x0A, 0x18, 0x28, 0xF2);
    static readonly Color PillBorderColor = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color LedColor       = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color HeaderColor    = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color LabelColor     = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TrackColor     = new Color32(0x0F, 0x19, 0x2A, 0xD9);

    // ── Internal state ──────────────────────────────────────────────
    Canvas _canvas;
    RectTransform _cardRT;
    StatRow _health, _hunger, _thirst, _shipPower;
    GameObject _chargingRow;
    TMP_Text _chargingText;
    SolarPanelCharger _solar;
    AudioSource _audio;

    bool _hungerWarned, _thirstWarned, _healthWarned, _shipPowerWarned;
    bool _legacyHidden;
    bool _chargingShown;

    class StatRow
    {
        public RectTransform root;
        public RectTransform fill;
        public Image fillImage;
        public TMP_Text pct;
        public int lastPctSeen;     // change-detection so we don't allocate text strings every frame
        public Color colorA, colorB;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("VitalsHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<VitalsHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        DisableLegacyResourceHUD();
        if (_solar == null) _solar = FindObjectOfType<SolarPanelCharger>();
    }

    void Update()
    {
        if (ResourceManager.Instance == null) return;

        float health    = ResourceManager.Instance.HealthPercent;
        float hunger    = ResourceManager.Instance.HungerPercent;
        float thirst    = ResourceManager.Instance.ThirstPercent;
        float shipPower = ResourceManager.Instance.ShipPowerPercent;

        UpdateStat(_health,    health);
        UpdateStat(_hunger,    hunger);
        UpdateStat(_thirst,    thirst);
        UpdateStat(_shipPower, shipPower);

        CheckWarning(health,    ref _healthWarned);
        CheckWarning(hunger,    ref _hungerWarned);
        CheckWarning(thirst,    ref _thirstWarned);
        CheckWarning(shipPower, ref _shipPowerWarned);

        // Charging row visibility toggle.
        bool charging = _solar != null && _solar.IsCharging;
        if (charging != _chargingShown && _chargingRow != null)
        {
            _chargingShown = charging;
            _chargingRow.SetActive(charging);
        }
    }

    void UpdateStat(StatRow row, float percent)
    {
        if (row == null) return;
        // Bar fill: drive via localScale.x so the gradient sprite doesn't squash.
        var s = row.fill.localScale;
        row.fill.localScale = new Vector3(Mathf.Clamp01(percent), s.y, s.z);

        // Pulse alpha when low.
        if (row.fillImage != null)
        {
            float a;
            if (percent < pulseThreshold)
            {
                float t = (Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
                a = Mathf.Lerp(0.35f, 1f, t);
            }
            else
            {
                a = 1f;
            }
            var c = row.fillImage.color;
            if (!Mathf.Approximately(c.a, a))
            {
                c.a = a;
                row.fillImage.color = c;
            }
        }

        // Percent text — only update on whole-percent change to avoid per-frame string alloc.
        int pctInt = Mathf.RoundToInt(Mathf.Clamp01(percent) * 100f);
        if (pctInt != row.lastPctSeen)
        {
            row.lastPctSeen = pctInt;
            if (row.pct != null) row.pct.text = $"{pctInt}%";
        }
    }

    void CheckWarning(float percent, ref bool warned)
    {
        if (percent < urgentThreshold && !warned)
        {
            warned = true;
            if (warningClip != null && _audio != null)
                _audio.PlayOneShot(warningClip);
        }
        else if (percent >= urgentThreshold && warned)
        {
            warned = false;
        }
    }

    void DisableLegacyResourceHUD()
    {
        if (_legacyHidden) return;
        _legacyHidden = true;
        var legacy = FindObjectOfType<ResourceHUD>(true);
        if (legacy == null) return;
        // Disable the legacy ROOT canvas if it has one (entire HUD scene
        // group), else just the GameObject. Either way, the scene-bound
        // bars/labels stop rendering and the new VitalsHUD is the only
        // vitals UI on screen.
        var canvas = legacy.GetComponentInParent<Canvas>(true);
        if (canvas != null) canvas.gameObject.SetActive(false);
        else legacy.gameObject.SetActive(false);
    }

    // ── Build canvas ─────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25; // above hotbar, below tutorial/prompt
        _canvas = canvas;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // Card root anchored top-left.
        var card = NewUI("Card", transform);
        card.anchorMin = new Vector2(0f, 1f);
        card.anchorMax = new Vector2(0f, 1f);
        card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(leftMargin, -topMargin);
        card.sizeDelta = new Vector2(cardWidth, 0f);
        _cardRT = card;

        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = PillBgColor;
        bg.raycastTarget = false;

        var border = NewUI("Border", card);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = PillBorderColor;
        borderImg.raycastTarget = false;
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // LED accent bar on the left.
        var led = NewUI("Led", card);
        led.anchorMin = new Vector2(0f, 0f);
        led.anchorMax = new Vector2(0f, 1f);
        led.pivot = new Vector2(0f, 0.5f);
        led.anchoredPosition = new Vector2(9f, 0f);
        led.sizeDelta = new Vector2(3f, -20f);
        led.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var ledImg = led.gameObject.AddComponent<Image>();
        ledImg.color = LedColor;
        ledImg.raycastTarget = false;

        // Vertical layout for header + stat rows.
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(26, 20, 14, 16);

        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header
        var headerGO = NewText(card, "Header", "// VITALS", 10f, FontStyles.Bold, HeaderColor);
        headerGO.alignment = TextAlignmentOptions.MidlineLeft;
        headerGO.characterSpacing = 6f;
        var headerLE = headerGO.gameObject.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 16f;

        _health    = BuildStatRow(card, "HEALTH",     new Color32(0xFF, 0x6B, 0x9F, 0xFF), new Color32(0xE6, 0x39, 0x52, 0xFF));
        _hunger    = BuildStatRow(card, "HUNGER",     new Color32(0xFF, 0xC4, 0x77, 0xFF), new Color32(0xFF, 0x8A, 0x4C, 0xFF));
        _thirst    = BuildStatRow(card, "THIRST",     new Color32(0x7B, 0xE2, 0xFF, 0xFF), new Color32(0x4A, 0x8B, 0xFF, 0xFF));
        _shipPower = BuildStatRow(card, "SHIP POWER", new Color32(0xB8, 0x8C, 0xFF, 0xFF), new Color32(0xC9, 0x4F, 0xFF, 0xFF));

        // Charging row (hidden by default).
        _chargingRow = BuildChargingRow(card);
        _chargingRow.SetActive(false);
    }

    StatRow BuildStatRow(RectTransform parent, string labelText, Color colorA, Color colorB)
    {
        var row = NewUI(labelText + "Row", parent);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 16f;
        var rowHL = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHL.childAlignment = TextAnchor.MiddleLeft;
        rowHL.childControlWidth = false;
        rowHL.childControlHeight = true;
        rowHL.childForceExpandWidth = false;
        rowHL.childForceExpandHeight = false;
        rowHL.spacing = 10f;
        rowHL.padding = new RectOffset(0, 0, 0, 0);

        // Label
        var lbl = NewText(row, "Label", labelText, 11f, FontStyles.Bold, LabelColor);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 2f;
        var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
        lblLE.preferredWidth = 92f;
        lblLE.preferredHeight = 14f;

        // Bar (track + fill)
        var track = NewUI("Track", row);
        var trackLE = track.gameObject.AddComponent<LayoutElement>();
        trackLE.preferredWidth = 130f;
        trackLE.preferredHeight = 9f;
        trackLE.flexibleWidth = 1f;
        var trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = TrackColor;
        trackImg.raycastTarget = false;

        var fill = NewUI("Fill", track);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = GetHorizontalGradient(colorA, colorB);
        fillImg.type = Image.Type.Simple;
        fillImg.color = Color.white; // gradient comes from the sprite; this multiplier left at white
        fillImg.raycastTarget = false;
        // Drive the fill via localScale.x so the gradient doesn't squash.
        fill.localScale = new Vector3(1f, 1f, 1f);

        // Soft glow under the fill.
        var glow = fill.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(colorA.r, colorA.g, colorA.b, 0.55f);
        glow.effectDistance = new Vector2(0f, 0f);

        // Percent text on the right.
        var pct = NewText(row, "Pct", "0%", 11f, FontStyles.Bold, LabelColor);
        pct.alignment = TextAlignmentOptions.MidlineRight;
        var pctLE = pct.gameObject.AddComponent<LayoutElement>();
        pctLE.preferredWidth = 36f;
        pctLE.preferredHeight = 14f;

        return new StatRow
        {
            root = row,
            fill = fill,
            fillImage = fillImg,
            pct = pct,
            lastPctSeen = -1,
            colorA = colorA,
            colorB = colorB
        };
    }

    GameObject BuildChargingRow(RectTransform parent)
    {
        var row = NewUI("ChargingRow", parent);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 14f;
        var rowHL = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHL.childAlignment = TextAnchor.MiddleLeft;
        rowHL.childControlWidth = false;
        rowHL.childControlHeight = true;
        rowHL.spacing = 8f;

        var dot = NewUI("Dot", row);
        var dotLE = dot.gameObject.AddComponent<LayoutElement>();
        dotLE.preferredWidth = 8f;
        dotLE.preferredHeight = 8f;
        var dotImg = dot.gameObject.AddComponent<Image>();
        dotImg.color = new Color32(0x88, 0xDC, 0xAA, 0xFF);
        dotImg.raycastTarget = false;

        var txt = NewText(row, "Text", "CHARGING", 9f, FontStyles.Bold, new Color32(0x88, 0xDC, 0xAA, 0xFF));
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.characterSpacing = 3f;
        var txtLE = txt.gameObject.AddComponent<LayoutElement>();
        txtLE.preferredHeight = 12f;
        _chargingText = txt;

        return row.gameObject;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    static TMP_FontAsset _hudFont;
    static bool _hudFontResolved;

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        if (!_hudFontResolved)
        {
            _hudFont = Resources.Load<TMP_FontAsset>("Techno SDF");
            if (_hudFont == null)
            {
                var rawFont = Resources.Load<Font>("Techno");
                if (rawFont != null) _hudFont = TMP_FontAsset.CreateFontAsset(rawFont);
            }
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationMono SDF");
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/CourierNewBold SDF");
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _hudFontResolved = true;
        }
        if (_hudFont != null) t.font = _hudFont;
    }

    // ── Horizontal gradient sprite (cached per colour pair) ──────────

    static System.Collections.Generic.Dictionary<long, Sprite> _gradients =
        new System.Collections.Generic.Dictionary<long, Sprite>();

    static Sprite GetHorizontalGradient(Color a, Color b)
    {
        long key = ((long)EncodeColor(a) << 32) | (uint)EncodeColor(b);
        if (_gradients.TryGetValue(key, out var s)) return s;
        const int W = 64, H = 4;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[W * H];
        for (int x = 0; x < W; x++)
        {
            float t = (float)x / (W - 1);
            Color c = Color.Lerp(a, b, t);
            for (int y = 0; y < H; y++) pixels[y * W + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        var spr = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), 100f);
        spr.name = "VitalsBarGradient";
        _gradients[key] = spr;
        return spr;
    }

    static int EncodeColor(Color c)
    {
        int r = Mathf.RoundToInt(c.r * 255f) & 0xFF;
        int g = Mathf.RoundToInt(c.g * 255f) & 0xFF;
        int b = Mathf.RoundToInt(c.b * 255f) & 0xFF;
        int a = Mathf.RoundToInt(c.a * 255f) & 0xFF;
        return (r << 24) | (g << 16) | (b << 8) | a;
    }
}
```

- [ ] **Step 2: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

If errors mention `ResourceManager` / `SolarPanelCharger`, those are existing classes already in the project — verify the names match by `Grep pattern: "class ResourceManager"` and `Grep pattern: "class SolarPanelCharger"`.

- [ ] **Step 3: Commit.**

```bash
git add "Assets/3 - Scripts/Survival/VitalsHUD.cs"
git commit -m "feat(hud): add VitalsHUD singleton (top-left beveled vitals card)"
```

---

## Task 3: Seed `VitalsHUD` in `EnsureGameplaySingletons`

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs`

Without this, loading a save from the main menu briefly has no vitals UI during the apply phase. Match the pattern used for `TutorialUI`, `Hotbar`, `InteractPromptUI`.

- [ ] **Step 1: Insert seeding block.**

Find `EnsureGameplaySingletons` in `MainMenuController.cs` (around line 473). After the existing seeding blocks (CompassHUD, NoteReadUI, InteractPromptUI), insert before the method's closing `}`:

```csharp
        if (VitalsHUD.Instance == null)
        {
            var go = new GameObject("VitalsHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<VitalsHUD>();
        }
```

- [ ] **Step 2: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: Commit.**

```bash
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(hud): seed VitalsHUD in EnsureGameplaySingletons"
```

---

## Task 4: Rewrite `PlayerWallet` UI as chip stack

**Files:**
- Modify: `Assets/3 - Scripts/Player/PlayerWallet.cs`

Keep the data API (`Money`, `AddMoney`, `SpendMoney`, `SetMoney`) and per-frame change-detection. Replace `CreateCornerHUD` and its row builders with chip layout. Chip auto-show/hide for ammo unchanged in semantics.

- [ ] **Step 1: Replace the file's render section.**

Open `Assets/3 - Scripts/Player/PlayerWallet.cs`. Replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tracks the player's money and renders the bottom-right currency HUD as
/// three rounded neon chips (MONEY, WOOD, AMMO). Auto-creates itself and its
/// HUD canvas on game start — no scene setup required.
/// </summary>
public class PlayerWallet : MonoBehaviour
{
    public static PlayerWallet Instance { get; private set; }

    [Header("UI (auto-created)")]
    public TextMeshProUGUI moneyText;
    public TextMeshProUGUI woodText;
    public TextMeshProUGUI ammoText;

    public int Money { get; private set; } = 0;

    // ── Palette ──────────────────────────────────────────────────────
    static readonly Color ChipBgTop   = new Color32(0x14, 0x2C, 0x48, 0xF2);
    static readonly Color ChipBgBot   = new Color32(0x0E, 0x1E, 0x34, 0xF2);
    static readonly Color ChipBorder  = new Color32(0x78, 0xC8, 0xFF, 0x8C);
    static readonly Color ChipGlow    = new Color(0.36f, 0.78f, 1f, 0.30f);
    static readonly Color LabelDim    = new Color32(0xA8, 0xD2, 0xEB, 0xCC);

    static readonly Color MoneyValueColor = new Color32(0xFF, 0xC2, 0x4A, 0xFF); // gold
    static readonly Color WoodValueColor  = new Color32(0xD4, 0xA0, 0x6B, 0xFF); // brown
    static readonly Color AmmoValueColor  = new Color32(0x88, 0xDC, 0xAA, 0xFF); // mint

    const float ChipMinWidth = 170f;
    const float ChipHeight   = 38f;
    const float ChipGap      = 8f;

    int _lastWoodSeen = int.MinValue;
    int _lastAmmoSeen = int.MinValue;
    bool _ammoChipVisible;
    GameObject _moneyChip;
    GameObject _woodChip;
    GameObject _ammoChip;
    PistolController _pistolCached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        GameObject go = new GameObject("PlayerWallet");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerWallet>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (moneyText == null || woodText == null) CreateCornerHUD();
        RefreshMoney();
        RefreshWood();
    }

    void Update()
    {
        int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
        if (wood != _lastWoodSeen)
        {
            _lastWoodSeen = wood;
            RefreshWood();
        }

        if (_pistolCached == null) _pistolCached = FindObjectOfType<PistolController>(true);
        bool show = _pistolCached != null && _pistolCached.IsEquipped;
        if (show != _ammoChipVisible)
        {
            _ammoChipVisible = show;
            if (_ammoChip != null) _ammoChip.SetActive(show);
        }
        if (show && _pistolCached.CurrentAmmo != _lastAmmoSeen)
        {
            _lastAmmoSeen = _pistolCached.CurrentAmmo;
            if (ammoText != null) ammoText.text = _lastAmmoSeen.ToString();
        }
    }

    public void AddMoney(int amount)
    {
        Money += amount;
        RefreshMoney();
        Debug.Log($"[PlayerWallet] +${amount}. Total: ${Money}");
    }

    public bool SpendMoney(int amount)
    {
        if (amount < 0 || Money < amount) return false;
        Money -= amount;
        RefreshMoney();
        return true;
    }

    public void SetMoney(int amount)
    {
        Money = amount;
        RefreshMoney();
    }

    void RefreshMoney()
    {
        if (moneyText != null) moneyText.text = $"${Money}";
    }

    void RefreshWood()
    {
        if (woodText != null)
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            woodText.text = wood.ToString();
        }
    }

    // ── Canvas build ─────────────────────────────────────────────────

    void CreateCornerHUD()
    {
        var canvasGO = new GameObject("WalletHUDCanvas");
        DontDestroyOnLoad(canvasGO);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Stack root anchored bottom-right, expands upward.
        var stack = NewUI("ChipStack", canvasGO.transform);
        stack.anchorMin = new Vector2(1f, 0f);
        stack.anchorMax = new Vector2(1f, 0f);
        stack.pivot = new Vector2(1f, 0f);
        stack.anchoredPosition = new Vector2(-24f, 24f);
        stack.sizeDelta = new Vector2(ChipMinWidth, 0f);

        var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.LowerRight;
        vlg.childControlWidth = false;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.spacing = ChipGap;
        vlg.padding = new RectOffset(0, 0, 0, 0);

        var fitter = stack.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Chips stacked top-to-bottom: MONEY, WOOD, AMMO.
        _moneyChip = BuildChip(stack, "MoneyChip", "MONEY", MoneyValueColor, out moneyText);
        _woodChip  = BuildChip(stack, "WoodChip",  "WOOD",  WoodValueColor,  out woodText);
        _ammoChip  = BuildChip(stack, "AmmoChip",  "AMMO",  AmmoValueColor,  out ammoText);
        _ammoChip.SetActive(false);

        moneyText.text = "$0";
        woodText.text  = "0";
        ammoText.text  = "0";
    }

    GameObject BuildChip(RectTransform parent, string name, string labelText, Color valueColor, out TextMeshProUGUI valueText)
    {
        var chip = NewUI(name, parent);
        var chipLE = chip.gameObject.AddComponent<LayoutElement>();
        chipLE.preferredWidth = ChipMinWidth;
        chipLE.preferredHeight = ChipHeight;

        // Background: rounded gradient via GalaxyHudKit.RoundedSprite (filled
        // rounded rect — works fine as the chip body since it's a positive
        // shape rather than a ring).
        var bg = chip.gameObject.AddComponent<Image>();
        bg.sprite = GalaxyHudKit.RoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = ChipBgBot;
        bg.raycastTarget = false;

        // Soft cyan glow under the chip (acts as the "halo" without needing
        // a second sprite).
        var glow = chip.gameObject.AddComponent<Shadow>();
        glow.effectColor = ChipGlow;
        glow.effectDistance = new Vector2(0f, 0f);

        // Border ring on top of the bg.
        var border = NewUI("Border", chip);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = GalaxyHudKit.RoundedSprite();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = new Color(ChipBorder.r, ChipBorder.g, ChipBorder.b, 0.0f); // bg already shows; border uses Outline component on text instead
        borderImg.raycastTarget = false;
        // We re-use the rounded sprite, recolored at alpha 0 — the actual
        // visible border is the outline component on the bg image. Simpler.
        // Set the bg's outline:
        var bgOutline = chip.gameObject.AddComponent<Outline>();
        bgOutline.effectColor = ChipBorder;
        bgOutline.effectDistance = new Vector2(1f, -1f);

        // Layout: label LEFT, value RIGHT.
        var hl = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = false;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.padding = new RectOffset(18, 18, 0, 0);
        hl.spacing = 8f;

        // Label
        var lbl = NewText(chip, "Label", labelText, 10f, FontStyles.Bold, LabelDim);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 3f;
        var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
        lblLE.preferredWidth = 60f;
        lblLE.flexibleWidth = 1f;

        // Value
        var val = NewText(chip, "Value", "0", 22f, FontStyles.Bold, valueColor);
        val.alignment = TextAlignmentOptions.MidlineRight;
        var valLE = val.gameObject.AddComponent<LayoutElement>();
        valLE.preferredWidth = 80f;
        // Soft glow on the value text.
        var valGlow = val.gameObject.AddComponent<Shadow>();
        valGlow.effectColor = new Color(valueColor.r, valueColor.g, valueColor.b, 0.55f);
        valGlow.effectDistance = new Vector2(0f, 0f);
        // Hard drop shadow for legibility.
        var valDrop = val.gameObject.AddComponent<Shadow>();
        valDrop.effectColor = new Color(0f, 0f, 0f, 0.85f);
        valDrop.effectDistance = new Vector2(0f, -1.5f);

        valueText = val;
        return chip.gameObject;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    static TMP_FontAsset _hudFont;
    static bool _hudFontResolved;

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        if (!_hudFontResolved)
        {
            _hudFont = Resources.Load<TMP_FontAsset>("Techno SDF");
            if (_hudFont == null)
            {
                var rawFont = Resources.Load<Font>("Techno");
                if (rawFont != null) _hudFont = TMP_FontAsset.CreateFontAsset(rawFont);
            }
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationMono SDF");
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/CourierNewBold SDF");
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _hudFontResolved = true;
        }
        if (_hudFont != null) t.font = _hudFont;
    }
}
```

- [ ] **Step 2: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: Commit.**

```bash
git add "Assets/3 - Scripts/Player/PlayerWallet.cs"
git commit -m "feat(hud): rebuild PlayerWallet UI as rounded neon chip stack"
```

---

## Task 5: Verification playthrough

**Files:** none modified.

A 5-minute Editor verification.

- [ ] **Step 1: Top-left vitals card visible at game start.**

Enter Play mode in the `1.6.7.7.7` scene. Top-left should show the new beveled card with `// VITALS` header and 4 stat rows: HEALTH, HUNGER, THIRST, SHIP POWER. Bars filled per the current ResourceManager percent values. The legacy `ResourceHUD` bars in the scene should NOT be visible (disabled by VitalsHUD.Start).

- [ ] **Step 2: Bars + percent text drive correctly.**

If you have cheats enabled, drain a resource (e.g., wait for hunger to tick down) and watch the corresponding bar fill drop + the percent text update. Verify the per-stat color matches: HEALTH=pink, HUNGER=orange, THIRST=blue, SHIP POWER=purple.

- [ ] **Step 3: Pulse + warning audio at low percent.**

Drive a stat below 25% — the bar fill should start pulsing (alpha oscillating). Below 10%, the `warningClip` AudioSource fires once. Recovering above 10% re-arms the warning.

- [ ] **Step 4: Charging indicator.**

If a `SolarPanelCharger` is present and active in the scene, the charging row should appear at the bottom of the vitals card with a mint dot + "CHARGING" text. Otherwise the row stays hidden.

- [ ] **Step 5: Bottom-right chip stack.**

Confirm 2 chips visible at start (MONEY $0, WOOD 0) — stacked top-to-bottom, right-aligned to the screen edge. Money value gold, wood value brown, both with subtle outer glow. Chips have rounded corners and a soft cyan border.

- [ ] **Step 6: Ammo chip auto-show.**

Equip the pistol (press 4 or whichever slot). Ammo chip slides in below wood with mint value. Unequip — chip hides.

- [ ] **Step 7: Save / load round-trip.**

Save from the pause menu, return to main menu, load. Both HUDs render correctly on load. Money and wood values restore.

- [ ] **Step 8: No commit.**

Verification only. If any check fails, file as a follow-up task and fix before claiming done.

---

## Self-review

**1. Spec coverage:**
- Spec Part 1 (VitalsHUD): Tasks 1 (shared sprites), 2 (full singleton), 3 (seeding). ✓
- Spec Part 2 (PlayerWallet rewrite): Task 4. ✓
- Pulse + warning audio: Task 2's UpdateStat + CheckWarning. ✓
- Charging indicator: Task 2's BuildChargingRow + Update toggle. ✓
- Legacy ResourceHUD disable: Task 2's DisableLegacyResourceHUD. ✓
- Shared sprite extraction: Task 1. ✓
- MainMenuController seeding: Task 3. ✓
- Verification: Task 5. ✓

**2. Placeholder scan:** None. All code blocks complete.

**3. Type consistency:**
- `UIPanelSprites.GetBeveledPanel()` / `GetBeveledOutline()` — consistent across Hotbar.cs (Task 1 step 3) and VitalsHUD (Task 2).
- `StatRow` private inner class fields (`root`, `fill`, `fillImage`, `pct`, `lastPctSeen`, `colorA`, `colorB`) used consistently in `BuildStatRow` and `UpdateStat`.
- `_moneyChip` / `_woodChip` / `_ammoChip` GameObjects — toggled via `SetActive` for the ammo case (consistent semantics with the existing `_ammoRowVisible`/`_ammoRowRT` pair being replaced).

One note on a subtle change: `BuildChip`'s border setup uses an `Outline` component on the chip's bg image rather than a separate ring sprite, because the chip is a filled rounded rect (not a transparent slot). This is intentional and documented inline.
