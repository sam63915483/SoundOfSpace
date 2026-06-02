using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWindowsPlayer
{
    public static void Execute()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        Debug.Log($"[BuildWindowsPlayer] Building with scenes: {string.Join(", ", scenes)}");

        string buildDir = Path.Combine(Application.dataPath, "..", "Builds", "Win64");
        Directory.CreateDirectory(buildDir);
        string outPath = Path.Combine(buildDir, "Solar System 2.exe");

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        Debug.Log($"[BuildWindowsPlayer] Output: {outPath}");
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        Debug.Log($"[BuildWindowsPlayer] Result: {summary.result}  size: {summary.totalSize} bytes  errors: {summary.totalErrors}  warnings: {summary.totalWarnings}  time: {summary.totalTime}");
        if (summary.result == BuildResult.Succeeded)
            Debug.Log($"[BuildWindowsPlayer] BUILD SUCCEEDED — launch '{outPath}' to test.");
        else
            Debug.LogError($"[BuildWindowsPlayer] BUILD FAILED: {summary.result}");
    }
}
