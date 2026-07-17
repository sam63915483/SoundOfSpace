# Performance fixes — 2026-07-15 (branch: feat/helmet-hud)

10 performance fixes applied from the audit, chosen for **biggest real-world GC/CPU
reduction during normal gameplay** and **low risk of behaviour change**. All are
allocation/scan eliminations — none change what the game *does*, only how much
garbage it makes or how many scene scans it runs.

> ⚠️ **Not yet compiled or play-tested.** Unity was not running, and this repo has
> no CLI build (Editor-only). These are careful static edits verified against live
> source. Let the Editor compile them and do a quick play-test. If anything looks
> off, `git diff` shows every change; this file explains each one.

Every fix is behaviour-preserving by design; the "why it's safe" line for each says
why the output can't change.

---

## 1. NPC talk-prompt string allocated every frame (6 files)
**Files:** NPCDialogue, BonfireNPCDialogue, RandomAlienDialogue, GuitarShopNPC, TevDialogue, ShipInstructorDialogue
**Bug:** each NPC's `Update()` ran `InteractPromptUI.Show(this, $"Press {glyph} to talk")`
every frame while the player was in range — the `$"..."` interpolation allocates
~1.2 KB/frame *per NPC*, even when nothing changed. Several NPCs in a village = steady
multi-KB/frame GC churn (→ stutter from GC spikes).
**Fix:** cached the built string in `_promptCached`, rebuilt only when the input source
flips (KBM ↔ controller, which is when the F↔X glyph changes). This is the **exact
pattern already shipping in `BonfireInteraction.cs`** — I propagated it, didn't invent it.
**Why safe:** the visible string is identical; it's just not re-interpolated when the
glyph hasn't changed. The one-shot `Show` calls in trigger/enter handlers were left alone.

## 2. Vendor talk-prompt string allocated every frame (2 files)
**Files:** Alien7Vendor, ShipMarketNPC
**Bug/Fix/Safe:** identical to #1 — same per-frame `$"..."` prompt, same cache fix.

