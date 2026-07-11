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
/// Fades in, holds for ~5s, fades out. If more lines arrive while one is on
/// screen they stack BELOW the active line as smaller, dimmer previews (up to
/// 3). When the active line fades out, the next preview slides up + scales to
/// full size to become the new active line, and its voice plays at the end of
/// that transition. Nothing is silently dropped — an over-full queue drops its
/// OLDEST waiting preview (and logs a warning), never the incoming line.
///
/// Lines may also be "live": <see cref="ShowLive"/> takes a delegate that is
/// re-evaluated every frame while the line is the active primary, used for the
/// hull-sealed countdown ("Hull sealed — m s of air remaining").
///
/// Auto-singleton with MainMenu skip — must also be seeded in
/// MainMenuController.EnsureGameplaySingletons per the trap in CLAUDE.md.
/// </summary>
public class HALLineHUD : MonoBehaviour
{
    public static HALLineHUD Instance { get; private set; }

    // A queued line: a static snapshot string (always set, used for previews and
    // for non-live primaries) plus an optional live text source evaluated each
    // frame while primary, plus an optional explicit voice key.
    struct Line
    {
        public string text;               // snapshot text (previews + static lines)
        public System.Func<string> live;  // optional per-frame text source (primary only)
        public string voiceKey;           // optional TTS key (defaults to text/live())
        public bool shipScoped;           // §5: purged from the queue if the player leaves the ship radius
        public string key;                // stable identity for dedup (so the same tip can't stack)
    }

    Canvas         _canvas;
    CanvasGroup    _group;
    RectTransform  _rt;
    TextMeshProUGUI _label;
    Image          _eye;
    Image          _eyeHalo;   // ref kept so Update can pulse it counter-phase

    // Preview rows for queued (waiting) lines, stacked below the primary strip.
    const int MaxQueued = 3;
    RectTransform[]   _prevRT    = new RectTransform[MaxQueued];
    CanvasGroup[]     _prevCG    = new CanvasGroup[MaxQueued];
    TextMeshProUGUI[] _prevLabel = new TextMeshProUGUI[MaxQueued];

    // HAL eye colour lives in HALVisuals (shared with AIChatScreen).
    static readonly Color HalText = new Color32(0xEA, 0xF6, 0xFF, 0xFF);

    const float FadeInSeconds   = 0.4f;
    const float HoldSeconds     = 5.5f;
    const float FadeOutSeconds  = 0.7f;
    const float GapBetweenLines = 0.3f;
    const float PromoteSeconds  = 0.3f;   // slide-up + scale-up when a preview is promoted
    const float PreviewScale    = 0.8f;
    const float PreviewAlpha    = 0.55f;

    // Layout anchors (top-center pivot).
    const float PrimaryHomeY  = -240f;    // anchoredPosition.y of the active strip
    const float PreviewFirstY = -292f;    // first preview row (just below the strip)
    const float PreviewStepY  = -34f;     // vertical gap per preview row

    readonly Queue<Line> _queue = new Queue<Line>();
    Coroutine _processRoutine;
    System.Func<string> _activeLive;      // non-null while the primary is a live line
    string _activeKey;                    // dedup key of the line currently showing

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

    /// True when nothing is showing and nothing is queued — the strip is idle.
    /// Scripted sequences (the Mission 1 intro) poll this to send the next line
    /// only after the current one has fully played and faded.
    public bool IsIdle => _processRoutine == null && _queue.Count == 0;

    /// <summary>
    /// Stop the active line, drop every queued line, and hide the strip + all
    /// preview rows immediately. Used on the quit-to-menu transition.
    /// </summary>
    public void ClearAll()
    {
        if (_processRoutine != null) { StopCoroutine(_processRoutine); _processRoutine = null; }
        _queue.Clear();
        _activeLive = null;
        _activeKey = null;
        if (_group != null) _group.alpha = 0f;
        for (int i = 0; i < MaxQueued; i++)
            if (_prevCG[i] != null) _prevCG[i].alpha = 0f;
        if (HALVoicePlayer.Instance != null) HALVoicePlayer.Instance.Stop();   // cut any narration mid-line
    }

