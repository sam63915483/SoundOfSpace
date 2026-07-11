# Astronaut Helmet HUD Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Frame the screen with a high-res astronaut-helmet-interior overlay whose recessed "screen" housings the three existing HUD clusters (vitals bottom-right, jetpack/boost bottom-left, compass top brow) seat into, plus visor glass FX, O₂-driven condensation, helmet sway, and a power-on animation for the boost cluster.

**Architecture:** All three clusters are procedural auto-singletons (`VitalsHUD`, `GForceHUD`, `CompassHUD`) that build their own ScreenSpaceOverlay canvases at runtime with identical CanvasScaler settings (1920×1080 reference, match 0.5) — so every canvas shares one coordinate space, and a new helmet-frame canvas above them stays pixel-aligned with the clusters at any resolution/aspect by using the same scaler and **corner-anchored pieces** (never one stretched full-screen image). A scene-placed `HelmetHudConfig` holds the user's texture, the accent color, and all tunables in the Inspector; a `HelmetOverlayHUD` auto-singleton (seeded in `EnsureGameplaySingletons` — trap #1) builds the frame, glass, and condensation layers from it.

**Tech Stack:** Unity 2022.3, Built-in RP, uGUI (ScreenSpaceOverlay canvases), procedural textures/sprites (existing codebase idiom), TMP, PlayerPrefs via `InputSettings`.

---

## Verified current state (inspection findings, 2026-07-10)

| Cluster | File | Canvas | Position | Visibility gate |
|---|---|---|---|---|
| Survival vitals (HEALTH/HUNGER/THIRST/SUIT O2 + ship rows) | `Assets/3 - Scripts/Survival/VitalsHUD.cs` | own overlay canvas, sortingOrder **830** | card anchored **bottom-right** (−24, 24), width 320, height varies via ContentSizeFitter (~150 normal, ~220 with ship rows + charging) | always on; ship rows toggle on `Ship.AnyShipPiloted` |
| Jetpack/boost (speed tape + 3D thrust gimbal + UP/DN/DIR seg bars) | `Assets/3 - Scripts/Ship/GForceHUD.cs` | own overlay canvas, sortingOrder **830** | card anchored **bottom-left** (20, 24), ~330×150 | `_canvas.enabled = (player.JetpackUnlocked \|\| Ship.PilotedInstance != null \|\| DroneController.Active != null) && !isMapOpen` (`GForceHUD.cs:139-151`) |
| Compass strip + heading badge | `Assets/3 - Scripts/UI/CompassHUD.cs` | own overlay canvas, sortingOrder **300** (deliberately UNDER LetterboxBars 820 so dialogue letterbox covers it) | strip anchored top-center, 560×36, topMargin 32; badge above strip | hides itself when map open |

- `Assets/3 - Scripts/Ship/BoostMeterUI.cs` is **legacy** — it self-suppresses when `GForceHUD.Instance.OwnsBoostMeter` is true (always). Do not touch it.
- All HUD canvases register with `HUDSceneGate` (MainMenu hide) and most with `HudVisibility` (HIDE-HUD setting + pod-cinematic force-hide, driven via CanvasGroup alpha).
- Sorting contract lives in `Assets/3 - Scripts/UI/UILayer.cs` (Hud=830, Toast=900, Pause=1000; phone ≈850).
- **Bloom is `BloomEffect.cs` (KinoBloom) running as an `OnRenderImage` camera effect** via `CustomPostProcessing` — it runs **before** ScreenSpaceOverlay UI is composited. Overlay UI can NEVER receive real camera bloom. Additionally its threshold is 0.8 gamma, so if the HUD were moved into camera space, the existing near-white HUD text (0xEAF6FF ≈ 0.93) would bloom indiscriminately. **Decision: fake the emissive glow with procedural soft-halo sprites + the existing `Shadow`-glow idiom (already used by VitalsHUD/CompassHUD). Do NOT migrate canvases to Screen Space – Camera.** (Optional future experiment noted in Phase 3.)
- Camera chromatic aberration already exists (`ChromaticAberrationEffect`, gated by `InputSettings.fxChromaticAberration`, intensity `fxChromaticAberrationIntensity`) — Phase 3 reuses it for the world view instead of rebuilding it.
- Camera-effect toggles follow the recipe: append `fx*` flag to `InputSettings` (+ PlayerPrefs Load/Save lines around `InputSettings.cs:316-330` / `418-432`) → `ToggleDef` row in `TabbedPauseMenu` CAMERA tab (~line 588+).
- Auto-singleton trap #1: anything auto-created with a MainMenu skip must ALSO be seeded in `MainMenuController.EnsureGameplaySingletons` / `EnsureGameplaySingletonsAsync` (`MainMenuController.cs:551+`).
- No CLI tests exist in this project (CLAUDE.md: Editor-only iteration). Verification per task = Coplay `check_compile_errors` + listed in-Editor visual checks. TDD steps are replaced accordingly.
- Nothing here needs SaveCollector/NewGameReset work: all state is UI-only or PlayerPrefs (handled by InputSettings).

## Sorting-order additions (contract)

```
VisorGlass          = 810   // tint/fresnel/scanlines — above hotbar(200)+compass(300), below letterbox(820) and clusters(830)
HelmetCondensation  = 838   // fog creeps OVER the readouts (functional low-O₂ feedback)
HelmetFrame         = 840   // helmet art above clusters(830), below phone(≈850)/toast(900)/pause(1000)
```

Known accepted quirks: the visor glass tints the compass (300) but sits under the vitals/boost clusters (830) so readouts stay crisp; the helmet frame remains visible over dialogue letterbox bars (like vitals already does) but is hidden by `HudVisibility` during the pod cinematic and by the HIDE HUD setting.

## Housing layout contract (reference units, 1920×1080 canvas)

Single source of truth = `HelmetHudLayout.cs`. Both the helmet art pieces and the cluster cards read these:

```
BottomLeftHousing  : window from bottom-left  corner, offset (16, 16),  size 380×230  (GForceHUD card + bezel)
BottomRightHousing : window from bottom-right corner, offset (16, 16),  size 360×260  (sized for MAX vitals height — ship rows + charging; card grows upward inside it)
TopBrowHousing     : window centered at top,  offset y 8,               size 640×72   (compass strip + heading badge)
Card inset within each window: 14 units (bezel ring + glass gap).
```

## Helmet texture contract (user-authored)

- Author at **3840×2160** (2 texture px = 1 canvas reference unit). PNG with alpha; center fully transparent.
- Import settings (user): Texture Type = Default (we sample via RawImage `uvRect`, no sprite slicing needed), sRGB on, Alpha Is Transparency on, mipmaps OFF, compression = High Quality or None, Max Size 4096, Non-Power-of-2 = None.
- The art is cut into **10 pieces** at runtime via RawImage uvRects; piece pixel-rects are serialized in `HelmetHudConfig` (defaults below) so if the art layout shifts, the user retunes rects in the Inspector — no re-code.
- **Corner pieces render at fixed size; span/edge pieces stretch.** Keep the art in span regions simple/uniform (a rim/strut that survives horizontal or vertical stretching). Recessed housing detail must live entirely inside corner/brow pieces.

