using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Top-center killstreak popup. Subscribes to KillstreakManager's events and
/// swaps a single Image's sprite to the pre-baked PNG for the active tier
/// (×2 DOUBLE KILL … ×11+ WICKED SICK cap). PNGs live in
/// Resources/Killstreak/tier_N.png and were rendered by Edge headless from
/// .superpowers/.../single-tier.html — we tried doing the cyan-halo + skewed
/// Impact lettering via TMP shader properties first but the SDF UNDERLAY +
/// FaceDilate couldn't reproduce the mockup's blurred glow cleanly. Baked
/// PNGs are the reliable path. The decay bar underneath stays as a dynamic
/// UI Image driven by KillstreakManager.DecayProgress01. All animations use
/// unscaledDeltaTime so they keep playing during the slow-mo dip.
/// </summary>
public class KillstreakHUD : MonoBehaviour
{
    public static KillstreakHUD Instance { get; private set; }

    // ── palette (decay bar) ───────────────────────────────────────────
    // Cap-tier tint (white text + red halo) is baked into tier_11.png.
    static readonly Color CyanGlow = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.10f);

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _root;
    Image _tierImage;         // shows the pre-baked PNG for the current tier
    Sprite[] _tierSprites;    // indexed by streak count; idx 0/1 unused (no popup for solo)
    RectTransform _barFillRT;

    bool _visible;
    bool _subscribed;
    Coroutine _visRoutine;
    Coroutine _punchRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("KillstreakHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<KillstreakHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        ImmediateHide();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var mgr = KillstreakManager.Instance;
        if (mgr != null)
        {
            mgr.OnKillRegistered -= HandleKill;
            mgr.OnStreakBroken   -= HandleBreak;
        }
    }

    void Update()
    {
        if (!_subscribed && KillstreakManager.Instance != null)
        {
            KillstreakManager.Instance.OnKillRegistered += HandleKill;
            KillstreakManager.Instance.OnStreakBroken   += HandleBreak;
            _subscribed = true;
        }

        if (_visible && _barFillRT != null && KillstreakManager.Instance != null)
            _barFillRT.localScale = new Vector3(KillstreakManager.Instance.DecayProgress01, 1f, 1f);
    }

    void HandleKill(int streak)
    {
        if (streak < 2) return; // no popup for solo kills
        ApplyTier(streak);
        if (!_visible)
        {
            StartVisibility(true);
        }
        else
        {
            if (_punchRoutine != null) StopCoroutine(_punchRoutine);
            _punchRoutine = StartCoroutine(PunchPulse());
        }
    }

    void HandleBreak()
    {
        if (!_visible) return;
        StartVisibility(false);
    }

    void ApplyTier(int streak)
    {
        int idx = Mathf.Clamp(streak, 2, _tierSprites.Length - 1);
        if (_tierImage != null && _tierSprites != null && _tierSprites[idx] != null)
            _tierImage.sprite = _tierSprites[idx];
    }

    void StartVisibility(bool show)
    {
        _visible = show;
        if (_visRoutine != null) StopCoroutine(_visRoutine);
        _visRoutine = StartCoroutine(VisibilityRoutine(show));
    }

    void ImmediateHide()
    {
        _visible = false;
        _group.alpha = 0f;
        _root.localScale = Vector3.one * 0.6f;
    }

    IEnumerator VisibilityRoutine(bool show)
    {
        float dur = show ? 0.25f : 0.4f;
        float fromAlpha = _group.alpha;
        float toAlpha   = show ? 1f : 0f;
        Vector3 fromScale = _root.localScale;
        Vector3 toScale   = show ? Vector3.one : Vector3.one * 0.85f;

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = show ? EaseOutBack(u) : u;
            _group.alpha = Mathf.Lerp(fromAlpha, toAlpha, u);
            _root.localScale = Vector3.Lerp(fromScale, toScale, k);
            yield return null;
        }
        _group.alpha = toAlpha;
        _root.localScale = toScale;
    }

    IEnumerator PunchPulse()
    {
        const float dur = 0.15f;
        const float peak = 1.08f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = u < 0.5f ? Mathf.Lerp(1f, peak, u * 2f)
                               : Mathf.Lerp(peak, 1f, (u - 0.5f) * 2f);
            _root.localScale = Vector3.one * k;
            yield return null;
        }
        _root.localScale = Vector3.one;
        _punchRoutine = null;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ── canvas build ──────────────────────────────────────────────────

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 830; // above LetterboxBars (820), with the other gameplay HUDs
        HUDSceneGate.Register(_canvas);

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Root anchored to the top-center of the screen, ~140 px down so it
        // sits under the compass (the compass canvas is sortingOrder 300 and
        // takes the top ~120 px of the screen).
        _root = NewUI("Root", transform);
        _root.anchorMin = new Vector2(0.5f, 1f);
        _root.anchorMax = new Vector2(0.5f, 1f);
        _root.pivot     = new Vector2(0.5f, 1f);
        _root.anchoredPosition = new Vector2(0f, -140f);
        var vlg = _root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        // Negative spacing pulls the decay bar UP into the tier PNG's bottom
        // transparent padding so the timer reads closer to the popup. The
        // image's content area (text + glow + streak line) is well above
        // this overlap, so the bar never visually crowds the popup itself.
        vlg.spacing = -56f;
        var fitter = _root.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Pre-baked tier image (mult + skewed Impact name + cyan halo +
        // streak line all rendered at HTML/Edge in single-tier.html, exported
        // as PNG, loaded from Resources/Killstreak/). One Image swaps sprite
        // per tier — TMP's runtime shader settings couldn't reproduce the
        // mockup's blurred halo / Impact weight cleanly, baked PNGs are the
        // reliable path.
        _tierSprites = LoadTierSprites();
        var imageRT = NewUI("TierImage", _root);
        var imageLE = imageRT.gameObject.AddComponent<LayoutElement>();
        // 630×273 = 1.5× the previous 420×182 baseline. VerticalLayoutGroup's
        // 4 px spacing keeps the decay bar below the image regardless of size.
        imageLE.preferredWidth  = 630f;
        imageLE.preferredHeight = 273f;
        _tierImage = imageRT.gameObject.AddComponent<Image>();
        _tierImage.raycastTarget = false;
        _tierImage.preserveAspect = true;
        // Default to tier 2 sprite so the popup has something to show before
        // ApplyTier runs (ImmediateHide makes it invisible anyway).
        if (_tierSprites != null && _tierSprites.Length > 2 && _tierSprites[2] != null)
            _tierImage.sprite = _tierSprites[2];

        // Decay bar (track + fill driven by KillstreakManager.DecayProgress01).
        var barRT = NewUI("DecayBar", _root);
        var barLE = barRT.gameObject.AddComponent<LayoutElement>();
        barLE.preferredWidth  = 240f;
        barLE.preferredHeight = 3f;
        var barBg = barRT.gameObject.AddComponent<Image>();
        barBg.color = BarTrack;
        barBg.raycastTarget = false;

        _barFillRT = NewUI("Fill", barRT);
        _barFillRT.anchorMin = Vector2.zero;
        _barFillRT.anchorMax = Vector2.one;
        _barFillRT.pivot     = new Vector2(0f, 0.5f);
        _barFillRT.offsetMin = Vector2.zero;
        _barFillRT.offsetMax = Vector2.zero;
        var fillImg = _barFillRT.gameObject.AddComponent<Image>();
        fillImg.color = CyanGlow;
        fillImg.raycastTarget = false;
    }

    /// <summary>
    /// Loads the 10 pre-baked tier PNGs from Resources/Killstreak/. Each is a
    /// composite (multiplier + skewed Impact name + cyan halo + streak line)
    /// rendered by Edge headless from .superpowers/.../single-tier.html and
    /// exported into the project. Loaded as Texture2D and wrapped in Sprites
    /// so the import settings don't have to be set to Sprite mode in the
    /// inspector. Indexed by streak count (idx 0/1 unused — solo kill = no
    /// popup; idx 11+ uses the WICKED SICK sprite).
    /// </summary>
    static Sprite[] LoadTierSprites()
    {
        const int firstTier = 2;
        const int lastTier  = 11;
        var sprites = new Sprite[lastTier + 1];
        for (int t = firstTier; t <= lastTier; t++)
        {
            var tex = Resources.Load<Texture2D>("Killstreak/tier_" + t);
            if (tex == null) continue;
            sprites[t] = Sprite.Create(tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f);
        }
        return sprites;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

}
