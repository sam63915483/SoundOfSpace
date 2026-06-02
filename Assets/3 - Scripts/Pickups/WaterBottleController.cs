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
        if (drinking)
        {
            float consumed = Mathf.Min(consumeRate * Time.deltaTime, fillPercent);
            fillPercent -= consumed;
            ResourceManager.Instance?.DrinkWater((consumed / 100f) * drinkAmount);
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

    void LateUpdate()
    {
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

        Transform holdPos = bottleHoldPosition != null ? bottleHoldPosition : transform;
        currentBottleInstance = Instantiate(waterBottlePrefab, holdPos);

        foreach (var rb  in currentBottleInstance.GetComponentsInChildren<Rigidbody>())  Object.Destroy(rb);
        foreach (var col in currentBottleInstance.GetComponentsInChildren<Collider>())   Object.Destroy(col);
    }

    void Unequip()
    {
        if (currentBottleInstance == null) return;

        Destroy(currentBottleInstance);
        currentBottleInstance = null;
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
}
