# Audio tuning sheet — 2026-08-10 loudness pass

State of the world before this pass: **no AudioMixer, no buses, no limiter,
zero of ~107 AudioSources routed anywhere**. The only global control was
`AudioListener.volume`, and the main menu reset it to 100% on every visit.
Almost every code-created source is **2D at full strength** (`AddComponent<AudioSource>`
defaults `spatialBlend = 0`), so distance never attenuates anything, and the
first minute of the game stacks ~4.4 units of 2D gain into a listener with no
headroom. That is the "drastically too loud".

## What the pass wired (code, done)

- **`GameAudioBus`** (`3 - Scripts/Audio/GameAudioBus.cs`) — Music / SFX /
  Ambience / UI volume buses over the master. A source is routed with one line
  right after creation: `GameAudioBus.Register(src, GameAudioBus.Bus.SFX);`
  PlayOneShot inherits its source's volume, so registering a source covers all
  its one-shots. **Do not register sources whose volume is animated by code**
  (suit wind, heartbeat fades) — the router captures volume-at-registration as
  the authored level and would fight the animation.
- **Sliders**: pause menu → CONTROLS tab → AUDIO now has MASTER / MUSIC / SFX /
  AMBIENCE / UI, persisted via PlayerPrefs, applied in `InputSettings.Begin`
  before gameplay sounds start.
- **Master default is now 0.5** for fresh installs (existing saved prefs win).
  This is the stand-in for a limiter — see "Why no mixer asset" below.
- **Bug fixes**: main menu no longer resets master to 1.0
  (`MainMenuController`); the death cutscene now respects the master slider
  (it used to bypass it entirely via `ignoreListenerVolume`); six door/switch/
  warning sounds that played at an implicit **1.0** (no volume argument) now
  have serialized volume fields defaulted to 0.7 (`AirlockController`,
  `MoonBaseDoor`, `MoonBasePowerSwitch`, `ResourceHUD`, `VitalsHUD`).
- **Routed so far**: UiSfxPlayer (UI + pause ambience→Music), menu ambience
  (→Music), dome hum (→Ambience). Everything else currently obeys MASTER only —
  routing is one `Register` line per source; add them as you touch systems.

## Why no mixer asset (and what it costs)

A real `.mixer` can't be authored reliably outside the Editor UI, and 97 of the
~107 sources are created in code anyway, so the code router reaches everything
without prefab churn. The one thing lost is a **limiter on the summed signal**.
The 0.5 master default is the headroom substitute. If you ever want the real
thing: create `GameAudio.mixer` in the Editor (Master → Music/SFX/Ambience/UI,
add a Limiter effect on Master), and we can point `GameAudioBus` at its groups
instead of scaling volumes — the call sites won't change.

## Tune-by-ear table (worst offenders first)

"Current" = what actually plays in the build (scene-authored values override
code defaults — those rows say *scene*). Suggested values are starting points
biased toward: nothing above 0.8, sustained loops ≤ 0.4, UI ≤ 0.5.

### Tier S — first 60 seconds, 2D, ≥0.9 (the playtest complaint)

| Source | Where to tune | Current | Suggested |
|---|---|---|---|
| Pod thruster loop | **scene** `PodArrivalSequence.thrusterVolume` | 1.0 | 0.55 |
| Pod heartbeat | **scene** `heartbeatApproachVolume` / `heartbeatImpactVolume` | 0.95 / 1.0 | 0.5 / 0.65 |
| Pod impact boom | **scene** `impactVolume` | 1.0 | 0.7 |
| Intro heartbeat (stacks with pod's!) | **scene** `IntroSequenceController.heartbeatTargetVolume` | 0.7 | 0.4 |
| Pod alarm beep | **scene** `alarmVolume` | 0.7 | 0.5 |
| Pod rumble | **scene** `rumbleVolume` | 0.55 | 0.4 |
| Cop radio voice | `TevSmugglingMission.cs` `CopRadioGain` const ×1.5 on a 0.85 source (**effective 1.275 — the loudest thing in the game, deliberate pre-mixer hack**) | 1.275 | 0.9 (drop gain to 1.0) |

### Tier A — full-volume 2D one-shots

| Source | Where | Current | Suggested |
|---|---|---|---|
| Cop zap / ping | `CopEnergyBlast.cs:175,195` | 1.0 | 0.7 |
| Cop ship boom / siren | `CopShipController.cs:207,112` | 1.0 / 0.9 | 0.7 / 0.6 |
| Tev/TR mission voices | `TevSmugglingMission.cs:983,1066` | 1.0 | 0.8 |
| Drowning death | `Poolrooms/DrowningController.cs:180` | 1.0 | 0.8 |
| Static field bed (dark) | `StaticFieldController.cs:353` | 1.0 | 0.6 |
| Dimension "true" hums ×6 | MirrorLake/Orchard/RedForest/WaitingRoom/WellField/WheatAtDusk | up to 1.0 | 0.7 |
| Water splash | `PlayerController.cs:416` | 0.9 | 0.6 |
| Stasis doors ×2 | `StasisPodDoor.doorVolume`, `ShuttleArrivalSequence.cs:989` | 0.9 | 0.65 |
| Enemy attack/charge/sniff/death | `EnemyController` fields | 0.85–1.0 | 0.6–0.75 |
| Mushroom squish | `MushroomSpawner.squishVolume` | 0.85 | 0.6 |

### Tier D — sustained 2D loops (they all sum, forever)

| Source | Where | Current | Suggested |
|---|---|---|---|
| O2 suction | `OxygenManager.suctionVolume` | 0.8 | 0.45 |
| Shuttle wind / thruster | `ShuttleArrivalSequence` fields | 0.8 | 0.5 |
| Reactor buzz | `ReactorGlow.reactorBuzzVolume` (modulates ×2.3!) | 0.75 | 0.4 |
| Ship thrust / engine | `Ship` fields | 0.6 / 0.4 | 0.45 / 0.3 |
| Footsteps | `PlayerController.footstepVolume` | 0.5 | 0.35 |
| Jetpack loops ×3 (can overlap) | `PlayerController` fields | 0.5 each | 0.3 |
| Suit breathing / wind | **scene** `PlayerSuitAudio` | 0.374 / 0.419 | keep |
| UI click / hover | `UiSfxPlayer` consts | 0.75 / 0.6 | 0.5 / 0.35 |

### Also worth knowing

- `PlayClipAtPoint` always spawns a 3D source with **log rolloff 1→500 m** —
  effectively audible everywhere. Tree/crystal/mushroom breaks, thruster
  detach, shuttle exit door and the Tev scare all use it. Volume args are now
  explicit everywhere; the 500 m radius itself can't be changed without
  replacing PlayClipAtPoint with a small helper (worth doing someday).
- Enemy **spit** has `minDistance 25` — full volume out to 25 m by design.
- The typewriter sources (11 NPCs) are 0.3 / 2D — fine.
- Scene-authored values live in `1.6.7.7.7.unity` and **override the code
  defaults** — tune those in the Inspector, not in the script.
