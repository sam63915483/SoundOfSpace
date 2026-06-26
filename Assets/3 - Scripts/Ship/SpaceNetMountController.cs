using UnityEngine;

/// <summary>
/// Drop ONE of these on each ship that supports installable nets. Auto-finds
/// the ship's two SpaceNet children (left = lowest local X, right = highest)
/// if the Inspector references aren't set. Runs every frame while at least
/// one of its target nets is dormant (inactive): if the player walks close
/// to that dormant net while holding a matching SpaceNetPickup, an F-prompt
/// is shown and pressing F activates the net + destroys the pickup.
///
/// While the player carries a matching pickup, a translucent ghost copy of
/// the dormant net is spawned at its eventual mount position so they can
/// see exactly where it'll go. The ghost is cyan when out of installation
/// range and green when inside range (mirrors ThrusterMount's preview).
///
/// Lives on the Ship root so it stays active even when the SpaceNet children
/// are inactive (an inactive GameObject's components can't run their own
/// proximity check).
/// </summary>
public class SpaceNetMountController : MonoBehaviour
{
    [Tooltip("Left-side dormant SpaceNet to activate when a left-net pickup is installed. Auto-resolves at runtime if null.")]
    public SpaceNet leftNet;
    [Tooltip("Right-side dormant SpaceNet to activate when a right-net pickup is installed. Auto-resolves at runtime if null.")]
    public SpaceNet rightNet;

    [Tooltip("How close the player must stand to a dormant net (in metres) for the install prompt to appear.")]
    public float installRadius = 2.5f;

    // Same palette as ThrusterMount so the install UX feels identical
    // across all part types.
    static readonly Color GhostDefaultColor = new Color(0.3f, 0.9f, 1f, 0.45f);
    static readonly Color GhostInZoneColor  = new Color(0.4f, 1.0f, 0.4f, 0.55f);

    Transform _playerCached;
    PlayerPickup _pickupCached;
    float _findRetryT;
    const float kFindRetryInterval = 1f;
    SpaceNet _shownPromptFor;

    GameObject _ghost;
    Material _ghostMat;
    SpaceNet _ghostFor;        // which target net the current ghost belongs to

    void Start()
    {
        if (leftNet == null || rightNet == null) AutoResolveNets();
    }

    void AutoResolveNets()
    {
        // Find SpaceNet children of this ship and split by sign of local X.
        var nets = GetComponentsInChildren<SpaceNet>(true);
        SpaceNet l = null, r = null;
        for (int i = 0; i < nets.Length; i++)
        {
            float x = nets[i].transform.localPosition.x;
            if (x < 0f) { if (l == null) l = nets[i]; }
            else        { if (r == null) r = nets[i]; }
        }
        if (leftNet == null)  leftNet  = l;
        if (rightNet == null) rightNet = r;
    }

    void Update()
    {
        // Lazy-cache scene refs (CLAUDE.md pattern — throttled retry).
        if (_playerCached == null || _pickupCached == null)
        {
            _findRetryT -= Time.deltaTime;
            if (_findRetryT <= 0f)
            {
                _findRetryT = kFindRetryInterval;
                if (_playerCached == null)
                {
                    var go = GameObject.FindGameObjectWithTag("Player");
                    if (go != null) _playerCached = go.transform;
                }
                if (_pickupCached == null) _pickupCached = FindObjectOfType<PlayerPickup>(true);
            }
            if (_playerCached == null || _pickupCached == null) return;
        }

        // What is the player holding?
        var held = _pickupCached.GetHeldObject();
        SpaceNetPickup heldNetPickup = held != null ? held.GetComponent<SpaceNetPickup>() : null;
        if (heldNetPickup == null)
        {
            ClearPromptIfAny();
            DestroyGhost();
            return;
        }

        // Which dormant net is the matching target?
        SpaceNet target = heldNetPickup.side == SpaceNetPickup.Side.Left ? leftNet : rightNet;
        if (target == null || target.gameObject.activeSelf)
        {
            // Either no slot for that side, or it's already installed.
            ClearPromptIfAny();
            DestroyGhost();
            return;
        }

        // Player is holding the matching part: show the ghost regardless of
        // range so they can see where it'll go from across the ship.
        EnsureGhost(target);

        // Distance check to the dormant net's position (world space).
        float sqr = (target.transform.position - _playerCached.position).sqrMagnitude;
        bool inRange = sqr <= installRadius * installRadius;
        SetGhostColor(inRange ? GhostInZoneColor : GhostDefaultColor);

        if (!inRange)
        {
            ClearPromptIfAny();
            return;
        }

        // In range — show prompt for this specific net.
        if (_shownPromptFor != target)
        {
            ClearPromptIfAny();
            _shownPromptFor = target;
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to install Space Net");
        }

        // Install on press.
        if (InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            target.gameObject.SetActive(true);
            _pickupCached.ClearHeldObject();
            Destroy(held);
            ClearPromptIfAny();
            DestroyGhost();
        }
    }

    // Spawns a translucent clone of the dormant SpaceNet at its mount
    // position so the player sees where the held pickup will land. The
    // source SpaceNet stays inactive — only the ghost copy is visible.
    void EnsureGhost(SpaceNet target)
    {
        if (_ghost != null && _ghostFor == target) return;
        DestroyGhost();
        if (target == null) return;

        _ghost = Instantiate(target.gameObject, target.transform.parent);
        _ghost.name = target.name + "_InstallGhost";
        _ghost.transform.localPosition = target.transform.localPosition;
        _ghost.transform.localRotation = target.transform.localRotation;
        _ghost.transform.localScale    = target.transform.localScale;
        _ghost.SetActive(true);
        _ghostFor = target;

        // Strip gameplay so the ghost can't gather dust, collide, or re-
        // trigger this mount controller.
        foreach (var c in _ghost.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in _ghost.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }
        foreach (var mb in _ghost.GetComponentsInChildren<MonoBehaviour>(true))
            mb.enabled = false;

        // Translucent material — same shape ThrusterMount uses for its
        // placement preview, no shadows so it doesn't darken the ship.
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
        _ghostFor = null;
    }

    void ClearPromptIfAny()
    {
        if (_shownPromptFor == null) return;
        InteractPromptUI.Clear(this);
        _shownPromptFor = null;
    }

    void OnDisable()
    {
        ClearPromptIfAny();
        DestroyGhost();
    }

    // Visualizes the install zones in the Scene view when this Ship root is
    // selected. Green wire sphere at each net's mount position, sized by
    // installRadius. Auto-resolves leftNet / rightNet so the gizmo still
    // renders before Start() runs (e.g. in the prefab editor).
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1.0f, 0.4f, 0.85f);
        SpaceNet l = leftNet, r = rightNet;
        if (l == null || r == null)
        {
            var nets = GetComponentsInChildren<SpaceNet>(true);
            for (int i = 0; i < nets.Length; i++)
            {
                float x = nets[i].transform.localPosition.x;
                if (x < 0f) { if (l == null) l = nets[i]; }
                else        { if (r == null) r = nets[i]; }
            }
        }
        if (l != null) Gizmos.DrawWireSphere(l.transform.position, installRadius);
        if (r != null) Gizmos.DrawWireSphere(r.transform.position, installRadius);
    }
}
