using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TutorialUI : MonoBehaviour
{
    public static TutorialUI Instance { get; private set; }

    // Resting scale applied to pillRoot. The big-entry animation scales OFF of
    // this (so the dramatic "first frame" is still bigEntryScale× the rest size).
    const float kRestScale = 1.3f;

    // ── Inspector ──────────────────────────────────────────────────────────
    [Tooltip("Seconds between revealed characters in the typewriter (~57 chars/sec at 0.0176s — 1.7× the NPC-dialogue rate).")]
    public float charDelay = 0.0176f;

    [Header("Step Complete Sound")]
    [Tooltip("Plays once when a tutorial step is satisfied and the UI auto-advances to the next.")]
    public AudioClip stepCompleteClip;
    [Range(0f, 1f)] public float stepCompleteVolume = 0.7f;

    [Header("Big Intro (first tip only)")]
    [Tooltip("Scale multiplier of the very first tutorial tip when it appears in the screen center.")]
    public float bigEntryScale = 2.5f;
    [Tooltip("Seconds the tip lingers big in the center after the typewriter finishes, before settling to the top-right. Default 0 = settle starts the instant the last letter appears.")]
    public float bigEntryHoldSeconds = 0f;
    [Tooltip("Duration of the slide+scale settle from center to top-right.")]
    public float bigEntrySettleDuration = 0.7f;

    [Header("Layout (1920x1080 reference)")]
    [Tooltip("Distance from the right edge of the screen to the right edge of the pill.")]
    public float rightMargin = 20f;
    [Tooltip("Distance from the top of the screen to the top of the pill.")]
    public float topMargin = 20f;
    [Tooltip("Fixed width of the pill. Tip text wraps to two lines if it exceeds this.")]
    public float pillWidth = 360f;
    [Tooltip("Diagonal cut on the top-left and bottom-right corners (pixels).")]
    public float bevelSize = 14f;
    [Tooltip("Vertical pixels the pill slides down from when first revealed.")]
    public float slideOffset = 40f;

    [Header("Animation")]
    [Tooltip("Duration of the slide-in / slide-out animation.")]
    public float slideDuration = 0.25f;

    // ── Internal state ─────────────────────────────────────────────────────
    CanvasGroup group;
    Canvas _canvas;
    RectTransform pillRoot;
    RectTransform pillRect;
    Image pillBg;
    Image pillBorder;
    Image accentBar;
    TextMeshProUGUI headerTagText;
    TextMeshProUGUI tipText;

    Coroutine fadeRoutine;
    Coroutine slideRoutine;
    Coroutine tipRevealRoutine;

    string _currentTipLine = "";
    bool _tipRevealing;
    bool _swungIn;
    bool _isOffScreen;
    bool _firstEntryDone;
    bool _useLeftSide;  // flipped by MapTutorial during map mode so the pill clears the legend
    AudioSource _audio;

    public bool IsTipRevealing => _tipRevealing;
    public bool IsOffScreen => _isOffScreen;
    public bool IsAnimating => slideRoutine != null;

    // The pill's underlying Canvas — exposed so the map controller's
    // "hide every canvas while in map mode" loop can skip it. Without this,
    // the MapTutorial pill animates invisibly under a disabled canvas.
    public Canvas TutorialCanvas => _canvas;

    // ── Palette ────────────────────────────────────────────────────────────
    static readonly Color PillBgTopColor    = new Color32(0x08, 0x12, 0x20, 0xE0);
    static readonly Color PillBgBottomColor = new Color32(0x0A, 0x18, 0x28, 0xEB);
    static readonly Color PillBorderColor   = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color AccentColor       = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color AccentColorDim    = new Color32(0x5C, 0xC8, 0xFF, 0xB3);
    static readonly Color HeaderTagColor    = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color TipColor          = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TipGlowColor      = new Color(0.38f, 0.78f, 1f, 0.45f);
    static readonly Color CompletedColor    = new Color32(0x88, 0xDC, 0xAA, 0xFF);
    static readonly Color CheckColor        = new Color32(0x88, 0xDC, 0xAA, 0xFF);
    static readonly Color CheckGlowColor    = new Color(0.30f, 0.92f, 0.45f, 0.55f);

    static Sprite beveledPanelSprite;
    static Sprite beveledOutlineSprite;
    static Sprite accentBarSprite;
    static Sprite checkSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("TutorialUI");
        DontDestroyOnLoad(go);
        go.AddComponent<TutorialUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Another TutorialUI already exists — almost always the one
            // MainMenuController.EnsureGameplaySingletons spawns before
            // scene load. That seeded instance has none of the Inspector
            // fields configured on the scene-placed copy, so before we
            // destroy ourselves, hand our serialized fields over to it.
            // (Without this, builds end up with Instance = the seeded
            // clipless one and PlayCompleteSound silently no-ops, while
            // the editor — which never runs the MainMenu seeding — looked
            // fine because the scene-placed copy won the Instance race.)
            if (Instance.stepCompleteClip == null && stepCompleteClip != null)
                Instance.stepCompleteClip = stepCompleteClip;
            Instance.stepCompleteVolume     = stepCompleteVolume;
            Instance.charDelay              = charDelay;
            Instance.bigEntryScale          = bigEntryScale;
            Instance.bigEntryHoldSeconds    = bigEntryHoldSeconds;
            Instance.bigEntrySettleDuration = bigEntrySettleDuration;
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Persist scene-placed instances the same way AutoCreate does — the user
        // adds a TutorialUI GameObject to the scene only to assign the audio clip
        // in the Inspector; without DontDestroyOnLoad it would die on scene
        // reload and leave the auto-created singleton without the clip.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        // Mirror the PlayerController SFX pattern: a dedicated child
        // GameObject hosts the AudioSource. Putting the AudioSource on the
        // same GameObject as the auto-built Canvas turned out to be the
        // reason the step-complete sound played in the editor but never in
        // standalone builds. A clean child GameObject — same shape used for
        // PlayerSFX/PlayerFootsteps — works in both.
        var sfxObj = new GameObject("TutorialSFX");
        sfxObj.transform.SetParent(transform, false);
        sfxObj.transform.localPosition = Vector3.zero;
        _audio = sfxObj.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        BuildCanvas();
        if (group != null) group.alpha = 0f;
        if (pillRoot != null)
            pillRoot.anchoredPosition = OffScreenPos();
        StartCoroutine(BorderPulse());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // X sign flips with side: anchor is top-right (1,1) by default and the
    // X-inset is negative; top-left (0,1) inverts that.
    Vector2 RestPos()      => new Vector2(_useLeftSide ? rightMargin : -rightMargin, -topMargin);
    Vector2 OffScreenPos() => new Vector2(_useLeftSide ? rightMargin : -rightMargin, -topMargin + slideOffset);

    // Toggled by MapTutorial so the pill sits on the LEFT during map mode
    // (the legend lives on the right and would otherwise be obscured).
    // No-op if already on the requested side.
    public void SetLeftSide(bool left)
    {
        if (_useLeftSide == left) return;
        _useLeftSide = left;
        if (pillRoot != null)
        {
            Vector2 anchor = new Vector2(left ? 0f : 1f, 1f);
            pillRoot.anchorMin = anchor;
            pillRoot.anchorMax = anchor;
            pillRoot.pivot = anchor;
            pillRoot.anchoredPosition = _isOffScreen ? OffScreenPos() : RestPos();
            var rootVlg = pillRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (rootVlg != null)
                rootVlg.childAlignment = left ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void ShowStep(string tip, int index, int total, string headerOverride = null)
    {
        _currentTipLine = DecorateKeyGlyphs(tip ?? "");
        if (tipRevealRoutine != null) StopCoroutine(tipRevealRoutine);
        tipRevealRoutine = null;
        _tipRevealing = false;
        if (tipText != null)
        {
            tipText.text = _currentTipLine;
            tipText.maxVisibleCharacters = 0;
            tipText.ForceMeshUpdate();
        }

        // Every tip gets the big-center-then-settle treatment. If a previous
        // entrance is still in flight, just leave the new tip text staged —
        // the running coroutine reveals from _currentTipLine, so it picks up
        // the latest text on its next character. (Manager.Update gates on
        // IsAnimating, so this overlap is rare in practice.)
        if (slideRoutine != null) return;
        // BigEntry brings the pill back on-screen from any prior state,
        // including post-SwingOff (used by BonusTutorial transitions).
        _isOffScreen = false;
        slideRoutine = StartCoroutine(BigEntryRoutine());
    }

    public void SetTip(string tip)
    {
        _currentTipLine = DecorateKeyGlyphs(tip ?? "");
        if (!_tipRevealing && tipText != null)
        {
            tipText.text = _currentTipLine;
            tipText.maxVisibleCharacters = _currentTipLine.Length;
        }
    }

    public void PlayCompleteSound()
    {
        if (_audio == null)
        {
            Debug.LogWarning("[TutorialUI] PlayCompleteSound: _audio is null.");
            return;
        }
        if (stepCompleteClip == null)
        {
            Debug.LogWarning("[TutorialUI] PlayCompleteSound: stepCompleteClip is null on Instance — drag a clip onto the TutorialUI GameObject in the scene.");
            return;
        }
        // In standalone builds an mp3 with `preloadAudioData=false` may not
        // have its sample data ready on first PlayOneShot — the call returns
        // silently. Force-load before playing.
        if (stepCompleteClip.loadState != AudioDataLoadState.Loaded)
            stepCompleteClip.LoadAudioData();
        Debug.Log($"[TutorialUI] PlayCompleteSound firing: clip={stepCompleteClip.name}, vol={stepCompleteVolume}, loadState={stepCompleteClip.loadState}");
        _audio.PlayOneShot(stepCompleteClip, stepCompleteVolume);
    }

    public void HideAll()
    {
        if (slideRoutine != null) { StopCoroutine(slideRoutine); slideRoutine = null; }
        if (tipRevealRoutine != null) { StopCoroutine(tipRevealRoutine); tipRevealRoutine = null; }
        _tipRevealing = false;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(group.alpha, 0f, 0.3f));
    }

    public void SwingOff(Action onComplete = null)
    {
        StartCoroutine(SlideOutRoutine(onComplete));
    }

    public void SwingOn(Action onComplete = null)
    {
        StartCoroutine(SlideInBonusRoutine(onComplete));
    }

    // ── Animation coroutines ───────────────────────────────────────────────

    IEnumerator SlideIn()
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, slideDuration);
        Vector2 from = OffScreenPos();
        Vector2 to   = RestPos();
        if (pillRoot != null) pillRoot.anchoredPosition = from;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = 1f - Mathf.Pow(1f - u, 3f);
            if (pillRoot != null) pillRoot.anchoredPosition = Vector2.Lerp(from, to, k);
            if (group != null) group.alpha = k;
            yield return null;
        }
        if (pillRoot != null) pillRoot.anchoredPosition = to;
        if (group != null) group.alpha = 1f;
        slideRoutine = null;
        _swungIn = true;
        if (tipRevealRoutine == null && !string.IsNullOrEmpty(_currentTipLine))
            tipRevealRoutine = StartCoroutine(RevealTipRoutine());
    }

    IEnumerator SlideOutRoutine(Action onComplete)
    {
        _isOffScreen = true;
        if (tipRevealRoutine != null) { StopCoroutine(tipRevealRoutine); tipRevealRoutine = null; }
        if (fadeRoutine != null) { StopCoroutine(fadeRoutine); fadeRoutine = null; }
        _tipRevealing = false;

        float t = 0f;
        float dur = Mathf.Max(0.01f, slideDuration);
        Vector2 from = (pillRoot != null) ? pillRoot.anchoredPosition : RestPos();
        Vector2 to   = OffScreenPos();
        float fromAlpha = (group != null) ? group.alpha : 1f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = u * u * u;
            if (pillRoot != null) pillRoot.anchoredPosition = Vector2.Lerp(from, to, k);
            if (group != null) group.alpha = Mathf.Lerp(fromAlpha, 0f, k);
            yield return null;
        }
        if (pillRoot != null) pillRoot.anchoredPosition = to;
        if (group != null) group.alpha = 0f;
        onComplete?.Invoke();
    }

    IEnumerator SlideInBonusRoutine(Action onComplete)
    {
        if (pillRoot != null) pillRoot.anchoredPosition = OffScreenPos();
        if (group != null) group.alpha = 0f;

        float t = 0f;
        float dur = Mathf.Max(0.01f, slideDuration);
        Vector2 from = OffScreenPos();
        Vector2 to   = RestPos();
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = 1f - Mathf.Pow(1f - u, 3f);
            if (pillRoot != null) pillRoot.anchoredPosition = Vector2.Lerp(from, to, k);
            if (group != null) group.alpha = k;
            yield return null;
        }
        if (pillRoot != null) pillRoot.anchoredPosition = to;
        if (group != null) group.alpha = 1f;
        _isOffScreen = false;
        if (tipRevealRoutine == null && !string.IsNullOrEmpty(_currentTipLine))
            tipRevealRoutine = StartCoroutine(RevealTipRoutine());
        onComplete?.Invoke();
    }

    IEnumerator RevealTipRoutine()
    {
        _tipRevealing = true;
        if (tipText != null)
        {
            tipText.text = _currentTipLine;
            tipText.maxVisibleCharacters = 0;
            tipText.ForceMeshUpdate();
        }
        int i = 0;
        while (true)
        {
            string line = _currentTipLine ?? "";
            if (tipText != null && tipText.text != line)
            {
                tipText.text = line;
                tipText.ForceMeshUpdate();
            }
            // Iterate by VISIBLE chars (after TMP parses markup), not raw
            // string length. _currentTipLine includes long color/size/bracket
            // tags from DecorateKeyGlyphs — iterating raw length keeps
            // ticking long after the last letter is shown, which the player
            // perceives as a 1–2s hang before the pill settles.
            int visibleCount = tipText != null
                ? tipText.textInfo.characterCount
                : line.Length;
            if (i >= visibleCount) break;
            i++;
            if (tipText != null) tipText.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(charDelay);
        }
        if (tipText != null) tipText.maxVisibleCharacters = int.MaxValue;
        _tipRevealing = false;
        tipRevealRoutine = null;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        group.alpha = to;
    }

    IEnumerator BorderPulse()
    {
        while (this != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.6f) + 1f) * 0.5f;
            if (accentBar != null)
                accentBar.color = Color.Lerp(AccentColorDim, AccentColor, t);
            yield return null;
        }
    }

    // First-tip dramatic entrance: fades in big at screen center, lets the
    // typewriter reveal the line, holds briefly so the player can read, then
    // settles down to the top-right rest position at normal scale. Subsequent
    // tips use SlideIn (small drop from above).
    IEnumerator BigEntryRoutine()
    {
        if (pillRoot == null || pillRect == null || group == null) yield break;

        // Resolve the pill's natural height after layout so the centered
        // position genuinely sits at screen center.
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(pillRect);
        float rectH = Mathf.Max(80f, pillRect.rect.height);
        float rectW = pillWidth;
        float scale = Mathf.Max(1f, bigEntryScale);

        // pillRoot has anchor & pivot at the upper corner — (1,1) on the
        // right or (0,1) on the left (toggled by SetLeftSide). To visually
        // center the rect at screen center, the X-inset is half-rect AWAY
        // from the anchor — negative on the right, positive on the left.
        // Multiply by kRestScale so the big-entry centers based on rendered
        // (post-base-scale) size, not raw sizeDelta.
        const float refW = 1920f;
        const float refH = 1080f;
        float bigScale = scale * kRestScale;
        float xSign = _useLeftSide ? 1f : -1f;
        Vector2 bigPos = new Vector2(
            xSign * (refW * 0.5f - rectW * bigScale * 0.5f),
            -refH * 0.5f + rectH * bigScale * 0.5f);

        Vector2 restPos = RestPos();
        Vector3 bigScaleV = Vector3.one * bigScale;
        Vector3 restScale = Vector3.one * kRestScale;

        pillRoot.anchoredPosition = bigPos;
        pillRoot.localScale = bigScaleV;
        group.alpha = 0f;

        // Fade in.
        float t = 0f;
        const float fadeDur = 0.4f;
        while (t < fadeDur)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(t / fadeDur);
            yield return null;
        }
        group.alpha = 1f;

        // Reveal the tip and wait for it to finish.
        if (tipRevealRoutine != null) StopCoroutine(tipRevealRoutine);
        tipRevealRoutine = StartCoroutine(RevealTipRoutine());
        while (_tipRevealing) yield return null;

        // Hold so the player can read at large size.
        float hold = Mathf.Max(0f, bigEntryHoldSeconds);
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        // Settle: animate position + scale together to the rest layout.
        float dur = Mathf.Max(0.05f, bigEntrySettleDuration);
        t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float k = 1f - Mathf.Pow(1f - u, 3f);
            pillRoot.anchoredPosition = Vector2.Lerp(bigPos, restPos, k);
            pillRoot.localScale = Vector3.Lerp(bigScaleV, restScale, k);
            yield return null;
        }
        pillRoot.anchoredPosition = restPos;
        pillRoot.localScale = restScale;

        slideRoutine = null;
        _swungIn = true;
        _firstEntryDone = true;
    }

    // ── Layout build ───────────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 830; // above LetterboxBars (820) — stays visible during dialogue / cook UI
        HUDSceneGate.Register(canvas);
        _canvas = canvas;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // ── PromptGroup (the slider, top-right anchored) ────────────────
        pillRoot = NewUI("PromptGroup", transform);
        pillRoot.anchorMin = pillRoot.anchorMax = new Vector2(1f, 1f);
        pillRoot.pivot = new Vector2(1f, 1f);
        pillRoot.anchoredPosition = RestPos();
        pillRoot.sizeDelta = new Vector2(pillWidth, 0f);
        pillRoot.localScale = Vector3.one * kRestScale; // 1.3× HUD scale per design

        var rootVlg = pillRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        rootVlg.childAlignment = TextAnchor.UpperRight;
        rootVlg.childControlWidth = true;
        rootVlg.childControlHeight = true;
        rootVlg.childForceExpandWidth = true;
        rootVlg.childForceExpandHeight = false;
        rootVlg.spacing = 0f;
        rootVlg.padding = new RectOffset(0, 0, 0, 0);

        var rootFitter = pillRoot.gameObject.AddComponent<ContentSizeFitter>();
        rootFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rootFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // ── Pill ─────────────────────────────────────────────────────────
        pillRect = NewUI("Pill", pillRoot);
        pillRect.anchorMin = new Vector2(0f, 0f);
        pillRect.anchorMax = new Vector2(1f, 0f);
        pillRect.pivot = new Vector2(0.5f, 1f);

        pillBg = pillRect.gameObject.AddComponent<Image>();
        pillBg.sprite = GetBeveledPanelSprite();
        pillBg.type = Image.Type.Sliced;
        pillBg.color = PillBgBottomColor;
        pillBg.raycastTarget = false;

        // LED bar — vertical strip on the left, matches VitalsHUD / GForceHUD
        // exactly: 3 px wide, plain cyan rect, anchored at x=9 with 20 px
        // vertical inset. Pulse animation in BorderPulse() keeps the cyan
        // gently breathing so the active-tutorial pill reads as "alive".
        var accentRT = NewUI("AccentBar", pillRect);
        accentRT.anchorMin = new Vector2(0f, 0f);
        accentRT.anchorMax = new Vector2(0f, 1f);
        accentRT.pivot = new Vector2(0f, 0.5f);
        accentRT.anchoredPosition = new Vector2(9f, 0f);
        accentRT.sizeDelta = new Vector2(3f, -20f);
        accentRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        accentBar = accentRT.gameObject.AddComponent<Image>();
        accentBar.color = AccentColor;
        accentBar.raycastTarget = false;

        // Border outline drawn on top of the body + accents.
        var border = NewUI("Border", pillRect);
        Stretch(border, 0f, 0f, 0f, 0f);
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        pillBorder = border.gameObject.AddComponent<Image>();
        pillBorder.sprite = GetBeveledOutlineSprite();
        pillBorder.type = Image.Type.Sliced;
        pillBorder.color = PillBorderColor;
        pillBorder.raycastTarget = false;

        // Pill content stack: header tag → body → inline sub-line.
        var pillVlg = pillRect.gameObject.AddComponent<VerticalLayoutGroup>();
        pillVlg.childAlignment = TextAnchor.MiddleLeft;
        pillVlg.childControlWidth = true;
        pillVlg.childControlHeight = true;
        pillVlg.childForceExpandWidth = true;
        pillVlg.childForceExpandHeight = false;
        pillVlg.spacing = 4f;
        pillVlg.padding = new RectOffset(22, 18, 12, 12);

        var pillFitter = pillRect.gameObject.AddComponent<ContentSizeFitter>();
        pillFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        pillFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Header tag — small uppercase "// TUTORIAL".
        headerTagText = NewText(pillRect, "Header", "// TUTORIAL", 9f, FontStyles.Bold | FontStyles.UpperCase, HeaderTagColor);
        headerTagText.alignment = TextAlignmentOptions.MidlineLeft;
        headerTagText.characterSpacing = 6f;

        // Body text — the live tip from TutorialStep.Tip.
        tipText = NewText(pillRect, "Tip", "", 14f, FontStyles.Bold, TipColor);
        tipText.alignment = TextAlignmentOptions.MidlineLeft;
        tipText.lineSpacing = 4f;
        tipText.characterSpacing = 1f;
        tipText.enableWordWrapping = true;
        var tipGlow = tipText.gameObject.AddComponent<Shadow>();
        tipGlow.effectColor = TipGlowColor;
        tipGlow.effectDistance = new Vector2(0f, 0f);
        var tipShadow = tipText.gameObject.AddComponent<Shadow>();
        tipShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        tipShadow.effectDistance = new Vector2(0f, -2f);

        // Bind the keycap sprite atlas so <sprite name="F"> etc. resolve in
        // tip text. Built lazily; falls back silently to bold text if the
        // runtime build fails.
        EnsureKeycapAsset();
        if (_keycapAsset != null) tipText.spriteAsset = _keycapAsset;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
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
        t.alignment = TextAlignmentOptions.TopLeft;
        return t;
    }

    // ── Procedural sprite generation ───────────────────────────────────────

    static Sprite GetBeveledPanelSprite()
    {
        if (beveledPanelSprite != null) return beveledPanelSprite;
        var tex = MakeBeveledPanelTexture(64, 14, true);
        beveledPanelSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                           100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        beveledPanelSprite.name = "TutorialBeveledPanel";
        return beveledPanelSprite;
    }

    static Sprite GetBeveledOutlineSprite()
    {
        if (beveledOutlineSprite != null) return beveledOutlineSprite;
        var tex = MakeBeveledOutlineTexture(64, 14, 2);
        beveledOutlineSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f),
                                             100f, 0u, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
        beveledOutlineSprite.name = "TutorialBeveledOutline";
        return beveledOutlineSprite;
    }

    static Sprite GetAccentBarSprite()
    {
        if (accentBarSprite != null) return accentBarSprite;
        var tex = MakeRoundedRectTexture(16, 8, Color.white);
        accentBarSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f),
                                        100f, 0u, SpriteMeshType.FullRect, new Vector4(7, 7, 7, 7));
        accentBarSprite.name = "TutorialAccentBar";
        return accentBarSprite;
    }

    static Sprite GetCheckSprite()
    {
        if (checkSprite != null) return checkSprite;
        var tex = MakeCheckTexture(32);
        checkSprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100f);
        checkSprite.name = "TutorialCheck";
        return checkSprite;
    }

    // Tutorial pill silhouette: rectangle with top-left + bottom-right corners
    // cut by a diagonal of `bevel` pixels. Optional vertical gradient.
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

    // Hollow outline matching MakeBeveledPanelTexture.
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

    static Texture2D MakeRoundedRectTexture(int size, int cornerRadius, Color color)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color(color.r, color.g, color.b,
                    color.a * RoundedRectAlpha(x, y, size, cornerRadius));
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeCheckTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];

        Vector2 a = new Vector2(0.18f, 0.55f) * size;
        Vector2 b = new Vector2(0.40f, 0.30f) * size;
        Vector2 c = new Vector2(0.82f, 0.78f) * size;
        float stroke = size * 0.13f;
        float feather = 1.4f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(DistToSegment(p, a, b), DistToSegment(p, b, c));
                float alpha = Mathf.Clamp01((stroke - d) / feather);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        Vector2 closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }

    // ── Keycap decoration ─────────────────────────────────────────────────
    //
    // Wraps known keyboard key glyphs in a cyan-tinted highlight box. Other
    // <b>...</b> runs (numeric counters, generic emphasis) and controller
    // glyphs are left untouched. This decoration runs ONLY on tip text fed
    // into TutorialUI — PromptGlyphs returns plain bold strings so NPCs and
    // pickup prompts that share those strings render normally.
    //
    // Order matters: longer labels first so "WASD + Shift" matches before
    // "WASD" or "Shift" alone.
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
            // Bracket-wrap the key in bright cyan: "[F]" / "[TAB]" / "[WASD]".
            // Visual delineates keys clearly against the body text without
            // depending on TMP's <mark> highlight (which silently no-ops on
            // certain font materials, including the Techno SDF used here).
            // Brackets are scaled to ~115% so they read as a frame.
            string replacement =
                "<color=#5CC8FF><size=115%>[</size><b>" + label + "</b><size=115%>]</size></color>";
            result = result.Replace(needle, replacement);
        }
        return result;
    }

    static float RoundedRectAlpha(int x, int y, int size, int radius)
    {
        int dx = 0, dy = 0;
        if (x < radius) dx = radius - x;
        else if (x >= size - radius) dx = x - (size - radius - 1);
        if (y < radius) dy = radius - y;
        else if (y >= size - radius) dy = y - (size - radius - 1);
        if (dx <= 0 || dy <= 0) return 1f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(radius - d + 0.5f);
    }

    // ── Keycap sprite atlas ────────────────────────────────────────────────
    //
    // Builds a runtime TMP_SpriteAsset containing one cyan-tinted rounded
    // keycap per supported key. Used by PromptGlyphs.Maybe to render inline
    // keycap visuals in tip text instead of plain bold letters. Multi-word
    // glyphs (Space, Shift, WASD, mouse, etc.) and controller paths stay as
    // bold text — sprite keycaps for arbitrary phrases would look inconsistent.

    static TMP_SpriteAsset _keycapAsset;
    static System.Collections.Generic.HashSet<string> _keycapNames;

    // (label drawn into the keycap, sprite name used in <sprite name=...>, cell width).
    // Width is wider for multi-character keys (TAB, Esc) so the letters fit.
    static readonly (string label, string name, int width)[] KeycapDefs = new[]
    {
        ("F",   "F",   28),
        ("E",   "E",   28),
        ("G",   "G",   28),
        ("M",   "M",   28),
        ("N",   "N",   28),
        ("B",   "B",   28),
        ("Q",   "Q",   28),
        ("TAB", "TAB", 44),
        ("Esc", "Esc", 44),
    };

    public static bool HasKeycapSprite(string keyName)
    {
        if (_keycapNames == null) return false;
        return _keycapNames.Contains(keyName);
    }

    static void EnsureKeycapAsset()
    {
        if (_keycapAsset != null) return;
        try
        {
            const int cellHeight = 28;
            const int padding = 2;
            int totalWidth = padding;
            for (int i = 0; i < KeycapDefs.Length; i++) totalWidth += KeycapDefs[i].width + padding;
            int atlasW = Mathf.NextPowerOfTwo(totalWidth);
            int atlasH = Mathf.NextPowerOfTwo(cellHeight + padding * 2);

            var atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
            atlas.filterMode = FilterMode.Bilinear;
            atlas.wrapMode = TextureWrapMode.Clamp;
            var clear = new Color[atlasW * atlasH];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color(0, 0, 0, 0);
            atlas.SetPixels(clear);

            // Construct asset first so we can pass it to TMP_SpriteCharacter's
            // 3-arg constructor (binds the asset reference on the character).
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = "TutorialKeycapAtlas";
            asset.spriteSheet = atlas;
            // "TextMeshPro/Sprite" is stripped from standalone builds unless it's
            // in Project Settings > Graphics > Always Included Shaders. When that
            // happens Shader.Find returns null and the old code NullRef'd on the
            // broken material — silently skip keycaps so tip text falls back to bold.
            var spriteShader = Shader.Find("TextMeshPro/Sprite");
            if (spriteShader == null)
            {
                _keycapAsset = null;
                _keycapNames = null;
                return;
            }
            var mat = new Material(spriteShader);
            mat.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            asset.material = mat;

            var glyphTable = new System.Collections.Generic.List<TMP_SpriteGlyph>();
            var charTable  = new System.Collections.Generic.List<TMP_SpriteCharacter>();
            var nameSet    = new System.Collections.Generic.HashSet<string>();

            int cursorX = padding;
            int cellY = padding;
            for (int i = 0; i < KeycapDefs.Length; i++)
            {
                var def = KeycapDefs[i];
                int w = def.width;
                DrawKeycapCell(atlas, cursorX, cellY, w, cellHeight, def.label);

                var rect = new UnityEngine.TextCore.GlyphRect(cursorX, cellY, w, cellHeight);
                var metrics = new UnityEngine.TextCore.GlyphMetrics(w, cellHeight, 0, cellHeight - 4, w);
                var glyph = new TMP_SpriteGlyph((uint)i, metrics, rect, 1.0f, 0);
                glyphTable.Add(glyph);

                var character = new TMP_SpriteCharacter(0, asset, glyph);
                character.name = def.name;
                charTable.Add(character);
                nameSet.Add(def.name);
                cursorX += w + padding;
            }
            atlas.Apply();

            // The two tables are read-only properties (internal setter) but
            // their backing lists are auto-initialized to empty in TMP 3.x —
            // mutate via AddRange instead of assigning. UpdateLookupTables
            // then rebuilds the sprite-name lookup dictionary.
            asset.spriteGlyphTable.AddRange(glyphTable);
            asset.spriteCharacterTable.AddRange(charTable);
            asset.UpdateLookupTables();

            _keycapAsset = asset;
            _keycapNames = nameSet;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TutorialUI] Keycap atlas build failed; falling back to bold text. {ex}");
            _keycapAsset = null;
            _keycapNames = null;
        }
    }

    // Draws one keycap into the atlas at (originX, originY) with size w×h:
    // dark fill + cyan border ring + label centered in white.
    static void DrawKeycapCell(Texture2D atlas, int originX, int originY, int w, int h, string label)
    {
        Color body   = new Color(0.04f, 0.07f, 0.12f, 0.92f);
        Color border = new Color(0.36f, 0.78f, 1f, 0.85f);
        int radius = 4;
        int cellShort = Mathf.Min(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float a = RoundedRectAlpha(x, y, cellShort, radius);
                if (a <= 0f) continue;
                int distEdge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                Color c = (distEdge < 1) ? border : body;
                c.a *= a;
                atlas.SetPixel(originX + x, originY + y, c);
            }
        }

        // Render label by sampling Unity's dynamic OS font into a temp tex.
        var font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Liberation Sans", "DejaVu Sans" }, 18);
        var labelTex = RenderLabelToTexture(label, w, h, font, 14);
        if (labelTex != null)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var src = labelTex.GetPixel(x, y);
                    if (src.a <= 0f) continue;
                    var dst = atlas.GetPixel(originX + x, originY + y);
                    Color lit = new Color(0.95f, 0.97f, 1f, 1f);
                    atlas.SetPixel(originX + x, originY + y, Color.Lerp(dst, lit, src.a));
                }
            UnityEngine.Object.Destroy(labelTex);
        }
    }

    // Renders `text` centered into a w×h transparent texture using the given
    // legacy Font's dynamic glyph atlas. Returns null on platforms where the
    // font texture isn't sampleable from C#.
    static Texture2D RenderLabelToTexture(string text, int w, int h, Font font, int fontSize)
    {
        if (font == null) return null;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var clear = new Color[w * h];
        for (int i = 0; i < clear.Length; i++) clear[i] = new Color(0, 0, 0, 0);
        tex.SetPixels(clear);

        font.RequestCharactersInTexture(text, fontSize, FontStyle.Bold);
        var atlasTex = font.material.mainTexture as Texture2D;
        if (atlasTex == null) { tex.Apply(); return tex; }

        int totalWidth = 0;
        for (int i = 0; i < text.Length; i++)
            if (font.GetCharacterInfo(text[i], out var info, fontSize, FontStyle.Bold))
                totalWidth += info.advance;

        int startX = (w - totalWidth) / 2;
        int baselineY = (h + fontSize) / 2 - 4;
        int cursor = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!font.GetCharacterInfo(text[i], out var info, fontSize, FontStyle.Bold)) continue;
            // CharacterInfo UV rect — top-right and bottom-left flip when
            // glyph is rotated; use min/max directly.
            float uMin = Mathf.Min(info.uvBottomLeft.x, info.uvTopRight.x);
            float uMax = Mathf.Max(info.uvBottomLeft.x, info.uvTopRight.x);
            float vMin = Mathf.Min(info.uvBottomLeft.y, info.uvTopRight.y);
            float vMax = Mathf.Max(info.uvBottomLeft.y, info.uvTopRight.y);
            int gw = (int)info.glyphWidth;
            int gh = (int)info.glyphHeight;
            for (int gy = 0; gy < gh; gy++)
            {
                for (int gx = 0; gx < gw; gx++)
                {
                    float u = uMin + (gx + 0.5f) / atlasTex.width * (uMax - uMin);
                    float v = vMax - (gy + 0.5f) / atlasTex.height * (vMax - vMin);
                    Color c = atlasTex.GetPixelBilinear(u, v);
                    if (c.a <= 0f) continue;
                    int dx = startX + cursor + gx + (int)info.minX;
                    int dy = baselineY - gy - (int)info.maxY;
                    if (dx < 0 || dy < 0 || dx >= w || dy >= h) continue;
                    var dst = tex.GetPixel(dx, dy);
                    tex.SetPixel(dx, dy, Color.Lerp(dst, new Color(1f, 1f, 1f, c.a), c.a));
                }
            }
            cursor += info.advance;
        }
        tex.Apply();
        return tex;
    }
}
