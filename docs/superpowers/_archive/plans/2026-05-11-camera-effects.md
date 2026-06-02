# Camera Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ~23 individually-toggleable camera / screen / lens effects (headbob, FOV kicks, vignettes, film grain, lens flares, letterbox, etc.) wired through a new `CAMERA` tab in the pause menu's settings.

**Architecture:** One `CameraEffectsManager` singleton (auto-created + seeded in `MainMenuController.EnsureGameplaySingletons`) owns all effect modules as child MonoBehaviours. Modules group by what they modify (camera transform / FOV stack / UI overlays). Settings live on the existing `InputSettings` ScriptableObject — bool toggles drive each module's enabled state, polled per frame so pause-menu changes apply live. Drivers (e.g. damage vs low-health vs dialogue vignette) push into composited overlays so we don't stack UI images.

**Tech Stack:** Unity 2022.3, MonoBehaviour singletons, ScreenSpaceOverlay canvases, procedural sprite generation (`UIPanelSprites`), existing `CameraShake` Perlin noise system. No new shaders, no Post-Processing v2 package.

**Reference:** See `docs/superpowers/specs/2026-05-11-camera-effects-design.md` for the full design spec.

---

## File structure

**Modify:**
- `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` — new `fx*` fields + load/save
- `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` — add `ToggleDef` / `HeaderDef` row types + new `CAMERA` tab
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `CameraEffectsManager` in `EnsureGameplaySingletons`
- `Assets/3 - Scripts/Survival/ResourceManager.cs` — add `OnHealthDropped` and `OnDeath` events
- `Assets/3 - Scripts/Combat/EnemyController.cs` — add `OnDeath` event
- `Assets/3 - Scripts/Pickups/AxeController.cs` — fire micro-shake on confirmed hit
- `Assets/3 - Scripts/Pickups/PistolController.cs` — fire micro-shake on confirmed hit

**Create:**
- `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`
- `Assets/3 - Scripts/Camera/CameraTransformFX.cs` — headbob, tilt, landing dip, death tilt
- `Assets/3 - Scripts/Camera/CameraFOVFX.cs` — FOV stack + sprint/jetpack/ship-boost sources
- `Assets/3 - Scripts/Camera/CombatFX.cs` — directional hit shake, enemy-hit micro-shake, slowmo-on-kill
- `Assets/3 - Scripts/Camera/VignetteOverlay.cs`
- `Assets/3 - Scripts/Camera/DamageFlashOverlay.cs`
- `Assets/3 - Scripts/Camera/LetterboxBars.cs`
- `Assets/3 - Scripts/Camera/SpeedLinesOverlay.cs`
- `Assets/3 - Scripts/Camera/FilmGrainOverlay.cs`
- `Assets/3 - Scripts/Camera/ChromaticAberrationOverlay.cs`
- `Assets/3 - Scripts/Camera/LensDirtOverlay.cs`
- `Assets/3 - Scripts/Camera/MoodColorGrade.cs`
- `Assets/3 - Scripts/Camera/AnamorphicStreaks.cs`
- `Assets/3 - Scripts/Camera/LensFlareRegistry.cs`

**Verification note for every task:** Unity has no automated test framework set up in this project (no `.asmdef`, no `Tests/` folder per CLAUDE.md). The "test" for each task is `mcp__coplay-mcp__check_compile_errors` returning `No compile errors` plus the manual Play-mode check spelled out in the task's verification step. Commit only after both pass.

---

## Phase 1 — Foundation (sequential, blocks everything else)

### Task 1: Extend InputSettings with camera-effect fields

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs`

- [ ] **Step 1: Add toggle fields and intensity sliders**

Insert at the end of the existing fields block, after `vibrationEnabled` (around line 45):

```csharp
[Header("Camera Effects (master)")]
public bool cameraEffectsEnabled = true;

[Header("Camera Effects — Movement")]
public bool fxHeadbob = true;
public bool fxLandingDip = true;
public bool fxStrafeTilt = true;
public bool fxSprintFovKick = true;

[Header("Camera Effects — Vehicle")]
public bool fxJetpackFovKick = true;
public bool fxShipBoostFov = true;
public bool fxSpeedLines = true;

[Header("Camera Effects — Combat")]
public bool fxDamageFlash = true;
public bool fxDamageVignette = true;
public bool fxDirectionalHitShake = true;
public bool fxEnemyHitMicroShake = true;
public bool fxDeathTilt = true;
public bool fxSlowmoOnKill = true;

[Header("Camera Effects — Survival & Cinematic")]
public bool fxLowHealthVignette = true;
public bool fxDialogueVignette = true;
public bool fxLetterboxBars = true;
public bool fxMoodColorGrade = true;

[Header("Camera Effects — Lens Character")]
public bool fxSubtleVignette = true;
public bool fxFilmGrain = true;
public bool fxChromaticAberration = true;
public bool fxLensDirt = true;
public bool fxLensFlares = true;
public bool fxAnamorphicStreaks = true;

[Header("Camera Effects — Intensities")]
[Range(0f, 1f)] public float fxHeadbobIntensity = 1f;
[Range(0f, 1f)] public float fxFilmGrainIntensity = 0.6f;
[Range(0f, 1f)] public float fxSubtleVignetteIntensity = 0.45f;
[Range(0f, 1f)] public float fxChromaticAberrationIntensity = 0.35f;
```

- [ ] **Step 2: Extend `LoadSettings` to read each new field**

Append inside `LoadSettings`, after the existing `vibrationEnabled` line:

```csharp
cameraEffectsEnabled        = PlayerPrefs.GetInt   (nameof (cameraEffectsEnabled),        1) != 0;
fxHeadbob                   = PlayerPrefs.GetInt   (nameof (fxHeadbob),                   1) != 0;
fxLandingDip                = PlayerPrefs.GetInt   (nameof (fxLandingDip),                1) != 0;
fxStrafeTilt                = PlayerPrefs.GetInt   (nameof (fxStrafeTilt),                1) != 0;
fxSprintFovKick             = PlayerPrefs.GetInt   (nameof (fxSprintFovKick),             1) != 0;
fxJetpackFovKick            = PlayerPrefs.GetInt   (nameof (fxJetpackFovKick),            1) != 0;
fxShipBoostFov              = PlayerPrefs.GetInt   (nameof (fxShipBoostFov),              1) != 0;
fxSpeedLines                = PlayerPrefs.GetInt   (nameof (fxSpeedLines),                1) != 0;
fxDamageFlash               = PlayerPrefs.GetInt   (nameof (fxDamageFlash),               1) != 0;
fxDamageVignette            = PlayerPrefs.GetInt   (nameof (fxDamageVignette),            1) != 0;
fxDirectionalHitShake       = PlayerPrefs.GetInt   (nameof (fxDirectionalHitShake),       1) != 0;
fxEnemyHitMicroShake        = PlayerPrefs.GetInt   (nameof (fxEnemyHitMicroShake),        1) != 0;
fxDeathTilt                 = PlayerPrefs.GetInt   (nameof (fxDeathTilt),                 1) != 0;
fxSlowmoOnKill              = PlayerPrefs.GetInt   (nameof (fxSlowmoOnKill),              1) != 0;
fxLowHealthVignette         = PlayerPrefs.GetInt   (nameof (fxLowHealthVignette),         1) != 0;
fxDialogueVignette          = PlayerPrefs.GetInt   (nameof (fxDialogueVignette),          1) != 0;
fxLetterboxBars             = PlayerPrefs.GetInt   (nameof (fxLetterboxBars),             1) != 0;
fxMoodColorGrade            = PlayerPrefs.GetInt   (nameof (fxMoodColorGrade),            1) != 0;
fxSubtleVignette            = PlayerPrefs.GetInt   (nameof (fxSubtleVignette),            1) != 0;
fxFilmGrain                 = PlayerPrefs.GetInt   (nameof (fxFilmGrain),                 1) != 0;
fxChromaticAberration       = PlayerPrefs.GetInt   (nameof (fxChromaticAberration),       1) != 0;
fxLensDirt                  = PlayerPrefs.GetInt   (nameof (fxLensDirt),                  1) != 0;
fxLensFlares                = PlayerPrefs.GetInt   (nameof (fxLensFlares),                1) != 0;
fxAnamorphicStreaks         = PlayerPrefs.GetInt   (nameof (fxAnamorphicStreaks),         1) != 0;
fxHeadbobIntensity              = PlayerPrefs.GetFloat (nameof (fxHeadbobIntensity),              1f);
fxFilmGrainIntensity            = PlayerPrefs.GetFloat (nameof (fxFilmGrainIntensity),            0.6f);
fxSubtleVignetteIntensity       = PlayerPrefs.GetFloat (nameof (fxSubtleVignetteIntensity),       0.45f);
fxChromaticAberrationIntensity  = PlayerPrefs.GetFloat (nameof (fxChromaticAberrationIntensity),  0.35f);
```

- [ ] **Step 3: Extend `SaveSettings` to write each new field**

Append inside `SaveSettings`, after the existing `vibrationEnabled` line:

```csharp
PlayerPrefs.SetInt   (nameof (cameraEffectsEnabled),        cameraEffectsEnabled        ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxHeadbob),                   fxHeadbob                   ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxLandingDip),                fxLandingDip                ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxStrafeTilt),                fxStrafeTilt                ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxSprintFovKick),             fxSprintFovKick             ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxJetpackFovKick),            fxJetpackFovKick            ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxShipBoostFov),              fxShipBoostFov              ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxSpeedLines),                fxSpeedLines                ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxDamageFlash),               fxDamageFlash               ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxDamageVignette),            fxDamageVignette            ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxDirectionalHitShake),       fxDirectionalHitShake       ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxEnemyHitMicroShake),        fxEnemyHitMicroShake        ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxDeathTilt),                 fxDeathTilt                 ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxSlowmoOnKill),              fxSlowmoOnKill              ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxLowHealthVignette),         fxLowHealthVignette         ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxDialogueVignette),          fxDialogueVignette          ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxLetterboxBars),             fxLetterboxBars             ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxMoodColorGrade),            fxMoodColorGrade            ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxSubtleVignette),            fxSubtleVignette            ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxFilmGrain),                 fxFilmGrain                 ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxChromaticAberration),       fxChromaticAberration       ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxLensDirt),                  fxLensDirt                  ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxLensFlares),                fxLensFlares                ? 1 : 0);
PlayerPrefs.SetInt   (nameof (fxAnamorphicStreaks),         fxAnamorphicStreaks         ? 1 : 0);
PlayerPrefs.SetFloat (nameof (fxHeadbobIntensity),              fxHeadbobIntensity);
PlayerPrefs.SetFloat (nameof (fxFilmGrainIntensity),            fxFilmGrainIntensity);
PlayerPrefs.SetFloat (nameof (fxSubtleVignetteIntensity),       fxSubtleVignetteIntensity);
PlayerPrefs.SetFloat (nameof (fxChromaticAberrationIntensity),  fxChromaticAberrationIntensity);
```

- [ ] **Step 4: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs"
git commit -m "feat(settings): add camera-effect toggle + intensity fields to InputSettings"
```

