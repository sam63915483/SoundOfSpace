using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The one chip that shows the running cat perk and its countdown. There is
/// never more than one, because there is never more than one perk.
///
/// Sits under the helmet HUD layer on the left, and is simply not present when
/// no perk is running — a permanently-visible empty slot would advertise a
/// system the player mostly isn't using.
///
/// Per-frame string building is change-gated: the label is only rewritten when
/// the displayed second actually changes, not every frame.
/// </summary>
public class CatPerkHUD : MonoBehaviour
{
    public static CatPerkHUD Instance { get; private set; }

    Canvas _canvas;
    RectTransform _chip;
    TextMeshProUGUI _label;
    Image _barFill, _border;
    int _shownSecond = -1;
    CatPerkKind _shownKind = CatPerkKind.None;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[CatPerkHUD]");
        DontDestroyOnLoad(go);
        go.AddComponent<CatPerkHUD>();
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
        // No GraphicRaycaster: this is a readout, and a stray raycast target
        // over the HUD is how invisible click-blockers get born.

        _chip = new GameObject("Chip", typeof(RectTransform)).GetComponent<RectTransform>();
        _chip.SetParent(canvasGO.transform, false);
        _chip.anchorMin = new Vector2(0f, 1f);
        _chip.anchorMax = new Vector2(0f, 1f);
        _chip.pivot = new Vector2(0f, 1f);
        _chip.anchoredPosition = new Vector2(28f, -120f);
        _chip.sizeDelta = new Vector2(250f, 30f);

        var bg = _chip.gameObject.AddComponent<Image>();
        bg.color = new Color(0.016f, 0.063f, 0.043f, 0.86f);
        bg.raycastTarget = false;

        _label = PhosphorUI.MakeLabel(_chip, "Label", "", 14f, PhosphorUI.Phosphor);
        var lrt = _label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(9f, 0f); lrt.offsetMax = new Vector2(-9f, -6f);
        _label.alignment = TextAlignmentOptions.Left;

        // countdown bar along the bottom edge
        var barBg = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
        barBg.SetParent(_chip, false);
        barBg.anchorMin = new Vector2(0f, 0f); barBg.anchorMax = new Vector2(1f, 0f);
        barBg.offsetMin = new Vector2(9f, 4f); barBg.offsetMax = new Vector2(-9f, 6.5f);
        var bimg = barBg.gameObject.AddComponent<Image>();
        bimg.color = new Color(0.05f, 0.16f, 0.12f, 1f);
        bimg.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
        fill.SetParent(barBg, false);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
        fill.pivot = new Vector2(0f, 0.5f);
        _barFill = fill.gameObject.AddComponent<Image>();
        _barFill.raycastTarget = false;

        _border = bg;
        _chip.gameObject.SetActive(false);
    }

    void Update()
    {
        var m = CatPerkManager.Instance;
        bool show = m != null && m.HasPerk;
        if (_chip.gameObject.activeSelf != show)
        {
            _chip.gameObject.SetActive(show);
            _shownSecond = -1;
            _shownKind = CatPerkKind.None;
        }
        if (!show) return;

        float f = m.TotalSeconds > 0.01f ? Mathf.Clamp01(m.SecondsLeft / m.TotalSeconds) : 0f;
        _barFill.rectTransform.localScale = new Vector3(f, 1f, 1f);

        int sec = Mathf.CeilToInt(m.SecondsLeft);
        if (sec == _shownSecond && m.ActiveKind == _shownKind) return;   // change-gated
        _shownSecond = sec;
        _shownKind = m.ActiveKind;

        Color c = CatPerkManager.TierColor(m.ActiveTier);
        _barFill.color = c;
        _label.color = c;
        _border.color = new Color(c.r * 0.10f, c.g * 0.16f, c.b * 0.13f, 0.86f);
        _label.text = $"{CatPerkManager.DisplayName(m.ActiveKind)}   " +
                      $"<color=#4A6E59>{sec / 60}:{(sec % 60):00}</color>";
    }
}
