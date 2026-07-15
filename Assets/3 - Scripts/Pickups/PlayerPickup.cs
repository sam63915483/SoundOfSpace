using UnityEngine;
using TMPro;

public class PlayerPickup : MonoBehaviour
{
    public float pickupRange = 4f;
    public Transform holdPosition;
    public LayerMask pickupLayer = ~0;
    public TextMeshProUGUI pickupPromptText;

    [Header("Hold to Pickup")]
    public float holdDuration = 2f;
    public KeyCode pickupKey = KeyCode.F;
    public KeyCode dropKey = KeyCode.G;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip pickupClip;
    [SerializeField, Range(0, 1)] private float pickupVolume = 0.6f;
    private AudioSource pickupAudioSource;

    private GameObject heldObject;
    private PickupHoldOffset heldObjectOffset;
    private EndlessManager endlessManager;
    private FishingRodController fishingRodController;
    private GuitarController guitarController;

    private float holdTimer = 0f;
    private bool isHolding = false;
    private int _lastShownPickupPct = -1; // change-detection cache for the hold-to-pickup string
    private GameObject lookedAtPickup;
    private GameObject _holdTarget;       // the pickup the current hold is locked onto
    private float _lookLostTime;          // seconds since the hold target left the crosshair
    // Tolerate brief look-away during a hold so tiny aim wobble doesn't reset
    // the whole 2s timer. Non-serialized const — tune here.
    const float PickupLookGrace = 0.35f;
    // Fat crosshair cast radius for the look-at test — a razor ray slipped off
    // the part on the smallest movement; a small sphere is far stickier.
    const float PickupCastRadius = 0.2f;
    private Camera playerCamera;
    private bool canPlaceRightNow = false;

    public bool IsHoldingPickupKey => isHolding;
    public bool IsHoldingObject => heldObject != null;

    void Start()
    {
        endlessManager = FindObjectOfType<EndlessManager>();
        playerCamera = Camera.main ?? FindObjectOfType<Camera>();
        fishingRodController = GetComponent<FishingRodController>();
        guitarController     = GetComponent<GuitarController>();

        if (holdPosition == null)
            Debug.LogError("PlayerPickup: holdPosition not assigned!");

        if (pickupPromptText != null)
            pickupPromptText.gameObject.SetActive(false);

        pickupAudioSource = GetComponent<AudioSource>();
        if (pickupAudioSource == null) pickupAudioSource = gameObject.AddComponent<AudioSource>();
        pickupAudioSource.playOnAwake = false;
    }

    void Update()
    {
        if (playerCamera == null) return;
        // Suppress drop / pickup input while the map is open — the map binds G
        // to cursor-lock toggle and a held item shouldn't drop in that mode.
        if (PlayerController.isMapOpen) return;

        UpdateLookAtAndPrompt();

        if (heldObject == null)
        {
            HandleHoldToPickup();
        }
        else
        {
            // Configurable keyboard drop key OR controller B button.
            if ((TutorialGate.GetKeyDown(dropKey, TutorialAbility.Pickup) ||
                 TutorialGate.DropPressed(TutorialAbility.Pickup)) && !canPlaceRightNow)
                DropObject();

            if (heldObject != null && holdPosition != null)
            {
                if (heldObjectOffset != null)
                {
                    heldObject.transform.position = holdPosition.TransformPoint(heldObjectOffset.localPositionOffset);
                    heldObject.transform.rotation = holdPosition.rotation * Quaternion.Euler(heldObjectOffset.localRotationOffset);
                }
                else
                {
                    heldObject.transform.position = holdPosition.position;
                    heldObject.transform.rotation = holdPosition.rotation;
                }
            }
        }
    }

