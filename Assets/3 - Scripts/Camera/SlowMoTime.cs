using UnityEngine;

/// <summary>
/// Shared slow-mo timescale helper for SlowmoOnKill, KillShotCam, and
/// BladeSweep's hit-stop.
///
/// DO NOT scale Time.fixedDeltaTime here (tried once to smooth the ~7Hz
/// physics-stepped camera during 0.15x slow-mo): NBodySimulation integrates
/// every FixedUpdate by the CONSTANT Universe.physicsTimeStep — and owns
/// Time.fixedDeltaTime (sets it in Awake) — so a smaller fixed step makes the
/// PLANETS run ~7x faster than the slowed player. Sam's kill shot ended with
/// the planet orbiting out from under him ("everything shifts, I ended up in
/// space"). The slightly steppy camera during slow-mo is the accepted trade;
/// fixing it properly would mean making the whole world sim timestep-aware.
/// </summary>
public static class SlowMoTime
{
    public static void Apply(float timeScale)
    {
        Time.timeScale = timeScale;
    }

    public static void Restore()
    {
        Time.timeScale = 1f;
    }
}
