# Ship fuel + per-ship power — design

**Date:** 2026-05-25
**Author:** Sam McNeil (with Claude)
**Branch context:** `feature/phone-ai-revamp`

## Goal

Add a per-ship **fuel** resource (refilled with crystals from the hotbar) and refactor today's global ship power into a **per-ship power** value. Both are needed for thrust; only power gates `LebronLight`. Each ship in the player's bought fleet has independent power and fuel. The AI on the phone can query either by ship number.

## Non-goals

- Reactor mesh / VFX changes (no glow, no animation — just add the interaction script).
- Refactoring `LebronLight` from a singleton into a per-ship component (keep singleton; just point it at the piloted ship for power drain).
- Non-solar power recharge sources (still only `SolarPanelCharger`).
- Non-crystal fuel sources.
- Hot-swappable reactor part (the reactor is fixed inside the ship prefab; we just bolt a behavior script onto its existing trigger collider).

## Design decisions (from brainstorming)

| Question | Decision |
|---|---|
| Pre-existing scene ship's fuel | Moot — there is no scene ship. Verified by GUID search in `1.6.7.7.7.unity` (zero ship prefab references; no `Ship.cs` script GUID). Every ship comes from `ShipMarketNPC.SpawnShipInstance` (vendor or debug menu). |
| Partial fill on F when crystals < need-to-full | Drain all crystals, fill partially. |
| Power scope | Per-ship (matches fuel symmetry). Each `Ship` instance owns its own power and fuel. |
| Empty-fuel UX | HUD bar pulses red below 25%; HAL voice line at 25% (low) and at 0% (dry). Mirrors existing low-power voice line. |
| Ambiguous AI query ("ship power" / "ship fuel" with no number) | Refuse and ask "which ship?" — strict and unambiguous. |
| Ship-number off-by-one | Fixed in commit `5a23439` (`BoughtShip.shipNumber` default `1` → `0`). First bought ship is now Ship 1. |

## Gating semantics (the central rule)

```
hasPower = powerCurrent > 0
hasFuel  = fuelCurrent  > 0

CanThrust         = hasPower AND hasFuel
CanRunLebronLight = hasPower
```

| State | Thrust | LebronLight | Headlight |
|---|---|---|---|
| power > 0, fuel > 0 | yes | yes | yes |
| power > 0, fuel = 0 | **no** | yes | yes |
| power = 0, fuel > 0 | **no** | **no** | yes |
| power = 0, fuel = 0 | **no** | **no** | yes |

Headlight is unaffected by power/fuel (current behavior preserved).

## Per-ship state on `Ship.cs`

New serialized fields:

```csharp
[Header("Power")]
public float powerMax = 50f;
public float powerIdleDrainPerSec   = 0.25f;
public float powerFlyingDrainPerSec = 1.5f;
float powerCurrent;
public float PowerPercent => powerMax > 0f ? powerCurrent / powerMax : 0f;

[Header("Fuel (1 crystal = 5 fuel units)")]
public float fuelMax = 100f;          // 20 crystals = full tank, 10 = half-empty refuel
public float fuelPilotedDrainPerSec  = 2.0f;  // base drain while piloted, no thrust
public float fuelThrustDrainPerSec   = 3.5f;  // WASD / Space / Ctrl held
public float fuelBoostDrainPerSec    = 6.0f;  // Shift + thrust
float fuelCurrent;
public float FuelPercent => fuelMax > 0f ? fuelCurrent / fuelMax : 0f;
```

Public read API:
```csharp
public bool HasPower            => powerCurrent > 0f;
public bool HasFuel             => fuelCurrent  > 0f;
public bool CanThrust           => HasPower && HasFuel;
public bool CanRunLebronLight   => HasPower;

public void DrainPower(float amount);   // clamps to [0, powerMax]
public void RestorePower(float amount);
public void DrainFuel(float amount);
public void RestoreFuel(float amount);  // used by ShipReactor
public void SetPower(float current);    // save-load only
public void SetFuel(float current);     // save-load only
```

### Drain logic — replaces today's `ResourceManager` power drain

In `Ship.Update()`, replacing the existing call to `ResourceManager.Instance.DrainShipPower`:

