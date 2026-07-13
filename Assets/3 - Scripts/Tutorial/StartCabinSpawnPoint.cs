using UnityEngine;

// Drop this on a GameObject (or empty child Transform) inside the StartCabin —
// wherever you want the player to wake up. The player's first-game spawn
// position will be this transform's position + rotation, with orbital velocity
// inherited from the planet this object is parented under.
//
// What it does on Awake (only when starting a fresh game — skipped when
// loading a save, since the save system already positions the player):
//   1. Sets PlayerController.spawnPoint to this transform.
//   2. Sets GameSetUp.startCondition to AtSpawnPoint so it doesn't auto-pilot
//      the player into the ship at scene start.
//   3. Suppresses the old "ship crash imminent" warning HUD that the legacy
//      TutorialManager schedules for ~5 seconds in.
//   4. Unlocks all TutorialGate abilities so the player can move/look freely
//      while we build out the new tutorial step-by-step. Once the new flow
//      is wired up, the steps will lock/unlock abilities progressively and
//      this UnlockAll can be removed.
//
// [DefaultExecutionOrder(100)] makes sure this runs AFTER TutorialManager.Awake
// (which calls TutorialGate.LockAll) but BEFORE any Start callback — Awakes
// always finish before Starts begin, regardless of order.
[DefaultExecutionOrder(100)]
public class StartCabinSpawnPoint : MonoBehaviour
{
    [Tooltip("Optional override. If set, the player spawns at THIS transform's position+rotation instead of this GameObject's. Useful when you want to keep the spawn-anchor as a child of the cabin without moving the cabin itself.")]
    public Transform spawnTransform;

    void Awake()
    {
        // Save loading takes precedence — let SaveCollector position the
        // player from save data. Don't fight it.
        if (PendingLoad.Data != null) return;

        var spawnT = spawnTransform != null ? spawnTransform : transform;

        var player = FindObjectOfType<PlayerController>(true);
        if (player != null)
        {
            player.spawnPoint = spawnT;
        }
        else
        {
            Debug.LogWarning("[StartCabinSpawnPoint] No PlayerController found in scene; spawn override skipped.");
        }

        var gameSetup = FindObjectOfType<GameSetUp>();
        if (gameSetup != null)
        {
            gameSetup.startCondition = GameSetUp.StartCondition.AtSpawnPoint;
        }
        else
        {
            Debug.LogWarning("[StartCabinSpawnPoint] No GameSetUp found in scene; auto-pilot suppression skipped.");
        }

        // BeginTutorial is deferred to Start (below) — TutorialUI auto-creates
        // in RuntimeInitializeOnLoadMethod(AfterSceneLoad), which fires after
        // all Awakes. Calling BeginTutorial in Awake would race with that:
        // ShowStep(0) would skip the UI ('TutorialUI.Instance is null') and
        // the player would never see a tip. By Start, AfterSceneLoad has
        // run and TutorialUI.Instance is guaranteed non-null.

        // Freeze the ship — kinematic + zero velocity so it doesn't fall and
        // crash into the planet. The old tutorial gated on Ship.OnShipCollision,
        // so a free-falling ship at scene start would trigger it. Also sets
        // canFly = false so the player can't accidentally take off if they
        // wander into the pilot seat.
        // Skip mission-prop ships (e.g. Tevsship): they manage their own
        // parked physics, and freezing one here would leave the MAIN ship
        // un-frozen. Same guard as GameSetUp.
        Ship ship = null;
        foreach (Ship s in FindObjectsOfType<Ship>())
        {
            if (s.GetComponent<TevSmugglingMission>() != null) continue;
            ship = s;
            break;
        }
        if (ship != null)
        {
            var shipRb = ship.Rigidbody;
            if (shipRb != null)
            {
                shipRb.velocity = Vector3.zero;
                shipRb.angularVelocity = Vector3.zero;
                shipRb.isKinematic = true;
            }
            ship.canFly = false;
        }
    }

    void Start()
    {
        if (PendingLoad.Data != null) return;

        // Vertical-slice opening: no forced tutorial. Spawn free; the phone-AI drives guidance.
        if (StoryDirector.Instance != null) StoryDirector.Instance.SetStoryStep(StoryStep.ColdOpen);
        // Ensure no abilities are gated on the new path:
        TutorialGate.UnlockAll();
    }
}
