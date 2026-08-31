using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// PLAY-MODE ORBIT DIAGNOSTIC (read-only observer — touches nothing).
///
/// Planets don't spin, so a body's local solar day == its orbital period around
/// the sun. This probe measures that live: every FixedUpdate sample it unwraps
/// each body's swept angle around the sun and logs a line per completed lap
/// ("day"), plus a warning if a body's sun-distance leaves its starting orbit by
/// more than 25% (the off-the-rails detector). Mirrors the editor-side
/// OrbitDiagnostic script so the two tests can be compared number-for-number.
///
/// Output: Console lines prefixed [OrbitProbe], and a CSV at
/// Logs/orbit_probe_&lt;timestamp&gt;.csv (project root, gitignored; falls back to
/// persistentDataPath in builds). Columns:
/// gameTimeSec,body,event,lapSeconds,distToSun,r0,driftPctVsFirstLap
///
/// Leave the game running (AFK is fine) — the longer the soak, the better the
/// drift data. Loading a save teleports bodies; the probe auto-rebaselines a
/// body whose sun-distance jumps >30% in one sample, and logs that it did.
/// </summary>
public class OrbitClockProbe : MonoBehaviour
{
    const int SampleEverySteps = 5;          // 20 Hz at the 100 Hz physics step
    const float AnomalyFrac = 0.25f;         // r departing r0 by this fraction = off the rails
    const float RebaselineFrac = 0.30f;      // per-sample jump = save-load teleport, re-anchor

    class Track
    {
        public CelestialBody body;
        public Vector3 planeU, planeW;
        public float r0, lastR;
        // Angle accumulators are DOUBLE on purpose: cumAngle grows to hundreds
        // of radians while each 0.05s sample adds ~1e-3 rad, and float rounding
        // at that magnitude overcounted laps by ~1.5% (measured 53 laps in a
        // window that fit 51.4). The bodies were exact; the ruler wasn't.
        public double lastAngle, cumAngle;
        public double prevLapTime;
        public float firstLap = -1f;
        public int laps;
        public bool anomalyLogged;
    }

    readonly List<Track> tracks = new List<Track>();
    CelestialBody sun;
    int stepCounter;
    // DOUBLE, like the angle accumulators: a float clock summing +0.01 rounds
    // to +0.00977 once elapsed passes 8192 (ulp 0.001), reading 2.3% slow —
    // every "day length drift" plateau in early soaks was this stopwatch, not
    // the orbits.
    double elapsed;
    StreamWriter csv;

    void Start()
    {
        foreach (var b in NBodySimulation.Bodies)
        {
            if (b == null || b.isStaticAttractor) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) { sun = b; continue; }
        }
        if (sun == null) { enabled = false; return; }

        foreach (var b in NBodySimulation.Bodies)
        {
            if (b == null || b.isStaticAttractor || b == sun) continue;
            var t = new Track { body = b };
            Baseline(t);
            tracks.Add(t);
        }

        try
        {
            string dir = Application.isEditor ? Path.Combine(Directory.GetCurrentDirectory(), "Logs")
                                              : Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"orbit_probe_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            csv = new StreamWriter(path);
            csv.WriteLine("gameTimeSec,body,event,lapSeconds,distToSun,r0,driftPctVsFirstLap");
            Debug.Log($"[OrbitProbe] watching {tracks.Count} bodies -> {path}");
        }
        catch (System.Exception e) { Debug.LogWarning($"[OrbitProbe] no CSV ({e.Message}), console only"); }
    }

    void Baseline(Track t)
    {
        Vector3 r = t.body.Position - sun.Position;
        Vector3 n = Vector3.Cross(r, t.body.velocity - sun.velocity);
        if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
        t.planeU = r.normalized;
        t.planeW = Vector3.Cross(n.normalized, t.planeU);
        t.r0 = r.magnitude;
        t.lastR = t.r0;
        t.lastAngle = 0.0;
        t.cumAngle = 0.0;
        t.anomalyLogged = false;
    }

    void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        if (++stepCounter < SampleEverySteps) return;
        stepCounter = 0;
        if (sun == null) return;

        Vector3 sp = sun.Position;
        foreach (var t in tracks)
        {
            if (t.body == null) continue;
            Vector3 r = t.body.Position - sp;
            float rm = r.magnitude;

            // Save-load / warp teleport: re-anchor rather than record a fake lap.
            if (Mathf.Abs(rm - t.lastR) > RebaselineFrac * Mathf.Max(t.lastR, 1f))
            {
                Log(t, "rebaseline", 0f, rm);
                Baseline(t);
                continue;
            }
            t.lastR = rm;

            double ang = System.Math.Atan2(Vector3.Dot(r, t.planeW), Vector3.Dot(r, t.planeU));
            double d = ang - t.lastAngle;
            while (d > System.Math.PI) d -= 2.0 * System.Math.PI;
            while (d < -System.Math.PI) d += 2.0 * System.Math.PI;
            t.lastAngle = ang;
            double before = System.Math.Abs(t.cumAngle);
            t.cumAngle += d;

            if ((int)(System.Math.Abs(t.cumAngle) / (2.0 * System.Math.PI)) > (int)(before / (2.0 * System.Math.PI)))
            {
                t.laps++;
                float lapLen = (float)(t.laps == 1 ? elapsed : elapsed - t.prevLapTime);
                t.prevLapTime = elapsed;
                if (t.firstLap < 0f) t.firstLap = lapLen;
                float drift = (lapLen - t.firstLap) / t.firstLap * 100f;
                Debug.Log($"[OrbitProbe] {t.body.bodyName}: day #{t.laps} = {lapLen:0.0}s ({lapLen / 60f:0.0} min), drift {drift:+0.0;-0.0}% vs first, distToSun {rm:0} (start {t.r0:0})");
                Log(t, "lap", lapLen, rm);
            }

            if (!t.anomalyLogged && Mathf.Abs(rm - t.r0) > AnomalyFrac * t.r0)
            {
                t.anomalyLogged = true;
                Debug.LogWarning($"[OrbitProbe] {t.body.bodyName} OFF THE RAILS at t={elapsed / 60f:0.0} min: distToSun {rm:0} vs start {t.r0:0}");
                Log(t, "anomaly", 0f, rm);
            }
        }
    }

    void Log(Track t, string ev, float lapLen, float rm)
    {
        if (csv == null) return;
        float drift = (t.firstLap > 0f && lapLen > 0f) ? (lapLen - t.firstLap) / t.firstLap * 100f : 0f;
        csv.WriteLine($"{elapsed:0.00},{t.body.bodyName},{ev},{lapLen:0.00},{rm:0.0},{t.r0:0.0},{drift:0.00}");
        csv.Flush();
    }

    void OnDestroy()
    {
        if (csv != null) { csv.Close(); csv = null; }
    }
}
