using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The chip that shows the firefly glow and its countdown — a copy of
/// <see cref="CatPerkHUD"/> in firefly amber, so the two status effects read
/// as one family. Sits directly under the cat-perk chip when that one is
/// showing and takes its place when it is not, so neither ever overlaps.
/// Absent entirely while nothing is glowing.
///
/// Per-frame string building is change-gated: the label is rewritten only
/// when the displayed second changes.
/// </summary>
public class FireflyGlowHUD : MonoBehaviour
{
    public static FireflyGlowHUD Instance { get; private set; }

    static readonly Color Amber = new Color32(0xFF, 0xC0, 0x4A, 0xFF);
    const float TopWithoutCat = -120f;   // CatPerkHUD's own row
    const float TopUnderCat   = -156f;   // one 30 px chip + a 6 px gap below it

    Canvas _canvas;
    RectTransform _chip;
    TextMeshProUGUI _label;
    Image _barFill;
    int _shownSecond = -1;
    bool _underCat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[FireflyGlowHUD]");
        DontDestroyOnLoad(go);
        go.AddComponent<FireflyGlowHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        var canvasGO = new GameObject("Canvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Hud;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: a readout, never a click target.

        _chip = new GameObject("Chip", typeof(RectTransform)).GetComponent<RectTransform>();
        _chip.SetParent(canvasGO.transform, false);
        _chip.anchorMin = new Vector2(0f, 1f);
        _chip.anchorMax = new Vector2(0f, 1f);
        _chip.pivot = new Vector2(0f, 1f);
        _chip.anchoredPosition = new Vector2(28f, TopWithoutCat);
        _chip.sizeDelta = new Vector2(250f, 30f);

        var bg = _chip.gameObject.AddComponent<Image>();
        bg.color = new Color(Amber.r * 0.10f, Amber.g * 0.16f, Amber.b * 0.13f, 0.86f);
        bg.raycastTarget = false;

        _label = PhosphorUI.MakeLabel(_chip, "Label", "", 14f, Amber);
        var lrt = _label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(9f, 0f); lrt.offsetMax = new Vector2(-9f, -6f);
        _label.alignment = TextAlignmentOptions.Left;

        var barBg = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
        barBg.SetParent(_chip, false);
        barBg.anchorMin = new Vector2(0f, 0f); barBg.anchorMax = new Vector2(1f, 0f);
        barBg.offsetMin = new Vector2(9f, 4f); barBg.offsetMax = new Vector2(-9f, 6.5f);
        var bimg = barBg.gameObject.AddComponent<Image>();
        bimg.color = new Color(0.16f, 0.12f, 0.05f, 1f);
        bimg.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
        fill.SetParent(barBg, false);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
        fill.pivot = new Vector2(0f, 0.5f);
        _barFill = fill.gameObject.AddComponent<Image>();
        _barFill.color = Amber;
        _barFill.raycastTarget = false;

        _chip.gameObject.SetActive(false);
    }

    void Update()
    {
        var g = FireflyGlow.Instance;
        bool show = g != null && g.IsActive && !HUDSceneGate.InMainMenu;
        if (_chip.gameObject.activeSelf != show)
        {
            _chip.gameObject.SetActive(show);
            _shownSecond = -1;
        }
        if (!show) return;

        // Step down under the cat chip while a cat perk is running.
        var cat = CatPerkManager.Instance;
        bool underCat = cat != null && cat.HasPerk;
        if (underCat != _underCat || _shownSecond < 0)
        {
            _underCat = underCat;
            _chip.anchoredPosition = new Vector2(28f, underCat ? TopUnderCat : TopWithoutCat);
        }

        float f = g.TotalSeconds > 0.01f ? Mathf.Clamp01(g.SecondsLeft / g.TotalSeconds) : 0f;
        _barFill.rectTransform.localScale = new Vector3(f, 1f, 1f);

        int sec = Mathf.CeilToInt(g.SecondsLeft);
        if (sec == _shownSecond) return;   // change-gated
        _shownSecond = sec;
        _label.text = $"FIREFLY GLOW   <color=#8A6A2A>{sec / 60}:{(sec % 60):00}</color>";
    }
}
