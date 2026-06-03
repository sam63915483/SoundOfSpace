using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-spray FX by BloodFX. Drives two things:
///  - A grow/die ENVELOPE on the whole effect (root scale): grows in over
///    growSeconds, holds through the gush, then shrinks to zero over dieSeconds.
///  - A BEAT pulse applied ONLY to the mesh "cone" sub-systems (the gush), which
///    shrink down and spurt back up irregularly. The droplet (billboard) layers
///    deliberately do NOT get the beat — pulsing the whole transform yanked the
///    live droplets in/out each beat, which looked like stretching/spreading in
///    a weird direction. The beat also rises over a short attack so the cone
///    doesn't snap (choppy).
/// </summary>
public class BloodSpray : MonoBehaviour
{
    Transform[] _cones;     // mesh "cone" sub-systems — these pulse with the beat
    Vector3[]   _coneBase;  // their prefab local scales

    float _beat = 1f, _beatTarget = 1f, _sinceBeat, _nextBeat;
    float _beatMin, _beatMax, _beatFall, _restScale;
    const float BeatAttack = 0.06f; // seconds for a beat to rise — smooths the snap

    public void Init(float gushSeconds, float growSeconds, Vector3 targetScale,
                     float restScale, float beatIntervalMin, float beatIntervalMax, float beatFallSeconds,
                     float dieSeconds, float dieStartScale)
    {
        var cones = new List<Transform>();
        var bases = new List<Vector3>();
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r != null && r.renderMode == ParticleSystemRenderMode.Mesh)
            {
                cones.Add(ps.transform);
                bases.Add(ps.transform.localScale);
            }
        }
        _cones = cones.ToArray();
        _coneBase = bases.ToArray();

        _beatMin   = Mathf.Max(0.02f, beatIntervalMin);
        _beatMax   = Mathf.Max(_beatMin, beatIntervalMax);
        _beatFall  = Mathf.Max(0.02f, beatFallSeconds);
        _restScale = Mathf.Clamp01(restScale);
        _nextBeat  = Random.Range(_beatMin, _beatMax);

        StartCoroutine(Run(Mathf.Max(0.1f, gushSeconds), Mathf.Max(0f, growSeconds), targetScale,
                           Mathf.Max(0.1f, dieSeconds), Mathf.Clamp01(dieStartScale)));
    }

    IEnumerator Run(float gush, float grow, Vector3 target, float dieSeconds, float dieStartScale)
    {
        grow = Mathf.Min(grow, gush);

        // Grow: whole effect scales in; cones already beating.
        transform.localScale = Vector3.zero;
        float t = 0f;
        while (t < grow)
        {
            float dt = Time.deltaTime; t += dt;
            transform.localScale = target * (t / grow);
            ApplyConeBeat(StepBeat(dt));
            yield return null;
        }

        // Gush: full size, cones beating.
        transform.localScale = target;
        t = grow;
        while (t < gush)
        {
            ApplyConeBeat(StepBeat(Time.deltaTime));
            t += Time.deltaTime;
            yield return null;
        }

        // Die-out: envelope dieStartScale -> 0 over dieSeconds; cones still beat.
        float t2 = 0f;
        while (t2 < dieSeconds)
        {
            float dt = Time.deltaTime; t2 += dt;
            float env = Mathf.Lerp(dieStartScale, 0f, t2 / dieSeconds);
            transform.localScale = target * env;
            ApplyConeBeat(StepBeat(dt));
            yield return null;
        }
        transform.localScale = Vector3.zero;

        Destroy(gameObject);
    }

    // Scale only the cone sub-systems by the beat factor (relative to their base
    // local scale). The envelope on the root already scales everything; this adds
    // the pulse on top, for the cones only.
    void ApplyConeBeat(float beatFactor)
    {
        if (_cones == null) return;
        for (int i = 0; i < _cones.Length; i++)
            if (_cones[i] != null) _cones[i].localScale = _coneBase[i] * beatFactor;
    }

    // Irregular beat envelope with a short attack: fires at random intervals with
    // varied strength, rises over BeatAttack (no snap), then decays back to rest.
    float StepBeat(float dt)
    {
        _sinceBeat += dt;
        if (_sinceBeat >= _nextBeat)
        {
            _beatTarget = Random.Range(0.65f, 1f);
            _sinceBeat = 0f;
            _nextBeat = Random.Range(_beatMin, _beatMax);
        }
        _beatTarget = Mathf.MoveTowards(_beatTarget, 0f, dt / _beatFall);
        _beat = Mathf.MoveTowards(_beat, _beatTarget, dt / BeatAttack);
        return _restScale + (1f - _restScale) * _beat;
    }
}
