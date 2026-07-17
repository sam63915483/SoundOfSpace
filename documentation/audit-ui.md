# Audit: UI & Hotbar

Read-only audit of `Assets/3 - Scripts/UI/` (Hotbar, InteractPromptUI, helmet HUD,
phone tablet, pause menus, HUD widgets, storage/photos). No files were modified.

## Summary

The UI subsystem is large (~21.6k lines across 50 files) and, on the whole,
well-engineered: the two conventions called out in the brief hold up. The Hotbar
is genuinely table-driven (`BuildRegistry()` at `Hotbar.cs:689`, no parallel
switch cases in the equip/detect paths), and interact prompts flow through the
single choke point `InteractPromptUI.Show` (94 call sites across 21 files, all via
the static API). Change-detection on per-frame text is applied correctly in most
HUDs (FPSOverlay, CompassHUD badge, Hotbar name plate/count, KillstreakHUD).
Trap #1 (auto-singleton seeding) is satisfied — every MainMenu-skipping
auto-singleton in the folder is seeded in `MainMenuController.EnsureGameplaySingletonsAsync`.

The real problems are concentrated in two places:
1. **Storage/staging panels re-scan the scene every frame while open** (per-slot
   `FindObjectOfType` + `Ship.FindPilotedShip()` in `Update`) — the highest-value fix.
2. **Two latent phone lifecycle bugs** — a lost death-subscription after a
   disable/enable cycle, and an AI chat that isn't torn down on force-close.

Counts: **~14 bugs/correctness issues** (2 High, ~8 Med, rest Low), **~9 dead/duplicated-code items**, **~12 performance/optimization items**.

---

## Bugs (severity, file:line, description, fix)

### High