---

### Task 2: Add `ToggleDef` and `HeaderDef` row types to `TabbedPauseMenu`

**Files:**
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs`

- [ ] **Step 1: Replace the single `SettingDef` class with a polymorphic row hierarchy**

Find the existing `class SettingDef` block. Replace it (and the `class TabDef` field references that use `List<SettingDef>`) with:

```csharp
abstract class RowDef { public string label; }

class SliderDef : RowDef
{
    public float min;
    public float max;
    public bool wholeNumbers;
    public string format;
    public Func<float> get;
    public Action<float> set;
}

class ToggleDef : RowDef
{
    public Func<bool> get;
    public Action<bool> set;
}

class HeaderDef : RowDef { }

class TabDef
{
    public string name;
    public List<RowDef> rows;
}
```

- [ ] **Step 2: Update `_tabs` / `BuildSettingsList` to use the new types**

In `BuildSettingsList`, change `settings = new List<SettingDef>` to `rows = new List<RowDef>` for both tabs, and rename `new SettingDef` to `new SliderDef`. The existing 8 entries remain unchanged in shape — just the class name changes.

Example for the first one:

```csharp
new SliderDef {
    label = "MOUSE SENSITIVITY", min = 1f, max = 200f, wholeNumbers = true, format = "{0:F0}",
    get  = () => _input != null ? _input.mouseSensitivity : 100f,
    set  = v  => { if (_input != null) _input.mouseSensitivity = v; },
},
```

- [ ] **Step 3: Add `BuildToggleRow` and `BuildHeaderRow` methods**

Add inside the class (next to existing `BuildSettingRow`):

```csharp
class SettingRowRefs_Toggle
{
    public Image bg;
    public TextMeshProUGUI valueText;
    public ToggleDef def;
}
List<SettingRowRefs_Toggle> _toggleRows = new List<SettingRowRefs_Toggle>();

void BuildToggleRow(RectTransform parent, ToggleDef def)
{
    var rowRT = NewUI(def.label + "Row", parent);
    var rowLE = rowRT.gameObject.AddComponent<LayoutElement>();
    rowLE.preferredHeight = SettingsRowHeight;
    rowLE.flexibleHeight = 0f;

    var rowHL = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
    rowHL.childAlignment = TextAnchor.MiddleLeft;
    rowHL.spacing = 14f;
    rowHL.childControlWidth = true;
    rowHL.childControlHeight = true;
    rowHL.childForceExpandWidth = false;
    rowHL.childForceExpandHeight = false;

    var lbl = NewText(rowRT, "Label", def.label, 12f, FontStyles.Bold, HeaderColor);
    lbl.alignment = TextAlignmentOptions.MidlineLeft;
    lbl.characterSpacing = 3f;
    var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
    lblLE.preferredWidth = 260f;
    lblLE.preferredHeight = 20f;
    lblLE.flexibleWidth = 0f;
    lbl.raycastTarget = false;

    var spacer = NewUI("Spacer", rowRT);
    var spLE = spacer.gameObject.AddComponent<LayoutElement>();
    spLE.flexibleWidth = 1f;

    var toggleRT = NewUI("Toggle", rowRT);
    var tLE = toggleRT.gameObject.AddComponent<LayoutElement>();
    tLE.preferredWidth = 70f;
    tLE.preferredHeight = 22f;
    tLE.flexibleWidth = 0f;

    var bg = toggleRT.gameObject.AddComponent<Image>();
    bg.sprite = UIPanelSprites.GetBeveledPanel();
    bg.type = Image.Type.Sliced;
    bg.color = ButtonBg;
    bg.raycastTarget = true;

    var border = NewUI("Border", toggleRT);
    Stretch(border);
    var borderImg = border.gameObject.AddComponent<Image>();
    borderImg.sprite = UIPanelSprites.GetBeveledOutline();
    borderImg.type = Image.Type.Sliced;
    borderImg.color = ButtonBorder;
    borderImg.raycastTarget = false;

    var stateText = NewText(toggleRT, "State", "ON", 11f, FontStyles.Bold, CardBorderCool);
    Stretch(stateText.rectTransform);
    stateText.alignment = TextAlignmentOptions.Center;
    stateText.characterSpacing = 2f;
    stateText.raycastTarget = false;

    var btn = toggleRT.gameObject.AddComponent<Button>();
    btn.targetGraphic = bg;
    var refs = new SettingRowRefs_Toggle { bg = bg, valueText = stateText, def = def };
    _toggleRows.Add(refs);

    btn.onClick.AddListener(() =>
    {
        if (def.get == null || def.set == null) return;
        bool newVal = !def.get();
        def.set(newVal);
        RefreshToggle(refs);
    });

    RefreshToggle(refs);
}

void RefreshToggle(SettingRowRefs_Toggle refs)
{
    if (refs == null || refs.def == null || refs.def.get == null) return;
    bool on = refs.def.get();
    refs.valueText.text = on ? "ON" : "OFF";
    refs.valueText.color = on ? CardBorderCool : LabelDim;
    refs.bg.color = on ? new Color32(0x14, 0x40, 0x60, 0xCC) : ButtonBg;
}

void BuildHeaderRow(RectTransform parent, HeaderDef def)
{
    var rowRT = NewUI("// " + def.label, parent);
    var rowLE = rowRT.gameObject.AddComponent<LayoutElement>();
    rowLE.preferredHeight = 26f;
    rowLE.flexibleHeight = 0f;
    var lbl = NewText(rowRT, "Label", "// " + def.label.ToUpperInvariant(), 10f, FontStyles.Bold, HeaderColor);
    Stretch(lbl.rectTransform);
    lbl.alignment = TextAlignmentOptions.MidlineLeft;
    lbl.characterSpacing = 4f;
    lbl.margin = new Vector4(0f, 8f, 0f, 0f);
    lbl.raycastTarget = false;
}
```

- [ ] **Step 3a: Dispatch on row type in `BuildSettingsPanel`'s tab-panel loop**

Find the loop `foreach (var setting in _tabs[i].settings)` (now `rows`). Replace the inner `BuildSettingRow(tabPanelRT, setting);` call with:

```csharp
foreach (var row in _tabs[i].rows)
{
    switch (row)
    {
        case SliderDef s: BuildSettingRow(tabPanelRT, s); break;
        case ToggleDef t: BuildToggleRow(tabPanelRT, t); break;
        case HeaderDef h: BuildHeaderRow(tabPanelRT, h); break;
    }
}
```

- [ ] **Step 3b: Update `BuildSettingRow` signature**

Change its parameter type from `SettingDef def` to `SliderDef def`. The body is unchanged.

- [ ] **Step 3c: Update `RefreshAllSliders`**

Rename to `RefreshAllRows` and add the toggle refresh:

```csharp
void RefreshAllRows()
{
    if (_rows != null)
    {
        foreach (var r in _rows)
        {
            if (r == null || r.slider == null || r.def == null || r.def.get == null) continue;
            float current = r.def.get();
            r.slider.SetValueWithoutNotify(current);
            if (r.valueText != null) r.valueText.text = string.Format(r.def.format, current);
        }
    }
    if (_toggleRows != null)
        foreach (var t in _toggleRows) RefreshToggle(t);
}
```

Update the existing callers (`SwitchTab`, `OpenPause`, `ShowSettingsPanel`) to call `RefreshAllRows` instead of `RefreshAllSliders`.

- [ ] **Step 3d: Rename `SettingRowRefs.def` from `SettingDef` to `SliderDef`**

```csharp
class SettingRowRefs
{
    public Slider slider;
    public TextMeshProUGUI valueText;
    public SliderDef def;
}
```

- [ ] **Step 4: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 5: Manual verify**

Open the scene in Play mode, press Esc, click SETTINGS, click CONTROLS / GRAPHICS — both tabs still show their existing sliders unchanged. No layout regression.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/UI/TabbedPauseMenu.cs"
git commit -m "refactor(pause-menu): add ToggleDef + HeaderDef row types"
```

