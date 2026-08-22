# Village doors — openable house doors at Humble Abode

**Date:** 2026-08-22
**Branch:** `feat/helmet-hud`
**Status:** design ratified, implementation pending

---

## Goal

Walk up to a house in the Humble Abode village, look at its door, press **F** —
the door swings open and you can walk inside. Press F again to shut it.

---

## Verified starting state

Everything below was confirmed against the live assets before designing, not
assumed.

### The doors are real, swingable, and already hinged correctly

Every house prefab in `Assets/LowPolyFantasyVillage/Prefabs/Houses/`
(`House_01`…`House_07`, `Mill_01`, `Church`) contains a **nested prefab
instance** of `DoorPart_01` or `DoorPart_02` — a door leaf with its own
non-convex `MeshCollider`, plus two `DoorHandle_01` children.

The leaf's mesh spans local **x 0 → ~1.0** with the handles at **x ≈ 0.946**, so
the transform pivot already sits **on the hinge edge**. Rotating `localRotation`
about **local Y** swings the door correctly. No re-rigging, no pivot fixing.

### The houses genuinely have interiors

`House_01.fbx` was parsed and ray-tested offline (Möller–Trumbore against 9,815
triangles):

- The front wall carries a real doorway hole, roughly **0.9 m wide × 1.9 m
  tall**, at the exact position the door leaf occupies.
- Walls are **~8 cm solid volumes** (two surfaces per ray crossing), so interiors
  render rather than showing backface-culled nothing.
- Windows are real holes too (the glass is separate geometry).
- The house body's `MeshCollider` is **non-convex**, so the doorway is walkable
  the moment the leaf moves out of it.
- There is an upper floor at y ≈ 2.63 m and no ground-floor mesh — the planet
  terrain is the floor.

Conclusion: nothing about the geometry blocks entry. Only the door does.

### The mesh combine is the one real blocker

All ten placed buildings are direct children of
`--- Celestial --- / Body Simulation / Humble Abode / TOWN-VILLAGE`:

| Instance | Source prefab |
|---|---|
| House_01, House_01 (1), House_01 (2) | `House_01` |
| House_02, House_03, House_05, House_06, House_07 | respective |
| Mill_01, Mill_01 (1) | `Mill_01` |

`TOWN-VILLAGE` has a `__CombinedMeshes` child (one of 20 combined clusters in
the scene). `MeshCombineTool` baked the door leaves **and their handles** into
that combined mesh and set `m_Enabled: 0` on their own `MeshRenderer`s —
confirmed in the scene YAML: the `House_01` instance carries ten `m_Enabled: 0`
modifications, four of which target the nested `DoorPart_01` / `DoorRoof_01`
renderers.

**So rotating a door today moves its collider while a ghost door stays welded
in the doorway.** That is the whole problem, and it is what must be undone.

The combine tool is non-destructive (it only disables renderers and parents its
output under `__CombinedMeshes`) and ships a Revert command, so this is fixable
by re-baking rather than by surgery on baked vertex data.

---

## Decisions

| Question | Decision |
|---|---|
| Where the script lives | On the shared `DoorPart_01` / `DoorPart_02` prefabs — placed houses and future ones both work |
| Behaviour | F toggles; swings **away** from the player; no auto-close |
| Co-op | Replicated, mirroring the `StasisDoorSync` transport |
| Save | **Not** saved — doors reset closed on load. `SaveCollector` untouched |
| Un-baking | Targeted: re-bake only clusters that contain doors |

---

## Components

### `Assets/3 - Scripts/World/VillageDoor.cs`

Subclasses `Interactable`, inheriting the entire F-prompt stack: the gaze gate,
`GameUI.ShowInteractionPrompt` ownership, controller-X parity, prompt clearing
on exit.

- **Prompt** — overrides `BuildInteractMessage()` to return
  `"Press {PromptGlyphs.Interact} to open door"` / `"…to close door"`, so it
  tracks the live input source.
- **Hinge** — `Awake` caches the authored `localRotation` as *closed*. The swing
  is a rotation about **local Y**, exposed as a serialized axis enum in case a
  door is ever authored differently. Local-axis rotation is inherently
  orientation-agnostic, which matters because these houses sit at arbitrary
  rotations on a sphere.
- **Direction** — on interact,
  `side = sign(transform.InverseTransformPoint(playerPos).z)`; the open target is
  `-90° × side`. The door swings away from whoever opened it, so it cannot shove
  them.
- **Motion** — `Update()` (calling `base.Update()`) eases toward the target angle
  over ~0.45 s. The collider stays enabled throughout.
- **Gaze** — the door's own `MeshCollider` is the gaze target. `InteractGaze`
  sphere-casts against real colliders and ignores triggers, so this works
  unmodified.
- **Trigger zone** — a `SphereCollider(isTrigger, r ≈ 2.0)` added to the DoorPart
  prefab by the setup command. House instances are scaled 1.3, giving ≈ 2.6 m
  reach.
