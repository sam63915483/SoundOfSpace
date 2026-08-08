using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover behaviour for a main-menu option row — the approved "treatment D".
///
/// On hover (or controller focus): the label goes to full white, the caret
/// brightens, a faint cyan wash fades in behind the row, the rule along the
/// bottom fills left-to-right in cyan→magenta, and a thin cyan scanline sweeps
/// down through the row once and fades out.
///
/// The scanline is the point. It is the same motif the stasis pod's DOWNLOADING
/// screen uses, which is exactly where a joining player arrives — so the menu
/// and the arrival rhyme rather than being two unrelated looks.
///
/// Responds to POINTER and to SELECT, so controller navigation lights rows the
/// same way the mouse does. Everything runs on unscaled time, since the menu
/// scene has no meaningful timescale and the pause menu can be up.
/// </summary>
public class MenuOptionRow : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    const float FillSeconds = 0.26f;
    const float FadeSeconds = 0.16f;
    const float ScanSeconds = 0.42f;

    static readonly Color AccentCool = new Color32(0x5B, 0xD8, 0xFF, 0xFF);

    TextMeshProUGUI _label, _caret;
    RectTransform _fill;
    Image _scan, _wash;

    bool _hot;          // pointer over OR selected
    float _t;           // 0..1 lit amount
    float _scanT = -1f; // <0 = idle; otherwise progress through one sweep
    float _rowHeight = 68f;

    Color _labelBase;

    public void Init(TextMeshProUGUI label, TextMeshProUGUI caret, RectTransform fill, Image scan, Image wash)
    {
        _label = label; _caret = caret; _fill = fill; _scan = scan; _wash = wash;
        if (_label != null) _labelBase = _label.color;
        Apply();
    }

    void OnEnable() { _hot = false; _t = 0f; _scanT = -1f; Apply(); }

    public void OnPointerEnter(PointerEventData e) => SetHot(true);
    public void OnPointerExit(PointerEventData e)  => SetHot(false);
    public void OnSelect(BaseEventData e)          => SetHot(true);
    public void OnDeselect(BaseEventData e)        => SetHot(false);

    void SetHot(bool on)
    {
        if (_hot == on) return;
        _hot = on;
        // Only sweep on the way IN — a scanline on exit reads as a glitch.
        if (on) _scanT = 0f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        float target = _hot ? 1f : 0f;
        if (!Mathf.Approximately(_t, target))
        {
            float rate = dt / (_hot ? FillSeconds : FadeSeconds);
            _t = Mathf.MoveTowards(_t, target, rate);
        }

        if (_scanT >= 0f)
        {
            _scanT += dt / ScanSeconds;
            if (_scanT >= 1f) _scanT = -1f;
        }

        Apply();
    }

    void Apply()
    {
        // Eased fill so it accelerates away from the left edge rather than
        // crawling linearly.
        float e = 1f - (1f - _t) * (1f - _t);

        if (_fill != null)
        {
            var parent = _fill.parent as RectTransform;
            float w = parent != null ? parent.rect.width : 0f;
            _fill.sizeDelta = new Vector2(w * e, 0f);
        }

        if (_label != null)
            _label.color = Color.Lerp(_labelBase, Color.white, e);

        if (_caret != null)
            _caret.color = new Color(AccentCool.r, AccentCool.g, AccentCool.b, Mathf.Lerp(0.55f, 1f, e));

        if (_wash != null)
            _wash.color = new Color(AccentCool.r, AccentCool.g, AccentCool.b, 0.09f * e);

        if (_scan != null)
        {
            if (_scanT < 0f)
            {
                _scan.color = new Color(AccentCool.r, AccentCool.g, AccentCool.b, 0f);
            }
            else
            {
                var rt = (RectTransform)transform;
                _rowHeight = rt.rect.height > 1f ? rt.rect.height : _rowHeight;
                var srt = _scan.rectTransform;
                srt.anchoredPosition = new Vector2(0f, -_scanT * _rowHeight);
                // Bright at the top, gone by the bottom.
                _scan.color = new Color(AccentCool.r, AccentCool.g, AccentCool.b, 1f - _scanT);
            }
        }
    }
}