Default template regions (texture pixels, **bottom-left origin**, matching the layout contract; window interiors transparent):

```
tlCorner   (0,    1800, 480, 360)      trCorner   (3360, 1800, 480, 360)
topBrow    (1280, 1980, 1280, 180)     // contains 1280×144 transparent compass window centered, window top 16px below texture top
topLeftSpan(480,  2064, 800, 96)       topRightSpan(2560, 2064, 800, 96)
leftEdge   (0,    460,  96,  1340)     rightEdge  (3744, 460,  96,  1340)
blHousing  (0,    0,   840, 560)       // contains 760×460 transparent window at inner offset (32,32)
brHousing  (3000, 0,   840, 560)       // mirrored
bottomSpan (840,  0,  2160, 96)
```

---

## File map

**Create** (all under `Assets/3 - Scripts/UI/Helmet/` — new folder, fine since there are no asmdefs):
- `HelmetHudPalette.cs` — static accent color + derived tints + `OnAccentChanged` event.
- `HelmetHudLayout.cs` — static housing-rect constants (contract above).
- `HelmetHudConfig.cs` — scene-placed MonoBehaviour: texture, piece rects, accent, intensities, sway gains. **User places + assigns.**
- `HelmetOverlayHUD.cs` — auto-singleton; builds frame canvas + 10 uvRect pieces; owns child overlays; gates on `fxHelmetOverlay`.
- `HelmetSway.cs` — camera-rotation + velocity driven damped sway applied to registered RectTransforms.
- `VisorGlassOverlay.cs` — tint + fresnel rim + scanlines (procedural textures).
- `CondensationOverlay.cs` — edge fog driven by `OxygenManager.SuitPercent`.
- `HelmetBezelKit.cs` — shared bezel/backplate/halo builder used by all three clusters.
- `HudBootFX.cs` — flicker + scanline-sweep power-on, generic, used by GForceHUD.

