using UnityEngine;
using TMPro;

public class ThrusterMount : MonoBehaviour
{
    public string acceptedThrusterType = "Left";
    public TextMeshProUGUI placePromptText;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip reattachClip;
    [SerializeField, Range(0, 1)] private float reattachVolume = 0.6f;

    private bool playerInRange = false;
    private PlayerPickup playerPickup;
    private ThrusterDetachOnImpact damageScript;

    private bool canPlace = false; // Tracks if conditions are met
    private AudioSource reattachSource;
    // Tracks whether THIS mount last activated placePromptText. Multiple
    // mounts often share the same TMP text reference in the inspector — if
    // each non-matching mount called SetActive(false) every frame, it would
    // race with the matching mount's SetActive(true) and the prompt would
    // flicker / disappear. With this flag, HidePrompt is a no-op unless this
    // specific mount was the one that turned the prompt on.
    private bool _showingPrompt;

    // Translucent preview of the final mounted geometry. Spawned whenever the
    // player is holding a part whose type matches this mount (regardless of
    // trigger zone) so they always see where the part will go. Tinted cyan
    // when not in zone, green when in zone (placeable). Destroyed on
    // wrong-part / no-part / successful placement / mount disable.
    private GameObject _ghost;
    private Material _ghostMat;

    // Tint while holding the matching part but NOT inside the trigger.
    static readonly Color GhostDefaultColor = new Color(0.3f, 0.9f, 1f, 0.45f);
    // Tint while inside the trigger and ready to place.
    static readonly Color GhostInZoneColor  = new Color(0.4f, 1.0f, 0.4f, 0.55f);

    void Start()
    {
        if (placePromptText != null)
            placePromptText.gameObject.SetActive(false);

        playerPickup = FindObjectOfType<PlayerPickup>();
        if (playerPickup == null)
            Debug.LogWarning($"ThrusterMount on {gameObject.name}: PlayerPickup not found.");

        damageScript = GetComponentInParent<ThrusterDetachOnImpact>();
        if (damageScript == null)
            Debug.LogError($"ThrusterMount on {gameObject.name}: ThrusterDetachOnImpact not found on parent!");

        reattachSource = GetComponent<AudioSource>();
        if (reattachSource == null) reattachSource = gameObject.AddComponent<AudioSource>();
        reattachSource.playOnAwake = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            playerInRange = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            UpdatePlacementState(false);
            HidePrompt();
        }
    }

    void Update()
    {
        if (playerPickup == null)
        {
            playerPickup = FindObjectOfType<PlayerPickup>();
            if (playerPickup == null) return;
        }

        GameObject held = playerPickup.GetHeldObject();
        bool isCorrectHeld = false;
        if (held != null)
        {
            ThrusterPickup pickup = held.GetComponent<ThrusterPickup>();
            if (pickup != null && pickup.thrusterType == acceptedThrusterType)
                isCorrectHeld = true;
        }

        if (!isCorrectHeld)
        {
            UpdatePlacementState(false);
            HidePrompt();
            DestroyGhost();
            return;
        }

        // Holding the matching part: ghost is visible regardless of zone,
        // tinted by whether we're standing in the placement collider.
        EnsureGhost();
        SetGhostColor(playerInRange ? GhostInZoneColor : GhostDefaultColor);

        if (playerInRange)
        {
            UpdatePlacementState(true);
            ShowPrompt();

            if (InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.Pickup))
            {
                if (reattachClip != null && reattachSource != null)
                    reattachSource.PlayOneShot(reattachClip, reattachVolume);
                PlaceThruster(held);
            }
        }
        else
        {
            UpdatePlacementState(false);
            HidePrompt();
        }
    }

    void ShowPrompt()
    {
        if (_showingPrompt) return;
        _showingPrompt = true;
        // Prefer the wired-in TMP reference (legacy path). Fall back to the
        // shared InteractPromptUI pill when nothing is wired — every
        // ThrusterMount in the SHIP44 prefab leaves placePromptText null,
        // which used to silently swallow the prompt entirely. The pill UI
        // matches the SpaceNet install prompt + every other "press F" cue
        // in the game, so visuals stay consistent.
        if (placePromptText != null)
        {
            placePromptText.gameObject.SetActive(true);
            placePromptText.text = $"Press {PromptGlyphs.Interact} to Place";
        }
        else
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to install {acceptedThrusterType}");
        }
    }

    // Clean up the ghost + any active InteractPromptUI claim explicitly when
    // disabled so the order is deterministic (e.g. ship destruction).
    void OnDisable()
    {
        DestroyGhost();
        HidePrompt();
    }

    void UpdatePlacementState(bool active)
    {
        if (canPlace != active)
        {
            canPlace = active;
            if (playerPickup != null)
                playerPickup.SetCanPlace(canPlace);
        }
    }

    void HidePrompt()
    {
        if (!_showingPrompt) return; // never had the prompt — don't fight other mounts that might
        if (placePromptText != null) placePromptText.gameObject.SetActive(false);
        else InteractPromptUI.Clear(this);
        _showingPrompt = false;
    }

    void PlaceThruster(GameObject heldThruster)
    {
        UpdatePlacementState(false);
        HidePrompt();

        if (damageScript != null)
            damageScript.ReattachPart(acceptedThrusterType);
        else
            Debug.LogError("Cannot reattach: damageScript is null!");

        if (playerPickup != null)
            playerPickup.ClearHeldObject();

        Destroy(heldThruster);
        // Real child has been re-activated by ReattachPart; preview is now
        // redundant and would visibly z-fight with the real geometry.
        DestroyGhost();
    }

    // Spawns a translucent clone of the "final placed" scene child (the same
    // GameObject ReattachPart would activate) parented to its real parent so
    // the ghost inherits ship transform / floating-origin shifts for free.
    // The source child stays inactive — only the ghost copy is visible.
    void EnsureGhost()
    {
        if (_ghost != null) return;
        if (damageScript == null) return;
        GameObject src = damageScript.GetChildForType(acceptedThrusterType);
        if (src == null) return;

        _ghost = Instantiate(src, src.transform.parent);
        _ghost.name = src.name + "_PlacementGhost";
        _ghost.transform.localPosition = src.transform.localPosition;
        _ghost.transform.localRotation = src.transform.localRotation;
        _ghost.transform.localScale    = src.transform.localScale;
        _ghost.SetActive(true); // source is inactive while detached; ghost overrides.

        // Strip gameplay components so the clone can't collide, take damage,
        // re-trigger this mount, or run any of its own logic.
        foreach (var c in _ghost.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
        foreach (var rb in _ghost.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }
        foreach (var mb in _ghost.GetComponentsInChildren<MonoBehaviour>(true))
            mb.enabled = false;

        // Translucent look — same material the build-menu ghost uses, no
        // shadows so the preview never darkens the ship. Stored on
        // _ghostMat so SetGhostColor can retint without re-spawning.
        _ghostMat = GhostPlacement.MakeGhostMaterial(GhostDefaultColor);
        foreach (var r in _ghost.GetComponentsInChildren<Renderer>(true))
        {
            int n = r.sharedMaterials.Length;
            var mats = new Material[n];
            for (int i = 0; i < n; i++) mats[i] = _ghostMat;
            r.sharedMaterials       = mats;
            r.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows        = false;
        }
    }

    void SetGhostColor(Color color)
    {
        if (_ghostMat == null) return;
        if (_ghostMat.color != color) _ghostMat.color = color;
    }

    void DestroyGhost()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _ghostMat = null;
    }
}