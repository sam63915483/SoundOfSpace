using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bar's beers and empty cups. ONE controller on the scene Player backs
/// TWO hotbar items:
///
///   • <c>ItemId.BeerCup</c> ("BEER") — full/partly-drunk beers. Stacks; the
///     slot's count badge is the number you carry. Cup 0 of the list is the
///     one in your hand when you hold a beer.
///   • <c>ItemId.EmptyCup</c> ("CUP") — the empties. Stacks the same way.
///
/// Drink a beer to the bottom and it BECOMES a cup: it leaves the beer list,
/// the empty count goes up, and the cup in your hand is now the empty one
/// (the Hotbar's highlight follows, because the BEER entry stops reporting
/// equipped and the CUP entry starts). Set the empty down on the bar
/// (<see cref="BarCounter"/>) and the empty count goes down.
///
/// Drinking: hold left click, the cup rises to the mouth in CAMERA space (the
/// same anchoring the fish-eating pose uses) and tips; the beer inside
/// (<see cref="BeerLiquid"/>) drains over ~3 s. Each finished beer adds one to
/// <see cref="BeerBuzz"/>.
///
/// Lives on the SCENE Player object next to the other controllers (added
/// components on the prefab instance, not on Player.prefab). Cup mesh =
/// FantasyVillage CupGOOD.prefab; the liquid is built at the interior numbers
/// measured from that mesh (knobs below).
/// </summary>
public class BeerCupController : MonoBehaviour
{
    public enum Held { None, Beer, Empty }

    [Header("UI")]
    [Tooltip("BEER slot icon. Leave empty to use the drawn tankard (BeerCupArt.BuildIcon(true)).")]
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
    [Tooltip("Thirst restored by a whole beer (the water bottle gives 100).")]
    public float thirstPerCup = 30f;

    [Header("Drink Animation (camera space, like eating a fish)")]
    [Tooltip("Where the cup's pivot (its BASE) goes while drinking, in camera space: X right, Y up, Z forward. The base sits low and close so the tipped rim lands at the mouth.")]
    public Vector3 drinkCamPoint = new Vector3(0.03f, -0.20f, 0.30f);
    [Tooltip("Tilt while drinking (degrees). Negative X pitches the base up and the rim back toward your mouth.")]
    public Vector3 drinkTiltEuler = new Vector3(-75f, 0f, -8f);
    [Tooltip("Seconds to raise the cup to the mouth (and to lower it again).")]
    public float drinkRaiseSeconds = 0.3f;
    [Tooltip("Vertical bob (metres) while drinking — the gulp.")]
    public float drinkBobAmount = 0.012f;
    public float drinkBobSpeed = 2.4f;

    [Header("Sound")]
    [SerializeField] AudioClip drinkLoopClip;
    [SerializeField, Range(0, 1)] float drinkVolume = 0.6f;

    // ── state ──────────────────────────────────────────────────────
    readonly List<float> _beers = new List<float>();   // fill 0-100 each; [0] in hand when Held.Beer
    int _empties;
    Held _held = Held.None;
    GameObject _cupInstance;
    BeerLiquid _liquid;
    ViewmodelMotor _motorRig;
    AudioSource _drinkSource;
    Camera _cam;
    float _nextCamRetry;
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
    public Held HeldKind => IsEquipped ? _held : Held.None;
    public bool HoldingBeer  => IsEquipped && _held == Held.Beer;
    public bool HoldingEmpty => IsEquipped && _held == Held.Empty;

    public int BeerCount  => _beers.Count;
    public int EmptyCount => _empties;
    public bool HasBeers   => _beers.Count > 0;
    public bool HasEmpties => _empties > 0;
    /// Fill of the beer in hand (0-100), 0 when not holding a beer.
    public float FillPercent => HoldingBeer && _beers.Count > 0 ? _beers[0] : 0f;

    /// <summary>Fired once each time a beer is drunk to the bottom.</summary>
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

