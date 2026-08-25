# Shuttle Autopilot Travel v1 — Build Plan (awaiting GO)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **GATE:** Per `docs/Handoff_ShuttleAutopilot_Travel_v1.md` protocol, NO code is written until Sam says GO on this plan. Phases 2–4 each get a detailed task expansion (full code, TDD steps) at execution time; the crux code decisions are already locked in here.

**Goal:** Parked shuttle on Humble Abode travels to any other planet via a NAV app on the shuttle computer — automated countdown/liftoff/transit, player-steered hover + assisted landing, players free-walking inside the whole flight. Demo scope: no fuel, no economy, intro untouched.

**Architecture:** Kinematic scripted motion; the shuttle stays Rigidbody-less and stays parented to a `CelestialBody` at all times (depart body → reparent to target body mid-transit), pose recomputed body-relative every physics step. Riders are NOT parented — `PlayerController` gets a small "platform frame" hook set (carry delta + velocity reference + gravity/up override), which is the pattern the reverted Ship carry attempt was reaching for. Host-authoritative state machine replicated by a new `ShuttleSync` named-message singleton in the house style.

**Tech stack:** Unity 2022.3 Built-in RP, NGO named messages over Relay, existing ShuttleComputer canvas, `compile-unity.py` for headless C# checks, pure-C# logic tests where possible.

---

## 1. Verification results — handoff §3 tagged against live code

