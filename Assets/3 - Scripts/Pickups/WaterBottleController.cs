using UnityEngine;
using System.Collections;
using TMPro;

public class WaterBottleController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Icon shown in the hotbar slot when this item is in the bar. Assign on the Player prefab.")]
    public Sprite hotbarIcon;

    [Header("Bottle Prefab")]
    public GameObject waterBottlePrefab;
    public Transform bottleHoldPosition;

    [Header("Arm Animation")]
    public float armAnimSpeed = 5f;
    [Tooltip("Tilt the arm target down (negative) or up (positive) from player forward.")]
    public float armPitch = 0f;
    [Tooltip("Swing the arm target left (negative) or right (positive) from player forward.")]
    public float armYaw = 0f;
    [Tooltip("Roll/twist the upper arm bone around its own shaft axis.")]
    public float armRoll = 0f;
    [Tooltip("Extra bend applied to the forearm while the arm is raised.")]
    public Vector3 forearmRotationOffset = Vector3.zero;

    [Header("UI")]
    public GameObject fillUI;
    public TextMeshProUGUI fillPercentText;

    [Header("Settings")]
    public float fillRate   = 20f;
    public float drinkAmount = 100f;
    public float consumeRate = 15f;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip drinkLoopClip;
    [SerializeField, Range(0, 1)] private float drinkVolume = 0.6f;
    private AudioSource drinkSource;

    const string kUpperArmBone = "Arm Upper.R";
    const string kForearmBone  = "Arm Lower.R";

    // ── state ──────────────────────────────────────────────────────
    bool  isInWater;
    float fillPercent;
    bool  thirstBlocked;
    GameObject currentBottleInstance;
    ViewmodelMotor _motorRig;   // floaty carry layer between the hold transform and the bottle

    // ── arm bones ──────────────────────────────────────────────────
    Transform  _upperArmR;
    Transform  _lowerArmR;
    Quaternion _upperArmRRest;
    Quaternion _lowerArmRRest;
    float      _armBlend;
    bool       _armReady;

    // ── references ────────────────────────────────────────────────
    FishingRodController fishingRodController;
    GuitarController     guitarController;
    AxeController        axeController;
    PistolController     pistolController;
    PlayerPickup         playerPickup;
    Ship                 ship;

    public bool IsEquipped => currentBottleInstance != null;

    // True once the player has picked up a water bottle from the world (via
    // WaterBottlePickup). Hotbar gates the bottle slot on this so the player
    // doesn't start the game with a free bottle. Persists via EquipmentSave.
    public bool IsUnlocked { get; private set; }

    /// <summary>Fired the first time the bottle is picked up from the world (IsUnlocked → true).</summary>
    public static event System.Action OnBottlePickedUp;
    /// <summary>Fired once when the bottle is first filled past a usable threshold.</summary>
    public static event System.Action OnBottleFilled;
    bool _filledFired;

    public void Unlock()
    {
        if (IsUnlocked) return;          // guard so a save-load restore doesn't re-fire the event
        IsUnlocked = true;
        OnBottlePickedUp?.Invoke();
    }

    // Public read-only view of the bottle's fill state (0-100). Tutorial
    // steps poll this to detect first refill / first drink.
    public float FillPercent => fillPercent;

    // ──────────────────────────────────────────────────────────────
    void Start()
    {
        fishingRodController = GetComponent<FishingRodController>();
        guitarController     = GetComponent<GuitarController>();
        axeController        = GetComponent<AxeController>();
        pistolController     = GetComponent<PistolController>();
        playerPickup         = GetComponent<PlayerPickup>();
        ship                 = FindObjectOfType<Ship>();

        if (fillUI != null) fillUI.SetActive(false);

        drinkSource = gameObject.AddComponent<AudioSource>();
        drinkSource.playOnAwake = false;
        drinkSource.loop = true;
        drinkSource.volume = drinkVolume;

        StartCoroutine(InitArmBones());
    }

    IEnumerator InitArmBones()
    {
        yield return new WaitForEndOfFrame();

        _upperArmR = FindDeepChild(transform, kUpperArmBone);
        _lowerArmR = FindDeepChild(transform, kForearmBone);

        if (_upperArmR == null || _lowerArmR == null)
        {
            string found = string.Join(", ", System.Array.ConvertAll(
                GetComponentsInChildren<Transform>(true), t => t.name));
            Debug.LogError($"[WaterBottleController] Could not find arm bones. Children: {found}");
            yield break;
        }

        yield return null;
        _upperArmRRest = _upperArmR.localRotation;
        _lowerArmRRest = _lowerArmR.localRotation;

        _armReady = true;
    }

    // ──────────────────────────────────────────────────────────────
    void Update()
    {
        if (ship != null && ship.IsPiloted) return;

        if (currentBottleInstance == null) return;

        // RMB or LT (controller) to fill while standing in water.
        if (isInWater && TutorialGate.SecondaryFireHeld())
        {
            fillPercent = Mathf.Clamp(fillPercent + fillRate * Time.deltaTime, 0f, 100f);
            if (!_filledFired && fillPercent >= 5f) { _filledFired = true; OnBottleFilled?.Invoke(); }
        }

        ShowFillUI(fillPercent > 0f);

        if (ResourceManager.Instance != null)
        {
            float thirst = ResourceManager.Instance.ThirstPercent;
            if (thirst >= 0.99f)
                thirstBlocked = true;
            else if (thirstBlocked && thirst <= 0.94f)
                thirstBlocked = false;
        }

        // LMB held or right-trigger held (controller).
        bool drinking = TutorialGate.FireHeld() && fillPercent > 0f && !thirstBlocked;
        DriveDrinkPose(drinking);
        if (drinking)
        {
            float consumed = Mathf.Min(consumeRate * Time.deltaTime, fillPercent);
            fillPercent -= consumed;
            ResourceManager.Instance?.DrinkWater((consumed / 100f) * drinkAmount);
            // Orientation board line 2. Fires on every frame of drinking; the
            // objective is idempotent so only the first one does anything.
            OrientationObjectives.Complete(OrientationObjectives.Objective.DrinkWater);
        }

        if (drinkSource != null)
        {
            if (drinking && drinkLoopClip != null)
            {
                if (!drinkSource.isPlaying)
                {
                    drinkSource.clip = drinkLoopClip;
                    drinkSource.volume = drinkVolume;
                    drinkSource.Play();
                }
            }
            else if (drinkSource.isPlaying)
            {
                drinkSource.Stop();
            }
        }
    }

    // Raise the bottle to the player's mouth while drinking and let it bob, then
    // lower it back. Driven through the motor's additive pose channel so the
    // carry springs (sway, bob, landing kicks) keep running underneath — the
    // bottle floats up rather than snapping to a fixed spot.
    void DriveDrinkPose(bool drinking)
    {
        if (_motorRig == null) return;

        _drinkBlend = Mathf.MoveTowards(_drinkBlend, drinking ? 1f : 0f,
                                        Time.deltaTime / Mathf.Max(0.01f, drinkRaiseSeconds));
        if (_drinkBlend <= 0.0001f)
        {
            _motorRig.PoseOffset = Vector3.zero;
            _motorRig.PoseEuler = Vector3.zero;
            _drinkBobPhase = 0f;
            return;
        }

        // Ease so the lift starts and settles softly instead of ramping linearly.
        float k = _drinkBlend * _drinkBlend * (3f - 2f * _drinkBlend);

        // Tip-and-bob only once the bottle is actually up at the mouth, so it
        // doesn't wobble on the way there.
        _drinkBobPhase += Time.deltaTime * drinkBobSpeed;
        float bob = Mathf.Sin(_drinkBobPhase * Mathf.PI * 2f) * drinkBobAmount * k;

        _motorRig.PoseOffset = drinkRaiseOffset * k + new Vector3(0f, bob, 0f);
        _motorRig.PoseEuler = drinkTiltEuler * k;
    }

    void LateUpdate()
    {
        // The bottle now floats on a ViewmodelMotor like every other equippable
        // instead of being carried by an animated right arm. The arm rig stays in
        // the file behind this flag, but it's off by default — it read worse than
        // the floating items, and CLAUDE.md already records that reaching the arm
        // at held items was an experiment that got ripped out once before.
        if (!useArmAnimation) return;
        if (!_armReady || _upperArmR == null || _lowerArmR == null) return;

        float target = currentBottleInstance != null ? 1f : 0f;
        _armBlend = Mathf.MoveTowards(_armBlend, target, armAnimSpeed * Time.deltaTime);

        _upperArmR.localRotation = _upperArmRRest;
        _lowerArmR.localRotation = _lowerArmRRest;

        if (_armBlend <= 0.001f) return;

        Vector3 targetDir = Quaternion.Euler(-armPitch, armYaw, 0f) * transform.forward;
        Vector3 shaft     = (_lowerArmR.position - _upperArmR.position).normalized;
        Vector3 raiseAxis = Vector3.Cross(shaft, targetDir).normalized;
        float   angle     = Vector3.Angle(shaft, targetDir);

        Quaternion worldRest   = _upperArmR.parent.rotation * _upperArmRRest;
        Quaternion worldTarget = Quaternion.AngleAxis(angle * _armBlend, raiseAxis) * worldRest;

        if (armRoll != 0f)
            worldTarget = worldTarget * Quaternion.AngleAxis(armRoll * _armBlend, shaft);

        _upperArmR.localRotation = Quaternion.Inverse(_upperArmR.parent.rotation) * worldTarget;

        if (forearmRotationOffset != Vector3.zero)
            _lowerArmR.localRotation = _lowerArmRRest * Quaternion.Euler(forearmRotationOffset * _armBlend);
    }

    // ──────────────────────────────────────────────────────────────
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Water")) isInWater = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Water"))
        {
            isInWater = false;
            ShowFillUI(false);
        }
    }

    // ──────────────────────────────────────────────────────────────
    void Equip()
    {
        if (fishingRodController != null && fishingRodController.IsEquipped) return;
        if (guitarController     != null && guitarController.IsEquipped)     return;
        if (axeController        != null && axeController.IsEquipped)        return;
        if (pistolController     != null && pistolController.IsEquipped)     return;
        if (playerPickup         != null && playerPickup.IsHoldingObject)    return;
        if (waterBottlePrefab    == null)                                    return;

        // bottleHoldPosition is wired to the HAND bone (a leftover from the arm
        // rig), which parked the bottle down by the player's side. Use the
        // shared camera-child hold point the pistol/axe/rod use instead; the old
        // field stays as the fallback.
        Transform holdPos = ViewmodelMotor.ResolveSharedHoldPoint(
            gameObject, bottleHoldPosition != null ? bottleHoldPosition : transform);

        // ...and sit at the SAME resting spot the pistol does. The hold
        // transform's own origin is up near the camera, which is why a small
        // hand-picked offset put the bottle in the player's face — the pistol
        // only reaches the bottom right by stacking its motor rest offset onto
        // its tuned holdPositionOffset. Deriving from that means the bottle
        // lands where the gun lands, and bottleMotorRestOffset is now just a
        // nudge on top rather than the whole placement.
        Vector3 rest = ViewmodelMotor.ReferenceRestOffset(gameObject) + bottleMotorRestOffset;
        _motorRig = ViewmodelMotor.CreateRig(holdPos, "BottleMotorRig", rest);
        currentBottleInstance = Instantiate(waterBottlePrefab, _motorRig.transform);
        currentBottleInstance.transform.localPosition = Vector3.zero;
        currentBottleInstance.transform.localRotation = Quaternion.identity;

        foreach (var rb  in currentBottleInstance.GetComponentsInChildren<Rigidbody>())  Object.Destroy(rb);
        foreach (var col in currentBottleInstance.GetComponentsInChildren<Collider>())   Object.Destroy(col);
        // Held viewmodels never cast shadows.
        foreach (var r in currentBottleInstance.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // The prefab is a world-sized bottle; held 30cm from the eye that fills
        // the screen. Normalise it like the held resources and fish are.
        ViewmodelMotor.NormalizeSize(currentBottleInstance, bottleWorldSize);
    }

    void Unequip()
    {
        if (currentBottleInstance == null) return;

        // Destroying the rig takes the bottle with it (it's a child) and leaves
        // no orphan ViewmodelMotor under the hold transform.
        if (_motorRig != null) Destroy(_motorRig.gameObject);
        else Destroy(currentBottleInstance);
        _motorRig = null;
        currentBottleInstance = null;
        _drinkBlend = 0f;
        _drinkBobPhase = 0f;
        ShowFillUI(false);
        if (drinkSource != null && drinkSource.isPlaying) drinkSource.Stop();
    }

    public void ForceUnequipBottle()
    {
        if (currentBottleInstance != null) Unequip();
    }

    public void ForceEquipBottle()
    {
        if (currentBottleInstance == null) Equip();
    }

    void ShowFillUI(bool show)
    {
        // The legacy scene-bound `fillUI` (WaterFillUI GameObject in 1.6.7.7.7)
        // is superseded by the procedural WaterFillHUD singleton which polls
        // FillPercent directly. Keep this method as a no-op so existing call
        // sites (Update / OnDisable) don't need touching, and so the legacy
        // GameObject — which was anchored behind the hotbar — stays inactive.
        if (fillUI != null && fillUI.activeSelf) fillUI.SetActive(false);
    }

    static Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    // (Appended at the END per the serialization convention in CLAUDE.md.)
    [Header("Floaty Carry (ViewmodelMotor)")]
    [Tooltip("NUDGE on top of the pistol's resting spot, not the whole placement — the bottle derives its base position from wherever the gun sits, so leave this at zero unless you want it offset from that. X = right, Y = up, Z = away from you.")]
    public Vector3 bottleMotorRestOffset = Vector3.zero;
    [Tooltip("Held size in metres (longest edge). The prefab is a world-sized bottle, which fills the screen at arm's length — this scales it down to match the other held items. 0 = leave the prefab's own scale alone.")]
    public float bottleWorldSize = 0.22f;
    [Tooltip("Legacy: animate the right arm bones to 'hold' the bottle instead of floating it. OFF — the floating viewmodel reads better, and CLAUDE.md records arm-reaching as an experiment already ripped out once. Kept only so the rig can be re-enabled for comparison.")]
    public bool useArmAnimation = false;

    [Header("Drink Animation")]
    [Tooltip("Camera-space shift while drinking — the bottle moves toward the player's mouth. X = right, Y = up, Z = away from you (negative pulls it in).")]
    public Vector3 drinkRaiseOffset = new Vector3(-0.055f, 0.085f, -0.10f);
    [Tooltip("Tilt applied while drinking (degrees). Negative X pitches the bottle's base up so it pours toward the mouth.")]
    public Vector3 drinkTiltEuler = new Vector3(-42f, 0f, -12f);
    [Tooltip("Seconds to raise the bottle to the mouth (and to lower it again).")]
    public float drinkRaiseSeconds = 0.28f;
    [Tooltip("Vertical bob amplitude (metres) while drinking — the gulp motion.")]
    public float drinkBobAmount = 0.014f;
    [Tooltip("Bob cycles per second while drinking.")]
    public float drinkBobSpeed = 2.6f;

    float _drinkBlend, _drinkBobPhase;
}
