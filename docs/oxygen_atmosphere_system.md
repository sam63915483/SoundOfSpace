# Oxygen & Atmosphere System — Implementation Spec

**For:** Claude Code
**Goal:** Add a survival oxygen system with two linked air pools (player suit + ship hull), altitude-gated refilling, an open-hatch depressurization hazard, and a suction-eject beat. The intent is to make space travel feel consequential: forgetting to seal the hull before launch should cost the player their large air buffer.

This spec is engine-agnostic. Map every concept below onto the actual systems in this codebase (player controller, ship controller, audio manager, HUD, trigger/zone volumes, death/respawn). Where this doc names a hook like `Player_Die()`, wire it to whatever already exists. Confirm the integration points in the "What to find in the codebase" section before writing code.

---

## 1. Fiction & naming (use these exact strings for VO and UI)

| Thing | Name |
|---|---|
| Home planet | Humble Abode |
| Home planet's moon | Constant Companion |
| Far mission planet | Cyclops |
| VO line on hull refill start | "Re-oxygenating the hull" |
| VO line on hull breach in flight | "Hull is ajar" |

---

## 2. Core model in one paragraph

There are two oxygen pools, each measured in **seconds of air**: the **suit** (120 s) and the **ship hull** (300 s). The suit keeps the player alive. The hull is a buffer: while the player is inside the ship and the hull still has air, the suit is sustained off the hull, so the **hull always depletes before the suit**. Air comes from breathable environments — the lower half of Humble Abode's atmosphere, and all of Cyclops. Above the midpoint of Humble Abode's atmosphere (and everywhere off-world that isn't Cyclops) there is no air. An **open hatch** lets the hull exchange with the outside: in a refill zone it tops the hull up; above the midpoint or in space it bleeds the hull out, faster the higher you are. A **closed hatch** seals the hull indefinitely. If a standing (non-piloting) player has the hatch open in flight while the hull still holds air, escaping pressure **drags them toward the hatch and ejects them**, dropping them onto suit-only air.

---

## 3. Tunable parameters

Units: **O2 is stored in seconds-of-air.** Draining at rate `r` means `o2 -= r * dt`. A refill/drain rate of `N` changes the pool by `N` seconds of air per real second, so a 300 s tank at drain rate 60 empties in 5 s.

| Constant | Default | Meaning |
|---|---|---|
| `SUIT_O2_MAX` | `120` | Suit capacity (2 min) |
| `HULL_O2_MAX` | `300` | Hull capacity (5 min) |
| `SUIT_DRAIN_RATE` | `1.0` | Suit loss/sec when exposed → full tank lasts 120 s |
| `SUIT_REFILL_RATE` | `24.0` | Suit gain/sec in breathable air → full in ~5 s |
| `HULL_REFILL_RATE` | `60.0` | Hull gain/sec while refilling → full in ~5 s |
| `HULL_DRAIN_MIN` | `5.0` | Hull loss/sec just above midpoint (slow) → ~60 s to empty |
| `HULL_DRAIN_MAX` | `60.0` | Hull loss/sec at atmosphere top & in space (almost instant) → ~5 s to empty |
| `SUCTION_FORCE_MIN` | tune | Pull toward hatch just above midpoint |
| `SUCTION_FORCE_MAX` | tune | Pull toward hatch at full altitude / in space |
| `HULL_AJAR_REPEAT` | `8.0` | Optional: seconds between repeats of the "Hull is ajar" line while still breaching |
| `SURFACE_ALT` | per-level | Ground altitude reference for Humble Abode |
| `ATMOSPHERE_TOP` | per-level | Altitude where Humble Abode's atmosphere ends / space begins |

Derived: `MIDPOINT_ALT = SURFACE_ALT + (ATMOSPHERE_TOP - SURFACE_ALT) * 0.5`. The refill cutoff and the start of hull-drain both sit at `MIDPOINT_ALT` — only the lower half of the atmosphere is breathable.

---

## 4. Zones & states

**Refill zones** (where breathable air exists):
- Humble Abode, at or below `MIDPOINT_ALT`.
- Cyclops — entire surface zone (use a trigger volume; no altitude split needed).
- Nowhere else. Constant Companion and open space are never refill zones.

**Player location states** (mutually exclusive):
- `on_foot` — walking on a surface or floating in space, *not* inside the ship.
- `inside_ship` — within the ship interior volume but *not* in the pilot seat.
- `piloting` — occupying the pilot seat / in ship control.

**Hull states** (derived each frame from hatch + ship position): `SEALED`, `REFILLING`, `DRAINING`.

---

## 5. Per-frame logic (the heart of the feature — implement this precisely)

```text
# O2 in seconds-of-air. o2 -= rate * dt.

# ---- Read world state this frame ----
ship_alt        = ship altitude above Humble Abode surface (meaningful only near Humble Abode)
player_alt      = player altitude above Humble Abode surface
on_humble       = player/ship currently at Humble Abode
on_cyclops      = currently at Cyclops
inside_ship     = player within ship interior volume
piloting        = player in pilot seat
on_foot         = not inside_ship
hatch_open      = ship hatch is open

MIDPOINT_ALT = SURFACE_ALT + (ATMOSPHERE_TOP - SURFACE_ALT) * 0.5

# Altitude factor: 0 at midpoint, ramps to 1 at atmosphere top, stays 1 in space.
if on_humble:
    altT = clamp((ship_alt - MIDPOINT_ALT) / (ATMOSPHERE_TOP - MIDPOINT_ALT), 0, 1)
else:
    altT = 1.0   # off Humble Abode and not on Cyclops = vacuum = full altitude

ship_in_refill_zone   = (on_humble and ship_alt   <= MIDPOINT_ALT) or on_cyclops
player_in_refill_zone = (on_humble and player_alt <= MIDPOINT_ALT) or on_cyclops

# ============================================================
# 1) HULL OXYGEN — only changes while the hatch is OPEN
# ============================================================
prev_hull_state = hull_state
if hatch_open and ship_in_refill_zone:
    hull_state = REFILLING
    hull_o2 = min(HULL_O2_MAX, hull_o2 + HULL_REFILL_RATE * dt)
elif hatch_open and not ship_in_refill_zone:
    hull_state = DRAINING
    hull_drain_rate = lerp(HULL_DRAIN_MIN, HULL_DRAIN_MAX, altT)
    hull_o2 = max(0, hull_o2 - hull_drain_rate * dt)
else:                       # hatch closed
    hull_state = SEALED     # holds its air, sustains the suit for free, never depletes

# Audio edge-triggers (fire on state entry, not every frame)
if hull_state == REFILLING and prev_hull_state != REFILLING:
    play_once(SFX_REOXYGENATING)      # "Re-oxygenating the hull"
if hull_state == DRAINING and prev_hull_state != DRAINING:
    play_once(SFX_HULL_AJAR)          # "Hull is ajar"
# Optional: while DRAINING, replay SFX_HULL_AJAR every HULL_AJAR_REPEAT seconds.

# ============================================================
# 2) IS THE PLAYER BREATHING?
# ============================================================
# Breathing if standing in breathable atmosphere, OR inside a hull that still has air.
player_breathing = player_in_refill_zone or (inside_ship and hull_o2 > 0)
# >>> This single line produces the "hull drains before the suit" behavior:
#     inside the ship the player breathes hull air until hull_o2 hits 0,
#     and only then does the suit start to drop.

# ============================================================
# 3) SUIT OXYGEN
# ============================================================
if player_breathing:
    suit_o2 = min(SUIT_O2_MAX, suit_o2 + SUIT_REFILL_RATE * dt)
else:
    suit_o2 = max(0, suit_o2 - SUIT_DRAIN_RATE * dt)
    if suit_o2 <= 0:
        Player_Die()      # hook existing death/respawn

# ============================================================
# 4) HATCH SUCTION — eject a standing player out an open hatch in flight
# ============================================================
suction_active = inside_ship and (not piloting) and hatch_open
                 and (not ship_in_refill_zone) and hull_o2 > 0
if suction_active:
    dir = normalize(hatch_opening_world_pos - player_world_pos)
    mag = lerp(SUCTION_FORCE_MIN, SUCTION_FORCE_MAX, altT)
    apply_force_to_player(dir * mag)
    # Once the player crosses the hatch threshold they become on_foot in space:
    # player_breathing flips to false and the suit's ~2 min countdown begins.
```

---

## 6. Why the numbers produce the intended feel

- **Closed hatch = safe.** A sealed hull never depletes and breathing off it is free, so the player can fly anywhere with a closed hatch and the suit stays topped up inside. This is the "do it right" path.
- **Open hatch in flight = you lose the 5-minute buffer.** While `DRAINING`, the hull bleeds out (≈60 s near the midpoint, down to ≈5 s high up / in space). If the player is *piloting*, they're belted in and just watch the hull meter fall; when it hits 0 the suit's 120 s begins. If the player is *standing*, suction throws them out the hatch onto suit-only air immediately. Either way the outcome is the same lesson: the hatch was open, the hull is gone, you're on 2 minutes.
- **Altitude scaling telegraphs danger.** Just above the midpoint the drain and suction are gentle; the higher you climb the more violent both become, so the boundary reads as a real threshold rather than a hard switch.

---

## 7. Per-location summary

| Location | Suit (on foot) | Hull with hatch open | Notes |
|---|---|---|---|
| Humble Abode ≤ midpoint | Refills | Refills | Primary base / refuel |
| Humble Abode > midpoint | Drains | Drains, rate rises with altitude | Telegraph the midpoint visually |
| Constant Companion (moon) | Drains | Drains (treated as vacuum) | Tank-budget destination only |
| Cyclops (far planet) | Refills | Refills | Breathable checkpoint + refuel |
| Open space | Drains | Drains at max | — |

An oxygenated hull overrides "suit drains" at any of these locations — while the player is inside the ship and `hull_o2 > 0`, the suit is sustained regardless of where the ship is.

---

## 8. Audio & HUD

**Audio.** Add two one-shot VO hooks: `SFX_REOXYGENATING` ("Re-oxygenating the hull") fires on entering the `REFILLING` state; `SFX_HULL_AJAR` ("Hull is ajar") fires on entering the `DRAINING` state, with an optional re-trigger every `HULL_AJAR_REPEAT` seconds while the breach continues. Both are edge-triggered off the hull state transition, never per-frame.

**HUD.**
- **Suit meter** — visible whenever the player is `on_foot`; recommend keeping it always visible. Fill = `suit_o2 / SUIT_O2_MAX`.
- **Hull meter** — visible while `piloting`. Recommended extension: also show it whenever `inside_ship` or whenever `hull_state == DRAINING`, so the danger is legible when the player stands up to a breach. Fill = `hull_o2 / HULL_O2_MAX`.
- **Atmosphere boundary (recommended).** Give the player a way to see the midpoint coming — an altitude readout, a haze band at `MIDPOINT_ALT`, or a refill indicator that tapers as they climb. Without this the refill cutoff can read as a bug.

---

## 9. Death & checkpoints

`suit_o2` reaching 0 is the only death condition in this system; route it to the existing death/respawn flow. On respawn, restore `suit_o2 = SUIT_O2_MAX`. Decide with the existing save system how the ship/hull resets on respawn (suggest: ship returns to its last safe state, hull full). **Cyclops is a checkpoint** — it registers as the respawn point on first arrival, in addition to refueling and being breathable; Humble Abode is the default checkpoint before then. Use whatever checkpoint registration already exists.

---

## 10. Design notes / one-line switches

**Suit refill vs. pause inside the ship.** As written, an oxygenated hull *refills* the suit (`player_breathing` is true inside with `hull_o2 > 0`, so the suit climbs), making the ship a true sanctuary. If you'd rather the hull only *holds* the suit steady (no top-off — a slightly harsher, more resource-tense feel), change the suit block so that when `player_breathing` is true *because of `inside_ship` specifically*, the suit is clamped at its current value instead of refilled, and only the `player_in_refill_zone` case refills. Drain order is unaffected either way.

**Suction floor.** `SUCTION_FORCE_MIN` keeps a noticeable tug even just above the midpoint so the eject still teaches the lesson at low altitude. Set it to `0` if you want the boundary to be genuinely gentle and only dangerous high up.

**Hull breathing cost.** This spec intentionally does **not** deplete a sealed hull from the player breathing — a closed hatch keeps the hull good indefinitely, matching "closing your hatch will keep it good." Only an open hatch above the midpoint costs hull air.

---

## 11. What to find in the codebase before implementing

- **Player controller:** on-foot vs. inside-ship state (interior volume or parenting), pilot-seat enter/exit, a way to apply an external force/impulse to the player body, and the death/respawn entry point.
- **Ship controller:** hatch open/close state and the action that toggles it, ship altitude/transform, pilot state, and the world transform of the hatch opening (for suction direction and the eject point).
- **Altitude / atmosphere source:** how to get altitude relative to a body, and existing zone/trigger definitions for "at Humble Abode / Constant Companion / Cyclops / in space." Reuse trigger volumes if they exist; otherwise add them.
- **Audio manager:** how to play a one-shot VO line; register `SFX_REOXYGENATING` and `SFX_HULL_AJAR`.
- **HUD/UI:** how meters are added to the existing HUD.
- **Save/checkpoint system:** checkpoint registration and respawn restore.

---

## 12. Acceptance checklist

- [ ] Standing on Humble Abode below the midpoint, the suit sits at full.
- [ ] Walking uphill past the midpoint, the suit stops refilling and begins to drain; HUD reflects it.
- [ ] In the ship at base, opening the hatch plays "Re-oxygenating the hull" and the hull climbs to full.
- [ ] Closing the hatch and flying to space: hull stays full (`SEALED`), no warning, suit stays topped up inside.
- [ ] Flying to space with the hatch open while **piloting**: "Hull is ajar" plays, hull drains fast; when hull hits 0 the suit begins draining; death ~120 s later if not descended.
- [ ] Standing up mid-flight with the hatch open above the midpoint: player is pulled to the hatch and ejected into space, then on suit-only air.
- [ ] Opening the hatch just above the midpoint gives a slow drain and gentle suction; high above, near-instant drain and strong suction.
- [ ] Landing on Constant Companion and stepping out: suit drains, no refill; stepping back into the oxygenated hull: suit safe again.
- [ ] Reaching Cyclops: suit and hull refill; Cyclops registers as the active checkpoint.
- [ ] Suit reaching 0 anywhere triggers death and respawn at the last checkpoint with the suit restored to full.
