# Audit: Cross-cutting Build Safety & Singletons

Read-only audit of `Assets/3 - Scripts/` for the four repo-specific build/singleton
traps in CLAUDE.md. No files were modified. Line numbers are as of this pass
(branch `feat/helmet-hud`).

## Summary

- **MainMenu seeding (trap #1):** Every *gameplay* auto-singleton that early-returns
  on the MainMenu scene **is** seeded in `MainMenuController.EnsureGameplaySingletonsAsync`
  (52 gameplay singletons, all accounted for). The **only** `RuntimeInitializeOnLoadMethod`
  + MainMenu-skip class that is NOT seeded is `LightingDebugToolbox` — a dev/debug tool.
  Per trap #1 this makes it invisible in builds; almost certainly intentional, but
  flagged below. `InventoryFullPopup` correctly sidesteps the trap (lazy `Show()`, no
  `RuntimeInitializeOnLoadMethod`). **No build-breaking gap found.**
- **NewGameReset:** No confirmed leak. Every SaveData-mirrored system is reset;
  the not-reset seeded singletons I inspected either self-reset on scene load
  (`KillstreakManager`) or hold no persistent gameplay state (`TutorialPerformanceReview`),
  or are deliberately excluded (AI story phase, per the file's own comment). A few
  story-slice singletons are listed under Uncertainties for a play-test-time check.
- **Singleton pattern:** No violations found. Sampled singletons all use the
  `Awake` Instance-guard + `OnDestroy` clear. (Several use a lowercase `instance`
  backing field, which is why a capital-`Instance = null` grep initially looked like
  it was missing clears — false alarm, they all clear.)
- **Resources.Load:** Four load sites reference assets **outside** the CLAUDE.md
  allowed set (TMP font / `HotbarIcons/` / `Flares/`): two concert material loads,
  one dust material, one killstreak-tier texture, plus a `Techno` HUD font. All are
  intentional but are convention deviations that silently no-op if the `Resources/`
  asset is ever removed.
- **Find\*/Camera.main per frame:** No blatant unconditional per-frame `Find*` /
  `Camera.main` found. The codebase is heavily optimized here — nearly all sites are
  lazy-cached (`if (x == null) x = Find…`), in `Start`/`Awake`/init, editor-only, or
  event-driven. Full categorized list below.

---

## MainMenu singleton seeding gaps (build-breaking)

**Method of check:** cross-referenced every file containing
`if (SceneManager.GetActiveScene().name == "MainMenu") return;` inside a
`RuntimeInitializeOnLoadMethod(AfterSceneLoad)` auto-creator against the seed list in
`MainMenuController.EnsureGameplaySingletonsAsync` (`Assets/3 - Scripts/UI/MainMenuController.cs:551-669`).

### Seeded correctly (no action)
All 52 of these appear in `EnsureGameplaySingletonsAsync`: PlayerWallet, TutorialUI,
WoodInventory, CrystalInventory, BonusTutorial, MapTutorial, Hotbar, StorageUI,
FishStagingUI, AutosaveManager, DimensionDevLoader, TutorialPerformanceReview,
CompassHUD, NoteReadUI, InteractPromptUI, NewspaperReaderUI, MonumentLinkPopupUI,
VitalsHUD, OxygenManager, OxygenHUD, WaterFillHUD, TabbedPauseMenu, CameraEffectsManager,
HelmetOverlayHUD, TrailerFreeCam, TrailerBlackHoleGrow, PixelLightLimitFix, HALLineHUD,
HALVolunteeredLog, HALVoicePlayer, HALCommentator, GForceHUD, FlightAssistStatusHUD,
ShipNameHUD, VelocityMarkersHUD, KillstreakManager, KillstreakHUD, PickupUIManager,
SpaceDustInventory, SpaceDustField, AIMemoryStore, GameKnowledgeBase, AIStoryController,
LLMService, PlayerPhoneUI, DeathCutsceneController, StoryDirector, Mission2Director,
ColdCompanyDirector, HintTrackRunner, PhotoLibrary, PhotoGalleryUI.

### NOT seeded — flagged
- **`LightingDebugToolbox`** — `Assets/3 - Scripts/Scripts/Game/Lighting/LightingDebugToolbox.cs:35-43`.
  `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` + MainMenu early-return, but absent from
  `EnsureGameplaySingletons`. Consequence per trap #1: **the F6–F12 lighting debugger
  never spawns in a build** (only in the Editor, which presses Play directly in the
  gameplay scene). This is a dev tool whose own header says "To REVERT: delete this file",
  so keeping it Editor-only is defensible — but if you ever need it in a build to chase
  the wedge-dimming artifact on the actual target hardware, it must be seeded. **Not a
  gameplay regression; documented here so it isn't mistaken for a bug later.**

### Correctly sidesteps the trap (no action)
- **`InventoryFullPopup`** — `Assets/3 - Scripts/UI/InventoryFullPopup.cs:24-35`. No
  `RuntimeInitializeOnLoadMethod`; auto-creates lazily on first `Show()` and still guards
  MainMenu inside `Show()`. Correct pattern for a "only needed on demand" popup.

### Minor: loading-bar step count drift (cosmetic, not build-breaking)
`EnsureGameplaySingletonsAsync` hard-codes `const int Total = 48;`
(`MainMenuController.cs:553`) but the seed block now creates ~52 singletons (several
share one `tick()` — e.g. NewspaperReaderUI+MonumentLinkPopupUI+VitalsHUD, Mission2Director+
ColdCompanyDirector). If `Total` and the `tick()` count diverge the loading bar over/
undershoots. Purely cosmetic; noted because the inline comment explicitly warns to keep
them in sync.

---

## NewGameReset leaks

`Assets/3 - Scripts/SaveSystem/NewGameReset.cs:57-115` (`Apply()`).

**Reset (verified present):** Hotbar, PlayerWallet, WoodInventory, CrystalInventory,
SpaceDustInventory (+filter flag), FishInventory, ResourceManager (vitals + deaths),
OxygenManager, EarlyGameProgress, `PlayerPhoneUI.HasEverOpened`, NoteCollection,
BuildMenuLock, StoryDirector, AIMemoryStore, HALVolunteeredLog, HALCommentator, NameStore
(3 statics), CompassHUD, MapTutorial, BonusTutorial, and a forced Autosave.

**Seeded-but-not-reset — verified safe:**
- `KillstreakManager` — holds `CurrentStreak`, but self-resets on `sceneLoaded` and on
  player death (`Assets/3 - Scripts/Combat/KillstreakManager.cs:20, 82`). New Game triggers
  a scene load, so the streak resets. No leak.
- `TutorialPerformanceReview` — transient report-card UI state only
  (`_activeThresholds`, `_onContinue`); no cross-session gameplay progress. No leak.

**Deliberately not reset (documented in the file header, not a bug):**
`AIStoryController` / `GameKnowledgeBase` story phase — intentionally-persistent knowledge
merge (`NewGameReset.cs:24-25`).

No confirmed leaks. See Uncertainties for the story-slice singletons I did not fully trace.

---

## Singleton pattern violations

None found. Sampled auto-singletons all implement the canonical pattern
(`Awake` Instance-guard + `OnDestroy` clear):
- `KillstreakManager.cs:68-77` — guard + clear.
- `Hotbar.cs:127-140` — guard + clear (lowercase `instance`).
- `StorageUI.cs:97,105` — `Awake` + `OnDestroy() { if (instance == this) instance = null; }`.
- `FishStagingUI.cs:95,103` — same.
- `LightingDebugToolbox.cs:45-55` — guard + clear.

Note on methodology: a case-sensitive `Instance = null` grep initially "missed"
Hotbar/StorageUI/FishStagingUI because they clear a lowercase `instance` field. All three
do clear it. No singleton was found lacking the `OnDestroy` clear.

---

## Illegal Resources.Load usage

CLAUDE.md allowed set: the TMP font, `Resources/HotbarIcons/`, `Resources/Flares/`.

**Within allowance (no action):** all `Resources.Load<TMP_FontAsset>("Fonts & Materials/…")`
and `Resources.Load<Sprite>("HotbarIcons/…")` sites (MainMenuController, HudFontResolver,
Hotbar, StorageUI, FishStagingUI, AutosaveManager, SaveLoadUI, NotePickup, MapVelocityHud,
GalaxyHudStyler, GalaxyPauseMenuStyler, StoryImpactNotice, TutorialPerformanceReview,
BonusTutorial). These load TMP fonts or hotbar icons.

**Outside the documented allowed set — flagged (all intentional, but convention deviations
that no-op silently if the `Resources/` asset is removed):**
- `Assets/3 - Scripts/Concert/ConcertBeamShared.cs:132` — `Resources.Load<Material>("ConcertAdditiveMaterial")`
- `Assets/3 - Scripts/Concert/ConcertLaser.cs:759` — `Resources.Load<Material>("ConcertAdditiveMaterial")`
- `Assets/3 - Scripts/World/SpaceDustField.cs:574` — `Resources.Load<Material>("SpaceDust")`
- `Assets/3 - Scripts/UI/KillstreakHUD.cs:268` — `Resources.Load<Texture2D>("Killstreak/tier_" + t)`
- `Assets/3 - Scripts/UI/HudFontResolver.cs:23-26` — `Resources.Load<TMP_FontAsset>("Techno SDF")` /
  `Resources.Load<Font>("Techno")` (a non-"Fonts & Materials" font path; falls back to the
  allowed Liberation/Courier SDFs at lines 29-31).

Recommendation: none require a fix if the corresponding `Resources/` assets exist and are
committed. Worth a one-time confirm that `ConcertAdditiveMaterial`, `SpaceDust`, and
`Killstreak/tier_*` actually live under a `Resources/` folder (silent null in a build
otherwise — the classic MainMenu-adjacent "works in Editor" failure mode).

---

## Find*/Camera.main in per-frame methods (consolidated list, file:line)

Full grep of `FindObjectOfType` / `FindObjectsOfType` / `Camera.main` / `GameObject.Find*`
across `Assets/3 - Scripts/` returned ~250 live sites. **No unconditional per-frame
`Find*`/`Camera.main` was confirmed.** They fall into these categories, all acceptable per
CLAUDE.md ("Cache once, lazy-refind only if null"):

### (a) Lazy-cached — acceptable (`if (x == null) x = Find…`, may sit in Update but only runs until found)
Representative sites: `AI/HALCommentator.cs:418,564,654,786`; `AI/TokenResolver.cs:74`;
`Camera/CameraFOVFX.cs:109`; `Camera/CameraTransformFX.cs:165`; `Combat/EnemyController.cs:299`;
`Camera/SpeedLinesOverlay.cs:252`; `Camera/RadialMotionBlurEffect.cs:101`;
`Ship/GForceHUD.cs:146,241,283,292`; `Ship/VelocityMarkersHUD.cs:105`;
`Ship/FlightAssistStatusHUD.cs:155`; `Survival/VitalsHUD.cs:100,112`;
`Survival/WaterFillHUD.cs:85`; `Survival/OxygenManager.cs:508,509`;
`Survival/ResourceManager.cs:110`; `World/WoodPopup.cs:68`; `World/CrystalPopup.cs:68`;
`World/SpaceDustField.cs:212`; `Ship/DustPopup.cs:85`; `Ship/ReactorPopup.cs:42`;
`Dimensions/*Controller.cs` (all `if (_player == null) _player = FindObjectOfType<PlayerController>()`
— CongregationController:329, FrozenSeaController:452, HallOfDoorsController:297,
NeonGridController:182, LongDarkController:361, OrchardController:391, ProcessionController:355,
RedForestController:384, RustSeaController:433, SliverTileSet:136, StaticFieldController:328,
WaitingFieldController:203, WaitingRoomController:498, WellFieldController:278);
`Concert/AudienceMember.cs:383`; `Concert/ConcertStageHub.cs:246,251`;
`Physics/BlackHoleCapture.cs:65,168`; `Ship/BoostMeterUI.cs:57`;
`Ship/LebronLight.cs:235`; `Tutorial/IntroSequenceController.cs:308,321,424`.

### (b) Throttled / fallback-only (Camera.main only when cached player is null; runs in Stream/periodic loops, not every frame)
`World/TreeSpawner.cs:159` (GetViewerPosition fallback), `World/CrystalSpawner.cs:185`,
`World/MushroomSpawner.cs:177`, `World/AlienNPCSpawner.cs:202`, `World/GrassSpawner.cs:221`,
`World/InstancedGrassRenderer.cs:789`, `Camera/LightLookAt.cs:27` (explicit 1 s retry throttle),
`Camera/LensFlareRegistry.cs:197`, `Camera/SpeedLinesOverlay.cs:111-112`.

### (c) Event-driven / one-shot (not per-frame) — spot-verified
- `Fishing/FishingRodController.cs:564` (`Camera.main`) + `:591` (`FindObjectOfType<EndlessManager>`)
  — both inside `SpawnBobber()`, called once per cast, not per frame. **Verified.**
- `Fishing/Bobber.cs:59` (Awake), `:126,139` (inside `StopOnWater`, on water-trigger). `Update()`
  at :64 contains no `Find`. **Verified.**
- `Cutscenes/DeathCutsceneController.cs:501,535` (once per death), `Combat/AlienNPCDamageable.cs:111`
  (on hit), pickup/vendor/NPC `FindObjectOfType` sites (Start/init/interaction), and all
  `SaveSystem/SaveCollector.cs:*` sites (capture/apply, one-shot).

### (d) Editor-only (`Assets/3 - Scripts/Editor/**`) — never in a build
`HotbarIconRenderer`, `LensFlareProbe`, `TrailerPPProbe`, `TrailerDiag`, `PlanetBakeTool`,
`PlanetPreviewTool`, `PlayModeDiagnostic`, `TrailerCaptureShot`, `CreateCutsceneScene`,
`BloodFXWire`, `VillageExclusionTool`, `TrailerTimelineSetup`, `TrailerAimAttach`. Not relevant.

---

## Notes & Uncertainties

- **Coverage honesty:** for the per-frame section I read the complete grep (400 lines) and
  spot-opened the unconditional-looking sites (FishingRodController.SpawnBobber, Bobber,
  TreeSpawner.GetViewerPosition) to confirm they are event-driven / fallback-only. I did not
  open the enclosing method of every one of the ~250 cached-lazy sites; the `if (x == null)`
  guard is visible on-line for the ones listed under (a), which is sufficient to classify them.
- **NewGameReset — verify at play-test:** these seeded story-slice singletons are NOT
  explicitly reset in `Apply()` and I did not fully trace their internal state:
  `Mission2Director`, `ColdCompanyDirector`, `HintTrackRunner`, `PhotoLibrary`.
  `StoryDirector.ResetForNewGame()` IS called and the mission directors largely bridge off
  StoryDirector, so a leak is unlikely; `PhotoLibrary` is disk-backed and *should* persist
  across games intentionally. Worth a New-Game → check-story-state pass once the story slice
  is play-tested. No code change recommended blind.
- **Resources.Load confirm:** recommend confirming `ConcertAdditiveMaterial`, `SpaceDust`
  material, and `Killstreak/tier_*` textures physically reside under a `Resources/` folder and
  are committed — a missing `Resources/` asset is a silent build-only null (same failure class
  as trap #1).
- **Forbidden zones respected:** no files under `Celestial/`, `Atmosphere.cs`,
  `Post Processing/Planet Effects/`, or any shader were modified or needed inspection beyond
  read-only.
