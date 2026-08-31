using UnityEngine;
using System.Text;

/// <summary>
/// MENU-ONLY diagnostics (Sam's idea, modeled on the landing MegaTracker):
/// measures the shuttle tour and shot director every tick so jank shows up as
/// NUMBERS instead of needing eyeballs. Watches for:
///   • shuttle acceleration spikes (the "UFO jerk"),
///   • attitude turn-rate above the tour's cap,
///   • clearance-ratio dips (how close to any body's bubble, 1.0 = touching),
///   • camera angular speed above the director's cap,
///   • Planet shots with the planet off-frame or on its dark side.
/// Immediate [MenuTracker] WARN on violation (throttled), compact summary
/// every 30s. Added by MenuOrbitBootstrap; costs microseconds.
/// </summary>
public class MenuTourTracker : MonoBehaviour
{
    public MenuShuttleTour tour;
    public MenuShotDirector director;
    public Camera cam;

    [Tooltip("Shuttle acceleration above this (u/s^2) counts as a jerk. Note the " +
             "baseline: circular orbit itself is constant centripetal accel " +
             "(~21 u/s^2 on the Cyclops ring), so this sits well above physics.")]
    public float accelSpikeThreshold = 60f;

    Vector3 prevPos, prevVel;
    float prevTransferBlend;
    Quaternion prevRot;
    Quaternion prevCamRot;
    bool primed;

    // window aggregates
    int steps, accelSpikes, turnViolations, clearanceHits, camSpins;
    int planetShotFrames, planetVisibleFrames, planetLitFrames;
    float worstAccel, worstTurn, worstClearance = 99f, worstCamSpin;
    float windowStart;
    float lastWarnAt;

    CelestialBody sun;

