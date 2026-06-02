#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ensures PlayerSettings.enableFrameTimingStats is on. Without this,
/// FrameTimingManager.GetLatestTimings() returns 0 for gpuFrameTime on the
/// Windows player, and the FPSOverlay's "gpu" line stays at "--" forever.
///
/// Runs once per editor reload via [InitializeOnLoad]. Idempotent — only
/// touches the setting if it's currently off, so it won't dirty the
/// ProjectSettings repeatedly.
/// </summary>
[InitializeOnLoad]
static class EnableFrameTimingStats
{
    static EnableFrameTimingStats()
    {
        if (!PlayerSettings.enableFrameTimingStats)
        {
            PlayerSettings.enableFrameTimingStats = true;
            Debug.Log("[EnableFrameTimingStats] Enabled PlayerSettings.enableFrameTimingStats — required for FPSOverlay's GPU frame time readout.");
        }
    }
}
#endif
