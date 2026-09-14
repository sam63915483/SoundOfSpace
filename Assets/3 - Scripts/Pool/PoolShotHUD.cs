using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The pool shot's HUD: one power bar under the crosshair spot (the
/// FishingTensionHUD bracket language) that only shows while charging, one dim
/// hint line at the bottom, and — since 2026-09-14 — the game layer: a tray of
/// the balls YOU sank this game (bottom centre, where the hotbar sits when the
/// helmet HUD is up), a SOLIDS / STRIPES label that flashes in when your group
/// is decided, and the YOU WIN / YOU LOSE banner (mockup A: bracketed headline,
/// reason line, a thin timer draining down to the re-rack).
///
/// Everything is drawn in code — ball icons are the shared HALVisuals disc
/// sprite tinted the builder's colours (a white disc + a squashed coloured disc
/// for stripes) with a white number spot. Built by PoolShotSession, lives as a
/// child of the table (no auto-singleton, no DontDestroyOnLoad).
/// </summary>
public class PoolShotHUD : MonoBehaviour
{
    const float BarWidth = 190f;
    const float BarHeight = 6f;
    const float BelowCentre = 86f;
    const float BracketArm = 7f;
    const float BracketThick = 2f;
    const float FadeSpeed = 9f;
    const float HotFrom = 0.85f;
    // tray (bottom centre, where the hotbar sits when the HUD is up)
    const float TrayY = 72f;
    const float IconSize = 34f;
    const float IconGap = 6f;
    const float NumberSpot = 15f;
    const float LabelAbove = 30f;
    // banner (mockup A)
    const float BannerFromTop = 0.30f;
    const float BannerBracketArm = 14f;
    const float BannerBracketThick = 2f;
    const float DrainWidth = 120f;
    static readonly Color Hot = new Color(0.94f, 0.26f, 0.18f, 1f);
    // the builder's ball colours (Editor-only code, so copied here)
    static readonly Color[] BallColors =
    {
        Color.white,
        new Color(0.98f, 0.80f, 0.12f), new Color(0.12f, 0.32f, 0.85f), new Color(0.86f, 0.14f, 0.12f),
        new Color(0.46f, 0.20f, 0.66f), new Color(0.96f, 0.50f, 0.10f), new Color(0.10f, 0.55f, 0.26f),
        new Color(0.56f, 0.12f, 0.16f), new Color(0.06f, 0.06f, 0.06f),
    };

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _bar;
    Image _fill;
    Image[] _brackets;
    TextMeshProUGUI _hint;
    float _barShown, _barWant, _charge;
    // tray
    RectTransform _tray;
    RectTransform _baseline;
    readonly System.Collections.Generic.List<RectTransform> _icons = new System.Collections.Generic.List<RectTransform>();
    readonly System.Collections.Generic.List<int> _trayBalls = new System.Collections.Generic.List<int>();
    TextMeshProUGUI _groupLabel;
    Image[] _groupBrackets;
    float _groupFlash;              // >0 while the reveal flash runs (seconds left)
    // banner
    RectTransform _banner;
    CanvasGroup _bannerGroup;
    TextMeshProUGUI _bannerBig, _bannerSub;
    Image _drain;
    Image[] _bannerBrackets;
    float _bannerT = -1f, _bannerSeconds;

    public static PoolShotHUD Create(Transform parent)
    {
        var go = new GameObject("PoolShotHUD");
        go.transform.SetParent(parent, false);
        var hud = go.AddComponent<PoolShotHUD>();
        hud.Build();
        return hud;
    }

    public void SetVisible(bool on)
    {
        if (_group != null) _group.alpha = on ? 1f : 0f;
        if (_canvas != null) _canvas.enabled = on;
        if (!on) HideResult();
    }
    public void SetCharge(float charge01, bool charging) { _charge = Mathf.Clamp01(charge01); _barWant = charging ? 1f : 0f; }
    public void SetHint(string text) { if (_hint != null && _hint.text != text) _hint.text = text; }

    /// The balls this player has sunk this game, in order. Rebuilds icons only when the list changed.
    public void SetTray(System.Collections.Generic.IReadOnlyList<int> balls)
    {
        if (_tray == null || balls == null) return;
        bool same = balls.Count == _trayBalls.Count;
        for (int i = 0; same && i < balls.Count; i++) same = balls[i] == _trayBalls[i];
        if (same) return;
        _trayBalls.Clear(); _trayBalls.AddRange(balls);
        RebuildTray();
    }

