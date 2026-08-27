using System.Collections;
using System.IO;
using UnityEngine;

// Flight recorder for the autopilot (2026-08-27, playtest-3 debugging).
//
// When `build/shuttle-selftest.flag` exists next to the project (its text =
// target body name; blank = Icey Twin), pressing Play flies one full leg with
// no crew aboard (the D-1 abort is bypassed) and appends a trajectory log to
// `build/shuttle-selftest.log` four times a second: phase, parent, altitude
// over the target sphere, distances, speed, landing validity + fail reason.
// Auto-lands after 5 s of green hover.
//
// This exists so the flight can be flown and READ from outside the editor
// (Coplay play_game + the log) instead of shipping trajectory fixes blind.
// A normal play session is untouched: no flag file, no recorder. DELETE THE
// FLAG when done.
public class ShuttleTravelSelfTest : MonoBehaviour
{
    static string FlagPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "build", "shuttle-selftest.flag"));
    static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "build", "shuttle-selftest.log"));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => TryStart();
        TryStart();
    }

    static void TryStart()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        if (!File.Exists(FlagPath)) return;
        if (FindObjectOfType<ShuttleTravelSelfTest>() != null) return;
        var go = new GameObject("ShuttleTravelSelfTest");
        go.AddComponent<ShuttleTravelSelfTest>();
    }

    static void Log(string line)
    {
        try { File.AppendAllText(LogPath, line + "\n"); } catch { }
        Debug.Log("[ShuttleSelfTest] " + line);
    }

    IEnumerator Start()
    {
        Log("=== boot t=" + Time.time.ToString("0.0") + " scene=" + gameObject.scene.name);

        float guard = 0f;
        while ((ShuttleAutopilot.Instance == null || NBodySimulation.Bodies.Length == 0) && guard < 20f)
        {
            guard += Time.deltaTime;
            yield return null;
        }
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null) { Log("ABORT: no ShuttleAutopilot after 20s"); yield break; }
        yield return new WaitForSeconds(3f);   // let the scene settle

        // ⚠️ THE INTRO RUNS ON EDITOR PLAY (Sam has said this repeatedly): the
        // arrival sequence freezes the player kinematic and pins them to the
        // pod. Teleporting/flying while it owns the player produced every
        // "player floating in space" recorder run. Skip it via its own test
        // hook and wait for the release before touching anything.
        var intro = FindObjectOfType<ShuttleArrivalSequence>();
        float introGuard = 0f;
        while (intro != null && !ShuttleArrivalSequence.IsPlaying && introGuard < 6f)
        {
            introGuard += Time.deltaTime;
            yield return null;
        }
        if (intro != null && ShuttleArrivalSequence.IsPlaying)
        {
            Log("intro sequence playing — SkipNow()");
            intro.SkipNow();
            float w = 0f;
            while (ShuttleArrivalSequence.IsPlaying && w < 30f) { w += Time.deltaTime; yield return null; }
            Log("intro done (IsPlaying=" + ShuttleArrivalSequence.IsPlaying + ")");
            yield return new WaitForSeconds(2f);
        }
        else
        {
            Log("no intro playing (intro=" + (intro != null) + ")");
        }

        string target = "Icey Twin";
        try
        {
            var text = File.ReadAllText(FlagPath).Trim();
            if (text.Length > 0) target = text;
        }
        catch { }

        // Seat the player in the cabin first (SecondPlayerArrival's recipe) so
        // the recorded leg exercises the RIDER path too — a crewless flight
        // isn't the thing Sam actually plays.
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null && pc.Rigidbody != null)
        {
            Transform pod = null;
            foreach (var tr in pilot.GetComponentsInChildren<Transform>(true))
                if (tr.name == "StasisPod") { pod = tr; break; }
            Vector3 seat = pod != null ? pod.TransformPoint(new Vector3(0f, 1.5f, 0f))
                                       : pilot.transform.TransformPoint(new Vector3(0f, 2.5f, 0f));
            var prb = pc.Rigidbody;
            prb.position = seat;
            pc.transform.position = seat;
            var body = pilot.CurrentBody;
            pc.SetVelocity(body != null ? body.velocity : Vector3.zero);
            Physics.SyncTransforms();
            Log("player seated in cabin at " + seat);
            yield return new WaitForSeconds(1f);
            Log("post-seat: cabinLocal=" + pilot.transform.InverseTransformPoint(pc.transform.position).ToString("F2")
                + " IsInside=" + ShuttleRiderFrame.IsInside(pilot, pc.transform.position));
        }

        ShuttleAutopilot.DebugSkipCrewCheck = true;
        bool ok = pilot.RequestTravelByName(target);
        Log("RequestTravel '" + target + "' -> " + ok + "  from=" +
            (pilot.CurrentBody != null ? pilot.CurrentBody.bodyName : "?"));
        if (!ok) yield break;

        CelestialBody tgt = null;
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyName == target) { tgt = b; break; }

        float t = 0f;
        float hoverSince = -1f;
        bool landRequested = false;
        var lastPhase = pilot.CurrentPhase;
        while (t < 150f)
        {
            t += 0.25f;
            yield return new WaitForSeconds(0.25f);
            if (pilot == null) { Log("ABORT: autopilot destroyed"); break; }

            var phase = pilot.CurrentPhase;
            Vector3 shuttleW = pilot.transform.position;
            float altOverTarget = tgt != null ? Vector3.Distance(shuttleW, tgt.Position) - tgt.radius : -1f;
            string parent = pilot.transform.parent != null ? pilot.transform.parent.name : "NONE";
            Log("t=" + t.ToString("000.00")
                + " phase=" + phase
                + " parent=" + parent
                + " localMag=" + pilot.transform.localPosition.magnitude.ToString("0.0")
                + " altOverTgt=" + altOverTarget.ToString("0.0")
                + " vel=" + pilot.CurrentSpeed.ToString("0.0")
                + " hoverAlt=" + pilot.CurrentGroundAltitude.ToString("0.0")
                + " progress=" + pilot.TransitProgress.ToString("0.00")
                + " valid=" + (pilot.LandingValid ? "GREEN" : "red:" + pilot.LandingFailReason)
                + " rider=" + (PlayerController.RiderMode ? (PlayerController.DbgRiderGrounded ? "grounded" : "AIRBORNE") : "no"));

            if (phase != lastPhase)
            {
                Log(">>> PHASE " + lastPhase + " -> " + phase);
                lastPhase = phase;
            }

            if (phase == ShuttleAutopilot.Phase.Countdown)
            {
                var pcC = FindObjectOfType<PlayerController>();
                if (pcC != null)
                    Log("  countdown: cabinLocal=" + pilot.transform.InverseTransformPoint(pcC.transform.position).ToString("F2")
                        + " IsInside=" + ShuttleRiderFrame.IsInside(pilot, pcC.transform.position));
            }

            if (phase == ShuttleAutopilot.Phase.Hover)
            {
                if (hoverSince < 0f) hoverSince = t;
                if (!landRequested)
                {
                    if (pilot.LandingValid && t - hoverSince >= 2f)
                    {
                        landRequested = true;
                        bool landed = pilot.RequestLand();
                        Log(">>> RequestLand -> " + landed);
                    }
                    else
                    {
                        // Wander forward hunting a green spot — what a player
                        // does with WASD (also exercises the hover steering).
                        pilot.SetPilotInput(new Vector2(0f, 1f), 0f);
                    }
                }
            }

            if (phase == ShuttleAutopilot.Phase.Parked && t > 5f)
            {
                Log("=== PARKED — leg complete. body=" +
                    (pilot.CurrentBody != null ? pilot.CurrentBody.bodyName : "?") +
                    " localMag=" + pilot.transform.localPosition.magnitude.ToString("0.0"));
                // Release aftermath — the playtest-6 clip/launch window. The
                // player's cabin-local position must stay put and the body-
                // relative speed must stay walking-scale.
                var pc2 = FindObjectOfType<PlayerController>();
                var body2 = pilot.CurrentBody;
                for (int s = 0; s < 30; s++)
                {
                    yield return new WaitForSeconds(0.1f);
                    if (pc2 == null || pc2.Rigidbody == null) break;
                    Vector3 cabinLocal = pilot.transform.InverseTransformPoint(pc2.transform.position);
                    float relSpeed = (pc2.Rigidbody.velocity - (body2 != null ? body2.velocity : Vector3.zero)).magnitude;
                    Log("release+" + (s * 0.1f).ToString("0.0") + "s cabinLocal=" + cabinLocal.ToString("F2")
                        + " relSpeed=" + relSpeed.ToString("0.0")
                        + " grounded=" + (pc2.IsOnGround ? "Y" : "n"));
                }
                break;
            }
        }
        Log("=== end t=" + t.ToString("0.0"));
        ShuttleAutopilot.DebugSkipCrewCheck = false;
    }
}
