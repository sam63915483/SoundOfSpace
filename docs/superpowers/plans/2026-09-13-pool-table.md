# Pool Table Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A playable low-poly pool table near Shlawg's Bar: F takes the camera behind the cue ball, keys aim, hold-LMB power, our own 2D ball sim.

**Architecture:** `PoolPhysics2D` (Unity-free sim, headless tests) → `PoolTable` (Interactable on the prefab root, drives ball visuals from the sim) → `PoolShotSession` (borrows the real camera the SolarMap way, aim/charge/strike, cue + guide) → `PoolShotHUD` (bar + hint). `Editor/PoolTableBuilder` builds the prefab from generated flat-shaded meshes and places it in the village.

**Tech Stack:** Unity 2022.3 built-in RP, C#; headless C# compile via the `dotnet csc` recipe in `prototypes/shuttle-computer/test/verify-fishing.py`; scene edits through the coplay editor bridge (edit mode only — Sam plays).

Spec: `docs/superpowers/specs/2026-09-13-pool-table-design.md`.

---

## Files

| File | Responsibility |
|---|---|
| `Assets/3 - Scripts/Pool/PoolPhysics2D.cs` | Sim: rack, step, strike, pockets, cushions, cast. No `using UnityEngine`. |
| `prototypes/pool/test/PoolSimTests.cs` + `verify-pool.py` | Headless tests of the sim. |
| `Assets/3 - Scripts/Pool/PoolTable.cs` | Interactable root: steps sim, moves balls, pocket drops, respawn, rack; public API for the session. |
| `Assets/3 - Scripts/Pool/PoolShotSession.cs` | The F-mode: camera borrow, input, cue, charge/strike, guide. `static IsActive`. |
| `Assets/3 - Scripts/Pool/PoolShotHUD.cs` | Power bar + hint line on its own canvas. |
| `Assets/3 - Scripts/Editor/PoolTableBuilder.cs` | Menu: build meshes/textures/materials/prefab; place near House_03. |
| `PlayerController.cs:726,807` | `|| PoolShotSession.IsActive` on the look and move gates. |
| `Camera/CameraTransformFX.cs:106` | `if (PoolShotSession.IsActive) return;` |
| `Editor/MeshCombineTool.cs:~410` | `if (t.GetComponent<PoolTable>() != null) return;` |

---

### Task 1: PoolPhysics2D + headless tests

- [ ] Write `prototypes/pool/test/PoolSimTests.cs` (a `Main` that runs named checks and prints PASS/FAIL, like `FishingTests.cs`): rack has no overlaps and 16 active balls; a 9 m/s +x break settles (`AllStopped`) within 60 s of sim time, every ball inside the rectangle, no overlaps; a head-on 1 m/s hit on a stationary ball leaves the cue ball < 0.15 m/s and the target > 0.85 m/s; a ball fired straight at the +x+y corner pocket is pocketed; `CastCueBall` along +x from the head spot reports ball index of the apex ball and a contact centre one diameter short of it.
- [ ] Write `prototypes/pool/test/verify-pool.py` (copy of `verify-fishing.py` pointed at `Assets/3 - Scripts/Pool/PoolPhysics2D.cs` + the test file). Run: `py -3 prototypes/pool/test/verify-pool.py` → expected: compile error (class missing).
- [ ] Write `PoolPhysics2D` — API:
  ```csharp
  public sealed class PoolPhysics2D {
      public const int BallCount = 16; public const int Cue = 0;
      public float HalfLength = 0.99f, HalfWidth = 0.495f, BallRadius = 0.028575f;
      public float CornerPocketRadius = 0.062f, SidePocketRadius = 0.058f, JawHalfWidth = 0.075f;
      public float RollingDecel = 0.55f, LinearDrag = 0.12f, StopSpeed = 0.012f;
      public float BallRestitution = 0.96f, CushionRestitution = 0.78f;
      public const float StepDt = 1f/240f;
      public float[] X, Y, VX, VY; public bool[] Active;
      public float HeadSpotX => -HalfLength*0.5f; public float FootSpotX => HalfLength*0.5f;
      public (float x, float y)[] Pockets;         // 6, built from the dims
      public event System.Action<int,int> BallPocketed; public event System.Action<int,int,float> BallsCollided; public event System.Action<int,float> CushionHit;
      public void Rack(); public void Advance(float seconds); public void Strike(float dirX, float dirY, float speed);
      public bool AllStopped { get; } public bool RespawnCue(); // true if placed
      public bool CastCueBall(float dirX, float dirY, out float cx, out float cy, out int hitBall, out float outX, out float outY);
  }
  ```
  Uses `System.Math` only. `Advance` accumulates into `StepDt` substeps (cap 0.1 s per call).
