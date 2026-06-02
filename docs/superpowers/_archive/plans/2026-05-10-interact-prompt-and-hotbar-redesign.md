# Interact Prompt + Hotbar Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify every "Press F to ..." prompt under a shared `InteractPromptUI` component (matching `TutorialUI`'s beveled-pill aesthetic) and revamp the hotbar with rounded slots, an active-slot scale/lift animation, proper item icons, and a floating name plate.

**Architecture:**
- One singleton (`InteractPromptUI`) replaces every per-NPC `talkPromptText`, the `GameUI.interactionInfo` text, and the close-hint text built into the cook/sell panels. Same visual language as `TutorialUI` so the prompts read as part of the same family.
- Hotbar keeps its current rounded-slot shape and `Hotbar.cs` structure; the visual revamp is sprite swaps (text → icon image), slot-size changes, an active-state animation coroutine, and a beveled name plate that bridges the hotbar's rounded language to the prompt pill's beveled language.
- Five item icons authored as PNG sprites at `Assets/2 - Materials/HotbarIcons/`, generated via Unity MCP image generation.

**Tech Stack:** Unity 2022.3 (no `.asmdef`s — everything compiles under default `Assembly-CSharp`), TextMeshPro, runtime `Texture2D`/`Sprite` generation for procedural beveled panels, no test framework (verification is Unity Editor compile + Play mode).

**Spec reference:** `docs/superpowers/specs/2026-05-10-interact-prompt-and-hotbar-redesign-design.md`

---

## File map

**New:**
- `Assets/3 - Scripts/UI/InteractPromptUI.cs` — singleton, the shared prompt
- `Assets/2 - Materials/HotbarIcons/water_bottle_icon.png` (+ 4 siblings)

**Modified:**
- `Assets/3 - Scripts/Scripts/Game/UI/GameUI.cs` — delegate to `InteractPromptUI`
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `InteractPromptUI` in `EnsureGameplaySingletons`
- `Assets/3 - Scripts/UI/Hotbar.cs` — slot size, icon swap, active animation, name plate
- `Assets/3 - Scripts/Pickups/{WaterBottleController,AxeController,PistolController}.cs` — `hotbarIcon` field
- `Assets/3 - Scripts/Fishing/FishingRodController.cs` — `hotbarIcon` field
- `Assets/3 - Scripts/NPC_Dialogue/GuitarController.cs` — `hotbarIcon` field (note: lives in NPC_Dialogue, not Pickups)
- `Assets/3 - Scripts/NPC_Dialogue/{NPCDialogue,TevDialogue,RandomAlienDialogue,GuitarShopNPC,BonfireNPCDialogue,BonfireInteraction}.cs` — `talkPromptText`/close-hint refactors
- `Assets/3 - Scripts/Vendor/Alien7Vendor.cs` — `talkPromptText` refactor
- `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` — `talkPromptText` + close-hint refactors

---

## Conventions used by every task

- **Compile check:** after each code change, focus the Unity Editor window; Unity recompiles automatically. Inspect Console — there must be **zero red errors** before continuing. Yellow warnings are acceptable unless they're new and obviously caused by this change.
- **Commit format:** match the repo style (`fix(scope): ...` / `feat(scope): ...` short summary, no body unless needed). Examples in `git log --oneline`.
- **`git add` is always specific:** never `git add -A`. The repo has unrelated uncommitted edits — only stage the files this task changed.
- **Per CLAUDE.md, no commits without instruction.** This plan's commit steps are the explicit instruction; execute them as written.

---

## Task 1: Create `InteractPromptUI` singleton

**Files:**
- Create: `Assets/3 - Scripts/UI/InteractPromptUI.cs`

This task ships a working component that nothing calls yet. We'll wire callers in later tasks. The class is self-contained — it auto-creates at scene load, builds its own canvas, and exposes a static API that's a no-op until `Show` is called.

- [ ] **Step 1: Create the file with the full implementation.**

