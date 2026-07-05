using UnityEngine;

/// <summary>
/// Wraps ObserverState.IsObserved with a grace window so screen-edge flicker doesn't
/// strobe state, and exposes the observed→unobserved transition the dimensions key off.
/// Plain class (not a MonoBehaviour): owners call Tick() once per frame.
/// </summary>
public class ObservationTracker
{
    readonly float _grace;
    float _lastObservedTime = float.NegativeInfinity;
    bool _wasEffectivelyObserved;

    public bool WasEverObserved { get; private set; }
    /// <summary>Seconds since last actually on screen (0 while observed).</summary>
    public float TimeUnobserved => Mathf.Max(0f, Time.time - _lastObservedTime);

    public ObservationTracker(float graceSeconds = 0.15f) { _grace = graceSeconds; }

    /// <summary>
    /// Update with current bounds. Returns effective observed state (true within grace).
    /// justLost fires true exactly once when effective state flips observed → unobserved.
    /// </summary>
    public bool Tick(Bounds b, out bool justLost, float maxDistance = Mathf.Infinity)
    {
        if (ObserverState.IsObserved(b, maxDistance))
        {
            _lastObservedTime = Time.time;
            WasEverObserved = true;
        }
        bool observed = Time.time - _lastObservedTime <= _grace;
        justLost = _wasEffectivelyObserved && !observed;
        _wasEffectivelyObserved = observed;
        return observed;
    }

    public void Reset()
    {
        _lastObservedTime = float.NegativeInfinity;
        _wasEffectivelyObserved = false;
        WasEverObserved = false;
    }
}
