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

    /// <summary>True while a "Press [F] …" prompt is on screen. Read by
    /// CrosshairReticle to morph the center reticle into its lock-on state.
    /// Tracks the logical shown/hidden state (flips at the start of the
    /// slide-in / slide-out), not the animation midpoint.</summary>
    public static bool IsPromptVisible { get; private set; }

    [Tooltip("Seconds for the slide-in / slide-out animation.")]
    public float slideDuration = 0.25f;
    [Tooltip("Pixels the pill slides up from when first revealed.")]
    public float slideOffset = 40f;
    [Tooltip("Vertical anchor — pixels above the bottom edge of the screen at rest. Default 260 keeps the prompt above the WaterFillHUD pill (at y=180).")]
    public float bottomMargin = 260f;
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
    bool _stickyOwner;          // true if Show(owner, ...) set a sticky owner; false for ShowOneShot.
    UnityEngine.Object _owner;
    string _ownerText;          // latest text for the sticky owner; applied by Update when looked-at.
    string _lastAppliedText;    // guards per-frame text rebuilds while shown.

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
        if (Instance == this) { Instance = null; IsPromptVisible = false; }
    }

    void Update()
    {
        // Auto-hide if the sticky owner was destroyed without calling Clear.
        // Common case: a pickup destroys itself in Interact() — Interactable.Update
        // re-asserts the prompt one last time AFTER Interact returns, leaving
        // _owner pointing at a now-destroyed object that nothing will Clear.
        if (_stickyOwner && _owner == null)
        {
            _stickyOwner = false;
            HideInternal();
            return;
        }

        // Suppress the floating prompt whenever a modal UI is up (NPC dialogue,
        // cook panel, vendor shops — all set PlayerController.isInDialogue).
        if (PlayerController.isInDialogue)
        {
            if (_shown) HideInternal();
            return;
        }

        // Continuous gaze gate (#1): the gate is evaluated here every frame on
        // the current owner — NOT inside Show() — so it works regardless of how
        // often an owner re-asserts Show (e.g. ShipReactor calls it only once).
        // Looked-at → (re)show with the owner's latest text; looked-away → hide
        // but KEEP ownership so it reappears the moment the crosshair returns.
        if (_stickyOwner && _owner != null)
        {
            if (InteractGaze.IsLookingAt(_owner))
            {
                if (!_shown || _ownerText != _lastAppliedText)
                {
                    _lastAppliedText = _ownerText;
                    ShowInternal(_ownerText);
                }
            }
            else if (_shown)
            {
                HideInternal();
            }
        }
    }

    Vector2 RestPos()      => new Vector2(0f, bottomMargin);
    Vector2 OffScreenPos() => new Vector2(0f, bottomMargin - slideOffset);

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Sticky prompt; stays until <c>Clear(owner)</c> with the same owner.</summary>
    public static void Show(UnityEngine.Object owner, string text)
    {
        if (Instance == null) return;
        var inst = Instance;

        // Claim ownership with a look-to-select preference: a new candidate only
        // takes the prompt from the current owner if we're not already looking at
        // the current owner (or we ARE looking at the newcomer). The actual
        // show/hide + gaze gating happens continuously in Update().
        if (owner != inst._owner)
        {
            bool take = inst._owner == null
                     || InteractGaze.IsLookingAt(owner)
                     || !InteractGaze.IsLookingAt(inst._owner);
            if (!take) return;
            inst._owner = owner;
        }
        inst._stickyOwner = true;
        inst._ownerText = text;
    }

    /// <summary>Clears iff <paramref name="owner"/> matches the current owner. Idempotent.</summary>
    public static void Clear(UnityEngine.Object owner)
    {
        if (Instance == null) return;
        if (Instance._owner != owner) return;
        Instance._owner = null;
        Instance._stickyOwner = false;
        Instance._ownerText = null;
        Instance._lastAppliedText = null;
        Instance.HideInternal();
    }

    /// <summary>Legacy: 3 s self-clearing prompt. Used by GameUI.DisplayInteractionInfo.</summary>
    public static void ShowOneShot(string text, float seconds = 3f)
    {
        if (Instance == null) return;
        Instance._owner = null;
        Instance._stickyOwner = false;
        Instance.ShowInternal(text);
        if (Instance._oneShotRoutine != null) Instance.StopCoroutine(Instance._oneShotRoutine);
        Instance._oneShotRoutine = Instance.StartCoroutine(Instance.OneShotRoutine(seconds));
    }

    void ShowInternal(string text)
    {
        // Drop Show calls while a modal UI owns the screen. See the matching
        // note in Update() — without this, an Interactable in range whose
        // Update() re-asserts "Press F" each frame would override the cook
        // panel's Clear(this) and the prompt would keep pulsing in.
        if (PlayerController.isInDialogue) return;
        if (_bodyText != null) _bodyText.text = DecorateKeyGlyphs(text ?? "");
        if (_shown) return;
        _shown = true;
        IsPromptVisible = true;
        if (_slideRoutine != null) StopCoroutine(_slideRoutine);
        _slideRoutine = StartCoroutine(SlideRoutine(true));
    }

    void HideInternal()
    {
        if (!_shown) return;
        _shown = false;
        IsPromptVisible = false;
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
        HUDSceneGate.Register(canvas);
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
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        return t;
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
            if (result.IndexOf(needle, System.StringComparison.Ordinal) < 0) continue;
            string replacement =
                "<color=#5CC8FF><size=115%>[</size><b>" + label + "</b><size=115%>]</size></color>";
            result = result.Replace(needle, replacement);
        }
        return result;
    }
}
