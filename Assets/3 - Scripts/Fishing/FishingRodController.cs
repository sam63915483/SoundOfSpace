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

    void Start()
    {
        ship = FindObjectOfType<Ship>();
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
        if (currentRodInstance != null && equipCoroutine == null && castAnimationCoroutine == null)
        {
            currentRodInstance.transform.localPosition = holdPositionOffset;
            originalRodRotation = Quaternion.Euler(holdRotationOffset);
            currentRodInstance.transform.localRotation = originalRodRotation;
        }

        // LMB or right-trigger pull (controller). Gated on TutorialAbility.Cast
        // so the player can't cast/reel before the CastBobberStep tutorial step
        // unlocks it. Once the tutorial ends, TutorialGate.UnlockAll makes this
        // pass through unconditionally.
        if (currentRodInstance != null && TutorialGate.FirePressed() && TutorialGate.IsUnlocked(TutorialAbility.Cast))
        {
            if (currentBobber != null)
            {
                Bobber bobberScript = currentBobber.GetComponent<Bobber>();
                if (bobberScript != null)
                {
                    // Try to catch a fish (only works during strike)
                    if (bobberScript.IsInWater && bobberScript.IsStriking)
                    {
                        float spin = spinAccumulated;
                        spinAccumulated = 0f;
                        trackingSpin = false;
                        if (spin >= 10f) spinComboCount++;
                        else spinComboCount = 0;
                        bool caught = bobberScript.TryCatchFish(spin, spinComboCount);
                        if (caught)
                        {
                            PlayCatchSound(spinComboCount);
                            OnFishCaught?.Invoke(spin);
                        }
                    }

                    // Play the catch/reel animation every time
                    if (castAnimationCoroutine != null)
                        StopCoroutine(castAnimationCoroutine);
                    castAnimationCoroutine = StartCoroutine(CatchAnimation());

                    // Always reel in after click
                    ReelInBobber();
                }
            }
            else
            {
                CastBobber();
            }
        }

        if (currentBobber != null && lineRenderer != null)
            UpdateFishingLine();

        UpdateSpinTracking();
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

    void UpdateFishingLine()
    {
        Transform attachPoint = lineAttachPoint != null ? lineAttachPoint : castPoint;
        if (attachPoint == null || currentBobber == null) return;

        lineRenderer.enabled = true;
        lineRenderer.positionCount = lineSegments;

        Vector3 start = attachPoint.position;
        Vector3 end = currentBobber.transform.position;
        Vector3 droopDir = GetSagDirection();

        Vector3 midPoint = (start + end) * 0.5f;
        float distance = Vector3.Distance(start, end);
        Vector3 controlPoint = midPoint + droopDir * (distance * sagAmount);

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

        currentRodInstance = Instantiate(fishingRodPrefab, rodHoldPosition);
        currentRodInstance.transform.localPosition = holdPositionOffset;
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
        var instance = currentRodInstance;
        _pendingDestroyRods.Add(instance);
        currentRodInstance = null;
        lineAttachPoint = null;

        equipCoroutine = StartCoroutine(AnimateUnequip(instance, originalRodRotation, targetRot, equipDuration));
    }

    IEnumerator AnimateUnequip(GameObject rod, Quaternion from, Quaternion to, float duration)
    {
        if (rod == null) { equipCoroutine = null; yield break; }
        float elapsed = 0f;
        Transform rodTransform = rod.transform;

        while (elapsed < duration)
        {
            if (rodTransform == null) { _pendingDestroyRods.Remove(rod); equipCoroutine = null; yield break; }
            rodTransform.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (rodTransform != null) rodTransform.localRotation = to;

        if (rod != null) Destroy(rod);
        _pendingDestroyRods.Remove(rod);
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

    void ReelInBobber()
    {
        if (currentBobber != null)
        {
            Destroy(currentBobber);
            currentBobber = null;
            if (lineRenderer != null) lineRenderer.enabled = false;
            Debug.Log("Bobber reeled in.");
        }
    }

    void CastBobber()
    {
        if (bobberPrefab == null || castPoint == null) return;
        if (currentRodInstance == null) return;

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

    void SpawnBobber()
    {
        if (currentBobber != null) return;

        Vector3 camForward = Camera.main.transform.forward;
        Vector3 spawnPos = castPoint.position + camForward * 1.5f;
        Quaternion spawnRot = Quaternion.LookRotation(camForward);
        spawnRot = spawnRot * Quaternion.Euler(bobberRotationOffset);

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

        Bobber bobberScript = currentBobber.GetComponent<Bobber>();
        if (bobberScript != null)
        {
            bobberScript.shootSpeed = bobberShootSpeed;
            // Method reference instead of a per-cast closure allocation.
            bobberScript.OnFishEscaped += ResetSpinCombo;
        }

        if (lineRenderer != null) lineRenderer.enabled = true;

        Debug.Log("Bobber released.");
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
}