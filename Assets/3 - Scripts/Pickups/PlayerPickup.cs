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
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickupLayer);
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
                pickupPromptText.text = "Hold F to Pickup";
        }
    }

    void HandleHoldToPickup()
    {
        if (lookedAtPickup == null)
        {
            ResetHold();
            return;
        }

        // Configurable keyboard pickup key OR controller X button (held).
        if (TutorialGate.GetKey(pickupKey, TutorialAbility.Pickup) ||
            TutorialGate.InteractHeld(TutorialAbility.Pickup))
        {
            if (!isHolding)
            {
                isHolding = true;
                holdTimer = 0f;
            }

            holdTimer += Time.deltaTime;

            if (pickupPromptText != null)
            {
                // Only re-allocate the string when the displayed integer percentage
                // actually changes (was allocating every frame while holding).
                int pct = Mathf.RoundToInt(Mathf.Clamp01(holdTimer / holdDuration) * 100);
                if (pct != _lastShownPickupPct)
                {
                    _lastShownPickupPct = pct;
                    pickupPromptText.text = $"Hold F to Pickup ({pct}%)";
                }
            }

            if (holdTimer >= holdDuration)
            {
                heldObject = lookedAtPickup;
                PickupObject(heldObject);
                ResetHold();
            }
        }
        else
        {
            ResetHold();
        }
    }

    void ResetHold()
    {
        isHolding = false;
        holdTimer = 0f;
        _lastShownPickupPct = -1;
    }

    void PickupObject(GameObject obj)
    {
        if (pickupClip != null && pickupAudioSource != null)
            pickupAudioSource.PlayOneShot(pickupClip, pickupVolume);

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
        if (heldObject != null)
        {
            Rigidbody rb = heldObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                Rigidbody playerRb = GetComponent<Rigidbody>();
                if (playerRb != null)
                    rb.velocity = playerRb.velocity;
            }

            heldObject.transform.SetParent(null);
            heldObject.transform.position += Vector3.up * 0.2f;

            PickupMarker marker = heldObject.GetComponent<PickupMarker>();
            if (marker != null && PickupUIManager.Instance != null)
                PickupUIManager.Instance.RegisterPickup(marker);

            heldObject.GetComponent<Collider>().enabled = true;

            if (endlessManager != null)
                endlessManager.RegisterPhysicsObject(heldObject.transform);

            heldObject = null;
            heldObjectOffset = null;
        }
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