    public void SetGroup(PoolGameState.Group g, bool animate)
    {
        if (_groupLabel == null) return;
        string want = g == PoolGameState.Group.Solids ? "SOLIDS" : g == PoolGameState.Group.Stripes ? "STRIPES" : "";
        if (_groupLabel.text == want) return;
        _groupLabel.text = want;
        _groupLabel.color = HelmetHudPalette.Accent;
        _groupLabel.rectTransform.localScale = Vector3.one;
        _groupFlash = animate && want.Length > 0 ? 1f : 0f;
        if (_groupBrackets != null) foreach (var b in _groupBrackets) if (b != null) b.enabled = _groupFlash > 0f;
    }

    public void ShowResult(bool win, string reason, float seconds)
    {
        if (_banner == null) return;
        Color c = win ? (Color)HelmetHudPalette.Accent : Hot;
        _bannerBig.text = win ? "YOU WIN" : "YOU LOSE";
        _bannerBig.color = c;
        _bannerSub.text = reason;
        _bannerSub.color = new Color(c.r, c.g, c.b, 0.8f);
        _drain.color = c;
        foreach (var b in _bannerBrackets) if (b != null) b.color = c;
        _bannerSeconds = Mathf.Max(0.3f, seconds);
        _bannerT = 0f;
        _bannerGroup.alpha = 0f;
        _banner.gameObject.SetActive(true);
    }

    public void HideResult()
    {
        _bannerT = -1f;
        if (_banner != null) _banner.gameObject.SetActive(false);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        TickBar(dt);
        TickGroup(dt);
        TickBanner(dt);
    }

    void TickBar(float dt)
    {
        _barShown = Mathf.MoveTowards(_barShown, _barWant, FadeSpeed * dt);
        if (_bar == null) return;
        bool show = _barShown > 0.001f;
        if (_bar.gameObject.activeSelf != show) _bar.gameObject.SetActive(show);
        if (!show) return;
        var cg = _bar.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = _barShown;
        if (_fill != null)
        {
            _fill.rectTransform.sizeDelta = new Vector2(BarWidth * _charge, BarHeight);
            _fill.color = ColorFor(_charge);
        }
        Color bracket = _charge >= HotFrom ? ColorFor(_charge) : HelmetHudPalette.AccentGlow;
        if (_brackets != null) foreach (var b in _brackets) if (b != null) b.color = bracket;
    }