| Handoff claim | Verdict | Reality |
|---|---|---|
| Intro pod descent — read-only reference | **[EXISTS]** ✅ | Two implementations. `ShuttleArrivalSequence.cs` (on the shuttle prefab root, order 50) is the direct precedent: drives `transform.localPosition` under the planet parent (rebase/orbit-immune by construction), Hermite phase blending with pinned seam velocities, orientation as pure `f(progress)`, and the documented one-frame-stale release recipe. `PodArrivalSequence.cs` shows the world-space `body.Position + dir * dist` form for between-bodies motion. |
| ShuttleComputerTerminal + app framework, "AppGridCols / WireAppGridNav trap" | **[EXISTS, but the trap is WRONG]** ⚠️ | `AppGridCols`/`WireAppGridNav` are in **`PlayerPhoneUI.cs`** — the phone, not the computer. The computer has no tile framework: a `static readonly AppDef[] Apps` row in `ShuttleComputerUI.cs:551` + hand-positioned `BuildHome`. **The real trap:** `BuildHome` wires every enabled tile to a hardcoded `ShowProjects` listener (`:686`) — works only because TRAX is the sole enabled app. NAV needs a per-app dispatch added first. Also: no Unicode glyphs (missing from the SDF atlas) — icons are built in `MakeAppIcon`. |
| ShuttleComputerWorldScreen mirror | **[EXISTS]** ✅ | Each machine rebuilds the same UI from replicated state and renders its own copy to the `ShuttleScreenMirror` RT — nothing visual crosses the wire. NAV must add a per-frame tick inside `DriveMachine()` (`ShuttleComputerWorldScreen.cs:187`) or the mirrored countdown/feed freezes. NAV must not add a sub-canvas (root-canvas-only layer swap). Private camera layers 30/31 are taken. |
| Planet registry + `landable` flag | **[EXISTS / flag MISSING]** | Registry = `NBodySimulation.Bodies` (fixed snapshot from Awake). **No `landable` flag exists.** Filter = `bodyType == Planet && !isStaticAttractor`, mirroring `MushroomSpawner.CanGrowMushroomsOn` — no CelestialBody edit, no inspector work. `CelestialBody.Position` returns `rb.position` (physics frame) — use it for all math. |
| ParentToBodyPhysicsFrame | **[EXISTS]** ✅ | `SpawnerCubeface.ParentToBodyPhysicsFrame(Transform, CelestialBody)` — converts through the body's `rb` pose. Correct for our reparent since all autopilot math uses `body.Position` (also physics frame). One frame convention for the whole feature: **physics frame** (`rb.position`/`rb.rotation`), stated in every file header. |
| Floating origin | **[EXISTS]** ✅ | Shuttle is a plain scene child of Humble Abode, **no Rigidbody anywhere in the prefab** — rebases are a non-event for it as long as it stays parented to a registered body. Never register the shuttle itself; never register children of registered transforms. Rebase soak test = drop `distanceThreshold` on the scene instance. |
| Door lock — "reuse the pod door's lock path" | **[PARTIAL]** ⚠️ | The host-authoritative door is the **stasis pod door** (`StasisPodDoor` + `StasisDoorSync`) — the pod *inside* the shuttle. The exit ramp is `ShuttleExitDoor.cs`: opens once after the film, **no close path, no lock, no sync**. We write the flight close/lock — but it needs no new sync: door state derives from the replicated phase on every machine. |
| Layers | **[EXISTS, richer than assumed]** ⚠️ | Terrain = **Body (10)** (shuttle too). Water = 4, **but ocean waterline triggers sit on Body 10** — every ground cast needs `QueryTriggerInteraction.Ignore`, and water rejection uses `CelestialBodyGenerator.GetOceanRadius()` (call-only; forbidden zone to edit). **Trees, buildings, NPCs, mushrooms, props all share WorldProp (3)** — one blocker mask covers them, but they can't be told apart by layer. **No player layer** (players are Default 0) — player-under-shuttle check is a distance test against the real player + `PlanetRelativeSync.AllPuppets`. Layers 6, 7, 15–29 free. |
| Save — parked pose serializes body-relative | **[MISSING]** ⚠️ | **The shuttle pose is not saved at all today** — it's implicit in the scene hierarchy. `data.ship` is vestigial (`FindMainShip()` typically null). We add `ShuttleSave` from scratch, modeled on `BodyRelativeTransform` + the `ExtraShipSave` sentinel conventions. Apply order is a documented **15-step** list (not 17); shuttle applies at step 7.5 (after ship, before player). |
| MP: "PlanetRelativeSync syncs planet-local pose" | **[EXISTS, but single-planet]** ⚠️ | It's hard-wired to a serialized `planetName = "Humble Abode"` string that never crosses the wire. **Multi-planet travel breaks player puppet sync before the shuttle even exists.** Frame identity must become replicated state. (Scope addition — see §3.) |
| "Puppets of riders are parented to the shuttle" (§4b) | **[WRONG approach]** ⚠️ | `PlaceRemote()` writes the puppet's **world** transform every frame from planet-space — parenting is a no-op. The fix is swapping the reference transform (planet ↔ shuttle) on the replicated phase, and forcing the snap branch (`remoteEverPlaced = false`) at every frame switch so smoothing never lerps across coordinate systems. |
| ShuttleInteriorVolume | **[MISSING]** | The prefab's only large trigger is the loot locker (7×1.4×11 — do not mistake it). Sam places the interior volume. |
| LandingCamera / LandingLamp | **[MISSING as objects; camera SOLVED as code]** | `ShuttleArrivalSequence.StartLandingCam()` already solved the hard parts (real enabled camera at CCTV cadence because manual `Render()` misses instanced space dust; ocean/atmosphere post copied via `CopyCamEffect`). We lift that code into a reusable component. Lamp = `ReactorGlow`-shaped emissive driver. |
| Riders / moving platform | **[MISSING — and the doc's approach won't survive contact]** ⚠️ | No moving-platform support exists anywhere. The player is **never parented** in this codebase; a Ship carry-player attempt was built and **reverted** (its scaffolding remains: `[DefaultExecutionOrder(-50)]`, `_contactingPlayer` written-never-read). Worse: the grounded grip drags `rb.velocity` toward **`referenceBody.velocity`** (the planet) every FixedUpdate, and `IsGrounded()` explicitly refuses non-landed Ships as ground. Parenting the player under the shuttle while this machinery runs would fight it constantly. See §2 for the replacement design. |

Other landmines the research surfaced (none in the handoff):

- **`SolarSystemSync.CarryRiders` will shove an unparented shuttle** near a corrected planet — it exempts only children of `CelestialBody`/`NetworkObject`. Solved structurally: the shuttle **never unparents** (§2).
- **`Physics.autoSyncTransforms` is off project-wide** — any direct `rb.position` write or collider-moving transform write needs an explicit `Physics.SyncTransforms()` before physics queries rely on it.
- Puppets have **all colliders disabled** — the D-1 "who is inside at 0" check cannot use the trigger volume for remote players; it's a shuttle-local box test (`InverseTransformPoint`) against real player + `AllPuppets` positions (same trick `StasisPodDoor` uses for zones).
- **SPACE is TRAX's play toggle and F closes the whole computer** before per-view handlers run — NAV's key handling slots into `ShuttleComputerUI.Update`'s existing precedence ladder; closing during HOVER is fine (doc says hover continues).
- WASD player movement is **already fully suppressed** while the terminal is open (`isInModalSlotUI`) — nothing to add there.

---

## 2. The rider system — design (deviation from handoff §4b, needs Sam's sign-off)

The handoff's primary route ("parent riders to the shuttle, run the controller in shuttle-local space") fights four live systems (never-parented player, planet-velocity grip, Ship-as-ground rejection, world-writing puppet placement) and its named fallback (move the planets instead) touches SolarSystemSync — off-limits. Proposing the third route the research surfaced, which is what the reverted Ship experiment was groping toward:

