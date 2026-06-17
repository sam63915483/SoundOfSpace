using UnityEngine;

/// <summary>
/// Mission 1 Pilot branch — the flight test (user's design). The instructor charges $20 and
/// calls BeginTest, which drops the player into the VR drone (DroneController). The drone
/// gains physics and settles onto the launchpad; the player takes off when ready. Pass = take
/// off, orbit Humble Abode once, and land back on the launchpad. Fail = take the goggles off
/// (the drone's two-press F abort) OR crash (a hard collision, reported by the drone).
/// Passing grants the galactic licence (Mission1.FlagLicensed).
///
/// Takeoff/orbit/land are transform math (distance to the pad + angle swept around the planet);
/// crashes come from the drone's physics collisions. Place one of these in the scene (e.g. on
/// SHIPSCHOOL) and wire the drone, launchpad, and an exit point.
/// </summary>
public class ShipPilotTest : MonoBehaviour
{
    public static ShipPilotTest Instance { get; private set; }

    [Header("Wiring")]
    public DroneController drone;
    [Tooltip("The launchpad transform — takeoff/landing reference.")]
    public Transform launchPad;
    [Tooltip("Where the player is set down when the test ends. Defaults to the launchpad.")]
    public Transform playerExitPoint;

    [Header("Tuning")]
    [Tooltip("Distance from the pad that counts as 'taken off'.")]
    public float takeoffDistance = 30f;
    [Tooltip("Distance to the pad that counts as 'landed' (after one orbit).")]
    public float landDistance = 25f;
    [Tooltip("Degrees swept around the planet for one orbit. 240 = two-thirds of a lap, so it registers without forcing a full+ loop.")]
    public float orbitDegrees = 240f;
    [Tooltip("Seconds after entry during which a collision won't fail you (covers the settle onto the pad).")]
    public float crashGraceSeconds = 1f;
    [Tooltip("Tev trust granted on passing.")]
    public float trustOnLicence = 1f;

    const string InstructorWaypointId = "m1_flight_instructor";

    CelestialBody _planet;
    bool _flying;
    bool _tookOff;
    bool _orbited;
    float _cumAngle;
    bool _haveLastProj;
    Vector3 _lastProj;
    float _crashGraceUntil;

    Vector3 _droneStartLocalPos;
    Quaternion _droneStartLocalRot;
    Vector3 _playerReturnLocalPos;
    Quaternion _playerReturnLocalRot;