    // The reveal: brackets pop around the label, it starts white and oversized, settles to the accent over a second.
    void TickGroup(float dt)
    {
        if (_groupLabel == null || _groupFlash <= 0f) return;
        _groupFlash = Mathf.Max(0f, _groupFlash - dt);
        float k = 1f - _groupFlash;                                   // 0 → 1 over one second
        float pop = Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(k / 0.35f));
        _groupLabel.rectTransform.localScale = Vector3.one * pop;
        _groupLabel.color = Color.Lerp(Color.white, HelmetHudPalette.Accent, Mathf.Clamp01(k));
        if (_groupFlash <= 0f && _groupBrackets != null) foreach (var b in _groupBrackets) if (b != null) b.enabled = false;
    }

    void TickBanner(float dt)
    {
        if (_bannerT < 0f || _banner == null) return;
        _bannerT += dt;
        float inK = Mathf.Clamp01(_bannerT / 0.25f);
        float outK = Mathf.Clamp01((_bannerSeconds - _bannerT) / 0.2f);
        _bannerGroup.alpha = Mathf.Min(inK, outK);
        _banner.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, inK);
        float drain = Mathf.Clamp01(1f - _bannerT / _bannerSeconds);
        _drain.rectTransform.sizeDelta = new Vector2(DrainWidth * drain, 2f);
        if (_bannerT >= _bannerSeconds + 0.05f) HideResult();
    }

    static Color ColorFor(float t)
    {
        Color calm = HelmetHudPalette.Accent;
        if (t <= HotFrom) return calm;
        return Color.Lerp(calm, Hot, Mathf.InverseLerp(HotFrom, 1f, t));
    }

    void Build()
    {
        var canvasGo = new GameObject("PoolCanvas", typeof(Canvas), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 180;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _group = canvasGo.GetComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        _bar = NewRect("PowerBar", canvasGo.transform);
        _bar.gameObject.AddComponent<CanvasGroup>();
        _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0.5f);
        _bar.pivot = new Vector2(0.5f, 0.5f);
        _bar.anchoredPosition = new Vector2(0f, -BelowCentre);
        _bar.sizeDelta = new Vector2(BarWidth, BarHeight);

        var track = NewImage("Track", _bar, new Vector2(BarWidth, BarHeight), new Vector2(0.5f, 0.5f), Vector2.zero);
        track.color = new Color(0f, 0f, 0f, 0.42f);
        _fill = NewImage("Fill", _bar, new Vector2(0f, BarHeight), new Vector2(0f, 0.5f), new Vector2(-BarWidth * 0.5f, 0f));
        _fill.color = HelmetHudPalette.Accent;

        _brackets = Brackets(_bar, BarWidth * 0.5f + 4f, BarHeight * 0.5f + 3f, BracketArm, BracketThick);
        foreach (var b in _brackets) b.color = HelmetHudPalette.AccentGlow;
        _bar.gameObject.SetActive(false);

        var hintRt = NewRect("Hint", canvasGo.transform);
        hintRt.anchorMin = new Vector2(0.5f, 0f); hintRt.anchorMax = new Vector2(0.5f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 26f);
        hintRt.sizeDelta = new Vector2(1200f, 30f);
        _hint = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_hint);
        _hint.fontSize = 17f;
        _hint.alignment = TextAlignmentOptions.Center;
        _hint.color = HelmetHudPalette.AccentGlow;
        _hint.text = "";

        // ── tray: the balls you sank this game ──
        _tray = NewRect("Tray", canvasGo.transform);
        _tray.anchorMin = _tray.anchorMax = new Vector2(0.5f, 0f);
        _tray.pivot = new Vector2(0.5f, 0f);
        _tray.anchoredPosition = new Vector2(0f, TrayY);
        _tray.sizeDelta = new Vector2(IconSize, IconSize);
        // One hairline under the icons (the hotbar's grounding trick) so an empty tray still reads as a tray.
        var baseline = NewImage("Baseline", _tray, new Vector2(IconSize + 24f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f));
        baseline.rectTransform.anchorMin = baseline.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        baseline.color = HelmetHudPalette.AccentGlow;
        _baseline = baseline.rectTransform;

        var groupRt = NewRect("Group", canvasGo.transform);
        groupRt.anchorMin = groupRt.anchorMax = new Vector2(0.5f, 0f);
        groupRt.pivot = new Vector2(0.5f, 0.5f);
        groupRt.anchoredPosition = new Vector2(0f, TrayY + IconSize + LabelAbove);
        groupRt.sizeDelta = new Vector2(240f, 24f);
        _groupLabel = groupRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_groupLabel);
        _groupLabel.fontSize = 15f;
        _groupLabel.characterSpacing = 12f;
        _groupLabel.alignment = TextAlignmentOptions.Center;
        _groupLabel.color = HelmetHudPalette.Accent;
        _groupLabel.raycastTarget = false;
        _groupLabel.text = "";
        _groupBrackets = Brackets(groupRt, 52f, 11f, BracketArm, BracketThick);
        foreach (var b in _groupBrackets) { b.color = HelmetHudPalette.AccentGlow; b.enabled = false; }

        // ── win / lose banner (mockup A: bracketed headline) ──
        _banner = NewRect("Banner", canvasGo.transform);
        _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
        _banner.anchorMin = _banner.anchorMax = new Vector2(0.5f, 1f - BannerFromTop);
        _banner.pivot = new Vector2(0.5f, 0.5f);
        _banner.anchoredPosition = Vector2.zero;
        _banner.sizeDelta = new Vector2(520f, 120f);
        var bigRt = NewRect("Big", _banner);
        bigRt.anchorMin = bigRt.anchorMax = new Vector2(0.5f, 0.5f); bigRt.pivot = new Vector2(0.5f, 0.5f);
        bigRt.anchoredPosition = new Vector2(0f, 14f); bigRt.sizeDelta = new Vector2(520f, 80f);
        _bannerBig = bigRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_bannerBig);
        _bannerBig.fontSize = 64f; _bannerBig.fontStyle = FontStyles.Bold; _bannerBig.characterSpacing = 14f;
        _bannerBig.alignment = TextAlignmentOptions.Center;
        _bannerBig.raycastTarget = false;
        var subRt = NewRect("Sub", _banner);
        subRt.anchorMin = subRt.anchorMax = new Vector2(0.5f, 0.5f); subRt.pivot = new Vector2(0.5f, 0.5f);
        subRt.anchoredPosition = new Vector2(0f, -30f); subRt.sizeDelta = new Vector2(520f, 24f);
        _bannerSub = subRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_bannerSub);
        _bannerSub.fontSize = 16f; _bannerSub.characterSpacing = 6f;
        _bannerSub.alignment = TextAlignmentOptions.Center;
        _bannerSub.raycastTarget = false;
        _drain = NewImage("Drain", _banner, new Vector2(DrainWidth, 2f), new Vector2(0.5f, 0.5f), new Vector2(0f, -56f));
        _bannerBrackets = Brackets(_banner, 260f, 60f, BannerBracketArm, BannerBracketThick);
        _banner.gameObject.SetActive(false);
    }

    void RebuildTray()
    {
        foreach (var rt in _icons) if (rt != null) Destroy(rt.gameObject);
        _icons.Clear();
        int n = _trayBalls.Count;
        float total = n * IconSize + Mathf.Max(0, n - 1) * IconGap;
        float width = Mathf.Max(IconSize, total);
        _tray.sizeDelta = new Vector2(width, IconSize);
        if (_baseline != null) _baseline.sizeDelta = new Vector2(width + 24f, 1f);
        float x = -total * 0.5f + IconSize * 0.5f;
        for (int i = 0; i < n; i++, x += IconSize + IconGap)
            _icons.Add(BallIcon(_trayBalls[i], _tray, new Vector2(x, IconSize * 0.5f)));
    }

    /// A code-drawn pool ball: coloured disc (solids), white disc with a coloured band (stripes), black 8, plus a white number spot.
    static RectTransform BallIcon(int ball, RectTransform parent, Vector2 pos)
    {
        bool stripe = ball >= 9;
        Color c = ball == 8 ? BallColors[8] : BallColors[stripe ? ball - 8 : ball];
        var disc = NewImage("Ball_" + ball, parent, new Vector2(IconSize, IconSize), new Vector2(0.5f, 0.5f), pos);
        disc.rectTransform.anchorMin = disc.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        disc.sprite = HALVisuals.Disc();
        disc.color = stripe ? new Color(0.96f, 0.96f, 0.93f) : c;
        if (stripe)
        {
            var band = NewImage("Band", disc.rectTransform, new Vector2(IconSize, IconSize), new Vector2(0.5f, 0.5f), Vector2.zero);
            band.sprite = HALVisuals.Disc();
            band.color = c;
            band.rectTransform.localScale = new Vector3(1f, 0.42f, 1f);
        }
        var spot = NewImage("Spot", disc.rectTransform, new Vector2(NumberSpot, NumberSpot), new Vector2(0.5f, 0.5f), Vector2.zero);
        spot.sprite = HALVisuals.Disc();
        spot.color = Color.white;
        var numRt = NewRect("Num", spot.rectTransform);
        numRt.anchorMin = numRt.anchorMax = new Vector2(0.5f, 0.5f); numRt.pivot = new Vector2(0.5f, 0.5f);
        numRt.sizeDelta = new Vector2(NumberSpot + 4f, NumberSpot);
        var num = numRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(num);
        num.fontSize = 11f; num.fontStyle = FontStyles.Bold;
        num.alignment = TextAlignmentOptions.Center;
        num.color = new Color(0.08f, 0.08f, 0.1f, 1f);
        num.text = ball.ToString();
        num.raycastTarget = false;
        return disc.rectTransform;
    }

    /// Four corner brackets (top-left and bottom-right pairs, the power bar's language) around a centred box.
    static Image[] Brackets(RectTransform parent, float halfW, float halfH, float arm, float thick)
    {
        return new[]
        {
            NewImage("BrL0", parent, new Vector2(arm, thick), new Vector2(0f, 0.5f), new Vector2(-halfW, halfH)),
            NewImage("BrL1", parent, new Vector2(thick, arm), new Vector2(0f, 1f), new Vector2(-halfW, halfH)),
            NewImage("BrR0", parent, new Vector2(arm, thick), new Vector2(1f, 0.5f), new Vector2(halfW, -halfH)),
            NewImage("BrR1", parent, new Vector2(thick, arm), new Vector2(1f, 0f), new Vector2(halfW, -halfH)),
        };
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
