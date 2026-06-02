using System.Collections;
using UnityEngine;

// Shared scale-fade helper for runtime-spawned world objects (trees,
// mushrooms, aliens). Scales the object from a tiny start size up to its
// captured target on appear, and scales back down before pool-return on
// despawn. No shader/material changes — works with any prefab.
//
// Usage:
//   var fade = obj.GetComponent<SpawnFade>() ?? obj.AddComponent<SpawnFade>();
//   fade.BeginFadeIn();                       // call AFTER setting final scale
//   ...
//   fade.BeginFadeOut(() => ReturnToPool(obj)); // delays pool-return until fade ends
public class SpawnFade : MonoBehaviour
{
    [Tooltip("Seconds to grow from startMultiplier × target to full target.")]
    public float fadeInDuration = 0.4f;
    [Tooltip("Seconds to shrink from full target down to startMultiplier × target.")]
    public float fadeOutDuration = 0.3f;
    [Tooltip("Fraction of the target scale used at fade-in start and fade-out end. 0.05 is small enough to read as invisible without hitting true zero (some shaders / colliders dislike scale zero).")]
    public float startMultiplier = 0.05f;

    Coroutine _routine;
    Vector3 _targetScale;
    bool _fadingOut;

    public void BeginFadeIn()
    {
        if (_routine != null) StopCoroutine(_routine);
        _fadingOut = false;
        _targetScale = transform.localScale;
        transform.localScale = _targetScale * startMultiplier;
        if (!gameObject.activeInHierarchy) return;
        _routine = StartCoroutine(FadeInRoutine());
    }

    public void BeginFadeOut(System.Action onComplete)
    {
        if (_routine != null) StopCoroutine(_routine);
        _fadingOut = true;
        // Capture current scale as the "from" — if BeginFadeIn was in flight,
        // current scale may be mid-fade and that's the right starting point.
        if (!gameObject.activeInHierarchy)
        {
            // Object already disabled — skip the animation and invoke the
            // callback synchronously so the spawner's bookkeeping stays sane.
            onComplete?.Invoke();
            return;
        }
        _routine = StartCoroutine(FadeOutRoutine(onComplete));
    }

    IEnumerator FadeInRoutine()
    {
        Vector3 from = _targetScale * startMultiplier;
        Vector3 to = _targetScale;
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeInDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // Ease-out cubic so the grow feels organic.
            k = 1f - Mathf.Pow(1f - k, 3f);
            transform.localScale = Vector3.Lerp(from, to, k);
            yield return null;
        }
        transform.localScale = to;
        _routine = null;
    }

    IEnumerator FadeOutRoutine(System.Action onComplete)
    {
        Vector3 from = transform.localScale;
        Vector3 to = _targetScale * startMultiplier;
        if (_targetScale == Vector3.zero)
        {
            // BeginFadeIn was never called (object spawned without going through
            // the fade-in path). Use current scale as the implicit target.
            _targetScale = from;
            to = _targetScale * startMultiplier;
        }
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeOutDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            transform.localScale = Vector3.Lerp(from, to, k);
            yield return null;
        }
        transform.localScale = to;
        _routine = null;
        onComplete?.Invoke();
    }

    void OnDisable()
    {
        // If the object is disabled mid-fade (e.g., scene unload), stop the
        // coroutine and restore the target scale so a future BeginFadeIn re-uses
        // a clean baseline.
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
            if (_targetScale != Vector3.zero) transform.localScale = _targetScale;
        }
    }
}
