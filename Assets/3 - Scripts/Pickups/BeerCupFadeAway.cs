using System.Collections;
using UnityEngine;

/// <summary>
/// An empty cup set down on the bar: waits <c>lifetime</c> seconds, then goes
/// see-through over <c>fadeSeconds</c> and is destroyed. Added by
/// <see cref="BarCounter"/>.
///
/// The fade swaps each renderer onto an instance of a Standard-shader FADE
/// material (a real asset the counter references, so its transparent variant
/// survives a build) carrying the cup's own texture and colour, then drives
/// its alpha down. Falls back to shrinking if no fade material was given.
/// </summary>
public class BeerCupFadeAway : MonoBehaviour
{
    Material _fadeSource;
    Material[] _instances;
    float _lifetime, _fadeSeconds;

    public void Begin(float lifetime, float fadeSeconds, Material fadeSource)
    {
        _lifetime = Mathf.Max(0f, lifetime);
        _fadeSeconds = Mathf.Max(0.05f, fadeSeconds);
        _fadeSource = fadeSource;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(_lifetime);

        var rends = GetComponentsInChildren<Renderer>(true);
        if (_fadeSource != null && rends.Length > 0)
        {
            _instances = new Material[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                var src = rends[i].sharedMaterial;
                var m = new Material(_fadeSource);
                if (src != null)
                {
                    if (src.HasProperty("_MainTex")) m.mainTexture = src.mainTexture;
                    if (src.HasProperty("_Color")) m.color = src.color;
                }
                _instances[i] = m;
                rends[i].sharedMaterial = m;
            }

            float t = 0f;
            while (t < _fadeSeconds)
            {
                t += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(t / _fadeSeconds);
                for (int i = 0; i < _instances.Length; i++)
                {
                    var c = _instances[i].color; c.a = a; _instances[i].color = c;
                }
                yield return null;
            }
        }
        else
        {
            // No fade material: shrink instead (never invisible-but-present).
            Vector3 from = transform.localScale;
            float t = 0f;
            while (t < _fadeSeconds)
            {
                t += Time.deltaTime;
                transform.localScale = Vector3.Lerp(from, from * 0.02f, Mathf.Clamp01(t / _fadeSeconds));
                yield return null;
            }
        }
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (_instances == null) return;
        for (int i = 0; i < _instances.Length; i++)
            if (_instances[i] != null) Destroy(_instances[i]);
    }
}
