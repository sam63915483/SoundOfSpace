using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-spray FX by BloodFX. Eases the effect in and out
/// by scale: grows from zero to its full (target) scale over growSeconds, holds
/// at full for the middle of its life, then shrinks back to zero over
/// shrinkSeconds before destroying itself. The target scale is captured AFTER
/// parenting so the world-space size is correct even when parented under a
/// non-uniformly scaled enemy.
/// </summary>
public class BloodSpray : MonoBehaviour
{
    public void Init(float lifetime, float growSeconds, float shrinkSeconds, Vector3 targetScale)
    {
        StartCoroutine(Run(Mathf.Max(0.02f, lifetime),
                           Mathf.Max(0f, growSeconds),
                           Mathf.Max(0f, shrinkSeconds),
                           targetScale));
    }

    IEnumerator Run(float lifetime, float grow, float shrink, Vector3 target)
    {
        // If grow + shrink overruns the lifetime, scale them down proportionally
        // so they still fit (and leave no hold time).
        if (grow + shrink > lifetime)
        {
            float sum = grow + shrink;
            grow   = lifetime * (grow / sum);
            shrink = lifetime * (shrink / sum);
        }
        float hold = Mathf.Max(0f, lifetime - grow - shrink);

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

        if (hold > 0f) yield return new WaitForSeconds(hold);

        // Shrink target -> 0.
        t = 0f;
        while (t < shrink)
        {
            transform.localScale = target * (1f - t / shrink);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = Vector3.zero;

        Destroy(gameObject);
    }
}
