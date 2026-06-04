# Oxygen & Atmosphere System — Design

**Date:** 2026-06-03
**Status:** Approved (design) → implementation plan next
**Source spec:** `docs/oxygen_atmosphere_system.md` (engine-agnostic intent)
**This doc:** maps that intent onto the real systems in this codebase and records the design decisions.

---

## 1. Goal (unchanged from source)

Two linked air pools measured in **seconds-of-air**: the **suit** (120 s) keeps the player alive; the **ship hull** (300 s) is a buffer. While the player is inside the ship with hull air, the suit is sustained off the hull, so the **hull always depletes before the suit**. Air comes from breathable environments (lower half of Humble Abode's atmosphere; all of Cyclops). An **open hatch** exchanges hull air with the outside (tops up in a refill zone, bleeds out above the midpoint / in space, faster with altitude). A **closed hatch** seals the hull indefinitely. A standing (non-piloting) player with the hatch open in flight is dragged toward the hatch and **ejected** onto suit-only air.

Fiction/naming strings (exact, for VO/UI): home planet **Humble Abode**, its moon **Constant Companion**, far planet **Cyclops**; VO **"Re-oxygenating the hull"** (refill start), **"Hull is ajar"** (breach in flight).

## 2. Tunables (§3 of source — spec defaults)

| Constant | Default | Meaning |
|---|---|---|
| `SUIT_O2_MAX` | 120 | Suit capacity (s) |
| `HULL_O2_MAX` | 300 | Hull capacity (s) |
| `SUIT_DRAIN_RATE` | 1.0 | Suit loss/s when exposed |
| `SUIT_REFILL_RATE` | 24.0 | Suit gain/s when breathing |
| `HULL_REFILL_RATE` | 60.0 | Hull gain/s while refilling |
| `HULL_DRAIN_MIN` | 5.0 | Hull loss/s just above midpoint |
| `HULL_DRAIN_MAX` | 60.0 | Hull loss/s at atmosphere top / space |
| `SUCTION_FORCE_MIN` | tune (>0) | Pull toward hatch just above midpoint |
| `SUCTION_FORCE_MAX` | tune | Pull at full altitude / space |
| `HULL_AJAR_REPEAT` | 8.0 | Seconds between "Hull is ajar" repeats while breaching |
| `atmosphereTopAltitude` | per-level (serialized) | Height (m) above Humble Abode surface where atmosphere ends |
| `cyclopsBreathableCeiling` | per-level (serialized, generous) | Altitude ceiling for Cyclops breathable zone |

`midpoint = atmosphereTopAltitude * 0.5` (surface = altitude 0 in our frame). Refill cutoff and start of hull-drain both sit at the midpoint — only the lower half of Humble Abode's atmosphere is breathable.

## 3. Design decisions (the four forks)

1. **Cyclops checkpoint:** *Autosave on Cyclops arrival.* When `ReferenceBody` first becomes Cyclops inside its breathable zone, fire one autosave via the existing autosave path and set a persisted `cyclopsCheckpointReached` flag. Death reloads the newest save (existing flow), so post-Cyclops deaths respawn at Cyclops; Humble Abode is the default before that. No new dedicated checkpoint-pointer subsystem.
2. **Suit inside oxygenated hull:** *Refill (sanctuary).* While `player_breathing` is true the suit climbs to full — the ship is a true safe haven.
3. **Suction floor:** *Always a noticeable tug.* `SUCTION_FORCE_MIN > 0`, so the eject lesson lands even just above the midpoint.
4. **VO audio:** *Generate TTS now via Coplay.* Both lines generated as HAL-style TTS (ElevenLabs "George" British) and routed through the existing `HALVoicePlayer` / HUD-subtitle path. Requires active Coplay login.

## 4. Architecture

A new **`OxygenManager`** MonoBehaviour, auto-singleton following `SpaceDustInventory.cs`:
- `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` + MainMenu early-return + `Instance` guard (clear in `OnDestroy`).
- **Seeded in `MainMenuController.EnsureGameplaySingletons()`** (trap #1 — mandatory or it never auto-creates in builds).
- Owns `suitO2`, `hullO2` (seconds-of-air) and the derived `hullState` (SEALED/REFILLING/DRAINING).
- Pool updates in `Update`; suction `AddForce` in `FixedUpdate`.
- All §2 constants are serialized fields (appended at class end, per convention), spec defaults.

`OxygenManager` stays isolated from `ResourceManager` (which remains the hunger/thirst/health authority). The only coupling is the death call.

## 5. World-state reads (no new per-planet trigger volumes)

- **Altitude** = `(referenceBody.Position − pos).magnitude − referenceBody.radius`, using `PlayerController.ReferenceBody` (already computed each FixedUpdate). Surface = altitude 0.
- **Planet identity** = `ReferenceBody.bodyName`. Confirmed present: "Humble Abode", "Cyclops", "Constant Companion".
  - `ship_in_refill_zone` / `player_in_refill_zone` = (`Humble Abode` && alt ≤ midpoint) || (`Cyclops` && alt ≤ `cyclopsBreathableCeiling`).
  - vacuum (altT = 1) = any other body, null body, or above atmosphere top.
- **inside_ship** = player inside a **new interior trigger volume** added to the ship, OR `piloting` (the player GameObject is disabled while piloting, so piloting ⇒ inside).
- **piloting** = `Ship.PilotedInstance` is this ship (`Ship.IsPiloted`).
- **on_foot** = not inside_ship.
- **hatch_open** = `Ship.HatchOpen`.

## 6. Per-frame logic (§5 of source, decisions applied)

```
prev = hullState
if hatch_open && ship_in_refill_zone:   hullState=REFILLING; hull = min(MAX, hull + HULL_REFILL_RATE*dt)
elif hatch_open && !ship_in_refill_zone: hullState=DRAINING;  hull = max(0, hull - lerp(MIN,MAX,altT)*dt)
else:                                    hullState=SEALED      # never depletes
# edge-triggered VO on hullState entry (REFILLING→"Re-oxygenating the hull", DRAINING→"Hull is ajar", repeat every 8s while DRAINING)

player_breathing = player_in_refill_zone || (inside_ship && hull > 0)
if player_breathing: suit = min(SUIT_MAX, suit + SUIT_REFILL_RATE*dt)   # refill (sanctuary)
else:                suit = max(0, suit - SUIT_DRAIN_RATE*dt); if suit<=0: Die()

altT = on_humble ? clamp((alt - midpoint)/(top - midpoint),0,1) : 1.0
suction = inside_ship && !piloting && hatch_open && !ship_in_refill_zone && hull>0
if suction: AddForce(normalize(hatchAnchor - pos) * lerp(SUCTION_MIN,SUCTION_MAX,altT))   # MIN>0
```

Death: `suit<=0` → `ResourceManager.TakeDamage(lethal, playHurtClip:false)` to enter the existing death pipeline (`DeathCutsceneController` → reload newest save).

## 7. Integration points (confirmed in codebase)

- **Player:** `PlayerController.ReferenceBody` (current body), `.Rigidbody` (`AddForce(.., ForceMode.Acceleration)` for suction). Death via `ResourceManager.TakeDamage`.
- **Ship:** `Ship.HatchOpen` / `hatchOpen` / `ToggleHatch()`; `Ship.IsPiloted` / `Ship.PilotedInstance`; `Ship.Rigidbody`; `Ship.hatch` transform (suction anchor — fall back to a child empty only if the pivot reads wrong).
- **Altitude:** `CelestialBody.Position`, `.radius`, `.bodyName` (gameplay accessors — NOT the forbidden generation code).
- **Audio/VO:** no central AudioManager; `HALVoicePlayer.TryPlay(string)` (canned TTS from StreamingAssets) for VO; `AudioSource.PlayOneShot` as fallback.
- **HUD:** `ResourceHUD` bar pattern — `SetBar(RectTransform fill, float percent)` scales `localScale.x`. Add suit + hull bars mirroring hunger/thirst.
- **Save:** `SaveData` DTO + `SaveCollector` Capture/Apply (strict order) + `NewGameReset.Apply()` reset.

## 8. Scene / prefab work (Unity MCP / Coplay)

1. **Ship interior trigger** — child trigger collider on the ship sized to the interior + a small `ShipInteriorVolume` relay reporting player enter/exit to `OxygenManager`.
2. **Hatch anchor** — reuse `Ship.hatch.position`; add a child empty only if needed.
3. **HUD bars** — two new bars on the `ResourceHUD` canvas; suit always visible on-foot, hull visible while piloting/inside/DRAINING; wire serialized refs.
4. **VO clips** — generate both lines via Coplay TTS, place where `HALVoicePlayer`/manifest reads them, wire edge-triggered playback.

## 9. Save / checkpoint detail

- `O2Save { float suitO2; float hullO2; bool cyclopsCheckpointReached; }` added to `SaveData`.
- `CaptureO2` / `ApplyO2` slotted into `SaveCollector` at the singletons step of the documented order.
- `NewGameReset.Apply()`: suit = full, hull = full, flag = false.
- On respawn/load, suit restored to full (apply path).

## 10. Constraints honored

- **Forbidden zone untouched** — altitude derived from `CelestialBody` gameplay accessors + a tunable atmosphere-height constant; no atmosphere/shader/generation code read or written.
- **Trap #1** — `OxygenManager` (and any new auto-singleton) seeded in `EnsureGameplaySingletons()`.
- Built-in RP (no URP); serialized fields appended at class ends; `CompareTag`; rigidbody writes via physics APIs; no `FindObjectOfType` in per-frame loops.
- New `.cs` + `.meta` files `git add`-ed.

## 11. Acceptance checklist (from source §12)

- [ ] Standing on Humble Abode below midpoint: suit full.
- [ ] Walking past midpoint: suit stops refilling, begins draining; HUD reflects it.
- [ ] At base, opening hatch: "Re-oxygenating the hull" plays, hull climbs to full.
- [ ] Closing hatch + flying to space: hull stays full (SEALED), no warning, suit topped up inside.
- [ ] Hatch open + piloting to space: "Hull is ajar", hull drains fast; at 0 the suit drains; death ~120 s later.
- [ ] Standing mid-flight, hatch open above midpoint: pulled to hatch and ejected, then suit-only.
- [ ] Just above midpoint: slow drain + gentle (but nonzero) suction; high up: near-instant drain + strong suction.
- [ ] Constant Companion on-foot: suit drains, no refill; back in oxygenated hull: safe.
- [ ] Reaching Cyclops: suit + hull refill; Cyclops registers as active checkpoint (autosave).
- [ ] Suit at 0 anywhere: death + respawn at last checkpoint, suit restored full.