    public bool TestActive => _flying;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        if (drone == null) drone = FindObjectOfType<DroneController>();
        if (drone != null)
        {
            _droneStartLocalPos = drone.transform.localPosition;
            _droneStartLocalRot = drone.transform.localRotation;
            drone.OnExitConfirmed += HandleAbort;
            drone.OnCrashed += HandleCrash;
        }
        _planet = FindPlanet("Humble Abode");
    }

    void OnDestroy()
    {
        if (drone != null) { drone.OnExitConfirmed -= HandleAbort; drone.OnCrashed -= HandleCrash; }
        if (Instance == this) Instance = null;
    }

    static CelestialBody FindPlanet(string name)
    {
        foreach (var b in FindObjectsOfType<CelestialBody>())
            if (b != null && b.bodyName == name) return b;
        return null;
    }

    /// <summary>Called by the instructor after the player pays. Drops them into the drone.</summary>
    public void BeginTest()
    {
        if (_flying || drone == null) return;
        if (_planet == null) _planet = FindPlanet("Humble Abode");

        var player = FindObjectOfType<PlayerController>(true);
        if (player == null) return;

        _tookOff = false;
        _orbited = false;
        _cumAngle = 0f;
        _haveLastProj = false;

        // Remember exactly where the player was standing (relative to the school, so a
        // floating-origin shift mid-flight doesn't invalidate it) so we can put them right
        // back there when the test ends.
        _playerReturnLocalPos = transform.InverseTransformPoint(player.transform.position);
        _playerReturnLocalRot = Quaternion.Inverse(transform.rotation) * player.transform.rotation;

        drone.Enter(player);
        _flying = true;
        _crashGraceUntil = Time.time + crashGraceSeconds;

        if (HALCommentator.Instance != null)
            HALCommentator.Instance.VolunteerExternal("Goggles on. Take off, fly one full lap around Humble Abode, then set down on the pad.");
    }

    void FixedUpdate()
    {
        if (!_flying || drone == null || _planet == null || launchPad == null) return;

        Vector3 dronePos  = drone.Body != null ? drone.Body.position : drone.transform.position;
        Vector3 planetPos = _planet.Position;
        float distToPad    = Vector3.Distance(dronePos, launchPad.position);

        if (!_tookOff)
        {
            if (distToPad > takeoffDistance) _tookOff = true;
            return;   // wait until they're actually airborne
        }

        // Orbit accumulation — total angle swept around the planet, plane-agnostic: the angle
        // between consecutive radial directions. A full lap = 360° regardless of the orbit
        // plane, so an inclined/tilted loop registers the same as an equatorial one (the old
        // signed-angle-around-the-pole method under-counted inclined orbits, forcing extra laps).
        Vector3 radial = (dronePos - planetPos).normalized;
        if (_haveLastProj) _cumAngle += Vector3.Angle(_lastProj, radial);
        _lastProj = radial;
        _haveLastProj = true;
        if (!_orbited && _cumAngle >= orbitDegrees)
        {
            _orbited = true;
            if (HALCommentator.Instance != null)
                HALCommentator.Instance.VolunteerExternal("That's one orbit. Bring it back down onto the pad.");
        }

        // Pass — back on the pad after a full orbit.
        if (_orbited && distToPad < landDistance)
        {
            Pass();
        }
    }

    void HandleAbort()
    {
        if (_flying) Fail("Goggles off. Test aborted.");
    }

    // Crash comes from the drone's physics (a hard collision). A short grace after entry
    // ignores the gentle settle onto the pad.
    void HandleCrash()
    {
        if (_flying && Time.time >= _crashGraceUntil) Fail("You crashed. Goggles off — try again.");
    }

    void Pass()
    {
        // The licence IS the Mission 1 Pilot-branch completion. There's no ship to fly yet —
        // buying one (which needs the licence) and the trip to Constant Companion are Mission 2.
        // One-time rewards fire only on the FIRST pass; the course can be re-run for practice.
        bool firstTime = !Mission1.Get(Mission1.FlagLicensed);
        Mission1.Set(Mission1.FlagLicensed, true);

        if (firstTime)
        {
            Mission1.Set(Mission1.FlagComplete, true);
            var sd = StoryDirector.Instance;
            if (sd != null)
            {
                sd.SetStoryStep(StoryStep.Mission1Complete);
                sd.AddTrust(trustOnLicence);
                sd.CompleteObjective("obj_pilot_school");
            }
            if (HALCommentator.Instance != null)
                HALCommentator.Instance.VolunteerExternal("Clean run - galactic pilot's licence granted. That's the first mission done. You'll need that licence to buy a ship of your own.");
            if (CompassHUD.Instance != null)
                CompassHUD.Instance.RemoveWaypoint(InstructorWaypointId);
        }
        else if (HALCommentator.Instance != null)
        {
            HALCommentator.Instance.VolunteerExternal("Clean run again. Nice flying.");
        }

        EndTest();
    }

    void Fail(string message)
    {
        if (HALCommentator.Instance != null) HALCommentator.Instance.VolunteerExternal(message);
        EndTest();
    }

    void EndTest()
    {
        _flying = false;

        Vector3 exitPos;
        Quaternion exitRot;
        if (playerExitPoint != null)
        {
            exitPos = playerExitPoint.position;
            exitRot = playerExitPoint.rotation;
        }
        else
        {
            // Back to exactly where they were standing (not the launchpad — that dropped them
            // inside the pad collider / through the planet).
            exitPos = transform.TransformPoint(_playerReturnLocalPos);
            exitRot = transform.rotation * _playerReturnLocalRot;
        }
        drone.Exit(exitPos, exitRot);

        // Park the drone back on the pad.
        drone.transform.localPosition = _droneStartLocalPos;
        drone.transform.localRotation = _droneStartLocalRot;
        if (drone.Body != null) { drone.Body.velocity = Vector3.zero; drone.Body.angularVelocity = Vector3.zero; }
    }
}
