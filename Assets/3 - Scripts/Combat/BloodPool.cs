using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-pool FX by BloodFX. Holds the pool at full
/// opacity for lingerSeconds, then fades it out over fadeSeconds and destroys
/// the GameObject. The pool prefab loops with a ~5s particle lifetime, so the
/// fade stops emission (freezing the live set) and ramps each live particle's
/// alpha down — this is shader-agnostic because the particle system applies
/// particle colour as a vertex multiply, independent of the material's property
/// names.
/// </summary>
public class BloodPool : MonoBehaviour
{
    ParticleSystem[] _systems;
    ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[64];
    float _linger;
    float _fade;

    public void Init(float lingerSeconds, float fadeSeconds)
    {
        _linger = Mathf.Max(0f, lingerSeconds);
        _fade   = Mathf.Max(0.05f, fadeSeconds);
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        if (_linger > 0f) yield return new WaitForSeconds(_linger);

        _systems = GetComponentsInChildren<ParticleSystem>(true);

        // Freeze births so the live set only shrinks (particles age out) — no
        // new full-alpha particles appear mid-fade.
        for (int i = 0; i < _systems.Length; i++)
            if (_systems[i] != null)
                _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);

        float elapsed = 0f;
        float prevMul = 1f;
        while (elapsed < _fade)
        {
            elapsed += Time.deltaTime;
            float mul = Mathf.Clamp01(1f - elapsed / _fade);
            // Per-frame ratio so repeated multiplies compose to a linear fade.
            float factor = prevMul > 0.0001f ? mul / prevMul : 0f;
            prevMul = mul;

            for (int i = 0; i < _systems.Length; i++)
            {
                var ps = _systems[i];
                if (ps == null) continue;
                int count = ps.particleCount;
                if (count == 0) continue;
                if (_buffer.Length < count) _buffer = new ParticleSystem.Particle[count];
                int n = ps.GetParticles(_buffer);
                for (int p = 0; p < n; p++)
                {
                    Color32 c = _buffer[p].startColor;
                    c.a = (byte)Mathf.RoundToInt(c.a * factor);
                    _buffer[p].startColor = c;
                }
                ps.SetParticles(_buffer, n);
            }
            yield return null;
        }

        Destroy(gameObject);
    }
}
