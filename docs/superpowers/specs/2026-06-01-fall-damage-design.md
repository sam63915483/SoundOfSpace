# Fall Damage — Design Spec (2026-06-01)

## Goal
Add fall damage to the player. The jetpack lets the player fly anywhere, so
landings can be fast. Damage must depend **only on vertical (toward-surface)
speed at landing** — horizontal speed is irrelevant. Three impact tiers (light /
medium / hard) each play an impact thud + a player pain voice and deal
speed-scaled damage. A normal jump must never trigger a tier.

## Constraints discovered
- Player is a `Rigidbody` controlled by `PlayerController`
  (`Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs`).
- `PlayerController` fires `public event Action OnLanded` on every
  airborne→grounded transition (`:517`), independent of the small land-sound
  gate (`minAirborneForLandSound`).
- Planet-relative velocity: `PlayerController.RelativeVelocity` (`:1226`) =
  `rb.velocity - referenceBody.velocity` (orbital motion subtracted out).
- Surface-up: `player.transform.up` (PlayerController rotates to gravity-up).
- `jumpForce = 20` applied as `VelocityChange` ⇒ a plain jump launches at
  ~20 m/s and lands at ~20 m/s. **Light tier must start above ~20.**
- Damage entry point: `ResourceManager.Instance.TakeDamage(float)`
  (`Assets/3 - Scripts/Survival/ResourceManager.cs`). It already triggers the
  red-flash / vignette / hit-shake FX and fires the death cutscene at 0 HP.
- `PlayerController` has **no** static singleton — find via `FindObjectOfType`
  once, cached.
- `PlayerController` and `ResourceManager` internals must not be edited
  (PlayerController is huge/fragile; ResourceManager is scene-placed). The whole
  feature is one new self-contained component.

## Component
`Assets/3 - Scripts/Survival/FallDamage.cs` — a `MonoBehaviour` placed on the
Player GameObject in `1.6.7.7.7.unity`.

### Vertical-speed measurement (horizontal-proof)
- Each `Update`: `down = -Vector3.Dot(player.RelativeVelocity, player.transform.up)`.
  Positive `down` = moving toward the surface. Horizontal running contributes ~0.
- Track `peakFallSpeed = Max(peakFallSpeed, down)` while airborne (robust against
  the collision zeroing velocity a frame early).
- On `OnLanded`: read `peakFallSpeed`, resolve tier, apply damage + sounds, then
  reset `peakFallSpeed = 0`. Because it resets every landing and standing/walking
  yields ~0 `down`, a normal jump never accumulates a tier.

### Tiers (Inspector-tunable speed bands, m/s)
| Tier   | Default band | Sounds                         |
|--------|--------------|--------------------------------|
| (none) | < 24         | nothing (existing land sound)  |
| Light  | 24 – 36      | `impactLight`  + `painLight`   |
| Medium | 36 – 50      | `impactMedium` + `painMedium`  |
| Hard   | ≥ 50         | `impactHard`   + `painHard`    |

Impact thud and pain voice always fire together for the resolved tier.

### Damage (continuous)
`damage = lightDamage + (speed - lightThreshold) * damagePerSpeed`, clamped
`[0, maxFallDamage]`. Defaults: `lightThreshold = 24`, `lightDamage = 8`,
`damagePerSpeed = 2`, `maxFallDamage = 100`. ⇒ ~36 m/s → 32, ~50 → 60, ~70 → 100
(lethal → death cutscene fires automatically). All numbers exposed in the
Inspector for playtest tuning.

### Audio
6 public `AudioClip` fields (`impactLight/Medium/Hard`, `painLight/Medium/Hard`)
played via a dedicated 2D `AudioSource` on this component (mirrors the player's
existing `sfxSource`), so we don't double-play `ResourceManager.damageClip`.
Per-pair volume fields.

### Lifecycle
- `Start`: lazy-find `PlayerController` (throttled retry if null), subscribe
  `OnLanded`.
- `OnDestroy`: unsubscribe.
- No `FindObjectOfType` in `Update` (cached; lazy-refind only if null), per repo
  conventions.

## Out of scope
- No save state (fall damage is instantaneous; HP is already saved by
  ResourceManager). Nothing to reset in `NewGameReset`.
- No changes to the existing small jump/land sound.
