# Audit: Portals / Cutscenes / Concert / Fishing / Dimensions

Read-only static audit, 2026-07-15. Scope: `Assets/3 - Scripts/{Portals,Cutscenes,Concert,Fishing,Dimensions}`.
No files were modified. Line citations are `file:line`.

## Summary

Overall the code is in good shape and largely follows the project conventions in
CLAUDE.md (lazy-refind with cooldowns, `rb.position` writes, `NBodySimulation.Bodies`
instead of per-frame `FindObjectsOfType`, cached cameras, throttled rescans, save
integration for the fishing dex). The heavy hitters — `DeathCutsceneController`,
`ConcertAudioDirector`, `ConcertLightProgram`, `ConcertLaser`, `AudienceMember` —
are careful about allocations and floating origin.

Findings are mostly **low severity**: a couple of missing null-checks, one
space-mixing cosmetic bug, a missing singleton guard, some dead/legacy code, and a
few always-on per-frame costs. No save-order or floating-origin desync bug was found
in the cutscene/portal paths; those paths are notably well-reasoned (PortalManager's
move-player-before-autosave and the async LoadingScreen return are both correct and
documented). Fishing inventory (incl. `fishColor`) is correctly persisted via
`SaveCollector.CaptureFishInventory` / `ApplyFishInventory`.

Counts: **7 bugs** (0 high, 2 medium, 5 low), **6 redundancy/dead-code items**,
**6 performance notes**.

---

## Bugs (severity, file:line, description, fix)

### B1 — MEDIUM — `Fishing/FishingRodController.cs:564` — unguarded `Camera.main`
`SpawnBobber()` reads `Camera.main.transform.forward` with no null check. If the main
camera is momentarily absent (scene transition, a cutscene that disables/retargets the
player camera, or during a portal load), casting throws a `NullReferenceException` and
the bobber never spawns. Only fires on cast, so not per-frame, but it is a hard crash of
the fishing flow.
Fix: cache/guard — `var cam = Camera.main; if (cam == null) return;` and reuse `cam`.

### B2 — MEDIUM — `Cutscenes/DeathCutsceneController.cs:398` — "newest save" assumes `ListSaves()` ordering
`BeginReloadLoad()` reloads `SaveSystem.ListSaves()[0]` as "the newest save," and
`HandleDeath` (line 221) likewise gates on `ListSaves().Count`. This is correct only if
`ListSaves()` is guaranteed sorted newest-first. If that contract ever changes (or an
autosave vs. manual-save timestamp tie), the death cutscene reloads the wrong slot,
silently reverting player progress. This is the one place where an off-by-ordering bug
would be very hard to notice.
Fix: make the ordering explicit here (sort by write time / mtime) rather than trusting
positional `[0]`, or add an assertion/comment tying it to the `ListSaves()` contract.

### B3 — LOW — `Fishing/Bobber.cs:70-71` — bob offset uses world up on a local position
```
float bobOffset = Mathf.Sin(Time.time * frequency) * amplitude;
transform.localPosition = baseLocalPosition + Vector3.up * bobOffset;
```
After the bobber is parented to the planet (`StopOnWater`), `baseLocalPosition` is in the
planet's local space but `Vector3.up` is world up. On any water body away from the
planet's "north pole" the bob is not along the local surface normal, so the float bobs at
a tilt / partially sideways. Cosmetic only.
Fix: convert the planet-radial up into the parent's local space, e.g.
`Vector3 localUp = transform.parent.InverseTransformDirection((transform.position - planet.Position).normalized);`
and offset along that.

### B4 — LOW — `Cutscenes/FlashbackManager.cs:17-20` — singleton has no guard
`Awake()` does `Instance = this;` with no `if (Instance != null && Instance != this)`
guard (CLAUDE.md mandates the guard for every singleton). Two instances in a scene would
let the last one silently win. `OnDestroy` does guard (`if (Instance == this)`), so it
won't null a live Instance, but the pattern is inconsistent with the rest of the codebase.
Fix: add the standard guard.

### B5 — LOW — `Cutscenes/CutsceneController.cs:14` — scaled `Invoke` on a scene-advance
`Invoke(nameof(LoadGameScene), displayDuration)` runs on scaled time. If the Cutscene
scene is ever entered with `Time.timeScale == 0` (e.g., a paused-state carryover), the
scene never advances to gameplay and the player is stuck on the placeholder card. This
scene is `FlashbackManager.nextSceneName = "Cutscene"` and is a **placeholder stub**
("[Flashback sequence — coming soon]"). Low risk today but a latent soft-lock.
Fix: use a `WaitForSecondsRealtime` coroutine, or ensure `Time.timeScale = 1` on entry.