**Modify:**
- `Assets/3 - Scripts/UI/UILayer.cs` — add the three constants.
- `Assets/3 - Scripts/Survival/VitalsHUD.cs` — seat card in BottomRightHousing, bezel/emissive restyle, sway registration.
- `Assets/3 - Scripts/Ship/GForceHUD.cs` — seat card in BottomLeftHousing, bezel restyle, sway registration, boot-FX trigger on show edge.
- `Assets/3 - Scripts/UI/CompassHUD.cs` — seat strip+badge in TopBrowHousing, accent via palette. Keep sortingOrder 300.
- `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` — `fxHelmetOverlay`, `fxHelmetCondensation` flags + prefs lines (fields **appended at class end**).
- `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` — two CAMERA-tab ToggleDefs.
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `HelmetOverlayHUD` in both `EnsureGameplaySingletons` paths (trap #1).

**User does manually (everything else is scripted):**
1. Author/provide the 3840×2160 helmet texture (template above) + set the import settings listed.
2. Place an empty `HelmetHudConfig` GameObject in `1.6.7.7.7.unity` (under `--- Managers ---`), add the component, assign the texture, and (later) tune accent color / piece rects / intensities in the Inspector.
3. All play-mode visual verification and feel-tuning (sway gains, fog thresholds, glow strengths — all exposed on the config).

---

### Task 1: Palette, layout contract, sorting orders

**Files:**
- Create: `Assets/3 - Scripts/UI/Helmet/HelmetHudPalette.cs`
- Create: `Assets/3 - Scripts/UI/Helmet/HelmetHudLayout.cs`
- Modify: `Assets/3 - Scripts/UI/UILayer.cs`

- [ ] **Step 1: Write HelmetHudPalette.cs**

```csharp
using System;
using UnityEngine;

/// <summary>
/// Single source of truth for the helmet HUD accent color. The user hasn't
/// locked the accent yet, so everything routes through here: change
/// DefaultAccent (or tweak HelmetHudConfig.accentColor in the Inspector at
/// runtime) and every bezel/glow/glass layer re-tints via OnAccentChanged.
/// Defaults to the existing LED cyan (VitalsHUD/GForceHUD 0x5CC8FF) so the
/// helmet matches the current HUD family out of the box.
/// </summary>
public static class HelmetHudPalette
{
    public static readonly Color32 DefaultAccent = new Color32(0x5C, 0xC8, 0xFF, 0xFF);

    static Color32 _accent = DefaultAccent;
    public static Color32 Accent => _accent;

    /// Fired after the accent changes — subscribers re-tint their cached images.
    public static event Action OnAccentChanged;

    public static void SetAccent(Color32 c)
    {
        if (_accent.r == c.r && _accent.g == c.g && _accent.b == c.b && _accent.a == c.a) return;
        _accent = c;
        OnAccentChanged?.Invoke();
    }

    // Derived tints (recomputed on read so they track the live accent).
    public static Color AccentGlow   => WithAlpha(_accent, 0.55f);
    public static Color AccentFaint  => WithAlpha(_accent, 0.10f);
    public static Color BezelRing    => Color.Lerp((Color)_accent, Color.black, 0.55f);
    public static Color GlassBackplate => new Color(0.03f, 0.07f, 0.12f, 0.92f);
    public static Color GlassSheen   => WithAlpha(_accent, 0.35f);
    public static Color FrameTint    => Color.white;   // helmet art rendered as-authored

    static Color WithAlpha(Color32 c, float a)
    {
        Color col = c; col.a = a; return col;
    }
}
```

- [ ] **Step 2: Write HelmetHudLayout.cs**

```csharp
using UnityEngine;

/// <summary>
/// Housing layout contract in 1920×1080 canvas reference units. The helmet
/// frame pieces AND the three cluster cards both read these, so the recessed
/// screens and their contents stay aligned at every aspect ratio (all HUD
/// canvases share identical CanvasScaler settings — same coordinate space).
/// Windows are corner/center-anchored, which is what survives aspect change.
/// </summary>
public static class HelmetHudLayout
{
    // Window rects: offset from their anchor corner + size.
    public static readonly Vector2 BottomLeftOffset  = new Vector2(16f, 16f);
    public static readonly Vector2 BottomLeftSize    = new Vector2(380f, 230f);

    public static readonly Vector2 BottomRightOffset = new Vector2(16f, 16f); // measured from bottom-RIGHT corner
    public static readonly Vector2 BottomRightSize   = new Vector2(360f, 260f);

    public static readonly float   TopBrowYOffset    = 8f;                    // from top edge, centered horizontally
    public static readonly Vector2 TopBrowSize       = new Vector2(640f, 72f);

    /// Gap between the housing window edge and the cluster card (bezel ring + glass).
    public const float CardInset = 14f;
}
```

- [ ] **Step 3: Add sorting constants to UILayer.cs** — insert between `Hud` and `Toast`, with the documented reasons:

```csharp
    public const int VisorGlass         = 810;  // helmet glass tint/fresnel/scanlines — under letterbox(820) + clusters(830)
    public const int HelmetCondensation = 838;  // O2 fog creeps OVER the cluster readouts (functional feedback)
    public const int HelmetFrame        = 840;  // helmet interior art — above clusters, below phone(≈850)/toasts(900)
```

Also update the layer-order comment block at the top of the file to include the three new rows.

- [ ] **Step 4: Compile check** — Coplay `check_compile_errors`; expect none.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/Helmet/HelmetHudPalette.cs" "Assets/3 - Scripts/UI/Helmet/HelmetHudPalette.cs.meta" "Assets/3 - Scripts/UI/Helmet/HelmetHudLayout.cs" "Assets/3 - Scripts/UI/Helmet/HelmetHudLayout.cs.meta" "Assets/3 - Scripts/UI/UILayer.cs"
git commit -m "feat(helmet-hud): palette + housing layout contract + sorting layers"
```

*(Every commit in this plan: remember new `.cs` AND `.meta` files — repo trap. The `Helmet/` folder's own `.meta` too on the first commit.)*

---

### Task 2 (Phase 1): HelmetHudConfig + HelmetOverlayHUD frame layer

**Files:**
- Create: `Assets/3 - Scripts/UI/Helmet/HelmetHudConfig.cs`
- Create: `Assets/3 - Scripts/UI/Helmet/HelmetOverlayHUD.cs`
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs` (seed in `EnsureGameplaySingletons` AND `EnsureGameplaySingletonsAsync` — mirror an existing `if (X.Instance == null)` block)

- [ ] **Step 1: Write HelmetHudConfig.cs**

```csharp
using UnityEngine;

/// <summary>
/// Scene-placed settings holder for the helmet HUD (user places this in the
/// gameplay scene and assigns the texture — auto-created singletons can't
/// take Inspector references). HelmetOverlayHUD finds it lazily (throttled)
/// and rebuilds whenever Version changes, so every field is live-tweakable
/// in play mode. Piece rects are in TEXTURE PIXELS, BOTTOM-LEFT origin,
/// matching RawImage.uvRect math directly (template: 3840×2160, 2px = 1 ref unit).
/// </summary>
public class HelmetHudConfig : MonoBehaviour
{
    public static HelmetHudConfig Instance { get; private set; }

    [Header("Art")]
    public Texture2D helmetTexture;

    [Header("Accent (routed through HelmetHudPalette to every layer)")]
    public Color accentColor = new Color32(0x5C, 0xC8, 0xFF, 0xFF);

    [Header("Piece rects (texture px, bottom-left origin — template 3840×2160)")]
    public Rect tlCorner     = new Rect(0,    1800, 480, 360);
    public Rect trCorner     = new Rect(3360, 1800, 480, 360);
    public Rect topBrow      = new Rect(1280, 1980, 1280, 180);
    public Rect topLeftSpan  = new Rect(480,  2064, 800,  96);
    public Rect topRightSpan = new Rect(2560, 2064, 800,  96);
    public Rect leftEdge     = new Rect(0,    460,  96, 1340);
    public Rect rightEdge    = new Rect(3744, 460,  96, 1340);
    public Rect blHousing    = new Rect(0,    0,   840, 560);
    public Rect brHousing    = new Rect(3000, 0,   840, 560);
    public Rect bottomSpan   = new Rect(840,  0,  2160,  96);

    [Header("Visor glass")]
    [Range(0f, 0.3f)]  public float glassTintAlpha   = 0.05f;
    [Range(0f, 0.5f)]  public float fresnelAlpha     = 0.12f;
    [Range(0f, 0.15f)] public float scanlineAlpha    = 0.03f;

    [Header("Sway")]
    [Range(0f, 3f)] public float lookSwayGain     = 1.0f;
    [Range(0f, 3f)] public float moveSwayGain     = 1.0f;
    [Range(4f, 60f)] public float swayMaxOffset   = 18f;   // ref units
    [Range(1f, 20f)] public float swaySmoothing   = 8f;

    [Header("Condensation (suit O2 feedback)")]
    [Tooltip("Suit O2 fraction where fog starts creeping in.")]
    [Range(0f, 1f)] public float fogStartPercent  = 0.6f;
    [Tooltip("Suit O2 fraction where fog reaches full strength.")]
    [Range(0f, 1f)] public float fogFullPercent   = 0.1f;
    [Range(0f, 1f)] public float fogMaxAlpha      = 0.85f;

    /// Bumped on any Inspector change → HelmetOverlayHUD rebuilds/re-reads.
    public int Version { get; private set; }

    void Awake()
    {
        Instance = this;
        HelmetHudPalette.SetAccent(accentColor);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnValidate()
    {
        Version++;
        HelmetHudPalette.SetAccent(accentColor);
    }
}
```

- [ ] **Step 2: Write HelmetOverlayHUD.cs** — auto-singleton mirroring `SpaceDustInventory` (MainMenu skip + `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`), plus the frame builder:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Astronaut-helmet interior frame. Renders the user-authored helmet texture
/// as 10 RawImage pieces on a ScreenSpaceOverlay canvas (UILayer.HelmetFrame):
/// fixed-size corner/brow pieces anchored to their screen corners, stretchable
/// spans between them — so the recessed housings stay put relative to the
/// corner-anchored cluster cards at ANY aspect ratio. One stretched full-screen
/// image would drift against the clusters; pieces don't.
/// Owns the visor-glass + condensation child overlays (Phases 3–4) and the
/// HelmetSway driver. Content waits until a HelmetHudConfig with a texture
/// exists in the scene (throttled find), and rebuilds when config.Version bumps.
/// Gated by InputSettings.fxHelmetOverlay (via CameraEffectsManager.Instance.Input).
/// </summary>
public class HelmetOverlayHUD : MonoBehaviour
{
    public static HelmetOverlayHUD Instance { get; private set; }

    Canvas _frameCanvas;
    RectTransform _swayRoot;
    HelmetHudConfig _config;
    float _nextConfigFind;
    int _builtVersion = int.MinValue;
    HelmetSway _sway;                    // Phase 3 (null-safe until then)
    VisorGlassOverlay _glass;            // Phase 3
    CondensationOverlay _condensation;   // Phase 4

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HelmetOverlayHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<HelmetOverlayHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildFrameCanvas();
        HelmetHudPalette.OnAccentChanged += Retint;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        HelmetHudPalette.OnAccentChanged -= Retint;
    }

    void Update()
    {
        // Throttled config find — the config is a scene object that can die on
        // scene reload; never FindObjectOfType per frame (repo rule).
        if (_config == null && Time.unscaledTime >= _nextConfigFind)
        {
            _nextConfigFind = Time.unscaledTime + 0.5f;
            _config = HelmetHudConfig.Instance != null
                ? HelmetHudConfig.Instance
                : FindObjectOfType<HelmetHudConfig>();
        }

        bool haveArt = _config != null && _config.helmetTexture != null;
        bool fxOn = FxEnabled();
        bool show = haveArt && fxOn;
        if (_frameCanvas != null && _frameCanvas.enabled != show
            && SceneManager.GetActiveScene().name != "MainMenu")
            _frameCanvas.enabled = show;

        if (haveArt && _config.Version != _builtVersion)
        {
            _builtVersion = _config.Version;
            RebuildPieces();
        }
    }

    static bool FxEnabled()
    {
        var mgr = CameraEffectsManager.Instance;
        // Default ON when settings aren't up yet (early boot) — the helmet is
        // the intended look, not an optional garnish.
        return mgr == null || mgr.Input == null || mgr.Input.fxHelmetOverlay;
    }

    void BuildFrameCanvas()
    {
        _frameCanvas = gameObject.AddComponent<Canvas>();
        _frameCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _frameCanvas.sortingOrder = UILayer.HelmetFrame;
        HUDSceneGate.Register(_frameCanvas);
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        HudVisibility.RegisterHideable(_frameCanvas);   // HIDE HUD + pod cinematic

        var rootGo = new GameObject("SwayRoot", typeof(RectTransform));
        rootGo.transform.SetParent(transform, false);
        _swayRoot = rootGo.GetComponent<RectTransform>();
        _swayRoot.anchorMin = Vector2.zero;
        _swayRoot.anchorMax = Vector2.one;
        _swayRoot.offsetMin = Vector2.zero;
        _swayRoot.offsetMax = Vector2.zero;
    }

    void RebuildPieces()
    {
        for (int i = _swayRoot.childCount - 1; i >= 0; i--)
            Destroy(_swayRoot.GetChild(i).gameObject);

        var c = _config;
        var tex = c.helmetTexture;
        // Fixed corner/brow pieces (size = px/2 → ref units).
        AddCorner("TL", c.tlCorner, tex, new Vector2(0f, 1f));
        AddCorner("TR", c.trCorner, tex, new Vector2(1f, 1f));
        AddCorner("BLHousing", c.blHousing, tex, new Vector2(0f, 0f));
        AddCorner("BRHousing", c.brHousing, tex, new Vector2(1f, 0f));
        AddBrow(c.topBrow, tex);
        // Stretch spans between the fixed pieces.
        AddHSpan("TopLeftSpan",  c.topLeftSpan,  tex, 0f,   c.tlCorner.width * 0.5f,   0.5f, -c.topBrow.width * 0.25f, true);
        AddHSpan("TopRightSpan", c.topRightSpan, tex, 0.5f, c.topBrow.width * 0.25f,   1f,   -c.trCorner.width * 0.5f, true);
        AddHSpan("BottomSpan",   c.bottomSpan,   tex, 0f,   c.blHousing.width * 0.5f,  1f,   -c.brHousing.width * 0.5f, false);
        AddVEdge("LeftEdge",  c.leftEdge,  tex, 0f, c.blHousing.height * 0.5f, c.tlCorner.height * 0.5f);
        AddVEdge("RightEdge", c.rightEdge, tex, 1f, c.brHousing.height * 0.5f, c.trCorner.height * 0.5f);
    }

    RawImage NewPiece(string name, Rect px, Texture2D tex)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_swayRoot, false);
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.uvRect = new Rect(px.x / tex.width, px.y / tex.height,
                              px.width / tex.width, px.height / tex.height);
        img.color = HelmetHudPalette.FrameTint;
        img.raycastTarget = false;
        return img;
    }

    void AddCorner(string name, Rect px, Texture2D tex, Vector2 corner)
    {
        var img = NewPiece(name, px, tex);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = corner;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(px.width * 0.5f, px.height * 0.5f);
    }

    void AddBrow(Rect px, Texture2D tex)
    {
        var img = NewPiece("TopBrow", px, tex);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(px.width * 0.5f, px.height * 0.5f);
    }

    // Horizontal span pinned to top (top=true) or bottom edge, stretching from
    // (anchorXMin + insetLeft) to (anchorXMax + insetRight); fixed height.
    void AddHSpan(string name, Rect px, Texture2D tex,
                  float anchorXMin, float insetLeft, float anchorXMax, float insetRight, bool top)
    {
        var img = NewPiece(name, px, tex);
        var rt = (RectTransform)img.transform;
        float y = top ? 1f : 0f;
        rt.anchorMin = new Vector2(anchorXMin, y);
        rt.anchorMax = new Vector2(anchorXMax, y);
        rt.pivot = new Vector2(0.5f, y);
        float h = px.height * 0.5f;
        rt.offsetMin = new Vector2(insetLeft,  top ? -h : 0f);
        rt.offsetMax = new Vector2(insetRight, top ? 0f : h);
    }

    // Vertical edge pinned to left (side=0) or right (side=1), stretching
    // between the bottom housing top and the top corner bottom; fixed width.
    void AddVEdge(string name, Rect px, Texture2D tex, float side, float insetBottom, float insetTop)
    {
        var img = NewPiece(name, px, tex);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = new Vector2(side, 0f);
        rt.anchorMax = new Vector2(side, 1f);
        rt.pivot = new Vector2(side, 0.5f);
        float w = px.width * 0.5f;
        rt.offsetMin = new Vector2(side == 0f ? 0f : -w, insetBottom);
        rt.offsetMax = new Vector2(side == 0f ? w : 0f, -insetTop);
    }

    void Retint()
    {
        if (_swayRoot == null) return;
        var images = _swayRoot.GetComponentsInChildren<RawImage>(true);
        for (int i = 0; i < images.Length; i++) images[i].color = HelmetHudPalette.FrameTint;
    }
}
```

*(Note: `HelmetSway`, `VisorGlassOverlay`, `CondensationOverlay` fields exist but are wired in Phases 3–4 — leave the fields, don't reference the types until those files exist. If compiling this task standalone, comment the three fields until Phase 3; or land Task 5's `HelmetSway.cs` in the same commit. Simplest: keep only `_frameCanvas`/`_swayRoot`/`_config` fields in this task and add the others in their phases.)*

- [ ] **Step 3: Seed in MainMenuController** — in BOTH `EnsureGameplaySingletons()` and the `EnsureGameplaySingletonsAsync` coroutine (`MainMenuController.cs:551+`), mirror an adjacent block:

```csharp
        if (HelmetOverlayHUD.Instance == null)
        {
            var go = new GameObject("HelmetOverlayHUD");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<HelmetOverlayHUD>();
        }