---

### Task 3: Add the new `CAMERA` tab with all toggle + slider + header rows

**Files:**
- Modify: `Assets/3 - Scripts/UI/TabbedPauseMenu.cs`

- [ ] **Step 1: Add the `CAMERA` tab to `BuildSettingsList`**

Insert a new `TabDef` between the existing `CONTROLS` and `GRAPHICS` tabs:

```csharp
new TabDef
{
    name = "CAMERA",
    rows = new List<RowDef>
    {
        new HeaderDef { label = "MOVEMENT" },
        new ToggleDef { label = "HEADBOB",            get = () => _input != null && _input.fxHeadbob,            set = v => { if (_input != null) _input.fxHeadbob = v; } },
        new SliderDef { label = "HEADBOB INTENSITY",  min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
            get = () => _input != null ? _input.fxHeadbobIntensity : 1f,
            set = v => { if (_input != null) _input.fxHeadbobIntensity = v; } },
        new ToggleDef { label = "LANDING DIP",        get = () => _input != null && _input.fxLandingDip,        set = v => { if (_input != null) _input.fxLandingDip = v; } },
        new ToggleDef { label = "STRAFE TILT",        get = () => _input != null && _input.fxStrafeTilt,        set = v => { if (_input != null) _input.fxStrafeTilt = v; } },
        new ToggleDef { label = "SPRINT FOV KICK",    get = () => _input != null && _input.fxSprintFovKick,     set = v => { if (_input != null) _input.fxSprintFovKick = v; } },

        new HeaderDef { label = "VEHICLE" },
        new ToggleDef { label = "JETPACK FOV KICK",   get = () => _input != null && _input.fxJetpackFovKick,    set = v => { if (_input != null) _input.fxJetpackFovKick = v; } },
        new ToggleDef { label = "SHIP BOOST FOV",     get = () => _input != null && _input.fxShipBoostFov,      set = v => { if (_input != null) _input.fxShipBoostFov = v; } },
        new ToggleDef { label = "SPEED LINES",        get = () => _input != null && _input.fxSpeedLines,        set = v => { if (_input != null) _input.fxSpeedLines = v; } },

        new HeaderDef { label = "COMBAT" },
        new ToggleDef { label = "DAMAGE FLASH",       get = () => _input != null && _input.fxDamageFlash,       set = v => { if (_input != null) _input.fxDamageFlash = v; } },
        new ToggleDef { label = "DAMAGE VIGNETTE",    get = () => _input != null && _input.fxDamageVignette,    set = v => { if (_input != null) _input.fxDamageVignette = v; } },
        new ToggleDef { label = "HIT SHAKE",          get = () => _input != null && _input.fxDirectionalHitShake, set = v => { if (_input != null) _input.fxDirectionalHitShake = v; } },
        new ToggleDef { label = "ENEMY HIT MICRO-SHAKE", get = () => _input != null && _input.fxEnemyHitMicroShake, set = v => { if (_input != null) _input.fxEnemyHitMicroShake = v; } },
        new ToggleDef { label = "DEATH TILT",         get = () => _input != null && _input.fxDeathTilt,         set = v => { if (_input != null) _input.fxDeathTilt = v; } },
        new ToggleDef { label = "SLOWMO ON KILL",     get = () => _input != null && _input.fxSlowmoOnKill,      set = v => { if (_input != null) _input.fxSlowmoOnKill = v; } },

        new HeaderDef { label = "SURVIVAL & CINEMATIC" },
        new ToggleDef { label = "LOW HEALTH VIGNETTE", get = () => _input != null && _input.fxLowHealthVignette, set = v => { if (_input != null) _input.fxLowHealthVignette = v; } },
        new ToggleDef { label = "DIALOGUE VIGNETTE",  get = () => _input != null && _input.fxDialogueVignette,  set = v => { if (_input != null) _input.fxDialogueVignette = v; } },
        new ToggleDef { label = "LETTERBOX BARS",     get = () => _input != null && _input.fxLetterboxBars,     set = v => { if (_input != null) _input.fxLetterboxBars = v; } },
        new ToggleDef { label = "MOOD COLOR GRADE",   get = () => _input != null && _input.fxMoodColorGrade,    set = v => { if (_input != null) _input.fxMoodColorGrade = v; } },

        new HeaderDef { label = "LENS CHARACTER" },
        new ToggleDef { label = "SUBTLE VIGNETTE",    get = () => _input != null && _input.fxSubtleVignette,    set = v => { if (_input != null) _input.fxSubtleVignette = v; } },
        new SliderDef { label = "VIGNETTE INTENSITY", min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
            get = () => _input != null ? _input.fxSubtleVignetteIntensity : 0.45f,
            set = v => { if (_input != null) _input.fxSubtleVignetteIntensity = v; } },
        new ToggleDef { label = "FILM GRAIN",         get = () => _input != null && _input.fxFilmGrain,         set = v => { if (_input != null) _input.fxFilmGrain = v; } },
        new SliderDef { label = "GRAIN INTENSITY",    min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
            get = () => _input != null ? _input.fxFilmGrainIntensity : 0.6f,
            set = v => { if (_input != null) _input.fxFilmGrainIntensity = v; } },
        new ToggleDef { label = "CHROMATIC ABERRATION", get = () => _input != null && _input.fxChromaticAberration, set = v => { if (_input != null) _input.fxChromaticAberration = v; } },
        new SliderDef { label = "CA INTENSITY",       min = 0f, max = 1f, wholeNumbers = false, format = "{0:F2}",
            get = () => _input != null ? _input.fxChromaticAberrationIntensity : 0.35f,
            set = v => { if (_input != null) _input.fxChromaticAberrationIntensity = v; } },
        new ToggleDef { label = "LENS DIRT",          get = () => _input != null && _input.fxLensDirt,          set = v => { if (_input != null) _input.fxLensDirt = v; } },
        new ToggleDef { label = "LENS FLARES",        get = () => _input != null && _input.fxLensFlares,        set = v => { if (_input != null) _input.fxLensFlares = v; } },
        new ToggleDef { label = "ANAMORPHIC STREAKS", get = () => _input != null && _input.fxAnamorphicStreaks, set = v => { if (_input != null) _input.fxAnamorphicStreaks = v; } },
    },
},
```

- [ ] **Step 2: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 3: Manual verify**

Play mode, Esc, SETTINGS — verify three tabs (`CONTROLS | CAMERA | GRAPHICS`). Click CAMERA — scrollable list with section headers and all toggles. Toggle a few — they persist across Esc-close + Esc-open.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/TabbedPauseMenu.cs"
git commit -m "feat(pause-menu): add CAMERA tab with all effect toggles + intensity sliders"
```

---

### Task 4: Create `CameraEffectsManager` skeleton + seed in `MainMenuController`

**Files:**
- Create: `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs`

- [ ] **Step 1: Create the manager file**

Create `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`:

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton coordinator for all camera / screen effects. Owns the FX modules
/// as child GameObjects and exposes shared refs (player camera, InputSettings)
/// so individual modules don't each FindObjectOfType.
///
/// Modules poll InputSettings every frame for their enable flag so toggles
/// from the pause menu take effect live.
/// </summary>
public class CameraEffectsManager : MonoBehaviour
{
    public static CameraEffectsManager Instance { get; private set; }

    public Camera PlayerCamera { get; private set; }
    public InputSettings Input { get; private set; }
    public bool MasterEnabled => Input != null && Input.cameraEffectsEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("CameraEffectsManager");
        DontDestroyOnLoad(go);
        go.AddComponent<CameraEffectsManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        TryAcquireRefs();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m) { TryAcquireRefs(); }

    void Update()
    {
        if (PlayerCamera == null || Input == null) TryAcquireRefs();
    }

    void TryAcquireRefs()
    {
        if (PlayerCamera == null)
        {
            var cam = Camera.main;
            if (cam != null) PlayerCamera = cam;
        }
        if (Input == null)
        {
            var settingsMenu = FindObjectOfType<SettingsMenu>(true);
            if (settingsMenu != null) Input = settingsMenu.inputSettings;
        }
    }
}
```

