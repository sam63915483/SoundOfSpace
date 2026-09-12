using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bar's beer cups as ONE hotbar equippable that can hold several cups.
/// Cloned from <see cref="WaterBottleController"/> with the water-refill half
/// removed:
///
///   • <see cref="BarCounter"/> spawns a full cup on the bar; its
///     <see cref="BeerCupPickup"/> calls <see cref="AddCup"/>. The Hotbar
///     adds the BEER slot on the next frame (registry-driven) and shows the
///     cup count on it. Cup 0 of the list is the one in your hand; the rest
///     wait in the pack.
///   • Hold left click: the cup floats up to the mouth, tips, and the beer
///     inside (<see cref="BeerLiquid"/>) drains over ~3 s. Thirst gets a little.
///   • Empty: left click just reminds you. Look at the bar and press F — the
///     counter calls <see cref="RemoveCurrentCup"/>, the next full cup (if
///     any) pops into your hand, otherwise the slot goes away.
///
/// Lives on the SCENE Player object next to the other controllers (they are
/// added components on the prefab instance, not on Player.prefab). The cup
/// mesh is FantasyVillage's CupGOOD.prefab; the liquid is built from primitives
/// at the interior numbers measured from that mesh (knobs below).
/// </summary>
public class BeerCupController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Icon for the hotbar slot. Leave empty to use the drawn tankard (BeerCupArt.BuildIcon).")]
    public Sprite hotbarIcon;

    [Header("Cup")]
    [Tooltip("The empty cup mesh (FantasyVillage CupGOOD.prefab). The beer inside is built by code.")]
    public GameObject cupPrefab;
    [Tooltip("Held size in metres (longest edge), like the bottle's 0.22.")]
    public float cupHeldSize = 0.20f;
    [Tooltip("Rest orientation in the hand (degrees). Default turns the handle toward your right hand.")]
    public Vector3 heldEuler = new Vector3(0f, 180f, 0f);
    [Tooltip("NUDGE on top of the pistol's resting spot, like the bottle's. Leave zero unless you want it offset.")]
    public Vector3 motorRestOffset = Vector3.zero;

    [Header("Beer inside the cup (cup-local, at prefab scale 1)")]
    [Tooltip("Centre of the cup's bore at the floor. Measured: the handle sits at -X, the bore is ~5 mm off centre.")]
    public Vector3 liquidAxis = new Vector3(0.005f, 0f, -0.001f);
    [Tooltip("Radius of the beer column. The inner wall is ~0.063-0.08.")]
    public float liquidRadius = 0.07f;
    [Tooltip("Cup-local Y of the interior floor.")]
    public float liquidFloorY = 0.035f;
    [Tooltip("Cup-local Y of the surface when full (the rim is at 0.239).")]
    public float liquidFullY = 0.19f;
    [Tooltip("Foam head thickness (metres).")]
    public float foamThickness = 0.02f;

    [Header("Drinking")]
    [Tooltip("Percent of the cup drunk per second while left click is held. 35 = a full cup in ~3 s.")]
    public float consumeRate = 35f;
    [Tooltip("Thirst restored by a whole cup (the water bottle gives 100).")]
    public float thirstPerCup = 30f;

    [Header("Drink Animation (camera-space, like the bottle)")]
    [Tooltip("Shift toward the mouth while drinking. X = right, Y = up, Z = away (negative pulls it in).")]
    public Vector3 drinkRaiseOffset = new Vector3(-0.05f, 0.10f, -0.12f);
    [Tooltip("Tilt while drinking (degrees). Negative X pitches the cup's base up so it pours into your mouth.")]
    public Vector3 drinkTiltEuler = new Vector3(-60f, 0f, -10f);
    public float drinkRaiseSeconds = 0.3f;
    [Tooltip("Vertical bob (metres) while drinking — the gulp.")]
    public float drinkBobAmount = 0.012f;
    public float drinkBobSpeed = 2.4f;

    [Header("Sound")]
    [SerializeField] AudioClip drinkLoopClip;
    [SerializeField, Range(0, 1)] float drinkVolume = 0.6f;

    // ── state ──────────────────────────────────────────────────────
    readonly List<float> _cups = new List<float>();   // fill 0-100 each; [0] is in hand
    GameObject _cupInstance;
    BeerLiquid _liquid;
    ViewmodelMotor _motorRig;
    AudioSource _drinkSource;
    float _drinkBlend, _drinkBobPhase;
    float _nextEmptyHint;

    // ── references ────────────────────────────────────────────────
    FishingRodController _rod;
    GuitarController _guitar;
    AxeController _axe;
    PistolController _pistol;
    GrappleGunController _grapple;
    WaterBottleController _bottle;
    PlayerPickup _playerPickup;
    Ship _ship;

    public bool IsEquipped => _cupInstance != null;
    /// True while the player owns at least one cup. The Hotbar shows the slot
    /// while this is true and evicts it when it goes false.
    public bool IsUnlocked => _cups.Count > 0;
    public int CupCount => _cups.Count;
    /// Fill of the cup in hand (0-100), 0 when there is none.
    public float FillPercent => _cups.Count > 0 ? _cups[0] : 0f;
    public bool IsEmpty => _cups.Count > 0 && _cups[0] <= 0.01f;

    /// <summary>Fired once each time a cup is drunk to the bottom.</summary>
    public static event System.Action OnCupEmptied;

    void Start()
    {
        _rod          = GetComponent<FishingRodController>();
        _guitar       = GetComponent<GuitarController>();
        _axe          = GetComponent<AxeController>();
        _pistol       = GetComponent<PistolController>();
        _grapple      = GetComponent<GrappleGunController>();
        _bottle       = GetComponent<WaterBottleController>();
        _playerPickup = GetComponent<PlayerPickup>();
        _ship         = FindObjectOfType<Ship>();

        if (hotbarIcon == null) hotbarIcon = BeerCupArt.BuildIcon();

        _drinkSource = gameObject.AddComponent<AudioSource>();
        _drinkSource.playOnAwake = false;
        _drinkSource.loop = true;
        _drinkSource.volume = drinkVolume;
    }

    // ── the cups ──────────────────────────────────────────────────

    /// <summary>Another cup into the pack (from the bar). Becomes the one in hand if there was none.</summary>
    public void AddCup(float fillPercent)
    {
        _cups.Add(Mathf.Clamp(fillPercent, 0f, 100f));
    }

    /// <summary>
    /// The cup in hand leaves the player (set down on the bar). The next cup, if
    /// any, takes its place in the hand; with none left the Hotbar evicts the slot.
    /// </summary>
    public void RemoveCurrentCup()
    {
        if (_cups.Count == 0) return;
        bool wasEquipped = IsEquipped;
        _cups.RemoveAt(0);
        ForceUnequipCup();
        if (wasEquipped && _cups.Count > 0) Equip();
    }

    /// <summary>Set the fill of the cup in hand (0-100).</summary>
    public void SetFill(float percent)
    {
        if (_cups.Count == 0) return;
        _cups[0] = Mathf.Clamp(percent, 0f, 100f);
        if (_liquid != null) _liquid.SetFill(_cups[0] / 100f);
    }

    /// <summary>All cups, in order (save).</summary>
    public float[] CupFills => _cups.ToArray();

    /// <summary>Replace every cup (load). Null/empty = no cups.</summary>
    public void RestoreCups(float[] fills)
    {
        ForceUnequipCup();
        _cups.Clear();
        if (fills != null)
            for (int i = 0; i < fills.Length; i++) _cups.Add(Mathf.Clamp(fills[i], 0f, 100f));
    }

    public void ForceEquipCup()   { if (_cupInstance == null && _cups.Count > 0) Equip(); }
    public void ForceUnequipCup() { if (_cupInstance != null) Unequip(); }

    // ── per frame ─────────────────────────────────────────────────

    void Update()
    {
        if (_ship != null && _ship.IsPiloted) return;
        if (_cupInstance == null || _cups.Count == 0) return;

        bool wantDrink = TutorialGate.FireHeld() && !PlayerController.isInDialogue;
        bool drinking = wantDrink && _cups[0] > 0f;
        DriveDrinkPose(drinking);

        if (drinking)
        {
            float consumed = Mathf.Min(consumeRate * Time.deltaTime, _cups[0]);
            _cups[0] -= consumed;
            if (_liquid != null) _liquid.SetFill(_cups[0] / 100f);
            ResourceManager.Instance?.DrinkWater((consumed / 100f) * thirstPerCup);

            if (_cups[0] <= 0.01f)
            {
                _cups[0] = 0f;
                PlayerSuitAudio.Instance?.PlayBurpAfterDelay();
                OnCupEmptied?.Invoke();
                _nextEmptyHint = 0f;
            }
        }
        else if (wantDrink && IsEmpty && Time.unscaledTime >= _nextEmptyHint)
        {
            // Left click on an empty cup: say where it goes, don't spam it.
            _nextEmptyHint = Time.unscaledTime + 3f;
            InteractPromptUI.ShowOneShot("Empty. Look at the bar and press "
                                         + PromptGlyphs.Interact + " to set it down", 2.5f, false);
        }

        if (_drinkSource != null)
        {
            if (drinking && drinkLoopClip != null)
            {
                if (!_drinkSource.isPlaying)
                {
                    _drinkSource.clip = drinkLoopClip;
                    _drinkSource.volume = drinkVolume;
                    _drinkSource.Play();
                }
            }
            else if (_drinkSource.isPlaying) _drinkSource.Stop();
        }
    }

    // Same shape as the bottle: drive the motor's additive pose channel so the
    // carry springs keep running underneath and the cup floats up to the mouth.
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

        float k = _drinkBlend * _drinkBlend * (3f - 2f * _drinkBlend);
        _drinkBobPhase += Time.deltaTime * drinkBobSpeed;
        float bob = Mathf.Sin(_drinkBobPhase * Mathf.PI * 2f) * drinkBobAmount * k;

        _motorRig.PoseOffset = drinkRaiseOffset * k + new Vector3(0f, bob, 0f);
        _motorRig.PoseEuler = drinkTiltEuler * k;
    }

    // ── viewmodel ─────────────────────────────────────────────────

    void Equip()
    {
        if (_cups.Count == 0) return;
        if (_rod     != null && _rod.IsEquipped)     return;
        if (_guitar  != null && _guitar.IsEquipped)  return;
        if (_axe     != null && _axe.IsEquipped)     return;
        if (_pistol  != null && _pistol.IsEquipped)  return;
        if (_grapple != null && _grapple.IsEquipped) return;
        if (_bottle  != null && _bottle.IsEquipped)  return;
        if (_playerPickup != null && _playerPickup.IsHoldingObject) return;
        if (cupPrefab == null)
        {
            Debug.LogWarning("[BeerCupController] no cupPrefab assigned (FantasyVillage CupGOOD.prefab).", this);
            return;
        }

        // Same hold point and the same resting spot as the pistol/bottle — see
        // WaterBottleController.Equip for why the offset is derived, not typed.
        Transform holdPos = ViewmodelMotor.ResolveSharedHoldPoint(gameObject, transform);
        Vector3 rest = ViewmodelMotor.ReferenceRestOffset(gameObject) + motorRestOffset;
        _motorRig = ViewmodelMotor.CreateRig(holdPos, "BeerCupMotorRig", rest);

        _cupInstance = Instantiate(cupPrefab, _motorRig.transform);
        _cupInstance.name = "BeerCup(Held)";
        _cupInstance.transform.localPosition = Vector3.zero;
        _cupInstance.transform.localRotation = Quaternion.Euler(heldEuler);
        // Any pickup script that rode along on a world cup must not run in the hand.
        foreach (var p in _cupInstance.GetComponentsInChildren<BeerCupPickup>(true)) Destroy(p);

        // Build the beer BEFORE normalising so the liquid scales with the cup.
        _liquid = BeerCupArt.AttachLiquid(_cupInstance, liquidAxis, liquidRadius,
                                          liquidFloorY, liquidFullY, foamThickness, _cups[0] / 100f);

        ViewmodelMotor.MakeViewmodel(_cupInstance);
        ViewmodelMotor.NormalizeSize(_cupInstance, cupHeldSize);
    }

    void Unequip()
    {
        if (_cupInstance == null) return;
        // Destroying the rig takes the cup with it and leaves no orphan motor.
        if (_motorRig != null) Destroy(_motorRig.gameObject);
        else Destroy(_cupInstance);
        _motorRig = null;
        _cupInstance = null;
        _liquid = null;
        _drinkBlend = 0f;
        _drinkBobPhase = 0f;
        if (_drinkSource != null && _drinkSource.isPlaying) _drinkSource.Stop();
    }

    void OnDisable()
    {
        // Piloting disables the Player; drop the viewmodel like the others do.
        if (_cupInstance != null) Unequip();
    }
}