- **`DoorId`** — FNV-1a hash of the hierarchy path. Deliberately **not**
  `string.GetHashCode`, which is randomised per process on .NET Core and would
  make host and guest disagree about which door is which.
- **Self-diagnosis** — if a door finds its own `MeshRenderer.enabled == false` at
  `Start`, it logs once naming the un-bake command, so the ghost-door failure
  mode is legible instead of mysterious.
- **Audio** — optional open/close `AudioClip`s played at an **explicit volume**
  (the `MoonBaseDoor` lesson: the no-arg `PlayClipAtPoint` overload is 1.0 at
  500 m rolloff).
- Live instances tracked in a static `AllDoors` list via `OnEnable`/`OnDisable`,
  per the repo convention.

### `Assets/3 - Scripts/Multiplayer/VillageDoorSync.cs`

Uses `StasisDoorSync`'s named-message transport, but is far simpler: a village
door has no timers and no autonomous state machine, so there is nothing for two
machines to drift on.

- Auto-singleton via `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`, gated on
  `FeatureVault.Multiplayer`, and **deliberately does not skip MainMenu** — the
  same dodge `WorldSync` / `StorageSync` / `EnemySync` use to sidestep CLAUDE.md
  trap #1. No `EnsureGameplaySingletons` edit is required.
- Messages: `KindRequest` (client → host: doorId, wantOpen) ·
  `KindState` (host → all: doorId, isOpen) · `KindSnapshot` (host → all: every
  door's state, on a ~2 s tick and on client connect, so late joiners and dropped
  packets self-correct).
- The presser swings **immediately** on their own screen and sends the request;
  the host's broadcast then confirms or corrects. State is sent as an
  **absolute** open/closed value, never a toggle, so a duplicated message cannot
  invert a door.
- Inert in single player.

### `Assets/3 - Scripts/Editor/MeshCombineTool.cs` (edited)

One rule added to `CollectEligible`, beside the existing `_Placed` guard:

```csharp
if (t.GetComponent<VillageDoor>() != null) return;   // whole subtree: leaf + handles
if (t.name.StartsWith("DoorPart")) return;           // belt-and-braces, un-stamped doors
```

Returning (rather than skipping one renderer) excludes the whole subtree, so the
handles stay with their leaf. Cost: the ten doors become their own renderers
again — roughly 20 draw calls against the ~3.5 k the tool exists to fight.

The class summary gains a line documenting doors alongside `_Placed`.

### `Assets/3 - Scripts/Editor/VillageDoorSetup.cs`

Menu command **`Tools ▸ Optimize ▸ Un-bake Village Doors`**. One click, run once.

1. **Stamp.** Patch `DoorPart_01.prefab` and `DoorPart_02.prefab` via
   `PrefabUtility.LoadPrefabContents` → add `VillageDoor` + the trigger sphere if
   absent → `SaveAsPrefabAsset` → `UnloadPrefabContents`. This is the repo's
   established safe prefab-patch route (the Shuttle_Lander lesson: never
   regenerate a prefab). Idempotent. All ten placed instances inherit the added
   component automatically, since none of them removes components.
2. **Re-bake.** Find cluster roots that contain both a `VillageDoor` and a
   `__CombinedMeshes` — here, exactly `TOWN-VILLAGE` — and for each run
   revert → re-combine. **Only clusters containing doors are touched**; the other
   19 combined clusters on the planet are untouched.
3. **Report.** Log doors stamped, clusters re-baked, draw-call delta.

Ordering is load-bearing — stamping must precede re-baking or the new skip rule
has nothing to match — which is why it is one command rather than two.

---

## Out of scope

- **No save fields.** Doors reset to closed on load. `SaveCollector`'s fragile
  apply-order is untouched.
- **No interior work** — no dressing, lighting, or grass-blocking inside the
  houses. The interiors are bare shells with an upper floor at y ≈ 2.6 m and no
  stairs.
- **`Church.prefab`** has an unrelated uncommitted change in the working tree
  (Unity re-serialised it and dropped two `MeshCollider`s). The Church is not
  placed in the scene, so it does not affect this work. Flagged, not fixed.

---

## Verification

- `python prototypes/shuttle-computer/test/compile-unity.py` compile-checks both
  assemblies. No claim of "it compiles" is made without that output.
- Then: Sam runs the menu command once in the Editor and playtests — walk to a
  village house, aim at the door, press F, walk in, press F to shut it behind
  you.

---

## Risks

| Risk | Assessment |
|---|---|
| Rotating a non-convex `MeshCollider` at runtime | Supported in Unity 2022 PhysX. Ten doors, and only during a 0.45 s swing. |
| Re-bake alters `TOWN-VILLAGE`'s combined mesh | Fully reversible via the existing Revert command. |
| Draw-call regression | ~20 calls. Negligible against the village's ~3.5 k. |
| Player caught in the swing arc | Mitigated by swinging away from the opener; PhysX push-out is the fallback. |