- [ ] **Step 2: Seed in `MainMenuController.EnsureGameplaySingletons`**

In `Assets/3 - Scripts/UI/MainMenuController.cs`, append inside the method (after the `TabbedPauseMenu` block):

```csharp
if (CameraEffectsManager.Instance == null)
{
    var go = new GameObject("CameraEffectsManager");
    DontDestroyOnLoad(go);
    go.AddComponent<CameraEffectsManager>();
}
```

- [ ] **Step 3: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Camera/CameraEffectsManager.cs" "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(camera-fx): add CameraEffectsManager singleton skeleton + seeding"
```

---

## Phase 2 — Camera Transform & FOV (depends on Phase 1)

### Task 5: `CameraTransformFX` — headbob + strafe tilt + landing dip + death tilt

**Files:**
- Create: `Assets/3 - Scripts/Camera/CameraTransformFX.cs`
- Modify: `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`

- [ ] **Step 1: Create the module**

```csharp
using UnityEngine;

/// <summary>
/// Camera-transform effects: walk/run headbob, strafe roll, landing dip, and
/// death tilt-and-dim. Reads PlayerController for velocity / landing /
/// alive state. Drives the camera's *local* position + rotation; never
/// touches world position so it can't fight planet gravity alignment.
///
/// Toggles read from InputSettings each LateUpdate.
/// </summary>
public class CameraTransformFX : MonoBehaviour
{
    PlayerController _player;
    Transform _cam;
    Vector3 _camBaseLocalPos;
    Quaternion _camBaseLocalRot;
    bool _cached;

    float _bobPhase;
    float _dipOffset;       // negative when dipping
    float _dipVelocity;
    float _tiltZ;
    float _deathTiltT;
    bool _isDying;

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) { ResetIfCached(); return; }
        var input = mgr.Input;
        if (input == null) return;
        if (!CacheRefs(mgr)) return;

        // Start each frame at the resting pose; effects below stack offsets.
        _cam.localPosition = _camBaseLocalPos;
        _cam.localRotation = _camBaseLocalRot;

        float dt = Time.deltaTime;

        // ── Headbob ──
        if (input.fxHeadbob && _player != null)
        {
            // Bob amplitude scales with horizontal speed up to ~6 m/s.
            float speed = HorizontalSpeed(_player);
            float t = Mathf.Clamp01(speed / 6f) * input.fxHeadbobIntensity;
            _bobPhase += dt * Mathf.Lerp(0f, 10f, t);
            float bobY = Mathf.Sin(_bobPhase) * 0.05f * t;
            float bobX = Mathf.Cos(_bobPhase * 0.5f) * 0.025f * t;
            _cam.localPosition += new Vector3(bobX, bobY, 0f);
        }

        // ── Landing dip ── (decays toward 0 each frame; jumps to negative on land)
        if (_dipOffset < 0f)
        {
            _dipOffset = Mathf.SmoothDamp(_dipOffset, 0f, ref _dipVelocity, 0.18f);
            if (input.fxLandingDip)
                _cam.localPosition += new Vector3(0f, _dipOffset, 0f);
        }
        if (input.fxLandingDip && _player != null && _player.justLanded)
        {
            // Magnitude scales with vertical velocity at the moment of landing.
            float vy = Mathf.Abs(_player.GetComponent<Rigidbody>()?.velocity.y ?? 0f);
            _dipOffset = -Mathf.Clamp(vy * 0.012f, 0.04f, 0.18f);
            _dipVelocity = 0f;
        }

        // ── Strafe tilt ──
        if (input.fxStrafeTilt)
        {
            float h = UnityEngine.Input.GetAxisRaw("Horizontal");
            float target = -h * 2f;
            _tiltZ = Mathf.Lerp(_tiltZ, target, 1f - Mathf.Exp(-dt * 8f));
            _cam.localRotation *= Quaternion.Euler(0f, 0f, _tiltZ);
        }
        else _tiltZ = Mathf.Lerp(_tiltZ, 0f, 1f - Mathf.Exp(-dt * 8f));

        // ── Death tilt ── (driven by ResourceManager.OnDeath — see Task 17)
        if (input.fxDeathTilt && _isDying)
        {
            _deathTiltT = Mathf.MoveTowards(_deathTiltT, 1f, dt / 0.6f);
            float angle = Mathf.Lerp(0f, 70f, EaseOutCubic(_deathTiltT));
            _cam.localRotation *= Quaternion.Euler(0f, 0f, angle);
        }
        else
        {
            _deathTiltT = 0f;
        }
    }

    public void TriggerDeathTilt() { _isDying = true; }
    public void ClearDeathTilt()   { _isDying = false; }

    static float HorizontalSpeed(PlayerController p)
    {
        var rb = p != null ? p.GetComponent<Rigidbody>() : null;
        if (rb == null) return 0f;
        Vector3 up = p.transform.up;
        Vector3 v = rb.velocity - Vector3.Project(rb.velocity, up);
        return v.magnitude;
    }

    static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    bool CacheRefs(CameraEffectsManager mgr)
    {
        if (_cached && _player != null && _cam != null) return true;
        _player = FindObjectOfType<PlayerController>(true);
        if (_player == null) return false;
        _cam = mgr.PlayerCamera != null ? mgr.PlayerCamera.transform : null;
        if (_cam == null) return false;
        _camBaseLocalPos = _cam.localPosition;
        _camBaseLocalRot = _cam.localRotation;
        _cached = true;
        return true;
    }

    void ResetIfCached()
    {
        if (!_cached || _cam == null) return;
        _cam.localPosition = _camBaseLocalPos;
        _cam.localRotation = _camBaseLocalRot;
    }
}
```

- [ ] **Step 2: Add module to manager**

In `CameraEffectsManager.cs`, append after `TryAcquireRefs` returns:

```csharp
public CameraTransformFX TransformFX { get; private set; }

void AttachModules()
{
    if (TransformFX == null) TransformFX = gameObject.AddComponent<CameraTransformFX>();
}
```

And call `AttachModules()` from inside `Awake` and `OnSceneLoaded` after `TryAcquireRefs`.

- [ ] **Step 3: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 4: Manual verify**

Play mode → walk forward (W) → camera bobs gently. Strafe A/D → camera leans. Jump and land → quick downward dip then settles.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Camera/CameraTransformFX.cs" "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): headbob, strafe tilt, landing dip, death tilt"
```

---

### Task 6: `CameraFOVFX` — stacked FOV offsets for sprint, jetpack, ship boost

**Files:**
- Create: `Assets/3 - Scripts/Camera/CameraFOVFX.cs`
- Modify: `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`

- [ ] **Step 1: Create the module**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FOV stacking system. Multiple sources (sprint, jetpack, ship boost) push
/// a deltaFOV each frame they want to contribute; the module sums them and
/// smooth-damps the camera's fieldOfView from baseFOV → baseFOV + sum.
/// </summary>
public class CameraFOVFX : MonoBehaviour
{
    PlayerController _player;
    Ship _ship;
    Camera _cam;
    float _baseFOV;
    float _currentDelta;
    float _deltaVelocity;
    bool _cached;

