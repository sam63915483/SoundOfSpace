using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-spray FX by BloodFX. Two stages, both beating:
///  1. GUSH (gushSeconds): grows to full, then spurts up from a shrunk resting
///     scale in irregular beats — the strong initial bleeding.
///  2. DIE-OUT (dieSeconds): drops to "much less" (dieStartScale) and shrinks
///     steadily to zero while STILL beating up and down, then destroys itself.
/// Target scale is captured AFTER parenting so world size is right under a
/// scaled enemy.
/// </summary>
public class BloodSpray : MonoBehaviour
{
    public void Init(float gushSeconds, float growSeconds, Vector3 targetScale,
                     float restScale, float beatIntervalMin, float beatIntervalMax, float beatFallSeconds,
                     float dieSeconds, float dieStartScale)
    {
        StartCoroutine(Run(Mathf.Max(0.1f, gushSeconds),
                           Mathf.Max(0f, growSeconds),
                           targetScale,
                           Mathf.Clamp01(restScale),
                           Mathf.Max(0.02f, beatIntervalMin),
                           Mathf.Max(beatIntervalMin, beatIntervalMax),
                           Mathf.Max(0.02f, beatFallSeconds),
                           Mathf.Max(0.1f, dieSeconds),
                           Mathf.Clamp01(dieStartScale)));
    }

    // Beat state, shared across both stages so the pulse is continuous.
    float _beat = 1f, _sinceBeat = 0f, _nextBeat = 0.3f;
    float _beatMin, _beatMax, _beatFall, _restScale;

    IEnumerator Run(float gush, float grow, Vector3 target, float restScale,
                    float beatMin, float beatMax, float beatFall, float dieSeconds, float dieStartScale)
    {
        _beatMin = beatMin; _beatMax = beatMax; _beatFall = beatFall; _restScale = restScale;
        _nextBeat = Random.Range(beatMin, beatMax);
        grow = Mathf.Min(grow, gush);

        // Grow 0 -> full (initial impact spurt). Zero first so no first-frame pop.
        transform.localScale = Vector3.zero;
        float t = 0f;
        while (t < grow)
        {
            transform.localScale = target * (t / grow);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = target;

        // Stage 1 — GUSH: full size, beating, for the rest of gushSeconds.
        t = grow;
        while (t < gush)
        {
            float dt = Time.deltaTime;
            t += dt;
            transform.localScale = target * StepBeat(dt);
            yield return null;
        }

        // Stage 2 — DIE-OUT: drop to dieStartScale and shrink to zero over
        // dieSeconds while still beating up and down.
        float t2 = 0f;
        while (t2 < dieSeconds)
        {
            float dt = Time.deltaTime;
            t2 += dt;
            float envelope = Mathf.Lerp(dieStartScale, 0f, t2 / dieSeconds);
            transform.localScale = target * (envelope * StepBeat(dt));
            yield return null;
        }
        transform.localScale = Vector3.zero;

        Destroy(gameObject);
    }

    // Advances the beat envelope one frame and returns the current pulse factor
    // (restScale at rest, up toward 1 on a beat). Beats fire at random intervals
    // with varied strength, decaying back to rest — irregular, like bleeding.
    float StepBeat(float dt)
    {
        _sinceBeat += dt;
        if (_sinceBeat >= _nextBeat)
        {
            _beat = Mathf.Max(_beat, Random.Range(0.65f, 1f));
            _sinceBeat = 0f;
            _nextBeat = Random.Range(_beatMin, _beatMax);
        }
        _beat = Mathf.MoveTowards(_beat, 0f, dt / _beatFall);
        return _restScale + (1f - _restScale) * _beat;
    }
}