```csharp
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Shared "Press F to ..." prompt. One pill at bottom-center. Owner-based
// sticky API matching what GameUI.ShowInteractionPrompt did; replaces every
// per-NPC talkPromptText and the cook/sell panel close-hint texts. Visual is
// TutorialUI's pill 1:1 (clipped corners, cyan LED bar, dark navy fill,
// bracketed [F] keycap).
public class InteractPromptUI : MonoBehaviour
{
    public static InteractPromptUI Instance { get; private set; }

    [Tooltip("Seconds for the slide-in / slide-out animation.")]
    public float slideDuration = 0.25f;
    [Tooltip("Pixels the pill slides up from when first revealed.")]
    public float slideOffset = 40f;
    [Tooltip("Vertical anchor — pixels above the bottom edge of the screen at rest.")]
    public float bottomMargin = 140f;
    [Tooltip("Diagonal cut on top-left and bottom-right corners (pixels).")]
    public float bevelSize = 14f;

    // ── Palette (matches TutorialUI exactly) ─────────────────────────
    static readonly Color PillBgBottomColor = new Color32(0x0A, 0x18, 0x28, 0xEB);
    static readonly Color PillBorderColor   = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color AccentColor       = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color TipColor          = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TipGlowColor      = new Color(0.38f, 0.78f, 1f, 0.45f);

    // ── Sprite cache (panel + outline). Generated lazily, kept static
    //    so multiple promptUIs share the same texture. ─────────────────
    static Sprite beveledPanelSprite;
    static Sprite beveledOutlineSprite;

    // ── Internal refs ────────────────────────────────────────────────
    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _pillRoot;
    RectTransform _pillRect;
    Image _pillBg;
    Image _pillBorder;
    Image _accentBar;
    TextMeshProUGUI _bodyText;

    Coroutine _slideRoutine;
    Coroutine _oneShotRoutine;

    bool _shown;
    UnityEngine.Object _owner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("InteractPromptUI");
        DontDestroyOnLoad(go);
        go.AddComponent<InteractPromptUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        if (_group != null) _group.alpha = 0f;
        if (_pillRoot != null) _pillRoot.anchoredPosition = OffScreenPos();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    Vector2 RestPos()      => new Vector2(0f, bottomMargin);
    Vector2 OffScreenPos() => new Vector2(0f, bottomMargin - slideOffset);

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Sticky prompt; stays until <c>Clear(owner)</c> with the same owner.</summary>
    public static void Show(UnityEngine.Object owner, string text)
    {
        if (Instance == null) return;
        Instance._owner = owner;
        Instance.ShowInternal(text);
    }

    /// <summary>Clears iff <paramref name="owner"/> matches the current owner. Idempotent.</summary>
    public static void Clear(UnityEngine.Object owner)
    {
        if (Instance == null) return;
        if (Instance._owner != owner) return;
        Instance._owner = null;
        Instance.HideInternal();
    }

    /// <summary>Legacy: 3 s self-clearing prompt. Used by GameUI.DisplayInteractionInfo.</summary>
    public static void ShowOneShot(string text, float seconds = 3f)
    {
        if (Instance == null) return;
        Instance._owner = null;
        Instance.ShowInternal(text);
        if (Instance._oneShotRoutine != null) Instance.StopCoroutine(Instance._oneShotRoutine);
        Instance._oneShotRoutine = Instance.StartCoroutine(Instance.OneShotRoutine(seconds));
    }

    void ShowInternal(string text)
    {
        if (_bodyText != null) _bodyText.text = DecorateKeyGlyphs(text ?? "");
        if (_shown) return;
        _shown = true;
        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        _slideRoutine = StartCoroutine(SlideRoutine(true));
    }

    void HideInternal()
    {
        if (!_shown) return;
        _shown = false;
        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        _slideRoutine = StartCoroutine(SlideRoutine(false));
    }

    IEnumerator OneShotRoutine(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (_owner == null) HideInternal();
        _oneShotRoutine = null;
    }

    IEnumerator SlideRoutine(bool show)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, slideDuration);
        Vector2 from = (_pillRoot != null) ? _pillRoot.anchoredPosition : OffScreenPos();
        Vector2 to = show ? RestPos() : OffScreenPos();
        float fromAlpha = (_group != null) ? _group.alpha : 0f;
        float toAlpha = show ? 1f : 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = show ? 1f - Mathf.Pow(1f - u, 3f) : u * u * u;
            if (_pillRoot != null) _pillRoot.anchoredPosition = Vector2.Lerp(from, to, k);
            if (_group != null) _group.alpha = Mathf.Lerp(fromAlpha, toAlpha, k);
            yield return null;
        }
        if (_pillRoot != null) _pillRoot.anchoredPosition = to;
        if (_group != null) _group.alpha = toAlpha;
        _slideRoutine = null;
    }

    // ── Build canvas ─────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above hotbar (50), below tutorial pill (500), below pause (1000)
        _canvas = canvas;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Root anchored at bottom-centre, sized by content.
        _pillRoot = NewUI("PromptRoot", transform);
        _pillRoot.anchorMin = new Vector2(0.5f, 0f);
        _pillRoot.anchorMax = new Vector2(0.5f, 0f);
        _pillRoot.pivot = new Vector2(0.5f, 0f);
        _pillRoot.anchoredPosition = RestPos();
        var rootFitter = _pillRoot.gameObject.AddComponent<ContentSizeFitter>();
        rootFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Pill body — beveled clipped panel.
        var pillRT = NewUI("Pill", _pillRoot);
        pillRT.anchorMin = new Vector2(0f, 0f);
        pillRT.anchorMax = new Vector2(1f, 0f);
        pillRT.pivot = new Vector2(0.5f, 0f);
        _pillRect = pillRT;

        _pillBg = pillRT.gameObject.AddComponent<Image>();
        _pillBg.sprite = GetBeveledPanelSprite();
        _pillBg.type = Image.Type.Sliced;
        _pillBg.color = PillBgBottomColor;
        _pillBg.raycastTarget = false;

        // Cyan LED accent bar — anchored to left edge.
        var accentRT = NewUI("AccentBar", pillRT);
        accentRT.anchorMin = new Vector2(0f, 0f);
        accentRT.anchorMax = new Vector2(0f, 1f);
        accentRT.pivot = new Vector2(0f, 0.5f);
        accentRT.anchoredPosition = new Vector2(8f, 0f);
        accentRT.sizeDelta = new Vector2(3f, -16f);
        accentRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _accentBar = accentRT.gameObject.AddComponent<Image>();
        _accentBar.color = AccentColor;
        _accentBar.raycastTarget = false;

        // Border outline.
        var border = NewUI("Border", pillRT);
        Stretch(border);
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _pillBorder = border.gameObject.AddComponent<Image>();
        _pillBorder.sprite = GetBeveledOutlineSprite();
        _pillBorder.type = Image.Type.Sliced;
        _pillBorder.color = PillBorderColor;
        _pillBorder.raycastTarget = false;

        var pillVlg = pillRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        pillVlg.childAlignment = TextAnchor.MiddleLeft;
        pillVlg.childControlWidth = true;
        pillVlg.childControlHeight = true;
        pillVlg.childForceExpandWidth = false;
        pillVlg.childForceExpandHeight = false;
        pillVlg.padding = new RectOffset(22, 18, 10, 10);

        var pillFitter = pillRT.gameObject.AddComponent<ContentSizeFitter>();
        pillFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        pillFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Body text — single-line "[F] Pick up bottle".
        _bodyText = NewText(pillRT, "Body", "", 14f, FontStyles.Bold, TipColor);
        _bodyText.alignment = TextAlignmentOptions.MidlineLeft;
        _bodyText.characterSpacing = 1f;
        _bodyText.enableWordWrapping = false;
        var bodyGlow = _bodyText.gameObject.AddComponent<Shadow>();
        bodyGlow.effectColor = TipGlowColor;
        bodyGlow.effectDistance = new Vector2(0f, 0f);
        var bodyShadow = _bodyText.gameObject.AddComponent<Shadow>();
        bodyShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        bodyShadow.effectDistance = new Vector2(0f, -2f);
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

    static TextMeshProUGUI NewText(Transform parent, string name, string text,
                                    float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
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
            if (_hudFont == null) _hudFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _hudFontResolved = true;
        }
        if (_hudFont != null) t.font = _hudFont;
    }

    // ── Procedural sprite generation (copied from TutorialUI; one extra
    //    caller doesn't justify a refactor of the existing component). ──

    static Sprite GetBeveledPanelSprite()
    {
        if (beveledPanelSprite != null) return beveledPanelSprite;
        var tex = MakeBeveledPanelTexture(64, 14, true);
        beveledPanelSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                            100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        beveledPanelSprite.name = "InteractPromptBeveledPanel";
        return beveledPanelSprite;
    }

    static Sprite GetBeveledOutlineSprite()
    {
        if (beveledOutlineSprite != null) return beveledOutlineSprite;
        var tex = MakeBeveledOutlineTexture(64, 14, 2);
        beveledOutlineSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                              100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        beveledOutlineSprite.name = "InteractPromptBeveledOutline";
        return beveledOutlineSprite;
    }

    static Texture2D MakeBeveledPanelTexture(int size, int bevel, bool verticalGradient)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        int s = size - 1;
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / s;
            float vAlpha = verticalGradient ? Mathf.Lerp(0.85f, 1.0f, v) : 1.0f;
            for (int x = 0; x < size; x++)
            {
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

    static Texture2D MakeBeveledOutlineTexture(int size, int bevel, int thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        int s = size - 1;
        int innerBevel = Mathf.Max(0, bevel - thickness);
        int innerSize = size - 2 * thickness;
        for (int y = 0; y < size; y++)
        {
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
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ── Keycap glyph wrapping ────────────────────────────────────────
    // Mirrors TutorialUI.DecorateKeyGlyphs — wraps `<b>F</b>` etc. in a
    // bracketed cyan badge so it reads as a discrete keycap instead of bold text.
    static readonly string[] KbdLabels = new[]
    {
        "WASD + Shift",
        "left click", "Left click",
        "Space", "Shift", "Ctrl", "WASD", "mouse", "Mouse",
        "TAB", "Esc", "LMB", "RMB",
        "F", "E", "G", "M", "N", "B", "Q",
    };

    static string DecorateKeyGlyphs(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        string result = source;
        for (int i = 0; i < KbdLabels.Length; i++)
        {
            string label = KbdLabels[i];
            string needle = "<b>" + label + "</b>";
            if (result.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
            string replacement =
                "<color=#5CC8FF><size=115%>[</size><b>" + label + "</b><size=115%>]</size></color>";
            result = result.Replace(needle, replacement);
        }
        return result;
    }
}
```