    /// <summary>
    /// Show a static line. If something is already on screen, this queues below
    /// it as a preview and is promoted when the active line finishes.
    /// </summary>
    public void Show(string text, bool shipScoped = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Enqueue(new Line { text = text, shipScoped = shipScoped, key = text });
    }

    /// <summary>
    /// Show a line whose text re-evaluates every frame while it is the active
    /// primary (e.g. a live countdown). <paramref name="voiceKey"/> is the TTS
    /// key; if null the initial text value is used. Previews show the snapshot.
    /// </summary>
    public void ShowLive(System.Func<string> textSource, string voiceKey = null, bool shipScoped = false, string dedupKey = null)
    {
        if (textSource == null) return;
        string snapshot = SafeEval(textSource);
        if (string.IsNullOrWhiteSpace(snapshot)) return;
        // Live tips need a STABLE dedup key — the snapshot text changes every frame
        // (e.g. a countdown), so fall back to dedupKey/voiceKey before the snapshot.
        string key = !string.IsNullOrEmpty(dedupKey) ? dedupKey
                   : (!string.IsNullOrEmpty(voiceKey) ? voiceKey : snapshot);
        Enqueue(new Line { text = snapshot, live = textSource, voiceKey = voiceKey, shipScoped = shipScoped, key = key });
    }

    /// <summary>
    /// §5: drop every QUEUED ship-scoped line (the active one keeps showing —
    /// it fades on its own in a few seconds). Called when the player leaves a
    /// ship's prompt radius so stale hull warnings don't pop up later.
    /// </summary>
    public void ClearShipScoped()
    {
        if (_queue.Count == 0) return;
        var kept = new List<Line>(_queue.Count);
        foreach (var l in _queue) if (!l.shipScoped) kept.Add(l);
        if (kept.Count == _queue.Count) return;   // nothing ship-scoped was waiting
        _queue.Clear();
        foreach (var l in kept) _queue.Enqueue(l);
        RefreshPreviews();
    }

    void Enqueue(Line line)
    {
        // "HIDE HUD" setting suppresses HAL tips entirely — no text strip, no TTS —
        // so trailer/clip captures with the HUD off stay clean. Gated on the user
        // setting only (UserHidden), NOT the cinematic force, so the pod-arrival
        // cutscene still gets its scripted HAL lines.
        if (HudVisibility.UserHidden) return;

        // No STACKING the same tip: if this exact tip is already showing or waiting
        // in the queue, ignore the new one. Prevents hatch-spam from piling up
        // duplicate "Hull exposed to the vacuum of space." / re-oxy / hull-sealed
        // tips. DIFFERENT tips still queue normally.
        if (!string.IsNullOrEmpty(line.key))
        {
            if (line.key == _activeKey) return;
            foreach (var q in _queue) if (q.key == line.key) return;
        }

        // Bounded queue: if full, drop the OLDEST waiting line (never the incoming).
        if (_queue.Count >= MaxQueued)
        {
            var dropped = _queue.Dequeue();
            Debug.LogWarning($"[HALLineHUD] tip queue full ({MaxQueued}); dropped oldest: \"{dropped.text}\"");
        }
        _queue.Enqueue(line);
        RefreshPreviews();
        if (_processRoutine == null) _processRoutine = StartCoroutine(ProcessQueue());
    }

    IEnumerator ProcessQueue()
    {
        bool firstInRun = true;
        while (_queue.Count > 0)
        {
            Line line = _queue.Dequeue();
            _activeKey = line.key;      // blocks duplicates of the showing line
            RefreshPreviews();          // remaining previews shift up immediately

            SetPrimaryText(line);

            bool hasVoice;
            if (firstInRun)
            {
                // No preview existed for this line — fade in from home, voice at
                // the start of the fade (the original behaviour for an idle HUD).
                _rt.anchoredPosition = new Vector2(0f, PrimaryHomeY);
                _rt.localScale = Vector3.one;
                hasVoice = PlayVoice(line);
                yield return Fade(0f, 1f, FadeInSeconds);
            }
            else
            {
                // This line was visible as a preview — slide it up + scale it to
                // full size, THEN play its voice (spec: TTS at end of transition).
                yield return Promote();
                hasVoice = PlayVoice(line);
            }
            firstInRun = false;

            // Voiced tips linger only as long as the narration; voiceless tips keep
            // the default read time.
            yield return HoldForLine(hasVoice);

            yield return Fade(1f, 0f, FadeOutSeconds);
            _activeLive = null;

            float gap = 0f;
            while (gap < GapBetweenLines) { gap += Time.unscaledDeltaTime; yield return null; }
        }
        _activeLive = null;
        _activeKey = null;
        _processRoutine = null;
    }

