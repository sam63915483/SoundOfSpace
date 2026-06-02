using UnityEngine;
using System.Collections;

public class ThrusterDetachOnImpact : MonoBehaviour
{
    // Debug toggle — when true, hard crashes never escalate to part detachment.
    // Toggled from the backtick debug menu (GravityDebugUI) so we don't have
    // to rebuild the ship every test run.
    public static bool DisableHardCrashes = false;

    public float impactVelocityThreshold = 5f;

    [Header("Thrusters")]
    public GameObject leftThrusterChild;
    public GameObject rightThrusterChild;
    public GameObject thrusterLeftPickupPrefab;
    public GameObject thrusterRightPickupPrefab;

    [Header("Satellite Dish")]
    public GameObject dishChild;                     // The dish model on the ship
    public GameObject dishPickupPrefab;              // The pickup prefab for the dish
    public Vector3 dishSpawnOffset = new Vector3(0f, 0.5f, 0.5f); // 👈 Custom spawn offset

    [Header("Solar Panel")]
    public GameObject solarPanelChild;
    public GameObject solarPanelPickupPrefab;
    public Vector3 solarPanelSpawnOffset = new Vector3(0f, 0.5f, 0.5f);

    [Header("Space Nets (optional — present on SHIP44, absent on legacy ships)")]
    [Tooltip("The left-side SpaceNet GameObject (the on-ship visual rim/arm/net). Null on ships that don't support nets.")]
    public GameObject leftSpaceNetChild;
    [Tooltip("The right-side SpaceNet GameObject. Null on ships that don't support nets.")]
    public GameObject rightSpaceNetChild;
    [Tooltip("Pickup prefab spawned at the left net's position when the ship crashes hard.")]
    public GameObject spaceNetLeftPickupPrefab;
    [Tooltip("Pickup prefab spawned at the right net's position when the ship crashes hard.")]
    public GameObject spaceNetRightPickupPrefab;

    [Header("UI")]
    public CrashWarningFader crashWarningFader;

    [Header("Detach Pop Force")]
    public float popForceOutward = 1.5f;
    public float popForceUpward = 2.0f;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip detachClip;
    [SerializeField, Range(0, 1)] private float detachVolume = 0.7f;

    private bool thrustersDetached = false;
    private bool dishDetached = false;
    private bool solarPanelDetached = false;
    private bool detachmentScheduled = false;
    private Ship shipController;

    void Start()
    {
        shipController = GetComponent<Ship>();
        if (shipController == null)
            Debug.LogError("Ship component not found on this GameObject!");
    }

    void OnCollisionEnter(Collision collision)
    {
        // Prior code also early-returned on thrustersDetached || dishDetached
        // here, which broke crash detection for Hull / NoDish ships (those
        // flags start true by design when the corresponding part isn't shipped
        // with the tier). DetachAllParts is idempotent — each branch's
        // `activeSelf` check skips already-detached parts — so we only need
        // the in-flight scheduling guard to prevent overlapping countdowns.
        if (detachmentScheduled) return;

        // Only allow catastrophic detachment while the player is actually piloting
        // the ship. An idle ship being bumped by an enemy or jostled by physics
        // glitches should never lose its parts — only a real crash during flight
        // should count as a "medium/hard" impact for detachment purposes.
        if (shipController == null || !shipController.IsPiloted) return;

        // Debug: skip the hard-crash escalation entirely if the test toggle is on.
        if (DisableHardCrashes) return;

        if (collision.relativeVelocity.magnitude > impactVelocityThreshold)
        {
            StartCoroutine(DelayedDetach(3f));
        }
    }

    IEnumerator DelayedDetach(float delay)
    {
        detachmentScheduled = true;
        Debug.Log($"Catastrophic damage to vessel!\nMessage self destruct in: {delay} seconds...");

        if (crashWarningFader != null)
            crashWarningFader.ShowCountdownWarning(3);
        else
            Debug.LogError("CrashWarningFader reference not assigned!");

        yield return new WaitForSeconds(delay);
        DetachAllParts();
        detachmentScheduled = false;
    }

