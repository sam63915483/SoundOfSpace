using UnityEngine;
using System.Collections;

public class FishingRodController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Icon shown in the hotbar slot when this item is in the bar. Assign on the Player prefab.")]
    public Sprite hotbarIcon;

    [Header("Fishing Rod Settings")]
    public GameObject fishingRodPrefab;
    public Transform rodHoldPosition;

    [Header("Hold Offset Adjuster")]
    [Tooltip("Local-space position offset relative to rodHoldPosition. Tunable in Play mode.")]
    public Vector3 holdPositionOffset = Vector3.zero;
    [Tooltip("Local-space resting rotation (Euler degrees) relative to rodHoldPosition. The equip / cast / catch animations resolve to this rotation. Tunable in Play mode.")]
    public Vector3 holdRotationOffset = Vector3.zero;

    [Header("Casting")]
    public GameObject bobberPrefab;
    public Transform castPoint;
    public string rodTipName = "RodTip";
    public float bobberShootSpeed = 5f;
    public Vector3 bobberRotationOffset = Vector3.zero;

    [Header("Fishing Line")]
    public Material lineMaterial;
    public float lineWidth = 0.02f;
    public Color lineColor = new Color(1f, 1f, 1f, 0.3f);
    [Range(2, 30)] public int lineSegments = 15;
    [Range(0f, 1f)] public float sagAmount = 0.3f;

    [Header("Sag Direction")]
    public bool autoAlignToGravity = true;
    public Vector3 sagDirectionOffset = Vector3.zero;

    [Header("Cast Animation")]
    public Vector3 castRotationAxis = Vector3.right;
    public float pullBackAngle = 50f;
    public float pullBackDuration = 0.15f;
    public float snapForwardDuration = 0.1f;
    public float overshootAngle = 5f;
    [Range(0f, 1f)] public float releasePoint = 0.7f;

    [Header("Equip Animation")]
    public float equipDuration = 0.4f;
    public float equipStartAngle = -120f;
    public float unequipEndAngle = 180f;

    [Header("Catch Animation")]
    public float catchPullBackAngle = 25f;
    public float catchPullDuration = 0.1f;
    public float catchReturnDuration = 0.25f;

    [Header("NPC Reference")]
    public NPCDialogue npcDialogue;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip castClip;
    [SerializeField, Range(0, 1)] private float castVolume = 0.6f;
    [SerializeField] private float castSoundDelay = 0f;
    [SerializeField] private AudioClip catchClip;
    [SerializeField] private AudioClip spinCatchClip;
    [SerializeField, Range(0, 1)] private float catchVolume     = 0.7f;
    [SerializeField, Range(0, 1)] private float spinCatchVolume = 0.8f;

    [Header("Spin Catch Pitch")]
    [Tooltip("Pitch added per consecutive spin catch (combo 1 = 1.0, combo 2 = 1+step, etc.)")]
    [SerializeField] private float spinCatchPitchStep = 0.1f;
    [SerializeField] private float spinCatchPitchMax  = 2.0f;

    private AudioSource audioSource;

    private GameObject currentRodInstance;
    private ViewmodelMotor _motorRig;   // floaty carry layer between the hold transform and the rod
    private GameObject currentBobber;
    private Transform lineAttachPoint;
    private Ship ship;
    private Quaternion originalRodRotation;
    private Coroutine castAnimationCoroutine;
    private Coroutine equipCoroutine;
    private GuitarController guitarController;
    private PlayerPickup playerPickup;
    private WaterBottleController waterBottleController;
    private AxeController axeController;
    private PistolController pistolController;

    // Rods whose unequip animation is still running. EquipRod drains this
    // synchronously so rapid equip/unequip spamming can't leak orphan rods.
    private readonly System.Collections.Generic.List<GameObject> _pendingDestroyRods = new System.Collections.Generic.List<GameObject>();

    // Spin combo tracking
    private PlayerController playerController;
    private bool wasPlayerGrounded = true;
    private bool trackingSpin = false;
    private float spinAccumulated = 0f;
    private float lastPlayerYaw = 0f;
    private int spinComboCount = 0;

    public bool IsEquipped => currentRodInstance != null;

    // True once the player has acquired the rod (picked up Tev's rod from the
    // cabin). Hotbar gates the rod slot on this; FishingRodPickup calls Unlock
    // when the player presses F on the rod prop. Persists via EquipmentSave.
    public bool IsUnlocked { get; private set; }

    public void Unlock() { IsUnlocked = true; }

    public static event System.Action OnBobberCast;
    public static event System.Action<float> OnFishCaught;

    private LineRenderer lineRenderer;
    private GameObject lineRendererObject;

    // The angler's rigidbody: the reference FRAME for every free-bobber
    // velocity. The planets ride sun-orbit rails, so the ground under the
    // player moves through world space at speed -- and the player's rigidbody
    // provably tracks it, or they would fly off the planet. Any bobber velocity
    // written in WORLD terms (zeroing it, damping toward zero) is therefore a
    // huge velocity relative to the ground, pointing a different way at every
    // spot on the globe. That was Sam's "retrieve works in some places and
    // stops working as the planet moves" bug, diagnosed by him almost exactly.
    Rigidbody _playerRb;
    public Vector3 OwnerVelocity => _playerRb != null ? _playerRb.velocity : Vector3.zero;

    /// The rod tip transform, for parking the wound-in bobber on.
    public Transform LineTip => lineAttachPoint;

    void Start()
    {
        ship = FindObjectOfType<Ship>();
        _playerRb = GetComponent<Rigidbody>();
        CreateLineRenderer();

        if (npcDialogue == null)
            npcDialogue = FindObjectOfType<NPCDialogue>();

        guitarController      = FindObjectOfType<GuitarController>();
        playerPickup          = GetComponent<PlayerPickup>();
        waterBottleController = GetComponent<WaterBottleController>();
        axeController         = GetComponent<AxeController>();
        pistolController      = GetComponent<PistolController>();
        playerController      = GetComponent<PlayerController>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    void CreateLineRenderer()
    {
        lineRendererObject = new GameObject("FishingLine");
        lineRendererObject.transform.SetParent(transform);
        lineRenderer = lineRendererObject.AddComponent<LineRenderer>();

        if (lineMaterial == null)
        {
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.SetColor("_Color", lineColor);
        }

        lineRenderer.material = lineMaterial;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = lineSegments;
        lineRenderer.enabled = false;
    }

    void Update()
    {
        if (ship != null && ship.IsPiloted)
            return;
        if (PlayerController.isMapOpen)
            return;

        // Live-apply the offset fields so the inspector values can be tuned in
        // Play mode without re-equipping. Skip while equip / cast / catch
        // animations are driving the rod themselves.
        Bobber liveBobber = currentBobber != null ? currentBobber.GetComponent<Bobber>() : null;
        bool reelingNow = liveBobber != null && liveBobber.IsFighting
                          && TutorialGate.FireHeld()
                          && TutorialGate.IsUnlocked(TutorialAbility.Cast);

        if (currentRodInstance != null && equipCoroutine == null && castAnimationCoroutine == null)
        {
            currentRodInstance.transform.localPosition = holdPositionOffset;
            originalRodRotation = Quaternion.Euler(holdRotationOffset);
            currentRodInstance.transform.localRotation =
                originalRodRotation * ReelPullBack(liveBobber, reelingNow);
        }

        // The mesh flex runs even mid-animation: a fish keeps pulling while the
        // hookset plays out, and freezing the bend there looked dead.
        ApplyMeshBend(liveBobber, reelingNow);
        UpdateLineTaut(liveBobber, reelingNow);


        // LMB or right-trigger pull (controller). Gated on TutorialAbility.Cast
        // so the player can't cast/reel before the CastBobberStep tutorial step
        // unlocks it. Once the tutorial ends, TutorialGate.UnlockAll makes this
        // pass through unconditionally.
        bool castUnlocked = TutorialGate.IsUnlocked(TutorialAbility.Cast);

        // Cleared the first frame the player is not holding fire — see the
        // fresh-press guard on the retrieve below.
        if (_awaitFireRelease && !TutorialGate.FireHeld()) _awaitFireRelease = false;

        if (currentRodInstance != null && castUnlocked)
        {
            Bobber bobberScript = liveBobber;

            // Gaze prompts, through the existing ToVerb()/prompt channel per
            // [INTEGRATE]. Sticky-free one-liners: the rod is in your hands, so
            // there is no gaze target to own the prompt.
            UpdateRodPrompt(bobberScript);

            // ── The fight (Phase 1) ─────────────────────────────────────────
            // HELD input, not a click: tension climbs while you reel and falls
            // while you let go. The whole skill is letting go during a run.
            if (bobberScript != null && bobberScript.IsFighting)
            {
                bool reeling = TutorialGate.FireHeld();
                bobberScript.TickFight(reeling, Time.deltaTime);
            }
            else if (bobberScript != null
                     && (bobberScript.IsHanging || bobberScript.IsReadyForLaunch))
            {
                // Hanging off the tip on its foot of line: a click casts it back
                // out, so the throw is the same bobber you reeled home and
                // nothing appears out of thin air. (Winding/glued means a cast
                // is already in progress — swallow input until it flies.)
                if (bobberScript.IsHanging && TutorialGate.FirePressed()) CastBobber();
            }
            else if (currentBobber != null && bobberScript != null)
            {
                // ── Hooking ──────────────────────────────────────────────────
                // A click sets the hook. So does simply holding the reel while
                // WORKING the lure -- a fish that hits a moving lure hooks itself
                // against the line, and it would be perverse to make the player
                // let go and re-click to claim a bite their retrieve earned.
                bool hookInput = TutorialGate.FirePressed()
                              || (bobberScript.IsRetrieving && TutorialGate.FireHeld());

                if (bobberScript.IsInWater && bobberScript.IsStriking && hookInput)
                {
                    float spin = spinAccumulated;
                    spinAccumulated = 0f;
                    trackingSpin = false;
                    if (spin >= 10f) spinComboCount++;
                    else spinComboCount = 0;

                    // Spin is BANKED at the hook and paid out when the fish is
                    // landed (Sam, 2026-09-01), so the trick is timed against the
                    // strike window exactly as before.
                    if (bobberScript.TryHookFish(transform, spin, spinComboCount))
                    {
                        bobberScript.SetRetrieving(false);
                        if (castAnimationCoroutine != null)
                            StopCoroutine(castAnimationCoroutine);
                        castAnimationCoroutine = StartCoroutine(CatchAnimation());
                    }
                }
                else
                {
                    // ── Working the lure back in ─────────────────────────────
                    // Holding the reel with nothing on drags the lure across the
                    // water rather than snapping it back to your hand. It is a
                    // real retrieve: the line comes tight, the rod takes a little
                    // bend, and a fish may well take it on the way in.
                    // A cast that is still holding the button must NOT start
                    // winding itself straight back in. Require the trigger to be
                    // let go once after the cast, the standard fresh-press guard.
                    bool winding = TutorialGate.FireHeld() && !_awaitFireRelease;
                    if (winding && !bobberScript.IsRetrieving && TutorialGate.FirePressed())
                    {
                        if (castAnimationCoroutine != null)
                            StopCoroutine(castAnimationCoroutine);
                        castAnimationCoroutine = StartCoroutine(CatchAnimation());
                    }
                    bobberScript.SetRetrieving(winding);
                }
            }
            else if (TutorialGate.FirePressed())
            {
                // BAIT IS OPTIONAL. Casting bare-handed works -- bites are just
                // slower and skew common, and a rare is still possible.
                CastBobber();
            }
        }

        // NOTE: the line is NOT drawn here — see OnBeforeRenderLine below.

        UpdateSpinTracking();
    }

    void OnEnable()  { Application.onBeforeRender += OnBeforeRenderLine; }
    void OnDisable() { Application.onBeforeRender -= OnBeforeRenderLine; }

    /// <summary>
    /// The fishing line is latched as late as possible — after every LateUpdate,
    /// immediately before the frame renders.
    ///
    /// It used to be drawn from Update, which read the rod tip a full frame
    /// stale: the tip's real pose isn't settled until ViewmodelMotor (150) has
    /// swayed the rig against a camera that CameraTransformFX (100) has already
    /// rolled. Strafing left/right drives BOTH of those — the head tilt and the
    /// rig's own strafeRoll — so the tip swung centimetres per frame while the
    /// line was still drawn from the previous frame's tip. That read as the line
    /// jittering during strafes, and as a permanent gap between the line and the
    /// rod tip whenever the player was turning at all.
    ///
    /// onBeforeRender rather than a high DefaultExecutionOrder because it is
    /// ordering-proof: KillShotCam (250) and TrailerFreeCam (200) also move the
    /// camera, and anything added later lands ahead of this automatically.
    /// </summary>
    void OnBeforeRenderLine()
    {
        if (currentBobber == null || lineRenderer == null) return;
        if (ship != null && ship.IsPiloted) return;
        if (PlayerController.isMapOpen) return;
        UpdateFishingLine();
    }

    void UpdateSpinTracking()
    {
        if (currentBobber == null || playerController == null)
        {
            trackingSpin = false;
            spinAccumulated = 0f;
            wasPlayerGrounded = true;
            return;
        }

        Bobber b = currentBobber.GetComponent<Bobber>();
        if (b == null || !b.IsStriking)
        {
            trackingSpin = false;
            spinAccumulated = 0f;
            wasPlayerGrounded = playerController.IsOnGround;
            return;
        }

        bool grounded = playerController.IsOnGround;
        float currentYaw = transform.eulerAngles.y;

        if (wasPlayerGrounded && !grounded)
        {
            // Player just jumped — start fresh spin tracking
            trackingSpin = true;
            spinAccumulated = 0f;
            lastPlayerYaw = currentYaw;
        }
        else if (!wasPlayerGrounded && grounded)
        {
            // Player landed — stop accumulating, keep total until next jump or catch
            trackingSpin = false;
        }
        else if (trackingSpin && !grounded)
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(lastPlayerYaw, currentYaw));
            spinAccumulated += delta;
            lastPlayerYaw = currentYaw;
        }

        wasPlayerGrounded = grounded;
    }

    Vector3 GetSagDirection()
    {
        if (!autoAlignToGravity)
            return sagDirectionOffset.normalized;

        // Use the cached body list from NBodySimulation rather than
        // FindObjectsOfType every frame (this method runs every Update while
        // a bobber is in the water).
        var bodies = NBodySimulation.Bodies;
        CelestialBody nearest = null;
        float minDist = Mathf.Infinity;
        Vector3 playerPos = transform.position;

        if (bodies != null)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (body == null) continue;
                float dist = Vector3.Distance(playerPos, body.transform.position);
                if (dist < minDist) { minDist = dist; nearest = body; }
            }
        }

        Vector3 gravityDir = Vector3.down;
        if (nearest != null)
            gravityDir = (nearest.transform.position - playerPos).normalized;

        if (sagDirectionOffset != Vector3.zero)
        {
            Quaternion offsetRot = Quaternion.LookRotation(gravityDir) * Quaternion.Euler(sagDirectionOffset);
            return offsetRot * Vector3.forward;
        }

        return gravityDir;
    }

    /// <summary>
    /// Where the line leaves the rod, in world space, accounting for the mesh
    /// bend. RodTip is a plain child Transform that a vertex deformation does
    /// not move, so without running it through the same bend the line launches
    /// from where the tip WOULD be if the rod were straight.
    ///
    /// Also the bobber's home: it is cast FROM here and wound back UP to here.
    /// </summary>
    public Vector3 LineOriginWorld
    {
        get
        {
            Transform tip = lineAttachPoint != null ? lineAttachPoint : castPoint;
            if (tip == null) return transform.position;
            Vector3 p = tip.position + tip.rotation * lineTipOffset;
            return _rodBend != null ? _rodBend.BentWorldPoint(p) : p;
        }
    }


    void UpdateFishingLine()
    {
        Transform attachPoint = lineAttachPoint != null ? lineAttachPoint : castPoint;
        if (attachPoint == null) return;

        if (currentBobber == null)
        {
            lineRenderer.enabled = false;
            return;
        }
        var stateCheck = currentBobber.GetComponent<Bobber>();
        if (stateCheck != null && stateCheck.IsReadyForLaunch)
        {
            // Wound to the tip for the throw: a zero-length line is an artifact.
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.positionCount = lineSegments;

        DrawLine(LineOriginWorld, currentBobber.transform.position, LineSagNow());
    }

    /// How much the line droops right now, 0 = bar tight.
    float LineSagNow()
    {
        float sag = sagAmount;
        if (_lineTaut > 0.001f) sag *= Mathf.Lerp(SemiTautSag, FullTautSag, _lineTaut);
        return sag;
    }

    /// <summary>Lay the line renderer along a sagging curve between two points.</summary>
    void DrawLine(Vector3 start, Vector3 end, float sag)
    {
        Vector3 droopDir = GetSagDirection();

        Vector3 midPoint = (start + end) * 0.5f;
        float distance = Vector3.Distance(start, end);

        // A loaded line goes TAUT. Slack line sags; at full tension it is nearly
        // straight, and it shivers during a run. Between this and the rod bend,
        // the fight is readable with the HUD bar switched off entirely.
        Vector3 controlPoint = midPoint + droopDir * (distance * sag);

        for (int i = 0; i < lineSegments; i++)
        {
            float t = i / (float)(lineSegments - 1);
            Vector3 point = QuadraticBezier(start, controlPoint, end, t);
            lineRenderer.SetPosition(i, point);
        }
    }

    Vector3 QuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }

    void ResetSpinCombo() { spinComboCount = 0; }

    public void ForceEquipRod()
    {
        if (currentRodInstance != null) return;
        EquipRod();
    }

    public void ForceUnequipRod()
    {
        if (currentRodInstance == null) return;
        UnequipRod();
    }

    void EquipRod()
    {
        if (fishingRodPrefab == null) return;
        if (guitarController      != null && guitarController.IsEquipped)      return;
        if (waterBottleController != null && waterBottleController.IsEquipped) return;
        if (axeController         != null && axeController.IsEquipped)         return;
        if (pistolController      != null && pistolController.IsEquipped)      return;
        if (playerPickup          != null && playerPickup.IsHoldingObject)     return;

        if (equipCoroutine != null) StopCoroutine(equipCoroutine);
        if (castAnimationCoroutine != null) StopCoroutine(castAnimationCoroutine);

        for (int i = 0; i < _pendingDestroyRods.Count; i++)
            if (_pendingDestroyRods[i] != null) Destroy(_pendingDestroyRods[i]);
        _pendingDestroyRods.Clear();

        // Floaty carry: a ViewmodelMotor rig sits between the hold transform and
        // the rod, so the existing equip / cast tweens (which drive the rod's own
        // localRotation) are untouched and simply ride on top of the sway.
        _motorRig = ViewmodelMotor.CreateRig(rodHoldPosition, "RodMotorRig", rodMotorRestOffset, holdPositionOffset);
        currentRodInstance = Instantiate(fishingRodPrefab, _motorRig.transform);
        currentRodInstance.transform.localPosition = holdPositionOffset;
        // Held viewmodels never cast shadows — otherwise the sun throws the rod's
        // silhouette onto the ground ahead as a blob pinned to your view.
        foreach (var r in currentRodInstance.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        originalRodRotation = Quaternion.Euler(holdRotationOffset);

        Quaternion startRot = originalRodRotation * Quaternion.AngleAxis(equipStartAngle, castRotationAxis);
        currentRodInstance.transform.localRotation = startRot;

        Transform tip = currentRodInstance.transform.Find(rodTipName);
        if (tip == null)
            tip = FindDeepChild(currentRodInstance.transform, rodTipName);
        lineAttachPoint = tip;

        if (lineAttachPoint == null)
            Debug.LogWarning($"Rod tip '{rodTipName}' not found!");

        Rigidbody rb = currentRodInstance.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        Collider col = currentRodInstance.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        SpawnAttachedBobber();

        equipCoroutine = StartCoroutine(AnimateEquip(startRot, originalRodRotation, equipDuration));
    }

    IEnumerator AnimateEquip(Quaternion from, Quaternion to, float duration)
    {
        float elapsed = 0f;
        Transform rodTransform = currentRodInstance.transform;

        while (elapsed < duration)
        {
            rodTransform.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rodTransform.localRotation = to;

        originalRodRotation = to;
        equipCoroutine = null;

        // The rod has landed in the hand: the bobber on the tip gets its
        // physics and drops down to hang on its line.
        if (currentBobber != null)
        {
            var b = currentBobber.GetComponent<Bobber>();
            if (b != null) b.DropToHang();
        }

        Debug.Log("Fishing rod equipped.");
    }

    void UnequipRod()
    {
        if (currentRodInstance == null) return;

        if (equipCoroutine != null) StopCoroutine(equipCoroutine);
        if (castAnimationCoroutine != null) StopCoroutine(castAnimationCoroutine);

        ReelInBobber();

        Quaternion targetRot = originalRodRotation * Quaternion.AngleAxis(unequipEndAngle, castRotationAxis);

        // Capture the instance and clear "equipped" state IMMEDIATELY so a
        // hotbar swap to another item (which checks rod.IsEquipped) sees the
        // slot as free during the put-away animation, instead of having to
        // wait for the animation to finish before the next item can equip.
        // Queue the RIG for destruction, not the rod — the rod is its child, so
        // destroying the rig takes both and no orphan ViewmodelMotor is left
        // under the hold transform. The put-away tween still drives the rod's
        // own transform, which rides the rig exactly as it did while equipped.
        var instance = currentRodInstance;
        var rigGo = _motorRig != null ? _motorRig.gameObject : instance;
        _pendingDestroyRods.Add(rigGo);
        _motorRig = null;
        currentRodInstance = null;
        lineAttachPoint = null;
        _rodBend = null;
        _meshBendAngle = 0f;
        _pullBackAngle = 0f;

        equipCoroutine = StartCoroutine(AnimateUnequip(instance, rigGo, originalRodRotation, targetRot, equipDuration));
    }

    IEnumerator AnimateUnequip(GameObject rod, GameObject rigGo, Quaternion from, Quaternion to, float duration)
    {
        if (rod == null) { if (rigGo != null) Destroy(rigGo); _pendingDestroyRods.Remove(rigGo); equipCoroutine = null; yield break; }
        float elapsed = 0f;
        Transform rodTransform = rod.transform;

        while (elapsed < duration)
        {
            if (rodTransform == null) { _pendingDestroyRods.Remove(rigGo); equipCoroutine = null; yield break; }
            rodTransform.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (rodTransform != null) rodTransform.localRotation = to;

        if (rigGo != null) Destroy(rigGo);
        _pendingDestroyRods.Remove(rigGo);
        equipCoroutine = null;

        Debug.Log("Fishing rod unequipped.");
    }

    Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Drop the line and take the bobber out of the world.
    ///
    /// Called on a retrieve finishing, on a fight ending, and -- importantly --
    /// on UNEQUIP. Sam, 2026-09-01: "if you switch from your fishing rod to
    /// another hotbar slot the bobber and line get retracted immediately to
    /// avoid complications." A bobber left floating while the player holds an
    /// axe would keep running its fishing coroutine and drawing a line to a rod
    /// that is not there.
    /// </summary>
    void ReelInBobber()
    {
        if (currentBobber != null)
        {
            Destroy(currentBobber);
            currentBobber = null;
            Debug.Log("Bobber reeled in.");
        }
        if (lineRenderer != null) lineRenderer.enabled = false;
        // Belt and braces: the bar must never survive the thing it belonged to.
        FishingTensionHUD.Hide();
        _lineTaut = 0f;
        _meshBendAngle = 0f;
        _pullBackAngle = 0f;
    }

    // True from the moment of a cast until the player lets go of fire. Stops a
    // held button from casting and immediately winding back in.
    bool _awaitFireRelease;

    void CastBobber()
    {
        if (bobberPrefab == null || castPoint == null) return;
        if (currentRodInstance == null) return;
        // Either nothing is out (first cast) or the wound-in bobber is hanging
        // off the tip waiting to be thrown again.
        Bobber hanging = null;
        if (currentBobber != null)
        {
            hanging = currentBobber.GetComponent<Bobber>();
            if (hanging == null || !hanging.IsHanging) return;
        }
        _awaitFireRelease = true;

        // The foot of line retracts over the rod's pull-back, so by the time the
        // fling comes forward the bobber is stuck to the tip and flies from it.
        if (hanging != null) hanging.WindToTip();

        if (castAnimationCoroutine != null)
            StopCoroutine(castAnimationCoroutine);
        castAnimationCoroutine = StartCoroutine(CastAnimation());
        OnBobberCast?.Invoke();
    }

    IEnumerator PlayCastSoundDelayed()
    {
        if (castSoundDelay > 0f)
            yield return new WaitForSeconds(castSoundDelay);
        if (castClip != null && audioSource != null)
            audioSource.PlayOneShot(castClip, castVolume);
    }

    void PlayCatchSound(int spinCombo)
    {
        if (audioSource == null) return;
        bool isSpinCatch = spinCombo > 0;
        AudioClip clip = (isSpinCatch && spinCatchClip != null) ? spinCatchClip : catchClip;
        float vol      = (isSpinCatch && spinCatchClip != null) ? spinCatchVolume : catchVolume;
        float pitch    = isSpinCatch
            ? Mathf.Min(1f + (spinCombo - 1) * spinCatchPitchStep, spinCatchPitchMax)
            : 1f;
        if (clip == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, vol);
    }

    IEnumerator CastAnimation()
    {
        if (castClip != null && audioSource != null)
            StartCoroutine(PlayCastSoundDelayed());

        Transform rodTransform = currentRodInstance.transform;
        Quaternion original = originalRodRotation;

        Quaternion pulledBack = original * Quaternion.AngleAxis(-pullBackAngle, castRotationAxis);
        float elapsed = 0f;
        while (elapsed < pullBackDuration)
        {
            rodTransform.localRotation = Quaternion.Slerp(original, pulledBack, elapsed / pullBackDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rodTransform.localRotation = pulledBack;

        yield return new WaitForSeconds(0.02f);

        Quaternion overshoot = original * Quaternion.AngleAxis(overshootAngle, castRotationAxis);
        elapsed = 0f;
        bool bobberSpawned = false;

        while (elapsed < snapForwardDuration)
        {
            float t = elapsed / snapForwardDuration;
            rodTransform.localRotation = Quaternion.Slerp(pulledBack, overshoot, t);

            if (!bobberSpawned && t >= releasePoint)
            {
                bobberSpawned = true;
                SpawnBobber();
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
        rodTransform.localRotation = overshoot;

        if (!bobberSpawned)
            SpawnBobber();

        elapsed = 0f;
        float settleDuration = 0.1f;
        while (elapsed < settleDuration)
        {
            rodTransform.localRotation = Quaternion.Slerp(overshoot, original, elapsed / settleDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rodTransform.localRotation = original;

        castAnimationCoroutine = null;
    }

    /// <summary>
    /// The cast animation's release point: instantiate the bobber and throw it.
    /// This is the flow that always worked — a fresh, fully physical object per
    /// cast, flying and bouncing under GravityObjectSimple. It spawns at the
    /// rod tip so the throw visibly leaves the rod.
    /// </summary>
    void SpawnBobber()
    {
        Vector3 camForward = Camera.main.transform.forward;

        // The bobber that was wound home is thrown back out -- same object.
        if (currentBobber != null)
        {
            var parked = currentBobber.GetComponent<Bobber>();
            if (parked == null || !parked.IsReadyForLaunch) return;
            Rigidbody ownerRb = GetComponent<Rigidbody>();
            parked.RelaunchFromTip(
                ownerRb != null ? ownerRb.velocity : Vector3.zero,
                camForward, bobberShootSpeed,
                Quaternion.LookRotation(camForward) * Quaternion.Euler(bobberRotationOffset));
            if (lineRenderer != null) lineRenderer.enabled = true;
            Debug.Log("Bobber released (relaunch).");
            return;
        }

        Vector3 spawnPos = LineOriginWorld;
        Quaternion spawnRot = Quaternion.LookRotation(camForward)
                            * Quaternion.Euler(bobberRotationOffset);

        currentBobber = Instantiate(bobberPrefab, spawnPos, spawnRot);

        Rigidbody bobberRb = currentBobber.GetComponent<Rigidbody>();
        if (bobberRb != null)
        {
            Rigidbody playerRb = GetComponent<Rigidbody>();
            Vector3 inheritedVelocity = playerRb != null ? playerRb.velocity : Vector3.zero;

            bobberRb.isKinematic = false;
            bobberRb.velocity = inheritedVelocity;
            bobberRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            bobberRb.interpolation = RigidbodyInterpolation.Interpolate;
            bobberRb.useGravity = false;

            bobberRb.AddForce(camForward * bobberShootSpeed, ForceMode.VelocityChange);
        }

        GravityObjectSimple grav = currentBobber.GetComponent<GravityObjectSimple>();
        if (grav == null)
            grav = currentBobber.AddComponent<GravityObjectSimple>();
        grav.enabled = true;

        EndlessManager em = FindObjectOfType<EndlessManager>();
        if (em != null)
            em.RegisterPhysicsObject(currentBobber.transform);

        WireBobber(currentBobber);

        if (lineRenderer != null) lineRenderer.enabled = true;

        Debug.Log("Bobber released.");
    }

    /// <summary>
    /// Everything a freshly instantiated bobber needs from the rod, in one
    /// place so the cast spawn and the equip spawn cannot drift apart.
    ///
    /// The player-collision ignore is set here, once per instance: the bobber
    /// hangs half a metre from the capsule and colliding with it would rattle
    /// the hang forever. Nothing in the lifecycle ever disables these
    /// colliders — which matters, because disabling a collider silently clears
    /// its ignore pairs.
    /// </summary>
    void WireBobber(GameObject bobberGo)
    {
        IgnorePlayerCollisions(bobberGo);

        Bobber bobberScript = bobberGo.GetComponent<Bobber>();
        if (bobberScript != null)
        {
            bobberScript.shootSpeed = bobberShootSpeed;
            // Method references instead of per-cast closure allocations.
            bobberScript.OnFishEscaped += ResetSpinCombo;
            bobberScript.OnLineSnapped += OnLineSnapped;
            bobberScript.OnFightEnded  += OnFightEnded;
            bobberScript.OnFishLanded  += HandleFishLanded;
            bobberScript.rodOwner = this;
        }
    }

    /// <summary>
    /// Bobber-vs-player collision ignores. Public because the Bobber must
    /// RE-ASSERT them every time physics re-attaches: IgnoreCollision state is
    /// silently cleared when a collider is disabled, and the equip glue
    /// (SetupAttached) disables every bobber collider -- so the pairs set at
    /// spawn do not survive to the first cast on their own.
    /// </summary>
    public void IgnorePlayerCollisions(GameObject bobberGo)
    {
        if (bobberGo == null) return;
        foreach (var mine in bobberGo.GetComponentsInChildren<Collider>(true))
            foreach (var theirs in GetComponentsInChildren<Collider>(true))
                if (mine != null && theirs != null && !mine.isTrigger && !theirs.isTrigger)
                    Physics.IgnoreCollision(mine, theirs, true);
    }

    /// <summary>
    /// Equip: the bobber is on the tip from the very first frame of the equip
    /// animation, glued as a prop. When the animation lands, AnimateEquip calls
    /// DropToHang and it gets its physics and falls onto its line. Sam: "anytime
    /// I equip the rod, the bobber should already be attached to the tip."
    /// </summary>
    void SpawnAttachedBobber()
    {
        if (bobberPrefab == null || lineAttachPoint == null || currentBobber != null) return;
        currentBobber = Instantiate(bobberPrefab, LineOriginWorld, Quaternion.identity);
        WireBobber(currentBobber);
        var b = currentBobber.GetComponent<Bobber>();
        if (b != null) b.SetupAttached(this);
    }

    IEnumerator CatchAnimation()
    {
        if (currentRodInstance == null) yield break;

        Transform rodTransform = currentRodInstance.transform;
        Quaternion original = originalRodRotation;

        Quaternion pulledBack = original * Quaternion.AngleAxis(-catchPullBackAngle, castRotationAxis);
        float elapsed = 0f;
        while (elapsed < catchPullDuration)
        {
            rodTransform.localRotation = Quaternion.Slerp(original, pulledBack, elapsed / catchPullDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rodTransform.localRotation = pulledBack;

        elapsed = 0f;
        while (elapsed < catchReturnDuration)
        {
            rodTransform.localRotation = Quaternion.Slerp(pulledBack, original, elapsed / catchReturnDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rodTransform.localRotation = original;

        castAnimationCoroutine = null;
    }

    // Smoothed pose values, so the rod loads and unloads instead of snapping
    // between frames.
    float _pullBackAngle;    // whole-rod rotation: you hauling back on it
    float _meshBendAngle;    // mesh deformation: the rod itself flexing
    float _lineTaut;         // 0 = hanging slack, 1 = bar-tight
    RodBend _rodBend;

    // How much of the authored sag survives at each end of the tautness range.
    // Semi-taut still reads as a LINE; full taut is nearly straight but never
    // perfectly so, because a dead-straight line looks like a laser.
    // A line with nothing on it hangs at its FULL authored sag; only load pulls
    // it up. Holding it at a third of the sag while idle was why a slack line
    // still looked half-tight.
    const float SemiTautSag = 1f;
    const float FullTautSag = 0.05f;

    /// <summary>
    /// Read the line's tightness off the FIGHT rather than recomputing it.
    ///
    /// The controller used to keep its own smoothed copy, which meant the line
    /// you looked at and the gate that decides when the rod may bend were two
    /// different numbers that could disagree -- exactly the two-places-computing
    /// -the-same-thing shape that every economy bug in this project came from.
    /// Outside a fight the line simply hangs.
    /// </summary>
    void UpdateLineTaut(Bobber b, bool reeling)
    {
        _lineTaut = b != null ? b.LineTaut01 : 0f;
    }

    /// <summary>
    /// How far you are hauling the rod BACK right now.
    ///
    /// Sam, 2026-09-01: "right now when you click and hold to reel a fish in,
    /// your rod tip actually goes down towards the fish rather than pulling it
    /// back up and away from the fish which is wrong." Exactly right, and the
    /// sign was the bug: the cast animation pulls back with a NEGATIVE angle on
    /// castRotationAxis, and the old bend used a positive one, so reeling
    /// pushed the rod down at the water instead of hauling away from it.
    ///
    /// Two separate things are going on and they were conflated:
    ///   - THIS is the angler. Reeling hauls the rod back and up.
    ///   - RodBend is the rod. The fish's pull bows the blank toward the water.
    /// Doing both at once is what makes the deep bend, and the deep bend is
    /// what snaps the line.
    /// </summary>
    Quaternion ReelPullBack(Bobber b, bool reeling)
    {
        var tune = FishingTuning.Active;
        float target = 0f;
        if (b != null && reeling && (b.IsFighting || b.IsRetrieving))
        {
            // An empty lure is barely any weight, so the haul is slighter.
            target = b.IsFighting ? tune.reelPullBackAngle : tune.reelPullBackAngle * 0.4f;
            // Hauling against a running fish is the big heave.
            if (b.FightIsRunning) target += tune.runPullBackExtra;
        }
        // Asymmetric: hauling back takes effort, letting go is instant. Sam:
        // "once you release left click the rod should very quickly return to its
        // normal position".
        float rate = target > _pullBackAngle ? tune.rodBendResponse : tune.rodReleaseResponse;
        _pullBackAngle = Mathf.Lerp(_pullBackAngle, target,
                                    1f - Mathf.Exp(-rate * Time.deltaTime));
        if (Mathf.Abs(_pullBackAngle) < 0.01f) return Quaternion.identity;
        // NEGATIVE = back and up, matching the cast animation's pull-back.
        return Quaternion.AngleAxis(-_pullBackAngle, castRotationAxis);
    }

    /// <summary>
    /// Flex the rod's actual mesh toward whatever is pulling on it.
    ///
    /// Load comes from both ends: the fish's pull (tension, doubled during a
    /// run) and your own reeling. Both together bows the rod hard -- and since
    /// reeling into a run is also what spikes tension toward the snap, the
    /// deepest bend you will ever see is also the moment you are about to lose
    /// the fish. The visual and the danger are the same thing, which is the
    /// point: you can read the fight off the rod without looking at the bar.
    /// </summary>
    void ApplyMeshBend(Bobber b, bool reeling)
    {
        if (currentRodInstance == null) return;
        var tune = FishingTuning.Active;

        if (_rodBend == null)
        {
            _rodBend = currentRodInstance.GetComponent<RodBend>();
            if (_rodBend == null) _rodBend = currentRodInstance.AddComponent<RodBend>();
        }

        float target = 0f;
        Vector3 pullTarget = currentRodInstance.transform.position
                           + currentRodInstance.transform.up * 2f;

        if (b != null && (b.IsFighting || b.IsRetrieving))
        {
            // ACTIVE load, not the resting bar. Sam, 2026-09-01: "the rod starts
            // bending way too much way too early... when it stops fighting make
            // the rod stop bending". So a rod with nothing happening on it goes
            // straight, and only pulling or a run loads it up.
            //
            // Through the KNEE CURVE: barely any bend below half load, then it
            // runs away toward maximum as the breaking point approaches. That is
            // what makes a deeply bent rod mean something.
            // RodLoad01 is ZERO until the line is tight -- the rod cannot be
            // loaded through slack line, and that ordering is the whole cascade.
            // It covers a retrieve as well as a fight, so winding an empty lure
            // puts a small honest bend in the rod.
            float load = b.RodLoad01(reeling);
            if (b.IsFighting && b.FightIsSpent) load *= 0.5f;
            target = tune.maxRodBend * FishingRules.BendCurve(Mathf.Clamp01(load),
                                                              tune.bendKnee, tune.bendAtKnee);
            // BendTargetPose: the bobber's drawn pose. Never a raw rb.position
            // read at render rate -- the raw pose of an interpolated body
            // sawtooths ~vel x lag against the rendered world, and the bend
            // feeds the tip feeds _tipVel feeds the tow.
            pullTarget = b.BendTargetPose;
        }

        // Loads gradually, unloads fast — a rod under strain bends into it, and
        // springs back the moment the strain comes off.
        float rate = target > _meshBendAngle ? tune.rodBendResponse : tune.rodReleaseResponse;
        _meshBendAngle = Mathf.Lerp(_meshBendAngle, target,
                                    1f - Mathf.Exp(-rate * Time.deltaTime));
        _rodBend.Apply(_meshBendAngle, pullTarget);
    }

    // What the prompt last said, so we only push a change (per-frame UI strings
    // are gated behind change-detection, per the conventions in CLAUDE.md).
    string _lastRodPrompt;

    /// <summary>
    /// "reel [hold]" while a fish is on. Deliberately does NOT claim the
    /// reticle: the crosshair's triangle-to-square morph means "there is an
    /// interactable in front of you", and a message about the rod in your own
    /// hands must not lie about that (Sam, 2026-09-01).
    ///
    /// The idle "cast" hint was removed too -- it fired every time the rod came
    /// out and said nothing the player didn't know.
    /// </summary>
    void UpdateRodPrompt(Bobber bobberScript)
    {
        string want = (bobberScript != null && bobberScript.IsFighting) ? "reel [hold]" : null;
        if (want == _lastRodPrompt) return;
        _lastRodPrompt = want;
        if (want != null) InteractPromptUI.ShowOneShot(want, 1.5f, false);
    }

    /// The line snapped: a sharp rod recoil and the snap sound. The fish and
    /// the bait are already gone by the time this runs.
    void OnLineSnapped()
    {
        if (currentRodInstance == null) return;
        if (castAnimationCoroutine != null) StopCoroutine(castAnimationCoroutine);
        castAnimationCoroutine = StartCoroutine(SnapAnimation());
        if (snapClip != null && audioSource != null)
        {
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(snapClip, snapVolume);
        }
        GamepadRumble.Pulse(1f, 1f, 0.25f);
    }

    /// <summary>
    /// The fish is actually in hand. Raised by the Bobber at the moment the
    /// catch is booked, which is now AFTER the bobber has been wound home --
    /// Sam: "even when catching a fish the bobber should be reeled back up to
    /// its resting position before it counts."
    ///
    /// OnFishCaught is load-bearing: the tutorial's catch-a-fish step,
    /// BonusTutorial and HintTrackRunner all listen on it, and its contract is
    /// "fires only on a successful catch".
    /// </summary>
    void HandleFishLanded(float spin, int combo)
    {
        PlayCatchSound(combo);
        OnFishCaught?.Invoke(spin);
    }

    /// <summary>
    /// A fight ended. Whether that ends the CAST depends on how:
    ///
    ///   Landed  - the fish is in your hand, wind in.
    ///   Snapped - the line parted, the rig is gone, wind in.
    ///   Slipped - the fish spat the hook. Nothing broke: the bobber stays
    ///             floating exactly where it is and the cast fishes on. Winding
    ///             in here was Sam's bug ("the bobber and line just disappear
    ///             because you failed to reel the fish in. that's not good").
    /// </summary>
    void OnFightEnded(FightOutcome outcome)
    {
        ResetSpinCombo();
        // Sam's design, 2026-09-02: a snapped line no longer destroys the
        // rig. The float bobs back up and keeps fishing, same as a spat
        // hook -- the recoil animation and snap sound (OnLineSnapped) are
        // the whole cost, plus the fish and bait. Landed still finishes via
        // the tow, which parks the bobber on the tip.
    }

    /// The recoil when the line parts: a hard flick back, then a loose settle.
    /// Deliberately snappier than CatchAnimation -- losing a fish should not
    /// look like landing one.
    IEnumerator SnapAnimation()
    {
        if (currentRodInstance == null) yield break;
        Transform rodTransform = currentRodInstance.transform;
        Quaternion original = originalRodRotation;
        Quaternion kicked = original * Quaternion.AngleAxis(snapRecoilAngle, castRotationAxis);

        float elapsed = 0f;
        while (elapsed < 0.06f)
        {
            rodTransform.localRotation = Quaternion.Slerp(original, kicked, elapsed / 0.06f);
            elapsed += Time.deltaTime;
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < 0.45f)
        {
            rodTransform.localRotation = Quaternion.Slerp(kicked, original, elapsed / 0.45f);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rodTransform.localRotation = original;
        castAnimationCoroutine = null;
    }

    // (Appended at the END per the serialization convention in CLAUDE.md.)
    [Header("Floaty Carry (ViewmodelMotor)")]
    [Tooltip("Camera-space offset of the whole rod chain from rodHoldPosition — the 'hold it further out' dial. X pushes right, Z pushes away from you. Tune the sway itself on the RodMotorRig in Play mode.")]
    public Vector3 rodMotorRestOffset = new Vector3(0.03f, 0f, 0.18f);

    [Header("Fight (Phase 1)")]
    [Tooltip("Sound when the line parts under too much tension.")]
    public AudioClip snapClip;
    [Range(0f, 1f)] public float snapVolume = 0.75f;
    [Tooltip("Degrees the rod flicks back when the line snaps.")]
    public float snapRecoilAngle = 42f;

    [Header("Line Attach Trim")]
    [Tooltip("Metres of extra offset for the line's start point, in the RodTip marker's own local axes. The marker in fishing_rod.prefab is hand-placed, so it can sit slightly short of the mesh's real tip. Tunable in Play mode — cast, then nudge until the line meets the rod.")]
    public Vector3 lineTipOffset = Vector3.zero;
}