- [ ] **Step 2: Compile check.**

Focus the Unity Editor window, wait for the compile spinner to clear. Verify Console has zero red errors. (Warnings about unused fields like `_canvas`, `_pillRect`, `_pillBg`, `_pillBorder` are fine — those are kept for future tweaks.)

- [ ] **Step 3: Smoke test.**

Enter Play mode in the `1.6.7.7.7` scene. The pill should not be visible (nothing has called `Show` yet). Open the Hierarchy: confirm a `InteractPromptUI` GameObject was auto-created with `DontDestroyOnLoad`. Exit Play mode.

- [ ] **Step 4: Commit.**

```bash
git add "Assets/3 - Scripts/UI/InteractPromptUI.cs"
git add "Assets/3 - Scripts/UI/InteractPromptUI.cs.meta"  # only if Unity has generated it
git commit -m "feat(prompt): add InteractPromptUI singleton (no callers yet)"
```

---

## Task 2: Wire `GameUI` to delegate to `InteractPromptUI`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/UI/GameUI.cs`

This makes every existing `Interactable` subclass start using the new pill — no per-NPC changes needed yet because `Interactable.Update` calls `GameUI.ShowInteractionPrompt`. After this task: hatches, pickups, mushrooms, and any other `Interactable` show the new beveled pill.

- [ ] **Step 1: Replace the file content.**

```csharp
using UnityEngine;

public class GameUI : MonoBehaviour {

    // Kept for backwards compat with scenes that wire this field, but the
    // new InteractPromptUI singleton is the visible prompt. We hide the
    // legacy text on first frame so it doesn't double-render.
    public TMPro.TMP_Text interactionInfo;

    static GameUI instance;
    bool _legacyHidden;

    void Update () {
        if (!_legacyHidden && interactionInfo != null) {
            interactionInfo.gameObject.SetActive(false);
            _legacyHidden = true;
        }
    }

    /// <summary>
    /// Sticky prompt owned by `owner`. Stays visible until the same owner
    /// calls ClearInteractionPrompt or another owner takes over.
    /// </summary>
    public static void ShowInteractionPrompt (Object owner, string info) {
        InteractPromptUI.Show(owner, info);
    }

    /// <summary>Clear the prompt iff `owner` is the current owner.</summary>
    public static void ClearInteractionPrompt (Object owner) {
        InteractPromptUI.Clear(owner);
    }

    /// <summary>
    /// Legacy one-shot prompt with 3 s auto-hide. Clears any current owner.
    /// Prefer ShowInteractionPrompt / ClearInteractionPrompt for in-zone prompts.
    /// </summary>
    public static void DisplayInteractionInfo (string info) {
        InteractPromptUI.ShowOneShot(info, 3f);
    }

    public static void CancelInteractionDisplay () {
        // No-op: ShowOneShot self-hides; sticky prompts are cleared by their owner.
    }

    static GameUI Instance {
        get {
            if (instance == null) instance = FindObjectOfType<GameUI>();
            return instance;
        }
    }
}
```

- [ ] **Step 2: Compile check.**

Console clean. The `Instance` property is now unused but kept so no other file breaks if it references `GameUI.Instance` (none do per a grep, but kept for safety).

- [ ] **Step 3: Play-mode test — hatch prompt.**

Enter Play mode. Walk the player onto the ship hatch. The new beveled pill should slide up from the bottom-center showing `[F] to open hatch` (or `close hatch` if already open). Walk away — the pill slides down. The legacy `interactionInfo` text (wherever it was anchored before) is no longer visible.

- [ ] **Step 4: Play-mode test — pickup prompt.**

Walk near a water bottle pickup (if one is reachable in the start state). The pill should show `[F] to pick up bottle`. If no bottle is reachable in the current scene, walk near any other `Interactable` (mushroom, fishing rod pickup, etc.). Confirm the pill renders.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/3 - Scripts/Scripts/Game/UI/GameUI.cs"
git commit -m "feat(prompt): route GameUI to InteractPromptUI; hide legacy text"
```

---

## Task 3: Add `InteractPromptUI` to `MainMenuController.EnsureGameplaySingletons`

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs:473`

Without this, loading a save from the main menu briefly has no prompt UI during the apply phase. Match the existing pattern used for `TutorialUI`, `Hotbar`, etc.

- [ ] **Step 1: Insert seeding block.**

Find `EnsureGameplaySingletons` (around line 473). After the existing `BonusTutorial` block (and before whatever comes next), insert:

```csharp
        if (InteractPromptUI.Instance == null)
        {
            var go = new GameObject("InteractPromptUI");
            DontDestroyOnLoad(go);
            go.AddComponent<InteractPromptUI>();
        }
```

- [ ] **Step 2: Compile check.**

Console clean.

- [ ] **Step 3: Play-mode test.**

Open the `MainMenu` scene. Click PLAY → NEW GAME. The gameplay scene loads. Walk to an Interactable. The pill renders. (No regression vs auto-create.)

Optional: Save a game from the pause menu, return to main menu, click PLAY → load that save. Same check — pill still works.

- [ ] **Step 4: Commit.**

```bash
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m "feat(prompt): seed InteractPromptUI in EnsureGameplaySingletons"
```

---