    void Start()
    {
        if (tour == null) tour = FindObjectOfType<MenuShuttleTour>();
        if (director == null) director = FindObjectOfType<MenuShotDirector>();
        if (cam == null && director != null) cam = director.cam;
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyType == CelestialBody.BodyType.Sun) sun = b;
        windowStart = Time.time;
    }

    void FixedUpdate()
    {
        if (tour == null) return;
        float dt = Time.fixedDeltaTime;
        Vector3 pos = tour.transform.position;
        Quaternion rot = tour.transform.rotation;

        if (primed)
        {
            Vector3 vel = (pos - prevPos) / dt;
            float accel = (vel - prevVel).magnitude / dt;
            float turn = Quaternion.Angle(prevRot, rot) / dt;

            steps++;
            // Orbit<->transfer boundaries have one legitimate velocity-profile
            // step (orbit speed -> ease-in from zero); don't count it as jank.
            bool boundary = (prevTransferBlend <= 0f) != (tour.TransferBlend <= 0f)
                         || (prevTransferBlend >= 1f) != (tour.TransferBlend >= 1f);
            if (accel > worstAccel && !boundary) worstAccel = accel;
            if (turn > worstTurn) worstTurn = turn;
            if (accel > accelSpikeThreshold && !boundary) { accelSpikes++; Warn($"accel spike {accel:0} u/s2 near {tour.FocusBody.bodyName} (transferBlend {tour.TransferBlend:0.00})"); }
            // 2x margin: FixedUpdate ordering between tracker and tour is
            // undefined, so per-step sampling can double-count a rotation.
            if (turn > tour.maxTurnRate * 2f) { turnViolations++; Warn($"turn rate {turn:0.0} deg/s exceeds 2x cap {tour.maxTurnRate}"); }

            float minRatio = 99f;
            CelestialBody closest = null;
            bool orbiting = tour.TransferBlend <= 0f;
            foreach (var b in NBodySimulation.Bodies)
            {
                if (b == null || b.radius <= 0f) continue;
                // Mirror the tour's avoidance rule: while orbiting, the focus
                // planet's own bubble is legitimately grazed by the engineered
                // circle — only off-focus proximity is a violation there.
                if (orbiting && b == tour.FocusBody) continue;
                float bubble = b.radius * 1.5f + 40f;
                float ratio = Vector3.Distance(pos, b.Position) / bubble;
                if (ratio < minRatio) { minRatio = ratio; closest = b; }
            }
            if (closest == null) { prevVel = (pos - prevPos) / dt; prevPos = pos; prevRot = rot; return; }
            if (minRatio < worstClearance) worstClearance = minRatio;
            if (minRatio < 1.02f) { clearanceHits++; Warn($"clearance {minRatio:0.00} at {closest.bodyName} — bubble engaged"); }

            prevVel = vel;
        }
        else { primed = true; prevVel = Vector3.zero; }
        prevPos = pos;
        prevRot = rot;
        prevTransferBlend = tour.TransferBlend;
    }

    bool camPrimed;

    void LateUpdate()
    {
        // Evidence hotkey (Sam): F10 the instant something looks wrong — the
        // exact frame lands in the scratchpad with full context, no need to
        // describe times or angles.
        if (Input.GetKeyDown(KeyCode.F10))
        {
            string p = @"C:\Users\Sammc\AppData\Local\Temp\claude\C--Users-Sammc-Desktop-1ass-1aughhh1\832cb4ec-8638-4eb9-b2cb-36e2a3211295\scratchpad\sam_F10_" + System.DateTime.Now.ToString("HHmmss") + ".png";
            ScreenCapture.CaptureScreenshot(p);
            Debug.Log($"[MenuTracker] F10 capture -> {p} | focus={(tour != null && tour.FocusBody != null ? tour.FocusBody.bodyName : "?")}");
        }

        if (cam == null || tour == null) return;
        float dt = Time.deltaTime;
        if (!camPrimed) { camPrimed = true; prevCamRot = cam.transform.rotation; return; }
        if (dt > 0.0001f)
        {
            float camSpin = Quaternion.Angle(prevCamRot, cam.transform.rotation) / dt;
            if (camSpin > worstCamSpin) worstCamSpin = camSpin;
            // Sam-directed rig has no formal cap; only scream at true snaps.
            if (camSpin > 400f) { camSpins++; Warn($"camera snap {camSpin:0} deg/s"); }
        }
        prevCamRot = cam.transform.rotation;

        if (director != null && director.CurrentShotName == "Planet" && tour.FocusBody != null)
        {
            planetShotFrames++;
            Vector3 vp = cam.WorldToViewportPoint(tour.FocusBody.Position);
            if (vp.z > 0f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f)
                planetVisibleFrames++;
            if (sun != null)
            {
                Vector3 sunDir = (sun.Position - tour.FocusBody.Position).normalized;
                Vector3 shDir = (tour.transform.position - tour.FocusBody.Position).normalized;
                if (Vector3.Dot(sunDir, shDir) > -0.1f) planetLitFrames++;
            }
        }

        if (Time.time - windowStart >= 30f)
        {
            float visPct = planetShotFrames > 0 ? 100f * planetVisibleFrames / planetShotFrames : -1f;
            float litPct = planetShotFrames > 0 ? 100f * planetLitFrames / planetShotFrames : -1f;
            Debug.Log($"[MenuTracker] 30s window: steps={steps} | accelSpikes={accelSpikes} (worst {worstAccel:0.0}) | " +
                      $"turnViol={turnViolations} (worst {worstTurn:0.0}) | bubbleHits={clearanceHits} (closest {worstClearance:0.00}) | " +
                      $"camSpins={camSpins} (worst {worstCamSpin:0.0}) | planetShot: inFrame {visPct:0}% lit {litPct:0}% | mode={(director != null ? director.CurrentShotName : "SamCam")} focus={tour.FocusBody.bodyName}");
            steps = accelSpikes = turnViolations = clearanceHits = camSpins = 0;
            planetShotFrames = planetVisibleFrames = planetLitFrames = 0;
            worstAccel = worstTurn = worstCamSpin = 0f;
            worstClearance = 99f;
            windowStart = Time.time;
        }
    }

    void Warn(string msg)
    {
        if (Time.time - lastWarnAt < 1f) return;
        lastWarnAt = Time.time;
        Debug.LogWarning("[MenuTracker] " + msg);
    }
}