    void DetachAllParts()
    {
        // Disable flight until all parts are reattached
        if (shipController != null) shipController.canFly = false;

        ReactivateMountPoints();

        // Detach left thruster
        if (leftThrusterChild != null && leftThrusterChild.activeSelf)
        {
            leftThrusterChild.SetActive(false);
            SpawnPickup(leftThrusterChild.transform, thrusterLeftPickupPrefab, "Left");
        }

        // Detach right thruster
        if (rightThrusterChild != null && rightThrusterChild.activeSelf)
        {
            rightThrusterChild.SetActive(false);
            SpawnPickup(rightThrusterChild.transform, thrusterRightPickupPrefab, "Right");
        }

        // Detach satellite dish with custom offset
        if (dishChild != null && dishChild.activeSelf)
        {
            dishDetached = true;
            dishChild.SetActive(false);
            SpawnPickup(dishChild.transform, dishPickupPrefab, "Dish", dishSpawnOffset);
        }

        // Detach solar panel with custom offset
        if (solarPanelChild != null && solarPanelChild.activeSelf)
        {
            solarPanelDetached = true;
            solarPanelChild.SetActive(false);
            SpawnPickup(solarPanelChild.transform, solarPanelPickupPrefab, "Solar", solarPanelSpawnOffset);
        }

        // Detach left SpaceNet
        if (leftSpaceNetChild != null && leftSpaceNetChild.activeSelf)
        {
            leftSpaceNetChild.SetActive(false);
            SpawnPickup(leftSpaceNetChild.transform, spaceNetLeftPickupPrefab, "SpaceNetLeft");
        }

        // Detach right SpaceNet
        if (rightSpaceNetChild != null && rightSpaceNetChild.activeSelf)
        {
            rightSpaceNetChild.SetActive(false);
            SpawnPickup(rightSpaceNetChild.transform, spaceNetRightPickupPrefab, "SpaceNetRight");
        }

        thrustersDetached = (leftThrusterChild != null || rightThrusterChild != null);
    }

    void ReactivateMountPoints()
    {
        ThrusterMount[] mounts = GetComponentsInChildren<ThrusterMount>(true);
        foreach (ThrusterMount mount in mounts)
            mount.gameObject.SetActive(true);
        Debug.Log($"Reactivated {mounts.Length} mount point(s).");
    }