## Task 4: Refactor 7 NPC scripts to use `InteractPromptUI`

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/NPCDialogue.cs`
- Modify: `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs`
- Modify: `Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs`
- Modify: `Assets/3 - Scripts/NPC_Dialogue/GuitarShopNPC.cs`
- Modify: `Assets/3 - Scripts/NPC_Dialogue/BonfireNPCDialogue.cs`
- Modify: `Assets/3 - Scripts/Vendor/Alien7Vendor.cs`
- Modify: `Assets/3 - Scripts/Fishing/FishMarketNPC.cs`

Pattern: every place that currently does

```csharp
talkPromptText.text = $"Press {PromptGlyphs.Interact} to talk";
talkPromptText.gameObject.SetActive(true);
```

becomes

```csharp
InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
```

Every place that does `talkPromptText.gameObject.SetActive(false);` becomes `InteractPromptUI.Clear(this);`.

The `talkPromptText` field stays declared (other scripts may reference it; removing it is a follow-up scene cleanup). It's just never written to anymore.

- [ ] **Step 1: NPCDialogue.cs — find every `talkPromptText.text = ...` and `talkPromptText.gameObject.SetActive(...)`.**

Per the grep run earlier, the writes are at:
- Line 86: `talkPromptText.text = $"Press {PromptGlyphs.Interact} to talk";` — replace whole line with `InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");`
- Line 106: same — same replacement
- Line 165: same — same replacement
- Line 325: same — same replacement

Find every `talkPromptText.gameObject.SetActive(false)` and replace with `InteractPromptUI.Clear(this);`. Do NOT replace `talkPromptText.gameObject.SetActive(true)` — it's the `text =` line right after that triggers Show, so the SetActive(true) call is redundant when Show is called. Delete those lines.

To find every occurrence in this file:
```bash
grep -n "talkPromptText" "Assets/3 - Scripts/NPC_Dialogue/NPCDialogue.cs"
```

For each line:
- `talkPromptText.text = ...;` → `InteractPromptUI.Show(this, ...);`
- `talkPromptText.gameObject.SetActive(true);` → delete (Show handles visibility)
- `talkPromptText.gameObject.SetActive(false);` → `InteractPromptUI.Clear(this);`
- `if (talkPromptText != null)` guard → delete the `null` check, keep only the `Show`/`Clear` call (the static API is null-safe — early-returns if `Instance == null`)
- Lines that *read* `talkPromptText.gameObject.activeSelf` for control flow — replace with a local bool tracking whether we've shown the prompt this enter. Or simpler: just call `Show` again (it's cheap and idempotent).

- [ ] **Step 2: Repeat for the other 6 scripts.**

For each file run `grep -n "talkPromptText" <file>`, apply the same transformation.

| File | Approx line numbers |
|---|---|
| TevDialogue.cs | 92, 134, 136, 157–159, 164 |
| RandomAlienDialogue.cs | 51, 75, 97 |
| GuitarShopNPC.cs | 68, 91, 209 |
| BonfireNPCDialogue.cs | 98, 120 |
| Alien7Vendor.cs | 109, 131 |
| FishMarketNPC.cs | 78, 102, 109, 118–119, 153, 214, 235–236 |

For `FishMarketNPC.cs` the existing string has double spaces (`"Press  {PromptGlyphs.Interact}  to talk"`). Replace with single-space form for consistency: `"Press {PromptGlyphs.Interact} to talk"`. The new pill has its own internal padding; the double spaces were a workaround for the old text.

- [ ] **Step 3: Compile check.**

Console clean.

- [ ] **Step 4: Play-mode test — every NPC.**

Walk to each accessible NPC. Verify the pill shows `[F] to talk` (or the matching prompt) and clears on enter dialogue / walk away. Specifically test:
1. Tev (Alien3 / Humble Abode)
2. Goods vendor (Alien7) — verify pill is hidden once shop UI opens
3. Fish market vendor — same
4. Guitar shop NPC
5. Random alien
6. Bonfire NPC
7. Generic NPCDialogue if any are placed

If a particular NPC isn't reachable in the early-game state, skip it; spot-check at least 3 different scripts.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/NPCDialogue.cs" \
        "Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs" \
        "Assets/3 - Scripts/NPC_Dialogue/RandomAlienDialogue.cs" \
        "Assets/3 - Scripts/NPC_Dialogue/GuitarShopNPC.cs" \
        "Assets/3 - Scripts/NPC_Dialogue/BonfireNPCDialogue.cs" \
        "Assets/3 - Scripts/Vendor/Alien7Vendor.cs" \
        "Assets/3 - Scripts/Fishing/FishMarketNPC.cs"
git commit -m "refactor(prompt): NPC talk prompts route through InteractPromptUI"
```

---

## Task 5: Refactor `BonfireInteraction` cook-panel close hint

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs`

The cook panel currently builds its own close-hint text inside the panel via `MkText(cookPanel.transform, "Press F to close", ...)` — line 414 builds `_closeHintText`, lines 121, 191 update it. Replace with `InteractPromptUI.Show(this, ...)` while the panel is open and `Clear` when it closes. Drop the `_closeHintText` field and the in-panel text.

- [ ] **Step 1: Find the close-hint plumbing.**

```bash
grep -n "_closeHintText\|closeHintText" "Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs"
```

You should see:
- Field declaration (search for `_closeHintText` near the other private fields)
- `_closeHintText = MkText(cookPanel.transform, ...)` at ~line 414 — the build site
- Two updates: lines 119–121 (text update on input source change) and ~line 190 (re-show on panel reopen)

- [ ] **Step 2: Delete the field, the build call, and the update calls.**

- Remove the `_closeHintText` field declaration.
- Remove the `MkText(cookPanel.transform, $"Press  {PromptGlyphs.Interact}  to close", ...)` line from `BuildUI` (line ~414).
- Remove every line that reads or writes `_closeHintText`.

- [ ] **Step 3: Add Show/Clear in OpenCookPanel / CloseCookPanel.**

In `OpenCookPanel` (around line 140) — after the `cookPanel.SetActive(true)` line — add:

```csharp
        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to close");
```

In `CloseCookPanel` (around line 175) — after `cookPanel.SetActive(false)` — add:

```csharp
        InteractPromptUI.Clear(this);
```

If there are other paths that close the panel without going through `CloseCookPanel`, find them and add the `Clear` call there too. (Unlikely — the file uses a single close path.)

- [ ] **Step 4: Compile check.**

Console clean.

- [ ] **Step 5: Play-mode test.**

Walk to a bonfire, press F to open the cook panel. The close-hint inside the panel should be gone; instead, the bottom-center pill shows `[F] to close`. Press F — panel closes, pill clears (the talk pill from Task 4 may re-appear if you're still in the bonfire's NPC trigger zone).

- [ ] **Step 6: Commit.**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs"
git commit -m "refactor(prompt): bonfire close hint via InteractPromptUI"
```

---

## Task 6: Refactor `FishMarketNPC` sell-panel close hint

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishMarketNPC.cs`

