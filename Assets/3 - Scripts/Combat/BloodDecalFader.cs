using System.Collections;
using UnityEngine;

/// <summary>
/// Drives a spawned blood-decal Projector: optionally sets its size, holds at
/// full opacity for lingerSeconds, then fades the projected material's _Color
/// alpha to zero over fadeSeconds and destroys itself. Uses the Projector's
/// instanced material so the shared asset isn't modified.
/// </summary>
[RequireComponent(typeof(Projector))]
public class BloodDecalFader : MonoBehaviour
{
    Material _mat;
    float _baseAlpha = 1f;
    float _linger;
    float _fade;

    public void Init(float lingerSeconds, float fadeSeconds, float orthographicSize)
    {
        _linger = Mathf.Max(0f, lingerSeconds);
        _fade   = Mathf.Max(0.05f, fadeSeconds);

        var proj = GetComponent<Projector>();
        if (proj != null)
        {
            if (orthographicSize > 0f) proj.orthographicSize = orthographicSize;
            // Projector.material is the SHARED asset (no auto-instancing like a
            // Renderer), so clone it per-instance or every decal fades together.
            if (proj.material != null)
            {
                _mat = new Material(proj.material);
                proj.material = _mat;
                if (_mat.HasProperty("_Color")) _baseAlpha = _mat.GetColor("_Color").a;
            }
        }
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    IEnumerator Run()
    {
        if (_linger > 0f) yield return new WaitForSeconds(_linger);

        float t = 0f;
        while (t < _fade)
        {
            if (_mat != null && _mat.HasProperty("_Color"))
            {
                Color c = _mat.GetColor("_Color");
                c.a = _baseAlpha * (1f - t / _fade);
                _mat.SetColor("_Color", c);
            }
            t += Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }
}
