using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Black-fullscreen loading overlay shown between the main menu and the
/// gameplay scene. Replaces the synchronous <c>SceneManager.LoadScene</c>
/// freeze-then-stutter on entry — instead the player gets an instant
/// transition to a progress bar while the scene async-loads, the TMP atlas
/// pre-warms, and the new scene's singletons settle.
///
/// Lifecycle:
///   • Auto-spawned BeforeSceneLoad (so it's available immediately on game
///     launch — before the main menu fully draws).
///   • DontDestroyOnLoad — survives scene transitions, can keep covering
///     the gameplay scene's first frame while singletons spawn.
///   • Canvas sortingOrder 30000 — above the pause menu (1000+) and every
///     other overlay so it ALWAYS draws on top.
///
/// Usage (from MainMenuController):
///   <code>LoadingScreen.Instance.LoadSceneAndShow("1.6.7.7.7");</code>
/// The coroutine runs on this DDOL singleton, NOT on the caller — caller
/// scenes can unload mid-load without aborting the routine.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

    Canvas _canvas;
    Image  _backdrop;
    RectTransform _barFillRT;
    TextMeshProUGUI _statusText;
    TextMeshProUGUI _percentText;

    float _shownProgress;       // 0..1, smoothed
    float _targetProgress;
    bool  _routineRunning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        go.AddComponent<LoadingScreen>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        Hide();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        // Smooth the progress bar toward its target so quick jumps in the
        // underlying AsyncOperation.progress (which can leap from 0 → 0.9
        // in one frame on a fast machine) animate visibly. Unscaled time
        // because Time.timeScale is 0 during pause.
        if (_canvas != null && _canvas.enabled)
        {
            _shownProgress = Mathf.MoveTowards(_shownProgress, _targetProgress, Time.unscaledDeltaTime * 1.5f);
            if (_barFillRT != null) _barFillRT.anchorMax = new Vector2(Mathf.Clamp01(_shownProgress), 1f);
            if (_percentText != null)
            {
                int pct = Mathf.RoundToInt(_shownProgress * 100f);
                _percentText.text = pct + "%";
            }
        }
    }

    // ── Public API ──────────────────────────────────────────────────

    public void Show(string status = "Loading...")
    {
        if (_canvas != null) _canvas.enabled = true;
        SetStatus(status);
        _targetProgress = 0f;
        _shownProgress  = 0f;
        if (_barFillRT != null) _barFillRT.anchorMax = new Vector2(0f, 1f);
        if (_percentText != null) _percentText.text = "0%";
    }

    public void Hide()
    {
        if (_canvas != null) _canvas.enabled = false;
    }

    public void SetProgress(float p) { _targetProgress = Mathf.Clamp01(p); }
    public void SetStatus(string s)  { if (_statusText != null) _statusText.text = s ?? string.Empty; }

    /// <summary>Public entry point. Starts the async load coroutine on this
    /// (DDOL) instance so callers can be destroyed by the scene transition
    /// without killing the routine.
    ///
    /// <paramref name="preSceneSetup"/> is a coroutine factory — it runs
    /// INSIDE the load routine, after the loading-screen Canvas has
    /// rendered for two frames. Use this for chunked work that should
    /// drive the loading bar (e.g. seeding 18 singletons with a yield
    /// between each so the bar animates instead of freezing at 10%).
    /// The callback receives (0..1 fraction, status text) — invoke it
    /// inside your coroutine after each step.</summary>
    public void LoadSceneAndShow(string sceneName,
        Func<Action<float, string>, IEnumerator> preSceneSetup = null,
        Action onReady = null)
    {
        if (_routineRunning) return;
        // Show INSTANTLY so the canvas paints on the next end-of-frame —
        // before any of the heavy work in the coroutine kicks off.
        Show("Preparing...");
        StartCoroutine(LoadSceneRoutine(sceneName, preSceneSetup, onReady));
    }

    // ── Coroutine ───────────────────────────────────────────────────

    IEnumerator LoadSceneRoutine(string sceneName,
        Func<Action<float, string>, IEnumerator> preSceneSetup,
        Action onReady)
    {
        _routineRunning = true;

        // CRITICAL: yield BEFORE any heavy work so the LoadingScreen Canvas
        // has a chance to render. Show() enabled the canvas synchronously,
        // but Unity only paints at end-of-frame; without yielding, the
        // preSceneSetup block below would freeze the main thread before
        // the canvas ever drew its first frame, exactly the bug this
        // pattern exists to prevent.
        yield return null;
        yield return null;

        // ── Phase 0: pre-scene chunked setup ──────────────────────
        // Singleton-seeding work runs here, one step per frame. Each
        // step's progress callback updates the bar (mapped into 0..0.4
        // band — pre-setup gets up to 40% since on cold start it dominates
        // wall-clock time) and the status text. The bar visibly fills
        // through this phase instead of freezing.
        if (preSceneSetup != null)
        {
            SetStatus("Initializing systems...");
            Action<float, string> report = (frac, label) =>
            {
                SetProgress(Mathf.Lerp(0f, 0.4f, Mathf.Clamp01(frac)));
                if (!string.IsNullOrEmpty(label)) SetStatus(label);
            };
            yield return preSceneSetup(report);
            SetProgress(0.4f);
            yield return null;
        }

        SetStatus("Loading scene...");

        // ── Phase 1: async scene load (allow up to 70% of bar) ─────
        // allowSceneActivation = false lets us hold the scene at the
        // "ready but not yet active" state while we pre-warm. Unity caps
        // op.progress at 0.9 in that mode.
        var op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;
        while (op.progress < 0.9f)
        {
            // Map raw 0..0.9 onto 0.4..0.7 — leaves the 0..0.4 band for the
            // pre-scene setup phase (singleton seeding) and reserves 0.7+
            // for font warmup / activation / settle.
            SetProgress(0.4f + Mathf.Lerp(0f, 0.3f, op.progress / 0.9f));
            yield return null;
        }
        SetProgress(0.7f);

        // ── Phase 2: TMP font atlas warmup ─────────────────────────
        // First-time use of an uncached character triggers TMP to
        // rasterize it into the atlas — a measurable per-character cost
        // that, in our build, manifested as multi-millisecond stutter
        // spikes on first dialogue / HUD reveal. Pre-rasterizing the
        // common character set during the load screen front-loads that
        // cost so the gameplay scene's first frame is clean.
        SetStatus("Warming font atlas...");
        yield return WarmFontAtlas();
        SetProgress(0.85f);

        // ── Phase 3: scene activation ──────────────────────────────
        // Flipping allowSceneActivation activates the loaded scene,
        // which fires every [RuntimeInitializeOnLoadMethod(AfterSceneLoad)]
        // hook (PlayerWallet, Hotbar, VitalsHUD, FPSOverlay,
        // CameraEffectsManager, etc.). Those Awakes happen while we're
        // still covering the screen, so the player doesn't see the
        // first-frame TMP/material burst.
        SetStatus("Initializing systems...");
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;
        SetProgress(0.95f);

        // Settle frames — give every singleton's Awake + first Start a
        // chance to run before we drop the cover.
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();

        SetProgress(1f);
        SetStatus("Ready");
        // Brief hold so the bar visibly fills to 100 before we cut.
        yield return new WaitForSecondsRealtime(0.15f);

        Hide();
        _routineRunning = false;
        onReady?.Invoke();
    }

    IEnumerator WarmFontAtlas()
    {
        // Force-load the project's primary HUD font and rasterize the
        // characters every gameplay-scene text uses on first frame. The
        // character set covers HUD numerals, common label words, dialogue
        // punctuation, and the special glyphs we render (★ for restart
        // hint, ▸/◂ for menu arrows, ↑↓←→ for compass / waypoint markers).
        var font = HudFontResolver.Default;
        if (font == null) yield break;

        const string ascii      = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        const string specials   = "★▸◂↑↓←→•·…—–’“”";
        font.TryAddCharacters(ascii + specials);
        yield return null;
    }

    // ── UI build ────────────────────────────────────────────────────

    void BuildUI()
    {
        // Canvas at sortingOrder 30000 — above the pause menu (1000),
        // FX overlays (800–820), build menu, save/load UI (2000). Stays
        // on top through scene transitions because we're DontDestroyOnLoad.
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 30000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var raycaster = gameObject.AddComponent<GraphicRaycaster>();
        raycaster.enabled = true; // block input while loading

        // Solid black backdrop covers the entire screen.
        var bdGO = new GameObject("Backdrop", typeof(RectTransform));
        bdGO.transform.SetParent(transform, false);
        var bdRT = bdGO.GetComponent<RectTransform>();
        bdRT.anchorMin = Vector2.zero;
        bdRT.anchorMax = Vector2.one;
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;
        _backdrop = bdGO.AddComponent<Image>();
        _backdrop.color = Color.black;
        _backdrop.raycastTarget = true;

        // Centered progress bar (track + fill).
        var trackGO = new GameObject("BarTrack", typeof(RectTransform));
        trackGO.transform.SetParent(transform, false);
        var trackRT = trackGO.GetComponent<RectTransform>();
        trackRT.anchorMin = new Vector2(0.5f, 0.5f);
        trackRT.anchorMax = new Vector2(0.5f, 0.5f);
        trackRT.pivot = new Vector2(0.5f, 0.5f);
        trackRT.anchoredPosition = new Vector2(0f, -20f);
        trackRT.sizeDelta = new Vector2(720f, 14f);
        var trackImg = trackGO.AddComponent<Image>();
        trackImg.color = new Color32(0x10, 0x18, 0x28, 0xFF);
        trackImg.raycastTarget = false;

        var fillGO = new GameObject("BarFill", typeof(RectTransform));
        fillGO.transform.SetParent(trackGO.transform, false);
        _barFillRT = fillGO.GetComponent<RectTransform>();
        _barFillRT.anchorMin = new Vector2(0f, 0f);
        _barFillRT.anchorMax = new Vector2(0f, 1f);
        _barFillRT.pivot = new Vector2(0f, 0.5f);
        _barFillRT.offsetMin = Vector2.zero;
        _barFillRT.offsetMax = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
        fillImg.raycastTarget = false;

        // Bar border outline.
        var borderGO = new GameObject("BarBorder", typeof(RectTransform));
        borderGO.transform.SetParent(trackGO.transform, false);
        var borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2f, -2f);
        borderRT.offsetMax = new Vector2(2f, 2f);
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = new Color32(0x78, 0xC8, 0xFF, 0x80);
        borderImg.raycastTarget = false;
        borderGO.transform.SetAsFirstSibling();

        // Status text — above the bar.
        var stTextGO = new GameObject("StatusText", typeof(RectTransform));
        stTextGO.transform.SetParent(transform, false);
        var stRT = stTextGO.GetComponent<RectTransform>();
        stRT.anchorMin = new Vector2(0.5f, 0.5f);
        stRT.anchorMax = new Vector2(0.5f, 0.5f);
        stRT.pivot = new Vector2(0.5f, 0.5f);
        stRT.anchoredPosition = new Vector2(0f, 20f);
        stRT.sizeDelta = new Vector2(900f, 40f);
        _statusText = stTextGO.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_statusText);
        _statusText.text = "Loading...";
        _statusText.fontSize = 22f;
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.color = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.characterSpacing = 4f;
        _statusText.raycastTarget = false;

        // Percent text — below the bar.
        var pctTextGO = new GameObject("PercentText", typeof(RectTransform));
        pctTextGO.transform.SetParent(transform, false);
        var pctRT = pctTextGO.GetComponent<RectTransform>();
        pctRT.anchorMin = new Vector2(0.5f, 0.5f);
        pctRT.anchorMax = new Vector2(0.5f, 0.5f);
        pctRT.pivot = new Vector2(0.5f, 0.5f);
        pctRT.anchoredPosition = new Vector2(0f, -50f);
        pctRT.sizeDelta = new Vector2(200f, 30f);
        _percentText = pctTextGO.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_percentText);
        _percentText.text = "0%";
        _percentText.fontSize = 16f;
        _percentText.color = new Color32(0x78, 0xC8, 0xFF, 0xCC);
        _percentText.alignment = TextAlignmentOptions.Center;
        _percentText.raycastTarget = false;
    }
}
