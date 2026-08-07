using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// The progression toast — "variant D" from the mockups, sitting directly under
/// the compass strip: a spaced-out track name, the gain on the right, and a
/// 12-segment level bar underneath.
///
/// NO panel and NO outline. The vitals / boost / compass readouts are not cards
/// — VitalsHUD.ApplyIntegratedStyle DISABLES their beveled background, border
/// and bezels once the helmet HUD is on, so they render as transparent light
/// cyan straight onto the visor glass. This matches that: a 10% accent wash for
/// legibility, repeating scanlines, and nothing else behind the content.
///
/// It also arrives and leaves the way the other clusters do — HudBootFX scans it
/// ON (the same power-on GForceHUD plays for the BOOST cluster) and ScanOut
/// mirrors it to wipe the readout back off.
///
/// Repeat actions COLLAPSE. Felling trees back to back would otherwise stack a
/// tower of near-identical toasts, so a second hit on a track that's already
/// showing re-uses the live toast, sums the gain (+1 → +2 → +5 for an Elite)
/// and restarts its timer. Level-ups always get their own uncollapsed toast.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
public class ProgressToastUI : MonoBehaviour
{
    public static ProgressToastUI Instance { get; private set; }

    const int   Segments      = 12;      // level-bar ticks
    const float HoldSeconds   = 2.6f;    // visible time before the fade
    const float RiseSeconds   = 0.28f;
    const int   MaxOnScreen   = 4;

    // Timing is split into FILL (the bar moving) and HOLD (sitting still),
    // as absolute seconds rather than a chain of multipliers — the two need to
    // be tuned against each other and multipliers made that guesswork.
    //
    // The bar is the only thing on the toast that moves, so it should be running
    // for most of the time the toast is up. Dead time after it finishes is what
    // makes the toast feel like it's overstaying.
    const float BarFillSeconds      = 1.4f;   // normal +1: fills for 1.4 of its 2.6s hold
    const float LevelUpFillOut      = 0.8f;   // old level running out to full
    const float LevelUpWrapPause    = 0.15f;  // beat at the wrap
    const float LevelUpFillIn       = 0.9f;   // carry-over filling on the new level
    const float LevelUpHoldSeconds  = 4f;     // ~1.95s of that is bar movement

    // Scanline opacity over the toast body. Deliberately faint — these read as
    // CRT texture on the visor, not as content.
    const float ScanlineAlpha = 0.08f;

    RectTransform _root;      // vertical stack, top-centre
    Canvas _canvas;
    readonly List<Toast> _live = new List<Toast>();

    // Gap between the bottom of the compass strip and the first toast.
    const float CompassGap = 14f;
    const float ToastWidth = 420f;
    const float StackSpacing = 8f;

    // ── Palette ──────────────────────────────────────────────────────────────
    // Text colours copied from VitalsHUD's rows. The accent routes through
    // HelmetHudPalette — the same source CompassHUD's sheen and the vitals LED
    // read — so retuning the helmet accent in HelmetHudConfig retints the toast
    // along with everything else, instead of leaving it stranded on a hardcoded
    // hex the way the first two attempts at this did.
    static readonly Color HeaderCyan  = new Color32(0x5C, 0xC8, 0xFF, 0xD9); // VitalsHUD.HeaderColor
    static readonly Color LabelText   = new Color32(0xEA, 0xF6, 0xFF, 0xFF); // VitalsHUD.LabelColor
    static readonly Color TrackBg     = new Color32(0x0F, 0x19, 0x2A, 0xD9); // VitalsHUD.TrackColor — unlit bar
    static Color Accent => HelmetHudPalette.Accent;                          // same source as the compass sheen

