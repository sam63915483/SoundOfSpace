using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-spray FX by BloodFX. Two stages:
///  1. GUSH (gushSeconds): grows from zero to full, then spurts up from a shrunk
///     resting scale in irregular beats — the active bleeding.
///  2. At the end of the gush the mesh "cone" sub-systems are stopped (the gush
///     goes away) while the billboard droplet sub-systems keep emitting, thinned
///     to a trickle, for trickleSeconds — the wound keeps weeping after the
///     gush. A short tail stops emission so the last droplets fade, then the FX
///     destroys itself. Target scale is captured AFTER parenting so world size
///     is right under a scaled enemy.
/// </summary>
public class BloodSpray : MonoBehaviour
{
    const float TailSeconds = 2f; // stop emission this long before destroy so droplets fade out

    public void Init(float gushSeconds, float growSeconds, Vector3 targetScale,
                     float restScale, float beatIntervalMin, float beatIntervalMax, float beatFallSeconds,
                     float trickleSeconds, float trickleEmissionScale)
    {
        StartCoroutine(Run(Mathf.Max(0.1f, gushSeconds),
                           Mathf.Max(0f, growSeconds),
                           targetScale,
                           Mathf.Clamp01(restScale),
                           Mathf.Max(0.02f, beatIntervalMin),
                           Mathf.Max(beatIntervalMin, beatIntervalMax),
                           Mathf.Max(0.02f, beatFallSeconds),
                           Mathf.Max(0f, trickleSeconds),
                           Mathf.Clamp01(trickleEmissionScale)));
    }

    IEnumerator Run(float gush, float grow, Vector3 target, float restScale,
                    float beatMin, float beatMax, float beatFall, float trickle, float trickleScale)
    {
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

        // Beat phase for the rest of the gush: rest at restScale, spurt up in
        // irregular beats. 'beat' decays toward rest and is re-triggered at
        // random intervals with varied strength.
        float beat = 1f, sinceBeat = 0f, nextBeat = Random.Range(beatMin, beatMax);
        t = grow;
        while (t < gush)
        {
            float dt = Time.deltaTime;
            t += dt; sinceBeat += dt;
            if (sinceBeat >= nextBeat)
            {
                beat = Mathf.Max(beat, Random.Range(0.65f, 1f));
                sinceBeat = 0f;
                nextBeat = Random.Range(beatMin, beatMax);
            }
            beat = Mathf.MoveTowards(beat, 0f, dt / beatFall);
            transform.localScale = target * (restScale + (1f - restScale) * beat);
            yield return null;
        }

        // End of gush: settle to full, stop the mesh "cone" gush, and thin the
        // billboard droplet emission to a trickle (the wound keeps weeping).
        transform.localScale = target;
        var systems = GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            if (ps == null) continue;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            bool isMeshGush = r != null && r.renderMode == ParticleSystemRenderMode.Mesh;
            if (isMeshGush)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); // gush tapers off and goes away
            }
            else
            {
                var em = ps.emission;
                em.rateOverTimeMultiplier     *= trickleScale;
                em.rateOverDistanceMultiplier *= trickleScale;
            }
        }

        // Trickle, then a tail: stop all emission so the last droplets fade.
        float tail = Mathf.Min(TailSeconds, trickle);
        if (trickle - tail > 0f) yield return new WaitForSeconds(trickle - tail);
        foreach (var ps in systems)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (tail > 0f) yield return new WaitForSeconds(tail);

        Destroy(gameObject);
    }
}