Same shape as Task 5. The sell panel builds its own `_closeHintText`. Lines 121 and 411 are the relevant ones (per the earlier grep).

- [ ] **Step 1: Find and delete the close-hint plumbing.**

```bash
grep -n "_closeHintText\|closeHintText" "Assets/3 - Scripts/Fishing/FishMarketNPC.cs"
```

Remove the field, the `MkText(sellPanel.transform, ..., $"Press  {PromptGlyphs.Interact}  to close", ...)` build line at ~411, and every read/write of `_closeHintText`.

- [ ] **Step 2: Add Show/Clear at the open/close call sites.**

Find where the sell panel is opened (search for `sellPanel.SetActive(true)` in the same file) and add immediately after:

```csharp
        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to close");
```

Find where it's closed (`sellPanel.SetActive(false)`) and add:

```csharp
        InteractPromptUI.Clear(this);
```

- [ ] **Step 3: Compile check.**

Console clean.

- [ ] **Step 4: Play-mode test.**

Walk to the fish market. Press F to open the sell panel. The bottom-center pill shows `[F] to close`. Sell or close — pill clears.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/3 - Scripts/Fishing/FishMarketNPC.cs"
git commit -m "refactor(prompt): fish market close hint via InteractPromptUI"
```

---

## Task 7: Generate hotbar icon assets via Unity MCP

**Files:**
- Create: `Assets/2 - Materials/HotbarIcons/water_bottle_icon.png`
- Create: `Assets/2 - Materials/HotbarIcons/fishing_rod_icon.png`
- Create: `Assets/2 - Materials/HotbarIcons/axe_icon.png`
- Create: `Assets/2 - Materials/HotbarIcons/pistol_icon.png`
- Create: `Assets/2 - Materials/HotbarIcons/guitar_icon.png`

Use the Coplay Unity MCP `mcp__coplay-mcp__generate_or_edit_images` tool. Unity Editor must be open to the project for the MCP server to talk to it.

- [ ] **Step 1: Create the destination folder.**

```bash
mkdir -p "Assets/2 - Materials/HotbarIcons"
```

- [ ] **Step 2: Generate the 5 icons.**

Load the tool schema first via `ToolSearch` with `select:mcp__coplay-mcp__generate_or_edit_images`. The tool generates an image and saves it to a Unity-relative asset path.

Shared style brief — use this verbatim for every icon, only swap the subject:

> Flat monochrome cyan-on-transparent UI icon, 256×256 PNG, sci-fi HUD aesthetic, single colour fill `#5CC8FF`, no gradients, no outline tricks, clean vector look, centred in frame with 12 % padding. Subject: **{SUBJECT}**.

| File | Subject |
|---|---|
| `water_bottle_icon.png` | a tall drinking-flask silhouette with a screw cap |
| `fishing_rod_icon.png` | an angled fishing rod with a hanging line and small hook |
| `axe_icon.png` | a short-handled hatchet, blade left, handle right |
| `pistol_icon.png` | a side-view semi-auto pistol silhouette, barrel pointing right |
| `guitar_icon.png` | a front-view acoustic guitar body with a sound hole |

For each: invoke `mcp__coplay-mcp__generate_or_edit_images` with the prompt above (substituting the subject) and save path `Assets/2 - Materials/HotbarIcons/{filename}`.

If any output looks off-style (e.g., gradients, multiple colors, off-centre), regenerate that one with a tweaked prompt (e.g., "single solid colour fill, no shading"). Don't keep going with mismatched icons — visual consistency across the 5 is the whole point.

- [ ] **Step 3: Configure import settings.**

For each generated PNG, focus the Unity Editor → Project window → click the asset → in Inspector:
- Texture Type: `Sprite (2D and UI)`
- Sprite Mode: `Single`
- Pixels Per Unit: `100` (default)
- Filter Mode: `Bilinear`
- Compression: `None` (icons are small; preserve crisp edges)
- Click `Apply`.

- [ ] **Step 4: Visual sanity check.**