    void UpdateLookAtAndPrompt()
    {
        bool itemEquipped = (fishingRodController != null && fishingRodController.IsEquipped)
                         || (guitarController != null && guitarController.IsEquipped);

        if (heldObject != null || itemEquipped)
        {
            if (pickupPromptText != null) pickupPromptText.gameObject.SetActive(false);
            lookedAtPickup = null;
            return;
        }

        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));
        // SphereCast (small radius) instead of a thin Raycast so keeping the
        // part under the crosshair doesn't demand pixel-perfect aim. Triggers
        // are hit too (the parts use "PickupHelper" trigger children).
        bool hitSomething = Physics.SphereCast(ray, PickupCastRadius, out RaycastHit hit,
                                               pickupRange, pickupLayer, QueryTriggerInteraction.Collide);
        bool isLookingAtPickup = false;

        if (hitSomething)
        {
            if (hit.collider.CompareTag("PickupHelper"))
            {
                GameObject actualPickup = hit.collider.transform.parent?.gameObject;
                if (actualPickup != null && actualPickup.CompareTag("ShipPart"))
                {
                    lookedAtPickup = actualPickup;
                    isLookingAtPickup = true;
                }
            }
            else if (hit.collider.CompareTag("ShipPart"))
            {
                lookedAtPickup = hit.collider.gameObject;
                isLookingAtPickup = true;
            }
        }

        if (!isLookingAtPickup)
            lookedAtPickup = null;

        if (pickupPromptText != null)
        {
            pickupPromptText.gameObject.SetActive(isLookingAtPickup);
            if (isLookingAtPickup)
            {
                pickupPromptText.text = $"Hold {PromptGlyphs.Interact} to Pickup";
                _lastShownPickupPct = -1;   // force the %-string to rebuild with the right glyph
            }
        }
    }

    void HandleHoldToPickup()
    {
        // Configurable keyboard pickup key OR controller X button (held).
        bool holdingBtn = TutorialGate.GetKey(pickupKey, TutorialAbility.Pickup) ||
                          TutorialGate.InteractHeld(TutorialAbility.Pickup);
        if (!holdingBtn) { ResetHold(); return; }

        if (lookedAtPickup != null)
        {
            // Fresh target under the crosshair — start a hold, or restart it if
            // the crosshair moved onto a DIFFERENT pickup mid-hold.
            if (!isHolding || _holdTarget != lookedAtPickup)
            {
                _holdTarget = lookedAtPickup;
                holdTimer = 0f;
                isHolding = true;
            }
            _lookLostTime = 0f;
        }
        else
        {
            // Crosshair slipped off. Tolerate brief aim wobble via a grace window
            // instead of cancelling the whole hold (the old code reset the 2s
            // timer to zero on the first missed frame — the "cancels halfway" bug).
            if (!isHolding || _holdTarget == null) { ResetHold(); return; }
            _lookLostTime += Time.deltaTime;
            if (_lookLostTime > PickupLookGrace) { ResetHold(); return; }
        }

        holdTimer += Time.deltaTime;

        if (pickupPromptText != null && pickupPromptText.gameObject.activeSelf)
        {
            // Only re-allocate the string when the displayed integer percentage
            // actually changes (was allocating every frame while holding).
            int pct = Mathf.RoundToInt(Mathf.Clamp01(holdTimer / holdDuration) * 100);
            if (pct != _lastShownPickupPct)
            {
                _lastShownPickupPct = pct;
                pickupPromptText.text = $"Hold {PromptGlyphs.Interact} to Pickup ({pct}%)";
            }
        }

        if (holdTimer >= holdDuration)
        {
            heldObject = _holdTarget;
            PickupObject(heldObject);
            ResetHold();
        }
    }

    void ResetHold()
    {
        isHolding = false;
        holdTimer = 0f;
        _lastShownPickupPct = -1;
        _holdTarget = null;
        _lookLostTime = 0f;
    }

    void PickupObject(GameObject obj)
    {
        if (pickupClip != null && pickupAudioSource != null)
            pickupAudioSource.PlayOneShot(pickupClip, pickupVolume);
        GamepadRumble.Pulse(0.1f, 0.3f, 0.08f);

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        obj.GetComponent<Collider>().enabled = false;

        PickupMarker marker = obj.GetComponent<PickupMarker>();
        if (marker != null && PickupUIManager.Instance != null)
            PickupUIManager.Instance.UnregisterPickup(marker);

        obj.transform.SetParent(holdPosition);
        heldObjectOffset = obj.GetComponent<PickupHoldOffset>();
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        if (endlessManager != null)
            endlessManager.UnregisterPhysicsObject(obj.transform);

        if (pickupPromptText != null)
            pickupPromptText.gameObject.SetActive(false);
    }

    void DropObject()
    {
        if (heldObject == null) return;

        // Position the TRANSFORM first — it's authoritative for where the part
        // should land (unparent keeps world position; small lift off the hand
        // along the player's up so it doesn't drop into the floor).
        heldObject.transform.SetParent(null);
        heldObject.transform.position += transform.up * 0.2f;

        Rigidbody rb = heldObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            // CRITICAL (the "drop teleports super far" bug): the parts are
            // CollisionDetectionMode.ContinuousDynamic and the project runs with
            // Physics.autoSyncTransforms = false, so a freshly-dynamic body keeps
            // the STALE rb.position it had while held and PhysX then reconciles by
            // snapping/depenetrating from there — flinging the part away. Seat the
            // body exactly at the visible transform before it simulates.
            rb.position = heldObject.transform.position;
            rb.rotation = heldObject.transform.rotation;
            rb.angularVelocity = Vector3.zero;
            Rigidbody playerRb = GetComponent<Rigidbody>();
            rb.velocity = playerRb != null ? playerRb.velocity : Vector3.zero;   // co-move with the player
        }

        Collider col = heldObject.GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Commit rb.position/rotation into PhysX NOW (autoSyncTransforms is off),
        // so the collider enables at the correct spot and can't teleport-snap.
        Physics.SyncTransforms();

        PickupMarker marker = heldObject.GetComponent<PickupMarker>();
        if (marker != null && PickupUIManager.Instance != null)
            PickupUIManager.Instance.RegisterPickup(marker);

        if (endlessManager != null)
            endlessManager.RegisterPhysicsObject(heldObject.transform);

        heldObject = null;
        heldObjectOffset = null;
    }

    public void SetCanPlace(bool canPlace)
    {
        canPlaceRightNow = canPlace;
    }

    public void ForcePickup(GameObject obj)
    {
        if (obj == null) return;
        if (heldObject != null)
            DropObject();

        // Mirror PickupObject's full setup so a programmatically-granted
        // pickup (cassette flow, save load, ship-market purchase) behaves
        // identically to a hold-F pickup: kinematic, collider off, parented
        // to the hold position, removed from EndlessManager + PickupUIManager.
        var rb = obj.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        var col = obj.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        var marker = obj.GetComponent<PickupMarker>();
        if (marker != null && PickupUIManager.Instance != null)
            PickupUIManager.Instance.UnregisterPickup(marker);

        if (holdPosition != null)
        {
            obj.transform.SetParent(holdPosition);
            heldObjectOffset = obj.GetComponent<PickupHoldOffset>();
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
        }
        else
        {
            heldObjectOffset = obj.GetComponent<PickupHoldOffset>();
        }

        if (endlessManager != null)
            endlessManager.UnregisterPhysicsObject(obj.transform);

        heldObject = obj;

        if (pickupPromptText != null)
            pickupPromptText.gameObject.SetActive(false);

        ResetHold();
    }

    public GameObject GetHeldObject() => heldObject;

    public void ClearHeldObject()
    {
        heldObject = null;
        heldObjectOffset = null;
    }

    public void ForceDropObject()
    {
        if (heldObject != null)
            DropObject();
    }
}