    void SetPrimaryText(Line line)
    {
        _activeLive = line.live;        // null for static lines
        if (_label != null) _label.text = line.live != null ? SafeEval(line.live) : line.text;
    }

    // Kicks off the canned clip; returns true if the line HAS a voice clip (so the
    // caller can hold the tip only as long as the narration). Returns false for
    // dynamic/voiceless lines (which just show as text for the default hold).
    bool PlayVoice(Line line)
    {
        if (HALVoicePlayer.Instance == null) return false;
        string key = !string.IsNullOrEmpty(line.voiceKey) ? line.voiceKey : line.text;
        if (string.IsNullOrEmpty(key)) return false;
        return HALVoicePlayer.Instance.TryPlay(key);
    }

    const float MinHoldSeconds = 1.2f;   // floor so a short clip doesn't flash by
    const float MaxHoldSeconds = 14f;    // ceiling so a long/stuck clip can't hang the HUD

    // Hold the active tip on screen: voiceless lines use the comfortable read time;
    // voiced lines stay only as long as the narration, then fall through to fade.
    IEnumerator HoldForLine(bool hasVoice)
    {
        float t = 0f;
        if (!hasVoice)
        {
            while (t < HoldSeconds) { t += Time.unscaledDeltaTime; yield return null; }
            yield break;
        }
        var vp = HALVoicePlayer.Instance;
        // Wait (briefly) for the clip to actually start — first play loads from disk.
        float startWait = 0f;
        while (startWait < 1f && (vp == null || !vp.IsPlaying))
        { startWait += Time.unscaledDeltaTime; t += Time.unscaledDeltaTime; yield return null; }
        // Hold while it's speaking (capped so it never hangs).
        while (vp != null && vp.IsPlaying && t < MaxHoldSeconds)
        { t += Time.unscaledDeltaTime; yield return null; }
        // Floor so a very short clip (or a failed load) doesn't blink past.
        while (t < MinHoldSeconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    // Slide the primary strip from the first preview slot (small + dim) up to its
    // home position at full size + opacity over PromoteSeconds.
    IEnumerator Promote()
    {
        Vector2 startPos = new Vector2(0f, PreviewFirstY);
        Vector2 homePos  = new Vector2(0f, PrimaryHomeY);
        float t = 0f;
        while (t < PromoteSeconds)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / PromoteSeconds);
            float e = 1f - Mathf.Pow(1f - u, 3f);   // ease-out cubic
            _rt.anchoredPosition = Vector2.Lerp(startPos, homePos, e);
            _rt.localScale = Vector3.one * Mathf.Lerp(PreviewScale, 1f, e);
            if (_group != null) _group.alpha = Mathf.Lerp(PreviewAlpha, 1f, e);
            yield return null;
        }
        _rt.anchoredPosition = homePos;
        _rt.localScale = Vector3.one;
        if (_group != null) _group.alpha = 1f;
    }

    // Mirror the current waiting queue into the preview rows.
    void RefreshPreviews()
    {
        var arr = _queue.ToArray();   // oldest..newest still waiting
        for (int i = 0; i < MaxQueued; i++)
        {
            bool on = i < arr.Length;
            if (_prevCG[i] != null) _prevCG[i].alpha = on ? PreviewAlpha : 0f;
            if (on)
            {
                if (_prevLabel[i] != null) _prevLabel[i].text = arr[i].text;
                if (_prevRT[i] != null)
                {
                    _prevRT[i].localScale = Vector3.one * PreviewScale;
                    _prevRT[i].anchoredPosition = new Vector2(0f, PreviewFirstY + i * PreviewStepY);
                }
            }
        }
    }

