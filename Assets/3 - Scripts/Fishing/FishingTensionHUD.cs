using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// The fight's only piece of UI: one tension bar under the reticle, in the
/// helmet-HUD bracket language. [BUILD] 1's "UI: one thing only".
///
/// Deliberately NOT shown: the fish's stamina, a species icon, a catch-zone
/// minigame. How tired the fish is comes through the rod bend and how long the
/// runs stop coming — read off the world, not off a meter. The bar exists
/// because tension is the one quantity the player cannot see any other way.
///
/// Hidden completely outside the fight state, per [INTEGRATE].
///
/// Auto-singleton: MainMenu-skipping, so per CLAUDE.md trap #1 it is ALSO
/// seeded in MainMenuController.EnsureGameplaySingletons() — without that it
/// never auto-creates in a build, only in the Editor.
/// </summary>
public class FishingTensionHUD : MonoBehaviour
{
    public static FishingTensionHUD Instance { get; private set; }

    const float BarWidth = 190f;
    const float BarHeight = 6f;
    const float BelowCrosshair = 86f;
    const float BracketArm = 7f;
    const float BracketThick = 2f;
    const float FadeSpeed = 9f;

    // Above this the bar shifts toward red. The player learns the colour before
    // they learn the number.
    const float DangerFrom = 0.75f;

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _root;
    Image _track, _fill;
    Image _brL0, _brL1, _brR0, _brR1;

    float _target;      // 0-1 tension
    float _shown;       // smoothed alpha
    bool  _active;
    float _shake;       // run-driven jitter

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("FishingTensionHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<FishingTensionHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        if (_group != null) _group.alpha = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Show the bar and set tension, 0-1. Call every frame of the fight.</summary>
    public static void Set(float tension01, bool running)
    {
        if (Instance == null) return;
        Instance._active = true;
        Instance._target = Mathf.Clamp01(tension01);
        if (running) Instance._shake = 1f;
    }

    /// <summary>Fight over — the bar fades out.</summary>
    public static void Hide()
    {
        if (Instance == null) return;
        Instance._active = false;
    }

    // ── Paint ────────────────────────────────────────────────────────────────

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float want = _active ? 1f : 0f;
        _shown = Mathf.MoveTowards(_shown, want, FadeSpeed * dt);
        if (_group != null) _group.alpha = _shown;
        if (_shown <= 0.001f)
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            return;
        }
        if (_root != null && !_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

        _shake = Mathf.MoveTowards(_shake, 0f, dt * 3.5f);

        // Fill.
        if (_fill != null)
        {
            var rt = _fill.rectTransform;
            rt.sizeDelta = new Vector2(BarWidth * _target, BarHeight);
            _fill.color = ColorFor(_target);
        }

        // A run shoves the whole bar sideways a little — the same tell as the
        // rod jerking, so a player watching the bar still feels the run.
        if (_root != null)
        {
            float jitter = _shake > 0f ? Mathf.Sin(Time.unscaledTime * 46f) * 3.2f * _shake : 0f;
            _root.anchoredPosition = new Vector2(jitter, -BelowCrosshair);
        }

        Color bracket = HelmetHudPalette.AccentGlow;
        if (_target >= DangerFrom) bracket = ColorFor(_target);
        SetBracketColor(bracket);
    }

    static Color ColorFor(float t)
    {
        Color calm = HelmetHudPalette.Accent;
        Color hot  = new Color(0.94f, 0.26f, 0.18f, 1f);
        if (t <= DangerFrom) return calm;
        float k = Mathf.InverseLerp(DangerFrom, 1f, t);
        return Color.Lerp(calm, hot, k);
    }

    void SetBracketColor(Color c)
    {
        if (_brL0 != null) _brL0.color = c;
        if (_brL1 != null) _brL1.color = c;
        if (_brR0 != null) _brR0.color = c;
        if (_brR1 != null) _brR1.color = c;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    void Build()
    {
        var canvasGo = new GameObject("TensionCanvas", typeof(Canvas), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Under the pause/vendor UIs, above the world-space HUD chrome.
        _canvas.sortingOrder = 180;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _group = canvasGo.GetComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Use the project's shared scene gate rather than a second private
        // sceneLoaded handler. One mechanism deciding when DontDestroyOnLoad HUD
        // canvases are allowed to draw is the whole point of HUDSceneGate; a
        // parallel one is how they drift apart.
        HUDSceneGate.Register(_canvas);

        _root = NewRect("TensionBar", canvasGo.transform);
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.anchoredPosition = new Vector2(0f, -BelowCrosshair);
        _root.sizeDelta = new Vector2(BarWidth, BarHeight);

        _track = NewImage("Track", _root, new Vector2(BarWidth, BarHeight),
                          new Vector2(0.5f, 0.5f), Vector2.zero);
        _track.color = new Color(0f, 0f, 0f, 0.42f);

        // Fill grows from the left edge.
        _fill = NewImage("Fill", _root, new Vector2(0f, BarHeight),
                         new Vector2(0f, 0.5f), new Vector2(-BarWidth * 0.5f, 0f));
        _fill.rectTransform.anchorMin = _fill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _fill.color = HelmetHudPalette.Accent;

        float halfW = BarWidth * 0.5f + 4f;
        float halfH = BarHeight * 0.5f + 3f;
        _brL0 = Bracket("BrL0", new Vector2(-halfW, halfH), new Vector2(BracketArm, BracketThick), new Vector2(0f, 0.5f));
        _brL1 = Bracket("BrL1", new Vector2(-halfW, halfH), new Vector2(BracketThick, BracketArm), new Vector2(0f, 1f));
        _brR0 = Bracket("BrR0", new Vector2(halfW, -halfH), new Vector2(BracketArm, BracketThick), new Vector2(1f, 0.5f));
        _brR1 = Bracket("BrR1", new Vector2(halfW, -halfH), new Vector2(BracketThick, BracketArm), new Vector2(1f, 0f));

        _root.gameObject.SetActive(false);
    }

    Image Bracket(string name, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        var img = NewImage(name, _root, size, pivot, pos);
        img.color = HelmetHudPalette.AccentGlow;
        return img;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static Image NewImage(string name, Transform parent, Vector2 size, Vector2 pivot, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }
}
