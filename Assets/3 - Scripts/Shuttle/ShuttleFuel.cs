using UnityEngine;

/// <summary>
/// The shuttle's reactor tank — the thing that turns "fly wherever you like"
/// into "know before you go".
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 2.)
///
/// <b>The rule, in one line:</b> a full tank buys exactly one 15 km jump and
/// leaves you empty on arrival.
///
/// Billing is per kilometre of the gap at the moment TRAVEL is pressed, plus a
/// flat launch-and-land charge that every hop pays however short it is. It is
/// charged ONCE, up front — the planets keep moving during the flight and the
/// shuttle is never re-billed, and there is no such thing as running dry
/// mid-transit. If you cannot afford a hop, the NAV app greys it out; a jump
/// that starts always finishes.
///
/// Same-planet relocation (picking the planet you are already on, to land
/// somewhere else) pays the flat charge only — you launched and landed, you
/// just did not cross anything.
///
/// The tank is world state: it belongs to the shuttle, is saved with it, and in
/// co-op it is shared — either player can feed it crystals through the reactor
/// prop, and the host owns the number.
/// </summary>
[DisallowMultipleComponent]
public class ShuttleFuel : MonoBehaviour, IReactorFuel
{
    public static ShuttleFuel Instance { get; private set; }

    [Header("Tank")]
    [Tooltip("Tank size in fuel units. One crystal is worth 5 units through the reactor " +
             "(ShipReactor.fuelPerCrystal), so a 100-unit tank is 20 crystals — the same " +
             "conversion the manual ship uses.")]
    public float fuelMax = 100f;

    [Header("Jump cost")]
    [Tooltip("The longest hop a FULL tank can pay for, in km. This is the headline number " +
             "of the whole travel design: everything further away has to wait for its orbit " +
             "to swing closer. See docs/DISTANCE_TABLE.md for what this range actually reaches.")]
    public float maxJumpKm = 15f;

    [Tooltip("Flat cost of getting off the ground and back down again, paid by every hop " +
             "no matter how short — including a same-planet relocation.")]
    public float launchLandCost = 5f;

    [Header("Start of a new game")]
    [Tooltip("Fuel the shuttle is carrying when a new game begins, before the intro " +
             "approach burns some off. Half a tank: enough to teach the gauge, not " +
             "enough to go anywhere interesting.")]
    public float newGameFuel = 50f;

    [Tooltip("Units burned by the scripted intro approach, spread across the descent so " +
             "the player watches the gauge fall before anyone explains what it is.")]
    public float introApproachBurn = 12f;

    // Current contents. Not serialized: the save owns it (SaveCollector), and a
    // scene-authored value would silently win over a loaded game.
    float _fuel;
    bool  _seeded;

    // ── State ────────────────────────────────────────────────────────────────

    public float Fuel => _fuel;
    public float FuelMax => fuelMax;
    public float FuelPercent => fuelMax > 0f ? Mathf.Clamp01(_fuel / fuelMax) : 0f;

    /// <summary>Units burned per km travelled. Derived, never authored: it is
    /// whatever makes a full tank equal exactly one <see cref="maxJumpKm"/> hop
    /// once the flat launch charge is paid.</summary>
    public float UnitsPerKm =>
        maxJumpKm > 0.01f ? Mathf.Max(0f, fuelMax - launchLandCost) / maxJumpKm : 0f;

    /// <summary>How far the shuttle could jump right now, in km. This is the honest
    /// number the NAV app shows — it already accounts for the launch charge, so it
    /// reads slightly under 15 km even on a full tank's worth minus a sip.</summary>
    public float RangeKm
    {
        get
        {
            float upk = UnitsPerKm;
            if (upk <= 0.0001f) return 0f;
            return Mathf.Max(0f, (_fuel - launchLandCost) / upk);
        }
    }

    /// <summary>Cost in fuel units of a hop across <paramref name="metres"/>.</summary>
    public float CostForMetres(float metres) =>
        launchLandCost + Mathf.Max(0f, metres) / 1000f * UnitsPerKm;

    public bool CanAfford(float metres) => _fuel >= CostForMetres(metres) - 0.0001f;

    /// <summary>Charge for a hop. Returns false and charges nothing if the tank is
    /// short — callers must treat that as "the jump does not happen".</summary>
    public bool TryCharge(float metres)
    {
        float cost = CostForMetres(metres);
        if (_fuel < cost - 0.0001f) return false;
        _fuel = Mathf.Clamp(_fuel - cost, 0f, fuelMax);
        Debug.Log($"[ShuttleFuel] charged {cost:F1} for {metres / 1000f:F2} km — {_fuel:F1}/{fuelMax:F0} left " +
                  $"(range now {RangeKm:F1} km)");
        return true;
    }

    // ── Filling it ───────────────────────────────────────────────────────────

    public void RestoreFuel(float amount)
    {
        if (amount <= 0f) return;
        _fuel = Mathf.Clamp(_fuel + amount, 0f, fuelMax);
    }

    /// <summary>Burn without a jump — the intro approach drains the gauge this way.</summary>
    public void Drain(float amount)
    {
        if (amount <= 0f) return;
        _fuel = Mathf.Clamp(_fuel - amount, 0f, fuelMax);
    }

    /// <summary>Set the tank directly. Used by the save system and by New Game;
    /// both need to overwrite whatever the scene or a previous run left here.</summary>
    public void SetFuel(float amount)
    {
        _fuel   = Mathf.Clamp(amount, 0f, fuelMax);
        _seeded = true;
    }

    /// <summary>New Game: half a tank. Called by NewGameReset, and as a fallback by
    /// Awake so a scene entered without any save still has something in it.</summary>
    public void ResetForNewGame() => SetFuel(newGameFuel);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (!_seeded) SetFuel(newGameFuel);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Find the tank, attaching one to the shuttle if this is a scene that
    /// predates the feature. Mirrors ShuttleAutopilot.EnsureAttached so an old
    /// scene or an old save never lands without a tank.</summary>
    public static ShuttleFuel EnsureAttached()
    {
        if (Instance != null) return Instance;
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null) return null;
        var f = pilot.GetComponent<ShuttleFuel>();
        if (f == null) f = pilot.gameObject.AddComponent<ShuttleFuel>();
        return f;
    }
}
