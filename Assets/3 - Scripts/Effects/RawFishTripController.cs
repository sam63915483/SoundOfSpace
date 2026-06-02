using System.Collections;
using UnityEngine;

// Singleton driver for the "ate raw alien fish" trip effect. Lazy-creates the
// post-processing effect, appends it to every CustomPostProcessing in the
// scene on first trigger, and runs an envelope coroutine that fades intensity
// in → holds → fades out. Uses unscaled time so a paused Fishingdex doesn't
// freeze the trip.
//
// Trips have two phases (early/late) with a smooth crossfade between them, so
// callers can describe e.g. "wave for 5s then full kaleidoscope for 25s".
// Calling StartTrip during an active trip overwrites the phase config and
// extends the end time — the new "early" phase begins immediately.
public class RawFishTripController : MonoBehaviour
{
    public static RawFishTripController Instance { get; private set; }

    KaleidoscopeTripEffect _effect;
    Coroutine _activeRoutine;
    float _endUnscaledTime;

    // Phase config for the *current* StartTrip call. Reset every call; the
    // phase clock starts at the moment of the call.
    float _phaseStartTime;
    float _earlyKaleido, _earlyWave;
    float _earlyDuration;
    float _lateKaleido, _lateWave;
    float _colourScale = 1f;

    const float FadeIn        = 2f;
    const float FadeOut       = 3f;
    const float PhaseCrossfade = 1.5f;

    public static void StartTrip(
        float durationSeconds,
        float earlyKaleidoStrength, float earlyWaveStrength,
        float earlyPhaseDuration,
        float lateKaleidoStrength,  float lateWaveStrength,
        float colourScale = 1f)
    {
        if (Instance == null)
        {
            var go = new GameObject("RawFishTripController");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RawFishTripController>();
        }
        Instance.BeginTrip(durationSeconds,
            earlyKaleidoStrength, earlyWaveStrength,
            earlyPhaseDuration,
            lateKaleidoStrength, lateWaveStrength,
            colourScale);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void EnsureEffectRegistered()
    {
        if (_effect == null)
        {
            _effect = ScriptableObject.CreateInstance<KaleidoscopeTripEffect>();
            _effect.shader = Shader.Find("Hidden/RawFishTrip");
            if (_effect.shader == null)
                Debug.LogWarning("[RawFishTripController] Shader 'Hidden/RawFishTrip' not found — effect will pass through. (In builds, ensure it's in Project Settings → Graphics → Always Included Shaders.)");
        }

        // Register with EVERY CustomPostProcessing in the scene — there may be
        // one on the player camera and another on the ship camera, and only
        // the active one renders.
        var cppList = FindObjectsOfType<CustomPostProcessing>(true);
        if (cppList == null) return;

        for (int c = 0; c < cppList.Length; c++)
        {
            var cpp = cppList[c];
            bool already = false;
            if (cpp.effects != null)
            {
                for (int i = 0; i < cpp.effects.Length; i++)
                    if (cpp.effects[i] == _effect) { already = true; break; }
            }
            if (already) continue;

            int oldLen = cpp.effects != null ? cpp.effects.Length : 0;
            var newArr = new PostProcessingEffect[oldLen + 1];
            if (oldLen > 0) System.Array.Copy(cpp.effects, newArr, oldLen);
            newArr[oldLen] = _effect;
            cpp.effects = newArr;
        }
    }

    void BeginTrip(
        float durationSeconds,
        float earlyKaleidoStrength, float earlyWaveStrength,
        float earlyPhaseDuration,
        float lateKaleidoStrength,  float lateWaveStrength,
        float colourScale)
    {
        EnsureEffectRegistered();
        _endUnscaledTime = Mathf.Max(_endUnscaledTime, Time.unscaledTime + durationSeconds);

        // Overwrite phase config with the new trip's spec, restart the phase
        // clock from now — the new "early" phase plays first regardless of
        // what the prior trip was doing. Fade envelope is independent (driven
        // by trip start/end inside TripRoutine) so this won't re-fade-in if a
        // trip is already at peak.
        _phaseStartTime = Time.unscaledTime;
        _earlyKaleido   = Mathf.Clamp01(earlyKaleidoStrength);
        _earlyWave      = Mathf.Clamp01(earlyWaveStrength);
        _earlyDuration  = Mathf.Max(0f, earlyPhaseDuration);
        _lateKaleido    = Mathf.Clamp01(lateKaleidoStrength);
        _lateWave       = Mathf.Clamp01(lateWaveStrength);
        _colourScale    = Mathf.Clamp01(colourScale);

        if (_activeRoutine == null)
            _activeRoutine = StartCoroutine(TripRoutine());
    }

    IEnumerator TripRoutine()
    {
        float startTime = Time.unscaledTime;

        while (Time.unscaledTime < _endUnscaledTime)
        {
            float now = Time.unscaledTime;
            float remaining = _endUnscaledTime - now;
            float elapsed = now - startTime;

            float fadeIn  = Mathf.Clamp01(elapsed / FadeIn);
            float fadeOut = Mathf.Clamp01(remaining / FadeOut);
            float envelope = Mathf.Min(fadeIn, fadeOut);

            // Smoothstep crossfade between early and late phases, centred on
            // _earlyDuration (so half the crossfade happens before, half after).
            float phaseElapsed = now - _phaseStartTime;
            float crossStart = _earlyDuration - PhaseCrossfade * 0.5f;
            float lateBlend = Mathf.Clamp01((phaseElapsed - crossStart) / PhaseCrossfade);
            lateBlend = lateBlend * lateBlend * (3f - 2f * lateBlend);

            float kaleidoMax = Mathf.Lerp(_earlyKaleido, _lateKaleido, lateBlend);
            float waveMax    = Mathf.Lerp(_earlyWave,    _lateWave,    lateBlend);

            if (_effect != null)
            {
                _effect.intensity       = envelope * _colourScale;   // colour shift, scaled per-call (default 1)
                _effect.kaleidoStrength = envelope * kaleidoMax;
                _effect.waveStrength    = envelope * waveMax;
                _effect.tripTime        = now;
            }
            yield return null;
        }

        if (_effect != null)
        {
            _effect.intensity       = 0f;
            _effect.kaleidoStrength = 0f;
            _effect.waveStrength    = 0f;
            _effect.tripTime        = Time.unscaledTime;
        }
        _activeRoutine = null;
    }
}