```

- [ ] **Step 4: Compile check** — Coplay `check_compile_errors`.

- [ ] **Step 5: USER — place config + texture.** Add `HelmetHudConfig` GameObject to `1.6.7.7.7.unity` under `--- Managers ---` (verify the organizer name in the Editor first), assign the helmet texture. Press Play: helmet frame appears; housings sit at the contract positions; resize the Game view through 16:9 / 21:9 / 16:10 — corner pieces and brow must hug their anchors while spans stretch.

- [ ] **Step 6: Commit** (`feat(helmet-hud): frame overlay canvas + config + singleton seeding`). Scene file with the placed config is the user's to commit alongside or after.

---

### Task 3 (Phase 2a): HelmetBezelKit — bezel + backlit-glass + emissive halo builder

**Files:**
- Create: `Assets/3 - Scripts/UI/Helmet/HelmetBezelKit.cs`

- [ ] **Step 1: Write HelmetBezelKit.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared "recessed display" dressing for the three helmet housings: a dark
/// metal bezel ring, a glass backplate with a top sheen line, and a soft
/// accent halo behind the content ("backlit screen"). REAL camera bloom can't
/// reach ScreenSpaceOverlay UI (KinoBloom runs in OnRenderImage, before UI
/// composites) — the halo sprite IS the bloom, faked the same way the existing
/// HUDs fake glow with Shadow components. All accent-tinted images register
/// for live re-tint via HelmetHudPalette.OnAccentChanged.
/// </summary>
public static class HelmetBezelKit
{
    class TintTarget { public Image img; public System.Func<Color> color; }
    static readonly List<TintTarget> _tints = new List<TintTarget>();
    static bool _subscribed;

    /// Builds bezel layers as the FIRST children of `card` (behind its content),
    /// expanded `pad` units beyond the card rect, ignoring any layout group.
    public static void BuildBezel(RectTransform card, float pad)
    {
        EnsureSubscribed();
        // Halo (outermost, softest) → backplate → ring → sheen (top edge).
        var halo = NewLayer(card, "BezelHalo", pad + 18f);
        halo.sprite = GetRadialHaloSprite();
        Track(halo, () => HelmetHudPalette.AccentGlow);

        var plate = NewLayer(card, "BezelGlass", pad);
        plate.sprite = UIPanelSprites.GetBeveledPanel();
        plate.type = Image.Type.Sliced;
        Track(plate, () => HelmetHudPalette.GlassBackplate);

        var ring = NewLayer(card, "BezelRing", pad);
        ring.sprite = UIPanelSprites.GetBeveledOutline();
        ring.type = Image.Type.Sliced;
        Track(ring, () => HelmetHudPalette.BezelRing);

        var sheenRt = NewRT(card, "BezelSheen");
        sheenRt.anchorMin = new Vector2(0f, 1f);
        sheenRt.anchorMax = new Vector2(1f, 1f);
        sheenRt.pivot = new Vector2(0.5f, 1f);
        sheenRt.offsetMin = new Vector2(-pad + 6f, -1f);
        sheenRt.offsetMax = new Vector2(pad - 6f, pad - 1f);
        sheenRt.sizeDelta = new Vector2(sheenRt.sizeDelta.x, 1f);
        var sheen = sheenRt.gameObject.AddComponent<Image>();
        sheen.raycastTarget = false;
        sheenRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Track(sheen, () => HelmetHudPalette.GlassSheen);

        // Draw order: push all four to the front-of-list (behind content).
        sheenRt.SetAsFirstSibling();
        ((RectTransform)ring.transform).SetAsFirstSibling();
        ((RectTransform)plate.transform).SetAsFirstSibling();
        ((RectTransform)halo.transform).SetAsFirstSibling();
    }

    static Image NewLayer(RectTransform card, string name, float pad)
    {
        var rt = NewRT(card, name);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-pad, -pad);
        rt.offsetMax = new Vector2(pad, pad);
        rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    static RectTransform NewRT(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Track(Image img, System.Func<Color> color)
    {
        img.color = color();
        _tints.Add(new TintTarget { img = img, color = color });
    }

    static void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        HelmetHudPalette.OnAccentChanged += () =>
        {
            for (int i = _tints.Count - 1; i >= 0; i--)
            {
                if (_tints[i].img == null) { _tints.RemoveAt(i); continue; }
                _tints[i].img.color = _tints[i].color();
            }
        };
    }

    // Soft radial halo sprite (procedural, cached) — the faked bloom.
    static Sprite _halo;
    static Sprite GetRadialHaloSprite()
    {
        if (_halo != null) return _halo;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                float a = Mathf.Pow(1f - d, 2.2f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        _halo = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
                              0u, SpriteMeshType.FullRect, new Vector4(40, 40, 40, 40));
        _halo.name = "HelmetBezelHalo";
        return _halo;
    }
}
```