    // Scanline spacing in UI units — one bright row every 4.
    const float ScanlinePeriod = 4f;
    static Texture2D _scanTex;
    static Texture2D ScanlineTexture()
    {
        if (_scanTex != null) return _scanTex;
        _scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,   // required — uvRect.height > 1 tiles it
            filterMode = FilterMode.Point,       // keeps the lines crisp instead of mushy
            name = "ProgressToastScanlines"
        };
        _scanTex.SetPixels(new[]
        {
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 0f),
            new Color(1f, 1f, 1f, 0f),
            new Color(1f, 1f, 1f, 0f),
        });
        _scanTex.Apply();
        return _scanTex;
    }

    // The toast now draws in the HUD cyan family by default so it belongs with
    // the compass / vitals / boost readouts. Set true to bring back the
    // per-track colours (orange Tree Killer, green Tree Daddy, …) — they still
    // exist on the phone Levels page either way, which is where having five
    // distinguishable colours actually earns its keep.
    const bool UseTrackColours = false;
    // Used until the compass reports a rect (it builds a frame or two later, and
    // it's hidden entirely in some scenes).
    const float FallbackTopOffset = -96f;
    float _nextReposition;

    // One live toast. Kept as a small class rather than a MonoBehaviour so the
    // whole thing is built and torn down without prefabs.
    class Toast
    {
        public ProgressTrack track;
        public bool          levelUp;
        public int           gain;
        public RectTransform rt;
        public CanvasGroup   group;
        public TextMeshProUGUI nameText, levelText, gainText;
        public Image[]       segs;
        public Coroutine     life;
        public Coroutine     bar;
        public float         shownProgress;   // what the bar is CURRENTLY drawing, 0..1
        public float         gainedFrom;      // segments past this point were earned by THIS action
        public Color         accent;
        public float         stackY;          // slot offset from the top of the stack
        public float         rise;            // spawn animation offset, 14 → 0
        public float         height;
        public GameObject    scanBar;         // sweep bar for the scan-off; sibling, so tracked for cleanup
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("ProgressToastUI");
        DontDestroyOnLoad(go);
        go.AddComponent<ProgressToastUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
    }

    void OnEnable()  { PlayerProgress.OnTrackChanged += HandleTrackChanged; }
    void OnDisable() { PlayerProgress.OnTrackChanged -= HandleTrackChanged; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildCanvas()
    {
        var canvasGO = new GameObject("ProgressToastCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;          // above the vitals HUD, below modal UI
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster — this is pure output and must never eat clicks.
        HUDSceneGate.Register(canvas.rootCanvas);
        HudVisibility.RegisterHideable(canvas.rootCanvas);

        _root = new GameObject("Stack", typeof(RectTransform)).GetComponent<RectTransform>();
        _root.SetParent(canvasGO.transform, false);
        _root.anchorMin = new Vector2(0.5f, 1f);
        _root.anchorMax = new Vector2(0.5f, 1f);
        _root.pivot     = new Vector2(0.5f, 1f);
        _root.anchoredPosition = new Vector2(0f, FallbackTopOffset);
        _root.sizeDelta = new Vector2(520f, 0f);
        _canvas = canvas;

        // NO VerticalLayoutGroup here — deliberately. A LayoutGroup OWNS its
        // children's anchors and anchoredPosition: it re-anchors every child to
        // the parent's TOP-LEFT and positions them itself. The rise animation
        // writes anchoredPosition directly, so `Vector2.zero` put each toast's
        // top-left corner on the stack's top-left corner — i.e. hard against the
        // left edge instead of centred. That was the "too far left" bug.
        //
        // The stack is four items at most and I already hold them in _live, so
        // Reflow() below just places them. No layout system, nothing to fight.
    }

    // Positions every live toast under the previous one, centred on _root.
    // Called whenever the list changes.
    void Reflow()
    {
        float y = 0f;
        for (int i = 0; i < _live.Count; i++)
        {
            var t = _live[i];
            t.stackY = y;
            ApplyPosition(t);
            y += t.height + StackSpacing;
        }
    }

    // Single place that writes a toast's position: its slot in the stack plus
    // whatever the rise animation is currently offsetting it by.
    static void ApplyPosition(Toast t)
    {
        if (t.rt == null) return;
        t.rt.anchoredPosition = new Vector2(0f, -t.stackY + t.rise);
    }

    void HandleTrackChanged(ProgressTrack track, int delta, bool leveledUp)
    {
        if (leveledUp) { Spawn(track, delta, true); return; }

        // Collapse into a live, non-level-up toast for the same track.
        for (int i = 0; i < _live.Count; i++)
        {
            var t = _live[i];
            if (t.track != track || t.levelUp) continue;
            t.gain += delta;
            Refresh(t);
            // Animate on from wherever the bar had got to, not from scratch.
            StartBar(t, t.shownProgress, TargetProgress(t.track), false);
            if (t.life != null) StopCoroutine(t.life);
            t.life = StartCoroutine(Life(t));
            return;
        }
        Spawn(track, delta, false);
    }

    void Spawn(ProgressTrack track, int delta, bool levelUp)
    {
        var t = Build(track, levelUp);
        t.gain = delta;

        // The bar ALWAYS charges from empty on a fresh toast. Animating only the
        // delta was technically honest but looked static — most actions move the
        // bar by a twelfth of a segment, so nothing appeared to happen. Sweeping
        // 0 → current every time reads as the suit taking a reading, and the
        // amount earned is carried by the "+1" and by the gained segments, which
        // pulse brighter as they land.
        Refresh(t);
        t.shownProgress = 0f;
        PaintBar(t, 0f);

        // Segments earned by THIS action — highlighted during the sweep.
        var p = PlayerProgress.Instance;
        int scoreBefore = (p != null ? p.ScoreOf(track) : 0) - delta;
        t.gainedFrom = levelUp ? 0f : PlayerProgress.LevelProgressForScore(track, scoreBefore);

        t.rise = 14f;               // settles to 0 during the rise animation
        _live.Add(t);
        while (_live.Count > MaxOnScreen) Kill(_live[0]);
        Reflow();
        t.life = StartCoroutine(Life(t));
        StartBar(t, 0f, TargetProgress(track), levelUp);
    }

    static float TargetProgress(ProgressTrack track)
    {
        var p = PlayerProgress.Instance;
        return p != null ? p.LevelProgressOf(track) : 0f;
    }

    void StartBar(Toast t, float from, float to, bool levelUp)
    {
        if (t.bar != null) StopCoroutine(t.bar);
        t.bar = StartCoroutine(BarRoutine(t, from, to, levelUp));
    }

    // Drives the segment bar. A level-up is drawn as two moves — run the old
    // level out to FULL, then wrap to empty and fill to the carry-over — so
    // crossing a threshold reads as "the bar completed", not as it mysteriously
    // jumping backwards.
    IEnumerator BarRoutine(Toast t, float from, float to, bool levelUp)
    {
        yield return new WaitForSecondsRealtime(0.1f);   // let the toast land first

        if (levelUp)
        {
            // Run the OLD level out to full, wrap, then fill the carry-over, so
            // crossing a threshold reads as "the bar completed" rather than the
            // bar mysteriously jumping backwards.
            yield return Sweep(t, from, 1f, LevelUpFillOut);
            yield return new WaitForSecondsRealtime(LevelUpWrapPause);
            t.shownProgress = 0f;
            PaintBar(t, 0f);
            yield return Sweep(t, 0f, to, LevelUpFillIn);
        }
        else
        {
            yield return Sweep(t, from, to, BarFillSeconds);
        }
        t.bar = null;
    }

    IEnumerator Sweep(Toast t, float from, float to, float dur)
    {
        float e = 0f;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(e / dur);
            u = 1f - (1f - u) * (1f - u);            // ease-out, matches the mockup
            t.shownProgress = Mathf.Lerp(from, to, u);
            PaintBar(t, t.shownProgress);
            yield return null;
        }
        t.shownProgress = to;
        PaintBar(t, to);
    }

    // Lights the first N segments. Split out from Refresh so the animation can
    // repaint without recomputing text every frame.
    //
    // Segments past gainedFrom are the ones this action actually earned, so they
    // burn brighter than the ones that were already banked — the sweep reads as
    // "here's your total, and here's the bit you just added".
    void PaintBar(Toast t, float progress)
    {
        if (t.segs == null) return;
        float clamped = Mathf.Clamp01(progress);
        int on    = Mathf.RoundToInt(clamped * Segments);
        int owned = Mathf.RoundToInt(Mathf.Clamp01(t.gainedFrom) * Segments);
        for (int i = 0; i < t.segs.Length; i++)
        {
            if (t.segs[i] == null) continue;
            if (i >= on)
                t.segs[i].color = TrackBg;        // same unlit bar colour as the vitals rows
            else if (i >= owned)
                // Newly earned — overbright so it blooms against the banked run.
                t.segs[i].color = new Color(
                    Mathf.Min(1f, t.accent.r * 1.6f + 0.25f),
                    Mathf.Min(1f, t.accent.g * 1.6f + 0.25f),
                    Mathf.Min(1f, t.accent.b * 1.6f + 0.25f), 1f);
            else
                t.segs[i].color = t.accent;
        }
    }

    Toast Build(ProgressTrack track, bool levelUp)
    {
        var t = new Toast { track = track, levelUp = levelUp };
        Color accent = PlayerProgress.ColorOf(track);

        var go = new GameObject("Toast_" + track, typeof(RectTransform));
        t.rt = go.GetComponent<RectTransform>();
        t.rt.SetParent(_root, false);
        t.group = go.AddComponent<CanvasGroup>();
        t.group.alpha = 0f;
        t.group.blocksRaycasts = false;
        t.group.interactable = false;

        // Explicit centre anchoring. anchorMin == anchorMax == (0.5, 1) with a
        // top-centre pivot means sizeDelta IS the size and anchoredPosition.x = 0
        // is dead centre under the compass, whatever the resolution.
        t.height = levelUp ? 62f : 46f;
        t.rt.anchorMin = new Vector2(0.5f, 1f);
        t.rt.anchorMax = new Vector2(0.5f, 1f);
        t.rt.pivot     = new Vector2(0.5f, 1f);
        t.rt.sizeDelta = new Vector2(ToastWidth, t.height);
        t.rt.anchoredPosition = Vector2.zero;

        // NO panel, NO outline — this is what VitalsHUD.ApplyIntegratedStyle does
        // when the helmet HUD is on: it DISABLES the beveled bg, the border and
        // the bezels, leaving the rows rendering straight onto the visor glass.
        // That's why the vitals/boost/compass read as transparent light cyan
        // rather than as cards. A solid panel here was the whole mismatch.
        //
        // All that's left as backing is the palette's faintest accent wash (10%),
        // purely so the text survives a bright horizon.
        var wash = NewImage(t.rt, "Wash", HelmetHudPalette.AccentFaint);
        Stretch(wash.rectTransform);

        // Scanlines — the other half of the look. A 1×4 repeating texture on a
        // RawImage, tiled via uvRect, so it costs one draw call and no shader.
        var scanGo = new GameObject("Scanlines", typeof(RectTransform));
        scanGo.transform.SetParent(t.rt, false);
        Stretch((RectTransform)scanGo.transform);
        var scan = scanGo.AddComponent<RawImage>();
        scan.texture = ScanlineTexture();
        scan.color = new Color(Accent.r, Accent.g, Accent.b, ScanlineAlpha);
        scan.uvRect = new Rect(0f, 0f, 1f, t.height / ScanlinePeriod);
        scan.raycastTarget = false;

        // Track name — wide letter-spacing is the whole visual signature here.
        // Built in the vitals header cyan; Refresh() retints it if track colours
        // are enabled.
        t.nameText = NewText(t.rt, "Name", 15f, HeaderCyan, TextAlignmentOptions.Left);
        Anchor(t.nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0.6f, 1f),
               new Vector2(18f, -8f), new Vector2(-18f, -26f));
        t.nameText.characterSpacing = 18f;

        t.levelText = NewText(t.rt, "Level", 13f, LabelText,
                              TextAlignmentOptions.Left);
        Anchor(t.levelText.rectTransform, new Vector2(0.55f, 1f), new Vector2(0.8f, 1f),
               new Vector2(0f, -8f), new Vector2(0f, -26f));

        t.gainText = NewText(t.rt, "Gain", 16f, Color.white, TextAlignmentOptions.Right);
        Anchor(t.gainText.rectTransform, new Vector2(0.6f, 1f), new Vector2(1f, 1f),
               new Vector2(0f, -8f), new Vector2(-18f, -28f));
        t.gainText.fontStyle = FontStyles.Bold;

        // Segmented level bar.
        var barRow = new GameObject("Segments", typeof(RectTransform)).GetComponent<RectTransform>();
        barRow.SetParent(t.rt, false);
        Anchor(barRow, new Vector2(0f, 0f), new Vector2(1f, 0f),
               new Vector2(18f, 10f), new Vector2(-18f, 15f));
        var hlg = barRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 3f;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

        t.segs = new Image[Segments];
        for (int i = 0; i < Segments; i++)
            t.segs[i] = NewImage(barRow, "S" + i, TrackBg);

        return t;
    }

    // Repaints the TEXT of a toast. The bar is driven separately by BarRoutine
    // so an in-flight fill isn't stomped when a collapse bumps the gain.
    void Refresh(Toast t)
    {
        var p = PlayerProgress.Instance;
        t.accent = t.gain < 0            ? PlayerProgress.NegativeColor   // losing rep is always red
                 : UseTrackColours       ? PlayerProgress.ColorOf(t.track)
                                         : Accent;

        t.nameText.text = t.levelUp
            ? PlayerProgress.DisplayName(t.track) + "  ▲"
            : PlayerProgress.DisplayName(t.track);
        t.nameText.color = t.accent;

        int lv = p != null ? p.LevelOf(t.track) : 0;
        t.levelText.text = p != null && p.IsMaxed(t.track) ? "MAX" : $"LV {lv}";
        t.gainText.text  = t.gain >= 0 ? $"+{t.gain}" : t.gain.ToString();
        t.gainText.color = t.accent;
    }

    IEnumerator Life(Toast t)
    {
        // SCAN ON. HudBootFX is the project's existing "screen power-on" — an
        // alpha flicker followed by a bright accent bar sweeping down the card,
        // clipped by a RectMask2D. It's what GForceHUD plays when the BOOST
        // cluster appears, so the toast now arrives the same way every other
        // helmet readout does. It owns group.alpha for its duration, which is
        // why nothing here touches alpha — only `rise`.
        HudBootFX.Play(t.group, t.rt);

        float e = 0f;
        while (e < RiseSeconds)
        {
            e += Time.unscaledDeltaTime;                 // keep animating in slow-mo
            t.rise = Mathf.Lerp(14f, 0f, Mathf.Clamp01(e / RiseSeconds));
            ApplyPosition(t);
            yield return null;
        }
        t.rise = 0f;
        ApplyPosition(t);

        yield return new WaitForSecondsRealtime(t.levelUp ? LevelUpHoldSeconds : HoldSeconds);

        // SCAN OFF — the mirror of the boot sweep, which HudBootFX doesn't
        // provide. A bright accent bar runs down the toast and the content is
        // wiped out BEHIND it, so the line reads as erasing the readout rather
        // than the whole thing simply dimming. Ends on a one-frame flicker, the
        // same tell the power-on uses.
        yield return ScanOut(t);

        t.life = null;
        Kill(t);
    }

    IEnumerator ScanOut(Toast t)
    {
        const float BarH = 10f;
        // Parented to _root, NOT to the toast: the toast's CanvasGroup is what
        // we're dimming, and a child of it would fade out along with the content
        // it's supposed to be outrunning. As a sibling it stays bright while the
        // readout dies behind it. Tracked on the Toast so an early Kill (stack
        // overflow) can't leak it.
        var barGo = new GameObject("ScanOut", typeof(RectTransform));
        barGo.transform.SetParent(_root, false);
        t.scanBar = barGo;
        var rt = (RectTransform)barGo.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(ToastWidth, BarH);
        var img = barGo.AddComponent<Image>();
        img.raycastTarget = false;

        float travel = Mathf.Max(0f, t.height - BarH);   // stays inside the toast's span
        const float dur = 0.34f;
        Color c = Accent;
        for (float s = 0f; s < dur; s += Time.unscaledDeltaTime)
        {
            float p = Mathf.Clamp01(s / dur);
            rt.anchoredPosition = new Vector2(0f, -(t.stackY + p * travel));
            c.a = 0.85f * (1f - p * p);
            img.color = c;
            t.group.alpha = Mathf.Clamp01(1f - p * 1.15f);   // wipe trails behind the bar
            yield return null;
        }
        t.group.alpha = 0f;
        if (barGo != null) Destroy(barGo);
        t.scanBar = null;
    }

    // Keep the stack sitting just under the compass strip. Throttled — the
    // compass only moves when the layout mode changes (helmet HUD on/off) or the
    // window is resized, so there's no reason to pay for this every frame.
    void LateUpdate()
    {
        if (_root == null || Time.unscaledTime < _nextReposition) return;
        _nextReposition = Time.unscaledTime + 0.25f;

        float y = FallbackTopOffset;
        var compass = CompassHUD.Instance;
        if (compass != null && compass.TryGetStripScreenBottom(out float screenBottom))
        {
            // Screen px → this canvas' units, then express as a downward offset
            // from the top edge (the stack is anchored top-centre, pivot top).
            float scale = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
            y = -((Screen.height - screenBottom) / scale + CompassGap);
        }
        if (!Mathf.Approximately(_root.anchoredPosition.y, y))
            _root.anchoredPosition = new Vector2(0f, y);
    }

    void Kill(Toast t)
    {
        if (t.life != null) { StopCoroutine(t.life); t.life = null; }
        if (t.bar  != null) { StopCoroutine(t.bar);  t.bar  = null; }
        if (t.scanBar != null) { Destroy(t.scanBar); t.scanBar = null; }
        _live.Remove(t);
        if (t.rt != null) Destroy(t.rt.gameObject);
        Reflow();   // close the gap the removed toast left
    }

    // ── tiny UI builders ─────────────────────────────────────────────────────
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    static Image NewImage(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, float size,
                                   Color c, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(txt);
        txt.fontSize = size;
        txt.color = c;
        txt.alignment = align;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Overflow;
        return txt;
    }


}
