using System.Collections;
using UnityEngine;

/// <summary>
/// Grapple gun — a prototype equippable (2026-09-10). Cloned from
/// PistolController's viewmodel / equip rig with every gun part stripped
/// (no ammo, reload, ADS, damage, tracer, alert). What is left:
///
///   LEFT CLICK  (idle)     fire the grapnel along the crosshair, up to `range`.
///                          It flies at `ballSpeed` and LATCHES to whatever the
///                          ray found, riding that object (planet, shuttle,
///                          tree…). A miss flies out to `range` in the nearest
///                          planet's frame (inheriting your speed) and resets.
///   LEFT CLICK  (any time) reset — ball removed, rope gone.
///   RIGHT CLICK (held)     the WINCH. The player's velocity along the rope is
///                          set to `reelSpeed` toward the anchor; the sideways
///                          share is kept (gravity swings you) with light
///                          damping; velocity AWAY from the anchor is cancelled
///                          so the rope never stretches. Within `holdDistance`
///                          you stop and hang there, pinned, until you release.
///                          Release = slack: you fall, the ball stays put.
///
/// The winch is the fishing bobber's velocity-level line constraint turned
/// around (Bobber.cs ~line 976): it never writes a position, so PhysX
/// depenetration can't compound into a launch. It runs AFTER PlayerController's
/// FixedUpdate (DefaultExecutionOrder) so the grounded grip can't undo it.
///
/// The rope is the fishing rod's LineRenderer Bézier, drawn muzzle → ball.
/// Unequipping resets the grapple, so you can never be tethered invisibly.
/// Live rope state is NOT saved; only unlocked/equipped (see SaveCollector).
/// </summary>
[DefaultExecutionOrder(50)]
public class GrappleGunController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Icon shown in the hotbar slot when this item is in the bar.")]
    public Sprite hotbarIcon;

    [Header("Gun Model")]
    [Tooltip("Optional viewmodel prefab. Leave EMPTY to use the built-in low-poly grapple gun (GrappleGunModel). A prefab should carry children named \"Muzzle\" and, optionally, \"HookHead\" (hidden while a shot is out).")]
    public GameObject gunPrefab;
    public Transform gunHoldPosition;

    [Header("Hold Offset Adjuster")]
    [Tooltip("Local-space position offset relative to gunHoldPosition (where the grip sits in the hand).")]
    public Vector3 holdPositionOffset = Vector3.zero;
    [Tooltip("Local-space resting rotation (Euler degrees) relative to gunHoldPosition.")]
    public Vector3 holdRotationOffset = Vector3.zero;
    [Tooltip("Local offset of the gun MODEL inside the grip pivot.")]
    public Vector3 gripOffset = Vector3.zero;

    [Header("Equip Animation")]
    public float equipDuration = 0.4f;
    public float equipStartAngle = -120f;
    public Vector3 equipRotationAxis = Vector3.right;
    public Vector3 equipStartPositionOffset = new Vector3(0f, -0.4f, 0f);
    [Tooltip("Route the viewmodel through PistolMotor (the shared reactive carry layer). Takes effect on the next equip.")]
    public bool useFloatyMotor = true;
    [Tooltip("Name of a child Transform inside the gun PREFAB the ball and rope start from. Resolved at equip time.")]
    public string muzzleChildName = "Muzzle";

    [Header("Ball")]
    [Tooltip("How far the hook can reach (metres). A miss flies out this far (in the nearest planet's frame, inheriting your speed) and then the gun resets itself.")]
    public float range = 1000f;
    [Tooltip("Hook flight speed (m/s).")]
    public float ballSpeed = 120f;
    [Tooltip("Optional projectile prefab. Leave empty for the built-in grapnel (GrappleGunModel.BuildHook).")]
    public GameObject ballPrefab;
    [Tooltip("Unused since the built-in grapnel replaced the placeholder sphere (kept so the scene serialization stays put).")]
    public float ballDiameter = 0.12f;
    [Tooltip("Unused — see ballDiameter.")]
    public Material ballMaterial;
    [Tooltip("Unused — see ballDiameter.")]
    public Color ballColor = new Color(0.9f, 0.3f, 0.15f, 1f);

    [Header("Winch (hold right click)")]
    [Tooltip("Reel-in speed along the rope (m/s).")]
    public float reelSpeed = 8f;
    [Tooltip("Distance from the anchor (metres) where you stop and hang.")]
    public float holdDistance = 1.5f;
    [Tooltip("How sharply the reel slows down as you reach holdDistance. Velocity toward the anchor = (distance - holdDistance) × this, capped at reelSpeed.")]
    public float holdSnap = 4f;
    [Tooltip("Per-second damping of your sideways (swing) velocity while reeling. 0 = free pendulum.")]
    public float swingDamping = 0.5f;
    [Tooltip("Extra sideways damping once you are hanging at holdDistance, so you settle instead of orbiting the anchor.")]
    public float heldSwingDamping = 3f;

    [Header("Rope")]
    public Material lineMaterial;
    public float lineWidth = 0.006f;
    public Color lineColor = new Color(0.45f, 0.30f, 0.14f, 1f);
    [Range(2, 30)] public int lineSegments = 15;
    [Tooltip("Droop of the slack rope as a fraction of its length (capped at a few metres). Snaps to 0 the instant you reel.")]
    public float slackSag = 0.2f;

    [Header("Sound Effects")]
    [SerializeField] AudioClip fireClip;
    [SerializeField] AudioClip latchClip;
    [SerializeField, Range(0, 1)] float sfxVolume = 0.7f;

    enum GrappleState { Idle, Flying, Anchored }

    GameObject _currentGunInstance;
    GameObject _rigRoot;
    Transform _pivot;
    Transform _resolvedMuzzle;
    Transform _holdCamera;
    PistolMotor _motor;
    Coroutine _equipCoroutine;
    bool _unlocked;
    AudioSource _audioSource;
    readonly System.Collections.Generic.List<GameObject> _pendingDestroyPivots = new System.Collections.Generic.List<GameObject>();

    // Grapple state
    GrappleState _state = GrappleState.Idle;
    GameObject _ball;
    Transform _anchorParent;       // what the ball rides; null for a miss shot
    bool _anchorHadParent;         // parent destroyed under us ⇒ reset
    Vector3 _anchorLocal;          // target in _anchorParent's local space
    Vector3 _missVel;              // miss shot: velocity relative to the frame it is parented to
    float _missTravelled;          // miss shot: metres flown so far
    Transform _hookHead;           // the grapnel seated in the barrel (hidden while a shot is out)
    Vector3 _anchorPrevPos;
    Vector3 _anchorVel;
    bool _anchorVelInit;
    bool _reelHeld;
    float _sagBlend = 1f;          // 1 = slack droop, 0 = taut

    // Rope
    GameObject _lineObject;
    LineRenderer _line;

    AxeController _axeController;
    FishingRodController _fishingRodController;
    GuitarController _guitarController;
    WaterBottleController _waterBottleController;
    PistolController _pistolController;
    PlayerPickup _playerPickup;
    PlayerController _playerController;
    Ship _ship;

    public bool IsEquipped => _currentGunInstance != null;
    public bool IsUnlocked => _unlocked;
    /// True while the ball is stuck to something.
    public bool IsAnchored => _state == GrappleState.Anchored;
    /// True while the winch is actually pulling this physics step.
    public bool IsReeling { get; private set; }

    void Awake()
    {
        // The hotbar reads hotbarIcon when it builds its registry; give it the
        // built-in side-view sprite if nothing is assigned in the Inspector.
        if (hotbarIcon == null) hotbarIcon = GrappleGunModel.BuildIcon();
    }

    void Start()
    {
        _axeController         = GetComponent<AxeController>();
        _fishingRodController  = GetComponent<FishingRodController>();
        _guitarController      = GetComponent<GuitarController>();
        _waterBottleController = GetComponent<WaterBottleController>();
        _pistolController      = GetComponent<PistolController>();
        _playerPickup          = GetComponent<PlayerPickup>();
        _playerController      = GetComponent<PlayerController>();
        _ship                  = FindObjectOfType<Ship>();
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;

        if (useFloatyMotor && GetComponent<PistolMotor>() == null)
            gameObject.AddComponent<PistolMotor>();
    }

    void OnDisable()
    {
        ResetGrapple();
    }

    void OnDestroy()
    {
        ResetGrapple();
        if (_lineObject != null) Destroy(_lineObject);
    }

    // ── Per-frame ─────────────────────────────────────────────────────────

    void Update()
    {
        if (_currentGunInstance == null) return;
        if (_ship != null && _ship.IsPiloted) { ResetGrapple(); return; }
        if (PauseState.MenuOpen) return;

        // Live-apply the hold offsets so they can be tuned in Play mode.
        if (_equipCoroutine == null && _pivot != null)
        {
            _currentGunInstance.transform.localPosition = gripOffset;
            _pivot.localPosition = holdPositionOffset;
            _pivot.localRotation = Quaternion.Euler(holdRotationOffset);
        }

        if (_equipCoroutine != null) return;

        bool uiBusy = PlayerPhoneUI.IsOpen || AIChatScreen.IsTypingActive || TutorialGate.UISelectionActive();

        if (!uiBusy && TutorialGate.FirePressed())
        {
            if (_state == GrappleState.Idle) Fire();
            else ResetGrapple();
        }

        _reelHeld = _state == GrappleState.Anchored && !uiBusy && TutorialGate.SecondaryFireHeld();

        if (_state == GrappleState.Flying) UpdateFlight();
        else if (_state == GrappleState.Anchored && _anchorHadParent && _anchorParent == null)
            ResetGrapple();   // the thing we were stuck to was destroyed

        // Taut the instant you reel; eases back to a droop when you let go.
        if (_reelHeld) _sagBlend = 0f;
        else _sagBlend = Mathf.MoveTowards(_sagBlend, 1f, Time.deltaTime / 0.35f);
    }

    void LateUpdate()
    {
        UpdateRope();
    }

    void FixedUpdate()
    {
        IsReeling = false;
        if (_state != GrappleState.Anchored || _ball == null) return;

        Vector3 anchor = _ball.transform.position;

        // Anchor velocity by finite difference (same rule as the bobber's rod
        // tip): a jump of metres in one step is an origin shift or teleport,
        // not motion — keep the previous estimate rather than spiking.
        if (_anchorVelInit)
        {
            Vector3 d = anchor - _anchorPrevPos;
            if (d.sqrMagnitude < 25f) _anchorVel = d / Time.fixedDeltaTime;
        }
        _anchorPrevPos = anchor;
        _anchorVelInit = true;

        if (!_reelHeld) return;
        if (_playerController == null) return;
        Rigidbody rb = _playerController.Rigidbody;
        if (rb == null || rb.isKinematic) return;

        Vector3 toAnchor = anchor - rb.position;
        float dist = toAnchor.magnitude;
        if (dist < 0.001f) return;
        Vector3 dir = toAnchor / dist;

        Vector3 rel = rb.velocity - _anchorVel;
        float along = Vector3.Dot(rel, dir);
        Vector3 lateral = rel - dir * along;

        // Reel at full speed until the hold band, then ease to a stop. Never
        // negative: velocity away from the anchor is simply removed, so the
        // rope never stretches and gravity can't drag you back out.
        float want = Mathf.Clamp((dist - holdDistance) * holdSnap, 0f, reelSpeed);
        bool held = dist <= holdDistance + 0.25f;
        float damping = swingDamping + (held ? heldSwingDamping : 0f);
        lateral *= Mathf.Exp(-damping * Time.fixedDeltaTime);

        rb.velocity = _anchorVel + dir * want + lateral;
        IsReeling = true;
    }

    // ── Firing / flight ───────────────────────────────────────────────────

    void Fire()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 origin = cam.transform.position;
        Vector3 forward = cam.transform.forward;

        _anchorParent = null;
        _anchorHadParent = false;
        Transform frame = null;
        if (Physics.Raycast(origin, forward, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
        {
            _anchorParent = hit.collider.transform;
            _anchorHadParent = true;
            // Bury the prongs: the hook's origin (rope end) sits a little short of the surface.
            _anchorLocal = _anchorParent.InverseTransformPoint(hit.point - forward * (GrappleGunModel.HookLength * 0.6f));
            frame = _anchorParent;
        }
        else
        {
            // A miss flies in the NEAREST PLANET'S frame and inherits the
            // shooter's speed relative to it. A fixed world-space target was
            // wrong twice over: the floating origin shifts the world under it,
            // and in orbit the player is moving at tens of m/s, so the hook
            // appeared to veer off at random.
            CelestialBody body = NearestBody(origin);
            Vector3 shooterVel = Vector3.zero;
            if (_playerController != null && _playerController.Rigidbody != null)
                shooterVel = _playerController.Rigidbody.velocity;
            if (body != null) { frame = body.transform; shooterVel -= body.velocity; }
            _missVel = forward * ballSpeed + shooterVel;
            _missTravelled = 0f;
        }

        Vector3 start = MuzzleWorld(origin + forward * 0.5f);
        _ball = CreateBall();
        _ball.transform.position = start;
        _ball.transform.rotation = Quaternion.LookRotation(forward, cam.transform.up);
        if (frame != null) _ball.transform.SetParent(frame, true);
        if (_hookHead != null) _hookHead.gameObject.SetActive(false);

        _state = GrappleState.Flying;
        _sagBlend = 0f;   // the rope pays out straight behind the ball
        if (fireClip != null && _audioSource != null) _audioSource.PlayOneShot(fireClip, sfxVolume);
        GamepadRumble.Pulse(0.2f, 0.6f, 0.08f);
    }

    void UpdateFlight()
    {
        if (_ball == null) { ResetGrapple(); return; }
        if (_anchorHadParent && _anchorParent == null) { ResetGrapple(); return; }

        Vector3 pos = _ball.transform.position;
        float dt = Time.deltaTime;

        if (_anchorParent == null)
        {
            // Miss: straight flight in the frame it is parented to, out to range.
            Vector3 stepV = _missVel * dt;
            _ball.transform.position = pos + stepV;
            _missTravelled += stepV.magnitude;
            if (_missTravelled >= range) ResetGrapple();
            return;
        }

        Vector3 target = _anchorParent.TransformPoint(_anchorLocal);
        float step = ballSpeed * dt;
        Vector3 to = target - pos;
        float remaining = to.magnitude;
        if (remaining > 0.001f) _ball.transform.rotation = Quaternion.LookRotation(to / remaining, _ball.transform.up);

        if (remaining <= step)
        {
            _ball.transform.position = target;
            Latch();
            return;
        }
        _ball.transform.position = Vector3.MoveTowards(pos, target, step);
    }

    void Latch()
    {
        _state = GrappleState.Anchored;
        _anchorVelInit = false;
        _anchorVel = Vector3.zero;
        _sagBlend = 1f;
        if (latchClip != null && _audioSource != null) _audioSource.PlayOneShot(latchClip, sfxVolume);
        GamepadRumble.Pulse(0.3f, 0.3f, 0.06f);
    }

    /// <summary>Drop the ball and the rope. Safe to call from any state.</summary>
    public void ResetGrapple()
    {
        _state = GrappleState.Idle;
        _reelHeld = false;
        IsReeling = false;
        _anchorParent = null;
        _anchorHadParent = false;
        _anchorVelInit = false;
        if (_ball != null) Destroy(_ball);
        _ball = null;
        if (_line != null) _line.enabled = false;
        if (_hookHead != null) _hookHead.gameObject.SetActive(true);
    }

    static CelestialBody NearestBody(Vector3 p)
    {
        var bodies = NBodySimulation.Bodies;
        CelestialBody nearest = null;
        float best = float.PositiveInfinity;
        if (bodies == null) return null;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.transform.position - p).sqrMagnitude;
            if (d < best) { best = d; nearest = b; }
        }
        return nearest;
    }

    GameObject CreateBall()
    {
        GameObject go;
        if (ballPrefab != null)
        {
            go = Instantiate(ballPrefab);
            foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
            var prb = go.GetComponent<Rigidbody>();
            if (prb != null) prb.isKinematic = true;
        }
        else
        {
            go = GrappleGunModel.BuildHook(null);
        }
        go.name = "GrappleHook";
        return go;
    }

    Vector3 MuzzleWorld(Vector3 fallback)
    {
        return _resolvedMuzzle != null ? _resolvedMuzzle.position : fallback;
    }

    // ── Rope ──────────────────────────────────────────────────────────────

    void EnsureLine()
    {
        if (_line != null) return;
        _lineObject = new GameObject("GrappleRope");
        _lineObject.transform.SetParent(transform);
        _line = _lineObject.AddComponent<LineRenderer>();
        if (lineMaterial == null)
        {
            // Same fallback the fishing rod ships with (proven in builds).
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.SetColor("_Color", lineColor);
        }
        _line.material = lineMaterial;
        _line.startColor = lineColor;
        _line.endColor = lineColor;
        _line.startWidth = lineWidth;
        _line.endWidth = lineWidth;
        _line.positionCount = lineSegments;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.enabled = false;
    }

    void UpdateRope()
    {
        if (_state == GrappleState.Idle || _ball == null || _currentGunInstance == null)
        {
            if (_line != null) _line.enabled = false;
            return;
        }
        EnsureLine();
        _line.enabled = true;
        _line.positionCount = lineSegments;
        _line.startWidth = lineWidth;
        _line.endWidth = lineWidth;

        Vector3 start = MuzzleWorld(transform.position);
        Vector3 end = _ball.transform.position;
        Vector3 droopDir = -transform.up;   // the player is aligned to local gravity
        float distance = Vector3.Distance(start, end);
        // Droop as a fraction of length, but never more than a few metres —
        // a kilometre of rope would otherwise sag a hundred metres.
        float droop = Mathf.Min(distance * slackSag, 2.5f) * _sagBlend;
        Vector3 control = (start + end) * 0.5f + droopDir * droop;

        for (int i = 0; i < lineSegments; i++)
        {
            float t = i / (float)(lineSegments - 1);
            float u = 1f - t;
            Vector3 p = u * u * start + 2f * u * t * control + t * t * end;
            _line.SetPosition(i, p);
        }
    }

    // ── Equip / unequip (PistolController's rig, trimmed) ─────────────────

    void EquipGun()
    {
        if (gunHoldPosition == null) return;
        if (_axeController          != null && _axeController.IsEquipped) return;
        if (_fishingRodController   != null && _fishingRodController.IsEquipped) return;
        if (_waterBottleController  != null && _waterBottleController.IsEquipped) return;
        if (_guitarController       != null && _guitarController.IsEquipped) return;
        if (_pistolController       != null && _pistolController.IsEquipped) return;
        if (_playerPickup           != null && _playerPickup.IsHoldingObject) return;

        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);

        for (int i = 0; i < _pendingDestroyPivots.Count; i++)
            if (_pendingDestroyPivots[i] != null) Destroy(_pendingDestroyPivots[i]);
        _pendingDestroyPivots.Clear();

        var rigGo = new GameObject("GrappleMotorRig");
        rigGo.transform.SetParent(gunHoldPosition, false);
        _rigRoot = rigGo;

        _motor = GetComponent<PistolMotor>();
        if (useFloatyMotor)
        {
            if (_motor == null) _motor = gameObject.AddComponent<PistolMotor>();
            _motor.SwayScale = 1f;
            _motor.Attach(rigGo.transform, holdPositionOffset);
        }

        _holdCamera = null;
        for (Transform t = gunHoldPosition; t != null; t = t.parent)
            if (t.GetComponent<Camera>() != null) { _holdCamera = t; break; }
        if (_holdCamera == null && Camera.main != null) _holdCamera = Camera.main.transform;

        var pivotGo = new GameObject("GrapplePivot");
        pivotGo.transform.SetParent(rigGo.transform, false);
        _pivot = pivotGo.transform;

        _currentGunInstance = gunPrefab != null ? Instantiate(gunPrefab, _pivot) : GrappleGunModel.BuildGun(_pivot);
        _currentGunInstance.transform.localPosition = gripOffset;
        _currentGunInstance.transform.localRotation = Quaternion.identity;

        var rb = _currentGunInstance.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        foreach (var col in _currentGunInstance.GetComponentsInChildren<Collider>()) col.enabled = false;

        _resolvedMuzzle = null;
        if (!string.IsNullOrEmpty(muzzleChildName))
            _resolvedMuzzle = FindChildByName(_currentGunInstance.transform, muzzleChildName);
        _hookHead = FindChildByName(_currentGunInstance.transform, "HookHead");

        Quaternion rest     = Quaternion.Euler(holdRotationOffset);
        Quaternion startRot = rest * Quaternion.AngleAxis(-equipStartAngle, equipRotationAxis);
        Vector3 restPos  = holdPositionOffset;
        Vector3 startPos = holdPositionOffset + equipStartPositionOffset;
        _pivot.localPosition = startPos;
        _pivot.localRotation = startRot;

        _equipCoroutine = StartCoroutine(AnimateEquipPose(startPos, restPos, startRot, rest, equipDuration, null));
    }

    void UnequipGun()
    {
        if (_currentGunInstance == null || _pivot == null) return;
        ResetGrapple();
        if (_motor != null) _motor.SwayScale = 1f;
        if (_equipCoroutine != null) StopCoroutine(_equipCoroutine);

        Quaternion startRot = _pivot.localRotation;
        Quaternion endRot   = startRot * Quaternion.AngleAxis(-180f, equipRotationAxis);
        Vector3 startPos = _pivot.localPosition;
        Vector3 endPos   = holdPositionOffset + equipStartPositionOffset;
        var pivot = _pivot;
        GameObject pivotGo = _rigRoot != null ? _rigRoot : pivot.gameObject;
        _pendingDestroyPivots.Add(pivotGo);
        var motorToDetach = GetComponent<PistolMotor>();
        if (motorToDetach != null && _rigRoot != null) motorToDetach.Detach(_rigRoot.transform);
        _rigRoot = null;
        _currentGunInstance = null;
        _pivot = null;
        _resolvedMuzzle = null;
        _hookHead = null;
        _equipCoroutine = StartCoroutine(AnimateEquipPoseOn(pivot, startPos, endPos, startRot, endRot, equipDuration, () =>
        {
            if (pivotGo != null) Destroy(pivotGo);
            _pendingDestroyPivots.Remove(pivotGo);
            _equipCoroutine = null;
        }));
    }

    IEnumerator AnimateEquipPose(Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float duration, System.Action onComplete)
    {
        Transform t = _pivot;
        yield return AnimateEquipPoseOn(t, fromPos, toPos, fromRot, toRot, duration, onComplete);
        _equipCoroutine = null;
    }

    IEnumerator AnimateEquipPoseOn(Transform t, Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float duration, System.Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration && t != null)
        {
            float u = elapsed / duration;
            t.localPosition = Vector3.Lerp(fromPos, toPos, u);
            t.localRotation = Quaternion.Slerp(fromRot, toRot, u);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (t != null) { t.localPosition = toPos; t.localRotation = toRot; }
        onComplete?.Invoke();
    }

    public void ForceEquipGrapple()
    {
        _unlocked = true;
        if (_currentGunInstance == null) EquipGun();
    }

    public void ForceUnequipGrapple()
    {
        if (_currentGunInstance != null) UnequipGun();
    }

    public void Unlock()
    {
        _unlocked = true;
    }

    static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildByName(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