- [ ] **Step 2: Compile check.**
- [ ] **Step 3: Commit** (`feat(helmet-hud): shared bezel/backlit-glass kit`).

---

### Task 4 (Phase 2b): Seat the three clusters in their housings

**Files:**
- Modify: `Assets/3 - Scripts/Survival/VitalsHUD.cs`
- Modify: `Assets/3 - Scripts/Ship/GForceHUD.cs`
- Modify: `Assets/3 - Scripts/UI/CompassHUD.cs`

All three components are runtime-created (no scene serialization), so changing field defaults is safe. Do NOT reorder existing serialized fields.

- [ ] **Step 1: VitalsHUD** — in `BuildCanvas()` (`VitalsHUD.cs:283-289`), replace the card placement so it derives from the contract, and add the bezel + sway registration after `_cardRT = card;`:

```csharp
        // Seat in the helmet's bottom-right housing (HelmetHudLayout contract).
        card.anchoredPosition = new Vector2(
            -(HelmetHudLayout.BottomRightOffset.x + HelmetHudLayout.CardInset),
            HelmetHudLayout.BottomRightOffset.y + HelmetHudLayout.CardInset);
        card.sizeDelta = new Vector2(
            HelmetHudLayout.BottomRightSize.x - HelmetHudLayout.CardInset * 2f, 0f);
        _cardRT = card;
        HelmetBezelKit.BuildBezel(card, HelmetHudLayout.CardInset - 4f);
        HelmetSway.Register(card, 0.85f);   // Phase 3 file; if landing Phase 2 first, add this line in Phase 3
```

Then restyle for the backlit look: change `bg.color = PillBgColor;` to a more transparent glass value `new Color32(0x0A, 0x18, 0x28, 0xB0)` (the bezel backplate now supplies depth), and change the LED accent to track the palette: `ledImg.color = HelmetHudPalette.Accent;`. The card's own `Border` image can stay (it reads as the inner screen edge).