        if (hotbarIcon == null) hotbarIcon = BeerCupArt.BuildIcon(true);
        if (emptyCupIcon == null) emptyCupIcon = BeerCupArt.BuildIcon(false);

        _drinkSource = gameObject.AddComponent<AudioSource>();
        _drinkSource.playOnAwake = false;
        _drinkSource.loop = true;
        _drinkSource.volume = drinkVolume;
    }

    // ── the cups ──────────────────────────────────────────────────

    /// <summary>A beer into the pack (from the bar). Does not change what is in the hand.</summary>
    public void AddBeer(float fillPercent)
    {
        _beers.Add(Mathf.Clamp(fillPercent, 0f, 100f));
    }

    /// <summary>One empty cup leaves the player (set down on the bar). Keeps holding an empty if more remain.</summary>
    public void RemoveEmptyCup()
    {
        if (_empties <= 0) return;
        _empties--;
        if (_empties == 0 && HoldingEmpty) ForceUnequipCup();
    }

    /// <summary>Set the fill of the beer in hand (0-100).</summary>
    public void SetFill(float percent)
    {
        if (_beers.Count == 0) return;
        _beers[0] = Mathf.Clamp(percent, 0f, 100f);
        if (_liquid != null && HoldingBeer) _liquid.SetFill(_beers[0] / 100f);
    }

    /// <summary>All beers, in order (save).</summary>
    public float[] BeerFills => _beers.ToArray();

    /// <summary>Replace everything (load). Null/empty beers = none.</summary>
    public void Restore(float[] beers, int empties, Held held)
    {
        ForceUnequipCup();
        _beers.Clear();
        if (beers != null)
            for (int i = 0; i < beers.Length; i++) _beers.Add(Mathf.Clamp(beers[i], 0f, 100f));
        _empties = Mathf.Max(0, empties);
        if (held == Held.Beer && HasBeers) EquipBeer();
        else if (held == Held.Empty && HasEmpties) EquipEmpty();
    }

    // Registry hooks ------------------------------------------------------

    public void EquipBeer()
    {
        if (!HasBeers) return;
        if (HoldingBeer) return;
        if (IsEquipped) Unequip();
        Equip(Held.Beer);
    }

    public void EquipEmpty()
    {
        if (!HasEmpties) return;
        if (HoldingEmpty) return;
        if (IsEquipped) Unequip();
        Equip(Held.Empty);
    }

    public void ForceUnequipCup() { if (_cupInstance != null) Unequip(); }
    public void ForceUnequipBeer()  { if (HoldingBeer)  Unequip(); }
    public void ForceUnequipEmpty() { if (HoldingEmpty) Unequip(); }

    // ── per frame ─────────────────────────────────────────────────

    void Update()
    {
        if (_ship != null && _ship.IsPiloted) return;
        if (_cupInstance == null) return;

        bool wantDrink = TutorialGate.FireHeld() && !PlayerController.isInDialogue;
        bool drinking = wantDrink && HoldingBeer && _beers.Count > 0 && _beers[0] > 0f;
        DriveDrinkPose(drinking);

        if (drinking)
        {
            float consumed = Mathf.Min(consumeRate * Time.deltaTime, _beers[0]);
            _beers[0] -= consumed;
            if (_liquid != null) _liquid.SetFill(_beers[0] / 100f);
            ResourceManager.Instance?.DrinkWater((consumed / 100f) * thirstPerCup);

            if (_beers[0] <= 0.01f) FinishBeer();
        }
        else if (wantDrink && HoldingEmpty && Time.unscaledTime >= _nextEmptyHint)
        {
            // Left click on an empty cup: say where it goes, don't spam it.
            _nextEmptyHint = Time.unscaledTime + 3f;
            InteractPromptUI.ShowOneShot("Empty cup. Look at the bar and press "
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

    /// The beer in hand is gone: it becomes an empty cup, still in the hand.
    void FinishBeer()
    {
        _beers.RemoveAt(0);
        _empties++;
        _held = Held.Empty;
        if (_liquid != null) _liquid.SetFill(0f);
        _nextEmptyHint = Time.unscaledTime + 1.5f;

        PlayerSuitAudio.Instance?.PlayBurpAfterDelay();
        if (BeerBuzz.Instance != null) BeerBuzz.Instance.AddBeer();
        OnCupEmptied?.Invoke();
    }

    Camera Cam(Transform hold)
    {
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        if (hold != null) { _cam = hold.GetComponentInParent<Camera>(); if (_cam != null) return _cam; }
        if (Time.unscaledTime < _nextCamRetry) return null;
        _nextCamRetry = Time.unscaledTime + 1f;
        _cam = Camera.main;
        return _cam;
    }

    // The cup floats up to the mouth through the motor's additive pose channel,
    // so the carry springs (sway, bob, landing kicks) keep running underneath.
    // The target is expressed in CAMERA space and converted into the hold
    // transform's frame, exactly like HeldItemViewmodel's eating pose — a small
    // fixed offset (the bottle's way) never left the bottom-right corner.
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

        Vector3 target = Vector3.zero;
        Transform hold = _motorRig.transform.parent;
        var cam = Cam(hold);
        if (cam != null && hold != null)
            target = hold.InverseTransformPoint(cam.transform.TransformPoint(drinkCamPoint)) - _motorRig.restOffset;

        _drinkBobPhase += Time.deltaTime * drinkBobSpeed;
        float bob = Mathf.Sin(_drinkBobPhase * Mathf.PI * 2f) * drinkBobAmount * k;

        _motorRig.PoseOffset = target * k + new Vector3(0f, bob, 0f);
        _motorRig.PoseEuler = drinkTiltEuler * k;
    }

    // ── viewmodel ─────────────────────────────────────────────────

    void Equip(Held kind)
    {
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

        _held = kind;

        // Same hold point and the same resting spot as the pistol/bottle — see
        // WaterBottleController.Equip for why the offset is derived, not typed.
        Transform holdPos = ViewmodelMotor.ResolveSharedHoldPoint(gameObject, transform);
        Vector3 rest = ViewmodelMotor.ReferenceRestOffset(gameObject) + motorRestOffset;
        _motorRig = ViewmodelMotor.CreateRig(holdPos, "BeerCupMotorRig", rest);

        _cupInstance = Instantiate(cupPrefab, _motorRig.transform);
        _cupInstance.name = kind == Held.Beer ? "Beer(Held)" : "EmptyCup(Held)";
        _cupInstance.transform.localPosition = Vector3.zero;
        _cupInstance.transform.localRotation = Quaternion.Euler(heldEuler);
        foreach (var p in _cupInstance.GetComponentsInChildren<BeerCupPickup>(true)) Destroy(p);

        // Build the beer BEFORE normalising so the liquid scales with the cup.
        float fill = kind == Held.Beer && _beers.Count > 0 ? _beers[0] / 100f : 0f;
        _liquid = BeerCupArt.AttachLiquid(_cupInstance, liquidAxis, liquidRadius,
                                          liquidFloorY, liquidFullY, foamThickness, fill);

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
        _held = Held.None;
        _drinkBlend = 0f;
        _drinkBobPhase = 0f;
        if (_drinkSource != null && _drinkSource.isPlaying) _drinkSource.Stop();
    }

    void OnDisable()
    {
        // Piloting disables the Player; drop the viewmodel like the others do.
        if (_cupInstance != null) Unequip();
    }

    // (Appended at the END per the serialization convention in CLAUDE.md.)
    [Header("UI (empty cup)")]
    [Tooltip("CUP slot icon. Leave empty to use the drawn empty tankard (BeerCupArt.BuildIcon(false)).")]
    public Sprite emptyCupIcon;
}