- [ ] Run the tests → all PASS. Also `py -3 prototypes/shuttle-computer/test/compile-unity.py` → OK.
- [ ] Commit: `feat(pool): PoolPhysics2D ball sim + headless tests`.

### Task 2: PoolTableBuilder — prefab (meshes, textures, materials)

- [ ] Write `Editor/PoolTableBuilder.cs` with a small `FlatMesh` helper (`AddQuad(a,b,c,d)`, `AddBox(center,size)`, `AddPrism(profile2D, from, to)`, `AddLathe(profile, sides)`, `Build(name)` → Mesh with per-face verts) and `MakeBallTexture(n)` (256×128, solids/stripes/number discs via a 3×5 digit font).
- [ ] Menu `Tools/Pool/Build Pool Table Prefab`: bed, 6 cushions with jaw-angled ends, rails with 18 sights, 6 pocket cups, apron, 4 tapered legs, 16 balls (UV sphere 18×12, smooth), cue stick lathe. Saves meshes/textures under `Assets/1 - samsPrefabs/Pool/`, materials under `Assets/2 - Materials/Pool/`, prefab `Assets/1 - samsPrefabs/Pool/PoolTable.prefab` with `PoolTable` + `PoolShotSession` components wired (ball/pocket transforms, cue, felt height, guide LineRenderers). Root `BoxCollider` over rails + bed.
- [ ] Run the menu item through the coplay bridge (`EditorApplication.ExecuteMenuItem`), render the prefab with a temp camera to PNG (the RenderSigns pattern) and look at it. Iterate on proportions until it reads as a pool table.
- [ ] Commit: `feat(pool): PoolTableBuilder — low-poly table, balls, cue prefab`.

### Task 3: PoolTable runtime

- [ ] Write `PoolTable : Interactable`: fields (end of class): `Transform[] balls; Transform[] pockets; float feltY; Transform cue; LineRenderer guide, ring, stub; float rollingDecel…`. `Awake`: build `PoolPhysics2D` from the fields, `Rack()`, add trigger sphere if absent. `Update`: `base.Update()`, `sim.Advance(dt)`, place/roll balls, tick pocket-drop animations, respawn cue when settled. `CanInteract()` → `!PoolShotSession.IsActive`. `Interact()` → `session.Open()`. Public: `Sim`, `CueBallLocal`, `ReRack()`, `IsSettled`.
- [ ] Compile headless → OK. Commit `feat(pool): PoolTable runtime (sim → visuals, pockets, respawn, rack)`.

### Task 4: PoolShotSession + HUD + three edits

- [ ] `PoolShotHUD`: overlay canvas (sorting 60), bar 190×6 at −86 px, brackets, amber, `SetCharge(float01, bool visible)`, `SetHint(string)`.
- [ ] `PoolShotSession` (order 210): `Open()`, `Close()`, `Setup/Teardown` mirroring `SolarMap` (camera borrow, EndlessManager register, `CameraTransformFX` off, HUD hidden, astronaut body shown, `PlayerController` gates). State machine per spec; `LateUpdate` pose from live anchors; input table per spec; cue placement in table-local; charge/strike; guide via `CastCueBall`.
- [ ] Edits: `PlayerController` look/move gates, `CameraTransformFX` bail, `MeshCombineTool` exclusion.
- [ ] Compile headless (editor + player) → OK, zero new warnings. Commit `feat(pool): shot session — camera behind the cue ball, key aim, hold-to-charge strike, guide, HUD`.

### Task 5: Place in the scene, docs, hand over

- [ ] Menu `Tools/Pool/Place Pool Table near Shlawg's Bar`: instantiate under `…/Humble Abode/TOWN-VILLAGE`, 7 m from House_03 on the door side, stand on local up (VendorPlacement recipe), face the house. Run it via the bridge; Sam saves the scene (ONLY SAM SAVES — announce it).
- [ ] `docs/CURRENT_STATE_AUDIT.md` addendum (one paragraph: what exists, files, how to rebuild the prefab), memory note.
- [ ] Commit `scene tools + docs`. Hand Sam the playtest checklist from the spec.