    void SpawnPickup(Transform spawnPoint, GameObject pickupPrefab, string partType, Vector3 customOffset = default(Vector3))
    {
        if (pickupPrefab == null)
        {
            Debug.LogError($"No pickup prefab for {partType}!");
            return;
        }

        // Use custom offset if provided, otherwise default forward * 0.3f
        Vector3 offset = (customOffset != default(Vector3)) ? customOffset : spawnPoint.forward * 0.3f;
        Vector3 spawnPos = spawnPoint.position + spawnPoint.TransformDirection(offset);

        if (detachClip != null)
            AudioSource.PlayClipAtPoint(detachClip, spawnPos, detachVolume);

        GameObject pickup = Instantiate(pickupPrefab, spawnPos, spawnPoint.rotation);

        ThrusterPickup pickupScript = pickup.GetComponent<ThrusterPickup>();
        if (pickupScript != null)
            pickupScript.thrusterType = partType;

        Rigidbody rb = pickup.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.velocity = GetComponent<Rigidbody>().velocity;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.useGravity = false;

            // Solar panel uses ship-relative axes because its 90° local rotation makes
            // spawnPoint.forward/up point in unintended directions; everything else uses
            // its own axes (existing thruster/dish behavior, unchanged).
            Vector3 forwardAxis = (partType == "Solar") ? transform.forward : spawnPoint.forward;
            Vector3 upAxis      = (partType == "Solar") ? transform.up      : spawnPoint.up;
            Vector3 popDirection = forwardAxis * popForceOutward + upAxis * popForceUpward;
            rb.AddForce(popDirection, ForceMode.VelocityChange);
        }

        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null) em.RegisterPhysicsObject(pickup.transform);

        PickupMarker marker = pickup.GetComponent<PickupMarker>();
        if (marker != null && PickupUIManager.Instance != null)
            PickupUIManager.Instance.RegisterPickup(marker);
    }

    // Returns the scene child that ReattachPart would activate for this type.
    // Used by ThrusterMount to clone the "final placed" geometry for the
    // translucent placement-preview ghost without duplicating the type→child
    // switch in two places.
    public GameObject GetChildForType(string partType)
    {
        switch (partType)
        {
            case "Left":         return leftThrusterChild;
            case "Right":        return rightThrusterChild;
            case "Dish":         return dishChild;
            case "Solar":        return solarPanelChild;
            case "SpaceNetLeft": return leftSpaceNetChild;
            case "SpaceNetRight":return rightSpaceNetChild;
            default:             return null;
        }
    }

    public void ReattachPart(string partType)
    {
        if (partType == "Left" && leftThrusterChild != null)
            leftThrusterChild.SetActive(true);
        else if (partType == "Right" && rightThrusterChild != null)
            rightThrusterChild.SetActive(true);
        else if (partType == "Dish" && dishChild != null)
        {
            dishChild.SetActive(true);
            dishDetached = false;
        }
        else if (partType == "Solar" && solarPanelChild != null)
        {
            solarPanelChild.SetActive(true);
            solarPanelDetached = false;
        }
        else if (partType == "SpaceNetLeft" && leftSpaceNetChild != null)
            leftSpaceNetChild.SetActive(true);
        else if (partType == "SpaceNetRight" && rightSpaceNetChild != null)
            rightSpaceNetChild.SetActive(true);

        bool leftOK = leftThrusterChild == null || leftThrusterChild.activeSelf;
        bool rightOK = rightThrusterChild == null || rightThrusterChild.activeSelf;
        bool dishOK = dishChild == null || dishChild.activeSelf;
        bool solarOK = solarPanelChild == null || solarPanelChild.activeSelf;

        Debug.Log($"Parts status - Left: {leftOK}, Right: {rightOK}, Dish: {dishOK}, Solar: {solarOK}");

        // Flight requires BOTH thrusters. The dish (orbital tracking only) and
        // solar panel are not required for flight — they're optional upgrades.
        thrustersDetached = !(leftOK && rightOK);
        dishDetached = !dishOK;
        solarPanelDetached = !solarOK;
        if (shipController != null)
        {
            shipController.canFly = leftOK && rightOK;
            if (shipController.canFly)
                Debug.Log("Thrusters attached — flight enabled.");
        }
    }

    // Keep compatibility with ThrusterMount
    public void ReattachThruster(string thrusterType)
    {
        ReattachPart(thrusterType);
    }

    // Save/load: directly mark each child active/inactive without spawning pickups.
    public void ApplyAttachment(bool leftAttached, bool rightAttached, bool dishAttached, bool solarAttached)
    {
        if (leftThrusterChild != null)  leftThrusterChild.SetActive(leftAttached);
        if (rightThrusterChild != null) rightThrusterChild.SetActive(rightAttached);
        if (dishChild != null)          dishChild.SetActive(dishAttached);
        if (solarPanelChild != null)    solarPanelChild.SetActive(solarAttached);

        thrustersDetached = !(leftAttached && rightAttached);
        dishDetached = !dishAttached;
        solarPanelDetached = !solarAttached;
        // Flight requires only the two thrusters; dish + solar are optional.
        if (shipController != null) shipController.canFly = leftAttached && rightAttached;
    }

    // Returns true if this ship currently has its satellite dish attached.
    // Used by MapOrbitLines to decide whether to render the ship's orbit
    // prediction (the dish is the "satellite uplink" that the map listens to).
    public bool HasDishAttached =>
        dishChild != null && dishChild.activeSelf;

    // Direct attach state for the SpaceNets — used by tier-spawn + save apply
    // to set up the ship without spawning floating pickups (no crash flow).
    public void SetSpaceNetAttached(bool leftAttached, bool rightAttached)
    {
        if (leftSpaceNetChild != null)  leftSpaceNetChild.SetActive(leftAttached);
        if (rightSpaceNetChild != null) rightSpaceNetChild.SetActive(rightAttached);
    }

    public bool HasLeftSpaceNetAttached  => leftSpaceNetChild  != null && leftSpaceNetChild.activeSelf;
    public bool HasRightSpaceNetAttached => rightSpaceNetChild != null && rightSpaceNetChild.activeSelf;

    // Per-part read accessors used by FleetTelemetry to render FLEET STATE
    // lines. Source of truth is the child GameObject's activeSelf — same
    // pattern as HasDishAttached above, more reliable than the inverse
    // detach-tracking bools (which only flip when crash-flow detach runs).
    public bool HasLeftThrusterAttached  => leftThrusterChild  != null && leftThrusterChild.activeSelf;
    public bool HasRightThrusterAttached => rightThrusterChild != null && rightThrusterChild.activeSelf;
    public bool HasSolarAttached         => solarPanelChild    != null && solarPanelChild.activeSelf;
}