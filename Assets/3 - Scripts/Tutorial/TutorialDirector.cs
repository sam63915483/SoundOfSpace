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
/// ramp opens, riders released). This script only stages the start.
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
    [Tooltip("Metres ABOVE the shuttle's parked pose where the descent starts. The box ceiling is 350 m up and the parked shuttle sits ~3 m up, so ~330 keeps the whole hull under the ceiling.")]
    public float departAltitude = 330f;
    [Tooltip("Seconds from the start of the descent to the 100 m hover. The real intro uses 30 s over 4 km; the box is 230 m, so shorter.")]
    public float descentSeconds = 14f;
    [Tooltip("Seconds after the loading screen has fully faded out before the stasis pod door opens.")]
    public float doorOpenDelay = 1f;

    [Header("Diagnostics")]
    [Tooltip("Seconds after touchdown to log how many grass cells are streaming (Sam: grass never showed on the slab).")]
    public float grassReportDelay = 15f;

    /// Metres in from the wall that fish are kept. The box is 350 m across, so
    /// the half-width is 175; 160 keeps a spawn from landing ON the pane, where
    /// a fish would clip through the digit rain.
    const float FishFenceRadius = 160f;

    IEnumerator Start()
    {
        // Fence the ambient fish into the box. The ocean does not stop at the
        // walls, and the field spawns in a ring around the PLAYER, so without
        // this most of the fish end up in water the player can see through the
        // cage and never reach (Sam's playtest). The box is built at the world
        // origin, and nothing in this scene shifts the origin.
        AmbientFishField.SetPlayArea(Vector3.zero, FishFenceRadius);

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

        // The door waits for the cover to be GONE (the fade takes a second on
        // its own), then doorOpenDelay more — Sam, 2026-09-15: "open a second
        // after the fade in plays". Before this it opened as the fade ended.
        yield return new WaitUntil(() => LoadingScreen.Instance == null || !LoadingScreen.Instance.IsShowing);
        yield return new WaitForSeconds(doorOpenDelay);
        PlayerController.isInDialogue = false;
        if (podDoor != null) podDoor.OpenHold();
        IntroSequenceController.ShuttleWakeActive = false;

        // From here it is the real travel flow: hover, the console's landing
        // feed, F / WASD / Q E / SPACE, touchdown, ramp. No hint (Sam, round 3).
        yield return new WaitUntil(() => pilot == null || pilot.CurrentPhase == ShuttleAutopilot.Phase.Parked);

        // ── THE TANK IS DRY ON ARRIVAL (Sam, 2026-09-16) ──────────────────
        //
        // "once the shuttle lands in the tutorial, its out of fuel and cant
        // launch again." That is what turns the box's second board into a real
        // objective: the player has to go and find crystals before the computer
        // will take them anywhere.
        //
        // Emptied AFTER touchdown, not at the start, because the descent itself
        // is flown by the autopilot and a dry tank during it would fight the
        // landing. Landing first and THEN running dry also reads as a reason -
        // you used the last of it getting down.
        var tank = FindObjectOfType<ShuttleFuel>();
        if (tank != null)
        {
            tank.SetFuel(0f);
            Debug.Log("[Tutorial] Shuttle tank emptied on touchdown - refuel via the reactor to leave.");
        }
        else Debug.LogWarning("[Tutorial] No ShuttleFuel found - the shuttle will still have fuel and the refuel objectives are skippable.");

        yield return new WaitForSeconds(grassReportDelay);
        var grass = FindObjectOfType<InstancedGrassRenderer>();
        Debug.Log("[Tutorial] " + grassReportDelay + " s after touchdown: grass cells streaming = "
                  + (grass != null ? grass.ActiveCellCount.ToString() : "no InstancedGrassRenderer")
                  + ", player at " + (pc != null ? pc.transform.position.ToString("F0") : "?"));
    }

    /// <summary>
    /// L — SKIP TO THE END OF THE TUTORIAL.
    ///
    /// Sam, after walking the whole box twice only to find the TRAVEL button
    /// dead: "make it so i can rebuild and then load the tutorial and then just
    /// click a keyboard button thats not being used to skip the tutorial to the
    /// pick a destination part and make the reactor full so that i can just go
    /// straight to testing the button."
    ///
    /// Crosses off every objective except the last, which leaves the TV on the
    /// DEPARTURE board, and fills the tank so the computer will actually let you
    /// leave. Nothing else is touched: you still walk to the console and use the
    /// real screen, because that is the thing being tested.
    ///
    /// L is unused everywhere else in the project, and this only exists in
    /// Tutorial.unity — TutorialDirector is a scene object in the box and does
    /// not exist in the gameplay scene, so there is no way to press this by
    /// accident in the real game.
    /// </summary>
    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.L)) return;

        foreach (OrientationObjectives.Objective o in
                 System.Enum.GetValues(typeof(OrientationObjectives.Objective)))
        {
            if (o == OrientationObjectives.Objective.SelectDestination) continue;
            OrientationObjectives.Complete(o);
        }

        var tank = FindObjectOfType<ShuttleFuel>();
        if (tank != null) tank.SetFuel(tank.FuelMax);

        Debug.Log("[Tutorial] SKIP (L): objectives crossed off, tank filled to "
                  + (tank != null ? (tank.FuelPercent * 100f).ToString("0") + "%" : "<no tank>")
                  + ". Walk to the console and pick a destination.");
    }

    void OnDestroy()
    {
        // The field is a DontDestroyOnLoad singleton: leaving the fence set
        // would follow the player into the real game and empty its oceans.
        AmbientFishField.ClearPlayArea();

        // Leaving mid-sequence (pause → MAIN MENU during the descent) must not
        // strand these statics for the next run.
        IntroSequenceController.ShuttleWakeActive = false;
        PlayerController.isInDialogue = false;
    }
}
