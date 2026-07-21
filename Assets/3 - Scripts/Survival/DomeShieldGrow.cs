using UnityEngine;

/// <summary>
/// Animates the shield bubble: it grows out of the emitter when a dome is placed
/// (expands from nothing with a slight overshoot, so it reads as the field
/// snapping on), and — driven by BubbleDome — collapses to nothing when the dome
/// runs out of fuel, then snaps back on when refuelled.
/// </summary>
public class DomeShieldGrow : MonoBehaviour
{
    public Transform bubble;         // the shield sphere to grow
    public float fullDiameter = 16f; // final localScale (matches 2×dome radius)
    public float growTime = 1.4f;
    public float collapseTime = 0.7f;

    enum State { Growing, Full, Collapsing, Collapsed }
    State _state;
    float _t;
    float _fromScale;   // scale captured at the start of a collapse

    void OnEnable()
    {
        if (bubble != null) bubble.localScale = Vector3.zero;
        _t = 0f;
        _state = State.Growing;
    }

    /// Power the field on (refuelled) or collapse it (out of fuel). Called by BubbleDome.
    public void SetShieldOn(bool on)
    {
        if (on)
        {
            if (_state == State.Growing || _state == State.Full) return;
            _t = 0f;
            _state = State.Growing;   // regrow from its current (collapsed) size
        }
        else
        {
            if (_state == State.Collapsing || _state == State.Collapsed) return;
            _fromScale = bubble != null ? bubble.localScale.x : fullDiameter;
            _t = 0f;
            _state = State.Collapsing;
        }
    }

    void Update()
    {
        if (bubble == null) return;
        switch (_state)
        {
            case State.Growing:
            {
                _t += Time.deltaTime;
                float u = Mathf.Clamp01(_t / Mathf.Max(0.01f, growTime));
                float e = 1f - Mathf.Pow(1f - u, 3f);                 // ease-out
                float overshoot = 1f + 0.06f * Mathf.Sin(u * Mathf.PI); // small "pop"
                bubble.localScale = Vector3.one * (fullDiameter * e * overshoot);
                if (u >= 1f) { bubble.localScale = Vector3.one * fullDiameter; _state = State.Full; }
                break;
            }
            case State.Collapsing:
            {
                _t += Time.deltaTime;
                float u = Mathf.Clamp01(_t / Mathf.Max(0.01f, collapseTime));
                float e = Mathf.Pow(1f - u, 2f);                      // ease-in shrink to nothing
                bubble.localScale = Vector3.one * (_fromScale * e);
                if (u >= 1f) { bubble.localScale = Vector3.zero; _state = State.Collapsed; }
                break;
            }
        }
    }
}