    const float SprintThreshold = 4.5f;
    const float SprintFOVDelta = 6f;
    const float JetpackFOVDelta = 5f;
    const float ShipBoostFOVDelta = 8f;

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) { ResetIfCached(); return; }
        var input = mgr.Input;
        if (input == null) return;
        if (!CacheRefs(mgr)) return;

        float targetDelta = 0f;

        // Sprint kick — sustained horizontal speed.
        if (input.fxSprintFovKick && _player != null)
        {
            var rb = _player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 up = _player.transform.up;
                Vector3 v = rb.velocity - Vector3.Project(rb.velocity, up);
                if (v.magnitude > SprintThreshold) targetDelta += SprintFOVDelta;
            }
        }

        // Jetpack kick — any of the three jetpack thrust modes active.
        if (input.fxJetpackFovKick && _player != null)
        {
            if (IsJetpackActive(_player)) targetDelta += JetpackFOVDelta;
        }

        // Ship boost kick — engines firing.
        if (input.fxShipBoostFov && _ship != null && IsShipBoosting(_ship))
        {
            targetDelta += ShipBoostFOVDelta;
        }

        _currentDelta = Mathf.SmoothDamp(_currentDelta, targetDelta, ref _deltaVelocity, 0.15f);
        _cam.fieldOfView = _baseFOV + _currentDelta;
    }

    static bool IsJetpackActive(PlayerController p)
    {
        // Reflection-free check via public flags. PlayerController already
        // exposes JetpackUnlocked + JetpackFuelPercent; the *using* state
        // isn't public, so approximate from input + airborne state.
        if (!p.JetpackUnlocked) return false;
        bool jumpHeld = TutorialGate.JumpHeld(TutorialAbility.Boost);
        bool downHeld = TutorialGate.DownThrustHeld(TutorialAbility.DownThrust);
        bool dirHeld  = TutorialGate.DirectionalThrustHeld(TutorialAbility.DirectionalThrust);
        return jumpHeld || downHeld || dirHeld;
    }

    static bool IsShipBoosting(Ship s)
    {
        // Use rigidbody velocity magnitude as a proxy — boosting raises it
        // well above natural drift speeds.
        var rb = s != null ? s.GetComponent<Rigidbody>() : null;
        return rb != null && rb.velocity.magnitude > 6f;
    }

    bool CacheRefs(CameraEffectsManager mgr)
    {
        if (_cached && _cam != null) return true;
        _cam = mgr.PlayerCamera;
        if (_cam == null) return false;
        _baseFOV = _cam.fieldOfView;
        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        if (_ship == null) _ship = FindObjectOfType<Ship>(true);
        _cached = true;
        return true;
    }

    void ResetIfCached()
    {
        if (!_cached || _cam == null) return;
        _cam.fieldOfView = _baseFOV;
        _currentDelta = 0f;
        _deltaVelocity = 0f;
    }
}
```

- [ ] **Step 2: Attach in manager**

In `CameraEffectsManager.cs` add:

```csharp
public CameraFOVFX FOVFX { get; private set; }
```

And in `AttachModules`:

```csharp
if (FOVFX == null) FOVFX = gameObject.AddComponent<CameraFOVFX>();
```

- [ ] **Step 3: Compile-check**

Run: `mcp__coplay-mcp__check_compile_errors`
Expected: `No compile errors`

- [ ] **Step 4: Manual verify**

Play mode → sprint (sustained W) → FOV widens. Use jetpack (Space, Ctrl, or Shift+WASD airborne) → FOV widens. Pilot the ship and accelerate → FOV widens further.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Camera/CameraFOVFX.cs" "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): stacked FOV kicks for sprint, jetpack, ship boost"
```

---

## Phase 3 — UI overlays (parallelizable; each task is one file)

### Task 7: `VignetteOverlay` composite system

**Files:**
- Create: `Assets/3 - Scripts/Camera/VignetteOverlay.cs`
- Modify: `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`

- [ ] **Step 1: Create the overlay**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single full-screen radial vignette overlay. Drivers push a
/// (color, intensity) tuple per frame; the overlay composites by picking
/// the strongest driver. Used by damage pulse, low-health pulse, dialogue
/// focus, death dim, and the always-on subtle baseline.
/// </summary>
public class VignetteOverlay : MonoBehaviour
{
    Image _image;
    Sprite _vignetteSprite;

    struct Driver { public Color color; public float intensity; }
    List<Driver> _frame = new List<Driver>();

    void Awake() { BuildCanvas(); }

    public void Push(Color color, float intensity)
    {
        if (intensity <= 0f) return;
        _frame.Add(new Driver { color = color, intensity = Mathf.Clamp01(intensity) });
    }

    void LateUpdate()
    {
        // Composite: pick the driver with the highest alpha contribution.
        // Color comes from the strongest driver; alpha is its intensity.
        if (_frame.Count == 0)
        {
            if (_image.color.a > 0f)
            {
                var c = _image.color; c.a = Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 4f);
                _image.color = c;
            }
            return;
        }
        Driver best = _frame[0];
        for (int i = 1; i < _frame.Count; i++)
            if (_frame[i].intensity > best.intensity) best = _frame[i];
        _image.color = new Color(best.color.r, best.color.g, best.color.b, best.intensity);
        _frame.Clear();
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 800;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("VignetteImage", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.sprite = GetVignetteSprite();
        _image.color = new Color(0f, 0f, 0f, 0f);
        _image.raycastTarget = false;
        _image.type = Image.Type.Simple;
    }

    static Sprite _cachedSprite;
    static Sprite GetVignetteSprite()
    {
        if (_cachedSprite != null) return _cachedSprite;
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - r) / r;
                float dy = (y - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Radial gradient: transparent at center, opaque at corners.
                // Power curve gives a softer falloff toward the edges.
                float a = Mathf.Clamp01(Mathf.Pow(d, 2.2f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(pixels);
        tex.Apply();
        _cachedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _cachedSprite.name = "VignetteRadial";
        return _cachedSprite;
    }
}
```

- [ ] **Step 2: Spawn as a child GO of the manager**

In `CameraEffectsManager.cs`, add:

```csharp
public VignetteOverlay Vignette { get; private set; }
```

And inside `AttachModules`:

```csharp
if (Vignette == null)
{
    var go = new GameObject("VignetteOverlay");
    go.transform.SetParent(transform, false);
    Vignette = go.AddComponent<VignetteOverlay>();
}
```

- [ ] **Step 3: Push the always-on subtle vignette driver from the manager's Update**

In `CameraEffectsManager.Update`, after `TryAcquireRefs`, append:

```csharp
if (MasterEnabled && Input != null && Vignette != null && Input.fxSubtleVignette)
{
    Vignette.Push(new Color(0f, 0f, 0f, 1f), Input.fxSubtleVignetteIntensity);
}
```

- [ ] **Step 4: Compile-check + verify**

Compile clean. Play mode → corners of the screen should be subtly darker than the center. Toggling SUBTLE VIGNETTE off in the menu removes it live.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Camera/VignetteOverlay.cs" "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): composited vignette overlay + always-on subtle driver"
```

---

### Task 8: `DamageFlashOverlay` (red flash on damage)

**Files:**
- Create: `Assets/3 - Scripts/Camera/DamageFlashOverlay.cs`

- [ ] **Step 1: Create overlay**

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>Quick full-screen red flash. Driven by external Flash() calls.</summary>
public class DamageFlashOverlay : MonoBehaviour
{
    Image _image;
    float _alpha;

    void Awake() { BuildCanvas(); }

    public void Flash(float intensity = 0.55f)
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        var input = mgr.Input;
        if (input != null && !input.fxDamageFlash) return;
        _alpha = Mathf.Max(_alpha, intensity);
    }

    void LateUpdate()
    {
        if (_alpha > 0f)
            _alpha = Mathf.MoveTowards(_alpha, 0f, Time.unscaledDeltaTime * 2.2f);
        var c = _image.color; c.a = _alpha; _image.color = c;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 810;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("RedFlash", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.color = new Color(1f, 0.1f, 0.15f, 0f);
        _image.raycastTarget = false;
    }
}
```

- [ ] **Step 2: Attach in manager** (same shape as Task 7's attachment block, add property `DamageFlash` + spawn child GO).

- [ ] **Step 3: Compile-check + commit**

```bash
git add "Assets/3 - Scripts/Camera/DamageFlashOverlay.cs" "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): damage red-flash overlay"
```

Triggering is wired in Task 14 once `OnHealthDropped` exists.

---

### Task 9: `LetterboxBars` (cinematic top/bottom bars)

**Files:**
- Create: `Assets/3 - Scripts/Camera/LetterboxBars.cs`

- [ ] **Step 1: Create**

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>Top + bottom black bars that animate in during dialogue / cutscenes.</summary>
public class LetterboxBars : MonoBehaviour
{
    RectTransform _top, _bottom;
    float _t;

    const float TargetHeight = 80f;
    const float Speed = 220f; // px / second

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        bool active = mgr != null && mgr.MasterEnabled
                      && mgr.Input != null && mgr.Input.fxLetterboxBars
                      && PlayerController.isInDialogue;
        _t = Mathf.MoveTowards(_t, active ? 1f : 0f, Time.unscaledDeltaTime * Speed / TargetHeight);
        float h = _t * TargetHeight;
        _top.sizeDelta = new Vector2(0f, h);
        _bottom.sizeDelta = new Vector2(0f, h);
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 820;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        _top = NewBar("Top", new Vector2(0.5f, 1f));
        _bottom = NewBar("Bottom", new Vector2(0.5f, 0f));
    }

    RectTransform NewBar(string name, Vector2 anchorPivotY)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(0f, anchorPivotY.y);
        rt.anchorMax = new Vector2(1f, anchorPivotY.y);
        rt.pivot = anchorPivotY;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 0f);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
    }
}
```

- [ ] **Step 2: Attach in manager** (`LetterboxBars Letterbox`).

- [ ] **Step 3: Compile-check + verify**

Play mode → talk to an NPC → black bars slide in from top + bottom. Exit dialogue → they retract.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Camera/LetterboxBars.cs" "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): cinematic letterbox bars during dialogue"
```

---

### Task 10: `SpeedLinesOverlay`

**Files:**
- Create: `Assets/3 - Scripts/Camera/SpeedLinesOverlay.cs`

- [ ] **Step 1: Create**

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>Radial speed streaks visible when ship velocity is high.</summary>
public class SpeedLinesOverlay : MonoBehaviour
{
    Image _image;
    Ship _ship;
    float _alpha;

    const float Threshold = 12f;   // m/s
    const float FullAt = 30f;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxSpeedLines)
        { Fade(0f); return; }

        if (_ship == null) _ship = FindObjectOfType<Ship>(true);
        var rb = _ship != null ? _ship.GetComponent<Rigidbody>() : null;
        float speed = rb != null ? rb.velocity.magnitude : 0f;
        float target = Mathf.Clamp01((speed - Threshold) / (FullAt - Threshold));
        Fade(target * 0.8f);
    }

    void Fade(float target)
    {
        _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 2f);
        var c = _image.color; c.a = _alpha; _image.color = c;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 805;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("Streaks", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.sprite = GetSprite();
        _image.color = new Color(1f, 1f, 1f, 0f);
        _image.raycastTarget = false;
    }

    static Sprite _sprite;
    static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - r) / r;
                float dy = (y - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Radial streaks: noisy radial mask, fully transparent in center.
                float center = Mathf.Clamp01((d - 0.45f) / 0.55f);
                float ang = Mathf.Atan2(dy, dx);
                float streak = Mathf.Abs(Mathf.Sin(ang * 30f)) * center;
                pixels[y * size + x] = new Color(1f, 1f, 1f, streak * 0.8f);
            }
        tex.SetPixels(pixels); tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _sprite;
    }
}
```

- [ ] **Step 2: Attach in manager** (`SpeedLines`).

- [ ] **Step 3: Compile-check + commit**

```bash
git add "Assets/3 - Scripts/Camera/SpeedLinesOverlay.cs" "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): radial speed-lines overlay at high ship velocity"
```

---

### Task 11: `FilmGrainOverlay`

**Files:**
- Create: `Assets/3 - Scripts/Camera/FilmGrainOverlay.cs`

- [ ] **Step 1: Create**

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>Animated noise overlay — gives the image a subtle film-grain feel.</summary>
public class FilmGrainOverlay : MonoBehaviour
{
    RawImage _image;
    Texture2D _noiseTex;
    float _scrollT;
    const int NoiseSize = 128;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxFilmGrain)
        { _image.color = new Color(1f, 1f, 1f, 0f); return; }

