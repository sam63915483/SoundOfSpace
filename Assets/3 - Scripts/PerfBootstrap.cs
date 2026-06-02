using UnityEngine;

/// <summary>
/// Tiny startup hook that strips stack-trace capture for `Debug.Log` calls.
///
/// In development builds, Unity captures a stack trace for every Debug.Log /
/// Debug.LogWarning / etc. and routes it through StackTraceUtility.ExtractStackTrace,
/// which allocates ~7 KB per call. TextMeshPro (and other middleware) emits
/// occasional Debug.Log lines during its lazy initialization — each one
/// triggered a multi-millisecond stutter spike when the player first walked
/// into a UI panel that hadn't been touched yet (561 KB GC frame observed).
///
/// We only strip traces for LogType.Log. Warning / Error / Exception keep
/// their traces, so real diagnostics still work — we're just refusing to pay
/// the trace cost for informational logs that nobody reads.
///
/// Runs BeforeSceneLoad so it applies before MainMenu's TMP components Awake.
/// </summary>
public static class PerfBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DisableLogStackTraces()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
    }
}