The pulse-warning, row-toggling, and ContentSizeFitter behavior are untouched — the card grows upward inside the 260-unit-tall housing (pivot (1,0)), which the housing is sized for.

- [ ] **Step 2: GForceHUD** — in `BuildCanvas()` (`GForceHUD.cs:497-503`):

```csharp
        card.anchoredPosition = new Vector2(
            HelmetHudLayout.BottomLeftOffset.x + HelmetHudLayout.CardInset,
            HelmetHudLayout.BottomLeftOffset.y + HelmetHudLayout.CardInset);
        card.sizeDelta = new Vector2(
            HelmetHudLayout.BottomLeftSize.x - HelmetHudLayout.CardInset * 2f, 0f);
        _cardRT = card;
        HelmetBezelKit.BuildBezel(card, HelmetHudLayout.CardInset - 4f);
        HelmetSway.Register(card, 0.85f);
```

Same restyle: bg alpha to 0xB0, `ledImg.color = HelmetHudPalette.Accent;`.

- [ ] **Step 3: CompassHUD** — seat strip + badge in the brow window. In `BuildCanvas()` change the defaults driving `_strip` and `_badgeRT` (`CompassHUD.cs:384-388` and `447-451`): set `stripWidth` default to `HelmetHudLayout.TopBrowSize.x - HelmetHudLayout.CardInset * 2f` (612) — do this by assigning in `BuildCanvas` before use rather than editing the serialized default, so an Inspector override still wins:

```csharp
        stripWidth = HelmetHudLayout.TopBrowSize.x - HelmetHudLayout.CardInset * 2f;
        topMargin  = HelmetHudLayout.TopBrowYOffset + HelmetHudLayout.CardInset + 22f; // badge row sits above the strip inside the window
```

and register sway: `HelmetSway.Register(_strip, 0.85f); HelmetSway.Register(_badgeRT, 0.85f);` at the end of `BuildCanvas()`. Route the accent statics through the palette where cheap: `CenterTickColor`/`StripSheenColor` usages → `HelmetHudPalette.Accent` / `HelmetHudPalette.GlassSheen` at build time (compass rebuilds only per process run; live re-tint here is not required — note it). Keep sortingOrder 300 (preserves letterbox-covers-compass during dialogue). No bezel kit on the compass — the brow window art + existing strip bg already read as a recessed slit; add only the halo: optional, skip by default.

- [ ] **Step 4: Compile check.**
- [ ] **Step 5: USER visual check** — Play: vitals card sits inside the BR housing with bezel + glow; boost card in BL housing (unlock jetpack via cheats or pilot a ship to see it); compass inside the brow. Check 21:9 and 16:10 Game-view presets again. Toggle HIDE HUD in CAMERA tab: clusters AND helmet frame all hide.
- [ ] **Step 6: Commit** (`feat(helmet-hud): seat vitals/boost/compass clusters into helmet housings with bezels`).

---

### Task 5 (Phase 3a): HelmetSway

**Files:**
- Create: `Assets/3 - Scripts/UI/Helmet/HelmetSway.cs`
- Modify: `Assets/3 - Scripts/UI/Helmet/HelmetOverlayHUD.cs` (add `_sway = gameObject.AddComponent<HelmetSway>();` in `Awake` and `HelmetSway.Register(_swayRoot, 1f);` in `BuildFrameCanvas`)

- [ ] **Step 1: Write HelmetSway.cs** — camera-rotation-delta driven (device-agnostic: works for mouse AND controller look without touching the input facade), plus surface-velocity bob:

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Damped helmet sway: registered RectTransforms get a shared 2D offset
/// derived from camera angular velocity (look) and player surface velocity
/// (movement), spring-damped back to center. Driven from camera rotation
/// deltas rather than input axes so mouse, pad, and cinematic cameras all
/// produce consistent sway. Runs in LateUpdate after camera controllers.
/// Offsets are in canvas reference units and multiply per-layer (frame 1.0,
/// clusters 0.85) for a subtle depth parallax.
/// </summary>
public class HelmetSway : MonoBehaviour
{
    class Entry { public RectTransform rt; public Vector2 basePos; public float mult; }
    static readonly List<Entry> _entries = new List<Entry>();