### B6 — LOW — `Concert/ConcertLightProgram.cs` — event handlers run while component disabled
`ConcertStageHub.UpdateStageVisuals` toggles `ConcertLightProgram.Instance.enabled`
(ConcertStageHub.cs:238) so the ~2 ms Update doesn't run when no stage is visible/night.
But the program subscribes to `ConcertAudioDirector`'s C# events (`HandleKick`,
`HandleSnare`, `HandleHihat`, `HandleDrop`, `HandleCrash`, `HandleSting`, buildup/silence)
in `TrySubscribeToDirector` (ConcertLightProgram.cs:530). MonoBehaviour `enabled=false`
does **not** gate delegate invocations, so while "disabled" these handlers still fire,
advancing look indices (`_currentLookInPool` etc.) and mutating `_choreoState`. Harmless
to correctness (Update-side application is skipped) but it is wasted work and means the
look progression keeps churning invisibly, so the first visible frame after re-enable is
at an arbitrary look index.
Fix: early-return in the handlers when `!enabled`/`!isActiveAndEnabled`, or gate them on
the same visible/night flag the hub uses.

### B7 — LOW — `Cutscenes/DeathCutsceneController.cs:180` — Escape double-consumed
`Update()` treats `KeyCode.Escape` (and Space / pad A / Start) as the "skip" input. If the
pause menu (or any other global Escape handler) also processes Escape in the same frame,
the skip and a menu-open can both fire. Not observed to break, but Escape is an
overloaded key; Space / pad-A alone would be safer as the advertised skip.
Fix: drop Escape from the skip set, or consume the input so other handlers don't also see it.

---

## Redundancies / Dead Code

- **`Cutscenes/CutsceneController.cs`** — entire class is a placeholder stub that shows
  "[Flashback sequence — coming soon]" then loads gameplay. Still referenced
  (`FlashbackManager.nextSceneName = "Cutscene"`), so keep, but it is unfinished content,
  not shippable cutscene logic.
- **`Cutscenes/DeathCutsceneController.cs:935`** — `void RevealLine(LineRenderer, Vector3[], float)`
  is defined but never called (only the plural `RevealLines` is used). Dead.
- **`Fishing/FishMarketNPC.cs:33-38`** — a block of `[HideInInspector]` legacy serialized
  fields (`sellButton`, `earningsText`, `commonInfoText`, `commonPlusButton`,
  `browseFishButton`, `fishingRodController`, …) explicitly commented "unused in new
  flow," kept only for scene-serialization compat. Dead weight; safe to prune once the
  scene is re-saved without them.
- **`Fishing/FishingdexManager.cs:305`** — `ShowList()` is an intentional no-op kept for
  external callers; **`ShowDetail(entry, RenderTexture _unused)`** (line 299) ignores its
  second arg. Compat shims, fine to leave, worth a cleanup pass.
- **`Concert/ConcertLightProgram.cs:723`** — `HandleKick()` is an empty event handler kept
  "for future expansions" (kicks no longer advance any look). It's still subscribed, so it
  is a live no-op delegate call on every kick.
