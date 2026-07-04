using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Central haptics hub. Two shapes of feedback:
//   Pulse(low, high, seconds)          — one-shot (shot fired, landed, bite)
//   SetChannel(id, low, high)          — continuous until cleared (ship thrust)
// Output each frame = per-motor max across all live pulses and channels,
// sent to Gamepad.current. Gated on Enabled (pause-menu VIBRATION toggle,
// pushed from InputSettings). Hard-zeroed while the game is paused or
// unfocused so the pad never buzzes over a menu.
public static class GamepadRumble
{
    public static bool Enabled = true;

    struct RumblePulse { public float low, high, endTime; }
    static readonly List<RumblePulse> _pulses = new List<RumblePulse>();
    static readonly Dictionary<string, Vector2> _channels = new Dictionary<string, Vector2>();

    static Gamepad _lastPad;
    static float _sentLow = -1f, _sentHigh = -1f;

    public static void Pulse(float low, float high, float seconds)
    {
        if (!Enabled) return;
        _pulses.Add(new RumblePulse {
            low = Mathf.Clamp01(low), high = Mathf.Clamp01(high),
            endTime = Time.unscaledTime + seconds });
    }

    public static void SetChannel(string id, float low, float high)
    {
        _channels[id] = new Vector2(Mathf.Clamp01(low), Mathf.Clamp01(high));
    }

    public static void ClearChannel(string id) => _channels.Remove(id);

    public static void StopAll()
    {
        _pulses.Clear();
        _channels.Clear();
        Send(0f, 0f, force: true);
    }

    // Called once per frame from TutorialGate's LateDriver.
    public static void Tick()
    {
        var pad = Gamepad.current;
        if (pad != _lastPad)
        {
            // Pad swapped/disconnected — silence the old one if still present.
            if (_lastPad != null && _lastPad.added) _lastPad.SetMotorSpeeds(0f, 0f);
            _sentLow = _sentHigh = -1f;
            _lastPad = pad;
        }
        if (pad == null) return;

        float low = 0f, high = 0f;
        bool muted = !Enabled || Time.timeScale == 0f || !Application.isFocused;
        if (!muted)
        {
            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= _pulses[i].endTime) { _pulses.RemoveAt(i); continue; }
                low  = Mathf.Max(low,  _pulses[i].low);
                high = Mathf.Max(high, _pulses[i].high);
            }
            foreach (var kv in _channels)
            {
                low  = Mathf.Max(low,  kv.Value.x);
                high = Mathf.Max(high, kv.Value.y);
            }
        }
        Send(low, high, force: false);
    }

    static void Send(float low, float high, bool force)
    {
        var pad = Gamepad.current;
        if (pad == null) return;
        // Only touch the HID when the value changes — SetMotorSpeeds every
        // frame at identical values is wasted output traffic.
        if (!force && Mathf.Approximately(low, _sentLow) && Mathf.Approximately(high, _sentHigh)) return;
        pad.SetMotorSpeeds(low, high);
        _sentLow = low; _sentHigh = high;
    }
}