Drag each sprite onto a test `Image` (or open Sprite Editor preview). Confirm: cyan, transparent background, properly centered, no random artifacts.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/2 - Materials/HotbarIcons"
git commit -m "feat(hotbar): add 5 generated cyan icon sprites"
```

---

## Task 8: Add `hotbarIcon` field to 5 controllers

**Files:**
- Modify: `Assets/3 - Scripts/Pickups/WaterBottleController.cs`
- Modify: `Assets/3 - Scripts/Fishing/FishingRodController.cs`
- Modify: `Assets/3 - Scripts/NPC_Dialogue/GuitarController.cs`
- Modify: `Assets/3 - Scripts/Pickups/AxeController.cs`
- Modify: `Assets/3 - Scripts/Pickups/PistolController.cs`

A field per controller. Inspector wires the icon to each controller on the Player prefab. The Hotbar will read `controller.hotbarIcon` in Task 9.

- [ ] **Step 1: WaterBottleController.cs — add field.**

Find the existing `[Header(...)]` block at the top of the class (typically the first `[Header(...) public GameObject xxxPrefab;` line). Insert immediately above the first existing `[Header(...)]`:

```csharp
    [Header("UI")]
    [Tooltip("Icon shown in the hotbar slot when this item is in the bar. Assign on the Player prefab.")]
    public Sprite hotbarIcon;

```

- [ ] **Step 2: Repeat for the other 4 controllers.**

Same exact insertion in:
- `Assets/3 - Scripts/Fishing/FishingRodController.cs`
- `Assets/3 - Scripts/NPC_Dialogue/GuitarController.cs`
- `Assets/3 - Scripts/Pickups/AxeController.cs`
- `Assets/3 - Scripts/Pickups/PistolController.cs`

- [ ] **Step 3: Compile check.**

Console clean.

- [ ] **Step 4: Wire the icons in the Player prefab.**

Open the Player prefab (find via `Assets/1 - samsPrefabs/` or wherever the Player root prefab lives — search the Project window for "Player" if unsure). Select the Player root in the prefab edit mode.

For each of the 5 controller components on the Player root:
1. Find the new `Hotbar Icon` field under the `UI` header.
2. Drag the matching sprite from `Assets/2 - Materials/HotbarIcons/` into the field:
   - WaterBottleController ← `water_bottle_icon`
   - FishingRodController ← `fishing_rod_icon`
   - GuitarController ← `guitar_icon`
   - AxeController ← `axe_icon`
   - PistolController ← `pistol_icon`
3. Save the prefab.

If the prefab edit doesn't stick (Unity is sometimes finicky about prefab overrides), try editing the live scene instance and pressing `Apply All` in the prefab override drop-down.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/3 - Scripts/Pickups/WaterBottleController.cs" \
        "Assets/3 - Scripts/Fishing/FishingRodController.cs" \
        "Assets/3 - Scripts/NPC_Dialogue/GuitarController.cs" \
        "Assets/3 - Scripts/Pickups/AxeController.cs" \
        "Assets/3 - Scripts/Pickups/PistolController.cs"

# Also commit the prefab if it was modified — git status will show it.
git status
# git add "<player prefab path>"  if shown
git commit -m "feat(hotbar): add hotbarIcon field to 5 equippable controllers"
```

---

## Task 9: Hotbar visual revamp — slot size + icon swap

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

This task changes slot sizes from 84→64 and replaces the centered text label with a centered Image bound to the controller's `hotbarIcon`. No animation yet — that's Task 10.

- [ ] **Step 1: Change slot constants.**

Near the top of `Hotbar.cs`, change:

```csharp
    const float SlotSize = 84f;
    const float SlotSpacing = 12f;
    const float BottomMargin = 36f;
```

to:

```csharp
    const float SlotSize = 64f;
    const float ActiveSize = 80f;       // size when slot is the equipped/cursor active slot
    const float ActiveLift = 8f;        // pixels lifted above the row when active
    const float SlotSpacing = 14f;
    const float BottomMargin = 36f;
```

- [ ] **Step 2: Add icon field to the registry `Entry` and `SlotVisuals`.**

Find the `sealed class Entry` block. Add a `Sprite Icon` field:

```csharp
        public Sprite Icon;                    // sprite from controller.hotbarIcon
```

Find the `class SlotVisuals` block. Replace `TextMeshProUGUI itemLabel` with:

```csharp
        public Image itemIcon;
```

- [ ] **Step 3: Update `BuildRegistry` to copy the icon.**

In every `new Entry { ... }` line, add `Icon = water?.hotbarIcon` / `rod?.hotbarIcon` / `guitar?.hotbarIcon` / `axe?.hotbarIcon` / `pistol?.hotbarIcon` to match each controller. Example for water:

```csharp
            new Entry { Id = ItemId.WaterBottle, DisplayName = "WATER",  Controller = water,
                        Icon = water != null ? water.hotbarIcon : null,
                        IsUnlocked   = () => water  != null && water.IsUnlocked,
                        // ... rest unchanged
            },
```

- [ ] **Step 4: Replace `itemLabel` build with `itemIcon`.**

In `BuildSlot` find the block that builds `itemLabel` (search for `__ItemLabel`). Replace the entire block with:

```csharp
        var iconRT = NewRT("__ItemIcon", slotRT);
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = new Vector2(40f, 40f);
        v.itemIcon = iconRT.gameObject.AddComponent<Image>();
        v.itemIcon.preserveAspect = true;
        v.itemIcon.raycastTarget = false;
        v.itemIcon.color = new Color32(0xF1, 0xF4, 0xFF, 0xC0);
```

Move the `__KeyLabel` block to anchor top-right (was top-left in original). Replace its anchor/pivot/anchoredPosition lines with:

```csharp
        keyRT.anchorMin = new Vector2(1f, 1f);
        keyRT.anchorMax = new Vector2(1f, 1f);
        keyRT.pivot = new Vector2(1f, 1f);
        keyRT.anchoredPosition = new Vector2(-7f, -5f);
        keyRT.sizeDelta = new Vector2(20f, 18f);
```

And change the alignment line from `TopLeft` to `TopRight`:

```csharp
        v.keyLabel.alignment = TextAlignmentOptions.TopRight;
```

Reduce key label size:
```csharp
        v.keyLabel.fontSize = 14f;
```

- [ ] **Step 5: Update `Refresh` to drive the icon.**

Find the `Refresh(bool dimmed)` method. Replace the per-slot block (everything inside the `for (int i = 0; i < NumSlots; i++)` loop) with:

```csharp
        for (int i = 0; i < NumSlots; i++)
        {
            var v = slotViews[i];
            ItemId id = slots[i];
            bool empty = id == ItemId.None;
            bool active = (equipped != ItemId.None)
                ? (!empty && id == equipped)
                : (i == _cycleCursor);

            // Icon — null sprite means empty / no icon assigned.
            Sprite sprite = null;
            if (!empty && _registry != null)
            {
                for (int r = 0; r < _registry.Length; r++)
                    if (_registry[r].Id == id) { sprite = _registry[r].Icon; break; }
            }
            v.itemIcon.sprite = sprite;
            v.itemIcon.enabled = sprite != null;

            // Dim non-active filled slots; lighter dim on empty slots.
            if (active)
            {
                v.itemIcon.color = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
                v.background.color = new Color32(0x14, 0x28, 0x44, 0xF8);
            }
            else
            {
                v.itemIcon.color = empty
                    ? new Color32(0xF1, 0xF4, 0xFF, 0x00)  // hide icon (none anyway)
                    : new Color32(0xF1, 0xF4, 0xFF, 0x80); // dim
                v.background.color = empty
                    ? new Color32(0x05, 0x03, 0x12, 0xC0)
                    : GalaxyHudKit.SlotColor;
            }

            v.glow.gameObject.SetActive(active);
            v.accent.color = new Color(1f, 1f, 1f, active ? 0.9f : 0.35f);
        }
```

- [ ] **Step 6: Recompute total bar width.**

The bar width is set in `BuildUI()`:

```csharp
        float totalWidth = NumSlots * SlotSize + (NumSlots - 1) * SlotSpacing;
        ...
        bar.sizeDelta = new Vector2(totalWidth + 32f, SlotSize + 32f);
```

Bar height needs to accommodate the lifted active slot — change to `ActiveSize + ActiveLift + 32f`:

```csharp
        bar.sizeDelta = new Vector2(totalWidth + 32f, ActiveSize + ActiveLift + 32f);
```

- [ ] **Step 7: Compile check.**

Console clean. There may be a warning about `itemLabel` being unused in `SlotVisuals` if any reference snuck through — search for `itemLabel` in Hotbar.cs and remove any leftover usage.

- [ ] **Step 8: Play-mode test.**

Pick up the axe (chop a tree or get it from the bonfire NPC depending on how the game state lays out). The hotbar at the bottom-center should show the axe's cyan icon in slot 3 (or wherever it lands). Press 1–5 — the active slot should highlight with brighter background + glow but still be the same size as resting (Task 10 adds the lift/scale).

- [ ] **Step 9: Commit.**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): swap text labels for icons; smaller slots"
```

---

## Task 10: Hotbar active-slot lift/scale + floating name plate

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs`

The active slot grows from 64×64 to 80×80 and lifts +8 px. A beveled name plate floats above it showing the item name. Both fade/animate over ~120 ms.