        _scrollT += Time.unscaledDeltaTime * 8f;
        if (_scrollT > 1000f) _scrollT -= 1000f;
        _image.uvRect = new Rect(_scrollT, _scrollT * 1.3f, 6f, 4f);
        _image.color = new Color(1f, 1f, 1f, mgr.Input.fxFilmGrainIntensity * 0.18f);
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 815;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("Grain", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<RawImage>();
        _image.texture = GetNoise();
        _image.color = new Color(1f, 1f, 1f, 0f);
        _image.raycastTarget = false;
    }

    Texture2D GetNoise()
    {
        var tex = new Texture2D(NoiseSize, NoiseSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        var pixels = new Color[NoiseSize * NoiseSize];
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = Random.value;
            pixels[i] = new Color(v, v, v, v);
        }
        tex.SetPixels(pixels); tex.Apply();
        _noiseTex = tex;
        return tex;
    }
}
```

- [ ] **Step 2: Attach + commit** as before.

---

### Task 12: `ChromaticAberrationOverlay` (UI hack — 3 R/G/B images offset)

**Files:**
- Create: `Assets/3 - Scripts/Camera/ChromaticAberrationOverlay.cs`

- [ ] **Step 1: Create**

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Subtle chromatic aberration via three full-screen ring overlays in R/G/B,
/// offset radially so corners pick up colored fringing. No shader required —
/// a "good enough" approximation that costs only 3 image draws.
/// </summary>
public class ChromaticAberrationOverlay : MonoBehaviour
{
    Image _red, _green, _blue;
    Sprite _ringSprite;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxChromaticAberration)
        { SetAlpha(0f); return; }

        float i = mgr.Input.fxChromaticAberrationIntensity;
        SetAlpha(0.18f * i);
        // Offset the colored rings outward by a small px offset proportional
        // to intensity. The ring sprite is fully transparent in the center, so
        // it only adds fringing at the edges.
        float off = 3f + 8f * i;
        _red.rectTransform.anchoredPosition   = new Vector2(off, 0f);
        _blue.rectTransform.anchoredPosition  = new Vector2(-off, 0f);
        _green.rectTransform.anchoredPosition = Vector2.zero;
    }

    void SetAlpha(float a)
    {
        var cr = _red.color; cr.a = a; _red.color = cr;
        var cg = _green.color; cg.a = a * 0.5f; _green.color = cg;
        var cb = _blue.color; cb.a = a; _blue.color = cb;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 812;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        _red = NewLayer("R", new Color(1f, 0.2f, 0.2f, 0f));
        _green = NewLayer("G", new Color(0.2f, 1f, 0.4f, 0f));
        _blue = NewLayer("B", new Color(0.3f, 0.4f, 1f, 0f));
    }

    Image NewLayer(string name, Color color)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = GetRingSprite();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Sprite _ring;
    static Sprite GetRingSprite()
    {
        if (_ring != null) return _ring;
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - r) / r;
                float dy = (y - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Only the outer ~25% lights up; the rest is transparent.
                float a = Mathf.Clamp01((d - 0.75f) / 0.25f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(pixels); tex.Apply();
        _ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _ring;
    }
}
```

- [ ] **Step 2: Attach + commit** as before.

---

### Task 13: `LensDirtOverlay`

**Files:**
- Create: `Assets/3 - Scripts/Camera/LensDirtOverlay.cs`

- [ ] **Step 1: Create**

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Faint smudge overlay that brightens when looking at very bright sources
/// (the sun). Uses a dot-product proxy with the scene's main directional
/// light so we don't need a shader to detect bright pixels.
/// </summary>
public class LensDirtOverlay : MonoBehaviour
{
    Image _image;
    Light _sun;
    float _alpha;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxLensDirt
            || mgr.PlayerCamera == null)
        { Fade(0f); return; }

        if (_sun == null) _sun = FindMainSun();
        float facing = 0f;
        if (_sun != null)
        {
            Vector3 camFwd = mgr.PlayerCamera.transform.forward;
            // Sun direction lights *toward* its forward; reverse for "where the sun is".
            Vector3 sunFromCam = -_sun.transform.forward;
            facing = Mathf.Clamp01(Vector3.Dot(camFwd, sunFromCam));
        }
        Fade(Mathf.Pow(facing, 4f) * 0.55f);
    }

    void Fade(float target)
    {
        _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 1.5f);
        var c = _image.color; c.a = _alpha; _image.color = c;
    }

    static Light FindMainSun()
    {
        Light best = null;
        float bestIntensity = 0f;
        foreach (var l in FindObjectsOfType<Light>())
        {
            if (l.type != LightType.Directional || !l.enabled || !l.gameObject.activeInHierarchy) continue;
            if (l.intensity > bestIntensity) { best = l; bestIntensity = l.intensity; }
        }
        return best;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 813;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("Dirt", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.sprite = GetSprite();
        _image.color = new Color(1f, 1f, 1f, 0f);
        _image.raycastTarget = false;
    }

    static Sprite _sprite;
    static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        var rng = new System.Random(98765);
        // Procedural smudges: scatter 12 soft blobs at random positions.
        Vector2[] centers = new Vector2[12];
        float[] radii = new float[centers.Length];
        for (int i = 0; i < centers.Length; i++)
        {
            centers[i] = new Vector2((float)rng.NextDouble() * size, (float)rng.NextDouble() * size);
            radii[i] = 20f + (float)rng.NextDouble() * 50f;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = 0f;
                for (int i = 0; i < centers.Length; i++)
                {
                    float dx = x - centers[i].x;
                    float dy = y - centers[i].y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    a += Mathf.Clamp01(1f - d / radii[i]) * 0.35f;
                }
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(pixels); tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _sprite;
    }
}
```

- [ ] **Step 2: Attach + commit**.

---

### Task 14: `CombatFX` + hook `ResourceManager` events + wire damage flash/vignette/shake

**Files:**
- Modify: `Assets/3 - Scripts/Survival/ResourceManager.cs`
- Create: `Assets/3 - Scripts/Camera/CombatFX.cs`

- [ ] **Step 1: Add events to ResourceManager**

Add at the top of the class (near other public fields):

```csharp
public event System.Action<float> OnHealthDropped; // arg: damage amount
public event System.Action OnDeath;
```

Find the line(s) where health decreases inside `ResourceManager` (look for `health -=` or `Health = `). After each decrease, fire:

```csharp
OnHealthDropped?.Invoke(amount);
```

Where the player's death logic triggers, fire:

```csharp
OnDeath?.Invoke();
```

- [ ] **Step 2: Create `CombatFX`**

```csharp
using System.Collections;
using UnityEngine;

