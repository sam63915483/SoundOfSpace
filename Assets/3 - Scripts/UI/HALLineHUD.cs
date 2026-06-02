using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-world HUD strip that surfaces volunteered AI lines (from HALCommentator)
/// without requiring the player to open the phone. A small red HAL eye on the
/// left + a single line of cyan-white text, near the top of the screen, below
/// the compass.
///
/// Fades in, holds for ~5s, fades out. Lines queue if multiple events fire
/// close together so the player never misses one.
///
/// Auto-singleton with MainMenu skip — must also be seeded in
/// MainMenuController.EnsureGameplaySingletons per the trap in CLAUDE.md.
/// </summary>
public class HALLineHUD : MonoBehaviour
{
    public static HALLineHUD Instance { get; private set; }

    Canvas         _canvas;
    CanvasGroup    _group;
    RectTransform  _rt;
    TextMeshProUGUI _label;
    Image          _eye;
    Image          _eyeHalo;   // ref kept so Update can pulse it counter-phase

    // HAL eye colour lives in HALVisuals (shared with AIChatScreen).
    static readonly Color HalText = new Color32(0xEA, 0xF6, 0xFF, 0xFF);

    const float FadeInSeconds  = 0.4f;
    const float HoldSeconds    = 5.5f;
    const float FadeOutSeconds = 0.7f;
    const float GapBetweenLines = 0.3f;

    readonly Queue<string> _queue = new Queue<string>();
    Coroutine _processRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HALLineHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<HALLineHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>
    /// Show a line. If something is already on screen, queues this one to
    /// appear after.
    /// </summary>
    public void Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _queue.Enqueue(text);
        if (_processRoutine == null) _processRoutine = StartCoroutine(ProcessQueue());
    }

    IEnumerator ProcessQueue()
    {
        while (_queue.Count > 0)
        {
            string line = _queue.Dequeue();
            if (_label != null) _label.text = line;

            // Voice — kick off canned clip playback synchronised with the
            // start of the fade-in. Returns false silently if the line has
            // no clip in HALVoiceManifest (dynamic lines like "Astronaut
            // Number 4." or "Target reached: Tev." just show as text).
            if (HALVoicePlayer.Instance != null) HALVoicePlayer.Instance.TryPlay(line);

            yield return Fade(0f, 1f, FadeInSeconds);

            float t = 0f;
            while (t < HoldSeconds) { t += Time.unscaledDeltaTime; yield return null; }

            yield return Fade(1f, 0f, FadeOutSeconds);

            t = 0f;
            while (t < GapBetweenLines) { t += Time.unscaledDeltaTime; yield return null; }
        }
        _processRoutine = null;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            // ease-out cubic for in, ease-in for out
            float eased = (to > from) ? 1f - Mathf.Pow(1f - u, 3f) : u * u * u;
            if (_group != null) _group.alpha = Mathf.Lerp(from, to, eased);
            yield return null;
        }
        if (_group != null) _group.alpha = to;
    }

    void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        // 830 — above HUDs (800-820) and the phone (850) is irrelevant because
        // we WANT HAL lines to surface above the phone too. Below pause menu
        // (1000).
        _canvas.sortingOrder = 870;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Strip near top-center, under the compass.
        _rt = NewUI("HalLineRoot", transform);
        _rt.anchorMin = new Vector2(0.5f, 1f);
        _rt.anchorMax = new Vector2(0.5f, 1f);
        _rt.pivot     = new Vector2(0.5f, 1f);
        _rt.sizeDelta = new Vector2(1100f, 48f);
        _rt.anchoredPosition = new Vector2(0f, -130f);

        _group = _rt.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Soft halo behind the core. Built FIRST so its sibling index is
        // lower than the core's, making it render underneath. Stored in
        // _eyeHalo so Update can pulse it counter-phase to the core.
        var glowRT = NewUI("EyeGlow", _rt);
        glowRT.anchorMin = new Vector2(0f, 0.5f);
        glowRT.anchorMax = new Vector2(0f, 0.5f);
        glowRT.pivot     = new Vector2(0f, 0.5f);
        glowRT.anchoredPosition = new Vector2(20f, 0f);
        glowRT.sizeDelta = new Vector2(28f, 28f);
        _eyeHalo = glowRT.gameObject.AddComponent<Image>();
        _eyeHalo.sprite = HALVisuals.Disc();
        _eyeHalo.color = new Color(HALVisuals.EyeRed.r, HALVisuals.EyeRed.g, HALVisuals.EyeRed.b, 0.30f);
        _eyeHalo.raycastTarget = false;

        // Red HAL eye core. Rendered on top of the halo.
        var eyeRT = NewUI("Eye", _rt);
        eyeRT.anchorMin = new Vector2(0f, 0.5f);
        eyeRT.anchorMax = new Vector2(0f, 0.5f);
        eyeRT.pivot     = new Vector2(0f, 0.5f);
        eyeRT.anchoredPosition = new Vector2(24f, 0f);
        eyeRT.sizeDelta = new Vector2(16f, 16f);
        _eye = eyeRT.gameObject.AddComponent<Image>();
        _eye.sprite = HALVisuals.Disc();
        _eye.color = HALVisuals.EyeRed;
        _eye.raycastTarget = false;

        // Text label.
        var textRT = NewUI("HalLineText", _rt);
        textRT.anchorMin = new Vector2(0f, 0f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.offsetMin = new Vector2(56f, 0f);
        textRT.offsetMax = new Vector2(-24f, 0f);
        _label = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_label);
        _label.fontSize = 24f;
        _label.color = HalText;
        _label.alignment = TextAlignmentOptions.MidlineLeft;
        _label.raycastTarget = false;
        _label.enableWordWrapping = true;
        _label.fontStyle = FontStyles.Bold;
        // Subtle glow so the strip reads at any background brightness.
        var shadow = _label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(HALVisuals.EyeRed.r, HALVisuals.EyeRed.g, HALVisuals.EyeRed.b, 0.45f);
        shadow.effectDistance = Vector2.zero;
    }

    // Pulse the HAL eye on a slow sine — matches the chat-header eye in
    // AIChatScreen so HAL's visual identity is consistent across the two
    // surfaces. Core and halo are counter-phase so the silhouette never
    // reads as flat. Image-alpha multiplies with the CanvasGroup alpha,
    // so this happily co-exists with the fade-in / hold / fade-out cycle.
    void Update()
    {
        if (_eye == null) return;
        float phase = Time.unscaledTime * 0.8f;
        float coreA = 0.7f + 0.3f * (Mathf.Sin(phase) * 0.5f + 0.5f);
        var c = _eye.color; c.a = coreA; _eye.color = c;
        if (_eyeHalo != null)
        {
            float haloA = 0.15f + 0.30f * (Mathf.Sin(phase + Mathf.PI) * 0.5f + 0.5f);
            var h = _eyeHalo.color; h.a = haloA; _eyeHalo.color = h;
        }
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }
}
