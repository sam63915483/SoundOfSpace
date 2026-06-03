using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-spray FX by BloodFX. Drives the effect by scale:
/// grows from zero to full over growSeconds (the initial impact spurt), then
/// settles to a RESTING scale (baseScale, e.g. 50%) and spurts back up toward
/// full in irregular little beats — quick rise, decay back to rest — like real
/// arterial bleeding (irregular intervals + varied beat strength). Finally
/// shrinks to zero over shrinkSeconds and destroys itself. Target scale is
/// captured AFTER parenting so world size is correct under a scaled enemy.
/// </summary>
public class BloodSpray : MonoBehaviour
{
    public void Init(float lifetime, float growSeconds, float shrinkSeconds, Vector3 targetScale,
                     float baseScale, float beatIntervalMin, float beatIntervalMax, float beatFallSeconds)
    {
        StartCoroutine(Run(Mathf.Max(0.02f, lifetime),
                           Mathf.Max(0f, growSeconds),
                           Mathf.Max(0f, shrinkSeconds),
                           targetScale,
                           Mathf.Clamp01(baseScale),
                           Mathf.Max(0.02f, beatIntervalMin),
                           Mathf.Max(beatIntervalMin, beatIntervalMax),
                           Mathf.Max(0.02f, beatFallSeconds)));
    }

    IEnumerator Run(float lifetime, float grow, float shrink, Vector3 target,
                    float baseScale, float beatMin, float beatMax, float beatFall)
    {
        if (grow + shrink > lifetime)
        {
            float sum = grow + shrink;
            grow   = lifetime * (grow / sum);
            shrink = lifetime * (shrink / sum);
        }
        float pulseDuration = Mathf.Max(0f, lifetime - grow - shrink);

        // Grow 0 -> full (initial impact spurt). Zero first so no first-frame pop.
        transform.localScale = Vector3.zero;
        float t = 0f;
        while (t < grow)
        {
            transform.localScale = target * (t / grow);
            t += Time.deltaTime;
            yield return null;
        }

        // Beat phase: rest at baseScale, spurt up toward full in irregular beats.
        // 'beat' is the spurt envelope (0 = rest, 1 = full); it decays toward 0
        // and is re-triggered at random intervals with a varied strength. Starts
        // at 1 so the grow flows straight into the first spurt's decay.
        float beat = 1f;
        float lastFactor = 1f;
        float sinceBeat = 0f;
        float nextBeat = Random.Range(beatMin, beatMax);
        t = 0f;
        while (t < pulseDuration)
        {
            float dt = Time.deltaTime;
            t += dt;
            sinceBeat += dt;
            if (sinceBeat >= nextBeat)
            {
                beat = Mathf.Max(beat, Random.Range(0.65f, 1f)); // new spurt, varied height
                sinceBeat = 0f;
                nextBeat = Random.Range(beatMin, beatMax);
            }
            beat = Mathf.MoveTowards(beat, 0f, dt / beatFall);
            lastFactor = baseScale + (1f - baseScale) * beat;
            transform.localScale = target * lastFactor;
            yield return null;
        }

        // Shrink from wherever the last beat left off -> 0, so there's no jump.
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