This task introduces the visual bridge between the rounded hotbar and the beveled prompt pill (the name plate is beveled, matching the prompt).

- [ ] **Step 1: Add tracking field for previous active slot.**

Near the top of the class, add:

```csharp
    int _animatedActiveIdx = -1;     // last-frame's active index, used to fire animations only on change
    Coroutine[] _slotAnimRoutines = new Coroutine[NumSlots];
```

- [ ] **Step 2: Build the floating name plate in `BuildUI`.**

After the `for (int i = 0; i < NumSlots; i++)` loop in `BuildUI`, add:

```csharp
        BuildNamePlate(bar);
```

Add the new method below `BuildUI`:

```csharp
    RectTransform _namePlateRT;
    Image _namePlateBg;
    Image _namePlateBorder;
    TextMeshProUGUI _namePlateText;
    CanvasGroup _namePlateGroup;

    void BuildNamePlate(RectTransform parent)
    {
        var rt = NewRT("__NamePlate", parent);
        rt.anchorMin = new Vector2(0f, 0f);     // x positioned manually each Refresh
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, ActiveSize + ActiveLift + 8f);
        rt.sizeDelta = new Vector2(0f, 0f);
        _namePlateRT = rt;

        _namePlateGroup = rt.gameObject.AddComponent<CanvasGroup>();
        _namePlateGroup.alpha = 0f;

        var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var pill = NewRT("Pill", rt);
        pill.anchorMin = new Vector2(0f, 0f);
        pill.anchorMax = new Vector2(1f, 0f);
        pill.pivot = new Vector2(0.5f, 0f);

        _namePlateBg = pill.gameObject.AddComponent<Image>();
        // Reuse InteractPromptUI's beveled sprite via a static sprite getter.
        // To avoid coupling we duplicate the procedural-sprite call locally:
        _namePlateBg.sprite = HotbarBeveledPanel.GetSprite();
        _namePlateBg.type = Image.Type.Sliced;
        _namePlateBg.color = new Color32(0x0A, 0x18, 0x28, 0xEB);
        _namePlateBg.raycastTarget = false;

        var ledRT = NewRT("Led", pill);
        ledRT.anchorMin = new Vector2(0f, 0f);
        ledRT.anchorMax = new Vector2(0f, 1f);
        ledRT.pivot = new Vector2(0f, 0.5f);
        ledRT.anchoredPosition = new Vector2(7f, 0f);
        ledRT.sizeDelta = new Vector2(2f, -10f);
        var ledImg = ledRT.gameObject.AddComponent<Image>();
        ledImg.color = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
        ledImg.raycastTarget = false;

        var border = NewRT("Border", pill);
        Stretch(border, 0f, 0f, 0f, 0f);
        _namePlateBorder = border.gameObject.AddComponent<Image>();
        _namePlateBorder.sprite = HotbarBeveledPanel.GetOutlineSprite();
        _namePlateBorder.type = Image.Type.Sliced;
        _namePlateBorder.color = new Color32(0x78, 0xC8, 0xFF, 0x73);
        _namePlateBorder.raycastTarget = false;

        var pillVlg = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        pillVlg.childAlignment = TextAnchor.MiddleCenter;
        pillVlg.padding = new RectOffset(18, 14, 6, 6);

        var pillFitter = pill.gameObject.AddComponent<ContentSizeFitter>();
        pillFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        pillFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var nameTxt = new GameObject("Name", typeof(RectTransform));
        nameTxt.transform.SetParent(pill, false);
        _namePlateText = nameTxt.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(_namePlateText);
        _namePlateText.text = "";
        _namePlateText.fontSize = 12f;
        _namePlateText.fontStyle = FontStyles.Bold;
        _namePlateText.alignment = TextAlignmentOptions.MidlineCenter;
        _namePlateText.characterSpacing = 3f;
        _namePlateText.color = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
        _namePlateText.enableWordWrapping = false;
        _namePlateText.raycastTarget = false;
    }
```

- [ ] **Step 3: Add the name-plate sprite helper class.**

At the bottom of `Hotbar.cs`, after the `Hotbar` class, add a small helper class so we don't sprinkle procedural-sprite code throughout:

```csharp
static class HotbarBeveledPanel
{
    static Sprite _panel, _outline;

    public static Sprite GetSprite()
    {
        if (_panel != null) return _panel;
        var tex = MakePanel(64, 14);
        _panel = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                               100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        _panel.name = "HotbarBeveledPanel";
        return _panel;
    }

    public static Sprite GetOutlineSprite()
    {
        if (_outline != null) return _outline;
        var tex = MakeOutline(64, 14, 2);
        _outline = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                 100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        _outline.name = "HotbarBeveledOutline";
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

- [ ] **Step 4: Add a `Stretch` helper that takes offsets** (already exists on the class — verify).

Search for `void Stretch(`. If the existing one only takes `(RectTransform rt, float left, float bottom, float right, float top)`, that's what's needed and the call above is correct. No change needed.

- [ ] **Step 5: Add the slot animation coroutine.**

Add to `Hotbar`:

```csharp
    IEnumerator AnimateSlotState(int idx, bool active)
    {
        var v = slotViews[idx];
        if (v == null) yield break;
        float dur = 0.12f;
        float t = 0f;
        Vector2 fromSize = v.root.sizeDelta;
        Vector2 toSize = active ? new Vector2(ActiveSize, ActiveSize) : new Vector2(SlotSize, SlotSize);
        Vector2 fromPos = v.root.anchoredPosition;
        // Existing slot Y baseline is +16 (per BuildSlot); active adds ActiveLift on top of that.
        float baselineY = 16f;
        Vector2 toPos = new Vector2(fromPos.x, active ? baselineY + ActiveLift : baselineY);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = 1f - Mathf.Pow(1f - u, 3f);
            v.root.sizeDelta = Vector2.Lerp(fromSize, toSize, k);
            v.root.anchoredPosition = Vector2.Lerp(fromPos, toPos, k);
            yield return null;
        }
        v.root.sizeDelta = toSize;
        v.root.anchoredPosition = toPos;
        _slotAnimRoutines[idx] = null;
    }
