using UnityEngine;

/// <summary>
/// The one number [BUILD] 2 needs: how high the sun is over the water where the
/// bobber is floating.
///
///   dot = Dot(surface normal under the bobber, direction to the sun)
///   +1 noon, 0 sunrise/sundown, -1 midnight
///
/// This is GEOMETRIC, taken from the n-body sim's actual sun position and the
/// planet you are standing on. It is deliberately NOT GalaxyTime: that clock is
/// decoupled from the orbital day/night on purpose, and using it here would make
/// the bite rate disagree with the sky the player is looking at.
///
/// The sun transform is the SunShadowCaster's, the same source EclipseShadowGate
/// measures from. Cached and re-searched at most once a second while missing —
/// never a FindObjectOfType per frame.
/// </summary>
public static class FishingSun
{
    static Transform _sun;
    static float _nextSearch;

    // Statics outlive scenes. Without this the cached sun is a destroyed object
    // after a load; the null check below copes, but clearing it is cheaper and
    // means the first lookup in a new scene is not delayed by the retry timer.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _sun = null; _nextSearch = 0f; }

    public static Transform SunTransform
    {
        get
        {
            if (_sun != null) return _sun;
            if (Time.time < _nextSearch) return null;
            _nextSearch = Time.time + 1f;
            var caster = Object.FindObjectOfType<SunShadowCaster>();
            if (caster != null) _sun = caster.transform;
            return _sun;
        }
    }

    /// <summary>
    /// Sun elevation at a point on a body, as a dot product in [-1, 1].
    /// Returns 0 (the twilight band, the neutral best case) when the sun or the
    /// body can't be resolved — a missing reference should not silently make
    /// fishing terrible.
    /// </summary>
    public static float SunDot(Vector3 worldPos, Transform body)
    {
        Transform sun = SunTransform;
        if (sun == null || body == null) return 0f;

        Vector3 up = worldPos - body.position;
        if (up.sqrMagnitude < 0.0001f) return 0f;
        up.Normalize();

        Vector3 toSun = sun.position - worldPos;
        if (toSun.sqrMagnitude < 0.0001f) return 0f;
        toSun.Normalize();

        return Vector3.Dot(up, toSun);
    }

    /// <summary>Human-readable band, for the dex / debug lines.</summary>
    public static string BandName(float dot)
    {
        float a = Mathf.Abs(dot);
        if (a <= FishingRules.TwilightEdge) return "twilight";
        return dot > 0f ? "day" : "night";
    }

    /// <summary>Clears the cached sun on scene change.</summary>
    public static void Forget() { _sun = null; _nextSearch = 0f; }
}
