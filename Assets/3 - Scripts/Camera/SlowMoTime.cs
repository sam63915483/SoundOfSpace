using UnityEngine;

/// <summary>
/// Shared slow-mo timescale helper. Setting Time.timeScale alone leaves
/// Time.fixedDeltaTime at its game-time value, so at 0.15x physics steps only
/// ~7 times per REAL second (instead of 50) — the player body's yaw and every
/// physics-driven object visibly stutter while the world is slowed ("camera
/// feels really low frames"). Apply() scales the fixed timestep with the
/// timescale so physics keeps its normal real-time cadence (same CPU cost per
/// real second as normal play); Restore() puts both back.
///
/// Used by SlowmoOnKill, KillShotCam, and BladeSweep's hit-stop. Pause menus
/// set Time.timeScale = 0 directly and are unaffected.
/// </summary>
public static class SlowMoTime
{
    static float _baseFixedDelta = -1f;

    public static void Apply(float timeScale)
    {
        if (_baseFixedDelta < 0f) _baseFixedDelta = Time.fixedDeltaTime;
        Time.timeScale = timeScale;
        Time.fixedDeltaTime = _baseFixedDelta * Mathf.Max(0.01f, timeScale);
    }

    public static void Restore()
    {
        Time.timeScale = 1f;
        if (_baseFixedDelta > 0f) Time.fixedDeltaTime = _baseFixedDelta;
    }
}