    public static void Register(RectTransform rt, float multiplier)
    {
        if (rt == null) return;
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].rt == rt) return;
        _entries.Add(new Entry { rt = rt, basePos = rt.anchoredPosition, mult = multiplier });
    }

    Camera _cam;
    float _nextCamFind;
    Quaternion _lastCamRot;
    bool _hasLast;
    Vector2 _offset, _offsetVel;
    PlayerController _player;
    float _nextPlayerFind;

    void LateUpdate()
    {
        var cfg = HelmetHudConfig.Instance;
        if (_cam == null && Time.unscaledTime >= _nextCamFind)
        { _nextCamFind = Time.unscaledTime + 0.5f; _cam = Camera.main; _hasLast = false; }
        if (_player == null && Time.unscaledTime >= _nextPlayerFind)
        { _nextPlayerFind = Time.unscaledTime + 0.5f; _player = FindObjectOfType<PlayerController>(); }

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        Vector2 target = Vector2.zero;
        if (cfg != null && _cam != null)
        {
            if (_hasLast)
            {
                // Angular delta in camera-local axes → yaw/pitch rates (deg/s).
                Quaternion delta = Quaternion.Inverse(_lastCamRot) * _cam.transform.rotation;
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                Vector3 rate = axis * (angle / dt);
                float yawRate = rate.y, pitchRate = rate.x;
                target.x += Mathf.Clamp(-yawRate  * 0.06f * cfg.lookSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
                target.y += Mathf.Clamp( pitchRate * 0.06f * cfg.lookSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
            }
            _lastCamRot = _cam.transform.rotation;
            _hasLast = true;

            if (_player != null && _player.isActiveAndEnabled)
            {
                Vector3 vLocal = _cam.transform.InverseTransformDirection(_player.SurfaceVelocity);
                target.x += Mathf.Clamp(-vLocal.x * 0.8f * cfg.moveSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
                target.y += Mathf.Clamp(-vLocal.y * 0.8f * cfg.moveSwayGain, -cfg.swayMaxOffset, cfg.swayMaxOffset);
            }
            target = Vector2.ClampMagnitude(target, cfg.swayMaxOffset);
        }

        float smooth = cfg != null ? 1f / Mathf.Max(0.01f, cfg.swaySmoothing) : 0.12f;
        _offset = Vector2.SmoothDamp(_offset, target, ref _offsetVel, smooth, Mathf.Infinity, dt);

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (e.rt == null) { _entries.RemoveAt(i); continue; }
            e.rt.anchoredPosition = e.basePos + _offset * e.mult;
        }
    }
}
```

**Gotcha to preserve:** `Register` snapshots `basePos` — so register AFTER the final layout position is set (all Task 4 call sites already do). `CompassHUD` gameplay markers move `wp.Ui`, not `_strip`, so strip sway composes fine.

- [ ] **Step 2: Compile check; USER feel check** (walk, sprint, jetpack, whip the camera — HUD lags a beat and settles; no drift at rest; nothing exceeds ~18 units).
- [ ] **Step 3: Commit** (`feat(helmet-hud): damped look/movement helmet sway`).

---

### Task 6 (Phase 3b): Visor glass layer + settings toggles

**Files:**
- Create: `Assets/3 - Scripts/UI/Helmet/VisorGlassOverlay.cs`
- Modify: `Assets/3 - Scripts/UI/Helmet/HelmetOverlayHUD.cs` (create child in `Awake`, gate its canvas with the frame in `Update`)
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs`
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs`

- [ ] **Step 1: Write VisorGlassOverlay.cs** — own child GameObject + canvas at `UILayer.VisorGlass`, HUDSceneGate + HudVisibility registered, containing three stretched layers under a sway-registered root (`HelmetSway.Register(root, 1f)`):
  1. **Tint**: full-screen `Image`, color = accent at `config.glassTintAlpha` (live via `OnAccentChanged` + per-frame alpha change-detect against config).
  2. **Fresnel rim**: full-screen `RawImage`, procedural 512×512 texture, alpha = `pow(saturate((d-0.55)/0.45), 1.8)` of normalized center distance (elliptical — normalize x/y separately so it hugs all four edges), tinted accent, alpha `config.fresnelAlpha`.
  3. **Scanlines**: `RawImage` with a 1×4 texture (rows: a=0, a=0, a=1, a=0), `wrapMode = Repeat`, and per-frame-cheap `uvRect = new Rect(0, 0, 1, Screen.height / 3f)` recomputed only when `Screen.height` changes; color = accent at `config.scanlineAlpha`.
  Follow the same procedural-texture style as `CompassHUD.MakeFadedBarTexture`. All images `raycastTarget = false`.
- [ ] **Step 2: InputSettings** — APPEND at the very end of the serialized fields (before the closing brace region of tunables, never mid-class):

```csharp
	[Header("Helmet HUD overlay")]
	public bool fxHelmetOverlay = true;       // helmet frame + visor glass + sway
	public bool fxHelmetCondensation = true;  // low-O2 fog (functional feedback — leaving it on is recommended)
```

Plus the Load lines next to `InputSettings.cs:322` (`PlayerPrefs.GetInt(nameof(fxHelmetOverlay), 1) != 0;` etc., default 1) and Save lines next to `:424`.

- [ ] **Step 3: TabbedPauseMenu** — in the CAMERA tab under the "SURVIVAL & CINEMATIC" header (~line 605), add:

```csharp
                    new ToggleDef { label = "HELMET OVERLAY",     get = () => _input != null && _input.fxHelmetOverlay,      set = v => { if (_input != null) _input.fxHelmetOverlay = v; } },
                    new ToggleDef { label = "HELMET CONDENSATION",get = () => _input != null && _input.fxHelmetCondensation, set = v => { if (_input != null) _input.fxHelmetCondensation = v; } },
```

- [ ] **Step 4: HelmetOverlayHUD.Update** — extend the gate: glass canvas enabled = frame `show`; condensation gate added in Phase 4 (`show && Input.fxHelmetCondensation`).
- [ ] **Step 5: World-view edge aberration** — no new code: the existing `ChromaticAberrationEffect` (CAMERA tab toggle) already provides it. USER: verify `CHROMATIC ABERRATION` is on and, if the edge split should strengthen with the helmet, nudge `fxChromaticAberrationIntensity` (slider already exists). *Optional stretch (skip by default): UI-side aberration on the helmet art via two extra RawImage copies of the fresnel rim offset ±1.5 units, tinted red/blue at 25% alpha.*
- [ ] **Step 6: Compile check; USER visual check** (subtle tint/rim/scanlines; toggles work live from pause menu; scanlines don't shimmer when resizing).
- [ ] **Step 7: Commit** (`feat(helmet-hud): visor glass layer + pause-menu toggles`).

---

### Task 7 (Phase 4): Condensation / O₂ fog

**Files:**
- Create: `Assets/3 - Scripts/UI/Helmet/CondensationOverlay.cs`
- Modify: `Assets/3 - Scripts/UI/Helmet/HelmetOverlayHUD.cs` (create child, gate on `fxHelmetCondensation`)

- [ ] **Step 1: Write CondensationOverlay.cs** — child canvas at `UILayer.HelmetCondensation` (above the readouts — fogging the screens IS the feedback), HUDSceneGate + HudVisibility registered, sway-registered root, one full-screen `RawImage`:
  - **Texture** (procedural 512×512, built once): `alpha = edgeMask * (0.55 + 0.45 * perlin)` where `edgeMask = pow(saturate((d-0.35)/0.65), 1.6)` of elliptical center distance and `perlin = Mathf.PerlinNoise(x*6f, y*6f)` — droplety fog that's dense at the rim, clear in the center. Color pale ice-blue `(0.75, 0.85, 0.95)`.
  - **Driver** (`Update`, change-detected alpha writes):

```csharp
        float suit = OxygenManager.Instance != null ? OxygenManager.Instance.SuitPercent : 1f;
        var cfg = HelmetHudConfig.Instance;
        float start = cfg != null ? cfg.fogStartPercent : 0.6f;
        float full  = cfg != null ? cfg.fogFullPercent  : 0.1f;
        float max   = cfg != null ? cfg.fogMaxAlpha     : 0.85f;
        float t = Mathf.InverseLerp(start, full, suit);          // 0 above start → 1 at full
        // Urgency pulse below the existing suit-alarm threshold (0.25) so the
        // fog breathes in time with the O2 alarm beeps.
        if (suit < 0.25f) t *= 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 4.8f);
        _targetAlpha = t * max;
        _alpha = Mathf.Lerp(_alpha, _targetAlpha, Time.unscaledDeltaTime * 1.5f);  // fog creeps, never snaps
        // uv drift sells "living" moisture:
        _img.uvRect = new Rect(Mathf.Sin(Time.unscaledTime * 0.03f) * 0.01f,
                               Time.unscaledTime * 0.004f % 1f, 1f, 1f);           // texture wrapMode = Repeat