```csharp
float dt = Time.deltaTime;
if (shipIsPiloted)
{
    powerCurrent -= powerFlyingDrainPerSec * dt;
    bool thrusting = thrusterInput.sqrMagnitude > 0.01f;
    bool boosting  = thrusting && Input.GetKey(KeyCode.LeftShift)
                                && (_boostUpDepleted == false || _boostDownDepleted == false || _boostDirDepleted == false);
    float fuelRate = boosting ? fuelBoostDrainPerSec
                   : thrusting ? fuelThrustDrainPerSec
                   : fuelPilotedDrainPerSec;
    fuelCurrent -= fuelRate * dt;
}
else
{
    powerCurrent -= powerIdleDrainPerSec * dt;
    // Fuel does NOT drain idle (per spec).
}
powerCurrent = Mathf.Clamp(powerCurrent, 0f, powerMax);
fuelCurrent  = Mathf.Clamp(fuelCurrent,  0f, fuelMax);
```

Note: the `boosting` check above is a *heuristic*. The cleaner read is to add a `_isBoostingThisTick` flag inside `Ship.FixedUpdate` that lights up exactly when `boostUp || boostDown || boostDir` is true, then read it in `Update`. Plan task will resolve this — the spec records the intent (boost+thrust drains more) and the plan picks the cleanest implementation given the existing boost-pool plumbing.

### Damage interaction

`ShipDamageManager` flips `canFly = false` on the damage states (MissingLeft, MissingRight, NoThrusters). Today that single flag also gates power drain. The refactor preserves this:

- `Ship.Update` checks `canFly` AND `CanThrust` together for the drain path AND the movement path.
- A ship with no thrusters drains neither power nor fuel idle drain (the existing behavior — `canFly = false` short-circuits drain).
- Once repaired, the per-ship state resumes from where it was.

### Initialization on spawn

In `ShipMarketNPC.SpawnShipInstance`, after the BoughtShip marker is added:

```csharp
var freshShip = instance.GetComponent<Ship>();
if (freshShip != null)
{
    freshShip.SetPower(freshShip.powerMax * 0.5f);
    freshShip.SetFuel (freshShip.fuelMax  * 0.5f);
}
```

This path is shared by vendor purchases AND the debug menu's `SpawnShip44` button.

## The reactor — `ShipReactor.cs` (new file)

Lives on the `Reactor` child GameObject inside each ship prefab. The user confirmed each ship's reactor has a `BoxCollider` with `isTrigger = true` already.

```csharp
public class ShipReactor : MonoBehaviour
{
    [Tooltip("Auto-resolved via GetComponentInParent<Ship> if null.")]
    public Ship ship;

    [Tooltip("How much fuel one crystal provides. fuelMax / fuelPerCrystal = crystals for a full tank. With defaults 100/5 = 20.")]
    public float fuelPerCrystal = 5f;

    bool _playerInZone;
    bool _promptShown;

    void Awake() { if (ship == null) ship = GetComponentInParent<Ship>(); }

    void OnTriggerEnter(Collider other) {
        if (other.CompareTag("Player")) _playerInZone = true;
    }
    void OnTriggerExit(Collider other) {
        if (other.CompareTag("Player")) {
            _playerInZone = false;
            HidePrompt();
        }
    }

    void Update() {
        if (!_playerInZone) { HidePrompt(); return; }
        bool holdingCrystals = IsPlayerHoldingCrystals();
        if (holdingCrystals && ship != null && ship.FuelPercent < 1f) {
            ShowPrompt("F  INSERT CRYSTALS");
            if (TutorialGate.InteractPressed()) Refuel();
        } else {
            HidePrompt();
        }
    }

    static bool IsPlayerHoldingCrystals() {
        // "Holding crystals" = a hotbar slot of type Crystal is currently
        // equipped/highlighted (slot-driven equip from previous feature).
        var hb = Hotbar.Instance;
        if (hb == null) return false;
        return hb.GetEquippedSlotId() == Hotbar.ItemId.Crystal
            && hb.GetResourceTotal(Hotbar.ItemId.Crystal) > 0;
    }

    void Refuel() {
        float deficit = ship.fuelMax - ship.FuelPercent * ship.fuelMax;
        if (deficit <= 0f) return;
        int crystalsNeeded = Mathf.CeilToInt(deficit / fuelPerCrystal);
        int available = Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.Crystal);
        int take = Mathf.Min(crystalsNeeded, available);
        if (take <= 0) return;
        if (!Hotbar.Instance.SpendResource(Hotbar.ItemId.Crystal, take)) return;
        float fuelAdded = take * fuelPerCrystal;
        ship.RestoreFuel(fuelAdded);
        ReactorPopup.Show($"+{Mathf.RoundToInt(fuelAdded)} FUEL");
    }

    void ShowPrompt(string text) { /* InteractPromptUI.ShowFor(this, text) */ }
    void HidePrompt() { /* InteractPromptUI.HideFor(this) */ }
}
```

