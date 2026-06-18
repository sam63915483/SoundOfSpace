# Stasis Pod Asset — Design

**Date:** 2026-06-18
**Status:** Approved (design); ready for implementation plan
**Related:** `docs/superpowers/specs/2026-06-18-stasis-pod-arrival-intro-design.md` (the cinematic this asset lives inside), `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs`

## Goal

Replace the removed placeholder pod (crude runtime cubes) with a real **stasis/escape pod** the player rides during the arrival descent. The player falls **feet-first** toward Humble Abode and looks out through **6 tinted windows**: 4 around the middle, one in the floor (the planet growing beneath), one in the ceiling (stars above). It must look deliberately built — dark industrial — not like grey boxes.

The blocking constraint that shaped every decision: **the asset generator can't make believable see-through windows, and Blender is not installed** (force-reimporting models also breaks the ship prefabs in this repo). So the pod is composed from Unity built-in primitives + the project's existing transparent-glass shader, authored into a real prefab.

## Non-goals (v1)

- No colliders / physics — the pod is cosmetic only.
- No animated frost/fog, no seat-harness, no interior console props — all easy to add later, out of scope now.
- Not a reusable gameplay object — it exists for the descent cinematic and is destroyed on crash.

## Decisions (locked during brainstorming)

| Question | Decision |
| --- | --- |
| Build method | **Authored prefab**, composed from Unity primitives via an editor builder script (no Blender, no asset-gen). |
| Aesthetic | **Dark industrial escape pod** — gunmetal hull, amber/red emissive trim, smoky tinted glass. |
| Descent framing | **Feet-first fall** — bottom window faces the planet, top window faces space, 4 side windows show the horizon. |

## Geometry — "faceted capsule"

A smooth pill-capsule with cut window holes would need authored mesh work (Blender — unavailable). Instead the silhouette is faceted, which suits the industrial look and needs no mesh-cutting:

- **Body:** an **octagonal prism**. 8 flat faces around the circumference, alternating:
  - **4 gunmetal panels** — the structural struts between windows.
  - **4 glass faces** — the side windows.
- **Top + bottom:** short **faceted collars** (octagonal frustums) tapering inward from the body to a **glass cap** at each end — the ceiling window (space) and the floor window (planet).
- Every piece is a **thin box or quad** so its surface is visible from *inside* the pod (single-sided primitive faces would be back-culled when viewed from within).

Approximate interior radius **~1.2–1.5 m** so the windows sit about an arm's length from the player — an intimate, slightly claustrophobic cryo-pod. The player camera sits at the pod center.

### Prefab hierarchy (target)

```
StasisPod (root, no collider)
├── Hull
│   ├── Body_Panel_x4      (gunmetal, the 4 solid octagon faces)
│   ├── Body_Window_x4     (glass, the 4 side windows)
│   ├── TopCollar          (gunmetal frustum)
│   ├── TopWindow          (glass cap — ceiling/space)
│   ├── BottomCollar       (gunmetal frustum)
│   └── BottomWindow       (glass cap — floor/planet)
├── Trim_*                 (thin emissive strips framing the windows)
└── InteriorLight          (dim amber Point light, small range ~3 m)
```

## Materials (3, all editable assets)

1. **Hull** — Standard shader, dark worn gunmetal (mid metallic, mid smoothness).
2. **Glass** — the existing `Custom/sFuture Glass Early Queue` shader (`Assets/sFuture Modules Pro/Materials/Glass_EarlyQueue.shader`), smoky dark tint, alpha ≈ 0.35. Its render queue (2450, ≤ 2500) is the whole reason this works: it makes the planet's `[ImageEffectOpaque]` atmosphere/ocean post-process render *through* the window correctly instead of the glass occluding it (the "transparent queue gotcha" in CLAUDE.md). The stock shader is `Cull Back`, so a flat pane would vanish when viewed from inside. Fix: make a one-line **`Cull Off` copy** of the shader (`PodGlass_DoubleSided.shader`) and use **single quads** for the window panes — visible from both sides, and no double-tint (a thin glass *box* would darken the view through two layers). The opaque hull pieces stay thin boxes.
3. **Trim** — Standard shader with amber/red emission (unlit-bright), framing the windows. Reads in the dark even before lighting; reinforced by the single dim amber interior point light.

## How it's built

A one-off **editor builder script** (run via Coplay `execute_script`, the same pattern used to wire the audio clip) that:

1. Creates/loads the 3 material assets (hull, glass `Cull Off`, emissive trim).
2. Composes the prefab hierarchy from primitives (thin boxes/quads), positioning the 8 body faces, the two collars, the two caps, the trim strips, and the interior light.
3. Strips colliders.
4. Saves it as **`Assets/1 - samsPrefabs/StasisPod.prefab`** via `PrefabUtility.SaveAsPrefabAsset` (alongside the other gameplay prefabs). Materials and the `Cull Off` glass shader go under **`Assets/2 - Materials/Intro/`** and **`Assets/3 - Scripts/Tutorial/`** respectively (the latter beside `PodArrivalSequence`).

Result: a clean, Inspector-tweakable prefab — the user can adjust panel materials, tint, emissive color, and light without code.

## Integration with `PodArrivalSequence`

- Add a serialized `GameObject podPrefab` field (appended at the END per the serialized-field convention).
- `Setup()` instantiates the prefab; `Teardown()` destroys the instance.
- **Orientation (fixes the old spin):** orient the pod from the **constant descent direction `_dir`** so the bottom window points at the planet (`+Y` aligned to `_dir`, since the planet sits at `-_dir` from the pod). The old placeholder spun because it recomputed `Quaternion.LookRotation(planet - head)` every frame, which jitters wildly once the pod is meters from the planet center. `_dir` is fixed for the whole flight, so the pod is rock-stable; the player free-looks inside it; the impact shake stays **positional only** (no rotation).
- Position the pod instance at the player each frame (the player is the floating-origin descent anchor) so it rides the descent and the floating-origin rebases.

## Risks / watch-items

- **Visible-from-inside:** confirm every face shows from within (thin boxes for opaque; `Cull Off` glass). If a face is missing, it was authored single-sided facing out.
- **Near clip:** windows at ~1.2–1.5 m are well clear of the default near clip plane; the interior light range stays small (~3 m) so it doesn't leak into the scene.
- **Atmosphere through glass:** verify in Play mode that the planet's atmosphere/ocean shows through the windows (the early-queue glass is exactly for this; the sFuture ship windows already prove it).
- **Performance:** trivial (a dozen primitives, one small light), destroyed after the cinematic.

## Acceptance

On a fresh New Game descent: the player sits inside a recognizable dark industrial pod, sees Humble Abode growing through the floor window and stars through the ceiling window as they fall, can free-look out the 4 side windows, the pod does **not** spin or jitter, and it's cleanly destroyed at the crash with the wake-up handoff unchanged.
