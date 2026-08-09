# Vaulted systems

Nothing here failed and nothing was deleted. Each system was switched off on
**2026-08-09** to keep the multiplayer baseline small while world-state
replication is built. Each comes back the same way.

## How to restore any of them

1. Flip its flag in `Assets/3 - Scripts/Tutorial/FeatureVault.cs` to `true`.
2. Drag the prefab from `Assets/1 - samsPrefabs/_Vaulted/` into
   `1.6.7.7.7.unity`, parented to **`Humble Abode`**.
3. Set its **local** position from the table below (they are body-relative —
   Humble Abode orbits, so world coordinates would be wrong by the time you
   read them).
4. Save the scene.

## Why prefab-and-delete, not "set inactive"

An inactive GameObject **still loads its meshes, textures and audio** with the
scene. The concert was vaulted specifically because it is heavy on a machine, so
leaving it in the scene disabled would have kept most of the cost. Removing the
instances gives back the CPU, GPU and scene-load memory; the prefab keeps the
work.

## What is vaulted

| Object | Prefab | Parent | Local position | Flag |
|---|---|---|---|---|
| `STAGEGOOD` | `STAGEGOOD.prefab` | `Humble Abode` | `(153.5, -125.9, -92.2)` | `ConcertVenue` |
| `STAGEGOOD2` | `STAGEGOOD2.prefab` | `Humble Abode` | `(-128.6, 46.9, 176.3)` | `ConcertVenue` |
| `AudienceZone` | `AudienceZone.prefab` | `Humble Abode` | `(101.4, -127.0, -126.7)` | `ConcertVenue` |
| `AudienceZone 2` | `AudienceZone_2.prefab` | `Humble Abode` | `(-80.0, 83.3, 165.3)` | `ConcertVenue` |
| `SHIPSCHOOL` | `SHIPSCHOOL.prefab` | `Humble Abode` | `(111.1, 97.5, 138.0)` | `ShipSchool` |
| `Tevsship` | `Tevsship.prefab` | `Humble Abode` | `(146.4, -108.9, 112.3)` | `TevCabinAmbush` |
| `TEV2` | `TEV2.prefab` | `Humble Abode` | `(78.1, 55.0, 178.1)` | `VillageTev` |

Both stages carried the full rig — cone lights, strobes, lasers, blinders, fog,
haze and speakers — so `STAGEGOOD` / `STAGEGOOD2` take the whole concert with
them. Verified after vaulting: **zero** objects carrying a `Concert*`,
`Audience*` or `SpeakerSource` component remain in the scene.

## ⚠️ Vaulting `Tevsship` also vaulted the smuggling mission

`Tevsship` carries **`TevSmugglingMission`** (the B-1 interrogation-and-chase
beat) as well as `SCARE/TevScareTrigger`. Restoring the ship restores the
mission with it; they are one hierarchy. That was implicit in "remove Tev's ship
outside his cabin", but it is a bigger feature than the jumpscare alone, so it
is called out here rather than being a surprise later.

## Explicitly NOT vaulted

- **Tev himself**, at his cabin (`TEV`, 9 m away) — he carries
  `TevMushroomOnboarding`, the entry point to the mushroom economy, and owns
  weekly rent collection. Only `TEV2` (203 m from the cabin, 67 m from the
  village) was vaulted. The two were told apart by distance, not by name.
- **`TevFamilyPhoto_Prop`** inside `StartCabin` — cabin dressing.
- **The village** — kept as scenery; it will be given a purpose later.
- **`Max Audience`** in the settings UI — an inactive settings row costing
  nothing. Left so the settings panel's layout and bindings are untouched.
- **`Assets/3 - Scripts/Concert/`** — the whole script folder still compiles.
  Only the scene instances were removed, so the flags and code are ready the
  moment the prefabs go back.
