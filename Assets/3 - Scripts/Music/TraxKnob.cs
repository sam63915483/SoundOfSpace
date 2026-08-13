using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Rotary knob widget. Drag vertically, or scroll. Shift = fine, double-click
/// resets to the value it was built with.
///
/// PORT OF <c>prototypes/shuttle-computer/ui/knob.js</c>.
///
/// Deliberately continuous (0-10 float) even though seeding quantizes to 0.5 —
/// the timbre parameters read the raw value, so a hair of movement still does
/// something audible even when the pattern hasn't changed.
///
/// The arc is an Image with type=Filled/Radial360 over a ring sprite, rotated
/// -135° so the sweep starts at the lower left and covers 270°. Rotating the
/// ring is free because a ring is rotationally symmetric.
/// </summary>
public class TraxKnob : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
{
    public const float Sweep = 0.75f;      // 270° as a fraction of a full turn
    public const float StartAngle = -135f;

    int _index;
    double _value;
    double _initial;
    Action<int, double> _onChanged;

    Image _fill;
    RectTransform _pointer;
    TextMeshProUGUI _valueText;
    Image _frame;

    Color _frameIdle, _frameLive;

    public int DialIndex { get { return _index; } }
    public double Value { get { return _value; } }

    public void Init(int index, double initial, Image frame, Image fill, RectTransform pointer,
                     TextMeshProUGUI valueText, Color frameIdle, Color frameLive,
                     Action<int, double> onChanged)
    {
        _index = index;
        _initial = initial;
        _value = initial;
        _frame = frame;
        _fill = fill;
        _pointer = pointer;
        _valueText = valueText;
        _frameIdle = frameIdle;
        _frameLive = frameLive;
        _onChanged = onChanged;
        Render();
    }

    /// Set without firing the change callback — for external state pushes.
    public void SetSilent(double v)
    {
        _value = Clamp(v);
        Render();
    }

    static double Clamp(double v)
    {
        if (v < 0) return 0;
        if (v > 10) return 10;
        // Round to 0.05 so the readout doesn't jitter with float noise.
        return Math.Floor(v * 20.0 + 0.5) / 20.0;
    }

    void Apply(double v)
    {
        double next = Clamp(v);
        if (next == _value) return;
        _value = next;
        Render();
        if (_onChanged != null) _onChanged(_index, _value);
    }

    void Render()
    {
        float t = (float)(_value / 10.0);
        if (_fill != null) _fill.fillAmount = Sweep * t;
        if (_pointer != null)
            _pointer.localEulerAngles = new Vector3(0, 0, -(StartAngle + 360f * Sweep * t));
        if (_valueText != null) _valueText.text = _value.ToString("0.0");
    }

    void SetLive(bool live)
    {
        if (_frame != null) _frame.color = live ? _frameLive : _frameIdle;
    }

    // ── input ────────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e) { SetLive(true); }
    public void OnEndDrag(PointerEventData e) { SetLive(false); }

    public void OnDrag(PointerEventData e)
    {
        // Up is more. ~200px for the full sweep; Shift drops to a fifth of that.
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        Apply(_value + e.delta.y * (fine ? 0.01 : 0.05));
    }

    public void OnScroll(PointerEventData e)
    {
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        double dir = e.scrollDelta.y > 0 ? 1 : -1;
        Apply(_value + dir * (fine ? 0.05 : 0.25));
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.clickCount >= 2) Apply(_initial);
    }
}