- **`Concert/ConcertHaze.cs:124-125`** — sets both `renderer.material` and
  `renderer.sharedMaterial` to the same shared material back-to-back. Redundant
  double-assignment (harmless — the `.material` *setter* doesn't clone — but pointless).
  Same shape worth checking in the other particle emitters (`ConcertFogPuff`).
- **`Concert/ConcertLaser.cs:30-33`** — back-compat `LaserMode` aliases (`FanSweep`,
  `LissajousScan`, `MultiBeamBurst`, `PulseStrobe`) are intentional and documented; not
  dead, just noting they exist.

## Performance / Optimization

- **`Fishing/FishingdexManager.cs:200-213`** — `PopulateList()` renders a fresh 128×128
  `RenderTexture` (`RenderFish`, which `Instantiate`s a fish prefab, `Render()`s, then
  `DestroyImmediate`s) for **every** entry in the lifetime dex, on every open; selection
  then renders a 256×256 detail RT. `FishInventory.AllFish` is a lifetime catch log that
  grows unbounded (`AddFish` never removes on sell — see FishInventory.cs:71-77 comment),
  so dex-open cost scales linearly with total fish ever caught. Consider capping visible
  rows / virtualizing the list, caching row thumbnails (`FishEntry.cachedHotbarPreview`
  already exists for this purpose but the dex re-renders instead of reusing it), or a
  pooled preview RT.
- **`Concert/ConcertStageHub.cs:270-307`** — `ApplyEnemyDamage()` runs an
  enemies × stages nested loop **every frame**, unconditionally, even when no concert is
  active/visible and even with zero night stages. Usually small N, but it is always-on.
  Could early-out when `ConcertLightProgram`/no stage is Full, or throttle.
- **`Concert/ConcertStageHub.cs:177-205`** — `Update` also runs `EnforceSpeakerState` and
  `UpdateStageVisuals` every frame plus `BuildStages` (allocating
  `FindObjectsOfType<SpeakerSource>` + `<AudienceZone>`) every `checkInterval` (3 s). The
  3 s throttle is fine; noting the per-tick allocation.
- **`Concert/ConcertLaser.cs:100,147-148`** — each laser pre-builds a fixed pool of
  `kBeamPoolSize = 48` beams × 2 LineRenderers = 96 GameObjects in `Start`, regardless of
  the tier actually in use (only `MaxBurst` uses 40). With multiple lasers per stage this
  is a large static object/draw-call count. Consider a lazily-grown pool sized to the max
  beam count the assigned modes need.
- **`Concert/AudienceMember.cs:403-701`** — per-member `LateUpdate` does substantial
  quaternion/bone math for up to N members. It is correctly distance-culled
  (`TickDistanceCull`, 100 m, 0.5 s throttle) and skips all bone math when culled, so this
  is acceptable — noted only as the dominant concert-crowd CPU cost when close.
- **Dimension controllers** — many create runtime `Material` instances via
  `DimensionSceneUtil.Mat` (`new Material(Shader.Find("Standard"))`) and never explicitly
  `Destroy` them. Because they are runtime-created (not asset-referenced) they are
  reclaimed on scene unload's `UnloadUnusedAssets`, so no true leak, but a controller that
  rebuilds materials on look-away (e.g. `ShiftingMazeController`) should be spot-checked to
  confirm it reuses cached materials (it does cache `_wallMat` etc. in Awake) rather than
  reallocating per shuffle.

## Notes & Uncertainties

- **PortalManager is solid.** The move-player-onto-cabin-spawn-before-autosave design
  (PortalManager.cs:55-63,103-120) and the async `LoadingScreen.LoadSceneAndShow` return
  path (85-101) are both correct re: floating origin / atmosphere reload, and match the
  documented traps. `MovePlayerToCabinSpawn` correctly writes `rb.position`/`rb.rotation`.
  No bug found; the `static bool _subscribed` + static `sceneLoaded` handler never
  unsubscribes, but that is intentional for a static manager (no leak).
- **DeathCutsceneController** is careful: it correctly frees per-death owned materials
  (`_ownedMats`), releases the RenderTexture, re-subscribes to the scene-placed
  `ResourceManager` on every load (EnsureSubscribed), restores `AudioListener.volume` on
  every exit path incl. `OnDestroy`, and reasserts `TotalDeaths` after reload. The static
  glow/nebula/line textures + additive materials are cached once and intentionally never
  freed. Only B2 (save ordering), B7 (Escape) noted.
- **B2 depends on the `SaveSystem.ListSaves()` contract**, which is outside this audit's
  scope — I did not verify its sort order. If it is documented/guaranteed newest-first,
  B2 downgrades to a comment-only nit.
- **Dimension controllers all follow the lazy-refind convention** (`_player == null &&
  --_playerRefindCooldown <= 0` before `FindObjectOfType<PlayerController>()`, e.g.
  WaitingFieldController.cs:201-205), and use `rb.MovePosition`/`MoveRotation` in
  FixedUpdate for the stalking monolith. The many `GetComponent<Collider>()` hits in the
  grep are all one-time build-time `Destroy(...GetComponent<Collider>())` calls in the
  runtime world-builders, not per-frame — not a concern.
- **DimensionAssetLibrary** getter uses `UnityEditor.AssetDatabase` under
  `#if UNITY_EDITOR` and relies on a Preloaded-Asset `OnEnable` in builds; if that
  preload is not wired, all dimension textures/audio silently fall back to flat colors /
  sine tones (by design). Worth confirming the asset is actually in Preloaded Assets for
  builds, but that's a project-settings check, not a code bug.
- **ConcertAudioDirector.ResolveSource** and the whole director are well-optimized
  (throttled 2 Hz rescan, ring buffers, no per-frame allocations). No issues.
