#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Editor side of the FPS hunt (runtime side: PerfTrace.cs).
///
/// 1. Keeps the Profiler window's frame buffer at Unity's maximum (2000 frames —
///    the slider in Preferences ▸ Analysis ▸ Profiler ▸ Frame Count). Runs once per
///    editor reload, idempotent, same pattern as EnableFrameTimingStats.
/// 2. Tools ▸ Solar System ▸ Perf ▸ Dump Profiler Snapshots: turns every .raw
///    snapshot PerfTrace wrote (End key in a dev build) into a plain-text
///    hierarchy report next to it — averaged over the snapshot, plus the worst
///    frames — so it can be read without scrubbing the Profiler window.
/// 3. Tools ▸ Solar System ▸ Perf ▸ Open Perf Folder: the CSV / .raw folder.
/// </summary>
[InitializeOnLoad]
static class PerfProfilerTools
{
    const int WantFrames = 2000;
    const string PrefKey = "ProfilerUserSettings.FrameCount";

    static PerfProfilerTools()
    {
        try { EnsureMaxFrameCount(false); }
        catch (Exception e) { Debug.LogWarning("[PerfProfilerTools] frame count: " + e.Message); }
    }

    static string PerfDir => Path.Combine(Application.persistentDataPath, "perf");

    [MenuItem("Tools/Solar System/Perf/Open Perf Folder")]
    static void OpenFolder()
    {
        Directory.CreateDirectory(PerfDir);
        EditorUtility.RevealInFinder(PerfDir);
    }

    [MenuItem("Tools/Solar System/Perf/Profiler Frame Count = 2000 (max)")]
    static void MenuEnsureFrames() => EnsureMaxFrameCount(true);

    static void EnsureMaxFrameCount(bool loud)
    {
        // ProfilerUserSettings is internal to UnityEditor; go through reflection
        // and fall back to the EditorPrefs key it is backed by.
        var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProfilerUserSettings");
        var prop = t != null ? t.GetProperty("frameCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
        int cur;
        if (prop != null)
        {
            cur = (int)prop.GetValue(null, null);
            if (cur < WantFrames) { prop.SetValue(null, WantFrames, null); Debug.Log("[PerfProfilerTools] Profiler frame count " + cur + " → " + WantFrames + " (Unity's maximum)."); }
            else if (loud) Debug.Log("[PerfProfilerTools] Profiler frame count already " + cur + ".");
            return;
        }
        cur = EditorPrefs.GetInt(PrefKey, 300);
        if (cur < WantFrames) { EditorPrefs.SetInt(PrefKey, WantFrames); Debug.Log("[PerfProfilerTools] Profiler frame count " + cur + " → " + WantFrames + " via EditorPrefs (restart the Profiler window to see it)."); }
        else if (loud) Debug.Log("[PerfProfilerTools] Profiler frame count already " + cur + ".");
    }

    // ==========================================================================
    [MenuItem("Tools/Solar System/Perf/Dump Profiler Snapshots (.raw → .txt)")]
    static void DumpAll()
    {
        if (!Directory.Exists(PerfDir)) { Debug.LogWarning("[PerfProfilerTools] no perf folder at " + PerfDir); return; }
        var raws = Directory.GetFiles(PerfDir, "*.raw");
        int done = 0;
        foreach (var raw in raws)
        {
            string txt = Path.ChangeExtension(raw, ".txt");
            if (File.Exists(txt) && File.GetLastWriteTimeUtc(txt) >= File.GetLastWriteTimeUtc(raw)) continue;
            try { Dump(raw, txt); done++; }
            catch (Exception e) { Debug.LogWarning("[PerfProfilerTools] " + Path.GetFileName(raw) + ": " + e.Message); }
        }
        Debug.Log("[PerfProfilerTools] dumped " + done + " snapshot(s) of " + raws.Length + " in " + PerfDir);
        if (raws.Length > 0) EditorUtility.RevealInFinder(PerfDir);
    }

    class Acc { public double total, self, calls; public int frames; }

    static void Dump(string raw, string txt)
    {
        if (!ProfilerDriver.LoadProfile(raw, false)) throw new Exception("LoadProfile failed");
        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
        if (first < 0 || last < first) throw new Exception("no frames");

        var sb = new StringBuilder(1 << 16);
        sb.AppendLine("PerfTrace snapshot: " + Path.GetFileName(raw));
        sb.AppendLine("frames " + first + ".." + last + " (" + (last - first + 1) + ")");

        // main-thread frame times
        var frames = new List<KeyValuePair<int, float>>();
        for (int f = first; f <= last; f++)
        {
            using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (v == null || !v.valid) continue;
                frames.Add(new KeyValuePair<int, float>(f, v.frameTimeMs));
            }
        }
        if (frames.Count == 0) throw new Exception("no valid main-thread frames");
        var sorted = new List<float>();
        foreach (var kv in frames) sorted.Add(kv.Value);
        sorted.Sort();
        double mean = 0; foreach (var x in sorted) mean += x; mean /= sorted.Count;
        sb.AppendLine(string.Format("frame ms  mean {0:0.00}  median {1:0.00}  p95 {2:0.00}  worst {3:0.00}   (≈ {4:0} fps mean)",
            mean, sorted[sorted.Count / 2], sorted[(int)(sorted.Count * 0.95)], sorted[sorted.Count - 1], 1000.0 / mean));
        sb.AppendLine();

        // threads present (probe frame = first)
        var threadNames = new List<string>();
        for (int ti = 0; ti < 32; ti++)
        {
            using (var v = ProfilerDriver.GetHierarchyFrameDataView(frames[0].Key, ti, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (v == null || !v.valid) break;
                threadNames.Add(v.threadName);
            }
        }
        sb.AppendLine("threads: " + string.Join(" | ", threadNames.ToArray()));
        sb.AppendLine();

        // averaged hierarchy for main thread (0) and the render thread
        DumpAverage(sb, frames, 0, "MAIN THREAD — average per frame over the snapshot (depth ≤ 5, ≥ 0.05 ms)");
        int renderIdx = threadNames.FindIndex(n => n != null && n.IndexOf("Render", StringComparison.OrdinalIgnoreCase) >= 0);
        if (renderIdx > 0) DumpAverage(sb, frames, renderIdx, "RENDER THREAD (" + threadNames[renderIdx] + ") — average per frame");

        // worst 3 frames, main thread
        frames.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int i = 0; i < Math.Min(3, frames.Count); i++)
            DumpFrame(sb, frames[i].Key, 0, "WORST FRAME #" + (i + 1) + "  frame " + frames[i].Key + "  " + frames[i].Value.ToString("0.00") + " ms");

        File.WriteAllText(txt, sb.ToString());
        Debug.Log("[PerfProfilerTools] wrote " + txt);
    }