**Platform frame provider — carry, don't parent.** New static hook on `PlayerController` (editable — it's foundational but not forbidden-zone):

```csharp
// PlayerController (appended fields — end of class, serialization rule)
[System.NonSerialized] public static Transform PlatformFrame;      // shuttle root while riding, else null
[System.NonSerialized] public static Vector3   PlatformVelocity;   // shuttle world-velocity this fixed step
```

Wired in four places, all inside existing seams:

1. **Carry.** `ShuttleAutopilot` runs its kinematic pose write in `FixedUpdate` at `[DefaultExecutionOrder(-50)]` (the Ship's documented slot: commits before PlayerController's FixedUpdate reads `rb.position`), computes the shuttle's pose **delta** for the step, applies that delta to each rider's `rb.position`/rotation (translate + rotate about the shuttle, exactly how `ShuttleArrivalSequence` applies incremental spin to the player), then `Physics.SyncTransforms()` so the moved floor colliders are live before the player's ground/wall queries run.
2. **Velocity frame.** Every `refVel = referenceBody.velocity` site in PlayerController (grip `:984`, ground↔air handoff `:920`, swim `:1262`, jump-up test `:1306`, wall clamp `:1729`, air-control frame `:1030`) reads through one new accessor: `FrameVelocity => PlatformFrame != null ? PlatformVelocity : (referenceBody != null ? referenceBody.velocity : Vector3.zero)`. This is precisely the substitution the air-control block already does for `ShipProximityZoneActive`.
3. **Gravity + up.** While `PlatformFrame != null`: skip the n-body loop, `rb.AddForce(-PlatformFrame.up * shuttleFloorGravity)` (D-7: normal floor gravity the whole flight), set `_lastGravityMag` (buoyancy/grip read it), and `UpOverrideTransform = shuttle` (existing hook, already used by the intro, with `BlendUpOverrideOut(1.5f)` for the handoff back).
4. **Grounding.** The Ship-rejection branch in `IsGrounded()` ignores the shuttle (it isn't a `Ship`, so it already passes — shuttle colliders are on Body 10, inside `walkableMask`). No change needed; verify in the spike.

Riders enter the frame at COUNTDOWN 0 (inside the volume test), leave it on PARKED with the `ReleasePlayer` recipe: reseat from the **live** pose, `Physics.SyncTransforms()`, `SetVelocity(body.velocity)`, `CameraTransformFX.SnapToCurrentPlayer()`, blend the up-override out.

Dropped physics items during flight (v1, per handoff): `PlayerPickup.DropObject` seeds `rb.velocity` from the player — add `PlatformVelocity` when the frame is active, and register dropped-inside items as (non-player) riders so the carry moves them too. Held items already follow the player.

**Why this over parenting:** zero changes to how the controller integrates movement, grounding, walls, camera; the touched sites are exactly the ones that must change under *any* design; and unwinding it is deleting one static and six one-line reads.

**Risk-first spike (handoff phase 2):** debug key drives the shuttle 200 m in a straight line with the player walking inside. If floor-glue jitters, the fallback within the same architecture is pinning riders with per-step `rb.position` writes from shuttle-local anchors captured at step start (heavier but strictly local changes). Nothing else in the plan depends on which variant wins.

---

## 3. Shuttle motion + phases (per handoff §2/§4, adjusted)

- **PARKED:** exactly today — plain child of the body, no script motion, no Rigidbody ever.
- **COUNTDOWN (10 s):** no motion. Timer = replicated `serverTimeAtPhaseStart`, derived locally (SolarSystemSync's timestamp trick) — never replicate the remaining float. At 0: occupancy test (shuttle-local box vs real player + puppets); empty → abort to PLANET LIST (D-1); else riders captured, door closes+locks.
- **LIFTOFF:** drive `localPosition = restLocal + localUp * altitude` under the depart body (ShuttleArrivalSequence form), Hermite ease to ~300 m.
- **TRANSIT:** shuttle **stays parented** (CarryRiders + rebase safety). World pose computed fresh each step: `A(t)` = depart-frame point, `B(t)` = target hover point, both from `body.Position` live; `pose = lerp(A,B, ease(t))`, up slerped between body-up directions. Written as world position while parented (fine — parent is just a frame anchor). At `t = 0.5`: `ParentToBodyPhysicsFrame(shuttle, targetBody)`. Never integrate from last pose; never cache world positions.
- **HOVER:** all math local to target body. Altitude hold via downward ray on Body mask (`QueryTriggerInteraction.Ignore`), critically-damped to 100 m, hold-last over holes; WASD tangential (15 m/s, heavy accel), Q/E yaw, upright = radial out, yaw preserved.
- **LANDING:** Hermite descent to hit + gear offset, 0.5 s settle, then PARKED: re-run `ParentToBodyPhysicsFrame`, release riders (same frame), unlock door, `OnPhaseChanged`.
- **`OnPhaseChanged(phase)` event** on the autopilot — HUD, door, NAV, lamp, world screen, rider frame, puppet frame switch all subscribe. Host advances phases; guests are `ClientDriven` (StasisPodDoor's kill-switch pattern) and only render.

**Landing validity (host, 10 Hz, one replicated bool):** 9 rays (centre + ring at footprint radius ~6 m — Sam confirms) on Body mask; all must hit; per-ray slope ≤ 12°; distance spread ≤ 1.5 m; hit radius ≥ ocean radius (`GetOceanRadius`, resolved once per body); OverlapSphere on WorldProp(3) empty (covers trees/buildings/NPCs/props in one mask); no player (real or puppet) within footprint by distance. Green lamp + screen border from the bool.

---

## 4. File list

**Create — `Assets/3 - Scripts/Shuttle/` (new folder):**

| File | Responsibility |
|---|---|
| `ShuttleAutopilot.cs` | Phase state machine + all kinematic motion, `[DefaultExecutionOrder(-50)]`, `OnPhaseChanged`, debug-key leg trigger (phase 3), host-only advancement. Frame convention header comment. |
| `ShuttleRiderFrame.cs` | Occupancy test (shuttle-local box), rider capture/release, per-step carry of rider rigidbodies + dropped items, release recipe. |
| `ShuttleLandingSensor.cs` | 9-ray + overlap validity check (pure-logic core split out for tests), 10 Hz host tick. |
| `ShuttleLandingCamera.cs` | Runtime camera → RT (lifted from `StartLandingCam`, incl. CCTV cadence + `CopyCamEffect`), created on HOVER, torn down on PARKED. |
| `LandingLamp.cs` | ReactorGlow-shaped emissive green/red/off by phase. |

**Create — elsewhere:**

| File | Responsibility |
|---|---|
| `Assets/3 - Scripts/Music/ShuttleComputerNavUI.cs` | `partial class ShuttleComputerUI` — NAV tile, 5 views, hover input reading inside the existing `Update` key ladder, RawImage feed. |
| `Assets/3 - Scripts/Multiplayer/ShuttleSync.cs` | House-pattern named-message singleton: `KindRequestState / KindPhase (reliable, on-change + 2 s heartbeat, carries phase + targetBody + serverTimeAtPhaseStart + pilotClientId) / KindPose (10 Hz unreliable, frame name + local pose + transitProgress) / KindValid / KindTravelRequest / KindPilotInput (~30 Hz unreliable absolute axes, host verifies sender == pilot, decays on 0.5 s silence) / KindLand (reliable one-shot)`. |
| `docs/PLAYTEST_ShuttleAutopilot_v1.md` | Sam's editor checklist (handoff §8). |
| `prototypes/shuttle-autopilot/` pure-C# tests | Validity-check cases (flat/slope/water/ridge/NPC), transit-blend fresh-evaluation test, Hermite seams — run via `compile-unity.py` pattern. |

**Modify:**

| File | Change |
|---|---|
| `Scripts/Game/Controllers/PlayerController.cs` | `PlatformFrame`/`PlatformVelocity` statics (appended), `FrameVelocity` accessor threaded through the 6 refVel sites, gravity-skip + floor-gravity branch. |
| `Assets/3 - Scripts/Tutorial/ShuttleExitDoor.cs` | `CloseForFlight()` / reopen; driven purely by `OnPhaseChanged` (no new sync — phase is the truth). Keep the existing "auto-open on any load path" pattern for PARKED saves. |
| `Assets/3 - Scripts/Music/ShuttleComputerUI.cs` | `AppDef` gets a click dispatch (fixes the hardcoded `ShowProjects`), NAV row entry, `MakeAppIcon` case. |
| `Assets/3 - Scripts/Music/ShuttleComputerCoopUI.cs` | `ReadScreen`/`ApplyScreen`/`CanvasViewId` NAV branches. |
| `Assets/3 - Scripts/Music/ShuttleComputerWorldScreen.cs` | NAV per-frame tick in `DriveMachine()`. |
| `Assets/3 - Scripts/Multiplayer/TraxSessionSync.cs` | `ViewNav = 5`, `Screen` fields (`navView`, `navTarget`) + `Same()` + writer/reader/relay in lockstep order. |
| `Assets/3 - Scripts/Multiplayer/PlanetRelativeSync.cs` | `ReferenceFrame` refactor: one property behind publish/place/flashlight/`TryGetCurrentLocalPose`; frame switches on replicated shuttle phase; snap-branch force on every switch. **(Required collateral: player sync is single-planet today.)** |
| `Assets/3 - Scripts/Pickups/PlayerPickup.cs` | Drop velocity += `PlatformVelocity` when riding. |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | `ShuttleSave { bodyName, localPos, localRot, doorOpen }` — absent-in-old-saves ⇒ empty bodyName ⇒ "leave the scene-authored pose", the correct pre-feature fallback. Always captured PARKED (travel state never saves; pod is the only save point and you can't reach it mid-flight — verify in plan review). |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | `CaptureShuttle` (physics-frame local pose) + `ApplyShuttle` at step 7.5; update the numbered doc-comment; `ApplyWorldSubset` gets the required comment: shuttle pose reaches guests via ShuttleSync live state (EnemySync route), not the join snapshot. |
| `Assets/3 - Scripts/SaveSystem/NewGameReset.cs` | Reset phase/target/pilot statics; re-anchor shuttle to authored home. |

**Not touched:** intro flow, `ShuttleArrivalSequence` (read-only donor), SolarSystemSync, NBodySimulation, EndlessManager, anything in the celestial forbidden zone, `Shuttle_Lander.prefab` regeneration (any prefab edit goes through the `LoadPrefabContents` patch pattern — but see §5, Sam places instead). No FeatureVault flag (D-5 spirit: no pre-built seams; branch is the gate).

---

## 5. Sam places (before phase 2 wiring)

1. **`ShuttleInteriorVolume`** — trigger BoxCollider child of `Shuttle_Lander` covering the walkable interior (used for occupancy math; puppets are tested by position, not trigger).
2. **`LandingLamp`** — small emissive mesh on/near the console (script drives material index; tell me which submesh).
3. Confirm **footprint radius** (~6 m per handoff) for the validity ring.

Landing camera needs no placement (runtime-created, like the intro's).

---

## 6. Phase order (mirrors handoff §10)

1. ~~Plan~~ → **GO from Sam** ← you are here.
2. **Rider spike** — `ShuttleRiderFrame` + PlayerController hooks + debug key: shuttle translates 200 m with player walking inside. Soak: forced rebases (tiny `distanceThreshold`), jump mid-move, drop an item. *Kill criterion: if floor glue isn't solid here, stop and re-plan §2's fallback with Sam.*
3. **`ShuttleAutopilot`** — full PARKED→…→PARKED loop single-player on a debug key, no UI. Transit-blend + Hermite logic extracted pure and tested first (TDD via compile-unity.py runner).
4. **Validity + lamp + landing camera.** Sensor logic pure-tested (flat ✓ / 20° ✗ / water ✗ / 3 m ridge ✗ / blocker ✗) before scene wiring.
5. **NAV app** — 5 views through the mirror, input ladder, door lock, abort path (D-1). Placeholder text only (`[AUTHOR]` stays Sam's).
6. **Multiplayer** — ShuttleSync, screen-state extension, PlanetRelativeSync frame refactor, pilot lease (D-3), guest input stream, rider puppet frame switch.
7. **Tests + `docs/PLAYTEST_ShuttleAutopilot_v1.md`** — save round-trip (park on another planet → save → reload), four-leg checklist, co-op leg.

Each phase compiles (`python prototypes/shuttle-computer/test/compile-unity.py`) and commits (files **and** .metas) before the next. Ship 2–4 before 5, per the handoff.

---

## 7. [OPEN] — needs Sam's word at GO

1. **Rider design deviation** (§2): carry-don't-parent via PlayerController platform hooks, instead of §4b's parenting. This edits `PlayerController` (foundational, allowed) at six velocity-frame sites plus a gravity branch. OK?
2. **PlanetRelativeSync frame refactor is in scope** (§4): without it, guests' puppets break the moment anyone is off Humble Abode — travel makes a latent bug live. It's ~4 call sites behind one property. OK?
3. **Landable set** = every `bodyType == Planet && !isStaticAttractor` body — eyeball the resulting list in the NAV grid on first run; if any planet should be excluded, we add the `bool landable` inspector flag then (one-line filter change).
4. **Mid-flight saving:** pod is the only save point and it's inside the shuttle. Plan assumes stasis-pod entry is simply unavailable during flight phases (interactable gated on PARKED) so a save is always a parked save. OK?
