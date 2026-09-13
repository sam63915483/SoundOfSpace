using UnityEngine;

/// <summary>
/// A lamp that is OFF by day and ON by night — the village lanterns (Sam,
/// 2026-09-13: by day the lamps light the ground but the grass takes only real
/// sunlight, so a lit lantern at noon looks wrong; also fourteen point lights
/// are a real cost, and by day they buy nothing).
///
/// Day/night is GEOMETRIC, from the actual sun position over this spot on the
/// planet (FishingSun.SunDot: +1 noon, 0 sunset, −1 midnight), the same source
/// the fishing bite rate uses — never GalaxyTime, which is deliberately
/// decoupled from the sky.
///
/// `Scale` is 1 at night, 0 by day, eased through the twilight band and
/// smoothed over `fadeSeconds`. TorchLightSteady multiplies its intensity by it
/// (and disables the Light at 0 so it stops costing a pixel-light pass). On a
/// Light without TorchLightSteady this component drives the Light itself.
/// </summary>
[RequireComponent(typeof(Light))]
public class DayNightLight : MonoBehaviour
{
    [Tooltip("Sun elevation (dot with local up) above which the lamp is fully OFF. 0 = the sun on the horizon; 0.08 ≈ 4.6° up.")]
    public float offAboveDot = 0.08f;
    [Tooltip("Sun elevation below which the lamp is fully ON. Negative = after sunset.")]
    public float onBelowDot = -0.04f;
    [Tooltip("Seconds to ease between on and off once the sun crosses the band.")]
    public float fadeSeconds = 2f;
    [Tooltip("Seconds between sun checks (the answer changes slowly; 14 lanterns need not all sample every frame).")]
    public float checkInterval = 0.25f;

    /// 1 = full night brightness, 0 = off.
    public float Scale { get; private set; } = 1f;

    Light _light;
    TorchLightSteady _steady;
    Transform _body;
    float _target = 1f;
    float _nextCheck;
    bool _first = true;

    void Awake()
    {
        _light = GetComponent<Light>();
        _steady = GetComponent<TorchLightSteady>();
        var body = GetComponentInParent<CelestialBody>();
        _body = body != null ? body.transform : null;
    }

    void OnEnable() { _first = true; _nextCheck = 0f; }

    void Update()
    {
        if (Time.time >= _nextCheck)
        {
            _nextCheck = Time.time + Mathf.Max(0.02f, checkInterval);
            if (_body == null) { var body = GetComponentInParent<CelestialBody>(); _body = body != null ? body.transform : null; }
            float dot = FishingSun.SunDot(transform.position, _body);
            // 1 below onBelowDot, 0 above offAboveDot, smooth in between.
            float t = Mathf.InverseLerp(offAboveDot, onBelowDot, dot);
            _target = Mathf.SmoothStep(0f, 1f, t);
        }
        if (_first) { Scale = _target; _first = false; }
        else Scale = Mathf.MoveTowards(Scale, _target, Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));

        // Without TorchLightSteady nobody else writes the Light: do it here.
        if (_steady == null && _light != null)
        {
            bool on = Scale > 0.001f;
            if (_light.enabled != on) _light.enabled = on;
            if (on) _light.intensity = _baseIntensity * Scale;
        }
    }

    float _baseIntensity = -1f;
    void Start() { if (_light != null && _baseIntensity < 0f) _baseIntensity = _light.intensity; }
}