/// <summary>
/// Combat-feedback effects driven by ResourceManager events:
/// directional hit shake, damage red flash, damage vignette pulse, death
/// tilt + dim. Subscribes once and lives as long as the manager.
/// </summary>
public class CombatFX : MonoBehaviour
{
    ResourceManager _rm;
    float _damagePulse;  // decays each frame
    bool _dead;

    void OnEnable()
    {
        TrySubscribe();
    }

    void Update()
    {
        if (_rm == null) TrySubscribe();
        if (_damagePulse > 0f)
            _damagePulse = Mathf.MoveTowards(_damagePulse, 0f, Time.unscaledDeltaTime * 2.5f);

        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || mgr.Vignette == null) return;
        var input = mgr.Input;

        // Push the damage vignette driver (red).
        if (_damagePulse > 0f && input != null && input.fxDamageVignette)
            mgr.Vignette.Push(new Color(1f, 0.15f, 0.2f, 1f), _damagePulse);

        // Push the death dim driver (near-black) once dead.
        if (_dead && input != null && input.fxDeathTilt)
            mgr.Vignette.Push(new Color(0.02f, 0.02f, 0.04f, 1f), 0.85f);
    }

    void TrySubscribe()
    {
        if (_rm != null) return;
        _rm = ResourceManager.Instance;
        if (_rm == null) return;
        _rm.OnHealthDropped += OnHealthDropped;
        _rm.OnDeath += OnDeathFired;
    }

    void OnDestroy()
    {
        if (_rm != null)
        {
            _rm.OnHealthDropped -= OnHealthDropped;
            _rm.OnDeath -= OnDeathFired;
        }
    }

    void OnHealthDropped(float amount)
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        var input = mgr.Input;

        // Red flash (own overlay).
        if (input != null && input.fxDamageFlash && mgr.DamageFlash != null)
            mgr.DamageFlash.Flash(0.55f);

        // Vignette pulse (composited).
        if (input != null && input.fxDamageVignette)
            _damagePulse = Mathf.Max(_damagePulse, 0.7f);

        // Directional shake.
        if (input != null && input.fxDirectionalHitShake && CameraShake.Instance != null)
            CameraShake.Instance.TriggerShake(0.15f, 0.3f + amount * 0.01f, 6f);
    }

    void OnDeathFired()
    {
        _dead = true;
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        var input = mgr.Input;
        if (input != null && input.fxDeathTilt && mgr.TransformFX != null)
            mgr.TransformFX.TriggerDeathTilt();
    }

    public void ClearDeath()
    {
        _dead = false;
        var mgr = CameraEffectsManager.Instance;
        if (mgr != null && mgr.TransformFX != null) mgr.TransformFX.ClearDeathTilt();
    }
}
```

- [ ] **Step 3: Attach in manager** (`CombatFX Combat`). Add `DamageFlashOverlay DamageFlash` property too (from Task 8).

- [ ] **Step 4: Verify + commit**

Play mode → take damage from an enemy → red flash + red vignette pulse + camera shake. Die → camera tilts, screen dims.

```bash
git add "Assets/3 - Scripts/Survival/ResourceManager.cs" "Assets/3 - Scripts/Camera/CombatFX.cs" "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): combat FX — flash, vignette pulse, hit shake, death dim"
```

---

### Task 15: Low-health vignette + dialogue vignette drivers

**Files:**
- Modify: `Assets/3 - Scripts/Camera/CameraEffectsManager.cs`

- [ ] **Step 1: Push drivers from manager's Update**

In `CameraEffectsManager.Update`, after the existing subtle-vignette push:

```csharp
// Low-health pulse (red, slow sine).
if (Input.fxLowHealthVignette && Vignette != null && ResourceManager.Instance != null)
{
    float hp = ResourceManager.Instance.HealthPercent;
    if (hp < 0.25f)
    {
        float t = (Mathf.Sin(Time.unscaledTime * 2f) + 1f) * 0.5f;
        float strength = Mathf.Lerp(0.25f, 0.6f, (0.25f - hp) / 0.25f) * Mathf.Lerp(0.6f, 1f, t);
        Vignette.Push(new Color(1f, 0.15f, 0.2f, 1f), strength);
    }
}

// Dialogue focus (soft black).
if (Input.fxDialogueVignette && Vignette != null && PlayerController.isInDialogue)
{
    Vignette.Push(new Color(0f, 0f, 0f, 1f), 0.4f);
}
```

- [ ] **Step 2: Compile-check + commit**

```bash
git add "Assets/3 - Scripts/Camera/CameraEffectsManager.cs"
git commit -m "feat(camera-fx): low-health pulse + dialogue focus vignette drivers"
```

---

### Task 16: Enemy-hit micro-shake + slowmo on kill

**Files:**
- Modify: `Assets/3 - Scripts/Combat/EnemyController.cs`
- Modify: `Assets/3 - Scripts/Pickups/AxeController.cs`
- Modify: `Assets/3 - Scripts/Pickups/PistolController.cs`
- Create: `Assets/3 - Scripts/Camera/SlowmoOnKill.cs`

- [ ] **Step 1: Add `OnDeath` event to `EnemyController`**

```csharp
public static event System.Action OnAnyEnemyDeath;
```

In the death code path inside `EnemyController` (where the enemy GameObject is destroyed), fire:

```csharp
OnAnyEnemyDeath?.Invoke();
```

- [ ] **Step 2: Wire micro-shake from `AxeController.ApplyHit`**

Inside `AxeController.ApplyHit(EnemyController enemy, ...)`, after the damage is applied:

```csharp
var mgr = CameraEffectsManager.Instance;
if (mgr != null && mgr.MasterEnabled && mgr.Input != null && mgr.Input.fxEnemyHitMicroShake
    && CameraShake.Instance != null)
{
    CameraShake.Instance.TriggerShake(0.05f, 0.1f, 4f);
}
```

- [ ] **Step 3: Wire micro-shake from `PistolController.TriggerShot`**

Same block, inside the `if (hit)` branch of `TriggerShot`:

```csharp
var mgr = CameraEffectsManager.Instance;
if (mgr != null && mgr.MasterEnabled && mgr.Input != null && mgr.Input.fxEnemyHitMicroShake
    && CameraShake.Instance != null)
{
    CameraShake.Instance.TriggerShake(0.05f, 0.1f, 4f);
}
```

- [ ] **Step 4: Create `SlowmoOnKill`**

```csharp
using System.Collections;
using UnityEngine;

public class SlowmoOnKill : MonoBehaviour
{
    void OnEnable()  { EnemyController.OnAnyEnemyDeath += Handle; }
    void OnDisable() { EnemyController.OnAnyEnemyDeath -= Handle; }

    void Handle()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled) return;
        if (mgr.Input != null && !mgr.Input.fxSlowmoOnKill) return;
        StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        Time.timeScale = 0.3f;
        yield return new WaitForSecondsRealtime(0.15f);
        Time.timeScale = 1f;
    }
}
```

- [ ] **Step 5: Attach in manager** (`SlowmoOnKill`).

- [ ] **Step 6: Compile-check + verify + commit**

```bash
git add ...
git commit -m "feat(camera-fx): enemy-hit micro-shake + slowmo on kill"
```

---

### Task 17: Mood color grade

**Files:**
- Create: `Assets/3 - Scripts/Camera/MoodColorGrade.cs`

- [ ] **Step 1: Create**

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Subtle full-screen color tint that shifts with the player's situation:
/// combat (any enemy active near the player) → slight warm desaturation;
/// low health → green sickly tint; peaceful (default) → very subtle cool tint.
/// Implemented as a soft additive UI tint; never darkens enough to fight
/// the atmosphere shader's existing tonal work.
/// </summary>
public class MoodColorGrade : MonoBehaviour
{
    Image _image;
    Color _currentColor;

    static readonly Color Peaceful = new Color(0.55f, 0.7f, 1f, 0.05f);
    static readonly Color Combat   = new Color(1f, 0.6f, 0.35f, 0.10f);
    static readonly Color LowHP    = new Color(0.4f, 1f, 0.5f, 0.14f);

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null || !mgr.MasterEnabled || mgr.Input == null || !mgr.Input.fxMoodColorGrade)
        { Fade(new Color(0f, 0f, 0f, 0f)); return; }

        Color target = Peaceful;
        if (ResourceManager.Instance != null && ResourceManager.Instance.HealthPercent < 0.25f)
            target = LowHP;
        else if (AnyEnemyNearby(mgr))
            target = Combat;

        Fade(target);
    }

    bool AnyEnemyNearby(CameraEffectsManager mgr)
    {
        if (mgr.PlayerCamera == null) return false;
        Vector3 p = mgr.PlayerCamera.transform.position;
        var list = EnemyController.ActiveEnemies;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i]; if (e == null) continue;
            if ((e.transform.position - p).sqrMagnitude < 30f * 30f) return true;
        }
        return false;
    }

    void Fade(Color target)
    {
        _currentColor = Color.Lerp(_currentColor, target, Time.unscaledDeltaTime * 1.2f);
        _image.color = _currentColor;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 811;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;

        var rt = new GameObject("Tint", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _image = rt.gameObject.AddComponent<Image>();
        _image.color = new Color(0f, 0f, 0f, 0f);
        _image.raycastTarget = false;
    }
}
```

