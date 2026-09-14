using System.Collections;
using UnityEngine;

/// <summary>
/// Tutorial box, phase 1 (docs/superpowers/specs/2026-09-14-tutorial-box-design.md).
///
/// Scene object in Tutorial.unity. Runs the cut-down shuttle wake: you load in
/// standing in the stasis pod with NO blackout and NO wake-up text, the shuttle
/// is already descending from the top of the box, the pod door opens a moment
/// later, the autopilot parks at the 100 m hover and — exactly like the real
/// game — the cockpit computer shows the landing feed and waits for you to land
/// it (walk to the console, F, WASD / Q E, SPACE).
///
/// Everything after LaunchIntroApproach is the real travel code
/// (ShuttleAutopilot → Hover → ShuttleComputerNavUI → BeginLanding → Parked →
/// ramp opens, riders released). This script only stages the start and shows
/// one hint line at the hover.
///
/// What it deliberately does NOT do (compare IntroSequenceController):
///   - no eyelid overlay / click-to-wake / "OPEN YOUR EYES" prompt
///   - never sets EarlyGameProgress.IntroPlayed (global run state)
///   - never spawns NewGameResetRunner, so the deferred seed save never fires;
///     NewGameReset.Apply() IS run, so the tutorial always starts from fresh
///     new-game state (empty pockets, full-ish tank, story flags clear).
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    [Header("Fly-in")]
    [Tooltip("Metres ABOVE the shuttle's parked pose where the descent starts. The box ceiling is 200 m up and the parked shuttle sits ~3 m up, so ~185 keeps the whole hull under the ceiling.")]
    public float departAltitude = 185f;
    [Tooltip("Seconds from the start of the descent to the 100 m hover. The real intro uses 30 s over 4 km; the box is 90 m, so shorter.")]
    public float descentSeconds = 12f;
    [Tooltip("Seconds after load before the stasis pod door opens.")]
    public float doorOpenDelay = 1f;

    [Header("Hint (shown when the hover is reached, hidden on touchdown)")]
    public string landingHeader = "TUTORIAL · LAND THE SHUTTLE";
    [TextArea(2, 4)]
    public string landingHint = "Walk to the cockpit computer and press F. WASD moves the shuttle, Q / E turns it, and SPACE sets it down when the landing zone reads CLEAR.";

    [Header("World")]
    [Tooltip("Planet-baseline O2 (%) on the slab. Humble Abode starts around 55.")]
    [Range(0f, 100f)] public float surfaceOxygenPercent = 55f;

    static CelestialBody FindTutorialGround()
    {
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyType == CelestialBody.BodyType.Planet) return b;
        return null;
    }

    IEnumerator Start()
    {
        // The scene's NBodySimulation pins the physics rate in its Awake (both
        // bodies are pinned, so it never moves anything); re-assert it anyway.
        Time.fixedDeltaTime = Universe.physicsTimeStep;

        // Same timing as NewGameResetRunner: one frame + one physics tick so
        // every Start() and the first FixedUpdate have run first.
        yield return null;
        yield return new WaitForFixedUpdate();
        try { NewGameReset.Apply(); }
        catch (System.Exception e) { Debug.LogError("[Tutorial] NewGameReset.Apply failed: " + e); }
        // Apply() claims a "stasis pod N" slot for the run. The tutorial never
        // writes it (StasisPodSave is fenced), but leave nothing pointing at a
        // slot name all the same.
        StasisPodSave.ActiveSlotName = null;

        // Planet oxygen comes from the tree count over the planet's whole area;
        // a 10 km fake planet with ~25 trees is a dead world and the suit would
        // drain the moment you step off the ramp. Vent a reserve so the slab
        // breathes like Humble Abode does at the start (~55 %). Apply() cleared
        // the reserves just above, so this is the only source.
        var ground = FindTutorialGround();
        if (ground != null && PlanetOxygen.Instance != null)
            PlanetOxygen.Instance.AddVentedReserve(ground, surfaceOxygenPercent);

        var pilot = ShuttleAutopilot.EnsureAttached();
        if (pilot == null)
        {
            Debug.LogError("[Tutorial] No 'Shuttle_Lander' in the scene — rebuild it via Tools ▸ Solar System ▸ Build Tutorial Scene.");
            yield break;
        }
        var pc = FindObjectOfType<PlayerController>();

        // Shuttle to the top of the box (straight-in: no bezier lean, or it
        // would arc out through a wall), then the player into the pod at the
        // shuttle's NEW pose — same pod-local stand offset the intro uses.
        pilot.PrepareIntroApproach(departAltitude, straightIn: true);
        Transform podT = null;
        foreach (var t in pilot.GetComponentsInChildren<Transform>(true))
            if (t.name == "StasisPod") { podT = t; break; }
        var podDoor = pilot.GetComponentInChildren<StasisPodDoor>(true);
        if (pc != null && podT != null)
        {
            var prb = pc.Rigidbody;
            prb.velocity = Vector3.zero;
            prb.angularVelocity = Vector3.zero;
            Vector3 pos = podT.TransformPoint(new Vector3(0f, 1.02f, 0f));
            Quaternion rot = Quaternion.LookRotation(podT.forward, podT.up);
            prb.position = pos;
            prb.rotation = rot;
            pc.transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
        }
        pilot.CaptureIntroRiders();

        // While the pod door is shut: the pod-save watcher must not read
        // "sealed inside" as a load (ShuttleWakeActive), and the rider pin
        // must hold the player still so nothing drifts through the door
        // (isInDialogue — look yes, walk no). Both lift when the door opens.
        IntroSequenceController.ShuttleWakeActive = true;
        PlayerController.isInDialogue = true;

        // Everything above is staged while the loading screen holds at 100 %.
        // The engines light the moment the cover starts to fade, so the first
        // thing you see is a shuttle already under way (no first-second pops).
        yield return new WaitUntil(() => LoadingScreen.Instance == null || !LoadingScreen.Instance.IsHolding);

        pilot.LaunchIntroApproach(descentSeconds);   // engines light, descent begins

        yield return new WaitForSeconds(doorOpenDelay);
        PlayerController.isInDialogue = false;
        if (podDoor != null) podDoor.OpenHold();
        IntroSequenceController.ShuttleWakeActive = false;

        // Hover reached → the console is already on the landing feed. One hint.
        yield return new WaitUntil(() => pilot == null || pilot.CurrentPhase == ShuttleAutopilot.Phase.Hover);
        if (pilot == null) yield break;
        if (TutorialUI.Instance != null && !string.IsNullOrEmpty(landingHint))
            TutorialUI.Instance.ShowStep(landingHint, 0, 0, landingHeader);

        // Touchdown → the real code opens the ramp and releases the riders.
        yield return new WaitUntil(() => pilot == null || pilot.CurrentPhase == ShuttleAutopilot.Phase.Parked);
        if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();
    }

    void OnDestroy()
    {
        // Leaving mid-sequence (pause → MAIN MENU during the descent) must not
        // strand these statics for the next run.
        IntroSequenceController.ShuttleWakeActive = false;
        PlayerController.isInDialogue = false;
    }
}
