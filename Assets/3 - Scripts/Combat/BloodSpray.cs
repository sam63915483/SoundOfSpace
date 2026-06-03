using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-spray FX by BloodFX. Drives the effect by scale:
/// grows from zero to its full (target) scale over growSeconds, then PULSES for
/// the middle of its life — dipping to pulseMinScale and back to full, over and
/// over (cosine, pulseHz cycles/sec) for a throbbing "still bleeding" feel — then
/// shrinks back to zero over shrinkSeconds before destroying itself. The target
/// scale is captured AFTER parenting so the world-space size is correct even when
/// parented under a non-uniformly scaled enemy.
/// </summary>
public class BloodSpray : MonoBehaviour
{
    public void Init(float lifetime, float growSeconds, float shrinkSeconds, Vector3 targetScale,
                     float pulseMinScale, float pulseHz)
    {
        StartCoroutine(Run(Mathf.Max(0.02f, lifetime),
                           Mathf.Max(0f, growSeconds),
                           Mathf.Max(0f, shrinkSeconds),
                           targetScale,
                           Mathf.Clamp01(pulseMinScale),
                           Mathf.Max(0f, pulseHz)));
    }

    IEnumerator Run(float lifetime, float grow, float shrink, Vector3 target, float pulseMin, float pulseHz)
    {
        // If grow + shrink overruns the lifetime, scale them down proportionally
        // so they still fit (and leave no pulse time).
        if (grow + shrink > lifetime)
        {
            float sum = grow + shrink;
            grow   = lifetime * (grow / sum);
            shrink = lifetime * (shrink / sum);
        }
        float pulseDuration = Mathf.Max(0f, lifetime - grow - shrink);

        // Grow 0 -> target. Set to zero first so there's no full-size pop on the
        // first rendered frame.
        transform.localScale = Vector3.zero;
        float t = 0f;
        while (t < grow)
        {
            transform.localScale = target * (t / grow);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = target;

        // Pulse between full (1.0) and pulseMin. Cosine starts at +1 so the
        // effect is at full when the pulse begins, then dips first — "shrink,
        // shoot back up, dip again..." for the whole middle of the life.
        float mid = (1f + pulseMin) * 0.5f;
        float amp = (1f - pulseMin) * 0.5f;
        float lastFactor = 1f;
        t = 0f;
        while (t < pulseDuration)
        {
            lastFactor = mid + amp * Mathf.Cos(2f * Mathf.PI * pulseHz * t);
            transform.localScale = target * lastFactor;
            t += Time.deltaTime;
            yield return null;
        }

        // Shrink from wherever the pulse left off -> 0, so there's no jump.
        t = 0f;
        while (t < shrink)
        {
            transform.localScale = target * Mathf.Lerp(lastFactor, 0f, t / shrink);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = Vector3.zero;

        Destroy(gameObject);
    }
}