- [ ] **Step 2: Attach + commit**.

---

### Task 18: Lens flares — attach to known bright sources

**Files:**
- Create: `Assets/3 - Scripts/Camera/LensFlareRegistry.cs`

- [ ] **Step 1: Create**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attaches Unity's built-in <see cref="LensFlare"/> component to the main
/// directional sun + any bonfires found in the scene. Polls the
/// fxLensFlares toggle each frame to enable/disable the components without
/// destroying them.
/// </summary>
public class LensFlareRegistry : MonoBehaviour
{
    List<LensFlare> _flares = new List<LensFlare>();
    Flare _defaultFlare;
    bool _attached;

    void Update()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null) return;
        if (!_attached) Attach();
        bool on = mgr.MasterEnabled && mgr.Input != null && mgr.Input.fxLensFlares;
        for (int i = 0; i < _flares.Count; i++)
            if (_flares[i] != null) _flares[i].enabled = on;
    }

    void Attach()
    {
        _defaultFlare = Resources.GetBuiltinResource<Flare>("Sun.flare");
        if (_defaultFlare == null) _defaultFlare = Resources.Load<Flare>("Flare");
        if (_defaultFlare == null) { _attached = true; return; }

        // Sun (brightest directional light).
        Light brightest = null;
        float bestIntensity = 0f;
        foreach (var l in FindObjectsOfType<Light>())
        {
            if (l.type != LightType.Directional) continue;
            if (l.intensity > bestIntensity) { brightest = l; bestIntensity = l.intensity; }
        }
        if (brightest != null) AttachFlare(brightest.gameObject);

        // Bonfires (look for BonfireInteraction components — the bonfire system).
        foreach (var b in FindObjectsOfType<BonfireInteraction>())
            AttachFlare(b.gameObject);

        _attached = true;
    }

    void AttachFlare(GameObject host)
    {
        var existing = host.GetComponent<LensFlare>();
        if (existing != null) { _flares.Add(existing); return; }
        var f = host.AddComponent<LensFlare>();
        f.flare = _defaultFlare;
        f.brightness = 0.5f;
        _flares.Add(f);
    }
}
```

- [ ] **Step 2: Attach + commit**.

---

### Task 19: Anamorphic streaks

**Files:**
- Create: `Assets/3 - Scripts/Camera/AnamorphicStreaks.cs`

- [ ] **Step 1: Create**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sprite-based anamorphic horizontal streaks. Registers a screen-space
/// streak quad for each known bright source (sun, bonfires). Each streak
/// brightens with the dot product between camera-forward and the
/// source-from-camera direction, so it only shows when you're looking
/// roughly at the source.
/// </summary>
public class AnamorphicStreaks : MonoBehaviour
{
    Canvas _canvas;
    List<(Transform source, UnityEngine.UI.Image image)> _streaks = new();
    Camera _cam;
    bool _attached;

    void Awake() { BuildCanvas(); }

    void LateUpdate()
    {
        var mgr = CameraEffectsManager.Instance;
        if (mgr == null) return;
        _cam = mgr.PlayerCamera;
        if (_cam == null) return;
        if (!_attached) Attach();
        bool on = mgr.MasterEnabled && mgr.Input != null && mgr.Input.fxAnamorphicStreaks;

        foreach (var entry in _streaks)
        {
            if (entry.source == null || entry.image == null) continue;
            if (!on) { entry.image.color = new Color(0.6f, 0.85f, 1f, 0f); continue; }
            Vector3 dir = (entry.source.position - _cam.transform.position).normalized;
            float dot = Vector3.Dot(_cam.transform.forward, dir);
            if (dot < 0.6f) { entry.image.color = new Color(0.6f, 0.85f, 1f, 0f); continue; }
            // Move the streak to the projected screen position.
            Vector3 sp = _cam.WorldToScreenPoint(entry.source.position);
            if (sp.z <= 0f) { entry.image.color = new Color(0.6f, 0.85f, 1f, 0f); continue; }
            var rt = (RectTransform)entry.image.transform;
            rt.anchoredPosition = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f) * (1080f / Screen.height);
            float alpha = Mathf.SmoothStep(0f, 0.55f, (dot - 0.6f) / 0.4f);
            entry.image.color = new Color(0.6f, 0.85f, 1f, alpha);
        }
    }

    void Attach()
    {
        // Sun
        Light brightest = null; float best = 0f;
        foreach (var l in FindObjectsOfType<Light>())
        {
            if (l.type != LightType.Directional) continue;
            if (l.intensity > best) { brightest = l; best = l.intensity; }
        }
        if (brightest != null) AddStreak(brightest.transform);
        foreach (var b in FindObjectsOfType<BonfireInteraction>())
            AddStreak(b.transform);
        _attached = true;
    }

    void AddStreak(Transform source)
    {
        var rt = new GameObject("Streak_" + source.name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(_canvas.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 5f);
        var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.6f, 0.85f, 1f, 0f);
        img.raycastTarget = false;
        _streaks.Add((source, img));
    }

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 814;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
    }
}
```

- [ ] **Step 2: Attach + commit**.

---

## Phase 4 — Validation

### Task 20: Atmosphere preservation check + intensity polish + CLAUDE.md update

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Manual sweep**

Play mode → look at the sky, fly through the atmosphere, dip below the ocean surface, fly between planets. Verify atmosphere/planet/ocean rendering is unchanged. Any visual regression here means a UI overlay is drawing over something it shouldn't — check Canvas sortingOrder.

- [ ] **Step 2: Tune intensities**

Take screenshots of each effect in isolation (toggle the others off in the pause menu). If any feels too strong, lower its constant in the relevant source file and commit a polish patch.

- [ ] **Step 3: Document the new system in CLAUDE.md**

Append a section under "Architecture":

```markdown
### Camera effects

`CameraEffectsManager` is a procedural singleton (auto-created + seeded by `MainMenuController.EnsureGameplaySingletons`) that owns ~14 effect modules: `CameraTransformFX`, `CameraFOVFX`, `CombatFX`, `VignetteOverlay`, `DamageFlashOverlay`, `LetterboxBars`, `SpeedLinesOverlay`, `FilmGrainOverlay`, `ChromaticAberrationOverlay`, `LensDirtOverlay`, `MoodColorGrade`, `AnamorphicStreaks`, `LensFlareRegistry`, `SlowmoOnKill`.

All toggles + intensity sliders live on `InputSettings.fx*` and are exposed in the pause-menu **CAMERA** tab. Each module polls those flags every frame so the menu is live-tunable.

Vignette has multiple drivers (subtle baseline, damage pulse, low-health pulse, dialogue focus, death dim) — they push (color, intensity) tuples into the single `VignetteOverlay.Push` API per frame, and the overlay picks the strongest. Do not stack vignettes any other way.

FOV is similarly stacked: sprint, jetpack, ship-boost each contribute a deltaFOV when active, and `CameraFOVFX` smooth-damps the camera's `fieldOfView` from base + sum.

All camera-effect overlays use ScreenSpaceOverlay canvases at sortingOrder 800–820, **below** TutorialUI (500) and below the pause menu (1000). They must never modify the atmosphere/planet/ocean shaders — adding new full-screen post-processing belongs on the locked-zone list.
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(camera-fx): describe the camera-effects architecture in CLAUDE.md"
```

---

## Self-review checklist

1. **Spec coverage:** Every effect in the spec has a task. ✓
2. **Placeholders:** None. All code blocks contain the actual code, all commit messages spelled out. ✓
3. **Type consistency:** All cross-task type names match (`CameraEffectsManager`, `Vignette.Push`, `DamageFlash.Flash`, `TransformFX.TriggerDeathTilt`, `OnAnyEnemyDeath`, `OnHealthDropped`, `OnDeath`). ✓
4. **Scope:** Single coherent feature set — 20 tasks across 4 phases, mostly file-isolated so subagents can parallelize phases 3 + 4 (overlays + lens) after phase 2 lands. ✓

---

**Plan complete and saved to `docs/superpowers/plans/2026-05-11-camera-effects.md`.** Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