- **High — `StorageUI.cs:227` + `:383`/`:430-453` (and the twin `FishStagingUI.cs:549-577`)** — `Update()` calls `RefreshAll()` **every frame** while the panel is open; `RefreshAll → PaintSlot → ResolveIcon(id)` calls `FindObjectOfType<WaterBottleController/FishingRodController/GuitarController/AxeController/PistolController>(true)` for any tool slot. With a tool in a hotbar/storage slot this is a full inactive-inclusive scene scan per tool per frame (~up to 27 slots). Direct violation of the "no FindObjectOfType in Update" rule. Fix: cache the tool Sprite per `ItemId` on first lookup (or read from the controllers' singletons), not per paint.

- **High — `StorageUI.cs:194` (and `FishStagingUI.cs:228`)** — the close-guard calls `Ship.FindPilotedShip()` every `Update` tick; that helper does `FindObjectsOfType<Ship>(true)` (allocating array + scan) every frame the UI is open. Fix: use the cached `Ship.PilotedInstance` static (as `Hotbar.cs:165` already does) instead of the scanning helper.

### Medium

- **Med — `PlayerPhoneUI.cs:322-328` (+ `:310-316`)** — `OnDisable` unsubscribes `ResourceManager.OnDeath` but never resets `_subscribedToResourceManager = false`, and `TrySubscribeResourceManager` early-returns while the flag is true. After any OnDisable→OnEnable cycle the `OnDeath += ForceCloseNoAnim` handler is never re-added, so the phone stops auto-closing on death. Fix: clear the flag in `OnDisable` after unsubscribing.

- **Med — `PlayerPhoneUI.cs:346-396` (`ForceCloseNoAnim`) / `:2887` (`ClosePhoneApp`)** — force-close (death / scene load / `OnConversationStarted`) tears down `_activeApp` but never calls `_activeChat.Exit()`. An open AI chat survives parented under the hidden screen, `_activeChat` stays non-null, and `AIChatScreen.IsTypingActive` can stay `true` (which suppresses all phone input at `:1366` and is read by PlayerController). A later `EnterAIChat` builds a second overlapping chat. Fix: `if (_activeChat != null) _activeChat.Exit();` in `ForceCloseNoAnim` (mirror `Close()`).

- **Med — `TabbedPauseMenu.cs:241` → `:203`/`:211`** — `if (_legacyMenu == null) TryAcquireLegacy();` runs in `Update()`; `TryAcquireLegacy` does `FindObjectOfType<SettingsMenu>(true)` + `FindObjectOfType<GalaxyPauseMenuStyler>(true)`. In a scene with no `SettingsMenu`, `_legacyMenu` never resolves, so both scans fire every frame forever. Fix: throttle retries (LightLookAt cadence) or set a "gave up" flag after N attempts.

- **Med — `GalaxyPauseMenuStyler.cs:423,541-542,662`** — `PlayerPrefs.Save()` is called inside `slider.onValueChanged`, so dragging a slider does a synchronous disk flush many times per second. Fix: write the pref in the callback but defer `Save()` to menu-close.

- **Med (conditional) — `GalaxyPauseMenuStyler.cs:30 / :1198 / :1223`** — `TabbedPauseMenu` disables this styler via `enabled = false` (`TabbedPauseMenu.cs:212`), which stops `Update()` but not the already-running `BorderPulse` + up to 12 `StarTwinkle` coroutines; they keep running `Sin`/`Lerp` every frame unless the GameObject is deactivated (hierarchy-dependent). Fix: `StopAllCoroutines()` when disabling, or gate the loops on `enabled`.

- **Med — `CommunityGalleryUI.cs:220-244` (`OpenViewer`)** — stale async-download race: opening photo B while photo A's download is in flight lets A's callback pass the `!_isOpen || !_viewerOpen` guard (both true again) and overwrite `_viewerTexture`/`_viewerImage` with the wrong image. Fix: capture the requested item/id and bail in the callback if it no longer matches the current viewer entry.

- **Med — `HelmetHudConfig.cs:63-67`** — `Awake` sets `Instance = this` with **no** singleton guard (missing `if (Instance != null && Instance != this) {...}`). Two configs in a scene silently clobber `Instance` (last-wins). `OnDestroy` guards correctly. Fix: add the standard guard.

- **Med — `HudIdleSweep.cs:99-141` and `HudBootFX.cs:25-69`** — orphaned-GameObject leak: the `"IdleSweep"`/`"BootSweep"` bar is created early and only `Destroy`ed at the end of the coroutine. If the component is disabled (or `Play` restarts) mid-sweep, Unity stops the coroutine before the `Destroy`, orphaning the child (which then accumulates). Both share the same bug (see Redundancies). Fix: track the bar in a field and destroy it in `OnDisable`/before restart.

### Low

- **Med/Low — `MainMenuController.cs:553`** — `const int Total = 48` but there are **49** `tick(...)` calls in `EnsureGameplaySingletonsAsync`; the loading bar overshoots (49/48 ≈ 102%). The inline comment warns to keep them in sync. Fix: `Total = 49` or drive off an actual counter.

- **Low-Med — `PlayerPhoneUI.cs:296-301` (`OnDestroy`)** — `_phoneCameraRT` (created `:598`) is only `Release()`d on a size-mismatch rebuild, and `_opaqueBlitMat` (`new Material`, `:969`) is never destroyed. Leaks only at app-quit for the live singleton (OS reclaims), but they're unmanaged objects. Fix: release/destroy both in `OnDestroy`.

- **Low — `PlayerPhoneUI.cs:296` / `PhoneAviMjpegWriter.cs:35-43`** — quitting mid-recording leaves a truncated AVI (RIFF/frame-count fields patched only in `Close()`); and the writer ctor has no try/finally, so a throw after `_fs` opens orphans the OS file handle. Fix: make the writer `IDisposable` / dispose `_fs` on ctor failure; call `StopVideoRecording` in `OnDestroy`.

- **Low — `HelmetOverlayHUD.cs:66` vs `:246-249`** — the duplicate-instance `Awake` guard returns before `BuildFrameCanvas`, leaving `_swayRoot`/`_frameCanvas` null; `Destroy(gameObject)` is deferred, so `Update`/`RebuildPieces` can run once and deref `_swayRoot.childCount` → NRE. Unlikely (double-guarded upstream) but unguarded. Fix: null-check `_swayRoot` at top of `Update`, or a `_built` flag.

- **Low — `PhotoGalleryUI.cs:568,578-582`** — `StartCoroutine(CloseModalAfter(...))` isn't tracked in `_uploadRoutine`; a teardown during the 1.2 s delay leaves it running (it then toggles UI). Fix: track and stop it with `_uploadRoutine`.

- **Low (texture leaks, app-lifetime singletons) — `CondensationOverlay.cs:58`, `VisorGlassOverlay.cs:56,59`, `HelmetOverlayHUD.cs:380-402`** — generated `Texture2D`s (fog 512², fresnel 512², scanline, inner-shadow) are cached but have no `OnDestroy` to `Destroy` them. Only leaks across editor domain reloads / at teardown since every owner lives for the app lifetime. Fix (optional): add `OnDestroy` that destroys the generated texture.

---

## Redundancies / Dead Code

- **`ControllerUINavigator.cs:527` `FindHighestCanvasAbove(Canvas)`** — defined, never called anywhere (verified by grep). Dead. (The live migration path uses `FindFirstSelectableInTopmostPanel`.)

- **`MainMenuController.cs:687-985` `EnsureGameplaySingletons_Legacy()`** — ~300 lines, never called (live path is the coroutine `EnsureGameplaySingletonsAsync`). It's also **stale** — missing ~20 singletons the async version seeds, so re-wiring it would silently reintroduce a Trap #1 regression. Author comment says "Delete after testing." Recommend deleting.

- **`PlayerPhoneUI.cs` landscape/rotation machinery** — `_isLandscape` is permanently false (retired per comments at `:112-114`, `:1370-1373`). Dead branches: `ApplyOrientation`/`RotatePhoneRoutine` (`:663-729`), the `_isLandscape` branches in `RefreshCameraSliceUV` (`:625-633`) and `ApplyContentOrientation` (`:921-930`), and `_rotateCoroutine` handling in Open/Close/ForceCloseNoAnim. Compiles but is dead weight.

- **`PlayerPhoneUI.cs:72` `SlideDuration`** — labeled legacy pacing constant; no reference found. Likely removable.

- **`HudIdleSweep.Sweep` (`:99-141`) ≈ `HudBootFX.Run` (`:25-69`)** — near-identical accent-scanline-bar logic (create → sweep → fade → destroy), and both carry the same orphaned-bar interrupt bug. Candidate for one shared helper.

- **`StorageUI.cs` ≈ `FishStagingUI.cs` (whole files)** — large copy-paste (`BuildSlotView`, `AddOutline`, `NewRT`, `Stretch`, `NewText`, `ResolveIcon`, `PaintSlot`, `IsStackable`, bag side-panel, cursor follower, `WireSlotNav`). Already flagged as a Phase-5 refactor in FishStagingUI's header. Any icon/paint fix must be applied in both until extracted.

- **`StorageUI.cs:430-453` / `FishStagingUI.cs:561-580` `ResolveIcon`** — split into two sequential `switch(id)` blocks on the same key; harmless but reads as dead structure. Collapse into one switch.

- **`GalaxyPauseMenuStyler.cs:177,305,427,549,666`** — five ~80-line near-identical `Build*Row` copies (label + slider + fill + handle + value). Should be one parameterized helper. (Whole class is legacy, replaced by TabbedPauseMenu — lower priority.)

- **`GalaxyPauseMenuStyler.cs:1038,1067`** — leftover `Debug.Log` (build-time + per-click SAVE) in shipping UI. `:21-27` instance `styled` guard is a dead no-op (instance field is always default-false at its only `Awake`).

---

## Performance / Optimization

- **`Hotbar.cs:954`** — `canvas.GetComponent<CanvasGroup>().alpha = groupAlpha;` runs every frame in `Refresh()` (called from `Update`). The CanvasGroup is created in `BuildUI` (`:1247`) but never cached — a per-frame `GetComponent`, against the convention. Fix: cache the CanvasGroup in a field at build time.

- **`CompassHUD.cs:146` (via `:284`)** — `AddWaypointByTag` position providers call `GameObject.FindWithTag(sourceTag)` inside the closure, evaluated **every frame per active gameplay waypoint** in `LateUpdate`. Fewer than the FindObjectOfType cases, but still a per-frame `Find` in a loop. Fix: resolve the Transform once (or throttle re-resolution) and cache it in the Waypoint.

- **`InteractGaze.cs:196` (`HasSolidCollider`), `:119` (`TryGetVisualBounds`), `:180` (`AimRayHit`), `:205` (`AimCenter`)** — `GetComponentsInChildren<Collider/Renderer/Graphic>()` allocate an array per call. `IsLookingAt` runs on the current prompt owner every frame (`InteractPromptUI.cs:115`) and on candidates in `Show`. Even the common solid-collider path hits `HasSolidCollider` (line 91→196) → array alloc every frame. Fix: cache the collider/renderer presence per Interactable, or use the non-alloc `GetComponentsInChildren(List<T>)` overloads with a reusable buffer.

- **`PlayerPhoneUI.cs:1620-1624` (`RefreshStatusBar`)** — battery `_batteryFill.anchorMax = new Vector2(pct,1f)` is reassigned every frame (dirtying layout) though `_batteryPct` is fixed at `Awake`; only the text above it is change-detected. Fix: set the fill once after `_batteryPct` is assigned.

- **`PlayerPhoneUI.cs:1330-1332` + `:1610`** — `RefreshStatusBar`/`UpdatePosition`/`UpdateAIUnreadBadge` (and `System.DateTime.Now` at `:1610`) run every frame even while the phone is closed. Fix: early-out the visual refreshes when `!IsOpen && !IsCameraMode`.

- **`StorageUI.cs:418,423,360` / `FishStagingUI.cs:549,554`** — per-frame `count.ToString()` / `weightLbs + " lb"` / cursor `count.ToString()` in `RefreshAll`/`RefreshCursorVisual` (in `Update`) allocate a string per populated slot every frame with no change-detection. Fix: gate assignments behind a cached last-value compare.

- **`StorageUI.cs:434-444` / `FishStagingUI.cs:565-569`** — `Resources.Load<Sprite>(...)` string-keyed lookup per stackable/FishBag slot every frame (via PaintSlot in Update). Fix: cache in statics on first load (as `Hotbar.ResolveFishBagSprite` already does).

- **`HALLineHUD.cs:482-486`** — a live-text tip sets `_label.text = SafeEval(_activeLive)` every frame with no change-detection (the provider also allocates a new string each frame). For a once-per-second countdown that's a per-frame TMP rebuild + alloc. Fix: only assign when the evaluated string changed.

- **`HudIdleSweep.cs:89-94` / `HousingScreenWarp`** — the sweep decay loop calls `SetUniform` every frame, dirtying the warp mesh (`SetVerticesDirty` → full `OnPopulateMesh` rebuild) for the whole 3-5 s decay. Intended animation, but a continuous per-frame mesh rebuild while idle.

- **`GalaxyHudStyler.cs:301-322` (StarTwinkle ×8 per panel + BorderPulse) / `TabbedPauseMenu.cs:1948` / `GalaxyPauseMenuStyler` stars** — decorative `Sin`/`Lerp` color coroutines run every frame even when the panel/canvas is hidden. Individually cheap; collectively wasteful. Could pause when the canvas is disabled.

- **`PlayerPhoneUI.cs` + `PhotoGalleryUI.cs:171-193`** — thumbnails decode synchronously on the main thread (`File.ReadAllBytes` + `Texture2D.LoadImage` per photo) when the app opens; a large photo roll hitches the frame. Fix: decode incrementally across frames.

- **`FPSOverlay.cs:126-133`** — auto-creates with no MainMenu early-return (intentional: dev overlay that should exist everywhere). Noted only so it isn't mistaken for a Trap #1 omission.

---

## Notes & Uncertainties

- **Trap #1 (auto-singleton seeding): PASS.** All MainMenu-skipping auto-singletons in the folder (Hotbar `:573`, InteractPromptUI `:589`, NoteReadUI, CompassHUD, StorageUI, FishStagingUI, PhotoLibrary, PhotoGalleryUI, HelmetOverlayHUD, HALLineHUD, KillstreakHUD, PlayerPhoneUI, TabbedPauseMenu) are seeded in `MainMenuController.EnsureGameplaySingletonsAsync`. The three not seeded are safe: `FPSOverlay` and `ControllerUINavigator` deliberately don't skip MainMenu (self-create everywhere); `InventoryFullPopup` has no `RuntimeInitializeOnLoadMethod` (lazy `Show()` create).

- **Table-driven Hotbar / single-choke InteractPromptUI: PASS.** No parallel switch cases in the Hotbar equip pipeline; the registry (`BuildRegistry` `:689`) is the single source. All 94 prompt call sites go through `InteractPromptUI.Show`/`ShowOneShot`/`Clear`.

- **Leaks verified clean:** `GalleryApiClient` wraps all UnityWebRequests in `using`; `PhotoLibrary.MakeThumbnail` / `GalleryApiClient.EncodeDownscaled` release temp RT + destroy temp Texture2D in `finally`; `PhotoGalleryUI` balances `sceneLoaded` subscribe/unsubscribe (OnEnable/OnDisable); `HudScreenProjector` releases its RT in both resize and `OnDestroy`; `KillstreakHUD`/`FPSOverlay` dispose their subscriptions/recorders in `OnDestroy`; `AIChatScreen.Exit()` self-destructs on the normal phone `Close()` path (the gap is only the force-close path flagged above). `RenderFish` returns a fresh RT per call, so `ReleaseDetailRT`'s Release+Destroy is correct ownership, not a double-free.

- **Severity calibration:** the two "High" storage items are per-frame scans **only while a modal storage/staging panel is open** — not a persistent whole-game cost — but they violate the explicit repo rule and are trivially cacheable, so they're the top fixes. All findings are static-analysis reads; none were reproduced at runtime (no build/test loop available per project setup).

- **Not deeply audited (out of the Update/text/leak lens):** the procedural texture-generation math in the various sprite factories (GalaxyHudKit, ScannerFrame, UIPanelSprites, Hotbar's HotbarHaloGlow/HotbarRoundedRing) — these are one-time cached statics and low-risk; correctness of their bevel/ring math was not verified pixel-by-pixel.