## 3. Hotbar `GetComponent<CanvasGroup>()` every frame
**File:** UI/Hotbar.cs (`Refresh`)
**Bug:** `canvas.GetComponent<CanvasGroup>().alpha = …` ran every frame (the hotbar
refreshes each frame, always on screen). `GetComponent` per frame against convention.
**Fix:** cache the `CanvasGroup` in `_canvasGroup` at build time (it's created in `BuildUI`);
null-safe lazy fallback kept.
**Why safe:** same component, just looked up once instead of every frame.

## 4. `Ship.RelativeVelocity` scans all bodies on every read
**File:** Scripts/Game/Controllers/Ship.cs
**Bug:** the property did an O(bodies) nearest-body search on *every get*, and multiple
speed-driven FX (SpeedLinesOverlay, RadialMotionBlurEffect, FOV) read it each frame →
O(bodies × readers)/frame during flight.
**Fix:** cache the result once per frame (`Time.frameCount` guard); all readers in a
frame share one scan.
**Why safe:** within a single frame the inputs (rb velocity, body positions) don't move,
and this value only drives visual FX, not physics. Values are identical.

## 5. `InteractGaze` allocates component arrays every frame
**File:** UI/InteractGaze.cs
**Bug:** `IsLookingAt` runs every frame on the current prompt owner. When you're in range
of an interactable but not aimed dead-center at it (common while walking near a chest/NPC),
it hit `HasSolidCollider` → `GetComponentsInChildren<Collider>()`, allocating a fresh array
every frame. `TryGetVisualBounds`/`AimCenter`/`AimRayHit` had the same pattern.
**Fix:** converted the four helpers to the non-alloc `GetComponentsInChildren(List<T>)`
overloads with reusable static buffers. The per-frame SphereCast was already frame-guarded
(untouched).
**Why safe:** same components enumerated in the same order; only the storage is reused.
Buffers are used synchronously within each call (no re-entrancy).

## 6. `AlienNPCDamageable` spawns a GameObject + AudioSource per hit
**File:** Combat/AlienNPCDamageable.cs
**Bug:** every bullet/axe hit and every death created a `new GameObject` + `AudioSource`,
then `Destroy`d it after the clip — a stream of alloc/teardown churn in a firefight.
**Fix:** one shared, persistent 2D `AudioSource` reused via `PlayOneShot` (which mixes
overlapping hits onto one source).
**Why safe:** still a 2D full-volume one-shot at the same volume; `PlayOneShot` supports
concurrent overlapping sounds, so rapid hits still all play.

## 7. `SpitProjectile` leaks a Material + `Shader.Find` per shot
**File:** Combat/SpitProjectile.cs
**Bug:** each spit did `new Material(Shader.Find("Standard"))` and assigned it via
`.material` — an explicitly-assigned material isn't auto-destroyed with the GameObject,
so every spit leaked one material (plus a string-keyed `Shader.Find` per shot).
**Fix:** one cached static material (all spits are the same green) assigned via
`sharedMaterial`.
**Why safe:** the material is never mutated per-instance, so sharing renders identically;
no more per-shot shader lookup or leak.

## 8. Storage / fish-staging panels re-scan the scene every frame (2 files)
**Files:** UI/StorageUI.cs, UI/FishStagingUI.cs (twins)
**Bug (two parts):**
 (a) `Update` called `Ship.FindPilotedShip()` every tick while open → `FindObjectsOfType<Ship>(true)` (array alloc + full scan) every frame.
 (b) `ResolveIcon(id)` did `Resources.Load` + `FindObjectOfType<Controller>(true)` **per tool slot per frame** (up to ~27 slots) — a full inactive-inclusive scene scan per tool per frame while the panel was open.
**Fix:** (a) use the cached `Ship.AnyShipPiloted` static (semantically exact — it's
`pilotedInstance != null && shipIsPiloted`, matching `IsPiloted`). (b) cache each id's
sprite in a static dictionary once it resolves non-null.
**Why safe:** (a) same condition, cached instead of scanned. (b) icon sprites are
session-stable assets; caching returns the identical sprite. Only affects frames while a
storage/staging panel is open.

## 9. `CompassHUD` tag-waypoint calls `FindWithTag` every frame
**File:** UI/CompassHUD.cs (`AddWaypointByTag`)
**Bug:** the position-provider closure ran `GameObject.FindWithTag(sourceTag)` every frame,
per active tag-waypoint (evaluated in `LateUpdate`).
**Fix:** cache the resolved Transform in the closure; re-find only when it goes null
(target destroyed/respawned) — the repo's "cache once, lazy-refind if null" convention.
**Why safe:** returns the same Transform's position; only re-resolves when the target is gone.

## 10. `OceanMaskRenderer` allocates + LEAKS a Material every post-process frame
**File:** Scripts/Post Processing/OceanMaskRenderer.cs  *(shading-adjacent — see note)*
**Bug:** `RenderOceanMask` (subscribed to `onPostProcessingBegin`, runs every frame ocean
is on screen) did `new Material(oceanMaskShader)` every frame and never destroyed it →
a leaked material + full material alloc *per frame*. The single biggest per-frame alloc
found in the audit.
**Fix:** cache the material (and the sphere `Vector4[]`), rebuilt **only** when the
ocean-body count changes; destroy it in `OnDestroy`.
**Why safe:** the same shader with the same per-frame `numSpheres`/`spheres` uniforms
produces an identical blit. Handled the one subtlety — `Material.SetVectorArray` locks its
length on first set, so I rebuild the material if the body count ever changes (it's
effectively constant in-game). **This file is ocean-shading-adjacent** (though outside the
CLAUDE.md forbidden `Planet Effects/` folder); the change touches only material *lifetime*,
never shader logic or generation code. If the ocean ever renders wrong after this, this is
the first fix to revert — but reusing a material cannot change rendering output.

---

## What was deliberately NOT touched
- Nothing in the forbidden atmosphere/planet-generation zone (`Celestial/`,
  `Atmosphere.cs`, `Planet Effects/`, shaders). OceanMaskRenderer (#10) is outside that
  folder and only its material lifetime was changed.
- Correctness bugs from the audit (BuildMenuLock dead, save held-item loss, NBody stale-body
  crash, the AI deleted-model crash path) — those are *behaviour* fixes, not perf, and were
  out of scope for this pass. They remain in the audit backlog.

## Files changed
16 source files (listed above via `git status`). No new files except this doc.
No commit was made — left in the working tree for you to review/compile first.