Notes on key types:
- **`Hotbar.GetEquippedSlotId()`** is new — exposes the type of the currently-equipped slot. From the previous feature, `_equippedSlot` is internal. Add a thin public getter: `public ItemId GetEquippedSlotId() => _equippedSlot >= 0 && _equippedSlot < NumSlots ? slots[_equippedSlot].id : ItemId.None;`
- **`ReactorPopup`** is a small new floating-text popup, modeled on `CrystalPopup` / `DustPopup` (world-space, fades out). One-shot per refuel.
- **`InteractPromptUI.ShowFor(component, text)`** — using the existing project pattern for "press F to interact" prompts. (`Interactable` + `GameUI.SetInteractionPrompt` already exists — I'll use the same hook.)

### Partial fill math

- `crystalsNeeded = ceil((fuelMax - fuelCurrent) / fuelPerCrystal)`
- `take = min(crystalsNeeded, available)`
- `fuelAdded = take * fuelPerCrystal`
- Clamp at `fuelMax` (already done by `RestoreFuel`'s clamp).

Examples (fuelMax=100, fuelPerCrystal=5):
- 0% → 100%: need 20, take min(20, available). 12 available → take 12, +60 fuel, fuelCurrent=60.
- 50% → 100%: need 10, take min(10, available). 15 available → take 10, +50 fuel, fuelCurrent=100.
- 99% → 100%: need 1 (ceil of 0.2), take 1, +5 fuel, fuelCurrent=100 (clamped from 104).

## `LebronLight` — read owning ship's power

`LebronLight` is currently a singleton that drains `ResourceManager.shipPower`. Refactor: route the drain through whatever Ship "owns" the light (the ship that spawned it / the ship the player is currently piloting when toggling it on).

Concretely:
- Add `public Ship owningShip;` field, auto-resolved via `Ship.PilotedInstance` when the light is toggled on.
- Replace `ResourceManager.Instance.DrainShipPower(usageRate * dt)` with `owningShip.DrainPower(usageRate * dt)`.
- In the toggle-on path, refuse to ignite when `owningShip == null || !owningShip.CanRunLebronLight`.
- In Update: when `owningShip.CanRunLebronLight` becomes false (power hit 0), auto-extinguish.

## `SolarPanelCharger` — restore owning ship's power

`SolarPanelCharger` is per-ship (lives on each ship prefab's solar panel). Replace `ResourceManager.Instance.RestoreShipPower(chargeRate * dt)` with the parent ship's `RestorePower(chargeRate * dt)`. Cache the parent Ship in `Awake` via `GetComponentInParent<Ship>()`.

## HUD changes

### `ResourceHUD` (legacy 4-row bar stack)

The HUD currently reads `ResourceManager.ShipPowerPercent`. Refactor to:

```csharp
Ship piloted = Ship.PilotedInstance;
float power = piloted != null ? piloted.PowerPercent : 0f;
float fuel  = piloted != null ? piloted.FuelPercent  : 0f;
// Row visibility: show power + fuel rows only when piloted (same gate as before).
bool show = piloted != null;
```

Add `shipFuelBarFill / shipFuelBarImage / shipFuelLabel / shipFuelRow` serialized fields. If `shipFuelRow == null` at startup, **procedurally clone `shipPowerRow`** immediately below it and wire the new refs (no scene work required to ship the feature).

Bar pulse thresholds: same as power (`pulseThreshold = 0.25f`, `urgentThreshold = 0.10f`, same `pulseFrequency`).

### `VitalsHUD` (newer compact card)

Same refactor — replace single ship-power read with piloted-ship's power and add a fuel row. If `VitalsHUD` is procedurally built, the row addition is one new entry in its build list.

### Hide rows when no ship is piloted

Today the ship-power row hides when `Ship.PilotedInstance == null` (line 50-52 of ResourceHUD has `_shipRowResolved` / `_shipRowVisible` flags). The new fuel row mirrors that: hidden whenever not piloted.

## AI changes

### `IntentRouter`

Replace the global `wantsShipPower` regex (`"ship power" || "battery"`) with **per-ship** intents:

```csharp
// "what's ship 2's power" / "ship 2 power" / "how much power does ship 2 have"
static readonly Regex ShipPowerIntent =
    new Regex(@"\bship\s+(\d+)\b.*\bpower\b|\bpower\b.*\bship\s+(\d+)\b",
              RegexOptions.IgnoreCase);

// "what's ship 2's fuel" / "ship 2 fuel" / "how much fuel does ship 2 have"
static readonly Regex ShipFuelIntent =
    new Regex(@"\bship\s+(\d+)\b.*\bfuel\b|\bfuel\b.*\bship\s+(\d+)\b",
              RegexOptions.IgnoreCase);
```

Handlers:
```csharp
if (ShipPowerIntent.IsMatch(msg)) {
    // ResolveShip via the existing helper. Returns "Ship N power is at X%."
}
if (ShipFuelIntent.IsMatch(msg)) {
    // Same shape. Returns "Ship N fuel is at X%."
}
```

**Ambiguous query handling** (per user decision):
- "what's ship power" / "what's the fuel" with no number → reply: `"Which ship? Try 'ship 1', 'ship 2', and so on."`
- Add a separate regex for ambiguous queries (matches "ship power" / "ship fuel" with no digit) so the AI doesn't fall through to the generic LLM path for these.

`AIChatScreen` has a vitals-only path that includes `sp = rm.ShipPowerPercent * 100f`. Refactor that to use `Ship.PilotedInstance.PowerPercent` (or skip the ship-power mention when not piloted).

### `FleetTelemetry`

The per-ship roster generator already exists. Add `power` and `fuel` columns to `RenderShipRow` so a generic "give me a fleet report" includes the new resources:

```csharp
return $"Ship {shipNumber}: {location}, {dust}, power {p}%, fuel {f}%, dish OK, {solar}, {thrusters}, {hatch}";
```

Where `p` and `f` read from the per-ship `Ship.PowerPercent` / `FuelPercent`. For ships with no `Ship` component reachable (destroyed, somehow), fall back to `?`.

### `HALCommentator`

Mirror the existing `_shipPowerLowFired` 25% / 40% hysteresis with two new low-fuel triggers AND retarget the existing low-power line to the piloted ship:

```csharp
Ship pi = Ship.PilotedInstance;
if (pi != null) {
    float pwr = pi.PowerPercent * 100f;
    float ful = pi.FuelPercent  * 100f;
    int n = BoughtShipNumberOf(pi);   // helper that returns the ship number

    // Power (was global; now per-ship piloted).
    if (pwr <= 25f && !_shipPowerLowFired) {
        Volunteer($"Ship {n} power at {Mathf.RoundToInt(pwr)}%. Solar panel exposure recommended.");
        _shipPowerLowFired = true;
    } else if (pwr >= 40f && _shipPowerLowFired) _shipPowerLowFired = false;

    // Fuel low (25%) — symmetric.
    if (ful <= 25f && !_shipFuelLowFired) {
        Volunteer($"Ship {n} fuel at {Mathf.RoundToInt(ful)}%. Insert crystals into the reactor.");
        _shipFuelLowFired = true;
    } else if (ful >= 40f && _shipFuelLowFired) _shipFuelLowFired = false;

    // Fuel dry (0%) — one-shot per drain cycle.
    if (ful <= 0f && !_shipFuelEmptyFired) {
        Volunteer($"Ship {n} reactor is dry. Thrust disabled.");
        _shipFuelEmptyFired = true;
    } else if (ful > 0f && _shipFuelEmptyFired) _shipFuelEmptyFired = false;
}
```

When `Ship.PilotedInstance == null`, hold the existing fired flags (no flicker when entering / exiting cockpits).

## `ResourceManager` simplification

After the refactor, `ResourceManager` no longer owns ship power:
- Remove fields: `shipPowerMax`, `shipPowerIdleDrainRate`, `shipPowerFlyingDrainRate`, `shipPowerRestoreThreshold`, `shipPowerCurrent`, `shipRef`, `shipPowerDepleted`.
- Remove methods: `DrainShipPower`, `RestoreShipPower`, `ShipPowerPercent`.
- Remove the ship-power drain block in `Update` (lines 89-108).
- Update `ApplyState(...)` to drop the `shipPower` parameter, and update its only call site (`SaveCollector.ApplyResources`).

`ResourceManager` becomes hunger/thirst/health/death only — its actual responsibility (player vitals).

## Save / load

### `ExtraShipSave` gains two fields

```csharp
[Serializable]
public class ExtraShipSave
{
    // ... existing fields ...
    public float power = -1f;   // -1 = legacy save, ignore on apply
    public float fuel  = -1f;   // -1 = legacy save, ignore on apply
}
```

Default `-1f` distinguishes a legacy save (field absent → JsonUtility leaves the field at its declared default) from a real saved value of 0 (saved as empty).

### Capture

In `SaveCollector.CaptureExtraShips` (already iterates BoughtShip instances), add for each entry:

```csharp
var ship = bs.GetComponent<Ship>();
if (ship != null) {
    entry.power = ship.PowerPercent * ship.powerMax;  // store absolute fuel/power, not percentage
    entry.fuel  = ship.FuelPercent  * ship.fuelMax;
}
```

Storing absolute units (not percent) so future tuning of `powerMax` / `fuelMax` doesn't silently corrupt the saved relative fill. (Tuning maxes mid-save is unusual but the absolute storage is the same number of bytes and avoids the pitfall.)

### Apply

In `SaveCollector.ApplyExtraShips`, after spawning each saved ship:

```csharp
var ship = spawnedShip.GetComponent<Ship>();
if (ship != null) {
    if (entry.power >= 0f) ship.SetPower(entry.power);
    if (entry.fuel  >= 0f) ship.SetFuel (entry.fuel);
}
// If either field is -1f (legacy save), the 50% spawn defaults from
// SpawnShipInstance carry through unchanged.
```

### `ResourcesSave.shipPower` — leave for legacy reads

Old saves still have `resources.shipPower`. We're discarding it (ResourceManager no longer reads it). Keep the field in `ResourcesSave` so JsonUtility doesn't error on legacy saves; just ignore the value. Optionally: drop it from `ApplyResources` and reduce its signature.

## Files affected (summary)

| Status | File | Why |
|---|---|---|
| Major | `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` | Per-ship power/fuel state, drain logic, gating |
| New | `Assets/3 - Scripts/Ship/ShipReactor.cs` | Crystal-insertion interaction |
| New | `Assets/3 - Scripts/Ship/ReactorPopup.cs` | World-space "+X FUEL" floater |
| Refactor | `Assets/3 - Scripts/Survival/ResourceManager.cs` | Strip ship-power; keep hunger/thirst/health/death |
| Refactor | `Assets/3 - Scripts/Ship/LebronLight.cs` | Read/drain owning ship's power instead of global |
| Refactor | `Assets/3 - Scripts/Ship/SolarPanelCharger.cs` | Charge owning ship's power instead of global |
| Modify | `Assets/3 - Scripts/Vendor/ShipMarketNPC.cs` | Set 50% power + fuel on spawn |
| Modify | `Assets/3 - Scripts/Survival/ResourceHUD.cs` | Add fuel row; bind both rows to `Ship.PilotedInstance` |
| Modify | `Assets/3 - Scripts/Survival/VitalsHUD.cs` | Add fuel row; bind both rows to piloted ship |
| Modify | `Assets/3 - Scripts/AI/HALCommentator.cs` | Retarget low-power to piloted ship; new low-fuel + dry-fuel lines |
| Modify | `Assets/3 - Scripts/AI/IntentRouter.cs` | Per-ship power + fuel intents; refuse ambiguous |
| Modify | `Assets/3 - Scripts/AI/AIChatScreen.cs` | Per-ship power/fuel mentions in the vitals tour |
| Modify | `Assets/3 - Scripts/AI/FleetTelemetry.cs` | Render power + fuel in `RenderShipRow` |
| Modify | `Assets/3 - Scripts/UI/Hotbar.cs` | Add `public ItemId GetEquippedSlotId()` getter |
| Modify | `Assets/3 - Scripts/SaveSystem/SaveData.cs` | Add `power`/`fuel` to `ExtraShipSave`; signal-value `-1f` |
| Modify | `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Capture/apply per-ship power/fuel; trim `ApplyResources` signature |

## Testing plan (Editor verification)

1. **Spawn a ship via debug menu** → confirm power and fuel bars show ~50% as soon as the player pilots it.
2. **Buy a ship from vendor** → same as above; both rows say 50%.
3. **Spawn a ship, pilot it, hold WASD for 30 s** → fuel drops faster than power. Bars match.
4. **Boost + WASD (Shift)** → fuel drops faster than non-boost thrust. Power drops at the existing piloted rate (no change).
5. **Run fuel to 0%** → ship stops thrusting. Headlight still works. LebronLight still works (power > 0).
6. **Run power to 0%** → ship stops thrusting. LebronLight cannot turn on / auto-extinguishes if already on. Headlight still works.
7. **Equip a crystal stack via hotbar slot 1-5** → walk into the reactor trigger → "F INSERT CRYSTALS" prompt appears.
8. **Press F with 5 crystals on a 50% tank** → all 5 crystals consumed, +25 fuel, fuelCurrent = 75. Brief "+25 FUEL" popup.
9. **Press F with 20 crystals on a 0% tank** → 20 crystals consumed, +100 fuel, fuelCurrent = 100 (clamped). Popup "+100 FUEL".
10. **Press F with 30 crystals on a 50% tank** → only 10 crystals consumed (the amount that fits to top up). fuelCurrent = 100. 20 crystals remain in hotbar.
11. **Solar charge** → unequipped ship under sunlight → power restores at chargeRate. Fuel doesn't change (no auto-recharge).
12. **AI: "What's ship 1's fuel?"** → "Ship 1 fuel is at X%."
13. **AI: "What's ship 1's power?"** → "Ship 1 power is at X%."
14. **AI: "What's ship power?"** (no number) → "Which ship? Try 'ship 1', 'ship 2', and so on."
15. **AI: "What's the fuel?"** → same refusal.
16. **HAL low-fuel voice line** → drain fuel to 25% → HAL volunteers "Ship N fuel at 25%. Insert crystals into the reactor."
17. **HAL dry-fuel voice line** → drain fuel to 0% → HAL volunteers "Ship N reactor is dry. Thrust disabled." Once only.
18. **Save / load round-trip** → save with fuel=43% and power=72% on Ship 2 → reload → values match exactly.
19. **Legacy save load** → load a pre-refactor save (no `power`/`fuel` fields in `ExtraShipSave`) → ship spawns with 50% defaults (the `-1f` legacy sentinel is detected and `SetPower`/`SetFuel` is not called).
20. **MainMenu trap sanity check** → build the game, launch from MainMenu → PLAY → buy a ship → fuel + power bars appear correctly. No NRE in `Player.log` for `Ship`, `ShipReactor`, `LebronLight`, `SolarPanelCharger`.

## Future seam

This design intentionally leaves room for:
- **Auto-pumps / refueling stations** on planets — they call `Ship.RestoreFuel(amount)` directly, same interface as the reactor.
- **Hot-swappable reactor part** (e.g. buying an upgraded reactor that gives 2× fuel capacity) — read `fuelMax` from a swappable `ReactorPart` ScriptableObject in the future.
- **Fuel transfer between ships** — both ships' `Drain`/`Restore` methods exist; a UI panel would just call them.
