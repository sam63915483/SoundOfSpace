using UnityEngine;
using TMPro;

public class CassettePlayer : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip musicClip;

    [Header("UI")]
    public TextMeshProUGUI promptText;

    [Header("Settings")]
    public float ejectHoldDuration = 1.5f;
    public KeyCode interactKey = KeyCode.F;

    [Header("Cassette Prefab")]
    public GameObject cassettePickupPrefab;

    private bool hasCassette = true;
    private bool playerInRange = false;
    private PlayerPickup playerPickup;
    private float ejectHoldTimer = 0f;
    private bool isEjectHolding = false;

    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
            audioSource.clip = musicClip;

        if (promptText != null)
            promptText.gameObject.SetActive(false);

        playerPickup = FindObjectOfType<PlayerPickup>();
        if (playerPickup == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerPickup = player.GetComponent<PlayerPickup>();
        }
    }

    void Update()
    {
        if (!playerInRange) return;

        // Ignore input if player is holding F for a pickup
        if (playerPickup != null && playerPickup.IsHoldingPickupKey)
        {
            if (promptText != null) promptText.gameObject.SetActive(false);
            return;
        }

        if (hasCassette)
        {
            HandleCassetteInside();
        }
        else
        {
            HandleCassetteMissing();
        }
    }

    void HandleCassetteInside()
    {
        // Composite: configurable keyboard interactKey OR controller X button.
        bool down = TutorialGate.GetKeyDown(interactKey, TutorialAbility.Pickup) ||
                    TutorialGate.InteractPressed(TutorialAbility.Pickup);
        bool held = TutorialGate.GetKey(interactKey, TutorialAbility.Pickup) ||
                    TutorialGate.InteractHeld(TutorialAbility.Pickup);
        bool up   = TutorialGate.GetKeyUp(interactKey, TutorialAbility.Pickup) ||
                    TutorialGate.InteractReleased(TutorialAbility.Pickup);

        if (down)
        {
            ejectHoldTimer = 0f;
            isEjectHolding = false;
        }

        if (held)
        {
            ejectHoldTimer += Time.deltaTime;
            if (promptText != null)
            {
                float progress = Mathf.Clamp01(ejectHoldTimer / ejectHoldDuration);
                promptText.text = $"Hold {PromptGlyphs.Interact} to Eject ({Mathf.RoundToInt(progress * 100)}%)";
            }

            if (ejectHoldTimer >= ejectHoldDuration && !isEjectHolding)
            {
                isEjectHolding = true;
                EjectCassette();
            }
        }

        if (up)
        {
            if (ejectHoldTimer < ejectHoldDuration && !isEjectHolding)
            {
                TogglePlayPause();
            }
            isEjectHolding = false;
            ejectHoldTimer = 0f;
            UpdatePromptText();
        }

        if (!held)
        {
            UpdatePromptText();
        }
    }

    void HandleCassetteMissing()
    {
        ejectHoldTimer = 0f;
        isEjectHolding = false;

        if (playerPickup != null && playerPickup.GetHeldObject() != null)
        {
            CassettePickup pickup = playerPickup.GetHeldObject().GetComponent<CassettePickup>();
            if (pickup != null && pickup.itemType == "Cassette")
            {
                if (promptText != null)
                {
                    promptText.gameObject.SetActive(true);
                    promptText.text = $"Press {PromptGlyphs.Interact} to Insert Cassette";
                }
                if (TutorialGate.GetKeyDown(interactKey, TutorialAbility.Pickup) ||
                    TutorialGate.InteractPressed(TutorialAbility.Pickup))
                {
                    InsertCassette(playerPickup.GetHeldObject());
                }
                return;
            }
        }

        if (promptText != null)
        {
            promptText.gameObject.SetActive(true);
            promptText.text = "No Cassette";
        }
    }

    void UpdatePromptText()
    {
        if (!playerInRange || promptText == null) return;
        if (hasCassette)
        {
            string status = (audioSource != null && audioSource.isPlaying) ? "Playing" : "Stopped";
            promptText.text = $"Press {PromptGlyphs.Interact} to Play/Stop | Hold {PromptGlyphs.Interact} to Eject\nStatus: {status}";
            promptText.gameObject.SetActive(true);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            UpdatePromptText();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (promptText != null) promptText.gameObject.SetActive(false);
            ejectHoldTimer = 0f;
            isEjectHolding = false;
        }
    }

    void TogglePlayPause()
    {
        if (audioSource == null || audioSource.clip == null) return;

        if (audioSource.isPlaying)
            audioSource.Pause();
        else
            audioSource.Play();
        UpdatePromptText();
    }

    void EjectCassette()
    {
        if (!hasCassette) return;

        hasCassette = false;
        if (audioSource != null) audioSource.Stop();

        // Re‑fetch PlayerPickup if missing
        if (playerPickup == null)
        {
            playerPickup = FindObjectOfType<PlayerPickup>();
            if (playerPickup == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) playerPickup = player.GetComponent<PlayerPickup>();
            }
        }

        if (playerPickup == null || cassettePickupPrefab == null)
        {
            Debug.LogError("Cannot eject: missing PlayerPickup or cassette prefab.");
            return;
        }

        Transform holdPos = playerPickup.holdPosition;
        if (holdPos == null) return;

        // Spawn cassette directly in hand
        GameObject cassette = Instantiate(cassettePickupPrefab, holdPos.position, holdPos.rotation);

        Rigidbody rb = cassette.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
        }

        Collider col = cassette.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        cassette.transform.SetParent(holdPos);
        cassette.transform.localPosition = Vector3.zero;
        cassette.transform.localRotation = Quaternion.identity;

        PickupHoldOffset offset = cassette.GetComponent<PickupHoldOffset>();
        if (offset != null)
        {
            cassette.transform.localPosition = offset.localPositionOffset;
            cassette.transform.localRotation = Quaternion.Euler(offset.localRotationOffset);
        }

        playerPickup.ForcePickup(cassette);
        UpdatePromptText();
    }

    void InsertCassette(GameObject heldCassette)
    {
        if (hasCassette) return;
        hasCassette = true;
        playerPickup?.ClearHeldObject();
        Destroy(heldCassette);
        UpdatePromptText();
    }

    // ───── Save/Load ─────
    public bool HasCassette => hasCassette;
    public void SetHasCassette(bool inserted)
    {
        hasCassette = inserted;
        if (!inserted && audioSource != null && audioSource.isPlaying) audioSource.Stop();
        UpdatePromptText();
    }
}