```

  Disable the canvas entirely when `_alpha < 0.004f` (no zero-alpha render cost — mirrors `CameraEffectsManager.GateOverlayCanvas` reasoning). Recovery (breathable air / pressurized base) clears fog automatically since `SuitPercent` refills fast (`suitRefillRate = 24/s`).
- [ ] **Step 2: Compile check; USER check** — cheat/fly above Humble Abode's breathable midpoint (or open the ship hatch in space): fog creeps in below 60% suit O₂, pulses below 25%, clears on re-entry. Confirm the vitals SUIT O2 row remains legible until ~full fog.
- [ ] **Step 3: Commit** (`feat(helmet-hud): O2-driven visor condensation`).

---

### Task 8 (Phase 5): Boost-cluster power-on FX

**Files:**
- Create: `Assets/3 - Scripts/UI/Helmet/HudBootFX.cs`
- Modify: `Assets/3 - Scripts/Ship/GForceHUD.cs` (show-edge trigger in `Update`, `GForceHUD.cs:147-151`)

- [ ] **Step 1: Write HudBootFX.cs**

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Screen power-on" for a HUD cluster: a short alpha flicker on the cluster's
/// CanvasGroup followed by a bright accent scanline sweeping down the card.
/// Generic — GForceHUD uses it when the boost cluster appears; any housing can
/// call Play. Unscaled time (pause/cinematic safe). The sweep bar is created
/// per play and destroyed after; a RectMask2D on the card clips it.
/// </summary>
public class HudBootFX : MonoBehaviour
{
    Coroutine _running;

    public static void Play(CanvasGroup group, RectTransform card)
    {
        if (group == null || card == null) return;
        var fx = group.GetComponent<HudBootFX>();
        if (fx == null) fx = group.gameObject.AddComponent<HudBootFX>();
        if (fx._running != null) fx.StopCoroutine(fx._running);
        fx._running = fx.StartCoroutine(fx.Run(group, card));
    }

    IEnumerator Run(CanvasGroup group, RectTransform card)
    {
        if (card.GetComponent<RectMask2D>() == null) card.gameObject.AddComponent<RectMask2D>();

        // Flicker: (time, alpha) keyframes over ~0.28s.
        float[] t = { 0f, 0.05f, 0.09f, 0.14f, 0.18f, 0.24f, 0.28f };
        float[] a = { 0f, 1f,    0.05f, 1f,    0.25f, 1f,    1f    };
        float elapsed = 0f;
        int k = 0;
        while (elapsed < t[t.Length - 1])
        {
            elapsed += Time.unscaledDeltaTime;
            while (k < t.Length - 2 && elapsed > t[k + 1]) k++;
            float seg = Mathf.InverseLerp(t[k], t[k + 1], elapsed);
            group.alpha = Mathf.Lerp(a[k], a[k + 1], seg);
            yield return null;
        }
        group.alpha = 1f;

        // Scanline sweep: accent bar, top → bottom over 0.3s, fading out.
        var barGo = new GameObject("BootSweep", typeof(RectTransform));
        barGo.transform.SetParent(card, false);
        var rt = (RectTransform)barGo.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 14f);
        var img = barGo.AddComponent<Image>();
        Color c = HelmetHudPalette.Accent;
        img.raycastTarget = false;
        var le = barGo.AddComponent<LayoutElement>(); le.ignoreLayout = true;

        float h = card.rect.height;
        const float dur = 0.3f;
        for (float s = 0f; s < dur; s += Time.unscaledDeltaTime)
        {
            float p = s / dur;
            rt.anchoredPosition = new Vector2(0f, -p * (h + 14f));
            c.a = 0.65f * (1f - p * p);
            img.color = c;
            yield return null;
        }
        Destroy(barGo);
        _running = null;
    }
}
```

- [ ] **Step 2: GForceHUD trigger** — the show gate at `GForceHUD.cs:147-151` currently flips `_canvas.enabled`. Add an edge field (append any new serialized fields at class end; this one is private/non-serialized so placement near the other private state is fine): `bool _wasShown;` and inside the gate:

```csharp
        if (_canvas != null && _canvas.enabled != show) _canvas.enabled = show;
        if (show && !_wasShown)
            HudBootFX.Play(GetComponent<CanvasGroup>(), _cardRT);   // screen powers on
        _wasShown = show;
```

Note the map-open case: closing the map replays the boot — decide by feel; if unwanted, only fire when the *reason* is equip/pilot: gate the FX on `!PlayerController.isMapOpen` having been the previous blocker (track `bool _hiddenByMapOnly` from the same booleans the gate already computes). Start simple (replay on every show) and let the user judge.

- [ ] **Step 3: Compile check; USER check** — equip jetpack / board ship: flicker + sweep; drone test mission (`DroneController`) also triggers it. Verify HIDE HUD doesn't fight it (HudVisibility multiplies a different CanvasGroup? — **it doesn't**: GForceHUD's group is unregistered, and BootFX drives that same group; confirm HIDE HUD still hides GForceHUD via... it currently does NOT register with HudVisibility, matching existing behavior — unchanged).
- [ ] **Step 4: Commit** (`feat(helmet-hud): boost-cluster power-on flicker + scanline sweep`).

---

### Task 9: Final verification sweep

- [ ] Compile clean (Coplay `check_compile_errors`).
- [ ] USER full pass at 16:9, 21:9, 16:10 window sizes: housing alignment, sway, glass, fog, boot FX, HIDE HUD toggle, pause-menu toggles, map open/close, dialogue letterbox (helmet stays, compass ducks under bars — pre-existing behavior).
- [ ] **Build sanity check** (trap #1): make a Windows build, confirm the helmet appears after PLAY from the main menu (proves the `EnsureGameplaySingletons` seeding). *(Known environment caveat: local build crashes may be the AMD driver issue — judge by whether the helmet renders, not by process stability.)*
- [ ] Update `docs/CURRENT_STATE_AUDIT.md` HUD section with the helmet layer + new sorting orders.
- [ ] Commit docs update.

---

## Self-review notes

- **Spec coverage:** helmet frame w/ aspect-safe housings (Task 2), cluster seating + bezels + emissive (Tasks 3–4), glass fresnel/scanlines/tint/aberration + sway (Tasks 5–6), O₂ condensation (Task 7), boot FX (Task 8), accent as a single tweakable (Task 1 + config), user-vs-Claude split (File map + USER steps).
- **Bloom requirement is deliberately reinterpreted** (faked halo, not camera bloom) — architectural impossibility on overlay UI documented in findings; flag this to the user before executing.
- **Type consistency check:** `HelmetHudPalette.Accent/AccentGlow/GlassBackplate/BezelRing/GlassSheen/FrameTint`, `HelmetHudLayout.BottomLeftOffset/Size, BottomRightOffset/Size, TopBrowYOffset/TopBrowSize, CardInset`, `HelmetHudConfig.Instance/Version/helmetTexture/…gains`, `HelmetSway.Register(rt, mult)`, `HelmetBezelKit.BuildBezel(card, pad)`, `HudBootFX.Play(group, card)` — used consistently across tasks.
- **Ordering:** Task 4 references `HelmetSway.Register` (Task 5 file). Either land Task 5 before Task 4, or comment the three `Register` lines until Task 5 — executor's choice; landing 5 before 4 is cleaner (noted inline).