```

- [ ] **Step 6: Trigger slot animation + name-plate update from `Refresh`.**

At the end of the `Refresh` method (after the existing for loop), add:

```csharp
        // Slot lift/scale animation — only fire on active-index change.
        int newActive = -1;
        for (int i = 0; i < NumSlots; i++)
        {
            var v = slotViews[i];
            ItemId id = slots[i];
            bool empty = id == ItemId.None;
            bool active = (equipped != ItemId.None) ? (!empty && id == equipped) : (i == _cycleCursor);
            if (active) newActive = i;
        }
        if (newActive != _animatedActiveIdx)
        {
            // Animate the previous-active slot back to rest.
            if (_animatedActiveIdx >= 0 && _animatedActiveIdx < NumSlots)
            {
                if (_slotAnimRoutines[_animatedActiveIdx] != null) StopCoroutine(_slotAnimRoutines[_animatedActiveIdx]);
                _slotAnimRoutines[_animatedActiveIdx] = StartCoroutine(AnimateSlotState(_animatedActiveIdx, false));
            }
            // Animate the new-active slot up.
            if (newActive >= 0)
            {
                if (_slotAnimRoutines[newActive] != null) StopCoroutine(_slotAnimRoutines[newActive]);
                _slotAnimRoutines[newActive] = StartCoroutine(AnimateSlotState(newActive, true));
            }
            _animatedActiveIdx = newActive;
        }

        // Name plate — show only when an active filled slot exists.
        ItemId activeId = (newActive >= 0) ? slots[newActive] : ItemId.None;
        bool plateShown = activeId != ItemId.None;
        if (plateShown && _namePlateRT != null)
        {
            _namePlateText.text = ItemName(activeId);
            // Position above the active slot.
            float slotX = slotViews[newActive].root.anchoredPosition.x;
            // Anchored at bar's bottom-left corner; the slot's anchoredPosition.x is
            // measured from the bar centre via the BuildSlot maths, so re-apply
            // the same offset: bar centre is at sizeDelta.x * 0.5 from the
            // bottom-left anchor. For simplicity, match the slot's anchoredPosition
            // directly — the name plate's anchor matches.
            var p = _namePlateRT.anchoredPosition;
            // Convert slot's centre-anchored x into our bottom-left-anchored space.
            // BarCentre = sizeDelta.x / 2, so slot's bottom-left X = barWidth/2 + slotX
            float barWidth = ((RectTransform)_namePlateRT.parent).sizeDelta.x;
            p.x = barWidth * 0.5f + slotX;
            _namePlateRT.anchoredPosition = p;
        }
        if (_namePlateGroup != null)
        {
            // Lerp alpha; cheap one-line lerp is fine, no coroutine needed here.
            float target = plateShown ? 1f : 0f;
            _namePlateGroup.alpha = Mathf.MoveTowards(_namePlateGroup.alpha, target, Time.unscaledDeltaTime * 8f);
        }
```

- [ ] **Step 7: Compile check.**

Console clean.

- [ ] **Step 8: Play-mode test.**

In Play mode:
1. Pick up the axe → press `3` to equip → slot 3 should grow + lift, name plate fades in showing `AXE`.
2. Press `4` (pistol unlocked) → slot 3 returns to rest, slot 4 lifts, name plate slides over to slot 4 showing `PISTOL`.
3. Press `3` again to toggle off → all slots flat, name plate fades out.
4. D-pad / scroll through slots — cycle cursor visually marches across, name plate follows when landing on a filled slot, hides when on empty.

- [ ] **Step 9: Commit.**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): active-slot lift/scale animation + floating name plate"
```

---

## Task 11: Verification playthrough

**Files:** none modified

A 10-minute integration check across the whole feature.

- [ ] **Step 1: Walk-through every prompt.**

In Play mode in the `1.6.7.7.7` scene, hit each Interactable category:
1. Hatch buttons (open + close hatch)
2. NPC talk (Tev, fish vendor, goods vendor, bonfire NPC, guitar NPC if reachable)
3. Pickups (water bottle, fishing rod, note, mushroom)
4. Cook panel open/close hint
5. Sell panel open/close hint
6. Cassette player ("Press F to insert cassette")

Every prompt should be the **same** beveled cyan pill at bottom-center with a `[F]` keycap. Not in different positions, not in different sizes. Walk away — pill slides down. Walk back — pill slides up.

- [ ] **Step 2: Switch to controller, retest one prompt.**

Plug in a controller (or hit a controller button to engage `ControllerEnabled`). The keycap should swap from `[F]` to `[X]` automatically (PromptGlyphs handles this). Verify on at least one NPC + one pickup.

- [ ] **Step 3: Hotbar smoke test.**

Pick up everything you can in the early game (water bottle, axe, pistol, fishing rod, guitar). The hotbar should populate left-to-right. Press 1–5 to equip each — the active slot lifts/scales every time. Name plate updates per equip.

Toggle the same key twice to unequip — slot returns to rest, name plate fades.

- [ ] **Step 4: Save / load round-trip.**

Open the pause menu (`Esc`) → Save game → name it `prompt-hotbar-test`. Return to main menu. Click PLAY → load `prompt-hotbar-test`. The hotbar should restore the same items + equipped state. Walk to an NPC; the prompt still works.

- [ ] **Step 5: Pause-menu / save-menu sortingOrder check.**

While the cook panel is open and the prompt pill is showing `[F] to close`, press `Esc` to open the pause menu (sortingOrder=1000). The pause menu should fully cover the prompt (which is at 200). Close the pause menu — prompt is visible again. If the prompt accidentally shows on top of the pause menu, that's a sortingOrder regression — bump pause menu canvas above 200 OR drop InteractPromptUI below 0 (the spec says 200 is correct, so this should pass).

- [ ] **Step 6: No commit.**

Verification only — nothing to commit. If any check fails, file a follow-up task before claiming done.

---

## Self-review

Reviewed the plan against the spec — every section is covered:

- **Spec Part 1 (InteractPromptUI):** Tasks 1, 2, 3 build the singleton + delegation + main-menu seeding.
- **Spec Part 1 (NPC refactors):** Task 4 covers all 7 NPC scripts.
- **Spec Part 1 (close hint refactors):** Tasks 5, 6 cover BonfireInteraction + FishMarketNPC.
- **Spec Part 2 (icons):** Task 7 generates the 5 sprites.
- **Spec Part 2 (controller fields):** Task 8 adds `hotbarIcon` to all 5 controllers.
- **Spec Part 2 (hotbar visuals):** Task 9 swaps text for icons + resizes; Task 10 adds the lift/scale animation + floating name plate.
- **Spec verification:** Task 11 hits the cross-cutting checks.

No placeholders. Method names consistent across tasks (`Show`/`Clear`/`ShowOneShot` everywhere). The `_animatedActiveIdx` field name is consistent. The `HotbarBeveledPanel.GetSprite/GetOutlineSprite` helper class is defined in Task 10 step 3, used in Task 10 step 2.

One scope note: the spec mentioned moving `MakeBeveledPanelTexture` and `DecorateKeyGlyphs` to a shared util; the plan instead **copies** them into both `InteractPromptUI` and `HotbarBeveledPanel` (the latter for just the panel, not the keycap decoration). Spec explicitly says "copy/extract; not worth a refactor for one extra caller" — this matches.
