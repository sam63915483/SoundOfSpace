using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The pool shot's HUD: one power bar under the crosshair spot (the
/// FishingTensionHUD bracket language) that only shows while charging, and one
/// dim hint line at the bottom. Built in code by PoolShotSession, lives as a
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

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _bar;
    Image _fill;
    Image[] _brackets;
    TextMeshProUGUI _hint;
    float _barShown, _barWant, _charge;

    public static PoolShotHUD Create(Transform parent)
    {
        var go = new GameObject("PoolShotHUD");
        go.transform.SetParent(parent, false);
        var hud = go.AddComponent<PoolShotHUD>();
        hud.Build();
        return hud;
    }

    public void SetVisible(bool on) { if (_group != null) _group.alpha = on ? 1f : 0f; if (_canvas != null) _canvas.enabled = on; }
    public void SetCharge(float charge01, bool charging) { _charge = Mathf.Clamp01(charge01); _barWant = charging ? 1f : 0f; }
    public void SetHint(string text) { if (_hint != null && _hint.text != text) _hint.text = text; }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
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

    static Color ColorFor(float t)
    {
        Color calm = HelmetHudPalette.Accent;
        Color hot = new Color(0.94f, 0.26f, 0.18f, 1f);
        if (t <= HotFrom) return calm;
        return Color.Lerp(calm, hot, Mathf.InverseLerp(HotFrom, 1f, t));
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

        float halfW = BarWidth * 0.5f + 4f, halfH = BarHeight * 0.5f + 3f;
        _brackets = new[]
        {
            NewImage("BrL0", _bar, new Vector2(BracketArm, BracketThick), new Vector2(0f, 0.5f), new Vector2(-halfW, halfH)),
            NewImage("BrL1", _bar, new Vector2(BracketThick, BracketArm), new Vector2(0f, 1f), new Vector2(-halfW, halfH)),
            NewImage("BrR0", _bar, new Vector2(BracketArm, BracketThick), new Vector2(1f, 0.5f), new Vector2(halfW, -halfH)),
            NewImage("BrR1", _bar, new Vector2(BracketThick, BracketArm), new Vector2(1f, 0f), new Vector2(halfW, -halfH)),
        };
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
