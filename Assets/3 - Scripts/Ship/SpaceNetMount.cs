using UnityEngine;
using TMPro;

/// <summary>
/// Trigger-collider-based install zone for a SpaceNet, mirroring
/// ThrusterMount. Drop one on a child GameObject of a Ship with a trigger
/// BoxCollider sized to wherever the player needs to stand to install the
/// matching pickup. `acceptedSide` says which side this mount handles —
/// Left or Right — and the trigger only accepts SpaceNetPickup objects
/// with the matching side.
///
/// While the player carries a matching pickup, a translucent ghost copy
/// of the dormant net is shown at its mount position (cyan when out of
/// trigger, green when inside). F installs: pickup is consumed and the
/// dormant SpaceNet child on the ship is reactivated.
///
/// Pairs with SpaceNetMountController only by convenience — if you don't
/// use SpaceNetMount components on a particular ship, SpaceNetMountController
/// falls back to its distance-check install path.
/// </summary>
public class SpaceNetMount : MonoBehaviour
{
    [Tooltip("Which side this mount accepts. Matches SpaceNetPickup.side.")]
    public SpaceNetPickup.Side acceptedSide = SpaceNetPickup.Side.Left;
    [Tooltip("Optional text reference (shared with other mounts). If null, falls back to InteractPromptUI.")]
    public TextMeshProUGUI placePromptText;

    [Header("Sound Effects")]
    [SerializeField] AudioClip reattachClip;
    [SerializeField, Range(0, 1)] float reattachVolume = 0.6f;

    static readonly Color GhostDefaultColor = new Color(0.3f, 0.9f, 1f, 0.45f);
    static readonly Color GhostInZoneColor  = new Color(0.4f, 1.0f, 0.4f, 0.55f);

    bool _playerInRange;
    bool _showingPrompt;
    PlayerPickup _playerPickup;
    ThrusterDetachOnImpact _detach;
    AudioSource _reattachSource;

    GameObject _ghost;
    Material _ghostMat;
    SpaceNet _target;

    void Start()
    {
        _playerPickup = FindObjectOfType<PlayerPickup>();
        _detach = GetComponentInParent<ThrusterDetachOnImpact>();
        _reattachSource = GetComponent<AudioSource>();
        if (_reattachSource == null) _reattachSource = gameObject.AddComponent<AudioSource>();
        _reattachSource.playOnAwake = false;

        if (placePromptText != null) placePromptText.gameObject.SetActive(false);

        // Resolve the dormant SpaceNet on this ship that this mount installs.
        // Auto-find by sign of localPosition.x on the ship root (same heuristic
        // as SpaceNetMountController).
        var shipRoot = transform.root;
        var nets = shipRoot.GetComponentsInChildren<SpaceNet>(true);
        for (int i = 0; i < nets.Length; i++)
        {
            float x = nets[i].transform.localPosition.x;
            bool isLeft = x < 0f;
            if ((isLeft && acceptedSide == SpaceNetPickup.Side.Left) ||
                (!isLeft && acceptedSide == SpaceNetPickup.Side.Right))
            {
                _target = nets[i];
                break;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) _playerInRange = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerInRange = false;
            HidePrompt();
        }
    }

    void Update()
    {
        if (_playerPickup == null)
        {
            _playerPickup = FindObjectOfType<PlayerPickup>();
            if (_playerPickup == null) return;
        }
        if (_target == null) return;

        // Already installed — nothing to do.
        if (_target.gameObject.activeSelf)
        {
            HidePrompt();
            DestroyGhost();
            return;
        }

        // What is the player holding?
        var held = _playerPickup.GetHeldObject();
        SpaceNetPickup pickup = held != null ? held.GetComponent<SpaceNetPickup>() : null;
        bool correctHeld = pickup != null && pickup.side == acceptedSide;
        if (!correctHeld)
        {
            HidePrompt();
            DestroyGhost();
            return;
        }

        EnsureGhost();
        SetGhostColor(_playerInRange ? GhostInZoneColor : GhostDefaultColor);

        if (!_playerInRange)
        {
            HidePrompt();
            return;
        }

        ShowPrompt();
        if (InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.Pickup))
        {
            if (reattachClip != null && _reattachSource != null)
                _reattachSource.PlayOneShot(reattachClip, reattachVolume);
            Install(held);
        }
    }

    void Install(GameObject heldPickup)
    {
        if (_detach != null)
        {
            _detach.ReattachPart(acceptedSide == SpaceNetPickup.Side.Left ? "SpaceNetLeft" : "SpaceNetRight");
        }
        else if (_target != null)
        {
            _target.gameObject.SetActive(true);
        }
        if (_playerPickup != null) _playerPickup.ClearHeldObject();
        Destroy(heldPickup);
        HidePrompt();
        DestroyGhost();
    }

    void ShowPrompt()
    {
        if (_showingPrompt) return;
        _showingPrompt = true;
        if (placePromptText != null)
        {
            placePromptText.gameObject.SetActive(true);
            placePromptText.text = $"Press {PromptGlyphs.Interact} to Place";
        }
        else
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to Place Space Net");
        }
    }

    void HidePrompt()
    {
        if (!_showingPrompt) return;
        _showingPrompt = false;
        if (placePromptText != null) placePromptText.gameObject.SetActive(false);
        else InteractPromptUI.Clear(this);
    }

    void EnsureGhost()
    {
        if (_ghost != null) return;
        if (_target == null) return;
        _ghost = Instantiate(_target.gameObject, _target.transform.parent);
        _ghost.name = _target.name + "_PlacementGhost";
        _ghost.transform.localPosition = _target.transform.localPosition;
        _ghost.transform.localRotation = _target.transform.localRotation;
        _ghost.transform.localScale    = _target.transform.localScale;
        _ghost.SetActive(true);

        foreach (var c in _ghost.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in _ghost.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }
        foreach (var mb in _ghost.GetComponentsInChildren<MonoBehaviour>(true))
            mb.enabled = false;

        _ghostMat = GhostPlacement.MakeGhostMaterial(GhostDefaultColor);
        foreach (var r in _ghost.GetComponentsInChildren<Renderer>(true))
        {
            int n = r.sharedMaterials.Length;
            var mats = new Material[n];
            for (int i = 0; i < n; i++) mats[i] = _ghostMat;
            r.sharedMaterials   = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
        }
    }

    void SetGhostColor(Color c)
    {
        if (_ghostMat == null) return;
        if (_ghostMat.color != c) _ghostMat.color = c;
    }

    void DestroyGhost()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _ghostMat = null;
    }

    void OnDisable()
    {
        HidePrompt();
        DestroyGhost();
    }
}
