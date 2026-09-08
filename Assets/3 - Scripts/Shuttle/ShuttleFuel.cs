using UnityEngine;

/// <summary>
/// The shuttle's reactor tank — the thing that turns "fly wherever you like"
/// into "know before you go".
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 2.)
///
/// <b>The rule, in one line:</b> a full tank buys exactly one 15 km jump and
/// leaves you empty on arrival.
///
/// Billing is a flat launch-and-land charge that every hop pays however short
/// it is, plus a distance charge that rises FASTER than the distance does (see
/// <see cref="jumpCostExponent"/>) — so a quick skip to a neighbour is cheap
/// and crossing the system is frightening, while the headline "one full tank =
/// one 15 km jump" stays exactly true. The price is read from the gap at the
/// moment TRAVEL is pressed. It is
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
             "to swing closer. See docs/DISTANCE_TABLE.md for what this range actually reaches. " +
             "The Shuttle_Lander prefab carries the live value — change it there too.")]
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

    // ── Appended 2026-09-08 (CLAUDE.md: new serialized fields go at the END) ──
    [Tooltip("How sharply the distance charge climbs. 1 = a straight line (cost is simply " +
             "proportional to km). Above 1, short hops get cheaper and long ones stay " +
             "expensive, WITHOUT changing how far a full tank reaches — the curve is pinned " +
             "at both ends. At 1.6 a 5 km hop is 21% of a tank instead of 37%, and a 2 km " +
             "skip to a neighbour is 9% instead of 18%, so riding the orbits until a planet " +
             "swings close is rewarded rather than merely permitted.")]
    [Range(1f, 3f)] public float jumpCostExponent = 1.6f;

    // Current contents. Not serialized: the save owns it (SaveCollector), and a
    // scene-authored value would silently win over a loaded game.
    float _fuel;
    bool  _seeded;

    // ── State ────────────────────────────────────────────────────────────────

    public float Fuel => _fuel;
    public float FuelMax => fuelMax;
    public float FuelPercent => fuelMax > 0f ? Mathf.Clamp01(_fuel / fuelMax) : 0f;

    /// <summary>Everything the distance charge has to spend across the whole
    /// range: a full tank minus the flat launch charge. Derived, never authored,
    /// so a full tank is always exactly one <see cref="maxJumpKm"/> hop.</summary>
    float DistanceBudget => Mathf.Max(0f, fuelMax - launchLandCost);

    /// <summary>Average units burned per km over a FULL-range jump. Display and
    /// back-compat only — the real charge is <see cref="CostForMetres"/>, which
    /// is a curve, not a straight line, whenever
    /// <see cref="jumpCostExponent"/> is above 1.</summary>
    public float UnitsPerKm => maxJumpKm > 0.01f ? DistanceBudget / maxJumpKm : 0f;

    float Exponent => Mathf.Max(0.01f, jumpCostExponent);

    /// <summary>Cost in fuel units of a hop across <paramref name="metres"/>:
    /// the flat launch charge plus the distance charge, which is the budget
    /// scaled by (distance / range) raised to <see cref="jumpCostExponent"/>.
    /// At exactly <see cref="maxJumpKm"/> this returns <see cref="fuelMax"/>,
    /// whatever the exponent is — the curve is pinned at both ends.</summary>
    public float CostForMetres(float metres)
    {
        if (maxJumpKm <= 0.01f) return launchLandCost;
        float frac = Mathf.Max(0f, metres) / 1000f / maxJumpKm;
        return launchLandCost + DistanceBudget * Mathf.Pow(frac, Exponent);
    }

    /// <summary>How far the shuttle could jump right now, in km — the exact
    /// inverse of <see cref="CostForMetres"/>, so the number the NAV app shows
    /// is the number the GO button will honour. It already accounts for the
    /// launch charge, so it reads slightly under <see cref="maxJumpKm"/> even on
    /// a full tank's worth minus a sip.</summary>
    public float RangeKm
    {
        get
        {
            float budget = DistanceBudget;
            if (budget <= 0.0001f || maxJumpKm <= 0.01f) return 0f;
            float spendable = Mathf.Max(0f, _fuel - launchLandCost);
            return maxJumpKm * Mathf.Pow(Mathf.Clamp01(spendable / budget), 1f / Exponent);
        }
    }

    public bool CanAfford(float metres) => _fuel >= CostForMetres(metres) - 0.0001f;

    // ── Jump billing: reserve at GO, burn while you fly ──────────────────────
    //
    // The price is locked in the moment TRAVEL is pressed — from the gap as it
    // stands right then, never re-billed as the planets move — but the tank does
    // NOT empty at the press. It empties as the shuttle actually flies, which is
    // what a fuel gauge is supposed to do (Sam, 2026-09-07: "it takes the fuel
    // right away, before the 10 second timer even counts down... once the
    // thrusters actually start firing then it should slowly start to deplete,
    // and finish depleting as soon as you touchdown").
    //
    // Draining is driven by FLIGHT PROGRESS rather than a clock, so it lands
    // exactly on touchdown however long the leg takes, and hovering — which the
    // player controls and can hold indefinitely — costs nothing.

    float _reserved;            // total cost of the jump in progress
    float _reservedSpent;       // how much of it has been burned so far

    /// <summary>Fuel committed to the jump in progress but not yet burned.</summary>
    public float Reserved => Mathf.Max(0f, _reserved - _reservedSpent);

    public bool JumpInProgress => _reserved > 0f;

    /// <summary>Lock in the price of a hop. Returns false and reserves nothing if
    /// the tank cannot cover it — callers must treat that as "the jump does not
    /// happen". Nothing is deducted yet; see <see cref="BurnTo"/>.</summary>
    public bool ReserveJump(float metres)
    {
        float cost = CostForMetres(metres);
        if (_fuel < cost - 0.0001f) return false;
        _reserved      = cost;
        _reservedSpent = 0f;
        Debug.Log($"[ShuttleFuel] reserved {cost:F1} for {metres / 1000f:F2} km — " +
                  $"tank stays at {_fuel:F1}/{fuelMax:F0} until the engines fire.");
        return true;
    }

    /// <summary>Burn the reserve up to <paramref name="fraction"/> of the way
    /// through the jump (0–1). Monotonic: it never refunds if a phase reports a
    /// lower progress than one already passed, so the gauge only ever falls.</summary>
    public void BurnTo(float fraction)
    {
        if (_reserved <= 0f) return;
        float target = _reserved * Mathf.Clamp01(fraction);
        if (target <= _reservedSpent) return;
        float step = target - _reservedSpent;
        _reservedSpent = target;
        _fuel = Mathf.Clamp(_fuel - step, 0f, fuelMax);
    }

    /// <summary>Touchdown: burn whatever is left of the reserve so the gauge
    /// finishes exactly as the shuttle lands, and close the jump.</summary>
    public void FinishJump()
    {
        if (_reserved <= 0f) return;
        BurnTo(1f);
        Debug.Log($"[ShuttleFuel] jump complete — burned {_reserved:F1}, " +
                  $"{_fuel:F1}/{fuelMax:F0} left (range now {RangeKm:F1} km)");
        _reserved = _reservedSpent = 0f;
    }

    /// <summary>Launch aborted during the countdown: hand back whatever was
    /// burned (nothing, at that point) and cancel the reservation.</summary>
    public void CancelJump()
    {
        if (_reserved <= 0f) return;
        _fuel = Mathf.Clamp(_fuel + _reservedSpent, 0f, fuelMax);
        Debug.Log($"[ShuttleFuel] jump cancelled — reservation of {_reserved:F1} dropped, " +
                  $"{_fuel:F1}/{fuelMax:F0} left.");
        _reserved = _reservedSpent = 0f;
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