    static string SafeEval(System.Func<string> f)
    {
        try { return f != null ? f() : null; }
        catch { return null; }
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
        // 870 — above HUDs (800-820) and the phone (850) because we WANT HAL
        // lines to surface above the phone too. Below pause menu (1000).
        _canvas.sortingOrder = 870;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Active strip near top-center, under the compass.
        _rt = NewUI("HalLineRoot", transform);
        _rt.anchorMin = new Vector2(0.5f, 1f);
        _rt.anchorMax = new Vector2(0.5f, 1f);
        _rt.pivot     = new Vector2(0.5f, 1f);
        _rt.sizeDelta = new Vector2(1100f, 48f);
        _rt.anchoredPosition = new Vector2(0f, PrimaryHomeY);

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

        BuildPreviewRows();
    }

    // Build the (initially hidden) preview rows that show waiting/queued lines.
    void BuildPreviewRows()
    {
        for (int i = 0; i < MaxQueued; i++)
        {
            var rowRT = NewUI($"HalPreview{i}", transform);
            rowRT.anchorMin = new Vector2(0.5f, 1f);
            rowRT.anchorMax = new Vector2(0.5f, 1f);
            rowRT.pivot     = new Vector2(0.5f, 1f);
            rowRT.sizeDelta = new Vector2(1100f, 40f);
            rowRT.anchoredPosition = new Vector2(0f, PreviewFirstY + i * PreviewStepY);
            rowRT.localScale = Vector3.one * PreviewScale;

            var cg = rowRT.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            // Small dim eye dot so previews still read as HAL lines.
            var dotRT = NewUI("EyeDot", rowRT);
            dotRT.anchorMin = new Vector2(0f, 0.5f);
            dotRT.anchorMax = new Vector2(0f, 0.5f);
            dotRT.pivot     = new Vector2(0f, 0.5f);
            dotRT.anchoredPosition = new Vector2(24f, 0f);
            dotRT.sizeDelta = new Vector2(11f, 11f);
            var dot = dotRT.gameObject.AddComponent<Image>();
            dot.sprite = HALVisuals.Disc();
            dot.color = new Color(HALVisuals.EyeRed.r, HALVisuals.EyeRed.g, HALVisuals.EyeRed.b, 0.85f);
            dot.raycastTarget = false;

            var textRT = NewUI("PreviewText", rowRT);
            textRT.anchorMin = new Vector2(0f, 0f);
            textRT.anchorMax = new Vector2(1f, 1f);
            textRT.offsetMin = new Vector2(48f, 0f);
            textRT.offsetMax = new Vector2(-24f, 0f);
            var lbl = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            HudFontResolver.Apply(lbl);
            lbl.fontSize = 21f;
            lbl.color = HalText;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.raycastTarget = false;
            lbl.enableWordWrapping = false;
            lbl.overflowMode = TextOverflowModes.Ellipsis;

            _prevRT[i]    = rowRT;
            _prevCG[i]    = cg;
            _prevLabel[i] = lbl;
        }
    }

    // Pulse the HAL eye on a slow sine — matches the chat-header eye in
    // AIChatScreen so HAL's visual identity is consistent across the two
    // surfaces. Core and halo are counter-phase so the silhouette never
    // reads as flat. Image-alpha multiplies with the CanvasGroup alpha,
    // so this happily co-exists with the fade-in / hold / fade-out cycle.
    // Also drives live-text lines (the hull-sealed countdown).
    bool _wasUserHidden;

    void Update()
    {
        // If the player flips "HIDE HUD" on while a tip is showing/queued, drop it
        // immediately (text + voice) so nothing lingers in a recorded frame.
        bool userHidden = HudVisibility.UserHidden;
        if (userHidden && !_wasUserHidden && !IsIdle) ClearAll();
        _wasUserHidden = userHidden;

        if (_activeLive != null && _label != null)
        {
            string live = SafeEval(_activeLive);
            if (live != null) _label.text = live;
        }

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