    static void DumpAverage(StringBuilder sb, List<KeyValuePair<int, float>> frames, int thread, string title)
    {
        var acc = new Dictionary<string, Acc>();
        var children = new List<int>();
        int n = 0;
        foreach (var kv in frames)
        {
            using (var v = ProfilerDriver.GetHierarchyFrameDataView(kv.Key, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (v == null || !v.valid) continue;
                n++;
                Walk(v, v.GetRootItemID(), "", 0, 5, children, (path, total, self, calls) =>
                {
                    Acc a;
                    if (!acc.TryGetValue(path, out a)) { a = new Acc(); acc[path] = a; }
                    a.total += total; a.self += self; a.calls += calls; a.frames++;
                });
            }
        }
        if (n == 0) return;
        sb.AppendLine("== " + title + "  [" + n + " frames]");
        sb.AppendLine(string.Format("{0,9} {1,9} {2,7} {3,6}  {4}", "avg tot", "avg self", "calls", "in%fr", "path"));
        var rows = new List<KeyValuePair<string, Acc>>(acc);
        rows.Sort((a, b) => b.Value.total.CompareTo(a.Value.total));
        int printed = 0;
        foreach (var r in rows)
        {
            double avgTot = r.Value.total / n;
            if (avgTot < 0.05) continue;
            sb.AppendLine(string.Format("{0,9:0.000} {1,9:0.000} {2,7:0.0} {3,5:0}%  {4}", avgTot, r.Value.self / n, r.Value.calls / n, 100.0 * r.Value.frames / n, r.Key));
            if (++printed >= 120) break;
        }
        sb.AppendLine();
    }

    static void DumpFrame(StringBuilder sb, int frame, int thread, string title)
    {
        using (var v = ProfilerDriver.GetHierarchyFrameDataView(frame, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
        {
            if (v == null || !v.valid) return;
            sb.AppendLine("== " + title);
            sb.AppendLine(string.Format("{0,9} {1,9} {2,7}  {3}", "total", "self", "calls", "path"));
            var rows = new List<KeyValuePair<string, double[]>>();
            var children = new List<int>();
            Walk(v, v.GetRootItemID(), "", 0, 5, children, (path, total, self, calls) => rows.Add(new KeyValuePair<string, double[]>(path, new[] { total, self, calls })));
            rows.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
            int printed = 0;
            foreach (var r in rows)
            {
                if (r.Value[0] < 0.1) continue;
                sb.AppendLine(string.Format("{0,9:0.000} {1,9:0.000} {2,7:0}  {3}", r.Value[0], r.Value[1], r.Value[2], r.Key));
                if (++printed >= 60) break;
            }
            sb.AppendLine();
        }
    }

    static void Walk(HierarchyFrameDataView v, int id, string path, int depth, int maxDepth, List<int> scratch, Action<string, double, double, double> emit)
    {
        var kids = new List<int>();
        v.GetItemChildren(id, kids);
        foreach (var k in kids)
        {
            string name = v.GetItemName(k);
            string p = path.Length == 0 ? name : path + "/" + name;
            double total = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime);
            double self = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnSelfTime);
            double calls = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnCalls);
            emit(p, total, self, calls);
            if (depth + 1 < maxDepth && total >= 0.05) Walk(v, k, p, depth + 1, maxDepth, scratch, emit);
        }
    }
}
#endif
